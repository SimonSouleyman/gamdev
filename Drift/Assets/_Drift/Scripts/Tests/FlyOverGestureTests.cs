using System.Collections.Generic;
using Drift.Bridge;
using Drift.Islands;
using Drift.UI;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The fly-over over the finished Pangäa, driven like a map app on a portrait phone (owner, v0.6.5: circling
    // the island and going from one thing to the next was "sehr schwierig und unintuitiv" with stick + look drag).
    public class FlyOverGestureTests
    {
        const float W = 1080f, H = 1920f, Dt = 1f / 60f;
        readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        static FlyOverCamera MakeCamera(float yaw = 20f, float pitch = 50f, float dist = 30f)
        {
            var fly = new FlyOverCamera();
            fly.SetLens(60f, W, H);
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            var focus = new Vector3(4f, 0f, -3f);
            fly.Reset(focus - rot * Vector3.forward * dist, rot, 0f);
            return fly;
        }

        Island MakeIsland(float radius = 6f)
        {
            var go = new GameObject("FlyOverIsland");
            go.SetActive(false);
            var isl = go.AddComponent<Island>();
            isl.useKeyboardInput = false;
            isl.sinkEnabled = false;
            isl.landRadius = radius;
            isl.shapeSeed = 4711;
            isl.carryResponse = 0f;
            isl.surfResponse = 0f;
            isl.driftRotation = 0f;
            go.SetActive(true);
            _objects.Add(go);
            return isl;
        }

        static float Px(FlyOverCamera fly, Vector3 world, Vector2 finger)
        {
            Vector3 s = fly.WorldToScreen(world);
            Assert.Greater(s.z, 0f, "in front of the camera");
            return Vector2.Distance(new Vector2(s.x, s.y), finger);
        }

        // ---------------------------------------------------------------- the camera

        [Test]
        public void Reset_TakesOverTheViewWithoutACut()
        {
            var rot = Quaternion.Euler(45f, 30f, 0f);
            var pos = new Vector3(3f, 20f, -7f);
            var fly = new FlyOverCamera();
            fly.SetLens(60f, W, H);
            fly.Reset(pos, rot, 0f);
            Assert.Less(Vector3.Distance(fly.Position, pos), 1e-3f);
            Assert.Less(Quaternion.Angle(fly.Rotation, rot), 0.01f);
            Assert.AreEqual(0f, fly.Focus.y, 1e-4f, "the focus sits where the view's centre meets the sea");
            Assert.Less(Px(fly, fly.Focus, new Vector2(W, H) * 0.5f), 0.5f);
        }

        [Test]
        public void OneFingerDrag_KeepsTheGrabbedPointUnderTheFinger()
        {
            foreach (var pose in new[] { new Vector2(0f, 50f), new Vector2(137f, 30f), new Vector2(-60f, 78f) })
            {
                var fly = MakeCamera(pose.x, pose.y);
                Vector2 finger = new Vector2(380f, 700f);
                Vector3 grabbed = fly.Hit(finger, fly.Focus.y);
                // A slow drag across the screen, frame by frame, in several directions.
                for (int i = 0; i < 90; i++)
                {
                    Vector2 next = finger + new Vector2(Mathf.Sin(i * 0.07f) * 6f, 9f);
                    fly.Step(new FlyOverCamera.Input { pan = true, panFrom = finger, panTo = next, touching = true }, Dt, null);
                    finger = next;
                    Assert.Less(Px(fly, grabbed, finger), 2f, $"yaw {pose.x} pitch {pose.y}, frame {i}");
                }
            }
        }

        [Test]
        public void OneFingerDrag_FlingsOnAfterAQuickRelease_AndComesToRest()
        {
            var fly = MakeCamera(0f, 60f);
            Vector2 finger = new Vector2(540f, 500f);
            for (int i = 0; i < 8; i++)
            {
                var next = finger + new Vector2(0f, 40f);
                fly.Step(new FlyOverCamera.Input { pan = true, panFrom = finger, panTo = next, touching = true }, Dt, null);
                finger = next;
            }
            Vector3 atRelease = fly.Focus;
            fly.Step(new FlyOverCamera.Input { released = true }, Dt, null);
            for (int i = 0; i < 20; i++) fly.Step(default, Dt, null);
            Vector3 d = fly.Focus - atRelease;
            Assert.Less(d.z, -1f, "dragging the map up the screen slides the view south, and it rolls on");
            for (int i = 0; i < 180; i++) fly.Step(default, Dt, null);
            Vector3 rest = fly.Focus;
            fly.Step(default, Dt, null);
            Assert.Less(Vector3.Distance(rest, fly.Focus), 1e-4f, "and stops");

            // A finger that stopped before lifting leaves nothing to fling.
            var still = MakeCamera(0f, 60f);
            finger = new Vector2(540f, 500f);
            for (int i = 0; i < 8; i++)
            {
                var next = finger + new Vector2(0f, 40f);
                still.Step(new FlyOverCamera.Input { pan = true, panFrom = finger, panTo = next, touching = true }, Dt, null);
                finger = next;
            }
            for (int i = 0; i < 20; i++) still.Step(new FlyOverCamera.Input { touching = true }, Dt, null);
            Vector3 held = still.Focus;
            still.Step(new FlyOverCamera.Input { released = true }, Dt, null);
            for (int i = 0; i < 30; i++) still.Step(default, Dt, null);
            Assert.Less(Vector3.Distance(held, still.Focus), 0.05f);
        }

        [Test]
        public void Pinch_ZoomsAboutTheSpotBetweenTheFingers()
        {
            var fly = MakeCamera(40f, 55f, 40f);
            Vector2 mid = new Vector2(760f, 1300f);
            Vector3 spot = fly.Hit(mid, fly.Focus.y);
            float d0 = fly.Distance;
            for (int i = 0; i < 30; i++)
                fly.Step(new FlyOverCamera.Input { pinch = true, pinchFrom = mid, pinchTo = mid, pinchScale = 1.02f, touching = true }, Dt, null);
            Assert.AreEqual(d0 / Mathf.Pow(1.02f, 30), fly.Distance, 0.01f, "fingers apart = closer");
            Assert.Less(Px(fly, spot, mid), 2f, "the spot between the fingers stays put");

            // Moving the fingers while pinching drags that spot along.
            Vector2 to = mid + new Vector2(-120f, -200f);
            fly.Step(new FlyOverCamera.Input { pinch = true, pinchFrom = mid, pinchTo = to, pinchScale = 0.9f, touching = true }, Dt, null);
            Assert.Less(Px(fly, spot, to), 2f);
        }

        [Test]
        public void Twist_TurnsTheViewByTheFingerAngle_AboutTheSpotBetweenThem()
        {
            var fly = MakeCamera(10f, 70f, 30f);
            Vector2 mid = new Vector2(500f, 1100f);
            Vector3 spot = fly.Hit(mid, fly.Focus.y);
            float yaw0 = fly.Yaw;
            fly.Step(new FlyOverCamera.Input { pinch = true, pinchFrom = mid, pinchTo = mid, pinchScale = 1f, twistDeg = 25f, touching = true }, Dt, null);
            Assert.AreEqual(25f, Mathf.DeltaAngle(yaw0, fly.Yaw), 1e-3f);
            Assert.Less(Px(fly, spot, mid), 2f);

            // Seen from straight above, a point under a finger turns with the finger: the content follows the hand.
            var top = MakeCamera(0f, 80f, 30f);
            Vector2 c = new Vector2(W, H) * 0.5f, finger = c + new Vector2(200f, 0f);
            Vector3 under = top.Hit(finger, top.Focus.y);
            top.Step(new FlyOverCamera.Input { pinch = true, pinchFrom = c, pinchTo = c, pinchScale = 1f, twistDeg = 30f, touching = true }, Dt, null);
            Vector2 turned = c + (Vector2)(Quaternion.Euler(0f, 0f, 30f) * new Vector2(200f, 0f));
            Assert.Less(Px(top, under, turned), 25f, "counter-clockwise fingers turn the picture counter-clockwise");
        }

        [Test]
        public void TwoFingersUpOrDown_TiltTheView_WithinItsLimits()
        {
            var fly = MakeCamera(0f, 50f);
            Vector3 focus = fly.Focus;
            fly.Step(new FlyOverCamera.Input { tiltPixels = 160f, touching = true }, Dt, null);
            Assert.AreEqual(50f - 160f / H * fly.settings.tiltPerScreen, fly.Pitch, 0.01f, "up the screen flattens the view");
            Assert.Less(Vector3.Distance(focus, fly.Focus), 1e-4f, "about the focus");
            for (int i = 0; i < 30; i++) fly.Step(new FlyOverCamera.Input { tiltPixels = 200f, touching = true }, Dt, null);
            Assert.AreEqual(fly.settings.minPitch, fly.Pitch, 1e-3f);
            for (int i = 0; i < 60; i++) fly.Step(new FlyOverCamera.Input { tiltPixels = -200f, touching = true }, Dt, null);
            Assert.AreEqual(fly.settings.maxPitch, fly.Pitch, 1e-3f);
            Assert.AreEqual(25f, fly.settings.minPitch);
            Assert.AreEqual(80f, fly.settings.maxPitch);
        }

        [Test]
        public void TapToFly_GlidesInAboutASecond_FramesTheSubject_ThenCirclesIt()
        {
            var fly = MakeCamera(20f, 45f, 60f);
            var target = new Vector3(40f, 1.5f, 25f);
            float yaw0 = fly.Yaw;
            fly.FlyTo(target, 14f, 50f, 200f);
            Assert.IsTrue(fly.FlyingTo);
            float t = 0f;
            Vector3 prev = fly.Focus;
            float maxStep = 0f;
            while (fly.FlyingTo && t < 3f)
            {
                fly.Step(default, Dt, null);
                t += Dt;
                maxStep = Mathf.Max(maxStep, Vector3.Distance(prev, fly.Focus));
                prev = fly.Focus;
            }
            Assert.GreaterOrEqual(t, 0.95f, "not a jump");
            Assert.LessOrEqual(t, 1.55f, "about a second, never a long wait");
            Assert.Less(Vector3.Distance(fly.Focus, target), 1e-3f);
            Assert.AreEqual(14f, fly.Distance, 1e-3f);
            Assert.AreEqual(50f, fly.Pitch, 1e-3f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(yaw0, fly.Yaw), 0.5f, "the map does not spin on the way");
            Assert.Less(maxStep, Vector3.Distance(new Vector3(4f, 0f, -3f), target) * 0.1f, "eased: no frame covers more than a tenth");
            Assert.Less(Px(fly, target, new Vector2(W, H) * 0.5f), 1f, "centred");

            // Arrived: it follows the subject and circles it slowly.
            Assert.IsTrue(fly.Tracking);
            float yaw1 = fly.Yaw;
            for (int i = 0; i < 180; i++)
            {
                target += new Vector3(0.02f, 0f, 0f);
                fly.SetTarget(target);
                fly.Step(default, Dt, null);
            }
            Assert.Greater(Mathf.DeltaAngle(yaw1, fly.Yaw), 3f, "circling");
            Assert.Less(Vector3.Distance(fly.Focus, target), 1.5f, "following the moving herd");

            // A touch lets go of the subject and stops the circling at once.
            fly.Step(new FlyOverCamera.Input { touching = true }, Dt, null);
            Assert.IsFalse(fly.Following);
            Assert.AreEqual(0f, fly.OrbitSpeed);
        }

        [Test]
        public void FrameDistance_ShowsTheSubjectAtTheAskedShareOfThePicture()
        {
            var fly = MakeCamera();
            float d = fly.FrameDistance(6f, 0.4f);
            float tanShort = Mathf.Tan(30f * Mathf.Deg2Rad) * (W / H);
            Assert.AreEqual(6f / (tanShort * 0.4f), d, 1e-3f);
            Assert.AreEqual(fly.settings.flyMinDistance, fly.FrameDistance(0.1f, 0.4f), 1e-4f, "never closer than the minimum");
        }

        [Test]
        public void Idle_CirclesAfterFourSeconds_AndStopsOnTouch()
        {
            var fly = MakeCamera(0f, 50f);
            float yaw0 = fly.Yaw;
            for (int i = 0; i < 234; i++) fly.Step(default, Dt, null);
            Assert.AreEqual(0f, Mathf.DeltaAngle(yaw0, fly.Yaw), 1e-3f, "3.9 s: still");
            for (int i = 0; i < 300; i++) fly.Step(default, Dt, null);
            Assert.Greater(Mathf.DeltaAngle(yaw0, fly.Yaw), 5f, "then it circles");
            Assert.AreEqual(fly.settings.orbitDegPerSecond, fly.OrbitSpeed, 1e-3f, "at full pace after the ramp");
            Vector3 focus = fly.Focus;
            Assert.Less(Px(fly, focus, new Vector2(W, H) * 0.5f), 1f, "round its focus");

            fly.Step(new FlyOverCamera.Input { touching = true }, Dt, null);
            Assert.AreEqual(0f, fly.OrbitSpeed, "a touch stops it at once");
            float yaw1 = fly.Yaw;
            for (int i = 0; i < 120; i++) fly.Step(default, Dt, null);
            Assert.AreEqual(0f, Mathf.DeltaAngle(yaw1, fly.Yaw), 1e-3f, "and the wait starts again");
        }

        [Test]
        public void FrameRateIndependent_FlightAndOrbit()
        {
            var a = MakeCamera();
            var b = MakeCamera();
            var target = new Vector3(-30f, 0f, 20f);
            a.FlyTo(target, 12f, 45f, 200f);
            b.FlyTo(target, 12f, 45f, 200f);
            for (int i = 0; i < 60 * 8; i++) a.Step(default, 1f / 60f, null);
            for (int i = 0; i < 30 * 8; i++) b.Step(default, 1f / 30f, null);
            // The arrival frame may differ by one 30 fps frame, so the circling may lag by that much.
            Assert.Less(Vector3.Distance(a.Position, b.Position), 0.15f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(a.Yaw, b.Yaw), 0.3f);
        }

        [Test]
        public void Bounds_FocusStaysNearTheIsland_AndTheCameraAboveTheGround()
        {
            var isl = MakeIsland(6f);
            var fly = new FlyOverCamera();
            fly.SetLens(60f, W, H);
            var rot = Quaternion.Euler(40f, 0f, 0f);
            fly.Reset(isl.transform.position - rot * Vector3.forward * 15f, rot, isl.transform.position.y);
            float reach = FlyOverCamera.Reach(isl.BoundingRadius, fly.settings.reach, fly.settings.reachMargin);
            Vector2 finger = new Vector2(540f, 400f);
            for (int i = 0; i < 400; i++)
            {
                var next = finger + new Vector2(0f, 30f);
                if (next.y > 1500f) next = new Vector2(540f, 400f);
                else fly.Step(new FlyOverCamera.Input { pan = true, panFrom = finger, panTo = next, touching = true }, Dt, isl);
                finger = next;
                Vector2 f = new Vector2(fly.Focus.x, fly.Focus.z);
                Assert.LessOrEqual((f - isl.PlanarPosition).magnitude, reach + 1e-3f);
            }
            // Low and flat across every hill: never inside the ground.
            fly.FlyTo(isl.transform.position, 5f, 25f, 100f);
            for (int i = 0; i < 1200; i++)
            {
                fly.Step(new FlyOverCamera.Input { yawDeg = 0.6f, move = new Vector2(0.3f, 0.2f) }, Dt, isl);
                float ground = Mathf.Max(0f, isl.SampleHeight(isl.ToLocal(fly.PlanarPosition)));
                Assert.GreaterOrEqual(fly.Position.y, isl.transform.position.y + ground + fly.settings.clearance - 1e-3f);
            }
            // Zoom limits.
            for (int i = 0; i < 300; i++) fly.Step(new FlyOverCamera.Input { zoomFactor = 1.1f }, Dt, isl);
            Assert.AreEqual(FlyOverCamera.MaxDistance(isl.BoundingRadius, fly.settings.maxDistance), fly.Distance, 0.01f);
            for (int i = 0; i < 600; i++) fly.Step(new FlyOverCamera.Input { zoomFactor = 0.9f }, Dt, isl);
            Assert.AreEqual(fly.settings.minDistance, fly.Distance, 0.01f);
        }

        [Test]
        public void Wheel_ZoomsSmoothlyAboutThePointer()
        {
            var fly = MakeCamera(0f, 50f, 40f);
            Vector2 pointer = new Vector2(300f, 1400f);
            Vector3 spot = fly.Hit(pointer, fly.Focus.y);
            fly.Step(new FlyOverCamera.Input { zoomFactor = 0.5f, zoomAtPoint = true, zoomPoint = pointer }, Dt, null);
            Assert.Greater(fly.Distance, 25f, "eased, not a jump");
            for (int i = 0; i < 120; i++) fly.Step(default, Dt, null);
            Assert.AreEqual(20f, fly.Distance, 0.05f);
            Assert.Less(Px(fly, spot, pointer), 3f);
        }

        [Test]
        public void Step_DoesNotAllocate()
        {
            var fly = MakeCamera();
            var input = new FlyOverCamera.Input { pinch = true, pinchFrom = new Vector2(500f, 900f), pinchTo = new Vector2(510f, 905f), pinchScale = 1.01f, twistDeg = 0.5f, touching = true };
            fly.Step(input, Dt, null);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) fly.Step(input, Dt, null);
            fly.FlyTo(new Vector3(9f, 0f, 9f), 12f, 50f, 100f);
            for (int i = 0; i < 200; i++) fly.Step(default, Dt, null);
            Assert.AreEqual(0, System.GC.GetAllocatedBytesForCurrentThread() - before);
        }

        // ---------------------------------------------------------------- the gesture recognizer

        static TouchGesture Feed2(TouchGestures g, Vector2 a, Vector2 b, float time) { g.Feed(2, 1, a, 2, b, time, 1f); return g.Consume(); }

        [Test]
        public void Gestures_ATapIsATap_ADragIsAPan()
        {
            var g = new TouchGestures();
            g.Feed(1, 7, new Vector2(500f, 800f), 0, default, 0f, 1f);
            g.Feed(1, 7, new Vector2(506f, 804f), 0, default, 0.05f, 1f);
            g.Feed(0, 0, default, 0, default, 0.12f, 1f);
            var r = g.Consume();
            Assert.IsTrue(r.tap);
            Assert.AreEqual(new Vector2(506f, 804f), r.tapPos);
            Assert.IsFalse(r.pan, "a few pixels of wobble never move the map");
            Assert.IsTrue(r.released && r.touched);

            g.Feed(1, 8, new Vector2(500f, 800f), 0, default, 1f, 1f);
            g.Feed(1, 8, new Vector2(500f, 860f), 0, default, 1.05f, 1f);
            g.Feed(1, 8, new Vector2(500f, 900f), 0, default, 1.1f, 1f);
            r = g.Consume();
            Assert.IsTrue(r.pan);
            Assert.AreEqual(new Vector2(500f, 900f), r.panTo);
            Assert.AreEqual(1, r.fingers);
            g.Feed(0, 0, default, 0, default, 1.15f, 1f);
            Assert.IsFalse(g.Consume().tap, "a drag is no tap");

            g.Feed(1, 9, new Vector2(500f, 800f), 0, default, 2f, 1f);
            g.Feed(1, 9, new Vector2(500f, 800f), 0, default, 2.8f, 1f);
            g.Feed(0, 0, default, 0, default, 3f, 1f);
            Assert.IsFalse(g.Consume().tap, "a long press is no tap");
        }

        [Test]
        public void Gestures_PinchJitterNeverTurns_TwistJitterNeverZooms()
        {
            var g = new TouchGestures();
            Vector2 c = new Vector2(540f, 960f);
            float scale = 1f, twist = 0f;
            // Spread from 300 to 600 px with a wobbling angle of +-4 degrees.
            for (int i = 0; i <= 60; i++)
            {
                float half = Mathf.Lerp(150f, 300f, i / 60f);
                float ang = Mathf.Sin(i * 0.9f) * 4f * Mathf.Deg2Rad;
                var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * half;
                var r = Feed2(g, c - d, c + d, i * Dt);
                scale *= r.scale;
                twist += r.twistDeg;
            }
            Assert.AreEqual(0f, twist, "the pinch never turned the view");
            Assert.Greater(scale, 1.8f, "and zoomed nearly the whole spread (only the small dead zone is lost)");
            Assert.Less(scale, 2.001f);
            g.Feed(0, 0, default, 0, default, 2f, 1f);
            g.Consume();

            // A 40 degree twist with the spread wobbling by 3 %.
            scale = 1f;
            twist = 0f;
            for (int i = 0; i <= 40; i++)
            {
                float ang = i * Mathf.Deg2Rad;
                float half = 200f * (1f + Mathf.Sin(i * 1.3f) * 0.03f);
                var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * half;
                var r = Feed2(g, c - d, c + d, 3f + i * Dt);
                scale *= r.scale;
                twist += r.twistDeg;
            }
            Assert.AreEqual(1f, scale, 1e-6f, "the twist never zoomed");
            Assert.Greater(twist, 27f, "turned by the finger angle past the dead zone");
            Assert.Less(twist, 40.01f);
            Assert.AreEqual(0f, g.Consume().tiltPixels);
        }

        [Test]
        public void Gestures_TwoFingersUpTogether_Tilt()
        {
            var g = new TouchGestures();
            float tilt = 0f, scale = 1f, twist = 0f;
            bool twoFinger = false;
            for (int i = 0; i <= 30; i++)
            {
                float y = 800f + i * 8f;
                var r = Feed2(g, new Vector2(400f + Mathf.Sin(i) * 2f, y), new Vector2(700f, y + Mathf.Cos(i) * 2f), i * Dt);
                tilt += r.tiltPixels;
                scale *= r.scale;
                twist += r.twistDeg;
                twoFinger |= r.twoFinger;
            }
            Assert.IsTrue(g.Tilting);
            Assert.Greater(tilt, 180f, "up the screen");
            Assert.IsFalse(twoFinger, "no pan, pinch or twist while tilting");
            Assert.AreEqual(1f, scale);
            Assert.AreEqual(0f, twist);
        }

        [Test]
        public void Gestures_LiftingOneOfTwoFingers_DragsOnWithoutAJumpOrATap()
        {
            var g = new TouchGestures();
            Vector2 c = new Vector2(540f, 960f);
            for (int i = 0; i <= 20; i++)
            {
                float half = 150f + i * 5f;
                Feed2(g, c - new Vector2(half, 0f), c + new Vector2(half, 0f), i * Dt);
            }
            var b = c + new Vector2(250f, 0f);
            g.Feed(1, 2, b, 0, default, 0.4f, 1f);
            var r = g.Consume();
            Assert.IsFalse(r.pan, "the finger left behind does not jump the map to itself");
            g.Feed(1, 2, b + new Vector2(0f, 30f), 0, default, 0.42f, 1f);
            r = g.Consume();
            Assert.IsTrue(r.pan);
            Assert.AreEqual(b, r.panFrom);
            g.Feed(0, 0, default, 0, default, 0.44f, 1f);
            Assert.IsFalse(g.Consume().tap);
        }

        [Test]
        public void Gestures_WholeChain_ADraggedFingerKeepsItsGroundPoint()
        {
            var g = new TouchGestures();
            var fly = MakeCamera(70f, 45f, 35f);
            Vector2 finger = new Vector2(300f, 600f);
            g.Feed(1, 3, finger, 0, default, 0f, 1f);
            Vector3 grabbed = fly.Hit(finger, fly.Focus.y);
            for (int i = 1; i < 60; i++)
            {
                finger += new Vector2(7f, 11f);
                g.Feed(1, 3, finger, 0, default, i * Dt, 1f);
                var r = g.Consume();
                fly.Step(new FlyOverCamera.Input { pan = r.pan, panFrom = r.panFrom, panTo = r.panTo, touching = r.fingers > 0 }, Dt, null);
            }
            // The first few pixels are the tap slop: the ground point trails by at most that much.
            Assert.Less(Px(fly, grabbed, finger), new TouchGestures().panSlop + 12f);
        }

        // ---------------------------------------------------------------- the Sehenswürdigkeiten ring

        static Sight At(string name, float angleDeg, float r = 20f)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            return new Sight { kind = SightKind.Village, index = (int)angleDeg, village = (int)angleDeg, name = name, world = new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r) };
        }

        [Test]
        public void Sights_StepRoundTheIsland_AndWrap()
        {
            var s = new PangaeaSights();
            s.Add(At("Ost", 90f));
            s.Add(At("Nord", 5f));
            s.Add(At("West", 270f));
            s.Add(At("Süd", 180f));
            s.Arrange(Vector2.zero);
            Assert.AreEqual("Nord", s[0].name, "clockwise from north");
            Assert.AreEqual("West", s[3].name);

            // Without a current stop: the next one clockwise from where the view looks.
            Vector3 lookingSouthEast = new Vector3(10f, 0f, -10f);
            Assert.AreEqual("Süd", s[s.Step(1, Vector2.zero, lookingSouthEast)].name);
            Assert.AreEqual("West", s[s.Step(1, Vector2.zero, lookingSouthEast)].name);
            Assert.AreEqual("Nord", s[s.Step(1, Vector2.zero, lookingSouthEast)].name, "wraps round");
            Assert.AreEqual("West", s[s.Step(-1, Vector2.zero, lookingSouthEast)].name);
            Assert.AreEqual("West (4/4)", s.Label(s.Index));

            s.Select(-1);
            Assert.AreEqual("Ost", s[s.Step(-1, Vector2.zero, lookingSouthEast)].name, "back: the previous one anticlockwise");

            // A rebuilt ring keeps the current stop.
            s.Add(At("Nordost", 45f));
            s.Arrange(Vector2.zero);
            Assert.AreEqual("Ost", s[s.Index].name);
            Assert.AreEqual("Ost (3/5)", s.Label(s.Index));
        }

        [Test]
        public void Sights_OfARealIsland_IncludeItsSummit()
        {
            var isl = MakeIsland(8f);
            var s = new PangaeaSights { summitMinHeight = -100f };
            s.Rebuild(isl, null);
            Assert.IsTrue(s.HasSummit);
            int summit = -1;
            for (int i = 0; i < s.Count; i++) if (s[i].kind == SightKind.Summit) summit = i;
            Assert.GreaterOrEqual(summit, 0);
            float top = isl.SampleHeight(s.SummitLocal);
            Assert.GreaterOrEqual(top, isl.MaxHeight - 0.5f, "the highest point of the heightfield");
            var subject = s.SubjectOf(summit, isl, null);
            Assert.IsNotNull(subject);
            Assert.IsTrue(subject.focus(out Vector3 f));
            Assert.AreEqual(isl.transform.TransformPoint(s.SummitLocal.x, Mathf.Max(0f, top), s.SummitLocal.y).y, f.y, 1e-3f);
        }
    }
}
