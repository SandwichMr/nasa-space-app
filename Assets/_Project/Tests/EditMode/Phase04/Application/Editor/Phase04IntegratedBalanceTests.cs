using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Crew;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.Eva;
using Trainer.Simulation.LifeSupport;
using Trainer.Tooling.Validation.Phase04;
using Trainer.Unity.Networking.BaseOperations;
using Trainer.Unity.Validation.Phase04;
using H = Phase04LivingBaseHarness;

namespace Trainer.Tests.Phase04.Application
{
    /// <summary>Real D1/D2 composition, synthetic fixtures, simulated seconds. No sockets, build or human evidence.</summary>
    public sealed class Phase04IntegratedBalanceTests
    {
        [Test]
        public void StalledAirServiceCanBeCancelledToRechargeAndThenCompleted()
        {
            var h = new H(H.WithPower(new Trainer.Simulation.PowerConfiguration(2000, 20000, 80, 60, 60, 100, 400)));
            PlayerId player = h.Join(); h.Start(player);
            h.Walk(player, H.AtAir);
            h.Run(player, BaseOperationAction.Inspect, h.Scenario.AirStation);
            h.Run(player, BaseOperationAction.ServiceAir, h.Scenario.AirStation, OperationChoice.SwitchBed);
            h.Run(player, BaseOperationAction.Inspect, h.Scenario.AirStation);
            Assert.That(h.Do(player, BaseOperationAction.ServiceAir, h.Scenario.AirStation, OperationChoice.RegenerateStandbyBed).Accepted);
            h.Tick(30);
            Assert.That(h.Now.TryGetOperationAt(h.Scenario.AirStation, out var stalled), Is.True);
            Assert.That(stalled.Stalled, Is.True);
            Assert.That(stalled.WorkSeconds, Is.LessThan(stalled.DurationSeconds));
            Assert.That(h.Base.Snapshot.Power.EnergyJoules, Is.EqualTo(0).Within(1e-8));
            Assert.That(h.Do(player, BaseOperationAction.CancelOperation, h.Scenario.AirStation).Accepted);
            h.Tick(1);
            Assert.That(h.Base.Snapshot.Power.EnergyJoules, Is.EqualTo(20).Within(1e-8), "released service load permits recharging at 80 - 60 W");
            h.Tick(250);
            h.Run(player, BaseOperationAction.Inspect, h.Scenario.AirStation);
            h.Run(player, BaseOperationAction.ServiceAir, h.Scenario.AirStation, OperationChoice.RegenerateStandbyBed);
            Assert.That(h.Equipment.Snapshot.Air.StandbyLoadingFraction, Is.EqualTo(0));
            Assert.That(h.CountActivities(ActivityKind.AirService, ActivityStatus.Cancelled), Is.EqualTo(1));
            Assert.That(h.Now.Outpost.State, Is.EqualTo(SessionState.Running));
            Assert.That(h.Now.Epoch, Is.EqualTo(0));
            TestContext.WriteLine("Power-limited service stalled; explicit player cancel released the station, battery recharged, and regeneration completed in the same session. This does not claim the unattended pilot performs that recovery.");
        }

        [Test]
        public void AirContinuityPilotRecoversPowerStallAndRetriesService()
        {
            var h = new H(H.WithPower(new Trainer.Simulation.PowerConfiguration(2000, 20000, 80, 60, 60, 100, 400)));
            var host = h.Join();
            using (var port = new LocalBaseOperationsSessionPort(h.Authority))
            using (var intents = new BaseOperationsInteractionClient(port, h.Scenario))
            using (var actor = new PortIntentsActor(port, intents))
            {
                Assert.That(port.Join().IsJoined);
                h.Start(host);
                var pilot = new BaseOperationsContinuityScript(actor, h.Scenario, ContinuityRole.AirAndLogistics, 900);
                bool regeneratedAfterCancellation = false;
                for (int i = 0; i < 9000 && !pilot.Finished; i++)
                {
                    pilot.Update(0.1);
                    h.Authority.Tick(0.1);
                    if (pilot.Metrics.PowerStallsCancelled > 0 && h.Now.Activities.Any(a =>
                        a.Kind == ActivityKind.AirService && a.Status == ActivityStatus.Completed
                        && a.ResultKey == "standby-bed-regenerated" && a.EndedAtSeconds > 300))
                        regeneratedAfterCancellation = true;
                }

                Assert.That(regeneratedAfterCancellation, Is.True, "real regeneration must complete after the power stall, not just be accepted");
                Assert.That(pilot.Finished, Is.True, pilot.Activity);
                Assert.That(pilot.Passed, Is.True, pilot.Verdict);
                Assert.That(pilot.Metrics.PowerStallsCancelled, Is.GreaterThanOrEqualTo(1), pilot.Verdict);
                Assert.That(pilot.Metrics.AirServices, Is.GreaterThanOrEqualTo(2), pilot.Verdict);
                Assert.That(h.CountActivities(ActivityKind.AirService, ActivityStatus.Completed), Is.GreaterThanOrEqualTo(1));
                Assert.That(h.Now.TryGetOperationAt(h.Scenario.AirStation, out OperationProgress unfinished), Is.False,
                    unfinished == null ? "air station is free" : "air service remained active");
                Assert.That(h.Now.Outpost.State, Is.EqualTo(SessionState.Running));
            }
        }

