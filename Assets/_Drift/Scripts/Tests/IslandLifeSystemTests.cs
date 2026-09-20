using System.Collections.Generic;
using System.Linq;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class IslandLifeSystemTests
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

        [Test]
        public void Repopulate_SecondCallForSameVersionIsNoOp()
        {
            var life = Make(8f, 5, "TestIsle", true);
            var herds = life.GetComponent<IslandHerdSystem>();
            Assert.AreEqual(1, life.GridBuilds);
            Assert.AreEqual(1, herds.Populations);
            life.Repopulate();
            herds.Repopulate();
            Assert.AreEqual(1, life.GridBuilds);
            Assert.AreEqual(1, herds.Populations);
        }

        [Test]
        public void Succession_TreesNeverDie_GrassYieldsToCanopy()
        {
            var life = Make(8f, 3);
            int Woody() => life.CountOf(LifeKind.Tree) + life.CountOf(LifeKind.Bush);
            int woodyStart = Woody(), woodyMin = woodyStart, treeMin = life.CountOf(LifeKind.Tree);
            int grassStart = life.CountOf(LifeKind.Grass), grassPeak = grassStart;
            for (int t = 0; t < 600; t++)
            {
                life.Tick(1f);
                woodyMin = Mathf.Min(woodyMin, Woody());
                treeMin = Mathf.Min(treeMin, life.CountOf(LifeKind.Tree));
                grassPeak = Mathf.Max(grassPeak, life.CountOf(LifeKind.Grass));
            }
            Assert.AreEqual(woodyStart, woodyMin);
            Assert.GreaterOrEqual(treeMin, 0);
            Assert.Greater(Woody(), woodyStart);
            Assert.GreaterOrEqual(grassPeak, grassStart);
            Assert.Less(life.CountOf(LifeKind.Grass), grassPeak);
            Assert.AreEqual(0, life.StrikeCount);
        }

        [Test]
        public void Herds_AreTightClustersSpacedApart()
        {
            var life = Make(4f, 7, "ClusterIsle", true);
            var herds = life.GetComponent<IslandHerdSystem>();
            Assert.Greater(herds.HerdCount, 1);
            for (int h = 0; h < herds.HerdCount; h++)
                for (int o = 0; o < h; o++)
                    Assert.GreaterOrEqual(Vector2.Distance(herds.HerdCenter(h), herds.HerdCenter(o)), herds.herdSpacing, "herds " + h + "/" + o + " spawned on top of each other");
            for (int t = 0; t < 200; t++) herds.Step(0.05f);
            for (int h = 0; h < herds.HerdCount; h++)
            {
                float bound = herds.BodyLength(h) * herds.formationSpacing * Mathf.Sqrt(herds.HerdSize(h) + 1) + 0.05f;
                Assert.LessOrEqual(herds.HerdRadius(h), bound, "herd " + h + " (" + herds.HerdKind(h) + ") is scattered");
            }
            Assert.LessOrEqual(herds.AnimalCount, herds.maxAnimals);
        }

        [Test]
        public void Lightning_OnlyStrikesInStorms()
        {
            var life = Make(6f, 9);
            var surface = life.GetComponent<FakeIslandSurface>();
            life.Simulate(600f);
            Assert.AreEqual(0, life.StrikeCount);
            surface.storm = 1f;
            for (int t = 0; t < 3000; t++) life.Step(0.2f);
            Assert.Greater(life.StrikeCount, 0);
            Assert.Greater(life.IgnitionCount, 0);
        }

        [Test]
        public void HerdsRoundTripThroughLifeSave()
        {
            var a = Make(8f, 11, "HerdIsle", true);
            var ha = a.GetComponent<IslandHerdSystem>();
            Assert.Greater(ha.HerdCount, 0);
            for (int t = 0; t < 100; t++) ha.Step(0.05f);
            var saved = JsonUtility.FromJson<LifeSaveData>(JsonUtility.ToJson(a.Capture()));
            Assert.AreEqual(ha.HerdCount, saved.herds.Count);

            var b = Make(8f, 11, "HerdIsle", true);
            var hb = b.GetComponent<IslandHerdSystem>();
            b.Restore(saved);
            Assert.AreEqual(ha.HerdCount, hb.HerdCount);
            Assert.AreEqual(ha.AnimalCount, hb.AnimalCount);
            for (int h = 0; h < ha.HerdCount; h++)
            {
                Assert.AreEqual(ha.HerdKind(h), hb.HerdKind(h));
                Assert.AreEqual(ha.HerdCenter(h), hb.HerdCenter(h));
                for (int m = 0; m < ha.HerdSize(h); m++) Assert.AreEqual(ha.AnimalPosition(h, m), hb.AnimalPosition(h, m));
            }
        }

        static void AssertSameSave(LifeSaveData a, LifeSaveData b)
        {
            Assert.AreEqual(a.nx, b.nx);
            Assert.AreEqual(a.nz, b.nz);
            Assert.AreEqual(a.cellSize, b.cellSize);
            Assert.AreEqual(a.originX, b.originX);
            Assert.AreEqual(a.originZ, b.originZ);
            Assert.AreEqual(a.noiseX, b.noiseX);
            Assert.AreEqual(a.noiseZ, b.noiseZ);
            Assert.AreEqual(a.stage, b.stage);
            Assert.AreEqual(a.burn, b.burn);
            Assert.AreEqual(a.fireT, b.fireT);
        }

        [Test]
        public void Repopulate_CreatesGridAndPlants()
        {
            var life = Make(8f);
            var save = life.Capture();
            Assert.Greater(save.nx, 0);
            Assert.AreEqual(save.nx * save.nz, save.stage.Length);
            Assert.Greater(life.PlantCount, 0);
            life.GetStageCounts(out int bare, out int plains, out int shrub, out int forest);
            Assert.Greater(bare + plains + shrub + forest, 0);
        }

        [Test]
        public void Repopulate_IsDeterministicForSameNameAndSeed()
        {
            var a = Make(8f, 5);
            var b = Make(8f, 5);
            AssertSameSave(a.Capture(), b.Capture());
            Assert.AreEqual(a.PlantCount, b.PlantCount);

            var before = a.Capture();
            a.Repopulate();
            AssertSameSave(before, a.Capture());
        }

        [Test]
        public void DifferentSeedsDiffer()
        {
            var a = Make(8f, 1);
            var b = Make(8f, 2);
            Assert.AreNotEqual(a.Capture().noiseX, b.Capture().noiseX);
            Assert.That(a.Capture().stage.SequenceEqual(b.Capture().stage), Is.False);
        }

        [Test]
        public void PopulationScalesWithLandArea()
        {
            var small = Make(4f);
            var medium = Make(8f);
            var large = Make(14f);

            Assert.Greater(small.PlantCount, 0);
            Assert.Greater(medium.PlantCount, small.PlantCount * 2);
            Assert.Greater(large.PlantCount, medium.PlantCount * 2);
        }

        [Test]
        public void LandCellCountTracksSurfaceArea()
        {
            var life = Make(10f);
            life.GetStats(out _, out _, out int landCells);
            float expected = Mathf.PI * 10f * 10f / (life.cellSize * life.cellSize);
            Assert.AreEqual(expected, landCells, expected * 0.25f);
        }

        [Test]
        public void Simulate_IsDeterministic()
        {
            var a = Make(8f, 3);
            var b = Make(8f, 3);
            a.Simulate(120f);
            b.Simulate(120f);
            AssertSameSave(a.Capture(), b.Capture());
            Assert.AreEqual(a.PlantCount, b.PlantCount);
        }

        [Test]
        public void Simulate_ChangesTheEcosystem()
        {
            var life = Make(8f, 3);
            var before = life.Capture();
            life.Simulate(120f);
            Assert.That(before.stage.SequenceEqual(life.Capture().stage), Is.False);
        }

        [Test]
        public void Simulate_KeepsStagesWithinBounds()
        {
            var life = Make(8f, 3);
            life.Simulate(300f);
            foreach (float s in life.Capture().stage)
            {
                Assert.GreaterOrEqual(s, 0f);
                Assert.LessOrEqual(s, 1.3f + 1e-4f);
            }
        }

        [Test]
        public void CatchUp_MatchesSimulateWithTenSecondSteps()
        {
            var a = Make(8f, 4);
            var b = Make(8f, 4);
            a.CatchUp(200f);
            b.Simulate(200f, 10f);
            AssertSameSave(a.Capture(), b.Capture());
        }

        [Test]
        public void CatchUp_IsDeterministic()
        {
            var a = Make(8f, 4);
            var b = Make(8f, 4);
            a.CatchUp(500f);
            b.CatchUp(500f);
            AssertSameSave(a.Capture(), b.Capture());
        }

        [Test]
        public void CatchUp_ShortIntervalsDoNothing()
        {
            var life = Make(8f, 4);
            var before = life.Capture();
            life.CatchUp(0.4f);
            life.CatchUp(-10f);
            AssertSameSave(before, life.Capture());
        }

        [Test]
        public void CaptureRestore_RoundTripsState()
        {
            var a = Make(8f, 6);
            a.Simulate(150f);
            var saved = a.Capture();

            var b = Make(8f, 6);
            b.Restore(saved);

            AssertSameSave(saved, b.Capture());
            a.GetStageCounts(out int a0, out int a1, out int a2, out int a3);
            b.GetStageCounts(out int b0, out int b1, out int b2, out int b3);
            Assert.AreEqual((a0, a1, a2, a3), (b0, b1, b2, b3));
            Assert.Greater(b.PlantCount, 0);
        }

        [Test]
        public void Capture_ReturnsIndependentCopies()
        {
            var life = Make(8f, 6);
            var saved = life.Capture();
            float original = saved.stage[0];
            saved.stage[0] = original + 50f;
            Assert.AreEqual(original, life.Capture().stage[0]);

            var b = Make(8f, 6);
            var toRestore = life.Capture();
            b.Restore(toRestore);
            toRestore.stage[0] = original + 50f;
            Assert.AreEqual(original, b.Capture().stage[0]);
        }

        [Test]
        public void Restore_NullIsIgnored()
        {
            var life = Make(8f, 6);
            var before = life.Capture();
            life.Restore(null);
            life.Restore(new LifeSaveData());
            AssertSameSave(before, life.Capture());
        }
    }
}
