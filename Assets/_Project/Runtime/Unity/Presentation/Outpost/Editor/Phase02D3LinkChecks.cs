using System;
using System.Linq;
using System.Reflection;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Application.Outpost;
using Trainer.Unity.World.Outpost;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using SessionState = Trainer.Application.SessionState;

namespace Trainer.Unity.Presentation.Outpost.Editor
{
    /// <summary>Opt-in integration smoke test using D3's real sync/input and the frozen fake.
    /// Reflection keeps this runner compilable on the common base. It never substitutes D3 movement.</summary>
    public static class Phase02D3LinkChecks
    {
        private const string Key = "Trainer.D4.D3Link";
        private static GameObject root;
        private static Component sync, input;
        private static Camera camera;
        private static OutpostPanel panel;
        private static FakeOutpostSessionPort fake;
        private static OutpostSessionSnapshot current;
        private static int checks, stage;
        private static double next;

        [MenuItem("Tools/Trainer/Phase 02/Check installed D3 visual link (fake port)")]
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run from Edit mode.");
            if(typeof(CrewVisual).Assembly.GetType("Trainer.Unity.Networking.Outpost.CrewAvatarSync")==null)
                throw new InvalidOperationException("D3 is absent; run only in an integration verification copy.");
            UnityEditor.SessionState.SetBool(Key,true); EditorApplication.EnterPlaymode();
        }
        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorApplication.playModeStateChanged-=OnMode; EditorApplication.playModeStateChanged+=OnMode;
        }
        private static void OnMode(PlayModeStateChange mode)
        {
            if(mode!=PlayModeStateChange.EnteredPlayMode || !UnityEditor.SessionState.GetBool(Key,false)) return;
            UnityEditor.SessionState.SetBool(Key,false); checks=stage=0;
            try
            {
                root=new GameObject("D4 temporary D3 integration fixture");
                Type syncType=typeof(CrewVisual).Assembly.GetType("Trainer.Unity.Networking.Outpost.CrewAvatarSync");
                Type inputType=typeof(CrewVisual).Assembly.GetType("Trainer.Unity.Controls.CrewKeyboardInput");
                sync=root.AddComponent(syncType); input=root.AddComponent(inputType);
                camera=new GameObject("D3 local follow test camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform);
                camera.transform.position=new Vector3(0,0,-10); camera.orthographic=true;
                Phase02VisualAssets.Set(sync,"avatarPrefab",AssetDatabase.LoadAssetAtPath<GameObject>(Phase02VisualAssets.CrewPath));
                Phase02VisualAssets.Set(sync,"followCamera",camera);
                syncType.GetField("cameraSmoothingSeconds",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(sync,0f);
                panel=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Phase02VisualAssets.PanelPath),root.transform).GetComponent<OutpostPanel>();
                panel.enabled=false; // No IMGUI needed in the headless integration check.
                fake=new FakeOutpostSessionPort(); var scenario=OutpostFixtures.Scenario();
                current=OutpostFixtures.Initial(scenario,0,new PlayerId(1),new PlayerId(2));
                fake.Publish(current); fake.Join(new PlayerId(1));
                Call(sync,"Bind",fake); Call(input,"Bind",fake,scenario);
                panel.Bind(fake,scenario); panel.enabled=true;
                object interaction=inputType.GetProperty("Interaction").GetValue(input);
                var start=(Func<StationId,TaskTypeId,bool>)Delegate.CreateDelegate(typeof(Func<StationId,TaskTypeId,bool>),interaction,"RequestStart");
                var cancel=(Func<StationId,bool>)Delegate.CreateDelegate(typeof(Func<StationId,bool>),interaction,"RequestCancel");
                panel.ConfigureInteraction(start,cancel,()=> (OutpostCommand)interaction.GetType().GetProperty("Pending").GetValue(interaction),
                    blocked=>Call(input,blocked?"Block":"Unblock",panel));
                next=EditorApplication.timeSinceStartup+.3; EditorApplication.update+=Tick;
            }
            catch(Exception e) { Fail(e); }
        }
        private static void Tick()
        {
            if(EditorApplication.timeSinceStartup<next) return;
            try
            {
                CrewVisual[] views=root.GetComponentsInChildren<CrewVisual>();
                switch(stage++)
                {
                    case 0:
                        Check((int)sync.GetType().GetProperty("AvatarCount").GetValue(sync)==2 && views.Length==2,"D3 alone spawns both D4 prefabs");
                        Check(views.Count(v=>v.IsLocal)==1 && views.Single(v=>v.IsLocal).Player==new PlayerId(1),"Installed ICrewAvatarView adapter receives the local identity");
                        Check(views.Single(v=>!v.IsLocal).Player==new PlayerId(2),"Remote identity reaches D4 appearance");
                        current=OutpostFixtures.Next(current,crew:OutpostFixtures.WithMember(current,new PlayerId(1),
                            new CrewPose(new BasePosition(3,7),new MoveDirection(1,0),CrewMotion.Walking),1),state:SessionState.Running);
                        fake.Publish(current);
                        break;
                    case 1:
                        var local=views.Single(v=>v.IsLocal);
                        Check(Mathf.Abs(local.transform.position.x-3)<.001 && Mathf.Abs(local.transform.position.y-7)<.001,"D3 positions the root from the confirmed pose");
                        Check(local.Walking && local.FacingIndex==1,"D3's real frame switches D4 animation");
                        Check(Mathf.Abs(camera.transform.position.x-3)<.001 && Mathf.Abs(camera.transform.position.y-7)<.001,"Only D3 follows the local astronaut");
                        Check(panel.OpenStation(OutpostFixtures.WaterProcessor) && (bool)input.GetType().GetProperty("IsBlocked").GetValue(input),"Opening D4 panel calls the real D3 Block");
                        Check(panel.RequestStart(OutpostFixtures.TreatWater) && panel.IsPending && fake.PendingCommands.Count==1,"D4 task button uses the existing D3 Interaction pending guard");
                        Check(!panel.RequestStart(OutpostFixtures.TreatWater) && fake.PendingCommands.Count==1,"UI and D3 share one request in flight");
                        fake.Reject(fake.PendingCommands[0].Id,OutpostReasons.StationOccupied);
                        Check(!panel.IsPending && panel.Feedback.Contains("ocupa"),"D3 result releases pending and displays authority reason");
                        panel.CloseStation();
                        Check(!(bool)input.GetType().GetProperty("IsBlocked").GetValue(input),"Closing D4 panel calls the real D3 Unblock");
                        fake.Disconnect(OutpostReasons.ConnectionLost);
                        Check((int)sync.GetType().GetProperty("AvatarCount").GetValue(sync)==0 && !panel.HasCurrentData,"D3 clears crew and D4 clears readings on the same lost membership");
                        break;
                    case 2:
                        Check(root.GetComponentsInChildren<CrewVisual>().Length==0,"No ghost visual after D3 deferred destruction");
                        Debug.Log("D4_D3_LINK_CHECKS_PASSED: "+checks+" · real D3 components, fake port, no authority or network");
                        Finish(0); return;
                }
                next=EditorApplication.timeSinceStartup+.3;
            }
            catch(Exception e) { Fail(e); }
        }
        private static void Call(object target,string name,params object[] arguments) => target.GetType().GetMethod(name).Invoke(target,arguments);
        private static void Check(bool value,string label)
        {
            if(!value) throw new InvalidOperationException("D4 D3 link failed: "+label);
            Debug.Log("D4_D3_CHECK "+(++checks)+": "+label);
        }
        private static void Fail(Exception error) { Debug.LogException(error); Finish(1); }
        private static void Finish(int code)
        {
            EditorApplication.update-=Tick;
            if(root!=null) Object.Destroy(root);
            if(UnityEngine.Application.isBatchMode || Environment.GetCommandLineArgs().Contains("-d4-ci")) EditorApplication.Exit(code);
            else EditorApplication.ExitPlaymode();
        }
    }
}
