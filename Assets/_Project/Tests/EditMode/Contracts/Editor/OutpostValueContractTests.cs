using System;
using System.Collections.Generic;
using NUnit.Framework;
using Trainer.Application.Development;
using Trainer.Application.Outpost;
using Trainer.Simulation;
using Trainer.Simulation.LifeSupport;

/// <summary>Phase 2 common base: guarantees of the value types and configuration constructors. These test the
/// types only; authority behaviour (D1/D2/D3) is not implemented by the base and is not tested here.</summary>
public sealed class OutpostValueContractTests
{
    [Test] public void StableKeysAndIdsRejectInvalidValues()
    {
        foreach (string good in new[] { "a", "water-processor", "x9", new string('k', OutpostLimits.MaxKeyLength) })
            Assert.That(StableKey.IsValid(good), Is.True, good);
        foreach (string bad in new[] { null, "", "Water", "9a", "-a", "a b", "a_b", "ñ", new string('k', OutpostLimits.MaxKeyLength + 1) })
            Assert.That(StableKey.IsValid(bad), Is.False, bad ?? "null");
        Assert.Throws<ArgumentException>(() => new StationId("Water"));
        Assert.Throws<ArgumentException>(() => new TaskTypeId(""));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerId(-3));

        Assert.That(PlayerId.None.IsValid, Is.False);
        Assert.That(default(StationId) == StationId.None, Is.True);
        Assert.That(StationId.None.IsValid, Is.False);
        Assert.That(new StationId("galley") == new StationId("galley"), Is.True);
        Assert.That(new TaskTypeId("a") != new TaskTypeId("b"), Is.True);
        Assert.That(new PlayerId(2).CompareTo(new PlayerId(5)), Is.LessThan(0));
    }

    [Test] public void PositionsAndRectanglesRejectNonFiniteOrEmptyValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BasePosition(double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BasePosition(0, double.PositiveInfinity));
        Assert.Throws<ArgumentException>(() => new BaseRect(1, 0, 1, 2));
        Assert.Throws<ArgumentException>(() => new BaseRect(0, 3, 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BaseRect(0, 0, double.NaN, 1));

        var rect = new BaseRect(0, 0, 4, 2);
        Assert.That(rect.Contains(new BasePosition(4, 2)), Is.True, "Edges belong to the rectangle.");
        Assert.That(rect.Contains(new BasePosition(4.0001, 1)), Is.False);
        Assert.That(rect.DistanceTo(new BasePosition(2, 1)), Is.EqualTo(0));
        Assert.That(rect.DistanceTo(new BasePosition(7, 6)), Is.EqualTo(5).Within(1e-12));
        Assert.That(new BasePosition(0, 0).DistanceTo(new BasePosition(3, 4)), Is.EqualTo(5).Within(1e-12));
    }

    [Test] public void MoveDirectionNormalisesDiagonalsAndRejectsInvalidAxes()
    {
        MoveDirection diagonal = MoveDirection.FromAxes(1, 1);
        Assert.That(diagonal.Magnitude, Is.EqualTo(1).Within(1e-12), "A diagonal is not faster than a straight line.");
        Assert.That(diagonal.X, Is.EqualTo(Math.Sqrt(0.5)).Within(1e-12));
        Assert.That(MoveDirection.FromAxes(-1, 0), Is.EqualTo(new MoveDirection(-1, 0)));
        Assert.That(MoveDirection.FromAxes(0.3, 0.4), Is.EqualTo(new MoveDirection(0.3, 0.4)), "Analog magnitudes below 1 are kept.");
        Assert.That(MoveDirection.FromAxes(1e300, -1e300).Magnitude, Is.EqualTo(1).Within(1e-12), "Huge finite axes do not overflow.");
        Assert.That(MoveDirection.FromAxes(0, 0).IsZero, Is.True);

        Assert.Throws<ArgumentOutOfRangeException>(() => MoveDirection.FromAxes(double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MoveDirection.FromAxes(0, double.NegativeInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MoveDirection(1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MoveDirection(1.0001, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MoveDirection(double.NaN, 0));
        Assert.That(MoveDirection.TryCreate(0.8, 0.8, out MoveDirection rejected), Is.False);
        Assert.That(rejected.IsZero, Is.True);
        Assert.That(MoveDirection.TryCreate(double.PositiveInfinity, 0, out _), Is.False);
        Assert.That(MoveDirection.TryCreate(diagonal.X, diagonal.Y, out MoveDirection decoded), Is.True);
        Assert.That(decoded, Is.EqualTo(diagonal));
    }

    [Test] public void LayoutCopiesItsInputsAndValidatesPlacements()
    {
        var bounds = new BaseRect(0, 0, 10, 10);
        var obstacles = new List<BaseRect> { new BaseRect(4, 4, 6, 6) };
        var stations = new List<StationPlacement> { new StationPlacement(new StationId("s1"), new BasePosition(1, 9), 1) };
        var spawns = new List<BasePosition> { new BasePosition(1, 1) };
        var layout = new OutpostLayout(bounds, obstacles, stations, spawns);

        obstacles.Clear();
        stations.Add(new StationPlacement(new StationId("s2"), new BasePosition(2, 2), 1));
        spawns[0] = new BasePosition(9, 9);
        Assert.That(layout.Obstacles.Count, Is.EqualTo(1));
        Assert.That(layout.Stations.Count, Is.EqualTo(1));
        Assert.That(layout.SpawnPoints[0], Is.EqualTo(new BasePosition(1, 1)));
        Assert.That(layout.MaxCrew, Is.EqualTo(1));
        Assert.Throws<NotSupportedException>(() => ((IList<BaseRect>)layout.Obstacles).Add(bounds));

        var one = new[] { new StationPlacement(new StationId("s1"), new BasePosition(1, 9), 1) };
        var spawn = new[] { new BasePosition(1, 1) };
        Assert.Throws<ArgumentException>(() => new OutpostLayout(bounds, new[] { new BaseRect(8, 8, 11, 9) }, one, spawn),
            "Obstacle outside bounds.");
        Assert.Throws<ArgumentException>(() => new OutpostLayout(bounds, new BaseRect[0], new[] { one[0], one[0] }, spawn),
            "Duplicate station.");
        Assert.Throws<ArgumentException>(() => new OutpostLayout(bounds, new BaseRect[0],
            new[] { new StationPlacement(new StationId("far"), new BasePosition(12, 1), 1) }, spawn), "Station outside bounds.");
        Assert.Throws<ArgumentException>(() => new OutpostLayout(bounds, new[] { new BaseRect(0.5, 0.5, 2, 2) }, one, spawn),
            "Spawn inside an obstacle.");
        Assert.Throws<ArgumentException>(() => new OutpostLayout(bounds, new BaseRect[0], one, new BasePosition[0]));
        Assert.Throws<ArgumentException>(() => new OutpostLayout(bounds, new BaseRect[0], new StationPlacement[0], spawn));
        var tooMany = new BasePosition[OutpostLimits.MaxCrew + 1];
        for (int i = 0; i < tooMany.Length; i++) tooMany[i] = new BasePosition(1 + i * 0.5, 1);
        Assert.Throws<ArgumentException>(() => new OutpostLayout(bounds, new BaseRect[0], one, tooMany));
        Assert.Throws<ArgumentNullException>(() => new OutpostLayout(bounds, null, one, spawn));
    }

    [Test] public void AdmissibilityIsTheSingleCollisionRule()
    {
        OutpostLayout fixture = OutpostFixtures.Scenario().Layout;
        Assert.That(fixture.IsAdmissible(new BasePosition(3, 3), 0.35), Is.True);
        Assert.That(fixture.IsAdmissible(new BasePosition(8, 5), 0.35), Is.True, "Inside the doorway.");
        Assert.That(fixture.IsAdmissible(new BasePosition(8, 2), 0.35), Is.False, "Inside a wall.");

        // Binary-exact coordinates so the boundary cases are not decided by rounding.
        var layout = new OutpostLayout(new BaseRect(0, 0, 16, 10),
            new[] { new BaseRect(7.75, 0, 8.25, 4), new BaseRect(7.75, 6, 8.25, 10) },
            new[] { new StationPlacement(new StationId("s"), new BasePosition(2, 2), 1) },
            new[] { new BasePosition(1, 1) });
        const double r = 0.5;
        Assert.That(layout.IsAdmissible(new BasePosition(7.25, 2), r), Is.True, "Touching a wall is admissible.");
        Assert.That(layout.IsAdmissible(new BasePosition(7.25 + 1.0 / 1024, 2), r), Is.False, "Overlapping a wall is not.");
        Assert.That(layout.IsAdmissible(new BasePosition(8, 4.5), r), Is.True, "Doorway, touching the wall end.");
        Assert.That(layout.IsAdmissible(new BasePosition(8, 4.5 - 1.0 / 1024), r), Is.False);
        Assert.That(layout.IsAdmissible(new BasePosition(8.625, 4.375), r), Is.True,
            "Disc against rectangle: 0.53 m from the wall corner is free (an inflated square would say blocked).");
        Assert.That(layout.IsAdmissible(new BasePosition(8.5, 4.25), r), Is.False, "0.35 m from the corner overlaps.");
        Assert.That(layout.IsAdmissible(new BasePosition(r, r), r), Is.True, "Touching the outer bounds.");
        Assert.That(layout.IsAdmissible(new BasePosition(r - 1.0 / 1024, 5), r), Is.False, "Crossing the outer bounds.");
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.IsAdmissible(new BasePosition(3, 3), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.IsAdmissible(new BasePosition(3, 3), double.NaN));
    }

    [Test] public void StationReachIsInclusiveOnTheConfirmedPosition()
    {
        var station = new StationPlacement(new StationId("s"), new BasePosition(0, 0), 1.2);
        Assert.That(station.IsWithinReach(new BasePosition(1.2, 0)), Is.True);
        Assert.That(station.IsWithinReach(new BasePosition(1.2000001, 0)), Is.False);
        Assert.Throws<ArgumentOutOfRangeException>(() => new StationPlacement(new StationId("s"), new BasePosition(0, 0), 0));
        Assert.Throws<ArgumentException>(() => new StationPlacement(StationId.None, new BasePosition(0, 0), 1));
    }

    [Test] public void ScenarioCrossChecksTasksStationsAndSpawns()
    {
        OutpostScenario fixture = OutpostFixtures.Scenario();
        Assert.That(fixture.Provenance, Does.Contain("No representan hardware"));
        Assert.That(fixture.TryGetTask(OutpostFixtures.TreatWater, out TaskDefinition treat), Is.True);
        Assert.That(treat.Station, Is.EqualTo(OutpostFixtures.WaterProcessor));
        Assert.That(fixture.TryGetTask(new TaskTypeId("missing"), out _), Is.False);

        var good = new TaskDefinition(new TaskTypeId("t"), OutpostFixtures.AirMonitor, TaskCategory.Support, 5, 0, ResourceEffect.None);
        var unknownStation = new TaskDefinition(new TaskTypeId("u"), new StationId("nowhere"), TaskCategory.Support, 5, 0, ResourceEffect.None);
        Assert.Throws<ArgumentException>(() => Rebuild(fixture, new[] { good, unknownStation }, fixture.Movement));
        Assert.Throws<ArgumentException>(() => Rebuild(fixture, new[] { good, good }, fixture.Movement), "Duplicate task.");
        Assert.Throws<ArgumentException>(() => Rebuild(fixture, new TaskDefinition[0], fixture.Movement));
        Assert.Throws<ArgumentException>(() => Rebuild(fixture, new[] { good }, new CrewMovementSettings(2, 3.5)),
            "A spawn point must be admissible for the crew radius.");
        Assert.Throws<ArgumentException>(() => new OutpostScenario("Bad Id", "x", fixture.Layout, fixture.Movement, new[] { good }, fixture.Simulation));
        Assert.Throws<ArgumentException>(() => new OutpostScenario("ok", " ", fixture.Layout, fixture.Movement, new[] { good }, fixture.Simulation));

        Assert.Throws<ArgumentException>(() => new TaskDefinition(new TaskTypeId("s"), OutpostFixtures.ScienceBench,
            TaskCategory.Science, 10, 50, ResourceEffect.None), "Science uses the phase 1 channel.");
        Assert.Throws<ArgumentOutOfRangeException>(() => new TaskDefinition(new TaskTypeId("s"), OutpostFixtures.AirMonitor,
            TaskCategory.Support, 0, 0, ResourceEffect.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TaskDefinition(new TaskTypeId("s"), OutpostFixtures.AirMonitor,
            TaskCategory.Support, 5, -1, ResourceEffect.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TaskDefinition(new TaskTypeId("s"), OutpostFixtures.AirMonitor,
            (TaskCategory)7, 5, 0, ResourceEffect.None));
        Assert.Throws<ArgumentNullException>(() => new TaskDefinition(new TaskTypeId("s"), OutpostFixtures.AirMonitor,
            TaskCategory.Support, 5, 0, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CrewMovementSettings(0, 0.3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CrewMovementSettings(2, double.PositiveInfinity));
    }

    private static OutpostScenario Rebuild(OutpostScenario s, IEnumerable<TaskDefinition> tasks, CrewMovementSettings movement) =>
        new OutpostScenario(s.Id, s.Provenance, s.Layout, movement, tasks, s.Simulation);

    [Test] public void StocksRejectNegativeNonFiniteAndOverCapacityAmounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WaterStock(-0.1, 0, 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WaterStock(0, double.NaN, 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WaterStock(0, 0, double.PositiveInfinity, 10));
        Assert.Throws<ArgumentException>(() => new WaterStock(11, 0, 10, 10));
        Assert.Throws<ArgumentException>(() => new WaterStock(0, 10.5, 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FoodStock(-1, 10));
        Assert.Throws<ArgumentException>(() => new FoodStock(11, 10));
        var water = new WaterStock(10, 0, 10, 0);
        Assert.That(water.PotableKilograms, Is.EqualTo(10), "Full and empty stocks are valid.");
    }

    [Test] public void ResourceEffectsDeclareAnExplicitMassBalance()
    {
        var changes = new List<ResourceChange>
        {
            new ResourceChange(ResourceKind.UntreatedWater, -2), new ResourceChange(ResourceKind.PotableWater, 1.8)
        };
        var effect = new ResourceEffect(changes, 0.2);
        changes.Clear();
        Assert.That(effect.Changes.Count, Is.EqualTo(2), "The effect keeps its own copy.");
        Assert.That(effect.DeltaFor(ResourceKind.PotableWater), Is.EqualTo(1.8));
        Assert.That(effect.DeltaFor(ResourceKind.StoredFood), Is.EqualTo(0));
        Assert.Throws<NotSupportedException>(() => ((IList<ResourceChange>)effect.Changes).Clear());

        Assert.Throws<ArgumentException>(() => new ResourceEffect(new[]
            { new ResourceChange(ResourceKind.UntreatedWater, -2), new ResourceChange(ResourceKind.PotableWater, 1.8) }, 0),
            "0.2 kg would disappear silently.");
        Assert.Throws<ArgumentException>(() => new ResourceEffect(new[]
            { new ResourceChange(ResourceKind.PotableWater, -1), new ResourceChange(ResourceKind.PotableWater, -1) }, 2),
            "One change per kind.");
        Assert.Throws<ArgumentException>(() => new ResourceEffect(new ResourceChange[0], 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceEffect(new ResourceChange[0], double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceChange(ResourceKind.StoredFood, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceChange(ResourceKind.StoredFood, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceChange((ResourceKind)42, 1));
        Assert.That(ResourceEffect.None.IsEmpty, Is.True);
        Assert.That(ResourceEffect.None.BoundaryOutflowKilograms, Is.EqualTo(0));

        Assert.Throws<ArgumentException>(() => new ResourceTransfer(Guid.Empty, effect));
        Assert.Throws<ArgumentNullException>(() => new ResourceTransfer(Guid.NewGuid(), null));
        Assert.That(new ResourceTransfer(Guid.NewGuid(), effect).Effect, Is.SameAs(effect));
    }

    [Test] public void CabinReadingsNeverTurnMissingIntoZero()
    {
        Assert.That(GasReading.Unavailable.PartialPressureKilopascals.HasValue, Is.False);
        Assert.That(GasReading.Unavailable.ObservedAtSeconds.HasValue, Is.False);
        Assert.Throws<ArgumentException>(() => new GasReading(ReadingStatus.Unavailable, 0, null));
        Assert.Throws<ArgumentException>(() => new GasReading(ReadingStatus.Valid, 21, null));
        Assert.Throws<ArgumentException>(() => new GasReading(ReadingStatus.Stale, null, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => GasReading.Valid(-0.1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GasReading.Valid(double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GasReading((ReadingStatus)9, null, null));
        var stale = new GasReading(ReadingStatus.Stale, 0.6, 12);
        Assert.That(stale.ObservedAtSeconds, Is.EqualTo(12));

        var observation = new CabinAirObservation(GasReading.Valid(21, 3), GasReading.Unavailable);
        Assert.That(observation.Oxygen.Status, Is.EqualTo(ReadingStatus.Valid));
        Assert.That(observation.CarbonDioxide.Status, Is.EqualTo(ReadingStatus.Unavailable), "O2 and CO2 are independent readings.");
        Assert.Throws<ArgumentNullException>(() => new CabinAirObservation(null, GasReading.Unavailable));

        Assert.Throws<ArgumentOutOfRangeException>(() => new CabinConfiguration(0, 295, 21, 0.4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CabinConfiguration(40, -1, 21, 0.4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CabinConfiguration(40, 295, -21, 0.4));
    }

    [Test] public void SimulationObservationsKeepServiceWithinDemand()
    {
        var power = new PowerSnapshot(0, 100, 200, 10, 5, 5, 0, 0);
        LifeSupportSnapshot life = OutpostFixtures.InitialResources(OutpostFixtures.Scenario()).LifeSupport;
        Assert.Throws<ArgumentException>(() => new OutpostSimulationSnapshot(power, 10, 10.5, life));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutpostSimulationSnapshot(power, double.NaN, 0, life));
        Assert.Throws<ArgumentNullException>(() => new OutpostSimulationSnapshot(null, 0, 0, life));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutpostDemand(-1, false, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutpostDemand(2, false, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutpostDemand(2, true, -5));
        Assert.That(new OutpostDemand(2, true, 150).ServiceLoadWatts, Is.EqualTo(150));
    }
}
