using System.Linq;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation;
using H = Phase03AuthorityHarness;

/// <summary>D2 phase 3: event, information, reception, validity and decisions stay separate; protection is effective.</summary>
public sealed class Phase03AuthorityAdvisoryTests
{
    private const string Text = "particle-warning";

    private static ParticleEventEpisode Episode(double issue, double shelterBy, double validUntil, double? end,
        double onset = 50, double eventEnd = 120)
    {
        var warning = new AdvisoryMessage("warning", AdvisoryKind.ShelterInstruction, issue, shelterBy, validUntil, null, Text);
        AdvisoryMessage[] messages = end.HasValue
            ? new[] { warning, new AdvisoryMessage("end", AdvisoryKind.InstructionEnd, end.Value, null, 999, "warning", "shelter-period-end") }
            : new[] { warning };
        return new ParticleEventEpisode("test-episode", true, "SINTÉTICO de prueba", null, onset, eventEnd, messages);
    }

    private static H WithEpisode(ParticleEventEpisode episode) => new H(H.With(ExpeditionFixtures.Scenario(false), episode: episode));

    private static void ToShelter(H h, PlayerId p, BasePosition from)
    {
        h.Walk(p, from, H.ShelterDoorAbove, H.InShelter);
        Assert.That(h.Crew(p).Zone, Is.EqualTo(CrewZone.Shelter));
    }

    [Test]
    public void FlowB_OutsideNewInformationCancelReturnShelterAndResult()
    {
        H h = WithEpisode(Episode(60, 100, 150, 130));
        PlayerId ana = h.Join(), leo = h.Join();
        h.Start(ana);
        h.PrepareSuit(ana, true);
        h.GoOut(ana, H.ChamberA);
        h.Walk(ana, H.AtRegolith);
        Assert.That(h.Now.Advisories, Is.Empty, "nothing is known before it is issued");
        Assert.That(h.StartTask(ana, ExpeditionFixtures.RegolithSite, ExpeditionFixtures.CollectRegolith).Accepted);
        h.Until(() => h.Now.Instruction.InForce, 20);
        Assert.That(h.Now.Advisories.Single().Message.IssuedAtSeconds, Is.EqualTo(60));
        Assert.That(h.Now.TryGetReception("warning", ana, out AdvisoryReception outside), "the regolith site is inside radio range");
        Assert.That(outside.ReceivedAtSeconds, Is.EqualTo(60).Within(1e-9), "the authority splits its step at the issue time");
        Assert.That(h.Now.TryGetReception("warning", leo, out _), "inside the base it arrives at once");
        Assert.That(h.Crew(ana).Protection, Is.EqualTo(ProtectionState.NotInShelter));
        Assert.That(h.Now.Disclosure, Is.Null, "the event truth stays hidden while playing");

        // Decision: cancel the science (no completion), declare the return, come back; Leo prepares the shelter.
        Assert.That(h.Cancel(ana, ExpeditionFixtures.RegolithSite).Accepted);
        Assert.That(h.DeclareReturn(ana).Accepted);
        Assert.That(h.Now.Decision.AdvisoriesKnown, Is.EquivalentTo(new[] { "warning" }), "the decision records what the declarer knew");
        ToShelter(h, leo, h.Position(leo));
        h.DoTask(leo, ExpeditionFixtures.ShelterPanel, ExpeditionFixtures.PrepareShelter);
        h.Tick(0.5); // ventilation starts in the step after preparation (it runs only while prepared and occupied)
        Assert.That(h.Crew(leo).Protection, Is.EqualTo(ProtectionState.Protected));
        h.ComeBack(ana, H.ChamberA);
        Assert.That(h.Now.Outcome, Is.EqualTo(ExpeditionOutcome.InProgress), "while the instruction is in force the mission waits for its end");
        ToShelter(h, ana, H.BeforeInnerHatch);
        Assert.That(h.Crew(ana).Protection, Is.EqualTo(ProtectionState.Protected));
        Assert.That(h.Time, Is.LessThan(100));

        h.Until(() => h.Now.Outpost.State != SessionState.Running, 60);
        Assert.That(h.Now.Outcome, Is.EqualTo(ExpeditionOutcome.ShelteredInTime));
        Assert.That(h.Now.Outpost.Outcome, Is.EqualTo(OutpostOutcome.ObjectiveAchieved), "sheltering in time is a correct result");
        Assert.That(h.Now.Assessments.Select(a => a.Category), Is.All.EqualTo(ProtectionCategory.InTime));
        Assert.That(h.Now.Objectives.Single(o => o.Key == "regolith-sample").Current, Is.EqualTo(0));
        Assert.That(h.Now.Disclosure.EventOnsetSeconds, Is.EqualTo(50), "the debrief reveals the event only at the end");
    }

