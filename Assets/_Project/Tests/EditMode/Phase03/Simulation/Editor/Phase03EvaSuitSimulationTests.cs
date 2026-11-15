using System;
using NUnit.Framework;
using Trainer.Simulation.Eva;

/// <summary>D1 phase 3: suit services move only for listed (sealed) suits, exactly once per transfer id.</summary>
public sealed class Phase03EvaSuitSimulationTests
{
    private const double Tol = 1e-9;
    private SuitModelConfiguration model;
    private EvaConfiguration configuration;
    private EvaSuitSimulation eva;

    [SetUp]
    public void SetUp()
    {
        // Synthetic test model: O2 1.0 kg (reserve 0.2), battery 1000 J (reserve 100), water 0.5 kg (reserve 0.1).
        model = new SuitModelConfiguration(new[]
        {
            new SuitServiceModel(SuitService.Oxygen, 1.0, 0.2, 0.001, 0.002, 0.004),
            new SuitServiceModel(SuitService.Battery, 1000, 100, 10, 12, 15),
            new SuitServiceModel(SuitService.Feedwater, 0.5, 0.1, 0.001, 0.003, 0.005)
        }, "SINTÉTICO de prueba");
        configuration = new EvaConfiguration(model, new[]
        {
            new SuitDefinition("suit-a", new SuitLevels(0.8, 600, 0.4)),
            new SuitDefinition("suit-b", new SuitLevels(0.3, 900, 0))
        }, new EvaSupplyConfiguration(1.0, 2.0, 1.0, 1.0));
        eva = new EvaSuitSimulation();
        eva.Reset(configuration);
    }

    private static EvaDemand Demand(params SuitDemand[] suits) => new EvaDemand(suits);

    [Test]
    public void ResetPublishesInitialLevelsAndNobodyOnSuitSupply()
    {
        Assert.That(eva.Snapshot.SimulatedTimeSeconds, Is.EqualTo(0));
        Assert.That(eva.Snapshot.Suits[0].Levels, Is.EqualTo(new SuitLevels(0.8, 600, 0.4)));
        Assert.That(eva.Snapshot.Suits[0].OnInternalSupply, Is.False);
        Assert.That(eva.Snapshot.Suits[1].BelowReserve, Is.EquivalentTo(new[] { SuitService.Feedwater }));
        Assert.That(eva.Snapshot.Suits[1].Depleted, Is.EquivalentTo(new[] { SuitService.Feedwater }));
        Assert.That(eva.Snapshot.Supply.OxygenRechargeKilograms, Is.EqualTo(1.0));
        Assert.Throws<InvalidOperationException>(() => new EvaSuitSimulation().Advance(1, EvaDemand.None));
    }

    [Test]
    public void OnlyListedSuitsConsumeAtTheirActivityRateAndToolsLoadOnlyTheBattery()
    {
        eva.Advance(10, Demand(new SuitDemand(0, SuitActivity.Work, 5)));
        SuitLevels a = eva.Snapshot.Suits[0].Levels;
        Assert.That(a.OxygenKilograms, Is.EqualTo(0.8 - 0.04).Within(Tol));
        Assert.That(a.BatteryJoules, Is.EqualTo(600 - (15 + 5) * 10).Within(Tol), "tool watts add to the battery");
        Assert.That(a.FeedwaterKilograms, Is.EqualTo(0.4 - 0.05).Within(Tol), "tool watts never touch oxygen or water");
        Assert.That(eva.Snapshot.Suits[1].Levels, Is.EqualTo(configuration.Suits[1].Initial), "an unlisted suit never consumes");
        Assert.That(eva.Snapshot.Suits[0].OnInternalSupply, Is.True);
        Assert.That(eva.Snapshot.Suits[1].OnInternalSupply, Is.False);
        eva.Advance(1, EvaDemand.None);
        Assert.That(eva.Snapshot.Suits[0].OnInternalSupply, Is.False);
    }

    [Test]
    public void ConsumptionDoesNotDependOnStepSize()
    {
        var other = new EvaSuitSimulation();
        other.Reset(configuration);
        EvaDemand walk = Demand(new SuitDemand(0, SuitActivity.Walk, 0), new SuitDemand(1, SuitActivity.Rest, 0));
        eva.Advance(40, walk);
        for (int i = 0; i < 160; i++) other.Advance(0.25, walk);
        for (int s = 0; s < 2; s++)
            foreach (SuitService service in new[] { SuitService.Oxygen, SuitService.Battery, SuitService.Feedwater })
                Assert.That(other.Snapshot.Suits[s].Levels.Get(service), Is.EqualTo(eva.Snapshot.Suits[s].Levels.Get(service)).Within(1e-9),
                    $"suit {s} {service}");
        Assert.That(other.Snapshot.SimulatedTimeSeconds, Is.EqualTo(40).Within(1e-9));
    }

