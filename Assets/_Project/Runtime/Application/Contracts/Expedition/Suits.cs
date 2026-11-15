using System;
using Trainer.Application.Outpost;
using Trainer.Simulation.Eva;

namespace Trainer.Application.Expedition
{
    /// <summary>
    /// Steps of the EDUCATIONAL suit checklist of this scenario (a declared simplification, not a real EVA
    /// preparation protocol or prebreathe recipe). Done in order: Don first, ChargeBattery and FillConsumables in any
    /// order, SealCheck last. A suit is ready to cycle out only with all four.
    /// </summary>
    [Flags]
    public enum ChecklistSteps
    {
        None = 0,
        Don = 1,
        ChargeBattery = 2,
        FillConsumables = 4,
        SealCheck = 8,
        All = Don | ChargeBattery | FillConsumables | SealCheck
    }

    /// <summary>One checklist step (for task rules).</summary>
    public enum ChecklistStep { Don, ChargeBattery, FillConsumables, SealCheck }

    public static class Checklist
    {
        public static ChecklistSteps Flag(ChecklistStep step)
        {
            switch (step)
            {
                case ChecklistStep.Don: return ChecklistSteps.Don;
                case ChecklistStep.ChargeBattery: return ChecklistSteps.ChargeBattery;
                case ChecklistStep.FillConsumables: return ChecklistSteps.FillConsumables;
                case ChecklistStep.SealCheck: return ChecklistSteps.SealCheck;
                default: throw new ArgumentOutOfRangeException(nameof(step));
            }
        }

        /// <summary>Steps that must be done before <paramref name="step"/>.</summary>
        public static ChecklistSteps Prerequisites(ChecklistStep step)
        {
            switch (step)
            {
                case ChecklistStep.Don: return ChecklistSteps.None;
                case ChecklistStep.ChargeBattery:
                case ChecklistStep.FillConsumables: return ChecklistSteps.Don;
                case ChecklistStep.SealCheck: return ChecklistSteps.Don | ChecklistSteps.ChargeBattery | ChecklistSteps.FillConsumables;
                default: throw new ArgumentOutOfRangeException(nameof(step));
            }
        }

        public static bool IsValid(ChecklistSteps steps) => (steps & ~ChecklistSteps.All) == 0;
    }

    public enum SuitStatus
    {
        /// <summary>On its rack, nobody wears it.</summary>
        Stowed,
        /// <summary>Worn inside the pressurized base or the pressurized airlock; NOT on suit supply.</summary>
        Worn,
        /// <summary>Worn and closed on its own supply (from the start of a depressurization until the end of the
        /// repressurization that brings its wearer back). Only sealed suits consume suit services.</summary>
        Sealed,
        /// <summary>Its wearer disconnected while sealed: nobody is in it, it consumes nothing and stays where it
        /// was until Restart (not teleported back or reused in this epoch).</summary>
        Abandoned
    }

    /// <summary>Rack that holds suit <see cref="SuitKey"/> at Reset (configuration). The Don task of that suit is
    /// offered at <see cref="Station"/>.</summary>
    public sealed class SuitRack
    {
        public string SuitKey { get; }
        public StationId Station { get; }

        public SuitRack(string suitKey, StationId station)
        {
            SuitKey = StableKey.Require(suitKey, nameof(suitKey));
            if (!station.IsValid) throw new ArgumentException("Rack station required.", nameof(station));
            Station = station;
        }
    }

    /// <summary>One suit as published (immutable): equipment state, wearer, checklist and the D1 reading.</summary>
    public sealed class SuitState
    {
        public int Index { get; }
        public string Key { get; }
        public SuitStatus Status { get; }
        /// <summary>Present wearer for Worn/Sealed; None otherwise.</summary>
        public PlayerId Wearer { get; }
        public ChecklistSteps Completed { get; }
        public SuitReading Reading { get; }
        public bool IsReady => Status == SuitStatus.Worn && Completed == ChecklistSteps.All;

        public SuitState(int index, string key, SuitStatus status, PlayerId wearer, ChecklistSteps completed, SuitReading reading)
        {
            if (index < 0 || index >= ExpeditionLimits.MaxSuits) throw new ArgumentOutOfRangeException(nameof(index));
            Index = index;
            Key = StableKey.Require(key, nameof(key));
            Status = OutpostGuard.Defined(status, nameof(status));
            if (!Checklist.IsValid(completed)) throw new ArgumentOutOfRangeException(nameof(completed));
            bool worn = status == SuitStatus.Worn || status == SuitStatus.Sealed;
            if (worn != wearer.IsValid) throw new ArgumentException("Worn and sealed suits have a wearer; others none.", nameof(wearer));
            if (status == SuitStatus.Stowed && completed != ChecklistSteps.None)
                throw new ArgumentException("A stowed suit has no checklist progress.", nameof(completed));
            if (worn && (completed & ChecklistSteps.Don) == 0) throw new ArgumentException("A worn suit was donned.", nameof(completed));
            if (status == SuitStatus.Sealed && completed != ChecklistSteps.All)
                throw new ArgumentException("Only a suit with the whole checklist can be sealed.", nameof(completed));
            Reading = reading ?? throw new ArgumentNullException(nameof(reading));
            if (reading.Index != index || !string.Equals(reading.Key, key, StringComparison.Ordinal))
                throw new ArgumentException("The reading belongs to another suit.", nameof(reading));
            Wearer = wearer;
            Completed = completed;
        }
    }

