using Trainer.Application;
using Trainer.Unity.Presentation;
using UnityEngine;

namespace Trainer.Unity.World
{
    /// <summary>Companion to the existing IMGUI HUD. D2 supplies the HUD and scene camera explicitly.</summary>
    [DisallowMultipleComponent]
    public sealed class PowerRoomView : MonoBehaviour
    {
        [SerializeField] private MissionHUD hud;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private PowerStationView[] stations;
        private MissionHUD subscribedHud;
        private PowerStationView selected;

        public PowerStationView SelectedStation => selected;
        public bool HasCurrentData => isActiveAndEnabled && hud != null && hud.isActiveAndEnabled && hud.Snapshot != null;
        public bool CanRequestScience => isActiveAndEnabled && HasCurrentData && !hud.IsPending &&
            selected != null && selected.Kind == PowerStationKind.Science &&
            (hud.Snapshot.State == SessionState.Ready || hud.Snapshot.State == SessionState.Running ||
             hud.Snapshot.State == SessionState.Paused);

        /// <summary>No port is created here. The HUD owns the single ISessionPort binding and pending request.</summary>
        public void Connect(MissionHUD missionHud, Camera sceneCamera)
        {
            Detach();
            hud = missionHud;
            viewCamera = sceneCamera;
            selected = null;
            if (isActiveAndEnabled) Attach();
            else ClearVisuals();
        }

        public void SelectStation(PowerStationKind kind)
        {
            if (!isActiveAndEnabled) return;
            selected = null;
            if (stations != null)
                foreach (var station in stations)
                    if (station != null && station.Kind == kind) { selected = station; break; }
            Refresh();
        }

        public void SetScienceEnabled(bool enabled)
        {
            if (CanRequestScience) hud.SetScienceEnabled(enabled);
        }

        private void OnEnable() { Attach(); }
        private void OnDisable() { Detach(); selected = null; ClearVisuals(); }
        private void OnDestroy() { Detach(); }

        private void Attach()
        {
            if (hud != null && subscribedHud == null)
            {
                subscribedHud = hud;
                subscribedHud.PresentationChanged += Refresh;
            }
            Refresh();
        }

        private void Detach()
        {
            if (subscribedHud != null) subscribedHud.PresentationChanged -= Refresh;
            subscribedHud = null;
        }

        private void Refresh()
        {
            var snapshot = isActiveAndEnabled && HasCurrentData ? hud.Snapshot : null;
            if (snapshot == null) selected = null;
            if (stations == null) return;
            foreach (var station in stations)
                if (station != null) station.Show(snapshot, selected == station);
        }

        private void ClearVisuals()
        {
            if (stations == null) return;
            foreach (var station in stations) if (station != null) station.Show(null, false);
        }

        private void OnGUI()
        {
            if (stations == null) return;
            var previous = GUI.color;
            var labelStyle = new GUIStyle(GUI.skin.button) { wordWrap = true, fontSize = 14 };
            foreach (var station in stations)
            {
                if (station == null || !station.isActiveAndEnabled || viewCamera == null || !viewCamera.isActiveAndEnabled) continue;
                Vector3 screen = viewCamera.WorldToScreenPoint(station.transform.position);
                if (screen.z <= 0 || !viewCamera.pixelRect.Contains(new Vector2(screen.x, screen.y))) continue;
                // Named buttons and sprite hit areas use the existing IMGUI input stack.
                var rect = new Rect(screen.x - 108, Screen.height - screen.y + 88, 216, 104);
                if (hud != null && rect.Overlaps(hud.PanelRect)) continue;
                var spriteRect = new Rect(screen.x - 80, Screen.height - screen.y - 80, 160, 160);
                if ((hud == null || !spriteRect.Overlaps(hud.PanelRect)) &&
                    GUI.Button(spriteRect, GUIContent.none, GUIStyle.none)) SelectStation(station.Kind);
                GUI.color = station.IsSelected ? new Color(1f, .85f, .48f) : Color.white;
                var content = new GUIContent(station.Title + "\n" + station.StatusText);
                if (GUI.Button(rect, content, labelStyle)) SelectStation(station.Kind);
                if (station.Icon != null) GUI.DrawTexture(new Rect(rect.x + 6, rect.y - 28, 24, 24), station.Icon, ScaleMode.ScaleToFit);
            }
            GUI.color = previous;

            float left = hud != null ? hud.PanelRect.xMax + 12 : 12;
            float width = Screen.width - left - 12;
            if (width < 260) return; // The HUD remains fully functional in narrow windows.
            GUILayout.BeginArea(new Rect(left, 12, width, 112), GUI.skin.box);
            GUILayout.Label("SALA COOPERATIVA DE ENERGÍA · Selecciona una estación");
            GUILayout.Label(hud != null ? hud.ConnectionText : "Sala sin conectar · Sin datos vigentes");
            GUILayout.Label(selected != null ? "Selección: " + selected.Title : "Batería · Servicio esencial · Instrumento científico");
            if (viewCamera == null) GUILayout.Label("Vista de sala sin conectar.");
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(left, Mathf.Max(136, Screen.height - 158), width, 146), GUI.skin.box);
            GUILayout.Label(!HasCurrentData ? "Sin datos vigentes. Esperando conexión." :
                selected != null ? selected.Title + " · " + selected.StatusText : "Selecciona CIENCIA para solicitar un cambio.");
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && CanRequestScience;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Activar ciencia", GUILayout.MinHeight(36))) SetScienceEnabled(true);
            if (GUILayout.Button("Desactivar ciencia", GUILayout.MinHeight(36))) SetScienceEnabled(false);
            GUILayout.EndHorizontal();
            GUI.enabled = wasEnabled;
            GUILayout.Label(hud != null ? hud.CommandStatus : "Sin solicitudes.");
            GUILayout.EndArea();
        }
    }
}
