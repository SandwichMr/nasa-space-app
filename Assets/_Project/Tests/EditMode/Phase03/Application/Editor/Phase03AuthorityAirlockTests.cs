using System;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation;
using Trainer.Simulation.Eva;
using H = Phase03AuthorityHarness;

/// <summary>D2 phase 3: the airlock is exclusive, physical (hatches block), costly and decided only by the authority.</summary>
public sealed class Phase03AuthorityAirlockTests
{
    [Test]
    public void CycleRequestsAreRefusedByPanelDistanceReadinessAndObstruction()
    {
        var h = new H();
        PlayerId ana = h.Join(), leo = h.Join();
        h.Start(ana);
        Assert.That(h.Cycle(ana, ExpeditionFixtures.InnerPanel).Reason, Is.EqualTo(OutpostReasons.OutOfRange), "from the spawn point");
        h.Walk(ana, H.AtSuitCheck);
        Assert.That(h.Send(ana, ExpeditionCommand.CycleAirlock(Guid.NewGuid(), 0, ExpeditionFixtures.SuitCheck, ExpeditionCommand.Unstated,
            ExpeditionCommand.Unstated)).Reason, Is.EqualTo(ExpeditionReasons.NotAnAirlockPanel));
        h.Walk(ana, H.BeforeInnerHatch, H.ChamberA);
        Assert.That(h.Crew(ana).Zone, Is.EqualTo(CrewZone.Airlock));
        Assert.That(h.Cycle(ana, ExpeditionFixtures.InnerPanel).Reason, Is.EqualTo(ExpeditionReasons.SuitNotReady),
            "nobody depressurizes without a ready suit");
        // Leo stands in the inner doorway: no hatch closes on a person.
        h.Walk(leo, H.BeforeInnerHatch, new BasePosition(12.15, 5.0));
        Assert.That(h.Cycle(ana, ExpeditionFixtures.InnerPanel).Reason, Is.EqualTo(ExpeditionReasons.AirlockObstructed));
        Assert.That(h.Now.Airlock.State, Is.EqualTo(AirlockState.PressurizedInnerOpen));
    }

    [Test]
    public void TwoSimultaneousRequestsStartOneCycleAndTheStaleOneIsRefused()
    {
        var h = new H();
        PlayerId ana = h.Join(), leo = h.Join();
        h.Start(ana);
        h.PrepareSuit(ana, true);
        h.Walk(ana, H.BeforeInnerHatch, H.ChamberA);
        h.Walk(leo, H.BeforeInnerHatch);
        // Both made the intent seeing cycle 0; arrival order decides.
        var first = ExpeditionCommand.CycleAirlock(Guid.NewGuid(), 0, ExpeditionFixtures.InnerPanel, h.Crew(ana).ZoneSequence, 0);
        var second = ExpeditionCommand.CycleAirlock(Guid.NewGuid(), 0, ExpeditionFixtures.InnerPanel, h.Crew(leo).ZoneSequence, 0);
        Assert.That(h.Send(ana, first).Accepted);
        Assert.That(h.Send(leo, second).Reason, Is.EqualTo(ExpeditionReasons.StaleAirlockState),
            "a request made for the previous state never runs the next cycle");
        var unstamped = new ExpeditionCommand(Guid.NewGuid(), 0, ExpeditionAction.CycleAirlock, ExpeditionFixtures.InnerPanel);
        Assert.That(h.Send(leo, unstamped).Reason, Is.EqualTo(ExpeditionReasons.AirlockBusy));
        Assert.That(h.Now.Airlock.CycleSequence, Is.EqualTo(1));
        Assert.That(h.Now.Airlock.CycleRequestedBy, Is.EqualTo(ana));
        Assert.That(h.Send(ana, first).Accepted, "an exact replay reproduces the stored result without a second cycle");
        Assert.That(h.Now.Airlock.CycleSequence, Is.EqualTo(1));
    }

    [Test]
    public void DepressurizingSealsOccupantsMovesTheirDemandFromBaseToSuitAndClosesBothHatches()
    {
        var h = new H();
        PlayerId ana = h.Join(), leo = h.Join();
        h.Start(ana);
        h.PrepareSuit(ana, true);
        h.Walk(ana, H.BeforeInnerHatch, H.ChamberA);

        double baseO2 = h.Base.Ledger.CrewOxygenIntakeKilograms;
        h.Tick(4);
        double twoAboard = h.Base.Ledger.CrewOxygenIntakeKilograms - baseO2;
        Assert.That(h.Cycle(ana, ExpeditionFixtures.InnerPanel).Accepted);
        Assert.That(h.Crew(ana).OnSuitSupply, Is.True);
        Assert.That(h.Now.Suits[0].Status, Is.EqualTo(SuitStatus.Sealed));
        Assert.That(h.Now.Phase, Is.EqualTo(ExpeditionPhase.Underway));
        Assert.That(h.Crew(ana).Margin.Status, Is.EqualTo(MarginStatus.Sufficient));
        Assert.That(h.Roster.ObstacleOverlay.Count, Is.EqualTo(2), "both hatches closed during the cycle");

        baseO2 = h.Base.Ledger.CrewOxygenIntakeKilograms;
        double suitO2 = h.Eva.Ledger.OxygenConsumedKilograms;
        h.Tick(4);
        double oneAboard = h.Base.Ledger.CrewOxygenIntakeKilograms - baseO2;
        Assert.That(oneAboard, Is.EqualTo(twoAboard / 2).Within(1e-12), "the sealed member no longer breathes base air");
        Assert.That(h.Eva.Ledger.OxygenConsumedKilograms - suitO2, Is.EqualTo(4 * h.Scenario.Eva.Model.Get(SuitService.Oxygen).RestRate).Within(1e-9),
            "the suit charges exactly one member at rest");
        Assert.That(h.Now.Suits[1].Reading.OnInternalSupply, Is.False);

        // Leo cannot follow: the inner hatch is closed.
        h.Move(leo, MoveDirection.FromAxes(1, 0));
        h.Tick(3);
        h.Move(leo, MoveDirection.Zero);
        Assert.That(h.Crew(leo).Zone, Is.EqualTo(CrewZone.Interior));
        Assert.That(h.Position(leo).X, Is.LessThanOrEqualTo(12.0 - 0.35 + 1e-9));

        h.Until(() => h.Now.Airlock.State == AirlockState.DepressurizedOuterOpen, 10);
        Assert.That(h.Roster.ObstacleOverlay, Is.EquivalentTo(new[] { ExpeditionFixtures.InnerHatch }), "only the outer hatch opened");
        long zone = h.Crew(ana).ZoneSequence;
        h.Walk(ana, H.OutsideHatch);
        Assert.That(h.Crew(ana).Zone, Is.EqualTo(CrewZone.Exterior));
        Assert.That(h.Crew(ana).ZoneSequence, Is.EqualTo(zone + 1));
    }

