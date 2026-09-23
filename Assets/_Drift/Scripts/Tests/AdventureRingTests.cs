using System.Collections.Generic;
using Drift.Core;
using Drift.Islands;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The adventure ring after the obstacle round (owner: "statt sie zu rammen soll man ihnen ausweichen müssen"):
    // islands never merge with the player, a hit bounces it off and costs buoyancy, flotsam is the only way back up,
    // and the difficulty rises with the run. The bot at the end drives the real Island.Tick / IslandWorld.Step loop
    // twice: once steering around the islands, once straight ahead.
    public class AdventureRingTests
    {
        const float C = 640f;
        const float HW = 28f;
        static readonly RingGeometry Ring = new RingGeometry(0f, HW, C);

        readonly List<GameObject> _objects = new();
        System.Func<Vector2, Vector2> _savedConstraint;

        [SetUp]
        public void SetUp()
        {
            _savedConstraint = Island.PositionConstraint;
            // An Editor left in Abenteuer has the scene's RingWorld holding the player to its band: not these islands.
            Island.PositionConstraint = null;
        }

        [TearDown]
        public void TearDown()
        {
            Island.PositionConstraint = _savedConstraint;
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        Island MakeIsland(Vector2 pos, int seed, float radius, bool player)
        {
            var go = new GameObject(player ? "AdvPlayer" : "AdvIsle_" + seed);
            go.SetActive(false);
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
            _objects.Add(go);
            return isl;
        }

        IslandWorld MakeWorld()
        {
            var go = new GameObject("AdvWorld");
            go.SetActive(false);
            var w = go.AddComponent<IslandWorld>();
            go.SetActive(true);
            _objects.Add(go);
            return w;
        }

        static int RegularSeed(int from)
        {
            while (Island.KindForSeed(from) != IslandKind.Regular) from++;
            return from;
        }

        // ---- A) islands are obstacles ----

        [Test]
        public void Hitting_BouncesOffCostsBuoyancyAndNeverMerges()
        {
            var world = MakeWorld();
            var player = MakeIsland(Vector2.zero, 101, 3f, true);
            var rock = MakeIsland(new Vector2(0f, player.BoundingRadius + 2.4f), RegularSeed(55), 4f, false);
            var pair = new List<Island> { player, rock };

            player.SetSelfVelocity(new Vector2(0f, 6f));
            float buoyBefore = player.RawBuoyancy;
            for (int i = 0; i < 40 && player.Hits == 0; i++)
            {
                player.Tick(Vector2.zero, 0.05f);
                world.Step(0.05f, pair);
            }

            Assert.AreEqual(1, player.Hits, "the contact counted once");
            Assert.AreEqual(1, world.Bumps);
            Assert.IsNotNull(rock, "the obstacle is still there");
            Assert.IsTrue(rock.isActiveAndEnabled, "nothing is merged in Abenteuer");
            Assert.AreEqual(buoyBefore - player.hitBuoyancyLoss, player.RawBuoyancy, 1e-3f);
            Assert.Less(player.SelfVelocity.y, 0f, "bounced back along the contact normal");
            Assert.GreaterOrEqual(player.SelfVelocity.magnitude, player.hitMinBounce * 0.9f);
            Assert.IsTrue(player.HitStunned);

            // The waterline follows the lost buoyancy within a moment.
            float sink = player.Sink;
            for (int i = 0; i < 20; i++) player.AdvanceSink(0.1f);
            Assert.Greater(player.Sink, sink, "the island really sits lower after a hit");
        }

        [Test]
        public void ScrapingAlong_CostsOnlyOncePerCooldown()
        {
            var world = MakeWorld();
            var player = MakeIsland(Vector2.zero, 102, 3f, true);
            var rock = MakeIsland(new Vector2(0f, player.BoundingRadius + 2f), RegularSeed(56), 4f, false);
            var pair = new List<Island> { player, rock };

            for (float t = 0f; t < 1f; t += 0.05f)
            {
                player.SetSelfVelocity(new Vector2(0f, 6f));
                player.Tick(Vector2.zero, 0.05f);
                world.Step(0.05f, pair);
            }
            Assert.AreEqual(1, player.Hits, "one scrape is one hit, not twenty");
            Assert.Greater(player.HitCooldownLeft, 0f);
        }

        [Test]
        public void SeveralHitsAreSurvivable_ButSinkTheIslandStepByStep()
        {
            var player = MakeIsland(Vector2.zero, 103, 3f, true);
            int hits = 0;
            float last = player.RawBuoyancy;
            while (!player.IsSunk && hits < 20)
            {
                player.RemoveBuoyancy(player.hitBuoyancyLoss);
                hits++;
                Assert.Less(player.RawBuoyancy, last + 1e-4f);
                last = player.RawBuoyancy;
                for (int i = 0; i < 10; i++) player.AdvanceSink(0.1f);
            }
            Assert.GreaterOrEqual(hits, 4, "a couple of hits have to be survivable");
            Assert.LessOrEqual(hits, 8, "but they really hurt");
        }

        [Test]
        public void ObstacleIslandsPushEachOtherApartInsteadOfMerging()
        {
            var world = MakeWorld();
            var a = MakeIsland(Vector2.zero, RegularSeed(70), 4f, false);
            var b = MakeIsland(new Vector2(a.BoundingRadius, 0f), RegularSeed(80), 4f, false);
            var pair = new List<Island> { a, b };
            float before = Vector2.Distance(a.PlanarPosition, b.PlanarPosition);
            for (int i = 0; i < 40; i++) world.Step(0.05f, pair);
            Assert.IsTrue(a.isActiveAndEnabled && b.isActiveAndEnabled, "obstacles never merge either");
            Assert.Greater(Vector2.Distance(a.PlanarPosition, b.PlanarPosition), before, "they slide apart");
        }

        [Test]
        public void Flotsam_IsTheWayBackUp()
        {
            var player = MakeIsland(Vector2.zero, 104, 3f, true);
            for (int i = 0; i < 200; i++) player.AdvanceSink(0.1f);
            float low = player.RawBuoyancy;
            Assert.Less(low, 0.95f);
            player.AddBuoyancy(0.09f);
            Assert.AreEqual(low + 0.09f, player.RawBuoyancy, 1e-3f);
            Assert.Less(player.Sink, 1e9f);

            // A hit and two crates come out about even.
            player.RemoveBuoyancy(player.hitBuoyancyLoss);
            float after = player.RawBuoyancy;
            player.AddBuoyancy(0.09f);
            player.AddBuoyancy(0.09f);
            Assert.Greater(player.RawBuoyancy, after);
        }

        [Test]
        public void CruiseKeepsThePaceAndTheSpeedScaleRaisesTheTopSpeed()
        {
            var player = MakeIsland(Vector2.zero, 105, 3f, true);
            float plain = player.MaxSpeed;
            player.AdventureCruise = 0.6f;
            for (int i = 0; i < 200; i++) player.Tick(Vector2.zero, 0.05f);
            Assert.AreEqual(0.6f * plain, player.Speed, 0.3f, "the ring keeps the island going by itself");

            player.AdventureSpeedScale = 1.35f;
            Assert.AreEqual(1.35f * plain, player.MaxSpeed, 1e-3f);
            for (int i = 0; i < 200; i++) player.Tick(new Vector2(0f, 1f), 0.05f);
            Assert.Greater(player.Speed, plain, "and the difficulty makes it faster");

            // Cozy is untouched by the adventure knobs.
            player.ModeOverride = GameMode.Cozy;
            Assert.AreEqual(plain, player.MaxSpeed, 1e-3f);
            player.SetSelfVelocity(Vector2.zero);
            for (int i = 0; i < 100; i++) player.Tick(Vector2.zero, 0.05f);
            Assert.Less(player.Speed, 0.05f, "no forced pace in Gemütlich");
        }

        // ---- A2) the race: always driving, steering only, braking never stops it ----

        // Drives an adventure island for `seconds` with one held input, in one of the two steering schemes, and
        // reports the slowest and the average speed along the track.
        struct Drive
        {
            public float min, avg, end, acrossEnd;
        }

        Drive RaceFor(Island player, Vector2 wheel, Vector2 stick, bool directScheme, float seconds, float settle = 1.5f)
        {
            bool was = Island.DirectionSteering;
            Island.DirectionSteering = directScheme;
            try
            {
                const float dt = 0.02f;
                for (float t = 0f; t < settle; t += dt) player.Tick(wheel, stick, dt);
                var d = new Drive { min = float.MaxValue };
                int n = 0;
                for (float t = 0f; t < seconds; t += dt)
                {
                    player.Tick(wheel, stick, dt);
                    float along = player.TrackSpeed;
                    d.min = Mathf.Min(d.min, along);
                    d.avg += along;
                    n++;
                }
                d.avg /= Mathf.Max(1, n);
                d.end = player.TrackSpeed;
                d.acrossEnd = player.SelfVelocity.x;
                return d;
            }
            finally
            {
                Island.DirectionSteering = was;
            }
        }

        Island MakeRacer(int seed, float cruise = 0.62f, float scale = 1.45f)
        {
            var player = MakeIsland(Vector2.zero, seed, 3f, true);
            player.AdventureCruise = cruise;
            player.AdventureSpeedScale = scale;
            player.AdventureTrack = Vector2.up;
            return player;
        }

        // The owner's ask: "man fährt immer und kann nur nach rechts und links lenken; rückwärts bremst ein
        // bisschen, ganz stoppen kann man nicht" - and it has to hold in BOTH steering schemes.
        [Test]
        public void Race_AlwaysDrives_SteersSideways_AndBrakingNeverStops([Values(false, true)] bool directScheme)
        {
            var player = MakeRacer(201);
            float top = player.MaxSpeed;
            float expect = player.AdventureCruise * top;

            var idle = RaceFor(player, Vector2.zero, Vector2.zero, directScheme, 4f);
            Assert.AreEqual(expect, idle.avg, 0.4f, "hands off, the ring keeps the base pace");
            Assert.Greater(idle.min, 0.5f * expect, "and never falls away from it");

            // Full brake: clearly slower, still moving forwards.
            var braked = RaceFor(player, new Vector2(0f, -1f), new Vector2(0f, -1f), directScheme, 4f, 2.5f);
            Assert.AreEqual(expect * player.adventureBrakeMin, braked.avg, 0.4f, "braking reaches the floor");
            Assert.Less(braked.avg, 0.7f * expect, "clearly slower than cruising");
            Assert.Greater(braked.min, 0.2f, "but it never stops and never runs backwards");

            // Steering is sideways only; the pace along the track is kept all the while.
            var right = RaceFor(player, new Vector2(1f, 0f), new Vector2(1f, 0f), directScheme, 3f);
            Assert.Greater(right.acrossEnd, 0.5f * player.AdventureSteerSpeed, "it really pulls to the right");
            Assert.Greater(right.min, 0.7f * expect, "without losing the pace along the track");
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, player.Yaw), 1e-2f, "and the island never turns in the race");

            var left = RaceFor(player, new Vector2(-1f, 0f), new Vector2(-1f, 0f), directScheme, 3f);
            Assert.Less(left.acrossEnd, -0.5f * player.AdventureSteerSpeed);

            // Pushing forwards asks for the top speed, never for less than the base pace.
            var fast = RaceFor(player, new Vector2(0f, 1f), new Vector2(0f, 1f), directScheme, 3f);
            Assert.AreEqual(top, fast.avg, 0.5f, "full ahead is the top speed");
            Assert.Greater(fast.avg, idle.avg + 1f);
        }

        // Steering hard into the rim during the race: the band holds, the island keeps racing along the track.
        [Test]
        public void Race_SteeringIntoTheRimNeverStopsTheRun()
        {
            var player = MakeRacer(207);
            float margin = player.BoundingRadius;
            Island.PositionConstraint = p => Ring.ClampAcross(p, margin);
            for (int i = 0; i < 400; i++)
            {
                player.Tick(new Vector2(1f, 0f), 0.02f);
                Assert.LessOrEqual(player.PlanarPosition.x, HW - margin + 1e-3f);
            }
            Assert.AreEqual(HW - margin, player.PlanarPosition.x, 1e-2f, "resting against the invisible wall");
            Assert.Greater(player.TrackSpeed, 0.8f * player.AdventureBaseSpeed, "and still racing along the track");
            Assert.IsFalse(player.IsSunk);
        }

        [Test]
        public void Race_BothSteeringSchemesDriveTheSame()
        {
            var a = MakeRacer(202);
            var b = MakeRacer(203);
            var wheel = RaceFor(a, new Vector2(0.6f, -0.3f), Vector2.zero, false, 3f);
            var stickDrive = RaceFor(b, Vector2.zero, new Vector2(0.6f, -0.3f), true, 3f);
            Assert.AreEqual(wheel.avg, stickDrive.avg, 0.25f, "same pace");
            Assert.AreEqual(wheel.acrossEnd, stickDrive.acrossEnd, 0.25f, "same sideways pull");
        }

        [Test]
        public void Race_EveryLevelIsFaster()
        {
            var ring = MakeRingWorld();
            var player = MakeRacer(204);
            float last = 0f;
            for (int level = 1; level <= 6; level++)
            {
                ring.SetRunSeconds((level - 1) * ring.levelSeconds + 1f);
                Assert.AreEqual(level, ring.Level);
                player.AdventureSpeedScale = ring.PaceScale;
                player.AdventureCruise = ring.Cruise;
                float pace = RaceFor(player, Vector2.zero, Vector2.zero, false, 2f, 3f).avg;
                if (level > 1) Assert.Greater(pace, last + 0.4f, "level " + level + " is clearly faster than the one before");
                last = pace;
            }
            Assert.Greater(last, 12f, "by level 6 it really races: " + last.ToString("F1") + " u/s");
        }

        // A stagger (lightning, scraping a ship) really slows the island down: the cruise floor must not undo it.
        [Test]
        public void Stagger_SlowsTheIslandAndTheCruiseFloorCannotCancelIt()
        {
            var player = MakeRacer(205);
            float free = RaceFor(player, Vector2.zero, Vector2.zero, false, 2f).avg;
            Assert.AreEqual(1f, player.StaggerFactor);

            player.Stagger(3f, 0.4f);
            Assert.IsTrue(player.Staggered);
            Assert.AreEqual(0.4f, player.StaggerFactor, 1e-4f);
            var slow = RaceFor(player, new Vector2(0f, 1f), Vector2.zero, false, 1f, 1f);
            Assert.Less(slow.avg, 0.8f * free, "full ahead cannot drive through a stagger");
            Assert.AreEqual(0.4f * player.MaxSpeed, slow.avg, 0.4f, "it caps the speed at its share");
            Assert.Greater(slow.avg, 0f, "but a stagger is a slow-down, not a stop");

            // The harsher and the longer of two overlapping staggers wins, and it runs out by itself.
            player.Stagger(1f, 0.8f);
            Assert.AreEqual(0.4f, player.StaggerFactor, 1e-4f);
            for (int i = 0; i < 300; i++) player.Tick(Vector2.zero, 0.02f);
            Assert.IsFalse(player.Staggered);
            Assert.AreEqual(1f, player.StaggerFactor);
            Assert.AreEqual(free, RaceFor(player, Vector2.zero, Vector2.zero, false, 1f).avg, 0.4f);
        }

        [Test]
        public void Race_IsAdventureOnly_CozyDrivesExactlyAsBefore()
        {
            var cozy = MakeIsland(Vector2.zero, 206, 3f, true);
            cozy.ModeOverride = GameMode.Cozy;
            cozy.AdventureCruise = 0.8f;
            cozy.AdventureSpeedScale = 2f;
            Assert.IsFalse(cozy.AdventureRacing);
            for (int i = 0; i < 200; i++) cozy.Tick(Vector2.zero, 0.02f);
            Assert.Less(cozy.Speed, 0.05f, "nothing pushes a cozy island");
            // And the wheel still turns it.
            for (int i = 0; i < 100; i++) cozy.Tick(new Vector2(1f, 1f), 0.02f);
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(0f, cozy.Yaw)), 20f, "cozy steering is a turn, as it always was");
        }

        [Test]
        public void SinkScale_MakesTheRunHarderTheLongerItLasts()
        {
            var player = MakeIsland(Vector2.zero, 106, 3f, true);
            float calm = player.SinkSecondsFull;
            player.AdventureSinkScale = 1.5f;
            Assert.AreEqual(calm / 1.5f, player.SinkSecondsFull, 1e-2f);
        }

        // ---- B) the difficulty curve ----

        RingWorld MakeRingWorld()
        {
            var go = new GameObject("AdvRingWorld");
            go.SetActive(false);
            var ring = go.AddComponent<RingWorld>();
            go.SetActive(true);
            _objects.Add(go);
            return ring;
        }

        [Test]
        public void Difficulty_RisesWithTheRunAndLevelsOff()
        {
            var ring = MakeRingWorld();
            ring.difficultyRampSeconds = 300f;
            ring.levelSeconds = 45f;

            ring.SetRunSeconds(0f);
            Assert.AreEqual(0f, ring.Difficulty);
            Assert.AreEqual(1, ring.Level);
            float spacing0 = ring.FlotsamSpacing, cruise0 = ring.Cruise, drift0 = ring.DriftShare;

            ring.SetRunSeconds(150f);
            Assert.AreEqual(0.5f, ring.Difficulty, 1e-3f);
            Assert.AreEqual(4, ring.Level);

            ring.SetRunSeconds(600f);
            Assert.AreEqual(1f, ring.Difficulty);
            Assert.Greater(ring.FlotsamSpacing, spacing0, "flotsam gets rarer");
            Assert.Greater(ring.Cruise, cruise0, "the base pace rises");
            Assert.Greater(ring.DriftShare, drift0, "more islands drift across the track");
            Assert.AreEqual(ring.flotsamSpacingMax, ring.FlotsamSpacing, 1e-3f);
            Assert.AreEqual(ring.cruiseMax, ring.Cruise, 1e-3f);
        }

        [Test]
        public void Spawner_PutsTheExtraIslandsOfALateRunOnTheRing()
        {
            var go = new GameObject("AdvSpawner");
            go.SetActive(false);
            var spawner = go.AddComponent<RingIslandSpawner>();
            spawner.life = false;
            spawner.mainIslands = 8;
            spawner.islets = 2;
            go.SetActive(true);
            _objects.Add(go);

            var start = new Vector2(0f, 400f);
            spawner.ResetRing(Ring, start, 7, true);
            Assert.AreEqual(10, spawner.LiveCount);
            Assert.AreEqual(8, spawner.MainCount);

            spawner.ExtraIslands = 4;
            for (int i = 0; i < 3; i++) spawner.Step(Ring, start, true);
            Assert.AreEqual(12, spawner.MainCount, "the ring fills up as the difficulty rises");
            Assert.AreEqual(spawner.TargetCount, spawner.LiveCount);
            var islands = new List<Island>();
            spawner.CollectIslands(islands);
            foreach (var isl in islands) Assert.IsTrue(Ring.Inside(isl.PlanarPosition, 0f), "inside the band: " + isl.PlanarPosition);
        }

        // ---- B2) the rim of the band ----

        [Test]
        public void Edge_FoamsAndPushesBackButTheIslandStaysOnTheBand()
        {
            var ring = MakeRingWorld();
            ring.circumference = C;
            ring.bandWidth = 2f * HW;
            ring.edgeWarnWidth = 10f;
            ring.edgePush = 4f;
            var player = MakeIsland(Vector2.zero, 107, 3f, true);
            ring.player = player;
            var g = new RingGeometry(0f, HW, C);

            // Middle of the band: nothing pushes.
            player.SetPlanarPosition(new Vector2(0f, 0f));
            ring.StepEdge(g, player.PlanarPosition, true);
            Assert.AreEqual(0f, ring.EdgeWarning);
            Assert.AreEqual(Vector2.zero, player.EdgePush);

            // Near the rim: the foam line lights up and the water pushes back towards the middle.
            player.SetPlanarPosition(new Vector2(HW - 4f, 0f));
            ring.StepEdge(g, player.PlanarPosition, true);
            Assert.Greater(ring.EdgeWarning, 0.5f);
            Assert.Less(player.EdgePush.x, 0f, "the water pushes back onto the band");
            Assert.AreEqual(0f, player.EdgePush.y);

            // The same on the other side, mirrored.
            player.SetPlanarPosition(new Vector2(-(HW - 4f), 0f));
            ring.StepEdge(g, player.PlanarPosition, true);
            Assert.Greater(player.EdgePush.x, 0f);

            // The run is over or not running: no push at all.
            player.SetPlanarPosition(new Vector2(HW - 1f, 0f));
            ring.StepEdge(g, player.PlanarPosition, false);
            Assert.AreEqual(0f, ring.EdgeWarning);
            Assert.AreEqual(Vector2.zero, player.EdgePush);
        }

        [Test]
        public void Edge_ThePlayerIsHeldInsideTheBandAndNeverSinksFromIt()
        {
            var ring = MakeRingWorld();
            ring.circumference = C;
            ring.bandWidth = 2f * HW;
            var player = MakeIsland(Vector2.zero, 108, 3f, true);
            ring.player = player;
            // What RingWorld installs in Adventure (Island.PositionConstraint).
            float margin = player.BoundingRadius * ring.playerWallMargin;
            Island.PositionConstraint = p => new RingGeometry(0f, HW, C).ClampAcross(p, margin);

            int sunk = 0;
            player.Sunk += () => sunk++;
            // Drive flat out at the rim for five seconds, in both steering schemes.
            bool wasDirect = Island.DirectionSteering;
            for (int i = 0; i < 100; i++)
            {
                Island.DirectionSteering = i >= 50;
                player.SetSelfVelocity(new Vector2(12f, 0f));
                player.Tick(new Vector2(1f, 1f), Vector2.right, 0.05f);
                Assert.LessOrEqual(player.PlanarPosition.x, HW - margin + 1e-3f, "held inside the band");
            }
            Island.DirectionSteering = wasDirect;
            Assert.AreEqual(HW - margin, player.PlanarPosition.x, 1e-2f, "it comes to rest at the invisible wall");
            Assert.LessOrEqual(player.SelfVelocity.x, 1e-3f, "and its outward speed is cancelled");
            Assert.IsFalse(player.IsSunk);
            Assert.IsFalse(player.LostOverEdge, "a run can only end by sinking");
            Assert.AreEqual(0, sunk);
            Assert.AreEqual(0f, player.transform.position.y, 0.3f, "the island stays on the water");
        }

        [Test]
        public void Edge_ObstacleIslandsNeverLeaveTheBand()
        {
            var ring = MakeRingWorld();
            ring.circumference = C;
            ring.bandWidth = 2f * HW;
            var g = new RingGeometry(0f, HW, C);
            var drifter = MakeIsland(new Vector2(HW + 12f, 30f), RegularSeed(91), 3f, false);
            ring.KeepIslandsOnRing(g, 0f);
            Assert.IsTrue(g.Inside(drifter.PlanarPosition, 0f), "obstacles stay on the band: " + drifter.PlanarPosition);
            Assert.LessOrEqual(drifter.PlanarPosition.x + drifter.BoundingRadius * ring.islandWallMargin, HW + 1e-3f);
        }

        // Direct-direction steering tops out at the deflection the player gives, but the ring's own base pace and
        // the flotsam boost still reach the unscaled top speed (they share the same cap).
        [Test]
        public void DirectSteering_ScalesTheTopSpeedButKeepsTheRingPace()
        {
            var player = MakeIsland(Vector2.zero, 109, 3f, true);
            bool was = Island.DirectionSteering;
            Island.DirectionSteering = true;
            try
            {
                for (int i = 0; i < 400; i++) player.Tick(Vector2.zero, new Vector2(0f, 0.2f), 0.05f);
                float gentle = player.SelfVelocity.magnitude;
                for (int i = 0; i < 400; i++) player.Tick(Vector2.zero, new Vector2(0f, 1f), 0.05f);
                float full = player.SelfVelocity.magnitude;
                Assert.Less(gentle, full * 0.85f, "a small deflection is a slow drift, not full speed");
                Assert.Greater(gentle, 0f);

                player.AdventureCruise = 0.7f;
                for (int i = 0; i < 400; i++) player.Tick(Vector2.zero, new Vector2(0f, 0.2f), 0.05f);
                Assert.Greater(player.SelfVelocity.magnitude, 0.65f * player.MaxSpeed, "the ring's base pace is not scaled down");
            }
            finally
            {
                Island.DirectionSteering = was;
            }
        }

        // ---- C) a bot on the real loop: dodging pays ----

        class BotResult
        {
            public float seconds, slowest = float.MaxValue, avgSpeed;
            public int hits, flotsam;
            public override string ToString() => $"{seconds:F1} s, {hits} hits, {flotsam} flotsam, {avgSpeed:F1} u/s (min {slowest:F1})";
        }

        // Drives one lap-and-a-half of the ring with real Island.Tick / IslandWorld.Step: `dodge` steers for the
        // widest gap ahead, the other holds its course. Flotsam sits on the track every `spacing` units and gives
        // buoyancy when it is run over, exactly as Encounters lays it out.
        // The race made input.x a sideways pull instead of a turn (the heading is pinned to the track), so the bot
        // is a plain proportional controller on the across coordinate now, not on the yaw.
        BotResult RunBot(int seed, bool dodge, float spacing = 26f, float limit = 300f)
        {
            var world = MakeWorld();
            var player = MakeIsland(new Vector2(0f, 0f), 12345, 3f, true);
            player.moveSpeed = 9.5f;
            player.AdventureCruise = 0.55f;
            var rnd = new System.Random(seed);
            var list = new List<Island> { player };
            var obstacles = new List<Island>();
            for (int i = 0; i < 14; i++)
            {
                float z = 40f + i * (C - 60f) / 14f;
                float x = Mathf.Lerp(-HW + 8f, HW - 8f, (float)rnd.NextDouble());
                var isl = MakeIsland(new Vector2(x, z), RegularSeed(900 + i * 7), 3f + 3f * (float)rnd.NextDouble(), false);
                obstacles.Add(isl);
                list.Add(isl);
            }
            var flotsam = new List<Vector2>();
            for (float z = 25f; z < C; z += spacing)
                flotsam.Add(new Vector2(Mathf.Lerp(-HW + 4f, HW - 4f, (float)rnd.NextDouble()), z));

            Island.PositionConstraint = p => Ring.ClampAcross(p, player.BoundingRadius * 0.9f);
            var result = new BotResult();
            float dt = 0.05f;
            for (float t = 0f; t < limit && !player.IsSunk; t += dt)
            {
                Vector2 pos = player.PlanarPosition;
                float turn = 0f;
                if (dodge)
                {
                    float wanted = SteerTarget(pos, obstacles, player.BoundingRadius);
                    turn = Mathf.Clamp((wanted - pos.x) / 6f, -1f, 1f);
                }
                else turn = Mathf.Clamp(-pos.x / 6f, -1f, 1f);
                player.Tick(new Vector2(turn, 1f), dt);
                world.Step(dt, list);
                player.AdvanceSink(dt);
                result.slowest = Mathf.Min(result.slowest, player.TrackSpeed);
                result.avgSpeed += player.TrackSpeed * dt;
                // Whatever the island drives over is collected.
                for (int i = flotsam.Count - 1; i >= 0; i--)
                {
                    Vector2 d = new Vector2(flotsam[i].x - pos.x, Ring.AlongDelta(flotsam[i].y, pos.y));
                    if (d.sqrMagnitude > 9f) continue;
                    flotsam.RemoveAt(i);
                    player.AddBuoyancy(0.07f);
                    result.flotsam++;
                }
                result.seconds = t + dt;
            }
            result.hits = player.Hits;
            if (result.seconds > 0f) result.avgSpeed /= result.seconds;
            return result;
        }

        // The middle of the widest free stretch across the band at the place the island will be in a moment.
        static float SteerTarget(Vector2 pos, List<Island> obstacles, float radius)
        {
            float lookahead = 22f;
            var spans = new List<Vector2> { new Vector2(-HW + radius, HW - radius) };
            foreach (var isl in obstacles)
            {
                if (isl == null || !isl.isActiveAndEnabled) continue;
                float dz = Ring.AlongDelta(isl.PlanarPosition.y, pos.y);
                if (dz < -2f || dz > lookahead) continue;
                float r = isl.BoundingRadius + radius + 2f;
                float a = isl.PlanarPosition.x - r, b = isl.PlanarPosition.x + r;
                for (int k = spans.Count - 1; k >= 0; k--)
                {
                    Vector2 s = spans[k];
                    if (b <= s.x || a >= s.y) continue;
                    spans.RemoveAt(k);
                    if (a - s.x > 1f) spans.Insert(k, new Vector2(s.x, a));
                    if (s.y - b > 1f) spans.Insert(k, new Vector2(b, s.y));
                }
            }
            float best = pos.x, score = float.MinValue;
            foreach (var s in spans)
            {
                float centre = (s.x + s.y) * 0.5f;
                float value = (s.y - s.x) - 0.35f * Mathf.Abs(centre - pos.x);
                if (value <= score) continue;
                score = value;
                best = centre;
            }
            return best;
        }

        [Test]
        public void Bot_DodgingSurvivesClearlyLongerThanDrivingStraight()
        {
            var dodger = RunBot(3, true);
            TearDown();
            SetUp();
            var straight = RunBot(3, false);
            string report = "dodging " + dodger + " vs straight " + straight;
            Debug.Log("Adventure bot: " + report);
            Assert.Less(straight.hits, 40, report);
            Assert.Greater(straight.hits, dodger.hits, "steering around them means fewer hits: " + report);
            Assert.Greater(dodger.seconds, straight.seconds + 20f, "dodging clearly pays: " + report);
            Assert.Greater(dodger.flotsam, 0, report);
        }
    }
}
