using System.Collections.Generic;
using System.Text;
using Drift.Core;
using Drift.Islands;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class SpeciesPoolTests
    {
        readonly List<GameObject> _objects = new();

        [SetUp]
        public void SetUp()
        {
            SpeciesPool.Clear();
            LifeLod.DistanceProvider = _ => 0f;
            LifeEnvironment.NightProvider = () => 0f;
        }

        [TearDown]
        public void TearDown()
        {
            SpeciesPool.Clear();
            LifeLod.DistanceProvider = null;
            LifeEnvironment.NightProvider = null;
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        static ulong Bit(LifeKind k) => SpeciesPool.Bit(k);

        static int Bits(ulong m)
        {
            int n = 0;
            while (m != 0) { m &= m - 1; n++; }
            return n;
        }

        static ulong HerdMask
        {
            get
            {
                ulong m = 0;
                foreach (var k in SpeciesPool.HerdKinds) m |= Bit(k);
                return m;
            }
        }

        static ulong CritterMask
        {
            get
            {
                ulong m = 0;
                foreach (var k in SpeciesPool.CritterKinds) m |= Bit(k);
                return m;
            }
        }

        GameObject MakeObject(float radius, int seed, LifeBiome biome, float beach, bool start, bool critters)
        {
            var go = new GameObject("PoolIsle");
            go.SetActive(false);
            var surface = go.AddComponent<FakeIslandSurface>();
            surface.radius = radius;
            surface.beach = beach;
            surface.biome = (int)biome;
            var life = go.AddComponent<IslandLifeSystem>();
            life.seed = seed;
            var herds = go.AddComponent<IslandHerdSystem>();
            herds.seed = seed;
            herds.StartIsland = start;
            if (critters) go.AddComponent<IslandCrittersSystem>().seed = seed;
            go.SetActive(true);
            _objects.Add(go);
            return go;
        }

        // ------------------------------------------------------------ the draw

        [Test]
        public void Choose_IsStable_AndGivesEveryBiomeASpeciesAndOneOrTwoCritters()
        {
            var rnd = new System.Random(5);
            for (int t = 0; t < 400; t++)
            {
                int seed = rnd.Next(1, 999999);
                ulong found = (ulong)rnd.Next() << 8 & SpeciesPool.AllMask;
                ulong a = SpeciesPool.Choose(seed, found, out var startA);
                ulong b = SpeciesPool.Choose(seed, found, out var startB);
                Assert.AreEqual(a, b, "same seed and album, same pool");
                Assert.AreEqual(startA, startB);
                Assert.IsTrue(startA == LifeKind.Hare || startA == LifeKind.Sheep, "the start island shows one of the most common species");
                Assert.AreEqual(Rarity.Common, SpeciesPool.RarityOf(startA));
                Assert.AreNotEqual(0UL, a & Bit(startA));
                int herds = Bits(a & HerdMask), critters = Bits(a & CritterMask);
                Assert.That(herds, Is.InRange(4, 6), "four to six herd species per run");
                Assert.That(critters, Is.InRange(1, 2), "one or two critter kinds per run");
                for (int biome = 0; biome < 4; biome++)
                {
                    ulong own = 0;
                    foreach (var k in Biomes.Animals((LifeBiome)biome)) own |= Bit(k);
                    Assert.AreNotEqual(0UL, a & own, "biome " + biome + " has a herd species in the pool");
                }
            }
        }

        [Test]
        public void Choose_Tiers_AreClearlyDifferent_AndMissingSpeciesAreFavoured()
        {
            int runs = 4000;
            var fresh = new int[64];
            var nearlyDone = new int[64];
            ulong allButGiraffe = SpeciesPool.AllMask & ~Bit(LifeKind.Giraffe);
            for (int s = 0; s < runs; s++)
            {
                ulong a = SpeciesPool.Choose(s * 7 + 1, 0, out _);
                ulong b = SpeciesPool.Choose(s * 7 + 1, allButGiraffe, out _);
                for (int k = 0; k < 64; k++)
                {
                    if ((a & (1UL << k)) != 0) fresh[k]++;
                    if ((b & (1UL << k)) != 0) nearlyDone[k]++;
                }
            }
            float common = fresh[(int)LifeKind.Penguin] / (float)runs, occasional = fresh[(int)LifeKind.Reindeer] / (float)runs, rare = fresh[(int)LifeKind.ArcticFox] / (float)runs;
            Assert.Greater(common, 0.6f, "a common species is in most runs");
            Assert.That(occasional, Is.InRange(0.15f, 0.4f), "an occasional one in some");
            Assert.Less(rare, 0.1f, "a rare one only now and then");
            Assert.Greater(fresh[(int)LifeKind.Crab], fresh[(int)LifeKind.Firefly] * 4, "crabs are common, fireflies rare");
            Assert.Greater(nearlyDone[(int)LifeKind.Giraffe], fresh[(int)LifeKind.Giraffe] * 4, "the last missing species of an almost full album is strongly favoured");
        }

        // ------------------------------------------------------------ album simulation

        public struct AlbumStats
        {
            public float median, p10, p25, p75, p90, mean, herdsPerRun, crittersPerRun, raresPerRun;
            public float[] curve;
            public float[] firstRunShare;
        }

        // Plays `players` simulated players through runs until each album (13 herd species + 4 critters) is complete.
        // A run: the pool drawn from a fresh seed and the album so far; a world of 20 islands (the start island
        // temperate, 19 with Island.BiomeForSeed); every pool species whose biome exists in the world is met by the
        // time everything is merged into the Pangaea.
        public static AlbumStats SimulateAlbums(int players, int seed = 1, int curveRuns = 14)
        {
            var rnd = new System.Random(seed);
            var done = new List<int>(players);
            var curve = new float[curveRuns];
            var firstRun = new float[64];
            ulong all = SpeciesPool.AllMask;
            int total = Bits(all);
            long herdSum = 0, critterSum = 0, rareSum = 0, runSum = 0;
            for (int p = 0; p < players; p++)
            {
                ulong found = 0;
                int run = 0;
                while (found != all && run < 100)
                {
                    run++;
                    ulong pool = SpeciesPool.Choose(rnd.Next(1, 999999), found, out _);
                    int biomes = 1;
                    for (int i = 0; i < 19; i++) biomes |= 1 << (int)Island.BiomeForSeed(rnd.Next());
                    ulong met = 0;
                    foreach (var k in SpeciesPool.HerdKinds)
                        if ((pool & Bit(k)) != 0 && (biomes & (1 << IslandHerdSystem.HomeBiomeOf(k))) != 0) met |= Bit(k);
                    met |= pool & CritterMask;
                    if (run == 1)
                        for (int k = 0; k < 64; k++) if ((pool & (1UL << k)) != 0) firstRun[k] += 1f / players;
                    herdSum += Bits(pool & HerdMask);
                    critterSum += Bits(pool & CritterMask);
                    foreach (var k in SpeciesPool.HerdKinds) if ((pool & Bit(k)) != 0 && SpeciesPool.RarityOf(k) == Rarity.Rare) rareSum++;
                    runSum++;
                    found |= met;
                    if (run <= curveRuns) curve[run - 1] += Bits(found) / (float)total / players;
                }
                for (int r = run; r < curveRuns; r++) curve[r] += 1f / players;
                done.Add(run);
            }
            done.Sort();
            float Q(float q) => done[Mathf.Clamp(Mathf.FloorToInt(q * done.Count), 0, done.Count - 1)];
            float mean = 0f;
            foreach (int d in done) mean += d;
            return new AlbumStats
            {
                median = Q(0.5f), p10 = Q(0.1f), p25 = Q(0.25f), p75 = Q(0.75f), p90 = Q(0.9f), mean = mean / done.Count,
                herdsPerRun = herdSum / (float)runSum, crittersPerRun = critterSum / (float)runSum, raresPerRun = rareSum / (float)runSum,
                curve = curve, firstRunShare = firstRun,
            };
        }

        public static string Report(int players = 3000)
        {
            var s = SimulateAlbums(players);
            var sb = new StringBuilder();
            sb.AppendFormat("{0} players: album complete after median {1} runs (p10 {2}, p25 {3}, p75 {4}, p90 {5}, mean {6:F1})\n",
                players, s.median, s.p10, s.p25, s.p75, s.p90, s.mean);
            sb.AppendFormat("per run: {0:F2} herd species, {1:F2} critter kinds, {2:F2} rare herd species\n", s.herdsPerRun, s.crittersPerRun, s.raresPerRun);
            sb.Append("album share after run 1..").Append(s.curve.Length).Append(':');
            foreach (float c in s.curve) sb.Append(' ').Append(Mathf.RoundToInt(c * 100f)).Append('%');
            sb.Append("\nfirst-run pool share:");
            foreach (var k in SpeciesPool.HerdKinds) sb.Append(' ').Append(k).Append(' ').Append(Mathf.RoundToInt(s.firstRunShare[(int)k] * 100f)).Append('%');
            foreach (var k in SpeciesPool.CritterKinds) sb.Append(' ').Append(k).Append(' ').Append(Mathf.RoundToInt(s.firstRunShare[(int)k] * 100f)).Append('%');
            return sb.ToString();
        }

        [Test]
        public void Simulation_AlbumFillsOverEightToTenRuns_WithFourToSixHerdSpeciesARun()
        {
            var s = SimulateAlbums(1500);
            Debug.Log(Report(1500));
            Assert.That(s.herdsPerRun, Is.InRange(4.3f, 5.5f));
            Assert.That(s.median, Is.InRange(7f, 11f), "median runs to a full album");
            Assert.LessOrEqual(s.p90, 18f, "even an unlucky player gets there");
            Assert.Less(s.curve[0], 0.45f, "one run shows well under half of the animals");
            Assert.Greater(s.curve[7], 0.9f, "after eight runs the album is nearly full");
        }

        // ------------------------------------------------------------ in the world

        [Test]
        public void PooledIslands_SpawnOnlyPoolSpecies_AndSmallIslandsStillGetAHerd()
        {
            ulong pool = Bit(LifeKind.Hare) | Bit(LifeKind.Flamingo) | Bit(LifeKind.Tortoise) | Bit(LifeKind.ArcticFox) | Bit(LifeKind.Giraffe) | Bit(LifeKind.Crab);
            SpeciesPool.Set(pool, LifeKind.Hare);
            for (int b = 0; b < 4; b++)
            {
                for (int seed = 1; seed <= 4; seed++)
                {
                    var herds = MakeObject(7f, 300 + seed * 11 + b, (LifeBiome)b, 1.5f, false, false).GetComponent<IslandHerdSystem>();
                    Assert.Greater(herds.HerdCount, 0, "biome " + b + " grows herds");
                    Assert.AreEqual(0UL, herds.SpeciesPresent & ~pool, "biome " + b + ": only pool species");
                }
            }
            // A small nordic island (area ~20) below the arctic fox's 25: the run's only nordic species still comes.
            var small = MakeObject(2.5f, 77, LifeBiome.Nordic, 0.8f, false, false).GetComponent<IslandHerdSystem>();
            Assert.Greater(small.HerdCount, 0, "a small island is not left empty because the pool drew a big species");
            Assert.IsTrue(small.HasSpecies(LifeKind.ArcticFox));
        }

        [Test]
        public void NoPool_KeepsTheOldSpeciesMix()
        {
            var herds = MakeObject(9f, 42, LifeBiome.Temperate, 1.5f, false, false).GetComponent<IslandHerdSystem>();
            Assert.GreaterOrEqual(Bits(herds.SpeciesPresent), 2, "without a pool every temperate species can come");
        }

        [Test]
        public void StartIsland_AlwaysHasAHerdOfTheStartSpecies()
        {
            foreach (var start in SpeciesPool.StartKinds)
            {
                SpeciesPool.Set(Bit(start) | Bit(LifeKind.Goat) | Bit(LifeKind.Ox) | Bit(LifeKind.Capybara) | Bit(LifeKind.Penguin) | Bit(LifeKind.Zebra) | Bit(LifeKind.Crab), start);
                for (int seed = 1; seed <= 12; seed++)
                {
                    // The start island's size (area ~24).
                    var herds = MakeObject(2.75f, seed * 97, LifeBiome.Temperate, 0.8f, true, false).GetComponent<IslandHerdSystem>();
                    Assert.Greater(herds.HerdCount, 0);
                    Assert.AreEqual(start, herds.HerdKind(0), "seed " + seed + ": the first herd is the start species");
                }
            }
        }

        // Owner 2026-09-24: "Auf der Anfangsinsel soll erstmal nur eine Herde Tiere sein." More come with the first merge.
        [Test]
        public void StartIsland_HasExactlyOneHerd_UntilItsFirstMerge()
        {
            SpeciesPool.Set(Bit(LifeKind.Sheep) | Bit(LifeKind.Hare) | Bit(LifeKind.Goat) | Bit(LifeKind.Capybara) | Bit(LifeKind.Penguin) | Bit(LifeKind.Zebra), LifeKind.Sheep);
            for (int seed = 1; seed <= 6; seed++)
            {
                var go = MakeObject(2.75f, seed * 131, LifeBiome.Temperate, 0.8f, true, false);
                var herds = go.GetComponent<IslandHerdSystem>();
                var plain = MakeObject(2.75f, seed * 131, LifeBiome.Temperate, 0.8f, false, false).GetComponent<IslandHerdSystem>();
                Assert.Greater(plain.HerdCount, 1, "an ordinary island of the start size holds more than one herd");
                Assert.AreEqual(1, herds.HerdCount, "seed " + seed);
                Assert.AreEqual(LifeKind.Sheep, herds.HerdKind(0));
                for (int i = 0; i < 1200; i++) herds.Step(0.05f);
                Assert.AreEqual(1, herds.HerdCount, "seed " + seed + ": still one herd after a minute");

                // Growing without a merge (it never does, but the cap must not hinge on it) keeps the one herd.
                var surface = go.GetComponent<FakeIslandSurface>();
                surface.radius = 3.2f;
                surface.version++;
                for (int i = 0; i < 20; i++) herds.Step(0.05f);
                Assert.AreEqual(1, herds.HerdCount);

                // The first merge lifts the cap: the guest's herds come along, and the bigger land brings more.
                var guest = MakeObject(2.5f, seed * 17 + 3, LifeBiome.Temperate, 0.8f, false, false).GetComponent<IslandHerdSystem>();
                int guestHerds = guest.HerdCount;
                herds.AbsorbFrom(guest);
                surface.radius = 5f;
                surface.version++;
                for (int i = 0; i < 20; i++) herds.Step(0.05f);
                Assert.Greater(herds.HerdCount, 1 + guestHerds, "seed " + seed + ": new land after the merge brings new herds");
            }

            SpeciesPool.Clear();
            var noPool = MakeObject(2.75f, 5, LifeBiome.Temperate, 0.8f, true, false).GetComponent<IslandHerdSystem>();
            Assert.AreEqual(1, noPool.HerdCount, "without a pool (adventure) the start island has one herd too");
        }

        [Test]
        public void Critters_OnlyThePoolsKindsLive()
        {
            SpeciesPool.Set(Bit(LifeKind.Hare) | Bit(LifeKind.Turtle), LifeKind.Hare);
            var c = MakeObject(8f, 5, LifeBiome.Temperate, 2.5f, false, true).GetComponent<IslandCrittersSystem>();
            Assert.AreEqual(0, c.DesiredCrabs());
            Assert.AreEqual(0, c.CrabCount, "no crabs in a run without them");
            Assert.AreEqual(0, c.DesiredFireflies());
            Assert.Greater(c.DesiredTurtles(), 0);

            SpeciesPool.Set(Bit(LifeKind.Hare) | Bit(LifeKind.Crab), LifeKind.Hare);
            var d = MakeObject(8f, 6, LifeBiome.Temperate, 2.5f, false, true).GetComponent<IslandCrittersSystem>();
            Assert.Greater(d.CrabCount, 0);
            Assert.AreEqual(0, d.DesiredTurtles());
        }

        // ------------------------------------------------------------ crabs

        [Test]
        public void Crab_IsATallShellOnLegs()
        {
            for (int v = 0; v < LifeMeshes.Variants; v++)
            {
                var t = LifeMeshes.GetTemplate(LifeKind.Crab, v);
                float top = float.MinValue, bottom = float.MaxValue, minX = 0f, maxX = 0f;
                foreach (var p in t.vertices)
                {
                    top = Mathf.Max(top, p.y);
                    bottom = Mathf.Min(bottom, p.y);
                    minX = Mathf.Min(minX, p.x);
                    maxX = Mathf.Max(maxX, p.x);
                }
                Assert.GreaterOrEqual(top, 0.45f, "the shell stands up (the old one was 0.19 high)");
                Assert.LessOrEqual(bottom, 0f, "the legs reach the sand");
                Assert.Greater(maxX - minX, 1.5f, "legs spread wide");
                Assert.LessOrEqual(t.vertices.Length, 80);
            }
        }

        [Test]
        public void Crab_StandsOnTheHighestPointOfItsFootprint()
        {
            SpeciesPool.Set(Bit(LifeKind.Hare) | Bit(LifeKind.Crab), LifeKind.Hare);
            var c = MakeObject(6f, 12, LifeBiome.Temperate, 1.2f, false, true).GetComponent<IslandCrittersSystem>();
            var surf = c.GetComponent<FakeIslandSurface>();
            Assert.Greater(c.CrabCount, 0);
            float size = c.crabScale * 1.2f;
            for (int i = 0; i < c.CritterCount; i++)
            {
                if (c.KindOf(i) != LifeKind.Crab) continue;
                Vector2 p = c.PositionOf(i);
                float y = c.CrabGround(p, size);
                Assert.GreaterOrEqual(y, 0f, "never under the water line");
                for (int k = 0; k < 4; k++)
                {
                    float a = k * Mathf.PI * 0.5f;
                    Vector2 q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * size * 0.5f;
                    Assert.GreaterOrEqual(y + 1e-4f, surf.SampleHeight(q), "the shell's rim stays above the sand");
                }
                Assert.GreaterOrEqual(y, surf.SampleHeight(p));
            }
        }
    }
}
