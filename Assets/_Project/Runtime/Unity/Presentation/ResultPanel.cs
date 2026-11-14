using System.Globalization;
using Trainer.Application;
using UnityEngine;

namespace Trainer.Unity.Presentation
{
    public sealed class ResultPanel
    {
        public string Text { get; private set; } = "Resultado: esperando estado.";

        public void Show(MissionSnapshot snapshot)
        {
            if (snapshot == null) { Text = "Resultado: esperando estado."; return; }
            string state;
            switch (snapshot.State)
            {
                case SessionState.Ready: state = "Lista para iniciar"; break;
                case SessionState.Running: state = "En marcha"; break;
                case SessionState.Paused: state = "En pausa"; break;
                case SessionState.Completed: state = "Misión completada"; break;
                case SessionState.Interrupted: state = "Misión interrumpida"; break;
                default: state = "Estado desconocido"; break;
            }
            Text = string.Format(CultureInfo.InvariantCulture,
                "{0}\nTiempo simulado: {1:0.##} s · Trabajo científico: {2:0.##} s\nÉpoca: {3} · Revisión: {4}",
                state, snapshot.Power.SimulatedTimeSeconds, snapshot.ScienceWorkSeconds,
                snapshot.Epoch, snapshot.Revision);
        }

        public void Draw() { GUILayout.Label(Text); }
    }
}
