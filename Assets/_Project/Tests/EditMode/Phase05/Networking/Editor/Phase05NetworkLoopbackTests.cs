using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Crew;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Incidents;
using Trainer.Application.Outpost;
using Trainer.Unity.Networking;
using Trainer.Unity.Networking.Incidents;
using H = Trainer.Tests.Phase05.Application.Phase05LivingBaseHarness;

namespace Trainer.Tests.Phase05.Networking
{
    /// <summary>
    /// D3 phase 5 networking end to end IN MEMORY with the REAL D2 <see cref="LivingBaseAuthority"/>: 'T' 'L' bridge, cable, client
    /// adapter/controller and the interaction client over the in-memory transport. Proves identity, full state for late joiners,
    /// client state identical to the authority's, stamps, results only to their sender, Leave once, honest abandonment and that the
    /// space-weather records a client sees are exactly those the authority published. No sockets (see the native two-client run).
    /// </summary>
    public sealed class Phase05NetworkLoopbackTests
    {
        private sealed class Rig : IDisposable
        {
            public readonly H Harness;
            public readonly InMemoryOutpostNetwork Net = new InMemoryOutpostNetwork();
            public readonly LivingBaseCommandBridge Bridge;
            public readonly List<string> HostErrors = new List<string>();
            private readonly List<IDisposable> owned = new List<IDisposable>();

            public Rig(H harness)
            {
                Harness = harness;
                Bridge = new LivingBaseCommandBridge(harness.Authority, Net.HostTransport);
                Bridge.ProtocolError += (c, code) => HostErrors.Add(code);
            }

            public LivingBaseConnectionController Connected()
            {
                var c = new LivingBaseConnectionController(Net.CreateClient());
                owned.Add(c);
                c.Connect();
                Pump();
                Assert.That(c.State, Is.EqualTo(SessionConnectionState.Connected));
                return c;
            }

            public void Own(IDisposable d) => owned.Add(d);

            public void Pump()
            {
                for (int i = 0; i < 3; i++)
                {
                    Bridge.FlushSnapshots();
                    Net.Pump();
                }
            }

            public void Step(double seconds, double dt = 0.25)
            {
                for (double left = seconds; left > 1e-9; left -= dt)
                {
                    Harness.Authority.Tick(Math.Min(dt, left));
                    Pump();
                }
            }

            public void Dispose()
            {
                foreach (IDisposable d in owned) d.Dispose();
                Bridge.Dispose();
            }
        }

        private static H PowerHarness() =>
            new H(H.Variant(new[] { H.Only(LivingBaseFixtures.TripKey) }, H.Eager(rest: 600)));

        /// <summary>Public structural fingerprint of a phase 5 snapshot (byte-level identity is proven by the cable tests in
        /// Tests/PlayMode/Networking/Phase05, which see the internal codec).</summary>
        public static string Fingerprint(LivingBaseSnapshot s)
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

        private static void AssertSameAsAuthority(Rig rig, ILivingBaseSessionPort port)
        {
            Assert.That(rig.Harness.Authority.TryGetSnapshot(out LivingBaseSnapshot truth), Is.True);
            Assert.That(port.TryGetLivingBase(out LivingBaseSnapshot seen), Is.True);
            Assert.That(Fingerprint(seen), Is.EqualTo(Fingerprint(truth)), "the client holds exactly the authority's state");
        }

        [Test]
        public void TwoClientsGetDistinctIdentitiesAndALateJoinerGetsTheCurrentIncident()
        {
            using (var rig = new Rig(PowerHarness()))
            {
                LivingBaseConnectionController first = rig.Connected();
                PlayerId a = first.Session.Membership.Player;
                Assert.That(rig.Harness.Session(a, ExpeditionAction.Resume).Accepted);
                rig.Step(20);
                Assert.That(rig.Harness.OpenIncident(IncidentType.PowerConverterTrip), Is.Not.Null, "the director opened the power fault");
                LivingBaseConnectionController late = rig.Connected();
                Assert.That(late.Session.Membership.Player, Is.Not.EqualTo(a));
                Assert.That(late.Session.TryGetLivingBase(out LivingBaseSnapshot s), Is.True);
                Assert.That(s.Incidents.Any(i => i.IsOpen && i.Kind == IncidentType.PowerConverterTrip), "current state, not initial values");
                AssertSameAsAuthority(rig, late.Session);
                AssertSameAsAuthority(rig, first.Session);
            }
        }

