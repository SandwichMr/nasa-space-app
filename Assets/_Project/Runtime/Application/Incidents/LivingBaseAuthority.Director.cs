using System;
using System.Collections.Generic;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.Incidents;

namespace Trainer.Application.Incidents
{
    /// <summary>Director, incident lifecycle (with D1 effects), timed operations and the chunk schedule.</summary>
    public sealed partial class LivingBaseAuthority
    {
        // ---------------------------------------------------------------- schedule

        private double SecondsToNextEvent(double now)
        {
            double next = nextEvaluationAt;
            foreach (Incident i in incidents)
                if (i.IsOpen && (i.Phase == IncidentPhase.Warning || i.Phase == IncidentPhase.Recovery) && i.Type != IncidentType.SpaceWeather)
                    next = Math.Min(next, i.DueAt);
            if (lastEpisode != null && !lastEpisode.Ended)
            {
                next = Math.Min(next, lastEpisode.NextIssueAt);
                double? shelterBy = lastEpisode.ShelterBy;
                if (shelterBy.HasValue && shelterBy.Value > now) next = Math.Min(next, shelterBy.Value);
            }
            foreach (Operation o in operations) next = Math.Min(next, now + Math.Max(0d, o.Duration - o.Work));
            next = Math.Min(next, NextAssignmentEvent(now));
            return Math.Max(0d, next - now);
        }

        /// <summary>Everything that happens at the end of one chunk, in contract order (§7).</summary>
        private void Process(double now, double elapsed)
        {
            baseOperations.TryGetSnapshot(out BaseOperationsSnapshot b);
            ProgressOperations(now, elapsed, b);
            UpdateEpisode(now, b);
            UpdateIncidents(now, b);
            if (now >= nextEvaluationAt - Epsilon)
            {
                while (nextEvaluationAt <= now + Epsilon) nextEvaluationAt += Scenario.Director.EvaluationIntervalSeconds;
                Evaluate(now, b);
            }
            UpdateAssignments(now, b);
        }

        // ---------------------------------------------------------------- director

        private void Evaluate(double now, BaseOperationsSnapshot b)
        {
            evaluations++;
            if (DirectorPhaseAt(now) != DirectorPhase.Ready) return;
            if (directorRandom.NextDouble() >= Scenario.Director.PressurePerEvaluation)
            {
                Decide(now, null, false, "no-pressure");
                return;
            }
            double total = 0d;
            foreach (IncidentDefinition d in Scenario.Catalog) total += d.Weight;
            double roll = directorRandom.NextDouble() * total;
            IncidentDefinition candidate = Scenario.Catalog[Scenario.Catalog.Count - 1];
            foreach (IncidentDefinition d in Scenario.Catalog)
            {
                if (roll < d.Weight)
                {
                    candidate = d;
                    break;
                }
                roll -= d.Weight;
            }
            string rejection = Rejection(candidate, now, b);
            if (rejection != null)
            {
                Decide(now, candidate.Key, false, rejection);
                return;
            }
            Start(candidate, now, b);
            Decide(now, candidate.Key, true, "accepted");
        }

        /// <summary>Viability and variety rules. A rejected candidate leaves no trace other than its decision record.</summary>
        private string Rejection(IncidentDefinition d, double now, BaseOperationsSnapshot b)
        {
            if (d.Key == lastIncidentKey) return "repeat-last";
            if (incidents.Exists(i => i.IsOpen && i.Type == d.Kind)) return "type-open";
            if (lastStartedAt.TryGetValue(d.Key, out double started) && now < started + d.CooldownSeconds) return "cooldown";
            bool powerOpen = incidents.Exists(i => i.IsOpen && (i.Type == IncidentType.PowerConverterTrip || i.Type == IncidentType.PowerControllerFault));
            bool weatherOpen = incidents.Exists(i => i.IsOpen && i.Type == IncidentType.SpaceWeather);
            switch (d.Kind)
            {
                case IncidentType.ProcessorDegradation:
                    WaterProcessorState water = equipment.Snapshot.WaterProcessor;
                    if (water.Condition != EquipmentCondition.Nominal || water.Operating != EquipmentOperatingState.Online) return "processor-not-nominal";
                    if (SpareCount(Scenario.BaseOperations.Equipment.WaterProcessor.FilterSpareKey) < 1) return "no-spare";
                    return null;
                case IncidentType.SpaceWeather:
                    return powerOpen ? "power-incident-open" : null;
                default:
                    if (powerOpen) return "power-incident-open";
                    if (weatherOpen) return "space-weather-open";
                    var power = b.Expedition.Outpost.Resources.Power;
                    if (power.CapacityJoules > 0d && power.EnergyJoules / power.CapacityJoules < Scenario.Director.MinBatteryFractionForPowerIncident)
                        return "battery-low";
                    if (d.Kind == IncidentType.PowerControllerFault && SpareCount(d.SpareKey) < 1) return "no-spare";
                    return null;
            }
        }

