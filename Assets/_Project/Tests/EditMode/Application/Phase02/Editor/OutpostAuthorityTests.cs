using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Application.Outpost;

namespace Trainer.Tests.Phase02
{
    /// <summary>D2 authority: validation order, reservations, progress, costs, ends, restart and mission result.
    /// Runs against test doubles of D1 (simulation) and D3 (roster) over the frozen synthetic fixture scenario.</summary>
    public sealed class OutpostAuthorityTests
    {
        private static readonly BasePosition NearWater = new BasePosition(3, 7.2);
        private static readonly BasePosition NearWater2 = new BasePosition(3.8, 7.5);
        private static readonly BasePosition NearBench = new BasePosition(13, 7.2);
        private static readonly BasePosition NearAir = new BasePosition(13, 2.8);

        private OutpostScenario scenario;
        private ScriptedOutpostSimulation simulation;
        private ScriptedCrewRoster roster;
        private OutpostAuthority authority;
        private List<OutpostSessionSnapshot> snapshots;
        private List<(PlayerId sender, CommandResult result)> results;

        private static OutpostMissionRules Rules(double duration = 240) => new OutpostMissionRules(duration, new[]
        {
            new OutpostObjectiveRule("treat-water", new[] { OutpostFixtures.TreatWater }, 2, true),
            new OutpostObjectiveRule("science", new[] { OutpostFixtures.RunExperiment }, 1, false)
        });

        [SetUp]
        public void SetUp() => Create(Rules());

        private void Create(OutpostMissionRules rules, double maxStep = OutpostAuthority.DefaultMaxStepSeconds)
        {
            scenario = OutpostFixtures.Scenario();
            simulation = new ScriptedOutpostSimulation();
            roster = new ScriptedCrewRoster(scenario);
            authority = new OutpostAuthority(scenario, rules, simulation, roster, maxStep);
            snapshots = new List<OutpostSessionSnapshot>();
            results = new List<(PlayerId, CommandResult)>();
            authority.SnapshotChanged += s => snapshots.Add(s);
            authority.CommandCompleted += (p, r) => results.Add((p, r));
        }

        private OutpostSessionSnapshot State
        {
            get
            {
                authority.TryGetSnapshot(out OutpostSessionSnapshot s);
                return s;
            }
        }

        private PlayerId JoinAt(BasePosition? position = null)
        {
            OutpostMembership m = authority.Join();
            Assert.That(m.IsJoined, Is.True);
            if (position.HasValue) roster.Place(m.Player, position.Value);
            return m.Player;
        }

        private CommandResult Send(PlayerId sender, OutpostCommand command)
        {
            int before = results.Count;
            authority.Submit(sender, command);
            Assert.That(results.Count, Is.EqualTo(before + 1), "exactly one result per Submit");
            Assert.That(results[before].sender, Is.EqualTo(sender), "result routed to its sender");
            return results[before].result;
        }

        private CommandResult Start(PlayerId sender, StationId station, TaskTypeId task, Guid? id = null) =>
            Send(sender, OutpostCommand.StartTask(id ?? Guid.NewGuid(), State.Epoch, station, task));

        private CommandResult Resume(PlayerId sender) =>
            Send(sender, new OutpostCommand(Guid.NewGuid(), State.Epoch, OutpostAction.Resume));

        private void Run(double seconds, double frame = 0.1)
        {
            for (double t = 0; t < seconds - 1e-9; t += frame) authority.Tick(Math.Min(frame, seconds - t));
        }

        private StationState Station(StationId id)
        {
            Assert.That(State.TryGetStation(id, out StationState s), Is.True);
            return s;
        }

        // ------------------------------------------------------------ construction and presence

