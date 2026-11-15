using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Development;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;
using H = Phase04LivingBaseHarness;

/// <summary>Phase 4 D2: the two meaningful interactions (water treatment decision, air service diagnosis) validated by the authority.</summary>
public sealed class Phase04WaterAndAirTests
{
    private static WaterStock Water(H h) => h.Now.Outpost.Resources.LifeSupport.Water;

    [Test]
    public void NominalBatchIsObservedThenAcceptedIntoPotable()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Walk(ana, H.AtWater);
        double untreated = Water(h).UntreatedKilograms;
        Assert.That(h.Do(ana, BaseOperationAction.ProcessBatch, h.Scenario.WaterStation).Accepted);
        Assert.That(h.Now.TryGetOperationAt(h.Scenario.WaterStation, out OperationProgress op) && op.Performer == ana);
        Assert.That(Water(h).UntreatedKilograms, Is.EqualTo(untreated), "nothing moves at start; the batch is applied at completion");
        h.Until(() => !h.Now.TryGetOperationAt(h.Scenario.WaterStation, out _), 60);

        BaseOperationsSnapshot s = h.Now;
        Assert.That(s.Water.HoldKilograms, Is.EqualTo(1.8).Within(1e-9), "2 kg × 0.90 nominal recovery");
        Assert.That(s.Water.ProductWithinLimit, Is.True);
        EquipmentObservation observation = s.EquipmentOf(EquipmentKind.WaterProcessor).LatestObservation;
        Assert.That(observation.Current, Is.True, "the batch reading is a new current observation");
        Assert.That(observation.TryGetReading("product-conductivity", out InstrumentReading reading) && reading.Value == 20);
        Assert.That(s.EquipmentOf(EquipmentKind.WaterProcessor).Knowledge, Is.EqualTo(EquipmentKnowledge.Nominal));

