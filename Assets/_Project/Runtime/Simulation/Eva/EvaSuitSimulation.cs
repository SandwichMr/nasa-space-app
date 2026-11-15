using System;
using System.Collections.Generic;

namespace Trainer.Simulation.Eva
{
    /// <summary>
    /// Phase 3 EVA model (D1): suit services and EVA supplies. Plain C#, single-threaded, owned and called only by
    /// the session authority. Units: seconds, kilograms, joules, watts.
    ///
    /// Who applies what (each cost exactly once):
    /// <list type="bullet">
    /// <item>Consumption of a suit on its own supply: here, in <see cref="Advance"/>, ONLY for the suits the authority
    /// lists in <see cref="EvaDemand"/> (sealed wearers). Unlisted suits never consume, so a member is charged either
    /// to the base (phase 2 CrewAboard) or to one suit, never both.</item>
    /// <item>Charging, filling and make-up gas: here, in <see cref="Apply"/>, once per transfer id per Reset. The base
    /// energy of charging and the base water of filling are applied by the phase 2 base model, not here.</item>
    /// <item>Who is sealed, which activity, when to apply: the authority (D2), never this class.</item>
    /// </list>
    ///
    /// Within one Advance each listed suit has a constant activity, so every rate is constant until a level reaches
    /// zero; integration is exact (consumed = min(level, rate × t)) and independent of step size. Demand that finds a
    /// service empty is reported as unserved in the ledger, never taken from another service or another suit
    /// (there is no O2 or battery transfer between suits). Not modelled: CO2 removal, heat balance, suit pressure,
    /// leaks, failures, any clinical consequence.
    /// </summary>
    public sealed class EvaSuitSimulation : IEvaSimulation
    {
        private const double CapacityTolerance = 1e-9;

        private readonly SimulationClock clock = new SimulationClock();
        private readonly Dictionary<Guid, EvaTransfer> applied = new Dictionary<Guid, EvaTransfer>();
        private EvaConfiguration configuration;
        private SuitLevels[] levels;
        private bool[] onInternalSupply;
        private double oxygenRecharge, makeupGas;
        private Totals totals;
        private EvaSnapshot snapshot;

        public EvaSnapshot Snapshot
        {
            get
            {
                RequireReset();
                return snapshot;
            }
        }

        public EvaLedger Ledger
        {
            get
            {
                RequireReset();
                return new EvaLedger(totals.OxygenConsumed, totals.BatteryConsumed, totals.FeedwaterConsumed,
                    totals.OxygenFilled, totals.FeedwaterFilled, totals.BatteryStored, totals.ChargeNotStored,
                    totals.MakeupGasUsed, totals.UnservedOxygen, totals.UnservedBattery, totals.UnservedFeedwater);
            }
        }

        /// <summary>The configuration of the last Reset (model and capacities).</summary>
        public EvaConfiguration Configuration
        {
            get
            {
                RequireReset();
                return configuration;
            }
        }

        public void Reset(EvaConfiguration value)
        {
            configuration = value ?? throw new ArgumentNullException(nameof(value));
            clock.Reset();
            applied.Clear();
            levels = new SuitLevels[value.Suits.Count];
            onInternalSupply = new bool[value.Suits.Count];
            for (int i = 0; i < levels.Length; i++) levels[i] = value.Suits[i].Initial;
            oxygenRecharge = value.Supply.OxygenRechargeKilograms;
            makeupGas = value.Supply.MakeupGasKilograms;
            totals = default;
            Publish();
        }

        public void Advance(double elapsedSeconds, EvaDemand demand)
        {
            RequireReset();
            if (demand == null) throw new ArgumentNullException(nameof(demand));
            SimulationClock.Validate(elapsedSeconds, clock.ElapsedSeconds);
            foreach (SuitDemand suit in demand.Suits)
                if (suit.SuitIndex >= levels.Length) throw new ArgumentOutOfRangeException(nameof(demand), $"Unknown suit {suit.SuitIndex}.");
            if (elapsedSeconds == 0d) return; // Zero is a no-op, including the supply flags.

            SuitModelConfiguration model = configuration.Model;
            Totals work = totals;
            var next = (SuitLevels[])levels.Clone();
            var supplied = new bool[levels.Length];
            foreach (SuitDemand suit in demand.Suits)
            {
                supplied[suit.SuitIndex] = true;
                SuitLevels current = next[suit.SuitIndex];
                foreach (SuitServiceModel service in model.Services)
                {
                    double requested = Rate(service, suit.Activity, suit.ToolWatts) * elapsedSeconds;
                    double level = current.Get(service.Service);
                    double consumed = Math.Min(level, requested);
                    current = current.With(service.Service, Math.Max(0d, level - consumed));
                    work.Add(service.Service, consumed, requested - consumed);
                }
                next[suit.SuitIndex] = current;
            }
            levels = next;
            onInternalSupply = supplied;
            totals = work;
            clock.Advance(elapsedSeconds);
            Publish();
        }

        public double SecondsUntilServiceEvent(EvaDemand demand)
        {
            RequireReset();
            if (demand == null) throw new ArgumentNullException(nameof(demand));
            double next = double.PositiveInfinity;
            foreach (SuitDemand suit in demand.Suits)
            {
                if (suit.SuitIndex >= levels.Length) throw new ArgumentOutOfRangeException(nameof(demand), $"Unknown suit {suit.SuitIndex}.");
                foreach (SuitServiceModel service in configuration.Model.Services)
                {
                    double rate = Rate(service, suit.Activity, suit.ToolWatts);
                    double level = levels[suit.SuitIndex].Get(service.Service);
                    // First the reserve crossing, then the zero crossing (both exact event times).
                    if (level > service.Reserve) next = Math.Min(next, (level - service.Reserve) / rate);
                    else if (level > 0d) next = Math.Min(next, level / rate);
                }
            }
            return next;
        }

