using System;
using System.Collections.Generic;
using Trainer.Application;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Unity.Networking.Outpost;

namespace Trainer.Unity.Networking.Expedition
{
    /// <summary>
    /// Host side of phase 3: between the transport (real connections) and the single <see cref="IExpeditionAuthority"/>
    /// (D2). Same rules as the phase 2 bridge, whose behaviour it mirrors on the phase 3 cable:
    /// identity only from <see cref="IExpeditionAuthority.Join"/> on a real connection (nothing in a payload chooses
    /// it); late joiners get the full state before Joined; results go only to their sender; disconnect (or disposal)
    /// calls Leave exactly once per member, so no ghost astronaut, abandoned reservation or phantom suit consumption
    /// remains; snapshots are coalesced and only the newest is sent once per frame (<see cref="FlushSnapshots"/>).
    /// It never validates mission rules, moves anyone, cycles the airlock or touches stocks.
    /// </summary>
    public sealed class ExpeditionCommandBridge : IDisposable
    {
        private readonly IExpeditionAuthority authority;
        private readonly ISessionHostTransport transport;
        private readonly HashSet<ulong> connected = new HashSet<ulong>();
        private readonly Dictionary<ulong, PlayerId> players = new Dictionary<ulong, PlayerId>();
        private readonly Dictionary<PlayerId, ulong> connections = new Dictionary<PlayerId, ulong>();
        private ExpeditionSessionSnapshot latest;
        private bool dirty;
        private bool disposed;

        /// <summary>(connection, code): dropped message, failed send or truncated reason.</summary>
        public event Action<ulong, string> ProtocolError;

        public int ConnectedClientCount => connected.Count;
        public int JoinedClientCount => players.Count;
        public long IgnoredMovementCount { get; private set; }
        /// <summary>Largest snapshot message sent so far, bytes (before framing).</summary>
        public int LargestSnapshotBytes { get; private set; }

        public ExpeditionCommandBridge(IExpeditionAuthority authority, ISessionHostTransport transport)
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
            if (!dirty || latest == null) return;
            dirty = false;
            if (players.Count == 0) return;
            List<byte[]> frames = FramesOf(latest);
            ulong[] targets = new ulong[players.Count];
            players.Keys.CopyTo(targets, 0); // a send may synchronously disconnect a client
            foreach (ulong target in targets) SendFrames(target, frames);
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
            foreach (PlayerId player in leaving) authority.Leave(player);
        }

        private List<byte[]> FramesOf(ExpeditionSessionSnapshot snapshot)
        {
            byte[] message = ExpeditionWireProtocol.EncodeSnapshot(snapshot);
            LargestSnapshotBytes = Math.Max(LargestSnapshotBytes, message.Length);
            return ExpeditionWireProtocol.Frame(message);
        }

        private void OnClientConnected(ulong connectionId) => connected.Add(connectionId);

        private void OnClientDisconnected(ulong connectionId)
        {
            connected.Remove(connectionId);
            if (!players.TryGetValue(connectionId, out PlayerId player)) return;
            players.Remove(connectionId);
            connections.Remove(player);
            authority.Leave(player); // committed first: Leave raises a snapshot that must not reach this link
        }

        private void OnMessage(ulong sender, byte[] payload)
        {
            if (!connected.Contains(sender))
            {
                ProtocolError?.Invoke(sender, SessionNetworkCodes.UnknownConnection);
                return;
            }
            if (!ExpeditionWireProtocol.TryDecodeFromClient(payload, out ExpeditionClientMessage message, out string error))
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
                        Send(sender, ExpeditionWireProtocol.EncodeResult(new CommandResult(message.Command.Id,
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

        private void SendState(ulong target, OutpostMembership membership)
        {
            if (membership.IsJoined && authority.TryGetSnapshot(out ExpeditionSessionSnapshot current))
                SendFrames(target, FramesOf(current));
            Send(target, ExpeditionWireProtocol.EncodeMembership(membership));
        }

        private void OnSnapshot(ExpeditionSessionSnapshot snapshot)
        {
            if (snapshot == null) return;
            latest = snapshot;
            dirty = true;
        }

        private void OnResult(PlayerId sender, CommandResult result)
        {
            if (result == null || !connections.TryGetValue(sender, out ulong target)) return;
            byte[] payload = ExpeditionWireProtocol.EncodeResult(result, out bool truncated);
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
                try { transport.DisconnectClient(target, SessionNetworkCodes.SendFailed); }
                catch (Exception) { /* the binding must still report link loss */ }
                finally { ProtocolError?.Invoke(target, SessionNetworkCodes.SendFailed); }
                return false;
            }
        }
    }
}
