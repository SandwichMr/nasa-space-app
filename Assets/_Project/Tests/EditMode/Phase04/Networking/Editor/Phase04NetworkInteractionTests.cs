using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Crew;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;

/// <summary>
/// D3's <see cref="BaseOperationsInteractionClient"/> (the <see cref="IBaseOperationsIntents"/> that D4's views call) against
/// D2's <see cref="FakeBaseOperationsSessionPort"/> and fixtures: what it stamps, when it sends nothing, how it is answered and
/// released. The fake decides nothing; every answer below is scripted by the test.
/// </summary>
public sealed class Phase04NetworkInteractionTests
{
    private static readonly PlayerId Me = new PlayerId(1), Other = new PlayerId(2);
    private static readonly BaseOperationsScenario Scenario = BaseOperationsFixtures.Scenario();
    private static StationId Air => BaseOperationsFixtures.AirStation;
    private static StationId Water => BaseOperationsFixtures.WaterStation;
    private static StationId Console => BaseOperationsFixtures.LogisticsConsole;

    private sealed class Rig
    {
        public readonly FakeBaseOperationsSessionPort Port = new FakeBaseOperationsSessionPort();
        public readonly BaseOperationsInteractionClient Client;
        public readonly List<string> Answered = new List<string>();
        public BaseOperationsSnapshot State;

        public Rig(bool join = true, Func<BaseOperationsSnapshotBuilder, BaseOperationsSnapshotBuilder> edit = null)
        {
            Client = new BaseOperationsInteractionClient(Port, Scenario);
            Client.Answered += Answered.Add;
            BaseOperationsSnapshot initial = BaseOperationsFixtures.Initial(Scenario, 2, Me, Other);
            // The local member changed zone twice (zone sequence 2) and stands at the air unit.
            BaseOperationsSnapshotBuilder b = BaseOperationsFixtures.Edit(Scenario, initial).Expedition(e => e.AtTime(30)
                .Session(SessionState.Running).Member(Me, zone: CrewZone.Airlock).Member(Me, zone: CrewZone.Interior)
                .Member(Me, position: new BasePosition(2.8, 6.0)));
            State = (edit != null ? edit(b) : b).Build();
            Port.Publish(State);
            if (join) Port.Join(Me);
        }

        public void Publish(Func<BaseOperationsSnapshotBuilder, BaseOperationsSnapshotBuilder> edit)
        {
            State = edit(BaseOperationsFixtures.Edit(Scenario, State)).Build();
            Port.Publish(State);
        }

        public BaseOperationCommand Sent => Port.PendingOperations.Single();
    }

    private static EquipmentStatus AirObserved(long sequence, bool current, long state = 0) =>
        new EquipmentStatus(EquipmentKind.AirRevitalization, Air, EquipmentOperatingState.Online,
            current ? EquipmentKnowledge.Nominal : EquipmentKnowledge.Unknown, Findings.None, state,
            new EquipmentObservation(sequence, EquipmentKind.AirRevitalization, Me, 10, 70, current ? state : Math.Max(0, state - 1),
                Findings.ActiveBedSaturated, Array.Empty<ObservedReading>(), current), null);

    [Test]
    public void NothingIsSentWithoutAMembershipOrState()
    {
        var rig = new Rig(join: false);
        Assert.That(rig.Client.RequestOperation(Air, BaseOperationAction.Inspect), Is.False);
        Assert.That(rig.Client.RequestPlaceOrder(Console, BaseOperationsFixtures.StandardManifest), Is.False);
        Assert.That(rig.Port.PendingOperations, Is.Empty);
        Assert.That(rig.Client.HasPending, Is.False);
        Assert.That(rig.Answered, Is.Empty, "nothing sent, nothing to answer");
    }

