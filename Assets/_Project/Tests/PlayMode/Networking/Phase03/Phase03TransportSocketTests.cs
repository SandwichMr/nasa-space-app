// No test asmdef exists, so this file compiles into Assembly-CSharp; the guard keeps it out of player builds (same
// convention as the phase 1 and 2 socket tests). REAL WebSocket sockets (Unity Transport 2.7.4) on 127.0.0.1 inside
// ONE process, with the REAL phase 3 expedition authority (D2) over D1/D3 models and the phase 3 cable. They prove
// binding + 'T' 'E' cable + bridge + adapter over the OS network stack; not two processes, LAN, TLS or WebGL.
#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Crew;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.Eva;
using Trainer.Simulation.LifeSupport;
using Trainer.Unity.Networking;
using Trainer.Unity.Networking.Expedition;

public sealed class Phase03TransportSocketTests
{
    private const double TimeoutSeconds = 10;

    private UnityTransportHost host;
    private ExpeditionCommandBridge bridge;
    private ExpeditionAuthority authority;
    private readonly List<UnityTransportClient> transports = new List<UnityTransportClient>();
    private readonly List<ExpeditionConnectionController> controllers = new List<ExpeditionConnectionController>();

    private void StartHost(ExpeditionScenario scenario)
    {
        authority = new ExpeditionAuthority(scenario, new OutpostBaseSimulation(OutpostSimulationParameters.SyntheticTraining()),
            new EvaSuitSimulation(), CrewRoster.For(scenario.Outpost));
        host = new UnityTransportHost();
        host.Listen(new WebSocketHostOptions { BindAddress = "127.0.0.1", Port = 0 });
        bridge = new ExpeditionCommandBridge(authority, host);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (ExpeditionConnectionController c in controllers) c.Dispose();
        foreach (UnityTransportClient t in transports) t.Dispose();
        controllers.Clear();
        transports.Clear();
        bridge?.Dispose();
        host?.Dispose();
        bridge = null;
        host = null;
    }

    private ExpeditionConnectionController Join()
    {
        var transport = new UnityTransportClient(new WebSocketClientOptions { Address = "127.0.0.1", Port = host.Port });
        var controller = new ExpeditionConnectionController(transport);
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

    private static ExpeditionSessionSnapshot Seen(ExpeditionConnectionController c)
    {
        Assert.That(c.Session.TryGetExpedition(out ExpeditionSessionSnapshot s), Is.True);
        return s;
    }

    [Test]
    public void TwoSocketClientsShareTheSameAuthoritativeExpeditionState()
    {
        StartHost(ExpeditionFixtures.Scenario(false));
        ExpeditionConnectionController a = Join(), b = Join();
        PlayerId aId = a.Session.Membership.Player;
        a.Session.Submit(ExpeditionCommand.Session(Guid.NewGuid(), 0, ExpeditionAction.Resume));
        PumpUntil(() => Seen(b).Outpost.State == SessionState.Running, "running on B");
        a.Session.SubmitMovement(new MovementInput(0, 1, MoveDirection.FromAxes(-1, 1)));
        PumpUntil(() => authority.TryGetSnapshot(out ExpeditionSessionSnapshot s) && s.Outpost.Crew.First(m => m.Player == aId).LastInputSequence == 1,
            "movement applied");
        authority.Tick(0.5);
        authority.TryGetSnapshot(out ExpeditionSessionSnapshot truth);
        PumpUntil(() => Seen(a).Revision == truth.Revision && Seen(b).Revision == truth.Revision, "both clients at the same revision");
        foreach (ExpeditionSessionSnapshot s in new[] { Seen(a), Seen(b) })
        {
            Assert.That(s.Outpost.Crew.First(m => m.Player == aId).Pose.Position, Is.EqualTo(truth.Outpost.Crew.First(m => m.Player == aId).Pose.Position));
            Assert.That(s.Crew.Select(c => c.Zone), Is.EqualTo(truth.Crew.Select(c => c.Zone)));
            Assert.That(s.Suits.Select(x => x.Reading.Levels), Is.EqualTo(truth.Suits.Select(x => x.Reading.Levels)));
            Assert.That(s.Airlock.State, Is.EqualTo(truth.Airlock.State));
        }
    }

    [Test]
    public void FullPhase3SnapshotsCrossRealSocketsInChunks()
    {
        StartHost(ExpeditionFixtures.Scenario(true)); // with the synthetic fixture episode (warning at 45 s)
        List<ExpeditionConnectionController> all = Enumerable.Range(0, 4).Select(_ => Join()).ToList();
        all[0].Session.Submit(ExpeditionCommand.Session(Guid.NewGuid(), 0, ExpeditionAction.Resume));
        PumpUntil(() => authority.TryGetSnapshot(out ExpeditionSessionSnapshot s) && s.Outpost.State == SessionState.Running, "running");
        for (int i = 0; i < 500; i++) authority.Tick(0.1); // past the warning (45 s): advisories and receptions for all
        authority.TryGetSnapshot(out ExpeditionSessionSnapshot truth);
        Assert.That(truth.Receptions.Count, Is.EqualTo(4));
        foreach (ExpeditionConnectionController c in all)
            PumpUntil(() => Seen(c).Revision == truth.Revision && Seen(c).Receptions.Count == 4, "full state on every client");
        Assert.That(bridge.LargestSnapshotBytes, Is.GreaterThan(1100), "the phase 3 snapshot really needed chunking");
    }

    [Test]
    public void ClosingASocketMakesTheMemberLeaveOnce()
    {
        StartHost(ExpeditionFixtures.Scenario(false));
        ExpeditionConnectionController a = Join(), b = Join();
        PlayerId bId = b.Session.Membership.Player;
        b.Disconnect();
        PumpUntil(() => Seen(a).Crew.Count == 1, "A no longer sees B");
        Assert.That(Seen(a).TryGetCrew(bId, out _), Is.False);
    }
}
#endif
