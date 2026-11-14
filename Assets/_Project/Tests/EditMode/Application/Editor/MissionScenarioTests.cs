using System;
using System.Collections.Generic;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Simulation;

/// <summary>
/// The phase 1 synthetic scenario (Data/Scenarios/Phase01_PowerCoop_Synthetic.asset) played end to end over
/// the real D1 PowerSystem. Values are illustrative, not NASA hardware. Expected numbers by hand:
/// science on: net = 100 - 60 - 80 = -40 W, so 2400 J last 60 s; science off: +40 W of charge.
/// Always on: 60 s of work, then partial service earns nothing. Managed (on 60 s, off 60 s, on 60 s): 120 s
/// of work at t = 180 s. Essential demand (60 W) never exceeds generation (100 W), so it is always served.
/// MissionSceneCompositionTests checks that the asset holds these same values.
/// </summary>
public sealed class MissionScenarioTests
{
    public const double InitialEnergyJoules = 2400, CapacityJoules = 6000, GenerationWatts = 100;
    public const double EssentialWatts = 60, ScienceWatts = 80, MaxChargeWatts = 100, MaxDischargeWatts = 200;
    public const double TargetSeconds = 120, DurationSeconds = 240;

    private MissionSession session;
    private readonly List<MissionSnapshot> published = new List<MissionSnapshot>();
    private readonly List<CommandResult> results = new List<CommandResult>();

    public static PowerConfiguration Configuration() => new PowerConfiguration(InitialEnergyJoules, CapacityJoules,
        GenerationWatts, EssentialWatts, ScienceWatts, MaxChargeWatts, MaxDischargeWatts);

    [SetUp] public void SetUp()
    {
        published.Clear();
        results.Clear();
        session = new MissionSession(new PowerSystem(), Configuration(), new MissionRules(TargetSeconds, DurationSeconds));
        session.SnapshotChanged += published.Add;
        session.CommandCompleted += results.Add;
    }

    private MissionSnapshot Current()
    {
        Assert.That(session.TryGetSnapshot(out MissionSnapshot snapshot), Is.True);
        return snapshot;
    }

    private CommandResult Send(SessionAction action, bool? science = null, Guid? id = null)
    {
        var command = new SessionCommand(id ?? Guid.NewGuid(), Current().Epoch, action, science);
        session.Submit(command);
        CommandResult result = results[results.Count - 1];
        Assert.That(result.CommandId, Is.EqualTo(command.Id));
        return result;
    }

    private void Run(double seconds, double frame)
    {
        for (double done = 0; done < seconds - 1e-12; done += frame) session.Tick(Math.Min(frame, seconds - done));
    }

    private void AssertEssentialAlwaysServed()
    {
        foreach (MissionSnapshot snapshot in published)
            Assert.That(snapshot.Power.EssentialServedWatts, Is.EqualTo(EssentialWatts));
    }

    [Test] public void ScenarioStartsReadyWithLabelledInitialCharge()
    {
        MissionSnapshot start = Current();
        Assert.That(start.State, Is.EqualTo(SessionState.Ready));
        Assert.That(start.Epoch, Is.EqualTo(0));
        Assert.That(start.Power.EnergyJoules, Is.EqualTo(InitialEnergyJoules));
        Assert.That(start.Power.SimulatedTimeSeconds, Is.EqualTo(0));
        Assert.That(start.ScienceWorkSeconds, Is.EqualTo(0));
        session.Tick(10);
        Assert.That(published.Count, Is.EqualTo(0), "Ready does not advance the clock.");
    }

