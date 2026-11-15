using System;
using System.Collections.Generic;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;

namespace Trainer.Unity.Validation.Phase04
{
    /// <summary>
    /// D5 phase 4 validation helper (pure C#, no Unity API): grid path planner over the scenario layout with BOTH hatches
    /// closed, so a scripted player stays inside the base. It only proposes held directions; the authority keeps moving
    /// and colliding the crew on its confirmed positions. The planner never sends a position.
    /// </summary>
    public sealed class Phase04Navigator
    {
        public const double Grid = 0.25;

        private readonly OutpostLayout layout;
        private readonly double radius;
        private readonly double minX, minY;
        private readonly int nx, ny;
        private readonly bool[] free;

        public Phase04Navigator(ExpeditionScenario scenario)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            layout = scenario.LayoutWithHatches(true, true);
            radius = scenario.Outpost.Movement.CrewRadiusMeters;
            // This pilot navigates the inhabited area; an extended exterior must not allocate a planet-sized grid.
            double maxX=0,maxY=0,minStationY=0;
            foreach(var station in layout.Stations)
            { maxX=Math.Max(maxX,station.Position.X);maxY=Math.Max(maxY,station.Position.Y);minStationY=Math.Min(minStationY,station.Position.Y); }
            BaseRect world=layout.Bounds;
            BaseRect bounds=new BaseRect(Math.Max(world.MinX,-4),Math.Max(world.MinY,minStationY-4),
                Math.Min(world.MaxX,maxX+4),Math.Min(world.MaxY,maxY+4));
            minX = bounds.MinX;
            minY = bounds.MinY;
            nx = (int)Math.Floor((bounds.MaxX - bounds.MinX) / Grid) + 1;
            ny = (int)Math.Floor((bounds.MaxY - bounds.MinY) / Grid) + 1;
            free = new bool[nx * ny];
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                    free[j * nx + i] = layout.IsAdmissible(Point(i, j), radius);
        }

        public OutpostLayout Layout => layout;
        public double CrewRadius => radius;

        public BasePosition Point(int i, int j) => new BasePosition(minX + i * Grid, minY + j * Grid);

        /// <summary>
        /// Waypoints from <paramref name="from"/> to a standing point within reach of <paramref name="station"/> (well inside
        /// its interaction radius), or null when none is reachable with the hatches closed.
        /// </summary>
        public List<BasePosition> PlanToStation(BasePosition from, StationId station)
        {
            if (!layout.TryGetStation(station, out StationPlacement placement)) return null;
            double reach = Math.Max(Grid, placement.InteractionRadiusMeters * 0.7);
            return Plan(from, p => p.DistanceTo(placement.Position) <= reach);
        }

        /// <summary>Waypoints (simplified grid path, last one is the goal cell) or null.</summary>
        public List<BasePosition> Plan(BasePosition from, Func<BasePosition, bool> isGoal)
        {
            if (isGoal == null) throw new ArgumentNullException(nameof(isGoal));
            int start = NearestFree(from);
            if (start < 0) return null;
            var previous = new int[nx * ny];
            for (int k = 0; k < previous.Length; k++) previous[k] = -2;
            var queue = new Queue<int>();
            previous[start] = -1;
            queue.Enqueue(start);
            int goal = -1;
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                if (isGoal(Point(cell % nx, cell / nx)))
                {
                    goal = cell;
                    break;
                }
                int ci = cell % nx, cj = cell / nx;
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        if (di == 0 && dj == 0) continue;
                        int i = ci + di, j = cj + dj;
                        if (!IsFree(i, j) || previous[j * nx + i] != -2) continue;
                        // A diagonal step only when both orthogonal neighbours are free (no corner cutting).
                        if (di != 0 && dj != 0 && (!IsFree(ci + di, cj) || !IsFree(ci, cj + dj))) continue;
                        previous[j * nx + i] = cell;
                        queue.Enqueue(j * nx + i);
                    }
            }
            if (goal < 0) return null;
            var cells = new List<int>();
            for (int c = goal; c >= 0; c = previous[c]) cells.Add(c);
            cells.Reverse();
            return Simplify(cells);
        }

        /// <summary>True when a free grid cell within reach of <paramref name="station"/> is connected to <paramref name="from"/>.</summary>
        public bool CanReach(BasePosition from, StationId station) => PlanToStation(from, station) != null;

        private bool IsFree(int i, int j) => i >= 0 && j >= 0 && i < nx && j < ny && free[j * nx + i];

        private int NearestFree(BasePosition p)
        {
            int ci = (int)Math.Round((p.X - minX) / Grid), cj = (int)Math.Round((p.Y - minY) / Grid);
            for (int ring = 0; ring <= 4; ring++)
            {
                int best = -1;
                double bestDistance = double.PositiveInfinity;
                for (int j = cj - ring; j <= cj + ring; j++)
                    for (int i = ci - ring; i <= ci + ring; i++)
                    {
                        if (!IsFree(i, j)) continue;
                        double d = Point(i, j).DistanceTo(p);
                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            best = j * nx + i;
                        }
                    }
                if (best >= 0) return best;
            }
            return -1;
        }

        /// <summary>Keeps a cell only where the straight segment from the last kept one would leave free cells.</summary>
        private List<BasePosition> Simplify(List<int> cells)
        {
            var points = new List<BasePosition>();
            if (cells.Count == 0) return points;
            int anchor = 0;
            for (int k = 2; k < cells.Count; k++)
                if (!StraightIsFree(cells[anchor], cells[k]))
                {
                    points.Add(Point(cells[k - 1] % nx, cells[k - 1] / nx));
                    anchor = k - 1;
                }
            int last = cells[cells.Count - 1];
            points.Add(Point(last % nx, last / nx));
            return points;
        }

        private bool StraightIsFree(int a, int b)
        {
            BasePosition pa = Point(a % nx, a / nx), pb = Point(b % nx, b / nx);
            double length = pa.DistanceTo(pb);
            int samples = Math.Max(1, (int)Math.Ceiling(length / (Grid * 0.5)));
            for (int s = 0; s <= samples; s++)
            {
                double t = (double)s / samples;
                var p = new BasePosition(pa.X + (pb.X - pa.X) * t, pa.Y + (pb.Y - pa.Y) * t);
                if (!layout.IsAdmissible(p, radius)) return false;
            }
            return true;
        }
    }
}
