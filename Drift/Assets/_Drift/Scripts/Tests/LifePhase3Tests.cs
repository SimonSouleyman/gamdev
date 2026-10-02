using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class LifePhase3Tests
    {
        readonly List<GameObject> _objects = new();
        float _night;

        [SetUp]
        public void SetUp()
        {
            LifeLod.DistanceProvider = _ => 0f;
            // Summer: the v0.6.8 seasons layer (courtship in spring, huddles in winter) stays out of these checks.
            LifeEnvironment.SeasonProvider = () => 0.375f;
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

        // Births need mature ground under the herd (StageAt > 0.95), so the succession is fast-forwarded first.
        // Radius 5 (area 79, ~11 herds, ~70 animals) leaves room under maxAnimals; a radius-8 island fills the
        // 120-animal cap on the spot and can never breed, which is exactly the cap rule.
        IslandHerdSystem Make(float radius, int seed, string name = "Isle", bool mature = true)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            var surface = go.AddComponent<FakeIslandSurface>();
            surface.radius = radius;
            var life = go.AddComponent<IslandLifeSystem>();
            life.seed = seed;
            var herds = go.AddComponent<IslandHerdSystem>();
            herds.seed = seed;
            go.SetActive(true);
            _objects.Add(go);
            if (mature) life.Simulate(600f, 10f);
            return herds;
        }

        static void Run(IslandHerdSystem herds, float seconds, float dt = 0.05f)
        {
            int n = Mathf.CeilToInt(seconds / dt);
            for (int i = 0; i < n; i++) herds.Step(dt);
        }

        // Steps until the first birth; returns the seconds it took, or -1.
        static float RunUntilBirth(IslandHerdSystem herds, float maxSeconds, float dt = 0.05f)
        {
            int births = herds.Births;
            int n = Mathf.CeilToInt(maxSeconds / dt);
            for (int i = 0; i < n; i++)
            {
                herds.Step(dt);
                if (herds.Births > births) return (i + 1) * dt;
            }
            return -1f;
        }

        static bool FindYoung(IslandHerdSystem herds, out int herd, out int member)
        {
            for (int h = 0; h < herds.HerdCount; h++)
                for (int m = 0; m < herds.HerdSize(h); m++)
                    if (herds.AnimalGrowth(h, m) < 1f) { herd = h; member = m; return true; }
            herd = member = -1;
            return false;
        }

        static void AssertCaps(IslandHerdSystem herds)
        {
            Assert.LessOrEqual(herds.HerdCount, herds.maxHerds);
            Assert.LessOrEqual(herds.AnimalCount, herds.maxAnimals);
            for (int h = 0; h < herds.HerdCount; h++) Assert.LessOrEqual(herds.HerdSize(h), herds.HerdMaxSize(h), "herd " + h + " outgrew its species cap");
        }

        // ------------------------------------------------------------ births

        [Test]
        public void Herds_CalmHerdBearsYoungByDay()
        {
            var herds = Make(5f,61);
            herds.growthChance = 1f;
            Assert.AreEqual(0, herds.YoungCount, "fresh herds are all adults");
            float t = RunUntilBirth(herds, 600f);
            Assert.Greater(t, 0f, "no birth within 10 minutes of calm daytime");
            Assert.LessOrEqual(t, herds.growthInterval * 3f + 1f, "a calm herd should breed within a few growth intervals");
            Assert.IsTrue(FindYoung(herds, out int h, out int m));
            Assert.Less(herds.AnimalGrowth(h, m), 0.05f);
            Assert.AreEqual(herds.youngScale, herds.AnimalSizeFactor(h, m), 0.05f);
            int p = herds.AnimalParent(h, m);
            Assert.GreaterOrEqual(p, 0, "a newborn has a parent");
            Assert.AreNotEqual(m, p);
            Assert.AreEqual(1f, herds.AnimalGrowth(h, p), "the parent is an adult");
            Run(herds, 5f);
            float dist = (herds.AnimalPosition(h, m) - herds.AnimalPosition(h, p)).magnitude;
            Assert.Less(dist, herds.BodyLength(h) * 4f, "the young stays by its parent");
            var surface = herds.GetComponent<FakeIslandSurface>();
            Assert.Greater(surface.SampleHeight(herds.AnimalPosition(h, m)), 0f);
            AssertCaps(herds);
        }

        [Test]
        public void Herds_NoBirthsAtNightOrWhileHuddlingOrStartled()
        {
            var herds = Make(5f,62);
            herds.growthChance = 1f;
            _night = 1f;
            Run(herds, 400f);
            Assert.AreEqual(0, herds.Births, "born at night");
            _night = 0f;
            herds.Agitation = 1f;
            Run(herds, 200f);
            Assert.AreEqual(0, herds.Births, "born in a storm huddle");
            herds.Agitation = 0f;
            for (int i = 0; i < 40; i++) { herds.Startle(Vector2.zero, 6f); Run(herds, 5f); }
            Assert.AreEqual(0, herds.Births, "born while fleeing");
            Assert.Greater(RunUntilBirth(herds, 400f), 0f, "the herds breed again once calm by day");
        }

        [Test]
        public void Herds_YoungAreOnePerHerdUntilIndependent()
        {
            var herds = Make(5f,63);
            herds.growthChance = 1f;
            herds.growthInterval = 5f;
            int steps = Mathf.CeilToInt(420f / 0.05f);
            int maxYoungInHerd = 0;
            for (int i = 0; i < steps; i++)
            {
                herds.Step(0.05f);
                for (int h = 0; h < herds.HerdCount; h++)
                {
                    int small = 0, young = 0;
                    for (int m = 0; m < herds.HerdSize(h); m++)
                    {
                        float g = herds.AnimalGrowth(h, m);
                        if (g < 1f) young++;
                        if (g < herds.youngIndependence) small++;
                    }
                    Assert.LessOrEqual(small, 1, "herd " + h + " has two dependent young at once");
                    maxYoungInHerd = Mathf.Max(maxYoungInHerd, young);
                }
                AssertCaps(herds);
            }
            Assert.GreaterOrEqual(herds.Births, 2);
            Assert.AreEqual(2, maxYoungInHerd, "a second young arrives once the first is independent");
        }

        // ------------------------------------------------------------ growth

        [Test]
        public void Herds_YoungGrowLinearlyToAdultAndLeaveTheParent()
        {
            var herds = Make(5f,64);
            herds.growthChance = 1f;
            Assert.Greater(RunUntilBirth(herds, 600f), 0f);
            herds.growthChance = 0f;
            Assert.IsTrue(FindYoung(herds, out int h, out int m));
            float growTime = herds.HerdGrowTime(h);
            Assert.GreaterOrEqual(growTime, 240f);
            float age0 = herds.AnimalAge(h, m);
            Run(herds, growTime * 0.5f);
            Assert.AreEqual((age0 + growTime * 0.5f) / growTime, herds.AnimalGrowth(h, m), 0.02f, "growth is linear in age");
            Assert.Less(herds.AnimalSizeFactor(h, m), 1f);
            Assert.GreaterOrEqual(herds.AnimalParent(h, m), 0);
            Run(herds, growTime * 0.5f + 1f);
            Assert.AreEqual(1f, herds.AnimalGrowth(h, m));
            Assert.AreEqual(1f, herds.AnimalSizeFactor(h, m));
            Assert.AreEqual(-1, herds.AnimalParent(h, m), "an adult has no parent slot");
            Assert.AreEqual(0, herds.YoungCount);
            var surface = herds.GetComponent<FakeIslandSurface>();
            for (int i = 0; i < herds.HerdSize(h); i++) Assert.Greater(surface.SampleHeight(herds.AnimalPosition(h, i)), 0f);
        }

        [Test]
        public void Herds_GrowthRebuildsTheMeshInStepsNotPerFrame()
        {
            var herds = Make(5f,65);
            herds.growthChance = 1f;
            herds.watchersPerHerd = 0;
            herds.playRate = 0f;
            Assert.Greater(RunUntilBirth(herds, 600f), 0f);
            herds.growthChance = 0f;
            _night = 1f;
            Run(herds, 150f);
            Assert.AreEqual(herds.AnimalCount, herds.SleepingCount, "everyone sleeps, so only growth can dirty the mesh");
            int young = herds.YoungCount;
            Assert.Greater(young, 0);
            int builds = herds.MeshBuilds, changes = herds.StateChanges;
            Run(herds, 60f);
            Assert.AreEqual(changes, herds.StateChanges);
            int perYoung = Mathf.CeilToInt(60f / (240f * herds.growthRebuildStep)) + 1;
            int newBuilds = herds.MeshBuilds - builds;
            Assert.Greater(newBuilds, 0, "a growing young must show up in the mesh");
            Assert.LessOrEqual(newBuilds, young * perYoung + 1, "growth must rebuild in ~4 % steps, not per frame");
        }

        [Test]
        public void Herds_YoungPreferredForPlayAndPlayMore()
        {
            var herds = Make(5f,66);
            herds.growthChance = 1f;
            Assert.Greater(RunUntilBirth(herds, 600f), 0f);
            herds.growthChance = 0f;
            herds.playRate = 6f;
            Run(herds, 300f);
            Assert.Greater(herds.PlaysStarted, 0);
            Assert.Greater(herds.YoungPlays, 0, "a herd with an awake young lets it chase");
        }

        // ------------------------------------------------------------ persistence and catch-up

        [Test]
        public void Herds_SaveRoundTripKeepsGrowthAgeAndParent()
        {
            var a = Make(5f,67, "A");
            a.growthChance = 1f;
            a.growthInterval = 5f;
            Run(a, 200f);
            Assert.Greater(a.YoungCount, 0);
            var json = JsonUtility.ToJson(new LifeSaveData { herds = a.Capture() });
            var saved = JsonUtility.FromJson<LifeSaveData>(json).herds;

            var b = Make(5f,68, "B", false);
            b.Restore(saved);
            Assert.AreEqual(a.HerdCount, b.HerdCount);
            Assert.AreEqual(a.AnimalCount, b.AnimalCount);
            Assert.AreEqual(a.YoungCount, b.YoungCount);
            for (int h = 0; h < a.HerdCount; h++)
            {
                Assert.AreEqual(a.HerdSize(h), b.HerdSize(h));
                for (int m = 0; m < a.HerdSize(h); m++)
                {
                    Assert.AreEqual(a.AnimalGrowth(h, m), b.AnimalGrowth(h, m), 1e-6f);
                    Assert.AreEqual(a.AnimalAge(h, m), b.AnimalAge(h, m), 1e-4f);
                    Assert.AreEqual(a.AnimalParent(h, m), b.AnimalParent(h, m), "parent of " + h + "/" + m);
                    Assert.AreEqual(a.AnimalSizeFactor(h, m), b.AnimalSizeFactor(h, m), 1e-6f);
                }
            }
            Run(b, 5f);
            AssertCaps(b);
        }

        [Test]
        public void Herds_OldSavesWithoutGrowthLoadAsAdults()
        {
            var a = Make(5f,69, "A");
            a.growthChance = 1f;
            a.growthInterval = 5f;
            Run(a, 200f);
            Assert.Greater(a.YoungCount, 0);
            var saved = a.Capture();
            foreach (var d in saved) { d.growth = null; d.age = null; d.parent = null; }
            saved = JsonUtility.FromJson<LifeSaveData>(JsonUtility.ToJson(new LifeSaveData { herds = saved })).herds;
            foreach (var d in saved) Assert.IsTrue(d.growth == null || d.growth.Length == 0, "a v2-v4 file has no growth field");

            var b = Make(5f,70, "B", false);
            b.Restore(saved);
            Assert.AreEqual(a.HerdCount, b.HerdCount);
            Assert.AreEqual(a.AnimalCount, b.AnimalCount);
            Assert.AreEqual(0, b.YoungCount);
            for (int h = 0; h < b.HerdCount; h++)
                for (int m = 0; m < b.HerdSize(h); m++)
                {
                    Assert.AreEqual(1f, b.AnimalGrowth(h, m));
                    Assert.AreEqual(-1, b.AnimalParent(h, m));
                    Assert.AreEqual(b.HerdGrowTime(h), b.AnimalAge(h, m));
                }
        }

        [Test]
        public void Herds_OfflineCatchUpAdvancesGrowthWithoutBirths()
        {
            var herds = Make(5f,71);
            var life = herds.GetComponent<IslandLifeSystem>();
            herds.growthChance = 1f;
            Assert.Greater(RunUntilBirth(herds, 600f), 0f);
            Assert.IsTrue(FindYoung(herds, out int h, out int m));
            float g0 = herds.AnimalGrowth(h, m);
            float growTime = herds.HerdGrowTime(h);
            int animals = herds.AnimalCount, births = herds.Births, builds = herds.MeshBuilds;

            life.Simulate(60f, 3f);
            float g1 = herds.AnimalGrowth(h, m);
            Assert.AreEqual(Mathf.Min(1f, g0 + 60f / life.timeScale / growTime), g1, 1e-4f, "60 life-seconds are 10 herd seconds");

            var before = new Dictionary<(int, int), float>();
            for (int hh = 0; hh < herds.HerdCount; hh++)
                for (int mm = 0; mm < herds.HerdSize(hh); mm++) before[(hh, mm)] = herds.AnimalGrowth(hh, mm);
            life.CatchUp(1800f);
            float span = 1800f / life.timeScale;
            Assert.AreEqual(Mathf.Min(1f, g1 + span / growTime), herds.AnimalGrowth(h, m), 1e-4f);
            foreach (var kv in before)
            {
                float expected = kv.Value >= 1f ? 1f : Mathf.Min(1f, kv.Value + span / herds.HerdGrowTime(kv.Key.Item1));
                Assert.AreEqual(expected, herds.AnimalGrowth(kv.Key.Item1, kv.Key.Item2), 1e-4f, "growth of " + kv.Key);
                if (expected >= 1f) Assert.AreEqual(-1, herds.AnimalParent(kv.Key.Item1, kv.Key.Item2), "grown-up animals leave the parent slot");
            }
            Assert.AreEqual(animals, herds.AnimalCount, "nothing is born while the app is closed");
            Assert.AreEqual(births, herds.Births);
            Assert.Greater(herds.MeshBuilds, builds, "the grown animals are re-baked once");

            herds.CatchUp(0f);
            herds.CatchUp(-5f);
            herds.CatchUp(float.NaN);
            Assert.AreEqual(animals, herds.AnimalCount);
        }

        // ------------------------------------------------------------ caps

        [Test]
        public void AbsorbFrom_CountsYoungAndKeepsParentsInTheSameHerd()
        {
            var host = Make(6f, 72, "Host");
            var g1 = Make(6f, 73, "Guest1");
            var g2 = Make(6f, 74, "Guest2");
            foreach (var hs in new[] { host, g1, g2 })
            {
                hs.growthChance = 1f;
                hs.growthInterval = 5f;
                Run(hs, 120f);
                Assert.Greater(hs.YoungCount, 0, hs.name + " has no young");
            }
            int total = host.AnimalCount + g1.AnimalCount + g2.AnimalCount;
            Assert.Greater(total, host.maxAnimals);

            host.AbsorbFrom(g1);
            host.AbsorbFrom(g2);
            Assert.AreEqual(0, g1.HerdCount);
            Assert.AreEqual(0, g2.HerdCount);
            AssertCaps(host);
            Assert.Greater(host.AnimalCount, host.maxAnimals / 2);
            var surface = host.GetComponent<FakeIslandSurface>();
            for (int t = 0; t < 200; t++)
            {
                host.Step(0.05f);
                AssertCaps(host);
                for (int h = 0; h < host.HerdCount; h++)
                    for (int m = 0; m < host.HerdSize(h); m++)
                    {
                        int p = host.AnimalParent(h, m);
                        if (host.AnimalGrowth(h, m) >= 1f) Assert.AreEqual(-1, p);
                        else if (p >= 0)
                        {
                            Assert.Less(p, host.HerdSize(h));
                            Assert.AreEqual(1f, host.AnimalGrowth(h, p), "a parent must be an adult of the same herd");
                        }
                        Assert.Greater(surface.SampleHeight(host.AnimalPosition(h, m)), 0f);
                    }
            }
            // 120 simple animals stay under 10k; the detail LOD adds up to 40 detailed ones (camera at distance 0 here).
            Assert.LessOrEqual(host.MeshVertexCount, 17000);
        }
    }
}
