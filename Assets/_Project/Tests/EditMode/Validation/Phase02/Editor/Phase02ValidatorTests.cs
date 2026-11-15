using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Application.Outpost;
using Trainer.Simulation;
using Trainer.Simulation.LifeSupport;
using Trainer.Tooling.Validation.Phase02;

/// <summary>D5 phase 2: static scenario checks (navigability, reachability, parallel work, effects).</summary>
public sealed class Phase02ScenarioValidatorTests
{
    private static string Codes(Phase02Report r) => string.Join(", ", r.Issues.Select(i => i.Severity + ":" + i.Code));

    [Test] public void D5ConformanceScenarioIsNavigableAndComplete()
    {
        Phase02Report report = OutpostScenarioValidator.Validate(Phase02Fixtures.Scenario());
        Assert.That(report.IsValid, Is.True, report.ToString());
        Assert.That(report.Has("NavigationGrid"), Is.True);
        Assert.That(report.Has("EffectNeedsStock"), Is.True, "overdraw is flagged as needing stock: " + Codes(report));
    }

    [Test] public void CommonBaseFixtureScenarioIsNavigable()
    {
        Phase02Report report = OutpostScenarioValidator.Validate(OutpostFixtures.Scenario());
        Assert.That(report.IsValid, Is.True, report.ToString());
    }

    [Test] public void EnclosedStationIsUnreachable()
    {
        Phase02Report report = OutpostScenarioValidator.Validate(Phase02Fixtures.EnclosedStationScenario());
        Assert.That(report.Issues.Any(i => i.Code == "StationUnreachable" && i.Message.Contains("dock-b")), Is.True, Codes(report));
        Assert.That(report.Issues.Count(i => i.Code.StartsWith("StationUnreachable")), Is.EqualTo(1), Codes(report));
    }

    [Test] public void ClosedDoorSplitsCrewAndStations()
    {
        Phase02Report report = OutpostScenarioValidator.Validate(Phase02Fixtures.SplitPlanScenario());
        Assert.That(report.Has("SpawnsDisconnected"), Is.True, Codes(report));
        Assert.That(report.Issues.Any(i => i.Code == "StationUnreachableFromSpawn0" && i.Message.Contains("far-lab")), Is.True, Codes(report));
    }

    [TestCase(0.55, false)] // door narrower than the crew diameter (0.6 m)
    [TestCase(0.75, true)]  // the grid is conservative by up to one step (0.05 m) on narrow passages
    public void DoorWidthAgainstCrewDiameter(double door, bool reachable)
    {
        var layout = new OutpostLayout(Phase02Fixtures.Bounds,
            new[] { new BaseRect(5.9, 0, 6.1, 5), new BaseRect(5.9, 5 + door, 6.1, 8) },
            Phase02Fixtures.Layout().Stations, Phase02Fixtures.Layout().SpawnPoints);
        Phase02Report report = OutpostScenarioValidator.Validate(Phase02Fixtures.WithLayout(layout));
        Assert.That(!report.Has("StationUnreachable"), Is.EqualTo(reachable), Codes(report));
    }

    [Test] public void SingleSpawnAndSingleTaskStationAreErrors()
    {
        var layout = new OutpostLayout(Phase02Fixtures.Bounds, new BaseRect[0],
            new[] { new StationPlacement(Phase02Fixtures.DockA, new BasePosition(2, 2), 1.2) }, new[] { new BasePosition(2, 3) });
        var scenario = new OutpostScenario("one-room", "D5 sintético", layout, Phase02Fixtures.Movement(),
            new[] { new TaskDefinition(Phase02Fixtures.FixA, Phase02Fixtures.DockA, TaskCategory.Support, 4, 0, ResourceEffect.None) },
            Phase02Fixtures.Simulation());
        Phase02Report report = OutpostScenarioValidator.Validate(scenario);
        Assert.That(report.Has("TooFewSpawnPoints") && report.Has("NoParallelWork"), Is.True, Codes(report));
        Assert.That(report.Has("NoScienceTask"), Is.True, Codes(report));
    }

