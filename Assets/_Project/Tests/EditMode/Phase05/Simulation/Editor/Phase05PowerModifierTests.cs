using System;
using NUnit.Framework;
using Trainer.Simulation;
using Trainer.Simulation.Incidents;
using Trainer.Simulation.LifeSupport;
using static Trainer.Tests.Simulation.Phase02.LifeSupportAnalyticFixtures;

namespace Trainer.Tests.Simulation.Phase05
{
    /// <summary>
    /// D1 phase 5: reversible electrical capacity (<see cref="IPowerCapacityModifiers"/>) on the single phase 2 base model.
    /// Synthetic power: battery 5000/10000 J, generation 300 W, essential 250 W, science 200 W, charge/discharge 1000 W.
    /// </summary>
    public sealed class Phase05PowerModifierTests
    {
        private static readonly Guid TripId = new Guid("00000000-0000-0000-0000-00000000a001");
        private static readonly Guid ControllerId = new Guid("00000000-0000-0000-0000-00000000a002");
        private static readonly Guid UpgradeId = new Guid("00000000-0000-0000-0000-00000000a003");

        private static PowerConfiguration TightPower() => new PowerConfiguration(5000, 10000, 300, 250, 200, 1000, 1000);
        private static OutpostBaseSimulation Tight() => Started(Config(TightPower()));
        private static IPowerCapacityModifiers Mods(OutpostBaseSimulation s) => (IPowerCapacityModifiers)s;

        [Test]
        public void TheBaseModelItselfCarriesThePowerModifiers()
        {
            OutpostBaseSimulation s = Tight();
            Assert.That(s, Is.InstanceOf<IPowerCapacityModifiers>());
            Assert.That(Mods(s).PowerCapacity.GenerationFactor, Is.EqualTo(1));
            Assert.That(Mods(s).PowerCapacity.Modifiers.Count, Is.Zero);
        }

        [Test]
        public void WithoutModifiersTheBaseIsBitForBitPhase4()
        {
            OutpostBaseSimulation untouched = Tight(), touched = Tight();
            Assert.That(Mods(touched).Apply(Trip(0.5)), Is.EqualTo(PowerModifierStatus.Applied));
            Assert.That(Mods(touched).Remove(TripId), Is.True);
            foreach (OutpostBaseSimulation s in new[] { untouched, touched })
            {
                s.Advance(30, Demand(3));
                s.Advance(70, Demand(3, science: true, serviceWatts: 50));
            }
            Assert.That(touched.Snapshot.Power.EnergyJoules, Is.EqualTo(untouched.Snapshot.Power.EnergyJoules));
            Assert.That(touched.CabinCarbonDioxideKilograms, Is.EqualTo(untouched.CabinCarbonDioxideKilograms));
        }

        [Test]
        public void AGenerationDerateTurnsChargingIntoDischargeAndTheLossIsNotRefunded()
        {
            OutpostBaseSimulation s = Tight();
            Mods(s).Apply(Trip(0.5));                       // 150 W generated vs 250 W essential: −100 W from the battery
            s.Advance(10, Demand(0));
            Assert.That(s.Snapshot.Power.EnergyJoules, Is.EqualTo(4000).Within(Exact));
            Assert.That(s.Snapshot.Power.GenerationWatts, Is.EqualTo(150).Within(Exact), "the snapshot shows the effective generation");
            Assert.That(Mods(s).Remove(TripId), Is.True);
            s.Advance(10, Demand(0));                       // back to +50 W
            Assert.That(s.Snapshot.Power.EnergyJoules, Is.EqualTo(4500).Within(Exact), "recovery restores the rate, not the lost energy");
        }

