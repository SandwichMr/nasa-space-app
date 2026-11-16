using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Trainer.Application.Incidents
{
    /// <summary>
    /// Versioned pseudo-random generator of the phase 5 director (PRNG version 1, <see cref="DirectorStatus.CurrentPrngVersion"/>):
    /// SplitMix64. Plain arithmetic on ulong, identical on every platform and runtime. Each stream is seeded from the session seed,
    /// the epoch and a stream tag, so assignment draws never shift director draws (and vice versa).
    /// </summary>
    public sealed class DirectorRandom
    {
        public const ulong DirectorStream = 0x44495245u;   // "DIRE"
        public const ulong AssignmentStream = 0x41535347u; // "ASSG"
        public const ulong EpisodeStream = 0x45504953u;    // "EPIS"

        private ulong state;

        public DirectorRandom(ulong seed, long epoch, ulong stream)
        {
            state = seed ^ Mix((ulong)epoch * 0x9E3779B97F4A7C15UL) ^ Mix(stream);
        }

        /// <summary>Next 64-bit value (SplitMix64).</summary>
        public ulong NextUInt64()
        {
            state += 0x9E3779B97F4A7C15UL;
            return Mix(state);
        }

        /// <summary>Uniform in [0, 1) with 53 bits.</summary>
        public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>Uniform in [min, max].</summary>
        public double Range(double min, double max) => min + (max - min) * NextDouble();

        private static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Deterministic ids and hashes of the phase 5 authority (no clock, no Guid.NewGuid).</summary>
    public static class LivingBaseIds
    {
        /// <summary>Guid from a SHA-256 of the given parts (stable text form, invariant culture).</summary>
        public static Guid Derive(params object[] parts)
        {
            var text = new StringBuilder("trainer-phase5");
            foreach (object part in parts)
                text.Append('|').Append(Convert.ToString(part, CultureInfo.InvariantCulture));
            byte[] hash;
            using (SHA256 sha = SHA256.Create()) hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
            var bytes = new byte[16];
            Array.Copy(hash, bytes, 16);
            bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50); // version-like nibble, never all zero
            return new Guid(bytes);
        }

        /// <summary>Lowercase hex SHA-256 of UTF-8 text.</summary>
        public static string Sha256Hex(string text)
        {
            byte[] hash;
            using (SHA256 sha = SHA256.Create()) hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
            var sb = new StringBuilder(64);
            foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>Canonical text of everything that drives the director and the boards (catalog, rules, templates, upgrade,
        /// episode keys). Same scenario ⇒ same text ⇒ same <see cref="DirectorStatus.ConfigurationHash"/>.</summary>
        public static string CanonicalConfiguration(LivingBaseScenario s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            var t = new StringBuilder("phase5-config-v1\n");
            string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
            foreach (IncidentDefinition d in s.Catalog)
                t.Append("incident ").Append(d.Key).Append(' ').Append(d.Kind).Append(' ').Append(F(d.Weight)).Append(' ')
                    .Append(F(d.CooldownSeconds)).Append(' ').Append(F(d.WarningSeconds)).Append(' ')
                    .Append(d.ProcessorFailAfterSeconds.HasValue ? F(d.ProcessorFailAfterSeconds.Value) : "-").Append(' ')
                    .Append(F(d.PowerFactor)).Append(' ').Append(F(d.IsolatedFactor)).Append(' ').Append(d.SpareKey ?? "-").Append('\n');
            DirectorRules r = s.Director;
            t.Append("director ").Append(F(r.EvaluationIntervalSeconds)).Append(' ').Append(F(r.InitialGraceSeconds)).Append(' ')
                .Append(F(r.RestAfterResolutionSeconds)).Append(' ').Append(r.MaxSimultaneous).Append(' ').Append(F(r.PressurePerEvaluation))
                .Append(' ').Append(F(r.MinBatteryFractionForPowerIncident)).Append(' ').Append(F(r.RecoveryObservationSeconds)).Append('\n');
            PowerOperationRules p = s.PowerOperations;
            t.Append("power ").Append(s.PowerStation.Value).Append(' ').Append(F(p.InspectSeconds)).Append(' ').Append(F(p.ResetSeconds))
                .Append(' ').Append(F(p.ReplaceSeconds)).Append(' ').Append(F(p.ObservationValiditySeconds)).Append('\n');
            foreach (AssignmentTemplate a in s.Templates)
                t.Append("template ").Append(a.Key).Append(' ').Append(a.ActivityKey).Append(' ').Append(a.Exterior).Append(' ')
                    .Append(a.RewardCredits).Append(' ').Append(F(a.DeadlineSeconds)).Append(' ').Append(F(a.RenewCooldownSeconds)).Append('\n');
            AssignmentRules b = s.Assignments;
            t.Append("board ").Append(b.OfferSlots).Append(' ').Append(F(b.OfferLifetimeSeconds)).Append(' ').Append(F(b.PostponeSeconds))
                .Append(' ').Append(b.MaxAccepted).Append('\n');
            UpgradeDefinition u = s.Upgrade;
            t.Append("upgrade ").Append(u.Key).Append(' ').Append(u.Station.Value).Append(' ').Append(u.CreditCost).Append(' ').Append(u.SpareKey)
                .Append(' ').Append(F(u.InstallSeconds)).Append(' ').Append(F(u.RecoveryFraction)).Append(' ').Append(F(u.AddedEssentialWatts)).Append('\n');
            SyntheticEpisodeRules e = s.SyntheticEpisodes;
            t.Append("synthetic ").Append(F(e.MinWatchLeadSeconds)).Append(' ').Append(F(e.MaxWatchLeadSeconds)).Append(' ')
                .Append(F(e.ShelterWithinSeconds)).Append(' ').Append(F(e.MinShelterPeriodSeconds)).Append(' ')
                .Append(F(e.MaxShelterPeriodSeconds)).Append(' ').Append(F(e.MessageValiditySeconds)).Append('\n');
            t.Append("historical ").Append(s.HistoricalEpisode == null ? "-" : s.HistoricalEpisode.Key + " "
                + (s.HistoricalEpisode.Dataset?.Sha256 ?? "-")).Append('\n');
            return t.ToString();
        }
    }
}
