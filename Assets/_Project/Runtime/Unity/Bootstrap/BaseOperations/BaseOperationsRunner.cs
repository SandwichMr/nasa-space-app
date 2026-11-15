using System;
using System.Collections.Generic;
using Trainer.Application.BaseOperations;
using UnityEngine;

namespace Trainer.Unity.Bootstrap.BaseOperations
{
    /// <summary>The single clock of the phase 4 session (D2). Exists and is bound ONLY on the authority; clients never tick.
    /// It drives <see cref="BaseOperationsAuthority.Tick"/>, which advances the equipment, base, EVA and roster together.</summary>
    [DisallowMultipleComponent]
    public sealed class BaseOperationsRunner : MonoBehaviour
    {
        private static readonly Dictionary<BaseOperationsAuthority, BaseOperationsRunner> owners =
            new Dictionary<BaseOperationsAuthority, BaseOperationsRunner>();
        private BaseOperationsAuthority authority;
        private double timeScale;

        public bool IsBound => authority != null;

        public void Bind(BaseOperationsAuthority sessionAuthority, double simulatedSecondsPerRealSecond)
        {
            if (sessionAuthority == null) throw new ArgumentNullException(nameof(sessionAuthority));
            if (double.IsNaN(simulatedSecondsPerRealSecond) || double.IsInfinity(simulatedSecondsPerRealSecond) || simulatedSecondsPerRealSecond < 0)
                throw new ArgumentOutOfRangeException(nameof(simulatedSecondsPerRealSecond));
            if (authority != null && authority != sessionAuthority) throw new InvalidOperationException("Runner already bound to another authority.");
            if (owners.TryGetValue(sessionAuthority, out BaseOperationsRunner owner) && owner != null && owner != this)
                throw new InvalidOperationException("Authority already has a runner.");
            owners[sessionAuthority] = this;
            authority = sessionAuthority;
            timeScale = simulatedSecondsPerRealSecond;
        }

        public void Unbind()
        {
            if (authority != null && owners.TryGetValue(authority, out BaseOperationsRunner owner) && owner == this) owners.Remove(authority);
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
