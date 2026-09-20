using Drift.Islands;
using Drift.Life;
using Drift.Tectonics;
using UnityEngine;

namespace Drift.SaveSystem
{
    // One master seed drives every per-world random stream. Each consumer gets its own hash so
    // changing the master changes all of them, and no two consumers ever share a System.Random seed.
    public static class WorldSeeds
    {
        public const uint SaltStreamer = 0x1F3D5B79u;
        public const uint SaltPlates = 0x2A6C4E80u;
        public const uint SaltFlocks = 0x3B7D5F91u;
        public const uint SaltVolcanoes = 0x4C8E60A2u;
        public const uint SaltPlayerShape = 0x5D9F71B3u;
        public const uint SaltPlayerLife = 0x6EA082C4u;
        public const uint SaltPlayerHerds = 0x7FB193D5u;

        public const int MaxDigits = 9;
        public const int MaxSeed = 999999999;

        public static int Derive(int master, uint salt)
        {
            unchecked
            {
                uint h = (uint)master + salt * 0x9E3779B9u;
                h ^= h >> 16; h *= 0x7FEB352Du;
                h ^= h >> 15; h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (int)(h & 0x7FFFFFFFu);
            }
        }

        public static int Random() => UnityEngine.Random.Range(1, 1000000);

        public static int Clamp(long seed)
        {
            if (seed < 0) seed = -seed;
            return (int)System.Math.Min(seed, MaxSeed);
        }

        public static bool TryParse(string text, out int seed)
        {
            seed = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (!long.TryParse(text.Trim(), out long v)) return false;
            seed = Clamp(v);
            return true;
        }

        // Only assigns seeds; the callers run the regeneration paths (WorldStreamer.ResetWorld,
        // PlateSystem.Restore, Island.ResetToStart) themselves so the order stays theirs.
        public static void Apply(int master, Island player, WorldStreamer streamer)
        {
            if (streamer == null) streamer = Object.FindAnyObjectByType<WorldStreamer>();
            if (streamer != null) streamer.seed = Derive(master, SaltStreamer);

            var plates = PlateSystem.Instance;
            if (plates == null) plates = Object.FindAnyObjectByType<PlateSystem>();
            if (plates != null) plates.seed = Derive(master, SaltPlates);

            if (player != null)
            {
                player.shapeSeed = Derive(master, SaltPlayerShape);
                var life = player.GetComponent<IslandLifeSystem>();
                if (life != null) life.seed = Derive(master, SaltPlayerLife);
                var herds = player.GetComponent<IslandHerdSystem>();
                if (herds != null) herds.seed = Derive(master, SaltPlayerHerds);
            }

            var flocks = Object.FindAnyObjectByType<FlockSystem>();
            if (flocks != null) flocks.Reseed(Derive(master, SaltFlocks));

            var volcanoes = Object.FindAnyObjectByType<VolcanoSpawner>();
            if (volcanoes != null) volcanoes.Reseed(Derive(master, SaltVolcanoes));
        }
    }
}
