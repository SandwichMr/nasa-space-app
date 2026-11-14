using System;
using NUnit.Framework;
using Trainer.Simulation;

public sealed class SimulationClockTests
{
    [Test] public void StartsAtZeroAndAccumulatesExplicitAdvances()
    {
        var clock = new SimulationClock();
        Assert.That(clock.ElapsedSeconds, Is.EqualTo(0d));
        clock.Advance(1.5);
        clock.Advance(2.5);
        Assert.That(clock.ElapsedSeconds, Is.EqualTo(4d));
    }

    [Test] public void ZeroAdvanceChangesNothing()
    {
        var clock = new SimulationClock();
        clock.Advance(3d);
        clock.Advance(0d);
        Assert.That(clock.ElapsedSeconds, Is.EqualTo(3d));
    }

    [Test] public void InvalidAdvanceThrowsAndLeavesTimeUnchanged()
    {
        var clock = new SimulationClock();
        clock.Advance(5d);
        foreach (double bad in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(bad));
            Assert.That(clock.ElapsedSeconds, Is.EqualTo(5d));
        }
    }

    [Test] public void OverflowingAdvanceIsRejected()
    {
        var clock = new SimulationClock();
        clock.Advance(double.MaxValue);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(double.MaxValue));
        Assert.That(clock.ElapsedSeconds, Is.EqualTo(double.MaxValue));
    }

    [Test] public void ResetReturnsToZero()
    {
        var clock = new SimulationClock();
        clock.Advance(42d);
        clock.Reset();
        Assert.That(clock.ElapsedSeconds, Is.EqualTo(0d));
    }
}
