using System;
using System.Linq;
using System.Text;
using Trainer.Application;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Application.Incidents;

namespace Trainer.Tests.Phase05.Application
{
    public static class Phase05Fingerprint
    {
        /// <summary>Public structural fingerprint of a phase 5 snapshot (same-assembly byte identity is proven by the cable tests in
        /// Tests/PlayMode/Networking/Phase05, which see the internal codec).</summary>
        public static string Of(LivingBaseSnapshot s)
        {
            var b = new System.Text.StringBuilder();
            b.Append(s.Epoch).Append('/').Append(s.Revision).Append(' ').Append(s.SimulatedTimeSeconds.ToString("R")).Append(' ')
                .Append(s.Data.Availability).Append(s.Data.ProblemKey).Append(' ').Append(s.Director.Phase).Append(s.Director.Evaluations)
                .Append(s.Director.ConfigurationHash).Append('\n');
            foreach (IncidentRecord i in s.Incidents) b.Append("I ").Append(i.Id).Append(i.Phase).Append(i.ActiveAtSeconds).Append(i.ResultKey).Append('\n');
            PowerEquipmentStatus p = s.Power;
            b.Append("P ").Append(p.Isolated).Append(p.PartReplaced).Append(p.Knowledge).Append(p.StateSequence).Append(p.Observation?.Sequence)
                .Append(p.Observation?.Findings).Append(p.Observation?.Current).Append(p.EffectiveGenerationWatts.ToString("R"))
                .Append(p.EffectiveMaxDischargeWatts.ToString("R")).Append('\n');
            if (s.SpaceWeather != null)
            {
                b.Append("W ").Append(s.SpaceWeather.Label).Append(s.SpaceWeather.Instruction.State).Append(s.SpaceWeather.Instruction.LatestMessageId);
                foreach (EpisodeMessageView m in s.SpaceWeather.Issued) b.Append(' ').Append(m.Message.Id).Append(m.Validity);
                foreach (SourceRecord r in s.SpaceWeather.VisibleRecords) b.Append(" r:").Append(r.SourceId);
                foreach (AdvisoryReception r in s.SpaceWeather.Receptions) b.Append(" rx:").Append(r.MessageId).Append(r.Player.Value);
                foreach (MemberProtection m in s.SpaceWeather.Members) b.Append(" m:").Append(m.Player.Value).Append(m.State);
                b.Append('\n');
            }
            foreach (AssignmentRecord a in s.Assignments) b.Append("A ").Append(a.Sequence).Append(a.TemplateKey).Append(a.State).Append(a.AcceptedBy.Value).Append('\n');
            b.Append("C ").Append(s.Credits).Append('/').Append(s.CreditsEarned).Append(" U ").Append(s.Upgrade.Installed).Append('\n');
            foreach (IncidentOperationProgress o in s.Operations) b.Append("O ").Append(o.CommandId).Append(o.WorkSeconds.ToString("R")).Append('\n');
            foreach (IncidentFeedback f in s.Feedback) b.Append("F ").Append(f.CommandId).Append(f.ExplanationKey).Append('\n');
            foreach (DirectorDecisionRecord d in s.Decisions) b.Append("D ").Append(d.Sequence).Append(d.CandidateKey).Append(d.Accepted).Append(d.ReasonKey).Append('\n');
            b.Append("crew ").Append(s.BaseOperations.Outpost.Crew.Count);
            return b.ToString();
        }
    }
}
