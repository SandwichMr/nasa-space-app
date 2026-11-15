using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.Eva;

/// <summary>Phase 3 common base: the authoritative snapshot refuses inconsistent states and is immutable.</summary>
public sealed class Phase03SnapshotContractTests
{
    private static readonly PlayerId Ana = new PlayerId(1);
    private static readonly PlayerId Leo = new PlayerId(2);
    private ExpeditionScenario scenario;
    private ExpeditionSessionSnapshot initial;

    [SetUp]
    public void SetUp()
    {
        scenario = ExpeditionFixtures.Scenario();
        initial = ExpeditionFixtures.Initial(scenario, 0, Ana, Leo);
    }

    private static ReturnMarginEstimate Margin(double at) => new ReturnMarginEstimate(MarginStatus.Sufficient, at, 40,
        SuitService.Oxygen, new[]
        {
            new ServiceMargin(SuitService.Oxygen, 0.2, 0.05, 0.003, 40),
            new ServiceMargin(SuitService.Battery, 3000, 500, 22, 100),
            new ServiceMargin(SuitService.Feedwater, 0.5, 0.05, 0.0045, 90)
        }, new ReturnPlan(6, 5, 0, 8, true));

    [Test]
    public void InitialFixtureIsConsistentAndMatchesPhase2()
    {
        Assert.That(initial.Outpost.Crew.Select(c => c.Player), Is.EqualTo(initial.Crew.Select(c => c.Player)));
        Assert.That(initial.Suits.All(s => s.Status == SuitStatus.Stowed && !s.Wearer.IsValid));
        Assert.That(initial.Airlock.State, Is.EqualTo(AirlockState.PressurizedInnerOpen));
        Assert.That(initial.Airlock.InnerHatchOpen && !initial.Airlock.OuterHatchOpen);
        Assert.That(initial.Instruction.State, Is.EqualTo(InstructionState.None), "no information is not 'safe': it is None");
        Assert.That(initial.Outcome, Is.EqualTo(ExpeditionOutcome.InProgress));
        Assert.That(initial.Outpost.Objectives.Single(o => o.Key == "crew-safe").Required, Is.True);
        Assert.That(initial.Disclosure, Is.Null);
        Assert.That(initial.TryGetTeamWorstMargin(out _, out _), Is.False);
    }

    [Test]
    public void SuitSupplyRulesCannotBeViolated()
    {
        // Outside without suit supply.
        Assert.Throws<ArgumentException>(() => ExpeditionFixtures.Edit(scenario, initial).Member(Ana, zone: CrewZone.Exterior).Build());
        // Sealed inside the pressurized base.
        Assert.Throws<ArgumentException>(() => ExpeditionFixtures.Edit(scenario, initial)
            .Suit(0, SuitStatus.Sealed, Ana, ChecklistSteps.All).Member(Ana, suitIndex: 0, onSuitSupply: true, margin: Margin(0)).Build());
        // A suit worn by an absent member.
        Assert.Throws<ArgumentException>(() => ExpeditionFixtures.Edit(scenario, initial).Suit(0, SuitStatus.Worn, new PlayerId(9), ChecklistSteps.Don).Build());
        // A member pointing at a suit that does not name them.
        Assert.Throws<ArgumentException>(() => ExpeditionFixtures.Edit(scenario, initial).Member(Ana, suitIndex: 1).Build());
        // Sealed without the whole checklist.
        Assert.Throws<ArgumentException>(() => new SuitState(0, "suit-a", SuitStatus.Sealed, Ana, ChecklistSteps.Don,
            initial.Suits[0].Reading));
    }

    [Test]
    public void AirlockOccupantsAreTheChamberMembersAndSealedWithoutPressure()
    {
        ExpeditionSessionSnapshot inChamber = ExpeditionFixtures.Edit(scenario, initial)
            .Suit(0, SuitStatus.Worn, Ana, ChecklistSteps.All).Member(Ana, new BasePosition(13.6, 5), CrewZone.Airlock, suitIndex: 0).Build();
        Assert.That(inChamber.Airlock.Occupants, Is.EqualTo(new[] { Ana }));
        Assert.That(inChamber.Crew[0].ZoneSequence, Is.EqualTo(1), "a confirmed zone change advances the sequence");
        // Depressurizing with an occupant not on suit supply.
        Assert.Throws<ArgumentException>(() => ExpeditionFixtures.Edit(scenario, inChamber)
            .Airlock(AirlockState.Depressurizing, 1, 2, 8, false, Ana).Build());
        ExpeditionSessionSnapshot cycling = ExpeditionFixtures.Edit(scenario, inChamber)
            .Suit(0, SuitStatus.Sealed, Ana, ChecklistSteps.All).Member(Ana, onSuitSupply: true, margin: Margin(0))
            .Airlock(AirlockState.Depressurizing, 1, 2, 8, false, Ana).Build();
        Assert.That(cycling.Airlock.IsCycling && !cycling.Airlock.InnerHatchOpen && !cycling.Airlock.OuterHatchOpen);
        Assert.That(cycling.TryGetTeamWorstMargin(out PlayerId worst, out _) && worst == Ana);
        Assert.Throws<ArgumentException>(() => new AirlockStatus(AirlockState.Depressurizing, 1, 9, 8, false, new[] { Ana }, Ana),
            "work within the duration");
        Assert.Throws<ArgumentException>(() => new AirlockStatus(AirlockState.PressurizedInnerOpen, 0, 1, 8, false, new PlayerId[0], Ana),
            "an idle airlock has no cycle progress");
    }

