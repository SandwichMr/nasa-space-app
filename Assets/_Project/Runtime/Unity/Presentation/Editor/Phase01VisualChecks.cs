using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Unity.World;
using UnityEditor;
using UnityEngine;
using SessionState = Trainer.Application.SessionState;
using Object = UnityEngine.Object;

namespace Trainer.Unity.Presentation.Editor
{
    /// <summary>Opt-in PlayMode checks and real Game-view captures. Uses the unchanged shared fake only.</summary>
    public static class Phase01VisualChecks
    {
        private const string Key = "Trainer.D4.VisualChecks";
        private static MissionHUD hud;
        private static PowerRoomView room;
        private static Camera camera;
        private static DeferredDelivery delivery;
        private static int checks, stage, nextFrame;
        private static bool failed;
        private static EditorWindow captureView;
        private static readonly List<Camera> mutedCameras = new List<Camera>();
        private static readonly List<GameObject> roots = new List<GameObject>();
        public const string Evidence = "/tmp/phase01-d4-evidence";

        [MenuItem("Tools/Trainer/Phase 01/Run D4 visual checks and captures")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run from Edit mode.");
            Directory.CreateDirectory(Evidence);
            foreach (string file in Directory.GetFiles(Evidence, "*.png")) File.Delete(file);
            if (!UnityEngine.Application.isBatchMode)
            {
                captureView = (EditorWindow)ScriptableObject.CreateInstance(Type.GetType("UnityEditor.GameView,UnityEditor"));
                captureView.ShowUtility();
                captureView.position = new Rect(20, 20, 1440, 940);
                captureView.Focus();
            }
            UnityEditor.SessionState.SetBool(Key, true);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !UnityEditor.SessionState.GetBool(Key, false)) return;
            UnityEditor.SessionState.SetBool(Key, false);
            checks = stage = 0;
            failed = false;
            roots.Clear();
            UnityEngine.Application.logMessageReceived += OnLog;
            try
            {
                CheckBehavior();
                nextFrame = Time.frameCount + 10;
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Fail(error); }
        }

        private static GameObject Spawn(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Check(asset != null, "Prefab loads: " + path);
            var instance = Object.Instantiate(asset);
            roots.Add(instance);
            return instance;
        }

