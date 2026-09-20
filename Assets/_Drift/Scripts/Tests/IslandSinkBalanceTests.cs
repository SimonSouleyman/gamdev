using System.Collections.Generic;
using System.Text;
using Drift.Islands;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // A scripted player on the real world plan: drives to an island, merges, refloats, repeat. Pure numbers
    // (no GameObjects), so a whole run costs microseconds and the difficulty curve can be asserted.
    public static class SinkRunSimulator
    {
        public enum Policy { Nearest, BestRefloatPerSecond, Cautious }

        public struct Isle
        {
            public Vector2 pos;
            public float radius, area;
        }

        public class Config
        {
            public bool oldModel;
            public Policy policy = Policy.BestRefloatPerSecond;
            // Share of Island.MaxSpeed actually made good on a leg (steering, acceleration, currents).
            public float cruise = 0.7f;
            // Seconds lost per leg on top of the travel: looking around, the drive-in before the merge.
            public float legOverhead = 4.5f;
            public float formMultiplier = 1.2f;
            public float mergeAreaKept = 0.88f;
            public float maxSeconds = 3600f;
            public float worldSize = 660f;
            public Vector2 start = Vector2.zero;
            public float startArea = 24f;
            public float secondsSmall = 95f, secondsHuge = 10f, halfArea = 250f, refloatExponent = 0.55f;
            // Old model: sink depth at which the island is gone, measured on merged islands (ridges buy depth).
            public float oldDepthBase = 0.85f, oldDepthPerLog = 2f;
            public float moveSpeed = 8f, agilityArea = 40f;
        }

        public class Result
        {
            public bool sunk, worldUnited;
            public float seconds, peakArea, secondsAbove300, secondsAbove1000;
            public int merges;
            public readonly List<Vector3> log = new();

            public override string ToString() =>
                (sunk ? "sunk" : worldUnited ? "united" : "alive") + " after " + seconds.ToString("F0") + " s, peak " + peakArea.ToString("F0")
                + ", merges " + merges + ", >=300 for " + secondsAbove300.ToString("F0") + " s, >=1000 for " + secondsAbove1000.ToString("F0") + " s";
        }

        static Vector2 WrapDelta(Vector2 a, Vector2 b, float w)
        {
            Vector2 d = a - b;
            d.x -= w * Mathf.Round(d.x / w);
            d.y -= w * Mathf.Round(d.y / w);
            return d;
        }

        public static float OldDepth(Config c, float area) => c.oldDepthBase + c.oldDepthPerLog * Mathf.Log(Mathf.Max(1f, area / c.startArea));

        public static float FullSeconds(Config c, float area)
        {
            if (!c.oldModel) return SinkBalance.SecondsForArea(area, c.secondsSmall, c.secondsHuge, c.halfArea) / c.formMultiplier;
            float rate = 0.012f * Mathf.Pow(Mathf.Max(area, 8f) / 30f, 0.35f) * c.formMultiplier;
            return OldDepth(c, area) / rate;
        }

        public static float Share(Config c, float guest, float host) =>
            c.oldModel ? Mathf.Clamp01(0.04f * guest / OldDepth(c, host)) : SinkBalance.RefloatShare(guest, host, c.refloatExponent, 1f);

        public static Result Run(IReadOnlyList<Isle> world, Config c)
        {
            var isles = new List<Isle>(world);
            var r = new Result();
            Vector2 pos = c.start;
            float area = c.startArea, buoy = 1f, t = 0f;
            r.peakArea = area;

            while (isles.Count > 0 && t < c.maxSeconds)
            {
                float full = FullSeconds(c, area);
                float left = buoy * full;
                float agility = Mathf.Pow(c.agilityArea / (c.agilityArea + area), 0.6f);
                float speed = c.moveSpeed * (0.55f + 0.45f * agility) * c.cruise;
                float reach = 1.2f * Mathf.Sqrt(area / Mathf.PI);

                int pick = -1;
                float pickScore = float.NegativeInfinity, pickLeg = 0f;
                bool anyGood = false;
                for (int k = 0; k < isles.Count; k++)
                {
                    float d = Mathf.Max(0f, WrapDelta(isles[k].pos, pos, c.worldSize).magnitude - reach - isles[k].radius);
                    float leg = d / speed + c.legOverhead;
                    if (leg >= 0.9f * left) continue;
                    float share = Share(c, isles[k].area, area);
                    float after = Mathf.Min(1f, buoy - leg / full + share);
                    float score;
                    switch (c.policy)
                    {
                        case Policy.Nearest: score = -leg; break;
                        case Policy.Cautious:
                            // Growth is the enemy: the smallest island that still leaves the bar comfortable.
                            bool good = after >= 0.75f;
                            if (good && !anyGood) { anyGood = true; pickScore = float.NegativeInfinity; }
                            if (anyGood && !good) continue;
                            score = good ? -isles[k].area : after;
                            break;
                        default: score = Mathf.Min(share, 1f - (buoy - leg / full)) / leg; break;
                    }
                    if (score > pickScore) { pickScore = score; pick = k; pickLeg = leg; }
                }

                float spent = pick < 0 ? left : pickLeg;
                if (area >= 300f) r.secondsAbove300 += spent;
                if (area >= 1000f) r.secondsAbove1000 += spent;
                t += spent;
                if (pick < 0)
                {
                    r.sunk = true;
                    break;
                }

                var g = isles[pick];
                isles.RemoveAt(pick);
                buoy = Mathf.Min(1f, buoy - pickLeg / full + Share(c, g.area, area));
                pos += WrapDelta(g.pos, pos, c.worldSize) * (g.area / (area + g.area));
                area += c.mergeAreaKept * g.area;
                r.peakArea = Mathf.Max(r.peakArea, area);
                r.merges++;
                r.log.Add(new Vector3(t, area, buoy));
            }

            r.seconds = t;
            r.worldUnited = isles.Count == 0;
            return r;
        }

        public static List<Isle> FromStreamer(WorldStreamer ws)
        {
            var list = new List<Isle>();
            foreach (var s in ws.WorldSlots())
                if (!s.consumed) list.Add(new Isle { pos = s.pos, radius = s.radius, area = s.EstimatedArea });
            return list;
        }

        // Every policy at three skill levels, new model against old; called from eval for the report.
        public static string Report(IReadOnlyList<Isle> world, Vector2 start, float worldSize)
        {
            var sb = new StringBuilder();
            float[] cruise = { 0.8f, 0.7f, 0.5f };
            float[] overhead = { 2.5f, 4.5f, 7.5f };
            foreach (bool old in new[] { true, false })
                foreach (Policy p in new[] { Policy.Nearest, Policy.BestRefloatPerSecond, Policy.Cautious })
                    for (int s = 0; s < 3; s++)
                    {
                        var c = new Config { oldModel = old, policy = p, cruise = cruise[s], legOverhead = overhead[s], start = start, worldSize = worldSize };
                        sb.Append(old ? "old " : "new ").Append(p).Append(" skill ").Append(s).Append(": ").Append(Run(world, c)).Append('\n');
                    }
            return sb.ToString();
        }
    }

    public class IslandSinkBalanceTests
    {
        readonly List<GameObject> _objects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        Island MakeIsland(Vector2 pos, int seed, float radius, bool player, bool volcano = false)
        {
            var go = new GameObject(player ? "TestPlayer" : "TestIsle_" + seed);
            go.SetActive(false);
            go.transform.position = new Vector3(pos.x, 0f, pos.y);
            var isl = go.AddComponent<Island>();
            isl.useKeyboardInput = player;
            isl.sinkEnabled = player;
            isl.landRadius = radius;
            isl.cellSize = IslandArchetypes.CellSize(radius);
            isl.shapeSeed = seed;
            isl.isVolcano = volcano;
            isl.carryResponse = 0f;
            go.SetActive(true);
            _objects.Add(go);
            return isl;
        }

        static int RegularSeed(int from)
        {
            while (Island.KindForSeed(from) != IslandKind.Regular) from++;
            return from;
        }

        static float SinkUntilSunk(Island isl, float dt, float limit, List<Vector2> trace = null)
        {
            float t = 0f;
            while (!isl.IsSunk && t < limit)
            {
                isl.AdvanceSink(dt);
                t += dt;
                trace?.Add(new Vector2(t, isl.Buoyancy));
            }
            return t;
        }

        static void SinkTo(Island isl, float buoyancy)
        {
            for (int k = 0; k < 4000 && isl.Buoyancy > buoyancy && !isl.IsSunk; k++) isl.AdvanceSink(0.1f);
        }

        [Test]
        public void SinkCurve_IsTheDesignedTable_AndGrowsSuperLinearly()
        {
            var isl = MakeIsland(Vector2.zero, 11, 3f, true);
            Assert.AreEqual(87.3f, isl.SinkSecondsForArea(25f), 1f);
            Assert.AreEqual(70.7f, isl.SinkSecondsForArea(100f), 1f);
            Assert.AreEqual(48.6f, isl.SinkSecondsForArea(300f), 1f);
            Assert.AreEqual(30.2f, isl.SinkSecondsForArea(800f), 1f);
            Assert.AreEqual(19.4f, isl.SinkSecondsForArea(2000f), 1f);
            Assert.Greater(isl.SinkSecondsForArea(100000f), isl.sinkSecondsHuge);
            Assert.Less(isl.SinkSecondsForArea(100000f), isl.sinkSecondsHuge + 1f);

            // What the player feels with a typical merged form of 75 % (multiplier 1.15): 80/60/40/25/17 s.
            float form = Mathf.Lerp(isl.elongatedSinkMultiplier, 1f, 0.75f);
            Assert.AreEqual(76f, isl.SinkSecondsForArea(25f) / form, 5f);
            Assert.AreEqual(60f, isl.SinkSecondsForArea(100f) / form, 4f);
            Assert.AreEqual(41f, isl.SinkSecondsForArea(300f) / form, 4f);
            Assert.AreEqual(25f, isl.SinkSecondsForArea(800f) / form, 3f);
            Assert.AreEqual(16.5f, isl.SinkSecondsForArea(2000f) / form, 2f);

            float lastSeconds = float.MaxValue, lastLoss = 0f, lastPerArea = 0f;
            for (float a = 10f; a <= 6000f; a *= 1.5f)
            {
                float s = isl.SinkSecondsForArea(a);
                Assert.Less(s, lastSeconds, "bigger sinks sooner at " + a);
                float loss = a / s;
                Assert.Greater(loss, lastLoss);
                Assert.Greater(loss / a, lastPerArea, "land lost per second grows faster than the area at " + a);
                lastSeconds = s;
                lastLoss = loss;
                lastPerArea = loss / a;
            }
        }

        [Test]
        public void RefloatShare_IsRelativeToTheHost()
        {
            Assert.AreEqual(1f, SinkBalance.RefloatShare(100f, 100f, 0.55f, 1f), 1e-5f, "equal size refloats fully");
            Assert.AreEqual(1f, SinkBalance.RefloatShare(300f, 100f, 0.55f, 1f), 1e-5f);
            float tenth = SinkBalance.RefloatShare(50f, 500f, 0.55f, 1f);
            Assert.GreaterOrEqual(tenth, 0.25f);
            Assert.LessOrEqual(tenth, 0.30f);
            Assert.AreEqual(tenth, SinkBalance.RefloatShare(5f, 50f, 0.55f, 1f), 1e-4f, "only the ratio counts");
            Assert.Less(SinkBalance.RefloatShare(5f, 2000f, 0.55f, 1f), 0.04f, "an islet barely helps a continent");
            Assert.AreEqual(0f, SinkBalance.RefloatShare(0f, 100f, 0.55f, 1f));

            float volcanic = SinkBalance.RefloatShare(50f, 500f, 0.55f, Island.RefloatFactorOf(IslandKind.Volcanic));
            float barren = SinkBalance.RefloatShare(50f, 500f, 0.55f, Island.RefloatFactorOf(IslandKind.Barren));
            Assert.AreEqual(1.6f * tenth, volcanic, 1e-4f);
            Assert.AreEqual(0.7f * tenth, barren, 1e-4f);

            float last = 0f;
            for (float g = 1f; g <= 400f; g *= 1.3f)
            {
                float s = SinkBalance.RefloatShare(g, 400f, 0.55f, 1f);
                Assert.Greater(s, last);
                last = s;
            }
        }

        [Test]
        public void Hypsometry_IsItsOwnInverse_AndCountsTheRealLand()
        {
            foreach (var shape in new[] { IslandShape.CreateBlob(6f, 5, 0.5f), IslandShape.CreateVolcano(5f, 9, 0.5f) })
            {
                var hyp = new IslandHypsometry();
                hyp.Build(shape);
                Assert.AreEqual(shape.MaxHeightRaw(), hyp.Top, 1e-5f);
                Assert.AreEqual(1f, hyp.LandFractionAt(0f));
                Assert.AreEqual(0f, hyp.LandFractionAt(hyp.Top));
                Assert.AreEqual(0f, hyp.DepthAt(1f));
                Assert.AreEqual(hyp.Top, hyp.DepthAt(0f), 1e-5f);

                float lastDepth = -1f;
                for (float f = 0.98f; f > 0.01f; f -= 0.03f)
                {
                    float depth = hyp.DepthAt(f);
                    Assert.Greater(depth, lastDepth);
                    lastDepth = depth;
                    Assert.AreEqual(f, hyp.LandFractionAt(depth), 1e-3f);

                    shape.sink = depth;
                    int land = 0;
                    for (int j = 0; j < shape.nz; j++)
                        for (int i = 0; i < shape.nx; i++)
                            if (shape.Height(i, j) > 0f) land++;
                    Assert.AreEqual(f, land / (float)hyp.LandCells, 0.02f, "counted land at depth " + depth);
                }
                shape.sink = 0f;
            }
        }

        [Test]
        public void StartIsland_SinksInAboutEightySeconds_TheBarIsATimer()
        {
            var isl = MakeIsland(Vector2.zero, 12346, 3f, true);
            Assert.AreEqual(1f, isl.Buoyancy, 1e-4f);
            float full = isl.SinkSecondsFull;
            Assert.AreEqual(80f, full, 6f);
            Assert.AreEqual(full, isl.SinkSecondsLeft, 0.01f);

            var trace = new List<Vector2>();
            float t = SinkUntilSunk(isl, 0.25f, 300f, trace);
            Assert.IsTrue(isl.IsSunk);
            // The form penalty grows a little while the shore frays, so the run ends somewhat before the estimate.
            Assert.AreEqual(full, t, 0.15f * full);
            Assert.AreEqual(0f, isl.SinkSecondsLeft);

            float last = 1f;
            foreach (var p in trace)
            {
                Assert.LessOrEqual(p.y, last + 1e-5f);
                last = p.y;
                if (p.x < 0.9f * t) Assert.AreEqual(1f - p.x / full, p.y, 0.12f, "bar at " + p.x);
            }
        }

        [Test]
        public void TheShoreGoesFirst_MostLandStaysUntilLate()
        {
            var isl = MakeIsland(Vector2.zero, 12346, 3f, true);
            SinkTo(isl, 0.5f);
            Assert.AreEqual(Mathf.Pow(0.5f, isl.sinkLandExponent), isl.LandFraction, 0.06f);
            Assert.Greater(isl.LandFraction, 0.7f);
        }

        [Test]
        public void BigIsland_SinksMuchFaster_WhateverItsTerrain()
        {
            var small = MakeIsland(Vector2.zero, 21, 3f, true);
            var big = MakeIsland(new Vector2(200f, 0f), 22, 13f, true);
            var peak = MakeIsland(new Vector2(400f, 0f), 23, 9f, true, true);
            Assert.Greater(big.LandArea, 350f);

            float tSmall = SinkUntilSunk(small, 0.25f, 300f);
            float fullBig = big.SinkSecondsFull, fullPeak = peak.SinkSecondsFull;
            float tBig = SinkUntilSunk(big, 0.25f, 300f);
            float tPeak = SinkUntilSunk(peak, 0.25f, 300f);

            Assert.AreEqual(fullBig, tBig, 0.15f * fullBig);
            Assert.AreEqual(fullPeak, tPeak, 0.2f * fullPeak, "a tall cone buys no extra time");
            Assert.Less(tBig, 0.62f * tSmall);
        }

        [Test]
        public void OnlyThePlayerSinks_AndAHoldStopsTheClock()
        {
            var ai = MakeIsland(Vector2.zero, 31, 4f, false);
            ai.sinkEnabled = true;
            for (int k = 0; k < 200; k++) ai.AdvanceSink(0.5f);
            Assert.AreEqual(0f, ai.Sink);
            Assert.IsTrue(float.IsPositiveInfinity(ai.SinkSecondsLeft));

            var player = MakeIsland(new Vector2(100f, 0f), 32, 3f, true);
            player.sinkEnabled = false;
            for (int k = 0; k < 200; k++) player.AdvanceSink(0.5f);
            Assert.AreEqual(0f, player.Sink);
            Assert.IsTrue(float.IsPositiveInfinity(player.SinkSecondsLeft));
        }

        [Test]
        public void Merge_RefloatsByTheGuestsShareOfTheHost()
        {
            var host = MakeIsland(Vector2.zero, 41, 8f, true);
            SinkTo(host, 0.4f);
            float before = host.Buoyancy;
            float sinkBefore = host.Sink;

            var islet = MakeIsland(new Vector2(0f, host.BoundingRadius + 1.5f), RegularSeed(42), 2.5f, false);
            float expected = SinkBalance.RefloatShare(islet.LandArea, host.FullArea, host.refloatExponent, islet.RefloatFactor);
            Assert.Less(expected, 0.4f);
            host.MergeFrom(islet, 3f, 0f);
            host.FinishUplift();
            Assert.AreEqual(expected, host.LastRefloat, 1e-4f);
            Assert.Less(host.Sink, sinkBefore);
            Assert.AreEqual(before + expected, host.Buoyancy, 0.02f);
            Assert.Less(host.Buoyancy, 0.95f, "a small guest does not refloat a big host");

            SinkTo(host, 0.3f);
            var twin = MakeIsland(new Vector2(0f, host.BoundingRadius + 8f), RegularSeed(60), 8.5f, false);
            host.MergeFrom(twin, 3f, 0f);
            host.FinishUplift();
            Assert.Greater(host.LastRefloat, 0.85f);
            Assert.Greater(host.Buoyancy, 0.95f, "an equal island refloats fully");
        }

        [Test]
        public void SaveData_RoundTripsTheBuoyancyThroughTheSinkDepth()
        {
            var a = MakeIsland(Vector2.zero, 51, 5f, true);
            SinkTo(a, 0.6f);
            var data = JsonUtility.FromJson<IslandSaveData>(JsonUtility.ToJson(a.Capture()));
            var b = MakeIsland(new Vector2(100f, 0f), 52, 3f, true);
            IslandSaveUtil.ApplySaved(b, data);
            Assert.AreEqual(a.Sink, b.Sink, 1e-5f);
            Assert.AreEqual(a.Buoyancy, b.Buoyancy, 0.01f);
        }

        static List<SinkRunSimulator.Isle> PlannedWorld(List<GameObject> objects, out WorldStreamer ws)
        {
            var playerGo = new GameObject("TestPlayer");
            playerGo.SetActive(false);
            var player = playerGo.AddComponent<Island>();
            player.useKeyboardInput = true;
            player.sinkEnabled = false;
            playerGo.SetActive(true);
            objects.Add(playerGo);

            var go = new GameObject("TestStreamer");
            go.SetActive(false);
            ws = go.AddComponent<WorldStreamer>();
            ws.player = player;
            objects.Add(go);
            return SinkRunSimulator.FromStreamer(ws);
        }

        [Test]
        public void ScriptedRuns_TheDifficultyCurveIsSane()
        {
            var world = PlannedWorld(_objects, out var ws);
            Assert.Greater(world.Count, 100);
            float size = ws.WorldSize;

            SinkRunSimulator.Result Run(bool old, SinkRunSimulator.Policy p, float cruise, float overhead) =>
                SinkRunSimulator.Run(world, new SinkRunSimulator.Config { oldModel = old, policy = p, cruise = cruise, legOverhead = overhead, worldSize = size });

            var greedy = Run(false, SinkRunSimulator.Policy.BestRefloatPerSecond, 0.7f, 4.5f);
            Assert.IsTrue(greedy.sunk, "a run ends");
            Assert.Greater(greedy.peakArea, 300f, "good play reaches a few hundred");
            Assert.Less(greedy.secondsAbove1000, 90f, "nobody holds a continent for long");

            var cautious = Run(false, SinkRunSimulator.Policy.Cautious, 0.7f, 4.5f);
            Assert.Greater(cautious.seconds, 1.5f * greedy.seconds, "staying small is the way to last");
            Assert.Greater(cautious.seconds, 300f);

            // No death spiral while small: even a dawdling player who only ever takes the nearest island,
            // however tiny, gets well past the start size before the legs become too long.
            var lazy = Run(false, SinkRunSimulator.Policy.Nearest, 0.5f, 7.5f);
            Assert.Greater(lazy.seconds, 100f);
            Assert.Greater(lazy.peakArea, 100f);

            var before = Run(true, SinkRunSimulator.Policy.BestRefloatPerSecond, 0.7f, 4.5f);
            Assert.Greater(before.peakArea, 2f * greedy.peakArea, "the old rule let the same player keep growing");
        }
    }
}
