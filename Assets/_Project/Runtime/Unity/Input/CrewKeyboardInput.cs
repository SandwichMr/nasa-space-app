using System.Collections.Generic;
using Trainer.Application.Crew;
using Trainer.Application.Outpost;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Trainer.Unity.Controls
{
    /// <summary>
    /// Local player's keyboard (Input System package, already the project's active input handler): WASD or
    /// arrows move, E interacts with the station in reach (start its first task, or cancel your own task).
    /// Sends intent only, through <see cref="CrewInputSender"/> and <see cref="StationInteractionClient"/>, for
    /// the identity of the bound port, so it can only ever drive the LOCAL astronaut.
    ///
    /// Focus: any UI that must own the keyboard (a task panel, a text field) calls <see cref="Block"/> with
    /// itself and <see cref="Unblock"/> when it closes; while blocked the astronaut is stopped once and E is
    /// ignored. Losing application focus stops the astronaut too, so no key stays "held" in the background.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrewKeyboardInput : MonoBehaviour
    {
        [SerializeField] private bool interactWithE = true;

        private readonly object applicationFocus = new object();
        private readonly HashSet<object> interactionBlockers = new HashSet<object>();
        private CrewInputSender sender;
        private StationInteractionClient interaction;

        public StationInteractionClient Interaction => interaction;
        public bool IsBlocked => sender != null && sender.IsBlocked;

        /// <summary>Connects to the local player's port and the build's scenario. Null unbinds.</summary>
        public void Bind(IOutpostSessionPort port, OutpostScenario scenario)
        {
            Stop();
            interaction?.Dispose();
            sender = null;
            interaction = null;
            if (port == null) return;
            sender = new CrewInputSender(port);
            foreach (object owner in interactionBlockers) sender.Block(owner);
            if (scenario != null) interaction = new StationInteractionClient(port, scenario);
        }

        public void Block(object owner)
        {
            if (owner == null) return;
            interactionBlockers.Add(owner);
            sender?.Block(owner);
        }

        public void Unblock(object owner)
        {
            if (owner == null) return;
            interactionBlockers.Remove(owner);
            sender?.Unblock(owner);
        }

        private void Update()
        {
            if (sender == null) return;
            Keyboard keyboard = Keyboard.current;
            double x = 0, y = 0;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) y -= 1;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) y += 1;
            }
            sender.Update(x, y);
            if (interactWithE && keyboard != null && keyboard.eKey.wasPressedThisFrame && !sender.IsBlocked)
                interaction?.Interact();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) Unblock(applicationFocus);
            else Block(applicationFocus);
            sender?.Update(0, 0); // Send the stop now; the next frame may not come while unfocused.
        }

        private void OnDisable() => Stop();

        private void OnDestroy()
        {
            Stop();
            interaction?.Dispose();
        }

        /// <summary>Stops the astronaut (zero direction) without unbinding.</summary>
        private void Stop()
        {
            if (sender == null) return;
            var stop = new object();
            sender.Block(stop);
            sender.Update(0, 0);
            sender.Unblock(stop);
        }
    }
}
