using System;
using System.Collections.Generic;
using System.Linq;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Development;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;
using Trainer.Unity.Validation.Phase04;
using static Trainer.Tooling.Validation.Phase04.Phase04Checks;

namespace Trainer.Tooling.Validation.Phase04
{
    /// <summary>
    /// D5 integral suite of the REAL phase 4 authority in memory (kind <see cref="Phase04Kinds.AuthorityInMemory"/>): two
    /// identities from Join on the authority itself, the labelled synthetic fixture scenario, the authority's own tick, and the
    /// continuity stream validator on every published snapshot. NO network and NO port adapters (that is
    /// <see cref="RunThroughLocalPorts"/>, kind <see cref="Phase04Kinds.PortsInMemory"/>), NO sockets, NO scene and NO people.
    /// Every check is Pending (never Passed) while the implementation it needs is missing.
    /// </summary>
    public static class BaseOperationsAuthorityConformance
    {
        private const double Tol = 1e-6;
        private const string K = Phase04Kinds.AuthorityInMemory;

        public static List<Phase04Check> RunAll(Phase04ImplementationDiscovery d)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            return new List<Phase04Check>
            {
                Run("composition built by the discovery", K, () =>
                {
                    AuthorityInstance i = d.CreateAuthority(BaseOperationsFixtures.Scenario());
                    (i.Authority as IDisposable)?.Dispose();
                    return i.Composition + (i.Base == null ? " (concrete base ledger; equipment effects recorded by the forwarding spy)" : "");
                }),
                Run("stable operation: two players keep the base for 15 simulated minutes; balances through the composed flow", K, () => StableOperation(d)),
                Run("low reserve by real consumption, then resupply and recovery", K, () => ReserveAndResupply(d)),
                Run("recycler fault: diagnose, isolate, spare, test, restore; lost water stays lost", K, () => RecyclerRepair(d)),
                Run("two station interactions: distinct confirmed decisions; forged or out-of-order intents refused without effect", K, () => Interactions(d)),
                Run("several activities completed and cancelled without closing or resetting the session", K, () => Activities(d)),
                Run("EVA sortie completed and aborted inside the continuous session", K, () => Phase04EvaConformance.Run(d)),
                Run("two users compete for a station and a spare; repeated and conflicting command ids", K, () => Competition(d)),
                Run("pause, leaving during a repair, late join with current state, restart to a new epoch", K, () => PauseLeaveJoinRestart(d)),
                Run("one clock and no duplicate cabin consumption (suit consumption also exercised by the EVA check)", K, () => SingleConsumption(d)),
                Run("unknown or old readings are never zero or normal", K, () => UnknownReadings(d))
            };
        }

        // ---- scenarios ---------------------------------------------------------------------------------------------

        private static string StableOperation(Phase04ImplementationDiscovery d)
        {
            BaseOperationsScenario scenario = BaseOperationsFixtures.Scenario();
            using (Phase04Session s = Phase04Session.Create(d, scenario, K))
            {
                AuthorityActor a = s.Join(), b = s.Join();
                s.Resume(a);
                var water = new BaseOperationsContinuityScript(a, scenario, ContinuityRole.Water, 900);
                var air = new BaseOperationsContinuityScript(b, scenario, ContinuityRole.AirAndLogistics, 900);
                s.Advance(905, dt => { water.Update(dt); air.Update(dt); });
                Require(water.Finished && air.Finished, "pilots did not finish: " + water.Activity + " / " + air.Activity);
                Require(water.Passed, "water pilot: " + water.Verdict);
                Require(air.Passed, "air/logistics pilot: " + air.Verdict);
                RequireNoStreamViolation(s);
                string balances = Balances(s);
                return $"water [{water.Metrics.Summary()}]; air [{air.Metrics.Summary()}]; {balances}";
            }
        }

