using System;
using System.Text;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;
using Trainer.Unity.Presentation.Expedition;
using UnityEngine;
using SessionState = Trainer.Application.SessionState;

namespace Trainer.Unity.Presentation.BaseOperations
{
    /// <summary>Compact HUD and proximity-only station panels. The injected ports own all pending requests and state.</summary>
    public sealed class BaseOperationsPanel : BaseOperationsBoundView
    {
        [SerializeField] private Texture2D battery, water, food, oxygen, carbonDioxide;
        private static readonly GaugeKind[] Essential = { GaugeKind.BatteryEnergy, GaugeKind.PotableWater, GaugeKind.StoredFood, GaugeKind.CabinOxygen, GaugeKind.CabinCarbonDioxide };
        private static readonly Color Background = new Color(.035f, .065f, .10f, 1);
        private static readonly Color Card = new Color(.07f, .12f, .17f, 1);
        private static readonly Color Cyan = new Color(.40f, .86f, .94f, 1);
        private static readonly Color Amber = new Color(1, .76f, .35f, 1);
        private static readonly Color Muted = new Color(.66f, .77f, .82f, 1);
        private GUIStyle text, small, heading, value, button;
        private Vector2 scroll;
        private float stationContentHeight = 1500;
        private StationId opened;
        private bool details;
        private string answer;
        public StationId OpenStation => opened;
        public bool DetailsOpen => details;
        public string Feedback => !Live ? "Sin conexión: sin datos vigentes."
            : Pending ? "SOLICITUD ENVIADA · esperando respuesta; el efecto aún no está confirmado."
            : BaseOperationsText.Reason(answer);
        public bool Pending => Live && (Context.Intents.HasPending || Context.ExpeditionIntents.HasPending);

        protected override void OnBound()
        {
            Context.Intents.Answered += Answered;
            Context.ExpeditionIntents.Answered += Answered;
        }
        protected override void OnUnbinding()
        {
            Context.Intents.Answered -= Answered;
            Context.ExpeditionIntents.Answered -= Answered;
        }
        private void Answered(string reason) { if (Live) answer = reason; }
        protected override void OnRestart() => ClearView();
        protected override void ClearView() { opened = StationId.None; details = false; scroll = Vector2.zero; answer = null; GUIUtility.keyboardControl = 0; }
        protected override void Present(BaseOperationsSnapshot snapshot)
        {
            if (opened.IsValid && !IsNearby(opened)) ClosePanel();
        }
        private void OnApplicationFocus(bool focused) { if (!focused) ClosePanel(); }

        public bool IsNearby(StationId station)
        {
            return Live && Context.Scenario.Layout.TryGetStation(station, out var placement)
                && Shown.Outpost.TryGetCrew(Context.Port.Membership.Player, out var crew)
                && placement.IsWithinReach(crew.Pose.Position);
        }
        public StationId NearbyStation()
        {
            if (!Live) return StationId.None;
            Shown.Outpost.TryGetCrew(Context.Port.Membership.Player, out var crew);
            if (crew == null) return StationId.None;
            StationId found = StationId.None;
            double distance = double.MaxValue;
            foreach (var station in Context.Scenario.Layout.Stations)
                if ((Context.Scenario.EquipmentAt(station.Id).HasValue || station.Id == Context.Scenario.Logistics.Console)
                    && station.IsWithinReach(crew.Pose.Position) && station.Position.DistanceTo(crew.Pose.Position) < distance)
                { found = station.Id; distance = station.Position.DistanceTo(crew.Pose.Position); }
            return found;
        }
        public bool OpenNearbyStation()
        {
            StationId station = NearbyStation();
            if (!station.IsValid) return false;
            ClosePanel(); opened = station; Context.BlockMovement(this); return true;
        }
        public void ToggleDetails()
        {
            if (!Live) return;
            bool next = !details; ClosePanel(); details = next;
            if (details) Context.BlockMovement(this);
        }
        public void ClosePanel()
        {
            Context?.UnblockMovement(this);
            opened = StationId.None; details = false; scroll = Vector2.zero;
            GUIUtility.keyboardControl = 0;
        }
        public bool RequestOperation(BaseOperationAction action, OperationChoice choice = OperationChoice.None)
        {
            if (!Live || !opened.IsValid || !IsNearby(opened) || Pending) return false;
            answer = null;
            bool sent = action == BaseOperationAction.CancelOperation ? Context.Intents.RequestCancelOperation(opened)
                : Context.Intents.RequestOperation(opened, action, choice);
            if (!sent && answer == null) answer = Context.Intents.LastReason ?? "RequestNotSent";
            return sent;
        }

