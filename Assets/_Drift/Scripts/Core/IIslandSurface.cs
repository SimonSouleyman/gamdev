using System;
using UnityEngine;

namespace Drift.Core
{
    public interface IIslandSurface
    {
        Transform SurfaceTransform { get; }
        Rect LocalBounds { get; }
        float BoundingRadius { get; }
        float LandArea { get; }
        int Version { get; }
        // 0 regular, 1 volcanic, 2 ancient, 3 barren (mirrors Drift.Islands.IslandKind without the dependency).
        int Character { get; }
        // (int)Drift.Core.LifeBiome: decides which plant and animal species an island brings forth.
        int Biome { get; }
        // 0..1 local storm strength (lightning source for fires), mirrors Island.StormIntensity.
        float StormIntensity { get; }
        // Self-propelled planar speed in units/s (drive and impact momentum, not the plate carry, which would
        // keep a carried island "moving fast" forever); Phase 4 crabs dive above IslandCrittersSystem.diveSpeed.
        float Speed { get; }
        float SampleHeight(Vector2 localXZ);
        void ApplyGroundTint(Func<Vector2, Color> tintAtLocal);
    }
}
