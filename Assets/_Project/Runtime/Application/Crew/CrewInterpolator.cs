using System;
using System.Collections.Generic;
using Trainer.Application.Outpost;

namespace Trainer.Application.Crew
{
    /// <summary>
    /// Smooth presentation of CONFIRMED poses (plain C#, presentation only; never fed back to the authority).
    ///
    /// Strategy: snapshot interpolation with a fixed playback delay. Every snapshot stores each member's
    /// confirmed position with the local time it arrived. A member is drawn at <c>now - DelaySeconds</c>,
    /// interpolated linearly between the two stored samples around that time; past the newest sample it holds
    /// the newest position (no extrapolation, so it never shows a place the authority did not confirm).
    /// The same rule applies to the local astronaut (no prediction in this prototype: it lags by the round
    /// trip plus the delay). A jump longer than <see cref="TeleportMeters"/> or a new epoch (Restart, spawn)
    /// snaps instead of sliding through walls. Members missing from a snapshot are dropped at once.
    /// </summary>
    public sealed class CrewInterpolator
    {
        private struct Sample
        {
            public double Time;
            public BasePosition Position;
        }

        private readonly Dictionary<PlayerId, List<Sample>> tracks = new Dictionary<PlayerId, List<Sample>>();
        private readonly int capacity;
        private long epoch = -1;

        public double DelaySeconds { get; }
        public double TeleportMeters { get; }

        public CrewInterpolator(double delaySeconds = 0.1, double teleportMeters = 1.5, int capacity = 8)
        {
            if (double.IsNaN(delaySeconds) || double.IsInfinity(delaySeconds) || delaySeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(delaySeconds));
            if (double.IsNaN(teleportMeters) || double.IsInfinity(teleportMeters) || teleportMeters <= 0)
                throw new ArgumentOutOfRangeException(nameof(teleportMeters));
            if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
            DelaySeconds = delaySeconds;
            TeleportMeters = teleportMeters;
            this.capacity = capacity;
        }

        public IEnumerable<PlayerId> Members => tracks.Keys;

        /// <summary>Records an accepted snapshot received at local time <paramref name="receivedAtSeconds"/>
        /// (monotonic, for example Unity's unscaled time).</summary>
        public void Push(OutpostSessionSnapshot snapshot, double receivedAtSeconds)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.Epoch != epoch)
            {
                tracks.Clear(); // Restart or another session: positions jump to spawn, never slide.
                epoch = snapshot.Epoch;
            }
            var present = new HashSet<PlayerId>();
            foreach (CrewMemberState member in snapshot.Crew)
            {
                present.Add(member.Player);
                if (!tracks.TryGetValue(member.Player, out List<Sample> track))
                {
                    track = new List<Sample>(capacity);
                    tracks.Add(member.Player, track);
                }
                BasePosition position = member.Pose.Position;
                if (track.Count > 0)
                {
                    Sample last = track[track.Count - 1];
                    if (last.Position.DistanceTo(position) > TeleportMeters) track.Clear();
                    else if (last.Position.Equals(position) && track.Count > 1
                        && track[track.Count - 2].Position.Equals(position))
                    {
                        // Standing still: refresh the time instead of filling the buffer with copies.
                        track[track.Count - 1] = new Sample { Time = receivedAtSeconds, Position = position };
                        continue;
                    }
                }
                if (track.Count == capacity) track.RemoveAt(0);
                track.Add(new Sample { Time = receivedAtSeconds, Position = position });
            }
            var gone = new List<PlayerId>();
            foreach (PlayerId id in tracks.Keys) if (!present.Contains(id)) gone.Add(id);
            foreach (PlayerId id in gone) tracks.Remove(id);
        }

        /// <summary>Removes every track (link lost, membership ended, presentation reset).</summary>
        public void Clear()
        {
            tracks.Clear();
            epoch = -1;
        }

        /// <summary>Position to draw for <paramref name="player"/> at local time <paramref name="nowSeconds"/>.</summary>
        public bool TrySample(PlayerId player, double nowSeconds, out BasePosition position)
        {
            position = default;
            if (!tracks.TryGetValue(player, out List<Sample> track) || track.Count == 0) return false;
            double t = nowSeconds - DelaySeconds;
            if (t <= track[0].Time)
            {
                position = track[0].Position;
                return true;
            }
            for (int i = 1; i < track.Count; i++)
            {
                Sample a = track[i - 1], b = track[i];
                if (t > b.Time) continue;
                double span = b.Time - a.Time;
                double k = span > 0 ? (t - a.Time) / span : 1d;
                position = new BasePosition(a.Position.X + (b.Position.X - a.Position.X) * k,
                    a.Position.Y + (b.Position.Y - a.Position.Y) * k);
                return true;
            }
            position = track[track.Count - 1].Position; // Newest confirmed position: hold, never extrapolate.
            return true;
        }
    }
}