    [Test]
    public void OutOfRangeReceptionArrivesLateAndIsAssessedAsSuch()
    {
        H h = WithEpisode(Episode(70, 80, 160, 150));
        PlayerId ana = h.Join(), leo = h.Join();
        h.Start(ana);
        // Leo prepares the shelter in advance and waits there (cooperation from the base).
        ToShelter(h, leo, h.Position(leo));
        h.DoTask(leo, ExpeditionFixtures.ShelterPanel, ExpeditionFixtures.PrepareShelter);
        h.PrepareSuit(ana, true);
        h.GoOut(ana, H.ChamberA);
        h.Walk(ana, H.AtRegolith, H.TowardCrater, H.AtCrater);
        Assert.That(h.StartTask(ana, ExpeditionFixtures.CraterRim, ExpeditionFixtures.CraterCore).Accepted);
        h.Until(() => h.Now.Instruction.InForce, 30);
        Assert.That(h.Now.TryGetReception("warning", ana, out _), Is.False, "the crater rim is outside radio range");
        Assert.That(h.Now.TryGetReception("warning", leo, out _), Is.True);
        Assert.That(h.Crew(leo).Protection, Is.EqualTo(ProtectionState.Protected));
        h.Until(() => h.Now.Outpost.TryGetStation(ExpeditionFixtures.CraterRim, out StationState s) && s.ActiveTask == null, 30);
        h.Walk(ana, H.TowardCrater);
        Assert.That(h.Now.TryGetReception("warning", ana, out AdvisoryReception late), "received on entering coverage");
        Assert.That(late.AfterShelterBy, Is.True);
        h.Walk(ana, H.AtRegolith);
        h.ComeBack(ana, H.ChamberA);
        ToShelter(h, ana, H.BeforeInnerHatch);
        h.Until(() => h.Now.Outpost.State != SessionState.Running, 80);
        Assert.That(h.Now.Outcome, Is.EqualTo(ExpeditionOutcome.ShelteredLate));
        ProtectionAssessment anaAssessment = h.Now.Assessments.Single(a => a.Player == ana);
        Assert.That(anaAssessment.Category, Is.EqualTo(ProtectionCategory.Late));
        Assert.That(anaAssessment.ReceivedAfterShelterBy, Is.True, "late because the information arrived late, not judged on unknown facts");
        Assert.That(h.Now.Assessments.Single(a => a.Player == leo).Category, Is.EqualTo(ProtectionCategory.InTime));
        Assert.That(h.Now.Objectives.Single(o => o.Key == "crater-core").Met, "the optional science was completed before the reception");
    }

    [Test]
    public void ShelterProtectionNeedsLocationPreparationAndService()
    {
        ExpeditionScenario s = H.With(ExpeditionFixtures.Scenario(false), episode: Episode(5, 60, 200, 190));
        // Synthetic weak base: 35 W spare (enough for the 30 W preparation, not for the 40 W ventilation).
        var h = new H(H.With(s, outpost: H.WithPower(s.Outpost, new PowerConfiguration(12000, 20000, 80, 60, 60, 100, 15))));
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Until(() => h.Now.Instruction.InForce, 10);
        Assert.That(h.Crew(ana).Protection, Is.EqualTo(ProtectionState.NotInShelter));
        ToShelter(h, ana, h.Position(ana));
        Assert.That(h.Crew(ana).Protection, Is.EqualTo(ProtectionState.ShelterNotPrepared), "standing in the room is not enough");
        h.DoTask(ana, ExpeditionFixtures.ShelterPanel, ExpeditionFixtures.PrepareShelter);
        h.Tick(1);
        Assert.That(h.Now.Shelter.Prepared && h.Now.Shelter.VentilationRunning);
        Assert.That(h.Crew(ana).Protection, Is.EqualTo(ProtectionState.ShelterServiceUnavailable), "no service, no protection");

        var good = new H(H.With(ExpeditionFixtures.Scenario(false), episode: Episode(5, 60, 200, 190)));
        PlayerId leo = good.Join();
        good.Start(leo);
        ToShelter(good, leo, good.Position(leo));
        good.DoTask(leo, ExpeditionFixtures.ShelterPanel, ExpeditionFixtures.PrepareShelter);
        good.Tick(1);
        Assert.That(good.Crew(leo).Protection, Is.EqualTo(ProtectionState.Protected));
        Assert.That(good.Now.Shelter.ServiceAvailable);
    }

