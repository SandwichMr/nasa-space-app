using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.Eva;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Application.BaseOperations
{
    /// <summary>
    /// The SINGLE phase 4 session authority (D2): the living base. It composes the phase 3 <see cref="ExpeditionAuthority"/>
    /// (the only roster, base model, EVA model and clock) with an injected continuous close policy, and adds the only
    /// equipment model (<see cref="IBaseEquipmentModel"/>, D1), station operations, observations, repairs, orders,
    /// activities, gauges and the irreversible rule. There is ONE clock: the phase 3 Tick; the equipment advances inside
    /// it, right before the base, with the same step (docs/phase-04/SESSION_CONTRACT.md §6).
    ///
    /// It never adds or removes stock itself: every movement is a D1 <see cref="ResourceTransfer"/> or
    /// <see cref="EquipmentEffect"/> with ids derived from the command. Single-threaded; state and the command record
    /// are committed before events; subscriber exceptions propagate.
    /// </summary>
    public sealed partial class BaseOperationsAuthority : IBaseOperationsAuthority
    {
        private const double Tolerance = 1e-9;

        private sealed class StoredCommand
        {
            public PlayerId Sender;
            public BaseOperationCommand Command;
            public CommandResult Result;
        }

        private readonly OutpostBaseSimulation outpost;
        private readonly IBaseEquipmentModel equipment;
        private readonly ExpeditionAuthority inner;
        private readonly ContinuousPolicy policy;
        private readonly Dictionary<Guid, StoredCommand> commands = new Dictionary<Guid, StoredCommand>();
        private BaseOperationsSnapshot published;
        private ExpeditionSessionSnapshot publishedFrom;

        public event Action<BaseOperationsSnapshot> SnapshotChanged;
        public event Action<PlayerId, CommandResult> CommandCompleted;

        public BaseOperationsScenario Scenario { get; }
        public BaseSessionMode Mode { get; }

        /// <summary>The composed phase 3 authority (read-only use: Route, SealedSpeed, diagnostics).</summary>
        public ExpeditionAuthority Expedition => inner;

        /// <param name="outpost">D1 base model; also the <see cref="IAirServiceCapacity"/> of the session.</param>
        /// <param name="equipment">D1 equipment model, reset here with the scenario configuration.</param>
        /// <param name="roster">D3 roster built for the scenario layout; must be empty.</param>
        public BaseOperationsAuthority(BaseOperationsScenario scenario, OutpostBaseSimulation outpost, IBaseEquipmentModel equipment,
            IEvaSimulation eva, IZonedCrewRoster roster, BaseSessionMode mode = BaseSessionMode.Continuous,
            double maxStepSeconds = ExpeditionAuthority.DefaultMaxStepSeconds)
        {
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            this.outpost = outpost ?? throw new ArgumentNullException(nameof(outpost));
            this.equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
            Mode = OutpostGuard.Defined(mode, nameof(mode));
            policy = new ContinuousPolicy(this);
            equipment.Reset(scenario.Equipment);
            inner = new ExpeditionAuthority(scenario.Expedition, outpost, eva, roster, maxStepSeconds, policy);
            policy.ResetEpochState();
            inner.SnapshotChanged += OnInnerSnapshot;
            inner.CommandCompleted += (sender, result) => CommandCompleted?.Invoke(sender, result);
            Rebuild();
        }

        // ---------------------------------------------------------------- IBaseOperationsAuthority

        public OutpostMembership Join() => inner.Join();

        public bool Leave(PlayerId player) => inner.Leave(player);

        public MovementInputDisposition SubmitMovement(PlayerId sender, MovementInput input) => inner.SubmitMovement(sender, input);

        public void Submit(PlayerId sender, ExpeditionCommand command) => inner.Submit(sender, command);

        public bool TryGetSnapshot(out BaseOperationsSnapshot snapshot)
        {
            inner.TryGetSnapshot(out ExpeditionSessionSnapshot current);
            if (!ReferenceEquals(current, publishedFrom)) Rebuild();
            snapshot = published;
            return true;
        }

        /// <summary>The single clock: advances the session by simulated seconds (runner only). Nothing happens unless Running.</summary>
        public void Tick(double elapsedSimulatedSeconds) => inner.Tick(elapsedSimulatedSeconds);

        public void Submit(PlayerId sender, BaseOperationCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (command.Id == Guid.Empty)
            {
                CommandCompleted?.Invoke(sender, Reject(command, OutpostReasons.InvalidCommandId));
                return;
            }
            if (commands.TryGetValue(command.Id, out StoredCommand stored))
            {
                bool same = stored.Sender == sender && stored.Command.HasSameContent(command);
                CommandCompleted?.Invoke(sender, same ? stored.Result : Reject(command, OutpostReasons.CommandIdConflict));
                return;
            }
            if (inner.HasCommand(command.Id))
            {
                CommandCompleted?.Invoke(sender, Reject(command, OutpostReasons.CommandIdConflict));
                return;
            }

            string rejection = policy.Validate(sender, command);
            bool changed = false;
            Exception fault = null;
            if (rejection == null)
            {
                try
                {
                    policy.Accept(sender, command);
                    inner.CommitExternal();
                }
                catch (Exception e)
                {
                    fault = e;
                    rejection = OutpostReasons.SimulationFault;
                    inner.FaultExternal();
                }
                changed = true;
            }
            var result = new CommandResult(command.Id, command.Epoch, rejection == null, rejection ?? OutpostReasons.Accepted);
            commands.Add(command.Id, new StoredCommand { Sender = sender, Command = command, Result = result });
            try
            {
                if (changed) inner.RaiseSnapshotChanged();
            }
            finally
            {
                CommandCompleted?.Invoke(sender, result);
            }
            if (fault != null) ExceptionDispatchInfo.Capture(fault).Throw();
        }

        private static CommandResult Reject(BaseOperationCommand command, string reason) =>
            new CommandResult(command.Id, command.Epoch, false, reason);

        // ---------------------------------------------------------------- publication

        private void OnInnerSnapshot(ExpeditionSessionSnapshot snapshot)
        {
            Rebuild();
            SnapshotChanged?.Invoke(published);
        }

        private void Rebuild()
        {
            inner.TryGetSnapshot(out ExpeditionSessionSnapshot current);
            published = policy.Build(current);
            publishedFrom = current;
        }
    }
}
