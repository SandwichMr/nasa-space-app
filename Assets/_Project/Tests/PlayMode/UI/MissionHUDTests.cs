#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using Trainer.Application;
using Trainer.Application.Development;
using SessionState = Trainer.Application.SessionState;
using Trainer.Unity.Presentation;
using UnityEditor;
using UnityEngine;

namespace Trainer.Tests.UI
{
    /// <summary>Play-mode smoke checks without a new NUnit assembly. Batch entry point below.
    /// The fixture delays delivery only; the protected shared fake decides every result.</summary>
    public sealed class MissionHUDTests : MonoBehaviour
    {
        private const string RunKey = "Trainer.UI.SmokeTests";
        private const string PrefabPath = "Assets/_Project/Prefabs/UI/MissionHUD.prefab";
        private static int checks;
        private MissionHUD hud;
        private DeliveryFixture delivery;

        [MenuItem("Tools/Trainer/Run Mission HUD checks")]
        public static void PrepareAndRun()
        {
            // Generate script GUIDs via Unity, and serialize only the allowed prefab.
            AssetDatabase.Refresh();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                var root = new GameObject("MissionHUD");
                root.AddComponent<MissionHUD>();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                DestroyImmediate(root);
            }
            UnityEditor.SessionState.SetBool(RunKey, true);
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
            if (state != PlayModeStateChange.EnteredPlayMode || !UnityEditor.SessionState.GetBool(RunKey, false)) return;
            UnityEditor.SessionState.SetBool(RunKey, false);
            new GameObject("UI smoke checks (temporary)").AddComponent<MissionHUDTests>();
        }

        private IEnumerator Start()
        {
            try { CheckBindings(); }
            catch (Exception error) { Fail(error); yield break; }
            // Destroy uses Unity's normal deferred lifecycle in Play mode.
            Destroy(hud.gameObject);
            yield return null;
            try
            {
                Check(delivery.SnapshotListeners == 0 && delivery.ResultListeners == 0, "destroy detaches both listeners");
                delivery.Fake.PublishExample(SessionState.Completed, 5);
                delivery.Flush();
                Debug.Log("MISSION_HUD_CHECKS_PASSED: " + checks);
                Finish(0);
            }
            catch (Exception error) { Fail(error); }
        }

        private static void Fail(Exception error)
        {
            Debug.LogException(error);
            Finish(1);
        }

        private static void Finish(int code)
        {
            if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(code);
            else EditorApplication.ExitPlaymode();
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("UI check failed: " + message);
            checks++;
        }

        private void CheckBindings()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Check(asset != null, "prefab exists");
            hud = Instantiate(asset).GetComponent<MissionHUD>();
            Check(hud != null && hud.Snapshot == null, "prefab starts without authority");
            var fake = new FakeSessionPort();
            hud.Bind(fake);
            Check(hud.Snapshot.State == SessionState.Ready, "initial snapshot binding");
            Check(hud.PowerText.Contains("5000 / 10000 J") && hud.PowerText.Contains("60 W"), "power values and units");
            hud.SetScienceEnabled(true);
            Check(hud.Snapshot.ScienceEnabled && !hud.IsPending && hud.CommandStatus.Contains("aceptada"), "synchronous result");
            fake.PublishExample(SessionState.Completed, 8);
            Check(hud.ResultText.Contains("completada") && hud.ResultText.Contains("8 s"), "result from snapshot");
            hud.SetScienceEnabled(false);
            Check(hud.CommandStatus.Contains("rechazada") && hud.Snapshot.ScienceEnabled, "rejection preserves state");
            hud.Restart();
            Check(hud.Snapshot.Epoch == 1 && !hud.IsPending && hud.CommandStatus.Contains("aceptada"), "restart old-epoch result");

            delivery = new DeliveryFixture();
            hud.Bind(delivery);
            Check(delivery.SnapshotListeners == 1 && delivery.ResultListeners == 1, "one subscription per event");
            hud.SetScienceEnabled(true);
            Check(hud.IsPending && !hud.Snapshot.ScienceEnabled, "pending does not optimistically change state");
            hud.Pause();
            Check(delivery.Submissions == 1, "pending prevents duplicate requests");
            delivery.FlushResults();
            Check(!hud.IsPending && !hud.Snapshot.ScienceEnabled, "result before snapshot does not invent state");
            delivery.FlushSnapshots();
            Check(hud.Snapshot.ScienceEnabled, "later snapshot confirms science");
            var current = hud.Snapshot;
            delivery.Emit(delivery.Initial);
            Check(ReferenceEquals(current, hud.Snapshot), "stale revision ignored");
            hud.Restart();
            delivery.FlushSnapshots();
            Check(hud.Snapshot.Epoch == 1 && hud.IsPending, "restart snapshot before result");
            delivery.FlushResults();
            Check(!hud.IsPending, "restart completes after epoch change");
            delivery.Emit(current);
            Check(hud.Snapshot.Epoch == 1, "old epoch ignored");

