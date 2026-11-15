using System;
using NUnit.Framework;
using Trainer.Simulation;
using Trainer.Simulation.Crew;
using Trainer.Simulation.LifeSupport;
using static Trainer.Tests.Simulation.Phase02.LifeSupportAnalyticFixtures;

namespace Trainer.Tests.Simulation.Phase02
{
    // Water, food, task transfers, mass balance, pause and restart. Synthetic values; closed-form expectations.
    public sealed class OutpostBaseSimulationResourceTests
    {
        [Test] public void ParametersRejectInvalidValuesAndFreeWater()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Crew(o2: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Crew(water: double.NaN));
            Assert.Throws<ArgumentException>(() => Crew(water: 0.001, returned: 0.002), "Return above intake.");
            Assert.Throws<ArgumentException>(() => new CrewMetabolicDemand(0, 0, 0, 0, 0, " "));
            Assert.Throws<ArgumentOutOfRangeException>(() => Air(maxSupply: double.PositiveInfinity));
            Assert.Throws<ArgumentException>(() => new AirRevitalizationParameters(1, 1, 1, null));
            Assert.Throws<ArgumentNullException>(() => new OutpostSimulationParameters(null, Air()));
            Assert.That(CrewMetabolicDemand.FromPerCrewDay(86.4, 0, 0, 0, 0, Label).OxygenConsumptionKilogramsPerSecond,
                Is.EqualTo(0.001).Within(Exact));
        }

        [Test] public void CrewDemandConsumesWaterAndFoodAnalytically()
        {
            OutpostBaseSimulation simulation = Started();
            simulation.Advance(100, Demand(2));
            LifeSupportSnapshot life = simulation.Snapshot.LifeSupport;
            Assert.That(life.Water.PotableKilograms, Is.EqualTo(20 - 2 * WaterPerMember * 100).Within(Exact));
            Assert.That(life.Water.UntreatedKilograms, Is.EqualTo(5 + 2 * ReturnPerMember * 100).Within(Exact));
            Assert.That(life.Food.StoredKilograms, Is.EqualTo(15 - 2 * FoodPerMember * 100).Within(Exact));
            Assert.That(life.Water.PotableCapacityKilograms, Is.EqualTo(40));
            Assert.That(life.Food.CapacityKilograms, Is.EqualTo(30));
            LifeSupportLedger l = simulation.Ledger;
            Assert.That(l.CrewPotableWaterIntakeKilograms, Is.EqualTo(0.4).Within(Exact));
            Assert.That(l.CrewUntreatedWaterReturnKilograms, Is.EqualTo(0.3).Within(Exact));
            Assert.That(l.CrewFoodIntakeKilograms, Is.EqualTo(0.2).Within(Exact));
        }

        [Test] public void NoCrewAboardMeansNoCrewConsumption()
        {
            OutpostBaseSimulation simulation = Started();
            simulation.Advance(500, Demand(0));
            LifeSupportSnapshot life = simulation.Snapshot.LifeSupport;
            Assert.That(life.Water.PotableKilograms, Is.EqualTo(20));
            Assert.That(life.Water.UntreatedKilograms, Is.EqualTo(5));
            Assert.That(life.Food.StoredKilograms, Is.EqualTo(15));
            Assert.That(simulation.Ledger.CrewOxygenIntakeKilograms, Is.EqualTo(0));
            Assert.That(simulation.Ledger.OxygenStoreKilograms, Is.EqualTo(1.0));
        }

        [Test] public void PotableWaterNeverGoesNegativeAndUnservedDemandIsRecorded()
        {
            // 0.1 kg potable, one member drinking 2 g/s -> empty at exactly 50 s; return stops with drinking.
            OutpostBaseSimulation simulation = Started(Config(water: new WaterStock(0.1, 0, 40, 20)));
            simulation.Advance(80, Demand(1));
            WaterStock water = simulation.Snapshot.LifeSupport.Water;
            Assert.That(water.PotableKilograms, Is.EqualTo(0));
            Assert.That(water.UntreatedKilograms, Is.EqualTo(ReturnPerMember * 50).Within(Exact));
            Assert.That(simulation.Ledger.CrewPotableWaterIntakeKilograms, Is.EqualTo(0.1).Within(Exact));
            Assert.That(simulation.Ledger.UnservedPotableWaterKilograms, Is.EqualTo(WaterPerMember * 30).Within(Exact));
            simulation.Advance(1000, Demand(3));
            Assert.That(simulation.Snapshot.LifeSupport.Water.PotableKilograms, Is.EqualTo(0));
        }

        [Test] public void UntreatedReturnStopsAtCapacityWithoutCreatingMass()
        {
            // 0.03 kg of room, 1.5 g/s return -> full at 20 s; the remaining 40 s of return is not stored.
            OutpostBaseSimulation simulation = Started(Config(water: new WaterStock(20, 19.97, 40, 20)));
            simulation.Advance(60, Demand(1));
            Assert.That(simulation.Snapshot.LifeSupport.Water.UntreatedKilograms, Is.EqualTo(20));
            Assert.That(simulation.Ledger.CrewUntreatedWaterReturnKilograms, Is.EqualTo(0.03).Within(Exact));
            Assert.That(simulation.Ledger.UntreatedReturnNotStoredKilograms, Is.EqualTo(ReturnPerMember * 40).Within(Exact));
        }

