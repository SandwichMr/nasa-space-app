using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Simulation.BaseOperations
{
    /// <summary>The two maintained machines of phase 4. Each one sits at an existing phase 2 station.</summary>
    public enum EquipmentKind
    {
        /// <summary>Water processor at <c>water-recycler</c>: untreated water in batches, product held for a quality check.</summary>
        WaterProcessor,
        /// <summary>Air revitalization unit at <c>air-scrubber</c>: CO2 removal with two swing sorbent beds and an inlet filter.</summary>
        AirRevitalization
    }

    /// <summary>
    /// Physical condition of a machine (model truth, never sent to clients as such). It is NOT a stock reserve:
    /// a low stock is a gauge band; Degraded means the machine still works with worse output; Failed means it cannot
    /// perform its function until repaired.
    /// </summary>
    public enum EquipmentCondition { Nominal, Degraded, Failed }

    /// <summary>Operating state decided by the session authority through the repair sequence. The model never changes it
    /// by itself. An isolated machine performs no function and takes no new batch.</summary>
    public enum EquipmentOperatingState { Online, Isolated }

    /// <summary>The single synthetic, reproducible fault of phase 4. No MTBF, statistics or real failure data.</summary>
    public enum EquipmentFault
    {
        None,
        /// <summary>Water processor filter restricted (scripted by <see cref="WaterProcessorConfiguration.DegradeAtSeconds"/>).</summary>
        ProcessorFilterRestricted
    }

    public enum SorbentBed { A, B }

    /// <summary>Unit of an <see cref="InstrumentReading"/>.</summary>
    public enum ReadingUnit
    {
        Kilograms,
        Joules,
        Watts,
        Kilopascals,
        /// <summary>Electrical conductivity of a water sample, µS/cm (a quality proxy, not a health index).</summary>
        MicrosiemensPerCentimeter,
        /// <summary>Dimensionless value in [0, 1] (bed loading, inlet flow relative to nominal).</summary>
        Fraction
    }

    internal static class EquipmentGuard
    {
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public static double NonNegative(double value, string name)
        {
            if (!IsFinite(value) || value < 0d) throw new ArgumentOutOfRangeException(name, value, "Must be finite and nonnegative.");
            return value;
        }

        public static double Positive(double value, string name)
        {
            if (!IsFinite(value) || value <= 0d) throw new ArgumentOutOfRangeException(name, value, "Must be finite and positive.");
            return value;
        }

        public static double Fraction(double value, string name)
        {
            if (!IsFinite(value) || value < 0d || value > 1d) throw new ArgumentOutOfRangeException(name, value, "Must be within [0, 1].");
            return value;
        }

        public static T Defined<T>(T value, string name) where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value)) throw new ArgumentOutOfRangeException(name, value, "Undefined value.");
            return value;
        }

        public static string Key(string value, string name)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 32 || value[0] < 'a' || value[0] > 'z')
                throw new ArgumentException($"'{value}' is not a stable key.", name);
            foreach (char c in value)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-'))
                    throw new ArgumentException($"'{value}' is not a stable key.", name);
            return value;
        }

        public static string Provenance(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Provenance is required.", name);
            return value;
        }
    }

    /// <summary>
    /// One instrument value as the crew can see it (immutable). Same quality rules as the phase 2
    /// <see cref="GasReading"/>: Valid/Stale carry a finite value and the simulated second it was measured, Unavailable
    /// carries neither. A missing or stale reading is never zero or normal.
    /// </summary>
    public sealed class InstrumentReading
    {
        public ReadingStatus Status { get; }
        public ReadingUnit Unit { get; }
        public double? Value { get; }
        public double? ObservedAtSeconds { get; }

        public InstrumentReading(ReadingStatus status, ReadingUnit unit, double? value, double? observedAtSeconds)
        {
            Status = EquipmentGuard.Defined(status, nameof(status));
            Unit = EquipmentGuard.Defined(unit, nameof(unit));
            if (status == ReadingStatus.Unavailable)
            {
                if (value.HasValue || observedAtSeconds.HasValue) throw new ArgumentException("An unavailable reading has no value or time.");
            }
            else
            {
                if (!value.HasValue || !observedAtSeconds.HasValue) throw new ArgumentException("A valid or stale reading needs a value and a time.");
                if (!EquipmentGuard.IsFinite(value.Value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (unit == ReadingUnit.Fraction) EquipmentGuard.Fraction(value.Value, nameof(value));
                else if (unit != ReadingUnit.Watts) EquipmentGuard.NonNegative(value.Value, nameof(value));
                EquipmentGuard.NonNegative(observedAtSeconds.Value, nameof(observedAtSeconds));
            }
            Value = value;
            ObservedAtSeconds = observedAtSeconds;
        }

        public static InstrumentReading Valid(ReadingUnit unit, double value, double observedAtSeconds) =>
            new InstrumentReading(ReadingStatus.Valid, unit, value, observedAtSeconds);

        public static InstrumentReading Unavailable(ReadingUnit unit) =>
            new InstrumentReading(ReadingStatus.Unavailable, unit, null, null);

        /// <summary>The same value marked Stale (Unavailable stays Unavailable).</summary>
        public InstrumentReading AsStale() =>
            Status == ReadingStatus.Unavailable ? this : new InstrumentReading(ReadingStatus.Stale, Unit, Value, ObservedAtSeconds);
    }

    /// <summary>
    /// Water processor parameters (configuration, immutable, SYNTHETIC). A batch of untreated water becomes product
    /// held in the processor (recovery fraction) and brine that leaves the boundary. The product conductivity depends
    /// on the condition; <see cref="ProductSpecLimitMicrosiemens"/> is the scenario's acceptance limit for routing to
    /// the potable tank, NOT a health threshold.
    /// </summary>
    public sealed class WaterProcessorConfiguration
    {
        public double BatchKilograms { get; }
        public double HoldCapacityKilograms { get; }
        public double NominalRecoveryFraction { get; }
        public double DegradedRecoveryFraction { get; }
        public double NominalConductivityMicrosiemens { get; }
        public double DegradedConductivityMicrosiemens { get; }
        public double ProductSpecLimitMicrosiemens { get; }
        /// <summary>Scripted simulated second at which the filter becomes restricted (Degraded); null = never.</summary>
        public double? DegradeAtSeconds { get; }
        /// <summary>Seconds after degradation, without repair, until the processor is Failed; null = never fails.</summary>
        public double? FailAfterDegradedSeconds { get; }
        /// <summary>Spare consumed by the repair intervention.</summary>
        public string FilterSpareKey { get; }

        public WaterProcessorConfiguration(double batchKilograms, double holdCapacityKilograms, double nominalRecoveryFraction,
            double degradedRecoveryFraction, double nominalConductivityMicrosiemens, double degradedConductivityMicrosiemens,
            double productSpecLimitMicrosiemens, double? degradeAtSeconds, double? failAfterDegradedSeconds, string filterSpareKey)
        {
            BatchKilograms = EquipmentGuard.Positive(batchKilograms, nameof(batchKilograms));
            HoldCapacityKilograms = EquipmentGuard.Positive(holdCapacityKilograms, nameof(holdCapacityKilograms));
            if (holdCapacityKilograms < batchKilograms) throw new ArgumentException("The hold must fit one batch of product.", nameof(holdCapacityKilograms));
            NominalRecoveryFraction = EquipmentGuard.Fraction(nominalRecoveryFraction, nameof(nominalRecoveryFraction));
            DegradedRecoveryFraction = EquipmentGuard.Fraction(degradedRecoveryFraction, nameof(degradedRecoveryFraction));
            if (nominalRecoveryFraction <= 0d || degradedRecoveryFraction > nominalRecoveryFraction)
                throw new ArgumentException("0 < degraded recovery <= nominal recovery <= 1.", nameof(degradedRecoveryFraction));
            NominalConductivityMicrosiemens = EquipmentGuard.NonNegative(nominalConductivityMicrosiemens, nameof(nominalConductivityMicrosiemens));
            DegradedConductivityMicrosiemens = EquipmentGuard.NonNegative(degradedConductivityMicrosiemens, nameof(degradedConductivityMicrosiemens));
            ProductSpecLimitMicrosiemens = EquipmentGuard.Positive(productSpecLimitMicrosiemens, nameof(productSpecLimitMicrosiemens));
            if (nominalConductivityMicrosiemens > productSpecLimitMicrosiemens || degradedConductivityMicrosiemens <= productSpecLimitMicrosiemens)
                throw new ArgumentException("Nominal product must be within the limit and degraded product above it (the decision must be observable).");
            if (degradeAtSeconds.HasValue) EquipmentGuard.NonNegative(degradeAtSeconds.Value, nameof(degradeAtSeconds));
            if (failAfterDegradedSeconds.HasValue) EquipmentGuard.Positive(failAfterDegradedSeconds.Value, nameof(failAfterDegradedSeconds));
            DegradeAtSeconds = degradeAtSeconds;
            FailAfterDegradedSeconds = failAfterDegradedSeconds;
            FilterSpareKey = EquipmentGuard.Key(filterSpareKey, nameof(filterSpareKey));
        }
    }

    /// <summary>
    /// Air revitalization unit parameters (configuration, immutable, SYNTHETIC). The active bed loads while it removes
    /// CO2 (per crew member aboard); above <see cref="KneeLoadingFraction"/> its removal factor falls linearly to
    /// <see cref="SaturatedRemovalFactor"/> at full loading. The removal factor applied to the base model is
    /// bed factor × inlet flow fraction. A standby bed is regenerated by an operation (energy and time).
    /// </summary>
    public sealed class AirRevitalizationConfiguration
    {
        public double LoadingPerCrewSecond { get; }
        public double KneeLoadingFraction { get; }
        public double SaturatedRemovalFactor { get; }
        public double InitialActiveLoadingFraction { get; }
        public double InitialStandbyLoadingFraction { get; }
        /// <summary>Scripted second at which the inlet filter restricts flow; null = never (the default scenario).</summary>
        public double? InletRestrictedAtSeconds { get; }
        public double RestrictedFlowFraction { get; }
        public string InletFilterSpareKey { get; }

        public AirRevitalizationConfiguration(double loadingPerCrewSecond, double kneeLoadingFraction, double saturatedRemovalFactor,
            double initialActiveLoadingFraction, double initialStandbyLoadingFraction, double? inletRestrictedAtSeconds,
            double restrictedFlowFraction, string inletFilterSpareKey)
        {
            LoadingPerCrewSecond = EquipmentGuard.NonNegative(loadingPerCrewSecond, nameof(loadingPerCrewSecond));
            KneeLoadingFraction = EquipmentGuard.Fraction(kneeLoadingFraction, nameof(kneeLoadingFraction));
            if (kneeLoadingFraction >= 1d) throw new ArgumentOutOfRangeException(nameof(kneeLoadingFraction), "The knee must be below full loading.");
            SaturatedRemovalFactor = EquipmentGuard.Fraction(saturatedRemovalFactor, nameof(saturatedRemovalFactor));
            InitialActiveLoadingFraction = EquipmentGuard.Fraction(initialActiveLoadingFraction, nameof(initialActiveLoadingFraction));
            InitialStandbyLoadingFraction = EquipmentGuard.Fraction(initialStandbyLoadingFraction, nameof(initialStandbyLoadingFraction));
            if (inletRestrictedAtSeconds.HasValue) EquipmentGuard.NonNegative(inletRestrictedAtSeconds.Value, nameof(inletRestrictedAtSeconds));
            InletRestrictedAtSeconds = inletRestrictedAtSeconds;
            RestrictedFlowFraction = EquipmentGuard.Fraction(restrictedFlowFraction, nameof(restrictedFlowFraction));
            if (restrictedFlowFraction <= 0d || restrictedFlowFraction >= 1d)
                throw new ArgumentOutOfRangeException(nameof(restrictedFlowFraction), "A restriction keeps some flow and is below nominal.");
            InletFilterSpareKey = EquipmentGuard.Key(inletFilterSpareKey, nameof(inletFilterSpareKey));
        }

        /// <summary>Removal factor of a bed at <paramref name="loading"/> (the single rule shared by D1, D2 and D5).</summary>
        public double BedFactor(double loading)
        {
            EquipmentGuard.Fraction(loading, nameof(loading));
            if (loading <= KneeLoadingFraction) return 1d;
            double t = (loading - KneeLoadingFraction) / (1d - KneeLoadingFraction);
            return 1d - t * (1d - SaturatedRemovalFactor);
        }
    }

    /// <summary>Count of one kind of spare part (immutable). Used as configuration and as published stock.</summary>
    public sealed class SpareStock
    {
        public string Key { get; }
        public int Count { get; }
        public int Capacity { get; }

        public SpareStock(string key, int count, int capacity)
        {
            Key = EquipmentGuard.Key(key, nameof(key));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (count < 0 || count > capacity) throw new ArgumentOutOfRangeException(nameof(count), "Within [0, capacity].");
            Count = count;
            Capacity = capacity;
        }
    }

    /// <summary>Everything <see cref="IBaseEquipmentModel.Reset"/> needs (configuration, immutable).</summary>
    public sealed class BaseEquipmentConfiguration
    {
        public WaterProcessorConfiguration WaterProcessor { get; }
        public AirRevitalizationConfiguration Air { get; }
        public IReadOnlyList<SpareStock> InitialSpares { get; }
        public string Provenance { get; }

        public BaseEquipmentConfiguration(WaterProcessorConfiguration waterProcessor, AirRevitalizationConfiguration air,
            IEnumerable<SpareStock> initialSpares, string provenance)
        {
            WaterProcessor = waterProcessor ?? throw new ArgumentNullException(nameof(waterProcessor));
            Air = air ?? throw new ArgumentNullException(nameof(air));
            Provenance = EquipmentGuard.Provenance(provenance, nameof(provenance));
            InitialSpares = BaseEquipmentSnapshot.CopySpares(initialSpares, nameof(initialSpares));
            bool filter = false, inlet = false;
            foreach (SpareStock spare in InitialSpares)
            {
                filter |= spare.Key == waterProcessor.FilterSpareKey;
                inlet |= spare.Key == air.InletFilterSpareKey;
            }
            if (!filter || !inlet) throw new ArgumentException("Every spare key used by the equipment needs a stock entry (count may be 0).", nameof(initialSpares));
        }
    }

    /// <summary>Demand held constant during one <see cref="IBaseEquipmentModel.Advance"/>, declared by the authority.</summary>
    public sealed class EquipmentDemand
    {
        /// <summary>Members breathing cabin air (same count as phase 2 <c>OutpostDemand.CrewAboard</c>).</summary>
        public int CrewAboard { get; }

        public EquipmentDemand(int crewAboard)
        {
            if (crewAboard < 0) throw new ArgumentOutOfRangeException(nameof(crewAboard));
            CrewAboard = crewAboard;
        }
    }

    /// <summary>Water processor state (model truth, immutable). <see cref="ProductConductivity"/> is the reading of the
    /// product currently held (Unavailable when the hold is empty) or of the last Test sample.</summary>
    public sealed class WaterProcessorState
    {
        public EquipmentCondition Condition { get; }
        public EquipmentOperatingState Operating { get; }
        public EquipmentFault Fault { get; }
        public double HoldKilograms { get; }
        public InstrumentReading ProductConductivity { get; }
        /// <summary>Simulated second the processor degraded; null while nominal.</summary>
        public double? DegradedAtSeconds { get; }

        public WaterProcessorState(EquipmentCondition condition, EquipmentOperatingState operating, EquipmentFault fault,
            double holdKilograms, InstrumentReading productConductivity, double? degradedAtSeconds)
        {
            Condition = EquipmentGuard.Defined(condition, nameof(condition));
            Operating = EquipmentGuard.Defined(operating, nameof(operating));
            Fault = EquipmentGuard.Defined(fault, nameof(fault));
            if ((condition == EquipmentCondition.Nominal) != (fault == EquipmentFault.None))
                throw new ArgumentException("A fault exists exactly when the condition is not nominal.", nameof(fault));
            HoldKilograms = EquipmentGuard.NonNegative(holdKilograms, nameof(holdKilograms));
            ProductConductivity = productConductivity ?? throw new ArgumentNullException(nameof(productConductivity));
            if (productConductivity.Unit != ReadingUnit.MicrosiemensPerCentimeter) throw new ArgumentException("Conductivity reading expected.", nameof(productConductivity));
            if (degradedAtSeconds.HasValue) EquipmentGuard.NonNegative(degradedAtSeconds.Value, nameof(degradedAtSeconds));
            if (degradedAtSeconds.HasValue == (condition == EquipmentCondition.Nominal))
                throw new ArgumentException("Degradation time exists exactly when not nominal.", nameof(degradedAtSeconds));
            DegradedAtSeconds = degradedAtSeconds;
        }
    }

    /// <summary>Air revitalization unit state (model truth, immutable).</summary>
    public sealed class AirRevitalizationState
    {
        public EquipmentOperatingState Operating { get; }
        public SorbentBed ActiveBed { get; }
        public double ActiveLoadingFraction { get; }
        public double StandbyLoadingFraction { get; }
        public double InletFlowFraction { get; }
        /// <summary>Factor applied to the base model's CO2 removal (0 while isolated).</summary>
        public double RemovalFactor { get; }

        public AirRevitalizationState(EquipmentOperatingState operating, SorbentBed activeBed, double activeLoadingFraction,
            double standbyLoadingFraction, double inletFlowFraction, double removalFactor)
        {
            Operating = EquipmentGuard.Defined(operating, nameof(operating));
            ActiveBed = EquipmentGuard.Defined(activeBed, nameof(activeBed));
            ActiveLoadingFraction = EquipmentGuard.Fraction(activeLoadingFraction, nameof(activeLoadingFraction));
            StandbyLoadingFraction = EquipmentGuard.Fraction(standbyLoadingFraction, nameof(standbyLoadingFraction));
            InletFlowFraction = EquipmentGuard.Fraction(inletFlowFraction, nameof(inletFlowFraction));
            RemovalFactor = EquipmentGuard.Fraction(removalFactor, nameof(removalFactor));
            if (operating == EquipmentOperatingState.Isolated && removalFactor != 0d)
                throw new ArgumentException("An isolated unit removes nothing.", nameof(removalFactor));
        }
    }

    /// <summary>Cumulative mass and parts accounting since Reset (diagnostics and D5 balances). Kilograms / counts.</summary>
    public sealed class EquipmentLedger
    {
        public double BatchInputKilograms { get; }
        public double BrineOutflowKilograms { get; }
        public double ReleasedToPotableKilograms { get; }
        public double ReleasedForReprocessKilograms { get; }
        public int SparesConsumed { get; }
        public int SparesReceived { get; }

        public EquipmentLedger(double batchInputKilograms, double brineOutflowKilograms, double releasedToPotableKilograms,
            double releasedForReprocessKilograms, int sparesConsumed, int sparesReceived)
        {
            BatchInputKilograms = EquipmentGuard.NonNegative(batchInputKilograms, nameof(batchInputKilograms));
            BrineOutflowKilograms = EquipmentGuard.NonNegative(brineOutflowKilograms, nameof(brineOutflowKilograms));
            ReleasedToPotableKilograms = EquipmentGuard.NonNegative(releasedToPotableKilograms, nameof(releasedToPotableKilograms));
            ReleasedForReprocessKilograms = EquipmentGuard.NonNegative(releasedForReprocessKilograms, nameof(releasedForReprocessKilograms));
            if (sparesConsumed < 0) throw new ArgumentOutOfRangeException(nameof(sparesConsumed));
            if (sparesReceived < 0) throw new ArgumentOutOfRangeException(nameof(sparesReceived));
            SparesConsumed = sparesConsumed;
            SparesReceived = sparesReceived;
        }

        public static readonly EquipmentLedger Zero = new EquipmentLedger(0, 0, 0, 0, 0, 0);
    }

    /// <summary>Immutable observation of the whole equipment model at <see cref="SimulatedTimeSeconds"/>.</summary>
    public sealed class BaseEquipmentSnapshot
    {
        public double SimulatedTimeSeconds { get; }
        public WaterProcessorState WaterProcessor { get; }
        public AirRevitalizationState Air { get; }
        public IReadOnlyList<SpareStock> Spares { get; }
        public EquipmentLedger Ledger { get; }

        public BaseEquipmentSnapshot(double simulatedTimeSeconds, WaterProcessorState waterProcessor, AirRevitalizationState air,
            IEnumerable<SpareStock> spares, EquipmentLedger ledger)
        {
            SimulatedTimeSeconds = EquipmentGuard.NonNegative(simulatedTimeSeconds, nameof(simulatedTimeSeconds));
            WaterProcessor = waterProcessor ?? throw new ArgumentNullException(nameof(waterProcessor));
            Air = air ?? throw new ArgumentNullException(nameof(air));
            Spares = CopySpares(spares, nameof(spares));
            Ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        }

        public bool TryGetSpare(string key, out SpareStock spare)
        {
            foreach (SpareStock candidate in Spares)
                if (candidate.Key == key)
                {
                    spare = candidate;
                    return true;
                }
            spare = null;
            return false;
        }

        internal static IReadOnlyList<SpareStock> CopySpares(IEnumerable<SpareStock> spares, string name)
        {
            if (spares == null) throw new ArgumentNullException(name);
            var copy = new List<SpareStock>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (SpareStock spare in spares)
            {
                if (spare == null) throw new ArgumentException("Null spare.", name);
                if (!keys.Add(spare.Key)) throw new ArgumentException($"Duplicate spare '{spare.Key}'.", name);
                copy.Add(spare);
            }
            if (copy.Count > BaseEquipmentLimits.MaxSpareKinds) throw new ArgumentException("Too many spare kinds.", name);
            return new ReadOnlyCollection<SpareStock>(copy.ToArray());
        }
    }

    public static class BaseEquipmentLimits
    {
        public const int MaxSpareKinds = 8;
        public const int MaxSpareCount = 99;
    }
}
