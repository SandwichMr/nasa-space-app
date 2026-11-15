using System;
using NUnit.Framework;
using Trainer.Simulation;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;
using static Trainer.Tests.Simulation.Phase02.LifeSupportAnalyticFixtures;

namespace Trainer.Tests.Simulation.Phase04
{
    /// <summary>D1 phase 4: <see cref="IAirServiceCapacity"/> on the single phase 2 <see cref="OutpostBaseSimulation"/>.
    /// Phase 2 analytic fixture: CO2 1.2 g/s per member, removal up to 4 g/s, cabin 40 m³ at 295 K.</summary>
    public sealed class Phase04AirCouplingTests
    {
        [Test] public void TheBaseModelItselfIsTheAirServiceCapacity()
        {
            OutpostBaseSimulation simulation = Started();
            Assert.That(simulation, Is.InstanceOf<IAirServiceCapacity>());
            Assert.That(simulation, Is.InstanceOf<IOutpostSimulation>());
            Assert.That(((IAirServiceCapacity)simulation).CarbonDioxideRemovalFactor, Is.EqualTo(1));
        }

        [Test] public void FactorOneReproducesPhaseTwoBitForBit()
        {
            OutpostBaseSimulation untouched = Started(), coupled = Started();
            coupled.SetCarbonDioxideRemovalFactor(1);
            foreach (OutpostBaseSimulation s in new[] { untouched, coupled })
            {
                s.Advance(30, Demand(5));
                s.Advance(70, Demand(5, science: true, serviceWatts: 50));
                s.Advance(200, Demand(1));
            }
            Assert.That(coupled.CabinCarbonDioxideKilograms, Is.EqualTo(untouched.CabinCarbonDioxideKilograms));
            Assert.That(coupled.CabinOxygenKilograms, Is.EqualTo(untouched.CabinOxygenKilograms));
            Assert.That(coupled.Ledger.CarbonDioxideRemovedKilograms, Is.EqualTo(untouched.Ledger.CarbonDioxideRemovedKilograms));
            Assert.That(coupled.Snapshot.Power.EnergyJoules, Is.EqualTo(untouched.Snapshot.Power.EnergyJoules));
            Assert.That(coupled.Snapshot.LifeSupport.Water.PotableKilograms, Is.EqualTo(untouched.Snapshot.LifeSupport.Water.PotableKilograms));
        }

        [Test] public void InvalidFactorsThrowWithoutChangingTheFactor()
        {
            var fresh = new OutpostBaseSimulation(Parameters());
            Assert.Throws<InvalidOperationException>(() => fresh.SetCarbonDioxideRemovalFactor(0.5), "requires Reset");
            OutpostBaseSimulation simulation = Started();
            simulation.SetCarbonDioxideRemovalFactor(0.25);
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.SetCarbonDioxideRemovalFactor(-0.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.SetCarbonDioxideRemovalFactor(1.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.SetCarbonDioxideRemovalFactor(double.NaN));
            Assert.That(simulation.CarbonDioxideRemovalFactor, Is.EqualTo(0.25));
        }

        [Test] public void AReducedFactorLetsCarbonDioxideAccumulateWithMassConserved()
        {
            // Four members exhale 4.8 g/s; removal 4 g/s × 0.5 = 2 g/s, so cabin CO2 rises 2.8 g/s from the floor.
            OutpostBaseSimulation simulation = Started();
            double floor = simulation.CabinCarbonDioxideKilograms;
            double tracked0 = TrackedMass(simulation);
            simulation.SetCarbonDioxideRemovalFactor(0.5);
            simulation.Advance(100, Demand(4));
            Assert.That(simulation.Ledger.CarbonDioxideRemovedKilograms, Is.EqualTo(0.2).Within(Exact));
            Assert.That(simulation.CabinCarbonDioxideKilograms, Is.EqualTo(floor + 0.28).Within(Exact));
            Assert.That(TrackedMass(simulation), Is.EqualTo(PredictedTrackedMass(tracked0, simulation.Ledger)).Within(Exact));
            Assert.That(simulation.CarbonDioxideRemovalFactor, Is.EqualTo(0.5), "the factor persists across Advance");
        }

        [Test] public void FactorZeroRemovesNoCarbonDioxideButLeavesOxygenSupplyAlone()
        {
            OutpostBaseSimulation reference = Started(), isolated = Started();
            isolated.SetCarbonDioxideRemovalFactor(0);
            reference.Advance(100, Demand(2));
            isolated.Advance(100, Demand(2));
            Assert.That(isolated.Ledger.CarbonDioxideRemovedKilograms, Is.EqualTo(0));
            Assert.That(isolated.CabinCarbonDioxideKilograms - reference.CabinCarbonDioxideKilograms, Is.EqualTo(0.24).Within(Exact));
            Assert.That(isolated.CabinOxygenKilograms, Is.EqualTo(reference.CabinOxygenKilograms), "O2 supply is a separate device");
            Assert.That(isolated.Ledger.OxygenSuppliedKilograms, Is.EqualTo(reference.Ledger.OxygenSuppliedKilograms));
        }

        [Test] public void RestoringTheFactorPullsCarbonDioxideBackDownToTheControlTarget()
        {
            OutpostBaseSimulation simulation = Started();
            double floor = simulation.CabinCarbonDioxideKilograms;
            simulation.SetCarbonDioxideRemovalFactor(0);
            simulation.Advance(100, Demand(2)); // +0.24 kg
            simulation.SetCarbonDioxideRemovalFactor(1);
            simulation.Advance(150, Demand(2)); // net −1.6 g/s: back to the floor after 150 s
            Assert.That(simulation.CabinCarbonDioxideKilograms, Is.EqualTo(floor).Within(Exact));
        }

        [Test] public void ResetRestoresFactorOne()
        {
            OutpostBaseSimulation simulation = Started();
            simulation.SetCarbonDioxideRemovalFactor(0.1);
            simulation.Reset(Config());
            Assert.That(simulation.CarbonDioxideRemovalFactor, Is.EqualTo(1));
        }

        [Test] public void TheEquipmentFactorDrivesTheBaseRemovalThroughTheDeclaredStepOrder()
        {
            // Authority order: equipment.Advance → SetCarbonDioxideRemovalFactor(equipment factor) → base Advance.
            var equipment = new BaseEquipmentSimulation();
            equipment.Reset(Phase04SimulationFixtures.Equipment(degradeAt: null, initialActive: 1.0));
            OutpostBaseSimulation simulation = Started();
            var demand = new EquipmentDemand(4);
            equipment.Advance(1, demand);
            simulation.SetCarbonDioxideRemovalFactor(equipment.Snapshot.Air.RemovalFactor); // saturated bed: 0.2
            simulation.Advance(100, Demand(4));
            Assert.That(simulation.Ledger.CarbonDioxideRemovedKilograms, Is.EqualTo(0.004 * 0.2 * 100).Within(Exact));
        }
    }
}
