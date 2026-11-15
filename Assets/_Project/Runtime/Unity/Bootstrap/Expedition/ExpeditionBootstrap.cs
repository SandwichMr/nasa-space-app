using System;
using Trainer.Application.Crew;
using Trainer.Application.Expedition;
using Trainer.Simulation.Eva;
using Trainer.Simulation.LifeSupport;
using Trainer.Unity.Configuration;
using UnityEngine;

namespace Trainer.Unity.Bootstrap
{
    /// <summary>
    /// Authority-side composition root of the phase 3 mission (D2). Creates the SINGLE expedition authority with D1's
    /// base model and EVA model and D3's roster (the same phase 2 <see cref="CrewRoster"/>, which implements
    /// <see cref="IZonedCrewRoster"/>), all from the same scenario, and binds the single runner. Only the authority process
    /// composes it; clients get an <see cref="IExpeditionSessionPort"/> from D3's network client.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionBootstrap : MonoBehaviour
    {
        [SerializeField] private ExpeditionScenarioDefinition scenario;
        [SerializeField] private ExpeditionRunner runner;

        public ExpeditionScenarioDefinition ScenarioDefinition => scenario;
        public ExpeditionAuthority Authority { get; private set; }
        public event Action<ExpeditionAuthority> Composed;
        public event Action<ExpeditionAuthority> ShutDown;

        public ExpeditionAuthority Compose()
        {
            if (Authority != null) throw new InvalidOperationException("Expedition already composed.");
            if (scenario == null) throw new InvalidOperationException("ExpeditionScenarioDefinition not assigned.");
            if (runner == null) throw new InvalidOperationException("ExpeditionRunner not assigned.");
            ExpeditionAuthority authority = Create(scenario);
            runner.Bind(authority, scenario.TimeScale * TimeScaleOverride());
            Authority = authority;
            Composed?.Invoke(authority);
            return authority;
        }

        public void Shutdown()
        {
            if (runner != null) runner.Unbind();
            ExpeditionAuthority released = Authority;
            Authority = null;
            if (released != null) ShutDown?.Invoke(released);
        }

        /// <summary>The production composition (also used by tests): D1 base model with the phase 2 synthetic training
        /// parameters, D1 EVA model, D3 roster, all built from <paramref name="definition"/>.</summary>
        public static ExpeditionAuthority Create(ExpeditionScenarioDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            string problem = definition.Validate();
            if (problem != null) throw new InvalidOperationException($"ExpeditionScenarioDefinition '{definition.name}': {problem}");
            ExpeditionScenario built = definition.ToScenario();
            return new ExpeditionAuthority(built, new OutpostBaseSimulation(OutpostSimulationParameters.SyntheticTraining()),
                new EvaSuitSimulation(), CrewRoster.For(built.Outpost), definition.MaxStepSeconds);
        }

        /// <summary>Optional -trainer-timescale N (authority only) for faster unattended acceptance runs; 1 otherwise.</summary>
        private static double TimeScaleOverride()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-trainer-timescale" && double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double value) && value > 0 && value <= 8)
                    return value;
            return 1d;
        }

        private void OnDestroy() => Shutdown();
    }
}
