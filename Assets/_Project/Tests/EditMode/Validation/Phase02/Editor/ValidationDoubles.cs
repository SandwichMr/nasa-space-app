using System;
using System.Collections.Generic;
using System.Linq;
using Trainer.Application.Outpost;
using Trainer.Simulation;
using Trainer.Simulation.LifeSupport;

// D5 TEST DOUBLES. They exist only to prove that the conformance suites accept a contract-abiding
// implementation and reject specific defects (mutants). They are NOT D3's roster or D1's model, carry no
// consumption model, and are excluded from implementation discovery by namespace.
namespace Trainer.Tooling.Validation.Phase02.Doubles
{
    [Flags]
    public enum RosterDefect { None = 0, DiagonalFaster = 1, Tunnels = 2, ReusesIds = 4, KeepsDirectionOnRestart = 8 }

    public sealed class ReferenceCrewRoster : ICrewRoster
    {
        private sealed class Member
        {
            public PlayerId Id; public int Slot; public BasePosition Position; public MoveDirection Facing = CrewPose.DefaultFacing;
            public CrewMotion Motion; public MoveDirection Held; public long Sequence;
        }

        private readonly OutpostLayout layout;
        private readonly CrewMovementSettings movement;
        private readonly RosterDefect defect;
        private readonly List<Member> members = new List<Member>();
        private IReadOnlyList<CrewMemberState> published = Array.Empty<CrewMemberState>();
        private int nextId = 1;

        public ReferenceCrewRoster(OutpostLayout layout, CrewMovementSettings movement, RosterDefect defect = RosterDefect.None)
        {
            this.layout = layout;
            this.movement = movement;
            this.defect = defect;
        }

        public IReadOnlyList<CrewMemberState> Members => published;

        public OutpostMembership Join()
        {
            int slot = Enumerable.Range(0, layout.MaxCrew).Where(s => members.All(m => m.Slot != s)).DefaultIfEmpty(-1).First();
            if (slot < 0) return OutpostMembership.Rejected(OutpostReasons.SessionFull);
            int id = (defect & RosterDefect.ReusesIds) != 0
                ? Enumerable.Range(1, 99).First(v => members.All(m => m.Id.Value != v))
                : nextId++;
            members.Add(new Member { Id = new PlayerId(id), Slot = slot, Position = layout.SpawnPoints[slot] });
            Publish();
            return OutpostMembership.Joined(new PlayerId(id));
        }

        public bool Leave(PlayerId player)
        {
            bool removed = members.RemoveAll(m => m.Id == player) > 0;
            if (removed) Publish();
            return removed;
        }

        public MovementInputDisposition SubmitMovement(PlayerId player, MovementInput input, long currentEpoch)
        {
            Member m = members.FirstOrDefault(x => x.Id == player);
            if (m == null) return MovementInputDisposition.NotJoined;
            MovementInputDisposition d = MovementInputPolicy.Evaluate(m.Sequence, currentEpoch, input);
            if (d == MovementInputDisposition.Applied)
            {
                m.Held = input.Direction;
                m.Sequence = input.Sequence;
                Publish();
            }
            return d;
        }

        public void Advance(double elapsedSeconds)
        {
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            double r = movement.CrewRadiusMeters;
            foreach (Member m in members)
            {
                if (m.Held.IsZero) { m.Motion = CrewMotion.Idle; continue; }
                double vx = m.Held.X, vy = m.Held.Y;
                if ((defect & RosterDefect.DiagonalFaster) != 0) { vx = Math.Sign(vx); vy = Math.Sign(vy); }
                double dx = vx * movement.MaxSpeedMetersPerSecond * elapsedSeconds, dy = vy * movement.MaxSpeedMetersPerSecond * elapsedSeconds;
                BasePosition start = m.Position, end;
                if ((defect & RosterDefect.Tunnels) != 0)
                {
                    var target = new BasePosition(start.X + dx, start.Y + dy);
                    end = layout.IsAdmissible(target, r) ? target : start;
                }
                else end = Sweep(start, dx, dy, r);
                double magnitude = m.Held.Magnitude;
                m.Facing = new MoveDirection(m.Held.X / magnitude, m.Held.Y / magnitude);
                m.Motion = end.Equals(start) ? CrewMotion.Idle : CrewMotion.Walking;
                m.Position = end;
            }
            Publish();
        }

        // Stop at contact: sub-steps shorter than a quarter radius, binary search inside the blocked one.
        private BasePosition Sweep(BasePosition start, double dx, double dy, double r)
        {
            double length = Math.Sqrt(dx * dx + dy * dy);
            int steps = Math.Max(1, (int)Math.Ceiling(length / (r / 4)));
            BasePosition at = start;
            for (int i = 1; i <= steps; i++)
            {
                var next = new BasePosition(start.X + dx * i / steps, start.Y + dy * i / steps);
                if (layout.IsAdmissible(next, r)) { at = next; continue; }
                double lo = 0, hi = 1;
                BasePosition from = at;
                for (int k = 0; k < 40; k++)
                {
                    double mid = (lo + hi) / 2;
                    var probe = new BasePosition(from.X + (next.X - from.X) * mid, from.Y + (next.Y - from.Y) * mid);
                    if (layout.IsAdmissible(probe, r)) lo = mid; else hi = mid;
                }
                return new BasePosition(from.X + (next.X - from.X) * lo, from.Y + (next.Y - from.Y) * lo);
            }
            return at;
        }

        public void ResetForRestart()
        {
            foreach (Member m in members)
            {
                m.Position = layout.SpawnPoints[m.Slot];
                m.Facing = CrewPose.DefaultFacing;
                m.Motion = CrewMotion.Idle;
                if ((defect & RosterDefect.KeepsDirectionOnRestart) == 0) m.Held = MoveDirection.Zero;
            }
            Publish();
        }

