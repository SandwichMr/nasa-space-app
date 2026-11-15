using System;

namespace Trainer.Simulation.LifeSupport
{
    /// <summary>
    /// Tracked mass stocks of the phase 2 base. Each kind is a different stock with its own meaning:
    /// potable water is not untreated water, and stored food mass already contains its own moisture
    /// (that moisture is never counted again as water).
    /// </summary>
    public enum ResourceKind
    {
        /// <summary>Water fit to drink, kilograms.</summary>
        PotableWater,
        /// <summary>Captured water waiting for treatment (not drinkable), kilograms.</summary>
        UntreatedWater,
        /// <summary>Packaged food as stowed, including its moisture, kilograms.</summary>
        StoredFood
    }

    internal static class LifeSupportGuard
    {
        public static bool IsNonNegativeFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;

        public static bool IsPositiveFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;

        public static double NonNegative(double value, string name)
        {
            if (!IsNonNegativeFinite(value))
                throw new ArgumentOutOfRangeException(name, value, "Must be finite and nonnegative.");
            return value;
        }

        public static double Positive(double value, string name)
        {
            if (!IsPositiveFinite(value))
                throw new ArgumentOutOfRangeException(name, value, "Must be finite and positive.");
            return value;
        }

        public static void WithinCapacity(double amount, double capacity, string name)
        {
            if (amount > capacity) throw new ArgumentException($"{name} exceeds its capacity.", name);
        }
    }

    /// <summary>
    /// Water ledger reading in kilograms (immutable). Amounts are finite, nonnegative and never above their
    /// capacity. Used both as scenario configuration (initial values and capacities) and as the stock
    /// published by the authority; only the authority's <see cref="IOutpostSimulation"/> changes the stock.
    /// </summary>
    public sealed class WaterStock
    {
        public double PotableKilograms { get; }
        public double UntreatedKilograms { get; }
        public double PotableCapacityKilograms { get; }
        public double UntreatedCapacityKilograms { get; }

        public WaterStock(double potableKilograms, double untreatedKilograms,
            double potableCapacityKilograms, double untreatedCapacityKilograms)
        {
            PotableKilograms = LifeSupportGuard.NonNegative(potableKilograms, nameof(potableKilograms));
            UntreatedKilograms = LifeSupportGuard.NonNegative(untreatedKilograms, nameof(untreatedKilograms));
            PotableCapacityKilograms = LifeSupportGuard.NonNegative(potableCapacityKilograms, nameof(potableCapacityKilograms));
            UntreatedCapacityKilograms = LifeSupportGuard.NonNegative(untreatedCapacityKilograms, nameof(untreatedCapacityKilograms));
            LifeSupportGuard.WithinCapacity(potableKilograms, potableCapacityKilograms, nameof(potableKilograms));
            LifeSupportGuard.WithinCapacity(untreatedKilograms, untreatedCapacityKilograms, nameof(untreatedKilograms));
        }
    }

    /// <summary>Stored food in kilograms as stowed (immutable). Finite, nonnegative, within capacity.
    /// Nutrients, food moisture and waste are not separate stocks in this base.</summary>
    public sealed class FoodStock
    {
        public double StoredKilograms { get; }
        public double CapacityKilograms { get; }

        public FoodStock(double storedKilograms, double capacityKilograms)
        {
            StoredKilograms = LifeSupportGuard.NonNegative(storedKilograms, nameof(storedKilograms));
            CapacityKilograms = LifeSupportGuard.NonNegative(capacityKilograms, nameof(capacityKilograms));
            LifeSupportGuard.WithinCapacity(storedKilograms, capacityKilograms, nameof(storedKilograms));
        }
    }
}
