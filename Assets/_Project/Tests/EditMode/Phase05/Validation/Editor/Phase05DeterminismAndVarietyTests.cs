using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Application.Incidents;
using Trainer.Application.Outpost;
using H = Trainer.Tests.Phase05.Application.Phase05LivingBaseHarness;
using Trainer.Tests.Phase05.Application;

namespace Trainer.Tests.Phase05.Validation
{
    /// <summary>
    /// D5 phase 5 long-horizon checks on the REAL authority (fixture scenario, synthetic and historical modes): same seed ⇒ the same
    /// published history, different seeds ⇒ variety, and the director's guarantees (budget, anti-repeat, per-key cooldown, rest after
    /// a resolution, no power incident during an open episode) hold over every published snapshot of two simulated hours.
    /// </summary>
    public sealed class Phase05DeterminismAndVarietyTests
    {
        private const double Horizon = 7200;

        private static List<string> Run(LivingBaseSetup setup, out H harness, double horizon = Horizon, bool repairPower = false)
        {
            harness = setup.RequestedMode == DataMode.Historical
                ? new H(LivingBaseFixtures.Scenario(LivingBaseFixtures.FixtureHistoricalEpisode()), setup)
                : new H(null, setup);
            PlayerId crew = harness.JoinAndStart();
            var history = new List<string>();
            for (int step = 0; step < horizon / 5 && harness.Time < horizon; step++) // bounded: a stopped session no longer advances
            {
                harness.Tick(5, 0.5);
                if (repairPower) RepairPower(harness, crew);
                history.Add(Phase05Fingerprint.Of(harness.Now));
            }
            return history;
        }

        /// <summary>A crew member who answers power incidents like a player: inspect, then reset or isolate/replace/restore.</summary>
        private static void RepairPower(H h, PlayerId p)
        {
            if (h.Phase4.Outpost.State != SessionState.Running) return;
            IncidentRecord open = h.Now.Incidents.FirstOrDefault(i => i.IsOpen && i.Phase != IncidentPhase.Warning
                && (i.Kind == IncidentType.PowerConverterTrip || i.Kind == IncidentType.PowerControllerFault));
            if (open == null || h.Now.Operations.Count > 0) return;
            if (h.Position(p).DistanceTo(H.AtPower) > 0.05) h.Walk(p, H.AtPower);
            PowerEquipmentStatus power = h.Now.Power;
            if (power.Isolated) h.RunPower(p, power.PartReplaced ? IncidentAction.RestorePower : IncidentAction.ReplacePowerPart);
            else if (power.Observation == null || !power.Observation.Current) h.RunPower(p, IncidentAction.InspectPower);
            else if ((power.Observation.Findings & PowerFindings.ControllerDamaged) != 0) h.RunPower(p, IncidentAction.IsolatePower);
            else if ((power.Observation.Findings & PowerFindings.ConverterTripped) != 0) h.RunPower(p, IncidentAction.ResetConverter);
        }

        private static List<string> Accepted(H h) =>
            h.Published.SelectMany(s => s.Decisions).Where(d => d.Accepted).GroupBy(d => d.Sequence).Select(g => g.First())
                .OrderBy(d => d.Sequence).Select(d => d.CandidateKey + "@" + d.AtSeconds.ToString("R")).ToList();

        [Test]
        public void SameSeedReproducesTwoHoursOfPublishedHistoryExactly()
        {
            List<string> a = Run(LivingBaseFixtures.SyntheticSetup(7), out H ha);
            List<string> b = Run(LivingBaseFixtures.SyntheticSetup(7), out H hb);
            Assert.That(b, Is.EqualTo(a));
            Assert.That(ha.Now.Director.ConfigurationHash, Is.EqualTo(hb.Now.Director.ConfigurationHash));
            Assert.That(Accepted(ha), Is.Not.Empty, "the director acted within two hours");
            List<string> c = Run(new LivingBaseSetup(DataMode.Historical, 7), out _);
            List<string> d = Run(new LivingBaseSetup(DataMode.Historical, 7), out _);
            Assert.That(d, Is.EqualTo(c), "historical mode is deterministic too");
        }

