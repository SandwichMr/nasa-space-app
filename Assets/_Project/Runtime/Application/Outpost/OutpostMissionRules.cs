using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Trainer.Application.Outpost
{
    /// <summary>
    /// One mission objective of the interior mission (configuration, immutable): count completed task instances
    /// of any of <see cref="Tasks"/> until <see cref="TargetCount"/>. Only instances that end with
    /// <see cref="TaskEndReason.Completed"/> count; cancelled, interrupted or refused ones never do.
    /// Published as <see cref="ObjectiveProgress"/> with <see cref="ProgressUnit.Count"/>.
    /// </summary>
    public sealed class OutpostObjectiveRule
    {
        public string Key { get; }
        public IReadOnlyList<TaskTypeId> Tasks { get; }
        public int TargetCount { get; }
        /// <summary>Required objectives end the mission with ObjectiveAchieved once all are met; optional ones
        /// (for example extra science) are published but never end the mission.</summary>
        public bool Required { get; }

        public OutpostObjectiveRule(string key, IEnumerable<TaskTypeId> tasks, int targetCount, bool required)
        {
            if (!StableKey.IsValid(key)) throw new ArgumentException($"'{key}' is not a stable key.", nameof(key));
            if (tasks == null) throw new ArgumentNullException(nameof(tasks));
            if (targetCount < 1) throw new ArgumentOutOfRangeException(nameof(targetCount), "At least one completion.");
            var copy = new List<TaskTypeId>();
            var seen = new HashSet<TaskTypeId>();
            foreach (TaskTypeId task in tasks)
            {
                if (!task.IsValid) throw new ArgumentException("Task type id required.", nameof(tasks));
                if (!seen.Add(task)) throw new ArgumentException($"Duplicate task '{task}'.", nameof(tasks));
                copy.Add(task);
            }
            if (copy.Count == 0) throw new ArgumentException("An objective counts at least one task type.", nameof(tasks));
            Key = key;
            Tasks = new ReadOnlyCollection<TaskTypeId>(copy.ToArray());
            TargetCount = targetCount;
            Required = required;
        }

        public bool Counts(TaskTypeId task)
        {
            foreach (TaskTypeId candidate in Tasks)
                if (candidate == task) return true;
            return false;
        }
    }

    /// <summary>
    /// Rules of the phase 2 interior mission (configuration, immutable). Separate from
    /// <see cref="OutpostScenario"/>, which by contract carries no mission rules. Values are scenario inputs,
    /// not approved mission data.
    ///
    /// End conditions, evaluated by the authority after each step, in priority order:
    /// essential power not fully served (phase 1 meaning) → Interrupted/EssentialServiceLost;
    /// every required objective met → Completed/ObjectiveAchieved;
    /// <see cref="MissionDurationSeconds"/> reached → Completed/TimeExpired.
    /// </summary>
    public sealed class OutpostMissionRules
    {
        /// <summary>Simulated seconds after which the mission ends if the required objectives were not met.</summary>
        public double MissionDurationSeconds { get; }
        public IReadOnlyList<OutpostObjectiveRule> Objectives { get; }

        public OutpostMissionRules(double missionDurationSeconds, IEnumerable<OutpostObjectiveRule> objectives)
        {
            if (double.IsNaN(missionDurationSeconds) || double.IsInfinity(missionDurationSeconds) || missionDurationSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(missionDurationSeconds));
            if (objectives == null) throw new ArgumentNullException(nameof(objectives));
            var copy = new List<OutpostObjectiveRule>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            bool anyRequired = false;
            foreach (OutpostObjectiveRule objective in objectives)
            {
                if (objective == null) throw new ArgumentException("Null objective.", nameof(objectives));
                if (!keys.Add(objective.Key)) throw new ArgumentException($"Duplicate objective '{objective.Key}'.", nameof(objectives));
                anyRequired |= objective.Required;
                copy.Add(objective);
            }
            if (copy.Count == 0 || copy.Count > OutpostLimits.MaxObjectives)
                throw new ArgumentException("Between 1 and MaxObjectives objectives.", nameof(objectives));
            if (!anyRequired) throw new ArgumentException("At least one objective must be required.", nameof(objectives));
            MissionDurationSeconds = missionDurationSeconds;
            Objectives = new ReadOnlyCollection<OutpostObjectiveRule>(copy.ToArray());
        }

        /// <summary>Null when every objective counts only task types of <paramref name="scenario"/>; otherwise
        /// the first problem. The authority refuses rules that do not match its scenario.</summary>
        public string ProblemWith(OutpostScenario scenario)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            foreach (OutpostObjectiveRule objective in Objectives)
                foreach (TaskTypeId task in objective.Tasks)
                    if (!scenario.TryGetTask(task, out _))
                        return $"Objective '{objective.Key}' counts unknown task '{task}'.";
            return null;
        }
    }
}