        private static string ReserveAndResupply(Phase04ImplementationDiscovery d)
        {
            // Same synthetic model/rates, with initial water just above reserve so real consumption reaches it in a bounded run.
            BaseOperationsScenario original = BaseOperationsFixtures.Scenario(null);
            var oldExpedition = original.Expedition;
            var oldOutpost = oldExpedition.Outpost;
            var life = oldOutpost.Simulation.LifeSupport;
            var water = life.InitialWater;
            var stocks = new WaterStock(original.GaugeRuleOf(GaugeKind.PotableWater).ReserveAt.Value + .1,
                water.UntreatedKilograms, water.PotableCapacityKilograms, water.UntreatedCapacityKilograms);
            var outpost = new OutpostScenario(oldOutpost.Id, oldOutpost.Provenance, oldOutpost.Layout, oldOutpost.Movement, oldOutpost.Tasks,
                new OutpostConfiguration(oldOutpost.Simulation.Power, new LifeSupportConfiguration(stocks, life.InitialFood, life.Cabin)));
            var expedition = new Trainer.Application.Expedition.ExpeditionScenario(outpost, oldExpedition.Zones, oldExpedition.Airlock,
                oldExpedition.Racks, oldExpedition.Eva, oldExpedition.TaskRules, oldExpedition.Shelter, oldExpedition.Comms,
                oldExpedition.Episode, oldExpedition.Rules, oldExpedition.SuitedSpeedFactor);
            var scenario = new BaseOperationsScenario(expedition, original.Equipment, original.WaterStation, original.AirStation,
                original.Operations, original.ObservationValiditySeconds, original.Logistics, original.Manifests, original.Gauges,
                original.Irreversible, original.ReportIntervalSeconds, original.Science, original.Provenance);
            using (Phase04Session s = Phase04Session.Create(d, scenario, K))
            {
                AuthorityActor a = s.Join(), b = s.Join();
                s.Resume(a);
                bool Low(BaseOperationsSnapshot x, GaugeKind kind) => x.GaugeOf(kind).Band == GaugeBand.Reserve || x.GaugeOf(kind).Band == GaugeBand.Exhausted;
                bool reached = s.AdvanceUntil(x => Low(x, GaugeKind.PotableWater) || Low(x, GaugeKind.StoredFood), 4 * 3600, null, 1.0);
                Require(reached, "scarcity fixture did not reach reserve through consumption in 4 simulated hours");
                BaseOperationsSnapshot low = s.State;
                GaugeKind lowKind = Low(low, GaugeKind.PotableWater) ? GaugeKind.PotableWater : GaugeKind.StoredFood;
                double before = Stock(low, lowKind);
                StationId console = scenario.Logistics.Console;
                s.WalkTo(b, console);
                s.Complete(b, BaseOperationAction.PlaceOrder, console, OperationChoice.None, scenario.Manifests[0].Key);
                long order = s.State.Orders.Last().Sequence;
                Require(s.AdvanceUntil(x => x.TryGetOrder(order, out ResupplyOrder o) && o.State == OrderState.Arrived, scenario.Manifests[0].LeadSeconds + 60),
                    "order never arrived");
                double atArrival = Stock(s.State, lowKind);
                Require(atArrival <= before + Tol, $"{lowKind} rose before unloading ({before} -> {atArrival}): food does not appear by ordering");
                s.Complete(b, BaseOperationAction.UnloadDelivery, console, OperationChoice.None, null, order);
                BaseOperationsSnapshot after = s.State;
                Require(Stock(after, lowKind) > atArrival + Tol, $"{lowKind} did not recover after unloading");
                after.TryGetOrder(order, out ResupplyOrder done);
                Require(done != null && (done.State == OrderState.Unloaded || done.Remaining.Count > 0), "order state " + done?.State);
                RequireNoStreamViolation(s);
                return $"synthetic scarcity fixture starts with {stocks.PotableKilograms:0.##} kg potable; {lowKind} reached {low.GaugeOf(lowKind).Band} at {low.SimulatedTimeSeconds:0} s ({before:0.##}); " +
                       $"after unloading {Stock(after, lowKind):0.##} ({after.GaugeOf(lowKind).Band}); order {done.State}";
            }
        }

        private static string RecyclerRepair(Phase04ImplementationDiscovery d)
        {
            BaseOperationsScenario scenario = BaseOperationsFixtures.Scenario();
            WaterProcessorConfiguration w = scenario.Equipment.WaterProcessor;
            StationId ws = scenario.WaterStation;
            using (Phase04Session s = Phase04Session.Create(d, scenario, K))
            {
                AuthorityActor a = s.Join();
                s.Resume(a);
                s.WalkTo(a, ws);
                Require(s.Do(a, BaseOperationAction.Isolate, ws) != OutpostReasons.Accepted, "Isolate accepted without an observation");
                s.Advance(w.DegradeAtSeconds.Value + 1 - s.State.SimulatedTimeSeconds);
                s.Complete(a, BaseOperationAction.Inspect, ws);
                EquipmentStatus seen = s.State.EquipmentOf(EquipmentKind.WaterProcessor);
                Require(seen.LatestObservation != null && seen.LatestObservation.Current, "no current observation after Inspect");
                Require((seen.LatestObservation.Findings & Findings.ProcessorFilterRestricted) != 0, "fault not found: " + seen.LatestObservation.Findings);
                Require(seen.Knowledge == EquipmentKnowledge.Degraded || seen.Knowledge == EquipmentKnowledge.Failed, "knowledge " + seen.Knowledge);

                EnsureUntreated(s, w.BatchKilograms);
                s.Complete(a, BaseOperationAction.ProcessBatch, ws);
                BaseOperationsSnapshot held = s.State;
                Require(held.Water.HoldKilograms > 0 && !held.Water.ProductWithinLimit, "degraded product should be held and off-spec");
                Near(w.BatchKilograms * w.DegradedRecoveryFraction, held.Water.HoldKilograms, 1e-6, "degraded hold");
                double potableBeforeReprocess = Potable(held);
                s.Complete(a, BaseOperationAction.RouteProduct, ws, OperationChoice.Reprocess);
                DecisionRecord reprocess = s.State.Decisions.LastOrDefault();
                Require(reprocess != null && reprocess.Choice == OperationChoice.Reprocess && reprocess.Assessment == DecisionAssessment.Appropriate,
                    "reprocessing off-spec product not recorded as appropriate");
                Require(Potable(s.State) <= potableBeforeReprocess + Tol, "reprocessing added potable water");
                double lost = w.BatchKilograms * (1 - w.DegradedRecoveryFraction);

                s.Complete(a, BaseOperationAction.Inspect, ws);
                Require(s.Do(a, BaseOperationAction.Restore, ws) != OutpostReasons.Accepted, "Restore accepted out of order");
                Require(s.Do(a, BaseOperationAction.Isolate, ws) == OutpostReasons.Accepted, "Isolate refused");
                Require(s.State.EquipmentOf(EquipmentKind.WaterProcessor).Operating == EquipmentOperatingState.Isolated, "not isolated");
                EnsureUntreated(s, w.BatchKilograms);
                string isolatedBatch = s.Do(a, BaseOperationAction.ProcessBatch, ws);
                Require(isolatedBatch == BaseOperationReasons.EquipmentIsolated, "batch on an isolated processor: " + isolatedBatch);

                int sparesBefore = SpareCount(s.State, w.FilterSpareKey);
                string intervene = s.Do(a, BaseOperationAction.Intervene, ws, OperationChoice.ReplaceProcessorFilter);
                Require(intervene == OutpostReasons.Accepted, "Intervene refused: " + intervene + " (the observation must stay current after Isolate)");
                Require(s.State.FreeSpares(w.FilterSpareKey) == sparesBefore - 1 && SpareCount(s.State, w.FilterSpareKey) == sparesBefore,
                    "spare not reserved (still counted) while intervening");
                Require(s.AdvanceUntil(x => !x.TryGetOperation(a.Player, out _), 120), "intervention did not finish");
                Require(SpareCount(s.State, w.FilterSpareKey) == sparesBefore - 1, "spare not consumed at completion");
                s.Complete(a, BaseOperationAction.TestEquipment, ws);
                RepairJob job = s.State.EquipmentOf(EquipmentKind.WaterProcessor).Repair;
                Require(job != null && job.LastTestPassed == true, "test did not pass after the replacement");
                Require(s.Do(a, BaseOperationAction.Restore, ws) == OutpostReasons.Accepted, "Restore refused after a passed test");
                EquipmentStatus restored = s.State.EquipmentOf(EquipmentKind.WaterProcessor);
                Require(restored.Operating == EquipmentOperatingState.Online && restored.Repair == null, "not back online");

                EnsureUntreated(s, w.BatchKilograms);
                s.Complete(a, BaseOperationAction.ProcessBatch, ws);
                BaseOperationsSnapshot good = s.State;
                Require(good.Water.ProductWithinLimit, "product still off-spec after the repair");
                Near(w.BatchKilograms * w.NominalRecoveryFraction, good.Water.HoldKilograms, 1e-6, "nominal hold after repair");
                double potable = Potable(good), hold = good.Water.HoldKilograms;
                s.Complete(a, BaseOperationAction.RouteProduct, ws, OperationChoice.AcceptToPotable);
                double gained = Potable(s.State) - potable;
                Require(gained <= hold + Tol, $"potable gained {gained} kg > released {hold} kg (lost water came back)");
                Require(s.State.Activities.Any(x => x.Kind == ActivityKind.Repair && x.Status == ActivityStatus.Completed), "no completed Repair activity");
                if (s.Instance.Equipment != null)
                {
                    EquipmentLedger l = s.Instance.Equipment.Snapshot.Ledger;
                    Near(l.BatchInputKilograms, l.BrineOutflowKilograms + l.ReleasedToPotableKilograms + l.ReleasedForReprocessKilograms
                        + s.Instance.Equipment.Snapshot.WaterProcessor.HoldKilograms, 1e-6, "equipment ledger");
                    Require(l.BrineOutflowKilograms + Tol >= lost, "brine of the degraded batch was refunded");
                }
                RequireNoStreamViolation(s);
                return $"repair completed at {s.State.SimulatedTimeSeconds:0} s; {lost:0.###} kg lost to brine with the degraded batch stays lost";
            }
        }

