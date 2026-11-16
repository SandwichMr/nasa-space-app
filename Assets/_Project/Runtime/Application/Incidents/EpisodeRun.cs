using System;
using System.Collections.Generic;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;

namespace Trainer.Application.Incidents
{
    /// <summary>
    /// One space-weather episode being played (D2), started at a session time. It issues the definition's messages at their
    /// offsets, records who received each one (radio coverage of phase 3 at the confirmed position), observes each member's
    /// qualitative protection and, when the shelter period ends, assesses each member with the phase 3 categories.
    /// Only issued messages, the records they quote and observed facts are ever published; the didactic onset/end are not.
    /// No dose, no shielding percentage, no clinical consequence.
    /// </summary>
    internal sealed class EpisodeRun
    {
        private sealed class MemberTrack
        {
            public PlayerId Player;
            public double? InstructionReceivedAt;
            public bool InstructionReceivedLate;
            public double? FirstProtectedAt;
            public bool LeftAfterProtected;
            public bool WasProtected;
            public MemberShelterState State = MemberShelterState.NotRequired;
        }

        private readonly List<EpisodeMessage> issued = new List<EpisodeMessage>();
        private readonly Dictionary<string, double> issuedAt = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly List<AdvisoryReception> receptions = new List<AdvisoryReception>();
        private readonly HashSet<string> received = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<MemberTrack> members = new List<MemberTrack>();
        private readonly List<ProtectionAssessment> assessments = new List<ProtectionAssessment>();

        public SpaceWeatherEpisodeDefinition Definition { get; }
        public double StartedAt { get; }
        public bool Ended { get; private set; }
        public bool InstructionIssued { get; private set; }
        public bool AnyoneProtected { get; private set; }

        public EpisodeRun(SpaceWeatherEpisodeDefinition definition, double startedAt)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            StartedAt = startedAt;
        }

        /// <summary>Session time of the next message to issue (infinity when all are issued).</summary>
        public double NextIssueAt
        {
            get
            {
                if (issued.Count >= Definition.Messages.Count) return double.PositiveInfinity;
                return StartedAt + Definition.Messages[issued.Count].OffsetSeconds;
            }
        }

        /// <summary>Session time of the instruction deadline in force (for chunking), or infinity.</summary>
        public double? ShelterBy
        {
            get
            {
                EpisodeMessage latest = LatestInstruction();
                return latest?.ShelterByOffsetSeconds == null ? (double?)null : StartedAt + latest.ShelterByOffsetSeconds.Value;
            }
        }

        public bool InstructionInForce => InstructionIssued && !Ended;

        /// <summary>Issues every message due at or before <paramref name="now"/>. Returns the kinds issued, in order.</summary>
        public List<EpisodeMessageKind> IssueDue(double now)
        {
            var kinds = new List<EpisodeMessageKind>();
            while (issued.Count < Definition.Messages.Count && StartedAt + Definition.Messages[issued.Count].OffsetSeconds <= now + 1e-9)
            {
                EpisodeMessage m = Definition.Messages[issued.Count];
                issued.Add(m);
                issuedAt[m.Id] = StartedAt + m.OffsetSeconds;
                kinds.Add(m.Kind);
                if (m.Kind == EpisodeMessageKind.ShelterInstruction) InstructionIssued = true;
                if (m.Kind == EpisodeMessageKind.InstructionEnd) Ended = true;
            }
            return kinds;
        }

        /// <summary>One observation of the crew at <paramref name="now"/>: receptions (radio coverage) and protection.</summary>
        public void Observe(double now, ExpeditionSessionSnapshot expedition, CommsConfiguration comms)
        {
            var positions = new Dictionary<PlayerId, BasePosition>();
            foreach (CrewMemberState c in expedition.Outpost.Crew) positions[c.Player] = c.Pose.Position;
            ShelterStatus shelter = expedition.Shelter;
            foreach (ExpeditionCrewState crew in expedition.Crew)
            {
                MemberTrack track = Track(crew.Player);
                if (!positions.TryGetValue(crew.Player, out BasePosition position)) continue;
                bool covered = comms.Covers(crew.Zone, position);
                if (covered)
                    foreach (EpisodeMessage m in issued)
                    {
                        string key = m.Id + "|" + crew.Player.Value;
                        if (received.Contains(key) || receptions.Count >= IncidentLimits.MaxReceptions) continue;
                        double? deadline = DeadlineAt(m);
                        bool late = deadline.HasValue && now > deadline.Value + 1e-9;
                        received.Add(key);
                        receptions.Add(new AdvisoryReception(m.Id, crew.Player, now, late));
                        if ((m.Kind == EpisodeMessageKind.ShelterInstruction || m.Kind == EpisodeMessageKind.InstructionUpdate)
                            && !track.InstructionReceivedAt.HasValue)
                        {
                            track.InstructionReceivedAt = now;
                            track.InstructionReceivedLate = late;
                        }
                    }

                if (!InstructionInForce)
                {
                    track.State = MemberShelterState.NotRequired;
                    continue;
                }
                bool isProtected = crew.Zone == CrewZone.Shelter && shelter.Prepared && shelter.ServiceAvailable && !crew.OnSuitSupply;
                track.State = isProtected ? MemberShelterState.Protected
                    : crew.OnSuitSupply ? MemberShelterState.OnSuitSupply
                    : crew.Zone != CrewZone.Shelter ? MemberShelterState.NotInShelter
                    : !shelter.Prepared ? MemberShelterState.ShelterNotPrepared
                    : MemberShelterState.ShelterServiceUnavailable;
                if (isProtected) AnyoneProtected = true;
                if (track.InstructionReceivedAt.HasValue)
                {
                    if (isProtected && !track.FirstProtectedAt.HasValue) track.FirstProtectedAt = now;
                    if (!isProtected && track.WasProtected) track.LeftAfterProtected = true;
                    track.WasProtected = isProtected;
                }
            }
        }

