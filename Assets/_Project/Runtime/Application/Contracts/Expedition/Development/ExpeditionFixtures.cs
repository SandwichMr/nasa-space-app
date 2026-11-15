using System;
using System.Collections.Generic;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation;
using Trainer.Simulation.Eva;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Application.Development
{
    /// <summary>
    /// DEVELOPMENT ONLY. Compact synthetic phase 3 scenario and snapshot builder for the fake, D4 presentation work
    /// and contract tests. Every number is an illustrative fixture (see <see cref="Provenance"/>): not NASA data, not
    /// a suit, not a rate, limit or EVA procedure to reuse. The builder assembles states the fixture chooses; it does
    /// not simulate, move crew, cycle the airlock or decide results.
    /// </summary>
    public static class ExpeditionFixtures
    {
        public const string ScenarioId = "fixture-eva-outpost";
        public const string Provenance =
            "FIXTURE SINTÉTICA de desarrollo: base de 12 x 10 m con refugio, esclusa de 2 plazas y un exterior de 17 x 10 m. " +
            "Valores ilustrativos; no representan trajes, hardware, datos ni procedimientos de la NASA.";
        public const string EpisodeProvenance =
            "EPISODIO SINTÉTICO de desarrollo (autorado, reproducible, sin Internet). No es un evento real ni una dosis.";

        public static readonly StationId RackA = new StationId("suit-rack-a");
        public static readonly StationId RackB = new StationId("suit-rack-b");
        public static readonly StationId Charger = new StationId("suit-charger");
        public static readonly StationId Consumables = new StationId("consumables-panel");
        public static readonly StationId SuitCheck = new StationId("suit-check");
        public static readonly StationId WaterRecycler = new StationId("water-recycler");
        public static readonly StationId ShelterPanel = new StationId("shelter-panel");
        public static readonly StationId InnerPanel = new StationId("airlock-inner-panel");
        public static readonly StationId OuterPanel = new StationId("airlock-outer-panel");
        public static readonly StationId RegolithSite = new StationId("regolith-site");
        public static readonly StationId CraterRim = new StationId("crater-rim");

        public static readonly TaskTypeId TreatWater = new TaskTypeId("treat-water");
        public static readonly TaskTypeId DonA = new TaskTypeId("don-suit-a");
        public static readonly TaskTypeId DonB = new TaskTypeId("don-suit-b");
        public static readonly TaskTypeId ChargeSuit = new TaskTypeId("charge-suit");
        public static readonly TaskTypeId FillSuit = new TaskTypeId("fill-suit");
        public static readonly TaskTypeId CheckSuit = new TaskTypeId("check-suit");
        public static readonly TaskTypeId PrepareShelter = new TaskTypeId("prepare-shelter");
        public static readonly TaskTypeId CollectRegolith = new TaskTypeId("collect-regolith");
        public static readonly TaskTypeId CraterCore = new TaskTypeId("crater-core");

        public const string SuitA = "suit-a";
        public const string SuitB = "suit-b";
        public const string WarningId = "fx-warning";
        public const string EndId = "fx-end";

        public static readonly BaseRect Chamber = new BaseRect(12.3, 3.5, 15.0, 6.5);
        public static readonly BaseRect InnerHatch = new BaseRect(12.0, 4.2, 12.3, 5.8);
        public static readonly BaseRect OuterHatch = new BaseRect(15.0, 4.2, 15.3, 5.8);

        /// <summary>Synthetic suit model of the fixture (Battery J, O2 kg, feedwater kg; rates per simulated second).</summary>
        public static SuitModelConfiguration SuitModel() => new SuitModelConfiguration(new[]
        {
            new SuitServiceModel(SuitService.Oxygen, 0.40, 0.08, 0.0016, 0.0022, 0.0030),
            new SuitServiceModel(SuitService.Battery, 5000, 1000, 18, 20, 22),
            new SuitServiceModel(SuitService.Feedwater, 0.80, 0.16, 0.0012, 0.0028, 0.0045)
        }, Provenance);

        public static SuitLevels RackLevels() => new SuitLevels(0.12, 2600, 0);

        /// <summary>Synthetic episode: event 40–90 s (hidden), warning at 45 s (shelter by 75 s, valid until 110 s),
        /// explicit end of the shelter period at 100 s.</summary>
        public static ParticleEventEpisode Episode() => new ParticleEventEpisode("fixture-particle-episode", true,
            EpisodeProvenance, null, 40, 90, new[]
            {
                new AdvisoryMessage(WarningId, AdvisoryKind.ShelterInstruction, 45, 75, 110, null, "particle-warning"),
                new AdvisoryMessage(EndId, AdvisoryKind.InstructionEnd, 100, null, 400, WarningId, "shelter-period-end")
            });

        public static ExpeditionScenario Scenario(bool withEpisode = true)
        {
            var obstacles = new[]
            {
                new BaseRect(0, 3, 2, 3.3), new BaseRect(4, 3, 6, 3.3), new BaseRect(6, 0, 6.3, 3.3),      // shelter walls
                new BaseRect(12, 0, 12.3, 4.2), new BaseRect(12, 5.8, 12.3, 10),                          // base east wall
                new BaseRect(12.3, 0, 15.3, 3.5), new BaseRect(12.3, 6.5, 15.3, 10),                      // airlock module
                new BaseRect(15, 3.5, 15.3, 4.2), new BaseRect(15, 5.8, 15.3, 6.5),                       // outer wall
                new BaseRect(24, 1, 26, 3.5), new BaseRect(25.5, 6, 27, 8)                                // rocks
            };
            var stations = new[]
            {
                new StationPlacement(RackA, new BasePosition(1.5, 8.8), 1.0),
                new StationPlacement(RackB, new BasePosition(3.5, 8.8), 1.0),
                new StationPlacement(Charger, new BasePosition(5.5, 8.8), 1.0),
                new StationPlacement(Consumables, new BasePosition(7.5, 8.8), 1.0),
                new StationPlacement(SuitCheck, new BasePosition(10, 7.5), 1.0),
                new StationPlacement(WaterRecycler, new BasePosition(10, 1.5), 1.2),
                new StationPlacement(ShelterPanel, new BasePosition(1.5, 1.0), 1.2),
                new StationPlacement(InnerPanel, new BasePosition(12.15, 5.0), 1.2),
                new StationPlacement(OuterPanel, new BasePosition(15.15, 5.0), 1.2),
                new StationPlacement(RegolithSite, new BasePosition(22, 5), 1.4),
                new StationPlacement(CraterRim, new BasePosition(29, 8.5), 1.4)
            };
            var spawns = new[] { new BasePosition(8, 4.5), new BasePosition(9.5, 4.5), new BasePosition(8, 6), new BasePosition(9.5, 6) };
            var layout = new OutpostLayout(new BaseRect(0, 0, 32, 10), obstacles, stations, spawns);
            var treat = new ResourceEffect(new[]
            {
                new ResourceChange(ResourceKind.UntreatedWater, -2.0), new ResourceChange(ResourceKind.PotableWater, 1.8)
            }, 0.2);
            var tasks = new[]
            {
                new TaskDefinition(TreatWater, WaterRecycler, TaskCategory.Support, 20, 150, treat),
                new TaskDefinition(DonA, RackA, TaskCategory.Support, 5, 0, ResourceEffect.None),
                new TaskDefinition(DonB, RackB, TaskCategory.Support, 5, 0, ResourceEffect.None),
                new TaskDefinition(ChargeSuit, Charger, TaskCategory.Support, 12, 200, ResourceEffect.None),
                new TaskDefinition(FillSuit, Consumables, TaskCategory.Support, 6, 0, ResourceEffect.None),
                new TaskDefinition(CheckSuit, SuitCheck, TaskCategory.Support, 5, 0, ResourceEffect.None),
                new TaskDefinition(PrepareShelter, ShelterPanel, TaskCategory.Support, 6, 30, ResourceEffect.None),
                new TaskDefinition(CollectRegolith, RegolithSite, TaskCategory.Support, 15, 0, ResourceEffect.None),
                new TaskDefinition(CraterCore, CraterRim, TaskCategory.Support, 20, 0, ResourceEffect.None)
            };
            var simulation = new OutpostConfiguration(new PowerConfiguration(12000, 20000, 80, 60, 60, 100, 400),
                new LifeSupportConfiguration(new WaterStock(20, 6, 40, 10), new FoodStock(15, 30), new CabinConfiguration(40, 295, 21, 0.4)));
            var outpost = new OutpostScenario(ScenarioId, Provenance, layout, new CrewMovementSettings(2.0, 0.35), tasks, simulation);

            var zones = new[]
            {
                new ZoneRegion(CrewZone.Shelter, new BaseRect(0, 0, 6, 3)),
                new ZoneRegion(CrewZone.Airlock, Chamber),
                new ZoneRegion(CrewZone.Exterior, new BaseRect(15, 0, 32, 10)),
                new ZoneRegion(CrewZone.Interior, new BaseRect(0, 0, 12.3, 10))
            };
            var airlock = new AirlockConfiguration(Chamber, InnerHatch, OuterHatch, InnerPanel, OuterPanel, 2, 8, 8, 120, 0.30,
                new BasePosition(15.7, 5.0));
            var eva = new EvaConfiguration(SuitModel(),
                new[] { new SuitDefinition(SuitA, RackLevels()), new SuitDefinition(SuitB, RackLevels()) },
                new EvaSupplyConfiguration(2.0, 2.0, 3.0, 3.0));
            var rules = new[]
            {
                ExpeditionTaskRule.Don(DonA, SuitA), ExpeditionTaskRule.Don(DonB, SuitB),
                ExpeditionTaskRule.ChargeBattery(ChargeSuit, 1.0), ExpeditionTaskRule.FillConsumables(FillSuit),
                ExpeditionTaskRule.SealCheck(CheckSuit), ExpeditionTaskRule.PrepareShelter(PrepareShelter),
                ExpeditionTaskRule.Exterior(CollectRegolith, 10), ExpeditionTaskRule.Exterior(CraterCore, 20)
            };
            var mission = new ExpeditionMissionRules(200, new[]
            {
                new ExpeditionObjectiveRule("crew-safe", ObjectiveRole.Safety, Array.Empty<TaskTypeId>(), 1),
                new ExpeditionObjectiveRule("regolith-sample", ObjectiveRole.Primary, new[] { CollectRegolith }, 1),
                new ExpeditionObjectiveRule("crater-core", ObjectiveRole.Optional, new[] { CraterCore }, 1)
            });
            return new ExpeditionScenario(outpost, zones, airlock,
                new[] { new SuitRack(SuitA, RackA), new SuitRack(SuitB, RackB) }, eva, rules,
                new ShelterConfiguration(ShelterPanel, PrepareShelter, 40), new CommsConfiguration(new BasePosition(13.5, 9.0), 12),
                withEpisode ? Episode() : null, mission, 0.6);
        }

        /// <summary>Ready at revision 0 of <paramref name="epoch"/>: <paramref name="players"/> idle at the spawn points of
        /// slots 0, 1, ..., inside, without suits; suits on their racks; airlock pressurized; no advisory.</summary>
        public static ExpeditionSessionSnapshot Initial(ExpeditionScenario scenario, long epoch, params PlayerId[] players)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (players == null) throw new ArgumentNullException(nameof(players));
            OutpostSessionSnapshot outpost = OutpostFixtures.Initial(scenario.Outpost, epoch, players);
            return new ExpeditionSnapshotBuilder(scenario, outpost, epoch).Build();
        }

        /// <summary>A builder starting from <paramref name="previous"/> whose Build has Revision + 1.</summary>
        public static ExpeditionSnapshotBuilder Edit(ExpeditionScenario scenario, ExpeditionSessionSnapshot previous) =>
            new ExpeditionSnapshotBuilder(scenario, previous);
    }

    /// <summary>
    /// DEVELOPMENT ONLY. Mutable copy of a phase 3 snapshot for fixtures. Airlock occupants are derived from the
    /// members' zones and the phase 2 objectives from the phase 3 ones, so a fixture cannot publish those
    /// inconsistently; everything else is checked by the snapshot constructor at <see cref="Build"/>.
    /// </summary>
    public sealed class ExpeditionSnapshotBuilder
    {
        private readonly ExpeditionScenario scenario;
        private readonly long epoch;
        private readonly long revision;
        private readonly string scenarioId;
        private SessionState state;
        private ExpeditionOutcome outcome;
        private readonly List<CrewMemberState> poses;
        private readonly List<ExpeditionCrewState> crew;
        private readonly List<StationState> stations;
        private OutpostSimulationSnapshot resources;
        private readonly List<SuitState> suits;
        private AirlockState airlockState;
        private long cycleSequence;
        private double cycleWork, cycleDuration;
        private bool cycleStalled;
        private PlayerId cycleRequestedBy;
        private ShelterStatus shelter;
        private EvaSupplyReading supply;
        private readonly List<IssuedAdvisory> advisories;
        private readonly List<AdvisoryReception> receptions;
        private ShelterInstructionStatus instruction;
        private ExpeditionPhase phase;
        private ReturnDecision decision;
        private readonly List<ExpeditionObjective> objectives;
        private readonly List<ProtectionAssessment> assessments;
        private readonly List<ExpeditionIncident> incidents;
        private EventDisclosure disclosure;

        internal ExpeditionSnapshotBuilder(ExpeditionScenario scenario, OutpostSessionSnapshot initial, long epoch)
        {
            this.scenario = scenario;
            this.epoch = epoch;
            revision = 0;
            scenarioId = initial.ScenarioId;
            state = initial.State;
            outcome = ExpeditionOutcome.InProgress;
            poses = new List<CrewMemberState>(initial.Crew);
            crew = new List<ExpeditionCrewState>();
            foreach (CrewMemberState member in initial.Crew)
                crew.Add(new ExpeditionCrewState(member.Player, CrewZone.Interior, 0, -1, false, SuitActivity.Rest,
                    ReturnMarginEstimate.NotApplicable(0), ProtectionState.NotRequired));
            stations = new List<StationState>(initial.Stations);
            resources = initial.Resources;
            suits = new List<SuitState>();
            for (int i = 0; i < scenario.Eva.Suits.Count; i++)
            {
                SuitDefinition suit = scenario.Eva.Suits[i];
                suits.Add(new SuitState(i, suit.Key, SuitStatus.Stowed, PlayerId.None, ChecklistSteps.None,
                    SuitReading.From(i, suit.Key, suit.Initial, false, scenario.Eva.Model)));
            }
            airlockState = AirlockState.PressurizedInnerOpen;
            shelter = new ShelterStatus(false, false, false, 0);
            EvaSupplyConfiguration s = scenario.Eva.Supply;
            supply = new EvaSupplyReading(s.OxygenRechargeKilograms, s.MakeupGasKilograms);
            advisories = new List<IssuedAdvisory>();
            receptions = new List<AdvisoryReception>();
            instruction = ShelterInstructionStatus.None;
            phase = ExpeditionPhase.Preparing;
            objectives = new List<ExpeditionObjective>();
            foreach (ExpeditionObjectiveRule rule in scenario.Rules.Objectives)
                objectives.Add(new ExpeditionObjective(rule.Key, rule.Role, 0, rule.TargetCount));
            assessments = new List<ProtectionAssessment>();
            incidents = new List<ExpeditionIncident>();
        }

        internal ExpeditionSnapshotBuilder(ExpeditionScenario scenario, ExpeditionSessionSnapshot previous)
        {
            this.scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            epoch = previous.Epoch;
            revision = checked(previous.Revision + 1);
            scenarioId = previous.Outpost.ScenarioId;
            state = previous.Outpost.State;
            outcome = previous.Outcome;
            poses = new List<CrewMemberState>(previous.Outpost.Crew);
            crew = new List<ExpeditionCrewState>(previous.Crew);
            stations = new List<StationState>(previous.Outpost.Stations);
            resources = previous.Outpost.Resources;
            suits = new List<SuitState>(previous.Suits);
            airlockState = previous.Airlock.State;
            cycleSequence = previous.Airlock.CycleSequence;
            cycleWork = previous.Airlock.CycleWorkSeconds;
            cycleDuration = previous.Airlock.CycleDurationSeconds;
            cycleStalled = previous.Airlock.Stalled;
            cycleRequestedBy = previous.Airlock.CycleRequestedBy;
            shelter = previous.Shelter;
            supply = previous.Supply;
            advisories = new List<IssuedAdvisory>(previous.Advisories);
            receptions = new List<AdvisoryReception>(previous.Receptions);
            instruction = previous.Instruction;
            phase = previous.Phase;
            decision = previous.Decision;
            objectives = new List<ExpeditionObjective>(previous.Objectives);
            assessments = new List<ProtectionAssessment>(previous.Assessments);
            incidents = new List<ExpeditionIncident>(previous.Incidents);
            disclosure = previous.Disclosure;
        }

        public double Now => resources.Power.SimulatedTimeSeconds;

        /// <summary>Moves the mission clock (the phase 2 resources' power time) to <paramref name="seconds"/>.</summary>
        public ExpeditionSnapshotBuilder AtTime(double seconds)
        {
            PowerSnapshot p = resources.Power;
            resources = new OutpostSimulationSnapshot(new PowerSnapshot(seconds, p.EnergyJoules, p.CapacityJoules,
                p.GenerationWatts, p.EssentialRequestedWatts, p.EssentialServedWatts, p.ScienceRequestedWatts,
                p.ScienceServedWatts), resources.ServiceLoadRequestedWatts, resources.ServiceLoadServedWatts, resources.LifeSupport);
            return this;
        }

        public ExpeditionSnapshotBuilder Resources(OutpostSimulationSnapshot value)
        {
            resources = value ?? throw new ArgumentNullException(nameof(value));
            return this;
        }

        /// <summary>Running/Paused/Ready (in progress). Use <see cref="Ended"/> for a result.</summary>
        public ExpeditionSnapshotBuilder Session(SessionState value)
        {
            if (value == SessionState.Completed || value == SessionState.Interrupted)
                throw new ArgumentException("Use Ended for a result.", nameof(value));
            state = value;
            outcome = ExpeditionOutcome.InProgress;
            if (phase == ExpeditionPhase.Closed) phase = ExpeditionPhase.Preparing;
            return this;
        }

        /// <summary>Ends the session with <paramref name="value"/>: phase 2 state/outcome follow the single mapping,
        /// the phase becomes Closed and the episode (if any) is disclosed.</summary>
        public ExpeditionSnapshotBuilder Ended(ExpeditionOutcome value)
        {
            if (value == ExpeditionOutcome.InProgress) throw new ArgumentException("Not an end.", nameof(value));
            OutpostOutcome mapped = ExpeditionOutcomes.ToOutpostOutcome(value);
            state = mapped == OutpostOutcome.EssentialServiceLost || mapped == OutpostOutcome.SimulationFault
                ? SessionState.Interrupted : SessionState.Completed;
            outcome = value;
            phase = ExpeditionPhase.Closed;
            if (scenario.Episode != null) disclosure = EventDisclosure.Of(scenario.Episode);
            for (int i = 0; i < stations.Count; i++)
                if (stations[i].ActiveTask != null)
                    stations[i] = new StationState(stations[i].Station, StationAvailability.Available, null,
                        new TaskEnd(stations[i].ActiveTask.Task, stations[i].ActiveTask.Performer, TaskEndReason.MissionEnded, Now));
            return this;
        }

        /// <summary>Changes one present member. A zone change increments its zone sequence; a pose change keeps the
        /// slot and input sequence.</summary>
        public ExpeditionSnapshotBuilder Member(PlayerId player, BasePosition? position = null, CrewZone? zone = null,
            int? suitIndex = null, bool? onSuitSupply = null, SuitActivity? activity = null, ReturnMarginEstimate margin = null,
            ProtectionState? protection = null, CrewMotion? motion = null)
        {
            int i = crew.FindIndex(m => m.Player == player);
            if (i < 0) throw new ArgumentException($"{player} is not in the snapshot.", nameof(player));
            ExpeditionCrewState c = crew[i];
            CrewZone nextZone = zone ?? c.Zone;
            bool supplyNow = onSuitSupply ?? c.OnSuitSupply;
            ReturnMarginEstimate nextMargin = margin ?? (supplyNow == c.OnSuitSupply ? c.Margin
                : supplyNow ? throw new ArgumentException("Give a margin when a member goes on suit supply.", nameof(margin))
                : ReturnMarginEstimate.NotApplicable(Now));
            crew[i] = new ExpeditionCrewState(player, nextZone, nextZone != c.Zone ? c.ZoneSequence + 1 : c.ZoneSequence,
                suitIndex ?? c.SuitIndex, supplyNow, activity ?? c.Activity, nextMargin, protection ?? c.Protection);
            if (position.HasValue || motion.HasValue)
            {
                int p = poses.FindIndex(m => m.Player == player);
                CrewMemberState pose = poses[p];
                var at = position ?? pose.Pose.Position;
                poses[p] = new CrewMemberState(player, pose.Slot, new CrewPose(at, pose.Pose.Facing, motion ?? pose.Pose.Motion),
                    pose.LastInputSequence);
            }
            return this;
        }

        /// <summary>Changes one suit; <paramref name="levels"/> default to the current reading's levels.</summary>
        public ExpeditionSnapshotBuilder Suit(int index, SuitStatus status, PlayerId wearer, ChecklistSteps completed,
            SuitLevels levels = null)
        {
            SuitState current = suits[index];
            SuitLevels next = levels ?? current.Reading.Levels;
            suits[index] = new SuitState(index, current.Key, status, wearer, completed,
                SuitReading.From(index, current.Key, next, status == SuitStatus.Sealed, scenario.Eva.Model));
            return this;
        }

        public ExpeditionSnapshotBuilder Station(StationState value)
        {
            int i = stations.FindIndex(s => s.Station == value.Station);
            if (i < 0) throw new ArgumentException($"Station '{value.Station}' is not in the snapshot.", nameof(value));
            stations[i] = value;
            return this;
        }

        /// <summary>Airlock state; occupants are derived from the members in zone Airlock at Build.</summary>
        public ExpeditionSnapshotBuilder Airlock(AirlockState value, long sequence, double work = 0, double duration = 0,
            bool stalled = false, PlayerId requestedBy = default)
        {
            airlockState = value;
            cycleSequence = sequence;
            cycleWork = work;
            cycleDuration = duration;
            cycleStalled = stalled;
            cycleRequestedBy = requestedBy;
            return this;
        }

        public ExpeditionSnapshotBuilder Shelter(ShelterStatus value) { shelter = value ?? throw new ArgumentNullException(nameof(value)); return this; }
        public ExpeditionSnapshotBuilder Supply(EvaSupplyReading value) { supply = value ?? throw new ArgumentNullException(nameof(value)); return this; }

        /// <summary>Publishes an issued message (must be issued at or before <see cref="Now"/>) with a validity.</summary>
        public ExpeditionSnapshotBuilder Issue(AdvisoryMessage message, AdvisoryValidity validity = AdvisoryValidity.Current)
        {
            int i = advisories.FindIndex(a => a.Message.Id == message.Id);
            var issued = new IssuedAdvisory(message, validity);
            if (i < 0) advisories.Add(issued);
            else advisories[i] = issued;
            return this;
        }

        public ExpeditionSnapshotBuilder Receive(string messageId, PlayerId player, double atSeconds)
        {
            IssuedAdvisory advisory = advisories.Find(a => a.Message.Id == messageId)
                ?? throw new ArgumentException("Issue the message first.", nameof(messageId));
            bool late = advisory.Message.ShelterBySeconds.HasValue && atSeconds > advisory.Message.ShelterBySeconds.Value;
            receptions.Add(new AdvisoryReception(messageId, player, atSeconds, late));
            return this;
        }

        public ExpeditionSnapshotBuilder Instruction(ShelterInstructionStatus value) { instruction = value ?? throw new ArgumentNullException(nameof(value)); return this; }
        public ExpeditionSnapshotBuilder Phase(ExpeditionPhase value) { phase = value; return this; }
        public ExpeditionSnapshotBuilder Decision(ReturnDecision value) { decision = value; return this; }

        public ExpeditionSnapshotBuilder Objective(string key, int current)
        {
            int i = objectives.FindIndex(o => o.Key == key);
            if (i < 0) throw new ArgumentException($"Unknown objective '{key}'.", nameof(key));
            objectives[i] = new ExpeditionObjective(key, objectives[i].Role, current, objectives[i].Target);
            return this;
        }

        public ExpeditionSnapshotBuilder Assessment(ProtectionAssessment value) { assessments.Add(value); return this; }
        public ExpeditionSnapshotBuilder Incident(ExpeditionIncident value) { incidents.Add(value); return this; }

        public ExpeditionSessionSnapshot Build()
        {
            var occupants = new List<PlayerId>();
            int sheltered = 0;
            foreach (ExpeditionCrewState member in crew)
            {
                if (member.Zone == CrewZone.Airlock) occupants.Add(member.Player);
                if (member.Zone == CrewZone.Shelter) sheltered++;
            }
            var airlock = new AirlockStatus(airlockState, cycleSequence, cycleWork, cycleDuration, cycleStalled, occupants, cycleRequestedBy);
            var shelterNow = new ShelterStatus(shelter.Prepared, shelter.VentilationRunning, shelter.ServiceAvailable, sheltered);
            var compatibility = new List<ObjectiveProgress>();
            foreach (ExpeditionObjective objective in objectives)
                compatibility.Add(new ObjectiveProgress(objective.Key, objective.Current, objective.Target, ProgressUnit.Count,
                    objective.Role == ObjectiveRole.Safety));
            var outpost = new OutpostSessionSnapshot(epoch, revision, state, ExpeditionOutcomes.ToOutpostOutcome(outcome),
                scenarioId, poses, stations, resources, compatibility);
            return new ExpeditionSessionSnapshot(outpost, crew, suits, airlock, shelterNow, supply, advisories, receptions,
                instruction, phase, decision, outcome, objectives, assessments, incidents, disclosure);
        }
    }
}
