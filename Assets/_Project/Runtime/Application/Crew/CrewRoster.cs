using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Application.Expedition;
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
    ///
    /// Phase 3 (<see cref="IZonedCrewRoster"/>, optional): an obstacle overlay (closed hatches) is added to the layout
    /// for collision, and a per-member speed factor (sealed suit) scales the walking speed. Without those calls the
    /// behaviour is exactly the phase 2 one. Still the only mover and the only collision authority.
    /// </summary>
    public sealed class CrewRoster : IZonedCrewRoster
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
            public double Speed = 1d;
        }

        private static readonly IReadOnlyList<CrewMemberState> Empty =
            new ReadOnlyCollection<CrewMemberState>(Array.Empty<CrewMemberState>());
        private static readonly IReadOnlyList<BaseRect> NoOverlay = new ReadOnlyCollection<BaseRect>(Array.Empty<BaseRect>());

        private readonly OutpostLayout layout;
        private readonly CrewMovementSettings movement;
        private readonly List<Member> members = new List<Member>();
        private OutpostLayout collision;
        private int lastId;

        public IReadOnlyList<CrewMemberState> Members { get; private set; } = Empty;
        public OutpostLayout Layout => layout;
        public CrewMovementSettings Movement => movement;
        public IReadOnlyList<BaseRect> ObstacleOverlay { get; private set; } = NoOverlay;

        public CrewRoster(OutpostLayout layout, CrewMovementSettings movement)
        {
            this.layout = layout ?? throw new ArgumentNullException(nameof(layout));
            this.movement = movement ?? throw new ArgumentNullException(nameof(movement));
            foreach (BasePosition spawn in layout.SpawnPoints)
                if (!layout.IsAdmissible(spawn, movement.CrewRadiusMeters))
                    throw new ArgumentException("Every spawn point must be admissible for the crew radius.", nameof(layout));
            collision = layout;
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
                    double distance = member.Held.Magnitude * movement.MaxSpeedMetersPerSecond * member.Speed * elapsedSeconds;
                    member.Position = CrewMotionSolver.Move(collision, movement.CrewRadiusMeters, before, member.Held, distance);
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
                member.Speed = 1d;
            }
            Publish();
        }

        public bool IsClear(BaseRect area)
        {
            foreach (Member member in members)
                if (area.DistanceTo(member.Position) < movement.CrewRadiusMeters) return false;
            return true;
        }

        public void SetObstacleOverlay(IEnumerable<BaseRect> rectangles)
        {
            if (rectangles == null) throw new ArgumentNullException(nameof(rectangles));
            var extra = new List<BaseRect>(rectangles);
            OutpostLayout next = layout;
            if (extra.Count > 0)
            {
                var obstacles = new List<BaseRect>(layout.Obstacles);
                obstacles.AddRange(extra);
                next = new OutpostLayout(layout.Bounds, obstacles, layout.Stations, layout.SpawnPoints);
            }
            // Never cut anyone in two: every present disc must stay admissible with the new obstacles.
            foreach (Member member in members)
                if (!next.IsAdmissible(member.Position, movement.CrewRadiusMeters))
                    throw new InvalidOperationException($"{member.Id} at {member.Position} would overlap a new obstacle.");
            collision = next;
            ObstacleOverlay = new ReadOnlyCollection<BaseRect>(extra.ToArray());
        }

        public bool SetSpeedFactor(PlayerId player, double factor)
        {
            if (double.IsNaN(factor) || factor <= 0d || factor > 1d) throw new ArgumentOutOfRangeException(nameof(factor), "In (0, 1].");
            Member member = members.Find(m => m.Id == player);
            if (member == null) return false;
            member.Speed = factor;
            return true;
        }

        public double SpeedFactorOf(PlayerId player) => members.Find(m => m.Id == player)?.Speed ?? 1d;

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
