using System.Text;
using Trainer.Application;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.Eva;
using Trainer.Unity.World.Expedition;
using UnityEngine;
namespace Trainer.Unity.Presentation.Expedition
{
    public sealed class ExpeditionPanel : ExpeditionBoundView
    {
        [SerializeField] private Texture2D warning;
        private bool details;
        private string answer;
        private GUIStyle text, title, box, button;
        public string AdvisoryText { get; private set; } = "Sin información recibida";
        protected override void OnBound() { Context.Intents.Answered += Answered; }
        protected override void OnUnbinding() { Context.Intents.Answered -= Answered; }
        protected override void OnRestart() { details=false; answer=null; }
        private void Answered(string reason) { answer=reason; }
        protected override void Present(ExpeditionSessionSnapshot s)
        {
            // Only messages received by THIS player are presented. A global emission is not a radio reception.
            IssuedAdvisory latest=null; AdvisoryReception reception=null;
            foreach (var a in s.Advisories)
                if (s.TryGetReception(a.Message.Id,Context.Port.Membership.Player,out var r)) { latest=a; reception=r; }
            AdvisoryText = latest == null ? "Sin información recibida · no significa seguro"
                : Message(latest.Message.TextKey)+(latest.Message.ShelterBySeconds.HasValue?$" · entra antes de t={latest.Message.ShelterBySeconds:0} s":"")+$"\nRecibido t={reception.ReceivedAtSeconds:0} s · emitido {latest.Message.IssuedAtSeconds:0} s"
                +$" · hasta {latest.Message.ValidUntilSeconds:0} s · "+Validity(latest.Validity)
                +(latest.Validity==AdvisoryValidity.Expired && latest.Message.Kind!=AdvisoryKind.InstructionEnd ? "\nSin actualización: mantén la instrucción de refugio." : "");
        }
        protected override void ClearView() { details=false; answer=null; AdvisoryText="Sin datos vigentes · desconectado"; }
        private void ToggleDetails()
        {
            details=!details;
            if (details) Context.BlockMovement(this); else Context.UnblockMovement(this);
        }
        private void OnGUI()
        {
            Styles(); var oldMatrix=GUI.matrix; var oldEnabled=GUI.enabled;
            float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);
            GUI.matrix=Matrix4x4.TRS(new Vector3((Screen.width-1280*scale)/2,(Screen.height-720*scale)/2,0),Quaternion.identity,Vector3.one*scale);
            try
            {
                if (!Live) { GUI.Box(new Rect(12,12,1256,58),"SIN DATOS VIGENTES · conecta para recibir el estado de la misión",box);return; }
                var s=Shown; s.TryGetCrew(Context.Port.Membership.Player,out var mine);
                GUI.Box(new Rect(12,12,1256,68),"",box);
                GUI.Label(new Rect(24,20,370,24),"EXPEDICIÓN · "+Phase(s.Phase)+$" · {s.SimulatedTimeSeconds:0} s",title);
                GUI.Label(new Rect(24,46,370,24),$"Base: {s.Outpost.Resources.Power.EnergyJoules:0} J · agua {s.Outpost.Resources.LifeSupport.Water.PotableKilograms:0.0} kg",text);
                if (warning!=null) GUI.DrawTexture(new Rect(398,25,32,32),warning,ScaleMode.ScaleToFit,true);
                GUI.Label(new Rect(440,19,620,58),AdvisoryText,text);
                if (GUI.Button(new Rect(1080,27,172,36),details ? "Cerrar detalles" : "Misión / equipo",button)) ToggleDetails();
                GUI.Box(new Rect(12,620,1256,88),"",box);
                GUI.Label(new Rect(24,628,720,72),mine==null ? "Esperando tu estado" : ExpeditionCrewBadge.Zone(mine.Zone)+" · "+Margin(mine.Margin,s.SimulatedTimeSeconds)
                    +"\n"+Protection(mine.Protection)+" · "+ShelterDirection(s)
                    +"\nWASD / flechas: caminar · E: actuar / cancelar · R: regreso",text);
                GUI.enabled=!Context.Intents.HasPending && s.Outpost.State==SessionState.Running && !details;
                if (GUI.Button(new Rect(810,636,205,36),"Interactuar / cancelar [E]",button)) Context.Intents.Interact();
                if (GUI.Button(new Rect(1030,636,222,36),"Declarar regreso [R]",button)) Context.Intents.RequestDeclareReturn();
                GUI.enabled=oldEnabled;
                GUI.Label(new Rect(810,677,440,24),Context.Intents.HasPending?"Esperando confirmación…":answer==null?"Acciones en la estación cercana":"Respuesta: "+Reason(answer),text);
                if (details || s.Phase==ExpeditionPhase.Closed)
                {
                    GUI.Box(new Rect(280,100,720,500),"",box);
                    GUI.Label(new Rect(300,114,680,34),s.Phase==ExpeditionPhase.Closed?"RESULTADO · "+Outcome(s.Outcome):"MISIÓN / COMPAÑEROS",title);
                    GUI.Label(new Rect(300,156,680,424),Details(s),text);
                }
            }
            finally { GUI.matrix=oldMatrix; GUI.enabled=oldEnabled; }
        }
        private string Details(ExpeditionSessionSnapshot s)
        {
            var b=new StringBuilder("Escenario educativo sintético · sin dosimetría humana\n");
            foreach(var o in s.Objectives) b.AppendLine((o.Met?"HECHO · ":"PENDIENTE · ")+Objective(o.Key)+(o.Role==ObjectiveRole.Optional?" (opcional)":""));
            b.AppendLine($"\nEsclusa: {s.Airlock.Occupants.Count}/{Context.Scenario.Airlock.Capacity} plazas · "+(s.Airlock.IsCycling?$"transición {s.Airlock.CycleFraction:P0}":"disponible")+(s.Airlock.Stalled?" · SIN SERVICIO":""));
            foreach(var c in s.Crew) b.AppendLine(c.Player+" · "+ExpeditionCrewBadge.Zone(c.Zone)+" · "+Margin(c.Margin,s.SimulatedTimeSeconds)+" · "+Protection(c.Protection));
            s.TryGetCrew(Context.Port.Membership.Player,out var mine);
            if(mine!=null && mine.SuitIndex>=0)
            {
                var suit=s.Suits[mine.SuitIndex];b.AppendLine("\nTraje: "+suit.Status+" · checklist: "+suit.Completed);
                foreach(var model in Context.Scenario.Eva.Model.Services) b.AppendLine(Service(model.Service)+$": {suit.Reading.Levels.Get(model.Service):0.###}/{model.Capacity:0.###} {model.Unit} · reserva {model.Reserve:0.###}");
            }
            b.AppendLine("\n"+AdvisoryText);
            foreach(var a in s.Assessments) b.AppendLine(a.Player+": "+Category(a.Category)+(a.FirstProtectedAtSeconds.HasValue?$" · protegido t={a.FirstProtectedAtSeconds:0} s":""));
            foreach(var i in s.Incidents) b.AppendLine("Incidente: "+i.Player+" · "+(i.Kind==IncidentKind.DisconnectedWhileSealed?"desconexión con traje sellado":i.Kind==IncidentKind.ReserveUsed?"se utilizó una reserva":"aviso no recibido"));
            return b.ToString();
        }
        private string ShelterDirection(ExpeditionSessionSnapshot s)
        {
            if (!s.Outpost.TryGetCrew(Context.Port.Membership.Player,out var me)) return "Refugio: dirección desconocida";
            foreach(var z in Context.Scenario.Zones) if(z.Zone==CrewZone.Shelter)
            {
                double dx=(z.Area.MinX+z.Area.MaxX)/2-me.Pose.Position.X,dy=(z.Area.MinY+z.Area.MaxY)/2-me.Pose.Position.Y;
                return "Refugio: "+(dx>1?"derecha ":dx < -1?"izquierda ":"")+(dy>1?"arriba":dy < -1?"abajo":"")+" (dirección, sigue los pasillos)";
            }
            return "Refugio: desconocido";
        }
        public static string Margin(ReturnMarginEstimate m,double? now=null) => m==null?"regreso: desconocido":m.Status==MarginStatus.NotApplicable?"margen: no aplica":m.Status==MarginStatus.ReturnUnavailable?"REGRESO NO DISPONIBLE":$"margen {m.MarginSeconds:0} s · limita {Service(m.LimitingService)} · calculado t={m.ComputedAtSeconds:0} s"+(now.HasValue && m.ComputedAtSeconds < now.Value-0.000001 ? " · DATO ANTERIOR" : " · ACTUAL")+(m.Status==MarginStatus.Insufficient?" · INSUFICIENTE":"");
        public static string Protection(ProtectionState p) => p==ProtectionState.Protected?"PROTECCIÓN CONFIRMADA":p==ProtectionState.NotRequired?"sin instrucción de protección":p==ProtectionState.NotInShelter?"entra al REFUGIO":p==ProtectionState.ShelterNotPrepared?"prepara el REFUGIO":"REFUGIO SIN SERVICIO";
        private static string Phase(ExpeditionPhase p) => p==ExpeditionPhase.Preparing?"preparación":p==ExpeditionPhase.Underway?"en curso":p==ExpeditionPhase.Returning?"regresando":"cerrada";
        private static string Message(string key) => key=="particle-warning"?"AVISO: entrad al refugio (ejercicio sintético)":key=="particle-update"?"ACTUALIZACIÓN: continúa la instrucción de refugio":key=="shelter-period-end"?"Control: fin del periodo de refugio del ejercicio":key;
        private static string Validity(AdvisoryValidity v) => v==AdvisoryValidity.Current?"VIGENTE":v==AdvisoryValidity.Superseded?"SUSTITUIDO":"VENCIDO";
        private static string Reason(string r) => r==OutpostReasons.Accepted?"aceptado":r==OutpostReasons.OutOfRange?"acércate a la estación":r==OutpostReasons.StationOccupied?"estación ocupada":r==OutpostReasons.InsufficientResources?"recursos insuficientes":r==OutpostReasons.PlayerBusy?"ya tienes una tarea":r==ExpeditionReasons.AirlockBusy?"esclusa en transición":r==ExpeditionReasons.SuitNotReady?"completa la preparación del traje":"No se pudo realizar: revisa tu preparación, la estación y el estado de la misión";
        private static string Outcome(ExpeditionOutcome o) => o==ExpeditionOutcome.ReturnedWithoutScience?"Regreso seguro sin ciencia":o==ExpeditionOutcome.ScienceReturned?"Ciencia y regreso cumplidos":o==ExpeditionOutcome.ShelteredInTime?"Refugio a tiempo":o==ExpeditionOutcome.ShelteredLate?"Protección confirmada tarde":o==ExpeditionOutcome.SuitServiceDepleted?"Un servicio del traje llegó a cero; ejercicio detenido":o==ExpeditionOutcome.TimeExpired?"Se acabó el tiempo":o==ExpeditionOutcome.ProtectionIncomplete?"Faltó completar la protección":o==ExpeditionOutcome.AdvisoryNotReceived?"El aviso no se recibió a tiempo":"Ejercicio interrumpido";
        private static string Service(SuitService s) => s==SuitService.Oxygen?"O2":s==SuitService.Battery?"batería":"agua de refrigeración";
        private static string Objective(string key) => key=="crew-safe"?"Todos de regreso":key=="regolith-sample"?"Muestra de regolito":key=="crater-core"?"Núcleo del cráter":"Objetivo de misión";
        private static string Steps(ChecklistSteps c) => ((c & ChecklistSteps.Don)!=0?"puesto; ":"falta ponerse; ")+((c & ChecklistSteps.ChargeBattery)!=0?"cargado; ":"falta batería; ")+((c & ChecklistSteps.FillConsumables)!=0?"O2 y agua listos; ":"faltan O2 y agua; ")+((c & ChecklistSteps.SealCheck)!=0?"sellos revisados":"falta revisar sellos");
        private static string Category(ProtectionCategory c) => c==ProtectionCategory.InTime?"protegido a tiempo":c==ProtectionCategory.Late?"protegido tarde":c==ProtectionCategory.Interrupted?"salió antes de tiempo":c==ProtectionCategory.NotProtected?"no se protegió":"no recibió el aviso";
        private void Styles()
        {
            if(text!=null)return;
            text=new GUIStyle(GUI.skin.label){fontSize=13,wordWrap=true};text.normal.textColor=new Color(.85f,.94f,1);
            title=new GUIStyle(text){fontSize=16,fontStyle=FontStyle.Bold};
            box=new GUIStyle(GUI.skin.box){alignment=TextAnchor.MiddleLeft,fontSize=15};box.normal.textColor=Color.white;
            button=new GUIStyle(GUI.skin.button){fontSize=13};
        }
    }
}
