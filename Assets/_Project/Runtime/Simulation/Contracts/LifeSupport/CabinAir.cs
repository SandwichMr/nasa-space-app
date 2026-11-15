using System;

namespace Trainer.Simulation.LifeSupport
{
    /// <summary>
    /// Declared cabin air configuration: ONE well-mixed zone (a stated simplification). Volume in cubic metres,
    /// gas temperature in kelvin, initial partial pressures in kilopascals. O2 and CO2 are separate
    /// quantities; total pressure and other gases are not modelled by this base. Values are scenario inputs
    /// with their own provenance, not universal rates or limits.
    /// </summary>
    public sealed class CabinConfiguration
    {
        public double FreeGasVolumeCubicMeters { get; }
        public double GasTemperatureKelvin { get; }
        public double InitialOxygenPartialPressureKilopascals { get; }
        public double InitialCarbonDioxidePartialPressureKilopascals { get; }

        public CabinConfiguration(double freeGasVolumeCubicMeters, double gasTemperatureKelvin,
            double initialOxygenPartialPressureKilopascals, double initialCarbonDioxidePartialPressureKilopascals)
        {
            FreeGasVolumeCubicMeters = LifeSupportGuard.Positive(freeGasVolumeCubicMeters, nameof(freeGasVolumeCubicMeters));
            GasTemperatureKelvin = LifeSupportGuard.Positive(gasTemperatureKelvin, nameof(gasTemperatureKelvin));
            InitialOxygenPartialPressureKilopascals = LifeSupportGuard.NonNegative(
                initialOxygenPartialPressureKilopascals, nameof(initialOxygenPartialPressureKilopascals));
            InitialCarbonDioxidePartialPressureKilopascals = LifeSupportGuard.NonNegative(
                initialCarbonDioxidePartialPressureKilopascals, nameof(initialCarbonDioxidePartialPressureKilopascals));
        }
    }

    /// <summary>Quality of one instrument reading. A missing or stale reading is never zero or normal.</summary>
    public enum ReadingStatus
    {
        /// <summary>Current reading taken at <see cref="GasReading.ObservedAtSeconds"/>.</summary>
        Valid,
        /// <summary>Last valid value; the instrument has not refreshed it since ObservedAtSeconds.</summary>
        Stale,
        /// <summary>No value available. Value and time are null.</summary>
        Unavailable
    }

    /// <summary>
    /// One instrument reading of a partial pressure in kilopascals (immutable). Valid/Stale carry a finite,
    /// nonnegative value and the simulated second it was measured; Unavailable carries neither. This is what
    /// the crew can see, kept separate from the model's physical state.
    /// </summary>
    public sealed class GasReading
    {
        public static readonly GasReading Unavailable = new GasReading(ReadingStatus.Unavailable, null, null);

        public ReadingStatus Status { get; }
        public double? PartialPressureKilopascals { get; }
        public double? ObservedAtSeconds { get; }

        public GasReading(ReadingStatus status, double? partialPressureKilopascals, double? observedAtSeconds)
        {
            if (!Enum.IsDefined(typeof(ReadingStatus), status)) throw new ArgumentOutOfRangeException(nameof(status));
            bool hasValue = partialPressureKilopascals.HasValue, hasTime = observedAtSeconds.HasValue;
            if (status == ReadingStatus.Unavailable)
            {
                if (hasValue || hasTime) throw new ArgumentException("An unavailable reading has no value or time.");
            }
            else
            {
                if (!hasValue || !hasTime) throw new ArgumentException("A valid or stale reading needs a value and a time.");
                LifeSupportGuard.NonNegative(partialPressureKilopascals.Value, nameof(partialPressureKilopascals));
                LifeSupportGuard.NonNegative(observedAtSeconds.Value, nameof(observedAtSeconds));
            }
            Status = status;
            PartialPressureKilopascals = partialPressureKilopascals;
            ObservedAtSeconds = observedAtSeconds;
        }

        public static GasReading Valid(double partialPressureKilopascals, double observedAtSeconds) =>
            new GasReading(ReadingStatus.Valid, partialPressureKilopascals, observedAtSeconds);
    }

    /// <summary>Cabin instrument readings published to players: oxygen and carbon dioxide are separate
    /// readings with independent status. No alarm, clinical or safety threshold is part of this contract.</summary>
    public sealed class CabinAirObservation
    {
        public GasReading Oxygen { get; }
        public GasReading CarbonDioxide { get; }

        public CabinAirObservation(GasReading oxygen, GasReading carbonDioxide)
        {
            Oxygen = oxygen ?? throw new ArgumentNullException(nameof(oxygen));
            CarbonDioxide = carbonDioxide ?? throw new ArgumentNullException(nameof(carbonDioxide));
        }
    }
}
