// No test asmdef exists, so this file compiles into Assembly-CSharp. In the Editor that assembly defines
// UNITY_INCLUDE_TESTS and references NUnit; normal player builds do neither, hence the guard.
// These tests use an in-memory loopback and FakeSessionPort: they verify the adapter's protocol,
// correlation and filtering logic, NOT real networking, an SDK, latency or two real clients.
#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Simulation;
using Trainer.Unity.Networking;

public sealed class NetworkSessionAdapterTests
{
    // ---- In-memory loopback (test fixture only; reliable, ordered, delivered when pumped) ----

    private sealed class Loopback
    {
        private readonly Queue<Action> deliveries = new Queue<Action>();
        private ulong nextConnectionId = 1;
        public readonly HostEnd Host;

        public Loopback() { Host = new HostEnd(this); }
        public ulong NextConnectionId() => nextConnectionId++;
        public void Enqueue(Action delivery) => deliveries.Enqueue(delivery);

        public void Pump()
        {
            int guard = 0;
            while (deliveries.Count > 0)
            {
                Assert.That(++guard, Is.LessThan(10000), "Loopback did not settle.");
                deliveries.Dequeue()();
            }
        }
    }

    private sealed class HostEnd : ISessionHostTransport
    {
        private readonly Loopback net;
        private readonly Dictionary<ulong, ClientEnd> clients = new Dictionary<ulong, ClientEnd>();
        public readonly List<(ulong To, byte[] Payload)> Sent = new List<(ulong, byte[])>();
        public bool FailResults;
        public readonly HashSet<ulong> FailSendsTo = new HashSet<ulong>();
        public event Action<ulong> ClientConnected;
        public event Action<ulong> ClientDisconnected;
        public event Action<ulong, byte[]> MessageReceived;

        public HostEnd(Loopback net) { this.net = net; }

        public void SendToClient(ulong connectionId, byte[] payload)
        {
            if (FailResults && payload[3] == 3) throw new InvalidOperationException("Result send failure.");
            if (FailSendsTo.Contains(connectionId)) throw new InvalidOperationException("Simulated send failure.");
            Sent.Add((connectionId, payload));
            if (clients.TryGetValue(connectionId, out ClientEnd client))
            {
                byte[] copy = (byte[])payload.Clone();
                net.Enqueue(() => client.Deliver(connectionId, copy));
            }
        }

        public void DisconnectClient(ulong connectionId, string reason)
        {
            if (clients.TryGetValue(connectionId, out ClientEnd client)) client.SimulateLinkLoss(reason);
            Drop(connectionId);
        }

        public void Accept(ClientEnd client, ulong connectionId)
        {
            clients[connectionId] = client;
            ClientConnected?.Invoke(connectionId);
        }

        public void Drop(ulong connectionId)
        {
            if (clients.Remove(connectionId)) ClientDisconnected?.Invoke(connectionId);
        }

        public void Deliver(ulong from, byte[] payload) => MessageReceived?.Invoke(from, payload);
    }

    /// <summary>One open link at a time. By default a pending connect ignores cancellation (a racy SDK),
    /// so the controller itself must tear down an unwanted link when it comes up.</summary>
    private sealed class ClientEnd : ISessionClientTransport
    {
        private readonly Loopback net;
        private long openLink;
        public ulong ConnectionId { get; private set; }
        public byte[] LastDelivered { get; private set; }
        public readonly List<byte[]> Sent = new List<byte[]>();
        public bool DeferDisconnectEvent;
        public bool ThrowOnConnect;
        public bool ThrowOnSend;
        public bool ThrowOnDisconnect;
        public event Action<long> Connected;
        public event Action<long, string> Disconnected;
        public event Action<long, byte[]> MessageReceived;

        public ClientEnd(Loopback net) { this.net = net; }

        public long OpenLink => openLink;

        public void Connect(long link)
        {
            if (ThrowOnConnect) throw new InvalidOperationException("Simulated connect failure.");
            // Every join gets a new transport-assigned connection ID, as a real SDK would.
            ulong id = net.NextConnectionId();
            net.Enqueue(() =>
            {
                openLink = link;
                ConnectionId = id;
                net.Host.Accept(this, id);
                Connected?.Invoke(link);
            });
        }

        public void Disconnect(long link)
        {
            if (ThrowOnDisconnect) throw new InvalidOperationException("Simulated disconnect failure.");
            if (link != openLink || openLink == 0) return;
            Close("LocalDisconnect", DeferDisconnectEvent);
        }

        /// <summary>Link failure seen by this client (e.g. host lost). Host-side drop arrives later.</summary>
        public void SimulateLinkLoss(string reason) => Close(reason, false);

        private void Close(string reason, bool deferEvent)
        {
            long link = openLink;
            ulong id = ConnectionId;
            openLink = 0;
            net.Enqueue(() => net.Host.Drop(id));
            if (deferEvent) net.Enqueue(() => Disconnected?.Invoke(link, reason));
            else Disconnected?.Invoke(link, reason);
        }

        public void SendToHost(long link, byte[] payload)
        {
            if (ThrowOnSend) throw new InvalidOperationException("Simulated send failure.");
            Sent.Add(payload);
            if (link != openLink || openLink == 0) return;
            ulong id = ConnectionId;
            byte[] copy = (byte[])payload.Clone();
            net.Enqueue(() => net.Host.Deliver(id, copy));
        }