    [Test]
    public void RepressurizingTakesMakeupGasOnceUnsealsAndResetsTheChecklist()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.PrepareSuit(ana, true);
        h.GoOut(ana, H.ChamberA);
        double gas = h.Now.Supply.MakeupGasKilograms;
        h.ComeBack(ana, H.ChamberA, cycle: false);
        Assert.That(h.Cycle(ana, ExpeditionFixtures.InnerPanel).Accepted);
        Assert.That(h.Now.Supply.MakeupGasKilograms, Is.EqualTo(gas - h.Scenario.Airlock.MakeupGasPerCycleKilograms).Within(1e-9));
        h.Until(() => h.Now.Airlock.State == AirlockState.PressurizedInnerOpen, 12);
        Assert.That(h.Now.Supply.MakeupGasKilograms, Is.EqualTo(gas - h.Scenario.Airlock.MakeupGasPerCycleKilograms).Within(1e-9),
            "taken once, at the start");
        Assert.That(h.Crew(ana).OnSuitSupply, Is.False);
        Assert.That(h.Now.Suits[0].Status, Is.EqualTo(SuitStatus.Worn));
        Assert.That(h.Now.Suits[0].Completed, Is.EqualTo(ChecklistSteps.Don), "another egress needs recharge and recheck");
        Assert.That(h.Eva.Ledger.MakeupGasUsedKilograms, Is.EqualTo(h.Scenario.Airlock.MakeupGasPerCycleKilograms).Within(1e-9));
    }

    [Test]
    public void WithoutMakeupGasTheReturnIsUnavailableAndTheRepressurizationRefused()
    {
        ExpeditionScenario s = ExpeditionFixtures.Scenario(false);
        EvaConfiguration noGas = new EvaConfiguration(s.Eva.Model, s.Eva.Suits, new EvaSupplyConfiguration(2, 2, 0, 3));
        var h = new H(H.With(s, eva: noGas));
        PlayerId ana = h.Join();
        h.Start(ana);
        h.PrepareSuit(ana, true);
        h.GoOut(ana, H.ChamberA);
        Assert.That(h.Crew(ana).Margin.Status, Is.EqualTo(MarginStatus.ReturnUnavailable), "no claim of a margin the airlock cannot honour");
        h.ComeBack(ana, H.ChamberA, cycle: false);
        Assert.That(h.Cycle(ana, ExpeditionFixtures.InnerPanel).Reason, Is.EqualTo(OutpostReasons.InsufficientResources));
    }

    [Test]
    public void TheCycleStallsWhileTheBaseCannotServeThePump()
    {
        ExpeditionScenario s = ExpeditionFixtures.Scenario(false);
        // Synthetic weak base: 80 W generation, 60 W essential, battery discharge limited to 200 W -> 220 W spare:
        // enough for the 200 W charger or the 120 W pump alone, not for pump + 150 W water treatment.
        var weak = new PowerConfiguration(12000, 20000, 80, 60, 60, 100, 200);
        var h = new H(H.With(s, outpost: H.WithPower(s.Outpost, weak)));
        PlayerId ana = h.Join(), leo = h.Join();
        h.Start(ana);
        h.PrepareSuit(ana, true);
        h.Walk(leo, H.AtWater);
        h.Walk(ana, H.BeforeInnerHatch, H.ChamberA);
        Assert.That(h.StartTask(leo, ExpeditionFixtures.WaterRecycler, ExpeditionFixtures.TreatWater).Accepted); // 150 + 120 W > 220 W spare
        Assert.That(h.Cycle(ana, ExpeditionFixtures.InnerPanel).Accepted);
        h.Tick(3);
        Assert.That(h.Now.Airlock.Stalled, Is.True, "pump + water treatment exceed what the base can serve");
        Assert.That(h.Now.Airlock.CycleWorkSeconds, Is.EqualTo(0).Within(1e-9));
        Assert.That(h.Cancel(leo, ExpeditionFixtures.WaterRecycler).Accepted);
        h.Tick(3);
        Assert.That(h.Now.Airlock.Stalled, Is.False);
        Assert.That(h.Now.Airlock.CycleWorkSeconds, Is.GreaterThan(2.5));
    }
}
