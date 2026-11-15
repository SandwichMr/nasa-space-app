using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;

/// <summary>Phase 4 common base: the snapshot keeps the session cycle, operations, spares and orders consistent, and is immutable.</summary>
public sealed class Phase04SnapshotContractTests
{
    private static readonly PlayerId Ana = new PlayerId(1);
    private static readonly PlayerId Leo = new PlayerId(2);
    private BaseOperationsScenario scenario;
    private BaseOperationsSnapshot initial;

    [SetUp]
    public void SetUp()
    {
        scenario = BaseOperationsFixtures.Scenario();
        initial = BaseOperationsFixtures.Initial(scenario, 0, Ana, Leo);
    }

    private OperationProgress Batch(PlayerId who, double work = 5) => new OperationProgress(Guid.NewGuid(), scenario.WaterStation,
        BaseOperationAction.ProcessBatch, OperationChoice.None, who, work, 20, false, null);

    [Test]
    public void InitialFixtureContainsThePhase3AndPhase2StatesOfTheSameRevision()
    {
        Assert.That(initial.Expedition.Outpost, Is.SameAs(initial.Outpost));
        Assert.That(initial.Revision, Is.EqualTo(0));
        Assert.That(initial.Session.Mode, Is.EqualTo(BaseSessionMode.Continuous));
        Assert.That(initial.Gauges.Select(g => g.Kind), Is.EqualTo(Enum.GetValues(typeof(GaugeKind)).Cast<GaugeKind>()));
        Assert.That(initial.Equipment.Select(e => e.Knowledge), Is.All.EqualTo(EquipmentKnowledge.Unknown), "nothing observed yet is unknown, not nominal");
        Assert.That(initial.Water.ProductConductivity.Status, Is.EqualTo(ReadingStatus.Unavailable));
        Assert.That(initial.GaugeOf(GaugeKind.PotableWater).Reading.Value, Is.EqualTo(20));
        Assert.That(initial.GaugeOf(GaugeKind.PotableWater).Band, Is.EqualTo(GaugeBand.Nominal));
        Assert.That(initial.FreeSpares(BaseOperationsFixtures.ProcessorFilter), Is.EqualTo(1));
        BaseOperationsSnapshot next = BaseOperationsFixtures.Edit(scenario, initial).Build();
        Assert.That(next.IsNewerThan(initial), Is.True);
        Assert.That(initial.IsNewerThan(next), Is.False);
    }

    [Test]
    public void ContinuousSessionNeverCompletesAndEndsOnlyIrreversibly()
    {
        Assert.Throws<ArgumentException>(() => BaseOperationsFixtures.Edit(scenario, initial)
            .Expedition(e => e.Ended(ExpeditionOutcome.ScienceReturned)).Build(), "completing an objective never closes the base");
        Assert.Throws<ArgumentException>(() => BaseOperationsFixtures.Edit(scenario, initial)
            .Expedition(e => e.Ended(ExpeditionOutcome.EssentialServiceLost)).Build(), "Interrupted needs the irreversible cause");
        var lost = new BaseSessionStatus(BaseSessionMode.Continuous, 60, 60, IrreversibleCause.EssentialPowerLost, 300, 0, 0);
        Assert.DoesNotThrow(() => BaseOperationsFixtures.Edit(scenario, initial).Session(lost)
            .Expedition(e => e.AtTime(300).Ended(ExpeditionOutcome.EssentialServiceLost)).Build());
        Assert.Throws<ArgumentException>(() => BaseOperationsFixtures.Edit(scenario, initial).Session(lost).Build(),
            "an irreversible cause while still running");
    }

    [Test]
    public void AnExpeditionCanFinishAndTheNextOneStartsInTheSameSession()
    {
        BaseOperationsSnapshot underway = BaseOperationsFixtures.Edit(scenario, initial)
            .Expedition(e => e.Session(SessionState.Running).AtTime(50).Phase(ExpeditionPhase.Underway))
            .Activity(new ActivityRecord(1, ActivityKind.Expedition, "expedition", ActivityStatus.Active, Ana, 40, null, null)).Build();
        BaseOperationsSnapshot back = BaseOperationsFixtures.Edit(scenario, underway)
            .Expedition(e => e.AtTime(90).Phase(ExpeditionPhase.Preparing).Decision(null))
            .Activity(new ActivityRecord(1, ActivityKind.Expedition, "expedition", ActivityStatus.Completed, Ana, 40, 90, "returned-without-science"))
            .Build();
        Assert.That(back.Outpost.State, Is.EqualTo(SessionState.Running));
        Assert.That(back.Expedition.Phase, Is.EqualTo(ExpeditionPhase.Preparing));
        Assert.That(back.Session.ActivitySequence, Is.EqualTo(1));
        BaseOperationsSnapshot second = BaseOperationsFixtures.Edit(scenario, back)
            .Activity(new ActivityRecord(2, ActivityKind.Expedition, "expedition", ActivityStatus.Active, Leo, 120, null, null)).Build();
        Assert.That(second.Activities.Count, Is.EqualTo(2));
    }

