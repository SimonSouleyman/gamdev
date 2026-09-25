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
    // really travelling (since v0.6.7 a direction held sideways keeps turning with it), and once the Pangäa is
    // finished the island stops and the same input flies the camera over it.
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
        public void ScreenYaw_IsWhatThePictureShows()
        {
            Assert.AreEqual(90f, IslandChaseCamera.ScreenYaw(Quaternion.Euler(51f, 90f, 0f)), 1e-3f);
            Assert.AreEqual(90f, IslandChaseCamera.ScreenYaw(Quaternion.Euler(51f, 90f, 1.2f)), 1e-3f, "the lean into a curve does not turn it");
            Assert.AreEqual(315f, IslandChaseCamera.ScreenYaw(Quaternion.Euler(10f, -45f, 0f)), 1e-3f);
            Assert.AreEqual(30f, IslandChaseCamera.ScreenYaw(Quaternion.Euler(90f, 30f, 0f)), 1e-2f, "straight down: the top edge is up");
        }

        [Test]
        public void TheViewTurnsNoFasterThanTheIslandMay()
        {
            Assert.AreEqual(60f, IslandChaseCamera.CourseTurnLimit(90f, 60f), 1e-5f);
            Assert.AreEqual(90f, IslandChaseCamera.CourseTurnLimit(90f, 120f), 1e-5f);
            Assert.AreEqual(90f, IslandChaseCamera.CourseTurnLimit(90f, 0f), 1e-5f, "no turn rate set: the camera's own cap");
        }

        // The whole cozy chain, headless: stick (a screen direction) -> the yaw the picture shows -> island -> course
        // -> view -> picture. The picture follows the view with the chase camera's look smoothing (lookLerp 8 in the
        // scene); the steering reads the picture, so what "right" means never lags what is on the screen.
        const float LookLerp = 8f;

        static void DriveFrame(Island isl, Vector2 screen, bool holdCourse, ref float viewYaw, ref float shownYaw, float dt)
        {
            Vector2 world = TiltMath.ToWorld(TiltMath.ClampStick(screen), shownYaw);
            isl.Tick(Vector2.zero, world, dt);
            viewYaw = IslandChaseCamera.StepCourse(viewYaw, isl.SelfVelocity, holdCourse, 1.2f, 3f, 90f, dt);
            shownYaw = IslandChaseCamera.FollowYaw(shownYaw, viewYaw, LookLerp, 720f, dt);
        }

        // Owner, v0.6.7: "Man fährt geradeaus und dreht dann nach rechts ab. Die Kamera dreht sich und die Insel fährt
        // vorwärts, der Steuerstick zeigt aber immer noch nach rechts." Right on the stick now stays right on the
        // screen: the island keeps curving right for as long as it is held - at a steady pace, never a spiral.
        [Test]
        public void HoldingRight_KeepsTurningRight_AtASteadyPace()
        {
            Island.DirectionSteering = true;
            var isl = MakeIsland(0f);
            isl.velocityAlign = 1.5f; // the scene's value
            float viewYaw = 0f, shownYaw = 0f, yaw0 = isl.Yaw;
            const float dt = 1f / 60f;
            var perSecond = new float[10];
            float last = 0f;
            bool hasLast = false;
            for (int i = 0; i < 600; i++)
            {
                DriveFrame(isl, Vector2.right, false, ref viewYaw, ref shownYaw, dt);
                if (!IslandChaseCamera.CourseYaw(isl.SelfVelocity, 0.05f, out float c)) continue;
                if (hasLast) perSecond[i / 60] += Mathf.DeltaAngle(last, c);
                last = c;
                hasLast = true;
            }
            for (int s = 3; s < 10; s++)
            {
                Assert.Greater(perSecond[s], 45f, $"second {s}: still turning right, right never became straight ahead");
                Assert.LessOrEqual(perSecond[s], 95f, $"second {s}: no faster than the view may swing");
            }
            Assert.LessOrEqual(perSecond[9], perSecond[4] + 5f, "a steady curve, not a spiral");
            Assert.Greater(Mathf.DeltaAngle(shownYaw, last), 10f, "the island still runs right of where the picture looks");
            Assert.Greater(isl.SelfVelocity.magnitude, 2f, "and keeps its way on");
            Assert.AreEqual(0f, Mathf.DeltaAngle(yaw0, isl.Yaw), 1e-3f, "the island's body never turns, whatever the camera does");
        }

        // Up on the stick is where the picture looks, so after the turn it is a straight line and the view settles on it.
        [Test]
        public void StickUp_AfterATurn_RunsStraightAlongTheView()
        {
            Island.DirectionSteering = true;
            var isl = MakeIsland(0f);
            isl.velocityAlign = 1.5f;
            float viewYaw = 0f, shownYaw = 0f;
            const float dt = 1f / 60f;
            for (int i = 0; i < 90; i++) DriveFrame(isl, Vector2.right, false, ref viewYaw, ref shownYaw, dt);
            Assert.Greater(viewYaw, 20f, "the turn right has swung the view");
            for (int i = 0; i < 180; i++) DriveFrame(isl, Vector2.up, false, ref viewYaw, ref shownYaw, dt);
            Vector2 from = isl.PlanarPosition;
            IslandChaseCamera.CourseYaw(isl.SelfVelocity, 0.05f, out float c0);
            for (int i = 0; i < 120; i++) DriveFrame(isl, Vector2.up, false, ref viewYaw, ref shownYaw, dt);
            IslandChaseCamera.CourseYaw(isl.SelfVelocity, 0.05f, out float c1);
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(c0, c1)), 2f, "a straight line");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(viewYaw, c1)), 3f, "the view looks along it");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(shownYaw, viewYaw)), 2f, "and the picture shows it");
            Vector2 leg = isl.PlanarPosition - from;
            Assert.Less(Vector2.Angle(leg, TiltMath.ToWorld(Vector2.up, shownYaw)), 3f, "up the screen is where the island went");
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
        // to turn: the view swung onto the course, so the same tilt pointed elsewhere on the screen and the view turned
        // like a boat. Under the tilt the view holds still, the steering reads the picture, and a new tilt direction is
        // the island's new course at once.
        [Test]
        public void Tilt_HoldsTheView_AndTheIslandGoesStraightWhereThePhoneTips()
        {
            Island.DirectionSteering = true;
            var isl = MakeIsland(0f);
            isl.velocityAlign = 1.5f; // the scene's value
            float viewYaw = 0f, shownYaw = 0f;
            const float dt = 1f / 60f;
            Vector2 Screen(float t) => t < 0f ? Vector2.up : Vector2.Lerp(Vector2.up, Vector2.right, Mathf.Clamp01(t / 0.3f));

            float worstView = 0f, angleAfter1s = 180f;
            for (float t = -3f; t < 1.5f; t += dt)
            {
                DriveFrame(isl, Screen(t), true, ref viewYaw, ref shownYaw, dt);
                worstView = Mathf.Max(worstView, Mathf.Abs(Mathf.DeltaAngle(0f, viewYaw)));
                if (t < 1f) angleAfter1s = Vector2.Angle(isl.SelfVelocity, Vector2.right);
            }
            Assert.Less(worstView, 1e-3f, "the view never turned");
            Assert.AreEqual(0f, shownYaw, 1e-3f, "so screen right stays east");
            Assert.Less(angleAfter1s, 20f, "a second after tipping right the island runs right");
            Assert.Greater(isl.SelfVelocity.magnitude, 1f);

            // Without the hold (the stick) the view still swings onto the course as before.
            float v2 = 0f;
            for (int i = 0; i < 120; i++) v2 = IslandChaseCamera.StepCourse(v2, new Vector2(5f, 0f), false, 1.2f, 3f, 90f, dt);
            Assert.Greater(v2, 45f);
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
            Assert.AreEqual("Hauptmenü", PangaeaFinale.HomeLabel);
            Assert.AreEqual(PangaeaFinale.TiltBannerBody, PangaeaFinale.BannerBodyFor(true, true));
            Assert.AreEqual(PangaeaFinale.DefaultBannerBody, PangaeaFinale.BannerBodyFor(false, true));
            Assert.AreEqual(PangaeaFinale.KeyboardBannerBody, PangaeaFinale.BannerBodyFor(false, false));
        }
    }
}
