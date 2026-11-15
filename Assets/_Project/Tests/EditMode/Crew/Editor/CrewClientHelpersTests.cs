using System;
using System.Collections.Generic;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Crew;
using Trainer.Application.Development;
using Trainer.Application.Outpost;

/// <summary>D3 client helpers over the shared development fake: input intent, smoothing and station requests.</summary>
public sealed class CrewClientHelpersTests
{
    private static readonly PlayerId P1 = new PlayerId(1), P2 = new PlayerId(2);
    private OutpostScenario scenario;
    private FakeOutpostSessionPort port;

    [SetUp] public void SetUp()
    {
        scenario = OutpostFixtures.Scenario();
        port = new FakeOutpostSessionPort();
    }

    private OutpostSessionSnapshot JoinAs(PlayerId local, params PlayerId[] crew)
    {
        OutpostSessionSnapshot s = OutpostFixtures.Next(OutpostFixtures.Initial(scenario, 0, crew), state: SessionState.Running);
        port.Publish(s);
        port.Join(local);
        return s;
    }

    [Test] public void InputIsSentOnlyOnChangeWithIncreasingSequences()
    {
        var sender = new CrewInputSender(port);
        Assert.That(sender.Update(1, 0), Is.False, "Not joined: nothing is sent.");
        Assert.That(port.DroppedMovementInputCount + port.MovementInputCount, Is.EqualTo(0));
        JoinAs(P1, P1);
        Assert.That(sender.Update(0, 0), Is.False, "The authority already holds zero.");
        Assert.That(sender.Update(1, 1), Is.True);
        Assert.That(port.LastMovementInput.Sequence, Is.EqualTo(1));
        Assert.That(port.LastMovementInput.Direction.Magnitude, Is.EqualTo(1).Within(1e-12), "Diagonal normalised.");
        Assert.That(sender.Update(1, 1), Is.False, "Held key: no flood.");
        Assert.That(sender.Update(0, 1), Is.True);
        Assert.That(sender.Update(0, 0), Is.True, "Release sends the stop.");
        Assert.That(port.LastMovementInput.Sequence, Is.EqualTo(3));
        Assert.That(port.MovementInputCount, Is.EqualTo(3));
    }

    [Test] public void RestartAndRejoinResendTheHeldDirection()
    {
        var sender = new CrewInputSender(port);
        OutpostSessionSnapshot s = JoinAs(P1, P1);
        sender.Update(1, 0);
        port.Publish(OutpostFixtures.Next(s, newEpoch: 1));
        Assert.That(sender.Update(1, 0), Is.True, "New epoch: the authority cleared the held direction.");
        Assert.That(port.LastMovementInput.Epoch, Is.EqualTo(1));
        Assert.That(port.LastMovementInput.Sequence, Is.EqualTo(2), "Sequences continue for the same PlayerId.");

        port.Disconnect(OutpostReasons.ConnectionLost);
        Assert.That(sender.Update(1, 0), Is.False);
        port.TryGetSnapshot(out OutpostSessionSnapshot last);
        port.Publish(OutpostFixtures.Next(last, crew: new[] { new CrewMemberState(P2, 0, CrewPose.IdleAt(scenario.Layout.SpawnPoints[0]), 0) }));
        port.Join(P2);
        Assert.That(sender.Update(1, 0), Is.True);
        Assert.That(port.LastMovementInput.Sequence, Is.EqualTo(1), "A new PlayerId starts again at 1.");
    }

    [Test] public void BlockedInputStopsOnceAndResumes()
    {
        var sender = new CrewInputSender(port);
        JoinAs(P1, P1);
        var panel = new object();
        sender.Update(0, 1);
        sender.Block(panel);
        Assert.That(sender.Update(0, 1), Is.True);
        Assert.That(port.LastMovementInput.Direction.IsZero, Is.True, "Task panel focused: the astronaut stops.");
        Assert.That(sender.Update(1, 1), Is.False, "Keys are ignored while blocked.");
        sender.Unblock(panel);
        Assert.That(sender.Update(0, 1), Is.True);
        Assert.That(port.LastMovementInput.Direction.Y, Is.EqualTo(1));
        Assert.Throws<ArgumentNullException>(() => sender.Block(null));
    }

