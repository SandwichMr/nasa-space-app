using System;
using System.Collections.Generic;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Unity.Networking.Outpost;

namespace Trainer.Unity.Networking.BaseOperations
{
    /// <summary>
    /// Client-side <see cref="IBaseOperationsSessionPort"/> backed by the remote phase 4 authority. It sends intentions and
    /// publishes what the authority confirms; it never simulates, advances a clock, keeps an inventory, moves anyone,
    /// reserves a spare or invents a result, so no client can diverge from the authority.
    /// <list type="bullet">
    /// <item>Membership becomes Joined only once a snapshot containing the assigned player is stored.</item>
    /// <item>Snapshots not newer (epoch, revision) than the current one are discarded. For each stored one the events are
    /// SnapshotChanged (phase 2 part), ExpeditionChanged (phase 3 part), BaseOperationsChanged, in that order.</item>
    /// <item>A phase 2 command travels as <see cref="ExpeditionCommand.FromOutpost"/> stamped with the local zone sequence;
    /// abandonment goes to the event of the kind that was submitted. Every result arrives through CommandCompleted and is
    /// matched by (id, epoch): a late, duplicated or foreign result never completes another request.</item>
    /// <item>A failed send abandons that request as ConnectionLost (result unknown: it may or may not have left), reports
    /// SendFailed and ends the link; it never throws at the caller and never reports a success.</item>
    /// <item>On link loss the membership becomes Left, pending requests are abandoned (result unknown) and the last snapshot
    /// is kept only as last known state.</item>
    /// </list>
    /// </summary>
    public sealed class BaseOperationsNetworkSessionAdapter : IBaseOperationsSessionPort
    {
        internal sealed class Submission
        {
            public OutpostCommand Outpost;
            public ExpeditionCommand Expedition;
            public BaseOperationCommand Operation;
        }

        private readonly ISessionClientTransport transport;
        private readonly Dictionary<(Guid, long), List<Submission>> pending = new Dictionary<(Guid, long), List<Submission>>();
        private BaseOperationsWireProtocol.Assembler assembler = new BaseOperationsWireProtocol.Assembler();
        private BaseOperationsSnapshot snapshot;
        private PlayerId awaitingSnapshotFor = PlayerId.None;
        private bool linkOpen;
        private long link;
        private int pendingCount;

        public OutpostMembership Membership { get; private set; } = OutpostMembership.NotJoined;
        public event Action<OutpostSessionSnapshot> SnapshotChanged;
        public event Action<ExpeditionSessionSnapshot> ExpeditionChanged;
        public event Action<BaseOperationsSnapshot> BaseOperationsChanged;
        public event Action<CommandResult> CommandCompleted;
        public event Action<OutpostCommand, string> CommandAbandoned;
        public event Action<ExpeditionCommand, string> ExpeditionCommandAbandoned;
        public event Action<BaseOperationCommand, string> BaseOperationCommandAbandoned;
        public event Action<OutpostMembership> MembershipChanged;
        public event Action<string> ProtocolError;

        internal Action Joined;
        internal Action<string> Rejected;
        internal Action<string> LinkFailed;

        public int PendingCommandCount => pendingCount;
        public long DiscardedSnapshotCount { get; private set; }
        public long DiscardedResultCount { get; private set; }
        public long DroppedMovementCount { get; private set; }
        public long FailedSendCount { get; private set; }

        internal BaseOperationsNetworkSessionAdapter(ISessionClientTransport transport)
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
            value = snapshot?.Expedition;
            return value != null;
        }

