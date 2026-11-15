using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Application.Outpost;
using Trainer.Simulation.Eva;

namespace Trainer.Application.Expedition
{
    /// <summary>Phase 3 state of one present crew member (immutable). Positions and poses stay in the phase 2
    /// <see cref="CrewMemberState"/> of the same snapshot.</summary>
    public sealed class ExpeditionCrewState
    {
        public PlayerId Player { get; }
        public CrewZone Zone { get; }
        /// <summary>Increases each time the authority confirms a zone change of this member (including Restart's
        /// return to the spawn point); never reused for the PlayerId. Commands state it to detect staleness.</summary>
        public long ZoneSequence { get; }
        /// <summary>Index of the worn suit, or -1.</summary>
        public int SuitIndex { get; }
        /// <summary>On suit supply (its suit is Sealed): this member's demand is charged to the suit, not the base.</summary>
        public bool OnSuitSupply { get; }
        public SuitActivity Activity { get; }
        public ReturnMarginEstimate Margin { get; }
        public ProtectionState Protection { get; }

        public ExpeditionCrewState(PlayerId player, CrewZone zone, long zoneSequence, int suitIndex, bool onSuitSupply,
            SuitActivity activity, ReturnMarginEstimate margin, ProtectionState protection)
        {
            if (!player.IsValid) throw new ArgumentException("Player required.", nameof(player));
            Player = player;
            Zone = OutpostGuard.Defined(zone, nameof(zone));
            if (zoneSequence < 0) throw new ArgumentOutOfRangeException(nameof(zoneSequence));
            ZoneSequence = zoneSequence;
            if (suitIndex < -1 || suitIndex >= ExpeditionLimits.MaxSuits) throw new ArgumentOutOfRangeException(nameof(suitIndex));
            SuitIndex = suitIndex;
            if (onSuitSupply && suitIndex < 0) throw new ArgumentException("Suit supply needs a suit.", nameof(onSuitSupply));
            if (zone == CrewZone.Exterior && !onSuitSupply) throw new ArgumentException("Outside only on suit supply.", nameof(zone));
            if (CrewZones.IsPressurizedBase(zone) && onSuitSupply)
                throw new ArgumentException("Inside the pressurized base nobody is on suit supply.", nameof(zone));
            OnSuitSupply = onSuitSupply;
            Activity = OutpostGuard.Defined(activity, nameof(activity));
            Margin = margin ?? throw new ArgumentNullException(nameof(margin));
            if (onSuitSupply == (margin.Status == MarginStatus.NotApplicable))
                throw new ArgumentException("A margin applies exactly while on suit supply.", nameof(margin));
            Protection = OutpostGuard.Defined(protection, nameof(protection));
            if (protection == ProtectionState.Protected && zone != CrewZone.Shelter)
                throw new ArgumentException("Protected only inside the shelter.", nameof(protection));
        }
    }

    /// <summary>
    /// Complete authoritative state of a phase 3 session (immutable; private read-only copies). It CONTAINS the
    /// phase 2 <see cref="OutpostSessionSnapshot"/> with the same epoch and revision (crew poses, stations and tasks,
    /// base resources, compatibility objectives), so phase 2 views keep working. Always a FULL state, never a delta.
    /// Order: (Epoch, Revision) of <see cref="Outpost"/>.
    ///
    /// The constructor refuses inconsistent states: crew lists that differ, suits worn by absent or other members,
    /// suit supply outside its rules, airlock occupants that are not in the chamber, an unpressurized airlock with
    /// someone not on suit supply, advisories issued in the future, receptions of unknown or future messages, an
    /// event disclosure before the session ended, or an outcome that does not match the phase 2 state.
    /// </summary>
    public sealed class ExpeditionSessionSnapshot
    {
        /// <summary>Tolerance when comparing issue and reception times with the clock.</summary>
        public const double TimeToleranceSeconds = 1e-6;

        public OutpostSessionSnapshot Outpost { get; }
        public IReadOnlyList<ExpeditionCrewState> Crew { get; }
        public IReadOnlyList<SuitState> Suits { get; }
        public AirlockStatus Airlock { get; }
        public ShelterStatus Shelter { get; }
        public EvaSupplyReading Supply { get; }
        public IReadOnlyList<IssuedAdvisory> Advisories { get; }
        public IReadOnlyList<AdvisoryReception> Receptions { get; }
        public ShelterInstructionStatus Instruction { get; }
        public ExpeditionPhase Phase { get; }
        public ReturnDecision Decision { get; }
        public ExpeditionOutcome Outcome { get; }
        public IReadOnlyList<ExpeditionObjective> Objectives { get; }
        public IReadOnlyList<ProtectionAssessment> Assessments { get; }
        public IReadOnlyList<ExpeditionIncident> Incidents { get; }
        public EventDisclosure Disclosure { get; }

        public long Epoch => Outpost.Epoch;
        public long Revision => Outpost.Revision;
        public double SimulatedTimeSeconds => Outpost.SimulatedTimeSeconds;

