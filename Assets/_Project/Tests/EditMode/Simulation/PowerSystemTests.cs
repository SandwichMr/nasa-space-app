// Disabled guard: this path compiles into Assembly-CSharp, which has no NUnit reference.
// Remove the guard only after the team authorizes an Editor folder or a test .asmdef here
// (see docs/phase-01/handoffs/01_SIMULATION_HANDOFF.md). Do not define the symbol globally.
#if TRAINER_SIMULATION_TESTS
using System;
using NUnit.Framework;
using Trainer.Simulation;

// Synthetic, illustrative values only (not lunar hardware data). Units: s, J, W.
// Expected values are closed-form results of the piecewise-constant model described in PowerSystem.
public sealed class PowerSystemTests
{
    private const double Exact = 1e-9;

    // Argument order: initial J, capacity J, generation W, essential W, science W, max charge W, max discharge W.
    private static PowerConfiguration Config(double initial, double capacity, double generation,
        double essential, double science, double maxCharge = 1000d, double maxDischarge = 1000d)
        => new PowerConfiguration(initial, capacity, generation, essential, science, maxCharge, maxDischarge);

    private static PowerSystem Started(PowerConfiguration config)
    {
        var system = new PowerSystem();
        system.Reset(config);
        return system;
    }

    [Test] public void UseBeforeResetIsRejected()
    {
        var system = new PowerSystem();
        Assert.Throws<InvalidOperationException>(() => { var _ = system.Snapshot; });
        Assert.Throws<InvalidOperationException>(() => system.Advance(1d, false));
        Assert.Throws<ArgumentNullException>(() => system.Reset(null));
    }

    [Test] public void ResetPublishesInitialObservation()
    {
        var s = Started(Config(1000, 10000, 100, 30, 20)).Snapshot;
        Assert.That(s.SimulatedTimeSeconds, Is.EqualTo(0d));
        Assert.That(s.EnergyJoules, Is.EqualTo(1000d));
        Assert.That(s.CapacityJoules, Is.EqualTo(10000d));
        Assert.That(s.GenerationWatts, Is.EqualTo(100d));
        Assert.That(s.EssentialRequestedWatts, Is.EqualTo(30d));
        Assert.That(s.EssentialServedWatts, Is.EqualTo(30d));
        Assert.That(s.ScienceRequestedWatts, Is.EqualTo(0d));
        Assert.That(s.ScienceServedWatts, Is.EqualTo(0d));
    }

    [Test] public void SurplusChargesBatteryAnalytically()
    {
        // net = 100 - 30 - 20 = +50 W for 10 s -> +500 J.
        var system = Started(Config(1000, 10000, 100, 30, 20));
        system.Advance(10d, true);
        var s = system.Snapshot;
        Assert.That(s.SimulatedTimeSeconds, Is.EqualTo(10d));
        Assert.That(s.EnergyJoules, Is.EqualTo(1500d).Within(Exact));
        Assert.That(s.ScienceRequestedWatts, Is.EqualTo(20d));
        Assert.That(s.ScienceServedWatts, Is.EqualTo(20d));
    }

    [Test] public void ChargePowerLimitCurtailsExtraGeneration()
    {
        // Surplus 90 W, charge limit 40 W -> +400 J in 10 s; 50 W curtailed, not stored.
        var system = Started(Config(0, 10000, 100, 10, 0, maxCharge: 40));
        system.Advance(10d, false);
        Assert.That(system.Snapshot.EnergyJoules, Is.EqualTo(400d).Within(Exact));
    }

    [Test] public void CapacityIsReachedInsideIntervalWithoutOverflow()
    {
        // +50 W from 9900 J reaches 10000 J after 2 s; remaining 8 s cannot add energy.
        var system = Started(Config(9900, 10000, 100, 50, 0));
        system.Advance(10d, false);
        var s = system.Snapshot;
        Assert.That(s.EnergyJoules, Is.EqualTo(10000d));
        Assert.That(s.EssentialServedWatts, Is.EqualTo(50d));
    }

    [Test] public void DeficitDischargesBatteryAndServesEssential()
    {
        // net = 10 - 50 = -40 W for 10 s -> -400 J.
        var system = Started(Config(1000, 10000, 10, 50, 0));
        system.Advance(10d, false);
        var s = system.Snapshot;
        Assert.That(s.EnergyJoules, Is.EqualTo(600d).Within(Exact));
        Assert.That(s.EssentialServedWatts, Is.EqualTo(50d));
    }

