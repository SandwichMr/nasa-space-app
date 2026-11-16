using System;
using System.Collections.Generic;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Incidents;
using Trainer.Application.Outpost;
using Trainer.Simulation.Incidents;
using H = Trainer.Tests.Phase05.Application.Phase05LivingBaseHarness;

namespace Trainer.Tests.Phase05.Application
{
    /// <summary>D2 phase 5: space-weather episodes (historical, synthetic, fallback), the assignment board and the upgrade.</summary>
    public sealed class Phase05SpaceWeatherAndBoardTests
    {
        private static H Weather(LivingBaseSetup setup = null, SpaceWeatherEpisodeDefinition historical = null, string problem = null) =>
            new H(H.Variant(new[] { H.Only(LivingBaseFixtures.WeatherKey) }, H.Eager(rest: 600), historical: historical), setup, problem);

        [Test]
        public void ASyntheticEpisodeIssuesOnlyDueMessagesAndIsLabelled()
        {
            H h = Weather();
            PlayerId p = h.JoinAndStart();
            h.Until(() => h.Now.SpaceWeather != null, 30);
            SpaceWeatherStatus w = h.Now.SpaceWeather;
            Assert.That(w.Mode, Is.EqualTo(DataMode.Synthetic));
            Assert.That(w.Label, Does.Contain("ENTRENAMIENTO"));
            Assert.That(w.Issued.Count, Is.EqualTo(1), "only the watch is issued at the start");
            Assert.That(w.Issued[0].Message.Kind, Is.EqualTo(EpisodeMessageKind.Watch));
            Assert.That(w.VisibleRecords, Is.Empty, "a synthetic episode quotes no NASA record");
            Assert.That(w.Instruction.State, Is.EqualTo(InstructionState.None));
            foreach (LivingBaseSnapshot s in h.Published)
                if (s.SpaceWeather != null)
                    foreach (EpisodeMessageView m in s.SpaceWeather.Issued)
                        Assert.That(m.IssuedAtSeconds, Is.LessThanOrEqualTo(s.SimulatedTimeSeconds + 1e-6), "no message from the future");
            h.Until(() => h.Now.SpaceWeather.Instruction.InForce, 90);
            Assert.That(h.OpenIncident(IncidentType.SpaceWeather).Phase, Is.EqualTo(IncidentPhase.Active));
            MemberProtection m0 = h.Now.SpaceWeather.Members[0];
            Assert.That(m0.State, Is.Not.EqualTo(MemberShelterState.Protected), "being inside the base is not enough");
            Assert.That(m0.ReceivedInstruction, Is.True, "interior members receive by radio");
        }

        [Test]
        public void ShelteringInAPreparedShelterIsProtectedAndAssessedInTime()
        {
            H h = Weather();
            PlayerId p = h.JoinAndStart();
            h.Walk(p, H.AtShelterPanel);
            h.DoTask(p, ExpeditionFixtures.ShelterPanel, ExpeditionFixtures.PrepareShelter);
            h.Until(() => h.Now.SpaceWeather != null && h.Now.SpaceWeather.Instruction.InForce, 120);
            h.Tick(1);
            Assert.That(h.Now.SpaceWeather.Members[0].State, Is.EqualTo(MemberShelterState.Protected));
            Assert.That(h.OpenIncident(IncidentType.SpaceWeather).Phase, Is.EqualTo(IncidentPhase.Intervention));
            h.Until(() => h.Now.SpaceWeather.Instruction.State == InstructionState.Ended, 240);
            Assert.That(h.Now.SpaceWeather.Assessments.Count, Is.EqualTo(1));
            Assert.That(h.Now.SpaceWeather.Assessments[0].Category, Is.EqualTo(ProtectionCategory.InTime));
            Assert.That(h.LatestIncident(IncidentType.SpaceWeather).ResultKey, Is.EqualTo("shelter-period-ended"));
            EpisodeMessageView end = h.Now.SpaceWeather.Issued[h.Now.SpaceWeather.Issued.Count - 1];
            Assert.That(end.Message.Basis, Is.EqualTo(MessageBasis.DidacticDecision), "the end is a training decision, never a NASA all-clear");
            Assert.That(h.Phase4.Outpost.State, Is.EqualTo(SessionState.Running));
        }

