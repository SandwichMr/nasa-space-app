using System;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Development;

public sealed class FakeSessionPortTests
{
    private static MissionSnapshot Read(FakeSessionPort port)
    {
        Assert.That(port.TryGetSnapshot(out MissionSnapshot snapshot), Is.True);
        return snapshot;
    }

    [Test] public void SubmitReportsCompletionAndKeepsPreviousSnapshotImmutable()
    {
        var port = new FakeSessionPort();
        var before = Read(port);
        CommandResult result = null;
        port.CommandCompleted += value => result = value;
        var command = new SessionCommand(Guid.NewGuid(), 0, SessionAction.SetScienceEnabled, true);
        port.Submit(command);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.CommandId, Is.EqualTo(command.Id));
        Assert.That(before.ScienceEnabled, Is.False);
        Assert.That(Read(port).ScienceEnabled, Is.True);
        Assert.That(Read(port).Revision, Is.EqualTo(1));
    }

    [Test] public void DuplicateIsNotAppliedAndConflictingIdIsRejected()
    {
        var port = new FakeSessionPort();
        var command = new SessionCommand(Guid.NewGuid(), 0, SessionAction.Resume);
        int changed = 0;
        CommandResult result = null;
        port.SnapshotChanged += value => changed++;
        port.CommandCompleted += value => result = value;
        port.Submit(command);
        port.Submit(new SessionCommand(command.Id, 0, SessionAction.Resume));
        Assert.That(result.Accepted, Is.True);
        Assert.That(changed, Is.EqualTo(1));
        port.Submit(new SessionCommand(command.Id, 0, SessionAction.Pause));
        Assert.That(result.Reason, Is.EqualTo("CommandIdConflict"));
        Assert.That(Read(port).State, Is.EqualTo(SessionState.Running));
    }

    [Test] public void RestartChangesEpochAndReplayDoesNotRestartAgain()
    {
        var port = new FakeSessionPort();
        var restart = new SessionCommand(Guid.NewGuid(), 0, SessionAction.Restart);
        CommandResult result = null;
        port.CommandCompleted += value => result = value;
        port.Submit(restart);
        port.Submit(restart);
        Assert.That(result.Epoch, Is.EqualTo(0));
        Assert.That(Read(port).Epoch, Is.EqualTo(1));
        Assert.That(Read(port).Revision, Is.EqualTo(0));
        port.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.Resume));
        Assert.That(result.Reason, Is.EqualTo("EpochMismatch"));
        port.Submit(new SessionCommand(Guid.NewGuid(), 2, SessionAction.Resume));
        Assert.That(result.Accepted, Is.False);
    }

    [Test] public void StateTransitionsAndActionValuesAreValidated()
    {
        var port = new FakeSessionPort();
        CommandResult result = null;
        port.CommandCompleted += value => result = value;
        port.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.Pause));
        Assert.That(result.Accepted, Is.False);
        port.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.SetScienceEnabled));
        Assert.That(result.Reason, Is.EqualTo("InvalidActionValue"));
        port.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.Resume));
        port.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.Pause));
        Assert.That(Read(port).State, Is.EqualTo(SessionState.Paused));
        port.PublishExample(SessionState.Completed, 12);
        port.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.Resume));
        Assert.That(result.Reason, Is.EqualTo("SessionEnded"));
        port.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.Restart));
        Assert.That(Read(port).ScienceWorkSeconds, Is.EqualTo(0));
    }

    [Test] public void ScienceWorkIsPreservedByCommandsAndFixturesAreExplicit()
    {
        var port = new FakeSessionPort();
        port.PublishExample(SessionState.Running, 42);
        port.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.SetScienceEnabled, false));
        Assert.That(Read(port).ScienceWorkSeconds, Is.EqualTo(42));
        Assert.That(Read(port).Power.ScienceRequestedWatts, Is.EqualTo(0));
        port.PublishExample(SessionState.Interrupted);
        Assert.That(Read(port).State, Is.EqualTo(SessionState.Interrupted));
    }

    [Test] public void InvalidCommandsNeverChangeSnapshot()
    {
        var port = new FakeSessionPort();
        var before = Read(port);
        port.Submit(new SessionCommand(Guid.Empty, 0, SessionAction.Resume));
        port.Submit(new SessionCommand(Guid.NewGuid(), 0, (SessionAction)999));
        port.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.Resume, true));
        Assert.That(Read(port), Is.SameAs(before));
        Assert.Throws<ArgumentNullException>(() => port.Submit(null));
    }
}
