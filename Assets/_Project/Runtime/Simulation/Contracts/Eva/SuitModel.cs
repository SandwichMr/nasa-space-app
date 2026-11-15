using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Trainer.Simulation.Eva
{
    /// <summary>
    /// Suit services of the phase 3 reduced EVA model (a declared educational simplification, not a suit design).
    /// Each service is a separate stock with its own unit; they are never averaged or converted into each other.
    /// The declared order is the tie-break order when two services limit the return at the same time.
    /// </summary>
    public enum SuitService
    {
        /// <summary>Breathing oxygen carried by the suit, kilograms.</summary>
        Oxygen,
        /// <summary>Electrical energy of the suit battery (fans, pump, radio, tools), joules.</summary>
        Battery,
        /// <summary>Cooling feedwater evaporated to space to reject heat, kilograms.</summary>
        Feedwater
    }

    /// <summary>What the wearer is doing during a step, as decided by the session authority from confirmed
    /// state (a task in progress = Work; confirmed walking = Walk; otherwise Rest).</summary>
    public enum SuitActivity { Rest, Walk, Work }

    /// <summary>
    /// Model of ONE suit service (configuration, immutable). Amounts are in the service unit (kg or J); rates
    /// are per simulated second (kg/s or W) and strictly positive, so a margin is always finite. The reserve is
    /// the part of the capacity that a plan must not use (a declared scenario value, not a safety standard).
    /// </summary>
    public sealed class SuitServiceModel
    {
        public SuitService Service { get; }
        public double Capacity { get; }
        public double Reserve { get; }
        public double RestRate { get; }
        public double WalkRate { get; }
        public double WorkRate { get; }

        public SuitServiceModel(SuitService service, double capacity, double reserve,
            double restRate, double walkRate, double workRate)
        {
            Service = EvaGuard.Defined(service, nameof(service));
            Capacity = EvaGuard.Positive(capacity, nameof(capacity));
            Reserve = EvaGuard.NonNegative(reserve, nameof(reserve));
            if (reserve >= capacity) throw new ArgumentException("The reserve must be below the capacity.", nameof(reserve));
            RestRate = EvaGuard.Positive(restRate, nameof(restRate));
            WalkRate = EvaGuard.Positive(walkRate, nameof(walkRate));
            WorkRate = EvaGuard.Positive(workRate, nameof(workRate));
        }

        /// <summary>Unit of amounts: "kg" for oxygen and feedwater, "J" for the battery.</summary>
        public string Unit => Service == SuitService.Battery ? "J" : "kg";

        /// <summary>Consumption per simulated second for <paramref name="activity"/>, WITHOUT tool load.</summary>
        public double RateFor(SuitActivity activity)
        {
            switch (activity)
            {
                case SuitActivity.Rest: return RestRate;
                case SuitActivity.Walk: return WalkRate;
                case SuitActivity.Work: return WorkRate;
                default: throw new ArgumentOutOfRangeException(nameof(activity));
            }
        }
    }

    /// <summary>
    /// The reduced suit model shared by every suit of a scenario (configuration, immutable): exactly one
    /// <see cref="SuitServiceModel"/> per <see cref="SuitService"/>, in enum order. Tool loads of a task are added to
    /// the battery only (watts), never to oxygen or feedwater. Values are scenario inputs with
    /// <see cref="Provenance"/>; they are not suit specifications, clinical limits or a universal EVA recipe.
    /// </summary>
    public sealed class SuitModelConfiguration
    {
        public IReadOnlyList<SuitServiceModel> Services { get; }
        public string Provenance { get; }

        public SuitModelConfiguration(IEnumerable<SuitServiceModel> services, string provenance)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (string.IsNullOrWhiteSpace(provenance)) throw new ArgumentException("Provenance is required.", nameof(provenance));
            var byService = new SuitServiceModel[EvaGuard.ServiceCount];
            foreach (SuitServiceModel model in services)
            {
                if (model == null) throw new ArgumentException("Null service model.", nameof(services));
                if (byService[(int)model.Service] != null)
                    throw new ArgumentException($"Duplicate model for {model.Service}.", nameof(services));
                byService[(int)model.Service] = model;
            }
            foreach (SuitServiceModel model in byService)
                if (model == null) throw new ArgumentException("Every suit service needs exactly one model.", nameof(services));
            Services = new ReadOnlyCollection<SuitServiceModel>(byService);
            Provenance = provenance;
        }

        public SuitServiceModel Get(SuitService service) => Services[(int)EvaGuard.Defined(service, nameof(service))];
    }

    /// <summary>Amount of each suit service (immutable): finite and nonnegative, oxygen and feedwater in kg,
    /// battery in J. Capacity is checked by whoever holds the model (suit definitions and the simulation).</summary>
    public sealed class SuitLevels : IEquatable<SuitLevels>
    {
        public double OxygenKilograms { get; }
        public double BatteryJoules { get; }
        public double FeedwaterKilograms { get; }

        public SuitLevels(double oxygenKilograms, double batteryJoules, double feedwaterKilograms)
        {
            OxygenKilograms = EvaGuard.NonNegative(oxygenKilograms, nameof(oxygenKilograms));
            BatteryJoules = EvaGuard.NonNegative(batteryJoules, nameof(batteryJoules));
            FeedwaterKilograms = EvaGuard.NonNegative(feedwaterKilograms, nameof(feedwaterKilograms));
        }

        public double Get(SuitService service)
        {
            switch (service)
            {
                case SuitService.Oxygen: return OxygenKilograms;
                case SuitService.Battery: return BatteryJoules;
                case SuitService.Feedwater: return FeedwaterKilograms;
                default: throw new ArgumentOutOfRangeException(nameof(service));
            }
        }

        /// <summary>A copy with <paramref name="service"/> set to <paramref name="value"/>.</summary>
        public SuitLevels With(SuitService service, double value)
        {
            switch (service)
            {
                case SuitService.Oxygen: return new SuitLevels(value, BatteryJoules, FeedwaterKilograms);
                case SuitService.Battery: return new SuitLevels(OxygenKilograms, value, FeedwaterKilograms);
                case SuitService.Feedwater: return new SuitLevels(OxygenKilograms, BatteryJoules, value);
                default: throw new ArgumentOutOfRangeException(nameof(service));
            }
        }

        /// <summary>True when every level is within its capacity in <paramref name="model"/>.</summary>
        public bool FitsIn(SuitModelConfiguration model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            foreach (SuitServiceModel service in model.Services)
                if (Get(service.Service) > service.Capacity) return false;
            return true;
        }

        public bool Equals(SuitLevels other) => other != null && OxygenKilograms.Equals(other.OxygenKilograms)
            && BatteryJoules.Equals(other.BatteryJoules) && FeedwaterKilograms.Equals(other.FeedwaterKilograms);
        public override bool Equals(object obj) => Equals(obj as SuitLevels);
        public override int GetHashCode() =>
            unchecked((OxygenKilograms.GetHashCode() * 397 ^ BatteryJoules.GetHashCode()) * 397 ^ FeedwaterKilograms.GetHashCode());
        public override string ToString() =>
            $"O2 {OxygenKilograms:0.###} kg · batería {BatteryJoules:0} J · agua {FeedwaterKilograms:0.###} kg";
    }

    /// <summary>One physical suit of the scenario (configuration, immutable): a stable key (a-z, 0-9, '-',
    /// starting with a letter, at most 32 characters, as phase 2 keys) and its levels at Reset (on its rack).</summary>
    public sealed class SuitDefinition
    {
        public string Key { get; }
        public SuitLevels Initial { get; }

        public SuitDefinition(string key, SuitLevels initial)
        {
            Key = EvaGuard.Key(key, nameof(key));
            Initial = initial ?? throw new ArgumentNullException(nameof(initial));
        }
    }

    /// <summary>
    /// Consumables of the EVA area that are NOT base life-support stocks (configuration, immutable), in kg:
    /// the oxygen recharge supply used to fill suits (separate from the cabin O2 store of the phase 2 model, which
    /// is not a published stock) and the airlock make-up gas that replaces what each cycle vents. Feedwater is
    /// NOT here: it comes from the base potable water through an explicit phase 2 transfer.
    /// </summary>
    public sealed class EvaSupplyConfiguration
    {
        public double OxygenRechargeKilograms { get; }
        public double OxygenRechargeCapacityKilograms { get; }
        public double MakeupGasKilograms { get; }
        public double MakeupGasCapacityKilograms { get; }

        public EvaSupplyConfiguration(double oxygenRechargeKilograms, double oxygenRechargeCapacityKilograms,
            double makeupGasKilograms, double makeupGasCapacityKilograms)
        {
            OxygenRechargeKilograms = EvaGuard.NonNegative(oxygenRechargeKilograms, nameof(oxygenRechargeKilograms));
            OxygenRechargeCapacityKilograms = EvaGuard.NonNegative(oxygenRechargeCapacityKilograms, nameof(oxygenRechargeCapacityKilograms));
            MakeupGasKilograms = EvaGuard.NonNegative(makeupGasKilograms, nameof(makeupGasKilograms));
            MakeupGasCapacityKilograms = EvaGuard.NonNegative(makeupGasCapacityKilograms, nameof(makeupGasCapacityKilograms));
            if (oxygenRechargeKilograms > oxygenRechargeCapacityKilograms)
                throw new ArgumentException("Oxygen recharge exceeds its capacity.", nameof(oxygenRechargeKilograms));
            if (makeupGasKilograms > makeupGasCapacityKilograms)
                throw new ArgumentException("Make-up gas exceeds its capacity.", nameof(makeupGasKilograms));
        }
    }

    /// <summary>Everything <see cref="IEvaSimulation.Reset"/> needs (configuration, immutable). Suit index = position
    /// in <see cref="Suits"/>; keys are unique and initial levels fit the model's capacities.</summary>
    public sealed class EvaConfiguration
    {
        /// <summary>Most suits a scenario may declare (bounds the published snapshot).</summary>
        public const int MaxSuits = 4;

        public SuitModelConfiguration Model { get; }
        public IReadOnlyList<SuitDefinition> Suits { get; }
        public EvaSupplyConfiguration Supply { get; }

        public EvaConfiguration(SuitModelConfiguration model, IEnumerable<SuitDefinition> suits, EvaSupplyConfiguration supply)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            Supply = supply ?? throw new ArgumentNullException(nameof(supply));
            if (suits == null) throw new ArgumentNullException(nameof(suits));
            var copy = new List<SuitDefinition>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (SuitDefinition suit in suits)
            {
                if (suit == null) throw new ArgumentException("Null suit.", nameof(suits));
                if (!keys.Add(suit.Key)) throw new ArgumentException($"Duplicate suit '{suit.Key}'.", nameof(suits));
                if (!suit.Initial.FitsIn(model)) throw new ArgumentException($"Suit '{suit.Key}' starts above a capacity.", nameof(suits));
                copy.Add(suit);
            }
            if (copy.Count == 0 || copy.Count > MaxSuits) throw new ArgumentException("Between 1 and MaxSuits suits.", nameof(suits));
            Suits = new ReadOnlyCollection<SuitDefinition>(copy.ToArray());
        }

        public int IndexOf(string suitKey)
        {
            for (int i = 0; i < Suits.Count; i++)
                if (string.Equals(Suits[i].Key, suitKey, StringComparison.Ordinal)) return i;
            return -1;
        }
    }

    internal static class EvaGuard
    {
        public const int ServiceCount = 3;
        public const int MaxKeyLength = 32;

        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public static double Finite(double value, string name)
        {
            if (!IsFinite(value)) throw new ArgumentOutOfRangeException(name, value, "Must be finite.");
            return value;
        }

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

        public static T Defined<T>(T value, string name) where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value)) throw new ArgumentOutOfRangeException(name, value, "Undefined value.");
            return value;
        }

        /// <summary>Same rule as the phase 2 StableKey (Simulation cannot depend on Application).</summary>
        public static bool IsKey(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaxKeyLength) return false;
            if (value[0] < 'a' || value[0] > 'z') return false;
            foreach (char c in value)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')) return false;
            return true;
        }

        public static string Key(string value, string name)
        {
            if (!IsKey(value)) throw new ArgumentException($"'{value}' is not a stable key.", name);
            return value;
        }
    }
}
