using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Simulation.BaseOperations;

namespace Trainer.Simulation.Incidents
{
    /// <summary>
    /// Phase 5 limits of the physical incident layer (D1). Every value is a SYNTHETIC game bound, not hardware data.
    /// </summary>
    public static class IncidentEffectLimits
    {
        /// <summary>Simultaneous power modifiers kept by the base model (incidents + isolation + upgrade).</summary>
        public const int MaxPowerModifiers = 8;
        /// <summary>Lowest composed generation / discharge factor: an incident never makes a capacity negative.</summary>
        public const double MinCapacityFactor = 0d;
        /// <summary>Highest composed factor: incidents only derate (no modifier makes the hardware better than declared).</summary>
        public const double MaxCapacityFactor = 1d;
        /// <summary>Upper bound of the extra essential load an upgrade may add (W). Synthetic.</summary>
        public const double MaxAddedEssentialWatts = 100d;
        /// <summary>The processor recovery may never reach 1 (brine always leaves). Synthetic ceiling of any upgrade.</summary>
        public const double MaxUpgradedRecoveryFraction = 0.97d;
    }

    /// <summary>Which declared electrical quantity a <see cref="PowerModifier"/> changes.</summary>
    public enum PowerModifierTarget
    {
        /// <summary>Multiplies <c>PowerConfiguration.GenerationWatts</c> (e.g. one array converter offline). Factor in [0, 1].</summary>
        GenerationFactor,
        /// <summary>Multiplies <c>PowerConfiguration.MaxDischargeWatts</c> (battery discharge controller derated). Factor in [0, 1].
        /// The battery still needs stored energy: a derated limit never creates energy.</summary>
        DischargeFactor,
        /// <summary>Adds watts to the essential request (an installed upgrade's electronics). In [0, MaxAddedEssentialWatts].</summary>
        EssentialLoadWatts
    }

    /// <summary>
    /// One identified, reversible change of the base's electrical capacity (immutable). Idempotent by <see cref="Id"/> like a
    /// phase 2 <c>ResourceTransfer</c>: the same id with the same content is AlreadyApplied, with other content IdConflict.
    /// <see cref="Cause"/> is a stable key tracing the incident/upgrade that requested it.
    /// Composition (declared policy): factors of one target MULTIPLY and the product is clamped to
    /// [<see cref="IncidentEffectLimits.MinCapacityFactor"/>, <see cref="IncidentEffectLimits.MaxCapacityFactor"/>]; essential
    /// watts ADD and are clamped to [0, <see cref="IncidentEffectLimits.MaxAddedEssentialWatts"/>]. Removing one modifier never
    /// removes another; removing does not refund energy already consumed or curtailed.
    /// </summary>
    public sealed class PowerModifier
    {
        public Guid Id { get; }
        public PowerModifierTarget Target { get; }
        public double Value { get; }
        public string Cause { get; }

        public PowerModifier(Guid id, PowerModifierTarget target, double value, string cause)
        {
            if (id == Guid.Empty) throw new ArgumentException("Modifier id must not be empty.", nameof(id));
            Id = id;
            Target = IncidentGuard.Defined(target, nameof(target));
            if (!IncidentGuard.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "Must be finite.");
            if (target == PowerModifierTarget.EssentialLoadWatts)
            {
                if (value < 0d || value > IncidentEffectLimits.MaxAddedEssentialWatts) throw new ArgumentOutOfRangeException(nameof(value));
            }
            else if (value < IncidentEffectLimits.MinCapacityFactor || value > IncidentEffectLimits.MaxCapacityFactor)
                throw new ArgumentOutOfRangeException(nameof(value), value, "A capacity factor is within [0, 1].");
            Value = value;
            Cause = IncidentGuard.Key(cause, nameof(cause));
        }

        public bool HasSameContent(PowerModifier other) =>
            other != null && other.Id == Id && other.Target == Target && other.Value == Value && string.Equals(other.Cause, Cause, StringComparison.Ordinal);
    }

    /// <summary>Outcome of <see cref="IPowerCapacityModifiers.Apply"/>. Anything other than Applied changes nothing.</summary>
    public enum PowerModifierStatus
    {
        Applied,
        AlreadyApplied,
        IdConflict,
        /// <summary>More than <see cref="IncidentEffectLimits.MaxPowerModifiers"/> would be active.</summary>
        LimitReached
    }

    /// <summary>The composed electrical capacity in force (immutable). Factors 1 and 0 W reproduce phase 4 exactly.</summary>
    public sealed class PowerCapacityState
    {
        public double GenerationFactor { get; }
        public double DischargeFactor { get; }
        public double AddedEssentialWatts { get; }
        /// <summary>Active modifiers in application order.</summary>
        public IReadOnlyList<PowerModifier> Modifiers { get; }

