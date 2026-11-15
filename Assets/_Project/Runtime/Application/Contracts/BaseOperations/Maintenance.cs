using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;

namespace Trainer.Application.BaseOperations
{
    /// <summary>What an inspection (or a batch/test reading) revealed. None on a current observation = "nothing found".</summary>
    [Flags]
    public enum Findings
    {
        None = 0,
        /// <summary>Held product conductivity above the scenario acceptance limit.</summary>
        ProductOffSpec = 1,
        /// <summary>Processor filter restricted (the synthetic fault).</summary>
        ProcessorFilterRestricted = 2,
        /// <summary>The processor cannot process until repaired.</summary>
        ProcessorFailed = 4,
        /// <summary>Active sorbent bed above its knee: CO2 removal is reduced.</summary>
        ActiveBedSaturated = 8,
        /// <summary>Standby bed not regenerated: switching to it would not help.</summary>
        StandbyBedLoaded = 16,
        /// <summary>Air inlet flow below nominal.</summary>
        InletFlowRestricted = 32
    }

    /// <summary>What the crew knows of a machine's condition (from current observations), never the hidden truth.</summary>
    public enum EquipmentKnowledge { Unknown, Nominal, Degraded, Failed }

    /// <summary>Instrument value inside an observation, with a stable key for texts ("product-conductivity",
    /// "active-bed-loading", "standby-bed-loading", "inlet-flow").</summary>
    public sealed class ObservedReading
    {
        public string Key { get; }
        public InstrumentReading Reading { get; }

        public ObservedReading(string key, InstrumentReading reading)
        {
            Key = StableKey.Require(key, nameof(key));
            Reading = reading ?? throw new ArgumentNullException(nameof(reading));
        }
    }

    /// <summary>
    /// One observation of a machine (immutable): who looked, when, what was found and the instrument values. Decisions
    /// state the <see cref="Sequence"/> they were based on; the authority refuses a decision whose observation is no
    /// longer <see cref="Current"/> (StaleObservation). An observation stops being current when the machine's state
    /// sequence changes or after the scenario validity window.
    /// </summary>
    public sealed class EquipmentObservation
    {
        public long Sequence { get; }
        public EquipmentKind Equipment { get; }
        public PlayerId ObservedBy { get; }
        public double ObservedAtSeconds { get; }
        public double ValidUntilSeconds { get; }
        /// <summary>Equipment state sequence at the time of the observation.</summary>
        public long StateSequence { get; }
        public Findings Findings { get; }
        public IReadOnlyList<ObservedReading> Readings { get; }
        public bool Current { get; }

        public EquipmentObservation(long sequence, EquipmentKind equipment, PlayerId observedBy, double observedAtSeconds,
            double validUntilSeconds, long stateSequence, Findings findings, IEnumerable<ObservedReading> readings, bool current)
        {
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            Sequence = sequence;
            Equipment = OutpostGuard.Defined(equipment, nameof(equipment));
            ObservedBy = observedBy;
            ObservedAtSeconds = OutpostGuard.NonNegative(observedAtSeconds, nameof(observedAtSeconds));
            ValidUntilSeconds = OutpostGuard.NonNegative(validUntilSeconds, nameof(validUntilSeconds));
            if (validUntilSeconds <= observedAtSeconds) throw new ArgumentException("Valid for a positive window.", nameof(validUntilSeconds));
            if (stateSequence < 0) throw new ArgumentOutOfRangeException(nameof(stateSequence));
            StateSequence = stateSequence;
            const Findings water = Findings.ProductOffSpec | Findings.ProcessorFilterRestricted | Findings.ProcessorFailed;
            const Findings air = Findings.ActiveBedSaturated | Findings.StandbyBedLoaded | Findings.InletFlowRestricted;
            if ((findings & ~(water | air)) != 0) throw new ArgumentOutOfRangeException(nameof(findings));
            if ((findings & (equipment == EquipmentKind.WaterProcessor ? air : water)) != 0)
                throw new ArgumentException("Findings of another machine.", nameof(findings));
            Findings = findings;
            if (readings == null) throw new ArgumentNullException(nameof(readings));
            var copy = new List<ObservedReading>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (ObservedReading reading in readings)
            {
                if (reading == null) throw new ArgumentException("Null reading.", nameof(readings));
                if (!keys.Add(reading.Key)) throw new ArgumentException($"Duplicate reading '{reading.Key}'.", nameof(readings));
                if (reading.Reading.ObservedAtSeconds.HasValue && reading.Reading.ObservedAtSeconds.Value > observedAtSeconds + 1e-6)
                    throw new ArgumentException("A reading cannot come from after the observation.", nameof(readings));
                copy.Add(reading);
            }
            if (copy.Count > BaseOperationsLimits.MaxReadingsPerObservation) throw new ArgumentException("Too many readings.", nameof(readings));
            Readings = new ReadOnlyCollection<ObservedReading>(copy.ToArray());
            Current = current;
        }

