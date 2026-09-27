using Drift.Audio;
using Drift.Islands;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class SpeedFeelTests
    {
        [Test]
        public void Drive_StaysQuietWhilePuttering_AndReachesOneAtTopSpeed()
        {
            Assert.AreEqual(0f, SpeedFeel.Drive(0f, 8f), 1e-6f);
            Assert.AreEqual(0f, SpeedFeel.Drive(8f * SpeedFeel.DriveStart, 8f), 1e-6f);
            Assert.AreEqual(1f, SpeedFeel.Drive(8f, 8f), 1e-4f);
            Assert.AreEqual(0f, SpeedFeel.Drive(5f, 0f), 1e-6f);

            float last = -1f;
            for (float s = 0f; s <= 8f; s += 0.2f)
            {
                float d = SpeedFeel.Drive(s, 8f);
                Assert.GreaterOrEqual(d, last, "drive never falls with speed at " + s);
                last = d;
            }
        }

        [Test]
        public void Drive_KeepsRisingAboveTopSpeed_WhenTheWaterCarries()
        {
            float own = SpeedFeel.Drive(8f, 8f);
            float carried = SpeedFeel.Drive(8f * (1f + SpeedFeel.OverdriveSpan), 8f);
            Assert.Greater(carried, own);
            Assert.AreEqual(SpeedFeel.Max, carried, 1e-4f);
            // Nothing beyond that: the framing must not run away when a plate really shoves.
            Assert.AreEqual(SpeedFeel.Max, SpeedFeel.Drive(80f, 8f), 1e-4f);
        }

        [Test]
        public void CurrentAlignment_IsPlusOneWithTheCurrentAndMinusOneAgainstIt()
        {
            Assert.AreEqual(1f, SpeedFeel.CurrentAlignment(new Vector2(2f, 0f), new Vector2(5f, 0f)), 1e-5f);
            Assert.AreEqual(-1f, SpeedFeel.CurrentAlignment(new Vector2(2f, 0f), new Vector2(-5f, 0f)), 1e-5f);
            Assert.AreEqual(0f, SpeedFeel.CurrentAlignment(new Vector2(0f, 2f), new Vector2(5f, 0f)), 1e-5f);
            Assert.AreEqual(0f, SpeedFeel.CurrentAlignment(Vector2.zero, new Vector2(5f, 0f)), 1e-6f);
            Assert.AreEqual(0f, SpeedFeel.CurrentAlignment(new Vector2(2f, 0f), Vector2.zero), 1e-6f);
        }

        [Test]
        public void FlowTarget_NeedsBothTheRightWayAndARealCurrent()
        {
            Assert.AreEqual(0f, SpeedFeel.FlowTarget(-1f, 3f), 1e-6f);
            Assert.AreEqual(0f, SpeedFeel.FlowTarget(0.2f, 3f), 1e-6f);
            Assert.AreEqual(0f, SpeedFeel.FlowTarget(1f, 0f), 1e-6f);
            Assert.AreEqual(1f, SpeedFeel.FlowTarget(1f, SpeedFeel.FlowFullCurrent), 1e-5f);
            Assert.Less(SpeedFeel.FlowTarget(1f, SpeedFeel.FlowFullCurrent * 0.4f), 0.45f);
        }

        [Test]
        public void Tracker_BuildsFlowOverSeconds_AndLosesItQuickly()
        {
            var t = new SpeedFeel.Tracker();
            var carry = new Vector2(2.5f, 0f);
            var self = new Vector2(6f, 0f);
            for (int i = 0; i < 180; i++) t.Step(8.5f, 8f, carry, self, 0f, 1f / 60f);
            Assert.Greater(t.Flow, 0.6f, "three seconds with the current build the flow");
            Assert.AreEqual(1f, t.Alignment, 1e-4f);
            float built = t.Flow;

            for (int i = 0; i < 30; i++) t.Step(6f, 8f, Vector2.zero, self, 0f, 1f / 60f);
            Assert.Less(t.Flow, built * 0.45f, "half a second off the stream and the flow is gone");
        }

        [Test]
        public void Tracker_FlowIgnoresACurrentPushingAgainstYou()
        {
            var t = new SpeedFeel.Tracker();
            for (int i = 0; i < 300; i++) t.Step(4f, 8f, new Vector2(-2.5f, 0f), new Vector2(6f, 0f), 0f, 1f / 60f);
            Assert.AreEqual(0f, t.Flow, 1e-4f);
            Assert.AreEqual(-1f, t.Alignment, 1e-4f);
        }

        [Test]
        public void Tracker_SurfRisesOnTheSeamAndStopsWhenItIsLost()
        {
            var t = new SpeedFeel.Tracker();
            for (int i = 0; i < 30; i++) t.Step(8f, 8f, Vector2.zero, new Vector2(8f, 0f), 0.9f, 1f / 60f);
            Assert.Greater(t.Surf, 0.7f);
            for (int i = 0; i < 60; i++) t.Step(8f, 8f, Vector2.zero, new Vector2(8f, 0f), 0f, 1f / 60f);
            Assert.Less(t.Surf, 0.1f);
        }

        [Test]
        public void Tracker_SnapsWhenSteppedWithoutTime_SoEditModeRendersShowTheRealState()
        {
            var t = new SpeedFeel.Tracker();
            t.Step(8f, 8f, new Vector2(2.5f, 0f), new Vector2(8f, 0f), 0.5f, 0f);
            Assert.AreEqual(1f, t.Drive, 1e-4f);
            Assert.AreEqual(1f, t.Flow, 1e-4f);
            Assert.AreEqual(0.5f, t.Surf, 1e-4f);
        }

        [Test]
        public void ShakeAmount_OnlyShowsUpNearTopSpeed()
        {
            Assert.AreEqual(0f, SpeedFeel.ShakeAmount(0f, 0.8f), 1e-6f);
            Assert.AreEqual(0f, SpeedFeel.ShakeAmount(0.8f, 0.8f), 1e-6f);
            Assert.Less(SpeedFeel.ShakeAmount(1f, 0.8f), 0.15f);
            Assert.AreEqual(1f, SpeedFeel.ShakeAmount(SpeedFeel.Max, 0.8f), 1e-5f);
        }

        [Test]
        public void Bank_LeansIntoTheTurnAndOnlyWhileMoving()
        {
            Assert.Greater(SpeedFeel.Bank(90f, 90f, 1f, 3f), 0f, "a right turn leans right");
            Assert.AreEqual(3f, SpeedFeel.Bank(90f, 90f, 1f, 3f), 1e-5f);
            Assert.AreEqual(-3f, SpeedFeel.Bank(-180f, 90f, 1f, 3f), 1e-5f);
            Assert.AreEqual(0f, SpeedFeel.Bank(90f, 90f, 0f, 3f), 1e-6f);
            Assert.AreEqual(0f, SpeedFeel.Bank(90f, 90f, 1f, 0f), 1e-6f);
        }

        [Test]
        public void Framing_IsCappedAtTheOverdriveMaximum()
        {
            Assert.AreEqual(0f, SpeedFeel.Framing(-1f), 1e-6f);
            Assert.AreEqual(SpeedFeel.Max, SpeedFeel.Framing(9f), 1e-6f);
        }

        [Test]
        public void CatchesSurf_FiresOnceOnJumpingOn_AndNotWhileABoostRuns()
        {
            Assert.IsTrue(SpeedFeel.CatchesSurf(0.5f, false, 0.35f, 0f, false));
            Assert.IsFalse(SpeedFeel.CatchesSurf(0.5f, true, 0.35f, 0f, false), "already surfing");
            Assert.IsFalse(SpeedFeel.CatchesSurf(0.2f, false, 0.35f, 0f, false), "not on the seam yet");
            Assert.IsFalse(SpeedFeel.CatchesSurf(0.5f, false, 0.35f, 1.2f, false), "cooldown");
            Assert.IsFalse(SpeedFeel.CatchesSurf(0.5f, false, 0.35f, 0f, true), "another boost owns the island");
        }

        [Test]
        public void FlowPush_OnlyAboveTheThresholdAndNeverBeyondTheFactor()
        {
            Assert.AreEqual(1f, SpeedFeel.FlowPush(0.2f, 0.5f, 1.1f), 1e-6f);
            Assert.AreEqual(1f, SpeedFeel.FlowPush(1f, 0.5f, 1f), 1e-6f);
            Assert.AreEqual(1f, SpeedFeel.FlowPush(0.5f, 0.5f, 1.1f), 1e-6f);
            Assert.AreEqual(1.05f, SpeedFeel.FlowPush(0.75f, 0.5f, 1.1f), 1e-5f);
            Assert.AreEqual(1.1f, SpeedFeel.FlowPush(1f, 0.5f, 1.1f), 1e-5f);
        }

        [Test]
        public void SurfHz_RisesWithTheBoundaryStrength()
        {
            Assert.Less(SpeedFeel.SurfHz(0f), SpeedFeel.SurfHz(0.5f));
            Assert.Less(SpeedFeel.SurfHz(0.5f), SpeedFeel.SurfHz(1f));
        }

        static SfxSynth Bare()
        {
            var s = new SfxSynth(48000, 0x51EEDu)
            {
                WindGain = 0f,
                WhistleGain = 0f,
                WaterGain = 0f,
                WarningGain = 0f
            };
            return s;
        }

        static double Rms(SfxSynth s, float seconds)
        {
            int frames = (int)(seconds * 48000);
            var buf = new float[frames * 2];
            s.Render(buf, 2, 48000);
            double sum = 0;
            for (int i = 0; i < buf.Length; i++) sum += buf[i] * buf[i];
            return System.Math.Sqrt(sum / buf.Length);
        }

        [Test]
        public void Sfx_FlowLayerIsSilentAtZeroAndGrowsWithTheFlow()
        {
            var quiet = Bare();
            Assert.Less(Rms(quiet, 0.5f), 1e-5);

            var mid = Bare();
            mid.FlowAmount = 0.5f;
            Rms(mid, 0.5f);
            double midRms = Rms(mid, 0.5f);

            var full = Bare();
            full.FlowAmount = 1f;
            Rms(full, 0.5f);
            double fullRms = Rms(full, 0.5f);

            Assert.Greater(midRms, 1e-4);
            Assert.Greater(fullRms, midRms * 1.5);
            Assert.Less(fullRms, 0.4, "the flow layer must stay well under the mix ceiling");
        }

        [Test]
        public void Sfx_SurfToneRisesAndStopsWhenTheBoundaryIsLost()
        {
            var s = Bare();
            s.SurfAmount = 0.2f;
            Rms(s, 0.3f);
            float lowHz = s.SurfHz;
            s.SurfAmount = 1f;
            Rms(s, 0.3f);
            Assert.Greater(s.SurfHz, lowHz + 100f);
            Assert.Greater(s.SurfLevel, 0f);
            double singing = Rms(s, 0.3f);
            Assert.Greater(singing, 1e-3);

            s.SurfAmount = 0f;
            Rms(s, 2f);
            Assert.Less(Rms(s, 0.3f), singing * 0.05);
        }

        [Test]
        public void Sfx_FlowAndSurfTogetherStayInsideTheMix()
        {
            var s = Bare();
            s.WindGain = 0.55f;
            s.WaterGain = 0.5f;
            s.WindAmount = 1f;
            s.WaterAmount = 1f;
            s.FlowAmount = 1f;
            s.SurfAmount = 1f;
            s.FlowGain = SfxSynth.FlowGainFull;
            s.SurfGain = SfxSynth.SurfGainFull;
            int frames = 48000;
            var buf = new float[frames * 2];
            s.Render(buf, 2, 48000);
            s.Render(buf, 2, 48000);
            float peak = 0f;
            for (int i = 0; i < buf.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(buf[i]));
            Assert.Less(peak, 1f);
        }
    }
}
