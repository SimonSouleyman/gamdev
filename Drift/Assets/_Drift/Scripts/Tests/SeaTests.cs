using Drift.Core;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The pure parts of the sea systems (Drift.Core.SeaMath). Each test is a public parameterless method so it
    // can also be invoked by reflection from `unity command eval` while the shared Editor must not run tests.
    public class SeaTests
    {
        [Test]
        public void WrapDelta_TakesTheShortWayAroundTheTorus()
        {
            Assert.AreEqual(20f, SeaMath.WrapDelta(650f, 10f, 660f), 1e-4f);
            Assert.AreEqual(-20f, SeaMath.WrapDelta(10f, 650f, 660f), 1e-4f);
            Assert.AreEqual(100f, SeaMath.WrapDelta(100f, 200f, 660f), 1e-4f);
            Assert.AreEqual(5f, SeaMath.WrapDelta(0f, 660f * 3f + 5f, 660f), 1e-3f);
            Assert.AreEqual(300f, SeaMath.WrapDelta(0f, 300f, 0f), 1e-4f);
        }

        [Test]
        public void WrapDistance_IsSymmetricAndNeverLongerThanHalfTheDiagonal()
        {
            var a = new Vector2(5f, 655f);
            var b = new Vector2(650f, 3f);
            float d = SeaMath.WrapDistance(a, b, 660f);
            Assert.AreEqual(SeaMath.WrapDistance(b, a, 660f), d, 1e-4f);
            Assert.AreEqual(Mathf.Sqrt(15f * 15f + 8f * 8f), d, 1e-3f);
            var rnd = new System.Random(3);
            for (int i = 0; i < 200; i++)
            {
                var p = new Vector2((float)rnd.NextDouble() * 5000f - 2500f, (float)rnd.NextDouble() * 5000f - 2500f);
                var q = new Vector2((float)rnd.NextDouble() * 5000f - 2500f, (float)rnd.NextDouble() * 5000f - 2500f);
                Assert.LessOrEqual(SeaMath.WrapDistance(p, q, 660f), 330f * Mathf.Sqrt(2f) + 1e-2f);
            }
        }

        [Test]
        public void CellSeed_RepeatsOnEveryCopyOfTheWorld()
        {
            int wrap = SeaMath.WrapCells(660f, 30f);
            Assert.AreEqual(22, wrap);
            for (int cx = -3; cx < 4; cx++)
                for (int cy = -3; cy < 4; cy++)
                {
                    uint h = SeaMath.CellSeed(23, cx, cy, wrap);
                    Assert.AreEqual(h, SeaMath.CellSeed(23, cx + wrap, cy, wrap));
                    Assert.AreEqual(h, SeaMath.CellSeed(23, cx, cy - 2 * wrap, wrap));
                    Assert.AreEqual(h, SeaMath.CellSeed(23, cx, cy, wrap));
                    Vector2 p0 = SeaMath.CellPoint(cx, cy, 30f, h);
                    Vector2 p1 = SeaMath.CellPoint(cx + wrap, cy, 30f, h);
                    Assert.AreEqual(660f, p1.x - p0.x, 1e-2f);
                    Assert.AreEqual(p0.y, p1.y, 1e-3f);
                    Assert.GreaterOrEqual(p0.x, cx * 30f);
                    Assert.LessOrEqual(p0.x, (cx + 1) * 30f);
                }
        }

        [Test]
        public void CellSeed_DependsOnSeedAndCell()
        {
            int wrap = SeaMath.WrapCells(660f, 60f);
            int same = 0;
            for (int i = 0; i < 11; i++)
            {
                if (SeaMath.CellSeed(1, i, 0, wrap) == SeaMath.CellSeed(2, i, 0, wrap)) same++;
                if (SeaMath.CellSeed(1, i, 0, wrap) == SeaMath.CellSeed(1, i, 1, wrap)) same++;
            }
            Assert.AreEqual(0, same);
            float sum = 0f;
            for (int i = 0; i < 400; i++)
            {
                float r = SeaMath.Rand(SeaMath.CellSeed(7, i % 20, i / 20, 1000), 2);
                Assert.GreaterOrEqual(r, 0f);
                Assert.Less(r, 1f);
                sum += r;
            }
            Assert.AreEqual(0.5f, sum / 400f, 0.06f);
        }

        static float Sail(Vector2 from, Vector2 to, Vector3[] circles, float step, float margin, out bool arrived)
        {
            Vector2 pos = from;
            float closest = float.MaxValue;
            arrived = false;
            for (int i = 0; i < 4000; i++)
            {
                Vector2 want = to - pos;
                if (want.magnitude < 1f) { arrived = true; break; }
                Vector2 dir = SeaMath.SteerAvoid(pos, want, circles, circles.Length, 12f, margin);
                Assert.AreEqual(1f, dir.magnitude, 1e-3f);
                pos += dir * step;
                for (int c = 0; c < circles.Length; c++)
                {
                    float gap = (pos - new Vector2(circles[c].x, circles[c].y)).magnitude - circles[c].z;
                    if (gap < closest) closest = gap;
                }
            }
            return closest;
        }

        [Test]
        public void SteerAvoid_SailsAroundAnIslandInTheWay()
        {
            var circles = new[] { new Vector3(50f, 0.4f, 12f) };
            float closest = Sail(new Vector2(0f, 0f), new Vector2(100f, 0f), circles, 0.1f, 2.5f, out bool arrived);
            Assert.IsTrue(arrived);
            Assert.Greater(closest, 0f);
        }

        [Test]
        public void SteerAvoid_ThreadsAnArchipelagoWithoutTouchingLand()
        {
            var circles = new[]
            {
                new Vector3(30f, 3f, 9f), new Vector3(55f, -14f, 11f), new Vector3(60f, 16f, 8f),
                new Vector3(85f, 2f, 10f), new Vector3(110f, -9f, 7f), new Vector3(0f, 0f, -1000f)
            };
            float closest = Sail(new Vector2(0f, 0f), new Vector2(140f, 0f), circles, 0.12f, 2.5f, out bool arrived);
            Assert.IsTrue(arrived);
            Assert.Greater(closest, 0f);
        }

        [Test]
        public void SteerAvoid_LeavesAClearCourseAlone()
        {
            var circles = new[] { new Vector3(0f, 40f, 10f), new Vector3(-30f, 0f, 10f) };
            Vector2 dir = SeaMath.SteerAvoid(Vector2.zero, new Vector2(3f, 0f), circles, 2, 12f, 2.5f);
            Assert.AreEqual(1f, dir.x, 1e-4f);
            Assert.AreEqual(0f, dir.y, 1e-4f);
        }

        [Test]
        public void SteerAvoid_TurnsOutwardInsideTheMargin()
        {
            var circles = new[] { new Vector3(0f, 0f, 10f) };
            Vector2 pos = new Vector2(10.5f, 0f);
            Vector2 dir = SeaMath.SteerAvoid(pos, new Vector2(-1f, 0f), circles, 1, 12f, 2.5f);
            Assert.Greater(Vector2.Dot(dir, pos.normalized), 0f);
        }

        [Test]
        public void SteerAvoid_ASteppingMoverNeverEntersAChasingCircle()
        {
            // The player island (index 0) runs the boat down at 6 u/s; the boat only makes 1.8 u/s and still
            // has to end up outside once PushOut is applied, like ShipSystem does.
            var circles = new[] { new Vector3(-30f, 0f, 8f) };
            Vector2 boat = Vector2.zero;
            for (int i = 0; i < 600; i++)
            {
                circles[0].x += 6f / 60f;
                Vector2 dir = SeaMath.SteerAvoid(boat, Vector2.right, circles, 1, 12f, 3f);
                boat += dir * (1.8f / 60f);
                boat = SeaMath.PushOut(boat, new Vector2(circles[0].x, circles[0].y), circles[0].z);
                Assert.GreaterOrEqual((boat - new Vector2(circles[0].x, circles[0].y)).magnitude, circles[0].z - 1e-3f);
            }
        }

        [Test]
        public void PushOut_MovesToTheRimAndKeepsOutsidePoints()
        {
            Vector2 c = new Vector2(4f, 4f);
            Vector2 p = SeaMath.PushOut(new Vector2(5f, 4f), c, 3f);
            Assert.AreEqual(3f, (p - c).magnitude, 1e-4f);
            Assert.AreEqual(7f, p.x, 1e-4f);
            Vector2 outside = new Vector2(20f, 4f);
            Assert.AreEqual(outside, SeaMath.PushOut(outside, c, 3f));
            Assert.AreEqual(3f, (SeaMath.PushOut(c, c, 3f) - c).magnitude, 1e-4f);
        }

        [Test]
        public void EdgeFade_GrowsInFromTheRangeEdge()
        {
            Assert.AreEqual(1f, SeaMath.EdgeFade(10f, 75f, 90f));
            Assert.AreEqual(1f, SeaMath.EdgeFade(75f, 75f, 90f));
            Assert.AreEqual(0f, SeaMath.EdgeFade(90f, 75f, 90f));
            Assert.AreEqual(0f, SeaMath.EdgeFade(140f, 75f, 90f));
            float prev = 1f;
            for (float d = 75f; d <= 90f; d += 0.5f)
            {
                float f = SeaMath.EdgeFade(d, 75f, 90f);
                Assert.LessOrEqual(f, prev + 1e-6f);
                prev = f;
            }
        }

        [Test]
        public void Storm_TakesTheSailsDownWithHysteresis()
        {
            Assert.IsFalse(SeaMath.SailsDown(0f, false));
            Assert.IsFalse(SeaMath.SailsDown(0.3f, false));
            Assert.IsTrue(SeaMath.SailsDown(0.5f, false));
            Assert.IsTrue(SeaMath.SailsDown(0.3f, true));
            Assert.IsFalse(SeaMath.SailsDown(0.1f, true));
            Assert.Less(SeaMath.SailsUpStorm, SeaMath.SailsDownStorm);
        }

        [Test]
        public void Night_LightsTheLanternWithHysteresis()
        {
            Assert.IsFalse(SeaMath.LanternOn(0f, false));
            Assert.IsFalse(SeaMath.LanternOn(0.4f, false));
            Assert.IsTrue(SeaMath.LanternOn(0.6f, false));
            Assert.IsTrue(SeaMath.LanternOn(0.4f, true));
            Assert.IsFalse(SeaMath.LanternOn(0.2f, true));
        }

        [Test]
        public void Caps_ClampCounts()
        {
            Assert.AreEqual(0, SeaMath.Capped(-3, 6));
            Assert.AreEqual(4, SeaMath.Capped(4, 6));
            Assert.AreEqual(6, SeaMath.Capped(60, 6));
        }

        [Test]
        public void Swell_StaysWithinUnitAmplitude()
        {
            for (int i = 0; i < 300; i++)
            {
                float h = SeaMath.Swell(new Vector2(i * 1.7f, i * -0.9f), i * 0.31f);
                Assert.LessOrEqual(Mathf.Abs(h), 1.0001f);
            }
        }

        // ---- boats running aground ----

        static readonly SeaKind[] Boats = { SeaKind.RowBoat, SeaKind.FishingBoat, SeaKind.SailBoat, SeaKind.TradingCog };

        [Test]
        public void Beaching_RowingBoatsStrandEasilyTheCogOnlyWhenHitHard()
        {
            for (int i = 0; i + 1 < Boats.Length; i++)
                Assert.Less(SeaMath.BeachClosingSpeed(Boats[i]), SeaMath.BeachClosingSpeed(Boats[i + 1]));
            Assert.IsTrue(SeaMath.ShouldBeach(SeaKind.RowBoat, 0.8f, 0f, 0, 4));
            Assert.IsFalse(SeaMath.ShouldBeach(SeaKind.SailBoat, 0.8f, 0f, 0, 4));
            Assert.IsTrue(SeaMath.ShouldBeach(SeaKind.SailBoat, 2f, 0f, 0, 4));
            Assert.IsFalse(SeaMath.ShouldBeach(SeaKind.TradingCog, 2f, 0f, 0, 4));
            Assert.IsFalse(SeaMath.ShouldBeach(SeaKind.TradingCog, 3.3f, 0f, 0, 4));
            Assert.IsTrue(SeaMath.ShouldBeach(SeaKind.TradingCog, 3.5f, 0f, 0, 4));
            foreach (SeaKind k in Boats)
            {
                Assert.IsFalse(SeaMath.ShouldBeach(k, 0f, 0f, 0, 4), "a resting island strands nobody");
                Assert.IsFalse(SeaMath.ShouldBeach(k, -2f, 10f, 0, 4), "an island moving away strands nobody");
                Assert.Greater(SeaMath.EvadeSpeed(k), SeaMath.BeachClosingSpeed(k), "a boat that is not beached must be able to get clear");
            }
            Assert.IsFalse(SeaMath.ShouldBeach(SeaKind.Dolphin, 50f, 50f, 0, 4));
        }

        [Test]
        public void Beaching_ABoatPinnedAgainstTheShoreStrandsAtAFractionOfTheSpeed()
        {
            foreach (SeaKind k in Boats)
            {
                float slow = SeaMath.BeachClosingSpeed(k) * 0.5f;
                Assert.IsFalse(SeaMath.ShouldBeach(k, slow, 0.5f, 0, 4));
                Assert.IsTrue(SeaMath.ShouldBeach(k, slow, SeaMath.BeachPinnedSeconds + 0.1f, 0, 4));
                Assert.IsFalse(SeaMath.ShouldBeach(k, SeaMath.BeachClosingSpeed(k) * 0.2f, 30f, 0, 4));
            }
        }

        [Test]
        public void Beaching_ClosingSpeedIsMeasuredAlongTheShoreNormal()
        {
            Vector2 outward = Vector2.right;
            Assert.AreEqual(5f, SeaMath.ClosingSpeed(new Vector2(5f, 0f), Vector2.zero, outward), 1e-5f);
            Assert.AreEqual(3.2f, SeaMath.ClosingSpeed(new Vector2(5f, 0f), new Vector2(1.8f, 0f), outward), 1e-5f);
            Assert.AreEqual(0f, SeaMath.ClosingSpeed(new Vector2(0f, 6f), Vector2.zero, outward), 1e-5f, "a glancing pass does not close");
            Assert.Less(SeaMath.ClosingSpeed(new Vector2(-4f, 0f), Vector2.zero, outward), 0f);
            Assert.AreEqual(1f, SeaMath.ClosingSpeed(Vector2.zero, new Vector2(-1f, 0f), outward), 1e-5f, "the boat itself sails into the shore");
        }

        [Test]
        public void Beaching_IsCappedPerIsland()
        {
            for (int n = 0; n < SeaMath.MaxBeachedPerIsland; n++)
                Assert.IsTrue(SeaMath.ShouldBeach(SeaKind.RowBoat, 5f, 0f, n, 4));
            Assert.IsFalse(SeaMath.ShouldBeach(SeaKind.RowBoat, 5f, 0f, SeaMath.MaxBeachedPerIsland, 4));
            Assert.IsFalse(SeaMath.ShouldBeach(SeaKind.RowBoat, 5f, 0f, 4, 99), "the hard cap wins over a larger setting");
            Assert.IsFalse(SeaMath.ShouldBeach(SeaKind.RowBoat, 5f, 0f, 2, 2));
            Assert.IsFalse(SeaMath.ShouldBeach(SeaKind.RowBoat, 5f, 0f, 0, 0));
            Assert.LessOrEqual(SeaMath.MaxBeachedPerIsland, 4);
        }

        [Test]
        public void Beaching_BetweenTwoIslandsTheFasterOneGetsTheBoat()
        {
            Assert.AreEqual(1, SeaMath.FasterHost(1f, 4f));
            Assert.AreEqual(0, SeaMath.FasterHost(4f, 1f));
            Assert.AreEqual(0, SeaMath.FasterHost(2f, 2f));
        }

        [Test]
        public void Beaching_RestingSpotIsTheStripAboveTheWaterlineAndNeverACliff()
        {
            Assert.IsTrue(SeaMath.ValidBeachSpot(0.02f, 0.2f));
            Assert.IsTrue(SeaMath.ValidBeachSpot(0.12f, 0.2f));
            Assert.IsFalse(SeaMath.ValidBeachSpot(-0.1f, 0.2f));
            Assert.IsFalse(SeaMath.ValidBeachSpot(0.3f, 0.2f));
            Assert.IsFalse(SeaMath.ValidBeachSpot(0.07f, SeaMath.BeachSlopeLimit + 0.1f));
            Assert.IsFalse(SeaMath.BeachSpotLost(0.07f, 0.2f, false));
            Assert.IsTrue(SeaMath.BeachSpotLost(0.07f, 0.2f, true), "built over");
            Assert.IsTrue(SeaMath.BeachSpotLost(0.9f, 0.2f, false), "uplifted by a merge");
            Assert.IsTrue(SeaMath.BeachSpotLost(0.07f, SeaMath.BeachSlopeLimit * 1.5f + 0.1f, false), "turned into a cliff");
            Assert.IsFalse(SeaMath.BeachSpotLost(0.2f, 0.5f, false), "a little uplift is tolerated");
        }

        [Test]
        public void Beaching_RefloatsAfterAWhileOrWhenTheSpotGoesUnder()
        {
            Assert.IsFalse(SeaMath.ShouldRefloat(12f, 0.07f, true));
            Assert.IsTrue(SeaMath.ShouldRefloat(0f, 0.07f, true));
            Assert.IsTrue(SeaMath.ShouldRefloat(12f, -0.05f, true), "the island sank under the hull");
            Assert.IsFalse(SeaMath.ShouldRefloat(12f, -0.01f, true));
            Assert.IsTrue(SeaMath.ShouldRefloat(12f, 0.07f, false), "the island is gone");
            for (uint s = 1; s < 200; s++)
            {
                float t = SeaMath.BeachSeconds(s * 7919u);
                Assert.GreaterOrEqual(t, 20f);
                Assert.LessOrEqual(t, 60f);
            }
        }

        [Test]
        public void Beaching_HullLiesOnItsSideLeaningDownhill()
        {
            for (uint s = 1; s < 200; s++)
            {
                float deg = SeaMath.BeachRoll(s * 104729u, s % 3u == 0u ? 5f : 0.1f, false) * Mathf.Rad2Deg;
                Assert.LessOrEqual(deg, -18f + 1e-3f);
                Assert.GreaterOrEqual(deg, -35f - 1e-3f);
                Assert.AreEqual(-deg, SeaMath.BeachRoll(s * 104729u, s % 3u == 0u ? -5f : -0.1f, false) * Mathf.Rad2Deg, 1e-3f);
                if (s % 3u == 0u) Assert.AreEqual(-35f, deg, 1e-3f, "a steep bank tips the hull as far as it goes");
                float cog = Mathf.Abs(SeaMath.BeachRoll(s * 104729u, 1f, true)) * Mathf.Rad2Deg;
                Assert.GreaterOrEqual(cog, 18f - 1e-3f);
                Assert.LessOrEqual(cog, 24f + 1e-3f);
            }
        }

        static Vector2 Heading(float yawDeg) => new Vector2(Mathf.Sin(yawDeg * Mathf.Deg2Rad), Mathf.Cos(yawDeg * Mathf.Deg2Rad));

        [Test]
        public void Riding_TheHullFollowsTheIslandsTranslationAndBodyRotation()
        {
            Vector2 islandPos = new Vector2(12f, -7f);
            Vector2 fwd = Heading(30f);
            Vector2 hullWorld = new Vector2(15f, -2f);
            Vector2 hullDir = Heading(100f);
            Vector2 local = SeaMath.ToBodyLocal(hullWorld, islandPos, fwd);
            Vector2 localDir = SeaMath.DirToBodyLocal(hullDir, fwd);
            Assert.AreEqual(0f, (SeaMath.FromBodyLocal(local, islandPos, fwd) - hullWorld).magnitude, 1e-4f);
            Assert.AreEqual(0f, (SeaMath.DirFromBodyLocal(localDir, fwd) - hullDir).magnitude, 1e-5f);

            Vector2 moved = islandPos + new Vector2(40f, 25f);
            Assert.AreEqual(0f, (SeaMath.FromBodyLocal(local, moved, fwd) - (hullWorld + new Vector2(40f, 25f))).magnitude, 1e-4f);

            // A merge turns the body by 90 degrees clockwise: the hull swings round the island centre with it.
            Vector2 turned = Heading(120f);
            Vector2 after = SeaMath.FromBodyLocal(local, moved, turned);
            Vector2 before = hullWorld - islandPos;
            Assert.AreEqual(before.magnitude, (after - moved).magnitude, 1e-4f);
            Assert.AreEqual(0f, ((after - moved) - new Vector2(before.y, -before.x)).magnitude, 1e-4f);
            Vector2 dirAfter = SeaMath.DirFromBodyLocal(localDir, turned);
            Assert.AreEqual(1f, dirAfter.magnitude, 1e-5f);
            Assert.AreEqual(0f, (dirAfter - Heading(190f)).magnitude, 1e-4f);
            Assert.AreEqual(local.x, SeaMath.ToBodyLocal(after, moved, turned).x, 1e-4f);
            Assert.AreEqual(local.y, SeaMath.ToBodyLocal(after, moved, turned).y, 1e-4f);
        }

        [Test]
        public void Riding_BodySpaceMatchesTheIslandConvention()
        {
            // Island.ToWorld: pos + bodyRight * x + bodyFwd * y with bodyRight = (fwd.y, -fwd.x).
            Vector2 fwd = Vector2.up;
            Assert.AreEqual(new Vector2(1f, 0f), SeaMath.BodyRight(fwd));
            Assert.AreEqual(new Vector2(3f, 5f), SeaMath.FromBodyLocal(new Vector2(3f, 5f), Vector2.zero, fwd));
            Vector2 east = Vector2.right;
            Assert.AreEqual(0f, (SeaMath.FromBodyLocal(new Vector2(3f, 5f), Vector2.zero, east) - new Vector2(5f, -3f)).magnitude, 1e-5f);
        }

        [Test]
        public void Riding_DockTiedBoatsStayTiedWhileTheIslandIsUnderWayAndCastOffLater()
        {
            Assert.IsFalse(SeaMath.StaysTied(0f, 0f));
            Assert.IsFalse(SeaMath.StaysTied(0.4f, 5f));
            Assert.IsTrue(SeaMath.StaysTied(3f, 0f));
            Assert.IsTrue(SeaMath.StaysTied(3f, SeaMath.TiedMaxOverdue - 1f));
            Assert.IsFalse(SeaMath.StaysTied(3f, SeaMath.TiedMaxOverdue + 1f), "nobody stays tied for ever");

            // The tied boat keeps its place at the jetty through a run and a turn of the island.
            Vector2 pos = Vector2.zero, fwd = Vector2.up;
            Vector2 jetty = new Vector2(6f, 1f);
            Vector2 local = SeaMath.ToBodyLocal(jetty + new Vector2(1.3f, 0f), pos, fwd);
            for (int i = 0; i < 300; i++)
            {
                pos += new Vector2(0.05f, 0.08f);
                fwd = Heading(i * 0.3f);
                Vector2 boat = SeaMath.FromBodyLocal(local, pos, fwd);
                Vector2 jettyNow = SeaMath.FromBodyLocal(new Vector2(6f, 1f), pos, fwd);
                Assert.AreEqual(1.3f, (boat - jettyNow).magnitude, 1e-3f);
            }
        }

        // ---- whales ----

        [Test]
        public void WhalePods_AreSmallGroupsAndOftenBringACalf()
        {
            int calves = 0, total = 0;
            var sizes = new int[5];
            for (uint i = 1; i <= 600; i++)
            {
                uint h = SeaMath.Hash(23u, i);
                int n = SeaMath.WhalePodSize(h, out bool calf);
                Assert.GreaterOrEqual(n, 2);
                Assert.LessOrEqual(n, 4);
                Assert.AreEqual(n, SeaMath.WhalePodSize(h, out bool again));
                Assert.AreEqual(calf, again);
                sizes[n]++;
                total++;
                if (calf) calves++;
            }
            Assert.Greater(sizes[2], 0);
            Assert.Greater(sizes[3], 0);
            Assert.Greater(sizes[4], 0);
            Assert.AreEqual(0.6f, calves / (float)total, 0.08f);
        }

        [Test]
        public void WhalePods_TravelInLooseFormationWithTheCalfAtItsMothersFlank()
        {
            for (uint i = 1; i <= 300; i++)
            {
                uint h = SeaMath.Hash(77u, i);
                int n = SeaMath.WhalePodSize(h, out bool calf);
                Assert.AreEqual(Vector2.zero, SeaMath.WhalePodOffset(0, n, calf, h));
                for (int a = 0; a < n; a++)
                {
                    Vector2 pa = SeaMath.WhalePodOffset(a, n, calf, h);
                    Assert.AreEqual(pa, SeaMath.WhalePodOffset(a, n, calf, h), "deterministic");
                    Assert.LessOrEqual(pa.magnitude, 2.6f, "the pod stays together (whale lengths)");
                    Assert.LessOrEqual(pa.y, 0f, "nobody swims ahead of the leader");
                    for (int b = a + 1; b < n; b++)
                    {
                        Vector2 pb = SeaMath.WhalePodOffset(b, n, calf, h);
                        float d = (pa - pb).magnitude;
                        bool pair = a == 0 && SeaMath.IsWhaleCalf(b, n, calf);
                        if (pair)
                        {
                            Assert.GreaterOrEqual(d, 0.3f, "the calf does not swim inside its mother");
                            Assert.LessOrEqual(d, 0.6f, "the calf stays at her flank");
                            Assert.Greater(Mathf.Abs(pb.x), Mathf.Abs(pb.y), "beside her, not behind");
                        }
                        else Assert.GreaterOrEqual(d, 0.8f, "adults keep about a body length apart");
                    }
                }
            }
            Assert.IsFalse(SeaMath.IsWhaleCalf(0, 2, true), "the mother is never the calf");
            Assert.IsTrue(SeaMath.IsWhaleCalf(1, 2, true));
            Assert.IsFalse(SeaMath.IsWhaleCalf(1, 2, false));
        }

        [Test]
        public void WhalePods_BreatheOneAfterTheOther()
        {
            Assert.AreEqual(0f, SeaMath.WhaleBreathLag(0, 4, true));
            Assert.Greater(SeaMath.WhaleBreathLag(1, 4, false), 0.5f);
            Assert.Greater(SeaMath.WhaleBreathLag(2, 4, false), SeaMath.WhaleBreathLag(1, 4, false));
            float calfLag = SeaMath.WhaleBreathLag(3, 4, true);
            Assert.Greater(calfLag, 0f);
            Assert.Less(calfLag, SeaMath.WhaleBreathLag(1, 4, true), "the calf comes up right after its mother");
        }

        [Test]
        public void Whales_SpawnCapsAreTwoPodsAndTwoLoners()
        {
            Assert.AreEqual(2, SeaMath.MaxWhalePodsInRange);
            Assert.AreEqual(2, SeaMath.MaxWhaleLonersInRange);
            Assert.IsTrue(SeaMath.WhaleSpawnAllowed(true, 0, 0, 2, 2));
            Assert.IsTrue(SeaMath.WhaleSpawnAllowed(true, 1, 2, 2, 2), "loners do not use up pod slots");
            Assert.IsFalse(SeaMath.WhaleSpawnAllowed(true, 2, 0, 2, 2));
            Assert.IsTrue(SeaMath.WhaleSpawnAllowed(false, 2, 1, 2, 2));
            Assert.IsFalse(SeaMath.WhaleSpawnAllowed(false, 0, 2, 2, 2));
            Assert.IsFalse(SeaMath.WhaleSpawnAllowed(true, 2, 0, 9, 9), "the hard cap wins over a larger setting");
            Assert.IsFalse(SeaMath.WhaleSpawnAllowed(false, 0, 2, 9, 9));
            Assert.IsFalse(SeaMath.WhaleSpawnAllowed(true, 0, 0, 0, 2));
            Assert.IsFalse(SeaMath.WhaleSpawnAllowed(false, 0, 0, 2, 0));
        }

        [Test]
        public void Whales_BullsOnlyBreachInCalmWaterWhereThePlayerCanSeeIt()
        {
            Assert.IsTrue(SeaMath.BreachAllowed(0.1f, 40f, true));
            Assert.IsFalse(SeaMath.BreachAllowed(0.5f, 40f, true), "storm");
            Assert.IsFalse(SeaMath.BreachAllowed(0.1f, 10f, true), "too close");
            Assert.IsFalse(SeaMath.BreachAllowed(0.1f, 95f, true), "too far to be seen");
            Assert.IsFalse(SeaMath.BreachAllowed(0.1f, 40f, false), "something in the way");
            Assert.GreaterOrEqual(SeaMath.WhaleClearance, 12f);
        }

        [Test]
        public void GermanNames_CoverEveryKind()
        {
            Assert.AreEqual("Delfin", SeaNames.German(SeaKind.Dolphin));
            Assert.AreEqual("Wal", SeaNames.German(SeaKind.Whale));
            Assert.AreEqual("Meeresschildkröte", SeaNames.German(SeaKind.SeaTurtle));
            Assert.AreEqual("Qualle", SeaNames.German(SeaKind.Jellyfish));
            Assert.AreEqual("Rochen", SeaNames.German(SeaKind.Ray));
            Assert.AreEqual("Segelboot", SeaNames.German(SeaKind.SailBoat));
            Assert.AreEqual("Fischerboot", SeaNames.German(SeaKind.FishingBoat));
            Assert.AreEqual("Handelsschiff", SeaNames.German(SeaKind.TradingCog));
            foreach (SeaKind k in System.Enum.GetValues(typeof(SeaKind)))
                Assert.AreNotEqual(k.ToString(), SeaNames.German(k));
        }
    }
}
