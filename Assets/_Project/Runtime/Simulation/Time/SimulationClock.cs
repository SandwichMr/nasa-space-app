using System;

namespace Trainer.Simulation
{
    /// <summary>
    /// Authority-owned simulated time in seconds. Advances only by explicit request,
    /// never from frame rate or wall-clock time. Plain C#, no UnityEngine dependency.
    /// </summary>
    public sealed class SimulationClock
    {
        /// <summary>Simulated seconds elapsed since the last Reset.</summary>
        public double ElapsedSeconds { get; private set; }

        /// <summary>Returns simulated time to zero.</summary>
        public void Reset()
        {
            ElapsedSeconds = 0d;
        }

        /// <summary>Throws without changing state unless seconds is finite and nonnegative
        /// and the resulting time stays finite. Zero is accepted and changes nothing.</summary>
        public static void Validate(double elapsedSeconds, double currentSeconds)
        {
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), elapsedSeconds,
                    "Elapsed seconds must be finite and nonnegative.");
            if (double.IsInfinity(currentSeconds + elapsedSeconds))
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), elapsedSeconds,
                    "Simulated time would overflow.");
        }

        /// <summary>Adds finite, nonnegative seconds. Invalid input throws and leaves time unchanged.</summary>
        public void Advance(double elapsedSeconds)
        {
            Validate(elapsedSeconds, ElapsedSeconds);
            ElapsedSeconds += elapsedSeconds;
        }
    }
}
