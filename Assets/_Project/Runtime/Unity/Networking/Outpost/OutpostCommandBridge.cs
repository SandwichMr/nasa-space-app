using System;
using System.Collections.Generic;
using Trainer.Application;
using Trainer.Application.Outpost;

namespace Trainer.Unity.Networking.Outpost
{
    /// <summary>
    /// Host side of phase 2. Sits between the transport (real connections) and the single
    /// <see cref="IOutpostAuthority"/> (D2). It never validates mission rules, moves anyone or touches stocks.
    ///
    /// - Identity: a connection becomes a crew member only through <see cref="IOutpostAuthority.Join"/> when it
    ///   sends Join; the returned PlayerId is bound to that connection and NOTHING in a payload can choose or
    ///   change it. A second Join on the same connection re-sends its state (idempotent, no duplicate member).
    /// - Late joiners get the full current snapshot first, then their Joined membership.
    /// - Commands and movement are forwarded with the bound sender; results go only to their sender.
    /// - Disconnect (or bridge disposal) calls <see cref="IOutpostAuthority.Leave"/> exactly once per member, so
    ///   no ghost astronaut or orphan reservation remains.
    /// - Snapshots are full state, so the bridge coalesces them: <see cref="FlushSnapshots"/> (once per frame,
    ///   from the host component) sends only the newest one to every joined connection.
    /// Single-threaded (Unity main thread). Transport exceptions never escape into the authority's events.
    /// </summary>
    public sealed class OutpostCommandBridge : IDisposable
    {
        private readonly IOutpostAuthority authority;
        private readonly ISessionHostTransport transport;
        private readonly HashSet<ulong> connected = new HashSet<ulong>();
        private readonly Dictionary<ulong, PlayerId> players = new Dictionary<ulong, PlayerId>();
        private readonly Dictionary<PlayerId, ulong> connections = new Dictionary<PlayerId, ulong>();
        private OutpostSessionSnapshot latest;
        private bool dirty;
        private bool disposed;

        /// <summary>(connection, code): dropped message, failed send (SendFailed) or truncated reason.</summary>
        public event Action<ulong, string> ProtocolError;

        public int ConnectedClientCount => connected.Count;
        public int JoinedClientCount => players.Count;
        /// <summary>Movement inputs the authority did not apply (stale, other epoch, unknown sender).</summary>
        public long IgnoredMovementCount { get; private set; }

        public OutpostCommandBridge(IOutpostAuthority authority, ISessionHostTransport transport)
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

        /// <summary>Sends the newest snapshot (if it changed since the last flush) to every joined connection.</summary>
        public void FlushSnapshots()
        {
            if (!dirty || latest == null) return;
            dirty = false;
            if (players.Count == 0) return;
            List<byte[]> frames = OutpostWireProtocol.Frame(OutpostWireProtocol.EncodeSnapshot(latest));
            ulong[] targets = new ulong[players.Count];
            players.Keys.CopyTo(targets, 0); // Copy: a send may synchronously disconnect a client.
            foreach (ulong target in targets) SendFrames(target, frames);
        }

        /// <summary>Unsubscribes and makes every joined member leave the authority (their links are gone).</summary>
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
            foreach (PlayerId player in leaving) authority.Leave(player);
        }

        private void OnClientConnected(ulong connectionId) => connected.Add(connectionId);

        private void OnClientDisconnected(ulong connectionId)
        {
            connected.Remove(connectionId);
            if (!players.TryGetValue(connectionId, out PlayerId player)) return;
            players.Remove(connectionId);
            connections.Remove(player);
            authority.Leave(player); // Committed first: Leave raises a snapshot that must not reach this link.
        }

        private void OnMessage(ulong sender, byte[] payload)
        {
            if (!connected.Contains(sender))
            {
                ProtocolError?.Invoke(sender, SessionNetworkCodes.UnknownConnection);
                return;
            }
            if (!OutpostWireProtocol.TryDecodeFromClient(payload, out OutpostClientMessage message, out string error))
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
                        // No identity to act for: answered here, the authority never sees it.
                        Send(sender, OutpostWireProtocol.EncodeResult(new CommandResult(message.Command.Id,
                            message.Command.Epoch, false, OutpostReasons.NotJoined), out _));
                        return;
                    }
                    authority.Submit(player, message.Command);
                    return;
                case OutpostMessageKind.Movement:
                    if (!joined || authority.SubmitMovement(player, message.Movement) != MovementInputDisposition.Applied)
                        IgnoredMovementCount++;
                    return;
            }
        }

        /// <summary>Current full state first (a joiner must hold it before Joined), then the membership.</summary>
        private void SendState(ulong target, OutpostMembership membership)
        {
            if (membership.IsJoined && authority.TryGetSnapshot(out OutpostSessionSnapshot current))
                SendFrames(target, OutpostWireProtocol.Frame(OutpostWireProtocol.EncodeSnapshot(current)));
            Send(target, OutpostWireProtocol.EncodeMembership(membership));
        }

        private void OnSnapshot(OutpostSessionSnapshot snapshot)
        {
            if (snapshot == null) return;
            latest = snapshot;
            dirty = true;
        }

        private void OnResult(PlayerId sender, CommandResult result)
        {
            // Host-local players and members that already left have no connection here.
            if (result == null || !connections.TryGetValue(sender, out ulong target)) return;
            byte[] payload = OutpostWireProtocol.EncodeResult(result, out bool truncated);
            if (truncated) ProtocolError?.Invoke(target, SessionNetworkCodes.ReasonTruncated);
            Send(target, payload);
        }

        private void SendFrames(ulong target, List<byte[]> frames)
        {
            // Each send gets its own array: the transport takes ownership and frames are shared across targets.
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
                // Runs inside the authority's events: a failing link must not break other subscribers/clients.
                connected.Remove(target);
                try { transport.DisconnectClient(target, SessionNetworkCodes.SendFailed); }
                catch (Exception) { /* The binding must still report link loss. */ }
                finally { ProtocolError?.Invoke(target, SessionNetworkCodes.SendFailed); }
                return false;
            }
        }
    }
}
