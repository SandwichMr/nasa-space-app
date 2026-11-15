#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;
using Trainer.Unity.Presentation.BaseOperations;
using UnityEngine;

namespace Trainer.Tests.UI.Phase04
{
    /// <summary>Native PlayMode smoke checks, following MissionHUDTests. Not a NUnit or network suite.</summary>
    public sealed class BaseOperationsPresentationTests : MonoBehaviour
    {
        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            style.normal.textColor = new Color(1, .83f, .5f);
            GUI.depth = -20;
            GUI.Label(new Rect(22, Screen.height * .265f, 920, 25), "D4 · FAKE COMÚN · estados guionizados · sin autoridad, simulación ni red", style);
        }
        public static void Run(Action<bool, string> check)
        {
            var scenario = BaseOperationsFixtures.Scenario();
            var me = new PlayerId(1);
            var other = new PlayerId(2);
            var fake = new FakeBaseOperationsSessionPort();
            var initial = BaseOperationsFixtures.Initial(scenario, 1, me, other);
            var snapshot = BaseOperationsFixtures.Edit(scenario, initial).Expedition(e => e.Session(Trainer.Application.SessionState.Running)
                .Member(me, new BasePosition(10, 1.5))).Build();
            fake.Publish(snapshot);
            var blocks = new HashSet<object>();
            var root = new GameObject("D4 lifecycle checks (temporary)");
            var panel = root.AddComponent<BaseOperationsPanel>();
            using (var intents = new FixtureIntents(fake))
            {
                var context = new BaseOperationsViewContext(fake, scenario, intents, intents, (owner, blocked) => { if (blocked) blocks.Add(owner); else blocks.Remove(owner); });
                try
                {
                    panel.Bind(context);
                    check(!panel.Live && !panel.OpenNearbyStation(), "not joined: no live readings or station action");
                    fake.Join(me);
                    check(panel.Live && panel.Shown == snapshot, "join loads the latest confirmed snapshot");
                    check(panel.OpenNearbyStation() && panel.OpenStation == scenario.WaterStation && blocks.Count == 1, "opening nearby water panel blocks movement once");
                    check(panel.RequestOperation(BaseOperationAction.Inspect), "inspection calls the real intent signature");
                    check(intents.LastCommand.Station == scenario.WaterStation && intents.LastCommand.Action == BaseOperationAction.Inspect, "station intent has the local station and action");
                    check(panel.Pending && panel.Feedback.Contains("esperando"), "pending response is distinct from confirmed effect");
                    check(!panel.RequestOperation(BaseOperationAction.Inspect) && fake.PendingOperations.Count == 1, "pending request is not duplicated");
                    check(panel.Shown == snapshot && snapshot.Operations.Count == 0, "sending a request cannot invent progress or stocks");
                    fake.Accept(intents.LastCommand.Id);
                    check(!panel.Pending && panel.Feedback.Contains("aceptada") && panel.Shown == snapshot, "accepted response alone cannot invent a state change");
                    panel.RequestOperation(BaseOperationAction.ProcessBatch);
                    fake.Reject(intents.LastCommand.Id, BaseOperationReasons.SpareUnavailable);
                    check(panel.Feedback.Contains("repuesto"), "resource rejection has an actionable explanation");
                    panel.ClosePanel();
                    check(blocks.Count == 0 && !panel.OpenStation.IsValid, "close releases movement and panel focus");
                    panel.ToggleDetails(); check(panel.DetailsOpen && blocks.Count == 1, "details share the same movement owner");
                    panel.ToggleDetails(); check(!panel.DetailsOpen && blocks.Count == 0, "closing details releases movement");
                    panel.OpenNearbyStation();
                    snapshot = BaseOperationsFixtures.Edit(scenario, snapshot).Expedition(e => e.Member(me, new BasePosition(8, 4.5))).Build(); fake.Publish(snapshot);
                    check(!panel.OpenStation.IsValid && blocks.Count == 0, "confirmed movement out of reach closes the station");
                    check(!panel.OpenNearbyStation(), "remote station cannot open or send actions");
                    snapshot = BaseOperationsFixtures.Edit(scenario, snapshot).Expedition(e => e.Member(me, new BasePosition(10, 1.5)))
                        .Operation(new OperationProgress(Guid.NewGuid(), scenario.WaterStation, BaseOperationAction.Inspect, OperationChoice.None, other, 2, 4, true, null)).Build();
                    fake.Publish(snapshot); panel.OpenNearbyStation();
                    check(panel.Shown.TryGetOperationAt(scenario.WaterStation, out var occupied) && occupied.Performer == other && occupied.Stalled, "other occupant and stalled progress remain authoritative");
                    panel.ToggleDetails();
                    fake.Disconnect("ConnectionLost");
                    check(!panel.Live && panel.Shown == null && !panel.DetailsOpen && blocks.Count == 0, "disconnect clears state, panel, focus and movement block");
                    fake.Join(me); check(panel.Live, "rejoin receives the current state");
                    panel.OpenNearbyStation(); panel.enabled = false;
                    check(!panel.Live && panel.Shown == null && blocks.Count == 0, "disable detaches binding and unblocks movement");
                    panel.enabled = true; panel.Bind(context); check(panel.Live, "explicit rebind after enable");
                    panel.OpenNearbyStation();
                    var restarted = BaseOperationsFixtures.Edit(scenario, BaseOperationsFixtures.Initial(scenario, 2, me, other))
                        .Expedition(e => e.Member(me, new BasePosition(10, 1.5))).Build();
                    fake.Publish(restarted);
                    check(panel.Shown.Epoch == 2 && !panel.OpenStation.IsValid && blocks.Count == 0, "new epoch closes the old interaction and frees input");
                    var replacement = new FakeBaseOperationsSessionPort(); replacement.Publish(restarted); replacement.Join(me);
                    using (var nextIntents = new FixtureIntents(replacement))
                    {
                        panel.Bind(new BaseOperationsViewContext(replacement, scenario, nextIntents, nextIntents, (owner, blocked) => { if (blocked) blocks.Add(owner); else blocks.Remove(owner); }));
                        fake.Disconnect("ConnectionLost");
                        check(panel.Live && panel.Shown == restarted, "old port cannot clear the new binding");
                        fake.Publish(BaseOperationsFixtures.Edit(scenario, restarted).Build());
                        check(panel.Shown == restarted, "old snapshot listener detached on rebind");
                        intents.Emit("SpareUnavailable");
                        check(!panel.Feedback.Contains("repuesto"), "old intent listener detached on rebind");
                        panel.OpenNearbyStation(); panel.Unbind();
                        check(blocks.Count == 0 && !panel.Live, "explicit unbind releases all view-owned input");
                    }
                    var unknown = ResourceGauge.Of(scenario.GaugeRuleOf(GaugeKind.CabinCarbonDioxide), InstrumentReading.Unavailable(ReadingUnit.Kilopascals), null, null);
                    check(BaseOperationsText.Value(unknown) == "SIN LECTURA" && BaseOperationsText.Band(unknown).Contains("SIN DATOS"), "unknown CO2 is never zero or safe");
                    var stale = ResourceGauge.Of(scenario.GaugeRuleOf(GaugeKind.CabinCarbonDioxide), new InstrumentReading(ReadingStatus.Stale, ReadingUnit.Kilopascals, .4, 1), null, .02);
                    check(BaseOperationsText.Band(stale).Contains("ANTIGUO") && BaseOperationsText.Trend(stale).Contains("sin confirmar"), "stale CO2 does not advertise a live nominal band or trend");
                    check(BaseOperationsText.Value(stale).Contains("kPa") && !BaseOperationsText.Value(stale).Contains("%"), "gas is a partial pressure, never a normalized life percentage");
                    var oldObservation = new EquipmentObservation(1, EquipmentKind.WaterProcessor, me, 1, 61, 1, Findings.None, new ObservedReading[0], false);
                    var oldEquipment = new EquipmentStatus(EquipmentKind.WaterProcessor, scenario.WaterStation, EquipmentOperatingState.Online, EquipmentKnowledge.Nominal, Findings.None, 1, oldObservation, null);
                    check(BaseOperationsText.Equipment(oldEquipment).Contains("ANTIGUA"), "an old observation cannot label equipment as currently nominal");
                    var depleted = ResourceGauge.Of(scenario.GaugeRuleOf(GaugeKind.StoredFood), InstrumentReading.Valid(ReadingUnit.Kilograms, 0, 1), 30, -.01);
                    check(BaseOperationsText.Band(depleted).Contains("AGOTADO") && BaseOperationsText.Action(depleted.Kind).Contains("descarga"), "empty food explains replenishment rather than inventing crops");
                    foreach (var kind in new[] { GaugeKind.BatteryEnergy, GaugeKind.PotableWater, GaugeKind.StoredFood })
                    {
                        double capacity = snapshot.GaugeOf(kind).Capacity.Value;
                        var full = ResourceGauge.Of(scenario.GaugeRuleOf(kind), InstrumentReading.Valid(GaugeBands.UnitOf(kind), capacity, 1), capacity, 0);
                        check(full.Band == GaugeBand.Nominal && BaseOperationsText.Value(full).Contains(" / ") && BaseOperationsText.Trend(full).Contains("ESTABLE"), "full resource keeps declared capacity and authoritative trend: " + kind);
                    }
                    check(BaseOperationsText.Reason("StaleObservation").Contains("inspeccionar"), "stale observation rejection offers reinspection");
                    check(BaseOperationsText.Explanation("accepted-off-spec-product").Contains("sin tratar"), "off-spec decision explains recoverable water consequence");
                }
                finally { panel.Unbind(); UnityEngine.Object.Destroy(root); }
            }
        }

