using System;
using System.Collections.Generic;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Incidents;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.Incidents;
using H = Trainer.Tests.Phase05.Application.Phase05LivingBaseHarness;

namespace Trainer.Tests.Phase05.Application
{
    /// <summary>D2 phase 5: composition, director determinism/variety and the two electrical incidents end to end.</summary>
    public sealed class Phase05DirectorAndPowerTests
    {
        [Test]
        public void TheAuthorityComposesPhase4AndPublishesAnInitialSnapshot()
        {
            var h = new H();
            LivingBaseSnapshot s = h.Now;
            Assert.That(s.Director.Phase, Is.EqualTo(DirectorPhase.Halted), "Ready session: the director is halted");
            Assert.That(s.Director.Seed, Is.EqualTo(42UL));
            Assert.That(s.Director.ConfigurationHash, Has.Length.EqualTo(64));
            Assert.That(s.Data.Availability, Is.EqualTo(DataAvailability.NotRequested));
            Assert.That(s.Incidents, Is.Empty);
            Assert.That(s.Assignments.Count, Is.EqualTo(2), "the board starts with its offer slots filled");
            Assert.That(s.Power.Knowledge, Is.EqualTo(EquipmentKnowledge.Unknown));
            Assert.That(new H().Now.Director.ConfigurationHash, Is.EqualTo(s.Director.ConfigurationHash), "same scenario, same hash");
        }

        [Test]
        public void NoIncidentDuringGraceAndNothingAdvancesWhilePaused()
        {
            var h = new H(H.Variant(director: H.Eager(grace: 60)));
            PlayerId p = h.JoinAndStart();
            h.Tick(55);
            Assert.That(h.Now.Incidents, Is.Empty);
            Assert.That(h.Now.Director.Phase, Is.EqualTo(DirectorPhase.Grace));
            Assert.That(h.Session(p, ExpeditionAction.Pause).Accepted);
            double t = h.Time;
            h.Tick(200);
            Assert.That(h.Time, Is.EqualTo(t), "paused: no clock");
            Assert.That(h.Now.Incidents, Is.Empty);
            Assert.That(h.Now.Director.Phase, Is.EqualTo(DirectorPhase.Halted));
        }

        [Test]
        public void SameSeedSameDecisionsDifferentSeedsGiveVariety()
        {
            List<string> Run(ulong seed)
            {
                var h = new H(setup: new LivingBaseSetup(DataMode.Synthetic, seed));
                h.JoinAndStart();
                var keys = new List<string>();
                long lastSeq = 0;
                for (int i = 0; i < 1800 * 4; i++)
                {
                    h.Authority.Tick(0.25);
                    foreach (IncidentRecord r in h.Now.Incidents)
                        if (r.Sequence > lastSeq)
                        {
                            keys.Add(r.Key + "@" + Math.Round(r.AnnouncedAtSeconds));
                            lastSeq = r.Sequence;
                        }
                }
                return keys;
            }

            List<string> a = Run(7), b = Run(7);
            Assert.That(b, Is.EqualTo(a), "same seed + same inputs ⇒ same incidents at the same times");
            Assert.That(a.Count, Is.GreaterThanOrEqualTo(2), "a 30-minute session produces setbacks: " + string.Join(", ", a));
            var distinct = new HashSet<string>();
            for (ulong seed = 1; seed <= 6; seed++) distinct.Add(string.Join(",", Run(seed)));
            Assert.That(distinct.Count, Is.GreaterThan(1), "different seeds give different sequences");
        }

        [Test]
        public void EveryEvaluationOutcomeIsRecordedAndTheSameKeyNeverRepeatsBackToBack()
        {
            var h = new H(H.Variant(director: new DirectorRules(10, 5, 5, 1, 1.0, 0.0, 1)));
            h.JoinAndStart();
            h.Tick(900);
            string last = null;
            foreach (IncidentRecord r in h.Now.Incidents)
            {
                Assert.That(r.Key, Is.Not.EqualTo(last), "anti-repeat");
                last = r.Key;
            }
            Assert.That(h.Now.Decisions, Is.Not.Empty);
            Assert.That(h.Now.Director.Evaluations, Is.GreaterThan(50));
        }

