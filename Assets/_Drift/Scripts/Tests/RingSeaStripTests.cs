using System.Collections.Generic;
using Drift.Islands;
using Drift.Tectonics;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The adventure sea as a strip between the rims, and which plate seams the ring draws.
    public class RingSeaStripTests
    {
        static readonly RingGeometry Ring = new RingGeometry(0f, 28f, 640f);

        static WaterFollower.RingStripLayout Layout(RingGeometry g, float col = 3f, float row = 3.2f) =>
            WaterFollower.RingStrip(2f * g.halfWidth, g.circumference, col, row);

        [Test]
        public void Strip_ColumnsSitExactlyOnBothRims()
        {
            var l = Layout(Ring);
            var verts = new List<Vector3>();
            var tris = new List<int>();
            WaterFollower.BuildRingStrip(l, Ring.halfWidth, 1f, 1f, verts, tris);
            float min = float.MaxValue, max = float.MinValue;
            foreach (var v in verts)
            {
                min = Mathf.Min(min, v.x);
                max = Mathf.Max(max, v.x);
            }
            Assert.AreEqual(-Ring.halfWidth, min, 0f, "left rim column");
            Assert.AreEqual(Ring.halfWidth, max, 0f, "right rim column");
            int stride = l.columns + 1;
            for (int k = 0; k <= l.Rows; k++)
            {
                Assert.AreEqual(-Ring.halfWidth, verts[k * stride].x, 0f);
                Assert.AreEqual(Ring.halfWidth, verts[k * stride + l.columns].x, 0f);
            }
            Assert.AreEqual(19, l.columns, "about 3 u across a 56 u band");
            Assert.LessOrEqual(l.columnStep, 3f + 1e-4f);
        }

        [Test]
        public void Strip_RowsAreCloseAndCoverHalfALapAroundAnyFocus()
        {
            foreach (float c in new[] { 300f, 640f, 777f, 1500f })
            {
                var g = new RingGeometry(0f, 28f, c);
                var l = Layout(g);
                Assert.LessOrEqual(l.rowStep, 3.2f + 1e-4f, $"C={c}");
                Assert.LessOrEqual(l.rowStep, 4f);
                float perLap = c / l.rowStep;
                Assert.AreEqual(Mathf.Round(perLap), perLap, 1e-3f, "whole rows per lap");
                // The snapped centre is at most half a row from the focus; the strip still reaches half a lap.
                Assert.GreaterOrEqual(l.halfRows * l.rowStep - 0.5f * l.rowStep, 0.5f * c - 1e-3f, $"C={c}");
                for (float fz = -1234.5f; fz < 1300f; fz += 97.3f)
                    Assert.LessOrEqual(Mathf.Abs(WaterFollower.RingSnapZ(fz, l.rowStep) - fz), 0.5f * l.rowStep + 1e-3f);
            }
            var def = Layout(Ring);
            Assert.AreEqual(200f, 640f / def.rowStep, 1e-3f);
            Assert.Less(def.VertexCount, 65000, "16-bit indices");
        }

        [Test]
        public void Strip_RowsLandOnTheSameWorldZEveryLap()
        {
            var l = Layout(Ring);
            float a = WaterFollower.RingSnapZ(123.4f, l.rowStep);
            float b = WaterFollower.RingSnapZ(123.4f + Ring.circumference, l.rowStep);
            Assert.AreEqual(Ring.circumference, b - a, 1e-3f);
            Assert.AreEqual(0f, Mathf.Repeat(a / l.rowStep + 0.5f, 1f) - 0.5f, 1e-4f);
        }

        [Test]
        public void Strip_FacesLookUpAndScaleIsCompensated()
        {
            var l = Layout(Ring);
            var verts = new List<Vector3>();
            var tris = new List<int>();
            WaterFollower.BuildRingStrip(l, Ring.halfWidth, 1f / 100f, 1f / 100f, verts, tris);
            Assert.AreEqual(l.VertexCount, verts.Count);
            Assert.AreEqual(l.columns * l.Rows * 6, tris.Count);
            for (int t = 0; t < tris.Count; t += 3)
            {
                Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                Assert.Greater(n.y, 0f, "clockwise from above = facing up");
            }
            Assert.AreEqual(Ring.halfWidth / 100f, verts[l.columns].x, 1e-6f);
        }

        [Test]
        public void Strip_BentCellsStayOnTheRingSurface()
        {
            // The bug: a flat cell 24-48 u long is a chord of the ring circle and sagged metres off it. With rows
            // <= 3.2 u apart the chord's midpoint stays within a few centimetres of the bent surface.
            var l = Layout(Ring);
            Vector2 focus = new Vector2(0f, 10f);
            float r = Ring.Radius;
            float worst = 0f;
            for (float dz = -300f; dz < 300f; dz += l.rowStep)
            {
                Vector3 p = Ring.Bend(new Vector3(Ring.MaxX, 0f, focus.y + dz), focus);
                Vector3 q = Ring.Bend(new Vector3(Ring.MaxX, 0f, focus.y + dz + l.rowStep), focus);
                Vector3 mid = (p + q) * 0.5f;
                Vector3 axis = new Vector3(mid.x, r, focus.y);
                worst = Mathf.Max(worst, r - (mid - axis).magnitude);
            }
            Assert.Less(worst, 0.05f);
        }

        [Test]
        public void RingSeams_OnlyTheOnesAlongTheTrackAreDrawn()
        {
            Assert.IsTrue(PlateSystem.RingSeamRunsAlong(new Vector2(0f, 0f), new Vector2(0f, 160f)));
            Assert.IsTrue(PlateSystem.RingSeamRunsAlong(new Vector2(5f, 0f), new Vector2(-3f, 90f)), "a lane that drifts sideways");
            Assert.IsTrue(PlateSystem.RingSeamRunsAlong(new Vector2(0f, 0f), new Vector2(10f, 20f)), "27 degrees off the track");
            Assert.IsFalse(PlateSystem.RingSeamRunsAlong(new Vector2(-28f, 80f), new Vector2(28f, 82f)), "straight across");
            Assert.IsFalse(PlateSystem.RingSeamRunsAlong(new Vector2(4f, 80f), new Vector2(16f, 76f)), "a lane jog");
            Assert.IsFalse(PlateSystem.RingSeamRunsAlong(new Vector2(0f, 0f), new Vector2(10f, 10f)), "45 degrees");
            Assert.IsFalse(PlateSystem.RingSeamRunsAlong(Vector2.one, Vector2.one), "degenerate");
        }

        [Test]
        public void SeamFade_RingFadesSoonerCozyIsUnchanged()
        {
            Vector2 cozy = PlateSystem.SeamFade(false, 120f, 130f, 8f, 85f, 0.45f);
            Assert.AreEqual(120f, cozy.y, 1e-4f);
            Assert.AreEqual(78f, cozy.x, 1e-4f);
            Vector2 ring = PlateSystem.SeamFade(true, 120f, 130f, 8f, 85f, 0.45f);
            Assert.AreEqual(85f, ring.y, 1e-4f);
            Assert.AreEqual(85f * 0.45f, ring.x, 1e-4f);
            Assert.Less(ring.x, ring.y);
            Vector2 small = PlateSystem.SeamFade(true, 50f, 130f, 8f, 85f, 0.45f);
            Assert.AreEqual(50f, small.y, 1e-4f, "never further than the cozy fade");
        }

        [Test]
        public void RingLayout_DrawnSeamsRunAlongAndCrossingBordersStillExist()
        {
            var go = new GameObject("RingSeamPlates");
            go.SetActive(false);
            var prev = PlateSystem.Instance;
            try
            {
                var ps = go.AddComponent<PlateSystem>();
                ps.showBorders = false;
                go.SetActive(true);
                ps.SetRingLayout(0f, 28f, 640f);
                int along = 0, across = 0;
                // Sample the lane shift over a few minutes: at some point the lanes wander apart and jog.
                for (int s = 0; s < 40; s++)
                {
                    ps.Step(6f);
                    foreach (var b in ps.Borders)
                    {
                        if (PlateSystem.RingSeamRunsAlong(b.p0, b.p1)) along++;
                        else across++;
                    }
                }
                Assert.Greater(along, 0, "lane boundaries along the track are drawn");
                Assert.Greater(across, 0, "the lane jogs exist as borders (plate logic untouched)");
            }
            finally
            {
                Object.DestroyImmediate(go);
                if (prev != null && prev != PlateSystem.Instance)
                {
                    prev.enabled = false;
                    prev.enabled = true;
                }
            }
        }
    }
}
