using Drift.Core;
using UnityEngine;

namespace Drift.Life
{
    // What one biome grows and breeds. The succession grid is the same everywhere: a biome only decides which
    // species fills each succession role (ground cover, flower, shrub, tree, shore palm, reed), how dense the
    // roles are and how the ground is tinted. Species of another biome never spawn from nothing; they arrive
    // with a merge and hold on from there (IslandLifeSystem.SpreadForeign, herd births breed true).
    public sealed class BiomeSpec
    {
        public const int RoleGround = 0, RoleFlower = 1, RoleShrub = 2, RoleTree = 3, RolePalm = 4, RoleReed = 5;

        public LifeBiome biome;
        public LifeKind[][] roles;
        public float[][] weights;
        public bool shorePalms = true, shoreReeds = true;
        public float groundDensity = 1f, shrubDensity = 1f, treeDensity = 1f;
        // 1 = ground cover gives way to the canopy (temperate rule); 0 = it stays under the trees (savanna).
        public float canopyClears = 1f;
        public int flowerEvery = 3;
        // Flowers give way to the closing canopy above this stage (temperate meadows); 2 = they stay.
        public float flowerMaxStage = 0.6f;
        // Ground tint per succession stage: multipliers on the terrain's grass colour in linear space (the island
        // mesh carries them as float vertex colours, so values far above 1 are fine). Biomes.Ground turns the
        // colour the ground should show into such a multiplier.
        public Color fresh, plains, shrub, forest;
        // Ground above snowLine (island-local height) fades to snow over 0.5 units, and snowPatches of the
        // lower ground lies under drifts placed by a coarse noise; 0 = none.
        public float snowLine, snowPatches;
        public Color snow = Biomes.Ground(0.95f, 0.97f, 1f);
        public float seasonAmplitude = 1f;
        public LifeKind[] animals;
        public LifeKind[] plants;
        public LifeKind[] collectibles;
        public ulong mask;

        public bool IsNative(LifeKind kind) => (mask & (1UL << (int)kind)) != 0;

        // r in 0..1 picks among the role's species by weight; a role may be empty (no shore palms up north).
        public bool TryPick(int role, float r, out LifeKind kind)
        {
            var kinds = roles[role];
            kind = default;
            if (kinds == null || kinds.Length == 0) return false;
            var w = weights[role];
            float total = 0f;
            for (int i = 0; i < w.Length; i++) total += w[i];
            float x = Mathf.Clamp01(r) * total;
            for (int i = 0; i < kinds.Length; i++)
            {
                x -= w[i];
                if (x <= 0f) { kind = kinds[i]; return true; }
            }
            kind = kinds[kinds.Length - 1];
            return true;
        }

        public Color GroundColour(float stage)
        {
            return stage < 0.25f ? Color.Lerp(fresh, plains, stage / 0.25f)
                : stage < 0.6f ? Color.Lerp(plains, shrub, (stage - 0.25f) / 0.35f)
                : Color.Lerp(shrub, forest, Mathf.Clamp01((stage - 0.6f) / 0.5f));
        }
    }

    public static class Biomes
    {
        public const int Count = 4;

        // _Grass of Materials/IslandTerrain.mat; the tint multiplies it in the terrain shader. Declared before
        // Specs: static initialisers run in textual order and the specs below call Ground.
        static readonly Color TerrainGrass = new Color(0.36f, 0.62f, 0.28f).linear;

        static readonly LifeKind[] None = new LifeKind[0];
        static readonly float[] NoWeights = new float[0];
        static readonly float[] One = { 1f };

