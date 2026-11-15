using System;
using System.Collections.Generic;
using System.IO;
using Trainer.Application.BaseOperations;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.Eva;
using Trainer.Simulation.LifeSupport;
using Trainer.Tests.UI.Phase04;
using Trainer.Unity.World.BaseOperations;
using Trainer.Unity.World.Expedition;
using Trainer.Unity.World.Outpost;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Trainer.Unity.Presentation.BaseOperations.Editor
{
    /// <summary>Opt-in native PlayMode checks with the COMMON fake. Temporary objects, no saved demo or scene.</summary>
    public static class Phase04VisualChecks
    {
        private const string RunKey = "D4.Phase04.FakeChecks";
        private static readonly PlayerId Me = new PlayerId(1), Other = new PlayerId(2);
        private static readonly List<string> passed = new List<string>();
        private static string Evidence => Path.GetFullPath("docs/phase-04/handoffs/d4-evidence");
        private static BaseOperationsScenario scenario;
        private static BaseOperationsSnapshot snapshot;
        private static FakeBaseOperationsSessionPort fake;
        private static BaseOperationsPresentationTests.FixtureIntents intents;
        private static BaseOperationsPanel panel;
        private static BaseOperationsWorldView world;
        private static ExpeditionWorldView exterior;
        private static GameObject root;
        private static Camera camera;
        private static readonly List<CrewVisual> crew = new List<CrewVisual>();
        private static int stage, nextFrame;
        private static double started;
        private static bool ending;
        private static readonly HashSet<object> blocks = new HashSet<object>();

        [MenuItem("Tools/Trainer/Phase 04/Run D4 fake PlayMode checks and captures")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit PlayMode before starting these checks.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            UnityEditor.SessionState.SetBool(RunKey, true);
            EditorApplication.EnterPlaymode();
        }
        [InitializeOnLoadMethod]
        private static void Register() { EditorApplication.playModeStateChanged -= Mode; EditorApplication.playModeStateChanged += Mode; }
        private static void Mode(PlayModeStateChange mode)
        {
            if (mode != PlayModeStateChange.EnteredPlayMode || !UnityEditor.SessionState.GetBool(RunKey, false)) return;
            UnityEditor.SessionState.SetBool(RunKey, false);
            passed.Clear(); crew.Clear(); blocks.Clear(); stage = 0; ending = false;
            started = EditorApplication.timeSinceStartup;
            UnityEngine.Application.logMessageReceived += Log;
            try
            {
                Directory.CreateDirectory(Evidence);
                BaseOperationsPresentationTests.Run(Check);
                Setup();
                nextFrame = Time.frameCount + 60;
                EditorApplication.update += Tick;
            }
            catch (Exception e) { End(false, e.ToString()); }
        }
        private static void Setup()
        {
            var type = Type.GetType("UnityEditor.GameView,UnityEditor");
            var window = (EditorWindow)ScriptableObject.CreateInstance(type);
            window.ShowUtility(); window.position = new Rect(40, 40, 1440, 930); window.Focus();
            root = new GameObject("D4 Phase04 COMMON FAKE — temporary, not production");
            root.AddComponent<BaseOperationsPresentationTests>();
            camera = new GameObject("Temporary fixture camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform); camera.transform.position = new Vector3(6.5f, 5, -10);
            camera.orthographic = true; camera.orthographicSize = 6.8f;
            camera.backgroundColor = new Color(.025f, .045f, .07f); camera.clearFlags = CameraClearFlags.SolidColor;
            UnityEngine.Application.targetFrameRate = 60;
            scenario = BaseOperationsFixtures.Scenario();
            fake = new FakeBaseOperationsSessionPort(); intents = new BaseOperationsPresentationTests.FixtureIntents(fake);
            var interior = Spawn("World/Phase02/BaseInterior").GetComponent<BaseInteriorView>(); interior.Configure(scenario.Expedition.Outpost);
            exterior = Spawn("World/Phase03/ExpeditionWorld").GetComponent<ExpeditionWorldView>(); Phase04VisualAssets.Set(exterior, "baseView", interior);
            panel = Spawn("UI/Phase04/BaseOperationsPanel").GetComponent<BaseOperationsPanel>();
            world = Spawn("World/Phase04/BaseOperationsWorld").GetComponent<BaseOperationsWorldView>(); world.ConfigureInterior(interior);
            camera.gameObject.AddComponent<BaseOperationsCameraFraming>().Configure(panel);
            var context = new BaseOperationsViewContext(fake, scenario, intents, intents, (owner, blocked) => { if (blocked) blocks.Add(owner); else blocks.Remove(owner); });
            snapshot = BaseOperationsFixtures.Edit(scenario, BaseOperationsFixtures.Initial(scenario, 1, Me, Other))
                .Expedition(e => e.Session(Trainer.Application.SessionState.Running)).Build();
            fake.Publish(snapshot); fake.Join(Me);
            exterior.Bind(new ExpeditionViewContext(fake, scenario.Expedition, intents, (owner, blocked) => { }));
            world.Bind(context); panel.Bind(context);
            foreach (var original in interior.Stations)
                if (scenario.EquipmentAt(original.Placement.Id).HasValue || original.Placement.Id == scenario.Logistics.Console)
                    Check(!original.gameObject.activeSelf, "phase 2 terminal replaced once without duplicate caption: " + original.Placement.Id.Value);
            Check(world.Stations.Count == 3, "three station prefabs mounted from the shared layout");
            Check(root.GetComponentsInChildren<Collider2D>().Length == 0 && root.GetComponentsInChildren<Rigidbody2D>().Length == 0, "D4 adds no competing collision or physics");
            for (int i = 0; i < 2; i++)
            {
                var actor = Spawn("Crew/Phase03/ExpeditionAstronaut");
                crew.Add(actor.GetComponent<CrewVisual>());
                Check(actor.GetComponent<Animator>().runtimeAnimatorController != null, "original astronaut animation controller retained");
            }
            UpdateCrew();
            foreach (var station in world.Stations)
            {
                Check(scenario.Layout.TryGetStation(station.Station, out var placement)
                    && Math.Abs(station.transform.localPosition.x - placement.Position.X) < .0001
                    && Math.Abs(station.transform.localPosition.y - placement.Position.Y) < .0001, "station uses exact authoritative placement: " + station.Station.Value);
            }
            foreach (var renderer in world.GetComponentsInChildren<SpriteRenderer>())
                Check(renderer.sprite != null && renderer.sharedMaterial != null, "station sprite and existing URP material resolve");
        }
        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (ending) return;
            if (EditorApplication.timeSinceStartup - started > 160) { End(false, "PlayMode fixture timeout"); return; }
            if (Time.frameCount < nextFrame) return;
            nextFrame = Time.frameCount + 35;
            try
            {
                switch (stage++)
                {
                    case 0:
                        Check(Math.Abs(camera.orthographicSize - 5.4f) < .0001 && camera.pixelRect.height < Screen.height, "existing-camera framing reserves HUD bands without moving the camera");
                        Capture("01-resources-fake.png"); break;
                    case 1:
                        Publish(BaseOperationsFixtures.Edit(scenario, snapshot).Expedition(e => e.AtTime(130).Member(Me, new BasePosition(9, 1.5)))
                            .Water(new WaterTreatmentStatus(1.5, InstrumentReading.Valid(ReadingUnit.MicrosiemensPerCentimeter, 80, 130), 40, PotableQuality.Verified, 0, 1))
                            .Equipment(Equipment(EquipmentKind.WaterProcessor, EquipmentKnowledge.Degraded, Findings.ProductOffSpec | Findings.ProcessorFilterRestricted, 80)));
                        Check(panel.OpenNearbyStation(), "water decision panel opens only within reach");
                        Capture("02-water-decision-fake.png"); break;
                    case 2:
                        panel.ClosePanel();
                        Publish(BaseOperationsFixtures.Edit(scenario, snapshot).Expedition(e => e.Member(Me, new BasePosition(1, 6)))
                            .Equipment(Equipment(EquipmentKind.AirRevitalization, EquipmentKnowledge.Degraded, Findings.ActiveBedSaturated, .84)));
                        panel.OpenNearbyStation();
                        Capture("03-air-decision-fake.png"); break;
                    case 3:
                        panel.ClosePanel();
                        var repair = new RepairJob(EquipmentKind.WaterProcessor, RepairSteps.Inspect | RepairSteps.Isolate, 130, 1, null);
                        var water = Equipment(EquipmentKind.WaterProcessor, EquipmentKnowledge.Degraded, Findings.ProcessorFilterRestricted, 80, repair);
                        Publish(BaseOperationsFixtures.Edit(scenario, snapshot).Expedition(e => e.Member(Me, new BasePosition(9, 1.5))).Equipment(water)
                            .Operation(new OperationProgress(Guid.NewGuid(), scenario.WaterStation, BaseOperationAction.Intervene, OperationChoice.ReplaceProcessorFilter, Me, 11, 20, false, BaseOperationsFixtures.ProcessorFilter)));
                        panel.OpenNearbyStation();
                        Check(world.Stations[0].StatusText.Contains("REPARACIÓN") && world.Stations[0].ConfirmedFraction == .55, "station shows confirmed repair work without extrapolating");
                        Capture("04-repair-fake.png"); break;
                    case 4:
                        Publish(BaseOperationsFixtures.Edit(scenario, snapshot)
                            .Equipment(Equipment(EquipmentKind.WaterProcessor, EquipmentKnowledge.Degraded, Findings.ProcessorFilterRestricted, 80,
                                new RepairJob(EquipmentKind.WaterProcessor, RepairSteps.Inspect | RepairSteps.Isolate | RepairSteps.Intervene, 130, 1, null)))
                            .Operation(new OperationProgress(Guid.NewGuid(), scenario.WaterStation, BaseOperationAction.TestEquipment, OperationChoice.None, Other, 4, 8, true, null)));
                        Check(world.Stations[0].StatusText.Contains("PRUEBA") && world.Stations[0].StatusText.Contains("OCUPADA"), "test and other occupant are visible in the world");
                        Capture("05-occupied-fake.png"); break;
                    case 5:
                        panel.ClosePanel();
                        var gauges = new ResourceGauge[snapshot.Gauges.Count];
                        for (int i = 0; i < gauges.Length; i++) gauges[i] = snapshot.Gauges[i];
                        gauges[(int)GaugeKind.CabinOxygen] = ResourceGauge.Of(scenario.GaugeRuleOf(GaugeKind.CabinOxygen), InstrumentReading.Unavailable(ReadingUnit.Kilopascals), null, null);
                        gauges[(int)GaugeKind.CabinCarbonDioxide] = ResourceGauge.Of(scenario.GaugeRuleOf(GaugeKind.CabinCarbonDioxide), new InstrumentReading(ReadingStatus.Stale, ReadingUnit.Kilopascals, .4, 100), null, .01);
                        Publish(BaseOperationsFixtures.Edit(scenario, snapshot).Gauges(gauges));
                        Capture("06-unknown-fake.png"); break;
                    case 6:
                        panel.ToggleDetails(); fake.Disconnect("ConnectionLost");
                        Check(!panel.Live && blocks.Count == 0 && world.Stations.Count == 0, "disconnect hides stations and releases focused panel");
                        Capture("07-disconnected-fake.png"); break;
                    case 7:
                        fake.Join(Me);
                        Publish(BaseOperationsFixtures.Edit(scenario, snapshot).EndOperation(scenario.WaterStation)
                            .Equipment(Equipment(EquipmentKind.WaterProcessor, EquipmentKnowledge.Nominal, Findings.None, 20))
                            .Water(new WaterTreatmentStatus(1.5, InstrumentReading.Valid(ReadingUnit.MicrosiemensPerCentimeter, 20, 130), 40, PotableQuality.Verified, 0, 1))
                            .Spare(new SpareStock(BaseOperationsFixtures.ProcessorFilter, 0, 3))
                            .Decision(new DecisionRecord(Guid.NewGuid(), Me, BaseOperationAction.Restore, OperationChoice.None, EquipmentKind.WaterProcessor, 130, DecisionAssessment.Appropriate, "processor-restored")));
                        panel.OpenNearbyStation();
                        Check(world.Stations[0].StatusText.Contains("NORMAL") && snapshot.Water.HoldKilograms == 1.5, "restored service does not invent restored water");
                        Capture("08-restored-fake.png"); break;
                    case 8:
                        panel.ClosePanel();
                        var oldResources = snapshot.Outpost.Resources;
                        var oldPower = oldResources.Power;
                        var lowResources = new OutpostSimulationSnapshot(new PowerSnapshot(130, 2000, oldPower.CapacityJoules,
                            80, 60, 60, 0, 0), 140, 140, new LifeSupportSnapshot(new WaterStock(4, 6, 40, 10), new FoodStock(0, 30),
                            new CabinAirObservation(GasReading.Valid(18, 130), GasReading.Valid(1.1, 130))));
                        Publish(BaseOperationsFixtures.Edit(scenario, snapshot).Expedition(e => e.Resources(lowResources)));
                        gauges = new ResourceGauge[snapshot.Gauges.Count];
                        for (int i = 0; i < gauges.Length; i++)
                        {
                            var old = snapshot.Gauges[i];
                            gauges[i] = ResourceGauge.Of(scenario.GaugeRuleOf(old.Kind), old.Reading, old.Capacity,
                                old.Kind == GaugeKind.CabinCarbonDioxide ? .01 : old.Kind == GaugeKind.StoredFood ? 0 : -.01);
                        }
                        Publish(BaseOperationsFixtures.Edit(scenario, snapshot).Gauges(gauges));
                        Capture("09-low-resources-fake.png"); break;
                    case 9:
                        // Scripted position and suit state, not input or a network game.
                        var margin = new ReturnMarginEstimate(MarginStatus.Sufficient, 130, 45, SuitService.Oxygen, new[] {
                            new ServiceMargin(SuitService.Oxygen, .2, .1, .002, 45), new ServiceMargin(SuitService.Battery, 2000, 500, 15, 100),
                            new ServiceMargin(SuitService.Feedwater, .2, .05, .001, 150) }, new ReturnPlan(8, 10, 0, 5, true));
                        Publish(BaseOperationsFixtures.Edit(scenario, snapshot).Expedition(e => e.Phase(ExpeditionPhase.Underway)
                            .Suit(0, SuitStatus.Sealed, Me, ChecklistSteps.All, new SuitLevels(.35, 2500, .3))
                            .Member(Me, new BasePosition(18, 5), CrewZone.Exterior, 0, true, SuitActivity.Rest, margin)));
                        camera.transform.position = new Vector3(20, 5, -10);
                        Capture("10-exterior-fake.png"); break;
                    case 10:
                        End(true, "Native PlayMode smoke checks with COMMON FAKE only. Captures are scripted states, not authority, networking, build or human play."); break;
                }
            }
            catch (Exception e) { End(false, e.ToString()); }
        }
        private static EquipmentStatus Equipment(EquipmentKind kind, EquipmentKnowledge knowledge, Findings findings, double reading, RepairJob repair = null)
        {
            var readings = kind == EquipmentKind.WaterProcessor
                ? new[] { new ObservedReading("product-conductivity", InstrumentReading.Valid(ReadingUnit.MicrosiemensPerCentimeter, reading, 130)) }
                : new[] { new ObservedReading("active-bed-loading", InstrumentReading.Valid(ReadingUnit.Fraction, reading, 130)),
                    new ObservedReading("standby-bed-loading", InstrumentReading.Valid(ReadingUnit.Fraction, .08, 130)),
                    new ObservedReading("inlet-flow", InstrumentReading.Valid(ReadingUnit.Fraction, 1, 130)) };
            var observation = new EquipmentObservation(1, kind, Me, 130, 190, 1, findings, readings, true);
            return new EquipmentStatus(kind, scenario.StationOf(kind), repair == null ? EquipmentOperatingState.Online : EquipmentOperatingState.Isolated,
                knowledge, findings, 1, observation, repair);
        }
        private static void Publish(BaseOperationsSnapshotBuilder builder) { snapshot = builder.Build(); fake.Publish(snapshot); UpdateCrew(); }
        private static void UpdateCrew()
        {
            foreach (var actor in crew)
            {
                int i = crew.IndexOf(actor); var player = new PlayerId(i + 1);
                snapshot.Outpost.TryGetCrew(player, out var pose);
                actor.transform.position = new Vector3((float)pose.Pose.Position.X, (float)pose.Pose.Position.Y, 0);
                actor.Present(player, i, player == Me, new MoveDirection(0, -1), CrewMotion.Idle, StationId.None);
                if (ExpeditionCrewFrame.TryBuild(snapshot.Expedition, player, Me, out var frame)) actor.GetComponent<ExpeditionCrewBadge>().PresentExpedition(frame);
            }
        }
        private static GameObject Spawn(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/" + name + ".prefab");
            Check(prefab != null, "serialized prefab available: " + name);
            return UnityEngine.Object.Instantiate(prefab, root.transform);
        }
        private static void Capture(string name)
        {
            Check(Screen.width >= 1000 && Screen.height >= 600, "render capture has a usable Game view size");
            ScreenCapture.CaptureScreenshot(Path.Combine(Evidence, name));
            Debug.Log("D4_PHASE04_CAPTURE " + name);
        }
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            passed.Add(message); Debug.Log("D4_PHASE04_CHECK " + message);
        }
        private static void Log(string message, string trace, LogType type)
        {
            if (ending || (type != LogType.Exception && type != LogType.Error && type != LogType.Assert)) return;
            // Exact known Unity Search indexing defect, separately recorded; never ignore other exceptions.
            if (message.StartsWith("ArgumentOutOfRangeException:") && trace.Contains("UnityEditor.Search.SearchDatabase"))
            { Debug.LogWarning("D4_PHASE04_KNOWN_EDITOR_INDEX_ERROR: " + message); return; }
            End(false, message + "\n" + trace);
        }
        private static void End(bool success, string detail)
        {
            if (ending) return; ending = true;
            EditorApplication.update -= Tick; UnityEngine.Application.logMessageReceived -= Log;
            Directory.CreateDirectory(Evidence);
            File.WriteAllText(Path.Combine(Evidence, "verification.txt"), (success ? "PASS" : "FAIL") + " · " + passed.Count + " native PlayMode smoke checks\n"
                + detail + "\n" + string.Join("\n", passed) + "\n");
            Debug.Log("D4_PHASE04_RESULT " + (success ? "PASS" : "FAIL") + " checks=" + passed.Count + "\n" + detail);
            panel?.Unbind(); world?.Unbind(); exterior?.Unbind(); intents?.Dispose();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-d4-ci") >= 0) EditorApplication.Exit(success ? 0 : 1);
            else { if (root != null) UnityEngine.Object.Destroy(root); EditorApplication.ExitPlaymode(); }
        }
    }
}
