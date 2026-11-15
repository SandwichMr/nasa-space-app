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
using Trainer.Unity.Networking;
using Trainer.Unity.Networking.BaseOperations;
using A = Phase04NetworkTestAuthority;
using F = Phase04NetworkFingerprint;

/// <summary>
/// D3 phase 4 networking end to end IN MEMORY: the 'T' 'B' bridge, cable, client adapter/controller, host-local port and the
/// interaction client over D3's in-memory transport, in front of <see cref="Phase04NetworkTestAuthority"/> — a labelled TEST
/// DOUBLE of the authority (D2's real one is not in this base). What is proven is the network's part: identity, full state,
/// ordering, stamps, deduplication by (id, epoch), Leave once, honest abandonment. The base rules themselves (reservations,
/// orders on Leave, stale observations) are D2's; here they come from the double. No sockets or processes (see PlayMode).
/// </summary>
public sealed class Phase04NetworkLoopbackTests
{
    private sealed class Rig : IDisposable
    {
        public readonly A Authority;
        public readonly InMemoryOutpostNetwork Net = new InMemoryOutpostNetwork();
        public readonly BaseOperationsCommandBridge Bridge;
        public readonly List<string> HostErrors = new List<string>();
        public readonly List<(PlayerId sender, CommandResult result)> Answers = new List<(PlayerId, CommandResult)>();
        private readonly List<IDisposable> owned = new List<IDisposable>();

        public Rig(BaseOperationsScenario scenario = null)
        {
            Authority = new A(scenario ?? A.NearStationsScenario());
            Authority.CommandCompleted += (p, r) => Answers.Add((p, r));
            Bridge = new BaseOperationsCommandBridge(Authority, Net.HostTransport);
            Bridge.ProtocolError += (c, code) => HostErrors.Add(code);
        }

        public BaseOperationsConnectionController Client(ISessionClientTransport transport = null)
        {
            var c = new BaseOperationsConnectionController(transport ?? Net.CreateClient());
            owned.Add(c);
            return c;
        }

        public BaseOperationsConnectionController Connected(ISessionClientTransport transport = null)
        {
            BaseOperationsConnectionController c = Client(transport);
            c.Connect();
            Pump();
            Assert.That(c.State, Is.EqualTo(SessionConnectionState.Connected));
            return c;
        }

        public LocalBaseOperationsSessionPort Local()
        {
            var p = new LocalBaseOperationsSessionPort(Authority);
            owned.Add(p);
            Assert.That(p.Join().IsJoined);
            return p;
        }

        public void Pump()
        {
            for (int i = 0; i < 3; i++)
            {
                Bridge.FlushSnapshots();
                Net.Pump();
            }
        }

        public void Step(double seconds, double dt = 0.1)
        {
            for (double left = seconds; left > 1e-9; left -= dt)
            {
                Authority.Tick(Math.Min(dt, left));
                Pump();
            }
        }

        public void Dispose()
        {
            foreach (IDisposable d in owned) d.Dispose();
            Bridge.Dispose();
        }
    }

    /// <summary>One participant, only through its port and its interaction client, like a player.</summary>
    private sealed class Driver
    {
        private readonly Rig rig;
        public readonly IBaseOperationsSessionPort Port;
        public readonly BaseOperationsInteractionClient Intents;
        public readonly List<CommandResult> Results = new List<CommandResult>();
        public readonly List<string> Answered = new List<string>();
        public readonly List<string> Events = new List<string>();
        private long sequence;

        public Driver(Rig rig, IBaseOperationsSessionPort port)
        {
            this.rig = rig;
            Port = port;
            Intents = new BaseOperationsInteractionClient(port, rig.Authority.Scenario);
            Intents.Answered += Answered.Add;
            port.CommandCompleted += Results.Add;
            port.SnapshotChanged += s => Events.Add("2:" + s.Revision);
            port.ExpeditionChanged += s => Events.Add("3:" + s.Revision);
            port.BaseOperationsChanged += s => Events.Add("4:" + s.Revision);
        }

        public PlayerId Me => Port.Membership.Player;

