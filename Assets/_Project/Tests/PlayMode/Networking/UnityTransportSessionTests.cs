// No test asmdef exists, so this file compiles into Assembly-CSharp. In the Editor that assembly defines
// UNITY_INCLUDE_TESTS and references NUnit; normal player builds do neither, hence the guard.
// These tests open REAL WebSocket sockets (Unity Transport) on 127.0.0.1 inside ONE process, with the real
// MissionSession + PowerSystem as the single authority. They prove the binding, wire protocol and routing
// over the OS network stack; they do not prove two processes, LAN, Internet, TLS or a WebGL browser.
#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Simulation;
using Trainer.Unity.Networking;

public sealed class UnityTransportSessionTests
{
    private const double TimeoutSeconds = 10;
    private UnityTransportHost host;
    private readonly List<UnityTransportClient> transports = new List<UnityTransportClient>();
    private readonly List<SessionConnectionController> controllers = new List<SessionConnectionController>();
    private readonly List<(ulong id, string code)> hostErrors = new List<(ulong, string)>();
    private readonly List<ulong> hostConnections = new List<ulong>();
    private MissionSession authority;
    private NetworkCommandBridge bridge;

    private sealed class Probe
    {
        public readonly SessionConnectionController Controller;
        public readonly List<MissionSnapshot> Snapshots = new List<MissionSnapshot>();
        public readonly List<CommandResult> Results = new List<CommandResult>();
        public readonly List<(SessionCommand command, string reason)> Abandoned = new List<(SessionCommand, string)>();

        public Probe(SessionConnectionController controller)
        {
            Controller = controller;
            controller.Session.SnapshotChanged += Snapshots.Add;
            controller.Session.CommandCompleted += Results.Add;
            controller.Session.CommandAbandoned += (command, reason) => Abandoned.Add((command, reason));
        }

        public NetworkSessionAdapter Port => Controller.Session;
        public SessionConnectionState State => Controller.State;

        public MissionSnapshot Current
        {
            get
            {
                Assert.That(Port.TryGetSnapshot(out MissionSnapshot snapshot), Is.True, "No authoritative state.");
                return snapshot;
            }
        }

        public CommandResult ResultFor(SessionCommand command) =>
            Results.Find(r => r.CommandId == command.Id && r.Epoch == command.Epoch);
    }

    [SetUp]
    public void SetUp()
    {
        authority = new MissionSession(new PowerSystem(),
            new PowerConfiguration(5000, 10000, 100, 60, 60, 100, 200), new MissionRules(120, 300));
        host = new UnityTransportHost();
        host.Listen(new WebSocketHostOptions { BindAddress = "127.0.0.1", Port = 0 });
        host.ClientConnected += hostConnections.Add;
        bridge = new NetworkCommandBridge(authority, host);
        bridge.ProtocolError += (id, code) => hostErrors.Add((id, code));
    }

    [TearDown]
    public void TearDown()
    {
        foreach (SessionConnectionController controller in controllers) controller.Dispose();
        foreach (UnityTransportClient transport in transports) transport.Dispose();
        bridge?.Dispose();
        host?.Dispose();
        controllers.Clear();
        transports.Clear();
        hostErrors.Clear();
        hostConnections.Clear();
    }

    // ---- Helpers ----

    private UnityTransportClient NewTransport(WebSocketClientOptions options = null)
    {
        var transport = new UnityTransportClient(options ?? new WebSocketClientOptions
            { Address = "127.0.0.1", Port = host.Port });
        transports.Add(transport);
        return transport;
    }

    private Probe NewClient(WebSocketClientOptions options = null)
    {
        var controller = new SessionConnectionController(NewTransport(options));
        controllers.Add(controller);
        return new Probe(controller);
    }

    private Probe Join()
    {
        Probe probe = NewClient();
        probe.Controller.Connect();
        PumpUntil(() => probe.State == SessionConnectionState.Connected, "client connected");
        return probe;
    }

    private void PumpOnce()
    {
        host?.Update();
        host?.Flush();
        foreach (UnityTransportClient transport in transports)
        {
            transport.Update();
            transport.Flush();
        }
    }

