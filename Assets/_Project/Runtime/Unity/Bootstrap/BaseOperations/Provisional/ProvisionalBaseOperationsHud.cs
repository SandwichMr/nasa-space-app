using System.Text;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using UnityEngine;

namespace Trainer.Unity.Bootstrap.BaseOperations
{
    /// <summary>
    /// PROVISIONAL view (D2), clearly labelled, until D4's phase 4 panel replaces it under the presentation mount: plain
    /// IMGUI text of the published gauges, machines, water, orders, operations and last decision, and buttons for the
    /// actions of the station the local player stands at. It only reads snapshots and calls <see cref="IBaseOperationsIntents"/>;
    /// it keeps no inventory and decides nothing (the authority answers every request).
    /// </summary>
    public sealed class ProvisionalBaseOperationsHud : MonoBehaviour, IBaseOperationsView
    {
        public const string Label = "VISTA PROVISIONAL D2 (fase 4) — se retira al montar el panel de D4";

        private BaseOperationsViewContext context;
        private Vector2 scroll;

        public bool IsBound => context != null;

        public void Bind(BaseOperationsViewContext viewContext) => context = viewContext;

        public void Unbind() => context = null;

        private void OnGUI()
        {
            if (context == null || !context.Port.TryGetBaseOperations(out BaseOperationsSnapshot s)) return;
            GUILayout.BeginArea(new Rect(Screen.width - 430, 10, 420, Screen.height - 20), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label(Label);
            GUILayout.Label(Summary(s));
            Actions(s);
            if (!string.IsNullOrEmpty(context.Intents.LastReason)) GUILayout.Label("Última respuesta: " + context.Intents.LastReason);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static string Summary(BaseOperationsSnapshot s)
        {
            var b = new StringBuilder();
            b.AppendLine($"t = {s.SimulatedTimeSeconds:0} s · {s.Outpost.State} · salidas terminadas {s.Session.ExpeditionsFinished}");
            if (s.Session.EssentialUnservedSeconds > 0)
                b.AppendLine($"¡Energía esencial sin servir! Quedan {s.Session.SecondsBeforeIrreversible:0} s para recuperarla");
            if (s.Session.EndCause != IrreversibleCause.None) b.AppendLine("Fin irreversible: " + s.Session.EndCause + " (solo Reiniciar)");
            foreach (ResourceGauge g in s.Gauges)
            {
                string value = g.Reading.Value.HasValue ? g.Reading.Value.Value.ToString("0.###") : "desconocido";
                string trend = g.TrendPerSecond.HasValue ? $" ({g.TrendPerSecond.Value:+0.####;-0.####}/s)" : "";
                b.AppendLine($"{g.Kind}: {value} {g.Reading.Unit} [{g.Band}]{trend}");
            }
            foreach (EquipmentStatus e in s.Equipment)
            {
                string obs = e.LatestObservation == null ? "sin observación"
                    : $"obs #{e.LatestObservation.Sequence} {(e.LatestObservation.Current ? "vigente" : "antigua")}: {e.LatestObservation.Findings}";
                string repair = e.Repair == null ? "" : $" · reparación {e.Repair.Completed} test={e.Repair.LastTestPassed}";
                b.AppendLine($"{e.Equipment}: {e.Operating}, se sabe {e.Knowledge}, síntomas {e.VisibleSymptoms} · {obs}{repair}");
            }
            b.AppendLine($"Reciclador: retención {s.Water.HoldKilograms:0.##} kg · potable {s.Water.PotableQuality}" +
                (s.Water.SuspectKilograms > 0 ? $" ({s.Water.SuspectKilograms:0.##} kg dudosos)" : "") + $" · lotes {s.Water.BatchesProcessed}");
            foreach (SpareStock spare in s.Spares) b.AppendLine($"Repuesto {spare.Key}: {spare.Count} (libres {s.FreeSpares(spare.Key)})");
            foreach (ResupplyOrder o in s.Orders) b.AppendLine($"Pedido {o.Sequence} {o.ManifestKey}: {o.State} (llega a {o.ArriveAtSeconds:0} s)");
            foreach (OperationProgress op in s.Operations)
                b.AppendLine($"{op.Station}: {op.Action} {op.Choice} {op.Fraction:P0}{(op.Stalled ? " (sin energía)" : "")} por {op.Performer}");
            if (s.Decisions.Count > 0)
            {
                DecisionRecord d = s.Decisions[s.Decisions.Count - 1];
                b.AppendLine($"Última decisión: {d.Action} {d.Choice} → {d.Assessment} ({d.ExplanationKey})");
            }
            if (s.LatestReport != null)
                b.AppendLine($"Informe {s.LatestReport.Sequence}: potable {s.LatestReport.PotableDeltaKilograms:+0.##;-0.##} kg, " +
                    $"comida {s.LatestReport.FoodDeltaKilograms:+0.##;-0.##} kg, completadas {s.LatestReport.ActivitiesCompleted}");
            return b.ToString();
        }

        private void Actions(BaseOperationsSnapshot s)
        {
            IBaseOperationsIntents intents = context.Intents;
            BaseOperationsScenario scenario = context.Scenario;
            if (!context.Port.Membership.IsJoined || !s.Outpost.TryGetCrew(context.Port.Membership.Player, out CrewMemberState me)) return;
            GUI.enabled = !intents.HasPending;
            foreach (StationPlacement station in scenario.Layout.Stations)
            {
                if (!station.IsWithinReach(me.Pose.Position)) continue;
                StationId id = station.Id;
                if (s.TryGetOperation(context.Port.Membership.Player, out OperationProgress mine) && mine.Station == id)
                {
                    if (GUILayout.Button("Cancelar operación")) intents.RequestCancelOperation(id);
                    continue;
                }
                if (id == scenario.WaterStation)
                {
                    Button("Inspeccionar reciclador", () => intents.RequestOperation(id, BaseOperationAction.Inspect));
                    Button("Procesar lote", () => intents.RequestOperation(id, BaseOperationAction.ProcessBatch));
                    Button("Producto → potable", () => intents.RequestOperation(id, BaseOperationAction.RouteProduct, OperationChoice.AcceptToPotable));
                    Button("Producto → reprocesar", () => intents.RequestOperation(id, BaseOperationAction.RouteProduct, OperationChoice.Reprocess));
                    Button("Recomprobar potable", () => intents.RequestOperation(id, BaseOperationAction.RecheckPotable));
                    Repair(id, OperationChoice.ReplaceProcessorFilter);
                }
                else if (id == scenario.AirStation)
                {
                    Button("Inspeccionar aire", () => intents.RequestOperation(id, BaseOperationAction.Inspect));
                    Button("Cambiar de lecho", () => intents.RequestOperation(id, BaseOperationAction.ServiceAir, OperationChoice.SwitchBed));
                    Button("Regenerar lecho en reserva", () => intents.RequestOperation(id, BaseOperationAction.ServiceAir, OperationChoice.RegenerateStandbyBed));
                    Repair(id, OperationChoice.ReplaceInletFilter);
                }
                else if (id == scenario.Logistics.Console)
                {
                    foreach (ResupplyManifest m in scenario.Manifests) Button("Pedir " + m.Key, () => intents.RequestPlaceOrder(id, m.Key));
                    foreach (ResupplyOrder o in s.Orders)
                    {
                        if (o.State == OrderState.Pending) Button($"Cancelar pedido {o.Sequence}", () => intents.RequestCancelOrder(id, o.Sequence));
                        if (o.State == OrderState.Arrived) Button($"Descargar pedido {o.Sequence}", () => intents.RequestUnload(id, o.Sequence));
                    }
                }
            }
            GUI.enabled = true;
        }

        private void Repair(StationId id, OperationChoice intervention)
        {
            IBaseOperationsIntents intents = context.Intents;
            Button("Aislar", () => intents.RequestOperation(id, BaseOperationAction.Isolate));
            Button("Intervenir (repuesto)", () => intents.RequestOperation(id, BaseOperationAction.Intervene, intervention));
            Button("Probar", () => intents.RequestOperation(id, BaseOperationAction.TestEquipment));
            Button("Restituir", () => intents.RequestOperation(id, BaseOperationAction.Restore));
        }

        private static void Button(string text, System.Func<bool> request)
        {
            if (GUILayout.Button(text)) request();
        }
    }
}