        private void Decide(double now, string key, bool accepted, string reason)
        {
            decisions.Add(new DirectorDecisionRecord(++decisionSequence, now, key, accepted, reason));
            Bound(decisions, IncidentLimits.MaxDirectorDecisions);
        }

        private void Start(IncidentDefinition d, double now, BaseOperationsSnapshot b)
        {
            var incident = new Incident
            {
                Id = LivingBaseIds.Derive(Setup.Seed, currentEpoch, "incident", incidentSequence + 1),
                Sequence = ++incidentSequence, Definition = d, Phase = IncidentPhase.Warning, AnnouncedAt = now,
                DueAt = now + d.WarningSeconds
            };
            incident.ModifierId = LivingBaseIds.Derive(incident.Id, "modifier");
            incident.IsolatedModifierId = LivingBaseIds.Derive(incident.Id, "isolated");
            incidents.Add(incident);
            lastIncidentKey = d.Key;
            lastStartedAt[d.Key] = now;
            if (d.Kind == IncidentType.SpaceWeather)
            {
                SpaceWeatherEpisodeDefinition episode;
                if (dataStatus.InUse == DataMode.Historical && !historicalPlayed)
                {
                    episode = Scenario.HistoricalEpisode;
                    historicalPlayed = true;
                }
                else episode = SyntheticEpisodeGenerator.Generate(Scenario.SyntheticEpisodes, episodeRandom, ++episodeOrdinal);
                lastEpisode = new EpisodeRun(episode, now);
                lastEpisodeIncident = incident;
                UpdateEpisode(now, b);
                FillBoard(now);
            }
            else if (d.WarningSeconds <= 0d) UpdateIncidents(now, b);
        }

        // ---------------------------------------------------------------- incidents

        private void UpdateIncidents(double now, BaseOperationsSnapshot b)
        {
            foreach (Incident i in incidents)
            {
                if (!i.IsOpen || i.Type == IncidentType.SpaceWeather) continue;
                if (i.Phase == IncidentPhase.Warning && now >= i.DueAt - Epsilon) Activate(i, now);
                switch (i.Type)
                {
                    case IncidentType.ProcessorDegradation: UpdateProcessor(i, now, b); break;
                    default:
                        if (i.Phase == IncidentPhase.Recovery && now >= i.DueAt - Epsilon) Resolve(i, now, i.Type == IncidentType.PowerConverterTrip ? "reset" : "repaired");
                        break;
                }
            }
        }

        private void Activate(Incident i, double now)
        {
            IncidentDefinition d = i.Definition;
            bool applied;
            switch (d.Kind)
            {
                case IncidentType.ProcessorDegradation:
                    applied = equipment.DegradeProcessor(i.ModifierId, d.ProcessorFailAfterSeconds) == EquipmentEffectStatus.Applied;
                    break;
                case IncidentType.PowerConverterTrip:
                    applied = ApplyModifier(new PowerModifier(i.ModifierId, PowerModifierTarget.GenerationFactor, d.PowerFactor, d.Key));
                    if (applied) converterTripped = true;
                    break;
                default:
                    applied = ApplyModifier(new PowerModifier(i.ModifierId, PowerModifierTarget.DischargeFactor, d.PowerFactor, d.Key));
                    if (applied) controllerDamaged = true;
                    break;
            }
            if (!applied)
            {
                Close(i, IncidentPhase.Cancelled, now, "not-applicable");
                return;
            }
            if (d.Kind != IncidentType.ProcessorDegradation) powerStateSequence++;
            i.Phase = IncidentPhase.Active;
            i.ActiveAt = now;
        }

        private bool ApplyModifier(PowerModifier modifier)
        {
            PowerModifierStatus status = ((IPowerCapacityModifiers)outpost).Apply(modifier);
            return status == PowerModifierStatus.Applied || status == PowerModifierStatus.AlreadyApplied;
        }