        double potable = Water(h).PotableKilograms;
        h.Run(ana, BaseOperationAction.RouteProduct, h.Scenario.WaterStation, OperationChoice.AcceptToPotable);
        Assert.That(Water(h).PotableKilograms, Is.EqualTo(potable + 1.8).Within(0.01), "product enters potable (minus a few seconds of drinking)");
        Assert.That(h.Now.Water.HoldKilograms, Is.EqualTo(0));
        Assert.That(h.Now.Water.PotableQuality, Is.EqualTo(PotableQuality.Verified));
        Assert.That(h.LastDecision.Assessment, Is.EqualTo(DecisionAssessment.Appropriate));
        Assert.That(h.LastDecision.ExplanationKey, Is.EqualTo("accepted-within-limit"));
        Assert.That(h.CountActivities(ActivityKind.WaterBatch, ActivityStatus.Completed), Is.EqualTo(1));
        Assert.That(BaseOperationsBalances.ProcessorResidualKilograms(h.Equipment.Snapshot), Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void AcceptingOffSpecProductHasABoundedRecoverableConsequence()
    {
        var h = new H(BaseOperationsFixtures.Scenario(0));
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Walk(ana, H.AtWater);
        h.Run(ana, BaseOperationAction.ProcessBatch, h.Scenario.WaterStation);
        Assert.That(h.Now.Water.HoldKilograms, Is.EqualTo(1.5).Within(1e-9), "degraded recovery 0.75");
        Assert.That(h.Now.Water.ProductWithinLimit, Is.False);
        Assert.That(h.Now.EquipmentOf(EquipmentKind.WaterProcessor).VisibleSymptoms & Findings.ProductOffSpec, Is.EqualTo(Findings.ProductOffSpec));

        h.Run(ana, BaseOperationAction.RouteProduct, h.Scenario.WaterStation, OperationChoice.AcceptToPotable);
        Assert.That(h.LastDecision.Assessment, Is.EqualTo(DecisionAssessment.Harmful));
        Assert.That(h.LastDecision.ExplanationKey, Is.EqualTo("accepted-off-spec-product"));
        Assert.That(h.Now.Water.PotableQuality, Is.EqualTo(PotableQuality.Suspect));
        Assert.That(h.Now.Water.SuspectKilograms, Is.EqualTo(1.5).Within(1e-9));
        Assert.That(h.Now.Outpost.State, Is.EqualTo(SessionState.Running), "no damage, no game over");

        double untreated = Water(h).UntreatedKilograms;
        h.Run(ana, BaseOperationAction.RecheckPotable, h.Scenario.WaterStation);
        Assert.That(h.Now.Water.PotableQuality, Is.EqualTo(PotableQuality.Verified));
        Assert.That(Water(h).UntreatedKilograms, Is.EqualTo(untreated + 1.5).Within(0.01), "the suspect mass goes back to untreated (plus the crew's water return)");
        Assert.That(h.Do(ana, BaseOperationAction.RecheckPotable, h.Scenario.WaterStation).Reason, Is.EqualTo(BaseOperationReasons.PotableNotSuspect));
    }

    [Test]
    public void ReprocessingOffSpecProductIsAppropriateAndReturnsItToUntreated()
    {
        var h = new H(BaseOperationsFixtures.Scenario(0));
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Walk(ana, H.AtWater);
        h.Run(ana, BaseOperationAction.ProcessBatch, h.Scenario.WaterStation);
        double untreated = Water(h).UntreatedKilograms;
        h.Run(ana, BaseOperationAction.RouteProduct, h.Scenario.WaterStation, OperationChoice.Reprocess);
        Assert.That(h.LastDecision.Assessment, Is.EqualTo(DecisionAssessment.Appropriate));
        Assert.That(Water(h).UntreatedKilograms, Is.EqualTo(untreated + 1.5).Within(0.01));
        Assert.That(h.Now.Water.PotableQuality, Is.EqualTo(PotableQuality.Verified));
    }

    [Test]
    public void DecisionsNeedTheCurrentObservation()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Walk(ana, H.AtAir);
        CommandResult none = h.Send(ana, h.Op(BaseOperationAction.ServiceAir, h.Scenario.AirStation, OperationChoice.SwitchBed, 1));
        Assert.That(none.Reason, Is.EqualTo(BaseOperationReasons.ObservationRequired));

        h.Walk(ana, H.AtWater);
        h.Run(ana, BaseOperationAction.ProcessBatch, h.Scenario.WaterStation);
        long seen = h.ObservationOf(EquipmentKind.WaterProcessor);
        CommandResult wrong = h.Send(ana, h.Op(BaseOperationAction.RouteProduct, h.Scenario.WaterStation, OperationChoice.AcceptToPotable, seen + 7));
        Assert.That(wrong.Reason, Is.EqualTo(BaseOperationReasons.StaleObservation));
        h.Tick(h.Scenario.ObservationValiditySeconds + 1);
        Assert.That(h.Now.EquipmentOf(EquipmentKind.WaterProcessor).LatestObservation.Current, Is.False, "the window expired");
        CommandResult stale = h.Send(ana, h.Op(BaseOperationAction.RouteProduct, h.Scenario.WaterStation, OperationChoice.AcceptToPotable, seen));
        Assert.That(stale.Reason, Is.EqualTo(BaseOperationReasons.StaleObservation));
        h.Run(ana, BaseOperationAction.Inspect, h.Scenario.WaterStation);
        Assert.That(h.Do(ana, BaseOperationAction.RouteProduct, h.Scenario.WaterStation, OperationChoice.Reprocess).Accepted, "a fresh look allows the decision");
    }

