using System;
using System.Collections.Generic;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.Incidents;

namespace Trainer.Application.Incidents
{
    /// <summary>Per-epoch state of the phase 5 authority, epoch changes and the snapshot builder.</summary>
    public sealed partial class LivingBaseAuthority
    {
        private sealed class Incident
        {
            public Guid Id;
            public long Sequence;
            public IncidentDefinition Definition;
            public IncidentPhase Phase;
            public double AnnouncedAt;
            public double? ActiveAt;
            public double? ClosedAt;
            public string Result;
            /// <summary>End of the warning or recovery window (hidden schedule, never published).</summary>
            public double DueAt;
            public Guid ModifierId;
            public Guid IsolatedModifierId;
            public bool IsOpen => Phase != IncidentPhase.Resolved && Phase != IncidentPhase.Cancelled;
            public IncidentType Type => Definition.Kind;
        }

        private sealed class Operation
        {
            public Guid CommandId;
            public PlayerId Performer;
            public StationId Station;
            public IncidentAction Action;
            public double Work;
            public double Duration;
        }

        // Per epoch (reset by SyncEpoch).
        private long currentEpoch = -1;
        private DirectorRandom directorRandom;
        private DirectorRandom assignmentRandom;
        private DirectorRandom episodeRandom;
        private long evaluations;
        private double nextEvaluationAt;
        private double restUntil;
        private string lastIncidentKey;
        private readonly Dictionary<string, double> lastStartedAt = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly List<Incident> incidents = new List<Incident>();
        private long incidentSequence;
        private long episodeOrdinal;
        private bool historicalPlayed;
        private EpisodeRun lastEpisode;
        private Incident lastEpisodeIncident;

        private bool converterTripped;
        private bool controllerDamaged;
        private bool powerIsolated;
        private bool partReplaced;
        private long powerStateSequence;
        private long observationSequence;
        private PowerObservation lastObservation;

        private readonly List<Assignment> assignments = new List<Assignment>();
        private long assignmentSequence;
        private readonly Dictionary<string, double> templateReadyAt = new Dictionary<string, double>(StringComparer.Ordinal);
        private string lastOfferedTemplate;
        private readonly HashSet<long> consumedActivities = new HashSet<long>();
        private int credits;
        private int creditsEarned;
        private double? upgradeInstalledAt;

        private readonly List<Operation> operations = new List<Operation>();
        private readonly List<IncidentFeedback> feedback = new List<IncidentFeedback>();
        private readonly List<DirectorDecisionRecord> decisions = new List<DirectorDecisionRecord>();
        private long decisionSequence;

        /// <summary>Detects a new phase 2 epoch (Restart) and starts the phase 5 state again from the setup. The phase 4
        /// restart already reset the D1 models (modifiers, upgrade and phase 5 ids cleared). True when the epoch changed.</summary>
        private bool SyncEpoch()
        {
            long epoch = Expedition.CurrentEpoch;
            if (epoch == currentEpoch) return false;
            currentEpoch = epoch;
            directorRandom = new DirectorRandom(Setup.Seed, epoch, DirectorRandom.DirectorStream);
            assignmentRandom = new DirectorRandom(Setup.Seed, epoch, DirectorRandom.AssignmentStream);
            episodeRandom = new DirectorRandom(Setup.Seed, epoch, DirectorRandom.EpisodeStream);
            evaluations = 0;
            nextEvaluationAt = Scenario.Director.EvaluationIntervalSeconds;
            restUntil = 0d;
            lastIncidentKey = null;
            lastStartedAt.Clear();
            incidents.Clear();
            incidentSequence = 0;
            episodeOrdinal = 0;
            historicalPlayed = false;
            lastEpisode = null;
            lastEpisodeIncident = null;
            converterTripped = controllerDamaged = powerIsolated = partReplaced = false;
            powerStateSequence = 0;
            observationSequence = 0;
            lastObservation = null;
            assignments.Clear();
            assignmentSequence = 0;
            templateReadyAt.Clear();
            lastOfferedTemplate = null;
            consumedActivities.Clear();
            credits = creditsEarned = 0;
            upgradeInstalledAt = null;
            operations.Clear();
            feedback.Clear();
            decisions.Clear();
            decisionSequence = 0;
            FillBoard(Expedition.SimulatedNow);
            return true;
        }

        // ---------------------------------------------------------------- snapshot

