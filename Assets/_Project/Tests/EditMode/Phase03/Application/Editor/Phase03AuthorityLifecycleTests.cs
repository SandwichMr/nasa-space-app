using System;
using System.Linq;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation;
using Trainer.Simulation.Eva;
using H = Phase03AuthorityHarness;

/// <summary>D2 phase 3: presence, restart, pause, staleness and idempotence keep one consistent session.</summary>
public sealed class Phase03AuthorityLifecycleTests
{
    [Test]
    public void DisconnectingOutsideLeavesNoGhostNoConsumptionAndRecordsTheIncident()
    {
        var h = new H();
        PlayerId ana = h.Join(), leo = h.Join();
        h.Start(ana);
        h.PrepareSuit(ana, true);
        h.GoOut(ana, H.ChamberA);
        h.Walk(ana, H.AtRegolith);
        Assert.That(h.StartTask(ana, ExpeditionFixtures.RegolithSite, ExpeditionFixtures.CollectRegolith).Accepted);
        h.Tick(2);
        Assert.That(h.Authority.Leave(ana), Is.True);
        Assert.That(h.Now.TryGetCrew(ana, out _), Is.False, "no ghost astronaut");
        Assert.That(h.Now.Suits[0].Status, Is.EqualTo(SuitStatus.Abandoned));
        Assert.That(h.Now.Suits[0].Wearer.IsValid, Is.False);
        h.Now.Outpost.TryGetStation(ExpeditionFixtures.RegolithSite, out StationState site);
        Assert.That(site.ActiveTask, Is.Null, "no phantom reservation");
        Assert.That(site.LastEnd.Reason, Is.EqualTo(TaskEndReason.PerformerLeft));
        Assert.That(h.Now.Incidents.Single().Kind, Is.EqualTo(IncidentKind.DisconnectedWhileSealed));
        SuitLevels frozen = h.Now.Suits[0].Reading.Levels;
        h.Tick(10);
        Assert.That(h.Now.Suits[0].Reading.Levels, Is.EqualTo(frozen), "an abandoned suit consumes nothing");
        Assert.That(h.Authority.Leave(ana), Is.False, "Leave is idempotent");

        // Reconnection: a new identity inside the base, no duplicate, the abandoned suit is not reused.
        PlayerId again = h.Join();
        Assert.That(again, Is.Not.EqualTo(ana));
        Assert.That(h.Crew(again).Zone, Is.EqualTo(CrewZone.Interior));
        Assert.That(h.Crew(again).SuitIndex, Is.EqualTo(-1));
        Assert.That(h.Now.Crew.Count, Is.EqualTo(2));
        h.Walk(again, H.AtRackA);
        Assert.That(h.StartTask(again, ExpeditionFixtures.RackA, ExpeditionFixtures.DonA).Reason, Is.EqualTo(ExpeditionReasons.SuitNotOnRack));
    }

