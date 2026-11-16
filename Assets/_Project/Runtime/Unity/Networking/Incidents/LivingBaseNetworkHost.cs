using System;
using Trainer.Application.Incidents;
using UnityEngine;

namespace Trainer.Unity.Networking.Incidents
{
    /// <summary>
    /// Phase 5 server/host composition (D3): the SAME Unity Transport over WebSocket host as phases 1–4 in front of a
    /// <see cref="LivingBaseCommandBridge"/> and the single <see cref="ILivingBaseAuthority"/> passed to <see cref="Attach"/>. Only in
    /// the authority process; one cable per scene, never next to an older host on the same port. Replacing or detaching the authority
    /// disconnects every client. A player in this process uses <see cref="CreateLocalPort"/>. Same command line as phases 1–4.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LivingBaseNetworkHost : MonoBehaviour
    {
        [SerializeField] private WebSocketHostOptions options = new WebSocketHostOptions();
        [SerializeField] private bool listenOnStart = true;
        [SerializeField] private bool readCommandLine = true;
        [SerializeField, Tooltip("Close a client whose message breaks the wire protocol.")]
        private bool disconnectOnProtocolError = true;
        [SerializeField, Min(0), Tooltip("Frame cap applied only in -batchmode. Snapshots go out once per frame. 0 = untouched.")]
        private int batchModeFrameRate = 30;
        [SerializeField] private bool runInBackground = true;

        private ILivingBaseAuthority authority;
        private long bindingVersion;

        public UnityTransportHost Transport { get; private set; }
        public LivingBaseCommandBridge Bridge { get; private set; }
        public ILivingBaseAuthority Authority => authority;
        public bool IsListening => Transport != null && Transport.IsListening;
        public int JoinedClientCount => Bridge?.JoinedClientCount ?? 0;

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

        public void StartHost()
        {
            if (IsListening) return;
            if (runInBackground) UnityEngine.Application.runInBackground = true;
            if (UnityEngine.Application.isBatchMode && batchModeFrameRate > 0) UnityEngine.Application.targetFrameRate = batchModeFrameRate;
            WebSocketHostOptions effective = options.Clone();
            if (readCommandLine) SessionNetworkArguments.Apply(effective, Environment.GetCommandLineArgs());
            var transport = new UnityTransportHost { AcceptClients = authority != null };
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
            if (authority != null) Bind(authority);
            string scheme = string.IsNullOrEmpty(effective.CertificatePemPath) ? "ws" : "wss";
            Debug.Log($"[LivingBaseNetworkHost] Listening on {scheme}://{effective.BindAddress}:{Transport.Port}{effective.Path} (cable 'T' 'L' v{LivingBaseWireProtocol.Version})");
        }

        public void Attach(ILivingBaseAuthority value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (ReferenceEquals(value, authority) && (Bridge != null || !IsListening)) return;
            long version = ++bindingVersion;
            Release(SessionNetworkCodes.AuthorityReplaced);
            if (bindingVersion != version) return; // a synchronous Leave/diagnostic callback attached a newer session
            authority = value;
            if (IsListening) Bind(value);
        }

        public void Detach()
        {
            ++bindingVersion;
            Release(SessionNetworkCodes.AuthorityUnavailable);
        }

        public LocalLivingBaseSessionPort CreateLocalPort()
        {
            if (authority == null) throw new InvalidOperationException("Attach an authority first.");
            return new LocalLivingBaseSessionPort(authority);
        }

        public void StopHost()
        {
            long version = ++bindingVersion;
            Release(SessionNetworkCodes.HostStopped);
            if (bindingVersion != version) return;
            if (Transport == null) return;
            Transport.ConnectionClosed -= OnConnectionClosed;
            Transport.Dispose();
            Transport = null;
        }

        private void Update() => Transport?.Update();

        private void LateUpdate()
        {
            Bridge?.FlushSnapshots();
            Transport?.Flush();
        }

        private void OnDestroy() => StopHost();

        private void Bind(ILivingBaseAuthority value)
        {
            Bridge = new LivingBaseCommandBridge(value, Transport);
            Bridge.ProtocolError += OnProtocolError;
            Transport.AcceptClients = true;
        }

        private void Release(string reason)
        {
            LivingBaseCommandBridge released = Bridge;
            Bridge = null;
            authority = null;
            if (released != null) released.ProtocolError -= OnProtocolError;
            try
            {
                if (Transport != null)
                {
                    Transport.AcceptClients = false;
                    Transport.DisconnectAll(reason);
                }
            }
            finally
            {
                released?.Dispose();
            }
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
