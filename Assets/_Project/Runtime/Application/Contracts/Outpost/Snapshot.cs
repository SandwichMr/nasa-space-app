using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Application.Outpost
{
    /// <summary>Phase 2 mission result, published in the snapshot (so clients need no local evaluation).
    /// Separate from the phase 1 <see cref="MissionOutcome"/>; the mission implementation documents which
    /// condition produces each value.</summary>
    public enum OutpostOutcome
    {
        /// <summary>Ready, Running or Paused.</summary>
        InProgress,
        /// <summary>Every required objective met. State Completed.</summary>
        ObjectiveAchieved,
        /// <summary>Mission duration elapsed first. State Completed.</summary>
        TimeExpired,
        /// <summary>An essential service defined by the scenario was not served. State Interrupted.</summary>
        EssentialServiceLost,
        /// <summary>The authority's simulation failed. State Interrupted; only Restart is accepted.</summary>
        SimulationFault
    }

    public enum ProgressUnit { Seconds, Kilograms, Count }

    /// <summary>One mission objective as published (immutable). Text for players is looked up from
    /// <see cref="Key"/> by the presentation.</summary>
    public sealed class ObjectiveProgress
    {
        public string Key { get; }
        public double Current { get; }
        public double Target { get; }
        public ProgressUnit Unit { get; }
        /// <summary>False for optional objectives (for example extra science).</summary>
        public bool Required { get; }
        public bool IsMet => Current >= Target;

        public ObjectiveProgress(string key, double current, double target, ProgressUnit unit, bool required)
        {
            Key = StableKey.Require(key, nameof(key));
            Current = OutpostGuard.NonNegative(current, nameof(current));
            Target = OutpostGuard.Positive(target, nameof(target));
            Unit = OutpostGuard.Defined(unit, nameof(unit));
            Required = required;
        }
    }

    /// <summary>
    /// Complete authoritative state of a phase 2 session (immutable; collections are private copies exposed
    /// read-only). Always a FULL state, never a delta, so a late joiner needs only the latest one.
    /// Order: (Epoch, Revision) — see <see cref="IsNewerThan"/>. The constructor rejects inconsistent states:
    /// duplicate ids or slots, an active task whose performer is not present, a member with two active tasks,
    /// active tasks after the mission ended, or an outcome that does not match the state.
    /// </summary>
    public sealed class OutpostSessionSnapshot
    {
        public long Epoch { get; }
        public long Revision { get; }
        public SessionState State { get; }
        public OutpostOutcome Outcome { get; }
        public string ScenarioId { get; }
        /// <summary>Present members, in PlayerId order as published by the authority.</summary>
        public IReadOnlyList<CrewMemberState> Crew { get; }
        /// <summary>Every station of the layout, in layout order.</summary>
        public IReadOnlyList<StationState> Stations { get; }
        public OutpostSimulationSnapshot Resources { get; }
        public IReadOnlyList<ObjectiveProgress> Objectives { get; }
        /// <summary>Mission clock: simulated seconds since the epoch started.</summary>
        public double SimulatedTimeSeconds => Resources.Power.SimulatedTimeSeconds;

        public OutpostSessionSnapshot(long epoch, long revision, SessionState state, OutpostOutcome outcome,
            string scenarioId, IEnumerable<CrewMemberState> crew, IEnumerable<StationState> stations,
            OutpostSimulationSnapshot resources, IEnumerable<ObjectiveProgress> objectives)
        {
            if (epoch < 0) throw new ArgumentOutOfRangeException(nameof(epoch));
            if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
            Epoch = epoch;
            Revision = revision;
            State = OutpostGuard.Defined(state, nameof(state));
            Outcome = OutpostGuard.Defined(outcome, nameof(outcome));
            if (!OutcomeMatches(state, outcome)) throw new ArgumentException("Outcome does not match the state.", nameof(outcome));
            ScenarioId = StableKey.Require(scenarioId, nameof(scenarioId));
            Resources = resources ?? throw new ArgumentNullException(nameof(resources));
            if (crew == null) throw new ArgumentNullException(nameof(crew));
            if (stations == null) throw new ArgumentNullException(nameof(stations));
            if (objectives == null) throw new ArgumentNullException(nameof(objectives));

            var crewCopy = new List<CrewMemberState>();
            var players = new HashSet<PlayerId>();
            var slots = new HashSet<int>();
            foreach (CrewMemberState member in crew)
            {
                if (member == null) throw new ArgumentException("Null crew member.", nameof(crew));
                if (!players.Add(member.Player)) throw new ArgumentException($"Duplicate player {member.Player}.", nameof(crew));
                if (!slots.Add(member.Slot)) throw new ArgumentException($"Duplicate slot {member.Slot}.", nameof(crew));
                crewCopy.Add(member);
            }
            if (crewCopy.Count > OutpostLimits.MaxCrew) throw new ArgumentException("Too many crew members.", nameof(crew));

            bool ended = state == SessionState.Completed || state == SessionState.Interrupted;
            var stationCopy = new List<StationState>();
            var stationIds = new HashSet<StationId>();
            var busy = new HashSet<PlayerId>();
            foreach (StationState station in stations)
            {
                if (station == null) throw new ArgumentException("Null station.", nameof(stations));
                if (!stationIds.Add(station.Station))
                    throw new ArgumentException($"Duplicate station '{station.Station}'.", nameof(stations));
                if (station.ActiveTask != null)
                {
                    PlayerId performer = station.ActiveTask.Performer;
                    if (ended) throw new ArgumentException("No task can be in progress after the mission ended.", nameof(stations));
                    if (!players.Contains(performer))
                        throw new ArgumentException($"Performer {performer} is not present.", nameof(stations));
                    if (!busy.Add(performer))
                        throw new ArgumentException($"Performer {performer} has two tasks in progress.", nameof(stations));
                }
                stationCopy.Add(station);
            }
            if (stationCopy.Count > OutpostLimits.MaxStations) throw new ArgumentException("Too many stations.", nameof(stations));

            var objectiveCopy = new List<ObjectiveProgress>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (ObjectiveProgress objective in objectives)
            {
                if (objective == null) throw new ArgumentException("Null objective.", nameof(objectives));
                if (!keys.Add(objective.Key)) throw new ArgumentException($"Duplicate objective '{objective.Key}'.", nameof(objectives));
                objectiveCopy.Add(objective);
            }
            if (objectiveCopy.Count > OutpostLimits.MaxObjectives) throw new ArgumentException("Too many objectives.", nameof(objectives));

            Crew = new ReadOnlyCollection<CrewMemberState>(crewCopy.ToArray());
            Stations = new ReadOnlyCollection<StationState>(stationCopy.ToArray());
            Objectives = new ReadOnlyCollection<ObjectiveProgress>(objectiveCopy.ToArray());
        }

        /// <summary>True when this snapshot must replace <paramref name="other"/>: newer epoch, or same epoch and
        /// greater revision (or no previous snapshot). Older or equal ones are discarded.</summary>
        public bool IsNewerThan(OutpostSessionSnapshot other) =>
            other == null || Epoch > other.Epoch || (Epoch == other.Epoch && Revision > other.Revision);

        public bool TryGetCrew(PlayerId player, out CrewMemberState member)
        {
            foreach (CrewMemberState candidate in Crew)
                if (candidate.Player == player)
                {
                    member = candidate;
                    return true;
                }
            member = null;
            return false;
        }

        public bool TryGetStation(StationId station, out StationState state)
        {
            foreach (StationState candidate in Stations)
                if (candidate.Station == station)
                {
                    state = candidate;
                    return true;
                }
            state = null;
            return false;
        }

        private static bool OutcomeMatches(SessionState state, OutpostOutcome outcome)
        {
            switch (state)
            {
                case SessionState.Completed:
                    return outcome == OutpostOutcome.ObjectiveAchieved || outcome == OutpostOutcome.TimeExpired;
                case SessionState.Interrupted:
                    return outcome == OutpostOutcome.EssentialServiceLost || outcome == OutpostOutcome.SimulationFault;
                default:
                    return outcome == OutpostOutcome.InProgress;
            }
        }
    }
}
