using System;
using System.Collections.Generic;
using Trainer.Application.Outpost;

namespace Trainer.Application.Expedition
{
    /// <summary>
    /// What the base KNOWS about the scenario's particle episode at each moment (D2, inside the authority). It issues
    /// each authored message at its time, derives its validity (Current, Superseded, Expired), records who received
    /// what and when (radio coverage decided by the authority), and summarizes the shelter instruction. It never
    /// exposes or uses the episode's event times: those are disclosed only after the session ends, by the authority.
    /// </summary>
    public sealed class AdvisoryTimeline
    {
        public const double TimeToleranceSeconds = 1e-9;

        private readonly ParticleEventEpisode episode;
        private readonly List<AdvisoryReception> receptions = new List<AdvisoryReception>();
        private readonly HashSet<(string, PlayerId)> received = new HashSet<(string, PlayerId)>();
        private int issued;

        public AdvisoryTimeline(ParticleEventEpisode episode) => this.episode = episode;

        public bool HasEpisode => episode != null;
        public IReadOnlyList<AdvisoryReception> Receptions => receptions;

        public void Reset()
        {
            issued = 0;
            receptions.Clear();
            received.Clear();
        }

        /// <summary>Issues every message due at <paramref name="now"/>. True if any was issued.</summary>
        public bool IssueDue(double now)
        {
            if (episode == null) return false;
            bool any = false;
            while (issued < episode.Messages.Count && episode.Messages[issued].IssuedAtSeconds <= now + TimeToleranceSeconds)
            {
                issued++;
                any = true;
            }
            return any;
        }

        /// <summary>Next moment after <paramref name="now"/> when what the base knows changes or a deadline is reached:
        /// the next issue, the expiry of an issued current message, or the deadline of the instruction in force.</summary>
        public double NextEventAfter(double now)
        {
            if (episode == null) return double.PositiveInfinity;
            double next = double.PositiveInfinity;
            if (issued < episode.Messages.Count) next = Math.Min(next, episode.Messages[issued].IssuedAtSeconds);
            for (int i = 0; i < issued; i++)
            {
                AdvisoryMessage m = episode.Messages[i];
                if (m.ValidUntilSeconds > now + TimeToleranceSeconds && !IsSuperseded(i)) next = Math.Min(next, m.ValidUntilSeconds);
            }
            ShelterInstructionStatus instruction = Instruction(now);
            if (instruction.InForce && instruction.ShelterBySeconds.Value > now + TimeToleranceSeconds)
                next = Math.Min(next, instruction.ShelterBySeconds.Value);
            return next;
        }

        /// <summary>Delivers to <paramref name="player"/> every issued, CURRENT message not received yet, if covered.
        /// Superseded or expired information is never delivered late as if it were news. True if any was delivered.</summary>
        public bool Deliver(PlayerId player, bool covered, double now)
        {
            if (episode == null || !covered) return false;
            bool any = false;
            for (int i = 0; i < issued; i++)
            {
                AdvisoryMessage m = episode.Messages[i];
                if (Validity(i, now) != AdvisoryValidity.Current || received.Contains((m.Id, player))) continue;
                received.Add((m.Id, player));
                bool late = m.ShelterBySeconds.HasValue && now > m.ShelterBySeconds.Value + TimeToleranceSeconds;
                receptions.Add(new AdvisoryReception(m.Id, player, now, late));
                any = true;
            }
            return any;
        }

        public List<IssuedAdvisory> Issued(double now)
        {
            var list = new List<IssuedAdvisory>(issued);
            for (int i = 0; i < issued; i++) list.Add(new IssuedAdvisory(episode.Messages[i], Validity(i, now)));
            return list;
        }

        /// <summary>The shelter instruction as known now. An expired latest message without replacement keeps the
        /// instruction in force (no current information is never an all-clear); only an explicit end ends it.</summary>
        public ShelterInstructionStatus Instruction(double now)
        {
            int latest = LatestOfChain();
            if (latest < 0) return ShelterInstructionStatus.None;
            AdvisoryMessage message = episode.Messages[latest];
            if (message.Kind == AdvisoryKind.InstructionEnd)
            {
                AdvisoryMessage last = LastInstructionBefore(latest);
                return new ShelterInstructionStatus(InstructionState.Ended, message.Id, last.ShelterBySeconds, last.ValidUntilSeconds,
                    message.IssuedAtSeconds);
            }
            InstructionState state = Validity(latest, now) == AdvisoryValidity.Expired
                ? InstructionState.InForceWithoutCurrentInformation : InstructionState.InForce;
            return new ShelterInstructionStatus(state, message.Id, message.ShelterBySeconds, message.ValidUntilSeconds, null);
        }

        /// <summary>First time <paramref name="player"/> received any instruction or update message, or null.</summary>
        public AdvisoryReception FirstInstructionReception(PlayerId player)
        {
            foreach (AdvisoryReception reception in receptions)
                if (reception.Player == player && KindOf(reception.MessageId) != AdvisoryKind.InstructionEnd) return reception;
            return null;
        }

        /// <summary>Ids of the messages <paramref name="player"/> has received (what that member knew).</summary>
        public List<string> KnownBy(PlayerId player)
        {
            var ids = new List<string>();
            foreach (AdvisoryReception reception in receptions)
                if (reception.Player == player) ids.Add(reception.MessageId);
            return ids;
        }

        private AdvisoryValidity Validity(int index, double now)
        {
            if (IsSuperseded(index)) return AdvisoryValidity.Superseded;
            return now + TimeToleranceSeconds >= episode.Messages[index].ValidUntilSeconds ? AdvisoryValidity.Expired : AdvisoryValidity.Current;
        }

        private bool IsSuperseded(int index)
        {
            string id = episode.Messages[index].Id;
            for (int i = index + 1; i < issued; i++)
                if (episode.Messages[i].Supersedes == id) return true;
            return false;
        }

        /// <summary>Latest issued message of the first instruction chain (instruction, its updates and its end).</summary>
        private int LatestOfChain()
        {
            if (episode == null || issued == 0) return -1;
            int latest = 0;
            for (int i = 1; i < issued; i++)
                if (episode.Messages[i].Supersedes == episode.Messages[latest].Id) latest = i;
            return latest;
        }

        private AdvisoryMessage LastInstructionBefore(int endIndex)
        {
            string target = episode.Messages[endIndex].Supersedes;
            for (int i = endIndex - 1; i >= 0; i--)
                if (episode.Messages[i].Id == target)
                {
                    if (episode.Messages[i].Kind != AdvisoryKind.InstructionEnd) return episode.Messages[i];
                    target = episode.Messages[i].Supersedes;
                }
            return episode.Messages[0];
        }

        private AdvisoryKind KindOf(string id)
        {
            foreach (AdvisoryMessage m in episode.Messages)
                if (m.Id == id) return m.Kind;
            return AdvisoryKind.ShelterInstruction;
        }
    }
}