    [Test] public void DepletionInsideIntervalStopsAtZeroAndLimitsService()
    {
        // -40 W from 100 J empties after 2.5 s; afterwards only generation (10 W) is served.
        var system = Started(Config(100, 10000, 10, 50, 30));
        system.Advance(10d, true);
        var s = system.Snapshot;
        Assert.That(s.EnergyJoules, Is.EqualTo(0d));
        Assert.That(s.EssentialRequestedWatts, Is.EqualTo(50d));
        Assert.That(s.EssentialServedWatts, Is.EqualTo(10d));
        Assert.That(s.ScienceRequestedWatts, Is.EqualTo(30d));
        Assert.That(s.ScienceServedWatts, Is.EqualTo(0d));
    }

    [Test] public void DischargePowerLimitServesEssentialBeforeScience()
    {
        // Supply = 0 W generation + 60 W discharge limit: essential 50 W, science gets 10 W of 30 W.
        var system = Started(Config(1000, 10000, 0, 50, 30, maxDischarge: 60));
        system.Advance(5d, true);
        var s = system.Snapshot;
        Assert.That(s.EssentialServedWatts, Is.EqualTo(50d));
        Assert.That(s.ScienceServedWatts, Is.EqualTo(10d));
        Assert.That(s.EnergyJoules, Is.EqualTo(700d).Within(Exact)); // 1000 - 60 * 5
    }

    [Test] public void EssentialHasPriorityWhenBatteryCannotHelp()
    {
        var system = Started(Config(1000, 10000, 20, 50, 30, maxDischarge: 0));
        system.Advance(5d, true);
        var s = system.Snapshot;
        Assert.That(s.EssentialServedWatts, Is.EqualTo(20d));
        Assert.That(s.ScienceServedWatts, Is.EqualTo(0d));
        Assert.That(s.EnergyJoules, Is.EqualTo(1000d));
    }

    [Test] public void ScienceNotRequestedConsumesNothing()
    {
        var withScience = Started(Config(1000, 10000, 0, 10, 30));
        var withoutScience = Started(Config(1000, 10000, 0, 10, 30));
        withScience.Advance(10d, true);
        withoutScience.Advance(10d, false);
        Assert.That(withoutScience.Snapshot.ScienceRequestedWatts, Is.EqualTo(0d));
        Assert.That(withoutScience.Snapshot.ScienceServedWatts, Is.EqualTo(0d));
        Assert.That(withoutScience.Snapshot.EnergyJoules, Is.EqualTo(900d).Within(Exact)); // 1000 - 10*10
        Assert.That(withScience.Snapshot.EnergyJoules, Is.EqualTo(600d).Within(Exact));    // 1000 - 40*10
    }

    [Test] public void ZeroAdvanceIsANoOpIncludingScienceRequest()
    {
        var system = Started(Config(1000, 10000, 0, 10, 30));
        var before = system.Snapshot;
        system.Advance(0d, true);
        Assert.That(system.Snapshot, Is.SameAs(before));
        Assert.That(system.Snapshot.ScienceRequestedWatts, Is.EqualTo(0d));
    }

    [Test] public void InvalidAdvanceIsRejectedWithoutConsumingResources()
    {
        var system = Started(Config(1000, 10000, 0, 10, 30));
        var before = system.Snapshot;
        foreach (double bad in new[] { -1d, double.NaN, double.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => system.Advance(bad, true));
            Assert.That(system.Snapshot, Is.SameAs(before));
        }
    }

    [Test] public void PreviousSnapshotsStayImmutable()
    {
        var system = Started(Config(1000, 10000, 0, 10, 0));
        var before = system.Snapshot;
        system.Advance(10d, false);
        Assert.That(system.Snapshot, Is.Not.SameAs(before));
        Assert.That(before.EnergyJoules, Is.EqualTo(1000d));
        Assert.That(before.SimulatedTimeSeconds, Is.EqualTo(0d));
    }

    [Test] public void ResetRestoresInitialConditionsWithoutMutatingConfiguration()
    {
        var config = Config(500, 2000, 15, 40, 25, 30, 35);
        var system = Started(config);
        var initial = system.Snapshot;
        system.Advance(100d, true);
        system.Reset(config);
        var again = system.Snapshot;
        Assert.That(again.SimulatedTimeSeconds, Is.EqualTo(initial.SimulatedTimeSeconds));
        Assert.That(again.EnergyJoules, Is.EqualTo(initial.EnergyJoules));
        Assert.That(again.EssentialServedWatts, Is.EqualTo(initial.EssentialServedWatts));
        Assert.That(again.ScienceRequestedWatts, Is.EqualTo(initial.ScienceRequestedWatts));
        Assert.That(config.InitialEnergyJoules, Is.EqualTo(500d));
        Assert.That(config.CapacityJoules, Is.EqualTo(2000d));
        Assert.That(config.MaxDischargeWatts, Is.EqualTo(35d));
    }

