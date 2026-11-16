using System;
using Trainer.Application.Incidents;
using Trainer.Application.Outpost;
using Trainer.Unity.Presentation.BaseOperations;
using UnityEngine;

namespace Trainer.Unity.Presentation.Incidents
{
    /// <summary>A phase 5 view that can open the decisions of a station the local player activated with E.</summary>
    public interface ILivingBaseStationView
    {
        bool OpenStation(StationId station);
    }

    /// <summary>
    /// Phase 5 presentation (D4), IMGUI on the same 1440×900 virtual canvas and skin as the phase 4 dashboard (which keeps its own
    /// place and is not modified). It reads ONLY the confirmed <see cref="LivingBaseSnapshot"/> and sends intents through
    /// <see cref="ILivingBaseIntents"/>; it never predicts an outcome, keeps an inventory or credit balance, or advances time.
    /// <list type="bullet">
    /// <item>Left column: data-mode badge, incident cards (phase + what to do), the space-weather card (mode label, instruction
    /// state, deadline, each issued message with its validity and basis, own shelter state, and on demand the DONKI source records
    /// that the issued messages quote), and the science board (credits, offers, accept/postpone/cancel).</item>
    /// <item>Right panel, opened with E at the power panel or the upgrade station: power status from the last CURRENT inspection,
    /// operation buttons and progress, or the upgrade card. Opening blocks movement and keys; Escape (or the close button) releases
    /// them.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LivingBasePanel : MonoBehaviour, ILivingBaseView, ILivingBaseStationView
    {
        public const int Depth = BaseOperationsPanel.HudDepth - 2;
        private const float ColumnX = 22, ColumnW = 430, SideX = 944, SideW = 488, SideBottom = 798;

        [SerializeField, Tooltip("Phase 4 dashboard: its top inset is respected and its side panel is closed when this one opens.")]
        private BaseOperationsPanel phase4;
        [SerializeField] private HudSkin skin = new HudSkin();
        [SerializeField] private bool showColumn = true;

        private LivingBaseViewContext context;
        private LivingBaseSnapshot shown;
        private StationId opened = StationId.None;
        private bool sourcesOpen;
        private string answer;
        private Vector2 columnScroll, sourcesScroll;
        private GUIStyle text, small, heading, button, badge;

        public bool Live => isActiveAndEnabled && context != null && context.Port.Membership.IsJoined && shown != null;
        public LivingBaseSnapshot Shown => shown;
        public StationId Opened => opened;
        public bool SourcesOpen => sourcesOpen;
        public bool Pending => Live && context.Intents.HasPending;
        public string Feedback => !Live ? "Sin conexión: sin datos vigentes."
            : Pending ? "SOLICITUD ENVIADA · esperando respuesta; el efecto aún no está confirmado."
            : LivingBaseText.Reason(answer);

        public void Bind(LivingBaseViewContext value)
        {
            Unbind();
            if (!isActiveAndEnabled || value == null) return;
            context = value;
            context.Port.LivingBaseChanged += Receive;
            context.Port.MembershipChanged += Membership;
            context.Intents.Answered += Answered;
            if (context.Port.Membership.IsJoined && context.Port.TryGetLivingBase(out LivingBaseSnapshot s)) Receive(s);
        }

        public void Unbind()
        {
            if (context != null)
            {
                context.Port.LivingBaseChanged -= Receive;
                context.Port.MembershipChanged -= Membership;
                context.Intents.Answered -= Answered;
                context.UnblockMovement(this);
            }
            context = null;
            shown = null;
            Clear();
        }

        private void Clear()
        {
            opened = StationId.None;
            sourcesOpen = false;
            answer = null;
            columnScroll = sourcesScroll = Vector2.zero;
        }

        private void Answered(string reason)
        {
            if (Live) answer = reason;
        }

        private void Membership(OutpostMembership membership)
        {
            context?.UnblockMovement(this);
            shown = null;
            Clear();
            if (membership.IsJoined && context.Port.TryGetLivingBase(out LivingBaseSnapshot s)) Receive(s);
        }

        private void Receive(LivingBaseSnapshot next)
        {
            if (context == null || next == null || !context.Port.Membership.IsJoined) return;
            if (shown != null && !next.BaseOperations.IsNewerThan(shown.BaseOperations)) return;
            if (!next.BaseOperations.Outpost.TryGetCrew(context.Port.Membership.Player, out _))
            {
                context.UnblockMovement(this);
                shown = null;
                Clear();
                return;
            }
            bool restart = shown != null && next.Epoch != shown.Epoch;
            shown = next;
            if (restart)
            {
                context.UnblockMovement(this);
                Clear();
            }
            if (opened.IsValid && !IsNearby(opened)) ClosePanel();
        }

        // ---- Public actions (also used by the autopilot and tests) -------------------------------------------

        public bool IsNearby(StationId station) =>
            Live && context.Scenario.BaseOperations.Layout.TryGetStation(station, out StationPlacement placement)
            && shown.BaseOperations.Outpost.TryGetCrew(context.Port.Membership.Player, out var crew) && placement.IsWithinReach(crew.Pose.Position);

        public bool OpenStation(StationId station)
        {
            if (!Live || !(station == context.Scenario.PowerStation || station == context.Scenario.Upgrade.Station) || !IsNearby(station)) return false;
            if (phase4 != null) phase4.ClosePanel();
            opened = station;
            context.BlockMovement(this);
            return true;
        }

        public void ClosePanel()
        {
            context?.UnblockMovement(this);
            opened = StationId.None;
            GUIUtility.keyboardControl = 0;
        }

        public void ToggleSources() => sourcesOpen = Live && !sourcesOpen;

        public bool RequestPower(IncidentAction action)
        {
            if (!Live || opened != context.Scenario.PowerStation || !IsNearby(opened) || Pending) return false;
            return Sent(context.Intents.RequestPower(action));
        }

        public bool RequestUpgrade()
        {
            if (!Live || opened != context.Scenario.Upgrade.Station || !IsNearby(opened) || Pending) return false;
            return Sent(context.Intents.RequestInstallUpgrade());
        }

        public bool RequestCancelOperation()
        {
            if (!Live || !opened.IsValid || Pending) return false;
            return Sent(context.Intents.RequestCancelOperation(opened));
        }

        public bool RequestAssignment(IncidentAction action, long sequence)
        {
            if (!Live || Pending) return false;
            return Sent(context.Intents.RequestAssignment(action, sequence));
        }

        private bool Sent(bool sent)
        {
            answer = sent ? null : context.Intents.LastReason ?? "RequestNotSent";
            return sent;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) ClosePanel();
        }