        public void Deliver(ulong connectionId, byte[] payload)
        {
            if (openLink == 0 || connectionId != ConnectionId) return;
            LastDelivered = payload;
            MessageReceived?.Invoke(openLink, payload);
        }

        /// <summary>Injects raw bytes as if they came from the host on <paramref name="link"/> (default: open link).</summary>
        public void Inject(byte[] payload, long link = 0)
        {
            LastDelivered = payload;
            MessageReceived?.Invoke(link == 0 ? openLink : link, payload);
        }
    }

    /// <summary>Test-only authority returning a configurable reason; FakeSessionPort reasons are fixed.</summary>
    private sealed class ScriptedAuthority : ISessionPort
    {
        private readonly MissionSnapshot snapshot = new MissionSnapshot(0, 0, SessionState.Ready,
            new PowerSnapshot(0, 5000, 10000, 100, 60, 60, 0, 0), 0, false);
        public string Reason = string.Empty;
        public event Action<MissionSnapshot> SnapshotChanged;
        public event Action<CommandResult> CommandCompleted;

        public bool TryGetSnapshot(out MissionSnapshot value) { value = snapshot; return true; }
        public void Publish() => SnapshotChanged?.Invoke(snapshot);
        public void Submit(SessionCommand command) =>
            CommandCompleted?.Invoke(new CommandResult(command.Id, command.Epoch, false, Reason));
    }

    // ---- Rig ----

    private sealed class Client
    {
        public ClientEnd Transport;
        public SessionConnectionController Controller;
        public NetworkSessionAdapter Session => Controller.Session;
        public readonly List<MissionSnapshot> Snapshots = new List<MissionSnapshot>();
        public readonly List<byte[]> SnapshotBytes = new List<byte[]>();
        public readonly List<CommandResult> Results = new List<CommandResult>();
        public readonly List<byte[]> ResultBytes = new List<byte[]>();
        public readonly List<(SessionCommand Command, string Reason)> Abandoned = new List<(SessionCommand, string)>();
        public readonly List<string> ProtocolErrors = new List<string>();
        public readonly List<SessionConnectionState> States = new List<SessionConnectionState>();
    }

    private sealed class Rig
    {
        public readonly Loopback Net = new Loopback();
        public readonly ISessionPort Port;
        public readonly FakeSessionPort Authority;
        public readonly NetworkCommandBridge Bridge;
        public readonly List<CommandResult> AuthorityResults = new List<CommandResult>();
        public readonly List<(ulong Sender, string Error)> HostErrors = new List<(ulong, string)>();

        public Rig(ISessionPort authority = null)
        {
            Authority = authority == null ? new FakeSessionPort() : null;
            Port = authority ?? Authority;
            Port.CommandCompleted += AuthorityResults.Add;
            Bridge = new NetworkCommandBridge(Port, Net.Host);
            Bridge.ProtocolError += (sender, error) => HostErrors.Add((sender, error));
        }

        public Client NewClient()
        {
            var client = new Client { Transport = new ClientEnd(Net) };
            client.Controller = new SessionConnectionController(client.Transport);
            client.Session.SnapshotChanged += s =>
            {
                client.Snapshots.Add(s);
                client.SnapshotBytes.Add(client.Transport.LastDelivered);
            };
            client.Session.CommandCompleted += r =>
            {
                client.Results.Add(r);
                client.ResultBytes.Add(client.Transport.LastDelivered);
            };
            client.Session.CommandAbandoned += (c, reason) => client.Abandoned.Add((c, reason));
            client.Session.ProtocolError += client.ProtocolErrors.Add;
            client.Controller.StateChanged += client.States.Add;
            return client;
        }

        public Client Join()
        {
            Client client = NewClient();
            client.Controller.Connect();
            Net.Pump();
            Assert.That(client.Controller.State, Is.EqualTo(SessionConnectionState.Connected));
            return client;
        }

        public MissionSnapshot AuthoritySnapshot()
        {
            Assert.That(Port.TryGetSnapshot(out MissionSnapshot snapshot), Is.True);
            return snapshot;
        }
    }

    private static MissionSnapshot Current(Client client)
    {
        Assert.That(client.Session.TryGetSnapshot(out MissionSnapshot snapshot), Is.True);
        return snapshot;
    }

    private static void AssertSameState(MissionSnapshot expected, MissionSnapshot actual)
    {
        Assert.That(actual.Epoch, Is.EqualTo(expected.Epoch));
        Assert.That(actual.Revision, Is.EqualTo(expected.Revision));
        Assert.That(actual.State, Is.EqualTo(expected.State));
        Assert.That(actual.ScienceWorkSeconds, Is.EqualTo(expected.ScienceWorkSeconds));
        Assert.That(actual.ScienceEnabled, Is.EqualTo(expected.ScienceEnabled));
        Assert.That(actual.Power.SimulatedTimeSeconds, Is.EqualTo(expected.Power.SimulatedTimeSeconds));
        Assert.That(actual.Power.EnergyJoules, Is.EqualTo(expected.Power.EnergyJoules));
        Assert.That(actual.Power.CapacityJoules, Is.EqualTo(expected.Power.CapacityJoules));
        Assert.That(actual.Power.GenerationWatts, Is.EqualTo(expected.Power.GenerationWatts));
        Assert.That(actual.Power.EssentialRequestedWatts, Is.EqualTo(expected.Power.EssentialRequestedWatts));
        Assert.That(actual.Power.EssentialServedWatts, Is.EqualTo(expected.Power.EssentialServedWatts));
        Assert.That(actual.Power.ScienceRequestedWatts, Is.EqualTo(expected.Power.ScienceRequestedWatts));
        Assert.That(actual.Power.ScienceServedWatts, Is.EqualTo(expected.Power.ScienceServedWatts));
    }