    [Test] public void ReplacementSenderContinuesConfirmedAndUnacknowledgedSequences()
    {
        var s = JoinAs(P1, P1);
        port.Publish(OutpostFixtures.Next(s, crew: OutpostFixtures.WithMember(s, P1, s.Crew[0].Pose, 12)));
        var first = new CrewInputSender(port);
        first.Update(1, 0);
        Assert.That(port.LastMovementInput.Sequence, Is.EqualTo(13), "Start above the confirmed sequence.");
        var replacement = new CrewInputSender(port);
        replacement.Update(0, 0);
        Assert.That(port.LastMovementInput.Sequence, Is.EqualTo(14), "Include locally issued inputs not yet in a snapshot.");
        Assert.That(port.LastMovementInput.Direction.IsZero, Is.True, "A fresh binding can stop a held direction.");
        replacement.Update(1, 0);
        Assert.That(port.LastMovementInput.Sequence, Is.EqualTo(15));
        Assert.That(replacement.Update(1, 0), Is.False, "Still no per-frame flooding.");
    }

    [Test] public void ReplacementSequenceSurvivesRestartAndResetsOnlyForNewIdentity()
    {
        var s = JoinAs(P1, P1);
        var first = new CrewInputSender(port);
        first.Update(1, 0);
        first.Update(0, 1);
        var next = OutpostFixtures.Next(s, newEpoch: 1, crew: OutpostFixtures.WithMember(s, P1, s.Crew[0].Pose, 2));
        port.Publish(next);
        var replacement = new CrewInputSender(port);
        replacement.Update(0, 1);
        Assert.That(port.LastMovementInput.Sequence, Is.EqualTo(3));
        Assert.That(port.LastMovementInput.Epoch, Is.EqualTo(1));
        port.Disconnect(OutpostReasons.ConnectionLost);
        replacement.Update(0, 1);
        port.Publish(OutpostFixtures.Next(next, crew: new[] { new CrewMemberState(P2, 0, s.Crew[0].Pose, 0) }));
        port.Join(P2);
        replacement.Update(0, 1);
        Assert.That(port.LastMovementInput.Sequence, Is.EqualTo(1));
        var otherPort = new FakeOutpostSessionPort();
        otherPort.Publish(OutpostFixtures.Initial(scenario, 0, P1)); otherPort.Join(P1);
        new CrewInputSender(otherPort).Update(1, 0);
        Assert.That(otherPort.LastMovementInput.Sequence, Is.EqualTo(1), "IDs in another session do not share sequence history.");
    }

    [Test] public void InterpolationUsesAFixedDelayAndNeverExtrapolates()
    {
        var smooth = new CrewInterpolator(0.1, 1.5);
        OutpostSessionSnapshot s0 = OutpostFixtures.Initial(scenario, 0, P1);
        smooth.Push(s0, 10.0);
        var moved = new CrewPose(new BasePosition(3.2, 3), MoveDirection.FromAxes(1, 0), CrewMotion.Walking);
        OutpostSessionSnapshot s1 = OutpostFixtures.Next(s0, crew: OutpostFixtures.WithMember(s0, P1, moved, 1));
        smooth.Push(s1, 10.1);

        Assert.That(smooth.TrySample(P1, 10.15, out BasePosition half), Is.True);
        Assert.That(half.X, Is.EqualTo(3.1).Within(1e-9), "Drawn 0.1 s in the past, halfway between samples.");
        smooth.TrySample(P1, 99, out BasePosition later);
        Assert.That(later.X, Is.EqualTo(3.2).Within(1e-12), "Holds the newest confirmed position, no guessing.");
        Assert.That(smooth.TrySample(P2, 10, out _), Is.False);

        var jumped = new CrewPose(new BasePosition(13, 8), MoveDirection.FromAxes(1, 0), CrewMotion.Idle);
        smooth.Push(OutpostFixtures.Next(s1, crew: OutpostFixtures.WithMember(s1, P1, jumped, 1)), 10.2);
        smooth.TrySample(P1, 10.25, out BasePosition snapped);
        Assert.That(snapped.X, Is.EqualTo(13), "A jump longer than the teleport distance snaps instead of sliding through walls.");

        smooth.Push(OutpostFixtures.Initial(scenario, 1, P1), 10.3);
        smooth.TrySample(P1, 10.31, out BasePosition restarted);
        Assert.That(restarted, Is.EqualTo(scenario.Layout.SpawnPoints[0]), "New epoch: straight to spawn.");
        smooth.Push(OutpostFixtures.Initial(scenario, 2), 10.4);
        Assert.That(smooth.TrySample(P1, 10.5, out _), Is.False, "Members who left are dropped.");
        Assert.Throws<ArgumentOutOfRangeException>(() => new CrewInterpolator(double.NaN));
    }

