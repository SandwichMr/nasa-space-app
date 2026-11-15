using System;
using System.Linq;
using NUnit.Framework;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.Eva;

/// <summary>Phase 3 common base: value rules of the frozen contracts (constructors refuse invalid input).</summary>
public sealed class Phase03ValueContractTests
{
    [Test]
    public void SuitModelNeedsExactlyOneServiceEachWithReserveBelowCapacityAndPositiveRates()
    {
        SuitModelConfiguration model = ExpeditionFixtures.SuitModel();
        Assert.That(model.Services.Select(s => s.Service), Is.EqualTo(new[] { SuitService.Oxygen, SuitService.Battery, SuitService.Feedwater }));
        Assert.That(model.Get(SuitService.Battery).Unit, Is.EqualTo("J"));
        Assert.That(model.Get(SuitService.Oxygen).Unit, Is.EqualTo("kg"));
        Assert.Throws<ArgumentException>(() => new SuitServiceModel(SuitService.Oxygen, 1, 1, 1, 1, 1), "reserve == capacity");
        Assert.Throws<ArgumentOutOfRangeException>(() => new SuitServiceModel(SuitService.Oxygen, 1, 0.1, 0, 1, 1), "zero rate");
        Assert.Throws<ArgumentOutOfRangeException>(() => new SuitServiceModel(SuitService.Oxygen, double.NaN, 0, 1, 1, 1));
        Assert.Throws<ArgumentException>(() => new SuitModelConfiguration(model.Services.Take(2), "x"), "missing service");
        Assert.Throws<ArgumentException>(() => new SuitModelConfiguration(
            model.Services.Concat(new[] { model.Services[0] }), "x"), "duplicate service");
        Assert.Throws<ArgumentException>(() => new SuitModelConfiguration(model.Services, " "), "provenance required");
    }

