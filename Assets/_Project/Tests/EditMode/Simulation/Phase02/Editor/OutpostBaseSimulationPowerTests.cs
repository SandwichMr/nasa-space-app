using System;
using NUnit.Framework;
using Trainer.Simulation;
using Trainer.Simulation.LifeSupport;
using static Trainer.Tests.Simulation.Phase02.LifeSupportAnalyticFixtures;

namespace Trainer.Tests.Simulation.Phase02
{
    // Electrical part of OutpostBaseSimulation. Synthetic values; expected numbers are closed-form.
    public sealed class OutpostBaseSimulationPowerTests
    {
        private static PowerConfiguration Power(double initial, double capacity, double generation, double essential,
            double science, double maxCharge = 1000d, double maxDischarge = 1000d) =>
            new PowerConfiguration(initial, capacity, generation, essential, science, maxCharge, maxDischarge);

        [Test] public void UseBeforeResetAndNullArgumentsAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new OutpostBaseSimulation(null));
            var simulation = new OutpostBaseSimulation(Parameters());
            Assert.Throws<InvalidOperationException>(() => { var _ = simulation.Snapshot; });
            Assert.Throws<InvalidOperationException>(() => { var _ = simulation.Ledger; });
            Assert.Throws<InvalidOperationException>(() => simulation.Advance(1, Demand(1)));
            Assert.Throws<InvalidOperationException>(() => simulation.SecondsUntilServiceChange(Demand(1)));
            Assert.Throws<InvalidOperationException>(() =>
                simulation.Apply(new ResourceTransfer(Guid.NewGuid(), TreatWater())));
            Assert.Throws<ArgumentNullException>(() => simulation.Reset(null));
            simulation.Reset(Config());
            Assert.Throws<ArgumentNullException>(() => simulation.Advance(1, null));
            Assert.Throws<ArgumentNullException>(() => simulation.SecondsUntilServiceChange(null));
            Assert.Throws<ArgumentNullException>(() => simulation.Apply(null));
        }

        [Test] public void WithoutServiceLoadMatchesPhaseOnePowerSystem()
        {
            // Includes surplus/charge limit, deficit/discharge to empty, discharge limit and full battery.
            PowerConfiguration[] configs =
            {
                Power(1000, 10000, 100, 30, 20),
                Power(500, 10000, 0, 30, 20),
                Power(0, 10000, 100, 10, 0, maxCharge: 40),
                Power(9000, 10000, 200, 30, 20),
                Power(1000, 10000, 10, 30, 50, maxDischarge: 40)
            };
            double[] steps = { 7, 13.5, 40, 0.25, 120 };
            foreach (PowerConfiguration config in configs)
            {
                var phaseOne = new PowerSystem();
                phaseOne.Reset(config);
                OutpostBaseSimulation phaseTwo = Started(Config(power: config));
                for (int i = 0; i < steps.Length; i++)
                {
                    bool science = i % 2 == 0;
                    Assert.That(phaseTwo.SecondsUntilServiceChange(Demand(0, science)),
                        Is.EqualTo(phaseOne.SecondsUntilServiceChange(science)));
                    phaseOne.Advance(steps[i], science);
                    phaseTwo.Advance(steps[i], Demand(0, science));
                    PowerSnapshot a = phaseOne.Snapshot, b = phaseTwo.Snapshot.Power;
                    Assert.That(b.SimulatedTimeSeconds, Is.EqualTo(a.SimulatedTimeSeconds));
                    Assert.That(b.EnergyJoules, Is.EqualTo(a.EnergyJoules).Within(Exact));
                    Assert.That(b.EssentialServedWatts, Is.EqualTo(a.EssentialServedWatts));
                    Assert.That(b.ScienceRequestedWatts, Is.EqualTo(a.ScienceRequestedWatts));
                    Assert.That(b.ScienceServedWatts, Is.EqualTo(a.ScienceServedWatts));
                    Assert.That(phaseTwo.LastFullyServedScienceSeconds,
                        Is.EqualTo(phaseOne.LastFullyServedScienceSeconds).Within(Exact));
                    Assert.That(phaseTwo.LastFullyServedServiceSeconds, Is.EqualTo(steps[i]).Within(Exact),
                        "No service load requested: the contract counts elapsed time.");
                }
            }
        }

        [Test] public void EssentialThenServiceThenSciencePriority()
        {
            // No battery: 100 W available. Essential 60 served, service gets 40 of 50, science gets 0 of 30.
            OutpostBaseSimulation simulation = Started(Config(power: Power(0, 0, 100, 60, 30)));
            simulation.Advance(10, Demand(0, true, 50));
            OutpostSimulationSnapshot s = simulation.Snapshot;
            Assert.That(s.Power.EssentialServedWatts, Is.EqualTo(60));
            Assert.That(s.ServiceLoadRequestedWatts, Is.EqualTo(50));
            Assert.That(s.ServiceLoadServedWatts, Is.EqualTo(40));
            Assert.That(s.Power.ScienceRequestedWatts, Is.EqualTo(30));
            Assert.That(s.Power.ScienceServedWatts, Is.EqualTo(0));
            Assert.That(simulation.LastFullyServedServiceSeconds, Is.EqualTo(0), "Partial service does not count.");
            Assert.That(simulation.LastFullyServedScienceSeconds, Is.EqualTo(0));

            simulation.Advance(10, Demand(0, true, 10));   // 60 + 10 + 30 = 100: everything fully served.
            Assert.That(simulation.LastFullyServedServiceSeconds, Is.EqualTo(10));
            Assert.That(simulation.LastFullyServedScienceSeconds, Is.EqualTo(10));
            Assert.That(simulation.Snapshot.ServiceLoadServedWatts, Is.EqualTo(10));
        }

        [Test] public void ServiceLoadDrainsBatteryAndSplitsAtDepletion()
        {
            // Generation 0, essential 10 W + service 20 W = 30 W from 300 J -> empty at exactly 10 s.
            OutpostBaseSimulation simulation = Started(Config(power: Power(300, 1000, 0, 10, 0)));
            OutpostDemand demand = Demand(1, false, 20);
            Assert.That(simulation.SecondsUntilServiceChange(demand), Is.EqualTo(10).Within(Exact));
            simulation.Advance(15, demand);
            Assert.That(simulation.Snapshot.Power.EnergyJoules, Is.EqualTo(0));
            Assert.That(simulation.LastFullyServedServiceSeconds, Is.EqualTo(10).Within(Exact));
            Assert.That(simulation.Snapshot.Power.EssentialServedWatts, Is.EqualTo(0));
            Assert.That(simulation.Snapshot.ServiceLoadServedWatts, Is.EqualTo(0));
            Assert.That(simulation.SecondsUntilServiceChange(demand), Is.EqualTo(0), "Essential already lost.");
        }

        [Test] public void ServiceEnergyIsChargedOnceByAdvance()
        {
            // 1 kW generation minus 100 W essential minus 150 W service = 750 W surplus; charge limit 1000 W.
            OutpostBaseSimulation simulation = Started();
            simulation.Advance(4, Demand(0, false, 150));
            Assert.That(simulation.Snapshot.Power.EnergyJoules, Is.EqualTo(5000 + 750 * 4).Within(Exact));
            // Same result as without a service load minus 150 W x 4 s on a battery-limited config.
            OutpostBaseSimulation drained = Started(Config(power: Power(5000, 10000, 0, 100, 0)));
            drained.Advance(4, Demand(0, false, 150));
            Assert.That(drained.Snapshot.Power.EnergyJoules, Is.EqualTo(5000 - 250 * 4).Within(Exact));
        }

        [Test] public void ZeroStepIsANoOpAndInvalidStepsChangeNothing()
        {
            OutpostBaseSimulation simulation = Started();
            simulation.Advance(5, Demand(2, true, 40));
            OutpostSimulationSnapshot before = simulation.Snapshot;
            LifeSupportLedger ledger = simulation.Ledger;
            double service = simulation.LastFullyServedServiceSeconds;

            simulation.Advance(0, Demand(4, false, 0));
            Assert.That(simulation.Snapshot, Is.SameAs(before));
            foreach (double bad in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Advance(bad, Demand(1)));
            Assert.That(simulation.Snapshot, Is.SameAs(before));
            Assert.That(simulation.Ledger, Is.SameAs(ledger));
            Assert.That(simulation.LastFullyServedServiceSeconds, Is.EqualTo(service));
        }

        [Test] public void ResultsAreIndependentOfStepSize()
        {
            // Crosses battery depletion, potable depletion and O2 store depletion within the run.
            var config = Config(power: Power(2000, 5000, 20, 30, 10),
                water: new WaterStock(0.3, 19.9, 40, 20), food: new FoodStock(0.25, 30));
            OutpostSimulationParameters parameters = Parameters(air: Air(store: 0.2));
            OutpostDemand demand = Demand(2, true, 5);

            OutpostBaseSimulation once = Started(config, parameters);
            once.Advance(300, demand);
            OutpostBaseSimulation many = Started(config, parameters);
            double service = 0, science = 0;
            for (int i = 0; i < 300; i++)
            {
                many.Advance(1, demand);
                service += many.LastFullyServedServiceSeconds;
                science += many.LastFullyServedScienceSeconds;
            }
            OutpostBaseSimulation uneven = Started(config, parameters);
            foreach (double step in new[] { 0.7, 41.3, 13, 100, 0.001, 144.999 }) uneven.Advance(step, demand);

            foreach (OutpostBaseSimulation other in new[] { many, uneven })
            {
                OutpostSimulationSnapshot a = once.Snapshot, b = other.Snapshot;
                Assert.That(b.Power.SimulatedTimeSeconds, Is.EqualTo(300).Within(Exact));
                Assert.That(b.Power.EnergyJoules, Is.EqualTo(a.Power.EnergyJoules).Within(1e-6));
                Assert.That(b.LifeSupport.Water.PotableKilograms, Is.EqualTo(a.LifeSupport.Water.PotableKilograms).Within(Exact));
                Assert.That(b.LifeSupport.Water.UntreatedKilograms, Is.EqualTo(a.LifeSupport.Water.UntreatedKilograms).Within(Exact));
                Assert.That(b.LifeSupport.Food.StoredKilograms, Is.EqualTo(a.LifeSupport.Food.StoredKilograms).Within(Exact));
                Assert.That(other.CabinOxygenKilograms, Is.EqualTo(once.CabinOxygenKilograms).Within(Exact));
                Assert.That(other.CabinCarbonDioxideKilograms, Is.EqualTo(once.CabinCarbonDioxideKilograms).Within(Exact));
                Assert.That(other.Ledger.OxygenStoreKilograms, Is.EqualTo(once.Ledger.OxygenStoreKilograms).Within(Exact));
            }
            Assert.That(service, Is.EqualTo(once.LastFullyServedServiceSeconds).Within(1e-6));
            Assert.That(science, Is.EqualTo(once.LastFullyServedScienceSeconds).Within(1e-6));
        }
    }
}