            hud.enabled = false;
            Check(hud.Snapshot == null && !hud.IsPending && delivery.SnapshotListeners == 0 && delivery.ResultListeners == 0,
                "disable clears state and listeners");
            delivery.Fake.PublishExample(SessionState.Paused, 3);
            delivery.Flush();
            Check(hud.Snapshot == null, "disabled view receives nothing");
            hud.enabled = true;
            Check(hud.Snapshot.State == SessionState.Paused && delivery.SnapshotListeners == 1, "enable refreshes once");
            hud.Bind(delivery);
            Check(delivery.SnapshotListeners == 1 && delivery.ResultListeners == 1, "same-port rebinding does not duplicate");
            fake.PublishExample(SessionState.Interrupted);
            Check(hud.Snapshot.State == SessionState.Paused, "old port detached");
            hud.SetConnectionStatus(false, "Lost");
            Check(hud.Snapshot == null && delivery.SnapshotListeners == 0 && hud.ConnectionText.Contains("Desconectado"),
                "disconnect erases retained data");
            delivery.ExposeSnapshot = false;
            hud.Bind(delivery);
            hud.Resume();
            Check(hud.Snapshot == null && !hud.IsPending, "no initial snapshot blocks commands");
            delivery.Fake.PublishExample(SessionState.Running);
            delivery.Flush();
            Check(hud.Snapshot.State == SessionState.Running, "first event supplies state");
            hud.Pause();
            Check(hud.IsPending, "deferred pause pending");
            hud.Bind(null);
            delivery.Flush();
            Check(hud.Snapshot == null && !hud.IsPending && hud.CommandStatus == "Sin solicitudes.", "rebind invalidates pending results");
            delivery.ExposeSnapshot = true;
            hud.Bind(delivery);
            delivery.ThrowOnSubmit = true;
            hud.Resume();
            Check(!hud.IsPending && hud.CommandStatus.Contains("Resultado desconocido"), "send failure clears pending without invented result");
            delivery.ThrowOnSubmit = false;
            hud.Resume();
            delivery.Flush();
            Check(!hud.IsPending && hud.CommandStatus.Contains("aceptada"), "action can be retried after send failure");
        }

        private sealed class DeliveryFixture : ISessionPort
        {
            internal readonly FakeSessionPort Fake = new FakeSessionPort();
            internal MissionSnapshot Initial;
            internal bool ExposeSnapshot = true;
            internal int Submissions;
            internal bool ThrowOnSubmit;
            private Action<MissionSnapshot> snapshots;
            private Action<CommandResult> results;
            private readonly List<MissionSnapshot> snapshotQueue = new List<MissionSnapshot>();
            private readonly List<CommandResult> resultQueue = new List<CommandResult>();
            internal int SnapshotListeners => snapshots?.GetInvocationList().Length ?? 0;
            internal int ResultListeners => results?.GetInvocationList().Length ?? 0;
            public event Action<MissionSnapshot> SnapshotChanged { add { snapshots += value; } remove { snapshots -= value; } }
            public event Action<CommandResult> CommandCompleted { add { results += value; } remove { results -= value; } }
            internal DeliveryFixture()
            {
                Fake.TryGetSnapshot(out Initial);
                Fake.SnapshotChanged += snapshotQueue.Add;
                Fake.CommandCompleted += resultQueue.Add;
            }
            public bool TryGetSnapshot(out MissionSnapshot snapshot)
            {
                if (ExposeSnapshot) return Fake.TryGetSnapshot(out snapshot);
                snapshot = null;
                return false;
            }
            public void Submit(SessionCommand command) { Submissions++; if (ThrowOnSubmit) throw new InvalidOperationException("Send failed"); Fake.Submit(command); }
            internal void Emit(MissionSnapshot snapshot) { snapshots?.Invoke(snapshot); }
            internal void FlushSnapshots() { var queue = snapshotQueue.ToArray(); snapshotQueue.Clear(); foreach (var item in queue) Emit(item); }
            internal void FlushResults() { var queue = resultQueue.ToArray(); resultQueue.Clear(); foreach (var item in queue) results?.Invoke(item); }
            internal void Flush() { FlushSnapshots(); FlushResults(); }
        }
    }
}
#endif