        [Test]
        public void ShippedScenarioDegradesAt240AndFailsAt540WithoutRepair()
        {
            var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<Trainer.Unity.Configuration.BaseOperationsScenarioDefinition>(
                "Assets/_Project/Data/Scenarios/Phase04/Phase04_LivingBase_Synthetic.asset");
            Assert.That(definition, Is.Not.Null);
            var h = new H(definition.ToScenario());
            PlayerId first = h.Join(); h.Join(); h.Start(first);
            h.Tick(239.75);
            Assert.That(h.Equipment.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Nominal));
            h.Tick(.25);
            Assert.That(h.Time, Is.EqualTo(240).Within(1e-9));
            Assert.That(h.Equipment.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Degraded));
            h.Tick(299.75);
            Assert.That(h.Equipment.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Degraded));
            h.Tick(.25);
            Assert.That(h.Time, Is.EqualTo(540).Within(1e-9));
            Assert.That(h.Equipment.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Failed));
            Assert.That(h.Now.Outpost.State, Is.EqualTo(SessionState.Running));
            Assert.That(h.Now.Epoch, Is.EqualTo(0));
            TestContext.WriteLine("Real authority and shipped scenario: Nominal at 239.75 s, Degraded at 240 s, still Degraded at 539.75 s, Failed at 540 s; same continuous session.");
        }

        [Test]
        public void ProcessorDestinationRefusalDoesNotDebitTheBaseOrFaultTheSession()
        {
            var h = new H();
            PlayerId player = h.Join(); h.Start(player);
            h.Walk(player, H.AtWater);
            Assert.That(h.Do(player, BaseOperationAction.ProcessBatch, h.Scenario.WaterStation).Accepted);

            // Fault injection, not a production command: make the destination nonempty AFTER the initial validation.
            // The shipped single-authority scenario cannot reach this state on its own.
            Assert.That(h.Equipment.Apply(EquipmentEffect.ReceiveBatch(Guid.NewGuid(), h.Scenario.Equipment.WaterProcessor.BatchKilograms)),
                Is.EqualTo(EquipmentEffectStatus.Applied));
            double untreated = h.Base.Snapshot.LifeSupport.Water.UntreatedKilograms;
            double returns = h.Base.Ledger.CrewUntreatedWaterReturnKilograms;
            double boundary = h.Base.Ledger.TransferBoundaryOutflowKilograms;
            double hold = h.Equipment.Snapshot.WaterProcessor.HoldKilograms;
            h.Until(() => !h.Now.TryGetOperationAt(h.Scenario.WaterStation, out _), 30);
            Assert.That(h.Now.Outpost.State, Is.EqualTo(SessionState.Running));
            Assert.That(h.Base.Ledger.TransferBoundaryOutflowKilograms, Is.EqualTo(boundary), "no base debit before destination validation");
            Assert.That(h.Base.Snapshot.LifeSupport.Water.UntreatedKilograms,
                Is.EqualTo(untreated + h.Base.Ledger.CrewUntreatedWaterReturnKilograms - returns).Within(1e-9));
            Assert.That(h.Equipment.Snapshot.WaterProcessor.HoldKilograms, Is.EqualTo(hold));
            Assert.That(h.Now.Water.BatchesProcessed, Is.EqualTo(0));
            Assert.That(h.Now.Activities.Any(a => a.Status == ActivityStatus.Failed && a.ResultKey == "equipment-refused"), Is.True);
        }

        [Test]
        public void AirPilotKeepsWalkingUntilTheInspectionIsActuallyAccepted()
        {
            var h = new H();
            var first = h.Join();
            using (var port = new LocalBaseOperationsSessionPort(h.Authority))
            using (var intents = new BaseOperationsInteractionClient(port, h.Scenario))
            using (var actor = new PortIntentsActor(port, intents))
            {
                Assert.That(port.Join().IsJoined);
                h.Start(first);
                var script = new BaseOperationsContinuityScript(actor, h.Scenario, ContinuityRole.AirAndLogistics, 900);
                for (int i = 0; i < 300; i++) { script.Update(.1); h.Authority.Tick(.1); }
                Assert.That(script.Metrics.OrdersPlaced, Is.EqualTo(1));
                Assert.That(script.Metrics.Inspections, Is.GreaterThanOrEqualTo(1), "planning must not start the 45 s cooldown and stop the walk");
                Assert.That(h.Now.EquipmentOf(EquipmentKind.AirRevitalization).LatestObservation, Is.Not.Null);
            }
        }
        [Test]
        public void CoupledAuthorityCo2PartitionErrorStaysWithinTheDocumentedBound()
        {
            var coarse = new H();
            var fine = new H();
            foreach (var h in new[] { coarse, fine })
            {
                PlayerId first = h.Join(); h.Join(); h.Start(first);
            }
            coarse.Tick(300, .25);
            fine.Tick(300, .07);
            double error = Math.Abs(coarse.Base.CabinCarbonDioxideKilograms - fine.Base.CabinCarbonDioxideKilograms);
            double bound = coarse.Base.Parameters.Air.MaxCarbonDioxideRemovalKilogramsPerSecond
                * (1 - coarse.Scenario.Equipment.Air.SaturatedRemovalFactor) * .25;
            Assert.That(coarse.Equipment.Snapshot.Air.ActiveLoadingFraction, Is.EqualTo(1));
            Assert.That(fine.Equipment.Snapshot.Air.ActiveLoadingFraction, Is.EqualTo(1));
            Assert.That(error, Is.GreaterThan(1e-12), "exercise the end-of-step factor approximation, rather than a constant factor");
            Assert.That(error, Is.LessThanOrEqualTo(bound + 1e-10));
            Assert.That(Math.Abs(coarse.Base.Ledger.CarbonDioxideRemovedKilograms - fine.Base.Ledger.CarbonDioxideRemovedKilograms),
                Is.EqualTo(error).Within(1e-9), "same crew input; only removal differs");
            TestContext.WriteLine($"300 s, two real coupled authorities: 0.25 s vs 0.07 s steps; CO2 error {error:R} kg <= {bound:R} kg. SESSION_CONTRACT §6 unchanged.");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CompletedOrAbortedEvaUsesRealSuitsAndKeepsTheContinuousBase(bool completeScience)
        {
            H h = null;
            RecordingEquipmentModel equipment = null;
            EvaAudit audit = null;
            h = new H(equipmentDecorator: model => equipment = new RecordingEquipmentModel(model),
                evaDecorator: model => audit = new EvaAudit(model, () => h, () => equipment));
            PlayerId evaPlayer = h.Join(); h.Join();
            double initialMass = TrackedMass(h);
            double initialEnergy = h.Base.Snapshot.Power.EnergyJoules + h.Eva.Snapshot.Suits.Sum(s => s.Levels.BatteryJoules);
            audit.PreviousBaseEnergy = h.Base.Snapshot.Power.EnergyJoules;
            h.Start(evaPlayer);

            // Processing and routing move mass across the base/equipment boundary before the EVA boundary is exercised.
            h.Walk(evaPlayer, H.AtWater);
            h.Run(evaPlayer, BaseOperationAction.ProcessBatch, h.Scenario.WaterStation);
            h.Run(evaPlayer, BaseOperationAction.RouteProduct, h.Scenario.WaterStation, OperationChoice.AcceptToPotable);
            h.Sortie(evaPlayer, completeScience);
            Assert.That(h.Now.Outpost.State, Is.EqualTo(SessionState.Running));
            Assert.That(h.Now.Expedition.Phase, Is.EqualTo(ExpeditionPhase.Preparing));
            Assert.That(h.Now.Epoch, Is.EqualTo(0));
            Assert.That(h.Now.Session.ExpeditionsFinished, Is.EqualTo(1));
            Assert.That(h.CountActivities(ActivityKind.ScienceAssignment, ActivityStatus.Completed), Is.EqualTo(completeScience ? 1 : 0));
            Assert.That(h.Now.Activities.Single(a => a.Kind == ActivityKind.Expedition).ResultKey,
                Is.EqualTo(completeScience ? "science-returned" : "returned-without-science"));
            Assert.That(h.Crew(evaPlayer).OnSuitSupply, Is.False);
            Assert.That(audit.SealedSeconds, Is.GreaterThan(0));
            Assert.That(audit.AboardMemberSeconds, Is.LessThan(2 * h.Time), "one member really left cabin supply");
            Assert.That(audit.AboardMemberSeconds + audit.SealedSeconds, Is.EqualTo(2 * h.Time).Within(1e-7));
            Assert.That(h.Equipment.Snapshot.SimulatedTimeSeconds, Is.EqualTo(h.Time).Within(1e-8));
            Assert.That(h.Eva.Snapshot.SimulatedTimeSeconds, Is.EqualTo(h.Time).Within(1e-8));

            var b = h.Base.Ledger;
            var e = h.Eva.Ledger;
            var metabolic = h.Base.Parameters.Crew;
            Assert.That(b.CrewOxygenIntakeKilograms, Is.EqualTo(audit.AboardMemberSeconds * metabolic.OxygenConsumptionKilogramsPerSecond).Within(1e-9));
            Assert.That(b.CrewPotableWaterIntakeKilograms, Is.EqualTo(audit.AboardMemberSeconds * metabolic.PotableWaterIntakeKilogramsPerSecond).Within(1e-9));
            Assert.That(e.OxygenConsumedKilograms, Is.EqualTo(audit.OxygenDemand).Within(1e-9));
            Assert.That(e.FeedwaterConsumedKilograms, Is.EqualTo(audit.FeedwaterDemand).Within(1e-9));
            Assert.That(e.BatteryConsumedJoules, Is.EqualTo(audit.BatteryDemand).Within(1e-7));
            Assert.That(e.OxygenConsumedKilograms, Is.GreaterThan(0));
            Assert.That(e.FeedwaterFilledKilograms, Is.GreaterThan(0), "water physically transferred from base into suit");
            Assert.That(e.MakeupGasUsedKilograms, Is.GreaterThan(0), "airlock physically cycled");
            Assert.That(e.UnservedOxygenKilograms + e.UnservedFeedwaterKilograms + e.UnservedBatteryJoules, Is.EqualTo(0));

            // Tracked mass includes all base stocks, cabin gases, processor hold, suits and EVA gas stores.
            // Body intake and exhalation are explicit boundary flows; brine and airlock make-up gas leave the boundary.
            double expectedMass = initialMass - b.CrewOxygenIntakeKilograms - b.CrewPotableWaterIntakeKilograms
                - b.CrewFoodIntakeKilograms + b.CrewCarbonDioxideReturnKilograms + b.CrewUntreatedWaterReturnKilograms
                - b.CarbonDioxideRemovedKilograms - h.Equipment.Snapshot.Ledger.BrineOutflowKilograms
                - e.OxygenConsumedKilograms - e.FeedwaterConsumedKilograms - e.MakeupGasUsedKilograms;
            Assert.That(TrackedMass(h), Is.EqualTo(expectedMass).Within(1e-8), "all internal transfers cancel");
            Assert.That(BaseOperationsBalances.ProcessorResidualKilograms(h.Equipment.Snapshot), Is.EqualTo(0).Within(1e-9));

            double totalEnergy = h.Base.Snapshot.Power.EnergyJoules + h.Eva.Snapshot.Suits.Sum(s => s.Levels.BatteryJoules);
            Assert.That(totalEnergy, Is.EqualTo(initialEnergy + audit.BaseNetEnergy + e.BatteryStoredJoules - e.BatteryConsumedJoules).Within(1e-6));
            TaskDefinition charge = h.Scenario.Expedition.Outpost.Tasks.Single(t => t.Id == ExpeditionFixtures.ChargeSuit);
            Assert.That(e.BatteryStoredJoules + e.ChargeNotStoredJoules, Is.EqualTo(charge.DurationSeconds * charge.PowerWatts).Within(1e-6),
                "one completed charger task, fixture efficiency 1; no free or repeated charge");
            TestContext.WriteLine($"EVA science={completeScience}, t={h.Time:R}s, cabin={audit.AboardMemberSeconds:R} member-s, sealed={audit.SealedSeconds:R} suit-s; " +
                $"mass residual={TrackedMass(h) - expectedMass:R} kg; energy residual={totalEnergy - initialEnergy - audit.BaseNetEnergy - e.BatteryStoredJoules + e.BatteryConsumedJoules:R} J.");
        }

        private static double TrackedMass(H h)
        {
            var s = h.Eva.Snapshot;
            return BaseOperationsBalances.WaterAndFoodKilograms(h.Base.Snapshot.LifeSupport, h.Equipment.Snapshot)
                + h.Base.CabinOxygenKilograms + h.Base.CabinCarbonDioxideKilograms + h.Base.Ledger.OxygenStoreKilograms
                + s.Supply.OxygenRechargeKilograms + s.Supply.MakeupGasKilograms
                + s.Suits.Sum(x => x.Levels.OxygenKilograms + x.Levels.FeedwaterKilograms);
        }

        // Forwarding audit at the real authority's EVA hook, immediately after each base step, before task effects.
        private sealed class EvaAudit : IEvaSimulation
        {
            private readonly EvaSuitSimulation inner;
            private readonly Func<H> harness;
            private readonly Func<RecordingEquipmentModel> equipment;
            public double AboardMemberSeconds, SealedSeconds, OxygenDemand, FeedwaterDemand, BatteryDemand, BaseNetEnergy, PreviousBaseEnergy;
            private int steps;
            public EvaAudit(EvaSuitSimulation inner, Func<H> harness, Func<RecordingEquipmentModel> equipment)
            { this.inner = inner; this.harness = harness; this.equipment = equipment; }
            public EvaSnapshot Snapshot => inner.Snapshot;
            public EvaLedger Ledger => inner.Ledger;
            public void Reset(EvaConfiguration config) => inner.Reset(config);
            public double SecondsUntilServiceEvent(EvaDemand demand) => inner.SecondsUntilServiceEvent(demand);
            public EvaTransferStatus Apply(EvaTransfer transfer) => inner.Apply(transfer);
            public void Advance(double elapsed, EvaDemand demand)
            {
                H h = harness();
                var eq = equipment();
                Assert.That(eq.Advances.Count, Is.EqualTo(++steps), "equipment and EVA called exactly once per step");
                var record = eq.Advances[steps - 1];
                Assert.That(record.Elapsed, Is.EqualTo(elapsed).Within(1e-9));
                Assert.That(record.Crew + demand.Suits.Count, Is.EqualTo(2), "one cabin or suit charge per present member");
                AboardMemberSeconds += elapsed * record.Crew;
                SealedSeconds += elapsed * demand.Suits.Count;
                foreach (var suit in demand.Suits)
                    foreach (var service in h.Scenario.Expedition.Eva.Model.Services)
                    {
                        double amount = EvaSuitSimulation.Rate(service, suit.Activity, suit.ToolWatts) * elapsed;
                        if (service.Service == SuitService.Oxygen) OxygenDemand += amount;
                        if (service.Service == SuitService.Feedwater) FeedwaterDemand += amount;
                        if (service.Service == SuitService.Battery) BatteryDemand += amount;
                    }
                var b = h.Base.Snapshot;
                var p = b.Power;
                double net = p.GenerationWatts - p.EssentialServedWatts - p.ScienceServedWatts - b.ServiceLoadServedWatts;
                double gain = net >= 0 ? Math.Min(p.CapacityJoules - PreviousBaseEnergy, Math.Min(net, h.Scenario.Expedition.Outpost.Simulation.Power.MaxChargeWatts) * elapsed)
                    : net * elapsed;
                Assert.That(PreviousBaseEnergy + gain, Is.GreaterThan(0), "fixture stays away from power starvation");
                Assert.That(p.EnergyJoules, Is.EqualTo(PreviousBaseEnergy + gain).Within(1e-7), "generation minus served loads and curtailed surplus");
                BaseNetEnergy += gain;
                PreviousBaseEnergy = p.EnergyJoules;
                inner.Advance(elapsed, demand);
            }
        }
    }
}
