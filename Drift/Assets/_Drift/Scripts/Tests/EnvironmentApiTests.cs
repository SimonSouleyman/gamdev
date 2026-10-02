using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // v0.6.8 environment layer of IslandLifeSystem: food per diet, grazing and regrowth, food and shelter search,
    // shade, climate across a merge seam and the slow blending of the seam.
    public class EnvironmentApiTests
    {
        readonly List<GameObject> _objects = new();
        System.Func<float> _seasonProvider;

        [SetUp]
        public void SetUp()
        {
            LifeLod.DistanceProvider = _ => 0f;
            _seasonProvider = LifeEnvironment.SeasonProvider;
            LifeEnvironment.SeasonProvider = null;
            IslandLifeSystem.SeasonSource = null;
            IslandLifeSystem.ResetSeasonReference();
        }

        [TearDown]
        public void TearDown()
        {
            LifeLod.DistanceProvider = null;
            LifeEnvironment.SeasonProvider = _seasonProvider;
            IslandLifeSystem.SeasonSource = null;
            IslandLifeSystem.ResetSeasonReference();
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        IslandLifeSystem Make(float radius, int seed = 7, LifeBiome biome = LifeBiome.Temperate, string name = "EnvIsle")
        {
            var go = new GameObject(name);
            go.SetActive(false);
            var surface = go.AddComponent<FakeIslandSurface>();
            surface.radius = radius;
            surface.biome = (int)biome;
            var life = go.AddComponent<IslandLifeSystem>();
            life.seed = seed;
            go.SetActive(true);
            _objects.Add(go);
            return life;
        }

        // Every land cell of the grid to one stage (a 0.5 u raster finds each 1.2 u cell).
        static void Fill(IslandLifeSystem life, float radius, float stage)
        {
            for (float x = -radius; x <= radius; x += 0.5f)
                for (float z = -radius; z <= radius; z += 0.5f)
                    life.SetStageAt(new Vector2(x, z), stage);
        }

        static void Patch(IslandLifeSystem life, Vector2 centre, float stage, float half = 1.3f)
        {
            for (float x = -half; x <= half; x += 0.5f)
                for (float z = -half; z <= half; z += 0.5f)
                    life.SetStageAt(centre + new Vector2(x, z), stage);
        }

        static Vector2 ShorePoint(IslandLifeSystem life, float radius)
        {
            for (float x = radius; x > 0f; x -= 0.2f)
                if (life.CoastDistanceAt(new Vector2(x, 0.3f)) == 0) return new Vector2(x, 0.3f);
            Assert.Fail("no shore cell found");
            return default;
        }

        [Test]
        public void FoodAt_EachDietReadsItsOwnPartOfTheGrid()
        {
            var life = Make(8f);
            // Cell centres (the 1.2 u grid starts at -8.4).
            var meadow = new Vector2(0.6f, 0.6f);
            var wood = new Vector2(-3f, 0.6f);
            var bare = new Vector2(0.6f, -3f);
            life.SetStageAt(meadow, 0.5f);
            life.SetStageAt(wood, 1.3f);
            life.SetStageAt(bare, 0.02f);
            Vector2 shore = ShorePoint(life, 8f);
            life.SetStageAt(shore, 0.3f);

            Assert.AreEqual(3, life.CoastDistanceAt(meadow), "the centre is inland");
            Assert.Greater(life.FoodAt(meadow, Diet.Grass), 0.9f, "a young meadow is the best pasture");
            Assert.Less(life.FoodAt(wood, Diet.Grass), 0.4f, "a closed temperate wood holds little grass");
            Assert.Less(life.FoodAt(bare, Diet.Grass), 0.05f, "bare ground holds no grass");

            Assert.Greater(life.FoodAt(wood, Diet.Leaves), 0.9f, "mature trees are full of leaves");
            Assert.AreEqual(0f, life.FoodAt(meadow, Diet.Leaves), 1e-4f, "a meadow has no foliage");

            Assert.AreEqual(0f, life.FoodAt(meadow, Diet.Reeds), 1e-4f, "no reeds inland");
            Assert.Greater(life.FoodAt(shore, Diet.Reeds), 0.6f, "low growth at the shore is reed");

            Assert.AreEqual(0f, life.FoodAt(meadow, Diet.Fish), 1e-4f, "no fish inland");
            float fish = life.FoodAt(shore, Diet.Fish);
            Assert.Greater(fish, 0.9f, "the shore always holds fish");
            life.GetComponent<FakeIslandSurface>().storm = 1f;
            float stormy = life.FoodAt(shore, Diet.Fish);
            Assert.Less(stormy, fish, "a storm makes fishing harder");
            Assert.Greater(stormy, 0.5f, "but never empties the sea");

            Assert.AreEqual(0f, life.FoodAt(new Vector2(20f, 20f), Diet.Grass), "off the grid there is nothing");
        }

        [Test]
        public void Savanna_KeepsItsGrassUnderTheTrees()
        {
            var savanna = Make(8f, 7, LifeBiome.Savanna, "Savanna");
            var temperate = Make(8f, 7, LifeBiome.Temperate, "Temperate");
            savanna.SetStageAt(Vector2.zero, 1.3f);
            temperate.SetStageAt(Vector2.zero, 1.3f);
            Assert.Greater(savanna.FoodAt(Vector2.zero, Diet.Grass), 0.8f);
            Assert.Less(temperate.FoodAt(Vector2.zero, Diet.Grass), 0.4f);
        }

        [Test]
        public void Graze_LowersTheCell_RegrowsSlowerThanAHerdEats_AndRecovers()
        {
            var life = Make(8f);
            // Cell centres (the 1.2 u grid starts at -8.4), so the tint reads exactly these two cells.
            var a = new Vector2(0.6f, 0.6f);
            var b = new Vector2(4.2f, 0.6f);
            life.SetStageAt(a, 0.5f);
            life.SetStageAt(b, 0.4f);

            float eaten = life.Graze(a, Diet.Grass, 0.1f);
            Assert.AreEqual(0.1f, eaten, 1e-4f, "a good meadow feeds fully");
            Assert.AreEqual(0.4f, life.StageAt(a), 1e-4f, "grazing lowers the stage by what was eaten");
            Assert.Greater(life.GrazedAt(a), 0.7f, "the cell carries a graze mark");
            Assert.Less(life.FoodAt(a, Diet.Grass), life.FoodAt(b, Diet.Grass), "a grazed meadow holds less grass than an untouched one");

            // The mark shows: the ground of the grazed cell is yellower than that of the untouched one at the same stage.
            life.RefreshAfterMerge();
            Color ga = life.GroundTintAt(a), gb = life.GroundTintAt(b);
            Assert.Less(ga.b / ga.r, gb.b / gb.r - 0.05f, "the grazed ground is not yellowed");

            // One real second (timeScale life-seconds): the grazed cell regrows far slower than a herd of 8 eats
            // (~0.02 stage per second), the untouched one at the normal rate.
            float a0 = life.StageAt(a), b0 = life.StageAt(b);
            life.Tick(life.timeScale);
            float regrowA = life.StageAt(a) - a0, regrowB = life.StageAt(b) - b0;
            Assert.Less(regrowA, 0.005f, "grazed grass regrows as fast as a herd eats it");
            Assert.Greater(regrowB, regrowA * 5f, "the graze mark does not slow the regrowth");

            // A few minutes later the pasture is back.
            for (int t = 0; t < 40; t++) life.Tick(life.grazeRecoverSeconds * life.timeScale / 30f);
            Assert.AreEqual(0f, life.GrazedAt(a), 1e-4f, "the graze mark never fades");
            Assert.Greater(life.StageAt(a), 0.9f, "the pasture never regrew");
        }

        [Test]
        public void Graze_LeavesStopAtTheWoodFloor_FishNeverRunOut_WoodsAreNotFelledByGrazers()
        {
            var life = Make(8f);
            var wood = new Vector2(-3f, 0.6f);
            life.SetStageAt(wood, 1.3f);
            float total = 0f;
            for (int i = 0; i < 40; i++) total += life.Graze(wood, Diet.Leaves, 0.05f);
            Assert.Greater(total, 0.2f);
            Assert.GreaterOrEqual(life.StageAt(wood), 0.7f - 1e-4f, "browsing took the trees below the wood floor");
            Assert.AreEqual(0f, life.Graze(wood, Diet.Leaves, 0.05f), 1e-4f, "stripped trees still feed");

            var forest = new Vector2(0.6f, 3f);
            life.SetStageAt(forest, 1.3f);
            life.Graze(forest, Diet.Grass, 0.5f);
            Assert.AreEqual(1.3f, life.StageAt(forest), 1e-4f, "grass eaters felled a closed wood");
            Assert.Greater(life.GrazedAt(forest), 0.5f, "cropping the undergrowth leaves no mark");

            Vector2 shore = ShorePoint(life, 8f);
            float s0 = life.StageAt(shore);
            for (int i = 0; i < 20; i++) Assert.Greater(life.Graze(shore, Diet.Fish, 0.05f), 0.04f);
            Assert.AreEqual(s0, life.StageAt(shore), 1e-5f, "fishing changed the vegetation");
            Assert.AreEqual(0f, life.Graze(new Vector2(0f, 0f), Diet.Fish, 0.05f), "fish inland");
        }

        [Test]
        public void TryFindFood_FindsTheGreenerPatch_AndPrefersTheNearerOne()
        {
            var life = Make(10f);
            Fill(life, 10f, 1.3f);
            var rnd = new System.Random(3);
            var from = new Vector2(-4f, 0f);
            Assert.IsFalse(life.TryFindFood(from, Diet.Grass, 9f, 0.5f, rnd, out _), "a closed wood is no pasture");

            var far = new Vector2(4.8f, 0f);
            Patch(life, far, 0.5f, 1.9f);
            Assert.IsTrue(life.TryFindFood(from, Diet.Grass, 9f, 0.5f, rnd, out Vector2 t));
            Assert.Less((t - far).magnitude, 3f, "the search walked past the green patch");
            Assert.Greater(life.FoodAt(t, Diet.Grass), 0.5f);

            var near = new Vector2(-4f, 3.6f);
            Patch(life, near, 0.5f, 1.9f);
            for (int i = 0; i < 6; i++)
            {
                Assert.IsTrue(life.TryFindFood(from, Diet.Grass, 9f, 0.5f, rnd, out t));
                Assert.Less((t - near).magnitude, 3.8f, "the nearer of two equal patches should win");
                Assert.Greater(life.FoodAt(t, Diet.Grass), 0.5f);
            }
            Assert.IsFalse(life.TryFindFood(from, Diet.Grass, 9f, 1.01f, rnd, out _), "minFood above anything must fail");
        }

        [Test]
        public void ShadeAt_UnderTrees_AndTryFindShelterGoesThere()
        {
            var life = Make(10f);
            Fill(life, 10f, 0.3f);
            var grove = new Vector2(3.6f, 3.6f);
            Patch(life, grove, 1.3f);
            Assert.Greater(life.ShadeAt(grove), 0.8f, "no shade in the middle of a grove");
            Assert.Less(life.ShadeAt(new Vector2(-4f, -4f)), 0.01f, "shade on an open meadow");
            float edge = life.ShadeAt(grove + new Vector2(2.4f, 0f));
            Assert.Greater(edge, 0f, "the shade must fade out, not stop at the cell");
            Assert.Less(edge, life.ShadeAt(grove));

            Assert.IsTrue(life.TryFindShelter(new Vector2(-2f, -1f), 9f, new System.Random(5), out Vector2 t));
            Assert.Greater(life.ShadeAt(t), 0.4f, "shelter found in the open");
            Assert.Less((t - grove).magnitude, 3.5f);
            Assert.IsFalse(life.TryFindShelter(new Vector2(-6f, -5f), 2f, null, out _), "shelter on an open meadow");
        }

        IslandLifeSystem StageMerge(LifeBiome hostBiome, LifeBiome guestBiome, out Vector2 guestAt)
        {
            const float hostRadius = 8f, guestRadius = 5f;
            guestAt = new Vector2(hostRadius + guestRadius + 0.6f, 0f);
            var host = Make(hostRadius, 1100, hostBiome, "MergeHost");
            var guest = Make(guestRadius, 1101, guestBiome, "MergeGuest");
            guest.transform.position = new Vector3(guestAt.x, 0f, guestAt.y);
            host.AbsorbFrom(guest);
            var surface = host.GetComponent<FakeIslandSurface>();
            surface.radius = guestAt.x + guestRadius;
            surface.version++;
            host.Tick(1f);
            return host;
        }

        [Test]
        public void ClimateAt_IsAGradientAcrossAMergeSeam()
        {
            var plain = Make(6f, 3, LifeBiome.Nordic, "Plain");
            Assert.AreEqual(-1f, plain.ClimateAt(Vector2.zero), 1e-4f);
            Assert.AreEqual(-1f, plain.ClimateAt(new Vector2(4f, -2f)), 1e-4f);

            var host = StageMerge(LifeBiome.Nordic, LifeBiome.Tropical, out Vector2 guestAt);
            Assert.AreEqual(-1f, host.ClimateAt(new Vector2(-5f, 0f)), 1e-3f, "deep in the nordic land");
            Assert.AreEqual(1f, host.ClimateAt(guestAt), 1e-3f, "deep in the tropical land");

            float prev = host.ClimateAt(new Vector2(-5f, 0f)), maxStep = 0f;
            bool between = false;
            for (float x = -5f; x <= guestAt.x; x += 0.2f)
            {
                float c = host.ClimateAt(new Vector2(x, 0f));
                maxStep = Mathf.Max(maxStep, Mathf.Abs(c - prev));
                if (c > -0.6f && c < 0.6f) between = true;
                prev = c;
            }
            Assert.IsTrue(between, "the seam is a hard line");
            Assert.Less(maxStep, 0.25f, "the climate jumps at the seam");
        }

        [Test]
        public void SeamBlending_MovesSeamCellsOverTime_AndKeepsTheDominantBiome()
        {
            var host = StageMerge(LifeBiome.Nordic, LifeBiome.Tropical, out Vector2 guestAt);
            var dominant = host.DominantBiome;
            int nordic = host.CellsOfBiome(LifeBiome.Nordic), tropical = host.CellsOfBiome(LifeBiome.Tropical);
            Assert.Greater(nordic, 0);
            Assert.Greater(tropical, 0);
            Assert.AreEqual(0, host.BiomeBlends);

            // A single-biome island never blends.
            var plain = Make(6f, 3, LifeBiome.Nordic, "Plain");
            plain.biomeBlendRate = 0.05f;
            for (int t = 0; t < 32; t++) plain.Tick(10f);
            Assert.AreEqual(0, plain.BiomeBlends);

            var seam = new List<Vector2>();
            var before = new List<int>();
            for (float x = -6f; x <= guestAt.x + 4f; x += 0.6f)
                for (float z = -6f; z <= 6f; z += 0.6f)
                {
                    seam.Add(new Vector2(x, z));
                    before.Add(host.BiomeAt(new Vector2(x, z)));
                }
            float climateBefore = 0f;
            foreach (var p in seam) climateBefore += host.ClimateAt(p);

            host.biomeBlendRate = 0.05f;
            for (int t = 0; t < 48; t++) host.Tick(10f);

            Assert.Greater(host.BiomeBlends, 0, "no seam cell took on its neighbour's biome");
            int changed = 0;
            for (int i = 0; i < seam.Count; i++) if (host.BiomeAt(seam[i]) != before[i]) changed++;
            Assert.Greater(changed, 0);
            Assert.AreEqual(dominant, host.DominantBiome, "the blending flipped the island's biome");
            Assert.Greater(host.CellsOfBiome(LifeBiome.Nordic), 8);
            Assert.Greater(host.CellsOfBiome(LifeBiome.Tropical), 8);
            Assert.AreEqual(host.LandCells, host.CellsOfBiome(LifeBiome.Nordic) + host.CellsOfBiome(LifeBiome.Tropical));
            float climateAfter = 0f;
            foreach (var p in seam) climateAfter += host.ClimateAt(p);
            Assert.AreNotEqual(climateBefore, climateAfter, "the climate field did not follow the blended cells");
        }

        [Test]
        public void SeasonProvider_FollowsTheIslandsSeason()
        {
            var life = Make(6f);
            Assert.IsNotNull(LifeEnvironment.SeasonProvider, "the life system installs the season provider");
            IslandLifeSystem.SeasonSource = life;
            life.Tick(life.seasonPeriod * life.timeScale * 0.3f);
            Assert.AreEqual(life.Season, LifeEnvironment.Season, 1e-4f);
            Assert.Greater(LifeEnvironment.Season, 0.25f);
        }

        [Test]
        public void EnvironmentQueries_DoNotAllocate()
        {
            var life = Make(10f);
            var rnd = new System.Random(1);
            life.Graze(Vector2.zero, Diet.Grass, 0.01f);
            life.TryFindFood(Vector2.zero, Diet.Grass, 6f, 0.2f, rnd, out _);
            life.TryFindShelter(Vector2.zero, 6f, rnd, out _);
            life.ClimateAt(Vector2.zero);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            float sink = 0f;
            for (int i = 0; i < 50; i++)
            {
                var p = new Vector2(i * 0.1f - 2f, 1f);
                sink += life.FoodAt(p, (Diet)(i % 4)) + life.ShadeAt(p) + life.ClimateAt(p) + life.Graze(p, Diet.Grass, 0.001f);
                life.TryFindFood(p, (Diet)(i % 4), 6f, 0.2f, rnd, out Vector2 t);
                life.TryFindShelter(p, 6f, null, out Vector2 s);
                sink += t.x + s.x;
            }
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated, "environment queries allocated " + allocated + " bytes (" + sink + ")");
        }
    }
}