    [Test]
    public void NoFutureInformationAndNoEarlyDisclosure()
    {
        AdvisoryMessage warning = scenario.Episode.Messages[0]; // issued at 45 s
        Assert.Throws<ArgumentException>(() => ExpeditionFixtures.Edit(scenario, initial).AtTime(10).Issue(warning).Build(),
            "a snapshot never carries a message that is not issued yet");
        ExpeditionSessionSnapshot issued = ExpeditionFixtures.Edit(scenario, initial).Session(SessionState.Running).AtTime(50)
            .Issue(warning).Receive(warning.Id, Ana, 45)
            .Instruction(new ShelterInstructionStatus(InstructionState.InForce, warning.Id, 75, 110, null)).Build();
        Assert.That(issued.TryGetReception(warning.Id, Ana, out AdvisoryReception r) && !r.AfterShelterBy);
        Assert.Throws<ArgumentException>(() => ExpeditionFixtures.Edit(scenario, issued).Receive(warning.Id, Leo, 60).Receive(warning.Id, Leo, 61).Build(),
            "one reception per member and message");
        Assert.Throws<ArgumentException>(() => ExpeditionFixtures.Edit(scenario, issued).Receive(warning.Id, Leo, 70).Build(),
            "a reception cannot be in the future");
        Assert.Throws<ArgumentException>(() => new ExpeditionSessionSnapshot(issued.Outpost, issued.Crew, issued.Suits, issued.Airlock,
            issued.Shelter, issued.Supply, issued.Advisories, issued.Receptions, issued.Instruction, issued.Phase, issued.Decision,
            issued.Outcome, issued.Objectives, issued.Assessments, issued.Incidents, EventDisclosure.Of(scenario.Episode)),
            "the event truth is disclosed only after the session ends");
        ExpeditionSessionSnapshot ended = ExpeditionFixtures.Edit(scenario, issued).Ended(ExpeditionOutcome.ShelteredInTime).Build();
        Assert.That(ended.Disclosure.EventOnsetSeconds, Is.EqualTo(40));
        Assert.That(ended.Outpost.State, Is.EqualTo(SessionState.Completed));
        Assert.That(ended.Outpost.Outcome, Is.EqualTo(OutpostOutcome.ObjectiveAchieved));
    }

    [Test]
    public void OutcomeAndPhaseMustMatchThePhase2State()
    {
        Assert.Throws<ArgumentException>(() => new ExpeditionSessionSnapshot(initial.Outpost, initial.Crew, initial.Suits, initial.Airlock,
            initial.Shelter, initial.Supply, initial.Advisories, initial.Receptions, initial.Instruction, initial.Phase, initial.Decision,
            ExpeditionOutcome.ScienceReturned, initial.Objectives, initial.Assessments, initial.Incidents, null));
        Assert.Throws<ArgumentException>(() => ExpeditionFixtures.Edit(scenario, initial).Phase(ExpeditionPhase.Closed).Build());
        Assert.Throws<ArgumentException>(() => ExpeditionFixtures.Edit(scenario, initial).Phase(ExpeditionPhase.Returning).Build(),
            "returning needs the decision");
    }

    [Test]
    public void SnapshotsOrderLikePhase2AndListsAreReadOnlyCopies()
    {
        ExpeditionSessionSnapshot next = ExpeditionFixtures.Edit(scenario, initial).Build();
        Assert.That(next.IsNewerThan(initial) && !initial.IsNewerThan(next) && !next.IsNewerThan(next));
        Assert.That(next.Revision, Is.EqualTo(initial.Revision + 1));
        Assert.Throws<NotSupportedException>(() => ((IList)next.Crew).Add(null));
        Assert.Throws<NotSupportedException>(() => ((IList)next.Suits).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList)next.Airlock.Occupants).Add(Ana));
    }

    [Test]
    public void PublishedPhase3TypesHaveNoPublicSetters()
    {
        Type[] types = typeof(ExpeditionSessionSnapshot).Assembly.GetTypes()
            .Where(t => t.IsPublic && (t.Namespace == "Trainer.Application.Expedition" || t.Namespace == "Trainer.Simulation.Eva")
                && t.IsClass).ToArray();
        Assert.That(types.Length, Is.GreaterThan(30));
        foreach (Type type in types)
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                Assert.That(property.SetMethod == null || !property.SetMethod.IsPublic, Is.True, type.Name + "." + property.Name);
        foreach (Type type in types)
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                Assert.Fail(type.Name + "." + field.Name + " is a public field");
    }
}
