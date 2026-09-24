using System.Collections.Generic;
using System.Linq;
using System.Text;
using Drift.Core;
using Drift.Islands;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // A steering bot on a real streamed small world, far away from anything in the open scene: it drives to the
    // nearest island the world still plans, merges it and goes on until WorldStreamer.Progress reports no island
    // left. Everything runs through Island.Tick / AdvanceUplift / AdvanceSink and IslandWorld.Step, as in the game.
    public static class CozyRunBot
    {
        public class Result
        {
            public int seed, merges, islandsLeft, worldIslands;
            public bool united, stuck;
            public float seconds, worldSize, fullArea, landArea, boundRadius, maxHeight, plannedArea;
            public readonly List<float> legs = new();
            public string stuckAt = "";

            public float MedianLeg => legs.Count == 0 ? 0f : legs.OrderBy(x => x).ElementAt(legs.Count / 2);

            public override string ToString()
            {
                var sorted = legs.OrderBy(x => x).ToList();
                string legText = sorted.Count == 0 ? "-" :
                    $"legs min {sorted[0]:F1} / median {MedianLeg:F1} / max {sorted[sorted.Count - 1]:F1} s";
                return $"seed {seed}: {(united ? "united" : stuck ? "STUCK" : "timeout")} after {seconds:F0} s ({seconds / 60f:F1} min), "
                    + $"{merges} merges of {worldIslands} planned, {legText}; Pangaea full {fullArea:F0} / above water {landArea:F0} "
                    + $"(planned world area {plannedArea:F0}), span {2f * boundRadius:F0} u of a {worldSize:F0} u world "
                    + $"({2f * boundRadius / Mathf.Max(1f, worldSize) * 100f:F0} %), peak {maxHeight:F1}" + stuckAt;
            }
        }

        static Vector2 WrapDelta(Vector2 a, Vector2 b, float w)
        {
            Vector2 d = a - b;
            if (w > 0f)
            {
                d.x -= w * Mathf.Round(d.x / w);
                d.y -= w * Mathf.Round(d.y / w);
            }
            return d;
        }

        // The player's scene values (Planet.unity) that matter for the pace.
        public static Island MakePlayer(List<GameObject> objects, Vector2 pos, int shapeSeed, GameMode mode)
        {
            var go = new GameObject("BotPlayer");
            go.SetActive(false);
            go.transform.position = new Vector3(pos.x, 0f, pos.y);
            var p = go.AddComponent<Island>();
            p.useKeyboardInput = true;
            p.sinkEnabled = true;
            p.ModeOverride = mode;
            p.shapeSeed = shapeSeed;
            p.moveSpeed = 9.5f;
            p.velocityAlign = 1.5f;
            p.driftRotation = 3f;
            p.sinkSecondsHuge = 18f;
            p.sinkHalfArea = 500f;
            // Only its own drive: no plate current or surf from whatever PlateSystem happens to be open.
            p.carryResponse = 0f;
            p.surfResponse = 0f;
            go.SetActive(true);
            objects.Add(go);
            return p;
        }

        public static Result Run(List<GameObject> objects, int seed, float maxSeconds = 1500f, float dt = 0.1f)
        {
            // The adventure ring may have left its clamp behind (a static); the open sea has none.
            var constraint = Island.PositionConstraint;
            Island.PositionConstraint = null;
            try { return RunUnconstrained(objects, seed, maxSeconds, dt); }
            finally { Island.PositionConstraint = constraint; }
        }

        static Result RunUnconstrained(List<GameObject> objects, int seed, float maxSeconds, float dt)
        {
            // Far from the scene's own world, so IslandWorld never sees its islands.
            Vector2 origin = new Vector2(40000f, 40000f);
            var player = MakePlayer(objects, origin, 12346, GameMode.Cozy);

            var sgo = new GameObject("BotStreamer");
            sgo.SetActive(false);
            var ws = sgo.AddComponent<WorldStreamer>();
            ws.player = player;
            ws.seed = seed;
            sgo.SetActive(true);
            objects.Add(sgo);
            var wgo = new GameObject("BotWorld");
            var world = wgo.AddComponent<IslandWorld>();
            objects.Add(wgo);

            ws.StreamAround(true);
            var r = new Result { seed = seed, worldSize = ws.WorldSize, worldIslands = ws.WorldIslandCount };
            foreach (var s in ws.WorldSlots()) r.plannedArea += s.EstimatedArea;

            var list = new List<Island>();
            void Refresh()
            {
                list.Clear();
                list.Add(player);
                foreach (var isl in sgo.GetComponentsInChildren<Island>()) list.Add(isl);
            }
            Refresh();

            float t = 0f, legStart = 0f, streamTimer = 0f, lastMerge = 0f;
            float w = ws.WorldSize;
            while (t < maxSeconds)
            {
                ws.Progress(out _, out int left);
                if (left == 0) { r.united = true; break; }
                Vector2 pos = player.PlanarPosition;
                Vector2 best = Vector2.zero;
                float bestD = float.MaxValue;
                if (t - lastMerge > 150f)
                {
                    r.stuck = true;
                    float nearestLive = float.MaxValue;
                    foreach (var isl in list) if (isl != null && isl != player) nearestLive = Mathf.Min(nearestLive, Vector2.Distance(isl.PlanarPosition, pos) - isl.BoundingRadius);
                    r.stuckAt = $" | stuck at {pos}, speed {player.Speed:F1}, live {list.Count - 1}, nearest live {nearestLive:F1}, contacts {world.ActiveContacts}, left {left}";
                    break;
                }
                foreach (var s in ws.WorldSlots())
                {
                    if (s.consumed) continue;
                    Vector2 d = WrapDelta(s.pos, pos, w);
                    float dist = d.magnitude - s.radius;
                    if (dist < bestD) { bestD = dist; best = d; }
                }
                float yawTo = Mathf.Atan2(best.x, best.y) * Mathf.Rad2Deg;
                float err = Mathf.DeltaAngle(player.Yaw, yawTo);
                var input = new Vector2(Mathf.Clamp(err / 25f, -1f, 1f), Mathf.Abs(err) > 100f ? 0.3f : 1f);

                player.RunProgress = ws.MergedShare;
                player.Tick(input, dt);
                player.AdvanceUplift(dt);
                player.AdvanceSink(dt);
                if (world.Step(dt, list))
                {
                    r.merges++;
                    r.legs.Add(t - legStart);
                    legStart = t;
                    lastMerge = t;
                    Refresh();
                }
                t += dt;
                streamTimer += dt;
                if (streamTimer >= 0.5f)
                {
                    streamTimer = 0f;
                    ws.StreamAround(true);
                    Refresh();
                }
            }
            player.FinishUplift();
            ws.Progress(out _, out r.islandsLeft);
            r.seconds = t;
            r.fullArea = player.FullArea;
            r.landArea = player.LandArea;
            r.boundRadius = player.BoundingRadius;
            r.maxHeight = player.MaxHeight;
            return r;
        }
    }

    public class CozyWorldTests
    {
        readonly List<GameObject> _objects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        Island MakeIsland(Vector2 pos, int seed, float radius, bool player, GameMode mode)
        {
            var go = new GameObject(player ? "TestPlayer" : "TestIsle_" + seed);
            go.SetActive(false);
            go.transform.position = new Vector3(pos.x, 0f, pos.y);
            var isl = go.AddComponent<Island>();
            isl.useKeyboardInput = player;
            isl.sinkEnabled = player;
            isl.ModeOverride = mode;
            isl.landRadius = radius;
            isl.cellSize = IslandArchetypes.CellSize(radius);
            isl.shapeSeed = seed;
            isl.carryResponse = 0f;
            isl.surfResponse = 0f;
            go.SetActive(true);
            _objects.Add(go);
            return isl;
        }

        static int RegularSeed(int from)
        {
            while (Island.KindForSeed(from) != IslandKind.Regular) from++;
            return from;
        }

        static void Sink(Island isl, float seconds, float dt = 0.25f)
        {
            for (float t = 0f; t < seconds; t += dt) isl.AdvanceSink(dt);
        }

        WorldStreamer MakeStreamer(int seed, Vector2 start)
        {
            var go = new GameObject("TestStreamer");
            go.SetActive(false);
            _objects.Add(go);
            var ws = go.AddComponent<WorldStreamer>();
            ws.seed = seed;
            ws.RestoreState(start, null);
            return ws;
        }

        // ---- A) cozy sinking = a size regulator ----

        [Test]
        public void Cozy_BigIslandsSinkFaster_SmallOnesSlower()
        {
            var small = MakeIsland(Vector2.zero, 21, 4f, true, GameMode.Cozy);
            var big = MakeIsland(new Vector2(300f, 0f), 22, 13f, true, GameMode.Cozy);
            small.StartArea = big.StartArea = 1f;
            Sink(small, 10f);
            Sink(big, 10f);
            float lostSmall = 1f - small.LandFraction, lostBig = 1f - big.LandFraction;
            Assert.Greater(lostSmall, 0f, "a grown island settles in cozy too");
            Assert.Greater(lostBig, 1.5f * lostSmall, $"big {lostBig:F3} vs small {lostSmall:F3}");
            Assert.IsFalse(small.SinkIsLethal);
            Assert.IsTrue(float.IsPositiveInfinity(big.SinkSecondsLeft));
        }

        [Test]
        public void Cozy_StopsAtItsFloor_NeverSinks_AndTheBarStaysCalm()
        {
            var isl = MakeIsland(Vector2.zero, 23, 9f, true, GameMode.Cozy);
            isl.StartArea = 1f;
            float full = isl.FullArea;
            float lowestBar = 1f;
            for (int k = 0; k < 2400; k++)
            {
                isl.AdvanceSink(0.5f);
                lowestBar = Mathf.Min(lowestBar, isl.Buoyancy);
            }
            Assert.IsFalse(isl.IsSunk);
            Assert.AreEqual(isl.cozyKeepShareStart * full, isl.SinkFloorArea, 1e-3f);
            Assert.AreEqual(isl.cozyKeepShareStart, isl.LandFraction, 0.04f, "rests on the floor");
            Assert.IsTrue(isl.SinkResting);
            Assert.GreaterOrEqual(lowestBar, Island.CozyBarAtRest - 1e-4f);

            // Resting really is resting: nothing moves any more.
            float sink = isl.Sink;
            Sink(isl, 60f);
            Assert.AreEqual(sink, isl.Sink, 0.01f);
        }

        [Test]
        public void Cozy_FloorGrowsWithTheRun_AndTheIslandRisesBackToIt()
        {
            var isl = MakeIsland(Vector2.zero, 24, 9f, true, GameMode.Cozy);
            isl.StartArea = 1f;
            float full = isl.FullArea;
            isl.RunProgress = 0.5f;
            Assert.AreEqual(Mathf.Lerp(isl.cozyKeepShareStart, isl.cozyKeepShareEnd, 0.5f) * full, isl.SinkFloorArea, 1e-3f);

            isl.RunProgress = 0f;
            Sink(isl, 900f, 0.5f);
            Assert.AreEqual(isl.cozyKeepShareStart, isl.LandFraction, 0.04f);

            isl.RunProgress = 1f;
            Sink(isl, 3f * isl.cozyRiseSeconds, 0.5f);
            Assert.AreEqual(isl.cozyKeepShareEnd, isl.LandFraction, 0.04f, "the Pangaea shows nearly all its land");
            Assert.IsTrue(isl.SinkResting);
        }

        [Test]
        public void Cozy_NeverBelowTheStartSize()
        {
            var isl = MakeIsland(Vector2.zero, 25, 3f, true, GameMode.Cozy);
            float start = isl.StartArea;
            Assert.AreEqual(isl.FullArea, start, 1e-4f);
            Sink(isl, 300f);
            Assert.AreEqual(0f, isl.Sink, "the start island does not sink at all");
            Assert.AreEqual(1f, isl.Buoyancy, 1e-4f);

            var islet = MakeIsland(new Vector2(0f, isl.BoundingRadius + 1.5f), RegularSeed(42), 2.5f, false, GameMode.Cozy);
            isl.MergeFrom(islet, 3f, 0f);
            isl.FinishUplift();
            Assert.Greater(isl.FullArea, start);
            Sink(isl, 900f, 0.5f);
            Assert.IsFalse(isl.IsSunk);
            // Within the resolution of the height classes (a flat beach ring shares one class).
            Assert.GreaterOrEqual(isl.LandArea, 0.9f * start, "never smaller than the start island");
            Assert.IsFalse(isl.Boosting, "no boost in cozy");
        }

        [Test]
        public void Cozy_HoldsStopTheWater()
        {
            var isl = MakeIsland(Vector2.zero, 26, 9f, true, GameMode.Cozy);
            isl.StartArea = 1f;
            isl.sinkEnabled = false;
            Sink(isl, 120f);
            Assert.AreEqual(0f, isl.Sink);
            isl.sinkEnabled = true;
            Sink(isl, 5f);
            Assert.Greater(isl.Sink, 0f);
        }

        // ---- B) adventure: survival timer that can be switched off, boost on merge ----

        [Test]
        public void Adventure_SinkingCanBeSwitchedOff()
        {
            var isl = MakeIsland(Vector2.zero, 27, 3f, true, GameMode.Adventure);
            isl.adventureSinking = false;
            Sink(isl, 300f);
            Assert.AreEqual(0f, isl.Sink);
            Assert.IsFalse(isl.IsSunk);
            Assert.IsFalse(isl.SinkIsLethal);
            Assert.IsTrue(float.IsPositiveInfinity(isl.SinkSecondsLeft));

            isl.adventureSinking = true;
            Assert.IsTrue(isl.SinkIsLethal);
            for (int k = 0; k < 2000 && !isl.IsSunk; k++) isl.AdvanceSink(0.25f);
            Assert.IsTrue(isl.IsSunk, "with sinking on the timer ends the run");
        }

        [Test]
        public void Adventure_AFlotsamBoostSurgesForwardAndFades()
        {
            var isl = MakeIsland(Vector2.zero, 28, 4f, true, GameMode.Adventure);
            Assert.IsFalse(isl.Boosting);
            Assert.AreEqual(1f, isl.BoostFactor);
            isl.SpeedBoost(4f, 1.6f);
            Assert.IsTrue(isl.Boosting);
            Assert.AreEqual(1.6f, isl.BoostFactor, 1e-4f);

            float peak = 0f;
            for (float t = 0f; t < 1.5f; t += 0.05f)
            {
                isl.Tick(Vector2.zero, 0.05f);
                peak = Mathf.Max(peak, isl.Speed);
            }
            Assert.Greater(isl.Speed, 1.3f * isl.MaxSpeed, "surges forward on its own");
            Assert.LessOrEqual(peak, 1.6f * isl.MaxSpeed + 1e-3f);

            float last = isl.BoostFactor;
            for (float t = 1.5f; t < 4.3f; t += 0.05f)
            {
                isl.Tick(Vector2.zero, 0.05f);
                Assert.LessOrEqual(isl.BoostFactor, last + 1e-5f, "fades, never jumps back");
                last = isl.BoostFactor;
            }
            Assert.IsFalse(isl.Boosting);
            Assert.AreEqual(1f, isl.BoostFactor);
            Assert.LessOrEqual(isl.Speed, isl.MaxSpeed + 1e-3f);
        }

        [Test]
        public void Adventure_MergingAnIslandNoLongerBoostsOrIsReachedAtAll()
        {
            var host = MakeIsland(Vector2.zero, 28, 4f, true, GameMode.Adventure);
            var islet = MakeIsland(new Vector2(0f, host.BoundingRadius + 1.5f), RegularSeed(43), 2.5f, false, GameMode.Adventure);
            host.MergeFrom(islet, 3f, 0f);
            host.FinishUplift();
            Assert.IsFalse(host.Boosting, "islands are obstacles in Abenteuer, a merge is no reward");
        }

        [Test]
        public void RewardApi_AddBuoyancy_AndSpeedBoost()
        {
            var isl = MakeIsland(Vector2.zero, 29, 6f, true, GameMode.Adventure);
            Sink(isl, 25f);
            float before = isl.RawBuoyancy, sink = isl.Sink;
            isl.AddBuoyancy(0.2f);
            Assert.AreEqual(Mathf.Min(1f, before + 0.2f), isl.RawBuoyancy, 0.01f);
            Assert.Less(isl.Sink, sink);
            isl.AddBuoyancy(5f);
            Assert.AreEqual(1f, isl.RawBuoyancy, 1e-4f, "clamped at fully afloat");

            isl.adventureSinking = false;
            Sink(isl, 1f);
            isl.adventureSinking = true;
            Sink(isl, 20f);
            float b = isl.RawBuoyancy;
            isl.adventureSinking = false;
            isl.AddBuoyancy(0.3f);
            Assert.AreEqual(b, isl.RawBuoyancy, 1e-5f, "no effect with sinking off");

            isl.SpeedBoost(2f, 1.4f);
            Assert.IsTrue(isl.Boosting);
            Assert.AreEqual(1.4f, isl.BoostFactor, 1e-4f);
            isl.SpeedBoost(1f, 1.2f);
            Assert.AreEqual(1.4f, isl.BoostFactor, 1e-4f, "the stronger boost wins");
            Assert.AreEqual(2f, isl.BoostRemaining, 1e-4f, "and the longer time");
            for (int k = 0; k < 50; k++) isl.Tick(Vector2.zero, 0.05f);
            Assert.IsFalse(isl.Boosting);
        }

        // ---- C) the small world ----

        [Test]
        public void SmallWorld_HasTheSetIslandsInEverySeed_SpreadOut_AwayFromTheStart()
        {
            var sb = new StringBuilder();
            foreach (int seed in new[] { 777, 4242, 1, 2, 3, 99, 31337, 123456 })
            {
                var ws = MakeStreamer(seed, Vector2.zero);
                Assert.AreEqual(306f, ws.WorldSize, 1e-3f, "the cozy world, 15 % smaller since 2026-09-22");
                var slots = ws.WorldSlots();
                Assert.AreEqual(ws.worldIslands, slots.Count, "seed " + seed);
                // Barren islands are planned 0.8 times smaller, so a small one can drop under the islet bound as well.
                float isletMax = 2f * Mathf.Sqrt(ws.smallIslandAreaScale);
                Assert.GreaterOrEqual(slots.Count(s => s.radius < isletMax), ws.worldIslets, "islets, seed " + seed);
                Assert.GreaterOrEqual(slots.Min(s => s.radius), 1.2f * 0.8f * Mathf.Sqrt(ws.smallIslandAreaScale) - 1e-3f, "doubled minimum area, seed " + seed);
                Assert.That(slots.Count(s => s.radius >= 9f * 0.8f), Is.InRange(ws.worldLarge, ws.worldLarge + ws.worldBig), "large, seed " + seed);

                float w = ws.WorldSize, nearestSum = 0f, minPair = float.MaxValue, area = 0f;
                for (int i = 0; i < slots.Count; i++)
                {
                    area += slots[i].EstimatedArea;
                    Vector2 s0 = slots[i].pos;
                    Assert.That(s0.x >= 0f && s0.x < w && s0.y >= 0f && s0.y < w, "wrapped position");
                    Vector2 ds = s0;
                    ds.x -= w * Mathf.Round(ds.x / w);
                    ds.y -= w * Mathf.Round(ds.y / w);
                    Assert.Greater(ds.magnitude, ws.startExclusion, "start exclusion, seed " + seed);
                    float nearest = float.MaxValue;
                    for (int j = 0; j < slots.Count; j++)
                    {
                        if (i == j) continue;
                        Vector2 d = slots[i].pos - slots[j].pos;
                        d.x -= w * Mathf.Round(d.x / w);
                        d.y -= w * Mathf.Round(d.y / w);
                        nearest = Mathf.Min(nearest, d.magnitude);
                    }
                    nearestSum += nearest;
                    minPair = Mathf.Min(minPair, nearest);
                }
                float meanNearest = nearestSum / slots.Count;
                Assert.Greater(meanNearest, 42f, "islands are spread out, seed " + seed);
                Assert.That(area, Is.InRange(700f, 2200f), "world land, seed " + seed);
                sb.AppendLine($"seed {seed}: {slots.Count} islands, area {area:F0}, largest r {slots.Max(s => s.radius):F1}, nearest centre mean {meanNearest:F0} / min {minPair:F0}");
            }
            Debug.Log("[CozyWorld] plan\n" + sb);
        }

        [Test]
        public void SmallWorld_ShowsEveryIslandOnce_WhileDrivingAcrossTheWrap()
        {
            var player = CozyRunBot.MakePlayer(_objects, new Vector2(30000f, 30000f), 12346, GameMode.Cozy);
            var go = new GameObject("TestStreamer");
            go.SetActive(false);
            _objects.Add(go);
            var ws = go.AddComponent<WorldStreamer>();
            ws.player = player;
            ws.seed = 4242;
            go.SetActive(true);
            for (int step = 0; step < 14; step++)
            {
                player.SetPlanarPosition(new Vector2(30000f + 45f * step, 30000f + 20f * step));
                ws.StreamAround(true);
                var live = go.GetComponentsInChildren<Island>();
                Assert.LessOrEqual(live.Length, ws.worldIslands);
                Assert.AreEqual(live.Length, live.Select(i => i.shapeSeed).Distinct().Count(), "an island twice at step " + step);
            }
        }

        [Test]
        public void Progress_IslandsLeftReachesZero_ExactlyWhenTheLastWorldIslandIsGone()
        {
            var player = CozyRunBot.MakePlayer(_objects, new Vector2(30000f, 30000f), 12346, GameMode.Cozy);
            var go = new GameObject("TestStreamer");
            go.SetActive(false);
            _objects.Add(go);
            var ws = go.AddComponent<WorldStreamer>();
            ws.player = player;
            ws.seed = 777;
            go.SetActive(true);
            ws.StreamAround(true);
            ws.Progress(out _, out int left);
            Assert.AreEqual(ws.worldIslands, left);

            // Visit every chunk of the 4 x 4 world and take its islands away one by one.
            float c = ws.ChunkSize;
            int expected = left;
            for (int cx = 0; cx < ws.WorldChunks; cx++)
                for (int cz = 0; cz < ws.WorldChunks; cz++)
                {
                    float bx = Mathf.Floor(30000f / c) * c, bz = Mathf.Floor(30000f / c) * c;
                    player.SetPlanarPosition(new Vector2(bx + (cx + 0.5f) * c, bz + (cz + 0.5f) * c));
                    ws.StreamAround(true);
                    foreach (var isl in go.GetComponentsInChildren<Island>())
                    {
                        Assert.Greater(expected, 0, "an island beyond the plan");
                        Object.DestroyImmediate(isl.gameObject);
                        expected--;
                        ws.Progress(out _, out left);
                        Assert.AreEqual(expected, left, "counted in the same frame");
                    }
                }
            Assert.AreEqual(0, left);
            Assert.AreEqual(1f, ws.MergedShare);
            ws.StreamAround(true);
            Assert.AreEqual(0, go.GetComponentsInChildren<Island>().Length, "nothing respawns");
        }

        [Test]
        public void LegacyLayout_KeepsTheBigWorld_UntilTheNextReset()
        {
            var ws = MakeStreamer(777, Vector2.zero);
            Assert.AreEqual(WorldStreamer.WorldGenVersion, ws.Layout);
            ws.UseLayout(WorldStreamer.LegacyWorldGenVersion);
            ws.RestoreState(Vector2.zero, null);
            Assert.IsTrue(ws.IsLegacyLayout);
            Assert.AreEqual(660f, ws.WorldSize);
            Assert.Greater(ws.WorldSlots().Count, 120);

            // A run started in the first small world (360 u) keeps its size and its islands, too.
            ws.UseLayout(WorldStreamer.MediumWorldGenVersion);
            ws.RestoreState(Vector2.zero, null);
            Assert.IsFalse(ws.IsLegacyLayout);
            Assert.AreEqual(360f, ws.WorldSize, 1e-3f);
            Assert.AreEqual(ws.worldIslands, ws.WorldSlots().Count);
            ws.ResetWorld();
            ws.RestoreState(Vector2.zero, null);
            Assert.AreEqual(WorldStreamer.WorldGenVersion, ws.Layout);
            Assert.AreEqual(ws.worldIslands, ws.WorldSlots().Count);
        }

        [TestCase(777)]
        [TestCase(4242)]
        [TestCase(20260922)]
        public void CozyRun_ABotUnitesTheSmallWorld_WellUnderTwentyMinutes(int seed)
        {
            var r = CozyRunBot.Run(_objects, seed);
            string line = r.ToString();
            Debug.Log("[CozyWorld] " + line);
            Assert.IsTrue(r.united, line);
            Assert.AreEqual(0, r.islandsLeft);
            Assert.GreaterOrEqual(r.merges, r.worldIslands - 2, "merged (nearly) every island itself: " + line);
            Assert.Less(r.seconds, 12f * 60f, "driving alone stays well under 20 minutes: " + line);
            Assert.That(r.MedianLeg, Is.InRange(5f, 25f), line);
        }
    }
}