    [Test]
    public void LeavingWithAnUnsealedSuitPutsItBackOnItsRack()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Walk(ana, H.AtRackA);
        h.DoTask(ana, ExpeditionFixtures.RackA, ExpeditionFixtures.DonA);
        h.Authority.Leave(ana);
        Assert.That(h.Now.Suits[0].Status, Is.EqualTo(SuitStatus.Stowed));
        Assert.That(h.Now.Suits[0].Completed, Is.EqualTo(ChecklistSteps.None));
        Assert.That(h.Now.Incidents, Is.Empty);
    }

    [Test]
    public void RestartReturnsEverythingAndOldIntentsAreRefused()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.PrepareSuit(ana, true);
        h.GoOut(ana, H.ChamberA);
        long zone = h.Crew(ana).ZoneSequence;
        ExpeditionCommand old = ExpeditionCommand.StartTask(Guid.NewGuid(), 0, ExpeditionFixtures.RegolithSite, ExpeditionFixtures.CollectRegolith, zone);
        Assert.That(h.Session(ana, ExpeditionAction.Restart).Accepted);
        ExpeditionSessionSnapshot s = h.Now;
        Assert.That(s.Epoch, Is.EqualTo(1));
        Assert.That(s.Revision, Is.EqualTo(0));
        Assert.That(s.Outpost.State, Is.EqualTo(SessionState.Ready));
        Assert.That(s.Suits.All(x => x.Status == SuitStatus.Stowed));
        Assert.That(s.Suits[0].Reading.Levels, Is.EqualTo(ExpeditionFixtures.RackLevels()));
        Assert.That(s.Airlock.State, Is.EqualTo(AirlockState.PressurizedInnerOpen));
        Assert.That(s.Airlock.CycleSequence, Is.EqualTo(0));
        Assert.That(h.Crew(ana).Zone, Is.EqualTo(CrewZone.Interior));
        Assert.That(h.Crew(ana).ZoneSequence, Is.EqualTo(zone + 1), "the return to the spawn point is a zone change");
        Assert.That(h.Crew(ana).OnSuitSupply, Is.False);
        Assert.That(s.Phase, Is.EqualTo(ExpeditionPhase.Preparing));
        Assert.That(h.Roster.SpeedFactorOf(ana), Is.EqualTo(1));
        Assert.That(h.Send(ana, old).Reason, Is.EqualTo(OutpostReasons.EpochMismatch));
    }

    [Test]
    public void PauseFreezesClockConsumptionAndCyclesAndRefusesCycling()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.PrepareSuit(ana, true);
        h.Walk(ana, H.BeforeInnerHatch, H.ChamberA);
        Assert.That(h.Cycle(ana, ExpeditionFixtures.InnerPanel).Accepted);
        h.Tick(2);
        Assert.That(h.Session(ana, ExpeditionAction.Pause).Accepted);
        double time = h.Time, work = h.Now.Airlock.CycleWorkSeconds;
        SuitLevels levels = h.Now.Suits[0].Reading.Levels;
        h.Tick(10);
        Assert.That(h.Time, Is.EqualTo(time));
        Assert.That(h.Now.Airlock.CycleWorkSeconds, Is.EqualTo(work));
        Assert.That(h.Now.Suits[0].Reading.Levels, Is.EqualTo(levels));
        Assert.That(h.DeclareReturn(ana).Accepted, "a decision can be recorded while paused");
        Assert.That(h.Session(ana, ExpeditionAction.Resume).Accepted);
        h.Tick(1);
        Assert.That(h.Now.Airlock.CycleWorkSeconds, Is.GreaterThan(work));
    }

    [Test]
    public void IntentsMadeBeforeAZoneChangeAreRefusedAndReplaysAreIdempotent()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.PrepareSuit(ana, true);
        h.Walk(ana, H.BeforeInnerHatch);
        long seen = h.Crew(ana).ZoneSequence;
        h.Walk(ana, H.ChamberA); // zone changes to Airlock after the intent below was "made"
        var stale = ExpeditionCommand.CycleAirlock(Guid.NewGuid(), 0, ExpeditionFixtures.InnerPanel, seen, h.Now.Airlock.CycleSequence);
        Assert.That(h.Send(ana, stale).Reason, Is.EqualTo(ExpeditionReasons.StaleZone));
        int revisions = h.Published.Count;
        Assert.That(h.Send(ana, stale).Reason, Is.EqualTo(ExpeditionReasons.StaleZone), "a replay reproduces the stored result");
        Assert.That(h.Published.Count, Is.EqualTo(revisions), "a rejection or a replay publishes nothing");
        var conflict = ExpeditionCommand.CycleAirlock(stale.Id, 0, ExpeditionFixtures.InnerPanel, h.Crew(ana).ZoneSequence, 0);
        Assert.That(h.Send(ana, conflict).Reason, Is.EqualTo(OutpostReasons.CommandIdConflict));
        Assert.That(h.Cycle(ana, ExpeditionFixtures.InnerPanel).Accepted, "the same intent with the current stamp is accepted");
    }

    [Test]
    public void LosingTheBaseEssentialServiceInterruptsTheMission()
    {
        ExpeditionScenario s = ExpeditionFixtures.Scenario(false);
        // Synthetic failing base: no battery energy, generation below the essential load.
        var h = new H(H.With(s, outpost: H.WithPower(s.Outpost, new PowerConfiguration(0, 20000, 40, 60, 60, 100, 400))));
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Tick(1);
        Assert.That(h.Now.Outcome, Is.EqualTo(ExpeditionOutcome.EssentialServiceLost));
        Assert.That(h.Now.Outpost.State, Is.EqualTo(SessionState.Interrupted));
        Assert.That(h.Session(ana, ExpeditionAction.Resume).Reason, Is.EqualTo(OutpostReasons.SessionEnded));
        Assert.That(h.Session(ana, ExpeditionAction.Restart).Accepted, "only Restart is accepted after the end");
    }

    [Test]
    public void EveryPublishedSnapshotIsConsistentAndOrdered()
    {
        var h = new H(H.With(ExpeditionFixtures.Scenario(false), episode: ExpeditionFixtures.Episode()));
        PlayerId ana = h.Join(), leo = h.Join();
        h.Start(ana);
        h.PrepareSuit(ana, true);
        h.GoOut(ana, H.ChamberA);
        h.Walk(leo, H.ShelterDoorAbove, H.InShelter);
        h.Tick(5);
        for (int i = 1; i < h.Published.Count; i++)
            Assert.That(h.Published[i].IsNewerThan(h.Published[i - 1]), $"snapshot {i} moves forward");
        Assert.That(h.Published.All(p => p.Outpost.Crew.Count == p.Crew.Count));
        Assert.That(h.Published.Where(p => p.Outpost.State == SessionState.Running).All(p => p.Disclosure == null));
    }
}
