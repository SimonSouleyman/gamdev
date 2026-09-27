using System.Collections.Generic;
using Drift.Core;
using Drift.Islands;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Owner (v0.6.4): "Die Maximalgeschwindigkeit der Insel soll verdoppelt werden. Die Boosts an sich sollen nicht
    // stärker werden, wenn man aber mehrere nacheinander immer wieder einsammelt bzw. lange auf den Plattengrenzen
    // schwimmt, soll sich das dann auswirken." - the "Schwung" (momentum), and the whale's ghost ride through islands.
    public class AdventureMomentumTests
    {
        readonly List<GameObject> _objects = new();
        System.Func<Vector2, Vector2> _savedConstraint;

        [SetUp]
        public void SetUp()
        {
            _savedConstraint = Island.PositionConstraint;
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
            var go = new GameObject(player ? "MomPlayer" : "MomIsle_" + seed);
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

        Island MakeRacer(int seed)
        {
            var p = MakeIsland(Vector2.zero, seed, 3f, true);
            p.AdventureCruise = 0.62f;
            p.AdventureSpeedScale = 1.45f;
            p.AdventureTrack = Vector2.up;
            return p;
        }

        RingWorld MakeRing()
        {
            var go = new GameObject("MomRing");
            go.SetActive(false);
            _objects.Add(go);
            return go.AddComponent<RingWorld>();
        }

        IslandWorld MakeWorld()
        {
            var go = new GameObject("MomWorld");
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

        static float Cruise(Island p, float seconds)
        {
            const float dt = 0.02f;
            float sum = 0f;
            int n = 0;
            for (float t = 0f; t < seconds; t += dt)
            {
                p.Tick(Vector2.zero, dt);
                if (t > seconds * 0.5f) { sum += p.TrackSpeed; n++; }
            }
            return sum / Mathf.Max(1, n);
        }

        [Test]
        public void FullMomentum_DoublesTheTopSpeed_ThePaceAndTheSteering()
        {
            var p = MakeRacer(301);
            float top = p.MaxSpeed, steer = p.AdventureSteerSpeed, pace = Cruise(p, 4f);
            p.SetMomentum(1f);
            Assert.AreEqual(2f * top, p.MaxSpeed, 1e-3f, "top speed x2 at full momentum");
            Assert.AreEqual(2f * steer, p.AdventureSteerSpeed, 1e-3f, "the sideways authority grows along, the band stays steerable");
            Assert.AreEqual(2f * pace, Cruise(p, 6f), 0.05f * pace, "and the island really runs twice as fast");
            p.SetMomentum(0.5f);
            Assert.AreEqual(1.5f * top, p.MaxSpeed, 1e-3f, "step by step in between");
            // Cozy never sees it.
            p.ModeOverride = GameMode.Cozy;
            Assert.AreEqual(1f, p.MomentumScale, 1e-6f);
        }

        [Test]
        public void ABoostAddsTheSameSpeed_WithOrWithoutMomentum()
        {
            var p = MakeRacer(302);
            float top0 = p.MaxSpeed;
            p.SpeedBoost(3f, 1.65f);
            float plain = 0f;
            for (int i = 0; i < 60; i++) { p.Tick(Vector2.zero, 0.02f); plain = Mathf.Max(plain, p.TrackSpeed); }

            var q = MakeRacer(303);
            q.SetMomentum(1f);
            for (int i = 0; i < 300; i++) q.Tick(Vector2.zero, 0.02f);
            q.SpeedBoost(3f, 1.65f);
            float withMomentum = 0f;
            for (int i = 0; i < 100; i++) { q.Tick(Vector2.zero, 0.02f); withMomentum = Mathf.Max(withMomentum, q.TrackSpeed); }
            // The boost's own share stays 0.65 x the momentum-free top speed: x1.65 alone, 2 + 0.65 at full momentum.
            Assert.LessOrEqual(plain, 1.65f * top0 + 0.05f);
            Assert.Greater(withMomentum, plain + 0.8f * top0, "momentum raises the boosted speed too");
            Assert.LessOrEqual(withMomentum, 2.65f * top0 + 0.05f, "but the boost itself is not stronger");
        }

        [Test]
        public void Momentum_ChainsUp_HoldsDuringABoost_DecaysWithAHalfLife_AndSurfingFeedsIt()
        {
            var ring = MakeRing();
            var p = MakeRacer(304);
            Assert.AreEqual(0f, p.Momentum);
            // Groups of two pieces every five seconds, each boost running three of them: after five to six groups in a
            // row the momentum is high; a single group is only a start.
            float chain = 0f;
            for (int group = 1; group <= 6; group++)
            {
                chain = Mathf.Clamp01(chain + 2f * ring.momentumPerBoost);
                if (group == 1) Assert.Less(chain, 0.35f, "one group is a start, not the top speed");
                for (float t = 0f; t < 3f; t += 0.02f) chain = ring.StepMomentum(chain, 0.02f, true, 0f);
                for (float t = 0f; t < 2f; t += 0.02f) chain = ring.StepMomentum(chain, 0.02f, false, 0f);
                if (group == 5) Assert.Greater(chain, 0.8f, "five boosts in succession: high momentum");
            }
            Assert.Greater(chain, 0.85f, "and it stays up while the chain goes on");
            p.SetMomentum(0.95f);
            p.AddMomentum(ring.momentumPerBoost);
            Assert.AreEqual(1f, p.Momentum, 1e-6f, "never above full");

            float m = 1f;
            for (float t = 0f; t < 3f; t += 0.02f) m = ring.StepMomentum(m, 0.02f, true, 0f);
            Assert.AreEqual(1f, m, 1e-4f, "held while a boost runs");
            for (float t = 0f; t < ring.momentumHalfLife - 1e-3f; t += 0.02f) m = ring.StepMomentum(m, 0.02f, false, 0f);
            Assert.AreEqual(0.5f, m, 0.02f, "half of it gone after the half-life without boost or surf");

            float s = 0f;
            for (float t = 0f; t < 20f; t += 0.02f) s = ring.StepMomentum(s, 0.02f, false, 1f);
            Assert.Greater(s, 0.25f, "a long surf builds momentum on its own");
            for (float t = 0f; t < 120f; t += 0.02f) s = ring.StepMomentum(s, 0.02f, false, 1f);
            Assert.Less(s, 0.6f, "surfing alone tops out around half: the chains of boosts fill the rest");
            float weak = 0f;
            for (float t = 0f; t < 20f; t += 0.02f) weak = ring.StepMomentum(weak, 0.02f, false, ring.momentumSurfStart * 0.9f);
            Assert.AreEqual(0f, weak, 1e-5f, "a brush past a boundary does not count");
        }

        [Test]
        public void AHitHalvesTheMomentum()
        {
            var world = MakeWorld();
            var p = MakeIsland(Vector2.zero, 305, 3f, true);
            var rock = MakeIsland(new Vector2(0f, p.BoundingRadius + 2.4f), RegularSeed(57), 4f, false);
            var pair = new List<Island> { p, rock };
            p.SetMomentum(0.8f);
            p.SetSelfVelocity(new Vector2(0f, 6f));
            for (int i = 0; i < 40 && p.Hits == 0; i++)
            {
                p.Tick(Vector2.zero, 0.05f);
                world.Step(0.05f, pair);
            }
            Assert.AreEqual(1, p.Hits);
            Assert.AreEqual(0.4f, p.Momentum, 1e-3f, "a hit costs half of it");
        }

        // The whale: "durch Inseln fahren können für die Zeit" - no bounce, no hit, no buoyancy, and never stuck inside.
        [Test]
        public void TheGhost_GlidesThroughAnObstacle_WithoutAHit()
        {
            var world = MakeWorld();
            var p = MakeRacer(306);
            var rock = MakeIsland(new Vector2(0f, 22f), RegularSeed(61), 6f, false);
            var pair = new List<Island> { p, rock };
            p.SpeedBoost(4f, 2.5f);
            p.Ghost(4f);
            Assert.AreEqual(2.5f, p.BoostFactor, 1e-4f);
            float buoy = p.RawBuoyancy;
            bool passing = false;
            float x0 = p.PlanarPosition.x;
            for (float t = 0f; t < 4f; t += 0.02f)
            {
                p.Tick(Vector2.zero, 0.02f);
                world.Step(0.02f, pair);
                passing |= p.GhostPassing;
            }
            Assert.IsTrue(passing, "it really went through the island");
            Assert.AreEqual(0, p.Hits, "no hit");
            Assert.AreEqual(0, world.Bumps);
            Assert.AreEqual(buoy, p.RawBuoyancy, 1e-5f, "no buoyancy lost");
            Assert.Greater(p.PlanarPosition.y, rock.PlanarPosition.y + rock.BoundingRadius, "straight through and out the far side");
            Assert.AreEqual(x0, p.PlanarPosition.x, 0.5f, "no bounce, no sidestep");
            Assert.IsTrue(rock.isActiveAndEnabled);
        }

        [Test]
        public void TheGhost_NeverEndsInsideAnIsland()
        {
            var world = MakeWorld();
            var p = MakeRacer(307);
            var rock = MakeIsland(new Vector2(0f, p.BoundingRadius + 7f), RegularSeed(71), 7f, false);
            var pair = new List<Island> { p, rock };
            // The whale boost runs out just as the island is inside the coast.
            p.Ghost(0.6f);
            bool heldInside = false;
            for (float t = 0f; t < 8f; t += 0.02f)
            {
                p.Tick(Vector2.zero, 0.02f);
                world.Step(0.02f, pair);
                if (t > 0.8f && p.GhostPassing) heldInside = true;
                if (!p.Ghosting && p.DetectContact(rock, out _, out _)) Assert.Fail("the ghost ended with the island inside the obstacle at t=" + t);
            }
            Assert.IsTrue(heldInside, "the ghost outlasted its own time while inside");
            Assert.AreEqual(0, p.Hits, "and came out clean");
            Assert.IsFalse(p.Ghosting, "then it ends");
        }
    }
}
