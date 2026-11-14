// Compiled out on purpose: see PhaseOneScenarioValidatorTests.cs. Without a PlayMode test assembly this
// file would land in Assembly-CSharp (no NUnit) and break player builds. Integration enables it with test
// .asmdef files plus TRAINER_PHASE1_VALIDATION_TESTS, then replaces the pending factories below with the
// real authority/clients. Cases that need components from other branches are Ignore'd, never faked.
#if TRAINER_PHASE1_VALIDATION_TESTS && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Simulation;
using Trainer.Tooling.Validation;

namespace Trainer.Tests.Integration
{
    /// <summary>Integration matrix over ISessionPort. Fake rows prove only the shared contract/fake
    /// (single thread, synchronous, no networking, no permissions).</summary>
    public sealed class PhaseOneIntegrationTests
    {
        // ---- Injection points for the authorized integration. Keep null until the real pieces exist. ----
        /// <summary>Creates N client ports connected to ONE authority (dev 3). Null = pending.</summary>
        public static Func<int, IReadOnlyList<ISessionPort>> ConnectedClientsFactory = null;
        /// <summary>Real simulation (dev 1). Null = pending.</summary>
        public static Func<IPowerSimulation> PowerSimulationFactory = null;

        private sealed class Probe
        {
            public readonly ISessionPort Port;
            public readonly List<MissionSnapshot> Snapshots = new List<MissionSnapshot>();
            public readonly List<CommandResult> Results = new List<CommandResult>();

            public Probe(ISessionPort port)
            {
                Port = port;
                port.SnapshotChanged += Snapshots.Add;
                port.CommandCompleted += Results.Add;
            }

            public MissionSnapshot Current
            {
                get { Assert.That(Port.TryGetSnapshot(out MissionSnapshot s), Is.True); return s; }
            }

            public CommandResult Send(SessionCommand command)
            {
                int before = Results.Count;
                Port.Submit(command);
                Assert.That(Results.Count, Is.EqualTo(before + 1), "synchronous fake expected exactly one result");
                return Results[Results.Count - 1];
            }

            public CommandResult Send(SessionAction action, bool? value = null)
            {
                return Send(new SessionCommand(Guid.NewGuid(), Current.Epoch, action, value));
            }
        }

        // ------------------------------------------------------------ executed against the shared fake

        [Test] public void Fake_RepeatedRequestIsAppliedOnce()
        {
            var probe = new Probe(new FakeSessionPort());
            var command = new SessionCommand(Guid.NewGuid(), 0, SessionAction.SetScienceEnabled, true);
            CommandResult first = probe.Send(command);
            CommandResult replay = probe.Send(new SessionCommand(command.Id, 0, SessionAction.SetScienceEnabled, true));
            Assert.That(PhaseOneScenarioValidator.ValidateReplay(first, replay).HasErrors, Is.False);
            Assert.That(probe.Snapshots.Count, Is.EqualTo(1));
            Assert.That(probe.Current.Revision, Is.EqualTo(1));
        }

        [Test] public void Fake_OldEpochHasNoEffectAfterRestart()
        {
            var probe = new Probe(new FakeSessionPort());
            Assert.That(probe.Send(SessionAction.Restart).Accepted, Is.True);
            MissionSnapshot fresh = probe.Current;
            CommandResult stale = probe.Send(new SessionCommand(Guid.NewGuid(), 0, SessionAction.SetScienceEnabled, true));
            Assert.That(stale.Reason, Is.EqualTo("EpochMismatch"));
            Assert.That(probe.Current, Is.SameAs(fresh));
        }

        [Test] public void Fake_PauseResumeAndRestartStreamIsValid()
        {
            var probe = new Probe(new FakeSessionPort());
            MissionSnapshot previous = probe.Current;
            probe.Send(SessionAction.Resume);
            probe.Send(SessionAction.SetScienceEnabled, true);
            probe.Send(SessionAction.Pause);
            Assert.That(probe.Send(SessionAction.Pause).Reason, Is.EqualTo("InvalidState"));
            probe.Send(SessionAction.Resume);
            probe.Send(SessionAction.Restart);
            foreach (MissionSnapshot next in probe.Snapshots)
            {
                ValidationReport report = PhaseOneScenarioValidator.ValidateMissionTransition(previous, next);
                Assert.That(report.HasErrors || report.Contains("RestartNotClean"), Is.False, report.ToString());
                previous = next;
            }
            Assert.That(probe.Current.Epoch, Is.EqualTo(1));
            Assert.That(probe.Current.ScienceWorkSeconds, Is.EqualTo(0));
        }

        // ------------------------------------------------------------ pending until integration

        [Test] public void Real_OneAuthorityPublishesSameStateToTwoClients()
        {
            if (ConnectedClientsFactory == null) Assert.Ignore("Pending: needs session authority + network adapter (dev 3) and an approved backend.");
            IReadOnlyList<ISessionPort> clients = ConnectedClientsFactory(2);
            // Integration: wait for both clients' first snapshot (async transport), submit from client 0,
            // then compare Epoch/Revision/State/Power/ScienceWorkSeconds on both clients.
            Assert.Inconclusive("Write the async wait once the transport exists; " + clients.Count + " clients created.");
        }

        [Test] public void Real_DuplicateFromTwoClientsIsAppliedOnce()
        {
            if (ConnectedClientsFactory == null) Assert.Ignore("Pending: needs real authority; identity/permissions must not come from the payload.");
            Assert.Inconclusive("Submit the same command ID from both clients and assert a single state change.");
        }

        [Test] public void Real_PowerSimulationRespectsEnergyBounds()
        {
            if (PowerSimulationFactory == null) Assert.Ignore("Pending: needs IPowerSimulation implementation (dev 1).");
            var config = new PowerConfiguration(18000, 36000, 50, 60, 20, 40, 40); // SYNTHETIC
            IPowerSimulation simulation = PowerSimulationFactory();
            simulation.Reset(config);
            PowerSnapshot previous = simulation.Snapshot;
            Assert.That(PhaseOneScenarioValidator.ValidatePowerSnapshot(previous, config).HasErrors, Is.False);
            for (int step = 0; step < 120; step++)
            {
                simulation.Advance(1.0, step >= 60);
                PowerSnapshot next = simulation.Snapshot;
                Assert.That(PhaseOneScenarioValidator.ValidatePowerSnapshot(next, config).HasErrors, Is.False, "step " + step);
                Assert.That(PhaseOneScenarioValidator.ValidatePowerTransition(previous, next, config).HasErrors, Is.False, "step " + step);
                previous = next;
            }
            // Lossless expectation for this fixture (computed independently): 15600 J at t = 120 s.
            Assert.That(previous.SimulatedTimeSeconds, Is.EqualTo(120).Within(1e-9));
            Assert.That(previous.EnergyJoules, Is.EqualTo(15600).Within(1e-6),
                "If the model declares losses or another integration rule, agree the expectation with dev 1 first.");
        }

        [Test] public void Real_SharedPauseStopsClock() { Assert.Ignore("Pending: fake never advances time; needs authority + simulation."); }
        [Test] public void Real_ObjectiveCountsOnlyPoweredWork() { Assert.Ignore("Pending: needs mission evaluation (dev 2)."); }
        [Test] public void Real_ConnectionFailureMessages() { Assert.Ignore("Pending: needs network adapter (dev 3) and UI (dev 4)."); }
        [Test] public void Real_BuildRunsTwoInstances() { Assert.Ignore("Pending: needs integrated scene in Build Settings and chosen topology."); }
    }
}
#endif
