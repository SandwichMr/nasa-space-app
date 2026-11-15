using Trainer.Application.BaseOperations;
using Trainer.Application.Outpost;
using UnityEngine;

namespace Trainer.Unity.Presentation.BaseOperations
{
    /// <summary>One presentation binding, on Unity's main thread. No simulation or client clock.</summary>
    public abstract class BaseOperationsBoundView : MonoBehaviour, IBaseOperationsView
    {
        protected BaseOperationsViewContext Context { get; private set; }
        public BaseOperationsSnapshot Shown { get; private set; }
        public bool Live => isActiveAndEnabled && Context != null && Context.Port.Membership.IsJoined && Shown != null;

        public void Bind(BaseOperationsViewContext context)
        {
            Unbind();
            if (!isActiveAndEnabled || context == null) return;
            Context = context;
            Context.Port.BaseOperationsChanged += Receive;
            Context.Port.MembershipChanged += Membership;
            OnBound();
            Refresh();
        }

        public void Unbind()
        {
            if (Context != null)
            {
                Context.Port.BaseOperationsChanged -= Receive;
                Context.Port.MembershipChanged -= Membership;
                Context.UnblockMovement(this);
                OnUnbinding();
            }
            Context = null;
            Shown = null;
            ClearView();
        }

        private void Membership(OutpostMembership membership)
        {
            Context.UnblockMovement(this);
            Shown = null;
            ClearView();
            if (membership.IsJoined) Refresh();
        }

        private void Refresh()
        {
            if (Context.Port.Membership.IsJoined && Context.Port.TryGetBaseOperations(out var snapshot)) Receive(snapshot);
        }

        private void Receive(BaseOperationsSnapshot snapshot)
        {
            if (Context == null || !Context.Port.Membership.IsJoined || snapshot == null
                || snapshot.Outpost.ScenarioId != Context.Scenario.Id || !snapshot.IsNewerThan(Shown)) return;
            if (!snapshot.Outpost.TryGetCrew(Context.Port.Membership.Player, out _))
            {
                Context.UnblockMovement(this); Shown = null; ClearView(); return;
            }
            bool restart = Shown != null && snapshot.Epoch != Shown.Epoch;
            Shown = snapshot;
            if (restart) { Context.UnblockMovement(this); OnRestart(); }
            Present(snapshot);
        }

        protected virtual void OnBound() { }
        protected virtual void OnUnbinding() { }
        protected virtual void OnRestart() { }
        protected abstract void Present(BaseOperationsSnapshot snapshot);
        protected abstract void ClearView();
        protected virtual void OnDisable() => Unbind();
        protected virtual void OnDestroy() => Unbind();
    }
}