        static readonly BiomeSpec[] Specs =
        {
            Make(new BiomeSpec
            {
                biome = LifeBiome.Temperate,
                roles = new[] { new[] { LifeKind.Grass }, new[] { LifeKind.Flower }, new[] { LifeKind.Bush }, new[] { LifeKind.Tree }, new[] { LifeKind.Palm }, new[] { LifeKind.Reed } },
                weights = new[] { One, One, One, One, One, One },
                fresh = new Color(1.15f, 1.2f, 0.75f), plains = new Color(1.3f, 1.2f, 0.62f),
                shrub = new Color(1.0f, 1.05f, 0.7f), forest = new Color(0.6f, 0.8f, 0.55f),
                animals = new[] { LifeKind.Hare, LifeKind.Sheep, LifeKind.Goat, LifeKind.Ox }
            }),
            Make(new BiomeSpec
            {
                biome = LifeBiome.Tropical,
                roles = new[]
                {
                    new[] { LifeKind.Fern }, new[] { LifeKind.Hibiscus }, new[] { LifeKind.Banana },
                    new[] { LifeKind.Palm, LifeKind.JungleTree, LifeKind.Bamboo }, new[] { LifeKind.Palm }, new[] { LifeKind.Reed }
                },
                weights = new[] { One, One, One, new[] { 0.42f, 0.3f, 0.28f }, One, One },
                groundDensity = 0.8f, shrubDensity = 1.2f, treeDensity = 1.1f, canopyClears = 0.6f, flowerEvery = 2, flowerMaxStage = 2f,
                fresh = Ground(0.45f, 0.72f, 0.25f), plains = Ground(0.3f, 0.68f, 0.2f),
                shrub = Ground(0.2f, 0.58f, 0.18f), forest = Ground(0.13f, 0.47f, 0.16f),
                seasonAmplitude = 0.25f,
                animals = new[] { LifeKind.Capybara, LifeKind.Flamingo, LifeKind.Tortoise }
            }),
            Make(new BiomeSpec
            {
                biome = LifeBiome.Nordic,
                roles = new[]
                {
                    new[] { LifeKind.Lichen, LifeKind.Mushroom }, new[] { LifeKind.Heather }, new[] { LifeKind.Juniper },
                    new[] { LifeKind.Spruce, LifeKind.Birch }, None, new[] { LifeKind.Reed }
                },
                weights = new[] { new[] { 0.8f, 0.2f }, One, One, new[] { 0.62f, 0.38f }, NoWeights, One },
                shorePalms = false,
                groundDensity = 0.7f, shrubDensity = 0.7f, treeDensity = 0.8f, canopyClears = 0.5f, flowerEvery = 2, flowerMaxStage = 2f,
                fresh = Ground(0.72f, 0.78f, 0.74f), plains = Ground(0.62f, 0.72f, 0.64f),
                shrub = Ground(0.52f, 0.64f, 0.58f), forest = Ground(0.42f, 0.56f, 0.52f),
                snowLine = 1.2f, snowPatches = 0.3f, seasonAmplitude = 0.4f,
                animals = new[] { LifeKind.Penguin, LifeKind.Reindeer, LifeKind.ArcticFox }
            }),
            Make(new BiomeSpec
            {
                biome = LifeBiome.Savanna,
                roles = new[]
                {
                    new[] { LifeKind.DryGrass }, new[] { LifeKind.Aloe }, new[] { LifeKind.ThornBush, LifeKind.TermiteMound },
                    new[] { LifeKind.Acacia, LifeKind.Baobab }, None, new[] { LifeKind.Reed }
                },
                weights = new[] { One, One, new[] { 0.7f, 0.3f }, new[] { 0.82f, 0.18f }, NoWeights, One },
                shorePalms = false,
                groundDensity = 1.3f, shrubDensity = 0.4f, treeDensity = 0.16f, canopyClears = 0f, flowerEvery = 4, flowerMaxStage = 2f,
                fresh = Ground(0.85f, 0.7f, 0.38f), plains = Ground(0.86f, 0.68f, 0.3f),
                shrub = Ground(0.8f, 0.6f, 0.28f), forest = Ground(0.72f, 0.52f, 0.25f),
                seasonAmplitude = 0.4f,
                animals = new[] { LifeKind.Meerkat, LifeKind.Zebra, LifeKind.Giraffe }
            })
        };

        // The tint that makes the terrain show the given (sRGB) colour.
        public static Color Ground(float r, float g, float b)
        {
            Color target = new Color(r, g, b).linear;
            return new Color(target.r / TerrainGrass.r, target.g / TerrainGrass.g, target.b / TerrainGrass.b, 1f);
        }

        static BiomeSpec Make(BiomeSpec s)
        {
            int n = 0;
            ulong seen = 0;
            foreach (var role in s.roles)
                foreach (var k in role)
                    if ((seen & (1UL << (int)k)) == 0) { seen |= 1UL << (int)k; n++; }
            s.plants = new LifeKind[n];
            n = 0;
            seen = 0;
            foreach (var role in s.roles)
                foreach (var k in role)
                    if ((seen & (1UL << (int)k)) == 0) { seen |= 1UL << (int)k; s.plants[n++] = k; }
            s.collectibles = new LifeKind[s.animals.Length + s.plants.Length];
            s.animals.CopyTo(s.collectibles, 0);
            s.plants.CopyTo(s.collectibles, s.animals.Length);
            foreach (var k in s.animals) seen |= 1UL << (int)k;
            s.mask = seen;
            return s;
        }

        public static BiomeSpec Of(int biome) => Specs[biome >= 0 && biome < Specs.Length ? biome : 0];
        public static BiomeSpec Of(LifeBiome biome) => Of((int)biome);

        // Every kind the journal can collect on the islands of a biome: its herd animals first, then its plants.
        // The arrays are shared; do not modify them.
        public static LifeKind[] Collectibles(LifeBiome biome) => Of(biome).collectibles;
        public static LifeKind[] Animals(LifeBiome biome) => Of(biome).animals;
        public static LifeKind[] Plants(LifeBiome biome) => Of(biome).plants;

        // The first biome that brings the kind forth; false for kinds that belong to no biome (birds, critters).
        public static bool TryHomeOf(LifeKind kind, out LifeBiome biome)
        {
            for (int i = 0; i < Specs.Length; i++)
                if (Specs[i].IsNative(kind)) { biome = Specs[i].biome; return true; }
            biome = LifeBiome.Temperate;
            return false;
        }
    }
}
