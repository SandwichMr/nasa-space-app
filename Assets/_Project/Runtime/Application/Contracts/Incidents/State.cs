using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;

namespace Trainer.Application.Incidents
{
    /// <summary>
    /// Lifecycle of one incident instance. Not every incident goes through every state, and resolving one NEVER ends the session.
    /// Warning = visible symptom/watch (preparation window); Active = effect applied; Intervention = crew working on it (repair
    /// open, controller isolated, shelter instruction in force); Recovery = repaired, waiting to confirm; Resolved / Cancelled.
    /// </summary>
    public enum IncidentPhase { Warning, Active, Intervention, Recovery, Resolved, Cancelled }

    /// <summary>One incident instance as clients may know it (immutable). Planned activation times are never published.</summary>
    public sealed class IncidentRecord
    {
        public Guid Id { get; }
        public long Sequence { get; }
        public IncidentType Kind { get; }
        public string Key { get; }
        public IncidentPhase Phase { get; }
        public double AnnouncedAtSeconds { get; }
        public double? ActiveAtSeconds { get; }
        public double? ClosedAtSeconds { get; }
        /// <summary>Stable key of the outcome once closed (e.g. "repaired", "reset", "shelter-period-ended", "epoch-restarted").</summary>
        public string ResultKey { get; }

        public IncidentRecord(Guid id, long sequence, IncidentType kind, string key, IncidentPhase phase, double announcedAtSeconds,
            double? activeAtSeconds, double? closedAtSeconds, string resultKey)
        {
            if (id == Guid.Empty) throw new ArgumentException("Incident id required.", nameof(id));
            Id = id;
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            Sequence = sequence;
            Kind = OutpostGuard.Defined(kind, nameof(kind));
            Key = StableKey.Require(key, nameof(key));
            Phase = OutpostGuard.Defined(phase, nameof(phase));
            AnnouncedAtSeconds = OutpostGuard.NonNegative(announcedAtSeconds, nameof(announcedAtSeconds));
            if (activeAtSeconds.HasValue && OutpostGuard.NonNegative(activeAtSeconds.Value, nameof(activeAtSeconds)) < announcedAtSeconds)
                throw new ArgumentException("Activation precedes the announcement.", nameof(activeAtSeconds));
            bool closed = phase == IncidentPhase.Resolved || phase == IncidentPhase.Cancelled;
            if (closed != closedAtSeconds.HasValue || closed != (resultKey != null))
                throw new ArgumentException("A closed incident has an end time and a result; an open one has neither.");
            if (closedAtSeconds.HasValue && OutpostGuard.NonNegative(closedAtSeconds.Value, nameof(closedAtSeconds)) < announcedAtSeconds)
                throw new ArgumentException("Closed before announced.", nameof(closedAtSeconds));
            if (phase != IncidentPhase.Warning && phase != IncidentPhase.Cancelled && !activeAtSeconds.HasValue)
                throw new ArgumentException("Past the warning an incident has an activation time.", nameof(activeAtSeconds));
            ActiveAtSeconds = activeAtSeconds;
            ClosedAtSeconds = closedAtSeconds;
            ResultKey = resultKey == null ? null : StableKey.Require(resultKey, nameof(resultKey));
        }

        public bool IsOpen => Phase != IncidentPhase.Resolved && Phase != IncidentPhase.Cancelled;
    }

    /// <summary>What the director is doing, as clients may know it (no future schedule is ever published).</summary>
    public enum DirectorPhase
    {
        /// <summary>Start of an epoch: no setback before the grace window ends.</summary>
        Grace,
        /// <summary>May propose a setback at its next evaluation.</summary>
        Ready,
        /// <summary>The simultaneity budget is full.</summary>
        Busy,
        /// <summary>Rest window after a resolution.</summary>
        Rest,
        /// <summary>Session not Running (Ready, Paused or Interrupted): no evaluation, no dispatch.</summary>
        Halted
    }

