// No test asmdef exists, so this file compiles into Assembly-CSharp; the guard keeps it out of player builds
// (same convention as UnityTransportSessionTests). These tests open REAL WebSocket sockets (Unity Transport)
// on 127.0.0.1 inside ONE process. The authority is a minimal TEST DOUBLE over the real D3 CrewRoster (D2's
// mission authority does not exist yet). They prove binding + phase 2 wire + bridge + adapter over the OS
// network stack; they do not prove two processes, LAN, Internet, TLS or a WebGL browser.
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
using Trainer.Application.Outpost;
using Trainer.Simulation.LifeSupport;
using Trainer.Unity.Networking;
using Trainer.Unity.Networking.Outpost;

public sealed class OutpostTransportSocketTests
{
    private const double TimeoutSeconds = 10;

    /// <summary>TEST DOUBLE: presence and movement only (Resume/Pause accepted, tasks rejected). Not D2's authority.</summary>
    private sealed class MovementOnlyAuthority : IOutpostAuthority
    {
        private readonly OutpostScenario scenario;
        private readonly OutpostSimulationSnapshot resources;
        private OutpostSessionSnapshot snapshot;
        private SessionState state = SessionState.Ready;
        private long revision;

        public readonly CrewRoster Roster;
        public readonly List<PlayerId> Left = new List<PlayerId>();
        public event Action<OutpostSessionSnapshot> SnapshotChanged;
        public event Action<PlayerId, CommandResult> CommandCompleted;

        public MovementOnlyAuthority(OutpostScenario scenario)
        {
            this.scenario = scenario;
            Roster = CrewRoster.For(scenario);
            resources = OutpostFixtures.InitialResources(scenario);
            Publish();
        }

        public OutpostMembership Join()
        {
            OutpostMembership m = Roster.Join();
            if (m.IsJoined) Publish();
            return m;
        }

        public bool Leave(PlayerId player)
        {
            if (!Roster.Leave(player)) return false;
            Left.Add(player);
            Publish();
            return true;
        }

        public void Submit(PlayerId sender, OutpostCommand command)
        {
            bool ok = command.Action == OutpostAction.Resume || command.Action == OutpostAction.Pause;
            if (ok) state = command.Action == OutpostAction.Resume ? SessionState.Running : SessionState.Paused;
            if (ok) Publish();
            CommandCompleted?.Invoke(sender, new CommandResult(command.Id, command.Epoch, ok,
                ok ? OutpostReasons.Accepted : OutpostReasons.InvalidState));
        }

        public MovementInputDisposition SubmitMovement(PlayerId sender, MovementInput input)
        {
            MovementInputDisposition d = Roster.SubmitMovement(sender, input, 0);
            if (d == MovementInputDisposition.Applied) Publish();
            return d;
        }

        public bool TryGetSnapshot(out OutpostSessionSnapshot value)
        {
            value = snapshot;
            return true;
        }

        public void Tick(double seconds)
        {
            if (state != SessionState.Running) return;
            IReadOnlyList<CrewMemberState> before = Roster.Members;
            Roster.Advance(seconds);
            if (!ReferenceEquals(before, Roster.Members)) Publish();
        }