        [Test]
        public void AConverterTripHalvesGenerationUntilAnInspectedReset()
        {
            var h = new H(H.Variant(new[] { H.Only(LivingBaseFixtures.TripKey) }, H.Eager()));
            PlayerId p = h.JoinAndStart();
            h.Until(() => h.OpenIncident(IncidentType.PowerConverterTrip) != null, 30);
            Assert.That(h.OpenIncident(IncidentType.PowerConverterTrip).Phase, Is.EqualTo(IncidentPhase.Warning));
            double nominal = h.Now.Power.EffectiveGenerationWatts;
            h.Until(() => h.OpenIncident(IncidentType.PowerConverterTrip).Phase == IncidentPhase.Active, 20);
            Assert.That(h.Now.Power.EffectiveGenerationWatts, Is.EqualTo(nominal * 0.5).Within(1e-9));
            Assert.That(h.Now.Power.Knowledge, Is.EqualTo(EquipmentKnowledge.Unknown), "the cause is not known without inspection");

            h.Walk(p, H.AtPower);
            Assert.That(h.Send(p, new IncidentCommand(Guid.NewGuid(), h.Now.Epoch, IncidentAction.ResetConverter, LivingBaseFixtures.PowerPanel,
                0, 1, h.Crew(p).ZoneSequence)).Reason, Is.EqualTo(IncidentReasons.ObservationRequired));
            h.RunPower(p, IncidentAction.InspectPower);
            Assert.That(h.Now.Power.Observation.Findings & PowerFindings.ConverterTripped, Is.EqualTo(PowerFindings.ConverterTripped));
            Assert.That(h.Now.Power.Knowledge, Is.EqualTo(EquipmentKnowledge.Degraded));
            h.RunPower(p, IncidentAction.ResetConverter);
            Assert.That(h.Now.Power.EffectiveGenerationWatts, Is.EqualTo(nominal).Within(1e-9));
            Assert.That(((IPowerCapacityModifiers)h.Base).PowerCapacity.Modifiers, Is.Empty);
            Assert.That(h.Now.Power.Knowledge, Is.EqualTo(EquipmentKnowledge.Unknown), "the state changed: the old observation is stale");
            h.Until(() => h.LatestIncident(IncidentType.PowerConverterTrip).Phase == IncidentPhase.Resolved, 30);
            Assert.That(h.LatestIncident(IncidentType.PowerConverterTrip).ResultKey, Is.EqualTo("reset"));
            Assert.That(h.Phase4.Outpost.State, Is.EqualTo(SessionState.Running), "resolving never ends the session");
            Assert.That(h.Now.Feedback[h.Now.Feedback.Count - 1].Assessment, Is.EqualTo(DecisionAssessment.Appropriate));
        }

