using System;
using Trainer.Application;
using UnityEngine;

namespace Trainer.Unity.Bootstrap
{
    /// <summary>
    /// The single clock of the shared session. Must exist and be bound ONLY on the authority
    /// (host/server); clients never tick a session. Unbound, it does nothing.
    /// Uses Time.deltaTime, so Unity's global time scale also affects the mission clock.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SimulationRunner : MonoBehaviour
    {
        private MissionSession session;
        private double timeScale;

        public bool IsBound => session != null;

        /// <param name="simulatedSecondsPerRealSecond">Finite and nonnegative time compression.</param>
        public void Bind(MissionSession missionSession, double simulatedSecondsPerRealSecond)
        {
            if (missionSession == null) throw new ArgumentNullException(nameof(missionSession));
            if (double.IsNaN(simulatedSecondsPerRealSecond) || double.IsInfinity(simulatedSecondsPerRealSecond)
                || simulatedSecondsPerRealSecond < 0)
                throw new ArgumentOutOfRangeException(nameof(simulatedSecondsPerRealSecond));
            if (session != null && session != missionSession)
                throw new InvalidOperationException("Runner already bound to another session.");
            session = missionSession;
            timeScale = simulatedSecondsPerRealSecond;
        }

        public void Unbind() => session = null;

        private void Update()
        {
            if (session == null) return;
            double elapsed = Time.deltaTime * timeScale;
            if (elapsed > 0) session.Tick(elapsed);
        }

        private void OnDestroy() => Unbind();
    }
}
