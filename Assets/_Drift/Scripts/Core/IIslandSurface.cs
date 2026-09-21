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
        // How far the island has sunk (grows while it sinks, shrinks while a volcano rises): the life systems watch
        // it to tell rising water (flee uphill) from a walk to the shore.
        float SinkDepth { get; }
        float SampleHeight(Vector2 localXZ);
        // Colours the ground from a cell grid (row-major, cellsX * cellsZ, cell centres at gridOrigin + (i + 0.5,
        // j + 0.5) * gridCell), bilinearly filtered and clamped at the edges. A grid instead of a per-vertex
        // callback: the delegate call and colour lerps per terrain vertex cost 3 ms on a big island.
        void ApplyGroundTint(Color[] cellColors, int cellsX, int cellsZ, Vector2 gridOrigin, float gridCell);
    }
}
