using System;
using Drift.Core;

namespace Drift.Life
{
    public enum Rarity { Common, Occasional, Rare }

    // "Artenauswahl der Runde": the herd species and critters a cozy run brings forth, so the album fills over many
    // runs instead of one. Chosen once per run (GameSession, from the world seed and what the album already holds) and
    // saved with the run; Mask 0 means no pool (Adventure, Edit Mode, tests): every species as before.
    // Per run: one of the two common temperate species (the start island is temperate and always shows it from the
    // first moment), one species for each of the other three biomes (so no biome's islands go empty), often a second
    // temperate one (the temperate biome has four species and the first slot is fixed to the common two), now and
    // then one extra herd species from anywhere, and one or two critter kinds. Every draw is weighted by rarity; species the album lacks
    // weigh more, and the more of the album is filled, the more they do (the last rare ones do not take 20 runs).
    public static class SpeciesPool
    {
        public static readonly LifeKind[] HerdKinds =
        {
            LifeKind.Hare, LifeKind.Sheep, LifeKind.Goat, LifeKind.Ox, LifeKind.Capybara, LifeKind.Flamingo, LifeKind.Tortoise,
            LifeKind.Penguin, LifeKind.Reindeer, LifeKind.ArcticFox, LifeKind.Meerkat, LifeKind.Zebra, LifeKind.Giraffe
        };
        public static readonly LifeKind[] CritterKinds = { LifeKind.Crab, LifeKind.Butterfly, LifeKind.Turtle, LifeKind.Firefly };
        // The most common temperate species: the start island's first herd is one of them.
        public static readonly LifeKind[] StartKinds = { LifeKind.Hare, LifeKind.Sheep };

        // Draw weights of the tiers (the pool) and herd spawn weights inside the pool (IslandHerdSystem.PickSpecies).
        public static float CommonWeight = 1f, OccasionalWeight = 0.3f, RareWeight = 0.06f;
        public static float CritterOccasionalWeight = 0.3f, CritterRareWeight = 0.08f;
        public static float CommonSpawn = 3f, OccasionalSpawn = 1.5f, RareSpawn = 0.8f;
        // Weight factor of a species the album lacks: UnfoundBase + UnfoundGrowth * (album share found)^3.
        public static float UnfoundBase = 1f, UnfoundGrowth = 20f;
        // Chances of a second temperate species, of one / two extra herd species from anywhere and of a second critter kind.
        public static float TemperateSecondChance = 0.55f, ExtraOneChance = 0.22f, ExtraTwoChance = 0f, SecondCritterChance = 0.35f;

        public static ulong Mask { get; private set; }
        public static LifeKind StartSpecies { get; private set; } = LifeKind.Hare;
        public static bool Active => Mask != 0;
        public static int Version { get; private set; }

        public static ulong Bit(LifeKind k) => 1UL << (int)k;
        public static bool Allows(LifeKind k) => Mask == 0 || (Mask & Bit(k)) != 0;

        public static ulong AllMask
        {
            get
            {
                ulong m = 0;
                foreach (var k in HerdKinds) m |= Bit(k);
                foreach (var k in CritterKinds) m |= Bit(k);
                return m;
            }
        }

        public static void Set(ulong mask, LifeKind start)
        {
            if (mask != 0) mask |= Bit(start);
            Mask = mask;
            StartSpecies = start;
            Version++;
        }

        public static void Clear() => Set(0, LifeKind.Hare);

        // Restores a saved pool; the start species is its first StartKind (a save only keeps the mask).
        public static void Restore(ulong mask)
        {
            var start = LifeKind.Hare;
            foreach (var k in StartKinds) if ((mask & Bit(k)) != 0) { start = k; break; }
            Set(mask & AllMask, start);
        }

        public static Rarity RarityOf(LifeKind k)
        {
            switch (k)
            {
                case LifeKind.Hare: case LifeKind.Sheep: case LifeKind.Capybara: case LifeKind.Penguin: case LifeKind.Meerkat:
                case LifeKind.Crab:
                    return Rarity.Common;
                case LifeKind.Ox: case LifeKind.Tortoise: case LifeKind.ArcticFox: case LifeKind.Giraffe:
                case LifeKind.Firefly:
                    return Rarity.Rare;
                default:
                    return Rarity.Occasional;
            }
        }

        public static bool IsPooled(LifeKind k) => Array.IndexOf(HerdKinds, k) >= 0 || Array.IndexOf(CritterKinds, k) >= 0;

