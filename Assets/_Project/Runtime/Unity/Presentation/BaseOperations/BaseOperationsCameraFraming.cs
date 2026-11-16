using UnityEngine;

namespace Trainer.Unity.Presentation.BaseOperations
{
    /// <summary>Opt-in viewport framing on D2's EXISTING camera. D3 alone owns camera position/follow.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class BaseOperationsCameraFraming : MonoBehaviour
    {
        [SerializeField] private BaseOperationsPanel panel;
        [SerializeField, Min(2)] private float visibleHeightMeters = 10.8f;
        private Camera target;
        private Rect previousRect;
        private float previousSize;
        public void Configure(BaseOperationsPanel value) { panel = value; }
        private void OnEnable()
        {
            target = GetComponent<Camera>(); previousRect = target.rect; previousSize = target.orthographicSize;
        }
        private void LateUpdate()
        {
            if (target == null || !target.orthographic) return;
            float scale = Mathf.Min(Screen.width / 1440f, Screen.height / 900f);
            float insetX = (Screen.width - 1440 * scale) / 2, insetY = (Screen.height - 900 * scale) / 2;
            bool focused = panel != null && panel.Live && (panel.OpenStation.IsValid || panel.DetailsOpen);
            // The world shows between the dashboard's top block and its footer (canvas y grows down, pixels grow up).
            float top = panel != null ? panel.TopInset : BaseOperationsPanel.TopBlockBottom;
            float bottom = 900 - BaseOperationsPanel.FooterTop, height = BaseOperationsPanel.FooterTop - top;
            target.pixelRect = new Rect(insetX, insetY + bottom * scale, (focused ? 944 : 1440) * scale, height * scale);
            target.orthographicSize = visibleHeightMeters / 2;
        }
        private void OnDisable()
        {
            if (target != null) { target.rect = previousRect; target.orthographicSize = previousSize; }
        }
    }
}
