using System.Collections.Generic;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Unity.Networking.Outpost;
using UnityEngine;

namespace Trainer.Unity.Networking.Expedition
{
    /// <summary>
    /// Feeds phase 3 crew frames (zone, suit, suit supply, margin, protection, received instruction) to every
    /// <see cref="IExpeditionCrewView"/> on the avatars that the phase 2 <see cref="CrewAvatarSync"/> already shows. It
    /// adds no avatar, moves nothing and keeps no state of its own: positions, presence and the camera stay in the phase 2
    /// component, and the data is the confirmed phase 3 snapshot of the same port.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionCrewPresenter : MonoBehaviour
    {
        [SerializeField] private CrewAvatarSync avatars;

        private IExpeditionSessionPort port;
        private readonly List<IExpeditionCrewView> views = new List<IExpeditionCrewView>();

        public void Bind(IExpeditionSessionPort value, CrewAvatarSync avatarSync = null)
        {
            if (avatarSync != null) avatars = avatarSync;
            if (port != null) port.ExpeditionChanged -= OnExpedition;
            port = value;
            if (port == null) return;
            port.ExpeditionChanged += OnExpedition;
            if (port.Membership.IsJoined && port.TryGetExpedition(out ExpeditionSessionSnapshot current)) OnExpedition(current);
        }

        private void OnDestroy() => Bind(null);

        private void OnExpedition(ExpeditionSessionSnapshot snapshot)
        {
            if (avatars == null || port == null || !port.Membership.IsJoined) return;
            PlayerId local = port.Membership.Player;
            foreach (ExpeditionCrewState member in snapshot.Crew)
            {
                if (!avatars.TryGetAvatar(member.Player, out GameObject root)) continue;
                if (!ExpeditionCrewFrame.TryBuild(snapshot, member.Player, local, out ExpeditionCrewFrame frame)) continue;
                views.Clear();
                root.GetComponentsInChildren(true, views);
                foreach (IExpeditionCrewView view in views) view.PresentExpedition(frame);
            }
        }
    }
}
