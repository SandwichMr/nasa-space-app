using System;
using System.Collections.Generic;
using Trainer.Application.Outpost;
using Trainer.Simulation;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Tests.Phase02
{
    /// <summary>
    /// TEST DOUBLE of D1's <see cref="IOutpostSimulation"/>. Not a model: no consumption, no cabin physics.
    /// Time advances exactly by the requested seconds; service and science are fully served or not according to
    /// knobs; Apply is all or nothing with id tracking like the contract demands. Used only by D2's tests.
    /// </summary>
    public sealed class ScriptedOutpostSimulation : IOutpostSimulation
    {
        private readonly Dictionary<Guid, ResourceEffect> applied = new Dictionary<Guid, ResourceEffect>();
        private OutpostConfiguration configuration;
        private double time, potable, untreated, food;
        private OutpostDemand lastDemand = new OutpostDemand(0, false, 0);

        public bool ServiceServed = true;
        public bool ScienceServed = true;
        public bool EssentialServed = true;
        public bool ThrowOnAdvance;
        public bool ThrowOnReset;
        public int ResetCount;
        public readonly List<OutpostDemand> Demands = new List<OutpostDemand>();
        public readonly List<ResourceTransfer> ApplyCalls = new List<ResourceTransfer>();

        public double LastFullyServedServiceSeconds { get; private set; }
        public double LastFullyServedScienceSeconds { get; private set; }

        public void Reset(OutpostConfiguration value)
        {
            if (ThrowOnReset) throw new InvalidOperationException("scripted reset failure");
            configuration = value ?? throw new ArgumentNullException(nameof(value));
            ResetCount++;
            time = 0;
            potable = value.LifeSupport.InitialWater.PotableKilograms;
            untreated = value.LifeSupport.InitialWater.UntreatedKilograms;
            food = value.LifeSupport.InitialFood.StoredKilograms;
            applied.Clear();
            LastFullyServedServiceSeconds = LastFullyServedScienceSeconds = 0;
            lastDemand = new OutpostDemand(0, false, 0);
        }

        public OutpostSimulationSnapshot Snapshot
        {
            get
            {
                PowerConfiguration p = configuration.Power;
                LifeSupportConfiguration life = configuration.LifeSupport;
                double essential = p.EssentialConsumptionWatts;
                double scienceRequested = lastDemand.ScienceRequested ? p.ScienceConsumptionWatts : 0;
                var power = new PowerSnapshot(time, p.InitialEnergyJoules, p.CapacityJoules, p.GenerationWatts,
                    essential, EssentialServed ? essential : 0,
                    scienceRequested, ScienceServed ? scienceRequested : 0);
                var water = new WaterStock(potable, untreated, life.InitialWater.PotableCapacityKilograms,
                    life.InitialWater.UntreatedCapacityKilograms);
                var cabin = new CabinAirObservation(GasReading.Unavailable, GasReading.Unavailable);
                return new OutpostSimulationSnapshot(power, lastDemand.ServiceLoadWatts,
                    ServiceServed ? lastDemand.ServiceLoadWatts : 0,
                    new LifeSupportSnapshot(water, new FoodStock(food, life.InitialFood.CapacityKilograms), cabin));
            }
        }

        public void Advance(double elapsedSeconds, OutpostDemand demand)
        {
            if (demand == null) throw new ArgumentNullException(nameof(demand));
            if (ThrowOnAdvance) throw new InvalidOperationException("scripted advance failure");
            if (elapsedSeconds == 0) return;
            Demands.Add(demand);
            lastDemand = demand;
            time += elapsedSeconds;
            LastFullyServedServiceSeconds = demand.ServiceLoadWatts <= 0 || ServiceServed ? elapsedSeconds : 0;
            LastFullyServedScienceSeconds = demand.ScienceRequested && ScienceServed ? elapsedSeconds : 0;
        }

        public double SecondsUntilServiceChange(OutpostDemand demand) => EssentialServed ? double.PositiveInfinity : 0;

        public ResourceTransferStatus Apply(ResourceTransfer transfer)
        {
            ApplyCalls.Add(transfer);
            if (applied.TryGetValue(transfer.Id, out ResourceEffect previous))
                return ReferenceEquals(previous, transfer.Effect) ? ResourceTransferStatus.AlreadyApplied : ResourceTransferStatus.IdConflict;
            double p = potable + transfer.Effect.DeltaFor(ResourceKind.PotableWater);
            double u = untreated + transfer.Effect.DeltaFor(ResourceKind.UntreatedWater);
            double f = food + transfer.Effect.DeltaFor(ResourceKind.StoredFood);
            if (p < 0 || u < 0 || f < 0) return ResourceTransferStatus.InsufficientStock;
            LifeSupportConfiguration life = configuration.LifeSupport;
            if (p > life.InitialWater.PotableCapacityKilograms || u > life.InitialWater.UntreatedCapacityKilograms
                || f > life.InitialFood.CapacityKilograms) return ResourceTransferStatus.ExceedsCapacity;
            potable = p;
            untreated = u;
            food = f;
            applied.Add(transfer.Id, transfer.Effect);
            return ResourceTransferStatus.Applied;
        }

        /// <summary>Test hook: drains untreated water outside the task flow (stands for D1's own consumption).</summary>
        public void SetUntreated(double kilograms) => untreated = kilograms;
    }

    /// <summary>
    /// TEST DOUBLE of D3's <see cref="ICrewRoster"/>: slots, ids never reused, held direction, straight moves that
    /// stop when the next position is not admissible. <see cref="Place"/> stands for "walked there" so authority
    /// tests can set confirmed positions directly. Not D3's movement implementation.
    /// </summary>
    public sealed class ScriptedCrewRoster : ICrewRoster
    {
        private sealed class Member
        {
            public PlayerId Player;
            public int Slot;
            public BasePosition Position;
            public MoveDirection Facing = CrewPose.DefaultFacing;
            public MoveDirection Held = MoveDirection.Zero;
            public CrewMotion Motion = CrewMotion.Idle;
            public long Sequence;
        }

        private readonly OutpostLayout layout;
        private readonly CrewMovementSettings movement;
        private readonly SortedDictionary<int, Member> members = new SortedDictionary<int, Member>();
        private int nextId = 1;
        private IReadOnlyList<CrewMemberState> published = Array.Empty<CrewMemberState>();

        public int AdvanceCalls;
        public double AdvancedSeconds;

        public ScriptedCrewRoster(OutpostScenario scenario)
        {
            layout = scenario.Layout;
            movement = scenario.Movement;
        }

        public IReadOnlyList<CrewMemberState> Members => published;

        public OutpostMembership Join()
        {
            var used = new HashSet<int>();
            foreach (Member m in members.Values) used.Add(m.Slot);
            for (int slot = 0; slot < layout.MaxCrew; slot++)
            {
                if (used.Contains(slot)) continue;
                var member = new Member { Player = new PlayerId(nextId++), Slot = slot, Position = layout.SpawnPoints[slot] };
                members.Add(member.Player.Value, member);
                Publish();
                return OutpostMembership.Joined(member.Player);
            }
            return OutpostMembership.Rejected(OutpostReasons.SessionFull);
        }

        public bool Leave(PlayerId player)
        {
            if (!members.Remove(player.Value)) return false;
            Publish();
            return true;
        }

        public MovementInputDisposition SubmitMovement(PlayerId player, MovementInput input, long currentEpoch)
        {
            if (!members.TryGetValue(player.Value, out Member member)) return MovementInputDisposition.NotJoined;
            MovementInputDisposition disposition = MovementInputPolicy.Evaluate(member.Sequence, currentEpoch, input);
            if (disposition != MovementInputDisposition.Applied) return disposition;
            member.Sequence = input.Sequence;
            member.Held = input.Direction;
            Publish();
            return disposition;
        }

        public void Advance(double elapsedSeconds)
        {
            AdvanceCalls++;
            AdvancedSeconds += elapsedSeconds;
            foreach (Member member in members.Values)
            {
                double distance = member.Held.Magnitude * movement.MaxSpeedMetersPerSecond * elapsedSeconds;
                if (distance <= 0)
                {
                    member.Motion = CrewMotion.Idle;
                    continue;
                }
                var next = new BasePosition(member.Position.X + member.Held.X * movement.MaxSpeedMetersPerSecond * elapsedSeconds,
                    member.Position.Y + member.Held.Y * movement.MaxSpeedMetersPerSecond * elapsedSeconds);
                if (layout.IsAdmissible(next, movement.CrewRadiusMeters))
                {
                    member.Position = next;
                    member.Motion = CrewMotion.Walking;
                    member.Facing = MoveDirection.FromAxes(member.Held.X / member.Held.Magnitude, member.Held.Y / member.Held.Magnitude);
                }
                else member.Motion = CrewMotion.Idle;
            }
            Publish();
        }

        public void ResetForRestart()
        {
            foreach (Member member in members.Values)
            {
                member.Position = layout.SpawnPoints[member.Slot];
                member.Facing = CrewPose.DefaultFacing;
                member.Held = MoveDirection.Zero;
                member.Motion = CrewMotion.Idle;
            }
            Publish();
        }

        /// <summary>Test hook: sets a confirmed, admissible position (stands for having walked there).</summary>
        public void Place(PlayerId player, BasePosition position)
        {
            if (!layout.IsAdmissible(position, movement.CrewRadiusMeters))
                throw new ArgumentException($"{position} is not admissible.", nameof(position));
            members[player.Value].Position = position;
            Publish();
        }

        private void Publish()
        {
            var list = new List<CrewMemberState>();
            foreach (Member m in members.Values)
                list.Add(new CrewMemberState(m.Player, m.Slot, new CrewPose(m.Position, m.Facing, m.Motion), m.Sequence));
            published = list.AsReadOnly();
        }
    }
}
