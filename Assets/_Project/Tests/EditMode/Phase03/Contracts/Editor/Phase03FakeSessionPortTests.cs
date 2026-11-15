using System;
using System.Collections.Generic;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;

/// <summary>Phase 3 common base: the development fake honours the client side of the port contract (and only that).</summary>
public sealed class Phase03FakeSessionPortTests
{
    private static readonly PlayerId Ana = new PlayerId(1);
    private ExpeditionScenario scenario;
    private FakeExpeditionSessionPort port;

    [SetUp]
    public void SetUp()
    {
        scenario = ExpeditionFixtures.Scenario();
        port = new FakeExpeditionSessionPort();
    }

    [Test]
    public void Phase2EventComesFirstThenPhase3ForTheSameRevision()
    {
        var order = new List<string>();
        port.SnapshotChanged += s => order.Add("outpost:" + s.Revision);
        port.ExpeditionChanged += s => order.Add("expedition:" + s.Revision);
        ExpeditionSessionSnapshot first = ExpeditionFixtures.Initial(scenario, 0, Ana);
        port.Publish(first);
        Assert.That(order, Is.EqualTo(new[] { "outpost:0", "expedition:0" }));
        Assert.That(port.TryGetSnapshot(out OutpostSessionSnapshot outpost) && ReferenceEquals(outpost, first.Outpost),
            "TryGetSnapshot is exactly the phase 2 part of the latest phase 3 snapshot");
        Assert.Throws<InvalidOperationException>(() => port.Publish(first), "snapshots only move forward");
    }

    [Test]
    public void JoinNeedsStateAndCommandsWaitForTheFixture()
    {
        Assert.Throws<InvalidOperationException>(() => port.Join(Ana));
        ExpeditionCommand early = ExpeditionCommand.DeclareReturn(Guid.NewGuid(), 0);
        string abandoned = null;
        port.ExpeditionCommandAbandoned += (c, reason) => abandoned = reason;
        port.Submit(early);
        Assert.That(abandoned, Is.EqualTo(OutpostReasons.NotConnected));

        ExpeditionSessionSnapshot first = ExpeditionFixtures.Initial(scenario, 0, Ana);
        port.Publish(first);
        port.Join(Ana);
        ExpeditionSessionSnapshot moved = ExpeditionFixtures.Edit(scenario, first).Member(Ana, zone: CrewZone.Shelter).Build();
        port.Publish(moved);
        var start = OutpostCommand.StartTask(Guid.NewGuid(), 0, ExpeditionFixtures.ShelterPanel, ExpeditionFixtures.PrepareShelter);
        port.Submit(start);
        Assert.That(port.PendingCommands[0].ZoneSequence, Is.EqualTo(1), "a phase 2 command is stamped with the confirmed zone sequence");
        CommandResult result = null;
        port.CommandCompleted += r => result = r;
        Assert.Throws<InvalidOperationException>(() => port.Accept(Guid.NewGuid()), "never invents results");
        port.Reject(start.Id, ExpeditionReasons.WrongZone);
        Assert.That(result.Accepted, Is.False);
        Assert.That(result.Reason, Is.EqualTo(ExpeditionReasons.WrongZone));
        Assert.That(port.PendingCommands, Is.Empty);
    }

    [Test]
    public void DisconnectAbandonsEachCommandThroughItsOwnEvent()
    {
        ExpeditionSessionSnapshot first = ExpeditionFixtures.Initial(scenario, 0, Ana);
        port.Publish(first);
        port.Join(Ana);
        var phase2 = new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Resume);
        ExpeditionCommand phase3 = ExpeditionCommand.CycleAirlock(Guid.NewGuid(), 0, ExpeditionFixtures.InnerPanel, 0, 0);
        port.Submit(phase2);
        port.Submit(phase3);
        OutpostCommand lost2 = null;
        ExpeditionCommand lost3 = null;
        port.CommandAbandoned += (c, r) => lost2 = c;
        port.ExpeditionCommandAbandoned += (c, r) => lost3 = c;
        port.Disconnect(OutpostReasons.ConnectionLost);
        Assert.That(lost2, Is.SameAs(phase2));
        Assert.That(lost3, Is.SameAs(phase3));
        Assert.That(port.Membership.Status, Is.EqualTo(MembershipStatus.Left));
        Assert.That(port.TryGetExpedition(out ExpeditionSessionSnapshot last) && last == first, "last known state is kept, not live");
        port.SubmitMovement(new MovementInput(0, 1, MoveDirection.FromAxes(1, 0)));
        Assert.That(port.DroppedMovementInputCount, Is.EqualTo(1));
    }
}