        [Test]
        public void StartsReadyAtEpochZeroWithEveryStationFreeAndObjectivesAtZero()
        {
            OutpostSessionSnapshot s = State;
            Assert.That(s.State, Is.EqualTo(SessionState.Ready));
            Assert.That((s.Epoch, s.Revision), Is.EqualTo((0L, 0L)));
            Assert.That(s.Outcome, Is.EqualTo(OutpostOutcome.InProgress));
            Assert.That(s.Stations.Select(x => x.Station), Is.EqualTo(scenario.Layout.Stations.Select(x => x.Id)));
            Assert.That(s.Stations.All(x => x.Availability == StationAvailability.Available && x.LastEnd == null));
            Assert.That(s.Objectives.Select(o => (o.Key, o.Current, o.Target, o.Required)),
                Is.EqualTo(new[] { ("treat-water", 0d, 2d, true), ("science", 0d, 1d, false) }));
            Assert.That(simulation.ResetCount, Is.EqualTo(1));
        }

        [Test]
        public void RefusesRulesThatCountUnknownTasksAndNonEmptyRoster()
        {
            var bad = new OutpostMissionRules(60, new[] { new OutpostObjectiveRule("x", new[] { new TaskTypeId("nope") }, 1, true) });
            Assert.Throws<ArgumentException>(() => new OutpostAuthority(scenario, bad, new ScriptedOutpostSimulation(), new ScriptedCrewRoster(scenario)));
            var busyRoster = new ScriptedCrewRoster(scenario);
            busyRoster.Join();
            Assert.Throws<ArgumentException>(() => new OutpostAuthority(scenario, Rules(), new ScriptedOutpostSimulation(), busyRoster));
        }

        [Test]
        public void JoinPublishesTheNewMemberAtItsSpawnAndRejectsWhenFull()
        {
            PlayerId a = JoinAt();
            Assert.That(snapshots.Count, Is.EqualTo(1));
            Assert.That(State.Revision, Is.EqualTo(1));
            Assert.That(State.TryGetCrew(a, out CrewMemberState member), Is.True);
            Assert.That(member.Pose.Position, Is.EqualTo(scenario.Layout.SpawnPoints[0]));
            for (int i = 1; i < scenario.Layout.MaxCrew; i++) JoinAt();
            OutpostMembership full = authority.Join();
            Assert.That(full.Status, Is.EqualTo(MembershipStatus.Rejected));
            Assert.That(full.Reason, Is.EqualTo(OutpostReasons.SessionFull));
            Assert.That(State.Revision, Is.EqualTo(scenario.Layout.MaxCrew), "a refused join publishes nothing");
        }

        [Test]
        public void MovementInputIsRecordedWithoutPublishingAndUsesTheCurrentEpoch()
        {
            PlayerId a = JoinAt();
            int published = snapshots.Count;
            Assert.That(authority.SubmitMovement(a, new MovementInput(0, 1, MoveDirection.FromAxes(1, 0))),
                Is.EqualTo(MovementInputDisposition.Applied));
            Assert.That(authority.SubmitMovement(a, new MovementInput(0, 1, MoveDirection.Zero)),
                Is.EqualTo(MovementInputDisposition.StaleSequence));
            Assert.That(authority.SubmitMovement(a, new MovementInput(3, 2, MoveDirection.Zero)),
                Is.EqualTo(MovementInputDisposition.EpochMismatch));
            Assert.That(authority.SubmitMovement(new PlayerId(99), new MovementInput(0, 5, MoveDirection.Zero)),
                Is.EqualTo(MovementInputDisposition.NotJoined));
            Assert.That(snapshots.Count, Is.EqualTo(published));
            authority.Tick(1);
            Assert.That(roster.AdvanceCalls, Is.EqualTo(0), "nobody moves before Running");
        }

        [Test]
        public void CrewMovesOnlyWhileRunning()
        {
            PlayerId a = JoinAt();
            Assert.That(Resume(a).Accepted, Is.True);
            authority.SubmitMovement(a, new MovementInput(0, 1, MoveDirection.FromAxes(1, 0)));
            authority.Tick(0.5);
            State.TryGetCrew(a, out CrewMemberState moved);
            Assert.That(moved.Pose.Position.X, Is.EqualTo(scenario.Layout.SpawnPoints[0].X + 1.0).Within(1e-9));
            Assert.That(moved.LastInputSequence, Is.EqualTo(1));
            Send(a, new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Pause));
            authority.Tick(0.5);
            State.TryGetCrew(a, out CrewMemberState paused);
            Assert.That(paused.Pose.Position, Is.EqualTo(moved.Pose.Position));
        }

