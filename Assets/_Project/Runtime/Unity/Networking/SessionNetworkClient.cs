using System;
using System.Globalization;
using UnityEngine;

namespace Trainer.Unity.Networking
{
    /// <summary>
    /// Client composition (desktop or WebGL player): <see cref="UnityTransportClient"/> +
    /// <see cref="SessionConnectionController"/>. Hand <see cref="Session"/> to the UI; never create a mission
    /// session or a SimulationRunner in a client process. Pumps the transport every frame on the main thread.
    /// If the link comes up but no authoritative state arrives within the synchronization timeout (e.g. the
    /// server rejects this client's protocol version), the link is ended as <see cref="SessionNetworkCodes.SyncTimeout"/>.
    /// Launch arguments: desktop command line -trainer-role client -trainer-server HOST -trainer-port N -trainer-path /p
    /// -trainer-secure -trainer-server-name NAME -trainer-connect; WebGL page query
    /// ?server=HOST&amp;port=N&amp;path=/p&amp;secure=1&amp;connect=1. In WebGL an empty address means "the host that served
    /// the page", with wss:// when the page is HTTPS.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionNetworkClient : MonoBehaviour
    {
        [SerializeField] private WebSocketClientOptions options = new WebSocketClientOptions();
        [SerializeField] private bool connectOnStart;
        [SerializeField] private bool readLaunchArguments = true;
        [SerializeField, Min(0), Tooltip("Seconds in Synchronizing before giving up. 0 = wait forever.")]
        private float synchronizationTimeoutSeconds = 10f;
        [SerializeField, Tooltip("Keep pumping the network while the window is unfocused (avoids host timeouts).")]
        private bool runInBackground = true;

        private UnityTransportClient transport;
        private float synchronizingSince;

        public SessionConnectionController Connection { get; private set; }
        /// <summary>The <see cref="Trainer.Application.ISessionPort"/> for the UI. Same instance across rejoins.</summary>
        public NetworkSessionAdapter Session => Connection?.Session;
        public SessionConnectionState State => Connection?.State ?? SessionConnectionState.Disconnected;
        public string Endpoint => transport?.Endpoint;

        /// <summary>Raised on every link state change after the controller's own StateChanged.</summary>
        public event Action<SessionConnectionState> StateChanged;

        /// <summary>(confirmed, detail) ready for MissionHUD.SetConnectionStatus. Confirmed only in Connected,
        /// i.e. after the authority's state arrived on the current link.</summary>
        public event Action<bool, string> ConnectionStatusChanged;

        /// <summary>Replaces the options. Only before the first Connect (the transport is then created).</summary>
        public void Configure(WebSocketClientOptions value)
        {
            if (Connection != null) throw new InvalidOperationException("Configure before the first Connect.");
            options = (value ?? throw new ArgumentNullException(nameof(value))).Clone();
        }

        private void Start()
        {
            bool connect = connectOnStart;
            if (readLaunchArguments) connect |= ApplyLaunchArguments();
            if (connect) Connect();
        }

        /// <summary>Joins (or rejoins after a loss). A rejoin is a new join: the old pending commands are gone.</summary>
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

        // UI submits during Update/OnGUI; send it this frame.
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
            if (runInBackground) UnityEngine.Application.runInBackground = true;
            transport = new UnityTransportClient(options);
            Connection = new SessionConnectionController(transport);
            Connection.StateChanged += OnStateChanged;
        }

        private void OnStateChanged(SessionConnectionState state)
        {
            if (state == SessionConnectionState.Synchronizing) synchronizingSince = Time.unscaledTime;
            StateChanged?.Invoke(state);
            ConnectionStatusChanged?.Invoke(SessionConnectionStatus.IsConfirmed(state),
                SessionConnectionStatus.Describe(state, Connection.LastDisconnectReason));
        }

