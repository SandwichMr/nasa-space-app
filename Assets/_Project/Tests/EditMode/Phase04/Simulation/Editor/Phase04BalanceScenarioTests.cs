using System;
using System.Collections.Generic;
using NUnit.Framework;
using Trainer.Simulation;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.Eva;
using Trainer.Simulation.LifeSupport;
using static Trainer.Tests.Simulation.Phase02.LifeSupportAnalyticFixtures;
using static Trainer.Tests.Simulation.Phase04.Phase04SimulationFixtures;

namespace Trainer.Tests.Simulation.Phase04
{
    /// <summary>
    /// D1 phase 4: paired transfers, boundary audits, crew partition, clock continuity and a reproducible
    /// stable → fault → scarcity → repair → resupply run. The <see cref="Harness"/> below is a TEST DRIVER that calls the
    /// two D1 models in the documented step order; it is not the session authority (D2) and decides nothing in production.
    /// </summary>
    public sealed class Phase04BalanceScenarioTests
    {
        // ------------------------------------------------------------ paired transfers

        [Test] public void TransferBuildersBalanceAndCrossTheBoundaryInTheDeclaredDirection()
        {
            Guid id = Id(1);
            ResourceTransfer intake = BaseOperationsTransfers.BatchIntake(id, 2);
            Assert.That(intake.Id, Is.EqualTo(id));
            Assert.That(intake.Effect.DeltaFor(ResourceKind.UntreatedWater), Is.EqualTo(-2));
            Assert.That(intake.Effect.BoundaryOutflowKilograms, Is.EqualTo(2), "into the processor = out of the base");
            ResourceTransfer potable = BaseOperationsTransfers.ProductToPotable(id, 1.8);
            Assert.That(potable.Effect.DeltaFor(ResourceKind.PotableWater), Is.EqualTo(1.8));
            Assert.That(potable.Effect.BoundaryOutflowKilograms, Is.EqualTo(-1.8));
            ResourceTransfer reprocess = BaseOperationsTransfers.ProductToUntreated(id, 1.5);
            Assert.That(reprocess.Effect.DeltaFor(ResourceKind.UntreatedWater), Is.EqualTo(1.5));
            ResourceTransfer recheck = BaseOperationsTransfers.RecheckPotable(id, 3);
            Assert.That(recheck.Effect.DeltaFor(ResourceKind.PotableWater), Is.EqualTo(-3));
            Assert.That(recheck.Effect.DeltaFor(ResourceKind.UntreatedWater), Is.EqualTo(3));
            Assert.That(recheck.Effect.BoundaryOutflowKilograms, Is.EqualTo(0));
            ResourceTransfer delivery = BaseOperationsTransfers.DeliveryInflow(id, 10, 2);
            Assert.That(delivery.Effect.DeltaFor(ResourceKind.PotableWater), Is.EqualTo(10));
            Assert.That(delivery.Effect.DeltaFor(ResourceKind.StoredFood), Is.EqualTo(2));
            Assert.That(delivery.Effect.BoundaryOutflowKilograms, Is.EqualTo(-12));
            Assert.That(BaseOperationsTransfers.DeliveryInflow(id, 0, 0), Is.Null, "nothing fits: no transfer");
            Assert.Throws<ArgumentOutOfRangeException>(() => BaseOperationsTransfers.BatchIntake(id, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => BaseOperationsTransfers.DeliveryInflow(id, -1, 0));
            Assert.Throws<ArgumentException>(() => BaseOperationsTransfers.BatchIntake(Guid.Empty, 2));
        }

        [Test] public void DerivedIdsAreDeterministicDistinctAndNeverEmpty()
        {
            Guid command = Id(42);
            Guid water = BaseOperationsTransfers.DeriveId(command, "unload-mass");
            Assert.That(BaseOperationsTransfers.DeriveId(command, "unload-mass"), Is.EqualTo(water));
            Assert.That(BaseOperationsTransfers.DeriveId(command, "unload-spare-processor-filter"), Is.Not.EqualTo(water));
            Assert.That(BaseOperationsTransfers.DeriveId(Id(43), "unload-mass"), Is.Not.EqualTo(water));
            Assert.That(water, Is.Not.EqualTo(command));
            Assert.That(water, Is.Not.EqualTo(Guid.Empty));
            Assert.Throws<ArgumentException>(() => BaseOperationsTransfers.DeriveId(Guid.Empty, "x"));
            Assert.Throws<ArgumentException>(() => BaseOperationsTransfers.DeriveId(command, ""));
        }

        [Test] public void DeliveryFitsWhatTheStoresCanHoldAndLeavesTheRestRemaining()
        {
            Assert.That(BaseOperationsTransfers.Fit(10, 37, 40), Is.EqualTo(3));
            Assert.That(BaseOperationsTransfers.Fit(10, 40, 40), Is.EqualTo(0));
            Assert.That(BaseOperationsTransfers.Fit(2, 10, 40), Is.EqualTo(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => BaseOperationsTransfers.Fit(-1, 0, 40));

            OutpostBaseSimulation outpost = Started(Config(water: new WaterStock(37, 5, 40, 20)));
            double fits = BaseOperationsTransfers.Fit(10, outpost.Snapshot.LifeSupport.Water.PotableKilograms, 40);
            Assert.That(outpost.Apply(BaseOperationsTransfers.DeliveryInflow(Id(1), fits, 0)), Is.EqualTo(ResourceTransferStatus.Applied));
            Assert.That(outpost.Snapshot.LifeSupport.Water.PotableKilograms, Is.EqualTo(40));
            Assert.That(outpost.Apply(BaseOperationsTransfers.DeliveryInflow(Id(2), 10, 0)), Is.EqualTo(ResourceTransferStatus.ExceedsCapacity), "an unfitted delivery is refused whole");
            Assert.That(outpost.Snapshot.LifeSupport.Water.PotableKilograms, Is.EqualTo(40));
        }

        [Test] public void ADuplicatedUnloadDeliversOnce()
        {
            OutpostBaseSimulation outpost = Started();
            BaseEquipmentSimulation equipment = StartedEquipment();
            Guid command = Id(9);
            Guid mass = BaseOperationsTransfers.DeriveId(command, "unload-mass");
            Guid spare = BaseOperationsTransfers.DeriveId(command, "unload-spare-" + ProcessorFilter);
            for (int replay = 0; replay < 3; replay++)
            {
                ResourceTransferStatus m = outpost.Apply(BaseOperationsTransfers.DeliveryInflow(mass, 5, 1));
                EquipmentEffectStatus s = equipment.Apply(EquipmentEffect.ReceiveSpares(spare, ProcessorFilter, 1));
                Assert.That(m, Is.EqualTo(replay == 0 ? ResourceTransferStatus.Applied : ResourceTransferStatus.AlreadyApplied));
                Assert.That(s, Is.EqualTo(replay == 0 ? EquipmentEffectStatus.Applied : EquipmentEffectStatus.AlreadyApplied));
            }
            Assert.That(outpost.Snapshot.LifeSupport.Water.PotableKilograms, Is.EqualTo(25));
            Assert.That(outpost.Snapshot.LifeSupport.Food.StoredKilograms, Is.EqualTo(16));
            Assert.That(SpareCount(equipment.Snapshot, ProcessorFilter), Is.EqualTo(2));
            Assert.That(outpost.Apply(BaseOperationsTransfers.DeliveryInflow(mass, 6, 1)), Is.EqualTo(ResourceTransferStatus.IdConflict));
        }

        [Test] public void ARefusedHalfOfAPairLeavesBothModelsUntouched()
        {
            // Not enough untreated water: the base refuses the intake, so the driver never sends ReceiveBatch.
            OutpostBaseSimulation outpost = Started(Config(water: new WaterStock(20, 1, 40, 20)));
            BaseEquipmentSimulation equipment = StartedEquipment();
            OutpostSimulationSnapshot baseBefore = outpost.Snapshot;
            BaseEquipmentSnapshot equipmentBefore = equipment.Snapshot;
            Assert.That(outpost.Apply(BaseOperationsTransfers.BatchIntake(Id(1), 2)), Is.EqualTo(ResourceTransferStatus.InsufficientStock));
            Assert.That(outpost.Snapshot, Is.SameAs(baseBefore));
            Assert.That(equipment.Snapshot, Is.SameAs(equipmentBefore));
            Assert.That(outpost.Apply(BaseOperationsTransfers.BatchIntake(Id(1), 1)), Is.EqualTo(ResourceTransferStatus.Applied), "the refused id was not consumed");
        }

        // ------------------------------------------------------------ audits

        [Test] public void CrewPartitionAuditDetectsDoubleChargesAndMismatchedDemands()
        {
            var oneSuit = new EvaDemand(new[] { new SuitDemand(0, SuitActivity.Walk, 0) });
            Assert.That(BaseOperationsBalances.CrewPartitionError(3, Demand(2), oneSuit, Crew(2)), Is.Null);
            Assert.That(BaseOperationsBalances.CrewPartitionError(3, Demand(3), oneSuit, Crew(3)), Is.Not.Null, "one member charged twice");
            Assert.That(BaseOperationsBalances.CrewPartitionError(3, Demand(1), oneSuit, Crew(1)), Is.Not.Null, "one member charged nowhere");
            Assert.That(BaseOperationsBalances.CrewPartitionError(3, Demand(2), oneSuit, Crew(3)), Is.Not.Null, "beds and cabin disagree");
            Assert.That(BaseOperationsBalances.CrewPartitionError(2, Demand(2), EvaDemand.None, Crew(2)), Is.Null);
            Assert.Throws<ArgumentNullException>(() => BaseOperationsBalances.CrewPartitionError(1, null, EvaDemand.None, Crew(1)));
        }

        [Test] public void TheCombinedAuditCatchesAHalfAppliedPair()
        {
            OutpostBaseSimulation outpost = Started();
            BaseEquipmentSimulation equipment = StartedEquipment();
            double initial = BaseOperationsBalances.WaterAndFoodKilograms(outpost.Snapshot.LifeSupport, equipment.Snapshot);
            outpost.Apply(BaseOperationsTransfers.BatchIntake(Id(1), 2));
            double broken = BaseOperationsBalances.WaterAndFoodResidualKilograms(initial, outpost.Snapshot.LifeSupport, outpost.Ledger, equipment.Snapshot, 0, 0);
            Assert.That(broken, Is.EqualTo(-2).Within(Exact), "2 kg left the base and never reached the processor");
            equipment.Apply(EquipmentEffect.ReceiveBatch(Id(1), 2));
            double closed = BaseOperationsBalances.WaterAndFoodResidualKilograms(initial, outpost.Snapshot.LifeSupport, outpost.Ledger, equipment.Snapshot, 0, 0);
            Assert.That(closed, Is.EqualTo(0).Within(Exact));
        }

        // ------------------------------------------------------------ clock continuity

        [Test] public void PauseMeansNoAdvanceAndTheModelsHaveNoClockOfTheirOwn()
        {
            var h = new Harness();
            h.Run(50, aboard: 2, process: false);
            BaseEquipmentSnapshot equipment = h.Equipment.Snapshot;
            OutpostSimulationSnapshot outpost = h.Outpost.Snapshot;
            // Paused: the authority does not call Advance. A zero step changes nothing either.
            h.Equipment.Advance(0, Crew(2));
            h.Outpost.Advance(0, Demand(2));
            Assert.That(h.Equipment.Snapshot, Is.SameAs(equipment));
            Assert.That(h.Outpost.Snapshot, Is.SameAs(outpost));
            h.Run(50, aboard: 2, process: false);
            Assert.That(h.Equipment.Snapshot.SimulatedTimeSeconds, Is.EqualTo(100).Within(Exact));
            Assert.That(h.Outpost.Snapshot.Power.SimulatedTimeSeconds, Is.EqualTo(100).Within(Exact));
        }

        [Test] public void LeavingAndReturningFromEvaKeepsOneClockAndChargesEachMemberOnce()
        {
            var h = new Harness();
            h.Run(50, aboard: 2, process: false);
            h.Run(100, aboard: 1, process: false); // one member sealed in a suit: charged to the suit model, not here
            h.Run(50, aboard: 2, process: false);
            // Bed: 0.3 + 0.002 × (2·50 + 1·100 + 2·50) = 0.9; potable: 20 − 0.002 × 300 member-seconds = 19.4.
            Assert.That(h.Equipment.Snapshot.SimulatedTimeSeconds, Is.EqualTo(200).Within(Exact));
            Assert.That(h.Equipment.Snapshot.Air.ActiveLoadingFraction, Is.EqualTo(0.9).Within(Exact));
            Assert.That(h.Outpost.Snapshot.LifeSupport.Water.PotableKilograms, Is.EqualTo(19.4).Within(Exact));
            Assert.That(h.Outpost.Ledger.CrewOxygenIntakeKilograms, Is.EqualTo(0.3).Within(Exact));
        }

        [Test] public void FinishingABatchDoesNotResetStocksOrTheClock()
        {
            var h = new Harness();
            h.Run(10, aboard: 2, process: false);
            double untreated = h.Outpost.Snapshot.LifeSupport.Water.UntreatedKilograms;
            h.ProcessOneBatch();
            Assert.That(h.Equipment.Snapshot.SimulatedTimeSeconds, Is.EqualTo(10).Within(Exact));
            Assert.That(h.Outpost.Snapshot.Power.SimulatedTimeSeconds, Is.EqualTo(10).Within(Exact));
            Assert.That(h.Outpost.Snapshot.LifeSupport.Water.UntreatedKilograms, Is.EqualTo(untreated - 2).Within(Exact), "nominal product went to the potable tank");
            Assert.That(h.Residual(), Is.EqualTo(0).Within(1e-9));
        }

        // ------------------------------------------------------------ reproducible run

        [Test] public void StableFaultScarcityRepairAndResupplyIsReproducibleAndConserved()
        {
            // Scenario timeline (synthetic): degraded at 1500 s, repaired at 2200 s, 10 kg potable + 2 kg food unloaded at 2600 s.
            Harness a = Harness.FullRun(repairAt: 2200, resupplyAt: 2600);
            Harness b = Harness.FullRun(repairAt: 2200, resupplyAt: 2600);
            Assert.That(b.Outpost.Snapshot.LifeSupport.Water.PotableKilograms, Is.EqualTo(a.Outpost.Snapshot.LifeSupport.Water.PotableKilograms), "bit-for-bit reproducible");
            Assert.That(b.Equipment.Snapshot.Ledger.BrineOutflowKilograms, Is.EqualTo(a.Equipment.Snapshot.Ledger.BrineOutflowKilograms));
            Assert.That(a.MaxAbsResidual, Is.LessThan(1e-9), "water + food closes at every step");
            Assert.That(a.MaxAbsProcessorResidual, Is.LessThan(1e-9));

            // Degraded window: off-spec product is reprocessed, so nothing new reaches the potable tank.
            Assert.That(a.ReleasedToPotableAt(1499), Is.GreaterThan(0), "stable phase: nominal product reaches the potable tank");
            Assert.That(a.ReleasedToPotableAt(2199), Is.EqualTo(a.ReleasedToPotableAt(1501)));
            Assert.That(a.PotableAt(2199), Is.LessThan(a.PotableAt(1501)));
            Assert.That(a.BrineAt(2199), Is.GreaterThan(a.BrineAt(1501)), "each reprocess loop loses water");
            // After repair the potable tank receives product again.
            Assert.That(a.ReleasedToPotableAt(2599), Is.GreaterThan(a.ReleasedToPotableAt(2201)));
            // Resupply recovers margin...
            Assert.That(a.PotableAt(2600), Is.GreaterThan(a.PotableAt(2599) + 9.9));
            Assert.That(a.Outpost.Ledger.UnservedPotableWaterKilograms, Is.EqualTo(0));
            // ...but recycling with losses is not self-sufficient: without deliveries base water keeps falling.
            Assert.That(a.TotalWaterAt(5000), Is.LessThan(a.TotalWaterAt(2600)));
            Assert.That(a.TotalWaterAt(2599), Is.LessThan(a.TotalWaterAt(0)));
            // Food only enters with the delivery; no station makes it.
            Assert.That(a.Outpost.Snapshot.LifeSupport.Food.StoredKilograms,
                Is.EqualTo(15 + 2 - a.Outpost.Ledger.CrewFoodIntakeKilograms).Within(1e-9));
        }

        [Test] public void WithoutRepairOrResupplyTheBaseRunsOutOfPotableWater()
        {
            Harness control = Harness.FullRun(repairAt: null, resupplyAt: null, seconds: 12000);
            Assert.That(control.Equipment.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Failed));
            Assert.That(control.Outpost.Snapshot.LifeSupport.Water.PotableKilograms, Is.EqualTo(0));
            Assert.That(control.Outpost.Ledger.UnservedPotableWaterKilograms, Is.GreaterThan(0), "scarcity is reported, not hidden");
            Assert.That(control.MaxAbsResidual, Is.LessThan(1e-9));
        }

        /// <summary>TEST DRIVER (not an authority): steps both D1 models in the documented order with 1 s steps and runs
        /// the batch → route loop instantly when the processor can take a batch. Phase 2 analytic fixture: per member
        /// potable 2 g/s, return 1.5 g/s, food 1 g/s; base potable 20 / untreated 5 / food 15 kg.</summary>
        private sealed class Harness
        {
            public readonly OutpostBaseSimulation Outpost = Started();
            public readonly BaseEquipmentSimulation Equipment =
                Phase04SimulationFixtures.StartedEquipment(Phase04SimulationFixtures.Equipment(degradeAt: 1500, failAfter: 1500));
            private readonly double initial;
            private readonly List<double[]> history = new List<double[]>();
            private double delivered;
            private int nextId = 1000;
            public double MaxAbsResidual, MaxAbsProcessorResidual;

            public Harness()
            {
                initial = BaseOperationsBalances.WaterAndFoodKilograms(Outpost.Snapshot.LifeSupport, Equipment.Snapshot);
                Record();
            }

            public static Harness FullRun(int? repairAt, int? resupplyAt, int seconds = 5000)
            {
                var h = new Harness();
                for (int t = 1; t <= seconds; t++)
                {
                    h.Run(1, aboard: 2, process: true);
                    if (t == repairAt) RepairProcessor(h.Equipment, 1);
                    if (t == resupplyAt) h.Deliver(10, 2);
                    h.Record();
                }
                return h;
            }

            public void Run(double seconds, int aboard, bool process)
            {
                var equipmentDemand = new EquipmentDemand(aboard);
                var baseDemand = new OutpostDemand(aboard, false, 0);
                Assert.That(BaseOperationsBalances.CrewPartitionError(aboard, baseDemand, EvaDemand.None, equipmentDemand), Is.Null);
                Equipment.Advance(seconds, equipmentDemand);                                   // 1
                Outpost.SetCarbonDioxideRemovalFactor(Equipment.Snapshot.Air.RemovalFactor);  // 2
                Outpost.Advance(seconds, baseDemand);                                          // 3
                if (process) ProcessOneBatch();
                Track();
            }

            public void ProcessOneBatch()
            {
                WaterProcessorState w = Equipment.Snapshot.WaterProcessor;
                double batch = 2.0;
                if (w.HoldKilograms > 0 || w.Operating != EquipmentOperatingState.Online || w.Condition == EquipmentCondition.Failed) return;
                if (Outpost.Snapshot.LifeSupport.Water.UntreatedKilograms < batch) return;
                Guid id = Next();
                Assert.That(Outpost.Apply(BaseOperationsTransfers.BatchIntake(id, batch)), Is.EqualTo(ResourceTransferStatus.Applied));
                Assert.That(Equipment.Apply(EquipmentEffect.ReceiveBatch(id, batch)), Is.EqualTo(EquipmentEffectStatus.Applied));
                Track();
                w = Equipment.Snapshot.WaterProcessor;
                Guid route = Next();
                WaterStock water = Outpost.Snapshot.LifeSupport.Water;
                if (w.ProductConductivity.Value <= 40 && BaseOperationsTransfers.Fit(w.HoldKilograms, water.PotableKilograms, water.PotableCapacityKilograms) == w.HoldKilograms)
                {
                    Assert.That(Equipment.Apply(EquipmentEffect.ReleaseToPotable(route, w.HoldKilograms)), Is.EqualTo(EquipmentEffectStatus.Applied));
                    Assert.That(Outpost.Apply(BaseOperationsTransfers.ProductToPotable(route, w.HoldKilograms)), Is.EqualTo(ResourceTransferStatus.Applied));
                }
                else if (BaseOperationsTransfers.Fit(w.HoldKilograms, water.UntreatedKilograms, water.UntreatedCapacityKilograms) == w.HoldKilograms)
                {
                    Assert.That(Equipment.Apply(EquipmentEffect.ReleaseForReprocess(route, w.HoldKilograms)), Is.EqualTo(EquipmentEffectStatus.Applied));
                    Assert.That(Outpost.Apply(BaseOperationsTransfers.ProductToUntreated(route, w.HoldKilograms)), Is.EqualTo(ResourceTransferStatus.Applied));
                }
                Track();
            }

            public void Deliver(double potableKg, double foodKg)
            {
                LifeSupportSnapshot life = Outpost.Snapshot.LifeSupport;
                double water = BaseOperationsTransfers.Fit(potableKg, life.Water.PotableKilograms, life.Water.PotableCapacityKilograms);
                double food = BaseOperationsTransfers.Fit(foodKg, life.Food.StoredKilograms, life.Food.CapacityKilograms);
                ResourceTransfer transfer = BaseOperationsTransfers.DeliveryInflow(Next(), water, food);
                Assert.That(Outpost.Apply(transfer), Is.EqualTo(ResourceTransferStatus.Applied));
                delivered += water + food;
                Track();
            }

            public double Residual() =>
                BaseOperationsBalances.WaterAndFoodResidualKilograms(initial, Outpost.Snapshot.LifeSupport, Outpost.Ledger, Equipment.Snapshot, delivered, 0);

            private void Track()
            {
                MaxAbsResidual = Math.Max(MaxAbsResidual, Math.Abs(Residual()));
                MaxAbsProcessorResidual = Math.Max(MaxAbsProcessorResidual, Math.Abs(BaseOperationsBalances.ProcessorResidualKilograms(Equipment.Snapshot)));
            }

            private void Record()
            {
                WaterStock w = Outpost.Snapshot.LifeSupport.Water;
                EquipmentLedger l = Equipment.Snapshot.Ledger;
                history.Add(new[] { w.PotableKilograms, w.PotableKilograms + w.UntreatedKilograms + Equipment.Snapshot.WaterProcessor.HoldKilograms,
                    l.ReleasedToPotableKilograms, l.BrineOutflowKilograms });
            }

            private Guid Next() => Id(nextId++);
            public double PotableAt(int t) => history[t][0];
            public double TotalWaterAt(int t) => history[t][1];
            public double ReleasedToPotableAt(int t) => history[t][2];
            public double BrineAt(int t) => history[t][3];
        }
    }
}
