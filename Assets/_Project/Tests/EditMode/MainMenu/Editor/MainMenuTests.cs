using System;
using NUnit.Framework;
using Trainer.Unity.MainMenu;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.EventSystems;

namespace Trainer.Tests.MainMenu
{
    public sealed class MainMenuTests
    {
        private sealed class Port : IMainMenuLaunchPort
        {
            public event Action<int, MenuLaunchState, string> Changed;
            public int Calls, Cancels, Id, Leaves;
            public void Connect(int id, MainMenuConfiguration c) { Calls++; Id=id; }
            public void Cancel(int id) { Cancels++; Changed?.Invoke(id,MenuLaunchState.Joined,"late synchronous callback"); }
            public void Leave() { Leaves++; }
            public void Emit(int id,MenuLaunchState state) => Changed?.Invoke(id,state,"test");
        }
        private MainMenuConfiguration config;
        [SetUp] public void SetUp() { config=ScriptableObject.CreateInstance<MainMenuConfiguration>();config.publicHost="example.com";config.publicPort=12345;config.timeoutSeconds=3; }
        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(config);
        [Test] public void DoubleClickStartsOnceAndTransportAloneDoesNotConfirmAdmission()
        {
            var p=new Port();using(var f=new MenuLaunchFlow(p,config)) { f.Connect(0);f.Connect(1);Assert.AreEqual(1,p.Calls);p.Emit(p.Id,MenuLaunchState.Synchronizing);Assert.True(f.Busy);p.Emit(p.Id,MenuLaunchState.Joined);Assert.AreEqual(MenuLaunchState.Joined,f.State); }
        }
        [Test] public void CancelInvalidatesSynchronousAndLateCallbacksAndAllowsRetry()
        {
            var p=new Port();using(var f=new MenuLaunchFlow(p,config)) { f.Connect(0);int first=p.Id;f.Cancel();p.Emit(first,MenuLaunchState.Joined);Assert.AreEqual(MenuLaunchState.Ready,f.State);f.Connect(1);p.Emit(first,MenuLaunchState.Failed);Assert.AreEqual(MenuLaunchState.Connecting,f.State);Assert.AreEqual(2,p.Calls); }
        }
        [Test] public void TimeoutCancelsAndLateSuccessCannotOpenGameplay()
        {
            var p=new Port();using(var f=new MenuLaunchFlow(p,config)) { f.Connect(0);int first=p.Id;f.Tick(4);Assert.AreEqual(MenuLaunchState.Failed,f.State);Assert.AreEqual(1,p.Cancels);p.Emit(first,MenuLaunchState.Joined);Assert.AreEqual(MenuLaunchState.Failed,f.State);f.Connect(5);Assert.AreEqual(2,p.Calls); }
        }
        [Test] public void FailureReleasesAttemptAndDisposeUnsubscribes()
        {
            var p=new Port();var f=new MenuLaunchFlow(p,config);f.Connect(0);p.Emit(p.Id,MenuLaunchState.Failed);Assert.AreEqual(1,p.Cancels);Assert.AreEqual(MenuLaunchState.Failed,f.State);f.Connect(1);f.Dispose();p.Emit(p.Id,MenuLaunchState.Joined);Assert.AreEqual(MenuLaunchState.Ready,f.State);
        }
        [Test] public void DisconnectAfterAdmissionReturnsToFailureAndReleasesClient()
        {
            var p=new Port();using(var f=new MenuLaunchFlow(p,config)){f.Connect(0);p.Emit(p.Id,MenuLaunchState.Joined);p.Emit(p.Id,MenuLaunchState.Failed);Assert.AreEqual(MenuLaunchState.Failed,f.State);Assert.AreEqual(1,p.Cancels);}
        }
        [Test] public void LeaveDoesNotOwnOrStopAuthority() { var p=new Port();using(var f=new MenuLaunchFlow(p,config)){ f.Connect(0);f.Leave();Assert.AreEqual(1,p.Leaves);Assert.AreEqual(1,p.Cancels); } }
        [TestCase("")][TestCase("localhost")][TestCase("127.0.0.1")][TestCase("10.1.2.3")][TestCase("192.168.0.2")][TestCase("172.18.1.1")]
        public void MissingOrPrivateEndpointDoesNotStartTransport(string host)
        {
            config.publicHost=host;var p=new Port();using(var f=new MenuLaunchFlow(p,config)){ f.Connect(0);Assert.AreEqual(MenuLaunchState.Failed,f.State);Assert.AreEqual(0,p.Calls); }
        }
        [Test] public void MissingIntegratorReportsHonestFailure() { using(var f=new MenuLaunchFlow(null,config)){ f.Connect(0);StringAssert.Contains("integración",f.Detail.ToLowerInvariant()); } }
        [Test] public void PreferencesSurviveLoadAndMuteKeepsVolumeAndCorruptionUsesDefaults()
        {
            float oldVolume=PlayerPrefs.GetFloat("main-menu.volume",.7f);int oldMute=PlayerPrefs.GetInt("main-menu.muted",0);
            try {
                MenuPreferences.Set(.45f,true,true);MenuPreferences.Set(.1f,false,false);MenuPreferences.Load();Assert.AreEqual(.45f,MenuPreferences.Volume,.001);Assert.True(MenuPreferences.Muted);Assert.AreEqual(0,AudioListener.volume);
                MenuPreferences.Set(MenuPreferences.Volume,false,false);Assert.AreEqual(.45f,AudioListener.volume,.001);
                PlayerPrefs.SetFloat("main-menu.volume",float.NaN);MenuPreferences.Load();Assert.AreEqual(.7f,MenuPreferences.Volume,.001);
            } finally {PlayerPrefs.SetFloat("main-menu.volume",oldVolume);PlayerPrefs.SetInt("main-menu.muted",oldMute);PlayerPrefs.Save();MenuPreferences.Load();}
        }
        [Test] public void SceneHasWiredReferencesAndExactlyOneEventSystem()
        {
            var scene=EditorSceneManager.OpenScene("Assets/_Project/Scenes/MainMenu.unity");
            var view=UnityEngine.Object.FindFirstObjectByType<MainMenuView>();Assert.NotNull(view);Assert.NotNull(view.configuration);Assert.NotNull(view.play);Assert.NotNull(view.volume);Assert.NotNull(view.resolution.template);Assert.NotNull(view.home);Assert.IsInstanceOf<MainMenuNetworkAdapter>(view.launchAdapter);Assert.IsTrue(view.configuration.HasEndpoint);Assert.NotNull(view.joinOnline);
            Assert.AreEqual(1,UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length);
            Assert.IsFalse(view.help.transform.parent.gameObject.activeSelf);Assert.IsTrue(view.home.activeSelf);
        }
    }
}
