using System.Collections.Generic;
using Trainer.Application.Crew;
using Trainer.Application.Expedition;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Trainer.Unity.Controls
{
    /// <summary>
    /// Phase 3 keys of the LOCAL player (Input System, as in phase 2): E interacts with the station in reach through
    /// <see cref="ExpeditionInteractionClient"/> (cancel the own task; cycle at an airlock panel; otherwise start the
    /// station's task) and R declares the team's return. Movement stays in the phase 2 <see cref="CrewKeyboardInput"/>
    /// (with its own E disabled in the phase 3 scene), so there is a single movement controller. Sends intents only.
    /// Any UI that owns the keyboard blocks these keys with <see cref="Block"/>/<see cref="Unblock"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionInteractionInput : MonoBehaviour
    {
        [SerializeField] private bool interactWithE = true;
        [SerializeField] private bool declareReturnWithR = true;

        private readonly HashSet<object> blockers = new HashSet<object>();
        private ExpeditionInteractionClient interaction;

        public ExpeditionInteractionClient Interaction => interaction;
        public bool IsBlocked => blockers.Count > 0;

        /// <summary>Connects to the local player's port and the build's scenario. Null unbinds.</summary>
        public ExpeditionInteractionClient Bind(IExpeditionSessionPort port, ExpeditionScenario scenario)
        {
            interaction?.Dispose();
            interaction = port != null && scenario != null ? new ExpeditionInteractionClient(port, scenario) : null;
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

        private void Update()
        {
            if (interaction == null || IsBlocked) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (interactWithE && keyboard.eKey.wasPressedThisFrame) interaction.Interact();
            else if (declareReturnWithR && keyboard.rKey.wasPressedThisFrame) interaction.RequestDeclareReturn();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) blockers.Remove(this);
            else blockers.Add(this);
        }

        private void OnDestroy() => interaction?.Dispose();
    }
}
