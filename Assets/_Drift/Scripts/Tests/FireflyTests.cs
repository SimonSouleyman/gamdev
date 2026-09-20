using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Fireflies on every island: a wandering swarm in the near tier, a swarm baked once (GPU drift and blink) in
    // the mid and far tier, a ground glow under every cluster, all of it fading with dusk, dawn and storms.
    public class FireflyTests
    {
        readonly List<GameObject> _objects = new();
        float _night;
        float _distance;

        [SetUp]
        public void SetUp()
        {
            _distance = 0f;
            _night = 0f;
            LifeLod.DistanceProvider = _ => _distance;
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

        IslandCrittersSystem Make(float radius, int seed, string name = "Isle", float beach = 1f, bool grown = false)
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
            if (grown) life.Simulate(600f, 10f);
            return critters;
        }

        static void Run(IslandCrittersSystem c, float seconds, float dt = 0.05f)
        {
            int n = Mathf.CeilToInt(seconds / dt);
            for (int i = 0; i < n; i++) c.Step(dt);
        }

        static FakeIslandSurface Surface(IslandCrittersSystem c) => c.GetComponent<FakeIslandSurface>();
        static Transform Glow(IslandCrittersSystem c) => c.transform.Find("FireflyGlow");

        [Test]
        public void EveryIsland_SmallOrLarge_HasFirefliesAtNightAndNoneByDay()
        {
            var islet = Make(1.2f, 501, "Islet", 0.5f);
            var mid = Make(5f, 502, "Mid", 1.5f, true);
            var big = Make(14f, 503, "Big", 3f);
            foreach (var c in new[] { islet, mid, big })
            {
                Run(c, 5f);
                Assert.AreEqual(0, c.FireflyCount, c.name + ": no fireflies by day");
                Assert.AreEqual(0f, c.FireflyAmount);
                Assert.IsNull(Glow(c), c.name + ": no glow object before the first night");
            }
            _night = 1f;
            foreach (var c in new[] { islet, mid, big })
            {
                Run(c, c.fadeTime + 1f);
                Assert.Greater(c.DesiredFireflies(), 0);
                Assert.AreEqual(c.DesiredFireflies(), c.FireflyCount, c.name + ": the whole swarm is out");
                Assert.AreEqual(c.FireflyCount, c.GlowHaloCount, c.name + ": every firefly draws a halo");
                Assert.GreaterOrEqual(c.GlowBlobCount, 1, c.name + ": ground glow under the swarm");
                Assert.AreEqual(c.GlowBlobs, c.GlowBlobCount);
                Assert.IsTrue(c.GlowVisible);
                Assert.AreEqual(c.GlowHaloCount * GlowBatch.HaloVerts + c.GlowBlobCount * GlowBatch.BlobVerts, c.GlowVertexCount);
                Assert.LessOrEqual(c.GlowVertexCount, 1500);
                var s = Surface(c);
                for (int i = 0; i < c.CritterCount; i++)
                    if (c.KindOf(i) == LifeKind.Firefly) Assert.Greater(s.SampleHeight(c.PositionOf(i)), 0.12f, c.name + ": a firefly over the water");
                for (int i = 0; i < c.GlowBlobs; i++) Assert.Greater(s.SampleHeight(c.GlowBlobOf(i)), 0.12f, c.name + ": ground glow on the water");
            }
            Assert.That(islet.FireflyCount, Is.InRange(4, islet.minFireflies), "a rock of a few square units keeps a handful");
            Assert.GreaterOrEqual(mid.FireflyCount, mid.minFireflies);
            Assert.AreEqual(big.maxFireflies, big.FireflyCount, "a continent hits the cap");
            Assert.GreaterOrEqual(big.maxFireflies, 40);
            Assert.LessOrEqual(big.GlowBlobCount, big.maxGlowBlobs);
            Assert.Greater(big.GlowBlobCount, mid.GlowBlobCount - 1);

            var renderer = Glow(big).GetComponent<MeshRenderer>();
            Assert.AreEqual("Drift/Critter", renderer.sharedMaterial.shader.name);
            Assert.AreEqual(1f, renderer.sharedMaterial.GetFloat("_GlowMode"));
            Assert.Greater(renderer.sharedMaterial.renderQueue, 3000, "drawn after the water");
            Assert.AreSame(renderer.sharedMaterial, Glow(islet).GetComponent<MeshRenderer>().sharedMaterial, "one shared glow material");
            Assert.AreEqual(1, CountChildren(big, "FireflyGlow"), "one extra draw call per island");

            _night = 0f;
            foreach (var c in new[] { islet, mid, big })
            {
                Run(c, c.fadeTime + 1f);
                Assert.AreEqual(0, c.FireflyCount, c.name + ": gone at dawn");
                Assert.IsFalse(c.GlowVisible);
            }
        }

        static int CountChildren(Component c, string name)
        {
            int n = 0;
            for (int i = 0; i < c.transform.childCount; i++) if (c.transform.GetChild(i).name == name) n++;
            return n;
        }

        [Test]
        public void Swarm_GrowsThroughDuskAndStaysRoundItsClusters()
        {
            var c = Make(8f, 504, "Dusk", 2f, true);
            int full = c.DesiredFireflies();
            _night = c.fireflyDusk - 0.02f;
            Run(c, 5f);
            Assert.AreEqual(0, c.FireflyCount);
            _night = (c.fireflyDusk + c.nightThreshold) * 0.5f;
            Run(c, c.fadeTime + 1f);
            Assert.That(c.FireflyAmount, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(c.FireflyCount, Is.InRange(full * 4 / 10, full * 7 / 10), "about half the swarm at half dusk");
            _night = 1f;
            Run(c, c.fadeTime + 1f);
            Assert.AreEqual(full, c.FireflyCount);
            Assert.AreEqual(full, c.FireflyHomes);
            for (int t = 0; t < 600; t++)
            {
                c.Step(0.05f);
                for (int i = 0; i < c.CritterCount; i++)
                {
                    if (c.KindOf(i) != LifeKind.Firefly) continue;
                    float nearest = float.MaxValue;
                    for (int b = 0; b < c.GlowBlobs; b++) nearest = Mathf.Min(nearest, (c.GlowBlobOf(b) - c.PositionOf(i)).magnitude);
                    Assert.LessOrEqual(nearest, c.fireflyClusterRadius + c.fireflyRange + 0.05f, "a firefly left its swarm");
                }
            }
            Assert.AreEqual(full, c.FireflyCount, "nobody is lost while wandering");
            Assert.LessOrEqual(c.FireflyCount, c.maxFireflies);
        }

        [Test]
        public void Storm_ThinsTheSwarmOut()
        {
            var c = Make(8f, 505, "Storm", 2f);
            _night = 1f;
            Run(c, c.fadeTime + 1f);
            int calm = c.FireflyCount;
            Surface(c).storm = 1f;
            Run(c, c.fadeTime + 1f);
            Assert.That(c.FireflyAmount, Is.EqualTo(1f - c.fireflyStormCut).Within(0.01f));
            Assert.Less(c.FireflyCount, calm / 2, "most of them hide from the storm");
            Assert.Greater(c.FireflyCount, 0, "a few keep glowing");
            Surface(c).storm = 0f;
            Run(c, c.fadeTime + 1f);
            Assert.AreEqual(calm, c.FireflyCount, "and they come back out");

            _distance = (c.detailDistance + c.simDistance) * 0.5f;
            Surface(c).storm = 1f;
            Run(c, 1f);
            Assert.AreEqual(LifeTier.Mid, c.Tier);
            Assert.Less(c.FirefliesShown, calm / 2, "the baked swarm is thinned on the GPU by the same amount");
            Assert.Greater(c.FirefliesShown, 0);
        }

        [Test]
        public void MidAndFarTier_BakeTheSwarmOnceAndStepNothing()
        {
            var c = Make(8f, 506, "Mid", 2f);
            _distance = (c.detailDistance + c.simDistance) * 0.5f;
            _night = 1f;
            c.Step(0.05f);
            Assert.AreEqual(LifeTier.Mid, c.Tier);
            Assert.AreEqual(0, c.FireflyCount, "no simulated fireflies outside the near tier");
            Assert.AreEqual(c.DesiredFireflies(), c.GlowHaloCount, "but the whole swarm is drawn");
            Assert.Greater(c.GlowBlobCount, 0);
            Assert.IsTrue(c.GlowVisible);
            Assert.AreEqual(c.GlowHaloCount, c.FirefliesShown);
            int builds = c.GlowMeshBuilds, homes = c.HomeBuilds, spawns = c.FireflySpawns;
            Run(c, 30f);
            Assert.AreEqual(builds, c.GlowMeshBuilds, "baked once: drift, bob and blink run in the shader");
            Assert.AreEqual(homes, c.HomeBuilds);
            Assert.AreEqual(spawns, c.FireflySpawns);

            _distance = c.simDistance + 20f;
            c.Step(0.05f);
            Assert.AreEqual(LifeTier.Far, c.Tier);
            Assert.AreEqual(Mathf.Min(c.farFireflies, c.DesiredFireflies()), c.GlowHaloCount, "a handful of glow points far away");
            Assert.LessOrEqual(c.GlowBlobCount, 2);
            builds = c.GlowMeshBuilds;
            Run(c, 30f);
            Assert.AreEqual(builds, c.GlowMeshBuilds);

            _distance = c.hideDistance + 10f;
            c.Step(0.05f);
            Assert.IsFalse(c.GlowVisible, "nothing beyond the hide distance");

            // Back under the camera the swarm takes over from the baked one at full brightness, at the same homes.
            _distance = 0f;
            c.Step(0.05f);
            Assert.AreEqual(LifeTier.Near, c.Tier);
            Assert.AreEqual(c.DesiredFireflies(), c.FireflyCount);
            int k = 0;
            for (int i = 0; i < c.CritterCount; i++)
            {
                if (c.KindOf(i) != LifeKind.Firefly) continue;
                Assert.AreEqual(1f, c.FadeOf(i), "no fade-in pop at the hand-over");
                Assert.LessOrEqual((c.PositionOf(i) - c.FireflyHomeOf(k++)).magnitude, 0.05f);
            }
            Assert.AreEqual(c.FireflyCount, c.GlowHaloCount);

            // A hidden far island at night never even computes its homes.
            var hidden = Make(8f, 507, "Hidden", 2f);
            _distance = hidden.hideDistance + 50f;
            Run(hidden, 5f);
            Assert.AreEqual(0, hidden.HomeBuilds);
            Assert.IsNull(Glow(hidden));
        }

        [Test]
        public void ShapeChanges_RehomeTheSwarmAtMostEveryTwoSeconds()
        {
            var c = Make(8f, 508, "Sinker", 2f);
            _night = 1f;
            Run(c, c.fadeTime + 1f);
            int homes = c.HomeBuilds;
            var s = Surface(c);
            for (int t = 0; t < 100; t++) { s.version++; c.Step(0.05f); }
            Assert.LessOrEqual(c.HomeBuilds - homes, 4, "a sinking island bumps its Version every frame");
            s.radius = 4f;
            s.version++;
            Run(c, 2.5f + c.fadeTime);
            for (int i = 0; i < c.CritterCount; i++)
                if (c.KindOf(i) == LifeKind.Firefly && !c.DyingOf(i)) Assert.Greater(s.SampleHeight(c.PositionOf(i)), 0.12f, "fireflies follow the shrunken shore");
            Assert.AreEqual(c.DesiredFireflies(), c.FireflyHomes);
            Assert.LessOrEqual(c.FireflyCount, c.maxFireflies);
        }
    }
}
