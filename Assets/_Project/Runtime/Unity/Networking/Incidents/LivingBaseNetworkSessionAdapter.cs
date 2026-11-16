using System;
using System.Collections.Generic;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Application.Incidents;
using Trainer.Unity.Networking.Outpost;

namespace Trainer.Unity.Networking.Incidents
{
    /// <summary>
    /// Client-side <see cref="ILivingBaseSessionPort"/> backed by the remote phase 5 authority (D3), 'T' 'L' cable. Same rules as
    /// phase 4: it sends intentions and publishes what the authority confirms; it never simulates, advances a clock, keeps an
    /// inventory or credit balance, runs a director or invents a result. Membership becomes Joined only once a snapshot containing
    /// the assigned player is stored; older snapshots are discarded; per stored snapshot the events are phase 2 → 3 → 4 → 5. Results
    /// are matched by (id, epoch). A failed send abandons that request as ConnectionLost (result unknown). On link loss the
    /// membership becomes Left and pending requests are abandoned; the last snapshot stays only as last known state.
    /// </summary>
    public sealed class LivingBaseNetworkSessionAdapter : ILivingBaseSessionPort
    {
        internal sealed class Submission
        {
            public OutpostCommand Outpost;
            public ExpeditionCommand Expedition;
            public BaseOperationCommand Operation;
            public IncidentCommand Incident;
        }

        private readonly ISessionClientTransport transport;
        private readonly Dictionary<(Guid, long), List<Submission>> pending = new Dictionary<(Guid, long), List<Submission>>();
        private LivingBaseWireProtocol.Assembler assembler = new LivingBaseWireProtocol.Assembler();
        private LivingBaseSnapshot snapshot;
        private PlayerId awaitingSnapshotFor = PlayerId.None;
        private bool linkOpen;
        private long link;
        private int pendingCount;

        public OutpostMembership Membership { get; private set; } = OutpostMembership.NotJoined;
        public event Action<OutpostSessionSnapshot> SnapshotChanged;
        public event Action<ExpeditionSessionSnapshot> ExpeditionChanged;
        public event Action<BaseOperationsSnapshot> BaseOperationsChanged;
        public event Action<LivingBaseSnapshot> LivingBaseChanged;
        public event Action<CommandResult> CommandCompleted;
        public event Action<OutpostCommand, string> CommandAbandoned;
        public event Action<ExpeditionCommand, string> ExpeditionCommandAbandoned;
        public event Action<BaseOperationCommand, string> BaseOperationCommandAbandoned;
        public event Action<IncidentCommand, string> IncidentCommandAbandoned;
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

        internal LivingBaseNetworkSessionAdapter(ISessionClientTransport transport)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public bool TryGetSnapshot(out OutpostSessionSnapshot value)
        {
            value = snapshot?.BaseOperations.Outpost;
            return value != null;
        }

        public bool TryGetExpedition(out ExpeditionSessionSnapshot value)
        {
            value = snapshot?.BaseOperations.Expedition;
            return value != null;
        }

        public bool TryGetBaseOperations(out BaseOperationsSnapshot value)
        {
            value = snapshot?.BaseOperations;
            return value != null;
        }

        public bool TryGetLivingBase(out LivingBaseSnapshot value)
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
            long zone = snapshot != null && snapshot.BaseOperations.Expedition.TryGetCrew(Membership.Player, out ExpeditionCrewState local)
                ? local.ZoneSequence : ExpeditionCommand.Unstated;
            ExpeditionCommand converted = ExpeditionCommand.FromOutpost(command, zone);
            Send(new Submission { Outpost = command, Expedition = converted }, converted.Id, converted.Epoch,
                LivingBaseWireProtocol.EncodeSubmit(converted));
        }

