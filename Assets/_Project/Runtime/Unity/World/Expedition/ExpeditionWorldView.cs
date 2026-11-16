using System.Collections.Generic;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Unity.Presentation.Expedition;
using Trainer.Unity.World.Outpost;
using UnityEngine;
using Trainer.Unity.Configuration;
namespace Trainer.Unity.World.Expedition
{
    public sealed class ExpeditionWorldView : ExpeditionBoundView
    {
        [SerializeField] private Sprite lunarSurface;
        [SerializeField] private Sprite regolith, rock, route, hatchOpen, hatchClosed, hatchCycle, shelter, antenna;
        [SerializeField] private Material material;
        [SerializeField] private OutpostStationView sciencePrefab, optionalPrefab;
        [SerializeField] private BaseInteriorView baseView;
        private Transform generated;
        private readonly List<GameObject> replacedStations = new List<GameObject>();
        private readonly Dictionary<OutpostStationView, (string text, float size)> captions = new Dictionary<OutpostStationView, (string, float)>();
        private SpriteRenderer inner, outer;
        private TextMesh airlockLabel, shelterLabel;
        private readonly Dictionary<StationId,TextMesh> endings = new Dictionary<StationId,TextMesh>();
        private readonly List<OutpostStationView> stations = new List<OutpostStationView>();
        protected override void OnMembershipRestored() => OnBound();
        protected override void OnBound()
        {
            generated = new GameObject("D4 geometry from authoritative scenario").transform;
            generated.SetParent(transform, false);
            var s = Context.Scenario;
            if (baseView != null)
                foreach (var station in baseView.Stations)
                {
                    if ((station.Placement.Id == s.Airlock.InnerPanel || station.Placement.Id == s.Airlock.OuterPanel || station.Placement.Id.Value == "regolith-site" || station.Placement.Id.Value == "crater-rim") && station.gameObject.activeSelf)
                    { replacedStations.Add(station.gameObject); station.gameObject.SetActive(false); }
                    string caption = PreparationCaption(station.Placement.Id.Value);
                    if (caption != null)
                    {
                        captions[station] = (station.DisplayName, station.CaptionSize);
                        station.SetCaption(caption, .05f);
                    }
                }
            if (lunarSurface != null)
            {
                var surface = new GameObject("World anchored lunar surface");
                surface.transform.SetParent(generated,false);
                surface.AddComponent<LunarSurfaceView>().Configure(lunarSurface,material);
            }
            else foreach (var z in s.Zones)
                if (z.Zone == CrewZone.Exterior) Draw("Regolith", z.Area, regolith, -950, true);
            foreach (var r in s.Layout.Obstacles)
            {
                if(lunarSurface!=null && r.MinX<LunarBasePlan.OuterHatch.MaxX)continue;
                var p = new BasePosition((r.MinX+r.MaxX)/2,(r.MinY+r.MaxY)/2);
                if (s.TryGetZone(p, out var zone) && zone == CrewZone.Exterior) Draw("Rock — exact blocking footprint",r,rock,-798);
            }
            if(lunarSurface==null) Draw("Airlock chamber",s.Airlock.Chamber,regolith,-940,true);
            inner = Draw("Inner hatch",s.Airlock.InnerHatch,hatchClosed,1500);
            outer = Draw("Outer hatch",s.Airlock.OuterHatch,hatchClosed,1500);
            airlockLabel = Text("ESCLUSA · sin datos",Center(s.Airlock.Chamber)+Vector3.up*(float)(s.Airlock.Chamber.Height/2-.8),.18f);
            foreach (var z in s.Zones) if (z.Zone == CrewZone.Shelter)
            {
                Vector3 p = Center(z.Area);
                if(lunarSurface==null) Icon("Shelter sign",shelter,p,1.2f,-790);
                shelterLabel = Text("REFUGIO · sin datos",lunarSurface!=null ? new Vector3(p.x,(float)z.Area.MaxY-3.5f,0) : p+Vector3.up*1.2f,.16f);
            }
            if(lunarSurface==null) Icon("Antenna",antenna,At(s.Comms.Antenna),1.2f,900);
            foreach (var p in s.Layout.Stations)
            {
                if (p.Id.Value != "regolith-site" && p.Id.Value != "crater-rim") continue;
                bool optional = p.Id.Value == "crater-rim";
                var station = Instantiate(optional ? optionalPrefab : sciencePrefab,generated,false);
                var body = station.GetComponentInChildren<SpriteRenderer>();
                Sprite artwork = baseView != null ? baseView.LunarSkin?.StationArtwork(p.Id.Value) : null;
                station.Configure(p,optional ? "NÚCLEO · OPCIONAL" : "MUESTRA DE REGOLITO",artwork != null ? artwork : body.sprite,optional ? new Color(1,.78f,.5f) : new Color(.8f,.7f,1));
                stations.Add(station);
                endings[p.Id] = Text("",At(p.Position)+Vector3.down*.85f,.12f);
            }
            // Short entry trail only; never invent a traversable route through authoritative obstacles.
            var entry = At(s.Airlock.ExteriorEntry);
            for (int i=0;i<4;i++)
            {
                var p = entry + Vector3.right*(.8f+i*.8f);
                if (s.Layout.IsAdmissible(new BasePosition(p.x,p.y),s.Outpost.Movement.CrewRadiusMeters)) Icon("Return entry chevron",route,p,.5f,-780);
            }
            Text("← ESCLUSA / REGRESO",entry+new Vector3(2,-2.2f),.14f);
        }
        protected override void Present(ExpeditionSessionSnapshot s)
        {
            if (inner == null) return;
            inner.sprite = s.Airlock.IsCycling ? hatchCycle : s.Airlock.InnerHatchOpen ? hatchOpen : hatchClosed;
            outer.sprite = s.Airlock.IsCycling ? hatchCycle : s.Airlock.OuterHatchOpen ? hatchOpen : hatchClosed;
            airlockLabel.text = "ESCLUSA · " + (s.Airlock.IsCycling ? $"TRANSICIÓN {s.Airlock.CycleFraction:P0}" : "DISPONIBLE")
                + $"\n{s.Airlock.Occupants.Count}/{Context.Scenario.Airlock.Capacity} plazas" + (s.Airlock.Stalled ? " · SIN SERVICIO" : "")
                + "\nInterior " + (s.Airlock.InnerHatchOpen ? "ABIERTA" : "CERRADA") + " · Exterior " + (s.Airlock.OuterHatchOpen ? "ABIERTA" : "CERRADA");
            if (shelterLabel != null) shelterLabel.text = "REFUGIO\n" + (!s.Shelter.Prepared ? "PREPARAR EN EL PANEL" : s.Shelter.ServiceAvailable ? "PREPARADO · CON SERVICIO" : "SIN SERVICIO");
            if (baseView != null) baseView.Present(s.Outpost,Context.Port.Membership.Player);
            s.Outpost.TryGetCrew(Context.Port.Membership.Player,out var me);
            foreach (var station in stations)
                if (s.Outpost.TryGetStation(station.Placement.Id,out var state))
                {
                    station.Present(state,s.Outpost,Context.Port.Membership.Player,me != null && station.Placement.IsWithinReach(me.Pose.Position));
                    endings[station.Placement.Id].text = state.ActiveTask != null || state.LastEnd == null ? ""
                        : state.LastEnd.Reason == TaskEndReason.CancelledByPerformer ? "CANCELADA · sin reembolso"
                        : state.LastEnd.Reason == TaskEndReason.Completed ? "COMPLETADA" : "INTERRUMPIDA";
                }
        }
        protected override void ClearView()
        {
            if (baseView != null) baseView.ClearReadings();
            foreach (var station in replacedStations) if (station != null) station.SetActive(true);
            replacedStations.Clear();
            foreach (var entry in captions) if (entry.Key != null) entry.Key.SetCaption(entry.Value.text, entry.Value.size);
            captions.Clear();
            stations.Clear(); endings.Clear();
            if (generated != null) { generated.gameObject.SetActive(false); if (UnityEngine.Application.isPlaying) Destroy(generated.gameObject); else DestroyImmediate(generated.gameObject); }
            generated = null;
        }
        private static Vector3 At(BasePosition p) => new Vector3((float)p.X,(float)p.Y,0);
        private static string PreparationCaption(string id)
        {
            switch (id)
            {
                case "suit-rack-a": return "TRAJE A";
                case "suit-rack-b": return "TRAJE B";
                case "suit-charger": return "CARGA";
                case "consumables-panel": return "O2 + AGUA";
                case "suit-check": return "SELLOS";
                case "shelter-panel": return "PREPARAR REFUGIO";
                default: return null;
            }
        }
        private static Vector3 Center(BaseRect r) => new Vector3((float)((r.MinX+r.MaxX)/2),(float)((r.MinY+r.MaxY)/2),0);
        private SpriteRenderer Draw(string name,BaseRect r,Sprite sprite,int order,bool tiled=false)
        {
            var v=Icon(name,sprite,Center(r),1,order); v.drawMode=tiled?SpriteDrawMode.Tiled:SpriteDrawMode.Sliced;
            v.size=new Vector2((float)r.Width,(float)r.Height);return v;
        }
        private SpriteRenderer Icon(string name,Sprite sprite,Vector3 p,float size,int order)
        {
            var g=new GameObject(name);g.transform.SetParent(generated,false);g.transform.localPosition=p;g.transform.localScale=Vector3.one*size;
            var v=g.AddComponent<SpriteRenderer>();v.sprite=sprite;v.sharedMaterial=material;v.sortingOrder=order;return v;
        }
        private TextMesh Text(string text,Vector3 p,float size)
        {
            var g=new GameObject(text);g.transform.SetParent(generated,false);g.transform.localPosition=p;
            var t=g.AddComponent<TextMesh>();t.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.fontSize=48;t.characterSize=size*.25f;
            t.anchor=TextAnchor.MiddleCenter;t.alignment=TextAlignment.Center;t.text=text;t.color=new Color(.85f,.94f,1);
            var r=g.GetComponent<MeshRenderer>();r.sharedMaterial=t.font.material;r.sortingOrder=3000;return t;
        }
    }
}
