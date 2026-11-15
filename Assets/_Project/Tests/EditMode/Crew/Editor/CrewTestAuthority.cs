using System;
using System.Collections.Generic;
using Trainer.Application;
using Trainer.Application.Crew;
using Trainer.Application.Development;
using Trainer.Application.Outpost;

/// <summary>
/// TEST DOUBLE, NOT THE MISSION AUTHORITY. D2 implements the real <see cref="IOutpostAuthority"/>. This one
/// applies just enough of the documented rules (ids, epoch, exclusion, reach, cancel/leave, restart) over the
/// real D3 <see cref="CrewRoster"/> so that D3's adapters can be exercised end to end. Resources are the fixed
/// fixture readings: it never simulates water, food, air or energy, and task effects are not applied.
/// </summary>
public sealed class CrewTestAuthority : IOutpostAuthority
{
    private sealed class ActiveTask
    {
        public PlayerId Performer;
        public TaskTypeId Task;
        public double Work;
        public double Duration;
    }

    private readonly Dictionary<Guid, (PlayerId sender, OutpostCommand command, CommandResult result)> seen =
        new Dictionary<Guid, (PlayerId, OutpostCommand, CommandResult)>();
    private readonly Dictionary<StationId, ActiveTask> active = new Dictionary<StationId, ActiveTask>();
    private readonly Dictionary<StationId, TaskEnd> lastEnds = new Dictionary<StationId, TaskEnd>();
    private readonly Trainer.Simulation.LifeSupport.OutpostSimulationSnapshot resources;
    private OutpostSessionSnapshot snapshot;
    private long epoch, revision;
    private double time;

    public OutpostScenario Scenario { get; }
    public CrewRoster Roster { get; private set; }
    public SessionState State { get; private set; } = SessionState.Ready;
    public List<PlayerId> Left { get; } = new List<PlayerId>();
    public int SubmitCount { get; private set; }

    public event Action<OutpostSessionSnapshot> SnapshotChanged;
    public event Action<PlayerId, CommandResult> CommandCompleted;

    public CrewTestAuthority(OutpostScenario scenario = null)
    {
        Scenario = scenario ?? OutpostFixtures.Scenario();
        Roster = CrewRoster.For(Scenario);
        resources = OutpostFixtures.InitialResources(Scenario);
        Publish(false);
    }

    public bool TryGetSnapshot(out OutpostSessionSnapshot value)
    {
        value = snapshot;
        return true;
    }

    public OutpostMembership Join()
    {
        OutpostMembership membership = Roster.Join();
        if (membership.IsJoined) Publish(true);
        return membership;
    }

    public bool Leave(PlayerId player)
    {
        if (!Roster.Leave(player)) return false;
        Left.Add(player);
        EndTasksOf(player, TaskEndReason.PerformerLeft);
        Publish(true);
        return true;
    }

    public MovementInputDisposition SubmitMovement(PlayerId sender, MovementInput input)
    {
        MovementInputDisposition disposition = Roster.SubmitMovement(sender, input, epoch);
        if (disposition == MovementInputDisposition.Applied) Publish(true);
        return disposition;
    }

