using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Application.Outpost;

namespace Trainer.Application.Expedition
{
    public enum AdvisoryKind
    {
        /// <summary>Mission control asks the crew to be protected in the shelter by <see cref="AdvisoryMessage.ShelterBySeconds"/>.</summary>
        ShelterInstruction,
        /// <summary>Replaces a previous instruction (new validity or deadline); the instruction stays in force.</summary>
        InstructionUpdate,
        /// <summary>Mission control explicitly ends THIS scenario's shelter period. It is a message, not a measurement:
        /// the end of a flare or of a reading never ends an instruction by itself, and this is not a universal all-clear.</summary>
        InstructionEnd
    }

    /// <summary>
    /// One message of mission control (configuration of an episode; published only once issued). Times are
    /// simulated seconds of the epoch. <see cref="ValidUntilSeconds"/> is how long its information is current: after
    /// it, the message is Expired, which means NO current information, never "safe". <see cref="TextKey"/> selects
    /// the player text in the presentation.
    /// </summary>
    public sealed class AdvisoryMessage
    {
        public string Id { get; }
        public AdvisoryKind Kind { get; }
        public double IssuedAtSeconds { get; }
        /// <summary>Instruction and update: deadline to be protected. End: null.</summary>
        public double? ShelterBySeconds { get; }
        public double ValidUntilSeconds { get; }
        /// <summary>Id of the message this one replaces; null for a first instruction.</summary>
        public string Supersedes { get; }
        public string TextKey { get; }

        public AdvisoryMessage(string id, AdvisoryKind kind, double issuedAtSeconds, double? shelterBySeconds,
            double validUntilSeconds, string supersedes, string textKey)
        {
            Id = StableKey.Require(id, nameof(id));
            Kind = OutpostGuard.Defined(kind, nameof(kind));
            IssuedAtSeconds = OutpostGuard.NonNegative(issuedAtSeconds, nameof(issuedAtSeconds));
            ValidUntilSeconds = OutpostGuard.Finite(validUntilSeconds, nameof(validUntilSeconds));
            if (!(validUntilSeconds > issuedAtSeconds)) throw new ArgumentException("A message must be valid after it is issued.", nameof(validUntilSeconds));
            if (kind == AdvisoryKind.InstructionEnd)
            {
                if (shelterBySeconds.HasValue) throw new ArgumentException("An end message has no shelter deadline.", nameof(shelterBySeconds));
            }
            else
            {
                if (!shelterBySeconds.HasValue) throw new ArgumentException("An instruction needs a shelter deadline.", nameof(shelterBySeconds));
                OutpostGuard.Finite(shelterBySeconds.Value, nameof(shelterBySeconds));
                if (shelterBySeconds.Value < issuedAtSeconds) throw new ArgumentException("The deadline cannot precede the message.", nameof(shelterBySeconds));
            }
            if (kind != AdvisoryKind.ShelterInstruction && supersedes == null)
                throw new ArgumentException("Updates and ends replace an earlier message.", nameof(supersedes));
            Supersedes = supersedes == null ? null : StableKey.Require(supersedes, nameof(supersedes));
            if (Supersedes == Id) throw new ArgumentException("A message cannot replace itself.", nameof(supersedes));
            ShelterBySeconds = shelterBySeconds;
            TextKey = StableKey.Require(textKey, nameof(textKey));
        }
    }

    /// <summary>
    /// Source and context of a HISTORICAL episode (configuration, immutable). Required when an episode is not
    /// synthetic. Measured quantities keep their original unit, observation location and integration; they are context
    /// for the replay, never converted into a lunar human dose (no X-ray class, pfu or orbital detector reading is a
    /// dose).
    /// </summary>
    public sealed class HistoricalSource
    {
        public string Source { get; }
        public string Reference { get; }
        public string ObservedStartUtc { get; }
        public string ObservedEndUtc { get; }
        public string Quantity { get; }
        public string Unit { get; }
        public string ObservationLocation { get; }
        public string Context { get; }

        public HistoricalSource(string source, string reference, string observedStartUtc, string observedEndUtc,
            string quantity, string unit, string observationLocation, string context)
        {
            Source = Required(source, nameof(source));
            Reference = Required(reference, nameof(reference));
            ObservedStartUtc = Required(observedStartUtc, nameof(observedStartUtc));
            ObservedEndUtc = Required(observedEndUtc, nameof(observedEndUtc));
            Quantity = Required(quantity, nameof(quantity));
            Unit = Required(unit, nameof(unit));
            ObservationLocation = Required(observationLocation, nameof(observationLocation));
            Context = Required(context, nameof(context));
        }