        private static string Interactions(Phase04ImplementationDiscovery d)
        {
            BaseOperationsScenario scenario = BaseOperationsFixtures.Scenario(null);
            StationId ws = scenario.WaterStation, airs = scenario.AirStation, console = scenario.Logistics.Console;
            using (Phase04Session s = Phase04Session.Create(d, scenario, K))
            {
                AuthorityActor a = s.Join(), b = s.Join();
                s.Resume(a);
                var refusals = new List<string>();
                void Refused(string what, BaseOperationCommand forged, params string[] expected)
                {
                    BaseOperationsSnapshot before = s.State;
                    string reason = a.Send(forged) ?? s.AwaitAnswer(a);
                    Require(reason != null && reason != OutpostReasons.Accepted, what + " accepted");
                    if (expected.Length > 0) Require(expected.Contains(reason), what + ": " + reason + " (expected " + string.Join("|", expected) + ")");
                    SameEffects(before, s.State, what);
                    refusals.Add(what + "=" + reason);
                }
                long epoch = s.State.Epoch;
                Refused("far from the station", BaseOperationCommand.Operation(Guid.NewGuid(), epoch, BaseOperationAction.Inspect, airs), OutpostReasons.OutOfRange);
                s.WalkTo(a, ws);
                epoch = s.State.Epoch;
                Refused("wrong epoch", BaseOperationCommand.Operation(Guid.NewGuid(), epoch + 1, BaseOperationAction.Inspect, ws), OutpostReasons.EpochMismatch);
                Refused("unknown station", BaseOperationCommand.Operation(Guid.NewGuid(), epoch, BaseOperationAction.Inspect, new StationId("no-such-station")),
                    OutpostReasons.UnknownStation);
                Refused("order at the water station", BaseOperationCommand.PlaceOrder(Guid.NewGuid(), epoch, ws, scenario.Manifests[0].Key),
                    BaseOperationReasons.WrongStationForAction);
                Refused("route an empty hold with a forged observation", BaseOperationCommand.Operation(Guid.NewGuid(), epoch,
                    BaseOperationAction.RouteProduct, ws, OperationChoice.AcceptToPotable, 999), BaseOperationReasons.ObservationRequired,
                    BaseOperationReasons.StaleObservation, BaseOperationReasons.HoldEmpty);
                Refused("restore without a repair", BaseOperationCommand.Operation(Guid.NewGuid(), epoch, BaseOperationAction.Restore, ws,
                    OperationChoice.None, 1), BaseOperationReasons.ObservationRequired, BaseOperationReasons.StaleObservation,
                    BaseOperationReasons.EquipmentNotIsolated, BaseOperationReasons.RepairStepOutOfOrder);
                Refused("malformed choice", new BaseOperationCommand(Guid.NewGuid(), epoch, BaseOperationAction.Inspect, ws, OperationChoice.SwitchBed),
                    OutpostReasons.InvalidActionValue);
                Refused("empty id", BaseOperationCommand.Operation(Guid.Empty, epoch, BaseOperationAction.Inspect, ws), OutpostReasons.InvalidCommandId);

                // Water decision: accept vs reprocess have different CONFIRMED effects.
                EnsureUntreated(s, scenario.Equipment.WaterProcessor.BatchKilograms);
                s.Complete(a, BaseOperationAction.ProcessBatch, ws);
                double hold = s.State.Water.HoldKilograms, potable = Potable(s.State), untreated = Untreated(s.State);
                s.Complete(a, BaseOperationAction.RouteProduct, ws, OperationChoice.Reprocess);
                Require(Untreated(s.State) > untreated && Potable(s.State) <= potable + Tol, "reprocess did not return product to untreated");
                EnsureUntreated(s, scenario.Equipment.WaterProcessor.BatchKilograms);
                s.Complete(a, BaseOperationAction.ProcessBatch, ws);
                potable = Potable(s.State);
                hold = s.State.Water.HoldKilograms;
                s.Complete(a, BaseOperationAction.RouteProduct, ws, OperationChoice.AcceptToPotable);
                Near(potable + hold, Potable(s.State), 1e-3, "accepted product reaches potable (minus consumption during the step)");

                // Air decision: switch vs regenerate.
                s.WalkTo(b, airs);
                s.Advance(120);
                s.Complete(b, BaseOperationAction.Inspect, airs);
                AirRevitalizationState airBefore = AirState(s);
                s.Complete(b, BaseOperationAction.ServiceAir, airs, OperationChoice.SwitchBed);
                AirRevitalizationState switched = AirState(s);
                if (airBefore != null && switched != null)
                    Require(switched.ActiveBed != airBefore.ActiveBed, "switch did not change the active bed");
                s.Complete(b, BaseOperationAction.Inspect, airs);
                s.Complete(b, BaseOperationAction.ServiceAir, airs, OperationChoice.RegenerateStandbyBed);
                AirRevitalizationState regenerated = AirState(s);
                if (regenerated != null) Near(0, regenerated.StandbyLoadingFraction, Tol, "standby after regeneration");
                Require(s.State.Decisions.Count(x => x.Equipment == EquipmentKind.AirRevitalization) >= 2, "air decisions not recorded");
                Require(s.State.Decisions.All(x => !string.IsNullOrEmpty(x.ExplanationKey)), "decision without explanation key");
                RequireNoStreamViolation(s);
                return string.Join("; ", refusals) + (s.Instance.Equipment == null ? " (air state from snapshots only)" : "");
            }
        }

