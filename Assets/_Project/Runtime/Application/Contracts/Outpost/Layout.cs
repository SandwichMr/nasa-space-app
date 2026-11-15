using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Trainer.Application.Outpost
{
    /// <summary>Fixed station on the floor plan (configuration). A crew member may start or keep working on
    /// it while the distance from their CONFIRMED position to <see cref="Position"/> is at most
    /// <see cref="InteractionRadiusMeters"/> (inclusive).</summary>
    public sealed class StationPlacement
    {
        public StationId Id { get; }
        public BasePosition Position { get; }
        public double InteractionRadiusMeters { get; }

        public StationPlacement(StationId id, BasePosition position, double interactionRadiusMeters)
        {
            if (!id.IsValid) throw new ArgumentException("Station id required.", nameof(id));
            Id = id;
            Position = position;
            InteractionRadiusMeters = OutpostGuard.Positive(interactionRadiusMeters, nameof(interactionRadiusMeters));
        }

        /// <summary>Proximity rule shared by the authority, validators and presentation hints.</summary>
        public bool IsWithinReach(BasePosition confirmedPosition) =>
            confirmedPosition.DistanceTo(Position) <= InteractionRadiusMeters;
    }

    /// <summary>Crew movement parameters (configuration). Speed in metres per second of simulated time;
    /// crew members are discs of <see cref="CrewRadiusMeters"/> for collision.</summary>
    public sealed class CrewMovementSettings
    {
        public double MaxSpeedMetersPerSecond { get; }
        public double CrewRadiusMeters { get; }

        public CrewMovementSettings(double maxSpeedMetersPerSecond, double crewRadiusMeters)
        {
            MaxSpeedMetersPerSecond = OutpostGuard.Positive(maxSpeedMetersPerSecond, nameof(maxSpeedMetersPerSecond));
            CrewRadiusMeters = OutpostGuard.Positive(crewRadiusMeters, nameof(crewRadiusMeters));
        }
    }

    /// <summary>
    /// Floor plan (configuration, immutable): the ONLY collision geometry of the game and the station
    /// positions used for proximity. Authored by D2 (scenario data), enforced by D3's movement on the
    /// authority, drawn by D4 to match. Unity colliders/physics never decide confirmed positions.
    /// Spawn point i belongs to crew slot i, so <see cref="MaxCrew"/> equals the number of spawn points.
    /// </summary>
    public sealed class OutpostLayout
    {
        public BaseRect Bounds { get; }
        public IReadOnlyList<BaseRect> Obstacles { get; }
        public IReadOnlyList<StationPlacement> Stations { get; }
        public IReadOnlyList<BasePosition> SpawnPoints { get; }
        public int MaxCrew => SpawnPoints.Count;

        public OutpostLayout(BaseRect bounds, IEnumerable<BaseRect> obstacles, IEnumerable<StationPlacement> stations,
            IEnumerable<BasePosition> spawnPoints)
        {
            if (obstacles == null) throw new ArgumentNullException(nameof(obstacles));
            if (stations == null) throw new ArgumentNullException(nameof(stations));
            if (spawnPoints == null) throw new ArgumentNullException(nameof(spawnPoints));
            Bounds = bounds;

            var obstacleCopy = new List<BaseRect>(obstacles);
            if (obstacleCopy.Count > OutpostLimits.MaxObstacles) throw new ArgumentException("Too many obstacles.", nameof(obstacles));
            foreach (BaseRect obstacle in obstacleCopy)
                if (!bounds.Contains(obstacle)) throw new ArgumentException("Obstacle outside bounds.", nameof(obstacles));

            var stationCopy = new List<StationPlacement>();
            var ids = new HashSet<StationId>();
            foreach (StationPlacement station in stations)
            {
                if (station == null) throw new ArgumentException("Null station.", nameof(stations));
                if (!ids.Add(station.Id)) throw new ArgumentException($"Duplicate station '{station.Id}'.", nameof(stations));
                if (!bounds.Contains(station.Position)) throw new ArgumentException($"Station '{station.Id}' outside bounds.", nameof(stations));
                stationCopy.Add(station);
            }
            if (stationCopy.Count == 0 || stationCopy.Count > OutpostLimits.MaxStations)
                throw new ArgumentException("Between 1 and MaxStations stations.", nameof(stations));

            var spawnCopy = new List<BasePosition>(spawnPoints);
            if (spawnCopy.Count == 0 || spawnCopy.Count > OutpostLimits.MaxCrew)
                throw new ArgumentException("Between 1 and MaxCrew spawn points.", nameof(spawnPoints));
            foreach (BasePosition spawn in spawnCopy)
            {
                if (!bounds.Contains(spawn)) throw new ArgumentException("Spawn point outside bounds.", nameof(spawnPoints));
                foreach (BaseRect obstacle in obstacleCopy)
                    if (obstacle.Contains(spawn)) throw new ArgumentException("Spawn point inside an obstacle.", nameof(spawnPoints));
            }

            Obstacles = new ReadOnlyCollection<BaseRect>(obstacleCopy.ToArray());
            Stations = new ReadOnlyCollection<StationPlacement>(stationCopy.ToArray());
            SpawnPoints = new ReadOnlyCollection<BasePosition>(spawnCopy.ToArray());
        }

        public bool TryGetStation(StationId id, out StationPlacement placement)
        {
            foreach (StationPlacement station in Stations)
                if (station.Id == id)
                {
                    placement = station;
                    return true;
                }
            placement = null;
            return false;
        }

        /// <summary>
        /// The collision rule: true when a crew disc of <paramref name="crewRadiusMeters"/> centred on
        /// <paramref name="center"/> lies inside <see cref="Bounds"/> and does not overlap any obstacle.
        /// Touching an edge is admissible. Every confirmed position must satisfy it; how a blocked move is
        /// resolved (stop, slide) is the movement implementation's choice.
        /// </summary>
        public bool IsAdmissible(BasePosition center, double crewRadiusMeters)
        {
            OutpostGuard.Positive(crewRadiusMeters, nameof(crewRadiusMeters));
            if (center.X - crewRadiusMeters < Bounds.MinX || center.X + crewRadiusMeters > Bounds.MaxX
                || center.Y - crewRadiusMeters < Bounds.MinY || center.Y + crewRadiusMeters > Bounds.MaxY)
                return false;
            foreach (BaseRect obstacle in Obstacles)
                if (obstacle.DistanceTo(center) < crewRadiusMeters) return false;
            return true;
        }
    }
}
