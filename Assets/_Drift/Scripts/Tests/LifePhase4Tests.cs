using System.Collections.Generic;
using Drift.Core;
using Drift.Islands;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class LifePhase4Tests
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

        // The fake island gets a 3-unit beach so a shore band (0.02..0.25) exists as a ring inside the radius.
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

        static void AssertCaps(IslandCrittersSystem c)
        {
            Assert.LessOrEqual(c.CrabCount, c.maxCrabs);
            Assert.LessOrEqual(c.TurtleCount, c.maxTurtles);
            Assert.LessOrEqual(c.ButterflyCount, c.maxButterflies);
            Assert.LessOrEqual(c.FireflyCount, c.maxFireflies);
        }

        // ------------------------------------------------------------ crabs

        [Test]
        public void Crabs_SpawnOnTheShoreBandAndScuttleAlongIt()
        {
            var c = Make(6f, 81);
            var s = Surface(c);
            Assert.Greater(c.DesiredCrabs(), 0);
            Assert.AreEqual(c.DesiredCrabs(), c.CrabCount, "every desired crab found a shore spot");
            var start = new List<Vector2>();
            for (int i = 0; i < c.CritterCount; i++) if (c.KindOf(i) == LifeKind.Crab) start.Add(c.PositionOf(i));
            int moved = 0, checks = 0;
            for (int t = 0; t < 1200; t++)
            {
                c.Step(0.05f);
                for (int i = 0; i < c.CritterCount; i++)
                {
                    if (c.KindOf(i) != LifeKind.Crab) continue;
                    float h = s.SampleHeight(c.PositionOf(i));
                    Assert.GreaterOrEqual(h, c.shoreMin - 1e-4f, "crab " + i + " below the shore band at t=" + t);
                    Assert.LessOrEqual(h, c.shoreMax + 1e-4f, "crab " + i + " above the shore band at t=" + t);
                    checks++;
                }
            }
            Assert.Greater(checks, 0);
            int k = 0;
            for (int i = 0; i < c.CritterCount; i++)
                if (c.KindOf(i) == LifeKind.Crab && (c.PositionOf(i) - start[k++]).magnitude > 0.2f) moved++;
            Assert.Greater(moved, c.CrabCount / 2, "most crabs scuttle within a minute");
            Assert.AreEqual(0, c.HiddenCrabs, "nobody dives while the island is still");
            Assert.AreEqual(0, c.Dives);
            Assert.AreEqual(LifeTier.Near, c.Tier);
        }

        [Test]
        public void Crabs_DiveWhenTheIslandMovesFastAndComeBack()
        {
            var c = Make(6f, 82);
            var s = Surface(c);
            // Dusk: no butterflies, and the fireflies live in their own glow mesh, so the mesh size only follows the crabs.
            _night = 0.55f;
            Run(c, 5f);
            int crabs = c.CrabCount;
            int vertsShown = c.MeshVertexCount;
            s.speed = c.diveSpeed * 2f;
            Run(c, 2f);
            Assert.AreEqual(crabs, c.HiddenCrabs, "every crab dives within its 0.6 s reaction");
            Assert.AreEqual(crabs, c.Dives);
            Assert.Less(c.MeshVertexCount, vertsShown, "hidden crabs are not drawn");
            Run(c, 10f);
            Assert.AreEqual(crabs, c.HiddenCrabs, "they stay in their holes while the island keeps moving");
            s.speed = 0f;
            Run(c, c.hiddenMin * 0.5f);
            Assert.Greater(c.HiddenCrabs, 0, "they wait a few seconds after the island slowed down");
            Run(c, c.hiddenMax + 1f);
            Assert.AreEqual(0, c.HiddenCrabs, "all back out");
            Assert.AreEqual(crabs, c.CrabCount);
            for (int i = 0; i < c.CritterCount; i++)
                if (c.KindOf(i) == LifeKind.Crab)
                {
                    float h = s.SampleHeight(c.PositionOf(i));
                    Assert.IsTrue(h >= c.shoreMin - 1e-4f && h <= c.shoreMax + 1e-4f, "a crab surfaced off the shore band");
                }
            Assert.AreEqual(vertsShown, c.MeshVertexCount);
            s.speed = c.diveSpeed * 0.5f;
            Run(c, 5f);
            Assert.AreEqual(0, c.HiddenCrabs, "a slow drift does not scare them");
        }

        [Test]
        public void Crabs_CountFollowsTheShorelineUpToTheCap()
        {
            var small = Make(3f, 83, "Small");
            var mid = Make(6f, 84, "Mid");
            var big = Make(14f, 85, "Big");
            Assert.Greater(small.CrabCount, 0);
            Assert.Greater(mid.CrabCount, small.CrabCount);
            Assert.GreaterOrEqual(big.CrabCount, mid.CrabCount);
            Assert.AreEqual(big.maxCrabs, big.CrabCount, "a big island hits the hard cap");
            Assert.AreEqual(big.maxCrabs, big.DesiredCrabs());
            AssertCaps(small); AssertCaps(mid); AssertCaps(big);
        }

        // ------------------------------------------------------------ turtles

        [Test]
        public void Turtles_OnlyOnIslandsAboveTwentyAreaAndCrawlBetweenShoreAndInland()
        {
            var tiny = Make(2.4f, 86, "Tiny", 1.5f);
            Assert.Less(Surface(tiny).LandArea, 20f);
            Assert.AreEqual(0, tiny.TurtleCount);
            Assert.AreEqual(0, tiny.DesiredTurtles());

            var c = Make(6f, 87);
            var s = Surface(c);
            Assert.Greater(s.LandArea, 20f);
            Assert.AreEqual(c.DesiredTurtles(), c.TurtleCount);
            Assert.Greater(c.TurtleCount, 0);
            Assert.LessOrEqual(c.TurtleCount, c.maxTurtles);
            bool crawled = false, inland = false;
            var start = new List<Vector2>();
            for (int i = 0; i < c.CritterCount; i++) if (c.KindOf(i) == LifeKind.Turtle) start.Add(c.PositionOf(i));
            for (int t = 0; t < 8000; t++)
            {
                c.Step(0.05f);
                for (int i = 0; i < c.CritterCount; i++)
                {
                    if (c.KindOf(i) != LifeKind.Turtle) continue;
                    float h = s.SampleHeight(c.PositionOf(i));
                    Assert.GreaterOrEqual(h, c.shoreMin - 1e-4f, "turtle in the water at t=" + t);
                    Assert.LessOrEqual(h, c.turtleInland + 0.1f + 1e-4f, "turtle too far inland at t=" + t);
                    if (c.StateOf(i) == CritterState.Move) crawled = true;
                    if (h > 0.3f) inland = true;
                }
            }
            Assert.IsTrue(crawled, "a turtle crawled within 400 s");
            Assert.IsTrue(inland, "a turtle went inland");
            int k = 0, moved = 0;
            for (int i = 0; i < c.CritterCount; i++)
                if (c.KindOf(i) == LifeKind.Turtle && (c.PositionOf(i) - start[k++]).magnitude > 0.1f) moved++;
            Assert.Greater(moved, 0);
        }

        [Test]
        public void Turtles_RestAtNight()
        {
            var c = Make(6f, 88);
            _night = 1f;
            Run(c, 200f);
            for (int i = 0; i < c.CritterCount; i++)
                if (c.KindOf(i) == LifeKind.Turtle) Assert.AreEqual(CritterState.Rest, c.StateOf(i));
        }

        // ------------------------------------------------------------ butterflies and fireflies

        [Test]
        public void Butterflies_OnlyByDayAndOnlyAboveFlowers()
        {
            // A fully sloped, mature island: succession has run out on the high ground, so the flowers that are
            // left sit on the low band (stage capped at 0.4) and stay there for the whole test.
            var c = Make(8f, 89, "Meadow", 8f);
            var life = c.GetComponent<IslandLifeSystem>();
            life.Simulate(600f, 10f);
            Assert.Greater(life.CountOf(LifeKind.Flower), 3, "the mature island keeps flowers on its low ground");
            Run(c, 40f);
            Assert.Greater(c.ButterflyCount, 0, "butterflies appear by day");
            Assert.LessOrEqual(c.ButterflyCount, c.maxButterflies);
            Assert.LessOrEqual(c.ButterflyCount, life.CountOf(LifeKind.Flower) / c.butterflyFlowers + 1);
            int idle = 0;
            for (int t = 0; t < 600; t++)
            {
                c.Step(0.05f);
                for (int i = 0; i < c.CritterCount; i++)
                {
                    if (c.KindOf(i) != LifeKind.Butterfly) continue;
                    float d = life.NearestPlantDistance(LifeKind.Flower, c.PositionOf(i));
                    Assert.LessOrEqual(d, c.butterflyRange + 0.15f, "a butterfly strayed from the flowers");
                    if (c.StateOf(i) == CritterState.Idle && c.FadeOf(i) >= 1f)
                    {
                        Assert.Less(d, 0.06f, "a resting butterfly sits on a flower");
                        idle++;
                    }
                }
            }
            Assert.Greater(idle, 0, "butterflies rest on flowers between flights");
            for (int i = 0; i < c.CritterCount; i++) Assert.AreNotEqual(LifeKind.Firefly, c.KindOf(i), "no fireflies by day");

            _night = 1f;
            Run(c, c.fadeTime + 2f);
            Assert.AreEqual(0, c.ButterflyCount, "butterflies fade out at night");
            _night = 0f;
            Run(c, 40f);
            Assert.Greater(c.ButterflyCount, 0, "and come back at dawn");
            AssertCaps(c);
        }

        [Test]
        public void Fireflies_OnlyAtNightOverGrownGround()
        {
            var c = Make(6f, 90);
            var s = Surface(c);
            Run(c, 30f);
            Assert.AreEqual(0, c.FireflyCount, "no fireflies by day");
            _night = 1f;
            Run(c, 40f);
            Assert.Greater(c.FireflyCount, 0, "fireflies appear at night");
            Assert.LessOrEqual(c.FireflyCount, c.maxFireflies);
            Assert.AreEqual(c.DesiredFireflies(), c.FireflyCount, "the whole swarm is out at deep night");
            Assert.AreEqual(0, c.ButterflyCount, "no butterflies at night");
            for (int t = 0; t < 400; t++)
            {
                c.Step(0.05f);
                for (int i = 0; i < c.CritterCount; i++)
                {
                    if (c.KindOf(i) != LifeKind.Firefly) continue;
                    Assert.Greater(s.SampleHeight(c.PositionOf(i)), 0.12f, "a firefly drifted over the water");
                    Assert.Greater(s.SampleHeight(c.TargetOf(i)), 0.12f);
                }
            }
            _night = 0f;
            Run(c, c.fadeTime + 2f);
            Assert.AreEqual(0, c.FireflyCount, "fireflies fade out at dawn");
            AssertCaps(c);
        }

        // ------------------------------------------------------------ tiers

        [Test]
        public void Critters_FarTierSpawnsNothingAndMidTierFreezesTransients()
        {
            var far = Make(8f, 91, "Far", 8f);
            LifeLod.DistanceProvider = _ => 1000f;
            int builds = far.MeshBuilds;
            var crabs = new List<Vector2>();
            for (int i = 0; i < far.CritterCount; i++) crabs.Add(far.PositionOf(i));
            Run(far, 60f);
            _night = 1f;
            Run(far, 60f);
            _night = 0f;
            Assert.AreEqual(LifeTier.Far, far.Tier);
            Assert.AreEqual(0, far.ButterflyCount);
            Assert.AreEqual(0, far.FireflyCount);
            Assert.AreEqual(0, far.ButterflySpawns + far.FireflySpawns);
            Assert.LessOrEqual(far.MeshBuilds - builds, 1, "a far island rebuilds nothing");
            for (int i = 0; i < far.CritterCount; i++) Assert.AreEqual(crabs[i], far.PositionOf(i), "far residents do not step");

            LifeLod.DistanceProvider = _ => 0f;
            Run(far, 40f);
            Assert.AreEqual(LifeTier.Near, far.Tier);
            Assert.Greater(far.ButterflyCount, 0, "back in the near tier the meadow fills again");

            var mid = Make(8f, 92, "Mid", 8f);
            Run(mid, 40f);
            Assert.Greater(mid.ButterflyCount, 0);
            LifeLod.DistanceProvider = _ => (mid.detailDistance + mid.simDistance) * 0.5f;
            var frozen = new List<Vector2>();
            for (int i = 0; i < mid.CritterCount; i++) if (mid.KindOf(i) == LifeKind.Butterfly) frozen.Add(mid.PositionOf(i));
            int midBuilds = mid.MeshBuilds, spawns = mid.ButterflySpawns;
            Run(mid, 20f);
            Assert.AreEqual(LifeTier.Mid, mid.Tier);
            Assert.AreEqual(frozen.Count, mid.ButterflyCount, "mid keeps its butterflies but adds none");
            Assert.AreEqual(spawns, mid.ButterflySpawns);
            int k = 0;
            for (int i = 0; i < mid.CritterCount; i++)
                if (mid.KindOf(i) == LifeKind.Butterfly) Assert.AreEqual(frozen[k++], mid.PositionOf(i), "mid butterflies are frozen");
            Assert.LessOrEqual(mid.MeshBuilds - midBuilds, Mathf.CeilToInt(20f / (mid.meshInterval * 2f)) + 1);
        }

        [Test]
        public void Critters_UnchangedStepDoesNotRebuild()
        {
            var c = Make(6f, 93);
            c.maxFireflies = 0;
            _night = 0.55f;
            Run(c, 10f);
            for (int i = 0; i < c.CritterCount; i++) Assert.IsTrue(c.KindOf(i) == LifeKind.Crab || c.KindOf(i) == LifeKind.Turtle, "dusk holds neither butterflies nor fireflies");
            c.Step(0f);
            c.Step(0f);
            int before = c.MeshBuilds, glow = c.GlowMeshBuilds;
            for (int t = 0; t < 30; t++) c.Step(0f);
            Assert.AreEqual(before, c.MeshBuilds);
            Assert.AreEqual(glow, c.GlowMeshBuilds);
        }

        // ------------------------------------------------------------ caps, merges, persistence

        [Test]
        public void Critters_CapsHoldAfterAbsorbAndShift()
        {
            var host = Make(14f, 94, "Host");
            var guest = Make(14f, 95, "Guest");
            Run(host, 30f);
            _night = 1f;
            Run(guest, 30f);
            _night = 0f;
            Assert.AreEqual(host.maxCrabs, host.CrabCount);
            Assert.AreEqual(guest.maxCrabs, guest.CrabCount);
            Assert.Greater(guest.FireflyCount, 0);
            host.ShiftLocal(new Vector2(0.5f, -0.25f));
            host.AbsorbFrom(guest);
            Assert.AreEqual(0, guest.CritterCount);
            AssertCaps(host);
            Assert.AreEqual(0, host.FireflyCount, "transients do not travel with a merge");
            Surface(host).version++;
            for (int t = 0; t < 200; t++) { host.Step(0.05f); AssertCaps(host); }
            var s = Surface(host);
            for (int i = 0; i < host.CritterCount; i++)
                if (host.KindOf(i) == LifeKind.Crab || host.KindOf(i) == LifeKind.Turtle)
                    Assert.GreaterOrEqual(s.SampleHeight(host.PositionOf(i)), host.shoreMin - 1e-4f, "a resident ended in the water after the merge");
            Assert.LessOrEqual(host.MeshVertexCount, 8000);
        }

        [Test]
        public void Critters_SaveRoundTripKeepsCrabsAndTurtlesAndDropsTransients()
        {
            var a = Make(8f, 96, "A", 8f);
            // Special moves are saved as the plain state they return to (CritterMovesTests); keep them out here so
            // the live states compare one to one.
            a.crabWaveChance = a.turtleNestChance = 0f;
            Run(a, 45f);
            Assert.Greater(a.ButterflyCount, 0);
            var lifeA = a.GetComponent<IslandLifeSystem>();
            var json = JsonUtility.ToJson(lifeA.Capture());
            var saved = JsonUtility.FromJson<LifeSaveData>(json);
            Assert.IsNotNull(saved.critters);
            Assert.AreEqual(a.CrabCount + a.TurtleCount, saved.critters.kind.Length);

            var b = Make(8f, 97, "B", 8f);
            var lifeB = b.GetComponent<IslandLifeSystem>();
            lifeB.Restore(saved);
            Assert.AreEqual(a.CrabCount, b.CrabCount);
            Assert.AreEqual(a.TurtleCount, b.TurtleCount);
            Assert.AreEqual(0, b.ButterflyCount + b.FireflyCount, "transients are not saved");
            int k = 0;
            for (int i = 0; i < a.CritterCount; i++)
            {
                var kind = a.KindOf(i);
                if (kind != LifeKind.Crab && kind != LifeKind.Turtle) continue;
                Assert.AreEqual(kind, b.KindOf(k));
                Assert.AreEqual(a.PositionOf(i), b.PositionOf(k));
                Assert.AreEqual(a.YawOf(i), b.YawOf(k), 1e-5f);
                Assert.AreEqual(a.StateOf(i), b.StateOf(k));
                k++;
            }
            Run(b, 5f);
            AssertCaps(b);

            // A file from before Phase 4 carries no critter block: the residents come fresh from the seed.
            saved.critters = null;
            var old = JsonUtility.FromJson<LifeSaveData>(JsonUtility.ToJson(saved));
            Assert.IsTrue(old.critters == null || old.critters.kind == null || old.critters.kind.Length == 0);
            var d = Make(8f, 98, "D", 8f);
            d.GetComponent<IslandLifeSystem>().Restore(old);
            Assert.AreEqual(d.DesiredCrabs(), d.CrabCount);
            Assert.AreEqual(d.DesiredTurtles(), d.TurtleCount);
        }

        [Test]
        public void Critters_HiddenCrabsRestoreAsIdleAndSinkingShoreRelocatesThem()
        {
            var a = Make(6f, 99, "A");
            var s = Surface(a);
            s.speed = 5f;
            Run(a, 2f);
            Assert.AreEqual(a.CrabCount, a.HiddenCrabs);
            var saved = a.Capture();
            var b = Make(6f, 100, "B");
            b.Restore(saved);
            Assert.AreEqual(a.CrabCount, b.CrabCount);
            Assert.AreEqual(0, b.HiddenCrabs, "a hidden crab is restored out of its hole");

            // The shore moves inward (a sinking island): the residents follow it or go, never sit under water.
            var sb = Surface(b);
            sb.height = 2f;
            sb.version++;
            Run(b, 1f);
            for (int i = 0; i < b.CritterCount; i++)
            {
                float h = sb.SampleHeight(b.PositionOf(i));
                Assert.GreaterOrEqual(h, b.shoreMin - 1e-4f, "resident " + i + " under water after the shore moved");
            }
            Assert.Greater(b.CrabCount, 0);
        }

        // ------------------------------------------------------------ seabirds

        static int SeedOfKind(IslandKind kind)
        {
            for (int s = 1; s < 10000; s++) if (Island.KindForSeed(s) == kind) return s;
            return 1;
        }

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

        [Test]
        public void Seabirds_CircleCliffIslandsGlidingAndNeverLand()
        {
            // Far from the live scene's world so its islands are outside the 300 u candidate radius.
            Vector2 origin = new Vector2(40000f, 40000f);
            var player = MakeIsland(origin, 12346, 3f, true);
            var flat = MakeIsland(origin + new Vector2(60f, 0f), SeedOfKind(IslandKind.Regular), 5f, false);
            var cliff = MakeIsland(origin + new Vector2(-60f, 10f), SeedOfKind(IslandKind.Barren), 5f, false);
            Assert.AreEqual(IslandKind.Barren, cliff.kind);
            var go = new GameObject("Flocks");
            go.SetActive(false);
            var flocks = go.AddComponent<FlockSystem>();
            flocks.player = player;
            flocks.spawnMin = 40f;
            flocks.spawnMax = 60f;
            go.SetActive(true);
            _objects.Add(go);

            Assert.IsTrue(flocks.IsCliff(cliff));
            Assert.IsTrue(flocks.IsCliff(cliff), "the cliff test is cached per island");
            flocks.Step(0.05f);
            Assert.AreEqual(flocks.flockCount + flocks.seabirdFlocks, flocks.FlockCount);
            Assert.AreEqual(flocks.seabirdFlocks, flocks.SeabirdFlockCount);
            Assert.LessOrEqual(flocks.seabirdFlocks, 2);
            Assert.LessOrEqual(flocks.SeabirdCount, flocks.seabirdFlocks * flocks.seabirdMaxBirds);

            bool orbitedCliff = false;
            float lowest = float.MaxValue;
            for (int t = 0; t < 4000; t++)
            {
                flocks.Step(0.05f);
                for (int i = 0; i < flocks.FlockCount; i++)
                {
                    if (!flocks.IsSeabird(i)) continue;
                    var st = flocks.StateOf(i);
                    Assert.IsTrue(st == FlockSystem.FlockState.Cruise || st == FlockSystem.FlockState.Orbit, "seabirds only cruise and orbit, got " + st);
                    var target = flocks.TargetOf(i);
                    if (target != null) Assert.IsTrue(flocks.IsCliff(target), "with a cliff island in range a seabird flock targets it, not " + target.name);
                    if (st == FlockSystem.FlockState.Orbit && target == cliff)
                    {
                        orbitedCliff = true;
                        lowest = Mathf.Min(lowest, flocks.HeightOf(i));
                    }
                }
            }
            Assert.IsTrue(orbitedCliff, "a seabird flock circled the cliff island within 200 s");
            Assert.Greater(lowest, cliff.transform.position.y + 0.5f, "they glide above the island, never on it");
            Assert.AreEqual(0, flocks.PerchedFlocks - CountPerchedRegular(flocks));
        }

        static int CountPerchedRegular(FlockSystem flocks)
        {
            int n = 0;
            for (int i = 0; i < flocks.FlockCount; i++)
                if (!flocks.IsSeabird(i) && flocks.StateOf(i) == FlockSystem.FlockState.Perched) n++;
            return n;
        }

        [Test]
        public void Templates_CrittersAreTinyAndSeabirdIsCheaperThanBird()
        {
            for (int v = 0; v < LifeMeshes.Variants; v++)
            {
                Assert.LessOrEqual(LifeMeshes.GetTemplate(LifeKind.Crab, v).vertices.Length, 80);
                Assert.LessOrEqual(LifeMeshes.GetTemplate(LifeKind.Turtle, v).vertices.Length, 24);
                Assert.LessOrEqual(LifeMeshes.GetTemplate(LifeKind.Butterfly, v).vertices.Length, 6);
                Assert.LessOrEqual(LifeMeshes.GetTemplate(LifeKind.Firefly, v).vertices.Length, 6);
                Assert.Less(LifeMeshes.GetTemplate(LifeKind.Seabird, v).vertices.Length, LifeMeshes.GetTemplate(LifeKind.Bird, v).vertices.Length / 3);
            }
            var c = Make(14f, 101, "Worst");
            _night = 1f;
            Run(c, 60f);
            Assert.LessOrEqual(c.MeshVertexCount, 12 * 80 + 3 * 24 + 24 * 6 + 16 * 6);
            Assert.AreEqual("Drift/Critter", c.CritterMaterial.shader.name);
        }
    }
}