        private static string Activities(Phase04ImplementationDiscovery d)
        {
            BaseOperationsScenario scenario = BaseOperationsFixtures.Scenario(null);
            StationId ws = scenario.WaterStation, console = scenario.Logistics.Console;
            using (Phase04Session s = Phase04Session.Create(d, scenario, K))
            {
                AuthorityActor a = s.Join(), b = s.Join();
                s.Resume(a);
                long epoch = s.State.Epoch;
                s.WalkTo(a, ws);
                for (int i = 0; i < 3; i++)
                {
                    EnsureUntreated(s, scenario.Equipment.WaterProcessor.BatchKilograms);
                    s.Complete(a, BaseOperationAction.ProcessBatch, ws);
                    s.Complete(a, BaseOperationAction.RouteProduct, ws,
                        s.State.Water.ProductWithinLimit ? OperationChoice.AcceptToPotable : OperationChoice.Reprocess);
                    Require(s.State.Outpost.State == SessionState.Running, "session left Running after batch " + (i + 1));
                }
                s.WalkTo(b, console);
                s.Complete(b, BaseOperationAction.PlaceOrder, console, OperationChoice.None, scenario.Manifests[0].Key);
                long first = s.State.Orders.Last().Sequence;
                s.Complete(b, BaseOperationAction.CancelOrder, console, OperationChoice.None, null, first);
                Require(s.State.TryGetOrder(first, out ResupplyOrder cancelled) && cancelled.State == OrderState.Cancelled, "order not cancelled");
                s.Advance(scenario.Logistics.MinSecondsBetweenOrders + 1);
                s.Complete(b, BaseOperationAction.PlaceOrder, console, OperationChoice.None, scenario.Manifests[0].Key);
                long second = s.State.Orders.Last().Sequence;
                Require(s.AdvanceUntil(x => x.TryGetOrder(second, out ResupplyOrder o) && o.State == OrderState.Arrived, scenario.Manifests[0].LeadSeconds + 60),
                    "order never arrived");
                s.Complete(b, BaseOperationAction.UnloadDelivery, console, OperationChoice.None, null, second);
                BaseOperationsSnapshot end = s.State;
                Require(end.Epoch == epoch && end.Outpost.State == SessionState.Running, "session closed or reset");
                Require(end.Activities.Count(x => x.Status == ActivityStatus.Completed) >= 3, "completed activities: " + end.Activities.Count(x => x.Status == ActivityStatus.Completed));
                Require(end.Activities.Any(x => x.Status == ActivityStatus.Cancelled && x.Kind == ActivityKind.Resupply), "no cancelled Resupply activity");
                Require(end.Session.ActivitySequence >= end.Activities.Count, "activity sequence");
                RequireNoStreamViolation(s);
                return $"{end.Session.ActivitySequence} activities by t={end.SimulatedTimeSeconds:0} s in epoch {end.Epoch}; still {end.Outpost.State}";
            }
        }

