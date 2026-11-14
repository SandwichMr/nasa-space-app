using System;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Simulation;

public sealed class MissionOutcomeEvaluatorTests
{
    private static readonly MissionRules Rules = new MissionRules(10, 100);

    private static PowerSnapshot Power(double time, double essentialRequested, double essentialServed,
        double scienceRequested, double scienceServed) =>
        new PowerSnapshot(time, 1000, 2000, 50, essentialRequested, essentialServed, scienceRequested, scienceServed);

    [Test] public void RulesRequirePositiveFiniteValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MissionRules(0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MissionRules(10, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MissionRules(double.NaN, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MissionRules(10, double.PositiveInfinity));
    }

    [Test] public void FullServiceNeedsPositiveDemandFullyServed()
    {
        Assert.That(MissionOutcomeEvaluator.IsFullyServed(60, 60), Is.True);
        Assert.That(MissionOutcomeEvaluator.IsFullyServed(60, 59.9), Is.False);
        Assert.That(MissionOutcomeEvaluator.IsFullyServed(0, 0), Is.False);
        Assert.That(MissionOutcomeEvaluator.IsFullyServed(double.NaN, 1), Is.False);
    }

    [Test] public void ServedScienceUsesSimulatedElapsedTimeOnlyWhenFullyServed()
    {
        PowerSnapshot start = Power(10, 60, 60, 0, 0);
        Assert.That(MissionOutcomeEvaluator.ServedScienceSeconds(start, Power(11.5, 60, 60, 20, 20), true), Is.EqualTo(1.5));
        Assert.That(MissionOutcomeEvaluator.ServedScienceSeconds(start, Power(11.5, 60, 60, 20, 10), true), Is.EqualTo(0.0));
        Assert.That(MissionOutcomeEvaluator.ServedScienceSeconds(start, Power(11.5, 60, 60, 20, 20), false), Is.EqualTo(0.0));
        Assert.That(MissionOutcomeEvaluator.ServedScienceSeconds(start, Power(10, 60, 60, 20, 20), true), Is.EqualTo(0.0));
        Assert.That(MissionOutcomeEvaluator.ServedScienceSeconds(start, Power(9, 60, 60, 20, 20), true), Is.EqualTo(0.0));
        Assert.Throws<ArgumentNullException>(() => MissionOutcomeEvaluator.ServedScienceSeconds(null, start, true));
    }

    [Test] public void EssentialLossHasPriorityOverObjectiveAndTime()
    {
        Assert.That(MissionOutcomeEvaluator.Evaluate(Rules, Power(200, 60, 30, 0, 0), 50),
            Is.EqualTo(MissionOutcome.EssentialServiceLost));
        Assert.That(MissionOutcomeEvaluator.Evaluate(Rules, Power(200, 60, 60, 0, 0), 10),
            Is.EqualTo(MissionOutcome.ObjectiveAchieved));
        Assert.That(MissionOutcomeEvaluator.Evaluate(Rules, Power(100, 60, 60, 0, 0), 9.9),
            Is.EqualTo(MissionOutcome.TimeExpired));
        Assert.That(MissionOutcomeEvaluator.Evaluate(Rules, Power(99, 60, 60, 0, 0), 9.9),
            Is.EqualTo(MissionOutcome.InProgress));
        Assert.That(MissionOutcomeEvaluator.Evaluate(Rules, Power(5, 0, 0, 0, 0), 0),
            Is.EqualTo(MissionOutcome.InProgress));
    }

    [Test] public void OutcomesMapToProtectedSessionStates()
    {
        Assert.That(MissionOutcomeEvaluator.ToSessionState(MissionOutcome.InProgress), Is.EqualTo(SessionState.Running));
        Assert.That(MissionOutcomeEvaluator.ToSessionState(MissionOutcome.ObjectiveAchieved), Is.EqualTo(SessionState.Completed));
        Assert.That(MissionOutcomeEvaluator.ToSessionState(MissionOutcome.TimeExpired), Is.EqualTo(SessionState.Completed));
        Assert.That(MissionOutcomeEvaluator.ToSessionState(MissionOutcome.EssentialServiceLost), Is.EqualTo(SessionState.Interrupted));
        Assert.That(MissionOutcomeEvaluator.ToSessionState(MissionOutcome.SimulationFault), Is.EqualTo(SessionState.Interrupted));
        Assert.Throws<ArgumentOutOfRangeException>(() => MissionOutcomeEvaluator.ToSessionState((MissionOutcome)99));
    }

    private static MissionSnapshot Published(SessionState state, double work, PowerSnapshot power) =>
        new MissionSnapshot(0, 1, state, power, work, false);

    [Test] public void ClassifyRecoversOutcomeFromPublishedSnapshot()
    {
        PowerSnapshot served = Power(100, 60, 60, 0, 0);
        Assert.That(MissionOutcomeEvaluator.Classify(Rules, Published(SessionState.Completed, 10, served)),
            Is.EqualTo(MissionOutcome.ObjectiveAchieved));
        Assert.That(MissionOutcomeEvaluator.Classify(Rules, Published(SessionState.Completed, 10 - 1e-9, served)),
            Is.EqualTo(MissionOutcome.ObjectiveAchieved));
        Assert.That(MissionOutcomeEvaluator.Classify(Rules, Published(SessionState.Completed, 9.9, served)),
            Is.EqualTo(MissionOutcome.TimeExpired));
        Assert.That(MissionOutcomeEvaluator.Classify(Rules, Published(SessionState.Interrupted, 3, Power(40, 60, 30, 0, 0))),
            Is.EqualTo(MissionOutcome.EssentialServiceLost));
        Assert.That(MissionOutcomeEvaluator.Classify(Rules, Published(SessionState.Interrupted, 3, served)),
            Is.EqualTo(MissionOutcome.SimulationFault));
        foreach (SessionState state in new[] { SessionState.Ready, SessionState.Running, SessionState.Paused })
            Assert.That(MissionOutcomeEvaluator.Classify(Rules, Published(state, 50, served)),
                Is.EqualTo(MissionOutcome.InProgress));
        Assert.Throws<ArgumentNullException>(() => MissionOutcomeEvaluator.Classify(null, Published(SessionState.Ready, 0, served)));
        Assert.Throws<ArgumentNullException>(() => MissionOutcomeEvaluator.Classify(Rules, null));
    }

    [Test] public void LimitsAreReachedWithinRoundingTolerance()
    {
        Assert.That(MissionOutcomeEvaluator.Evaluate(Rules, Power(100 - 1e-9, 60, 60, 0, 0), 0),
            Is.EqualTo(MissionOutcome.TimeExpired));
        Assert.That(MissionOutcomeEvaluator.Evaluate(Rules, Power(50, 60, 60, 0, 0), 10 - 1e-9),
            Is.EqualTo(MissionOutcome.ObjectiveAchieved));
        Assert.That(MissionOutcomeEvaluator.Evaluate(Rules, Power(100 - 1e-3, 60, 60, 0, 0), 10 - 1e-3),
            Is.EqualTo(MissionOutcome.InProgress));
    }
}
