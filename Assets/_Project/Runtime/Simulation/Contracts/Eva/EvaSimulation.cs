using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Trainer.Simulation.Eva
{
    /// <summary>Consumption request of ONE suit for one Advance (immutable): the suit is on its internal supply
    /// (sealed) for the whole interval with <see cref="Activity"/> and an extra battery load of
    /// <see cref="ToolWatts"/> (a task's tool; 0 otherwise). Declared by the session authority.</summary>
    public sealed class SuitDemand
    {
        public int SuitIndex { get; }
        public SuitActivity Activity { get; }
        public double ToolWatts { get; }

        public SuitDemand(int suitIndex, SuitActivity activity, double toolWatts)
        {
            if (suitIndex < 0 || suitIndex >= EvaConfiguration.MaxSuits) throw new ArgumentOutOfRangeException(nameof(suitIndex));
            SuitIndex = suitIndex;
            Activity = EvaGuard.Defined(activity, nameof(activity));
            ToolWatts = EvaGuard.NonNegative(toolWatts, nameof(toolWatts));
        }
    }

    /// <summary>
    /// Demand held constant during one <see cref="IEvaSimulation.Advance"/> (immutable). ONLY suits listed here
    /// consume, and only the authority lists them: a suit is listed exactly while its wearer is on suit supply
    /// (sealed), so a member's metabolic demand is charged either to the base (OutpostDemand.CrewAboard) or to
    /// one suit, never to both. Each suit index appears at most once.
    /// </summary>
    public sealed class EvaDemand
    {
        public static readonly EvaDemand None = new EvaDemand(Array.Empty<SuitDemand>());

        public IReadOnlyList<SuitDemand> Suits { get; }

        public EvaDemand(IEnumerable<SuitDemand> suits)
        {
            if (suits == null) throw new ArgumentNullException(nameof(suits));
            var copy = new List<SuitDemand>();
            var seen = new HashSet<int>();
            foreach (SuitDemand suit in suits)
            {
                if (suit == null) throw new ArgumentException("Null suit demand.", nameof(suits));
                if (!seen.Add(suit.SuitIndex)) throw new ArgumentException($"Suit {suit.SuitIndex} listed twice.", nameof(suits));
                copy.Add(suit);
            }
            Suits = new ReadOnlyCollection<SuitDemand>(copy.ToArray());
        }

        public bool TryGet(int suitIndex, out SuitDemand demand)
        {
            foreach (SuitDemand candidate in Suits)
                if (candidate.SuitIndex == suitIndex)
                {
                    demand = candidate;
                    return true;
                }
            demand = null;
            return false;
        }
    }

    public enum EvaTransferKind
    {
        /// <summary>Energy delivered by the base charger, J. The base already spent it as a service load while the
        /// charging task progressed; the suit stores what fits and the rest is reported as not stored (heat).</summary>
        ChargeBattery,
        /// <summary>Oxygen moved from the EVA recharge supply into the suit, kg (all or nothing).</summary>
        FillOxygen,
        /// <summary>Feedwater entering the suit from the base boundary, kg (all or nothing). The base potable water
        /// debit is a separate phase 2 ResourceTransfer applied by the authority BEFORE this one.</summary>
        FillFeedwater,
        /// <summary>Airlock make-up gas taken from the EVA supply for one repressurization, kg (all or nothing).
        /// No suit (SuitIndex = -1).</summary>
        UseMakeupGas
    }

    /// <summary>One identified change of EVA stocks (immutable). The id makes it idempotent: applied at most once
    /// per Reset (the authority uses the id of the task or airlock command that caused it).</summary>
    public sealed class EvaTransfer
    {
        public Guid Id { get; }
        public EvaTransferKind Kind { get; }
        public int SuitIndex { get; }
        /// <summary>J for ChargeBattery, kg otherwise; finite and positive.</summary>
        public double Amount { get; }

        public EvaTransfer(Guid id, EvaTransferKind kind, int suitIndex, double amount)
        {
            if (id == Guid.Empty) throw new ArgumentException("Transfer id must not be empty.", nameof(id));
            Id = id;
            Kind = EvaGuard.Defined(kind, nameof(kind));
            if (kind == EvaTransferKind.UseMakeupGas)
            {
                if (suitIndex != -1) throw new ArgumentException("Make-up gas has no suit (index -1).", nameof(suitIndex));
            }
            else if (suitIndex < 0 || suitIndex >= EvaConfiguration.MaxSuits) throw new ArgumentOutOfRangeException(nameof(suitIndex));
            SuitIndex = suitIndex;
            Amount = EvaGuard.Positive(amount, nameof(amount));
        }

        public bool HasSameContent(EvaTransfer other) =>
            other != null && other.Id == Id && other.Kind == Kind && other.SuitIndex == SuitIndex && other.Amount.Equals(Amount);
    }

    /// <summary>Outcome of <see cref="IEvaSimulation.Apply"/>. Anything other than Applied changes nothing.</summary>
    public enum EvaTransferStatus
    {
        Applied,
        /// <summary>Same id and content already applied since Reset; nothing changes again.</summary>
        AlreadyApplied,
        /// <summary>Id already used with other content; nothing changes.</summary>
        IdConflict,
        /// <summary>The source supply would become negative.</summary>
        InsufficientStock,
        /// <summary>The suit would exceed a capacity (FillOxygen, FillFeedwater).</summary>
        ExceedsCapacity,
        /// <summary>The suit index is not in the configuration.</summary>
        UnknownSuit
    }

    /// <summary>
    /// Published reading of one suit (immutable). <see cref="OnInternalSupply"/> repeats whether the suit was in the
    /// last nonzero Advance's demand. <see cref="BelowReserve"/> and <see cref="Depleted"/> are derived from the
    /// levels (below the declared reserve; exactly zero) and are information, not clinical states.
    /// </summary>
    public sealed class SuitReading
    {
        public int Index { get; }
        public string Key { get; }
        public SuitLevels Levels { get; }
        public bool OnInternalSupply { get; }
        public IReadOnlyList<SuitService> BelowReserve { get; }
        public IReadOnlyList<SuitService> Depleted { get; }
        public bool IsDepleted => Depleted.Count > 0;

        public SuitReading(int index, string key, SuitLevels levels, bool onInternalSupply,
            IEnumerable<SuitService> belowReserve, IEnumerable<SuitService> depleted)
        {
            if (index < 0 || index >= EvaConfiguration.MaxSuits) throw new ArgumentOutOfRangeException(nameof(index));
            Index = index;
            Key = EvaGuard.Key(key, nameof(key));
            Levels = levels ?? throw new ArgumentNullException(nameof(levels));
            OnInternalSupply = onInternalSupply;
            BelowReserve = Services(belowReserve, nameof(belowReserve));
            Depleted = Services(depleted, nameof(depleted));
            foreach (SuitService service in Depleted)
                if (levels.Get(service) != 0d) throw new ArgumentException($"{service} is not at zero.", nameof(depleted));
        }

        /// <summary>Reading derived from <paramref name="levels"/> and the model's reserves.</summary>
        public static SuitReading From(int index, string key, SuitLevels levels, bool onInternalSupply, SuitModelConfiguration model)
        {
            if (levels == null) throw new ArgumentNullException(nameof(levels));
            if (model == null) throw new ArgumentNullException(nameof(model));
            var below = new List<SuitService>();
            var empty = new List<SuitService>();
            foreach (SuitServiceModel service in model.Services)
            {
                double level = levels.Get(service.Service);
                if (level < service.Reserve) below.Add(service.Service);
                if (level == 0d) empty.Add(service.Service);
            }
            return new SuitReading(index, key, levels, onInternalSupply, below, empty);
        }

        private static IReadOnlyList<SuitService> Services(IEnumerable<SuitService> values, string name)
        {
            if (values == null) throw new ArgumentNullException(name);
            var copy = new List<SuitService>();
            foreach (SuitService service in values)
            {
                EvaGuard.Defined(service, name);
                if (copy.Contains(service)) throw new ArgumentException($"{service} listed twice.", name);
                copy.Add(service);
            }
            copy.Sort();
            return new ReadOnlyCollection<SuitService>(copy.ToArray());
        }
    }

    /// <summary>EVA supply stocks as published, kg (immutable).</summary>
    public sealed class EvaSupplyReading
    {
        public double OxygenRechargeKilograms { get; }
        public double MakeupGasKilograms { get; }

        public EvaSupplyReading(double oxygenRechargeKilograms, double makeupGasKilograms)
        {
            OxygenRechargeKilograms = EvaGuard.NonNegative(oxygenRechargeKilograms, nameof(oxygenRechargeKilograms));
            MakeupGasKilograms = EvaGuard.NonNegative(makeupGasKilograms, nameof(makeupGasKilograms));
        }
    }

    /// <summary>Immutable observation of the EVA model at <see cref="SimulatedTimeSeconds"/> (seconds since Reset).</summary>
    public sealed class EvaSnapshot
    {
        public double SimulatedTimeSeconds { get; }
        public IReadOnlyList<SuitReading> Suits { get; }
        public EvaSupplyReading Supply { get; }

        public EvaSnapshot(double simulatedTimeSeconds, IEnumerable<SuitReading> suits, EvaSupplyReading supply)
        {
            SimulatedTimeSeconds = EvaGuard.NonNegative(simulatedTimeSeconds, nameof(simulatedTimeSeconds));
            Supply = supply ?? throw new ArgumentNullException(nameof(supply));
            if (suits == null) throw new ArgumentNullException(nameof(suits));
            var copy = new List<SuitReading>();
            foreach (SuitReading suit in suits)
            {
                if (suit == null) throw new ArgumentException("Null suit reading.", nameof(suits));
                if (suit.Index != copy.Count) throw new ArgumentException("Suit readings must be in index order.", nameof(suits));
                copy.Add(suit);
            }
            if (copy.Count == 0 || copy.Count > EvaConfiguration.MaxSuits) throw new ArgumentException("Between 1 and MaxSuits suits.", nameof(suits));
            Suits = new ReadOnlyCollection<SuitReading>(copy.ToArray());
        }
    }

    /// <summary>
    /// Diagnostic ledger of the EVA model since Reset (immutable): what each service consumed, what transfers
    /// brought in and what did not fit. Not part of the published session snapshot; for the authority, tests and
    /// validation. Closure per suit service: level(t) = level(0) + filled/charged − consumed.
    /// </summary>
    public sealed class EvaLedger
    {
        public static readonly EvaLedger Zero = new EvaLedger(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        public double OxygenConsumedKilograms { get; }
        public double BatteryConsumedJoules { get; }
        public double FeedwaterConsumedKilograms { get; }
        public double OxygenFilledKilograms { get; }
        public double FeedwaterFilledKilograms { get; }
        public double BatteryStoredJoules { get; }
        /// <summary>Charger energy that did not fit in the battery (lost as heat, not created or kept).</summary>
        public double ChargeNotStoredJoules { get; }
        public double MakeupGasUsedKilograms { get; }
        /// <summary>Demand that could not be served because a service was already empty.</summary>
        public double UnservedOxygenKilograms { get; }
        public double UnservedBatteryJoules { get; }
        public double UnservedFeedwaterKilograms { get; }

        public EvaLedger(double oxygenConsumedKilograms, double batteryConsumedJoules, double feedwaterConsumedKilograms,
            double oxygenFilledKilograms, double feedwaterFilledKilograms, double batteryStoredJoules,
            double chargeNotStoredJoules, double makeupGasUsedKilograms, double unservedOxygenKilograms,
            double unservedBatteryJoules, double unservedFeedwaterKilograms)
        {
            OxygenConsumedKilograms = oxygenConsumedKilograms;
            BatteryConsumedJoules = batteryConsumedJoules;
            FeedwaterConsumedKilograms = feedwaterConsumedKilograms;
            OxygenFilledKilograms = oxygenFilledKilograms;
            FeedwaterFilledKilograms = feedwaterFilledKilograms;
            BatteryStoredJoules = batteryStoredJoules;
            ChargeNotStoredJoules = chargeNotStoredJoules;
            MakeupGasUsedKilograms = makeupGasUsedKilograms;
            UnservedOxygenKilograms = unservedOxygenKilograms;
            UnservedBatteryJoules = unservedBatteryJoules;
            UnservedFeedwaterKilograms = unservedFeedwaterKilograms;
        }
    }

    /// <summary>
    /// Phase 3 EVA model (suits + EVA supply). Implemented by D1; called ONLY by the session authority, which owns
    /// the single instance. Plain C#, single-threaded, no UnityEngine. It never decides who is outside, which task
    /// progresses or when the airlock cycles: it moves suit stocks for the demand it is given (Advance) and for
    /// identified transfers (Apply). Base stocks stay in the phase 2 IOutpostSimulation, unchanged.
    /// </summary>
    public interface IEvaSimulation
    {
        /// <summary>Time zero, initial suit levels and supplies; clears applied transfer ids. Null throws.</summary>
        void Reset(EvaConfiguration configuration);

        /// <summary>Requires Reset; immutable observation.</summary>
        EvaSnapshot Snapshot { get; }

        /// <summary>Requires Reset; diagnostic ledger since Reset.</summary>
        EvaLedger Ledger { get; }

        /// <summary>Requires Reset and finite, nonnegative seconds (zero is a no-op). Consumes, for each listed suit,
        /// activity rate (+ tool watts on the battery) × time, never below zero (unserved demand goes to the
        /// ledger). An unknown suit index or invalid input throws without changing state.</summary>
        void Advance(double elapsedSeconds, EvaDemand demand);

        /// <summary>Seconds until a listed suit service crosses its reserve or reaches zero at this demand (the
        /// authority splits its step there so the event time is exact); infinity when none will. Does not mutate.</summary>
        double SecondsUntilServiceEvent(EvaDemand demand);

        /// <summary>Applies <paramref name="transfer"/>; see <see cref="EvaTransferStatus"/>. Requires Reset.
        /// Never throws for stock or capacity.</summary>
        EvaTransferStatus Apply(EvaTransfer transfer);
    }
}
