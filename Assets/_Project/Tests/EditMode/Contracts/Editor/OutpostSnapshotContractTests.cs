using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Application.Outpost;
using Trainer.Simulation.LifeSupport;

/// <summary>Phase 2 common base: snapshot invariants, immutability, command shape and movement ordering. Only
/// guarantees of the types and of the shared policy are tested; no authority exists in this base.</summary>
public sealed class OutpostSnapshotContractTests
{
    private static readonly PlayerId P1 = new PlayerId(1), P2 = new PlayerId(2);
    private OutpostScenario scenario;

    [SetUp] public void SetUp() => scenario = OutpostFixtures.Scenario();

    private static CrewMemberState Member(PlayerId player, int slot, double x = 3, double y = 3) =>
        new CrewMemberState(player, slot, CrewPose.IdleAt(new BasePosition(x, y)), 0);

    private static StationState Busy(StationId station, PlayerId performer) =>
        new StationState(station, StationAvailability.Occupied,
            new TaskProgress(OutpostFixtures.TreatWater, performer, 1, 20, false), null);

    private OutpostSessionSnapshot Snapshot(SessionState state = SessionState.Running,
        OutpostOutcome outcome = OutpostOutcome.InProgress, IEnumerable<CrewMemberState> crew = null,
        IEnumerable<StationState> stations = null, IEnumerable<ObjectiveProgress> objectives = null, long epoch = 0,
        long revision = 1) =>
        new OutpostSessionSnapshot(epoch, revision, state, outcome, scenario.Id,
            crew ?? new[] { Member(P1, 0), Member(P2, 1) },
            stations ?? scenario.Layout.Stations.Select(s => StationState.Free(s.Id)),
            OutpostFixtures.InitialResources(scenario), objectives ?? OutpostFixtures.ExampleObjectives());

    [Test] public void SnapshotIsAPrivateReadOnlyCopy()
    {
        var crew = new List<CrewMemberState> { Member(P1, 0) };
        var stations = scenario.Layout.Stations.Select(s => StationState.Free(s.Id)).ToList();
        var objectives = OutpostFixtures.ExampleObjectives().ToList();
        OutpostSessionSnapshot snapshot = Snapshot(crew: crew, stations: stations, objectives: objectives);

        crew.Add(Member(P2, 1));
        stations.Clear();
        objectives.Clear();
        Assert.That(snapshot.Crew.Count, Is.EqualTo(1));
        Assert.That(snapshot.Stations.Count, Is.EqualTo(3));
        Assert.That(snapshot.Objectives.Count, Is.EqualTo(2));
        Assert.Throws<NotSupportedException>(() => ((IList<CrewMemberState>)snapshot.Crew).Add(Member(P2, 1)));
        Assert.Throws<NotSupportedException>(() => ((IList<StationState>)snapshot.Stations)[0] = null);
        Assert.That(snapshot.SimulatedTimeSeconds, Is.EqualTo(0));
        Assert.That(snapshot.TryGetCrew(P1, out CrewMemberState one), Is.True);
        Assert.That(one.Slot, Is.EqualTo(0));
        Assert.That(snapshot.TryGetCrew(P2, out _), Is.False);
        Assert.That(snapshot.TryGetStation(OutpostFixtures.AirMonitor, out StationState air), Is.True);
        Assert.That(air.Availability, Is.EqualTo(StationAvailability.Available));
    }

