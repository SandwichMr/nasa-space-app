using System;
using System.Collections.Generic;
using System.Linq;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.Eva;

namespace Trainer.Tooling.Validation.Phase03
{
    /// <summary>One D5 phase 3 check (typed result; kinds are not added up).</summary>
    public sealed class Phase03Check
    {
        public string Name { get; }
        public bool Passed { get; }
        public string Detail { get; }
        public Phase03Check(string name, bool passed, string detail)
        {
            Name = name;
            Passed = passed;
            Detail = detail ?? "";
        }
        public override string ToString() => (Passed ? "[Passed] " : "[FAILED] ") + Name + " - " + Detail;
    }

    /// <summary>
    /// D5 whole-plan validator of a phase 3 scenario (what the constructor cannot check cheaply). On a 0.25 m grid of
    /// positions admissible for the crew disc: zone coverage; physical isolation by the hatches (with both closed the
    /// base, the chamber and the exterior are separate; with one open, only its side joins the chamber); every station
    /// reachable from its required zone; exterior return routes; chamber capacity; the authored episode labelled
    /// (synthetic or sourced) with an explicit end; and that the shipped mission is winnable within the declared reserve.
    /// </summary>
    public static class ExpeditionScenarioValidator
    {
        public const double Grid = 0.25;

        public static List<Phase03Check> Validate(ExpeditionScenario s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            var checks = new List<Phase03Check>();
            double r = s.Outpost.Movement.CrewRadiusMeters;
            OutpostLayout open = s.Layout, closed = s.LayoutWithHatches(true, true);
            List<BasePosition> points = GridPoints(open, r);

            int uncovered = points.Count(p => !s.TryGetZone(p, out _));
            checks.Add(new Phase03Check("zones cover every admissible position", uncovered == 0,
                $"{points.Count} admissible grid points, {uncovered} without zone"));

            HashSet<int> fromBaseClosed = Flood(closed, r, s.Layout.SpawnPoints[0]);
            checks.Add(Isolation("airlock closed: the base reaches neither the chamber nor the exterior", s, closed, r, fromBaseClosed,
                mustReach: new[] { CrewZone.Interior, CrewZone.Shelter }, mustNot: new[] { CrewZone.Airlock, CrewZone.Exterior }));
            HashSet<int> fromBaseInner = Flood(s.LayoutWithHatches(false, true), r, s.Layout.SpawnPoints[0]);
            checks.Add(Isolation("inner hatch open: the base reaches the chamber, never the exterior", s, s.LayoutWithHatches(false, true), r,
                fromBaseInner, mustReach: new[] { CrewZone.Airlock }, mustNot: new[] { CrewZone.Exterior }));
            BasePosition chamberCenter = Center(s.Airlock.Chamber);
            HashSet<int> fromChamberOuter = Flood(s.LayoutWithHatches(true, false), r, chamberCenter);
            checks.Add(Isolation("outer hatch open: the chamber reaches the exterior, never the base", s, s.LayoutWithHatches(true, false), r,
                fromChamberOuter, mustReach: new[] { CrewZone.Exterior }, mustNot: new[] { CrewZone.Interior, CrewZone.Shelter }));

            HashSet<int> everything = Flood(open, r, s.Layout.SpawnPoints[0]);
            var unreachable = new List<string>();
            foreach (StationPlacement station in s.Layout.Stations)
            {
                bool panel = s.Airlock.IsPanel(station.Id);
                CrewZone? zone = panel ? (CrewZone?)null : RequiredZone(s, station.Id);
                bool ok = points.Any(p => Contains(everything, open, r, p) && station.IsWithinReach(p)
                    && (zone == null || (s.TryGetZone(p, out CrewZone z) && z == zone)));
                if (!ok) unreachable.Add(station.Id.Value);
            }
            checks.Add(new Phase03Check("every station has a standing point in reach, in its required zone", unreachable.Count == 0,
                unreachable.Count == 0 ? $"{s.Layout.Stations.Count} stations" : "unreachable: " + string.Join(", ", unreachable)));

            bool doorways = Fits(s.Airlock.InnerHatch, r) && Fits(s.Airlock.OuterHatch, r)
                && s.Layout.SpawnPoints.All(p => s.Airlock.InnerHatch.DistanceTo(p) >= r && s.Airlock.OuterHatch.DistanceTo(p) >= r);
            checks.Add(new Phase03Check("hatch doorways fit a crew disc and are clear of spawns", doorways,
                $"inner {Long(s.Airlock.InnerHatch):0.##} m, outer {Long(s.Airlock.OuterHatch):0.##} m, crew diameter {2 * r:0.##} m"));
            int chamberPoints = points.Count(p => s.TryGetZone(p, out CrewZone z) && z == CrewZone.Airlock
                && s.Airlock.InnerHatch.DistanceTo(p) >= r && s.Airlock.OuterHatch.DistanceTo(p) >= r);
            double needed = s.Airlock.Capacity * Math.Pow(2 * r / Grid, 2);
            checks.Add(new Phase03Check("the chamber holds its capacity clear of both doorways", chamberPoints >= needed,
                $"{chamberPoints} clear grid points for {s.Airlock.Capacity} crew (needs about {needed:0})"));

            var route = new ReturnRouteField(s);
            var routes = new List<string>();
            bool allRoutes = true;
            foreach (ExpeditionTaskRule rule in s.TaskRules.Where(t => t.Purpose == TaskPurpose.ExteriorWork))
            {
                s.Outpost.TryGetTask(rule.Task, out TaskDefinition task);
                s.Layout.TryGetStation(task.Station, out StationPlacement station);
                BasePosition stand = Stand(points, everything, open, r, station, s);
                bool ok = route.TryGetPathMeters(stand, out double meters);
                allRoutes &= ok;
                routes.Add($"{station.Id}: {(ok ? meters.ToString("0.0") + " m" : "no route")}");
            }
            checks.Add(new Phase03Check("every exterior site has a return route to the airlock entry", allRoutes, string.Join("; ", routes)));

            checks.Add(Winnable(s, route, points, everything, open, r));
            checks.Add(EpisodeCheck(s));
            return checks;
        }

