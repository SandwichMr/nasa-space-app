using System;
using Trainer.Application.Outpost;
using Trainer.Simulation.Eva;

namespace Trainer.Application.Expedition
{
    /// <summary>
    /// Intents a phase 3 view may send (implemented by D3's interaction client for the LOCAL player). Views never
    /// build commands themselves: they call these, and the authority decides. One request in flight; false when
    /// nothing was sent (not joined, no state, busy, or nothing to do).
    /// </summary>
    public interface IExpeditionIntents
    {
        /// <summary>"E": cancel the own task if any; at an airlock panel, cycle; else start the station's task.</summary>
        bool Interact();
        bool RequestStart(StationId station, TaskTypeId task);
        bool RequestCancel(StationId station);
        bool RequestCycle(StationId airlockPanel);
        bool RequestDeclareReturn();
        bool HasPending { get; }
        /// <summary>Reason code of the last answer to this client (Accepted or a rejection), or null.</summary>
        string LastReason { get; }
        /// <summary>The authority answered (reason code) or a request was abandoned (client-only code).</summary>
        event Action<string> Answered;
    }

    /// <summary>
    /// What a phase 3 view receives from the scene composition (D2): the local participant's port, the build's
    /// scenario, the intents and a way to hold the keyboard (a focused panel blocks movement; it must unblock).
    /// </summary>
    public sealed class ExpeditionViewContext
    {
        public IExpeditionSessionPort Port { get; }
        public ExpeditionScenario Scenario { get; }
        public IExpeditionIntents Intents { get; }
        private readonly Action<object, bool> setMovementBlocked;

        public ExpeditionViewContext(IExpeditionSessionPort port, ExpeditionScenario scenario, IExpeditionIntents intents,
            Action<object, bool> setMovementBlocked)
        {
            Port = port ?? throw new ArgumentNullException(nameof(port));
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            Intents = intents ?? throw new ArgumentNullException(nameof(intents));
            this.setMovementBlocked = setMovementBlocked ?? throw new ArgumentNullException(nameof(setMovementBlocked));
        }

        public void BlockMovement(object owner) => setMovementBlocked(owner, true);
        public void UnblockMovement(object owner) => setMovementBlocked(owner, false);
    }

    /// <summary>
    /// D4 mount point for phase 3 presentation (HUD, world overlays, result screen). Implemented by MonoBehaviours
    /// placed under the scene's presentation mount; the composition calls <see cref="Bind"/> once the port exists and
    /// <see cref="Unbind"/> before replacing it. A view only reads the port and calls the intents: no movement, clock,
    /// inventory or second evaluation lives in a view.
    /// </summary>
    public interface IExpeditionView
    {
        void Bind(ExpeditionViewContext context);
        void Unbind();
    }

    /// <summary>What an astronaut visual receives every phase 3 snapshot (D4 draws suit, zone and margin cues).</summary>
    public readonly struct ExpeditionCrewFrame
    {
        public PlayerId Player { get; }
        public int Slot { get; }
        public bool IsLocal { get; }
        public CrewZone Zone { get; }
        /// <summary>Worn suit key, or null.</summary>
        public string SuitKey { get; }
        /// <summary>Status of the worn suit; Stowed when wearing none.</summary>
        public SuitStatus SuitStatus { get; }
        public bool OnSuitSupply { get; }
        public SuitActivity Activity { get; }
        public ReturnMarginEstimate Margin { get; }
        public ProtectionState Protection { get; }
        /// <summary>This member received the instruction currently in force.</summary>
        public bool ReceivedCurrentInstruction { get; }
        public StationId WorkingAt { get; }

        public ExpeditionCrewFrame(PlayerId player, int slot, bool isLocal, CrewZone zone, string suitKey,
            SuitStatus suitStatus, bool onSuitSupply, SuitActivity activity, ReturnMarginEstimate margin,
            ProtectionState protection, bool receivedCurrentInstruction, StationId workingAt)
        {
            Player = player;
            Slot = slot;
            IsLocal = isLocal;
            Zone = zone;
            SuitKey = suitKey;
            SuitStatus = suitStatus;
            OnSuitSupply = onSuitSupply;
            Activity = activity;
            Margin = margin;
            Protection = protection;
            ReceivedCurrentInstruction = receivedCurrentInstruction;
            WorkingAt = workingAt;
        }

        /// <summary>Frame of <paramref name="player"/> from a snapshot; false if the member is not present.</summary>
        public static bool TryBuild(ExpeditionSessionSnapshot snapshot, PlayerId player, PlayerId local, out ExpeditionCrewFrame frame)
        {
            frame = default;
            if (snapshot == null || !snapshot.TryGetCrew(player, out ExpeditionCrewState member)
                || !snapshot.Outpost.TryGetCrew(player, out CrewMemberState pose)) return false;
            SuitState suit = member.SuitIndex >= 0 ? snapshot.Suits[member.SuitIndex] : null;
            StationId working = StationId.None;
            foreach (StationState station in snapshot.Outpost.Stations)
                if (station.Occupant == player) working = station.Station;
            bool received = snapshot.Instruction.InForce
                && snapshot.TryGetReception(snapshot.Instruction.LatestMessageId, player, out _);
            frame = new ExpeditionCrewFrame(player, pose.Slot, player == local, member.Zone, suit?.Key,
                suit?.Status ?? SuitStatus.Stowed, member.OnSuitSupply, member.Activity, member.Margin, member.Protection,
                received, working);
            return true;
        }
    }

    /// <summary>Implemented by D4 on the astronaut prefab (any component on it or its children), next to the phase 2
    /// <c>ICrewAvatarView</c>. Draws only; the root is positioned by the phase 2 avatar sync.</summary>
    public interface IExpeditionCrewView
    {
        void PresentExpedition(ExpeditionCrewFrame frame);
    }
}
