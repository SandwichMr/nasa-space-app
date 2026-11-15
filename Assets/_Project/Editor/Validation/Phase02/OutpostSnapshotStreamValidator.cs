using System;
using System.Collections.Generic;
using System.Linq;
using Trainer.Application;
using Trainer.Application.Outpost;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Tooling.Validation.Phase02
{
    /// <summary>
    /// Checks authority snapshots one by one (state rules) and as a stream (transition rules) against the
    /// scenario. It observes only what a client receives, so it can run on the host, a client or a log replay.
    /// It never corrects or produces state.
    ///
    /// What a stream cannot know: the crew demand model of D1. So exact mass accounting is only asserted for
    /// transitions with no simulated time (Δt = 0), where stocks may change ONLY by completion effects; the
    /// "applied once" rule for steps with time is covered by the authority conformance spy.
    /// </summary>
    public sealed class OutpostSnapshotStreamValidator
    {
        public const double PositionTolerance = 1e-6;
        public const double MassTolerance = 1e-6;

        private readonly OutpostScenario scenario;
        private OutpostSessionSnapshot previous;
        private int maxPlayerSeen;
        private readonly HashSet<PlayerId> everSeen = new HashSet<PlayerId>();
        private readonly HashSet<PlayerId> departed = new HashSet<PlayerId>();
        private readonly Dictionary<PlayerId, long> lastSequence = new Dictionary<PlayerId, long>();

        public int Count { get; private set; }
        public Phase02Report Report { get; } = new Phase02Report();

        public OutpostSnapshotStreamValidator(OutpostScenario scenario)
        {
            this.scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
        }

        /// <summary>Validates <paramref name="next"/> and its transition from the last observed snapshot.
        /// Returns only the issues of this call (they are also accumulated in <see cref="Report"/>).</summary>
        public Phase02Report Observe(OutpostSessionSnapshot next)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            var report = ValidateState(next, scenario);
            if (previous != null) ValidateTransition(previous, next, report);
            foreach (CrewMemberState member in next.Crew)
            {
                if (departed.Contains(member.Player))
                    report.Error("PlayerIdReused", member.Player + " left earlier and appears again.");
                else if (!everSeen.Contains(member.Player) && member.Player.Value <= maxPlayerSeen)
                    report.Error("PlayerIdReused", member.Player + " is new but not above the highest id seen (" + maxPlayerSeen + ").");
                everSeen.Add(member.Player);
                if (member.Player.Value > maxPlayerSeen) maxPlayerSeen = member.Player.Value;
                if (lastSequence.TryGetValue(member.Player, out long last) && member.LastInputSequence < last)
                    report.Error("InputSequenceRegressed", member.Player + " sequence " + last + " → " + member.LastInputSequence + ".");
                lastSequence[member.Player] = member.LastInputSequence;
            }
            if (previous != null)
                foreach (CrewMemberState gone in previous.Crew)
                    if (!next.TryGetCrew(gone.Player, out _))
                    {
                        departed.Add(gone.Player);
                        lastSequence.Remove(gone.Player);
                    }
            previous = next;
            Count++;
            Report.Merge(report);
            return report;
        }

        /// <summary>Rules for a single snapshot given the scenario.</summary>
        public static Phase02Report ValidateState(OutpostSessionSnapshot s, OutpostScenario scenario)
        {
            var report = new Phase02Report();
            OutpostLayout layout = scenario.Layout;
            double radius = scenario.Movement.CrewRadiusMeters;
            if (s.ScenarioId != scenario.Id)
                report.Error("ScenarioMismatch", "Snapshot scenario '" + s.ScenarioId + "' is not '" + scenario.Id + "'.");

            foreach (CrewMemberState member in s.Crew)
            {
                if (member.Slot >= layout.MaxCrew)
                    report.Error("SlotOutOfRange", member.Player + " slot " + member.Slot + " ≥ MaxCrew " + layout.MaxCrew + ".");
                if (!layout.IsAdmissible(member.Pose.Position, radius))
                    report.Error("PositionNotAdmissible", member.Player + " at " + member.Pose.Position + " violates the plan.");
            }

            var known = new HashSet<StationId>(layout.Stations.Select(p => p.Id));
            var listed = new HashSet<StationId>(s.Stations.Select(st => st.Station));
            foreach (StationId id in known.Except(listed)) report.Error("StationMissing", "Station '" + id + "' is not published.");
            foreach (StationId id in listed.Except(known)) report.Error("StationUnknown", "Station '" + id + "' is not in the plan.");

            foreach (StationState station in s.Stations)
            {
                TaskProgress active = station.ActiveTask;
                if (active == null) continue;
                if (!scenario.TryGetTask(active.Task, out TaskDefinition definition))
                {
                    report.Error("TaskUnknown", "Station '" + station.Station + "' runs unknown task '" + active.Task + "'.");
                    continue;
                }
                if (definition.Station != station.Station)
                    report.Error("TaskWrongStation", "Task '" + active.Task + "' runs at '" + station.Station + "', defined at '" + definition.Station + "'.");
                if (Math.Abs(definition.DurationSeconds - active.DurationSeconds) > 1e-9)
                    report.Error("TaskDurationMismatch", "Task '" + active.Task + "' publishes " + active.DurationSeconds + " s, defined " + definition.DurationSeconds + " s.");
                if (s.State == SessionState.Running && s.TryGetCrew(active.Performer, out CrewMemberState performer)
                    && layout.TryGetStation(station.Station, out StationPlacement placement)
                    && !placement.IsWithinReach(performer.Pose.Position))
                    report.Error("PerformerOutOfReach", active.Performer + " works at '" + station.Station + "' from "
                        + performer.Pose.Position + ", beyond " + placement.InteractionRadiusMeters + " m.");
            }

            OutpostSimulationSnapshot r = s.Resources;
            var p = r.Power;
            if (p.EnergyJoules < -1e-9 || p.EnergyJoules > p.CapacityJoules + 1e-9)
                report.Error("EnergyOutOfRange", "Energy " + p.EnergyJoules + " J outside [0, " + p.CapacityJoules + "].");
            ValidateReading("O2", r.LifeSupport.Cabin.Oxygen, s.SimulatedTimeSeconds, report);
            ValidateReading("CO2", r.LifeSupport.Cabin.CarbonDioxide, s.SimulatedTimeSeconds, report);
            return report;
        }

        private static void ValidateReading(string gas, GasReading reading, double now, Phase02Report report)
        {
            if (reading.Status == ReadingStatus.Unavailable) return;
            if (reading.PartialPressureKilopascals < 0)
                report.Error("ReadingNegative", gas + " partial pressure is negative.");
            if (reading.ObservedAtSeconds > now + 1e-9)
                report.Error("ReadingFromFuture", gas + " observed at " + reading.ObservedAtSeconds + " s after " + now + " s.");
        }

        private void ValidateTransition(OutpostSessionSnapshot prev, OutpostSessionSnapshot next, Phase02Report report)
        {
            if (!next.IsNewerThan(prev))
            {
                report.Error("NotNewer", "(" + next.Epoch + ", " + next.Revision + ") is not newer than (" + prev.Epoch + ", " + prev.Revision + ").");
                return;
            }
            if (next.Epoch != prev.Epoch)
            {
                ValidateRestart(prev, next, report);
                return;
            }
            double dt = next.SimulatedTimeSeconds - prev.SimulatedTimeSeconds;
            if (dt < -1e-12) report.Error("TimeWentBack", "Simulated time " + prev.SimulatedTimeSeconds + " → " + next.SimulatedTimeSeconds + " s.");
            if (IsEnded(prev.State) && !IsEnded(next.State))
                report.Error("EndedMissionResumed", prev.State + " → " + next.State + " without a restart.");
            if (IsEnded(prev.State) && dt > 1e-12)
                report.Error("TimeAfterEnd", "Simulated time advanced after the mission ended.");

            double maxStep = scenario.Movement.MaxSpeedMetersPerSecond * Math.Max(dt, 0) + PositionTolerance;
            foreach (CrewMemberState after in next.Crew)
            {
                if (!prev.TryGetCrew(after.Player, out CrewMemberState before))
                {
                    BasePosition spawn = scenario.Layout.SpawnPoints[Math.Min(after.Slot, scenario.Layout.MaxCrew - 1)];
                    if (after.Pose.Position.DistanceTo(spawn) > maxStep)
                        report.Error("JoinNotAtSpawn", after.Player + " joined at " + after.Pose.Position + ", spawn " + spawn + ".");
                    if (prev.Crew.Any(m => m.Slot == after.Slot))
                        report.Error("SlotTaken", after.Player + " joined into occupied slot " + after.Slot + ".");
                    continue;
                }
                if (after.Slot != before.Slot) report.Error("SlotChanged", after.Player + " slot " + before.Slot + " → " + after.Slot + ".");
                double moved = after.Pose.Position.DistanceTo(before.Pose.Position);
                if (moved > maxStep)
                    report.Error("MovedTooFar", after.Player + " moved " + moved.ToString("0.######") + " m in " + dt.ToString("0.######")
                        + " s (max " + (maxStep - PositionTolerance).ToString("0.######") + ").");
            }

            var ended = new List<TaskEnd>();
            foreach (StationState before in prev.Stations)
            {
                if (!next.TryGetStation(before.Station, out StationState after)) continue;
                bool newEnd = after.LastEnd != null && !SameEnd(after.LastEnd, before.LastEnd);
                if (newEnd) ended.Add(after.LastEnd);
                TaskProgress was = before.ActiveTask;
                if (was == null) continue;
                bool sameInstance = after.ActiveTask != null && after.ActiveTask.Task == was.Task && after.ActiveTask.Performer == was.Performer;
                if (sameInstance && !newEnd)
                {
                    if (after.ActiveTask.WorkSeconds + 1e-9 < was.WorkSeconds)
                        report.Error("ProgressWentBack", "'" + before.Station + "' progress " + was.WorkSeconds + " → " + after.ActiveTask.WorkSeconds + " s.");
                    if (after.ActiveTask.WorkSeconds - was.WorkSeconds > Math.Max(dt, 0) + 1e-9)
                        report.Error("ProgressFasterThanTime", "'" + before.Station + "' gained more work than elapsed time.");
                    continue;
                }
                if (!newEnd || after.LastEnd.Task != was.Task || after.LastEnd.Performer != was.Performer)
                {
                    report.Error("TaskVanished", "'" + before.Station + "' task '" + was.Task + "' by " + was.Performer + " ended without a matching LastEnd.");
                    continue;
                }
                TaskEndReason reason = after.LastEnd.Reason;
                bool performerGone = !next.TryGetCrew(was.Performer, out _);
                if (performerGone && reason != TaskEndReason.PerformerLeft)
                    report.Error("LeaveNotRecorded", was.Performer + " left but '" + before.Station + "' ended with " + reason + ".");
                if (!performerGone && reason == TaskEndReason.PerformerLeft)
                    report.Error("PhantomLeave", "'" + before.Station + "' ended PerformerLeft but " + was.Performer + " is present.");
                if (reason == TaskEndReason.Completed && was.WorkSeconds + Math.Max(dt, 0) + 1e-9 < was.DurationSeconds)
                    report.Error("CompletedTooEarly", "'" + before.Station + "' completed with " + was.WorkSeconds + " s of " + was.DurationSeconds + " s and Δt " + dt + " s.");
                if (reason == TaskEndReason.MissionEnded && !IsEnded(next.State))
                    report.Error("MissionEndedWhileRunning", "'" + before.Station + "' ended MissionEnded in state " + next.State + ".");
            }

            foreach (CrewMemberState gone in prev.Crew.Where(m => !next.TryGetCrew(m.Player, out _)))
                foreach (StationState st in next.Stations)
                    if (st.Occupant == gone.Player) report.Error("GhostOccupant", gone.Player + " left but still occupies '" + st.Station + "'.");

            if (dt <= 1e-12) ValidateInstantLedger(prev, next, ended, report);
            ValidateCapacityUnchanged(prev, next, report);
        }

        private void ValidateInstantLedger(OutpostSessionSnapshot prev, OutpostSessionSnapshot next, List<TaskEnd> ended, Phase02Report report)
        {
            double expectedPotable = 0, expectedUntreated = 0, expectedFood = 0;
            foreach (TaskEnd end in ended.Where(e => e.Reason == TaskEndReason.Completed))
                if (scenario.TryGetTask(end.Task, out TaskDefinition def))
                {
                    expectedPotable += def.CompletionEffect.DeltaFor(ResourceKind.PotableWater);
                    expectedUntreated += def.CompletionEffect.DeltaFor(ResourceKind.UntreatedWater);
                    expectedFood += def.CompletionEffect.DeltaFor(ResourceKind.StoredFood);
                }
            LifeSupportSnapshot a = prev.Resources.LifeSupport, b = next.Resources.LifeSupport;
            Check("Potable", b.Water.PotableKilograms - a.Water.PotableKilograms, expectedPotable);
            Check("Untreated", b.Water.UntreatedKilograms - a.Water.UntreatedKilograms, expectedUntreated);
            Check("Food", b.Food.StoredKilograms - a.Food.StoredKilograms, expectedFood);

            void Check(string what, double actual, double expected)
            {
                if (Math.Abs(actual - expected) > MassTolerance)
                    report.Error("UnaccountedMass", what + " changed " + actual.ToString("0.######") + " kg with Δt = 0; completion effects explain "
                        + expected.ToString("0.######") + " kg.");
            }
        }

        private static void ValidateCapacityUnchanged(OutpostSessionSnapshot prev, OutpostSessionSnapshot next, Phase02Report report)
        {
            WaterStock a = prev.Resources.LifeSupport.Water, b = next.Resources.LifeSupport.Water;
            if (a.PotableCapacityKilograms != b.PotableCapacityKilograms || a.UntreatedCapacityKilograms != b.UntreatedCapacityKilograms
                || prev.Resources.LifeSupport.Food.CapacityKilograms != next.Resources.LifeSupport.Food.CapacityKilograms)
                report.Error("CapacityChanged", "Stock capacities changed during an epoch.");
        }

        private void ValidateRestart(OutpostSessionSnapshot prev, OutpostSessionSnapshot next, Phase02Report report)
        {
            if (next.Epoch != prev.Epoch + 1) report.Info("EpochSkipped", "Epoch " + prev.Epoch + " → " + next.Epoch + " (a snapshot was missed).");
            if (next.Revision != 0 && next.Epoch == prev.Epoch + 1)
                report.Warning("RestartRevision", "First observed snapshot of epoch " + next.Epoch + " has revision " + next.Revision + " (0 expected if none was missed).");
            if (next.Revision != 0) return; // Only the very first snapshot of the epoch is the reset state.

            foreach (CrewMemberState member in next.Crew)
            {
                BasePosition spawn = scenario.Layout.SpawnPoints[Math.Min(member.Slot, scenario.Layout.MaxCrew - 1)];
                if (member.Pose.Position.DistanceTo(spawn) > PositionTolerance || member.Pose.Motion != CrewMotion.Idle
                    || !member.Pose.Facing.Equals(CrewPose.DefaultFacing))
                    report.Error("RestartPoseNotReset", member.Player + " after restart: " + member.Pose.Position + " " + member.Pose.Motion + ".");
                if (prev.TryGetCrew(member.Player, out CrewMemberState before) && before.Slot != member.Slot)
                    report.Error("RestartSlotChanged", member.Player + " slot changed on restart.");
            }
            foreach (StationState st in next.Stations)
                if (st.ActiveTask != null || st.LastEnd != null)
                    report.Error("RestartTaskNotCleared", "'" + st.Station + "' keeps task state after restart.");
            if (next.State != SessionState.Ready && next.State != SessionState.Running)
                report.Error("RestartState", "Restart produced " + next.State + ".");
            if (Math.Abs(next.SimulatedTimeSeconds) > 1e-9)
                report.Error("RestartClock", "Simulated time after restart is " + next.SimulatedTimeSeconds + " s.");

            var initial = scenario.Simulation.LifeSupport;
            var w = next.Resources.LifeSupport.Water;
            if (Math.Abs(w.PotableKilograms - initial.InitialWater.PotableKilograms) > MassTolerance
                || Math.Abs(w.UntreatedKilograms - initial.InitialWater.UntreatedKilograms) > MassTolerance
                || Math.Abs(next.Resources.LifeSupport.Food.StoredKilograms - initial.InitialFood.StoredKilograms) > MassTolerance)
                report.Error("RestartStocks", "Stocks after restart differ from the scenario's initial stocks.");
            if (Math.Abs(next.Resources.Power.EnergyJoules - scenario.Simulation.Power.InitialEnergyJoules) > 1e-6)
                report.Error("RestartEnergy", "Energy after restart differs from the initial energy.");
        }

        private static bool SameEnd(TaskEnd a, TaskEnd b) =>
            b != null && a.Task == b.Task && a.Performer == b.Performer && a.Reason == b.Reason && a.EndedAtSeconds == b.EndedAtSeconds;

        private static bool IsEnded(SessionState state) => state == SessionState.Completed || state == SessionState.Interrupted;
    }
}
