using System;
using Trainer.Application.Outpost;
using Trainer.Simulation.LifeSupport;
using Trainer.Unity.Configuration;
using UnityEngine;

namespace Trainer.Unity.Bootstrap
{
    /// <summary>
    /// Authority-side composition root of the phase 2 interior mission. It does not choose implementations:
    /// the scene composition passes factories for D1's <see cref="IOutpostSimulation"/> and D3's
    /// <see cref="ICrewRoster"/> (both built from the same <see cref="OutpostScenario"/>), only in the authority
    /// process. Clients never compose it; they get an <see cref="IOutpostSessionPort"/> from D3's network client.
    /// D3's host bridge follows <see cref="Composed"/>/<see cref="ShutDown"/> and uses <see cref="Authority"/>.
    ///
    /// Integration status: D1 and D3 implementations are not in this branch. Until they arrive, only tests
    /// compose this with test doubles; no fake is wired for production.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OutpostBootstrap : MonoBehaviour
    {
        [SerializeField] private OutpostScenarioDefinition scenario;
        [SerializeField] private OutpostRunner runner;

        public OutpostScenarioDefinition ScenarioDefinition => scenario;

        /// <summary>The single authority, or null before Compose / after Shutdown.</summary>
        public OutpostAuthority Authority { get; private set; }

        /// <summary>Raised once per successful Compose.</summary>
        public event Action<OutpostAuthority> Composed;

        /// <summary>Raised once when a composed authority is released; holders must drop it (no longer ticked).</summary>
        public event Action<OutpostAuthority> ShutDown;

        /// <summary>Creates the single authority and starts its clock.</summary>
        public OutpostAuthority Compose(Func<OutpostScenario, IOutpostSimulation> createSimulation,
            Func<OutpostScenario, ICrewRoster> createRoster)
        {
            if (Authority != null) throw new InvalidOperationException("Outpost mission already composed.");
            if (scenario == null) throw new InvalidOperationException("OutpostScenarioDefinition not assigned.");
            if (runner == null) throw new InvalidOperationException("OutpostRunner not assigned.");

            OutpostAuthority authority = Create(scenario, createSimulation, createRoster);
            runner.Bind(authority, scenario.TimeScale);
            Authority = authority;
            Composed?.Invoke(authority);
            return authority;
        }

        /// <summary>Stops the clock and releases the authority. Safe to call more than once.</summary>
        public void Shutdown()
        {
            if (runner != null) runner.Unbind();
            OutpostAuthority released = Authority;
            Authority = null;
            if (released != null) ShutDown?.Invoke(released);
        }

        /// <summary>Composition without runner or scene, for tests or another composition root. Each factory is
        /// called once with the scenario built from <paramref name="definition"/>.</summary>
        public static OutpostAuthority Create(OutpostScenarioDefinition definition,
            Func<OutpostScenario, IOutpostSimulation> createSimulation, Func<OutpostScenario, ICrewRoster> createRoster)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (createSimulation == null) throw new ArgumentNullException(nameof(createSimulation));
            if (createRoster == null) throw new ArgumentNullException(nameof(createRoster));
            string problem = definition.Validate();
            if (problem != null) throw new InvalidOperationException($"OutpostScenarioDefinition '{definition.name}': {problem}");
            OutpostScenario built = definition.ToScenario();
            IOutpostSimulation simulation = createSimulation(built)
                ?? throw new InvalidOperationException("The simulation factory returned null.");
            ICrewRoster roster = createRoster(built)
                ?? throw new InvalidOperationException("The roster factory returned null.");
            return definition.CreateAuthority(built, simulation, roster);
        }

        private void OnDestroy() => Shutdown();
    }
}
