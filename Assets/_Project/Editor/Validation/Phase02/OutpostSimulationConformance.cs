using System;
using System.Collections.Generic;
using Trainer.Simulation.LifeSupport;
using static Trainer.Tooling.Validation.Phase02.Phase02Checks;

namespace Trainer.Tooling.Validation.Phase02
{
    /// <summary>
    /// Black-box conformance of an <see cref="IOutpostSimulation"/> (D1): Reset to configuration, Apply exactly
    /// once and all-or-nothing, rejection of bad elapsed times, clock, served ≤ requested, energy and stock
    /// bounds, and no stocked mass appearing during Advance. It does NOT judge D1's rates: those are D1's
    /// documented model; a mass increase is reported for review, not invented as a threshold.
    /// </summary>
    public static class OutpostSimulationConformance
    {
        private const string Kind = Phase02CheckKinds.SimulationConformance;
        private const double Mass = 1e-9;

        public static List<Phase02Check> Run(Func<IOutpostSimulation> factory, string label)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            string p = label + ": ";
            OutpostConfiguration config = Phase02Fixtures.Simulation();
            IOutpostSimulation New()
            {
                IOutpostSimulation sim = factory() ?? throw new Phase02CheckFailed("Factory returned null.");
                sim.Reset(config);
                return sim;
            }
            ResourceTransfer Fix(Guid id) => new ResourceTransfer(id, Phase02Fixtures.FixAEffect());