        private void UpdateProcessor(Incident i, double now, BaseOperationsSnapshot b)
        {
            if (i.Phase == IncidentPhase.Warning) return;
            WaterProcessorState water = equipment.Snapshot.WaterProcessor;
            bool nominal = water.Condition == EquipmentCondition.Nominal && water.Operating == EquipmentOperatingState.Online;
            switch (i.Phase)
            {
                case IncidentPhase.Active:
                case IncidentPhase.Intervention:
                    if (nominal)
                    {
                        i.Phase = IncidentPhase.Recovery;
                        i.DueAt = now + Scenario.Director.RecoveryObservationSeconds;
                    }
                    else if (water.Condition != EquipmentCondition.Nominal || water.Operating == EquipmentOperatingState.Isolated)
                    {
                        bool working = water.Operating == EquipmentOperatingState.Isolated;
                        foreach (OperationProgress op in b.Operations) working |= op.Station == Scenario.BaseOperations.WaterStation;
                        i.Phase = working ? IncidentPhase.Intervention : IncidentPhase.Active;
                    }
                    break;
                case IncidentPhase.Recovery:
                    if (!nominal) i.Phase = IncidentPhase.Intervention;
                    else if (now >= i.DueAt - Epsilon) Resolve(i, now, "repaired");
                    break;
            }
        }

        private void Resolve(Incident i, double now, string result)
        {
            Close(i, IncidentPhase.Resolved, now, result);
            restUntil = Math.Max(restUntil, now + Scenario.Director.RestAfterResolutionSeconds);
        }

        private static void Close(Incident i, IncidentPhase phase, double now, string result)
        {
            i.Phase = phase;
            i.ClosedAt = now;
            i.Result = result;
            if (phase == IncidentPhase.Resolved && !i.ActiveAt.HasValue) i.ActiveAt = now;
        }

        private void UpdateEpisode(double now, BaseOperationsSnapshot b)
        {
            if (lastEpisode == null || lastEpisodeIncident == null || !lastEpisodeIncident.IsOpen) return;
            Incident i = lastEpisodeIncident;
            List<EpisodeMessageKind> kinds = lastEpisode.IssueDue(now);
            lastEpisode.Observe(now, b.Expedition, Scenario.BaseOperations.Expedition.Comms);
            foreach (EpisodeMessageKind kind in kinds)
                if (kind == EpisodeMessageKind.ShelterInstruction && i.Phase == IncidentPhase.Warning)
                {
                    i.Phase = IncidentPhase.Active;
                    i.ActiveAt = now;
                }
            if (i.Phase == IncidentPhase.Active && lastEpisode.AnyoneProtected) i.Phase = IncidentPhase.Intervention;
            if (lastEpisode.Ended)
            {
                var present = new List<PlayerId>();
                foreach (ExpeditionCrewState crew in b.Expedition.Crew) present.Add(crew.Player);
                lastEpisode.Assess(present);
                Resolve(i, now, "shelter-period-ended");
            }
        }

        // ---------------------------------------------------------------- timed operations

        private void ProgressOperations(double now, double elapsed, BaseOperationsSnapshot b)
        {
            for (int k = operations.Count - 1; k >= 0; k--)
            {
                Operation o = operations[k];
                if (!CanKeepWorking(o, b))
                {
                    operations.RemoveAt(k);
                    continue;
                }
                o.Work += elapsed;
                if (o.Work < o.Duration - Epsilon) continue;
                operations.RemoveAt(k);
                Complete(o, now);
            }
        }

        private bool CanKeepWorking(Operation o, BaseOperationsSnapshot b)
        {
            ExpeditionCrewState crew = CrewOf(b, o.Performer);
            CrewMemberState member = MemberOf(b, o.Performer);
            if (crew == null || member == null || crew.OnSuitSupply) return false;
            StationPlacement placement = PlacementOf(o.Station);
            return placement != null && placement.IsWithinReach(member.Pose.Position);
        }

