using System;
using System.Collections.Generic;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Application.Outpost;

/// <summary>
/// The examples of docs/phase-02/SESSION_CONTRACT.md, compiled and executed against the development fake.
/// The fixture plays the authority's part BY HAND (it decides results and publishes consequences); these
/// examples show the client-side flow and the documented rules, not an implemented authority.
/// </summary>
public sealed class OutpostContractExamplesTests
{
    private static readonly PlayerId Ana = new PlayerId(1), Leo = new PlayerId(2);
    private OutpostScenario scenario;

    [SetUp] public void SetUp() => scenario = OutpostFixtures.Scenario();

    // Example 1 — join and initial state; telling the local astronaut from remote ones.
    [Test] public void Example1_JoinAndInitialState()
    {
        var port = new FakeOutpostSessionPort();
        var seen = new List<string>();
        port.MembershipChanged += m => seen.Add(m.Status.ToString());

        // Authority side (simulated by the fixture): Ana joined first (slot 0), Leo second (slot 1).
        port.Publish(OutpostFixtures.Initial(scenario, 0, Ana, Leo));
        port.Join(Leo);

        // Client side: identity comes from Membership, state from the snapshot.
        Assert.That(port.Membership.IsJoined, Is.True);
        Assert.That(port.TryGetSnapshot(out OutpostSessionSnapshot state), Is.True);
        foreach (CrewMemberState member in state.Crew)
        {
            bool local = member.Player == port.Membership.Player;
            Assert.That(local, Is.EqualTo(member.Player == Leo));
            Assert.That(member.Pose.Position, Is.EqualTo(scenario.Layout.SpawnPoints[member.Slot]));
            Assert.That(member.Pose.Motion, Is.EqualTo(CrewMotion.Idle));
        }
        Assert.That(state.ScenarioId, Is.EqualTo(scenario.Id), "Same build scenario as the authority.");
        Assert.That(seen, Is.EqualTo(new[] { "Joined" }));
    }

    // Example 2 — movement requested (intent) and confirmed (snapshot).
    [Test] public void Example2_MovementRequestedThenConfirmed()
    {
        var port = new FakeOutpostSessionPort();
        port.Publish(OutpostFixtures.Initial(scenario, 0, Ana));
        port.Join(Ana);
        port.TryGetSnapshot(out OutpostSessionSnapshot before);

        // Client: hold "right + up". Diagonal input is normalised, so it is not faster.
        long sequence = 1;
        port.SubmitMovement(new MovementInput(before.Epoch, sequence, MoveDirection.FromAxes(1, 1)));
        Assert.That(port.TryGetSnapshot(out OutpostSessionSnapshot unchanged) && unchanged == before, Is.True,
            "No local prediction: nothing moves until the authority confirms.");

        // Authority (fixture): after 0.5 s at 2 m/s the confirmed position moved 1 m along the diagonal.
        double step = 0.5 * scenario.Movement.MaxSpeedMetersPerSecond * Math.Sqrt(0.5);
        BasePosition start = scenario.Layout.SpawnPoints[0];
        var pose = new CrewPose(new BasePosition(start.X + step, start.Y + step), MoveDirection.FromAxes(1, 1), CrewMotion.Walking);
        Assert.That(scenario.Layout.IsAdmissible(pose.Position, scenario.Movement.CrewRadiusMeters), Is.True);
        port.Publish(OutpostFixtures.Next(before, crew: OutpostFixtures.WithMember(before, Ana, pose, sequence)));

        port.TryGetSnapshot(out OutpostSessionSnapshot after);
        after.TryGetCrew(Ana, out CrewMemberState ana);
        Assert.That(ana.LastInputSequence, Is.EqualTo(sequence), "Input 1 is applied.");
        Assert.That(ana.Pose.Position.DistanceTo(start), Is.EqualTo(1.0).Within(1e-9));
        Assert.That(ana.Pose.Motion, Is.EqualTo(CrewMotion.Walking));

        // The authority's ordering rule: a repeated or older sequence is ignored, without a result.
        var repeated = new MovementInput(after.Epoch, sequence, MoveDirection.Zero);
        Assert.That(MovementInputPolicy.Evaluate(ana.LastInputSequence, after.Epoch, repeated),
            Is.EqualTo(MovementInputDisposition.StaleSequence));
    }

