using System;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.Incidents;

namespace Trainer.Application.Incidents
{
    /// <summary>Validation and acceptance of <see cref="IncidentCommand"/> (SESSION_CONTRACT §6 order; first failure is the reason).</summary>
    public sealed partial class LivingBaseAuthority
    {
        public void Submit(PlayerId sender, IncidentCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (command.Id == Guid.Empty)
            {
                CommandCompleted?.Invoke(sender, new CommandResult(command.Id, command.Epoch, false, OutpostReasons.InvalidCommandId));
                return;
            }
            if (commands.TryGetValue(command.Id, out StoredCommand stored) || Expedition.HasCommand(command.Id))
            {
                bool same = stored != null && stored.Kind == CommandKind.Incident && stored.Sender == sender
                    && stored.Incident.HasSameContent(command) && stored.Result != null;
                CommandCompleted?.Invoke(sender, same ? stored.Result
                    : new CommandResult(command.Id, command.Epoch, false, OutpostReasons.CommandIdConflict));
                return;
            }

            SyncEpoch();
            baseOperations.TryGetSnapshot(out BaseOperationsSnapshot b);
            double now = Expedition.SimulatedNow;
            string rejection = Validate(sender, command, b, now);
            if (rejection == null) Accept(sender, command, b, now);
            var result = new CommandResult(command.Id, command.Epoch, rejection == null, rejection ?? OutpostReasons.Accepted);
            commands.Add(command.Id, new StoredCommand { Sender = sender, Kind = CommandKind.Incident, Incident = command, Result = result });
            try
            {
                if (rejection == null) CommitChange();
            }
            finally
            {
                CommandCompleted?.Invoke(sender, result);
            }
        }

        private string Validate(PlayerId sender, IncidentCommand c, BaseOperationsSnapshot b, double now)
        {
            ExpeditionCrewState crew = CrewOf(b, sender);
            CrewMemberState member = MemberOf(b, sender);
            if (crew == null || member == null) return OutpostReasons.NotJoined;
            if (c.Epoch != currentEpoch) return OutpostReasons.EpochMismatch;
            string shape = c.ShapeError();
            if (shape != null) return shape;
            if (Expedition.IsEnded) return OutpostReasons.SessionEnded;
            SessionState state = Expedition.CurrentState;
            bool allowedPaused = c.Action == IncidentAction.CancelOperation || c.Action == IncidentAction.PostponeAssignment
                || c.Action == IncidentAction.CancelAssignment;
            if (state != SessionState.Running && !(allowedPaused && state == SessionState.Paused))
                return OutpostReasons.InvalidState;
            if (IncidentCommand.IsAssignmentAction(c.Action)) return ValidateAssignment(sender, c, now);

            StationPlacement placement = PlacementOf(c.Station);
            if (placement == null) return OutpostReasons.UnknownStation;
            if (IncidentCommand.IsPowerAction(c.Action) && c.Station != Scenario.PowerStation) return IncidentReasons.WrongStationForAction;
            if (c.Action == IncidentAction.InstallUpgrade && c.Station != Scenario.Upgrade.Station) return IncidentReasons.WrongStationForAction;

            if (c.Action == IncidentAction.CancelOperation)
                return operations.Exists(o => o.Performer == sender && o.Station == c.Station) ? null : OutpostReasons.NoActiveTask;

            if (IsBusy(sender, b)) return OutpostReasons.PlayerBusy;
            if (IsOccupied(c.Station, b)) return OutpostReasons.StationOccupied;
            if (c.ZoneSequence != crew.ZoneSequence) return IncidentReasons.StaleZone;
            if (!placement.IsWithinReach(member.Pose.Position)) return OutpostReasons.OutOfRange;
            if (crew.OnSuitSupply) return IncidentReasons.SealedNotAllowed;
            return ValidateAction(c, now);
        }

        private string ValidateAction(IncidentCommand c, double now)
        {
            if (IncidentCommand.NeedsObservation(c.Action))
            {
                if (lastObservation == null) return IncidentReasons.ObservationRequired;
                if (c.ObservationSequence != lastObservation.Sequence || !IsCurrent(lastObservation, now)) return IncidentReasons.StaleObservation;
            }
            switch (c.Action)
            {
                case IncidentAction.ResetConverter:
                    return converterTripped || controllerDamaged ? null : IncidentReasons.NothingToRepair;
                case IncidentAction.IsolatePower:
                    if (!controllerDamaged) return IncidentReasons.NothingToRepair;
                    return powerIsolated ? IncidentReasons.PowerAlreadyIsolated : null;
                case IncidentAction.ReplacePowerPart:
                    if (!powerIsolated) return IncidentReasons.PowerNotIsolated;
                    if (partReplaced) return IncidentReasons.NothingToRepair;
                    return SpareCount(ControllerSpareKey()) < 1 ? IncidentReasons.SpareUnavailable : null;
                case IncidentAction.RestorePower:
                    if (!powerIsolated) return IncidentReasons.PowerNotIsolated;
                    return partReplaced ? null : IncidentReasons.PartNotReplaced;
                case IncidentAction.InstallUpgrade:
                    if (upgradeInstalledAt.HasValue || operations.Exists(o => o.Action == IncidentAction.InstallUpgrade))
                        return IncidentReasons.UpgradeInstalled;
                    if (credits < Scenario.Upgrade.CreditCost) return IncidentReasons.InsufficientCredits;
                    return SpareCount(Scenario.Upgrade.SpareKey) < 1 ? IncidentReasons.SpareUnavailable : null;
                default:
                    return null;
            }
        }

