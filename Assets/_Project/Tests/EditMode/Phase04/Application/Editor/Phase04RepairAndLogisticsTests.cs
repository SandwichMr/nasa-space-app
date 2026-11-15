using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Development;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;
using H = Phase04LivingBaseHarness;

/// <summary>Phase 4 D2: the synthetic fault repaired in order, spares reserved and consumed once, and resupply orders.</summary>
public sealed class Phase04RepairAndLogisticsTests
{
    private static int Spares(H h, string key)
    {
        foreach (SpareStock spare in h.Now.Spares)
            if (spare.Key == key) return spare.Count;
        return -1;
    }

    [Test]
    public void DegradedProcessorIsRepairedInspectIsolateInterveneTestRestore()
    {
        var h = new H(BaseOperationsFixtures.Scenario(5));
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Walk(ana, H.AtWater);
        h.Tick(6);
        StationId water = h.Scenario.WaterStation;
        Assert.That(h.Do(ana, BaseOperationAction.Intervene, water, OperationChoice.ReplaceProcessorFilter).Reason,
            Is.EqualTo(BaseOperationReasons.EquipmentNotIsolated), "no healing from an online machine");
        Assert.That(h.Do(ana, BaseOperationAction.TestEquipment, water).Reason, Is.EqualTo(BaseOperationReasons.EquipmentNotIsolated));

        h.Run(ana, BaseOperationAction.Inspect, water);
        EquipmentStatus seen = h.Machine(EquipmentKind.WaterProcessor);
        Assert.That(seen.LatestObservation.Findings & Findings.ProcessorFilterRestricted, Is.EqualTo(Findings.ProcessorFilterRestricted));
        Assert.That(seen.Knowledge, Is.EqualTo(EquipmentKnowledge.Degraded));

        Assert.That(h.Do(ana, BaseOperationAction.Isolate, water).Accepted);
        EquipmentStatus isolated = h.Machine(EquipmentKind.WaterProcessor);
        Assert.That(isolated.Operating, Is.EqualTo(EquipmentOperatingState.Isolated));
        Assert.That(isolated.Repair.Completed, Is.EqualTo(RepairSteps.Inspect | RepairSteps.Isolate));
        Assert.That(isolated.LatestObservation.Current, Is.True, "isolation leaves a current observation for the next step");
        Assert.That(h.Do(ana, BaseOperationAction.ProcessBatch, water).Reason, Is.EqualTo(BaseOperationReasons.EquipmentIsolated));
        Assert.That(h.Do(ana, BaseOperationAction.Inspect, water).Reason, Is.EqualTo(BaseOperationReasons.EquipmentIsolated));
        Assert.That(h.Do(ana, BaseOperationAction.TestEquipment, water).Reason, Is.EqualTo(BaseOperationReasons.RepairStepOutOfOrder));
        Assert.That(h.Do(ana, BaseOperationAction.Restore, water).Reason, Is.EqualTo(BaseOperationReasons.RepairStepOutOfOrder));
        Assert.That(h.Do(ana, BaseOperationAction.Intervene, water, OperationChoice.ReplaceInletFilter).Reason,
            Is.EqualTo(BaseOperationReasons.InterventionNotForEquipment));

        string filter = BaseOperationsFixtures.ProcessorFilter;
        Assert.That(h.Do(ana, BaseOperationAction.Intervene, water, OperationChoice.ReplaceProcessorFilter).Accepted);
        Assert.That(h.Now.FreeSpares(filter), Is.EqualTo(0), "reserved at start");
        Assert.That(Spares(h, filter), Is.EqualTo(1), "not consumed yet");
        Assert.That(h.LastDecision.Assessment, Is.EqualTo(DecisionAssessment.Appropriate));
        h.Until(() => !h.Now.TryGetOperationAt(water, out _), 60);
        Assert.That(Spares(h, filter), Is.EqualTo(0), "consumed once at completion");
        Assert.That(h.Equipment.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Nominal));
        Assert.That(h.Machine(EquipmentKind.WaterProcessor).Operating, Is.EqualTo(EquipmentOperatingState.Isolated), "still offline until restored");