        private void OnDisable() => Unbind();

        // ---- Drawing --------------------------------------------------------------------------------------------

        private void OnGUI()
        {
            if (context == null) return;
            Styles();
            Matrix4x4 previousMatrix = GUI.matrix;
            bool previousEnabled = GUI.enabled;
            int previousDepth = GUI.depth;
            float scale = Mathf.Min(Screen.width / 1440f, Screen.height / 900f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1440 * scale) / 2, (Screen.height - 900 * scale) / 2, 0), Quaternion.identity,
                Vector3.one * scale);
            GUI.depth = Depth;
            try
            {
                if ((opened.IsValid || sourcesOpen) && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
                {
                    ClosePanel();
                    sourcesOpen = false;
                    Event.current.Use();
                }
                if (!Live) return;
                float top = (phase4 != null ? phase4.TopInset : BaseOperationsPanel.TopBlockBottom) + 8;
                if (showColumn) Column(new Rect(ColumnX, top, ColumnW, SideBottom - top));
                if (opened.IsValid) Side(new Rect(SideX, top, SideW, SideBottom - top));
                else if (sourcesOpen) Sources(new Rect(SideX, top, SideW, SideBottom - top));
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.enabled = previousEnabled;
                GUI.depth = previousDepth;
            }
        }

        private void Column(Rect area)
        {
            skin.Panel(area);
            var inner = new Rect(area.x + 12, area.y + 10, area.width - 24, area.height - 20);
            GUILayout.BeginArea(inner);
            columnScroll = GUILayout.BeginScrollView(columnScroll);
            GUILayout.Label(LivingBaseText.DataBadge(shown.Data), badge);
            if (shown.Data.Dataset != null)
                GUILayout.Label("Conjunto: " + shown.Data.Dataset.DatasetId + " · SHA-256 " + shown.Data.Dataset.Sha256.Substring(0, 12) + "…", small);
            GUILayout.Space(6);

            GUILayout.Label("CONTRATIEMPOS", heading);
            bool any = false;
            foreach (IncidentRecord i in shown.Incidents)
            {
                if (!i.IsOpen) continue;
                any = true;
                GUILayout.Label(LivingBaseText.Phase(i.Phase) + " · " + LivingBaseText.Kind(i.Kind), text);
                GUILayout.Label(LivingBaseText.Hint(i.Kind), small);
                GUILayout.Space(4);
            }
            if (!any) GUILayout.Label("Ninguno abierto ahora. El director descansa o espera.", small);
            for (int k = shown.Feedback.Count - 1, n = 0; k >= 0 && n < 2; k--)
                if (shown.Feedback[k].Performer == context.Port.Membership.Player)
                {
                    GUILayout.Label(LivingBaseText.Feedback(shown.Feedback[k]), small);
                    n++;
                }

            if (shown.SpaceWeather != null) Weather(shown.SpaceWeather);

            GUILayout.Space(8);
            GUILayout.Label($"CIENCIA · créditos {shown.Credits} (ganados {shown.CreditsEarned})", heading);
            foreach (AssignmentRecord a in shown.Assignments)
            {
                if (!a.IsOpen) continue;
                GUILayout.Label(LivingBaseText.Activity(a.ActivityKey) + $" · +{a.RewardCredits} · {LivingBaseText.Assignment(a.State)}" +
                                $" · plazo {LivingBaseText.Number(Math.Max(0, a.DueAtSeconds - shown.SimulatedTimeSeconds))} s", text);
                GUILayout.BeginHorizontal();
                GUI.enabled = !Pending;
                if (a.State == AssignmentState.Offered || a.State == AssignmentState.Postponed)
                {
                    if (GUILayout.Button("Aceptar", button)) RequestAssignment(IncidentAction.AcceptAssignment, a.Sequence);
                    if (a.State == AssignmentState.Offered && GUILayout.Button("Posponer", button))
                        RequestAssignment(IncidentAction.PostponeAssignment, a.Sequence);
                }
                else if (a.State == AssignmentState.Accepted && GUILayout.Button("Cancelar", button))
                    RequestAssignment(IncidentAction.CancelAssignment, a.Sequence);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            UpgradeStatus u = shown.Upgrade;
            GUILayout.Label(u.Installed ? "Mejora del reciclador: INSTALADA" :
                $"Mejora del reciclador: {u.CreditCost} créditos + 1 filtro (instálala en el reciclador con E)", small);
            GUILayout.Space(6);
            GUILayout.Label(Feedback, small);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void Weather(SpaceWeatherStatus w)
        {
            GUILayout.Space(8);
            GUILayout.Label("CLIMA ESPACIAL", heading);
            GUILayout.Label(w.Label, badge);
            GUILayout.Label(LivingBaseText.Instruction(w.Instruction.State), text);
            if (w.Instruction.ShelterBySeconds.HasValue && w.Instruction.State != Trainer.Application.Expedition.InstructionState.Ended)
            {
                double left = w.Instruction.ShelterBySeconds.Value - shown.SimulatedTimeSeconds;
                GUILayout.Label(left > 0 ? $"Plazo para estar en el refugio: {LivingBaseText.Number(left)} s" : "El plazo para refugiarse ya pasó.", text);
            }
            foreach (MemberProtection m in w.Members)
                if (m.Player == context.Port.Membership.Player)
                    GUILayout.Label("Tú: " + LivingBaseText.Shelter(m.State) + (m.ReceivedInstruction ? "" : " · aún no recibiste la instrucción (radio)"), small);
            for (int k = w.Issued.Count - 1; k >= 0; k--)
            {
                EpisodeMessageView v = w.Issued[k];
                GUILayout.Label($"t={LivingBaseText.Number(v.IssuedAtSeconds)} s · {LivingBaseText.Validity(v.Validity)}", small);
                GUILayout.Label(LivingBaseText.Message(v.Message.TextKey) + " (" + LivingBaseText.Basis(v.Message.Basis) + ")", small);
            }
            foreach (Trainer.Application.Expedition.ProtectionAssessment a in w.Assessments)
                if (a.Player == context.Port.Membership.Player)
                    GUILayout.Label("Resultado del refugio: " + LivingBaseText.Protection(a.Category), text);
            GUILayout.Label(w.TimelineNote, small);
            if (w.VisibleRecords.Count > 0 && GUILayout.Button(sourcesOpen ? "Ocultar fuentes" : $"Ver fuentes ({w.VisibleRecords.Count})", button)) ToggleSources();
        }

        private void Sources(Rect area)
        {
            SpaceWeatherStatus w = shown.SpaceWeather;
            skin.Panel(area);
            GUILayout.BeginArea(new Rect(area.x + 12, area.y + 10, area.width - 24, area.height - 20));
            GUILayout.Label("FUENTES (lo publicado hasta ahora)", heading);
            if (w == null)
            {
                GUILayout.Label("No hay episodio activo.", small);
            }
            else
            {
                if (w.Dataset != null) GUILayout.Label("Licencia/uso: " + w.Dataset.License, small);
                sourcesScroll = GUILayout.BeginScrollView(sourcesScroll);
                foreach (SourceRecord r in w.VisibleRecords)
                {
                    GUILayout.Label(r.Provider + " · " + LivingBaseText.RecordKind(r.Kind) + " · " + r.SourceId, text);
                    GUILayout.Label("Publicado: " + (r.IssuedUtc ?? "desconocido") + " · Hora del evento: " + (r.EventTimeUtc ?? "desconocida"), small);
                    GUILayout.Label("Texto original (inglés): " + r.Summary, small);
                    foreach (SourceMeasurement m in r.Measurements)
                        GUILayout.Label("  " + m.Quantity + ": " + (m.Value.HasValue ? LivingBaseText.Number(m.Value.Value) + " " + m.Unit : "desconocido") +
                                        " · " + m.Location, small);
                    GUILayout.Label(r.Uri, small);
                    GUILayout.Space(6);
                }
                GUILayout.EndScrollView();
            }
            if (GUILayout.Button("Cerrar (Esc)", button)) sourcesOpen = false;
            GUILayout.EndArea();
        }

        private void Side(Rect area)
        {
            skin.Panel(area);
            GUILayout.BeginArea(new Rect(area.x + 12, area.y + 10, area.width - 24, area.height - 20));
            IncidentOperationProgress own = null;
            foreach (IncidentOperationProgress op in shown.Operations)
                if (op.Station == opened) own = op;
            if (opened == context.Scenario.PowerStation) PowerPanel();
            else UpgradePanel();
            if (own != null)
            {
                GUILayout.Label($"Trabajo en curso: {LivingBaseText.Action(own.Action)} · {LivingBaseText.Number(own.WorkSeconds)}/" +
                                $"{LivingBaseText.Number(own.DurationSeconds)} s", text);
                GUI.enabled = !Pending && own.Performer == context.Port.Membership.Player;
                if (GUILayout.Button("Cancelar trabajo", button)) RequestCancelOperation();
                GUI.enabled = true;
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label(Feedback, small);
            if (GUILayout.Button("Cerrar (Esc)", button)) ClosePanel();
            GUILayout.EndArea();
        }

        private void PowerPanel()
        {
            PowerEquipmentStatus p = shown.Power;
            GUILayout.Label("PANEL ELÉCTRICO", heading);
            GUILayout.Label($"Generación disponible: {LivingBaseText.Number(p.EffectiveGenerationWatts)} W", text);
            GUILayout.Label($"Descarga máxima de batería: {LivingBaseText.Number(p.EffectiveMaxDischargeWatts)} W", text);
            if (p.AddedEssentialWatts > 0) GUILayout.Label($"Carga extra por la mejora: {LivingBaseText.Number(p.AddedEssentialWatts)} W", small);
            GUILayout.Label("Estado: " + LivingBaseText.Knowledge(p.Knowledge), text);
            if (p.Observation != null)
                GUILayout.Label((p.Observation.Current ? "Inspección vigente" : "Inspección caducada") + ": " + LivingBaseText.Findings(p.Observation.Findings), small);
            if (p.Isolated) GUILayout.Label(p.PartReplaced ? "Aislado · relé cambiado: falta restituir." : "Aislado: cambia el relé.", small);
            GUILayout.Space(6);
            GUI.enabled = !Pending;
            if (GUILayout.Button("Inspeccionar", button)) RequestPower(IncidentAction.InspectPower);
            bool current = p.Observation != null && p.Observation.Current;
            GUI.enabled = !Pending && current;
            if (GUILayout.Button("Reiniciar convertidor", button)) RequestPower(IncidentAction.ResetConverter);
            if (GUILayout.Button("Aislar controlador", button)) RequestPower(IncidentAction.IsolatePower);
            GUI.enabled = !Pending && p.Isolated;
            if (GUILayout.Button("Cambiar relé (gasta un repuesto)", button)) RequestPower(IncidentAction.ReplacePowerPart);
            if (GUILayout.Button("Restituir", button)) RequestPower(IncidentAction.RestorePower);
            GUI.enabled = true;
            GUILayout.Label("Reiniciar e inspeccionar no gastan repuestos. Aislar corta la batería hasta restituir.", small);
        }

        private void UpgradePanel()
        {
            UpgradeStatus u = shown.Upgrade;
            GUILayout.Label("MEJORA DEL RECICLADOR", heading);
            GUILayout.Label($"Recupera el {LivingBaseText.Number(u.RecoveryFraction * 100)} % del agua tratada (antes menos).", text);
            GUILayout.Label($"Coste: {u.CreditCost} créditos de ciencia + 1 repuesto ({u.SpareKey}).", text);
            GUILayout.Label($"A cambio, consume {LivingBaseText.Number(u.AddedEssentialWatts)} W más siempre.", text);
            GUILayout.Label($"Tenéis {shown.Credits} créditos.", small);
            GUI.enabled = !Pending && !u.Installed;
            if (GUILayout.Button(u.Installed ? "Ya instalada" : "Instalar mejora", button)) RequestUpgrade();
            GUI.enabled = true;
        }

        private void Styles()
        {
            if (text != null) return;
            text = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, normal = { textColor = HudSkin.Title } };
            small = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, normal = { textColor = HudSkin.Label } };
            heading = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = HudSkin.Cyan } };
            badge = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = true, normal = { textColor = HudSkin.Amber } };
            button = new GUIStyle(GUI.skin.button) { fontSize = 15, fixedHeight = 32 };
        }
    }
}