        [Test]
        public void ADischargeDerateRespectsBothStoredEnergyAndTheDischargeLimit()
        {
            var noSun = new PowerConfiguration(5000, 10000, 0, 250, 200, 1000, 1000);
            OutpostBaseSimulation s = Started(Config(noSun));
            Mods(s).Apply(new PowerModifier(ControllerId, PowerModifierTarget.DischargeFactor, 0.2, "discharge-controller-fault"));
            s.Advance(10, Demand(0));
            Assert.That(s.Snapshot.Power.EssentialServedWatts, Is.EqualTo(200).Within(Exact), "limited to 1000 W × 0.2 although 5000 J are stored");
            Assert.That(s.Snapshot.Power.EnergyJoules, Is.EqualTo(3000).Within(Exact));
            Assert.That(s.Snapshot.Power.EssentialServedWatts, Is.LessThan(s.Snapshot.Power.EssentialRequestedWatts), "essential service is lost");
        }

        [Test]
        public void AnIsolatedControllerLeavesOnlyGenerationEvenWithAFullBattery()
        {
            OutpostBaseSimulation s = Tight();
            Mods(s).Apply(new PowerModifier(ControllerId, PowerModifierTarget.DischargeFactor, 0.0, "isolated"));
            s.Advance(10, Demand(0, serviceWatts: 200));    // 300 W generation: essential 250 served, service only 50 of 200
            Assert.That(s.Snapshot.Power.EssentialServedWatts, Is.EqualTo(250).Within(Exact));
            Assert.That(s.Snapshot.ServiceLoadServedWatts, Is.EqualTo(50).Within(Exact));
            Assert.That(s.Snapshot.Power.EnergyJoules, Is.EqualTo(5000).Within(Exact), "nothing left to charge, nothing discharged");
        }

        [Test]
        public void TwoModifiersRemovedInEitherOrderNeverRemoveEachOther()
        {
            foreach (bool tripFirst in new[] { true, false })
            {
                OutpostBaseSimulation s = Tight();
                Mods(s).Apply(Trip(0.5));
                Mods(s).Apply(new PowerModifier(ControllerId, PowerModifierTarget.GenerationFactor, 0.8, "other"));
                Assert.That(Mods(s).PowerCapacity.GenerationFactor, Is.EqualTo(0.4).Within(1e-12));
                Mods(s).Remove(tripFirst ? TripId : ControllerId);
                Assert.That(Mods(s).PowerCapacity.GenerationFactor, Is.EqualTo(tripFirst ? 0.8 : 0.5).Within(1e-12));
                Mods(s).Remove(tripFirst ? ControllerId : TripId);
                Assert.That(Mods(s).PowerCapacity.GenerationFactor, Is.EqualTo(1));
            }
        }

        [Test]
        public void IdsApplyOncePerResetAndConflictsChangeNothing()
        {
            OutpostBaseSimulation s = Tight();
            Assert.That(Mods(s).Apply(Trip(0.5)), Is.EqualTo(PowerModifierStatus.Applied));
            Assert.That(Mods(s).Apply(Trip(0.5)), Is.EqualTo(PowerModifierStatus.AlreadyApplied));
            Assert.That(Mods(s).Apply(Trip(0.3)), Is.EqualTo(PowerModifierStatus.IdConflict));
            Assert.That(Mods(s).PowerCapacity.GenerationFactor, Is.EqualTo(0.5));
            Assert.That(Mods(s).Remove(TripId), Is.True);
            Assert.That(Mods(s).Remove(TripId), Is.False, "removal is idempotent");
            Assert.That(Mods(s).Apply(Trip(0.5)), Is.EqualTo(PowerModifierStatus.AlreadyApplied), "a late callback cannot re-apply a removed id");
            Assert.That(Mods(s).PowerCapacity.GenerationFactor, Is.EqualTo(1));
        }

        [Test]
        public void ResetClearsModifiersAndTheirIds()
        {
            OutpostBaseSimulation s = Tight();
            Mods(s).Apply(Trip(0.5));
            s.Reset(Config(TightPower()));
            Assert.That(Mods(s).PowerCapacity.Modifiers.Count, Is.Zero);
            Assert.That(Mods(s).Apply(Trip(0.5)), Is.EqualTo(PowerModifierStatus.Applied), "a new epoch is a new id space");
        }