        private static string Competition(Phase04ImplementationDiscovery d)
        {
            BaseOperationsScenario scenario = BaseOperationsFixtures.Scenario();
            WaterProcessorConfiguration w = scenario.Equipment.WaterProcessor;
            StationId ws = scenario.WaterStation;
            using (Phase04Session s = Phase04Session.Create(d, scenario, K))
            {
                AuthorityActor a = s.Join(), b = s.Join();
                s.Resume(a);
                s.WalkTo(a, ws);
                s.WalkTo(b, ws);
                long epoch = s.State.Epoch;
                var inspect = BaseOperationCommand.Operation(Guid.NewGuid(), epoch, BaseOperationAction.Inspect, ws, OperationChoice.None,
                    BaseOperationCommand.Unstated, a.ZoneSequence(s.State));
                Require((a.Send(inspect) ?? s.AwaitAnswer(a)) == OutpostReasons.Accepted, "first Inspect refused");
                string second = s.Do(b, BaseOperationAction.Inspect, ws);
                Require(second == OutpostReasons.StationOccupied, "second player at an occupied station: " + second);
                int effects = s.Instance.Equipment?.Effects.Count ?? -1;
                string replay = a.Send(inspect) ?? s.AwaitAnswer(a);
                Require(replay == OutpostReasons.Accepted, "same id and content not replayed: " + replay);
                var conflict = BaseOperationCommand.Operation(inspect.Id, epoch, BaseOperationAction.ProcessBatch, ws);
                string conflicting = a.Send(conflict) ?? s.AwaitAnswer(a);
                Require(conflicting == OutpostReasons.CommandIdConflict, "same id, other content: " + conflicting);
                int originalAnswers = a.Results.Count(r => r.CommandId == inspect.Id);
                string stolen = b.Send(inspect) ?? s.AwaitAnswer(b);
                // Contract: an id reused by ANOTHER sender is refused (CommandIdConflict); never Accepted, never answered to
                // the original sender, never a second effect.
                Require(stolen == OutpostReasons.CommandIdConflict, "another sender reusing the id must get CommandIdConflict: " + stolen);
                Require(a.Results.Count(r => r.CommandId == inspect.Id) == originalAnswers, "the original sender got an answer for another sender's reuse");
                Require(s.State.Operations.Count(o => o.Station == ws) == 1, "two operations at one station");
                if (effects >= 0) Require(s.Instance.Equipment.Effects.Count == effects, "a replay applied an equipment effect");
                Require(s.AdvanceUntil(x => !x.TryGetOperation(a.Player, out _), 60), "inspection did not end");

                // Spare: a reservation is exclusive and a cancelled intervention releases it without consuming the spare.
                s.Advance(Math.Max(0, w.DegradeAtSeconds.Value + 1 - s.State.SimulatedTimeSeconds));
                s.Complete(a, BaseOperationAction.Inspect, ws);
                Require(s.Do(a, BaseOperationAction.Isolate, ws) == OutpostReasons.Accepted, "Isolate refused");
                int count = SpareCount(s.State, w.FilterSpareKey);
                Require(s.Do(a, BaseOperationAction.Intervene, ws, OperationChoice.ReplaceProcessorFilter) == OutpostReasons.Accepted, "Intervene refused");
                Require(s.State.FreeSpares(w.FilterSpareKey) == count - 1, "spare not reserved");
                string busy = s.Do(a, BaseOperationAction.Inspect, scenario.AirStation);
                Require(busy != OutpostReasons.Accepted, "a busy player started a second operation");
                string cancel = s.Do(a, BaseOperationAction.CancelOperation, ws);
                Require(cancel == OutpostReasons.Accepted, "cancel refused: " + cancel);
                Require(s.State.FreeSpares(w.FilterSpareKey) == count && SpareCount(s.State, w.FilterSpareKey) == count, "cancel did not release the spare (or consumed it)");
                RequireNoStreamViolation(s);
                return $"occupied={second}; replay={replay}; conflict={conflicting}; other sender={stolen}; busy={busy}";
            }
        }

        private static string PauseLeaveJoinRestart(Phase04ImplementationDiscovery d)
        {
            BaseOperationsScenario scenario = BaseOperationsFixtures.Scenario();
            WaterProcessorConfiguration w = scenario.Equipment.WaterProcessor;
            StationId ws = scenario.WaterStation;
            using (Phase04Session s = Phase04Session.Create(d, scenario, K))
            {
                AuthorityActor a = s.Join();
                s.Resume(a);
                s.Advance(30);
                s.Pause(a);
                BaseOperationsSnapshot paused = s.State;
                s.Advance(60);
                Near(paused.SimulatedTimeSeconds, s.State.SimulatedTimeSeconds, Tol, "clock while paused");
                Near(Potable(paused), Potable(s.State), Tol, "potable while paused");
                Near(paused.Outpost.Resources.Power.EnergyJoules, s.State.Outpost.Resources.Power.EnergyJoules, Tol, "energy while paused");
                s.Resume(a);

                s.WalkTo(a, ws);
                s.Advance(Math.Max(0, w.DegradeAtSeconds.Value + 1 - s.State.SimulatedTimeSeconds));
                s.Complete(a, BaseOperationAction.Inspect, ws);
                Require(s.Do(a, BaseOperationAction.Isolate, ws) == OutpostReasons.Accepted, "Isolate refused");
                int count = SpareCount(s.State, w.FilterSpareKey);
                Require(s.Do(a, BaseOperationAction.Intervene, ws, OperationChoice.ReplaceProcessorFilter) == OutpostReasons.Accepted, "Intervene refused");
                s.Advance(3);
                double energyBeforeLeave = s.State.Outpost.Resources.Power.EnergyJoules;
                Require(a.Leave(), "Leave returned false");
                BaseOperationsSnapshot left = s.State;
                Require(left.Operations.All(o => o.Performer != a.Player), "operation of the leaver kept");
                Require(left.FreeSpares(w.FilterSpareKey) == count && SpareCount(left, w.FilterSpareKey) == count, "reservation not released on leave");
                Require(left.Outpost.Resources.Power.EnergyJoules <= energyBeforeLeave + Tol, "energy refunded on leave");
                Require(left.Outpost.State == SessionState.Running, "the base stopped when a player left");
                Require(left.EquipmentOf(EquipmentKind.WaterProcessor).Repair != null, "the repair job disappeared with the leaver");

                s.Advance(20);
                BaseOperationsSnapshot beforeJoin = s.State;
                AuthorityActor late = s.Join();
                BaseOperationsSnapshot joined = s.State;
                Require(joined.Epoch == beforeJoin.Epoch && joined.SimulatedTimeSeconds >= beforeJoin.SimulatedTimeSeconds, "late join reset the base");
                Require(joined.Water.BatchesProcessed == beforeJoin.Water.BatchesProcessed && SpareCount(joined, w.FilterSpareKey) == count
                        && joined.EquipmentOf(EquipmentKind.WaterProcessor).Repair != null, "late joiner does not see the current state");
                s.WalkTo(late, ws);
                string intervene = s.Do(late, BaseOperationAction.Intervene, ws, OperationChoice.ReplaceProcessorFilter);
                Require(intervene == OutpostReasons.Accepted, "the late joiner cannot continue the repair: " + intervene);
                Require(s.AdvanceUntil(x => !x.TryGetOperation(late.Player, out _), 120), "intervention did not finish");
                s.Complete(late, BaseOperationAction.TestEquipment, ws);
                Require(s.Do(late, BaseOperationAction.Restore, ws) == OutpostReasons.Accepted, "Restore refused for the late joiner");

                long epoch = s.State.Epoch;
                s.Restart(late);
                BaseOperationsSnapshot restarted = s.State;
                Require(restarted.Epoch == epoch + 1, "Restart did not raise the epoch");
                RequireNoStreamViolation(s);
                return $"paused 60 s with no change; leave released the spare; late joiner restored the processor; epoch {epoch} -> {restarted.Epoch}";
            }
        }