    [Test]
    public void IntentsAreStampedWithEpochZoneAndTheCurrentObservation()
    {
        var rig = new Rig(edit: b => b.Equipment(AirObserved(4, true, 3)));
        Assert.That(rig.Client.RequestOperation(Air, BaseOperationAction.ServiceAir, OperationChoice.SwitchBed));
        BaseOperationCommand c = rig.Sent;
        Assert.That((c.Epoch, c.Action, c.Station, c.Choice), Is.EqualTo((2L, BaseOperationAction.ServiceAir, Air, OperationChoice.SwitchBed)));
        Assert.That(c.ObservationSequence, Is.EqualTo(4), "the observation the player saw");
        Assert.That(c.ZoneSequence, Is.EqualTo(2), "the confirmed zone sequence of the local member");
        Assert.That(c.ShapeError(), Is.Null);
        Assert.That(c.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(rig.Client.HasPending && ReferenceEquals(rig.Client.Pending, c));
    }

    [Test]
    public void ADecisionWithoutACurrentObservationIsNotSent()
    {
        var none = new Rig();
        Assert.That(none.Client.RequestOperation(Air, BaseOperationAction.Isolate), Is.False, "no observation at all");
        var stale = new Rig(edit: b => b.Equipment(AirObserved(4, false, 3)));
        Assert.That(stale.Client.TryGetCurrentObservation(Air, out _), Is.False);
        Assert.That(stale.Client.RequestOperation(Air, BaseOperationAction.Intervene, OperationChoice.ReplaceInletFilter), Is.False,
            "an observation that is no longer current is never used");
        Assert.That(stale.Client.RequestOperation(Air, BaseOperationAction.Inspect), "inspecting needs no observation");
        Assert.That(stale.Sent.ObservationSequence, Is.EqualTo(BaseOperationCommand.Unstated));
        Assert.That(new Rig().Client.RequestOperation(Console, BaseOperationAction.Restore), Is.False, "no machine at the console");
    }

    [Test]
    public void MalformedRequestsAreNotSent()
    {
        var rig = new Rig(edit: b => b.Equipment(AirObserved(4, true)));
        Assert.That(rig.Client.RequestOperation(Air, BaseOperationAction.ServiceAir), Is.False, "a decision without a choice");
        Assert.That(rig.Client.RequestOperation(Air, BaseOperationAction.Inspect, OperationChoice.SwitchBed), Is.False, "a choice that does not belong");
        Assert.That(rig.Client.RequestOperation(Console, BaseOperationAction.PlaceOrder), Is.False, "orders go through RequestPlaceOrder");
        Assert.That(rig.Client.RequestPlaceOrder(Console, "Not A Key"), Is.False);
        Assert.That(rig.Client.RequestCancelOrder(Console, 0), Is.False);
        Assert.That(rig.Client.RequestUnload(Console, -3), Is.False);
        Assert.That(rig.Port.PendingOperations, Is.Empty);
    }

    [Test]
    public void OrdersCarryTheZoneStampAndCancellingOwnWorkDoesNot()
    {
        var rig = new Rig();
        Assert.That(rig.Client.RequestUnload(Console, 7));
        BaseOperationCommand unload = rig.Sent;
        Assert.That((unload.Action, unload.OrderSequence, unload.ZoneSequence), Is.EqualTo((BaseOperationAction.UnloadDelivery, 7L, 2L)));
        rig.Port.Reject(unload.Id, BaseOperationReasons.OrderNotArrived);
        Assert.That(rig.Client.RequestCancelOperation(Water));
        BaseOperationCommand cancel = rig.Sent;
        Assert.That(cancel.Action, Is.EqualTo(BaseOperationAction.CancelOperation));
        Assert.That(cancel.ZoneSequence, Is.EqualTo(BaseOperationCommand.Unstated), "walking away must not prevent stopping");
        rig.Port.Accept(cancel.Id);
        Assert.That(rig.Client.RequestOperation(Water, BaseOperationAction.CancelOperation), "the same through RequestOperation");
        Assert.That(rig.Sent.ZoneSequence, Is.EqualTo(BaseOperationCommand.Unstated));
    }

    [Test]
    public void OneRequestInFlightAndOnlyItsOwnAnswerReleasesIt()
    {
        var rig = new Rig();
        Assert.That(rig.Client.RequestOperation(Air, BaseOperationAction.Inspect));
        Assert.That(rig.Client.RequestPlaceOrder(Console, BaseOperationsFixtures.StandardManifest), Is.False, "busy");
        BaseOperationCommand first = rig.Sent;
        var other = ExpeditionCommand.Session(Guid.NewGuid(), 2, ExpeditionAction.Pause);
        rig.Port.Submit(other);
        rig.Port.Accept(other.Id); // someone else's answer on the same port
        Assert.That(rig.Client.HasPending, "an unrelated result is not ours");
        Assert.That(rig.Answered, Is.Empty);
        rig.Port.Reject(first.Id, OutpostReasons.OutOfRange);
        Assert.That(rig.Answered, Is.EqualTo(new[] { OutpostReasons.OutOfRange }));
        Assert.That(rig.Client.LastReason, Is.EqualTo(OutpostReasons.OutOfRange));
        Assert.That(rig.Client.HasPending, Is.False);
        Assert.That(rig.Client.RequestPlaceOrder(Console, BaseOperationsFixtures.StandardManifest), "free again");
        rig.Port.Accept(rig.Sent.Id);
        Assert.That(rig.Answered.Last(), Is.EqualTo(OutpostReasons.Accepted));
    }

    [Test]
    public void ALostMembershipReleasesThePendingRequestHonestly()
    {
        var lost = new Rig();
        Assert.That(lost.Client.RequestOperation(Air, BaseOperationAction.Inspect));
        lost.Port.Disconnect("ClosedByRemote");
        Assert.That(lost.Answered, Is.EqualTo(new[] { OutpostReasons.ConnectionLost }), "unknown result, once");
        Assert.That(lost.Client.HasPending, Is.False);
        Assert.That(lost.Client.RequestOperation(Air, BaseOperationAction.Inspect), Is.False, "nothing sent after the link is gone");

        var local = new Rig();
        Assert.That(local.Client.RequestOperation(Air, BaseOperationAction.Inspect));
        local.Port.Disconnect(OutpostReasons.LocalDisconnect);
        Assert.That(local.Answered, Is.EqualTo(new[] { OutpostReasons.LocalDisconnect }));
    }

    [Test]
    public void InteractCancelsOnlyTheLocalPlayersOwnOperation()
    {
        var rig = new Rig();
        Assert.That(rig.Client.Interact(), Is.False, "nothing of its own");
        rig.Publish(b => b.Operation(new OperationProgress(Guid.NewGuid(), Console, BaseOperationAction.PlaceOrder, OperationChoice.None,
            Other, 1, 3, false, null)));
        Assert.That(rig.Client.Interact(), Is.False, "another member's work is not ours to cancel");
        rig.Publish(b => b.Operation(new OperationProgress(Guid.NewGuid(), Air, BaseOperationAction.Inspect, OperationChoice.None, Me, 1, 4, false, null)));
        Assert.That(rig.Client.TryGetOwnOperation(out OperationProgress own) && own.Station == Air);
        Assert.That(rig.Client.Interact());
        Assert.That((rig.Sent.Action, rig.Sent.Station), Is.EqualTo((BaseOperationAction.CancelOperation, Air)));
    }

    [Test]
    public void OnlyPhase4StationsAreOfferedInReach()
    {
        var rig = new Rig();
        Assert.That(rig.Client.TryFindStationInReach(out StationPlacement near) && near.Id == Air);
        Assert.That(rig.Client.IsBaseOperationsStation(Console) && rig.Client.IsBaseOperationsStation(Water));
        Assert.That(rig.Client.IsBaseOperationsStation(ExpeditionFixtures.RackA), Is.False);
        rig.Publish(b => b.Expedition(e => e.Member(Me, position: ExpeditionFixtures.Scenario(false).Layout.SpawnPoints[0])));
        Assert.That(rig.Client.TryFindStationInReach(out _), Is.False, "spawn point: no phase 4 station within reach");
    }

    /// <summary>A port that throws when asked to send (for example a transport refusing the message).</summary>
    private sealed class ThrowingPort : IBaseOperationsSessionPort
    {
        private readonly FakeBaseOperationsSessionPort inner;
        public ThrowingPort(FakeBaseOperationsSessionPort inner) => this.inner = inner;
        public OutpostMembership Membership => inner.Membership;
        public event Action<OutpostSessionSnapshot> SnapshotChanged { add => inner.SnapshotChanged += value; remove => inner.SnapshotChanged -= value; }
        public event Action<ExpeditionSessionSnapshot> ExpeditionChanged { add => inner.ExpeditionChanged += value; remove => inner.ExpeditionChanged -= value; }
        public event Action<BaseOperationsSnapshot> BaseOperationsChanged { add => inner.BaseOperationsChanged += value; remove => inner.BaseOperationsChanged -= value; }
        public event Action<CommandResult> CommandCompleted { add => inner.CommandCompleted += value; remove => inner.CommandCompleted -= value; }
        public event Action<OutpostCommand, string> CommandAbandoned { add => inner.CommandAbandoned += value; remove => inner.CommandAbandoned -= value; }
        public event Action<ExpeditionCommand, string> ExpeditionCommandAbandoned { add => inner.ExpeditionCommandAbandoned += value; remove => inner.ExpeditionCommandAbandoned -= value; }
        public event Action<BaseOperationCommand, string> BaseOperationCommandAbandoned { add => inner.BaseOperationCommandAbandoned += value; remove => inner.BaseOperationCommandAbandoned -= value; }
        public event Action<OutpostMembership> MembershipChanged { add => inner.MembershipChanged += value; remove => inner.MembershipChanged -= value; }
        public bool TryGetSnapshot(out OutpostSessionSnapshot snapshot) => inner.TryGetSnapshot(out snapshot);
        public bool TryGetExpedition(out ExpeditionSessionSnapshot snapshot) => inner.TryGetExpedition(out snapshot);
        public bool TryGetBaseOperations(out BaseOperationsSnapshot snapshot) => inner.TryGetBaseOperations(out snapshot);
        public void Submit(OutpostCommand command) => inner.Submit(command);
        public void Submit(ExpeditionCommand command) => inner.Submit(command);
        public void Submit(BaseOperationCommand command) => throw new InvalidOperationException("send refused (simulated)");
        public void SubmitMovement(MovementInput input) => inner.SubmitMovement(input);
    }

    [Test]
    public void APortThatThrowsNeverLooksLikeASuccess()
    {
        var fake = new FakeBaseOperationsSessionPort();
        var client = new BaseOperationsInteractionClient(new ThrowingPort(fake), Scenario);
        var answers = new List<string>();
        client.Answered += answers.Add;
        fake.Publish(BaseOperationsFixtures.Initial(Scenario, 0, Me));
        fake.Join(Me);
        bool sent = false;
        Assert.DoesNotThrow(() => sent = client.RequestOperation(Water, BaseOperationAction.Inspect));
        Assert.That(sent);
        Assert.That(answers, Is.EqualTo(new[] { OutpostReasons.ConnectionLost }));
        Assert.That(client.HasPending, Is.False);
        client.Dispose();
        Assert.Throws<ObjectDisposedException>(() => client.RequestOperation(Water, BaseOperationAction.Inspect));
    }
}
