using System.Collections.Generic;
using Drift.Bridge;
using Drift.Core;
using Drift.Islands;
using Drift.SaveSystem;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The cozy camera after the owner's 2026-09-22 session: the view swings onto the course the island is
    // really travelling (without ever making a held direction curve), and once the Pangäa is finished the
    // island stops and the same input flies the camera over it.
    public class CozyCameraTests
    {
        readonly List<GameObject> _objects = new();
        bool _steering;
        GameMode _mode;

        [SetUp]
        public void SetUp()
        {
            _steering = Island.DirectionSteering;
            // Another Editor session may have left the adventure mode on; the cozy camera is what is tested here.
            _mode = GameModes.Current;
            GameModes.Set(GameMode.Cozy);
        }

        [TearDown]
        public void TearDown()
        {
            GameModes.Set(_mode);
            Island.DirectionSteering = _steering;
            Island.DirectionProvider = null;
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        Island MakeIsland(float yaw)
        {
            var go = new GameObject("CozyCamIsland");
            go.SetActive(false);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var isl = go.AddComponent<Island>();
            isl.useKeyboardInput = true;
            isl.sinkEnabled = false;
            isl.landRadius = 3f;
            isl.shapeSeed = 4711;
            isl.carryResponse = 0f;
            isl.surfResponse = 0f;
            isl.driftRotation = 0f;
            go.SetActive(true);
            _objects.Add(go);
            return isl;
        }

        // ---------------------------------------------------------------- the view swings onto the course

        [Test]
        public void CourseYaw_ReadsTheTravel_AndIgnoresAnIslandThatHasAllButStopped()
        {
            Assert.IsTrue(IslandChaseCamera.CourseYaw(new Vector2(0f, 4f), 1.2f, out float north));
            Assert.AreEqual(0f, north, 1e-3f);
            Assert.IsTrue(IslandChaseCamera.CourseYaw(new Vector2(4f, 0f), 1.2f, out float east));
            Assert.AreEqual(90f, east, 1e-3f);
            // Gliding to a halt must not spin the view: below the dead zone there is no course at all.
            Assert.IsFalse(IslandChaseCamera.CourseYaw(new Vector2(0.4f, 0.2f), 1.2f, out _));
            Assert.IsFalse(IslandChaseCamera.CourseYaw(Vector2.zero, 1.2f, out _));
        }

        [Test]
        public void FollowYaw_SettlesInAboutASecond_AndNeverJumps()
        {
            float yaw = 0f, worstStep = 0f;
            const float dt = 0.02f, response = 3f, cap = 90f;
            for (int i = 0; i < 50; i++)
            {
                float next = IslandChaseCamera.FollowYaw(yaw, 45f, response, cap, dt);
                worstStep = Mathf.Max(worstStep, Mathf.Abs(Mathf.DeltaAngle(yaw, next)));
                yaw = next;
            }
            Assert.AreEqual(45f, yaw, 4f, "a 45 degree swing is done after a second");
            Assert.LessOrEqual(worstStep, cap * dt + 1e-4f, "never faster than the cap");
            // Half a second in it is well on its way, so it reads as a turn and not as a snap.
            float half = 0f;
            for (int i = 0; i < 25; i++) half = IslandChaseCamera.FollowYaw(half, 45f, response, cap, dt);
            Assert.That(half, Is.InRange(10f, 40f));
            Assert.AreEqual(17f, IslandChaseCamera.FollowYaw(17f, 90f, response, cap, 0f), 1e-5f, "a paused frame holds still");
        }

        [Test]
        public void SteerFrame_FreezesWhileADirectionIsHeld_AndCatchesUpOnceItIsLet()
        {
            float steer = 0f;
            for (int i = 0; i < 100; i++) steer = IslandChaseCamera.SteerYaw(steer, 60f, true, 0.02f);
            Assert.AreEqual(0f, steer, 1e-4f, "a held direction keeps its frame, so it stays a straight line");
            for (int i = 0; i < 50; i++) steer = IslandChaseCamera.SteerYaw(steer, 60f, false, 0.02f);
            Assert.AreEqual(60f, steer, 3f, "released, the frame is back on the view within a second");
        }

        // The loop the owner warned about: the steering is camera-relative, so a view that chases the input
        // would feed itself. Here the whole chain runs headlessly - screen direction -> steering frame ->
        // island -> course -> view - with the hardest case, a direction held sideways to the view.
        [Test]
        public void HoldingADirection_TurnsTheViewOntoTheCourse_WithoutTheIslandCircling()
        {
            Island.DirectionSteering = true;
            var isl = MakeIsland(0f);
            float viewYaw = 0f, steerYaw = 0f, yaw0 = isl.Yaw;
            var screen = new Vector2(1f, 0f); // hard right on the stick, the whole time
            const float dt = 0.02f;

            Vector2 legA = Vector2.zero, legB = Vector2.zero;
            Vector2 from = isl.PlanarPosition;
            for (int i = 0; i < 400; i++)
            {
                Vector2 world = TiltMath.ToWorld(TiltMath.ClampStick(screen), steerYaw);
                isl.Tick(Vector2.zero, world, dt);
                if (IslandChaseCamera.CourseYaw(isl.SelfVelocity, 1.2f, out float course))
                    viewYaw = IslandChaseCamera.FollowYaw(viewYaw, course, 3f, 90f, dt);
                steerYaw = IslandChaseCamera.SteerYaw(steerYaw, viewYaw, true, dt);
                if (i == 199) { legA = isl.PlanarPosition - from; from = isl.PlanarPosition; }
                if (i == 399) legB = isl.PlanarPosition - from;
            }

            Assert.Less(Vector2.Angle(legA, legB), 1f, "the second half of the drive runs exactly as straight as the first");
            Assert.AreEqual(90f, Mathf.DeltaAngle(0f, viewYaw), 3f, "the view has swung onto the course (due east)");
            Assert.AreEqual(90f, Mathf.DeltaAngle(0f, IslandChaseCamera.CourseYaw(isl.SelfVelocity, 1.2f, out float c) ? c : 0f), 3f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(yaw0, isl.Yaw), 1e-3f, "the island never turns, whatever the camera does");

            // And once the stick is let go the frame is the view: "up the screen" is now where the island goes.
            for (int i = 0; i < 60; i++) steerYaw = IslandChaseCamera.SteerYaw(steerYaw, viewYaw, false, dt);
            Assert.AreEqual(0f, Mathf.DeltaAngle(steerYaw, viewYaw), 1f);
        }

        [Test]
        public void TheSliderGoesBackToTheFixedNorthView()
        {
            Island.DirectionSteering = true;
            var isl = MakeIsland(140f);
            var go = new GameObject("CozyChaseCamera");
            _objects.Add(go);
            var cam = go.AddComponent<IslandChaseCamera>();
            cam.target = isl;

            cam.courseFollow = 0f;
            Assert.IsFalse(cam.CourseFollowActive);
            cam.SnapToTarget();
            Assert.AreEqual(0f, cam.ViewYawDeg, 1e-3f, "north stays up");
            Assert.AreEqual(0f, cam.SteerYawDeg, 1e-3f);
            Assert.Less(go.transform.position.z, isl.transform.position.z, "the camera sits south of the island");

            cam.courseFollow = 1f;
            Assert.AreEqual(3f, cam.CourseResponse, 1e-4f, "about a second");
            Assert.IsTrue(IslandChaseCamera.FollowsCourse(true, 1f, false, true), "cozy open sea");
            Assert.IsFalse(IslandChaseCamera.FollowsCourse(true, 0f, false, true), "the slider is off");
            Assert.IsFalse(IslandChaseCamera.FollowsCourse(false, 1f, false, true), "the old steering wheel");
            // The adventure ring keeps its own view along the track.
            Assert.IsFalse(IslandChaseCamera.FollowsCourse(true, 1f, true, true), "on the ring");
            Assert.IsFalse(IslandChaseCamera.FollowsCourse(true, 1f, false, false), "adventure");
        }

        // ---------------------------------------------------------------- the fly-over over the Pangäa

        [Test]
        public void FreeLook_TakesTheSteeringOffTheIsland_AndGivesItToTheCamera()
        {
            Assert.IsFalse(GameSession.IslandInputLocked(true, false, false, false), "an ordinary drive");
            Assert.IsTrue(GameSession.IslandInputLocked(true, false, false, true), "the fly-over steers no island");
            Assert.IsTrue(GameSession.IslandInputLocked(false, false, false, false));
            Assert.IsFalse(GameSession.IslandInputLocked(true, false, false), "the old overload is unchanged");

            Assert.IsTrue(PangaeaFinale.FlyOverActive(true, true, false, false, true));
            Assert.IsFalse(PangaeaFinale.FlyOverActive(false, true, false, false, true), "nothing finished yet");
            Assert.IsFalse(PangaeaFinale.FlyOverActive(true, false, false, false, true), "back on the title");
            Assert.IsFalse(PangaeaFinale.FlyOverActive(true, true, true, false, true), "photo mode has the camera");
            Assert.IsFalse(PangaeaFinale.FlyOverActive(true, true, false, true, true), "the flight into space has it");
            Assert.IsFalse(PangaeaFinale.FlyOverActive(true, true, false, false, false), "switched off");
            // A pause keeps the fly-over (the camera just stands still), so nothing cuts back to the island.
            Assert.IsTrue(PangaeaFinale.FlyOverActive(true, true, false, false, true));
        }

        // The fly-over's own camera (map gestures, bounds, fly-to, idle orbit) is tested in FlyOverGestureTests.

        // ---------------------------------------------------------------- tilt: no front, no turning view

        // The owner's phone test of v0.6.2: with the tilt it still felt as if the island had a front it first had
        // to turn. A phone is never let go like a stick, so the steering frame stayed frozen while the view swung
        // onto the course; the same tilt then pointed elsewhere on the screen and the view turned like a boat.
        // Under the tilt the view now holds still, and a new tilt direction is the island's new course at once.
        [Test]
        public void Tilt_HoldsTheView_AndTheIslandGoesStraightWhereThePhoneTips()
        {
            Island.DirectionSteering = true;
            var isl = MakeIsland(0f);
            isl.velocityAlign = 1.5f; // the scene's value
            float viewYaw = 0f, steerYaw = 0f;
            const float dt = 1f / 60f;
            Vector2 Screen(float t) => t < 0f ? Vector2.up : Vector2.Lerp(Vector2.up, Vector2.right, Mathf.Clamp01(t / 0.3f));

            float worstView = 0f, angleAfter1s = 180f;
            for (float t = -3f; t < 1.5f; t += dt)
            {
                Vector2 world = TiltMath.ToWorld(TiltMath.ClampStick(Screen(t)), steerYaw);
                isl.Tick(Vector2.zero, world, dt);
                IslandChaseCamera.StepYaws(ref viewYaw, ref steerYaw, isl.SelfVelocity, true, true, 1.2f, 3f, 90f, dt);
                worstView = Mathf.Max(worstView, Mathf.Abs(Mathf.DeltaAngle(0f, viewYaw)));
                if (t < 1f) angleAfter1s = Vector2.Angle(isl.SelfVelocity, Vector2.right);
            }
            Assert.Less(worstView, 1e-3f, "the view never turned");
            Assert.AreEqual(0f, steerYaw, 1e-3f, "so screen right stays east");
            Assert.Less(angleAfter1s, 20f, "a second after tipping right the island runs right");
            Assert.Greater(isl.SelfVelocity.magnitude, 1f);

            // Without the hold (the stick) the view still swings onto the course as before.
            float v2 = 0f, s2 = 0f;
            for (int i = 0; i < 120; i++) IslandChaseCamera.StepYaws(ref v2, ref s2, new Vector2(5f, 0f), true, false, 1.2f, 3f, 90f, dt);
            Assert.Greater(v2, 45f);
            Assert.AreEqual(0f, s2, 1e-3f, "a held stick keeps its frame");
        }

        // ---------------------------------------------------------------- a second round after the finale

        // v0.6.2 on the phone: after the Pangäa finale the camera stood still in every later run, in both modes.
        // The finale asked whether the chase camera was running only after switching it off, so it never switched
        // it back on.
        [Test]
        public void TheFinale_GivesTheChaseCameraBack()
        {
            var isl = MakeIsland(0f);
            var go = new GameObject("FinaleChaseCamera");
            _objects.Add(go);
            var cam = go.AddComponent<IslandChaseCamera>();
            cam.target = isl;
            Assert.IsTrue(cam.enabled);

            bool was = PangaeaFinale.HandOffChase(cam);
            Assert.IsTrue(was, "it was running before the flight");
            Assert.IsFalse(cam.enabled, "the flight has the camera");

            cam.Suspended = true; // as the fly-over leaves it
            PangaeaFinale.HandBackChase(cam, was, isl);
            Assert.IsTrue(cam.enabled, "the next run's camera follows again");
            Assert.IsFalse(cam.Suspended);

            cam.enabled = false;
            PangaeaFinale.HandBackChase(cam, PangaeaFinale.HandOffChase(cam), isl);
            Assert.IsFalse(cam.enabled, "one that was off on purpose stays off");
            Assert.IsTrue(PangaeaFinale.HandOffChase(null));
        }

        [Test]
        public void HomeButton_HasItsNewLabel_AndTheBannerNamesTheInputInUse()
        {
            Assert.AreEqual("Home", PangaeaFinale.HomeLabel);
            Assert.AreEqual(PangaeaFinale.TiltBannerBody, PangaeaFinale.BannerBodyFor(true, true));
            Assert.AreEqual(PangaeaFinale.DefaultBannerBody, PangaeaFinale.BannerBodyFor(false, true));
            Assert.AreEqual(PangaeaFinale.KeyboardBannerBody, PangaeaFinale.BannerBodyFor(false, false));
        }
    }
}