            return new List<Phase02Check>
            {
                Check(p + "Reset publishes the configured stocks, energy and t = 0", Kind, () =>
                {
                    OutpostSimulationSnapshot s = New().Snapshot;
                    Require(s.Power.SimulatedTimeSeconds == 0, "Time after Reset is " + s.Power.SimulatedTimeSeconds + " s.");
                    Near(config.Power.InitialEnergyJoules, s.Power.EnergyJoules, 1e-9, "Energy after Reset");
                    SameStocks(config.LifeSupport.InitialWater, config.LifeSupport.InitialFood, s, "after Reset");
                }),
                Check(p + "Apply changes stocks by the effect exactly once", Kind, () =>
                {
                    IOutpostSimulation sim = New();
                    Guid id = Guid.NewGuid();
                    WaterStock w0 = sim.Snapshot.LifeSupport.Water;
                    Require(sim.Apply(Fix(id)) == ResourceTransferStatus.Applied, "First Apply must be Applied.");
                    WaterStock w1 = sim.Snapshot.LifeSupport.Water;
                    Near(w0.UntreatedKilograms - 1.0, w1.UntreatedKilograms, Mass, "Untreated after Apply");
                    Near(w0.PotableKilograms + 0.9, w1.PotableKilograms, Mass, "Potable after Apply");
                    Require(sim.Apply(Fix(id)) == ResourceTransferStatus.AlreadyApplied, "Repeated Apply must be AlreadyApplied.");
                    WaterStock w2 = sim.Snapshot.LifeSupport.Water;
                    Require(w2.UntreatedKilograms == w1.UntreatedKilograms && w2.PotableKilograms == w1.PotableKilograms, "Repeated Apply changed stocks.");
                }),
                Check(p + "same id with another effect is IdConflict without change", Kind, () =>
                {
                    IOutpostSimulation sim = New();
                    Guid id = Guid.NewGuid();
                    sim.Apply(Fix(id));
                    WaterStock w1 = sim.Snapshot.LifeSupport.Water;
                    var other = new ResourceTransfer(id, new ResourceEffect(new[] { new ResourceChange(ResourceKind.PotableWater, -1.0) }, 1.0));
                    Require(sim.Apply(other) == ResourceTransferStatus.IdConflict, "Expected IdConflict.");
                    Require(sim.Snapshot.LifeSupport.Water.PotableKilograms == w1.PotableKilograms, "IdConflict changed stocks.");
                }),
                Check(p + "insufficient stock is all-or-nothing", Kind, () =>
                {
                    IOutpostSimulation sim = New();
                    OutpostSimulationSnapshot before = sim.Snapshot;
                    var transfer = new ResourceTransfer(Guid.NewGuid(), Phase02Fixtures.OverdrawEffect());
                    Require(sim.Apply(transfer) == ResourceTransferStatus.InsufficientStock, "Expected InsufficientStock.");
                    SameStocks(before.LifeSupport.Water, before.LifeSupport.Food, sim.Snapshot, "after a rejected Apply (no partial potable gain)");
                    Require(sim.Apply(transfer) == ResourceTransferStatus.InsufficientStock, "A rejected id must not be recorded as applied.");
                }),
                Check(p + "capacity overflow is all-or-nothing", Kind, () =>
                {
                    IOutpostSimulation sim = New();
                    OutpostSimulationSnapshot before = sim.Snapshot;
                    var overflow = new ResourceTransfer(Guid.NewGuid(), new ResourceEffect(new[]
                    {
                        new ResourceChange(ResourceKind.StoredFood, -1.0),
                        new ResourceChange(ResourceKind.PotableWater, 21.0)
                    }, -20.0));
                    Require(sim.Apply(overflow) == ResourceTransferStatus.ExceedsCapacity, "Expected ExceedsCapacity (potable 20 + 21 > 40).");
                    SameStocks(before.LifeSupport.Water, before.LifeSupport.Food, sim.Snapshot, "after a capacity rejection");
                }),
                Check(p + "Reset forgets applied ids and restores stocks", Kind, () =>
                {
                    IOutpostSimulation sim = New();
                    Guid id = Guid.NewGuid();
                    sim.Apply(Fix(id));
                    sim.Advance(10, new OutpostDemand(2, true, 50));
                    sim.Reset(config);
                    SameStocks(config.LifeSupport.InitialWater, config.LifeSupport.InitialFood, sim.Snapshot, "after second Reset");
                    Require(sim.Snapshot.Power.SimulatedTimeSeconds == 0, "Clock not reset.");
                    Require(sim.Apply(Fix(id)) == ResourceTransferStatus.Applied, "Same id must apply again after Reset.");
                }),
                Check(p + "invalid elapsed time rejected without change", Kind, () =>
                {
                    IOutpostSimulation sim = New();
                    OutpostSimulationSnapshot before = sim.Snapshot;
                    foreach (double bad in new[] { double.NaN, double.PositiveInfinity, -1.0 })
                    {
                        bool threw = false;
                        try { sim.Advance(bad, new OutpostDemand(1, false, 0)); } catch (ArgumentException) { threw = true; }
                        Require(threw, "Advance(" + bad + ") must throw ArgumentException.");
                    }
                    Require(ReferenceEquals(before, sim.Snapshot) || before.Power.SimulatedTimeSeconds == sim.Snapshot.Power.SimulatedTimeSeconds, "Rejected Advance changed the clock.");
                    SameStocks(before.LifeSupport.Water, before.LifeSupport.Food, sim.Snapshot, "after rejected Advance");
                }),
                Check(p + "clock, served ≤ requested, bounds over a varied run", Kind, () =>
                {
                    IOutpostSimulation sim = New();
                    var demands = new[]
                    {
                        new OutpostDemand(0, false, 0), new OutpostDemand(2, false, 100), new OutpostDemand(2, true, 0),
                        new OutpostDemand(1, true, 300), new OutpostDemand(2, false, 5000)
                    };
                    double t = 0;
                    for (int i = 0; i < 50; i++)
                    {
                        OutpostDemand d = demands[i % demands.Length];
                        double dt = 0.5 + (i % 3);
                        OutpostSimulationSnapshot before = sim.Snapshot;
                        sim.Advance(dt, d);
                        t += dt;
                        OutpostSimulationSnapshot s = sim.Snapshot;
                        Near(t, s.Power.SimulatedTimeSeconds, 1e-9, "Simulated time after step " + i);
                        Near(d.ServiceLoadWatts, s.ServiceLoadRequestedWatts, 1e-9, "Requested service load echoes the demand");
                        Require(sim.LastFullyServedServiceSeconds >= 0 && sim.LastFullyServedServiceSeconds <= dt + 1e-9, "LastFullyServedServiceSeconds outside [0, dt].");
                        Require(sim.LastFullyServedScienceSeconds >= 0 && sim.LastFullyServedScienceSeconds <= dt + 1e-9, "LastFullyServedScienceSeconds outside [0, dt].");
                        if (!d.ScienceRequested) Require(sim.LastFullyServedScienceSeconds == 0, "Science served without being requested.");
                        double change = sim.SecondsUntilServiceChange(d);
                        Require(!double.IsNaN(change) && change >= 0, "SecondsUntilServiceChange = " + change + ".");
                        double maxGain = s.Power.GenerationWatts * dt + 1e-6;
                        Require(s.Power.EnergyJoules - before.Power.EnergyJoules <= maxGain, "Energy rose more than generation × dt.");
                        double storedBefore = Stored(before), storedAfter = Stored(s);
                        if (storedAfter > storedBefore + Mass)
                            throw new Phase02CheckFailed("Stocked mass rose by " + (storedAfter - storedBefore) + " kg during Advance without Apply (step " + i
                                + "); if intended, D1 must document the source and boundary.");
                    }
                }),
                Check(p + "earlier snapshots are not mutated", Kind, () =>
                {
                    IOutpostSimulation sim = New();
                    OutpostSimulationSnapshot held = sim.Snapshot;
                    double potable = held.LifeSupport.Water.PotableKilograms, time = held.Power.SimulatedTimeSeconds;
                    sim.Apply(Fix(Guid.NewGuid()));
                    sim.Advance(5, new OutpostDemand(2, true, 100));
                    Require(held.LifeSupport.Water.PotableKilograms == potable && held.Power.SimulatedTimeSeconds == time, "A published snapshot changed.");
                }),
                Check(p + "cabin readings: absent is never 0, observed time not in the future", Kind, () =>
                {
                    IOutpostSimulation sim = New();
                    sim.Advance(30, new OutpostDemand(2, false, 0));
                    var cabin = sim.Snapshot.LifeSupport.Cabin;
                    foreach (GasReading g in new[] { cabin.Oxygen, cabin.CarbonDioxide })
                    {
                        if (g.Status == ReadingStatus.Unavailable)
                            Require(g.PartialPressureKilopascals == null, "Unavailable reading carries a value.");
                        else
                        {
                            Require(g.PartialPressureKilopascals >= 0, "Negative partial pressure.");
                            Require(g.ObservedAtSeconds <= sim.Snapshot.Power.SimulatedTimeSeconds + 1e-9, "Reading from the future.");
                        }
                    }
                    return "O2 " + cabin.Oxygen.Status + ", CO2 " + cabin.CarbonDioxide.Status;
                })
            };
        }

        private static double Stored(OutpostSimulationSnapshot s) =>
            s.LifeSupport.Water.PotableKilograms + s.LifeSupport.Water.UntreatedKilograms + s.LifeSupport.Food.StoredKilograms;

        private static void SameStocks(WaterStock water, FoodStock food, OutpostSimulationSnapshot s, string when)
        {
            WaterStock w = s.LifeSupport.Water;
            Require(Math.Abs(w.PotableKilograms - water.PotableKilograms) <= Mass
                && Math.Abs(w.UntreatedKilograms - water.UntreatedKilograms) <= Mass
                && Math.Abs(s.LifeSupport.Food.StoredKilograms - food.StoredKilograms) <= Mass,
                "Stocks " + when + ": potable " + w.PotableKilograms + ", untreated " + w.UntreatedKilograms + ", food "
                + s.LifeSupport.Food.StoredKilograms + "; expected " + water.PotableKilograms + ", " + water.UntreatedKilograms + ", " + food.StoredKilograms + ".");
        }
    }
}