    private static SessionCommand Science(long epoch, bool enabled) =>
        new SessionCommand(Guid.NewGuid(), epoch, SessionAction.SetScienceEnabled, enabled);

    private static SessionCommand Make(long epoch, SessionAction action) =>
        new SessionCommand(Guid.NewGuid(), epoch, action);

    // ---- Join and initial state ----

    [Test]
    public void JoiningClientHasNoStateUntilAuthoritySnapshotArrives()
    {
        var rig = new Rig();
        Client client = rig.NewClient();

        client.Controller.Connect();
        Assert.That(client.Controller.State, Is.EqualTo(SessionConnectionState.Connecting));
        Assert.That(client.Session.TryGetSnapshot(out _), Is.False);

        rig.Net.Pump();

        Assert.That(client.States, Is.EqualTo(new[] { SessionConnectionState.Connecting,
            SessionConnectionState.Synchronizing, SessionConnectionState.Connected }));
        Assert.That(client.Snapshots.Count, Is.EqualTo(1));
        AssertSameState(rig.AuthoritySnapshot(), Current(client));
        Assert.That(rig.Bridge.ConnectedClientCount, Is.EqualTo(1));
    }

    [Test]
    public void LateJoinerReceivesCurrentAuthorityState()
    {
        var rig = new Rig();
        Client first = rig.Join();
        first.Session.Submit(Science(0, true));
        first.Session.Submit(Make(0, SessionAction.Resume));
        rig.Net.Pump();
        rig.Authority.PublishExample(SessionState.Running, 42.5);
        rig.Net.Pump();

        Client late = rig.Join();

        Assert.That(late.Snapshots.Count, Is.EqualTo(1));
        AssertSameState(rig.AuthoritySnapshot(), Current(late));
        Assert.That(Current(late).Revision, Is.EqualTo(3));
        Assert.That(Current(late).State, Is.EqualTo(SessionState.Running));
        Assert.That(Current(late).ScienceEnabled, Is.True);
    }

    // ---- Commands and results ----

    [Test]
    public void SubmitIsOnlyIntentAndResultArrivesAsynchronously()
    {
        var rig = new Rig();
        Client client = rig.Join();
        SessionCommand command = Science(0, true);

        client.Session.Submit(command);

        Assert.That(client.Results, Is.Empty);
        Assert.That(client.Session.PendingCommandCount, Is.EqualTo(1));
        Assert.That(Current(client).ScienceEnabled, Is.False, "No local prediction of authority state.");

        rig.Net.Pump();

        Assert.That(client.Results.Count, Is.EqualTo(1));
        Assert.That(client.Results[0].CommandId, Is.EqualTo(command.Id));
        Assert.That(client.Results[0].Epoch, Is.EqualTo(0));
        Assert.That(client.Results[0].Accepted, Is.True);
        Assert.That(client.Results[0].Reason, Is.EqualTo("Accepted"));
        Assert.That(client.Session.PendingCommandCount, Is.EqualTo(0));
        AssertSameState(rig.AuthoritySnapshot(), Current(client));
        Assert.That(Current(client).Revision, Is.EqualTo(1));
    }

    [Test]
    public void RejectionReasonIsPreservedFromAuthority()
    {
        var rig = new Rig();
        Client client = rig.Join();

        client.Session.Submit(Make(0, SessionAction.Pause)); // Ready -> Pause is invalid for the authority.
        rig.Net.Pump();

        Assert.That(client.Results.Count, Is.EqualTo(1));
        Assert.That(client.Results[0].Accepted, Is.False);
        Assert.That(client.Results[0].Reason, Is.EqualTo("InvalidState"));
        Assert.That(client.Snapshots.Count, Is.EqualTo(1), "Rejected commands do not change state.");
    }

    [Test]
    public void ConversionPreservesAllPublishedFields()
    {
        var rig = new Rig();
        Client client = rig.Join();

        rig.Authority.PublishExample(SessionState.Completed, 1234.5678);
        rig.Net.Pump();

        AssertSameState(rig.AuthoritySnapshot(), Current(client));
        Assert.That(Current(client).State, Is.EqualTo(SessionState.Completed));
        Assert.That(Current(client).ScienceWorkSeconds, Is.EqualTo(1234.5678));
    }

    [Test]
    public void RestartResultKeepsOriginalEpochAndNewEpochComesFromSnapshot()
    {
        var rig = new Rig();
        Client client = rig.Join();
        client.Session.Submit(Science(0, true));
        rig.Net.Pump();
        SessionCommand restart = Make(0, SessionAction.Restart);

        client.Session.Submit(restart);
        rig.Net.Pump();

        Assert.That(client.Results[1].CommandId, Is.EqualTo(restart.Id));
        Assert.That(client.Results[1].Epoch, Is.EqualTo(0));
        Assert.That(client.Results[1].Accepted, Is.True);
        Assert.That(Current(client).Epoch, Is.EqualTo(1));
        Assert.That(Current(client).Revision, Is.EqualTo(0), "Lower revision in a newer epoch is accepted.");
        Assert.That(Current(client).ScienceEnabled, Is.False);
        AssertSameState(rig.AuthoritySnapshot(), Current(client));
    }