    /// <summary>Director identity and phase (immutable). Seed + PRNG version + configuration hash + dataset + ordered inputs reproduce a run.</summary>
    public sealed class DirectorStatus
    {
        public const int CurrentPrngVersion = 1;

        public DirectorPhase Phase { get; }
        public ulong Seed { get; }
        public int PrngVersion { get; }
        public long Evaluations { get; }
        /// <summary>Lowercase hex SHA-256 of the canonical director/catalog configuration text.</summary>
        public string ConfigurationHash { get; }

        public DirectorStatus(DirectorPhase phase, ulong seed, int prngVersion, long evaluations, string configurationHash)
        {
            Phase = OutpostGuard.Defined(phase, nameof(phase));
            Seed = seed;
            if (prngVersion != CurrentPrngVersion) throw new ArgumentOutOfRangeException(nameof(prngVersion), "Unknown PRNG version.");
            PrngVersion = prngVersion;
            if (evaluations < 0) throw new ArgumentOutOfRangeException(nameof(evaluations));
            Evaluations = evaluations;
            if (configurationHash == null || configurationHash.Length != 64) throw new ArgumentException("SHA-256 hex expected.", nameof(configurationHash));
            ConfigurationHash = configurationHash;
        }
    }

    /// <summary>Why the director chose or rejected a candidate at an evaluation (immutable, bounded window).</summary>
    public sealed class DirectorDecisionRecord
    {
        public long Sequence { get; }
        public double AtSeconds { get; }
        /// <summary>Catalog key considered, or null when the pressure roll proposed nothing.</summary>
        public string CandidateKey { get; }
        public bool Accepted { get; }
        public string ReasonKey { get; }

        public DirectorDecisionRecord(long sequence, double atSeconds, string candidateKey, bool accepted, string reasonKey)
        {
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            Sequence = sequence;
            AtSeconds = OutpostGuard.NonNegative(atSeconds, nameof(atSeconds));
            CandidateKey = candidateKey == null ? null : StableKey.Require(candidateKey, nameof(candidateKey));
            if (accepted && candidateKey == null) throw new ArgumentException("An accepted decision names its candidate.");
            Accepted = accepted;
            ReasonKey = StableKey.Require(reasonKey, nameof(reasonKey));
        }
    }

    /// <summary>What a power inspection can reveal.</summary>
    [Flags]
    public enum PowerFindings
    {
        None = 0,
        /// <summary>Generation below the array's declared value.</summary>
        GenerationLow = 1,
        /// <summary>Battery discharge limited below its declared maximum.</summary>
        DischargeLimited = 2,
        /// <summary>A converter tripped (a reset can clear it).</summary>
        ConverterTripped = 4,
        /// <summary>The discharge controller is damaged (needs isolation and a part).</summary>
        ControllerDamaged = 8
    }

    /// <summary>One power inspection (immutable). Current while the power state sequence is unchanged and before ValidUntil.</summary>
    public sealed class PowerObservation
    {
        public long Sequence { get; }
        public PlayerId ObservedBy { get; }
        public double ObservedAtSeconds { get; }
        public double ValidUntilSeconds { get; }
        public long StateSequence { get; }
        public PowerFindings Findings { get; }
        public double GenerationWatts { get; }
        public double MaxDischargeWatts { get; }
        public bool Current { get; }

