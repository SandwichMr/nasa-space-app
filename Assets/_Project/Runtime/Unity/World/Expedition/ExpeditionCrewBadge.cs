using Trainer.Application.Expedition;
using Trainer.Simulation.Eva;
using UnityEngine;
namespace Trainer.Unity.World.Expedition
{
    public sealed class ExpeditionCrewBadge : MonoBehaviour, IExpeditionCrewView
    {
        [SerializeField] private TextMesh label;
        public void PresentExpedition(ExpeditionCrewFrame f)
        {
            if (label == null) return;
            string margin = f.Margin.Status == MarginStatus.NotApplicable ? ""
                : f.Margin.Status == MarginStatus.ReturnUnavailable ? "\nREGRESO NO DISPONIBLE"
                : $"\n{f.Margin.MarginSeconds:0} s · {Service(f.Margin.LimitingService)}";
            label.text = (f.Zone == CrewZone.Exterior ? "FUERA" : Zone(f.Zone)) + (f.OnSuitSupply ? "\nSELLADO" : "") + (f.IsLocal ? margin : "")
                + (f.Protection == ProtectionState.Protected ? "\nPROTEGIDO" : "")
                + (f.WorkingAt.IsValid ? "\nTRABAJANDO" : "");
            label.color = f.Margin.Status == MarginStatus.Insufficient || f.Margin.Status == MarginStatus.ReturnUnavailable
                ? new Color(1,.72f,.45f) : new Color(.75f,.94f,1);
        }
        private static string Service(SuitService s) => s == SuitService.Oxygen ? "O2" : s == SuitService.Battery ? "batería" : "agua";
        public static string Zone(CrewZone z) => z == CrewZone.Exterior ? "EXTERIOR" : z == CrewZone.Airlock ? "ESCLUSA" : z == CrewZone.Shelter ? "REFUGIO" : "BASE";
    }
}