        private static string SingleConsumption(Phase04ImplementationDiscovery d)
        {
            BaseOperationsScenario scenario = BaseOperationsFixtures.Scenario();
            using (Phase04Session s = Phase04Session.Create(d, scenario, K))
            {
                if (s.Instance.Base == null && s.Instance.PhysicalBase != null && s.Instance.Equipment != null && s.Instance.Eva != null)
                {
                    AuthorityActor first = s.Join(); s.Join(); s.Resume(first);
                    s.Advance(300);
                    var model = s.Instance.PhysicalBase;
                    double time = s.State.SimulatedTimeSeconds;
                    Near(time, model.Snapshot.Power.SimulatedTimeSeconds, 1e-8, "base clock");
                    Near(time, s.Instance.Equipment.Advances.Sum(x => x.Elapsed), 1e-8, "equipment steps sum");
                    Near(time, s.Instance.Eva.Snapshot.SimulatedTimeSeconds, 1e-8, "suit model clock");
                    Require(s.Instance.Equipment.Advances.All(x => x.Crew == 2), "equipment counts exactly two aboard");
                    Near(2 * time * model.Parameters.Crew.PotableWaterIntakeKilogramsPerSecond,
                        model.Ledger.CrewPotableWaterIntakeKilograms, 1e-8, "one water intake per member-second");
                    Near(2 * time * model.Parameters.Crew.OxygenConsumptionKilogramsPerSecond,
                        model.Ledger.CrewOxygenIntakeKilograms, 1e-8, "one oxygen intake per member-second");
                    Near(0, s.Instance.Eva.Ledger.OxygenConsumedKilograms, 1e-10, "unsealed suits do not also consume");
                    Near(model.CarbonDioxideRemovalFactor, s.Instance.Equipment.Snapshot.Air.RemovalFactor, 1e-10, "same air capacity");
                    return $"concrete base ledger audit, not a base-call spy: {time:0.###} s on all three models; exactly 600 cabin member-seconds; no suit debit while unsealed";
                }
                if (s.Instance.Base == null || s.Instance.Equipment == null)
                    throw new Phase04CheckPending("no base spy or supported concrete model ledger: " + s.Instance.Composition);
                AuthorityActor a = s.Join(), b = s.Join();
                s.Resume(a);
                double t0 = s.State.SimulatedTimeSeconds;
                int baseCalls = s.Instance.Base.Advances.Count, eqCalls = s.Instance.Equipment.Advances.Count, factors = s.Instance.Base.FactorsSet.Count;
                s.Advance(300);
                double simulated = s.State.SimulatedTimeSeconds - t0;
                double baseSum = s.Instance.Base.Advances.Skip(baseCalls).Sum(x => x.Elapsed);
                double eqSum = s.Instance.Equipment.Advances.Skip(eqCalls).Sum(x => x.Elapsed);
                Near(simulated, baseSum, 1e-6, "base model advanced time vs session clock");
                Near(simulated, eqSum, 1e-6, "equipment advanced time vs session clock");
                int baseSteps = s.Instance.Base.Advances.Count - baseCalls, eqSteps = s.Instance.Equipment.Advances.Count - eqCalls;
                int set = s.Instance.Base.FactorsSet.Count - factors;
                Require(set >= eqSteps, $"air factor set {set} times for {eqSteps} equipment steps");
                var crew = s.Instance.Base.Advances.Skip(baseCalls).Select(x => x.Demand.CrewAboard).Distinct().ToList();
                Require(crew.All(c => c <= 2), "base demand counts more crew than present: " + string.Join(",", crew));
                return $"{simulated:0.###} s simulated = {baseSum:0.###} s base ({baseSteps} steps) = {eqSum:0.###} s equipment ({eqSteps} steps); " +
                       "EVA consumption audit is D1's (one model per service verified by discovery)";
            }
        }

