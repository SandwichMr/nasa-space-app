// No test asmdef exists, so this file compiles into Assembly-CSharp; the guard keeps it out of player builds (same
// convention as the phase 1–3 socket tests). REAL WebSocket sockets (Unity Transport 2.7.4) on 127.0.0.1 inside ONE
// process, the phase 4 cable, bridge and adapters, in front of D3's labelled TEST DOUBLE of the authority
// (Phase04NetworkTestAuthority; D2's real authority is not in this base). They prove binding + 'T' 'B' + bridge + adapter
// over the OS network stack; not two processes, LAN, TLS, WebGL or D2's authority.
#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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
using F = Phase04NetworkFingerprint;

public sealed class Phase04NetworkSocketTests
{
    private const double TimeoutSeconds = 10;

    private UnityTransportHost host;
    private BaseOperationsCommandBridge bridge;
    private Phase04NetworkTestAuthority authority;
    private readonly List<UnityTransportClient> transports = new List<UnityTransportClient>();
    private readonly List<BaseOperationsConnectionController> controllers = new List<BaseOperationsConnectionController>();

    private void StartHost()
    {
        authority = new Phase04NetworkTestAuthority(Phase04NetworkTestAuthority.NearStationsScenario());
        host = new UnityTransportHost();
        host.Listen(new WebSocketHostOptions { BindAddress = "127.0.0.1", Port = 0 });
        bridge = new BaseOperationsCommandBridge(authority, host);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (BaseOperationsConnectionController c in controllers) c.Dispose();
        foreach (UnityTransportClient t in transports) t.Dispose();
        controllers.Clear();
        transports.Clear();
        bridge?.Dispose();
        host?.Dispose();
        bridge = null;
        host = null;
    }

    private BaseOperationsConnectionController Join()
    {
        var transport = new UnityTransportClient(new WebSocketClientOptions { Address = "127.0.0.1", Port = host.Port });
        var controller = new BaseOperationsConnectionController(transport);
        transports.Add(transport);
        controllers.Add(controller);
        controller.Connect();
        PumpUntil(() => controller.State == SessionConnectionState.Connected, "joined");
        return controller;
    }

