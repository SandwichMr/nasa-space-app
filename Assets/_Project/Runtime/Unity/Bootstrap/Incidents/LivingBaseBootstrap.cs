using System;
using System.Globalization;
using Trainer.Application.Crew;
using Trainer.Application.Incidents;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.Eva;
using Trainer.Simulation.LifeSupport;
using Trainer.Unity.Configuration;
using Trainer.Unity.Integration.SpaceWeather;
using UnityEngine;

namespace Trainer.Unity.Bootstrap.Incidents
{
    /// <summary>
    /// Authority-side composition root of the phase 5 living base (D2). Creates the SINGLE <see cref="LivingBaseAuthority"/> (which owns
    /// the only phase 4 authority, roster, models and clock) and binds the single runner. Data mode and seed come from the scenario,
    /// optionally overridden on the authority's command line:
    /// <c>-trainer-phase5-mode historical|synthetic</c>, <c>-trainer-phase5-seed N</c>,
    /// <c>-trainer-phase5-dataset FOLDER</c> (absolute or under StreamingAssets; a missing folder exercises the fallback),
    /// <c>-trainer-timescale N</c> (0 &lt; N ≤ 8). "current" is refused (not offered in phase 5). Clients never compose this.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LivingBaseBootstrap : MonoBehaviour
    {
        [SerializeField] private LivingBaseScenarioDefinition scenario;
        [SerializeField] private LivingBaseRunner runner;

        public LivingBaseScenarioDefinition ScenarioDefinition => scenario;
        public LivingBaseAuthority Authority { get; private set; }
        public event Action<LivingBaseAuthority> Composed;
        public event Action<LivingBaseAuthority> ShutDown;

        public LivingBaseAuthority Compose()
        {
            if (Authority != null) throw new InvalidOperationException("Living base already composed.");
            if (scenario == null) throw new InvalidOperationException("LivingBaseScenarioDefinition not assigned.");
            if (runner == null) throw new InvalidOperationException("LivingBaseRunner not assigned.");
            string[] args = Environment.GetCommandLineArgs();
            LivingBaseAuthority authority = Create(scenario, ResolveSetup(scenario, args), DatasetFolder(scenario, args),
                UnityEngine.Application.streamingAssetsPath);
            runner.Bind(authority, scenario.TimeScale * TimeScaleOverride(args));
            Authority = authority;
            Debug.Log($"[LivingBaseBootstrap] mode requested={authority.Setup.RequestedMode} seed={authority.Setup.Seed} " +
                      $"data={DescribeData(authority)}");
            Composed?.Invoke(authority);
            return authority;
        }

        public void Shutdown()
        {
            if (runner != null) runner.Unbind();
            LivingBaseAuthority released = Authority;
            Authority = null;
            if (released != null) ShutDown?.Invoke(released);
        }

        /// <summary>The production composition (also used by validators): historical dataset loaded only when requested; a
        /// failed load passes its problem key so the authority reports FallbackSynthetic.</summary>
        public static LivingBaseAuthority Create(LivingBaseScenarioDefinition definition, LivingBaseSetup setup, string datasetFolder,
            string streamingAssetsPath)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            string problem = definition.Validate();
            if (problem != null) throw new InvalidOperationException($"LivingBaseScenarioDefinition '{definition.name}': {problem}");
            SpaceWeatherEpisodeDefinition historical = null;
            string problemKey = null;
            if (setup.RequestedMode == DataMode.Historical
                && !HistoricalDatasetSource.TryLoad(streamingAssetsPath, datasetFolder, out historical, out problemKey))
                Debug.LogWarning($"[LivingBaseBootstrap] Historical dataset unavailable ({problemKey}): labelled synthetic fallback.");
            LivingBaseScenario built = definition.ToScenario(historical);
            return new LivingBaseAuthority(built, setup, new OutpostBaseSimulation(OutpostSimulationParameters.SyntheticTraining()),
                new BaseEquipmentSimulation(), new EvaSuitSimulation(), CrewRoster.For(built.BaseOperations.Expedition.Outpost), problemKey,
                definition.MaxStepSeconds);
        }

        public static LivingBaseSetup ResolveSetup(LivingBaseScenarioDefinition definition, string[] args)
        {
            DataMode mode = definition.DefaultMode;
            ulong seed = definition.DefaultSeed;
            string modeText = Argument(args, "-trainer-phase5-mode");
            if (modeText != null)
            {
                if (string.Equals(modeText, "historical", StringComparison.OrdinalIgnoreCase)) mode = DataMode.Historical;
                else if (string.Equals(modeText, "synthetic", StringComparison.OrdinalIgnoreCase)) mode = DataMode.Synthetic;
                else Debug.LogWarning($"[LivingBaseBootstrap] Mode '{modeText}' is not offered in phase 5; using {mode}.");
            }
            string seedText = Argument(args, "-trainer-phase5-seed");
            if (seedText != null && ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed)) seed = parsed;
            return new LivingBaseSetup(mode, seed);
        }

        public static string DatasetFolder(LivingBaseScenarioDefinition definition, string[] args) =>
            Argument(args, "-trainer-phase5-dataset") ?? definition.HistoricalDatasetFolder;

        public static string DescribeData(LivingBaseAuthority authority) =>
            authority.TryGetSnapshot(out LivingBaseSnapshot s) && s != null
                ? s.Data.Availability + (s.Data.ProblemKey != null ? " (" + s.Data.ProblemKey + ")" : "")
                  + (s.Data.Dataset != null ? " sha256=" + s.Data.Dataset.Sha256 : "")
                : "unknown";

        private static string Argument(string[] args, string name)
        {
            if (args == null) return null;
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        private static double TimeScaleOverride(string[] args)
        {
            string text = Argument(args, "-trainer-timescale");
            return text != null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0 && value <= 8
                ? value : 1d;
        }

        private void OnDestroy() => Shutdown();
    }
}
