using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trainer.Unity.World.Outpost
{
    /// <summary>Optional sprites for the lunar interior skin. Purely visual: any missing entry falls back to
    /// the previous procedural grid / flat colours / prefab sprite, so geometry and authority are never affected.</summary>
    [Serializable]
    public sealed class LunarInteriorSkin
    {
        [Serializable]
        public struct StationSprite
        {
            [Tooltip("Station id from the scenario, e.g. suit-rack-a.")] public string stationId;
            public Sprite sprite;
        }

        [Tooltip("Floor for the habitat and every room (tiled, 2 m repeat).")] public Sprite corridorFloor;
        [Tooltip("EVA preparation room: the exit towards the lunar surface keeps its own floor.")] public Sprite evaFloor;
        public Sprite airlockFloor;
        [Tooltip("9-slice wall panel.")] public Sprite wall;
        [Tooltip("9-slice bench / storage top.")] public Sprite furniture;
        [Tooltip("9-slice soft shadow drawn inside each room along its walls.")] public Sprite edgeShadow;
        [Tooltip("Function-specific artwork for each interactive station.")]
        public List<StationSprite> stations = new List<StationSprite>();

        /// <summary>Only the EVA preparation room differs from the corridor floor; null keeps the corridor floor.</summary>
        public Sprite FloorForRoom(string roomName) => roomName == "PREPARACIÓN EVA" ? evaFloor : null;

        /// <summary>Artwork for a station id, or null to keep the prefab's sprite.</summary>
        public Sprite StationArtwork(string stationId)
        {
            if (stations == null) return null;
            foreach (var entry in stations)
                if (entry.stationId == stationId && entry.sprite != null) return entry.sprite;
            return null;
        }
    }
}
