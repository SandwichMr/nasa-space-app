using System;
using System.Collections.Generic;
using Trainer.Simulation.Incidents;

namespace Trainer.Simulation.BaseOperations
{
    /// <summary>
    /// Phase 4 equipment model (D1). Plain C#, single-threaded, no UnityEngine, owned and called ONLY by the session
    /// authority. Units: seconds, kilograms, µS/cm, fractions.
    ///
    /// What it owns: the water processor hold, the two sorbent beds, the inlet flow, the spare parts and the single
    /// scripted synthetic fault. What it never touches: the phase 2 stocks (energy, potable/untreated water, food, cabin
    /// gas). Water crosses between the base and the processor as a pair with the same id: a phase 2
    /// <c>ResourceTransfer</c> with boundary flow plus an <see cref="EquipmentEffect"/> (see
    /// <see cref="BaseOperationsTransfers"/>).
    ///
    /// Model (declared simplifications, SYNTHETIC parameters from <see cref="BaseEquipmentConfiguration"/>):
    /// <list type="bullet">
    /// <item>A batch becomes product (batch × recovery of the condition at that instant) held in the processor, and
    /// brine (the rest) that leaves the boundary for good. Held product keeps the conductivity it was made with.</item>
    /// <item>The scripted fault degrades the processor once at <c>DegradeAtSeconds</c>; without a filter replacement it
    /// fails <c>FailAfterDegradedSeconds</c> later (isolation does not stop that timer). A replacement returns it to
    /// Nominal and does not re-arm the script. No MTBF, statistics or real failure data.</item>
    /// <item>The active bed loads at <c>LoadingPerCrewSecond × CrewAboard</c> while the unit is Online, independent of
    /// inlet flow; the standby bed never loads. Removal factor = <c>BedFactor(active)</c> × inlet flow, 0 when isolated.
    /// Between the knee and full loading the factor falls continuously; the authority applies the factor published
    /// after each step, so results there depend on the authority step size (cut at knee/full by
    /// <see cref="SecondsUntilEquipmentEvent"/>).</item>
    /// </list>
    ///
    /// Every event is applied at its exact instant inside <see cref="Advance"/>; an event exactly at the end of the
    /// interval is applied in that Advance. Effects are all or nothing, once per id per Reset; a refused effect changes
    /// nothing and does not consume its id. Snapshots are immutable and replaced, never modified.
    ///
    /// Phase 5 (D1): also <see cref="IEquipmentIncidentTarget"/>, on this same object. A director degradation behaves like
    /// the scripted fault but starts at the current instant with its own optional fail timer (null = never fails); the
    /// phase 4 filter replacement clears it and cancels the timer. Spares can be consumed by phase 5 repairs. One bounded
    /// upgrade raises the NOMINAL recovery only. Phase 5 ids share the id space of <see cref="Apply"/>. Without phase 5
    /// calls the model is phase 4 unchanged.
    /// </summary>
    public sealed class BaseEquipmentSimulation : IBaseEquipmentModel, IEquipmentIncidentTarget
    {
        private const int MaxSegmentsPerAdvance = 16;

        private readonly SimulationClock clock = new SimulationClock();
        private readonly Dictionary<Guid, EquipmentEffect> applied = new Dictionary<Guid, EquipmentEffect>();
        private readonly Dictionary<Guid, IncidentRequest> appliedIncidents = new Dictionary<Guid, IncidentRequest>();

        private BaseEquipmentConfiguration configuration;
        private State state;
        private BaseEquipmentSnapshot snapshot;

        /// <summary>Configuration given to the last Reset (null before).</summary>
        public BaseEquipmentConfiguration Configuration => configuration;

        public BaseEquipmentSnapshot Snapshot
        {
            get
            {
                RequireReset();
                return snapshot;
            }
        }

