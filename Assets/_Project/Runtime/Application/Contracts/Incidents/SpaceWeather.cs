using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Application.Outpost;

namespace Trainer.Application.Incidents
{
    /// <summary>Kinds of phase 5 space-weather messages. Watch precedes any instruction; the rest map onto the phase 3 chain.</summary>
    public enum EpisodeMessageKind
    {
        /// <summary>"Increased particle flux possible": prepare (postpone EVA starts, prepare shelter). No shelter deadline.</summary>
        Watch,
        /// <summary>Return from EVA and shelter by a deadline (phase 3 ShelterInstruction).</summary>
        ShelterInstruction,
        /// <summary>New information replacing an earlier message (phase 3 InstructionUpdate).</summary>
        InstructionUpdate,
        /// <summary>The SCENARIO ends the shelter period (phase 3 InstructionEnd). Never a NASA all-clear.</summary>
        InstructionEnd
    }

    /// <summary>Why a message exists: quoting a real source message, or an explicit didactic decision of the training scenario.</summary>
    public enum MessageBasis
    {
        /// <summary>Paraphrases a real DONKI notification listed in <see cref="EpisodeMessage.SourceRecordIds"/>.</summary>
        SourceNotification,
        /// <summary>Training decision (deadline, instruction, end of shelter period). Not issued by NASA.</summary>
        DidacticDecision,
        /// <summary>Produced by the seeded synthetic generator. Not an observation.</summary>
        SyntheticTraining
    }

    /// <summary>
    /// One message of an episode definition (immutable). Times are game seconds RELATIVE to the episode start and come from an
    /// explicit didactic compression of the source chronology (documented in the definition), not an operational reconstruction.
    /// </summary>
    public sealed class EpisodeMessage
    {
        public string Id { get; }
        public EpisodeMessageKind Kind { get; }
        public MessageBasis Basis { get; }
        public double OffsetSeconds { get; }
        public double ValidForSeconds { get; }
        /// <summary>Shelter deadline relative to the episode start (instructions/updates only).</summary>
        public double? ShelterByOffsetSeconds { get; }
        public string Supersedes { get; }
        public string TextKey { get; }
        /// <summary>DONKI ids this message paraphrases (empty for didactic/synthetic messages).</summary>
        public IReadOnlyList<string> SourceRecordIds { get; }
        /// <summary>Issue time of the quoted source message (demonstrable availability), or null when unknown / not applicable.</summary>
        public string SourceIssuedUtc { get; }

        public EpisodeMessage(string id, EpisodeMessageKind kind, MessageBasis basis, double offsetSeconds, double validForSeconds,
            double? shelterByOffsetSeconds, string supersedes, string textKey, IEnumerable<string> sourceRecordIds, string sourceIssuedUtc)
        {
            Id = StableKey.Require(id, nameof(id));
            Kind = OutpostGuard.Defined(kind, nameof(kind));
            Basis = OutpostGuard.Defined(basis, nameof(basis));
            OffsetSeconds = OutpostGuard.NonNegative(offsetSeconds, nameof(offsetSeconds));
            ValidForSeconds = OutpostGuard.Positive(validForSeconds, nameof(validForSeconds));
            bool instruction = kind == EpisodeMessageKind.ShelterInstruction || kind == EpisodeMessageKind.InstructionUpdate;
            if (instruction)
            {
                if (!shelterByOffsetSeconds.HasValue || shelterByOffsetSeconds.Value < offsetSeconds || !OutpostGuard.IsFinite(shelterByOffsetSeconds.Value))
                    throw new ArgumentException("An instruction needs a deadline at or after its issue.", nameof(shelterByOffsetSeconds));
            }
            else if (shelterByOffsetSeconds.HasValue) throw new ArgumentException("Only instructions carry a deadline.", nameof(shelterByOffsetSeconds));
            if ((kind == EpisodeMessageKind.InstructionUpdate || kind == EpisodeMessageKind.InstructionEnd) == (supersedes == null))
                throw new ArgumentException("Updates and ends replace an earlier message; watches and instructions do not.", nameof(supersedes));
            Supersedes = supersedes == null ? null : StableKey.Require(supersedes, nameof(supersedes));
            ShelterByOffsetSeconds = shelterByOffsetSeconds;
            TextKey = StableKey.Require(textKey, nameof(textKey));
            var ids = new List<string>();
            foreach (string sourceId in sourceRecordIds ?? Array.Empty<string>()) ids.Add(ProvenanceText.Id(sourceId, nameof(sourceRecordIds)));
            if (ids.Count > IncidentLimits.MaxSourceRecords) throw new ArgumentException("Too many source ids.", nameof(sourceRecordIds));
            if (basis == MessageBasis.SourceNotification && ids.Count == 0) throw new ArgumentException("A quoted message names its source.", nameof(sourceRecordIds));
            if (basis != MessageBasis.SourceNotification && ids.Count > 0 && basis == MessageBasis.SyntheticTraining)
                throw new ArgumentException("A synthetic message quotes no source.", nameof(sourceRecordIds));
            if (kind == EpisodeMessageKind.InstructionEnd && basis == MessageBasis.SourceNotification)
                throw new ArgumentException("The end of a shelter period is a training decision, never quoted as a NASA all-clear.", nameof(basis));
            SourceRecordIds = new ReadOnlyCollection<string>(ids);
            SourceIssuedUtc = ProvenanceText.OptionalUtc(sourceIssuedUtc, nameof(sourceIssuedUtc));
        }
    }