    // Example 3 — two players try to occupy the same station.
    [Test] public void Example3_TwoPlayersTryTheSameStation()
    {
        var anaPort = new FakeOutpostSessionPort();
        var leoPort = new FakeOutpostSessionPort();
        OutpostSessionSnapshot s0 = OutpostFixtures.Initial(scenario, 0, Ana, Leo);
        // Both are next to the water processor (3, 8), within 1.2 m.
        s0 = OutpostFixtures.Next(s0, crew: new[]
        {
            new CrewMemberState(Ana, 0, CrewPose.IdleAt(new BasePosition(3, 7)), 0),
            new CrewMemberState(Leo, 1, CrewPose.IdleAt(new BasePosition(3.8, 7.5)), 0)
        }, state: SessionState.Running);
        foreach (FakeOutpostSessionPort p in new[] { anaPort, leoPort }) p.Publish(s0);
        anaPort.Join(Ana);
        leoPort.Join(Leo);

        CommandResult anaResult = null, leoResult = null;
        anaPort.CommandCompleted += r => anaResult = r;
        leoPort.CommandCompleted += r => leoResult = r;
        OutpostCommand anaStart = OutpostCommand.StartTask(Guid.NewGuid(), s0.Epoch, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
        OutpostCommand leoStart = OutpostCommand.StartTask(Guid.NewGuid(), s0.Epoch, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
        anaPort.Submit(anaStart);
        leoPort.Submit(leoStart);

        // Authority rule (D2 implements): commands are applied in arrival order; Ana's arrived first.
        var occupied = new StationState(OutpostFixtures.WaterProcessor, StationAvailability.Occupied,
            new TaskProgress(OutpostFixtures.TreatWater, Ana, 0, 20, false), null);
        OutpostSessionSnapshot s1 = OutpostFixtures.Next(s0, stations: OutpostFixtures.WithStation(s0, occupied));
        foreach (FakeOutpostSessionPort p in new[] { anaPort, leoPort }) p.Publish(s1);
        anaPort.Accept(anaStart.Id);
        leoPort.Reject(leoStart.Id, OutpostReasons.StationOccupied);

        Assert.That(anaResult.Accepted, Is.True);
        Assert.That(leoResult.Reason, Is.EqualTo(OutpostReasons.StationOccupied));
        leoPort.TryGetSnapshot(out OutpostSessionSnapshot leoView);
        leoView.TryGetStation(OutpostFixtures.WaterProcessor, out StationState station);
        Assert.That(station.Occupant, Is.EqualTo(Ana), "Leo sees who holds the station.");
        // A snapshot where the same person or station is reserved twice cannot even be built:
        Assert.Throws<ArgumentException>(() => OutpostFixtures.Next(s1, stations: OutpostFixtures.WithStation(s1,
            new StationState(OutpostFixtures.AirMonitor, StationAvailability.Occupied,
                new TaskProgress(OutpostFixtures.CheckCabinAir, Ana, 0, 8, false), null))));
    }

    // Example 4 — task rejected for distance (proximity is checked on the CONFIRMED position).
    [Test] public void Example4_TaskRejectedForDistance()
    {
        var port = new FakeOutpostSessionPort();
        OutpostSessionSnapshot s0 = OutpostFixtures.Next(OutpostFixtures.Initial(scenario, 0, Ana), state: SessionState.Running);
        port.Publish(s0);
        port.Join(Ana);
        s0.TryGetCrew(Ana, out CrewMemberState ana);
        scenario.Layout.TryGetStation(OutpostFixtures.ScienceBench, out StationPlacement bench);
        Assert.That(bench.IsWithinReach(ana.Pose.Position), Is.False, "Spawn (3, 3) is ~11 m from the bench.");

        CommandResult result = null;
        port.CommandCompleted += r => result = r;
        OutpostCommand start = OutpostCommand.StartTask(Guid.NewGuid(), s0.Epoch, OutpostFixtures.ScienceBench, OutpostFixtures.RunExperiment);
        port.Submit(start);
        port.Reject(start.Id, OutpostReasons.OutOfRange);   // fixture answers as the authority would

        Assert.That(result.Accepted, Is.False);
        Assert.That(result.Reason, Is.EqualTo(OutpostReasons.OutOfRange));
        port.TryGetSnapshot(out OutpostSessionSnapshot after);
        Assert.That(after, Is.SameAs(s0), "A rejection changes no state and no revision.");
    }

    // Example 5 — cancel, disconnect and restart.
    [Test] public void Example5_CancelDisconnectAndRestart()
    {
        var port = new FakeOutpostSessionPort();
        var abandoned = new List<string>();
        CommandResult last = null;
        port.CommandAbandoned += (command, reason) => abandoned.Add(reason);
        port.CommandCompleted += r => last = r;
        var near = new CrewMemberState(Ana, 0, CrewPose.IdleAt(new BasePosition(3, 7)), 0);
        OutpostSessionSnapshot s0 = OutpostFixtures.Next(OutpostFixtures.Initial(scenario, 0, Ana), crew: new[] { near },
            state: SessionState.Running);
        var working = new StationState(OutpostFixtures.WaterProcessor, StationAvailability.Occupied,
            new TaskProgress(OutpostFixtures.TreatWater, Ana, 6, 20, false), null);
        OutpostSessionSnapshot s1 = OutpostFixtures.Next(s0, stations: OutpostFixtures.WithStation(s0, working));
        port.Publish(s1);
        port.Join(Ana);

        // Cancel: the task ends without its completion effect; energy already used is not refunded.
        OutpostCommand cancel = OutpostCommand.CancelTask(Guid.NewGuid(), s1.Epoch, OutpostFixtures.WaterProcessor);
        port.Submit(cancel);
        var cancelled = new StationState(OutpostFixtures.WaterProcessor, StationAvailability.Available, null,
            new TaskEnd(OutpostFixtures.TreatWater, Ana, TaskEndReason.CancelledByPerformer, s1.SimulatedTimeSeconds));
        OutpostSessionSnapshot s2 = OutpostFixtures.Next(s1, stations: OutpostFixtures.WithStation(s1, cancelled));
        port.Publish(s2);
        port.Accept(cancel.Id);
        Assert.That(last.Accepted, Is.True);

        // Restart (by anyone): new epoch at revision 0, crew back at spawn, stations cleared.
        OutpostCommand restart = new OutpostCommand(Guid.NewGuid(), s2.Epoch, OutpostAction.Restart);
        port.Submit(restart);
        OutpostSessionSnapshot fresh = OutpostFixtures.Initial(scenario, s2.Epoch + 1, Ana);
        port.Publish(fresh);
        port.Accept(restart.Id);
        Assert.That(last.Epoch, Is.EqualTo(0), "A result keeps the epoch of its command.");
        port.TryGetSnapshot(out OutpostSessionSnapshot now);
        Assert.That(now.Epoch, Is.EqualTo(1));
        Assert.That(now.Stations[0].LastEnd, Is.Null);

        // A late command built from the old epoch is answered EpochMismatch by the authority.
        OutpostCommand late = OutpostCommand.StartTask(Guid.NewGuid(), 0, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
        port.Submit(late);
        port.Reject(late.Id, OutpostReasons.EpochMismatch);
        Assert.That(last.Reason, Is.EqualTo(OutpostReasons.EpochMismatch));

        // Disconnect: pending commands are abandoned (result unknown) and the identity ends.
        port.Submit(OutpostCommand.StartTask(Guid.NewGuid(), 1, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater));
        port.Disconnect(OutpostReasons.ConnectionLost);
        Assert.That(abandoned, Is.EqualTo(new[] { OutpostReasons.ConnectionLost }));
        Assert.That(port.Membership.Status, Is.EqualTo(MembershipStatus.Left));
        // On the authority the same event is Leave(Ana): task cancelled as PerformerLeft, member removed.
        // A rejoin receives a NEW PlayerId; Ana's old id is never reused.
    }

    // Example 6 — presentation driven by the fake (what D4 does before the real port exists).
    private sealed class CrewLabels
    {
        private readonly IOutpostSessionPort port;
        public readonly List<string> Lines = new List<string>();

        public CrewLabels(IOutpostSessionPort port)
        {
            this.port = port;
            port.SnapshotChanged += Render;
            port.MembershipChanged += _ => Render(null);
        }

        private void Render(OutpostSessionSnapshot ignored)
        {
            Lines.Clear();
            if (!port.Membership.IsJoined || !port.TryGetSnapshot(out OutpostSessionSnapshot s))
            {
                Lines.Add("Sin datos vigentes");
                return;
            }
            foreach (CrewMemberState m in s.Crew)
                Lines.Add($"{(m.Player == port.Membership.Player ? "Tú" : "Astronauta " + (m.Slot + 1))}: {m.Pose.Motion}");
        }
    }

    [Test] public void Example6_PresentationWithTheFake()
    {
        var port = new FakeOutpostSessionPort();
        var labels = new CrewLabels(port);           // the view only knows IOutpostSessionPort
        port.Publish(OutpostFixtures.Initial(scenario, 0, Ana, Leo));
        Assert.That(labels.Lines, Is.EqualTo(new[] { "Sin datos vigentes" }), "Not joined yet: nothing is live.");
        port.Join(Ana);
        Assert.That(labels.Lines, Is.EqualTo(new[] { "Tú: Idle", "Astronauta 2: Idle" }));
        port.Disconnect(OutpostReasons.ConnectionLost);
        Assert.That(labels.Lines, Is.EqualTo(new[] { "Sin datos vigentes" }), "A lost link clears live data.");
    }

    /// <summary>
    /// Compile-only sketch (never executed here) of the host adapter D3 writes: the PlayerId comes from the
    /// real connection, never from the payload, and results go back only to their sender.
    /// </summary>
    public static void HostAdapterSketch(IOutpostAuthority authority, Func<PlayerId, ulong> connectionOf,
        Action<ulong, CommandResult> sendResult)
    {
        var connections = new Dictionary<ulong, PlayerId>();
        authority.CommandCompleted += (sender, result) => sendResult(connectionOf(sender), result);
        OutpostMembership joined = authority.Join();                  // on a new, synchronised connection
        if (joined.IsJoined) connections[42UL] = joined.Player;
        OutpostCommand fromWire = new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Pause);
        if (connections.TryGetValue(42UL, out PlayerId sender42)) authority.Submit(sender42, fromWire);
        if (connections.TryGetValue(42UL, out PlayerId leaving)) authority.Leave(leaving); // on disconnect
        connections.Remove(42UL);
    }
}
