using System;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Development;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;

/// <summary>Phase 4 common base: value types refuse invalid input and the shared rules (bands, repair order, command shape) hold.</summary>
public sealed class Phase04ValueContractTests
{
    private static readonly StationId Water = BaseOperationsFixtures.WaterStation;

    [Test]
    public void InstrumentReadingFollowsTheGasReadingQualityRules()
    {
        Assert.Throws<ArgumentException>(() => new InstrumentReading(ReadingStatus.Unavailable, ReadingUnit.Kilograms, 1, null));
        Assert.Throws<ArgumentException>(() => new InstrumentReading(ReadingStatus.Valid, ReadingUnit.Kilograms, 1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => InstrumentReading.Valid(ReadingUnit.Kilograms, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => InstrumentReading.Valid(ReadingUnit.Fraction, 1.5, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => InstrumentReading.Valid(ReadingUnit.Kilograms, double.NaN, 0));
        Assert.That(InstrumentReading.Valid(ReadingUnit.Watts, -20, 3).Value, Is.EqualTo(-20), "power balance may be negative");
        InstrumentReading stale = InstrumentReading.Valid(ReadingUnit.Kilograms, 2, 5).AsStale();
        Assert.That(stale.Status, Is.EqualTo(ReadingStatus.Stale));
        Assert.That(stale.Value, Is.EqualTo(2));
        Assert.That(InstrumentReading.Unavailable(ReadingUnit.Kilograms).AsStale().Status, Is.EqualTo(ReadingStatus.Unavailable));
    }

    [Test]
    public void WaterProcessorConfigurationMakesTheDecisionObservable()
    {
        Assert.DoesNotThrow(() => new WaterProcessorConfiguration(2, 2, 0.9, 0.75, 20, 80, 40, 120, 180, "processor-filter"));
        Assert.Throws<ArgumentException>(() => new WaterProcessorConfiguration(2, 1, 0.9, 0.75, 20, 80, 40, null, null, "f"), "hold fits a batch");
        Assert.Throws<ArgumentException>(() => new WaterProcessorConfiguration(2, 2, 0.7, 0.75, 20, 80, 40, null, null, "f"), "degraded <= nominal");
        Assert.Throws<ArgumentException>(() => new WaterProcessorConfiguration(2, 2, 0.9, 0.75, 20, 30, 40, null, null, "f"), "degraded must be off-spec");
        Assert.Throws<ArgumentException>(() => new WaterProcessorConfiguration(2, 2, 0.9, 0.75, 50, 80, 40, null, null, "f"), "nominal must be in spec");
        Assert.Throws<ArgumentException>(() => new WaterProcessorConfiguration(2, 2, 0.9, 0.75, 20, 80, 40, null, null, "Bad Key"));
    }

    [Test]
    public void AirBedFactorIsOneUntilTheKneeThenLinear()
    {
        var air = new AirRevitalizationConfiguration(0.002, 0.6, 0.2, 0.3, 0, null, 0.5, "inlet-filter");
        Assert.That(air.BedFactor(0.0), Is.EqualTo(1));
        Assert.That(air.BedFactor(0.6), Is.EqualTo(1));
        Assert.That(air.BedFactor(0.8), Is.EqualTo(0.6).Within(1e-12));
        Assert.That(air.BedFactor(1.0), Is.EqualTo(0.2).Within(1e-12));
        Assert.Throws<ArgumentOutOfRangeException>(() => air.BedFactor(1.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AirRevitalizationConfiguration(0.002, 1.0, 0.2, 0.3, 0, null, 0.5, "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AirRevitalizationConfiguration(0.002, 0.6, 0.2, 0.3, 0, null, 1.0, "x"));
    }

    [Test]
    public void EquipmentConfigurationNeedsAStockEntryForEverySpareItUses()
    {
        var water = new WaterProcessorConfiguration(2, 2, 0.9, 0.75, 20, 80, 40, null, null, "processor-filter");
        var air = new AirRevitalizationConfiguration(0.002, 0.6, 0.2, 0.3, 0, null, 0.5, "inlet-filter");
        Assert.Throws<ArgumentException>(() => new BaseEquipmentConfiguration(water, air, new[] { new SpareStock("processor-filter", 0, 2) }, "x"));
        Assert.Throws<ArgumentException>(() => new BaseEquipmentConfiguration(water, air,
            new[] { new SpareStock("processor-filter", 0, 2), new SpareStock("processor-filter", 1, 2) }, "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpareStock("inlet-filter", 3, 2));
        Assert.Throws<ArgumentException>(() => new BaseEquipmentConfiguration(water, air,
            new[] { new SpareStock("processor-filter", 0, 2), new SpareStock("inlet-filter", 0, 2) }, " "));
    }

    [Test]
    public void EquipmentStatesKeepConditionFaultAndIsolationConsistent()
    {
        InstrumentReading none = InstrumentReading.Unavailable(ReadingUnit.MicrosiemensPerCentimeter);
        Assert.DoesNotThrow(() => new WaterProcessorState(EquipmentCondition.Nominal, EquipmentOperatingState.Online, EquipmentFault.None, 0, none, null));
        Assert.Throws<ArgumentException>(() => new WaterProcessorState(EquipmentCondition.Degraded, EquipmentOperatingState.Online, EquipmentFault.None, 0, none, 3));
        Assert.Throws<ArgumentException>(() => new WaterProcessorState(EquipmentCondition.Degraded, EquipmentOperatingState.Online,
            EquipmentFault.ProcessorFilterRestricted, 0, none, null));
        Assert.Throws<ArgumentException>(() => new WaterProcessorState(EquipmentCondition.Nominal, EquipmentOperatingState.Online, EquipmentFault.None, 0,
            InstrumentReading.Unavailable(ReadingUnit.Kilograms), null));
        Assert.Throws<ArgumentException>(() => new AirRevitalizationState(EquipmentOperatingState.Isolated, SorbentBed.A, 0.5, 0, 1, 0.4));
        Assert.DoesNotThrow(() => new AirRevitalizationState(EquipmentOperatingState.Isolated, SorbentBed.A, 0.5, 0, 1, 0));
    }

    [Test]
    public void EquipmentEffectsCheckTheirShape()
    {
        Guid id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => EquipmentEffect.ReceiveBatch(Guid.Empty, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => EquipmentEffect.ReceiveBatch(id, 0));
        Assert.Throws<ArgumentException>(() => EquipmentEffect.ReceiveSpares(id, "Bad", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => EquipmentEffect.ReceiveSpares(id, "inlet-filter", 0));
        EquipmentEffect a = EquipmentEffect.ReleaseToPotable(id, 1.8);
        Assert.That(a.HasSameContent(EquipmentEffect.ReleaseToPotable(id, 1.8)), Is.True);
        Assert.That(a.HasSameContent(EquipmentEffect.ReleaseForReprocess(id, 1.8)), Is.False);
        Assert.That(EquipmentEffect.SwitchBed(id).Equipment, Is.EqualTo(EquipmentKind.AirRevitalization));
    }

    [Test]
    public void GaugeBandsSeparateUnknownReserveExhaustedLimitAndDeficit()
    {
        InstrumentReading kg(double v) => InstrumentReading.Valid(ReadingUnit.Kilograms, v, 1);
        Assert.That(GaugeBands.Classify(GaugeKind.PotableWater, InstrumentReading.Unavailable(ReadingUnit.Kilograms), 8, null), Is.EqualTo(GaugeBand.Unknown));
        Assert.That(GaugeBands.Classify(GaugeKind.PotableWater, kg(0), 8, null), Is.EqualTo(GaugeBand.Exhausted));
        Assert.That(GaugeBands.Classify(GaugeKind.PotableWater, kg(8), 8, null), Is.EqualTo(GaugeBand.Reserve));
        Assert.That(GaugeBands.Classify(GaugeKind.PotableWater, kg(8.01), 8, null), Is.EqualTo(GaugeBand.Nominal));
        Assert.That(GaugeBands.Classify(GaugeKind.PotableWater, kg(3).AsStale(), 8, null), Is.EqualTo(GaugeBand.Reserve), "stale keeps its last value");
        Assert.That(GaugeBands.Classify(GaugeKind.ProcessorHold, kg(0), null, null), Is.EqualTo(GaugeBand.Nominal), "an empty hold is not a shortage");
        Assert.That(GaugeBands.Classify(GaugeKind.CabinCarbonDioxide, InstrumentReading.Valid(ReadingUnit.Kilopascals, 0.71, 1), null, 0.7),
            Is.EqualTo(GaugeBand.AboveLimit));
        Assert.That(GaugeBands.Classify(GaugeKind.PowerBalance, InstrumentReading.Valid(ReadingUnit.Watts, -1, 1), null, null), Is.EqualTo(GaugeBand.Deficit));
        Assert.Throws<ArgumentException>(() => new GaugeRule(GaugeKind.CabinCarbonDioxide, 0.5, null));
        Assert.Throws<ArgumentException>(() => new GaugeRule(GaugeKind.PotableWater, 8, 30));
        Assert.Throws<ArgumentException>(() => new GaugeRule(GaugeKind.PowerBalance, 0, null));
    }

    [Test]
    public void ResourceGaugeEnforcesUnitCapacityAndTheSharedBand()
    {
        var rule = new GaugeRule(GaugeKind.PotableWater, 8, null);
        Assert.That(ResourceGauge.Of(rule, InstrumentReading.Valid(ReadingUnit.Kilograms, 5, 2), 40, -0.01).Band, Is.EqualTo(GaugeBand.Reserve));
        Assert.Throws<ArgumentException>(() => new ResourceGauge(GaugeKind.PotableWater, InstrumentReading.Valid(ReadingUnit.Kilograms, 5, 2), 40, null, 8, null,
            GaugeBand.Nominal), "band must follow the rule");
        Assert.Throws<ArgumentException>(() => ResourceGauge.Of(rule, InstrumentReading.Valid(ReadingUnit.Joules, 5, 2), 40, null), "unit");
        Assert.Throws<ArgumentException>(() => ResourceGauge.Of(rule, InstrumentReading.Valid(ReadingUnit.Kilograms, 50, 2), 40, null), "capacity");
    }

    [Test]
    public void RepairSequenceIsInspectIsolateInterveneTestRestore()
    {
        const RepairSteps i = RepairSteps.Inspect, iso = RepairSteps.Isolate, iv = RepairSteps.Intervene, t = RepairSteps.Test;
        Assert.That(RepairSequence.IsAllowed(RepairSteps.None, RepairSteps.Isolate, null), Is.False, "inspect first");
        Assert.That(RepairSequence.IsAllowed(i, RepairSteps.Isolate, null), Is.True);
        Assert.That(RepairSequence.IsAllowed(i, RepairSteps.Intervene, null), Is.False, "isolate before intervening");
        Assert.That(RepairSequence.IsAllowed(i | iso, RepairSteps.Intervene, null), Is.True);
        Assert.That(RepairSequence.IsAllowed(i | iso, RepairSteps.Restore, null), Is.False, "no restore without a test");
        Assert.That(RepairSequence.IsAllowed(i | iso | iv, RepairSteps.Test, null), Is.True);
        Assert.That(RepairSequence.IsAllowed(i | iso | iv | t, RepairSteps.Restore, false), Is.False, "failed test");
        Assert.That(RepairSequence.IsAllowed(i | iso | iv | t, RepairSteps.Intervene, false), Is.True, "intervene again after a failed test");
        Assert.That(RepairSequence.IsAllowed(i | iso | iv | t, RepairSteps.Restore, true), Is.True);
        Assert.That(RepairSequence.IsAllowed(i | iso, RepairSteps.Inspect, null), Is.False, "while isolated the check is Test");
    }

    [Test]
    public void RepairJobAndEquipmentStatusAgreeWithIsolation()
    {
        Assert.Throws<ArgumentException>(() => new RepairJob(EquipmentKind.WaterProcessor, RepairSteps.Inspect, 0, 1, null), "starts at Isolate");
        Assert.Throws<ArgumentException>(() => new RepairJob(EquipmentKind.WaterProcessor, RepairSteps.Inspect | RepairSteps.Isolate, 0, 1, true));
        var job = new RepairJob(EquipmentKind.WaterProcessor, RepairSteps.Inspect | RepairSteps.Isolate, 0, 1, null);
        Assert.Throws<ArgumentException>(() => new EquipmentStatus(EquipmentKind.WaterProcessor, Water, EquipmentOperatingState.Online,
            EquipmentKnowledge.Degraded, Findings.None, 2, null, job), "online machine has no open job");
        Assert.Throws<ArgumentException>(() => new EquipmentStatus(EquipmentKind.WaterProcessor, Water, EquipmentOperatingState.Isolated,
            EquipmentKnowledge.Degraded, Findings.None, 2, null, null), "isolated machine has a job");
        var stale = new EquipmentObservation(1, EquipmentKind.WaterProcessor, new PlayerId(1), 10, 70, 1, Findings.ProcessorFilterRestricted,
            Array.Empty<ObservedReading>(), true);
        Assert.Throws<ArgumentException>(() => new EquipmentStatus(EquipmentKind.WaterProcessor, Water, EquipmentOperatingState.Isolated,
            EquipmentKnowledge.Degraded, Findings.None, 2, stale, job), "a current observation matches the state sequence");
    }

    [Test]
    public void ObservationsRefuseFindingsOfTheOtherMachineAndFutureReadings()
    {
        Assert.Throws<ArgumentException>(() => new EquipmentObservation(1, EquipmentKind.WaterProcessor, new PlayerId(1), 10, 70, 0,
            Findings.ActiveBedSaturated, Array.Empty<ObservedReading>(), true));
        Assert.Throws<ArgumentException>(() => new EquipmentObservation(1, EquipmentKind.AirRevitalization, new PlayerId(1), 10, 70, 0, Findings.None,
            new[] { new ObservedReading("inlet-flow", InstrumentReading.Valid(ReadingUnit.Fraction, 1, 11)) }, true));
        Assert.Throws<ArgumentException>(() => new EquipmentObservation(1, EquipmentKind.AirRevitalization, new PlayerId(1), 10, 10, 0, Findings.None,
            Array.Empty<ObservedReading>(), true), "positive validity window");
    }

    [Test]
    public void WaterTreatmentSuspectExactlyWithSuspectMass()
    {
        InstrumentReading reading = InstrumentReading.Valid(ReadingUnit.MicrosiemensPerCentimeter, 80, 5);
        Assert.Throws<ArgumentException>(() => new WaterTreatmentStatus(1.5, reading, 40, PotableQuality.Suspect, 0, 1));
        Assert.Throws<ArgumentException>(() => new WaterTreatmentStatus(1.5, reading, 40, PotableQuality.Verified, 1.5, 1));
        Assert.That(new WaterTreatmentStatus(1.5, reading, 40, PotableQuality.Verified, 0, 1).ProductWithinLimit, Is.False);
        Assert.That(new WaterTreatmentStatus(1.8, InstrumentReading.Valid(ReadingUnit.MicrosiemensPerCentimeter, 20, 5), 40,
            PotableQuality.Verified, 0, 1).ProductWithinLimit, Is.True);
        Assert.That(new WaterTreatmentStatus(1.8, InstrumentReading.Valid(ReadingUnit.MicrosiemensPerCentimeter, 20, 5).AsStale(), 40,
            PotableQuality.Verified, 0, 1).ProductWithinLimit, Is.False, "a stale reading does not justify accepting");
    }

    [Test]
    public void CommandShapeRequiresStationChoiceOrderAndObservationWhereTheyApply()
    {
        Guid id = Guid.NewGuid();
        Assert.That(BaseOperationCommand.Operation(id, 0, BaseOperationAction.Inspect, Water).ShapeError(), Is.Null);
        Assert.That(BaseOperationCommand.Operation(Guid.Empty, 0, BaseOperationAction.Inspect, Water).ShapeError(), Is.EqualTo(OutpostReasons.InvalidCommandId));
        Assert.That(new BaseOperationCommand(id, 0, (BaseOperationAction)99, Water).ShapeError(), Is.EqualTo(OutpostReasons.UnknownAction));
        Assert.That(new BaseOperationCommand(id, 0, BaseOperationAction.Inspect, StationId.None).ShapeError(), Is.EqualTo(OutpostReasons.InvalidActionValue));
        Assert.That(BaseOperationCommand.Operation(id, 0, BaseOperationAction.RouteProduct, Water, OperationChoice.AcceptToPotable).ShapeError(),
            Is.EqualTo(OutpostReasons.InvalidActionValue), "decision without observation");
        Assert.That(BaseOperationCommand.Operation(id, 0, BaseOperationAction.RouteProduct, Water, OperationChoice.AcceptToPotable, 3).ShapeError(), Is.Null);
        Assert.That(BaseOperationCommand.Operation(id, 0, BaseOperationAction.RouteProduct, Water, OperationChoice.SwitchBed, 3).ShapeError(),
            Is.EqualTo(OutpostReasons.InvalidActionValue), "choice of another action");
        Assert.That(BaseOperationCommand.Operation(id, 0, BaseOperationAction.Inspect, Water, OperationChoice.None, 3).ShapeError(),
            Is.EqualTo(OutpostReasons.InvalidActionValue), "observation where none applies");
        Assert.That(BaseOperationCommand.PlaceOrder(id, 0, BaseOperationsFixtures.LogisticsConsole, "standard-resupply").ShapeError(), Is.Null);
        Assert.That(BaseOperationCommand.PlaceOrder(id, 0, BaseOperationsFixtures.LogisticsConsole, "Bad Key").ShapeError(), Is.EqualTo(OutpostReasons.InvalidActionValue));
        Assert.That(BaseOperationCommand.ForOrder(id, 0, BaseOperationAction.CancelOrder, BaseOperationsFixtures.LogisticsConsole, 0).ShapeError(),
            Is.EqualTo(OutpostReasons.InvalidActionValue));
        Assert.That(BaseOperationCommand.ForOrder(id, 0, BaseOperationAction.UnloadDelivery, BaseOperationsFixtures.LogisticsConsole, 1).ShapeError(), Is.Null);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BaseOperationCommand(id, -1, BaseOperationAction.Inspect, Water));
    }

    [Test]
    public void CommandContentComparisonCoversEveryField()
    {
        Guid id = Guid.NewGuid();
        var a = BaseOperationCommand.Operation(id, 2, BaseOperationAction.ServiceAir, BaseOperationsFixtures.AirStation, OperationChoice.SwitchBed, 4, 1);
        Assert.That(a.HasSameContent(BaseOperationCommand.Operation(id, 2, BaseOperationAction.ServiceAir, BaseOperationsFixtures.AirStation,
            OperationChoice.SwitchBed, 4, 1)), Is.True);
        Assert.That(a.HasSameContent(BaseOperationCommand.Operation(id, 2, BaseOperationAction.ServiceAir, BaseOperationsFixtures.AirStation,
            OperationChoice.RegenerateStandbyBed, 4, 1)), Is.False);
        Assert.That(a.HasSameContent(BaseOperationCommand.Operation(id, 2, BaseOperationAction.ServiceAir, BaseOperationsFixtures.AirStation,
            OperationChoice.SwitchBed, 5, 1)), Is.False);
    }

    [Test]
    public void ManifestsAndOrdersFollowTheirLifeCycle()
    {
        Assert.Throws<ArgumentException>(() => ManifestItem.Mass(ResourceKind.UntreatedWater, 5), "untreated is not delivered");
        Assert.Throws<ArgumentException>(() => new ResupplyManifest("m", new[] { ManifestItem.Mass(ResourceKind.StoredFood, 1),
            ManifestItem.Mass(ResourceKind.StoredFood, 2) }, 100, 10), "one line per resource");
        Assert.Throws<ArgumentException>(() => new ResupplyManifest("m", new[] { ManifestItem.Mass(ResourceKind.StoredFood, 1) }, 100, 100));
        ManifestItem[] items = { ManifestItem.Mass(ResourceKind.StoredFood, 6) };
        var p = new PlayerId(1);
        Assert.DoesNotThrow(() => new ResupplyOrder(1, "m", p, 10, 40, 190, OrderState.Pending, items, null));
        Assert.Throws<ArgumentException>(() => new ResupplyOrder(1, "m", p, 10, 40, 40, OrderState.Pending, items, null), "arrival after commit");
        Assert.Throws<ArgumentException>(() => new ResupplyOrder(1, "m", p, 10, 40, 190, OrderState.Unloaded, items, 200), "unloaded carries nothing");
        Assert.Throws<ArgumentException>(() => new ResupplyOrder(1, "m", p, 10, 40, 190, OrderState.Arrived, Array.Empty<ManifestItem>(), null));
        Assert.Throws<ArgumentException>(() => new ResupplyOrder(1, "m", p, 10, 40, 190, OrderState.Cancelled, items, 50), "cancel only while pending");
        Assert.DoesNotThrow(() => new ResupplyOrder(1, "m", p, 10, 40, 190, OrderState.Cancelled, items, 30));
        Assert.That(new ResupplyOrder(1, "m", p, 10, 40, 190, OrderState.Arrived, items, null).InFlight, Is.True);
    }

    [Test]
    public void SessionStatusSeparatesTheGlobalCycleFromActivities()
    {
        var rule = new IrreversibleRule(60);
        BaseSessionStatus start = BaseSessionStatus.Start(BaseSessionMode.Continuous, rule);
        Assert.That(start.SecondsBeforeIrreversible, Is.EqualTo(60));
        Assert.Throws<ArgumentException>(() => new BaseSessionStatus(BaseSessionMode.Continuous, 10, 60, IrreversibleCause.EssentialPowerLost, 100, 0, 0),
            "power loss needs the whole window");
        Assert.Throws<ArgumentException>(() => new BaseSessionStatus(BaseSessionMode.Continuous, 0, 60, IrreversibleCause.None, 100, 0, 0));
        Assert.DoesNotThrow(() => new BaseSessionStatus(BaseSessionMode.Continuous, 60, 60, IrreversibleCause.EssentialPowerLost, 100, 0, 0));
        Assert.Throws<ArgumentException>(() => new ActivityRecord(1, ActivityKind.Expedition, "expedition", ActivityStatus.Completed, p(1), 5, null, null));
        Assert.Throws<ArgumentException>(() => new ActivityRecord(1, ActivityKind.Expedition, "expedition", ActivityStatus.Active, p(1), 5, 9, null));
        Assert.Throws<ArgumentException>(() => new ActivityRecord(1, ActivityKind.Expedition, "expedition", ActivityStatus.Completed, p(1), 5, 4, null));
        Assert.Throws<ArgumentException>(() => new OperationsReport(1, 120, 120, 0, 0, 0, 0, 0));
        PlayerId p(int v) => new PlayerId(v);
    }

    [Test]
    public void OperationDefinitionsAndScenarioRefuseMissingOrAmbiguousParts()
    {
        Assert.Throws<ArgumentException>(() => new OperationDefinition(BaseOperationAction.CancelOperation, OperationChoice.None, 0, 0));
        Assert.Throws<ArgumentException>(() => new OperationDefinition(BaseOperationAction.Isolate, OperationChoice.None, 0, 10), "immediate draws no power");
        Assert.Throws<ArgumentException>(() => new OperationDefinition(BaseOperationAction.ServiceAir, OperationChoice.Reprocess, 3, 0));
        BaseOperationsScenario scenario = BaseOperationsFixtures.Scenario();
        Assert.That(scenario.EquipmentAt(BaseOperationsFixtures.WaterStation), Is.EqualTo(EquipmentKind.WaterProcessor));
        Assert.That(scenario.EquipmentAt(BaseOperationsFixtures.LogisticsConsole), Is.Null);
        Assert.That(scenario.DefinitionOf(BaseOperationAction.ProcessBatch, OperationChoice.None).PowerWatts, Is.EqualTo(150));
        Assert.That(scenario.GaugeRuleOf(GaugeKind.CabinCarbonDioxide).LimitAt, Is.EqualTo(0.7));
        foreach (var task in scenario.Expedition.Outpost.Tasks)
            Assert.That(task.Station, Is.Not.EqualTo(BaseOperationsFixtures.WaterStation), "one meaning per station");

        var ops = new System.Collections.Generic.List<OperationDefinition>(BaseOperationsFixtures.Operations());
        ops.RemoveAt(1);
        Assert.Throws<ArgumentException>(() => new BaseOperationsScenario(scenario.Expedition, scenario.Equipment, scenario.WaterStation,
            scenario.AirStation, ops, 60, scenario.Logistics, scenario.Manifests, scenario.Gauges, scenario.Irreversible, 120, scenario.Science, "x"));
        Assert.Throws<ArgumentException>(() => new BaseOperationsScenario(scenario.Expedition, scenario.Equipment, scenario.WaterStation,
            scenario.WaterStation, BaseOperationsFixtures.Operations(), 60, scenario.Logistics, scenario.Manifests, scenario.Gauges,
            scenario.Irreversible, 120, scenario.Science, "x"), "distinct stations");
        Assert.Throws<ArgumentException>(() => new BaseOperationsScenario(scenario.Expedition, scenario.Equipment, scenario.WaterStation,
            scenario.AirStation, BaseOperationsFixtures.Operations(), 60, scenario.Logistics,
            new[] { new ResupplyManifest("m", new[] { ManifestItem.Spares("unknown-part", 1) }, 100, 10) }, scenario.Gauges,
            scenario.Irreversible, 120, scenario.Science, "x"), "unknown spare");
        Assert.Throws<ArgumentException>(() => new BaseOperationsScenario(scenario.Expedition, scenario.Equipment, scenario.WaterStation,
            scenario.AirStation, BaseOperationsFixtures.Operations(), 60, scenario.Logistics, scenario.Manifests,
            new[] { new GaugeRule(GaugeKind.PotableWater, 8, null) }, scenario.Irreversible, 120, scenario.Science, "x"), "one rule per gauge");
    }
}