    [Test]
    public void RepeatedSubmissionGetsAuthorityReplayWithoutStateChange()
    {
        var rig = new Rig();
        Client client = rig.Join();
        SessionCommand command = Science(0, true);

        client.Session.Submit(command);
        rig.Net.Pump();
        client.Session.Submit(command);
        rig.Net.Pump();

        Assert.That(client.Results.Count, Is.EqualTo(2));
        Assert.That(client.Results[1].CommandId, Is.EqualTo(command.Id));
        Assert.That(client.Results[1].Accepted, Is.True);
        Assert.That(client.Snapshots.Count, Is.EqualTo(2), "Initial + one change; replay adds none.");
        Assert.That(rig.AuthoritySnapshot().Revision, Is.EqualTo(1));
    }

    [Test]
    public void LongOrInvalidReasonIsCutWithoutBreakingAuthorityEvents()
    {
        var authority = new ScriptedAuthority { Reason = new string('é', 700) + "\uD800" };
        var rig = new Rig(authority);
        var laterSubscriber = new List<CommandResult>();
        authority.CommandCompleted += laterSubscriber.Add; // Registered after the bridge.
        Client client = rig.Join();

        client.Session.Submit(Science(0, true));
        rig.Net.Pump();
        authority.Reason = "bad\uD800";
        client.Session.Submit(Science(0, true));
        rig.Net.Pump();

        Assert.That(laterSubscriber.Count, Is.EqualTo(2));
        Assert.That(client.Results.Count, Is.EqualTo(2));
        Assert.That(client.Results[0].Accepted, Is.False);
        Assert.That(client.Results[0].Reason, Is.EqualTo(new string('é', 512)), "Cut at a character boundary.");
        Assert.That(Encoding.UTF8.GetByteCount(client.Results[0].Reason), Is.LessThanOrEqualTo(1024));
        Assert.That(client.Results[1].Reason, Is.EqualTo("bad�"));
        Assert.That(rig.HostErrors, Is.EqualTo(new[] {
            (client.Transport.ConnectionId, SessionNetworkCodes.ReasonTruncated) }));
    }

    // ---- Ordering and single delivery ----

    [Test]
    public void StaleAndDuplicateSnapshotsAreDiscarded()
    {
        var rig = new Rig();
        Client client = rig.Join();
        byte[] initialBytes = client.SnapshotBytes[0];
        client.Session.Submit(Science(0, true));
        rig.Net.Pump();
        byte[] revisionOneBytes = client.SnapshotBytes[1];

        client.Transport.Inject(initialBytes);
        client.Transport.Inject(revisionOneBytes);

        Assert.That(client.Snapshots.Count, Is.EqualTo(2));
        Assert.That(client.Session.DiscardedSnapshotCount, Is.EqualTo(2));
        Assert.That(Current(client).Revision, Is.EqualTo(1));
    }

    [Test]
    public void SnapshotFromOlderEpochIsDiscardedAfterRestart()
    {
        var rig = new Rig();
        Client client = rig.Join();
        rig.Authority.PublishExample(SessionState.Running);
        rig.Authority.PublishExample(SessionState.Running);
        rig.Net.Pump();
        byte[] epochZeroRevisionTwo = client.SnapshotBytes[2];
        client.Session.Submit(Make(0, SessionAction.Restart));
        rig.Net.Pump();

        client.Transport.Inject(epochZeroRevisionTwo);

        Assert.That(Current(client).Epoch, Is.EqualTo(1));
        Assert.That(Current(client).Revision, Is.EqualTo(0));
        Assert.That(client.Session.DiscardedSnapshotCount, Is.EqualTo(1));
    }

    [Test]
    public void DuplicateOrUnsolicitedResultIsDeliveredAtMostOnce()
    {
        var rig = new Rig();
        Client sender = rig.Join();
        Client other = rig.Join();
        sender.Session.Submit(Science(0, true));
        rig.Net.Pump();
        byte[] resultBytes = sender.ResultBytes[0];

        sender.Transport.Inject(resultBytes);
        other.Transport.Inject(resultBytes);

        Assert.That(sender.Results.Count, Is.EqualTo(1));
        Assert.That(sender.Session.DiscardedResultCount, Is.EqualTo(1));
        Assert.That(other.Results, Is.Empty);
        Assert.That(other.Session.DiscardedResultCount, Is.EqualTo(1));
    }

    // ---- One authority, several clients, sender authenticity ----