        public PowerCapacityState(double generationFactor, double dischargeFactor, double addedEssentialWatts, IEnumerable<PowerModifier> modifiers)
        {
            GenerationFactor = IncidentGuard.Fraction(generationFactor, nameof(generationFactor));
            DischargeFactor = IncidentGuard.Fraction(dischargeFactor, nameof(dischargeFactor));
            if (!IncidentGuard.IsFinite(addedEssentialWatts) || addedEssentialWatts < 0d || addedEssentialWatts > IncidentEffectLimits.MaxAddedEssentialWatts)
                throw new ArgumentOutOfRangeException(nameof(addedEssentialWatts));
            AddedEssentialWatts = addedEssentialWatts;
            var list = new List<PowerModifier>(modifiers ?? throw new ArgumentNullException(nameof(modifiers)));
            if (list.Count > IncidentEffectLimits.MaxPowerModifiers) throw new ArgumentException("Too many modifiers.", nameof(modifiers));
            Modifiers = new ReadOnlyCollection<PowerModifier>(list);
        }

        public static readonly PowerCapacityState Nominal = new PowerCapacityState(1d, 1d, 0d, Array.Empty<PowerModifier>());

        /// <summary>The single composition rule (see <see cref="PowerModifier"/>).</summary>
        public static PowerCapacityState Compose(IEnumerable<PowerModifier> modifiers)
        {
            if (modifiers == null) throw new ArgumentNullException(nameof(modifiers));
            double generation = 1d, discharge = 1d, watts = 0d;
            var list = new List<PowerModifier>();
            foreach (PowerModifier m in modifiers)
            {
                if (m == null) throw new ArgumentException("Null modifier.", nameof(modifiers));
                list.Add(m);
                switch (m.Target)
                {
                    case PowerModifierTarget.GenerationFactor: generation *= m.Value; break;
                    case PowerModifierTarget.DischargeFactor: discharge *= m.Value; break;
                    default: watts += m.Value; break;
                }
            }
            return new PowerCapacityState(Clamp(generation), Clamp(discharge), Math.Min(Math.Max(watts, 0d), IncidentEffectLimits.MaxAddedEssentialWatts), list);
        }

        private static double Clamp(double factor) =>
            Math.Min(Math.Max(factor, IncidentEffectLimits.MinCapacityFactor), IncidentEffectLimits.MaxCapacityFactor);
    }

    /// <summary>
    /// Reversible electrical capacity of the phase 2 base (D1). Implemented on the SAME object as the single
    /// <c>IOutpostSimulation</c> (like <c>IAirServiceCapacity</c>): there is no second base model. Applies from the next
    /// <c>Advance</c>; Reset clears every modifier (phase 4 behaviour). Only the session authority calls it.
    /// </summary>
    public interface IPowerCapacityModifiers
    {
        /// <summary>Requires Reset. All or nothing; never throws for state reasons.</summary>
        PowerModifierStatus Apply(PowerModifier modifier);

        /// <summary>Requires Reset. Removes the modifier with that id; false if it is not active (idempotent).</summary>
        bool Remove(Guid modifierId);

        /// <summary>Requires Reset. The composed state in force (immutable).</summary>
        PowerCapacityState PowerCapacity { get; }
    }

    /// <summary>
    /// Phase 5 additions to the equipment model (D1), on the SAME object as the single <c>IBaseEquipmentModel</c>. The frozen
    /// phase 4 <c>EquipmentEffectKind</c> enum is not extended: these are separate, idempotent entry points that reuse
    /// <see cref="EquipmentEffectStatus"/> as their result. Every id is applied at most once per Reset and shares the id space
    /// of <c>IBaseEquipmentModel.Apply</c>.
    /// </summary>
    public interface IEquipmentIncidentTarget
    {
        /// <summary>Director-driven degradation of the water processor (filter restricted), exactly like the phase 4 scripted
        /// fault but requested at the current instant. Requires a Nominal processor (InvalidForState otherwise).
        /// <paramref name="failAfterSeconds"/>: optional synthetic time to Failed while still Degraded (null = never fails).
        /// The phase 4 repair (ReplaceProcessorFilter) clears it and cancels the fail timer; nothing re-arms it.</summary>
        EquipmentEffectStatus DegradeProcessor(Guid id, double? failAfterSeconds);

        /// <summary>Consumes one spare of <paramref name="spareKey"/> for a phase 5 repair or installation. InsufficientSpares
        /// if none is left; InvalidForState for an unknown key.</summary>
        EquipmentEffectStatus ConsumeSpare(Guid id, string spareKey);

        /// <summary>Raises the processor's NOMINAL recovery fraction to <paramref name="recoveryFraction"/> (bounded upgrade).
        /// Must be above the configured nominal, below <see cref="IncidentEffectLimits.MaxUpgradedRecoveryFraction"/>, and only
        /// once per Reset (a second install is InvalidForState). Degraded recovery is unchanged; brine still leaves.</summary>
        EquipmentEffectStatus UpgradeProcessorRecovery(Guid id, double recoveryFraction);

        /// <summary>Nominal recovery in force (configured value, or the upgraded one).</summary>
        double ProcessorNominalRecovery { get; }

        /// <summary>True once <see cref="UpgradeProcessorRecovery"/> was applied in this Reset.</summary>
        bool ProcessorUpgraded { get; }
    }

    internal static class IncidentGuard
    {
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

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
            if (string.IsNullOrEmpty(value) || value.Length > 48 || value[0] < 'a' || value[0] > 'z')
                throw new ArgumentException($"'{value}' is not a stable key.", name);
            foreach (char c in value)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-'))
                    throw new ArgumentException($"'{value}' is not a stable key.", name);
            return value;
        }
    }
}
