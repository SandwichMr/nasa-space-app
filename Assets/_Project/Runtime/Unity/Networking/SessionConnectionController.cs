using System;
using System.Collections.Generic;
using Trainer.Application;

namespace Trainer.Unity.Networking
{
    /// <summary>
    /// SDK-neutral client transport seam. Production binding: <see cref="UnityTransportClient"/> (Unity Transport
    /// over WebSocket, also in WebGL). Tests may use an in-memory loopback.
    /// Each connection attempt carries a <c>link</c> id chosen by the controller; the binding echoes it on every
    /// event of that attempt, so late events from an earlier attempt can be recognised and ignored.
    /// Requirements: reliable, ordered delivery on a link; every callback on the Unity main thread.
    /// </summary>
    public interface ISessionClientTransport
    {
        /// <summary>Starts attempt <paramref name="link"/>; ends with Connected(link) or Disconnected(link, reason).</summary>
        void Connect(long link);
        /// <summary>Closes or cancels <paramref name="link"/>. Idempotent; unknown or closed links are ignored.
        /// Events for that link may still arrive afterwards; they are ignored.</summary>
        void Disconnect(long link);
        /// <summary>Ownership of <paramref name="payload"/> passes to the transport.</summary>
        void SendToHost(long link, byte[] payload);
        event Action<long> Connected;
        event Action<long, string> Disconnected;
        event Action<long, byte[]> MessageReceived;
    }

    /// <summary>Link status only. Mission state (Ready, Interrupted...) comes exclusively from snapshots.</summary>
    public enum SessionConnectionState { Disconnected, Connecting, Synchronizing, Connected, ConnectionLost }

    /// <summary>
    /// Client connection lifecycle. Owns the <see cref="NetworkSessionAdapter"/> exposed as <see cref="Session"/>.
    /// Synchronizing: link up, waiting for the authority's initial snapshot. Host loss moves to ConnectionLost;
    /// there is no host migration or automatic reconnection. Calling Connect again is a fresh join.
    /// </summary>
    public sealed class SessionConnectionController : IDisposable
    {
        private readonly ISessionClientTransport transport;
        private long link;
        private bool disposed;

        public NetworkSessionAdapter Session { get; }
        public SessionConnectionState State { get; private set; } = SessionConnectionState.Disconnected;
        /// <summary>Why the most recent link ended: the transport's reason, ConnectFailed, SendFailed, or
        /// <see cref="SessionNetworkCodes.LocalDisconnect"/> when the local side ended a live or pending link.</summary>
        public string LastDisconnectReason { get; private set; }
        public event Action<SessionConnectionState> StateChanged;

        public SessionConnectionController(ISessionClientTransport transport)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Session = new NetworkSessionAdapter(transport);
            Session.SnapshotAccepted = OnSnapshotAccepted;
            transport.Connected += OnConnected;
            transport.Disconnected += OnDisconnected;
            transport.MessageReceived += OnMessage;
        }

        /// <summary>Starts a join. No-op while connecting or connected. If the transport throws, the state
        /// becomes ConnectionLost (ConnectFailed) so a later Connect can retry, and the exception propagates.</summary>
        public void Connect()
        {
            if (disposed) throw new ObjectDisposedException(nameof(SessionConnectionController));
            if (State != SessionConnectionState.Disconnected && State != SessionConnectionState.ConnectionLost)
                return;
            long attempt = checked(++link);
            LastDisconnectReason = null;
            SetState(SessionConnectionState.Connecting);
            if (State != SessionConnectionState.Connecting || link != attempt) return; // Changed by a callback.
            try
            {
                transport.Connect(attempt);
            }
            catch
            {
                if (State == SessionConnectionState.Connecting && link == attempt)
                    LoseLink(SessionNetworkCodes.ConnectFailed);
                throw;
            }
        }

        /// <summary>Leaves the session. Pending commands are abandoned with LocalDisconnect. After a
        /// ConnectionLost the original reason is kept. State is committed even if the transport throws.</summary>
        public void Disconnect()
        {
            if (State == SessionConnectionState.Disconnected) return;
            if (State != SessionConnectionState.ConnectionLost)
                LastDisconnectReason = SessionNetworkCodes.LocalDisconnect;
            // Everything is committed before any call out, so late or re-entrant callbacks see a closed link.
            State = SessionConnectionState.Disconnected;
            List<SessionCommand> abandoned = Session.CloseLink();
            try
            {
                transport.Disconnect(link);
            }
            finally
            {
                Session.NotifyAbandoned(abandoned, SessionNetworkCodes.LocalDisconnect);
                StateChanged?.Invoke(SessionConnectionState.Disconnected);
            }
        }

        /// <summary>Ends the current link as lost with <paramref name="reason"/>, e.g. <see
        /// cref="SessionNetworkCodes.SyncTimeout"/> detected by a composition that owns a clock. No-op when
        /// already Disconnected or ConnectionLost. Pending commands are abandoned with ConnectionLost (result
        /// unknown). State is committed before the transport is asked to close the link.</summary>
        public void AbortLink(string reason)
        {
            if (State == SessionConnectionState.Disconnected || State == SessionConnectionState.ConnectionLost)
                return;
            long id = link;
            LoseLink(string.IsNullOrEmpty(reason) ? SessionNetworkCodes.ConnectionLost : reason);
            transport.Disconnect(id);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                Disconnect();
            }
            finally
            {
                transport.Connected -= OnConnected;
                transport.Disconnected -= OnDisconnected;
                transport.MessageReceived -= OnMessage;
                Session.SnapshotAccepted = null;
            }
        }

        private void OnConnected(long id)
        {
            if (id != link || State != SessionConnectionState.Connecting)
            {
                // Cancelled or superseded attempt that still came up: tear it down so no ghost link remains.
                transport.Disconnect(id);
                return;
            }
            Session.OpenLink(id);
            SetState(SessionConnectionState.Synchronizing);
            if (State != SessionConnectionState.Synchronizing || link != id) return;
            try
            {
                // Explicit request: the initial state does not depend on connect/message callback ordering.
                Session.RequestInitialState();
            }
            catch
            {
                LoseLink(SessionNetworkCodes.SendFailed);
                transport.Disconnect(id);
                throw;
            }
        }

        private void OnDisconnected(long id, string reason)
        {
            if (id != link) return; // Late event from an earlier attempt.
            if (State == SessionConnectionState.Disconnected || State == SessionConnectionState.ConnectionLost)
                return;
            LoseLink(string.IsNullOrEmpty(reason) ? SessionNetworkCodes.ConnectionLost : reason);
        }

        private void OnMessage(long id, byte[] payload)
        {
            if (id == link && (State == SessionConnectionState.Synchronizing
                || State == SessionConnectionState.Connected))
                Session.Receive(payload);
        }

        private void OnSnapshotAccepted()
        {
            if (State == SessionConnectionState.Synchronizing) SetState(SessionConnectionState.Connected);
        }

        private void LoseLink(string reason)
        {
            State = SessionConnectionState.ConnectionLost;
            LastDisconnectReason = reason;
            List<SessionCommand> abandoned = Session.CloseLink();
            Session.NotifyAbandoned(abandoned, SessionNetworkCodes.ConnectionLost);
            StateChanged?.Invoke(SessionConnectionState.ConnectionLost);
        }

        private void SetState(SessionConnectionState next)
        {
            State = next;
            StateChanged?.Invoke(next);
        }
    }
}