    [Test]
    public void ServicesStopAtZeroAndUnservedDemandIsReportedNotTakenElsewhere()
    {
        // Suit b: O2 0.3 kg at rest 0.001 kg/s lasts 300 s; ask for 400 s.
        eva.Advance(400, Demand(new SuitDemand(1, SuitActivity.Rest, 0)));
        SuitReading b = eva.Snapshot.Suits[1];
        Assert.That(b.Levels.OxygenKilograms, Is.EqualTo(0));
        Assert.That(b.Depleted, Does.Contain(SuitService.Oxygen));
        Assert.That(b.IsDepleted, Is.True);
        Assert.That(eva.Ledger.UnservedOxygenKilograms, Is.EqualTo(0.1).Within(Tol));
        Assert.That(eva.Snapshot.Suits[0].Levels.OxygenKilograms, Is.EqualTo(0.8), "no O2 transfer between suits exists");
        Assert.That(eva.Snapshot.Supply.OxygenRechargeKilograms, Is.EqualTo(1.0), "nor from the recharge supply");
    }

    [Test]
    public void NextServiceEventIsTheReserveCrossingThenTheZeroCrossing()
    {
        EvaDemand demand = Demand(new SuitDemand(0, SuitActivity.Work, 0));
        // O2: (0.8 - 0.2) / 0.004 = 150 s; battery (600-100)/15 = 33.3 s; water (0.4-0.1)/0.005 = 60 s.
        Assert.That(eva.SecondsUntilServiceEvent(demand), Is.EqualTo(500.0 / 15).Within(1e-9));
        eva.Advance(500.0 / 15, demand);
        Assert.That(eva.Snapshot.Suits[0].BelowReserve, Is.Empty.Or.EquivalentTo(new[] { SuitService.Battery }));
        // Now the battery sits at its reserve: its next event is zero (100 / 15), before water (60 - 33.3).
        Assert.That(eva.SecondsUntilServiceEvent(demand), Is.EqualTo(100.0 / 15).Within(1e-6));
        Assert.That(eva.SecondsUntilServiceEvent(EvaDemand.None), Is.EqualTo(double.PositiveInfinity));
    }

    [Test]
    public void FillsAreAllOrNothingAndIdempotent()
    {
        Guid id = Guid.NewGuid();
        Assert.That(eva.Apply(new EvaTransfer(Guid.NewGuid(), EvaTransferKind.FillOxygen, 0, 0.3)), Is.EqualTo(EvaTransferStatus.ExceedsCapacity),
            "0.8 + 0.3 > 1.0");
        Assert.That(eva.Snapshot.Suits[0].Levels.OxygenKilograms, Is.EqualTo(0.8));
        Assert.That(eva.Snapshot.Supply.OxygenRechargeKilograms, Is.EqualTo(1.0), "nothing moved");
        var fill = new EvaTransfer(id, EvaTransferKind.FillOxygen, 1, 0.7);
        Assert.That(eva.Apply(fill), Is.EqualTo(EvaTransferStatus.Applied));
        Assert.That(eva.Snapshot.Suits[1].Levels.OxygenKilograms, Is.EqualTo(1.0).Within(Tol));
        Assert.That(eva.Snapshot.Supply.OxygenRechargeKilograms, Is.EqualTo(0.3).Within(Tol));
        Assert.That(eva.Apply(fill), Is.EqualTo(EvaTransferStatus.AlreadyApplied));
        Assert.That(eva.Apply(new EvaTransfer(id, EvaTransferKind.FillOxygen, 1, 0.1)), Is.EqualTo(EvaTransferStatus.IdConflict));
        Assert.That(eva.Snapshot.Supply.OxygenRechargeKilograms, Is.EqualTo(0.3).Within(Tol), "a replay never moves again");
        Assert.That(eva.Apply(new EvaTransfer(Guid.NewGuid(), EvaTransferKind.FillOxygen, 0, 0.2)), Is.EqualTo(EvaTransferStatus.Applied));
        Assert.That(eva.Apply(new EvaTransfer(Guid.NewGuid(), EvaTransferKind.FillOxygen, 0, 0.0001)), Is.EqualTo(EvaTransferStatus.ExceedsCapacity));
        Assert.That(eva.Apply(new EvaTransfer(Guid.NewGuid(), EvaTransferKind.UseMakeupGas, -1, 1.5)), Is.EqualTo(EvaTransferStatus.InsufficientStock));
        Assert.That(eva.Apply(new EvaTransfer(Guid.NewGuid(), EvaTransferKind.FillFeedwater, 1, 0.5)), Is.EqualTo(EvaTransferStatus.Applied));
        Assert.That(eva.Apply(new EvaTransfer(Guid.NewGuid(), EvaTransferKind.FillFeedwater, 3, 0.1)), Is.EqualTo(EvaTransferStatus.UnknownSuit));
    }