    [Test] public void LeavingScienceOnStallsAtEmptyBatteryAndTimeExpires()
    {
        Send(SessionAction.SetScienceEnabled, true);
        Send(SessionAction.Resume);
        Run(DurationSeconds + 10, 0.5);
        MissionSnapshot end = Current();
        Assert.That(end.State, Is.EqualTo(SessionState.Completed));
        Assert.That(session.Outcome, Is.EqualTo(MissionOutcome.TimeExpired));
        Assert.That(MissionOutcomeEvaluator.Classify(session.Rules, end), Is.EqualTo(MissionOutcome.TimeExpired));
        Assert.That(end.ScienceWorkSeconds, Is.EqualTo(60).Within(1e-9), "Only fully served science counts.");
        Assert.That(end.Power.SimulatedTimeSeconds, Is.EqualTo(DurationSeconds).Within(1e-9));
        Assert.That(end.Power.EnergyJoules, Is.EqualTo(0));
        Assert.That(end.Power.ScienceServedWatts, Is.LessThanOrEqualTo(ScienceWatts - 1));
        AssertEssentialAlwaysServed();
    }

    [Test] public void StoppingScienceToRechargeReachesTheObjective()
    {
        Send(SessionAction.SetScienceEnabled, true);
        Send(SessionAction.Resume);
        Run(60, 1.0 / 60);
        Assert.That(Current().ScienceWorkSeconds, Is.EqualTo(60).Within(1e-6));
        Assert.That(Current().Power.EnergyJoules, Is.EqualTo(0).Within(1e-6));
        Send(SessionAction.SetScienceEnabled, false);
        Run(60, 1.0 / 60);
        Assert.That(Current().Power.EnergyJoules, Is.EqualTo(InitialEnergyJoules).Within(1e-6));
        Assert.That(Current().ScienceWorkSeconds, Is.EqualTo(60).Within(1e-6), "No work while science is off.");
        Send(SessionAction.SetScienceEnabled, true);
        Run(70, 1.0 / 60);
        MissionSnapshot end = Current();
        Assert.That(end.State, Is.EqualTo(SessionState.Completed));
        Assert.That(session.Outcome, Is.EqualTo(MissionOutcome.ObjectiveAchieved));
        Assert.That(MissionOutcomeEvaluator.Classify(session.Rules, end), Is.EqualTo(MissionOutcome.ObjectiveAchieved));
        Assert.That(end.ScienceWorkSeconds, Is.EqualTo(TargetSeconds).Within(1e-6));
        Assert.That(end.Power.SimulatedTimeSeconds, Is.EqualTo(180).Within(1e-6));
        AssertEssentialAlwaysServed();
    }

    [Test] public void ScienceOffChargesToCapacityAndEarnsNoWork()
    {
        Send(SessionAction.Resume);
        Run(90, 0.25);
        Assert.That(Current().Power.EnergyJoules, Is.EqualTo(CapacityJoules).Within(1e-9));
        Run(DurationSeconds, 0.25);
        MissionSnapshot end = Current();
        Assert.That(session.Outcome, Is.EqualTo(MissionOutcome.TimeExpired));
        Assert.That(end.ScienceWorkSeconds, Is.EqualTo(0));
        Assert.That(end.Power.EnergyJoules, Is.EqualTo(CapacityJoules), "Surplus is curtailed, never stored above capacity.");
    }

    [Test] public void ResultDoesNotDependOnFrameLength()
    {
        double[] frames = { 1.0 / 60, 0.1, 0.7, 5 };
        var ends = new List<MissionSnapshot>();
        foreach (double frame in frames)
        {
            SetUp();
            Send(SessionAction.SetScienceEnabled, true);
            Send(SessionAction.Resume);
            Run(45, frame);
            Send(SessionAction.SetScienceEnabled, false);
            Run(75, frame);
            Send(SessionAction.SetScienceEnabled, true);
            Run(DurationSeconds, frame);
            ends.Add(Current());
            Assert.That(session.Outcome, Is.EqualTo(MissionOutcome.ObjectiveAchieved), "frame " + frame);
        }
        foreach (MissionSnapshot end in ends)
        {
            Assert.That(end.ScienceWorkSeconds, Is.EqualTo(TargetSeconds).Within(1e-6));
            // 45 s on, 75 s off, then 75 s more on: completion at t = 195 s whatever the frame.
            Assert.That(end.Power.SimulatedTimeSeconds, Is.EqualTo(195).Within(1e-6));
        }
    }

