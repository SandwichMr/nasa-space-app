// No test asmdef exists, so this file compiles into Assembly-CSharp; the guard keeps it out of player builds (same
// convention as the phase 1–3 PlayMode tests). D5 phase 4: the PRODUCTIVE scene in Play Mode inside the Editor (one
// process, the scene's own composition and clock) and the D4 view prefabs bound to the development fake with unknown
// readings. Neither proves sockets between processes, a build, render on a target device or a session played by people.
#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Trainer.Application.BaseOperations;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;
using UnityEngine;

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

        // The productive-scene check that used [UnityTest] never compiled: without a test asmdef Assembly-CSharp does not
        // reference UnityEngine.TestRunner (autoReferenced: false), so [UnityTest] does not exist here. It is to be run, with the
        // same assertions, against the BUILD: authority + two clients (docs/phase-04/INTEGRATION.md, gates 5–6). Pending.

        /// <summary>D4 prefabs bind to the fake, survive unknown/stale readings without sending intents, and unbind.</summary>
        [Test]
        public void D4ViewsBindToUnknownReadingsWithoutSendingIntents()
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
            fake.Publish(unknown);
            Assert.That(unknown.GaugeOf(GaugeKind.CabinCarbonDioxide).Band, Is.EqualTo(GaugeBand.Unknown), "fixture sanity");
            Assert.That(intents.Calls, Is.EqualTo(0), "a view sent an intent by itself");
            Assert.That(fake.PendingOperations.Count, Is.EqualTo(0), "a view built a command");
            foreach (IBaseOperationsView view in views) view.Unbind();
            TestContext.WriteLine($"{views.Count} D4 view(s) bound to unknown cabin readings without errors. Whether they SHOW 'sin dato' " +
                                  "instead of 0/normal needs D4's visual evidence or a person: not judged here.");
#else
            Assert.Inconclusive("Editor only");
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
