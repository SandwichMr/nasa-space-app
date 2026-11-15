using System;
using System.Collections.Generic;
using Trainer.Application;
using Trainer.Application.Outpost;

namespace Trainer.Unity.Networking.Outpost
{
    /// <summary>
    /// Client-side <see cref="IOutpostSessionPort"/> backed by a remote authority. Sends intentions and
    /// publishes what the authority returns; it never simulates, moves anyone, validates rules or invents results.
    /// Created and driven by <see cref="OutpostConnectionController"/>. Single-threaded (Unity main thread).
    ///
    /// - Membership becomes Joined only once a snapshot containing the assigned player is stored.
    /// - Snapshots older than or equal to the current one (epoch, revision) are discarded.
    /// - Each Submit ends in one CommandCompleted (matching id and epoch) or one CommandAbandoned when the link
    ///   closes (result unknown). Unsolicited or duplicated results are discarded.
    /// - On link loss the membership becomes Left and the last snapshot is kept only as last known state.
    /// </summary>
    public sealed class OutpostNetworkSessionAdapter : IOutpostSessionPort
    {
        private readonly ISessionClientTransport transport;
        private readonly Dictionary<(Guid, long), List<OutpostCommand>> pending =
            new Dictionary<(Guid, long), List<OutpostCommand>>();
        private OutpostWireProtocol.Assembler assembler = new OutpostWireProtocol.Assembler();
        private OutpostSessionSnapshot snapshot;
        private PlayerId awaitingSnapshotFor = PlayerId.None;
        private bool linkOpen;
        private long link;
        private int pendingCount;

        public OutpostMembership Membership { get; private set; } = OutpostMembership.NotJoined;
        public event Action<OutpostSessionSnapshot> SnapshotChanged;
        public event Action<CommandResult> CommandCompleted;
        public event Action<OutpostCommand, string> CommandAbandoned;
        public event Action<OutpostMembership> MembershipChanged;
        /// <summary>A host message was dropped (a <see cref="SessionNetworkCodes"/> value).</summary>
        public event Action<string> ProtocolError;

        internal Action Joined;
        internal Action<string> Rejected;

        public int PendingCommandCount => pendingCount;
        public long DiscardedSnapshotCount { get; private set; }
        public long DiscardedResultCount { get; private set; }
        public long DroppedMovementCount { get; private set; }

        internal OutpostNetworkSessionAdapter(ISessionClientTransport transport)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public bool TryGetSnapshot(out OutpostSessionSnapshot value)
        {
            value = snapshot;
            return value != null;
        }

        public void Submit(OutpostCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!linkOpen || !Membership.IsJoined)
            {
                CommandAbandoned?.Invoke(command, OutpostReasons.NotConnected);
                return;
            }
            byte[] payload = OutpostWireProtocol.EncodeSubmit(command);
            var key = (command.Id, command.Epoch);
            if (!pending.TryGetValue(key, out List<OutpostCommand> queue))
            {
                queue = new List<OutpostCommand>(1);
                pending.Add(key, queue);
            }
            queue.Add(command); // Registered before sending: the result may come back immediately.
            pendingCount++;
            try
            {
                transport.SendToHost(link, payload);
            }
            catch
            {
                queue.Remove(command);
                pendingCount--;
                if (queue.Count == 0) pending.Remove(key);
                throw;
            }
        }

        public void SubmitMovement(MovementInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (!linkOpen || !Membership.IsJoined)
            {
                DroppedMovementCount++;
                return;
            }
            transport.SendToHost(link, OutpostWireProtocol.EncodeMovement(input));
        }

        internal void OpenLink(long id)
        {
            // A new link is a new join: never reuse the previous link's state or identity.
            link = id;
            linkOpen = true;
            snapshot = null;
            awaitingSnapshotFor = PlayerId.None;
            assembler = new OutpostWireProtocol.Assembler();
            if (Membership.Status != MembershipStatus.NotJoined) SetMembership(OutpostMembership.NotJoined);
        }

        internal void RequestJoin() => transport.SendToHost(link, OutpostWireProtocol.EncodeJoin());