    [Test] public void PublishedContractTypesExposeNoMutableState()
    {
        Type[] types = typeof(OutpostSessionSnapshot).Assembly.GetTypes()
            .Where(t => t.IsPublic && (t.Namespace == "Trainer.Application.Outpost" || t.Namespace == "Trainer.Simulation.LifeSupport"))
            .Where(t => !t.IsInterface && !t.IsEnum && !(t.IsAbstract && t.IsSealed)) // static classes hold constants only
            .ToArray();
        Assert.That(types.Length, Is.GreaterThanOrEqualTo(25), "Every contract value type is inspected.");
        foreach (Type type in types)
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                Assert.That(property.SetMethod == null || !property.SetMethod.IsPublic, Is.True, $"{type.Name}.{property.Name} has a public setter.");
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                Assert.That(field.IsInitOnly || field.IsLiteral, Is.True, $"{type.Name}.{field.Name} is a writable field.");
            if (type.IsValueType) Assert.That(type.GetCustomAttributes().Any(a => a.GetType().Name == "IsReadOnlyAttribute"),
                Is.True, $"{type.Name} must be a readonly struct.");
        }
    }

    [Test] public void SnapshotRejectsInconsistentStates()
    {
        Assert.Throws<ArgumentException>(() => Snapshot(crew: new[] { Member(P1, 0), Member(P1, 1) }), "Duplicate player.");
        Assert.Throws<ArgumentException>(() => Snapshot(crew: new[] { Member(P1, 0), Member(P2, 0) }), "Duplicate slot.");
        Assert.Throws<ArgumentException>(() => Snapshot(crew: new[] { Member(P1, 0) },
            stations: new[] { Busy(OutpostFixtures.WaterProcessor, P2) }), "Performer not present.");
        Assert.Throws<ArgumentException>(() => Snapshot(stations: new[]
            { Busy(OutpostFixtures.WaterProcessor, P1), Busy(OutpostFixtures.AirMonitor, P1) }), "One task per member.");
        Assert.Throws<ArgumentException>(() => Snapshot(SessionState.Completed, OutpostOutcome.TimeExpired,
            stations: new[] { Busy(OutpostFixtures.WaterProcessor, P1) }), "No task in progress after the end.");
        Assert.Throws<ArgumentException>(() => Snapshot(stations: new[]
            { StationState.Free(OutpostFixtures.AirMonitor), StationState.Free(OutpostFixtures.AirMonitor) }), "Duplicate station.");
        Assert.Throws<ArgumentException>(() => Snapshot(objectives: new[]
            { new ObjectiveProgress("x", 0, 1, ProgressUnit.Count, true), new ObjectiveProgress("x", 0, 2, ProgressUnit.Count, true) }));
        var tooMany = Enumerable.Range(0, OutpostLimits.MaxObjectives + 1)
            .Select(i => new ObjectiveProgress("o" + i, 0, 1, ProgressUnit.Count, false));
        Assert.Throws<ArgumentException>(() => Snapshot(objectives: tooMany));

        Assert.Throws<ArgumentException>(() => Snapshot(SessionState.Completed, OutpostOutcome.InProgress));
        Assert.Throws<ArgumentException>(() => Snapshot(SessionState.Running, OutpostOutcome.ObjectiveAchieved));
        Assert.Throws<ArgumentException>(() => Snapshot(SessionState.Interrupted, OutpostOutcome.TimeExpired));
        Assert.Throws<ArgumentException>(() => Snapshot(SessionState.Completed, OutpostOutcome.SimulationFault));
        Assert.Throws<ArgumentOutOfRangeException>(() => Snapshot((SessionState)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => Snapshot(epoch: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Snapshot(revision: -1));
        Assert.Throws<ArgumentException>(() => new OutpostSessionSnapshot(0, 0, SessionState.Ready, OutpostOutcome.InProgress,
            "Not A Key", new CrewMemberState[0], new StationState[0], OutpostFixtures.InitialResources(scenario), new ObjectiveProgress[0]));
        Assert.Throws<ArgumentNullException>(() => new OutpostSessionSnapshot(0, 0, SessionState.Ready, OutpostOutcome.InProgress,
            scenario.Id, new CrewMemberState[0], new StationState[0], null, new ObjectiveProgress[0]));

        Assert.That(Snapshot(stations: new[] { Busy(OutpostFixtures.WaterProcessor, P1), Busy(OutpostFixtures.AirMonitor, P2) })
            .Stations[1].Occupant, Is.EqualTo(P2), "Two members on two stations is valid.");
        Assert.That(Snapshot(SessionState.Interrupted, OutpostOutcome.EssentialServiceLost).Outcome,
            Is.EqualTo(OutpostOutcome.EssentialServiceLost));
    }

    [Test] public void OrderingIsEpochThenRevision()
    {
        OutpostSessionSnapshot e0r1 = Snapshot(revision: 1), e0r2 = Snapshot(revision: 2), e1r0 = Snapshot(epoch: 1, revision: 0);
        Assert.That(e0r1.IsNewerThan(null), Is.True);
        Assert.That(e0r2.IsNewerThan(e0r1), Is.True);
        Assert.That(e0r1.IsNewerThan(e0r2), Is.False);
        Assert.That(e0r1.IsNewerThan(Snapshot(revision: 1)), Is.False, "Equal is not newer.");
        Assert.That(e1r0.IsNewerThan(e0r2), Is.True, "A new epoch wins with revision 0.");
        Assert.That(e0r2.IsNewerThan(e1r0), Is.False);
    }

    [Test] public void StationAndTaskStateStayConsistent()
    {
        var progress = new TaskProgress(OutpostFixtures.TreatWater, P1, 5, 20, true);
        Assert.That(progress.Fraction, Is.EqualTo(0.25));
        Assert.That(progress.Stalled, Is.True);
        Assert.Throws<ArgumentException>(() => new StationState(OutpostFixtures.WaterProcessor, StationAvailability.Occupied, null, null));
        Assert.Throws<ArgumentException>(() => new StationState(OutpostFixtures.WaterProcessor, StationAvailability.Available, progress, null));
        Assert.Throws<ArgumentException>(() => new StationState(OutpostFixtures.WaterProcessor, StationAvailability.Unavailable, progress, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TaskProgress(OutpostFixtures.TreatWater, P1, 20.5, 20, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TaskProgress(OutpostFixtures.TreatWater, P1, double.NaN, 20, false));
        Assert.Throws<ArgumentException>(() => new TaskProgress(OutpostFixtures.TreatWater, PlayerId.None, 0, 20, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TaskEnd(OutpostFixtures.TreatWater, P1, (TaskEndReason)99, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TaskEnd(OutpostFixtures.TreatWater, P1, TaskEndReason.Completed, -1));
        var ended = new StationState(OutpostFixtures.WaterProcessor, StationAvailability.Available, null,
            new TaskEnd(OutpostFixtures.TreatWater, P2, TaskEndReason.PerformerLeft, 42));
        Assert.That(ended.Occupant, Is.EqualTo(PlayerId.None));
        Assert.That(Snapshot(crew: new[] { Member(P1, 0) }, stations: new[] { ended }).Stations[0].LastEnd.Performer,
            Is.EqualTo(P2), "A finished task may name a member who already left.");
    }

    [Test] public void CommandShapeIsOneSharedCheck()
    {
        Guid id = Guid.NewGuid();
        StationId water = OutpostFixtures.WaterProcessor;
        TaskTypeId treat = OutpostFixtures.TreatWater;
        Assert.That(OutpostCommand.StartTask(id, 0, water, treat).ShapeError(), Is.Null);
        Assert.That(OutpostCommand.CancelTask(id, 0, water).ShapeError(), Is.Null);
        foreach (OutpostAction action in new[] { OutpostAction.Pause, OutpostAction.Resume, OutpostAction.Restart })
        {
            Assert.That(new OutpostCommand(id, 0, action).ShapeError(), Is.Null, action.ToString());
            Assert.That(new OutpostCommand(id, 0, action, water).ShapeError(), Is.EqualTo(OutpostReasons.InvalidActionValue));
        }
        Assert.That(new OutpostCommand(id, 0, OutpostAction.StartTask, water).ShapeError(), Is.EqualTo(OutpostReasons.InvalidActionValue));
        Assert.That(new OutpostCommand(id, 0, OutpostAction.StartTask, StationId.None, treat).ShapeError(), Is.EqualTo(OutpostReasons.InvalidActionValue));
        Assert.That(new OutpostCommand(id, 0, OutpostAction.CancelTask, water, treat).ShapeError(), Is.EqualTo(OutpostReasons.InvalidActionValue));
        Assert.That(new OutpostCommand(id, 0, (OutpostAction)77).ShapeError(), Is.EqualTo(OutpostReasons.UnknownAction));
        Assert.That(new OutpostCommand(Guid.Empty, 0, OutpostAction.Pause).ShapeError(), Is.EqualTo(OutpostReasons.InvalidCommandId));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutpostCommand(id, -1, OutpostAction.Pause));

        OutpostCommand start = OutpostCommand.StartTask(id, 0, water, treat);
        Assert.That(start.HasSameContent(OutpostCommand.StartTask(id, 0, water, treat)), Is.True);
        Assert.That(start.HasSameContent(OutpostCommand.StartTask(id, 1, water, treat)), Is.False);
        Assert.That(start.HasSameContent(OutpostCommand.CancelTask(id, 0, water)), Is.False);
        Assert.That(start.HasSameContent(null), Is.False);
        Assert.That(OutpostReasons.Accepted, Is.EqualTo("Accepted"), "Phase 1 strings keep their meaning.");
    }

    [Test] public void MovementPolicyAppliesOnlyNewerInputsOfTheCurrentEpoch()
    {
        MovementInput Input(long epoch, long sequence) => new MovementInput(epoch, sequence, MoveDirection.FromAxes(1, 0));
        Assert.That(MovementInputPolicy.Evaluate(0, 0, Input(0, 1)), Is.EqualTo(MovementInputDisposition.Applied));
        Assert.That(MovementInputPolicy.Evaluate(5, 0, Input(0, 5)), Is.EqualTo(MovementInputDisposition.StaleSequence), "Duplicate.");
        Assert.That(MovementInputPolicy.Evaluate(5, 0, Input(0, 4)), Is.EqualTo(MovementInputDisposition.StaleSequence), "Reordered.");
        Assert.That(MovementInputPolicy.Evaluate(5, 0, Input(0, 9)), Is.EqualTo(MovementInputDisposition.Applied), "Gaps are allowed.");
        Assert.That(MovementInputPolicy.Evaluate(5, 1, Input(0, 9)), Is.EqualTo(MovementInputDisposition.EpochMismatch), "From before Restart.");
        Assert.That(MovementInputPolicy.Evaluate(5, 1, Input(2, 9)), Is.EqualTo(MovementInputDisposition.EpochMismatch));
        Assert.That(MovementInputPolicy.Evaluate(5, 1, Input(1, 6)), Is.EqualTo(MovementInputDisposition.Applied),
            "Sequences continue across epochs.");
        Assert.Throws<ArgumentNullException>(() => MovementInputPolicy.Evaluate(0, 0, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementInput(0, 0, MoveDirection.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementInput(-1, 1, MoveDirection.Zero));
    }

    [Test] public void MembershipAndPoseCarryConsistentIdentity()
    {
        Assert.That(OutpostMembership.NotJoined.Player, Is.EqualTo(PlayerId.None));
        Assert.That(OutpostMembership.Joined(P1).IsJoined, Is.True);
        Assert.That(OutpostMembership.Left(P1, OutpostReasons.ConnectionLost).Player, Is.EqualTo(P1));
        Assert.That(OutpostMembership.Rejected(OutpostReasons.SessionFull).Player, Is.EqualTo(PlayerId.None));
        Assert.Throws<ArgumentException>(() => OutpostMembership.Joined(PlayerId.None));
        Assert.Throws<ArgumentException>(() => OutpostMembership.Left(PlayerId.None, OutpostReasons.ConnectionLost));
        Assert.Throws<ArgumentException>(() => OutpostMembership.Rejected(" "));
        Assert.Throws<ArgumentException>(() => OutpostMembership.Left(P1, null));

        Assert.Throws<ArgumentException>(() => new CrewPose(new BasePosition(0, 0), MoveDirection.Zero, CrewMotion.Idle));
        Assert.Throws<ArgumentException>(() => new CrewPose(new BasePosition(0, 0), new MoveDirection(0.5, 0), CrewMotion.Walking));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CrewPose(new BasePosition(0, 0), CrewPose.DefaultFacing, (CrewMotion)3));
        var walking = new CrewPose(new BasePosition(1, 1), MoveDirection.FromAxes(1, 1), CrewMotion.Walking);
        Assert.That(walking.Facing.Magnitude, Is.EqualTo(1).Within(1e-12));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CrewMemberState(P1, OutpostLimits.MaxCrew, walking, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CrewMemberState(P1, -1, walking, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CrewMemberState(P1, 0, walking, -1));
        Assert.Throws<ArgumentException>(() => new CrewMemberState(PlayerId.None, 0, walking, 0));
        Assert.Throws<ArgumentNullException>(() => new CrewMemberState(P1, 0, null, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() => new ObjectiveProgress("o", -1, 1, ProgressUnit.Count, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ObjectiveProgress("o", 0, 0, ProgressUnit.Count, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ObjectiveProgress("o", 0, 1, (ProgressUnit)5, true));
        Assert.That(new ObjectiveProgress("o", 2, 2, ProgressUnit.Kilograms, false).IsMet, Is.True);
    }
}
