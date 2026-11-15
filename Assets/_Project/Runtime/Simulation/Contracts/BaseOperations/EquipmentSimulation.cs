using System;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Simulation.BaseOperations
{
    /// <summary>What an <see cref="EquipmentEffect"/> does. Every kind is applied at most once per id and Reset.</summary>
    public enum EquipmentEffectKind
    {
        /// <summary>A batch of untreated water enters the processor (the base already removed it with a phase 2 transfer,
        /// boundary outflow +Kilograms, same id). Product = Kilograms × recovery of the current condition goes to the hold;
        /// the rest leaves as brine. Requires Online, not Failed, an empty hold and Kilograms == batch size.</summary>
        ReceiveBatch,
        /// <summary>The held product leaves the processor to the potable tank (the base then adds it with a phase 2
        /// transfer, boundary inflow, same id). Kilograms must equal the hold exactly.</summary>
        ReleaseToPotable,
        /// <summary>The held product leaves the processor back to the untreated stock (same pattern). Kilograms == hold.</summary>
        ReleaseForReprocess,
        /// <summary>Takes a fresh quality reading of the processor's current product without moving mass (repair Test).</summary>
        SampleProduct,
        /// <summary>Sets the operating state of <see cref="EquipmentEffect.Equipment"/> (Isolate / Restore).</summary>
        SetOperatingState,
        /// <summary>Consumes one filter spare and returns the processor to Nominal. Requires Isolated.</summary>
        ReplaceProcessorFilter,
        /// <summary>Swaps active and standby beds. Requires Online.</summary>
        SwitchBed,
        /// <summary>Sets the standby bed loading to zero (the regeneration operation finished).</summary>
        RegenerateStandbyBed,
        /// <summary>Consumes one inlet filter spare and restores nominal inlet flow.</summary>
        ReplaceInletFilter,
        /// <summary>Adds <see cref="EquipmentEffect.Count"/> spares of <see cref="EquipmentEffect.SpareKey"/> (resupply unload).</summary>
        ReceiveSpares
    }

    /// <summary>
    /// One identified change of the equipment model (immutable). Like the phase 2 <see cref="ResourceTransfer"/> it is
    /// idempotent by <see cref="Id"/>: applied at most once per Reset; the same id with the same content again is
    /// AlreadyApplied; with other content, IdConflict. The constructor checks the shape of each kind.
    /// </summary>
    public sealed class EquipmentEffect
    {
        public Guid Id { get; }
        public EquipmentEffectKind Kind { get; }
        public EquipmentKind Equipment { get; }
        public double Kilograms { get; }
        public EquipmentOperatingState OperatingState { get; }
        public string SpareKey { get; }
        public int Count { get; }

        private EquipmentEffect(Guid id, EquipmentEffectKind kind, EquipmentKind equipment, double kilograms,
            EquipmentOperatingState operatingState, string spareKey, int count)
        {
            if (id == Guid.Empty) throw new ArgumentException("Effect id must not be empty.", nameof(id));
            Id = id;
            Kind = EquipmentGuard.Defined(kind, nameof(kind));
            Equipment = EquipmentGuard.Defined(equipment, nameof(equipment));
            OperatingState = EquipmentGuard.Defined(operatingState, nameof(operatingState));
            bool massKind = kind == EquipmentEffectKind.ReceiveBatch || kind == EquipmentEffectKind.ReleaseToPotable
                || kind == EquipmentEffectKind.ReleaseForReprocess;
            if (massKind) EquipmentGuard.Positive(kilograms, nameof(kilograms));
            else if (kilograms != 0d) throw new ArgumentException("Only batch and release effects move mass.", nameof(kilograms));
            bool waterKind = massKind || kind == EquipmentEffectKind.SampleProduct || kind == EquipmentEffectKind.ReplaceProcessorFilter;
            bool airKind = kind == EquipmentEffectKind.SwitchBed || kind == EquipmentEffectKind.RegenerateStandbyBed
                || kind == EquipmentEffectKind.ReplaceInletFilter;
            if (waterKind && equipment != EquipmentKind.WaterProcessor) throw new ArgumentException("Water processor effect.", nameof(equipment));
            if (airKind && equipment != EquipmentKind.AirRevitalization) throw new ArgumentException("Air unit effect.", nameof(equipment));
            if (kind == EquipmentEffectKind.ReceiveSpares)
            {
                EquipmentGuard.Key(spareKey, nameof(spareKey));
                if (count < 1 || count > BaseEquipmentLimits.MaxSpareCount) throw new ArgumentOutOfRangeException(nameof(count));
            }
            else if (spareKey != null || count != 0) throw new ArgumentException("Only ReceiveSpares names a spare and a count.");
            Kilograms = kilograms;
            SpareKey = spareKey;
            Count = count;
        }

        public static EquipmentEffect ReceiveBatch(Guid id, double kilograms) =>
            new EquipmentEffect(id, EquipmentEffectKind.ReceiveBatch, EquipmentKind.WaterProcessor, kilograms, EquipmentOperatingState.Online, null, 0);
        public static EquipmentEffect ReleaseToPotable(Guid id, double kilograms) =>
            new EquipmentEffect(id, EquipmentEffectKind.ReleaseToPotable, EquipmentKind.WaterProcessor, kilograms, EquipmentOperatingState.Online, null, 0);
        public static EquipmentEffect ReleaseForReprocess(Guid id, double kilograms) =>
            new EquipmentEffect(id, EquipmentEffectKind.ReleaseForReprocess, EquipmentKind.WaterProcessor, kilograms, EquipmentOperatingState.Online, null, 0);
        public static EquipmentEffect SampleProduct(Guid id) =>
            new EquipmentEffect(id, EquipmentEffectKind.SampleProduct, EquipmentKind.WaterProcessor, 0, EquipmentOperatingState.Online, null, 0);
        public static EquipmentEffect SetOperatingState(Guid id, EquipmentKind equipment, EquipmentOperatingState state) =>
            new EquipmentEffect(id, EquipmentEffectKind.SetOperatingState, equipment, 0, state, null, 0);
        public static EquipmentEffect ReplaceProcessorFilter(Guid id) =>
            new EquipmentEffect(id, EquipmentEffectKind.ReplaceProcessorFilter, EquipmentKind.WaterProcessor, 0, EquipmentOperatingState.Online, null, 0);
        public static EquipmentEffect SwitchBed(Guid id) =>
            new EquipmentEffect(id, EquipmentEffectKind.SwitchBed, EquipmentKind.AirRevitalization, 0, EquipmentOperatingState.Online, null, 0);
        public static EquipmentEffect RegenerateStandbyBed(Guid id) =>
            new EquipmentEffect(id, EquipmentEffectKind.RegenerateStandbyBed, EquipmentKind.AirRevitalization, 0, EquipmentOperatingState.Online, null, 0);
        public static EquipmentEffect ReplaceInletFilter(Guid id) =>
            new EquipmentEffect(id, EquipmentEffectKind.ReplaceInletFilter, EquipmentKind.AirRevitalization, 0, EquipmentOperatingState.Online, null, 0);
        /// <summary>Spares are not tied to one machine; <see cref="Equipment"/> is WaterProcessor by convention.</summary>
        public static EquipmentEffect ReceiveSpares(Guid id, string spareKey, int count) =>
            new EquipmentEffect(id, EquipmentEffectKind.ReceiveSpares, EquipmentKind.WaterProcessor, 0, EquipmentOperatingState.Online, spareKey, count);

        public bool HasSameContent(EquipmentEffect other) =>
            other != null && other.Id == Id && other.Kind == Kind && other.Equipment == Equipment && other.Kilograms == Kilograms
            && other.OperatingState == OperatingState && string.Equals(other.SpareKey, SpareKey, StringComparison.Ordinal) && other.Count == Count;
    }

    /// <summary>Outcome of <see cref="IBaseEquipmentModel.Apply"/>. Anything other than Applied changes nothing.</summary>
    public enum EquipmentEffectStatus
    {
        Applied,
        AlreadyApplied,
        IdConflict,
        /// <summary>No spare of the required kind.</summary>
        InsufficientSpares,
        /// <summary>Spare or hold capacity would be exceeded.</summary>
        ExceedsCapacity,
        /// <summary>The machine's state does not allow it (hold not empty / wrong mass, isolated, failed, not isolated...).</summary>
        InvalidForState
    }

    /// <summary>
    /// Phase 4 equipment model (D1 implements; ONLY the session authority calls it). Plain C#, single-threaded, no
    /// UnityEngine. It owns the processor hold, the sorbent beds, the inlet flow, the spares and the scripted synthetic
    /// fault. It never touches the phase 2 stocks: water moves between the base and the processor as a pair with the same
    /// id (phase 2 <see cref="ResourceTransfer"/> with boundary flow + <see cref="EquipmentEffect"/>), like the phase 3
    /// potable→suit fill. Lost water (brine) is never returned.
    /// </summary>
    public interface IBaseEquipmentModel
    {
        /// <summary>Time zero, initial spares, nominal machines, empty hold, Online. Clears applied ids. Null throws.</summary>
        void Reset(BaseEquipmentConfiguration configuration);

        /// <summary>Requires Reset; immutable observation.</summary>
        BaseEquipmentSnapshot Snapshot { get; }

        /// <summary>Requires Reset; finite, nonnegative seconds (zero is a no-op). Loads the active bed, applies scripted
        /// degradation, failure and inlet restriction at their exact times. Invalid input throws without changing state.</summary>
        void Advance(double elapsedSeconds, EquipmentDemand demand);

        /// <summary>Seconds until the next scripted change or the active bed crossing its knee/full loading under
        /// <paramref name="demand"/>; infinity when none. The authority cuts its step there. Does not mutate.</summary>
        double SecondsUntilEquipmentEvent(EquipmentDemand demand);

        /// <summary>All or nothing; see <see cref="EquipmentEffectStatus"/>. Never throws for state or stock reasons.</summary>
        EquipmentEffectStatus Apply(EquipmentEffect effect);
    }

    /// <summary>
    /// Coupling of the air unit to the phase 2 base model. D1 implements it on the SAME object as the single
    /// <see cref="IOutpostSimulation"/> (no second base model). The authority sets the factor before each base Advance
    /// from <see cref="AirRevitalizationState.RemovalFactor"/>; 1 after Reset reproduces phase 2 exactly.
    /// </summary>
    public interface IAirServiceCapacity
    {
        /// <summary>Multiplier in [0, 1] of the declared maximum CO2 removal rate. Out of range throws.</summary>
        void SetCarbonDioxideRemovalFactor(double factor);
        double CarbonDioxideRemovalFactor { get; }
    }
}
