using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.EventSystems;

namespace Trainer.Unity.MainMenu
{
    // Explicit opt-in validation only. No fake transport, endpoint, authority or simulated online success.
    public sealed class MainMenuSmoke : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfRequested()
        {
            if ((Array.IndexOf(Environment.GetCommandLineArgs(),"-main-menu-smoke") >= 0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-main-menu-play-smoke") >= 0))
                new GameObject("Main menu evidence").AddComponent<MainMenuSmoke>();
        }
        private void Awake() { UnityEngine.Application.runInBackground=true; UnityEngine.Application.logMessageReceived += OnLog; }
        private void OnDestroy() => UnityEngine.Application.logMessageReceived -= OnLog;
        private void OnLog(string message,string stack,LogType type)
        {
            if(type==LogType.Exception || type==LogType.Error) UnityEngine.Application.Quit(1);
        }
        private IEnumerator Start()
        {
            string path=Path.GetFullPath("Logs/main-menu");Directory.CreateDirectory(path);
            yield return new WaitForSecondsRealtime(2);
            var view=UnityEngine.Object.FindFirstObjectByType<MainMenuView>();
            if(view==null) {Debug.LogError("[MainMenuSmoke] Missing scene view");UnityEngine.Application.Quit(1);yield break;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-main-menu-play-smoke")>=0) {yield return PlaySmoke(view,path);yield break;}
            yield return Capture(path+"/01-home.png");
            yield return Press(Key.Tab);
            if (EventSystem.current.currentSelectedGameObject != view.helpButton.gameObject) throw new Exception("Tab did not move focus");
            yield return Press(Key.Enter);
            if (!view.help.activeInHierarchy) throw new Exception("Enter did not open help");
            yield return Press(Key.Escape);
            if (!view.home.activeInHierarchy) throw new Exception("Escape did not return home");
            view.settingsButton.onClick.Invoke();yield return Capture(path+"/02-settings.png");
            view.volume.value=.34f;view.mute.isOn=true;
            if(AudioListener.volume!=0) throw new Exception("Mute did not apply");
            view.cancel.onClick.Invoke();view.creditsButton.onClick.Invoke();yield return Capture(path+"/03-credits.png");
            view.closeButtons[1].onClick.Invoke();view.helpButton.onClick.Invoke();yield return Capture(path+"/04-help.png");
            view.closeButtons[0].onClick.Invoke();
            view.play.onClick.Invoke();yield return Capture(path+"/05-multiplayer.png");
            view.joinOnline.onClick.Invoke();yield return Capture(path+"/06-connecting.png");
            yield return new WaitForSecondsRealtime(16);
            yield return Capture(path+"/07-connection-result.png");
            if(view.retry.gameObject.activeInHierarchy) { view.retry.onClick.Invoke(); yield return new WaitForSecondsRealtime(.3f); }
            view.back.onClick.Invoke();view.multiplayerBack.onClick.Invoke();
            Screen.SetResolution(1280,800,false);yield return new WaitForSecondsRealtime(1);yield return Capture(path+"/08-home-16x10.png");
            Screen.SetResolution(1024,768,false);yield return new WaitForSecondsRealtime(1);yield return Capture(path+"/09-home-4x3.png");
            Debug.Log("[MainMenuSmoke] PASS: home/settings/credits/help/error/retry/back; Tab/Enter/Escape; mute applied; 16:9,16:10,4:3 captures. Inspect the real connection result.");
            UnityEngine.Application.Quit();
        }
        private IEnumerator PlaySmoke(MainMenuView view,string path)
        {
            view.play.onClick.Invoke();view.joinOnline.onClick.Invoke();
            var adapter=view.launchAdapter as MainMenuNetworkAdapter;
            double until=Time.realtimeSinceStartupAsDouble+20;
            while((adapter.Session==null || !adapter.Session.Membership.IsJoined) && Time.realtimeSinceStartupAsDouble<until) yield return null;
            if(adapter.Session==null || !adapter.Session.Membership.IsJoined) throw new Exception("Real server did not admit the menu client");
            var composition=UnityEngine.Object.FindFirstObjectByType<Trainer.Unity.Bootstrap.Incidents.LivingBaseSceneComposition>();
            if(composition==null || composition.Role!=Trainer.Unity.Bootstrap.MissionProcessRole.Client || composition.Authority!=null) throw new Exception("Client must have no local authority");
            var identity=adapter.Session.Membership.Player;
            adapter.Session.TryGetSnapshot(out var before);
            if(before==null || !before.TryGetCrew(identity,out var crew)) throw new Exception("Initial crew snapshot missing");
            var position=crew.Pose.Position;
            if(before.State==Trainer.Application.SessionState.Ready)
                adapter.Session.Submit(Trainer.Application.Expedition.ExpeditionCommand.Session(Guid.NewGuid(),before.Epoch,Trainer.Application.Expedition.ExpeditionAction.Resume));
            yield return new WaitForSecondsRealtime(1);
            bool observer=Array.IndexOf(Environment.GetCommandLineArgs(),"-main-menu-observer")>=0;
            if(observer)
            {
                yield return new WaitForSecondsRealtime(25);
                if(adapter.Session==null || !adapter.Session.TryGetSnapshot(out var later) || later.SimulatedTimeSeconds<=before.SimulatedTimeSeconds) throw new Exception("Other client leaving stopped the shared clock");
                Debug.Log("[MainMenuPlaySmoke] OBSERVER PASS continued clock="+later.SimulatedTimeSeconds+" crew="+later.Crew.Count);
            }
            else
            {
                yield return new WaitForSecondsRealtime(2);
                foreach(var key in new[]{Key.D,Key.W,Key.A,Key.S})
                {
                    InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(key));
                    yield return new WaitForSecondsRealtime(.6f);
                    InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());
                    yield return new WaitForSecondsRealtime(.2f);
                    if(adapter.Session.TryGetSnapshot(out var after) && after.TryGetCrew(identity,out var moved) && moved.Pose.Position.DistanceTo(position)>.03) break;
                }
                if(!adapter.Session.TryGetSnapshot(out var result) || !result.TryGetCrew(identity,out var finalCrew) || finalCrew.Pose.Position.DistanceTo(position)<=.03) throw new Exception("Keyboard movement was not confirmed by the authority");
                Debug.Log("[MainMenuPlaySmoke] MOVEMENT PASS crew="+result.Crew.Count+" time="+result.SimulatedTimeSeconds);
                yield return Capture(path+"/10-integrated-gameplay.png");
                view.ReturnFromGameplay();yield return new WaitForSecondsRealtime(1);
                if(!view.GetComponent<Canvas>().enabled) throw new Exception("Return did not restore menu");
                view.joinOnline.onClick.Invoke();until=Time.realtimeSinceStartupAsDouble+20;
                while((adapter.Session==null || !adapter.Session.Membership.IsJoined) && Time.realtimeSinceStartupAsDouble<until)yield return null;
                if(adapter.Session==null || !adapter.Session.Membership.IsJoined || adapter.Session.Membership.Player.Equals(identity))throw new Exception("Rejoin did not create a fresh confirmed membership");
                Debug.Log("[MainMenuPlaySmoke] REJOIN PASS");
            }
            view.ReturnFromGameplay();yield return new WaitForSecondsRealtime(1);
            Debug.Log("[MainMenuPlaySmoke] PASS real server, admitted snapshot, gameplay, return");
            UnityEngine.Application.Quit();
        }
        private IEnumerator Capture(string path) { yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(path);yield return new WaitForSecondsRealtime(.4f);if(!File.Exists(path)) throw new Exception("Screenshot was not written: " + path); }
        private IEnumerator Press(Key key)
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(key));
            yield return new WaitForSecondsRealtime(.15f);
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return new WaitForSecondsRealtime(.15f);
        }
    }
}
