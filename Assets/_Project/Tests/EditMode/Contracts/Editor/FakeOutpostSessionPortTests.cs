using System;
using System.Collections.Generic;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Application.Outpost;

/// <summary>Development fake of the phase 2 port: deferred, fixture-driven, never an authority.</summary>
public sealed class FakeOutpostSessionPortTests
{
    private static readonly PlayerId P1 = new PlayerId(1), P2 = new PlayerId(2);
    private OutpostScenario scenario;
    private FakeOutpostSessionPort port;
    private List<CommandResult> results;
    private List<(OutpostCommand, string)> abandoned;
    private List<OutpostSessionSnapshot> snapshots;
    private List<OutpostMembership> memberships;

    [SetUp] public void SetUp()
    {
        scenario = OutpostFixtures.Scenario();
        port = new FakeOutpostSessionPort();
        results = new List<CommandResult>();
        abandoned = new List<(OutpostCommand, string)>();
        snapshots = new List<OutpostSessionSnapshot>();
        memberships = new List<OutpostMembership>();
        port.CommandCompleted += results.Add;
        port.CommandAbandoned += (command, reason) => abandoned.Add((command, reason));
        port.SnapshotChanged += snapshots.Add;
        port.MembershipChanged += memberships.Add;
    }

    private OutpostSessionSnapshot JoinAs(PlayerId player, params PlayerId[] crew)
    {
        OutpostSessionSnapshot initial = OutpostFixtures.Initial(scenario, 0, crew.Length == 0 ? new[] { player } : crew);
        port.Publish(initial);
        port.Join(player);
        return initial;
    }

    private OutpostCommand Start() =>
        OutpostCommand.StartTask(Guid.NewGuid(), 0, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);

    [Test] public void StartsWithoutStateOrIdentityAndIsLabelled()
    {
        Assert.That(port.TryGetSnapshot(out OutpostSessionSnapshot none), Is.False);
        Assert.That(none, Is.Null);
        Assert.That(port.Membership.Status, Is.EqualTo(MembershipStatus.NotJoined));
        StringAssert.Contains("DESARROLLO", FakeOutpostSessionPort.Label);
        StringAssert.Contains("sin autoridad", FakeOutpostSessionPort.Label);
        Assert.Throws<ArgumentNullException>(() => port.Submit(null));
        Assert.Throws<ArgumentNullException>(() => port.SubmitMovement(null));
        Assert.Throws<ArgumentNullException>(() => port.Publish(null));
    }

    [Test] public void CommandsBeforeJoiningAreAbandonedNotSent()
    {
        OutpostCommand command = Start();
        port.Submit(command);
        Assert.That(abandoned.Count, Is.EqualTo(1));
        Assert.That(abandoned[0].Item1, Is.SameAs(command));
        Assert.That(abandoned[0].Item2, Is.EqualTo(OutpostReasons.NotConnected));
        Assert.That(port.PendingCommands.Count, Is.EqualTo(0));
        Assert.That(results.Count, Is.EqualTo(0));
    }

    [Test] public void JoinNeedsPublishedStateThatContainsThePlayer()
    {
        Assert.Throws<InvalidOperationException>(() => port.Join(P1), "No state yet.");
        port.Publish(OutpostFixtures.Initial(scenario, 0, P2));
        Assert.Throws<InvalidOperationException>(() => port.Join(P1), "P1 is not in the published crew.");
        port.Join(P2);
        Assert.That(port.Membership.Player, Is.EqualTo(P2));
        Assert.That(memberships.Count, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => port.Join(P2), "Already joined.");
        Assert.Throws<InvalidOperationException>(() => port.RejectJoin(OutpostReasons.SessionFull));
    }

    [Test] public void RejectedJoinKeepsThePortWithoutIdentity()
    {
        port.Publish(OutpostFixtures.Initial(scenario, 0, P1));
        port.RejectJoin(OutpostReasons.SessionFull);
        Assert.That(port.Membership.Status, Is.EqualTo(MembershipStatus.Rejected));
        Assert.That(port.Membership.Reason, Is.EqualTo(OutpostReasons.SessionFull));
        port.Submit(Start());
        Assert.That(abandoned[0].Item2, Is.EqualTo(OutpostReasons.NotConnected));
    }

