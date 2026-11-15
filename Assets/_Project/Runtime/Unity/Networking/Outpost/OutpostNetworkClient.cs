using System;
using Trainer.Application.Outpost;
using UnityEngine;

namespace Trainer.Unity.Networking.Outpost
{
    /// <summary>
    /// Phase 2 client composition (desktop or WebGL): <see cref="UnityTransportClient"/> +
    /// <see cref="OutpostConnectionController"/>. Hand <see cref="Session"/> to presentation and input; never
    /// create an authority, roster or simulation in a client process. Same launch arguments as phase 1:
    /// -trainer-server HOST -trainer-port N -trainer-path /p -trainer-secure -trainer-server-name NAME
    /// -trainer-connect, or the WebGL page query ?server=&amp;port=&amp;path=&amp;secure=&amp;connect=1.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OutpostNetworkClient : MonoBehaviour
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

        public OutpostConnectionController Connection { get; private set; }
        /// <summary>The <see cref="IOutpostSessionPort"/> for presentation and input. Same instance across rejoins.</summary>
        public OutpostNetworkSessionAdapter Session => Connection?.Session;
        public SessionConnectionState State => Connection?.State ?? SessionConnectionState.Disconnected;
        public string Endpoint => transport?.Endpoint;

        public event Action<SessionConnectionState> StateChanged;
        /// <summary>(confirmed, detail): confirmed only in Connected (joined with state).</summary>
        public event Action<bool, string> ConnectionStatusChanged;

        public void Configure(WebSocketClientOptions value)
        {
            if (Connection != null) throw new InvalidOperationException("Configure before the first Connect.");
            options = (value ?? throw new ArgumentNullException(nameof(value))).Clone();
            launchArgumentsRead = false; // Launch arguments still override the configured values.
        }

        /// <summary>Creates the adapter without connecting, so presentation can bind to <see cref="Session"/> early.</summary>
        public OutpostNetworkSessionAdapter Prepare()
        {
            EnsureComposed();
            return Session;
        }

        private void Start()
        {
            // The scene composition may call Prepare before this Start (an object activated during another
            // object's Awake does not get its Start ordered by DefaultExecutionOrder). The launch arguments are
            // read once, by whichever comes first, so -trainer-server/-trainer-connect are never dropped.
            ReadLaunchArgumentsOnce();
            if (connectOnStart || launchConnect) Connect();
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

        /// <summary>Joins, or rejoins after a loss (a new PlayerId; old pending commands are gone).</summary>
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

        // Input and UI submit during Update; send it this frame.
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

        private void EnsureComposed()
        {
            if (Connection != null) return;
            ReadLaunchArgumentsOnce();
            if (runInBackground) UnityEngine.Application.runInBackground = true;
            transport = new UnityTransportClient(options);
            Connection = new OutpostConnectionController(transport);
            Connection.StateChanged += OnStateChanged;
        }

        private void OnStateChanged(SessionConnectionState state)
        {
            if (state == SessionConnectionState.Synchronizing) synchronizingSince = Time.unscaledTime;
            Debug.Log($"[OutpostNetworkClient] {state}" + (Connection.LastDisconnectReason != null
                ? " (" + Connection.LastDisconnectReason + ")" : "")
                + (state == SessionConnectionState.Connected ? " as " + Connection.Session.Membership.Player : ""));
            StateChanged?.Invoke(state);
            ConnectionStatusChanged?.Invoke(OutpostConnectionStatus.IsConfirmed(state),
                OutpostConnectionStatus.Describe(state, Connection.LastDisconnectReason));
        }
    }
}