        public ExpeditionSessionSnapshot(OutpostSessionSnapshot outpost, IEnumerable<ExpeditionCrewState> crew,
            IEnumerable<SuitState> suits, AirlockStatus airlock, ShelterStatus shelter, EvaSupplyReading supply,
            IEnumerable<IssuedAdvisory> advisories, IEnumerable<AdvisoryReception> receptions,
            ShelterInstructionStatus instruction, ExpeditionPhase phase, ReturnDecision decision, ExpeditionOutcome outcome,
            IEnumerable<ExpeditionObjective> objectives, IEnumerable<ProtectionAssessment> assessments,
            IEnumerable<ExpeditionIncident> incidents, EventDisclosure disclosure)
        {
            Outpost = outpost ?? throw new ArgumentNullException(nameof(outpost));
            Airlock = airlock ?? throw new ArgumentNullException(nameof(airlock));
            Shelter = shelter ?? throw new ArgumentNullException(nameof(shelter));
            Supply = supply ?? throw new ArgumentNullException(nameof(supply));
            Instruction = instruction ?? throw new ArgumentNullException(nameof(instruction));
            Phase = OutpostGuard.Defined(phase, nameof(phase));
            Outcome = OutpostGuard.Defined(outcome, nameof(outcome));
            Decision = decision;
            Disclosure = disclosure;
            double now = outpost.SimulatedTimeSeconds;
            bool ended = outpost.State == SessionState.Completed || outpost.State == SessionState.Interrupted;

            // Outcome and phase agree with the phase 2 fields (single mapping, no second evaluation).
            if (ExpeditionOutcomes.ToOutpostOutcome(outcome) != outpost.Outcome)
                throw new ArgumentException("The phase 3 outcome does not match the phase 2 outcome.", nameof(outcome));
            if ((phase == ExpeditionPhase.Closed) != ended) throw new ArgumentException("Closed exactly when the session ended.", nameof(phase));
            if ((phase == ExpeditionPhase.Returning) && decision == null) throw new ArgumentException("Returning needs the decision.", nameof(decision));
            if (decision != null && phase == ExpeditionPhase.Preparing) throw new ArgumentException("No decision before the EVA.", nameof(decision));
            if (disclosure != null && !ended) throw new ArgumentException("The event is disclosed only after the session ends.", nameof(disclosure));

            // Crew: one phase 3 state per present member, in the same order.
            Crew = CopyList(crew, nameof(crew), OutpostLimits.MaxCrew);
            if (Crew.Count != outpost.Crew.Count) throw new ArgumentException("One phase 3 state per present member.", nameof(crew));
            for (int i = 0; i < Crew.Count; i++)
                if (Crew[i].Player != outpost.Crew[i].Player) throw new ArgumentException("Crew states must follow the phase 2 crew order.", nameof(crew));

            Suits = CopyList(suits, nameof(suits), ExpeditionLimits.MaxSuits);
            if (Suits.Count == 0) throw new ArgumentException("At least one suit.", nameof(suits));
            for (int i = 0; i < Suits.Count; i++)
                if (Suits[i].Index != i) throw new ArgumentException("Suits in index order.", nameof(suits));
            foreach (SuitState suit in Suits)
                if (suit.Wearer.IsValid)
                {
                    ExpeditionCrewState wearer = Find(suit.Wearer);
                    if (wearer == null || wearer.SuitIndex != suit.Index)
                        throw new ArgumentException($"Suit '{suit.Key}' is worn by an absent or other member.", nameof(suits));
                    if (wearer.OnSuitSupply != (suit.Status == SuitStatus.Sealed))
                        throw new ArgumentException("Suit supply matches a sealed suit.", nameof(suits));
                }
            foreach (ExpeditionCrewState member in Crew)
                if (member.SuitIndex >= 0 && (member.SuitIndex >= Suits.Count || Suits[member.SuitIndex].Wearer != member.Player))
                    throw new ArgumentException($"{member.Player} wears a suit that does not name them.", nameof(crew));

            // Airlock occupants are exactly the members in the chamber; outside base pressure they are sealed.
            var inChamber = new List<PlayerId>();
            foreach (ExpeditionCrewState member in Crew)
                if (member.Zone == CrewZone.Airlock)
                {
                    inChamber.Add(member.Player);
                    if (airlock.State != AirlockState.PressurizedInnerOpen && !member.OnSuitSupply)
                        throw new ArgumentException("Without base pressure every occupant is on suit supply.", nameof(airlock));
                }
            if (inChamber.Count != airlock.Occupants.Count) throw new ArgumentException("Airlock occupants must be the chamber's members.", nameof(airlock));
            foreach (PlayerId occupant in inChamber)
                if (!Contains(airlock.Occupants, occupant)) throw new ArgumentException("Airlock occupants must be the chamber's members.", nameof(airlock));
            // The cycle requester may have left since: the cycle continues for the occupants.

            // Information: only issued messages; receptions of issued messages at or after their issue.
            Advisories = CopyList(advisories, nameof(advisories), ExpeditionLimits.MaxAdvisories);
            var issuedAt = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (IssuedAdvisory advisory in Advisories)
            {
                if (advisory.Message.IssuedAtSeconds > now + TimeToleranceSeconds)
                    throw new ArgumentException($"'{advisory.Message.Id}' is not issued yet.", nameof(advisories));
                if (issuedAt.ContainsKey(advisory.Message.Id)) throw new ArgumentException("Duplicate advisory.", nameof(advisories));
                issuedAt.Add(advisory.Message.Id, advisory.Message.IssuedAtSeconds);
            }
            Receptions = CopyList(receptions, nameof(receptions), ExpeditionLimits.MaxReceptions);
            var received = new HashSet<(string, PlayerId)>();
            foreach (AdvisoryReception reception in Receptions)
            {
                if (!issuedAt.TryGetValue(reception.MessageId, out double at))
                    throw new ArgumentException($"Reception of unknown message '{reception.MessageId}'.", nameof(receptions));
                if (reception.ReceivedAtSeconds < at - TimeToleranceSeconds || reception.ReceivedAtSeconds > now + TimeToleranceSeconds)
                    throw new ArgumentException("A reception happens between the issue and now.", nameof(receptions));
                if (!received.Add((reception.MessageId, reception.Player)))
                    throw new ArgumentException("A member receives a message once.", nameof(receptions));
            }
            if (instruction.LatestMessageId != null && !issuedAt.ContainsKey(instruction.LatestMessageId))
                throw new ArgumentException("The instruction refers to an unissued message.", nameof(instruction));

            Objectives = CopyList(objectives, nameof(objectives), OutpostLimits.MaxObjectives);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (ExpeditionObjective objective in Objectives)
                if (!keys.Add(objective.Key)) throw new ArgumentException($"Duplicate objective '{objective.Key}'.", nameof(objectives));
            Assessments = CopyList(assessments, nameof(assessments), ExpeditionLimits.MaxAssessments);
            var assessed = new HashSet<PlayerId>();
            foreach (ProtectionAssessment assessment in Assessments)
                if (!assessed.Add(assessment.Player)) throw new ArgumentException("One assessment per member.", nameof(assessments));
            Incidents = CopyList(incidents, nameof(incidents), ExpeditionLimits.MaxIncidents);
        }

