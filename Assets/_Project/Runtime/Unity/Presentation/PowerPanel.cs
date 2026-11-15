using System.Globalization;
using Trainer.Simulation;
using UnityEngine;

namespace Trainer.Unity.Presentation
{
    public sealed class PowerPanel
    {
        public string Text { get; private set; } = "Batería y cargas: esperando estado.";

        public void Show(PowerSnapshot power)
        {
            Text = power == null ? "Batería y cargas: esperando estado." : string.Format(
                CultureInfo.InvariantCulture,
                "Batería: {0:0.##} / {1:0.##} J\nPotencias del último paso medido (Reset: lectura inicial).\nGeneración: {2:0.##} W\n" +
                "Carga esencial — solicitada: {3:0.##} W · servida: {4:0.##} W\n" +
                "Ciencia — solicitada: {5:0.##} W · servida: {6:0.##} W",
                power.EnergyJoules, power.CapacityJoules, power.GenerationWatts,
                power.EssentialRequestedWatts, power.EssentialServedWatts,
                power.ScienceRequestedWatts, power.ScienceServedWatts);
        }

        public void Draw() { GUILayout.Label(Text, new GUIStyle(GUI.skin.label) { wordWrap = true }); }
    }
}
