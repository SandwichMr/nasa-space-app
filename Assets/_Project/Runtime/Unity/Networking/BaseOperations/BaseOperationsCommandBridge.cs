using System;
using System.Collections.Generic;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Outpost;
using Trainer.Unity.Networking.Outpost;

namespace Trainer.Unity.Networking.BaseOperations
{
    /// <summary>
    /// Host side of phase 4: between the transport (real connections) and the single <see cref="IBaseOperationsAuthority"/>
    /// (D2). Same rules as the phase 2–3 bridges, on the 'T' 'B' cable:
    /// <list type="bullet">
    /// <item>Identity only from <see cref="IBaseOperationsAuthority.Join"/> on a real connection; nothing in a payload chooses
    /// or changes it, so a client cannot act for another member.</item>
    /// <item>A late joiner gets the CURRENT full state (stocks, machines, spares, reservations, orders, operations) before its
    /// Joined membership, never the initial values.</item>
    /// <item>Results go only to their sender. Phase 2/3 intents and phase 4 operations are forwarded as they are; the
    /// authority validates and deduplicates (one id space for both kinds).</item>
    /// <item>Disconnect (or disposal) calls Leave exactly once per member: the authority ends its operation, releases its
    /// reserved spares and keeps its orders, so no ghost, phantom reservation or second charge remains.</item>
    /// <item>Snapshots are coalesced and only the newest is sent once per frame (<see cref="FlushSnapshots"/>).</item>
    /// </list>
    /// It never validates base rules, moves anyone, advances a clock or touches stocks.
    /// </summary>
    public sealed class BaseOperationsCommandBridge : IDisposable
    {
        private readonly IBaseOperationsAuthority authority;
        private readonly ISessionHostTransport transport;
        private readonly HashSet<ulong> connected = new HashSet<ulong>();
        private readonly Dictionary<ulong, PlayerId> players = new Dictionary<ulong, PlayerId>();
        private readonly Dictionary<PlayerId, ulong> connections = new Dictionary<PlayerId, ulong>();
        private readonly List<ulong> failedLinks = new List<ulong>();
        private BaseOperationsSnapshot latest;
        private bool dirty;
        private bool disposed;

        /// <summary>(connection, code): dropped message, failed send or truncated reason.</summary>
        public event Action<ulong, string> ProtocolError;

        public int ConnectedClientCount => connected.Count;
        public int JoinedClientCount => players.Count;
        public long IgnoredMovementCount { get; private set; }
        /// <summary>Largest snapshot message sent so far, bytes (before framing).</summary>
        public int LargestSnapshotBytes { get; private set; }
        /// <summary>Leave calls made by this bridge (one per member whose link ended).</summary>
        public long LeaveCount { get; private set; }

        public BaseOperationsCommandBridge(IBaseOperationsAuthority authority, ISessionHostTransport transport)
        {
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            authority.SnapshotChanged += OnSnapshot;
            authority.CommandCompleted += OnResult;
            transport.ClientConnected += OnClientConnected;
            transport.ClientDisconnected += OnClientDisconnected;
            transport.MessageReceived += OnMessage;
        }

        public bool TryGetPlayer(ulong connectionId, out PlayerId player) => players.TryGetValue(connectionId, out player);

        public void FlushSnapshots()
        {
            ReleaseFailedLinks();
            if (!dirty || latest == null) return;
            dirty = false;
            if (players.Count == 0) return;
            List<byte[]> frames = FramesOf(latest);
            ulong[] targets = new ulong[players.Count];
            players.Keys.CopyTo(targets, 0); // a send may synchronously disconnect a client
            foreach (ulong target in targets) SendFrames(target, frames);
            ReleaseFailedLinks();
        }

