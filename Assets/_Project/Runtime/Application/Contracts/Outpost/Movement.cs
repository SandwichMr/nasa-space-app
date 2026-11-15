using System;

namespace Trainer.Application.Outpost
{
    /// <summary>
    /// Held-direction intent of the sending client (immutable). It carries NO identity and NO position: the host
    /// adapter binds the sender and the authority computes the position. The direction holds until a newer
    /// applied input replaces it, the member leaves or the mission restarts. Movement inputs are not commands:
    /// they have no id, no result and are never stored beyond the last applied sequence per member.
    /// </summary>
    public sealed class MovementInput
    {
        /// <summary>Epoch the client observed; inputs of another epoch are discarded.</summary>
        public long Epoch { get; }
        /// <summary>1 or more, strictly increasing per PlayerId for the whole membership (it does not restart
        /// with the epoch; a rejoin gets a new PlayerId and may start again at 1).</summary>
        public long Sequence { get; }
        public MoveDirection Direction { get; }

        public MovementInput(long epoch, long sequence, MoveDirection direction)
        {
            if (epoch < 0) throw new ArgumentOutOfRangeException(nameof(epoch));
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence), "Sequences start at 1.");
            Epoch = epoch;
            Sequence = sequence;
            Direction = direction;
        }
    }

    public enum MovementInputDisposition
    {
        /// <summary>Became the member's held direction.</summary>
        Applied,
        /// <summary>Sequence not greater than the last applied one (old, duplicated or reordered); ignored.</summary>
        StaleSequence,
        /// <summary>Different epoch than the current one; ignored.</summary>
        EpochMismatch,
        /// <summary>The sender is not a present member; ignored.</summary>
        NotJoined
    }

    /// <summary>The single ordering rule for movement inputs. Every authority applies it; nothing else decides
    /// which input is current. Gaps are allowed: only the latest direction matters.</summary>
    public static class MovementInputPolicy
    {
        public static MovementInputDisposition Evaluate(long lastAppliedSequence, long currentEpoch, MovementInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.Epoch != currentEpoch) return MovementInputDisposition.EpochMismatch;
            if (input.Sequence <= lastAppliedSequence) return MovementInputDisposition.StaleSequence;
            return MovementInputDisposition.Applied;
        }
    }
}
