using System.Collections.Generic;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Application.BaseOperations
{
    public sealed partial class BaseOperationsAuthority
    {
        private sealed partial class ContinuousPolicy
        {
            /// <summary>The complete phase 4 state around the phase 3 snapshot of the same revision.</summary>
            public BaseOperationsSnapshot Build(ExpeditionSessionSnapshot expedition)
            {
                double now = expedition.SimulatedTimeSeconds;
                var session = new BaseSessionStatus(owner.Mode, essentialUnserved, scenario.Irreversible.EssentialUnservedGraceSeconds,
                    endCause, endedAt, activitySequence, expeditionsFinished);

                var equipmentStatus = new List<EquipmentStatus>(2);
                foreach (Machine machine in machines) equipmentStatus.Add(StatusOf(machine, now));

                WaterProcessorState water = Truth.WaterProcessor;
                InstrumentReading conductivity = water.HoldKilograms > Tolerance
                    ? water.ProductConductivity : InstrumentReading.Unavailable(ReadingUnit.MicrosiemensPerCentimeter);
                var waterStatus = new WaterTreatmentStatus(water.HoldKilograms, conductivity,
                    scenario.Equipment.WaterProcessor.ProductSpecLimitMicrosiemens,
                    suspectKilograms > 0d ? PotableQuality.Suspect : PotableQuality.Verified, suspectKilograms, batchesProcessed);

                var progress = new List<OperationProgress>();
                foreach (StationPlacement station in scenario.Layout.Stations)
                    if (operations.TryGetValue(station.Id, out Operation op)) progress.Add(op.ToProgress());

                var orderRecords = new List<ResupplyOrder>(orders.Count);
                foreach (Order order in orders) orderRecords.Add(order.ToRecord());
                var activityRecords = new List<ActivityRecord>(activities.Count);
                foreach (Activity activity in activities) activityRecords.Add(activity.ToRecord());

                return new BaseOperationsSnapshot(expedition, session, Gauges(now), equipmentStatus, waterStatus, Truth.Spares, progress,
                    orderRecords, activityRecords, new List<DecisionRecord>(decisions), latestReport);
            }

            private EquipmentStatus StatusOf(Machine machine, double now)
            {
                EquipmentKind kind = machine.Kind;
                EquipmentObservation observation = machine.Observation;
                bool current = IsCurrent(observation, machine, now);
                if (observation != null && observation.Current != current)
                    observation = new EquipmentObservation(observation.Sequence, observation.Equipment, observation.ObservedBy,
                        observation.ObservedAtSeconds, observation.ValidUntilSeconds, observation.StateSequence, observation.Findings,
                        observation.Readings, current);

                EquipmentKnowledge knowledge = EquipmentKnowledge.Unknown;
                if (current)
                {
                    Findings seen = observation.Findings;
                    knowledge = kind == EquipmentKind.WaterProcessor
                        ? (seen & Findings.ProcessorFailed) != 0 ? EquipmentKnowledge.Failed
                          : (seen & Findings.ProcessorFilterRestricted) != 0 ? EquipmentKnowledge.Degraded : EquipmentKnowledge.Nominal
                        : (seen & Findings.InletFlowRestricted) != 0 ? EquipmentKnowledge.Degraded : EquipmentKnowledge.Nominal;
                }

                Findings visible = Findings.None;
                EquipmentOperatingState operating;
                if (kind == EquipmentKind.WaterProcessor)
                {
                    WaterProcessorState water = Truth.WaterProcessor;
                    operating = water.Operating;
                    if (water.HoldKilograms > Tolerance && OffSpec(water.ProductConductivity)) visible |= Findings.ProductOffSpec;
                    if (machine.FailureSeen && water.Condition == EquipmentCondition.Failed) visible |= Findings.ProcessorFailed;
                }
                else operating = Truth.Air.Operating;

                RepairJob repair = machine.RepairOpen
                    ? new RepairJob(kind, machine.Completed, machine.RepairStartedAt, machine.RepairObservation, machine.LastTestPassed)
                    : null;
                return new EquipmentStatus(kind, scenario.StationOf(kind), operating, knowledge, visible, machine.StateSequence, observation, repair);
            }

            private List<ResourceGauge> Gauges(double now)
            {
                OutpostSimulationSnapshot r = Base;
                LifeSupportSnapshot life = r.LifeSupport;
                WaterStock w = life.Water;
                return new List<ResourceGauge>
                {
                    Gauge(GaugeKind.BatteryEnergy, InstrumentReading.Valid(ReadingUnit.Joules, r.Power.EnergyJoules, now), r.Power.CapacityJoules),
                    Gauge(GaugeKind.PowerBalance, InstrumentReading.Valid(ReadingUnit.Watts, PowerBalance(r), now), null),
                    Gauge(GaugeKind.PotableWater, InstrumentReading.Valid(ReadingUnit.Kilograms, w.PotableKilograms, now), w.PotableCapacityKilograms),
                    Gauge(GaugeKind.UntreatedWater, InstrumentReading.Valid(ReadingUnit.Kilograms, w.UntreatedKilograms, now), w.UntreatedCapacityKilograms),
                    Gauge(GaugeKind.ProcessorHold, InstrumentReading.Valid(ReadingUnit.Kilograms, Truth.WaterProcessor.HoldKilograms, now),
                        scenario.Equipment.WaterProcessor.HoldCapacityKilograms),
                    Gauge(GaugeKind.StoredFood, InstrumentReading.Valid(ReadingUnit.Kilograms, life.Food.StoredKilograms, now), life.Food.CapacityKilograms),
                    Gauge(GaugeKind.CabinOxygen, Gas(life.Cabin.Oxygen), null),
                    Gauge(GaugeKind.CabinCarbonDioxide, Gas(life.Cabin.CarbonDioxide), null)
                };
            }

            private ResourceGauge Gauge(GaugeKind kind, InstrumentReading reading, double? capacity) =>
                ResourceGauge.Of(scenario.GaugeRuleOf(kind), reading, capacity, trends[(int)kind]);

            private static InstrumentReading Gas(GasReading gas) =>
                new InstrumentReading(gas.Status, ReadingUnit.Kilopascals, gas.PartialPressureKilopascals, gas.ObservedAtSeconds);
        }
    }
}