    private void PumpUntil(Func<bool> done, string what)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!done())
        {
            if (watch.Elapsed.TotalSeconds > TimeoutSeconds) Assert.Fail($"Timed out waiting for: {what}.");
            PumpOnce();
            Thread.Sleep(1);
        }
    }

    /// <summary>Lets in-flight traffic settle when asserting that something does NOT arrive.</summary>
    private void PumpFor(int milliseconds)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            PumpOnce();
            Thread.Sleep(1);
        }
    }

    private static SessionCommand Science(long epoch, bool enabled) =>
        new SessionCommand(Guid.NewGuid(), epoch, SessionAction.SetScienceEnabled, enabled);

    private static SessionCommand Command(long epoch, SessionAction action) =>
        new SessionCommand(Guid.NewGuid(), epoch, action);

    private MissionSnapshot AuthorityState
    {
        get
        {
            authority.TryGetSnapshot(out MissionSnapshot snapshot);
            return snapshot;
        }
    }

    private static void AssertSameState(MissionSnapshot expected, MissionSnapshot actual)
    {
        Assert.That(actual.Epoch, Is.EqualTo(expected.Epoch));
        Assert.That(actual.Revision, Is.EqualTo(expected.Revision));
        Assert.That(actual.State, Is.EqualTo(expected.State));
        Assert.That(actual.ScienceEnabled, Is.EqualTo(expected.ScienceEnabled));
        Assert.That(actual.ScienceWorkSeconds, Is.EqualTo(expected.ScienceWorkSeconds));
        Assert.That(actual.Power.EnergyJoules, Is.EqualTo(expected.Power.EnergyJoules));
        Assert.That(actual.Power.SimulatedTimeSeconds, Is.EqualTo(expected.Power.SimulatedTimeSeconds));
        Assert.That(actual.Power.ScienceServedWatts, Is.EqualTo(expected.Power.ScienceServedWatts));
        Assert.That(actual.Power.EssentialServedWatts, Is.EqualTo(expected.Power.EssentialServedWatts));
    }

    private bool BothAt(Probe a, Probe b, long revision) =>
        a.Port.TryGetSnapshot(out MissionSnapshot sa) && sa.Revision == revision
        && b.Port.TryGetSnapshot(out MissionSnapshot sb) && sb.Revision == revision;

    // ---- Join and initial state ----

    [Test]
    public void TwoClientsJoinOneAuthorityOverRealSocketsAndGetItsInitialState()
    {
        Assert.That(host.Port, Is.GreaterThan(0));
        Probe a = Join();
        Probe b = Join();

        Assert.That(bridge.ConnectedClientCount, Is.EqualTo(2));
        Assert.That(hostConnections, Is.EqualTo(new ulong[] { 1, 2 }), "Ids come from the host transport.");
        AssertSameState(AuthorityState, a.Current);
        AssertSameState(AuthorityState, b.Current);
        Assert.That(a.Current.State, Is.EqualTo(SessionState.Ready));
        Assert.That(a.Results, Is.Empty);
    }

    [Test]
    public void LateJoinerGetsTheCurrentAuthoritativeStateNotTheInitialOne()
    {
        Probe a = Join();
        var science = Science(0, true);
        a.Port.Submit(science);
        PumpUntil(() => a.ResultFor(science) != null, "science result");

        Probe late = Join();

        Assert.That(late.Current.Revision, Is.EqualTo(1));
        Assert.That(late.Current.ScienceEnabled, Is.True);
        AssertSameState(AuthorityState, late.Current);
    }

    // ---- Intentions, results to the sender, shared snapshots ----

    [Test]
    public void SubmitIsOnlyAnIntentionAndTheResultReachesOnlyTheSender()
    {
        Probe a = Join();
        Probe b = Join();
        var science = Science(0, true);

        a.Port.Submit(science);
        Assert.That(a.Results, Is.Empty, "No local acceptance before the authority answers.");
        Assert.That(a.Current.ScienceEnabled, Is.False, "No optimistic prediction.");
        Assert.That(a.Port.PendingCommandCount, Is.EqualTo(1));

        PumpUntil(() => a.ResultFor(science) != null && BothAt(a, b, 1), "result and snapshot");
        PumpFor(100);

        Assert.That(a.ResultFor(science).Accepted, Is.True);
        Assert.That(a.ResultFor(science).Reason, Is.EqualTo("Accepted"));
        Assert.That(b.Results, Is.Empty, "Another client never sees the sender's result.");
        Assert.That(a.Port.PendingCommandCount, Is.EqualTo(0));
        AssertSameState(a.Current, b.Current);
        Assert.That(b.Current.ScienceEnabled, Is.True);
    }

    [Test]
    public void BothClientsSeeTheSameEnergyAndWorkComputedOnlyByTheAuthority()
    {
        Probe a = Join();
        Probe b = Join();
        var science = Science(0, true);
        var resume = Command(0, SessionAction.Resume);
        a.Port.Submit(science);
        b.Port.Submit(resume);
        PumpUntil(() => a.ResultFor(science) != null && b.ResultFor(resume) != null, "both results");
        Assert.That(b.ResultFor(resume).Accepted, Is.True);

        double energyBefore = AuthorityState.Power.EnergyJoules;
        for (int i = 0; i < 5; i++) authority.Tick(2); // Only the host advances time.
        long revision = AuthorityState.Revision;
        PumpUntil(() => BothAt(a, b, revision), "ticked snapshots");

        AssertSameState(AuthorityState, a.Current);
        AssertSameState(AuthorityState, b.Current);
        Assert.That(a.Current.State, Is.EqualTo(SessionState.Running));
        Assert.That(a.Current.ScienceWorkSeconds, Is.GreaterThan(0));
        Assert.That(a.Current.Power.SimulatedTimeSeconds, Is.EqualTo(10).Within(1e-9));
        Assert.That(a.Current.Power.EnergyJoules, Is.Not.EqualTo(energyBefore), "Science drew energy.");
    }

    [Test]
    public void RejectionReasonComesFromTheAuthority()
    {
        Probe a = Join();
        var pause = Command(0, SessionAction.Pause); // Ready cannot be paused.
        a.Port.Submit(pause);
        PumpUntil(() => a.ResultFor(pause) != null, "rejection");

        Assert.That(a.ResultFor(pause).Accepted, Is.False);
        Assert.That(a.ResultFor(pause).Reason, Is.EqualTo("InvalidState"));
        Assert.That(a.Current.Revision, Is.EqualTo(0));
    }

    [Test]
    public void RestartMovesEveryClientToTheNewEpoch()
    {
        Probe a = Join();
        Probe b = Join();
        var science = Science(0, true);
        a.Port.Submit(science);
        PumpUntil(() => BothAt(a, b, 1), "science snapshot");

        var restart = Command(0, SessionAction.Restart);
        b.Port.Submit(restart);
        PumpUntil(() => b.ResultFor(restart) != null && a.Current.Epoch == 1 && b.Current.Epoch == 1, "restart");

        Assert.That(b.ResultFor(restart).Epoch, Is.EqualTo(0), "Result keeps the command's epoch.");
        Assert.That(a.Current.Revision, Is.EqualTo(0));
        Assert.That(a.Current.ScienceEnabled, Is.False);
        AssertSameState(AuthorityState, a.Current);

        var stale = Science(0, true); // Old epoch after Restart.
        a.Port.Submit(stale);
        PumpUntil(() => a.ResultFor(stale) != null, "stale command result");
        Assert.That(a.ResultFor(stale).Reason, Is.EqualTo("EpochMismatch"));
    }

    // ---- Identity is the transport's, never the client's ----

    [Test]
    public void ForeignCommandIdIsRejectedBeforeReachingTheAuthority()
    {
        Probe a = Join();
        Probe b = Join();
        var original = Science(0, true);
        a.Port.Submit(original);
        PumpUntil(() => a.ResultFor(original) != null, "original result");
        long revision = AuthorityState.Revision;

        // B copies A's command ID and content: the bridge binds IDs to the real sender connection.
        var copy = new SessionCommand(original.Id, original.Epoch, original.Action, original.RequestedScienceEnabled);
        b.Port.Submit(copy);
        PumpUntil(() => b.ResultFor(copy) != null, "conflict result");
        PumpFor(100);

        Assert.That(b.ResultFor(copy).Accepted, Is.False);
        Assert.That(b.ResultFor(copy).Reason, Is.EqualTo("CommandIdConflict"));
        Assert.That(a.Results.Count, Is.EqualTo(1), "The owner gets no second result.");
        Assert.That(AuthorityState.Revision, Is.EqualTo(revision), "The authority never saw the copy.");
    }

    [Test]
    public void ClientCannotSendHostMessagesOrExtraIdentityBytes()
    {
        Probe a = Join();
        var raw = NewTransport();
        bool rawConnected = false;
        raw.Connected += link => rawConnected = true;
        raw.Connect(1);
        PumpUntil(() => rawConnected && hostConnections.Count == 2, "raw client connected");
        ulong rawId = hostConnections[1];
        MissionSnapshot before = a.Current;

        var forged = new MissionSnapshot(99, 99, SessionState.Completed, before.Power, 999, true);
        raw.SendToHost(1, SessionWireProtocol.EncodeSnapshot(forged));
        raw.SendToHost(1, SessionWireProtocol.EncodeResult(new CommandResult(Guid.NewGuid(), 0, true, "Accepted"), out _));
        byte[] submit = SessionWireProtocol.EncodeSubmit(Command(0, SessionAction.Resume));
        byte[] withClaim = new byte[submit.Length + 9];
        Array.Copy(submit, withClaim, submit.Length);
        withClaim[submit.Length] = 1; // e.g. an "I am the host" flag or a declared player id: not in the format.
        raw.SendToHost(1, withClaim);
        PumpUntil(() => hostErrors.Count >= 3, "host protocol errors");
        PumpFor(100);

        Assert.That(hostErrors, Is.EqualTo(new[]
        {
            (rawId, SessionNetworkCodes.UnexpectedMessageKind),
            (rawId, SessionNetworkCodes.UnexpectedMessageKind),
            (rawId, SessionNetworkCodes.MalformedPayload),
        }), "Errors are attributed to the real sender connection.");
        AssertSameState(before, AuthorityState);
        AssertSameState(before, a.Current);
        Assert.That(a.Snapshots.Count, Is.EqualTo(1), "Nothing forged was relayed to other clients.");
    }

    [Test]
    public void HostProtocolFaultsAreClassifiedAsClientFaultsButHostDiagnosticsAreNot()
    {
        Assert.That(SessionNetworkArguments.IsClientFault(SessionNetworkCodes.UnexpectedMessageKind), Is.True);
        Assert.That(SessionNetworkArguments.IsClientFault(SessionNetworkCodes.UnsupportedVersion), Is.True);
        Assert.That(SessionNetworkArguments.IsClientFault(SessionNetworkCodes.MalformedPayload), Is.True);
        Assert.That(SessionNetworkArguments.IsClientFault(SessionNetworkCodes.ReasonTruncated), Is.False);
        Assert.That(SessionNetworkArguments.IsClientFault(SessionNetworkCodes.SendFailed), Is.False);
        Assert.That(SessionNetworkArguments.IsClientFault(SessionNetworkCodes.UnknownConnection), Is.False);
    }

    // ---- Disconnections ----

    [Test]
    public void HostClosingAClientAbandonsItsPendingCommandsAsResultUnknown()
    {
        Probe a = Join();
        Probe b = Join();
        host.DisconnectClient(hostConnections[0], "Test");
        var inFlight = Science(0, true);
        a.Port.Submit(inFlight); // A does not know yet; the host already dropped it.

        PumpUntil(() => a.State == SessionConnectionState.ConnectionLost, "client A lost");
        PumpUntil(() => bridge.ConnectedClientCount == 1, "host removed A");

        Assert.That(a.Controller.LastDisconnectReason, Is.EqualTo("ClosedByRemote"));
        Assert.That(a.Abandoned, Is.EqualTo(new[] { (inFlight, SessionNetworkCodes.ConnectionLost) }));
        Assert.That(a.Results, Is.Empty, "No invented result.");
        Assert.That(a.Port.TryGetSnapshot(out _), Is.True, "Last authoritative state is kept as last known.");
        Assert.That(b.State, Is.EqualTo(SessionConnectionState.Connected));
    }

    [Test]
    public void RejoinAfterLossIsAFreshJoinWithANewConnectionId()
    {
        Probe a = Join();
        host.DisconnectClient(hostConnections[0], "Test");
        PumpUntil(() => a.State == SessionConnectionState.ConnectionLost, "client lost");

        a.Controller.Connect();
        PumpUntil(() => a.State == SessionConnectionState.Connected, "rejoined");

        Assert.That(hostConnections, Is.EqualTo(new ulong[] { 1, 2 }), "Ids are never reused.");
        Assert.That(bridge.ConnectedClientCount, Is.EqualTo(1));
        AssertSameState(AuthorityState, a.Current);
        var science = Science(0, true);
        a.Port.Submit(science);
        PumpUntil(() => a.ResultFor(science) != null, "result after rejoin");
        Assert.That(a.ResultFor(science).Accepted, Is.True);
    }

    [Test]
    public void HostShutdownIsReportedToEveryClientAsConnectionLost()
    {
        Probe a = Join();
        Probe b = Join();
        bridge.Dispose();
        bridge = null;
        host.Dispose();
        host = null;

        PumpUntil(() => a.State == SessionConnectionState.ConnectionLost
                        && b.State == SessionConnectionState.ConnectionLost, "both lost");

        Assert.That(a.Controller.LastDisconnectReason, Is.EqualTo("ClosedByRemote").Or.EqualTo("Timeout"));
        Assert.That(b.Port.TryGetSnapshot(out _), Is.True);
    }

    [Test]
    public void LocalDisconnectIsSeenByTheHost()
    {
        Probe a = Join();
        a.Controller.Disconnect();
        PumpUntil(() => bridge.ConnectedClientCount == 0, "host saw the close");
        Assert.That(a.State, Is.EqualTo(SessionConnectionState.Disconnected));
        Assert.That(a.Controller.LastDisconnectReason, Is.EqualTo(SessionNetworkCodes.LocalDisconnect));
    }

    [Test]
    public void UnreachableServerEndsAsConnectionLostWithoutAnySnapshot()
    {
        ushort closedPort = host.Port;
        bridge.Dispose();
        bridge = null;
        host.Dispose();
        host = null;
        Probe a = NewClient(new WebSocketClientOptions
            { Address = "127.0.0.1", Port = closedPort, ConnectTimeoutMs = 100, MaxConnectAttempts = 3 });

        a.Controller.Connect();
        PumpUntil(() => a.State == SessionConnectionState.ConnectionLost, "connection attempts exhausted");

        Assert.That(a.Controller.LastDisconnectReason, Is.EqualTo("MaxConnectionAttempts"));
        Assert.That(a.Port.TryGetSnapshot(out _), Is.False);
    }

    [Test]
    public void HostThatRefusesClientsClosesThemAndTheyNeverSynchronize()
    {
        host.AcceptClients = false;
        Probe a = NewClient();
        a.Controller.Connect();
        PumpUntil(() => a.State == SessionConnectionState.ConnectionLost, "refused");

        Assert.That(a.Controller.LastDisconnectReason, Is.EqualTo("ClosedByRemote"));
        Assert.That(a.Port.TryGetSnapshot(out _), Is.False);
        Assert.That(bridge.ConnectedClientCount, Is.EqualTo(0));
    }

    [Test]
    public void SynchronizationTimeoutAbortsALinkThatNeverGetsState()
    {
        bridge.Dispose(); // Link comes up, but nobody answers the SyncRequest.
        bridge = null;
        Probe a = NewClient();
        var states = new List<SessionConnectionState>();
        a.Controller.StateChanged += states.Add;
        a.Controller.Connect();
        PumpUntil(() => a.State == SessionConnectionState.Synchronizing, "synchronizing");
        PumpFor(100);
        Assert.That(a.State, Is.EqualTo(SessionConnectionState.Synchronizing));

        a.Controller.AbortLink(SessionNetworkCodes.SyncTimeout);
        bool hostSawClose = false;
        host.ClientDisconnected += id => hostSawClose = true;
        PumpUntil(() => hostSawClose, "host saw the close");

        Assert.That(a.State, Is.EqualTo(SessionConnectionState.ConnectionLost));
        Assert.That(a.Controller.LastDisconnectReason, Is.EqualTo(SessionNetworkCodes.SyncTimeout));
        Assert.That(SessionConnectionStatus.IsConfirmed(a.State), Is.False);
        Assert.That(states, Is.EqualTo(new[] { SessionConnectionState.Connecting,
            SessionConnectionState.Synchronizing, SessionConnectionState.ConnectionLost }));
    }

    // ---- Options and launch arguments ----

    [Test]
    public void InvalidOptionsFailAtComposition()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnityTransportClient(new WebSocketClientOptions { Port = 0 }));
        Assert.Throws<ArgumentException>(() => new UnityTransportClient(new WebSocketClientOptions { Address = " " }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new UnityTransportClient(new WebSocketClientOptions { MaxConnectAttempts = 0 }));
        using (var other = new UnityTransportHost())
        {
            Assert.Throws<ArgumentException>(() => other.Listen(new WebSocketHostOptions { BindAddress = "not-an-ip" }));
            Assert.Throws<ArgumentException>(() =>
                other.Listen(new WebSocketHostOptions { Port = 0, CertificatePemPath = "only-cert.pem" }));
            // Unity Transport reuses ports; without the probe a second authority would share this one.
            Assert.Throws<InvalidOperationException>(() =>
                other.Listen(new WebSocketHostOptions { BindAddress = "127.0.0.1", Port = host.Port }));
            Assert.Throws<InvalidOperationException>(() =>
                other.Listen(new WebSocketHostOptions { BindAddress = "0.0.0.0", Port = host.Port }));
            Assert.That(other.IsListening, Is.False);
        }
    }

    [Test]
    public void LaunchArgumentsConfigureEndpointsAndPageUrlDefaultsToItsHost()
    {
        var hostOptions = new WebSocketHostOptions();
        SessionNetworkArguments.Apply(hostOptions,
            new[] { "server", "-batchmode", "-trainer-bind", "127.0.0.1", "-trainer-port", "9000", "-trainer-path", "/game" });
        Assert.That((hostOptions.BindAddress, hostOptions.Port, hostOptions.Path), Is.EqualTo(("127.0.0.1", 9000, "/game")));

        var desktop = new WebSocketClientOptions();
        bool connect = SessionNetworkArguments.Apply(desktop,
            new[] { "client", "-trainer-role", "client", "-trainer-server", "game.example.org", "-trainer-port", "443", "-trainer-secure", "-trainer-connect" });
        Assert.That(connect, Is.True);
        Assert.That((desktop.Address, desktop.Port, desktop.Secure), Is.EqualTo(("game.example.org", 443, true)));
        Assert.Throws<ArgumentException>(() => SessionNetworkArguments.Apply(desktop, new[] { "-trainer-port", "70000" }));

        var page = new WebSocketClientOptions { Address = "" };
        connect = SessionNetworkArguments.ApplyPageUrl(page, "https://game.example.org/play/index.html?port=8443&connect=1");
        Assert.That(connect, Is.True);
        Assert.That((page.Address, page.Port, page.Secure), Is.EqualTo(("game.example.org", 8443, true)));

        var local = new WebSocketClientOptions { Address = "" };
        SessionNetworkArguments.ApplyPageUrl(local, "http://127.0.0.1:8000/index.html?server=10.0.0.5&path=%2Fgame");
        Assert.That((local.Address, local.Secure, local.Path), Is.EqualTo(("10.0.0.5", false, "/game")));
        var explicitSecure = new WebSocketClientOptions();
        connect = SessionNetworkArguments.ApplyPageUrl(explicitSecure,
            "https://game.example.org/index.html?server=base.example.org&port=443&path=%2Ftrainer&secure=1&connect=1");
        Assert.That(connect, Is.True);
        Assert.That((explicitSecure.Address, explicitSecure.Port, explicitSecure.Secure, explicitSecure.Path),
            Is.EqualTo(("base.example.org", 443, true, "/trainer")));
    }

    [Test]
    public void ConnectionStatusIsConfirmedOnlyWithAuthoritativeState()
    {
        foreach (SessionConnectionState state in Enum.GetValues(typeof(SessionConnectionState)))
            Assert.That(SessionConnectionStatus.IsConfirmed(state), Is.EqualTo(state == SessionConnectionState.Connected));
        Assert.That(SessionConnectionStatus.Describe(SessionConnectionState.ConnectionLost, "MaxConnectionAttempts"),
            Does.Contain("no se encontró"));
        Assert.That(SessionConnectionStatus.Describe(SessionConnectionState.ConnectionLost, "ProtocolError"),
            Does.Contain("se cortó"));
        Assert.That(SessionConnectionStatus.Describe(SessionConnectionState.ConnectionLost, "Other"), Does.Contain("(Other)"));
    }
}
#endif
