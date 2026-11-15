using Trainer.Unity.Networking.Outpost;
using UnityEngine;

namespace Trainer.Unity.World.Outpost
{
    // Installed explicitly by Phase02VisualAssets after D2 brings D3 into integration.
    // Depends on D3's ICrewAvatarView; no copied/shared interface, movement or networking here.
    [RequireComponent(typeof(CrewVisual))]
    public sealed class D3CrewVisualAdapter : MonoBehaviour, ICrewAvatarView
    {
        private CrewVisual visual;
        public void Present(CrewAvatarFrame frame)
        {
            if (visual == null) visual = GetComponent<CrewVisual>();
            visual.Present(frame.Player, frame.Slot, frame.IsLocal, frame.Facing, frame.Motion, frame.WorkingAt);
        }
    }
}
