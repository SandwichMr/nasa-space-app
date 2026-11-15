using System;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Application.BaseOperations
{
    /// <summary>The functional bars of the phase 4 HUD. Each one has a unit, a cause (model), a consequence and a player action.</summary>
    public enum GaugeKind
    {
        /// <summary>Battery energy, J (phase 1 power model). Action: reduce loads, wait for generation.</summary>
        BatteryEnergy,
        /// <summary>Generation minus requested load, W (negative = drawing the battery).</summary>
        PowerBalance,
        /// <summary>Potable water, kg. Action: process and route batches, resupply.</summary>
        PotableWater,
        /// <summary>Untreated water waiting for treatment, kg (detail, not drinkable).</summary>
        UntreatedWater,
        /// <summary>Product held in the processor awaiting a routing decision, kg (detail).</summary>
        ProcessorHold,
        /// <summary>Stored food, kg as stowed. Only resupply adds food in phase 4 (no crops).</summary>
        StoredFood,
        /// <summary>Cabin O2 partial pressure, kPa (instrument reading).</summary>
        CabinOxygen,
        /// <summary>Cabin CO2 partial pressure, kPa (instrument reading). Action: air service.</summary>
        CabinCarbonDioxide
    }

    /// <summary>
    /// Band of a gauge. Separate from equipment state on purpose: <see cref="Reserve"/> is a LOW STOCK (scenario reserve
    /// line), never "machine degraded" or "machine failed", which are equipment knowledge. <see cref="Unknown"/> is never
    /// zero or normal.
    /// </summary>
    public enum GaugeBand
    {
        Unknown,
        Nominal,
        /// <summary>At or below the scenario reserve line (low-is-bad gauges).</summary>
        Reserve,
        /// <summary>The stock is empty (value 0).</summary>
        Exhausted,
        /// <summary>Above the scenario limit line (CO2). A scenario line, not a clinical threshold.</summary>
        AboveLimit,
        /// <summary>Power balance negative: loads exceed generation and the battery is drawn.</summary>
        Deficit
    }

    /// <summary>Scenario lines of one gauge (configuration, immutable, SYNTHETIC values with the scenario provenance).</summary>
    public sealed class GaugeRule
    {
        public GaugeKind Kind { get; }
        /// <summary>Low-is-bad reserve line in the gauge unit, or null.</summary>
        public double? ReserveAt { get; }
        /// <summary>High-is-bad limit line in the gauge unit, or null.</summary>
        public double? LimitAt { get; }

        public GaugeRule(GaugeKind kind, double? reserveAt, double? limitAt)
        {
            Kind = OutpostGuard.Defined(kind, nameof(kind));
            if (reserveAt.HasValue) OutpostGuard.NonNegative(reserveAt.Value, nameof(reserveAt));
            if (limitAt.HasValue) OutpostGuard.Positive(limitAt.Value, nameof(limitAt));
            if (kind == GaugeKind.PowerBalance && (reserveAt.HasValue || limitAt.HasValue))
                throw new ArgumentException("The power balance band is its sign; it takes no lines.");
            if (kind == GaugeKind.CabinCarbonDioxide && reserveAt.HasValue)
                throw new ArgumentException("CO2 is high-is-bad: use LimitAt.", nameof(reserveAt));
            if (kind != GaugeKind.CabinCarbonDioxide && limitAt.HasValue)
                throw new ArgumentException("Only CO2 takes a limit line.", nameof(limitAt));
            ReserveAt = reserveAt;
            LimitAt = limitAt;
        }
    }

    /// <summary>The single classification rule shared by the authority (D2), views (D4) and validators (D5).</summary>
    public static class GaugeBands
    {
        public static ReadingUnit UnitOf(GaugeKind kind)
        {
            switch (OutpostGuard.Defined(kind, nameof(kind)))
            {
                case GaugeKind.BatteryEnergy: return ReadingUnit.Joules;
                case GaugeKind.PowerBalance: return ReadingUnit.Watts;
                case GaugeKind.CabinOxygen:
                case GaugeKind.CabinCarbonDioxide: return ReadingUnit.Kilopascals;
                default: return ReadingUnit.Kilograms;
            }
        }

        /// <summary>Unavailable → Unknown. Stale readings are classified from their last value (the gauge still says Stale).
        /// Order: Deficit (power sign) → AboveLimit → Exhausted (stocks at 0) → Reserve → Nominal.</summary>
        public static GaugeBand Classify(GaugeKind kind, InstrumentReading reading, double? reserveAt, double? limitAt)
        {
            if (reading == null) throw new ArgumentNullException(nameof(reading));
            if (reading.Status == ReadingStatus.Unavailable) return GaugeBand.Unknown;
            double value = reading.Value.Value;
            if (kind == GaugeKind.PowerBalance) return value < 0d ? GaugeBand.Deficit : GaugeBand.Nominal;
            if (limitAt.HasValue && value > limitAt.Value) return GaugeBand.AboveLimit;
            bool stock = kind != GaugeKind.CabinCarbonDioxide && kind != GaugeKind.CabinOxygen && kind != GaugeKind.ProcessorHold;
            if (stock && value <= 0d) return GaugeBand.Exhausted;
            if (reserveAt.HasValue && value <= reserveAt.Value) return GaugeBand.Reserve;
            return GaugeBand.Nominal;
        }
    }

    /// <summary>
    /// One published bar (immutable). The reading carries status and time; capacity is null for readings without one
    /// (power balance, partial pressures). The trend is the authority's rate of change over its last step, in unit/s,
    /// or null when unknown. The constructor enforces <see cref="GaugeBands.Classify"/> with the copied lines.
    /// </summary>
    public sealed class ResourceGauge
    {
        public GaugeKind Kind { get; }
        public InstrumentReading Reading { get; }
        public double? Capacity { get; }
        public double? TrendPerSecond { get; }
        public double? ReserveAt { get; }
        public double? LimitAt { get; }
        public GaugeBand Band { get; }

        public ResourceGauge(GaugeKind kind, InstrumentReading reading, double? capacity, double? trendPerSecond,
            double? reserveAt, double? limitAt, GaugeBand band)
        {
            Kind = OutpostGuard.Defined(kind, nameof(kind));
            Reading = reading ?? throw new ArgumentNullException(nameof(reading));
            if (reading.Unit != GaugeBands.UnitOf(kind)) throw new ArgumentException("Reading unit does not match the gauge.", nameof(reading));
            if (capacity.HasValue)
            {
                OutpostGuard.NonNegative(capacity.Value, nameof(capacity));
                if (reading.Value.HasValue && reading.Value.Value > capacity.Value + 1e-9)
                    throw new ArgumentException("Value above capacity.", nameof(capacity));
            }
            if (trendPerSecond.HasValue) OutpostGuard.Finite(trendPerSecond.Value, nameof(trendPerSecond));
            var rule = new GaugeRule(kind, reserveAt, limitAt);
            Band = OutpostGuard.Defined(band, nameof(band));
            if (band != GaugeBands.Classify(kind, reading, rule.ReserveAt, rule.LimitAt))
                throw new ArgumentException("Band does not follow GaugeBands.Classify.", nameof(band));
            Capacity = capacity;
            TrendPerSecond = trendPerSecond;
            ReserveAt = reserveAt;
            LimitAt = limitAt;
        }

        /// <summary>Builds the gauge with the band computed by the shared rule.</summary>
        public static ResourceGauge Of(GaugeRule rule, InstrumentReading reading, double? capacity, double? trendPerSecond)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            return new ResourceGauge(rule.Kind, reading, capacity, trendPerSecond, rule.ReserveAt, rule.LimitAt,
                GaugeBands.Classify(rule.Kind, reading, rule.ReserveAt, rule.LimitAt));
        }
    }
}
