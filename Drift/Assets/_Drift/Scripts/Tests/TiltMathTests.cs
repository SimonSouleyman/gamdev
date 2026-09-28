using Drift.Core;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The pure side of the tilt steering. The Editor has no IMU, so these tests feed the chain the gravity
    // vectors a phone would report and check what comes out.
    public class TiltMathTests
    {
        static readonly Vector3 Flat = TiltMath.FlatGravity;              // lying on the table, screen up
        static readonly Vector3 Upright = new Vector3(0f, -1f, 0f);       // held vertically, portrait

        // Gravity as a phone reports it after the far edge (device +Y) was dipped down by deg.
        static Vector3 DipFarEdge(float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            return new Vector3(0f, Mathf.Sin(r), -Mathf.Cos(r));
        }

        // ... and after the right edge (device +X) was dipped down by deg.
        static Vector3 DipRightEdge(float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, -Mathf.Cos(r));
        }

        [Test]
        public void HoldingStillIsZero()
        {
            Assert.AreEqual(Vector2.zero, TiltMath.DeviceTilt(Flat, Flat));
            Assert.AreEqual(Vector2.zero, TiltMath.DeviceTilt(Upright, Upright));
            var s = TiltSettings.Default;
            Assert.AreEqual(Vector2.zero, TiltMath.Evaluate(Flat, Flat, ScreenOrientation.Portrait, s));
        }

        [Test]
        public void DippingTheFarEdgeDrivesForward()
        {
            Vector2 t = TiltMath.DeviceTilt(Flat, DipFarEdge(20f));
            Assert.AreEqual(20f, t.y, 0.01f);
            Assert.AreEqual(0f, t.x, 0.01f);
        }

        [Test]
        public void DippingTheRightEdgeDrivesRight()
        {
            Vector2 t = TiltMath.DeviceTilt(Flat, DipRightEdge(20f));
            Assert.AreEqual(20f, t.x, 0.01f);
            Assert.AreEqual(0f, t.y, 0.01f);
        }

        // The whole point of calibrating: you may lean back on the sofa and the middle is still "stop".
        [Test]
        public void CalibratedUprightStillReadsPitch()
        {
            Assert.AreEqual(Vector2.zero, TiltMath.DeviceTilt(Upright, Upright));
            float r = 20f * Mathf.Deg2Rad;
            Vector3 tipped = new Vector3(0f, -Mathf.Cos(r), -Mathf.Sin(r));
            Vector2 t = TiltMath.DeviceTilt(Upright, tipped);
            Assert.AreEqual(20f, t.y, 0.01f);
            Assert.AreEqual(0f, t.x, 0.01f);
        }

        [Test]
        public void AnyNeutralIsItsOwnZero()
        {
            Vector3 lean = TiltMath.PoseFor(Flat, new Vector2(0f, -52f));
            Assert.AreEqual(Vector2.zero, TiltMath.DeviceTilt(lean, lean));
            Vector2 forward = TiltMath.DeviceTilt(lean, TiltMath.PoseFor(lean, new Vector2(0f, 12f)));
            Assert.AreEqual(12f, forward.y, 0.01f);
            Assert.AreEqual(0f, forward.x, 0.01f);
        }

        [Test]
        public void PoseForIsTheInverseOfDeviceTilt()
        {
            foreach (var want in new[] { new Vector2(0f, 10f), new Vector2(10f, 0f), new Vector2(7f, -12f), new Vector2(-25f, -3f) })
            {
                Vector2 got = TiltMath.DeviceTilt(Flat, TiltMath.PoseFor(Flat, want));
                Assert.AreEqual(want.x, got.x, 0.01f, "x of " + want);
                Assert.AreEqual(want.y, got.y, 0.01f, "y of " + want);
            }
        }

        [Test]
        public void DeadZoneKeepsASteadyHandStill()
        {
            var s = TiltSettings.Default;
            float full = s.FullTiltDeg;
            Assert.AreEqual(Vector2.zero, TiltMath.Shape(new Vector2(0f, full * (s.deadZone - 0.02f)), s));
            Assert.Greater(TiltMath.Shape(new Vector2(0f, full * (s.deadZone + 0.05f)), s).magnitude, 0f);
            // Radial, not per axis: a diagonal just inside the dead zone is still a standstill.
            float edge = full * s.deadZone * 0.7f;
            Assert.AreEqual(Vector2.zero, TiltMath.Shape(new Vector2(edge, edge), s));
        }

        [Test]
        public void FullTiltIsFullSpeedAndNoMore()
        {
            var s = TiltSettings.Default;
            Assert.AreEqual(1f, TiltMath.Shape(new Vector2(0f, s.FullTiltDeg), s).magnitude, 1e-4f);
            Assert.AreEqual(1f, TiltMath.Shape(new Vector2(0f, s.FullTiltDeg * 3f), s).magnitude, 1e-4f);
            Assert.AreEqual(1f, TiltMath.Shape(new Vector2(0f, 179f), s).magnitude, 1e-4f);
        }

        [Test]
        public void MoreTiltIsMoreSpeed()
        {
            var s = TiltSettings.Default;
            float last = -1f;
            for (float f = 0.2f; f <= 1f; f += 0.1f)
            {
                float m = TiltMath.Shape(new Vector2(0f, s.FullTiltDeg * f), s).magnitude;
                Assert.Greater(m, last);
                last = m;
            }
        }

        [Test]
        public void SensitivityChangesHowFarYouHaveToTilt()
        {
            var low = TiltSettings.Default; low.sensitivity = 0f;
            var high = TiltSettings.Default; high.sensitivity = 1f;
            Assert.AreEqual(TiltSettings.LooseTiltDeg, low.FullTiltDeg, 1e-3f);
            Assert.AreEqual(TiltSettings.TightTiltDeg, high.FullTiltDeg, 1e-3f);
            var tilt = new Vector2(0f, 15f);
            Assert.Less(TiltMath.Shape(tilt, low).magnitude, TiltMath.Shape(tilt, high).magnitude);
        }

        [Test]
        public void ShapeKeepsTheDirection()
        {
            var s = TiltSettings.Default;
            Vector2 tilt = new Vector2(12f, -9f);
            Vector2 outp = TiltMath.Shape(tilt, s);
            Assert.AreEqual(0f, Vector2.Angle(tilt, outp), 0.01f);
        }

        [Test]
        public void PortraitIsTheIdentityAndTheOtherOrientationsTurnWithTheScreen()
        {
            var t = new Vector2(1f, 2f);
            Assert.AreEqual(t, TiltMath.ToScreen(t, ScreenOrientation.Portrait));
            Assert.AreEqual(new Vector2(-1f, -2f), TiltMath.ToScreen(t, ScreenOrientation.PortraitUpsideDown));
            Assert.AreEqual(new Vector2(-2f, 1f), TiltMath.ToScreen(t, ScreenOrientation.LandscapeLeft));
            Assert.AreEqual(new Vector2(2f, -1f), TiltMath.ToScreen(t, ScreenOrientation.LandscapeRight));
            // Turning the screen never changes how far you tilted.
            foreach (ScreenOrientation o in new[] { ScreenOrientation.Portrait, ScreenOrientation.PortraitUpsideDown, ScreenOrientation.LandscapeLeft, ScreenOrientation.LandscapeRight })
                Assert.AreEqual(t.magnitude, TiltMath.ToScreen(t, o).magnitude, 1e-4f);
        }

        [Test]
        public void SmoothingConvergesAndDoesNotDependOnTheFrameRate()
        {
            Vector3 target = DipFarEdge(30f);
            Vector3 a = Flat, b = Flat;
            for (int i = 0; i < 60; i++) a = TiltMath.Smooth(a, target, 0.12f, 1f / 60f);
            for (int i = 0; i < 240; i++) b = TiltMath.Smooth(b, target, 0.12f, 1f / 240f);
            Assert.AreEqual(0f, Vector3.Angle(a, b), 0.5f);
            Assert.AreEqual(0f, Vector3.Angle(a, target), 0.5f);
            Assert.AreEqual(1f, a.magnitude, 1e-4f);
        }

        [Test]
        public void SmoothingDampensASingleShakyFrame()
        {
            Vector3 held = DipFarEdge(10f);
            Vector3 shake = DipFarEdge(30f);
            Vector3 smoothed = TiltMath.Smooth(held, shake, 0.12f, 1f / 60f);
            float got = TiltMath.DeviceTilt(Flat, smoothed).y;
            Assert.Greater(got, 10f);
            Assert.Less(got, 14f);
        }

        [Test]
        public void TheNeutralFollowsASlowlyChangingPosture()
        {
            var s = TiltSettings.Default;
            Vector3 held = TiltMath.PoseFor(Flat, new Vector2(0f, 4f));   // inside the dead zone: output 0
            Vector3 neutral = Flat;
            for (int i = 0; i < 600; i++) neutral = TiltMath.FollowNeutral(neutral, held, s.neutralFollow, 0f, 1f / 60f);
            float left = TiltMath.DeviceTilt(neutral, held).y;
            Assert.Less(left, 4f);
            Assert.Greater(left, 0f);
        }

        [Test]
        public void HoldingAFullTiltDoesNotEraseIt()
        {
            var s = TiltSettings.Default;
            Vector3 held = TiltMath.PoseFor(Flat, new Vector2(0f, s.FullTiltDeg));
            Vector3 neutral = Flat;
            for (int i = 0; i < 600; i++) neutral = TiltMath.FollowNeutral(neutral, held, s.neutralFollow, 1f, 1f / 60f);
            Assert.AreEqual(s.FullTiltDeg, TiltMath.DeviceTilt(neutral, held).y, 1e-3f);
        }

        [Test]
        public void UpTheScreenIsAwayFromTheCamera()
        {
            Assert.AreEqual(new Vector2(0f, 1f), TiltMath.ToWorld(new Vector2(0f, 1f), 0f));
            Assert.AreEqual(new Vector2(1f, 0f), TiltMath.ToWorld(new Vector2(1f, 0f), 0f));
            Vector2 turned = TiltMath.ToWorld(new Vector2(0f, 1f), 90f);
            Assert.AreEqual(1f, turned.x, 1e-4f);
            Assert.AreEqual(0f, turned.y, 1e-4f);
            Vector2 right = TiltMath.ToWorld(new Vector2(1f, 0f), 90f);
            Assert.AreEqual(0f, right.x, 1e-4f);
            Assert.AreEqual(-1f, right.y, 1e-4f);
            // Half speed stays half speed whatever the camera is doing.
            Assert.AreEqual(0.5f, TiltMath.ToWorld(new Vector2(0f, 0.5f), 37f).magnitude, 1e-4f);
        }

        [Test]
        public void SteepnessWarnsAboutANeutralThatCannotMeasureRoll()
        {
            Assert.AreEqual(0f, TiltMath.SteepnessDeg(Flat), 1e-3f);
            Assert.AreEqual(90f, TiltMath.SteepnessDeg(Upright), 1e-3f);
            Assert.Less(TiltMath.SteepnessDeg(TiltMath.PoseFor(Flat, new Vector2(0f, -45f))), TiltMath.WarnSteepDeg);
        }

        [Test]
        public void TurnThrottleFallbackTurnsTowardsTheDirection()
        {
            Vector2 north = new Vector2(0f, 1f);
            Assert.Greater(TiltMath.AsTurnThrottle(new Vector2(1f, 0f), north).x, 0.9f);     // east = turn right
            Assert.Less(TiltMath.AsTurnThrottle(new Vector2(-1f, 0f), north).x, -0.9f);      // west = turn left
            Vector2 ahead = TiltMath.AsTurnThrottle(north, north);
            Assert.AreEqual(0f, ahead.x, 1e-4f);
            Assert.AreEqual(1f, ahead.y, 1e-4f);
            Assert.AreEqual(Vector2.zero, TiltMath.AsTurnThrottle(Vector2.zero, north));
        }

        // The whole chain, as the game runs it: gravity in, a 0..1 world direction out.
        [Test]
        public void FullChainPortraitFacingNorth()
        {
            var s = TiltSettings.Default;
            Vector3 pose = TiltMath.PoseFor(Flat, new Vector2(0f, s.FullTiltDeg));
            Vector2 screen = TiltMath.Evaluate(Flat, pose, ScreenOrientation.Portrait, s);
            Vector2 world = TiltMath.ToWorld(screen, 0f);
            Assert.AreEqual(0f, world.x, 1e-3f);
            Assert.AreEqual(1f, world.y, 1e-3f);
        }
    }
}
