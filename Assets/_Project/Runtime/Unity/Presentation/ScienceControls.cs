using System;
using Trainer.Application;
using UnityEngine;

namespace Trainer.Unity.Presentation
{
    public sealed class ScienceControls
    {
        public string Text { get; private set; } = "Ciencia: esperando estado.";

        public void Show(MissionSnapshot snapshot)
        {
            Text = snapshot == null ? "Ciencia: esperando estado." :
                "Ciencia opcional: " + (snapshot.ScienceEnabled ? "activada" : "desactivada") +
                " (estado confirmado)";
        }

        public void Draw(MissionSnapshot snapshot, bool available, Action<SessionAction, bool?> submit)
        {
            GUILayout.Label(Text, new GUIStyle(GUI.skin.label) { wordWrap = true });
            bool previous = GUI.enabled;
            bool active = snapshot != null && (snapshot.State == SessionState.Ready ||
                snapshot.State == SessionState.Running || snapshot.State == SessionState.Paused);
            GUI.enabled = previous && available && active;
            if (GUILayout.Button("Activar ciencia", GUILayout.MinHeight(38)))
                submit(SessionAction.SetScienceEnabled, true);
            if (GUILayout.Button("Desactivar ciencia", GUILayout.MinHeight(38)))
                submit(SessionAction.SetScienceEnabled, false);
            GUI.enabled = previous && available && snapshot != null &&
                (snapshot.State == SessionState.Ready || snapshot.State == SessionState.Paused);
            if (GUILayout.Button("Iniciar / continuar", GUILayout.MinHeight(38))) submit(SessionAction.Resume, null);
            GUI.enabled = previous && available && snapshot != null && snapshot.State == SessionState.Running;
            if (GUILayout.Button("Pausar", GUILayout.MinHeight(38))) submit(SessionAction.Pause, null);
            GUI.enabled = previous && available && snapshot != null;
            if (GUILayout.Button("Reiniciar misión", GUILayout.MinHeight(38))) submit(SessionAction.Restart, null);
            GUI.enabled = previous;
        }
    }
}