        /// <summary>Commits the closed link (membership Left if joined) and returns the commands that lost their result.</summary>
        internal List<OutpostCommand> CloseLink(string reason, out bool membershipChanged)
        {
            linkOpen = false;
            awaitingSnapshotFor = PlayerId.None;
            assembler.Clear();
            membershipChanged = false;
            if (Membership.IsJoined)
            {
                Membership = OutpostMembership.Left(Membership.Player,
                    string.IsNullOrWhiteSpace(reason) ? OutpostReasons.ConnectionLost : reason);
                membershipChanged = true;
            }
            var abandoned = new List<OutpostCommand>(pendingCount);
            foreach (List<OutpostCommand> queue in pending.Values) abandoned.AddRange(queue);
            pending.Clear();
            pendingCount = 0;
            return abandoned;
        }

        internal void NotifyClosed(List<OutpostCommand> abandoned, string abandonReason, bool membershipChanged)
        {
            try
            {
                if (membershipChanged) MembershipChanged?.Invoke(Membership);
            }
            finally
            {
                foreach (OutpostCommand command in abandoned) CommandAbandoned?.Invoke(command, abandonReason);
            }
        }

        internal void Receive(byte[] frame)
        {
            if (!linkOpen) return; // Late delivery from a closed link.
            if (!assembler.TryAccept(frame, out byte[] payload, out string error))
            {
                if (error != null) ProtocolError?.Invoke(error);
                return; // Error, or a chunk of a message still incomplete.
            }
            if (!OutpostWireProtocol.TryDecodeFromHost(payload, out OutpostHostMessage message, out error))
            {
                ProtocolError?.Invoke(error);
                return;
            }
            switch (message.Kind)
            {
                case OutpostMessageKind.Snapshot: ApplySnapshot(message.Snapshot); break;
                case OutpostMessageKind.Result: ApplyResult(message.Result); break;
                case OutpostMessageKind.Membership: ApplyMembership(message.Membership); break;
            }
        }

        private void ApplySnapshot(OutpostSessionSnapshot next)
        {
            if (!next.IsNewerThan(snapshot))
            {
                DiscardedSnapshotCount++;
                return;
            }
            snapshot = next;
            SnapshotChanged?.Invoke(next);
            if (awaitingSnapshotFor.IsValid && linkOpen && next.TryGetCrew(awaitingSnapshotFor, out _))
                CompleteJoin(awaitingSnapshotFor);
        }

        private void ApplyMembership(OutpostMembership membership)
        {
            switch (membership.Status)
            {
                case MembershipStatus.Joined:
                    if (Membership.IsJoined) return; // Re-sent state of the same join.
                    if (snapshot != null && snapshot.TryGetCrew(membership.Player, out _)) CompleteJoin(membership.Player);
                    else awaitingSnapshotFor = membership.Player; // Joined only with state that contains the player.
                    return;
                case MembershipStatus.Rejected:
                    if (Membership.IsJoined) return;
                    SetMembership(membership);
                    Rejected?.Invoke(membership.Reason);
                    return;
                default:
                    ProtocolError?.Invoke(SessionNetworkCodes.UnexpectedMessageKind);
                    return;
            }
        }

        private void CompleteJoin(PlayerId player)
        {
            awaitingSnapshotFor = PlayerId.None;
            SetMembership(OutpostMembership.Joined(player));
            Joined?.Invoke();
        }

        private void ApplyResult(CommandResult result)
        {
            var key = (result.CommandId, result.Epoch);
            if (!pending.TryGetValue(key, out List<OutpostCommand> queue))
            {
                DiscardedResultCount++;
                return;
            }
            queue.RemoveAt(0);
            if (queue.Count == 0) pending.Remove(key);
            pendingCount--;
            CommandCompleted?.Invoke(result);
        }

