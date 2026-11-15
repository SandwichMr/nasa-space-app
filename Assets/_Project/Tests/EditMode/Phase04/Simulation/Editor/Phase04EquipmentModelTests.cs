using System;
using NUnit.Framework;
using Trainer.Simulation;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;
using static Trainer.Tests.Simulation.Phase04.Phase04SimulationFixtures;

namespace Trainer.Tests.Simulation.Phase04
{
    /// <summary>D1 phase 4: <see cref="BaseEquipmentSimulation"/> (processor hold, beds, spares, scripted fault, effects).</summary>
    public sealed class Phase04EquipmentModelTests
    {
        // ------------------------------------------------------------ reset and input

        [Test] public void RequiresResetAndRejectsNullConfiguration()
        {
            var model = new BaseEquipmentSimulation();
            Assert.Throws<InvalidOperationException>(() => { _ = model.Snapshot; });
            Assert.Throws<InvalidOperationException>(() => model.Advance(1, Crew(1)));
            Assert.Throws<InvalidOperationException>(() => model.SecondsUntilEquipmentEvent(Crew(1)));
            Assert.Throws<InvalidOperationException>(() => model.Apply(EquipmentEffect.SwitchBed(Id(1))));
            Assert.Throws<ArgumentNullException>(() => model.Reset(null));
        }

        [Test] public void ResetPublishesNominalOnlineEmptyHoldAndUnknownProductQuality()
        {
            BaseEquipmentSnapshot s = StartedEquipment().Snapshot;
            Assert.That(s.SimulatedTimeSeconds, Is.EqualTo(0));
            Assert.That(s.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Nominal));
            Assert.That(s.WaterProcessor.Operating, Is.EqualTo(EquipmentOperatingState.Online));
            Assert.That(s.WaterProcessor.Fault, Is.EqualTo(EquipmentFault.None));
            Assert.That(s.WaterProcessor.HoldKilograms, Is.EqualTo(0));
            // Nothing measured yet: Unavailable without a value, never a zero reading.
            Assert.That(s.WaterProcessor.ProductConductivity.Status, Is.EqualTo(ReadingStatus.Unavailable));
            Assert.That(s.WaterProcessor.ProductConductivity.Value, Is.Null);
            Assert.That(s.Air.ActiveBed, Is.EqualTo(SorbentBed.A));
            Assert.That(s.Air.ActiveLoadingFraction, Is.EqualTo(0.3));
            Assert.That(s.Air.StandbyLoadingFraction, Is.EqualTo(0));
            Assert.That(s.Air.InletFlowFraction, Is.EqualTo(1));
            Assert.That(s.Air.RemovalFactor, Is.EqualTo(1), "below the knee the bed removes at full capacity");
            Assert.That(SpareCount(s, ProcessorFilter), Is.EqualTo(1));
            Assert.That(SpareCount(s, InletFilter), Is.EqualTo(1));
            Assert.That(s.Ledger.BatchInputKilograms, Is.EqualTo(0));
            Assert.That(s.Ledger.SparesConsumed, Is.EqualTo(0));
        }

