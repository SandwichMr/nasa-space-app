using System;
using System.Collections.Generic;
using Trainer.Application.BaseOperations;
using Trainer.Application.Outpost;

namespace Trainer.Application.Incidents
{
    /// <summary>Renewable science assignments (D2): a board of offers fulfilled by REAL phase 4 science activities.</summary>
    public sealed partial class LivingBaseAuthority
    {
        private sealed class Assignment
        {
            public long Sequence;
            public AssignmentTemplate Template;
            public AssignmentState State;
            public double OfferedAt;
            public double DueAt;
            public double? AcceptedAt;
            public double? ClosedAt;
            public PlayerId AcceptedBy;

            public bool IsOpen => State == AssignmentState.Offered || State == AssignmentState.Accepted || State == AssignmentState.Postponed;

            public AssignmentRecord ToRecord() => new AssignmentRecord(Sequence, Template.Key, Template.ActivityKey, Template.Exterior, State,
                OfferedAt, DueAt, AcceptedAt, ClosedAt, AcceptedBy, Template.RewardCredits, State == AssignmentState.Completed);
        }

        private IEnumerable<Assignment> PublishedAssignments()
        {
            // Open entries always; the most recent closed ones fill the remaining window. Published in sequence order.
            var open = assignments.FindAll(a => a.IsOpen);
            var closed = assignments.FindAll(a => !a.IsOpen);
            int room = Math.Max(0, IncidentLimits.MaxAssignments - open.Count);
            var chosen = new List<Assignment>(open);
            chosen.AddRange(closed.GetRange(Math.Max(0, closed.Count - room), Math.Min(room, closed.Count)));
            chosen.Sort((x, y) => x.Sequence.CompareTo(y.Sequence));
            return chosen;
        }

        /// <summary>Expiries, postponed offers returning, fulfilment by new activities, then refilling the board.</summary>
        private void UpdateAssignments(double now, BaseOperationsSnapshot b)
        {
            foreach (Assignment a in assignments)
            {
                switch (a.State)
                {
                    case AssignmentState.Offered when now >= a.DueAt - Epsilon:
                        Close(a, AssignmentState.Expired, now);
                        break;
                    case AssignmentState.Postponed when now >= a.DueAt - Epsilon:
                        a.State = AssignmentState.Offered;
                        a.DueAt = now + Scenario.Assignments.OfferLifetimeSeconds;
                        break;
                    case AssignmentState.Accepted:
                        long fulfilling = FulfillingActivity(a, b);
                        if (fulfilling > 0)
                        {
                            consumedActivities.Add(fulfilling);
                            Close(a, AssignmentState.Completed, now);
                            creditsEarned += a.Template.RewardCredits;
                            credits = Math.Min(IncidentLimits.MaxCredits, credits + a.Template.RewardCredits);
                        }
                        else if (now >= a.DueAt - Epsilon) Close(a, AssignmentState.Expired, now);
                        break;
                }
            }
            FillBoard(now);
            PruneAssignments();
        }

        /// <summary>Sequence of a real phase 4 science activity with the template's key, completed at or after acceptance and not
        /// already used by another assignment; 0 when none.</summary>
        private long FulfillingActivity(Assignment a, BaseOperationsSnapshot b)
        {
            foreach (ActivityRecord activity in b.Activities)
                if (activity.Kind == ActivityKind.ScienceAssignment && activity.Status == ActivityStatus.Completed
                    && activity.Key == a.Template.ActivityKey && !consumedActivities.Contains(activity.Sequence)
                    && activity.EndedAtSeconds.HasValue && activity.EndedAtSeconds.Value >= a.AcceptedAt.Value - Epsilon)
                    return activity.Sequence;
            return 0;
        }

        private void Close(Assignment a, AssignmentState state, double now)
        {
            a.State = state;
            a.ClosedAt = now;
            templateReadyAt[a.Template.Key] = now + a.Template.RenewCooldownSeconds;
        }

