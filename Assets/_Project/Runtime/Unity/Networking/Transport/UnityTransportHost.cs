using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using Unity.Collections;
using Unity.Networking.Transport;

namespace Trainer.Unity.Networking
{
    /// <summary>
    /// <see cref="ISessionHostTransport"/> over Unity Transport (com.unity.transport) with WebSocket, the socket
    /// type WebGL players can use. Runs on a non-WebGL player or the Editor (browsers cannot listen).
    /// Single-threaded: call <see cref="Update"/> once per frame (and preferably <see cref="Flush"/> late in the
    /// frame) on the Unity main thread; every event is raised from those calls.
    /// Connection ids are assigned here, increase monotonically and are never reused, so a stale id can never
    /// address a newer client. Nothing a client sends can choose or change its id.
    /// </summary>
    public sealed class UnityTransportHost : ISessionHostTransport, IDisposable
    {
        private readonly Dictionary<NetworkConnection, ulong> idsByConnection = new Dictionary<NetworkConnection, ulong>();
        private readonly Dictionary<ulong, NetworkConnection> connectionsById = new Dictionary<ulong, NetworkConnection>();
        // Connections closed by this side: Unity Transport raises no local Disconnect event for them.
        private readonly Queue<ulong> closedLocally = new Queue<ulong>();
        private NetworkDriver driver;
        private ulong lastId;
        private bool disposed;

        public event Action<ulong> ClientConnected;
        public event Action<ulong> ClientDisconnected;
        public event Action<ulong, byte[]> MessageReceived;

        /// <summary>Diagnostics: (connection id, reason) whenever a connection ends. Reasons are the
        /// transport's (ClosedByRemote, Timeout...) or the one passed to DisconnectClient/DisconnectAll.</summary>
        public event Action<ulong, string> ConnectionClosed;

        /// <summary>When false, new connections are closed as soon as they are accepted (no authority to
        /// serve them). Existing connections are unaffected.</summary>
        public bool AcceptClients { get; set; } = true;

        public bool IsListening => driver.IsCreated;
        public int ConnectionCount => connectionsById.Count;
        /// <summary>Port actually bound (useful when <see cref="WebSocketHostOptions.Port"/> is 0).</summary>
        public ushort Port => driver.IsCreated ? driver.GetLocalEndpoint().Port : (ushort)0;

        /// <summary>Binds and listens. Throws if already listening, on WebGL, or if the address/port is unusable.</summary>
        public void Listen(WebSocketHostOptions options)
        {
            if (disposed) throw new ObjectDisposedException(nameof(UnityTransportHost));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (driver.IsCreated) throw new InvalidOperationException("Host is already listening.");
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new PlatformNotSupportedException("A WebGL player cannot listen for connections.");
#else
            ushort port = options.ValidatedPort();
            if (!NetworkEndpoint.TryParse(options.BindAddress, port, out NetworkEndpoint endpoint))
                throw new ArgumentException($"Invalid bind address '{options.BindAddress}'.", nameof(options));
            if (port != 0) EnsureNoOtherListener(options.BindAddress, port);
            NetworkSettings settings = options.CreateSettings();
            try
            {
                driver = NetworkDriver.Create(new WebSocketNetworkInterface(), settings);
            }
            finally
            {
                settings.Dispose();
            }
            int bind = driver.Bind(endpoint);
            int listen = bind == 0 ? driver.Listen() : 0;
            if (bind != 0 || listen != 0)
            {
                driver.Dispose();
                driver = default;
                throw new InvalidOperationException(bind != 0
                    ? $"Cannot bind {options.BindAddress}:{port} (error {bind})."
                    : $"Cannot listen on {options.BindAddress}:{port} (error {listen}).");
            }
#endif
        }

        /// <summary>Receives and dispatches: deferred local closes, new connections, data and disconnects.
        /// A subscriber exception does not drop later events; the first one is rethrown at the end.</summary>
        public void Update()
        {
            if (!driver.IsCreated) return;
            driver.ScheduleUpdate().Complete();
            ExceptionDispatchInfo failure = null;

            while (closedLocally.Count > 0)
            {
                ulong id = closedLocally.Dequeue();
                Raise(ref failure, () => ClientDisconnected?.Invoke(id));
            }

            NetworkConnection accepted;
            while ((accepted = driver.Accept()) != default)
            {
                if (!AcceptClients)
                {
                    driver.Disconnect(accepted);
                    continue;
                }
                ulong id = checked(++lastId);
                idsByConnection.Add(accepted, id);
                connectionsById.Add(id, accepted);
                Raise(ref failure, () => ClientConnected?.Invoke(id));
            }

            NetworkEvent.Type type;
            while ((type = driver.PopEvent(out NetworkConnection connection, out DataStreamReader reader))
                   != NetworkEvent.Type.Empty)
            {
                // Unknown: closed locally or refused above; its remaining events are stale.
                if (!idsByConnection.TryGetValue(connection, out ulong id)) continue;
                if (type == NetworkEvent.Type.Data)
                {
                    byte[] payload = new byte[reader.Length];
                    reader.ReadBytes(payload);
                    Raise(ref failure, () => MessageReceived?.Invoke(id, payload));
                }
                else if (type == NetworkEvent.Type.Disconnect)
                {
                    string reason = UnityTransportReasons.Describe(reader);
                    Forget(connection, id);
                    Raise(ref failure, () => ConnectionClosed?.Invoke(id, reason));
                    Raise(ref failure, () => ClientDisconnected?.Invoke(id));
                }
            }
            failure?.Throw();
        }

