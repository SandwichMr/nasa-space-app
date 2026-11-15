using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Crew;
using Trainer.Application.Development;
using Trainer.Application.Outpost;
using Trainer.Simulation.LifeSupport;
using Trainer.Unity.Networking;
using Trainer.Unity.Networking.Outpost;

/// <summary>
/// D3 phase 2 networking end to end IN MEMORY: real bridge, real client adapter/controller, real wire format
/// and real roster, over an in-memory transport and the test authority double (not D2's authority).
/// No sockets, no processes, no second machine: see the handoff for what remains to run in Unity.
/// </summary>
public sealed class OutpostNetworkLoopbackTests
{
    private sealed class FailingSend : ISessionClientTransport
    {
        private readonly ISessionClientTransport inner;
        public bool Fail, EnqueueBeforeFailure;
        public Action DeliverBeforeFailure;
        public FailingSend(ISessionClientTransport inner) => this.inner = inner;
        public event Action<long> Connected { add => inner.Connected += value; remove => inner.Connected -= value; }
        public event Action<long, string> Disconnected { add => inner.Disconnected += value; remove => inner.Disconnected -= value; }
        public event Action<long, byte[]> MessageReceived { add => inner.MessageReceived += value; remove => inner.MessageReceived -= value; }
        public void Connect(long link) => inner.Connect(link);
        public void Disconnect(long link) => inner.Disconnect(link);
        public void SendToHost(long link, byte[] bytes)
        {
            if (!Fail) { inner.SendToHost(link, bytes); return; }
            if (EnqueueBeforeFailure) inner.SendToHost(link, bytes);
            DeliverBeforeFailure?.Invoke();
            throw new InvalidOperationException("Injected send failure; delivery may be unknown.");
        }
    }