        [Test] public void ZeroAdvanceIsANoOpAndInvalidInputThrowsWithoutChangingState()
        {
            BaseEquipmentSimulation model = StartedEquipment();
            BaseEquipmentSnapshot before = model.Snapshot;
            model.Advance(0, Crew(4));
            Assert.That(model.Snapshot, Is.SameAs(before));
            Assert.Throws<ArgumentOutOfRangeException>(() => model.Advance(-1, Crew(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => model.Advance(double.NaN, Crew(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => model.Advance(double.PositiveInfinity, Crew(1)));
            Assert.Throws<ArgumentNullException>(() => model.Advance(1, null));
            Assert.Throws<ArgumentNullException>(() => model.SecondsUntilEquipmentEvent(null));
            Assert.Throws<ArgumentNullException>(() => model.Apply(null));
            Assert.That(model.Snapshot, Is.SameAs(before));
        }

        // ------------------------------------------------------------ beds, events and dt partition

        [Test] public void ActiveBedLoadsWithCrewAboardAndCrossesTheKneeAtTheAnnouncedTime()
        {
            BaseEquipmentSimulation model = StartedEquipment(Equipment(degradeAt: null));
            // 0.3 + 0.002 * 2 * t reaches the 0.6 knee at t = 75 s and full loading at t = 175 s.
            Assert.That(model.SecondsUntilEquipmentEvent(Crew(2)), Is.EqualTo(75).Within(Tolerance));
            Assert.That(model.SecondsUntilEquipmentEvent(Crew(0)), Is.EqualTo(double.PositiveInfinity), "no crew aboard, no loading");
            model.Advance(75, Crew(2));
            Assert.That(model.Snapshot.Air.ActiveLoadingFraction, Is.EqualTo(0.6).Within(Tolerance));
            Assert.That(model.Snapshot.Air.RemovalFactor, Is.EqualTo(1).Within(Tolerance));
            Assert.That(model.SecondsUntilEquipmentEvent(Crew(2)), Is.EqualTo(100).Within(Tolerance));
            model.Advance(50, Crew(2)); // loading 0.8: factor 1 - (0.2 / 0.4) * (1 - 0.2) = 0.6
            Assert.That(model.Snapshot.Air.ActiveLoadingFraction, Is.EqualTo(0.8).Within(Tolerance));
            Assert.That(model.Snapshot.Air.RemovalFactor, Is.EqualTo(0.6).Within(Tolerance));
            model.Advance(500, Crew(2));
            Assert.That(model.Snapshot.Air.ActiveLoadingFraction, Is.EqualTo(1), "loading is capped at full");
            Assert.That(model.Snapshot.Air.RemovalFactor, Is.EqualTo(0.2).Within(Tolerance));
            Assert.That(model.Snapshot.Air.StandbyLoadingFraction, Is.EqualTo(0), "the standby bed does not load");
            Assert.That(model.SecondsUntilEquipmentEvent(Crew(2)), Is.EqualTo(double.PositiveInfinity));
        }

        [Test] public void ResultsDoNotDependOnHowTheIntervalIsSplit()
        {
            BaseEquipmentSimulation whole = StartedEquipment(), split = StartedEquipment();
            whole.Advance(400, Crew(3));
            for (int i = 0; i < 160; i++) split.Advance(2.5, Crew(3));
            BaseEquipmentSnapshot a = whole.Snapshot, b = split.Snapshot;
            Assert.That(b.SimulatedTimeSeconds, Is.EqualTo(a.SimulatedTimeSeconds).Within(Tolerance));
            Assert.That(b.Air.ActiveLoadingFraction, Is.EqualTo(a.Air.ActiveLoadingFraction).Within(Tolerance));
            Assert.That(b.Air.RemovalFactor, Is.EqualTo(a.Air.RemovalFactor).Within(Tolerance));
            Assert.That(b.WaterProcessor.Condition, Is.EqualTo(a.WaterProcessor.Condition));
            Assert.That(b.WaterProcessor.DegradedAtSeconds, Is.EqualTo(a.WaterProcessor.DegradedAtSeconds));
            Assert.That(a.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Failed), "120 s + 180 s < 400 s");
        }

        [Test] public void ScriptedDegradationAndFailureHappenAtTheirExactTimes()
        {
            BaseEquipmentSimulation model = StartedEquipment();
            Assert.That(model.SecondsUntilEquipmentEvent(Crew(0)), Is.EqualTo(120).Within(Tolerance));
            model.Advance(119.5, Crew(0));
            Assert.That(model.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Nominal));
            model.Advance(0.5, Crew(0));
            WaterProcessorState w = model.Snapshot.WaterProcessor;
            Assert.That(w.Condition, Is.EqualTo(EquipmentCondition.Degraded));
            Assert.That(w.Fault, Is.EqualTo(EquipmentFault.ProcessorFilterRestricted));
            Assert.That(w.DegradedAtSeconds, Is.EqualTo(120));
            Assert.That(model.SecondsUntilEquipmentEvent(Crew(0)), Is.EqualTo(180).Within(Tolerance));
            model.Advance(179, Crew(0));
            Assert.That(model.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Degraded));
            model.Advance(1, Crew(0));
            Assert.That(model.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Failed));
            Assert.That(model.Snapshot.WaterProcessor.Fault, Is.EqualTo(EquipmentFault.ProcessorFilterRestricted));
            Assert.That(model.Snapshot.WaterProcessor.DegradedAtSeconds, Is.EqualTo(120), "degradation time is kept");
            Assert.That(model.SecondsUntilEquipmentEvent(Crew(0)), Is.EqualTo(double.PositiveInfinity));
        }