        private void OnGUI()
        {
            Styles();
            Matrix4x4 previousMatrix = GUI.matrix;
            bool previousEnabled = GUI.enabled;
            Color previousColor = GUI.color;
            int previousDepth = GUI.depth;
            float scale = Mathf.Min(Screen.width / 1440f, Screen.height / 900f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1440 * scale) / 2, (Screen.height - 900 * scale) / 2, 0), Quaternion.identity, Vector3.one * scale);
            GUI.depth = -10;
            try
            {
                if ((opened.IsValid || details) && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
                { ClosePanel(); Event.current.Use(); }
                Paint(new Rect(0, 0, 1440, 193), Background);
                Paint(new Rect(0, 800, 1440, 100), Background);
                Label(new Rect(22, 12, 900, 24), "BASE VIVA  /  " + (Live ? State(Shown.Outpost.State) + "  ·  " + BaseOperationsText.Number(Shown.SimulatedTimeSeconds) + " s" : "SIN CONEXIÓN"), heading);
                GUI.enabled = Live;
                if (GUI.Button(new Rect(1236, 10, 182, 32), details ? "Cerrar detalles" : "Recursos y actividad", button)) ToggleDetails();
                GUI.enabled = previousEnabled;
                for (int i = 0; i < Essential.Length; i++) Gauge(new Rect(22 + i * 282, 48, 268, 119), Essential[i], Icon(i));
                if (!Live)
                {
                    Label(new Rect(24, 170, 1390, 24), "? SIN DATOS VIGENTES · Conecta para recibir el estado. No se muestran reservas ni acciones confirmadas.", small);
                    Label(new Rect(24, 818, 1390, 50), "Sin conexión. El último estado no confirma el resultado de solicitudes pendientes.", text);
                    return;
                }
                var p = Shown.Outpost.Resources.Power;
                Label(new Rect(24, 170, 1390, 22), $"ENERGÍA  ·  Generación {p.GenerationWatts:0.##} W   /   Demanda {p.EssentialRequestedWatts + p.ScienceRequestedWatts + Shown.Outpost.Resources.ServiceLoadRequestedWatts:0.##} W   ·   Balance {BaseOperationsText.Reading(Shown.GaugeOf(GaugeKind.PowerBalance).Reading)}   ·   " + BaseOperationsText.Band(Shown.GaugeOf(GaugeKind.PowerBalance)), small);
                Footer();
                if (opened.IsValid || details) Paint(new Rect(944, 193, 496, 607), Background);
                if (opened.IsValid) StationPanel();
                else if (details) DetailsPanel();
                if (Shown.Session.EssentialUnservedSeconds > 0)
                {
                    Paint(new Rect(22, 199, 910, 30), new Color(.29f, .16f, .08f));
                    Label(new Rect(32, 202, 890, 25), $"! Carga esencial sin servicio · ventana del escenario: {Shown.Session.SecondsBeforeIrreversible:0.#} s · reduce cargas", small);
                }
            }
            finally { GUI.matrix = previousMatrix; GUI.enabled = previousEnabled; GUI.color = previousColor; GUI.depth = previousDepth; }
        }