        private static string Required(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Required.", name);
            return value;
        }
    }

    /// <summary>
    /// An energetic-particle episode of the scenario (configuration, immutable). Three things are kept apart:
    /// the EVENT (<see cref="EventOnsetSeconds"/>/<see cref="EventEndSeconds"/>: scenario truth, never published
    /// before the session ends and never used to judge players), the INFORMATION (<see cref="Messages"/>, published
    /// only once issued) and the crew's DECISIONS (their actions). The default episode is authored and explicitly
    /// synthetic, reproducible and offline; a historical one needs its <see cref="Source"/>.
    /// </summary>
    public sealed class ParticleEventEpisode
    {
        public string Id { get; }
        public bool Synthetic { get; }
        public string Provenance { get; }
        public HistoricalSource Source { get; }
        public double EventOnsetSeconds { get; }
        public double EventEndSeconds { get; }
        /// <summary>In issue order; ids unique; each replaced id refers to an EARLIER message.</summary>
        public IReadOnlyList<AdvisoryMessage> Messages { get; }

        public ParticleEventEpisode(string id, bool synthetic, string provenance, HistoricalSource source,
            double eventOnsetSeconds, double eventEndSeconds, IEnumerable<AdvisoryMessage> messages)
        {
            Id = StableKey.Require(id, nameof(id));
            if (string.IsNullOrWhiteSpace(provenance)) throw new ArgumentException("Provenance label required.", nameof(provenance));
            if (!synthetic && source == null) throw new ArgumentException("A historical episode needs its source and context.", nameof(source));
            Synthetic = synthetic;
            Provenance = provenance;
            Source = source;
            EventOnsetSeconds = OutpostGuard.NonNegative(eventOnsetSeconds, nameof(eventOnsetSeconds));
            EventEndSeconds = OutpostGuard.Finite(eventEndSeconds, nameof(eventEndSeconds));
            if (!(eventEndSeconds > eventOnsetSeconds)) throw new ArgumentException("The event must end after its onset.", nameof(eventEndSeconds));
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            var copy = new List<AdvisoryMessage>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            double last = double.NegativeInfinity;
            foreach (AdvisoryMessage message in messages)
            {
                if (message == null) throw new ArgumentException("Null message.", nameof(messages));
                if (!ids.Add(message.Id)) throw new ArgumentException($"Duplicate message '{message.Id}'.", nameof(messages));
                if (message.IssuedAtSeconds < last) throw new ArgumentException("Messages must be in issue order.", nameof(messages));
                if (message.Supersedes != null && !copy.Exists(m => m.Id == message.Supersedes))
                    throw new ArgumentException($"'{message.Id}' replaces an unknown or later message.", nameof(messages));
                last = message.IssuedAtSeconds;
                copy.Add(message);
            }
            if (copy.Count == 0 || copy.Count > ExpeditionLimits.MaxAdvisories)
                throw new ArgumentException("Between 1 and MaxAdvisories messages.", nameof(messages));
            if (copy[0].Kind != AdvisoryKind.ShelterInstruction)
                throw new ArgumentException("An episode starts with a shelter instruction.", nameof(messages));
            Messages = new ReadOnlyCollection<AdvisoryMessage>(copy.ToArray());
        }
    }

    public enum AdvisoryValidity
    {
        /// <summary>Issued, not replaced and before its ValidUntil.</summary>
        Current,
        /// <summary>A later message replaced it.</summary>
        Superseded,
        /// <summary>Its ValidUntil passed without a replacement: NO current information (never "safe").</summary>
        Expired
    }

    /// <summary>A message that has been ISSUED (immutable), with its validity at the snapshot time. Snapshots never
    /// contain messages that are not issued yet.</summary>
    public sealed class IssuedAdvisory
    {
        public AdvisoryMessage Message { get; }
        public AdvisoryValidity Validity { get; }

