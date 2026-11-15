using System;
using System.IO;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Unity.Bootstrap.BaseOperations;
using Trainer.Unity.Bootstrap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Trainer.Unity.Presentation.BaseOperations.Editor
{
    /// <summary>Rendered productive scene and session controls, using its own local authority. Automated, not human keyboard evidence.</summary>
    public static class Phase04ProductiveVisualChecks
    {
        private const string RunKey = "Phase04.ProductiveRenderChecks";
        private static string Evidence => Phase04RenderEvidence.DirectoryPath("Logs/phase04-integration/productive-visual");
        private static double started;
        private static int stage, nextFrame;
        private static bool ended;
        private static BaseOperationsSceneComposition scene;
        private static BaseOperationsPanel panel;

        public static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Phase04_LivingBase.unity", OpenSceneMode.Single);
            UnityEditor.SessionState.SetBool(RunKey, true);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void Register() { EditorApplication.playModeStateChanged -= Mode; EditorApplication.playModeStateChanged += Mode; }
        private static void Mode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !UnityEditor.SessionState.GetBool(RunKey, false)) return;
            UnityEditor.SessionState.SetBool(RunKey, false);
            started = EditorApplication.timeSinceStartup; stage = 0; ended = false;
            try
            {
                Directory.CreateDirectory(Evidence);
                Phase04RenderEvidence.OpenGameView();
                nextFrame = Time.frameCount + 90;
                EditorApplication.update += Tick;
            }
            catch (Exception e) { End(false, e.ToString()); }
        }

        private static void Tick()
        {
            Phase04RenderEvidence.FocusFixedGameView();
            EditorApplication.QueuePlayerLoopUpdate();
            if (ended) return;
            if (EditorApplication.timeSinceStartup - started > 180) { End(false, "render timeout"); return; }
            if (Time.frameCount < nextFrame) return;
            nextFrame = Time.frameCount + 30;
            try
            {
                switch (stage++)
                {
                    case 0:
                        scene = UnityEngine.Object.FindFirstObjectByType<BaseOperationsSceneComposition>();
                        panel = UnityEngine.Object.FindFirstObjectByType<BaseOperationsPanel>();
                        Require(scene != null && panel != null && scene.PresentedPort != null && scene.PresentedPort.Membership.IsJoined,
                            "productive scene local member and D4 views");
                        var controls = UnityEngine.Object.FindFirstObjectByType<OutpostSessionControls>();
                        Require(controls != null && controls.DrawDepth < BaseOperationsPanel.HudDepth, "session controls draw above the HUD background");
                        Capture("01-ready.png");
                        break;
                    case 1:
                        VerifySessionStrip("01-ready.png");
                        Command(ExpeditionAction.Resume); break;
                    case 2:
                        Require(State().Outpost.State == Trainer.Application.SessionState.Running && State().SimulatedTimeSeconds > 0, "scene clock advances");
                        Capture("02-running.png"); break;
                    case 3:
                        VerifySessionStrip("02-running.png");
                        panel.ToggleDetails(); break;
                    case 4:
                        Require(panel.DetailsOpen, "details open"); Capture("03-details.png"); break;
                    case 5:
                        panel.ClosePanel(); Command(ExpeditionAction.Pause); break;
                    case 6:
                        Require(State().Outpost.State == Trainer.Application.SessionState.Paused, "pause through session command");
                        Capture("04-paused.png"); break;
                    case 7:
                        VerifySessionStrip("04-paused.png");
                        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-lunar-render")>=0) break;
                        End(true, "Productive scene rendered; local member, advancing clock, details and pause. Inspect captures for session buttons and text clipping. No human play or keyboard simulated."); break;
                    case 8:
                        UnityEngine.Object.FindFirstObjectByType<Trainer.Unity.Networking.Outpost.CrewAvatarSync>().enabled=false;
                        Camera.main.transform.position=Array.IndexOf(Environment.GetCommandLineArgs(),"-large-base-render")>=0 ? new Vector3(100,55,-10) : new Vector3(70,20,-10); break;
                    case 9:
                        Require(UnityEngine.Object.FindFirstObjectByType<Trainer.Unity.World.Expedition.LunarSurfaceView>()!=null,"world anchored lunar surface active");
                        Capture("05-exterior-render-probe.png");break;
                    case 10:
                        Camera.main.transform.position=Array.IndexOf(Environment.GetCommandLineArgs(),"-large-base-render")>=0 ? new Vector3(124,71,-10) : new Vector3(94,36,-10);break;
                    case 11:
                        Capture("06-exterior-next-cell-render-probe.png");break;
                    case 12:
                        Camera.main.transform.position=Array.IndexOf(Environment.GetCommandLineArgs(),"-large-base-render")>=0 ? new Vector3(34,20,-10) : new Vector3(9,5,-10);break;
                    case 13:
                        Capture("07-base-overview-render-probe.png");break;
                    case 14:
                        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-checker-render")>=0)break;
                        End(true,"Productive scene and controls passed. Lunar terrain captured at global (70,20), (94,36) and base (9,5). Only the render camera moved; no crew teleport, EVA gameplay or human test claimed.");break;
                    case 15:
                        Camera.main.transform.position=Array.IndexOf(Environment.GetCommandLineArgs(),"-large-base-render")>=0 ? new Vector3(65,20,-10) : new Vector3(23,5,-10);break;
                    case 16:
                        Capture("08-eva-checker-interior.png");break;
                    case 17:
                        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-large-base-render")>=0)break;
                        End(true,"Checkerboard interior, modular walls and EVA/airlock render probes captured; productive controls, clock, details and pause passed. Render camera probes only.");break;
                    case 18:
                        SetProbeHeight(48);Camera.main.transform.position=new Vector3(37,20,-10);break;
                    case 19:
                        Capture("09-large-layout-overview.png");break;
                    case 20:
                        SetProbeHeight(12.5f);Camera.main.transform.position=new Vector3(10,33,-10);break;
                    case 21:
                        Capture("10-water-room-scale.png");break;
                    case 22:
                        End(true,"Large habitat rendered at gameplay scale and full-plan evidence scale; room layout, grey grid, EVA connection and productive controls captured. Only the evidence camera was repositioned.");break;
                }
            }
            catch (Exception e) { End(false, e.ToString()); }
        }

        private static void SetProbeHeight(float height)
        {
            var framing=UnityEngine.Object.FindFirstObjectByType<BaseOperationsCameraFraming>();
            typeof(BaseOperationsCameraFraming).GetField("visibleHeightMeters",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(framing,height);
        }

        private static BaseOperationsSnapshot State()
        {
            if (!scene.PresentedPort.TryGetBaseOperations(out var state)) throw new InvalidOperationException("Missing productive state.");
            return state;
        }
        private static void Command(ExpeditionAction action) => scene.PresentedPort.Submit(ExpeditionCommand.Session(Guid.NewGuid(), State().Epoch, action));
        private static void Require(bool ok, string detail) { if (!ok) throw new InvalidOperationException(detail); UnityEngine.Debug.Log("PRODUCTIVE_RENDER_CHECK " + detail); }
        private static void Capture(string name)
        {
            bool lunarProbe=Array.IndexOf(Environment.GetCommandLineArgs(),"-lunar-render")>=0;
            Require(lunarProbe ? Screen.width>=640 && Screen.height>=480
                : Screen.width == Phase04RenderEvidence.Width && Screen.height == Phase04RenderEvidence.Height,
                $"{(lunarProbe ? "native render probe" : "fixed capture")} resolution {Screen.width}x{Screen.height}");
            ScreenCapture.CaptureScreenshot(Path.Combine(Evidence, name));
        }
        private static void VerifySessionStrip(string name)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Require(texture.LoadImage(File.ReadAllBytes(Path.Combine(Evidence, name))), "capture saved before inspecting " + name);
                float scale = Mathf.Min(texture.width / 1440f, texture.height / 900f);
                float insetX = (texture.width - 1440 * scale) / 2, insetY = (texture.height - 900 * scale) / 2;
                Color pixel = texture.GetPixel(Mathf.RoundToInt(insetX + 18 * scale), texture.height - 1 - Mathf.RoundToInt(insetY + 52 * scale));
                Require(pixel.g > pixel.r + .1f && pixel.g > pixel.b + .05f, "session strip status is actually visible in " + name);
                int textPixels = 0;
                for (int y = Mathf.RoundToInt(insetY + 46 * scale); y < insetY + 73 * scale; y++)
                    for (int x = Mathf.RoundToInt(insetX + 1120 * scale); x < insetX + 1420 * scale; x++)
                    {
                        Color c = texture.GetPixel(x, texture.height - 1 - y);
                        if (Mathf.Min(c.r, Mathf.Min(c.g, c.b)) > .35f) textPixels++;
                    }
                Require(textPixels > 20, "session button text is rendered in " + name);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
        private static void End(bool success, string detail)
        {
            if (ended) return; ended = true;
            EditorApplication.update -= Tick;
            Directory.CreateDirectory(Evidence);
            File.WriteAllText(Path.Combine(Evidence, "verification.txt"), (success ? "PASS" : "FAIL") + "\n" + detail + "\n");
            UnityEngine.Debug.Log("PRODUCTIVE_RENDER_RESULT " + (success ? "PASS" : "FAIL") + " " + detail);
            Phase04RenderEvidence.CloseGameView();
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
