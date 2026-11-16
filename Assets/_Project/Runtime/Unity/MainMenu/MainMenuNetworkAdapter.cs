using System;
using System.Collections;
using Trainer.Unity.Networking;
using Trainer.Unity.Networking.Incidents;
using Trainer.Unity.Bootstrap.Incidents;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Trainer.Unity.MainMenu
{
    // Loads the real gameplay first, sets Client before Awake, then uses that scene's ONE client and identity.
    public sealed class MainMenuNetworkAdapter : MonoBehaviour, IMainMenuLaunchPort
    {
        public const string GameplayScene = "Phase05_LivingBase";
        public event Action<int, MenuLaunchState, string> Changed;
        public LivingBaseNetworkSessionAdapter Session => client?.Session;
        private LivingBaseNetworkClient client;
        private Coroutine routine;
        private AsyncOperation unloading;
        private int attempt, generation;
        private bool joined;
        private MainMenuConfiguration config;
        private void Awake() => LivingBaseSceneComposition.Preparing += PrepareScene;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void DedicatedEntry()
        {
            if (UnityEngine.Application.isBatchMode && SceneManager.GetActiveScene().name == "MainMenu")
                SceneManager.LoadScene(GameplayScene);
        }
        public void Connect(int id, MainMenuConfiguration value)
        {
            if (routine != null || client != null) throw new InvalidOperationException("Disconnect the previous attempt first.");
            config=value;attempt=id;
            routine=StartCoroutine(LoadAndConnect(++generation));
        }
        private void PrepareScene(LivingBaseSceneComposition composition)
        {
            if(config==null || composition.gameObject.scene.name!=GameplayScene) return;
            var options=new WebSocketClientOptions { Address=config.publicHost,Port=config.publicPort,Path=config.path,Secure=config.secure };
            // Explicit development flags support the real local server smoke; packaged defaults remain public.
            SessionNetworkArguments.Apply(options,Environment.GetCommandLineArgs());
#if UNITY_WEBGL && !UNITY_EDITOR
            if(UnityEngine.Application.absoluteURL.StartsWith("https:",StringComparison.OrdinalIgnoreCase) && !options.Secure)
                throw new InvalidOperationException("HTTPS requiere WSS con TLS real.");
#endif
            composition.ConfigureMenuClient(options);
            client=composition.NetworkClient;
            client.StateChanged+=OnState;
        }
        private IEnumerator LoadAndConnect(int token)
        {
            Changed?.Invoke(attempt,MenuLaunchState.Loading,"Preparando la base…");
            if(unloading!=null) {yield return unloading;unloading=null;}
            if(token!=generation) yield break;
            var operation=SceneManager.LoadSceneAsync(GameplayScene,LoadSceneMode.Additive);
            yield return operation;
            if(token!=generation) {if(client!=null){client.StateChanged-=OnState;client.Disconnect();client=null;}UnloadGameplay();routine=null;yield break;}
            yield return null; // Composition.Start binds presentation before this one Connect.
            routine=null;
            if(client==null) {Changed?.Invoke(attempt,MenuLaunchState.Failed,"No se pudo preparar la base.");yield break;}
            Debug.Log("[MainMenu] Connecting gameplay client to "+client.Endpoint);
            client.Connect();
        }
        private void OnState(SessionConnectionState state)
        {
            switch(state)
            {
                case SessionConnectionState.Connecting: Changed?.Invoke(attempt,MenuLaunchState.Connecting,"Conectando con el servidor…");break;
                case SessionConnectionState.Synchronizing: Changed?.Invoke(attempt,MenuLaunchState.Synchronizing,"Esperando admisión y estado de la base…");break;
                case SessionConnectionState.Connected:
                    joined=true;Changed?.Invoke(attempt,MenuLaunchState.Joined,"Partida confirmada.");break;
                case SessionConnectionState.Disconnected:
                case SessionConnectionState.ConnectionLost:
                    joined=false;
                    Changed?.Invoke(attempt,MenuLaunchState.Failed,Trainer.Unity.Networking.Outpost.OutpostConnectionStatus.Describe(state,client?.Connection?.LastDisconnectReason));break;
            }
        }
        public void Cancel(int id) {if(id==attempt) Leave();}
        public void Leave()
        {
            generation++;joined=false;
            if(client!=null) {client.StateChanged-=OnState;client.Disconnect();client=null;}
            // A load in flight must finish before it can be unloaded; its coroutine checks generation.
            if(routine==null) UnloadGameplay();
        }
        private void UnloadGameplay()
        {
            var scene=SceneManager.GetSceneByName(GameplayScene);
            if(scene.IsValid() && scene.isLoaded) unloading=SceneManager.UnloadSceneAsync(scene);
        }
        private void OnGUI()
        {
            if(joined && GUI.Button(new Rect(Screen.width-210,Screen.height*.34f,195,36),"Volver al menú"))
            {
                var view=GetComponent<MainMenuView>();
                view.ReturnFromGameplay();
            }
        }
        private void OnDestroy()
        {
            LivingBaseSceneComposition.Preparing-=PrepareScene;
            if(client!=null) {client.StateChanged-=OnState;client.Disconnect();}
        }
    }
}
