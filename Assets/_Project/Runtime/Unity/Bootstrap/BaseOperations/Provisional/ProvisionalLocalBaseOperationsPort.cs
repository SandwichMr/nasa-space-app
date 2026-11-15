using System;
using System.Collections.Generic;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Unity.Networking.Expedition;

namespace Trainer.Unity.Bootstrap.BaseOperations
{
    /// <summary>
    /// PROVISIONAL (D2, until D3's <c>LocalBaseOperationsSessionPort</c> is merged in <c>integration/phase-04</c>): the
    /// in-process port of the host's own player. Phase 2/3 traffic goes through the existing phase 3
    /// <see cref="LocalExpeditionSessionPort"/> over the composed phase 3 authority; phase 4 operations go to the
    /// <see cref="BaseOperationsAuthority"/>. Events per revision: SnapshotChanged, ExpeditionChanged, BaseOperationsChanged.
    /// No network, no fake: the state is the authority's.
    /// </summary>
    public sealed class ProvisionalLocalBaseOperationsPort : IBaseOperationsSessionPort, IDisposable
    {
        private readonly BaseOperationsAuthority authority;
        private readonly LocalExpeditionSessionPort phase3;
        private readonly HashSet<Guid> pending = new HashSet<Guid>();
        private readonly Dictionary<Guid, BaseOperationCommand> sent = new Dictionary<Guid, BaseOperationCommand>();
        private BaseOperationsSnapshot latest;
        private bool disposed;

        public event Action<OutpostSessionSnapshot> SnapshotChanged;
        public event Action<ExpeditionSessionSnapshot> ExpeditionChanged;
        public event Action<BaseOperationsSnapshot> BaseOperationsChanged;
        public event Action<CommandResult> CommandCompleted;
        public event Action<OutpostCommand, string> CommandAbandoned;
        public event Action<ExpeditionCommand, string> ExpeditionCommandAbandoned;
        public event Action<BaseOperationCommand, string> BaseOperationCommandAbandoned;
        public event Action<OutpostMembership> MembershipChanged;

        public ProvisionalLocalBaseOperationsPort(BaseOperationsAuthority authority)
        {
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
            phase3 = new LocalExpeditionSessionPort(authority.Expedition);
            phase3.SnapshotChanged += s => SnapshotChanged?.Invoke(s);
            phase3.ExpeditionChanged += OnExpedition;
            phase3.CommandCompleted += r => CommandCompleted?.Invoke(r);
            phase3.CommandAbandoned += (c, r) => CommandAbandoned?.Invoke(c, r);
            phase3.ExpeditionCommandAbandoned += (c, r) => ExpeditionCommandAbandoned?.Invoke(c, r);
            phase3.MembershipChanged += OnMembership;
            authority.CommandCompleted += OnAuthorityResult;
            authority.TryGetSnapshot(out latest);
        }

        public OutpostMembership Membership => phase3.Membership;

        public OutpostMembership Join() => phase3.Join();

        public void Leave(string reason = OutpostReasons.LocalDisconnect) => phase3.Leave(reason);

        public bool TryGetSnapshot(out OutpostSessionSnapshot snapshot) => phase3.TryGetSnapshot(out snapshot);

        public bool TryGetExpedition(out ExpeditionSessionSnapshot snapshot) => phase3.TryGetExpedition(out snapshot);

        public bool TryGetBaseOperations(out BaseOperationsSnapshot snapshot)
        {
            snapshot = latest;
            return latest != null;
        }

        public void Submit(OutpostCommand command) => phase3.Submit(command);

        public void Submit(ExpeditionCommand command) => phase3.Submit(command);

        public void SubmitMovement(MovementInput input) => phase3.SubmitMovement(input);

        public void Submit(BaseOperationCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (disposed || !Membership.IsJoined)
            {
                BaseOperationCommandAbandoned?.Invoke(command, OutpostReasons.NotConnected);
                return;
            }
            pending.Add(command.Id);
            sent[command.Id] = command;
            authority.Submit(Membership.Player, command);
        }

        private void OnExpedition(ExpeditionSessionSnapshot snapshot)
        {
            ExpeditionChanged?.Invoke(snapshot);
            if (authority.TryGetSnapshot(out BaseOperationsSnapshot current) && ReferenceEquals(current.Expedition, snapshot)
                && current.IsNewerThan(latest))
            {
                latest = current;
                BaseOperationsChanged?.Invoke(current);
            }
        }

        private void OnAuthorityResult(PlayerId sender, CommandResult result)
        {
            if (!Membership.IsJoined || sender != Membership.Player || !pending.Remove(result.CommandId)) return;
            sent.Remove(result.CommandId);
            CommandCompleted?.Invoke(result);
        }

        private void OnMembership(OutpostMembership membership)
        {
            if (!membership.IsJoined) AbandonPending(membership.Reason);
            MembershipChanged?.Invoke(membership);
        }

        private void AbandonPending(string reason)
        {
            var lost = new List<BaseOperationCommand>(sent.Values);
            pending.Clear();
            sent.Clear();
            foreach (BaseOperationCommand command in lost)
                BaseOperationCommandAbandoned?.Invoke(command, string.IsNullOrEmpty(reason) ? OutpostReasons.LocalDisconnect : reason);
        }

        public void Dispose()
        {
            if (disposed) return;
            authority.CommandCompleted -= OnAuthorityResult;
            phase3.Dispose();
            AbandonPending(OutpostReasons.LocalDisconnect);
            disposed = true;
        }
    }
}