        public bool TryGetReading(string key, out InstrumentReading reading)
        {
            foreach (ObservedReading candidate in Readings)
                if (candidate.Key == key)
                {
                    reading = candidate.Reading;
                    return true;
                }
            reading = null;
            return false;
        }
    }

    /// <summary>Steps of the repair sequence of a machine (flags of completed steps).</summary>
    [Flags]
    public enum RepairSteps
    {
        None = 0,
        Inspect = 1,
        Isolate = 2,
        Intervene = 4,
        Test = 8,
        Restore = 16
    }

    /// <summary>
    /// The single ordering rule of the repair sequence: Inspect → Isolate → Intervene → Test → Restore. Test may be repeated
    /// and Intervene may follow a failed Test; Restore needs a passed Test. Nothing heals a machine from elsewhere or skips
    /// the isolation of an intervention that needs it.
    /// </summary>
    public static class RepairSequence
    {
        /// <summary>True when <paramref name="step"/> may be done after <paramref name="completed"/> given the last test result.</summary>
        public static bool IsAllowed(RepairSteps completed, RepairSteps step, bool? lastTestPassed)
        {
            switch (step)
            {
                case RepairSteps.Inspect: return (completed & RepairSteps.Isolate) == 0;
                case RepairSteps.Isolate: return completed == RepairSteps.Inspect;
                case RepairSteps.Intervene:
                    return (completed & RepairSteps.Isolate) != 0 && (completed & RepairSteps.Restore) == 0
                        && ((completed & RepairSteps.Intervene) == 0 || lastTestPassed == false);
                case RepairSteps.Test: return (completed & RepairSteps.Intervene) != 0 && (completed & RepairSteps.Restore) == 0;
                case RepairSteps.Restore: return (completed & RepairSteps.Test) != 0 && lastTestPassed == true;
                default: return false;
            }
        }
    }

    /// <summary>A repair in progress on one machine (immutable). Open from the Isolate step until Restore.</summary>
    public sealed class RepairJob
    {
        public EquipmentKind Equipment { get; }
        public RepairSteps Completed { get; }
        public double StartedAtSeconds { get; }
        /// <summary>Observation that justified the isolation.</summary>
        public long ObservationSequence { get; }
        /// <summary>Result of the last Test, or null before any.</summary>
        public bool? LastTestPassed { get; }

        public RepairJob(EquipmentKind equipment, RepairSteps completed, double startedAtSeconds, long observationSequence, bool? lastTestPassed)
        {
            Equipment = OutpostGuard.Defined(equipment, nameof(equipment));
            if ((completed & ~(RepairSteps.Inspect | RepairSteps.Isolate | RepairSteps.Intervene | RepairSteps.Test | RepairSteps.Restore)) != 0)
                throw new ArgumentOutOfRangeException(nameof(completed));
            if ((completed & (RepairSteps.Inspect | RepairSteps.Isolate)) != (RepairSteps.Inspect | RepairSteps.Isolate))
                throw new ArgumentException("A repair job starts at Isolate, after an inspection.", nameof(completed));
            if ((completed & RepairSteps.Restore) != 0) throw new ArgumentException("A restored machine has no open job.", nameof(completed));
            if (lastTestPassed.HasValue != ((completed & RepairSteps.Test) != 0))
                throw new ArgumentException("A test result exists exactly after a Test.", nameof(lastTestPassed));
            Completed = completed;
            StartedAtSeconds = OutpostGuard.NonNegative(startedAtSeconds, nameof(startedAtSeconds));
            if (observationSequence < 1) throw new ArgumentOutOfRangeException(nameof(observationSequence));
            ObservationSequence = observationSequence;
            LastTestPassed = lastTestPassed;
        }
    }

