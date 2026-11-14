using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Error;

namespace Trainer.Unity.Networking
{
    /// <summary>
    /// <see cref="ISessionClientTransport"/> over Unity Transport WebSocket. Works in desktop players, the Editor
    /// and WebGL (the browser opens the socket). Single-threaded: call <see cref="Update"/> once per frame (and
    /// preferably <see cref="Flush"/> late in the frame) on the Unity main thread; events come only from Update.
    /// Each controller link maps to its own transport connection, so events of an older attempt keep their own
    /// link id and the controller can ignore them.
    /// </summary>
    public sealed class UnityTransportClient : ISessionClientTransport, IDisposable
    {
        private readonly WebSocketClientOptions options;
        private readonly Dictionary<long, NetworkConnection> connectionsByLink = new Dictionary<long, NetworkConnection>();
        private readonly Dictionary<NetworkConnection, long> linksByConnection = new Dictionary<NetworkConnection, long>();
        private NetworkDriver driver;
        private bool disposed;

        public event Action<long> Connected;
        public event Action<long, string> Disconnected;
        public event Action<long, byte[]> MessageReceived;

        /// <param name="options">Copied: later edits to the instance do not affect this transport.</param>
        public UnityTransportClient(WebSocketClientOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            this.options = options.Clone();
            // Validate now so a bad address/port fails at composition, not at the first Connect.
            this.options.CreateSettings().Dispose();
        }

        public string Endpoint => $"{(options.Secure ? "wss" : "ws")}://{options.Address}:{options.Port}{options.Path}";

        /// <summary>Starts a connection attempt; it ends with Connected(link) or Disconnected(link, reason).
        /// Throws if the transport cannot even start it (the controller reports ConnectFailed).</summary>
        public void Connect(long link)
        {
            if (disposed) throw new ObjectDisposedException(nameof(UnityTransportClient));
            if (connectionsByLink.ContainsKey(link)) throw new InvalidOperationException($"Link {link} already exists.");
            EnsureDriver();
            NetworkConnection connection = driver.Connect(new FixedString512Bytes(options.Address.Trim()),
                options.ValidatedPort());
            if (connection == default)
                throw new InvalidOperationException($"Unity Transport could not start a connection to {Endpoint}.");
            connectionsByLink.Add(link, connection);
            linksByConnection.Add(connection, link);
        }

        /// <summary>Closes or cancels the link. Idempotent; unknown links are ignored. The remote host sees
        /// ClosedByRemote once the next update is sent.</summary>
        public void Disconnect(long link)
        {
            if (!connectionsByLink.TryGetValue(link, out NetworkConnection connection)) return;
            connectionsByLink.Remove(link);
            linksByConnection.Remove(connection);
            if (driver.IsCreated) driver.Disconnect(connection);
        }

        /// <summary>Queues a message on an open link. A link whose transport connection already closed drops the
        /// message silently: its Disconnected event follows and the command is abandoned as result-unknown.
        /// Throws for unknown links or if the transport refuses the message.</summary>
        public void SendToHost(long link, byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (!driver.IsCreated || !connectionsByLink.TryGetValue(link, out NetworkConnection connection))
                throw new InvalidOperationException($"Link {link} is not open.");
            if (driver.GetConnectionState(connection) != NetworkConnection.State.Connected) return;
            UnityTransportReasons.Send(driver, connection, payload);
        }

        /// <summary>Receives and dispatches events. A subscriber exception does not drop later events; the
        /// first one is rethrown at the end.</summary>
        public void Update()
        {
            if (!driver.IsCreated) return;
            driver.ScheduleUpdate().Complete();
            ExceptionDispatchInfo failure = null;
            NetworkEvent.Type type;
            while ((type = driver.PopEvent(out NetworkConnection connection, out DataStreamReader reader))
                   != NetworkEvent.Type.Empty)
            {
                if (!linksByConnection.TryGetValue(connection, out long link)) continue; // Closed locally.
                try
                {
                    if (type == NetworkEvent.Type.Connect) Connected?.Invoke(link);
                    else if (type == NetworkEvent.Type.Data)
                    {
                        byte[] payload = new byte[reader.Length];
                        reader.ReadBytes(payload);
                        MessageReceived?.Invoke(link, payload);
                    }
                    else if (type == NetworkEvent.Type.Disconnect)
                    {
                        string reason = UnityTransportReasons.Describe(reader);
                        connectionsByLink.Remove(link);
                        linksByConnection.Remove(connection);
                        Disconnected?.Invoke(link, reason);
                    }
                }
                catch (Exception exception)
                {
                    if (failure == null) failure = ExceptionDispatchInfo.Capture(exception);
                }
            }
            failure?.Throw();
        }

        public void Flush()
        {
            if (driver.IsCreated) driver.ScheduleFlushSend().Complete();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (!driver.IsCreated) return;
            try
            {
                foreach (NetworkConnection connection in connectionsByLink.Values) driver.Disconnect(connection);
                // Disconnect only marks the connections; an update runs the state machine that tells the peer.
                driver.ScheduleUpdate().Complete();
                driver.ScheduleFlushSend().Complete();
            }
            finally
            {
                connectionsByLink.Clear();
                linksByConnection.Clear();
                driver.Dispose();
                driver = default;
            }
        }

        private void EnsureDriver()
        {
            if (driver.IsCreated) return;
            NetworkSettings settings = options.CreateSettings();
            try
            {
                driver = NetworkDriver.Create(new WebSocketNetworkInterface(), settings);
            }
            finally
            {
                settings.Dispose();
            }
        }
    }

    /// <summary>Shared Unity Transport helpers for both sides of the binding.</summary>
    internal static class UnityTransportReasons
    {
        /// <summary>Reason byte of a Disconnect event as text: Timeout, MaxConnectionAttempts, ClosedByRemote,
        /// AuthenticationFailure, ProtocolError... Unknown values become ConnectionLost.</summary>
        internal static string Describe(DataStreamReader reader)
        {
            if (reader.Length < 1) return SessionNetworkCodes.ConnectionLost;
            var reason = (DisconnectReason)reader.ReadByte();
            return reason == DisconnectReason.Default || !Enum.IsDefined(typeof(DisconnectReason), reason)
                ? SessionNetworkCodes.ConnectionLost
                : reason.ToString();
        }

        /// <summary>Reliability and order come from WebSocket/TCP, so the null pipeline is enough.</summary>
        internal static void Send(NetworkDriver driver, NetworkConnection connection, byte[] payload)
        {
            int status = driver.BeginSend(NetworkPipeline.Null, connection, out DataStreamWriter writer, payload.Length);
            if (status < 0) throw new InvalidOperationException($"Unity Transport BeginSend failed ({(StatusCode)status}).");
            if (!writer.WriteBytes(payload))
            {
                driver.AbortSend(writer);
                throw new InvalidOperationException($"Message of {payload.Length} bytes does not fit in one packet.");
            }
            status = driver.EndSend(writer);
            if (status < 0) throw new InvalidOperationException($"Unity Transport EndSend failed ({(StatusCode)status}).");
        }
    }
}
