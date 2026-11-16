using System;
using System.Globalization;

namespace Trainer.Application.Incidents
{
    /// <summary>
    /// Seeded generator of SYNTHETIC training space-weather episodes (D2). Every number comes from <see cref="SyntheticEpisodeRules"/>
    /// (game parameters, not physics) and the PRNG stream; no NASA record is quoted and nothing is presented as a forecast.
    /// Structure: watch at 0 → shelter instruction after a lead → mid-period update → end of shelter period (training decision).
    /// </summary>
    public static class SyntheticEpisodeGenerator
    {
        public const string Label = "ENTRENAMIENTO · episodio sintético";
        public const string Provenance = "Synthetic training episode generated from the scenario rules and the session seed; not a NASA record.";
        public const string TimelineNote = "Synthetic timeline in session seconds; no real event is represented.";

        public static SpaceWeatherEpisodeDefinition Generate(SyntheticEpisodeRules rules, DirectorRandom random, long ordinal)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (ordinal < 1) throw new ArgumentOutOfRangeException(nameof(ordinal));
            double lead = Round(random.Range(rules.MinWatchLeadSeconds, rules.MaxWatchLeadSeconds));
            double period = Round(random.Range(rules.MinShelterPeriodSeconds, rules.MaxShelterPeriodSeconds));
            double validity = rules.MessageValiditySeconds;
            double instructionAt = lead;
            double shelterBy = instructionAt + rules.ShelterWithinSeconds;
            double updateAt = instructionAt + Round(period / 2d);
            double endAt = instructionAt + period;
            string n = ordinal.ToString(CultureInfo.InvariantCulture);
            var messages = new[]
            {
                new EpisodeMessage("syn-watch", EpisodeMessageKind.Watch, MessageBasis.SyntheticTraining, 0d, validity, null, null,
                    "sw-synthetic-watch", null, null),
                new EpisodeMessage("syn-instruction", EpisodeMessageKind.ShelterInstruction, MessageBasis.SyntheticTraining, instructionAt,
                    validity, shelterBy, null, "sw-synthetic-instruction", null, null),
                new EpisodeMessage("syn-update", EpisodeMessageKind.InstructionUpdate, MessageBasis.SyntheticTraining, updateAt, validity,
                    Math.Max(shelterBy, updateAt), "syn-instruction", "sw-synthetic-update", null, null),
                new EpisodeMessage("syn-end", EpisodeMessageKind.InstructionEnd, MessageBasis.DidacticDecision, endAt, validity, null,
                    "syn-update", "sw-shelter-period-ended", null, null)
            };
            return new SpaceWeatherEpisodeDefinition("synthetic-" + n, DataMode.Synthetic, Label, Provenance, TimelineNote, null, null,
                messages, instructionAt, endAt);
        }

        private static double Round(double seconds) => Math.Round(seconds, 1, MidpointRounding.AwayFromZero);
    }
}