        [Test]
        public void StayingOutsideTheShelterIsAssessedNotProtected()
        {
            H h = Weather();
            h.JoinAndStart();
            h.Until(() => h.Now.SpaceWeather != null && h.Now.SpaceWeather.Instruction.State == InstructionState.Ended, 300);
            Assert.That(h.Now.SpaceWeather.Assessments[0].Category, Is.EqualTo(ProtectionCategory.NotProtected));
        }

        [Test]
        public void TheHistoricalEpisodePlaysFirstAndShowsOnlyQuotedRecordsWhenIssued()
        {
            H h = Weather(new LivingBaseSetup(DataMode.Historical, 42), LivingBaseFixtures.FixtureHistoricalEpisode());
            h.JoinAndStart();
            Assert.That(h.Now.Data.Availability, Is.EqualTo(DataAvailability.Loaded));
            h.Until(() => h.Now.SpaceWeather != null, 30);
            SpaceWeatherStatus w = h.Now.SpaceWeather;
            Assert.That(w.Mode, Is.EqualTo(DataMode.Historical));
            Assert.That(w.Dataset, Is.Not.Null);
            Assert.That(w.VisibleRecords.Count, Is.EqualTo(1), "only the record quoted by the issued watch");
            Assert.That(w.VisibleRecords[0].SourceId, Is.EqualTo("FX-AL-001"));
            h.Until(() => h.Now.SpaceWeather.Issued.Count >= 2, 60);
            Assert.That(h.Now.SpaceWeather.VisibleRecords.Count, Is.EqualTo(2));
            h.Until(() => h.Now.SpaceWeather.Instruction.State == InstructionState.Ended, 200);
        }

        [Test]
        public void AMissingDatasetFallsBackToLabelledSyntheticEpisodesNeverToNoHazard()
        {
            H h = Weather(new LivingBaseSetup(DataMode.Historical, 42), null, "dataset-hash-mismatch");
            h.JoinAndStart();
            Assert.That(h.Now.Data.Availability, Is.EqualTo(DataAvailability.FallbackSynthetic));
            Assert.That(h.Now.Data.ProblemKey, Is.EqualTo("dataset-hash-mismatch"));
            h.Until(() => h.Now.SpaceWeather != null, 30);
            Assert.That(h.Now.SpaceWeather.Mode, Is.EqualTo(DataMode.Synthetic));
        }

        [Test]
        public void CurrentObservationModeIsRefused()
        {
            Assert.Throws<ArgumentException>(() => new LivingBaseSetup(DataMode.Current, 1));
        }

        [Test]
        public void NoPowerIncidentIsStartedWhileAnEpisodeIsOpen()
        {
            var catalog = new[] { H.Only(LivingBaseFixtures.WeatherKey), H.Only(LivingBaseFixtures.TripKey) };
            var h = new H(H.Variant(catalog, H.Eager(rest: 0, maxSimultaneous: 2)));
            h.JoinAndStart();
            for (int i = 0; i < 4 * 900; i++)
            {
                h.Authority.Tick(0.25);
                bool weather = h.OpenIncident(IncidentType.SpaceWeather) != null;
                IncidentRecord trip = h.OpenIncident(IncidentType.PowerConverterTrip);
                if (weather && trip != null)
                    Assert.That(trip.AnnouncedAtSeconds, Is.LessThan(h.OpenIncident(IncidentType.SpaceWeather).AnnouncedAtSeconds),
                        "a power fault may predate the episode, never start during it");
            }
        }

        // ---------------------------------------------------------------- board and upgrade

        [Test]
        public void TheBoardOffersRenewsAndBoundsAcceptance()
        {
            var h = new H();
            PlayerId p = h.JoinAndStart();
            List<AssignmentRecord> board = new List<AssignmentRecord>(h.Now.Assignments);
            Assert.That(board.Count, Is.EqualTo(2));
            Assert.That(board[0].TemplateKey, Is.Not.EqualTo(board[1].TemplateKey));
            Assert.That(h.Assignment(p, IncidentAction.AcceptAssignment, 99).Reason, Is.EqualTo(IncidentReasons.UnknownAssignment));
            Assert.That(h.Assignment(p, IncidentAction.CancelAssignment, board[0].Sequence).Reason, Is.EqualTo(IncidentReasons.AssignmentNotAccepted));
            Assert.That(h.Assignment(p, IncidentAction.AcceptAssignment, board[0].Sequence).Accepted);
            Assert.That(h.Assignment(p, IncidentAction.AcceptAssignment, board[1].Sequence).Reason, Is.EqualTo(IncidentReasons.AssignmentLimitReached));
            Assert.That(h.Assignment(p, IncidentAction.PostponeAssignment, board[1].Sequence).Accepted);
            Assert.That(h.Assignment(p, IncidentAction.CancelAssignment, board[0].Sequence).Accepted);
            Assert.That(h.Now.Credits, Is.Zero, "cancelling earns nothing");
            Assert.That(h.Phase4.Outpost.State, Is.EqualTo(SessionState.Running), "no reset, no global completion");
        }

