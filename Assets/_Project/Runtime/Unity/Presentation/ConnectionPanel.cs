using UnityEngine;

namespace Trainer.Unity.Presentation
{
    public sealed class ConnectionPanel
    {
        public string Text { get; private set; } = "Sin puerto de sesión. Sin datos vigentes.";

        public void Show(bool assigned, string detail)
        {
            Text = assigned ? "Puerto asignado · " + detail : "Desconectado · Sin datos vigentes.";
        }

        public void Draw() { GUILayout.Label(Text, new GUIStyle(GUI.skin.label) { wordWrap = true }); }
    }
}
