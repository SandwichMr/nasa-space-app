using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Application.Outpost;

namespace Trainer.Application.Expedition
{
    /// <summary>Size limits of the phase 3 extension (constructors enforce them so wire encoders can rely on them).
    /// Phase 2 limits (<see cref="OutpostLimits"/>) still bound crew, stations, tasks, obstacles and objectives.</summary>
    public static class ExpeditionLimits
    {
        public const int MaxZoneRegions = 16;
        public const int MaxSuits = Trainer.Simulation.Eva.EvaConfiguration.MaxSuits;
        public const int MaxAirlockCapacity = 4;
        public const int MaxAdvisories = 8;
        public const int MaxReceptions = 64;
        public const int MaxIncidents = 32;
        public const int MaxAssessments = OutpostLimits.MaxCrew;
        public const int MaxKnownAdvisories = MaxAdvisories;
    }

    /// <summary>
    /// Where a crew member is, derived ONLY by the authority from the CONFIRMED position (never sent by a client).
    /// Interior and Shelter are pressurized base; Airlock is the chamber (its pressure depends on the cycle);
    /// Exterior is the lunar surface (no breathable atmosphere: only a sealed suit sustains a member there).
    /// </summary>
    public enum CrewZone { Interior, Shelter, Airlock, Exterior }

    public static class CrewZones
    {
        public static bool IsPressurizedBase(CrewZone zone) => zone == CrewZone.Interior || zone == CrewZone.Shelter;
    }

    /// <summary>One rectangle of a zone (configuration). Regions are checked in scenario order and the FIRST one
    /// containing the member's centre gives the zone, so a smaller region (shelter, chamber) goes before a larger
    /// one. Every admissible position must fall in some region (validated by D5).</summary>
    public sealed class ZoneRegion
    {
        public CrewZone Zone { get; }
        public BaseRect Area { get; }

        public ZoneRegion(CrewZone zone, BaseRect area)
        {
            Zone = OutpostGuard.Defined(zone, nameof(zone));
            Area = area;
        }
    }

    public enum AirlockState
    {
        /// <summary>Idle at base pressure; inner hatch open (passable), outer hatch closed (blocks).</summary>
        PressurizedInnerOpen,
        /// <summary>Cycling to vacuum; both hatches closed. Occupants are on suit supply.</summary>
        Depressurizing,
        /// <summary>Idle at vacuum; outer hatch open (passable), inner hatch closed (blocks).</summary>
        DepressurizedOuterOpen,
        /// <summary>Cycling to base pressure; both hatches closed. Occupants are on suit supply until it ends.</summary>
        Repressurizing
    }

    /// <summary>
    /// Airlock of the scenario (configuration, immutable). Hatch rectangles are NOT layout obstacles: the plan is
    /// drawn with doorways, and the authority makes a CLOSED hatch block movement by adding its rectangle to the
    /// roster's obstacle overlay. A hatch only closes when no crew disc overlaps it (nobody is cut in two).
    /// Panels are layout stations reachable from both sides of their hatch. Capacity limits cycling, not walking in.
    /// Energy: <see cref="PumpPowerWatts"/> joins the base service load while a cycle progresses (a cycle advances
    /// only while that load is fully served). Gas: each repressurization takes <see cref="MakeupGasPerCycleKilograms"/>
    /// from the EVA make-up supply once, at its start (what the previous depressurization vented).
    /// </summary>
    public sealed class AirlockConfiguration
    {
        public BaseRect Chamber { get; }
        public BaseRect InnerHatch { get; }
        public BaseRect OuterHatch { get; }
        public StationId InnerPanel { get; }
        public StationId OuterPanel { get; }
        public int Capacity { get; }
        public double DepressurizeSeconds { get; }
        public double RepressurizeSeconds { get; }
        public double PumpPowerWatts { get; }
        public double MakeupGasPerCycleKilograms { get; }
        /// <summary>Exterior point just outside the outer hatch: the destination of every return route.</summary>
        public BasePosition ExteriorEntry { get; }