        public IssuedAdvisory(AdvisoryMessage message, AdvisoryValidity validity)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Validity = OutpostGuard.Defined(validity, nameof(validity));
        }
    }

    /// <summary>When a member received an issued message (immutable). Kept after the member leaves (history).</summary>
    public sealed class AdvisoryReception
    {
        public string MessageId { get; }
        public PlayerId Player { get; }
        public double ReceivedAtSeconds { get; }
        /// <summary>Received after the message's ShelterBy (late reception: for example out of radio range).</summary>
        public bool AfterShelterBy { get; }

        public AdvisoryReception(string messageId, PlayerId player, double receivedAtSeconds, bool afterShelterBy)
        {
            MessageId = StableKey.Require(messageId, nameof(messageId));
            if (!player.IsValid) throw new ArgumentException("Player required.", nameof(player));
            Player = player;
            ReceivedAtSeconds = OutpostGuard.NonNegative(receivedAtSeconds, nameof(receivedAtSeconds));
            AfterShelterBy = afterShelterBy;
        }
    }

    public enum InstructionState
    {
        /// <summary>No shelter instruction was issued in this epoch.</summary>
        None,
        /// <summary>An instruction is in force and its latest message is current.</summary>
        InForce,
        /// <summary>An instruction is in force but its latest message expired with no replacement: there is no
        /// current information, so the instruction is NOT lifted (absence of news is not an all-clear).</summary>
        InForceWithoutCurrentInformation,
        /// <summary>An explicit end message was issued.</summary>
        Ended
    }

    /// <summary>The shelter instruction as the base knows it (immutable; only issued information).</summary>
    public sealed class ShelterInstructionStatus
    {
        public static readonly ShelterInstructionStatus None = new ShelterInstructionStatus(InstructionState.None, null, null, null, null);

        public InstructionState State { get; }
        /// <summary>Latest instruction/update in force (or the one ended); null for None.</summary>
        public string LatestMessageId { get; }
        public double? ShelterBySeconds { get; }
        public double? ValidUntilSeconds { get; }
        public double? EndedAtSeconds { get; }
        public bool InForce => State == InstructionState.InForce || State == InstructionState.InForceWithoutCurrentInformation;

        public ShelterInstructionStatus(InstructionState state, string latestMessageId, double? shelterBySeconds,
            double? validUntilSeconds, double? endedAtSeconds)
        {
            State = OutpostGuard.Defined(state, nameof(state));
            bool none = state == InstructionState.None;
            if (none != (latestMessageId == null)) throw new ArgumentException("Only None has no message.", nameof(latestMessageId));
            LatestMessageId = latestMessageId == null ? null : StableKey.Require(latestMessageId, nameof(latestMessageId));
            if (none != !shelterBySeconds.HasValue || none != !validUntilSeconds.HasValue)
                throw new ArgumentException("An instruction has a deadline and a validity.");
            if ((state == InstructionState.Ended) != endedAtSeconds.HasValue)
                throw new ArgumentException("Only an ended instruction has an end time.", nameof(endedAtSeconds));
            ShelterBySeconds = shelterBySeconds;
            ValidUntilSeconds = validUntilSeconds;
            EndedAtSeconds = endedAtSeconds;
        }
    }

    /// <summary>The episode's event truth, revealed ONLY in snapshots of an ended session (debrief): it shows how
    /// information lagged the event. It is never an input of any evaluation.</summary>
    public sealed class EventDisclosure
    {
        public string EpisodeId { get; }
        public bool Synthetic { get; }
        public string Provenance { get; }
        public double EventOnsetSeconds { get; }
        public double EventEndSeconds { get; }

        public EventDisclosure(string episodeId, bool synthetic, string provenance, double eventOnsetSeconds, double eventEndSeconds)
        {
            EpisodeId = StableKey.Require(episodeId, nameof(episodeId));
            if (string.IsNullOrWhiteSpace(provenance)) throw new ArgumentException("Provenance required.", nameof(provenance));
            Synthetic = synthetic;
            Provenance = provenance;
            EventOnsetSeconds = OutpostGuard.NonNegative(eventOnsetSeconds, nameof(eventOnsetSeconds));
            EventEndSeconds = OutpostGuard.Finite(eventEndSeconds, nameof(eventEndSeconds));
            if (!(eventEndSeconds > eventOnsetSeconds)) throw new ArgumentException("End after onset.", nameof(eventEndSeconds));
        }

        public static EventDisclosure Of(ParticleEventEpisode episode) =>
            new EventDisclosure(episode.Id, episode.Synthetic, episode.Provenance, episode.EventOnsetSeconds, episode.EventEndSeconds);
    }
}
