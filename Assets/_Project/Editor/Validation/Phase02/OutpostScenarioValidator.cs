using System;
using System.Collections.Generic;
using System.Linq;
using Trainer.Application.Outpost;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Tooling.Validation.Phase02
{
    public enum Phase02Severity { Error, Warning, Info }

    public sealed class Phase02Issue
    {
        public Phase02Severity Severity { get; }
        public string Code { get; }
        public string Message { get; }

        public Phase02Issue(Phase02Severity severity, string code, string message)
        {
            Severity = severity;
            Code = code;
            Message = message;
        }

        public override string ToString() => Severity + " " + Code + ": " + Message;
    }

    public sealed class Phase02Report
    {
        private readonly List<Phase02Issue> issues = new List<Phase02Issue>();
        public IReadOnlyList<Phase02Issue> Issues => issues;
        public bool IsValid => issues.All(i => i.Severity != Phase02Severity.Error);

        public void Add(Phase02Severity severity, string code, string message) =>
            issues.Add(new Phase02Issue(severity, code, message));

        public void Error(string code, string message) => Add(Phase02Severity.Error, code, message);
        public void Warning(string code, string message) => Add(Phase02Severity.Warning, code, message);
        public void Info(string code, string message) => Add(Phase02Severity.Info, code, message);

        public bool Has(string code) => issues.Any(i => i.Code == code);
        public void Merge(Phase02Report other) => issues.AddRange(other.issues);

        public override string ToString() => issues.Count == 0 ? "OK" : string.Join("; ", issues);
    }

    /// <summary>
    /// Static checks of a phase 2 scenario BEFORE anyone plays it. It only reads the contract types; the
    /// collision rule is the shared <see cref="OutpostLayout.IsAdmissible"/>, never a second geometry.
    ///
    /// Navigability is a flood fill over a grid of admissible disc centres starting at every spawn point. A move
    /// between neighbouring cells is accepted only if both ends and the midpoint are admissible; with a step
    /// well below the crew radius the swept disc can deviate from that test by at most step²/(8·radius)
    /// (reported as Info). A station is reachable when a reached centre is within its interaction radius.
    /// </summary>
    public static class OutpostScenarioValidator
    {
        public const int MaxGridCells = 4_000_000;

        public static Phase02Report Validate(OutpostScenario scenario, double gridStepMeters = 0)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            var report = new Phase02Report();
            OutpostLayout layout = scenario.Layout;
            double radius = scenario.Movement.CrewRadiusMeters;

            // Two players is the phase 2 requirement; more is allowed.
            if (layout.MaxCrew < 2)
                report.Error("TooFewSpawnPoints", "MaxCrew = " + layout.MaxCrew + "; phase 2 needs at least two astronauts.");

            for (int i = 0; i < layout.SpawnPoints.Count; i++)
            {
                if (!layout.IsAdmissible(layout.SpawnPoints[i], radius))
                    report.Error("SpawnNotAdmissible", "Spawn " + i + " " + layout.SpawnPoints[i] + " is not admissible.");
                for (int j = i + 1; j < layout.SpawnPoints.Count; j++)
                    if (layout.SpawnPoints[i].DistanceTo(layout.SpawnPoints[j]) < 2 * radius)
                        report.Warning("SpawnOverlap", "Spawns " + i + " and " + j + " overlap visually (crew do not collide).");
            }

            // Stations and tasks: two people must be able to do DIFFERENT tasks at the same time.
            var stationsWithTasks = new HashSet<StationId>(scenario.Tasks.Select(t => t.Station));
            if (stationsWithTasks.Count < 2)
                report.Error("NoParallelWork", "Fewer than two stations offer tasks: two players cannot work on different tasks.");
            foreach (StationPlacement station in layout.Stations)
                if (!stationsWithTasks.Contains(station.Id))
                    report.Warning("StationWithoutTask", "Station '" + station.Id + "' offers no task.");
            if (!scenario.Tasks.Any(t => t.Category == TaskCategory.Support))
                report.Error("NoSupportTask", "No support task: the interior need cannot be served.");
            if (!scenario.Tasks.Any(t => t.Category == TaskCategory.Science))
                report.Warning("NoScienceTask", "No science task: optional science is part of the phase 2 goal.");

            for (int i = 0; i < layout.Stations.Count; i++)
                for (int j = i + 1; j < layout.Stations.Count; j++)
                {
                    StationPlacement a = layout.Stations[i], b = layout.Stations[j];
                    if (a.Position.DistanceTo(b.Position) < a.InteractionRadiusMeters + b.InteractionRadiusMeters)
                        report.Info("ReachOverlap", "Reach of '" + a.Id + "' and '" + b.Id
                            + "' overlaps; the UI must name the station it acts on.");
                }

            ValidateEffects(scenario, report);
            ValidatePower(scenario, report);
            ValidateNavigation(scenario, gridStepMeters, report);
            return report;
        }

        private static void ValidateEffects(OutpostScenario scenario, Phase02Report report)
        {
            WaterStock water = scenario.Simulation.LifeSupport.InitialWater;
            FoodStock food = scenario.Simulation.LifeSupport.InitialFood;
            foreach (TaskDefinition task in scenario.Tasks)
            {
                foreach (ResourceChange change in task.CompletionEffect.Changes)
                {
                    double stock, capacity;
                    switch (change.Kind)
                    {
                        case ResourceKind.PotableWater: stock = water.PotableKilograms; capacity = water.PotableCapacityKilograms; break;
                        case ResourceKind.UntreatedWater: stock = water.UntreatedKilograms; capacity = water.UntreatedCapacityKilograms; break;
                        default: stock = food.StoredKilograms; capacity = food.CapacityKilograms; break;
                    }
                    double magnitude = Math.Abs(change.DeltaKilograms);
                    if (magnitude > capacity)
                        report.Error("EffectNeverApplicable", "Task '" + task.Id + "' changes " + change.Kind + " by "
                            + change.DeltaKilograms + " kg but its capacity is " + capacity + " kg.");
                    else if (change.DeltaKilograms < 0 && stock + change.DeltaKilograms < 0)
                        report.Info("EffectNeedsStock", "Task '" + task.Id + "' cannot complete from the initial "
                            + change.Kind + " (" + stock + " kg); a start is rejected with InsufficientResources.");
                    else if (change.DeltaKilograms > 0 && stock + change.DeltaKilograms > capacity)
                        report.Info("EffectNeedsRoom", "Task '" + task.Id + "' would overflow initial " + change.Kind + ".");
                }
            }
        }

        private static void ValidatePower(OutpostScenario scenario, Phase02Report report)
        {
            var power = scenario.Simulation.Power;
            double allSupport = scenario.Tasks.Where(t => t.Category == TaskCategory.Support).Sum(t => t.PowerWatts);
            if (allSupport > 0 && power.EssentialConsumptionWatts + allSupport > power.GenerationWatts + power.MaxDischargeWatts)
                report.Info("ServiceMayStall", "Essential + every support task (" + (power.EssentialConsumptionWatts + allSupport)
                    + " W) exceeds generation + discharge (" + (power.GenerationWatts + power.MaxDischargeWatts)
                    + " W): some combinations stall; it must be visible to players.");
        }

        private static void ValidateNavigation(OutpostScenario scenario, double requestedStep, Phase02Report report)
        {
            OutpostLayout layout = scenario.Layout;
            double radius = scenario.Movement.CrewRadiusMeters;
            double step = requestedStep > 0 ? requestedStep : Math.Min(0.05, radius / 4);
            BaseRect b = layout.Bounds;
            double minX = b.MinX + radius, minY = b.MinY + radius, maxX = b.MaxX - radius, maxY = b.MaxY - radius;
            if (maxX < minX || maxY < minY)
            {
                report.Error("BoundsTooSmall", "Bounds cannot hold a crew disc of radius " + radius + " m.");
                return;
            }
            int nx = (int)Math.Floor((maxX - minX) / step) + 1, ny = (int)Math.Floor((maxY - minY) / step) + 1;
            while ((long)nx * ny > MaxGridCells)
            {
                step *= 2;
                nx = (int)Math.Floor((maxX - minX) / step) + 1;
                ny = (int)Math.Floor((maxY - minY) / step) + 1;
            }
            report.Info("NavigationGrid", nx + "×" + ny + " cells, step " + step + " m, sweep tolerance "
                + (step * step / (8 * radius)).ToString("0.######") + " m.");

            BasePosition Cell(int ix, int iy) => new BasePosition(minX + ix * step, minY + iy * step);
            var admissible = new bool[nx, ny];
            for (int ix = 0; ix < nx; ix++)
                for (int iy = 0; iy < ny; iy++)
                    admissible[ix, iy] = layout.IsAdmissible(Cell(ix, iy), radius);

            var component = new int[nx, ny];
            int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
            var spawnComponent = new int[layout.SpawnPoints.Count];
            int components = 0;
            for (int s = 0; s < layout.SpawnPoints.Count; s++)
            {
                BasePosition spawn = layout.SpawnPoints[s];
                // Seed: nearest admissible cell connected to the exact spawn by an admissible straight segment.
                int sx = Clamp((int)Math.Round((spawn.X - minX) / step), 0, nx - 1);
                int sy = Clamp((int)Math.Round((spawn.Y - minY) / step), 0, ny - 1);
                if (!admissible[sx, sy] || !SegmentAdmissible(layout, spawn, Cell(sx, sy), radius, step))
                {
                    report.Error("SpawnIsolated", "Spawn " + s + " " + spawn + " does not connect to the navigation grid.");
                    spawnComponent[s] = -1;
                    continue;
                }
                if (component[sx, sy] == 0)
                {
                    components++;
                    var queue = new Queue<(int, int)>();
                    component[sx, sy] = components;
                    queue.Enqueue((sx, sy));
                    while (queue.Count > 0)
                    {
                        (int cx, int cy) = queue.Dequeue();
                        for (int k = 0; k < 4; k++)
                        {
                            int ex = cx + dx[k], ey = cy + dy[k];
                            if (ex < 0 || ey < 0 || ex >= nx || ey >= ny || component[ex, ey] != 0 || !admissible[ex, ey]) continue;
                            BasePosition from = Cell(cx, cy), to = Cell(ex, ey);
                            if (!layout.IsAdmissible(new BasePosition((from.X + to.X) / 2, (from.Y + to.Y) / 2), radius)) continue;
                            component[ex, ey] = components;
                            queue.Enqueue((ex, ey));
                        }
                    }
                }
                spawnComponent[s] = component[sx, sy];
            }

            int main = spawnComponent.Length > 0 ? spawnComponent[0] : -1;
            for (int s = 1; s < spawnComponent.Length; s++)
                if (spawnComponent[s] > 0 && main > 0 && spawnComponent[s] != main)
                    report.Error("SpawnsDisconnected", "Spawn " + s + " cannot walk to spawn 0: the crew is split.");

            foreach (StationPlacement station in layout.Stations)
            {
                bool reachedByMain = false, reachedByAny = false;
                double best = double.PositiveInfinity;
                for (int ix = 0; ix < nx; ix++)
                    for (int iy = 0; iy < ny; iy++)
                    {
                        if (component[ix, iy] == 0) continue;
                        double d = Cell(ix, iy).DistanceTo(station.Position);
                        if (d < best) best = d;
                        if (d <= station.InteractionRadiusMeters)
                        {
                            reachedByAny = true;
                            if (component[ix, iy] == main) reachedByMain = true;
                        }
                    }
                if (!reachedByAny)
                    report.Error("StationUnreachable", "Station '" + station.Id + "' cannot be reached: closest walkable centre "
                        + (double.IsInfinity(best) ? "none" : best.ToString("0.###") + " m") + ", reach "
                        + station.InteractionRadiusMeters + " m.");
                else if (!reachedByMain)
                    report.Error("StationUnreachableFromSpawn0", "Station '" + station.Id + "' is reachable only from another region.");
            }
        }

        private static bool SegmentAdmissible(OutpostLayout layout, BasePosition a, BasePosition b, double radius, double step)
        {
            double length = a.DistanceTo(b);
            int samples = Math.Max(1, (int)Math.Ceiling(length / (step / 4)));
            for (int i = 0; i <= samples; i++)
            {
                double t = (double)i / samples;
                if (!layout.IsAdmissible(new BasePosition(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t), radius)) return false;
            }
            return true;
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    }
}
