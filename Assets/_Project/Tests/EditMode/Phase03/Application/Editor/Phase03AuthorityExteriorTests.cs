using System.Linq;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.Eva;
using H = Phase03AuthorityHarness;

/// <summary>D2 phase 3: exterior work, return margin, declared return and the closing of the expedition.</summary>
public sealed class Phase03AuthorityExteriorTests
{
    [Test]
    public void ExteriorWorkNeedsTheExteriorAndRunsOnTheSuitWithoutRefundWhenCancelled()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.PrepareSuit(ana, true);
        h.GoOut(ana, H.ChamberA);
        h.Walk(ana, H.AtRegolith);
        SuitLevels before = h.Now.Suits[0].Reading.Levels;
        Assert.That(h.StartTask(ana, ExpeditionFixtures.RegolithSite, ExpeditionFixtures.CollectRegolith).Accepted);
        h.Tick(5);
        Assert.That(h.Crew(ana).Activity, Is.EqualTo(SuitActivity.Work));
        SuitModelConfiguration model = h.Scenario.Eva.Model;
        Assert.That(before.BatteryJoules - h.Now.Suits[0].Reading.Levels.BatteryJoules,
            Is.EqualTo(5 * (model.Get(SuitService.Battery).WorkRate + 10)).Within(1e-6), "work rate plus the 10 W tool");
        Assert.That(h.Cancel(ana, ExpeditionFixtures.RegolithSite).Accepted);
        Assert.That(h.Now.Objectives.Single(o => o.Key == "regolith-sample").Current, Is.EqualTo(0), "a cancelled task counts nothing");
        Assert.That(h.Now.Suits[0].Reading.Levels.BatteryJoules, Is.LessThan(before.BatteryJoules), "consumed suit services are not refunded");
        Assert.That(h.Base.Snapshot.ServiceLoadRequestedWatts, Is.EqualTo(0), "exterior work never draws base power");
    }

    [Test]
    public void ExteriorTasksAreRefusedFromInsideAndAfterTheReturnIsDeclared()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        Assert.That(h.StartTask(ana, ExpeditionFixtures.RegolithSite, ExpeditionFixtures.CollectRegolith).Reason, Is.EqualTo(OutpostReasons.OutOfRange));
        Assert.That(h.DeclareReturn(ana).Reason, Is.EqualTo(ExpeditionReasons.NoExpeditionUnderway));
        h.PrepareSuit(ana, true);
        h.GoOut(ana, H.ChamberA);
        h.Walk(ana, H.AtRegolith);
        Assert.That(h.DeclareReturn(ana).Accepted);
        Assert.That(h.Now.Phase, Is.EqualTo(ExpeditionPhase.Returning));
        Assert.That(h.Now.Decision.DeclaredBy, Is.EqualTo(ana));
        Assert.That(h.Now.Decision.TeamMarginSeconds.HasValue, Is.True, "the decision records the margin known then");
        Assert.That(h.StartTask(ana, ExpeditionFixtures.RegolithSite, ExpeditionFixtures.CollectRegolith).Reason, Is.EqualTo(ExpeditionReasons.ReturnDeclared));
        Assert.That(h.DeclareReturn(ana).Reason, Is.EqualTo(ExpeditionReasons.ReturnAlreadyDeclared));
    }

    [Test]
    public void MarginIsPerMemberLimitedByOneServiceAndTheTeamValueIsTheWorst()
    {
        var h = new H();
        PlayerId ana = h.Join(), leo = h.Join();
        h.Start(ana);
        h.PrepareSuit(ana, true);
        h.PrepareSuit(leo, false);
        h.Walk(ana, H.BeforeInnerHatch, H.ChamberA);
        h.Walk(leo, H.BeforeInnerHatch, H.ChamberB);
        Assert.That(h.Cycle(ana, ExpeditionFixtures.InnerPanel).Accepted);
        Assert.That(h.Crew(leo).OnSuitSupply, Is.True, "every occupant is sealed by the cycle");
        h.Until(() => h.Now.Airlock.OuterHatchOpen, 10);
        h.Walk(ana, H.OutsideHatch);
        h.Walk(leo, H.OutsideHatch, H.AtRegolith, H.TowardCrater, H.AtCrater);
        ReturnMarginEstimate near = h.Crew(ana).Margin, far = h.Crew(leo).Margin;
        Assert.That(far.Plan.PathMeters, Is.GreaterThan(near.Plan.PathMeters));
        Assert.That(far.Plan.PathMeters, Is.GreaterThanOrEqualTo(h.Position(leo).DistanceTo(h.Scenario.Airlock.ExteriorEntry)),
            "the route is never shorter than the straight line");
        Assert.That(far.MarginSeconds, Is.LessThan(near.MarginSeconds));
        Assert.That(far.MarginSeconds, Is.EqualTo(far.Services.Min(s => s.MarginSeconds)), "limited by the first service that runs short");
        Assert.That(h.Now.TryGetTeamWorstMargin(out PlayerId worst, out ReturnMarginEstimate team) && worst == leo);
        Assert.That(team.MarginSeconds, Is.EqualTo(far.MarginSeconds), "the team margin is the worst member, not an average");
        Assert.That(near.Plan.AirlockWaitSeconds, Is.EqualTo(0), "the outer hatch is open: no wait");
        Assert.That(near.Plan.IngressSeconds, Is.EqualTo(h.Scenario.Airlock.RepressurizeSeconds));
    }

    [Test]
    public void FlowA_PrepareGoOutDoScienceReturnIngressAndResult()
    {
        var h = new H();
        PlayerId ana = h.Join(), leo = h.Join();
        h.Start(ana);
        h.PrepareSuit(ana, true);
        h.GoOut(ana, H.ChamberA);
        h.Walk(ana, H.AtRegolith);
        h.DoTask(ana, ExpeditionFixtures.RegolithSite, ExpeditionFixtures.CollectRegolith);
        Assert.That(h.Now.Objectives.Single(o => o.Key == "regolith-sample").Met);
        Assert.That(h.Now.Outcome, Is.EqualTo(ExpeditionOutcome.InProgress), "nobody is back yet");
        // The base keeps working while someone is outside.
        h.Walk(leo, H.AtWater);
        h.DoTask(leo, ExpeditionFixtures.WaterRecycler, ExpeditionFixtures.TreatWater);
        h.ComeBack(ana, H.ChamberA);
        Assert.That(h.Now.Outcome, Is.EqualTo(ExpeditionOutcome.ScienceReturned));
        Assert.That(h.Now.Outpost.State, Is.EqualTo(SessionState.Completed));
        Assert.That(h.Now.Outpost.Outcome, Is.EqualTo(OutpostOutcome.ObjectiveAchieved));
        Assert.That(h.Now.Objectives.Single(o => o.Role == ObjectiveRole.Safety).Met);
        Assert.That(h.Now.Phase, Is.EqualTo(ExpeditionPhase.Closed));
    }

    [Test]
    public void EveryoneWhoWentOutMustBeBackNotOnlyTheOneWithMoreSupplies()
    {
        var h = new H();
        PlayerId ana = h.Join(), leo = h.Join();
        h.Start(ana);
        h.PrepareSuit(ana, true);
        h.PrepareSuit(leo, false);
        h.Walk(ana, H.BeforeInnerHatch, H.ChamberA);
        h.Walk(leo, H.BeforeInnerHatch, H.ChamberB);
        Assert.That(h.Cycle(leo, ExpeditionFixtures.InnerPanel).Accepted);
        h.Until(() => h.Now.Airlock.OuterHatchOpen, 10);
        h.Walk(ana, H.OutsideHatch, H.AtRegolith);
        h.DoTask(ana, ExpeditionFixtures.RegolithSite, ExpeditionFixtures.CollectRegolith);
        h.Walk(leo, H.OutsideHatch);
        h.ComeBack(ana, H.ChamberA); // Ana is back inside; Leo is still outside
        Assert.That(h.Now.Outcome, Is.EqualTo(ExpeditionOutcome.InProgress));
        Assert.That(h.Crew(leo).Zone, Is.EqualTo(CrewZone.Exterior));
        Assert.That(h.Crew(leo).OnSuitSupply, Is.True);
        // Leo calls the empty airlock from outside (allowed: it is not an egress), comes in and only then it closes.
        Assert.That(h.Cycle(leo, ExpeditionFixtures.OuterPanel).Accepted);
        h.Until(() => h.Now.Airlock.OuterHatchOpen, 10);
        Assert.That(h.Now.Outcome, Is.EqualTo(ExpeditionOutcome.InProgress));
        h.ComeBack(leo, H.ChamberA);
        Assert.That(h.Now.Outcome, Is.EqualTo(ExpeditionOutcome.ScienceReturned));
    }

    [Test]
    public void CancellingInTimeAndReturningWithoutScienceIsACorrectResult()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.PrepareSuit(ana, true);
        h.GoOut(ana, H.ChamberA);
        h.Walk(ana, H.AtRegolith);
        Assert.That(h.StartTask(ana, ExpeditionFixtures.RegolithSite, ExpeditionFixtures.CollectRegolith).Accepted);
        h.Tick(4);
        Assert.That(h.Cancel(ana, ExpeditionFixtures.RegolithSite).Accepted);
        Assert.That(h.DeclareReturn(ana).Accepted);
        h.ComeBack(ana, H.ChamberA);
        Assert.That(h.Now.Outcome, Is.EqualTo(ExpeditionOutcome.ReturnedWithoutScience));
        Assert.That(h.Now.Outpost.Outcome, Is.EqualTo(OutpostOutcome.ObjectiveAchieved), "a safe return is not a defeat");
        Assert.That(h.Now.Objectives.Single(o => o.Role == ObjectiveRole.Safety).Met);
    }

    [Test]
    public void LowSuppliesMakeTheMarginInsufficientRecordReserveUseAndEndBeforeAnyClinicalClaim()
    {
        ExpeditionScenario s = ExpeditionFixtures.Scenario(false);
        // Synthetic tiny oxygen: capacity 0.05 kg, reserve 0.02 kg.
        var tiny = new SuitModelConfiguration(new[]
        {
            new SuitServiceModel(SuitService.Oxygen, 0.05, 0.02, 0.0016, 0.0022, 0.0030),
            s.Eva.Model.Get(SuitService.Battery), s.Eva.Model.Get(SuitService.Feedwater)
        }, "SINTÉTICO de prueba: O2 mínimo");
        var eva = new EvaConfiguration(tiny, new[] { new SuitDefinition("suit-a", new SuitLevels(0.01, 2600, 0)),
            new SuitDefinition("suit-b", new SuitLevels(0.01, 2600, 0)) }, s.Eva.Supply);
        var h = new H(H.With(s, eva: eva));
        PlayerId ana = h.Join();
        h.Start(ana);
        h.PrepareSuit(ana, true);
        h.GoOut(ana, H.ChamberA);
        h.Walk(ana, H.AtRegolith);
        Assert.That(h.Crew(ana).Margin.Status, Is.EqualTo(MarginStatus.Insufficient));
        Assert.That(h.Crew(ana).Margin.LimitingService, Is.EqualTo(SuitService.Oxygen));
        h.Tick(30);
        Assert.That(h.Now.Incidents.Count(i => i.Kind == IncidentKind.ReserveUsed && i.Service == SuitService.Oxygen), Is.EqualTo(1),
            "using the reserve is recorded once");
        Assert.That(h.Now.Outcome, Is.EqualTo(ExpeditionOutcome.SuitServiceDepleted));
        Assert.That(h.Now.Outpost.State, Is.EqualTo(SessionState.Interrupted));
    }
}
