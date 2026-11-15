using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Application.Outpost;
using Trainer.Simulation.LifeSupport;
using Trainer.Unity.World.Outpost;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using SessionState = Trainer.Application.SessionState;

namespace Trainer.Unity.Presentation.Outpost.Editor
{
    /// <summary>Explicit PlayMode smoke checks and actual Game view captures. Never saves scenes.
    /// The frozen fake supplies labelled states; it does not prove authority, movement or networking.</summary>
    public static class Phase02VisualChecks
    {
        public const string Evidence = "/tmp/d4-phase02-evidence";
        private const string Key = "Trainer.D4.Phase02.Checks";
        private static readonly List<GameObject> roots = new List<GameObject>();
        private static readonly List<Camera> muted = new List<Camera>();
        private static readonly List<CrewVisual> crewViews = new List<CrewVisual>();
        private static FakeOutpostSessionPort fake;
        private static OutpostScenario scenario;
        private static OutpostPanel panel;
        private static BaseInteriorView interior;
        private static Camera camera;
        private static OutpostCommand pending;
        private static OutpostSessionSnapshot current;
        private static bool blocked, failed;
        private static int checks, stage, nextFrame;
        private static Sprite walkingSprite;
        private static Vector3 rootPosition;
        private static EditorWindow captureView;
        private static int editorIndexingExceptions;

        [MenuItem("Tools/Trainer/Phase 02/Run D4 PlayMode checks and captures")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run from Edit mode.");
            Directory.CreateDirectory(Evidence);
            UnityEditor.SessionState.SetBool(Key,true);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorApplication.playModeStateChanged -= OnMode;
            EditorApplication.playModeStateChanged += OnMode;
        }