    [Test]
    public void TwoClientsObserveOneAuthorityAndResultsReturnOnlyToSender()
    {
        var rig = new Rig();
        Client a = rig.Join();
        Client b = rig.Join();

        a.Session.Submit(Science(0, true));
        rig.Net.Pump();
        b.Session.Submit(Make(0, SessionAction.Resume));
        rig.Net.Pump();

        Assert.That(a.Results.Count, Is.EqualTo(1));
        Assert.That(b.Results.Count, Is.EqualTo(1));
        Assert.That(a.Results[0].Accepted && b.Results[0].Accepted, Is.True);
        Assert.That(a.Results[0].CommandId, Is.Not.EqualTo(b.Results[0].CommandId));
        MissionSnapshot authority = rig.AuthoritySnapshot();
        AssertSameState(authority, Current(a));
        AssertSameState(authority, Current(b));
        Assert.That(authority.Revision, Is.EqualTo(2));
        Assert.That(authority.State, Is.EqualTo(SessionState.Running));
        Assert.That(authority.ScienceEnabled, Is.True);
    }

    [Test]
    public void BroadcastGivesEachClientItsOwnBuffer()
    {
        var rig = new Rig();
        Client a = rig.Join();
        Client b = rig.Join();
        int before = rig.Net.Host.Sent.Count;

        rig.Authority.PublishExample(SessionState.Running);

        Assert.That(rig.Net.Host.Sent.Count, Is.EqualTo(before + 2));
        Assert.That(rig.Net.Host.Sent[before].Payload, Is.Not.SameAs(rig.Net.Host.Sent[before + 1].Payload));
        Assert.That(rig.Net.Host.Sent[before].Payload, Is.EqualTo(rig.Net.Host.Sent[before + 1].Payload));
    }

    [Test]
    public void FailingSendToOneClientDoesNotBreakOthersOrAuthorityEvents()
    {
        var rig = new Rig();
        Client a = rig.Join();
        Client b = rig.Join();
        var laterSubscriber = new List<MissionSnapshot>();
        rig.Authority.SnapshotChanged += laterSubscriber.Add; // Registered after the bridge.
        rig.Net.Host.FailSendsTo.Add(a.Transport.ConnectionId);

        rig.Authority.PublishExample(SessionState.Running);
        rig.Net.Pump();

        Assert.That(laterSubscriber.Count, Is.EqualTo(1));
        Assert.That(Current(b).State, Is.EqualTo(SessionState.Running));
        Assert.That(Current(a).State, Is.EqualTo(SessionState.Ready));
        Assert.That(rig.HostErrors, Is.EqualTo(new[] { (a.Transport.ConnectionId, SessionNetworkCodes.SendFailed) }));
    }

    [Test]
    public void CommandIdOwnedByAnotherSenderIsRejectedBeforeAuthority()
    {
        var rig = new Rig();
        Client a = rig.Join();
        Client b = rig.Join();
        SessionCommand command = Science(0, true);
        a.Session.Submit(command);
        rig.Net.Pump();
        int authorityCalls = rig.AuthorityResults.Count;

        b.Session.Submit(command); // Same ID and content, different real sender.
        rig.Net.Pump();

        Assert.That(rig.AuthorityResults.Count, Is.EqualTo(authorityCalls));
        Assert.That(b.Results.Count, Is.EqualTo(1));
        Assert.That(b.Results[0].Accepted, Is.False);
        Assert.That(b.Results[0].Reason, Is.EqualTo("CommandIdConflict"));
        Assert.That(a.Results.Count, Is.EqualTo(1));
    }

    [Test]
    public void EmptyCommandIdIsRejectedWithContractReason()
    {
        var rig = new Rig();
        Client client = rig.Join();

        client.Session.Submit(new SessionCommand(Guid.Empty, 0, SessionAction.Resume));
        rig.Net.Pump();

        Assert.That(client.Results.Count, Is.EqualTo(1));
        Assert.That(client.Results[0].Reason, Is.EqualTo("InvalidCommandId"));
        Assert.That(rig.AuthorityResults, Is.Empty);
    }

    [Test]
    public void ClientCannotSendHostMessagesAndHostCannotSendIntentions()
    {
        var rig = new Rig();
        Client a = rig.Join();
        Client b = rig.Join();
        byte[] snapshotBytes = a.SnapshotBytes[0];
        a.Session.Submit(Science(0, true));
        rig.Net.Pump();
        byte[] resultBytes = a.ResultBytes[0];
        byte[] submitBytes = a.Transport.Sent[a.Transport.Sent.Count - 1];
        int hostSends = rig.Net.Host.Sent.Count;
        int authorityCalls = rig.AuthorityResults.Count;

        rig.Net.Host.Deliver(a.Transport.ConnectionId, snapshotBytes);
        rig.Net.Host.Deliver(a.Transport.ConnectionId, resultBytes);
        b.Transport.Inject(submitBytes);

        Assert.That(rig.HostErrors, Is.EqualTo(new[] {
            (a.Transport.ConnectionId, SessionNetworkCodes.UnexpectedMessageKind),
            (a.Transport.ConnectionId, SessionNetworkCodes.UnexpectedMessageKind) }));
        Assert.That(rig.Net.Host.Sent.Count, Is.EqualTo(hostSends), "Nothing was rebroadcast.");
        Assert.That(rig.AuthorityResults.Count, Is.EqualTo(authorityCalls));
        Assert.That(b.ProtocolErrors, Is.EqualTo(new[] { SessionNetworkCodes.UnexpectedMessageKind }));
        Assert.That(b.Results, Is.Empty);
    }

