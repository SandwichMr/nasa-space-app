using System;
using System.Collections.Generic;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Incidents;
using Trainer.Application.Outpost;

namespace Trainer.Application.Development
{
    /// <summary>
    /// DEVELOPMENT fake of <see cref="ILivingBaseSessionPort"/> for D4 previews and tests. It is NOT an authority: it does not
    /// validate, move, simulate, direct or decide; it only publishes fixture states, records submissions and completes them when
    /// the test says so. Phase 2–4 behaviour is the unchanged <see cref="FakeBaseOperationsSessionPort"/> it wraps. Never mounted
    /// in a productive scene.
    /// </summary>
    public sealed class FakeLivingBaseSessionPort : ILivingBaseSessionPort
    {
        public const string Label = "DESARROLLO · FakeLivingBaseSessionPort · sin red, sin simulación, sin autoridad";

        private readonly FakeBaseOperationsSessionPort inner = new FakeBaseOperationsSessionPort();
        private readonly List<IncidentCommand> pending = new List<IncidentCommand>();
        private LivingBaseSnapshot snapshot;

        public FakeLivingBaseSessionPort()
        {
            inner.SnapshotChanged += s => SnapshotChanged?.Invoke(s);
            inner.ExpeditionChanged += s => ExpeditionChanged?.Invoke(s);
            inner.BaseOperationsChanged += s => BaseOperationsChanged?.Invoke(s);
            inner.CommandCompleted += r => CommandCompleted?.Invoke(r);
            inner.CommandAbandoned += (c, r) => CommandAbandoned?.Invoke(c, r);
            inner.ExpeditionCommandAbandoned += (c, r) => ExpeditionCommandAbandoned?.Invoke(c, r);
            inner.BaseOperationCommandAbandoned += (c, r) => BaseOperationCommandAbandoned?.Invoke(c, r);
            inner.MembershipChanged += m => MembershipChanged?.Invoke(m);
        }

        /// <summary>The wrapped phase 4 fake (to complete phase 2–4 submissions in tests).</summary>
        public FakeBaseOperationsSessionPort BaseOperationsFake => inner;
        public IReadOnlyList<IncidentCommand> PendingIncidentCommands => pending.ToArray();

        public OutpostMembership Membership => inner.Membership;
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

        public bool TryGetSnapshot(out OutpostSessionSnapshot value) => inner.TryGetSnapshot(out value);
        public bool TryGetExpedition(out ExpeditionSessionSnapshot value) => inner.TryGetExpedition(out value);
        public bool TryGetBaseOperations(out BaseOperationsSnapshot value) => inner.TryGetBaseOperations(out value);

        public bool TryGetLivingBase(out LivingBaseSnapshot value)
        {
            value = snapshot;
            return value != null;
        }

        public void Submit(OutpostCommand command) => inner.Submit(command);
        public void Submit(ExpeditionCommand command) => inner.Submit(command);
        public void Submit(BaseOperationCommand command) => inner.Submit(command);
        public void SubmitMovement(MovementInput input) => inner.SubmitMovement(input);

        public void Submit(IncidentCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!Membership.IsJoined)
            {
                IncidentCommandAbandoned?.Invoke(command, OutpostReasons.NotConnected);
                return;
            }
            pending.Add(command);
        }

        /// <summary>Publishes a fixture state: phase 2, 3, 4 events (from the wrapped fake), then the phase 5 event.</summary>
        public void Publish(LivingBaseSnapshot next)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            inner.Publish(next.BaseOperations);
            snapshot = next;
            LivingBaseChanged?.Invoke(next);
        }

        public void Join(PlayerId player) => inner.Join(player);
        public void RejectJoin(string reason) => inner.RejectJoin(reason);

        /// <summary>Like a lost link: phase 5 pending commands are abandoned too.</summary>
        public void Disconnect(string reason)
        {
            IncidentCommand[] abandoned = pending.ToArray();
            pending.Clear();
            inner.Disconnect(reason);
            foreach (IncidentCommand command in abandoned) IncidentCommandAbandoned?.Invoke(command, reason);
        }

        public CommandResult Accept(Guid commandId) => Complete(commandId, true, OutpostReasons.Accepted);

        public CommandResult Reject(Guid commandId, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason) || reason == OutpostReasons.Accepted)
                throw new ArgumentException("A rejection needs a reason other than Accepted.", nameof(reason));
            return Complete(commandId, false, reason);
        }

        private CommandResult Complete(Guid commandId, bool accepted, string reason)
        {
            int index = pending.FindIndex(c => c.Id == commandId);
            if (index < 0) return accepted ? inner.Accept(commandId) : inner.Reject(commandId, reason);
            IncidentCommand command = pending[index];
            pending.RemoveAt(index);
            var result = new CommandResult(command.Id, command.Epoch, accepted, reason);
            CommandCompleted?.Invoke(result);
            return result;
        }
    }
}