        public bool TryGetBaseOperations(out BaseOperationsSnapshot value)
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
            long zone = snapshot != null && snapshot.Expedition.TryGetCrew(Membership.Player, out ExpeditionCrewState local)
                ? local.ZoneSequence : ExpeditionCommand.Unstated;
            ExpeditionCommand converted = ExpeditionCommand.FromOutpost(command, zone);
            Send(new Submission { Outpost = command, Expedition = converted }, converted.Id, converted.Epoch,
                BaseOperationsWireProtocol.EncodeSubmit(converted));
        }

        public void Submit(ExpeditionCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!linkOpen || !Membership.IsJoined)
            {
                ExpeditionCommandAbandoned?.Invoke(command, OutpostReasons.NotConnected);
                return;
            }
            Send(new Submission { Expedition = command }, command.Id, command.Epoch, BaseOperationsWireProtocol.EncodeSubmit(command));
        }

        /// <summary>Null, or a manifest key that is not a stable key (the cable cannot carry it), throws ArgumentException.</summary>
        public void Submit(BaseOperationCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!BaseOperationsWireProtocol.CanCarry(command))
                throw new ArgumentException("A manifest key must be null or a stable key.", nameof(command));
            if (!linkOpen || !Membership.IsJoined)
            {
                BaseOperationCommandAbandoned?.Invoke(command, OutpostReasons.NotConnected);
                return;
            }
            Send(new Submission { Operation = command }, command.Id, command.Epoch, BaseOperationsWireProtocol.EncodeOperation(command));
        }

        private void Send(Submission submission, Guid id, long epoch, byte[] payload)
        {
            var key = (id, epoch);
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
            catch (Exception)
            {
                FailedSendCount++;
                if (pending.TryGetValue(key, out List<Submission> current) && ReferenceEquals(current, queue) && queue.Remove(submission))
                {
                    pendingCount--;
                    if (queue.Count == 0) pending.Remove(key);
                    Abandon(submission, OutpostReasons.ConnectionLost); // unknown, never a success or a server rejection
                }
                ProtocolError?.Invoke(SessionNetworkCodes.SendFailed);
                LinkFailed?.Invoke(SessionNetworkCodes.SendFailed); // a reliable link that refuses a send is unusable
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
            try
            {
                transport.SendToHost(link, BaseOperationsWireProtocol.EncodeMovement(input));
            }
            catch (Exception)
            {
                DroppedMovementCount++;
                FailedSendCount++;
                ProtocolError?.Invoke(SessionNetworkCodes.SendFailed);
                LinkFailed?.Invoke(SessionNetworkCodes.SendFailed);
            }
        }

        internal void OpenLink(long id)
        {
            link = id;
            linkOpen = true;
            snapshot = null;
            awaitingSnapshotFor = PlayerId.None;
            assembler = new BaseOperationsWireProtocol.Assembler();
            if (Membership.Status != MembershipStatus.NotJoined) SetMembership(OutpostMembership.NotJoined);
        }

        internal void RequestJoin() => transport.SendToHost(link, BaseOperationsWireProtocol.EncodeJoin());

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
            if (submission.Operation != null) BaseOperationCommandAbandoned?.Invoke(submission.Operation, reason);
            else if (submission.Outpost != null) CommandAbandoned?.Invoke(submission.Outpost, reason);
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
            if (!BaseOperationsWireProtocol.TryDecodeFromHost(payload, out BaseOperationsHostMessage message, out error))
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

        private void ApplySnapshot(BaseOperationsSnapshot next)
        {
            if (!next.IsNewerThan(snapshot))
            {
                DiscardedSnapshotCount++;
                return;
            }
            snapshot = next;
            SnapshotChanged?.Invoke(next.Outpost);
            ExpeditionChanged?.Invoke(next.Expedition);
            BaseOperationsChanged?.Invoke(next);
            if (awaitingSnapshotFor.IsValid && linkOpen && next.Outpost.TryGetCrew(awaitingSnapshotFor, out _)) CompleteJoin(awaitingSnapshotFor);
        }

        private void ApplyMembership(OutpostMembership membership)
        {
            switch (membership.Status)
            {
                case MembershipStatus.Joined:
                    if (Membership.IsJoined) return;
                    if (snapshot != null && snapshot.Outpost.TryGetCrew(membership.Player, out _)) CompleteJoin(membership.Player);
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

    /// <summary>Phase 4 client connection lifecycle, with the phase 2–3 rules: Connecting → Synchronizing → Connected;
    /// SessionFull, a failed send or host loss → ConnectionLost; Connect again = a FRESH join with a new PlayerId (no resumed
    /// identity, operation or reservation); no automatic reconnection or host migration.</summary>
    public sealed class BaseOperationsConnectionController : IDisposable
    {
        private readonly ISessionClientTransport transport;
        private long link;
        private bool disposed;

        public BaseOperationsNetworkSessionAdapter Session { get; }
        public SessionConnectionState State { get; private set; } = SessionConnectionState.Disconnected;
        public string LastDisconnectReason { get; private set; }
        public event Action<SessionConnectionState> StateChanged;

        public BaseOperationsConnectionController(ISessionClientTransport transport)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Session = new BaseOperationsNetworkSessionAdapter(transport) { Joined = OnJoined, Rejected = OnRejected, LinkFailed = OnLinkFailed };
            transport.Connected += OnConnected;
            transport.Disconnected += OnDisconnected;
            transport.MessageReceived += OnMessage;
        }

        public void Connect()
        {
            if (disposed) throw new ObjectDisposedException(nameof(BaseOperationsConnectionController));
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
                Session.LinkFailed = null;
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

        private void OnLinkFailed(string reason)
        {
            try
            {
                AbortLink(reason);
            }
            catch (Exception)
            {
                // The link is already gone locally; the transport's own disconnect event cannot resurrect it.
            }
        }

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
