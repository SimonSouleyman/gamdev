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

        [Test]
        public void FlyOver_MovesWithTheView_AndKeepsItsBounds()
        {
            Assert.AreEqual(new Vector2(0f, 1f), FlyOverCamera.Screen2World(Vector2.up, 0f), "up the screen is north");
            var east = FlyOverCamera.Screen2World(Vector2.up, 90f);
            Assert.AreEqual(1f, east.x, 1e-4f);
            Assert.AreEqual(0f, east.y, 1e-4f);

            var centre = new Vector2(10f, -4f);
            Assert.AreEqual(centre + new Vector2(3f, 0f), FlyOverCamera.ClampToReach(centre + new Vector2(3f, 0f), centre, 20f));
            var far = FlyOverCamera.ClampToReach(centre + new Vector2(100f, 0f), centre, 20f);
            Assert.AreEqual(20f, (far - centre).magnitude, 1e-3f, "never further out than the reach");
            Assert.AreEqual(44f, FlyOverCamera.Reach(20f, 1.3f, 18f), 1e-3f);
            Assert.AreEqual(70f, FlyOverCamera.MaxHeight(10f, 70f), 1e-3f, "a small island still allows a high look");
            Assert.AreEqual(110f, FlyOverCamera.MaxHeight(50f, 70f), 1e-3f, "a continent needs more height");
            // Camera distance behind the focus: steep = nearly overhead, flat = well back.
            Assert.AreEqual(0f, FlyOverCamera.Distance(0f, 45f), 1e-4f);
            Assert.AreEqual(20f, FlyOverCamera.Distance(20f, 45f), 1e-3f);
            Assert.Greater(FlyOverCamera.Distance(20f, 25f), FlyOverCamera.Distance(20f, 70f));
            Assert.Less(FlyOverCamera.SpeedAt(6f, 5f, 70f, 13f), FlyOverCamera.SpeedAt(60f, 5f, 70f, 13f), "higher up it moves faster");
        }

        [Test]
        public void FlyOver_FliesOverTheIsland_StaysAboveIt_AndNeverLeavesIt()
        {
            var isl = MakeIsland(0f);
            var fly = new FlyOverCamera();
            fly.settings.minHeight = 5f;
            fly.settings.maxHeight = 60f;
            fly.Reset(new Vector3(0f, 12f, -9f), Quaternion.Euler(35f, 0f, 0f), isl.transform.position.y);
            Assert.AreEqual(12f, fly.Height, 1e-3f);
            Assert.AreEqual(0f, fly.Yaw, 1e-3f);
            Assert.AreEqual(35f, fly.Pitch, 1e-3f);

            float reach = FlyOverCamera.Reach(isl.BoundingRadius, fly.settings.reachFactor, fly.settings.reachPad);
            var forward = new FlyOverCamera.Input { move = Vector2.up };
            for (int i = 0; i < 600; i++)
            {
                fly.Step(forward, 0.02f, isl);
                Assert.LessOrEqual((fly.Focus - isl.PlanarPosition).magnitude, reach + 1e-3f, "the view stays over the island");
                Assert.GreaterOrEqual(fly.Position.y, isl.transform.position.y + fly.settings.minHeight - 1e-3f, "stays above the water");
                // Whatever the player does, the island is what the camera looks at.
                Assert.Less(Vector3.Angle(fly.Rotation * Vector3.forward, fly.LookPoint - fly.Position), 1e-2f);
            }
            Assert.Greater(fly.Focus.y, 0.5f, "it really flew north");

            // Looking around turns the view; the pitch never tips over or past the horizon.
            for (int i = 0; i < 200; i++) fly.Step(new FlyOverCamera.Input { look = new Vector2(-6f, -6f) }, 0.02f, isl);
            Assert.Greater(Mathf.DeltaAngle(0f, fly.Yaw), 5f, "dragging left has turned the view right");
            Assert.That(fly.Pitch, Is.InRange(fly.settings.minPitch - 0.01f, fly.settings.maxPitch + 0.01f));

            // Zoom: out to the ceiling, then in to the floor.
            for (int i = 0; i < 200; i++) fly.Step(new FlyOverCamera.Input { zoomFactor = 1.05f }, 0.02f, isl);
            Assert.AreEqual(FlyOverCamera.MaxHeight(isl.BoundingRadius, fly.settings.maxHeight), fly.Height, 0.01f);
            for (int i = 0; i < 400; i++) fly.Step(new FlyOverCamera.Input { zoomFactor = 0.95f }, 0.02f, isl);
            Assert.AreEqual(fly.settings.minHeight, fly.Height, 0.01f);
        }
    }
}
