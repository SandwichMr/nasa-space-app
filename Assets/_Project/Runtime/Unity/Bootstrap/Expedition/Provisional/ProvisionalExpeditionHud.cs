using System.Linq;
using System.Text;
using Trainer.Application;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.Eva;
using Trainer.Unity.World.Outpost;
using UnityEngine;

namespace Trainer.Unity.Bootstrap.Provisional
{
    /// <summary>
    /// PROVISIONAL (D2 integration, not final art): a plain IMGUI HUD so the phase 3 scene is playable and testable until
    /// D4's presentation arrives through the same <see cref="IExpeditionView"/> mount. It only READS the confirmed
    /// snapshot and calls the injected intents (no movement, clock, inventory or evaluation). It also forwards each
    /// snapshot to the phase 2 base view for station states. Absence of information is shown as such, never as "safe".
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProvisionalExpeditionHud : MonoBehaviour, IExpeditionView
    {
        public const string Label = "PROVISIONAL · HUD de integración (D2) · se sustituye por la presentación de D4";

        [SerializeField] private BaseInteriorView baseView;

        private ExpeditionViewContext context;
        private ExpeditionSessionSnapshot snapshot;
        private string lastAnswer;
        private GUIStyle small, heading, box, button;

        public ExpeditionSessionSnapshot Shown => snapshot;

        public void Bind(ExpeditionViewContext value)
        {
            Unbind();
            context = value;
            context.Port.ExpeditionChanged += OnExpedition;
            context.Intents.Answered += OnAnswered;
            if (context.Port.TryGetExpedition(out ExpeditionSessionSnapshot current)) OnExpedition(current);
        }

        public void Unbind()
        {
            if (context == null) return;
            context.Port.ExpeditionChanged -= OnExpedition;
            context.Intents.Answered -= OnAnswered;
            context = null;
            snapshot = null;
            if (baseView != null) baseView.ClearReadings();
        }

        private void OnDestroy() => Unbind();

        private void OnExpedition(ExpeditionSessionSnapshot next)
        {
            snapshot = next;
            if (baseView != null && context != null) baseView.Present(next.Outpost, context.Port.Membership.Player);
        }

        private void OnAnswered(string reason) => lastAnswer = reason;

        private bool Live => context != null && snapshot != null && context.Port.Membership.IsJoined;

        private void OnGUI()
        {
            EnsureStyles();
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1280 * scale) / 2, (Screen.height - 720 * scale) / 2, 0),
                Quaternion.identity, new Vector3(scale, scale, 1));
            GUI.Label(new Rect(8, 702, 900, 18), Label, small);
            if (!Live)
            {
                GUI.Box(new Rect(8, 8, 1264, 88), "Sin datos vigentes: conéctate o espera el estado de la autoridad.", box);
                return;
            }
            ExpeditionSessionSnapshot s = snapshot;
            PlayerId me = context.Port.Membership.Player;
            s.TryGetCrew(me, out ExpeditionCrewState mine);

            // Top band: mission, airlock, information.
            GUI.Box(new Rect(8, 8, 410, 88), "", box);
            GUI.Label(new Rect(16, 12, 400, 20), $"Misión · t = {s.SimulatedTimeSeconds:0} s · {State(s)}", heading);
            GUI.Label(new Rect(16, 34, 400, 60), $"Fase: {Phase(s.Phase)}\nEnergía base: {s.Outpost.Resources.Power.EnergyJoules / 1000:0.0} kJ · " +
                $"agua potable {s.Outpost.Resources.LifeSupport.Water.PotableKilograms:0.0} kg\n{Objectives(s)}", small);
            GUI.Box(new Rect(424, 8, 300, 88), "", box);
            GUI.Label(new Rect(432, 12, 290, 20), "Esclusa: " + Airlock(s.Airlock), heading);
            GUI.Label(new Rect(432, 34, 290, 60), $"Ocupantes {s.Airlock.Occupants.Count}/{context.Scenario.Airlock.Capacity} · " +
                $"ciclos {s.Airlock.CycleSequence}" + (s.Airlock.IsCycling ? $"\nProgreso {s.Airlock.CycleFraction:P0}" + (s.Airlock.Stalled ? " · SIN ENERGÍA" : "") : "") +
                $"\nGas de reposición {s.Supply.MakeupGasKilograms:0.00} kg · recarga O2 {s.Supply.OxygenRechargeKilograms:0.00} kg", small);
            GUI.Box(new Rect(730, 8, 542, 88), "", box);
            GUI.Label(new Rect(738, 12, 530, 20), "Clima espacial: " + Instruction(s.Instruction), heading);
            GUI.Label(new Rect(738, 34, 530, 60), Advisory(s, me), small);

            // Bottom band: my suit, margin, protection and keys.
            GUI.Box(new Rect(8, 624, 1264, 76), "", box);
            GUI.Label(new Rect(16, 628, 620, 70), Suit(s, mine), small);
            GUI.Label(new Rect(640, 628, 420, 70), Help(s, mine), small);
            if (mine != null && s.Phase == ExpeditionPhase.Underway && s.Outpost.State == SessionState.Running
                && GUI.Button(new Rect(1068, 640, 196, 40), "Declarar regreso (R)", button))
                context.Intents.RequestDeclareReturn();

            if (s.Phase == ExpeditionPhase.Closed) GUI.Box(new Rect(240, 150, 800, 400), Result(s), box);
        }

        private static string State(ExpeditionSessionSnapshot s) =>
            s.Outpost.State == SessionState.Ready ? "preparada (pulsa Iniciar)" : s.Outpost.State == SessionState.Running ? "en curso"
            : s.Outpost.State == SessionState.Paused ? "en pausa" : s.Outpost.State == SessionState.Completed ? "terminada" : "interrumpida";

        private static string Phase(ExpeditionPhase p) =>
            p == ExpeditionPhase.Preparing ? "preparación" : p == ExpeditionPhase.Underway ? "salida en curso"
            : p == ExpeditionPhase.Returning ? "regreso declarado" : "cerrada";

        private static string Objectives(ExpeditionSessionSnapshot s) =>
            string.Join(" · ", s.Objectives.Select(o => (o.Met ? "✔ " : "○ ") + ObjectiveText(o.Key) + (o.Role == ObjectiveRole.Optional ? " (opcional)" : "")));

        public static string ObjectiveText(string key) =>
            key == "crew-safe" ? "todos de vuelta" : key == "regolith-sample" ? "muestra de regolito" : key == "crater-core" ? "núcleo del cráter" : key;

        private static string Airlock(AirlockStatus a) =>
            a.State == AirlockState.PressurizedInnerOpen ? "presurizada · puerta interior abierta"
            : a.State == AirlockState.Depressurizing ? "despresurizando · puertas cerradas"
            : a.State == AirlockState.DepressurizedOuterOpen ? "sin aire · puerta exterior abierta" : "represurizando · puertas cerradas";

        private static string Instruction(ShelterInstructionStatus i) =>
            i.State == InstructionState.None ? "sin información recibida (no significa \"seguro\")"
            : i.State == InstructionState.InForce ? $"REFUGIARSE antes de t = {i.ShelterBySeconds:0} s"
            : i.State == InstructionState.InForceWithoutCurrentInformation ? "aviso VENCIDO sin actualización: sigue vigente, sin datos actuales"
            : "fin del periodo de refugio indicado por control (no es un \"todo despejado\" universal)";

        private static string Advisory(ExpeditionSessionSnapshot s, PlayerId me)
        {
            if (s.Advisories.Count == 0) return "Ningún mensaje emitido.";
            IssuedAdvisory last = s.Advisories[s.Advisories.Count - 1];
            var sb = new StringBuilder();
            sb.Append($"{MessageText(last.Message.TextKey)}\nEmitido t = {last.Message.IssuedAtSeconds:0} s · vigencia hasta {last.Message.ValidUntilSeconds:0} s ({Validity(last.Validity)})");
            sb.Append(s.TryGetReception(last.Message.Id, me, out AdvisoryReception r)
                ? $"\nRecibido por ti a t = {r.ReceivedAtSeconds:0} s" + (r.AfterShelterBy ? " (tarde: después del plazo)" : "")
                : "\nNo lo has recibido (fuera de cobertura de radio)");
            return sb.ToString();
        }

        public static string MessageText(string key) =>
            key == "particle-warning" ? "Control (SINTÉTICO): posible aumento de partículas energéticas. Entrad al refugio."
            : key == "particle-update" ? "Control (SINTÉTICO): el aviso continúa; mismo plazo, nueva vigencia."
            : key == "shelter-period-end" ? "Control (SINTÉTICO): termina el periodo de refugio de este ejercicio." : key;

        private static string Validity(AdvisoryValidity v) =>
            v == AdvisoryValidity.Current ? "vigente" : v == AdvisoryValidity.Superseded ? "sustituido" : "vencido: sin información actual";

        private string Suit(ExpeditionSessionSnapshot s, ExpeditionCrewState mine)
        {
            if (mine == null) return "";
            var sb = new StringBuilder($"Tú: {Zone(mine.Zone)}");
            if (mine.SuitIndex < 0) sb.Append(" · sin traje");
            else
            {
                SuitState suit = s.Suits[mine.SuitIndex];
                SuitModelConfiguration model = context.Scenario.Eva.Model;
                sb.Append($" · traje {suit.Key} {SuitStatusText(suit.Status)} · checklist {Steps(suit.Completed)}\n");
                foreach (SuitServiceModel service in model.Services)
                {
                    double level = suit.Reading.Levels.Get(service.Service);
                    sb.Append($"{ServiceText(service.Service)} {level:0.###}/{service.Capacity:0.###} {service.Unit} (reserva {service.Reserve:0.###})   ");
                }
            }
            sb.Append('\n');
            ReturnMarginEstimate m = mine.Margin;
            sb.Append(m.Status == MarginStatus.NotApplicable ? "Margen de regreso: no aplica (dentro de la base)"
                : m.Status == MarginStatus.ReturnUnavailable ? "Margen de regreso: NO DISPONIBLE (la esclusa no puede completar el ingreso)"
                : $"Margen de regreso: {m.MarginSeconds:0} s · limita {ServiceText(m.LimitingService)} · ruta {m.Plan.PathMeters:0} m" +
                  (m.Status == MarginStatus.Insufficient ? " · INSUFICIENTE: ya se usa la reserva" : ""));
            if (s.TryGetTeamWorstMargin(out PlayerId worst, out ReturnMarginEstimate team))
                sb.Append($" · peor del equipo: {team.MarginSeconds:0} s ({worst})");
            if (mine.Protection != ProtectionState.NotRequired) sb.Append(" · " + ProtectionText(mine.Protection));
            return sb.ToString();
        }

        public static string ServiceText(SuitService s) => s == SuitService.Oxygen ? "O2" : s == SuitService.Battery ? "batería" : "agua refrig.";

        public static string Zone(CrewZone z) =>
            z == CrewZone.Interior ? "en la base" : z == CrewZone.Shelter ? "en el refugio" : z == CrewZone.Airlock ? "en la esclusa" : "fuera (superficie)";

        private static string SuitStatusText(SuitStatus s) =>
            s == SuitStatus.Worn ? "puesto" : s == SuitStatus.Sealed ? "SELLADO (suministro propio)" : s == SuitStatus.Stowed ? "en soporte" : "abandonado";

        private static string Steps(ChecklistSteps c) =>
            ((c & ChecklistSteps.Don) != 0 ? "✔puesto " : "○puesto ") + ((c & ChecklistSteps.ChargeBattery) != 0 ? "✔batería " : "○batería ")
            + ((c & ChecklistSteps.FillConsumables) != 0 ? "✔O2+agua " : "○O2+agua ") + ((c & ChecklistSteps.SealCheck) != 0 ? "✔sellos" : "○sellos");

        public static string ProtectionText(ProtectionState p) =>
            p == ProtectionState.Protected ? "PROTEGIDO en el refugio" : p == ProtectionState.NotInShelter ? "NO estás en el refugio"
            : p == ProtectionState.ShelterNotPrepared ? "el refugio no está preparado (usa su panel)"
            : p == ProtectionState.ShelterServiceUnavailable ? "el refugio no tiene servicio (energía)" : "";

        private string Help(ExpeditionSessionSnapshot s, ExpeditionCrewState mine)
        {
            string answer = lastAnswer == null ? "" : "Última respuesta: " + ReasonText(lastAnswer);
            return "WASD/flechas: moverse · E: interactuar (tarea, panel de esclusa) · R: declarar regreso\n" +
                   (context.Intents.HasPending ? "Esperando la respuesta de la autoridad…" : answer);
        }

        public static string ReasonText(string reason)
        {
            switch (reason)
            {
                case OutpostReasons.Accepted: return "aceptado";
                case OutpostReasons.OutOfRange: return "demasiado lejos";
                case OutpostReasons.StationOccupied: return "la estación está ocupada";
                case OutpostReasons.PlayerBusy: return "ya tienes una tarea";
                case OutpostReasons.InvalidState: return "la misión no está en curso";
                case OutpostReasons.InsufficientResources: return "no hay recursos suficientes";
                case ExpeditionReasons.WrongZone: return "esa tarea se hace en otra zona";
                case ExpeditionReasons.SuitRequired: return "necesitas un traje puesto";
                case ExpeditionReasons.AlreadySuited: return "ya llevas un traje";
                case ExpeditionReasons.SuitNotOnRack: return "ese traje no está en su soporte";
                case ExpeditionReasons.ChecklistOutOfOrder: return "falta un paso anterior del checklist";
                case ExpeditionReasons.ChecklistStepDone: return "ese paso ya está hecho";
                case ExpeditionReasons.SealedNotAllowed: return "con el traje sellado no se puede";
                case ExpeditionReasons.AirlockBusy: return "la esclusa está ciclando";
                case ExpeditionReasons.AirlockObstructed: return "alguien bloquea una puerta de la esclusa";
                case ExpeditionReasons.AirlockOverCapacity: return "demasiadas personas en la esclusa";
                case ExpeditionReasons.SuitNotReady: return "alguien en la esclusa no tiene el traje listo";
                case ExpeditionReasons.EgressBlockedByInstruction: return "hay un aviso vigente: nadie sale";
                case ExpeditionReasons.ReturnDeclared: return "ya se declaró el regreso";
                case ExpeditionReasons.StaleZone: return "cambiaste de zona: vuelve a intentarlo";
                case ExpeditionReasons.StaleAirlockState: return "la esclusa cambió: vuelve a intentarlo";
                case ExpeditionReasons.NoExpeditionUnderway: return "nadie está fuera";
                case ExpeditionReasons.ReturnAlreadyDeclared: return "el regreso ya estaba declarado";
                default: return reason;
            }
        }

        private static string Result(ExpeditionSessionSnapshot s)
        {
            var sb = new StringBuilder("RESULTADO\n\n" + OutcomeText(s.Outcome) + "\n\n");
            foreach (ExpeditionObjective o in s.Objectives) sb.Append((o.Met ? "✔ " : "○ ") + ObjectiveText(o.Key) + "\n");
            foreach (ProtectionAssessment a in s.Assessments)
                sb.Append($"{a.Player}: {Category(a.Category)}" + (a.ReceivedAtSeconds.HasValue ? $" · recibió el aviso a {a.ReceivedAtSeconds:0} s" : "")
                    + (a.FirstProtectedAtSeconds.HasValue ? $" · protegido a {a.FirstProtectedAtSeconds:0} s" : "") + "\n");
            foreach (ExpeditionIncident i in s.Incidents) sb.Append($"Incidente: {IncidentText(i)}\n");
            if (s.Decision != null) sb.Append($"Regreso declarado por {s.Decision.DeclaredBy} a {s.Decision.AtSeconds:0} s con {s.Decision.AdvisoriesKnown.Count} aviso(s) recibido(s)\n");
            if (s.Disclosure != null)
                sb.Append($"\nLo que ocurrió (episodio {(s.Disclosure.Synthetic ? "SINTÉTICO" : "histórico")}): evento de {s.Disclosure.EventOnsetSeconds:0} a {s.Disclosure.EventEndSeconds:0} s. " +
                          "La información llegó después del inicio del evento; se os evalúa solo con lo que recibisteis.");
            sb.Append("\n\nPulsa Reiniciar para volver a intentarlo.");
            return sb.ToString();
        }

        public static string OutcomeText(ExpeditionOutcome o)
        {
            switch (o)
            {
                case ExpeditionOutcome.ScienceReturned: return "Misión cumplida: volvisteis todos con la ciencia.";
                case ExpeditionOutcome.ReturnedWithoutScience: return "Regreso seguro sin ciencia: cancelar a tiempo fue una decisión correcta.";
                case ExpeditionOutcome.ShelteredInTime: return "Refugio a tiempo: todos los que recibieron el aviso se protegieron antes del plazo.";
                case ExpeditionOutcome.ShelteredLate: return "Refugio tarde: todos se protegieron, alguno después del plazo (¿aviso tardío?).";
                case ExpeditionOutcome.ProtectionIncomplete: return "Protección incompleta: alguien que recibió el aviso no estuvo protegido.";
                case ExpeditionOutcome.AdvisoryNotReceived: return "Nadie recibió el aviso a tiempo para actuar (cobertura de radio).";
                case ExpeditionOutcome.TimeExpired: return "Se acabó el tiempo de la misión.";
                case ExpeditionOutcome.SuitServiceDepleted: return "Un servicio del traje llegó a cero fuera: la simulación se detiene (la reserva existe para evitarlo).";
                case ExpeditionOutcome.EssentialServiceLost: return "La base perdió su servicio esencial de energía.";
                case ExpeditionOutcome.SimulationFault: return "Fallo de simulación.";
                default: return "En curso.";
            }
        }

        private static string Category(ProtectionCategory c) =>
            c == ProtectionCategory.InTime ? "protegido a tiempo" : c == ProtectionCategory.Late ? "protegido tarde"
            : c == ProtectionCategory.Interrupted ? "salió del refugio antes del fin" : c == ProtectionCategory.NotProtected ? "no se protegió"
            : "no recibió el aviso";

        private static string IncidentText(ExpeditionIncident i) =>
            i.Kind == IncidentKind.DisconnectedWhileSealed ? $"{i.Player} se desconectó fuera; el traje {i.SuitKey} quedó abandonado"
            : i.Kind == IncidentKind.ReserveUsed ? $"{i.Player} usó la reserva de {ServiceText(i.Service.Value)} a {i.AtSeconds:0} s"
            : $"{i.Player} no recibió el aviso";

        private void EnsureStyles()
        {
            if (small != null) return;
            small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, richText = false };
            small.normal.textColor = new Color(.88f, .92f, .96f);
            heading = new GUIStyle(small) { fontSize = 14, fontStyle = FontStyle.Bold };
            box = new GUIStyle(GUI.skin.box) { fontSize = 13, wordWrap = true, alignment = TextAnchor.UpperLeft, padding = new RectOffset(12, 12, 10, 10) };
            box.normal.textColor = Color.white;
            button = new GUIStyle(GUI.skin.button) { fontSize = 13 };
        }
    }
}