        public void Reset(BaseEquipmentConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            this.configuration = configuration;
            clock.Reset();
            applied.Clear();
            appliedIncidents.Clear();
            var spares = new Dictionary<string, int>(StringComparer.Ordinal);
            var capacities = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (SpareStock spare in configuration.InitialSpares)
            {
                spares[spare.Key] = spare.Count;
                capacities[spare.Key] = spare.Capacity;
                order.Add(spare.Key);
            }
            state = new State
            {
                Condition = EquipmentCondition.Nominal,
                WaterOperating = EquipmentOperatingState.Online,
                Conductivity = InstrumentReading.Unavailable(ReadingUnit.MicrosiemensPerCentimeter),
                DegradeScriptPending = configuration.WaterProcessor.DegradeAtSeconds.HasValue,
                AirOperating = EquipmentOperatingState.Online,
                ActiveBed = SorbentBed.A,
                ActiveLoading = configuration.Air.InitialActiveLoadingFraction,
                StandbyLoading = configuration.Air.InitialStandbyLoadingFraction,
                InletFlow = 1d,
                InletScriptPending = configuration.Air.InletRestrictedAtSeconds.HasValue,
                Spares = spares,
                Capacities = capacities,
                SpareOrder = order
            };
            Publish();
        }

        public void Advance(double elapsedSeconds, EquipmentDemand demand)
        {
            RequireReset();
            if (demand == null) throw new ArgumentNullException(nameof(demand));
            SimulationClock.Validate(elapsedSeconds, clock.ElapsedSeconds);
            if (elapsedSeconds == 0d) return;

            State work = state.Copy();
            double now = clock.ElapsedSeconds;
            double end = now + elapsedSeconds;
            int segments = 0;
            FireDueEvents(ref work, now);
            while (now < end)
            {
                if (++segments > MaxSegmentsPerAdvance)
                    throw new InvalidOperationException("Equipment step did not converge; state left unchanged.");
                double dt = Math.Min(end - now, NextEvent(work, now, demand));
                bool reachesEnd = dt >= end - now;
                LoadBed(ref work, demand, dt);
                now = reachesEnd ? end : now + dt;
                FireDueEvents(ref work, now);
            }

            state = work;
            clock.Advance(elapsedSeconds);
            Publish();
        }

        public double SecondsUntilEquipmentEvent(EquipmentDemand demand)
        {
            RequireReset();
            if (demand == null) throw new ArgumentNullException(nameof(demand));
            return NextEvent(state, clock.ElapsedSeconds, demand);
        }

        public EquipmentEffectStatus Apply(EquipmentEffect effect)
        {
            RequireReset();
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            if (applied.TryGetValue(effect.Id, out EquipmentEffect previous))
                return previous.HasSameContent(effect) ? EquipmentEffectStatus.AlreadyApplied : EquipmentEffectStatus.IdConflict;
            if (appliedIncidents.ContainsKey(effect.Id)) return EquipmentEffectStatus.IdConflict;

            State work = state.Copy();
            EquipmentEffectStatus status = Evaluate(ref work, effect);
            if (status != EquipmentEffectStatus.Applied) return status;
            state = work;
            applied.Add(effect.Id, effect);
            Publish();
            return EquipmentEffectStatus.Applied;
        }

        // ---------------------------------------------------------------- phase 5 incident target

        public double ProcessorNominalRecovery
        {
            get
            {
                RequireReset();
                return state.UpgradedRecovery ?? configuration.WaterProcessor.NominalRecoveryFraction;
            }
        }

        public bool ProcessorUpgraded
        {
            get
            {
                RequireReset();
                return state.UpgradedRecovery.HasValue;
            }
        }

        public EquipmentEffectStatus DegradeProcessor(Guid id, double? failAfterSeconds)
        {
            RequireReset();
            if (failAfterSeconds.HasValue && (!IncidentGuard.IsFinite(failAfterSeconds.Value) || failAfterSeconds.Value <= 0d))
                throw new ArgumentOutOfRangeException(nameof(failAfterSeconds));
            return ApplyIncident(new IncidentRequest(IncidentRequestKind.Degrade, failAfterSeconds, null), id, (ref State s) =>
            {
                if (s.Condition != EquipmentCondition.Nominal) return EquipmentEffectStatus.InvalidForState;
                s.Condition = EquipmentCondition.Degraded;
                s.DegradedAt = clock.ElapsedSeconds;
                s.DirectorDegraded = true;
                s.DirectorFailAfter = failAfterSeconds;
                return EquipmentEffectStatus.Applied;
            });
        }

