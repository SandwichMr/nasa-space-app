using NUnit.Framework;
using Trainer.Simulation.Eva;

/// <summary>D1 phase 3: the return margin is limited by the first service that runs short, with the reserve kept.</summary>
public sealed class Phase03ReturnMarginTests
{
    // Binary-exact synthetic numbers so the limit case is exactly zero.
    private static readonly SuitModelConfiguration Model = new SuitModelConfiguration(new[]
    {
        new SuitServiceModel(SuitService.Oxygen, 1.0, 0.25, 0.0625, 0.125, 0.25),
        new SuitServiceModel(SuitService.Battery, 1024, 128, 8, 16, 32),
        new SuitServiceModel(SuitService.Feedwater, 2.0, 0.5, 0.0625, 0.125, 0.25)
    }, "SINTÉTICO de prueba");

    [Test]
    public void LimitingServiceIsTheSmallestMarginNotTheAverage()
    {
        // Walk 4 s, wait 0, ingress 4 s.
        var plan = new ReturnPlan(8, 4, 0, 4, true);
        var levels = new SuitLevels(1.0, 1024, 2.0);
        ReturnMarginEstimate m = ReturnMarginCalculator.Estimate(Model, levels, SuitActivity.Work, 0, plan, 30);
        // O2: usable 0.75, needed 0.125*4 + 0.0625*4 = 0.75 -> margin 0.
        // Battery: usable 896, needed 16*4 + 8*4 = 96 -> 800/32 = 25 s. Water: usable 1.5, needed 0.75 -> 0.75/0.25 = 3 s.
        Assert.That(m.Services[0].MarginSeconds, Is.EqualTo(0));
        Assert.That(m.Services[1].MarginSeconds, Is.EqualTo(25));
        Assert.That(m.Services[2].MarginSeconds, Is.EqualTo(3));
        Assert.That(m.LimitingService, Is.EqualTo(SuitService.Oxygen));
        Assert.That(m.MarginSeconds, Is.EqualTo(0), "the smallest, not (0 + 25 + 3) / 3");
        Assert.That(m.Status, Is.EqualTo(MarginStatus.Sufficient), "a margin of exactly zero is the inclusive limit");
        Assert.That(m.ComputedAtSeconds, Is.EqualTo(30));
        Assert.That(m.Plan, Is.SameAs(plan));
    }

    [Test]
    public void BelowTheLimitIsInsufficientBecauseTheReserveIsNotUsable()
    {
        var plan = new ReturnPlan(8, 4, 0, 4, true);
        // 1/64 kg less O2 than the limit case: still above zero but inside what the reserve protects.
        ReturnMarginEstimate m = ReturnMarginCalculator.Estimate(Model, new SuitLevels(1.0 - 1.0 / 64, 1024, 2.0), SuitActivity.Rest, 0, plan, 0);
        Assert.That(m.Status, Is.EqualTo(MarginStatus.Insufficient));
        Assert.That(m.MarginSeconds, Is.LessThan(0));
        Assert.That(m.Services[0].Usable, Is.EqualTo(0.75 - 1.0 / 64), "usable = level - reserve");
    }

    [Test]
    public void ToolLoadChangesOnlyTheBatteryRateAndCanMakeItLimiting()
    {
        // Ingress 4 s at Work: O2 (0.75 - 0.25) / 0.25 = 2 s; battery (128 - 32) / 32 = 3 s, or / 64 = 1.5 s with a 32 W tool.
        var plan = new ReturnPlan(0, 0, 0, 4, true);
        var levels = new SuitLevels(1.0, 256, 2.0);
        ReturnMarginEstimate without = ReturnMarginCalculator.Estimate(Model, levels, SuitActivity.Work, 0, plan, 0);
        ReturnMarginEstimate with = ReturnMarginCalculator.Estimate(Model, levels, SuitActivity.Work, 32, plan, 0);
        Assert.That(without.LimitingService, Is.EqualTo(SuitService.Oxygen));
        Assert.That(with.Services[0].MarginSeconds, Is.EqualTo(without.Services[0].MarginSeconds));
        Assert.That(with.Services[2].MarginSeconds, Is.EqualTo(without.Services[2].MarginSeconds));
        Assert.That(with.Services[1].CurrentRate, Is.EqualTo(64));
        Assert.That(with.LimitingService, Is.EqualTo(SuitService.Battery));
    }

    [Test]
    public void LongerRoutesWaitingAndIngressAllReduceTheMargin()
    {
        var levels = new SuitLevels(1.0, 1024, 2.0);
        double near = ReturnMarginCalculator.Estimate(Model, levels, SuitActivity.Walk, 0, new ReturnPlan(2, 1, 0, 4, true), 0).MarginSeconds;
        double far = ReturnMarginCalculator.Estimate(Model, levels, SuitActivity.Walk, 0, new ReturnPlan(20, 10, 0, 4, true), 0).MarginSeconds;
        double waiting = ReturnMarginCalculator.Estimate(Model, levels, SuitActivity.Walk, 0, new ReturnPlan(2, 1, 8, 4, true), 0).MarginSeconds;
        Assert.That(far, Is.LessThan(near));
        Assert.That(waiting, Is.LessThan(near), "the airlock wait counts");
    }

    [Test]
    public void AnUnreachableReturnIsNeverReportedAsSufficient()
    {
        ReturnMarginEstimate m = ReturnMarginCalculator.Estimate(Model, new SuitLevels(1.0, 1024, 2.0), SuitActivity.Rest, 0,
            new ReturnPlan(2, 1, 0, 4, false), 0);
        Assert.That(m.Status, Is.EqualTo(MarginStatus.ReturnUnavailable));
        Assert.That(m.MarginSeconds, Is.GreaterThan(0), "plenty of suit services do not make an unavailable airlock usable");
    }
}
