using System;
using System.Collections.Generic;
using Trainer.Application.Outpost;

namespace Trainer.Application.Expedition
{
    /// <summary>
    /// Walking distance from any exterior point to the airlock's exterior entry (D2, computed once per scenario).
    /// A grid of <see cref="CellMeters"/> over the Exterior zone keeps the cells whose centre is admissible for the
    /// crew disc on the plan; Dijkstra from the entry gives each cell the length of an 8-connected path (diagonals only
    /// when both neighbours are free, so no corner is cut). A member's route = nearest reachable cell's distance + the
    /// straight segment to its centre, which is never shorter than the straight line to the entry. It is a planning
    /// estimate for the return margin, not the movement solver: the roster still decides every confirmed position.
    /// </summary>
    public sealed class ReturnRouteField
    {
        public const double CellMeters = 0.25;

        private readonly double minX, minY;
        private readonly int nx, ny;
        private readonly double[] distance;
        private readonly BasePosition entry;

        public int ReachableCells { get; }

        public ReturnRouteField(ExpeditionScenario scenario)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            OutpostLayout layout = scenario.Layout;
            double radius = scenario.Outpost.Movement.CrewRadiusMeters;
            entry = scenario.Airlock.ExteriorEntry;

            double x0 = double.PositiveInfinity, y0 = double.PositiveInfinity, x1 = double.NegativeInfinity, y1 = double.NegativeInfinity;
            foreach (ZoneRegion region in scenario.Zones)
                if (region.Zone == CrewZone.Exterior)
                {
                    x0 = Math.Min(x0, region.Area.MinX); y0 = Math.Min(y0, region.Area.MinY);
                    x1 = Math.Max(x1, region.Area.MaxX); y1 = Math.Max(y1, region.Area.MaxY);
                }
            x0 = Math.Max(x0, layout.Bounds.MinX); y0 = Math.Max(y0, layout.Bounds.MinY);
            x1 = Math.Min(x1, layout.Bounds.MaxX); y1 = Math.Min(y1, layout.Bounds.MaxY);
            minX = x0;
            minY = y0;
            nx = Math.Max(1, (int)Math.Ceiling((x1 - x0) / CellMeters));
            ny = Math.Max(1, (int)Math.Ceiling((y1 - y0) / CellMeters));
            distance = new double[nx * ny];
            var free = new bool[nx * ny];
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    BasePosition c = Center(i, j);
                    free[j * nx + i] = layout.Bounds.Contains(c) && layout.IsAdmissible(c, radius)
                        && scenario.TryGetZone(c, out CrewZone zone) && zone == CrewZone.Exterior;
                    distance[j * nx + i] = double.PositiveInfinity;
                }

            if (!TryCell(entry, out int ei, out int ej)) throw new ArgumentException("The exterior entry is outside the route grid.", nameof(scenario));
            var heap = new MinHeap(nx * ny);
            // Seed every free cell around the entry with its straight distance to the entry point.
            for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    int i = ei + di, j = ej + dj;
                    if (i < 0 || j < 0 || i >= nx || j >= ny || !free[j * nx + i]) continue;
                    double d = Center(i, j).DistanceTo(entry);
                    if (d < distance[j * nx + i]) { distance[j * nx + i] = d; heap.Push(j * nx + i, d); }
                }
            double diagonal = CellMeters * Math.Sqrt(2d);
            while (heap.TryPop(out int index, out double d))
            {
                if (d > distance[index]) continue;
                int ci = index % nx, cj = index / nx;
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        if (di == 0 && dj == 0) continue;
                        int i = ci + di, j = cj + dj;
                        if (i < 0 || j < 0 || i >= nx || j >= ny || !free[j * nx + i]) continue;
                        if (di != 0 && dj != 0 && (!free[cj * nx + i] || !free[j * nx + ci])) continue; // no corner cutting
                        double next = d + (di != 0 && dj != 0 ? diagonal : CellMeters);
                        if (next < distance[j * nx + i])
                        {
                            distance[j * nx + i] = next;
                            heap.Push(j * nx + i, next);
                        }
                    }
            }
            int reachable = 0;
            foreach (double d in distance) if (!double.IsInfinity(d)) reachable++;
            ReachableCells = reachable;
        }

        /// <summary>Route length in metres from <paramref name="position"/> to the entry; false when no reachable cell is
        /// next to the position (it is not on the exterior route network).</summary>
        public bool TryGetPathMeters(BasePosition position, out double meters)
        {
            meters = double.PositiveInfinity;
            if (!TryCell(position, out int ci, out int cj)) return false;
            for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    int i = ci + di, j = cj + dj;
                    if (i < 0 || j < 0 || i >= nx || j >= ny) continue;
                    double d = distance[j * nx + i];
                    if (double.IsInfinity(d)) continue;
                    meters = Math.Min(meters, d + Center(i, j).DistanceTo(position));
                }
            if (double.IsInfinity(meters)) return false;
            meters = Math.Max(meters, position.DistanceTo(entry)); // never shorter than the straight line
            return true;
        }

        private BasePosition Center(int i, int j) => new BasePosition(minX + (i + 0.5) * CellMeters, minY + (j + 0.5) * CellMeters);

        private bool TryCell(BasePosition p, out int i, out int j)
        {
            i = (int)Math.Floor((p.X - minX) / CellMeters);
            j = (int)Math.Floor((p.Y - minY) / CellMeters);
            if (i == nx && p.X <= minX + nx * CellMeters) i = nx - 1;
            if (j == ny && p.Y <= minY + ny * CellMeters) j = ny - 1;
            return i >= 0 && j >= 0 && i < nx && j < ny;
        }

        /// <summary>Binary min-heap of (index, priority) with lazy deletion (netstandard 2.1 has no PriorityQueue).</summary>
        private sealed class MinHeap
        {
            private readonly List<(int index, double priority)> items;

            public MinHeap(int capacity) => items = new List<(int, double)>(Math.Min(capacity, 1 << 16));

            public void Push(int index, double priority)
            {
                items.Add((index, priority));
                int c = items.Count - 1;
                while (c > 0)
                {
                    int p = (c - 1) / 2;
                    if (items[p].priority <= items[c].priority) break;
                    (items[p], items[c]) = (items[c], items[p]);
                    c = p;
                }
            }

            public bool TryPop(out int index, out double priority)
            {
                if (items.Count == 0)
                {
                    index = -1;
                    priority = 0;
                    return false;
                }
                (index, priority) = items[0];
                int last = items.Count - 1;
                items[0] = items[last];
                items.RemoveAt(last);
                int c = 0;
                while (true)
                {
                    int l = 2 * c + 1, r = l + 1, m = c;
                    if (l < items.Count && items[l].priority < items[m].priority) m = l;
                    if (r < items.Count && items[r].priority < items[m].priority) m = r;
                    if (m == c) break;
                    (items[m], items[c]) = (items[c], items[m]);
                    c = m;
                }
                return true;
            }
        }
    }
}