        public EquipmentEffectStatus ConsumeSpare(Guid id, string spareKey)
        {
            RequireReset();
            if (spareKey == null) throw new ArgumentNullException(nameof(spareKey));
            return ApplyIncident(new IncidentRequest(IncidentRequestKind.ConsumeSpare, null, spareKey), id, (ref State s) =>
            {
                if (!s.Spares.ContainsKey(spareKey)) return EquipmentEffectStatus.InvalidForState;
                return TakeSpare(ref s, spareKey) ? EquipmentEffectStatus.Applied : EquipmentEffectStatus.InsufficientSpares;
            });
        }

        public EquipmentEffectStatus UpgradeProcessorRecovery(Guid id, double recoveryFraction)
        {
            RequireReset();
            if (!IncidentGuard.IsFinite(recoveryFraction)) throw new ArgumentOutOfRangeException(nameof(recoveryFraction));
            return ApplyIncident(new IncidentRequest(IncidentRequestKind.Upgrade, recoveryFraction, null), id, (ref State s) =>
            {
                if (s.UpgradedRecovery.HasValue
                    || recoveryFraction <= configuration.WaterProcessor.NominalRecoveryFraction
                    || recoveryFraction > IncidentEffectLimits.MaxUpgradedRecoveryFraction)
                    return EquipmentEffectStatus.InvalidForState;
                s.UpgradedRecovery = recoveryFraction;
                return EquipmentEffectStatus.Applied;
            });
        }

        private delegate EquipmentEffectStatus IncidentChange(ref State s);

        private EquipmentEffectStatus ApplyIncident(IncidentRequest request, Guid id, IncidentChange change)
        {
            if (appliedIncidents.TryGetValue(id, out IncidentRequest previous))
                return previous.Equals(request) ? EquipmentEffectStatus.AlreadyApplied : EquipmentEffectStatus.IdConflict;
            if (applied.ContainsKey(id)) return EquipmentEffectStatus.IdConflict;
            State work = state.Copy();
            EquipmentEffectStatus status = change(ref work);
            if (status != EquipmentEffectStatus.Applied) return status;
            state = work;
            appliedIncidents.Add(id, request);
            Publish();
            return EquipmentEffectStatus.Applied;
        }

        private enum IncidentRequestKind { Degrade, ConsumeSpare, Upgrade }

