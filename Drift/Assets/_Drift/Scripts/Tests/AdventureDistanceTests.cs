using System.Collections.Generic;
using Drift.Core;
using Drift.Islands;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Abenteuer scores the distance along the ring, the levels follow it, and Tilda's briefing holds the race at the
    // start line (owner, 2026-09-23: "nicht nach Zeit, sondern nach zurückgelegter Strecke"; "die Insel soll erst
    // losfahren können, wenn das Tutorial fertig ist").
    public class AdventureDistanceTests
    {
        readonly List<GameObject> _objects = new();
        System.Func<Vector2, Vector2> _savedConstraint;
        bool _savedHeld, _savedLocked, _savedDirect;

        [SetUp]
        public void SetUp()
        {
            _savedConstraint = Island.PositionConstraint;
            _savedHeld = RingWorld.StartHeld;
            _savedLocked = Island.InputLocked;
            _savedDirect = Island.DirectionSteering;
            Island.PositionConstraint = null;
            RingWorld.StartHeld = false;
            Island.InputLocked = false;
            Island.DirectionSteering = false;
        }

        [TearDown]
        public void TearDown()
        {
            Island.PositionConstraint = _savedConstraint;
            RingWorld.StartHeld = _savedHeld;
            Island.InputLocked = _savedLocked;
            Island.DirectionSteering = _savedDirect;
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        GameObject New(string name)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            _objects.Add(go);
            return go;
        }

        Island MakeRacer(Vector2 pos)
        {
            var go = New("DistPlayer");
            go.transform.position = new Vector3(pos.x, 0f, pos.y);
            var isl = go.AddComponent<Island>();
            isl.useKeyboardInput = true;
            isl.sinkEnabled = true;
            isl.ModeOverride = GameMode.Adventure;
            isl.landRadius = 3f;
            isl.cellSize = IslandArchetypes.CellSize(3f);
            isl.shapeSeed = 4711;
            isl.carryResponse = 0f;
            isl.surfResponse = 0f;
            go.SetActive(true);
            return isl;
        }

        RingWorld MakeRing(Island player)
        {
            var go = New("DistRingWorld");
            var ring = go.AddComponent<RingWorld>();
            ring.GetComponent<RingIslandSpawner>().life = false;
            // Stepped by hand: never let it take over the scene's ring (an Editor left in Abenteuer).
            ring.enabled = false;
            go.SetActive(true);
            ring.player = player;
            Island.PositionConstraint = ring.ClampPlayer;
            return ring;
        }

        // One frame in the order of the game: RingWorld (execution order -40) before the islands.
        static void Frame(RingWorld ring, Island player, float dt)
        {
            ring.StepRace(dt);
            player.Tick(Vector2.zero, dt);
        }

        // ---- the odometer ----

        [Test]
        public void Odometer_CountsNewGroundOnlyWhileTheRaceRuns()
        {
            var o = new RunOdometer();
            o.Step(100f, true);
            Assert.AreEqual(0f, o.Distance, "the first sample only sets the start");
            o.Step(110f, true);
            Assert.AreEqual(10f, o.Distance, 1e-4f);
            o.Step(104f, true);
            Assert.AreEqual(10f, o.Distance, 1e-4f, "never negative");
            o.Step(112f, true);
            Assert.AreEqual(12f, o.Distance, 1e-4f, "the metres after a bounce back are not counted twice");
            o.Step(130f, false);
            Assert.AreEqual(12f, o.Distance, 1e-4f, "held, paused or sunk: nothing counts");
            o.Step(131f, true);
            Assert.AreEqual(13f, o.Distance, 1e-4f, "and the ground passed meanwhile is not counted later");
            o.Step(131f + RunOdometer.TeleportDistance + 5f, true);
            Assert.AreEqual(13f, o.Distance, 1e-4f, "a reset or load is no distance");
            o.Reset();
            Assert.AreEqual(0f, o.Distance);
        }

        // ---- levels by distance ----

        [Test]
        public void Levels_FollowTheDistanceAndLastTheSameTimeAtTheBasePace()
        {
            var ring = MakeRing(MakeRacer(new Vector2(0f, 5000f)));
            ring.levelSeconds = 45f;
            Assert.AreEqual(ring.levelDistance, ring.LevelLength(1), 1e-3f);
            float basePace1 = ring.CruiseAt(1) * ring.PaceScaleAt(1);
            for (int level = 1; level <= 14; level++)
            {
                float pace = ring.CruiseAt(level) * ring.PaceScaleAt(level) / basePace1;
                Assert.AreEqual(ring.levelDistance * pace, ring.LevelLength(level), 1e-2f, "level " + level + " is as much longer as it is faster");
                if (level > 1) Assert.GreaterOrEqual(ring.LevelLength(level), ring.LevelLength(level - 1));
                float start = ring.LevelStartDistance(level);
                Assert.AreEqual(level, ring.LevelAt(start + 0.01f), "level " + level + " starts at " + start);
                if (level > 1) Assert.AreEqual(level - 1, ring.LevelAt(start - 0.01f));
                Assert.AreEqual((level - 1) * 45f, ring.ProgressAt(start + 1e-3f, out _), 0.05f, "whole levels are levelSeconds each");
                ring.SetRunDistance(start + 1f);
                Assert.AreEqual(level, ring.Level);
                Assert.AreEqual(ring.CruiseAt(level), ring.Cruise, 1e-6f);
            }
            Assert.Greater(ring.LevelLength(8), 2f * ring.LevelLength(1), "the later levels are much longer: the pace doubles");

            Assert.AreEqual(1, ring.LevelAt(0f));
            Assert.AreEqual(1, ring.LevelAt(-50f));
            int last = 0;
            for (float d = 0f; d < 200000f; d += 997f)
            {
                int l = ring.LevelAt(d);
                Assert.GreaterOrEqual(l, last, "never back down a level");
                last = l;
            }
            Assert.Greater(last, 64, "long runs are fine past the walked levels");
            Assert.AreEqual(ring.LevelStartDistance(80), ring.DistanceForProgress(79f * 45f), 0.5f);
            Assert.AreEqual(80, ring.LevelAt(ring.LevelStartDistance(80) + 1f));
        }

        [Test]
        public void Levels_TheFirstIsAboutFortyFiveSecondsAtTheStartPace()
        {
            var player = MakeRacer(new Vector2(0f, 5000f));
            var ring = MakeRing(player);
            player.AdventureSpeedScale = ring.PaceScaleAt(1);
            player.AdventureCruise = ring.CruiseAt(1);
            float seconds = ring.levelDistance / player.AdventureBaseSpeed;
            Assert.AreEqual(ring.levelSeconds, seconds, 12f, "level 1 = " + seconds.ToString("F1") + " s at " + player.AdventureBaseSpeed.ToString("F2") + " u/s");
        }

        // ---- the race, stepped frame by frame ----

        [Test]
        public void Race_ScoresTheDistanceDriven()
        {
            var player = MakeRacer(new Vector2(0f, 5000f));
            var ring = MakeRing(player);
            float z0 = player.PlanarPosition.y;
            for (int i = 0; i < 200; i++) Frame(ring, player, 0.05f);
            float driven = player.PlanarPosition.y - z0;
            Assert.Greater(driven, 30f, "it races by itself");
            Assert.AreEqual(driven, ring.RunDistance, 1f, "the score is the ground covered along the track");
            Assert.AreEqual(10f, ring.RunSeconds, 0.06f);

            player.sinkEnabled = false;
            float held = ring.RunDistance;
            for (int i = 0; i < 20; i++) Frame(ring, player, 0.05f);
            Assert.AreEqual(held, ring.RunDistance, 1e-4f, "only while the island may sink does the run count");
        }

        [Test]
        public void StartHold_TheIslandWaitsAndNothingCounts()
        {
            var player = MakeRacer(new Vector2(0f, 5000f));
            var ring = MakeRing(player);
            RingWorld.StartHeld = true;
            Vector2 start = player.PlanarPosition;
            for (int i = 0; i < 100; i++)
            {
                Frame(ring, player, 0.05f);
                // A bump or the plate current shoves it: the hold puts it straight back.
                if (i == 40) player.SetPlanarPosition(start + new Vector2(2f, 3f));
            }
            Assert.IsTrue(ring.Holding);
            Assert.AreEqual(0f, (player.PlanarPosition - start).magnitude, 1e-3f, "the island stands at the start line");
            Assert.AreEqual(0f, player.AdventureCruise, "no cruise while Tilda talks");
            Assert.AreEqual(0f, player.SelfVelocity.magnitude, 1e-4f);
            Assert.AreEqual(0f, ring.RunDistance);
            Assert.AreEqual(0f, ring.RunSeconds, "the clock stands still as well");

            // "Los!": the race starts at once from the same spot.
            RingWorld.StartHeld = false;
            Frame(ring, player, 0.05f);
            Assert.IsFalse(ring.Holding);
            Assert.Greater(player.AdventureCruise, 0f);
            for (int i = 0; i < 100; i++) Frame(ring, player, 0.05f);
            Assert.Greater(player.PlanarPosition.y - start.y, 15f);
            Assert.AreEqual(player.PlanarPosition.y - start.y, ring.RunDistance, 1f);
        }

        [Test]
        public void StartHold_AReplayInTheMiddleOfARunStopsTheIslandWhereItIs()
        {
            var player = MakeRacer(new Vector2(0f, 5000f));
            var ring = MakeRing(player);
            for (int i = 0; i < 100; i++) Frame(ring, player, 0.05f);
            Assert.Greater(player.TrackSpeed, 3f, "racing");

            RingWorld.StartHeld = true;
            Frame(ring, player, 0.05f);
            Vector2 at = player.PlanarPosition;
            float distance = ring.RunDistance, seconds = ring.RunSeconds;
            for (int i = 0; i < 60; i++) Frame(ring, player, 0.05f);
            Assert.AreEqual(0f, (player.PlanarPosition - at).magnitude, 1e-3f, "it holds again, right where it was");
            Assert.AreEqual(distance, ring.RunDistance, 1e-3f);
            Assert.AreEqual(seconds, ring.RunSeconds, 1e-4f);

            RingWorld.StartHeld = false;
            for (int i = 0; i < 60; i++) Frame(ring, player, 0.05f);
            Assert.Greater(ring.RunDistance, distance + 5f, "and races on after it");
        }

        [Test]
        public void StartHold_NothingOnTheTrackMoves()
        {
            var player = MakeRacer(new Vector2(0f, 5000f));
            var ring = MakeRing(player);
            ring.driftShareStart = 1f;
            ring.driftShareMax = 1f;
            ring.Spawner.mainIslands = 6;
            ring.Spawner.islets = 2;
            ring.Spawner.ResetRing(ring.Geometry, player.PlanarPosition, 7, true);
            var islands = new List<Island>();
            ring.Spawner.CollectIslands(islands);
            Assert.Greater(islands.Count, 3);

            RingWorld.StartHeld = true;
            Frame(ring, player, 0.05f);
            var where = new Dictionary<Island, Vector2>();
            foreach (var isl in islands) where[isl] = isl.PlanarPosition;
            for (int i = 0; i < 40; i++)
            {
                // Whatever carries them meanwhile (the plate lanes, their own drift) ...
                foreach (var isl in islands) isl.SetPlanarPosition(isl.PlanarPosition + new Vector2(0.3f, -0.4f));
                Frame(ring, player, 0.05f);
            }
            // ... the hold puts every obstacle back and gives none of them a speed of its own.
            foreach (var isl in islands)
            {
                Assert.AreEqual(0f, (isl.PlanarPosition - where[isl]).magnitude, 1e-3f, isl.name);
                Assert.AreEqual(0f, isl.SelfVelocity.magnitude, 1e-4f, isl.name + " does not drift");
            }

            RingWorld.StartHeld = false;
            Frame(ring, player, 0.05f);
            int drifting = 0;
            foreach (var isl in islands) if (isl.SelfVelocity.sqrMagnitude > 1e-4f) drifting++;
            Assert.Greater(drifting, 0, "after the start the islands drift across the track again");
        }
    }
}
