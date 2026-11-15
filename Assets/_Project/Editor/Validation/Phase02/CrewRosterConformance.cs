using System;
using System.Collections.Generic;
using System.Linq;
using Trainer.Application.Outpost;
using static Trainer.Tooling.Validation.Phase02.Phase02Checks;

namespace Trainer.Tooling.Validation.Phase02
{
    /// <summary>
    /// Black-box conformance of an <see cref="ICrewRoster"/> (D3) on D5's plan (<see cref="Phase02Fixtures"/>):
    /// identity and slots, movement ordering, speed (straight, diagonal, analog), bounds, obstacles, tunnelling
    /// with large steps, per-step distance bound, facing/motion, leave and restart. Each check builds a fresh
    /// roster through <c>factory(layout, movement)</c>; nothing here moves anyone by itself.
    /// </summary>
    public static class CrewRosterConformance
    {
        private const string Kind = Phase02CheckKinds.RosterConformance;
        private const double Tol = 1e-6;

        public static List<Phase02Check> Run(Func<OutpostLayout, CrewMovementSettings, ICrewRoster> factory, string label)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            string p = label + ": ";
            OutpostLayout layout = Phase02Fixtures.Layout();
            CrewMovementSettings movement = Phase02Fixtures.Movement();
            double speed = movement.MaxSpeedMetersPerSecond, r = movement.CrewRadiusMeters;
            ICrewRoster New() => factory(layout, movement) ?? throw new Phase02CheckFailed("Factory returned null.");