        private readonly struct IncidentRequest : IEquatable<IncidentRequest>
        {
            private readonly IncidentRequestKind kind;
            private readonly double? value;
            private readonly string key;

            public IncidentRequest(IncidentRequestKind kind, double? value, string key)
            {
                this.kind = kind;
                this.value = value;
                this.key = key;
            }

            public bool Equals(IncidentRequest other) =>
                kind == other.kind && Nullable.Equals(value, other.value) && string.Equals(key, other.key, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is IncidentRequest other && Equals(other);

            public override int GetHashCode() => (int)kind;
        }

        // ---------------------------------------------------------------- effects

        private EquipmentEffectStatus Evaluate(ref State s, EquipmentEffect effect)
        {
            WaterProcessorConfiguration water = configuration.WaterProcessor;
            double now = clock.ElapsedSeconds;
            switch (effect.Kind)
            {
                case EquipmentEffectKind.ReceiveBatch:
                {
                    if (s.WaterOperating != EquipmentOperatingState.Online || s.Condition == EquipmentCondition.Failed
                        || s.Hold != 0d || effect.Kilograms != water.BatchKilograms)
                        return EquipmentEffectStatus.InvalidForState;
                    double product = effect.Kilograms * RecoveryOf(s.Condition);
                    if (product > water.HoldCapacityKilograms) return EquipmentEffectStatus.ExceedsCapacity;
                    s.Hold = product;
                    s.BatchInput += effect.Kilograms;
                    s.Brine += effect.Kilograms - product;
                    s.Conductivity = InstrumentReading.Valid(ReadingUnit.MicrosiemensPerCentimeter, ConductivityOf(s.Condition), now);
                    s.HoldConductivity = ConductivityOf(s.Condition);
                    return EquipmentEffectStatus.Applied;
                }
                case EquipmentEffectKind.ReleaseToPotable:
                case EquipmentEffectKind.ReleaseForReprocess:
                {
                    // Draining the hold is not a processing function: allowed while isolated or failed.
                    if (s.Hold == 0d || effect.Kilograms != s.Hold) return EquipmentEffectStatus.InvalidForState;
                    if (effect.Kind == EquipmentEffectKind.ReleaseToPotable) s.ReleasedToPotable += s.Hold;
                    else s.ReleasedForReprocess += s.Hold;
                    s.Hold = 0d;
                    s.Conductivity = InstrumentReading.Unavailable(ReadingUnit.MicrosiemensPerCentimeter);
                    return EquipmentEffectStatus.Applied;
                }
                case EquipmentEffectKind.SampleProduct:
                {
                    // Held product: fresh reading of that product. Empty hold: test run of the machine as it is now
                    // (a failed processor produces nothing to measure: Unavailable, never zero).
                    if (s.Hold > 0d)
                        s.Conductivity = InstrumentReading.Valid(ReadingUnit.MicrosiemensPerCentimeter, s.HoldConductivity, now);
                    else if (s.Condition == EquipmentCondition.Failed)
                        s.Conductivity = InstrumentReading.Unavailable(ReadingUnit.MicrosiemensPerCentimeter);
                    else
                        s.Conductivity = InstrumentReading.Valid(ReadingUnit.MicrosiemensPerCentimeter, ConductivityOf(s.Condition), now);
                    return EquipmentEffectStatus.Applied;
                }
                case EquipmentEffectKind.SetOperatingState:
                {
                    if (effect.Equipment == EquipmentKind.WaterProcessor)
                    {
                        if (s.WaterOperating == effect.OperatingState) return EquipmentEffectStatus.InvalidForState;
                        s.WaterOperating = effect.OperatingState;
                    }
                    else
                    {
                        if (s.AirOperating == effect.OperatingState) return EquipmentEffectStatus.InvalidForState;
                        s.AirOperating = effect.OperatingState;
                    }
                    return EquipmentEffectStatus.Applied;
                }
                case EquipmentEffectKind.ReplaceProcessorFilter:
                {
                    if (s.WaterOperating != EquipmentOperatingState.Isolated) return EquipmentEffectStatus.InvalidForState;
                    if (!TakeSpare(ref s, water.FilterSpareKey)) return EquipmentEffectStatus.InsufficientSpares;
                    s.Condition = EquipmentCondition.Nominal;
                    s.DegradedAt = null;
                    s.DegradeScriptPending = false;
                    s.DirectorDegraded = false;
                    s.DirectorFailAfter = null;
                    return EquipmentEffectStatus.Applied;
                }
                case EquipmentEffectKind.SwitchBed:
                {
                    if (s.AirOperating != EquipmentOperatingState.Online) return EquipmentEffectStatus.InvalidForState;
                    s.ActiveBed = s.ActiveBed == SorbentBed.A ? SorbentBed.B : SorbentBed.A;
                    double loading = s.ActiveLoading;
                    s.ActiveLoading = s.StandbyLoading;
                    s.StandbyLoading = loading;
                    return EquipmentEffectStatus.Applied;
                }
                case EquipmentEffectKind.RegenerateStandbyBed:
                    s.StandbyLoading = 0d;
                    return EquipmentEffectStatus.Applied;
                case EquipmentEffectKind.ReplaceInletFilter:
                {
                    if (s.AirOperating != EquipmentOperatingState.Isolated) return EquipmentEffectStatus.InvalidForState;
                    if (!TakeSpare(ref s, configuration.Air.InletFilterSpareKey)) return EquipmentEffectStatus.InsufficientSpares;
                    s.InletFlow = 1d;
                    s.InletScriptPending = false;
                    return EquipmentEffectStatus.Applied;
                }
                case EquipmentEffectKind.ReceiveSpares:
                {
                    if (!s.Spares.TryGetValue(effect.SpareKey, out int count)) return EquipmentEffectStatus.InvalidForState;
                    if (count + effect.Count > s.Capacities[effect.SpareKey]) return EquipmentEffectStatus.ExceedsCapacity;
                    s.Spares[effect.SpareKey] = count + effect.Count;
                    s.SparesReceived += effect.Count;
                    return EquipmentEffectStatus.Applied;
                }
                default:
                    return EquipmentEffectStatus.InvalidForState;
            }
        }

        private static bool TakeSpare(ref State s, string key)
        {
            if (!s.Spares.TryGetValue(key, out int count) || count < 1) return false;
            s.Spares[key] = count - 1;
            s.SparesConsumed++;
            return true;
        }

        private double RecoveryOf(EquipmentCondition condition) =>
            condition == EquipmentCondition.Nominal
                ? state.UpgradedRecovery ?? configuration.WaterProcessor.NominalRecoveryFraction
                : configuration.WaterProcessor.DegradedRecoveryFraction;

        /// <summary>Fail delay of the current degradation: the director's own timer, else the scripted one.</summary>
        private double? FailAfter(in State s) =>
            s.DirectorDegraded ? s.DirectorFailAfter : configuration.WaterProcessor.FailAfterDegradedSeconds;

        private double ConductivityOf(EquipmentCondition condition) =>
            condition == EquipmentCondition.Nominal
                ? configuration.WaterProcessor.NominalConductivityMicrosiemens
                : configuration.WaterProcessor.DegradedConductivityMicrosiemens;

        // ---------------------------------------------------------------- time

        /// <summary>Seconds from <paramref name="now"/> to the next rate or condition change (positive or infinity).
        /// A scripted time at or before <paramref name="now"/> is due, not upcoming: Advance fires it first.</summary>
        private double NextEvent(in State s, double now, EquipmentDemand demand)
        {
            double next = double.PositiveInfinity;
            WaterProcessorConfiguration water = configuration.WaterProcessor;
            if (s.DegradeScriptPending && water.DegradeAtSeconds.Value > now)
                next = Math.Min(next, water.DegradeAtSeconds.Value - now);
            double? failAfter = FailAfter(s);
            if (s.Condition == EquipmentCondition.Degraded && failAfter.HasValue)
            {
                double failAt = s.DegradedAt.Value + failAfter.Value;
                if (failAt > now) next = Math.Min(next, failAt - now);
            }
            AirRevitalizationConfiguration air = configuration.Air;
            if (s.InletScriptPending && air.InletRestrictedAtSeconds.Value > now)
                next = Math.Min(next, air.InletRestrictedAtSeconds.Value - now);
            double rate = LoadingRate(s, demand);
            if (rate > 0d)
            {
                if (s.ActiveLoading < air.KneeLoadingFraction)
                    next = Math.Min(next, (air.KneeLoadingFraction - s.ActiveLoading) / rate);
                else if (s.ActiveLoading < 1d)
                    next = Math.Min(next, (1d - s.ActiveLoading) / rate);
            }
            return next;
        }

        private double LoadingRate(in State s, EquipmentDemand demand) =>
            s.AirOperating == EquipmentOperatingState.Online ? configuration.Air.LoadingPerCrewSecond * demand.CrewAboard : 0d;

        /// <summary>Loads the active bed for <paramref name="dt"/>; lands exactly on the knee or full loading when that
        /// boundary is reached within the segment.</summary>
        private void LoadBed(ref State s, EquipmentDemand demand, double dt)
        {
            double rate = LoadingRate(s, demand);
            if (rate <= 0d || s.ActiveLoading >= 1d) return;
            double knee = configuration.Air.KneeLoadingFraction;
            double boundary = s.ActiveLoading < knee ? knee : 1d;
            double reached = s.ActiveLoading + rate * dt;
            s.ActiveLoading = (boundary - s.ActiveLoading) / rate <= dt ? boundary : Math.Min(1d, reached);
        }

        /// <summary>Applies every scripted change whose time is at or before <paramref name="now"/>, at its own time.</summary>
        private void FireDueEvents(ref State s, double now)
        {
            WaterProcessorConfiguration water = configuration.WaterProcessor;
            if (s.DegradeScriptPending && water.DegradeAtSeconds.Value <= now)
            {
                s.DegradeScriptPending = false;
                if (s.Condition == EquipmentCondition.Nominal)
                {
                    s.Condition = EquipmentCondition.Degraded;
                    s.DegradedAt = water.DegradeAtSeconds.Value;
                }
            }
            double? failAfter = FailAfter(s);
            if (s.Condition == EquipmentCondition.Degraded && failAfter.HasValue
                && s.DegradedAt.Value + failAfter.Value <= now)
                s.Condition = EquipmentCondition.Failed;
            AirRevitalizationConfiguration air = configuration.Air;
            if (s.InletScriptPending && air.InletRestrictedAtSeconds.Value <= now)
            {
                s.InletScriptPending = false;
                s.InletFlow = air.RestrictedFlowFraction;
            }
        }

        // ---------------------------------------------------------------- publication

        private void Publish()
        {
            EquipmentFault fault = state.Condition == EquipmentCondition.Nominal ? EquipmentFault.None : EquipmentFault.ProcessorFilterRestricted;
            var water = new WaterProcessorState(state.Condition, state.WaterOperating, fault, state.Hold, state.Conductivity, state.DegradedAt);
            double factor = state.AirOperating == EquipmentOperatingState.Online
                ? configuration.Air.BedFactor(state.ActiveLoading) * state.InletFlow
                : 0d;
            var air = new AirRevitalizationState(state.AirOperating, state.ActiveBed, state.ActiveLoading, state.StandbyLoading,
                state.InletFlow, factor);
            var spares = new List<SpareStock>(state.SpareOrder.Count);
            foreach (string key in state.SpareOrder)
                spares.Add(new SpareStock(key, state.Spares[key], state.Capacities[key]));
            var ledger = new EquipmentLedger(state.BatchInput, state.Brine, state.ReleasedToPotable, state.ReleasedForReprocess,
                state.SparesConsumed, state.SparesReceived);
            snapshot = new BaseEquipmentSnapshot(clock.ElapsedSeconds, water, air, spares, ledger);
        }

        private void RequireReset()
        {
            if (configuration == null)
                throw new InvalidOperationException("Reset must be called before using the equipment model.");
        }

        /// <summary>Mutable model state; <see cref="Copy"/> lets a step or an effect work on a copy and commit atomically.</summary>
        private struct State
        {
            public EquipmentCondition Condition;
            public EquipmentOperatingState WaterOperating;
            public double? DegradedAt;
            public bool DegradeScriptPending;
            public bool DirectorDegraded;
            public double? DirectorFailAfter;
            public double? UpgradedRecovery;
            public double Hold;
            public double HoldConductivity;
            public InstrumentReading Conductivity;
            public EquipmentOperatingState AirOperating;
            public SorbentBed ActiveBed;
            public double ActiveLoading, StandbyLoading, InletFlow;
            public bool InletScriptPending;
            public Dictionary<string, int> Spares;
            public Dictionary<string, int> Capacities;
            public List<string> SpareOrder;
            public double BatchInput, Brine, ReleasedToPotable, ReleasedForReprocess;
            public int SparesConsumed, SparesReceived;

            public State Copy()
            {
                State copy = this;
                copy.Spares = new Dictionary<string, int>(Spares, StringComparer.Ordinal);
                return copy;
            }
        }
    }
}
