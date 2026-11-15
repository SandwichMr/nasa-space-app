using System;

namespace Trainer.Application.Outpost
{
    /// <summary>
    /// Point on the base floor plane, in metres (immutable, finite). Top-down view: +X towards the right of
    /// the screen, +Y towards the top of the screen; origin at the layout origin. Recommended Unity mapping
    /// (decided by D2 in the scene): world X = X, world Y = Y, 1 Unity unit = 1 m, Z only for draw order.
    /// </summary>
    public readonly struct BasePosition : IEquatable<BasePosition>
    {
        public double X { get; }
        public double Y { get; }

        public BasePosition(double x, double y)
        {
            X = OutpostGuard.Finite(x, nameof(x));
            Y = OutpostGuard.Finite(y, nameof(y));
        }

        /// <summary>Euclidean distance in metres.</summary>
        public double DistanceTo(BasePosition other)
        {
            double dx = other.X - X, dy = other.Y - Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public bool Equals(BasePosition other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is BasePosition other && Equals(other);
        public override int GetHashCode() => unchecked(X.GetHashCode() * 397 ^ Y.GetHashCode());
        public override string ToString() => $"({X:0.###} m, {Y:0.###} m)";
    }

    /// <summary>Axis-aligned rectangle in metres (immutable): finite, Min strictly below Max on both axes.
    /// Edges belong to the rectangle.</summary>
    public readonly struct BaseRect : IEquatable<BaseRect>
    {
        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;

        public BaseRect(double minX, double minY, double maxX, double maxY)
        {
            MinX = OutpostGuard.Finite(minX, nameof(minX));
            MinY = OutpostGuard.Finite(minY, nameof(minY));
            MaxX = OutpostGuard.Finite(maxX, nameof(maxX));
            MaxY = OutpostGuard.Finite(maxY, nameof(maxY));
            if (!(minX < maxX) || !(minY < maxY)) throw new ArgumentException("Min must be below Max on both axes.");
        }

        public bool Contains(BasePosition p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;

        public bool Contains(BaseRect other) =>
            other.MinX >= MinX && other.MaxX <= MaxX && other.MinY >= MinY && other.MaxY <= MaxY;

        /// <summary>Distance in metres from <paramref name="p"/> to the rectangle (zero inside or on an edge).</summary>
        public double DistanceTo(BasePosition p)
        {
            double dx = Math.Max(Math.Max(MinX - p.X, 0d), p.X - MaxX);
            double dy = Math.Max(Math.Max(MinY - p.Y, 0d), p.Y - MaxY);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public bool Equals(BaseRect other) => MinX.Equals(other.MinX) && MinY.Equals(other.MinY)
            && MaxX.Equals(other.MaxX) && MaxY.Equals(other.MaxY);
        public override bool Equals(object obj) => obj is BaseRect other && Equals(other);
        public override int GetHashCode() =>
            unchecked(((MinX.GetHashCode() * 397 ^ MinY.GetHashCode()) * 397 ^ MaxX.GetHashCode()) * 397 ^ MaxY.GetHashCode());
    }

    /// <summary>
    /// Movement intent on the floor plane (immutable, unitless). Components finite in [-1, 1] and magnitude at
    /// most 1 (+<see cref="MagnitudeTolerance"/>). Zero means stop. The authority walks at
    /// <c>Magnitude * CrewMovementSettings.MaxSpeedMetersPerSecond</c>, so a diagonal is never faster than a
    /// straight line. Use <see cref="FromAxes"/> for raw input; the constructor rejects magnitudes above 1.
    /// </summary>
    public readonly struct MoveDirection : IEquatable<MoveDirection>
    {
        public const double MagnitudeTolerance = 1e-9;
        public static readonly MoveDirection Zero = default;

        public double X { get; }
        public double Y { get; }
        public double Magnitude => Math.Sqrt(X * X + Y * Y);
        public bool IsZero => X == 0d && Y == 0d;

        public MoveDirection(double x, double y)
        {
            if (!TryValidate(x, y, out string error)) throw new ArgumentOutOfRangeException(nameof(x), error);
            X = x;
            Y = y;
        }

        /// <summary>False (and <see cref="Zero"/>) for NaN, infinity, a component outside [-1, 1] or a magnitude
        /// above 1. For decoders: nothing is clamped or guessed.</summary>
        public static bool TryCreate(double x, double y, out MoveDirection value)
        {
            if (!TryValidate(x, y, out _))
            {
                value = Zero;
                return false;
            }
            value = new MoveDirection(x, y);
            return true;
        }

        /// <summary>Raw axes (keyboard, stick). Throws for NaN/infinity; a magnitude above 1 is scaled down to 1
        /// keeping the direction, so (1, 1) becomes (0.7071, 0.7071). Smaller magnitudes are kept (analog).</summary>
        public static MoveDirection FromAxes(double x, double y)
        {
            OutpostGuard.Finite(x, nameof(x));
            OutpostGuard.Finite(y, nameof(y));
            // Scale by the largest component first so huge finite axes cannot overflow the magnitude.
            double largest = Math.Max(Math.Abs(x), Math.Abs(y));
            if (largest > 1d)
            {
                x /= largest;
                y /= largest;
            }
            double magnitude = Math.Sqrt(x * x + y * y);
            if (magnitude <= 1d) return new MoveDirection(x, y);
            return new MoveDirection(Clamp(x / magnitude), Clamp(y / magnitude));
        }

        private static double Clamp(double value) => value > 1d ? 1d : value < -1d ? -1d : value;

        private static bool TryValidate(double x, double y, out string error)
        {
            error = null;
            if (!OutpostGuard.IsFinite(x) || !OutpostGuard.IsFinite(y)) error = "Components must be finite.";
            else if (Math.Abs(x) > 1d || Math.Abs(y) > 1d) error = "Components must be within [-1, 1].";
            else if (Math.Sqrt(x * x + y * y) > 1d + MagnitudeTolerance) error = "Magnitude must not exceed 1.";
            return error == null;
        }

        public bool Equals(MoveDirection other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is MoveDirection other && Equals(other);
        public override int GetHashCode() => unchecked(X.GetHashCode() * 397 ^ Y.GetHashCode());
        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }
}
