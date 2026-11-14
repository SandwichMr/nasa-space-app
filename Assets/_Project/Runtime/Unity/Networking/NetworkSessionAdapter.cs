using System;
using System.Collections.Generic;
using Trainer.Application;

namespace Trainer.Unity.Networking
{
    /// <summary>
    /// Client-side <see cref="ISessionPort"/> backed by a remote authority. It sends intentions and publishes
    /// what the authority returns; it never simulates, validates mission rules or invents results.
    /// Created and driven by <see cref="SessionConnectionController"/>. Single-threaded (Unity main thread).
    /// </summary>
    public sealed class NetworkSessionAdapter : ISessionPort
    {
        private readonly ISessionClientTransport transport;
        // Commands sent on the current link and still awaiting their authority result, keyed by ID and epoch,
        // in submission order.
        private readonly Dictionary<(Guid, long), List<SessionCommand>> pending =
            new Dictionary<(Guid, long), List<SessionCommand>>();
        private MissionSnapshot snapshot;
        private bool linkOpen;
        private long link;
        private int pendingCount;

        public event Action<MissionSnapshot> SnapshotChanged;
        public event Action<CommandResult> CommandCompleted;

        /// <summary>
        /// Adapter-specific, outside <see cref="ISessionPort"/>: a command that will never get a result on this
        /// link. <see cref="SessionNetworkCodes.NotConnected"/> means it was not sent. ConnectionLost or
        /// LocalDisconnect mean it may or may not have reached the authority; trust the next snapshot.
        /// </summary>
        public event Action<SessionCommand, string> CommandAbandoned;

        /// <summary>Adapter-specific: a host message was dropped (a <see cref="SessionNetworkCodes"/> value).</summary>
        public event Action<string> ProtocolError;

        internal Action SnapshotAccepted;

        public int PendingCommandCount => pendingCount;
        public long DiscardedSnapshotCount { get; private set; }
        public long DiscardedResultCount { get; private set; }

        internal NetworkSessionAdapter(ISessionClientTransport transport)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        /// <summary>False until the authority's state arrives on the current link. After a lost link it keeps
        /// returning the last authoritative snapshot; connection status is reported by the controller.</summary>
        public bool TryGetSnapshot(out MissionSnapshot value)
        {
            value = snapshot;
            return value != null;
        }

        /// <summary>Sends an intention. Acceptance arrives only through <see cref="CommandCompleted"/>.
        /// If the transport throws, the command is not left pending and the exception propagates.</summary>
        public void Submit(SessionCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!linkOpen)
            {
                CommandAbandoned?.Invoke(command, SessionNetworkCodes.NotConnected);
                return;
            }
            byte[] payload = SessionWireProtocol.EncodeSubmit(command);
            var key = (command.Id, command.Epoch);
            if (!pending.TryGetValue(key, out List<SessionCommand> queue))
            {
                queue = new List<SessionCommand>(1);
                pending.Add(key, queue);
            }
            // Registered before sending: a transport may deliver the result immediately.
            queue.Add(command);
            pendingCount++;
            try
            {
                transport.SendToHost(link, payload);
            }
            catch
            {
                int index = queue.LastIndexOf(command);
                if (index >= 0)
                {
                    queue.RemoveAt(index);
                    pendingCount--;
                    if (queue.Count == 0) pending.Remove(key);
                }
                throw;
            }
        }

        internal void OpenLink(long id)
        {
            // A new link is a new join: wait for the authority's initial state instead of reusing old data.
            link = id;
            linkOpen = true;
            snapshot = null;
        }

        internal void RequestInitialState() => transport.SendToHost(link, SessionWireProtocol.EncodeSyncRequest());

        /// <summary>Commits the closed link and returns the commands that lost their result.</summary>
        internal List<SessionCommand> CloseLink()
        {
            linkOpen = false;
            var abandoned = new List<SessionCommand>(pendingCount);
            foreach (List<SessionCommand> queue in pending.Values) abandoned.AddRange(queue);
            pending.Clear();
            pendingCount = 0;
            return abandoned;
        }

        internal void NotifyAbandoned(List<SessionCommand> abandoned, string reason)
        {
            foreach (SessionCommand command in abandoned) CommandAbandoned?.Invoke(command, reason);
        }

        internal void Receive(byte[] payload)
        {
            if (!linkOpen) return; // Late delivery from a closed link.
            if (!SessionWireProtocol.TryDecodeFromHost(payload, out HostMessage message, out string error))
            {
                ProtocolError?.Invoke(error);
                return;
            }
            if (message.Kind == SessionWireProtocol.SnapshotKind) ApplySnapshot(message.Snapshot);
            else ApplyResult(message.Result);
        }

        private void ApplySnapshot(MissionSnapshot next)
        {
            // Authority order: newer epoch, or same epoch with a newer revision. Older/equal are discarded.
            if (snapshot != null && !(next.Epoch > snapshot.Epoch
                || (next.Epoch == snapshot.Epoch && next.Revision > snapshot.Revision)))
            {
                DiscardedSnapshotCount++;
                return;
            }
            snapshot = next;
            SnapshotAccepted?.Invoke();
            SnapshotChanged?.Invoke(next);
        }

        private void ApplyResult(CommandResult result)
        {
            // One result per submission; unsolicited or duplicated results are discarded.
            var key = (result.CommandId, result.Epoch);
            if (!pending.TryGetValue(key, out List<SessionCommand> queue))
            {
                DiscardedResultCount++;
                return;
            }
            queue.RemoveAt(0);
            if (queue.Count == 0) pending.Remove(key);
            pendingCount--;
            CommandCompleted?.Invoke(result);
        }
    }
}