    [Test] public void SameInputsAreRepeatable()
    {
        var config = Config(500, 2000, 15, 40, 25, 30, 35);
        var a = Started(config);
        var b = Started(config);
        double[] steps = { 1.25, 7d, 0.5, 30d, 3d };
        bool[] science = { true, false, true, true, false };
        for (int i = 0; i < steps.Length; i++)
        {
            a.Advance(steps[i], science[i]);
            b.Advance(steps[i], science[i]);
        }
        Assert.That(a.Snapshot.EnergyJoules, Is.EqualTo(b.Snapshot.EnergyJoules));
        Assert.That(a.Snapshot.SimulatedTimeSeconds, Is.EqualTo(b.Snapshot.SimulatedTimeSeconds));
        Assert.That(a.Snapshot.ScienceServedWatts, Is.EqualTo(b.Snapshot.ScienceServedWatts));
    }

    [Test] public void StepRefinementMatchesSingleStepAcrossDepletion()
    {
        // -45 W from 1000 J empties at 22.2... s. Model is piecewise exact, so differences are only
        // floating-point rounding: 1e-9 J tolerance on kJ magnitudes (~1e-12 relative) is ample.
        var config = Config(1000, 5000, 5, 40, 10);
        var coarse = Started(config);
        var fine = Started(config);
        var irregular = Started(config);
        coarse.Advance(30d, true);
        for (int i = 0; i < 3000; i++) fine.Advance(0.01, true);
        irregular.Advance(7.3, true);
        irregular.Advance(14.9, true);
        irregular.Advance(7.8, true);
        foreach (var other in new[] { fine, irregular })
        {
            Assert.That(other.Snapshot.EnergyJoules, Is.EqualTo(coarse.Snapshot.EnergyJoules).Within(Exact));
            Assert.That(other.Snapshot.SimulatedTimeSeconds, Is.EqualTo(30d).Within(Exact));
            Assert.That(other.Snapshot.EssentialServedWatts, Is.EqualTo(coarse.Snapshot.EssentialServedWatts));
        }
        // Mid-way check before depletion: 1000 - 45 * 20 = 100 J.
        var half = Started(config);
        for (int i = 0; i < 2000; i++) half.Advance(0.01, true);
        Assert.That(half.Snapshot.EnergyJoules, Is.EqualTo(100d).Within(1e-6));
    }

    [Test] public void BoundsHoldOverDeterministicSequence()
    {
        var system = Started(Config(300, 1000, 25, 20, 30, 15, 40));
        uint seed = 12345;
        for (int i = 0; i < 2000; i++)
        {
            seed = seed * 1664525u + 1013904223u; // fixed LCG, reproducible
            double dt = (seed % 1000) / 100d;
            bool science = (seed >> 16) % 2 == 0;
            double before = system.Snapshot.EnergyJoules;
            system.Advance(dt, science);
            var s = system.Snapshot;
            Assert.That(s.EnergyJoules, Is.GreaterThanOrEqualTo(0d));
            Assert.That(s.EnergyJoules, Is.LessThanOrEqualTo(s.CapacityJoules));
            Assert.That(s.EssentialServedWatts, Is.LessThanOrEqualTo(s.EssentialRequestedWatts));
            Assert.That(s.ScienceServedWatts, Is.LessThanOrEqualTo(s.ScienceRequestedWatts));
            Assert.That(s.ScienceServedWatts, Is.GreaterThanOrEqualTo(0d));
            // No free energy: battery gain bounded by charge limit, loss by discharge limit.
            Assert.That(s.EnergyJoules - before, Is.LessThanOrEqualTo(15d * dt + Exact));
            Assert.That(before - s.EnergyJoules, Is.LessThanOrEqualTo(40d * dt + Exact));
        }
    }

    [Test] public void ZeroCapacityServesOnlyGeneration()
    {
        var system = Started(Config(0, 0, 10, 50, 30));
        system.Advance(10d, true);
        var s = system.Snapshot;
        Assert.That(s.EnergyJoules, Is.EqualTo(0d));
        Assert.That(s.EssentialServedWatts, Is.EqualTo(10d));
        Assert.That(s.ScienceServedWatts, Is.EqualTo(0d));
    }
}
#endif