        private LivingBaseSnapshot BuildSnapshot(BaseOperationsSnapshot b)
        {
            double now = b.Expedition.Outpost.SimulatedTimeSeconds;
            var incidentRecords = new List<IncidentRecord>();
            int firstIncident = Math.Max(0, incidents.Count - IncidentLimits.MaxIncidents);
            for (int i = firstIncident; i < incidents.Count; i++)
            {
                Incident x = incidents[i];
                incidentRecords.Add(new IncidentRecord(x.Id, x.Sequence, x.Type, x.Definition.Key, x.Phase, x.AnnouncedAt, x.ActiveAt,
                    x.ClosedAt, x.Result));
            }
            SpaceWeatherStatus weather = null;
            if (lastEpisodeIncident != null && incidents.IndexOf(lastEpisodeIncident) >= firstIncident)
                weather = lastEpisode.Status(lastEpisodeIncident.Id, now);

            var assignmentRecords = new List<AssignmentRecord>();
            foreach (Assignment a in PublishedAssignments())
                assignmentRecords.Add(a.ToRecord());

            var ops = new List<IncidentOperationProgress>();
            foreach (Operation o in operations)
                ops.Add(new IncidentOperationProgress(o.CommandId, o.Station, o.Action, o.Performer, Math.Min(o.Work, o.Duration), o.Duration));

            UpgradeDefinition u = Scenario.Upgrade;
            var upgrade = new UpgradeStatus(u.Key, upgradeInstalledAt.HasValue, u.CreditCost, u.SpareKey, u.RecoveryFraction,
                u.AddedEssentialWatts, upgradeInstalledAt);

            return new LivingBaseSnapshot(b, Setup, dataStatus, new DirectorStatus(DirectorPhaseAt(now), Setup.Seed,
                    DirectorStatus.CurrentPrngVersion, evaluations, configurationHash), incidentRecords, PowerStatus(now), weather,
                assignmentRecords, credits, creditsEarned, upgrade, ops, Tail(feedback, IncidentLimits.MaxFeedback),
                Tail(decisions, IncidentLimits.MaxDirectorDecisions));
        }

        private PowerEquipmentStatus PowerStatus(double now)
        {
            PowerCapacityState capacity = ((IPowerCapacityModifiers)outpost).PowerCapacity;
            PowerObservation observation = null;
            EquipmentKnowledge knowledge = EquipmentKnowledge.Unknown;
            if (lastObservation != null)
            {
                bool current = IsCurrent(lastObservation, now);
                observation = new PowerObservation(lastObservation.Sequence, lastObservation.ObservedBy, lastObservation.ObservedAtSeconds,
                    lastObservation.ValidUntilSeconds, lastObservation.StateSequence, lastObservation.Findings, lastObservation.GenerationWatts,
                    lastObservation.MaxDischargeWatts, current);
                if (current)
                    knowledge = lastObservation.Findings == PowerFindings.None ? EquipmentKnowledge.Nominal : EquipmentKnowledge.Degraded;
            }
            var power = Scenario.BaseOperations.Expedition.Outpost.Simulation.Power;
            return new PowerEquipmentStatus(Scenario.PowerStation, powerIsolated, partReplaced, knowledge, powerStateSequence, observation,
                power.GenerationWatts * capacity.GenerationFactor, power.MaxDischargeWatts * capacity.DischargeFactor,
                capacity.AddedEssentialWatts);
        }

        private bool IsCurrent(PowerObservation observation, double now) =>
            observation.StateSequence == powerStateSequence && now < observation.ValidUntilSeconds;

        private DirectorPhase DirectorPhaseAt(double now)
        {
            if (Expedition.CurrentState != SessionState.Running) return DirectorPhase.Halted;
            if (now < Scenario.Director.InitialGraceSeconds) return DirectorPhase.Grace;
            if (OpenIncidents() >= Scenario.Director.MaxSimultaneous) return DirectorPhase.Busy;
            if (now < restUntil) return DirectorPhase.Rest;
            return DirectorPhase.Ready;
        }

        private int OpenIncidents()
        {
            int n = 0;
            foreach (Incident i in incidents) if (i.IsOpen) n++;
            return n;
        }

        private static List<T> Tail<T>(List<T> items, int max) =>
            items.Count <= max ? new List<T>(items) : items.GetRange(items.Count - max, max);

        private static void Bound<T>(List<T> items, int max)
        {
            if (items.Count > max) items.RemoveRange(0, items.Count - max);
        }
    }
}
