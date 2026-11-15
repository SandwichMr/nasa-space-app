using System;
using System.Collections.Generic;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Tooling.Validation.Phase04
{
    /// <summary>Base stocks captured around one call (kg and J).</summary>
    public struct BaseStocks
    {
        public double Potable, Untreated, Food, Energy, Time;

        public static BaseStocks Of(OutpostSimulationSnapshot s) => s == null ? default(BaseStocks) : new BaseStocks
        {
            Potable = s.LifeSupport.Water.PotableKilograms,
            Untreated = s.LifeSupport.Water.UntreatedKilograms,
            Food = s.LifeSupport.Food.StoredKilograms,
            Energy = s.Power.EnergyJoules,
            Time = s.Power.SimulatedTimeSeconds
        };
    }

    /// <summary>
    /// VALIDATION SPY (Editor assembly, never in a scene or build): forwards every call to the REAL D1 base model and records
    /// what the authority asked for and what the model did, so D5 can prove "each cost applied once", "water only comes in
    /// through a transfer with a boundary inflow" and "the air factor is set every step". It decides nothing and changes no
    /// value. It implements <see cref="IAirServiceCapacity"/> only by forwarding to the wrapped object (the contract requires
    /// both on the SAME object).
    /// </summary>
    public sealed class RecordingBaseSimulation : IOutpostSimulation, IAirServiceCapacity
    {
        public sealed class ApplyRecord
        {
            public Guid Id;
            public ResourceEffect Effect;
            public ResourceTransferStatus Status;
            public BaseStocks Before, After;
        }

        public sealed class AdvanceRecord
        {
            public double Elapsed;
            public OutpostDemand Demand;
            public BaseStocks Before, After;
        }

        private readonly IOutpostSimulation inner;

        public RecordingBaseSimulation(IOutpostSimulation inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IOutpostSimulation Inner => inner;
        public List<ApplyRecord> Applies { get; } = new List<ApplyRecord>();
        public List<AdvanceRecord> Advances { get; } = new List<AdvanceRecord>();
        public List<double> FactorsSet { get; } = new List<double>();
        public int Resets { get; private set; }

        public OutpostSimulationSnapshot Snapshot => inner.Snapshot;
        public double LastFullyServedServiceSeconds => inner.LastFullyServedServiceSeconds;
        public double LastFullyServedScienceSeconds => inner.LastFullyServedScienceSeconds;

        public void Reset(OutpostConfiguration configuration)
        {
            Resets++;
            inner.Reset(configuration);
        }

        public void Advance(double elapsedSeconds, OutpostDemand demand)
        {
            BaseStocks before = BaseStocks.Of(inner.Snapshot);
            inner.Advance(elapsedSeconds, demand);
            Advances.Add(new AdvanceRecord { Elapsed = elapsedSeconds, Demand = demand, Before = before, After = BaseStocks.Of(inner.Snapshot) });
        }

        public double SecondsUntilServiceChange(OutpostDemand demand) => inner.SecondsUntilServiceChange(demand);

        public ResourceTransferStatus Apply(ResourceTransfer transfer)
        {
            BaseStocks before = BaseStocks.Of(inner.Snapshot);
            ResourceTransferStatus status = inner.Apply(transfer);
            Applies.Add(new ApplyRecord { Id = transfer.Id, Effect = transfer.Effect, Status = status, Before = before, After = BaseStocks.Of(inner.Snapshot) });
            return status;
        }

        public void SetCarbonDioxideRemovalFactor(double factor)
        {
            if (!(inner is IAirServiceCapacity capacity))
                throw new InvalidOperationException("The base model " + inner.GetType().Name + " does not implement IAirServiceCapacity.");
            capacity.SetCarbonDioxideRemovalFactor(factor);
            FactorsSet.Add(factor);
        }

        public double CarbonDioxideRemovalFactor => inner is IAirServiceCapacity capacity ? capacity.CarbonDioxideRemovalFactor : 1d;
    }

    /// <summary>VALIDATION SPY of the D1 equipment model (same rules as <see cref="RecordingBaseSimulation"/>).</summary>
    public sealed class RecordingEquipmentModel : IBaseEquipmentModel
    {
        public sealed class EffectRecord
        {
            public EquipmentEffect Effect;
            public EquipmentEffectStatus Status;
            public BaseEquipmentSnapshot Before, After;
        }

        private readonly IBaseEquipmentModel inner;

        public RecordingEquipmentModel(IBaseEquipmentModel inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IBaseEquipmentModel Inner => inner;
        public List<EffectRecord> Effects { get; } = new List<EffectRecord>();
        public List<(double Elapsed, int Crew)> Advances { get; } = new List<(double, int)>();
        public int Resets { get; private set; }

        public BaseEquipmentSnapshot Snapshot => inner.Snapshot;

        public void Reset(BaseEquipmentConfiguration configuration)
        {
            Resets++;
            inner.Reset(configuration);
        }

        public void Advance(double elapsedSeconds, EquipmentDemand demand)
        {
            inner.Advance(elapsedSeconds, demand);
            Advances.Add((elapsedSeconds, demand == null ? -1 : demand.CrewAboard));
        }

        public double SecondsUntilEquipmentEvent(EquipmentDemand demand) => inner.SecondsUntilEquipmentEvent(demand);

        public EquipmentEffectStatus Apply(EquipmentEffect effect)
        {
            BaseEquipmentSnapshot before = inner.Snapshot;
            EquipmentEffectStatus status = inner.Apply(effect);
            Effects.Add(new EffectRecord { Effect = effect, Status = status, Before = before, After = inner.Snapshot });
            return status;
        }
    }
}