        private static void OnMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !UnityEditor.SessionState.GetBool(Key,false)) return;
            UnityEditor.SessionState.SetBool(Key,false);
            checks = stage = editorIndexingExceptions = 0; failed = false; pending = null; blocked = false;
            roots.Clear(); crewViews.Clear(); muted.Clear();
            UnityEngine.Application.logMessageReceived += OnLog;
            try { Setup(); nextFrame = Time.frameCount+16; EditorApplication.update += Tick; }
            catch (Exception e) { Fail(e); }
        }

        private static void Setup()
        {
            if (!UnityEngine.Application.isBatchMode)
            {
                Type type = Type.GetType("UnityEditor.GameView,UnityEditor");
                captureView = (EditorWindow)ScriptableObject.CreateInstance(type);
                captureView.ShowUtility(); captureView.position = new Rect(20,20,1440,940); captureView.Focus();
            }
            Screen.SetResolution(1440,900,FullScreenMode.Windowed);
            foreach (Camera existing in Camera.allCameras)
                if (existing.enabled) { muted.Add(existing); existing.enabled = false; }
            var clear = New("D4 temporary clear camera").AddComponent<Camera>();
            clear.depth = -100; clear.cullingMask = 0; clear.clearFlags = CameraClearFlags.SolidColor;
            clear.backgroundColor = new Color(.025f,.045f,.08f);
            camera = New("D4 temporary overview camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(8,5,-10);
            camera.orthographic = true; camera.orthographicSize = 5.65f;
            camera.backgroundColor = clear.backgroundColor;
            interior = Spawn(Phase02VisualAssets.BasePath).GetComponent<BaseInteriorView>();
            panel = Spawn(Phase02VisualAssets.PanelPath).GetComponent<OutpostPanel>();
            scenario = OutpostFixtures.Scenario();
            fake = new FakeOutpostSessionPort();
            panel.DevelopmentLabel = "DESARROLLO · FakeOutpostSessionPort · estados guionizados · sin red, simulación ni movimiento real";
            fake.CommandCompleted += result => { if (pending != null && pending.Id == result.CommandId) pending = null; };
            fake.CommandAbandoned += (command,reason) => { if (ReferenceEquals(pending,command)) pending = null; };
            panel.Bind(fake,scenario,interior);
            panel.ConfigureInteraction(StartFixture,CancelFixture,()=>pending,value=>blocked=value);
            current = OutpostFixtures.Initial(scenario,0,new PlayerId(1),new PlayerId(2));
            fake.Publish(current);
            Check(!panel.HasCurrentData,"Snapshot before Joined is not live");
            fake.Join(new PlayerId(1));
            Check(panel.HasCurrentData,"Joined exposes confirmed snapshot");
            Check(interior.Stations.Count==3,"Three distinguishable physical stations");
            Check(interior.Blockers.Count==scenario.Layout.Obstacles.Count,"No invented blocking geometry");
            for (int i=0;i<interior.Blockers.Count;i++)
            {
                Bounds drawn = interior.Blockers[i].bounds;
                BaseRect exact = scenario.Layout.Obstacles[i];
                Check(Math.Abs(drawn.min.x-exact.MinX)<.001 && Math.Abs(drawn.min.y-exact.MinY)<.001 &&
                    Math.Abs(drawn.max.x-exact.MaxX)<.001 && Math.Abs(drawn.max.y-exact.MaxY)<.001,"Visible wall footprint equals authority rectangle");
            }
            Check(interior.GetComponentsInChildren<Collider2D>(true).Length==0 && interior.GetComponentsInChildren<Rigidbody2D>(true).Length==0,"Visuals add no second collision authority");
            foreach (SpriteRenderer renderer in interior.GetComponentsInChildren<SpriteRenderer>())
                Check(renderer.sprite != null && renderer.sharedMaterial != null,"Imported renderer references present");
            Check(!panel.OpenStation(OutpostFixtures.WaterProcessor) && !panel.RequestStart(OutpostFixtures.TreatWater) && fake.PendingCommands.Count==0,"Remote station UI cannot send actions");
            for (int slot=0;slot<8;slot++)
            {
                CrewVisual view = Spawn(Phase02VisualAssets.CrewPath).GetComponent<CrewVisual>();
                crewViews.Add(view);
                // ONLY this labelled Editor fixture places roots. Production roots belong to D3.
                view.transform.position = new Vector3(1.4f+slot%4*1.5f, 2.4f+slot/4*1.5f,0);
                view.Present(new PlayerId(slot+1),slot,slot==0,new MoveDirection(0,-1),CrewMotion.Idle,StationId.None);
                Check(view.GetComponent<Animator>().runtimeAnimatorController != null,"Actual controller on astronaut prefab");
            }
            Check(crewViews[0].CrewColor!=crewViews[1].CrewColor && crewViews[0].IsLocal && !crewViews[1].IsLocal,"Slot identity and local marker distinguish astronauts");
            Check(crewViews[0].GetComponentsInChildren<Collider2D>().Length==0,"Crew prefab is visual only");
            var sheet = AssetDatabase.LoadAllAssetsAtPath(Phase02VisualAssets.Art+"astronaut-sheet.png").OfType<Sprite>().ToArray();
            Check(sheet.Length==16,"Sixteen real imported animation frames");
            foreach (string path in Directory.GetFiles(Phase02VisualAssets.Animation,"*.anim"))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                Check(clip != null && AnimationUtility.GetAnimationClipSettings(clip).loopTime,"Real looping clip: "+clip.name);
                Check(AnimationUtility.GetObjectReferenceCurveBindings(clip).Length==1,"Clip drives sprite frames");
            }
            Check(OutpostPanel.Reading(GasReading.Unavailable)=="sin lectura" &&
                OutpostPanel.Reading(new GasReading(ReadingStatus.Stale,20,2)).Contains("ANTIGUA"),"Absent and stale gas readings never become current zero");
        }

        private static bool StartFixture(StationId station,TaskTypeId task)
        {
            if (pending != null) return false;
            pending = OutpostCommand.StartTask(Guid.NewGuid(),current.Epoch,station,task);
            fake.Submit(pending); return true;
        }
        private static bool CancelFixture(StationId station)
        {
            if (pending != null) return false;
            pending = OutpostCommand.CancelTask(Guid.NewGuid(),current.Epoch,station);
            fake.Submit(pending); return true;
        }
        private static void Publish(OutpostSessionSnapshot state) { current=state; fake.Publish(state); }

        private static void Tick()
        {
            if (Time.frameCount<nextFrame) return;
            try
            {
                float scale = Mathf.Min(Screen.width/1280f,Screen.height/720f);
                float offset = (Screen.height-720*scale)/2;
                camera.pixelRect = new Rect(0,offset+96*scale,Screen.width,490*scale);
                camera.aspect = camera.pixelRect.width/camera.pixelRect.height;
                switch (stage++)
                {
                    case 0:
                        Debug.Log("D4_PHASE02_CAPTURE_VIEWPORT: "+Screen.width+"x"+Screen.height);
                        Check(Screen.width>=1200 && Screen.height>=720,"Game view has usable capture resolution");
                        Capture("01-base-and-eight-identities.png");
                        break;
                    case 1:
                        for(int i=2;i<crewViews.Count;i++) crewViews[i].gameObject.SetActive(false);
                        var pose = new CrewPose(new BasePosition(3,7),new MoveDirection(0,1),CrewMotion.Idle);
                        Publish(OutpostFixtures.Next(current,crew:OutpostFixtures.WithMember(current,new PlayerId(1),pose,1),state:SessionState.Running));
                        crewViews[0].transform.position = new Vector3(3,7,0);
                        Check(panel.OpenStation(OutpostFixtures.WaterProcessor) && blocked,"Nearby task panel blocks D3 input callback");
                        Check(panel.RequestStart(OutpostFixtures.TreatWater) && panel.IsPending,"Start is an intention pending authority");
                        Check(!panel.RequestStart(OutpostFixtures.TreatWater) && fake.PendingCommands.Count==1,"No duplicate submission while request pending");
                        Check(interior.Stations[0].StatusText=="LIBRE" && interior.Stations[0].ConfirmedFraction==0,"No optimistic occupation or progress");
                        break;
                    case 2: Capture("02-nearby-request-pending.png"); break;
                    case 3:
                        fake.Accept(pending.Id);
                        Check(!panel.IsPending && interior.Stations[0].ConfirmedFraction==0,"Result before snapshot does not fabricate progress");
                        var active = new StationState(OutpostFixtures.WaterProcessor,StationAvailability.Occupied,
                            new TaskProgress(OutpostFixtures.TreatWater,new PlayerId(1),7,20,false),null);
                        Publish(OutpostFixtures.Next(current,stations:OutpostFixtures.WithStation(current,active)));
                        Check(Math.Abs(interior.Stations[0].ConfirmedFraction-.35)<.0001 && interior.Stations[0].StatusText.Contains("TÚ"),"Occupation and progress only from confirmed snapshot");
                        crewViews[0].Present(new PlayerId(1),0,true,new MoveDirection(0,1),CrewMotion.Idle,OutpostFixtures.WaterProcessor);
                        break;
                    case 4: Capture("03-confirmed-occupation-and-task.png"); break;
                    case 5:
                        Check(panel.RequestCancel() && panel.IsPending,"Only own nearby task can cancel");
                        fake.Reject(pending.Id,OutpostReasons.InvalidState);
                        Check(panel.Feedback.Contains("estado actual") && !panel.IsPending,"Rejected command leaves confirmed task untouched");
                        Check(Math.Abs(interior.Stations[0].ConfirmedFraction-.35)<.0001,"Rejection never rolls back authoritative progress");
                        var occupiedByOther = new StationState(OutpostFixtures.WaterProcessor,StationAvailability.Occupied,
                            new TaskProgress(OutpostFixtures.TreatWater,new PlayerId(2),11,20,true),null);
                        Publish(OutpostFixtures.Next(current,stations:OutpostFixtures.WithStation(current,occupiedByOther)));
                        Check(!panel.RequestCancel() && !panel.RequestStart(OutpostFixtures.TreatWater),"Cannot cancel another astronaut or start at occupied station");
                        Check(interior.Stations[0].StatusText.Contains("SIN SERVICIO"),"Stalled progress explicitly labelled");
                        break;
                    case 6: Capture("04-other-occupant-stalled.png"); break;
                    case 7:
                        panel.CloseStation(); Check(!blocked,"Closing panel releases D3 input callback");
                        camera.transform.position = new Vector3(4,7,-10); camera.orthographicSize = 3;
                        crewViews[0].Present(new PlayerId(1),0,true,new MoveDirection(1,0),CrewMotion.Walking,StationId.None);
                        rootPosition = crewViews[0].transform.position;
                        break;
                    case 8:
                        crewViews[0].GetComponent<Animator>().Play("WalkRight",0,.05f);
                        crewViews[0].GetComponent<Animator>().Update(0);
                        walkingSprite = crewViews[0].transform.Find("Body").GetComponent<SpriteRenderer>().sprite;
                        Check(crewViews[0].Walking && crewViews[0].FacingIndex==1 && walkingSprite.name.StartsWith("crew-1-"),"Animator really switches to walking right");
                        Capture("05-walk-right-frame-a.png");
                        break;
                    case 9:
                        var animator = crewViews[0].GetComponent<Animator>();
                        animator.Play("WalkRight",0,.62f); animator.Update(0);
                        var another = crewViews[0].transform.Find("Body").GetComponent<SpriteRenderer>().sprite;
                        Check(another!=walkingSprite,"Two different walk sprites rendered by the real Animator");
                        Check(crewViews[0].transform.position==rootPosition,"Animation does not move the root");
                        Capture("06-walk-right-frame-b.png");
                        break;
                    case 10:
                        foreach (var direction in new[] {new MoveDirection(0,-1),new MoveDirection(1,0),new MoveDirection(0,1),new MoveDirection(-1,0)})
                        {
                            crewViews[0].Present(new PlayerId(1),0,true,direction,CrewMotion.Idle,StationId.None);
                            crewViews[0].GetComponent<Animator>().Update(.1f);
                            Check(!crewViews[0].Walking,"Confirmed idle selects idle animation");
                        }
                        // Out of range auto-closes a panel on a subsequent confirmed snapshot.
                        Check(panel.OpenStation(OutpostFixtures.WaterProcessor),"Reopen nearby panel");
                        Publish(OutpostFixtures.Next(current,crew:OutpostFixtures.WithMember(current,new PlayerId(1),CrewPose.IdleAt(new BasePosition(3,3)),2)));
                        Check(!panel.PanelOpen && !blocked && !panel.RequestStart(OutpostFixtures.TreatWater),"Leaving confirmed reach closes panel and prevents UI actions");
                        panel.enabled=false;
                        Check(!panel.HasCurrentData && interior.Stations[0].ConfirmedFraction==0,"Disable clears station readings");
                        panel.enabled=true;
                        Check(panel.HasCurrentData,"Enable reads current port without duplicate bind");
                        panel.Bind(fake,scenario,interior); panel.Bind(fake,scenario,interior);
                        Check(ListenerCount(fake,"SnapshotChanged")==1 && ListenerCount(fake,"MembershipChanged")==1,"Rebind preserves a single observer per event");
                        fake.Disconnect(OutpostReasons.ConnectionLost);
                        foreach(var view in crewViews) view.gameObject.SetActive(false); // Fixture only; D3 removes real crew on Leave.
                        Check(!panel.HasCurrentData && !panel.PanelOpen && !blocked && interior.Stations[0].StatusText=="Sin datos vigentes","Disconnect invalidates every reading and input blocker");
                        camera.transform.position = new Vector3(8,5,-10); camera.orthographicSize = 5.65f;
                        break;
                    case 11: Capture("07-disconnected.png"); break;
                    case 12:
                        var replacement = new FakeOutpostSessionPort();
                        var initial = OutpostFixtures.Initial(scenario,0,new PlayerId(7));
                        replacement.Publish(initial); replacement.Join(new PlayerId(7));
                        panel.Bind(replacement,scenario,interior);
                        Check(panel.HasCurrentData && panel.Snapshot.Epoch==0,"New port with older epoch accepted after rebind");
                        Check(ListenerCount(fake,"SnapshotChanged")==0,"Previous port detached");
                        fake.Publish(OutpostFixtures.Next(current,newEpoch:2));
                        Check(panel.Snapshot.Epoch==0,"Old port callbacks cannot revive stale state");
                        PublishTerminal(replacement,initial);
                        Check(!panel.OpenStation(OutpostFixtures.WaterProcessor),"Terminal mission disables task panel");
                        Object.Destroy(panel.gameObject);
                        break;
                    case 13:
                        Check(!failed,"No D4/runtime errors during animation, lifecycle and render");
                        Debug.Log("D4_EDITOR_SEARCH_STARTUP_EXCEPTIONS: "+editorIndexingExceptions+" (UnityEditor.Search.SearchDatabase, reported separately)");
                        foreach(string file in new[] {"01-base-and-eight-identities.png","02-nearby-request-pending.png","03-confirmed-occupation-and-task.png","04-other-occupant-stalled.png","05-walk-right-frame-a.png","06-walk-right-frame-b.png","07-disconnected.png"})
                            Check(File.Exists(Path.Combine(Evidence,file)) && new FileInfo(Path.Combine(Evidence,file)).Length>10000,"Real capture saved: "+file);
                        Debug.Log("D4_PHASE02_CHECKS_PASSED: "+checks);
                        Finish(0); return;
                }
                nextFrame = Time.frameCount+13;
            }
            catch(Exception e) { Fail(e); }
        }

        private static void PublishTerminal(FakeOutpostSessionPort target,OutpostSessionSnapshot previous) =>
            target.Publish(OutpostFixtures.Next(previous,state:SessionState.Completed,outcome:OutpostOutcome.TimeExpired));
        private static int ListenerCount(object source,string name) =>
            ((Delegate)source.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(source))?.GetInvocationList().Length ?? 0;
        private static GameObject New(string name) { var go = new GameObject(name); roots.Add(go); return go; }
        private static GameObject Spawn(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Check(prefab!=null,"Real prefab imported: "+path);
            var go = Object.Instantiate(prefab); roots.Add(go); return go;
        }
        private static void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(Evidence,name));
        private static void Check(bool pass,string label)
        {
            if(!pass) throw new InvalidOperationException("D4 check failed: "+label);
            checks++; Debug.Log("D4_CHECK "+checks+": "+label);
        }
        private static void OnLog(string message,string trace,LogType type)
        {
            if (type==LogType.Exception && trace.Contains("UnityEditor.Search.SearchDatabase") && trace.Contains("SearchInit.IndexationOnStartup"))
            {
                editorIndexingExceptions++;
                Debug.LogWarning("D4_EDITOR_STARTUP_EXCEPTION: Unity Search indexing failed before use; outside D4 runtime. See original exception in this log.");
                return;
            }
            if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) failed=true;
        }
        private static void Fail(Exception e) { Debug.LogException(e); Finish(1); }
        private static void Finish(int code)
        {
            EditorApplication.update-=Tick;
            UnityEngine.Application.logMessageReceived-=OnLog;
            foreach(GameObject root in roots) if(root!=null) Object.Destroy(root);
            foreach(Camera existing in muted) if(existing!=null) existing.enabled=true;
            if(captureView!=null) captureView.Close();
            if(System.Environment.GetCommandLineArgs().Contains("-d4-ci")) EditorApplication.Exit(code);
            else EditorApplication.ExitPlaymode();
        }
    }
}