        private Texture2D Icon(int index) => index == 0 ? battery : index == 1 ? water : index == 2 ? food : index == 3 ? oxygen : carbonDioxide;
        private void Gauge(Rect rect, GaugeKind kind, Texture2D icon)
        {
            Paint(rect, Card);
            ResourceGauge gauge = Live ? Shown.GaugeOf(kind) : null;
            Color accent = Accent(gauge);
            Paint(new Rect(rect.x, rect.y, 3, rect.height), accent);
            if (icon != null) GUI.DrawTexture(new Rect(rect.x + 12, rect.y + 12, 25, 25), icon, ScaleMode.ScaleToFit);
            Label(new Rect(rect.x + 46, rect.y + 9, 212, 20), BaseOperationsText.Name(kind), small);
            Label(new Rect(rect.x + 13, rect.y + 34, 250, 28), BaseOperationsText.Value(gauge), value);
            bool valid = gauge != null && gauge.Reading.Status == ReadingStatus.Valid;
            // ONLY a declared capacity can be the denominator. Cabin partial pressures are never percentages.
            if (valid && gauge.Capacity.HasValue && gauge.Capacity.Value > 0)
            {
                Paint(new Rect(rect.x + 14, rect.y + 66, 240, 5), Background);
                Paint(new Rect(rect.x + 14, rect.y + 66, 240 * (float)(gauge.Reading.Value.Value / gauge.Capacity.Value), 5), accent);
            }
            else
            {
                string line = gauge == null ? "Capacidad / rango sin confirmar"
                    : gauge.LimitAt.HasValue ? "Línea del escenario ≤ " + BaseOperationsText.Number(gauge.LimitAt.Value) + " " + BaseOperationsText.Unit(gauge.Reading.Unit)
                    : gauge.ReserveAt.HasValue ? "Reserva del escenario: " + BaseOperationsText.Number(gauge.ReserveAt.Value) + " " + BaseOperationsText.Unit(gauge.Reading.Unit)
                    : "Sin escala declarada";
                Label(new Rect(rect.x + 13, rect.y + 62, 250, 20), line, small);
            }
            Label(new Rect(rect.x + 13, rect.y + 77, 250, 20), BaseOperationsText.Band(gauge), small, accent);
            Label(new Rect(rect.x + 13, rect.y + 96, 250, 20), BaseOperationsText.Trend(gauge), small);
            GUI.Label(rect, new GUIContent("", BaseOperationsText.Action(kind)), GUIStyle.none);
        }

