using System;
using System.Collections.Generic;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;

/// <summary>Phase 4 common base: the development fake follows the client side of the port contract and is not an authority.</summary>
public sealed class Phase04FakeSessionPortTests
{
    private static readonly PlayerId Ana = new PlayerId(1);
    private BaseOperationsScenario scenario;
    private BaseOperationsSnapshot initial;
    private FakeBaseOperationsSessionPort port;

    [SetUp]
    public void SetUp()
    {
        scenario = BaseOperationsFixtures.Scenario();
        initial = BaseOperationsFixtures.Initial(scenario, 0, Ana);
        port = new FakeBaseOperationsSessionPort();
    }

    [Test]
    public void EventsArePhase2ThenPhase3ThenPhase4ForTheSameRevision()
    {
        var order = new List<string>();
        port.SnapshotChanged += s => order.Add("p2:" + s.Revision);
        port.ExpeditionChanged += s => order.Add("p3:" + s.Revision);
        port.BaseOperationsChanged += s => order.Add("p4:" + s.Revision);
        port.Publish(initial);
        Assert.That(order, Is.EqualTo(new[] { "p2:0", "p3:0", "p4:0" }));
        Assert.That(port.TryGetExpedition(out ExpeditionSessionSnapshot e) && e == initial.Expedition, Is.True);
        Assert.Throws<InvalidOperationException>(() => port.Publish(initial), "never older or equal");
    }

    [Test]
    public void JoinNeedsASnapshotWithThePlayer()
    {
        Assert.Throws<InvalidOperationException>(() => port.Join(Ana));
        port.Publish(initial);
        Assert.Throws<InvalidOperationException>(() => port.Join(new PlayerId(2)));
        port.Join(Ana);
        Assert.That(port.Membership.IsJoined, Is.True);
    }

    [Test]
    public void CommandsWaitForTheFixtureAndTheFakeNeverInventsResults()
    {
        string abandoned = null;
        port.BaseOperationCommandAbandoned += (c, r) => abandoned = r;
        var inspect = BaseOperationCommand.Operation(Guid.NewGuid(), 0, BaseOperationAction.Inspect, scenario.WaterStation);
        port.Submit(inspect);
        Assert.That(abandoned, Is.EqualTo(OutpostReasons.NotConnected), "not joined");

        port.Publish(initial);
        port.Join(Ana);
        var results = new List<CommandResult>();
        port.CommandCompleted += results.Add;
        port.Submit(inspect);
        Assert.That(port.PendingOperations, Has.Count.EqualTo(1));
        Assert.That(results, Is.Empty, "no result until the fixture decides");
        Assert.Throws<InvalidOperationException>(() => port.Accept(Guid.NewGuid()));
        port.Reject(inspect.Id, BaseOperationReasons.StaleObservation);
        Assert.That(results[0].Accepted, Is.False);
        Assert.That(results[0].Reason, Is.EqualTo(BaseOperationReasons.StaleObservation));
        Assert.Throws<ArgumentException>(() => port.Reject(Guid.NewGuid(), OutpostReasons.Accepted));
    }

    [Test]
    public void DisconnectAbandonsEachPendingCommandThroughItsOwnEvent()
    {
        port.Publish(initial);
        port.Join(Ana);
        var reasons = new List<string>();
        port.CommandAbandoned += (c, r) => reasons.Add("p2:" + r);
        port.ExpeditionCommandAbandoned += (c, r) => reasons.Add("p3:" + r);
        port.BaseOperationCommandAbandoned += (c, r) => reasons.Add("p4:" + r);
        port.Submit(new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Pause));
        port.Submit(ExpeditionCommand.DeclareReturn(Guid.NewGuid(), 0));
        port.Submit(BaseOperationCommand.PlaceOrder(Guid.NewGuid(), 0, scenario.Logistics.Console, BaseOperationsFixtures.StandardManifest));
        port.Disconnect(OutpostReasons.ConnectionLost);
        Assert.That(reasons, Is.EqualTo(new[] { "p2:ConnectionLost", "p3:ConnectionLost", "p4:ConnectionLost" }));
        Assert.That(port.Membership.IsJoined, Is.False);
        Assert.That(port.TryGetBaseOperations(out _), Is.True, "last known state is kept");
    }

    [Test]
    public void MovementIsOnlyRecorded()
    {
        port.SubmitMovement(new MovementInput(0, 1, new MoveDirection(1, 0)));
        Assert.That(port.DroppedMovementInputCount, Is.EqualTo(1));
        port.Publish(initial);
        port.Join(Ana);
        port.SubmitMovement(new MovementInput(0, 2, new MoveDirection(1, 0)));
        Assert.That(port.MovementInputCount, Is.EqualTo(1));
        Assert.That(initial.Outpost.Crew[0].Pose.Position, Is.EqualTo(BaseOperationsFixtures.Edit(scenario, initial).Build().Outpost.Crew[0].Pose.Position));
    }
}
