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
            if (Array.IndexOf(Environment.GetCommandLineArgs(),"-main-menu-smoke") >= 0)
                new GameObject("Main menu evidence").AddComponent<MainMenuSmoke>();
        }
        private void Awake() => UnityEngine.Application.logMessageReceived += OnLog;
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
            Debug.Log("[MainMenuSmoke] PASS: home/settings/credits/help/error/retry/back; Tab/Enter/Escape; mute applied; 16:9,16:10,4:3 captures. Gameplay integration pending; inspect real connection result.");
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
