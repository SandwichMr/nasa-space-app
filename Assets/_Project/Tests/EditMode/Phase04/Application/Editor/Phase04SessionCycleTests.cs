using System;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation;
using H = Phase04LivingBaseHarness;

/// <summary>Phase 4 D2: global session cycle versus activities, pause, restart, irreversible rule and training mode.</summary>
public sealed class Phase04SessionCycleTests
{
    [Test]
    public void ContinuousSessionRunsPastThePhase3MissionDurationAndNeverCompletes()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        double duration = h.Scenario.Expedition.Rules.MissionDurationSeconds;
        h.Tick(duration + 100);
        Assert.That(h.Now.Outpost.State, Is.EqualTo(SessionState.Running));
        Assert.That(h.Time, Is.GreaterThan(duration));
        Assert.That(h.Now.Session.Mode, Is.EqualTo(BaseSessionMode.Continuous));
        Assert.That(h.Now.Session.EndCause, Is.EqualTo(IrreversibleCause.None));
        foreach (BaseOperationsSnapshot s in h.Published) Assert.That(s.Outpost.State, Is.Not.EqualTo(SessionState.Completed));
    }

    [Test]
    public void PauseStopsTheClockAndEveryConsumption()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Tick(5);
        Assert.That(h.Session(ana, ExpeditionAction.Pause).Accepted);
        BaseOperationsSnapshot paused = h.Now;
        double loading = h.Equipment.Snapshot.Air.ActiveLoadingFraction;
        h.Tick(30);
        Assert.That(h.Time, Is.EqualTo(paused.SimulatedTimeSeconds));
        Assert.That(h.Now.Outpost.Resources.LifeSupport.Water.PotableKilograms, Is.EqualTo(paused.Outpost.Resources.LifeSupport.Water.PotableKilograms));
        Assert.That(h.Equipment.Snapshot.Air.ActiveLoadingFraction, Is.EqualTo(loading), "no bed loading while paused");
        Assert.That(h.Now.Revision, Is.EqualTo(paused.Revision), "nothing published while paused");
        Assert.That(h.Session(ana, ExpeditionAction.Resume).Accepted);
        h.Tick(1);
        Assert.That(h.Time, Is.GreaterThan(paused.SimulatedTimeSeconds));
    }

    [Test]
    public void OneClockEquipmentAndBaseAdvanceTogether()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Tick(37.3, 0.3);
        Assert.That(h.Equipment.Snapshot.SimulatedTimeSeconds, Is.EqualTo(h.Base.Snapshot.Power.SimulatedTimeSeconds).Within(1e-9));
        Assert.That(h.Equipment.Snapshot.Air.ActiveLoadingFraction, Is.GreaterThan(0.3), "beds load with the crew aboard");
    }

    [Test]
    public void TwoSortiesInOneSessionKeepClockStocksAndMachines()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Sortie(ana);
        Assert.That(h.Now.Outpost.State, Is.EqualTo(SessionState.Running), "a finished sortie does not end the base");
        Assert.That(h.Now.Expedition.Phase, Is.EqualTo(ExpeditionPhase.Preparing));
        Assert.That(h.Now.Session.ExpeditionsFinished, Is.EqualTo(1));
        Assert.That(h.CountActivities(ActivityKind.Expedition, ActivityStatus.Completed), Is.EqualTo(1));
        Assert.That(h.CountActivities(ActivityKind.ScienceAssignment, ActivityStatus.Completed), Is.EqualTo(1));
        foreach (ActivityRecord closed in h.Now.Activities)
            if (closed.Status != ActivityStatus.Active)
                Assert.That(closed.EndedAtSeconds, Is.Not.Null.And.GreaterThanOrEqualTo(closed.StartedAtSeconds), "a closed activity keeps its end time");
        double timeAfterFirst = h.Time;
        double loadingAfterFirst = h.Equipment.Snapshot.Air.ActiveLoadingFraction;

        h.Sortie(ana);
        Assert.That(h.Now.Outpost.State, Is.EqualTo(SessionState.Running));
        Assert.That(h.Now.Session.ExpeditionsFinished, Is.EqualTo(2));
        Assert.That(h.CountActivities(ActivityKind.Expedition, ActivityStatus.Completed), Is.EqualTo(2));
        Assert.That(h.CountActivities(ActivityKind.ScienceAssignment, ActivityStatus.Completed), Is.EqualTo(2), "science is a repeatable opportunity");
        Assert.That(h.Time, Is.GreaterThan(timeAfterFirst), "the clock is never reset between sorties");
        Assert.That(h.Equipment.Snapshot.SimulatedTimeSeconds, Is.EqualTo(h.Time).Within(1e-9));
        Assert.That(h.Equipment.Snapshot.Air.ActiveLoadingFraction, Is.Not.EqualTo(loadingAfterFirst), "machines keep running");
        Assert.That(h.Now.Epoch, Is.EqualTo(0), "no hidden restart");
    }

    [Test]
    public void PeriodicReportIsNotTerminal()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        Assert.That(h.Now.LatestReport, Is.Null);
        h.Tick(h.Scenario.ReportIntervalSeconds + 1);
        OperationsReport report = h.Now.LatestReport;
        Assert.That(report, Is.Not.Null);
        Assert.That(report.Sequence, Is.EqualTo(1));
        Assert.That(report.ToSeconds - report.FromSeconds, Is.EqualTo(h.Scenario.ReportIntervalSeconds).Within(1e-6));
        Assert.That(report.PotableDeltaKilograms, Is.LessThan(0), "the crew drinks; nothing is produced without action");
        Assert.That(h.Now.Outpost.State, Is.EqualTo(SessionState.Running));
    }

    [Test]
    public void EssentialPowerLossEndsOnlyAfterTheRecoveryWindow()
    {
        // No stored energy and generation below the essential load: the essential channel is never fully served.
        var h = new H(H.WithPower(new PowerConfiguration(0, 20000, 40, 60, 60, 100, 400)));
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Tick(10);
        Assert.That(h.Now.Outpost.State, Is.EqualTo(SessionState.Running), "phase 3 would have ended at once; phase 4 gives a window");
        Assert.That(h.Now.Session.EssentialUnservedSeconds, Is.EqualTo(10).Within(0.3));
        Assert.That(h.Now.Session.SecondsBeforeIrreversible, Is.EqualTo(50).Within(0.3));
        h.Tick(60);
        BaseOperationsSnapshot end = h.Now;
        Assert.That(end.Outpost.State, Is.EqualTo(SessionState.Interrupted));
        Assert.That(end.Session.EndCause, Is.EqualTo(IrreversibleCause.EssentialPowerLost));
        Assert.That(end.Session.EndedAtSeconds.Value, Is.EqualTo(60).Within(0.3));
        Assert.That(h.Do(ana, BaseOperationAction.Inspect, h.Scenario.WaterStation).Reason, Is.EqualTo(OutpostReasons.SessionEnded));
        Assert.That(h.Session(ana, ExpeditionAction.Restart).Accepted, "only Restart is accepted, and it is clear");
        Assert.That(h.Now.Session.EndCause, Is.EqualTo(IrreversibleCause.None));
        Assert.That(h.Now.Epoch, Is.EqualTo(1));
    }

    [Test]
    public void RestartBringsBackTheScenarioWithANewEpoch()
    {
        var h = new H(BaseOperationsFixtures.Scenario(1));
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Walk(ana, H.AtLogistics);
        Assert.That(h.PlaceOrder(ana).Accepted);
        h.Tick(10);
        Assert.That(h.Now.Orders, Has.Count.EqualTo(1));
        Assert.That(h.Equipment.Snapshot.WaterProcessor.Condition, Is.Not.EqualTo(Trainer.Simulation.BaseOperations.EquipmentCondition.Nominal));

        Assert.That(h.Session(ana, ExpeditionAction.Restart).Accepted);
        BaseOperationsSnapshot s = h.Now;
        Assert.That(s.Epoch, Is.EqualTo(1));
        Assert.That(s.Outpost.State, Is.EqualTo(SessionState.Ready));
        Assert.That(s.SimulatedTimeSeconds, Is.EqualTo(0));
        Assert.That(s.Orders, Is.Empty);
        Assert.That(s.Activities, Is.Empty);
        Assert.That(s.Session.ActivitySequence, Is.EqualTo(0));
        Assert.That(h.Equipment.Snapshot.WaterProcessor.Condition, Is.EqualTo(Trainer.Simulation.BaseOperations.EquipmentCondition.Nominal));
        Assert.That(s.EquipmentOf(Trainer.Simulation.BaseOperations.EquipmentKind.WaterProcessor).LatestObservation, Is.Null);
        Assert.That(s.Spares[0].Count, Is.EqualTo(h.Scenario.Equipment.InitialSpares[0].Count));
        Assert.That(h.Do(ana, BaseOperationAction.Inspect, h.Scenario.WaterStation).Reason, Is.EqualTo(OutpostReasons.EpochMismatch).Or.EqualTo(OutpostReasons.InvalidState));
    }

    [Test]
    public void LateJoinerReceivesTheCurrentStateNotTheInitialOne()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Walk(ana, H.AtWater);
        h.Run(ana, BaseOperationAction.ProcessBatch, h.Scenario.WaterStation);
        h.Tick(5);
        double potable = h.Now.Outpost.Resources.LifeSupport.Water.PotableKilograms;
        PlayerId ben = h.Join();
        BaseOperationsSnapshot s = h.Now;
        Assert.That(s.Outpost.TryGetCrew(ben, out _), Is.True);
        Assert.That(s.Water.HoldKilograms, Is.GreaterThan(0));
        Assert.That(s.Water.BatchesProcessed, Is.EqualTo(1));
        Assert.That(s.Outpost.Resources.LifeSupport.Water.PotableKilograms, Is.EqualTo(potable));
        Assert.That(s.SimulatedTimeSeconds, Is.GreaterThan(20));
    }

    [Test]
    public void CommandIdsShareOneSpaceAndReplaysReturnTheSameResult()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        var id = Guid.NewGuid();
        Assert.That(h.Send(ana, ExpeditionCommand.Session(id, 0, ExpeditionAction.Pause)).Accepted);
        CommandResult conflict = h.Send(ana, BaseOperationCommand.Operation(id, 0, BaseOperationAction.Inspect, h.Scenario.WaterStation));
        Assert.That(conflict.Reason, Is.EqualTo(OutpostReasons.CommandIdConflict));
        Assert.That(h.Session(ana, ExpeditionAction.Resume).Accepted);

        h.Walk(ana, H.AtWater);
        BaseOperationCommand inspect = h.Op(BaseOperationAction.Inspect, h.Scenario.WaterStation);
        CommandResult first = h.Send(ana, inspect);
        Assert.That(first.Accepted);
        long revision = h.Now.Revision;
        CommandResult replay = h.Send(ana, inspect);
        Assert.That(replay, Is.SameAs(first), "the stored result is replayed");
        Assert.That(h.Now.Revision, Is.EqualTo(revision), "a replay changes nothing");
        Assert.That(h.Send(ana, ExpeditionCommand.Session(inspect.Id, 0, ExpeditionAction.Pause)).Reason, Is.EqualTo(OutpostReasons.CommandIdConflict));
    }

    [Test]
    public void TrainingModeKeepsTheFinitePhase3Rules()
    {
        var h = new H(null, BaseSessionMode.Training);
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Walk(ana, H.AtWater);
        Assert.That(h.Do(ana, BaseOperationAction.ProcessBatch, h.Scenario.WaterStation).Accepted);
        h.Tick(h.Scenario.Expedition.Rules.MissionDurationSeconds + 5);
        BaseOperationsSnapshot end = h.Now;
        Assert.That(end.Outpost.State, Is.EqualTo(SessionState.Completed), "training ends by duration, as phase 3");
        Assert.That(end.Session.Mode, Is.EqualTo(BaseSessionMode.Training));
        Assert.That(end.Session.EndCause, Is.EqualTo(IrreversibleCause.None));
        Assert.That(end.Operations, Is.Empty, "no operation survives the end");
    }

    [Test]
    public void GaugesAreOnePerKindWithTrendsAfterAStep()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Tick(2);
        BaseOperationsSnapshot s = h.Now;
        Assert.That(s.Gauges, Has.Count.EqualTo(Enum.GetValues(typeof(GaugeKind)).Length));
        Assert.That(s.GaugeOf(GaugeKind.PotableWater).TrendPerSecond, Is.Not.Null.And.LessThan(0));
        Assert.That(s.GaugeOf(GaugeKind.PotableWater).Band, Is.EqualTo(GaugeBand.Nominal).Or.EqualTo(GaugeBand.Reserve));
        Assert.That(s.GaugeOf(GaugeKind.ProcessorHold).Reading.Value, Is.EqualTo(0));
    }
}