        public BaseOperationsSnapshot State
        {
            get
            {
                Assert.That(Port.TryGetBaseOperations(out BaseOperationsSnapshot s), Is.True);
                return s;
            }
        }

        public BasePosition Position => State.Outpost.Crew.Single(c => c.Player == Me).Pose.Position;

        public string Do(Func<BaseOperationsInteractionClient, bool> request)
        {
            int before = Answered.Count;
            Assert.That(request(Intents), Is.True, "the request was sent");
            rig.Pump();
            Assert.That(Answered.Count, Is.EqualTo(before + 1), "answered once");
            return Answered.Last();
        }

        /// <summary>Requests, then lets the authority run until the operation is no longer in progress.</summary>
        public void Complete(Func<BaseOperationsInteractionClient, bool> request)
        {
            Assert.That(Do(request), Is.EqualTo(OutpostReasons.Accepted));
            for (int i = 0; i < 1000 && State.TryGetOperation(Me, out _); i++) rig.Step(0.1);
            Assert.That(State.TryGetOperation(Me, out _), Is.False, "finished");
        }

        public void Move(MoveDirection direction) => Port.SubmitMovement(new MovementInput(State.Epoch, ++sequence, direction));

        public void Walk(BasePosition target)
        {
            for (int guard = 0; ; guard++)
            {
                Assert.That(guard, Is.LessThan(3000), $"{Me} stuck at {Position} going to {target}");
                double dx = target.X - Position.X, dy = target.Y - Position.Y, d = Math.Sqrt(dx * dx + dy * dy);
                if (d <= 0.02) { Move(MoveDirection.Zero); rig.Pump(); rig.Step(0.1); return; }
                Move(MoveDirection.FromAxes(dx / d, dy / d));
                rig.Pump();
                rig.Step(Math.Min(0.1, d / rig.Authority.Scenario.Expedition.Outpost.Movement.MaxSpeedMetersPerSecond), 0.1);
            }
        }
    }

    private static StationId Air => BaseOperationsFixtures.AirStation;
    private static StationId Console => BaseOperationsFixtures.LogisticsConsole;
    private static string Inlet => BaseOperationsFixtures.InletFilter;

    private static void Resume(Rig rig, Driver d)
    {
        Pumped(rig, d, () => d.Port.Submit(ExpeditionCommand.Session(Guid.NewGuid(), d.State.Epoch, ExpeditionAction.Resume)), out string reason);
        Assert.That(reason, Is.EqualTo(OutpostReasons.Accepted));
    }

    private static string Answer(Driver d, Action send)
    {
        int before = d.Results.Count;
        send();
        return d.Results.Count > before ? d.Results.Last().Reason : null;
    }

    private static void Pumped(Rig rig, Driver d, Action send, out string reason)
    {
        int before = d.Results.Count;
        send();
        rig.Pump();
        Assert.That(d.Results.Count, Is.EqualTo(before + 1));
        reason = d.Results.Last().Reason;
    }

    // ---- state, ordering and late join -------------------------------------------------------------------------