        [Test] public void FoodNeverGoesNegativeAndUnservedDemandIsRecorded()
        {
            OutpostBaseSimulation simulation = Started(Config(food: new FoodStock(0.05, 30)));
            simulation.Advance(40, Demand(2));   // 2 g/s -> empty at 25 s
            Assert.That(simulation.Snapshot.LifeSupport.Food.StoredKilograms, Is.EqualTo(0));
            Assert.That(simulation.Ledger.UnservedFoodKilograms, Is.EqualTo(2 * FoodPerMember * 15).Within(Exact));
        }

        [Test] public void ApplyIsAllOrNothingAndOncePerId()
        {
            OutpostBaseSimulation simulation = Started();
            var id = Guid.NewGuid();
            var transfer = new ResourceTransfer(id, TreatWater());
            Assert.That(simulation.Apply(transfer), Is.EqualTo(ResourceTransferStatus.Applied));
            WaterStock water = simulation.Snapshot.LifeSupport.Water;
            Assert.That(water.UntreatedKilograms, Is.EqualTo(3).Within(Exact));
            Assert.That(water.PotableKilograms, Is.EqualTo(21.8).Within(Exact));
            Assert.That(simulation.Ledger.TransferBoundaryOutflowKilograms, Is.EqualTo(0.2).Within(Exact));
            Assert.That(simulation.Snapshot.Power.SimulatedTimeSeconds, Is.EqualTo(0), "Apply does not advance time.");

            OutpostSimulationSnapshot afterFirst = simulation.Snapshot;
            Assert.That(simulation.Apply(new ResourceTransfer(id, TreatWater())), Is.EqualTo(ResourceTransferStatus.AlreadyApplied));
            var other = new ResourceEffect(new[] { new ResourceChange(ResourceKind.StoredFood, -1) }, 1);
            Assert.That(simulation.Apply(new ResourceTransfer(id, other)), Is.EqualTo(ResourceTransferStatus.IdConflict));
            Assert.That(simulation.Snapshot, Is.SameAs(afterFirst), "Duplicates change nothing.");
        }

        [Test] public void InsufficientStockAndCapacityLeaveEveryStockUnchanged()
        {
            OutpostBaseSimulation simulation = Started(Config(water: new WaterStock(39, 5, 40, 20)));
            OutpostSimulationSnapshot before = simulation.Snapshot;
            // Needs 16 kg food, only 15: nothing applies, not even the water part.
            var tooMuchFood = new ResourceEffect(new[]
            {
                new ResourceChange(ResourceKind.StoredFood, -16),
                new ResourceChange(ResourceKind.UntreatedWater, -1)
            }, 17);
            Assert.That(simulation.Apply(new ResourceTransfer(Guid.NewGuid(), tooMuchFood)),
                Is.EqualTo(ResourceTransferStatus.InsufficientStock));
            Assert.That(simulation.Snapshot, Is.SameAs(before));
            var overfill = new ResourceEffect(new[]
            {
                new ResourceChange(ResourceKind.UntreatedWater, -1.0),
                new ResourceChange(ResourceKind.PotableWater, 1.0)
            }, 0);
            var overfillTransfer = new ResourceTransfer(Guid.NewGuid(), overfill);
            Assert.That(simulation.Apply(overfillTransfer), Is.EqualTo(ResourceTransferStatus.Applied));
            OutpostSimulationSnapshot full = simulation.Snapshot;
            Assert.That(full.LifeSupport.Water.PotableKilograms, Is.EqualTo(40));
            Assert.That(simulation.Apply(new ResourceTransfer(Guid.NewGuid(), overfill)),
                Is.EqualTo(ResourceTransferStatus.ExceedsCapacity));
            Assert.That(simulation.Snapshot, Is.SameAs(full));
            Assert.That(before.LifeSupport.Water.PotableKilograms, Is.EqualTo(39), "Published snapshots are immutable.");
        }

        [Test] public void MassBalanceClosesOverAMixedRun()
        {
            OutpostBaseSimulation simulation = Started(Config(water: new WaterStock(1, 19, 40, 20), food: new FoodStock(0.4, 30)),
                Parameters(air: Air(store: 0.3, maxSupply: 0.0025, maxRemoval: 0.002)));
            double initial = TrackedMass(simulation);
            int[] crew = { 1, 3, 0, 2, 4, 1 };
            for (int i = 0; i < crew.Length; i++)
            {
                simulation.Advance(37.5 + 11 * i, Demand(crew[i], i % 2 == 0, 20));
                if (i == 2) Assert.That(simulation.Apply(new ResourceTransfer(Guid.NewGuid(), TreatWater())),
                    Is.EqualTo(ResourceTransferStatus.Applied));
                Assert.That(TrackedMass(simulation), Is.EqualTo(PredictedTrackedMass(initial, simulation.Ledger)).Within(Exact));
            }
            LifeSupportLedger l = simulation.Ledger;
            Assert.That(l.OxygenStoreKilograms, Is.EqualTo(0.3 - l.OxygenSuppliedKilograms).Within(Exact));
            Assert.That(l.UnservedPotableWaterKilograms + l.UnservedFoodKilograms, Is.GreaterThan(0),
                "The run reaches empty stocks, so the limit branch is exercised.");
        }