        public EvaTransferStatus Apply(EvaTransfer transfer)
        {
            RequireReset();
            if (transfer == null) throw new ArgumentNullException(nameof(transfer));
            if (applied.TryGetValue(transfer.Id, out EvaTransfer previous))
                return previous.HasSameContent(transfer) ? EvaTransferStatus.AlreadyApplied : EvaTransferStatus.IdConflict;
            if (transfer.Kind != EvaTransferKind.UseMakeupGas && transfer.SuitIndex >= levels.Length)
                return EvaTransferStatus.UnknownSuit;

            SuitModelConfiguration model = configuration.Model;
            switch (transfer.Kind)
            {
                case EvaTransferKind.ChargeBattery:
                {
                    SuitLevels suit = levels[transfer.SuitIndex];
                    double room = Math.Max(0d, model.Get(SuitService.Battery).Capacity - suit.BatteryJoules);
                    double stored = Math.Min(room, transfer.Amount);
                    levels[transfer.SuitIndex] = suit.With(SuitService.Battery, suit.BatteryJoules + stored);
                    totals.BatteryStored += stored;
                    totals.ChargeNotStored += transfer.Amount - stored; // Energy balance: stored + not stored = delivered.
                    break;
                }
                case EvaTransferKind.FillOxygen:
                {
                    if (oxygenRecharge - transfer.Amount < -Tolerance(configuration.Supply.OxygenRechargeCapacityKilograms))
                        return EvaTransferStatus.InsufficientStock;
                    if (!Fits(SuitService.Oxygen, transfer)) return EvaTransferStatus.ExceedsCapacity;
                    oxygenRecharge = Math.Max(0d, oxygenRecharge - transfer.Amount);
                    Fill(SuitService.Oxygen, transfer);
                    totals.OxygenFilled += transfer.Amount;
                    break;
                }
                case EvaTransferKind.FillFeedwater:
                {
                    if (!Fits(SuitService.Feedwater, transfer)) return EvaTransferStatus.ExceedsCapacity;
                    Fill(SuitService.Feedwater, transfer);
                    totals.FeedwaterFilled += transfer.Amount;
                    break;
                }
                case EvaTransferKind.UseMakeupGas:
                {
                    if (makeupGas - transfer.Amount < -Tolerance(configuration.Supply.MakeupGasCapacityKilograms))
                        return EvaTransferStatus.InsufficientStock;
                    makeupGas = Math.Max(0d, makeupGas - transfer.Amount);
                    totals.MakeupGasUsed += transfer.Amount;
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(transfer));
            }
            applied.Add(transfer.Id, transfer);
            Publish();
            return EvaTransferStatus.Applied;
        }

        /// <summary>Consumption per second of one service: activity rate, plus the tool load on the battery only.</summary>
        public static double Rate(SuitServiceModel service, SuitActivity activity, double toolWatts) =>
            service.RateFor(activity) + (service.Service == SuitService.Battery ? toolWatts : 0d);

        private bool Fits(SuitService service, EvaTransfer transfer)
        {
            double capacity = configuration.Model.Get(service).Capacity;
            return levels[transfer.SuitIndex].Get(service) + transfer.Amount <= capacity + Tolerance(capacity);
        }

        /// <summary>Adds within capacity (a deficit computed as capacity − level may round one ulp above it).</summary>
        private void Fill(SuitService service, EvaTransfer transfer)
        {
            SuitLevels suit = levels[transfer.SuitIndex];
            double capacity = configuration.Model.Get(service).Capacity;
            levels[transfer.SuitIndex] = suit.With(service, Math.Min(capacity, suit.Get(service) + transfer.Amount));
        }

        private static double Tolerance(double scale) => CapacityTolerance * Math.Max(1d, scale);

        private void Publish()
        {
            var readings = new SuitReading[levels.Length];
            for (int i = 0; i < levels.Length; i++)
                readings[i] = SuitReading.From(i, configuration.Suits[i].Key, levels[i], onInternalSupply[i], configuration.Model);
            snapshot = new EvaSnapshot(clock.ElapsedSeconds, readings, new EvaSupplyReading(oxygenRecharge, makeupGas));
        }

        private void RequireReset()
        {
            if (configuration == null) throw new InvalidOperationException("Reset must be called before using the EVA simulation.");
        }

        private struct Totals
        {
            public double OxygenConsumed, BatteryConsumed, FeedwaterConsumed;
            public double OxygenFilled, FeedwaterFilled, BatteryStored, ChargeNotStored, MakeupGasUsed;
            public double UnservedOxygen, UnservedBattery, UnservedFeedwater;

            public void Add(SuitService service, double consumed, double unserved)
            {
                switch (service)
                {
                    case SuitService.Oxygen: OxygenConsumed += consumed; UnservedOxygen += unserved; break;
                    case SuitService.Battery: BatteryConsumed += consumed; UnservedBattery += unserved; break;
                    case SuitService.Feedwater: FeedwaterConsumed += consumed; UnservedFeedwater += unserved; break;
                }
            }
        }
    }
}
