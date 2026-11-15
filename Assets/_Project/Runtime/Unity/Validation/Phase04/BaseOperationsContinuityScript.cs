using System;
using System.Collections.Generic;
using System.Globalization;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;

namespace Trainer.Unity.Validation.Phase04
{
    /// <summary>Which half of the base a scripted player looks after.</summary>
    public enum ContinuityRole
    {
        /// <summary>Water recycler: batches, routing decisions, recheck, and its repair (Inspect → Isolate → Intervene → Test → Restore).</summary>
        Water,
        /// <summary>Air unit (bed service and its repair) and logistics (orders and unloading).</summary>
        AirAndLogistics
    }

    /// <summary>Counters of one scripted player (what it saw ACCEPTED by the authority; nothing is self-reported success).</summary>
    public sealed class ContinuityMetrics
    {
        public double DriverSeconds;
        public double FirstSimulatedSeconds = -1;
        public double LastSimulatedSeconds;
        /// <summary>Driver time of the last snapshot whose simulated clock moved forward (frozen-state detection).</summary>
        public double LastAdvanceDriverSeconds;
        public long Epoch = -1;
        public int Requests;
        public int Accepted;
        public int BatchesStarted;
        public int RoutesAccepted;
        public int RoutesRejectedOffSpec;
        public int RechecksStarted;
        public int Inspections;
        public int AirServices;
        public int PowerStallsCancelled;
        public int RepairsRestored;
        public int OrdersPlaced;
        public int UnloadsStarted;
        public int OrdersSeenUnloaded;
        public bool FaultSeen;
        public readonly Dictionary<string, int> Refusals = new Dictionary<string, int>(StringComparer.Ordinal);

        public double SimulatedSeconds => FirstSimulatedSeconds < 0 ? 0 : LastSimulatedSeconds - FirstSimulatedSeconds;

        public string Summary()
        {
            var refusals = new List<string>();
            foreach (KeyValuePair<string, int> r in Refusals) refusals.Add(r.Key + "=" + r.Value);
            refusals.Sort(StringComparer.Ordinal);
            return string.Format(CultureInfo.InvariantCulture,
                "driver={0:0.0}s sim={1:0.0}s epoch={2} requests={3} accepted={4} batches={5} routes={6} offSpecReprocess={7} rechecks={8} " +
                "inspections={9} airServices={10} powerStallsCancelled={11} repairs={12} orders={13} unloads={14} ordersUnloaded={15} faultSeen={16} refusals=[{17}]",
                DriverSeconds, SimulatedSeconds, Epoch, Requests, Accepted, BatchesStarted, RoutesAccepted, RoutesRejectedOffSpec,
                RechecksStarted, Inspections, AirServices, PowerStallsCancelled, RepairsRestored, OrdersPlaced, UnloadsStarted, OrdersSeenUnloaded, FaultSeen,
                string.Join(",", refusals));
        }
    }

    /// <summary>
    /// D5 continuity pilot (pure C#). Plays ONE crew member of the continuous base for a fixed driver time through an
    /// <see cref="IBaseOperationsActor"/>: walks to stations, inspects, decides from the CURRENT observation, repairs,
    /// orders and unloads. It never sends a position, stock or result. It does NOT prove fun, comprehension or a session
    /// played by people; it produces reproducible evidence that the session keeps running (no close, no reset).
    ///
    /// Verdict at the end of the driver time: Passed only when the session was never reset, completed or interrupted, the
    /// simulated clock advanced, and the role made real progress (water: at least <see cref="MinRoutes"/> routed batches
    /// and, if the synthetic fault was seen, at least one restored repair; air/logistics: at least one air service and one
    /// unloaded order).
    /// </summary>
    public sealed class BaseOperationsContinuityScript
    {
        public const int MinRoutes = 3;
        /// <summary>The session must still be advancing at the end: early progress followed by a frozen state is a FAIL.</summary>
        public const double MaxFrozenDriverSeconds = 15;
        private const double WaypointArrival = 0.2;
        private const double StuckSeconds = 6;
        private const int MaxReplans = 8;
        private const double RetryRealSeconds = 1.0;
        private const double InspectEverySimSeconds = 45;
        private const double ReorderEverySimSeconds = 240;