        /// <summary>Sends what was queued this frame without waiting for the next <see cref="Update"/>.</summary>
        public void Flush()
        {
            if (driver.IsCreated) driver.ScheduleFlushSend().Complete();
        }

        /// <summary>Queues <paramref name="payload"/> for one client. Closed or unknown connections are ignored
        /// (their Disconnect event follows). Throws if the transport refuses the message (e.g. send queue full).</summary>
        public void SendToClient(ulong connectionId, byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (!driver.IsCreated || !connectionsById.TryGetValue(connectionId, out NetworkConnection connection))
                return;
            if (driver.GetConnectionState(connection) != NetworkConnection.State.Connected) return;
            UnityTransportReasons.Send(driver, connection, payload);
        }

        /// <summary>Closes one connection; the remote client gets ClosedByRemote. Idempotent. ClientDisconnected
        /// is raised on the next <see cref="Update"/>, never re-entrantly.</summary>
        public void DisconnectClient(ulong connectionId, string reason)
        {
            if (!connectionsById.TryGetValue(connectionId, out NetworkConnection connection)) return;
            Forget(connection, connectionId);
            if (driver.IsCreated) driver.Disconnect(connection);
            closedLocally.Enqueue(connectionId);
            ConnectionClosed?.Invoke(connectionId, reason);
        }

        public void DisconnectAll(string reason)
        {
            ulong[] ids = new ulong[connectionsById.Count];
            connectionsById.Keys.CopyTo(ids, 0);
            foreach (ulong id in ids) DisconnectClient(id, reason);
        }

        /// <summary>Closes every connection, tells the clients (one last send) and releases the socket.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (!driver.IsCreated) return;
            try
            {
                foreach (NetworkConnection connection in connectionsById.Values) driver.Disconnect(connection);
                // Disconnect only marks the connections; an update runs the state machine that tells the peer.
                driver.ScheduleUpdate().Complete();
                driver.ScheduleFlushSend().Complete();
            }
            finally
            {
                idsByConnection.Clear();
                connectionsById.Clear();
                closedLocally.Clear();
                driver.Dispose();
                driver = default;
            }
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        /// <summary>
        /// Unity Transport binds its listening socket with address reuse (Baselib AddressReuse.Allow), so a second
        /// server process on the same port starts "successfully" and the OS can split clients between two
        /// authorities. A bind probe cannot detect that portably (Mono's ReuseAddress also sets SO_REUSEPORT,
        /// and a probe without reuse would block restarts while TIME_WAIT sockets remain), so probe by
        /// connecting: if anything accepts on that port, refuse to start. Probe and bind can still race if two
        /// servers start at the same instant.
        /// </summary>
        private static void EnsureNoOtherListener(string bindAddress, ushort port)
        {
            IPAddress address = IPAddress.Parse(bindAddress.Trim());
            if (address.Equals(IPAddress.Any)) address = IPAddress.Loopback;
            else if (address.Equals(IPAddress.IPv6Any)) address = IPAddress.IPv6Loopback;
            using (var probe = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp))
            {
                bool accepted;
                try
                {
                    IAsyncResult attempt = probe.BeginConnect(new IPEndPoint(address, port), null, null);
                    accepted = attempt.AsyncWaitHandle.WaitOne(500) && Finish(probe, attempt);
                }
                catch (SocketException)
                {
                    accepted = false;
                }
                if (accepted)
                    throw new InvalidOperationException(
                        $"Another server already listens on {bindAddress}:{port}; only one authority may use it.");
            }
        }

        private static bool Finish(Socket probe, IAsyncResult attempt)
        {
            try
            {
                probe.EndConnect(attempt);
                return probe.Connected;
            }
            catch (SocketException)
            {
                return false; // Refused: nobody listens there.
            }
        }
#endif

        private void Forget(NetworkConnection connection, ulong id)
        {
            idsByConnection.Remove(connection);
            connectionsById.Remove(id);
        }

        private static void Raise(ref ExceptionDispatchInfo failure, Action raise)
        {
            try
            {
                raise();
            }
            catch (Exception exception)
            {
                if (failure == null) failure = ExceptionDispatchInfo.Capture(exception);
            }
        }
    }
}
