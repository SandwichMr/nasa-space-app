using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Application.Outpost;
using Trainer.Simulation.Eva;

namespace Trainer.Application.Expedition
{
    public enum ExpeditionPhase
    {
        /// <summary>Nobody has been on suit supply in this epoch.</summary>
        Preparing,
        /// <summary>Someone has been sealed (the EVA started) and no return was declared.</summary>
        Underway,
        /// <summary>A crew member declared the return: no new exterior work starts.</summary>
        Returning,
        /// <summary>The session ended (Completed or Interrupted).</summary>
        Closed
    }

    /// <summary>
    /// Phase 3 result, published in the snapshot (clients never evaluate). Returning or sheltering in time is a
    /// correct result even without science; it is not a defeat.
    /// </summary>
    public enum ExpeditionOutcome
    {
        InProgress,
        /// <summary>Everyone who went out is back unsealed in the base and the primary objective was done.</summary>
        ScienceReturned,
        /// <summary>Everyone who went out is back safely after a declared return, without the primary objective.</summary>
        ReturnedWithoutScience,
        /// <summary>When the instruction ended, every member who had received it was protected from its deadline on.</summary>
        ShelteredInTime,
        /// <summary>Every member who received it was protected at the end, some after the deadline (for example a late reception).</summary>
        ShelteredLate,
        /// <summary>Someone who had received the instruction was not protected (or left protection) before its end.</summary>
        ProtectionIncomplete,
        /// <summary>The instruction ended and nobody present had received it.</summary>
        AdvisoryNotReceived,
        /// <summary>The mission time ran out before the expedition closed.</summary>
        TimeExpired,
        /// <summary>A suit service reached zero while its wearer was on suit supply. The simulation stops here; no
        /// medical consequence is modelled.</summary>
        SuitServiceDepleted,
        /// <summary>The base essential load was not fully served (phase 1/2 meaning).</summary>
        EssentialServiceLost,
        SimulationFault
    }

    /// <summary>Single mapping of a phase 3 outcome onto the phase 2 snapshot fields (a coarse compatibility value:
    /// phase 3 clients read <see cref="ExpeditionOutcome"/>). ObjectiveAchieved = closed with the safety objective met;
    /// TimeExpired = closed by a deadline (mission or instruction) without it; EssentialServiceLost = base or suit
    /// essential service; SimulationFault.</summary>
    public static class ExpeditionOutcomes
    {
        public static OutpostOutcome ToOutpostOutcome(ExpeditionOutcome outcome)
        {
            switch (outcome)
            {
                case ExpeditionOutcome.InProgress: return OutpostOutcome.InProgress;
                case ExpeditionOutcome.ScienceReturned:
                case ExpeditionOutcome.ReturnedWithoutScience:
                case ExpeditionOutcome.ShelteredInTime:
                case ExpeditionOutcome.ShelteredLate: return OutpostOutcome.ObjectiveAchieved;
                case ExpeditionOutcome.ProtectionIncomplete:
                case ExpeditionOutcome.AdvisoryNotReceived:
                case ExpeditionOutcome.TimeExpired: return OutpostOutcome.TimeExpired;
                case ExpeditionOutcome.SuitServiceDepleted:
                case ExpeditionOutcome.EssentialServiceLost: return OutpostOutcome.EssentialServiceLost;
                case ExpeditionOutcome.SimulationFault: return OutpostOutcome.SimulationFault;
                default: throw new ArgumentOutOfRangeException(nameof(outcome));
            }
        }

        public static bool IsEnded(ExpeditionOutcome outcome) => outcome != ExpeditionOutcome.InProgress;

        /// <summary>True when the crew-safety objective is met by this outcome.</summary>
        public static bool IsSafe(ExpeditionOutcome outcome) =>
            ToOutpostOutcome(outcome) == OutpostOutcome.ObjectiveAchieved;
    }

    public enum ObjectiveRole
    {
        /// <summary>Everyone who went out returns safely (met only when the expedition closes safely). Exactly one.</summary>
        Safety,
        /// <summary>The science goal that motivates the EVA (at least one).</summary>
        Primary,
        /// <summary>Extra science with a cost that must be weighed against the return margin.</summary>
        Optional
    }

    /// <summary>Objective of the phase 3 mission (configuration, immutable). Primary/Optional count completed
    /// instances of <see cref="Tasks"/>; Safety has no tasks and target 1.</summary>
    public sealed class ExpeditionObjectiveRule
    {
        public string Key { get; }
        public ObjectiveRole Role { get; }
        public IReadOnlyList<TaskTypeId> Tasks { get; }
        public int TargetCount { get; }

