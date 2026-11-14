using System;
using System.Collections.Generic;
using Trainer.Simulation;

namespace Trainer.Application
{
    /// <summary>
    /// Authoritative mission session. Exactly one instance per shared session, owned by the
    /// authority (host/server). It is the only code that advances the injected IPowerSimulation.
    ///
    /// Single-threaded: Submit and Tick must be called on the same thread. Callbacks are synchronous;
    /// state and deduplication are committed before any event is raised, so subscribers may submit
    /// further commands. Subscriber exceptions propagate.
    ///
    /// No permission check: ISessionPort carries no sender identity. A network adapter must
    /// authenticate the sender and apply the team's permission policy before calling Submit.
    /// </summary>
    public sealed class MissionSession : ISessionPort
    {
        public const double DefaultMaxStepSeconds = 1.0;

        private readonly IPowerSimulation simulation;
        private readonly PowerConfiguration configuration;
        private readonly double maxStepSeconds;
        private readonly Dictionary<Guid, SessionCommand> commands = new Dictionary<Guid, SessionCommand>();
        private readonly Dictionary<Guid, CommandResult> results = new Dictionary<Guid, CommandResult>();
        private MissionSnapshot snapshot;

        public event Action<MissionSnapshot> SnapshotChanged;
        public event Action<CommandResult> CommandCompleted;

        public MissionRules Rules { get; }

        /// <summary>Authority-side detail of why the mission ended (not part of MissionSnapshot).</summary>
        public MissionOutcome Outcome { get; private set; } = MissionOutcome.InProgress;

        /// <param name="simulation">Any IPowerSimulation implementation; this session calls Reset on it.</param>
        /// <param name="maxStepSeconds">Longest simulated sub-step per Advance call. Shorter steps reduce
        /// the error of evaluating served work from instantaneous snapshots.</param>
        public MissionSession(IPowerSimulation simulation, PowerConfiguration configuration, MissionRules rules,
            double maxStepSeconds = DefaultMaxStepSeconds)
        {
            this.simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            if (double.IsNaN(maxStepSeconds) || double.IsInfinity(maxStepSeconds) || maxStepSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxStepSeconds));
            this.maxStepSeconds = maxStepSeconds;
            snapshot = new MissionSnapshot(0, 0, SessionState.Ready, ResetSimulation(), 0, false);
        }

        public bool TryGetSnapshot(out MissionSnapshot value)
        {
            value = snapshot;
            return true;
        }

        public void Submit(SessionCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (commands.TryGetValue(command.Id, out SessionCommand previous))
            {
                bool same = previous.Epoch == command.Epoch && previous.Action == command.Action
                    && previous.RequestedScienceEnabled == command.RequestedScienceEnabled;
                // Replay of an exact command reproduces its stored result without reapplying it.
                CommandCompleted?.Invoke(same ? results[command.Id]
                    : new CommandResult(command.Id, command.Epoch, false, "CommandIdConflict"));
                return;
            }

            string rejection = Validate(command);
            MissionSnapshot next = snapshot;
            if (rejection == null) next = Apply(command);

            var result = new CommandResult(command.Id, command.Epoch, rejection == null, rejection ?? "Accepted");
            commands.Add(command.Id, command);
            results.Add(command.Id, result);
            snapshot = next;
            try
            {
                if (result.Accepted) SnapshotChanged?.Invoke(next);
            }
            finally { CommandCompleted?.Invoke(result); }
        }

        /// <summary>
        /// Advances the mission by simulated seconds. Only the authority's runner calls this.
        /// Does nothing unless Running. Publishes at most one snapshot per call.
        /// </summary>
        public void Tick(double elapsedSimulatedSeconds)
        {
            if (double.IsNaN(elapsedSimulatedSeconds) || double.IsInfinity(elapsedSimulatedSeconds)
                || elapsedSimulatedSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedSimulatedSeconds));
            if (elapsedSimulatedSeconds == 0 || snapshot.State != SessionState.Running) return;

            bool science = snapshot.ScienceEnabled;
            double work = snapshot.ScienceWorkSeconds;
            double remaining = elapsedSimulatedSeconds;
            MissionOutcome outcome = MissionOutcome.InProgress;
            PowerSnapshot after = snapshot.Power;

            while (remaining > 0 && outcome == MissionOutcome.InProgress)
            {
                PowerSnapshot before = RequireSnapshot();
                double step = Math.Min(remaining, maxStepSeconds);
                // Do not run past the mission duration inside a single step.
                double timeLeft = Rules.MissionDurationSeconds - before.SimulatedTimeSeconds;
                if (timeLeft > 0) step = Math.Min(step, timeLeft);

                simulation.Advance(step, science);
                after = RequireSnapshot();
                work += MissionOutcomeEvaluator.ServedScienceSeconds(before, after, science);
                remaining -= step;
                outcome = MissionOutcomeEvaluator.Evaluate(Rules, after, work);
            }

            Outcome = outcome;
            snapshot = new MissionSnapshot(snapshot.Epoch, checked(snapshot.Revision + 1),
                MissionOutcomeEvaluator.ToSessionState(outcome), after, work, science);
            SnapshotChanged?.Invoke(snapshot);
        }

        private string Validate(SessionCommand command)
        {
            if (command.Id == Guid.Empty) return "InvalidCommandId";
            if (command.Epoch != snapshot.Epoch) return "EpochMismatch";
            if (!Enum.IsDefined(typeof(SessionAction), command.Action)) return "UnknownAction";
            if ((command.Action == SessionAction.SetScienceEnabled) != command.RequestedScienceEnabled.HasValue)
                return "InvalidActionValue";
            if (command.Action == SessionAction.Restart) return null;
            if (snapshot.State == SessionState.Completed || snapshot.State == SessionState.Interrupted)
                return "SessionEnded";
            if (command.Action == SessionAction.Pause && snapshot.State != SessionState.Running)
                return "InvalidState";
            if (command.Action == SessionAction.Resume && snapshot.State != SessionState.Ready
                && snapshot.State != SessionState.Paused) return "InvalidState";
            return null;
        }

        private MissionSnapshot Apply(SessionCommand command)
        {
            switch (command.Action)
            {
                case SessionAction.Restart:
                    long epoch = checked(snapshot.Epoch + 1);
                    PowerSnapshot power = ResetSimulation();
                    Outcome = MissionOutcome.InProgress;
                    return new MissionSnapshot(epoch, 0, SessionState.Ready, power, 0, false);
                case SessionAction.Pause:
                    return With(SessionState.Paused, snapshot.ScienceEnabled);
                case SessionAction.Resume:
                    return With(SessionState.Running, snapshot.ScienceEnabled);
                case SessionAction.SetScienceEnabled:
                    return With(snapshot.State, command.RequestedScienceEnabled.Value);
                default:
                    throw new InvalidOperationException("Unvalidated action.");
            }
        }

        private MissionSnapshot With(SessionState state, bool science) =>
            new MissionSnapshot(snapshot.Epoch, checked(snapshot.Revision + 1), state,
                snapshot.Power, snapshot.ScienceWorkSeconds, science);

        private PowerSnapshot ResetSimulation()
        {
            simulation.Reset(configuration);
            return RequireSnapshot();
        }

        private PowerSnapshot RequireSnapshot() =>
            simulation.Snapshot ?? throw new InvalidOperationException("IPowerSimulation returned a null snapshot.");
    }
}
