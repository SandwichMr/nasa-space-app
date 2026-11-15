using System;
using System.IO;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.Eva;
using Trainer.Unity.World.Expedition;
using Trainer.Unity.World.Outpost;
using UnityEditor;
using UnityEngine;
namespace Trainer.Unity.Presentation.Expedition.Editor
{
    // Explicit fake fixture, never installed in a production scene; no scenes saved.
    public static class Phase03VisualChecks
    {
        private const string Key="D4.Phase03.Check";
        private static int checks,stage,frame;
        private static bool failed;
        private static ExpeditionPanel panel;
        private static ExpeditionWorldView world;
        private static FakeExpeditionSessionPort fake;
        private static ExpeditionScenario scenario;
        private static ExpeditionSessionSnapshot snapshot;
        private static ExpeditionViewContext context;
        private static GameObject root;
        private static readonly PlayerId Me=new PlayerId(1);
        private static string Evidence=>Path.GetFullPath("docs/phase-03/handoffs/d4-evidence");
        [MenuItem("Tools/Trainer/Phase 03/Run D4 fake checks and captures")]
        public static void Run()
        {
            UnityEditor.SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        [InitializeOnLoadMethod] private static void Register(){EditorApplication.playModeStateChanged+=Mode;}
        private static void Mode(PlayModeStateChange mode)
        {
            if(mode!=PlayModeStateChange.EnteredPlayMode || !UnityEditor.SessionState.GetBool(Key,false))return;
            UnityEditor.SessionState.SetBool(Key,false);checks=stage=0;failed=false;
            try { Setup();frame=Time.frameCount+40;EditorApplication.update+=Tick; }
            catch(Exception e){Fail(e);}
        }
        private static void Setup()
        {
            Directory.CreateDirectory(Evidence);
            foreach(var c in Camera.allCameras)c.enabled=false;
            root=new GameObject("D4 FAKE checks — not production");
            var camera=new GameObject("Temporary fixture camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.transform.position=new Vector3(17,5,-10);camera.orthographic=true;camera.orthographicSize=7;camera.backgroundColor=new Color(.025f,.045f,.08f);
            if(!UnityEngine.Application.isBatchMode)
            {
                var type=Type.GetType("UnityEditor.GameView,UnityEditor");var window=(EditorWindow)ScriptableObject.CreateInstance(type);window.ShowUtility();window.position=new Rect(0,0,1440,920);window.Focus();
            }
            scenario=ExpeditionFixtures.Scenario();fake=new FakeExpeditionSessionPort();
            var interior=Spawn("World/Phase02/BaseInterior").GetComponent<BaseInteriorView>();interior.Configure(scenario.Outpost);
            world=Spawn("World/Phase03/ExpeditionWorld").GetComponent<ExpeditionWorldView>();Phase03VisualAssets.Set(world,"baseView",interior);
            panel=Spawn("UI/Phase03/ExpeditionPanel").GetComponent<ExpeditionPanel>();
            context=new ExpeditionViewContext(fake,scenario,new Intents(),(o,b)=>{});
            snapshot=ExpeditionFixtures.Initial(scenario,1,Me,new PlayerId(2));
            var margin=new ReturnMarginEstimate(MarginStatus.Sufficient,0,45,SuitService.Oxygen,new[]{
                new ServiceMargin(SuitService.Oxygen,.2,.1,.002,45),new ServiceMargin(SuitService.Battery,2000,500,15,100),new ServiceMargin(SuitService.Feedwater,.2,.05,.001,150)},new ReturnPlan(8,10,0,5,true));
            var b=ExpeditionFixtures.Edit(scenario,snapshot).Phase(ExpeditionPhase.Underway);
            for(int i=0;i<2;i++) b.Suit(i,SuitStatus.Sealed,new PlayerId(i+1),ChecklistSteps.All,new SuitLevels(.35,2500,.3))
                .Member(new PlayerId(i+1),new BasePosition(17+i*2,5),CrewZone.Exterior,i,true,SuitActivity.Rest,margin);
            snapshot=b.Build();fake.Publish(snapshot);
            world.Bind(context);panel.Bind(context);Check(!panel.Live,"not live before membership");fake.Join(Me);Check(panel.Live,"membership exposes current state");
            for(int i=0;i<2;i++)
            {
                var g=Spawn("Crew/Phase03/ExpeditionAstronaut");g.transform.position=new Vector3(17+i*2,5,0);
                g.GetComponent<CrewVisual>().Present(new PlayerId(i+1),i,i==0,new MoveDirection(0,-1),CrewMotion.Idle,StationId.None);
                Check(g.GetComponent<Animator>().runtimeAnimatorController!=null,"original animation controller retained");
                Check(g.GetComponent<ExpeditionCrewBadge>()!=null,"crew phase3 view present");
                if(ExpeditionCrewFrame.TryBuild(snapshot,new PlayerId(i+1),Me,out var f))g.GetComponent<ExpeditionCrewBadge>().PresentExpedition(f);
            }
            var mark=Phase03VisualAssets.Label(root.transform,"Fake label",new Vector3(17,1,0),.19f);mark.text="D4 · FAKE COMÚN · SIN RED NI SIMULACIÓN REAL";
            foreach(var r in world.GetComponentsInChildren<SpriteRenderer>())Check(r.sprite!=null && r.sharedMaterial!=null,"world sprite and material references");
            Check(root.GetComponentsInChildren<Collider2D>().Length==0 && root.GetComponentsInChildren<Rigidbody2D>().Length==0,"no competing physics");
            Check(ExpeditionPanel.Margin(null).Contains("desconocido"),"unknown estimate never zero");
        }
        private static void Tick()
        {
            if(Time.frameCount<frame)return;
            frame=Time.frameCount+20;
            try
            {
                switch(stage++)
                {
                    case 0: Capture("01-exterior-fake.png");break;
                    case 1:
                        var message=scenario.Episode.Messages[0];
                        snapshot=ExpeditionFixtures.Edit(scenario,snapshot).AtTime(message.IssuedAtSeconds+1).Issue(message).Build();fake.Publish(snapshot);
                        Check(panel.AdvisoryText.Contains("Sin información recibida"),"unreceived global message not exposed");
                        snapshot=ExpeditionFixtures.Edit(scenario,snapshot).Receive(message.Id,Me,snapshot.SimulatedTimeSeconds).Build();fake.Publish(snapshot);
                        Check(panel.AdvisoryText.Contains("Recibido"),"received timestamp shown");
                        Capture("02-received-warning-fake.png");break;
                    case 2:
                        message=scenario.Episode.Messages[0];
                        snapshot=ExpeditionFixtures.Edit(scenario,snapshot).AtTime(message.ValidUntilSeconds+1).Issue(message,AdvisoryValidity.Expired).Build();fake.Publish(snapshot);
                        Check(panel.AdvisoryText.Contains("VENCIDO") && panel.AdvisoryText.Contains("mantén"),"expired warning retains instruction");
                        Capture("03-expired-warning-fake.png");break;
                    case 3:
                        snapshot=ExpeditionFixtures.Edit(scenario,snapshot).Airlock(AirlockState.Depressurizing,1,2,5,true,Me).Build();fake.Publish(snapshot);
                        Check(world.Shown.Airlock.Stalled,"stalled cycle confirmed");Capture("04-airlock-transition-fake.png");break;
                    case 4:
                        snapshot=ExpeditionFixtures.Edit(scenario,snapshot).Station(new StationState(ExpeditionFixtures.RegolithSite,StationAvailability.Occupied,new TaskProgress(ExpeditionFixtures.CollectRegolith,Me,3,10,true),null)).Build();fake.Publish(snapshot);
                        Check(world.Shown.Outpost.TryGetStation(ExpeditionFixtures.RegolithSite,out var task) && task.ActiveTask.Stalled,"confirmed science occupation and stalled work");Capture("05-science-occupied-fake.png");break;
                    case 5:
                        snapshot=ExpeditionFixtures.Edit(scenario,snapshot).Station(new StationState(ExpeditionFixtures.RegolithSite,StationAvailability.Available,null,new TaskEnd(ExpeditionFixtures.CollectRegolith,Me,TaskEndReason.CancelledByPerformer,snapshot.SimulatedTimeSeconds))).Build();fake.Publish(snapshot);
                        Check(world.Shown.Outpost.TryGetStation(ExpeditionFixtures.RegolithSite,out var cancelled) && cancelled.ActiveTask==null,"confirmed cancellation clears task");Capture("06-science-cancelled-fake.png");break;
                    case 6:
                        var edit=ExpeditionFixtures.Edit(scenario,snapshot).Airlock(AirlockState.PressurizedInnerOpen,2).Shelter(new ShelterStatus(true,true,true,2));
                        for(int i=0;i<2;i++)edit.Suit(i,SuitStatus.Worn,new PlayerId(i+1),ChecklistSteps.All).Member(new PlayerId(i+1),new BasePosition(2+i*2,1.5),CrewZone.Shelter,i,false,protection:ProtectionState.Protected);
                        snapshot=edit.Build();fake.Publish(snapshot);
                        var cam=root.GetComponentInChildren<Camera>();cam.transform.position=new Vector3(5,3,-10);
                        foreach(var crew in root.GetComponentsInChildren<CrewVisual>())
                        {
                            snapshot.Outpost.TryGetCrew(crew.Player,out var pose);crew.transform.position=new Vector3((float)pose.Pose.Position.X,(float)pose.Pose.Position.Y,0);
                            if(ExpeditionCrewFrame.TryBuild(snapshot,crew.Player,Me,out var f))crew.GetComponent<ExpeditionCrewBadge>().PresentExpedition(f);
                        }
                        Check(panel.Shown.Crew[0].Protection==ProtectionState.Protected,"protection comes from confirmed session state");Capture("07-shelter-confirmed-fake.png");break;
                    case 7:
                        fake.Disconnect("fixture-disconnected");Check(!panel.Live && panel.Shown==null && world.Shown==null,"disconnect clears live state");
                        Check(world.transform.childCount==0 || !world.transform.GetChild(0).gameObject.activeSelf,"disconnected geometry hidden");Capture("08-disconnected-fake.png");break;
                    case 8:
                        fake.Join(Me);Check(panel.Live && world.Shown!=null,"rejoin rebuilds world and state");
                        panel.enabled=false;world.enabled=false;Check(panel.Shown==null && world.Shown==null,"disable removes binding");
                        panel.enabled=true;world.enabled=true;panel.Bind(context);world.Bind(context);Check(panel.Live,"explicit rebind after enable");
                        var old=fake;fake=new FakeExpeditionSessionPort();fake.Publish(snapshot);fake.Join(Me);
                        context=new ExpeditionViewContext(fake,scenario,new Intents(),(o,b)=>{});panel.Bind(context);world.Bind(context);
                        old.Disconnect("old-port");Check(panel.Live,"old port cannot clear new binding");
                        panel.Unbind();world.Unbind();Check(!panel.Live,"explicit unbind");Finish();break;
                }
            }
            catch(Exception e){Fail(e);}
        }
        private static GameObject Spawn(string name)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/"+name+".prefab");
            Check(prefab!=null,"prefab exists: "+name);var g=UnityEngine.Object.Instantiate(prefab,root.transform);return g;
        }
        private static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Debug.Log("D4_CHECK "+message);}
        private static void Capture(string name){if(!UnityEngine.Application.isBatchMode)ScreenCapture.CaptureScreenshot(Path.Combine(Evidence,name));}
        private static void Fail(Exception e){failed=true;Debug.LogException(e);Finish();}
        private static void Finish()
        {
            EditorApplication.update-=Tick;
            Debug.Log($"D4_FAKE_CHECKS: {checks} checks; failed={failed}");
            if(panel!=null)panel.Unbind();if(world!=null)world.Unbind();
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-d4-ci")>=0)EditorApplication.Exit(failed?1:0);else EditorApplication.ExitPlaymode();
        }
        private sealed class Intents:IExpeditionIntents
        {
            public bool HasPending=>false;public string LastReason=>null;
            public event Action<string> Answered { add{} remove{} }
            public bool Interact()=>false;public bool RequestStart(StationId s,TaskTypeId t)=>false;
            public bool RequestCancel(StationId s)=>false;public bool RequestCycle(StationId s)=>false;public bool RequestDeclareReturn()=>false;
        }
    }
}
