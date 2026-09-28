using Drift.Core;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The pure parts of the cozy sea shows (Drift.Core.SeaShow): picking a show, the camera's footprint on the water,
    // coast finding and the motion curves of seals, turtles, flying fish and rays.
    public class SeaShowTests
    {
        [Test]
        public void Order_TriesEveryKindOnceAndStartsElsewhereForTheNextRoll()
        {
            for (int roll = -30; roll <= 30; roll++)
            {
                int seen = 0;
                for (int i = 0; i < SeaShow.KindCount; i++) seen |= 1 << (int)SeaShow.Order(roll, i);
                Assert.AreEqual((1 << SeaShow.KindCount) - 1, seen, "roll " + roll);
                Assert.AreNotEqual(SeaShow.Order(roll, 0), SeaShow.Order(roll + 1, 0));
            }
            Assert.AreEqual(SeaShow.Order(int.MinValue, 0), SeaShow.Order(int.MinValue, SeaShow.KindCount));
        }

        [Test]
        public void Pick_OnlyReturnsReadyKinds()
        {
            int turtle = 1 << (int)SeaShowKind.Turtle;
            for (int roll = 0; roll < 20; roll++) Assert.AreEqual((int)SeaShowKind.Turtle, SeaShow.Pick(roll, turtle));
            Assert.AreEqual(-1, SeaShow.Pick(3, 0));
            int all = (1 << SeaShow.KindCount) - 1;
            for (int roll = 0; roll < SeaShow.KindCount; roll++) Assert.AreEqual((int)SeaShow.Order(roll, 0), SeaShow.Pick(roll, all));

            var cd = new float[SeaShow.KindCount];
            cd[(int)SeaShowKind.Seal] = 5f;
            cd[(int)SeaShowKind.WhaleBlow] = 0.01f;
            int ready = SeaShow.ReadyMask(cd);
            Assert.IsFalse(SeaShow.Has(ready, SeaShowKind.Seal));
            Assert.IsFalse(SeaShow.Has(ready, SeaShowKind.WhaleBlow));
            Assert.IsTrue(SeaShow.Has(ready, SeaShowKind.FlyingFish));
            for (int roll = 0; roll < 20; roll++)
            {
                int k = SeaShow.Pick(roll, ready);
                Assert.AreNotEqual((int)SeaShowKind.Seal, k);
                Assert.AreNotEqual((int)SeaShowKind.WhaleBlow, k);
            }
            Assert.AreEqual(0, SeaShow.ReadyMask(null));
        }

        [Test]
        public void WaterHit_FindsWhereTheRayMeetsTheSea()
        {
            Vector2 hit = SeaShow.WaterHit(new Vector3(2f, 10f, 3f), new Vector3(0f, -1f, 1f).normalized, 100f);
            Assert.AreEqual(2f, hit.x, 1e-3f);
            Assert.AreEqual(13f, hit.y, 1e-3f);
            // Looking up or too flat: the planar direction, cut at maxDistance.
            Vector2 up = SeaShow.WaterHit(new Vector3(0f, 5f, 0f), new Vector3(1f, 0.2f, 0f), 40f);
            Assert.AreEqual(40f, up.x, 1e-3f);
            Assert.AreEqual(0f, up.y, 1e-3f);
            Vector2 flat = SeaShow.WaterHit(new Vector3(0f, 5f, 0f), new Vector3(0f, -0.01f, 1f), 40f);
            Assert.AreEqual(40f, flat.magnitude, 1e-3f);
        }

        [Test]
        public void HalfWidthAt_FollowsFieldOfViewAndAspect()
        {
            Assert.AreEqual(10f * Mathf.Tan(30f * Mathf.Deg2Rad), SeaShow.HalfWidthAt(10f, 60f, 1f), 1e-4f);
            Assert.AreEqual(10f * Mathf.Tan(30f * Mathf.Deg2Rad) * 0.5625f, SeaShow.HalfWidthAt(10f, 60f, 0.5625f), 1e-4f);
            Assert.AreEqual(0f, SeaShow.HalfWidthAt(-3f, 60f, 1f));
        }

        [Test]
        public void ArcDirection_StaysInsideTheArc()
        {
            Vector2 f = new Vector2(3f, 4f);
            Vector2 mid = SeaShow.ArcDirection(f, 1f, 0.5f);
            Assert.AreEqual(0.6f, mid.x, 1e-4f);
            Assert.AreEqual(0.8f, mid.y, 1e-4f);
            for (int i = 0; i <= 20; i++)
            {
                Vector2 d = SeaShow.ArcDirection(f, 0.7f, i / 20f);
                Assert.AreEqual(1f, d.magnitude, 1e-4f);
                Assert.LessOrEqual(Vector2.Angle(f, d), 0.7f * Mathf.Rad2Deg + 1e-2f);
            }
            Assert.AreEqual(0.7f * Mathf.Rad2Deg, Vector2.Angle(f, SeaShow.ArcDirection(f, 0.7f, 0f)), 1e-2f);
        }

        [Test]
        public void DiscPoint_StaysInsideTheCircle()
        {
            Vector2 c = new Vector2(-5f, 7f);
            Vector2 sum = Vector2.zero;
            for (uint h = 1; h <= 400; h++)
            {
                Vector2 p = SeaShow.DiscPoint(c, 6f, SeaMath.Hash(h, 9u), 3);
                Assert.LessOrEqual((p - c).magnitude, 6f + 1e-4f);
                sum += p - c;
            }
            Assert.Less((sum / 400f).magnitude, 0.8f);
        }

        [Test]
        public void FindCoast_FindsBeachAndWaterPastTheShore()
        {
            var beachy = new[] { 1.2f, 0.8f, 0.1f, -0.2f, -0.6f, -1f };
            Assert.IsTrue(SeaShow.FindCoast(beachy, beachy.Length, 0.02f, 0.16f, -0.45f, out int beach, out int water));
            Assert.AreEqual(2, beach);
            Assert.AreEqual(4, water);

            var cliff = new[] { 1.2f, 0.8f, 0.5f, -0.8f };
            Assert.IsTrue(SeaShow.FindCoast(cliff, cliff.Length, 0.02f, 0.16f, -0.45f, out beach, out water));
            Assert.AreEqual(-1, beach);
            Assert.AreEqual(3, water);

            var sea = new[] { -1f, -1f, -1f };
            Assert.IsFalse(SeaShow.FindCoast(sea, sea.Length, 0.02f, 0.16f, -0.45f, out beach, out water));
            var shallows = new[] { 0.5f, -0.1f, -0.3f, -0.4f };
            Assert.IsFalse(SeaShow.FindCoast(shallows, shallows.Length, 0.02f, 0.16f, -0.45f, out beach, out water));
            Assert.AreEqual(-1, beach);
            // Only the first `count` samples count.
            Assert.IsFalse(SeaShow.FindCoast(beachy, 4, 0.02f, 0.16f, -0.45f, out _, out _));
            // A lagoon in the middle: the first water after land is what counts.
            var lagoon = new[] { -0.8f, 0.4f, 0.05f, -0.9f, 0.3f };
            Assert.IsTrue(SeaShow.FindCoast(lagoon, lagoon.Length, 0.02f, 0.16f, -0.45f, out beach, out water));
            Assert.AreEqual(2, beach);
            Assert.AreEqual(3, water);
        }

        [Test]
        public void PolarStep_FollowsTheCoastAndArrives()
        {
            Vector2 from = new Vector2(5f, 0f), to = new Vector2(0f, 5f);
            Vector2 p = from;
            int steps = 0;
            while (p != to && steps < 1000)
            {
                Vector2 n = SeaShow.PolarStep(p, to, 0.25f);
                Assert.AreEqual(5f, n.magnitude, 1e-3f, "stays on the circle");
                Assert.LessOrEqual((n - p).magnitude, 0.25f + 1e-3f);
                p = n;
                steps++;
            }
            Assert.AreEqual(to, p);
            // Quarter circle of radius 5 is 7.85 long.
            Assert.AreEqual(Mathf.CeilToInt(Mathf.PI * 2.5f / 0.25f), steps, 1);
            Assert.AreEqual(new Vector2(8f, 0f), SeaShow.PolarStep(new Vector2(6f, 0f), new Vector2(8f, 0f), 5f));
            Vector2 half = SeaShow.PolarStep(new Vector2(6f, 0f), new Vector2(8f, 0f), 1f);
            Assert.AreEqual(7f, half.x, 1e-3f);
            // Across the origin it takes the short way round, not through the land.
            Vector2 side = SeaShow.PolarStep(new Vector2(4f, 0.1f), new Vector2(-4f, 0.1f), 1f);
            Assert.AreEqual(4f, side.magnitude, 0.05f);
        }

        [Test]
        public void TurtleSurfacePhase_BringsTheBreathOnTimeOncePerPeriod()
        {
            const float rate = 0.45f, clock = 1234.5f, lead = 1.2f;
            float phase = SeaShow.TurtleSurfacePhase(clock, rate, lead);
            Assert.AreEqual(0f, SeaShow.TurtleBreath(clock, phase, rate));
            Assert.AreEqual(0f, SeaShow.TurtleBreath(clock + lead - 0.05f, phase, rate));
            Assert.Greater(SeaShow.TurtleBreath(clock + lead + 0.4f, phase, rate), 0f);
            int surfacings = 0;
            float prev = 0f, period = 2f * Mathf.PI / rate;
            for (float t = 0f; t < 3f * period; t += 0.05f)
            {
                float b = SeaShow.TurtleBreath(clock + t, phase, rate);
                Assert.GreaterOrEqual(b, 0f);
                Assert.LessOrEqual(b, 1f);
                if (SeaShow.Surfaced(prev, b)) surfacings++;
                prev = b;
            }
            Assert.AreEqual(3, surfacings);
        }

        [Test]
        public void SealRise_ComesUpHoldsAndGoesDown()
        {
            Assert.AreEqual(0f, SeaShow.SealRise(0f, 4f));
            Assert.AreEqual(0f, SeaShow.SealRise(4f, 4f));
            Assert.AreEqual(0f, SeaShow.SealRise(-1f, 4f));
            Assert.AreEqual(1f, SeaShow.SealRise(2f, 4f), 1e-5f);
            Assert.Less(SeaShow.SealRise(0.2f, 4f), SeaShow.SealRise(0.4f, 4f));
            Assert.Greater(SeaShow.SealRise(3.6f, 4f), SeaShow.SealRise(3.9f, 4f));
            for (float t = 0f; t < 4f; t += 0.1f) Assert.That(SeaShow.SealRise(t, 4f), Is.InRange(0f, 1f));
            Assert.LessOrEqual(Mathf.Abs(SeaShow.SealLook(7f, 1f)), 0.95f);
        }

        [Test]
        public void SkipHeight_ArcsLowerEachHopAndTouchesTheWaterBetween()
        {
            for (float t = 0.05f; t < 1f; t += 0.1f)
                Assert.AreEqual(Mathf.Sin(t * Mathf.PI) * 0.5f, SeaShow.SkipHeight(t, 1, 0.5f, out _), 1e-4f);
            Assert.AreEqual(0f, SeaShow.SkipHeight(0f, 3, 1f, out _));
            Assert.AreEqual(0f, SeaShow.SkipHeight(1f, 3, 1f, out _));
            Assert.AreEqual(0f, SeaShow.SkipHeight(1f / 3f, 3, 1f, out _), 1e-3f);
            float h0 = SeaShow.SkipHeight(1f / 6f, 3, 1f, out float u0);
            float h1 = SeaShow.SkipHeight(0.5f, 3, 1f, out float u1);
            float h2 = SeaShow.SkipHeight(5f / 6f, 3, 1f, out _);
            Assert.AreEqual(1f, h0, 1e-3f);
            Assert.AreEqual(0.7f, h1, 1e-3f);
            Assert.Less(h2, h1);
            Assert.AreEqual(0.5f, u0, 1e-3f);
            Assert.AreEqual(0.5f, u1, 1e-3f);
            Assert.AreEqual(0, SeaShow.HopIndex(0.2f, 3));
            Assert.AreEqual(2, SeaShow.HopIndex(0.99f, 3));
            Assert.AreEqual(2, SeaShow.HopIndex(1.5f, 3));
        }

        [Test]
        public void RayLeap_StartsAndEndsUnderWaterAndLandsFlat()
        {
            Assert.AreEqual(-0.4f, SeaShow.RayLeap(0f, 1.2f, out float p0, out _), 1e-4f);
            Assert.AreEqual(-0.4f, SeaShow.RayLeap(1f, 1.2f, out float p1, out _), 1e-4f);
            Assert.AreEqual(1.2f, SeaShow.RayLeap(0.5f, 1.2f, out _, out _), 1e-4f);
            Assert.Greater(p0, 0.5f, "nose up at the launch");
            Assert.LessOrEqual(p1, 0f);
            Assert.Greater(Mathf.Abs(p1), 0f);
            Assert.Less(Mathf.Abs(p1), 0.3f, "a belly flop, not a dive");
        }
    }
}