        /// <summary>Scripted test driver only. It stamps and forwards to the COMMON fake; it never decides effects.</summary>
        public sealed class FixtureIntents : IBaseOperationsIntents, IExpeditionIntents, IDisposable
        {
            private readonly FakeBaseOperationsSessionPort fake;
            public BaseOperationCommand LastCommand { get; private set; }
            public bool HasPending => fake.PendingOperations.Count > 0 || fake.PendingExpeditionCommands.Count > 0;
            public string LastReason { get; private set; }
            public event Action<string> Answered;
            public FixtureIntents(FakeBaseOperationsSessionPort fake)
            { this.fake = fake; fake.CommandCompleted += Completed; fake.BaseOperationCommandAbandoned += Abandoned; }
            private void Completed(CommandResult result) => Emit(result.Reason);
            private void Abandoned(BaseOperationCommand command, string reason) => Emit(reason);
            public void Emit(string reason) { LastReason = reason; Answered?.Invoke(reason); }
            private bool Send(BaseOperationCommand command)
            {
                if (HasPending || !fake.Membership.IsJoined) return false;
                LastCommand = command; LastReason = null; fake.Submit(command); return true;
            }
            public bool RequestOperation(StationId station, BaseOperationAction action, OperationChoice choice = OperationChoice.None)
            {
                if (!fake.TryGetBaseOperations(out var snapshot)) return false;
                EquipmentKind? kind = station == snapshot.Equipment[0].Station ? EquipmentKind.WaterProcessor : station == snapshot.Equipment[1].Station ? EquipmentKind.AirRevitalization : (EquipmentKind?)null;
                long observation = BaseOperationCommand.NeedsObservation(action) && kind.HasValue ? snapshot.EquipmentOf(kind.Value).LatestObservation?.Sequence ?? -1 : -1;
                return Send(BaseOperationCommand.Operation(Guid.NewGuid(), snapshot.Epoch, action, station, choice, observation));
            }
            public bool RequestCancelOperation(StationId station) => RequestOperation(station, BaseOperationAction.CancelOperation);
            public bool RequestPlaceOrder(StationId station, string manifest)
            { if (!fake.TryGetBaseOperations(out var s)) return false; return Send(BaseOperationCommand.PlaceOrder(Guid.NewGuid(), s.Epoch, station, manifest)); }
            public bool RequestCancelOrder(StationId station, long sequence) => ForOrder(station, sequence, BaseOperationAction.CancelOrder);
            public bool RequestUnload(StationId station, long sequence) => ForOrder(station, sequence, BaseOperationAction.UnloadDelivery);
            private bool ForOrder(StationId station, long sequence, BaseOperationAction action)
            { if (!fake.TryGetBaseOperations(out var s)) return false; return Send(BaseOperationCommand.ForOrder(Guid.NewGuid(), s.Epoch, action, station, sequence)); }
            public bool Interact() => false;
            public bool RequestStart(StationId station, TaskTypeId task) => false;
            public bool RequestCancel(StationId station) => false;
            public bool RequestCycle(StationId station) => false;
            public bool RequestDeclareReturn() => false;
            public void Dispose() { fake.CommandCompleted -= Completed; fake.BaseOperationCommandAbandoned -= Abandoned; }
        }
    }
}
#endif
