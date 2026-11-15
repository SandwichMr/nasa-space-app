using System;
using System.Collections.Generic;
using System.Linq;
using Trainer.Application;
using Trainer.Application.Outpost;
using Trainer.Simulation.LifeSupport;
using static Trainer.Tooling.Validation.Phase02.Phase02Checks;

namespace Trainer.Tooling.Validation.Phase02
{
    /// <summary>How to build the real authority for conformance. Filled at integration by D5 from what D2
    /// actually delivers (constructor and stepping method); nothing is guessed silently.</summary>
    public sealed class AuthorityHarness
    {
        /// <summary>Builds D2's authority over the given scenario and simulation (the spy wrapping D1's model).</summary>
        public Func<OutpostScenario, IOutpostSimulation, IOutpostAuthority> Create { get; set; }
        /// <summary>D1's real simulation.</summary>
        public Func<IOutpostSimulation> CreateSimulation { get; set; }
        /// <summary>Advances the authority by simulated seconds (the runner's tick). Null: timed checks are Pending.</summary>
        public Action<IOutpostAuthority, double> Advance { get; set; }
        public string Label { get; set; } = "authority";
    }

    /// <summary>
    /// Black-box conformance of D2's <see cref="IOutpostAuthority"/> composed with D1 and D3, on D5's scenario.
    /// Every snapshot raised is also fed to <see cref="OutpostSnapshotStreamValidator"/>; every result is
    /// checked to be routed to its sender. Costs are verified with <see cref="RecordingOutpostSimulation"/>.
    /// </summary>
    public static class OutpostAuthorityConformance
    {
        private const string Kind = Phase02CheckKinds.AuthorityConformance;

