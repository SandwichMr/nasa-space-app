using System;
using System.Collections.Generic;
using System.Linq;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;
using Trainer.Tooling.Validation.Phase03;
using Trainer.Unity.Validation.Phase04;

namespace Trainer.Tooling.Validation.Phase04
{
    /// <summary>
    /// D5 whole-scenario validator of a phase 4 scenario: what the frozen constructors cannot check cheaply. It runs the phase 3
    /// plan validator on the contained expedition (zones, hatches, routes, airlock) and adds the phase 4 rules: the water, air
    /// and logistics stations stand inside the base and are walkable from every spawn with both hatches closed; the synthetic
    /// fault is scheduled (reproducible) and the repair is possible (a spare exists or can be ordered); one batch fits the
    /// hold and the untreated tank; decisions stay current long enough to be taken; resupply fits the stores; reserve lines
    /// sit below capacities; and the data is labelled (synthetic or sourced).
    /// </summary>
    public static class BaseOperationsScenarioValidator
    {
        public static List<Phase04Check> Validate(BaseOperationsScenario s, string label)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            string k = Phase04Kinds.ScenarioValidator;
            string p = string.IsNullOrEmpty(label) ? "" : label + ": ";
            var checks = new List<Phase04Check>();

            foreach (Phase03Check c in ExpeditionScenarioValidator.Validate(s.Expedition))
                checks.Add(Phase04Checks.Result(p + "phase 3 plan rule: " + c.Name, k, c.Passed, c.Detail));

            var navigator = new Phase04Navigator(s.Expedition);
            StationId[] phase4 = { s.WaterStation, s.AirStation, s.Logistics.Console };
            var notInside = new List<string>();
            var unreachable = new List<string>();
            foreach (StationId station in phase4)
            {
                s.Layout.TryGetStation(station, out StationPlacement placement);
                if (!s.Expedition.TryGetZone(placement.Position, out CrewZone zone) || zone != CrewZone.Interior) notInside.Add(station.Value);
                foreach (BasePosition spawn in s.Layout.SpawnPoints)
                    if (!navigator.CanReach(spawn, station)) unreachable.Add(station.Value + " from " + spawn);
            }
            checks.Add(Phase04Checks.Result(p + "water, air and logistics stations are inside the base (Interior zone)", k, notInside.Count == 0,
                notInside.Count == 0 ? "3 stations" : "outside the interior: " + string.Join(", ", notInside)));
            checks.Add(Phase04Checks.Result(p + "phase 4 stations walkable from every spawn with both hatches closed", k, unreachable.Count == 0,
                unreachable.Count == 0 ? s.Layout.SpawnPoints.Count + " spawns x 3 stations" : "no route: " + string.Join("; ", unreachable)));

            WaterProcessorConfiguration w = s.Equipment.WaterProcessor;
            AirRevitalizationConfiguration a = s.Equipment.Air;
            WaterStock water = s.Expedition.Outpost.Simulation.LifeSupport.InitialWater;
            checks.Add(Phase04Checks.Result(p + "one batch fits the processor hold and the untreated tank", k,
                w.BatchKilograms <= w.HoldCapacityKilograms && w.BatchKilograms <= water.UntreatedCapacityKilograms,
                $"batch {w.BatchKilograms} kg, hold {w.HoldCapacityKilograms} kg, untreated capacity {water.UntreatedCapacityKilograms} kg"));
            checks.Add(Phase04Checks.Result(p + "the routing decision is observable (nominal within the line, degraded above)", k,
                w.NominalConductivityMicrosiemens <= w.ProductSpecLimitMicrosiemens && w.DegradedConductivityMicrosiemens > w.ProductSpecLimitMicrosiemens,
                $"{w.NominalConductivityMicrosiemens} / {w.DegradedConductivityMicrosiemens} µS/cm against {w.ProductSpecLimitMicrosiemens} µS/cm (scenario line, not a health index)"));
            checks.Add(Phase04Checks.Result(p + "synthetic recycler fault scheduled (reproducible, no statistics)", k, w.DegradeAtSeconds.HasValue,
                w.DegradeAtSeconds.HasValue
                    ? $"degrades at {w.DegradeAtSeconds} s, fails {(w.FailAfterDegradedSeconds.HasValue ? w.FailAfterDegradedSeconds + " s later" : "never")}"
                    : "no fault: the maintenance loop cannot be exercised"));

            bool waterSpare = SpareAvailable(s, w.FilterSpareKey), airSpare = SpareAvailable(s, a.InletFilterSpareKey);
            checks.Add(Phase04Checks.Result(p + "each repair has a spare on board or orderable", k, waterSpare && airSpare,
                $"{w.FilterSpareKey}: {(waterSpare ? "yes" : "no")}; {a.InletFilterSpareKey}: {(airSpare ? "yes" : "no")}"));