        /// <summary>With full suits, going straight from the airlock to each exterior site, doing its task and then
        /// standing still must leave a non-negative margin: the shipped mission can be done inside the declared reserve.</summary>
        private static Phase03Check Winnable(ExpeditionScenario s, ReturnRouteField route, List<BasePosition> points, HashSet<int> reach,
            OutpostLayout open, double r)
        {
            SuitModelConfiguration model = s.Eva.Model;
            double speed = s.Outpost.Movement.MaxSpeedMetersPerSecond * s.SuitedSpeedFactor;
            var details = new List<string>();
            bool ok = true;
            foreach (ExpeditionTaskRule rule in s.TaskRules.Where(t => t.Purpose == TaskPurpose.ExteriorWork))
            {
                s.Outpost.TryGetTask(rule.Task, out TaskDefinition task);
                s.Layout.TryGetStation(task.Station, out StationPlacement station);
                BasePosition stand = Stand(points, reach, open, r, station, s);
                if (!route.TryGetPathMeters(stand, out double meters)) { ok = false; continue; }
                double walk = meters / speed;
                var levels = new SuitLevels(model.Get(SuitService.Oxygen).Capacity, model.Get(SuitService.Battery).Capacity,
                    model.Get(SuitService.Feedwater).Capacity);
                foreach (SuitServiceModel service in model.Services)
                {
                    double used = service.RestRate * s.Airlock.DepressurizeSeconds + service.WalkRate * walk
                        + (service.WorkRate + (service.Service == SuitService.Battery ? rule.SuitToolWatts : 0)) * task.DurationSeconds;
                    levels = levels.With(service.Service, Math.Max(0, levels.Get(service.Service) - used));
                }
                ReturnMarginEstimate m = ReturnMarginCalculator.Estimate(model, levels, SuitActivity.Rest, 0,
                    new ReturnPlan(meters, walk, 0, s.Airlock.RepressurizeSeconds, true), 0);
                ok &= m.MarginSeconds >= 0;
                details.Add($"{task.Id}: margin {m.MarginSeconds:0} s at rest after the task, limited by {m.LimitingService}");
            }
            return new Phase03Check("each exterior objective is reachable and returnable within the declared reserve", ok, string.Join("; ", details));
        }

        private static Phase03Check EpisodeCheck(ExpeditionScenario s)
        {
            ParticleEventEpisode e = s.Episode;
            if (e == null) return new Phase03Check("particle episode labelled and explicitly ended", true, "no episode: the HUD must show 'sin información'");
            bool labelled = e.Synthetic || e.Source != null;
            bool ended = e.Messages.Any(m => m.Kind == AdvisoryKind.InstructionEnd);
            bool inTime = e.Messages.All(m => m.IssuedAtSeconds < s.Rules.MissionDurationSeconds);
            bool deadlines = e.Messages.Where(m => m.ShelterBySeconds.HasValue).All(m => m.ShelterBySeconds.Value > m.IssuedAtSeconds);
            return new Phase03Check("particle episode labelled and explicitly ended", labelled && ended && inTime && deadlines,
                $"{(e.Synthetic ? "SYNTHETIC" : "historical, source " + e.Source?.Source)}; {e.Messages.Count} messages; explicit end: {ended}; " +
                $"all issued before the mission ends: {inTime}; hidden event {e.EventOnsetSeconds:0}-{e.EventEndSeconds:0} s (never used to judge)");
        }