        public void Submit(ExpeditionCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!linkOpen || !Membership.IsJoined)
            {
                ExpeditionCommandAbandoned?.Invoke(command, OutpostReasons.NotConnected);
                return;
            }
            Send(new Submission { Expedition = command }, command.Id, command.Epoch, LivingBaseWireProtocol.EncodeSubmit(command));
        }

        /// <summary>Null, or a manifest key that is not a stable key (the cable cannot carry it), throws ArgumentException.</summary>
        public void Submit(BaseOperationCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!LivingBaseWireProtocol.CanCarry(command))
                throw new ArgumentException("A manifest key must be null or a stable key.", nameof(command));
            if (!linkOpen || !Membership.IsJoined)
            {
                BaseOperationCommandAbandoned?.Invoke(command, OutpostReasons.NotConnected);
                return;
            }
            Send(new Submission { Operation = command }, command.Id, command.Epoch, LivingBaseWireProtocol.EncodeOperation(command));
        }

        /// <summary>Null, or a station that is not a stable key (the cable cannot carry it), throws.</summary>
        public void Submit(IncidentCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (command.Station.IsValid && !StableKey.IsValid(command.Station.Value))
                throw new ArgumentException("A station must be a stable key.", nameof(command));
            if (!linkOpen || !Membership.IsJoined)
            {
                IncidentCommandAbandoned?.Invoke(command, OutpostReasons.NotConnected);
                return;
            }
            Send(new Submission { Incident = command }, command.Id, command.Epoch, LivingBaseWireProtocol.EncodeIncident(command));
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
                transport.SendToHost(link, LivingBaseWireProtocol.EncodeMovement(input));
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
            assembler = new LivingBaseWireProtocol.Assembler();
            if (Membership.Status != MembershipStatus.NotJoined) SetMembership(OutpostMembership.NotJoined);
        }

        internal void RequestJoin() => transport.SendToHost(link, LivingBaseWireProtocol.EncodeJoin());

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
            if (submission.Incident != null) IncidentCommandAbandoned?.Invoke(submission.Incident, reason);
            else if (submission.Operation != null) BaseOperationCommandAbandoned?.Invoke(submission.Operation, reason);
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
            if (!LivingBaseWireProtocol.TryDecodeFromHost(payload, out LivingBaseHostMessage message, out error))
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

        private void ApplySnapshot(LivingBaseSnapshot next)
        {
            if (snapshot != null && !next.BaseOperations.IsNewerThan(snapshot.BaseOperations))
            {
                DiscardedSnapshotCount++;
                return;
            }
            snapshot = next;
            SnapshotChanged?.Invoke(next.BaseOperations.Outpost);
            ExpeditionChanged?.Invoke(next.BaseOperations.Expedition);
            BaseOperationsChanged?.Invoke(next.BaseOperations);
            LivingBaseChanged?.Invoke(next);
            if (awaitingSnapshotFor.IsValid && linkOpen && next.BaseOperations.Outpost.TryGetCrew(awaitingSnapshotFor, out _)) CompleteJoin(awaitingSnapshotFor);
        }

        private void ApplyMembership(OutpostMembership membership)
        {
            switch (membership.Status)
            {
                case MembershipStatus.Joined:
                    if (Membership.IsJoined) return;
                    if (snapshot != null && snapshot.BaseOperations.Outpost.TryGetCrew(membership.Player, out _)) CompleteJoin(membership.Player);
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

    /// <summary>Phase 5 client connection lifecycle, with the phase 2–3 rules: Connecting → Synchronizing → Connected;
    /// SessionFull, a failed send or host loss → ConnectionLost; Connect again = a FRESH join with a new PlayerId (no resumed
    /// identity, operation or reservation); no automatic reconnection or host migration.</summary>
    public sealed class LivingBaseConnectionController : IDisposable
    {
        private readonly ISessionClientTransport transport;
        private long link;
        private bool disposed;

        public LivingBaseNetworkSessionAdapter Session { get; }
        public SessionConnectionState State { get; private set; } = SessionConnectionState.Disconnected;
        public string LastDisconnectReason { get; private set; }
        public event Action<SessionConnectionState> StateChanged;

        public LivingBaseConnectionController(ISessionClientTransport transport)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Session = new LivingBaseNetworkSessionAdapter(transport) { Joined = OnJoined, Rejected = OnRejected, LinkFailed = OnLinkFailed };
            transport.Connected += OnConnected;
            transport.Disconnected += OnDisconnected;
            transport.MessageReceived += OnMessage;
        }

        public void Connect()
        {
            if (disposed) throw new ObjectDisposedException(nameof(LivingBaseConnectionController));
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
