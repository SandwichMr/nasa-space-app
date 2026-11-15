using System;
using Trainer.Application.Outpost;

namespace Trainer.Application.BaseOperations
{
    /// <summary>Size limits of every phase 4 snapshot and configuration (wire encoders rely on them).</summary>
    public static class BaseOperationsLimits
    {
        public const int MaxGauges = 8;
        public const int MaxOperations = 8;
        public const int MaxOrders = 8;
        public const int MaxManifests = 8;
        public const int MaxManifestItems = 6;
        public const int MaxActivities = 32;
        public const int MaxDecisions = 16;
        public const int MaxReadingsPerObservation = 4;
        public const int MaxScienceOpportunities = 8;
        public const int MaxOperationDefinitions = 24;
    }

    /// <summary>
    /// Global cycle of the session (NOT the cycle of a task or expedition).
    /// <list type="bullet">
    /// <item><see cref="Continuous"/>: the phase 4 living base. Objectives, sorties, repairs and orders are activities
    /// inside the session; none of them ends it. The phase 2 state is only Ready, Running, Paused or Interrupted
    /// (irreversible rule); never Completed.</item>
    /// <item><see cref="Training"/>: the finite phase 3 rules (end by objectives or duration), kept for training and
    /// regression. The phase 2 and phase 3 scenes keep their own authorities and do not use this enum.</item>
    /// </list>
    /// </summary>
    public enum BaseSessionMode { Continuous, Training }

    /// <summary>Why a continuous session ended irreversibly. Anything recoverable (low stock, degraded machine, cancelled
    /// sortie) is never an end.</summary>
    public enum IrreversibleCause
    {
        None,
        /// <summary>The essential power channel stayed not fully served for <see cref="IrreversibleRule.EssentialUnservedGraceSeconds"/>
        /// in a row (the base can no longer run its essential loads; no physiology is modelled).</summary>
        EssentialPowerLost,
        /// <summary>A sealed suit service reached zero (phase 3 rule: the simulation stops; no medical meaning).</summary>
        SuitServiceDepleted,
        /// <summary>The authority's model failed; only Restart is accepted.</summary>
        SimulationFault
    }

    /// <summary>Scenario rule for the only irreversible end of a continuous session (configuration, immutable).</summary>
    public sealed class IrreversibleRule
    {
        /// <summary>Consecutive simulated seconds of essential power not fully served before the end. A recovery window,
        /// not a clinical or engineering threshold.</summary>
        public double EssentialUnservedGraceSeconds { get; }

        public IrreversibleRule(double essentialUnservedGraceSeconds)
        {
            EssentialUnservedGraceSeconds = OutpostGuard.Positive(essentialUnservedGraceSeconds, nameof(essentialUnservedGraceSeconds));
        }
    }

    /// <summary>
    /// Published state of the global session cycle (immutable). Pause, resume and restart are the phase 2/3 commands and
    /// the phase 2 <see cref="SessionState"/> of the contained snapshot; this type adds what the continuous mode needs.
    /// </summary>
    public sealed class BaseSessionStatus
    {
        public BaseSessionMode Mode { get; }
        /// <summary>Consecutive seconds the essential channel has not been fully served (0 when served).</summary>
        public double EssentialUnservedSeconds { get; }
        public double EssentialUnservedGraceSeconds { get; }
        public IrreversibleCause EndCause { get; }
        public double? EndedAtSeconds { get; }
        /// <summary>Last activity sequence number assigned in this epoch (0 = none).</summary>
        public long ActivitySequence { get; }
        /// <summary>Sorties finished (returned, cancelled or ended) in this epoch; the base keeps running after each.</summary>
        public int ExpeditionsFinished { get; }

        public BaseSessionStatus(BaseSessionMode mode, double essentialUnservedSeconds, double essentialUnservedGraceSeconds,
            IrreversibleCause endCause, double? endedAtSeconds, long activitySequence, int expeditionsFinished)
        {
            Mode = OutpostGuard.Defined(mode, nameof(mode));
            EssentialUnservedSeconds = OutpostGuard.NonNegative(essentialUnservedSeconds, nameof(essentialUnservedSeconds));
            EssentialUnservedGraceSeconds = OutpostGuard.Positive(essentialUnservedGraceSeconds, nameof(essentialUnservedGraceSeconds));
            EndCause = OutpostGuard.Defined(endCause, nameof(endCause));
            if (endedAtSeconds.HasValue) OutpostGuard.NonNegative(endedAtSeconds.Value, nameof(endedAtSeconds));
            if (endedAtSeconds.HasValue != (endCause != IrreversibleCause.None))
                throw new ArgumentException("An end time exists exactly with an end cause.", nameof(endedAtSeconds));
            if (endCause == IrreversibleCause.EssentialPowerLost && essentialUnservedSeconds < essentialUnservedGraceSeconds)
                throw new ArgumentException("EssentialPowerLost needs the whole grace window unserved.", nameof(endCause));
            if (activitySequence < 0) throw new ArgumentOutOfRangeException(nameof(activitySequence));
            if (expeditionsFinished < 0) throw new ArgumentOutOfRangeException(nameof(expeditionsFinished));
            ActivitySequence = activitySequence;
            ExpeditionsFinished = expeditionsFinished;
        }