        private void SetMembership(OutpostMembership value)
        {
            Membership = value;
            MembershipChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// Client connection lifecycle of phase 2 (same states and rules as phase 1's controller): Connecting ->
    /// Synchronizing (link up, Join sent) -> Connected (Joined, state received). A refused join (SessionFull)
    /// or host loss ends in ConnectionLost. No host migration, no automatic reconnection: Connect again is a
    /// fresh join that gets a new PlayerId.
    /// </summary>
    public sealed class OutpostConnectionController : IDisposable
    {
        private readonly ISessionClientTransport transport;
        private long link;
        private bool disposed;

        public OutpostNetworkSessionAdapter Session { get; }
        public SessionConnectionState State { get; private set; } = SessionConnectionState.Disconnected;
        public string LastDisconnectReason { get; private set; }
        public event Action<SessionConnectionState> StateChanged;

        public OutpostConnectionController(ISessionClientTransport transport)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Session = new OutpostNetworkSessionAdapter(transport) { Joined = OnJoined, Rejected = OnRejected };
            transport.Connected += OnConnected;
            transport.Disconnected += OnDisconnected;
            transport.MessageReceived += OnMessage;
        }

        public void Connect()
        {
            if (disposed) throw new ObjectDisposedException(nameof(OutpostConnectionController));
            if (State != SessionConnectionState.Disconnected && State != SessionConnectionState.ConnectionLost) return;
            long attempt = checked(++link);
            LastDisconnectReason = null;
            SetState(SessionConnectionState.Connecting);
            if (State != SessionConnectionState.Connecting || link != attempt) return;
            try
            {
                transport.Connect(attempt);
            }
            catch
            {
                if (State == SessionConnectionState.Connecting && link == attempt) LoseLink(SessionNetworkCodes.ConnectFailed);
                throw;
            }
        }

        /// <summary>Leaves: pending commands are abandoned with LocalDisconnect and the membership ends.</summary>
        public void Disconnect()
        {
            if (State == SessionConnectionState.Disconnected) return;
            bool wasLost = State == SessionConnectionState.ConnectionLost;
            if (!wasLost) LastDisconnectReason = SessionNetworkCodes.LocalDisconnect;
            State = SessionConnectionState.Disconnected;
            List<OutpostCommand> abandoned = Session.CloseLink(OutpostReasons.LocalDisconnect, out bool membershipChanged);
            try
            {
                transport.Disconnect(link);
            }
            finally
            {
                Session.NotifyClosed(abandoned, OutpostReasons.LocalDisconnect, membershipChanged);
                StateChanged?.Invoke(SessionConnectionState.Disconnected);
            }
        }

        /// <summary>Ends the current link as lost (for example SyncTimeout). Pending commands: ConnectionLost.</summary>
        public void AbortLink(string reason)
        {
            if (State == SessionConnectionState.Disconnected || State == SessionConnectionState.ConnectionLost) return;
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
                Session.Joined = null;
                Session.Rejected = null;
            }
        }

        private void OnConnected(long id)
        {
            if (id != link || State != SessionConnectionState.Connecting)
            {
                transport.Disconnect(id); // Superseded attempt that still came up: no ghost link.
                return;
            }
            Session.OpenLink(id);
            SetState(SessionConnectionState.Synchronizing);
            if (State != SessionConnectionState.Synchronizing || link != id) return;
            try
            {
                Session.RequestJoin();
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
            if (id != link) return;
            if (State == SessionConnectionState.Disconnected || State == SessionConnectionState.ConnectionLost) return;
            LoseLink(string.IsNullOrEmpty(reason) ? SessionNetworkCodes.ConnectionLost : reason);
        }

        private void OnMessage(long id, byte[] payload)
        {
            if (id == link && (State == SessionConnectionState.Synchronizing || State == SessionConnectionState.Connected))
                Session.Receive(payload);
        }

        private void OnJoined()
        {
            if (State == SessionConnectionState.Synchronizing) SetState(SessionConnectionState.Connected);
        }

        private void OnRejected(string reason) => AbortLink(reason);

        private void LoseLink(string reason)
        {
            State = SessionConnectionState.ConnectionLost;
            LastDisconnectReason = reason;
            List<OutpostCommand> abandoned = Session.CloseLink(reason, out bool membershipChanged);
            Session.NotifyClosed(abandoned, OutpostReasons.ConnectionLost, membershipChanged);
            StateChanged?.Invoke(SessionConnectionState.ConnectionLost);
        }

        private void SetState(SessionConnectionState next)
        {
            State = next;
            StateChanged?.Invoke(next);
        }
    }

    /// <summary>Player-facing link text for phase 2 (adds the join refusal to the phase 1 texts).</summary>
    public static class OutpostConnectionStatus
    {
        public static bool IsConfirmed(SessionConnectionState state) => state == SessionConnectionState.Connected;

        public static string Describe(SessionConnectionState state, string reason)
        {
            if (state == SessionConnectionState.ConnectionLost && reason == OutpostReasons.SessionFull)
                return "La base está completa: no queda sitio para otro astronauta";
            if (state == SessionConnectionState.Synchronizing) return "Entrando en la base...";
            return SessionConnectionStatus.Describe(state, reason);
        }
    }
}
