using System;
using System.Collections.Generic;
using Trainer.Application.BaseOperations;
using Trainer.Application.Development;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;
using static Trainer.Tooling.Validation.Phase04.Phase04Checks;

namespace Trainer.Tooling.Validation.Phase04
{
    /// <summary>
    /// D5 conformance of the REAL D1 equipment model (<see cref="IBaseEquipmentModel"/>) and of the air coupling
    /// (<see cref="IAirServiceCapacity"/> on the base model), driven with the labelled synthetic fixture configuration.
    /// The checks follow SESSION_CONTRACT §3 and the cost table: product = batch × recovery of the current condition, the rest
    /// leaves the boundary and never comes back; effects are idempotent by id; the scheduled fault is reproducible; a
    /// replacement consumes exactly one spare and restores the condition without refunding lost mass; an isolated air unit
    /// removes nothing; factor 1 is phase 2 behaviour.
    /// </summary>
    public static class BaseEquipmentConformance
    {
        private const double Tol = 1e-6;
        private static readonly EquipmentDemand Nobody = new EquipmentDemand(0);
        private static readonly EquipmentDemand TwoAboard = new EquipmentDemand(2);

        public static List<Phase04Check> RunAll(Func<IBaseEquipmentModel> create, string name)
        {
            if (create == null) throw new ArgumentNullException(nameof(create));
            string k = Phase04Kinds.EquipmentConformance;
            BaseEquipmentConfiguration cfg = BaseOperationsFixtures.Equipment(120);
            WaterProcessorConfiguration w = cfg.WaterProcessor;
            AirRevitalizationConfiguration a = cfg.Air;
            IBaseEquipmentModel Fresh(BaseEquipmentConfiguration c = null)
            {
                IBaseEquipmentModel m = create();
                m.Reset(c ?? cfg);
                return m;
            }
            var checks = new List<Phase04Check>
            {
                Info("equipment model under test", k, name + " with " + BaseOperationsFixtures.ScenarioId + " (synthetic)"),

                Run("reset publishes the configured initial state", k, () =>
                {
                    BaseEquipmentSnapshot s = Fresh().Snapshot;
                    Require(s != null, "no snapshot after Reset");
                    Near(0, s.SimulatedTimeSeconds, Tol, "time");
                    Require(s.WaterProcessor.Condition == EquipmentCondition.Nominal && s.WaterProcessor.Fault == EquipmentFault.None, "processor not nominal");
                    Require(s.WaterProcessor.Operating == EquipmentOperatingState.Online && s.Air.Operating == EquipmentOperatingState.Online, "not online");
                    Near(0, s.WaterProcessor.HoldKilograms, Tol, "hold");
                    Near(a.InitialActiveLoadingFraction, s.Air.ActiveLoadingFraction, Tol, "active bed");
                    Near(a.InitialStandbyLoadingFraction, s.Air.StandbyLoadingFraction, Tol, "standby bed");
                    Near(1, s.Air.InletFlowFraction, Tol, "inlet flow");
                    Near(a.BedFactor(s.Air.ActiveLoadingFraction) * s.Air.InletFlowFraction, s.Air.RemovalFactor, Tol, "removal factor = bed factor × flow");
                    foreach (SpareStock spare in cfg.InitialSpares)
                        Require(s.TryGetSpare(spare.Key, out SpareStock got) && got.Count == spare.Count, "spare " + spare.Key);
                    Require(s.Ledger.BatchInputKilograms == 0 && s.Ledger.BrineOutflowKilograms == 0 && s.Ledger.SparesConsumed == 0, "ledger not zero");
                }),

                Run("batch: product = batch × nominal recovery, the rest leaves the boundary as brine", k, () =>
                {
                    IBaseEquipmentModel m = Fresh();
                    Require(m.Apply(EquipmentEffect.ReceiveBatch(Guid.NewGuid(), w.BatchKilograms)) == EquipmentEffectStatus.Applied, "batch not applied");
                    BaseEquipmentSnapshot s = m.Snapshot;
                    Near(w.BatchKilograms * w.NominalRecoveryFraction, s.WaterProcessor.HoldKilograms, Tol, "hold");
                    Near(w.BatchKilograms, s.Ledger.BatchInputKilograms, Tol, "ledger input");
                    Near(w.BatchKilograms * (1 - w.NominalRecoveryFraction), s.Ledger.BrineOutflowKilograms, Tol, "ledger brine");
                    return $"hold {s.WaterProcessor.HoldKilograms:0.###} kg, brine {s.Ledger.BrineOutflowKilograms:0.###} kg";
                }),

                Run("effects are idempotent by id (same content AlreadyApplied, other content IdConflict)", k, () =>
                {
                    IBaseEquipmentModel m = Fresh();
                    Guid id = Guid.NewGuid();
                    m.Apply(EquipmentEffect.ReceiveBatch(id, w.BatchKilograms));
                    double hold = m.Snapshot.WaterProcessor.HoldKilograms;
                    Require(m.Apply(EquipmentEffect.ReceiveBatch(id, w.BatchKilograms)) == EquipmentEffectStatus.AlreadyApplied, "repeat not AlreadyApplied");
                    Require(m.Apply(EquipmentEffect.ReceiveBatch(id, w.BatchKilograms / 2)) == EquipmentEffectStatus.IdConflict, "other content not IdConflict");
                    Near(hold, m.Snapshot.WaterProcessor.HoldKilograms, Tol, "hold after repeats");
                }),

                Run("release empties the hold and the ledger balances (input = brine + released + held)", k, () =>
                {
                    IBaseEquipmentModel m = Fresh();
                    m.Apply(EquipmentEffect.ReceiveBatch(Guid.NewGuid(), w.BatchKilograms));
                    double held = m.Snapshot.WaterProcessor.HoldKilograms;
                    Require(m.Apply(EquipmentEffect.ReleaseToPotable(Guid.NewGuid(), held)) == EquipmentEffectStatus.Applied, "release not applied");
                    m.Apply(EquipmentEffect.ReceiveBatch(Guid.NewGuid(), w.BatchKilograms));
                    double again = m.Snapshot.WaterProcessor.HoldKilograms;
                    Require(m.Apply(EquipmentEffect.ReleaseForReprocess(Guid.NewGuid(), again)) == EquipmentEffectStatus.Applied, "reprocess release not applied");
                    BaseEquipmentSnapshot s = m.Snapshot;
                    Near(0, s.WaterProcessor.HoldKilograms, Tol, "hold");
                    EquipmentLedger l = s.Ledger;
                    Near(l.BatchInputKilograms, l.BrineOutflowKilograms + l.ReleasedToPotableKilograms + l.ReleasedForReprocessKilograms
                        + s.WaterProcessor.HoldKilograms, Tol, "mass balance");
                }),

                Run("cannot release more than held, nor receive more than the hold fits", k, () =>
                {
                    IBaseEquipmentModel m = Fresh();
                    m.Apply(EquipmentEffect.ReceiveBatch(Guid.NewGuid(), w.BatchKilograms));
                    double held = m.Snapshot.WaterProcessor.HoldKilograms;
                    Require(m.Apply(EquipmentEffect.ReleaseToPotable(Guid.NewGuid(), held + 1)) != EquipmentEffectStatus.Applied, "released more than held");
                    Require(m.Apply(EquipmentEffect.ReceiveBatch(Guid.NewGuid(), w.HoldCapacityKilograms)) != EquipmentEffectStatus.Applied,
                        "received beyond the hold capacity");
                    Near(held, m.Snapshot.WaterProcessor.HoldKilograms, Tol, "hold unchanged");
                }),

                Run("synthetic fault: degraded exactly at DegradeAtSeconds (cut the step at the announced event)", k, () =>
                {
                    IBaseEquipmentModel m = Fresh();
                    double until = m.SecondsUntilEquipmentEvent(Nobody);
                    Require(!double.IsNaN(until) && until > 0, "SecondsUntilEquipmentEvent " + until);
                    Near(w.DegradeAtSeconds.Value, until, 1e-6, "seconds until the scheduled degradation");
                    m.Advance(until * 0.999, Nobody);
                    Require(m.Snapshot.WaterProcessor.Condition == EquipmentCondition.Nominal, "degraded early");
                    m.Advance(m.SecondsUntilEquipmentEvent(Nobody), Nobody);
                    WaterProcessorState p = m.Snapshot.WaterProcessor;
                    Require(p.Condition == EquipmentCondition.Degraded && p.Fault == EquipmentFault.ProcessorFilterRestricted, "not degraded: " + p.Condition);
                    Near(w.DegradeAtSeconds.Value, p.DegradedAtSeconds ?? -1, 1e-6, "degraded at");
                }),

                Run("degraded: lower recovery and an off-spec product reading", k, () =>
                {
                    IBaseEquipmentModel m = Fresh();
                    AdvanceTo(m, w.DegradeAtSeconds.Value + 1, Nobody);
                    m.Apply(EquipmentEffect.ReceiveBatch(Guid.NewGuid(), w.BatchKilograms));
                    Near(w.BatchKilograms * w.DegradedRecoveryFraction, m.Snapshot.WaterProcessor.HoldKilograms, Tol, "degraded hold");
                    m.Apply(EquipmentEffect.SampleProduct(Guid.NewGuid()));
                    InstrumentReading c = m.Snapshot.WaterProcessor.ProductConductivity;
                    Require(c.Status == ReadingStatus.Valid && c.Value.HasValue, "no valid conductivity after a sample");
                    Require(c.Value.Value > w.ProductSpecLimitMicrosiemens, $"degraded product {c.Value} µS/cm not above the line");
                    return $"{c.Value:0.#} µS/cm against {w.ProductSpecLimitMicrosiemens} µS/cm";
                }),

                Run("unrepaired: Failed after FailAfterDegradedSeconds, and no batch is accepted", k, () =>
                {
                    IBaseEquipmentModel m = Fresh();
                    AdvanceTo(m, w.DegradeAtSeconds.Value + w.FailAfterDegradedSeconds.Value + 1, Nobody);
                    Require(m.Snapshot.WaterProcessor.Condition == EquipmentCondition.Failed, "condition " + m.Snapshot.WaterProcessor.Condition);
                    Require(m.Apply(EquipmentEffect.ReceiveBatch(Guid.NewGuid(), w.BatchKilograms)) != EquipmentEffectStatus.Applied, "failed processor took a batch");
                }),

                Run("filter replacement consumes one spare, restores nominal and refunds nothing", k, () =>
                {
                    IBaseEquipmentModel m = Fresh();
                    AdvanceTo(m, w.DegradeAtSeconds.Value + 1, Nobody);
                    m.Apply(EquipmentEffect.ReceiveBatch(Guid.NewGuid(), w.BatchKilograms));
                    m.Apply(EquipmentEffect.ReleaseForReprocess(Guid.NewGuid(), m.Snapshot.WaterProcessor.HoldKilograms));
                    double brine = m.Snapshot.Ledger.BrineOutflowKilograms;
                    m.Snapshot.TryGetSpare(w.FilterSpareKey, out SpareStock before);
                    Require(m.Apply(EquipmentEffect.SetOperatingState(Guid.NewGuid(), EquipmentKind.WaterProcessor, EquipmentOperatingState.Isolated))
                            == EquipmentEffectStatus.Applied, "isolate not applied");
                    Require(m.Apply(EquipmentEffect.ReplaceProcessorFilter(Guid.NewGuid())) == EquipmentEffectStatus.Applied, "replacement not applied");
                    BaseEquipmentSnapshot s = m.Snapshot;
                    s.TryGetSpare(w.FilterSpareKey, out SpareStock after);
                    Require(after != null && before != null && after.Count == before.Count - 1, "spare count " + before?.Count + " -> " + after?.Count);
                    Require(s.Ledger.SparesConsumed == 1, "ledger spares consumed " + s.Ledger.SparesConsumed);
                    Require(s.WaterProcessor.Condition == EquipmentCondition.Nominal, "condition " + s.WaterProcessor.Condition);
                    Near(brine, s.Ledger.BrineOutflowKilograms, Tol, "brine refunded");
                    Require(m.Apply(EquipmentEffect.ReplaceProcessorFilter(Guid.NewGuid())) == EquipmentEffectStatus.InsufficientSpares,
                        "second replacement without spare not InsufficientSpares");
                    Require(m.Apply(EquipmentEffect.SetOperatingState(Guid.NewGuid(), EquipmentKind.WaterProcessor, EquipmentOperatingState.Online))
                            == EquipmentEffectStatus.Applied, "back online not applied");
                }),

                Run("received spares never exceed the capacity", k, () =>
                {
                    IBaseEquipmentModel m = Fresh();
                    m.Snapshot.TryGetSpare(w.FilterSpareKey, out SpareStock before);
                    EquipmentEffectStatus one = m.Apply(EquipmentEffect.ReceiveSpares(Guid.NewGuid(), w.FilterSpareKey, 1));
                    Require(one == EquipmentEffectStatus.Applied, "one spare not applied: " + one);
                    m.Apply(EquipmentEffect.ReceiveSpares(Guid.NewGuid(), w.FilterSpareKey, before.Capacity));
                    m.Snapshot.TryGetSpare(w.FilterSpareKey, out SpareStock after);
                    Require(after.Count <= after.Capacity, $"{after.Count} > capacity {after.Capacity}");
                    Require(m.Snapshot.Ledger.SparesReceived >= 1, "ledger spares received");
                }),

                Run("air: the active bed loads with the crew aboard and the factor follows BedFactor", k, () =>
                {
                    IBaseEquipmentModel m = Fresh();
                    AdvanceTo(m, 10, TwoAboard);
                    AirRevitalizationState air = m.Snapshot.Air;
                    Near(a.InitialActiveLoadingFraction + a.LoadingPerCrewSecond * 2 * 10, air.ActiveLoadingFraction, 1e-6, "active loading after 10 s with 2 aboard");
                    Near(a.BedFactor(air.ActiveLoadingFraction) * air.InletFlowFraction, air.RemovalFactor, Tol, "removal factor");
                    AdvanceTo(m, 200, TwoAboard);
                    air = m.Snapshot.Air;
                    Require(air.ActiveLoadingFraction > a.KneeLoadingFraction && air.RemovalFactor < 1, "no reduction above the knee");
                    return $"loading {air.ActiveLoadingFraction:0.###}, factor {air.RemovalFactor:0.###}";
                }),

                Run("air: an isolated unit removes nothing; switch swaps the beds; regeneration empties the standby", k, () =>
                {
                    IBaseEquipmentModel m = Fresh();
                    AdvanceTo(m, 100, TwoAboard);
                    AirRevitalizationState before = m.Snapshot.Air;
                    Require(m.Apply(EquipmentEffect.SwitchBed(Guid.NewGuid())) == EquipmentEffectStatus.Applied, "switch not applied");
                    AirRevitalizationState switched = m.Snapshot.Air;
                    Require(switched.ActiveBed != before.ActiveBed, "active bed unchanged");
                    Near(before.StandbyLoadingFraction, switched.ActiveLoadingFraction, Tol, "new active");
                    Near(before.ActiveLoadingFraction, switched.StandbyLoadingFraction, Tol, "new standby");
                    Require(m.Apply(EquipmentEffect.RegenerateStandbyBed(Guid.NewGuid())) == EquipmentEffectStatus.Applied, "regeneration not applied");
                    Near(0, m.Snapshot.Air.StandbyLoadingFraction, Tol, "standby after regeneration");
                    m.Apply(EquipmentEffect.SetOperatingState(Guid.NewGuid(), EquipmentKind.AirRevitalization, EquipmentOperatingState.Isolated));
                    Near(0, m.Snapshot.Air.RemovalFactor, 0, "isolated factor");
                }),

                Run("inlet restriction (scheduled) and its replacement restore the flow", k, () =>
                {
                    var air = new AirRevitalizationConfiguration(a.LoadingPerCrewSecond, a.KneeLoadingFraction, a.SaturatedRemovalFactor,
                        a.InitialActiveLoadingFraction, a.InitialStandbyLoadingFraction, 50, a.RestrictedFlowFraction, a.InletFilterSpareKey);
                    IBaseEquipmentModel m = Fresh(new BaseEquipmentConfiguration(w, air, cfg.InitialSpares, cfg.Provenance));
                    AdvanceTo(m, 60, Nobody);
                    Near(a.RestrictedFlowFraction, m.Snapshot.Air.InletFlowFraction, Tol, "restricted flow");
                    m.Apply(EquipmentEffect.SetOperatingState(Guid.NewGuid(), EquipmentKind.AirRevitalization, EquipmentOperatingState.Isolated));
                    Require(m.Apply(EquipmentEffect.ReplaceInletFilter(Guid.NewGuid())) == EquipmentEffectStatus.Applied, "replacement not applied");
                    Near(1, m.Snapshot.Air.InletFlowFraction, Tol, "flow after replacement");
                }),

                Run("reset is reproducible (same configuration and steps, same state)", k, () =>
                {
                    IBaseEquipmentModel x = Fresh(), y = Fresh();
                    foreach (IBaseEquipmentModel m in new[] { x, y })
                    {
                        AdvanceTo(m, 150, TwoAboard);
                        m.Apply(EquipmentEffect.ReceiveBatch(new Guid("5a1e0000-0000-0000-0000-000000000001"), w.BatchKilograms));
                        AdvanceTo(m, 400, TwoAboard);
                    }
                    Near(x.Snapshot.WaterProcessor.HoldKilograms, y.Snapshot.WaterProcessor.HoldKilograms, 0, "hold");
                    Near(x.Snapshot.Air.ActiveLoadingFraction, y.Snapshot.Air.ActiveLoadingFraction, 0, "active bed");
                    Require(x.Snapshot.WaterProcessor.Condition == y.Snapshot.WaterProcessor.Condition, "condition");
                    x.Reset(cfg);
                    Near(0, x.Snapshot.SimulatedTimeSeconds, 0, "time after Reset");
                    Require(x.Snapshot.WaterProcessor.Condition == EquipmentCondition.Nominal, "Reset did not restore the condition");
                })
            };
            return checks;
        }

