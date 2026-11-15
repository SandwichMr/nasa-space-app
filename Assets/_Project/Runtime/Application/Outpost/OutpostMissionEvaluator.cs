using System;
using System.Collections.Generic;
using Trainer.Simulation;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Application.Outpost
{
    /// <summary>
    /// Pure evaluation for the interior mission. It reuses the phase 1 service rule
    /// (<see cref="MissionOutcomeEvaluator.IsFullyServed"/> and its tolerances) so "essential service" keeps
    /// exactly the phase 1 meaning. It never changes stocks: <see cref="CanApply"/> only predicts.
    /// </summary>
    public static class OutpostMissionEvaluator
    {
        /// <summary>Same tolerance as phase 1 for reaching the duration or finishing work.</summary>
        public const double TimeToleranceSeconds = MissionOutcomeEvaluator.TimeToleranceSeconds;

        /// <summary>Essential demand of zero counts as served (nothing is required), as in phase 1.</summary>
        public static bool IsEssentialServed(PowerSnapshot power)
        {
            if (power == null) throw new ArgumentNullException(nameof(power));
            return power.EssentialRequestedWatts <= 0
                || MissionOutcomeEvaluator.IsFullyServed(power.EssentialRequestedWatts, power.EssentialServedWatts);
        }

        /// <summary>True when every required objective reached its target. <paramref name="completed"/> holds the
        /// completed count of each objective, in <see cref="OutpostMissionRules.Objectives"/> order.</summary>
        public static bool RequiredObjectivesMet(OutpostMissionRules rules, IReadOnlyList<int> completed)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (completed == null) throw new ArgumentNullException(nameof(completed));
            if (completed.Count != rules.Objectives.Count) throw new ArgumentException("One count per objective.", nameof(completed));
            for (int i = 0; i < completed.Count; i++)
                if (rules.Objectives[i].Required && completed[i] < rules.Objectives[i].TargetCount) return false;
            return true;
        }

        /// <summary>Post-step evaluation: essential loss, then required objectives, then the time limit.</summary>
        public static OutpostOutcome Evaluate(OutpostMissionRules rules, PowerSnapshot power, IReadOnlyList<int> completed)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (!IsEssentialServed(power)) return OutpostOutcome.EssentialServiceLost;
            if (RequiredObjectivesMet(rules, completed)) return OutpostOutcome.ObjectiveAchieved;
            if (power.SimulatedTimeSeconds >= rules.MissionDurationSeconds - TimeToleranceSeconds) return OutpostOutcome.TimeExpired;
            return OutpostOutcome.InProgress;
        }

        public static SessionState ToSessionState(OutpostOutcome outcome)
        {
            switch (outcome)
            {
                case OutpostOutcome.InProgress: return SessionState.Running;
                case OutpostOutcome.ObjectiveAchieved:
                case OutpostOutcome.TimeExpired: return SessionState.Completed;
                case OutpostOutcome.EssentialServiceLost:
                case OutpostOutcome.SimulationFault: return SessionState.Interrupted;
                default: throw new ArgumentOutOfRangeException(nameof(outcome));
            }
        }

        /// <summary>
        /// Start-time prediction for StartTask (<see cref="OutpostReasons.InsufficientResources"/>): true when the
        /// effect could be applied to the published ledger right now (no stock below zero, none above capacity).
        /// It reserves nothing: at completion the simulation's Apply decides again, all or nothing.
        /// </summary>
        public static bool CanApply(LifeSupportSnapshot ledger, ResourceEffect effect)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            foreach (ResourceChange change in effect.Changes)
            {
                double amount, capacity;
                switch (change.Kind)
                {
                    case ResourceKind.PotableWater:
                        amount = ledger.Water.PotableKilograms; capacity = ledger.Water.PotableCapacityKilograms; break;
                    case ResourceKind.UntreatedWater:
                        amount = ledger.Water.UntreatedKilograms; capacity = ledger.Water.UntreatedCapacityKilograms; break;
                    case ResourceKind.StoredFood:
                        amount = ledger.Food.StoredKilograms; capacity = ledger.Food.CapacityKilograms; break;
                    default:
                        return false;
                }
                double after = amount + change.DeltaKilograms;
                if (after < 0d || after > capacity) return false;
            }
            return true;
        }
    }
}