    [Test]
    public void RemoteClientsHoldExactlyTheAuthorityStateWithEventsInPhaseOrder()
    {
        using (var rig = new Rig())
        {
            var ana = new Driver(rig, rig.Local());                 // host that plays, slot 0 (console)
            var leo = new Driver(rig, rig.Connected().Session);    // slot 1 (console)
            var kai = new Driver(rig, rig.Connected().Session);    // slot 2 (air)
            Resume(rig, ana);
            rig.Pump();
            kai.Complete(i => i.RequestOperation(Air, BaseOperationAction.Inspect));
            leo.Complete(i => i.RequestPlaceOrder(Console, BaseOperationsFixtures.StandardManifest));
            Assert.That(kai.Do(i => i.RequestOperation(Air, BaseOperationAction.Intervene, OperationChoice.ReplaceInletFilter)),
                Is.EqualTo(OutpostReasons.Accepted));
            rig.Step(2);

            Assert.That(rig.Authority.TryGetSnapshot(out BaseOperationsSnapshot truth));
            foreach (Driver d in new[] { ana, leo, kai })
            {
                Assert.That(d.State.Revision, Is.EqualTo(truth.Revision));
                Assert.That(F.Of(d.State), Is.EqualTo(F.Of(truth)), "stocks, machines, spares, reservation, orders, operations, activities");
            }
            Assert.That(leo.State.Orders.Single().State, Is.EqualTo(OrderState.Pending));
            Assert.That(leo.State.FreeSpares(Inlet), Is.EqualTo(0), "the other client sees the reservation, not a copy of its own");
            Assert.That(leo.State.TryGetOperation(kai.Me, out OperationProgress op) && op.ReservedSpare == Inlet);

            // For every stored revision: phase 2, then phase 3, then phase 4, and nothing out of order or repeated.
            for (int k = 0; k + 2 < leo.Events.Count; k += 3)
            {
                string revision = leo.Events[k].Substring(2);
                Assert.That(leo.Events.Skip(k).Take(3), Is.EqualTo(new[] { "2:" + revision, "3:" + revision, "4:" + revision }));
            }
            Assert.That(leo.Events.Count % 3, Is.EqualTo(0));
        }
    }

    [Test]
    public void ALateJoinerGetsTheCurrentBaseBeforeItsMembershipNeverTheInitialOne()
    {
        using (var rig = new Rig())
        {
            var ana = new Driver(rig, rig.Local());
            var kai = new Driver(rig, rig.Connected().Session); // slot 1
            Resume(rig, ana);
            ana.Complete(i => i.RequestPlaceOrder(Console, BaseOperationsFixtures.StandardManifest));
            rig.Step(40); // past the cancel window: in transit
            BaseOperationsConnectionController late = rig.Client();
            BaseOperationsSnapshot atJoin = null;
            late.Session.MembershipChanged += m =>
            {
                if (m.IsJoined && late.Session.TryGetBaseOperations(out BaseOperationsSnapshot s) && s.Outpost.TryGetCrew(m.Player, out _)) atJoin = s;
            };
            late.Connect();
            rig.Pump();
            Assert.That(atJoin, Is.Not.Null, "Joined only with a snapshot that contains the newcomer");
            Assert.That(atJoin.SimulatedTimeSeconds, Is.GreaterThan(40), "the running clock, not time 0");
            Assert.That(atJoin.Orders.Single().State, Is.EqualTo(OrderState.InTransit), "the team's order in flight");
            Assert.That(atJoin.Activities.Single().Kind, Is.EqualTo(ActivityKind.Resupply));
            Assert.That(rig.Authority.TryGetSnapshot(out BaseOperationsSnapshot truth));
            Assert.That(F.Of(atJoin), Is.EqualTo(F.Of(truth)));
            Assert.That(kai.State.Outpost.Crew.Count, Is.EqualTo(3));
        }
    }

    [Test]
    public void PauseStopsTheClockForEveryClientAndOwnWorkCanStillBeCancelled()
    {
        using (var rig = new Rig())
        {
            var ana = new Driver(rig, rig.Connected().Session);
            var leo = new Driver(rig, rig.Connected().Session);
            var kai = new Driver(rig, rig.Connected().Session);
            Resume(rig, ana);
            rig.Pump();
            kai.Complete(i => i.RequestOperation(Air, BaseOperationAction.Inspect));
            Assert.That(kai.Do(i => i.RequestOperation(Air, BaseOperationAction.Intervene, OperationChoice.ReplaceInletFilter)), Is.EqualTo(OutpostReasons.Accepted));
            Pumped(rig, ana, () => ana.Port.Submit(ExpeditionCommand.Session(Guid.NewGuid(), ana.State.Epoch, ExpeditionAction.Pause)), out string paused);
            Assert.That(paused, Is.EqualTo(OutpostReasons.Accepted));
            double t = leo.State.SimulatedTimeSeconds;
            rig.Step(5);
            Assert.That(leo.State.SimulatedTimeSeconds, Is.EqualTo(t), "no client clock: time stops everywhere");
            Assert.That(kai.Do(i => i.RequestCancelOperation(Air)), Is.EqualTo(OutpostReasons.Accepted), "cancelling own work is allowed in pause");
            Assert.That(leo.State.FreeSpares(Inlet), Is.EqualTo(1), "the reservation is released for everyone");
            Assert.That(leo.State.Operations, Is.Empty);
            Assert.That(kai.Do(i => i.RequestOperation(Air, BaseOperationAction.Inspect)), Is.EqualTo(OutpostReasons.InvalidState));
        }
    }

