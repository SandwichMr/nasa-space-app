using System;
using System.Collections.Generic;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.Eva;
using Trainer.Simulation.Incidents;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Application.Incidents
{
    /// <summary>
    /// The SINGLE phase 5 session authority (D2): setbacks, NASA data and replayability around the ONE phase 4
    /// <see cref="BaseOperationsAuthority"/> (which owns the only roster, base model, EVA model, equipment model and clock).
    ///
    /// One clock: <see cref="Tick"/> advances the phase 4 authority in CHUNKS that end at the next phase 5 event (director
    /// evaluation, end of a warning or recovery window, issue of an episode message, end of a timed operation, an assignment
    /// due time). Between chunks, in this order (docs/phase-05/SESSION_CONTRACT.md §7): operations progress, episode messages and
    /// protection, incident transitions and D1 effects, director evaluation, assignments. One publication per Tick.
    ///
    /// It never adds or removes stock itself: every physical change is a D1 call (<see cref="IPowerCapacityModifiers"/>,
    /// <see cref="IEquipmentIncidentTarget"/>) with an id derived from the seed, epoch and incident or command. Every command id is
    /// recorded for the authority's lifetime across phases 3, 4 and 5. Single-threaded; state committed before events.
    /// </summary>
    public sealed partial class LivingBaseAuthority : ILivingBaseAuthority
    {
        private const double Epsilon = 1e-9;
        /// <summary>Shortest chunk the authority asks the phase 4 clock for (avoids zero-length steps at coincident events).</summary>
        private const double MinChunkSeconds = 1e-3;
        private const int MaxChunksPerTick = 4096;

        private enum CommandKind { Expedition, BaseOperation, Incident }

        private sealed class StoredCommand
        {
            public PlayerId Sender;
            public CommandKind Kind;
            public IncidentCommand Incident;
            public CommandResult Result;
        }

        private readonly BaseOperationsAuthority baseOperations;
        private readonly OutpostBaseSimulation outpost;
        private readonly BaseEquipmentSimulation equipment;
        private readonly Dictionary<Guid, StoredCommand> commands = new Dictionary<Guid, StoredCommand>();
        private readonly string configurationHash;
        private readonly DataStatus dataStatus;

        private LivingBaseSnapshot published;
        private bool deferPublication;

        public event Action<LivingBaseSnapshot> SnapshotChanged;
        public event Action<PlayerId, CommandResult> CommandCompleted;

        public LivingBaseScenario Scenario { get; }
        public LivingBaseSetup Setup { get; }

        /// <summary>The composed phase 4 authority (read-only use: diagnostics, Route, SealedSpeed).</summary>
        public BaseOperationsAuthority BaseOperations => baseOperations;

        /// <param name="outpost">D1 base model; also the session's <see cref="IPowerCapacityModifiers"/>.</param>
        /// <param name="equipment">D1 equipment model; also the session's <see cref="IEquipmentIncidentTarget"/>.</param>
        /// <param name="roster">D3 roster built for the scenario layout; must be empty.</param>
        /// <param name="historicalProblemKey">When Historical is requested but the dataset could not be loaded (the scenario has no
        /// historical episode), the stable key of the problem ("dataset-missing", "dataset-hash-mismatch", …). The session then
        /// plays labelled synthetic episodes: a load failure never means "no hazard".</param>
        public LivingBaseAuthority(LivingBaseScenario scenario, LivingBaseSetup setup, OutpostBaseSimulation outpost,
            BaseEquipmentSimulation equipment, IEvaSimulation eva, IZonedCrewRoster roster, string historicalProblemKey = null,
            double maxStepSeconds = ExpeditionAuthority.DefaultMaxStepSeconds)
        {
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            this.outpost = outpost ?? throw new ArgumentNullException(nameof(outpost));
            this.equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
            dataStatus = ResolveData(scenario, setup, historicalProblemKey);
            configurationHash = LivingBaseIds.Sha256Hex(LivingBaseIds.CanonicalConfiguration(scenario));
            baseOperations = new BaseOperationsAuthority(scenario.BaseOperations, outpost, equipment, eva, roster,
                BaseSessionMode.Continuous, maxStepSeconds);
            baseOperations.SnapshotChanged += OnBaseOperationsSnapshot;
            baseOperations.CommandCompleted += (sender, result) => CommandCompleted?.Invoke(sender, result);
            SyncEpoch();
            Publish(raise: false);
        }

        /// <summary>Data mode in use and why (fixed for the authority's lifetime).</summary>
        public DataStatus Data => dataStatus;

        private static DataStatus ResolveData(LivingBaseScenario scenario, LivingBaseSetup setup, string problemKey)
        {
            if (setup.RequestedMode == DataMode.Synthetic)
                return new DataStatus(DataMode.Synthetic, DataMode.Synthetic, DataAvailability.NotRequested, null, null);
            if (scenario.HistoricalEpisode != null)
                return new DataStatus(DataMode.Historical, DataMode.Historical, DataAvailability.Loaded, scenario.HistoricalEpisode.Dataset, null);
            return new DataStatus(DataMode.Historical, DataMode.Synthetic, DataAvailability.FallbackSynthetic, null,
                problemKey ?? "dataset-missing");
        }

        // ---------------------------------------------------------------- ILivingBaseAuthority

        public OutpostMembership Join() => baseOperations.Join();

        public bool Leave(PlayerId player)
        {
            operations.RemoveAll(o => o.Performer == player);
            return baseOperations.Leave(player);
        }

        public MovementInputDisposition SubmitMovement(PlayerId sender, MovementInput input) => baseOperations.SubmitMovement(sender, input);

        public bool TryGetSnapshot(out LivingBaseSnapshot snapshot)
        {
            baseOperations.TryGetSnapshot(out BaseOperationsSnapshot current);
            if (published == null || !ReferenceEquals(published.BaseOperations, current)) Publish(raise: false);
            snapshot = published;
            return true;
        }

        public void Submit(PlayerId sender, ExpeditionCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (Forwardable(sender, command.Id, CommandKind.Expedition, command.Epoch,
                    command.Action == ExpeditionAction.StartTask ? command.Station : StationId.None))
                baseOperations.Submit(sender, command);
        }

        public void Submit(PlayerId sender, BaseOperationCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (Forwardable(sender, command.Id, CommandKind.BaseOperation, command.Epoch, command.Station))
                baseOperations.Submit(sender, command);
        }

        /// <summary>The single clock: advances the session by simulated seconds (runner only). Nothing happens unless Running.</summary>
        public void Tick(double elapsedSimulatedSeconds)
        {
            if (double.IsNaN(elapsedSimulatedSeconds) || double.IsInfinity(elapsedSimulatedSeconds) || elapsedSimulatedSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(elapsedSimulatedSeconds));
            deferPublication = true;
            try
            {
                double remaining = elapsedSimulatedSeconds;
                int chunks = 0;
                while (remaining > 0d)
                {
                    SyncEpoch();
                    if (Expedition.CurrentState != SessionState.Running)
                    {
                        baseOperations.Tick(remaining);
                        SyncEpoch();
                        break;
                    }
                    double before = Expedition.SimulatedNow;
                    double chunk = Math.Min(remaining, Math.Max(MinChunkSeconds, SecondsToNextEvent(before)));
                    if (++chunks > MaxChunksPerTick) chunk = remaining;
                    baseOperations.Tick(chunk);
                    remaining -= chunk;
                    if (SyncEpoch()) continue;
                    double after = Expedition.SimulatedNow;
                    if (after > before) Process(after, after - before);
                }
            }
            finally
            {
                deferPublication = false;
            }
            Publish(raise: true);
        }

        // ---------------------------------------------------------------- command record

        /// <summary>Phase 3/4 commands: shared id space and the cross-phase busy rules, then the phase 4 authority decides.</summary>
        private bool Forwardable(PlayerId sender, Guid id, CommandKind kind, long epoch, StationId station)
        {
            if (id != Guid.Empty && commands.TryGetValue(id, out StoredCommand stored))
            {
                if (stored.Kind != kind)
                {
                    CommandCompleted?.Invoke(sender, new CommandResult(id, epoch, false, OutpostReasons.CommandIdConflict));
                    return false;
                }
                return true; // the phase 3/4 record answers duplicates itself
            }
            if (id == Guid.Empty) return true;
            // Only station work (phase 3 StartTask, phase 4 station operations) competes with phase 5 operations.
            if (station.IsValid && epoch == currentEpoch
                && (operations.Exists(o => o.Performer == sender) || operations.Exists(o => o.Station == station)))
            {
                string reason = operations.Exists(o => o.Performer == sender) ? OutpostReasons.PlayerBusy : OutpostReasons.StationOccupied;
                var result = new CommandResult(id, epoch, false, reason);
                commands.Add(id, new StoredCommand { Sender = sender, Kind = kind, Result = result });
                CommandCompleted?.Invoke(sender, result);
                return false;
            }
            commands.Add(id, new StoredCommand { Sender = sender, Kind = kind });
            return true;
        }

        // ---------------------------------------------------------------- publication

        private ExpeditionAuthority Expedition => baseOperations.Expedition;

        private void OnBaseOperationsSnapshot(BaseOperationsSnapshot snapshot)
        {
            if (deferPublication) return;
            SyncEpoch();
            Publish(raise: true);
        }

        /// <summary>Commits a phase 5 change outside Tick as a new phase 2 revision; the phase 4 event then publishes phase 5.</summary>
        private void CommitChange()
        {
            Expedition.CommitExternal();
            Expedition.RaiseSnapshotChanged();
        }

        private void Publish(bool raise)
        {
            baseOperations.TryGetSnapshot(out BaseOperationsSnapshot b);
            published = BuildSnapshot(b);
            if (raise) SnapshotChanged?.Invoke(published);
        }
    }
}
