using System;
using System.Text;
using Trainer.Application;
using Trainer.Application.Outpost;
using Trainer.Simulation.LifeSupport;
using Trainer.Unity.World.Outpost;
using UnityEngine;

namespace Trainer.Unity.Presentation.Outpost
{
    /// <summary>Secondary HUD and a proximity-only station panel. Reads the port; task intentions go
    /// through D3's existing Interaction instance, injected as delegates by D2. No movement or clock.</summary>
    [DisallowMultipleComponent]
    public sealed class OutpostPanel : MonoBehaviour
    {
        [SerializeField] private BaseInteriorView interior;
        [SerializeField] private Texture2D energyIcon;
        [SerializeField] private Texture2D objectiveIcon;
        private IOutpostSessionPort port;
        private OutpostScenario scenario;
        private OutpostSessionSnapshot snapshot;
        private StationId selected;
        private Func<StationId, TaskTypeId, bool> start;
        private Func<StationId, bool> cancel;
        private Func<OutpostCommand> pending;
        private Action<bool> block;
        private Action<OutpostSessionSnapshot> snapshotListener;
        private Action<OutpostMembership> membershipListener;
        private Action<CommandResult> resultListener;
        private Action<OutpostCommand, string> abandonListener;
        private int binding;
        private bool details;
        private GUIStyle small, heading, large, button;
        public string DevelopmentLabel { get; set; }
        public string Feedback { get; private set; } = "Acércate a una estación para trabajar.";
        public bool HasCurrentData => snapshot != null && port != null && port.Membership.IsJoined;
        public OutpostSessionSnapshot Snapshot => HasCurrentData ? snapshot : null;
        public bool PanelOpen => selected.IsValid;
        public bool IsPending => pending != null && pending() != null;

        public void ConfigureInteraction(Func<StationId, TaskTypeId, bool> requestStart,
            Func<StationId, bool> requestCancel, Func<OutpostCommand> getPending, Action<bool> setBlocked)
        {
            CloseStation(); // Release the previous input owner before replacing its callbacks.
            start = requestStart; cancel = requestCancel; pending = getPending; block = setBlocked;
        }

        public void Bind(IOutpostSessionPort value, OutpostScenario configuration, BaseInteriorView view = null)
        {
            Unsubscribe();
            Clear();
            if (!ReferenceEquals(value,port))
            {
                start = null; cancel = null; pending = null; block = null;
            }
            port = value;
            scenario = configuration;
            if (view != null) interior = view;
            if (configuration != null && interior != null) interior.Configure(configuration);
            if (isActiveAndEnabled) Subscribe();
        }

        private void Subscribe()
        {
            if (port == null || scenario == null || snapshotListener != null) return;
            int token = ++binding;
            snapshotListener = state => { if (token == binding) Accept(state); };
            membershipListener = membership =>
            {
                if (token != binding) return;
                if (!membership.IsJoined) Clear();
                else if (port.TryGetSnapshot(out OutpostSessionSnapshot state)) Accept(state);
            };
            resultListener = result =>
            {
                if (token == binding && HasCurrentData)
                    Feedback = result.Accepted ? "Solicitud aceptada. Espera el estado confirmado." : DescribeReason(result.Reason);
            };
            abandonListener = (command, reason) =>
            {
                if (token == binding) Feedback = "Enlace interrumpido: resultado sin confirmar. " + DescribeReason(reason);
            };
            port.SnapshotChanged += snapshotListener;
            port.MembershipChanged += membershipListener;
            port.CommandCompleted += resultListener;
            port.CommandAbandoned += abandonListener;
            if (port.Membership.IsJoined && port.TryGetSnapshot(out OutpostSessionSnapshot current)) Accept(current);
        }

        private void Unsubscribe()
        {
            ++binding;
            if (port != null)
            {
                port.SnapshotChanged -= snapshotListener;
                port.MembershipChanged -= membershipListener;
                port.CommandCompleted -= resultListener;
                port.CommandAbandoned -= abandonListener;
            }
            snapshotListener = null; membershipListener = null; resultListener = null; abandonListener = null;
        }

        private void Accept(OutpostSessionSnapshot state)
        {
            if (!isActiveAndEnabled || !port.Membership.IsJoined || state.ScenarioId != scenario.Id ||
                !state.TryGetCrew(port.Membership.Player, out _) || !state.IsNewerThan(snapshot)) return;
            bool restarted = snapshot != null && state.Epoch != snapshot.Epoch;
            bool first = snapshot == null;
            snapshot = state;
            if (first || restarted) Feedback = "Acércate a una estación para trabajar.";
            if (restarted || !InReach(selected) || state.State == SessionState.Completed || state.State == SessionState.Interrupted)
                CloseStation();
            if (interior != null) interior.Present(state, port.Membership.Player);
        }