        public AirlockConfiguration(BaseRect chamber, BaseRect innerHatch, BaseRect outerHatch, StationId innerPanel,
            StationId outerPanel, int capacity, double depressurizeSeconds, double repressurizeSeconds,
            double pumpPowerWatts, double makeupGasPerCycleKilograms, BasePosition exteriorEntry)
        {
            if (!innerPanel.IsValid || !outerPanel.IsValid) throw new ArgumentException("Both airlock panels are required.");
            if (innerPanel == outerPanel) throw new ArgumentException("Inner and outer panels must differ.", nameof(outerPanel));
            if (capacity < 1 || capacity > ExpeditionLimits.MaxAirlockCapacity) throw new ArgumentOutOfRangeException(nameof(capacity));
            Chamber = chamber;
            InnerHatch = innerHatch;
            OuterHatch = outerHatch;
            InnerPanel = innerPanel;
            OuterPanel = outerPanel;
            Capacity = capacity;
            DepressurizeSeconds = OutpostGuard.Positive(depressurizeSeconds, nameof(depressurizeSeconds));
            RepressurizeSeconds = OutpostGuard.Positive(repressurizeSeconds, nameof(repressurizeSeconds));
            PumpPowerWatts = OutpostGuard.NonNegative(pumpPowerWatts, nameof(pumpPowerWatts));
            MakeupGasPerCycleKilograms = OutpostGuard.NonNegative(makeupGasPerCycleKilograms, nameof(makeupGasPerCycleKilograms));
            ExteriorEntry = exteriorEntry;
        }

        public bool IsPanel(StationId station) => station.IsValid && (station == InnerPanel || station == OuterPanel);
    }

    /// <summary>Airlock as published (immutable). <see cref="CycleSequence"/> counts cycles STARTED in the epoch;
    /// a CycleAirlock command states the value it saw, so a request made for a previous state is refused.</summary>
    public sealed class AirlockStatus
    {
        public AirlockState State { get; }
        public long CycleSequence { get; }
        /// <summary>Fully served seconds of the current cycle; 0 when idle.</summary>
        public double CycleWorkSeconds { get; }
        /// <summary>Duration of the current cycle; 0 when idle.</summary>
        public double CycleDurationSeconds { get; }
        /// <summary>The last step did not fully serve the pump, so the cycle did not advance.</summary>
        public bool Stalled { get; }
        /// <summary>Present members whose confirmed centre is in the chamber (zone Airlock), in PlayerId order.</summary>
        public IReadOnlyList<PlayerId> Occupants { get; }
        /// <summary>Who started the current cycle; None when idle.</summary>
        public PlayerId CycleRequestedBy { get; }

        public bool IsCycling => State == AirlockState.Depressurizing || State == AirlockState.Repressurizing;
        public bool InnerHatchOpen => State == AirlockState.PressurizedInnerOpen;
        public bool OuterHatchOpen => State == AirlockState.DepressurizedOuterOpen;
        public double CycleFraction => CycleDurationSeconds > 0d ? CycleWorkSeconds / CycleDurationSeconds : 0d;

        public AirlockStatus(AirlockState state, long cycleSequence, double cycleWorkSeconds, double cycleDurationSeconds,
            bool stalled, IEnumerable<PlayerId> occupants, PlayerId cycleRequestedBy)
        {
            State = OutpostGuard.Defined(state, nameof(state));
            if (cycleSequence < 0) throw new ArgumentOutOfRangeException(nameof(cycleSequence));
            CycleSequence = cycleSequence;
            bool cycling = state == AirlockState.Depressurizing || state == AirlockState.Repressurizing;
            CycleWorkSeconds = OutpostGuard.NonNegative(cycleWorkSeconds, nameof(cycleWorkSeconds));
            CycleDurationSeconds = OutpostGuard.NonNegative(cycleDurationSeconds, nameof(cycleDurationSeconds));
            if (cycling)
            {
                if (!(cycleDurationSeconds > 0d) || cycleWorkSeconds > cycleDurationSeconds)
                    throw new ArgumentException("A cycle needs a positive duration and work within it.", nameof(cycleWorkSeconds));
                if (!cycleRequestedBy.IsValid) throw new ArgumentException("A cycle records who requested it.", nameof(cycleRequestedBy));
            }
            else if (cycleWorkSeconds != 0d || cycleDurationSeconds != 0d || stalled || cycleRequestedBy.IsValid)
                throw new ArgumentException("An idle airlock has no cycle progress, stall or requester.");
            Stalled = stalled;
            CycleRequestedBy = cycleRequestedBy;
            if (occupants == null) throw new ArgumentNullException(nameof(occupants));
            var copy = new List<PlayerId>();
            foreach (PlayerId occupant in occupants)
            {
                if (!occupant.IsValid) throw new ArgumentException("Invalid occupant.", nameof(occupants));
                if (copy.Contains(occupant)) throw new ArgumentException($"{occupant} listed twice.", nameof(occupants));
                copy.Add(occupant);
            }
            if (copy.Count > OutpostLimits.MaxCrew) throw new ArgumentException("Too many occupants.", nameof(occupants));
            copy.Sort();
            Occupants = new ReadOnlyCollection<PlayerId>(copy.ToArray());
        }

