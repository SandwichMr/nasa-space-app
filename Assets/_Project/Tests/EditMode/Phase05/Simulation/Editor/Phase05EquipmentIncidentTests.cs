using System;
using NUnit.Framework;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.Incidents;
using static Trainer.Tests.Simulation.Phase04.Phase04SimulationFixtures;

namespace Trainer.Tests.Simulation.Phase05
{
    /// <summary>
    /// D1 phase 5: <see cref="IEquipmentIncidentTarget"/> on the single equipment model. Phase 4 synthetic processor: batch 2 kg,
    /// recovery 0.90 / 0.75, product 20 / 80 µS/cm, spare "processor-filter". Phase 5 disables the scripted fault (degradeAt null).
    /// </summary>
    public sealed class Phase05EquipmentIncidentTests
    {
        private static BaseEquipmentSimulation Unscripted(int processorSpares = 1) =>
            StartedEquipment(Equipment(degradeAt: null, failAfter: null, processorSpares: processorSpares));

        private static IEquipmentIncidentTarget Target(BaseEquipmentSimulation m) => m;

        [Test]
        public void ADirectorDegradationActsAtTheCurrentInstantAndFailsOnlyAtItsOwnTimer()
        {
            BaseEquipmentSimulation m = Unscripted();
            m.Advance(50, Crew(2));
            Assert.That(Target(m).DegradeProcessor(Id(1), 30), Is.EqualTo(EquipmentEffectStatus.Applied));
            Assert.That(m.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Degraded));
            Assert.That(m.Snapshot.WaterProcessor.DegradedAtSeconds, Is.EqualTo(50));
            Assert.That(m.SecondsUntilEquipmentEvent(Crew(0)), Is.EqualTo(30).Within(Tolerance), "the authority cuts its step at the failure");
            m.Advance(29.75, Crew(0));
            Assert.That(m.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Degraded));
            m.Advance(0.25, Crew(0));
            Assert.That(m.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Failed), "exactly at 80 s");
        }

        [Test]
        public void ADegradationWithoutTimerNeverFails()
        {
            BaseEquipmentSimulation m = Unscripted();
            Target(m).DegradeProcessor(Id(1), null);
            Assert.That(double.IsPositiveInfinity(m.SecondsUntilEquipmentEvent(Crew(0))), Is.True);
            m.Advance(10000, Crew(0));
            Assert.That(m.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Degraded));
        }

        [Test]
        public void ADegradedBatchLosesMoreWaterAndTheBalanceStillCloses()
        {
            BaseEquipmentSimulation m = Unscripted();
            Target(m).DegradeProcessor(Id(1), null);
            Assert.That(m.Apply(EquipmentEffect.ReceiveBatch(Id(2), 2.0)), Is.EqualTo(EquipmentEffectStatus.Applied));
            EquipmentLedger l = m.Snapshot.Ledger;
            Assert.That(m.Snapshot.WaterProcessor.HoldKilograms, Is.EqualTo(1.5).Within(Tolerance));
            Assert.That(l.BrineOutflowKilograms, Is.EqualTo(0.5).Within(Tolerance));
            Assert.That(l.BatchInputKilograms, Is.EqualTo(m.Snapshot.WaterProcessor.HoldKilograms + l.BrineOutflowKilograms).Within(Tolerance),
                "input = product + residue");
        }

        [Test]
        public void ARefusedDegradationChangesNothingAndDoesNotConsumeItsId()
        {
            BaseEquipmentSimulation m = Unscripted();
            Target(m).DegradeProcessor(Id(1), null);
            Assert.That(Target(m).DegradeProcessor(Id(2), null), Is.EqualTo(EquipmentEffectStatus.InvalidForState), "already degraded");
            RepairProcessor(m, 10);
            Assert.That(Target(m).DegradeProcessor(Id(2), null), Is.EqualTo(EquipmentEffectStatus.Applied), "the refused id was not consumed");
        }

        [Test]
        public void IdsAreIdempotentAndShareTheEquipmentIdSpace()
        {
            BaseEquipmentSimulation m = Unscripted();
            Assert.That(Target(m).DegradeProcessor(Id(1), 30), Is.EqualTo(EquipmentEffectStatus.Applied));
            Assert.That(Target(m).DegradeProcessor(Id(1), 30), Is.EqualTo(EquipmentEffectStatus.AlreadyApplied));
            Assert.That(Target(m).DegradeProcessor(Id(1), 60), Is.EqualTo(EquipmentEffectStatus.IdConflict));
            Assert.That(m.Apply(EquipmentEffect.SampleProduct(Id(1))), Is.EqualTo(EquipmentEffectStatus.IdConflict), "phase 4 effect with a phase 5 id");
            Assert.That(m.Apply(EquipmentEffect.SampleProduct(Id(2))), Is.EqualTo(EquipmentEffectStatus.Applied));
            Assert.That(Target(m).ConsumeSpare(Id(2), ProcessorFilter), Is.EqualTo(EquipmentEffectStatus.IdConflict), "phase 5 effect with a phase 4 id");
        }

        [Test]
        public void ARepairCancelsTheFailTimerAndNoLateCallbackUndoesIt()
        {
            BaseEquipmentSimulation m = Unscripted();
            Target(m).DegradeProcessor(Id(1), 30);
            m.Advance(10, Crew(0));
            RepairProcessor(m, 10);
            Assert.That(m.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Nominal));
            Assert.That(Target(m).DegradeProcessor(Id(1), 30), Is.EqualTo(EquipmentEffectStatus.AlreadyApplied), "the old request replays as already applied");
            m.Advance(100, Crew(0));
            Assert.That(m.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Nominal), "the cancelled timer never fires");
        }

        [Test]
        public void ConsumingSparesIsBoundedAndTraced()
        {
            BaseEquipmentSimulation m = Unscripted(processorSpares: 1);
            Assert.That(Target(m).ConsumeSpare(Id(1), ProcessorFilter), Is.EqualTo(EquipmentEffectStatus.Applied));
            Assert.That(SpareCount(m.Snapshot, ProcessorFilter), Is.Zero);
            Assert.That(m.Snapshot.Ledger.SparesConsumed, Is.EqualTo(1));
            Assert.That(Target(m).ConsumeSpare(Id(2), ProcessorFilter), Is.EqualTo(EquipmentEffectStatus.InsufficientSpares));
            Assert.That(Target(m).ConsumeSpare(Id(3), "no-such-spare"), Is.EqualTo(EquipmentEffectStatus.InvalidForState));
            Assert.That(m.Snapshot.Ledger.SparesConsumed, Is.EqualTo(1));
        }

        [Test]
        public void TheUpgradeRaisesNominalRecoveryOnceWithinItsCeiling()
        {
            BaseEquipmentSimulation m = Unscripted();
            Assert.That(Target(m).ProcessorNominalRecovery, Is.EqualTo(0.90));
            Assert.That(Target(m).UpgradeProcessorRecovery(Id(1), 0.99), Is.EqualTo(EquipmentEffectStatus.InvalidForState), "above the ceiling");
            Assert.That(Target(m).UpgradeProcessorRecovery(Id(2), 0.85), Is.EqualTo(EquipmentEffectStatus.InvalidForState), "not an upgrade");
            Assert.That(Target(m).UpgradeProcessorRecovery(Id(3), 0.95), Is.EqualTo(EquipmentEffectStatus.Applied));
            Assert.That(Target(m).ProcessorUpgraded, Is.True);
            Assert.That(Target(m).UpgradeProcessorRecovery(Id(4), 0.96), Is.EqualTo(EquipmentEffectStatus.InvalidForState), "only once");
            m.Apply(EquipmentEffect.ReceiveBatch(Id(5), 2.0));
            Assert.That(m.Snapshot.WaterProcessor.HoldKilograms, Is.EqualTo(1.9).Within(Tolerance));
            Assert.That(m.Snapshot.Ledger.BrineOutflowKilograms, Is.EqualTo(0.1).Within(Tolerance), "brine still leaves");
        }

        [Test]
        public void TheUpgradeDoesNotChangeDegradedRecovery()
        {
            BaseEquipmentSimulation m = Unscripted();
            Target(m).UpgradeProcessorRecovery(Id(1), 0.95);
            Target(m).DegradeProcessor(Id(2), null);
            m.Apply(EquipmentEffect.ReceiveBatch(Id(3), 2.0));
            Assert.That(m.Snapshot.WaterProcessor.HoldKilograms, Is.EqualTo(1.5).Within(Tolerance));
        }

        [Test]
        public void ResetForgetsTheUpgradeAndThePhase5Ids()
        {
            BaseEquipmentSimulation m = Unscripted();
            Target(m).UpgradeProcessorRecovery(Id(1), 0.95);
            Target(m).DegradeProcessor(Id(2), 30);
            m.Reset(Equipment(degradeAt: null, failAfter: null));
            Assert.That(Target(m).ProcessorUpgraded, Is.False);
            Assert.That(Target(m).ProcessorNominalRecovery, Is.EqualTo(0.90));
            Assert.That(m.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Nominal));
            Assert.That(Target(m).DegradeProcessor(Id(2), 30), Is.EqualTo(EquipmentEffectStatus.Applied));
        }

        [Test]
        public void ThePhase4ScriptedFaultIsUnchangedWhenNoPhase5EffectIsUsed()
        {
            BaseEquipmentSimulation m = StartedEquipment(Equipment(degradeAt: 120, failAfter: 180));
            m.Advance(120, Crew(1));
            Assert.That(m.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Degraded));
            Assert.That(m.SecondsUntilEquipmentEvent(Crew(0)), Is.LessThanOrEqualTo(180));
            m.Advance(180, Crew(0));
            Assert.That(m.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Failed));
        }
    }
}