        public bool InReach(StationId id) => HasCurrentData && scenario.Layout.TryGetStation(id, out StationPlacement at)
            && snapshot.TryGetCrew(port.Membership.Player, out CrewMemberState local) && at.IsWithinReach(local.Pose.Position);

        public StationId NearestStation()
        {
            if (!HasCurrentData || !snapshot.TryGetCrew(port.Membership.Player, out CrewMemberState local)) return StationId.None;
            StationId nearest = StationId.None;
            double best = double.PositiveInfinity;
            foreach (StationPlacement station in scenario.Layout.Stations)
            {
                double distance = local.Pose.Position.DistanceTo(station.Position);
                if (station.IsWithinReach(local.Pose.Position) && distance < best) { best = distance; nearest = station.Id; }
            }
            return nearest;
        }

        public bool OpenStation(StationId id)
        {
            if (!InReach(id) || snapshot.State == SessionState.Completed || snapshot.State == SessionState.Interrupted) return false;
            selected = id;
            block?.Invoke(true);
            return true;
        }

        public void CloseStation()
        {
            bool wasOpen = selected.IsValid;
            selected = StationId.None;
            if (wasOpen) block?.Invoke(false);
        }

        public bool CanStart(TaskDefinition task)
        {
            if (task == null || !PanelOpen || task.Station != selected || !InReach(selected) ||
                snapshot.State != SessionState.Running || IsPending || start == null) return false;
            if (!snapshot.TryGetStation(selected, out StationState station) || station.Availability != StationAvailability.Available)
                return false;
            foreach (StationState current in snapshot.Stations) if (current.Occupant == port.Membership.Player) return false;
            return true;
        }

        public bool RequestStart(TaskTypeId id)
        {
            if (scenario == null) return false;
            foreach (TaskDefinition task in scenario.Tasks)
                if (task.Id == id && CanStart(task)) return start(task.Station, task.Id);
            return false;
        }

        public bool RequestCancel()
        {
            if (!PanelOpen || !InReach(selected) || cancel == null || IsPending ||
                (snapshot.State != SessionState.Running && snapshot.State != SessionState.Paused) ||
                !snapshot.TryGetStation(selected, out StationState station) || station.ActiveTask == null ||
                station.Occupant != port.Membership.Player) return false;
            return cancel(selected);
        }

