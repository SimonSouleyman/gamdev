using Drift.Tectonics;
using Drift.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Storms on the adventure ring: where they lie, how many of them there are, how their wind circles the eye and
    // what a lightning strike on the island costs. All of it is pure arithmetic - no scene, no Editor state.
    public class AdventureStormTests
    {
        const float Circumference = 640f;
        const float CenterX = 0f;
        const float HalfWidth = 28f;
        const float Radius = 16f;
        const float Lane = 16f;

        [Test]
        public void Count_RisesWithTheLevelAndStopsAtTheCap()
        {
            Assert.AreEqual(1, RingStormPlan.CountForLevel(1, 1f, 0.5f, 3));
            Assert.AreEqual(1, RingStormPlan.CountForLevel(2, 1f, 0.5f, 3));
            Assert.AreEqual(2, RingStormPlan.CountForLevel(3, 1f, 0.5f, 3));
            Assert.AreEqual(3, RingStormPlan.CountForLevel(5, 1f, 0.5f, 3));
            Assert.AreEqual(3, RingStormPlan.CountForLevel(40, 1f, 0.5f, 3), "the cap holds");

            int prev = 0;
            for (int level = 1; level <= 60; level++)
            {
                int n = RingStormPlan.CountForLevel(level, 1f, 0.35f, 4);
                Assert.GreaterOrEqual(n, prev, "never fewer storms than a level earlier");
                Assert.LessOrEqual(n, 4);
                prev = n;
            }
            Assert.AreEqual(0, RingStormPlan.CountForLevel(9, 0f, 0f, 4), "off means off");
            Assert.AreEqual(0, RingStormPlan.CountForLevel(9, 2f, 1f, 0));
        }

        [Test]
        public void EveryStorm_LeavesALaneOfClearWaterBesideIt()
        {
            for (int seed = 0; seed < 60; seed++)
            for (int slots = 1; slots <= 4; slots++)
            for (int i = 0; i < slots; i++)
            {
                RingStormPlan.Place(i, slots, Circumference, CenterX, HalfWidth, Radius, Lane, seed,
                                    out float along, out float x, out float radius);
                Assert.GreaterOrEqual(RingStormPlan.Lane(x, radius, CenterX, HalfWidth), Lane - 1e-3f,
                    $"seed {seed}, slot {i}/{slots}: the storm walls the band off");
                Assert.Greater(radius, 0f);
                Assert.LessOrEqual(radius, Radius + 1e-4f, "never wider than asked for");
                Assert.GreaterOrEqual(along, 0f);
                Assert.Less(along, Circumference);
            }
        }

        [Test]
        public void ANarrowBand_ShrinksTheStormInsteadOfBlockingIt()
        {
            // A band barely wider than the storm: the radius gives way, the lane does not.
            const float narrow = 12f;
            for (int seed = 0; seed < 40; seed++)
            {
                RingStormPlan.Place(0, 2, Circumference, 5f, narrow, Radius, 10f, seed,
                                    out _, out float x, out float radius);
                Assert.Less(radius, Radius, "a wide storm does not fit a narrow band");
                Assert.GreaterOrEqual(RingStormPlan.Lane(x, radius, 5f, narrow), 10f - 1e-3f);
            }
        }

        [Test]
        public void Storms_AreSpreadAroundTheRing_OneToAStratum()
        {
            const int slots = 4;
            float cell = Circumference / slots;
            for (int seed = 0; seed < 40; seed++)
            {
                var along = new float[slots];
                for (int i = 0; i < slots; i++)
                {
                    RingStormPlan.Place(i, slots, Circumference, CenterX, HalfWidth, Radius, Lane, seed,
                                        out along[i], out _, out _);
                    Assert.AreEqual(i * cell + cell * 0.5f, along[i], cell * 0.31f, "storm i stays in stratum i");
                }
                for (int i = 1; i < slots; i++)
                    Assert.Greater(along[i] - along[i - 1], cell * 0.35f, "two storms never sit on top of each other");
            }
        }

        [Test]
        public void APlacedStorm_NeverMovesWhenTheLevelAddsAnother()
        {
            // The slot count is fixed (maxStorms), only how many of them are filled changes, so slot 0 and 1 keep
            // their place when slot 2 is added.
            RingStormPlan.Place(1, 3, Circumference, CenterX, HalfWidth, Radius, Lane, 17, out float a0, out float x0, out float r0);
            RingStormPlan.Place(1, 3, Circumference, CenterX, HalfWidth, Radius, Lane, 17, out float a1, out float x1, out float r1);
            Assert.AreEqual(a0, a1);
            Assert.AreEqual(x0, x1);
            Assert.AreEqual(r0, r1);

            RingStormPlan.Place(1, 3, Circumference, CenterX, HalfWidth, Radius, Lane, 18, out float a2, out float x2, out _);
            Assert.IsTrue(!Mathf.Approximately(a0, a2) || !Mathf.Approximately(x0, x2), "another run lays them out anew");
        }

        [Test]
        public void TheWind_CirclesTheEye_AndIsCalmInIt()
        {
            var straight = new Vector2(1f, 0f);
            var weather = new StormData { radius = 20f, swirl = 0f, gustPhase = 0.4f };
            var ring = new StormData { radius = 20f, swirl = 1f, gustPhase = 0.4f };

            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                var rel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 15f;
                Assert.AreEqual(straight, StormSystem.SwirlDirection(in weather, rel, rel.magnitude, straight),
                    "without swirl the wind blows one way through the whole storm");

                Vector2 w = StormSystem.SwirlDirection(in ring, rel, rel.magnitude, straight);
                Assert.AreEqual(1f, w.magnitude, 1e-4f);
                Assert.AreEqual(0f, Vector2.Dot(w.normalized, rel.normalized), 1e-3f, "square to the radius");
            }

            // In the eye there is no tangent and no gale: the wind falls back to the straight direction.
            Assert.AreEqual(straight, StormSystem.SwirlDirection(in ring, Vector2.zero, 0f, straight));
            Vector2 near = StormSystem.SwirlDirection(in ring, new Vector2(1f, 0f), 1f, straight);
            Assert.Greater(Vector2.Dot(near, straight), 0.75f, "just off the eye it is still mostly straight");
        }

        [Test]
        public void ARingStorm_NeverBlowsOver()
        {
            var ring = new StormData { lifetime = float.PositiveInfinity, radius = 16f, strength = 1f, slot = 0 };
            var weather = new StormData { lifetime = 25f, radius = 16f, strength = 1f, slot = -1 };
            Assert.IsTrue(ring.IsRing);
            Assert.IsFalse(weather.IsRing);

            ring.age = 0f;
            Assert.AreEqual(0f, ring.Envelope(4f), 1e-5f, "it still blows up");
            ring.age = 4f;
            Assert.AreEqual(1f, ring.Envelope(4f), 1e-5f);
            ring.age = 100000f;
            Assert.AreEqual(1f, ring.Envelope(4f), 1e-5f, "and then stays");

            weather.age = 24f;
            Assert.Less(weather.Envelope(4f), 0.2f, "a weather storm dies down");
        }

        [Test]
        public void Lightning_GoesForTheIsland_OnlyInsideAStormAndOutsideTheCooldown()
        {
            Assert.IsTrue(StormStrike.AimsAtIsland(1f, 0f, 0.5f, 0.2f));
            Assert.IsFalse(StormStrike.AimsAtIsland(1f, 0f, 0.5f, 0.8f), "the other half stays over the sea");
            Assert.IsFalse(StormStrike.AimsAtIsland(0f, 0f, 1f, 0.01f), "no storm over the island, no strike");
            Assert.IsFalse(StormStrike.AimsAtIsland(1f, 2.5f, 1f, 0.01f), "not while the cooldown runs");
            Assert.IsFalse(StormStrike.AimsAtIsland(1f, 0f, 0f, 0f), "the share switches it off");

            // At the fringe of a storm the island is nearly left alone; in the middle it is hit at the full share.
            int fringe = 0, middle = 0;
            for (int i = 0; i < 1000; i++)
            {
                float roll = (i + 0.5f) / 1000f;
                if (StormStrike.AimsAtIsland(0.15f, 0f, 0.6f, roll)) fringe++;
                if (StormStrike.AimsAtIsland(1f, 0f, 0.6f, roll)) middle++;
            }
            Assert.AreEqual(90, fringe, 2);
            Assert.AreEqual(600, middle, 2);
        }

        [Test]
        public void AStrike_SlowsAndSinksByTheStrengthOfTheBolt()
        {
            Assert.AreEqual(1.2f, StormStrike.SlowSeconds(1.2f, 1f), 1e-5f);
            Assert.AreEqual(0.6f, StormStrike.SlowSeconds(1.2f, 0f), 1e-5f, "a weak bolt costs half");
            Assert.AreEqual(0.03f, StormStrike.BuoyancyLoss(0.03f, 1f), 1e-6f);
            Assert.AreEqual(0.015f, StormStrike.BuoyancyLoss(0.03f, 0f), 1e-6f);
            // It is a stagger, never a stop and never a sinking: both costs stay small and finite.
            for (int i = 0; i <= 10; i++)
            {
                float s = i / 10f;
                Assert.That(StormStrike.SlowSeconds(1.2f, s), Is.InRange(0.6f, 1.2f));
                Assert.That(StormStrike.BuoyancyLoss(0.03f, s), Is.InRange(0.015f, 0.03f));
            }
            Assert.AreEqual(0f, StormStrike.SlowSeconds(-5f, 1f), 1e-6f);
            Assert.AreEqual(0f, StormStrike.BuoyancyLoss(-5f, 1f), 1e-6f);
        }

        [Test]
        public void AStrike_ComesDownOnTheIsland()
        {
            var island = new Vector2(12f, -40f);
            const float radius = 6f;
            for (int i = 0; i < 200; i++)
            {
                float angle = i * 0.317f;
                float unit = (i * 37 % 100) / 100f;
                Vector2 at = StormStrike.Point(island, radius, angle, unit);
                Assert.LessOrEqual((at - island).magnitude, radius * 0.75f + 1e-4f);
            }
            Assert.AreEqual(island, StormStrike.Point(island, radius, 1.1f, 0f));
        }
    }
}
