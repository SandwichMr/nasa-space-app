using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Application.Outpost;

namespace Trainer.Application.Crew
{
    /// <summary>
    /// D3 implementation of <see cref="ICrewRoster"/>: identities, presence and confirmed movement inside the
    /// authority. The mission authority (D2) owns the single instance and is its only caller, on one thread.
    ///
    /// - Ids start at 1, increase and are never reused by this instance; the lowest free slot picks the spawn.
    /// - Movement inputs follow <see cref="MovementInputPolicy"/>; only the held direction and the last applied
    ///   sequence are kept per present member (nothing per frame).
    /// - <see cref="Advance"/> walks each member at Magnitude * MaxSpeed through <see cref="CrewMotionSolver"/>.
    /// - <see cref="Members"/> is replaced by a new immutable list exactly when something visible changed
    ///   (reference comparison tells the authority whether to publish a new revision).
    /// Crew discs do not block each other.
    /// </summary>
    public sealed class CrewRoster : ICrewRoster
    {
        private sealed class Member
        {
            public PlayerId Id;
            public int Slot;
            public BasePosition Position;
            public MoveDirection Held;
            public MoveDirection Facing;
            public CrewMotion Motion;
            public long LastSequence;
        }

        private static readonly IReadOnlyList<CrewMemberState> Empty =
            new ReadOnlyCollection<CrewMemberState>(Array.Empty<CrewMemberState>());

        private readonly OutpostLayout layout;
        private readonly CrewMovementSettings movement;
        private readonly List<Member> members = new List<Member>();
        private int lastId;

        public IReadOnlyList<CrewMemberState> Members { get; private set; } = Empty;
        public OutpostLayout Layout => layout;
        public CrewMovementSettings Movement => movement;

        public CrewRoster(OutpostLayout layout, CrewMovementSettings movement)
        {
            this.layout = layout ?? throw new ArgumentNullException(nameof(layout));
            this.movement = movement ?? throw new ArgumentNullException(nameof(movement));
            foreach (BasePosition spawn in layout.SpawnPoints)
                if (!layout.IsAdmissible(spawn, movement.CrewRadiusMeters))
                    throw new ArgumentException("Every spawn point must be admissible for the crew radius.", nameof(layout));
        }

        public static CrewRoster For(OutpostScenario scenario)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            return new CrewRoster(scenario.Layout, scenario.Movement);
        }

        public OutpostMembership Join()
        {
            if (members.Count >= layout.MaxCrew) return OutpostMembership.Rejected(OutpostReasons.SessionFull);
            int slot = 0;
            while (members.Exists(m => m.Slot == slot)) slot++;
            var member = new Member
            {
                Id = new PlayerId(checked(++lastId)),
                Slot = slot,
                Position = layout.SpawnPoints[slot],
                Held = MoveDirection.Zero,
                Facing = CrewPose.DefaultFacing,
                Motion = CrewMotion.Idle
            };
            members.Add(member); // Ids increase, so the list stays in PlayerId order.
            Publish();
            return OutpostMembership.Joined(member.Id);
        }

        public bool Leave(PlayerId player)
        {
            int index = members.FindIndex(m => m.Id == player);
            if (index < 0) return false;
            members.RemoveAt(index);
            Publish();
            return true;
        }

        public MovementInputDisposition SubmitMovement(PlayerId player, MovementInput input, long currentEpoch)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            Member member = members.Find(m => m.Id == player);
            if (member == null) return MovementInputDisposition.NotJoined;
            MovementInputDisposition disposition = MovementInputPolicy.Evaluate(member.LastSequence, currentEpoch, input);
            if (disposition != MovementInputDisposition.Applied) return disposition;
            member.Held = input.Direction;
            member.LastSequence = input.Sequence;
            Publish(); // LastInputSequence is published: the client sees which input is in force.
            return disposition;
        }

        public void Advance(double elapsedSeconds)
        {
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (elapsedSeconds == 0d) return;
            bool changed = false;
            foreach (Member member in members)
            {
                BasePosition before = member.Position;
                CrewMotion motion = CrewMotion.Idle;
                MoveDirection facing = member.Facing;
                if (!member.Held.IsZero)
                {
                    double distance = member.Held.Magnitude * movement.MaxSpeedMetersPerSecond * elapsedSeconds;
                    member.Position = CrewMotionSolver.Move(layout, movement.CrewRadiusMeters, before, member.Held, distance);
                    facing = Unit(member.Held);
                    if (!member.Position.Equals(before)) motion = CrewMotion.Walking;
                }
                changed |= !member.Position.Equals(before) || motion != member.Motion || !facing.Equals(member.Facing);
                member.Motion = motion;
                member.Facing = facing;
            }
            if (changed) Publish();
        }

        public void ResetForRestart()
        {
            foreach (Member member in members)
            {
                member.Position = layout.SpawnPoints[member.Slot];
                member.Held = MoveDirection.Zero;
                member.Facing = CrewPose.DefaultFacing;
                member.Motion = CrewMotion.Idle;
            }
            Publish();
        }

        private static MoveDirection Unit(MoveDirection direction)
        {
            double magnitude = direction.Magnitude;
            return MoveDirection.FromAxes(direction.X / magnitude, direction.Y / magnitude);
        }

        private void Publish()
        {
            var states = new CrewMemberState[members.Count];
            for (int i = 0; i < members.Count; i++)
            {
                Member m = members[i];
                states[i] = new CrewMemberState(m.Id, m.Slot, new CrewPose(m.Position, m.Facing, m.Motion), m.LastSequence);
            }
            Members = new ReadOnlyCollection<CrewMemberState>(states);
        }
    }
}