    private void PumpOnce()
    {
        host?.Update();
        bridge?.FlushSnapshots();
        host?.Flush();
        foreach (UnityTransportClient t in transports)
        {
            t.Update();
            t.Flush();
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

    private static BaseOperationsSnapshot Seen(BaseOperationsConnectionController c)
    {
        Assert.That(c.Session.TryGetBaseOperations(out BaseOperationsSnapshot s), Is.True);
        return s;
    }

    private BaseOperationsSnapshot Truth()
    {
        Assert.That(authority.TryGetSnapshot(out BaseOperationsSnapshot s), Is.True);
        return s;
    }

    private string Request(BaseOperationsConnectionController c, Func<BaseOperationsInteractionClient, bool> send)
    {
        var intents = new BaseOperationsInteractionClient(c.Session, authority.Scenario);
        string answer = null;
        intents.Answered += a => answer = a;
        Assert.That(send(intents), "sent");
        PumpUntil(() => answer != null, "answered");
        intents.Dispose();
        return answer;
    }

    /// <summary>Advances the authority, then waits until every joined client has its newest state.</summary>
    private void Run(double seconds)
    {
        for (double t = 0; t < seconds - 1e-9; t += 0.1) authority.Tick(0.1);
        long revision = Truth().Revision;
        PumpUntil(() => controllers.Where(c => c.Session.Membership.IsJoined).All(c => Seen(c).Revision == revision), "clients in sync");
    }

    private void Resume(BaseOperationsConnectionController c)
    {
        CommandResult result = null;
        Action<CommandResult> seen = r => result = r;
        c.Session.CommandCompleted += seen;
        c.Session.Submit(ExpeditionCommand.Session(Guid.NewGuid(), Seen(c).Epoch, ExpeditionAction.Resume));
        PumpUntil(() => result != null, "resume answered");
        c.Session.CommandCompleted -= seen;
        Assert.That(result.Accepted, result.Reason);
    }

    [Test]
    public void TwoSocketClientsHoldTheSamePhase4State()
    {
        StartHost();
        BaseOperationsConnectionController a = Join(), b = Join(), c = Join(); // console, console, air
        Resume(a);
        Assert.That(Request(b, i => i.RequestPlaceOrder(BaseOperationsFixtures.LogisticsConsole, BaseOperationsFixtures.StandardManifest)),
            Is.EqualTo(OutpostReasons.Accepted));
        Assert.That(Request(c, i => i.RequestOperation(BaseOperationsFixtures.AirStation, BaseOperationAction.Inspect)), Is.EqualTo(OutpostReasons.Accepted));
        Run(5);
        Assert.That(Request(c, i => i.RequestOperation(BaseOperationsFixtures.AirStation, BaseOperationAction.Intervene,
            OperationChoice.ReplaceInletFilter)), Is.EqualTo(OutpostReasons.Accepted));
        Run(1);
        BaseOperationsSnapshot truth = Truth();
        PumpUntil(() => new[] { a, b, c }.All(x => Seen(x).Revision == truth.Revision), "all clients at the authority's revision");
        foreach (BaseOperationsConnectionController x in new[] { a, b, c })
            Assert.That(F.Of(Seen(x)), Is.EqualTo(F.Of(truth)), "order, reservation, observation, operation: exactly the authority's");
        Assert.That(Seen(a).Orders.Single().ManifestKey, Is.EqualTo(BaseOperationsFixtures.StandardManifest));
        Assert.That(Seen(a).FreeSpares(BaseOperationsFixtures.InletFilter), Is.EqualTo(0));
    }

    [Test]
    public void FullPhase4SnapshotsCrossRealSocketsInChunks()
    {
        StartHost();
        List<BaseOperationsConnectionController> all = Enumerable.Range(0, 4).Select(_ => Join()).ToList();
        Resume(all[0]);
        Assert.That(Request(all[0], i => i.RequestPlaceOrder(BaseOperationsFixtures.LogisticsConsole, BaseOperationsFixtures.StandardManifest)),
            Is.EqualTo(OutpostReasons.Accepted));
        Run(4);
        BaseOperationsSnapshot truth = Truth();
        foreach (BaseOperationsConnectionController c in all)
            PumpUntil(() => Seen(c).Revision == truth.Revision && Seen(c).Orders.Count == 1, "full state on every client");
        Assert.That(bridge.LargestSnapshotBytes, Is.GreaterThan(1100), "the phase 4 snapshot really needed chunking");
        Console.WriteLine($"largest phase 4 snapshot over sockets: {bridge.LargestSnapshotBytes} B");
    }

    [Test]
    public void ClosingASocketDuringAnInterventionLeavesOnceAndFreesTheSpare()
    {
        StartHost();
        BaseOperationsConnectionController a = Join(), b = Join(), c = Join();
        Resume(a);
        Assert.That(Request(c, i => i.RequestOperation(BaseOperationsFixtures.AirStation, BaseOperationAction.Inspect)), Is.EqualTo(OutpostReasons.Accepted));
        Run(5);
        Assert.That(Request(c, i => i.RequestOperation(BaseOperationsFixtures.AirStation, BaseOperationAction.Intervene,
            OperationChoice.ReplaceInletFilter)), Is.EqualTo(OutpostReasons.Accepted));
        PumpUntil(() => Seen(a).FreeSpares(BaseOperationsFixtures.InletFilter) == 0, "A sees the reservation");
        PlayerId gone = c.Session.Membership.Player;
        c.Disconnect();
        PumpUntil(() => !Seen(a).Outpost.TryGetCrew(gone, out _), "A no longer sees C");
        Assert.That(Seen(a).Operations, Is.Empty, "no phantom operation");
        Assert.That(Seen(a).FreeSpares(BaseOperationsFixtures.InletFilter), Is.EqualTo(1), "the reservation is released");
        Assert.That(bridge.LeaveCount, Is.EqualTo(1));
        Assert.That(authority.Left, Is.EqualTo(new[] { gone }));
        Assert.That(authority.SparesConsumed, Is.EqualTo(0));
    }

    [Test]
    public void ALateSocketJoinerGetsTheCurrentBase()
    {
        StartHost();
        BaseOperationsConnectionController a = Join();
        Resume(a);
        Assert.That(Request(a, i => i.RequestPlaceOrder(BaseOperationsFixtures.LogisticsConsole, BaseOperationsFixtures.StandardManifest)),
            Is.EqualTo(OutpostReasons.Accepted));
        Run(45);
        BaseOperationsConnectionController late = Join();
        BaseOperationsSnapshot first = Seen(late);
        Assert.That(first.SimulatedTimeSeconds, Is.GreaterThan(40));
        Assert.That(first.Orders.Single().State, Is.EqualTo(OrderState.InTransit));
        Assert.That(F.Of(first), Is.EqualTo(F.Of(Truth())));
    }
}
#endif
