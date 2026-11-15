using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.Eva;
using H = Phase03AuthorityHarness;

/// <summary>D2 phase 3: the educational checklist is enforced in order and each preparation cost is applied once.</summary>
public sealed class Phase03AuthorityChecklistTests
{
    [Test]
    public void JoinSpawnsInsideWithoutSuitAndTasksWaitForTheMission()
    {
        var h = new H();
        PlayerId ana = h.Join();
        Assert.That(h.Crew(ana).Zone, Is.EqualTo(CrewZone.Interior));
        Assert.That(h.Crew(ana).SuitIndex, Is.EqualTo(-1));
        Assert.That(h.Now.Suits[0].Status, Is.EqualTo(SuitStatus.Stowed));
        h.Move(ana, MoveDirection.FromAxes(-1, 1)); // recorded in Ready, but positions only change while Running
        h.Tick(2);
        Assert.That(h.Position(ana), Is.EqualTo(h.Scenario.Layout.SpawnPoints[0]));
        Assert.That(h.StartTask(ana, ExpeditionFixtures.RackA, ExpeditionFixtures.DonA).Reason, Is.EqualTo(OutpostReasons.InvalidState));
    }

    [Test]
    public void ChecklistOrderAndRackOwnershipAreEnforced()
    {
        var h = new H();
        PlayerId ana = h.Join(), leo = h.Join();
        h.Start(ana);
        h.Walk(ana, H.AtCharger);
        Assert.That(h.StartTask(ana, ExpeditionFixtures.Charger, ExpeditionFixtures.ChargeSuit).Reason, Is.EqualTo(ExpeditionReasons.SuitRequired));
        h.Walk(ana, H.AtRackA);
        h.DoTask(ana, ExpeditionFixtures.RackA, ExpeditionFixtures.DonA);
        Assert.That(h.Now.Suits[0].Status, Is.EqualTo(SuitStatus.Worn));
        Assert.That(h.Now.Suits[0].Wearer, Is.EqualTo(ana));
        Assert.That(h.Crew(ana).SuitIndex, Is.EqualTo(0));
        h.Walk(ana, H.AtRackB);
        Assert.That(h.StartTask(ana, ExpeditionFixtures.RackB, ExpeditionFixtures.DonB).Reason, Is.EqualTo(ExpeditionReasons.AlreadySuited));
        h.Walk(leo, H.AtRackA);
        Assert.That(h.StartTask(leo, ExpeditionFixtures.RackA, ExpeditionFixtures.DonA).Reason, Is.EqualTo(ExpeditionReasons.SuitNotOnRack));
        h.Walk(ana, H.AtSuitCheck);
        Assert.That(h.StartTask(ana, ExpeditionFixtures.SuitCheck, ExpeditionFixtures.CheckSuit).Reason,
            Is.EqualTo(ExpeditionReasons.ChecklistOutOfOrder), "seal check after charge and fill");
        h.Walk(ana, H.AtCharger);
        h.DoTask(ana, ExpeditionFixtures.Charger, ExpeditionFixtures.ChargeSuit);
        Assert.That(h.StartTask(ana, ExpeditionFixtures.Charger, ExpeditionFixtures.ChargeSuit).Reason, Is.EqualTo(ExpeditionReasons.ChecklistStepDone));
    }