        private void Publish()
        {
            snapshot = new OutpostSessionSnapshot(0, revision++, state, OutpostOutcome.InProgress, scenario.Id, Roster.Members,
                scenario.Layout.Stations.Select(s => StationState.Free(s.Id)), resources, OutpostFixtures.ExampleObjectives());
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    private UnityTransportHost host;
    private OutpostCommandBridge bridge;
    private MovementOnlyAuthority authority;
    private readonly List<UnityTransportClient> transports = new List<UnityTransportClient>();
    private readonly List<OutpostConnectionController> controllers = new List<OutpostConnectionController>();

    private void StartHost(OutpostScenario scenario)
    {
        authority = new MovementOnlyAuthority(scenario);
        host = new UnityTransportHost();
        host.Listen(new WebSocketHostOptions { BindAddress = "127.0.0.1", Port = 0 });
        bridge = new OutpostCommandBridge(authority, host);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (OutpostConnectionController c in controllers) c.Dispose();
        foreach (UnityTransportClient t in transports) t.Dispose();
        controllers.Clear();
        transports.Clear();
        bridge?.Dispose();
        host?.Dispose();
        bridge = null;
        host = null;
    }

    private OutpostConnectionController Join()
    {
        var transport = new UnityTransportClient(new WebSocketClientOptions { Address = "127.0.0.1", Port = host.Port });
        var controller = new OutpostConnectionController(transport);
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

    private static CrewMemberState Seen(OutpostConnectionController c, PlayerId id)
    {
        Assert.That(c.Session.TryGetSnapshot(out OutpostSessionSnapshot s), Is.True);
        Assert.That(s.TryGetCrew(id, out CrewMemberState member), Is.True);
        return member;
    }

    [Test]
    public void TwoSocketClientsSeeTheSameConfirmedMovement()
    {
        StartHost(OutpostFixtures.Scenario());
        OutpostConnectionController a = Join(), b = Join();
        PlayerId aId = a.Session.Membership.Player, bId = b.Session.Membership.Player;
        Assert.That(aId, Is.Not.EqualTo(bId));
        PumpUntil(() => b.Session.TryGetSnapshot(out OutpostSessionSnapshot s) && s.Crew.Count == 2, "both visible");

        a.Session.Submit(new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Resume));
        var input = new CrewInputSender(a.Session);
        PumpUntil(() => input.Update(1, 0), "input sent");
        PumpUntil(() => authority.Roster.Members.First(m => m.Player == aId).LastInputSequence == 1, "input applied");
        authority.Tick(0.5);
        BasePosition expected = authority.Roster.Members.First(m => m.Player == aId).Pose.Position;
        PumpUntil(() => Seen(b, aId).Pose.Position.Equals(expected), "B sees A's confirmed position");
        Assert.That(Seen(a, aId).Pose.Position, Is.EqualTo(expected));
        Assert.That(Seen(b, aId).Pose.Motion, Is.EqualTo(CrewMotion.Walking));
    }

    [Test]
    public void LargeSnapshotsCrossRealSocketsInChunks()
    {
        OutpostScenario f = OutpostFixtures.Scenario();
        var stations = Enumerable.Range(0, OutpostLimits.MaxStations)
            .Select(i => new StationPlacement(new StationId("station-with-a-long-name-" + i.ToString("00")),
                new BasePosition(0.5 + i * 0.45, 9.5), 0.4)).ToArray();
        var spawns = Enumerable.Range(0, OutpostLimits.MaxCrew).Select(i => new BasePosition(1 + i * 0.8, 2)).ToArray();
        var layout = new OutpostLayout(f.Layout.Bounds, f.Layout.Obstacles, stations, spawns);
        var tasks = stations.Select((s, i) => new TaskDefinition(new TaskTypeId("task-" + i), s.Id, TaskCategory.Support, 5, 0,
            ResourceEffect.None));
        StartHost(new OutpostScenario("fixture-many-stations", f.Provenance, layout, f.Movement, tasks, f.Simulation));
        List<OutpostConnectionController> all = Enumerable.Range(0, OutpostLimits.MaxCrew).Select(_ => Join()).ToList();
        foreach (OutpostConnectionController c in all)
            PumpUntil(() => c.Session.TryGetSnapshot(out OutpostSessionSnapshot s) && s.Crew.Count == OutpostLimits.MaxCrew,
                "full crew on every client");
    }

    [Test]
    public void ClosingASocketMakesTheMemberLeaveOnce()
    {
        StartHost(OutpostFixtures.Scenario());
        OutpostConnectionController a = Join(), b = Join();
        PlayerId bId = b.Session.Membership.Player;
        b.Disconnect();
        PumpUntil(() => authority.Left.Count == 1, "host noticed the close");
        PumpUntil(() => a.Session.TryGetSnapshot(out OutpostSessionSnapshot s) && s.Crew.Count == 1, "A no longer sees B");
        Assert.That(authority.Left, Is.EqualTo(new[] { bId }));
    }
}
#endif
