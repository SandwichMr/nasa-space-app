using System;
using System.Collections.Generic;
using Trainer.Application.Outpost;
using UnityEngine;

namespace Trainer.Unity.Bootstrap
{
    /// <summary>
    /// The single clock of the phase 2 session. Exists and is bound ONLY on the authority; clients never tick.
    /// Unbound, it does nothing. Uses Time.deltaTime, so Unity's global time scale also affects the mission.
    /// Same rules as the phase 1 <see cref="SimulationRunner"/>, which stays unchanged.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OutpostRunner : MonoBehaviour
    {
        private static readonly Dictionary<OutpostAuthority, OutpostRunner> owners = new Dictionary<OutpostAuthority, OutpostRunner>();
        private OutpostAuthority authority;
        private double timeScale;

        public bool IsBound => authority != null;

        /// <param name="simulatedSecondsPerRealSecond">Finite and nonnegative time compression.</param>
        public void Bind(OutpostAuthority sessionAuthority, double simulatedSecondsPerRealSecond)
        {
            if (sessionAuthority == null) throw new ArgumentNullException(nameof(sessionAuthority));
            if (double.IsNaN(simulatedSecondsPerRealSecond) || double.IsInfinity(simulatedSecondsPerRealSecond)
                || simulatedSecondsPerRealSecond < 0)
                throw new ArgumentOutOfRangeException(nameof(simulatedSecondsPerRealSecond));
            if (authority != null && authority != sessionAuthority)
                throw new InvalidOperationException("Runner already bound to another authority.");
            if (owners.TryGetValue(sessionAuthority, out OutpostRunner owner) && owner != null && owner != this)
                throw new InvalidOperationException("Authority already has a runner.");
            owners[sessionAuthority] = this;
            authority = sessionAuthority;
            timeScale = simulatedSecondsPerRealSecond;
        }

        public void Unbind()
        {
            if (authority != null && owners.TryGetValue(authority, out OutpostRunner owner) && owner == this)
                owners.Remove(authority);
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