    [Test] public void StationRequestsGoToTheAuthorityOneAtATime()
    {
        OutpostSessionSnapshot s = JoinAs(P1, P1, P2);
        var near = new CrewPose(new BasePosition(3, 7.2), MoveDirection.FromAxes(0, 1), CrewMotion.Idle);
        s = OutpostFixtures.Next(s, crew: OutpostFixtures.WithMember(s, P1, near, 0));
        port.Publish(s);
        using (var interaction = new StationInteractionClient(port, scenario))
        {
            Assert.That(interaction.TryFindStationInReach(out StationPlacement station), Is.True);
            Assert.That(station.Id, Is.EqualTo(OutpostFixtures.WaterProcessor));
            Assert.That(interaction.TasksAt(station.Id)[0].Id, Is.EqualTo(OutpostFixtures.TreatWater));

            Assert.That(interaction.Interact(), Is.True);
            OutpostCommand sent = port.PendingCommands[0];
            Assert.That(sent.Action, Is.EqualTo(OutpostAction.StartTask));
            Assert.That(sent.Station, Is.EqualTo(OutpostFixtures.WaterProcessor));
            Assert.That(interaction.Interact(), Is.False, "One request in flight.");
            Assert.That(port.PendingCommands.Count, Is.EqualTo(1));

            port.Reject(sent.Id, OutpostReasons.StationOccupied);
            Assert.That(interaction.Pending, Is.Null);
            Assert.That(interaction.LastResult.Reason, Is.EqualTo(OutpostReasons.StationOccupied));

            var mine = new StationState(OutpostFixtures.WaterProcessor, StationAvailability.Occupied,
                new TaskProgress(OutpostFixtures.TreatWater, P1, 2, 20, false), null);
            port.Publish(OutpostFixtures.Next(s, stations: OutpostFixtures.WithStation(s, mine)));
            Assert.That(interaction.Interact(), Is.True);
            Assert.That(port.PendingCommands[0].Action, Is.EqualTo(OutpostAction.CancelTask), "E on your own task cancels it.");

            var abandoned = new List<string>();
            interaction.Abandoned += (command, reason) => abandoned.Add(reason);
            port.Disconnect(OutpostReasons.ConnectionLost);
            Assert.That(abandoned, Is.EqualTo(new[] { OutpostReasons.ConnectionLost }));
            Assert.That(interaction.Pending, Is.Null, "No orphan request after the link is lost.");
            Assert.That(interaction.Interact(), Is.False, "Not joined: nothing is sent.");
        }
    }

    [Test] public void NoStationInReachSendsNothing()
    {
        JoinAs(P1, P1);
        using (var interaction = new StationInteractionClient(port, scenario))
        {
            Assert.That(interaction.TryFindStationInReach(out _), Is.False, "Spawn is far from every station.");
            Assert.That(interaction.Interact(), Is.False);
            Assert.That(port.PendingCommands.Count, Is.EqualTo(0));
        }
    }
}