        public ExpeditionObjectiveRule(string key, ObjectiveRole role, IEnumerable<TaskTypeId> tasks, int targetCount)
        {
            Key = StableKey.Require(key, nameof(key));
            Role = OutpostGuard.Defined(role, nameof(role));
            if (tasks == null) throw new ArgumentNullException(nameof(tasks));
            var copy = new List<TaskTypeId>();
            foreach (TaskTypeId task in tasks)
            {
                if (!task.IsValid) throw new ArgumentException("Task type id required.", nameof(tasks));
                if (copy.Contains(task)) throw new ArgumentException($"Duplicate task '{task}'.", nameof(tasks));
                copy.Add(task);
            }
            if (role == ObjectiveRole.Safety)
            {
                if (copy.Count != 0 || targetCount != 1) throw new ArgumentException("The safety objective has no tasks and target 1.");
            }
            else if (copy.Count == 0) throw new ArgumentException("A science objective counts at least one task.", nameof(tasks));
            if (targetCount < 1) throw new ArgumentOutOfRangeException(nameof(targetCount));
            Tasks = new ReadOnlyCollection<TaskTypeId>(copy.ToArray());
            TargetCount = targetCount;
        }

        public bool Counts(TaskTypeId task)
        {
            foreach (TaskTypeId candidate in Tasks)
                if (candidate == task) return true;
            return false;
        }
    }

    /// <summary>Rules of the phase 3 mission (configuration, immutable).</summary>
    public sealed class ExpeditionMissionRules
    {
        public double MissionDurationSeconds { get; }
        public IReadOnlyList<ExpeditionObjectiveRule> Objectives { get; }

        public ExpeditionMissionRules(double missionDurationSeconds, IEnumerable<ExpeditionObjectiveRule> objectives)
        {
            MissionDurationSeconds = OutpostGuard.Positive(missionDurationSeconds, nameof(missionDurationSeconds));
            if (objectives == null) throw new ArgumentNullException(nameof(objectives));
            var copy = new List<ExpeditionObjectiveRule>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            int safety = 0, primary = 0;
            foreach (ExpeditionObjectiveRule objective in objectives)
            {
                if (objective == null) throw new ArgumentException("Null objective.", nameof(objectives));
                if (!keys.Add(objective.Key)) throw new ArgumentException($"Duplicate objective '{objective.Key}'.", nameof(objectives));
                if (objective.Role == ObjectiveRole.Safety) safety++;
                if (objective.Role == ObjectiveRole.Primary) primary++;
                copy.Add(objective);
            }
            if (copy.Count > OutpostLimits.MaxObjectives) throw new ArgumentException("Too many objectives.", nameof(objectives));
            if (safety != 1 || primary < 1) throw new ArgumentException("Exactly one safety objective and at least one primary.", nameof(objectives));
            Objectives = new ReadOnlyCollection<ExpeditionObjectiveRule>(copy.ToArray());
        }
    }

    /// <summary>Published objective (immutable).</summary>
    public sealed class ExpeditionObjective
    {
        public string Key { get; }
        public ObjectiveRole Role { get; }
        public int Current { get; }
        public int Target { get; }
        public bool Met => Current >= Target;

        public ExpeditionObjective(string key, ObjectiveRole role, int current, int target)
        {
            Key = StableKey.Require(key, nameof(key));
            Role = OutpostGuard.Defined(role, nameof(role));
            if (current < 0) throw new ArgumentOutOfRangeException(nameof(current));
            if (target < 1) throw new ArgumentOutOfRangeException(nameof(target));
            Current = current;
            Target = target;
        }
    }

    /// <summary>The team's return decision (immutable), recorded with the information the DECLARING member had at
    /// that time: the advisories they had received and the team's worst margin then. Never judged with later or
    /// unreceived information.</summary>
    public sealed class ReturnDecision
    {
        public PlayerId DeclaredBy { get; }
        public double AtSeconds { get; }
        public IReadOnlyList<string> AdvisoriesKnown { get; }
        /// <summary>Worst return margin among sealed members at the decision (null if none was valid).</summary>
        public double? TeamMarginSeconds { get; }

        public ReturnDecision(PlayerId declaredBy, double atSeconds, IEnumerable<string> advisoriesKnown, double? teamMarginSeconds)
        {
            if (!declaredBy.IsValid) throw new ArgumentException("Declarer required.", nameof(declaredBy));
            DeclaredBy = declaredBy;
            AtSeconds = OutpostGuard.NonNegative(atSeconds, nameof(atSeconds));
            if (advisoriesKnown == null) throw new ArgumentNullException(nameof(advisoriesKnown));
            var copy = new List<string>();
            foreach (string id in advisoriesKnown)
            {
                StableKey.Require(id, nameof(advisoriesKnown));
                if (!copy.Contains(id)) copy.Add(id);
            }
            if (copy.Count > ExpeditionLimits.MaxKnownAdvisories) throw new ArgumentException("Too many advisories.", nameof(advisoriesKnown));
            AdvisoriesKnown = new ReadOnlyCollection<string>(copy.ToArray());
            if (teamMarginSeconds.HasValue) OutpostGuard.Finite(teamMarginSeconds.Value, nameof(teamMarginSeconds));
            TeamMarginSeconds = teamMarginSeconds;
        }
    }

