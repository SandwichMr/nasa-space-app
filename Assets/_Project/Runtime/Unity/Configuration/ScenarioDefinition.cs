using Trainer.Application;
using Trainer.Simulation;
using UnityEngine;

namespace Trainer.Unity.Configuration
{
    /// <summary>
    /// Authoring data for one mission scenario. Default values are ILLUSTRATIVE placeholders chosen
    /// only to produce a visible trade-off (science drains the battery); they are not approved
    /// mission data. Conversion creates new in-memory objects and never mutates this asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Trainer/Scenario Definition", fileName = "ScenarioDefinition")]
    public sealed class ScenarioDefinition : ScriptableObject
    {
        [Header("Label — shown to players and testers")]
        [SerializeField, Tooltip("Short scenario name.")]
        private string displayName = "Escenario sintético";
        [SerializeField, TextArea(2, 6), Tooltip("Where the values come from. Say plainly when they are illustrative.")]
        private string provenance = "Valores ilustrativos para enseñar el compromiso energía/ciencia. " +
            "No representan hardware ni datos de la NASA.";

        [Header("Power (joules / watts) — illustrative")]
        [SerializeField, Min(0)] private double initialEnergyJoules = 5000;
        [SerializeField, Min(0)] private double capacityJoules = 10000;
        [SerializeField, Min(0)] private double generationWatts = 100;
        [SerializeField, Min(0)] private double essentialConsumptionWatts = 60;
        [SerializeField, Min(0), Tooltip("Must be > 0 so served science work can be observed.")]
        private double scienceConsumptionWatts = 60;
        [SerializeField, Min(0)] private double maxChargeWatts = 100;
        [SerializeField, Min(0)] private double maxDischargeWatts = 200;

        [Header("Mission (simulated seconds) — illustrative")]
        [SerializeField, Min(0)] private double scienceWorkTargetSeconds = 120;
        [SerializeField, Min(0)] private double missionDurationSeconds = 300;

        [Header("Clock")]
        [SerializeField, Min(0), Tooltip("Simulated seconds per real second.")]
        private double timeScale = 1;
        [SerializeField, Min(0), Tooltip("Longest simulated sub-step per Advance call.")]
        private double maxStepSeconds = MissionSession.DefaultMaxStepSeconds;

        public string DisplayName => displayName;
        public string Provenance => provenance;
        public double TimeScale => timeScale;
        public double MaxStepSeconds => maxStepSeconds;

        /// <summary>Returns null when the values can build a session, otherwise the first problem found.
        /// Never modifies the asset.</summary>
        public string Validate()
        {
            double[] power = { initialEnergyJoules, capacityJoules, generationWatts, essentialConsumptionWatts,
                scienceConsumptionWatts, maxChargeWatts, maxDischargeWatts };
            foreach (double value in power)
                if (!IsFinite(value) || value < 0) return "Power values must be finite and nonnegative.";
            if (initialEnergyJoules > capacityJoules) return "Initial energy exceeds capacity.";
            if (!(scienceConsumptionWatts > 0))
                return "Science consumption must be positive to evaluate served science work.";
            if (!IsPositive(scienceWorkTargetSeconds)) return "Science work target must be positive.";
            if (!IsPositive(missionDurationSeconds)) return "Mission duration must be positive.";
            if (!IsPositive(maxStepSeconds)) return "Max step must be positive.";
            if (!IsPositive(timeScale)) return "Time scale must be positive (0 would freeze the mission clock).";
            return null;
        }

        /// <summary>Throws if values violate the PowerConfiguration contract.</summary>
        public PowerConfiguration ToPowerConfiguration()
        {
            if (!(scienceConsumptionWatts > 0))
                throw new System.ArgumentOutOfRangeException(nameof(scienceConsumptionWatts),
                    "Science consumption must be positive to evaluate served science work.");
            return new PowerConfiguration(initialEnergyJoules, capacityJoules, generationWatts,
                essentialConsumptionWatts, scienceConsumptionWatts, maxChargeWatts, maxDischargeWatts);
        }

        public MissionRules ToMissionRules() => new MissionRules(scienceWorkTargetSeconds, missionDurationSeconds);

        /// <summary>Creates the authoritative session for this scenario over the given simulation.</summary>
        public MissionSession CreateSession(IPowerSimulation simulation)
        {
            string problem = Validate();
            if (problem != null) throw new System.InvalidOperationException($"ScenarioDefinition '{name}': {problem}");
            return new MissionSession(simulation, ToPowerConfiguration(), ToMissionRules(), maxStepSeconds);
        }

#if UNITY_EDITOR
        // Authoring feedback only: reports, never clamps or rewrites the values.
        private void OnValidate()
        {
            string problem = Validate();
            if (problem != null) Debug.LogWarning($"ScenarioDefinition '{name}': {problem}", this);
        }
#endif

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool IsPositive(double value) => IsFinite(value) && value > 0;
    }
}