        private void Publish() => published = members.OrderBy(m => m.Id.Value)
            .Select(m => new CrewMemberState(m.Id, m.Slot, new CrewPose(m.Position, m.Facing, m.Motion), m.Sequence))
            .ToList().AsReadOnly();
    }

    [Flags]
    public enum SimulationDefect { None = 0, AppliesTwice = 1, PartialApply = 2, MassFromNowhere = 4 }

    /// <summary>Ledger-only double: Apply rules and a trivial energy balance. No crew consumption, no cabin model.</summary>
    public sealed class ReferenceOutpostSimulation : IOutpostSimulation
    {
        private readonly SimulationDefect defect;
        private readonly Dictionary<Guid, ResourceEffect> applied = new Dictionary<Guid, ResourceEffect>();
        private OutpostConfiguration config;
        private double time, energy, potable, untreated, food, serviceRequested, serviceServed, essentialServed;

        public ReferenceOutpostSimulation() : this(SimulationDefect.None) { }
        public ReferenceOutpostSimulation(SimulationDefect defect) { this.defect = defect; }

        public double LastFullyServedServiceSeconds { get; private set; }
        public double LastFullyServedScienceSeconds { get; private set; }

        public OutpostSimulationSnapshot Snapshot
        {
            get
            {
                PowerConfiguration p = config.Power;
                LifeSupportConfiguration l = config.LifeSupport;
                var power = new PowerSnapshot(time, energy, p.CapacityJoules, p.GenerationWatts, p.EssentialConsumptionWatts, essentialServed, 0, 0);
                var water = new WaterStock(potable, untreated, l.InitialWater.PotableCapacityKilograms, l.InitialWater.UntreatedCapacityKilograms);
                var cabin = new CabinAirObservation(GasReading.Unavailable, GasReading.Unavailable);
                return new OutpostSimulationSnapshot(power, serviceRequested, serviceServed,
                    new LifeSupportSnapshot(water, new FoodStock(food, l.InitialFood.CapacityKilograms), cabin));
            }
        }

        public void Reset(OutpostConfiguration configuration)
        {
            config = configuration ?? throw new ArgumentNullException(nameof(configuration));
            time = 0;
            energy = configuration.Power.InitialEnergyJoules;
            potable = configuration.LifeSupport.InitialWater.PotableKilograms;
            untreated = configuration.LifeSupport.InitialWater.UntreatedKilograms;
            food = configuration.LifeSupport.InitialFood.StoredKilograms;
            serviceRequested = serviceServed = 0;
            essentialServed = configuration.Power.EssentialConsumptionWatts;
            applied.Clear();
        }

        public void Advance(double elapsedSeconds, OutpostDemand demand)
        {
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (demand == null) throw new ArgumentNullException(nameof(demand));
            PowerConfiguration p = config.Power;
            double load = p.EssentialConsumptionWatts + demand.ServiceLoadWatts + (demand.ScienceRequested ? p.ScienceConsumptionWatts : 0);
            double next = energy + (p.GenerationWatts - load) * elapsedSeconds;
            bool served = next >= 0;
            energy = Math.Max(0, Math.Min(p.CapacityJoules, next));
            time += elapsedSeconds;
            serviceRequested = demand.ServiceLoadWatts;
            serviceServed = served ? demand.ServiceLoadWatts : 0;
            LastFullyServedServiceSeconds = served ? elapsedSeconds : 0;
            LastFullyServedScienceSeconds = served && demand.ScienceRequested ? elapsedSeconds : 0;
            if ((defect & SimulationDefect.MassFromNowhere) != 0)
                potable = Math.Min(potable + 0.1, config.LifeSupport.InitialWater.PotableCapacityKilograms);
        }

        public double SecondsUntilServiceChange(OutpostDemand demand) => double.PositiveInfinity;

        public ResourceTransferStatus Apply(ResourceTransfer transfer)
        {
            if (transfer == null) throw new ArgumentNullException(nameof(transfer));
            if (applied.TryGetValue(transfer.Id, out ResourceEffect previous))
            {
                if (!Same(previous, transfer.Effect)) return ResourceTransferStatus.IdConflict;
                if ((defect & SimulationDefect.AppliesTwice) == 0) return ResourceTransferStatus.AlreadyApplied;
            }
            double np = potable + transfer.Effect.DeltaFor(ResourceKind.PotableWater);
            double nu = untreated + transfer.Effect.DeltaFor(ResourceKind.UntreatedWater);
            double nf = food + transfer.Effect.DeltaFor(ResourceKind.StoredFood);
            LifeSupportConfiguration l = config.LifeSupport;
            if (np < 0 || nu < 0 || nf < 0)
            {
                if ((defect & SimulationDefect.PartialApply) != 0) potable = Math.Max(potable, Math.Min(np, l.InitialWater.PotableCapacityKilograms));
                return ResourceTransferStatus.InsufficientStock;
            }
            if (np > l.InitialWater.PotableCapacityKilograms || nu > l.InitialWater.UntreatedCapacityKilograms || nf > l.InitialFood.CapacityKilograms)
                return ResourceTransferStatus.ExceedsCapacity;
            potable = np; untreated = nu; food = nf;
            applied[transfer.Id] = transfer.Effect;
            return ResourceTransferStatus.Applied;
        }

        private static bool Same(ResourceEffect a, ResourceEffect b) =>
            a.BoundaryOutflowKilograms == b.BoundaryOutflowKilograms && a.Changes.Count == b.Changes.Count
            && a.Changes.All(c => b.DeltaFor(c.Kind) == c.DeltaKilograms);
    }
}
