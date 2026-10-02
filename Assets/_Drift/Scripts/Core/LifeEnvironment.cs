using System;
using UnityEngine;

namespace Drift.Core
{
    // World-wide conditions life systems react to, supplied by whoever owns them (DayNightCycle installs the
    // night provider, WaterFeedback the wind and storm providers) so Drift.Life never references
    // Drift.Visuals. Without a provider it is always day, with a light steady breeze and no storm.
    public static class LifeEnvironment
    {
        public static readonly Vector2 DefaultWind = new Vector2(0.6f, 0.2f);

        // Every lightning strike, wherever it comes from (a storm over the sea, IslandLifeSystem hitting an island):
        // world position of the impact and 0..1 strength. StormVisuals draws the bolt and the flash, AudioDirector the
        // thunder, without either knowing who struck.
        public static event Action<Vector3, float> LightningStruck;
        public static void ReportLightning(Vector3 world, float strength) => LightningStruck?.Invoke(world, strength);

        public static Func<float> NightProvider;
        // Planar wind in world units/s (direction * speed).
        public static Func<Vector2> WindProvider;
        // 0..1 storm strength at the player, folded into the sway magnitude by the life systems.
        public static Func<float> StormProvider;

        public static float NightAmount
        {
            get
            {
                var p = NightProvider;
                return p != null ? p() : 0f;
            }
        }

        public static Vector2 Wind
        {
            get
            {
                var p = WindProvider;
                return p != null ? p() : DefaultWind;
            }
        }

        public static float Storm
        {
            get
            {
                var p = StormProvider;
                return p != null ? Mathf.Clamp01(p()) : 0f;
            }
        }

        // 0..1 rain at a world position (the storm visuals know where the rain columns are); without a provider
        // the player's storm strength stands in for every island.
        public static Func<Vector3, float> RainProvider;

        public static float RainAt(Vector3 worldPosition)
        {
            var p = RainProvider;
            return p != null ? Mathf.Clamp01(p(worldPosition)) : Storm;
        }

        // 0..1 how far the year has turned (IslandLifeSystem.Season of the player's island); -1 without a provider.
        public static Func<float> SeasonProvider;

        public static float Season
        {
            get
            {
                var p = SeasonProvider;
                return p != null ? Mathf.Repeat(p(), 1f) : -1f;
            }
        }

        // 0..1 time of day, 0 = midnight, 0.5 = noon (DayNightCycle.TimeOfDay); -1 without a provider, which the
        // herds read as "no noon": the midday behaviours (sheep in the shade, oxen wading) then only come with
        // rain or by chance.
        public static Func<float> TimeOfDayProvider;

        public static float TimeOfDay
        {
            get
            {
                var p = TimeOfDayProvider;
                return p != null ? Mathf.Repeat(p(), 1f) : -1f;
            }
        }

        // Camera-to-point distance for per-animal detail (IslandHerdSystem's detail LOD). Without a provider it
        // is the planar LifeLod.Distance; a camera that knows its height can install the true 3D distance so a
        // high camera does not pay for detail nobody can see.
        public static Func<Vector3, float> ViewDistanceProvider;

        public static float ViewDistance(Vector3 worldPosition)
        {
            var p = ViewDistanceProvider;
            return p != null ? p(worldPosition) : LifeLod.Distance(worldPosition);
        }

        // Something worth watching from `worldFrom` within maxDistance (the nearest other island, a ship, an
        // erupting volcano): herds line up and look at it. No provider = nothing to see.
        public delegate bool PointOfInterestProvider(Vector3 worldFrom, float maxDistance, out Vector3 worldPoint);
        public static PointOfInterestProvider PointOfInterest;

        public static bool TryPointOfInterest(Vector3 worldFrom, float maxDistance, out Vector3 worldPoint)
        {
            var p = PointOfInterest;
            if (p != null) return p(worldFrom, maxDistance, out worldPoint);
            worldPoint = default;
            return false;
        }
    }
}