    [Test]
    public void MessageFromUnknownConnectionIsIgnored()
    {
        var rig = new Rig();
        Client client = rig.Join();
        client.Session.Submit(Science(0, true));
        byte[] submitBytes = client.Transport.Sent[client.Transport.Sent.Count - 1];
        rig.Net.Pump();
        int authorityCalls = rig.AuthorityResults.Count;

        rig.Net.Host.Deliver(999, submitBytes);

        Assert.That(rig.HostErrors, Is.EqualTo(new[] { (999UL, SessionNetworkCodes.UnknownConnection) }));
        Assert.That(rig.AuthorityResults.Count, Is.EqualTo(authorityCalls));
    }

    [Test]
    public void MalformedPayloadsAreRejectedOnBothSides()
    {
        var rig = new Rig();
        Client client = rig.Join();
        ulong id = client.Transport.ConnectionId;
        client.Session.Submit(Science(0, true));
        byte[] submit = client.Transport.Sent[client.Transport.Sent.Count - 1];
        rig.Net.Pump();
        byte[] snapshot = client.SnapshotBytes[1];
        int authorityCalls = rig.AuthorityResults.Count;

        byte[] truncated = new byte[submit.Length - 1];
        Array.Copy(submit, truncated, truncated.Length);
        byte[] trailing = new byte[submit.Length + 1];
        Array.Copy(submit, trailing, submit.Length);
        byte[] badMagic = (byte[])submit.Clone();
        badMagic[0] ^= 0xFF;
        byte[] badVersion = (byte[])submit.Clone();
        badVersion[2] = 99;
        byte[] badKind = (byte[])submit.Clone();
        badKind[3] = 200;
        byte[] badScienceFlag = (byte[])submit.Clone();
        badScienceFlag[badScienceFlag.Length - 1] = 7;

        foreach (byte[] payload in new[] { truncated, trailing, badMagic, badVersion, badKind, badScienceFlag,
                     new byte[0], new byte[3000] })
            rig.Net.Host.Deliver(id, payload);

        Assert.That(rig.HostErrors, Is.EqualTo(new[] {
            (id, SessionNetworkCodes.MalformedPayload), (id, SessionNetworkCodes.MalformedPayload),
            (id, SessionNetworkCodes.BadMagic), (id, SessionNetworkCodes.UnsupportedVersion),
            (id, SessionNetworkCodes.UnknownMessageKind), (id, SessionNetworkCodes.MalformedPayload),
            (id, SessionNetworkCodes.EmptyPayload), (id, SessionNetworkCodes.PayloadTooLarge) }));
        Assert.That(rig.AuthorityResults.Count, Is.EqualTo(authorityCalls));

        byte[] badState = (byte[])snapshot.Clone();
        badState[4 + 16] = 77; // SessionState field after header, epoch and revision.
        byte[] shortSnapshot = new byte[snapshot.Length - 3];
        Array.Copy(snapshot, shortSnapshot, shortSnapshot.Length);
        client.Transport.Inject(badState);
        client.Transport.Inject(shortSnapshot);

        Assert.That(client.ProtocolErrors, Is.EqualTo(new[] {
            SessionNetworkCodes.MalformedPayload, SessionNetworkCodes.MalformedPayload }));
        Assert.That(client.Snapshots.Count, Is.EqualTo(2));
    }

    // ---- Connection control, separate from mission state ----

    [Test]
    public void SubmitWhileDisconnectedIsAbandonedNotFakedAsResult()
    {
        var rig = new Rig();
        Client client = rig.NewClient();
        SessionCommand command = Make(0, SessionAction.Resume);

        client.Session.Submit(command);

        Assert.That(client.Abandoned.Count, Is.EqualTo(1));
        Assert.That(client.Abandoned[0].Command, Is.SameAs(command));
        Assert.That(client.Abandoned[0].Reason, Is.EqualTo(SessionNetworkCodes.NotConnected));
        Assert.That(client.Results, Is.Empty);
        Assert.That(client.Transport.Sent, Is.Empty);
    }

    [Test]
    public void HostLossIsConnectionLossAndPendingOutcomeIsUnknown()
    {
        var rig = new Rig();
        Client client = rig.Join();
        SessionCommand command = Science(0, true);
        client.Session.Submit(command);

        client.Transport.SimulateLinkLoss("HostUnreachable");

        Assert.That(client.Controller.State, Is.EqualTo(SessionConnectionState.ConnectionLost));
        Assert.That(client.Controller.LastDisconnectReason, Is.EqualTo("HostUnreachable"));
        Assert.That(client.Abandoned, Is.EqualTo(new[] { (command, SessionNetworkCodes.ConnectionLost) }));
        Assert.That(client.Session.PendingCommandCount, Is.EqualTo(0));
        Assert.That(Current(client).State, Is.EqualTo(SessionState.Ready),
            "Mission state is not rewritten by the client.");

        rig.Net.Pump();

        // In this loopback the queued intention still reached the authority: the outcome was unknown.
        Assert.That(rig.AuthoritySnapshot().ScienceEnabled, Is.True);
        Assert.That(client.Results, Is.Empty);
        Assert.That(Current(client).ScienceEnabled, Is.False);
        Assert.That(rig.Bridge.ConnectedClientCount, Is.EqualTo(0));
    }