    // ---- simultaneity, stamps, restart ---------------------------------------------------------------------------

    [Test]
    public void TwoClientsAtOneStationAtOnceStartOneOperation()
    {
        using (var rig = new Rig())
        {
            var ana = new Driver(rig, rig.Connected().Session); // slot 0, console
            var leo = new Driver(rig, rig.Connected().Session); // slot 1, console
            Resume(rig, ana);
            rig.Pump();
            Assert.That(ana.Intents.RequestPlaceOrder(Console, BaseOperationsFixtures.StandardManifest));
            Assert.That(leo.Intents.RequestPlaceOrder(Console, BaseOperationsFixtures.StandardManifest));
            rig.Pump(); // both in flight before either answer
            string[] answers = { ana.Answered.Single(), leo.Answered.Single() };
            Assert.That(answers.Count(a => a == OutpostReasons.Accepted), Is.EqualTo(1));
            Assert.That(answers, Has.Member(OutpostReasons.StationOccupied));
            Assert.That(leo.State.Operations.Count, Is.EqualTo(1));
            rig.Step(4);
            Assert.That(ana.State.Orders.Count, Is.EqualTo(1), "one order, not two");
        }
    }

    [Test]
    public void ADecisionInFlightWhenItsObservationExpiresIsRefusedAsStale()
    {
        using (var rig = new Rig())
        {
            var ana = new Driver(rig, rig.Connected().Session);
            rig.Connected();
            var kai = new Driver(rig, rig.Connected().Session); // slot 2, air
            Resume(rig, ana);
            rig.Pump();
            kai.Complete(i => i.RequestOperation(Air, BaseOperationAction.Inspect));
            Assert.That(kai.Intents.TryGetCurrentObservation(Air, out EquipmentObservation seen));
            Assert.That(kai.Intents.RequestOperation(Air, BaseOperationAction.Intervene, OperationChoice.ReplaceInletFilter));
            Assert.That(kai.Intents.Pending.ObservationSequence, Is.EqualTo(seen.Sequence), "stamped with what was seen");
            Assert.That(kai.Intents.Pending.ZoneSequence, Is.EqualTo(0));
            for (int i = 0; i < 620; i++) rig.Authority.Tick(0.1); // the observation window (60 s) closes before the intent arrives
            rig.Pump();
            Assert.That(kai.Answered.Last(), Is.EqualTo(BaseOperationReasons.StaleObservation));
            Assert.That(kai.Intents.HasPending, Is.False);
            Assert.That(kai.State.FreeSpares(Inlet), Is.EqualTo(1), "nothing reserved");
            Assert.That(kai.Intents.RequestOperation(Air, BaseOperationAction.Intervene, OperationChoice.ReplaceInletFilter), Is.False,
                "without a current observation the client sends no decision");
        }
    }