        h.Run(ana, BaseOperationAction.TestEquipment, water);
        EquipmentStatus tested = h.Machine(EquipmentKind.WaterProcessor);
        Assert.That(tested.Repair.LastTestPassed, Is.True);
        double brine = h.Equipment.Snapshot.Ledger.BrineOutflowKilograms;
        Assert.That(h.Do(ana, BaseOperationAction.Restore, water).Accepted);
        EquipmentStatus restored = h.Machine(EquipmentKind.WaterProcessor);
        Assert.That(restored.Operating, Is.EqualTo(EquipmentOperatingState.Online));
        Assert.That(restored.Repair, Is.Null);
        Assert.That(restored.Knowledge, Is.EqualTo(EquipmentKnowledge.Nominal));
        Assert.That(h.CountActivities(ActivityKind.Repair, ActivityStatus.Completed), Is.EqualTo(1));
        Assert.That(h.Equipment.Snapshot.Ledger.BrineOutflowKilograms, Is.EqualTo(brine), "nothing lost comes back");
        h.Run(ana, BaseOperationAction.ProcessBatch, water);
        Assert.That(h.Now.Water.HoldKilograms, Is.EqualTo(1.8).Within(1e-9), "nominal recovery after the repair");
    }

    [Test]
    public void CancellingAnInterventionReleasesTheReservedSpareWithoutRefundingEnergy()
    {
        var h = new H(BaseOperationsFixtures.Scenario(1));
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Walk(ana, H.AtWater);
        StationId water = h.Scenario.WaterStation;
        h.Run(ana, BaseOperationAction.Inspect, water);
        Assert.That(h.Do(ana, BaseOperationAction.Isolate, water).Accepted);
        Assert.That(h.Do(ana, BaseOperationAction.Intervene, water, OperationChoice.ReplaceProcessorFilter).Accepted);
        h.Tick(5);
        Assert.That(h.Now.FreeSpares(BaseOperationsFixtures.ProcessorFilter), Is.EqualTo(0));
        Assert.That(h.Send(ana, h.Op(BaseOperationAction.CancelOperation, water)).Accepted);
        Assert.That(h.Now.FreeSpares(BaseOperationsFixtures.ProcessorFilter), Is.EqualTo(1), "reservation released");
        Assert.That(Spares(h, BaseOperationsFixtures.ProcessorFilter), Is.EqualTo(1));
        Assert.That(h.Equipment.Snapshot.WaterProcessor.Condition, Is.Not.EqualTo(EquipmentCondition.Nominal), "no effect applied");
        Assert.That(h.Machine(EquipmentKind.WaterProcessor).Repair.Completed & RepairSteps.Intervene, Is.EqualTo(RepairSteps.None));
        Assert.That(h.Send(ana, h.Op(BaseOperationAction.CancelOperation, water)).Reason, Is.EqualTo(OutpostReasons.NoActiveTask));
    }

    [Test]
    public void IsolationNeedsAFindingAndAnEmptyHold()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Walk(ana, H.AtWater);
        h.Run(ana, BaseOperationAction.Inspect, h.Scenario.WaterStation);
        Assert.That(h.Do(ana, BaseOperationAction.Isolate, h.Scenario.WaterStation).Reason, Is.EqualTo(BaseOperationReasons.NothingToRepair));
    }

    [Test]
    public void FailedProcessorRefusesBatchesAndShowsTheSymptom()
    {
        var h = new H(BaseOperationsFixtures.Scenario(1));
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Walk(ana, H.AtWater);
        h.Tick(185);
        Assert.That(h.Equipment.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Failed));
        Assert.That(h.Now.EquipmentOf(EquipmentKind.WaterProcessor).VisibleSymptoms, Is.EqualTo(Findings.None), "nobody has noticed yet");
        Assert.That(h.Do(ana, BaseOperationAction.ProcessBatch, h.Scenario.WaterStation).Reason, Is.EqualTo(BaseOperationReasons.EquipmentFailed));
        h.Tick(0.25);
        Assert.That(h.Now.EquipmentOf(EquipmentKind.WaterProcessor).VisibleSymptoms & Findings.ProcessorFailed, Is.EqualTo(Findings.ProcessorFailed));
        Assert.That(h.Now.Outpost.State, Is.EqualTo(SessionState.Running), "a failed machine is recoverable, not the end");
    }

    [Test]
    public void OrderLifecycleWithCancelWindowLeadTimeAndUnload()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Walk(ana, H.AtLogistics);
        Assert.That(h.ForOrder(ana, BaseOperationAction.CancelOrder, 1).Reason, Is.EqualTo(BaseOperationReasons.UnknownOrder));
        Assert.That(h.Send(ana, BaseOperationCommand.PlaceOrder(System.Guid.NewGuid(), 0, h.Scenario.Logistics.Console, "no-such")).Reason,
            Is.EqualTo(BaseOperationReasons.UnknownManifest));

        Assert.That(h.PlaceOrder(ana).Accepted);
        h.Until(() => h.Now.Orders.Count == 1, 10);
        Assert.That(h.Now.Orders[0].State, Is.EqualTo(OrderState.Pending));
        Assert.That(h.ForOrder(ana, BaseOperationAction.CancelOrder, 1).Accepted, "free cancellation inside the window");
        Assert.That(h.Now.Orders[0].State, Is.EqualTo(OrderState.Cancelled));
        Assert.That(h.CountActivities(ActivityKind.Resupply, ActivityStatus.Cancelled), Is.EqualTo(1));
        Assert.That(h.PlaceOrder(ana).Reason, Is.EqualTo(BaseOperationReasons.OrderTooSoon));

        h.Tick(h.Scenario.Logistics.MinSecondsBetweenOrders);
        Assert.That(h.PlaceOrder(ana).Accepted);
        h.Until(() => h.Now.Orders.Count == 2, 10);
        ResupplyOrder order = h.Now.Orders[1];
        Assert.That(h.PlaceOrder(ana).Reason, Is.EqualTo(BaseOperationReasons.OrderLimitReached));
        Assert.That(h.ForOrder(ana, BaseOperationAction.UnloadDelivery, order.Sequence).Reason, Is.EqualTo(BaseOperationReasons.OrderNotArrived));
        h.Tick(31);
        Assert.That(h.Now.Orders[1].State, Is.EqualTo(OrderState.InTransit));
        Assert.That(h.ForOrder(ana, BaseOperationAction.CancelOrder, order.Sequence).Reason, Is.EqualTo(BaseOperationReasons.OrderNotCancellable));
        h.Until(() => h.Now.Orders[1].State == OrderState.Arrived, 200);
        Assert.That(h.Time, Is.EqualTo(order.ArriveAtSeconds).Within(0.3));

        WaterStock before = h.Now.Outpost.Resources.LifeSupport.Water;
        double food = h.Now.Outpost.Resources.LifeSupport.Food.StoredKilograms;
        CommandResult unload = h.ForOrder(ana, BaseOperationAction.UnloadDelivery, order.Sequence);
        Assert.That(unload.Accepted, unload.Reason);
        h.Until(() => !h.Now.TryGetOperationAt(h.Scenario.Logistics.Console, out _), 30);
        ResupplyOrder done = h.Now.Orders[1];
        double fitPotable = System.Math.Min(10, before.PotableCapacityKilograms - before.PotableKilograms);
        Assert.That(h.Now.Outpost.Resources.LifeSupport.Food.StoredKilograms, Is.GreaterThan(food + 5.9), "food enters only through an unloaded delivery");
        Assert.That(h.Now.Outpost.Resources.LifeSupport.Water.PotableKilograms, Is.GreaterThan(before.PotableKilograms + fitPotable - 0.2));
        Assert.That(Spares(h, BaseOperationsFixtures.ProcessorFilter), Is.EqualTo(2));
        if (fitPotable >= 10 - 1e-6)
        {
            Assert.That(done.State, Is.EqualTo(OrderState.Unloaded));
            Assert.That(h.CountActivities(ActivityKind.Resupply, ActivityStatus.Completed), Is.EqualTo(1));
        }
        else Assert.That(done.State, Is.EqualTo(OrderState.Arrived), "what does not fit stays in the order");
    }
}