        private bool ApplyLaunchArguments()
        {
            if (Connection != null) return false;
#if UNITY_WEBGL && !UNITY_EDITOR
            return SessionNetworkArguments.ApplyPageUrl(options, UnityEngine.Application.absoluteURL);
#else
            return SessionNetworkArguments.Apply(options, Environment.GetCommandLineArgs());
#endif
        }
    }

    /// <summary>Player-facing description of the link, for the connection panel. Mission state (Running,
    /// Completed...) never comes from here; it comes from snapshots.</summary>
    public static class SessionConnectionStatus
    {
        public static bool IsConfirmed(SessionConnectionState state) => state == SessionConnectionState.Connected;

        public static string Describe(SessionConnectionState state, string reason)
        {
            switch (state)
            {
                case SessionConnectionState.Connecting: return "Conectando con la base...";
                case SessionConnectionState.Synchronizing: return "Recibiendo el estado de la misión...";
                case SessionConnectionState.Connected: return "Conectado: datos confirmados por la base";
                case SessionConnectionState.ConnectionLost: return "Se perdió la conexión" + Explain(reason);
                default: return "Sin conexión con la base";
            }
        }

        private static string Explain(string reason)
        {
            switch (reason)
            {
                case "MaxConnectionAttempts": return ": no se encontró la base";
                case "Timeout": return ": la base dejó de responder";
                case "ClosedByRemote": return ": la base cerró la conexión";
                case "ProtocolError": return ": la comunicación con la base se cortó"; // e.g. server process died
                case "AuthenticationFailure": return ": conexión segura no válida";
                case SessionNetworkCodes.SyncTimeout: return ": la base no envió su estado a tiempo";
                case SessionNetworkCodes.ConnectFailed: return ": no se pudo iniciar la conexión";
                case SessionNetworkCodes.SendFailed: return ": no se pudo enviar";
                default: return string.IsNullOrEmpty(reason) ? "" : " (" + reason + ")";
            }
        }
    }

    /// <summary>Launch-argument parsing shared by host and client compositions.</summary>
    internal static class SessionNetworkArguments
    {
        /// <summary>Codes caused by what a client sent (not by the host itself).</summary>
        internal static bool IsClientFault(string code) =>
            code == SessionNetworkCodes.EmptyPayload || code == SessionNetworkCodes.PayloadTooLarge
            || code == SessionNetworkCodes.BadMagic || code == SessionNetworkCodes.UnsupportedVersion
            || code == SessionNetworkCodes.UnknownMessageKind || code == SessionNetworkCodes.UnexpectedMessageKind
            || code == SessionNetworkCodes.MalformedPayload;

        internal static void Apply(WebSocketHostOptions options, string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string value = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i])
                {
                    case "-trainer-bind": options.BindAddress = Required(args[i], value); i++; break;
                    case "-trainer-port": options.Port = Port(args[i], value); i++; break;
                    case "-trainer-path": options.Path = Required(args[i], value); i++; break;
                    case "-trainer-cert": options.CertificatePemPath = Required(args[i], value); i++; break;
                    case "-trainer-key": options.PrivateKeyPemPath = Required(args[i], value); i++; break;
                }
            }
        }

        /// <returns>True if -trainer-connect was given.</returns>
        internal static bool Apply(WebSocketClientOptions options, string[] args)
        {
            bool connect = false;
            for (int i = 0; i < args.Length; i++)
            {
                string value = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i])
                {
                    case "-trainer-server": options.Address = Required(args[i], value); i++; break;
                    case "-trainer-port": options.Port = Port(args[i], value); i++; break;
                    case "-trainer-path": options.Path = Required(args[i], value); i++; break;
                    case "-trainer-server-name": options.ServerName = Required(args[i], value); i++; break;
                    case "-trainer-secure": options.Secure = true; break;
                    case "-trainer-connect": connect = true; break;
                }
            }
            return connect;
        }

        /// <summary>WebGL: query parameters of the page URL. An empty address defaults to the page's host
        /// (wss:// when the page is HTTPS). Returns true if connect=1 was given.</summary>
        internal static bool ApplyPageUrl(WebSocketClientOptions options, string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri page)) return false;
            if (string.IsNullOrWhiteSpace(options.Address))
            {
                options.Address = page.Host;
                options.Secure = page.Scheme == Uri.UriSchemeHttps;
            }
            bool connect = false;
            string query = page.Query.TrimStart('?');
            foreach (string pair in query.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = pair.IndexOf('=');
                string key = Uri.UnescapeDataString(equals < 0 ? pair : pair.Substring(0, equals));
                string value = equals < 0 ? "" : Uri.UnescapeDataString(pair.Substring(equals + 1).Replace('+', ' '));
                switch (key)
                {
                    case "server": options.Address = Required(key, value); break;
                    case "port": options.Port = Port(key, value); break;
                    case "path": options.Path = Required(key, value); break;
                    case "serverName": options.ServerName = Required(key, value); break;
                    case "secure": options.Secure = value == "1" || value == "true"; break;
                    case "connect": connect = value == "1" || value == "true"; break;
                }
            }
            return connect;
        }

        private static string Required(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"Missing value for {name}.");
            return value.Trim();
        }

        private static int Port(string name, string value)
        {
            if (!int.TryParse(Required(name, value), NumberStyles.None, CultureInfo.InvariantCulture, out int port)
                || port < 0 || port > ushort.MaxValue)
                throw new ArgumentException($"Invalid port for {name}: '{value}'.");
            return port;
        }
    }
}
