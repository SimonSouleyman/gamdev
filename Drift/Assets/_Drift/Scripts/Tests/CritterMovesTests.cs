using System.Collections.Generic;
using Drift.Core;
using Drift.Islands;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Every critter species' own special move: crab claw wave, turtle nest, butterfly spiral, firefly light wave,
    // seabird plunge-dive and songbird murmur. Each one starts, looks like itself and hands back to the normal
    // behaviour; day/night, storms and the rising water end or forbid them.
    public class CritterMovesTests
    {
        readonly List<GameObject> _objects = new();
        float _night;

        [SetUp]
        public void SetUp()
        {
            LifeLod.DistanceProvider = _ => 0f;
            _night = 0f;
            LifeEnvironment.NightProvider = () => _night;
        }

        [TearDown]
        public void TearDown()
        {
            LifeLod.DistanceProvider = null;
            LifeEnvironment.NightProvider = null;
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        IslandCrittersSystem Make(float radius, int seed, string name = "Isle", float beach = 3f)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            var surface = go.AddComponent<FakeIslandSurface>();
            surface.radius = radius;
            surface.beach = beach;
            var life = go.AddComponent<IslandLifeSystem>();
            life.seed = seed;
            var critters = go.AddComponent<IslandCrittersSystem>();
            critters.seed = seed;
            go.SetActive(true);
            _objects.Add(go);
            return critters;
        }

        static void Run(IslandCrittersSystem c, float seconds, float dt = 0.05f)
        {
            int n = Mathf.CeilToInt(seconds / dt);
            for (int i = 0; i < n; i++) c.Step(dt);
        }

        static FakeIslandSurface Surface(IslandCrittersSystem c) => c.GetComponent<FakeIslandSurface>();

        static int First(IslandCrittersSystem c, LifeKind kind)
        {
            for (int i = 0; i < c.CritterCount; i++) if (c.KindOf(i) == kind && !c.DyingOf(i)) return i;
            return -1;
        }

        // ------------------------------------------------------------ crab: Winkertanz

        [Test]
        public void Crab_WaveDance_StaysPutWigglesAndReturnsToScuttling()
        {
            var c = Make(6f, 301);
            var s = Surface(c);
            c.crabWaveChance = 0f;
            _night = 0.55f;
            Run(c, 2f);
            int i = First(c, LifeKind.Crab);
            Assert.GreaterOrEqual(i, 0);
            Assert.IsFalse(c.TryStartSpecialMove(i), "no claw dance at dusk... only by day");
            _night = 0f;
            Assert.IsTrue(c.TryStartSpecialMove(i));
            Assert.AreEqual(CritterState.Wave, c.StateOf(i));
            Assert.AreEqual(1, c.CrabWaves);
            Vector2 at = c.PositionOf(i);
            var yaws = new HashSet<int>();
            for (int t = 0; t < 40; t++)
            {
                c.Step(0.05f);
                if (c.StateOf(i) != CritterState.Wave) break;
                Assert.AreEqual(at, c.PositionOf(i), "a dancing crab stays on its spot");
                yaws.Add(Mathf.RoundToInt(c.YawOf(i)));
            }
            Assert.GreaterOrEqual(yaws.Count, 2, "the body shuffles left and right to the beat");
            Run(c, c.crabWaveMax + 0.5f);
            Assert.AreNotEqual(CritterState.Wave, c.StateOf(i), "the dance ends");
            Assert.AreEqual(0, c.CountIn(CritterState.Wave));
            Run(c, 30f);
            Assert.Greater((c.PositionOf(i) - at).magnitude, 0.05f, "and the crab scuttles on");
            float h = s.SampleHeight(c.PositionOf(i));
            Assert.IsTrue(h >= c.shoreMin - 1e-4f && h <= c.shoreMax + 1e-4f);
        }

        [Test]
        public void Crab_WaveDance_HappensNaturallyAndAFastIslandEndsItInTheHole()
        {
            var c = Make(6f, 302);
            var s = Surface(c);
            c.crabWaveChance = 1f;
            Run(c, 10f);
            Assert.Greater(c.CrabWaves, 0, "with the chance at 1 crabs dance on their own");
            Assert.Greater(c.CountIn(CritterState.Wave), 0);
            s.speed = c.diveSpeed * 2f;
            Run(c, 2f);
            Assert.AreEqual(0, c.CountIn(CritterState.Wave));
            Assert.AreEqual(c.CrabCount, c.HiddenCrabs, "dancers dive like everyone else");
            s.speed = 0f;
            s.storm = 1f;
            Run(c, c.hiddenMax + 6f);
            Assert.AreEqual(0, c.CountIn(CritterState.Wave), "no dancing in a storm");
            int waves = c.CrabWaves;
            Run(c, 10f);
            Assert.AreEqual(waves, c.CrabWaves);
        }

        // ------------------------------------------------------------ turtle: Nestbau

        [Test]
        public void Turtle_Nest_CrawlsUpDigsLeavesAMoundAndCrawlsBack()
        {
            var c = Make(6f, 303);
            var s = Surface(c);
            c.turtleNestChance = 0f;
            _night = 0.55f; // no butterflies in the mesh count
            int i = First(c, LifeKind.Turtle);
            Assert.GreaterOrEqual(i, 0);
            _night = 0f;
            Assert.IsTrue(c.TryStartSpecialMove(i));
            Assert.IsTrue(c.NestingOf(i));
            Assert.AreEqual(CritterState.Move, c.StateOf(i));
            float nestH = s.SampleHeight(c.TargetOf(i));
            Assert.IsTrue(nestH >= 0.3f - 1e-4f && nestH <= c.turtleInland + 1e-4f, "the nest lies up the beach");

            bool dug = false;
            int vertsDigging = 0;
            for (int t = 0; t < 4000 && c.NestsDug == 0; t++)
            {
                c.Step(0.05f);
                float h = s.SampleHeight(c.PositionOf(i));
                Assert.IsTrue(h >= c.shoreMin - 1e-4f && h <= c.turtleInland + 0.1f + 1e-4f, "turtle left its ground at t=" + t);
                if (c.StateOf(i) == CritterState.Nest)
                {
                    dug = true;
                    vertsDigging = Mathf.Max(vertsDigging, c.MeshVertexCount);
                }
            }
            Assert.IsTrue(dug, "she dug");
            Assert.AreEqual(1, c.NestsDug);
            Assert.AreEqual(1, c.NestMoundCount, "a mound stays behind");
            Assert.AreEqual(CritterState.Move, c.StateOf(i), "and she crawls back to the water");
            Assert.IsFalse(c.NestingOf(i));
            Assert.Less(s.SampleHeight(c.TargetOf(i)), 0.3f + 1e-4f);
            Run(c, 120f);
            Assert.AreNotEqual(CritterState.Nest, c.StateOf(i));
            Run(c, c.nestMoundTime);
            Assert.AreEqual(0, c.NestMoundCount, "the mound sinks away after a while");
        }

        [Test]
        public void Turtle_Nest_NotAtNightAndGivenUpWhenTheWaterRises()
        {
            var c = Make(6f, 304);
            var s = Surface(c);
            c.turtleNestChance = 1f;
            c.turtleRestMin = c.turtleRestMax = 1f;
            _night = 1f;
            Run(c, 60f);
            Assert.AreEqual(0, c.NestsDug, "turtles rest at night");
            Assert.AreEqual(0, c.CountIn(CritterState.Nest));
            _night = 0f;
            Run(c, 200f);
            Assert.Greater(c.NestsDug, 0, "by day they nest on their own");
            Assert.LessOrEqual(c.NestMoundCount, c.maxNests);
            // The island sinks: the whole old beach goes under, mounds and nest trips with it; nobody drowns.
            s.radius = 4f;
            s.version++;
            Run(c, 1f);
            Assert.AreEqual(0, c.NestMoundCount, "mounds under water are gone");
            for (int k = 0; k < c.CritterCount; k++)
            {
                if (c.KindOf(k) != LifeKind.Turtle && c.KindOf(k) != LifeKind.Crab) continue;
                Assert.GreaterOrEqual(s.SampleHeight(c.PositionOf(k)), c.shoreMin - 1e-4f, "resident under water");
                if (c.NestingOf(k)) Assert.GreaterOrEqual(s.SampleHeight(c.TargetOf(k)), c.shoreMin, "a nest trip into the water");
            }
            Run(c, 30f);
            for (int k = 0; k < c.CritterCount; k++)
                if (c.KindOf(k) == LifeKind.Turtle) Assert.GreaterOrEqual(s.SampleHeight(c.PositionOf(k)), c.shoreMin - 1e-4f);
        }

        // ------------------------------------------------------------ butterfly: Spiraltanz

        [Test]
        public void Butterfly_SpiralDance_PairCirclesUpAndFliesBackToFlowers()
        {
            var c = Make(8f, 305, "Meadow", 8f);
            var life = c.GetComponent<IslandLifeSystem>();
            life.Simulate(600f, 10f);
            c.butterflySpiralChance = 0f;
            Run(c, 40f);
            Assert.Greater(c.ButterflyCount, 1, "a meadow with a few butterflies");

            bool started = false;
            for (int t = 0; t < 400 && !started; t++)
            {
                for (int i = 0; i < c.CritterCount && !started; i++)
                    if (c.KindOf(i) == LifeKind.Butterfly && !c.DyingOf(i)) started = c.TryStartSpecialMove(i);
                if (!started) c.Step(0.05f);
            }
            Assert.IsTrue(started, "two butterflies came within reach of each other");
            Assert.AreEqual(2, c.CountIn(CritterState.Spiral), "the dance is a pair");
            int a = -1, b = -1;
            for (int i = 0; i < c.CritterCount; i++)
                if (c.StateOf(i) == CritterState.Spiral) { if (a < 0) a = i; else b = i; }
            Assert.AreEqual(b, c.PartnerOf(a));
            Assert.AreEqual(a, c.PartnerOf(b));

            float maxLift = 0f, maxApart = 0f;
            float angleTravel = 0f;
            Vector2 prev = c.PositionOf(a) - c.PositionOf(b);
            for (int t = 0; t < 60; t++)
            {
                c.Step(0.05f);
                if (c.StateOf(a) != CritterState.Spiral) break;
                Vector2 d = c.PositionOf(a) - c.PositionOf(b);
                maxApart = Mathf.Max(maxApart, d.magnitude);
                maxLift = Mathf.Max(maxLift, c.LiftOf(a));
                angleTravel += Mathf.Abs(Vector2.SignedAngle(prev, d));
                prev = d;
            }
            Assert.LessOrEqual(maxApart, 2f * c.spiralRadius + 1e-3f, "they stay close, on opposite sides of one centre");
            Assert.Greater(maxLift, c.spiralRise * 0.5f, "and rise while they circle");
            Assert.Greater(angleTravel, 360f, "at least one full turn round each other");

            Run(c, c.butterflySpiralTime * 1.2f + 0.5f);
            Assert.AreEqual(0, c.CountIn(CritterState.Spiral), "the dance ends");
            Run(c, 10f);
            for (int i = 0; i < c.CritterCount; i++)
            {
                if (c.KindOf(i) != LifeKind.Butterfly) continue;
                Assert.AreEqual(0f, c.LiftOf(i), 1e-4f, "back down at flower height");
                Assert.LessOrEqual(life.NearestPlantDistance(LifeKind.Flower, c.PositionOf(i)), c.butterflyRange + 0.15f);
            }
        }

        [Test]
        public void Butterfly_SpiralDance_EndsWhenNightFalls()
        {
            var c = Make(8f, 306, "Meadow", 8f);
            c.GetComponent<IslandLifeSystem>().Simulate(600f, 10f);
            c.butterflySpiralChance = 1f;
            Run(c, 40f);
            Assert.Greater(c.SpiralDances, 0, "at chance 1 pairs dance on their own");
            _night = 1f;
            Run(c, c.fadeTime + 2f);
            Assert.AreEqual(0, c.ButterflyCount);
            Assert.AreEqual(0, c.CountIn(CritterState.Spiral));
        }

        // ------------------------------------------------------------ firefly: Lichtwelle

        [Test]
        public void Firefly_LightWave_OnlyAtNightRunsItsTimeAndReachesTheShader()
        {
            var c = Make(6f, 307);
            var s = Surface(c);
            c.fireflyWavesPerMinute = 0f;
            Run(c, 5f);
            Assert.IsFalse(c.TriggerFireflyWave(), "no swarm by day, no wave");
            _night = 1f;
            Run(c, 5f);
            Assert.IsTrue(c.TriggerFireflyWave());
            Assert.IsTrue(c.FireflyWaveActive);
            Run(c, 0.1f);
            var glow = c.transform.Find("FireflyGlow").GetComponent<MeshRenderer>();
            var block = new MaterialPropertyBlock();
            glow.GetPropertyBlock(block);
            Vector4 wave = block.GetVector("_SyncWave");
            Assert.AreEqual(c.fireflyWaveTime, wave.w, 1e-4f, "the renderer got the wave");
            Assert.AreEqual(c.FireflyWaveOrigin.x, wave.x, 1e-4f);
            bool atHome = false;
            for (int i = 0; i < c.GlowBlobs; i++) if (c.GlowBlobOf(i) == c.FireflyWaveOrigin) atHome = true;
            Assert.IsTrue(atHome, "the wave starts at one of the swarm's clusters");
            Run(c, c.fireflyWaveTime + 0.2f);
            Assert.IsFalse(c.FireflyWaveActive, "the wave ends");
            glow.GetPropertyBlock(block);
            Assert.AreEqual(0f, block.GetVector("_SyncWave").w, "and the shader is told");

            c.fireflyWavesPerMinute = 6f;
            Run(c, 120f);
            Assert.GreaterOrEqual(c.FireflyWaves, 3, "at 6 per minute waves come on their own");
            s.storm = 1f;
            Run(c, 0.2f);
            Assert.IsFalse(c.FireflyWaveActive, "a storm breaks the wave");
            int n = c.FireflyWaves;
            Run(c, 30f);
            Assert.AreEqual(n, c.FireflyWaves);
            s.storm = 0f;
            _night = 0f;
            Run(c, 30f);
            Assert.AreEqual(n, c.FireflyWaves, "none by day");
            Assert.AreEqual("Drift/Critter", c.GlowMaterial.shader.name);
            Assert.IsTrue(c.GlowMaterial.HasProperty("_SyncWave"));
        }

        // ------------------------------------------------------------ saving and merging

        [Test]
        public void SpecialMoves_AreNotSavedAndDoNotTravelWithAMerge()
        {
            var a = Make(6f, 308, "A");
            a.crabWaveChance = a.turtleNestChance = 0f;
            int crab = First(a, LifeKind.Crab), turtle = First(a, LifeKind.Turtle);
            Assert.IsTrue(a.TryStartSpecialMove(crab));
            Assert.IsTrue(a.TryStartSpecialMove(turtle));
            Run(a, 0.5f);
            var saved = a.Capture();
            foreach (int st in saved.state) Assert.LessOrEqual(st, (int)CritterState.Rest, "only the old states are written");
            var b = Make(6f, 309, "B");
            b.Restore(saved);
            Assert.AreEqual(0, b.CountIn(CritterState.Wave) + b.CountIn(CritterState.Nest));

            var host = Make(6f, 310, "Host");
            Assert.IsTrue(a.TryStartSpecialMove(crab) || a.StateOf(crab) == CritterState.Wave);
            host.AbsorbFrom(a);
            Assert.AreEqual(0, host.CountIn(CritterState.Wave) + host.CountIn(CritterState.Nest));
            for (int i = 0; i < host.CritterCount; i++) Assert.IsFalse(host.NestingOf(i));
        }

        // ------------------------------------------------------------ flocks: Stoßtauchen and Schwarmtanz

        Island MakeIsland(Vector2 pos, int seed, float radius, bool player)
        {
            var go = new GameObject(player ? "TestPlayer" : "TestIsle_" + seed);
            go.SetActive(false);
            go.transform.position = new Vector3(pos.x, 0f, pos.y);
            var isl = go.AddComponent<Island>();
            isl.useKeyboardInput = player;
            isl.landRadius = radius;
            isl.shapeSeed = seed;
            go.SetActive(true);
            _objects.Add(go);
            return isl;
        }

        static int SeedOfKind(IslandKind kind)
        {
            for (int s = 1; s < 10000; s++) if (Island.KindForSeed(s) == kind) return s;
            return 1;
        }

        FlockSystem MakeFlocks(Island player)
        {
            var go = new GameObject("Flocks");
            go.SetActive(false);
            var flocks = go.AddComponent<FlockSystem>();
            flocks.player = player;
            flocks.spawnMin = 40f;
            flocks.spawnMax = 60f;
            go.SetActive(true);
            _objects.Add(go);
            return flocks;
        }

        [Test]
        public void Seabird_PlungeDive_HitsTheWaterNextToTheCliffAndClimbsBack()
        {
            Vector2 origin = new Vector2(-40000f, 40000f);
            var player = MakeIsland(origin, 12347, 3f, true);
            var cliff = MakeIsland(origin + new Vector2(-50f, 10f), SeedOfKind(IslandKind.Barren), 5f, false);
            var flocks = MakeFlocks(player);
            flocks.seabirdDivesPerMinute = 0f;
            flocks.Step(0.05f);

            int sea = -1;
            for (int t = 0; t < 4000 && sea < 0; t++)
            {
                flocks.Step(0.05f);
                for (int i = 0; i < flocks.FlockCount; i++)
                    if (flocks.IsSeabird(i) && flocks.StateOf(i) == FlockSystem.FlockState.Orbit && flocks.TargetOf(i) == cliff) sea = i;
            }
            Assert.GreaterOrEqual(sea, 0, "a seabird flock circles the cliff");
            Assert.AreEqual(-1f, flocks.OrbitDirection(sea), "seabirds circle clockwise");
            flocks.Step(2f);
            Assert.Greater(flocks.FormationOf(sea), 0.99f, "strung out on the chain");

            bool started = false;
            for (int t = 0; t < 40 && !started; t++) { started = flocks.TryStartSpecialMove(sea); flocks.Step(0.05f); }
            Assert.IsTrue(started, "a bird over open water dives");
            int bird = -1;
            for (int k = 0; k < flocks.BirdCountOf(sea); k++) if (flocks.IsDiving(sea, k)) bird = k;
            Assert.GreaterOrEqual(bird, 0);
            float water = cliff.transform.position.y;
            float lowest = float.MaxValue, flockHeight = flocks.HeightOf(sea);
            for (int t = 0; t < 200 && flocks.IsDiving(sea, bird); t++)
            {
                flocks.Step(0.05f);
                Vector3 p = flocks.BirdPosition(sea, bird);
                lowest = Mathf.Min(lowest, p.y);
                Assert.IsTrue(flocks.StateOf(sea) == FlockSystem.FlockState.Orbit, "the flock keeps circling meanwhile");
            }
            Assert.LessOrEqual(lowest, water + 0.05f, "it reached the sea");
            Assert.IsFalse(flocks.IsDiving(sea, bird), "the dive ends");
            Assert.Greater(flocks.BirdPosition(sea, bird).y, water + 1f, "back up with the others");
            Assert.Greater(flocks.HeightOf(sea), water + 0.5f);

            flocks.seabirdDivesPerMinute = 10f;
            int before = flocks.Dives;
            for (int t = 0; t < 1200; t++) flocks.Step(0.05f);
            Assert.Greater(flocks.Dives, before, "dives come on their own");
        }

        [Test]
        public void Songbird_Murmur_SwirlsOverTheIslandAndMovesOn()
        {
            Vector2 origin = new Vector2(40000f, -40000f);
            var player = MakeIsland(origin, 12348, 3f, true);
            var isle = MakeIsland(origin + new Vector2(50f, 0f), SeedOfKind(IslandKind.Regular), 5f, false);
            var flocks = MakeFlocks(player);
            flocks.murmurChance = 0f;
            flocks.Step(0.05f);
            int f = -1;
            for (int t = 0; t < 4000 && f < 0; t++)
            {
                flocks.Step(0.05f);
                for (int i = 0; i < flocks.FlockCount; i++)
                    if (!flocks.IsSeabird(i) && flocks.StateOf(i) == FlockSystem.FlockState.Orbit) f = i;
            }
            Assert.GreaterOrEqual(f, 0, "a songbird flock circles an island");
            Assert.AreEqual(1f, flocks.OrbitDirection(f), "songbirds circle counter-clockwise");
            _night = 1f;
            Assert.IsTrue(flocks.TryStartSpecialMove(f), "a forced murmur may start at any time");
            _night = 0f;
            Assert.AreEqual(FlockSystem.FlockState.Murmur, flocks.StateOf(f));
            var target = flocks.TargetOf(f);
            for (int t = 0; t < 60; t++) flocks.Step(0.05f);
            Assert.Greater(flocks.FormationOf(f), 0.99f);
            // The birds spread along the figure: much wider than the loose pulk, and they keep changing place.
            float span = 0f;
            Vector3 b0 = flocks.BirdPosition(f, 0);
            for (int k = 1; k < flocks.BirdCountOf(f); k++) span = Mathf.Max(span, (flocks.BirdPosition(f, k) - b0).magnitude);
            Assert.Greater(span, flocks.murmurSize * 0.6f);
            Vector3 before = flocks.BirdPosition(f, 0);
            flocks.Step(0.5f);
            Assert.Greater((flocks.BirdPosition(f, 0) - before).magnitude, 0.3f);
            float closest = float.MaxValue;
            for (int t = 0; t < 800 && flocks.StateOf(f) == FlockSystem.FlockState.Murmur; t++)
            {
                flocks.Step(0.05f);
                closest = Mathf.Min(closest, (flocks.PositionOf(f) - target.PlanarPosition).magnitude);
            }
            Assert.Less(closest, 3f, "the dance hangs over the island's middle");
            Assert.AreNotEqual(FlockSystem.FlockState.Murmur, flocks.StateOf(f), "the dance ends");
            for (int t = 0; t < 40; t++) flocks.Step(0.05f);
            Assert.Less(flocks.FormationOf(f), 0.01f, "back to the loose pulk");
            Assert.GreaterOrEqual(flocks.Murmurs, 1);

            flocks.murmurChance = 1f;
            int n = flocks.Murmurs;
            for (int t = 0; t < 6000; t++) flocks.Step(0.05f);
            Assert.Greater(flocks.Murmurs, n, "murmurs come on their own by day");
        }
    }
}