    [Test] public void EffectLargerThanCapacityIsNeverApplicable()
    {
        var huge = new ResourceEffect(new[] { new ResourceChange(ResourceKind.StoredFood, -31), new ResourceChange(ResourceKind.UntreatedWater, 31) }, 0);
        var scenario = new OutpostScenario("huge", "D5 sintético", Phase02Fixtures.Layout(), Phase02Fixtures.Movement(),
            new[]
            {
                new TaskDefinition(Phase02Fixtures.FixA, Phase02Fixtures.DockA, TaskCategory.Support, 4, 0, huge),
                new TaskDefinition(Phase02Fixtures.LabRun, Phase02Fixtures.FarLab, TaskCategory.Science, 4, 0, ResourceEffect.None)
            }, Phase02Fixtures.Simulation());
        Phase02Report report = OutpostScenarioValidator.Validate(scenario);
        Assert.That(report.Issues.Count(i => i.Code == "EffectNeverApplicable"), Is.EqualTo(2), Codes(report));
    }
}

/// <summary>D5 phase 2: snapshot state and transition rules, with one mutant per rule.</summary>
public sealed class Phase02SnapshotStreamValidatorTests
{
    private static readonly PlayerId Ana = new PlayerId(1), Leo = new PlayerId(2), Eva = new PlayerId(3);
    private OutpostScenario scenario;
    private OutpostSessionSnapshot s0;

    [SetUp] public void SetUp()
    {
        scenario = OutpostFixtures.Scenario();
        s0 = OutpostFixtures.Initial(scenario, 0, Ana, Leo);
    }

    private OutpostSimulationSnapshot At(double t, double potable = -1, double untreated = -1, double energy = -1)
    {
        OutpostSimulationSnapshot r = s0.Resources;
        WaterStock w = r.LifeSupport.Water;
        var water = new WaterStock(potable < 0 ? w.PotableKilograms : potable, untreated < 0 ? w.UntreatedKilograms : untreated,
            w.PotableCapacityKilograms, w.UntreatedCapacityKilograms);
        PowerSnapshot p = r.Power;
        var power = new PowerSnapshot(t, energy < 0 ? p.EnergyJoules : energy, p.CapacityJoules, p.GenerationWatts,
            p.EssentialRequestedWatts, p.EssentialServedWatts, 0, 0);
        return new OutpostSimulationSnapshot(power, 0, 0, new LifeSupportSnapshot(water, r.LifeSupport.Food, r.LifeSupport.Cabin));
    }

    private static CrewPose Walk(double x, double y) => new CrewPose(new BasePosition(x, y), new MoveDirection(1, 0), CrewMotion.Walking);

    private Phase02Report Feed(params OutpostSessionSnapshot[] stream)
    {
        var v = new OutpostSnapshotStreamValidator(scenario);
        foreach (OutpostSessionSnapshot s in stream) v.Observe(s);
        return v.Report;
    }

    private static StationState Occupied(StationId station, TaskTypeId task, PlayerId who, double work, double duration) =>
        new StationState(station, StationAvailability.Occupied, new TaskProgress(task, who, work, duration, false), null);

    private static StationState Ended(StationId station, TaskTypeId task, PlayerId who, TaskEndReason reason, double t) =>
        new StationState(station, StationAvailability.Available, null, new TaskEnd(task, who, reason, t));

    private static void HasError(Phase02Report r, string code) =>
        Assert.That(r.Issues.Any(i => i.Code == code && i.Severity == Phase02Severity.Error), Is.True, "Expected " + code + ": " + r);

    [Test] public void ValidPlaythroughHasNoErrors()
    {
        var s1 = OutpostFixtures.Next(s0, state: SessionState.Running);
        // Ana walks 1 m in 0.5 s at 2 m/s towards the water processor (3, 8) from (3, 3): stays on x = 3.
        var s2 = OutpostFixtures.Next(s1, crew: OutpostFixtures.WithMember(s1, Ana, new CrewPose(new BasePosition(3, 4), new MoveDirection(0, 1), CrewMotion.Walking), 1), resources: At(0.5));
        var s3 = OutpostFixtures.Next(s2, crew: OutpostFixtures.WithMember(s2, Ana, new CrewPose(new BasePosition(3, 7), new MoveDirection(0, 1), CrewMotion.Idle), 2), resources: At(2.0));
        var s4 = OutpostFixtures.Next(s3, stations: OutpostFixtures.WithStation(s3, Occupied(OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater, Ana, 0, 20)));
        var s5 = OutpostFixtures.Next(s4, stations: OutpostFixtures.WithStation(s4, Occupied(OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater, Ana, 10, 20)), resources: At(12));
        // Completion inside a timed step: the stream does not judge stocks here (D1 demand unknown).
        var s6 = OutpostFixtures.Next(s5, stations: OutpostFixtures.WithStation(s5, Ended(OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater, Ana, TaskEndReason.Completed, 22)),
            resources: At(22, potable: 21.8, untreated: 8));
        var s7 = OutpostFixtures.Next(s6, crew: OutpostFixtures.WithoutMember(s6, Leo));
        // Restart keeps ids, slots and input sequences (SESSION_CONTRACT): Ana's last applied sequence stays 2.
        var r = OutpostFixtures.Initial(scenario, 1, Ana);
        var s8 = OutpostFixtures.Next(r, crew: OutpostFixtures.WithMember(r, Ana, CrewPose.IdleAt(scenario.Layout.SpawnPoints[0]), 2), newEpoch: 1);
        Phase02Report report = Feed(s0, s1, s2, s3, s4, s5, s6, s7, s8);
        Assert.That(report.IsValid, Is.True, report.ToString());
    }