        [Test]
        public void TheModifierBudgetIsBounded()
        {
            OutpostBaseSimulation s = Tight();
            for (int i = 0; i < IncidentEffectLimits.MaxPowerModifiers; i++)
                Assert.That(Mods(s).Apply(new PowerModifier(Guid.NewGuid(), PowerModifierTarget.GenerationFactor, 1, "n")), Is.EqualTo(PowerModifierStatus.Applied));
            Assert.That(Mods(s).Apply(Trip(0.5)), Is.EqualTo(PowerModifierStatus.LimitReached));
            Assert.That(Mods(s).PowerCapacity.Modifiers.Count, Is.EqualTo(IncidentEffectLimits.MaxPowerModifiers));
        }

        [Test]
        public void AnAddedEssentialLoadIsRequestedAndPaidEveryStep()
        {
            OutpostBaseSimulation s = Tight();
            Mods(s).Apply(new PowerModifier(UpgradeId, PowerModifierTarget.EssentialLoadWatts, 15, "high-recovery-regulator"));
            s.Advance(10, Demand(0));
            Assert.That(s.Snapshot.Power.EssentialRequestedWatts, Is.EqualTo(265).Within(Exact));
            Assert.That(s.Snapshot.Power.EnergyJoules, Is.EqualTo(5350).Within(Exact), "+35 W instead of +50 W");
        }

        [Test]
        public void ModifiedRatesAreIndependentOfTheStepPartition()
        {
            OutpostBaseSimulation one = Tight(), many = Tight();
            foreach (OutpostBaseSimulation s in new[] { one, many }) Mods(s).Apply(Trip(0.5));
            one.Advance(60, Demand(2, serviceWatts: 30));
            for (int i = 0; i < 240; i++) many.Advance(0.25, Demand(2, serviceWatts: 30));
            Assert.That(many.Snapshot.Power.EnergyJoules, Is.EqualTo(one.Snapshot.Power.EnergyJoules).Within(1e-7));
            Assert.That(many.CabinCarbonDioxideKilograms, Is.EqualTo(one.CabinCarbonDioxideKilograms).Within(1e-9));
        }

        [Test]
        public void TheBatteryEmptyEventIsCutWithTheEffectiveCapacity()
        {
            var small = new PowerConfiguration(500, 10000, 300, 250, 200, 1000, 1000);
            OutpostBaseSimulation s = Started(Config(small));
            Assert.That(s.SecondsUntilServiceChange(Demand(0)), Is.EqualTo(9500 / 50.0).Within(Exact), "charging to full at +50 W");
            Mods(s).Apply(Trip(0.5));                       // −100 W: empty in 5 s
            Assert.That(s.SecondsUntilServiceChange(Demand(0)), Is.EqualTo(5).Within(Exact));
            s.Advance(8, Demand(0));
            Assert.That(s.Snapshot.Power.EnergyJoules, Is.EqualTo(0));
            Assert.That(s.Snapshot.Power.EssentialServedWatts, Is.EqualTo(150).Within(Exact), "then only the derated generation serves");
        }

        [Test]
        public void EnergyFollowsTheEffectiveRatesAndNothingIsRefundedOnRemoval()
        {
            OutpostBaseSimulation s = Tight();
            s.Advance(20, Demand(1, serviceWatts: 40));     // 300 − 250 − 40 = +10 W → 5200 J
            Mods(s).Apply(Trip(0.5));
            s.Advance(30, Demand(1));                       // 150 − 250 = −100 W → 2200 J
            Mods(s).Apply(new PowerModifier(UpgradeId, PowerModifierTarget.EssentialLoadWatts, 15, "upgrade"));
            s.Advance(15, Demand(1));                       // 150 − 265 = −115 W → 475 J
            Assert.That(s.Snapshot.Power.EnergyJoules, Is.EqualTo(475).Within(1e-6));
            Mods(s).Remove(TripId);
            s.Advance(200, Demand(1));                      // 300 − 265 = +35 W → 7475 J
            Assert.That(s.Snapshot.Power.EnergyJoules, Is.EqualTo(7475).Within(1e-6));
        }

        private static PowerModifier Trip(double factor) =>
            new PowerModifier(TripId, PowerModifierTarget.GenerationFactor, factor, "array-converter-trip");
    }
}