    [Test]
    public void ConnectingAgainIsAFreshJoinWithCurrentState()
    {
        var rig = new Rig();
        Client client = rig.Join();
        client.Transport.SimulateLinkLoss("HostUnreachable");
        rig.Authority.PublishExample(SessionState.Running, 5);
        rig.Net.Pump();

        client.Controller.Connect();
        rig.Net.Pump();

        Assert.That(client.Controller.State, Is.EqualTo(SessionConnectionState.Connected));
        Assert.That(client.Controller.LastDisconnectReason, Is.Null);
        AssertSameState(rig.AuthoritySnapshot(), Current(client));
    }

    [Test]
    public void LocalDisconnectIsNotReportedAsConnectionLoss()
    {
        var rig = new Rig();
        Client client = rig.Join();
        SessionCommand command = Make(0, SessionAction.Resume);
        client.Session.Submit(command);

        client.Controller.Disconnect();
        rig.Net.Pump();
        client.Session.Submit(Make(0, SessionAction.Resume));

        Assert.That(client.Controller.State, Is.EqualTo(SessionConnectionState.Disconnected));
        Assert.That(client.Controller.LastDisconnectReason, Is.EqualTo(SessionNetworkCodes.LocalDisconnect));
        Assert.That(client.States, Has.No.Member(SessionConnectionState.ConnectionLost));
        Assert.That(client.Abandoned[0], Is.EqualTo((command, SessionNetworkCodes.LocalDisconnect)));
        Assert.That(client.Abandoned[1].Reason, Is.EqualTo(SessionNetworkCodes.NotConnected));
        Assert.That(client.Results, Is.Empty);
    }

    [Test]
    public void LateDisconnectOfPreviousLinkDoesNotAffectQuickRejoin()
    {
        var rig = new Rig();
        Client client = rig.Join();
        long firstLink = client.Transport.OpenLink;
        client.Transport.DeferDisconnectEvent = true;

        client.Controller.Disconnect();
        client.Controller.Connect(); // Before the transport reports the first link closed.
        rig.Net.Pump();

        Assert.That(client.Controller.State, Is.EqualTo(SessionConnectionState.Connected));
        Assert.That(client.States, Has.No.Member(SessionConnectionState.ConnectionLost));
        Assert.That(client.Controller.LastDisconnectReason, Is.Null);
        Assert.That(rig.Bridge.ConnectedClientCount, Is.EqualTo(1));

        int snapshots = client.Snapshots.Count;
        rig.Authority.PublishExample(SessionState.Running); // Newer snapshot, not yet delivered.
        client.Transport.Inject(rig.Net.Host.Sent[rig.Net.Host.Sent.Count - 1].Payload, firstLink);
        Assert.That(client.Snapshots.Count, Is.EqualTo(snapshots), "A message tagged with an old link is ignored.");
        rig.Net.Pump();
        Assert.That(client.Snapshots.Count, Is.EqualTo(snapshots + 1), "The same bytes on the current link apply.");
    }

    [Test]
    public void CancelledConnectIsTornDownWhenItCompletes()
    {
        var rig = new Rig();
        Client client = rig.NewClient();

        client.Controller.Connect();
        client.Controller.Disconnect(); // The loopback ignores cancelling a pending connect.
        rig.Net.Pump();

        Assert.That(client.Controller.State, Is.EqualTo(SessionConnectionState.Disconnected));
        Assert.That(client.Transport.OpenLink, Is.EqualTo(0));
        Assert.That(rig.Bridge.ConnectedClientCount, Is.EqualTo(0), "No ghost connection remains on the host.");

        client.Controller.Connect();
        rig.Net.Pump();
        Assert.That(client.Controller.State, Is.EqualTo(SessionConnectionState.Connected));
        Assert.That(rig.Bridge.ConnectedClientCount, Is.EqualTo(1));
    }

    [Test]
    public void DisconnectAfterLossKeepsTheRealReason()
    {
        var rig = new Rig();
        Client client = rig.Join();
        client.Transport.SimulateLinkLoss("HostUnreachable");

        client.Controller.Disconnect();
        client.Controller.Dispose();

        Assert.That(client.Controller.State, Is.EqualTo(SessionConnectionState.Disconnected));
        Assert.That(client.Controller.LastDisconnectReason, Is.EqualTo("HostUnreachable"));
    }

    [Test]
    public void ThrowingConnectLeavesARetryableState()
    {
        var rig = new Rig();
        Client client = rig.NewClient();
        client.Transport.ThrowOnConnect = true;

        Assert.Throws<InvalidOperationException>(() => client.Controller.Connect());

        Assert.That(client.Controller.State, Is.EqualTo(SessionConnectionState.ConnectionLost));
        Assert.That(client.Controller.LastDisconnectReason, Is.EqualTo(SessionNetworkCodes.ConnectFailed));
        client.Transport.ThrowOnConnect = false;
        client.Controller.Connect();
        rig.Net.Pump();
        Assert.That(client.Controller.State, Is.EqualTo(SessionConnectionState.Connected));
    }

    [Test]
    public void ThrowingSendDoesNotLeaveAGhostPendingCommand()
    {
        var rig = new Rig();
        Client client = rig.Join();
        client.Transport.ThrowOnSend = true;

        Assert.Throws<InvalidOperationException>(() => client.Session.Submit(Science(0, true)));

        Assert.That(client.Session.PendingCommandCount, Is.EqualTo(0));
        client.Transport.ThrowOnSend = false;
        client.Transport.SimulateLinkLoss("HostUnreachable");
        Assert.That(client.Abandoned, Is.Empty, "A command that was never sent is not reported again.");
    }

