using System;
using UnityEngine;

namespace Trainer.Unity.Networking.BaseOperations
{
    /// <summary>
    /// Phase 4 client composition (desktop or WebGL): the SAME <see cref="UnityTransportClient"/> as phases 1–3 +
    /// <see cref="BaseOperationsConnectionController"/>. Hand <see cref="Session"/> to presentation, input and the interaction
    /// client; never create an authority, roster, model, clock or runner in a client process. Same launch arguments as before:
    /// -trainer-server HOST -trainer-port N -trainer-path /p -trainer-secure -trainer-server-name NAME -trainer-connect, or the
    /// WebGL query ?server=&amp;port=&amp;path=&amp;secure=&amp;connect=1. The arguments are read once, by whichever comes
    /// first of <see cref="Prepare"/> and Start, so a composition that prepares early never drops them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BaseOperationsNetworkClient : MonoBehaviour
    {
        [SerializeField] private WebSocketClientOptions options = new WebSocketClientOptions();
        [SerializeField] private bool connectOnStart;
        [SerializeField] private bool readLaunchArguments = true;
        [SerializeField, Min(0), Tooltip("Seconds waiting for the join answer before giving up. 0 = wait forever.")]
        private float synchronizationTimeoutSeconds = 10f;
        [SerializeField] private bool runInBackground = true;

        private UnityTransportClient transport;
        private float synchronizingSince;
        private bool launchArgumentsRead;
        private bool launchConnect;

        public BaseOperationsConnectionController Connection { get; private set; }
        public BaseOperationsNetworkSessionAdapter Session => Connection?.Session;
        public SessionConnectionState State => Connection?.State ?? SessionConnectionState.Disconnected;
        public string Endpoint => transport?.Endpoint;

        public event Action<SessionConnectionState> StateChanged;
        /// <summary>(confirmed, detail): confirmed only in Connected (joined with state).</summary>
        public event Action<bool, string> ConnectionStatusChanged;

        public void Configure(WebSocketClientOptions value)
        {
            if (Connection != null) throw new InvalidOperationException("Configure before the first Connect.");
            options = (value ?? throw new ArgumentNullException(nameof(value))).Clone();
            launchArgumentsRead = false;
        }

        public BaseOperationsNetworkSessionAdapter Prepare()
        {
            EnsureComposed();
            return Session;
        }

        private void Start()
        {
            ReadLaunchArgumentsOnce();
            if (connectOnStart || launchConnect) Connect();
        }

        /// <summary>Starts a FRESH join (also after a lost link): a new PlayerId; nothing of a former identity is resumed.</summary>
        public void Connect()
        {
            EnsureComposed();
            Connection.Connect();
        }

        public void Disconnect() => Connection?.Disconnect();

        private void Update()
        {
            if (transport == null) return;
            transport.Update();
            if (Connection.State == SessionConnectionState.Synchronizing && synchronizationTimeoutSeconds > 0
                && Time.unscaledTime - synchronizingSince > synchronizationTimeoutSeconds)
                Connection.AbortLink(SessionNetworkCodes.SyncTimeout);
        }

        private void LateUpdate() => transport?.Flush();

        private void OnDestroy()
        {
            try
            {
                Connection?.Dispose();
            }
            finally
            {
                transport?.Dispose();
                transport = null;
            }
        }

        private void ReadLaunchArgumentsOnce()
        {
            if (launchArgumentsRead || !readLaunchArguments || Connection != null) return;
            launchArgumentsRead = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            launchConnect = SessionNetworkArguments.ApplyPageUrl(options, UnityEngine.Application.absoluteURL);
#else
            launchConnect = SessionNetworkArguments.Apply(options, Environment.GetCommandLineArgs());
#endif
        }

        private void EnsureComposed()
        {
            if (Connection != null) return;
            ReadLaunchArgumentsOnce();
            if (runInBackground) UnityEngine.Application.runInBackground = true;
            transport = new UnityTransportClient(options);
            Connection = new BaseOperationsConnectionController(transport);
            Connection.StateChanged += OnStateChanged;
        }

        private void OnStateChanged(SessionConnectionState state)
        {
            if (state == SessionConnectionState.Synchronizing) synchronizingSince = Time.unscaledTime;
            Debug.Log($"[BaseOperationsNetworkClient] {state}" + (Connection.LastDisconnectReason != null ? " (" + Connection.LastDisconnectReason + ")" : "")
                + (state == SessionConnectionState.Connected ? " as " + Connection.Session.Membership.Player : ""));
            StateChanged?.Invoke(state);
            ConnectionStatusChanged?.Invoke(Outpost.OutpostConnectionStatus.IsConfirmed(state),
                Outpost.OutpostConnectionStatus.Describe(state, Connection.LastDisconnectReason));
        }
    }
}