        [Test]
        public void UnacceptedOffersExpireAndTheBoardRefillsWithoutImmediateRepeats()
        {
            var h = new H();
            h.JoinAndStart();
            long first = h.Now.Assignments[0].Sequence;
            h.Tick(h.Scenario.Assignments.OfferLifetimeSeconds + 1);
            Assert.That(h.Now.Assignments.Count, Is.GreaterThanOrEqualTo(2));
            bool expired = false;
            foreach (AssignmentRecord a in h.Now.Assignments) if (a.Sequence == first) expired = a.State == AssignmentState.Expired;
            Assert.That(expired, Is.True);
        }

        [Test]
        public void ARealSortieFulfilsTheAcceptedAssignmentOnceAndPaysTheUpgrade()
        {
            var templates = new[] { new AssignmentTemplate("regolith-survey", BaseOperationsFixtures.RegolithOpportunity, true, 2, 900, 60) };
            var upgrade = new UpgradeDefinition(LivingBaseFixtures.UpgradeKey, BaseOperationsFixtures.WaterStation, 2,
                BaseOperationsFixtures.ProcessorFilter, 20, 0.95, 15);
            var h = new H(H.Variant(new[] { H.Only(LivingBaseFixtures.WeatherKey) }, new DirectorRules(30, 3000, 90, 1, 0.5, 0.25, 10),
                templates, new AssignmentRules(1, 900, 120, 1), upgrade));
            PlayerId p = h.JoinAndStart();
            h.Walk(p, H.AtWater);
            Assert.That(h.Send(p, h.AtStation(p, IncidentAction.InstallUpgrade, BaseOperationsFixtures.WaterStation)).Reason,
                Is.EqualTo(IncidentReasons.InsufficientCredits));
            long seq = h.Now.Assignments[0].Sequence;
            Assert.That(h.Assignment(p, IncidentAction.AcceptAssignment, seq).Accepted);
            h.Sortie(p);
            h.Tick(1);
            AssignmentRecord done = Find(h, seq);
            Assert.That(done.State, Is.EqualTo(AssignmentState.Completed));
            Assert.That(done.Rewarded, Is.True);
            Assert.That(h.Now.Credits, Is.EqualTo(2));
            Assert.That(h.Now.CreditsEarned, Is.EqualTo(2));
            h.Tick(5);
            Assert.That(h.Now.Credits, Is.EqualTo(2), "the reward is applied once");

            double essential = h.Phase4.Expedition.Outpost.Resources.Power.EssentialRequestedWatts;
            h.Walk(p, H.AtWater);
            Assert.That(h.Send(p, h.AtStation(p, IncidentAction.InstallUpgrade, BaseOperationsFixtures.WaterStation)).Accepted);
            Assert.That(h.Now.Credits, Is.Zero);
            h.Until(() => h.Now.Upgrade.Installed, 40);
            Assert.That(h.Equipment.ProcessorNominalRecovery, Is.EqualTo(0.95));
            h.Tick(1);
            Assert.That(h.Phase4.Expedition.Outpost.Resources.Power.EssentialRequestedWatts, Is.EqualTo(essential + 15).Within(1e-9),
                "the upgrade adds a permanent essential load");
            Assert.That(h.Send(p, h.AtStation(p, IncidentAction.InstallUpgrade, BaseOperationsFixtures.WaterStation)).Reason,
                Is.EqualTo(IncidentReasons.UpgradeInstalled));
        }

        private static AssignmentRecord Find(H h, long sequence)
        {
            foreach (AssignmentRecord a in h.Now.Assignments) if (a.Sequence == sequence) return a;
            Assert.Fail("assignment " + sequence + " published");
            return null;
        }
    }
}