    [Test]
    public void RestartWhileARequestIsInFlightAnswersItForTheOldEpochAndNothingOldReachesTheNewSession()
    {
        using (var rig = new Rig())
        {
            var ana = new Driver(rig, rig.Local());
            BaseOperationsConnectionController leoLink = rig.Connected();
            var leo = new Driver(rig, leoLink.Session);
            Resume(rig, ana);
            ana.Complete(i => i.RequestPlaceOrder(Console, BaseOperationsFixtures.StandardManifest));
            rig.Step(1);
            var oldFrames = new List<byte[]>(rig.Net.HostFrames); // every frame sent so far: snapshots, results, memberships
            Assert.That(leo.Intents.RequestCancelOrder(Console, leo.State.Orders.Single().Sequence));
            Assert.That(Answer(ana, () => ana.Port.Submit(ExpeditionCommand.Session(Guid.NewGuid(), ana.State.Epoch, ExpeditionAction.Restart))),
                Is.EqualTo(OutpostReasons.Accepted)); // the host restarts before Leo's intent arrives
            rig.Pump();
            Assert.That(leo.Answered.Single(), Is.EqualTo(OutpostReasons.EpochMismatch), "an old-epoch intent is answered, never applied");
            Assert.That(leo.State.Epoch, Is.EqualTo(1));
            Assert.That(leo.State.Revision, Is.EqualTo(0), "Restart starts the new epoch at revision 0");
            Assert.That(leo.State.Orders, Is.Empty, "the new session starts from the scenario");
            Assert.That(leo.State.FreeSpares(Inlet), Is.EqualTo(1));
            string fresh = F.Of(leo.State);

            ulong connection = rig.Net.HostTransport.Links.Keys.Single();
            var leoAdapter = leoLink.Session;
            long discardedSnapshots = leoAdapter.DiscardedSnapshotCount, discardedResults = leoAdapter.DiscardedResultCount;
            foreach (byte[] frame in oldFrames) rig.Net.HostTransport.SendToClient(connection, frame); // replay the whole old epoch
            rig.Net.Pump();
            Assert.That(F.Of(leo.State), Is.EqualTo(fresh), "late snapshots and results of the old epoch change nothing");
            Assert.That(leoAdapter.DiscardedSnapshotCount, Is.GreaterThan(discardedSnapshots));
            Assert.That(leoAdapter.DiscardedResultCount, Is.GreaterThanOrEqualTo(discardedResults));
            Assert.That(leo.Answered.Count, Is.EqualTo(1), "no late answer reaches the interaction client");
            Assert.That(leoLink.State, Is.EqualTo(SessionConnectionState.Connected));
        }
    }

    // ---- duplicates, forgery, results only to their sender -------------------------------------------------------

    [Test]
    public void AReplayedOrForgedRequestIsNeverChargedTwice()
    {
        using (var rig = new Rig())
        {
            var ana = new Driver(rig, rig.Connected().Session);
            rig.Connected();
            InMemoryOutpostNetwork.Client kaiTransport = rig.Net.CreateClient();
            BaseOperationsConnectionController kaiLink = rig.Connected(kaiTransport);
            var kai = new Driver(rig, kaiLink.Session); // slot 2, air
            Resume(rig, ana);
            rig.Pump();
            kai.Complete(i => i.RequestOperation(Air, BaseOperationAction.Inspect));
            Assert.That(kai.Do(i => i.RequestOperation(Air, BaseOperationAction.Intervene, OperationChoice.ReplaceInletFilter)), Is.EqualTo(OutpostReasons.Accepted));
            byte[] intervene = kaiTransport.Sent.Last();
            int submits = rig.Authority.OperationSubmitCount;

            rig.Net.HostTransport.Inject(kaiTransport.ConnectionId, intervene); // the same bytes again on the same link
            rig.Net.HostTransport.Inject(rig.Net.HostTransport.Links.Keys.Min(), intervene); // and forged from Ana's link (the first one)
            rig.Pump();
            Assert.That(rig.Authority.OperationSubmitCount, Is.EqualTo(submits + 2), "both reached the authority");
            Assert.That(rig.Answers.Skip(rig.Answers.Count - 2).Select(a => a.result.Reason),
                Is.EqualTo(new[] { OutpostReasons.Accepted, OutpostReasons.CommandIdConflict }),
                "replay answered with the stored result; another sender is a conflict (identity comes from the link)");
            Assert.That(kaiLink.Session.DiscardedResultCount, Is.EqualTo(1), "the repeated result completes nothing on the client");
            Assert.That(kai.Answered.Count(a => a == OutpostReasons.Accepted), Is.EqualTo(2), "inspect + one intervention");
            Assert.That(ana.Results.Count(r => r.Reason == OutpostReasons.CommandIdConflict), Is.EqualTo(0),
                "a result for an id the client never sent is discarded, not shown");
            Assert.That(kai.State.Operations.Count, Is.EqualTo(1));

            rig.Step(13); // completes (12 s): the spare is consumed exactly once
            Assert.That(rig.Authority.SparesConsumed, Is.EqualTo(1));
            Assert.That(ana.State.Spares.Single(s => s.Key == Inlet).Count, Is.EqualTo(0));
            rig.Net.HostTransport.Inject(kaiTransport.ConnectionId, intervene);
            rig.Pump();
            Assert.That(rig.Authority.SparesConsumed, Is.EqualTo(1), "a replay after completion charges nothing");
        }
    }

