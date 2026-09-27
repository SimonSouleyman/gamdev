using System.Collections.Generic;
using Drift.Islands;
using Drift.Tectonics;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class RingWorldTests
    {
        const float C = 640f;
        const float HW = 40f;
        static readonly RingGeometry Ring = new RingGeometry(0f, HW, C);

        readonly List<GameObject> _objects = new();
        PlateSystem _previousPlates;

        [SetUp]
        public void SetUp() => _previousPlates = PlateSystem.Instance;

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
            if (_previousPlates != null)
            {
                _previousPlates.enabled = false;
                _previousPlates.enabled = true;
            }
        }

        // ---- wrap / clamp / bend maths ----

        [Test]
        public void AlongDelta_TakesTheShortWayRound()
        {
            Assert.AreEqual(-10f, Ring.AlongDelta(630f, 0f), 1e-3f);
            Assert.AreEqual(10f, Ring.AlongDelta(0f, 630f), 1e-3f);
            Assert.AreEqual(310f, Ring.AlongDelta(-330f, 0f), 1e-3f);
            Assert.AreEqual(25f, Ring.AlongDelta(5000f + 25f + 3f * C, 5000f), 1e-2f);
            for (float z = -2000f; z < 2000f; z += 37.3f)
            {
                float d = Ring.AlongDelta(z, 123f);
                Assert.GreaterOrEqual(d, -C * 0.5f - 1e-3f);
                Assert.Less(d, C * 0.5f + 1e-3f);
                Assert.AreEqual(0f, Mathf.Repeat(z - 123f - d + 0.5f * C, C) - 0.5f * C, 1e-2f, "differs by whole laps");
            }
        }

        [Test]
        public void WrapNear_IsTheSameForEveryLap()
        {
            float a = Ring.WrapNear(77f, 1000f);
            for (int k = -3; k <= 3; k++) Assert.AreEqual(a, Ring.WrapNear(77f + k * C, 1000f), 1e-2f);
            Assert.LessOrEqual(Mathf.Abs(a - 1000f), C * 0.5f);
        }

        [Test]
        public void ClampAcross_KeepsTheBandAndTheAlongCoordinate()
        {
            var inside = new Vector2(12f, 5000f);
            Assert.AreEqual(inside, Ring.ClampAcross(inside, 3f));
            Vector2 right = Ring.ClampAcross(new Vector2(90f, -777f), 5f);
            Assert.AreEqual(HW - 5f, right.x, 1e-4f);
            Assert.AreEqual(-777f, right.y);
            Vector2 left = Ring.ClampAcross(new Vector2(-41f, 3f), 0f);
            Assert.AreEqual(-HW, left.x, 1e-4f);
            // A huge island still gets a strip to sit on.
            Vector2 huge = Ring.ClampAcross(new Vector2(30f, 0f), 500f);
            Assert.AreEqual(HW * 0.1f, huge.x, 1e-3f);
            Assert.IsTrue(Ring.Inside(huge, 0f));
        }

        [Test]
        public void Bend_IsTheIdentityAtTheFocusAndClosesTheRing()
        {
            var focus = new Vector2(3f, 250f);
            float r = Ring.Radius;
            Assert.AreEqual(C / (2f * Mathf.PI), r, 1e-3f);

            var atFocus = new Vector3(-7f, 2f, 250f);
            Assert.Less((Ring.Bend(atFocus, focus) - atFocus).magnitude, 1e-4f);

            // Close by: the sea rises by ~dz^2 / 2R, the classic inside-of-a-ring look.
            Vector3 near = Ring.Bend(new Vector3(0f, 0f, 250f + 20f), focus);
            Assert.AreEqual(20f * 20f / (2f * r), near.y, 0.05f);
            Assert.Greater(near.y, 0f);

            // A quarter round: up at the height of the axis, one radius ahead; half round: the top of the ring.
            Vector3 quarter = Ring.Bend(new Vector3(0f, 0f, 250f + C * 0.25f), focus);
            Assert.AreEqual(r, quarter.y, 1e-2f);
            Assert.AreEqual(250f + r, quarter.z, 1e-2f);
            Vector3 ahead = Ring.Bend(new Vector3(5f, 0f, 250f + C * 0.5f), focus);
            Vector3 behind = Ring.Bend(new Vector3(5f, 0f, 250f - C * 0.5f), focus);
            Assert.Less((ahead - behind).magnitude, 1e-2f, "both ways meet at the antipode");
            Assert.AreEqual(2f * r, ahead.y, 1e-2f);

            // Height points at the axis: something standing up on the far side hangs down.
            Vector3 mast = Ring.Bend(new Vector3(0f, 4f, 250f + C * 0.5f), focus);
            Assert.AreEqual(2f * r - 4f, mast.y, 1e-2f);

            // Nothing is bent across inside the band; the world ends at the rims, so everything beyond a lip is
            // folded onto it and the sea closes into the edge - there is no curtain hanging below the band.
            Assert.AreEqual(17f, Ring.Bend(new Vector3(17f, 0f, 400f), focus).x, 1e-4f);
            Vector3 lip = Ring.Bend(new Vector3(HW + 1f, 0f, 250f), focus);
            Assert.AreEqual(HW, lip.x, 1e-3f, "just past the lip lands on it");
            Assert.AreEqual(0f, lip.y, 1e-3f, "and stays at sea level - nothing falls");
            Vector3 far = Ring.Bend(new Vector3(300f, 0f, 250f), focus);
            Assert.AreEqual(HW, far.x, 1e-3f, "the far sea folds onto the rim");
            Assert.AreEqual(0f, far.y, 1e-3f);
            Assert.AreEqual(-HW, Ring.Bend(new Vector3(-300f, 0f, 250f), focus).x, 1e-3f, "the other rim likewise");
        }

        // ---- plates ----

        PlateSystem MakePlates(int seed)
        {
            var focus = new GameObject("RingTestFocus");
            _objects.Add(focus);
            var go = new GameObject("RingTestPlates");
            go.SetActive(false);
            var ps = go.AddComponent<PlateSystem>();
            ps.seed = seed;
            ps.showBorders = false;
            ps.focus = focus.transform;
            go.SetActive(true);
            _objects.Add(go);
            return ps;
        }

        [Test]
        public void RingPlates_AreSurfLanesAlongTheTrack()
        {
            var ps = MakePlates(4242);
            ps.SetRingLayout(0f, HW, C);
            Assert.IsTrue(ps.RingLayout);
            Assert.AreEqual(0f, Mathf.Repeat(C / ps.RingCellLength + 0.5f, 1f) - 0.5f, 1e-3f, "whole plates per lap");

            float alongLength = 0f, acrossLength = 0f;
            int along = 0;
            foreach (var b in ps.Borders)
            {
                Assert.GreaterOrEqual(Mathf.Min(b.p0.x, b.p1.x), -HW - 0.01f, "clipped to the band");
                Assert.LessOrEqual(Mathf.Max(b.p0.x, b.p1.x), HW + 0.01f, "clipped to the band");
                Vector2 d = b.p1 - b.p0;
                float len = d.magnitude;
                if (len < 1f) continue;
                d /= len;
                if (Mathf.Abs(d.y) > 0.75f) { along++; alongLength += len; }
                else if (Mathf.Abs(d.x) > 0.75f) acrossLength += len;
            }
            Assert.Greater(along, 1, "boundaries running along the track (surf lanes)");
            Assert.Greater(alongLength, 200f);
            // The design changed with the obstacle round: the lanes run along the track, the only pieces across it
            // are the short jogs where a lane steps sideways.
            Assert.Greater(alongLength, 4f * acrossLength, "mostly along the driving direction");
        }

        [Test]
        public void RingLanes_MoveSidewaysOverTime()
        {
            var ps = MakePlates(31337);
            ps.SetRingLayout(0f, HW, C);
            float first = LaneBoundaryX(ps, 0f);
            float moved = 0f;
            for (int i = 0; i < 40; i++)
            {
                ps.Step(1f);
                moved = Mathf.Max(moved, Mathf.Abs(LaneBoundaryX(ps, 0f) - first));
            }
            Assert.Greater(moved, 2f, "the good line has to change over a run");
            Assert.Less(moved, HW, "but the lanes stay on the band");
        }

        // The nearest along-track boundary to (0, z).
        static float LaneBoundaryX(PlateSystem ps, float z)
        {
            float best = float.MaxValue, x = 0f;
            foreach (var b in ps.Borders)
            {
                Vector2 d = b.p1 - b.p0;
                if (d.magnitude < 5f || Mathf.Abs(d.normalized.y) < 0.75f) continue;
                float lo = Mathf.Min(b.p0.y, b.p1.y), hi = Mathf.Max(b.p0.y, b.p1.y);
                if (z < lo - 5f || z > hi + 5f) continue;
                float mid = (b.p0.x + b.p1.x) * 0.5f;
                if (Mathf.Abs(mid) >= best) continue;
                best = Mathf.Abs(mid);
                x = mid;
            }
            return x;
        }

        [Test]
        public void RingLaneCurrents_RunAlongTheTrackAndDifferBetweenLanes()
        {
            var ps = MakePlates(777);
            ps.SetRingLayout(0f, HW, C);
            ps.Step(5f);
            float spread = 0f;
            float lo = float.MaxValue, hi = float.MinValue;
            for (float x = -HW + 4f; x <= HW - 4f; x += 2f)
            {
                Vector2 v = ps.SampleVelocity(new Vector2(x, 20f));
                // Only a tectonic event kick can push a lane sideways at all; its own current runs along the ring.
                Assert.Less(Mathf.Abs(v.x), 0.35f * ps.ringFlowSpeed, "the current runs along the ring");
                lo = Mathf.Min(lo, v.y);
                hi = Mathf.Max(hi, v.y);
            }
            spread = hi - lo;
            Assert.Greater(spread, 0.3f, "the lanes slide past each other");
            Assert.LessOrEqual(hi, ps.ringFlowSpeed + 0.5f);
        }

        [Test]
        public void RingPlates_RepeatEveryLapAndGiveASurfLane()
        {
            var ps = MakePlates(99);
            ps.SetRingLayout(0f, HW, C);
            for (float z = -300f; z <= 300f; z += 41f)
                for (float x = -35f; x <= 35f; x += 14f)
                {
                    var p = new Vector2(x, z);
                    Assert.Less((ps.SampleVelocity(p) - ps.SampleVelocity(p + new Vector2(0f, C))).magnitude, 1e-4f);
                    Assert.Less((ps.SampleVelocity(p) - ps.SampleVelocity(p - new Vector2(0f, 2f * C))).magnitude, 1e-4f);
                }

            // Driving along the longest along-track boundary surfs it.
            PlateSystem.Border? lane = null;
            float best = 0f;
            foreach (var b in ps.Borders)
            {
                Vector2 d = b.p1 - b.p0;
                if (Mathf.Abs(d.normalized.y) < 0.75f || d.magnitude <= best) continue;
                best = d.magnitude;
                lane = b;
            }
            Assert.IsTrue(lane.HasValue);
            var l = lane.Value;
            Vector2 mid = ps.SeamPoint(l, 0.5f);
            Vector2 heading = ps.SeamTangent(l, 0.5f);
            ps.SurfVelocity(mid, heading, 3f, 1f, out float strength);
            Assert.Greater(strength, 0.3f);
        }

        // The owner asked for wider lanes with more push ("die ströme ... sollen breiter sein und ein bisschen mehr
        // boost geben"): in the ring a lane is ringSurfWidth wide and pushes ringSurfBoost times as hard, cozy keeps
        // the narrow surfWidth and the plain surfSpeed.
        [Test]
        public void RingSurfLanes_AreWiderAndPushHarderThanTheCozyOnes()
        {
            var ps = MakePlates(99);
            Assert.Greater(ps.ringSurfWidth, ps.surfWidth, "wider in the ring");
            Assert.Greater(ps.ringSurfBoost, 1f, "and it pushes harder");
            ps.SetRingLayout(0f, HW, C);
            ps.Step(5f);

            PlateSystem.Border? lane = null;
            float best = 0f;
            foreach (var b in ps.Borders)
            {
                Vector2 d = b.p1 - b.p0;
                if (Mathf.Abs(d.normalized.y) < 0.75f || d.magnitude <= best) continue;
                best = d.magnitude;
                lane = b;
            }
            Assert.IsTrue(lane.HasValue);
            var l = lane.Value;
            Vector2 mid = ps.SeamPoint(l, 0.5f);
            Vector2 heading = ps.SeamTangent(l, 0.5f);
            Vector2 side = new Vector2(heading.y, -heading.x);

            // On the seam: a real push along the track.
            Vector2 on = ps.SurfVelocity(mid, heading, 3f, 1f, out float onStrength);
            Assert.Greater(Vector2.Dot(on, heading), 3f, "riding the lane is clearly worth it: " + on);

            // Half the new width away it still carries - the wide lane is easy to find and to hold - but the seam
            // itself stays the best line, so it is still a lane and not the whole band.
            float off = ps.ringSurfWidth * 0.5f;
            ps.SurfVelocity(mid + side * off, heading, 3f, 1f, out float nearStrength);
            Assert.Greater(nearStrength, 0.3f * onStrength, "the wide lane still carries " + off + " u off the seam");
            Assert.Less(nearStrength, onStrength, "but the seam is still the best line");
            // Where the old narrow lane had already given up (surfWidth + the island's share), the wide one carries.
            float wasOut = ps.surfWidth + ps.surfRadiusShare * 3f + 0.5f;
            ps.SurfVelocity(mid + side * wasOut, heading, 3f, 1f, out float stillOn);
            Assert.Greater(stillOn, 0f, wasOut + " u off the seam would have been outside the cozy lane");
        }

        [Test]
        public void ClearingTheRing_RestoresTheCozyLayout()
        {
            var cozy = MakePlates(777);
            int count = cozy.Borders.Count;
            Vector2 first = cozy.Borders[0].p0;
            cozy.SetRingLayout(0f, HW, C);
            cozy.ClearRingLayout();
            Assert.IsFalse(cozy.RingLayout);
            Assert.AreEqual(count, cozy.Borders.Count);
            Assert.AreEqual(first, cozy.Borders[0].p0);
        }

        // ---- island supply ----

        (RingIslandSpawner spawner, Island player) MakeRing(Vector2 playerPos)
        {
            var pgo = new GameObject("RingTestPlayer");
            pgo.SetActive(false);
            pgo.transform.position = new Vector3(playerPos.x, 0f, playerPos.y);
            var player = pgo.AddComponent<Island>();
            player.useKeyboardInput = true;
            player.landRadius = 3f;
            pgo.SetActive(true);
            _objects.Add(pgo);

            var go = new GameObject("RingTestSpawner");
            go.SetActive(false);
            var spawner = go.AddComponent<RingIslandSpawner>();
            spawner.life = false;
            spawner.mainIslands = 10;
            spawner.islets = 4;
            go.SetActive(true);
            _objects.Add(go);
            return (spawner, player);
        }

        static List<Island> Islands(RingIslandSpawner s)
        {
            var list = new List<Island>();
            s.CollectIslands(list);
            return list;
        }

        [Test]
        public void Spawner_FillsTheWholeRingInsideTheBand()
        {
            var start = new Vector2(0f, 1200f);
            var (spawner, _) = MakeRing(start);
            spawner.ResetRing(Ring, start, 5, true);
            var islands = Islands(spawner);
            Assert.AreEqual(spawner.TargetCount, islands.Count);
            Assert.AreEqual(0, spawner.PendingCount);

            int ahead = 0, behind = 0;
            foreach (var isl in islands)
            {
                Vector2 p = isl.PlanarPosition;
                Assert.IsTrue(Ring.Inside(p, 0f), "inside the band: " + p);
                float dz = Ring.AlongDelta(p.y, start.y);
                Assert.AreEqual(p.y - start.y, dz, 1e-2f, "planted within half a lap of the player");
                Assert.Greater(new Vector2(p.x - start.x, dz).magnitude, spawner.startExclusion, "the start stays open");
                if (dz > 0f) ahead++; else behind++;
            }
            Assert.Greater(ahead, 2, "spread round the ring");
            Assert.Greater(behind, 2, "spread round the ring");
        }

        [Test]
        public void Spawner_ReplacesMergedIslandsFarFromThePlayer()
        {
            var start = new Vector2(0f, 0f);
            var (spawner, _) = MakeRing(start);
            spawner.ResetRing(Ring, start, 11, true);
            var before = Islands(spawner);
            var kept = new HashSet<Island>(before);
            for (int i = 0; i < 4; i++)
            {
                kept.Remove(before[i]);
                Object.DestroyImmediate(before[i].gameObject);
            }

            var here = new Vector2(10f, 3000f);
            spawner.Step(Ring, here, true);
            var after = Islands(spawner);
            Assert.AreEqual(spawner.TargetCount, after.Count, "never runs out");
            int fresh = 0;
            foreach (var isl in after)
            {
                Assert.IsTrue(Ring.Inside(isl.PlanarPosition, 0f));
                if (kept.Contains(isl)) continue;
                fresh++;
                float dz = Mathf.Abs(Ring.AlongDelta(isl.PlanarPosition.y, here.y));
                Assert.GreaterOrEqual(dz, spawner.respawnMinDistance * C - 0.01f, "replacements rise far away");
            }
            Assert.AreEqual(4, fresh);
            Assert.AreEqual(spawner.TargetCount + 4, spawner.TotalSpawned);
        }
    }
}
