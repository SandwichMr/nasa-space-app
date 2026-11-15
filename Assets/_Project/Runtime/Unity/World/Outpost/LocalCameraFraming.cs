using UnityEngine;

namespace Trainer.Unity.World.Outpost
{
    /// <summary>D2 opts in on its EXISTING orthographic camera. D3 alone follows the local root.
    /// This component reserves space for the secondary HUD; it never changes camera position/rotation.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class LocalCameraFraming : MonoBehaviour
    {
        [SerializeField, Min(2)] private float visibleHeightMeters = 9.5f;
        private Camera target;
        private Rect originalRect;
        private float originalSize;
        private void OnEnable()
        {
            target = GetComponent<Camera>(); originalRect = target.rect; originalSize = target.orthographicSize;
        }
        private void LateUpdate()
        {
            if (target == null || !target.orthographic) return;
            float scale = Mathf.Min(Screen.width/1280f, Screen.height/720f);
            float inset = (Screen.height-720*scale)/2;
            target.pixelRect = new Rect(0, inset+96*scale, Screen.width, Mathf.Max(1,528*scale));
            target.orthographicSize = visibleHeightMeters/2;
        }
        private void OnDisable()
        {
            if (target != null) { target.rect = originalRect; target.orthographicSize = originalSize; }
        }
    }
}