            var checks = new List<Phase02Check>
            {
                Check(p + "join assigns new ids, lowest slots, spawn pose", Kind, () =>
                {
                    ICrewRoster roster = New();
                    OutpostMembership a = roster.Join(), b = roster.Join();
                    Require(a.IsJoined && b.IsJoined, "Two joins must succeed.");
                    Require(a.Player != b.Player, "Ids must differ.");
                    CrewMemberState ma = Member(roster, a.Player), mb = Member(roster, b.Player);
                    Require(ma.Slot == 0 && mb.Slot == 1, "Slots must be 0 and 1, got " + ma.Slot + ", " + mb.Slot + ".");
                    Require(ma.Pose.Position.Equals(layout.SpawnPoints[0]) && mb.Pose.Position.Equals(layout.SpawnPoints[1]), "Members must start at their spawn points.");
                    Require(ma.Pose.Motion == CrewMotion.Idle && ma.Pose.Facing.Equals(CrewPose.DefaultFacing), "Joined member must be idle with default facing.");
                    Require(roster.Members.Select(m => m.Player.Value).SequenceEqual(roster.Members.Select(m => m.Player.Value).OrderBy(v => v)), "Members must be in PlayerId order.");
                }),
                Check(p + "join beyond MaxCrew is rejected with SessionFull", Kind, () =>
                {
                    ICrewRoster roster = New();
                    roster.Join(); roster.Join();
                    OutpostMembership third = roster.Join();
                    Require(third.Status == MembershipStatus.Rejected && third.Reason == OutpostReasons.SessionFull, "Third join: " + third.Status + " " + third.Reason + ".");
                    Require(roster.Members.Count == 2, "A rejected join must not add a member.");
                }),
                Check(p + "ids never reused, freed slot reused, leave idempotent", Kind, () =>
                {
                    ICrewRoster roster = New();
                    PlayerId a = roster.Join().Player, b = roster.Join().Player;
                    Require(roster.Leave(a), "Leave of a present member must return true.");
                    Require(!roster.Leave(a), "Second leave must return false.");
                    Require(!roster.Leave(new PlayerId(999)), "Leave of an unknown id must return false.");
                    OutpostMembership c = roster.Join();
                    Require(c.IsJoined && c.Player != a && c.Player != b, "Rejoin must get a NEW id, got " + c.Player + ".");
                    Require(Member(roster, c.Player).Slot == 0, "Rejoin must take the lowest free slot (0).");
                    Require(roster.Members.Count == 2, "Exactly two members after leave + join.");
                }),
                Check(p + "movement ordering: stale, duplicate, epoch, not joined", Kind, () =>
                {
                    ICrewRoster roster = New();
                    PlayerId a = roster.Join().Player;
                    Require(roster.SubmitMovement(a, Input(0, 5, 1, 0), 0) == MovementInputDisposition.Applied, "Seq 5 must apply.");
                    Require(roster.SubmitMovement(a, Input(0, 5, -1, 0), 0) == MovementInputDisposition.StaleSequence, "Duplicate seq must be stale.");
                    Require(roster.SubmitMovement(a, Input(0, 3, -1, 0), 0) == MovementInputDisposition.StaleSequence, "Older seq must be stale.");
                    Require(roster.SubmitMovement(a, Input(1, 9, -1, 0), 0) == MovementInputDisposition.EpochMismatch, "Other epoch must mismatch.");
                    Require(roster.SubmitMovement(new PlayerId(77), Input(0, 1, 1, 0), 0) == MovementInputDisposition.NotJoined, "Unknown sender must be NotJoined.");
                    Require(Member(roster, a).Pose.Position.Equals(layout.SpawnPoints[0]), "Position must change only in Advance.");
                    roster.Advance(0.5);
                    Require(Member(roster, a).Pose.Position.X > layout.SpawnPoints[0].X, "Applied (1, 0) must move right; rejected inputs must not replace it.");
                    Require(Member(roster, a).LastInputSequence == 5, "LastInputSequence must be the last applied (5).");
                }),
                Check(p + "straight speed = MaxSpeed", Kind, () =>
                {
                    ICrewRoster roster = New();
                    PlayerId a = roster.Join().Player;
                    BasePosition start = Member(roster, a).Pose.Position;
                    roster.SubmitMovement(a, Input(0, 1, 1, 0), 0);
                    roster.Advance(0.5);
                    Near(speed * 0.5, Member(roster, a).Pose.Position.DistanceTo(start), Tol, "Distance in 0.5 s");
                }),
                Check(p + "diagonal is not faster (FromAxes(1,1))", Kind, () =>
                {
                    ICrewRoster roster = New();
                    PlayerId a = roster.Join().Player;
                    BasePosition start = Member(roster, a).Pose.Position;
                    roster.SubmitMovement(a, new MovementInput(0, 1, MoveDirection.FromAxes(1, 1)), 0);
                    roster.Advance(0.5);
                    BasePosition end = Member(roster, a).Pose.Position;
                    Near(speed * 0.5, end.DistanceTo(start), Tol, "Diagonal distance in 0.5 s");
                    Near(end.X - start.X, end.Y - start.Y, Tol, "Diagonal must keep 45°");
                }),
                Check(p + "analog magnitude scales speed", Kind, () =>
                {
                    ICrewRoster roster = New();
                    PlayerId a = roster.Join().Player;
                    BasePosition start = Member(roster, a).Pose.Position;
                    roster.SubmitMovement(a, Input(0, 1, 0.5, 0), 0);
                    roster.Advance(0.5);
                    Near(speed * 0.25, Member(roster, a).Pose.Position.DistanceTo(start), Tol, "Half-stick distance in 0.5 s");
                }),
                Check(p + "bounds: walking into the outer wall stays admissible and close", Kind, () =>
                {
                    ICrewRoster roster = New();
                    PlayerId a = roster.Join().Player;
                    roster.SubmitMovement(a, Input(0, 1, 0, -1), 0);
                    double dt = 0.02;
                    for (int i = 0; i < 300; i++)
                    {
                        BasePosition before = Member(roster, a).Pose.Position;
                        roster.Advance(dt);
                        BasePosition after = Member(roster, a).Pose.Position;
                        Require(layout.IsAdmissible(after, r), "Inadmissible position " + after + ".");
                        Require(after.DistanceTo(before) <= speed * dt + Tol, "Step longer than speed × dt.");
                    }
                    double gap = Member(roster, a).Pose.Position.Y - (layout.Bounds.MinY + r);
                    Require(gap >= -Tol && gap <= speed * dt + Tol, "Blocked member should stop within one step of the wall; gap " + gap + " m.");
                }),
                Check(p + "obstacle blocks and is never crossed", Kind, () =>
                {
                    ICrewRoster roster = New();
                    roster.Join();
                    PlayerId b = roster.Join().Player; // spawn (4, 3), wall at x 5.9
                    roster.SubmitMovement(b, Input(0, 1, 1, 0), 0);
                    for (int i = 0; i < 200; i++)
                    {
                        roster.Advance(0.02);
                        BasePosition at = Member(roster, b).Pose.Position;
                        Require(layout.IsAdmissible(at, r), "Inadmissible position " + at + ".");
                        Require(at.X <= Phase02Fixtures.Wall.MinX - r + Tol, "Crossed the wall: " + at + ".");
                    }
                }),
                Check(p + "one large step does not tunnel through a thin wall", Kind, () =>
                {
                    ICrewRoster roster = New();
                    roster.Join();
                    PlayerId b = roster.Join().Player;
                    roster.SubmitMovement(b, Input(0, 1, 1, 0), 0);
                    roster.Advance(1.5); // 3 m requested: the target (7, 3) is admissible BEHIND the wall
                    BasePosition at = Member(roster, b).Pose.Position;
                    Require(layout.IsAdmissible(at, r), "Inadmissible position " + at + ".");
                    Require(at.X < Phase02Fixtures.Wall.MinX, "Tunnelled through the wall in one step: " + at + ".");
                }),
                Check(p + "Advance(0) is still; invalid elapsed rejected without change", Kind, () =>
                {
                    ICrewRoster roster = New();
                    PlayerId a = roster.Join().Player;
                    roster.SubmitMovement(a, Input(0, 1, 1, 0), 0);
                    BasePosition start = Member(roster, a).Pose.Position;
                    roster.Advance(0);
                    Require(Member(roster, a).Pose.Position.Equals(start), "Advance(0) moved the member.");
                    foreach (double bad in new[] { double.NaN, double.PositiveInfinity, -0.1 })
                    {
                        bool threw = false;
                        try { roster.Advance(bad); } catch (ArgumentException) { threw = true; }
                        Require(threw, "Advance(" + bad + ") must throw ArgumentException.");
                        Require(Member(roster, a).Pose.Position.Equals(start), "Advance(" + bad + ") changed the position.");
                    }
                }),
                Check(p + "facing and motion follow confirmed walking", Kind, () =>
                {
                    ICrewRoster roster = New();
                    PlayerId a = roster.Join().Player;
                    roster.SubmitMovement(a, Input(0, 1, 1, 0), 0);
                    roster.Advance(0.1);
                    CrewPose walking = Member(roster, a).Pose;
                    Require(walking.Motion == CrewMotion.Walking, "Moving member must be Walking.");
                    Near(1, walking.Facing.X, Tol, "Facing X after walking right");
                    roster.SubmitMovement(a, Input(0, 2, 0, 0), 0);
                    roster.Advance(0.1);
                    CrewPose stopped = Member(roster, a).Pose;
                    Require(stopped.Motion == CrewMotion.Idle, "Stopped member must be Idle.");
                    Require(stopped.Facing.Equals(walking.Facing), "Facing must be kept when stopping.");
                    Require(stopped.Position.Equals(walking.Position), "Zero direction must not move.");
                }),
                Check(p + "crew members do not collide with each other", Kind, () =>
                {
                    ICrewRoster roster = New();
                    PlayerId a = roster.Join().Player, b = roster.Join().Player; // 2 m apart on y = 3
                    roster.SubmitMovement(a, Input(0, 1, 1, 0), 0);
                    roster.SubmitMovement(b, Input(0, 1, -1, 0), 0);
                    roster.Advance(0.5);
                    Near(Member(roster, a).Pose.Position.X, Member(roster, b).Pose.Position.X, Tol, "Both must reach x = 3 (pass through)");
                }),
                Check(p + "restart: spawn, idle, default facing, direction cleared, ids/slots/sequences kept", Kind, () =>
                {
                    ICrewRoster roster = New();
                    PlayerId a = roster.Join().Player, b = roster.Join().Player;
                    roster.SubmitMovement(a, Input(0, 4, 1, 0), 0);
                    roster.SubmitMovement(b, Input(0, 2, 0, 1), 0);
                    roster.Advance(0.5);
                    roster.ResetForRestart();
                    foreach (CrewMemberState m in roster.Members)
                    {
                        Require(m.Pose.Position.Equals(layout.SpawnPoints[m.Slot]), m.Player + " not back at spawn.");
                        Require(m.Pose.Motion == CrewMotion.Idle && m.Pose.Facing.Equals(CrewPose.DefaultFacing), m.Player + " pose not reset.");
                    }
                    Require(Member(roster, a).Slot == 0 && Member(roster, b).Slot == 1, "Slots must be kept.");
                    roster.Advance(0.5);
                    Require(Member(roster, a).Pose.Position.Equals(layout.SpawnPoints[0]), "Held direction must be cleared by restart.");
                    Require(roster.SubmitMovement(a, Input(1, 4, 1, 0), 1) == MovementInputDisposition.StaleSequence, "Sequences must be kept across restart.");
                    Require(roster.SubmitMovement(a, Input(1, 5, 1, 0), 1) == MovementInputDisposition.Applied, "Next sequence in the new epoch must apply.");
                }),
                Check(p + "published member list is immutable / replaced on change", Kind, () =>
                {
                    ICrewRoster roster = New();
                    PlayerId a = roster.Join().Player;
                    IReadOnlyList<CrewMemberState> held = roster.Members;
                    BasePosition before = held[0].Pose.Position;
                    roster.SubmitMovement(a, Input(0, 1, 1, 0), 0);
                    roster.Advance(0.5);
                    roster.Join();
                    Require(held.Count == 1 && held[0].Pose.Position.Equals(before), "An earlier Members list changed after Advance/Join.");
                    if (held is IList<CrewMemberState> list)
                    {
                        bool threw = false;
                        try { list.Add(held[0]); } catch (NotSupportedException) { threw = true; }
                        Require(threw, "Members must not be writable through IList.");
                    }
                }),
                Check(p + "leave forgets the held direction", Kind, () =>
                {
                    ICrewRoster roster = New();
                    PlayerId a = roster.Join().Player;
                    roster.SubmitMovement(a, Input(0, 1, 1, 0), 0);
                    roster.Leave(a);
                    Require(roster.SubmitMovement(a, Input(0, 2, 1, 0), 0) == MovementInputDisposition.NotJoined, "Input after leave must be NotJoined.");
                    PlayerId c = roster.Join().Player;
                    roster.Advance(0.5);
                    Require(Member(roster, c).Pose.Position.Equals(layout.SpawnPoints[0]), "New member inherited the old held direction.");
                })
            };
            return checks;
        }

        private static MovementInput Input(long epoch, long sequence, double x, double y) =>
            new MovementInput(epoch, sequence, new MoveDirection(x, y));

        private static CrewMemberState Member(ICrewRoster roster, PlayerId player)
        {
            foreach (CrewMemberState m in roster.Members)
                if (m.Player == player) return m;
            throw new Phase02CheckFailed(player + " is not in Members.");
        }
    }
}
