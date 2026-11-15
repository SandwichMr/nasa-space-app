using System.Collections.Generic;
using Trainer.Application.BaseOperations;
using Trainer.Application.Outpost;
using Trainer.Unity.Presentation.BaseOperations;
using Trainer.Unity.World.Outpost;
using UnityEngine;

namespace Trainer.Unity.World.BaseOperations
{
    /// <summary>Replaces only water/air/logistics visual terminals, preserving the authority's layout and phase 3 world.</summary>
    public sealed class BaseOperationsWorldView : BaseOperationsBoundView
    {
        [SerializeField] private BaseInteriorView baseInterior;
        [SerializeField] private BaseEquipmentView waterPrefab, airPrefab, logisticsPrefab;
        private Transform generated;
        private readonly List<GameObject> replaced = new List<GameObject>();
        private readonly List<BaseEquipmentView> stations = new List<BaseEquipmentView>();
        public IReadOnlyList<BaseEquipmentView> Stations => stations;

        /// <summary>D2 supplies the existing interior BEFORE Bind. No scene search or extra interior is created.</summary>
        public void ConfigureInterior(BaseInteriorView interior)
        {
            if (Live) Unbind();
            baseInterior = interior;
        }
        protected override void Present(BaseOperationsSnapshot snapshot)
        {
            if (generated == null) BuildStations();
            // Some phase 3 views also project the base. Suppress the replaced visuals after each refresh.
            foreach (var original in replaced) if (original != null) original.SetActive(false);
            snapshot.Outpost.TryGetCrew(Context.Port.Membership.Player, out var local);
            foreach (var station in stations)
            {
                Context.Scenario.Layout.TryGetStation(station.Station, out var placement);
                station.Present(snapshot, Context.Scenario.EquipmentAt(station.Station), Context.Port.Membership.Player,
                    local != null && placement.IsWithinReach(local.Pose.Position));
            }
        }
        private void BuildStations()
        {
            generated = new GameObject("Phase04 station visuals — authoritative positions").transform;
            generated.SetParent(transform, false);
            if (baseInterior != null)
                foreach (var station in baseInterior.Stations)
                    if ((Context.Scenario.EquipmentAt(station.Placement.Id).HasValue || station.Placement.Id == Context.Scenario.Logistics.Console)
                        && station.gameObject.activeSelf)
                    { replaced.Add(station.gameObject); station.gameObject.SetActive(false); }
            Spawn(Context.Scenario.WaterStation, waterPrefab, "RECICLADOR · AGUA");
            Spawn(Context.Scenario.AirStation, airPrefab, "AIRE · CO2");
            Spawn(Context.Scenario.Logistics.Console, logisticsPrefab, "SUMINISTROS");
        }
        private void Spawn(StationId station, BaseEquipmentView prefab, string caption)
        {
            if (prefab == null || !Context.Scenario.Layout.TryGetStation(station, out var placement)) return;
            var view = Instantiate(prefab, generated, false);
            view.name = "Station " + station.Value;
            view.Configure(placement, caption);
            stations.Add(view);
        }
        protected override void ClearView()
        {
            foreach (var original in replaced) if (original != null) original.SetActive(true);
            replaced.Clear(); stations.Clear();
            if (generated != null)
            {
                generated.gameObject.SetActive(false);
                if (UnityEngine.Application.isPlaying) Destroy(generated.gameObject); else DestroyImmediate(generated.gameObject);
            }
            generated = null;
        }
    }
}