        public PowerObservation(long sequence, PlayerId observedBy, double observedAtSeconds, double validUntilSeconds, long stateSequence,
            PowerFindings findings, double generationWatts, double maxDischargeWatts, bool current)
        {
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            Sequence = sequence;
            if (!observedBy.IsValid) throw new ArgumentException("Observer required.", nameof(observedBy));
            ObservedBy = observedBy;
            ObservedAtSeconds = OutpostGuard.NonNegative(observedAtSeconds, nameof(observedAtSeconds));
            ValidUntilSeconds = OutpostGuard.Finite(validUntilSeconds, nameof(validUntilSeconds));
            if (!(validUntilSeconds > observedAtSeconds)) throw new ArgumentException("Valid after observed.", nameof(validUntilSeconds));
            if (stateSequence < 0) throw new ArgumentOutOfRangeException(nameof(stateSequence));
            StateSequence = stateSequence;
            if (((int)findings & ~15) != 0) throw new ArgumentOutOfRangeException(nameof(findings));
            Findings = findings;
            GenerationWatts = OutpostGuard.NonNegative(generationWatts, nameof(generationWatts));
            MaxDischargeWatts = OutpostGuard.NonNegative(maxDischargeWatts, nameof(maxDischargeWatts));
            Current = current;
        }
    }

    /// <summary>The power panel as clients may know it (immutable). Effective values are the capacities in force (truth of the
    /// installed hardware, like a nameplate read by the panel); the fault CAUSE is only known through a current observation.</summary>
    public sealed class PowerEquipmentStatus
    {
        public StationId Station { get; }
        public bool Isolated { get; }
        public bool PartReplaced { get; }
        public EquipmentKnowledge Knowledge { get; }
        public long StateSequence { get; }
        public PowerObservation Observation { get; }
        public double EffectiveGenerationWatts { get; }
        public double EffectiveMaxDischargeWatts { get; }
        public double AddedEssentialWatts { get; }

        public PowerEquipmentStatus(StationId station, bool isolated, bool partReplaced, EquipmentKnowledge knowledge, long stateSequence,
            PowerObservation observation, double effectiveGenerationWatts, double effectiveMaxDischargeWatts, double addedEssentialWatts)
        {
            if (!station.IsValid) throw new ArgumentException("Station required.", nameof(station));
            Station = station;
            if (partReplaced && !isolated) throw new ArgumentException("A part is replaced only while isolated.", nameof(partReplaced));
            Isolated = isolated;
            PartReplaced = partReplaced;
            Knowledge = OutpostGuard.Defined(knowledge, nameof(knowledge));
            if (stateSequence < 0) throw new ArgumentOutOfRangeException(nameof(stateSequence));
            StateSequence = stateSequence;
            if (knowledge != EquipmentKnowledge.Unknown && (observation == null || !observation.Current))
                throw new ArgumentException("Knowledge comes only from a current observation.", nameof(knowledge));
            Observation = observation;
            EffectiveGenerationWatts = OutpostGuard.NonNegative(effectiveGenerationWatts, nameof(effectiveGenerationWatts));
            EffectiveMaxDischargeWatts = OutpostGuard.NonNegative(effectiveMaxDischargeWatts, nameof(effectiveMaxDischargeWatts));
            AddedEssentialWatts = OutpostGuard.NonNegative(addedEssentialWatts, nameof(addedEssentialWatts));
        }
    }

    /// <summary>One issued space-weather message with its session times and validity (immutable).</summary>
    public sealed class EpisodeMessageView
    {
        public EpisodeMessage Message { get; }
        public double IssuedAtSeconds { get; }
        public double ValidUntilSeconds { get; }
        public double? ShelterBySeconds { get; }
        public AdvisoryValidity Validity { get; }

