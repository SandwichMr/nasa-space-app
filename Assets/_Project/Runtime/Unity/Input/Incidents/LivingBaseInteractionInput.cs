using System;
using System.Collections.Generic;
using Trainer.Application.Crew;
using Trainer.Application.Incidents;
using Trainer.Application.Outpost;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Trainer.Unity.Controls
{
    /// <summary>
    /// Phase 5 interaction key of the LOCAL player (Input System, D3). E, in order:
    /// <list type="number">
    /// <item>own phase 5 operation in progress → cancel it;</item>
    /// <item>a phase 5 station (power panel, upgrade station) in reach → <see cref="StationActivated"/>, so the D4 panel offers that
    /// station's decisions (they need a choice: the key never picks one);</item>
    /// <item>otherwise → the phase 4 key behaviour (<see cref="BaseOperationsInteractionInput.Activate"/>), which itself falls back to
    /// phase 3.</item>
    /// </list>
    /// In the phase 5 scene the phase 4 component keeps its E disabled so the key is handled once. Sends intents only. Any UI that owns
    /// the keyboard blocks it with <see cref="Block"/>/<see cref="Unblock"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LivingBaseInteractionInput : MonoBehaviour
    {
        [SerializeField] private bool interactWithE = true;
        [SerializeField, Tooltip("Phase 4 E behaviour used away from phase 5 stations (its own E stays disabled).")]
        private BaseOperationsInteractionInput phase4;

        private readonly HashSet<object> blockers = new HashSet<object>();
        private LivingBaseInteractionClient interaction;

        public LivingBaseInteractionClient Interaction => interaction;
        public bool IsBlocked => blockers.Count > 0;

        /// <summary>E at a phase 5 station in reach (no own phase 5 operation in progress).</summary>
        public event Action<StationId> StationActivated;

        public LivingBaseInteractionClient Bind(ILivingBaseSessionPort port, LivingBaseScenario scenario)
        {
            interaction?.Dispose();
            interaction = port != null && scenario != null ? new LivingBaseInteractionClient(port, scenario) : null;
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

        /// <summary>The E behaviour without a keyboard (autopilot, on-screen button). True when something was sent or activated.</summary>
        public bool Activate()
        {
            if (interaction == null || IsBlocked) return false;
            if (interaction.Interact()) return true;
            if (interaction.TryFindStationInReach(out StationPlacement station))
            {
                StationActivated?.Invoke(station.Id);
                return true;
            }
            return phase4 != null && phase4.Activate();
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
