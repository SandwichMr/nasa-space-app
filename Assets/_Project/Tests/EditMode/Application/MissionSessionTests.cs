// Compiled only when TRAINER_MISSION_TESTS is defined. This allowed path is outside an Editor folder
// and there is no test .asmdef, so without the guard NUnit would break Assembly-CSharp.
// See docs/phase-01/handoffs/02_MISSION_HANDOFF.md for activation options.
#if TRAINER_MISSION_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Simulation;

public sealed class MissionSessionTests
{
    /// <summary>Scripted IPowerSimulation internal to these tests: time advances exactly, served
    /// power is controlled by the test. No physics; it is not a second public API.</summary>
    private sealed class ScriptedPowerSimulation : IPowerSimulation
    {
        public int ResetCount;
        public readonly List<double> Steps = new List<double>();
        public readonly List<bool> ScienceFlags = new List<bool>();
        public double ScienceServedFraction = 1;
        public bool EssentialServed = true;
        public bool FreezeTime;          // clock does not move (contract violation)
        public double TimeDrift;         // each step lands this many seconds short (rounding error)
        public int ThrowOnAdvanceCall;   // 1-based Advance call that throws; 0 = never
        public bool ThrowOnReset;
        private PowerConfiguration config;
        private PowerSnapshot snapshot;

        public PowerSnapshot Snapshot => snapshot ?? throw new InvalidOperationException("Reset first.");

        public void Reset(PowerConfiguration configuration)
        {
            config = configuration ?? throw new ArgumentNullException(nameof(configuration));
            if (ThrowOnReset) throw new InvalidOperationException("Scripted reset failure.");
            ResetCount++;
            snapshot = Make(0, false);
        }

        public void Advance(double elapsedSeconds, bool scienceRequested)
        {
            if (config == null) throw new InvalidOperationException("Reset first.");
            Steps.Add(elapsedSeconds);
            ScienceFlags.Add(scienceRequested);
            if (Steps.Count == ThrowOnAdvanceCall) throw new InvalidOperationException("Scripted advance failure.");
            if (elapsedSeconds == 0 || FreezeTime) return;
            snapshot = Make(snapshot.SimulatedTimeSeconds + elapsedSeconds - TimeDrift, scienceRequested);
        }

        private PowerSnapshot Make(double time, bool science)
        {
            double essential = config.EssentialConsumptionWatts;
            double scienceDemand = science ? config.ScienceConsumptionWatts : 0;
            return new PowerSnapshot(time, config.InitialEnergyJoules, config.CapacityJoules, config.GenerationWatts,
                essential, EssentialServed ? essential : essential / 2,
                scienceDemand, scienceDemand * ScienceServedFraction);
        }
    }

    private static readonly PowerConfiguration Config = new PowerConfiguration(5000, 10000, 100, 60, 60, 100, 200);

    private ScriptedPowerSimulation sim;
    private MissionSession session;
    private List<MissionSnapshot> snapshots;
    private List<CommandResult> results;

    [SetUp] public void SetUp()
    {
        sim = new ScriptedPowerSimulation();
        session = new MissionSession(sim, Config, new MissionRules(10, 100), 1.0);
        snapshots = new List<MissionSnapshot>();
        results = new List<CommandResult>();
        session.SnapshotChanged += snapshots.Add;
        session.CommandCompleted += results.Add;
    }

    private MissionSnapshot Current()
    {
        Assert.That(session.TryGetSnapshot(out MissionSnapshot value), Is.True);
        return value;
    }

    private CommandResult Send(SessionAction action, bool? science = null, long? epoch = null)
    {
        session.Submit(new SessionCommand(Guid.NewGuid(), epoch ?? Current().Epoch, action, science));
        return results[results.Count - 1];
    }

    [Test] public void StartsReadyAtEpochZeroWithResetSimulation()
    {
        MissionSnapshot s = Current();
        Assert.That(s.Epoch, Is.EqualTo(0L));
        Assert.That(s.Revision, Is.EqualTo(0L));
        Assert.That(s.State, Is.EqualTo(SessionState.Ready));
        Assert.That(s.ScienceEnabled, Is.False);
        Assert.That(s.ScienceWorkSeconds, Is.EqualTo(0.0));
        Assert.That(s.Power, Is.SameAs(sim.Snapshot));
        Assert.That(sim.ResetCount, Is.EqualTo(1));
    }

