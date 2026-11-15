using NUnit.Framework;
using Trainer.Simulation;
using Trainer.Simulation.LifeSupport;
using static Trainer.Tests.Simulation.Phase02.LifeSupportAnalyticFixtures;

namespace Trainer.Tests.Simulation.Phase02
{
    // One well-mixed cabin (V = 40 m³, T = 295 K), O2 supply and CO2 removal as separate services. Synthetic values.
    public sealed class OutpostBaseSimulationCabinTests
    {
        private static readonly CabinConfiguration DefaultCabin = Cabin();

        [Test] public void InitialReadingsMatchTheDeclaredCabinThroughTheIdealGasLaw()
        {
            OutpostBaseSimulation simulation = Started();
            CabinAirObservation cabin = simulation.Snapshot.LifeSupport.Cabin;
            Assert.That(cabin.Oxygen.Status, Is.EqualTo(ReadingStatus.Valid));
            Assert.That(cabin.Oxygen.PartialPressureKilopascals, Is.EqualTo(21).Within(Exact));
            Assert.That(cabin.Oxygen.ObservedAtSeconds, Is.EqualTo(0));
            Assert.That(cabin.CarbonDioxide.PartialPressureKilopascals, Is.EqualTo(0.5).Within(Exact));
            // 21 kPa * 40 m³ * 0.0319988 kg/mol / (8.314462618 J/(mol K) * 295 K) = 10.959 kg.
            Assert.That(simulation.CabinOxygenKilograms, Is.EqualTo(O2Mass(21, DefaultCabin)).Within(Exact));
            Assert.That(simulation.CabinOxygenKilograms, Is.EqualTo(10.959).Within(0.001));
            Assert.That(simulation.CabinCarbonDioxideKilograms, Is.EqualTo(Co2Mass(0.5, DefaultCabin)).Within(Exact));
            Assert.That(CabinGas.PartialPressureKilopascals(simulation.CabinOxygenKilograms, CabinGas.OxygenMolarMass,
                DefaultCabin), Is.EqualTo(21).Within(Exact));
        }

        [Test] public void ServicesWithEnoughCapacityHoldTheDeclaredComposition()
        {
            // Two members: O2 demand 2 g/s <= supply 3 g/s; CO2 production 2.4 g/s <= removal 4 g/s.
            OutpostBaseSimulation simulation = Started();
            simulation.Advance(100, Demand(2));
            CabinAirObservation cabin = simulation.Snapshot.LifeSupport.Cabin;
            Assert.That(cabin.Oxygen.PartialPressureKilopascals, Is.EqualTo(21).Within(Exact));
            Assert.That(cabin.CarbonDioxide.PartialPressureKilopascals, Is.EqualTo(0.5).Within(Exact));
            Assert.That(cabin.Oxygen.ObservedAtSeconds, Is.EqualTo(100));
            LifeSupportLedger l = simulation.Ledger;
            Assert.That(l.OxygenSuppliedKilograms, Is.EqualTo(0.2).Within(Exact));
            Assert.That(l.OxygenStoreKilograms, Is.EqualTo(0.8).Within(Exact));
            Assert.That(l.CrewOxygenIntakeKilograms, Is.EqualTo(0.2).Within(Exact));
            Assert.That(l.CrewCarbonDioxideReturnKilograms, Is.EqualTo(0.24).Within(Exact));
            Assert.That(l.CarbonDioxideRemovedKilograms, Is.EqualTo(0.24).Within(Exact));
        }

        [Test] public void OxygenSupplyDoesNotRemoveCarbonDioxide()
        {
            // No removal capacity: CO2 accumulates at the exhaled rate while O2 is still held.
            OutpostBaseSimulation simulation = Started(parameters: Parameters(air: Air(maxRemoval: 0)));
            simulation.Advance(100, Demand(2));
            Assert.That(simulation.CabinCarbonDioxideKilograms,
                Is.EqualTo(Co2Mass(0.5, DefaultCabin) + 2 * Co2PerMember * 100).Within(Exact));
            Assert.That(simulation.Snapshot.LifeSupport.Cabin.Oxygen.PartialPressureKilopascals, Is.EqualTo(21).Within(Exact));
            Assert.That(simulation.Snapshot.LifeSupport.Cabin.CarbonDioxide.PartialPressureKilopascals, Is.GreaterThan(0.5));
        }

