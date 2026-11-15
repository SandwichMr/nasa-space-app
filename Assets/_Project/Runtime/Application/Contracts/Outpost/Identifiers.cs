using System;

namespace Trainer.Application.Outpost
{
    /// <summary>Size limits that bound every phase 2 snapshot and configuration. Constructors enforce them so
    /// wire encoders can rely on them. Raising one is a coordinated base change.</summary>
    public static class OutpostLimits
    {
        public const int MaxCrew = 8;
        public const int MaxStations = 16;
        public const int MaxTaskTypes = 32;
        public const int MaxObstacles = 64;
        public const int MaxObjectives = 8;
        public const int MaxKeyLength = 32;
    }

    /// <summary>Stable text keys used for scenario, station, task and objective ids: 1 to
    /// <see cref="OutpostLimits.MaxKeyLength"/> characters of a-z, 0-9 and '-', starting with a letter.
    /// The same value identifies the same thing in every build and on the wire.</summary>
    public static class StableKey
    {
        public static bool IsValid(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > OutpostLimits.MaxKeyLength) return false;
            if (value[0] < 'a' || value[0] > 'z') return false;
            foreach (char c in value)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')) return false;
            return true;
        }

        internal static string Require(string value, string name)
        {
            if (!IsValid(value)) throw new ArgumentException($"'{value}' is not a stable key.", name);
            return value;
        }
    }

    /// <summary>
    /// Crew member identity, assigned only by the session authority when a connection joins
    /// (<see cref="IOutpostAuthority.Join"/>). Value 1 or greater; <see cref="None"/> (0) means no player.
    /// Never carried in a client command: the host adapter binds it from the real connection. Not reused by
    /// the same authority after Leave, so a stale reference never aliases a newcomer.
    /// </summary>
    public readonly struct PlayerId : IEquatable<PlayerId>, IComparable<PlayerId>
    {
        public static readonly PlayerId None = default;

        public int Value { get; }
        public bool IsValid => Value >= 1;

        public PlayerId(int value)
        {
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(value), value, "Player ids start at 1.");
            Value = value;
        }

        public bool Equals(PlayerId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is PlayerId other && Equals(other);
        public override int GetHashCode() => Value;
        public int CompareTo(PlayerId other) => Value.CompareTo(other.Value);
        public override string ToString() => IsValid ? "P" + Value : "None";
        public static bool operator ==(PlayerId a, PlayerId b) => a.Equals(b);
        public static bool operator !=(PlayerId a, PlayerId b) => !a.Equals(b);
    }

    /// <summary>Stable station id (a <see cref="StableKey"/>), defined by the scenario layout.
    /// <c>default</c> is <see cref="None"/>.</summary>
    public readonly struct StationId : IEquatable<StationId>
    {
        public static readonly StationId None = default;
        private readonly string value;

        public StationId(string value) { this.value = StableKey.Require(value, nameof(value)); }

        public string Value => value ?? string.Empty;
        public bool IsValid => value != null;

        public bool Equals(StationId other) => string.Equals(value, other.value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is StationId other && Equals(other);
        public override int GetHashCode() => value == null ? 0 : StringComparer.Ordinal.GetHashCode(value);
        public override string ToString() => value ?? "None";
        public static bool operator ==(StationId a, StationId b) => a.Equals(b);
        public static bool operator !=(StationId a, StationId b) => !a.Equals(b);
    }

    /// <summary>Stable task type id (a <see cref="StableKey"/>), defined by the scenario.
    /// <c>default</c> is <see cref="None"/>.</summary>
    public readonly struct TaskTypeId : IEquatable<TaskTypeId>
    {
        public static readonly TaskTypeId None = default;
        private readonly string value;

        public TaskTypeId(string value) { this.value = StableKey.Require(value, nameof(value)); }

        public string Value => value ?? string.Empty;
        public bool IsValid => value != null;

        public bool Equals(TaskTypeId other) => string.Equals(value, other.value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is TaskTypeId other && Equals(other);
        public override int GetHashCode() => value == null ? 0 : StringComparer.Ordinal.GetHashCode(value);
        public override string ToString() => value ?? "None";
        public static bool operator ==(TaskTypeId a, TaskTypeId b) => a.Equals(b);
        public static bool operator !=(TaskTypeId a, TaskTypeId b) => !a.Equals(b);
    }

    internal static class OutpostGuard
    {
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public static double Finite(double value, string name)
        {
            if (!IsFinite(value)) throw new ArgumentOutOfRangeException(name, value, "Must be finite.");
            return value;
        }

        public static double NonNegative(double value, string name)
        {
            if (!IsFinite(value) || value < 0d)
                throw new ArgumentOutOfRangeException(name, value, "Must be finite and nonnegative.");
            return value;
        }

        public static double Positive(double value, string name)
        {
            if (!IsFinite(value) || value <= 0d)
                throw new ArgumentOutOfRangeException(name, value, "Must be finite and positive.");
            return value;
        }

        public static T Defined<T>(T value, string name) where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value)) throw new ArgumentOutOfRangeException(name, value, "Undefined value.");
            return value;
        }
    }
}