        [Test]
        public void PowerRepairThroughTheCableStampsTheCurrentObservationAndAnswersOnlyTheSender()
        {
            using (var rig = new Rig(PowerHarness()))
            {
                LivingBaseConnectionController c1 = rig.Connected(), c2 = rig.Connected();
                PlayerId p1 = c1.Session.Membership.Player;
                var intents = new LivingBaseInteractionClient(c1.Session, rig.Harness.Scenario);
                rig.Own(intents);
                var answers = new List<string>();
                intents.Answered += answers.Add;
                var foreign = new List<CommandResult>();
                c2.Session.CommandCompleted += foreign.Add;
                Assert.That(rig.Harness.Session(p1, ExpeditionAction.Resume).Accepted);
                rig.Step(20);
                Assert.That(rig.Harness.OpenIncident(IncidentType.PowerConverterTrip), Is.Not.Null);
                rig.Harness.Walk(p1, H.AtPower);
                rig.Pump();

                Assert.That(intents.RequestPower(IncidentAction.ResetConverter), Is.False, "no current observation yet: nothing is sent");
                Assert.That(intents.RequestPower(IncidentAction.InspectPower), Is.True);
                rig.Pump();
                Assert.That(answers.Last(), Is.EqualTo(OutpostReasons.Accepted));
                rig.Step(rig.Harness.Scenario.PowerOperations.InspectSeconds + 1);
                Assert.That(intents.TryGetCurrentObservation(out PowerObservation seen), Is.True);
                Assert.That(intents.RequestPower(IncidentAction.ResetConverter), Is.True);
                Assert.That(intents.Pending.ObservationSequence, Is.EqualTo(seen.Sequence), "stamped from the confirmed snapshot");
                rig.Pump();
                Assert.That(answers.Count, Is.EqualTo(2));
                Assert.That(foreign, Is.Empty, "results go only to their sender");
                AssertSameAsAuthority(rig, c2.Session);
            }
        }

        [Test]
        public void DisconnectLeavesOnceAndAbandonsThePendingRequest()
        {
            using (var rig = new Rig(PowerHarness()))
            {
                LivingBaseConnectionController c = rig.Connected();
                PlayerId p = c.Session.Membership.Player;
                Assert.That(rig.Harness.Session(p, ExpeditionAction.Resume).Accepted);
                rig.Step(2);
                var abandoned = new List<string>();
                c.Session.IncidentCommandAbandoned += (cmd, reason) => abandoned.Add(reason);
                rig.Harness.Authority.TryGetSnapshot(out LivingBaseSnapshot now);
                var command = new IncidentCommand(Guid.NewGuid(), now.Epoch, IncidentAction.InspectPower, LivingBaseFixtures.PowerPanel, 0, 0,
                    rig.Harness.Crew(p).ZoneSequence);
                c.Session.Submit(command); // sent, not yet delivered
                c.Disconnect();
                Assert.That(abandoned, Is.EqualTo(new[] { OutpostReasons.LocalDisconnect }), "unknown outcome, never a success");
                rig.Pump();
                Assert.That(rig.Bridge.LeaveCount, Is.EqualTo(1));
                c.Session.Submit(command);
                Assert.That(abandoned.Last(), Is.EqualTo(OutpostReasons.NotConnected));
            }
        }

        [Test]
        public void ARemoteClientSeesOnlyTheSpaceWeatherRecordsTheAuthorityPublished()
        {
            var h = new H(H.Variant(new[] { H.Only(LivingBaseFixtures.WeatherKey) }, H.Eager(rest: 600), historical: LivingBaseFixtures.FixtureHistoricalEpisode()),
                new LivingBaseSetup(DataMode.Historical, 7));
            using (var rig = new Rig(h))
            {
                LivingBaseConnectionController c = rig.Connected();
                PlayerId p = c.Session.Membership.Player;
                Assert.That(h.Session(p, ExpeditionAction.Resume).Accepted);
                int checkedSnapshots = 0;
                c.Session.LivingBaseChanged += s =>
                {
                    if (s.SpaceWeather == null) return;
                    checkedSnapshots++;
                    var quoted = new HashSet<string>(s.SpaceWeather.Issued.SelectMany(i => i.Message.SourceRecordIds));
                    Assert.That(s.SpaceWeather.VisibleRecords.All(r => quoted.Contains(r.SourceId)), "no record ahead of its message");
                };
                for (int i = 0; i < 400 && !(c.Session.TryGetLivingBase(out LivingBaseSnapshot s) && s.SpaceWeather != null
                    && s.SpaceWeather.Instruction.State == InstructionState.Ended); i++)
                    rig.Step(1);
                Assert.That(checkedSnapshots, Is.GreaterThan(5));
                AssertSameAsAuthority(rig, c.Session);
            }
        }
    }
}
