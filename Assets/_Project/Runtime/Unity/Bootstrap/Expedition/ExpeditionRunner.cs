using System;
using System.Collections.Generic;
using Trainer.Application.Expedition;
using UnityEngine;

namespace Trainer.Unity.Bootstrap
{
    /// <summary>The single clock of the phase 3 session (D2). Exists and is bound ONLY on the authority; clients never
    /// tick, so no client has an independent clock. Same rules as the phase 2 <see cref="OutpostRunner"/>.</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionRunner : MonoBehaviour
    {
        private static readonly Dictionary<ExpeditionAuthority, ExpeditionRunner> owners = new Dictionary<ExpeditionAuthority, ExpeditionRunner>();
        private ExpeditionAuthority authority;
        private double timeScale;

        public bool IsBound => authority != null;

        public void Bind(ExpeditionAuthority sessionAuthority, double simulatedSecondsPerRealSecond)
        {
            if (sessionAuthority == null) throw new ArgumentNullException(nameof(sessionAuthority));
            if (double.IsNaN(simulatedSecondsPerRealSecond) || double.IsInfinity(simulatedSecondsPerRealSecond) || simulatedSecondsPerRealSecond < 0)
                throw new ArgumentOutOfRangeException(nameof(simulatedSecondsPerRealSecond));
            if (authority != null && authority != sessionAuthority) throw new InvalidOperationException("Runner already bound to another authority.");
            if (owners.TryGetValue(sessionAuthority, out ExpeditionRunner owner) && owner != null && owner != this)
                throw new InvalidOperationException("Authority already has a runner.");
            owners[sessionAuthority] = this;
            authority = sessionAuthority;
            timeScale = simulatedSecondsPerRealSecond;
        }

        public void Unbind()
        {
            if (authority != null && owners.TryGetValue(authority, out ExpeditionRunner owner) && owner == this) owners.Remove(authority);
            authority = null;
        }

        private void Update()
        {
            if (authority == null) return;
            double elapsed = Time.deltaTime * timeScale;
            if (elapsed > 0) authority.Tick(elapsed);
        }

        private void OnDestroy() => Unbind();
    }
}