        private void Clear()
        {
            CloseStation(); snapshot = null; details = false;
            Feedback = "Sin datos vigentes · esperando incorporación a la base.";
            if (interior != null) interior.ClearReadings();
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() { Unsubscribe(); Clear(); }
        private void OnDestroy() { Unsubscribe(); Clear(); }

        private void OnGUI()
        {
            InitStyles();
            Matrix4x4 previous = GUI.matrix;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width-1280*scale)/2, (Screen.height-720*scale)/2, 0),
                Quaternion.identity, new Vector3(scale, scale, 1));
            try { DrawHud(); }
            finally { GUI.matrix = previous; GUI.enabled = true; GUI.color = Color.white; }
        }

        private void DrawHud()
        {
            Box(new Rect(16,16,1248,78));
            GUI.Label(new Rect(34,25,265,26), "BASE · TRIPULACIÓN", heading);
            GUI.Label(new Rect(34,55,265,26), HasCurrentData ? StateLabel(snapshot.State) + " · " + snapshot.SimulatedTimeSeconds.ToString("0.0") + " s" : "SIN CONEXIÓN VIGENTE", small);
            if (HasCurrentData)
            {
                Card(318, energyIcon, "ENERGÍA", snapshot.Resources.Power.EnergyJoules.ToString("0") + " J");
                Card(570, null, "AGUA POTABLE", snapshot.Resources.LifeSupport.Water.PotableKilograms.ToString("0.0") + " kg");
                ObjectiveProgress required = null;
                foreach (ObjectiveProgress objective in snapshot.Objectives) if (objective.Required) { required = objective; break; }
                Card(822, objectiveIcon, "OBJETIVO", required == null ? "—" : required.Current.ToString("0.#") + " / " + required.Target.ToString("0.#") + " " + Unit(required.Unit));
                if (GUI.Button(new Rect(1090,34,155,40), details ? "Ocultar detalles" : "Más detalles", button)) details = !details;
            }
            if (!string.IsNullOrEmpty(DevelopmentLabel))
            { Box(new Rect(16,102,1248,30)); GUI.Label(new Rect(28,106,1220,24), DevelopmentLabel, small); }
            if (details && HasCurrentData) DrawDetails();
            if (PanelOpen && HasCurrentData) DrawTask();
            Box(new Rect(16,630,1248,74));
            string hint = "WASD / flechas · Camina hacia una estación";
            StationId near = NearestStation();
            if (near.IsValid) hint = "E · Empezar / cancelar aquí     ·     " + StationLabel(near);
            if (!HasCurrentData) hint = "Esperando el estado confirmado de tu astronauta";
            else if (snapshot.State == SessionState.Completed || snapshot.State == SessionState.Interrupted)
                hint = "MISIÓN FINALIZADA · " + OutcomeLabel(snapshot.Outcome);
            GUI.Label(new Rect(32,638,1000,26), hint, heading);
            GUI.Label(new Rect(32,669,1015,26), IsPending ? "Solicitud pendiente · el estado visible sigue confirmado" : Feedback, small);
            GUI.enabled = HasCurrentData && near.IsValid && !PanelOpen;
            if (GUI.Button(new Rect(1074,646,172,40), "Ver tarea cercana", button)) OpenStation(near);
            GUI.enabled = true;
        }

        private void DrawDetails()
        {
            Box(new Rect(944,142,320,460));
            GUILayout.BeginArea(new Rect(962,155,284,434));
            GUILayout.Label("SUMINISTROS Y LECTURAS", heading);
            var life = snapshot.Resources.LifeSupport;
            GUILayout.Label("Agua pendiente de tratar\n" + life.Water.UntreatedKilograms.ToString("0.0") + " kg", small);
            GUILayout.Label("Comida almacenada\n" + life.Food.StoredKilograms.ToString("0.0") + " kg", small);
            GUILayout.Label("Aire de cabina · una zona\nO₂: " + Reading(life.Cabin.Oxygen) + "\nCO₂: " + Reading(life.Cabin.CarbonDioxide), small);
            GUILayout.Label("Último paso eléctrico\nServicios: " + snapshot.Resources.ServiceLoadServedWatts.ToString("0.#") + " / " +
                snapshot.Resources.ServiceLoadRequestedWatts.ToString("0.#") + " W", small);
            foreach (ObjectiveProgress objective in snapshot.Objectives)
                GUILayout.Label((objective.Required ? "Requerido · " : "Opcional · ") + TaskLabel(objective.Key) + "\n" +
                    objective.Current.ToString("0.#") + " / " + objective.Target.ToString("0.#") + " " + Unit(objective.Unit), small);
            GUILayout.EndArea();
        }

        private void DrawTask()
        {
            Box(new Rect(32,156,420,448));
            GUILayout.BeginArea(new Rect(52,174,380,410));
            GUILayout.Label(StationLabel(selected), large);
            if (snapshot.TryGetStation(selected, out StationState station))
            {
                if (station.ActiveTask != null)
                {
                    snapshot.TryGetCrew(station.Occupant, out CrewMemberState crew);
                    GUILayout.Label("OCUPADA · Astronauta " + (crew == null ? "?" : (crew.Slot + 1).ToString()), heading);
                    GUILayout.Label(station.ActiveTask.WorkSeconds.ToString("0.0") + " / " + station.ActiveTask.DurationSeconds.ToString("0.#") +
                        " s confirmados" + (station.ActiveTask.Stalled ? "\nSin servicio suficiente: no avanza" : ""), small);
                }
                else GUILayout.Label(station.Availability == StationAvailability.Available ? "DISPONIBLE" : "NO DISPONIBLE", heading);
                foreach (TaskDefinition task in scenario.Tasks)
                {
                    if (task.Station != selected) continue;
                    GUILayout.Space(10);
                    GUILayout.Label(TaskLabel(task.Id.Value), heading);
                    double watts = task.Category == TaskCategory.Science ? scenario.Simulation.Power.ScienceConsumptionWatts : task.PowerWatts;
                    GUILayout.Label(task.DurationSeconds.ToString("0.#") + " s de trabajo · " + watts.ToString("0.#") + " W mientras trabaja\n" + Effects(task.CompletionEffect), small);
                    GUI.enabled = CanStart(task);
                    if (GUILayout.Button(IsPending ? "Esperando confirmación…" : "Empezar aquí", button, GUILayout.Height(36))) RequestStart(task.Id);
                    GUI.enabled = true;
                }
                GUI.enabled = station.Occupant == port.Membership.Player && InReach(selected) && !IsPending && cancel != null;
                if (GUILayout.Button("Cancelar mi tarea", button, GUILayout.Height(34))) RequestCancel();
                GUI.enabled = true;
            }
            GUILayout.Label("Al cancelar no se devuelve la energía usada.\nLos materiales cambian solo al completar.", small);
            if (GUILayout.Button("Cerrar · volver a caminar", button, GUILayout.Height(36))) CloseStation();
            GUILayout.EndArea();
        }

        private string StationLabel(StationId id)
        {
            if (interior != null) foreach (var station in interior.Stations) if (station.Placement.Id == id) return station.DisplayName;
            return id.Value;
        }

        public static string Reading(GasReading value) => value.Status == ReadingStatus.Unavailable ? "sin lectura" :
            value.PartialPressureKilopascals.Value.ToString("0.###") + " kPa · " +
            (value.Status == ReadingStatus.Stale ? "LECTURA ANTIGUA" : "medido") + " @ " + value.ObservedAtSeconds.Value.ToString("0.#") + " s";
        private static string Unit(ProgressUnit unit) => unit == ProgressUnit.Seconds ? "s" : unit == ProgressUnit.Kilograms ? "kg" : "veces";
        private static string TaskLabel(string key) => key == "treat-water" ? "Tratar agua" : key == "run-experiment" || key == "science" ? "Investigar muestras" : key == "check-cabin-air" ? "Revisar el aire" : key;
        private static string StateLabel(SessionState state) => state == SessionState.Running ? "EN MISIÓN" : state == SessionState.Paused ? "EN PAUSA" : state == SessionState.Ready ? "LISTA" : "FINALIZADA";
        private static string OutcomeLabel(OutpostOutcome value) => value == OutpostOutcome.ObjectiveAchieved ? "Objetivo logrado" : value == OutpostOutcome.TimeExpired ? "Tiempo agotado" : value == OutpostOutcome.EssentialServiceLost ? "Servicio esencial interrumpido" : "Simulación interrumpida";
        public static string DescribeReason(string reason)
        {
            switch (reason)
            {
                case OutpostReasons.OutOfRange: return "Acércate a la estación.";
                case OutpostReasons.StationOccupied: return "Otra persona ocupa la estación.";
                case OutpostReasons.PlayerBusy: return "Termina o cancela tu otra tarea.";
                case OutpostReasons.InsufficientResources: return "Faltan materiales o capacidad para completar.";
                case OutpostReasons.EpochMismatch: return "La misión se reinició. Consulta el estado actual.";
                case OutpostReasons.SessionEnded: return "La misión terminó.";
                case OutpostReasons.ConnectionLost: return "Se perdió la conexión.";
                case OutpostReasons.InvalidState: return "Esa tarea no está disponible en el estado actual de la misión.";
                case OutpostReasons.NotJoined:
                case OutpostReasons.NotConnected:
                case OutpostReasons.LocalDisconnect: return "Debes incorporarte de nuevo a la base.";
                case OutpostReasons.StationUnavailable: return "Esta estación no está disponible.";
                case OutpostReasons.NoActiveTask: return "No tienes una tarea activa en esa estación.";
                default: return "No se pudo aplicar la solicitud. Consulta el estado de la base.";
            }
        }
        private static string Effects(ResourceEffect effect)
        {
            if (effect.IsEmpty) return "Sin transferencia de materiales.";
            var text = new StringBuilder("Al completar: ");
            foreach (ResourceChange change in effect.Changes)
                text.Append(change.DeltaKilograms.ToString("+0.##;-0.##")).Append(" kg ")
                    .Append(change.Kind == ResourceKind.PotableWater ? "potable" : change.Kind == ResourceKind.UntreatedWater ? "por tratar" : "comida").Append(" · ");
            if (effect.BoundaryOutflowKilograms != 0) text.Append(effect.BoundaryOutflowKilograms.ToString("0.##")).Append(" kg salen del modelo");
            return text.ToString();
        }

        private void InitStyles()
        {
            if (small != null) return;
            small = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, richText = false, normal = { textColor = new Color(.75f,.83f,.9f) } };
            heading = new GUIStyle(small) { fontSize = 17, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            large = new GUIStyle(heading) { fontSize = 23 };
            button = new GUIStyle(GUI.skin.button) { fontSize = 15, wordWrap = true, padding = new RectOffset(8,8,6,6) };
        }
        private static void Box(Rect rect)
        {
            GUI.color = new Color(.055f,.09f,.15f,1); GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(.25f,.56f,.67f); GUI.DrawTexture(new Rect(rect.x,rect.y,rect.width,2),Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
        private void Card(float x, Texture icon, string label, string value)
        {
            if (icon != null) GUI.DrawTexture(new Rect(x,36,32,32),icon,ScaleMode.ScaleToFit);
            GUI.Label(new Rect(x+42,25,202,22),label,small);
            GUI.Label(new Rect(x+42,50,202,30),value,large);
        }
    }
}
