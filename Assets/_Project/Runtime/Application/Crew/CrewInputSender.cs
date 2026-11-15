using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Trainer.Application.Outpost;

namespace Trainer.Application.Crew
{
    /// <summary>
    /// Client side of movement intent (plain C#). Turns raw axes into <see cref="MovementInput"/> for the local
    /// player only, numbering sequences per PlayerId and sending only when the effective direction changes, the
    /// epoch changes (Restart clears the authority's held direction) or the identity changes (rejoin: a new
    /// PlayerId starts again at sequence 1). It never sends a position and never predicts.
    /// While any blocker is registered (for example a focused task panel) the effective direction is zero, so
    /// the astronaut stops once and stays still until the last blocker is removed.
    /// </summary>
    public sealed class CrewInputSender
    {
        private sealed class SequenceHistory
        {
            public PlayerId Player;
            public long Issued;
        }
        // A binding may be replaced before its inputs are acknowledged. Keep the high-water mark with
        // the port, weakly held, so another session with the same numeric PlayerId cannot inherit it.
        private static readonly ConditionalWeakTable<IOutpostSessionPort, SequenceHistory> histories =
            new ConditionalWeakTable<IOutpostSessionPort, SequenceHistory>();
        private readonly IOutpostSessionPort port;
        private readonly SequenceHistory history;
        private readonly HashSet<object> blockers = new HashSet<object>();
        private PlayerId identity = PlayerId.None;
        private long sequence;
        private long sentEpoch = -1;
        private MoveDirection sent = MoveDirection.Zero;

        public CrewInputSender(IOutpostSessionPort port)
        {
            this.port = port ?? throw new ArgumentNullException(nameof(port));
            history = histories.GetValue(port, _ => new SequenceHistory());
        }

        public bool IsBlocked => blockers.Count > 0;
        /// <summary>Last direction sent for the current identity (Zero before any).</summary>
        public MoveDirection LastSent => sent;
        public long LastSequence => sequence;

        /// <summary>Registers <paramref name="owner"/> as blocking movement. Idempotent per owner.</summary>
        public void Block(object owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            blockers.Add(owner);
        }

        /// <summary>Removes <paramref name="owner"/>; movement resumes when no blocker remains.</summary>
        public void Unblock(object owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            blockers.Remove(owner);
        }

        /// <summary>Removes every blocker (for example when the presentation is torn down).</summary>
        public void ClearBlockers() => blockers.Clear();

        /// <summary>
        /// Call once per frame with raw axes (keyboard: -1, 0 or 1 per axis). Returns true when an input was sent.
        /// Nothing is sent while not joined or before the authority's state is available.
        /// </summary>
        public bool Update(double rawX, double rawY)
        {
            MoveDirection wanted = IsBlocked ? MoveDirection.Zero : MoveDirection.FromAxes(rawX, rawY);
            OutpostMembership membership = port.Membership;
            if (!membership.IsJoined || !port.TryGetSnapshot(out OutpostSessionSnapshot snapshot))
            {
                if (!membership.IsJoined) Forget();
                return false;
            }
            if (membership.Player != identity)
            {
                Forget();
                identity = membership.Player;
            }
            if (!snapshot.TryGetCrew(identity, out CrewMemberState local)) return false;
            if (history.Player != identity)
            {
                history.Player = identity;
                history.Issued = 0;
            }
            sequence = Math.Max(sequence, Math.Max(local.LastInputSequence, history.Issued));
            bool epochChanged = snapshot.Epoch != sentEpoch;
            // A fresh binding with prior inputs must be able to stop an unknown held direction. Restart
            // clears direction but preserves sequences; a new identity with no inputs already holds Zero.
            bool needed = epochChanged ? !wanted.IsZero || (sentEpoch < 0 && sequence > 0) : !wanted.Equals(sent);
            if (!needed)
            {
                if (epochChanged)
                {
                    sentEpoch = snapshot.Epoch;
                    sent = MoveDirection.Zero;
                }
                return false;
            }
            sequence = checked(sequence + 1);
            history.Issued = sequence; // Reserve even if delivery throws after the input was sent.
            port.SubmitMovement(new MovementInput(snapshot.Epoch, sequence, wanted));
            sentEpoch = snapshot.Epoch;
            sent = wanted;
            return true;
        }

        private void Forget()
        {
            identity = PlayerId.None;
            sequence = 0;
            sentEpoch = -1;
            sent = MoveDirection.Zero;
        }
    }
}
