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

        public double TimeScale => timeScale;
        public double MaxStepSeconds => maxStepSeconds;

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
        public MissionSession CreateSession(IPowerSimulation simulation) =>
            new MissionSession(simulation, ToPowerConfiguration(), ToMissionRules(), maxStepSeconds);
    }
}