        public static string LabelOf(Rarity r) => r == Rarity.Common ? "häufig" : r == Rarity.Occasional ? "gelegentlich" : "selten";

        public static float SpawnWeight(LifeKind k)
        {
            var r = RarityOf(k);
            return r == Rarity.Common ? CommonSpawn : r == Rarity.Occasional ? OccasionalSpawn : RareSpawn;
        }

        static float DrawWeight(LifeKind k, bool critter)
        {
            var r = RarityOf(k);
            if (r == Rarity.Common) return CommonWeight;
            if (r == Rarity.Occasional) return critter ? CritterOccasionalWeight : OccasionalWeight;
            return critter ? CritterRareWeight : RareWeight;
        }

        // found: bit (int)LifeKind of what the album already holds (seen). Deterministic for (seed, found).
        public static ulong Choose(int seed, ulong found, out LifeKind start)
        {
            var rnd = new Random(unchecked(seed * 16777619 ^ 0x5EC1E5));
            ulong all = AllMask;
            float share = (float)DiscoveryShare(found & all, all);
            float unfound = UnfoundBase + UnfoundGrowth * share * share * share;
            ulong mask = 0;

            start = Pick(rnd, StartKinds, found, mask, unfound, false);
            mask |= Bit(start);
            for (int b = 1; b < 4; b++)
            {
                var k = Pick(rnd, Biomes.Animals((LifeBiome)b), found, mask, unfound, false);
                mask |= Bit(k);
            }
            if (rnd.NextDouble() < TemperateSecondChance) mask |= Bit(Pick(rnd, Biomes.Animals(LifeBiome.Temperate), found, mask, unfound, false));
            double x = rnd.NextDouble();
            int extra = x < ExtraTwoChance ? 2 : x < ExtraTwoChance + ExtraOneChance ? 1 : 0;
            for (int i = 0; i < extra; i++) mask |= Bit(Pick(rnd, HerdKinds, found, mask, unfound, false));

            ulong critters = Bit(Pick(rnd, CritterKinds, found, 0, unfound, true));
            if (rnd.NextDouble() < SecondCritterChance) critters |= Bit(Pick(rnd, CritterKinds, found, critters, unfound, true));
            return mask | critters;
        }

        static double DiscoveryShare(ulong found, ulong all)
        {
            int n = 0, f = 0;
            for (ulong m = all; m != 0; m &= m - 1) n++;
            for (ulong m = found; m != 0; m &= m - 1) f++;
            return n > 0 ? (double)f / n : 0.0;
        }

        // A weighted draw among the kinds not yet in `taken`; the first kind when every one is taken.
        static LifeKind Pick(Random rnd, LifeKind[] kinds, ulong found, ulong taken, float unfound, bool critter)
        {
            float total = 0f;
            foreach (var k in kinds) if ((taken & Bit(k)) == 0) total += Weight(k, found, unfound, critter);
            double r = rnd.NextDouble() * total;
            LifeKind last = kinds[0];
            foreach (var k in kinds)
            {
                if ((taken & Bit(k)) != 0) continue;
                last = k;
                r -= Weight(k, found, unfound, critter);
                if (r <= 0.0) return k;
            }
            return last;
        }

        static float Weight(LifeKind k, ulong found, float unfound, bool critter) =>
            DrawWeight(k, critter) * ((found & Bit(k)) != 0 ? 1f : unfound);

        public static string Describe(ulong mask)
        {
            if (mask == 0) return "alle Arten";
            var sb = new System.Text.StringBuilder();
            foreach (var k in HerdKinds)
                if ((mask & Bit(k)) != 0) sb.Append(sb.Length > 0 ? ", " : "").Append(LifeNames.Of(k)).Append(" (").Append(LabelOf(RarityOf(k))).Append(')');
            sb.Append(" | ");
            bool first = true;
            foreach (var k in CritterKinds)
                if ((mask & Bit(k)) != 0) { sb.Append(first ? "" : ", ").Append(LifeNames.Of(k)).Append(" (").Append(LabelOf(RarityOf(k))).Append(')'); first = false; }
            return sb.ToString();
        }

#if UNITY_EDITOR
        // Statics survive leaving Play Mode when domain reload is off; Edit Mode (tests, previews) never has a pool.
        [UnityEditor.InitializeOnLoadMethod]
        static void HookPlayMode()
        {
            UnityEditor.EditorApplication.playModeStateChanged += s =>
            {
                if (s == UnityEditor.PlayModeStateChange.EnteredEditMode) Clear();
            };
        }
#endif
    }
}
