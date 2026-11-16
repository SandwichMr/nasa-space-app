using System;
using Trainer.Unity.Networking;
using Trainer.Unity.Networking.BaseOperations;
using UnityEngine;

namespace Trainer.Unity.MainMenu
{
    // The one real phase-4 protocol client, reusable by the phase-5 integrator through Session.
    // Does not load gameplay, create an authority or infer success from a WebSocket opening.
    public sealed class MainMenuNetworkAdapter : MonoBehaviour, IMainMenuLaunchPort
    {
        public event Action<int, MenuLaunchState, string> Changed;
        public BaseOperationsNetworkSessionAdapter Session => controller?.Session;
        private UnityTransportClient transport;
        private BaseOperationsConnectionController controller;
        private int attempt;
        private bool polling, releaseRequested;
        public void Connect(int id, MainMenuConfiguration config)
        {
            Release(); attempt=id;
            if (config == null || !config.HasEndpoint) throw new InvalidOperationException("Public endpoint missing.");
#if UNITY_WEBGL && !UNITY_EDITOR
            if (UnityEngine.Application.absoluteURL.StartsWith("https:", StringComparison.OrdinalIgnoreCase) && !config.secure)
                throw new InvalidOperationException("Una página HTTPS requiere WebSocket WSS con TLS real.");
#endif
            transport = new UnityTransportClient(new WebSocketClientOptions {
                Address=config.publicHost, Port=config.publicPort, Path=config.path, Secure=config.secure,
                ConnectTimeoutMs=1000, MaxConnectAttempts=10
            });
            controller = new BaseOperationsConnectionController(transport);
            controller.StateChanged += OnStateChanged;
            UnityEngine.Application.runInBackground=true;
            polling=true;
            try { controller.Connect(); }
            finally { polling=false; if(releaseRequested) Release(); }
        }
        private void OnStateChanged(SessionConnectionState state)
        {
            switch(state)
            {
                case SessionConnectionState.Connecting:
                    Changed?.Invoke(attempt,MenuLaunchState.Connecting,"Conectando con el servidor…");break;
                case SessionConnectionState.Synchronizing:
                    Changed?.Invoke(attempt,MenuLaunchState.Synchronizing,"Esperando admisión y estado de la base…");break;
                case SessionConnectionState.Connected:
                    Changed?.Invoke(attempt,MenuLaunchState.Joined,"Sesión confirmada por el servidor.\nLa partida aún no está disponible en esta versión.\nPuedes desconectarte y volver.");break;
                case SessionConnectionState.ConnectionLost:
                case SessionConnectionState.Disconnected:
                    Debug.Log("[MainMenu] Link ended: "+controller?.LastDisconnectReason);
                    Changed?.Invoke(attempt,MenuLaunchState.Failed,Trainer.Unity.Networking.Outpost.OutpostConnectionStatus.Describe(state, controller?.LastDisconnectReason) + "\nPuedes reintentar o volver.");break;
            }
        }
        public void Cancel(int id) { if(id != attempt) return;if(polling) releaseRequested=true;else Release(); }
        public void Leave() { if(polling) releaseRequested=true;else Release(); }
        private void Update()
        {
            if (transport==null) return;
            polling=true;
            try { transport.Update(); }
            catch(Exception e) { Debug.LogException(e); Changed?.Invoke(attempt,MenuLaunchState.Failed,"La conexión se interrumpió. Puedes reintentar.");releaseRequested=true; }
            finally { polling=false; if(releaseRequested) Release(); }
        }
        private void LateUpdate() => transport?.Flush();
        private void Release()
        {
            releaseRequested=false;
            var old=controller;controller=null;
            if(old!=null) {old.StateChanged-=OnStateChanged;old.Dispose();}
            transport?.Dispose();transport=null;
        }
        private void OnDestroy() => Release();
    }
}