        private void Footer()
        {
            Shown.Expedition.TryGetCrew(Context.Port.Membership.Player, out var crew);
            string info = crew != null && crew.OnSuitSupply ? ExpeditionPanel.Margin(crew.Margin, Shown.SimulatedTimeSeconds)
                : "WASD / flechas · camina y repartid el trabajo. E: interacción existente · R: regreso";
            Label(new Rect(24, 807, 1130, 23), info, small);
            string notice = Feedback;
            if (string.IsNullOrEmpty(notice) && !string.IsNullOrEmpty(GUI.tooltip)) notice = GUI.tooltip;
            if (string.IsNullOrEmpty(notice)) notice = Advisory();
            Label(new Rect(24, 833, 1130, 56), notice, text);
            StationId nearby = NearbyStation();
            GUI.enabled = Live && nearby.IsValid;
            if (GUI.Button(new Rect(1176, 814, 242, 42), opened.IsValid ? "Cerrar estación" : "Abrir estación cercana", button))
            { if (opened.IsValid) ClosePanel(); else OpenNearbyStation(); }
            GUI.enabled = true;
        }
        private string Advisory()
        {
            IssuedAdvisory latest = null;
            AdvisoryReception received = null;
            foreach (var advisory in Shown.Expedition.Advisories)
                if (Shown.Expedition.TryGetReception(advisory.Message.Id, Context.Port.Membership.Player, out var reception)) { latest = advisory; received = reception; }
            if (latest == null) return "Sin aviso recibido · no significa seguro. Mantén las reservas y comprueba los equipos.";
            var message = latest.Message;
            string instruction = message.Kind == AdvisoryKind.InstructionEnd ? "Control: fin explícito del periodo de refugio del ejercicio."
                : "AVISO · Entra al refugio" + (message.ShelterBySeconds.HasValue ? $" antes de t={message.ShelterBySeconds:0.#} s." : ".");
            return instruction + $" Recibido t={received.ReceivedAtSeconds:0.#} s · válido hasta {message.ValidUntilSeconds:0.#} s."
                + (latest.Validity == AdvisoryValidity.Expired && message.Kind != AdvisoryKind.InstructionEnd ? " DATO VENCIDO: mantén la instrucción." : "");
        }
        private void StationPanel()
        {
            const float x = 954, width = 464;
            Paint(new Rect(x, 204, width, 583), Background);
            Paint(new Rect(x, 204, 3, 583), Cyan);
            string title = opened == Context.Scenario.WaterStation ? "RECICLADOR DE AGUA"
                : opened == Context.Scenario.AirStation ? "UNIDAD DE AIRE" : "SUMINISTROS DEL EQUIPO";
            Label(new Rect(x + 18, 218, 376, 26), title, heading);
            if (GUI.Button(new Rect(x + width - 48, 214, 32, 30), "×", button)) { ClosePanel(); return; }
            scroll = GUI.BeginScrollView(new Rect(x + 14, 252, width - 25, 523), scroll, new Rect(0, 0, width - 48, stationContentHeight));
            try
            {
                float y = 2;
                if (Shown.TryGetOperationAt(opened, out var operation))
                {
                    Label(new Rect(4, y, 406, 54), Performer(operation.Performer) + " · " + BaseOperationsText.Operation(operation.Action, operation.Choice)
                        + $"\n{operation.WorkSeconds:0.#} / {operation.DurationSeconds:0.#} s" + (operation.Stalled ? " · ESPERA: SIN POTENCIA" : " · CONFIRMADO"), text);
                    y += 60;
                    Paint(new Rect(4, y, 396, 7), Card); Paint(new Rect(4, y, 396 * (float)operation.Fraction, 7), operation.Stalled ? Amber : Cyan); y += 22;
                    if (operation.Performer == Context.Port.Membership.Player)
                    {
                        bool old = GUI.enabled; GUI.enabled = !Pending;
                        if (GUI.Button(new Rect(4, y, 396, 35), "Cancelar mi operación", button)) RequestOperation(BaseOperationAction.CancelOperation);
                        GUI.enabled = old; y += 44;
                    }
                    else { Label(new Rect(4, y, 396, 40), "OCUPADA · espera o atiende otra estación.", text); y += 48; }
                }
                EquipmentKind? kind = Context.Scenario.EquipmentAt(opened);
                if (kind.HasValue) EquipmentPanel(kind.Value, ref y);
                else LogisticsPanel(ref y);
                if (Shown.Decisions.Count > 0)
                {
                    var decision = Shown.Decisions[Shown.Decisions.Count - 1];
                    if (kind.HasValue && decision.Equipment == kind.Value)
                    {
                        Label(new Rect(4, y + 10, 396, 82), "ÚLTIMA CONSECUENCIA CONFIRMADA\n" + BaseOperationsText.Explanation(decision.ExplanationKey), text);
                        y += 100;
                    }
                }
                Label(new Rect(4, y + 8, 396, 70), Feedback, text, Amber);
                stationContentHeight = y + 90;
            }
            finally { GUI.EndScrollView(); }
        }