        /// <summary>Assessments when the shelter period ends (uses only issued messages and each member's reception).</summary>
        public void Assess(IEnumerable<PlayerId> present)
        {
            if (assessments.Count > 0) return;
            double? deadline = ShelterBy;
            foreach (PlayerId player in present)
            {
                MemberTrack t = Track(player);
                ProtectionAssessment a;
                if (!t.InstructionReceivedAt.HasValue)
                    a = new ProtectionAssessment(player, ProtectionCategory.NotReceived, null, null, false);
                else if (!t.FirstProtectedAt.HasValue)
                    a = new ProtectionAssessment(player, ProtectionCategory.NotProtected, t.InstructionReceivedAt, null, t.InstructionReceivedLate);
                else
                {
                    ProtectionCategory c = t.LeftAfterProtected ? ProtectionCategory.Interrupted
                        : deadline.HasValue && t.FirstProtectedAt.Value > deadline.Value + 1e-9 ? ProtectionCategory.Late
                        : ProtectionCategory.InTime;
                    a = new ProtectionAssessment(player, c, t.InstructionReceivedAt, t.FirstProtectedAt, t.InstructionReceivedLate);
                }
                if (assessments.Count < IncidentLimits.MaxAssessments) assessments.Add(a);
                t.State = MemberShelterState.NotRequired;
            }
        }

        public SpaceWeatherStatus Status(Guid incidentId, double now)
        {
            var views = new List<EpisodeMessageView>();
            var quoted = new HashSet<string>(StringComparer.Ordinal);
            foreach (EpisodeMessage m in issued)
            {
                double at = issuedAt[m.Id];
                double? deadline = DeadlineAt(m);
                views.Add(new EpisodeMessageView(m, at, at + m.ValidForSeconds, deadline, ValidityOf(m, now)));
                foreach (string id in m.SourceRecordIds) quoted.Add(id);
            }
            var records = new List<SourceRecord>();
            foreach (SourceRecord r in Definition.Records)
                if (quoted.Contains(r.SourceId)) records.Add(r);
            var protections = new List<MemberProtection>();
            foreach (MemberTrack t in members)
                if (protections.Count < OutpostLimits.MaxCrew)
                    protections.Add(new MemberProtection(t.Player, t.State, HasReceived(EpisodeMessageKind.Watch, t.Player),
                        t.InstructionReceivedAt.HasValue));
            return new SpaceWeatherStatus(incidentId, Definition.Key, Definition.Mode, Definition.Label, Definition.TimelineNote,
                Definition.Dataset, records, views, receptions, Instruction(now), protections, assessments);
        }

        public void Forget(PlayerId player) => members.RemoveAll(m => m.Player == player);

        private ShelterInstructionStatus Instruction(double now)
        {
            EpisodeMessage latest = LatestInstruction();
            if (latest == null) return ShelterInstructionStatus.None;
            double at = issuedAt[latest.Id];
            double validUntil = at + latest.ValidForSeconds;
            double shelterBy = StartedAt + latest.ShelterByOffsetSeconds.Value;
            if (Ended)
            {
                EpisodeMessage end = issued[issued.Count - 1];
                return new ShelterInstructionStatus(InstructionState.Ended, latest.Id, shelterBy, validUntil, issuedAt[end.Id]);
            }
            return new ShelterInstructionStatus(now < validUntil ? InstructionState.InForce : InstructionState.InForceWithoutCurrentInformation,
                latest.Id, shelterBy, validUntil, null);
        }

        private EpisodeMessage LatestInstruction()
        {
            for (int i = issued.Count - 1; i >= 0; i--)
                if (issued[i].Kind == EpisodeMessageKind.ShelterInstruction || issued[i].Kind == EpisodeMessageKind.InstructionUpdate)
                    return issued[i];
            return null;
        }

        private AdvisoryValidity ValidityOf(EpisodeMessage m, double now)
        {
            foreach (EpisodeMessage later in issued)
                if (later.Supersedes == m.Id) return AdvisoryValidity.Superseded;
            if (m.Kind == EpisodeMessageKind.Watch && InstructionIssued) return AdvisoryValidity.Superseded;
            return now < issuedAt[m.Id] + m.ValidForSeconds ? AdvisoryValidity.Current : AdvisoryValidity.Expired;
        }

        private double? DeadlineAt(EpisodeMessage m) =>
            m.ShelterByOffsetSeconds.HasValue ? StartedAt + m.ShelterByOffsetSeconds.Value : (double?)null;

        private bool HasReceived(EpisodeMessageKind kind, PlayerId player)
        {
            foreach (EpisodeMessage m in issued)
                if (m.Kind == kind && received.Contains(m.Id + "|" + player.Value)) return true;
            return false;
        }

        private MemberTrack Track(PlayerId player)
        {
            foreach (MemberTrack t in members)
                if (t.Player == player) return t;
            var created = new MemberTrack { Player = player };
            if (members.Count < OutpostLimits.MaxCrew) members.Add(created);
            return created;
        }
    }
}