        [Test] public void PausingMeansNotAdvancingAndResumingMatchesAContinuousRun()
        {
            // Pause is the authority not calling Advance: nothing is consumed while paused.
            OutpostBaseSimulation paused = Started();
            paused.Advance(30, Demand(2, false, 50));
            OutpostSimulationSnapshot atPause = paused.Snapshot;
            paused.Advance(0, Demand(2, false, 50));
            Assert.That(paused.Snapshot, Is.SameAs(atPause));
            paused.Advance(70, Demand(2, false, 50));

            OutpostBaseSimulation continuous = Started();
            continuous.Advance(100, Demand(2, false, 50));
            Assert.That(paused.Snapshot.LifeSupport.Water.PotableKilograms,
                Is.EqualTo(continuous.Snapshot.LifeSupport.Water.PotableKilograms).Within(Exact));
            Assert.That(paused.Snapshot.Power.EnergyJoules, Is.EqualTo(continuous.Snapshot.Power.EnergyJoules).Within(Exact));
            Assert.That(paused.CabinCarbonDioxideKilograms, Is.EqualTo(continuous.CabinCarbonDioxideKilograms).Within(Exact));
        }

        [Test] public void ResetRestoresInitialStateAndForgetsAppliedIds()
        {
            OutpostConfiguration config = Config();
            OutpostBaseSimulation simulation = Started(config);
            OutpostSimulationSnapshot initial = simulation.Snapshot;
            var transfer = new ResourceTransfer(Guid.NewGuid(), TreatWater());
            simulation.Apply(transfer);
            simulation.Advance(120, Demand(3, true, 30));

            simulation.Reset(config);
            OutpostSimulationSnapshot s = simulation.Snapshot;
            Assert.That(s.Power.SimulatedTimeSeconds, Is.EqualTo(0));
            Assert.That(s.Power.EnergyJoules, Is.EqualTo(initial.Power.EnergyJoules));
            Assert.That(s.Power.ScienceRequestedWatts, Is.EqualTo(0));
            Assert.That(s.ServiceLoadRequestedWatts, Is.EqualTo(0));
            Assert.That(s.LifeSupport.Water.PotableKilograms, Is.EqualTo(20));
            Assert.That(s.LifeSupport.Water.UntreatedKilograms, Is.EqualTo(5));
            Assert.That(s.LifeSupport.Food.StoredKilograms, Is.EqualTo(15));
            Assert.That(s.LifeSupport.Cabin.Oxygen.PartialPressureKilopascals, Is.EqualTo(21).Within(Exact));
            Assert.That(s.LifeSupport.Cabin.Oxygen.ObservedAtSeconds, Is.EqualTo(0));
            Assert.That(simulation.LastFullyServedServiceSeconds, Is.EqualTo(0));
            Assert.That(simulation.LastFullyServedScienceSeconds, Is.EqualTo(0));
            Assert.That(simulation.Ledger.CrewOxygenIntakeKilograms, Is.EqualTo(0));
            Assert.That(simulation.Ledger.TransferBoundaryOutflowKilograms, Is.EqualTo(0));
            Assert.That(simulation.Ledger.OxygenStoreKilograms, Is.EqualTo(1.0));
            Assert.That(simulation.Apply(transfer), Is.EqualTo(ResourceTransferStatus.Applied), "Ids are per Reset.");
        }

        [Test] public void SyntheticTrainingSetCreatesNoFoodCrisisInAShortMission()
        {
            // Four members for 30 simulated minutes with the common fixture's stocks (15 kg food, 20 kg potable).
            OutpostBaseSimulation simulation = Started(
                Config(water: new WaterStock(20, 10, 40, 20), food: new FoodStock(15, 30)),
                OutpostSimulationParameters.SyntheticTraining());
            simulation.Advance(1800, Demand(4));
            LifeSupportSnapshot life = simulation.Snapshot.LifeSupport;
            Assert.That(life.Food.StoredKilograms, Is.EqualTo(15 - 4 * 1.5 * 1800 / 86400).Within(Exact));
            Assert.That(life.Food.StoredKilograms / 15, Is.GreaterThan(0.99));
            Assert.That(simulation.Ledger.UnservedFoodKilograms, Is.EqualTo(0));
            StringAssert.StartsWith("SINTÉTICO", simulation.Parameters.Crew.Provenance);
        }
    }
}