    [Test]
    public void ResultsReachOnlyTheirSender()
    {
        using (var rig = new Rig())
        {
            var ana = new Driver(rig, rig.Connected().Session);
            var leo = new Driver(rig, rig.Connected().Session);
            var kai = new Driver(rig, rig.Connected().Session);
            Resume(rig, ana);
            rig.Pump();
            int leoResults = leo.Results.Count, anaResults = ana.Results.Count;
            Assert.That(kai.Do(i => i.RequestOperation(Air, BaseOperationAction.Inspect)), Is.EqualTo(OutpostReasons.Accepted));
            Assert.That(kai.Do(i => i.RequestPlaceOrder(Console, BaseOperationsFixtures.StandardManifest)), Is.EqualTo(OutpostReasons.PlayerBusy));
            Assert.That(leo.Results.Count, Is.EqualTo(leoResults));
            Assert.That(ana.Results.Count, Is.EqualTo(anaResults));
        }
    }

    // ---- leave, disconnect, reconnect, failed sends ----------------------------------------------------------------

    [Test]
    public void DisconnectingDuringAnInterventionLeavesOnceReleasesTheSpareAndKeepsTheOrders()
    {
        using (var rig = new Rig())
        {
            var ana = new Driver(rig, rig.Connected().Session);
            BaseOperationsConnectionController leoLink = rig.Connected();
            var leo = new Driver(rig, leoLink.Session);
            BaseOperationsConnectionController kaiLink = rig.Connected();
            var kai = new Driver(rig, kaiLink.Session);
            Resume(rig, ana);
            leo.Complete(i => i.RequestPlaceOrder(Console, BaseOperationsFixtures.StandardManifest));
            kai.Complete(i => i.RequestOperation(Air, BaseOperationAction.Inspect));
            Assert.That(kai.Do(i => i.RequestOperation(Air, BaseOperationAction.Intervene, OperationChoice.ReplaceInletFilter)), Is.EqualTo(OutpostReasons.Accepted));
            rig.Step(3);
            Assert.That(ana.State.FreeSpares(Inlet), Is.EqualTo(0));
            PlayerId kaiId = kai.Me, leoId = leo.Me;

            Assert.That(kai.Intents.RequestOperation(Air, BaseOperationAction.Inspect)); // in flight when the link closes
            kaiLink.Disconnect(); // no cancel: Leave alone must release the reservation
            rig.Pump();
            Assert.That(kai.Answered.Last(), Is.EqualTo(OutpostReasons.LocalDisconnect), "result unknown, never a success");
            Assert.That(kai.Intents.HasPending, Is.False);
            leoLink.Disconnect();
            rig.Pump();
            rig.Step(1);

            Assert.That(rig.Bridge.LeaveCount, Is.EqualTo(2));
            Assert.That(rig.Authority.Left, Is.EqualTo(new[] { kaiId, leoId }), "Leave exactly once per member");
            BaseOperationsSnapshot seen = ana.State;
            Assert.That(seen.Outpost.TryGetCrew(kaiId, out _), Is.False, "no ghost");
            Assert.That(seen.Operations, Is.Empty, "no phantom operation");
            Assert.That(seen.FreeSpares(Inlet), Is.EqualTo(1), "the reserved spare is free again");
            Assert.That(seen.Spares.Single(s => s.Key == Inlet).Count, Is.EqualTo(1), "and was not consumed");
            Assert.That(rig.Authority.SparesConsumed, Is.EqualTo(0));
            Assert.That(seen.Orders.Single().PlacedBy, Is.EqualTo(leoId), "the order of a member who left is kept");

            // Reconnection = a fresh join: a new identity, nothing resumed or duplicated.
            kaiLink.Connect();
            rig.Pump();
            Assert.That(kaiLink.Session.Membership.Player, Is.Not.EqualTo(kaiId));
            Assert.That(ana.State.Outpost.Crew.Count, Is.EqualTo(2));
            Assert.That(ana.State.Operations, Is.Empty);
            Assert.That(ana.State.FreeSpares(Inlet), Is.EqualTo(1));
        }
    }

