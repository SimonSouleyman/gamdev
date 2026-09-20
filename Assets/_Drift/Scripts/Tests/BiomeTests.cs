using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class BiomeTests
    {
        readonly List<GameObject> _objects = new();
        float _night, _camDist;

        static readonly LifeKind[] NewAnimals =
        {
            LifeKind.Capybara, LifeKind.Flamingo, LifeKind.Tortoise, LifeKind.Reindeer, LifeKind.Penguin,
            LifeKind.ArcticFox, LifeKind.Zebra, LifeKind.Giraffe, LifeKind.Meerkat
        };

        [SetUp]
        public void SetUp()
        {
            _camDist = 0f;
            _night = 0f;
            LifeLod.DistanceProvider = _ => _camDist;
            LifeEnvironment.NightProvider = () => _night;
            LifeEnvironment.TimeOfDayProvider = null;
            LifeEnvironment.PointOfInterest = null;
            LifeEnvironment.ViewDistanceProvider = null;
            LifeEnvironment.WindProvider = null;
            LifeEnvironment.StormProvider = null;
            IslandLifeSystem.ResetSeasonReference();
        }

        [TearDown]
        public void TearDown()
        {
            LifeLod.DistanceProvider = null;
            LifeEnvironment.NightProvider = null;
            LifeEnvironment.TimeOfDayProvider = null;
            LifeEnvironment.PointOfInterest = null;
            LifeEnvironment.ViewDistanceProvider = null;
            IslandLifeSystem.ResetSeasonReference();
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        IslandHerdSystem Make(float radius, int seed, LifeBiome biome, float beach = 0f, string name = "Isle")
        {
            var go = new GameObject(name);
            go.SetActive(false);
            var surface = go.AddComponent<FakeIslandSurface>();
            surface.radius = radius;
            surface.beach = beach;
            surface.biome = (int)biome;
            var life = go.AddComponent<IslandLifeSystem>();
            life.seed = seed;
            var herds = go.AddComponent<IslandHerdSystem>();
            herds.seed = seed;
            go.SetActive(true);
            _objects.Add(go);
            return herds;
        }

        static IslandLifeSystem Life(IslandHerdSystem h) => h.GetComponent<IslandLifeSystem>();
        static FakeIslandSurface Surface(Component c) => c.GetComponent<FakeIslandSurface>();

        static void Run(IslandHerdSystem herds, float seconds, float dt = 0.05f)
        {
            int n = Mathf.CeilToInt(seconds / dt);
            for (int i = 0; i < n; i++) herds.Step(dt);
        }

        // Steps until done() or the time is up; returns the steps member 0 of herd 0 went through while `act` was on.
        HashSet<int> Watch(IslandHerdSystem herds, AnimalActivity act, float seconds, System.Func<bool> done = null, int herd = 0, int member = 0)
        {
            var seen = new HashSet<int>();
            int n = Mathf.CeilToInt(seconds / 0.05f);
            for (int i = 0; i < n; i++)
            {
                herds.Step(0.05f);
                if (herd < herds.HerdCount && member < herds.HerdSize(herd) && herds.ActivityOf(herd, member) == act) seen.Add(herds.ActivityStep(herd, member));
                if (done != null && done()) break;
            }
            return seen;
        }

        static int Bits(ulong mask)
        {
            int n = 0;
            while (mask != 0) { mask &= mask - 1; n++; }
            return n;
        }

        // ------------------------------------------------------------ biomes

        [Test]
        public void EveryBiome_GrowsAndBreedsOnlyItsOwnSpecies()
        {
            for (int b = 0; b < Biomes.Count; b++)
            {
                var spec = Biomes.Of(b);
                var herds = Make(8f, 900 + b, (LifeBiome)b, 2f, "Biome" + b);
                var life = Life(herds);
                life.Simulate(900f, 10f);
                ulong plants = life.PlantsPresent, animals = herds.SpeciesPresent;
                Assert.AreEqual(0UL, plants & ~spec.mask, spec.biome + ": a plant of another biome grew from the seed");
                Assert.AreEqual(0UL, animals & ~spec.mask, spec.biome + ": an animal of another biome spawned from the seed");
                Assert.GreaterOrEqual(Bits(plants), 3, spec.biome + " plants");
                Assert.GreaterOrEqual(Bits(animals), 1, spec.biome + " animals");
                Assert.AreEqual(0, life.ForeignPlantCount);
                Assert.AreEqual(b, life.Biome);
                Assert.AreEqual(LifeNames.OfBiome(b), herds.BiomeName);
                bool tree = false;
                foreach (var k in spec.plants) if (LifeMeshes.PlantSlot(k) == BiomeSpec.RoleTree && life.CountOfKind(k) > 0) tree = true;
                Assert.IsTrue(tree, spec.biome + " grew no tree of its own");
                Assert.Greater(life.CountOf(LifeKind.Tree), 0, "CountOf(Tree) counts the biome's tree analogues");
            }
        }

        [Test]
        public void Biomes_HaveDistinctSpecies_AndGermanNames()
        {
            Assert.AreEqual("Gemäßigt", LifeNames.OfBiome(LifeBiome.Temperate));
            Assert.AreEqual("Tropisch", LifeNames.OfBiome(LifeBiome.Tropical));
            Assert.AreEqual("Nordisch", LifeNames.OfBiome(LifeBiome.Nordic));
            Assert.AreEqual("Savanne", LifeNames.OfBiome(LifeBiome.Savanna));
            Assert.AreEqual(LifeMeshes.KindCount, LifeNames.KindCount);
            Assert.AreEqual(LifeMeshes.KindCount, System.Enum.GetValues(typeof(LifeKind)).Length);
            foreach (LifeKind k in System.Enum.GetValues(typeof(LifeKind)))
            {
                Assert.IsFalse(string.IsNullOrEmpty(LifeNames.Of(k)), k.ToString());
                Assert.IsFalse(string.IsNullOrEmpty(LifeNames.Plural(k)), k.ToString());
            }
            for (int a = 0; a < Biomes.Count; a++)
            {
                var sa = Biomes.Of(a);
                Assert.AreEqual(3 + (a == 0 ? 1 : 0), sa.animals.Length);
                Assert.AreEqual(sa.animals.Length + sa.plants.Length, Biomes.Collectibles((LifeBiome)a).Length);
                foreach (var k in sa.animals)
                {
                    Assert.IsTrue(IslandHerdSystem.IsHerdSpecies(k), k.ToString());
                    Assert.AreEqual(a, IslandHerdSystem.HomeBiomeOf(k), k.ToString());
                    for (int b = 0; b < Biomes.Count; b++)
                        if (b != a) Assert.IsFalse(Biomes.Of(b).IsNative(k), k + " lives in two biomes");
                }
                for (int b = a + 1; b < Biomes.Count; b++)
                {
                    // Palm and reed are the only plants two biomes share.
                    ulong shared = sa.mask & Biomes.Of(b).mask & ~(1UL << (int)LifeKind.Palm) & ~(1UL << (int)LifeKind.Reed);
                    Assert.AreEqual(0UL, shared, a + "/" + b);
                }
            }
            // Saves store the numeric value: the old kinds must never move.
            Assert.AreEqual(4, (int)LifeKind.Hare);
            Assert.AreEqual(7, (int)LifeKind.Ox);
            Assert.AreEqual(15, (int)LifeKind.Reed);
            Assert.AreEqual(16, (int)LifeKind.Capybara);
            Assert.AreEqual(24, (int)LifeKind.Meerkat);
            Assert.AreEqual(41, (int)LifeKind.Baobab);
            Assert.AreEqual(14, (int)AnimalActivity.Flee);
            Assert.AreEqual("rutscht auf dem Bauch zum Wasser", IslandHerdSystem.MoodText(AnimalState.Walk, AnimalActivity.Slide));
            Assert.AreEqual("steht auf einem Bein", IslandHerdSystem.MoodText(AnimalState.Rest, AnimalActivity.OneLeg));
            for (int i = (int)AnimalActivity.Slide; i <= (int)AnimalActivity.Shake; i++)
                Assert.IsFalse(string.IsNullOrEmpty(IslandHerdSystem.MoodText(AnimalState.Graze, (AnimalActivity)i)), ((AnimalActivity)i).ToString());
        }

        // ------------------------------------------------------------ merges

        [Test]
        public void Merge_KeepsForeignPlantsAlive_AndTheySpreadOnlyFromExistingOnesToNeighbourCells()
        {
            var host = Life(Make(9f, 921, LifeBiome.Temperate, 0f, "Host"));
            var guest = Life(Make(3f, 922, LifeBiome.Savanna, 0f, "Guest"));
            host.Simulate(1200f, 10f);
            guest.Simulate(1200f, 10f);
            Assert.AreEqual(0, host.ForeignPlantCount);
            Assert.AreEqual(0, host.ForeignSpreads, "nothing foreign comes from nothing");
            Assert.Greater(guest.CountOfKind(LifeKind.DryGrass), 0);

            host.AbsorbFrom(guest);
            Surface(host).version++;
            host.Tick(1f);
            int arrived = host.ForeignPlantCount;
            Assert.Greater(arrived, 5);
            Assert.IsTrue(host.HasPlant(LifeKind.DryGrass));
            Assert.IsTrue(host.IsForeign(LifeKind.DryGrass));
            Assert.IsFalse(host.IsForeign(LifeKind.Grass));

            var spec = Biomes.Of(LifeBiome.Temperate);
            float reach = host.cellSize * 2.9f;
            var before = new List<Vector3>();
            for (int round = 0; round < 60; round++)
            {
                before.Clear();
                for (int i = 0; i < host.PlantCount; i++)
                    if (!spec.IsNative(host.PlantKindOf(i))) before.Add(new Vector3(host.PlantPositionOf(i).x, (int)host.PlantKindOf(i), host.PlantPositionOf(i).y));
                host.Tick(60f);
                for (int i = 0; i < host.PlantCount; i++)
                {
                    var kind = host.PlantKindOf(i);
                    if (spec.IsNative(kind) || host.PlantDyingOf(i)) continue;
                    Vector2 p = host.PlantPositionOf(i);
                    float best = float.MaxValue;
                    foreach (var q in before)
                        if ((int)q.y == (int)kind) best = Mathf.Min(best, Vector2.Distance(p, new Vector2(q.x, q.z)));
                    Assert.LessOrEqual(best, reach, kind + " appeared away from every plant of its kind");
                    Assert.Greater(Surface(host).SampleHeight(p), 0.15f, kind + " seeded on unsuitable ground");
                }
            }
            Assert.Greater(host.ForeignSpreads, 0, "the foreign plants seeded at least one neighbour cell");
            Assert.IsTrue(host.HasPlant(LifeKind.DryGrass), "the collected species is still there an hour of life later");
            Assert.LessOrEqual(host.LiveVertexEstimate, host.maxVegetationVerts);
        }

        [Test]
        public void Merge_ForeignHerdsSurviveAndBreedTrue_NewHerdsAreNative()
        {
            var host = Make(8f, 931, LifeBiome.Temperate, 0f, "Host");
            var guest = Make(5f, 932, LifeBiome.Savanna, 0f, "Guest");
            Life(host).Simulate(900f, 10f);
            guest.ClearHerds();
            Assert.GreaterOrEqual(guest.AddHerd(LifeKind.Zebra, new Vector2(1f, 1f), 4), 0);
            int hostHerds = host.HerdCount;
            host.AbsorbFrom(guest);
            Assert.AreEqual(hostHerds + 1, host.HerdCount);
            Assert.IsTrue(host.HasSpecies(LifeKind.Zebra));
            Assert.IsTrue(host.IsForeign(LifeKind.Zebra));
            Assert.IsFalse(host.IsForeign(LifeKind.Hare));

            host.growthInterval = 1f;
            host.growthChance = 1f;
            host.maxAnimals = 400;
            int zebras = host.AnimalsOf(LifeKind.Zebra);
            Run(host, 30f, 0.1f);
            Assert.Greater(host.AnimalsOf(LifeKind.Zebra), zebras, "the zebra herd bore a zebra on a temperate island");
            Assert.AreEqual(1, host.HerdsOf(LifeKind.Zebra));

            // New land brings new herds, and those are the host biome's.
            ulong beforeMask = host.SpeciesPresent;
            Surface(host).radius = 14f;
            Surface(host).version++;
            Run(host, 1f, 0.1f);
            Assert.Greater(host.HerdCount, hostHerds + 1);
            ulong added = host.SpeciesPresent & ~beforeMask;
            Assert.AreEqual(0UL, added & ~Biomes.Of(LifeBiome.Temperate).mask, "a foreign species spawned from nothing");
            Assert.AreEqual(1, host.HerdsOf(LifeKind.Zebra));
        }

        [Test]
        public void Caps_NeverTakeTheLastHerdOfACollectedSpecies()
        {
            var host = Make(10f, 941, LifeBiome.Temperate, 0f, "Host");
            host.ClearHerds();
            for (int i = 0; i < 12; i++) host.AddHerd(LifeKind.Hare, new Vector2(-6f + i, 2f), 9);
            var guest = Make(10f, 942, LifeBiome.Nordic, 0f, "Guest");
            guest.ClearHerds();
            guest.AddHerd(LifeKind.Reindeer, new Vector2(0f, -3f), 8);
            guest.AddHerd(LifeKind.ArcticFox, new Vector2(3f, -3f), 2);
            for (int i = 0; i < 4; i++) guest.AddHerd(LifeKind.Penguin, new Vector2(-4f + 2f * i, -5f), 10);
            host.AbsorbFrom(guest);
            Assert.LessOrEqual(host.AnimalCount, host.maxAnimals);
            Assert.IsTrue(host.HasSpecies(LifeKind.Reindeer));
            Assert.IsTrue(host.HasSpecies(LifeKind.ArcticFox));
            Assert.IsTrue(host.HasSpecies(LifeKind.Penguin));
            Assert.IsTrue(host.HasSpecies(LifeKind.Hare));
        }

        // ------------------------------------- the biome belongs to the land, not to the island

        // A host that swallows a guest lying next to it: the guest's disc becomes new land of the merged island,
        // so its cells keep the guest's biome while everything the host already had stays the host's.
        // Returns the host; `guestAt` is where the guest's centre sits in the host's local space.
        IslandHerdSystem StageMerge(LifeBiome hostBiome, LifeBiome guestBiome, out Vector2 guestAt,
            float hostRadius = 8f, float guestRadius = 5f, float mature = 900f, int seed = 1100)
        {
            // The guest sits just outside the host, so everything it brings is new land for the merged island -
            // an overlapping guest would drop its plants onto ground the host already owns.
            guestAt = new Vector2(hostRadius + guestRadius + 0.6f, 0f);
            float grown = guestAt.x + guestRadius;
            var host = Make(hostRadius, seed, hostBiome, 0f, "MergeHost");
            var guest = Make(guestRadius, seed + 1, guestBiome, 0f, "MergeGuest");
            guest.transform.position = new Vector3(guestAt.x, 0f, guestAt.y);
            if (mature > 0f)
            {
                Life(host).Simulate(mature, 10f);
                Life(guest).Simulate(mature, 10f);
            }
            Life(host).AbsorbFrom(Life(guest));
            host.AbsorbFrom(guest);
            Surface(host).radius = grown;
            Surface(host).version++;
            Life(host).Tick(1f);
            return host;
        }

        [Test]
        public void Merge_AbsorbedLandKeepsItsOwnBiome_AndTheIslandReportsBoth()
        {
            var host = StageMerge(LifeBiome.Temperate, LifeBiome.Savanna, out Vector2 guestAt);
            var life = Life(host);

            Assert.AreEqual((int)LifeBiome.Savanna, life.BiomeAt(guestAt), "the absorbed land turned into the host's biome");
            Assert.AreEqual((int)LifeBiome.Savanna, life.BiomeAt(guestAt + new Vector2(0f, 2f)));
            Assert.AreEqual((int)LifeBiome.Temperate, life.BiomeAt(new Vector2(-5f, 0f)), "the host's own ground changed biome");
            Assert.AreEqual((int)LifeBiome.Temperate, life.BiomeAt(Vector2.zero));

            Assert.AreEqual(LifeBiome.Temperate, life.DominantBiome, "most of the land is the host's");
            Assert.AreEqual((int)LifeBiome.Temperate, life.Biome);
            Assert.AreEqual((int)LifeBiome.Temperate, host.Biome);
            Assert.AreEqual("Gemäßigt", host.BiomeName);
            Assert.AreEqual((1 << (int)LifeBiome.Temperate) | (1 << (int)LifeBiome.Savanna), life.BiomesPresent);
            Assert.AreEqual(life.BiomesPresent, host.BiomesPresent);
            Assert.Greater(life.CellsOfBiome(LifeBiome.Savanna), 20, "the guest brought no land of its own");
            Assert.Greater(life.CellsOfBiome(LifeBiome.Temperate), life.CellsOfBiome(LifeBiome.Savanna));
            Assert.AreEqual(0, life.CellsOfBiome(LifeBiome.Nordic));
            Assert.AreEqual(life.LandCells, life.CellsOfBiome(LifeBiome.Temperate) + life.CellsOfBiome(LifeBiome.Savanna),
                "every land cell carries exactly one biome");

            // Both biomes are at home now, so nothing on this island counts as a guest species any more.
            Assert.IsFalse(life.IsForeign(LifeKind.DryGrass));
            Assert.IsFalse(life.IsForeign(LifeKind.Grass));
            Assert.IsTrue(life.IsForeign(LifeKind.Spruce), "a biome that is not on this island is still foreign");
            Assert.IsFalse(host.IsForeign(LifeKind.Zebra));
            Assert.IsTrue(host.IsForeign(LifeKind.Reindeer));
        }

        [Test]
        public void Merge_SuccessionOnAbsorbedLandGrowsTheGuestsSpecies_AndNeverTheHosts()
        {
            var host = StageMerge(LifeBiome.Temperate, LifeBiome.Savanna, out Vector2 guestAt);
            var life = Life(host);
            var hostSpec = Biomes.Of(LifeBiome.Temperate);
            var guestSpec = Biomes.Of(LifeBiome.Savanna);
            // Palm and reed are the only plants two biomes share; they are shore plants, not succession species.
            ulong shared = (1UL << (int)LifeKind.Palm) | (1UL << (int)LifeKind.Reed);

            for (int round = 0; round < 40; round++) life.Tick(60f);

            int onGuest = 0, onHost = 0;
            for (int i = 0; i < life.PlantCount; i++)
            {
                if (life.PlantDyingOf(i)) continue;
                var kind = life.PlantKindOf(i);
                if ((shared & (1UL << (int)kind)) != 0) continue;
                int biome = life.BiomeAt(life.PlantPositionOf(i));
                if (biome == (int)LifeBiome.Savanna)
                {
                    onGuest++;
                    Assert.IsTrue(guestSpec.IsNative(kind), kind + " grew on savanna ground");
                    Assert.IsFalse(hostSpec.IsNative(kind), kind + ": the host's species took the absorbed land");
                }
                else if (biome == (int)LifeBiome.Temperate)
                {
                    onHost++;
                    Assert.IsTrue(hostSpec.IsNative(kind), kind + " grew on temperate ground");
                }
            }
            Assert.Greater(onGuest, 10, "the absorbed land grew nothing");
            Assert.Greater(onHost, 10);
            Assert.Greater(life.CountOfKind(LifeKind.DryGrass), 0, "savanna ground stopped making savanna grass");
            Assert.Greater(life.CountOfKind(LifeKind.Acacia), 0, "savanna ground stopped making acacias");
            Assert.Greater(life.CountOfKind(LifeKind.Tree), 0, "temperate ground stopped making temperate trees");
            Assert.LessOrEqual(life.LiveVertexEstimate, life.maxVegetationVerts);
        }

        [Test]
        public void Merge_NewHerdsComeFromTheBiomeOfTheGroundTheyAreFoundedOn()
        {
            var host = StageMerge(LifeBiome.Temperate, LifeBiome.Savanna, out Vector2 guestAt, 8f, 5f, 300f, 1110);
            var life = Life(host);
            bool sawGuestBiome = false, sawHostBiome = false;
            int founded = 0;
            for (int round = 0; round < 6; round++)
            {
                Surface(host).version++;
                host.Repopulate();
                for (int h = 0; h < host.HerdCount; h++)
                {
                    var kind = host.HerdKind(h);
                    int home = IslandHerdSystem.HomeBiomeOf(kind);
                    Assert.AreEqual(life.BiomeAt(host.HerdCenter(h)), home,
                        kind + " was founded on ground of another biome");
                    founded++;
                    if (home == (int)LifeBiome.Savanna) sawGuestBiome = true;
                    if (home == (int)LifeBiome.Temperate) sawHostBiome = true;
                }
                Assert.LessOrEqual(host.HerdCount, host.maxHerds);
                Assert.LessOrEqual(host.AnimalCount, host.maxAnimals);
            }
            Assert.Greater(founded, 10);
            Assert.IsTrue(sawGuestBiome, "the savanna half never produced a savanna herd");
            Assert.IsTrue(sawHostBiome);
        }

        [Test]
        public void Merge_TheTwoHalvesAreTintedDifferently_AndTheBorderIsBlended()
        {
            var host = StageMerge(LifeBiome.Temperate, LifeBiome.Savanna, out Vector2 guestAt);
            var life = Life(host);
            life.Simulate(600f, 20f);

            Color guestGround = life.GroundTintAt(guestAt);
            Color hostGround = life.GroundTintAt(new Vector2(-5f, 0f));
            float apart = Mathf.Abs(guestGround.r - hostGround.r) + Mathf.Abs(guestGround.g - hostGround.g) + Mathf.Abs(guestGround.b - hostGround.b);
            Assert.Greater(apart, 0.4f, "the absorbed half is tinted like the host's");

            // Walking across the border, no single step may jump by the whole difference: the blend spreads it.
            float step = life.cellSize * 0.5f;
            float biggest = 0f;
            Color prev = life.GroundTintAt(new Vector2(-5f, 0f));
            for (float x = -5f + step; x <= guestAt.x; x += step)
            {
                Color c = life.GroundTintAt(new Vector2(x, 0f));
                biggest = Mathf.Max(biggest, Mathf.Abs(c.r - prev.r) + Mathf.Abs(c.g - prev.g) + Mathf.Abs(c.b - prev.b));
                prev = c;
            }
            Assert.Less(biggest, apart * 0.5f, "the biome border is a hard edge");
        }

        [Test]
        public void Merge_PerCellBiomeSurvivesASaveAndOldFilesLoadUniform()
        {
            var host = StageMerge(LifeBiome.Tropical, LifeBiome.Nordic, out Vector2 guestAt, 8f, 5f, 600f, 1120);
            var life = Life(host);
            Assert.AreEqual((int)LifeBiome.Nordic, life.BiomeAt(guestAt));

            var captured = life.Capture();
            Assert.IsNotNull(captured.biomeRuns);
            Assert.Less(captured.biomeRuns.Length, captured.stage.Length / 4, "the run-length encoding did not compress");
            string json = JsonUtility.ToJson(captured);
            var data = JsonUtility.FromJson<LifeSaveData>(json);

            float grown = Surface(host).radius;
            var copy = Make(grown, 1120, LifeBiome.Tropical, 0f, "Copy");
            Life(copy).Restore(data);
            Assert.AreEqual(life.BiomesPresent, Life(copy).BiomesPresent);
            Assert.AreEqual(life.DominantBiome, Life(copy).DominantBiome);
            Assert.AreEqual(life.CellsOfBiome(LifeBiome.Nordic), Life(copy).CellsOfBiome(LifeBiome.Nordic));
            Assert.AreEqual(life.CellsOfBiome(LifeBiome.Tropical), Life(copy).CellsOfBiome(LifeBiome.Tropical));
            Assert.AreEqual((int)LifeBiome.Nordic, Life(copy).BiomeAt(guestAt));
            Assert.AreEqual((int)LifeBiome.Tropical, Life(copy).BiomeAt(new Vector2(-5f, 0f)));

            // A file from before the per-cell biome: every cell is the island's own seed biome, as it used to be.
            int at = json.IndexOf(",\"biomeRuns\":", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(at, 0);
            string older = json.Remove(at, json.IndexOf(']', at) - at + 1);
            Assert.IsFalse(older.Contains("biomeRuns"));
            var old = Make(grown, 1120, LifeBiome.Tropical, 0f, "Old");
            Life(old).Restore(JsonUtility.FromJson<LifeSaveData>(older));
            Assert.AreEqual(1 << (int)LifeBiome.Tropical, Life(old).BiomesPresent);
            Assert.AreEqual(LifeBiome.Tropical, Life(old).DominantBiome);
            Assert.AreEqual(0, Life(old).CellsOfBiome(LifeBiome.Nordic));
            Assert.AreEqual((int)LifeBiome.Tropical, Life(old).BiomeAt(guestAt));
        }

        [Test]
        public void MergedIsland_StepsAndTintsWithoutAllocating()
        {
            var host = StageMerge(LifeBiome.Temperate, LifeBiome.Nordic, out Vector2 guestAt, 8f, 5f, 600f, 1130);
            var life = Life(host);
            Run(host, 10f);
            ulong sink = 0;
            for (int warm = 0; warm < 2; warm++)
            {
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++)
                {
                    sink += (ulong)life.BiomeAt(guestAt) + (ulong)life.BiomeAt(Vector2.zero);
                    sink += (ulong)(int)life.BiomeAtCell(3, 3) + (ulong)life.BiomesPresent + (ulong)life.CellsOfBiome(LifeBiome.Nordic);
                    if (life.IsForeign(LifeKind.Acacia) && host.IsForeign(LifeKind.Zebra)) sink++;
                }
                long after = System.GC.GetAllocatedBytesForCurrentThread();
                if (warm == 1) Assert.AreEqual(0L, after - before, "the per-cell biome accessors allocate");
            }
            Assert.AreNotEqual(0UL, sink);
            long b0 = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) { host.Step(0.05f); life.Step(0.05f); }
            long b1 = System.GC.GetAllocatedBytesForCurrentThread();
            Assert.LessOrEqual(b1 - b0, 1024L, "a merged island allocates per frame");
        }

        // ------------------------------------------------------------ templates

        [Test]
        public void NewAnimals_HaveSimpleAndDetailTemplatesWithEveryAnimationChannel()
        {
            foreach (var kind in NewAnimals)
                for (int v = 0; v < LifeMeshes.Variants; v++)
                {
                    var simple = LifeMeshes.GetTemplate(kind, v);
                    Assert.GreaterOrEqual(simple.vertices.Length, 40, kind + " simple");
                    Assert.LessOrEqual(simple.vertices.Length, 140, kind + " simple");
                    Assert.IsNull(simple.lever, kind + ": simple templates animate with lever = z, up = y");
                    foreach (bool young in new[] { false, true })
                        for (int pose = 0; pose < 2; pose++)
                        {
                            var t = LifeMeshes.GetDetailTemplate(kind, v, young, pose);
                            string id = kind + "/" + v + (young ? "/young" : "") + "/pose" + pose;
                            int n = t.vertices.Length;
                            Assert.GreaterOrEqual(n, 180, id);
                            Assert.LessOrEqual(n, 420, id);
                            Assert.Greater(n, simple.vertices.Length * 2, id + " is not more detailed than the simple template");
                            Assert.AreEqual(n, t.lever.Length, id);
                            Assert.AreEqual(n, t.up.Length, id);
                            Assert.AreEqual(n, t.leg.Length, id);
                            Assert.AreEqual(n, t.normals.Length, id);
                            Assert.AreEqual(n, t.colors.Length, id);
                            Assert.AreEqual(0, t.triangles.Length % 3, id);
                            float maxUp = 0f, minY = float.MaxValue;
                            for (int i = 0; i < n; i++)
                            {
                                maxUp = Mathf.Max(maxUp, t.up[i]);
                                minY = Mathf.Min(minY, t.vertices[i].y);
                                Assert.IsFalse(float.IsNaN(t.lever[i]) || float.IsNaN(t.up[i]) || float.IsNaN(t.leg[i]), id);
                                if (pose == 0 && t.vertices[i].y < 1e-4f && t.vertices[i].y > -1e-4f && t.leg[i] != 0f)
                                {
                                    Assert.AreEqual(0f, t.up[i], 1e-4f, id + ": a foot stays on the ground when the animal lies down");
                                    Assert.AreEqual(0f, t.lever[i], 1e-4f, id + ": a foot stays planted when the head dips");
                                }
                            }
                            Assert.GreaterOrEqual(minY, -0.02f, id + " reaches below the ground");
                            if (pose == 0)
                            {
                                float maxLever = 0f, maxLeg = 0f;
                                for (int i = 0; i < n; i++) { maxLever = Mathf.Max(maxLever, t.lever[i]); maxLeg = Mathf.Max(maxLeg, Mathf.Abs(t.leg[i])); }
                                Assert.Greater(maxLever, 0.2f, id + ": the head needs a pitch lever");
                                Assert.Greater(maxLeg, 0f, id + ": walking legs need a step lift");
                                Assert.Greater(maxUp, 0f, id);
                            }
                        }
                    bool special = AnimalModels.HasSpecialPose(kind);
                    var d0 = LifeMeshes.GetDetailTemplate(kind, v, false, 0);
                    var d1 = LifeMeshes.GetDetailTemplate(kind, v, false, 1);
                    Assert.AreEqual(special, !ReferenceEquals(d0, d1), kind + " special pose");
                    Assert.AreSame(d0, LifeMeshes.GetDetailTemplate(kind, v, false), kind.ToString());
                }
            float giraffe = 0f, penguinSlide = 0f;
            foreach (var p in LifeMeshes.GetDetailTemplate(LifeKind.Giraffe, 0, false).vertices) giraffe = Mathf.Max(giraffe, p.y);
            foreach (var p in LifeMeshes.GetDetailTemplate(LifeKind.Penguin, 0, false, 1).vertices) penguinSlide = Mathf.Max(penguinSlide, p.y);
            Assert.Greater(giraffe, 1.5f, "a giraffe towers over everything");
            Assert.Less(penguinSlide, 0.4f, "a sliding penguin lies flat");
        }

        [Test]
        public void NewPlants_AreCheap_AndFillASuccessionRole()
        {
            for (int b = 1; b < Biomes.Count; b++)
                foreach (var kind in Biomes.Of(b).plants)
                {
                    int role = LifeMeshes.PlantSlot(kind);
                    Assert.IsTrue(role >= 0 && role < LifeMeshes.PlantKinds, kind.ToString());
                    Assert.IsTrue(LifeMeshes.IsPlant(kind));
                    int budget = role == BiomeSpec.RoleGround ? 30 : role == BiomeSpec.RoleFlower ? 48 : role == BiomeSpec.RoleShrub ? 60 : 125;
                    for (int v = 0; v < LifeMeshes.Variants; v++)
                    {
                        var t = LifeMeshes.GetTemplate(kind, v);
                        Assert.LessOrEqual(t.vertices.Length, budget, kind + "/" + v);
                        Assert.Greater(t.vertices.Length, 8, kind + "/" + v);
                        Assert.AreEqual(t.vertices.Length, t.sway.Length);
                    }
                }
            foreach (var w in LifeMeshes.GetTemplate(LifeKind.TermiteMound, 0).sway) Assert.AreEqual(0f, w, "a termite mound does not sway");
            Assert.AreEqual((int)LifeKind.Tree, LifeMeshes.PlantSlot(LifeKind.Acacia));
            Assert.AreEqual((int)LifeKind.Grass, LifeMeshes.PlantSlot(LifeKind.Fern));
        }

        // ------------------------------------------------------------ choreographies

        IslandHerdSystem Stage(LifeBiome biome, LifeKind kind, int size, Vector2 at, float radius = 8f, float beach = 2.5f, bool mature = false, int seed = 950)
        {
            var herds = Make(radius, seed, biome, beach, kind.ToString());
            if (mature) Life(herds).Simulate(900f, 10f);
            herds.ClearHerds();
            Assert.GreaterOrEqual(herds.AddHerd(kind, at, size), 0, "no ground for " + kind);
            herds.behaviourRate = 0f;
            Run(herds, 0.5f);
            herds.behaviourRate = 1f;
            return herds;
        }

        void AssertStartleCancels(IslandHerdSystem herds, AnimalActivity act)
        {
            Assert.IsTrue(herds.StartChoreography(0, act), act + " restarts");
            Watch(herds, act, 40f, () => herds.CountActivity(act) > 0);
            Assert.Greater(herds.CountActivity(act), 0, act + " is under way");
            herds.Startle(new Vector2(20f, 0f));
            Run(herds, 0.2f);
            Assert.AreEqual(0, herds.CountActivity(act), act + " is cancelled by a startle");
            for (int m = 0; m < herds.HerdSize(0); m++)
            {
                Assert.IsFalse(herds.IsDived(0, m));
                Assert.AreEqual(0f, herds.AnimalBakedPitch(0, m));
            }
            Assert.AreEqual(1, herds.FleeingHerds);
        }

        [Test]
        public void Penguins_SlideOnTheirBelliesIntoTheWater_DiveAndShake()
        {
            var herds = Stage(LifeBiome.Nordic, LifeKind.Penguin, 5, new Vector2(4.6f, 0f));
            Assert.IsTrue(herds.StartChoreography(0, AnimalActivity.Slide));
            bool dived = false, flat = false;
            float lowest = float.MaxValue;
            var seen = new HashSet<int>();
            for (int i = 0; i < 1400; i++)
            {
                herds.Step(0.05f);
                if (herds.ActivityOf(0, 0) == AnimalActivity.Slide)
                {
                    seen.Add(herds.ActivityStep(0, 0));
                    if (herds.ActivityStep(0, 0) == IslandHerdSystem.SlideGlide) { flat |= herds.AnimalPose(0, 0) == 1; lowest = Mathf.Min(lowest, Surface(herds).SampleHeight(herds.AnimalPosition(0, 0))); }
                }
                dived |= herds.IsDived(0, 0);
                if (seen.Count >= 6 && herds.ActivityOf(0, 0) == AnimalActivity.None) break;
            }
            for (int s = IslandHerdSystem.SlideQueue; s <= IslandHerdSystem.SlideShake; s++) Assert.IsTrue(seen.Contains(s), "slide step " + s + " never happened");
            Assert.IsTrue(dived, "the penguin dived");
            Assert.IsTrue(flat, "the penguin lay on its belly while sliding");
            Assert.Less(lowest, 0.1f, "the slide ends at the water");
            Assert.AreEqual(5, herds.HerdSize(0), "nobody drowns");
            Assert.Greater(herds.Slides, 0);
            Assert.AreEqual("rutscht auf dem Bauch zum Wasser", IslandHerdSystem.MoodText(AnimalState.Look, AnimalActivity.Slide));
            herds.behaviourRate = 0f;
            Run(herds, 60f, 0.1f);
            Assert.AreEqual(0, herds.CountActivity(AnimalActivity.Slide));
            herds.behaviourRate = 1f;
            AssertStartleCancels(herds, AnimalActivity.Slide);
        }

        [Test]
        public void Flamingos_ParadeInStep_ThenStandOnOneLeg()
        {
            var herds = Stage(LifeBiome.Tropical, LifeKind.Flamingo, 6, new Vector2(4.6f, 0f));
            Assert.IsTrue(herds.StartChoreography(0, AnimalActivity.Parade));
            var seen = new HashSet<int>();
            bool synced = false, marched = false, oneLeg = false;
            Vector2 flagPos = default;
            for (int i = 0; i < 2000; i++)
            {
                herds.Step(0.05f);
                var act = herds.ActivityOf(0, 0);
                if (act != AnimalActivity.Parade && act != AnimalActivity.OneLeg) { if (seen.Count >= 3) break; continue; }
                int step = act == AnimalActivity.OneLeg ? IslandHerdSystem.ParadeOneLeg : herds.ActivityStep(0, 0);
                seen.Add(step);
                if (step == IslandHerdSystem.ParadeFlag)
                {
                    flagPos = herds.AnimalPosition(0, 0);
                    int same = 0, row = 0;
                    for (int m = 0; m < herds.HerdSize(0); m++)
                        if (herds.ActivityOf(0, m) == AnimalActivity.Parade) { row++; if (Mathf.Abs(Mathf.DeltaAngle(herds.AnimalYaw(0, m), herds.AnimalYaw(0, 0))) < 0.5f) same++; }
                    if (row >= 3 && same == row) synced = true;
                }
                if (step == IslandHerdSystem.ParadeMarch && Vector2.Distance(herds.AnimalPosition(0, 0), flagPos) > 0.2f) marched = true;
                if (act == AnimalActivity.OneLeg && herds.AnimalPose(0, 0) == 1) oneLeg = true;
            }
            for (int s = IslandHerdSystem.ParadeGather; s <= IslandHerdSystem.ParadeOneLeg; s++) Assert.IsTrue(seen.Contains(s), "parade step " + s + " never happened");
            Assert.IsTrue(synced, "the row turns its heads together");
            Assert.IsTrue(marched, "the row marches along the water");
            Assert.IsTrue(oneLeg, "the parade ends on one leg");
            Assert.AreEqual(6, herds.HerdSize(0));
            AssertStartleCancels(herds, AnimalActivity.Parade);
        }

        [Test]
        public void Flamingos_RestStandingOnOneLeg()
        {
            var herds = Stage(LifeBiome.Tropical, LifeKind.Flamingo, 6, new Vector2(2f, 0f));
            _night = 1f;
            Run(herds, 60f, 0.1f);
            Assert.Greater(herds.SleepingCount, 0);
            int standing = 0;
            for (int m = 0; m < herds.HerdSize(0); m++)
                if (herds.AnimalStateOf(0, m) == AnimalState.Sleep)
                {
                    Assert.AreEqual(1, herds.AnimalPose(0, m));
                    Assert.AreEqual(AnimalActivity.OneLeg, herds.ActivityOf(0, m));
                    standing++;
                }
            Assert.Greater(standing, 0);
        }

        [Test]
        public void Giraffes_BrowseTheTreeTops()
        {
            var herds = Stage(LifeBiome.Savanna, LifeKind.Giraffe, 4, Vector2.zero, 9f, 0f, true);
            Assert.Greater(Life(herds).CountOf(LifeKind.Tree), 0);
            Assert.IsTrue(herds.StartChoreography(0, AnimalActivity.Browse));
            var seen = Watch(herds, AnimalActivity.Browse, 90f, null);
            Assert.IsTrue(seen.Contains(IslandHerdSystem.BrowseStretch), "stretch");
            Assert.IsTrue(seen.Contains(IslandHerdSystem.BrowseChew), "chew");
            Assert.IsTrue(seen.Contains(IslandHerdSystem.BrowseSway), "sway");
            herds.behaviourRate = 0f;
            Run(herds, 90f, 0.1f);
            Assert.AreEqual(0, herds.CountActivity(AnimalActivity.Browse), "the herd moves on");
            herds.behaviourRate = 1f;
            AssertStartleCancels(herds, AnimalActivity.Browse);
        }

        [Test]
        public void ZebrasAndReindeer_StampedeInAnArc_AndSettleWithHeadsUp()
        {
            foreach (var kind in new[] { LifeKind.Zebra, LifeKind.Reindeer })
            {
                var herds = Stage(kind == LifeKind.Zebra ? LifeBiome.Savanna : LifeBiome.Nordic, kind, 6, Vector2.zero, 10f, 0f);
                Assert.IsTrue(herds.StartChoreography(0, AnimalActivity.Stampede), kind.ToString());
                Assert.AreEqual(1, herds.Stampedes);
                var seen = new HashSet<int>();
                float path = 0f, farthest = 0f;
                Vector2 last = herds.HerdCenter(0), start = last;
                for (int i = 0; i < 1600 && herds.CountActivity(AnimalActivity.Stampede) > 0; i++)
                {
                    herds.Step(0.05f);
                    seen.Add(herds.ActivityStep(0, 0));
                    path += Vector2.Distance(herds.HerdCenter(0), last);
                    last = herds.HerdCenter(0);
                    farthest = Mathf.Max(farthest, Vector2.Distance(last, start));
                }
                Assert.IsTrue(seen.Contains(IslandHerdSystem.StampedeGallop) && seen.Contains(IslandHerdSystem.StampedeHeadsUp), kind + " steps");
                Assert.Greater(path, 8f, kind + " gallops a wide arc");
                Assert.Greater(farthest, 3f, kind.ToString());
                Assert.Less(Vector2.Distance(last, start), 2.5f, kind + " comes round again");
                Assert.AreEqual(0, herds.CountActivity(AnimalActivity.Stampede));
                AssertStartleCancels(herds, AnimalActivity.Stampede);
            }
        }

        [Test]
        public void Capybaras_SoakInTheShallows_AndShakeDry()
        {
            var herds = Stage(LifeBiome.Tropical, LifeKind.Capybara, 5, new Vector2(4.6f, 0f));
            Assert.IsTrue(herds.StartChoreography(0, AnimalActivity.Soak));
            bool loaf = false, shook = false;
            for (int i = 0; i < 2400; i++)
            {
                herds.Step(0.05f);
                for (int m = 0; m < herds.HerdSize(0); m++)
                {
                    if (herds.ActivityOf(0, m) == AnimalActivity.Soak && herds.ActivityStep(0, m) == IslandHerdSystem.SoakLoaf)
                    {
                        loaf = true;
                        Assert.AreEqual(AnimalState.Rest, herds.AnimalStateOf(0, m));
                        Assert.Less(Surface(herds).SampleHeight(herds.AnimalPosition(0, m)), 0.03f, "soaking means standing in the water");
                    }
                    if (herds.ActivityOf(0, m) == AnimalActivity.Shake) shook = true;
                }
                if (shook) break;
            }
            Assert.IsTrue(loaf, "somebody soaked");
            Assert.IsTrue(shook, "somebody shook itself dry afterwards");
            Assert.AreEqual(5, herds.HerdSize(0), "nobody drowns");
            herds.behaviourRate = 0f;
            Run(herds, 90f, 0.1f);
            herds.behaviourRate = 1f;
            AssertStartleCancels(herds, AnimalActivity.Soak);
        }

        [Test]
        public void Tortoises_TuckIntoTheirShellsWhenStartled_AndPeekOutAgain()
        {
            var herds = Stage(LifeBiome.Tropical, LifeKind.Tortoise, 3, Vector2.zero);
            Vector2 centre = herds.HerdCenter(0);
            herds.Startle(new Vector2(3f, 0f));
            Assert.AreEqual(3, herds.CountActivity(AnimalActivity.Tuck));
            Assert.AreEqual(0, herds.FleeingHerds, "a tortoise does not run");
            Assert.AreEqual(1, herds.AnimalPose(0, 0));
            var seen = Watch(herds, AnimalActivity.Tuck, 20f, () => herds.CountActivity(AnimalActivity.Tuck) == 0);
            Assert.IsTrue(seen.Contains(IslandHerdSystem.TuckHide) && seen.Contains(IslandHerdSystem.TuckPeek));
            Assert.AreEqual(0, herds.CountActivity(AnimalActivity.Tuck));
            Assert.Less(Vector2.Distance(centre, herds.HerdCenter(0)), 0.2f);
            Assert.AreEqual(0, herds.AnimalPose(0, 0));
        }

        [Test]
        public void Oxen_CircleTheirYoungAtDusk_AndStayThroughTheNight()
        {
            var herds = Stage(LifeBiome.Temperate, LifeKind.Ox, 5, Vector2.zero, 8f, 0f, true);
            herds.growthInterval = 0.5f;
            herds.growthChance = 1f;
            herds.behaviourRate = 0f;
            for (int i = 0; i < 400 && herds.YoungCount == 0; i++) herds.Step(0.1f);
            Assert.Greater(herds.YoungCount, 0);
            herds.growthChance = 0f;
            herds.behaviourRate = 1f;
            _night = 0.4f;
            for (int i = 0; i < 600 && herds.Circles == 0; i++) herds.Step(0.1f);
            Assert.AreEqual(1, herds.Circles, "dusk closes the circle");
            Run(herds, 25f, 0.1f);
            Vector2 c = herds.HerdCenter(0);
            float youngR = 0f, adultR = float.MaxValue;
            int guards = 0;
            for (int m = 0; m < herds.HerdSize(0); m++)
            {
                float d = Vector2.Distance(herds.AnimalPosition(0, m), c);
                if (herds.IsYoung(0, m)) { youngR = Mathf.Max(youngR, d); continue; }
                adultR = Mathf.Min(adultR, d);
                if (herds.ActivityOf(0, m) == AnimalActivity.Circle && herds.ActivityStep(0, m) == IslandHerdSystem.CircleGuard) guards++;
            }
            Assert.Less(youngR, adultR, "the young are inside the ring");
            Assert.GreaterOrEqual(guards, 3);
            _night = 1f;
            Run(herds, 90f, 0.1f);
            Assert.Greater(herds.CountActivity(AnimalActivity.Circle), 0, "the circle lasts through the night");
            Assert.Greater(herds.SleepingCount, 0);
            herds.Startle(new Vector2(20f, 0f));
            Run(herds, 0.2f);
            Assert.AreEqual(0, herds.CountActivity(AnimalActivity.Circle));
        }

        [Test]
        public void ArcticFoxes_Pounce_MeerkatsStandSentry_SheepHopInAChain()
        {
            var fox = Stage(LifeBiome.Nordic, LifeKind.ArcticFox, 2, Vector2.zero);
            Assert.IsTrue(fox.StartChoreography(0, AnimalActivity.Pounce));
            int who = fox.ActivityOf(0, 0) == AnimalActivity.Pounce ? 0 : 1;
            Vector2 from = fox.AnimalPosition(0, who);
            float pitch = 0f;
            var seen = new HashSet<int>();
            for (int i = 0; i < 200 && fox.ActivityOf(0, who) == AnimalActivity.Pounce; i++)
            {
                fox.Step(0.05f);
                if (fox.ActivityOf(0, who) != AnimalActivity.Pounce) break;
                seen.Add(fox.ActivityStep(0, who));
                pitch = Mathf.Max(pitch, fox.AnimalBakedPitch(0, who));
            }
            for (int s = IslandHerdSystem.PounceStalk; s <= IslandHerdSystem.PounceShake; s++) Assert.IsTrue(seen.Contains(s), "pounce step " + s);
            Assert.Greater(pitch, 40f, "the fox dives nose first");
            Assert.Greater(Vector2.Distance(from, fox.AnimalPosition(0, who)), 0.05f);
            Assert.AreEqual(0f, fox.AnimalBakedPitch(0, who));
            Assert.IsTrue(fox.StartChoreography(0, AnimalActivity.Pounce));
            Run(fox, 2f);
            fox.Startle(new Vector2(9f, 0f));
            Assert.AreEqual(0, fox.CountActivity(AnimalActivity.Pounce));

            var meerkats = Stage(LifeBiome.Savanna, LifeKind.Meerkat, 6, Vector2.zero, 8f, 0f, false, 951);
            Assert.IsTrue(meerkats.StartChoreography(0, AnimalActivity.Sentry));
            bool upright = false;
            for (int i = 0; i < 400 && !upright; i++)
            {
                meerkats.Step(0.05f);
                for (int m = 0; m < meerkats.HerdSize(0); m++)
                    if (meerkats.ActivityOf(0, m) == AnimalActivity.Sentry && meerkats.AnimalPose(0, m) == 1) upright = true;
            }
            Assert.IsTrue(upright, "the sentry stands up on its hind legs");
            meerkats.Startle(new Vector2(9f, 0f));
            Assert.AreEqual(0, meerkats.CountActivity(AnimalActivity.Sentry));

            var sheep = Stage(LifeBiome.Temperate, LifeKind.Sheep, 6, Vector2.zero, 10f, 0f, false, 952);
            bool hopping = false;
            for (int i = 0; i < 4000 && sheep.HopChains < 4; i++)
            {
                sheep.Step(0.05f);
                if (sheep.CountActivity(AnimalActivity.HopChain) > 0) hopping = true;
            }
            Assert.GreaterOrEqual(sheep.HopChains, 4, "one sheep after the other jumps at the same spot");
            Assert.IsTrue(hopping);
        }

        // ------------------------------------------------------------ budgets, saves

        [Test]
        public void VertexBudgets_HoldInEveryBiome()
        {
            for (int b = 1; b < Biomes.Count; b++)
            {
                var herds = Make(13f, 960 + b, (LifeBiome)b, 3f, "Big" + b);
                var life = Life(herds);
                life.Simulate(900f, 15f);
                Assert.LessOrEqual(life.LiveVertexEstimate, life.maxVegetationVerts, ((LifeBiome)b).ToString());
                Assert.LessOrEqual(life.MeshVertexCount, 60000 + 42, ((LifeBiome)b).ToString());
                Assert.LessOrEqual(herds.HerdCount, herds.maxHerds);
                Assert.LessOrEqual(herds.AnimalCount, herds.maxAnimals);
                _camDist = 60f;
                Run(herds, herds.hiddenMeshInterval + 0.3f, 0.1f);
                Assert.AreEqual(0, herds.DetailedCount);
                Assert.LessOrEqual(herds.MeshVertexCount, 12500, (LifeBiome)b + " simple herd mesh");
                _camDist = 0f;
                Run(herds, herds.hiddenMeshInterval + 0.6f, 0.1f);
                Assert.LessOrEqual(herds.DetailedCount, herds.maxDetailed);
                Assert.LessOrEqual(herds.MeshVertexCount, 12500 + herds.maxDetailVertices, (LifeBiome)b + " detailed herd mesh");
            }
        }

        [Test]
        public void Save_RoundTripsNewKinds_AndForeignPlants()
        {
            var host = Make(8f, 971, LifeBiome.Tropical, 2f, "Host");
            var guest = Make(3f, 972, LifeBiome.Nordic, 0f, "Guest");
            Life(host).Simulate(900f, 10f);
            Life(guest).Simulate(900f, 10f);
            guest.ClearHerds();
            Assert.GreaterOrEqual(guest.AddHerd(LifeKind.Penguin, new Vector2(1f, 0f), 5), 0);
            Life(host).AbsorbFrom(Life(guest));
            host.AbsorbFrom(guest);
            Surface(host).version++;
            Life(host).Tick(1f);
            Run(host, 0.2f);
            Assert.IsTrue(host.HasSpecies(LifeKind.Penguin));
            int spruce = Life(host).CountOfKind(LifeKind.Spruce), lichen = Life(host).CountOfKind(LifeKind.Lichen), foreign = Life(host).ForeignPlantCount;
            Assert.Greater(foreign, 0);

            string json = JsonUtility.ToJson(Life(host).Capture());
            var data = JsonUtility.FromJson<LifeSaveData>(json);
            Assert.IsNotNull(data.foreign);
            Assert.AreEqual(Mathf.Min(foreign, Life(host).maxSavedForeign) * 2, data.foreign.Length);

            var copy = Make(8f, 971, LifeBiome.Tropical, 2f, "Copy");
            Life(copy).Restore(data);
            Assert.AreEqual(host.SpeciesPresent, copy.SpeciesPresent);
            Assert.AreEqual(host.AnimalsOf(LifeKind.Penguin), copy.AnimalsOf(LifeKind.Penguin));
            Assert.AreEqual(host.AnimalCount, copy.AnimalCount);
            Assert.AreEqual(spruce, Life(copy).CountOfKind(LifeKind.Spruce));
            Assert.AreEqual(lichen, Life(copy).CountOfKind(LifeKind.Lichen));
            Assert.AreEqual(foreign, Life(copy).ForeignPlantCount);
            Assert.AreEqual(Life(host).PlantsPresent, Life(copy).PlantsPresent);
        }

        [Test]
        public void OldSaves_WithoutBiomeData_StillLoad()
        {
            var old = Make(8f, 981, LifeBiome.Temperate, 0f, "Old");
            Life(old).Simulate(300f, 10f);
            string json = JsonUtility.ToJson(Life(old).Capture());
            int at = json.IndexOf(",\"foreign\":", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(at, 0);
            int end = json.IndexOf(']', at);
            json = json.Remove(at, end - at + 1);
            Assert.IsFalse(json.Contains("foreign"));
            var data = JsonUtility.FromJson<LifeSaveData>(json);
            data.herds.Add(new HerdSaveData { kind = 99, m = new float[6], variant = new int[1] });

            var loaded = Make(8f, 981, LifeBiome.Temperate, 0f, "Loaded");
            Life(loaded).Restore(data);
            Assert.AreEqual(old.HerdCount, loaded.HerdCount, "an unknown herd kind is skipped, the rest loads");
            Assert.AreEqual(old.AnimalCount, loaded.AnimalCount);
            Assert.AreEqual(old.SpeciesPresent, loaded.SpeciesPresent);
            Assert.AreEqual(0, Life(loaded).ForeignPlantCount);
            Assert.AreEqual(0UL, Life(loaded).PlantsPresent & ~Biomes.Of(LifeBiome.Temperate).mask);
            Assert.AreEqual(0UL, loaded.SpeciesPresent & ~Biomes.Of(LifeBiome.Temperate).mask);
        }

        [Test]
        public void CollectionAccessors_AndTheSteadyState_AllocateNothing()
        {
            var herds = Make(8f, 991, LifeBiome.Nordic, 2f, "Alloc");
            var life = Life(herds);
            life.Simulate(600f, 10f);
            Run(herds, 20f);
            ulong sink = 0;
            int names = 0;
            for (int warm = 0; warm < 2; warm++)
            {
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 50; i++)
                {
                    sink ^= herds.SpeciesPresent ^ life.PlantsPresent;
                    sink += (ulong)herds.SpeciesCount + (ulong)herds.AnimalsOf(LifeKind.Penguin) + (ulong)life.CountOfKind(LifeKind.Spruce);
                    names += LifeNames.Of(LifeKind.Penguin).Length + herds.BiomeName.Length + Biomes.Collectibles(LifeBiome.Nordic).Length;
                    if (herds.HasSpecies(LifeKind.Reindeer) && life.HasPlant(LifeKind.Birch)) names++;
                }
                long after = System.GC.GetAllocatedBytesForCurrentThread();
                if (warm == 1) Assert.AreEqual(0L, after - before, "collection accessors allocate");
            }
            Assert.Greater(names, 0);
            Assert.AreNotEqual(0UL, sink);
            long b0 = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) { herds.Step(0.05f); life.Step(0.05f); }
            long b1 = System.GC.GetAllocatedBytesForCurrentThread();
            Assert.LessOrEqual(b1 - b0, 1024L, "steady state allocates");
        }
    }
}
