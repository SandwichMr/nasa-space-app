using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using H = Phase04LivingBaseHarness;

/// <summary>Phase 4 D2: one performer per station and per member, proximity on confirmed positions, leaving and moving away.</summary>
public sealed class Phase04OccupancyAndDisconnectTests
{
    [Test]
    public void FirstToArriveHoldsTheStationAndTheMemberIsBusyElsewhere()
    {
        var h = new H();
        PlayerId ana = h.Join();
        PlayerId ben = h.Join();
        h.Start(ana);
        h.Walk(ana, H.AtWater);
        h.Walk(ben, new BasePosition(9.2, 2.4));
        Assert.That(h.Do(ana, BaseOperationAction.ProcessBatch, h.Scenario.WaterStation).Accepted);
        Assert.That(h.Do(ben, BaseOperationAction.Inspect, h.Scenario.WaterStation).Reason, Is.EqualTo(OutpostReasons.StationOccupied));
        Assert.That(h.Do(ana, BaseOperationAction.Inspect, h.Scenario.WaterStation).Reason, Is.EqualTo(OutpostReasons.PlayerBusy));
        Assert.That(h.Send(ben, h.Op(BaseOperationAction.CancelOperation, h.Scenario.WaterStation)).Reason, Is.EqualTo(OutpostReasons.NoActiveTask),
            "nobody cancels another member's operation");
        Assert.That(h.Now.Outpost.TryGetStation(h.Scenario.WaterStation, out StationState station)
            && station.Availability == StationAvailability.Unavailable, "phase 2 views see the station taken");

        // A phase 4 operation also blocks phase 2/3 tasks for its performer.
        CommandResult task = h.Send(ana, ExpeditionCommand.StartTask(System.Guid.NewGuid(), 0, ExpeditionFixtures.RackA, ExpeditionFixtures.DonA, h.Crew(ana).ZoneSequence));
        Assert.That(task.Reason, Is.EqualTo(OutpostReasons.PlayerBusy));
    }

    [Test]
    public void MovingAwayEndsTheOperationWithoutItsEffect()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Walk(ana, H.AtWater);
        double untreated = h.Now.Outpost.Resources.LifeSupport.Water.UntreatedKilograms;
        Assert.That(h.Do(ana, BaseOperationAction.ProcessBatch, h.Scenario.WaterStation).Accepted);
        h.Tick(5);
        h.Walk(ana, new BasePosition(8, 5));
        Assert.That(h.Now.TryGetOperationAt(h.Scenario.WaterStation, out _), Is.False);
        Assert.That(h.CountActivities(ActivityKind.WaterBatch, ActivityStatus.Cancelled), Is.EqualTo(1));
        Assert.That(h.Now.Water.HoldKilograms, Is.EqualTo(0));
        Assert.That(h.Now.Outpost.Resources.LifeSupport.Water.UntreatedKilograms, Is.GreaterThan(untreated - 0.01),
            "no batch was taken (untreated only grows with the crew's water return)");
    }

    [Test]
    public void LeavingCancelsTheOperationReleasesTheSpareAndKeepsOrders()
    {
        var h = new H(BaseOperationsFixtures.Scenario(1));
        PlayerId ana = h.Join();
        PlayerId ben = h.Join();
        h.Start(ana);
        h.Walk(ben, H.AtLogistics);
        Assert.That(h.PlaceOrder(ben).Accepted);
        h.Until(() => h.Now.Orders.Count == 1, 10);

        h.Walk(ana, H.AtWater);
        h.Run(ana, BaseOperationAction.Inspect, h.Scenario.WaterStation);
        Assert.That(h.Do(ana, BaseOperationAction.Isolate, h.Scenario.WaterStation).Accepted);
        Assert.That(h.Do(ana, BaseOperationAction.Intervene, h.Scenario.WaterStation, OperationChoice.ReplaceProcessorFilter).Accepted);
        h.Tick(2);
        Assert.That(h.Now.FreeSpares(BaseOperationsFixtures.ProcessorFilter), Is.EqualTo(0));

        Assert.That(h.Authority.Leave(ana), Is.True);
        BaseOperationsSnapshot s = h.Now;
        Assert.That(s.Operations, Is.Empty);
        Assert.That(s.FreeSpares(BaseOperationsFixtures.ProcessorFilter), Is.EqualTo(1));
        Assert.That(s.Outpost.TryGetCrew(ana, out _), Is.False);
        Assert.That(s.Outpost.State, Is.EqualTo(SessionState.Running), "the base goes on for the others");
        Assert.That(h.Authority.Leave(ben), Is.True);
        Assert.That(h.Now.Orders, Has.Count.EqualTo(1), "an order survives its author leaving");
        Assert.That(h.Now.Orders[0].InFlight, Is.True);
        Assert.That(h.Authority.Leave(ben), Is.False);
    }

    [Test]
    public void PausedSessionAcceptsOnlyCancellations()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Walk(ana, H.AtWater);
        Assert.That(h.Do(ana, BaseOperationAction.ProcessBatch, h.Scenario.WaterStation).Accepted);
        Assert.That(h.Session(ana, ExpeditionAction.Pause).Accepted);
        Assert.That(h.Do(ana, BaseOperationAction.Inspect, h.Scenario.AirStation).Reason, Is.EqualTo(OutpostReasons.InvalidState));
        Assert.That(h.Send(ana, h.Op(BaseOperationAction.CancelOperation, h.Scenario.WaterStation)).Accepted);
        Assert.That(h.Now.Operations, Is.Empty);
    }

    [Test]
    public void ResultsGoOnlyToTheirSender()
    {
        var h = new H();
        PlayerId ana = h.Join();
        PlayerId ben = h.Join();
        h.Start(ana);
        BaseOperationCommand command = h.Op(BaseOperationAction.Inspect, h.Scenario.WaterStation);
        h.Results.Clear();
        h.Authority.Submit(ben, command);
        Assert.That(h.Results, Has.Count.EqualTo(1));
        Assert.That(h.Results[0].Sender, Is.EqualTo(ben));
        Assert.That(h.Results[0].Result.Reason, Is.EqualTo(OutpostReasons.OutOfRange));
    }
}