    [Test]
    public void ChargingStoresWhatFitsAndReportsTheRestAsNotStored()
    {
        Assert.That(eva.Apply(new EvaTransfer(Guid.NewGuid(), EvaTransferKind.ChargeBattery, 1, 250)), Is.EqualTo(EvaTransferStatus.Applied));
        Assert.That(eva.Snapshot.Suits[1].Levels.BatteryJoules, Is.EqualTo(1000));
        Assert.That(eva.Ledger.BatteryStoredJoules, Is.EqualTo(100));
        Assert.That(eva.Ledger.ChargeNotStoredJoules, Is.EqualTo(150), "delivered = stored + not stored: no energy is created or kept");
    }

    [Test]
    public void LedgerClosesEveryServicePerSuit()
    {
        EvaDemand mix = Demand(new SuitDemand(0, SuitActivity.Walk, 3), new SuitDemand(1, SuitActivity.Work, 0));
        eva.Advance(20, mix);
        eva.Apply(new EvaTransfer(Guid.NewGuid(), EvaTransferKind.FillFeedwater, 1, 0.2));
        eva.Apply(new EvaTransfer(Guid.NewGuid(), EvaTransferKind.FillOxygen, 1, 0.5));
        eva.Apply(new EvaTransfer(Guid.NewGuid(), EvaTransferKind.ChargeBattery, 0, 50));
        eva.Advance(35, mix);
        EvaLedger ledger = eva.Ledger;
        double o2Now = eva.Snapshot.Suits[0].Levels.OxygenKilograms + eva.Snapshot.Suits[1].Levels.OxygenKilograms;
        double o2Start = 0.8 + 0.3;
        Assert.That(o2Now, Is.EqualTo(o2Start + ledger.OxygenFilledKilograms - ledger.OxygenConsumedKilograms).Within(Tol));
        double batteryNow = eva.Snapshot.Suits[0].Levels.BatteryJoules + eva.Snapshot.Suits[1].Levels.BatteryJoules;
        Assert.That(batteryNow, Is.EqualTo(600 + 900 + ledger.BatteryStoredJoules - ledger.BatteryConsumedJoules).Within(1e-6));
        double waterNow = eva.Snapshot.Suits[0].Levels.FeedwaterKilograms + eva.Snapshot.Suits[1].Levels.FeedwaterKilograms;
        Assert.That(waterNow, Is.EqualTo(0.4 + ledger.FeedwaterFilledKilograms - ledger.FeedwaterConsumedKilograms).Within(Tol));
        Assert.That(eva.Snapshot.Supply.OxygenRechargeKilograms, Is.EqualTo(1.0 - ledger.OxygenFilledKilograms).Within(Tol));
    }

    [Test]
    public void InvalidInputThrowsWithoutChangingStateAndResetForgetsIds()
    {
        EvaSnapshot before = eva.Snapshot;
        Assert.Throws<ArgumentOutOfRangeException>(() => eva.Advance(double.NaN, EvaDemand.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => eva.Advance(-1, EvaDemand.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => eva.Advance(1, Demand(new SuitDemand(3, SuitActivity.Rest, 0))));
        Assert.That(eva.Snapshot, Is.SameAs(before));
        eva.Advance(0, Demand(new SuitDemand(0, SuitActivity.Work, 0)));
        Assert.That(eva.Snapshot, Is.SameAs(before), "zero seconds is a no-op");
        var fill = new EvaTransfer(Guid.NewGuid(), EvaTransferKind.FillFeedwater, 1, 0.1);
        Assert.That(eva.Apply(fill), Is.EqualTo(EvaTransferStatus.Applied));
        eva.Reset(configuration);
        Assert.That(eva.Apply(fill), Is.EqualTo(EvaTransferStatus.Applied), "Reset clears the applied ids");
        Assert.That(eva.Snapshot.Suits[1].Levels.FeedwaterKilograms, Is.EqualTo(0.1).Within(Tol));
    }
}