        [Test]
        public void AControllerFaultNeedsIsolationAPartAndRestorationAndAResetIsNotIndicated()
        {
            var h = new H(H.Variant(new[] { H.Only(LivingBaseFixtures.ControllerKey) }, H.Eager()));
            PlayerId p = h.JoinAndStart();
            h.Until(() => h.OpenIncident(IncidentType.PowerControllerFault)?.Phase == IncidentPhase.Active, 40);
            double maxDischarge = h.Scenario.BaseOperations.Expedition.Outpost.Simulation.Power.MaxDischargeWatts;
            Assert.That(h.Now.Power.EffectiveMaxDischargeWatts, Is.EqualTo(maxDischarge * 0.4).Within(1e-9));

            h.Walk(p, H.AtPower);
            Assert.That(h.DoPower(p, IncidentAction.ReplacePowerPart).Reason, Is.EqualTo(IncidentReasons.PowerNotIsolated));
            h.RunPower(p, IncidentAction.InspectPower);
            Assert.That(h.Now.Power.Observation.Findings & PowerFindings.ControllerDamaged, Is.EqualTo(PowerFindings.ControllerDamaged));
            h.RunPower(p, IncidentAction.ResetConverter);
            IncidentFeedback reset = h.Now.Feedback[h.Now.Feedback.Count - 1];
            Assert.That(reset.Assessment, Is.EqualTo(DecisionAssessment.NotIndicated));
            Assert.That(h.Now.Power.EffectiveMaxDischargeWatts, Is.EqualTo(maxDischarge * 0.4).Within(1e-9), "a reset does not repair damage");

            Assert.That(h.DoPower(p, IncidentAction.IsolatePower).Accepted, "observation still current");
            Assert.That(h.Now.Power.Isolated, Is.True);
            Assert.That(h.Now.Power.EffectiveMaxDischargeWatts, Is.EqualTo(0));
            Assert.That(h.DoPower(p, IncidentAction.RestorePower).Reason, Is.EqualTo(IncidentReasons.PartNotReplaced));
            int relays = SpareCount(h, LivingBaseFixtures.PowerRelay);
            h.RunPower(p, IncidentAction.ReplacePowerPart);
            Assert.That(SpareCount(h, LivingBaseFixtures.PowerRelay), Is.EqualTo(relays - 1), "the part is a real spare");
            Assert.That(h.DoPower(p, IncidentAction.RestorePower).Accepted);
            Assert.That(h.Now.Power.EffectiveMaxDischargeWatts, Is.EqualTo(maxDischarge).Within(1e-9));
            h.Until(() => h.LatestIncident(IncidentType.PowerControllerFault).Phase == IncidentPhase.Resolved, 30);
        }

        [Test]
        public void ADirectorProcessorDegradationIsRepairedWithThePhase4Sequence()
        {
            var h = new H(H.Variant(new[] { H.Only(LivingBaseFixtures.ProcessorKey) }, H.Eager()));
            PlayerId p = h.JoinAndStart();
            h.Until(() => h.OpenIncident(IncidentType.ProcessorDegradation)?.Phase == IncidentPhase.Active, 60);
            Assert.That(h.Equipment.Snapshot.WaterProcessor.Condition, Is.EqualTo(EquipmentCondition.Degraded));
            h.Walk(p, H.AtWater);
            StationId water = h.Scenario.BaseOperations.WaterStation;
            foreach (BaseOperationAction action in new[] { BaseOperationAction.Inspect, BaseOperationAction.Isolate,
                         BaseOperationAction.Intervene, BaseOperationAction.TestEquipment, BaseOperationAction.Restore })
            {
                long observation = BaseOperationCommand.NeedsObservation(action)
                    ? h.Phase4.EquipmentOf(EquipmentKind.WaterProcessor).LatestObservation?.Sequence ?? 1 : BaseOperationCommand.Unstated;
                OperationChoice choice = action == BaseOperationAction.Intervene ? OperationChoice.ReplaceProcessorFilter : OperationChoice.None;
                CommandResult r = h.Send(p, BaseOperationCommand.Operation(Guid.NewGuid(), h.Now.Epoch, action, water, choice, observation));
                Assert.That(r.Accepted, action + ": " + r.Reason);
                h.Until(() => !h.Phase4.TryGetOperationAt(water, out _), 60);
            }
            h.Until(() => h.LatestIncident(IncidentType.ProcessorDegradation).Phase == IncidentPhase.Resolved, 30);
            Assert.That(h.LatestIncident(IncidentType.ProcessorDegradation).ResultKey, Is.EqualTo("repaired"));
        }

        [Test]
        public void IdsAreSharedAcrossPhasesAndDuplicatesReplayTheirResult()
        {
            var h = new H();
            PlayerId p = h.JoinAndStart();
            h.Walk(p, H.AtPower);
            IncidentCommand inspect = h.Power(p, IncidentAction.InspectPower);
            CommandResult first = h.Send(p, inspect);
            Assert.That(first.Accepted, first.Reason);
            Assert.That(h.Send(p, inspect), Is.SameAs(first), "same id, same content, same sender: same result");
            CommandResult other = h.Send(p, ExpeditionCommand.Session(inspect.Id, h.Now.Epoch, ExpeditionAction.Pause));
            Assert.That(other.Reason, Is.EqualTo(OutpostReasons.CommandIdConflict));
            Assert.That(h.Phase4.Outpost.State, Is.EqualTo(SessionState.Running));
            var restart = ExpeditionCommand.Session(Guid.NewGuid(), h.Now.Epoch, ExpeditionAction.Pause);
            Assert.That(h.Send(p, restart).Accepted);
            Assert.That(h.Send(p, new IncidentCommand(restart.Id, h.Now.Epoch, IncidentAction.InspectPower, LivingBaseFixtures.PowerPanel, 0, 0,
                h.Crew(p).ZoneSequence)).Reason, Is.EqualTo(OutpostReasons.CommandIdConflict));
        }

