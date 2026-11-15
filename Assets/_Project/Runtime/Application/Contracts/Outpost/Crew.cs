using System;

namespace Trainer.Application.Outpost
{
    /// <summary>What D4 animates: standing still or walking (confirmed by the authority).</summary>
    public enum CrewMotion { Idle, Walking }

    /// <summary>
    /// Confirmed pose of one crew member (immutable). <see cref="Facing"/> is a unit vector: the direction of
    /// the last nonzero confirmed movement, or <see cref="DefaultFacing"/> (towards the bottom of the screen)
    /// before the first one and after Restart. Walking means the last authority step moved the member.
    /// </summary>
    public sealed class CrewPose
    {
        public static readonly MoveDirection DefaultFacing = new MoveDirection(0d, -1d);
        /// <summary>Accepted deviation of <see cref="Facing"/> from unit length.</summary>
        public const double FacingTolerance = 1e-6;

        public BasePosition Position { get; }
        public MoveDirection Facing { get; }
        public CrewMotion Motion { get; }

        public CrewPose(BasePosition position, MoveDirection facing, CrewMotion motion)
        {
            if (Math.Abs(facing.Magnitude - 1d) > FacingTolerance)
                throw new ArgumentException("Facing must be a unit vector.", nameof(facing));
            Position = position;
            Facing = facing;
            Motion = OutpostGuard.Defined(motion, nameof(motion));
        }

        public static CrewPose IdleAt(BasePosition position) => new CrewPose(position, DefaultFacing, CrewMotion.Idle);
    }

    /// <summary>One present crew member as published (immutable).</summary>
    public sealed class CrewMemberState
    {
        public PlayerId Player { get; }
        /// <summary>0 to MaxCrew-1, assigned by the authority at join (lowest free). Picks the spawn point and,
        /// in D4, the colour. Not an identity: a slot is reused after its member leaves.</summary>
        public int Slot { get; }
        public CrewPose Pose { get; }
        /// <summary>Sequence of the last movement input the authority applied for this member; 0 = none yet.</summary>
        public long LastInputSequence { get; }

        public CrewMemberState(PlayerId player, int slot, CrewPose pose, long lastInputSequence)
        {
            if (!player.IsValid) throw new ArgumentException("Player id required.", nameof(player));
            if (slot < 0 || slot >= OutpostLimits.MaxCrew) throw new ArgumentOutOfRangeException(nameof(slot));
            if (lastInputSequence < 0) throw new ArgumentOutOfRangeException(nameof(lastInputSequence));
            Player = player;
            Slot = slot;
            Pose = pose ?? throw new ArgumentNullException(nameof(pose));
            LastInputSequence = lastInputSequence;
        }
    }

    public enum MembershipStatus
    {
        /// <summary>No identity on this port yet (not connected or join not answered).</summary>
        NotJoined,
        /// <summary>The authority assigned <see cref="OutpostMembership.Player"/> and its state already arrived.</summary>
        Joined,
        /// <summary>The authority refused to join (for example SessionFull); see Reason.</summary>
        Rejected,
        /// <summary>The identity ended (left, link lost); see Reason. A rejoin gets a NEW PlayerId.</summary>
        Left
    }

    /// <summary>This port's own crew identity (immutable). The only way to tell the local astronaut from remote
    /// ones: compare <see cref="Player"/> with <see cref="CrewMemberState.Player"/>.</summary>
    public sealed class OutpostMembership
    {
        public static readonly OutpostMembership NotJoined = new OutpostMembership(MembershipStatus.NotJoined, PlayerId.None, string.Empty);

        public MembershipStatus Status { get; }
        /// <summary>Joined: the local player. Left: the identity that ended. Otherwise None.</summary>
        public PlayerId Player { get; }
        /// <summary>Empty for NotJoined/Joined; a reason code for Rejected/Left.</summary>
        public string Reason { get; }
        public bool IsJoined => Status == MembershipStatus.Joined;

        private OutpostMembership(MembershipStatus status, PlayerId player, string reason)
        {
            Status = status;
            Player = player;
            Reason = reason;
        }

        public static OutpostMembership Joined(PlayerId player)
        {
            if (!player.IsValid) throw new ArgumentException("Player id required.", nameof(player));
            return new OutpostMembership(MembershipStatus.Joined, player, string.Empty);
        }

        public static OutpostMembership Rejected(string reason) =>
            new OutpostMembership(MembershipStatus.Rejected, PlayerId.None, RequireReason(reason));

        public static OutpostMembership Left(PlayerId formerPlayer, string reason)
        {
            if (!formerPlayer.IsValid) throw new ArgumentException("Former player id required.", nameof(formerPlayer));
            return new OutpostMembership(MembershipStatus.Left, formerPlayer, RequireReason(reason));
        }

        private static string RequireReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reason required.", nameof(reason));
            return reason;
        }
    }
}
