using System.Collections.Generic;
using System.Linq;
using Drift.Core;
using Drift.Islands;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class LifePhase1Tests
    {
        readonly List<GameObject> _objects = new();

        [SetUp]
        public void SetUp() => LifeLod.DistanceProvider = _ => 0f;

        [TearDown]
        public void TearDown()
        {
            LifeLod.DistanceProvider = null;
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        IslandLifeSystem Make(float radius, int seed = 1, string name = "TestIsle", bool herds = false)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            var surface = go.AddComponent<FakeIslandSurface>();
            surface.radius = radius;
            var life = go.AddComponent<IslandLifeSystem>();
            life.seed = seed;
            if (herds) go.AddComponent<IslandHerdSystem>().seed = seed;
            go.SetActive(true);
            _objects.Add(go);
            return life;
        }

        static void AssertSameSave(LifeSaveData a, LifeSaveData b)
        {
            Assert.AreEqual(a.nx, b.nx);
            Assert.AreEqual(a.nz, b.nz);
            Assert.AreEqual(a.originX, b.originX);
            Assert.AreEqual(a.originZ, b.originZ);
            Assert.AreEqual(a.noiseX, b.noiseX);
            Assert.AreEqual(a.noiseZ, b.noiseZ);
            Assert.AreEqual(a.stage, b.stage);
            Assert.AreEqual(a.burn, b.burn);
            Assert.AreEqual(a.fireT, b.fireT);
        }

        // ------------------------------------------------------------ offline catch-up

        [Test]
        public void Simulate_AdvancesSuccessionAndAge()
        {
            var life = Make(8f, 3);
            life.GetStats(out float before, out _, out _);
            int woodyBefore = life.CountOf(LifeKind.Tree) + life.CountOf(LifeKind.Bush);
            life.Simulate(600f);
            life.GetStats(out float after, out int burning, out _);
            Assert.Greater(after, before);
            Assert.AreEqual(0, burning);
            Assert.AreEqual(600f, life.LifeAge, 1e-3f);
            Assert.Greater(life.CountOf(LifeKind.Tree) + life.CountOf(LifeKind.Bush), woodyBefore);
            Assert.AreEqual(life.PlantCount, life.LivePlantCount, "fast-forward must not leave fading plants behind");
        }

        [Test]
        public void Simulate_MatchesStepwiseTicks()
        {
            var a = Make(8f, 4);
            var b = Make(8f, 4);
            a.Simulate(90f, 3f);
            for (int t = 0; t < 30; t++) b.Tick(3f);
            AssertSameSave(a.Capture(), b.Capture());
        }

        [Test]
        public void Simulate_ZeroStepFallsBackToOneSecond()
        {
            var a = Make(8f, 3);
            var b = Make(8f, 3);
            a.Simulate(30f, 0f);
            b.Simulate(30f, 1f);
            AssertSameSave(a.Capture(), b.Capture());
            Assert.AreEqual(30f, a.LifeAge, 1e-3f);
        }

        [Test]
        public void Simulate_NegativeOrNaNStepDoesNotHang()
        {
            var life = Make(8f, 3);
            life.Simulate(10f, -1f);
            life.Simulate(10f, float.NaN);
            Assert.AreEqual(20f, life.LifeAge, 1e-3f);
        }

        [Test]
        public void Simulate_NonPositiveSecondsIsANoOp()
        {
            var life = Make(8f, 3);
            var before = life.Capture();
            life.Simulate(0f);
            life.Simulate(-100f);
            life.Simulate(float.NaN);
            AssertSameSave(before, life.Capture());
            Assert.AreEqual(0f, life.LifeAge);
        }

        [Test]
        public void CatchUp_LongOfflineStaysWithinBoundsAndKeepsHerds()
        {
            var life = Make(8f, 6, "Offline", true);
            var herds = life.GetComponent<IslandHerdSystem>();
            int animals = herds.AnimalCount;
            life.CatchUp(1800f);
            foreach (float s in life.Capture().stage)
            {
                Assert.GreaterOrEqual(s, 0f);
                Assert.LessOrEqual(s, 1.3f + 1e-4f);
            }
            Assert.AreEqual(animals, herds.AnimalCount);
            Assert.AreEqual(1800f, life.LifeAge, 1e-3f);
        }

        // ------------------------------------------------------------ fixes

        [Test]
        public void Capture_WithoutGridReturnsNullInsteadOfThrowing()
        {
            var go = new GameObject("NoSurface");
            _objects.Add(go);
            var life = go.AddComponent<IslandLifeSystem>();
            var herds = go.AddComponent<IslandHerdSystem>();
            Assert.IsFalse(life.HasGrid);
            Assert.IsNull(life.Capture());
            Assert.DoesNotThrow(() => life.GetStats(out _, out _, out _));
            Assert.DoesNotThrow(() => life.GetStageCounts(out _, out _, out _, out _));
            Assert.DoesNotThrow(() => life.GetHudStats(out _, out _, out _, out _, out _, out _));
            Assert.DoesNotThrow(() => life.Simulate(100f));
            Assert.DoesNotThrow(() => life.Step(0.1f));
            Assert.DoesNotThrow(() => herds.Step(0.1f));
            Assert.AreEqual(0, herds.Capture().Count);
        }

        [Test]
        public void Capture_OnIslandWithoutLandReturnsNull()
        {
            var life = Make(0f, 2, "AllSea", true);
            Assert.IsFalse(life.HasGrid);
            Assert.IsNull(life.Capture());
        }

        [Test]
        public void Repopulate_DoesNotDependOnObjectName()
        {
            var a = Make(8f, 5, "Island_0_0_1", true);
            var b = Make(8f, 5, "Renamed island", true);
            AssertSameSave(a.Capture(), b.Capture());
            Assert.AreEqual(a.PlantCount, b.PlantCount);
            var ha = a.GetComponent<IslandHerdSystem>();
            var hb = b.GetComponent<IslandHerdSystem>();
            Assert.AreEqual(ha.HerdCount, hb.HerdCount);
            for (int h = 0; h < ha.HerdCount; h++)
            {
                Assert.AreEqual(ha.HerdKind(h), hb.HerdKind(h));
                Assert.AreEqual(ha.HerdCenter(h), hb.HerdCenter(h));
            }
        }

        [Test]
        public void Repopulate_DifferentSeedsStillDiffer()
        {
            var a = Make(8f, 5, "Same", true);
            var b = Make(8f, 6, "Same", true);
            Assert.That(a.Capture().stage.SequenceEqual(b.Capture().stage), Is.False);
            var ha = a.GetComponent<IslandHerdSystem>();
            var hb = b.GetComponent<IslandHerdSystem>();
            bool differs = ha.HerdCount != hb.HerdCount;
            for (int h = 0; !differs && h < ha.HerdCount; h++) differs = ha.HerdCenter(h) != hb.HerdCenter(h);
            Assert.IsTrue(differs);
        }

        [Test]
        public void LandBoundsAndCentroid_ShareOneLandDefinition()
        {
            var s = new IslandShape(1f, 5, 5, new Vector2(-2f, -2f));
            s.h[1 * 5 + 1] = 1f;
            s.h[3 * 5 + 3] = IslandShape.Sea + IslandShape.ShelfAboveSea + 0.05f;
            Assert.IsTrue(IslandShape.IsLand(1f));
            Assert.IsFalse(IslandShape.IsLand(0f));
            Assert.IsTrue(IslandShape.IsShelf(s.h[3 * 5 + 3]));
            Assert.IsFalse(IslandShape.IsShelf(IslandShape.Sea));

            Rect land = s.LandBounds();
            Assert.AreEqual(new Vector2(-1f, -1f), land.min);
            Assert.AreEqual(new Vector2(-1f, -1f), land.max);
            Assert.AreEqual(new Vector2(-1f, -1f), s.LandCentroid());

            Rect shelf = s.ShelfBounds();
            Assert.AreEqual(new Vector2(-1f, -1f), shelf.min);
            Assert.AreEqual(new Vector2(1f, 1f), shelf.max);
            Assert.AreEqual(s.LandBounds(IslandShape.ShelfAboveSea), shelf);
        }

        // ------------------------------------------------------------ caps after merges

        [Test]
        public void AbsorbFrom_EnforcesHerdAndAnimalCaps()
        {
            var host = Make(14f, 21, "Host", true);
            var g1 = Make(14f, 22, "Guest1", true);
            var g2 = Make(14f, 23, "Guest2", true);
            var hh = host.GetComponent<IslandHerdSystem>();
            var h1 = g1.GetComponent<IslandHerdSystem>();
            var h2 = g2.GetComponent<IslandHerdSystem>();
            int total = hh.AnimalCount + h1.AnimalCount + h2.AnimalCount;
            Assert.Greater(total, hh.maxAnimals, "the three islands must exceed the cap for the test to mean anything");
            var species = new HashSet<LifeKind>();
            foreach (var hs in new[] { hh, h1, h2 })
                for (int h = 0; h < hs.HerdCount; h++) species.Add(hs.HerdKind(h));

            hh.AbsorbFrom(h1);
            hh.AbsorbFrom(h2);
            Assert.AreEqual(0, h1.HerdCount);
            Assert.AreEqual(0, h2.HerdCount);
            Assert.LessOrEqual(hh.HerdCount, hh.maxHerds);
            Assert.LessOrEqual(hh.AnimalCount, hh.maxAnimals);
            Assert.Greater(hh.AnimalCount, hh.maxAnimals / 2);
            Assert.AreEqual(species.Count, hh.SpeciesCount, "the cap must not wipe out a species");
            var surface = host.GetComponent<FakeIslandSurface>();
            for (int h = 0; h < hh.HerdCount; h++)
            {
                Assert.Greater(hh.HerdSize(h), 0);
                for (int m = 0; m < hh.HerdSize(h); m++)
                    Assert.Greater(surface.SampleHeight(hh.AnimalPosition(h, m)), 0f, "absorbed animal landed in the sea");
            }
            for (int t = 0; t < 100; t++) hh.Step(0.05f);
            Assert.LessOrEqual(hh.AnimalCount, hh.maxAnimals);
            // 120 simple animals stay under 10k; the detail LOD adds up to 40 detailed ones (camera at distance 0 here).
            Assert.LessOrEqual(hh.MeshVertexCount, 17000);
        }

        [Test]
        public void Restore_EnforcesCapsOnOversizedSaves()
        {
            var a = Make(14f, 31, "A", true);
            var b = Make(14f, 32, "B", true);
            var saved = a.Capture().herds;
            saved.AddRange(b.Capture().herds);
            var c = Make(14f, 33, "C", true);
            var hc = c.GetComponent<IslandHerdSystem>();
            hc.Restore(saved);
            Assert.LessOrEqual(hc.HerdCount, hc.maxHerds);
            Assert.LessOrEqual(hc.AnimalCount, hc.maxAnimals);
        }

        // ------------------------------------------------------------ tiers and animation data

        [Test]
        public void HerdMesh_CarriesShaderAnimationData()
        {
            var life = Make(8f, 7, "Anim", true);
            var herds = life.GetComponent<IslandHerdSystem>();
            Assert.IsTrue(herds.MeshHasAnimationData);
            Assert.AreEqual("Drift/Animal", herds.AnimalMaterial.shader.name);
            Assert.IsFalse(life.HasMesh && life.GetComponentInChildren<MeshFilter>() == null);
        }

        [Test]
        public void Herds_NearTierRebuildsAtMostAtMeshRate()
        {
            var life = Make(8f, 8, "Near", true);
            var herds = life.GetComponent<IslandHerdSystem>();
            int before = herds.MeshBuilds;
            for (int t = 0; t < 120; t++) herds.Step(1f / 60f);
            Assert.AreEqual(LifeTier.Near, herds.Tier);
            Assert.LessOrEqual(herds.MeshBuilds - before, Mathf.CeilToInt(2f / herds.meshInterval) + 1);
        }

        [Test]
        public void Herds_UnchangedStepDoesNotRebuild()
        {
            var life = Make(8f, 8, "Idle", true);
            var herds = life.GetComponent<IslandHerdSystem>();
            herds.Step(0f);
            herds.Step(0f);
            int before = herds.MeshBuilds;
            for (int t = 0; t < 20; t++) herds.Step(0f);
            Assert.AreEqual(before, herds.MeshBuilds);
        }

        [Test]
        public void Herds_FarTierDriftsCentresWithoutPerFrameRebuilds()
        {
            var life = Make(8f, 9, "Far", true);
            var herds = life.GetComponent<IslandHerdSystem>();
            var centres = new List<Vector2>();
            for (int h = 0; h < herds.HerdCount; h++) centres.Add(herds.HerdCenter(h));
            LifeLod.DistanceProvider = _ => 1000f;
            int before = herds.MeshBuilds;
            for (int t = 0; t < 400; t++) herds.Step(0.05f);
            Assert.AreEqual(LifeTier.Far, herds.Tier);
            Assert.LessOrEqual(herds.MeshBuilds - before, Mathf.CeilToInt(20f / herds.hiddenMeshInterval) + 1);
            bool moved = false;
            for (int h = 0; h < herds.HerdCount && !moved; h++) moved = herds.HerdCenter(h) != centres[h];
            Assert.IsTrue(moved, "far herds must still drift");
            var surface = life.GetComponent<FakeIslandSurface>();
            for (int h = 0; h < herds.HerdCount; h++)
            {
                float bound = herds.BodyLength(h) * herds.formationSpacing * Mathf.Sqrt(herds.HerdSize(h) + 1) + 0.05f;
                Assert.LessOrEqual(herds.HerdRadius(h), bound, "far herd " + h + " lost its formation");
                for (int m = 0; m < herds.HerdSize(h); m++) Assert.Greater(surface.SampleHeight(herds.AnimalPosition(h, m)), 0f);
            }
            Assert.LessOrEqual(herds.AnimalCount, herds.maxAnimals);
        }

        [Test]
        public void Herds_MidTierKeepsSimulatingAtLowerRate()
        {
            var life = Make(8f, 9, "Mid", true);
            var herds = life.GetComponent<IslandHerdSystem>();
            LifeLod.DistanceProvider = _ => (herds.detailDistance + herds.simDistance) * 0.5f;
            var centres = new List<Vector2>();
            for (int h = 0; h < herds.HerdCount; h++) centres.Add(herds.HerdCenter(h));
            int before = herds.MeshBuilds;
            for (int t = 0; t < 600; t++) herds.Step(1f / 60f);
            Assert.AreEqual(LifeTier.Mid, herds.Tier);
            bool moved = false;
            for (int h = 0; h < herds.HerdCount && !moved; h++) moved = herds.HerdCenter(h) != centres[h];
            Assert.IsTrue(moved);
            Assert.LessOrEqual(herds.MeshBuilds - before, Mathf.CeilToInt(10f / (herds.meshInterval * 2f)) + 1);
        }

        [Test]
        public void Plants_FarTierTicksSlowlyAndNeverRebuilds()
        {
            var life = Make(8f, 10);
            LifeLod.DistanceProvider = _ => 1000f;
            int builds = life.MeshBuilds, ticks = life.Ticks;
            float age = life.LifeAge;
            for (int t = 0; t < 600; t++) life.Step(1f / 60f);
            Assert.AreEqual(LifeTier.Far, life.Tier);
            Assert.AreEqual(builds, life.MeshBuilds);
            float farAge = life.LifeAge - age;
            int farTicks = life.Ticks - ticks;
            Assert.Greater(farTicks, 0);

            // Far islands cover the same life-time in fewer, coarser ticks.
            var near = Make(8f, 10, "NearPlants");
            LifeLod.DistanceProvider = _ => 0f;
            float nearAge0 = near.LifeAge;
            int nearTicks0 = near.Ticks;
            for (int t = 0; t < 600; t++) near.Step(1f / 60f);
            Assert.AreEqual(LifeTier.Near, near.Tier);
            Assert.AreEqual(near.LifeAge - nearAge0, farAge, near.tickInterval * near.farTickFactor * near.timeScale);
            Assert.Greater(near.Ticks - nearTicks0, farTicks * 4);
        }

        [Test]
        public void Plants_MidTierDoublesMeshInterval()
        {
            var nearLife = Make(8f, 11, "NearVeg");
            var midLife = Make(8f, 11, "MidVeg");
            var surfaceMid = midLife.GetComponent<FakeIslandSurface>();
            LifeLod.DistanceProvider = p => p.x > 0.5f ? (nearLife.detailDistance + nearLife.simDistance) * 0.5f : 0f;
            surfaceMid.transform.position = new Vector3(1f, 0f, 0f);
            int nb = nearLife.MeshBuilds, mb = midLife.MeshBuilds;
            for (int t = 0; t < 600; t++) { nearLife.Step(1f / 60f); midLife.Step(1f / 60f); }
            Assert.AreEqual(LifeTier.Near, nearLife.Tier);
            Assert.AreEqual(LifeTier.Mid, midLife.Tier);
            Assert.LessOrEqual(nearLife.MeshBuilds - nb, Mathf.CeilToInt(10f / nearLife.meshInterval) + 1);
            Assert.LessOrEqual(midLife.MeshBuilds - mb, Mathf.CeilToInt(10f / (midLife.meshInterval * 2f)) + 1);
        }

        [Test]
        public void LifeLod_FallsBackToNearWithoutProviderOrCamera()
        {
            LifeLod.DistanceProvider = null;
            if (Camera.main == null) Assert.AreEqual(0f, LifeLod.Distance(new Vector3(500f, 0f, 500f)));
            Assert.AreEqual(LifeTier.Near, LifeLod.Tier(10f, 45f, 100f));
            Assert.AreEqual(LifeTier.Mid, LifeLod.Tier(60f, 45f, 100f));
            Assert.AreEqual(LifeTier.Far, LifeLod.Tier(100f, 45f, 100f));
        }
    }
}
