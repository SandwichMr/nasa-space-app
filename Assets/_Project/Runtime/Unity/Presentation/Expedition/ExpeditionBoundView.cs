using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using UnityEngine;
namespace Trainer.Unity.Presentation.Expedition
{
    // One binding lifetime; no commands, local simulation, or stale state revival.
    public abstract class ExpeditionBoundView : MonoBehaviour, IExpeditionView
    {
        protected ExpeditionViewContext Context { get; private set; }
        public ExpeditionSessionSnapshot Shown { get; private set; }
        public bool Live => isActiveAndEnabled && Context != null && Context.Port.Membership.IsJoined && Shown != null;
        public void Bind(ExpeditionViewContext context)
        {
            Unbind();
            if (!isActiveAndEnabled || context == null) return;
            Context = context;
            context.Port.ExpeditionChanged += Receive;
            context.Port.MembershipChanged += Membership;
            OnBound();
            Refresh();
        }
        public void Unbind()
        {
            if (Context != null)
            {
                Context.Port.ExpeditionChanged -= Receive;
                Context.Port.MembershipChanged -= Membership;
                Context.UnblockMovement(this);
                OnUnbinding();
            }
            Context = null; Shown = null; ClearView();
        }
        private void Membership(OutpostMembership value)
        {
            // A focused details panel must not leave D3's input blocked after disconnect/rejoin.
            Context.UnblockMovement(this);
            Shown = null;
            ClearView();
            if (value.IsJoined) { OnMembershipRestored(); Refresh(); }
        }
        private void Refresh()
        {
            if (Context != null && Context.Port.Membership.IsJoined && Context.Port.TryGetExpedition(out var s)) Receive(s);
        }
        private void Receive(ExpeditionSessionSnapshot s)
        {
            if (Context == null || !Context.Port.Membership.IsJoined || s.Outpost.ScenarioId != Context.Scenario.Outpost.Id || !s.IsNewerThan(Shown)) return;
            bool restart = Shown != null && s.Epoch != Shown.Epoch;
            Shown = s;
            if (restart) { Context.UnblockMovement(this); OnRestart(); }
            Present(s);
        }
        protected virtual void OnMembershipRestored() { }
        protected virtual void OnBound() { }
        protected virtual void OnUnbinding() { }
        protected virtual void OnRestart() { }
        protected abstract void Present(ExpeditionSessionSnapshot snapshot);
        protected abstract void ClearView();
        protected virtual void OnDisable() => Unbind();
        protected virtual void OnDestroy() => Unbind();
    }
}
