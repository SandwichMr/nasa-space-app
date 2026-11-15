using System;
using System.Collections.Generic;
using NUnit.Framework;
using Trainer.Application.Crew;
using Trainer.Application.Development;
using Trainer.Application.Outpost;

/// <summary>D3 roster and movement solver: identity, presence, ordering, speed, diagonal and collisions.</summary>
public sealed class CrewRosterTests
{
    private OutpostScenario scenario;
    private CrewRoster roster;
    private double Speed => scenario.Movement.MaxSpeedMetersPerSecond;
    private double Radius => scenario.Movement.CrewRadiusMeters;

    [SetUp] public void SetUp()
    {
        scenario = OutpostFixtures.Scenario();
        roster = CrewRoster.For(scenario);
    }

    private PlayerId Join() => roster.Join().Player;

    private CrewMemberState Member(PlayerId id)
    {
        foreach (CrewMemberState m in roster.Members) if (m.Player == id) return m;
        Assert.Fail($"{id} not present");
        return null;
    }

    private MovementInputDisposition Hold(PlayerId id, long seq, double x, double y, long epoch = 0) =>
        roster.SubmitMovement(id, new MovementInput(epoch, seq, MoveDirection.FromAxes(x, y)), epoch);

    [Test] public void IdsAreNeverReusedAndSlotsAreTheLowestFree()
    {
        PlayerId a = Join(), b = Join(), c = Join();
        Assert.That(new[] { a.Value, b.Value, c.Value }, Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(roster.Leave(b), Is.True);
        Assert.That(roster.Leave(b), Is.False, "Leave is idempotent.");
        PlayerId d = Join();
        Assert.That(d.Value, Is.EqualTo(4), "A rejoin is a new identity.");
        Assert.That(Member(d).Slot, Is.EqualTo(1), "The freed slot is reused.");
        Assert.That(Member(d).Pose.Position, Is.EqualTo(scenario.Layout.SpawnPoints[1]));
        Assert.That(Member(d).LastInputSequence, Is.EqualTo(0));
        Assert.That(roster.Members.Count, Is.EqualTo(3), "No duplicate members.");
        Assert.That(roster.Members[0].Player.Value < roster.Members[1].Player.Value, Is.True, "PlayerId order.");
        Join();
        OutpostMembership full = roster.Join();
        Assert.That(full.Status, Is.EqualTo(MembershipStatus.Rejected));
        Assert.That(full.Reason, Is.EqualTo(OutpostReasons.SessionFull));
        Assert.That(roster.SubmitMovement(new PlayerId(99), new MovementInput(0, 1, MoveDirection.Zero), 0),
            Is.EqualTo(MovementInputDisposition.NotJoined));
    }

    [Test] public void SpeedIsTheSameInEveryDirectionAndAnalogIsSlower()
    {
        PlayerId a = Join();
        BasePosition start = Member(a).Pose.Position;
        Hold(a, 1, 1, 1);
        roster.Advance(0.5);
        Assert.That(Member(a).Pose.Position.DistanceTo(start), Is.EqualTo(Speed * 0.5).Within(1e-9),
            "Diagonal keys: normalised, not sqrt(2) faster.");
        Assert.That(Member(a).Pose.Motion, Is.EqualTo(CrewMotion.Walking));
        Assert.That(Member(a).Pose.Facing.X, Is.EqualTo(Math.Sqrt(0.5)).Within(1e-9));

        BasePosition mid = Member(a).Pose.Position;
        Hold(a, 2, 0.5, 0);
        roster.Advance(0.5);
        Assert.That(Member(a).Pose.Position.X - mid.X, Is.EqualTo(Speed * 0.25).Within(1e-9), "Half stick, half speed.");

        Hold(a, 3, 0, 0);
        BasePosition stop = Member(a).Pose.Position;
        roster.Advance(1);
        Assert.That(Member(a).Pose.Position, Is.EqualTo(stop));
        Assert.That(Member(a).Pose.Motion, Is.EqualTo(CrewMotion.Idle));
        Assert.That(Member(a).Pose.Facing, Is.EqualTo(MoveDirection.FromAxes(1, 0)), "Facing keeps the last move.");
    }

    [Test] public void MembersIsReplacedOnlyWhenSomethingChanged()
    {
        PlayerId a = Join();
        IReadOnlyList<CrewMemberState> idle = roster.Members;
        roster.Advance(1);
        roster.Advance(0);
        Assert.That(roster.Members, Is.SameAs(idle), "Idle crew: no new state, no new revision needed.");
        Hold(a, 1, 1, 0);
        Assert.That(roster.Members, Is.Not.SameAs(idle), "The applied sequence is published.");
        Assert.That(Member(a).LastInputSequence, Is.EqualTo(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => roster.Advance(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => roster.Advance(-1));
        Assert.Throws<ArgumentNullException>(() => roster.SubmitMovement(a, null, 0));
    }

    [Test] public void OldDuplicatedAndForeignEpochInputsAreIgnored()
    {
        PlayerId a = Join();
        Assert.That(Hold(a, 5, 1, 0), Is.EqualTo(MovementInputDisposition.Applied));
        Assert.That(Hold(a, 5, -1, 0), Is.EqualTo(MovementInputDisposition.StaleSequence));
        Assert.That(Hold(a, 3, 0, 1), Is.EqualTo(MovementInputDisposition.StaleSequence));
        Assert.That(roster.SubmitMovement(a, new MovementInput(1, 9, MoveDirection.FromAxes(0, -1)), 0),
            Is.EqualTo(MovementInputDisposition.EpochMismatch));
        BasePosition start = Member(a).Pose.Position;
        roster.Advance(0.25);
        Assert.That(Member(a).Pose.Position.X, Is.GreaterThan(start.X), "Still the applied input (seq 5, right).");
        Assert.That(Member(a).Pose.Position.Y, Is.EqualTo(start.Y));
    }

    [Test] public void WallsStopAndSlideAndNoTimeStepTunnels()
    {
        PlayerId a = Join();
        // Spawn (3, 3); the wall starts at x = 7.8 for y < 4. Push right for a very long frame.
        Hold(a, 1, 1, 0);
        roster.Advance(30);
        BasePosition atWall = Member(a).Pose.Position;
        Assert.That(atWall.X, Is.EqualTo(7.8 - Radius).Within(1e-3), "Stops touching the wall, never inside or past it.");
        Assert.That(scenario.Layout.IsAdmissible(atWall, Radius), Is.True);

        // Diagonal into the wall slides upwards, through the door (y 4..6) and into the other room.
        Hold(a, 2, 1, 1);
        roster.Advance(0.5);
        BasePosition slid = Member(a).Pose.Position;
        Assert.That(slid.Y, Is.GreaterThan(atWall.Y + 0.5), "Sliding along the wall, not stuck.");
        Assert.That(slid.DistanceTo(atWall), Is.LessThanOrEqualTo(Speed * 0.5 + 1e-9), "Never faster than the speed.");
        roster.Advance(5);
        Assert.That(Member(a).Pose.Position.X, Is.GreaterThan(8.2), "Reached the right room through the door.");
    }

    [Test] public void RandomWalkKeepsEveryConfirmedPositionAdmissibleAndWithinSpeed()
    {
        var random = new Random(20261114);
        var ids = new List<PlayerId> { Join(), Join(), Join() };
        long seq = 0;
        for (int step = 0; step < 4000; step++)
        {
            foreach (PlayerId id in ids)
                if (random.NextDouble() < 0.2)
                    roster.SubmitMovement(id, new MovementInput(0, ++seq,
                        MoveDirection.FromAxes(random.Next(-1, 2), random.Next(-1, 2))), 0);
            var before = new Dictionary<PlayerId, BasePosition>();
            foreach (CrewMemberState m in roster.Members) before[m.Player] = m.Pose.Position;
            double dt = random.NextDouble() < 0.05 ? random.NextDouble() * 3 : random.NextDouble() / 30;
            roster.Advance(dt);
            foreach (CrewMemberState m in roster.Members)
            {
                Assert.That(scenario.Layout.IsAdmissible(m.Pose.Position, Radius), Is.True, $"step {step}");
                Assert.That(m.Pose.Position.DistanceTo(before[m.Player]), Is.LessThanOrEqualTo(Speed * dt + 1e-9), $"step {step}");
            }
        }
    }

    [Test] public void RestartReturnsEveryoneToSpawnAndKeepsIdentity()
    {
        PlayerId a = Join(), b = Join();
        Hold(a, 7, 0, 1);
        roster.Advance(1);
        roster.ResetForRestart();
        Assert.That(Member(a).Pose.Position, Is.EqualTo(scenario.Layout.SpawnPoints[0]));
        Assert.That(Member(a).Pose.Motion, Is.EqualTo(CrewMotion.Idle));
        Assert.That(Member(a).Pose.Facing, Is.EqualTo(CrewPose.DefaultFacing));
        Assert.That(Member(a).LastInputSequence, Is.EqualTo(7), "Sequences continue across epochs.");
        Assert.That(Member(b).Slot, Is.EqualTo(1));
        roster.Advance(1);
        Assert.That(Member(a).Pose.Position, Is.EqualTo(scenario.Layout.SpawnPoints[0]), "Held direction was cleared.");
        Assert.That(Hold(a, 7, 1, 0, epoch: 1), Is.EqualTo(MovementInputDisposition.StaleSequence));
        Assert.That(Hold(a, 8, 1, 0, epoch: 1), Is.EqualTo(MovementInputDisposition.Applied));
    }

    [Test] public void SolverRejectsBrokenStartAndInvalidDistance()
    {
        Assert.Throws<InvalidOperationException>(() => CrewMotionSolver.Move(scenario.Layout, Radius,
            new BasePosition(8, 2), MoveDirection.FromAxes(1, 0), 1), "Start inside a wall.");
        Assert.Throws<ArgumentOutOfRangeException>(() => CrewMotionSolver.Move(scenario.Layout, Radius,
            new BasePosition(3, 3), MoveDirection.FromAxes(1, 0), double.PositiveInfinity));
        Assert.That(CrewMotionSolver.Move(scenario.Layout, Radius, new BasePosition(3, 3), MoveDirection.Zero, 5),
            Is.EqualTo(new BasePosition(3, 3)));
        Assert.That(CrewMotionSolver.Move(scenario.Layout, Radius, new BasePosition(Radius, Radius),
            MoveDirection.FromAxes(-1, -1), 2), Is.EqualTo(new BasePosition(Radius, Radius)), "Wedged in a corner.");
    }
}