        private static Phase03Check Isolation(string name, ExpeditionScenario s, OutpostLayout layout, double r, HashSet<int> reached,
            CrewZone[] mustReach, CrewZone[] mustNot)
        {
            List<BasePosition> points = GridPoints(layout, r);
            var zones = new HashSet<CrewZone>();
            foreach (BasePosition p in points)
                if (Contains(reached, layout, r, p) && s.TryGetZone(p, out CrewZone z)) zones.Add(z);
            bool ok = mustReach.All(zones.Contains) && !mustNot.Any(zones.Contains);
            return new Phase03Check(name, ok, "reached zones: " + string.Join(", ", zones.OrderBy(z => z)));
        }

        private static CrewZone? RequiredZone(ExpeditionScenario s, StationId station)
        {
            foreach (TaskDefinition task in s.Outpost.Tasks)
                if (task.Station == station) return s.RuleFor(task.Id).RequiredZone;
            return null;
        }

        private static BasePosition Stand(List<BasePosition> points, HashSet<int> reach, OutpostLayout layout, double r,
            StationPlacement station, ExpeditionScenario s) =>
            points.Where(p => Contains(reach, layout, r, p) && station.IsWithinReach(p)).OrderBy(p => p.DistanceTo(station.Position))
                .DefaultIfEmpty(station.Position).First();

        private static bool Fits(BaseRect hatch, double r) => Long(hatch) >= 2 * r;
        private static double Long(BaseRect rect) => Math.Max(rect.Width, rect.Height);
        private static BasePosition Center(BaseRect rect) => new BasePosition((rect.MinX + rect.MaxX) / 2, (rect.MinY + rect.MaxY) / 2);

        // ---- grid ------------------------------------------------------------------------------------------

        private static List<BasePosition> GridPoints(OutpostLayout layout, double r)
        {
            var list = new List<BasePosition>();
            BaseRect b = layout.Bounds;
            for (double y = b.MinY + Grid / 2; y < b.MaxY; y += Grid)
                for (double x = b.MinX + Grid / 2; x < b.MaxX; x += Grid)
                {
                    var p = new BasePosition(x, y);
                    if (layout.IsAdmissible(p, r)) list.Add(p);
                }
            return list;
        }

        private static int Key(OutpostLayout layout, BasePosition p)
        {
            int i = (int)Math.Floor((p.X - layout.Bounds.MinX) / Grid), j = (int)Math.Floor((p.Y - layout.Bounds.MinY) / Grid);
            return j * 100000 + i;
        }

        private static bool Contains(HashSet<int> set, OutpostLayout layout, double r, BasePosition p) => set.Contains(Key(layout, p));

        /// <summary>4-connected flood fill of admissible grid points from the point nearest to <paramref name="start"/>.</summary>
        private static HashSet<int> Flood(OutpostLayout layout, double r, BasePosition start)
        {
            var seen = new HashSet<int>();
            BaseRect b = layout.Bounds;
            int nx = (int)Math.Ceiling(b.Width / Grid), ny = (int)Math.Ceiling(b.Height / Grid);
            BasePosition At(int i, int j) => new BasePosition(b.MinX + (i + 0.5) * Grid, b.MinY + (j + 0.5) * Grid);
            int si = (int)Math.Floor((start.X - b.MinX) / Grid), sj = (int)Math.Floor((start.Y - b.MinY) / Grid);
            var queue = new Queue<(int, int)>();
            for (int dj = -2; dj <= 2 && queue.Count == 0; dj++)
                for (int di = -2; di <= 2; di++)
                {
                    int i = si + di, j = sj + dj;
                    if (i < 0 || j < 0 || i >= nx || j >= ny || !layout.IsAdmissible(At(i, j), r)) continue;
                    queue.Enqueue((i, j));
                    seen.Add(j * 100000 + i);
                    break;
                }
            while (queue.Count > 0)
            {
                (int ci, int cj) = queue.Dequeue();
                foreach ((int di, int dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int i = ci + di, j = cj + dj;
                    if (i < 0 || j < 0 || i >= nx || j >= ny) continue;
                    int key = j * 100000 + i;
                    if (seen.Contains(key) || !layout.IsAdmissible(At(i, j), r)) continue;
                    seen.Add(key);
                    queue.Enqueue((i, j));
                }
            }
            return seen;
        }
    }
}