        [Test]
        public void DifferentSeedsGiveDifferentSessions()
        {
            var distinct = new HashSet<string>();
            for (ulong seed = 1; seed <= 12; seed++)
            {
                Run(LivingBaseFixtures.SyntheticSetup(seed), out H h, 3600);
                distinct.Add(string.Join(",", Accepted(h)));
            }
            Assert.That(distinct.Count, Is.GreaterThanOrEqualTo(6), "at least half of 12 seeds produce a different incident history");
        }

        [Test]
        public void DirectorGuaranteesHoldOverTwoHours()
        {
            foreach (ulong seed in new ulong[] { 3, 11, 29 })
            {
                Run(LivingBaseFixtures.SyntheticSetup(seed), out H h, Horizon, repairPower: true);
                LivingBaseScenario s = h.Scenario;
                var decisions = h.Published.SelectMany(p => p.Decisions).Where(d => d.Accepted).GroupBy(d => d.Sequence).Select(g => g.First())
                    .OrderBy(d => d.Sequence).ToList();
                for (int i = 1; i < decisions.Count; i++)
                    Assert.That(decisions[i].CandidateKey, Is.Not.EqualTo(decisions[i - 1].CandidateKey), $"seed {seed}: anti-repeat");
                foreach (IGrouping<string, DirectorDecisionRecord> byKey in decisions.GroupBy(d => d.CandidateKey))
                {
                    double cooldown = s.DefinitionOf(byKey.Key).CooldownSeconds;
                    List<DirectorDecisionRecord> list = byKey.ToList();
                    for (int i = 1; i < list.Count; i++)
                        Assert.That(list[i].AtSeconds - list[i - 1].AtSeconds, Is.GreaterThanOrEqualTo(cooldown - 1e-6), $"seed {seed}: cooldown of {byKey.Key}");
                }
                foreach (LivingBaseSnapshot snap in h.Published)
                {
                    Assert.That(snap.Incidents.Count(i => i.IsOpen), Is.LessThanOrEqualTo(s.Director.MaxSimultaneous), $"seed {seed}: budget");
                    bool weatherOpen = snap.Incidents.Any(i => i.IsOpen && i.Kind == IncidentType.SpaceWeather);
                    if (weatherOpen)
                        Assert.That(snap.Incidents.Any(i => i.IsOpen && i.Phase == IncidentPhase.Warning
                            && (i.Kind == IncidentType.PowerConverterTrip || i.Kind == IncidentType.PowerControllerFault)
                            && i.AnnouncedAtSeconds > snap.Incidents.First(w => w.IsOpen && w.Kind == IncidentType.SpaceWeather).AnnouncedAtSeconds),
                            Is.False, $"seed {seed}: no power incident starts during an open episode");
                }
                var closes = h.Published.SelectMany(p => p.Incidents).Where(i => i.Phase == IncidentPhase.Resolved)
                    .Select(i => i.ClosedAtSeconds.Value).Distinct().ToList();
                foreach (double closed in closes)
                    Assert.That(decisions.Any(d => d.AtSeconds > closed + 1e-6 && d.AtSeconds < closed + s.Director.RestAfterResolutionSeconds - 1e-6),
                        Is.False, $"seed {seed}: rest window after the resolution at {closed}");
                Assert.That(decisions.All(d => d.AtSeconds >= s.Director.InitialGraceSeconds - 1e-6), $"seed {seed}: grace");
                Console.WriteLine($"seed {seed}: accepted={decisions.Count} keys={string.Join(",", decisions.Select(d => d.CandidateKey))} resolutions={closes.Count}");
                Assert.That(decisions.Count, Is.GreaterThanOrEqualTo(2), $"seed {seed}: the checks are exercised");
            }
        }
    }
}
