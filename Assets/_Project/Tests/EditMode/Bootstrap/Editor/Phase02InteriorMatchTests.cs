using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Crew;
using Trainer.Application.Outpost;
using Trainer.Unity.Bootstrap;
using Trainer.Unity.Configuration;
using Trainer.Unity.Networking;
using Trainer.Unity.Networking.Outpost;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Reflection;
using Trainer.Tooling.Validation.Phase02;
using Trainer.Unity.World.Outpost;
using Trainer.Tooling.BuildChecks.Phase02;
using SessionState = Trainer.Application.SessionState;

/// <summary>
/// D2 integration: a full two-player match of the shipped <c>Phase02_InteriorBase</c> mission over the REAL stack
/// the scene composes: the shipped scenario asset, D2's <see cref="OutpostAuthority"/> built with the scene's own
/// factories (D1 <c>OutpostBaseSimulation</c>, D3 <c>CrewRoster</c>), D3's real <see cref="OutpostCommandBridge"/>,
/// wire protocol, client controller/adapter and <see cref="CrewInputSender"/>, and the host player's
/// <see cref="LocalOutpostSessionPort"/>. Only the transport is in memory (D3's <see cref="InMemoryOutpostNetwork"/>):
/// no sockets, processes or rendering. It does NOT replace the two-person build test.
/// </summary>
public sealed class Phase02InteriorMatchTests
{
    public const string AssetPath = "Assets/_Project/Data/Scenarios/Phase02/Phase02_InteriorBase_Synthetic.asset";

    [Test]
    public void HostAndRemotePlayerCompleteTheInteriorMissionAndRestart()
    {
        var definition = AssetDatabase.LoadAssetAtPath<OutpostScenarioDefinition>(AssetPath);
        Assert.That(definition, Is.Not.Null, AssetPath);
        Phase02InteriorMatch.Play(definition);
    }

    [Test]
    public void WaterAsLastObjectivePublishesD1StocksOnBothPortsBeforeAnyFurtherTick()
    {
        var definition = AssetDatabase.LoadAssetAtPath<OutpostScenarioDefinition>(AssetPath);
        Assert.That(definition, Is.Not.Null, AssetPath);
        Phase02InteriorMatch.PlayWithWaterLast(definition);
    }

    private static void Set(object instance, string field, object value) => instance.GetType()
        .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(instance, value);