        public static AirlockStatus Idle(AirlockState state, long cycleSequence, IEnumerable<PlayerId> occupants) =>
            new AirlockStatus(state, cycleSequence, 0d, 0d, false, occupants, PlayerId.None);
    }

    /// <summary>
    /// Storm shelter of the scenario (configuration, immutable). The shelter AREA is the scenario's Shelter zone.
    /// Protection is effective only with real requirements, never by touching an icon: the member's confirmed
    /// centre is in the Shelter zone, the shelter was prepared by completing <see cref="PrepareTask"/> at
    /// <see cref="Panel"/>, and its ventilation (<see cref="VentilationWatts"/>, base service load while prepared and
    /// occupied) was fully served in the last step. No shielding percentage or dose is modelled.
    /// </summary>
    public sealed class ShelterConfiguration
    {
        public StationId Panel { get; }
        public TaskTypeId PrepareTask { get; }
        public double VentilationWatts { get; }

        public ShelterConfiguration(StationId panel, TaskTypeId prepareTask, double ventilationWatts)
        {
            if (!panel.IsValid) throw new ArgumentException("Shelter panel required.", nameof(panel));
            if (!prepareTask.IsValid) throw new ArgumentException("Shelter preparation task required.", nameof(prepareTask));
            Panel = panel;
            PrepareTask = prepareTask;
            VentilationWatts = OutpostGuard.NonNegative(ventilationWatts, nameof(ventilationWatts));
        }
    }

    /// <summary>Shelter as published (immutable).</summary>
    public sealed class ShelterStatus
    {
        public bool Prepared { get; }
        /// <summary>Ventilation requested in the last step (prepared and occupied).</summary>
        public bool VentilationRunning { get; }
        /// <summary>The last step fully served the base service channel while ventilation ran (false when not running).</summary>
        public bool ServiceAvailable { get; }
        public int Occupants { get; }

        public ShelterStatus(bool prepared, bool ventilationRunning, bool serviceAvailable, int occupants)
        {
            if (occupants < 0 || occupants > OutpostLimits.MaxCrew) throw new ArgumentOutOfRangeException(nameof(occupants));
            if (ventilationRunning && !prepared) throw new ArgumentException("Ventilation runs only in a prepared shelter.");
            if (serviceAvailable && !ventilationRunning) throw new ArgumentException("Service availability needs running ventilation.");
            Prepared = prepared;
            VentilationRunning = ventilationRunning;
            ServiceAvailable = serviceAvailable;
            Occupants = occupants;
        }
    }

    /// <summary>Protection of one member while a shelter instruction is in force (else NotRequired).</summary>
    public enum ProtectionState
    {
        NotRequired,
        Protected,
        NotInShelter,
        ShelterNotPrepared,
        ShelterServiceUnavailable
    }

    /// <summary>
    /// Radio coverage (configuration, immutable). Advisories reach the base communications equipment when issued.
    /// A member receives an issued, still valid advisory while inside the pressurized base or the airlock, or while
    /// outside within <see cref="ExteriorRangeMeters"/> of <see cref="Antenna"/> (straight-line distance from the
    /// confirmed position; terrain shadowing is not modelled). This is a scenario value, not a radio link budget.
    /// </summary>
    public sealed class CommsConfiguration
    {
        public BasePosition Antenna { get; }
        public double ExteriorRangeMeters { get; }

        public CommsConfiguration(BasePosition antenna, double exteriorRangeMeters)
        {
            Antenna = antenna;
            ExteriorRangeMeters = OutpostGuard.Positive(exteriorRangeMeters, nameof(exteriorRangeMeters));
        }

        public bool Covers(CrewZone zone, BasePosition confirmedPosition) =>
            zone != CrewZone.Exterior || confirmedPosition.DistanceTo(Antenna) <= ExteriorRangeMeters;
    }
}
