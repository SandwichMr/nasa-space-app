using System;
using System.Collections.Generic;
using Trainer.Application.Outpost;

namespace Trainer.Application.Expedition
{
    /// <summary>
    /// Tracks, per present member, how they responded to the shelter instruction they RECEIVED (D2, inside the
    /// authority), and assesses them when the instruction ends. Inputs are only the member's reception time, the
    /// issued deadline (ShelterBy) and the authority's protection state at each step; never the hidden event times.
    ///
    /// Rules (D = deadline in force, R = first reception): the first protection at or after R is recorded; after
    /// max(D, first protection) any unprotected step marks the member Interrupted. InTime = first ≤ D and never
    /// interrupted; Late = first &gt; D and never interrupted; NotProtected = never protected after R; NotReceived =
    /// no reception (they could not know: not judged as a decision).
    /// </summary>
    public sealed class ProtectionTracker
    {
        private sealed class Entry
        {
            public double? ReceivedAt;
            public bool ReceivedAfterDeadline;
            public double? FirstProtectedAt;
            public bool Interrupted;
        }

        private readonly Dictionary<PlayerId, Entry> entries = new Dictionary<PlayerId, Entry>();

        public void Reset() => entries.Clear();

        public void Remove(PlayerId player) => entries.Remove(player);

        /// <summary>Records one step's end for <paramref name="player"/> at <paramref name="now"/>.</summary>
        public void Observe(PlayerId player, ProtectionState state, AdvisoryReception reception, ShelterInstructionStatus instruction, double now)
        {
            if (instruction == null) throw new ArgumentNullException(nameof(instruction));
            if (!instruction.InForce || reception == null) return;
            if (!entries.TryGetValue(player, out Entry entry))
            {
                entry = new Entry();
                entries.Add(player, entry);
            }
            if (!entry.ReceivedAt.HasValue)
            {
                entry.ReceivedAt = reception.ReceivedAtSeconds;
                entry.ReceivedAfterDeadline = reception.AfterShelterBy;
            }
            double deadline = instruction.ShelterBySeconds.Value;
            bool protectedNow = state == ProtectionState.Protected;
            if (protectedNow && !entry.FirstProtectedAt.HasValue) entry.FirstProtectedAt = now;
            if (!protectedNow && entry.FirstProtectedAt.HasValue && now >= Math.Max(deadline, entry.FirstProtectedAt.Value))
                entry.Interrupted = true;
        }

        public ProtectionAssessment Assess(PlayerId player, double deadline)
        {
            if (!entries.TryGetValue(player, out Entry entry) || !entry.ReceivedAt.HasValue)
                return new ProtectionAssessment(player, ProtectionCategory.NotReceived, null, null, false);
            ProtectionCategory category;
            if (!entry.FirstProtectedAt.HasValue) category = ProtectionCategory.NotProtected;
            else if (entry.Interrupted) category = ProtectionCategory.Interrupted;
            else if (entry.FirstProtectedAt.Value > deadline + AdvisoryTimeline.TimeToleranceSeconds) category = ProtectionCategory.Late;
            else category = ProtectionCategory.InTime;
            return new ProtectionAssessment(player, category, entry.ReceivedAt, entry.FirstProtectedAt, entry.ReceivedAfterDeadline);
        }

        /// <summary>Team result from the assessments of the members present when the instruction ended.</summary>
        public static ExpeditionOutcome Outcome(IReadOnlyList<ProtectionAssessment> assessments)
        {
            if (assessments == null) throw new ArgumentNullException(nameof(assessments));
            bool anyReceived = false, late = false;
            foreach (ProtectionAssessment a in assessments)
            {
                if (a.Category == ProtectionCategory.NotReceived) continue;
                anyReceived = true;
                if (a.Category == ProtectionCategory.NotProtected || a.Category == ProtectionCategory.Interrupted)
                    return ExpeditionOutcome.ProtectionIncomplete;
                if (a.Category == ProtectionCategory.Late) late = true;
            }
            if (!anyReceived) return ExpeditionOutcome.AdvisoryNotReceived;
            return late ? ExpeditionOutcome.ShelteredLate : ExpeditionOutcome.ShelteredInTime;
        }
    }
}