    [Test] public void ResultsWaitForTheFixtureAndCannotBeInvented()
    {
        JoinAs(P1);
        OutpostCommand first = Start(), second = Start();
        port.Submit(first);
        port.Submit(second);
        Assert.That(results.Count, Is.EqualTo(0), "Nothing is decided at Submit.");
        Assert.That(port.PendingCommands.Count, Is.EqualTo(2));

        CommandResult rejected = port.Reject(second.Id, OutpostReasons.StationOccupied);
        CommandResult accepted = port.Accept(first.Id);
        Assert.That(results, Is.EqualTo(new[] { rejected, accepted }), "Completion order is the fixture's choice.");
        Assert.That(accepted.CommandId, Is.EqualTo(first.Id));
        Assert.That(accepted.Epoch, Is.EqualTo(first.Epoch));
        Assert.That(accepted.Reason, Is.EqualTo(OutpostReasons.Accepted));
        Assert.That(rejected.Accepted, Is.False);
        Assert.That(port.PendingCommands.Count, Is.EqualTo(0));

        Assert.Throws<InvalidOperationException>(() => port.Accept(first.Id), "Already completed.");
        Assert.Throws<InvalidOperationException>(() => port.Accept(Guid.NewGuid()), "Never submitted.");
        port.Submit(Start());
        Assert.Throws<ArgumentException>(() => port.Reject(port.PendingCommands[0].Id, OutpostReasons.Accepted));
        Assert.Throws<ArgumentException>(() => port.Reject(port.PendingCommands[0].Id, ""));
        Assert.That(snapshots.Count, Is.EqualTo(1), "Results never change the published state by themselves.");
    }

    [Test] public void RepeatedIdsAreNotDeduplicatedByTheFake()
    {
        JoinAs(P1);
        OutpostCommand command = Start();
        port.Submit(command);
        port.Submit(command);
        Assert.That(port.PendingCommands.Count, Is.EqualTo(2), "Deduplication is the authority's rule, not the fake's.");
        port.Accept(command.Id);
        port.Accept(command.Id);
        Assert.That(results.Count, Is.EqualTo(2), "One answer per submission.");
    }

    [Test] public void PublishOnlyMovesForward()
    {
        OutpostSessionSnapshot initial = JoinAs(P1, P1, P2);
        Assert.Throws<InvalidOperationException>(() => port.Publish(initial), "Equal revision.");
        OutpostSessionSnapshot next = OutpostFixtures.Next(initial);
        port.Publish(next);
        Assert.Throws<InvalidOperationException>(() => port.Publish(OutpostFixtures.Initial(scenario, 0, P1, P2)), "Older revision.");
        port.Publish(OutpostFixtures.Next(next, newEpoch: 1));
        Assert.That(port.TryGetSnapshot(out OutpostSessionSnapshot current), Is.True);
        Assert.That(current.Epoch, Is.EqualTo(1));
        Assert.That(current.Revision, Is.EqualTo(0));
        Assert.Throws<InvalidOperationException>(() =>
                port.Publish(OutpostFixtures.Next(current, crew: OutpostFixtures.WithoutMember(current, P1))),
            "While joined, the local player cannot silently vanish.");
        port.Publish(OutpostFixtures.Next(current, crew: OutpostFixtures.WithoutMember(current, P2)));
        Assert.That(snapshots.Count, Is.EqualTo(4));
    }

    [Test] public void DisconnectAbandonsEveryPendingCommandAndEndsTheIdentity()
    {
        JoinAs(P1);
        OutpostCommand a = Start(), b = OutpostCommand.CancelTask(Guid.NewGuid(), 0, OutpostFixtures.WaterProcessor);
        port.Submit(a);
        port.Submit(b);
        port.Disconnect(OutpostReasons.ConnectionLost);

        Assert.That(port.Membership.Status, Is.EqualTo(MembershipStatus.Left));
        Assert.That(port.Membership.Player, Is.EqualTo(P1));
        Assert.That(abandoned.Count, Is.EqualTo(2));
        Assert.That(abandoned[0], Is.EqualTo((a, OutpostReasons.ConnectionLost)));
        Assert.That(abandoned[1], Is.EqualTo((b, OutpostReasons.ConnectionLost)));
        Assert.That(results.Count, Is.EqualTo(0), "Abandoned is not a result: the authority may have applied it.");
        Assert.Throws<InvalidOperationException>(() => port.Accept(a.Id));
        Assert.That(port.TryGetSnapshot(out _), Is.True, "Last known state is kept; Membership says it is not live.");

        port.Submit(Start());
        Assert.That(abandoned[2].Item2, Is.EqualTo(OutpostReasons.NotConnected));
        Assert.Throws<InvalidOperationException>(() => port.Disconnect(OutpostReasons.ConnectionLost));
    }

    [Test] public void MovementIsRecordedButNeverApplied()
    {
        var early = new MovementInput(0, 1, MoveDirection.FromAxes(1, 0));
        port.SubmitMovement(early);
        Assert.That(port.DroppedMovementInputCount, Is.EqualTo(1));
        Assert.That(port.LastMovementInput, Is.Null);

        OutpostSessionSnapshot initial = JoinAs(P1);
        var input = new MovementInput(0, 2, MoveDirection.FromAxes(0, 1));
        port.SubmitMovement(input);
        Assert.That(port.LastMovementInput, Is.SameAs(input));
        Assert.That(port.MovementInputCount, Is.EqualTo(1));
        Assert.That(port.TryGetSnapshot(out OutpostSessionSnapshot current), Is.True);
        Assert.That(current, Is.SameAs(initial), "The fake never moves anyone.");
        Assert.That(snapshots.Count, Is.EqualTo(1));
    }
}