    [Test] public void OlderOrEqualSnapshotIsRejected() => HasError(Feed(s0, OutpostFixtures.Next(s0), s0), "NotNewer");

    [Test] public void TeleportIsRejected()
    {
        var s1 = OutpostFixtures.Next(s0, crew: OutpostFixtures.WithMember(s0, Ana, Walk(5, 3), 1), resources: At(0.5));
        HasError(Feed(s0, s1), "MovedTooFar");
    }

    [Test] public void MovementWithoutTimeIsRejected()
    {
        var s1 = OutpostFixtures.Next(s0, crew: OutpostFixtures.WithMember(s0, Ana, Walk(3.1, 3), 1));
        HasError(Feed(s0, s1), "MovedTooFar");
    }

    [Test] public void PositionInsideObstacleIsRejected()
    {
        var s1 = OutpostFixtures.Next(s0, crew: OutpostFixtures.WithMember(s0, Ana, Walk(7.6, 2), 1), resources: At(10));
        HasError(Feed(s0, s1), "PositionNotAdmissible");
    }

    [Test] public void ReusedPlayerIdIsRejected()
    {
        var s1 = OutpostFixtures.Next(s0, crew: OutpostFixtures.WithoutMember(s0, Ana));
        var back = new List<CrewMemberState>(s1.Crew) { new CrewMemberState(Ana, 0, CrewPose.IdleAt(scenario.Layout.SpawnPoints[0]), 0) };
        HasError(Feed(s0, s1, OutpostFixtures.Next(s1, crew: back)), "PlayerIdReused");
    }

    [Test] public void InputSequenceMustNotRegress()
    {
        var s1 = OutpostFixtures.Next(s0, crew: OutpostFixtures.WithMember(s0, Ana, CrewPose.IdleAt(scenario.Layout.SpawnPoints[0]), 5));
        var s2 = OutpostFixtures.Next(s1, crew: OutpostFixtures.WithMember(s1, Ana, CrewPose.IdleAt(scenario.Layout.SpawnPoints[0]), 4));
        HasError(Feed(s0, s1, s2), "InputSequenceRegressed");
    }

    [Test] public void TaskCannotVanishWithoutEnd()
    {
        var run = OutpostFixtures.Next(s0, state: SessionState.Running, crew: OutpostFixtures.WithMember(s0, Ana, CrewPose.IdleAt(new BasePosition(3, 7)), 1), resources: At(5));
        var s1 = OutpostFixtures.Next(run, stations: OutpostFixtures.WithStation(run, Occupied(OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater, Ana, 0, 20)));
        var s2 = OutpostFixtures.Next(s1, stations: OutpostFixtures.WithStation(s1, StationState.Free(OutpostFixtures.WaterProcessor)));
        HasError(Feed(run, s1, s2), "TaskVanished");
    }

    [Test] public void LeaveMustRecordPerformerLeft()
    {
        var run = OutpostFixtures.Next(s0, state: SessionState.Running, crew: OutpostFixtures.WithMember(s0, Ana, CrewPose.IdleAt(new BasePosition(3, 7)), 1), resources: At(5));
        var s1 = OutpostFixtures.Next(run, stations: OutpostFixtures.WithStation(run, Occupied(OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater, Ana, 3, 20)));
        var wrong = OutpostFixtures.Next(s1, crew: OutpostFixtures.WithoutMember(s1, Ana),
            stations: OutpostFixtures.WithStation(s1, Ended(OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater, Ana, TaskEndReason.CancelledByPerformer, 5)));
        HasError(Feed(run, s1, wrong), "LeaveNotRecorded");
        var right = OutpostFixtures.Next(s1, crew: OutpostFixtures.WithoutMember(s1, Ana),
            stations: OutpostFixtures.WithStation(s1, Ended(OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater, Ana, TaskEndReason.PerformerLeft, 5)));
        Assert.That(Feed(run, s1, right).IsValid, Is.True);
    }

