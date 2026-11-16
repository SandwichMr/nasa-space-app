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
        /// <summary>Row of the 1440×900 canvas left free for the scene's session status line.</summary>
        public const float SessionStripY = 64f;
        /// <summary>Header row where the session buttons (start, pause, restart, connect) are drawn, right-aligned.</summary>
        public const float HeaderButtonY = 14f;
        public const int HudDepth = -10, SessionControlsDepth = HudDepth - 1;
        /// <summary>Bottom of the expanded / collapsed top dashboard block and top of the footer: the world viewport lives between them.</summary>
        public const float TopBlockBottom = 264f, CollapsedTopBlockBottom = 100f, FooterTop = 804f;
        private const float GaugeTop = 98f, GaugeHeight = 158f, SideBottom = 798f;
        private bool dataHidden;
        /// <summary>True while the live resource cards are folded away (local view preference, not session state).</summary>
        public bool DataHidden => dataHidden;
        /// <summary>Current bottom of the top block in canvas units; the camera framing follows it.</summary>
        public float TopInset => dataHidden ? CollapsedTopBlockBottom : TopBlockBottom;
        private float SideTop => TopInset + 8;
        public void ToggleData() => dataHidden = !dataHidden;
        /// <summary>Header area reserved for the session controls (start/pause, restart, connect), right-aligned.</summary>
        public static readonly Rect SessionDock = new Rect(1418 - 432, 8, 432, 48);
        private Action<Rect, Rect> sessionDock;
        /// <summary>Scene wiring: draws the session status line and buttons inside this dashboard's frame (dock, status line).</summary>
        public void ConfigureSessionDock(Action<Rect, Rect> draw) => sessionDock = draw;
        [SerializeField] private Texture2D battery, water, food, oxygen, carbonDioxide;
        [SerializeField, Tooltip("Pixel-art dashboard skin. Visual only; empty fields fall back to flat colours.")]
        private HudSkin skin = new HudSkin();
        public HudSkin Skin => skin;
        private static readonly GaugeKind[] Essential = { GaugeKind.BatteryEnergy, GaugeKind.PotableWater, GaugeKind.StoredFood, GaugeKind.CabinOxygen, GaugeKind.CabinCarbonDioxide };
        private static readonly Color Background = new Color(.035f, .065f, .10f, 1);
        private static readonly Color Cyan = new Color(.40f, .86f, .94f, 1);
        private static readonly Color Amber = new Color(1, .76f, .35f, 1);
        private GUIStyle text, small, heading, value, button, title, label, unit;
        private Vector2 scroll;
        private float stationContentHeight = 1500;
        private float detailsContentHeight = 1050;
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
            GUI.depth = HudDepth;
            try
            {
                if ((opened.IsValid || details) && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
                { ClosePanel(); Event.current.Use(); }
                // Session controls are a separate IMGUI component drawn at a lower depth (in front): their status line and
                // header buttons sit on top of this dashboard frame.
                skin.Panel(new Rect(8, 6, 1424, TopInset - 6));
                Header();
                if (dataHidden) CompactStatus();
                else for (int i = 0; i < Essential.Length; i++) Gauge(new Rect(22 + i * 282, GaugeTop, 268, GaugeHeight), Essential[i], Icon(i));
                skin.Panel(new Rect(8, FooterTop, 1424, 900 - FooterTop - 6));
                if (!Live)
                {
                    Label(new Rect(28, FooterTop + 12, 1100, 18), "? SIN DATOS VIGENTES · Conecta para recibir el estado. No se muestran reservas ni acciones confirmadas.", small, HudSkin.Amber);
                    Label(new Rect(28, FooterTop + 36, 1100, 44), "Sin conexión. El último estado no confirma el resultado de solicitudes pendientes.", text);
                    return;
                }
                Footer();
                if (opened.IsValid || details) skin.Panel(new Rect(944, SideTop, 488, SideBottom - SideTop));
                if (opened.IsValid) StationPanel();
                else if (details) DetailsPanel();
                if (Shown.Session.EssentialUnservedSeconds > 0)
                {
                    Paint(new Rect(22, SideTop, 910, 32), new Color(.29f, .12f, .06f, .95f));
                    HudSkin.Icon(new Rect(30, SideTop + 4, 24, 24), skin.statusCritical);
                    Label(new Rect(62, SideTop + 7, 860, 22), $"Carga esencial sin servicio · ventana del escenario: {Shown.Session.SecondsBeforeIrreversible:0.#} s · reduce cargas", text, HudSkin.Red);
                }
            }
            finally { GUI.matrix = previousMatrix; GUI.enabled = previousEnabled; GUI.color = previousColor; GUI.depth = previousDepth; }
        }

        private Texture2D Icon(int index)
        {
            Texture2D skinned = index == 0 ? skin.iconBattery : index == 1 ? skin.iconWater : index == 2 ? skin.iconFood : index == 3 ? skin.iconOxygen : skin.iconCarbonDioxide;
            if (skinned != null) return skinned;
            return index == 0 ? battery : index == 1 ? water : index == 2 ? food : index == 3 ? oxygen : carbonDioxide;
        }

        private void Header()
        {
            HudSkin.Icon(new Rect(22, 10, 44, 44), skin.badge);
            float x = skin.badge != null ? 80 : 22;
            string state = Live ? State(Shown.Outpost.State) : "SIN CONEXIÓN";
            string caption = "BASE VIVA  /  " + state;
            float w = title.CalcSize(new GUIContent(caption)).x;
            Label(new Rect(x, 14, w + 4, 34), caption, title);
            if (Live)
            {
                HudSkin.Paint(new Rect(x + w + 14, 30, 6, 6), HudSkin.Cyan);
                Label(new Rect(x + w + 28, 22, 200, 24), BaseOperationsText.Number(Shown.SimulatedTimeSeconds) + " s", unit, HudSkin.Title);
            }
            if (GUI.Button(new Rect(SessionDock.x - 8 - 210 - 8 - 170, HeaderButtonY, 170, 34), dataHidden ? "▼  Mostrar datos" : "▲  Ocultar datos", button)) ToggleData();
            float left = SessionDock.x - 8 - 210;
            GUI.enabled = Live;
            if (GUI.Button(new Rect(left, HeaderButtonY, 210, 34), details ? "Cerrar detalles" : "Recursos y actividad", button)) ToggleDetails();
            GUI.enabled = true;
            // Session controls get their own framed dock in this header, drawn in this pass so they belong to the panel.
            if (sessionDock != null)
            {
                skin.Card(SessionDock);
                sessionDock(SessionDock, new Rect(24, SessionStripY + 2, dataHidden ? 1040 : 1396, 22));
                GUI.enabled = true;
            }
            HudSkin.Paint(new Rect(22, SessionStripY - 6, 1396, 1), new Color(HudSkin.Line.r, HudSkin.Line.g, HudSkin.Line.b, .45f));
        }

        /// <summary>Collapsed view: one icon and status symbol per essential reserve, so a warning is never hidden.</summary>
        private void CompactStatus()
        {
            float x = 1418;
            for (int i = Essential.Length - 1; i >= 0; i--)
            {
                ResourceGauge gauge = Live ? Shown.GaugeOf(Essential[i]) : null;
                x -= 64;
                var cell = new Rect(x, SessionStripY - 1, 58, 30);
                HudSkin.Icon(new Rect(cell.x + 4, cell.y + 3, 24, 24), Icon(i));
                Texture2D symbol = StatusIcon(StatusOf(gauge));
                if (symbol != null) HudSkin.Icon(new Rect(cell.x + 30, cell.y + 6, 20, 20), symbol);
                else Paint(new Rect(cell.x + 34, cell.y + 10, 10, 10), StatusColor(StatusOf(gauge)));
                GUI.Label(cell, new GUIContent("", BaseOperationsText.Name(Essential[i]) + " · " + BaseOperationsText.Value(gauge) + " · " + BaseOperationsText.Band(gauge)), GUIStyle.none);
            }
        }

        private enum Status { Normal, Warning, Critical, NoData }
        private static Status StatusOf(ResourceGauge gauge)
        {
            if (gauge == null || gauge.Reading.Status != ReadingStatus.Valid || gauge.Band == GaugeBand.Unknown) return Status.NoData;
            if (gauge.Band == GaugeBand.Nominal) return Status.Normal;
            return gauge.Band == GaugeBand.Exhausted ? Status.Critical : Status.Warning;
        }
        private Texture2D StatusIcon(Status status) => status == Status.Normal ? skin.statusOk : status == Status.Warning ? skin.statusWarn
            : status == Status.Critical ? skin.statusCritical : skin.statusNone;
        private static Color StatusColor(Status status) => status == Status.Normal ? HudSkin.Green : status == Status.Warning ? HudSkin.Amber
            : status == Status.Critical ? HudSkin.Red : HudSkin.Dim;

        private void Gauge(Rect rect, GaugeKind kind, Texture2D icon)
        {
            skin.Card(rect);
            ResourceGauge gauge = Live ? Shown.GaugeOf(kind) : null;
            Status status = StatusOf(gauge);
            Color accent = StatusColor(status);
            if (skin.cardFrame == null) Paint(new Rect(rect.x, rect.y, 3, rect.height), accent);
            float x = rect.x + 16, w = rect.width - 32;
            HudSkin.Icon(new Rect(x, rect.y + 14, 24, 24), icon);
            Label(new Rect(x + 32, rect.y + 17, w - 64, 20), BaseOperationsText.Name(kind), label);
            HudSkin.Icon(new Rect(rect.xMax - 42, rect.y + 13, 26, 26), StatusIcon(status));
            bool valid = gauge != null && gauge.Reading.Status != ReadingStatus.Unavailable;
            if (valid)
            {
                string number = BaseOperationsText.Number(gauge.Reading.Value.Value);
                float nw = value.CalcSize(new GUIContent(number)).x;
                Label(new Rect(x, rect.y + 42, nw + 4, 32), number, value);
                string rest = (gauge.Capacity.HasValue ? "/ " + BaseOperationsText.Number(gauge.Capacity.Value) + " " : "") + BaseOperationsText.Unit(gauge.Reading.Unit);
                Label(new Rect(x + nw + 8, rect.y + 52, w - nw - 8, 20), rest, unit, HudSkin.Cyan);
            }
            else Label(new Rect(x, rect.y + 46, w, 26), "SIN LECTURA", value, HudSkin.Dim);
            // ONLY a declared capacity can be the denominator. Cabin partial pressures are never percentages.
            if (gauge != null && gauge.Reading.Status == ReadingStatus.Valid && gauge.Capacity.HasValue && gauge.Capacity.Value > 0)
                skin.Bar(new Rect(x, rect.y + 80, w, 12), (float)(gauge.Reading.Value.Value / gauge.Capacity.Value), BarFill(kind, status), accent);
            else
            {
                string line = gauge == null ? "Capacidad / rango sin confirmar"
                    : gauge.LimitAt.HasValue ? "Línea del escenario ≤ " + BaseOperationsText.Number(gauge.LimitAt.Value) + " " + BaseOperationsText.Unit(gauge.Reading.Unit)
                    : gauge.ReserveAt.HasValue ? "Reserva del escenario: " + BaseOperationsText.Number(gauge.ReserveAt.Value) + " " + BaseOperationsText.Unit(gauge.Reading.Unit)
                    : "Sin escala declarada";
                Label(new Rect(x, rect.y + 78, w, 16), line, small, HudSkin.Title);
            }
            Label(new Rect(x, rect.y + 98, w, 16), BaseOperationsText.Band(gauge), small, accent);
            string trend = BaseOperationsText.Trend(gauge);
            Texture2D arrow = gauge == null || !gauge.TrendPerSecond.HasValue || gauge.Reading.Status != ReadingStatus.Valid ? null
                : gauge.TrendPerSecond.Value > 0 ? skin.trendUp : gauge.TrendPerSecond.Value < 0 ? skin.trendDown : skin.trendFlat;
            HudSkin.Icon(new Rect(x, rect.y + 116, 14, 12), arrow);
            Label(new Rect(arrow != null ? x + 20 : x, rect.y + 114, w - 20, 16), trend, small,
                arrow == skin.trendUp && arrow != null ? HudSkin.Green : arrow == skin.trendDown && arrow != null ? HudSkin.Amber : HudSkin.Dim);
            HudSkin.Paint(new Rect(x, rect.y + 133, w, 1), new Color(HudSkin.Line.r, HudSkin.Line.g, HudSkin.Line.b, .35f));
            Label(new Rect(x, rect.y + 137, w, 16), CardFooter(kind), small, HudSkin.Dim);
            GUI.Label(rect, new GUIContent("", BaseOperationsText.Action(kind)), GUIStyle.none);
        }

        private Texture2D BarFill(GaugeKind kind, Status status)
        {
            if (status == Status.Critical) return skin.barRed;
            if (status == Status.Warning) return skin.barAmber;
            return kind == GaugeKind.BatteryEnergy ? skin.barBlue : kind == GaugeKind.PotableWater ? skin.barCyan : skin.barGreen;
        }

        /// <summary>Bottom line of each card: the power balance on the battery card (the old full-width energy line), otherwise
        /// the action that changes this reserve.</summary>
        private string CardFooter(GaugeKind kind)
        {
            if (!Live) return BaseOperationsText.Action(kind);
            if (kind != GaugeKind.BatteryEnergy) return BaseOperationsText.Action(kind);
            var p = Shown.Outpost.Resources.Power;
            double demand = p.EssentialRequestedWatts + p.ScienceRequestedWatts + Shown.Outpost.Resources.ServiceLoadRequestedWatts;
            return $"Gen {p.GenerationWatts:0.##} W · Dem {demand:0.##} W · Balance " + BaseOperationsText.Reading(Shown.GaugeOf(GaugeKind.PowerBalance).Reading);
        }

        private void Footer()
        {
            Shown.Expedition.TryGetCrew(Context.Port.Membership.Player, out var crew);
            string info = crew != null && crew.OnSuitSupply
                ? "TRAJE · " + ExpeditionPanel.Margin(crew.Margin, Shown.SimulatedTimeSeconds) + " · " + ExpeditionPanel.Protection(crew.Protection)
                : "WASD / flechas · camina y repartid el trabajo.   E · estación cercana / esclusa / tarea   ·   R · regreso";
            Label(new Rect(28, FooterTop + 10, 1120, 20), info, small, HudSkin.Label);
            string notice = Feedback;
            if (string.IsNullOrEmpty(notice) && !string.IsNullOrEmpty(GUI.tooltip)) notice = GUI.tooltip;
            // A received shelter advisory keeps its own line: an answer or a tooltip never hides it.
            if (TryAdvisory(crew, out string advisory))
            {
                HudSkin.Icon(new Rect(28, FooterTop + 33, 22, 22), skin.statusWarn);
                Label(new Rect(skin.statusWarn != null ? 58 : 28, FooterTop + 32, 1090, 28), advisory, text, HudSkin.Amber);
                Label(new Rect(28, FooterTop + 62, 1120, 24), notice, small);
            }
            else
            {
                Label(new Rect(28, FooterTop + 32, 1120, 32), string.IsNullOrEmpty(notice) ? NoAdvisory : notice, text);
                Legend(new Rect(28, FooterTop + 66, 700, 18));
            }
            StationId nearby = NearbyStation();
            GUI.enabled = Live && nearby.IsValid;
            if (GUI.Button(new Rect(1172, FooterTop + 20, 246, 46), opened.IsValid ? "Cerrar estación" : "Abrir estación cercana", button))
            { if (opened.IsValid) ClosePanel(); else OpenNearbyStation(); }
            GUI.enabled = true;
        }

        /// <summary>Key of the status symbols used on the resource cards.</summary>
        private void Legend(Rect row)
        {
            if (skin.statusOk == null) return;
            float x = row.x;
            Label(new Rect(x, row.y + 1, 80, 16), "ESTADO:", label); x += 70;
            foreach (var (icon, caption, color) in new[] { (skin.statusOk, "EN RANGO", HudSkin.Green), (skin.statusWarn, "AVISO", HudSkin.Amber),
                (skin.statusCritical, "AGOTADO", HudSkin.Red), (skin.statusNone, "SIN DATOS", HudSkin.Dim) })
            {
                HudSkin.Icon(new Rect(x, row.y, 18, 18), icon);
                Label(new Rect(x + 22, row.y + 1, 100, 16), caption, small, color);
                x += 30 + small.CalcSize(new GUIContent(caption)).x + 18;
            }
        }
        public const string NoAdvisory = "Sin aviso recibido · no significa seguro. Mantén las reservas y comprueba los equipos.";

        /// <summary>The latest shelter advisory RECEIVED by this player (never a global emission), or false.</summary>
        public string Advisory => Live && TryAdvisory(null, out string text) ? text : NoAdvisory;

        private bool TryAdvisory(ExpeditionCrewState crew, out string text)
        {
            text = null;
            IssuedAdvisory latest = null;
            AdvisoryReception received = null;
            foreach (var advisory in Shown.Expedition.Advisories)
                if (Shown.Expedition.TryGetReception(advisory.Message.Id, Context.Port.Membership.Player, out var reception)) { latest = advisory; received = reception; }
            if (latest == null) return false;
            var message = latest.Message;
            string instruction = message.Kind == AdvisoryKind.InstructionEnd ? "Control: fin explícito del periodo de refugio del ejercicio."
                : "AVISO · Entra al refugio" + (message.ShelterBySeconds.HasValue ? $" antes de t={message.ShelterBySeconds:0.#} s." : ".");
            text = instruction + $" Recibido t={received.ReceivedAtSeconds:0.#} s · válido hasta {message.ValidUntilSeconds:0.#} s."
                + (latest.Validity == AdvisoryValidity.Expired && message.Kind != AdvisoryKind.InstructionEnd ? " DATO VENCIDO: mantén la instrucción." : "")
                + (crew != null && message.Kind != AdvisoryKind.InstructionEnd ? " · " + ExpeditionPanel.Protection(crew.Protection) : "");
            return true;
        }
        private void StationPanel()
        {
            const float x = 954, width = 464;
            if (skin.panelFrame == null) { Paint(new Rect(x, SideTop + 10, width, SideBottom - SideTop - 20), Background); Paint(new Rect(x, SideTop + 10, 3, SideBottom - SideTop - 20), Cyan); }
            string title = opened == Context.Scenario.WaterStation ? "RECICLADOR DE AGUA"
                : opened == Context.Scenario.AirStation ? "UNIDAD DE AIRE" : "SUMINISTROS DEL EQUIPO";
            Label(new Rect(x + 18, SideTop + 20, 376, 26), title, heading, HudSkin.Title);
            if (GUI.Button(new Rect(x + width - 50, SideTop + 16, 34, 30), "×", button)) { ClosePanel(); return; }
            HudSkin.Paint(new Rect(x + 14, SideTop + 52, width - 28, 1), new Color(HudSkin.Line.r, HudSkin.Line.g, HudSkin.Line.b, .45f));
            scroll = GUI.BeginScrollView(new Rect(x + 14, SideTop + 60, width - 25, SideBottom - SideTop - 74), scroll, new Rect(0, 0, width - 48, stationContentHeight));
            try
            {
                float y = 2;
                if (Shown.TryGetOperationAt(opened, out var operation))
                {
                    Label(new Rect(4, y, 406, 54), Performer(operation.Performer) + " · " + BaseOperationsText.Operation(operation.Action, operation.Choice)
                        + $"\n{operation.WorkSeconds:0.#} / {operation.DurationSeconds:0.#} s" + (operation.Stalled ? " · ESPERA: SIN POTENCIA" : " · CONFIRMADO"), text);
                    y += 60;
                    skin.Bar(new Rect(4, y, 396, 12), (float)operation.Fraction, operation.Stalled ? skin.barAmber : skin.barCyan, operation.Stalled ? Amber : Cyan); y += 24;
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
                // The latest decision ON THIS machine, even if a teammate decided something elsewhere afterwards.
                DecisionRecord decision = kind.HasValue ? LatestDecision(kind.Value) : null;
                if (decision != null)
                {
                    Label(new Rect(4, y + 10, 396, 98), "ÚLTIMA CONSECUENCIA CONFIRMADA · " + Performer(decision.Performer) + $" · t={decision.AtSeconds:0.#} s\n"
                        + BaseOperationsText.Explanation(decision.ExplanationKey), text);
                    y += 112;
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
            if (skin.panelFrame == null) Paint(new Rect(954, SideTop + 10, 464, SideBottom - SideTop - 20), Background);
            Label(new Rect(974, SideTop + 20, 410, 29), "RESERVAS, EQUIPO Y ACTIVIDAD", heading, HudSkin.Title);
            HudSkin.Paint(new Rect(968, SideTop + 52, 436, 1), new Color(HudSkin.Line.r, HudSkin.Line.g, HudSkin.Line.b, .45f));
            scroll = GUI.BeginScrollView(new Rect(972, SideTop + 60, 432, SideBottom - SideTop - 74), scroll, new Rect(0, 0, 409, detailsContentHeight));
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
                Label(new Rect(2, y, 402, 27), "Salidas finalizadas: " + Shown.Session.ExpeditionsFinished + " · la sesión continúa", small); y += 34;
                string eva = ExpeditionDetails();
                float evaHeight = text.CalcHeight(new GUIContent(eva), 402);
                Label(new Rect(2, y, 402, evaHeight), eva, text); y += evaHeight + 8;
                detailsContentHeight = Mathf.Max(SideBottom - SideTop - 74, y);
            }
            finally { GUI.EndScrollView(); }
        }

        public DecisionRecord LatestDecision(EquipmentKind kind)
        {
            if (!Live) return null;
            for (int i = Shown.Decisions.Count - 1; i >= 0; i--)
                if (Shown.Decisions[i].Equipment == kind) return Shown.Decisions[i];
            return null;
        }

        private string Performer(PlayerId player) => Shown.Outpost.TryGetCrew(player, out var crew) ? "Astronauta " + (crew.Slot + 1) + (player == Context.Port.Membership.Player ? " · TÚ" : "") : "Compañero";
        /// <summary>Phase 3 data that the retired ExpeditionPanel showed: objectives, airlock, own suit and protection.</summary>
        private string ExpeditionDetails()
        {
            var s = Shown.Expedition;
            var b = new StringBuilder("EXPEDICIÓN Y REFUGIO\n");
            foreach (var o in s.Objectives) b.Append(o.Met ? "HECHO · " : "PENDIENTE · ").Append(o.Key).Append(o.Role == ObjectiveRole.Optional ? " (opcional)" : "").Append('\n');
            b.Append($"Esclusa: {s.Airlock.Occupants.Count}/{Context.Scenario.Expedition.Airlock.Capacity} plazas · ")
                .Append(s.Airlock.IsCycling ? $"transición {s.Airlock.CycleFraction:P0}" : "disponible").Append(s.Airlock.Stalled ? " · SIN SERVICIO" : "").Append('\n');
            b.Append("Refugio: ").Append(!s.Shelter.Prepared ? "sin preparar" : s.Shelter.ServiceAvailable ? "preparado · con servicio" : "SIN SERVICIO").Append('\n');
            if (s.TryGetCrew(Context.Port.Membership.Player, out var mine))
            {
                b.Append("Tú: ").Append(ExpeditionPanel.Margin(mine.Margin, s.SimulatedTimeSeconds)).Append(" · ").Append(ExpeditionPanel.Protection(mine.Protection)).Append('\n');
                if (mine.SuitIndex >= 0 && mine.SuitIndex < s.Suits.Count)
                {
                    var suit = s.Suits[mine.SuitIndex];
                    b.Append("Traje: ").Append(suit.Status == SuitStatus.Sealed ? "sellado" : "puesto").Append(" · revisión ").Append(suit.Completed).Append('\n');
                    foreach (var model in Context.Scenario.Expedition.Eva.Model.Services)
                        b.Append($"{model.Service}: {suit.Reading.Levels.Get(model.Service):0.###}/{model.Capacity:0.###} {model.Unit} · reserva {model.Reserve:0.###}\n");
                }
            }
            return b.ToString().TrimEnd();
        }

        private static string OrderLabel(OrderState state) => state == OrderState.Pending ? "PENDIENTE" : state == OrderState.InTransit ? "EN TRÁNSITO" : state == OrderState.Arrived ? "LLEGÓ · SIN DESCARGAR" : state == OrderState.Unloaded ? "DESCARGADO" : "CANCELADO";
        private static string Manifest(System.Collections.Generic.IReadOnlyList<ManifestItem> items)
        {
            var text = new StringBuilder();
            foreach (var item in items) { if (text.Length > 0) text.Append(" · "); text.Append(item.IsSpare ? item.Count + " × " + item.SpareKey : BaseOperationsText.Number(item.Kilograms) + " kg " + (item.Resource == ResourceKind.StoredFood ? "comida" : "agua")); }
            return text.Length == 0 ? "Sin carga pendiente" : text.ToString();
        }
        private static string RepairProgress(RepairSteps steps) => "inspección ✓ → aislamiento ✓ → filtro " + ((steps & RepairSteps.Intervene) != 0 ? "✓" : "pendiente") + " → prueba " + ((steps & RepairSteps.Test) != 0 ? "✓" : "pendiente");
        private static string State(SessionState state) => state == SessionState.Running ? "EN MARCHA" : state == SessionState.Paused ? "EN PAUSA" : state == SessionState.Ready ? "LISTA" : "DETENIDA";
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
            value = new GUIStyle(text) { fontSize = 26, fontStyle = FontStyle.Bold, wordWrap = false };
            title = new GUIStyle(text) { fontSize = 26, fontStyle = FontStyle.Bold, wordWrap = false };
            title.normal.textColor = HudSkin.Title;
            label = new GUIStyle(text) { fontSize = 13, fontStyle = FontStyle.Bold, wordWrap = false };
            label.normal.textColor = HudSkin.Label;
            unit = new GUIStyle(text) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = false };
            button = skin.Button(new GUIStyle(GUI.skin.button) { fontSize = 14, wordWrap = true });
        }
    }
}