        [Test] public void NoScriptedFaultMeansTheProcessorStaysNominal()
        {
            BaseEquipmentSimulation model = StartedEquipment(Equipment(degradeAt: null));
            model.Advance(10000, Crew(0));
            Assert.That(model.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Nominal));
        }

        // ------------------------------------------------------------ processor mass

        [Test] public void NominalBatchSplitsIntoHeldProductAndBrineThatLeavesTheBoundary()
        {
            BaseEquipmentSimulation model = StartedEquipment();
            model.Advance(10, Crew(0));
            Assert.That(model.Apply(EquipmentEffect.ReceiveBatch(Id(1), 2.0)), Is.EqualTo(EquipmentEffectStatus.Applied));
            BaseEquipmentSnapshot s = model.Snapshot;
            Assert.That(s.WaterProcessor.HoldKilograms, Is.EqualTo(1.8).Within(Tolerance));
            Assert.That(s.Ledger.BatchInputKilograms, Is.EqualTo(2.0));
            Assert.That(s.Ledger.BrineOutflowKilograms, Is.EqualTo(0.2).Within(Tolerance));
            Assert.That(s.Ledger.BatchInputKilograms, Is.EqualTo(s.Ledger.BrineOutflowKilograms + s.WaterProcessor.HoldKilograms).Within(Tolerance));
            Assert.That(s.WaterProcessor.ProductConductivity.Status, Is.EqualTo(ReadingStatus.Valid));
            Assert.That(s.WaterProcessor.ProductConductivity.Value, Is.EqualTo(20));
            Assert.That(s.WaterProcessor.ProductConductivity.ObservedAtSeconds, Is.EqualTo(10));
            Assert.That(BaseOperationsBalances.ProcessorResidualKilograms(s), Is.EqualTo(0).Within(Tolerance));
        }

        [Test] public void DegradedBatchRecoversLessAndIsOffSpec()
        {
            BaseEquipmentSimulation model = StartedEquipment();
            model.Advance(130, Crew(0));
            Assert.That(model.Apply(EquipmentEffect.ReceiveBatch(Id(1), 2.0)), Is.EqualTo(EquipmentEffectStatus.Applied));
            BaseEquipmentSnapshot s = model.Snapshot;
            Assert.That(s.WaterProcessor.HoldKilograms, Is.EqualTo(1.5).Within(Tolerance));
            Assert.That(s.Ledger.BrineOutflowKilograms, Is.EqualTo(0.5).Within(Tolerance));
            Assert.That(s.WaterProcessor.ProductConductivity.Value, Is.EqualTo(80));
            Assert.That(s.WaterProcessor.ProductConductivity.Value, Is.GreaterThan(40), "off-spec against the scenario line");
        }

        [Test] public void HeldProductKeepsTheQualityItWasMadeWith()
        {
            BaseEquipmentSimulation model = StartedEquipment();
            model.Advance(100, Crew(0));
            model.Apply(EquipmentEffect.ReceiveBatch(Id(1), 2.0));
            model.Advance(50, Crew(0)); // degrades at 120 s while the nominal product waits in the hold
            Assert.That(model.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Degraded));
            Assert.That(model.Snapshot.WaterProcessor.ProductConductivity.Value, Is.EqualTo(20));
            Assert.That(model.Snapshot.WaterProcessor.HoldKilograms, Is.EqualTo(1.8).Within(Tolerance));
        }

        [Test] public void RejectedBatchesChangeNothing()
        {
            BaseEquipmentSimulation model = StartedEquipment();
            BaseEquipmentSnapshot before = model.Snapshot;
            Assert.That(model.Apply(EquipmentEffect.ReceiveBatch(Id(1), 1.5)), Is.EqualTo(EquipmentEffectStatus.InvalidForState), "not the batch size");
            Assert.That(model.Snapshot, Is.SameAs(before));

            Assert.That(model.Apply(EquipmentEffect.ReceiveBatch(Id(2), 2.0)), Is.EqualTo(EquipmentEffectStatus.Applied));
            BaseEquipmentSnapshot held = model.Snapshot;
            Assert.That(model.Apply(EquipmentEffect.ReceiveBatch(Id(3), 2.0)), Is.EqualTo(EquipmentEffectStatus.InvalidForState), "hold not empty");
            Assert.That(model.Snapshot, Is.SameAs(held));

            BaseEquipmentSimulation isolated = StartedEquipment();
            isolated.Apply(EquipmentEffect.SetOperatingState(Id(1), EquipmentKind.WaterProcessor, EquipmentOperatingState.Isolated));
            Assert.That(isolated.Apply(EquipmentEffect.ReceiveBatch(Id(2), 2.0)), Is.EqualTo(EquipmentEffectStatus.InvalidForState), "isolated");

            BaseEquipmentSimulation failed = StartedEquipment();
            failed.Advance(300, Crew(0));
            Assert.That(failed.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Failed));
            Assert.That(failed.Apply(EquipmentEffect.ReceiveBatch(Id(2), 2.0)), Is.EqualTo(EquipmentEffectStatus.InvalidForState), "failed");
            Assert.That(failed.Snapshot.Ledger.BatchInputKilograms, Is.EqualTo(0));
        }

        [Test] public void ReleasesMoveExactlyTheHoldAndClearTheProductReading()
        {
            BaseEquipmentSimulation model = StartedEquipment();
            model.Apply(EquipmentEffect.ReceiveBatch(Id(1), 2.0));
            double hold = model.Snapshot.WaterProcessor.HoldKilograms;
            Assert.That(model.Apply(EquipmentEffect.ReleaseToPotable(Id(2), 1.0)), Is.EqualTo(EquipmentEffectStatus.InvalidForState), "partial release");
            Assert.That(model.Apply(EquipmentEffect.ReleaseToPotable(Id(3), hold)), Is.EqualTo(EquipmentEffectStatus.Applied));
            BaseEquipmentSnapshot s = model.Snapshot;
            Assert.That(s.WaterProcessor.HoldKilograms, Is.EqualTo(0));
            Assert.That(s.Ledger.ReleasedToPotableKilograms, Is.EqualTo(hold));
            Assert.That(s.WaterProcessor.ProductConductivity.Status, Is.EqualTo(ReadingStatus.Unavailable));
            Assert.That(model.Apply(EquipmentEffect.ReleaseForReprocess(Id(4), hold)), Is.EqualTo(EquipmentEffectStatus.InvalidForState), "empty hold");

            model.Apply(EquipmentEffect.ReceiveBatch(Id(5), 2.0));
            Assert.That(model.Apply(EquipmentEffect.ReleaseForReprocess(Id(6), model.Snapshot.WaterProcessor.HoldKilograms)), Is.EqualTo(EquipmentEffectStatus.Applied));
            Assert.That(model.Snapshot.Ledger.ReleasedForReprocessKilograms, Is.EqualTo(1.8).Within(Tolerance));
            Assert.That(BaseOperationsBalances.ProcessorResidualKilograms(model.Snapshot), Is.EqualTo(0).Within(Tolerance));
        }

        [Test] public void AnIsolatedOrFailedProcessorCanStillBeDrained()
        {
            BaseEquipmentSimulation model = StartedEquipment();
            model.Advance(130, Crew(0));
            model.Apply(EquipmentEffect.ReceiveBatch(Id(1), 2.0));
            model.Apply(EquipmentEffect.SetOperatingState(Id(2), EquipmentKind.WaterProcessor, EquipmentOperatingState.Isolated));
            model.Advance(200, Crew(0));
            Assert.That(model.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Failed));
            Assert.That(model.Apply(EquipmentEffect.ReleaseForReprocess(Id(3), 1.5)), Is.EqualTo(EquipmentEffectStatus.Applied));
        }

        // ------------------------------------------------------------ idempotency

        [Test] public void AnEffectIdIsAppliedOnceAndAConflictingReuseIsRefused()
        {
            BaseEquipmentSimulation model = StartedEquipment();
            Assert.That(model.Apply(EquipmentEffect.ReceiveBatch(Id(1), 2.0)), Is.EqualTo(EquipmentEffectStatus.Applied));
            BaseEquipmentSnapshot after = model.Snapshot;
            Assert.That(model.Apply(EquipmentEffect.ReceiveBatch(Id(1), 2.0)), Is.EqualTo(EquipmentEffectStatus.AlreadyApplied));
            Assert.That(model.Apply(EquipmentEffect.SwitchBed(Id(1))), Is.EqualTo(EquipmentEffectStatus.IdConflict));
            Assert.That(model.Snapshot, Is.SameAs(after), "a replay delivers nothing twice");
            Assert.That(model.Snapshot.Ledger.BatchInputKilograms, Is.EqualTo(2.0));
        }

        [Test] public void ARefusedEffectDoesNotConsumeItsId()
        {
            BaseEquipmentSimulation model = StartedEquipment();
            Assert.That(model.Apply(EquipmentEffect.ReplaceProcessorFilter(Id(7))), Is.EqualTo(EquipmentEffectStatus.InvalidForState), "online");
            model.Apply(EquipmentEffect.SetOperatingState(Id(8), EquipmentKind.WaterProcessor, EquipmentOperatingState.Isolated));
            Assert.That(model.Apply(EquipmentEffect.ReplaceProcessorFilter(Id(7))), Is.EqualTo(EquipmentEffectStatus.Applied));
        }

        // ------------------------------------------------------------ repair

        [Test] public void RepairConsumesOneSpareRestoresNominalAndReturnsNothingLost()
        {
            BaseEquipmentSimulation model = StartedEquipment();
            model.Advance(130, Crew(0));
            model.Apply(EquipmentEffect.ReceiveBatch(Id(1), 2.0));
            model.Apply(EquipmentEffect.ReleaseForReprocess(Id(2), 1.5));
            double brine = model.Snapshot.Ledger.BrineOutflowKilograms;

            Assert.That(model.Apply(EquipmentEffect.SetOperatingState(Id(3), EquipmentKind.WaterProcessor, EquipmentOperatingState.Isolated)), Is.EqualTo(EquipmentEffectStatus.Applied));
            Assert.That(model.Apply(EquipmentEffect.ReplaceProcessorFilter(Id(4))), Is.EqualTo(EquipmentEffectStatus.Applied));
            BaseEquipmentSnapshot s = model.Snapshot;
            Assert.That(s.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Nominal));
            Assert.That(s.WaterProcessor.Fault, Is.EqualTo(EquipmentFault.None));
            Assert.That(s.WaterProcessor.DegradedAtSeconds, Is.Null);
            Assert.That(s.WaterProcessor.Operating, Is.EqualTo(EquipmentOperatingState.Isolated), "only Restore brings it back");
            Assert.That(SpareCount(s, ProcessorFilter), Is.EqualTo(0));
            Assert.That(s.Ledger.SparesConsumed, Is.EqualTo(1));
            Assert.That(s.Ledger.BrineOutflowKilograms, Is.EqualTo(brine), "lost water is never returned");

            Assert.That(model.Apply(EquipmentEffect.ReplaceProcessorFilter(Id(4))), Is.EqualTo(EquipmentEffectStatus.AlreadyApplied));
            Assert.That(SpareCount(model.Snapshot, ProcessorFilter), Is.EqualTo(0), "no double charge");
            Assert.That(model.Apply(EquipmentEffect.ReplaceProcessorFilter(Id(5))), Is.EqualTo(EquipmentEffectStatus.InsufficientSpares));

            model.Apply(EquipmentEffect.SetOperatingState(Id(6), EquipmentKind.WaterProcessor, EquipmentOperatingState.Online));
            model.Advance(1000, Crew(0));
            Assert.That(model.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Nominal), "the scripted fault happens once and the failure timer is cleared");
        }

        [Test] public void RepairAfterFailureAlsoRestoresNominal()
        {
            BaseEquipmentSimulation model = StartedEquipment();
            model.Advance(300, Crew(0));
            Assert.That(model.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Failed));
            RepairProcessor(model, 10);
            Assert.That(model.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Nominal));
            Assert.That(model.Apply(EquipmentEffect.ReceiveBatch(Id(20), 2.0)), Is.EqualTo(EquipmentEffectStatus.Applied));
            Assert.That(model.Snapshot.WaterProcessor.HoldKilograms, Is.EqualTo(1.8).Within(Tolerance));
        }

        [Test] public void SampleReadsTheHeldProductOrTheMachineOutputWhenEmpty()
        {
            BaseEquipmentSimulation model = StartedEquipment();
            model.Advance(130, Crew(0));
            Assert.That(model.Apply(EquipmentEffect.SampleProduct(Id(1))), Is.EqualTo(EquipmentEffectStatus.Applied));
            Assert.That(model.Snapshot.WaterProcessor.ProductConductivity.Value, Is.EqualTo(80), "empty hold: test sample of the degraded machine");
            Assert.That(model.Snapshot.WaterProcessor.ProductConductivity.ObservedAtSeconds, Is.EqualTo(130));

            RepairProcessor(model, 10);
            Assert.That(model.Snapshot.WaterProcessor.ProductConductivity.Value, Is.EqualTo(20), "test after repair is within the line");

            model.Apply(EquipmentEffect.ReceiveBatch(Id(20), 2.0));
            model.Advance(5, Crew(0));
            model.Apply(EquipmentEffect.SampleProduct(Id(21)));
            Assert.That(model.Snapshot.WaterProcessor.ProductConductivity.Value, Is.EqualTo(20));
            Assert.That(model.Snapshot.WaterProcessor.ProductConductivity.ObservedAtSeconds, Is.EqualTo(135));
            Assert.That(model.Snapshot.WaterProcessor.HoldKilograms, Is.EqualTo(1.8).Within(Tolerance), "sampling moves no mass");
        }

        [Test] public void SamplingAFailedEmptyProcessorGivesNoValueNotZero()
        {
            BaseEquipmentSimulation model = StartedEquipment();
            model.Advance(300, Crew(0));
            Assert.That(model.Apply(EquipmentEffect.SampleProduct(Id(1))), Is.EqualTo(EquipmentEffectStatus.Applied));
            Assert.That(model.Snapshot.WaterProcessor.ProductConductivity.Status, Is.EqualTo(ReadingStatus.Unavailable));
        }

        // ------------------------------------------------------------ operating state and air unit

        [Test] public void IsolatingTheAirUnitStopsRemovalAndLoadingAndRestoringResumesFromTheSameBed()
        {
            BaseEquipmentSimulation model = StartedEquipment(Equipment(degradeAt: null));
            model.Advance(100, Crew(2)); // loading 0.7
            Assert.That(model.Apply(EquipmentEffect.SetOperatingState(Id(1), EquipmentKind.AirRevitalization, EquipmentOperatingState.Isolated)), Is.EqualTo(EquipmentEffectStatus.Applied));
            Assert.That(model.Snapshot.Air.RemovalFactor, Is.EqualTo(0));
            Assert.That(model.SecondsUntilEquipmentEvent(Crew(2)), Is.EqualTo(double.PositiveInfinity));
            model.Advance(100, Crew(2));
            Assert.That(model.Snapshot.Air.ActiveLoadingFraction, Is.EqualTo(0.7).Within(Tolerance));
            Assert.That(model.Apply(EquipmentEffect.SetOperatingState(Id(2), EquipmentKind.AirRevitalization, EquipmentOperatingState.Isolated)), Is.EqualTo(EquipmentEffectStatus.InvalidForState), "already isolated: no phantom change");
            Assert.That(model.Apply(EquipmentEffect.SetOperatingState(Id(3), EquipmentKind.AirRevitalization, EquipmentOperatingState.Online)), Is.EqualTo(EquipmentEffectStatus.Applied));
            Assert.That(model.Snapshot.Air.RemovalFactor, Is.EqualTo(new AirRevitalizationConfiguration(0.002, 0.6, 0.2, 0.3, 0, null, 0.5, InletFilter).BedFactor(0.7)).Within(Tolerance));
        }

        [Test] public void SwitchingBedsSwapsLoadingsAndRegenerationEmptiesTheStandbyBed()
        {
            BaseEquipmentSimulation model = StartedEquipment(Equipment(degradeAt: null));
            model.Advance(150, Crew(2)); // active 0.9
            Assert.That(model.Apply(EquipmentEffect.SwitchBed(Id(1))), Is.EqualTo(EquipmentEffectStatus.Applied));
            AirRevitalizationState a = model.Snapshot.Air;
            Assert.That(a.ActiveBed, Is.EqualTo(SorbentBed.B));
            Assert.That(a.ActiveLoadingFraction, Is.EqualTo(0));
            Assert.That(a.StandbyLoadingFraction, Is.EqualTo(0.9).Within(Tolerance));
            Assert.That(a.RemovalFactor, Is.EqualTo(1));
            Assert.That(model.Apply(EquipmentEffect.RegenerateStandbyBed(Id(2))), Is.EqualTo(EquipmentEffectStatus.Applied));
            Assert.That(model.Snapshot.Air.StandbyLoadingFraction, Is.EqualTo(0));
            Assert.That(model.Snapshot.Air.ActiveBed, Is.EqualTo(SorbentBed.B));

            model.Apply(EquipmentEffect.SetOperatingState(Id(3), EquipmentKind.AirRevitalization, EquipmentOperatingState.Isolated));
            Assert.That(model.Apply(EquipmentEffect.SwitchBed(Id(4))), Is.EqualTo(EquipmentEffectStatus.InvalidForState), "switching requires Online");
        }

        [Test] public void InletRestrictionHalvesRemovalUntilTheFilterIsReplacedWhileIsolated()
        {
            BaseEquipmentSimulation model = StartedEquipment(Equipment(degradeAt: null, inletRestrictedAt: 50));
            Assert.That(model.SecondsUntilEquipmentEvent(Crew(0)), Is.EqualTo(50).Within(Tolerance));
            model.Advance(60, Crew(1)); // loading 0.42, below the knee
            Assert.That(model.Snapshot.Air.InletFlowFraction, Is.EqualTo(0.5));
            Assert.That(model.Snapshot.Air.RemovalFactor, Is.EqualTo(0.5).Within(Tolerance));
            Assert.That(model.Apply(EquipmentEffect.ReplaceInletFilter(Id(1))), Is.EqualTo(EquipmentEffectStatus.InvalidForState), "requires isolation");
            model.Apply(EquipmentEffect.SetOperatingState(Id(2), EquipmentKind.AirRevitalization, EquipmentOperatingState.Isolated));
            Assert.That(model.Apply(EquipmentEffect.ReplaceInletFilter(Id(3))), Is.EqualTo(EquipmentEffectStatus.Applied));
            Assert.That(model.Snapshot.Air.InletFlowFraction, Is.EqualTo(1));
            Assert.That(model.Snapshot.Air.RemovalFactor, Is.EqualTo(0), "still isolated");
            Assert.That(SpareCount(model.Snapshot, InletFilter), Is.EqualTo(0));
            Assert.That(model.Apply(EquipmentEffect.ReplaceInletFilter(Id(4))), Is.EqualTo(EquipmentEffectStatus.InsufficientSpares));
            model.Apply(EquipmentEffect.SetOperatingState(Id(5), EquipmentKind.AirRevitalization, EquipmentOperatingState.Online));
            model.Advance(1000, Crew(0));
            Assert.That(model.Snapshot.Air.InletFlowFraction, Is.EqualTo(1), "the scripted restriction happens once");
        }

        // ------------------------------------------------------------ spares

        [Test] public void ReceivedSparesRespectCapacityAllOrNothingAndAreCountedOnce()
        {
            BaseEquipmentSimulation model = StartedEquipment();
            Assert.That(model.Apply(EquipmentEffect.ReceiveSpares(Id(1), ProcessorFilter, 2)), Is.EqualTo(EquipmentEffectStatus.Applied));
            Assert.That(SpareCount(model.Snapshot, ProcessorFilter), Is.EqualTo(3));
            Assert.That(model.Apply(EquipmentEffect.ReceiveSpares(Id(1), ProcessorFilter, 2)), Is.EqualTo(EquipmentEffectStatus.AlreadyApplied));
            Assert.That(model.Apply(EquipmentEffect.ReceiveSpares(Id(2), InletFilter, 3)), Is.EqualTo(EquipmentEffectStatus.ExceedsCapacity));
            Assert.That(SpareCount(model.Snapshot, InletFilter), Is.EqualTo(1), "nothing partial");
            Assert.That(model.Apply(EquipmentEffect.ReceiveSpares(Id(3), "unknown-part", 1)), Is.EqualTo(EquipmentEffectStatus.InvalidForState));
            Assert.That(model.Snapshot.Ledger.SparesReceived, Is.EqualTo(2));
            Assert.That(BaseOperationsTransfers.SpareRoom(model.Snapshot, InletFilter), Is.EqualTo(2));
            Assert.That(BaseOperationsTransfers.SpareRoom(model.Snapshot, ProcessorFilter), Is.EqualTo(0));
            Assert.That(BaseOperationsTransfers.SpareRoom(model.Snapshot, "unknown-part"), Is.EqualTo(0));
        }

        // ------------------------------------------------------------ snapshots and reset

        [Test] public void EarlierSnapshotsAreNeverModified()
        {
            BaseEquipmentSimulation model = StartedEquipment();
            BaseEquipmentSnapshot first = model.Snapshot;
            model.Advance(130, Crew(2));
            model.Apply(EquipmentEffect.ReceiveBatch(Id(1), 2.0));
            model.Apply(EquipmentEffect.ReceiveSpares(Id(2), ProcessorFilter, 1));
            model.Apply(EquipmentEffect.SwitchBed(Id(3)));
            Assert.That(first.SimulatedTimeSeconds, Is.EqualTo(0));
            Assert.That(first.WaterProcessor.HoldKilograms, Is.EqualTo(0));
            Assert.That(first.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Nominal));
            Assert.That(first.Air.ActiveBed, Is.EqualTo(SorbentBed.A));
            Assert.That(first.Air.ActiveLoadingFraction, Is.EqualTo(0.3));
            Assert.That(SpareCount(first, ProcessorFilter), Is.EqualTo(1));
            Assert.That(first.Ledger.BatchInputKilograms, Is.EqualTo(0));
            Assert.That(first.Spares, Is.Not.InstanceOf<System.Collections.Generic.List<SpareStock>>());
        }

        [Test] public void ResetReturnsToTheConfigurationAndForgetsAppliedIds()
        {
            BaseEquipmentSimulation model = StartedEquipment();
            model.Advance(300, Crew(2));
            model.Apply(EquipmentEffect.ReceiveSpares(Id(1), ProcessorFilter, 2));
            model.Reset(Equipment());
            BaseEquipmentSnapshot s = model.Snapshot;
            Assert.That(s.SimulatedTimeSeconds, Is.EqualTo(0));
            Assert.That(s.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Nominal));
            Assert.That(s.Air.ActiveLoadingFraction, Is.EqualTo(0.3));
            Assert.That(SpareCount(s, ProcessorFilter), Is.EqualTo(1));
            Assert.That(model.Apply(EquipmentEffect.ReceiveSpares(Id(1), ProcessorFilter, 2)), Is.EqualTo(EquipmentEffectStatus.Applied), "new epoch, ids cleared");
            Assert.That(model.SecondsUntilEquipmentEvent(Crew(0)), Is.EqualTo(120).Within(Tolerance), "the scripted fault is armed again");
        }
    }
}
