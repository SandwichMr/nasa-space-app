// No test asmdef exists, so this file compiles into Assembly-CSharp (it needs the internal 'T' 'L' codec); the guard keeps it out
// of player builds. Pure byte-level tests of D3's phase 5 cable over states produced by the REAL D2 authority: no transport, socket
// or process.
#if UNITY_INCLUDE_TESTS
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
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.Eva;
using Trainer.Simulation.LifeSupport;
using Trainer.Unity.Networking;
using Trainer.Unity.Networking.BaseOperations;
using Trainer.Unity.Networking.Incidents;
using Trainer.Unity.Networking.Outpost;

namespace Trainer.Tests.Phase05.Networking
{
    public sealed class Phase05NetworkWireTests
    {
        private static LivingBaseAuthority Authority(LivingBaseSetup setup, out PlayerId player)
        {
            LivingBaseScenario scenario = LivingBaseFixtures.Scenario();
            var authority = new LivingBaseAuthority(scenario, setup, new OutpostBaseSimulation(OutpostSimulationParameters.SyntheticTraining()),
                new BaseEquipmentSimulation(), new EvaSuitSimulation(), CrewRoster.For(scenario.BaseOperations.Expedition.Outpost));
            OutpostMembership m = authority.Join();
            Assert.That(m.IsJoined);
            player = m.Player;
            Assert.That(authority.TryGetSnapshot(out LivingBaseSnapshot s));
            authority.Submit(player, ExpeditionCommand.Session(Guid.NewGuid(), s.Epoch, ExpeditionAction.Resume));
            return authority;
        }

        private static LivingBaseSnapshot Now(LivingBaseAuthority a)
        {
            Assert.That(a.TryGetSnapshot(out LivingBaseSnapshot s));
            return s;
        }

        private static void RoundTrip(LivingBaseSnapshot s)
        {
            byte[] message = LivingBaseWireProtocol.EncodeSnapshot(s);
            Assert.That(LivingBaseWireProtocol.TryDecodeFromHost(message, out LivingBaseHostMessage decoded, out string e), Is.True, e);
            Assert.That(LivingBaseWireProtocol.EncodeSnapshot(decoded.Snapshot), Is.EqualTo(message), "decode ∘ encode is the identity");
        }

        [Test]
        public void EverySnapshotOfAnHourOfSyntheticPlayRoundTripsExactly()
        {
            LivingBaseAuthority a = Authority(LivingBaseFixtures.SyntheticSetup(), out _);
            int largest = 0;
            for (int i = 0; i < 3600; i++)
            {
                a.Tick(1);
                if (i % 15 != 0) continue;
                LivingBaseSnapshot s = Now(a);
                RoundTrip(s);
                largest = Math.Max(largest, LivingBaseWireProtocol.EncodeSnapshot(s).Length);
            }
            Assert.That(Now(a).Incidents.Count, Is.GreaterThan(0), "the director produced incidents within the hour");
            Assert.That(largest, Is.LessThan(LivingBaseWireProtocol.MaxMessageBytes));
            Console.WriteLine("largest phase 5 snapshot: " + largest + " bytes");
        }

        [Test]
        public void HistoricalSnapshotsRoundTripWithProvenance()
        {
            LivingBaseAuthority a = Authority(new LivingBaseSetup(DataMode.Historical, 11), out _);
            bool weather = false;
            for (int i = 0; i < 3600 && !weather; i++)
            {
                a.Tick(1);
                LivingBaseSnapshot s = Now(a);
                if (s.SpaceWeather == null || s.SpaceWeather.Issued.Count < 2) continue;
                weather = true;
                RoundTrip(s);
            }
            Assert.That(Now(a).Data.Availability, Is.Not.EqualTo(DataAvailability.NotRequested));
            Assert.That(weather || Now(a).Incidents.Count > 0, "the session ran");
        }

        [Test]
        public void IncidentCommandsRoundTripAndForeignOrBrokenMessagesAreRejected()
        {
            var command = new IncidentCommand(Guid.NewGuid(), 1, IncidentAction.InspectPower, LivingBaseFixtures.PowerPanel, 0, 0, 3);
            byte[] good = LivingBaseWireProtocol.EncodeIncident(command);
            Assert.That(LivingBaseWireProtocol.TryDecodeFromClient(good, out LivingBaseClientMessage m, out string e), Is.True, e);
            Assert.That(m.Incident.HasSameContent(command), Is.True);

            Assert.That(LivingBaseWireProtocol.TryDecodeFromClient(BaseOperationsWireProtocol.EncodeJoin(), out _, out e), Is.False, "'T' 'B' is foreign");
            Assert.That(e, Is.EqualTo(SessionNetworkCodes.BadMagic));
            Assert.That(BaseOperationsWireProtocol.TryDecodeFromClient(LivingBaseWireProtocol.EncodeJoin(), out _, out _), Is.False, "'T' 'L' is foreign to phase 4");
            Assert.That(LivingBaseWireProtocol.TryDecodeFromClient(good.Take(good.Length - 1).ToArray(), out _, out _), Is.False, "short");
            Assert.That(LivingBaseWireProtocol.TryDecodeFromClient(good.Concat(new byte[] { 0 }).ToArray(), out _, out _), Is.False, "trailing");
            byte[] unknown = (byte[])good.Clone();
            unknown[3] = 77;
            Assert.That(LivingBaseWireProtocol.TryDecodeFromClient(unknown, out _, out e), Is.False);
            Assert.That(e, Is.EqualTo(SessionNetworkCodes.UnknownMessageKind));
            Assert.That(LivingBaseWireProtocol.TryDecodeFromHost(good, out _, out e), Is.False, "a client kind is unexpected from the host");

            byte[] join = LivingBaseWireProtocol.EncodeJoin();
            Assert.That(LivingBaseWireProtocol.TryDecodeFromClient(join, out LivingBaseClientMessage j, out e), Is.True, e);
            Assert.That(j.Kind, Is.EqualTo(OutpostMessageKind.Join));
        }

        [Test]
        public void TruncatedSnapshotsAreNeverPublishedAndFramesReassemble()
        {
            LivingBaseAuthority a = Authority(LivingBaseFixtures.SyntheticSetup(), out _);
            a.Tick(120);
            byte[] message = LivingBaseWireProtocol.EncodeSnapshot(Now(a));
            for (int cut = message.Length - 1; cut > 8; cut -= Math.Max(1, message.Length / 50))
                Assert.That(LivingBaseWireProtocol.TryDecodeFromHost(message.Take(cut).ToArray(), out _, out _), Is.False, "cut at " + cut);
            List<byte[]> frames = LivingBaseWireProtocol.Frame(message);
            var assembler = new LivingBaseWireProtocol.Assembler();
            byte[] whole = null;
            foreach (byte[] f in frames) assembler.TryAccept(f, out whole, out _);
            Assert.That(whole, Is.EqualTo(message));
            if (frames.Count > 2)
            {
                var gap = new LivingBaseWireProtocol.Assembler();
                gap.TryAccept(frames[0], out _, out _);
                Assert.That(gap.TryAccept(frames[2], out byte[] none, out string error), Is.False);
                Assert.That(none, Is.Null);
                Assert.That(error, Is.EqualTo(SessionNetworkCodes.MalformedPayload));
            }
        }
    }
}
#endif