        private readonly IBaseOperationsActor actor;
        private readonly BaseOperationsScenario scenario;
        private readonly Phase04Navigator navigator;
        private readonly double durationSeconds;

        private bool waiting;
        private BaseOperationAction waitingAction;
        private OperationChoice waitingChoice;
        private double retryAt;
        private double lastWaterInspect = double.NegativeInfinity, lastAirInspect = double.NegativeInfinity;
        private double lastOrderAt = double.NegativeInfinity;
        private bool waterNeedsTest, airNeedsTest;
        private bool recoveringAirPower;
        private double airPowerRecoveryTarget;
        private double stalledAirSince = -1;
        private readonly HashSet<long> unloadedSeen = new HashSet<long>();

        private StationId pathStation;
        private List<BasePosition> path;
        private int pathIndex;
        private double bestDistance = double.PositiveInfinity, bestAt;
        private int replans;

        public ContinuityRole Role { get; }
        public ContinuityMetrics Metrics { get; } = new ContinuityMetrics();
        public bool Finished { get; private set; }
        public bool Passed { get; private set; }
        public string Verdict { get; private set; } = "";
        public string Activity { get; private set; } = "join";
        public List<string> Log { get; } = new List<string>();

        public BaseOperationsContinuityScript(IBaseOperationsActor actor, BaseOperationsScenario scenario, ContinuityRole role, double durationSeconds)
        {
            this.actor = actor ?? throw new ArgumentNullException(nameof(actor));
            this.scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            if (double.IsNaN(durationSeconds) || durationSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            this.durationSeconds = durationSeconds;
            Role = role;
            navigator = new Phase04Navigator(scenario.Expedition);
        }

        /// <summary>Advances the pilot by <paramref name="driverSeconds"/> (real seconds in a build, simulated in memory).</summary>
        public void Update(double driverSeconds)
        {
            if (Finished) return;
            Metrics.DriverSeconds += Math.Max(0, driverSeconds);
            if (!actor.Membership.IsJoined || !actor.TryGetState(out BaseOperationsSnapshot s)) return;
            if (!Observe(s)) return;
            if (Metrics.DriverSeconds >= durationSeconds)
            {
                actor.Move(0, 0);
                Judge(s);
                return;
            }
            if (s.Outpost.State != SessionState.Running)
            {
                actor.Move(0, 0);
                Activity = "session " + s.Outpost.State;
                return;
            }
            if (waiting)
            {
                if (actor.HasPending) return;
                string answer = actor.TakeAnswer();
                if (answer == null) return;
                waiting = false;
                OnAnswer(s, answer);
                return;
            }
            if (s.TryGetOperation(actor.Membership.Player, out OperationProgress own))
            {
                actor.Move(0, 0);
                Activity = "operating " + own.Action + (own.Stalled ? " (stalled: power not served)" : "");
                if (Role == ContinuityRole.AirAndLogistics && own.Action == BaseOperationAction.ServiceAir
                    && own.Choice == OperationChoice.RegenerateStandbyBed && own.Stalled)
                {
                    if (stalledAirSince < 0) stalledAirSince = Metrics.DriverSeconds;
                    if (!waiting && Metrics.DriverSeconds - stalledAirSince >= 3
                        && actor.Request(BaseOperationAction.CancelOperation, own.Station, OperationChoice.None, null, BaseOperationCommand.Unstated))
                    {
                        waiting = true;
                        waitingAction = BaseOperationAction.CancelOperation;
                        waitingChoice = OperationChoice.None;
                        Note(s, "cancelled stalled air regeneration to release power and recharge");
                    }
                }
                else if (!own.Stalled) stalledAirSince = -1;
                return;
            }
            stalledAirSince = -1;
            if (recoveringAirPower)
            {
                double energy = s.Outpost.Resources.Power.EnergyJoules;
                if (energy < airPowerRecoveryTarget)
                {
                    actor.Move(0, 0);
                    Activity = "waiting for power recharge (" + energy.ToString("0", CultureInfo.InvariantCulture) + "/"
                               + airPowerRecoveryTarget.ToString("0", CultureInfo.InvariantCulture) + " J)";
                    return;
                }
                recoveringAirPower = false;
                Note(s, "battery recovered to " + energy.ToString("0", CultureInfo.InvariantCulture) + " J; retrying air regeneration");
            }
            if (Metrics.DriverSeconds < retryAt) return;
            Goal goal = Role == ContinuityRole.Water ? NextWater(s) : NextAirOrLogistics(s);
            if (goal == null)
            {
                actor.Move(0, 0);
                Activity = "idle";
                return;
            }
            Activity = goal.Action + " at " + goal.Station;
            if (!WalkTo(s, goal.Station)) return;
            if (actor.HasPending) return;
            actor.TakeAnswer();
            if (!actor.Request(goal.Action, goal.Station, goal.Choice, goal.Manifest, goal.Order))
            {
                retryAt = Metrics.DriverSeconds + RetryRealSeconds;
                return;
            }
            Metrics.Requests++;
            waiting = true;
            waitingAction = goal.Action;
            waitingChoice = goal.Choice;
            Note(s, "request " + goal.Action + (goal.Choice != OperationChoice.None ? "/" + goal.Choice : "") + " at " + goal.Station);
        }

        /// <summary>Continuity invariants seen by this player. False when the run just finished.</summary>
        private bool Observe(BaseOperationsSnapshot s)
        {
            if (Metrics.Epoch < 0)
            {
                Metrics.Epoch = s.Epoch;
                Metrics.FirstSimulatedSeconds = s.SimulatedTimeSeconds;
                Note(s, "joined " + actor.Membership.Player + " role " + Role);
            }
            else if (s.Epoch != Metrics.Epoch)
            {
                Finish(s, false, "epoch changed " + Metrics.Epoch + " -> " + s.Epoch + ": the base was reset during the run");
                return false;
            }
            if (s.SimulatedTimeSeconds + 1e-9 < Metrics.LastSimulatedSeconds)
            {
                Finish(s, false, "simulated clock went back");
                return false;
            }
            if (s.SimulatedTimeSeconds > Metrics.LastSimulatedSeconds + 1e-9) Metrics.LastAdvanceDriverSeconds = Metrics.DriverSeconds;
            Metrics.LastSimulatedSeconds = s.SimulatedTimeSeconds;
            if (s.Outpost.State == SessionState.Completed)
            {
                Finish(s, false, "continuous session reported Completed");
                return false;
            }
            if (s.Outpost.State == SessionState.Interrupted)
            {
                Finish(s, false, "session interrupted: " + s.Session.EndCause);
                return false;
            }
            Findings water = FindingsOf(s, EquipmentKind.WaterProcessor);
            if ((water & (Findings.ProcessorFilterRestricted | Findings.ProcessorFailed)) != 0) Metrics.FaultSeen = true;
            foreach (ResupplyOrder order in s.Orders)
                if (order.State == OrderState.Unloaded && unloadedSeen.Add(order.Sequence)) Metrics.OrdersSeenUnloaded++;
            return true;
        }

        private void OnAnswer(BaseOperationsSnapshot s, string answer)
        {
            if (answer == OutpostReasons.Accepted)
            {
                Metrics.Accepted++;
                switch (waitingAction)
                {
                    case BaseOperationAction.CancelOperation:
                        if (Role == ContinuityRole.AirAndLogistics)
                        {
                            recoveringAirPower = true;
                            Metrics.PowerStallsCancelled++;
                            airPowerRecoveryTarget = RequiredAirRegenerationEnergy() * 1.1;
                        }
                        break;
                    case BaseOperationAction.ProcessBatch: Metrics.BatchesStarted++; break;
                    case BaseOperationAction.RouteProduct:
                        Metrics.RoutesAccepted++;
                        if (waitingChoice == OperationChoice.Reprocess) Metrics.RoutesRejectedOffSpec++;
                        break;
                    case BaseOperationAction.RecheckPotable: Metrics.RechecksStarted++; break;
                    case BaseOperationAction.Inspect:
                        Metrics.Inspections++;
                        if (Role == ContinuityRole.Water) lastWaterInspect = s.SimulatedTimeSeconds;
                        else lastAirInspect = s.SimulatedTimeSeconds;
                        break;
                    case BaseOperationAction.ServiceAir: Metrics.AirServices++; break;
                    case BaseOperationAction.Restore: Metrics.RepairsRestored++; break;
                    case BaseOperationAction.PlaceOrder:
                        Metrics.OrdersPlaced++;
                        lastOrderAt = s.SimulatedTimeSeconds;
                        break;
                    case BaseOperationAction.UnloadDelivery: Metrics.UnloadsStarted++; break;
                    case BaseOperationAction.Intervene:
                        if (waitingChoice == OperationChoice.ReplaceProcessorFilter) waterNeedsTest = true;
                        else airNeedsTest = true;
                        break;
                    case BaseOperationAction.TestEquipment:
                        if (Role == ContinuityRole.Water) waterNeedsTest = false;
                        else airNeedsTest = false;
                        break;
                }
                Note(s, "accepted " + waitingAction);
                return;
            }
            Metrics.Refusals.TryGetValue(answer, out int count);
            Metrics.Refusals[answer] = count + 1;
            if (waitingAction == BaseOperationAction.PlaceOrder) lastOrderAt = s.SimulatedTimeSeconds;
            retryAt = Metrics.DriverSeconds + RetryRealSeconds;
            Note(s, "refused " + waitingAction + ": " + answer);
        }

        private void Judge(BaseOperationsSnapshot s)
        {
            var missing = new List<string>();
            if (Metrics.SimulatedSeconds <= 0) missing.Add("simulated clock did not advance");
            double frozen = Metrics.DriverSeconds - Metrics.LastAdvanceDriverSeconds;
            if (frozen > MaxFrozenDriverSeconds)
                missing.Add($"state frozen for the last {frozen:0.#} s (no simulated advance; session {s.Outpost.State})");
            if (Role == ContinuityRole.Water)
            {
                if (Metrics.RoutesAccepted < MinRoutes) missing.Add("routed batches " + Metrics.RoutesAccepted + " < " + MinRoutes);
                if (Metrics.FaultSeen && Metrics.RepairsRestored < 1) missing.Add("recycler fault seen but no repair restored");
            }
            else
            {
                if (Metrics.AirServices < 1) missing.Add("no air service accepted");
                if (Metrics.OrdersSeenUnloaded < 1) missing.Add("no order unloaded");
            }
            Finish(s, missing.Count == 0, missing.Count == 0 ? "continuity criteria met" : string.Join("; ", missing));
        }

        private void Finish(BaseOperationsSnapshot s, bool passed, string verdict)
        {
            actor.Move(0, 0);
            Finished = true;
            Passed = passed;
            Verdict = verdict + " | " + Metrics.Summary();
            Note(s, (passed ? "PASS " : "FAIL ") + Verdict);
        }

        private void Note(BaseOperationsSnapshot s, string text) =>
            Log.Add(string.Format(CultureInfo.InvariantCulture, "driver={0:0.0}s sim={1:0.0}s rev={2} {3} {4}",
                Metrics.DriverSeconds, s.SimulatedTimeSeconds, s.Revision, Role, text));

        // ---- decisions -------------------------------------------------------------------------------------------

        private sealed class Goal
        {
            public StationId Station;
            public BaseOperationAction Action;
            public OperationChoice Choice;
            public string Manifest;
            public long Order = BaseOperationCommand.Unstated;
        }

        private static Goal At(StationId station, BaseOperationAction action, OperationChoice choice = OperationChoice.None) =>
            new Goal { Station = station, Action = action, Choice = choice };

        private static Findings FindingsOf(BaseOperationsSnapshot s, EquipmentKind kind)
        {
            EquipmentObservation o = s.EquipmentOf(kind).LatestObservation;
            return o != null && o.Current ? o.Findings : Findings.None;
        }

        private static bool HasCurrentObservation(BaseOperationsSnapshot s, EquipmentKind kind)
        {
            EquipmentObservation o = s.EquipmentOf(kind).LatestObservation;
            return o != null && o.Current;
        }

        private Goal NextWater(BaseOperationsSnapshot s)
        {
            StationId station = scenario.WaterStation;
            EquipmentStatus eq = s.EquipmentOf(EquipmentKind.WaterProcessor);
            WaterTreatmentStatus water = s.Water;
            double now = s.SimulatedTimeSeconds;
            if (eq.Repair != null)
                return RepairStep(station, eq.Repair, OperationChoice.ReplaceProcessorFilter, waterNeedsTest);
            if (water.PotableQuality == PotableQuality.Suspect) return At(station, BaseOperationAction.RecheckPotable);
            bool current = HasCurrentObservation(s, EquipmentKind.WaterProcessor);
            Findings found = FindingsOf(s, EquipmentKind.WaterProcessor);
            const Findings fault = Findings.ProcessorFilterRestricted | Findings.ProcessorFailed;
            bool spare = s.FreeSpares(scenario.Equipment.WaterProcessor.FilterSpareKey) > 0;
            if (current && (found & fault) != 0 && spare && water.HoldKilograms <= 0) return At(station, BaseOperationAction.Isolate);
            if (water.HoldKilograms > 0)
            {
                if (!current) return Inspect(station, now, ref lastWaterInspect, true);
                return At(station, BaseOperationAction.RouteProduct,
                    water.ProductWithinLimit ? OperationChoice.AcceptToPotable : OperationChoice.Reprocess);
            }
            if ((found & Findings.ProcessorFailed) != 0) return null; // wait for a spare
            if (eq.LatestObservation == null) return Inspect(station, now, ref lastWaterInspect, true);
            double untreated = s.Outpost.Resources.LifeSupport.Water.UntreatedKilograms;
            if (untreated >= scenario.Equipment.WaterProcessor.BatchKilograms) return At(station, BaseOperationAction.ProcessBatch);
            return Inspect(station, now, ref lastWaterInspect, false);
        }

        private Goal NextAirOrLogistics(BaseOperationsSnapshot s)
        {
            StationId console = scenario.Logistics.Console;
            double now = s.SimulatedTimeSeconds;
            foreach (ResupplyOrder order in s.Orders)
                if (order.State == OrderState.Arrived)
                    return new Goal { Station = console, Action = BaseOperationAction.UnloadDelivery, Order = order.Sequence };
            bool inFlight = false;
            foreach (ResupplyOrder order in s.Orders) inFlight |= order.InFlight;
            if (!inFlight && now - lastOrderAt >= Math.Max(ReorderEverySimSeconds, scenario.Logistics.MinSecondsBetweenOrders) && NeedsResupply(s))
                return new Goal { Station = console, Action = BaseOperationAction.PlaceOrder, Manifest = scenario.Manifests[0].Key };

            StationId station = scenario.AirStation;
            EquipmentStatus eq = s.EquipmentOf(EquipmentKind.AirRevitalization);
            if (eq.Repair != null) return RepairStep(station, eq.Repair, OperationChoice.ReplaceInletFilter, airNeedsTest);
            if (!HasCurrentObservation(s, EquipmentKind.AirRevitalization)) return Inspect(station, now, ref lastAirInspect, false);
            Findings found = FindingsOf(s, EquipmentKind.AirRevitalization);
            if ((found & Findings.InletFlowRestricted) != 0
                && s.FreeSpares(scenario.Equipment.Air.InletFilterSpareKey) > 0)
                return At(station, BaseOperationAction.Isolate);
            if ((found & Findings.StandbyBedLoaded) != 0)
                return At(station, BaseOperationAction.ServiceAir, OperationChoice.RegenerateStandbyBed);
            if ((found & Findings.ActiveBedSaturated) != 0)
                return At(station, BaseOperationAction.ServiceAir, OperationChoice.SwitchBed);
            return null;
        }

        private double RequiredAirRegenerationEnergy()
        {
            foreach (OperationDefinition operation in scenario.Operations)
                if (operation.Action == BaseOperationAction.ServiceAir
                    && operation.Choice == OperationChoice.RegenerateStandbyBed)
                    return operation.DurationSeconds * operation.PowerWatts;
            return 0;
        }

        /// <summary>Low stock (reserve or exhausted band), a missing free spare, or simply the periodic resupply of a living base.</summary>
        private bool NeedsResupply(BaseOperationsSnapshot s)
        {
            GaugeBand potable = s.GaugeOf(GaugeKind.PotableWater).Band, food = s.GaugeOf(GaugeKind.StoredFood).Band;
            if (potable == GaugeBand.Reserve || potable == GaugeBand.Exhausted || food == GaugeBand.Reserve || food == GaugeBand.Exhausted) return true;
            if (s.FreeSpares(scenario.Equipment.WaterProcessor.FilterSpareKey) <= 0) return true;
            return Metrics.OrdersPlaced == 0;
        }

        private static Goal Inspect(StationId station, double now, ref double last, bool force)
        {
            if (!force && now - last < InspectEverySimSeconds) return null;
            // Planning a walk is not an inspection. Start the cooldown only after the authority accepts it.
            return At(station, BaseOperationAction.Inspect);
        }

        private static Goal RepairStep(StationId station, RepairJob job, OperationChoice intervention, bool needsTest)
        {
            if (RepairSequence.IsAllowed(job.Completed, RepairSteps.Restore, job.LastTestPassed)) return At(station, BaseOperationAction.Restore);
            bool intervened = (job.Completed & RepairSteps.Intervene) != 0;
            bool tested = (job.Completed & RepairSteps.Test) != 0;
            if (intervened && (needsTest || !tested) && RepairSequence.IsAllowed(job.Completed, RepairSteps.Test, job.LastTestPassed))
                return At(station, BaseOperationAction.TestEquipment);
            if (RepairSequence.IsAllowed(job.Completed, RepairSteps.Intervene, job.LastTestPassed))
                return At(station, BaseOperationAction.Intervene, intervention);
            return null;
        }

        // ---- movement --------------------------------------------------------------------------------------------

        /// <summary>True when the confirmed position is within reach of <paramref name="station"/>; otherwise holds a direction.</summary>
        private bool WalkTo(BaseOperationsSnapshot s, StationId station)
        {
            if (!s.Outpost.TryGetCrew(actor.Membership.Player, out CrewMemberState me)) return false;
            BasePosition position = me.Pose.Position;
            if (!scenario.Layout.TryGetStation(station, out StationPlacement placement)) return false;
            if (placement.IsWithinReach(position))
            {
                actor.Move(0, 0);
                path = null;
                replans = 0;
                return true;
            }
            double now = s.SimulatedTimeSeconds;
            if (path == null || pathStation != station)
            {
                path = navigator.PlanToStation(position, station);
                pathStation = station;
                pathIndex = 0;
                bestDistance = double.PositiveInfinity;
                bestAt = now;
                if (path == null)
                {
                    Finish(s, false, "no walkable route to " + station + " from " + position);
                    return false;
                }
            }
            while (pathIndex < path.Count - 1 && position.DistanceTo(path[pathIndex]) <= WaypointArrival)
            {
                pathIndex++;
                bestDistance = double.PositiveInfinity;
                bestAt = now;
            }
            BasePosition target = path[pathIndex];
            double dx = target.X - position.X, dy = target.Y - position.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < bestDistance - 0.05)
            {
                bestDistance = d;
                bestAt = now;
            }
            else if (now - bestAt > StuckSeconds)
            {
                if (++replans > MaxReplans)
                {
                    Finish(s, false, "stuck at " + position + " going to " + station);
                    return false;
                }
                path = null;
                Note(s, "replan to " + station + " (" + replans + ")");
                return false;
            }
            if (d < 1e-6)
            {
                actor.Move(0, 0);
                return false;
            }
            double magnitude = pathIndex == path.Count - 1 && d < 0.6 ? 0.4 : 1.0;
            actor.Move(dx / d * magnitude, dy / d * magnitude);
            return false;
        }
    }
}