    /// <summary>Published state of one machine (immutable): operating state, what is known and what is visible.</summary>
    public sealed class EquipmentStatus
    {
        public EquipmentKind Equipment { get; }
        public StationId Station { get; }
        public EquipmentOperatingState Operating { get; }
        public EquipmentKnowledge Knowledge { get; }
        /// <summary>Symptoms visible without inspection (for example ProductOffSpec from the batch reading, ProcessorFailed
        /// when a batch is refused). Subset of the findings an inspection would give.</summary>
        public Findings VisibleSymptoms { get; }
        /// <summary>Increases on every state change of the machine (condition, operating state, beds, filter).</summary>
        public long StateSequence { get; }
        public EquipmentObservation LatestObservation { get; }
        public RepairJob Repair { get; }

        public EquipmentStatus(EquipmentKind equipment, StationId station, EquipmentOperatingState operating,
            EquipmentKnowledge knowledge, Findings visibleSymptoms, long stateSequence, EquipmentObservation latestObservation, RepairJob repair)
        {
            Equipment = OutpostGuard.Defined(equipment, nameof(equipment));
            if (!station.IsValid) throw new ArgumentException("Station required.", nameof(station));
            Station = station;
            Operating = OutpostGuard.Defined(operating, nameof(operating));
            Knowledge = OutpostGuard.Defined(knowledge, nameof(knowledge));
            VisibleSymptoms = visibleSymptoms;
            if (stateSequence < 0) throw new ArgumentOutOfRangeException(nameof(stateSequence));
            StateSequence = stateSequence;
            if (latestObservation != null)
            {
                if (latestObservation.Equipment != equipment) throw new ArgumentException("Observation of another machine.", nameof(latestObservation));
                if (latestObservation.StateSequence > stateSequence) throw new ArgumentException("Observation from a future state.", nameof(latestObservation));
                if (latestObservation.Current && latestObservation.StateSequence != stateSequence)
                    throw new ArgumentException("A current observation matches the present state sequence.", nameof(latestObservation));
            }
            LatestObservation = latestObservation;
            if (repair != null && repair.Equipment != equipment) throw new ArgumentException("Repair of another machine.", nameof(repair));
            if ((repair != null) != (operating == EquipmentOperatingState.Isolated))
                throw new ArgumentException("A machine is isolated exactly while a repair job is open.", nameof(repair));
            Repair = repair;
        }
    }

    /// <summary>Verification state of the potable tank. Suspect after off-spec product was routed to it; a Recheck moves the
    /// suspect mass back to untreated. No health effect is modelled.</summary>
    public enum PotableQuality { Verified, Suspect }

    /// <summary>Published water treatment detail (immutable).</summary>
    public sealed class WaterTreatmentStatus
    {
        public double HoldKilograms { get; }
        public InstrumentReading ProductConductivity { get; }
        public double ProductSpecLimitMicrosiemens { get; }
        public PotableQuality PotableQuality { get; }
        /// <summary>Mass of off-spec product routed to potable and not yet rechecked (bounded consequence), kg.</summary>
        public double SuspectKilograms { get; }
        public long BatchesProcessed { get; }

        public WaterTreatmentStatus(double holdKilograms, InstrumentReading productConductivity, double productSpecLimitMicrosiemens,
            PotableQuality potableQuality, double suspectKilograms, long batchesProcessed)
        {
            HoldKilograms = OutpostGuard.NonNegative(holdKilograms, nameof(holdKilograms));
            ProductConductivity = productConductivity ?? throw new ArgumentNullException(nameof(productConductivity));
            if (productConductivity.Unit != ReadingUnit.MicrosiemensPerCentimeter) throw new ArgumentException("Conductivity expected.", nameof(productConductivity));
            ProductSpecLimitMicrosiemens = OutpostGuard.Positive(productSpecLimitMicrosiemens, nameof(productSpecLimitMicrosiemens));
            PotableQuality = OutpostGuard.Defined(potableQuality, nameof(potableQuality));
            SuspectKilograms = OutpostGuard.NonNegative(suspectKilograms, nameof(suspectKilograms));
            if ((potableQuality == PotableQuality.Suspect) != (suspectKilograms > 0d))
                throw new ArgumentException("Suspect exactly while suspect mass remains.", nameof(suspectKilograms));
            if (batchesProcessed < 0) throw new ArgumentOutOfRangeException(nameof(batchesProcessed));
            BatchesProcessed = batchesProcessed;
        }