    public enum TaskPurpose
    {
        /// <summary>Phase 2 base work (water, air, laboratory). Default for tasks without a rule.</summary>
        BaseOperation,
        SuitPreparation,
        /// <summary>Work outside at an exterior station, on suit supply.</summary>
        ExteriorWork,
        ShelterPreparation
    }

    /// <summary>Suit condition the performer must meet to START (and keep) a task.</summary>
    public enum SuitRequirement
    {
        /// <summary>Not on suit supply (inside the pressurized base); a worn suit is allowed.</summary>
        NotSealed,
        /// <summary>Wears no suit (to don one).</summary>
        Unsuited,
        /// <summary>Wears a suit that is not sealed (checklist steps after Don).</summary>
        SuitWorn,
        /// <summary>On suit supply (exterior work).</summary>
        Sealed
    }

    /// <summary>
    /// Phase 3 rule of ONE phase 2 task type (configuration, immutable). The phase 2 <see cref="TaskDefinition"/>
    /// keeps its meaning unchanged (station, duration, base power, base completion effect); this rule adds where and
    /// how it may be done and the EVA effects that the base model cannot express. Tasks without a rule are
    /// <see cref="BaseOperation"/>s. Build them with the factory methods; the scenario validates them against tasks.
    /// </summary>
    public sealed class ExpeditionTaskRule
    {
        public TaskTypeId Task { get; }
        public TaskPurpose Purpose { get; }
        public CrewZone RequiredZone { get; }
        public SuitRequirement Suit { get; }
        /// <summary>SuitPreparation only.</summary>
        public ChecklistStep? Step { get; }
        /// <summary>Don only: the suit taken from the rack where the task is offered.</summary>
        public string SuitKey { get; }
        /// <summary>ExteriorWork: Work; otherwise Rest (only sealed performers consume suit services).</summary>
        public SuitActivity Activity { get; }
        /// <summary>ExteriorWork: tool load on the performer's suit battery while in progress, W.</summary>
        public double SuitToolWatts { get; }
        /// <summary>ChargeBattery only: stored fraction of the base energy drawn (PowerWatts × Duration), in (0, 1].
        /// The rest is lost as heat in the charger; it is never created or kept.</summary>
        public double ChargeEfficiency { get; }

        private ExpeditionTaskRule(TaskTypeId task, TaskPurpose purpose, CrewZone zone, SuitRequirement suit,
            ChecklistStep? step, string suitKey, SuitActivity activity, double toolWatts, double chargeEfficiency)
        {
            if (!task.IsValid) throw new ArgumentException("Task type id required.", nameof(task));
            Task = task;
            Purpose = OutpostGuard.Defined(purpose, nameof(purpose));
            RequiredZone = OutpostGuard.Defined(zone, nameof(zone));
            Suit = OutpostGuard.Defined(suit, nameof(suit));
            Step = step;
            SuitKey = suitKey == null ? null : StableKey.Require(suitKey, nameof(suitKey));
            Activity = OutpostGuard.Defined(activity, nameof(activity));
            SuitToolWatts = OutpostGuard.NonNegative(toolWatts, nameof(toolWatts));
            ChargeEfficiency = OutpostGuard.NonNegative(chargeEfficiency, nameof(chargeEfficiency));
        }

        public static ExpeditionTaskRule BaseOperation(TaskTypeId task) =>
            new ExpeditionTaskRule(task, TaskPurpose.BaseOperation, CrewZone.Interior, SuitRequirement.NotSealed,
                null, null, SuitActivity.Rest, 0d, 0d);

        public static ExpeditionTaskRule Don(TaskTypeId task, string suitKey) =>
            new ExpeditionTaskRule(task, TaskPurpose.SuitPreparation, CrewZone.Interior, SuitRequirement.Unsuited,
                ChecklistStep.Don, StableKey.Require(suitKey, nameof(suitKey)), SuitActivity.Rest, 0d, 0d);

        public static ExpeditionTaskRule ChargeBattery(TaskTypeId task, double efficiency)
        {
            if (!(efficiency > 0d) || efficiency > 1d) throw new ArgumentOutOfRangeException(nameof(efficiency), "In (0, 1].");
            return new ExpeditionTaskRule(task, TaskPurpose.SuitPreparation, CrewZone.Interior, SuitRequirement.SuitWorn,
                ChecklistStep.ChargeBattery, null, SuitActivity.Rest, 0d, efficiency);
        }

        public static ExpeditionTaskRule FillConsumables(TaskTypeId task) =>
            new ExpeditionTaskRule(task, TaskPurpose.SuitPreparation, CrewZone.Interior, SuitRequirement.SuitWorn,
                ChecklistStep.FillConsumables, null, SuitActivity.Rest, 0d, 0d);

        public static ExpeditionTaskRule SealCheck(TaskTypeId task) =>
            new ExpeditionTaskRule(task, TaskPurpose.SuitPreparation, CrewZone.Interior, SuitRequirement.SuitWorn,
                ChecklistStep.SealCheck, null, SuitActivity.Rest, 0d, 0d);

        public static ExpeditionTaskRule Exterior(TaskTypeId task, double suitToolWatts) =>
            new ExpeditionTaskRule(task, TaskPurpose.ExteriorWork, CrewZone.Exterior, SuitRequirement.Sealed,
                null, null, SuitActivity.Work, suitToolWatts, 0d);

        public static ExpeditionTaskRule PrepareShelter(TaskTypeId task) =>
            new ExpeditionTaskRule(task, TaskPurpose.ShelterPreparation, CrewZone.Shelter, SuitRequirement.NotSealed,
                null, null, SuitActivity.Rest, 0d, 0d);
    }
}
