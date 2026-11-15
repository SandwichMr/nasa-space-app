// No test asmdef exists, so this file compiles into Assembly-CSharp; the guard keeps it out of player builds (same
// convention as the phase 1–3 PlayMode tests). D5 phase 4: the PRODUCTIVE scene in Play Mode inside the Editor (one
// process, the scene's own composition and clock) and the D4 view prefabs bound to the development fake with unknown
// readings. Neither proves sockets between processes, a build, render on a target device or a session played by people.
#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Trainer.Application.BaseOperations;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;
using Trainer.Unity.Validation.Phase04;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Trainer.Tests.Phase04.Integration
{
    public sealed class Phase04SceneIntegrationTests
    {
        public const string ScenePath = "Assets/_Project/Scenes/Phase04_LivingBase.unity";
        private static readonly string[] PrefabFolders = { "Assets/_Project/Prefabs/UI/Phase04", "Assets/_Project/Prefabs/World/Phase04" };

        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.Destroy(go);
            spawned.Clear();
        }

        /// <summary>The productive scene runs ONE continuous session whose clock advances and never closes in 5 s.</summary>
        [UnityTest]
        public IEnumerator ProductiveSceneRunsOneContinuousSession()
        {
#if UNITY_EDITOR
            if (!System.IO.File.Exists(ScenePath)) Assert.Inconclusive("pendiente de D2: " + ScenePath + " missing");
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            IBaseOperationsSessionPort port = null;
            IBaseOperationsIntents intents = null;
            BaseOperationsScenario scenario = null;
            float deadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (BaseOperationsAutopilot.TryFindSession(out port, out intents, out scenario) && port.Membership.IsJoined
                    && port.TryGetBaseOperations(out _)) break;
                yield return null;
            }
            if (port == null || !port.Membership.IsJoined)
                Assert.Inconclusive("the scene exposes no joined phase 4 port/intents/scenario in Play Mode (default role may need a host flag; see D2 handoff)");
            Assert.That(port.GetType().Name.IndexOf("Fake", StringComparison.OrdinalIgnoreCase), Is.LessThan(0), "fake port in the productive scene");
            Assert.That(port.TryGetBaseOperations(out BaseOperationsSnapshot first), Is.True);
            Assert.That(first.Session.Mode, Is.EqualTo(BaseSessionMode.Continuous));
            if (first.Outpost.State == Trainer.Application.SessionState.Ready)
                port.Submit(ExpeditionCommand.Session(Guid.NewGuid(), first.Epoch, ExpeditionAction.Resume));
            var observed = new List<BaseOperationsSnapshot>();
            port.BaseOperationsChanged += observed.Add;
            float until = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < until) yield return null;
            port.BaseOperationsChanged -= observed.Add;
            Assert.That(observed.Count, Is.GreaterThan(0), "no snapshot in 5 s");
            BaseOperationsSnapshot last = observed[observed.Count - 1];
            Assert.That(last.Epoch, Is.EqualTo(first.Epoch), "the base was reset");
            Assert.That(last.SimulatedTimeSeconds, Is.GreaterThan(first.SimulatedTimeSeconds), "the clock did not advance");
            Assert.That(last.Outpost.State, Is.Not.EqualTo(Trainer.Application.SessionState.Completed));
            for (int i = 1; i < observed.Count; i++)
                Assert.That(observed[i].SimulatedTimeSeconds, Is.GreaterThanOrEqualTo(observed[i - 1].SimulatedTimeSeconds), "two clocks or a clock going back");
            double real = 5, simulated = last.SimulatedTimeSeconds - first.SimulatedTimeSeconds;
            TestContext.WriteLine($"real {real:0.0} s, simulated {simulated:0.0} s (ratio {simulated / real:0.00}); {observed.Count} snapshots");
#else
            Assert.Inconclusive("Editor only");
            yield break;