    /// <summary>
    /// A complete space-weather episode the director can play (immutable): historical (frozen DONKI records + didactic timeline)
    /// or synthetic (seeded generator). The didactic onset/end are the scenario's HIDDEN truth: the authority never publishes them
    /// and never uses them to inform clients; only issued messages are visible, when issued.
    /// </summary>
    public sealed class SpaceWeatherEpisodeDefinition
    {
        public string Key { get; }
        public DataMode Mode { get; }
        public string Label { get; }
        public string Provenance { get; }
        /// <summary>How source hours were compressed into game seconds (honest, visible note).</summary>
        public string TimelineNote { get; }
        public DatasetManifest Dataset { get; }
        public IReadOnlyList<SourceRecord> Records { get; }
        public IReadOnlyList<EpisodeMessage> Messages { get; }
        public double DidacticOnsetSeconds { get; }
        public double DidacticEndSeconds { get; }

        public SpaceWeatherEpisodeDefinition(string key, DataMode mode, string label, string provenance, string timelineNote,
            DatasetManifest dataset, IEnumerable<SourceRecord> records, IEnumerable<EpisodeMessage> messages,
            double didacticOnsetSeconds, double didacticEndSeconds)
        {
            Key = StableKey.Require(key, nameof(key));
            Mode = OutpostGuard.Defined(mode, nameof(mode));
            if (mode == DataMode.Current) throw new ArgumentException("Current observation is not implemented in phase 5.", nameof(mode));
            Label = ProvenanceText.Required(label, nameof(label));
            Provenance = ProvenanceText.Required(provenance, nameof(provenance));
            TimelineNote = ProvenanceText.Required(timelineNote, nameof(timelineNote));
            if (mode == DataMode.Historical && (dataset == null || dataset.Mode != DataMode.Historical))
                throw new ArgumentException("A historical episode needs its historical dataset manifest.", nameof(dataset));
            Dataset = dataset;
            var recordList = new List<SourceRecord>(records ?? Array.Empty<SourceRecord>());
            if (recordList.Count > IncidentLimits.MaxSourceRecords || recordList.Contains(null)) throw new ArgumentException("Too many or null records.", nameof(records));
            var recordIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (SourceRecord r in recordList)
                if (!recordIds.Add(r.SourceId)) throw new ArgumentException($"Duplicate source record '{r.SourceId}'.", nameof(records));
            if (mode == DataMode.Synthetic && recordList.Count > 0) throw new ArgumentException("A synthetic episode carries no source records.", nameof(records));
            Records = new ReadOnlyCollection<SourceRecord>(recordList);

            var list = new List<EpisodeMessage>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            double last = double.NegativeInfinity;
            bool instructed = false, ended = false;
            foreach (EpisodeMessage m in messages ?? throw new ArgumentNullException(nameof(messages)))
            {
                if (m == null) throw new ArgumentException("Null message.", nameof(messages));
                if (!ids.Add(m.Id)) throw new ArgumentException($"Duplicate message '{m.Id}'.", nameof(messages));
                if (m.OffsetSeconds < last) throw new ArgumentException("Messages must be in issue order.", nameof(messages));
                if (ended) throw new ArgumentException("Nothing follows the end of the shelter period.", nameof(messages));
                if (m.Kind == EpisodeMessageKind.Watch && instructed) throw new ArgumentException("A watch precedes the instruction.", nameof(messages));
                if (m.Kind == EpisodeMessageKind.ShelterInstruction)
                {
                    if (instructed) throw new ArgumentException("One instruction chain per episode.", nameof(messages));
                    instructed = true;
                }
                if ((m.Kind == EpisodeMessageKind.InstructionUpdate || m.Kind == EpisodeMessageKind.InstructionEnd)
                    && (!instructed || !list.Exists(p => p.Id == m.Supersedes && p.Kind != EpisodeMessageKind.Watch)))
                    throw new ArgumentException($"'{m.Id}' replaces an unknown, later or watch message.", nameof(messages));
                if (m.Kind == EpisodeMessageKind.InstructionEnd) ended = true;
                foreach (string sourceId in m.SourceRecordIds)
                    if (!recordIds.Contains(sourceId)) throw new ArgumentException($"'{m.Id}' quotes an unknown record '{sourceId}'.", nameof(messages));
                if (mode == DataMode.Synthetic && m.Basis == MessageBasis.SourceNotification)
                    throw new ArgumentException("A synthetic episode quotes no NASA message.", nameof(messages));
                last = m.OffsetSeconds;
                list.Add(m);
            }
            if (!instructed || !ended) throw new ArgumentException("An episode has one instruction and one end of shelter period.", nameof(messages));
            if (list.Count > IncidentLimits.MaxEpisodeMessages) throw new ArgumentException("Too many messages.", nameof(messages));
            Messages = new ReadOnlyCollection<EpisodeMessage>(list);
            DidacticOnsetSeconds = OutpostGuard.NonNegative(didacticOnsetSeconds, nameof(didacticOnsetSeconds));
            DidacticEndSeconds = OutpostGuard.Finite(didacticEndSeconds, nameof(didacticEndSeconds));
            if (!(didacticEndSeconds > didacticOnsetSeconds)) throw new ArgumentException("The didactic event ends after its onset.", nameof(didacticEndSeconds));
        }
    }
}
