using System;
using Trainer.Application;
using Trainer.Simulation;
using Trainer.Unity.Configuration;
using UnityEngine;

namespace Trainer.Unity.Bootstrap
{
    /// <summary>
    /// Authority-side composition root. NOT wired to any scene yet and does not compose itself:
    /// the IPowerSimulation implementation (D1) is supplied by the later integration step via
    /// Compose. Clients do not use this component; they obtain an ISessionPort from the network adapter.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionBootstrap : MonoBehaviour
    {
        [SerializeField] private ScenarioDefinition scenario;
        [SerializeField] private SimulationRunner runner;

        /// <summary>The authoritative session, or null before Compose / after Shutdown.</summary>
        public MissionSession Session { get; private set; }

        /// <summary>Port for authority-local consumers (network adapter, host UI).</summary>
        public ISessionPort Port => Session;

        /// <summary>Raised once per successful Compose.</summary>
        public event Action<MissionSession> Composed;

        /// <summary>Creates the single authoritative session and starts its clock.</summary>
        public MissionSession Compose(IPowerSimulation simulation)
        {
            if (simulation == null) throw new ArgumentNullException(nameof(simulation));
            if (Session != null) throw new InvalidOperationException("Mission already composed.");
            if (scenario == null) throw new InvalidOperationException("ScenarioDefinition not assigned.");
            if (runner == null) throw new InvalidOperationException("SimulationRunner not assigned.");

            MissionSession session = Create(scenario, simulation);
            runner.Bind(session, scenario.TimeScale);
            Session = session;
            Composed?.Invoke(session);
            return session;
        }

        /// <summary>Stops the clock and releases the session.</summary>
        public void Shutdown()
        {
            if (runner != null) runner.Unbind();
            Session = null;
        }

        /// <summary>Composition without runner or scene, usable from tests or another composition root.</summary>
        public static MissionSession Create(ScenarioDefinition definition, IPowerSimulation simulation)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return definition.CreateSession(simulation);
        }

        private void OnDestroy() => Shutdown();
    }
}
