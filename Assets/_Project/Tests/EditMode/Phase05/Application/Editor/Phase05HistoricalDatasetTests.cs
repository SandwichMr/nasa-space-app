using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Incidents;
using H = Trainer.Tests.Phase05.Application.Phase05LivingBaseHarness;

namespace Trainer.Tests.Phase05.Application
{
    /// <summary>D2/D5 phase 5: the frozen DONKI episode loads, verifies its hash and plays offline; any defect falls back.</summary>
    public sealed class Phase05HistoricalDatasetTests
    {
        public const string RelativeFolder = "Assets/StreamingAssets/ScientificData/Phase05/donki-2017-09-10";

        public static string DatasetFolder()
        {
            string root = Environment.GetEnvironmentVariable("TRAINER_PROJECT_ROOT");
            for (string dir = root ?? Directory.GetCurrentDirectory(); dir != null; dir = Path.GetDirectoryName(dir))
            {
                string candidate = Path.Combine(dir, RelativeFolder);
                if (Directory.Exists(candidate)) return candidate;
            }
            Assert.Fail("frozen dataset folder not found from " + Directory.GetCurrentDirectory());
            return null;
        }

        private static byte[] Manifest() => File.ReadAllBytes(Path.Combine(DatasetFolder(), "manifest.json"));
        private static byte[] Episode() => File.ReadAllBytes(Path.Combine(DatasetFolder(), "episode.json"));

        [Test]
        public void TheFrozenEpisodeLoadsWithItsProvenance()
        {
            Assert.That(HistoricalEpisodeLoader.TryLoad(Manifest(), Episode(), out SpaceWeatherEpisodeDefinition e, out string problem), Is.True, problem);
            Assert.That(e.Mode, Is.EqualTo(DataMode.Historical));
            Assert.That(e.Dataset.Sha256, Has.Length.EqualTo(64));
            Assert.That(e.Records.Count, Is.EqualTo(8));
            Assert.That(e.Messages[0].Kind, Is.EqualTo(EpisodeMessageKind.Watch));
            Assert.That(e.Messages[e.Messages.Count - 1].Basis, Is.EqualTo(MessageBasis.DidacticDecision));
            SourceRecord notice = null;
            foreach (SourceRecord r in e.Records) if (r.SourceId == "20170910-AL-001") notice = r;
            Assert.That(notice.EventTimeUtc, Is.Null, "unknown stays null");
            Assert.That(notice.IssuedUtc, Is.EqualTo("2017-09-10T16:07Z"));
            Assert.That(notice.Uri, Does.StartWith("https://ccmc.gsfc.nasa.gov/DONKI/"));
        }

        [Test]
        public void AnyByteChangeIsAHashMismatch()
        {
            byte[] episode = Episode();
            episode[episode.Length / 2] ^= 0x01;
            Assert.That(HistoricalEpisodeLoader.TryLoad(Manifest(), episode, out SpaceWeatherEpisodeDefinition e, out string problem), Is.False);
            Assert.That(e, Is.Null);
            Assert.That(problem, Is.EqualTo(HistoricalEpisodeLoader.HashMismatch));
        }

        [Test]
        public void MissingOrBrokenFilesHaveStableProblemKeys()
        {
            Assert.That(HistoricalEpisodeLoader.TryLoad(null, Episode(), out _, out string a), Is.False);
            Assert.That(a, Is.EqualTo(HistoricalEpisodeLoader.Missing));
            Assert.That(HistoricalEpisodeLoader.TryLoad(Encoding.UTF8.GetBytes("{not json"), Episode(), out _, out string b), Is.False);
            Assert.That(b, Is.EqualTo(HistoricalEpisodeLoader.ManifestInvalid));
            byte[] bad = Encoding.UTF8.GetBytes("{\"schemaVersion\": 1, \"mode\": \"Historical\"}");
            string manifest = "{\"schemaVersion\": 1, \"sha256\": \"" + LivingBaseIds.Sha256Hex(bad) + "\"}";
            Assert.That(HistoricalEpisodeLoader.TryLoad(Encoding.UTF8.GetBytes(manifest), bad, out _, out string c), Is.False);
            Assert.That(c, Is.EqualTo(HistoricalEpisodeLoader.Invalid));
            string future = "{\"schemaVersion\": 2, \"sha256\": \"" + LivingBaseIds.Sha256Hex(bad) + "\"}";
            Assert.That(HistoricalEpisodeLoader.TryLoad(Encoding.UTF8.GetBytes(future), bad, out _, out string d), Is.False);
            Assert.That(d, Is.EqualTo(HistoricalEpisodeLoader.SchemaUnsupported));
        }

        [Test]
        public void TheRealEpisodePlaysOfflineAndNeverShowsTheFlareClassBeforeItsSubmission()
        {
            Assert.That(HistoricalEpisodeLoader.TryLoad(Manifest(), Episode(), out SpaceWeatherEpisodeDefinition e, out _), Is.True);
            var h = new H(H.Variant(new[] { H.Only(LivingBaseFixtures.WeatherKey) }, H.Eager(rest: 600), historical: e),
                new LivingBaseSetup(DataMode.Historical, 2017));
            h.JoinAndStart();
            h.Until(() => h.Now.SpaceWeather != null, 30);
            Assert.That(h.Now.SpaceWeather.Label, Does.Contain("HISTÓRICO"));
            bool flareSeen = false;
            while (h.Now.SpaceWeather.Instruction.State != InstructionState.Ended)
            {
                h.Tick(1);
                foreach (SourceRecord r in h.Now.SpaceWeather.VisibleRecords)
                    if (r.Kind == SourceRecordKind.Flare)
                    {
                        flareSeen = true;
                        Assert.That(h.Now.SpaceWeather.Issued[h.Now.SpaceWeather.Issued.Count - 1].Message.Id,
                            Is.EqualTo("hist-update-ongoing").Or.EqualTo("hist-end"));
                    }
                Assert.That(h.Time, Is.LessThan(600));
            }
            Assert.That(flareSeen, Is.True);
        }
    }
}
