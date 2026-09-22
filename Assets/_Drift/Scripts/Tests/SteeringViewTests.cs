using System.Collections.Generic;
using Drift.Core;
using Drift.Islands;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Direct-direction steering (screen direction -> world direction -> travel) and the chase camera's
    // speed feel: the pure parts of "there is no front, the island just drifts where you point".
    public class SteeringViewTests
    {
        readonly List<GameObject> _objects = new();
        bool _steering;

        [SetUp]
        public void SetUp()
        {
            _steering = Island.DirectionSteering;
        }

        [TearDown]
        public void TearDown()
        {
            Island.DirectionSteering = _steering;
            Island.DirectionProvider = null;
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        Island MakeIsland(float yaw, bool player = true)
        {
            var go = new GameObject("TestIsland");
            go.SetActive(false);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var isl = go.AddComponent<Island>();
            isl.useKeyboardInput = player;
            isl.sinkEnabled = false;
            isl.landRadius = 3f;
            isl.shapeSeed = 4711;
            isl.carryResponse = 0f;
            isl.driftRotation = 0f;
            go.SetActive(true);
            _objects.Add(go);
            return isl;
        }

        // ---------------------------------------------------------------- screen -> world

        [Test]
        public void Keys_NameADirectionOnTheScreen_NotATurn()
        {
            Assert.AreEqual(Vector2.up, TiltMath.KeysToScreen(false, false, true, false), "W");
            Assert.AreEqual(Vector2.down, TiltMath.KeysToScreen(false, false, false, true), "S");
            Assert.AreEqual(Vector2.right, TiltMath.KeysToScreen(false, true, false, false), "D");
            Assert.AreEqual(Vector2.left, TiltMath.KeysToScreen(true, false, false, false), "A");
            Assert.AreEqual(Vector2.zero, TiltMath.KeysToScreen(true, true, true, true), "alle Tasten");
            // A diagonal is not faster than a straight run.
            Vector2 wd = TiltMath.KeysToScreen(false, true, true, false);
            Assert.AreEqual(1f, wd.magnitude, 1e-4f);
            Assert.AreEqual(45f, Vector2.Angle(Vector2.up, wd), 1e-3f);
        }

        [Test]
        public void ScreenDirection_TurnsWithTheCameraYaw_NotWithTheIsland()
        {
            // Camera looking north (+Z): up the screen is +Z, right is +X.
            Assert.AreEqual(new Vector2(0f, 1f), TiltMath.ToWorld(Vector2.up, 0f));
            Assert.AreEqual(new Vector2(1f, 0f), TiltMath.ToWorld(Vector2.right, 0f));
            // Camera looking east (+X): up the screen is +X, right is -Z.
            var up = TiltMath.ToWorld(Vector2.up, 90f);
            var right = TiltMath.ToWorld(Vector2.right, 90f);
            Assert.AreEqual(1f, up.x, 1e-5f);
            Assert.AreEqual(0f, up.y, 1e-5f);
            Assert.AreEqual(0f, right.x, 1e-5f);
            Assert.AreEqual(-1f, right.y, 1e-5f);
            // Half a stick deflection stays half throttle.
            Assert.AreEqual(0.5f, TiltMath.ToWorld(new Vector2(0f, 0.5f), 37f).magnitude, 1e-5f);
        }

        [Test]
        public void ClampStick_NeverExceedsFullThrottle()
        {
            Assert.AreEqual(1f, TiltMath.ClampStick(new Vector2(3f, 4f)).magnitude, 1e-5f);
            Assert.AreEqual(0.4f, TiltMath.ClampStick(new Vector2(0f, 0.4f)).magnitude, 1e-5f);
        }

        // ---------------------------------------------------------------- the island really drives that way

        [Test]
        public void CommandedDirection_MovesTheIsland_WithoutTurningIt()
        {
            Island.DirectionSteering = true;
            var isl = MakeIsland(0f);
            float yaw0 = isl.Yaw;

            // East for two seconds, then south for two: a cozy drive around a corner.
            Vector2 start = isl.PlanarPosition;
            for (int i = 0; i < 100; i++) isl.Tick(Vector2.zero, new Vector2(1f, 0f), 0.02f);
            Vector2 mid = isl.PlanarPosition;
            for (int i = 0; i < 100; i++) isl.Tick(Vector2.zero, new Vector2(0f, -1f), 0.02f);
            Vector2 end = isl.PlanarPosition;

            Assert.Greater((mid - start).magnitude, 4f, "travelled east");
            Assert.Less(Vector2.Angle(mid - start, new Vector2(1f, 0f)), 6f, "course east");
            Assert.Less(Vector2.Angle(end - mid, new Vector2(0f, -1f)), 30f, "course south after the corner");
            Assert.Less(Vector2.Angle(isl.SelfVelocity, new Vector2(0f, -1f)), 8f, "running south by the end");
            Assert.AreEqual(0f, Mathf.DeltaAngle(yaw0, isl.Yaw), 1e-3f, "heading never turns");
            Assert.AreEqual(1f, isl.Forward.z, 1e-3f, "heading still points north");
        }

        [Test]
        public void HoldingADirection_NeverCurvesAway()
        {
            Island.DirectionSteering = true;
            var isl = MakeIsland(140f);
            var dir = new Vector2(0.6f, 0.8f);
            for (int i = 0; i < 150; i++) isl.Tick(Vector2.zero, dir, 0.02f);
            Vector2 a = isl.PlanarPosition;
            for (int i = 0; i < 150; i++) isl.Tick(Vector2.zero, dir, 0.02f);
            Vector2 b = isl.PlanarPosition;
            // Second leg is still the commanded direction: nothing wandered while the direction was held.
            Assert.Less(Vector2.Angle(b - a, dir), 1f);
            Assert.AreEqual(140f, isl.Yaw, 1e-2f, "heading untouched whatever way the island drives");
        }

        [Test]
        public void ReleasingTheDirection_LetsTheIslandGlide()
        {
            Island.DirectionSteering = true;
            var isl = MakeIsland(0f);
            for (int i = 0; i < 100; i++) isl.Tick(Vector2.zero, new Vector2(1f, 0f), 0.02f);
            float fast = isl.SelfVelocity.magnitude;
            for (int i = 0; i < 100; i++) isl.Tick(Vector2.zero, Vector2.zero, 0.02f);
            float slow = isl.SelfVelocity.magnitude;
            Assert.Less(slow, fast * 0.75f, "the island rolls out");
            Assert.Greater(slow, fast * 0.05f, "but keeps gliding, it does not stop dead");
            Assert.Less(Vector2.Angle(isl.SelfVelocity, new Vector2(1f, 0f)), 5f, "and keeps its course");
        }

        // ---------------------------------------------------------------- the fixed view

        [Test]
        public void FixedView_KeepsNorthUp_WhateverTheIslandsHeadingIs()
        {
            Island.DirectionSteering = true;
            var isl = MakeIsland(0f);
            var camGo = new GameObject("TestChaseCamera");
            _objects.Add(camGo);
            var cam = camGo.AddComponent<IslandChaseCamera>();
            cam.target = isl;
            cam.SnapToTarget();
            Vector3 northPose = camGo.transform.position;
            float northYaw = camGo.transform.eulerAngles.y;

            Assert.AreEqual(0f, cam.ViewYawDeg, 1e-3f);
            Assert.Less(northPose.z, isl.transform.position.z, "camera sits south of the island");
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, northYaw), 0.5f, "and looks north");

            // The same island, turned right round: the view must not move with it.
            var turned = MakeIsland(137f);
            cam.target = turned;
            cam.SnapToTarget();
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, camGo.transform.eulerAngles.y), 0.5f);
            Assert.Less(camGo.transform.position.z, turned.transform.position.z);

            // With the old steering wheel the camera goes back to sitting behind the heading.
            Island.DirectionSteering = false;
            cam.SnapToTarget();
            Assert.AreEqual(137f, Mathf.Repeat(camGo.transform.eulerAngles.y, 360f), 0.5f);
        }

        // ---------------------------------------------------------------- camera feel

        [Test]
        public void SmoothPose_StandsStillWhileTheGameIsPaused()
        {
            var pose = new Vector3(0f, 9f, -7f);
            var desired = new Vector3(40f, 9f, 33f);
            Assert.AreEqual(pose, IslandChaseCamera.SmoothPose(pose, desired, 5f, 0f));
            Assert.AreEqual(pose, IslandChaseCamera.SmoothPose(pose, desired, 5f, -1f));
            var stepped = IslandChaseCamera.SmoothPose(pose, desired, 5f, 0.0166f);
            Assert.Greater(Vector3.Distance(stepped, pose), 0.1f);
            Assert.Less(Vector3.Distance(stepped, desired), Vector3.Distance(pose, desired));
        }

        // The bug the owner saw: with the shake added to the transform and then smoothed from there again,
        // nothing pulls the camera back while Time.deltaTime is 0 and it walks off the island.
        [Test]
        public void ShakeIsAnOffset_SoAPausedCameraNeverWandersOff()
        {
            var desired = new Vector3(0f, 9f, -7f);
            var shake = new Vector3(0.05f, 0.02f, 0f);
            Vector3 pose = desired;
            float worst = 0f;
            for (int frame = 0; frame < 600; frame++)
            {
                pose = IslandChaseCamera.SmoothPose(pose, desired, 5f, 0f);
                worst = Mathf.Max(worst, Vector3.Distance(pose + shake, desired));
            }
            Assert.AreEqual(shake.magnitude, worst, 1e-4f, "600 paused frames never move the view off the island");

            // And while the game runs the offset does not build up either.
            pose = desired + new Vector3(3f, 0f, 2f);
            for (int frame = 0; frame < 600; frame++) pose = IslandChaseCamera.SmoothPose(pose, desired, 5f, 0.0166f);
            Assert.Less(Vector3.Distance(pose + shake, desired), shake.magnitude + 1e-3f);
        }

        [Test]
        public void SpeedFeel_IsANudgeNotAShake()
        {
            var go = new GameObject("FeelCamera");
            _objects.Add(go);
            var cam = go.AddComponent<IslandChaseCamera>();

            // A hint of rumble at the island's own top speed, the full (still small) amount only when the
            // current carries it beyond that.
            float atTop = SpeedFeel.ShakeAmount(1f, cam.speedShakeStart) * cam.speedShake;
            float atMax = SpeedFeel.ShakeAmount(SpeedFeel.Max, cam.speedShakeStart) * cam.speedShake;
            Assert.Greater(atTop, 0.0005f, "a hint is left at top speed");
            Assert.Less(atTop, 0.01f, "but the frame no longer wobbles");
            Assert.Less(atMax, 0.03f);
            Assert.AreEqual(0f, SpeedFeel.ShakeAmount(0.5f, cam.speedShakeStart), 1e-6f, "nothing while puttering");

            // Framing: a noticeable but gentle opening, never a lurch backwards.
            Assert.LessOrEqual(cam.speedFovGain * SpeedFeel.Max, 6f, "field of view opens by at most 6 degrees");
            Assert.LessOrEqual(cam.speedPullBack * SpeedFeel.Max, 0.2f, "and the camera drops back by at most a fifth");
            Assert.LessOrEqual(cam.turnRoll, 1.5f);
        }

        [Test]
        public void Bank_FollowsTheCourse_AndStaysWithinTheSetDegrees()
        {
            Assert.AreEqual(0f, SpeedFeel.Bank(0f, 90f, 1f, 1.2f), 1e-6f);
            Assert.AreEqual(1.2f, SpeedFeel.Bank(200f, 90f, 1f, 1.2f), 1e-4f, "clamped to the slider");
            Assert.AreEqual(-1.2f, SpeedFeel.Bank(-200f, 90f, 1f, 1.2f), 1e-4f);
            Assert.AreEqual(0f, SpeedFeel.Bank(90f, 90f, 0f, 1.2f), 1e-6f, "no lean while standing still");
        }
    }
}