        private void Accept(PlayerId sender, IncidentCommand c, BaseOperationsSnapshot b, double now)
        {
            if (IncidentCommand.IsAssignmentAction(c.Action))
            {
                AcceptAssignmentCommand(sender, c, now, b);
                return;
            }
            PowerOperationRules rules = Scenario.PowerOperations;
            switch (c.Action)
            {
                case IncidentAction.CancelOperation:
                    operations.RemoveAll(o => o.Performer == sender && o.Station == c.Station);
                    break;
                case IncidentAction.InspectPower:
                    StartOperation(sender, c, rules.InspectSeconds);
                    break;
                case IncidentAction.ResetConverter:
                    StartOperation(sender, c, rules.ResetSeconds);
                    break;
                case IncidentAction.ReplacePowerPart:
                    StartOperation(sender, c, rules.ReplaceSeconds);
                    break;
                case IncidentAction.InstallUpgrade:
                    // Credits and the spare are committed now: cancelling later refunds nothing (declared rule).
                    if (equipment.ConsumeSpare(LivingBaseIds.Derive(c.Id, "upgrade-spare"), Scenario.Upgrade.SpareKey) != EquipmentEffectStatus.Applied)
                        throw new InvalidOperationException("Validated spare could not be consumed.");
                    credits -= Scenario.Upgrade.CreditCost;
                    StartOperation(sender, c, Scenario.Upgrade.InstallSeconds);
                    break;
                case IncidentAction.IsolatePower:
                {
                    Incident fault = incidents.Find(i => i.IsOpen && i.Type == IncidentType.PowerControllerFault);
                    var mods = (IPowerCapacityModifiers)outpost;
                    if (fault != null)
                    {
                        mods.Remove(fault.ModifierId);
                        ApplyModifier(new PowerModifier(fault.IsolatedModifierId, PowerModifierTarget.DischargeFactor, fault.Definition.IsolatedFactor,
                            fault.Definition.Key));
                        fault.Phase = IncidentPhase.Intervention;
                    }
                    powerIsolated = true;
                    powerStateSequence++;
                    AddFeedback(c.Id, sender, c.Action, now, DecisionAssessment.Appropriate, "controller-isolated");
                    break;
                }
                case IncidentAction.RestorePower:
                {
                    Incident fault = incidents.Find(i => i.IsOpen && i.Type == IncidentType.PowerControllerFault);
                    if (fault != null)
                    {
                        var mods = (IPowerCapacityModifiers)outpost;
                        mods.Remove(fault.ModifierId);
                        mods.Remove(fault.IsolatedModifierId);
                        fault.Phase = IncidentPhase.Recovery;
                        fault.DueAt = now + Scenario.Director.RecoveryObservationSeconds;
                    }
                    controllerDamaged = powerIsolated = partReplaced = false;
                    powerStateSequence++;
                    AddFeedback(c.Id, sender, c.Action, now, DecisionAssessment.Appropriate, "power-restored");
                    break;
                }
            }
        }

        private void StartOperation(PlayerId sender, IncidentCommand c, double duration) =>
            operations.Add(new Operation { CommandId = c.Id, Performer = sender, Station = c.Station, Action = c.Action, Duration = duration });

        /// <summary>Busy with a phase 2/3 task, a phase 4 operation or a phase 5 operation.</summary>
        private bool IsBusy(PlayerId player, BaseOperationsSnapshot b)
        {
            if (operations.Exists(o => o.Performer == player)) return true;
            foreach (OperationProgress op in b.Operations) if (op.Performer == player) return true;
            foreach (StationState s in b.Expedition.Outpost.Stations) if (s.ActiveTask != null && s.ActiveTask.Performer == player) return true;
            return false;
        }

        private bool IsOccupied(StationId station, BaseOperationsSnapshot b)
        {
            if (operations.Exists(o => o.Station == station)) return true;
            foreach (OperationProgress op in b.Operations) if (op.Station == station) return true;
            foreach (StationState s in b.Expedition.Outpost.Stations) if (s.Station == station && s.ActiveTask != null) return true;
            return false;
        }
    }
}
