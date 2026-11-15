using System.Collections.Generic;
using Trainer.Application.Crew;
using Trainer.Application.Outpost;
using UnityEngine;

namespace Trainer.Unity.Networking.Outpost
{
    /// <summary>What a visual astronaut receives every frame (D4 renders it; no game logic lives in the view).</summary>
    public readonly struct CrewAvatarFrame
    {
        public PlayerId Player { get; }
        /// <summary>0..MaxCrew-1: use it for colour/name ("Astronauta Slot+1"). Not an identity.</summary>
        public int Slot { get; }
        /// <summary>True only for this participant's own astronaut.</summary>
        public bool IsLocal { get; }
        /// <summary>Unit vector of the last confirmed movement (for sprite flip / 4- or 8-way direction).</summary>
        public MoveDirection Facing { get; }
        /// <summary>Confirmed Idle/Walking (drive the walk animation from this, not from the smoothed transform).</summary>
        public CrewMotion Motion { get; }
        /// <summary>Station where this member performs a task, or None.</summary>
        public StationId WorkingAt { get; }

        public CrewAvatarFrame(PlayerId player, int slot, bool isLocal, MoveDirection facing, CrewMotion motion, StationId workingAt)
        {
            Player = player;
            Slot = slot;
            IsLocal = isLocal;
            Facing = facing;
            Motion = motion;
            WorkingAt = workingAt;
        }
    }

    /// <summary>Implemented by D4 on the astronaut prefab (any component on it or its children). The prefab root is
    /// positioned by <see cref="CrewAvatarSync"/>; the view only draws (sprite, animator, colour, local marker).</summary>
    public interface ICrewAvatarView
    {
        void Present(CrewAvatarFrame frame);
    }