    [Test]
    public void LevelsAndSuitsRefuseNegativeNaNAndAboveCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SuitLevels(-0.1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SuitLevels(0, double.PositiveInfinity, 0));
        SuitModelConfiguration model = ExpeditionFixtures.SuitModel();
        var tooMuch = new SuitDefinition("suit-x", new SuitLevels(0.5, 0, 0)); // O2 capacity is 0.40 kg
        Assert.Throws<ArgumentException>(() => new EvaConfiguration(model, new[] { tooMuch }, new EvaSupplyConfiguration(0, 0, 0, 0)));
        Assert.Throws<ArgumentException>(() => new SuitDefinition("Suit A", new SuitLevels(0, 0, 0)), "stable key");
        Assert.Throws<ArgumentException>(() => new EvaSupplyConfiguration(3, 2, 0, 0), "recharge above capacity");
        Assert.That(new SuitLevels(0.1, 10, 0.2).With(SuitService.Battery, 20).BatteryJoules, Is.EqualTo(20));
    }

    [Test]
    public void TransfersAndDemandsHaveStrictShapes()
    {
        Assert.Throws<ArgumentException>(() => new EvaTransfer(Guid.Empty, EvaTransferKind.FillOxygen, 0, 0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EvaTransfer(Guid.NewGuid(), EvaTransferKind.FillOxygen, 0, 0));
        Assert.Throws<ArgumentException>(() => new EvaTransfer(Guid.NewGuid(), EvaTransferKind.UseMakeupGas, 0, 0.1), "make-up gas has no suit");
        Assert.Throws<ArgumentOutOfRangeException>(() => new EvaTransfer(Guid.NewGuid(), EvaTransferKind.ChargeBattery, -1, 10));
        Assert.Throws<ArgumentException>(() => new EvaDemand(new[] { new SuitDemand(0, SuitActivity.Walk, 0), new SuitDemand(0, SuitActivity.Rest, 0) }),
            "a suit consumes once per step");
        Assert.Throws<ArgumentOutOfRangeException>(() => new SuitDemand(0, SuitActivity.Work, double.NaN));
    }

    [Test]
    public void MarginMustBeTheSmallestServiceNeverAnAverage()
    {
        var plan = new ReturnPlan(10, 8, 0, 8, true);
        ServiceMargin[] services =
        {
            new ServiceMargin(SuitService.Oxygen, 0.2, 0.05, 0.003, 50),
            new ServiceMargin(SuitService.Battery, 2000, 400, 20, 80),
            new ServiceMargin(SuitService.Feedwater, 0.5, 0.05, 0.004, 112.5)
        };
        var ok = new ReturnMarginEstimate(MarginStatus.Sufficient, 12, 50, SuitService.Oxygen, services, plan);
        Assert.That(ok.LimitingService, Is.EqualTo(SuitService.Oxygen));
        double average = services.Average(s => s.MarginSeconds);
        Assert.Throws<ArgumentException>(() => new ReturnMarginEstimate(MarginStatus.Sufficient, 12, average, SuitService.Oxygen, services, plan),
            "an averaged margin is refused");
        Assert.Throws<ArgumentException>(() => new ReturnMarginEstimate(MarginStatus.Sufficient, 12, 50, SuitService.Battery, services, plan),
            "the limiting service must be the smallest");
        Assert.Throws<ArgumentException>(() => new ReturnMarginEstimate(MarginStatus.Insufficient, 12, 50, SuitService.Oxygen, services, plan),
            "insufficient means negative");
        Assert.Throws<ArgumentException>(() => new ReturnMarginEstimate(MarginStatus.Sufficient, 12, 50, SuitService.Oxygen, services,
            new ReturnPlan(10, 8, 0, 8, false)), "an unreachable plan cannot be sufficient");
        var unavailable = new ReturnMarginEstimate(MarginStatus.ReturnUnavailable, 12, 50, SuitService.Oxygen, services,
            new ReturnPlan(10, 8, 0, 8, false));
        Assert.That(unavailable.Plan.Reachable, Is.False);
        Assert.That(ReturnMarginEstimate.NotApplicable(3).Services, Is.Empty);
    }

    [Test]
    public void ExpeditionCommandShapeAndPhase2Mapping()
    {
        var station = ExpeditionFixtures.RegolithSite;
        var task = ExpeditionFixtures.CollectRegolith;
        Assert.That(ExpeditionCommand.StartTask(Guid.NewGuid(), 0, station, task, 3).ShapeError(), Is.Null);
        Assert.That(ExpeditionCommand.CycleAirlock(Guid.NewGuid(), 0, ExpeditionFixtures.InnerPanel, 1, 0).ShapeError(), Is.Null);
        Assert.That(ExpeditionCommand.DeclareReturn(Guid.NewGuid(), 0).ShapeError(), Is.Null);
        Assert.That(new ExpeditionCommand(Guid.Empty, 0, ExpeditionAction.Pause).ShapeError(), Is.EqualTo(OutpostReasons.InvalidCommandId));
        Assert.That(new ExpeditionCommand(Guid.NewGuid(), 0, (ExpeditionAction)99).ShapeError(), Is.EqualTo(OutpostReasons.UnknownAction));
        Assert.That(new ExpeditionCommand(Guid.NewGuid(), 0, ExpeditionAction.CycleAirlock).ShapeError(), Is.EqualTo(OutpostReasons.InvalidActionValue));
        Assert.That(new ExpeditionCommand(Guid.NewGuid(), 0, ExpeditionAction.Pause, zoneSequence: 2).ShapeError(),
            Is.EqualTo(OutpostReasons.InvalidActionValue), "zone stamps only on location-dependent intents");
        Assert.That(new ExpeditionCommand(Guid.NewGuid(), 0, ExpeditionAction.StartTask, station, task, 1, 4).ShapeError(),
            Is.EqualTo(OutpostReasons.InvalidActionValue), "airlock stamp only on CycleAirlock");
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExpeditionCommand(Guid.NewGuid(), -1, ExpeditionAction.Pause));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExpeditionCommand(Guid.NewGuid(), 0, ExpeditionAction.StartTask, station, task, -2));

        var outpost = OutpostCommand.StartTask(Guid.NewGuid(), 4, station, task);
        ExpeditionCommand mapped = ExpeditionCommand.FromOutpost(outpost, 7);
        Assert.That(mapped.Id, Is.EqualTo(outpost.Id));
        Assert.That(mapped.Epoch, Is.EqualTo(4));
        Assert.That(mapped.Action, Is.EqualTo(ExpeditionAction.StartTask));
        Assert.That(mapped.ZoneSequence, Is.EqualTo(7));
        Assert.That(ExpeditionCommand.FromOutpost(new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Restart), 7).ZoneSequence,
            Is.EqualTo(ExpeditionCommand.Unstated));
        Assert.That(ExpeditionCommand.FromOutpost(new OutpostCommand(Guid.NewGuid(), 0, (OutpostAction)42), 0).ShapeError(),
            Is.EqualTo(OutpostReasons.UnknownAction));
        Assert.That(mapped.HasSameContent(ExpeditionCommand.FromOutpost(outpost, 7)), Is.True);
        Assert.That(mapped.HasSameContent(ExpeditionCommand.FromOutpost(outpost, 8)), Is.False, "the stamp is content");
    }

    [Test]
    public void AdvisoriesSeparateEventInformationAndValidity()
    {
        Assert.Throws<ArgumentException>(() => new AdvisoryMessage("w", AdvisoryKind.ShelterInstruction, 10, null, 20, null, "t"),
            "an instruction needs a deadline");
        Assert.Throws<ArgumentException>(() => new AdvisoryMessage("w", AdvisoryKind.ShelterInstruction, 10, 5, 20, null, "t"),
            "the deadline cannot precede the message");
        Assert.Throws<ArgumentException>(() => new AdvisoryMessage("w", AdvisoryKind.ShelterInstruction, 10, 15, 10, null, "t"),
            "valid after issue");
        Assert.Throws<ArgumentException>(() => new AdvisoryMessage("e", AdvisoryKind.InstructionEnd, 10, null, 20, null, "t"),
            "an end replaces a message");
        Assert.Throws<ArgumentException>(() => new AdvisoryMessage("e", AdvisoryKind.InstructionEnd, 10, 12, 20, "w", "t"),
            "an end has no deadline");
        ParticleEventEpisode episode = ExpeditionFixtures.Episode();
        Assert.That(episode.Synthetic, Is.True);
        Assert.That(episode.Source, Is.Null);
        Assert.Throws<ArgumentException>(() => new ParticleEventEpisode("h", false, "x", null, 1, 2, episode.Messages),
            "a historical episode needs source, timestamps, units and context");
        Assert.Throws<ArgumentException>(() => new ParticleEventEpisode("p", true, "x", null, 1, 2, episode.Messages.Reverse()),
            "messages in issue order, replacements refer to earlier ones");
        var source = new HistoricalSource("NOAA SWPC", "https://example.invalid/event", "2017-09-10T16:25Z", "2017-09-13T00:00Z",
            ">=10 MeV proton flux", "pfu", "GOES (Earth orbit)", "Detector reading in Earth orbit: not a lunar human dose");
        Assert.That(new ParticleEventEpisode("h", false, "x", source, 1, 2, episode.Messages).Source.Unit, Is.EqualTo("pfu"));
        Assert.Throws<ArgumentException>(() => new HistoricalSource("x", "y", "s", "e", "q", " ", "l", "c"), "units required");
        Assert.Throws<ArgumentException>(() => new ShelterInstructionStatus(InstructionState.InForce, "w", 10, null, null));
        Assert.Throws<ArgumentException>(() => new ShelterInstructionStatus(InstructionState.Ended, "w", 10, 20, null));
        Assert.That(ShelterInstructionStatus.None.InForce, Is.False);
        Assert.That(new ShelterInstructionStatus(InstructionState.InForceWithoutCurrentInformation, "w", 10, 20, null).InForce,
            Is.True, "expired information does not lift an instruction");
    }

    [Test]
    public void AssessmentsAndIncidentsMatchTheirCategory()
    {
        var p = new PlayerId(1);
        Assert.That(new ProtectionAssessment(p, ProtectionCategory.NotReceived, null, null, false).Category, Is.EqualTo(ProtectionCategory.NotReceived));
        Assert.Throws<ArgumentException>(() => new ProtectionAssessment(p, ProtectionCategory.NotReceived, 3, null, false));
        Assert.Throws<ArgumentException>(() => new ProtectionAssessment(p, ProtectionCategory.InTime, 3, null, false), "protected needs a time");
        Assert.Throws<ArgumentException>(() => new ProtectionAssessment(p, ProtectionCategory.NotProtected, 3, 4, false));
        Assert.Throws<ArgumentException>(() => new ExpeditionIncident(IncidentKind.ReserveUsed, p, 1, "suit-a", null));
        Assert.Throws<ArgumentException>(() => new ExpeditionIncident(IncidentKind.DisconnectedWhileSealed, p, 1, "suit-a", SuitService.Oxygen));
        Assert.Throws<ArgumentException>(() => new ReturnDecision(PlayerId.None, 1, Array.Empty<string>(), null));
    }

    [Test]
    public void OutcomeMappingKeepsPhase2StateMeaning()
    {
        foreach (ExpeditionOutcome outcome in Enum.GetValues(typeof(ExpeditionOutcome)))
            Assert.DoesNotThrow(() => ExpeditionOutcomes.ToOutpostOutcome(outcome));
        Assert.That(ExpeditionOutcomes.ToOutpostOutcome(ExpeditionOutcome.ReturnedWithoutScience), Is.EqualTo(OutpostOutcome.ObjectiveAchieved),
            "returning without science after a declared return is a safe result, not a defeat");
        Assert.That(ExpeditionOutcomes.ToOutpostOutcome(ExpeditionOutcome.ShelteredInTime), Is.EqualTo(OutpostOutcome.ObjectiveAchieved));
        Assert.That(ExpeditionOutcomes.ToOutpostOutcome(ExpeditionOutcome.SuitServiceDepleted), Is.EqualTo(OutpostOutcome.EssentialServiceLost));
        Assert.That(ExpeditionOutcomes.IsSafe(ExpeditionOutcome.ProtectionIncomplete), Is.False);
    }

    [Test]
    public void ScenarioReferencesAreChecked()
    {
        ExpeditionScenario scenario = ExpeditionFixtures.Scenario();
        Assert.That(scenario.TryGetZone(scenario.Layout.SpawnPoints[0], out CrewZone zone) && zone == CrewZone.Interior);
        Assert.That(scenario.TryGetZone(new Trainer.Application.Outpost.BasePosition(13.6, 5), out zone) && zone == CrewZone.Airlock);
        Assert.That(scenario.TryGetZone(new Trainer.Application.Outpost.BasePosition(20, 5), out zone) && zone == CrewZone.Exterior);
        Assert.That(scenario.TryGetZone(new Trainer.Application.Outpost.BasePosition(3, 1.5), out zone) && zone == CrewZone.Shelter);
        Assert.That(scenario.RuleFor(ExpeditionFixtures.TreatWater).Purpose, Is.EqualTo(TaskPurpose.BaseOperation), "tasks without a rule are base work");
        Assert.That(scenario.RuleFor(ExpeditionFixtures.CollectRegolith).Suit, Is.EqualTo(SuitRequirement.Sealed));
        Assert.That(scenario.Airlock.IsPanel(ExpeditionFixtures.OuterPanel), Is.True);
        Assert.That(scenario.LayoutWithHatches(true, true).Obstacles.Count, Is.EqualTo(scenario.Layout.Obstacles.Count + 2));

        // A wrong zone for a rule, a missing Don or an exterior task with a base effect are refused.
        Assert.Throws<ArgumentException>(() => Rebuild(scenario, rules: scenario.TaskRules
            .Where(r => r.Task != ExpeditionFixtures.CollectRegolith)
            .Concat(new[] { ExpeditionTaskRule.Exterior(ExpeditionFixtures.TreatWater, 5) })));
        Assert.Throws<ArgumentException>(() => Rebuild(scenario, rules: scenario.TaskRules.Where(r => r.Task != ExpeditionFixtures.DonB)));
        Assert.Throws<ArgumentException>(() => Rebuild(scenario, rules: scenario.TaskRules.Where(r => r.Step != ChecklistStep.SealCheck)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rebuild(scenario, speed: 1.5));
    }

    private static ExpeditionScenario Rebuild(ExpeditionScenario s, System.Collections.Generic.IEnumerable<ExpeditionTaskRule> rules = null,
        double? speed = null) =>
        new ExpeditionScenario(s.Outpost, s.Zones, s.Airlock, s.Racks, s.Eva, rules ?? s.TaskRules, s.Shelter, s.Comms, s.Episode,
            s.Rules, speed ?? s.SuitedSpeedFactor);
}