        private void EquipmentPanel(EquipmentKind kind, ref float y)
        {
            var equipment = Shown.EquipmentOf(kind);
            Shown.TryGetOperationAt(opened, out var operation);
            Label(new Rect(4, y, 396, 26), BaseOperationsText.Equipment(equipment, operation), heading); y += 30;
            var observation = equipment.LatestObservation;
            bool current = observation != null && observation.Current;
            string observed = observation == null ? "SIN OBSERVACIÓN · inspecciona para decidir."
                : (current ? "OBSERVACIÓN VIGENTE" : "OBSERVACIÓN ANTIGUA · vuelve a inspeccionar")
                    + $" · t={observation.ObservedAtSeconds:0.#} s\n" + BaseOperationsText.FindingsText(observation.Findings);
            Label(new Rect(4, y, 396, 66), observed, text); y += 70;
            if (observation != null)
                foreach (var reading in observation.Readings)
                { Label(new Rect(4, y, 396, 45), BaseOperationsText.ReadingName(reading.Key) + "\n" + BaseOperationsText.Reading(reading.Reading), text); y += 49; }
            bool isolated = equipment.Operating == EquipmentOperatingState.Isolated;
            if (kind == EquipmentKind.WaterProcessor)
            {
                Label(new Rect(4, y, 396, 66), $"Producto en espera: {Shown.Water.HoldKilograms:0.##} kg\nConductividad: " + BaseOperationsText.Reading(Shown.Water.ProductConductivity)
                    + $"\nLínea del escenario: ≤ {Shown.Water.ProductSpecLimitMicrosiemens:0.##} µS/cm", text); y += 72;
                Label(new Rect(4, y, 396, 52), Shown.Water.PotableQuality == PotableQuality.Suspect
                    ? $"! TANQUE POR RECOMPROBAR · {Shown.Water.SuspectKilograms:0.##} kg a devolver a sin tratar."
                    : "La ruta se confirma al terminar: el producto retenido todavía no es agua del tanque.", text); y += 58;
            }
            if (!isolated)
            {
                if (kind == EquipmentKind.WaterProcessor)
                {
                    if (Shown.Water.HoldKilograms > 0)
                    {
                        OperationButton(ref y, BaseOperationAction.RouteProduct, OperationChoice.AcceptToPotable, current, "Comprueba la conductividad: producto → potable");
                        OperationButton(ref y, BaseOperationAction.RouteProduct, OperationChoice.Reprocess, current, "Si está fuera de línea, devuelve a sin tratar");
                    }
                    OperationButton(ref y, BaseOperationAction.Inspect, OperationChoice.None, true, "Lee el equipo antes de elegir");
                    if (Shown.Water.HoldKilograms == 0)
                        OperationButton(ref y, BaseOperationAction.ProcessBatch, OperationChoice.None, equipment.Knowledge != EquipmentKnowledge.Failed,
                            $"Sin tratar → producto · lote {Context.Scenario.Equipment.WaterProcessor.BatchKilograms:0.##} kg");
                    if (Shown.Water.PotableQuality == PotableQuality.Suspect) OperationButton(ref y, BaseOperationAction.RecheckPotable, OperationChoice.None, true, "La masa pendiente vuelve a sin tratar");
                }
                else
                {
                    OperationButton(ref y, BaseOperationAction.Inspect, OperationChoice.None, true, "Lee el equipo antes de elegir");
                    OperationButton(ref y, BaseOperationAction.ServiceAir, OperationChoice.SwitchBed, current, "Compara la carga de ambos lechos");
                    OperationButton(ref y, BaseOperationAction.ServiceAir, OperationChoice.RegenerateStandbyBed, current, "Libera el lecho de reserva con tiempo y energía");
                }
                OperationButton(ref y, BaseOperationAction.Isolate, OperationChoice.None, current, "Con hallazgo de avería · el servicio queda detenido");
            }
            else
            {
                var repair = equipment.Repair;
                Label(new Rect(4, y, 396, 61), "REPARACIÓN · pasos confirmados: " + RepairProgress(repair.Completed)
                    + "\n" + (repair.LastTestPassed.HasValue ? repair.LastTestPassed.Value ? "PRUEBA APROBADA" : "PRUEBA FALLIDA · revisa la intervención" : "Prueba aún pendiente"), text); y += 67;
                OperationChoice choice = kind == EquipmentKind.WaterProcessor ? OperationChoice.ReplaceProcessorFilter : OperationChoice.ReplaceInletFilter;
                string spare = kind == EquipmentKind.WaterProcessor ? Context.Scenario.Equipment.WaterProcessor.FilterSpareKey : Context.Scenario.Equipment.Air.InletFilterSpareKey;
                OperationButton(ref y, BaseOperationAction.Intervene, choice, current && RepairSequence.IsAllowed(repair.Completed, RepairSteps.Intervene, repair.LastTestPassed),
                    "Repuestos libres: " + Shown.FreeSpares(spare) + " · se consumen al completar");
                OperationButton(ref y, BaseOperationAction.TestEquipment, OperationChoice.None, RepairSequence.IsAllowed(repair.Completed, RepairSteps.Test, repair.LastTestPassed), "Confirma el resultado antes de restituir");
                OperationButton(ref y, BaseOperationAction.Restore, OperationChoice.None, current && RepairSequence.IsAllowed(repair.Completed, RepairSteps.Restore, repair.LastTestPassed), "Restituye servicio; no devuelve recursos gastados");
            }
        }

