using System.Globalization;
using Trainer.Application;
using UnityEngine;

namespace Trainer.Unity.Presentation
{
    public sealed class ResultPanel
    {
        public string Text { get; private set; } = "Resultado: esperando estado.";

        public void Show(MissionSnapshot snapshot, MissionRules rules = null, MissionOutcome? outcome = null)
        {
            if (snapshot == null) { Text = "Resultado: esperando estado."; return; }
            string state;
            switch (snapshot.State)
            {
                case SessionState.Ready: state = "Lista para iniciar"; break;
                case SessionState.Running: state = "En marcha"; break;
                case SessionState.Paused: state = "En pausa"; break;
                case SessionState.Completed:
                    MissionOutcome? completed = outcome ?? (rules == null ? (MissionOutcome?)null
                        : MissionOutcomeEvaluator.Classify(rules, snapshot));
                    state = completed == MissionOutcome.ObjectiveAchieved ? "Objetivo logrado · Misión completada"
                        : completed == MissionOutcome.TimeExpired ? "Tiempo agotado · Objetivo pendiente"
                        : "Misión finalizada · Resultado sin clasificar";
                    break;
                case SessionState.Interrupted:
                    MissionOutcome? interrupted = outcome ?? (rules == null ? (MissionOutcome?)null
                        : MissionOutcomeEvaluator.Classify(rules, snapshot));
                    state = interrupted == MissionOutcome.EssentialServiceLost
                        ? "Misión interrumpida · Servicio esencial insuficiente"
                        : interrupted == MissionOutcome.SimulationFault
                            ? "Misión interrumpida · Error de simulación" : "Misión interrumpida";
                    break;
                default: state = "Estado desconocido"; break;
            }
            string progress = rules == null
                ? string.Format(CultureInfo.InvariantCulture, "Trabajo científico: {0:0.##} s · Objetivo sin configurar", snapshot.ScienceWorkSeconds)
                : string.Format(CultureInfo.InvariantCulture,
                    "Trabajo científico: {0:0.##} / {1:0.##} s · Faltan {2:0.##} s\nLímite: {3:0.##} s simulados",
                    snapshot.ScienceWorkSeconds, rules.ScienceWorkTargetSeconds,
                    System.Math.Max(0, rules.ScienceWorkTargetSeconds - snapshot.ScienceWorkSeconds), rules.MissionDurationSeconds);
            Text = string.Format(CultureInfo.InvariantCulture,
                "{0}\nTiempo simulado: {1:0.##} s\n{2}\nÉpoca: {3} · Revisión: {4}",
                state, snapshot.Power.SimulatedTimeSeconds, progress, snapshot.Epoch, snapshot.Revision);
        }

        public void Draw() { GUILayout.Label(Text, new GUIStyle(GUI.skin.label) { wordWrap = true }); }
    }
}