    [Test]
    public void OperationsOccupyPerformerAndStationAndHideThePhase2Station()
    {
        BaseOperationsSnapshot busy = BaseOperationsFixtures.Edit(scenario, initial)
            .Expedition(e => e.Session(SessionState.Running)).Operation(Batch(Ana)).Build();
        Assert.That(busy.Outpost.TryGetStation(scenario.WaterStation, out StationState station), Is.True);
        Assert.That(station.Availability, Is.EqualTo(StationAvailability.Unavailable));
        Assert.That(busy.TryGetOperation(Ana, out _), Is.True);
        Assert.That(busy.TryGetOperationAt(scenario.WaterStation, out _), Is.True);

        Assert.Throws<ArgumentException>(() => BaseOperationsFixtures.Edit(scenario, busy).Operation(new OperationProgress(Guid.NewGuid(),
            scenario.AirStation, BaseOperationAction.Inspect, OperationChoice.None, Ana, 0, 4, false, null)).Build(), "two operations for one member");
        Assert.Throws<ArgumentException>(() => BaseOperationsFixtures.Edit(scenario, initial)
            .Operation(Batch(new PlayerId(7))).Build(), "absent performer");
        Assert.Throws<ArgumentException>(() => BaseOperationsFixtures.Edit(scenario, busy)
            .Expedition(e => e.Station(StationState.Free(scenario.WaterStation))).Build(), "phase 2 station must be Unavailable");
        var lost = new BaseSessionStatus(BaseSessionMode.Continuous, 60, 60, IrreversibleCause.EssentialPowerLost, 0, 0, 0);
        Assert.Throws<ArgumentException>(() => BaseOperationsFixtures.Edit(scenario, busy).Session(lost)
            .Expedition(e => e.Ended(ExpeditionOutcome.EssentialServiceLost)).Build(), "no operation after the end");
        BaseOperationsSnapshot freed = BaseOperationsFixtures.Edit(scenario, busy).EndOperation(scenario.WaterStation).Build();
        Assert.That(freed.Operations, Is.Empty);
    }

    [Test]
    public void ReservedSparesNeverExceedTheStock()
    {
        OperationProgress Intervene(PlayerId who, StationId at) => new OperationProgress(Guid.NewGuid(), at, BaseOperationAction.Intervene,
            OperationChoice.ReplaceProcessorFilter, who, 0, 20, false, BaseOperationsFixtures.ProcessorFilter);
        BaseOperationsSnapshot one = BaseOperationsFixtures.Edit(scenario, initial).Operation(Intervene(Ana, scenario.WaterStation)).Build();
        Assert.That(one.FreeSpares(BaseOperationsFixtures.ProcessorFilter), Is.EqualTo(0));
        Assert.Throws<ArgumentException>(() => BaseOperationsFixtures.Edit(scenario, one).Operation(Intervene(Leo, scenario.AirStation)).Build());
    }

    [Test]
    public void OrdersActivitiesAndReportsRefuseDuplicatesAndTheFuture()
    {
        ManifestItem[] items = { ManifestItem.Mass(ResourceKind.StoredFood, 6) };
        Assert.Throws<ArgumentException>(() => BaseOperationsFixtures.Edit(scenario, initial)
            .Order(new ResupplyOrder(1, BaseOperationsFixtures.StandardManifest, Ana, 50, 80, 230, OrderState.Pending, items, null)).Build(),
            "placed in the future");
        Assert.Throws<ArgumentException>(() => BaseOperationsFixtures.Edit(scenario, initial)
            .Report(new OperationsReport(1, 0, 120, 0, 0, 0, 0, 0)).Build(), "report covering the future");
        BaseOperationsSnapshot later = BaseOperationsFixtures.Edit(scenario, initial).Expedition(e => e.AtTime(150))
            .Order(new ResupplyOrder(1, BaseOperationsFixtures.StandardManifest, Ana, 100, 130, 280, OrderState.InTransit, items, null))
            .Report(new OperationsReport(1, 0, 120, 2, 1, -3.5, -1.2, -800)).Build();
        Assert.That(later.TryGetOrder(1, out ResupplyOrder order), Is.True);
        Assert.That(order.InFlight, Is.True);
        Assert.That(later.LatestReport.Sequence, Is.EqualTo(1));
    }

    [Test]
    public void EquipmentAndGaugeListsAreOnePerKindInOrder()
    {
        ResourceGauge[] gauges = BaseOperationsFixtures.GaugesFrom(scenario, initial.Outpost, 0);
        Assert.Throws<ArgumentException>(() => BaseOperationsFixtures.Edit(scenario, initial).Gauges(gauges.Reverse().ToArray()).Build());
        Assert.Throws<ArgumentException>(() => BaseOperationsFixtures.Edit(scenario, initial).Gauges(gauges.Take(7).ToArray()).Build());
        Assert.Throws<ArgumentException>(() => new BaseOperationsSnapshot(initial.Expedition, initial.Session, initial.Gauges,
            new[] { initial.Equipment[1], initial.Equipment[0] }, initial.Water, initial.Spares, initial.Operations, initial.Orders,
            initial.Activities, initial.Decisions, null));
    }

    [Test]
    public void PublishedCollectionsAreReadOnly()
    {
        Assert.Throws<NotSupportedException>(() => ((IList)initial.Gauges).Add(null));
        Assert.Throws<NotSupportedException>(() => ((IList)initial.Operations).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList)initial.Spares).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList)scenario.Manifests[0].Items).Clear());
    }

    [Test]
    public void PublishedPhase4TypesHaveNoPublicSetters()
    {
        Type[] types = typeof(BaseOperationsSnapshot).Assembly.GetTypes()
            .Where(t => t.IsPublic && t.IsClass
                && (t.Namespace == "Trainer.Application.BaseOperations" || t.Namespace == "Trainer.Simulation.BaseOperations")).ToArray();
        Assert.That(types.Length, Is.GreaterThan(25));
        foreach (Type type in types)
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                Assert.That(property.SetMethod == null || !property.SetMethod.IsPublic, Is.True, type.Name + "." + property.Name);
        foreach (Type type in types)
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                Assert.Fail(type.Name + "." + field.Name + " is a public field");
    }
}