#endif
        }

        /// <summary>D4 prefabs bind to the fake, survive unknown/stale readings without sending intents, and unbind.</summary>
        [UnityTest]
        public IEnumerator D4ViewsBindToUnknownReadingsWithoutSendingIntents()
        {
#if UNITY_EDITOR
            var prefabs = new List<GameObject>();
            foreach (string folder in PrefabFolders.Where(UnityEditor.AssetDatabase.IsValidFolder))
                foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                    prefabs.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid)));
            var withViews = prefabs.Where(p => p != null && p.GetComponentsInChildren<IBaseOperationsView>(true).Length > 0).ToList();
            if (withViews.Count == 0) Assert.Inconclusive("pendiente de D4: no prefab with IBaseOperationsView under " + string.Join(", ", PrefabFolders));

            BaseOperationsScenario scenario = BaseOperationsFixtures.Scenario();
            var player = new PlayerId(1);
            var fake = new FakeBaseOperationsSessionPort();
            var intents = new RecordingIntents();
            var context = new BaseOperationsViewContext(fake, scenario, intents, new InertExpeditionIntents(), (owner, blocked) => { });
            var views = new List<IBaseOperationsView>();
            foreach (GameObject prefab in withViews)
            {
                GameObject instance = UnityEngine.Object.Instantiate(prefab);
                spawned.Add(instance);
                views.AddRange(instance.GetComponentsInChildren<IBaseOperationsView>(true));
            }
            fake.Join(player);
            BaseOperationsSnapshot s0 = BaseOperationsFixtures.Initial(scenario, 0, player);
            BaseOperationsSnapshot unknown = BaseOperationsFixtures.Edit(scenario, s0).Expedition(e => e.Session(Trainer.Application.SessionState.Running).AtTime(5)
                .Resources(UnknownCabin(s0.Outpost.Resources))).Build();
            foreach (IBaseOperationsView view in views) view.Bind(context);
            fake.Publish(s0);
            yield return null;
            fake.Publish(unknown);
            yield return null;
            yield return null;
            Assert.That(unknown.GaugeOf(GaugeKind.CabinCarbonDioxide).Band, Is.EqualTo(GaugeBand.Unknown), "fixture sanity");
            Assert.That(intents.Calls, Is.EqualTo(0), "a view sent an intent by itself");
            Assert.That(fake.PendingOperations.Count, Is.EqualTo(0), "a view built a command");
            foreach (IBaseOperationsView view in views) view.Unbind();
            TestContext.WriteLine($"{views.Count} D4 view(s) bound to unknown cabin readings without errors. Whether they SHOW 'sin dato' " +
                                  "instead of 0/normal needs D4's visual evidence or a person: not judged here.");
#else
            Assert.Inconclusive("Editor only");
            yield break;
#endif
        }

        private static OutpostSimulationSnapshot UnknownCabin(OutpostSimulationSnapshot r)
        {
            var life = new LifeSupportSnapshot(r.LifeSupport.Water, r.LifeSupport.Food, new CabinAirObservation(GasReading.Unavailable, GasReading.Unavailable));
            return new OutpostSimulationSnapshot(r.Power, r.ServiceLoadRequestedWatts, r.ServiceLoadServedWatts, life);
        }

        /// <summary>Counts every intent call; sends nothing.</summary>
        private sealed class RecordingIntents : IBaseOperationsIntents
        {
            public int Calls;
            public bool RequestOperation(StationId station, BaseOperationAction action, OperationChoice choice = OperationChoice.None) { Calls++; return false; }
            public bool RequestPlaceOrder(StationId console, string manifestKey) { Calls++; return false; }
            public bool RequestCancelOrder(StationId console, long orderSequence) { Calls++; return false; }
            public bool RequestUnload(StationId console, long orderSequence) { Calls++; return false; }
            public bool RequestCancelOperation(StationId station) { Calls++; return false; }
            public bool HasPending => false;
            public string LastReason => null;
            public event Action<string> Answered { add { } remove { } }
        }

        /// <summary>Phase 3 intents that do nothing (the views under test are phase 4 ones).</summary>
        private sealed class InertExpeditionIntents : IExpeditionIntents
        {
            public bool Interact() => false;
            public bool RequestStart(StationId station, TaskTypeId task) => false;
            public bool RequestCancel(StationId station) => false;
            public bool RequestCycle(StationId airlockPanel) => false;
            public bool RequestDeclareReturn() => false;
            public bool HasPending => false;
            public string LastReason => null;
            public event Action<string> Answered { add { } remove { } }
        }
    }
}
#endif