        private static string UnknownReadings(Phase04ImplementationDiscovery d)
        {
            BaseOperationsScenario scenario = BaseOperationsFixtures.Scenario();
            using (Phase04Session s = Phase04Session.Create(d, scenario, K))
            {
                AuthorityActor a = s.Join();
                BaseOperationsSnapshot start = s.State;
                foreach (EquipmentStatus e in start.Equipment)
                    Require(e.Knowledge == EquipmentKnowledge.Unknown || e.LatestObservation != null, e.Equipment + " known as " + e.Knowledge + " without any observation");
                InstrumentReading c = start.Water.ProductConductivity;
                Require(c.Status != ReadingStatus.Valid || start.Water.BatchesProcessed > 0, "product conductivity Valid before any batch");
                Require(!start.Water.ProductWithinLimit || start.Water.BatchesProcessed > 0, "no product yet, but reported within the limit");
                s.Resume(a);
                s.Advance(10);
                RequireNoStreamViolation(s);
                return "machines start Unknown; no product reading before a batch; gauges follow GaugeBands (Unavailable → Unknown)";
            }
        }

        /// <summary>The same continuity pilots through D3's local ports and intents (kind PortsInMemory).</summary>
        public static List<Phase04Check> RunThroughLocalPorts(Phase04ImplementationDiscovery d, double seconds = 900)
        {
            return new List<Phase04Check>
            {
                Run("two local ports + intents keep the base for " + (seconds / 60).ToString("0") + " simulated minutes", Phase04Kinds.PortsInMemory, () =>
                {
                    BaseOperationsScenario scenario = BaseOperationsFixtures.Scenario();
                    AuthorityInstance i = d.CreateAuthority(scenario);
                    var clients = new[] { d.CreateLocalClient(i.Authority, scenario), d.CreateLocalClient(i.Authority, scenario) };
                    var stream = new BaseOperationsStreamValidator(scenario, Phase04Kinds.PortsInMemory);
                    clients[0].port.BaseOperationsChanged += stream.Observe;
                    var actors = new List<PortIntentsActor>();
                    try
                    {
                        foreach (var c in clients) Require(c.join().IsJoined, "local Join rejected");
                        clients[0].port.Submit(Trainer.Application.Expedition.ExpeditionCommand.Session(Guid.NewGuid(),
                            Epoch(clients[0].port), Trainer.Application.Expedition.ExpeditionAction.Resume));
                        actors.Add(new PortIntentsActor(clients[0].port, clients[0].intents));
                        actors.Add(new PortIntentsActor(clients[1].port, clients[1].intents));
                        var water = new BaseOperationsContinuityScript(actors[0], scenario, ContinuityRole.Water, seconds);
                        var air = new BaseOperationsContinuityScript(actors[1], scenario, ContinuityRole.AirAndLogistics, seconds);
                        for (double t = 0; t < seconds + 5; t += Phase04Session.Frame)
                        {
                            water.Update(Phase04Session.Frame);
                            air.Update(Phase04Session.Frame);
                            i.Advance(Phase04Session.Frame);
                        }
                        Require(water.Passed, "water pilot: " + water.Verdict);
                        Require(air.Passed, "air/logistics pilot: " + air.Verdict);
                        Require(!stream.HasViolations, string.Join("; ", stream.Results("").Where(r => r.Status == Phase04CheckStatus.Failed)));
                        return "in memory, no sockets; water [" + water.Metrics.Summary() + "]; air [" + air.Metrics.Summary() + "]";
                    }
                    finally
                    {
                        clients[0].port.BaseOperationsChanged -= stream.Observe;
                        foreach (PortIntentsActor actor in actors) actor.Dispose();
                        foreach (var c in clients)
                        {
                            (c.intents as IDisposable)?.Dispose();
                            (c.port as IDisposable)?.Dispose();
                        }
                        (i.Authority as IDisposable)?.Dispose();
                    }
                })
            };
        }

        // ---- helpers -----------------------------------------------------------------------------------------------

        private static long Epoch(IBaseOperationsSessionPort port) => port.TryGetBaseOperations(out BaseOperationsSnapshot s) ? s.Epoch : 0;

        private static void RequireNoStreamViolation(Phase04Session s)
        {
            List<Phase04Check> failed = s.Stream.Results("").Where(r => r.Status == Phase04CheckStatus.Failed).ToList();
            Require(failed.Count == 0, "continuity stream: " + string.Join(" | ", failed.Select(f => f.Name + ": " + f.Detail)));
        }

