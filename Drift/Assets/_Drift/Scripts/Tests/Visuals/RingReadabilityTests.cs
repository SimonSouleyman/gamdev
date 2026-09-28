using Drift.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The readability rules of the sky over the adventure ring and the cozy storms (RingReadability): the
    // cloud-free stretch of track, the storm puff budget on phones, rain near the camera and pickup beacons.
    public class RingReadabilityTests
    {
        static readonly Vector4 Clear = new Vector4(11f, 7f, 75f, 45f);

        [Test]
        public void Clouds_StayOffTheTrackInFrontOfThePlayer()
        {
            for (float ahead = -20f; ahead <= 75f; ahead += 5f)
            for (float across = -10f; across <= 10f; across += 2.5f)
                Assert.AreEqual(0f, RingReadability.CloudCorridor(across, ahead, Clear), 1e-5f, $"ahead {ahead}, across {across}");
        }

        [Test]
        public void Clouds_StayAsSceneryAtTheSidesFarAheadAndBehindTheCamera()
        {
            Assert.AreEqual(1f, RingReadability.CloudCorridor(18f, 20f, Clear), 1e-5f, "beside the corridor");
            Assert.AreEqual(1f, RingReadability.CloudCorridor(-20f, 5f, Clear), 1e-5f, "other side");
            Assert.AreEqual(1f, RingReadability.CloudCorridor(0f, 125f, Clear), 1e-5f, "far ahead, where the band climbs");
            Assert.AreEqual(1f, RingReadability.CloudCorridor(0f, -45f, Clear), 1e-5f, "behind the camera");
            float prev = 0f;
            for (float ahead = 75f; ahead <= 120f; ahead += 3f)
            {
                float v = RingReadability.CloudCorridor(0f, ahead, Clear);
                Assert.GreaterOrEqual(v, prev - 1e-5f, "clouds come back smoothly with the distance");
                prev = v;
            }
            for (int i = 0; i < 40; i++)
                Assert.AreEqual(1f, RingReadability.CloudCorridor(i - 20f, i * 3f - 30f, new Vector4(11f, 7f, 0f, 45f)), "0 = off");
        }

        [Test]
        public void LiteStorms_HaveLessThanHalfThePuffs()
        {
            Assert.AreEqual(CloudShadows.PuffsPerClump, RingReadability.StormPuffsPerClump(false));
            int lite = RingReadability.StormPuffsPerClump(true);
            Assert.AreEqual(6, lite);
            int full = StormVisuals.MaxStorms * RingReadability.StormClumps(14, false, 9) * RingReadability.StormPuffsPerClump(false);
            int phone = StormVisuals.MaxStorms * RingReadability.StormClumps(14, true, 9) * lite;
            Assert.AreEqual(504, full);
            Assert.Less(phone, full / 2, $"{phone} puffs on the phone vs {full}");
            // The centre and the two towers always stay: they carry the silhouette.
            for (int k = 6; k < 9; k++) Assert.IsTrue(RingReadability.KeepStormPuff(k, true));
            for (int k = 0; k < 9; k++) Assert.IsTrue(RingReadability.KeepStormPuff(k, false));
            Assert.Greater(RingReadability.StormPuffGrow(0, true), 1f, "the kept ring puffs close the gaps");
        }

        [Test]
        public void LiteMode_AutoFollowsThePhone()
        {
            Assert.IsTrue(RingReadability.IsLite(RingReadability.LiteMode.Auto, true, "PC_RPAsset"));
            Assert.IsTrue(RingReadability.IsLite(RingReadability.LiteMode.Auto, false, "Mobile_RPAsset"));
            Assert.IsFalse(RingReadability.IsLite(RingReadability.LiteMode.Auto, false, "PC_RPAsset"));
            Assert.IsFalse(RingReadability.IsLite(RingReadability.LiteMode.Auto, false, null));
            Assert.IsTrue(RingReadability.IsLite(RingReadability.LiteMode.An, false, null));
            Assert.IsFalse(RingReadability.IsLite(RingReadability.LiteMode.Aus, true, "Mobile_RPAsset"));
        }

        [Test]
        public void CozyRain_KeepsOutOfTheCamerasFace()
        {
            Vector4 cozy = RingReadability.RainNear(new Vector2(12f, 38f), 0.2f, 70f);
            Vector4 adventure = RingReadability.RainNear(new Vector2(6f, 18f), 1f, 18f);
            Assert.AreEqual(0f, RingReadability.RainVisible(cozy, 10f), 1e-5f);
            Assert.AreEqual(1f, RingReadability.RainVisible(cozy, 40f), 1e-5f);
            Assert.Less(RingReadability.RainVisible(cozy, 18f), RingReadability.RainVisible(adventure, 18f));
            Assert.AreEqual(0.35f, adventure.z, 1e-5f, "adventure keeps every streak lane");
            Assert.Greater(cozy.z, 0.8f, "cozy keeps only a few lanes close up");
            Assert.GreaterOrEqual(cozy.w, cozy.y);
        }

        [Test]
        public void Beacons_BurnBrighterAtNightAndGoOutWhenCollected()
        {
            float day = RingReadability.BeaconStrength(1f, -1f, 0f, 0.5f);
            float night = RingReadability.BeaconStrength(1f, -1f, 1f, 0.5f);
            Assert.AreEqual(0.5f, day, 1e-5f);
            Assert.AreEqual(1f, night, 1e-5f);
            Assert.AreEqual(0f, RingReadability.BeaconStrength(0f, -1f, 1f, 0.5f), 1e-5f, "not faded in yet");
            float prev = night;
            for (float c = 0f; c <= 1f; c += 0.1f)
            {
                float v = RingReadability.BeaconStrength(1f, c, 1f, 0.5f);
                Assert.LessOrEqual(v, prev + 1e-5f);
                prev = v;
            }
            Assert.AreEqual(0f, RingReadability.BeaconStrength(1f, 0.6f, 1f, 0.5f), 1e-5f, "gone before the piece has shrunk away");
        }
    }
}