        /// <summary>Offers new assignments into free slots: no template twice at once, not the last offered one again when another
        /// is available, renew cooldown respected, no exterior work offered while a shelter instruction is in force.</summary>
        private void FillBoard(double now)
        {
            int offered = 0;
            foreach (Assignment a in assignments)
                if (a.State == AssignmentState.Offered || a.State == AssignmentState.Postponed) offered++;
            bool instruction = lastEpisode != null && lastEpisode.InstructionInForce;
            while (offered < Scenario.Assignments.OfferSlots)
            {
                var candidates = new List<AssignmentTemplate>();
                foreach (AssignmentTemplate t in Scenario.Templates)
                {
                    if (assignments.Exists(a => a.IsOpen && a.Template.Key == t.Key)) continue;
                    if (templateReadyAt.TryGetValue(t.Key, out double ready) && now < ready - Epsilon) continue;
                    if (t.Exterior && instruction) continue;
                    candidates.Add(t);
                }
                if (candidates.Count > 1) candidates.RemoveAll(t => t.Key == lastOfferedTemplate);
                if (candidates.Count == 0) return;
                AssignmentTemplate pick = candidates[(int)(assignmentRandom.NextUInt64() % (ulong)candidates.Count)];
                assignments.Add(new Assignment
                {
                    Sequence = ++assignmentSequence, Template = pick, State = AssignmentState.Offered, OfferedAt = now,
                    DueAt = now + Scenario.Assignments.OfferLifetimeSeconds
                });
                lastOfferedTemplate = pick.Key;
                offered++;
            }
        }

        private void PruneAssignments()
        {
            // Keep every open entry and enough closed history for the published window.
            while (assignments.Count > IncidentLimits.MaxAssignments * 2)
            {
                int index = assignments.FindIndex(a => !a.IsOpen);
                if (index < 0) break;
                assignments.RemoveAt(index);
            }
        }

        private double NextAssignmentEvent(double now)
        {
            double next = double.PositiveInfinity;
            foreach (Assignment a in assignments)
                if (a.IsOpen && a.DueAt > now) next = Math.Min(next, a.DueAt);
            foreach (KeyValuePair<string, double> ready in templateReadyAt)
                if (ready.Value > now) next = Math.Min(next, ready.Value);
            return next;
        }

        private string ValidateAssignment(PlayerId sender, IncidentCommand c, double now)
        {
            Assignment a = assignments.Find(x => x.Sequence == c.AssignmentSequence);
            if (a == null) return IncidentReasons.UnknownAssignment;
            switch (c.Action)
            {
                case IncidentAction.AcceptAssignment:
                    if (a.State != AssignmentState.Offered) return IncidentReasons.AssignmentNotOffered;
                    int accepted = 0;
                    foreach (Assignment x in assignments) if (x.State == AssignmentState.Accepted) accepted++;
                    if (accepted >= Scenario.Assignments.MaxAccepted) return IncidentReasons.AssignmentLimitReached;
                    return null;
                case IncidentAction.PostponeAssignment:
                    return a.State == AssignmentState.Offered ? null : IncidentReasons.AssignmentNotOffered;
                default:
                    return a.State == AssignmentState.Accepted ? null : IncidentReasons.AssignmentNotAccepted;
            }
        }

        private void AcceptAssignmentCommand(PlayerId sender, IncidentCommand c, double now, BaseOperationsSnapshot b)
        {
            Assignment a = assignments.Find(x => x.Sequence == c.AssignmentSequence);
            switch (c.Action)
            {
                case IncidentAction.AcceptAssignment:
                    a.State = AssignmentState.Accepted;
                    a.AcceptedAt = now;
                    a.AcceptedBy = sender;
                    a.DueAt = now + a.Template.DeadlineSeconds;
                    break;
                case IncidentAction.PostponeAssignment:
                    a.State = AssignmentState.Postponed;
                    a.DueAt = now + Scenario.Assignments.PostponeSeconds;
                    break;
                default:
                    Close(a, AssignmentState.Cancelled, now);
                    break;
            }
            FillBoard(now);
        }
    }
}