        /// <summary>A link whose send failed leaves ONCE here, without waiting for a ClientDisconnected that may never come
        /// (a throwing DisconnectClient, or a transport disposed before its next Update). Deferred out of the send so the
        /// authority is never re-entered from inside its own callback; idempotent with <see cref="OnClientDisconnected"/>.</summary>
        private void ReleaseFailedLinks()
        {
            while (failedLinks.Count > 0)
            {
                ulong id = failedLinks[0];
                failedLinks.RemoveAt(0);
                if (!players.TryGetValue(id, out PlayerId player)) continue;
                players.Remove(id);
                connections.Remove(player);
                Leave(player);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            authority.SnapshotChanged -= OnSnapshot;
            authority.CommandCompleted -= OnResult;
            transport.ClientConnected -= OnClientConnected;
            transport.ClientDisconnected -= OnClientDisconnected;
            transport.MessageReceived -= OnMessage;
            var leaving = new List<PlayerId>(players.Values);
            players.Clear();
            connections.Clear();
            connected.Clear();
            foreach (PlayerId player in leaving) Leave(player);
        }

        private List<byte[]> FramesOf(BaseOperationsSnapshot snapshot)
        {
            byte[] message = BaseOperationsWireProtocol.EncodeSnapshot(snapshot);
            LargestSnapshotBytes = Math.Max(LargestSnapshotBytes, message.Length);
            return BaseOperationsWireProtocol.Frame(message);
        }

        private void OnClientConnected(ulong connectionId) => connected.Add(connectionId);

        private void OnClientDisconnected(ulong connectionId)
        {
            connected.Remove(connectionId);
            if (!players.TryGetValue(connectionId, out PlayerId player)) return;
            players.Remove(connectionId);
            connections.Remove(player);
            Leave(player); // committed first: Leave raises a snapshot that must not reach this link
        }

        private void Leave(PlayerId player)
        {
            LeaveCount++;
            authority.Leave(player);
        }

        private void OnMessage(ulong sender, byte[] payload)
        {
            if (!connected.Contains(sender))
            {
                ProtocolError?.Invoke(sender, SessionNetworkCodes.UnknownConnection);
                return;
            }
            if (!BaseOperationsWireProtocol.TryDecodeFromClient(payload, out BaseOperationsClientMessage message, out string error))
            {
                ProtocolError?.Invoke(sender, error);
                return;
            }
            bool joined = players.TryGetValue(sender, out PlayerId player);
            switch (message.Kind)
            {
                case OutpostMessageKind.Join:
                    if (joined)
                    {
                        SendState(sender, OutpostMembership.Joined(player));
                        return;
                    }
                    OutpostMembership membership = authority.Join();
                    if (membership.IsJoined)
                    {
                        players.Add(sender, membership.Player);
                        connections.Add(membership.Player, sender);
                    }
                    SendState(sender, membership);
                    return;
                case OutpostMessageKind.Submit:
                    if (!joined)
                    {
                        NotJoined(sender, message.Command.Id, message.Command.Epoch);
                        return;
                    }
                    authority.Submit(player, message.Command);
                    return;
                case OutpostMessageKind.Movement:
                    if (!joined || authority.SubmitMovement(player, message.Movement) != MovementInputDisposition.Applied)
                        IgnoredMovementCount++;
                    return;
                default: // BaseOperationsWireProtocol.Operation
                    if (!joined)
                    {
                        NotJoined(sender, message.Operation.Id, message.Operation.Epoch);
                        return;
                    }
                    authority.Submit(player, message.Operation);
                    return;
            }
        }

        private void NotJoined(ulong sender, Guid id, long epoch) =>
            Send(sender, BaseOperationsWireProtocol.EncodeResult(new CommandResult(id, epoch, false, OutpostReasons.NotJoined), out _));

        private void SendState(ulong target, OutpostMembership membership)
        {
            if (membership.IsJoined && authority.TryGetSnapshot(out BaseOperationsSnapshot current) && current != null)
                SendFrames(target, FramesOf(current));
            Send(target, BaseOperationsWireProtocol.EncodeMembership(membership));
        }

        private void OnSnapshot(BaseOperationsSnapshot snapshot)
        {
            if (snapshot == null) return;
            latest = snapshot;
            dirty = true;
        }

        private void OnResult(PlayerId sender, CommandResult result)
        {
            if (result == null || !connections.TryGetValue(sender, out ulong target)) return;
            byte[] payload = BaseOperationsWireProtocol.EncodeResult(result, out bool truncated);
            if (truncated) ProtocolError?.Invoke(target, SessionNetworkCodes.ReasonTruncated);
            Send(target, payload);
        }

        private void SendFrames(ulong target, List<byte[]> frames)
        {
            foreach (byte[] frame in frames)
                if (!Send(target, (byte[])frame.Clone())) return;
        }

        private bool Send(ulong target, byte[] payload)
        {
            try
            {
                transport.SendToClient(target, payload);
                return true;
            }
            catch (Exception)
            {
                connected.Remove(target);
                if (!failedLinks.Contains(target)) failedLinks.Add(target);
                try { transport.DisconnectClient(target, SessionNetworkCodes.SendFailed); }
                catch (Exception) { /* the binding must still report link loss */ }
                finally { ProtocolError?.Invoke(target, SessionNetworkCodes.SendFailed); }
                return false;
            }
        }
    }
}