    [Test]
    public void ThrowingInitialStateRequestIsReportedAsLinkLoss()
    {
        var rig = new Rig();
        Client client = rig.NewClient();
        client.Controller.Connect();
        client.Transport.ThrowOnSend = true;

        Assert.Throws<InvalidOperationException>(() => rig.Net.Pump());
        client.Transport.ThrowOnSend = false;
        rig.Net.Pump();

        Assert.That(client.Controller.State, Is.EqualTo(SessionConnectionState.ConnectionLost));
        Assert.That(client.Controller.LastDisconnectReason, Is.EqualTo(SessionNetworkCodes.SendFailed));
        Assert.That(client.Transport.OpenLink, Is.EqualTo(0));
        Assert.That(rig.Bridge.ConnectedClientCount, Is.EqualTo(0));
    }

    [Test]
    public void ThrowingTransportDisconnectStillClosesTheSession()
    {
        var rig = new Rig();
        Client client = rig.Join();
        SessionCommand command = Make(0, SessionAction.Resume);
        client.Session.Submit(command);
        client.Transport.ThrowOnDisconnect = true;

        Assert.Throws<InvalidOperationException>(() => client.Controller.Disconnect());
        int sent = client.Transport.Sent.Count;
        client.Session.Submit(Make(0, SessionAction.Resume));
        rig.Net.Pump();

        Assert.That(client.Controller.State, Is.EqualTo(SessionConnectionState.Disconnected));
        Assert.That(client.States[client.States.Count - 1], Is.EqualTo(SessionConnectionState.Disconnected));
        Assert.That(client.Abandoned[0], Is.EqualTo((command, SessionNetworkCodes.LocalDisconnect)));
        Assert.That(client.Abandoned[1].Reason, Is.EqualTo(SessionNetworkCodes.NotConnected));
        Assert.That(client.Transport.Sent.Count, Is.EqualTo(sent), "Nothing is sent after leaving.");
        Assert.That(client.Results, Is.Empty, "Late results on the still-open link are ignored.");
    }

    [Test]
    public void FailedHostResultClosesLinkAndAbandonsPending()
    {
        var rig = new Rig();
        Client client = rig.Join();
        rig.Net.Host.FailResults = true;
        client.Session.Submit(Make(0, SessionAction.Resume));
        rig.Net.Pump();
        Assert.That(rig.AuthoritySnapshot().State, Is.EqualTo(SessionState.Running));
        Assert.That(client.Controller.State, Is.EqualTo(SessionConnectionState.ConnectionLost));
        Assert.That(client.Session.PendingCommandCount, Is.EqualTo(0));
        Assert.That(client.Abandoned.Count, Is.EqualTo(1));
        Assert.That(client.Results, Is.Empty);
    }

    [Test]
    public void FailedInitialSnapshotEndsSynchronization()
    {
        var rig = new Rig();
        Client client = rig.NewClient();
        rig.Net.Host.FailSendsTo.Add(1);
        client.Controller.Connect();
        rig.Net.Pump();
        Assert.That(client.Controller.State, Is.EqualTo(SessionConnectionState.ConnectionLost));
        Assert.That(client.Controller.LastDisconnectReason, Is.EqualTo(SessionNetworkCodes.SendFailed));
    }

    [Test]
    public void InvalidSnapshotNumbersNeverConnectOrReplaceState()
    {
        var rig = new Rig();
        Client client = rig.Join();
        MissionSnapshot initial = Current(client);
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, -1d })
        {
            var invalid = new MissionSnapshot(0, 99, SessionState.Running,
                new PowerSnapshot(bad, 1, 1, 0, 0, 0, 0, 0), 0, false);
            client.Transport.Inject(SessionWireProtocol.EncodeSnapshot(invalid));
            Assert.That(Current(client), Is.SameAs(initial));
        }
        foreach (var invalid in new[] {
            new MissionSnapshot(-1, 0, SessionState.Ready, initial.Power, 0, false),
            new MissionSnapshot(0, -1, SessionState.Ready, initial.Power, 0, false),
            new MissionSnapshot(0, 99, SessionState.Ready, initial.Power, double.NaN, false),
            new MissionSnapshot(0, 99, SessionState.Ready, new PowerSnapshot(0, 2, 1, 0, 0, 0, 0, 0), 0, false),
            new MissionSnapshot(0, 99, SessionState.Ready, new PowerSnapshot(0, 1, 1, 0, 0, 1, 0, 0), 0, false) })
        {
            client.Transport.Inject(SessionWireProtocol.EncodeSnapshot(invalid));
            Assert.That(Current(client), Is.SameAs(initial));
        }
        Assert.That(client.ProtocolErrors.Count, Is.EqualTo(8));
    }

    [Test]
    public void DisposedBridgeStopsForwarding()
    {
        var rig = new Rig();
        Client client = rig.Join();
        rig.Bridge.Dispose();

        client.Session.Submit(Science(0, true));
        rig.Authority.PublishExample(SessionState.Running);
        rig.Net.Pump();

        Assert.That(rig.AuthorityResults, Is.Empty);
        Assert.That(client.Snapshots.Count, Is.EqualTo(1));
        Assert.That(client.Results, Is.Empty);
    }
}
#endif