        [Test] public void CarbonDioxideRemovalDoesNotSupplyOxygen()
        {
            // No supply capacity: O2 falls at the respired rate while CO2 is still held.
            OutpostBaseSimulation simulation = Started(parameters: Parameters(air: Air(maxSupply: 0)));
            simulation.Advance(100, Demand(2));
            Assert.That(simulation.CabinOxygenKilograms, Is.EqualTo(O2Mass(21, DefaultCabin) - 2 * O2PerMember * 100).Within(Exact));
            Assert.That(simulation.Snapshot.LifeSupport.Cabin.CarbonDioxide.PartialPressureKilopascals, Is.EqualTo(0.5).Within(Exact));
            Assert.That(simulation.Ledger.OxygenStoreKilograms, Is.EqualTo(1.0), "No supply, no store use.");
        }

        [Test] public void OxygenStoreEmptiesAtTheExactInstant()
        {
            // Store 0.1 kg, two members at 2 g/s -> empty at 50 s; then O2 falls for the remaining 30 s.
            OutpostBaseSimulation simulation = Started(parameters: Parameters(air: Air(store: 0.1)));
            simulation.Advance(80, Demand(2));
            Assert.That(simulation.Ledger.OxygenStoreKilograms, Is.EqualTo(0));
            Assert.That(simulation.Ledger.OxygenSuppliedKilograms, Is.EqualTo(0.1).Within(Exact));
            Assert.That(simulation.CabinOxygenKilograms, Is.EqualTo(O2Mass(21, DefaultCabin) - 2 * O2PerMember * 30).Within(Exact));
        }

        [Test] public void SupplyRecoversTheTargetThenFollowsDemand()
        {
            // Four members (4 g/s) exceed the 3 g/s supply: -1 g/s for 100 s = 0.1 kg deficit.
            OutpostBaseSimulation simulation = Started();
            simulation.Advance(100, Demand(4));
            double target = O2Mass(21, DefaultCabin);
            Assert.That(simulation.CabinOxygenKilograms, Is.EqualTo(target - 0.1).Within(Exact));
            Assert.That(simulation.Snapshot.LifeSupport.Cabin.Oxygen.PartialPressureKilopascals, Is.LessThan(21));
            // One member: +2 g/s -> back at the target after 50 s, then supply equals demand for 30 s.
            simulation.Advance(80, Demand(1));
            Assert.That(simulation.CabinOxygenKilograms, Is.EqualTo(target).Within(Exact));
            Assert.That(simulation.Ledger.OxygenSuppliedKilograms, Is.EqualTo(0.3 + 0.003 * 50 + 0.001 * 30).Within(Exact));
        }

        [Test] public void RespirationAndExhalationStopWhenTheCabinHasNoOxygenLeft()
        {
            // Tiny O2 inventory and no supply: respiration is limited by what exists, never negative.
            var cabin = Cabin(o2Kpa: 0.01);
            double o2 = O2Mass(0.01, cabin);
            OutpostBaseSimulation simulation = Started(Config(cabin: cabin), Parameters(air: Air(store: 0)));
            double secondsToEmpty = o2 / (2 * O2PerMember);
            simulation.Advance(secondsToEmpty + 100, Demand(2));
            Assert.That(simulation.CabinOxygenKilograms, Is.EqualTo(0));
            Assert.That(simulation.Ledger.CrewOxygenIntakeKilograms, Is.EqualTo(o2).Within(Exact));
            Assert.That(simulation.Ledger.CrewCarbonDioxideReturnKilograms,
                Is.EqualTo(2 * Co2PerMember * secondsToEmpty).Within(Exact));
            Assert.That(simulation.Snapshot.LifeSupport.Cabin.Oxygen.PartialPressureKilopascals, Is.EqualTo(0));
            Assert.That(simulation.Snapshot.LifeSupport.Cabin.Oxygen.Status, Is.EqualTo(ReadingStatus.Valid),
                "A measured zero is a valid reading; it is not the same as a missing one.");
        }

