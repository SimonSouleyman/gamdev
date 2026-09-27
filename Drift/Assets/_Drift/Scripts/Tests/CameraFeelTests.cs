using System.Collections.Generic;
using Drift.Islands;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The 2026-09-23 play-test round: the adventure camera must look fast (boost, near misses), and the cozy
    // camera must keep animals readable on big islands without losing the overview.
    public class CameraFeelTests
    {
        readonly List<GameObject> _objects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        IslandChaseCamera MakeCamera()
        {
            var go = new GameObject("FeelCamera");
            _objects.Add(go);
            return go.AddComponent<IslandChaseCamera>();
        }

        // ---------------------------------------------------------------- boost and kicks

        [Test]
        public void BoostShare_IsZeroWithoutBoostAndOneAtTheReference()
        {
            Assert.AreEqual(0f, SpeedFeel.BoostShare(1f, 1.55f), 1e-6f);
            Assert.AreEqual(1f, SpeedFeel.BoostShare(1.55f, 1.55f), 1e-5f);
            Assert.AreEqual(0.5f, SpeedFeel.BoostShare(1.275f, 1.55f), 1e-4f);
            Assert.AreEqual(1f, SpeedFeel.BoostShare(2.5f, 1.55f), 1e-6f, "clamped");
            Assert.AreEqual(1f, SpeedFeel.BoostShare(1.2f, 1f), 1e-6f, "a degenerate reference still reads a boost");
            Assert.AreEqual(0f, SpeedFeel.BoostShare(1f, 1f), 1e-6f);
        }

        [Test]
        public void PickupStrength_IsFullFromNothing_AndSmallWhenAlreadyBoosting()
        {
            Assert.AreEqual(1f, SpeedFeel.PickupStrength(1f, 1.55f, 1.55f, 0.3f), 1e-5f);
            Assert.AreEqual(0.3f, SpeedFeel.PickupStrength(1.55f, 1.55f, 1.55f, 0.3f), 1e-5f, "a refresh at full boost only nudges");
            float half = SpeedFeel.PickupStrength(1.275f, 1.55f, 1.55f, 0.3f);
            Assert.AreEqual(0.5f, half, 1e-3f);
        }

        [Test]
        public void KickBump_SwellsInsteadOfJumping_AndFadesQuickly()
        {
            Assert.AreEqual(0f, SpeedFeel.Bump(1f), 1e-6f, "starts at zero: no jump in the field of view");
            Assert.AreEqual(1f, SpeedFeel.Bump(0.5f), 1e-6f);
            Assert.AreEqual(0f, SpeedFeel.Bump(0f), 1e-6f);

            float k = 1f, peakT = 0f, peak = 0f, last = 0f, worstStep = 0f;
            const float dt = 1f / 60f;
            for (int f = 1; f <= 120; f++)
            {
                k = SpeedFeel.Decay(k, dt, 0.4f);
                float b = SpeedFeel.Bump(k);
                worstStep = Mathf.Max(worstStep, Mathf.Abs(b - last));
                last = b;
                if (b > peak) { peak = b; peakT = f * dt; }
            }
            Assert.Greater(peak, 0.99f);
            Assert.AreEqual(0.4f * Mathf.Log(2f), peakT, 0.02f, "peaks after about 0.7 x the kick time");
            Assert.Less(worstStep, 0.12f, "no frame changes it by more than an eighth");
            Assert.Less(last, 0.05f, "and after 2 s it has faded");

            Assert.AreEqual(0.6f, SpeedFeel.Decay(0.6f, 0f, 0.3f), 1e-6f, "paused: stands still");
            Assert.AreEqual(0f, SpeedFeel.Decay(0.0005f, 0.016f, 0.3f), 1e-6f, "tiny rests snap to zero");
        }

        [Test]
        public void RingDolly_MovesInWithTheSpeed_AndNeverFurtherThanTheAmount()
        {
            Assert.AreEqual(1f, SpeedFeel.RingDolly(0f, 0.12f), 1e-6f);
            Assert.AreEqual(0.88f, SpeedFeel.RingDolly(SpeedFeel.Max, 0.12f), 1e-5f);
            Assert.AreEqual(0.88f, SpeedFeel.RingDolly(5f, 0.12f), 1e-5f);
            Assert.Less(SpeedFeel.RingDolly(1f, 0.12f), 1f);
            Assert.AreEqual(1f, SpeedFeel.RingDolly(1f, 0f), 1e-6f);
        }

        [Test]
        public void AdventureFieldOfView_OpensClearlyButStaysModerate()
        {
            var cam = MakeCamera();
            float speed = cam.ringSpeedFovGain * SpeedFeel.Max;
            float boost = cam.boostFovGain;
            float kicks = cam.boostFovKick + cam.dodgeFovKick;
            Assert.GreaterOrEqual(speed + boost, 8f, "a boosted run opens the view noticeably");
            Assert.LessOrEqual(speed + boost, 12f, "but steady state stays moderate");
            Assert.LessOrEqual(speed + boost + kicks, 18f, "even with every kick at once");
            Assert.LessOrEqual(cam.ringSpeedDolly, 0.2f);
            Assert.LessOrEqual(cam.cozyBoostScale, 1f);
        }

        // ---------------------------------------------------------------- big islands

        [Test]
        public void SoftRadius_KeepsSmallIslandsAndSlowsTheGrowthAboveTheKnee()
        {
            Assert.AreEqual(8f, SpeedFeel.SoftRadius(8f, 10f, 0.5f), 1e-6f);
            Assert.AreEqual(10f, SpeedFeel.SoftRadius(10f, 10f, 0.5f), 1e-5f);
            Assert.AreEqual(20f, SpeedFeel.SoftRadius(40f, 10f, 0.5f), 1e-4f);
            Assert.AreEqual(40f, SpeedFeel.SoftRadius(40f, 10f, 1f), 1e-4f, "exponent 1 = the old framing");
            Assert.AreEqual(40f, SpeedFeel.SoftRadius(40f, 0f, 0.5f), 1e-6f, "no knee = off");

            float last = 0f;
            for (float r = 1f; r < 80f; r += 0.5f)
            {
                float s = SpeedFeel.SoftRadius(r, 10f, 0.5f);
                Assert.GreaterOrEqual(s, last, "never shrinks as the island grows");
                Assert.LessOrEqual(s, r + 1e-4f);
                last = s;
            }
        }

        [Test]
        public void IdleCloseIn_OnlyOnBigIslands()
        {
            Assert.AreEqual(1f, SpeedFeel.IdleCloseIn(3f, 0.7f, 6f), 1e-6f);
            Assert.AreEqual(1f, SpeedFeel.IdleCloseIn(6f, 0.7f, 6f), 1e-6f);
            Assert.AreEqual(0.85f, SpeedFeel.IdleCloseIn(9f, 0.7f, 6f), 1e-5f);
            Assert.AreEqual(0.7f, SpeedFeel.IdleCloseIn(40f, 0.7f, 6f), 1e-6f);
        }

        // Pixels per world unit at the island centre on a 1920 px portrait screen, field of view 60, default zoom.
        static float PxPerUnit(float radius, float knee, float soft, float lifeZoom = 1f)
        {
            float r = SpeedFeel.SoftRadius(radius, knee, soft);
            IslandChaseCamera.FollowPose(Vector3.zero, Vector3.up, Vector3.back, r, 3f, 0.85f, 9f * lifeZoom, 7f * lifeZoom, 1f,
                out Vector3 pos, out _, out _);
            return 1920f / (2f * pos.magnitude * Mathf.Tan(30f * Mathf.Deg2Rad));
        }

        [Test]
        public void BigIslands_AnimalsStayReadable_SmallIslandsUnchanged()
        {
            var cam = MakeCamera();
            float knee = cam.framingKneeRadius, soft = cam.framingSoftExponent;
            // land ~100, ~400, ~1000 have bounding radii of about 9, 20 and 35.
            Assert.AreEqual(PxPerUnit(9f, 0f, 1f), PxPerUnit(9f, knee, soft), 1e-3f, "a small island keeps its framing");
            float before400 = PxPerUnit(20f, 0f, 1f), after400 = PxPerUnit(20f, knee, soft);
            float before1000 = PxPerUnit(35f, 0f, 1f), after1000 = PxPerUnit(35f, knee, soft);
            Assert.Greater(after400, before400 * 1.25f);
            Assert.Greater(after1000, before1000 * 1.5f);
            Assert.GreaterOrEqual(after1000, 28f, "a 0.5 u animal is at least 14 px tall on a continent");
            float idle1000 = PxPerUnit(35f, knee, soft, SpeedFeel.IdleCloseIn(35f, cam.idleCloseIn, cam.idleMinRadius));
            Assert.GreaterOrEqual(idle1000, 40f, "and 20 px once the island rests");
        }

        [Test]
        public void BigIslands_ZoomingOutStillShowsTheOldOverview()
        {
            var cam = MakeCamera();
            foreach (float radius in new[] { 4f, 12f, 20f, 35f, 60f })
            {
                float factor = IslandChaseCamera.OverviewZoomFactor(radius, cam.framingKneeRadius, cam.framingSoftExponent,
                    cam.referenceRadius, cam.zoomExponent);
                Assert.GreaterOrEqual(factor, 1f);
                float soft = SpeedFeel.SoftRadius(radius, cam.framingKneeRadius, cam.framingSoftExponent);
                IslandChaseCamera.FollowPose(Vector3.zero, Vector3.up, Vector3.back, radius, cam.referenceRadius, cam.zoomExponent,
                    9f, 7f, cam.zoomMax, out Vector3 oldPos, out _, out _);
                IslandChaseCamera.FollowPose(Vector3.zero, Vector3.up, Vector3.back, soft, cam.referenceRadius, cam.zoomExponent,
                    9f, 7f, cam.zoomMax * factor, out Vector3 newPos, out _, out _);
                Assert.GreaterOrEqual(newPos.magnitude, oldPos.magnitude * 0.9f, "radius " + radius);
                Assert.AreEqual(oldPos.y, newPos.y, oldPos.y * 1e-3f, "the same height at full zoom-out, radius " + radius);
            }
        }

        [Test]
        public void ZoomMax_GrowsWithTheIsland_OnlyAboveTheKnee()
        {
            var cam = MakeCamera();
            Assert.AreEqual(cam.zoomMax, cam.ZoomMaxEffective, 1e-6f, "no target: the plain maximum");
            Assert.AreEqual(1f, IslandChaseCamera.OverviewZoomFactor(8f, 10f, 0.5f, 3f, 0.85f), 1e-6f);
            Assert.Greater(IslandChaseCamera.OverviewZoomFactor(35f, 10f, 0.5f, 3f, 0.85f), 1.5f);
        }
    }
}
