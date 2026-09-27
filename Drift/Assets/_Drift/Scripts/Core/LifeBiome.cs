namespace Drift.Core
{
    // What grows and lives on an island by nature. Deterministic per island seed (Island.BiomeForSeed); a merged
    // island keeps its own biome while the guest's plants and animals move in, which is how species are collected.
    public enum LifeBiome
    {
        Temperate = 0,
        Tropical = 1,
        Nordic = 2,
        Savanna = 3
    }
}
