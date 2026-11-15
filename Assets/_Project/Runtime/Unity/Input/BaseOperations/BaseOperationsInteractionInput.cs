using System;
using System.Collections.Generic;
using Trainer.Application.BaseOperations;
using Trainer.Application.Crew;
using Trainer.Application.Outpost;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Trainer.Unity.Controls
{
    /// <summary>
    /// Phase 4 interaction key of the LOCAL player (Input System, as in phases 2–3). E, in order:
    /// <list type="number">
    /// <item>own phase 4 operation in progress → cancel it (<see cref="BaseOperationsInteractionClient.Interact"/>);</item>
    /// <item>a phase 4 station (water, air, logistics) in reach → <see cref="StationActivated"/>, so a D4 panel can offer the
    /// decisions of that station (they need a choice, so the key never picks one by itself);</item>
    /// <item>otherwise → the phase 3 interaction (own task, airlock panel, station task), when one is bound.</item>
    /// </list>
    /// Movement stays in the phase 2 <c>CrewKeyboardInput</c> (one movement controller) and R stays in the phase 3
    /// <c>ExpeditionInteractionInput</c>; in the phase 4 scene the composition disables the phase 3 E so the key is handled
    /// once. Sends intents only. Any UI that owns the keyboard blocks it with <see cref="Block"/>/<see cref="Unblock"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BaseOperationsInteractionInput : MonoBehaviour
    {
        [SerializeField] private bool interactWithE = true;

        private readonly HashSet<object> blockers = new HashSet<object>();
        private BaseOperationsInteractionClient interaction;
        private ExpeditionInteractionClient expedition;

        public BaseOperationsInteractionClient Interaction => interaction;
        public bool IsBlocked => blockers.Count > 0;

        /// <summary>E at a phase 4 station in reach (no own operation in progress).</summary>
        public event Action<StationId> StationActivated;

        /// <summary>Connects to the local player's port and the build's scenario; <paramref name="phase3"/> (optional) handles E
        /// away from phase 4 stations. A null port or scenario unbinds.</summary>
        public BaseOperationsInteractionClient Bind(IBaseOperationsSessionPort port, BaseOperationsScenario scenario,
            ExpeditionInteractionClient phase3 = null)
        {
            interaction?.Dispose();
            interaction = port != null && scenario != null ? new BaseOperationsInteractionClient(port, scenario) : null;
            expedition = interaction != null ? phase3 : null;
            return interaction;
        }

        public void Block(object owner)
        {
            if (owner != null) blockers.Add(owner);
        }

        public void Unblock(object owner)
        {
            if (owner != null) blockers.Remove(owner);
        }

        /// <summary>The E behaviour, callable without a keyboard (tests, on-screen button). True when something was sent or a
        /// station was activated.</summary>
        public bool Activate()
        {
            if (interaction == null || IsBlocked) return false;
            if (interaction.Interact()) return true;
            if (interaction.TryFindStationInReach(out StationPlacement station))
            {
                StationActivated?.Invoke(station.Id);
                return true;
            }
            return expedition != null && expedition.Interact();
        }

        private void Update()
        {
            if (!interactWithE || interaction == null || IsBlocked) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.eKey.wasPressedThisFrame) Activate();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) blockers.Remove(this);
            else blockers.Add(this);
        }

        private void OnDestroy() => interaction?.Dispose();
    }
}