        /// <summary>Seconds left of the recovery window (the grace when served).</summary>
        public double SecondsBeforeIrreversible => Math.Max(0d, EssentialUnservedGraceSeconds - EssentialUnservedSeconds);

        public static BaseSessionStatus Start(BaseSessionMode mode, IrreversibleRule rule) =>
            new BaseSessionStatus(mode, 0, (rule ?? throw new ArgumentNullException(nameof(rule))).EssentialUnservedGraceSeconds,
                IrreversibleCause.None, null, 0, 0);
    }

    /// <summary>Kind of a bounded piece of work inside the session (activity cycle, separate from the global cycle).</summary>
    public enum ActivityKind
    {
        /// <summary>One sortie: from the first member sealed until everybody who went out is back (or it is cancelled).</summary>
        Expedition,
        /// <summary>A science opportunity completed (phase 2 science task or phase 3 exterior objective). Repeatable.</summary>
        ScienceAssignment,
        WaterBatch,
        AirService,
        Repair,
        Resupply
    }

    public enum ActivityStatus { Active, Completed, Cancelled, Failed }

    /// <summary>One activity as published (immutable). Completing one NEVER ends the continuous session.</summary>
    public sealed class ActivityRecord
    {
        public long Sequence { get; }
        public ActivityKind Kind { get; }
        /// <summary>Stable key (opportunity, manifest, equipment or objective key) for texts.</summary>
        public string Key { get; }
        public ActivityStatus Status { get; }
        public PlayerId StartedBy { get; }
        public double StartedAtSeconds { get; }
        public double? EndedAtSeconds { get; }
        /// <summary>Stable key of the result text (for example "science-returned", "returned-without-science"), or null.</summary>
        public string ResultKey { get; }

        public ActivityRecord(long sequence, ActivityKind kind, string key, ActivityStatus status, PlayerId startedBy,
            double startedAtSeconds, double? endedAtSeconds, string resultKey)
        {
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            Sequence = sequence;
            Kind = OutpostGuard.Defined(kind, nameof(kind));
            Key = StableKey.Require(key, nameof(key));
            Status = OutpostGuard.Defined(status, nameof(status));
            StartedBy = startedBy;
            StartedAtSeconds = OutpostGuard.NonNegative(startedAtSeconds, nameof(startedAtSeconds));
            if (endedAtSeconds.HasValue)
            {
                OutpostGuard.NonNegative(endedAtSeconds.Value, nameof(endedAtSeconds));
                if (endedAtSeconds.Value < startedAtSeconds) throw new ArgumentException("Ends after it starts.", nameof(endedAtSeconds));
            }
            if (endedAtSeconds.HasValue == (status == ActivityStatus.Active))
                throw new ArgumentException("An end time exists exactly when the activity is no longer active.", nameof(endedAtSeconds));
            if (resultKey != null) StableKey.Require(resultKey, nameof(resultKey));
            ResultKey = resultKey;
        }
    }

    /// <summary>
    /// Periodic NON-terminal summary (immutable): what happened between two report times. It informs; it never closes
    /// the session or grants a victory. Deltas are signed changes of the stock over the interval.
    /// </summary>
    public sealed class OperationsReport
    {
        public long Sequence { get; }
        public double FromSeconds { get; }
        public double ToSeconds { get; }
        public int ActivitiesCompleted { get; }
        public int ActivitiesCancelled { get; }
        public double PotableDeltaKilograms { get; }
        public double FoodDeltaKilograms { get; }
        public double EnergyDeltaJoules { get; }

        public OperationsReport(long sequence, double fromSeconds, double toSeconds, int activitiesCompleted, int activitiesCancelled,
            double potableDeltaKilograms, double foodDeltaKilograms, double energyDeltaJoules)
        {
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            Sequence = sequence;
            FromSeconds = OutpostGuard.NonNegative(fromSeconds, nameof(fromSeconds));
            ToSeconds = OutpostGuard.NonNegative(toSeconds, nameof(toSeconds));
            if (toSeconds <= fromSeconds) throw new ArgumentException("A report covers a positive interval.", nameof(toSeconds));
            if (activitiesCompleted < 0) throw new ArgumentOutOfRangeException(nameof(activitiesCompleted));
            if (activitiesCancelled < 0) throw new ArgumentOutOfRangeException(nameof(activitiesCancelled));
            ActivitiesCompleted = activitiesCompleted;
            ActivitiesCancelled = activitiesCancelled;
            PotableDeltaKilograms = OutpostGuard.Finite(potableDeltaKilograms, nameof(potableDeltaKilograms));
            FoodDeltaKilograms = OutpostGuard.Finite(foodDeltaKilograms, nameof(foodDeltaKilograms));
            EnergyDeltaJoules = OutpostGuard.Finite(energyDeltaJoules, nameof(energyDeltaJoules));
        }
    }
}