        private void OperationButton(ref float y, BaseOperationAction action, OperationChoice choice, bool available, string explanation)
        {
            var definition = Context.Scenario.DefinitionOf(action, choice);
            if (definition == null) return;
            bool enabled = GUI.enabled;
            bool busy = Shown.TryGetOperation(Context.Port.Membership.Player, out _) || Shown.TryGetOperationAt(opened, out _);
            foreach (var station in Shown.Outpost.Stations) if (station.Occupant == Context.Port.Membership.Player) busy = true;
            bool suited = Shown.Expedition.TryGetCrew(Context.Port.Membership.Player, out var member) && member.OnSuitSupply;
            GUI.enabled = enabled && available && !Pending && !busy && !suited && Shown.Outpost.State == SessionState.Running;
            if (GUI.Button(new Rect(4, y, 396, 34), BaseOperationsText.Operation(action, choice) + $"   ·   {definition.DurationSeconds:0.#} s / {definition.PowerWatts:0.#} W", button)) RequestOperation(action, choice);
            GUI.enabled = enabled;
            Label(new Rect(6, y + 37, 390, 37), explanation, small); y += 79;
        }

        private void LogisticsPanel(ref float y)
        {
            Label(new Rect(4, y, 396, 60), "El pedido llega a la consola y necesita descarga. Solo entonces entra al almacén; el excedente queda pendiente.", text); y += 66;
            foreach (var manifest in Context.Scenario.Manifests)
            {
                Label(new Rect(4, y, 396, 66), Manifest(manifest.Items) + $"\nPlazo {manifest.LeadSeconds:0.#} s · cupo {Context.Scenario.Logistics.MaxOrdersInFlight} · intervalo {Context.Scenario.Logistics.MinSecondsBetweenOrders:0.#} s", text); y += 70;
                bool enabled = GUI.enabled; GUI.enabled = !Pending && Shown.Outpost.State == SessionState.Running;
                if (GUI.Button(new Rect(4, y, 396, 34), "Solicitar entrega · " + manifest.Key, button)) SendLogistics(() => Context.Intents.RequestPlaceOrder(opened, manifest.Key));
                GUI.enabled = enabled; y += 46;
            }
            foreach (var order in Shown.Orders)
            {
                Label(new Rect(4, y, 396, 71), $"PEDIDO {order.Sequence} · " + OrderLabel(order.State) + $"\nLlega t={order.ArriveAtSeconds:0.#} s · ahora {Shown.SimulatedTimeSeconds:0.#} s\n" + Manifest(order.Remaining), text); y += 77;
                if (order.State == OrderState.Arrived || order.State == OrderState.Pending)
                {
                    bool enabled = GUI.enabled; GUI.enabled = !Pending;
                    if (GUI.Button(new Rect(4, y, 396, 34), order.State == OrderState.Arrived ? "Descargar entrega" : $"Cancelar antes de t={order.CommitAtSeconds:0.#} s", button))
                        SendLogistics(() => order.State == OrderState.Arrived ? Context.Intents.RequestUnload(opened, order.Sequence) : Context.Intents.RequestCancelOrder(opened, order.Sequence));
                    GUI.enabled = enabled; y += 44;
                }
            }
        }
        private void SendLogistics(Func<bool> request)
        {
            if (!Live || !IsNearby(opened) || Pending) return;
            answer = null;
            if (!request() && answer == null) answer = Context.Intents.LastReason ?? "RequestNotSent";
        }
        private void DetailsPanel()
        {
            Paint(new Rect(954, 204, 464, 583), Background);
            Label(new Rect(974, 219, 410, 29), "RESERVAS, EQUIPO Y ACTIVIDAD", heading);
            scroll = GUI.BeginScrollView(new Rect(972, 260, 432, 512), scroll, new Rect(0, 0, 409, 1050));
            try
            {
                float y = 0;
                foreach (var gauge in Shown.Gauges)
                {
                    Label(new Rect(2, y, 402, 68), BaseOperationsText.Name(gauge.Kind) + " · " + BaseOperationsText.Value(gauge)
                        + "\n" + BaseOperationsText.Band(gauge) + " · " + BaseOperationsText.Trend(gauge)
                        + "\n" + BaseOperationsText.Action(gauge.Kind), text); y += 76;
                }
                Label(new Rect(2, y, 402, 68), $"Producto en espera: {Shown.Water.HoldKilograms:0.##} kg.\nResiduo: cantidad no publicada por este puerto. La salmuera sale de la frontera y no es reserva potable.", text); y += 76;
                foreach (var spare in Shown.Spares)
                { Label(new Rect(2, y, 402, 24), spare.Key + $": {Shown.FreeSpares(spare.Key)} libres / {spare.Count} presentes", small); y += 28; }
                if (Shown.LatestReport != null)
                {
                    var r = Shown.LatestReport;
                    Label(new Rect(2, y, 402, 72), $"INFORME {r.FromSeconds:0.#}–{r.ToSeconds:0.#} s · la base continúa\n{r.ActivitiesCompleted} actividades completadas · {r.ActivitiesCancelled} canceladas\nAgua {r.PotableDeltaKilograms:+0.##;-0.##;0} kg · comida {r.FoodDeltaKilograms:+0.##;-0.##;0} kg · energía {r.EnergyDeltaJoules:+0.##;-0.##;0} J", text); y += 80;
                }
                Label(new Rect(2, y, 402, 27), "Salidas finalizadas: " + Shown.Session.ExpeditionsFinished + " · la sesión continúa", small);
            }
            finally { GUI.EndScrollView(); }
        }