        /// <summary>True when the held product reading is valid and within the limit (the routing decision rule).</summary>
        public bool ProductWithinLimit =>
            ProductConductivity.Status == Trainer.Simulation.LifeSupport.ReadingStatus.Valid
            && ProductConductivity.Value.Value <= ProductSpecLimitMicrosiemens;
    }

    /// <summary>How the authority judged a decision against the truth at that moment (for the explanation, not a score).</summary>
    public enum DecisionAssessment
    {
        /// <summary>Indicated by the situation.</summary>
        Appropriate,
        /// <summary>Accepted but it did not address the situation (time, energy or a spare spent; no harm).</summary>
        NotIndicated,
        /// <summary>Bounded, recoverable consequence (for example off-spec product in the potable tank).</summary>
        Harmful
    }

    /// <summary>One accepted decision and its explanation key (immutable).</summary>
    public sealed class DecisionRecord
    {
        public Guid CommandId { get; }
        public PlayerId Performer { get; }
        public BaseOperationAction Action { get; }
        public OperationChoice Choice { get; }
        public EquipmentKind Equipment { get; }
        public double AtSeconds { get; }
        public DecisionAssessment Assessment { get; }
        /// <summary>Stable key of the explanation text (for example "accepted-off-spec-product", "bed-switch-restored-removal").</summary>
        public string ExplanationKey { get; }

        public DecisionRecord(Guid commandId, PlayerId performer, BaseOperationAction action, OperationChoice choice,
            EquipmentKind equipment, double atSeconds, DecisionAssessment assessment, string explanationKey)
        {
            if (commandId == Guid.Empty) throw new ArgumentException("Command id required.", nameof(commandId));
            CommandId = commandId;
            Performer = performer;
            Action = OutpostGuard.Defined(action, nameof(action));
            Choice = OutpostGuard.Defined(choice, nameof(choice));
            Equipment = OutpostGuard.Defined(equipment, nameof(equipment));
            AtSeconds = OutpostGuard.NonNegative(atSeconds, nameof(atSeconds));
            Assessment = OutpostGuard.Defined(assessment, nameof(assessment));
            ExplanationKey = StableKey.Require(explanationKey, nameof(explanationKey));
        }
    }

    /// <summary>A timed station operation in progress (immutable). It occupies its performer and its station exactly like a
    /// phase 2 task; the phase 2 station is published Unavailable meanwhile.</summary>
    public sealed class OperationProgress
    {
        public Guid CommandId { get; }
        public StationId Station { get; }
        public BaseOperationAction Action { get; }
        public OperationChoice Choice { get; }
        public PlayerId Performer { get; }
        public double WorkSeconds { get; }
        public double DurationSeconds { get; }
        public bool Stalled { get; }
        /// <summary>Spare reserved at start and consumed only at completion (released on cancel/leave), or null.</summary>
        public string ReservedSpare { get; }
        public double Fraction => WorkSeconds / DurationSeconds;

        public OperationProgress(Guid commandId, StationId station, BaseOperationAction action, OperationChoice choice, PlayerId performer,
            double workSeconds, double durationSeconds, bool stalled, string reservedSpare)
        {
            if (commandId == Guid.Empty) throw new ArgumentException("Command id required.", nameof(commandId));
            if (!station.IsValid) throw new ArgumentException("Station required.", nameof(station));
            if (!performer.IsValid) throw new ArgumentException("Performer required.", nameof(performer));
            CommandId = commandId;
            Station = station;
            Action = OutpostGuard.Defined(action, nameof(action));
            Choice = OutpostGuard.Defined(choice, nameof(choice));
            Performer = performer;
            DurationSeconds = OutpostGuard.Positive(durationSeconds, nameof(durationSeconds));
            WorkSeconds = OutpostGuard.NonNegative(workSeconds, nameof(workSeconds));
            if (workSeconds > durationSeconds) throw new ArgumentOutOfRangeException(nameof(workSeconds), "Work exceeds duration.");
            Stalled = stalled;
            if (reservedSpare != null) StableKey.Require(reservedSpare, nameof(reservedSpare));
            ReservedSpare = reservedSpare;
        }
    }
}
