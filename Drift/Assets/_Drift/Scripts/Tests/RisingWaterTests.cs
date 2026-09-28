using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // A sinking island: the water climbs a cone step by step (FakeHillSurface.sinkDepth plus a new Version, like
    // Island's half-second sink refresh). The herds have to walk up and stay alive until the top itself goes under.
    public class RisingWaterTests
    {
        readonly List<GameObject> _objects = new();

        [SetUp]
        public void SetUp()
        {
            LifeLod.DistanceProvider = p => 0f;
            LifeEnvironment.NightProvider = () => 0f;
            LifeEnvironment.TimeOfDayProvider = null;
            LifeEnvironment.PointOfInterest = null;
        }

        [TearDown]
        public void TearDown()
        {
            LifeLod.DistanceProvider = null;
            LifeEnvironment.NightProvider = null;
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        (IslandHerdSystem herds, FakeHillSurface hill) Make(int seed)
        {
            var go = new GameObject("SinkingHill");
            go.SetActive(false);
            var hill = go.AddComponent<FakeHillSurface>();
            hill.radius = 12f;
            hill.peak = 3f;
            go.AddComponent<IslandLifeSystem>().seed = seed;
            var herds = go.AddComponent<IslandHerdSystem>();
            herds.seed = seed;
            go.SetActive(true);
            _objects.Add(go);
            for (int i = 0; i < 40; i++) herds.Step(0.05f);
            return (herds, hill);
        }

        static void Sink(IslandHerdSystem herds, FakeHillSurface hill, float depth, float seconds)
        {
            int steps = Mathf.CeilToInt(seconds / 0.05f);
            float from = hill.sinkDepth;
            for (int i = 1; i <= steps; i++)
            {
                // Island refreshes its sink every half second: ten 0.05 s steps per shape change.
                if (i % 10 == 0)
                {
                    hill.sinkDepth = Mathf.Lerp(from, depth, i / (float)steps);
                    hill.version++;
                }
                herds.Step(0.05f);
            }
        }

        [Test]
        public void Herds_ClimbAwayFromRisingWaterInsteadOfDrowning()
        {
            var (herds, hill) = Make(301);
            int start = herds.AnimalCount;
            Assume.That(start >= 6, "too few animals");
            // Two thirds of the cone go under, slowly enough for a walk up: the top keeps a radius of 4.
            Sink(herds, hill, 2f, 120f);
            Assert.AreEqual(start, herds.AnimalCount, "animals drowned although the hilltop stayed dry");
            Assert.Greater(herds.RefugeMoves, 0, "nobody fled uphill");
            Assert.AreEqual(0, herds.Drowned);
            for (int h = 0; h < herds.HerdCount; h++)
                for (int m = 0; m < herds.HerdSize(h); m++)
                    Assert.Greater(hill.SampleHeight(herds.AnimalPosition(h, m)), 0.03f, "an animal stands in the water");
        }

        // The owner: "nicht alle sofort auf dem höchsten Punkt sammeln - nur weit genug weg, um nicht zu sinken, und
        // trotzdem auf der Insel verteilt". A third of the cone goes under: the safe band (SafeHeight plus the pace
        // margin) is still a disc of radius ~3.8, so the herds that flee stop at its edge, each on its own spot.
        [Test]
        public void Herds_SpreadOverTheSafeGround_InsteadOfCrowdingThePeak()
        {
            var (herds, hill) = Make(304);
            int start = herds.AnimalCount;
            Assume.That(herds.HerdCount >= 5, "too few herds");
            Sink(herds, hill, 1f, 60f);
            Assert.Greater(herds.RefugeMoves, 0, "nobody fled");
            Assert.AreEqual(start, herds.AnimalCount, "animals drowned although most of the hill stayed dry");
            Assert.AreEqual(0, herds.Drowned);
            Assert.IsTrue(herds.WaterRising);
            Assert.Greater(herds.SafeHeight, 0.03f + herds.refugeStart + 0.2f, "the sinking pace adds to the margin");

            int n = herds.HerdCount, onPeak = 0;
            float nearestSum = 0f;
            for (int h = 0; h < n; h++)
            {
                Vector2 c = herds.HerdCenter(h);
                Assert.Greater(hill.SampleHeight(c), 0.03f + herds.refugeStart, "herd " + h + " stands below the safe band");
                if (c.magnitude < 1f) onPeak++;
                float nearest = float.MaxValue;
                for (int o = 0; o < n; o++) if (o != h) nearest = Mathf.Min(nearest, Vector2.Distance(c, herds.HerdCenter(o)));
                nearestSum += nearest;
                for (int m = 0; m < herds.HerdSize(h); m++)
                    Assert.Greater(hill.SampleHeight(herds.AnimalPosition(h, m)), 0.03f, "an animal stands in the water");
            }
            Assert.LessOrEqual(onPeak, Mathf.Max(1, n / 4), "the herds crowd onto the peak");
            Assert.Greater(nearestSum / n, 0.7f, "the herds stand in each other instead of spreading over the safe ground");
        }

        [Test]
        public void Herds_GoOnlyWhenTheirLandIsGone()
        {
            var (herds, hill) = Make(302);
            int start = herds.AnimalCount;
            Assume.That(start > 0);
            Sink(herds, hill, 2f, 120f);
            Assert.AreEqual(start, herds.AnimalCount);
            Sink(herds, hill, 3.2f, 20f);
            Assert.AreEqual(0, herds.AnimalCount, "animals outlived the last dry ground");
        }

        [Test]
        public void Villagers_GatherUphillRestAndBuildAgain()
        {
            var go = new GameObject("SinkingVillage");
            go.SetActive(false);
            var hill = go.AddComponent<FakeHillSurface>();
            hill.radius = 16f;
            hill.peak = 2.2f;
            var life = go.AddComponent<IslandLifeSystem>();
            life.seed = 404;
            go.SetActive(true);
            _objects.Add(go);
            life.Simulate(900f, 10f);
            go.SetActive(false);
            var s = go.AddComponent<IslandSettlementSystem>();
            s.seed = 404;
            go.SetActive(true);
            s.Simulate(1500f);
            Assume.That(s.VillageCount, Is.GreaterThan(0), "no village founded on the hill");
            Assume.That(s.SettlerCount, Is.GreaterThan(1));
            int folk = s.SettlerCount;
            float before = hill.SampleHeight(s.VillageCenter(0));

            // The water climbs until the old village square is on the beach.
            float target = Mathf.Max(0.2f, before - s.siteMinHeight + 0.1f);
            for (int i = 1; i <= 20; i++)
            {
                hill.sinkDepth = target * i / 20f;
                hill.version++;
                for (int k = 0; k < 4; k++) s.Step(0.5f);
            }
            Assert.AreEqual(1, s.VillageCount, "the village gave up instead of moving");
            Assert.GreaterOrEqual(hill.SampleHeight(s.VillageCenter(0)), s.siteMinHeight, "the village square stayed low");
            Assert.GreaterOrEqual(s.SettlerCount, folk, "villagers were lost while their homes sank");
            for (int i = 0; i < s.SettlerCount; i++)
                Assert.Greater(hill.SampleHeight(s.SettlerPositionOf(i)), 0f, "a villager stands in the water");

            for (int i = 0; i < 1200; i++) s.Step(0.5f);
            int standing = 0;
            for (int i = 0; i < s.BuildingCount; i++)
                if (s.BuildingStateOf(i) != BuildingState.Sinking && hill.SampleHeight(s.BuildingPositionOf(i)) >= s.siteMinHeight) standing++;
            Assert.Greater(standing, 1, "no new homes on the high ground");
        }

        [Test]
        public void Herds_KeepTheirPlacesWhenTheWaterIsNotRising()
        {
            var (herds, hill) = Make(303);
            int moves = herds.RefugeMoves;
            for (int i = 0; i < 1200; i++) herds.Step(0.05f);
            Assert.AreEqual(moves, herds.RefugeMoves, "herds fled on an island that is not sinking");
        }
    }
}