        public EpisodeMessageView(EpisodeMessage message, double issuedAtSeconds, double validUntilSeconds, double? shelterBySeconds, AdvisoryValidity validity)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            IssuedAtSeconds = OutpostGuard.NonNegative(issuedAtSeconds, nameof(issuedAtSeconds));
            ValidUntilSeconds = OutpostGuard.Finite(validUntilSeconds, nameof(validUntilSeconds));
            if (!(validUntilSeconds > issuedAtSeconds)) throw new ArgumentException("Valid after issued.", nameof(validUntilSeconds));
            if (shelterBySeconds.HasValue != message.ShelterByOffsetSeconds.HasValue) throw new ArgumentException("Deadline mismatch.", nameof(shelterBySeconds));
            if (shelterBySeconds.HasValue && OutpostGuard.Finite(shelterBySeconds.Value, nameof(shelterBySeconds)) < issuedAtSeconds)
                throw new ArgumentException("Deadline before issue.", nameof(shelterBySeconds));
            ShelterBySeconds = shelterBySeconds;
            Validity = OutpostGuard.Defined(validity, nameof(validity));
        }
    }

    /// <summary>What a member's shelter state is while an instruction is in force (qualitative; no dose, no shielding percentage).</summary>
    public enum MemberShelterState
    {
        NotRequired,
        Protected,
        NotInShelter,
        OnSuitSupply,
        ShelterNotPrepared,
        ShelterServiceUnavailable
    }

    /// <summary>What one member knows and how they are placed during the current episode (immutable).</summary>
    public sealed class MemberProtection
    {
        public PlayerId Player { get; }
        public MemberShelterState State { get; }
        public bool ReceivedWatch { get; }
        public bool ReceivedInstruction { get; }

        public MemberProtection(PlayerId player, MemberShelterState state, bool receivedWatch, bool receivedInstruction)
        {
            if (!player.IsValid) throw new ArgumentException("Player required.", nameof(player));
            Player = player;
            State = OutpostGuard.Defined(state, nameof(state));
            ReceivedWatch = receivedWatch;
            ReceivedInstruction = receivedInstruction;
        }
    }

    /// <summary>
    /// The current (or last) space-weather episode as clients may know it (immutable). Only ISSUED messages, the records they
    /// quote, who received what, the instruction in force and the qualitative protection of each member. Unissued messages,
    /// the didactic onset/end and any future record never appear here.
    /// </summary>
    public sealed class SpaceWeatherStatus
    {
        public Guid IncidentId { get; }
        public string EpisodeKey { get; }
        public DataMode Mode { get; }
        public string Label { get; }
        public string TimelineNote { get; }
        public DatasetManifest Dataset { get; }
        public IReadOnlyList<SourceRecord> VisibleRecords { get; }
        public IReadOnlyList<EpisodeMessageView> Issued { get; }
        public IReadOnlyList<AdvisoryReception> Receptions { get; }
        public ShelterInstructionStatus Instruction { get; }
        public IReadOnlyList<MemberProtection> Members { get; }
        /// <summary>Assessments once the shelter period ended (empty before).</summary>
        public IReadOnlyList<ProtectionAssessment> Assessments { get; }

        public SpaceWeatherStatus(Guid incidentId, string episodeKey, DataMode mode, string label, string timelineNote, DatasetManifest dataset,
            IEnumerable<SourceRecord> visibleRecords, IEnumerable<EpisodeMessageView> issued, IEnumerable<AdvisoryReception> receptions,
            ShelterInstructionStatus instruction, IEnumerable<MemberProtection> members, IEnumerable<ProtectionAssessment> assessments)
        {
            if (incidentId == Guid.Empty) throw new ArgumentException("Incident id required.", nameof(incidentId));
            IncidentId = incidentId;
            EpisodeKey = StableKey.Require(episodeKey, nameof(episodeKey));
            Mode = OutpostGuard.Defined(mode, nameof(mode));
            if (mode == DataMode.Current) throw new ArgumentException("Current observation is not implemented.", nameof(mode));
            Label = ProvenanceText.Required(label, nameof(label));
            TimelineNote = ProvenanceText.Required(timelineNote, nameof(timelineNote));
            if ((mode == DataMode.Historical) != (dataset != null)) throw new ArgumentException("Historical ↔ dataset.", nameof(dataset));
            Dataset = dataset;
            VisibleRecords = Bounded(visibleRecords, IncidentLimits.MaxSourceRecords, nameof(visibleRecords));
            Issued = Bounded(issued, IncidentLimits.MaxEpisodeMessages, nameof(issued));
            var quoted = new HashSet<string>(StringComparer.Ordinal);
            foreach (EpisodeMessageView m in Issued)
                foreach (string id in m.Message.SourceRecordIds) quoted.Add(id);
            foreach (SourceRecord r in VisibleRecords)
                if (!quoted.Contains(r.SourceId)) throw new ArgumentException($"Record '{r.SourceId}' is not quoted by an issued message.", nameof(visibleRecords));
            Receptions = Bounded(receptions, IncidentLimits.MaxReceptions, nameof(receptions));
            Instruction = instruction ?? throw new ArgumentNullException(nameof(instruction));
            Members = Bounded(members, OutpostLimits.MaxCrew, nameof(members));
            Assessments = Bounded(assessments, IncidentLimits.MaxAssessments, nameof(assessments));
        }

        internal static IReadOnlyList<T> Bounded<T>(IEnumerable<T> items, int max, string name) where T : class
        {
            var list = new List<T>(items ?? throw new ArgumentNullException(name));
            if (list.Count > max || list.Contains(null)) throw new ArgumentException($"At most {max} non-null items.", name);
            return new ReadOnlyCollection<T>(list);
        }
    }

    public enum AssignmentState { Offered, Accepted, Completed, Cancelled, Expired, Postponed }

    /// <summary>One assignment offer/assignment (immutable). The reward is applied once (<see cref="Rewarded"/>), never twice.</summary>
    public sealed class AssignmentRecord
    {
        public long Sequence { get; }
        public string TemplateKey { get; }
        public string ActivityKey { get; }
        public bool Exterior { get; }
        public AssignmentState State { get; }
        public double OfferedAtSeconds { get; }
        /// <summary>Offer expiry (Offered/Postponed: when it reappears) or deadline (Accepted).</summary>
        public double DueAtSeconds { get; }
        public double? AcceptedAtSeconds { get; }
        public double? ClosedAtSeconds { get; }
        public PlayerId AcceptedBy { get; }
        public int RewardCredits { get; }
        public bool Rewarded { get; }

        public AssignmentRecord(long sequence, string templateKey, string activityKey, bool exterior, AssignmentState state, double offeredAtSeconds,
            double dueAtSeconds, double? acceptedAtSeconds, double? closedAtSeconds, PlayerId acceptedBy, int rewardCredits, bool rewarded)
        {
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            Sequence = sequence;
            TemplateKey = StableKey.Require(templateKey, nameof(templateKey));
            ActivityKey = StableKey.Require(activityKey, nameof(activityKey));
            Exterior = exterior;
            State = OutpostGuard.Defined(state, nameof(state));
            OfferedAtSeconds = OutpostGuard.NonNegative(offeredAtSeconds, nameof(offeredAtSeconds));
            DueAtSeconds = OutpostGuard.Finite(dueAtSeconds, nameof(dueAtSeconds));
            bool accepted = state == AssignmentState.Accepted || state == AssignmentState.Completed || state == AssignmentState.Cancelled
                || (state == AssignmentState.Expired && acceptedAtSeconds.HasValue);
            if (accepted != acceptedAtSeconds.HasValue || accepted != acceptedBy.IsValid)
                throw new ArgumentException("Acceptance time and acceptor go together with an accepted state.");
            bool closed = state == AssignmentState.Completed || state == AssignmentState.Cancelled || state == AssignmentState.Expired;
            if (closed != closedAtSeconds.HasValue) throw new ArgumentException("Closed states have an end time.", nameof(closedAtSeconds));
            if (rewarded != (state == AssignmentState.Completed)) throw new ArgumentException("Only a completed assignment is rewarded (once).", nameof(rewarded));
            AcceptedAtSeconds = acceptedAtSeconds;
            ClosedAtSeconds = closedAtSeconds;
            AcceptedBy = acceptedBy;
            if (rewardCredits < 1 || rewardCredits > 4) throw new ArgumentOutOfRangeException(nameof(rewardCredits));
            RewardCredits = rewardCredits;
            Rewarded = rewarded;
        }

        public bool IsOpen => State == AssignmentState.Offered || State == AssignmentState.Accepted || State == AssignmentState.Postponed;
    }

    /// <summary>The bounded upgrade as clients may know it (immutable).</summary>
    public sealed class UpgradeStatus
    {
        public string Key { get; }
        public bool Installed { get; }
        public int CreditCost { get; }
        public string SpareKey { get; }
        public double RecoveryFraction { get; }
        public double AddedEssentialWatts { get; }
        public double? InstalledAtSeconds { get; }

        public UpgradeStatus(string key, bool installed, int creditCost, string spareKey, double recoveryFraction, double addedEssentialWatts,
            double? installedAtSeconds)
        {
            Key = StableKey.Require(key, nameof(key));
            if (installed != installedAtSeconds.HasValue) throw new ArgumentException("Installed ↔ time.", nameof(installedAtSeconds));
            Installed = installed;
            if (creditCost < 1 || creditCost > IncidentLimits.MaxCredits) throw new ArgumentOutOfRangeException(nameof(creditCost));
            CreditCost = creditCost;
            SpareKey = StableKey.Require(spareKey, nameof(spareKey));
            if (!(recoveryFraction > 0d && recoveryFraction < 1d)) throw new ArgumentOutOfRangeException(nameof(recoveryFraction));
            RecoveryFraction = recoveryFraction;
            AddedEssentialWatts = OutpostGuard.Positive(addedEssentialWatts, nameof(addedEssentialWatts));
            InstalledAtSeconds = installedAtSeconds.HasValue ? OutpostGuard.NonNegative(installedAtSeconds.Value, nameof(installedAtSeconds)) : (double?)null;
        }
    }

    /// <summary>A phase 5 timed operation in progress (immutable).</summary>
    public sealed class IncidentOperationProgress
    {
        public Guid CommandId { get; }
        public StationId Station { get; }
        public IncidentAction Action { get; }
        public PlayerId Performer { get; }
        public double WorkSeconds { get; }
        public double DurationSeconds { get; }

        public IncidentOperationProgress(Guid commandId, StationId station, IncidentAction action, PlayerId performer, double workSeconds, double durationSeconds)
        {
            if (commandId == Guid.Empty) throw new ArgumentException("Command id required.", nameof(commandId));
            CommandId = commandId;
            if (!station.IsValid) throw new ArgumentException("Station required.", nameof(station));
            Station = station;
            Action = OutpostGuard.Defined(action, nameof(action));
            if (action != IncidentAction.InspectPower && action != IncidentAction.ResetConverter && action != IncidentAction.ReplacePowerPart
                && action != IncidentAction.InstallUpgrade)
                throw new ArgumentException("Only timed actions have progress.", nameof(action));
            if (!performer.IsValid) throw new ArgumentException("Performer required.", nameof(performer));
            Performer = performer;
            DurationSeconds = OutpostGuard.Positive(durationSeconds, nameof(durationSeconds));
            WorkSeconds = OutpostGuard.NonNegative(workSeconds, nameof(workSeconds));
            if (workSeconds > durationSeconds) throw new ArgumentException("Work beyond duration.", nameof(workSeconds));
        }
    }

    /// <summary>Assessment of an accepted phase 5 decision (immutable, bounded window), e.g. resetting a damaged controller.</summary>
    public sealed class IncidentFeedback
    {
        public Guid CommandId { get; }
        public PlayerId Performer { get; }
        public IncidentAction Action { get; }
        public double AtSeconds { get; }
        public DecisionAssessment Assessment { get; }
        public string ExplanationKey { get; }

        public IncidentFeedback(Guid commandId, PlayerId performer, IncidentAction action, double atSeconds, DecisionAssessment assessment, string explanationKey)
        {
            if (commandId == Guid.Empty) throw new ArgumentException("Command id required.", nameof(commandId));
            CommandId = commandId;
            if (!performer.IsValid) throw new ArgumentException("Performer required.", nameof(performer));
            Performer = performer;
            Action = OutpostGuard.Defined(action, nameof(action));
            AtSeconds = OutpostGuard.NonNegative(atSeconds, nameof(atSeconds));
            Assessment = OutpostGuard.Defined(assessment, nameof(assessment));
            ExplanationKey = StableKey.Require(explanationKey, nameof(explanationKey));
        }
    }

    /// <summary>Data mode in use and why (immutable). A failed historical load is FallbackSynthetic, never "no hazard".</summary>
    public sealed class DataStatus
    {
        public DataMode Requested { get; }
        public DataMode InUse { get; }
        public DataAvailability Availability { get; }
        public DatasetManifest Dataset { get; }
        /// <summary>Stable key of the load problem when falling back (e.g. "dataset-missing", "dataset-hash-mismatch").</summary>
        public string ProblemKey { get; }

        public DataStatus(DataMode requested, DataMode inUse, DataAvailability availability, DatasetManifest dataset, string problemKey)
        {
            Requested = OutpostGuard.Defined(requested, nameof(requested));
            InUse = OutpostGuard.Defined(inUse, nameof(inUse));
            Availability = OutpostGuard.Defined(availability, nameof(availability));
            if (requested == DataMode.Current || inUse == DataMode.Current) throw new ArgumentException("Current observation is not implemented.");
            switch (availability)
            {
                case DataAvailability.Loaded:
                    if (requested != DataMode.Historical || inUse != DataMode.Historical || dataset == null || problemKey != null)
                        throw new ArgumentException("Loaded = historical dataset in use, no problem.");
                    break;
                case DataAvailability.FallbackSynthetic:
                    if (requested != DataMode.Historical || inUse != DataMode.Synthetic || dataset != null || problemKey == null)
                        throw new ArgumentException("Fallback = historical requested, synthetic in use, with a problem key.");
                    break;
                default:
                    if (requested != DataMode.Synthetic || inUse != DataMode.Synthetic || dataset != null || problemKey != null)
                        throw new ArgumentException("NotRequested = synthetic requested and in use.");
                    break;
            }
            Dataset = dataset;
            ProblemKey = problemKey == null ? null : StableKey.Require(problemKey, nameof(problemKey));
        }
    }

    /// <summary>
    /// The complete phase 5 state around the phase 4 snapshot of the SAME epoch and revision (immutable, always full, never a
    /// delta; ordered by the phase 2 (Epoch, Revision)). Contains only what clients may know.
    /// </summary>
    public sealed class LivingBaseSnapshot
    {
        public BaseOperationsSnapshot BaseOperations { get; }
        public LivingBaseSetup Setup { get; }
        public DataStatus Data { get; }
        public DirectorStatus Director { get; }
        public IReadOnlyList<IncidentRecord> Incidents { get; }
        public PowerEquipmentStatus Power { get; }
        public SpaceWeatherStatus SpaceWeather { get; }
        public IReadOnlyList<AssignmentRecord> Assignments { get; }
        public int Credits { get; }
        public int CreditsEarned { get; }
        public UpgradeStatus Upgrade { get; }
        public IReadOnlyList<IncidentOperationProgress> Operations { get; }
        public IReadOnlyList<IncidentFeedback> Feedback { get; }
        public IReadOnlyList<DirectorDecisionRecord> Decisions { get; }

        public long Epoch => BaseOperations.Expedition.Outpost.Epoch;
        public long Revision => BaseOperations.Expedition.Outpost.Revision;
        public double SimulatedTimeSeconds => BaseOperations.Expedition.Outpost.SimulatedTimeSeconds;

        public LivingBaseSnapshot(BaseOperationsSnapshot baseOperations, LivingBaseSetup setup, DataStatus data, DirectorStatus director,
            IEnumerable<IncidentRecord> incidents, PowerEquipmentStatus power, SpaceWeatherStatus spaceWeather, IEnumerable<AssignmentRecord> assignments,
            int credits, int creditsEarned, UpgradeStatus upgrade, IEnumerable<IncidentOperationProgress> operations, IEnumerable<IncidentFeedback> feedback,
            IEnumerable<DirectorDecisionRecord> decisions)
        {
            BaseOperations = baseOperations ?? throw new ArgumentNullException(nameof(baseOperations));
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            Data = data ?? throw new ArgumentNullException(nameof(data));
            if (data.Requested != setup.RequestedMode) throw new ArgumentException("Data status must answer the setup's requested mode.", nameof(data));
            Director = director ?? throw new ArgumentNullException(nameof(director));
            if (director.Seed != setup.Seed) throw new ArgumentException("Director seed ≠ setup seed.", nameof(director));
            Incidents = SpaceWeatherStatus.Bounded(incidents, IncidentLimits.MaxIncidents, nameof(incidents));
            double now = SimulatedTimeSeconds;
            long last = 0;
            var seen = new HashSet<Guid>();
            foreach (IncidentRecord i in Incidents)
            {
                if (i.Sequence <= last) throw new ArgumentException("Incidents in sequence order.", nameof(incidents));
                if (!seen.Add(i.Id)) throw new ArgumentException("Duplicate incident id.", nameof(incidents));
                if (i.AnnouncedAtSeconds > now + 1e-6 || (i.ClosedAtSeconds ?? 0) > now + 1e-6) throw new ArgumentException("Incident from the future.", nameof(incidents));
                last = i.Sequence;
            }
            Power = power ?? throw new ArgumentNullException(nameof(power));
            if (spaceWeather != null && !seen.Contains(spaceWeather.IncidentId))
                throw new ArgumentException("The space-weather status belongs to a published incident.", nameof(spaceWeather));
            SpaceWeather = spaceWeather;
            Assignments = SpaceWeatherStatus.Bounded(assignments, IncidentLimits.MaxAssignments, nameof(assignments));
            long lastAssignment = 0;
            foreach (AssignmentRecord a in Assignments)
            {
                if (a.Sequence <= lastAssignment) throw new ArgumentException("Assignments in sequence order.", nameof(assignments));
                if (a.OfferedAtSeconds > now + 1e-6) throw new ArgumentException("Assignment from the future.", nameof(assignments));
                lastAssignment = a.Sequence;
            }
            if (credits < 0 || credits > IncidentLimits.MaxCredits) throw new ArgumentOutOfRangeException(nameof(credits));
            if (creditsEarned < credits) throw new ArgumentOutOfRangeException(nameof(creditsEarned), "Credits never exceed what was earned.");
            Credits = credits;
            CreditsEarned = creditsEarned;
            Upgrade = upgrade ?? throw new ArgumentNullException(nameof(upgrade));
            Operations = SpaceWeatherStatus.Bounded(operations, IncidentLimits.MaxOperations, nameof(operations));
            var performers = new HashSet<PlayerId>();
            var stations = new HashSet<StationId>();
            foreach (IncidentOperationProgress op in Operations)
                if (!performers.Add(op.Performer) || !stations.Add(op.Station)) throw new ArgumentException("One operation per member and station.", nameof(operations));
            Feedback = SpaceWeatherStatus.Bounded(feedback, IncidentLimits.MaxFeedback, nameof(feedback));
            Decisions = SpaceWeatherStatus.Bounded(decisions, IncidentLimits.MaxDirectorDecisions, nameof(decisions));
        }

        public bool TryGetIncident(Guid id, out IncidentRecord record)
        {
            foreach (IncidentRecord i in Incidents)
                if (i.Id == id)
                {
                    record = i;
                    return true;
                }
            record = null;
            return false;
        }
    }
}