    [Test] public void InstantStockChangeNeedsACompletion()
    {
        var s1 = OutpostFixtures.Next(s0, resources: At(0, potable: 25));
        HasError(Feed(s0, s1), "UnaccountedMass");
    }

    [Test] public void InstantCompletionMustMatchItsEffectOnce()
    {
        var run = OutpostFixtures.Next(s0, state: SessionState.Running, crew: OutpostFixtures.WithMember(s0, Ana, CrewPose.IdleAt(new BasePosition(3, 7)), 1), resources: At(20));
        var s1 = OutpostFixtures.Next(run, stations: OutpostFixtures.WithStation(run, Occupied(OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater, Ana, 20, 20)));
        var once = OutpostFixtures.Next(s1, stations: OutpostFixtures.WithStation(s1, Ended(OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater, Ana, TaskEndReason.Completed, 20)),
            resources: At(20, potable: 21.8, untreated: 8));
        Assert.That(Feed(run, s1, once).IsValid, Is.True, "Effect applied once at Δt = 0 is accounted.");
        var twice = OutpostFixtures.Next(s1, stations: OutpostFixtures.WithStation(s1, Ended(OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater, Ana, TaskEndReason.Completed, 20)),
            resources: At(20, potable: 23.6, untreated: 6));
        HasError(Feed(run, s1, twice), "UnaccountedMass");
    }

    [Test] public void CompletionBeforeDurationIsRejected()
    {
        var run = OutpostFixtures.Next(s0, state: SessionState.Running, crew: OutpostFixtures.WithMember(s0, Ana, CrewPose.IdleAt(new BasePosition(3, 7)), 1), resources: At(5));
        var s1 = OutpostFixtures.Next(run, stations: OutpostFixtures.WithStation(run, Occupied(OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater, Ana, 2, 20)));
        var s2 = OutpostFixtures.Next(s1, stations: OutpostFixtures.WithStation(s1, Ended(OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater, Ana, TaskEndReason.Completed, 6)), resources: At(6));
        HasError(Feed(run, s1, s2), "CompletedTooEarly");
    }

    [Test] public void PerformerOutOfReachIsRejected()
    {
        var run = OutpostFixtures.Next(s0, state: SessionState.Running);
        var s1 = OutpostFixtures.Next(run, stations: OutpostFixtures.WithStation(run, Occupied(OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater, Ana, 0, 20)));
        HasError(Feed(run, s1), "PerformerOutOfReach");
    }

    [Test] public void EndedMissionCannotResumeWithinEpoch()
    {
        var done = OutpostFixtures.Next(s0, state: SessionState.Completed, outcome: OutpostOutcome.TimeExpired);
        var back = OutpostFixtures.Next(done, state: SessionState.Running, outcome: OutpostOutcome.InProgress);
        HasError(Feed(s0, done, back), "EndedMissionResumed");
    }

    [Test] public void RestartMustResetPosesTasksAndStocks()
    {
        var moved = OutpostFixtures.Next(s0, crew: OutpostFixtures.WithMember(s0, Ana, Walk(4, 3), 1), resources: At(1, potable: 18));
        var badPose = OutpostFixtures.Next(moved, newEpoch: 1, resources: At(0));
        HasError(Feed(s0, moved, badPose), "RestartPoseNotReset");
        var initial = OutpostFixtures.Initial(scenario, 1, Ana, Leo);
        var fresh = OutpostFixtures.Next(initial, crew: OutpostFixtures.WithMember(initial, Ana, CrewPose.IdleAt(scenario.Layout.SpawnPoints[0]), 1), newEpoch: 1);
        var badStocks = OutpostFixtures.Next(fresh, newEpoch: 1, resources: At(0, potable: 18));
        HasError(Feed(s0, moved, badStocks), "RestartStocks");
        Assert.That(Feed(s0, moved, fresh).IsValid, Is.True);
    }

    [Test] public void JoinMustUseAFreeSlotAtItsSpawn()
    {
        var crew = new List<CrewMemberState>(s0.Crew) { new CrewMemberState(Eva, 2, CrewPose.IdleAt(new BasePosition(10, 5)), 0) };
        HasError(Feed(s0, OutpostFixtures.Next(s0, crew: crew)), "JoinNotAtSpawn");
    }
}