    [Test]
    public void ShutdownDetachesTheHostAndAnOldCallbackCannotCloseTheNewComposition()
    {
        var definition = AssetDatabase.LoadAssetAtPath<OutpostScenarioDefinition>(AssetPath);
        var root = new GameObject("Phase02 shutdown regression");
        try
        {
            var runner = root.AddComponent<OutpostRunner>();
            var bootstrap = root.AddComponent<OutpostBootstrap>();
            var host = root.AddComponent<OutpostNetworkHost>();
            var composition = root.AddComponent<OutpostSceneComposition>();
            Set(bootstrap, "scenario", definition); Set(bootstrap, "runner", runner);
            Set(composition, "bootstrap", bootstrap); Set(composition, "networkHost", host);
            Set(composition, "authorityPlays", false); // No socket or local UI required in EditMode.
            composition.ApplyRole(MissionProcessRole.Authority); // Awake does not run in EditMode.
            var old = composition.ComposeAuthority();
            Assert.That(host.Authority, Is.SameAs(old));
            bootstrap.Shutdown();
            Assert.That(runner.IsBound, Is.False);
            Assert.That(host.Authority, Is.Null, "The released authority must not keep serving clients.");
            bootstrap.Shutdown();
            var current = composition.ComposeAuthority();
            Assert.That(current, Is.Not.SameAs(old));
            var callback = typeof(OutpostSceneComposition).GetMethod("OnAuthorityShutDown", BindingFlags.NonPublic | BindingFlags.Instance);
            callback.Invoke(composition, new object[] { old });
            callback.Invoke(composition, new object[] { old });
            Assert.That(host.Authority, Is.SameAs(current));
            Assert.That(bootstrap.Authority, Is.SameAs(current));
            Assert.That(runner.IsBound, Is.True);
            var local = host.CreateLocalPort(); local.Join();
            Set(composition, "localPort", local);
            local.MembershipChanged += membership => { if (!membership.IsJoined) throw new InvalidOperationException("Teardown subscriber failure"); };
            Assert.Throws<InvalidOperationException>(() => bootstrap.Shutdown());
            Assert.That(host.Authority, Is.Null);
            Assert.That(runner.IsBound, Is.False);
            current.TryGetSnapshot(out var ended);
            Assert.That(ended.Crew, Is.Empty, "Subscriber failure must not retain the local member or server binding.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [Test]
    public void AReentrantHostAttachDuringOldLeaveKeepsTheNewestAuthority()
    {
        var definition = AssetDatabase.LoadAssetAtPath<OutpostScenarioDefinition>(AssetPath);
        OutpostAuthority Create() => OutpostBootstrap.Create(definition, OutpostSceneComposition.CreateSimulation, OutpostSceneComposition.CreateRoster);
        var first = Create(); var superseded = Create(); var newest = Create();
        var root = new GameObject("Phase02 host reentrancy regression");
        try
        {
            var host = root.AddComponent<OutpostNetworkHost>();
            host.Attach(first);
            var network = new InMemoryOutpostNetwork();
            using (var bridge = new OutpostCommandBridge(first, network.HostTransport))
            using (var client = new OutpostConnectionController(network.CreateClient()))
            {
                typeof(OutpostNetworkHost).GetProperty("Bridge").GetSetMethod(true).Invoke(host, new object[] { bridge });
                client.Connect(); network.Pump();
                Assert.That(bridge.JoinedClientCount, Is.EqualTo(1));
                bool replace = true;
                first.SnapshotChanged += s => {
                    if (replace && s.Crew.Count == 0) { replace = false; host.Attach(newest); }
                };
                host.Attach(superseded); // Disposing the old bridge synchronously invokes Leave.
                Assert.That(host.Authority, Is.SameAs(newest), "The outer Attach must not overwrite a newer callback's session.");
                first.TryGetSnapshot(out var ended);
                Assert.That(ended.Crew, Is.Empty);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [Test]
    public void ConfiguredGeometryAuditInstantiatesTheActualPlanWithoutChangingThePrefab()
    {
        var definition = AssetDatabase.LoadAssetAtPath<OutpostScenarioDefinition>(AssetPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/World/Phase02/BaseInterior.prefab");
        WithUntitledScene(() =>
        {
            var check = AuditWithoutEditorChanges(prefab.GetComponent<BaseInteriorView>(), definition.ToScenario(), prefab);
            Assert.That(check.Status, Is.EqualTo(Phase02CheckStatus.Passed), check.Detail);
            Assert.That(check.Detail, Does.Contain("4 instantiated stations").And.Contain("8 obstacle"));
        });
    }

    [Test]
    public void ConfiguredGeometryAuditCleansPreviewAfterGeometryFailure()
    {
        var definition = AssetDatabase.LoadAssetAtPath<OutpostScenarioDefinition>(AssetPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/World/Phase02/BaseInterior.prefab");
        WithUntitledScene(() =>
        {
            int previews = EditorSceneManager.previewSceneCount;
            var fixtureScene = EditorSceneManager.NewPreviewScene();
            GameObject shifted = null, missing = null;
            try
            {
                shifted = UnityEngine.Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(shifted, fixtureScene);
                shifted.transform.position = Vector3.right;
                var invalid = AuditWithoutEditorChanges(shifted.GetComponent<BaseInteriorView>(), definition.ToScenario(), prefab);
                Assert.That(invalid.Status, Is.EqualTo(Phase02CheckStatus.Failed));
                Assert.That(invalid.Detail, Does.Contain("off the plan"), "The check must generate and inspect geometry before failing.");

                missing = new GameObject("Missing projection assets");
                SceneManager.MoveGameObjectToScene(missing, fixtureScene);
                var incomplete = missing.AddComponent<BaseInteriorView>();
                invalid = AuditWithoutEditorChanges(incomplete, definition.ToScenario(), prefab);
                Assert.That(invalid.Status, Is.EqualTo(Phase02CheckStatus.Failed), "An empty/missing projection cannot get a false Passed.");
            }
            finally
            {
                // Close only the preview owned by this fixture, including any surviving objects.
                Assert.That(EditorSceneManager.ClosePreviewScene(fixtureScene), Is.True);
                Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(previews));
                Assert.That(shifted == null && missing == null, Is.True, "Fixture objects must also be destroyed with their preview.");
            }
        });
    }

    private static void WithUntitledScene(Action check)
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene untitled = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
            .FirstOrDefault(s => s.isLoaded && string.IsNullOrEmpty(s.path) && !EditorSceneManager.IsPreviewScene(s));
        bool owned = !untitled.IsValid();
        // Reuse batch's untitled scene without changing its contents or dirty flag. Only when
        // none exists can an additive test scene be created without the unsaved-scene guard.
        if (owned) untitled = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            Assert.That(SceneManager.SetActiveScene(untitled), Is.True);
            Assert.That(untitled.path, Is.Empty, "Reproduce the native untitled/unsaved context.");
            if (owned) EditorSceneManager.MarkSceneDirty(untitled);
            check();
        }
        finally
        {
            try
            {
                if (previous.IsValid() && previous.isLoaded && SceneManager.GetActiveScene() != previous)
                    SceneManager.SetActiveScene(previous);
            }
            finally
            {
                if (owned) EditorSceneManager.CloseScene(untitled, true); // Never close a pre-existing/user scene.
            }
        }
    }

    private static Phase02Check AuditWithoutEditorChanges(BaseInteriorView source, OutpostScenario scenario, GameObject prefab)
    {
        Scene active = SceneManager.GetActiveScene();
        Scene[] scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        bool[] loaded = scenes.Select(s => s.isLoaded).ToArray();
        bool[] dirty = scenes.Select(s => s.isDirty).ToArray();
        string[] paths = scenes.Select(s => s.path).ToArray();
        int[][] roots = scenes.Select(s => s.isLoaded
            ? s.GetRootGameObjects().Select(g => g.GetInstanceID()).ToArray() : Array.Empty<int>()).ToArray();
        int previews = EditorSceneManager.previewSceneCount;
        Func<int[]> objects = () => Resources.FindObjectsOfTypeAll<GameObject>().Where(g => !EditorUtility.IsPersistent(g) && g.scene.IsValid())
            .Select(g => g.GetInstanceID()).OrderBy(id => id).ToArray();
        int[] before = objects();
        bool prefabDirty = EditorUtility.IsDirty(prefab);
        string[] prefabState = prefab.GetComponentsInChildren<Component>(true).Select(c => EditorJsonUtility.ToJson(c)).ToArray();
        byte[] prefabBytes = System.IO.File.ReadAllBytes(AssetDatabase.GetAssetPath(prefab));
        try { return Phase02BuildChecks.AuditConfiguredInterior(source, scenario); }
        finally
        {
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(active));
            Assert.That(Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt), Is.EqualTo(scenes));
            for (int i = 0; i < scenes.Length; i++)
            {
                Assert.That(scenes[i].path, Is.EqualTo(paths[i]));
                Assert.That(scenes[i].isLoaded, Is.EqualTo(loaded[i]));
                Assert.That(scenes[i].isDirty, Is.EqualTo(dirty[i]), "Validation must preserve the scene's dirty flag.");
                if (loaded[i]) Assert.That(scenes[i].GetRootGameObjects().Select(g => g.GetInstanceID()), Is.EqualTo(roots[i]));
            }
            Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(previews), "No validation preview may leak.");
            Assert.That(objects(), Is.EqualTo(before), "No validation GameObject may survive or replace an existing object.");
            Assert.That(EditorUtility.IsDirty(prefab), Is.EqualTo(prefabDirty));
            Assert.That(prefab.GetComponentsInChildren<Component>(true).Select(c => EditorJsonUtility.ToJson(c)), Is.EqualTo(prefabState));
            Assert.That(System.IO.File.ReadAllBytes(AssetDatabase.GetAssetPath(prefab)), Is.EqualTo(prefabBytes));
        }
    }

    [Test]
    public void FakeScannerAllowsEditorFixturesButRejectsTheSameCodeInPlayerSources()
    {
        const string connectedFake = "panel.Bind(new FakeOutpostSessionPort(), scenario);";
        Assert.That(Phase02BuildChecks.UsesDevelopmentSessionInPlayerSource("Assets/_Project/Runtime/Unity/Editor/Fixture.cs", connectedFake), Is.False);
        Assert.That(Phase02BuildChecks.UsesDevelopmentSessionInPlayerSource("Assets/_Project/Runtime/Unity/Bootstrap/Production.cs", connectedFake), Is.True);
        Assert.That(Phase02BuildChecks.ScanProductionFakeUsage().Status, Is.EqualTo(Phase02CheckStatus.Passed));
    }
}

/// <summary>The scripted match, reusable by any runner that can provide the scenario definition.</summary>
public static class Phase02InteriorMatch
{
    private static readonly StationId Air = new StationId("air-scrubber");
    private static readonly StationId Water = new StationId("water-recycler");
    private static readonly StationId Lab = new StationId("science-lab");
    private static readonly StationId Analyzer = new StationId("sample-analyzer");
    private static readonly TaskTypeId CheckAir = new TaskTypeId("check-cabin-air");
    private static readonly TaskTypeId TreatWater = new TaskTypeId("treat-water");
    private static readonly TaskTypeId Experiment = new TaskTypeId("run-experiment");
    private static readonly TaskTypeId Analyze = new TaskTypeId("analyze-sample");

    private sealed class Seat
    {
        public readonly string Name;
        public readonly IOutpostSessionPort Port;
        public readonly CrewInputSender Input;
        public readonly List<CommandResult> Results = new List<CommandResult>();
        public readonly List<string> Abandoned = new List<string>();

        public Seat(string name, IOutpostSessionPort port)
        {
            Name = name;
            Port = port;
            Input = new CrewInputSender(port);
            port.CommandCompleted += Results.Add;
            port.CommandAbandoned += (c, reason) => Abandoned.Add(reason);
        }

        public PlayerId Id => Port.Membership.Player;

        public OutpostSessionSnapshot View
        {
            get
            {
                Assert.That(Port.TryGetSnapshot(out OutpostSessionSnapshot s) && s != null, Is.True, Name + " has state");
                return s;
            }
        }

        public BasePosition Position
        {
            get
            {
                Assert.That(View.TryGetCrew(Id, out CrewMemberState me), Is.True, Name + " sees itself");
                return me.Pose.Position;
            }
        }
    }

    private sealed class Match
    {
        public readonly InMemoryOutpostNetwork Net = new InMemoryOutpostNetwork();
        public readonly OutpostAuthority Authority;
        public readonly OutpostCommandBridge Bridge;
        public readonly List<string> ProtocolErrors = new List<string>();
        public readonly double Speed;
        public readonly Trainer.Simulation.LifeSupport.IOutpostSimulation Simulation;

        public Match(OutpostScenarioDefinition definition)
        {
            Trainer.Simulation.LifeSupport.IOutpostSimulation model = null;
            Authority = OutpostBootstrap.Create(definition, s => model = OutpostSceneComposition.CreateSimulation(s),
                OutpostSceneComposition.CreateRoster);
            Simulation = model;
            Bridge = new OutpostCommandBridge(Authority, Net.HostTransport);
            Bridge.ProtocolError += (id, code) => ProtocolErrors.Add(code);
            Speed = Authority.Scenario.Movement.MaxSpeedMetersPerSecond;
        }

        /// <summary>One frame of delivery: coalesced snapshots go out and every queued message arrives.</summary>
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

        public CommandResult Send(Seat seat, OutpostAction action, StationId station = default, TaskTypeId task = default)
        {
            var command = new OutpostCommand(Guid.NewGuid(), seat.View.Epoch, action, station, task);
            seat.Port.Submit(command);
            Pump();
            CommandResult result = seat.Results.LastOrDefault(r => r.CommandId == command.Id);
            Assert.That(result, Is.Not.Null, $"{seat.Name}: {action} answered");
            return result;
        }

        /// <summary>Walks through waypoints with held directions only (as the keyboard does); the authority moves.</summary>
        public void Walk(Seat seat, params (double x, double y)[] waypoints)
        {
            foreach ((double x, double y) in waypoints)
            {
                for (int guard = 0; ; guard++)
                {
                    Assert.That(guard, Is.LessThan(2000), $"{seat.Name} could not reach ({x}, {y}); stuck at {seat.Position}");
                    BasePosition p = seat.Position;
                    double dx = x - p.X, dy = y - p.Y, d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < 0.02) break;
                    seat.Input.Update(dx / d, dy / d);
                    Pump();
                    Tick(Math.Min(0.05, d / Speed));
                }
            }
            seat.Input.Update(0, 0);
            Pump();
            Tick(0.01);
        }

        public void TickUntil(Func<bool> condition, double maxSeconds, string what)
        {
            double elapsed = 0;
            while (!condition())
            {
                Assert.That(elapsed, Is.LessThan(maxSeconds), "Timed out waiting for: " + what);
                Tick(0.1);
                elapsed += 0.1;
            }
        }
    }

    private static StationState StationOf(Seat seat, StationId id)
    {
        Assert.That(seat.View.TryGetStation(id, out StationState s), Is.True, id.ToString());
        return s;
    }

    private static ObjectiveProgress Objective(Seat seat, string key) => seat.View.Objectives.Single(o => o.Key == key);

    private static void AssertAccepted(CommandResult r, string what) =>
        Assert.That(r.Accepted, Is.True, what + " rejected: " + r.Reason);

    private static void AssertRejected(CommandResult r, string reason, string what)
    {
        Assert.That(r.Accepted, Is.False, what + " must be rejected");
        Assert.That(r.Reason, Is.EqualTo(reason), what);
    }

    private static void AssertSameView(Match match, Seat a, Seat b)
    {
        match.Pump();
        OutpostSessionSnapshot va = a.View, vb = b.View;
        Assert.That(vb.Epoch, Is.EqualTo(va.Epoch), "Same epoch on both players.");
        Assert.That(vb.Revision, Is.EqualTo(va.Revision), "Same confirmed revision on both players.");
        Assert.That(vb.Resources.LifeSupport.Water.PotableKilograms, Is.EqualTo(va.Resources.LifeSupport.Water.PotableKilograms));
        Assert.That(vb.Resources.LifeSupport.Water.UntreatedKilograms, Is.EqualTo(va.Resources.LifeSupport.Water.UntreatedKilograms));
        Assert.That(vb.Resources.Power.EnergyJoules, Is.EqualTo(va.Resources.Power.EnergyJoules));
    }

    public static void PlayWithWaterLast(OutpostScenarioDefinition definition)
    {
        var match = new Match(definition);
        using (var hostPort = new LocalOutpostSessionPort(match.Authority))
        using (var controller = new OutpostConnectionController(match.Net.CreateClient()))
        {
            try
            {
                hostPort.Join();
                var host = new Seat("host", hostPort);
                var remote = new Seat("remote", controller.Session);
                controller.Connect(); match.Pump();
                AssertAccepted(match.Send(host, OutpostAction.Resume), "start");
                match.Walk(host, (3, 5), (3, 7.8));
                AssertAccepted(match.Send(host, OutpostAction.StartTask, Air, CheckAir), "air first");
                match.Tick(10);
                match.Walk(host, (3, 5), (9, 5), (9, 7.8));
                for (int i = 0; i < 2; i++)
                {
                    AssertAccepted(match.Send(host, OutpostAction.StartTask, Water, TreatWater), "water last");
                    match.Tick(20);
                }
                Assert.That(host.View.Outcome, Is.EqualTo(OutpostOutcome.ObjectiveAchieved));
                AssertSameView(match, host, remote);
                var water = match.Simulation.Snapshot.LifeSupport.Water;
                Assert.That(host.View.Resources.LifeSupport.Water.PotableKilograms, Is.EqualTo(water.PotableKilograms));
                Assert.That(remote.View.Resources.LifeSupport.Water.UntreatedKilograms, Is.EqualTo(water.UntreatedKilograms));
                double time = host.View.SimulatedTimeSeconds;
                using (var late = new LocalOutpostSessionPort(match.Authority))
                {
                    late.Join(); match.Pump();
                    Assert.That(host.View.Resources.LifeSupport.Water.PotableKilograms, Is.EqualTo(water.PotableKilograms));
                    Assert.That(host.View.SimulatedTimeSeconds, Is.EqualTo(time), "Join must not repair stocks at the same time.");
                }
            }
            finally { match.Bridge.Dispose(); }
        }
    }

    public static void Play(OutpostScenarioDefinition definition)
    {
        var match = new Match(definition);
        OutpostScenario scenario = match.Authority.Scenario;

        // --- Join: the host plays through its local port, the second player through the real network adapter.
        var hostPort = new LocalOutpostSessionPort(match.Authority);
        var host = new Seat("host", hostPort);
        Assert.That(hostPort.Join().IsJoined, Is.True);
        var controller = new OutpostConnectionController(match.Net.CreateClient());
        var remote = new Seat("remote", controller.Session);
        controller.Connect();
        match.Pump();
        Assert.That(controller.State, Is.EqualTo(SessionConnectionState.Connected));
        Assert.That(remote.Id, Is.Not.EqualTo(host.Id), "Distinct authority-assigned identities.");
        Assert.That(host.View.Crew.Count, Is.EqualTo(2));
        Assert.That(remote.View.Crew.Count, Is.EqualTo(2), "Each player sees both astronauts.");
        Assert.That(host.Position, Is.EqualTo(scenario.Layout.SpawnPoints[0]));
        Assert.That(remote.Position, Is.EqualTo(scenario.Layout.SpawnPoints[1]));
        Assert.That(host.View.State, Is.EqualTo(SessionState.Ready));
        double untreated0 = host.View.Resources.LifeSupport.Water.UntreatedKilograms;
        double potable0 = host.View.Resources.LifeSupport.Water.PotableKilograms;

        // --- Ready: no task before the mission starts; the start button sends Resume.
        AssertRejected(match.Send(remote, OutpostAction.StartTask, Water, TreatWater), OutpostReasons.InvalidState, "task while Ready");
        AssertAccepted(match.Send(host, OutpostAction.Resume), "start mission");
        Assert.That(remote.View.State, Is.EqualTo(SessionState.Running), "The remote player sees the mission start.");

        // --- Reach is checked on the confirmed position.
        AssertRejected(match.Send(remote, OutpostAction.StartTask, Water, TreatWater), OutpostReasons.OutOfRange, "far station");

        // --- Movement: both walk through the door into the water plant; each sees the other move.
        BasePosition remoteStart = remote.Position;
        match.Walk(host, (2, 5), (9, 5), (9, 7.8));
        Assert.That(remote.View.TryGetCrew(host.Id, out CrewMemberState hostSeenByRemote), Is.True);
        Assert.That(hostSeenByRemote.Pose.Position, Is.EqualTo(host.Position), "Remote sees the host's confirmed position.");
        Assert.That(remote.Position, Is.EqualTo(remoteStart), "The remote astronaut did not move by itself.");
        match.Walk(remote, (3.5, 5), (8.4, 5), (8.4, 7.9));
        foreach (CrewMemberState member in host.View.Crew)
            Assert.That(scenario.Layout.IsAdmissible(member.Pose.Position, scenario.Movement.CrewRadiusMeters), Is.True,
                "Confirmed positions respect walls and bounds.");

        // --- Contention at one station; one task per person.
        AssertAccepted(match.Send(host, OutpostAction.StartTask, Water, TreatWater), "host treats water");
        AssertRejected(match.Send(remote, OutpostAction.StartTask, Water, TreatWater), OutpostReasons.StationOccupied, "same station");
        AssertRejected(match.Send(host, OutpostAction.StartTask, Air, CheckAir), OutpostReasons.PlayerBusy, "second task");
        Assert.That(StationOf(remote, Water).Occupant, Is.EqualTo(host.Id), "Remote sees who occupies the station.");

        // --- Parallel work: the remote goes to the lab and runs the optional science.
        match.Walk(remote, (8.4, 5), (15, 5), (15, 7.8));
        AssertAccepted(match.Send(remote, OutpostAction.StartTask, Lab, Experiment), "remote runs science");
        match.TickUntil(() => StationOf(host, Water).LastEnd != null, 40, "first water treatment");
        Assert.That(StationOf(host, Water).LastEnd.Reason, Is.EqualTo(TaskEndReason.Completed));
        Assert.That(Objective(remote, "treat-water").Current, Is.EqualTo(1));
        double untreated1 = host.View.Resources.LifeSupport.Water.UntreatedKilograms;
        double potable1 = host.View.Resources.LifeSupport.Water.PotableKilograms;
        // Crew drink/return water continuously (D1, grams per minute here); the task moves 2.0 → 1.8 kg once.
        Assert.That(untreated1, Is.EqualTo(untreated0 - 2.0).Within(0.02), "−2.0 kg untreated, applied once");
        Assert.That(potable1, Is.EqualTo(potable0 + 1.8).Within(0.02), "+1.8 kg potable, applied once");
        AssertSameView(match, host, remote);

        // --- Second treatment.
        AssertAccepted(match.Send(host, OutpostAction.StartTask, Water, TreatWater), "host treats water again");
        match.TickUntil(() => Objective(host, "treat-water").Current >= 2, 40, "second water treatment");
        Assert.That(host.View.Resources.LifeSupport.Water.UntreatedKilograms, Is.EqualTo(untreated0 - 4.0).Within(0.03));
        match.TickUntil(() => StationOf(remote, Lab).LastEnd != null, 40, "science experiment");
        Assert.That(StationOf(remote, Lab).LastEnd.Reason, Is.EqualTo(TaskEndReason.Completed));
        Assert.That(Objective(host, "science").Current, Is.EqualTo(1));
        Assert.That(host.View.State, Is.EqualTo(SessionState.Running), "Air still pending: the mission goes on.");

        // --- Disconnect during a task: the station is freed, no ghost astronaut stays.
        match.Walk(remote, (15, 5), (15, 2.2));
        AssertAccepted(match.Send(remote, OutpostAction.StartTask, Analyzer, Analyze), "remote analyzes a sample");
        PlayerId oldRemote = remote.Id;
        controller.Disconnect();
        match.Pump();
        Assert.That(host.View.Crew.Count, Is.EqualTo(1), "No ghost after disconnect.");
        Assert.That(StationOf(host, Analyzer).ActiveTask, Is.Null);
        Assert.That(StationOf(host, Analyzer).LastEnd.Reason, Is.EqualTo(TaskEndReason.PerformerLeft));

        // --- Rejoin: a new identity in the freed slot, no duplicate.
        controller.Connect();
        match.Pump();
        Assert.That(controller.State, Is.EqualTo(SessionConnectionState.Connected));
        Assert.That(remote.Id, Is.Not.EqualTo(oldRemote));
        Assert.That(host.View.Crew.Count, Is.EqualTo(2));
        Assert.That(remote.Position, Is.EqualTo(scenario.Layout.SpawnPoints[1]), "Rejoins at its slot's spawn point.");

        // --- Walking away ends the task without its effect; then the air check finishes the mission.
        match.Walk(host, (9, 5), (3, 5), (3, 7.8));
        AssertAccepted(match.Send(host, OutpostAction.StartTask, Air, CheckAir), "host checks the air");
        match.Walk(host, (3, 5));
        Assert.That(StationOf(remote, Air).LastEnd.Reason, Is.EqualTo(TaskEndReason.PerformerMovedAway));
        Assert.That(Objective(host, "check-cabin-air").Current, Is.EqualTo(0));
        match.Walk(host, (3, 7.8));
        AssertAccepted(match.Send(host, OutpostAction.StartTask, Air, CheckAir), "host checks the air again");
        match.TickUntil(() => host.View.State == SessionState.Completed, 30, "mission end");
        Assert.That(remote.View.State, Is.EqualTo(SessionState.Completed));
        Assert.That(remote.View.Outcome, Is.EqualTo(OutpostOutcome.ObjectiveAchieved));
        Assert.That(host.View.SimulatedTimeSeconds, Is.LessThan(240), "Finished before the time limit.");
        AssertRejected(match.Send(remote, OutpostAction.StartTask, Lab, Experiment), OutpostReasons.SessionEnded, "after the end");
        AssertSameView(match, host, remote);

        // --- Retry: any member restarts; everything returns to the scenario's start.
        long epoch = host.View.Epoch;
        AssertAccepted(match.Send(remote, OutpostAction.Restart), "restart");
        Assert.That(host.View.Epoch, Is.EqualTo(epoch + 1));
        Assert.That(remote.View.State, Is.EqualTo(SessionState.Ready));
        Assert.That(host.View.Objectives.All(o => o.Current == 0), Is.True, "Objectives back to zero.");
        Assert.That(host.View.Resources.LifeSupport.Water.UntreatedKilograms, Is.EqualTo(untreated0));
        Assert.That(host.Position, Is.EqualTo(scenario.Layout.SpawnPoints[0]));
        Assert.That(remote.Position, Is.EqualTo(scenario.Layout.SpawnPoints[1]));
        AssertAccepted(match.Send(host, OutpostAction.Resume), "start again");
        match.Walk(remote, (3.5, 3.5));
        Assert.That(host.View.TryGetCrew(remote.Id, out CrewMemberState moved) && moved.Pose.Position.Y > 3.4, Is.True,
            "Movement works in the new epoch (the sender re-sent with the new epoch).");

        // --- Close: the host leaves too; nobody remains.
        controller.Disconnect();
        hostPort.Dispose();
        match.Pump();
        Assert.That(match.Authority.TryGetSnapshot(out OutpostSessionSnapshot last) && last.Crew.Count == 0, Is.True);
        Assert.That(match.ProtocolErrors, Is.Empty, "No wire protocol errors.");
        Assert.That(host.Abandoned.Concat(remote.Abandoned).Where(r => r != OutpostReasons.ConnectionLost && r != OutpostReasons.LocalDisconnect),
            Is.Empty, "No command was lost unexpectedly.");
        controller.Dispose();
        match.Bridge.Dispose();
    }
}