        private void Complete(Operation o, double now)
        {
            switch (o.Action)
            {
                case IncidentAction.InspectPower:
                {
                    PowerCapacityState capacity = ((IPowerCapacityModifiers)outpost).PowerCapacity;
                    var power = Scenario.BaseOperations.Expedition.Outpost.Simulation.Power;
                    PowerFindings findings = PowerFindings.None;
                    if (capacity.GenerationFactor < 1d) findings |= PowerFindings.GenerationLow;
                    if (capacity.DischargeFactor < 1d) findings |= PowerFindings.DischargeLimited;
                    if (converterTripped) findings |= PowerFindings.ConverterTripped;
                    if (controllerDamaged) findings |= PowerFindings.ControllerDamaged;
                    lastObservation = new PowerObservation(++observationSequence, o.Performer, now,
                        now + Scenario.PowerOperations.ObservationValiditySeconds, powerStateSequence, findings,
                        power.GenerationWatts * capacity.GenerationFactor, power.MaxDischargeWatts * capacity.DischargeFactor, true);
                    break;
                }
                case IncidentAction.ResetConverter:
                    if (converterTripped)
                    {
                        Incident trip = incidents.Find(i => i.IsOpen && i.Type == IncidentType.PowerConverterTrip);
                        if (trip != null) ((IPowerCapacityModifiers)outpost).Remove(trip.ModifierId);
                        converterTripped = false;
                        powerStateSequence++;
                        if (trip != null)
                        {
                            trip.Phase = IncidentPhase.Recovery;
                            trip.DueAt = now + Scenario.Director.RecoveryObservationSeconds;
                        }
                        Feedback(o, now, DecisionAssessment.Appropriate, "converter-reset");
                    }
                    else Feedback(o, now, DecisionAssessment.NotIndicated, "reset-does-not-repair-damage");
                    break;
                case IncidentAction.ReplacePowerPart:
                {
                    string spare = ControllerSpareKey();
                    if (spare != null && equipment.ConsumeSpare(LivingBaseIds.Derive(o.CommandId, "spare"), spare) == EquipmentEffectStatus.Applied)
                    {
                        partReplaced = true;
                        powerStateSequence++;
                        Feedback(o, now, DecisionAssessment.Appropriate, "part-replaced");
                    }
                    else Feedback(o, now, DecisionAssessment.NotIndicated, "spare-unavailable");
                    break;
                }
                case IncidentAction.InstallUpgrade:
                {
                    UpgradeDefinition u = Scenario.Upgrade;
                    EquipmentEffectStatus status = equipment.UpgradeProcessorRecovery(LivingBaseIds.Derive(o.CommandId, "upgrade"), u.RecoveryFraction);
                    if (status == EquipmentEffectStatus.Applied || status == EquipmentEffectStatus.AlreadyApplied)
                    {
                        ApplyModifier(new PowerModifier(LivingBaseIds.Derive(o.CommandId, "upgrade-load"), PowerModifierTarget.EssentialLoadWatts,
                            u.AddedEssentialWatts, u.Key));
                        upgradeInstalledAt = now;
                        Feedback(o, now, DecisionAssessment.Appropriate, "upgrade-installed");
                    }
                    else Feedback(o, now, DecisionAssessment.NotIndicated, "upgrade-not-installed");
                    break;
                }
            }
        }

        private void Feedback(Operation o, double now, DecisionAssessment assessment, string key) =>
            AddFeedback(o.CommandId, o.Performer, o.Action, now, assessment, key);

        private void AddFeedback(Guid commandId, PlayerId performer, IncidentAction action, double now, DecisionAssessment assessment, string key)
        {
            feedback.Add(new IncidentFeedback(commandId, performer, action, now, assessment, key));
            Bound(feedback, IncidentLimits.MaxFeedback);
        }

        // ---------------------------------------------------------------- lookups

        private string ControllerSpareKey()
        {
            foreach (IncidentDefinition d in Scenario.Catalog)
                if (d.Kind == IncidentType.PowerControllerFault) return d.SpareKey;
            return null;
        }

        private int SpareCount(string key)
        {
            if (key == null) return 0;
            return equipment.Snapshot.TryGetSpare(key, out SpareStock spare) ? spare.Count : 0;
        }

        private StationPlacement PlacementOf(StationId station)
        {
            foreach (StationPlacement p in Scenario.BaseOperations.Expedition.Outpost.Layout.Stations)
                if (p.Id == station) return p;
            return null;
        }

        private static ExpeditionCrewState CrewOf(BaseOperationsSnapshot b, PlayerId player)
        {
            foreach (ExpeditionCrewState c in b.Expedition.Crew) if (c.Player == player) return c;
            return null;
        }

        private static CrewMemberState MemberOf(BaseOperationsSnapshot b, PlayerId player)
        {
            foreach (CrewMemberState c in b.Expedition.Outpost.Crew) if (c.Player == player) return c;
            return null;
        }
    }
}