    [Test] public void PauseFreezesClockAndResumeContinues()
    {
        Send(SessionAction.SetScienceEnabled, true);
        Send(SessionAction.Resume);
        Run(20, 0.5);
        Assert.That(Send(SessionAction.Pause).Accepted, Is.True);
        MissionSnapshot paused = Current();
        int count = published.Count;
        Run(30, 0.5);
        Assert.That(published.Count, Is.EqualTo(count), "Paused publishes nothing from the clock.");
        Assert.That(Current().Power.SimulatedTimeSeconds, Is.EqualTo(paused.Power.SimulatedTimeSeconds));
        Assert.That(Send(SessionAction.Pause).Reason, Is.EqualTo("InvalidState"));
        Assert.That(Send(SessionAction.SetScienceEnabled, false).Accepted, Is.True, "Science can change while paused.");
        Assert.That(Send(SessionAction.Resume).Accepted, Is.True);
        Run(10, 0.5);
        Assert.That(Current().Power.SimulatedTimeSeconds, Is.EqualTo(30).Within(1e-9));
        Assert.That(Current().ScienceWorkSeconds, Is.EqualTo(20).Within(1e-9));
    }

    [Test] public void RestartAfterResultStartsCleanEpochAndOldCommandsAreRejected()
    {
        Send(SessionAction.SetScienceEnabled, true);
        Send(SessionAction.Resume);
        Run(DurationSeconds, 1);
        Assert.That(Current().State, Is.EqualTo(SessionState.Completed));
        Assert.That(Send(SessionAction.Resume).Reason, Is.EqualTo("SessionEnded"));

        Guid restartId = Guid.NewGuid();
        Assert.That(Send(SessionAction.Restart, null, restartId).Accepted, Is.True);
        MissionSnapshot fresh = Current();
        Assert.That(fresh.Epoch, Is.EqualTo(1));
        Assert.That(fresh.Revision, Is.EqualTo(0));
        Assert.That(fresh.State, Is.EqualTo(SessionState.Ready));
        Assert.That(fresh.ScienceEnabled, Is.False);
        Assert.That(fresh.ScienceWorkSeconds, Is.EqualTo(0));
        Assert.That(fresh.Power.EnergyJoules, Is.EqualTo(InitialEnergyJoules));
        Assert.That(fresh.Power.SimulatedTimeSeconds, Is.EqualTo(0));
        Assert.That(session.Outcome, Is.EqualTo(MissionOutcome.InProgress));

        // A second participant pressing Restart late with the old epoch does not restart again.
        session.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.Restart));
        Assert.That(results[results.Count - 1].Reason, Is.EqualTo("EpochMismatch"));
        // A network retry of the same Restart replays its stored result without a new epoch.
        int before = published.Count;
        session.Submit(new SessionCommand(restartId, 0, SessionAction.Restart));
        Assert.That(results[results.Count - 1].Accepted, Is.True);
        Assert.That(results[results.Count - 1].Epoch, Is.EqualTo(0));
        Assert.That(published.Count, Is.EqualTo(before));
        Assert.That(Current().Epoch, Is.EqualTo(1));

        Send(SessionAction.SetScienceEnabled, true);
        Send(SessionAction.Resume);
        Run(30, 1);
        Assert.That(Current().ScienceWorkSeconds, Is.EqualTo(30).Within(1e-9), "The new epoch plays like the first.");
    }

    [Test] public void TwoParticipantsSendingConflictingIntentionsAreOrderedByTheAuthority()
    {
        Send(SessionAction.Resume);
        long revision = Current().Revision;
        var on = new SessionCommand(Guid.NewGuid(), 0, SessionAction.SetScienceEnabled, true);
        var off = new SessionCommand(Guid.NewGuid(), 0, SessionAction.SetScienceEnabled, false);
        session.Submit(on);
        session.Submit(off);
        session.Submit(on); // Duplicate delivery of the first participant's command.
        Assert.That(Current().ScienceEnabled, Is.False, "Last applied intention wins; the duplicate is not reapplied.");
        Assert.That(Current().Revision, Is.EqualTo(revision + 2));
        Assert.That(results.FindAll(r => r.CommandId == on.Id).TrueForAll(r => r.Accepted), Is.True);
    }
}