        // ------------------------------------------------------------ validation order

        [Test]
        public void ValidationFollowsTheContractOrder()
        {
            PlayerId a = JoinAt(NearWater);
            int revision = (int)State.Revision;

            Assert.That(Send(a, new OutpostCommand(Guid.Empty, 0, OutpostAction.Pause)).Reason, Is.EqualTo(OutpostReasons.InvalidCommandId));
            Assert.That(Send(new PlayerId(42), new OutpostCommand(Guid.NewGuid(), 7, (OutpostAction)99)).Reason, Is.EqualTo(OutpostReasons.NotJoined));
            Assert.That(Send(a, new OutpostCommand(Guid.NewGuid(), 7, (OutpostAction)99)).Reason, Is.EqualTo(OutpostReasons.EpochMismatch));
            Assert.That(Send(a, new OutpostCommand(Guid.NewGuid(), 0, (OutpostAction)99)).Reason, Is.EqualTo(OutpostReasons.UnknownAction));
            Assert.That(Send(a, new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Pause, OutpostFixtures.WaterProcessor)).Reason,
                Is.EqualTo(OutpostReasons.InvalidActionValue));
            Assert.That(Send(a, new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Pause)).Reason, Is.EqualTo(OutpostReasons.InvalidState));
            Assert.That(Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater).Reason, Is.EqualTo(OutpostReasons.InvalidState));
            Assert.That(State.Revision, Is.EqualTo(revision), "rejections never publish");

            Assert.That(Resume(a).Accepted, Is.True);
            Assert.That(Resume(a).Reason, Is.EqualTo(OutpostReasons.InvalidState));
            Assert.That(Start(a, new StationId("nowhere"), OutpostFixtures.TreatWater).Reason, Is.EqualTo(OutpostReasons.UnknownStation));
            Assert.That(Start(a, OutpostFixtures.WaterProcessor, new TaskTypeId("nothing")).Reason, Is.EqualTo(OutpostReasons.UnknownTaskType));
            Assert.That(Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.RunExperiment).Reason,
                Is.EqualTo(OutpostReasons.TaskNotOfferedAtStation));
            Assert.That(Start(a, OutpostFixtures.ScienceBench, OutpostFixtures.RunExperiment).Reason, Is.EqualTo(OutpostReasons.OutOfRange));
            Assert.That(Send(a, OutpostCommand.CancelTask(Guid.NewGuid(), 0, OutpostFixtures.WaterProcessor)).Reason,
                Is.EqualTo(OutpostReasons.NoActiveTask));
            Assert.That(Send(a, OutpostCommand.CancelTask(Guid.NewGuid(), 0, new StationId("nowhere"))).Reason,
                Is.EqualTo(OutpostReasons.UnknownStation));

            simulation.SetUntreated(1.0);   // treat-water needs 2 kg untreated
            Assert.That(Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater).Reason,
                Is.EqualTo(OutpostReasons.InsufficientResources));
        }

        [Test]
        public void OutOfRangeUsesTheConfirmedPositionNotTheCommand()
        {
            PlayerId a = JoinAt();   // at spawn (3, 3), 5 m from the water processor
            Resume(a);
            Assert.That(Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater).Reason, Is.EqualTo(OutpostReasons.OutOfRange));
            roster.Place(a, NearWater);
            Assert.That(Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater).Accepted, Is.True);
        }

        // ------------------------------------------------------------ reservations

        [Test]
        public void ExclusiveStationFirstArrivalWinsSecondGetsStationOccupied()
        {
            PlayerId a = JoinAt(NearWater), b = JoinAt(NearWater2);
            Resume(a);
            CommandResult first = Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            CommandResult second = Start(b, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            Assert.That(first.Accepted, Is.True);
            Assert.That(second.Reason, Is.EqualTo(OutpostReasons.StationOccupied));
            StationState water = Station(OutpostFixtures.WaterProcessor);
            Assert.That(water.Availability, Is.EqualTo(StationAvailability.Occupied));
            Assert.That(water.Occupant, Is.EqualTo(a));
        }

        [Test]
        public void StationOccupiedIsReportedBeforeOutOfRange()
        {
            PlayerId a = JoinAt(NearWater), b = JoinAt();   // b stays at its spawn, out of reach
            Resume(a);
            Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            Assert.That(Start(b, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater).Reason, Is.EqualTo(OutpostReasons.StationOccupied));
        }

        [Test]
        public void APersonCannotRunTwoTasksAtOnce()
        {
            // Bench and air monitor are 5.2 m apart: place A within reach of the air monitor after starting at the bench.
            PlayerId a = JoinAt(NearBench);
            Resume(a);
            Assert.That(Start(a, OutpostFixtures.ScienceBench, OutpostFixtures.RunExperiment).Accepted, Is.True);
            Assert.That(Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater).Reason, Is.EqualTo(OutpostReasons.PlayerBusy));
            Assert.That(Start(a, OutpostFixtures.ScienceBench, OutpostFixtures.RunExperiment).Reason, Is.EqualTo(OutpostReasons.PlayerBusy),
                "PlayerBusy is checked before StationOccupied");
            Assert.That(State.Stations.Count(s => s.Occupant == a), Is.EqualTo(1));
        }

        [Test]
        public void CommandIdsAreDeduplicatedPerSenderAndContent()
        {
            PlayerId a = JoinAt(NearWater), b = JoinAt(NearWater2);
            Resume(a);
            Guid id = Guid.NewGuid();
            OutpostCommand start = OutpostCommand.StartTask(id, 0, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            CommandResult first = Send(a, start);
            long revision = State.Revision;
            int published = snapshots.Count;

            CommandResult replay = Send(a, OutpostCommand.StartTask(id, 0, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater));
            Assert.That(replay, Is.SameAs(first), "an exact replay reproduces the stored result");
            Assert.That(State.Revision, Is.EqualTo(revision));
            Assert.That(snapshots.Count, Is.EqualTo(published));

            Assert.That(Send(a, OutpostCommand.CancelTask(id, 0, OutpostFixtures.WaterProcessor)).Reason, Is.EqualTo(OutpostReasons.CommandIdConflict));
            Assert.That(Send(b, start).Reason, Is.EqualTo(OutpostReasons.CommandIdConflict), "same id from another sender");
            Assert.That(Send(a, start), Is.SameAs(first), "conflicts are not stored");
        }

        [Test]
        public void RejectedCommandsAreStoredButInvalidIdsAreNot()
        {
            PlayerId a = JoinAt();
            Guid id = Guid.NewGuid();
            CommandResult rejected = Send(a, new OutpostCommand(id, 0, OutpostAction.Pause));
            Assert.That(rejected.Reason, Is.EqualTo(OutpostReasons.InvalidState));
            Resume(a);
            Assert.That(Send(a, new OutpostCommand(id, 0, OutpostAction.Pause)), Is.SameAs(rejected),
                "the stored rejection is replayed even though Pause would now be valid");
        }

        // ------------------------------------------------------------ progress, costs and ends

        [Test]
        public void SupportTaskProgressesWithServedServiceAndAppliesItsEffectOnce()
        {
            PlayerId a = JoinAt(NearWater);
            Resume(a);
            Guid id = Guid.NewGuid();
            Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater, id);
            double potable = State.Resources.LifeSupport.Water.PotableKilograms;

            Run(10);
            TaskProgress half = Station(OutpostFixtures.WaterProcessor).ActiveTask;
            Assert.That(half.WorkSeconds, Is.EqualTo(10).Within(1e-6));
            Assert.That(half.Stalled, Is.False);
            Assert.That(simulation.Demands.Last().ServiceLoadWatts, Is.EqualTo(150), "the task's watts join the service load");
            Assert.That(simulation.Demands.Last().ScienceRequested, Is.False);
            Assert.That(simulation.Demands.Last().CrewAboard, Is.EqualTo(1));
            Assert.That(simulation.ApplyCalls, Is.Empty);

            Run(10.5);
            StationState done = Station(OutpostFixtures.WaterProcessor);
            Assert.That(done.ActiveTask, Is.Null);
            Assert.That(done.LastEnd.Reason, Is.EqualTo(TaskEndReason.Completed));
            Assert.That(done.LastEnd.Performer, Is.EqualTo(a));
            Assert.That(done.LastEnd.EndedAtSeconds, Is.EqualTo(20).Within(1e-6), "steps are trimmed to the remaining work");
            Assert.That(simulation.ApplyCalls.Count, Is.EqualTo(1));
            Assert.That(simulation.ApplyCalls[0].Id, Is.EqualTo(id), "transfer id = StartTask command id");
            Assert.That(State.Resources.LifeSupport.Water.PotableKilograms, Is.EqualTo(potable + 1.8).Within(1e-9));
            Assert.That(State.Objectives[0].Current, Is.EqualTo(1));
            Assert.That(simulation.Demands.Last().ServiceLoadWatts, Is.EqualTo(0), "no load after completion");
        }

        [Test]
        public void UnservedServiceStallsTheTaskWithoutProgress()
        {
            PlayerId a = JoinAt(NearWater);
            Resume(a);
            Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            simulation.ServiceServed = false;
            Run(5);
            TaskProgress stalled = Station(OutpostFixtures.WaterProcessor).ActiveTask;
            Assert.That(stalled.WorkSeconds, Is.EqualTo(0));
            Assert.That(stalled.Stalled, Is.True);
            simulation.ServiceServed = true;
            Run(1);
            Assert.That(Station(OutpostFixtures.WaterProcessor).ActiveTask.Stalled, Is.False);
            Assert.That(Station(OutpostFixtures.WaterProcessor).ActiveTask.WorkSeconds, Is.EqualTo(1).Within(1e-6));
        }

        [Test]
        public void ScienceUsesThePhaseOneScienceChannel()
        {
            PlayerId a = JoinAt(NearBench);
            Resume(a);
            Start(a, OutpostFixtures.ScienceBench, OutpostFixtures.RunExperiment);
            simulation.ScienceServed = false;
            Run(3);
            Assert.That(simulation.Demands.Last().ScienceRequested, Is.True);
            Assert.That(simulation.Demands.Last().ServiceLoadWatts, Is.EqualTo(0));
            Assert.That(Station(OutpostFixtures.ScienceBench).ActiveTask.Stalled, Is.True);
            simulation.ScienceServed = true;
            Run(30);
            Assert.That(Station(OutpostFixtures.ScienceBench).LastEnd.Reason, Is.EqualTo(TaskEndReason.Completed));
            Assert.That(simulation.ApplyCalls, Is.Empty, "an empty effect needs no Apply");
            Assert.That(State.Objectives[1].Current, Is.EqualTo(1));
            Assert.That(State.State, Is.EqualTo(SessionState.Running), "optional objectives never end the mission");
        }

        [Test]
        public void ZeroWattSupportProgressesWithElapsedTimeEvenWhenServiceIsShort()
        {
            PlayerId a = JoinAt(NearAir), b = JoinAt(NearWater);
            Resume(a);
            Start(a, OutpostFixtures.AirMonitor, OutpostFixtures.CheckCabinAir);
            Start(b, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            simulation.ServiceServed = false;
            Run(4);
            Assert.That(Station(OutpostFixtures.AirMonitor).ActiveTask.WorkSeconds, Is.EqualTo(4).Within(1e-6));
            Assert.That(Station(OutpostFixtures.WaterProcessor).ActiveTask.WorkSeconds, Is.EqualTo(0));
        }

        [Test]
        public void WalkingAwayEndsTheTaskWithoutEffect()
        {
            PlayerId a = JoinAt(NearWater);
            Resume(a);
            Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            Run(5);
            authority.SubmitMovement(a, new MovementInput(0, 1, MoveDirection.FromAxes(0, -1)));
            Run(2);
            StationState water = Station(OutpostFixtures.WaterProcessor);
            Assert.That(water.ActiveTask, Is.Null);
            Assert.That(water.Availability, Is.EqualTo(StationAvailability.Available));
            Assert.That(water.LastEnd.Reason, Is.EqualTo(TaskEndReason.PerformerMovedAway));
            Assert.That(simulation.ApplyCalls, Is.Empty);
            Assert.That(State.Objectives[0].Current, Is.EqualTo(0));
        }

        [Test]
        public void CancelEndsOnlyTheSendersOwnTask()
        {
            PlayerId a = JoinAt(NearWater), b = JoinAt(NearWater2);
            Resume(a);
            Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            Run(3);
            Assert.That(Send(b, OutpostCommand.CancelTask(Guid.NewGuid(), 0, OutpostFixtures.WaterProcessor)).Reason,
                Is.EqualTo(OutpostReasons.NoActiveTask));
            Send(a, new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Pause));
            Assert.That(Send(a, OutpostCommand.CancelTask(Guid.NewGuid(), 0, OutpostFixtures.WaterProcessor)).Accepted, Is.True,
                "cancel is allowed while Paused");
            StationState water = Station(OutpostFixtures.WaterProcessor);
            Assert.That(water.LastEnd.Reason, Is.EqualTo(TaskEndReason.CancelledByPerformer));
            Assert.That(water.LastEnd.EndedAtSeconds, Is.EqualTo(3).Within(1e-6));
            Assert.That(simulation.ApplyCalls, Is.Empty);
            Resume(a);
            Assert.That(Start(b, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater).Accepted, Is.True, "station free again");
        }

        [Test]
        public void LeavingFreesTheStationAndTheMemberWithoutOrphans()
        {
            PlayerId a = JoinAt(NearWater), b = JoinAt(NearWater2);
            Resume(a);
            Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            Run(3);
            Assert.That(authority.Leave(a), Is.True);
            OutpostSessionSnapshot s = State;
            Assert.That(s.TryGetCrew(a, out _), Is.False);
            Assert.That(s.Crew.Count, Is.EqualTo(1));
            StationState water = Station(OutpostFixtures.WaterProcessor);
            Assert.That(water.ActiveTask, Is.Null);
            Assert.That(water.LastEnd.Reason, Is.EqualTo(TaskEndReason.PerformerLeft));
            Run(1);
            Assert.That(simulation.Demands.Last().ServiceLoadWatts, Is.EqualTo(0), "no load left without a responsible member");
            Assert.That(simulation.Demands.Last().CrewAboard, Is.EqualTo(1));
            Assert.That(simulation.ApplyCalls, Is.Empty);
            long revision = State.Revision;
            Assert.That(authority.Leave(a), Is.False, "Leave is idempotent");
            Assert.That(State.Revision, Is.EqualTo(revision));
            Assert.That(Start(b, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater).Accepted, Is.True);
            Assert.That(Send(a, new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Pause)).Reason, Is.EqualTo(OutpostReasons.NotJoined));
        }

        [Test]
        public void CompletionRefusedBySimulationEndsWithInsufficientResourcesAndCountsNothing()
        {
            PlayerId a = JoinAt(NearWater);
            Resume(a);
            Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            Run(10);
            simulation.SetUntreated(0.5);   // consumed elsewhere meanwhile
            Run(11);
            StationState water = Station(OutpostFixtures.WaterProcessor);
            Assert.That(water.LastEnd.Reason, Is.EqualTo(TaskEndReason.InsufficientResources));
            Assert.That(simulation.ApplyCalls.Count, Is.EqualTo(1));
            Assert.That(State.Resources.LifeSupport.Water.UntreatedKilograms, Is.EqualTo(0.5), "all or nothing");
            Assert.That(State.Objectives[0].Current, Is.EqualTo(0));
        }

        // ------------------------------------------------------------ mission result and restart

        [Test]
        public void RequiredObjectivesEndTheMissionAndCloseOtherTasks()
        {
            PlayerId a = JoinAt(NearWater), b = JoinAt(NearBench);
            Resume(a);
            Start(b, OutpostFixtures.ScienceBench, OutpostFixtures.RunExperiment);
            Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            Run(20.5);
            Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            Run(25);
            OutpostSessionSnapshot s = State;
            Assert.That(s.State, Is.EqualTo(SessionState.Completed));
            Assert.That(s.Outcome, Is.EqualTo(OutpostOutcome.ObjectiveAchieved));
            Assert.That(s.SimulatedTimeSeconds, Is.EqualTo(40.5).Within(1e-6), "second treatment: started at 20.5 s, 20 s of work");
            Assert.That(Station(OutpostFixtures.ScienceBench).LastEnd.Reason, Is.EqualTo(TaskEndReason.Completed));
            Assert.That(s.Objectives.Select(o => o.Current), Is.EqualTo(new[] { 2d, 1d }));
            Assert.That(s.Stations.All(x => x.ActiveTask == null));
        }

        [Test]
        public void TasksStillRunningAtTheEndCloseWithMissionEnded()
        {
            PlayerId a = JoinAt(NearWater), b = JoinAt(NearBench);
            Resume(a);
            Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            Run(20.5);
            Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            Run(5);
            Start(b, OutpostFixtures.ScienceBench, OutpostFixtures.RunExperiment);
            Run(20);
            Assert.That(State.Outcome, Is.EqualTo(OutpostOutcome.ObjectiveAchieved));
            Assert.That(Station(OutpostFixtures.ScienceBench).LastEnd.Reason, Is.EqualTo(TaskEndReason.MissionEnded));
            Assert.That(State.Objectives[1].Current, Is.EqualTo(0));
            Assert.That(Start(b, OutpostFixtures.ScienceBench, OutpostFixtures.RunExperiment).Reason, Is.EqualTo(OutpostReasons.SessionEnded));
            Assert.That(Resume(a).Reason, Is.EqualTo(OutpostReasons.SessionEnded));
        }

        [Test]
        public void TimeLimitEndsWithTimeExpired()
        {
            Create(Rules(duration: 12));
            PlayerId a = JoinAt(NearWater);
            Resume(a);
            Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            Run(15);
            Assert.That(State.Outcome, Is.EqualTo(OutpostOutcome.TimeExpired));
            Assert.That(State.SimulatedTimeSeconds, Is.EqualTo(12).Within(1e-6), "the clock stops at the duration");
            Assert.That(Station(OutpostFixtures.WaterProcessor).LastEnd.Reason, Is.EqualTo(TaskEndReason.MissionEnded));
            Assert.That(simulation.ApplyCalls, Is.Empty);
        }

        [Test]
        public void EssentialLossInterrupts()
        {
            PlayerId a = JoinAt(NearWater);
            Resume(a);
            Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            Run(2);
            simulation.EssentialServed = false;
            Run(1);
            Assert.That(State.State, Is.EqualTo(SessionState.Interrupted));
            Assert.That(State.Outcome, Is.EqualTo(OutpostOutcome.EssentialServiceLost));
            Assert.That(State.Stations.All(s => s.ActiveTask == null));
        }

        [Test]
        public void SimulationFaultInterruptsRethrowsAndOnlyRestartIsAccepted()
        {
            PlayerId a = JoinAt(NearWater);
            Resume(a);
            Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            Run(2);
            simulation.ThrowOnAdvance = true;
            Assert.Throws<InvalidOperationException>(() => authority.Tick(0.1));
            Assert.That(State.State, Is.EqualTo(SessionState.Interrupted));
            Assert.That(State.Outcome, Is.EqualTo(OutpostOutcome.SimulationFault));
            Assert.That(Station(OutpostFixtures.WaterProcessor).LastEnd.Reason, Is.EqualTo(TaskEndReason.MissionEnded));
            Assert.That(snapshots.Last(), Is.SameAs(State), "the fault was published");
            Assert.That(Resume(a).Reason, Is.EqualTo(OutpostReasons.SessionEnded));
            simulation.ThrowOnAdvance = false;
            Assert.That(Send(a, new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Restart)).Accepted, Is.True);
            Assert.That(State.State, Is.EqualTo(SessionState.Ready));
        }

        [Test]
        public void FailedRestartInterruptsWithSimulationFault()
        {
            PlayerId a = JoinAt(NearWater);
            Resume(a);
            Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            simulation.ThrowOnReset = true;
            Guid id = Guid.NewGuid();
            Assert.Throws<InvalidOperationException>(() => authority.Submit(a, new OutpostCommand(id, 0, OutpostAction.Restart)));
            Assert.That(results.Last().result.Reason, Is.EqualTo(OutpostReasons.SimulationFault));
            Assert.That(State.Outcome, Is.EqualTo(OutpostOutcome.SimulationFault));
            Assert.That(State.Stations.All(s => s.ActiveTask == null));
            int resets = simulation.ResetCount;
            simulation.ThrowOnReset = false;
            authority.Submit(a, new OutpostCommand(id, 0, OutpostAction.Restart));
            Assert.That(simulation.ResetCount, Is.EqualTo(resets), "a replay does not reset again");
        }

        [Test]
        public void RestartStartsANewEpochAndErasesTasksWithoutEffects()
        {
            PlayerId a = JoinAt(NearWater), b = JoinAt(NearBench);
            Resume(a);
            Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            Run(20.5);
            Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            Start(b, OutpostFixtures.ScienceBench, OutpostFixtures.RunExperiment);
            authority.SubmitMovement(b, new MovementInput(0, 3, MoveDirection.Zero));
            Run(2);
            int applies = simulation.ApplyCalls.Count;

            OutpostCommand restart = new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Restart);
            CommandResult result = Send(b, restart);
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Epoch, Is.EqualTo(0), "the result keeps the command's epoch");
            OutpostSessionSnapshot s = State;
            Assert.That((s.Epoch, s.Revision, s.State), Is.EqualTo((1L, 0L, SessionState.Ready)));
            Assert.That(s.SimulatedTimeSeconds, Is.EqualTo(0));
            Assert.That(s.Stations.All(x => x.ActiveTask == null && x.LastEnd == null), "no LastEnd after Restart");
            Assert.That(s.Objectives.All(o => o.Current == 0));
            Assert.That(simulation.ResetCount, Is.EqualTo(2));
            Assert.That(simulation.ApplyCalls.Count, Is.EqualTo(applies), "nothing applied by Restart");
            Assert.That(s.Crew.Select(c => c.Player), Is.EqualTo(new[] { a, b }), "ids and slots kept");
            Assert.That(s.Crew.All(c => c.Pose.Position.Equals(scenario.Layout.SpawnPoints[c.Slot]) && c.Pose.Motion == CrewMotion.Idle));
            s.TryGetCrew(b, out CrewMemberState kept);
            Assert.That(kept.LastInputSequence, Is.EqualTo(3), "sequences survive Restart");

            Assert.That(Send(a, new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Resume)).Reason, Is.EqualTo(OutpostReasons.EpochMismatch));
            Assert.That(Send(b, restart), Is.SameAs(result), "replaying the restart does not restart again");
            Assert.That(State.Epoch, Is.EqualTo(1));
        }

        [Test]
        public void EverySnapshotIsNewerThanThePreviousOne()
        {
            PlayerId a = JoinAt(NearWater);
            Resume(a);
            Start(a, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
            Run(3);
            authority.Leave(a);
            PlayerId b = JoinAt();
            Send(b, new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Restart));
            for (int i = 1; i < snapshots.Count; i++)
                Assert.That(snapshots[i].IsNewerThan(snapshots[i - 1]), Is.True, $"snapshot {i}");
        }

        [Test]
        public void SubscribersSeeCommittedStateAndMayCallBack()
        {
            PlayerId a = JoinAt(NearWater);
            Resume(a);
            bool calledBack = false;
            authority.SnapshotChanged += s =>
            {
                if (calledBack || s.Stations.All(x => x.ActiveTask == null)) return;
                calledBack = true;
                Assert.That(State, Is.SameAs(s), "state committed before the event");
                authority.Submit(a, OutpostCommand.StartTask(Guid.NewGuid(), 0, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater));
            };
            int before = results.Count;
            authority.Submit(a, OutpostCommand.StartTask(Guid.NewGuid(), 0, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater));
            Assert.That(calledBack, Is.True);
            Assert.That(results.Skip(before).Select(r => r.result.Reason),
                Is.EqualTo(new[] { OutpostReasons.PlayerBusy, OutpostReasons.Accepted }),
                "the nested command sees the committed reservation");
        }
    }
}
