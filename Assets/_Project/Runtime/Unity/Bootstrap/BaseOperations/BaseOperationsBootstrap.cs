using System;
using Trainer.Application.BaseOperations;
using Trainer.Application.Crew;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.Eva;
using Trainer.Simulation.LifeSupport;
using Trainer.Unity.Configuration;
using UnityEngine;

namespace Trainer.Unity.Bootstrap.BaseOperations
{
    /// <summary>
    /// Authority-side composition root of the phase 4 living base (D2). Creates the SINGLE continuous authority over D1's
    /// base, EVA and equipment models and D3's roster, all from the same scenario, and binds the single runner. Only the
    /// authority process composes it; clients get an <see cref="IBaseOperationsSessionPort"/> from D3's network client.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BaseOperationsBootstrap : MonoBehaviour
    {
        [SerializeField] private BaseOperationsScenarioDefinition scenario;
        [SerializeField] private BaseOperationsRunner runner;

        public BaseOperationsScenarioDefinition ScenarioDefinition => scenario;
        public BaseOperationsAuthority Authority { get; private set; }
        public event Action<BaseOperationsAuthority> Composed;
        public event Action<BaseOperationsAuthority> ShutDown;

        public BaseOperationsAuthority Compose()
        {
            if (Authority != null) throw new InvalidOperationException("Living base already composed.");
            if (scenario == null) throw new InvalidOperationException("BaseOperationsScenarioDefinition not assigned.");
            if (runner == null) throw new InvalidOperationException("BaseOperationsRunner not assigned.");
            BaseOperationsAuthority authority = Create(scenario);
            runner.Bind(authority, scenario.TimeScale * TimeScaleOverride());
            Authority = authority;
            Composed?.Invoke(authority);
            return authority;
        }

        public void Shutdown()
        {
            if (runner != null) runner.Unbind();
            BaseOperationsAuthority released = Authority;
            Authority = null;
            if (released != null) ShutDown?.Invoke(released);
        }

        /// <summary>The production composition (also used by tests): D1 base model (phase 2 synthetic training parameters),
        /// D1 EVA and equipment models, D3 roster, all from <paramref name="definition"/>, continuous mode.</summary>
        public static BaseOperationsAuthority Create(BaseOperationsScenarioDefinition definition, BaseSessionMode mode = BaseSessionMode.Continuous)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            string problem = definition.Validate();
            if (problem != null) throw new InvalidOperationException($"BaseOperationsScenarioDefinition '{definition.name}': {problem}");
            BaseOperationsScenario built = definition.ToScenario();
            return new BaseOperationsAuthority(built, new OutpostBaseSimulation(OutpostSimulationParameters.SyntheticTraining()),
                new BaseEquipmentSimulation(), new EvaSuitSimulation(), CrewRoster.For(built.Expedition.Outpost), mode, definition.MaxStepSeconds);
        }

        /// <summary>Optional -trainer-timescale N (authority only, 0 &lt; N ≤ 8) for unattended acceptance runs; 1 otherwise.</summary>
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
