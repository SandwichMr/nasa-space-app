using Trainer.Application.Outpost;
using UnityEngine;

namespace Trainer.Unity.World.Outpost
{
    /// <summary>Appearance only. D3 owns the root, presence, interpolation and camera position.</summary>
    [DisallowMultipleComponent]
    public sealed class CrewVisual : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer body;
        [SerializeField] private SpriteRenderer marker;
        [SerializeField] private TextMesh badge;
        [SerializeField] private Animator animator;

        private static readonly Color[] Colors = {
            new Color(.34f, .90f, .95f), new Color(1f, .68f, .35f),
            new Color(.79f, .60f, 1f), new Color(.58f, .93f, .45f),
            new Color(1f, .53f, .68f), new Color(1f, .89f, .38f),
            new Color(.46f, .65f, 1f), new Color(.95f, .95f, .95f)
        };

        public int FacingIndex { get; private set; }
        public bool Walking { get; private set; }
        public PlayerId Player { get; private set; }
        public bool IsLocal { get; private set; }
        public Color CrewColor { get; private set; }

        // Called by the optional ICrewAvatarView adapter, or by a labelled Editor fixture.
        // Does not set position, translate, read input, spawn crew or send commands.
        public void Present(PlayerId player, int slot, bool isLocal, MoveDirection facing,
            CrewMotion motion, StationId workingAt)
        {
            Player = player;
            IsLocal = isLocal;
            CrewColor = Colors[Mathf.Clamp(slot, 0, Colors.Length - 1)];
            FacingIndex = System.Math.Abs(facing.X) > System.Math.Abs(facing.Y)
                ? (facing.X > 0 ? 1 : 3) : (facing.Y > 0 ? 2 : 0);
            Walking = motion == CrewMotion.Walking;
            if (body != null) body.color = Color.Lerp(Color.white, CrewColor, .23f);
            if (marker != null) marker.color = CrewColor;
            if (badge != null)
            {
                badge.text = (isLocal ? "TÚ · " : "") + (slot + 1) + (!workingAt.IsValid ? "" : " · TAREA");
                badge.color = CrewColor;
            }
            if (animator != null)
            {
                animator.SetInteger("Facing", FacingIndex);
                animator.SetBool("Walking", Walking);
            }
        }

        private void LateUpdate()
        {
            int order = 1000 - Mathf.RoundToInt(transform.position.y * 20);
            if (body != null) body.sortingOrder = order;
            if (marker != null) marker.sortingOrder = order - 1;
            if (badge != null) badge.GetComponent<MeshRenderer>().sortingOrder = 3000;
        }
    }
}