    [Test]
    public void WalkingAwayEndsTheOperationThroughMovementIntentsOnly()
    {
        using (var rig = new Rig())
        {
            var ana = new Driver(rig, rig.Local());
            rig.Connected();
            var kai = new Driver(rig, rig.Connected().Session); // slot 2, air
            Resume(rig, ana);
            kai.Complete(i => i.RequestOperation(Air, BaseOperationAction.Inspect));
            Assert.That(kai.Do(i => i.RequestOperation(Air, BaseOperationAction.Intervene, OperationChoice.ReplaceInletFilter)), Is.EqualTo(OutpostReasons.Accepted));
            kai.Walk(new BasePosition(4.0, 8.5)); // out of the 1.2 m reach of the air unit
            Assert.That(ana.State.Operations, Is.Empty, "the authority ended it on the confirmed position");
            Assert.That(ana.State.FreeSpares(Inlet), Is.EqualTo(1));
            Assert.That(rig.Authority.SparesConsumed, Is.EqualTo(0));
            Assert.That(kai.Intents.Interact(), Is.False, "nothing of its own left to cancel");
        }
    }

    /// <summary>Client transport whose sends can be made to fail (as a full or closed Unity Transport queue throws).</summary>
    private sealed class FailingTransport : ISessionClientTransport
    {
        private readonly ISessionClientTransport inner;
        public bool Fail;
        public FailingTransport(ISessionClientTransport inner) => this.inner = inner;
        public event Action<long> Connected { add => inner.Connected += value; remove => inner.Connected -= value; }
        public event Action<long, string> Disconnected { add => inner.Disconnected += value; remove => inner.Disconnected -= value; }
        public event Action<long, byte[]> MessageReceived { add => inner.MessageReceived += value; remove => inner.MessageReceived -= value; }
        public void Connect(long link) => inner.Connect(link);
        public void Disconnect(long link) => inner.Disconnect(link);
        public void SendToHost(long link, byte[] payload)
        {
            if (Fail) throw new InvalidOperationException("Unity Transport BeginSend failed (simulated).");
            inner.SendToHost(link, payload);
        }
    }

    [Test]
    public void AFailedSendReleasesTheRequestAsUnknownAndEndsTheLink()
    {
        using (var rig = new Rig())
        {
            var ana = new Driver(rig, rig.Local());
            var failing = new FailingTransport(rig.Net.CreateClient());
            BaseOperationsConnectionController leoLink = rig.Connected(failing);
            var leo = new Driver(rig, leoLink.Session);
            Resume(rig, ana);
            rig.Pump();
            int submits = rig.Authority.OperationSubmitCount;
            var errors = new List<string>();
            leoLink.Session.ProtocolError += errors.Add;
            failing.Fail = true;
            bool sent = false;
            Assert.DoesNotThrow(() => sent = leo.Intents.RequestPlaceOrder(Console, BaseOperationsFixtures.StandardManifest));
            Assert.That(sent, "the request was made");
            Assert.That(leo.Answered.Single(), Is.EqualTo(OutpostReasons.ConnectionLost), "unknown, not Accepted and not a server rejection");
            Assert.That(leo.Intents.HasPending, Is.False, "the UI is released");
            Assert.That(leo.Results, Is.Empty, "no invented result");
            Assert.That(errors, Has.Member(SessionNetworkCodes.SendFailed));
            Assert.That(leoLink.State, Is.EqualTo(SessionConnectionState.ConnectionLost));
            Assert.That(leoLink.Session.Membership.IsJoined, Is.False);
            rig.Pump();
            Assert.That(rig.Authority.OperationSubmitCount, Is.EqualTo(submits), "nothing arrived");
            Assert.That(rig.Bridge.LeaveCount, Is.EqualTo(1), "the host saw the link close: one Leave");
            Assert.That(ana.State.Outpost.Crew.Count, Is.EqualTo(1));
            Assert.That(leo.Intents.RequestPlaceOrder(Console, BaseOperationsFixtures.StandardManifest), Is.False, "nothing sent without a link");
        }
    }