        [Test] public void CarbonDioxideRemovalNeverGoesBelowTheDeclaredComposition()
        {
            // High removal capacity, accumulated CO2, then fewer crew: removal brings it back exactly to 0.5 kPa.
            OutpostBaseSimulation simulation = Started(parameters: Parameters(air: Air(maxRemoval: 0.002)));
            simulation.Advance(100, Demand(4));   // 4.8 g/s in, 2 g/s out -> +0.28 kg
            Assert.That(simulation.CabinCarbonDioxideKilograms, Is.EqualTo(Co2Mass(0.5, DefaultCabin) + 0.28).Within(Exact));
            simulation.Advance(1000, Demand(0));  // -2 g/s -> floor after 140 s, then holds
            Assert.That(simulation.CabinCarbonDioxideKilograms, Is.EqualTo(Co2Mass(0.5, DefaultCabin)).Within(Exact));
            Assert.That(simulation.Ledger.CarbonDioxideRemovedKilograms, Is.EqualTo(0.2 + 0.28).Within(Exact));
        }

        [Test] public void WithoutEssentialPowerServicesStopAndReadingsGoStale()
        {
            // Generation 0, essential 10 W from 100 J -> services and instruments run exactly 10 s.
            var power = new PowerConfiguration(100, 1000, 0, 10, 0, 1000, 1000);
            OutpostBaseSimulation simulation = Started(Config(power: power));
            simulation.Advance(5, Demand(2));
            simulation.Advance(25, Demand(2));
            double target = O2Mass(21, DefaultCabin);
            Assert.That(simulation.CabinOxygenKilograms, Is.EqualTo(target - 2 * O2PerMember * 20).Within(Exact));
            Assert.That(simulation.CabinCarbonDioxideKilograms,
                Is.EqualTo(Co2Mass(0.5, DefaultCabin) + 2 * Co2PerMember * 20).Within(Exact));
            CabinAirObservation cabin = simulation.Snapshot.LifeSupport.Cabin;
            Assert.That(cabin.Oxygen.Status, Is.EqualTo(ReadingStatus.Stale));
            Assert.That(cabin.Oxygen.ObservedAtSeconds, Is.EqualTo(5), "Last reading taken while powered.");
            Assert.That(cabin.Oxygen.PartialPressureKilopascals, Is.EqualTo(21).Within(Exact), "Last value, not zero.");
            Assert.That(cabin.CarbonDioxide.Status, Is.EqualTo(ReadingStatus.Stale));
        }

        [Test] public void ReadingsAreUnavailableWhenNeverMeasured()
        {
            // Essential load cannot be served from time zero: no instrument value exists yet.
            var power = new PowerConfiguration(0, 1000, 5, 10, 0, 1000, 1000);
            OutpostBaseSimulation simulation = Started(Config(power: power));
            CabinAirObservation cabin = simulation.Snapshot.LifeSupport.Cabin;
            Assert.That(cabin.Oxygen.Status, Is.EqualTo(ReadingStatus.Unavailable));
            Assert.That(cabin.Oxygen.PartialPressureKilopascals, Is.Null);
            Assert.That(cabin.CarbonDioxide.Status, Is.EqualTo(ReadingStatus.Unavailable));
            simulation.Advance(10, Demand(1));
            Assert.That(simulation.Snapshot.LifeSupport.Cabin.CarbonDioxide.PartialPressureKilopascals, Is.Null);
        }
    }
}
