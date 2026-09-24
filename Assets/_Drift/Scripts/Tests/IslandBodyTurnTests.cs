using System.Collections.Generic;
using Drift.Islands;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class IslandBodyTurnTests
    {
        readonly List<GameObject> _objects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        Island MakeIsland(Vector2 pos, int seed, float yaw, bool player, float radius = 3f, bool fullScale = true)
        {
            var go = new GameObject(player ? "TestPlayer" : "TestIsle_" + seed);
            go.SetActive(false);
            go.transform.position = new Vector3(pos.x, 0f, pos.y);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var isl = go.AddComponent<Island>();
            isl.useKeyboardInput = player;
            isl.sinkEnabled = false;
            isl.landRadius = radius;
            isl.shapeSeed = seed;
            isl.carryResponse = 0f;
            // These tests pin the turn algorithm on its full scale; the tuned-down defaults are checked below.
            if (fullScale)
            {
                isl.bodyTurnAmount = 1f;
                isl.bodyTurnMaxQueued = 120f;
                isl.bodyTurnTime = 2.5f;
                isl.bodyTurnTimeHuge = 5f;
                isl.bodyTurnMaxRate = 70f;
                isl.bodyTurnMaxRateHuge = 35f;
            }
            go.SetActive(true);
            _objects.Add(go);
            return isl;
        }

        [Test]
        public void Defaults_TurnAThirdAsFar_AtHalfTheRate()
        {
            var host = MakeIsland(Vector2.zero, 11, 20f, true, 3f, false);
            for (int k = 0; k < 50; k++) host.Tick(new Vector2(0f, 1f), 0.02f);
            Vector2 fwd = new Vector2(host.Forward.x, host.Forward.z);
            var guest = MakeIsland(host.PlanarPosition + fwd * 5.2f, 777, 140f, false, 3f, false);
            host.MergeFrom(guest, 3f, 0f);
            host.FinishUplift();
            Assert.GreaterOrEqual(Mathf.Abs(host.LastBodyTurn), 30f - 1e-3f);
            Assert.LessOrEqual(Mathf.Abs(host.LastBodyTurn), 60f + 1e-3f, "a third of the old 90..180");
            host.driftRotation = 0f;
            float maxRate = 0f;
            for (int k = 0; k < 300; k++)
            {
                host.Tick(new Vector2(0f, 1f), 0.02f);
                maxRate = Mathf.Max(maxRate, Mathf.Abs(host.BodyTurnRate));
            }
            Assert.LessOrEqual(maxRate, 35f + 0.5f, "half the old 70 deg/s cap");
        }

        [Test]
        public void Merge_TurnsTheBodyAtLeastAQuarter_HeadingAndCourseStay()
        {
            var host = MakeIsland(Vector2.zero, 11, 20f, true);
            for (int k = 0; k < 50; k++) host.Tick(new Vector2(0f, 1f), 0.02f);
            Vector2 fwd = new Vector2(host.Forward.x, host.Forward.z);
            var guest = MakeIsland(host.PlanarPosition + fwd * 5.2f, 777, 140f, false);

            float yaw0 = host.Yaw, body0 = host.BodyYaw;
            host.MergeFrom(guest, 3f, 0f);
            host.FinishUplift();
            Assert.GreaterOrEqual(Mathf.Abs(host.BodyTurnRemaining), 90f - 1e-3f);
            Assert.LessOrEqual(Mathf.Abs(host.BodyTurnRemaining), 180f + 1e-3f);

            host.driftRotation = 0f;
            float queued = host.BodyTurnRemaining;
            for (int k = 0; k < 600; k++) host.Tick(new Vector2(0f, 1f), 0.02f);

            Assert.IsFalse(host.IsBodyTurning);
            Assert.AreEqual(0f, Mathf.DeltaAngle(yaw0, host.Yaw), 1e-3f, "heading");
            Assert.AreEqual(0f, Mathf.DeltaAngle(body0 + queued, host.BodyYaw), 0.5f, "body yaw");
            Assert.Less(Vector2.Angle(fwd, host.SelfVelocity), 0.1f, "course");
            Assert.AreEqual(0f, Mathf.DeltaAngle(host.Yaw + host.BodyYaw, host.transform.eulerAngles.y), 0.01f, "transform = heading + body");
        }

        Island Ram(Island host, float ahead, float side, int seed)
        {
            Vector2 fwd = new Vector2(host.Forward.x, host.Forward.z);
            Vector2 right = new Vector2(fwd.y, -fwd.x);
            var guest = MakeIsland(host.PlanarPosition + fwd * ahead + right * side, seed, 140f, false);
            host.MergeFrom(guest, 3f, 0f);
            host.FinishUplift();
            return guest;
        }

        [Test]
        public void TorqueSign_FollowsTheContactSide_InHeadingSpace()
        {
            Assert.AreEqual(1f, Island.TorqueSign(Vector2.up, new Vector2(1f, 1f), 5f, 5f, false), "right of the nose = clockwise");
            Assert.AreEqual(-1f, Island.TorqueSign(Vector2.up, new Vector2(-1f, 1f), 5f, 5f, true), "left of the nose = counter-clockwise");
            Assert.AreEqual(1f, Island.TorqueSign(Vector2.right, new Vector2(1f, -1f), 5f, 5f, false), "heading east: south is right");
            Assert.AreEqual(-1f, Island.TorqueSign(Vector2.right, new Vector2(1f, 1f), 5f, 5f, true));
            Assert.AreEqual(1f, Island.TorqueSign(Vector2.up, new Vector2(2f, -3f), 5f, 5f, false), "a hit on the right rear still turns clockwise");
        }

        [Test]
        public void TorqueSign_DeadCentre_TurnsTheNoseToTheSideWithLessLand()
        {
            Assert.AreEqual(1f, Island.TorqueSign(Vector2.up, new Vector2(0.02f, 1f), 10f, 4f, false));
            Assert.AreEqual(-1f, Island.TorqueSign(Vector2.up, new Vector2(-0.02f, 1f), 4f, 10f, true));
            Assert.AreEqual(1f, Island.TorqueSign(Vector2.up, Vector2.up, 7f, 7f, true), "balanced: the tie flag decides");
            Assert.AreEqual(-1f, Island.TorqueSign(Vector2.up, Vector2.up, 7f, 7f, false));
        }

        [TestCase(1, true)]
        [TestCase(-1, true)]
        [TestCase(1, false)]
        [TestCase(-1, false)]
        public void Merge_TurnsTowardsTheStruckSide_WhateverTheBodyYaw(int side, bool player)
        {
            var host = MakeIsland(Vector2.zero, 11, 20f, player);
            host.SetBodyYaw(130f);
            Ram(host, 4.4f, side * 2.6f, 777);

            Assert.AreEqual(side, (int)Mathf.Sign(host.LastBodyTurn), "direction");
            Assert.GreaterOrEqual(Mathf.Abs(host.LastBodyTurn), 90f - 1e-3f);
            Assert.LessOrEqual(Mathf.Abs(host.LastBodyTurn), 180f + 1e-3f);
            Assert.AreEqual(host.LastBodyTurn, host.BodyTurnRemaining, 1e-3f);
        }

        [Test]
        public void RapidMerges_TurnLess_AndTheQueueIsCapped()
        {
            var host = MakeIsland(Vector2.zero, 11, 0f, true);
            host.driftRotation = 0f;
            Ram(host, 4.6f, 2.2f, 777);
            float first = host.LastBodyTurn;
            Assert.GreaterOrEqual(first, 90f - 1e-3f);

            for (int k = 0; k < 50; k++) host.Tick(Vector2.zero, 0.02f);
            Ram(host, host.BoundingRadius + 1.5f, 2.2f, 778);
            float second = host.LastBodyTurn;
            Assert.Greater(second, 0f, "same side, same direction");
            Assert.LessOrEqual(second, Mathf.Lerp(host.bodyTurnRapidAngle, 180f, 1f / host.bodyTurnCooldown) + 1e-3f);
            Assert.Less(second, 0.55f * first, "one second after a merge the turn is mostly suppressed");
            Assert.LessOrEqual(Mathf.Abs(host.BodyTurnRemaining), host.bodyTurnMaxQueued + 1e-3f);

            Ram(host, host.BoundingRadius + 1.5f, 2.2f, 779);
            Assert.LessOrEqual(Mathf.Abs(host.LastBodyTurn), host.bodyTurnRapidAngle + 1e-3f, "same instant = at most the minimum turn");
            Ram(host, host.BoundingRadius + 1.5f, 2.2f, 780);
            Ram(host, host.BoundingRadius + 1.5f, 2.2f, 781);
            Assert.LessOrEqual(Mathf.Abs(host.BodyTurnRemaining), host.bodyTurnMaxQueued + 1e-3f);

            for (int k = 0; k < 800; k++) host.Tick(Vector2.zero, 0.02f);
            Assert.IsFalse(host.IsBodyTurning);
            Ram(host, host.BoundingRadius + 1.5f, 2.2f, 782);
            Assert.GreaterOrEqual(Mathf.Abs(host.LastBodyTurn), 90f - 1e-3f, "after the cooldown a merge turns fully again");
        }

        struct Response
        {
            public float angle, t50, t90, t95, t99, peakRate, overshoot, maxRateStep, firstRate;
        }

        // Ticks through the turn that is queued right now and measures its time response.
        static Response Measure(Island host, float dt, float seconds)
        {
            var r = new Response { angle = host.BodyTurnRemaining, t50 = -1f, t90 = -1f, t95 = -1f, t99 = -1f };
            float sign = Mathf.Sign(r.angle), total = Mathf.Abs(r.angle), lastRate = host.BodyTurnRate * sign, lastRemaining = total;
            bool first = true;
            for (float t = dt; t <= seconds; t += dt)
            {
                host.Tick(Vector2.zero, dt);
                float remaining = host.BodyTurnRemaining * sign;
                float rate = host.BodyTurnRate * sign;
                if (first) { r.firstRate = rate; first = false; }
                r.maxRateStep = Mathf.Max(r.maxRateStep, Mathf.Abs(rate - lastRate));
                r.peakRate = Mathf.Max(r.peakRate, rate);
                r.overshoot = Mathf.Max(r.overshoot, -remaining);
                Assert.LessOrEqual(remaining, lastRemaining + 1e-3f, "monotonic approach at " + t);
                float done = 1f - remaining / total;
                if (r.t50 < 0f && done >= 0.5f) r.t50 = t;
                if (r.t90 < 0f && done >= 0.9f) r.t90 = t;
                if (r.t95 < 0f && done >= 0.95f) r.t95 = t;
                if (r.t99 < 0f && done >= 0.99f) r.t99 = t;
                lastRate = rate;
                lastRemaining = remaining;
            }
            return r;
        }

        [Test]
        public void BodyTurn_SwellsFromRest_AndSettlesDampedWithoutOvershoot()
        {
            var host = MakeIsland(Vector2.zero, 11, 0f, true);
            host.driftRotation = 0f;
            float body0 = host.BodyYaw;
            Ram(host, 4.6f, 2.2f, 777);
            float queued = host.LastBodyTurn;
            Assert.AreEqual(0f, host.BodyTurnRate, 1e-6f, "the impact does not jump-start the rotation");

            var r = Measure(host, 0.02f, 12f);
            Assert.Less(r.firstRate, 1f, "yaw rate starts at zero");
            Assert.Less(r.maxRateStep, 6f, "yaw rate is continuous (deg/s per 20 ms tick)");
            Assert.LessOrEqual(r.peakRate, host.BodyTurnRateCap + 1e-3f);
            Assert.Greater(r.peakRate, 25f);
            Assert.LessOrEqual(r.overshoot, 3f);
            Assert.Greater(r.t50, 0.2f * host.BodyTurnSettleTime, "no lurch: half way takes a while");
            Assert.GreaterOrEqual(r.t95, 0.9f * host.BodyTurnSettleTime);
            Assert.LessOrEqual(r.t95, host.BodyTurnSettleTime + Mathf.Abs(queued) / host.BodyTurnRateCap);
            Assert.Greater(r.t99, r.t95 + 0.2f * host.BodyTurnSettleTime, "a long soft tail");
            Assert.IsFalse(host.IsBodyTurning);
            Assert.AreEqual(0f, host.BodyTurnRate);
            Assert.AreEqual(0f, Mathf.DeltaAngle(body0 + queued, host.BodyYaw), 0.05f);
        }

        [Test]
        public void BodyTurn_AContinentTurnsMoreSluggishly()
        {
            var small = MakeIsland(Vector2.zero, 11, 0f, true);
            var huge = MakeIsland(new Vector2(300f, 0f), 12, 0f, true, 22f);
            small.driftRotation = 0f;
            huge.driftRotation = 0f;
            Assert.AreEqual(small.bodyTurnTime, small.BodyTurnSettleTime, 0.05f);
            Assert.AreEqual(small.bodyTurnMaxRate, small.BodyTurnRateCap, 1f);
            Assert.Greater(huge.LandArea, 1000f);
            Assert.AreEqual(5f, huge.BodyTurnSettleTime, 0.5f);
            Assert.Less(huge.BodyTurnRateCap, 42f);

            Ram(small, 4.6f, 2.2f, 777);
            Ram(huge, huge.BoundingRadius + 1.5f, 6f, 778);
            var rs = Measure(small, 0.02f, 20f);
            var rh = Measure(huge, 0.02f, 20f);
            Assert.Greater(rh.t99, rs.t99);
            Assert.Greater(rh.t50 / Mathf.Abs(rh.angle), 1.3f * rs.t50 / Mathf.Abs(rs.angle), "seconds per degree");
            Assert.LessOrEqual(rh.peakRate, huge.BodyTurnRateCap + 1e-3f);
            Assert.Less(rh.peakRate, rs.peakRate);
            Assert.LessOrEqual(rh.overshoot, 3f);
            Assert.IsFalse(huge.IsBodyTurning);
        }

        [TestCase(0.25f)]
        [TestCase(1.5f)]
        [TestCase(10f)]
        public void BodyTurn_IsStableForLargeTimeSteps(float dt)
        {
            var host = MakeIsland(Vector2.zero, 11, 0f, true);
            host.driftRotation = 0f;
            float body0 = host.BodyYaw;
            Ram(host, 4.6f, 2.2f, 777);
            float queued = host.LastBodyTurn;

            var r = Measure(host, dt, 40f);
            Assert.IsFalse(float.IsNaN(host.BodyYaw));
            Assert.LessOrEqual(r.overshoot, 3f);
            Assert.LessOrEqual(r.peakRate, host.BodyTurnRateCap + 1e-3f);
            Assert.IsFalse(host.IsBodyTurning);
            Assert.AreEqual(0f, Mathf.DeltaAngle(body0 + queued, host.BodyYaw), 0.05f);
        }

        [Test]
        public void BodyTurn_ASecondMergeAddsToTheTargetWithoutAJolt()
        {
            var host = MakeIsland(Vector2.zero, 11, 0f, true);
            host.driftRotation = 0f;
            float body0 = host.BodyYaw;
            Ram(host, 4.6f, 2.2f, 777);
            float first = host.LastBodyTurn;
            for (int k = 0; k < 60; k++) host.Tick(Vector2.zero, 0.02f);
            float rateBefore = host.BodyTurnRate, remainingBefore = host.BodyTurnRemaining;
            Assert.Greater(rateBefore, 10f);

            Ram(host, host.BoundingRadius + 1.5f, 2.2f, 778);
            float second = host.LastBodyTurn;
            Assert.Greater(second, 0f);
            Assert.AreEqual(rateBefore, host.BodyTurnRate, 1e-4f, "the new impact leaves the yaw rate as it is");
            Assert.AreEqual(remainingBefore + second, host.BodyTurnRemaining, 1e-3f, "targets add up");

            var r = Measure(host, 0.02f, 16f);
            Assert.Less(r.maxRateStep, 6f);
            Assert.LessOrEqual(r.overshoot, 3f);
            Assert.IsFalse(host.IsBodyTurning);
            Assert.AreEqual(0f, Mathf.DeltaAngle(body0 + first + second, host.BodyYaw), 0.1f);
        }

        [Test]
        public void BodyTurn_InFlightIsSavedAsFinished_AndSetBodyYawSnaps()
        {
            var host = MakeIsland(Vector2.zero, 11, 0f, true);
            host.driftRotation = 0f;
            float body0 = host.BodyYaw;
            Ram(host, 4.6f, 2.2f, 777);
            float queued = host.LastBodyTurn;
            for (int k = 0; k < 40; k++) host.Tick(Vector2.zero, 0.02f);
            Assert.IsTrue(host.IsBodyTurning);
            Assert.Greater(Mathf.Abs(host.BodyTurnRemaining), 5f);

            Assert.AreEqual(0f, Mathf.DeltaAngle(body0 + queued, host.Capture().bodyYaw), 1e-3f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(body0 + queued, IslandSaveUtil.CapturePose(host).bodyYaw), 1e-3f);

            host.SetBodyYaw(33f);
            Assert.IsFalse(host.IsBodyTurning);
            Assert.AreEqual(0f, host.BodyTurnRate);
            Assert.AreEqual(0f, host.BodyTurnRemaining);
            host.Tick(Vector2.zero, 0.02f);
            Assert.AreEqual(33f, host.BodyYaw, 1e-3f);
        }

        [Test]
        public void LocalSpace_IsTheBodySpace_AndMatchesTheTransform()
        {
            var isl = MakeIsland(new Vector2(4f, -2f), 12, 35f, false);
            var local = new Vector2(0.7f, -0.4f);
            float h0 = isl.SampleHeight(local);
            Vector2 w0 = isl.ToWorld(local);

            isl.SetBodyYaw(110f);
            Assert.AreEqual(35f, isl.Yaw, 1e-3f);
            Assert.AreEqual(h0, isl.SampleHeight(local), 1e-6f);

            Vector2 w1 = isl.ToWorld(local);
            Vector3 viaTransform = isl.transform.TransformPoint(new Vector3(local.x, 0f, local.y));
            Assert.AreEqual(0f, Vector2.Distance(w1, new Vector2(viaTransform.x, viaTransform.z)), 1e-4f);
            Assert.AreEqual(0f, Vector2.Distance(local, isl.ToLocal(w1)), 1e-4f);
            // Unity yaw is clockwise, SignedAngle counter-clockwise.
            Assert.AreEqual(-110f, Vector2.SignedAngle(w0 - isl.PlanarPosition, w1 - isl.PlanarPosition), 0.01f);
        }

        [Test]
        public void SaveData_RoundTripsTheBodyYaw_OldSavesReadZero()
        {
            var a = MakeIsland(Vector2.zero, 13, 50f, false);
            a.SetBodyYaw(-68.5f);

            string json = JsonUtility.ToJson(a.Capture());
            var data = JsonUtility.FromJson<IslandSaveData>(json);
            var b = MakeIsland(Vector2.zero, 99, data.yaw, false);
            IslandSaveUtil.ApplySaved(b, data);
            Assert.AreEqual(50f, b.Yaw, 1e-3f);
            Assert.AreEqual(-68.5f, b.BodyYaw, 1e-3f);

            var pose = JsonUtility.FromJson<IslandSaveData>(JsonUtility.ToJson(IslandSaveUtil.CapturePose(a)));
            Assert.IsFalse(pose.HasShape);
            var c = MakeIsland(Vector2.zero, 13, pose.yaw, false);
            IslandSaveUtil.ApplySaved(c, pose);
            Assert.AreEqual(-68.5f, c.BodyYaw, 1e-3f);

            var old = JsonUtility.FromJson<IslandSaveData>(json.Replace("\"bodyYaw\":", "\"unknownKey\":"));
            Assert.AreEqual(0f, old.bodyYaw);
        }

        [Test]
        public void FollowPose_CloseUpIgnoresIslandSizeAndFlattens()
        {
            Assert.AreEqual(0f, IslandChaseCamera.CloseBlend(IslandChaseCamera.CloseZoomStart), 1e-6f);
            Assert.AreEqual(0f, IslandChaseCamera.CloseBlend(2f), 1e-6f);
            Assert.AreEqual(1f, IslandChaseCamera.CloseBlend(IslandChaseCamera.CloseZoomEnd), 1e-6f);

            float z = IslandChaseCamera.CloseZoomEnd;
            IslandChaseCamera.FollowPose(Vector3.zero, Vector3.up, Vector3.back, 3f, 3f, 0.85f, 9f, 7f, z, out var small, out _, out _);
            IslandChaseCamera.FollowPose(Vector3.zero, Vector3.up, Vector3.back, 30f, 3f, 0.85f, 9f, 7f, z, out var big, out _, out float bigScale);
            Assert.AreEqual(1f, bigScale, 1e-5f);
            Assert.AreEqual(0f, Vector3.Distance(small, big), 1e-5f);
            Assert.Less(small.magnitude, 1f);

            float last = 0f;
            for (float zoom = z; zoom <= 3f; zoom += 0.02f)
            {
                IslandChaseCamera.FollowPose(Vector3.zero, Vector3.up, Vector3.back, 12f, 3f, 0.85f, 9f, 7f, zoom, out var p, out _, out _);
                Assert.Greater(p.magnitude, last, "distance grows with zoom at " + zoom);
                last = p.magnitude;
            }
        }
    }
}
