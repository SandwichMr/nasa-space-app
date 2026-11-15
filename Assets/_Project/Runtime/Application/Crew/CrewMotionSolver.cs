using System;
using Trainer.Application.Outpost;

namespace Trainer.Application.Crew
{
    /// <summary>
    /// Authoritative movement of one crew disc against the scenario floor plan (plain C#, deterministic).
    /// The ONLY collision resolution of phase 2: the result always satisfies
    /// <see cref="OutpostLayout.IsAdmissible"/> when the start does, and the path length never exceeds the
    /// requested distance (no teleport, a diagonal is not faster). Blocked moves approach the obstacle and then
    /// slide along the free axis with whatever length is left, so players do not stick to walls.
    /// </summary>
    public static class CrewMotionSolver
    {
        /// <summary>Longest sub-step as a fraction of the crew radius, so a move cannot jump over an obstacle.</summary>
        public const double SubstepRadiusFraction = 0.25;
        private const int ApproachIterations = 16;

        /// <summary>
        /// Moves from <paramref name="from"/> along <paramref name="direction"/> for up to
        /// <paramref name="distanceMeters"/> (finite, nonnegative). A zero direction or distance returns
        /// <paramref name="from"/>. Throws if <paramref name="from"/> is not admissible: the caller's invariant is broken.
        /// </summary>
        public static BasePosition Move(OutpostLayout layout, double crewRadiusMeters, BasePosition from,
            MoveDirection direction, double distanceMeters)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (double.IsNaN(distanceMeters) || double.IsInfinity(distanceMeters) || distanceMeters < 0)
                throw new ArgumentOutOfRangeException(nameof(distanceMeters));
            if (!layout.IsAdmissible(from, crewRadiusMeters))
                throw new InvalidOperationException($"Start position {from} is not admissible.");
            double magnitude = direction.Magnitude;
            if (magnitude == 0d || distanceMeters == 0d) return from;

            // Travel beyond twice the map perimeter cannot matter and would only cost iterations.
            double limit = 4d * (layout.Bounds.Width + layout.Bounds.Height);
            double remaining = Math.Min(distanceMeters, limit);
            double ux = direction.X / magnitude, uy = direction.Y / magnitude;
            double substep = crewRadiusMeters * SubstepRadiusFraction;
            double x = from.X, y = from.Y;

            while (remaining > 0d)
            {
                double length = Math.Min(substep, remaining);
                remaining -= length;
                double dx = ux * length, dy = uy * length;
                if (Admissible(layout, crewRadiusMeters, x + dx, y + dy))
                {
                    x += dx;
                    y += dy;
                    continue;
                }
                // Blocked: get as close as possible along the move, then slide on one axis with what is left.
                double t = Approach(layout, crewRadiusMeters, x, y, dx, dy);
                x += dx * t;
                y += dy * t;
                double left = length * (1d - t);
                bool preferX = Math.Abs(dx) >= Math.Abs(dy);
                bool slid = TrySlide(layout, crewRadiusMeters, ref x, ref y, preferX, Math.Sign(preferX ? dx : dy), left)
                    || TrySlide(layout, crewRadiusMeters, ref x, ref y, !preferX, Math.Sign(preferX ? dy : dx), left);
                if (!slid) break; // Wedged: nothing more can move this step.
            }
            return new BasePosition(x, y);
        }

        /// <summary>Slides on one axis by at most the leftover length, so the sub-step path stays within its length.</summary>
        private static bool TrySlide(OutpostLayout layout, double r, ref double x, ref double y, bool alongX,
            int sign, double leftover)
        {
            if (sign == 0 || leftover <= 0d) return false;
            double dx = alongX ? sign * leftover : 0d, dy = alongX ? 0d : sign * leftover;
            double t = Admissible(layout, r, x + dx, y + dy) ? 1d : Approach(layout, r, x, y, dx, dy);
            if (t <= 0d) return false;
            x += dx * t;
            y += dy * t;
            return true;
        }

        /// <summary>Largest fraction of (dx, dy) that stays admissible, by bisection (start is admissible).</summary>
        private static double Approach(OutpostLayout layout, double r, double x, double y, double dx, double dy)
        {
            double low = 0d, high = 1d;
            for (int i = 0; i < ApproachIterations; i++)
            {
                double mid = 0.5 * (low + high);
                if (Admissible(layout, r, x + dx * mid, y + dy * mid)) low = mid;
                else high = mid;
            }
            return low;
        }

        private static bool Admissible(OutpostLayout layout, double r, double x, double y) =>
            layout.IsAdmissible(new BasePosition(x, y), r);
    }
}