    [Test] public void TerminalWaterSnapshotMatchesTheRealModelOnBothPortsWithoutAnotherTick()
    {
        var scenario = NearStationScenario();
        var model = new OutpostBaseSimulation(OutpostSimulationParameters.SyntheticTraining());
        var authority = new OutpostAuthority(scenario, new OutpostMissionRules(240, new[] {
            new OutpostObjectiveRule("water", new[] { OutpostFixtures.TreatWater }, 1, true)
        }), model, CrewRoster.For(scenario));
        var net = new InMemoryOutpostNetwork();
        using (var bridge = new OutpostCommandBridge(authority, net.HostTransport))
        using (var host = new LocalOutpostSessionPort(authority))
        using (var client = new OutpostConnectionController(net.CreateClient()))
        {
            void Pump() { for (int i = 0; i < 4; i++) { bridge.FlushSnapshots(); net.Pump(); } }
            host.Join(); client.Connect(); Pump();
            host.Submit(new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Resume));
            host.Submit(OutpostCommand.StartTask(Guid.NewGuid(), 0, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater));
            host.TryGetSnapshot(out var before);
            authority.Tick(20); Pump();
            host.TryGetSnapshot(out var local); client.Session.TryGetSnapshot(out var remote);
            Assert.That(local.State, Is.EqualTo(SessionState.Completed));
            foreach (var observed in new[] { local, remote })
            {
                Assert.That(observed.Resources.LifeSupport.Water.PotableKilograms, Is.EqualTo(model.Snapshot.LifeSupport.Water.PotableKilograms));
                Assert.That(observed.Resources.LifeSupport.Water.UntreatedKilograms, Is.EqualTo(model.Snapshot.LifeSupport.Water.UntreatedKilograms));
            }
            Assert.That(model.Ledger.TransferBoundaryOutflowKilograms, Is.EqualTo(.2), "Exactly one real Apply.");
            Assert.That(local.Revision, Is.EqualTo(before.Revision + 1));
            Assert.That(before.Resources.LifeSupport.Water.PotableKilograms, Is.EqualTo(20), "The earlier snapshot is immutable.");
            authority.Join(); Pump(); host.TryGetSnapshot(out var afterJoin);
            Assert.That(afterJoin.Resources.LifeSupport.Water.PotableKilograms, Is.EqualTo(local.Resources.LifeSupport.Water.PotableKilograms));
            Assert.That(afterJoin.SimulatedTimeSeconds, Is.EqualTo(local.SimulatedTimeSeconds));
        }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void SendFailureTerminatesOnlyItsPendingRequestAndLateResultsDoNotReviveIt(bool enqueued, bool answered)
    {
        var scenario = NearStationScenario();
        var authority = new OutpostAuthority(scenario, new OutpostMissionRules(240, new[] {
            new OutpostObjectiveRule("water", new[] { OutpostFixtures.TreatWater }, 2, true)
        }), new OutpostBaseSimulation(OutpostSimulationParameters.SyntheticTraining()), CrewRoster.For(scenario));
        var net = new InMemoryOutpostNetwork();
        using (var bridge = new OutpostCommandBridge(authority, net.HostTransport))
        {
            void Pump() { for (int i = 0; i < 4; i++) { bridge.FlushSnapshots(); net.Pump(); } }
            var raw = net.CreateClient();
            var fault = new FailingSend(raw);
            using (var controller = new OutpostConnectionController(fault))
            {
                controller.Connect(); Pump();
                var port = controller.Session;
                port.Submit(new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Resume)); Pump();
                using (var interaction = new StationInteractionClient(port, scenario))
                {
                    int completed = 0, abandoned = 0;
                    var abandonmentReasons = new List<string>();
                    interaction.Completed += (c, r) => completed++;
                    interaction.Abandoned += (c, r) => { abandoned++; abandonmentReasons.Add(r); };
                    fault.Fail = true; fault.EnqueueBeforeFailure = enqueued;
                    fault.DeliverBeforeFailure = answered ? (Action)net.Pump : null;
                    Assert.Throws<InvalidOperationException>(() => interaction.RequestStart(OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater));
                    Assert.That(interaction.Pending, Is.Null, "The UI must not keep an orphan pending.");
                    Assert.That(port.PendingCommandCount, Is.EqualTo(0), "No negative counter after a synchronous result.");
                    Assert.That(completed, Is.EqualTo(answered ? 1 : 0));
                    Assert.That(abandoned, Is.EqualTo(answered ? 0 : 1), "Unknown delivery is abandoned, never a fabricated server rejection.");
                    Assert.That(abandonmentReasons, Is.EqualTo(answered ? Array.Empty<string>() : new[] { OutpostReasons.ConnectionLost }));
                    Assert.That(interaction.LastResult == null, Is.EqualTo(!answered));
                    fault.Fail = false; Pump();
                    Assert.That(completed + abandoned, Is.EqualTo(1), "A late result cannot complete an abandoned request.");
                    Assert.That(interaction.RequestCancel(OutpostFixtures.WaterProcessor), Is.True, "The UI remains usable.");
                    controller.Disconnect(); Pump();
                    Assert.That(interaction.Pending, Is.Null);
                    Assert.That(port.PendingCommandCount, Is.EqualTo(0));
                    Assert.That(completed + abandoned, Is.EqualTo(2), "Link loss terminates the new request exactly once.");
                    Assert.That(abandonmentReasons.Last(), Is.EqualTo(OutpostReasons.LocalDisconnect));
                    controller.Connect(); Pump();
                    Assert.That(interaction.RequestStart(OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater), Is.True);
                    Pump();
                    Assert.That(interaction.Pending, Is.Null, "Reconnect can request again without recreating the interaction.");
                }
            }
        }
    }

    private sealed class Rig
    {
        public readonly InMemoryOutpostNetwork Net = new InMemoryOutpostNetwork();
        public readonly CrewTestAuthority Authority;
        public readonly OutpostCommandBridge Bridge;
        public readonly List<(ulong, string)> HostErrors = new List<(ulong, string)>();

        public Rig(OutpostScenario scenario = null)
        {
            Authority = new CrewTestAuthority(scenario);
            Bridge = new OutpostCommandBridge(Authority, Net.HostTransport);
            Bridge.ProtocolError += (id, code) => HostErrors.Add((id, code));
        }

        public Player Connect()
        {
            var player = new Player(this);
            player.Controller.Connect();
            Pump();
            return player;
        }

        /// <summary>One "frame": coalesced snapshots go out, then everything is delivered.</summary>
        public void Pump()
        {
            for (int i = 0; i < 4; i++)
            {
                Bridge.FlushSnapshots();
                Net.Pump();
            }
        }

        public void Tick(double seconds)
        {
            Authority.Tick(seconds);
            Pump();
        }
    }

    private sealed class Player
    {
        public readonly InMemoryOutpostNetwork.Client Transport;
        public readonly OutpostConnectionController Controller;
        public readonly CrewInputSender Input;
        public readonly List<CommandResult> Results = new List<CommandResult>();
        public readonly List<string> Abandoned = new List<string>();
        public readonly List<string> Errors = new List<string>();

        public Player(Rig rig)
        {
            Transport = rig.Net.CreateClient();
            Controller = new OutpostConnectionController(Transport);
            Input = new CrewInputSender(Port);
            Port.CommandCompleted += Results.Add;
            Port.CommandAbandoned += (c, r) => Abandoned.Add(r);
            Port.ProtocolError += Errors.Add;
        }

        public OutpostNetworkSessionAdapter Port => Controller.Session;
        public PlayerId Id => Port.Membership.Player;

        public OutpostSessionSnapshot View
        {
            get
            {
                Assert.That(Port.TryGetSnapshot(out OutpostSessionSnapshot s), Is.True);
                return s;
            }
        }

        public CrewMemberState Sees(PlayerId id)
        {
            Assert.That(View.TryGetCrew(id, out CrewMemberState m), Is.True, $"{id} visible");
            return m;
        }
    }

    /// <summary>Fixture rooms with the water processor next to the spawn points (synthetic, for reach tests).</summary>
    private static OutpostScenario NearStationScenario()
    {
        OutpostScenario f = OutpostFixtures.Scenario();
        var layout = new OutpostLayout(f.Layout.Bounds, f.Layout.Obstacles, new[]
        {
            new StationPlacement(OutpostFixtures.WaterProcessor, new BasePosition(3.75, 3.75), 1.2),
            new StationPlacement(OutpostFixtures.ScienceBench, new BasePosition(13, 8), 1.2),
            new StationPlacement(OutpostFixtures.AirMonitor, new BasePosition(13, 2), 1.2)
        }, f.Layout.SpawnPoints);
        return new OutpostScenario("fixture-near-station", f.Provenance, layout, f.Movement, f.Tasks, f.Simulation);
    }

    private static void Resume(Rig rig, Player who)
    {
        who.Port.Submit(new OutpostCommand(Guid.NewGuid(), who.View.Epoch, OutpostAction.Resume));
        rig.Pump();
        Assert.That(who.Results.Last().Accepted, Is.True);
    }

    [Test] public void TwoClientsJoinSeeEachOtherAndOnlyTheLocalOneMoves()
    {
        var rig = new Rig();
        Player a = rig.Connect(), b = rig.Connect();
        Assert.That(a.Controller.State, Is.EqualTo(SessionConnectionState.Connected));
        Assert.That(b.Controller.State, Is.EqualTo(SessionConnectionState.Connected));
        Assert.That(a.Id, Is.Not.EqualTo(b.Id), "Each connection has its own authority-assigned identity.");
        Assert.That(a.View.Crew.Count, Is.EqualTo(2));
        Assert.That(b.View.Crew.Count, Is.EqualTo(2), "Both astronauts are visible to both players.");
        Resume(rig, a);

        BasePosition bStart = b.Sees(b.Id).Pose.Position;
        BasePosition aStart = a.Sees(a.Id).Pose.Position;
        a.Input.Update(1, 1); // A holds up-right.
        rig.Pump();
        rig.Tick(0.5);

        CrewMemberState aSeenByB = b.Sees(a.Id);
        Assert.That(aSeenByB.Pose.Position.DistanceTo(aStart), Is.EqualTo(1.0).Within(1e-9),
            "B sees A walk 2 m/s * 0.5 s, diagonal normalised.");
        Assert.That(aSeenByB.Pose.Motion, Is.EqualTo(CrewMotion.Walking));
        Assert.That(a.Sees(a.Id).Pose.Position, Is.EqualTo(aSeenByB.Pose.Position), "Same confirmed position on both clients.");
        Assert.That(b.Sees(b.Id).Pose.Position, Is.EqualTo(bStart), "B did not press anything and did not move.");
        Assert.That(a.Sees(a.Id).LastInputSequence, Is.EqualTo(1));
    }

    [Test] public void LateJoinerGetsTheCurrentStateBeforeJoined()
    {
        var rig = new Rig();
        Player a = rig.Connect();
        Resume(rig, a);
        a.Input.Update(1, 0);
        rig.Pump();
        rig.Tick(1);
        BasePosition aNow = a.Sees(a.Id).Pose.Position;

        var c = new Player(rig);
        bool stateBeforeJoined = false;
        c.Port.MembershipChanged += m =>
        {
            if (m.IsJoined) stateBeforeJoined = c.Port.TryGetSnapshot(out OutpostSessionSnapshot s) && s.TryGetCrew(m.Player, out _);
        };
        c.Controller.Connect();
        rig.Pump();
        Assert.That(stateBeforeJoined, Is.True);
        Assert.That(c.Sees(a.Id).Pose.Position, Is.EqualTo(aNow), "The newcomer sees A where A already is.");
        Assert.That(c.View.State, Is.EqualTo(SessionState.Running));
    }

    [Test] public void ClientsCannotTeleportForgeStateOrActForOthers()
    {
        var rig = new Rig(NearStationScenario());
        Player a = rig.Connect(), b = rig.Connect();
        Resume(rig, a);
        a.Input.Update(1, 0);
        rig.Pump();
        byte[] movement = a.Transport.Sent.Last();
        BasePosition before = a.Sees(a.Id).Pose.Position;

        // Forged movement with magnitude 5 (a "teleport" attempt) and NaN: rejected before the authority.
        byte[] fast = (byte[])movement.Clone();
        BitConverter.GetBytes(5.0).CopyTo(fast, 20);
        byte[] nan = (byte[])movement.Clone();
        BitConverter.GetBytes(double.NaN).CopyTo(nan, 20);
        rig.Net.HostTransport.Inject(a.Transport.ConnectionId, fast);
        rig.Net.HostTransport.Inject(a.Transport.ConnectionId, nan);
        // B replaying A's membership (claiming A's identity) and a whole host snapshot (claiming positions).
        rig.Net.HostTransport.Inject(b.Transport.ConnectionId, rig.Net.HostFrames.First(f => f[3] == 12));
        rig.Net.HostTransport.Inject(b.Transport.ConnectionId, rig.Net.HostFrames.First(f => f[3] == 11));
        rig.Pump();
        Assert.That(rig.HostErrors.Select(e => e.Item2), Is.EqualTo(new[]
        {
            SessionNetworkCodes.MalformedPayload, SessionNetworkCodes.MalformedPayload,
            SessionNetworkCodes.UnexpectedMessageKind, SessionNetworkCodes.PayloadTooLarge
        }));
        Assert.That(rig.HostErrors[2].Item1, Is.EqualTo(b.Transport.ConnectionId), "Blamed on the real sender.");
        Assert.That(rig.Bridge.TryGetPlayer(b.Transport.ConnectionId, out PlayerId stillB) && stillB == b.Id, Is.True,
            "Nothing a payload says changes the identity bound to a connection.");
        Assert.That(a.Sees(a.Id).Pose.Position, Is.EqualTo(before), "No position changed without a tick.");

        // A starts the task next to the spawn; B tries to cancel A's task: the authority sees B as the sender.
        a.Port.Submit(OutpostCommand.StartTask(Guid.NewGuid(), a.View.Epoch, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater));
        a.Input.Update(0, 0);
        rig.Pump();
        Assert.That(a.Results.Last().Accepted, Is.True);
        b.Port.Submit(OutpostCommand.CancelTask(Guid.NewGuid(), b.View.Epoch, OutpostFixtures.WaterProcessor));
        rig.Pump();
        Assert.That(b.Results.Last().Reason, Is.EqualTo(OutpostReasons.NoActiveTask));
        Assert.That(a.Results.Count, Is.EqualTo(2), "B's result never reaches A (A only got Resume and Start).");
        b.View.TryGetStation(OutpostFixtures.WaterProcessor, out StationState station);
        Assert.That(station.Occupant, Is.EqualTo(a.Id));
    }

    [Test] public void ResultsAreRoutedOnlyToTheirSenderOnTheWire()
    {
        var rig = new Rig();
        Player a = rig.Connect(), b = rig.Connect();
        int aFrames = a.Transport.Received.Count;
        var command = new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Resume);
        b.Port.Submit(command);
        rig.Net.Pump(); // Results only, no snapshot flush yet.
        Assert.That(b.Results.Single().CommandId, Is.EqualTo(command.Id));
        Assert.That(a.Transport.Received.Skip(aFrames).Any(f => f[3] == 10), Is.False,
            "A's link never carries B's result (not merely filtered by A's adapter).");
        Assert.That(a.Port.DiscardedResultCount, Is.EqualTo(0));
    }

    [Test] public void RepeatedJoinOnOneConnectionNeverDuplicatesTheMember()
    {
        var rig = new Rig();
        Player a = rig.Connect();
        PlayerId id = a.Id;
        byte[] join = a.Transport.Sent.First(f => f[3] == 1);
        rig.Net.HostTransport.Inject(a.Transport.ConnectionId, join);
        rig.Net.HostTransport.Inject(a.Transport.ConnectionId, join);
        rig.Pump();
        rig.Authority.TryGetSnapshot(out OutpostSessionSnapshot s);
        Assert.That(s.Crew.Count, Is.EqualTo(1));
        Assert.That(a.Id, Is.EqualTo(id));
        Assert.That(rig.Bridge.JoinedClientCount, Is.EqualTo(1));
    }

    [Test] public void MembershipIsJoinedOnlyOnceTheStateArrived()
    {
        // Record what a real bridge sends to a joiner, then deliver it to another client in the wrong order.
        var source = new Rig();
        Player first = source.Connect();
        byte[] membership = first.Transport.Received.First(f => f[3] == 12);
        byte[] state = first.Transport.Received.First(f => f[3] == 11);

        var net = new InMemoryOutpostNetwork(); // No bridge: frames are delivered by hand.
        var controller = new OutpostConnectionController(net.CreateClient());
        controller.Connect();
        net.Pump();
        Assert.That(controller.State, Is.EqualTo(SessionConnectionState.Synchronizing));
        ulong id = net.HostTransport.Links.Keys.Single();
        net.HostTransport.SendToClient(id, membership);
        net.Pump();
        Assert.That(controller.Session.Membership.IsJoined, Is.False, "No state yet: not joined.");
        Assert.That(controller.State, Is.EqualTo(SessionConnectionState.Synchronizing));
        net.HostTransport.SendToClient(id, state);
        net.Pump();
        Assert.That(controller.Session.Membership.IsJoined, Is.True);
        Assert.That(controller.State, Is.EqualTo(SessionConnectionState.Connected));
        Assert.That(controller.Session.TryGetSnapshot(out OutpostSessionSnapshot s) && s.TryGetCrew(controller.Session.Membership.Player, out _));
    }

    [Test] public void TwoPlayersRaceForOneStationAndTheAuthorityPicksTheFirst()
    {
        var rig = new Rig(NearStationScenario());
        Player a = rig.Connect(), b = rig.Connect();
        Resume(rig, a);
        var ai = new StationInteractionClient(a.Port, rig.Authority.Scenario);
        var bi = new StationInteractionClient(b.Port, rig.Authority.Scenario);
        Assert.That(ai.Interact(), Is.True);
        Assert.That(bi.Interact(), Is.True, "Both are in reach and both ask.");
        rig.Pump();
        Assert.That(ai.LastResult.Accepted, Is.True);
        Assert.That(bi.LastResult.Reason, Is.EqualTo(OutpostReasons.StationOccupied));
        a.View.TryGetStation(OutpostFixtures.WaterProcessor, out StationState seenByA);
        b.View.TryGetStation(OutpostFixtures.WaterProcessor, out StationState seenByB);
        Assert.That(seenByA.Occupant, Is.EqualTo(a.Id));
        Assert.That(seenByB.Occupant, Is.EqualTo(a.Id), "Both clients agree on who holds the station.");

        // Walking away cancels it (reach is checked on the authority's confirmed position).
        a.Input.Update(1, 0);
        rig.Pump();
        rig.Tick(2);
        b.View.TryGetStation(OutpostFixtures.WaterProcessor, out StationState after);
        Assert.That(after.Availability, Is.EqualTo(StationAvailability.Available));
        Assert.That(after.LastEnd.Reason, Is.EqualTo(TaskEndReason.PerformerMovedAway));
    }

    [Test] public void DisconnectLeavesOnceFreesTheStationAndRejoinIsANewIdentity()
    {
        var rig = new Rig(NearStationScenario());
        Player a = rig.Connect(), b = rig.Connect();
        Resume(rig, a);
        var bi = new StationInteractionClient(b.Port, rig.Authority.Scenario);
        bi.Interact();
        rig.Pump();
        Assert.That(bi.LastResult.Accepted, Is.True);
        PlayerId oldB = b.Id;
        b.Port.Submit(new OutpostCommand(Guid.NewGuid(), b.View.Epoch, OutpostAction.Pause)); // Still in flight.
        b.Controller.Disconnect();
        rig.Pump();

        Assert.That(b.Abandoned, Is.EqualTo(new[] { OutpostReasons.LocalDisconnect }), "Pending: result unknown.");
        Assert.That(b.Port.Membership.Status, Is.EqualTo(MembershipStatus.Left));
        Assert.That(rig.Authority.Left, Is.EqualTo(new[] { oldB }), "Leave called exactly once.");
        Assert.That(a.View.Crew.Count, Is.EqualTo(1), "No ghost astronaut.");
        a.View.TryGetStation(OutpostFixtures.WaterProcessor, out StationState freed);
        Assert.That(freed.Availability, Is.EqualTo(StationAvailability.Available), "No orphan reservation.");
        Assert.That(freed.LastEnd.Reason, Is.EqualTo(TaskEndReason.PerformerLeft));

        b.Controller.Connect();
        rig.Pump();
        Assert.That(b.Controller.State, Is.EqualTo(SessionConnectionState.Connected));
        Assert.That(b.Id, Is.Not.EqualTo(oldB), "Rejoin gets a new PlayerId.");
        Assert.That(b.Sees(b.Id).Slot, Is.EqualTo(1), "The free slot is reused.");
        Assert.That(a.View.Crew.Count, Is.EqualTo(2), "Exactly two astronauts after the rejoin.");

        // Host-side close (e.g. protocol violation) also leaves exactly once.
        rig.Net.HostTransport.DisconnectClient(b.Transport.ConnectionId, SessionNetworkCodes.ProtocolViolation);
        rig.Pump();
        Assert.That(b.Controller.State, Is.EqualTo(SessionConnectionState.ConnectionLost));
        Assert.That(rig.Authority.Left.Count, Is.EqualTo(2));
        Assert.That(a.View.Crew.Count, Is.EqualTo(1));
    }

    [Test] public void RestartReturnsEveryoneToSpawnAndHeldInputIsResent()
    {
        var rig = new Rig();
        Player a = rig.Connect();
        Resume(rig, a);
        a.Input.Update(1, 0);
        rig.Pump();
        rig.Tick(1);
        Assert.That(a.Sees(a.Id).Pose.Position, Is.Not.EqualTo(rig.Authority.Scenario.Layout.SpawnPoints[0]));
        byte[] oldEpochMove = a.Transport.Sent.Last();

        a.Port.Submit(new OutpostCommand(Guid.NewGuid(), a.View.Epoch, OutpostAction.Restart));
        rig.Pump();
        Assert.That(a.View.Epoch, Is.EqualTo(1));
        Assert.That(a.Sees(a.Id).Pose.Position, Is.EqualTo(rig.Authority.Scenario.Layout.SpawnPoints[0]));
        long ignoredBefore = rig.Bridge.IgnoredMovementCount;
        rig.Net.HostTransport.Inject(a.Transport.ConnectionId, oldEpochMove);
        rig.Pump();
        Assert.That(rig.Bridge.IgnoredMovementCount, Is.EqualTo(ignoredBefore + 1), "Old-epoch movement is ignored.");

        Resume(rig, a);
        Assert.That(a.Input.Update(1, 0), Is.True, "Same keys, new epoch: the direction is sent again.");
        rig.Pump();
        rig.Tick(0.5);
        Assert.That(a.Sees(a.Id).Pose.Position.X, Is.GreaterThan(rig.Authority.Scenario.Layout.SpawnPoints[0].X));
    }

    [Test] public void BlockedInputStopsTheAstronautOnEveryClient()
    {
        var rig = new Rig();
        Player a = rig.Connect(), b = rig.Connect();
        Resume(rig, a);
        a.Input.Update(0, 1);
        rig.Pump();
        rig.Tick(0.25);
        var panel = new object();
        a.Input.Block(panel);
        a.Input.Update(0, 1);
        rig.Pump();
        BasePosition stopped = b.Sees(a.Id).Pose.Position;
        rig.Tick(1);
        Assert.That(b.Sees(a.Id).Pose.Position, Is.EqualTo(stopped));
        Assert.That(b.Sees(a.Id).Pose.Motion, Is.EqualTo(CrewMotion.Idle));
    }

    [Test] public void AFullBaseRefusesTheJoinWithoutAGhost()
    {
        var rig = new Rig();
        for (int i = 0; i < rig.Authority.Scenario.Layout.MaxCrew; i++) rig.Connect();
        Player extra = rig.Connect();
        Assert.That(extra.Controller.State, Is.EqualTo(SessionConnectionState.ConnectionLost));
        Assert.That(extra.Controller.LastDisconnectReason, Is.EqualTo(OutpostReasons.SessionFull));
        Assert.That(extra.Port.Membership.Status, Is.EqualTo(MembershipStatus.Rejected));
        Assert.That(OutpostConnectionStatus.Describe(extra.Controller.State, extra.Controller.LastDisconnectReason),
            Does.Contain("completa"));
        rig.Authority.TryGetSnapshot(out OutpostSessionSnapshot s);
        Assert.That(s.Crew.Count, Is.EqualTo(rig.Authority.Scenario.Layout.MaxCrew));
        extra.Port.Submit(new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Pause));
        Assert.That(extra.Abandoned, Is.EqualTo(new[] { OutpostReasons.NotConnected }));
    }

    [Test] public void LargeSnapshotsTravelInChunksBelowTheTransportLimit()
    {
        OutpostScenario f = OutpostFixtures.Scenario();
        var stations = Enumerable.Range(0, OutpostLimits.MaxStations)
            .Select(i => new StationPlacement(new StationId("station-with-a-long-name-" + i.ToString("00")),
                new BasePosition(0.5 + i * 0.45, 9.5), 0.4)).ToArray();
        var spawns = Enumerable.Range(0, OutpostLimits.MaxCrew).Select(i => new BasePosition(1 + i * 0.8, 2)).ToArray();
        var layout = new OutpostLayout(f.Layout.Bounds, f.Layout.Obstacles, stations, spawns);
        var tasks = stations.Select((s, i) => new TaskDefinition(new TaskTypeId("task-" + i), s.Id, TaskCategory.Support, 5, 0,
            ResourceEffect.None));
        var big = new OutpostScenario("fixture-many-stations", f.Provenance, layout, f.Movement, tasks, f.Simulation);
        var rig = new Rig(big);
        var players = Enumerable.Range(0, OutpostLimits.MaxCrew).Select(_ => rig.Connect()).ToList();

        Assert.That(rig.Net.HostFrames.Max(frame => frame.Length), Is.LessThanOrEqualTo(1100));
        Assert.That(rig.Net.HostFrames.Any(frame => frame[3] == 13), Is.True, "Chunk frames were needed.");
        foreach (Player p in players)
        {
            Assert.That(p.Controller.State, Is.EqualTo(SessionConnectionState.Connected));
            Assert.That(p.View.Stations.Count, Is.EqualTo(OutpostLimits.MaxStations));
            Assert.That(p.View.Crew.Count, Is.EqualTo(OutpostLimits.MaxCrew));
            Assert.That(p.Errors, Is.Empty);
        }
    }

    [Test] public void CorruptedHostFramesAreDroppedWithoutChangingState()
    {
        var rig = new Rig();
        Player a = rig.Connect();
        OutpostSessionSnapshot before = a.View;
        ulong id = a.Transport.ConnectionId;
        byte[] snapshotFrame = rig.Net.HostFrames.Last(f => f[3] == 11);
        byte[] truncated = snapshotFrame.Take(snapshotFrame.Length - 3).ToArray();
        byte[] phaseOne = { 0x54, 0x53, 1, 4 }; // Phase 1 header: not this protocol.
        byte[] orphanChunk = { 0x54, 0x4F, 1, 13, 1, 0, 2, 0, 9, 9 }; // Chunk 1 of 2 without chunk 0.
        foreach (byte[] bad in new[] { truncated, phaseOne, orphanChunk, new byte[0] })
            rig.Net.HostTransport.SendToClient(id, bad);
        rig.Net.Pump();
        Assert.That(a.Errors, Is.EqualTo(new[]
        {
            SessionNetworkCodes.MalformedPayload, SessionNetworkCodes.BadMagic, SessionNetworkCodes.MalformedPayload,
            SessionNetworkCodes.EmptyPayload
        }));
        Assert.That(a.View, Is.SameAs(before));
        Assert.That(a.Controller.State, Is.EqualTo(SessionConnectionState.Connected));
    }

    [Test] public void HostLocalPlayerIsAnOrdinaryMember()
    {
        var rig = new Rig();
        var local = new LocalOutpostSessionPort(rig.Authority);
        Assert.That(local.Join().IsJoined, Is.True);
        Player remote = rig.Connect();
        Assert.That(remote.View.Crew.Count, Is.EqualTo(2));
        var hostInput = new CrewInputSender(local);
        local.Submit(new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Resume));
        hostInput.Update(1, 0);
        rig.Tick(0.5);
        Assert.That(remote.Sees(local.Membership.Player).Pose.Motion, Is.EqualTo(CrewMotion.Walking),
            "The remote player sees the host's astronaut walk.");
        PlayerId hostId = local.Membership.Player;
        local.Dispose();
        rig.Pump();
        Assert.That(rig.Authority.Left, Is.EqualTo(new[] { hostId }));
        Assert.That(remote.View.Crew.Count, Is.EqualTo(1));
    }

    [Test] public void ReplacingTheBridgeMakesEveryRemoteMemberLeave()
    {
        var rig = new Rig();
        Player a = rig.Connect(), b = rig.Connect();
        rig.Bridge.Dispose();
        Assert.That(rig.Authority.Left, Is.EquivalentTo(new[] { a.Id, b.Id }));
        rig.Authority.TryGetSnapshot(out OutpostSessionSnapshot s);
        Assert.That(s.Crew, Is.Empty);
    }
}