    [Test]
    public void ActionRulesRefuseImpossibleRequests()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        Assert.That(h.Do(ana, BaseOperationAction.ProcessBatch, h.Scenario.WaterStation).Reason, Is.EqualTo(OutpostReasons.OutOfRange), "from the spawn");
        h.Walk(ana, H.AtWater);
        Assert.That(h.Do(ana, BaseOperationAction.ProcessBatch, h.Scenario.AirStation).Reason, Is.EqualTo(BaseOperationReasons.WrongStationForAction));
        Assert.That(h.Do(ana, BaseOperationAction.ServiceAir, h.Scenario.WaterStation, OperationChoice.SwitchBed).Reason,
            Is.EqualTo(BaseOperationReasons.WrongStationForAction));
        Assert.That(h.Send(ana, h.Op(BaseOperationAction.Inspect, new StationId("nowhere"))).Reason, Is.EqualTo(OutpostReasons.UnknownStation));
        Assert.That(h.Do(ana, BaseOperationAction.RecheckPotable, h.Scenario.WaterStation).Reason, Is.EqualTo(BaseOperationReasons.PotableNotSuspect));
        h.Run(ana, BaseOperationAction.Inspect, h.Scenario.WaterStation);
        Assert.That(h.Do(ana, BaseOperationAction.RouteProduct, h.Scenario.WaterStation, OperationChoice.AcceptToPotable).Reason,
            Is.EqualTo(BaseOperationReasons.HoldEmpty));
        h.Run(ana, BaseOperationAction.ProcessBatch, h.Scenario.WaterStation);
        Assert.That(h.Do(ana, BaseOperationAction.ProcessBatch, h.Scenario.WaterStation).Reason, Is.EqualTo(BaseOperationReasons.HoldNotEmpty));
        Assert.That(h.Send(ana, new BaseOperationCommand(System.Guid.NewGuid(), 0, BaseOperationAction.RouteProduct, h.Scenario.WaterStation)).Reason,
            Is.EqualTo(OutpostReasons.InvalidActionValue), "shape: a decision without choice and observation");
        Assert.That(h.Send(ana, BaseOperationCommand.Operation(System.Guid.NewGuid(), 3, BaseOperationAction.Inspect, h.Scenario.WaterStation)).Reason,
            Is.EqualTo(OutpostReasons.EpochMismatch));
        Assert.That(h.Send(new PlayerId(9), h.Op(BaseOperationAction.Inspect, h.Scenario.WaterStation)).Reason, Is.EqualTo(OutpostReasons.NotJoined));
    }

    [Test]
    public void SaturatedBedIsDiagnosedAndSwitchingRestoresRemoval()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Walk(ana, H.AtAir);
        h.Run(ana, BaseOperationAction.Inspect, h.Scenario.AirStation);
        Assert.That(h.Do(ana, BaseOperationAction.ServiceAir, h.Scenario.AirStation, OperationChoice.SwitchBed).Accepted);
        Assert.That(h.LastDecision.ExplanationKey, Is.EqualTo("bed-switch-not-needed"));
        Assert.That(h.LastDecision.Assessment, Is.EqualTo(DecisionAssessment.NotIndicated));
        h.Until(() => !h.Now.TryGetOperationAt(h.Scenario.AirStation, out _), 30);

        // The new active bed started empty; it loads with one crew member until above the knee.
        h.Until(() => h.Equipment.Snapshot.Air.ActiveLoadingFraction > 0.65, 400, 1);
        double reduced = h.Equipment.Snapshot.Air.RemovalFactor;
        Assert.That(reduced, Is.LessThan(1));
        h.Run(ana, BaseOperationAction.Inspect, h.Scenario.AirStation);
        EquipmentObservation seen = h.Now.EquipmentOf(EquipmentKind.AirRevitalization).LatestObservation;
        Assert.That(seen.Findings & Findings.ActiveBedSaturated, Is.EqualTo(Findings.ActiveBedSaturated));
        Assert.That(seen.Findings & Findings.StandbyBedLoaded, Is.EqualTo(Findings.None), "the first bed is below its knee");
        Assert.That(h.Do(ana, BaseOperationAction.Isolate, h.Scenario.AirStation).Reason, Is.EqualTo(BaseOperationReasons.NothingToRepair),
            "a saturated bed is serviced, not repaired");

        h.Run(ana, BaseOperationAction.ServiceAir, h.Scenario.AirStation, OperationChoice.SwitchBed);
        DecisionRecord decision = h.LastDecision;
        Assert.That(decision.Assessment, Is.EqualTo(DecisionAssessment.Appropriate));
        Assert.That(decision.ExplanationKey, Is.EqualTo("bed-switch-restored-removal"));
        Assert.That(h.Equipment.Snapshot.Air.RemovalFactor, Is.GreaterThan(reduced));
        h.Tick(0.25);
        Assert.That(((IAirServiceCapacity)h.Base).CarbonDioxideRemovalFactor, Is.EqualTo(h.Equipment.Snapshot.Air.RemovalFactor).Within(1e-9),
            "the base applies the equipment factor on the next step");
        Assert.That(h.CountActivities(ActivityKind.AirService, ActivityStatus.Completed), Is.EqualTo(2));

        h.Run(ana, BaseOperationAction.Inspect, h.Scenario.AirStation);
        h.Run(ana, BaseOperationAction.ServiceAir, h.Scenario.AirStation, OperationChoice.RegenerateStandbyBed);
        Assert.That(h.LastDecision.Assessment, Is.EqualTo(DecisionAssessment.Appropriate), "the standby bed was loaded");
        Assert.That(h.Equipment.Snapshot.Air.StandbyLoadingFraction, Is.EqualTo(0));
    }
}
