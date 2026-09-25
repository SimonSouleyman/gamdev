using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Owner 2026-09-26: "Bitte nicht mehr als 3 gleiche Tiergruppen auf einer Insel." At most maxHerdsPerSpecies herds of a
    // species per island - when an island is populated, when new land brings herds (one at a time, herdSpawnGap apart),
    // when a herd is added from outside, and after a merge, where the surplus herds of a species fold into their own
    // kind before any other species is trimmed. The herd mesh follows a merge at once (RefreshMesh).
    public class HerdCapTests
    {
        readonly List<GameObject> _objects = new();

        [SetUp]
        public void SetUp()
        {
            SpeciesPool.Clear();
            LifeLod.DistanceProvider = _ => 0f;
            LifeEnvironment.NightProvider = () => 0f;
            LifeEnvironment.ViewDistanceProvider = null;
            LifeEnvironment.PointOfInterest = null;
            IslandLifeSystem.ResetSeasonReference();
        }

        [TearDown]
        public void TearDown()
        {
            SpeciesPool.Clear();
            LifeLod.DistanceProvider = null;
            LifeEnvironment.NightProvider = null;
            IslandLifeSystem.ResetSeasonReference();
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        IslandHerdSystem Make(float radius, int seed, LifeBiome biome, string name = "CapIsle")
        {
            var go = new GameObject(name);
            go.SetActive(false);
            var s = go.AddComponent<FakeIslandSurface>();
            s.radius = radius;
            s.height = 1.2f;
            s.biome = (int)biome;
            go.AddComponent<IslandLifeSystem>().seed = seed;
            var herds = go.AddComponent<IslandHerdSystem>();
            herds.seed = seed;
            go.SetActive(true);
            _objects.Add(go);
            return herds;
        }

        static FakeIslandSurface Surface(IslandHerdSystem h) => h.GetComponent<FakeIslandSurface>();

        static int MostOfOneSpecies(IslandHerdSystem h)
        {
            int most = 0;
            foreach (var k in SpeciesPool.HerdKinds) most = Mathf.Max(most, h.HerdsOf(k));
            return most;
        }

        [Test]
        public void Populate_NeverMoreThanThreeHerdsOfASpecies()
        {
            for (int b = 0; b < 4; b++)
                for (int seed = 1; seed <= 3; seed++)
                {
                    var herds = Make(10f, 100 * b + seed, (LifeBiome)b);
                    Assert.Greater(herds.DesiredHerds(), 20, "the island must want far more herds than the cap allows");
                    Assert.Greater(herds.HerdCount, 1);
                    Assert.LessOrEqual(MostOfOneSpecies(herds), herds.maxHerdsPerSpecies, (LifeBiome)b + " seed " + seed);
                }

            // A run's pool with a single temperate species: three herds of it, however big the island.
            SpeciesPool.Set(SpeciesPool.Bit(LifeKind.Hare) | SpeciesPool.Bit(LifeKind.Zebra), LifeKind.Hare);
            var pooled = Make(10f, 77, LifeBiome.Temperate);
            Assert.AreEqual(3, pooled.HerdsOf(LifeKind.Hare));
            Assert.AreEqual(3, pooled.HerdCount);
        }

        [Test]
        public void AddHerd_RefusesAFourthHerdOfTheSameSpecies()
        {
            var herds = Make(9f, 5, LifeBiome.Temperate);
            herds.ClearHerds();
            for (int i = 0; i < 3; i++) Assert.GreaterOrEqual(herds.AddHerd(LifeKind.Hare, new Vector2(-4f + 3f * i, 0f), 5), 0);
            Assert.AreEqual(-1, herds.AddHerd(LifeKind.Hare, new Vector2(0f, 4f), 5));
            Assert.GreaterOrEqual(herds.AddHerd(LifeKind.Sheep, new Vector2(0f, 4f), 5), 0, "another species still fits");
            Assert.AreEqual(3, herds.HerdsOf(LifeKind.Hare));
        }

        [Test]
        public void NewLand_BringsItsHerdsOneAtATime()
        {
            var herds = Make(4f, 21, LifeBiome.Temperate);
            int start = herds.HerdCount;
            Surface(herds).radius = 10f;
            Surface(herds).version++;
            int prev = start, spawns = 0;
            float t = 0f, last = -100f, minGap = float.MaxValue;
            for (int i = 0; i < 600; i++)
            {
                herds.Step(0.1f);
                t += 0.1f;
                int n = herds.HerdCount;
                Assert.LessOrEqual(n - prev, 1, "two herds appeared in the same step");
                if (n > prev)
                {
                    spawns++;
                    minGap = Mathf.Min(minGap, t - last);
                    last = t;
                }
                prev = n;
                Assert.LessOrEqual(MostOfOneSpecies(herds), herds.maxHerdsPerSpecies);
            }
            Assert.GreaterOrEqual(spawns, 3, "the new land brought hardly any herds");
            Assert.GreaterOrEqual(minGap, herds.herdSpawnGap - 0.15f, "herds appeared closer together than herdSpawnGap");
        }

        [Test]
        public void Merge_FoldsTheFourthHerdOfASpeciesIntoItsOwnKind_OtherSpeciesUntouched()
        {
            var host = Make(9f, 31, LifeBiome.Temperate, "Host");
            host.ClearHerds();
            for (int i = 0; i < 3; i++) Assert.GreaterOrEqual(host.AddHerd(LifeKind.Hare, new Vector2(-5f + 4f * i, 2f), 5), 0);
            Assert.GreaterOrEqual(host.AddHerd(LifeKind.Sheep, new Vector2(0f, -4f), 6), 0);
            var guest = Make(6f, 32, LifeBiome.Nordic, "Guest");
            guest.ClearHerds();
            Assert.GreaterOrEqual(guest.AddHerd(LifeKind.Hare, new Vector2(-2f, 0f), 4), 0);
            Assert.GreaterOrEqual(guest.AddHerd(LifeKind.Hare, new Vector2(2f, 0f), 6), 0);
            Assert.GreaterOrEqual(guest.AddHerd(LifeKind.Reindeer, new Vector2(0f, 3f), 6), 0);

            host.AbsorbFrom(guest);
            Assert.AreEqual(3, host.HerdsOf(LifeKind.Hare));
            Assert.AreEqual(25, host.AnimalsOf(LifeKind.Hare), "the hares of the folded herds joined their kind");
            Assert.AreEqual(1, host.HerdsOf(LifeKind.Sheep));
            Assert.AreEqual(6, host.AnimalsOf(LifeKind.Sheep));
            Assert.AreEqual(1, host.HerdsOf(LifeKind.Reindeer));
            Assert.AreEqual(6, host.AnimalsOf(LifeKind.Reindeer));
            for (int h = 0; h < host.HerdCount; h++) Assert.LessOrEqual(host.HerdSize(h), host.HerdMaxSize(h));
        }

        [Test]
        public void Merge_SpeciesCapComesFirst_TheAnimalCapTrimsAfterwards()
        {
            var host = Make(9f, 41, LifeBiome.Temperate, "Host");
            host.maxAnimals = 40;
            host.ClearHerds();
            for (int i = 0; i < 3; i++) Assert.GreaterOrEqual(host.AddHerd(LifeKind.Hare, new Vector2(-5f + 4f * i, 2f), 8), 0);
            Assert.GreaterOrEqual(host.AddHerd(LifeKind.Sheep, new Vector2(0f, -4f), 6), 0);
            var guest = Make(6f, 42, LifeBiome.Temperate, "Guest");
            guest.ClearHerds();
            for (int i = 0; i < 2; i++) Assert.GreaterOrEqual(guest.AddHerd(LifeKind.Hare, new Vector2(-2f + 4f * i, 0f), 8), 0);
            Assert.GreaterOrEqual(guest.AddHerd(LifeKind.Ox, new Vector2(0f, 3f), 4), 0);

            host.AbsorbFrom(guest);
            Assert.LessOrEqual(host.AnimalCount, 40);
            Assert.LessOrEqual(host.HerdsOf(LifeKind.Hare), 3);
            Assert.Greater(host.HerdsOf(LifeKind.Hare), 0);
            Assert.AreEqual(6, host.AnimalsOf(LifeKind.Sheep), "the sheep paid for the hares' surplus");
            Assert.AreEqual(4, host.AnimalsOf(LifeKind.Ox), "the oxen paid for the hares' surplus");
        }

        [Test]
        public void Restore_AppliesTheSpeciesCap()
        {
            var a = Make(9f, 51, LifeBiome.Temperate, "Old");
            a.maxHerdsPerSpecies = 6;
            a.ClearHerds();
            for (int i = 0; i < 5; i++) Assert.GreaterOrEqual(a.AddHerd(LifeKind.Sheep, new Vector2(-6f + 3f * i, 0f), 4), 0);
            var saved = a.Capture();
            var b = Make(9f, 52, LifeBiome.Temperate, "Loaded");
            b.Restore(saved);
            Assert.AreEqual(3, b.HerdsOf(LifeKind.Sheep));
            Assert.AreEqual(20, b.AnimalsOf(LifeKind.Sheep));
        }

        [Test]
        public void AnimalCap_GrowsWithTheLandOnlyWhenAskedTo()
        {
            var herds = Make(10f, 61, LifeBiome.Temperate);
            float area = Surface(herds).LandArea;
            Assert.AreEqual(herds.maxAnimals, herds.AnimalCap, "off by default");
            herds.animalsPerArea = 0.6f;
            Assert.AreEqual(Mathf.Clamp(Mathf.RoundToInt(area * 0.6f), herds.maxAnimals, herds.maxAnimalsLimit), herds.AnimalCap);
            Surface(herds).radius = 2f;
            Assert.AreEqual(herds.maxAnimals, herds.AnimalCap, "never below maxAnimals");
            Surface(herds).radius = 40f;
            Assert.AreEqual(herds.maxAnimalsLimit, herds.AnimalCap, "never above maxAnimalsLimit");
            // Phone budget: every animal at the biggest simple template (78 verts) plus the full detail budget.
            Assert.LessOrEqual(herds.maxAnimalsLimit * 78 + herds.maxDetailVertices, 30000);
        }

        [Test]
        public void MergeAndShift_RebuildTheHerdMeshAtOnce()
        {
            var host = Make(8f, 71, LifeBiome.Temperate, "Host");
            host.ClearHerds();
            Assert.GreaterOrEqual(host.AddHerd(LifeKind.Sheep, new Vector2(-2f, 0f), 5), 0);
            host.Step(0.1f);
            host.Step(0.1f);
            var guest = Make(5f, 72, LifeBiome.Temperate, "Guest");
            guest.ClearHerds();
            Assert.GreaterOrEqual(guest.AddHerd(LifeKind.Hare, new Vector2(1f, 1f), 6), 0);

            int builds = host.MeshBuilds;
            host.ShiftLocal(new Vector2(1.5f, -0.5f));
            Assert.Greater(host.MeshBuilds, builds, "ShiftLocal left the old bake in place");
            AssertMeshMatches(host);

            builds = host.MeshBuilds;
            host.AbsorbFrom(guest);
            Assert.Greater(host.MeshBuilds, builds, "AbsorbFrom left the old bake in place");
            Assert.AreEqual(2, host.HerdCount);
            AssertMeshMatches(host);
        }

        static void AssertMeshMatches(IslandHerdSystem herds)
        {
            MeshFilter mf = null;
            foreach (Transform t in herds.transform)
                if (t.name == "Herds") mf = t.GetComponent<MeshFilter>();
            Assert.IsNotNull(mf);
            var verts = mf.sharedMesh.vertices;
            for (int h = 0; h < herds.HerdCount; h++)
                for (int m = 0; m < herds.HerdSize(h); m++)
                {
                    Assert.IsTrue(herds.AnimalMeshRange(h, m, out int start, out int count), "herd " + h + " member " + m + " not drawn");
                    Vector3 lo = verts[start], hi = verts[start];
                    for (int k = start; k < start + count; k++) { lo = Vector3.Min(lo, verts[k]); hi = Vector3.Max(hi, verts[k]); }
                    Vector3 mid = (lo + hi) * 0.5f;
                    Vector2 p = herds.AnimalPosition(h, m);
                    Assert.Less(Vector2.Distance(new Vector2(mid.x, mid.z), p), 0.3f, "herd " + h + " member " + m + " drawn away from where it stands");
                }
        }
    }
}