        private string Performer(PlayerId player) => Shown.Outpost.TryGetCrew(player, out var crew) ? "Astronauta " + (crew.Slot + 1) + (player == Context.Port.Membership.Player ? " · TÚ" : "") : "Compañero";
        private static string OrderLabel(OrderState state) => state == OrderState.Pending ? "PENDIENTE" : state == OrderState.InTransit ? "EN TRÁNSITO" : state == OrderState.Arrived ? "LLEGÓ · SIN DESCARGAR" : state == OrderState.Unloaded ? "DESCARGADO" : "CANCELADO";
        private static string Manifest(System.Collections.Generic.IReadOnlyList<ManifestItem> items)
        {
            var text = new StringBuilder();
            foreach (var item in items) { if (text.Length > 0) text.Append(" · "); text.Append(item.IsSpare ? item.Count + " × " + item.SpareKey : BaseOperationsText.Number(item.Kilograms) + " kg " + (item.Resource == ResourceKind.StoredFood ? "comida" : "agua")); }
            return text.Length == 0 ? "Sin carga pendiente" : text.ToString();
        }
        private static string RepairProgress(RepairSteps steps) => "inspección ✓ → aislamiento ✓ → filtro " + ((steps & RepairSteps.Intervene) != 0 ? "✓" : "pendiente") + " → prueba " + ((steps & RepairSteps.Test) != 0 ? "✓" : "pendiente");
        private static string State(SessionState state) => state == SessionState.Running ? "EN MARCHA" : state == SessionState.Paused ? "EN PAUSA" : state == SessionState.Ready ? "LISTA" : "DETENIDA";
        private static Color Accent(ResourceGauge gauge) => gauge == null || gauge.Reading.Status != ReadingStatus.Valid ? Muted : gauge.Band == GaugeBand.Nominal ? Cyan : Amber;
        private static void Paint(Rect rect, Color color) { Color old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old; }
        private static void Label(Rect rect, string content, GUIStyle style, Color? color = null)
        { Color old = GUI.color; if (color.HasValue) GUI.color = color.Value; GUI.Label(rect, content, style); GUI.color = old; }
        private void Styles()
        {
            if (text != null) return;
            text = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, padding = new RectOffset(0, 0, 0, 0) };
            text.normal.textColor = new Color(.89f, .95f, .96f);
            small = new GUIStyle(text) { fontSize = 12 };
            heading = new GUIStyle(text) { fontSize = 17, fontStyle = FontStyle.Bold };
            value = new GUIStyle(text) { fontSize = 22, fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { fontSize = 14, wordWrap = true };
        }
    }
}
