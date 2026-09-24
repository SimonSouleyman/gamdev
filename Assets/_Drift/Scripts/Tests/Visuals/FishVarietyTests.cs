using System.Collections.Generic;
using Drift.Core;
using Drift.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The cozy sea-life layer of FishSystem: many more schools of seven extra kinds framed on the camera's view,
    // nothing of it in adventure, no allocations per step, a hard vertex budget.
    public class FishVarietyTests
    {
        static readonly Vector2 OpenSea = new Vector2(4000f, 4000f);

        readonly List<GameObject> _objects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        FishSystem Make(GameMode mode)
        {
            var go = new GameObject("FishVariety");
            go.SetActive(false);
            _objects.Add(go);
            var fish = go.AddComponent<FishSystem>();
            fish.seed = 11;
            fish.ModeOverride = mode;
            return fish;
        }

        static void Run(FishSystem fish, float seconds, float dt = 0.1f)
        {
            for (float t = 0f; t < seconds; t += dt) fish.Step(dt);
        }

        [Test]
        public void AdventureHasNoExtraSchools()
        {
            var fish = Make(GameMode.Adventure);
            fish.SetViewOverride(OpenSea, new Vector2(20f, 15f), 1f);
            Run(fish, 20f);
            Assert.AreEqual(fish.maxSchools, fish.SchoolSlots, "adventure keeps its pool");
            Assert.AreEqual(0, fish.AmbientActive);
            Assert.AreEqual(1f, fish.ViewScale);
        }

        [Test]
        public void CozyFillsTheViewWithManySchools()
        {
            var fish = Make(GameMode.Cozy);
            fish.SetViewOverride(OpenSea, new Vector2(20f, 15f), 1f);
            Run(fish, 8f);
            Assert.GreaterOrEqual(fish.AmbientTarget, fish.cozyAmbientMin);
            Assert.GreaterOrEqual(fish.AmbientActive, fish.AmbientTarget - 1, "the layer fills up to its target");
            int inView = 0;
            for (int i = 0; i < fish.SchoolSlots; i++)
                if (fish.IsAmbient(i) && fish.InView(fish.SchoolPosition(i))) inView++;
            Assert.GreaterOrEqual(inView, 5, "most extra schools swim in the picture");
            Assert.LessOrEqual(fish.AmbientActive, fish.cozyAmbientSchools);
        }

        [Test]
        public void ManyKindsAndSizesOverTime()
        {
            var fish = Make(GameMode.Cozy);
            fish.cozyAmbientLifetime = new Vector2(4f, 8f);
            var kinds = new HashSet<FishSystem.FishSpecies>();
            int minCount = int.MaxValue, maxCount = 0;
            // The view hops around so schools retire (out of sight) and new ones of other kinds arrive.
            for (int hop = 0; hop < 30; hop++)
            {
                fish.SetViewOverride(OpenSea + new Vector2(hop * 150f, 0f), new Vector2(20f, 15f), 1f);
                Run(fish, 3f, 0.25f);
                for (int i = 0; i < fish.SchoolSlots; i++)
                {
                    if (!fish.IsAmbient(i)) continue;
                    kinds.Add(fish.SchoolSpecies(i));
                    minCount = Mathf.Min(minCount, fish.SchoolFishCount(i));
                    maxCount = Mathf.Max(maxCount, fish.SchoolFishCount(i));
                }
            }
            // Open sea without islands: only the five open-water kinds can appear there.
            Assert.GreaterOrEqual(kinds.Count, 5, string.Join(",", kinds));
            Assert.LessOrEqual(minCount, 2, "lone big fish");
            Assert.GreaterOrEqual(maxCount, 40, "big silver clouds");
        }

        [Test]
        public void StepDoesNotAllocateAndRespectsTheBudget()
        {
            var fish = Make(GameMode.Cozy);
            fish.SetViewOverride(OpenSea, new Vector2(30f, 25f), 2f);
            Run(fish, 10f, 1f / 30f);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            Run(fish, 10f, 1f / 30f);
            long after = System.GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(0, after - before, "bytes allocated in 300 steps");
            Assert.Greater(fish.AmbientDrawn, 0);
            Assert.LessOrEqual(fish.VertexCount, fish.maxSchools * fish.maxFish * 7 + fish.maxBaitBalls * fish.baitBallFish * 7 + fish.cozyAmbientVertexBudget);
        }

        [Test]
        public void ViewScaleGrowsTheFishOnlyInCozy()
        {
            var fish = Make(GameMode.Cozy);
            fish.SetViewOverride(OpenSea, new Vector2(20f, 15f), 2f);
            Run(fish, 4f);
            Assert.AreEqual(2f, fish.ViewScale, 1e-4f);
            fish.ModeOverride = GameMode.Adventure;
            fish.Step(0.1f);
            Assert.AreEqual(1f, fish.ViewScale);
            Assert.AreEqual(0, fish.AmbientActive, "switching to adventure drops the cozy layer");
            Assert.AreEqual(fish.maxSchools, fish.SchoolSlots);
        }

        [Test]
        public void SchoolsLeaveOnceOutOfSight()
        {
            var fish = Make(GameMode.Cozy);
            fish.SetViewOverride(OpenSea, new Vector2(20f, 15f), 1f);
            Run(fish, 6f);
            Assert.Greater(fish.AmbientActive, 0);
            fish.SetViewOverride(OpenSea + new Vector2(500f, 0f), new Vector2(20f, 15f), 1f);
            fish.Step(0.6f);
            for (int i = 0; i < fish.SchoolSlots; i++)
                if (fish.IsAmbient(i))
                    Assert.Less((fish.SchoolPosition(i) - fish.ViewCenter).magnitude, fish.ViewRadius * 1.6f + 8f, "old schools were retired");
        }
    }
}
