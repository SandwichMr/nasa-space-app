using System;
using System.Collections.Generic;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Tooling.Validation.Phase02
{
    /// <summary>
    /// Validation spy: forwards every call to the REAL simulation it wraps (D1's implementation in conformance
    /// runs) and records what the authority asked for. It never decides a result, changes stock or alters a
    /// demand. Used to prove "costs applied once" and "demand matches the active tasks" on the authority side.
    /// Not for production scenes.
    /// </summary>
    public sealed class RecordingOutpostSimulation : IOutpostSimulation
    {
        private readonly IOutpostSimulation inner;

        public List<(Guid Id, ResourceEffect Effect, ResourceTransferStatus Status)> Applies { get; } =
            new List<(Guid, ResourceEffect, ResourceTransferStatus)>();
        public List<(double Elapsed, OutpostDemand Demand)> Advances { get; } = new List<(double, OutpostDemand)>();
        public int Resets { get; private set; }

        public RecordingOutpostSimulation(IOutpostSimulation inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

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
            inner.Advance(elapsedSeconds, demand);
            Advances.Add((elapsedSeconds, demand));
        }

        public double SecondsUntilServiceChange(OutpostDemand demand) => inner.SecondsUntilServiceChange(demand);

        public ResourceTransferStatus Apply(ResourceTransfer transfer)
        {
            ResourceTransferStatus status = inner.Apply(transfer);
            Applies.Add((transfer.Id, transfer.Effect, status));
            return status;
        }
    }
}