    /// <summary>
    /// Shows one astronaut per present crew member from the CONFIRMED snapshots of an
    /// <see cref="IOutpostSessionPort"/>: spawns D4's prefab when a member appears, destroys it when the member
    /// leaves (no duplicates: one avatar per PlayerId), places it with <see cref="CrewInterpolator"/> (fixed
    /// delay, no extrapolation, snap on Restart/respawn) and follows ONLY the local astronaut with the camera.
    /// It never moves anyone in the simulation and sends nothing. Mapping: world X = X m, world Y = Y m,
    /// Z = <see cref="avatarZ"/> (1 Unity unit = 1 m, as recommended by the contract; D2 decides the scene).
    /// While not joined there is no live data, so every avatar is removed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrewAvatarSync : MonoBehaviour
    {
        [SerializeField, Tooltip("Astronaut prefab from D4; components implementing ICrewAvatarView receive frames.")]
        private GameObject avatarPrefab;
        [SerializeField] private Transform avatarParent;
        [SerializeField, Tooltip("Follows the local astronaut only. Optional.")]
        private Camera followCamera;
        [SerializeField, Min(0)] private float cameraSmoothingSeconds = 0.15f;
        [SerializeField, Min(0)] private float interpolationDelaySeconds = 0.1f;
        [SerializeField, Min(0.01f)] private float teleportMeters = 1.5f;
        [SerializeField] private float avatarZ;

        private sealed class Avatar
        {
            public GameObject Root;
            public ICrewAvatarView[] Views;
        }

        private readonly Dictionary<PlayerId, Avatar> avatars = new Dictionary<PlayerId, Avatar>();
        private CrewInterpolator interpolator;
        private IOutpostSessionPort port;
        private Vector3 cameraVelocity;

        public IOutpostSessionPort Port => port;
        public int AvatarCount => avatars.Count;

        /// <summary>The avatar root shown for <paramref name="player"/> (read-only access for phase 3 decorations).</summary>
        public bool TryGetAvatar(PlayerId player, out GameObject root)
        {
            root = avatars.TryGetValue(player, out Avatar avatar) ? avatar.Root : null;
            return root != null;
        }

        /// <summary>The local astronaut's transform, or null.</summary>
        public Transform LocalAvatar =>
            port != null && port.Membership.IsJoined && avatars.TryGetValue(port.Membership.Player, out Avatar a)
                ? a.Root.transform : null;

        /// <summary>Connects to a port (network client adapter or host-local port). Null unbinds and clears.</summary>
        public void Bind(IOutpostSessionPort value)
        {
            if (ReferenceEquals(value, port)) return;
            Unsubscribe();
            ClearAll();
            port = value;
            if (port == null) return;
            port.SnapshotChanged += OnSnapshot;
            port.MembershipChanged += OnMembership;
            if (port.Membership.IsJoined && port.TryGetSnapshot(out OutpostSessionSnapshot current)) OnSnapshot(current);
        }

        private void Awake() => interpolator = new CrewInterpolator(interpolationDelaySeconds, teleportMeters);

        private void OnDestroy()
        {
            Unsubscribe();
            ClearAll();
        }

        private void Unsubscribe()
        {
            if (port == null) return;
            port.SnapshotChanged -= OnSnapshot;
            port.MembershipChanged -= OnMembership;
        }

        private void OnMembership(OutpostMembership membership)
        {
            if (!membership.IsJoined)
            {
                ClearAll(); // Not live: never leave stale astronauts on screen.
                return;
            }
            if (port.TryGetSnapshot(out OutpostSessionSnapshot current)) OnSnapshot(current);
        }

        private void OnSnapshot(OutpostSessionSnapshot snapshot)
        {
            if (port == null || !port.Membership.IsJoined) return;
            EnsureInterpolator();
            interpolator.Push(snapshot, Time.unscaledTimeAsDouble);
            var working = new Dictionary<PlayerId, StationId>();
            foreach (StationState station in snapshot.Stations)
                if (station.ActiveTask != null) working[station.ActiveTask.Performer] = station.Station;

            var present = new HashSet<PlayerId>();
            foreach (CrewMemberState member in snapshot.Crew)
            {
                present.Add(member.Player);
                if (!avatars.TryGetValue(member.Player, out Avatar avatar))
                {
                    avatar = Spawn(member);
                    avatars.Add(member.Player, avatar);
                }
                working.TryGetValue(member.Player, out StationId at);
                var frame = new CrewAvatarFrame(member.Player, member.Slot, member.Player == port.Membership.Player,
                    member.Pose.Facing, member.Pose.Motion, at);
                foreach (ICrewAvatarView view in avatar.Views) view.Present(frame);
            }
            var gone = new List<PlayerId>();
            foreach (PlayerId id in avatars.Keys) if (!present.Contains(id)) gone.Add(id);
            foreach (PlayerId id in gone) Despawn(id);
        }

        private void Update()
        {
            if (interpolator == null) return;
            double now = Time.unscaledTimeAsDouble;
            foreach (KeyValuePair<PlayerId, Avatar> entry in avatars)
                if (interpolator.TrySample(entry.Key, now, out BasePosition p))
                    entry.Value.Root.transform.localPosition = new Vector3((float)p.X, (float)p.Y, avatarZ);
        }

        private void LateUpdate()
        {
            Transform local = LocalAvatar;
            if (followCamera == null || local == null) return;
            Vector3 current = followCamera.transform.position;
            var target = new Vector3(local.position.x, local.position.y, current.z);
            followCamera.transform.position = cameraSmoothingSeconds > 0
                ? Vector3.SmoothDamp(current, target, ref cameraVelocity, cameraSmoothingSeconds, Mathf.Infinity, Time.unscaledDeltaTime)
                : target;
        }

        private Avatar Spawn(CrewMemberState member)
        {
            Transform parent = avatarParent != null ? avatarParent : transform;
            GameObject root = avatarPrefab != null
                ? Instantiate(avatarPrefab, parent, false)
                : new GameObject(); // Without D4's prefab the avatar is an empty marker (position only).
            root.name = "Astronaut " + member.Player;
            BasePosition p = member.Pose.Position;
            root.transform.localPosition = new Vector3((float)p.X, (float)p.Y, avatarZ);
            return new Avatar { Root = root, Views = root.GetComponentsInChildren<ICrewAvatarView>(true) };
        }

        private void Despawn(PlayerId id)
        {
            if (avatars.TryGetValue(id, out Avatar avatar) && avatar.Root != null) Destroy(avatar.Root);
            avatars.Remove(id);
        }

        private void ClearAll()
        {
            foreach (Avatar avatar in avatars.Values)
                if (avatar.Root != null) Destroy(avatar.Root);
            avatars.Clear();
            interpolator?.Clear();
        }

        private void EnsureInterpolator()
        {
            if (interpolator == null) interpolator = new CrewInterpolator(interpolationDelaySeconds, teleportMeters);
        }
    }
}
