using System;
using System.Collections.Generic;
using Trainer.Application;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Unity.Networking.Outpost;

namespace Trainer.Unity.Networking.Expedition
{
    /// <summary>
    /// Client-side <see cref="IExpeditionSessionPort"/> backed by the remote phase 3 authority. Like the phase 2
    /// adapter, it sends intentions and publishes what the authority returns; it never simulates, moves anyone,
    /// consumes suit services, cycles the airlock, evaluates or invents results (so no client can diverge).
    ///
    /// - Membership becomes Joined only once a snapshot containing the assigned player is stored.
    /// - Snapshots older than or equal to the current one are discarded; for each stored one SnapshotChanged (phase 2
    ///   part) is raised first, then ExpeditionChanged.
    /// - A phase 2 <see cref="OutpostCommand"/> is sent as <see cref="ExpeditionCommand.FromOutpost"/> stamped with the
    ///   local member's zone sequence of the latest snapshot; its abandonment goes to CommandAbandoned. A phase 3
    ///   command's abandonment goes to ExpeditionCommandAbandoned. Results of both arrive through CommandCompleted.
    /// - On link loss the membership becomes Left, pending commands are abandoned (result unknown) and the last snapshot
    ///   is kept only as last known state.
    /// </summary>
    public sealed class ExpeditionNetworkSessionAdapter : IExpeditionSessionPort
    {
        internal sealed class Submission
        {
            public OutpostCommand Outpost;
            public ExpeditionCommand Expedition;
        }

        private readonly ISessionClientTransport transport;
        private readonly Dictionary<(Guid, long), List<Submission>> pending = new Dictionary<(Guid, long), List<Submission>>();
        private Trainer.Unity.Networking.Outpost.OutpostWireProtocol.Assembler assembler = ExpeditionWireProtocol.NewAssembler();
        private ExpeditionSessionSnapshot snapshot;
        private PlayerId awaitingSnapshotFor = PlayerId.None;
        private bool linkOpen;
        private long link;
        private int pendingCount;

        public OutpostMembership Membership { get; private set; } = OutpostMembership.NotJoined;
        public event Action<OutpostSessionSnapshot> SnapshotChanged;
        public event Action<ExpeditionSessionSnapshot> ExpeditionChanged;
        public event Action<CommandResult> CommandCompleted;
        public event Action<OutpostCommand, string> CommandAbandoned;
        public event Action<ExpeditionCommand, string> ExpeditionCommandAbandoned;
        public event Action<OutpostMembership> MembershipChanged;
        public event Action<string> ProtocolError;

        internal Action Joined;
        internal Action<string> Rejected;

        public int PendingCommandCount => pendingCount;
        public long DiscardedSnapshotCount { get; private set; }
        public long DiscardedResultCount { get; private set; }
        public long DroppedMovementCount { get; private set; }

        internal ExpeditionNetworkSessionAdapter(ISessionClientTransport transport)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public bool TryGetSnapshot(out OutpostSessionSnapshot value)
        {
            value = snapshot?.Outpost;
            return value != null;
        }

        public bool TryGetExpedition(out ExpeditionSessionSnapshot value)
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
            long zone = snapshot != null && snapshot.TryGetCrew(Membership.Player, out ExpeditionCrewState local)
                ? local.ZoneSequence : ExpeditionCommand.Unstated;
            Send(new Submission { Outpost = command, Expedition = ExpeditionCommand.FromOutpost(command, zone) });
        }

