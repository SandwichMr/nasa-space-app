using System;
using System.Collections.Generic;
using Trainer.Application.Incidents;
using UnityEngine;

namespace Trainer.Unity.Bootstrap.Incidents
{
    /// <summary>The single clock of the phase 5 session (D2). Exists and is bound ONLY on the authority; clients never tick. It drives
    /// <see cref="LivingBaseAuthority.Tick"/>, which advances the phase 4 session (equipment, base, EVA, roster), the director,
    /// episodes and boards together.</summary>
    [DisallowMultipleComponent]
    public sealed class LivingBaseRunner : MonoBehaviour
    {
        private static readonly Dictionary<LivingBaseAuthority, LivingBaseRunner> owners = new Dictionary<LivingBaseAuthority, LivingBaseRunner>();
        private LivingBaseAuthority authority;
        private double timeScale;

        public bool IsBound => authority != null;
        public double TimeScale => timeScale;

        public void Bind(LivingBaseAuthority sessionAuthority, double simulatedSecondsPerRealSecond)
        {
            if (sessionAuthority == null) throw new ArgumentNullException(nameof(sessionAuthority));
            if (double.IsNaN(simulatedSecondsPerRealSecond) || double.IsInfinity(simulatedSecondsPerRealSecond) || simulatedSecondsPerRealSecond < 0)
                throw new ArgumentOutOfRangeException(nameof(simulatedSecondsPerRealSecond));
            if (authority != null && authority != sessionAuthority) throw new InvalidOperationException("Runner already bound to another authority.");
            if (owners.TryGetValue(sessionAuthority, out LivingBaseRunner owner) && owner != null && owner != this)
                throw new InvalidOperationException("Authority already has a runner.");
            owners[sessionAuthority] = this;
            authority = sessionAuthority;
            timeScale = simulatedSecondsPerRealSecond;
        }

        public void Unbind()
        {
            if (authority != null && owners.TryGetValue(authority, out LivingBaseRunner owner) && owner == this) owners.Remove(authority);
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