        /// <summary>Spy-level balance audit of the composed flow (water and spares); "snapshots only" when no spies.</summary>
        private static string Balances(Phase04Session s)
        {
            RecordingBaseSimulation b = s.Instance.Base;
            RecordingEquipmentModel e = s.Instance.Equipment;
            if (b == null && s.Instance.PhysicalBase != null && e != null)
            {
                var model = s.Instance.PhysicalBase;
                var life = s.Scenario.Expedition.Outpost.Simulation.LifeSupport;
                double initial = life.InitialWater.PotableKilograms + life.InitialWater.UntreatedKilograms + life.InitialFood.StoredKilograms;
                double delivered = s.State.Orders.Where(o => o.State == OrderState.Unloaded)
                    .Sum(o => s.Scenario.Manifests.Single(m => m.Key == o.ManifestKey).Items
                        .Where(item => item.Resource == ResourceKind.PotableWater || item.Resource == ResourceKind.StoredFood).Sum(item => item.Kilograms));
                Require(s.Instance.Eva == null || s.Instance.Eva.Ledger.FeedwaterFilledKilograms == 0, "interior continuity run unexpectedly filled a suit");
                Near(0, BaseOperationsBalances.WaterAndFoodResidualKilograms(initial, model.Snapshot.LifeSupport,
                    model.Ledger, e.Snapshot, delivered, 0), 1e-7, "base plus processor mass ledger, including delivery and brine");
                Near(0, BaseOperationsBalances.ProcessorResidualKilograms(e.Snapshot), 1e-8, "processor mass ledger");
                Near(e.Snapshot.Ledger.BatchInputKilograms - e.Snapshot.Ledger.ReleasedToPotableKilograms
                    - e.Snapshot.Ledger.ReleasedForReprocessKilograms - delivered, model.Ledger.TransferBoundaryOutflowKilograms,
                    1e-8, "paired base/equipment flows and declared delivery");
                return $"concrete base + processor ledger closed; delivery {delivered:0.###} kg, brine {e.Snapshot.Ledger.BrineOutflowKilograms:0.###} kg; no base-call spy";
            }
            if (b == null || e == null) throw new Phase04CheckPending("no observable base/equipment balance ledger");
            var applied = b.Applies.Where(x => x.Status == ResourceTransferStatus.Applied).ToList();
            var duplicated = applied.GroupBy(x => x.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Require(duplicated.Count == 0, "a base transfer id applied twice: " + string.Join(",", duplicated));
            foreach (RecordingBaseSimulation.AdvanceRecord adv in b.Advances)
            {
                Require(adv.After.Potable <= adv.Before.Potable + Tol, $"potable rose inside a base step at t={adv.Before.Time:0.###}");
                Require(adv.After.Food <= adv.Before.Food + Tol, $"food rose inside a base step at t={adv.Before.Time:0.###}");
            }
            int pairs = 0;
            foreach (RecordingEquipmentModel.EffectRecord fx in e.Effects.Where(x => x.Status == EquipmentEffectStatus.Applied))
            {
                RecordingBaseSimulation.ApplyRecord match = applied.FirstOrDefault(x => x.Id == fx.Effect.Id);
                switch (fx.Effect.Kind)
                {
                    case EquipmentEffectKind.ReceiveBatch:
                        Require(match != null, "batch " + fx.Effect.Id + " without its base transfer (same id)");
                        Near(-fx.Effect.Kilograms, match.Effect.DeltaFor(ResourceKind.UntreatedWater), 1e-9, "untreated out for the batch");
                        Near(fx.Effect.Kilograms, match.Effect.BoundaryOutflowKilograms, 1e-9, "batch leaves the base boundary");
                        pairs++;
                        break;
                    case EquipmentEffectKind.ReleaseToPotable:
                        Require(match != null, "release " + fx.Effect.Id + " without its base transfer");
                        Near(fx.Effect.Kilograms, match.Effect.DeltaFor(ResourceKind.PotableWater), 1e-9, "potable in for the release");
                        pairs++;
                        break;
                    case EquipmentEffectKind.ReleaseForReprocess:
                        Require(match != null, "reprocess " + fx.Effect.Id + " without its base transfer");
                        Near(fx.Effect.Kilograms, match.Effect.DeltaFor(ResourceKind.UntreatedWater), 1e-9, "untreated in for the reprocess");
                        pairs++;
                        break;
                }
            }
            foreach (RecordingBaseSimulation.ApplyRecord x in applied.Where(x => x.Effect.DeltaFor(ResourceKind.PotableWater) > 0))
                Require(e.Effects.Any(fx => fx.Effect.Id == x.Id) || x.Effect.BoundaryOutflowKilograms < 0,
                    "potable added without a release or a boundary inflow (" + x.Id + ")");
            EquipmentLedger l = e.Snapshot.Ledger;
            Near(l.BatchInputKilograms, l.BrineOutflowKilograms + l.ReleasedToPotableKilograms + l.ReleasedForReprocessKilograms
                + e.Snapshot.WaterProcessor.HoldKilograms, 1e-6, "equipment ledger");
            return $"balances: {applied.Count} base transfers applied once each, {pairs} paired with equipment effects, " +
                   $"brine {l.BrineOutflowKilograms:0.###} kg left the boundary, ledger closed";
        }

        private static void SameEffects(BaseOperationsSnapshot a, BaseOperationsSnapshot b, string what)
        {
            Require(a.Water.HoldKilograms == b.Water.HoldKilograms && a.Water.BatchesProcessed == b.Water.BatchesProcessed, what + ": water changed");
            Require(b.Operations.Count <= a.Operations.Count, what + ": an operation started");
            for (int i = 0; i < a.Equipment.Count; i++)
                Require(a.Equipment[i].Operating == b.Equipment[i].Operating, what + ": equipment state changed");
            foreach (SpareStock spare in a.Spares)
                Require(SpareCount(b, spare.Key) == spare.Count, what + ": spares changed");
            Require(b.Orders.Count == a.Orders.Count, what + ": an order was placed");
        }

        /// <summary>Waits (bounded) for the crew's return flow to fill the untreated tank enough for one batch.</summary>
        private static void EnsureUntreated(Phase04Session s, double kilograms)
        {
            if (!s.AdvanceUntil(x => Untreated(x) >= kilograms, 1800))
                throw new Phase04CheckPending($"the fixture never has {kilograms} kg of untreated water for a batch (has {Untreated(s.State):0.###})");
        }

        private static AirRevitalizationState AirState(Phase04Session s) => s.Instance.Equipment?.Snapshot.Air;
        private static int SpareCount(BaseOperationsSnapshot s, string key) => s.Spares.FirstOrDefault(x => x.Key == key)?.Count ?? 0;
        private static double Potable(BaseOperationsSnapshot s) => s.Outpost.Resources.LifeSupport.Water.PotableKilograms;
        private static double Untreated(BaseOperationsSnapshot s) => s.Outpost.Resources.LifeSupport.Water.UntreatedKilograms;

        private static double Stock(BaseOperationsSnapshot s, GaugeKind kind) => s.GaugeOf(kind).Reading.Value ?? double.NaN;
    }
}