        public void Submit(ExpeditionCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!linkOpen || !Membership.IsJoined)
            {
                ExpeditionCommandAbandoned?.Invoke(command, OutpostReasons.NotConnected);
                return;
            }
            Send(new Submission { Expedition = command });
        }

        private void Send(Submission submission)
        {
            ExpeditionCommand command = submission.Expedition;
            byte[] payload = ExpeditionWireProtocol.EncodeSubmit(command);
            var key = (command.Id, command.Epoch);
            if (!pending.TryGetValue(key, out List<Submission> queue))
            {
                queue = new List<Submission>(1);
                pending.Add(key, queue);
            }
            queue.Add(submission); // registered before sending: the result may come back immediately
            pendingCount++;
            try
            {
                transport.SendToHost(link, payload);
            }
            catch
            {
                if (pending.TryGetValue(key, out List<Submission> current) && ReferenceEquals(current, queue) && queue.Remove(submission))
                {
                    pendingCount--;
                    if (queue.Count == 0) pending.Remove(key);
                    Abandon(submission, OutpostReasons.ConnectionLost); // unknown, not a server rejection
                }
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
            transport.SendToHost(link, ExpeditionWireProtocol.EncodeMovement(input));
        }

        internal void OpenLink(long id)
        {
            link = id;
            linkOpen = true;
            snapshot = null;
            awaitingSnapshotFor = PlayerId.None;
            assembler = ExpeditionWireProtocol.NewAssembler();
            if (Membership.Status != MembershipStatus.NotJoined) SetMembership(OutpostMembership.NotJoined);
        }

        internal void RequestJoin() => transport.SendToHost(link, ExpeditionWireProtocol.EncodeJoin());

        internal List<Submission> CloseLink(string reason, out bool membershipChanged)
        {
            linkOpen = false;
            awaitingSnapshotFor = PlayerId.None;
            assembler.Clear();
            membershipChanged = false;
            if (Membership.IsJoined)
            {
                Membership = OutpostMembership.Left(Membership.Player, string.IsNullOrWhiteSpace(reason) ? OutpostReasons.ConnectionLost : reason);
                membershipChanged = true;
            }
            var abandoned = new List<Submission>(pendingCount);
            foreach (List<Submission> queue in pending.Values) abandoned.AddRange(queue);
            pending.Clear();
            pendingCount = 0;
            return abandoned;
        }

        internal void NotifyClosed(List<Submission> abandoned, string abandonReason, bool membershipChanged)
        {
            try
            {
                if (membershipChanged) MembershipChanged?.Invoke(Membership);
            }
            finally
            {
                foreach (Submission submission in abandoned) Abandon(submission, abandonReason);
            }
        }

        private void Abandon(Submission submission, string reason)
        {
            if (submission.Outpost != null) CommandAbandoned?.Invoke(submission.Outpost, reason);
            else ExpeditionCommandAbandoned?.Invoke(submission.Expedition, reason);
        }

        internal void Receive(byte[] frame)
        {
            if (!linkOpen) return;
            if (!assembler.TryAccept(frame, out byte[] payload, out string error))
            {
                if (error != null) ProtocolError?.Invoke(error);
                return;
            }
            if (!ExpeditionWireProtocol.TryDecodeFromHost(payload, out ExpeditionHostMessage message, out error))
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

        private void ApplySnapshot(ExpeditionSessionSnapshot next)
        {
            if (!next.IsNewerThan(snapshot))
            {
                DiscardedSnapshotCount++;
                return;
            }
            snapshot = next;
            SnapshotChanged?.Invoke(next.Outpost);
            ExpeditionChanged?.Invoke(next);
            if (awaitingSnapshotFor.IsValid && linkOpen && next.TryGetCrew(awaitingSnapshotFor, out _)) CompleteJoin(awaitingSnapshotFor);
        }

        private void ApplyMembership(OutpostMembership membership)
        {
            switch (membership.Status)
            {
                case MembershipStatus.Joined:
                    if (Membership.IsJoined) return;
                    if (snapshot != null && snapshot.TryGetCrew(membership.Player, out _)) CompleteJoin(membership.Player);
                    else awaitingSnapshotFor = membership.Player;
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
            if (!pending.TryGetValue(key, out List<Submission> queue))
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

    /// <summary>Phase 3 client connection lifecycle, identical in rules to the phase 2 controller (Connecting ->
    /// Synchronizing -> Connected; SessionFull or host loss -> ConnectionLost; Connect again = a fresh join with a new
    /// PlayerId; no automatic reconnection or host migration).</summary>
    public sealed class ExpeditionConnectionController : IDisposable
    {
        private readonly ISessionClientTransport transport;
        private long link;
        private bool disposed;

        public ExpeditionNetworkSessionAdapter Session { get; }
        public SessionConnectionState State { get; private set; } = SessionConnectionState.Disconnected;
        public string LastDisconnectReason { get; private set; }
        public event Action<SessionConnectionState> StateChanged;

        public ExpeditionConnectionController(ISessionClientTransport transport)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Session = new ExpeditionNetworkSessionAdapter(transport) { Joined = OnJoined, Rejected = OnRejected };
            transport.Connected += OnConnected;
            transport.Disconnected += OnDisconnected;
            transport.MessageReceived += OnMessage;
        }

        public void Connect()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ExpeditionConnectionController));
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

        public void Disconnect()
        {
            if (State == SessionConnectionState.Disconnected) return;
            bool wasLost = State == SessionConnectionState.ConnectionLost;
            if (!wasLost) LastDisconnectReason = SessionNetworkCodes.LocalDisconnect;
            State = SessionConnectionState.Disconnected;
            var abandoned = Session.CloseLink(OutpostReasons.LocalDisconnect, out bool membershipChanged);
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
                transport.Disconnect(id);
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
            var abandoned = Session.CloseLink(reason, out bool membershipChanged);
            Session.NotifyClosed(abandoned, OutpostReasons.ConnectionLost, membershipChanged);
            StateChanged?.Invoke(SessionConnectionState.ConnectionLost);
        }

        private void SetState(SessionConnectionState next)
        {
            State = next;
            StateChanged?.Invoke(next);
        }
    }
}
