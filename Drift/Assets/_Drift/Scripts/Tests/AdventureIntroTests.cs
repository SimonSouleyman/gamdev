using System.Collections.Generic;
using Drift.Core;
using Drift.Islands;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // v0.6.6 adventure round (owner, 2026-09-25): a breath before every race ("kurzer Moment zum Durchatmen", the camera
    // starts further away and glides in), the race camera a little higher ("nicht mehr als 10°"), and a marker ring
    // around every piece of flotsam (Tests/Visuals/AdventureFlotsamRingTests).
    public class AdventureIntroTests
    {
        readonly List<GameObject> _objects = new();
        System.Func<Vector2, Vector2> _savedConstraint;
        bool _savedHeld, _savedLocked, _savedDirect;
        float _savedPull;

        [SetUp]
        public void SetUp()
        {
            _savedConstraint = Island.PositionConstraint;
            _savedHeld = RingWorld.StartHeld;
            _savedPull = RingWorld.IntroPull;
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
            RingWorld.IntroPull = _savedPull;
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
            var go = New("IntroPlayer");
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
            var go = New("IntroRingWorld");
            var ring = go.AddComponent<RingWorld>();
            ring.GetComponent<RingIslandSpawner>().life = false;
            // Stepped by hand: never let it take over the scene's ring (an Editor left in Abenteuer).
            ring.enabled = false;
            go.SetActive(true);
            ring.player = player;
            Island.PositionConstraint = ring.ClampPlayer;
            return ring;
        }

        // ---- the breath before the race ----

        [Test]
        public void Intro_LastsItsSeconds_AndEndsExactlyOnce()
        {
            var intro = new AdventureIntro();
            Assert.IsFalse(intro.Holding);
            Assert.AreEqual(0f, intro.Pull);
            intro.Arm(2.8f);
            Assert.IsTrue(intro.Holding);
            Assert.AreEqual(1f, intro.Pull, 1e-6f, "it starts in the wide framing");
            float t = 0f;
            int ends = 0;
            for (int i = 0; i < 200 && intro.Holding; i++)
            {
                t += 0.02f;
                if (intro.Step(0.02f, false)) ends++;
            }
            Assert.AreEqual(1, ends);
            Assert.AreEqual(2.8f, t, 0.021f, "2.8 s of breath, not more, not less");
            Assert.IsFalse(intro.Step(0.02f, false), "and it never ends twice");
            Assert.AreEqual(0f, intro.Pull);
        }

        [Test]
        public void Intro_WaitsForTheBriefing_AndThePause()
        {
            var intro = new AdventureIntro();
            intro.Arm(2.5f);
            for (int i = 0; i < 500; i++) Assert.IsFalse(intro.Step(0.05f, true), "Tilda still talks");
            Assert.IsTrue(intro.Holding);
            Assert.AreEqual(1f, intro.Pull, 1e-6f, "the camera waits wide while she talks");
            for (int i = 0; i < 100; i++) intro.Step(0f, false);
            Assert.AreEqual(0f, intro.Elapsed, 1e-6f, "paused (dt 0): nothing counts");
            int frames = 0;
            while (intro.Holding && frames < 1000) { intro.Step(0.05f, false); frames++; }
            Assert.AreEqual(50, frames, 1, "after the briefing the intro runs once, in full");
        }

        [Test]
        public void IntroPull_RestsThenGlidesInSmoothly()
        {
            const float total = 2.8f;
            float prev = 1f, maxStep = 0f;
            for (int i = 0; i <= 280; i++)
            {
                float e = total * i / 280f;
                float p = AdventureIntro.PullAt(e, total, AdventureIntro.HoldShare);
                Assert.LessOrEqual(p, prev + 1e-6f, "monotonic: the camera only ever comes closer (" + e + " s)");
                Assert.That(p, Is.InRange(0f, 1f));
                if (e <= total * AdventureIntro.HoldShare) Assert.AreEqual(1f, p, 1e-6f, "a short rest in the wide view first");
                maxStep = Mathf.Max(maxStep, prev - p);
                prev = p;
            }
            Assert.AreEqual(0f, prev, 1e-6f, "the race framing exactly when the race starts");
            // Ease-in-out: slow at both ends, never a jump.
            float h = AdventureIntro.HoldShare * total, glide = total - h;
            Assert.Less(1f - AdventureIntro.PullAt(h + 0.05f * glide, total, AdventureIntro.HoldShare), 0.01f, "eases out of the rest");
            Assert.Less(AdventureIntro.PullAt(total - 0.05f * glide, total, AdventureIntro.HoldShare), 0.01f, "eases into the race framing");
            Assert.Less(maxStep, 0.02f, "no step larger than 2 % per 10 ms");
        }

        [Test]
        public void IntroHold_NoDistanceNoSinkNoClock_ThenTheRaceStarts()
        {
            var player = MakeRacer(new Vector2(0f, 5000f));
            var ring = MakeRing(player);
            var intro = new AdventureIntro();
            intro.Arm(2.8f);
            int started = 0;
            System.Action onStart = () => started++;
            RingWorld.RaceStarted += onStart;
            try
            {
                Vector2 start = player.PlanarPosition;
                float buoy = player.Buoyancy;
                float t = 0f;
                // The game: GameSession holds the start (StartHeld, sinking off, input locked) while the intro runs.
                while (intro.Holding && t < 10f)
                {
                    RingWorld.StartHeld = true;
                    player.sinkEnabled = false;
                    ring.StepRace(0.05f);
                    player.Tick(new Vector2(1f, 1f), 0.05f);
                    Assert.AreEqual(0, started, "no \"Los!\" while held");
                    intro.Step(0.05f, false);
                    t += 0.05f;
                }
                Assert.AreEqual(2.8f, t, 0.051f);
                Assert.AreEqual(0f, (player.PlanarPosition - start).magnitude, 1e-3f, "the island waits on the start line");
                Assert.AreEqual(0f, ring.RunDistance, "no metres during the intro");
                Assert.AreEqual(0f, ring.RunSeconds, "no clock during the intro");
                Assert.AreEqual(buoy, player.Buoyancy, 1e-5f, "no sinking during the intro");

                RingWorld.StartHeld = false;
                player.sinkEnabled = true;
                for (int i = 0; i < 60; i++)
                {
                    ring.StepRace(0.05f);
                    player.Tick(Vector2.zero, 0.05f);
                }
                Assert.AreEqual(1, started, "the race starts once, right after the intro");
                Assert.Greater(ring.RunDistance, 5f, "and then it races");
                Assert.Greater(ring.RunSeconds, 2.5f);
            }
            finally { RingWorld.RaceStarted -= onStart; }
        }

        // ---- the race camera ----

        static float Elevation(float back, float up) => Mathf.Atan2(up, back) * Mathf.Rad2Deg;

        [Test]
        public void RaiseOrbit_LiftsTheViewByTheAngle_AtTheSameDistance()
        {
            float back = 11f, up = 1.6f;
            float before = Elevation(back, up), reach = Mathf.Sqrt(back * back + up * up);
            IslandChaseCamera.RaiseOrbit(ref back, ref up, 6f, 1f);
            Assert.AreEqual(before + 6f, Elevation(back, up), 1e-3f);
            Assert.AreEqual(reach, Mathf.Sqrt(back * back + up * up), 1e-4f, "the island keeps its size on screen");
            IslandChaseCamera.RaiseOrbit(ref back, ref up, 0f, 1.5f);
            Assert.AreEqual(reach * 1.5f, Mathf.Sqrt(back * back + up * up), 1e-4f);
            Assert.AreEqual(before + 6f, Elevation(back, up), 1e-3f);
        }

        [Test]
        public void RaceCamera_NeverMoreThanTenDegreesHigher()
        {
            var go = New("IntroCam");
            var cam = go.AddComponent<IslandChaseCamera>();
            Assert.That(cam.ringRaiseDeg, Is.InRange(0.5f, IslandChaseCamera.MaxRingLookUpDeg), "the race camera stands a little higher");
            Assert.Greater(cam.RingLookUpDeg(0f), 0f, "and looks further ahead");
            Assert.LessOrEqual(cam.RingLookUpDeg(1f), IslandChaseCamera.MaxRingLookUpDeg + 1e-5f);
            Assert.Greater(cam.RingLookUpDeg(1f), cam.RingLookUpDeg(0f), "more at full momentum and on the whale");
            cam.ringLookUpDeg = 9f;
            cam.ringFastLookUpDeg = 9f;
            Assert.AreEqual(IslandChaseCamera.MaxRingLookUpDeg, cam.RingLookUpDeg(1f), 1e-5f, "capped at 10 degrees");
            float prev = -1f;
            for (int i = 0; i <= 10; i++)
            {
                float r = cam.RingLookUpDeg(i / 10f);
                Assert.GreaterOrEqual(r, prev);
                prev = r;
            }
        }

        [Test]
        public void IntroCamera_GlidesMonotonicallyFromWideToTheRaceFraming()
        {
            var go = New("IntroCam2");
            var cam = go.AddComponent<IslandChaseCamera>();
            const float total = 2.8f;
            float prevReach = float.MaxValue, prevElev = float.MaxValue;
            float firstReach = 0f, firstElev = 0f, lastReach = 0f, lastElev = 0f;
            for (int i = 0; i <= 140; i++)
            {
                float pull = AdventureIntro.PullAt(total * i / 140f, total, AdventureIntro.HoldShare);
                float back = 11f, up = 1.6f;
                IslandChaseCamera.RaiseOrbit(ref back, ref up, cam.ringRaiseDeg + cam.ringIntroRaiseDeg * pull, Mathf.Lerp(1f, cam.ringIntroDistance, pull));
                float reach = Mathf.Sqrt(back * back + up * up), elev = Elevation(back, up);
                Assert.LessOrEqual(reach, prevReach + 1e-4f, "only ever closer");
                Assert.LessOrEqual(elev, prevElev + 1e-4f, "only ever lower");
                if (i == 0) { firstReach = reach; firstElev = elev; }
                lastReach = reach;
                lastElev = elev;
                prevReach = reach;
                prevElev = elev;
            }
            Assert.Greater(firstReach, lastReach * 1.5f, "noticeably further back at the start");
            Assert.Greater(firstElev, lastElev + 10f, "and noticeably higher");
            float b0 = 11f, u0 = 1.6f;
            IslandChaseCamera.RaiseOrbit(ref b0, ref u0, cam.ringRaiseDeg, 1f);
            Assert.AreEqual(Mathf.Sqrt(b0 * b0 + u0 * u0), lastReach, 1e-3f, "ends exactly in the race framing");
        }
    }
}
