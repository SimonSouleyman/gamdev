namespace Drift.Islands
{
    public enum IslandKind { Regular, Volcanic, Ancient, Barren }

    // Classic = the original IslandShape.CreateBlob (player, tests, old saves); the others are the streamed
    // world's shape families (IslandArchetypes.Create). Values are saved as ints: append only.
    public enum IslandArchetype { Classic, Blob, Ridge, Crescent, TwinPeak, Sandbank, Mesa, Archipelago, Stack }
}
