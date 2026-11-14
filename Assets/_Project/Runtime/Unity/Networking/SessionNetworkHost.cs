using System;
using Trainer.Application;
using Trainer.Unity.Bootstrap;
using UnityEngine;

namespace Trainer.Unity.Networking
{
    /// <summary>
    /// Server/host composition: listens with <see cref="UnityTransportHost"/> and puts a
    /// <see cref="NetworkCommandBridge"/> in front of the single authoritative <see cref="ISessionPort"/>.
    /// Place it ONLY in the authority process (dedicated server or host). With a <see cref="MissionBootstrap"/>
    /// assigned it follows Composed/ShutDown automatically; otherwise call <see cref="Attach"/>/<see cref="Detach"/>.
    /// Replacing the authority closes every client connection: a new session restarts epochs at 0 and clients
    /// must rejoin to accept its state. Without an authority, new connections are closed at once.
    /// Command line (non-WebGL players): -trainer-bind ADDRESS, -trainer-port N, -trainer-path /p,
    /// -trainer-cert FILE, -trainer-key FILE.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionNetworkHost : MonoBehaviour
    {
        [SerializeField] private MissionBootstrap bootstrap;
        [SerializeField] private WebSocketHostOptions options = new WebSocketHostOptions();
        [SerializeField] private bool listenOnStart = true;
        [SerializeField] private bool readCommandLine = true;
        [SerializeField, Tooltip("Close a client whose message breaks the wire protocol (bad version, kind or format).")]
        private bool disconnectOnProtocolError = true;
        [SerializeField, Min(0), Tooltip("Frame cap applied only in -batchmode (dedicated server). The authority " +
            "ticks and publishes once per frame, so an uncapped headless loop would flood clients. 0 = untouched.")]
        private int batchModeFrameRate = 30;
        [SerializeField, Tooltip("Keep ticking and pumping the network while the window is unfocused.")]
        private bool runInBackground = true;

        private ISessionPort authority;

        public UnityTransportHost Transport { get; private set; }
        public NetworkCommandBridge Bridge { get; private set; }
        public bool IsListening => Transport != null && Transport.IsListening;
        public int ConnectedClientCount => Bridge?.ConnectedClientCount ?? 0;

        /// <summary>Host diagnostics: (connection id, code). Protocol errors, send failures, closes.</summary>
        public event Action<ulong, string> Diagnostic;

        public void Configure(WebSocketHostOptions value)
        {
            if (IsListening) throw new InvalidOperationException("Configure before StartHost.");
            options = (value ?? throw new ArgumentNullException(nameof(value))).Clone();
        }

        private void Start()
        {
            if (listenOnStart) StartHost();
        }

        /// <summary>Starts listening and attaches the bootstrap's current session, if any.</summary>
        public void StartHost()
        {
            if (IsListening) return;
            if (runInBackground) UnityEngine.Application.runInBackground = true;
            if (UnityEngine.Application.isBatchMode && batchModeFrameRate > 0)
                UnityEngine.Application.targetFrameRate = batchModeFrameRate;
            WebSocketHostOptions effective = options.Clone();
            if (readCommandLine) SessionNetworkArguments.Apply(effective, Environment.GetCommandLineArgs());
            var transport = new UnityTransportHost { AcceptClients = false };
            try
            {
                transport.Listen(effective);
            }
            catch
            {
                transport.Dispose();
                throw;
            }
            Transport = transport;
            Transport.ConnectionClosed += OnConnectionClosed;
            Debug.Log($"[SessionNetworkHost] Listening on ws://{effective.BindAddress}:{Transport.Port}{effective.Path}");
            if (bootstrap != null)
            {
                bootstrap.Composed += OnComposed;
                bootstrap.ShutDown += OnShutDown;
                if (bootstrap.Session != null) Attach(bootstrap.Session);
            }
        }

        /// <summary>Puts <paramref name="session"/> behind the network. Existing clients are disconnected.</summary>
        public void Attach(ISessionPort session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (!IsListening) throw new InvalidOperationException("StartHost first.");
            if (ReferenceEquals(session, authority)) return;
            ReleaseBridge(SessionNetworkCodes.AuthorityReplaced);
            authority = session;
            Bridge = new NetworkCommandBridge(session, Transport);
            Bridge.ProtocolError += OnProtocolError;
            Transport.AcceptClients = true;
        }

        /// <summary>Stops serving the current authority and disconnects every client. Keeps listening.</summary>
        public void Detach() => ReleaseBridge(SessionNetworkCodes.AuthorityUnavailable);

        public void StopHost()
        {
            if (bootstrap != null)
            {
                bootstrap.Composed -= OnComposed;
                bootstrap.ShutDown -= OnShutDown;
            }
            ReleaseBridge(SessionNetworkCodes.HostStopped);
            if (Transport == null) return;
            Transport.ConnectionClosed -= OnConnectionClosed;
            Transport.Dispose();
            Transport = null;
        }

        private void Update() => Transport?.Update();

        // The authority publishes during Update (SimulationRunner); send it this frame.
        private void LateUpdate() => Transport?.Flush();

        private void OnDestroy() => StopHost();

        private void OnComposed(MissionSession session) => Attach(session);

        private void OnShutDown(MissionSession session)
        {
            if (ReferenceEquals(session, authority)) Detach();
        }

        private void ReleaseBridge(string reason)
        {
            if (Transport != null)
            {
                Transport.AcceptClients = false;
                Transport.DisconnectAll(reason);
            }
            if (Bridge != null)
            {
                Bridge.ProtocolError -= OnProtocolError;
                Bridge.Dispose();
                Bridge = null;
            }
            authority = null;
        }

        private void OnProtocolError(ulong connectionId, string code)
        {
            Diagnostic?.Invoke(connectionId, code);
            if (disconnectOnProtocolError && SessionNetworkArguments.IsClientFault(code))
                Transport?.DisconnectClient(connectionId, SessionNetworkCodes.ProtocolViolation);
        }

        private void OnConnectionClosed(ulong connectionId, string reason) => Diagnostic?.Invoke(connectionId, reason);
    }
}
