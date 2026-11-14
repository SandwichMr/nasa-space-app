using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
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
        /// <summary>Shortest step used when trimming to the remaining science work (bounds overshoot).</summary>
        public const double MinimumWorkStepSeconds = 1e-3;

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
        /// <param name="maxStepSeconds">Longest simulated sub-step per Advance call. Integrated
        /// simulations additionally stop at dispatch events; legacy implementations use endpoint estimates.</param>
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
            Exception fault = null;
            if (rejection == null)
            {
                try { next = Apply(command); }
                catch (Exception e)
                {
                    // Only Restart touches the simulation. Record the command so a replay does not reset
                    // again, and end the session so nothing advances a simulation in an unknown state.
                    fault = e;
                    rejection = "SimulationFault";
                    Outcome = MissionOutcome.SimulationFault;
                    next = With(SessionState.Interrupted, snapshot.ScienceEnabled);
                }
            }

            var result = new CommandResult(command.Id, command.Epoch, rejection == null, rejection ?? "Accepted");
            commands.Add(command.Id, command);
            results.Add(command.Id, result);
            bool changed = next != snapshot;
            snapshot = next;
            try
            {
                if (changed) SnapshotChanged?.Invoke(next);
            }
            finally { CommandCompleted?.Invoke(result); }
            if (fault != null) ExceptionDispatchInfo.Capture(fault).Throw();
        }

        /// <summary>
        /// Advances the mission by simulated seconds. Only the authority's runner calls this.
        /// Does nothing unless Running. Publishes at most one snapshot per call.
        /// If the simulation throws, progress from completed sub-steps is kept, the session becomes
        /// Interrupted (Outcome = SimulationFault) and the exception is rethrown.
        /// If the simulation stops advancing its clock, the rest of this call is skipped.
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
            PowerSnapshot last = snapshot.Power;
            bool progressed = false;

            try
            {
                while (remaining > 0 && outcome == MissionOutcome.InProgress)
                {
                    PowerSnapshot before = RequireSnapshot();
                    double step = NextStep(remaining, before.SimulatedTimeSeconds, science, work);
                    var integrated = simulation as IIntegratedPowerSimulation;
                    if (integrated != null)
                    {
                        double boundary = integrated.SecondsUntilServiceChange(science);
                        if (boundary == 0d)
                        {
                            outcome = MissionOutcomeEvaluator.Evaluate(Rules, before, work);
                            if (outcome != MissionOutcome.InProgress)
                            {
                                last = before;
                                progressed = true;
                                break;
                            }
                        }
                        else step = Math.Min(step, boundary);
                    }
                    simulation.Advance(step, science);
                    PowerSnapshot after = RequireSnapshot();
                    // A simulation whose clock does not move would otherwise loop for every remaining step.
                    if (!(after.SimulatedTimeSeconds > before.SimulatedTimeSeconds)
                        && !(integrated != null && after.EnergyJoules != before.EnergyJoules)) break;
                    // A very short boundary segment can change energy while its duration rounds
                    // away in the clock. Commit that event, then continue with the new dispatch.

                    work += integrated != null ? integrated.LastFullyServedScienceSeconds
                        : MissionOutcomeEvaluator.ServedScienceSeconds(before, after, science);
                    remaining -= step;
                    outcome = MissionOutcomeEvaluator.Evaluate(Rules, after, work);
                    last = after;
                    progressed = true;
                }
            }
            catch
            {
                Outcome = MissionOutcome.SimulationFault;
                snapshot = new MissionSnapshot(snapshot.Epoch, checked(snapshot.Revision + 1),
                    SessionState.Interrupted, last, work, science);
                try { SnapshotChanged?.Invoke(snapshot); }
                catch { /* the simulation failure is the error reported to the caller */ }
                throw;
            }

            if (!progressed) return;
            Outcome = outcome;
            snapshot = new MissionSnapshot(snapshot.Epoch, checked(snapshot.Revision + 1),
                MissionOutcomeEvaluator.ToSessionState(outcome), last, work, science);
            SnapshotChanged?.Invoke(snapshot);
        }

        /// <summary>Sub-step length: at most maxStepSeconds, never past the mission duration, and trimmed
        /// to science work still needed. Only legacy simulations use MinimumWorkStepSeconds.</summary>
        private double NextStep(double remaining, double simulatedTime, bool science, double work)
        {
            double step = Math.Min(remaining, maxStepSeconds);
            double timeLeft = Rules.MissionDurationSeconds - simulatedTime;
            if (timeLeft > 0) step = Math.Min(step, timeLeft);
            if (science)
                step = Math.Min(step, Math.Max(Rules.ScienceWorkTargetSeconds - work,
                    simulation is IIntegratedPowerSimulation ? double.Epsilon : MinimumWorkStepSeconds));
            return step;
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
