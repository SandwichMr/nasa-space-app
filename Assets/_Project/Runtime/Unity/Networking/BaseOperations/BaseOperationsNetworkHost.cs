using System;
using Trainer.Application.BaseOperations;
using UnityEngine;

namespace Trainer.Unity.Networking.BaseOperations
{
    /// <summary>
    /// Phase 4 server/host composition: the SAME Unity Transport over WebSocket host as phases 1–3
    /// (<see cref="UnityTransportHost"/>) in front of a <see cref="BaseOperationsCommandBridge"/> and the single
    /// <see cref="IBaseOperationsAuthority"/> that D2's composition passes to <see cref="Attach"/>. Place it ONLY in the
    /// authority process; a scene uses one cable, so never next to an Outpost/Expedition host on the same port. Replacing or
    /// detaching the authority disconnects every client and makes their members leave once. A player in this same process uses
    /// <see cref="CreateLocalPort"/>. Command line (non-WebGL), same as phases 1–3: -trainer-bind ADDRESS -trainer-port N
    /// -trainer-path /p -trainer-cert F -trainer-key F.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BaseOperationsNetworkHost : MonoBehaviour
    {
        [SerializeField] private WebSocketHostOptions options = new WebSocketHostOptions();
        [SerializeField] private bool listenOnStart = true;
        [SerializeField] private bool readCommandLine = true;
        [SerializeField, Tooltip("Close a client whose message breaks the wire protocol.")]
        private bool disconnectOnProtocolError = true;
        [SerializeField, Min(0), Tooltip("Frame cap applied only in -batchmode. Snapshots go out once per frame. 0 = untouched.")]
        private int batchModeFrameRate = 30;
        [SerializeField] private bool runInBackground = true;

        private IBaseOperationsAuthority authority;
        private long bindingVersion;

        public UnityTransportHost Transport { get; private set; }
        public BaseOperationsCommandBridge Bridge { get; private set; }
        public IBaseOperationsAuthority Authority => authority;
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
            Debug.Log($"[BaseOperationsNetworkHost] Listening on {scheme}://{effective.BindAddress}:{Transport.Port}{effective.Path} (cable 'T' 'B' v{BaseOperationsWireProtocol.Version})");
        }

        public void Attach(IBaseOperationsAuthority value)
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

        public LocalBaseOperationsSessionPort CreateLocalPort()
        {
            if (authority == null) throw new InvalidOperationException("Attach an authority first.");
            return new LocalBaseOperationsSessionPort(authority);
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

        private void Bind(IBaseOperationsAuthority value)
        {
            Bridge = new BaseOperationsCommandBridge(value, Transport);
            Bridge.ProtocolError += OnProtocolError;
            Transport.AcceptClients = true;
        }

        private void Release(string reason)
        {
            BaseOperationsCommandBridge released = Bridge;
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