    [Test]
    public void ChargingSpendsBaseEnergyAndStoresItOnceWhileCancellingRefundsNothing()
    {
        var h = new H();
        PlayerId ana = h.Join(), leo = h.Join();
        h.Start(ana);
        h.Walk(ana, H.AtRackA);
        h.DoTask(ana, ExpeditionFixtures.RackA, ExpeditionFixtures.DonA);
        h.Walk(leo, H.AtRackB);
        h.DoTask(leo, ExpeditionFixtures.RackB, ExpeditionFixtures.DonB);
        h.Walk(ana, H.AtCharger);

        // Cancel half-way: the base energy already spent stays spent and the suit gains nothing.
        double batteryBefore = h.Now.Suits[0].Reading.Levels.BatteryJoules;
        double drawnBefore = h.Base.Snapshot.Power.EnergyJoules;
        Assert.That(h.StartTask(ana, ExpeditionFixtures.Charger, ExpeditionFixtures.ChargeSuit).Accepted);
        h.Tick(6);
        Assert.That(h.Cancel(ana, ExpeditionFixtures.Charger).Accepted);
        Assert.That(h.Now.Suits[0].Reading.Levels.BatteryJoules, Is.EqualTo(batteryBefore), "no partial charge");
        Assert.That(h.Base.Snapshot.Power.EnergyJoules, Is.LessThan(drawnBefore - 6 * (200 - 20) * 0.9), "energy drawn while charging is not refunded");
        Assert.That(h.Now.Suits[0].Completed & ChecklistSteps.ChargeBattery, Is.EqualTo(ChecklistSteps.None));

        // Complete it: the suit stores exactly 200 W x 12 s (efficiency 1) up to capacity, once.
        h.DoTask(ana, ExpeditionFixtures.Charger, ExpeditionFixtures.ChargeSuit);
        SuitModelConfiguration model = h.Scenario.Eva.Model;
        double expected = System.Math.Min(model.Get(SuitService.Battery).Capacity, batteryBefore + 200 * 12);
        Assert.That(h.Now.Suits[0].Reading.Levels.BatteryJoules, Is.EqualTo(expected).Within(1e-6));
        Assert.That(h.Eva.Ledger.BatteryStoredJoules + h.Eva.Ledger.ChargeNotStoredJoules, Is.EqualTo(2400).Within(1e-6),
            "delivered = stored + not stored");
        Assert.That(h.Now.Suits[1].Reading.Levels.BatteryJoules, Is.EqualTo(ExpeditionFixtures.RackLevels().BatteryJoules), "the other suit is untouched");
    }

    [Test]
    public void FillingMovesBaseWaterIntoTheSuitAndOxygenFromTheRechargeSupplyOnce()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.Walk(ana, H.AtRackA);
        h.DoTask(ana, ExpeditionFixtures.RackA, ExpeditionFixtures.DonA);
        h.Walk(ana, H.AtConsumables);
        double potable = h.Base.Snapshot.LifeSupport.Water.PotableKilograms;
        double outflow = h.Base.Ledger.TransferBoundaryOutflowKilograms;
        double recharge = h.Now.Supply.OxygenRechargeKilograms;
        SuitLevels rack = ExpeditionFixtures.RackLevels();
        SuitModelConfiguration model = h.Scenario.Eva.Model;
        double water = model.Get(SuitService.Feedwater).Capacity - rack.FeedwaterKilograms;
        double oxygen = model.Get(SuitService.Oxygen).Capacity - rack.OxygenKilograms;
        h.DoTask(ana, ExpeditionFixtures.Consumables, ExpeditionFixtures.FillSuit);

        SuitLevels filled = h.Now.Suits[0].Reading.Levels;
        Assert.That(filled.FeedwaterKilograms, Is.EqualTo(model.Get(SuitService.Feedwater).Capacity).Within(1e-9));
        Assert.That(filled.OxygenKilograms, Is.EqualTo(model.Get(SuitService.Oxygen).Capacity).Within(1e-9));
        // Crew drinking also lowers potable water a little: compare through the base ledger's boundary outflow.
        Assert.That(h.Base.Ledger.TransferBoundaryOutflowKilograms - outflow, Is.EqualTo(water).Within(1e-9),
            "the base water leaves its boundary exactly once, into the suit");
        Assert.That(h.Base.Snapshot.LifeSupport.Water.PotableKilograms, Is.LessThan(potable - water + 1e-6));
        Assert.That(h.Now.Supply.OxygenRechargeKilograms, Is.EqualTo(recharge - oxygen).Within(1e-9), "O2 comes from the EVA recharge supply");
        Assert.That(h.Eva.Ledger.FeedwaterFilledKilograms, Is.EqualTo(water).Within(1e-9));
    }

    [Test]
    public void AFullChecklistMakesTheSuitReadyAndBaseWorkNeedsNoSeal()
    {
        var h = new H();
        PlayerId ana = h.Join();
        h.Start(ana);
        h.PrepareSuit(ana, true);
        Assert.That(h.Now.Suits[0].Completed, Is.EqualTo(ChecklistSteps.All));
        h.Walk(ana, H.AtWater);
        Assert.That(h.StartTask(ana, ExpeditionFixtures.WaterRecycler, ExpeditionFixtures.TreatWater).Accepted,
            "a worn, unsealed suit does not prevent base work");
    }
}