    [Test] public void ConstructorRejectsMissingDependencies()
    {
        var rules = new MissionRules(10, 100);
        Assert.Throws<ArgumentNullException>(() => new MissionSession(null, Config, rules));
        Assert.Throws<ArgumentNullException>(() => new MissionSession(new ScriptedPowerSimulation(), null, rules));
        Assert.Throws<ArgumentNullException>(() => new MissionSession(new ScriptedPowerSimulation(), Config, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MissionSession(new ScriptedPowerSimulation(), Config, rules, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MissionSession(new ScriptedPowerSimulation(), Config, rules, double.NaN));
    }

    [Test] public void ResultIsCorrelatedToCommandIdAndOriginalEpoch()
    {
        var command = new SessionCommand(Guid.NewGuid(), 0, SessionAction.Resume);
        session.Submit(command);
        Assert.That(results.Count, Is.EqualTo(1));
        Assert.That(results[0].CommandId, Is.EqualTo(command.Id));
        Assert.That(results[0].Epoch, Is.EqualTo(0L));
        Assert.That(results[0].Accepted, Is.True);
        Assert.That(results[0].Reason, Is.EqualTo("Accepted"));
        Assert.That(Current().State, Is.EqualTo(SessionState.Running));
        Assert.That(Current().Revision, Is.EqualTo(1L));
        Assert.That(snapshots.Count, Is.EqualTo(1));
    }

    [Test] public void AllowedTransitionsAndRejections()
    {
        Assert.That(Send(SessionAction.Pause).Reason, Is.EqualTo("InvalidState"));
        Assert.That(Send(SessionAction.SetScienceEnabled, true).Accepted, Is.True);
        Assert.That(Send(SessionAction.Resume).Accepted, Is.True);
        Assert.That(Send(SessionAction.Resume).Reason, Is.EqualTo("InvalidState"));
        Assert.That(Send(SessionAction.Pause).Accepted, Is.True);
        Assert.That(Current().State, Is.EqualTo(SessionState.Paused));
        Assert.That(Send(SessionAction.SetScienceEnabled, false).Accepted, Is.True);
        Assert.That(Send(SessionAction.Resume).Accepted, Is.True);
        Assert.That(Current().State, Is.EqualTo(SessionState.Running));
        Assert.That(Current().ScienceEnabled, Is.False);
        Assert.That(Current().Revision, Is.EqualTo(5L));
    }

    [Test] public void InvalidCommandsAreRejectedWithoutChangingSnapshot()
    {
        MissionSnapshot before = Current();
        session.Submit(new SessionCommand(Guid.Empty, 0, SessionAction.Resume));
        Assert.That(results[results.Count - 1].Reason, Is.EqualTo("InvalidCommandId"));
        session.Submit(new SessionCommand(Guid.NewGuid(), 0, (SessionAction)999));
        Assert.That(results[results.Count - 1].Reason, Is.EqualTo("UnknownAction"));
        Assert.That(Send(SessionAction.Resume, true).Reason, Is.EqualTo("InvalidActionValue"));
        Assert.That(Send(SessionAction.SetScienceEnabled).Reason, Is.EqualTo("InvalidActionValue"));
        Assert.That(Current(), Is.SameAs(before));
        Assert.That(snapshots.Count, Is.EqualTo(0));
        Assert.Throws<ArgumentNullException>(() => session.Submit(null));
    }

    [Test] public void RepeatedCommandIsNotAppliedTwiceAndConflictIsRejected()
    {
        var command = new SessionCommand(Guid.NewGuid(), 0, SessionAction.SetScienceEnabled, true);
        session.Submit(command);
        session.Submit(new SessionCommand(command.Id, 0, SessionAction.SetScienceEnabled, true));
        Assert.That(results.Count, Is.EqualTo(2));
        Assert.That(results[1], Is.SameAs(results[0]));
        Assert.That(snapshots.Count, Is.EqualTo(1));
        Assert.That(Current().Revision, Is.EqualTo(1L));

        session.Submit(new SessionCommand(command.Id, 0, SessionAction.SetScienceEnabled, false));
        Assert.That(results[2].Accepted, Is.False);
        Assert.That(results[2].Reason, Is.EqualTo("CommandIdConflict"));
        Assert.That(Current().ScienceEnabled, Is.True);
    }

    [Test] public void RejectionsAreAlsoRememberedPerId()
    {
        var pause = new SessionCommand(Guid.NewGuid(), 0, SessionAction.Pause);
        session.Submit(pause);
        Send(SessionAction.Resume);
        session.Submit(pause); // now Pause would be valid, but the stored rejection is replayed
        Assert.That(results[results.Count - 1].Reason, Is.EqualTo("InvalidState"));
        Assert.That(Current().State, Is.EqualTo(SessionState.Running));
    }

    [Test] public void RestartResetsSimulationStateAndObjective()
    {
        Send(SessionAction.SetScienceEnabled, true);
        Send(SessionAction.Resume);
        session.Tick(3);
        Assert.That(Current().ScienceWorkSeconds, Is.EqualTo(3.0));

        Assert.That(Send(SessionAction.Restart).Accepted, Is.True);
        MissionSnapshot s = Current();
        Assert.That(s.Epoch, Is.EqualTo(1L));
        Assert.That(s.Revision, Is.EqualTo(0L));
        Assert.That(s.State, Is.EqualTo(SessionState.Ready));
        Assert.That(s.ScienceEnabled, Is.False);
        Assert.That(s.ScienceWorkSeconds, Is.EqualTo(0.0));
        Assert.That(s.Power.SimulatedTimeSeconds, Is.EqualTo(0.0));
        Assert.That(sim.ResetCount, Is.EqualTo(2));
        Assert.That(session.Outcome, Is.EqualTo(MissionOutcome.InProgress));
    }

    [Test] public void RestartReplayDoesNotRestartAgainAndOldOrFutureEpochsAreRejected()
    {
        var restart = new SessionCommand(Guid.NewGuid(), 0, SessionAction.Restart);
        session.Submit(restart);
        session.Submit(restart);
        Assert.That(results[1], Is.SameAs(results[0]));
        Assert.That(results[1].Epoch, Is.EqualTo(0L));
        Assert.That(Current().Epoch, Is.EqualTo(1L));
        Assert.That(sim.ResetCount, Is.EqualTo(2));

        Assert.That(Send(SessionAction.Resume, epoch: 0).Reason, Is.EqualTo("EpochMismatch"));
        Assert.That(Send(SessionAction.Resume, epoch: 2).Reason, Is.EqualTo("EpochMismatch"));
        Assert.That(Send(SessionAction.Resume, epoch: 1).Accepted, Is.True);
    }

    [Test] public void TickDoesNothingUnlessRunning()
    {
        session.Tick(5);
        Send(SessionAction.Resume);
        Send(SessionAction.Pause);
        int published = snapshots.Count;
        session.Tick(5);
        session.Tick(0);
        Assert.That(sim.Steps.Count, Is.EqualTo(0));
        Assert.That(snapshots.Count, Is.EqualTo(published));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Tick(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Tick(double.PositiveInfinity));
    }

    [Test] public void TickSubStepsAndPublishesOneConsistentSnapshot()
    {
        Send(SessionAction.SetScienceEnabled, true);
        Send(SessionAction.Resume);
        MissionSnapshot before = Current();
        int published = snapshots.Count;
        session.Tick(3.5);
        Assert.That(sim.Steps, Is.EqualTo(new List<double> { 1, 1, 1, 0.5 }));
        Assert.That(sim.ScienceFlags.TrueForAll(f => f), Is.True);
        Assert.That(snapshots.Count, Is.EqualTo(published + 1));
        MissionSnapshot after = Current();
        Assert.That(after, Is.SameAs(snapshots[snapshots.Count - 1]));
        Assert.That(after.Revision, Is.EqualTo(before.Revision + 1));
        Assert.That(after.Power, Is.SameAs(sim.Snapshot));
        Assert.That(after.ScienceWorkSeconds, Is.EqualTo(3.5));
        Assert.That(before.ScienceWorkSeconds, Is.EqualTo(0.0));
        Assert.That(before.Power.SimulatedTimeSeconds, Is.EqualTo(0.0));
    }

    [Test] public void OnlyFullyServedScienceCountsAsWork()
    {
        Send(SessionAction.Resume);
        session.Tick(2); // science disabled
        Assert.That(Current().ScienceWorkSeconds, Is.EqualTo(0.0));
        Send(SessionAction.SetScienceEnabled, true);
        sim.ScienceServedFraction = 0.5;
        session.Tick(2); // requested but only partially served
        Assert.That(Current().ScienceWorkSeconds, Is.EqualTo(0.0));
        sim.ScienceServedFraction = 1;
        session.Tick(2);
        Assert.That(Current().ScienceWorkSeconds, Is.EqualTo(2.0));
    }

    [Test] public void ReachingObjectiveCompletesAndOnlyRestartIsAccepted()
    {
        Send(SessionAction.SetScienceEnabled, true);
        Send(SessionAction.Resume);
        session.Tick(25);
        Assert.That(Current().State, Is.EqualTo(SessionState.Completed));
        Assert.That(session.Outcome, Is.EqualTo(MissionOutcome.ObjectiveAchieved));
        Assert.That(sim.Steps.Count, Is.EqualTo(10)); // stops advancing once the outcome is reached
        Assert.That(Current().ScienceWorkSeconds, Is.EqualTo(10.0));

        int steps = sim.Steps.Count;
        session.Tick(5);
        Assert.That(sim.Steps.Count, Is.EqualTo(steps));
        Assert.That(Send(SessionAction.Resume).Reason, Is.EqualTo("SessionEnded"));
        Assert.That(Send(SessionAction.SetScienceEnabled, false).Reason, Is.EqualTo("SessionEnded"));
        Assert.That(Send(SessionAction.Restart).Accepted, Is.True);
        Assert.That(Current().State, Is.EqualTo(SessionState.Ready));
    }

    [Test] public void EssentialShortfallInterrupts()
    {
        Send(SessionAction.Resume);
        session.Tick(1);
        sim.EssentialServed = false;
        session.Tick(3);
        Assert.That(Current().State, Is.EqualTo(SessionState.Interrupted));
        Assert.That(session.Outcome, Is.EqualTo(MissionOutcome.EssentialServiceLost));
        Assert.That(sim.Steps.Count, Is.EqualTo(2));
        Assert.That(Send(SessionAction.Pause).Reason, Is.EqualTo("SessionEnded"));
    }

    [Test] public void DurationEndsMissionWithoutOvershooting()
    {
        Send(SessionAction.Resume);
        session.Tick(99.5);
        Assert.That(Current().State, Is.EqualTo(SessionState.Running));
        session.Tick(10);
        Assert.That(Current().State, Is.EqualTo(SessionState.Completed));
        Assert.That(session.Outcome, Is.EqualTo(MissionOutcome.TimeExpired));
        Assert.That(Current().Power.SimulatedTimeSeconds, Is.EqualTo(100.0));
    }

    [Test] public void UnsubscribedHandlersAreNotCalledAndReentrantSubmitIsSafe()
    {
        int extra = 0;
        Action<MissionSnapshot> handler = _ => extra++;
        session.SnapshotChanged += handler;
        Send(SessionAction.Resume);
        session.SnapshotChanged -= handler;
        Send(SessionAction.Pause);
        Assert.That(extra, Is.EqualTo(1));

        bool submitted = false;
        session.SnapshotChanged += s =>
        {
            if (submitted || s.State != SessionState.Running) return;
            submitted = true;
            session.Submit(new SessionCommand(Guid.NewGuid(), s.Epoch, SessionAction.Pause));
        };
        Send(SessionAction.Resume);
        Assert.That(Current().State, Is.EqualTo(SessionState.Paused));
        Assert.That(results.TrueForAll(r => r.Accepted), Is.True);
    }
    [Test] public void LastStepIsTrimmedToRemainingScienceWork()
    {
        var local = new ScriptedPowerSimulation();
        var s = new MissionSession(local, Config, new MissionRules(2.5, 100), 1.0);
        s.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.SetScienceEnabled, true));
        s.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.Resume));
        s.Tick(5);
        Assert.That(local.Steps, Is.EqualTo(new List<double> { 1, 1, 0.5 }));
        Assert.That(s.TryGetSnapshot(out MissionSnapshot snap), Is.True);
        Assert.That(snap.ScienceWorkSeconds, Is.EqualTo(2.5));
        Assert.That(snap.State, Is.EqualTo(SessionState.Completed));
    }

    [Test] public void FrozenSimulationClockDoesNotLoopOrPublish()
    {
        Send(SessionAction.Resume);
        int published = snapshots.Count;
        sim.FreezeTime = true;
        session.Tick(1000);
        Assert.That(sim.Steps.Count, Is.EqualTo(1));
        Assert.That(snapshots.Count, Is.EqualTo(published));
        Assert.That(Current().State, Is.EqualTo(SessionState.Running));
    }

    [Test] public void SimulationLandingJustShortOfDurationStillEndsMission()
    {
        Send(SessionAction.Resume);
        sim.TimeDrift = 1e-9;
        session.Tick(150);
        Assert.That(Current().State, Is.EqualTo(SessionState.Completed));
        Assert.That(session.Outcome, Is.EqualTo(MissionOutcome.TimeExpired));
        Assert.That(sim.Steps.Count < 110, Is.True);
    }

    [Test] public void AdvanceFailureKeepsCompletedProgressAndInterrupts()
    {
        Send(SessionAction.SetScienceEnabled, true);
        Send(SessionAction.Resume);
        sim.ThrowOnAdvanceCall = 3;
        int published = snapshots.Count;
        Assert.Throws<InvalidOperationException>(() => session.Tick(5));
        MissionSnapshot s = Current();
        Assert.That(s.State, Is.EqualTo(SessionState.Interrupted));
        Assert.That(s.ScienceWorkSeconds, Is.EqualTo(2.0));
        Assert.That(s.Power.SimulatedTimeSeconds, Is.EqualTo(2.0));
        Assert.That(session.Outcome, Is.EqualTo(MissionOutcome.SimulationFault));
        Assert.That(snapshots.Count, Is.EqualTo(published + 1));
        Assert.That(Send(SessionAction.Resume).Reason, Is.EqualTo("SessionEnded"));
        sim.ThrowOnAdvanceCall = 0;
        Assert.That(Send(SessionAction.Restart).Accepted, Is.True);
        Assert.That(Current().State, Is.EqualTo(SessionState.Ready));
    }

    [Test] public void RestartFailureIsRecordedAndReplayDoesNotResetAgain()
    {
        Send(SessionAction.Resume);
        sim.ThrowOnReset = true;
        var restart = new SessionCommand(Guid.NewGuid(), 0, SessionAction.Restart);
        Assert.Throws<InvalidOperationException>(() => session.Submit(restart));
        Assert.That(results[results.Count - 1].Reason, Is.EqualTo("SimulationFault"));
        Assert.That(Current().Epoch, Is.EqualTo(0L));
        Assert.That(Current().State, Is.EqualTo(SessionState.Interrupted));
        Assert.That(session.Outcome, Is.EqualTo(MissionOutcome.SimulationFault));

        sim.ThrowOnReset = false;
        session.Submit(restart); // replay: stored rejection, no second reset
        Assert.That(results[results.Count - 1].Reason, Is.EqualTo("SimulationFault"));
        Assert.That(sim.ResetCount, Is.EqualTo(1));
        Assert.That(Send(SessionAction.Restart).Accepted, Is.True);
        Assert.That(Current().Epoch, Is.EqualTo(1L));
        Assert.That(sim.ResetCount, Is.EqualTo(2));
    }
}
#endif