        /// <summary>True when this snapshot must replace <paramref name="other"/> (same rule as phase 2).</summary>
        public bool IsNewerThan(ExpeditionSessionSnapshot other) => other == null || Outpost.IsNewerThan(other.Outpost);

        public bool TryGetCrew(PlayerId player, out ExpeditionCrewState member)
        {
            member = Find(player);
            return member != null;
        }

        public bool TryGetSuitOf(PlayerId player, out SuitState suit)
        {
            ExpeditionCrewState member = Find(player);
            suit = member != null && member.SuitIndex >= 0 ? Suits[member.SuitIndex] : null;
            return suit != null;
        }

        /// <summary>The worst (smallest) valid return margin among members on suit supply: the team's margin, never
        /// an average. False when nobody on suit supply has a valid margin.</summary>
        public bool TryGetTeamWorstMargin(out PlayerId member, out ReturnMarginEstimate margin)
        {
            member = PlayerId.None;
            margin = null;
            foreach (ExpeditionCrewState state in Crew)
            {
                if (!state.OnSuitSupply) continue;
                MarginStatus status = state.Margin.Status;
                if (status != MarginStatus.Sufficient && status != MarginStatus.Insufficient) continue;
                if (margin == null || state.Margin.MarginSeconds < margin.MarginSeconds)
                {
                    member = state.Player;
                    margin = state.Margin;
                }
            }
            return margin != null;
        }

        /// <summary>When <paramref name="player"/> received <paramref name="messageId"/>, if they did.</summary>
        public bool TryGetReception(string messageId, PlayerId player, out AdvisoryReception reception)
        {
            foreach (AdvisoryReception candidate in Receptions)
                if (candidate.Player == player && string.Equals(candidate.MessageId, messageId, StringComparison.Ordinal))
                {
                    reception = candidate;
                    return true;
                }
            reception = null;
            return false;
        }

        private ExpeditionCrewState Find(PlayerId player)
        {
            foreach (ExpeditionCrewState member in Crew)
                if (member.Player == player) return member;
            return null;
        }

        private static bool Contains(IReadOnlyList<PlayerId> list, PlayerId player)
        {
            foreach (PlayerId candidate in list)
                if (candidate == player) return true;
            return false;
        }

        private static IReadOnlyList<T> CopyList<T>(IEnumerable<T> items, string name, int max) where T : class
        {
            if (items == null) throw new ArgumentNullException(name);
            var copy = new List<T>();
            foreach (T item in items)
            {
                if (item == null) throw new ArgumentException("Null entry.", name);
                copy.Add(item);
            }
            if (copy.Count > max) throw new ArgumentException("Too many entries.", name);
            return new ReadOnlyCollection<T>(copy.ToArray());
        }
    }
}