    [Test]
    public void ExpiredInformationIsNotAnAllClearAndAMissingEpisodeIsNoInformation()
    {
        H h = WithEpisode(Episode(10, 20, 30, null));
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Until(() => h.Time >= 31, 40);
        Assert.That(h.Now.Advisories.Single().Validity, Is.EqualTo(AdvisoryValidity.Expired));
        Assert.That(h.Now.Instruction.State, Is.EqualTo(InstructionState.InForceWithoutCurrentInformation),
            "no current information does not lift the instruction");
        Assert.That(h.Crew(ana).Protection, Is.EqualTo(ProtectionState.NotInShelter));

        var none = new H();
        none.Join();
        none.Start();
        none.Tick(20);
        Assert.That(none.Now.Instruction.State, Is.EqualTo(InstructionState.None));
        Assert.That(none.Now.Advisories, Is.Empty);
        Assert.That(none.Now.Crew[0].Protection, Is.EqualTo(ProtectionState.NotRequired));
    }

    [Test]
    public void ExpiredWarningIsNotDeliveredLateAndUnprotectedReceiversFailTheInstruction()
    {
        H h = WithEpisode(Episode(10, 30, 60, 80));
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Until(() => h.Time >= 65, 70);
        PlayerId late = h.Join();
        Assert.That(h.Now.TryGetReception("warning", late, out _), Is.False, "expired information is never delivered as news");
        h.Until(() => h.Now.Outpost.State != SessionState.Running, 30);
        Assert.That(h.Now.Outcome, Is.EqualTo(ExpeditionOutcome.ProtectionIncomplete));
        Assert.That(h.Now.Assessments.Single(a => a.Player == ana).Category, Is.EqualTo(ProtectionCategory.NotProtected));
        Assert.That(h.Now.Assessments.Single(a => a.Player == late).Category, Is.EqualTo(ProtectionCategory.NotReceived));
        Assert.That(h.Now.Incidents.Any(i => i.Kind == IncidentKind.AdvisoryNotReceived && i.Player == late));
    }

    [Test]
    public void ALateJoinerReceivesTheCurrentInstructionWhenJoining()
    {
        H h = WithEpisode(Episode(10, 40, 100, 90));
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Tick(15);
        PlayerId leo = h.Join();
        Assert.That(h.Now.TryGetReception("warning", leo, out AdvisoryReception r) && r.ReceivedAtSeconds == h.Time);
        Assert.That(r.AfterShelterBy, Is.False);
    }

    [Test]
    public void EgressIsBlockedWhileAnInstructionIsInForceButAnEmptyCycleIsAllowed()
    {
        H h = WithEpisode(Episode(30, 80, 200, 190));
        PlayerId ana = h.Join();
        h.Start(ana);
        h.PrepareSuit(ana, true);
        h.Until(() => h.Now.Instruction.InForce, 20);
        h.Walk(ana, H.BeforeInnerHatch);
        Assert.That(h.Cycle(ana, ExpeditionFixtures.InnerPanel).Accepted, "an empty cycle (to fetch returning crew) is allowed");
        h.Until(() => h.Now.Airlock.OuterHatchOpen, 12);
        Assert.That(h.Cycle(ana, ExpeditionFixtures.InnerPanel).Accepted);
        h.Until(() => h.Now.Airlock.InnerHatchOpen, 12);
        h.Walk(ana, H.ChamberA);
        Assert.That(h.Cycle(ana, ExpeditionFixtures.InnerPanel).Reason, Is.EqualTo(ExpeditionReasons.EgressBlockedByInstruction));
    }

    [Test]
    public void OutcomesUseOnlyIssuedInformationNeverTheHiddenEventTimes()
    {
        ExpeditionOutcome Run(double onset, double end, out ProtectionCategory category)
        {
            H h = WithEpisode(Episode(10, 40, 200, 60, onset, end));
            PlayerId ana = h.Join();
            h.Start(ana);
            h.Tick(12);
            ToShelter(h, ana, h.Position(ana));
            h.DoTask(ana, ExpeditionFixtures.ShelterPanel, ExpeditionFixtures.PrepareShelter);
            h.Until(() => h.Now.Outpost.State != SessionState.Running, 60);
            category = h.Now.Assessments.Single().Category;
            return h.Now.Outcome;
        }
        ExpeditionOutcome early = Run(1, 5, out ProtectionCategory a);
        ExpeditionOutcome late = Run(55, 190, out ProtectionCategory b);
        Assert.That(early, Is.EqualTo(late));
        Assert.That(a, Is.EqualTo(b));
        Assert.That(early, Is.EqualTo(ExpeditionOutcome.ShelteredInTime));
    }
}