        /// <summary>Advances to <paramref name="time"/> cutting each step at the announced equipment event (like the authority).</summary>
        public static void AdvanceTo(IBaseEquipmentModel m, double time, EquipmentDemand demand)
        {
            for (int guard = 0; guard < 10000 && m.Snapshot.SimulatedTimeSeconds < time - 1e-9; guard++)
            {
                double next = m.SecondsUntilEquipmentEvent(demand);
                if (double.IsNaN(next) || next < 0) throw new Phase04CheckFailed("SecondsUntilEquipmentEvent returned " + next);
                double dt = Math.Min(time - m.Snapshot.SimulatedTimeSeconds, Math.Min(next <= 0 ? 1e-6 : next, 10));
                m.Advance(dt, demand);
            }
        }

        /// <summary>IAirServiceCapacity on the REAL base model: factor 1 = phase 2 identical, factor 0 lets CO2 rise faster.</summary>
        public static List<Phase04Check> RunAirCoupling(Func<IOutpostSimulation> create, OutpostConfiguration configuration, string name)
        {
            string k = Phase04Kinds.AirCoupling;
            var demand = new OutpostDemand(2, false, 0);
            return new List<Phase04Check>
            {
                Run("factor 1 after Reset; [0,1] enforced", k, () =>
                {
                    IOutpostSimulation m = create();
                    if (!(m is IAirServiceCapacity cap)) throw new Phase04CheckPending("pendiente de D1: " + name + " does not implement IAirServiceCapacity");
                    m.Reset(configuration);
                    Near(1, cap.CarbonDioxideRemovalFactor, 0, "factor after Reset");
                    cap.SetCarbonDioxideRemovalFactor(0.5);
                    Near(0.5, cap.CarbonDioxideRemovalFactor, 0, "factor set");
                    bool threw = false;
                    try { cap.SetCarbonDioxideRemovalFactor(1.5); }
                    catch (ArgumentException) { threw = true; }
                    Require(threw, "factor 1.5 accepted");
                    m.Reset(configuration);
                    Near(1, cap.CarbonDioxideRemovalFactor, 0, "Reset restores factor 1");
                }),
                Run("factor 1 is phase 2 behaviour; factor 0 lets cabin CO2 rise more", k, () =>
                {
                    IOutpostSimulation plain = create(), one = create(), zero = create();
                    if (!(one is IAirServiceCapacity c1) || !(zero is IAirServiceCapacity c0))
                        throw new Phase04CheckPending("pendiente de D1: " + name + " does not implement IAirServiceCapacity");
                    foreach (IOutpostSimulation m in new[] { plain, one, zero }) m.Reset(configuration);
                    c1.SetCarbonDioxideRemovalFactor(1);
                    c0.SetCarbonDioxideRemovalFactor(0);
                    for (int i = 0; i < 60; i++)
                        foreach (IOutpostSimulation m in new[] { plain, one, zero }) m.Advance(5, demand);
                    double p = Co2(plain), o = Co2(one), z = Co2(zero);
                    Near(p, o, 0, "CO2 with factor 1 vs never set");
                    Require(z > p, $"CO2 with factor 0 ({z}) not above factor 1 ({p})");
                    return $"after 300 s with 2 aboard: {p:0.###} kPa (factor 1) vs {z:0.###} kPa (factor 0); readings are the model's, not clinical";
                })
            };
        }

        private static double Co2(IOutpostSimulation m)
        {
            GasReading g = m.Snapshot.LifeSupport.Cabin.CarbonDioxide;
            if (!g.PartialPressureKilopascals.HasValue) throw new Phase04CheckPending("cabin CO2 reading unavailable in this configuration");
            return g.PartialPressureKilopascals.Value;
        }
    }
}
