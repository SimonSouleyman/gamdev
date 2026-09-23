using System.Collections.Generic;
using Drift.Core;
using Drift.Islands;
using Drift.Tectonics;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The adventure round after the 2026-09-23 play test: surf lanes that hold ~2 s longer, more islands and rising
    // volcanoes level by level (never closing the track), the escort as a pace and not a boost, and a grace after a
    // hit so one mistake does not cascade.
    public class AdventureDifficultyTests
    {
        const float C = 640f;
        const float HW = 28f;
        // Far off the cozy world the scene keeps in edit mode: its islands would wrap into the ring window otherwise.
        const float FarX = 20000f;
        static readonly RingGeometry Far = new RingGeometry(FarX, HW, C);

        readonly List<GameObject> _objects = new();
        System.Func<Vector2, Vector2> _savedConstraint;
        PlateSystem _savedPlates;

        [SetUp]
        public void SetUp()
        {
            _savedConstraint = Island.PositionConstraint;
            _savedPlates = PlateSystem.Instance;
            Island.PositionConstraint = null;
        }

        [TearDown]
        public void TearDown()
        {
            Island.PositionConstraint = _savedConstraint;
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
            // A test plate system took over the static instance: give the scene its own back.
            if (_savedPlates != null && PlateSystem.Instance != _savedPlates && _savedPlates.isActiveAndEnabled)
            {
                _savedPlates.enabled = false;
                _savedPlates.enabled = true;
            }
        }

        GameObject New(string name)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            _objects.Add(go);
            return go;
        }

        Island MakeIsland(Vector2 pos, int seed, float radius, bool player)
        {
            var go = New(player ? "DiffPlayer" : "DiffIsle_" + seed);
            go.transform.position = new Vector3(pos.x, 0f, pos.y);
            var isl = go.AddComponent<Island>();
            isl.useKeyboardInput = player;
            isl.sinkEnabled = player;
            isl.ModeOverride = GameMode.Adventure;
            isl.landRadius = radius;
            isl.cellSize = IslandArchetypes.CellSize(radius);
            isl.shapeSeed = seed;
            isl.carryResponse = 0f;
            isl.surfResponse = 0f;
            go.SetActive(true);
            return isl;
        }

        static int RegularSeed(int from)
        {
            while (Island.KindForSeed(from) != IslandKind.Regular) from++;
            return from;
        }

        // ---- 1) surf lanes hold longer ----

        PlateSystem MakePlates(int seed, float plateLength, float rowDrift, out Transform focus)
        {
            var f = New("DiffPlateFocus");
            f.SetActive(true);
            focus = f.transform;
            var go = New("DiffPlates");
            var ps = go.AddComponent<PlateSystem>();
            ps.seed = seed;
            ps.showBorders = false;
            ps.focus = focus;
            ps.ringPlateLength = plateLength;
            ps.ringRowDrift = rowDrift;
            go.SetActive(true);
            ps.SetRingLayout(0f, HW, C);
            return ps;
        }

        // Rides the lane boundary it starts on at `speed` for `seconds` (following it across the band like a player
        // on the wave) and returns the mean time between two changes of the boundary under the island, i.e. how
        // long one lane boundary holds.
        static float MeanHold(PlateSystem ps, Transform focus, float speed, float seconds)
        {
            float x = -HW + 2f * HW / ps.ringLanes, z = 0f, since = 0f, sum = 0f;
            const float dt = 0.05f;
            int changes = 0;
            Vector2Int keyA = new Vector2Int(int.MinValue, 0), keyB = keyA;
            for (float t = 0f; t < seconds; t += dt)
            {
                ps.Step(dt);
                z += speed * dt;
                since += dt;
                focus.position = new Vector3(x, 0f, z);
                if (!ps.NearestSeam(new Vector2(x, z), 14f, out var b, out float bt, out Vector2 q)) continue;
                if (Mathf.Abs(ps.SeamTangent(b, bt).y) < 0.6f) continue;
                bool same = (b.a.cell == keyA && b.b.cell == keyB) || (b.a.cell == keyB && b.b.cell == keyA);
                if (!same)
                {
                    if (keyA.x != int.MinValue) { sum += since; changes++; }
                    keyA = b.a.cell;
                    keyB = b.b.cell;
                    since = 0f;
                }
                x = Mathf.MoveTowards(x, q.x, 15f * dt);
            }
            return changes > 0 ? sum / changes : seconds;
        }

        [Test]
        public void SurfLanes_HoldAboutTwoSecondsLonger()
        {
            var ps = MakePlates(4242, 160f, 0.12f, out var focus);
            Assert.AreEqual(160f, ps.RingCellLength, 1e-3f, "four whole lane sections per lap");
            float newFast = MeanHold(ps, focus, 26f, 60f);
            float newSlow = MeanHold(ps, focus, 20f, 60f);

            var old = MakePlates(4242, 110f, 0.2f, out var oldFocus);
            float oldFast = MeanHold(old, oldFocus, 26f, 60f);
            float oldSlow = MeanHold(old, oldFocus, 20f, 60f);

            string report = $"26 u/s: {oldFast:F2} -> {newFast:F2} s, 20 u/s: {oldSlow:F2} -> {newSlow:F2} s";
            Debug.Log("Lane hold: " + report);
            Assert.Greater(newFast - oldFast, 1.6f, report);
            Assert.Greater(newSlow - oldSlow, 1.8f, report);
            Assert.Greater(newFast, 5.5f, report);
            Assert.Less(newSlow, 11f, "but the lanes still change over a run: " + report);
        }

        // ---- 2) more islands and volcanoes, level by level ----

        RingWorld MakeRingWorld()
        {
            var go = New("DiffRingWorld");
            var ring = go.AddComponent<RingWorld>();
            ring.GetComponent<RingIslandSpawner>().life = false;
            // Only the difficulty arithmetic is used: never let it take over the scene's ring (an Editor left in Abenteuer).
            ring.enabled = false;
            go.SetActive(true);
            return ring;
        }

        [Test]
        public void ObstaclesGrowLevelByLevel()
        {
            var ring = MakeRingWorld();
            ring.levelSeconds = 45f;
            ring.extraIslandsPerLevel = 2f;
            ring.extraIslandsAtMax = 14;
            ring.volcanoFirstLevel = 2;
            ring.volcanoesPerLevel = 1f;
            ring.maxVolcanoes = 8;

            Assert.AreEqual(0, ring.ExtraIslandsFor(1));
            Assert.AreEqual(2, ring.ExtraIslandsFor(2));
            Assert.AreEqual(6, ring.ExtraIslandsFor(4));
            Assert.AreEqual(12, ring.ExtraIslandsFor(7));
            Assert.AreEqual(14, ring.ExtraIslandsFor(30), "capped");

            Assert.AreEqual(0, ring.VolcanoesFor(0f));
            Assert.AreEqual(0, ring.VolcanoesFor(44.9f), "no volcano on the first level");
            Assert.AreEqual(1, ring.VolcanoesFor(45f), "the first one rises as level 2 starts");
            Assert.AreEqual(1, ring.VolcanoesFor(89f));
            Assert.AreEqual(2, ring.VolcanoesFor(90f));
            Assert.AreEqual(6, ring.VolcanoesFor(270f), "one more every level");
            Assert.AreEqual(8, ring.VolcanoesFor(3000f), "capped");
            int last = 0;
            for (float t = 0f; t < 600f; t += 5f)
            {
                int n = ring.VolcanoesFor(t) + ring.ExtraIslandsFor(1 + Mathf.FloorToInt(t / 45f));
                Assert.GreaterOrEqual(n, last, "never fewer obstacles later in the run");
                last = n;
            }
        }

        (RingIslandSpawner spawner, Island player) MakeSpawnerRing(Vector2 start, int mains, int islets)
        {
            var player = MakeIsland(start, 4711, 3f, true);
            var go = New("DiffSpawner");
            var spawner = go.AddComponent<RingIslandSpawner>();
            spawner.life = false;
            spawner.mainIslands = mains;
            spawner.islets = islets;
            go.SetActive(true);
            spawner.ResetRing(Far, start, 7, true);
            return (spawner, player);
        }

        // Widest gap (coast to coast) left across the band where the cone stands, every island there counted.
        static float WidestLane(Vector2 at, float reach, float window)
        {
            var spans = new List<Vector2> { new Vector2(Far.MinX + 1f, Far.MaxX - 1f) };
            void Cut(float a, float b)
            {
                for (int k = spans.Count - 1; k >= 0; k--)
                {
                    Vector2 s = spans[k];
                    if (b <= s.x || a >= s.y) continue;
                    spans.RemoveAt(k);
                    if (a > s.x) spans.Add(new Vector2(s.x, a));
                    if (b < s.y) spans.Add(new Vector2(b, s.y));
                }
            }
            foreach (var isl in Island.All)
            {
                if (isl == null || !isl.isActiveAndEnabled || isl.useKeyboardInput) continue;
                float r = isl.IsEmerging ? Mathf.Max(isl.BoundingRadius, isl.landRadius * 1.2f + 0.5f) : isl.BoundingRadius;
                if (Mathf.Abs(Far.AlongDelta(isl.PlanarPosition.y, at.y)) >= window + r) continue;
                Cut(isl.PlanarPosition.x - r, isl.PlanarPosition.x + r);
            }
            float best = 0f;
            foreach (var s in spans) best = Mathf.Max(best, s.y - s.x);
            return best;
        }

        [Test]
        public void Volcanoes_RiseAheadOfThePlayer_AndAlwaysLeaveALane()
        {
            var start = new Vector2(FarX, 5000f);
            var (spawner, player) = MakeSpawnerRing(start, 10, 3);
            int mains = spawner.MainCount;
            const float speed = 24f;
            int raised = 0;
            for (int i = 0; i < 8; i++)
            {
                var v = spawner.TryRaiseVolcano(Far, start, 1f, speed, player.BoundingRadius);
                if (v == null) continue;
                raised++;
                Assert.IsTrue(v.isVolcano);
                Assert.IsTrue(v.IsEmerging, "it rises out of the sea, it does not pop up");
                float dz = Far.AlongDelta(v.PlanarPosition.y, start.y);
                Assert.GreaterOrEqual(dz, spawner.volcanoAheadMin - 1e-3f, "never right under or beside the player");
                Assert.LessOrEqual(dz, spawner.volcanoAheadMax + 1e-3f, "ahead, where it can be seen rising");
                Assert.IsTrue(Far.Inside(v.PlanarPosition, 0f), "on the band: " + v.PlanarPosition);
                // Fully up before the player gets there (at least nearly volcanoRiseDoneGap away).
                Assert.GreaterOrEqual(dz - v.EmergeRemaining * speed, 30f, "up in time: " + dz + " u, " + v.EmergeRemaining + " s");
                float lane = WidestLane(v.PlanarPosition, v.landRadius * 1.2f + 0.5f, v.landRadius * 1.2f + 0.5f + 2f * player.BoundingRadius);
                Assert.GreaterOrEqual(lane, spawner.volcanoFreeLane - 1e-3f, "a free lane stays beside it");
            }
            Assert.GreaterOrEqual(raised, 3, "even the same 55 u stretch of a crowded ring takes several (in the race each one gets a fresh stretch)");
            Assert.AreEqual(raised, spawner.VolcanoCount);
            Assert.AreEqual(mains, spawner.MainCount, "volcanoes come on top of the islands, they replace none");

            // Once up they are solid obstacles like every other island.
            var list = new List<Island>();
            spawner.CollectIslands(list);
            foreach (var isl in list)
                if (isl.isVolcano)
                {
                    for (int k = 0; k < 80 && isl.IsEmerging; k++) isl.AdvanceEmergence(0.1f);
                    Assert.IsFalse(isl.IsEmerging);
                    Assert.Greater(isl.BoundingRadius, 1.5f, "the cone stands out of the water");
                }
        }

        [Test]
        public void Volcanoes_NeverCloseTheTrack()
        {
            var start = new Vector2(FarX, 7000f);
            var (spawner, player) = MakeSpawnerRing(start, 0, 0);
            // A wall across the whole stretch where volcanoes could rise, with one narrow gap: nothing may go in.
            for (float z = start.y + 70f; z <= start.y + 190f; z += 12f)
            {
                MakeIsland(new Vector2(FarX - 19f, z), RegularSeed(300 + (int)z), 5f, false);
                MakeIsland(new Vector2(FarX + 12f, z), RegularSeed(500 + (int)z), 5f, false);
            }
            for (int i = 0; i < 6; i++)
                Assert.IsNull(spawner.TryRaiseVolcano(Far, start, 1f, 24f, player.BoundingRadius), "the only lane stays open");
            Assert.AreEqual(0, spawner.VolcanoCount);
        }

        [Test]
        public void RingIslands_HideTheirLifeEarly()
        {
            var go = New("DiffLifeSpawner");
            var spawner = go.AddComponent<RingIslandSpawner>();
            spawner.mainIslands = 2;
            spawner.islets = 0;
            spawner.lifeHideDistance = 90f;
            go.SetActive(true);
            spawner.ResetRing(Far, new Vector2(FarX, 9000f), 3, true);
            var list = new List<Island>();
            spawner.CollectIslands(list);
            Assert.AreEqual(2, list.Count);
            foreach (var isl in list)
            {
                Assert.AreEqual(90f, isl.GetComponent<Drift.Life.IslandLifeSystem>().hideDistance);
                Assert.AreEqual(90f, isl.GetComponent<Drift.Life.IslandHerdSystem>().hideDistance);
                Assert.AreEqual(90f, isl.GetComponent<Drift.Life.IslandCrittersSystem>().hideDistance);
            }
        }

        // ---- 3) the escort is a pace, not a boost ----

        [Test]
        public void Escort_RaisesThePace_ButIsNoBoost()
        {
            var player = MakeIsland(Vector2.zero, 811, 3f, true);
            player.AdventureSpeedScale = 1.45f;
            float plain = player.MaxSpeed;
            player.EscortFactor = 1.12f;
            Assert.AreEqual(plain * 1.12f, player.MaxSpeed, 1e-3f);
            Assert.IsFalse(player.Boosting, "an escort swimming along is not the flotsam surge");
            player.SpeedBoost(3f, 1.65f);
            Assert.IsTrue(player.Boosting);
            player.ModeOverride = GameMode.Cozy;
            player.EscortFactor = 1.5f;
            Assert.Less(player.MaxSpeed, plain, "Gemütlich knows no escort pace");
        }

        // ---- 4) a grace after a hit ----

        struct HitRun
        {
            public int hits;
            public float endZ, endX;
        }

        HitRun DriveIntoARock(float grace, float sidestep)
        {
            var world = New("DiffWorld").AddComponent<IslandWorld>();
            world.gameObject.SetActive(true);
            var player = MakeIsland(Vector2.zero, 812, 3f, true);
            player.AdventureCruise = 0.62f;
            player.AdventureSpeedScale = 1.45f;
            player.AdventureTrack = Vector2.up;
            player.hitGrace = grace;
            player.hitSidestep = sidestep;
            // A big rock straight ahead, a little to the left of the island's line.
            var rock = MakeIsland(new Vector2(-1f, 22f), RegularSeed(77), 7f, false);
            var pair = new List<Island> { player, rock };
            const float dt = 0.02f;
            // Hands off the controls for six seconds: the ring drives the island on by itself.
            for (float t = 0f; t < 6f; t += dt)
            {
                player.Tick(Vector2.zero, dt);
                world.Step(dt, pair);
            }
            var r = new HitRun { hits = player.Hits, endZ = player.PlanarPosition.y, endX = player.PlanarPosition.x };
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
            return r;
        }

        [Test]
        public void HitGrace_OneMistakeDoesNotCascade()
        {
            var before = DriveIntoARock(0f, 0f);
            var after = DriveIntoARock(2.2f, 7f);
            string report = $"without grace {before.hits} hits (z {before.endZ:F0}), with grace {after.hits} hits (z {after.endZ:F0}, x {after.endX:F1})";
            Debug.Log("Hit grace: " + report);
            Assert.GreaterOrEqual(before.hits, 2, "the old spiral: the ring drove the island back into the same rock. " + report);
            Assert.AreEqual(1, after.hits, "one mistake is one hit: " + report);
            Assert.Greater(after.endZ, 22f + 7f, "and the race goes on past the rock: " + report);
        }

        [Test]
        public void HitGrace_GlidesThroughAndShovesClear()
        {
            var world = New("DiffWorld2").AddComponent<IslandWorld>();
            world.gameObject.SetActive(true);
            var player = MakeIsland(Vector2.zero, 813, 3f, true);
            player.AdventureCruise = 0.62f;
            player.AdventureSpeedScale = 1.45f;
            player.AdventureTrack = Vector2.up;
            var rock = MakeIsland(new Vector2(-1.5f, player.BoundingRadius + 3f), RegularSeed(78), 4f, false);
            var pair = new List<Island> { player, rock };
            player.SetSelfVelocity(new Vector2(0f, 8f));
            float buoy = player.RawBuoyancy;
            for (int i = 0; i < 60 && player.Hits == 0; i++)
            {
                player.Tick(Vector2.zero, 0.02f);
                world.Step(0.02f, pair);
            }
            Assert.AreEqual(1, player.Hits);
            Assert.IsTrue(player.HitGrace);
            Assert.Greater(player.SelfVelocity.x, 0.8f * player.hitSidestep, "shoved to the side away from the rock");
            float afterHit = player.RawBuoyancy;
            Assert.AreEqual(buoy - player.hitBuoyancyLoss, afterHit, 1e-3f);

            // A contact inside the grace costs nothing and does not bounce.
            Vector2 v = player.SelfVelocity;
            Assert.IsFalse(player.Bump(rock, Vector2.zero, 0.02f));
            Assert.AreEqual(v, player.SelfVelocity);
            Assert.AreEqual(afterHit, player.RawBuoyancy, 1e-4f);
            // It lasts while the island is still inside a coast, then runs out.
            for (int i = 0; i < 300 && player.HitGrace; i++)
            {
                player.Bump(rock, Vector2.zero, 0.02f);
                player.Tick(Vector2.zero, 0.02f);
                if (i == 150) Assert.IsTrue(player.HitGrace, "held while gliding through");
            }
            for (int i = 0; i < 30; i++) player.Tick(Vector2.zero, 0.02f);
            Assert.IsFalse(player.HitGrace, "and ends once clear");
        }
    }
}