    public void Submit(PlayerId sender, OutpostCommand command)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        SubmitCount++;
        if (command.Id == Guid.Empty)
        {
            CommandCompleted?.Invoke(sender, Result(command, OutpostReasons.InvalidCommandId));
            return;
        }
        if (seen.TryGetValue(command.Id, out var previous))
        {
            bool same = previous.sender == sender && previous.command.HasSameContent(command);
            CommandCompleted?.Invoke(sender, same ? previous.result : Result(command, OutpostReasons.CommandIdConflict));
            return;
        }
        string rejection = Validate(sender, command);
        bool changed = rejection == null && Apply(sender, command);
        CommandResult result = Result(command, rejection ?? OutpostReasons.Accepted);
        seen.Add(command.Id, (sender, command, result));
        if (changed) Publish(command.Action != OutpostAction.Restart);
        CommandCompleted?.Invoke(sender, result);
    }

    /// <summary>Advances the test clock while Running: roster movement, reach checks and task progress.</summary>
    public void Tick(double seconds)
    {
        if (State != SessionState.Running || seconds <= 0) return;
        IReadOnlyList<CrewMemberState> before = Roster.Members;
        Roster.Advance(seconds);
        time += seconds;
        bool changed = !ReferenceEquals(before, Roster.Members);
        foreach (StationPlacement placement in Scenario.Layout.Stations)
        {
            if (!active.TryGetValue(placement.Id, out ActiveTask task)) continue;
            foreach (CrewMemberState member in Roster.Members)
                if (member.Player == task.Performer && !placement.IsWithinReach(member.Pose.Position))
                {
                    End(placement.Id, TaskEndReason.PerformerMovedAway);
                    task = null;
                    break;
                }
            if (task == null)
            {
                changed = true;
                continue;
            }
            task.Work = Math.Min(task.Duration, task.Work + seconds);
            if (task.Work >= task.Duration) End(placement.Id, TaskEndReason.Completed);
            changed = true;
        }
        if (changed) Publish(true);
    }

    private string Validate(PlayerId sender, OutpostCommand command)
    {
        if (!TryGetMember(sender, out CrewMemberState member)) return OutpostReasons.NotJoined;
        if (command.Epoch != epoch) return OutpostReasons.EpochMismatch;
        string shape = command.ShapeError();
        if (shape != null) return shape;
        switch (command.Action)
        {
            case OutpostAction.Restart: return null;
            case OutpostAction.Pause: return State == SessionState.Running ? null : OutpostReasons.InvalidState;
            case OutpostAction.Resume:
                return State == SessionState.Ready || State == SessionState.Paused ? null : OutpostReasons.InvalidState;
            case OutpostAction.StartTask:
                if (State != SessionState.Running) return OutpostReasons.InvalidState;
                if (!Scenario.Layout.TryGetStation(command.Station, out StationPlacement placement)) return OutpostReasons.UnknownStation;
                if (!Scenario.TryGetTask(command.Task, out TaskDefinition task)) return OutpostReasons.UnknownTaskType;
                if (task.Station != command.Station) return OutpostReasons.TaskNotOfferedAtStation;
                foreach (ActiveTask other in active.Values) if (other.Performer == sender) return OutpostReasons.PlayerBusy;
                if (active.ContainsKey(command.Station)) return OutpostReasons.StationOccupied;
                if (!placement.IsWithinReach(member.Pose.Position)) return OutpostReasons.OutOfRange;
                return null;
            case OutpostAction.CancelTask:
                if (State != SessionState.Running && State != SessionState.Paused) return OutpostReasons.InvalidState;
                if (!Scenario.Layout.TryGetStation(command.Station, out _)) return OutpostReasons.UnknownStation;
                return active.TryGetValue(command.Station, out ActiveTask own) && own.Performer == sender
                    ? null : OutpostReasons.NoActiveTask;
            default: return OutpostReasons.UnknownAction;
        }
    }

    private bool Apply(PlayerId sender, OutpostCommand command)
    {
        switch (command.Action)
        {
            case OutpostAction.Restart:
                epoch = checked(epoch + 1);
                revision = 0;
                time = 0;
                State = SessionState.Ready;
                active.Clear();
                lastEnds.Clear();
                Roster.ResetForRestart();
                return true;
            case OutpostAction.Pause: State = SessionState.Paused; return true;
            case OutpostAction.Resume: State = SessionState.Running; return true;
            case OutpostAction.StartTask:
                Scenario.TryGetTask(command.Task, out TaskDefinition task);
                active[command.Station] = new ActiveTask { Performer = sender, Task = command.Task, Duration = task.DurationSeconds };
                return true;
            case OutpostAction.CancelTask:
                End(command.Station, TaskEndReason.CancelledByPerformer);
                return true;
        }
        return false;
    }

    private void EndTasksOf(PlayerId player, TaskEndReason reason)
    {
        foreach (StationId station in new List<StationId>(active.Keys))
            if (active[station].Performer == player) End(station, reason);
    }

    private void End(StationId station, TaskEndReason reason)
    {
        ActiveTask task = active[station];
        active.Remove(station);
        lastEnds[station] = new TaskEnd(task.Task, task.Performer, reason, time);
    }

    private bool TryGetMember(PlayerId player, out CrewMemberState member)
    {
        foreach (CrewMemberState candidate in Roster.Members)
            if (candidate.Player == player)
            {
                member = candidate;
                return true;
            }
        member = null;
        return false;
    }

    private static CommandResult Result(OutpostCommand command, string reason) =>
        new CommandResult(command.Id, command.Epoch, reason == OutpostReasons.Accepted, reason);

    private void Publish(bool bump)
    {
        if (bump) revision = checked(revision + 1);
        var stations = new List<StationState>();
        foreach (StationPlacement placement in Scenario.Layout.Stations)
        {
            active.TryGetValue(placement.Id, out ActiveTask task);
            lastEnds.TryGetValue(placement.Id, out TaskEnd end);
            stations.Add(task == null
                ? new StationState(placement.Id, StationAvailability.Available, null, end)
                : new StationState(placement.Id, StationAvailability.Occupied,
                    new TaskProgress(task.Task, task.Performer, task.Work, task.Duration, false), end));
        }
        snapshot = new OutpostSessionSnapshot(epoch, revision, State, OutpostOutcome.InProgress, Scenario.Id,
            Roster.Members, stations, resources, OutpostFixtures.ExampleObjectives());
        SnapshotChanged?.Invoke(snapshot);
    }
}