    [Test]
    public void ForeignOrMalformedCableMessagesAreRejectedOnBothSides()
    {
        using (var rig = new Rig())
        {
            BaseOperationsConnectionController link = rig.Connected();
            var leo = new Driver(rig, link.Session);
            string before = F.Of(leo.State);
            ulong connection = rig.Net.HostTransport.Links.Keys.Single();
            rig.Net.HostTransport.Inject(connection, new byte[] { 0x54, 0x45, 1, 1 });       // a phase 3 Join
            rig.Net.HostTransport.Inject(connection, new byte[] { 0x54, 0x42, 1, 4, 1, 2 }); // truncated phase 4 Operation
            rig.Net.HostTransport.Inject(connection, new byte[] { 0x54, 0x42, 1, 11 });      // a client cannot send snapshots
            rig.Net.HostTransport.Inject(connection, new byte[] { 0x54, 0x42, 1, 77 });      // unknown kind
            rig.Net.HostTransport.Inject(connection, new byte[300]);                         // oversize
            rig.Pump();
            Assert.That(rig.HostErrors, Is.EqualTo(new[] { SessionNetworkCodes.BadMagic, SessionNetworkCodes.MalformedPayload,
                SessionNetworkCodes.UnexpectedMessageKind, SessionNetworkCodes.UnknownMessageKind, SessionNetworkCodes.PayloadTooLarge }));
            Assert.That(rig.Authority.OperationSubmitCount + rig.Authority.SubmitCount, Is.EqualTo(0), "nothing reached the authority");

            var clientErrors = new List<string>();
            link.Session.ProtocolError += clientErrors.Add;
            rig.Net.HostTransport.SendToClient(connection, new byte[] { 0x54, 0x45, 1, 11 });    // phase 3 snapshot header
            rig.Net.HostTransport.SendToClient(connection, new byte[] { 0x54, 0x42, 1, 11, 0 }); // truncated phase 4 snapshot
            rig.Net.HostTransport.SendToClient(connection, new byte[] { 0x54, 0x42, 1, 13, 1, 0, 2, 0, 9 }); // chunk 1 of 2 first
            rig.Net.Pump();
            Assert.That(clientErrors, Is.EqualTo(new[] { SessionNetworkCodes.BadMagic, SessionNetworkCodes.MalformedPayload,
                SessionNetworkCodes.MalformedPayload }));
            Assert.That(link.State, Is.EqualTo(SessionConnectionState.Connected), "dropped messages never invent state");
            Assert.That(F.Of(leo.State), Is.EqualTo(before));
        }
    }

    [Test]
    public void SubmittingWithoutALinkIsAbandonedAsNotConnected()
    {
        using (var rig = new Rig())
        {
            BaseOperationsConnectionController link = rig.Client();
            var abandoned = new List<string>();
            link.Session.BaseOperationCommandAbandoned += (c, reason) => abandoned.Add(reason);
            link.Session.Submit(BaseOperationCommand.Operation(Guid.NewGuid(), 0, BaseOperationAction.Inspect, Air));
            Assert.That(abandoned, Is.EqualTo(new[] { OutpostReasons.NotConnected }));
            Assert.Throws<ArgumentException>(() => link.Session.Submit(new BaseOperationCommand(Guid.NewGuid(), 0,
                BaseOperationAction.PlaceOrder, Console, OperationChoice.None, "Not A Key")), "the cable cannot carry it");
            Assert.That(rig.Authority.OperationSubmitCount, Is.EqualTo(0));
        }
    }
}