        private static void CheckBehavior()
        {
            hud = Spawn(Phase01VisualAssets.HudPath).GetComponent<MissionHUD>();
            room = Spawn(Phase01VisualAssets.RoomPath).GetComponent<PowerRoomView>();
            var stations = room.GetComponentsInChildren<PowerStationView>();
            Check(stations.Length == 3, "Three real station instances");
            foreach (var sprite in room.GetComponentsInChildren<SpriteRenderer>())
                Check(sprite.sprite != null && sprite.sharedMaterial != null, "Sprite and material imported");
            Check(!room.HasCurrentData, "Room prefab creates no authority");
            mutedCameras.Clear();
            foreach (var existing in Camera.allCameras)
                if (existing.enabled) { mutedCameras.Add(existing); existing.enabled = false; }
            var clearCamera = new GameObject("D4 temporary background clear").AddComponent<Camera>();
            roots.Add(clearCamera.gameObject);
            clearCamera.cullingMask = 0;
            clearCamera.depth = -100;
            clearCamera.clearFlags = CameraClearFlags.SolidColor;
            clearCamera.backgroundColor = new Color(.035f, .06f, .10f);
            camera = new GameObject("D4 temporary capture camera").AddComponent<Camera>();
            roots.Add(camera.gameObject);
            camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.orthographicSize = 4.4f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .06f, .10f);
            // Fixture framing only; no production camera, scene, settings or target is selected/saved.
            Screen.SetResolution(1440, 900, FullScreenMode.Windowed);
            room.Connect(hud, camera);
            delivery = new DeferredDelivery();
            hud.Bind(delivery);
            Check(delivery.SnapshotListeners == 1 && delivery.ResultListeners == 1, "HUD alone subscribes to port");
            Check(RoomListeners() == 1, "Room attaches exactly one presentation listener");
            Check(room.HasCurrentData && stations[0].HasCurrentData, "Initial shared snapshot observed");
            var battery = Find(PowerStationKind.Battery);
            Check(battery.StatusText.Contains("5000 / 10000 J"), "Battery text uses confirmed energy and units");
            var fill = battery.transform.Find("Confirmed energy");
            Check(Mathf.Approximately(fill.localScale.x, .5f), "Battery visual derives fraction from snapshot");
            room.SelectStation(PowerStationKind.Essential);
            Check(room.SelectedStation.Kind == PowerStationKind.Essential && !room.CanRequestScience, "Essential selection sends no science");
            room.SetScienceEnabled(true);
            Check(delivery.Submissions == 0, "Non-science station cannot issue science request");
            room.SelectStation(PowerStationKind.Science);
            Check(room.CanRequestScience && room.SelectedStation.IsSelected, "Science selection exposes controls and marker");
            room.SetScienceEnabled(true);
            Check(hud.IsPending && !hud.Snapshot.ScienceEnabled, "Intent remains pending without optimistic science");
            Check(room.SelectedStation.StatusText.Contains("Desactivada"), "World retains confirmed science while pending");
            hud.SetScienceEnabled(false);
            room.SetScienceEnabled(false);
            Check(delivery.Submissions == 1, "World and HUD share duplicate-request guard");
            delivery.FlushResults();
            Check(!hud.IsPending && !hud.Snapshot.ScienceEnabled, "Result alone does not invent a snapshot");
            delivery.FlushSnapshots();
            Check(Find(PowerStationKind.Science).StatusText.Contains("Servido 20 W"), "Confirmed snapshot updates world watts");
            delivery.Fake.PublishExample(SessionState.Completed, 8);
            delivery.Flush();
            Check(!room.CanRequestScience && hud.ResultText.Contains("completada"), "Terminal state disables world requests");
            hud.Restart();
            delivery.Flush();
            Check(hud.Snapshot.Epoch == 1 && !hud.IsPending, "Restart through original HUD still works");
            room.enabled = false;
            Check(RoomListeners() == 0 && !room.HasCurrentData && !battery.HasCurrentData && room.SelectedStation == null,
                "Room disable clears state and listener");
            room.enabled = true;
            Check(RoomListeners() == 1 && battery.HasCurrentData, "Room enable refreshes without new port listeners");
            room.Connect(hud, camera);
            room.Connect(hud, camera);
            Check(RoomListeners() == 1, "Repeated Connect does not duplicate listeners");
            room.SelectStation(PowerStationKind.Science);
            room.SetScienceEnabled(true);
            hud.SetConnectionStatus(false, "Lost");
            Check(!room.HasCurrentData && !battery.HasCurrentData && !fill.GetComponent<SpriteRenderer>().enabled,
                "Disconnect erases text validity and battery fill");
            Check(room.SelectedStation == null && !hud.IsPending && !room.CanRequestScience, "Disconnect abandons selection and pending intent");
            Check(delivery.SnapshotListeners == 0 && delivery.ResultListeners == 0, "Disconnect removes both port listeners");
            delivery.Flush();
            Check(!room.HasCurrentData, "Late old delivery cannot restore state");
            var fresh = new FakeSessionPort();
            hud.Bind(fresh);
            hud.SetConnectionStatus(true, "DESARROLLO · FakeSessionPort · Sin red ni física");
            Check(room.HasCurrentData && hud.Snapshot.Epoch == 0, "Rebind accepts a fresh epoch without stale data");
            room.SelectStation(PowerStationKind.Science);
            room.SetScienceEnabled(true);
            Check(hud.Snapshot.ScienceEnabled && !hud.IsPending, "Unchanged shared fake also works synchronously");
            hud.enabled = false;
            Check(!room.HasCurrentData && !battery.HasCurrentData, "Disabled HUD invalidates world too");
            hud.enabled = true;
            Check(room.HasCurrentData && hud.ConnectionText.Contains("sin verificar"), "Reenabled HUD refreshes and drops external connection claim");
            room.Connect(null, camera);
            Check(RoomListeners() == 0 && !battery.HasCurrentData, "Disconnecting companion removes HUD listener");
            room.Connect(hud, camera);
            room.SelectStation(PowerStationKind.Science);
            hud.SetConnectionStatus(true, "DESARROLLO · FakeSessionPort · Sin red ni física");
        }

        private static PowerStationView Find(PowerStationKind kind)
        {
            foreach (var station in room.GetComponentsInChildren<PowerStationView>()) if (station.Kind == kind) return station;
            throw new InvalidOperationException("Missing station " + kind);
        }

        private static int RoomListeners()
        {
            var field = typeof(MissionHUD).GetField("PresentationChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            return ((Delegate)field.GetValue(hud))?.GetInvocationList().Length ?? 0;
        }

        private static void Tick()
        {
            if (Time.frameCount < nextFrame) return;
            try
            {
                // Keep all fixture overlays in the captured Game view, even when the Editor chooses another size.
                float left = Mathf.Min(460, Screen.width * .40f);
                camera.pixelRect = new Rect(left, 160, Mathf.Max(1, Screen.width - left - 12), Mathf.Max(1, Screen.height - 290));
                camera.aspect = camera.pixelRect.width / camera.pixelRect.height;
                switch (stage++)
                {
                    case 0:
                        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                            Check(Screen.width >= 1200 && Screen.height >= 720, "Capture viewport fits all three station labels");
                        Debug.Log("D4_CAPTURE_VIEWPORT: " + Screen.width + "x" + Screen.height);
                        Capture("01-science-confirmed.png");
                        break;
                    case 1:
                        delivery = new DeferredDelivery();
                        hud.Bind(delivery);
                        hud.SetConnectionStatus(true, "DESARROLLO · FakeSessionPort · Entrega diferida de prueba");
                        room.SelectStation(PowerStationKind.Science);
                        room.SetScienceEnabled(true);
                        break;
                    case 2: Capture("02-request-pending.png"); break;
                    case 3:
                        delivery.Flush();
                        delivery.Fake.PublishExample(SessionState.Completed, 8);
                        delivery.Flush();
                        break;
                    case 4: Capture("03-mission-result.png"); break;
                    case 5: hud.SetConnectionStatus(false, "Disconnected fixture"); break;
                    case 6: Capture("04-disconnected.png"); break;
                    case 7:
                        Object.Destroy(room.gameObject);
                        break;
                    case 8:
                        Check(RoomListeners() == 0, "Deferred Destroy removes room presentation listener");
                        foreach (string file in new[] { "01-science-confirmed.png", "02-request-pending.png", "03-mission-result.png", "04-disconnected.png" })
                            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                                Check(File.Exists(Path.Combine(Evidence, file)) && new FileInfo(Path.Combine(Evidence, file)).Length > 1000,
                                    "Real Game-view capture written: " + file);
                        Check(!failed, "No Unity errors or exceptions during render/lifecycle");
                        Debug.Log("D4_VISUAL_CHECKS_PASSED: " + checks);
                        Finish(0);
                        return;
                }
                nextFrame = Time.frameCount + 12;
            }
            catch (Exception error) { Fail(error); }
        }

        private static void Capture(string file)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            { Debug.Log("D4_CAPTURE_SKIPPED: graphics disabled"); return; }
            ScreenCapture.CaptureScreenshot(Path.Combine(Evidence, file));
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failed = true;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("D4 check failed: " + message);
            checks++;
        }

        private static void Fail(Exception error) { Debug.LogException(error); Finish(1); }
        private static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            UnityEngine.Application.logMessageReceived -= OnLog;
            foreach (var root in roots) if (root != null) Object.Destroy(root);
            roots.Clear();
            foreach (var existing in mutedCameras) if (existing != null) existing.enabled = true;
            mutedCameras.Clear();
            if (captureView != null) { captureView.Close(); captureView = null; }
            if (UnityEngine.Application.isBatchMode || Array.IndexOf(Environment.GetCommandLineArgs(), "-d4-ci") >= 0)
                EditorApplication.Exit(code);
            else EditorApplication.ExitPlaymode();
        }

        // Delays events, never makes decisions: the existing protected fake remains the only fixture authority.
        private sealed class DeferredDelivery : ISessionPort
        {
            internal readonly FakeSessionPort Fake = new FakeSessionPort();
            internal int Submissions;
            private Action<MissionSnapshot> snapshots;
            private Action<CommandResult> results;
            private readonly List<MissionSnapshot> snapshotQueue = new List<MissionSnapshot>();
            private readonly List<CommandResult> resultQueue = new List<CommandResult>();
            internal int SnapshotListeners => snapshots?.GetInvocationList().Length ?? 0;
            internal int ResultListeners => results?.GetInvocationList().Length ?? 0;
            public event Action<MissionSnapshot> SnapshotChanged { add { snapshots += value; } remove { snapshots -= value; } }
            public event Action<CommandResult> CommandCompleted { add { results += value; } remove { results -= value; } }
            internal DeferredDelivery() { Fake.SnapshotChanged += snapshotQueue.Add; Fake.CommandCompleted += resultQueue.Add; }
            public bool TryGetSnapshot(out MissionSnapshot snapshot) => Fake.TryGetSnapshot(out snapshot);
            public void Submit(SessionCommand command) { Submissions++; Fake.Submit(command); }
            internal void FlushSnapshots() { var values = snapshotQueue.ToArray(); snapshotQueue.Clear(); foreach (var value in values) snapshots?.Invoke(value); }
            internal void FlushResults() { var values = resultQueue.ToArray(); resultQueue.Clear(); foreach (var value in values) results?.Invoke(value); }
            internal void Flush() { FlushSnapshots(); FlushResults(); }
        }
    }
}