        [Test]
        public void BusyRulesHoldAcrossPhases()
        {
            var h = new H();
            PlayerId p = h.JoinAndStart();
            h.Walk(p, H.AtPower);
            Assert.That(h.DoPower(p, IncidentAction.InspectPower).Accepted);
            Assert.That(h.DoPower(p, IncidentAction.InspectPower).Reason, Is.EqualTo(OutpostReasons.PlayerBusy));
            CommandResult task = h.Send(p, ExpeditionCommand.StartTask(Guid.NewGuid(), h.Now.Epoch, ExpeditionFixtures.ShelterPanel,
                ExpeditionFixtures.PrepareShelter, h.Crew(p).ZoneSequence));
            Assert.That(task.Reason, Is.EqualTo(OutpostReasons.PlayerBusy), "a phase 5 operation keeps the member busy for phase 3 tasks");
            Assert.That(h.Send(p, h.AtStation(p, IncidentAction.CancelOperation, LivingBaseFixtures.PowerPanel)).Accepted);
            Assert.That(h.Now.Operations, Is.Empty);
        }

        [Test]
        public void WalkingAwayAbandonsAnOperationWithoutEffect()
        {
            var h = new H();
            PlayerId p = h.JoinAndStart();
            h.Walk(p, H.AtPower);
            Assert.That(h.DoPower(p, IncidentAction.InspectPower).Accepted);
            h.Walk(p, H.AtWater);
            Assert.That(h.Now.Operations, Is.Empty);
            Assert.That(h.Now.Power.Observation, Is.Null);
        }

        [Test]
        public void ARestartStartsANewEpochWithoutIncidentsOrModifiers()
        {
            var h = new H(H.Variant(new[] { H.Only(LivingBaseFixtures.TripKey) }, H.Eager()));
            PlayerId p = h.JoinAndStart();
            h.Until(() => h.OpenIncident(IncidentType.PowerConverterTrip)?.Phase == IncidentPhase.Active, 40);
            long epoch = h.Now.Epoch;
            Assert.That(h.Session(p, ExpeditionAction.Restart).Accepted);
            Assert.That(h.Now.Epoch, Is.EqualTo(epoch + 1));
            Assert.That(h.Now.Incidents, Is.Empty);
            Assert.That(((IPowerCapacityModifiers)h.Base).PowerCapacity.Modifiers, Is.Empty);
            Assert.That(h.Now.Setup.Seed, Is.EqualTo(42UL), "the setup is kept");
        }

        [Test]
        public void TheSnapshotNeverPublishesTheHiddenSchedule()
        {
            var h = new H(H.Variant(new[] { H.Only(LivingBaseFixtures.TripKey) }, H.Eager()));
            h.JoinAndStart();
            h.Until(() => h.OpenIncident(IncidentType.PowerConverterTrip) != null, 30);
            IncidentRecord warning = h.OpenIncident(IncidentType.PowerConverterTrip);
            Assert.That(warning.ActiveAtSeconds, Is.Null, "the planned activation time is not published");
            foreach (LivingBaseSnapshot s in h.Published)
                foreach (IncidentRecord r in s.Incidents)
                    Assert.That(r.AnnouncedAtSeconds, Is.LessThanOrEqualTo(s.SimulatedTimeSeconds + 1e-6));
        }

        private static int SpareCount(H h, string key)
        {
            Assert.That(h.Equipment.Snapshot.TryGetSpare(key, out SpareStock s), Is.True);
            return s.Count;
        }
    }
}
