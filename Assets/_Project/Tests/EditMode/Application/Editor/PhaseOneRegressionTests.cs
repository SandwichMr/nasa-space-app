using System;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Simulation;
using Trainer.Unity.Bootstrap;
using Trainer.Tooling.Validation;
using UnityEngine;

public sealed class PhaseOneRegressionTests
{
    private static MissionSession Start(double energy, double step, double target = 10, double essential = 0)
    {
        var session = new MissionSession(new PowerSystem(),
            new PowerConfiguration(energy, 1, 0, essential, essential == 0 ? 1 : 0, 1, 1),
            new MissionRules(target, 10), step);
        if (essential == 0) session.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.SetScienceEnabled, true));
        session.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.Resume));
        return session;
    }

    [TestCase(0.5, 1)] [TestCase(0.5, 0.3)] [TestCase(0.5, 0.1)] [TestCase(0.5, 0.01)]
    [TestCase(0.53, 1)] [TestCase(0.53, 0.3)] [TestCase(0.53, 0.1)] [TestCase(0.53, 0.01)]
    public void ScienceWorkIsIndependentOfStepAtDepletion(double energy, double step)
    {
        var session = Start(energy, step);
        session.Tick(1);
        session.TryGetSnapshot(out MissionSnapshot snapshot);
        Assert.That(snapshot.ScienceWorkSeconds, Is.EqualTo(energy).Within(1e-12));
        Assert.That(snapshot.Power.EnergyJoules, Is.EqualTo(0));
    }

    [TestCase(0.5)] [TestCase(0.0001)]
    public void ObjectiveReachedExactlyAtEmptyBatteryIsCompleted(double target)
    {
        var session = Start(target, 1, target);
        session.Tick(1);
        session.TryGetSnapshot(out MissionSnapshot snapshot);
        Assert.That(session.Outcome, Is.EqualTo(MissionOutcome.ObjectiveAchieved));
        Assert.That(snapshot.Power.SimulatedTimeSeconds, Is.EqualTo(target).Within(1e-12));
        Assert.That(snapshot.ScienceWorkSeconds, Is.EqualTo(target).Within(1e-12));
    }

    [TestCase(0.5, 1)] [TestCase(0.53, 0.3)]
    public void EssentialLossStopsAtDepletionEvent(double energy, double step)
    {
        var session = Start(energy, step, 10, 1);
        session.Tick(1);
        session.TryGetSnapshot(out MissionSnapshot snapshot);
        Assert.That(session.Outcome, Is.EqualTo(MissionOutcome.EssentialServiceLost));
        Assert.That(snapshot.Power.SimulatedTimeSeconds, Is.EqualTo(energy).Within(1e-12));
    }

    [Test]
    public void AlreadyUnservedEssentialDoesNotAdvanceClock()
    {
        var session = Start(0, 1, 10, 1);
        session.Tick(1);
        session.TryGetSnapshot(out MissionSnapshot snapshot);
        Assert.That(snapshot.State, Is.EqualTo(Trainer.Application.SessionState.Interrupted));
        Assert.That(snapshot.Power.SimulatedTimeSeconds, Is.EqualTo(0));
    }

    [Test]
    public void IntegratedServiceSurvivesBoundaryAndRejectsInvalidAdvanceWithoutMutation()
    {
        var power = new PowerSystem();
        var config = new PowerConfiguration(.5, 1, 0, 0, 1, 1, 1);
        power.Reset(config);
        power.Advance(1, true);
        Assert.That(power.LastFullyServedScienceSeconds, Is.EqualTo(.5));
        PowerSnapshot previous = power.Snapshot;
        Assert.Throws<ArgumentOutOfRangeException>(() => power.Advance(double.NaN, false));
        Assert.That(power.Snapshot, Is.SameAs(previous));
        Assert.That(power.LastFullyServedScienceSeconds, Is.EqualTo(.5));
        power.Reset(config);
        Assert.That(power.LastFullyServedScienceSeconds, Is.EqualTo(0));
    }

    [Test]
    public void PartialSciencePowerDoesNotEarnFullServiceTime()
    {
        var power = new PowerSystem();
        power.Reset(new PowerConfiguration(0, 0, .5, 0, 1, 0, 0));
        power.Advance(10, true);
        Assert.That(power.LastFullyServedScienceSeconds, Is.EqualTo(0));
    }

    [Test]
    public void RunnerOwnershipIsReleasedByUnbindAndDestroy()
    {
        var a = new GameObject("runner a");
        var b = new GameObject("runner b");
        try
        {
            var session = Start(1, 1);
            var first = a.AddComponent<SimulationRunner>();
            var second = b.AddComponent<SimulationRunner>();
            first.Bind(session, 1);
            Assert.Throws<InvalidOperationException>(() => second.Bind(session, 1));
            first.Unbind();
            second.Bind(session, 1);
            UnityEngine.Object.DestroyImmediate(b);
            first.Bind(session, 1);
            Assert.That(first.IsBound, Is.True);
        }
        finally { UnityEngine.Object.DestroyImmediate(a); if (b != null) UnityEngine.Object.DestroyImmediate(b); }
    }

    [Test]
    public void FiniteLargeConfigurationOnlyWarnsAboutPrecision()
    {
        var report = PhaseOneScenarioValidator.ValidateConfiguration(new PowerConfiguration(1e16, 1e16, 0, 0, 0, 0, 0));
        Assert.That(report.HasErrors, Is.False);
        Assert.That(report.Contains("ExceedsExactRange"), Is.True);
    }
}