        public static List<Phase02Check> Run(AuthorityHarness harness)
        {
            if (harness == null) throw new ArgumentNullException(nameof(harness));
            string p = harness.Label + ": ";
            if (harness.Create == null || harness.CreateSimulation == null)
                return new List<Phase02Check>
                {
                    new Phase02Check(p + "authority conformance", Kind, Phase02CheckStatus.Pending,
                        "No D2 authority / D1 simulation factory in this build (expected before integration/phase-02).")
                };

            Session Open() => new Session(harness);
            void Timed() { if (harness.Advance == null) throw new Phase02CheckPending("No authority tick hook (AuthorityHarness.Advance)."); }

            return new List<Phase02Check>
            {
                Check(p + "two joins, spawn poses, SessionFull, snapshot before Join returns", Kind, () =>
                {
                    var s = Open();
                    Require(s.Snapshots.Count >= 2, "Join must raise SnapshotChanged with the new member before returning.");
                    OutpostSessionSnapshot snap = s.Now;
                    Require(snap.Crew.Count == 2, "Crew count " + snap.Crew.Count + ".");
                    Require(snap.TryGetCrew(s.P1, out var a) && a.Slot == 0 && snap.TryGetCrew(s.P2, out var b) && b.Slot == 1, "Slots 0/1 expected.");
                    OutpostMembership third = s.A.Join();
                    Require(third.Status == MembershipStatus.Rejected && third.Reason == OutpostReasons.SessionFull, "Third Join: " + third.Status + ".");
                    s.Finish();
                }),
                Check(p + "command id rules: empty, replay, conflict, other sender, not joined, epoch", Kind, () =>
                {
                    var s = Open();
                    Require(s.Send(s.P1, new OutpostCommand(Guid.Empty, 0, OutpostAction.Resume)).Reason == OutpostReasons.InvalidCommandId, "Empty id.");
                    var resume = new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Resume);
                    Require(s.Send(s.P1, resume).Accepted, "Resume from Ready must be accepted.");
                    long revision = s.Now.Revision;
                    int snapshots = s.Snapshots.Count;
                    CommandResult replay = s.Send(s.P1, resume);
                    Require(replay.Accepted && replay.Reason == OutpostReasons.Accepted, "Replay must reproduce Accepted.");
                    Require(s.Now.Revision == revision && s.Snapshots.Count == snapshots, "Replay must not publish a change.");
                    Require(s.Send(s.P1, new OutpostCommand(resume.Id, 0, OutpostAction.Pause)).Reason == OutpostReasons.CommandIdConflict, "Same id, other content.");
                    Require(s.Send(s.P2, resume).Reason == OutpostReasons.CommandIdConflict, "Same id from another sender.");
                    Require(s.Send(new PlayerId(99), new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Pause)).Reason == OutpostReasons.NotJoined, "Unknown sender.");
                    Require(s.Send(s.P1, new OutpostCommand(Guid.NewGuid(), 5, OutpostAction.Pause)).Reason == OutpostReasons.EpochMismatch, "Wrong epoch.");
                    Require(s.Send(s.P1, new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.StartTask)).Reason == OutpostReasons.InvalidActionValue, "Missing station/task.");
                    s.Finish();
                }),
                Check(p + "state gates: Ready and Paused reject StartTask", Kind, () =>
                {
                    var s = Open();
                    Require(s.Start(s.P1, Phase02Fixtures.DockA, Phase02Fixtures.FixA).Reason == OutpostReasons.InvalidState, "StartTask in Ready.");
                    Require(s.Send(s.P1, new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Pause)).Reason == OutpostReasons.InvalidState, "Pause in Ready.");
                    s.Resume();
                    Require(s.Now.State == SessionState.Running, "Resume must reach Running.");
                    Require(s.Send(s.P1, new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Pause)).Accepted, "Pause in Running.");
                    Require(s.Start(s.P1, Phase02Fixtures.DockA, Phase02Fixtures.FixA).Reason == OutpostReasons.InvalidState, "StartTask in Paused.");
                    s.Finish();
                }),
                Check(p + "StartTask validation reasons", Kind, () =>
                {
                    var s = Open();
                    s.Resume();
                    long revision = s.Now.Revision;
                    Require(s.Start(s.P1, Phase02Fixtures.Missing, Phase02Fixtures.FixA).Reason == OutpostReasons.UnknownStation, "UnknownStation.");
                    Require(s.Start(s.P1, Phase02Fixtures.DockA, Phase02Fixtures.MissingTask).Reason == OutpostReasons.UnknownTaskType, "UnknownTaskType.");
                    Require(s.Start(s.P1, Phase02Fixtures.DockA, Phase02Fixtures.LabRun).Reason == OutpostReasons.TaskNotOfferedAtStation, "TaskNotOfferedAtStation.");
                    Require(s.Start(s.P1, Phase02Fixtures.FarLab, Phase02Fixtures.LabRun).Reason == OutpostReasons.OutOfRange, "OutOfRange (far-lab from spawn 0).");
                    Require(s.Start(s.P1, Phase02Fixtures.DockA, Phase02Fixtures.Overdraw).Reason == OutpostReasons.InsufficientResources, "InsufficientResources.");
                    Require(s.Now.Revision == revision, "Rejections must not change the revision.");
                    s.Finish();
                }),
                Check(p + "simultaneous requests: first wins, second StationOccupied; PlayerBusy; distinct tasks", Kind, () =>
                {
                    var s = Open();
                    s.Resume();
                    CommandResult first = s.Start(s.P1, Phase02Fixtures.DockA, Phase02Fixtures.FixA);
                    CommandResult second = s.Start(s.P2, Phase02Fixtures.DockA, Phase02Fixtures.FixA);
                    Require(first.Accepted, "First request: " + first.Reason + ".");
                    Require(second.Reason == OutpostReasons.StationOccupied, "Second request: " + second.Reason + ".");
                    Require(s.Station(Phase02Fixtures.DockA).Occupant == s.P1, "dock-a occupant must be P1.");
                    Require(s.Start(s.P2, Phase02Fixtures.DockB, Phase02Fixtures.FixB).Accepted, "P2 must start a different task at dock-b.");
                    Require(s.Start(s.P1, Phase02Fixtures.DockB, Phase02Fixtures.FixB).Reason == OutpostReasons.PlayerBusy, "P1 busy elsewhere must get PlayerBusy.");
                    Require(s.Station(Phase02Fixtures.DockB).Occupant == s.P2, "dock-b occupant must be P2.");
                    s.Finish();
                }),
                Check(p + "cancel: only the performer, no effect applied", Kind, () =>
                {
                    var s = Open();
                    s.Resume();
                    s.Start(s.P1, Phase02Fixtures.DockA, Phase02Fixtures.FixA);
                    Require(s.Cancel(s.P2, Phase02Fixtures.DockA).Reason == OutpostReasons.NoActiveTask, "Non-performer cancel.");
                    Require(s.Cancel(s.P1, Phase02Fixtures.DockA).Accepted, "Performer cancel.");
                    StationState st = s.Station(Phase02Fixtures.DockA);
                    Require(st.ActiveTask == null && st.LastEnd?.Reason == TaskEndReason.CancelledByPerformer, "dock-a must be free with CancelledByPerformer.");
                    if (harness.Advance != null) s.Step(Phase02Fixtures.FixADuration + 1);
                    Require(s.Sim.Applies.Count == 0, "Apply called after a cancel.");
                    s.Finish();
                }),
                Check(p + "completion applies the effect exactly once with the StartTask id; demand matches", Kind, () =>
                {
                    Timed();
                    var s = Open();
                    s.Resume();
                    var start = OutpostCommand.StartTask(Guid.NewGuid(), s.Now.Epoch, Phase02Fixtures.DockA, Phase02Fixtures.FixA);
                    Require(s.Send(s.P1, start).Accepted, "StartTask fix-a.");
                    int before = s.Sim.Advances.Count;
                    s.Step(Phase02Fixtures.FixADuration + 2);
                    StationState st = s.Station(Phase02Fixtures.DockA);
                    Require(st.LastEnd?.Reason == TaskEndReason.Completed, "fix-a must complete; LastEnd " + st.LastEnd?.Reason + ".");
                    Require(s.Sim.Applies.Count == 1, "Apply calls: " + s.Sim.Applies.Count + " (exactly 1 expected).");
                    Require(s.Sim.Applies[0].Id == start.Id, "ResourceTransfer.Id must be the StartTask command id.");
                    Require(s.Sim.Applies[0].Status == ResourceTransferStatus.Applied, "Apply status " + s.Sim.Applies[0].Status + ".");
                    var during = s.Sim.Advances.Skip(before).ToList();
                    Require(during.Count > 0, "The authority did not advance the simulation.");
                    Require(during.All(a => a.Demand.CrewAboard == 2), "Demand.CrewAboard must be 2.");
                    Require(during.Any(a => Math.Abs(a.Demand.ServiceLoadWatts - Phase02Fixtures.FixAPower) < 1e-9), "Support task power never requested.");
                    Require(during.All(a => a.Demand.ServiceLoadWatts <= Phase02Fixtures.FixAPower + 1e-9), "Service load above the active tasks.");
                    Require(during.All(a => !a.Demand.ScienceRequested), "Science requested without a science task.");
                    s.Step(5);
                    Require(s.Sim.Applies.Count == 1, "Apply repeated after completion.");
                    s.Finish();
                }),
                Check(p + "walk through the door, science task requests the science channel", Kind, () =>
                {
                    Timed();
                    var s = Open();
                    s.Resume();
                    s.WalkTo(s.P2, new BasePosition(4, 6.5));
                    s.WalkTo(s.P2, new BasePosition(10, 6.5));
                    s.WalkTo(s.P2, new BasePosition(10, 3));
                    CommandResult started = s.Start(s.P2, Phase02Fixtures.FarLab, Phase02Fixtures.LabRun);
                    Require(started.Accepted, "far-lab start after walking: " + started.Reason + ".");
                    int before = s.Sim.Advances.Count;
                    s.Step(1);
                    Require(s.Sim.Advances.Skip(before).All(a => a.Demand.ScienceRequested), "Science task must request the science channel.");
                    s.Finish();
                }),
                Check(p + "walking out of reach ends the task (PerformerMovedAway), no effect", Kind, () =>
                {
                    Timed();
                    var s = Open();
                    s.Resume();
                    s.Start(s.P1, Phase02Fixtures.DockA, Phase02Fixtures.FixA);
                    s.Move(s.P1, 0, 1);
                    s.Step(0.75);
                    s.Move(s.P1, 0, 0);
                    StationState st = s.Station(Phase02Fixtures.DockA);
                    Require(st.ActiveTask == null && st.LastEnd?.Reason == TaskEndReason.PerformerMovedAway, "Expected PerformerMovedAway, got " + st.LastEnd?.Reason + ".");
                    s.Step(Phase02Fixtures.FixADuration);
                    Require(s.Sim.Applies.Count == 0, "Apply called for an abandoned task.");
                    s.Finish();
                }),
                Check(p + "disconnect mid-task: station freed, PerformerLeft, no ghost, idempotent", Kind, () =>
                {
                    var s = Open();
                    s.Resume();
                    s.Start(s.P1, Phase02Fixtures.DockA, Phase02Fixtures.FixA);
                    Require(s.A.Leave(s.P1), "Leave must return true.");
                    Require(!s.A.Leave(s.P1), "Second Leave must return false.");
                    OutpostSessionSnapshot now = s.Now;
                    Require(now.Crew.Count == 1 && !now.TryGetCrew(s.P1, out _), "P1 must disappear from the crew.");
                    StationState st = s.Station(Phase02Fixtures.DockA);
                    Require(st.ActiveTask == null && st.LastEnd?.Reason == TaskEndReason.PerformerLeft, "Expected PerformerLeft.");
                    Require(s.A.SubmitMovement(s.P1, new MovementInput(now.Epoch, 50, new MoveDirection(1, 0))) == MovementInputDisposition.NotJoined, "Departed player's input must be NotJoined.");
                    Require(s.Start(s.P2, Phase02Fixtures.DockB, Phase02Fixtures.FixB).Accepted, "Remaining player keeps playing.");
                    if (harness.Advance != null)
                    {
                        s.Step(Phase02Fixtures.FixADuration + 1);
                        Require(s.Sim.Advances.Last().Demand.CrewAboard == 1, "Demand must count one crew member after the leave.");
                    }
                    Require(s.Sim.Applies.All(a => a.Effect.IsEmpty), "fix-a effect applied after its performer left.");
                    OutpostMembership again = s.A.Join();
                    Require(again.IsJoined && again.Player != s.P1 && again.Player != s.P2, "Rejoin must get a new id.");
                    s.Finish();
                }),
                Check(p + "restart: new epoch, tasks cleared without effects, stale commands rejected, ids kept", Kind, () =>
                {
                    var s = Open();
                    s.Resume();
                    var start = OutpostCommand.StartTask(Guid.NewGuid(), 0, Phase02Fixtures.DockA, Phase02Fixtures.FixA);
                    s.Send(s.P1, start);
                    int resets = s.Sim.Resets;
                    Require(s.Send(s.P2, new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Restart)).Accepted, "Restart.");
                    OutpostSessionSnapshot now = s.Now;
                    Require(now.Epoch == 1 && now.Revision == 0, "Restart must give (1, 0), got (" + now.Epoch + ", " + now.Revision + ").");
                    Require(now.Stations.All(st => st.ActiveTask == null && st.LastEnd == null), "Tasks must be cleared.");
                    Require(s.Sim.Resets == resets + 1, "Restart must Reset the simulation once.");
                    Require(now.TryGetCrew(s.P1, out _) && now.TryGetCrew(s.P2, out _), "Ids must be kept.");
                    Require(s.Start(s.P1, Phase02Fixtures.DockA, Phase02Fixtures.FixA, epoch: 0).Reason == OutpostReasons.EpochMismatch, "Old-epoch command.");
                    Require(s.Send(s.P1, start).Accepted, "Replaying a pre-restart command must reproduce its stored result.");
                    Require(s.Station(Phase02Fixtures.DockA).ActiveTask == null, "Replay must not re-apply the command.");
                    if (harness.Advance != null) s.Step(Phase02Fixtures.FixADuration + 1);
                    Require(s.Sim.Applies.Count == 0, "Apply called for a task cleared by restart.");
                    s.Finish();
                }),
                Check(p + "paused: held direction does not move anyone", Kind, () =>
                {
                    Timed();
                    var s = Open();
                    s.Resume();
                    s.Send(s.P1, new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Pause));
                    s.Now.TryGetCrew(s.P1, out var before);
                    s.Move(s.P1, 1, 0);
                    s.Step(1);
                    s.Now.TryGetCrew(s.P1, out var after);
                    Require(after.Pose.Position.Equals(before.Pose.Position), "Position advanced while paused.");
                    s.Finish();
                }),
            };
        }

        private sealed class Session
        {
            private readonly AuthorityHarness harness;
            private readonly Dictionary<PlayerId, long> sequences = new Dictionary<PlayerId, long>();
            public IOutpostAuthority A { get; }
            public RecordingOutpostSimulation Sim { get; }
            public OutpostScenario Scenario { get; } = Phase02Fixtures.Scenario();
            public OutpostSnapshotStreamValidator Stream { get; }
            public List<OutpostSessionSnapshot> Snapshots { get; } = new List<OutpostSessionSnapshot>();
            public List<(PlayerId Sender, CommandResult Result)> Results { get; } = new List<(PlayerId, CommandResult)>();
            public PlayerId P1 { get; }
            public PlayerId P2 { get; }

            public Session(AuthorityHarness harness)
            {
                this.harness = harness;
                Sim = new RecordingOutpostSimulation(harness.CreateSimulation() ?? throw new Phase02CheckFailed("Simulation factory returned null."));
                A = harness.Create(Scenario, Sim) ?? throw new Phase02CheckFailed("Authority factory returned null.");
                Stream = new OutpostSnapshotStreamValidator(Scenario);
                A.SnapshotChanged += s => { Snapshots.Add(s); Stream.Observe(s); };
                A.CommandCompleted += (sender, result) => Results.Add((sender, result));
                OutpostMembership a = A.Join(), b = A.Join();
                Require(a.IsJoined && b.IsJoined, "Two joins must succeed: " + a.Status + ", " + b.Status + ".");
                P1 = a.Player;
                P2 = b.Player;
            }

            public OutpostSessionSnapshot Now
            {
                get
                {
                    Require(A.TryGetSnapshot(out OutpostSessionSnapshot s), "No snapshot available.");
                    return s;
                }
            }

            public StationState Station(StationId id)
            {
                Require(Now.TryGetStation(id, out StationState st), "Station '" + id + "' missing.");
                return st;
            }

            public CommandResult Send(PlayerId sender, OutpostCommand command)
            {
                int before = Results.Count;
                A.Submit(sender, command);
                var mine = Results.Skip(before).Where(r => r.Result.CommandId == command.Id).ToList();
                Require(mine.Count == 1, "Expected exactly one result for " + command.Action + ", got " + mine.Count + ".");
                Require(mine[0].Sender == sender, "Result routed to " + mine[0].Sender + " instead of " + sender + ".");
                Require(mine[0].Result.Epoch == command.Epoch, "Result epoch must be the command's epoch.");
                return mine[0].Result;
            }

            public CommandResult Start(PlayerId who, StationId station, TaskTypeId task, long? epoch = null) =>
                Send(who, OutpostCommand.StartTask(Guid.NewGuid(), epoch ?? Now.Epoch, station, task));

            public CommandResult Cancel(PlayerId who, StationId station) =>
                Send(who, OutpostCommand.CancelTask(Guid.NewGuid(), Now.Epoch, station));

            public void Resume() => Require(Send(P1, new OutpostCommand(Guid.NewGuid(), Now.Epoch, OutpostAction.Resume)).Accepted, "Resume rejected.");

            public void Move(PlayerId who, double x, double y)
            {
                sequences.TryGetValue(who, out long seq);
                seq++;
                sequences[who] = seq;
                MovementInputDisposition d = A.SubmitMovement(who, new MovementInput(Now.Epoch, seq, MoveDirection.FromAxes(x, y)));
                Require(d == MovementInputDisposition.Applied, "Movement input " + seq + " was " + d + ".");
            }

            public void Step(double seconds, double dt = 0.05)
            {
                for (double t = 0; t < seconds - 1e-9; t += dt) harness.Advance(A, Math.Min(dt, seconds - t));
            }

            /// <summary>Walks in a straight line with held directions, as a client would, and stops.</summary>
            public void WalkTo(PlayerId who, BasePosition target)
            {
                for (int i = 0; i < 400; i++)
                {
                    Require(Now.TryGetCrew(who, out CrewMemberState m), who + " missing.");
                    double dx = target.X - m.Pose.Position.X, dy = target.Y - m.Pose.Position.Y;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < 0.02) { Move(who, 0, 0); return; }
                    double scale = Math.Min(1, d / (Scenario.Movement.MaxSpeedMetersPerSecond * 0.05));
                    Move(who, dx / d * scale, dy / d * scale);
                    harness.Advance(A, 0.05);
                }
                throw new Phase02CheckFailed(who + " could not walk to " + target + " (blocked or not moving).");
            }

            public void Finish()
            {
                Require(Stream.Report.IsValid, "Snapshot stream: " + Stream.Report);
            }
        }
    }
}
