using System;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Tests.Simulation.Phase04
{
    /// <summary>
    /// SYNTHETIC values for the D1 phase 4 tests, chosen so results are hand-checkable. Not NASA data, not hardware,
    /// not water-quality or clinical thresholds. Units: s, kg, µS/cm, fraction.
    /// Processor: batch 2 kg, recovery 0.90 nominal / 0.75 degraded, product 20 / 80 µS/cm against a 40 µS/cm
    /// acceptance line, degraded at 120 s, failed 180 s later if not repaired.
    /// Beds: 0.002 loading per crew-second, knee 0.6, 0.2 factor when full, active bed starts at 0.3.
    /// </summary>
    internal static class Phase04SimulationFixtures
    {
        public const string Label = "SINTÉTICO · fixture de pruebas D1 fase 4";
        public const double Tolerance = 1e-9;
        public const string ProcessorFilter = "processor-filter";
        public const string InletFilter = "inlet-filter";

        public static BaseEquipmentConfiguration Equipment(double? degradeAt = 120, double? failAfter = 180,
            double? inletRestrictedAt = null, int processorSpares = 1, int inletSpares = 1, double initialActive = 0.3,
            double initialStandby = 0.0) =>
            new BaseEquipmentConfiguration(
                new WaterProcessorConfiguration(2.0, 2.0, 0.90, 0.75, 20, 80, 40, degradeAt, failAfter, ProcessorFilter),
                new AirRevitalizationConfiguration(0.002, 0.6, 0.2, initialActive, initialStandby, inletRestrictedAt, 0.5, InletFilter),
                new[] { new SpareStock(ProcessorFilter, processorSpares, 3), new SpareStock(InletFilter, inletSpares, 3) },
                Label);

        public static BaseEquipmentSimulation StartedEquipment(BaseEquipmentConfiguration configuration = null)
        {
            var model = new BaseEquipmentSimulation();
            model.Reset(configuration ?? Equipment());
            return model;
        }

        public static EquipmentDemand Crew(int aboard) => new EquipmentDemand(aboard);

        public static Guid Id(int n) => new Guid(n, 0, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });

        public static int SpareCount(BaseEquipmentSnapshot snapshot, string key)
        {
            if (!snapshot.TryGetSpare(key, out SpareStock spare)) throw new ArgumentException(key);
            return spare.Count;
        }

        /// <summary>Isolate → replace filter → restore, with distinct ids starting at <paramref name="firstId"/>.</summary>
        public static void RepairProcessor(BaseEquipmentSimulation model, int firstId)
        {
            Expect(model.Apply(EquipmentEffect.SetOperatingState(Id(firstId), EquipmentKind.WaterProcessor, EquipmentOperatingState.Isolated)));
            Expect(model.Apply(EquipmentEffect.ReplaceProcessorFilter(Id(firstId + 1))));
            Expect(model.Apply(EquipmentEffect.SampleProduct(Id(firstId + 2))));
            Expect(model.Apply(EquipmentEffect.SetOperatingState(Id(firstId + 3), EquipmentKind.WaterProcessor, EquipmentOperatingState.Online)));
        }

        private static void Expect(EquipmentEffectStatus status)
        {
            if (status != EquipmentEffectStatus.Applied) throw new InvalidOperationException("Expected Applied, got " + status);
        }
    }
}
