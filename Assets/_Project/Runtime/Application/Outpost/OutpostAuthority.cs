using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Application.Outpost
{
    /// <summary>
    /// The single phase 2 session authority (D2). Owns the only <see cref="IOutpostSimulation"/> (D1) and the
    /// only <see cref="ICrewRoster"/> (D3) of the session and is their only caller. It alone decides task
    /// validation, reservations, progress, completion and the mission result; it never adds or removes stock
    /// (the simulation does, in Advance and Apply) and never moves crew (the roster does).
    ///
    /// Single-threaded (Unity main thread). State and the command record are committed before any event is
    /// raised, so subscribers may call back in. Subscriber exceptions propagate. Only the authority's runner
    /// calls <see cref="Tick"/>; positions are never read from commands: proximity uses the roster's confirmed
    /// poses.
    ///
    /// One authority step (see <see cref="Tick"/>): crew moves along held directions, tasks whose performer's
    /// confirmed position left the station radius end (PerformerMovedAway), the simulation advances with the
    /// demand of the remaining tasks, each task is credited with its fully served seconds, finished tasks call
    /// Apply once, and the mission end conditions are evaluated.
    /// </summary>
    public sealed class OutpostAuthority : IOutpostAuthority
    {
        public const double DefaultMaxStepSeconds = 0.25;
        /// <summary>Shortest step used when trimming to the remaining work of a task (bounds tiny steps).</summary>
        public const double MinimumWorkStepSeconds = 1e-3;
        /// <summary>Relative tolerance when deciding that a task was not fully served in a step.</summary>
        public const double ServiceTolerance = 1e-9;

        private sealed class StoredCommand
        {
            public PlayerId Sender;
            public OutpostCommand Command;
            public CommandResult Result;
        }

        private readonly IOutpostSimulation simulation;
        private readonly ICrewRoster roster;
        private readonly double maxStepSeconds;
        private readonly OutpostTaskBoard board;
        private readonly int[] completed;
        private readonly Dictionary<Guid, StoredCommand> commands = new Dictionary<Guid, StoredCommand>();

        private long epoch;
        private long revision;
        private SessionState state = SessionState.Ready;
        private OutpostOutcome outcome = OutpostOutcome.InProgress;
        private OutpostSessionSnapshot snapshot;

        public event Action<OutpostSessionSnapshot> SnapshotChanged;
        public event Action<PlayerId, CommandResult> CommandCompleted;

        public OutpostScenario Scenario { get; }
        public OutpostMissionRules Rules { get; }
        public OutpostOutcome Outcome => outcome;

        /// <param name="simulation">D1 implementation; this authority calls Reset on it.</param>
        /// <param name="roster">D3 implementation built for <paramref name="scenario"/>'s layout and movement
        /// settings; it must be empty (nobody joined yet).</param>
        public OutpostAuthority(OutpostScenario scenario, OutpostMissionRules rules, IOutpostSimulation simulation,
            ICrewRoster roster, double maxStepSeconds = DefaultMaxStepSeconds)
        {
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            this.roster = roster ?? throw new ArgumentNullException(nameof(roster));
            if (double.IsNaN(maxStepSeconds) || double.IsInfinity(maxStepSeconds) || maxStepSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxStepSeconds));
            string problem = rules.ProblemWith(scenario);
            if (problem != null) throw new ArgumentException(problem, nameof(rules));
            if (roster.Members == null || roster.Members.Count != 0)
                throw new ArgumentException("The roster must start empty.", nameof(roster));
            this.maxStepSeconds = maxStepSeconds;
            board = new OutpostTaskBoard(scenario.Layout);
            completed = new int[rules.Objectives.Count];
            simulation.Reset(scenario.Simulation);
            snapshot = Build(RequireResources());
        }

        public bool TryGetSnapshot(out OutpostSessionSnapshot value)
        {
            value = snapshot;
            return true;
        }

        // ---------------------------------------------------------------- presence and movement

        public OutpostMembership Join()
        {
            OutpostMembership membership = roster.Join()
                ?? throw new InvalidOperationException("ICrewRoster.Join returned null.");
            if (membership.IsJoined)
            {
                Commit(RequireResources());
                SnapshotChanged?.Invoke(snapshot);
            }
            return membership;
        }

        public bool Leave(PlayerId player)
        {
            bool hadTask = false;
            if (board.TryGetForPlayer(player, out OutpostTaskBoard.ActiveTask task))
            {
                board.End(task.Station, TaskEndReason.PerformerLeft, snapshot.SimulatedTimeSeconds);
                hadTask = true;
            }
            bool left = roster.Leave(player);
            if (!left && !hadTask) return false;
            Commit(snapshot.Resources);
            SnapshotChanged?.Invoke(snapshot);
            return left;
        }

        /// <summary>Records the held direction through the roster. Publishes nothing by itself: positions change
        /// only in <see cref="Tick"/>, and the next published snapshot carries the new LastInputSequence.</summary>
        public MovementInputDisposition SubmitMovement(PlayerId sender, MovementInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return roster.SubmitMovement(sender, input, epoch);
        }

        // ---------------------------------------------------------------- commands

        public void Submit(PlayerId sender, OutpostCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (command.Id == Guid.Empty)
            {
                CommandCompleted?.Invoke(sender, Reject(command, OutpostReasons.InvalidCommandId));
                return;
            }
            if (commands.TryGetValue(command.Id, out StoredCommand stored))
            {
                // Exact replay from the same sender reproduces the stored result without reapplying it.
                bool same = stored.Sender == sender && stored.Command.HasSameContent(command);
                CommandCompleted?.Invoke(sender, same ? stored.Result : Reject(command, OutpostReasons.CommandIdConflict));
                return;
            }

            string rejection = Validate(sender, command);
            bool changed = false;
            Exception fault = null;
            if (rejection == null)
            {
                try
                {
                    Apply(sender, command);
                }
                catch (Exception e)
                {
                    // Only Restart touches the simulation here. End the session so nothing advances a model in an
                    // unknown state; the command is recorded so a replay does not reset again.
                    fault = e;
                    rejection = OutpostReasons.SimulationFault;
                    EnterFault(snapshot.Resources);
                }
                changed = true;
            }

            var result = new CommandResult(command.Id, command.Epoch, rejection == null, rejection ?? OutpostReasons.Accepted);
            commands.Add(command.Id, new StoredCommand { Sender = sender, Command = command, Result = result });
            try
            {
                if (changed) SnapshotChanged?.Invoke(snapshot);
            }
            finally
            {
                CommandCompleted?.Invoke(sender, result);
            }
            if (fault != null) ExceptionDispatchInfo.Capture(fault).Throw();
        }

        private static CommandResult Reject(OutpostCommand command, string reason) =>
            new CommandResult(command.Id, command.Epoch, false, reason);

        /// <summary>Validation in the order fixed by SESSION_CONTRACT.md; the first failure is the reason.</summary>
        private string Validate(PlayerId sender, OutpostCommand command)
        {
            if (!TryGetMember(sender, out CrewMemberState member)) return OutpostReasons.NotJoined;
            if (command.Epoch != epoch) return OutpostReasons.EpochMismatch;
            string shape = command.ShapeError();
            if (shape != null) return shape;

            bool ended = state == SessionState.Completed || state == SessionState.Interrupted;
            switch (command.Action)
            {
                case OutpostAction.Restart:
                    return null;
                case OutpostAction.Pause:
                    if (ended) return OutpostReasons.SessionEnded;
                    return state == SessionState.Running ? null : OutpostReasons.InvalidState;
                case OutpostAction.Resume:
                    if (ended) return OutpostReasons.SessionEnded;
                    return state == SessionState.Ready || state == SessionState.Paused ? null : OutpostReasons.InvalidState;
                case OutpostAction.StartTask:
                    return ValidateStart(sender, member, command);
                case OutpostAction.CancelTask:
                    if (ended) return OutpostReasons.SessionEnded;
                    if (state != SessionState.Running && state != SessionState.Paused) return OutpostReasons.InvalidState;
                    if (!Scenario.Layout.TryGetStation(command.Station, out _)) return OutpostReasons.UnknownStation;
                    if (!board.TryGetAtStation(command.Station, out OutpostTaskBoard.ActiveTask active) || active.Performer != sender)
                        return OutpostReasons.NoActiveTask;
                    return null;
                default:
                    return OutpostReasons.UnknownAction;
            }
        }

        private string ValidateStart(PlayerId sender, CrewMemberState member, OutpostCommand command)
        {
            if (state == SessionState.Completed || state == SessionState.Interrupted) return OutpostReasons.SessionEnded;
            if (state != SessionState.Running) return OutpostReasons.InvalidState;
            if (!Scenario.Layout.TryGetStation(command.Station, out StationPlacement placement)) return OutpostReasons.UnknownStation;
            if (!Scenario.TryGetTask(command.Task, out TaskDefinition definition)) return OutpostReasons.UnknownTaskType;
            if (definition.Station != command.Station) return OutpostReasons.TaskNotOfferedAtStation;
            if (!IsStationAvailable(command.Station)) return OutpostReasons.StationUnavailable;
            if (board.TryGetForPlayer(sender, out _)) return OutpostReasons.PlayerBusy;
            if (board.TryGetAtStation(command.Station, out _)) return OutpostReasons.StationOccupied;
            // Confirmed position from the roster, never a coordinate from the command.
            if (!placement.IsWithinReach(member.Pose.Position)) return OutpostReasons.OutOfRange;
            // Current model ledger (not the last published copy), as the contract's pre-check requires.
            if (!OutpostMissionEvaluator.CanApply(RequireResources().LifeSupport, definition.CompletionEffect))
                return OutpostReasons.InsufficientResources;
            return null;
        }

        /// <summary>No scenario rule of the interior mission blocks a station; kept as the single place to add one.</summary>
        private static bool IsStationAvailable(StationId station) => true;

        private void Apply(PlayerId sender, OutpostCommand command)
        {
            switch (command.Action)
            {
                case OutpostAction.Restart:
                    Restart();
                    return;
                case OutpostAction.Pause:
                    state = SessionState.Paused;
                    break;
                case OutpostAction.Resume:
                    state = SessionState.Running;
                    break;
                case OutpostAction.StartTask:
                    Scenario.TryGetTask(command.Task, out TaskDefinition definition);
                    board.Reserve(command.Station, definition, sender, command.Id);
                    break;
                case OutpostAction.CancelTask:
                    board.End(command.Station, TaskEndReason.CancelledByPerformer, snapshot.SimulatedTimeSeconds);
                    break;
                default:
                    throw new InvalidOperationException("Unvalidated action.");
            }
            Commit(snapshot.Resources);
        }

        /// <summary>New epoch at revision 0: model reset, tasks and last ends erased without effects, objectives
        /// back to zero, crew back to spawn points (ids, slots and sequences kept by the roster).</summary>
        private void Restart()
        {
            long next = checked(epoch + 1);
            simulation.Reset(Scenario.Simulation);   // may throw: nothing else has changed yet
            OutpostSimulationSnapshot resources = RequireResources();
            board.Clear();
            Array.Clear(completed, 0, completed.Length);
            roster.ResetForRestart();
            epoch = next;
            revision = 0;
            state = SessionState.Ready;
            outcome = OutpostOutcome.InProgress;
            snapshot = Build(resources);
        }

        // ---------------------------------------------------------------- clock

        /// <summary>
        /// Advances the mission by simulated seconds (finite, nonnegative). Only the authority's runner calls it.
        /// Does nothing unless Running; publishes at most one snapshot per call. If the simulation or the roster
        /// throws, progress of completed sub-steps is kept, the mission becomes Interrupted (SimulationFault),
        /// active tasks end with MissionEnded and the exception is rethrown.
        /// </summary>
        public void Tick(double elapsedSimulatedSeconds)
        {
            if (double.IsNaN(elapsedSimulatedSeconds) || double.IsInfinity(elapsedSimulatedSeconds) || elapsedSimulatedSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedSimulatedSeconds));
            if (elapsedSimulatedSeconds == 0 || state != SessionState.Running) return;

            double remaining = elapsedSimulatedSeconds;
            OutpostOutcome result = OutpostOutcome.InProgress;
            OutpostSimulationSnapshot last = snapshot.Resources;
            bool progressed = false;
            try
            {
                while (remaining > 0 && result == OutpostOutcome.InProgress)
                {
                    OutpostSimulationSnapshot before = RequireResources();
                    double step = NextStep(remaining, before.Power.SimulatedTimeSeconds);

                    double boundary = simulation.SecondsUntilServiceChange(Demand());
                    if (boundary == 0d)
                    {
                        result = OutpostMissionEvaluator.Evaluate(Rules, before.Power, completed);
                        if (result != OutpostOutcome.InProgress)
                        {
                            last = before;
                            progressed = true;
                            break;
                        }
                    }
                    else if (boundary > 0d) step = Math.Min(step, boundary);

                    roster.Advance(step);
                    progressed = true;
                    EndTasksOutOfReach(before.Power.SimulatedTimeSeconds);

                    simulation.Advance(step, Demand());
                    OutpostSimulationSnapshot after = RequireResources();
                    last = after;
                    double elapsed = after.Power.SimulatedTimeSeconds - before.Power.SimulatedTimeSeconds;
                    // A model whose clock does not move would otherwise loop for every remaining step.
                    if (!(elapsed > 0)) break;

                    CreditWork(elapsed, after.Power.SimulatedTimeSeconds);
                    remaining -= step;
                    result = OutpostMissionEvaluator.Evaluate(Rules, after.Power, completed);
                }
            }
            catch
            {
                EnterFault(last);
                try { SnapshotChanged?.Invoke(snapshot); }
                catch { /* the simulation failure is the error reported to the caller */ }
                throw;
            }

            if (!progressed) return;
            if (result != OutpostOutcome.InProgress)
            {
                outcome = result;
                state = OutpostMissionEvaluator.ToSessionState(result);
                board.EndAll(TaskEndReason.MissionEnded, last.Power.SimulatedTimeSeconds);
            }
            Commit(last);
            SnapshotChanged?.Invoke(snapshot);
        }

        /// <summary>At most maxStepSeconds, never past the mission duration, and trimmed to the work still needed
        /// by any active task (not below <see cref="MinimumWorkStepSeconds"/>).</summary>
        private double NextStep(double remaining, double simulatedTime)
        {
            double step = Math.Min(remaining, maxStepSeconds);
            double timeLeft = Rules.MissionDurationSeconds - simulatedTime;
            if (timeLeft > 0) step = Math.Min(step, timeLeft);
            foreach (OutpostTaskBoard.ActiveTask task in board.Active())
                step = Math.Min(step, Math.Max(task.RemainingSeconds, MinimumWorkStepSeconds));
            return step;
        }

        /// <summary>Demand of the current crew and active tasks. Stalled tasks keep requesting: they try again.</summary>
        private OutpostDemand Demand()
        {
            bool science = false;
            double serviceWatts = 0d;
            foreach (OutpostTaskBoard.ActiveTask task in board.Active())
            {
                if (task.Definition.Category == TaskCategory.Science) science = true;
                else serviceWatts += task.Definition.PowerWatts;
            }
            return new OutpostDemand(roster.Members.Count, science, serviceWatts);
        }

        private void EndTasksOutOfReach(double now)
        {
            foreach (OutpostTaskBoard.ActiveTask task in board.Active())
            {
                Scenario.Layout.TryGetStation(task.Station, out StationPlacement placement);
                if (!TryGetMember(task.Performer, out CrewMemberState member) || !placement.IsWithinReach(member.Pose.Position))
                    board.End(task.Station, TaskEndReason.PerformerMovedAway, now);
            }
        }

        /// <summary>Progress = fully served seconds of the step: science channel for Science, service channel for
        /// Support with power, elapsed time for Support at 0 W. A task that reaches its duration calls Apply once.</summary>
        private void CreditWork(double elapsed, double now)
        {
            double service = Clamp(simulation.LastFullyServedServiceSeconds, elapsed);
            double science = Clamp(simulation.LastFullyServedScienceSeconds, elapsed);
            foreach (OutpostTaskBoard.ActiveTask task in board.Active())
            {
                TaskDefinition definition = task.Definition;
                double credit = definition.Category == TaskCategory.Science ? science
                    : definition.PowerWatts > 0d ? service : elapsed;
                task.Stalled = credit < elapsed * (1d - ServiceTolerance);
                task.WorkSeconds = Math.Min(definition.DurationSeconds, task.WorkSeconds + credit);
                if (task.RemainingSeconds <= OutpostMissionEvaluator.TimeToleranceSeconds)
                {
                    task.WorkSeconds = definition.DurationSeconds;
                    Complete(task, now);
                }
            }
        }

        private void Complete(OutpostTaskBoard.ActiveTask task, double now)
        {
            ResourceEffect effect = task.Definition.CompletionEffect;
            // An empty effect moves no mass, so there is nothing for the simulation to apply or refuse.
            ResourceTransferStatus status = effect.IsEmpty ? ResourceTransferStatus.Applied
                : simulation.Apply(new ResourceTransfer(task.TransferId, effect));
            switch (status)
            {
                case ResourceTransferStatus.Applied:
                    board.End(task.Station, TaskEndReason.Completed, now);
                    for (int i = 0; i < completed.Length; i++)
                        if (Rules.Objectives[i].Counts(task.Definition.Id)) completed[i]++;
                    break;
                case ResourceTransferStatus.InsufficientStock:
                case ResourceTransferStatus.ExceedsCapacity:
                    board.End(task.Station, TaskEndReason.InsufficientResources, now);
                    break;
                default:
                    // Transfer ids are StartTask ids, unique for the authority's life and cleared by Reset:
                    // AlreadyApplied or IdConflict means the model and the authority disagree.
                    throw new InvalidOperationException($"IOutpostSimulation.Apply returned {status} for a new transfer.");
            }
        }

        private static double Clamp(double seconds, double elapsed) =>
            double.IsNaN(seconds) || seconds < 0d ? 0d : Math.Min(seconds, elapsed);

        // ---------------------------------------------------------------- snapshots

        private void EnterFault(OutpostSimulationSnapshot resources)
        {
            outcome = OutpostOutcome.SimulationFault;
            state = SessionState.Interrupted;
            board.EndAll(TaskEndReason.MissionEnded, resources.Power.SimulatedTimeSeconds);
            Commit(resources);
        }

        private void Commit(OutpostSimulationSnapshot resources)
        {
            revision = checked(revision + 1);
            snapshot = Build(resources);
        }

        private OutpostSessionSnapshot Build(OutpostSimulationSnapshot resources)
        {
            var objectives = new List<ObjectiveProgress>(completed.Length);
            for (int i = 0; i < completed.Length; i++)
            {
                OutpostObjectiveRule rule = Rules.Objectives[i];
                objectives.Add(new ObjectiveProgress(rule.Key, completed[i], rule.TargetCount, ProgressUnit.Count, rule.Required));
            }
            return new OutpostSessionSnapshot(epoch, revision, state, outcome, Scenario.Id, roster.Members,
                board.BuildStates(), resources, objectives);
        }

        private OutpostSimulationSnapshot RequireResources() =>
            simulation.Snapshot ?? throw new InvalidOperationException("IOutpostSimulation returned a null snapshot.");

        private bool TryGetMember(PlayerId player, out CrewMemberState member)
        {
            if (player.IsValid)
                foreach (CrewMemberState candidate in roster.Members)
                    if (candidate.Player == player)
                    {
                        member = candidate;
                        return true;
                    }
            member = null;
            return false;
        }
    }
}
