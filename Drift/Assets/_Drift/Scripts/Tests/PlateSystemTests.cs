using System.Collections.Generic;
using System.Linq;
using Drift.Tectonics;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class PlateSystemTests
    {
        readonly List<GameObject> _objects = new();
        PlateSystem _previousInstance;

        [SetUp]
        public void SetUp()
        {
            _previousInstance = PlateSystem.Instance;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();

            if (_previousInstance != null)
            {
                _previousInstance.enabled = false;
                _previousInstance.enabled = true;
            }
        }

        PlateSystem Make(int seed = 4242)
        {
            var go = new GameObject("TestPlates");
            go.SetActive(false);
            var ps = go.AddComponent<PlateSystem>();
            ps.seed = seed;
            ps.showBorders = false;
            go.SetActive(true);
            _objects.Add(go);
            return ps;
        }

        static PlateSystem.Border? Longest(PlateSystem ps, BoundaryKind kind)
        {
            PlateSystem.Border? best = null;
            float bestLen = 4f;
            foreach (var b in ps.Borders)
            {
                if (b.kind != kind) continue;
                float len = (b.p1 - b.p0).magnitude;
                if (len <= bestLen) continue;
                bestLen = len;
                best = b;
            }
            return best;
        }

        static PlateSystem.Border? FindBorder(PlateSystem ps, BoundaryKind kind)
        {
            for (int i = 0; i < 40; i++)
            {
                var b = Longest(ps, kind);
                if (b.HasValue) return b;
                ps.Step(0.5f);
            }
            return null;
        }

        static List<PlateOffsetData> Sorted(PlateSaveData d) =>
            d.offsets.OrderBy(o => o.cx).ThenBy(o => o.cz).ToList();

        static void AssertSameSave(PlateSaveData a, PlateSaveData b)
        {
            Assert.AreEqual(a.time, b.time);
            var oa = Sorted(a);
            var ob = Sorted(b);
            Assert.AreEqual(oa.Count, ob.Count);
            for (int i = 0; i < oa.Count; i++)
            {
                Assert.AreEqual(oa[i].cx, ob[i].cx);
                Assert.AreEqual(oa[i].cz, ob[i].cz);
                Assert.AreEqual(oa[i].ox, ob[i].ox, 1e-5f);
                Assert.AreEqual(oa[i].oy, ob[i].oy, 1e-5f);
                Assert.AreEqual(oa[i].px, ob[i].px, 1e-5f);
                Assert.AreEqual(oa[i].py, ob[i].py, 1e-5f);
            }
        }

        static PlateSystem Simulated(PlateSystem ps)
        {
            ps.Impulse(new Vector2(50f, 50f), new Vector2(3f, -2f));
            ps.Impulse(new Vector2(-70f, 20f), new Vector2(-1f, 4f));
            for (int i = 0; i < 30; i++) ps.Step(0.1f);
            return ps;
        }

        [Test]
        public void Step_IsDeterministicForSameSeed()
        {
            var a = Simulated(Make(7));
            var b = Simulated(Make(7));

            AssertSameSave(a.Capture(), b.Capture());
            Assert.AreEqual(a.Borders.Count, b.Borders.Count);
            for (int i = 0; i < a.Borders.Count; i++)
            {
                Assert.AreEqual(a.Borders[i].kind, b.Borders[i].kind);
                Assert.AreEqual(a.Borders[i].p0, b.Borders[i].p0);
                Assert.AreEqual(a.Borders[i].p1, b.Borders[i].p1);
                Assert.AreEqual(a.Borders[i].closing, b.Borders[i].closing);
            }
            var probe = new Vector2(12f, -34f);
            Assert.AreEqual(a.SampleVelocity(probe), b.SampleVelocity(probe));
        }

        [Test]
        public void DifferentSeedsProduceDifferentPlates()
        {
            var a = Make(1);
            var b = Make(2);
            Assert.AreNotEqual(a.NearestPlate(Vector2.zero).position, b.NearestPlate(Vector2.zero).position);
        }

        [Test]
        public void Step_AdvancesTimeByScaledDelta()
        {
            var ps = Make();
            ps.timeScale = 3f;
            ps.Step(0.5f);
            Assert.AreEqual(1.5f, ps.Capture().time, 1e-5f);
        }

        [Test]
        public void PlatesMoveOverTime()
        {
            var ps = Make();
            Vector2 before = ps.NearestPlate(Vector2.zero).position;
            ps.Step(2f);
            Assert.AreNotEqual(before, ps.NearestPlate(Vector2.zero).position);
        }

        [Test]
        public void ConvergenceAt_IsNeverNegative()
        {
            var ps = Make();
            ps.Step(1f);
            for (float x = -150f; x <= 150f; x += 25f)
                for (float z = -150f; z <= 150f; z += 25f)
                    Assert.GreaterOrEqual(ps.ConvergenceAt(new Vector2(x, z)), 0f);
        }

        [Test]
        public void ConvergenceAt_OnConvergentBoundaryMatchesClosingSpeed()
        {
            var ps = Make();
            ps.convergenceInfluence = 1f;
            var border = FindBorder(ps, BoundaryKind.Convergent);
            Assert.IsTrue(border.HasValue, "no convergent boundary found");

            Vector2 mid = (border.Value.p0 + border.Value.p1) * 0.5f;
            float c = ps.ConvergenceAt(mid);
            Assert.Greater(c, 0f);
            Assert.AreEqual(border.Value.closing, c, 0.01f);
        }

        [Test]
        public void ConvergenceAt_OnDivergentBoundaryIsZero()
        {
            var ps = Make();
            ps.convergenceInfluence = 1f;
            var border = FindBorder(ps, BoundaryKind.Divergent);
            Assert.IsTrue(border.HasValue, "no divergent boundary found");
            Assert.Less(border.Value.closing, 0f);

            Vector2 mid = (border.Value.p0 + border.Value.p1) * 0.5f;
            Assert.AreEqual(0f, ps.ConvergenceAt(mid), 1e-4f);
        }

        [Test]
        public void ConvergenceAt_InsideAPlateIsZero()
        {
            var ps = Make();
            ps.convergenceInfluence = 1f;
            var plate = ps.NearestPlate(new Vector2(55f, 55f));
            Assert.AreEqual(0f, ps.ConvergenceAt(plate.position), 1e-4f);
        }

        [Test]
        public void Impulse_PushesTheNearestPlateOnly()
        {
            var ps = Make();
            var pos = new Vector2(30f, 40f);
            var v = new Vector2(3f, -2f);
            var plate = ps.NearestPlate(pos);
            Vector2Int id = plate.core.id;
            Vector2 velBefore = ps.SampleVelocity(pos);

            ps.Impulse(pos, v);

            Vector2 velAfter = ps.SampleVelocity(pos);
            Assert.AreEqual(v.x, (velAfter - velBefore).x, 1e-4f);
            Assert.AreEqual(v.y, (velAfter - velBefore).y, 1e-4f);

            for (int i = 0; i < 5; i++) ps.Step(0.1f);

            var pushed = ps.Capture().offsets.Where(o => o.cx == id.x && o.cz == id.y).ToList();
            Assert.AreEqual(1, pushed.Count);
            Assert.Greater(Vector2.Dot(new Vector2(pushed[0].ox, pushed[0].oy), v), 0f);
        }

        [Test]
        public void Impulse_DecaysBackToRest()
        {
            var ps = Make();
            // Random plate events push plates too; the decay of one impulse is only observable without them.
            ps.eventRate = 0f;
            var pos = new Vector2(30f, 40f);
            Vector2Int id = ps.NearestPlate(pos).core.id;
            ps.Impulse(pos, new Vector2(2f, 1f));
            for (int i = 0; i < 60; i++) ps.Step(1f);
            Assert.That(ps.Capture().offsets.Any(o => o.cx == id.x && o.cz == id.y), Is.False);
        }

        [Test]
        public void CaptureRestore_RoundTripsState()
        {
            var a = Simulated(Make(9));
            var saved = a.Capture();
            Assert.Greater(saved.offsets.Count, 0);

            var b = Make(9);
            b.Restore(saved);

            AssertSameSave(saved, b.Capture());
            var probe = new Vector2(50f, 50f);
            Assert.AreEqual(a.NearestPlate(probe).position.x, b.NearestPlate(probe).position.x, 1e-4f);
            Assert.AreEqual(a.NearestPlate(probe).position.y, b.NearestPlate(probe).position.y, 1e-4f);
            Assert.AreEqual(a.ConvergenceAt(probe), b.ConvergenceAt(probe), 1e-4f);
        }

        [Test]
        public void CaptureRestore_ContinuesIdentically()
        {
            var a = Simulated(Make(9));
            var b = Make(9);
            b.Restore(a.Capture());

            for (int i = 0; i < 10; i++)
            {
                a.Step(0.2f);
                b.Step(0.2f);
            }
            AssertSameSave(a.Capture(), b.Capture());
        }

        [Test]
        public void Capture_IsASnapshot()
        {
            var ps = Simulated(Make());
            var saved = ps.Capture();
            float t = saved.time;
            int n = saved.offsets.Count;
            ps.Step(1f);
            Assert.AreEqual(t, saved.time);
            Assert.AreEqual(n, saved.offsets.Count);
        }

        PlateSystem MakeSeams(Vector3 focusAt, int seed = 4242)
        {
            var ps = Make(seed);
            ps.transform.position = focusAt;
            ps.focus = ps.transform;
            ps.enableEvents = false;
            ps.borderRefreshInterval = 0f;
            ps.Step(3f);
            return ps;
        }

        static bool Closed(PlateSystem.Border b) => !b.open0 && !b.open1;

        [Test]
        public void Seam_IsCurvedButPinnedToItsChordEnds()
        {
            var ps = MakeSeams(Vector3.zero);
            float deepest = 0f;
            foreach (var b in ps.Borders)
            {
                if (!b.open0) Assert.Less((ps.SeamPoint(b, 0f) - b.p0).magnitude, 1e-4f);
                if (!b.open1) Assert.Less((ps.SeamPoint(b, 1f) - b.p1).magnitude, 1e-4f);
                for (int i = 0; i <= 16; i++) deepest = Mathf.Max(deepest, Mathf.Abs(ps.SeamOffset(b, i / 16f)));
            }
            Assert.Greater(deepest, 2f, "no border meanders");
        }

        [Test]
        public void Seam_ThreeBordersMeetInOnePointAtAJunction()
        {
            var ps = MakeSeams(Vector3.zero);
            var ends = new List<Vector2>();
            foreach (var b in ps.Borders)
            {
                if (!b.open0) ends.Add(ps.SeamPoint(b, 0f));
                if (!b.open1) ends.Add(ps.SeamPoint(b, 1f));
            }
            int junctions = 0;
            foreach (var e in ends)
            {
                if (e.magnitude > ps.viewRadius * 0.6f) continue;
                int meeting = ends.Count(o => (o - e).magnitude < 0.02f);
                Assert.GreaterOrEqual(meeting, 3, $"seam end {e} is not shared by three borders");
                junctions++;
            }
            Assert.Greater(junctions, 0);
        }

        [Test]
        public void Seam_StaysWithinTheAmplitudeCap()
        {
            var ps = MakeSeams(Vector3.zero);
            for (int round = 0; round < 6; round++)
            {
                foreach (var b in ps.Borders)
                {
                    float cap = b.open0 || b.open1 ? ps.seamMeanderMax : Mathf.Min(ps.seamMeanderMax, ps.seamMeander * b.length);
                    Assert.LessOrEqual(b.amplitude, ps.seamMeanderMax + 1e-4f);
                    Vector2 chord = (b.p1 - b.p0).normalized;
                    for (int i = 0; i <= 32; i++)
                    {
                        float t = i / 32f;
                        Assert.LessOrEqual(Mathf.Abs(ps.SeamOffset(b, t)), cap + 1e-4f);
                        Vector2 rel = ps.SeamPoint(b, t) - b.p0;
                        float off = Mathf.Abs(rel.x * chord.y - rel.y * chord.x);
                        Assert.LessOrEqual(off, ps.seamMeanderMax + 1e-2f);
                    }
                }
                ps.Step(17f);
            }
        }

        [Test]
        public void Meander_IsBoundedAndSmoothInPlaceAndTime()
        {
            for (int key = 1; key < 40; key += 7)
                for (float x = -20f; x < 20f; x += 0.013f)
                {
                    float v = PlateSystem.Meander(key * 7919, x, 123f);
                    Assert.LessOrEqual(Mathf.Abs(v), 1f + 1e-5f);
                    Assert.Less(Mathf.Abs(PlateSystem.Meander(key * 7919, x + 0.013f, 123f) - v), 0.12f);
                    Assert.Less(Mathf.Abs(PlateSystem.Meander(key * 7919, x, 124f) - v), 0.06f);
                }
        }

        [Test]
        public void Seam_DoesNotDependOnPlateOrEndOrder()
        {
            var ps = MakeSeams(Vector3.zero);
            int compared = 0;
            foreach (var b in ps.Borders)
            {
                if (b.length < 20f) continue;
                var r = ps.MakeBorder(b.b, b.a, b.p1, b.p0, b.open1, b.open0, b.kind, b.closing);
                Assert.AreEqual(b.key, r.key);
                Assert.Less((b.p0 - r.p0).magnitude, 1e-4f);
                Assert.Less((b.normal - r.normal).magnitude, 1e-4f);
                for (int i = 0; i <= 8; i++)
                    Assert.Less((ps.SeamPoint(b, i / 8f) - ps.SeamPoint(r, i / 8f)).magnitude, 1e-3f);
                compared++;
            }
            Assert.Greater(compared, 3);
        }

        [Test]
        public void Seam_IsIdenticalInWrappedCopiesOfTheWorld()
        {
            var near = MakeSeams(Vector3.zero, 11);
            var far = MakeSeams(new Vector3(near.WorldSize, 0f, -2f * near.WorldSize), 11);
            var shift = new Vector2(near.WorldSize, -2f * near.WorldSize);
            int compared = 0;
            foreach (var b in near.Borders)
            {
                if (!Closed(b) || b.length < 20f) continue;
                foreach (var o in far.Borders)
                {
                    if (!Closed(o) || o.key != b.key || (o.p0 - shift - b.p0).magnitude > 0.05f) continue;
                    Assert.AreNotEqual(b.a.cell, o.a.cell);
                    for (int i = 0; i <= 8; i++)
                        Assert.Less((near.SeamPoint(b, i / 8f) - (far.SeamPoint(o, i / 8f) - shift)).magnitude, 0.03f);
                    compared++;
                }
            }
            Assert.Greater(compared, 3);
        }

        [Test]
        public void ClosestOnSeam_RoundTripsPointsOfTheSeam()
        {
            var ps = MakeSeams(Vector3.zero);
            int compared = 0;
            foreach (var b in ps.Borders)
            {
                if (!Closed(b) || b.length < 30f) continue;
                for (int i = 1; i < 10; i++)
                {
                    float t = i / 10f;
                    Vector2 on = ps.SeamPoint(b, t);
                    Assert.IsTrue(ps.ClosestOnSeam(b, on, out float back, out Vector2 q));
                    Assert.Less((q - on).magnitude, 0.03f);
                    Assert.AreEqual(t, back, 0.02f);

                    Vector2 tan = ps.SeamTangent(b, t);
                    Assert.Greater(Vector2.Dot(tan, b.p1 - b.p0), 0f);
                    Vector2 beside = on + new Vector2(-tan.y, tan.x) * 2f;
                    ps.ClosestOnSeam(b, beside, out _, out Vector2 q2);
                    Assert.LessOrEqual((q2 - beside).magnitude, 2f + 0.02f);
                }
                Vector2 past = b.p1 + (b.p1 - b.p0).normalized * 5f;
                Assert.IsFalse(ps.ClosestOnSeam(b, past, out float end, out _));
                Assert.AreEqual(1f, end, 1e-4f);

                Assert.GreaterOrEqual(ps.SeamLength(b), b.length - 1e-3f);
                float walked = ps.SeamAdvance(b, 0.2f, 10f);
                Assert.AreEqual(10f, ArcBetween(ps, b, 0.2f, walked), 0.25f);
                Assert.IsTrue(ps.NearestSeam(ps.SeamPoint(b, 0.5f), 1f, out var found, out _, out _));
                Assert.AreEqual(b.key, found.key);
                compared++;
            }
            Assert.Greater(compared, 2);
        }

        static float ArcBetween(PlateSystem ps, PlateSystem.Border b, float t0, float t1)
        {
            float sum = 0f;
            Vector2 prev = ps.SeamPoint(b, t0);
            for (int i = 1; i <= 40; i++)
            {
                Vector2 p = ps.SeamPoint(b, Mathf.Lerp(t0, t1, i / 40f));
                sum += (p - prev).magnitude;
                prev = p;
            }
            return sum;
        }

        [Test]
        public void Events_StartOnTheCurvedSeam()
        {
            var ps = MakeSeams(Vector3.zero);
            var started = new List<PlateEventData>();
            ps.EventStarted += started.Add;
            ps.enableEvents = true;
            ps.eventRate = 400f;
            ps.Step(0.02f);
            Assert.Greater(started.Count, 5);
            foreach (var e in started)
                Assert.IsTrue(ps.NearestSeam(e.position, 0.5f, out _, out _, out _), $"event at {e.position} is off the seam");
        }

        [Test]
        public void SeamMesh_StaysWithinTheVertexBudget()
        {
            var ps = MakeSeams(Vector3.zero);
            ps.showBorders = true;
            ps.Step(0.1f);
            int normal = ps.SeamVertexCount;
            Assert.Greater(normal, 8 * 10);
            Assert.LessOrEqual(normal, PlateSystem.MaxSeamVertices);
            Assert.AreEqual(0, ps.SeamIndexCount % 3);

            ps.seamSegment = 0.25f;
            ps.Step(0.1f);
            Assert.Greater(ps.SeamVertexCount, normal);
            Assert.LessOrEqual(ps.SeamVertexCount, PlateSystem.MaxSeamVertices);
        }

        [Test]
        public void Restore_NullIsIgnored()
        {
            var ps = Make();
            ps.Step(1f);
            float t = ps.Capture().time;
            ps.Restore(null);
            Assert.AreEqual(t, ps.Capture().time);
        }
    }
}
