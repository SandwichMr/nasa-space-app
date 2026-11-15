using System;
using System.Collections.Generic;
using Trainer.Application.Outpost;
using UnityEngine;

namespace Trainer.Unity.World.Outpost
{
    /// <summary>Read-only projection of D2's layout. The drawn blocking rectangles are exactly the authority's.
    /// No Collider2D, Rigidbody2D, movement solver, input, session or fallback scenario lives here.</summary>
    public sealed class BaseInteriorView : MonoBehaviour
    {
        [SerializeField] private Sprite floor;
        [SerializeField] private Sprite wall;
        [SerializeField] private Sprite stripe;
        [SerializeField] private Sprite waterStation;
        [SerializeField] private Sprite airStation;
        [SerializeField] private Sprite scienceStation;
        [SerializeField] private Sprite genericStation;
        [SerializeField] private Material material;
        [SerializeField] private OutpostStationView stationPrefab;
        [SerializeField] private Transform generated;
        [SerializeField] private List<OutpostStationView> stations = new List<OutpostStationView>();
        [SerializeField] private List<SpriteRenderer> blockers = new List<SpriteRenderer>();
        public IReadOnlyList<OutpostStationView> Stations => stations;
        public IReadOnlyList<SpriteRenderer> Blockers => blockers;
        public OutpostScenario Scenario { get; private set; }

        public void Configure(OutpostScenario scenario)
        {
            ClearGeometry();
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            generated = new GameObject("Layout projection (D2 geometry)").transform;
            generated.SetParent(transform, false);
            BaseRect b = scenario.Layout.Bounds;
            Draw("Floor", b, floor, -1000, Color.white, true);
            // This trim lies OUTSIDE the walkable bounds, never narrows a doorway.
            const double t = .18;
            Draw("Outside west", new BaseRect(b.MinX-t,b.MinY-t,b.MinX,b.MaxY+t), wall, -850, Color.white);
            Draw("Outside east", new BaseRect(b.MaxX,b.MinY-t,b.MaxX+t,b.MaxY+t), wall, -850, Color.white);
            Draw("Outside south", new BaseRect(b.MinX,b.MinY-t,b.MaxX,b.MinY), wall, -850, Color.white);
            Draw("Outside north", new BaseRect(b.MinX,b.MaxY,b.MaxX,b.MaxY+t), wall, -850, Color.white);
            foreach (BaseRect obstacle in scenario.Layout.Obstacles)
            {
                SpriteRenderer r = Draw("Blocking rectangle", obstacle, wall, -800, Color.white);
                blockers.Add(r);
                // Bevel stays inside the exact physical footprint.
                double inset = Math.Min(.06, Math.Min(obstacle.Width, obstacle.Height) / 4);
                Draw("Wall inset", new BaseRect(obstacle.MinX+inset,obstacle.MinY+inset,
                    obstacle.MaxX-inset,obstacle.MaxY-inset), stripe, -799, new Color(.31f,.43f,.54f));
            }
            foreach (StationPlacement p in scenario.Layout.Stations)
            {
                string key = p.Id.ToString();
                string label = key;
                Sprite sprite = genericStation;
                Color color = new Color(.8f,.85f,.9f);
                if (key.Contains("water")) { label = "AGUA"; sprite = waterStation; color = new Color(.4f,.86f,1); }
                else if (key.Contains("air")) { label = "AIRE"; sprite = airStation; color = new Color(.5f,.94f,.74f); }
                else
                {
                    foreach (TaskDefinition task in scenario.Tasks)
                        if (task.Station == p.Id && task.Category == TaskCategory.Science)
                        { label = "CIENCIA"; sprite = scienceStation; color = new Color(.81f,.67f,1); break; }
                }
                var station = Instantiate(stationPrefab, generated, false);
                station.name = "Station " + key;
                station.Configure(p, label, sprite, color);
                stations.Add(station);
            }
        }

        public void Present(OutpostSessionSnapshot snapshot, PlayerId local)
        {
            if (Scenario == null || snapshot == null || snapshot.ScenarioId != Scenario.Id) { ClearReadings(); return; }
            snapshot.TryGetCrew(local, out CrewMemberState member);
            foreach (OutpostStationView station in stations)
                if (snapshot.TryGetStation(station.Placement.Id, out StationState state))
                    station.Present(state, snapshot, local, member != null && station.Placement.IsWithinReach(member.Pose.Position));
                else station.Clear();
        }

        public void ClearReadings() { foreach (var station in stations) station.Clear(); }

        private SpriteRenderer Draw(string label, BaseRect b, Sprite sprite, int order, Color color, bool tiled = false)
        {
            var obj = new GameObject(label);
            obj.transform.SetParent(generated, false);
            obj.transform.localPosition = new Vector3((float)((b.MinX+b.MaxX)/2), (float)((b.MinY+b.MaxY)/2), 0);
            var renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            renderer.sortingOrder = order;
            renderer.color = color;
            renderer.drawMode = tiled ? SpriteDrawMode.Tiled : SpriteDrawMode.Sliced;
            renderer.size = new Vector2((float)b.Width, (float)b.Height);
            return renderer;
        }

        private void ClearGeometry()
        {
            if (generated != null)
            {
                generated.gameObject.SetActive(false);
                if (UnityEngine.Application.isPlaying) Destroy(generated.gameObject);
                else DestroyImmediate(generated.gameObject);
            }
            stations.Clear(); blockers.Clear(); Scenario = null;
        }
    }
}
