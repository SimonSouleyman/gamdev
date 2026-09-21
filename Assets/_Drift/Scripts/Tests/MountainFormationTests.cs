using System.Collections.Generic;
using Drift.Islands;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class MountainFormationTests
    {
        readonly List<GameObject> _objects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        Island MakeIsland(Vector2 pos, int seed, float radius, bool volcano = false)
        {
            var go = new GameObject("TestMountain_" + seed);
            go.SetActive(false);
            go.transform.position = new Vector3(pos.x, 0f, pos.y);
            var isl = go.AddComponent<Island>();
            isl.useKeyboardInput = false;
            isl.sinkEnabled = false;
            isl.landRadius = radius;
            isl.shapeSeed = seed;
            isl.isVolcano = volcano;
            isl.carryResponse = 0f;
            go.SetActive(true);
            _objects.Add(go);
            return isl;
        }

        // Heights on the island's own grid points (LocalBounds starts on a grid point, cells are 0.5).
        static float[] Grid(Island isl, out int nx, out int nz)
        {
            var b = isl.LocalBounds;
            const float c = 0.5f;
            nx = Mathf.RoundToInt(b.width / c) + 1;
            nz = Mathf.RoundToInt(b.height / c) + 1;
            var f = new float[nx * nz];
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                    f[j * nx + i] = isl.SampleHeight(new Vector2(b.xMin + i * c, b.yMin + j * c));
            return f;
        }

        static float WorstExcess(Island isl, float degrees, float peakDegrees, out float steepestDeg)
        {
            var f = Grid(isl, out int nx, out int nz);
            var probe = new IslandShape(0.5f, nx, nz, Vector2.zero);
            float tan = probe.SteepestSlope(f, IslandShape.SlopeRule.Degrees(degrees, peakDegrees, 0f), out float excess);
            steepestDeg = Mathf.Atan(tan) * Mathf.Rad2Deg;
            return excess;
        }

        static float Peak(Island isl)
        {
            float m = 0f;
            foreach (float v in Grid(isl, out _, out _)) m = Mathf.Max(m, v);
            return m;
        }

        static int LandCount(float[] f)
        {
            int n = 0;
            foreach (float v in f) if (v > 0f) n++;
            return n;
        }

        Island StrongMerge()
        {
            var host = MakeIsland(Vector2.zero, 4242, 4f);
            var guest = MakeIsland(new Vector2(0f, 8.2f), 777, 4f);
            host.MergeFrom(guest, 8f, 1f);
            return host;
        }

        [Test]
        public void StrongMerge_NoSlopeSteeperThanTheLimit_DuringAndAfterTheRise()
        {
            var host = StrongMerge();
            // The rise blends the old land into the relaxed one, so on the way it is never steeper than either.
            float start = Mathf.Max(1e-3f, WorstExcess(host, host.maxSlope, host.volcanoMaxSlope, out _));
            for (int k = 0; k < 8; k++)
            {
                host.AdvanceUplift(host.upliftDuration * 0.15f);
                Assert.LessOrEqual(WorstExcess(host, host.maxSlope, host.volcanoMaxSlope, out float deg), start + 1e-3f, "during the rise, steepest " + deg);
            }
            host.FinishUplift();
            Assert.LessOrEqual(WorstExcess(host, host.maxSlope, host.volcanoMaxSlope, out float after), 1e-3f, "steepest " + after);
            Assert.LessOrEqual(after, host.maxSlope + 0.5f, "a merge highland stays below the peak band");
        }

        [Test]
        public void RepeatedMerges_StayGentleAndLow()
        {
            var host = MakeIsland(Vector2.zero, 99, 3.5f);
            int[] seeds = { 11, 23, 37, 41, 53 };
            for (int k = 0; k < seeds.Length; k++)
            {
                float a = k * 2.2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var g = MakeIsland(host.PlanarPosition + dir * (host.BoundingRadius + 2.5f), seeds[k], 3.5f);
                host.MergeFrom(g, 5f, 0.5f);
                host.FinishUplift();
            }
            Assert.LessOrEqual(WorstExcess(host, host.maxSlope, host.volcanoMaxSlope, out float deg), 1e-3f, "steepest " + deg);
            Assert.Less(Peak(host), 2.2f, "five merges pile up no tower");
        }

        [Test]
        public void Merge_KeepsTheCoastline_AndInventsNoLand()
        {
            var host = MakeIsland(Vector2.zero, 4242, 4f);
            var guest = MakeIsland(new Vector2(0f, 8.2f), 777, 4f);
            host.MergeFrom(guest, 8f, 1f);
            var start = Grid(host, out _, out _);
            host.FinishUplift();
            var end = Grid(host, out _, out _);
            Assert.AreEqual(start.Length, end.Length);
            for (int k = 0; k < end.Length; k++)
            {
                if (end[k] > 0f) Assert.Greater(start[k], 0f, "land where there was sea at cell " + k);
                if (start[k] > 0f) Assert.Greater(end[k], 0f, "coast eaten at cell " + k);
                if (start[k] <= IslandShape.BeachBand) Assert.AreEqual(start[k], end[k], 1e-4f, "beach band touched at cell " + k);
            }
            Assert.AreEqual(LandCount(start), LandCount(end));
        }

        [Test]
        public void Uplift_RisesSlowly()
        {
            var host = StrongMerge();
            float p0 = Peak(host);
            Assert.GreaterOrEqual(host.upliftDuration, 5f);
            host.AdvanceUplift(1f);
            float p1 = Peak(host);
            host.AdvanceUplift(2f);
            Assert.IsTrue(host.IsUplifting, "still rising after 3 s");
            host.FinishUplift();
            float p2 = Peak(host);
            Assert.Greater(p2, p0 + 0.2f, "the merge raises land");
            Assert.Less(p1 - p0, 0.2f * (p2 - p0), "no pop in the first second");
        }

        [Test]
        public void Uplift_BumpsTheVersionOnlyEveryHalfSecond()
        {
            var host = StrongMerge();
            int v0 = host.Version;
            for (int k = 0; k < 120; k++) host.AdvanceUplift(1f / 60f);
            Assert.IsTrue(host.IsUplifting);
            Assert.LessOrEqual(host.Version - v0, 4, "two seconds of rise");
            Assert.GreaterOrEqual(host.Version - v0, 3, "the life systems still hear of the rising land");
        }

        [Test]
        public void MidRiseMerge_KeepsTheRestOfTheFirstRise()
        {
            var host = StrongMerge();
            host.AdvanceUplift(host.upliftDuration * 0.3f);
            float before = Peak(host);
            var third = MakeIsland(host.PlanarPosition + new Vector2(host.BoundingRadius + 2.5f, 0f), 91, 3f);
            host.MergeFrom(third, 3f, 0f);
            Assert.AreEqual(before, Peak(host), 0.05f, "no jump at the second hit");
            host.FinishUplift();
            Assert.Greater(Peak(host), before + 0.1f, "the rest of the first rise still comes");
            Assert.LessOrEqual(WorstExcess(host, host.maxSlope, host.volcanoMaxSlope, out float deg), 1e-3f, "steepest " + deg);
        }

        [Test]
        public void Volcano_StaysAConeUnderItsOwnLimit()
        {
            var v = MakeIsland(Vector2.zero, 555, 4.5f, true);
            Assert.LessOrEqual(WorstExcess(v, v.volcanoMaxSlope, v.volcanoMaxSlope, out float deg), 1e-3f, "steepest " + deg);
            Assert.Greater(deg, v.maxSlope, "still steeper than rolling hills");
            Assert.Greater(Peak(v), 3f, "still a tall cone");
        }

        [Test]
        public void LimitSlopes_LeavesTheBeachBandAndSeaAlone()
        {
            var s = new IslandShape(0.5f, 9, 9, Vector2.zero);
            for (int k = 0; k < s.h.Length; k++) s.h[k] = 0.1f;
            s.h[4 * 9 + 4] = 5f;
            s.h[0] = IslandShape.Sea;
            var rule = IslandShape.SlopeRule.Degrees(30f, 30f, 0f);
            s.LimitSlopes(rule, 8);
            s.SteepestSlope(s.h, rule, out float excess);
            Assert.LessOrEqual(excess, 1e-4f);
            Assert.AreEqual(IslandShape.Sea, s.h[0]);
            Assert.Less(s.h[4 * 9 + 4], 1.5f);
            for (int k = 1; k < s.h.Length; k++) Assert.GreaterOrEqual(s.h[k], 0.1f - 1e-5f, "nothing sinks below where it was or the beach band");
        }
    }
}