            var timed = new[]
            {
                BaseOperationAction.Inspect, BaseOperationAction.ProcessBatch, BaseOperationAction.RouteProduct, BaseOperationAction.RecheckPotable,
                BaseOperationAction.ServiceAir, BaseOperationAction.Intervene, BaseOperationAction.TestEquipment, BaseOperationAction.UnloadDelivery
            };
            var instant = s.Operations.Where(o => timed.Contains(o.Action) && o.DurationSeconds <= 0).Select(o => o.Action + "/" + o.Choice).ToList();
            checks.Add(Phase04Checks.Result(p + "work at a station takes time (no instant inspection, batch, service, repair or unload)", k,
                instant.Count == 0, instant.Count == 0 ? s.Operations.Count + " definitions" : "instant: " + string.Join(", ", instant)));

            double inspect = s.DefinitionOf(BaseOperationAction.Inspect, OperationChoice.None).DurationSeconds;
            double longestDecision = s.Operations.Where(o => BaseOperationCommand.NeedsObservation(o.Action)).Max(o => o.DurationSeconds);
            checks.Add(Phase04Checks.Result(p + "an observation stays current long enough to walk and decide", k,
                s.ObservationValiditySeconds > inspect + longestDecision,
                $"validity {s.ObservationValiditySeconds} s > inspect {inspect} s + longest decision {longestDecision} s"));

            FoodStock food = s.Expedition.Outpost.Simulation.LifeSupport.InitialFood;
            var overflow = new List<string>();
            foreach (ResupplyManifest m in s.Manifests)
                foreach (ManifestItem item in m.Items)
                {
                    if (item.IsSpare)
                    {
                        SpareStock initial = s.Equipment.InitialSpares.FirstOrDefault(x => x.Key == item.SpareKey);
                        if (initial == null || item.Count > initial.Capacity) overflow.Add(m.Key + ":" + item.SpareKey);
                    }
                    else
                    {
                        double capacity = item.Resource == ResourceKind.StoredFood ? food.CapacityKilograms
                            : item.Resource == ResourceKind.PotableWater ? water.PotableCapacityKilograms : water.UntreatedCapacityKilograms;
                        if (item.Kilograms > capacity) overflow.Add(m.Key + ":" + item.Resource);
                    }
                }
            checks.Add(Phase04Checks.Result(p + "each manifest line fits its store when empty", k, overflow.Count == 0,
                overflow.Count == 0 ? s.Manifests.Count + " manifest(s)" : "never fits: " + string.Join(", ", overflow)));
            bool orderable = s.Manifests.Any(m => m.LeadSeconds > 0 && m.CancelWindowSeconds < m.LeadSeconds);
            checks.Add(Phase04Checks.Result(p + "resupply has a cost: lead time and a cancel window shorter than it", k, orderable,
                string.Join("; ", s.Manifests.Select(m => $"{m.Key}: lead {m.LeadSeconds} s, cancel {m.CancelWindowSeconds} s"))));

            var lines = new List<string>();
            bool linesOk = true;
            foreach (GaugeRule rule in s.Gauges)
            {
                if (!rule.ReserveAt.HasValue) continue;
                double? capacity = rule.Kind == GaugeKind.PotableWater ? water.PotableCapacityKilograms
                    : rule.Kind == GaugeKind.StoredFood ? food.CapacityKilograms
                    : rule.Kind == GaugeKind.UntreatedWater ? water.UntreatedCapacityKilograms
                    : rule.Kind == GaugeKind.BatteryEnergy ? s.Expedition.Outpost.Simulation.Power.CapacityJoules
                    : (double?)null;
                if (capacity.HasValue && rule.ReserveAt.Value >= capacity.Value) linesOk = false;
                lines.Add($"{rule.Kind} reserve {rule.ReserveAt}{(capacity.HasValue ? " / capacity " + capacity : "")}");
            }
            checks.Add(Phase04Checks.Result(p + "reserve lines sit below capacities (a full store is never 'reserve')", k, linesOk, string.Join("; ", lines)));

            bool labelled = Labelled(s.Provenance) && Labelled(s.Equipment.Provenance);
            checks.Add(Phase04Checks.Result(p + "scenario and equipment data labelled (synthetic or sourced)", k, labelled,
                Short(s.Provenance)));
            checks.Add(Phase04Checks.Result(p + "irreversible end only after a recovery window", k, s.Irreversible.EssentialUnservedGraceSeconds > 0,
                $"essential power unserved for {s.Irreversible.EssentialUnservedGraceSeconds} s in a row; reports every {s.ReportIntervalSeconds} s (non-terminal)"));
            return checks;
        }

        private static bool SpareAvailable(BaseOperationsScenario s, string key) =>
            s.Equipment.InitialSpares.Any(x => x.Key == key && x.Count > 0)
            || s.Manifests.Any(m => m.Items.Any(i => i.IsSpare && i.SpareKey == key && i.Count > 0));

        private static bool Labelled(string provenance)
        {
            if (string.IsNullOrWhiteSpace(provenance)) return false;
            string t = provenance.ToUpperInvariant();
            return t.Contains("SINTÉTIC") || t.Contains("SINTETIC") || t.Contains("SYNTHETIC") || t.Contains("FUENTE") || t.Contains("SOURCE");
        }

        private static string Short(string text) => text == null ? "" : text.Length <= 90 ? text : text.Substring(0, 90) + "…";
    }
}