    public enum ProtectionCategory
    {
        /// <summary>Protected from the deadline (or earlier) until the instruction ended, without leaving.</summary>
        InTime,
        /// <summary>First protected after the deadline, then without leaving until the end.</summary>
        Late,
        /// <summary>Protected at some point after reception but left protection before the end.</summary>
        Interrupted,
        /// <summary>Received the instruction but was never protected afterwards.</summary>
        NotProtected,
        /// <summary>Never received the instruction (for example out of radio range the whole time). Not judged
        /// as a decision: they could not know.</summary>
        NotReceived
    }

    /// <summary>Assessment of one member when an instruction ends (immutable). It uses ONLY the issued messages and
    /// that member's reception, never the hidden event times.</summary>
    public sealed class ProtectionAssessment
    {
        public PlayerId Player { get; }
        public ProtectionCategory Category { get; }
        public double? ReceivedAtSeconds { get; }
        public double? FirstProtectedAtSeconds { get; }
        public bool ReceivedAfterShelterBy { get; }

        public ProtectionAssessment(PlayerId player, ProtectionCategory category, double? receivedAtSeconds,
            double? firstProtectedAtSeconds, bool receivedAfterShelterBy)
        {
            if (!player.IsValid) throw new ArgumentException("Player required.", nameof(player));
            Player = player;
            Category = OutpostGuard.Defined(category, nameof(category));
            if ((category == ProtectionCategory.NotReceived) != !receivedAtSeconds.HasValue)
                throw new ArgumentException("Only a member who did not receive the instruction has no reception time.", nameof(receivedAtSeconds));
            bool protectedOnce = category == ProtectionCategory.InTime || category == ProtectionCategory.Late
                || category == ProtectionCategory.Interrupted;
            if (protectedOnce != firstProtectedAtSeconds.HasValue)
                throw new ArgumentException("Protection time matches the category.", nameof(firstProtectedAtSeconds));
            if (receivedAtSeconds.HasValue) OutpostGuard.NonNegative(receivedAtSeconds.Value, nameof(receivedAtSeconds));
            if (firstProtectedAtSeconds.HasValue) OutpostGuard.NonNegative(firstProtectedAtSeconds.Value, nameof(firstProtectedAtSeconds));
            if (receivedAfterShelterBy && !receivedAtSeconds.HasValue) throw new ArgumentException("Late reception needs a reception.");
            ReceivedAtSeconds = receivedAtSeconds;
            FirstProtectedAtSeconds = firstProtectedAtSeconds;
            ReceivedAfterShelterBy = receivedAfterShelterBy;
        }
    }

    public enum IncidentKind
    {
        /// <summary>A member's link ended while on suit supply; their suit is Abandoned and nobody is in it.</summary>
        DisconnectedWhileSealed,
        /// <summary>A sealed suit service went below its declared reserve (the plan used the reserve).</summary>
        ReserveUsed,
        /// <summary>An instruction ended and this member never received it.</summary>
        AdvisoryNotReceived
    }

    /// <summary>Something the debrief must mention (immutable). Kept in the order it happened.</summary>
    public sealed class ExpeditionIncident
    {
        public IncidentKind Kind { get; }
        public PlayerId Player { get; }
        public double AtSeconds { get; }
        /// <summary>The suit involved, or null.</summary>
        public string SuitKey { get; }
        /// <summary>ReserveUsed: the service; otherwise null.</summary>
        public SuitService? Service { get; }

        public ExpeditionIncident(IncidentKind kind, PlayerId player, double atSeconds, string suitKey, SuitService? service)
        {
            Kind = OutpostGuard.Defined(kind, nameof(kind));
            if (!player.IsValid) throw new ArgumentException("Player required.", nameof(player));
            Player = player;
            AtSeconds = OutpostGuard.NonNegative(atSeconds, nameof(atSeconds));
            SuitKey = suitKey == null ? null : StableKey.Require(suitKey, nameof(suitKey));
            if ((kind == IncidentKind.ReserveUsed) != service.HasValue)
                throw new ArgumentException("Only ReserveUsed names a service.", nameof(service));
            if (service.HasValue && !Enum.IsDefined(typeof(SuitService), service.Value)) throw new ArgumentOutOfRangeException(nameof(service));
            Service = service;
        }
    }
}
