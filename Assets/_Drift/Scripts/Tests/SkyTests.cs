using Drift.Core;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The pure parts of the day / night sky (Drift.Core.SkyPalette, SkyMath; Drift.Tests does not reference
    // Drift.Visuals). Each test is a public parameterless method so it can also be invoked by reflection from
    // `unity command eval` while the shared Editor must not run tests.
    public class SkyTests
    {
        const float Step = 0.002f;
        const float AxisTilt = 35f, Azimuth = 40f, MoonTilt = 66f, MoonAzimuth = 65f, MoonLead = 0.3f;

        static float MaxDelta(Color a, Color b)
        {
            return Mathf.Max(Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g)), Mathf.Max(Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a)));
        }

        static float MaxDelta(in SkyKey a, in SkyKey b)
        {
            float d = MaxDelta(a.zenith, b.zenith);
            d = Mathf.Max(d, MaxDelta(a.horizon, b.horizon));
            d = Mathf.Max(d, MaxDelta(a.sunGlow, b.sunGlow));
            d = Mathf.Max(d, MaxDelta(a.antiGlow, b.antiGlow));
            d = Mathf.Max(d, MaxDelta(a.sunDisc, b.sunDisc));
            d = Mathf.Max(d, MaxDelta(a.cloudLit, b.cloudLit));
            d = Mathf.Max(d, MaxDelta(a.cloudDark, b.cloudDark));
            d = Mathf.Max(d, MaxDelta(a.ambient, b.ambient));
            return Mathf.Max(d, MaxDelta(a.waterDeep, b.waterDeep));
        }

        [Test]
        public void Palette_NoChannelJumpsOverAFullDay()
        {
            var palette = new SkyPalette();
            float worst = 0f;
            SkyKey prev = palette.Evaluate(0f);
            for (int i = 1; i <= 500; i++)
            {
                SkyKey now = palette.Evaluate(i * Step);
                worst = Mathf.Max(worst, MaxDelta(prev, now));
                prev = now;
            }
            Assert.Less(worst, 0.05f, "largest channel step per 0.002 of a day");
        }

        [Test]
        public void Palette_WrapsWithoutASeam()
        {
            var palette = new SkyPalette();
            Assert.Less(MaxDelta(palette.Evaluate(0.9995f), palette.Evaluate(0.0005f)), 0.01f);
            Assert.Less(MaxDelta(palette.Evaluate(1f), palette.Evaluate(0f)), 1e-5f);
            Assert.Less(MaxDelta(palette.Evaluate(-0.25f), palette.Evaluate(0.75f)), 1e-5f);
            Assert.Less(MaxDelta(palette.Evaluate(1.5f), palette.Evaluate(0.5f)), 1e-5f);
        }

        [Test]
        public void Palette_KeysAreSortedAndHitExactly()
        {
            var palette = new SkyPalette();
            for (int i = 1; i < palette.keys.Length; i++)
                Assert.Greater(palette.keys[i].time, palette.keys[i - 1].time, "key " + i);
            foreach (SkyKey k in palette.keys)
                Assert.Less(MaxDelta(palette.Evaluate(k.time), k), 1e-4f, "key at " + k.time);
        }

        [Test]
        public void Palette_StaysContinuousWithStormAndForCustomKeys()
        {
            var palette = new SkyPalette { easing = 1f };
            var tint = new Color(0.6f, 0.62f, 0.68f);
            float worst = 0f;
            SkyKey prev = SkyMath.ApplyStorm(palette.Evaluate(0f), 0.7f, 0.75f, tint, tint);
            for (int i = 1; i <= 500; i++)
            {
                SkyKey now = SkyMath.ApplyStorm(palette.Evaluate(i * Step), 0.7f, 0.75f, tint, tint);
                worst = Mathf.Max(worst, MaxDelta(prev, now));
                prev = now;
            }
            Assert.Less(worst, 0.06f);

            var two = new SkyPalette { keys = new[] { SkyPalette.DefaultKeys()[0], SkyPalette.DefaultKeys()[6] } };
            Assert.Less(MaxDelta(two.Evaluate(0.999f), two.Evaluate(0.001f)), 0.02f);
            var one = new SkyPalette { keys = new[] { SkyPalette.DefaultKeys()[6] } };
            Assert.Less(MaxDelta(one.Evaluate(0.1f), one.Evaluate(0.9f)), 1e-6f);
        }

        [Test]
        public void Palette_NightIsIndigoNotBlack_AndNoonIsBlue()
        {
            var palette = new SkyPalette();
            SkyKey night = palette.Evaluate(0f), noon = palette.Evaluate(0.5f);
            Assert.Greater(night.zenith.b, 0.1f);
            Assert.Greater(night.zenith.b, night.zenith.r * 2f);
            Assert.Greater(night.horizon.b, night.zenith.b);
            Assert.Greater(noon.zenith.b, noon.zenith.r * 3f);
            Assert.Greater(SkyMath.Luminance(noon.horizon), SkyMath.Luminance(noon.zenith));
            SkyKey sunset = palette.Evaluate(0.75f);
            Assert.Greater(sunset.horizon.r, sunset.horizon.b + 0.3f, "orange horizon");
            Assert.Greater(sunset.antiGlow.b, sunset.antiGlow.g, "violet opposite the sun");
            Assert.Greater(sunset.antiGlow.a, 0.5f);
        }

        // The sky's limb colour, the haze colour and the water's reflection tint are one value by construction:
        // DayNightCycle hands out palette.horizon as WaterSky and CurvedWorld pushes the same field as _CurveFogColor.
        [Test]
        public void HorizonColour_IsTheFogColour_AlsoInAStorm()
        {
            var palette = new SkyPalette();
            var tint = new Color(0.6f, 0.62f, 0.68f);
            for (int i = 0; i < 50; i++)
            {
                float t = i / 50f;
                SkyKey k = SkyMath.ApplyStorm(palette.Evaluate(t), i % 2 == 0 ? 0f : 0.8f, 0.75f, tint, tint);
                Color fog = k.horizon, waterSky = k.horizon;
                Assert.AreEqual(fog.linear, waterSky.linear);
                // A storm only ever darkens the horizon, it never pushes it past the clear-sky value.
                Assert.LessOrEqual(SkyMath.Luminance(k.horizon), SkyMath.Luminance(palette.Evaluate(t).horizon) + 1e-5f);
            }
        }

        [Test]
        public void Sun_RisesAtAQuarterCulminatesAtNoonSetsAtThreeQuarters()
        {
            Assert.AreEqual(0f, SkyMath.SunDirection(0.25f, AxisTilt, Azimuth).y, 1e-4f);
            Assert.AreEqual(0f, SkyMath.SunDirection(0.75f, AxisTilt, Azimuth).y, 1e-4f);
            Assert.AreEqual(Mathf.Cos(AxisTilt * Mathf.Deg2Rad), SkyMath.SunDirection(0.5f, AxisTilt, Azimuth).y, 1e-4f);
            Assert.Less(SkyMath.SunDirection(0f, AxisTilt, Azimuth).y, -0.8f);
            for (int i = 0; i < 100; i++)
                Assert.AreEqual(1f, SkyMath.SunDirection(i / 100f, AxisTilt, Azimuth).magnitude, 1e-4f);
            // Same construction as the Quaternion form DayNightCycle used before.
            float a = (0.4f - 0.25f) * Mathf.PI * 2f;
            Vector3 q = Quaternion.Euler(0f, Azimuth, 0f) * (Quaternion.Euler(AxisTilt, 0f, 0f) * new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
            Assert.Less((q - SkyMath.SunDirection(0.4f, AxisTilt, Azimuth)).magnitude, 1e-4f);
        }

        [Test]
        public void Stars_NoneAtNoon_AllAtMidnight_MonotonicThroughDuskAndDawn()
        {
            Assert.AreEqual(0f, SkyMath.StarVisibility(SkyMath.SunDirection(0.5f, AxisTilt, Azimuth).y), 1e-6f);
            Assert.AreEqual(1f, SkyMath.StarVisibility(SkyMath.SunDirection(0f, AxisTilt, Azimuth).y), 1e-6f);
            Assert.AreEqual(0f, SkyMath.StarVisibility(SkyMath.SunDirection(0.3f, AxisTilt, Azimuth).y), 1e-6f);
            float prev = 0f;
            for (float t = 0.5f; t <= 1.0001f; t += Step)
            {
                float v = SkyMath.StarVisibility(SkyMath.SunDirection(t, AxisTilt, Azimuth).y);
                Assert.GreaterOrEqual(v, prev - 1e-6f, "dusk at " + t);
                Assert.Less(v - prev, 0.06f, "no pop at " + t);
                prev = v;
            }
            Assert.AreEqual(1f, prev, 1e-6f);
            for (float t = 0f; t <= 0.5f; t += Step)
            {
                float v = SkyMath.StarVisibility(SkyMath.SunDirection(t, AxisTilt, Azimuth).y);
                Assert.LessOrEqual(v, prev + 1e-6f, "dawn at " + t);
                prev = v;
            }
            Assert.AreEqual(0f, prev, 1e-6f);
        }

        [Test]
        public void SunDisc_IsGoneBeforeTheStarsAreOut_AndSwellsAtTheHorizon()
        {
            Assert.AreEqual(1f, SkyMath.SunDiscVisibility(0.3f), 1e-6f);
            Assert.AreEqual(1f, SkyMath.SunDiscVisibility(0f), 1e-6f);
            Assert.AreEqual(0f, SkyMath.SunDiscVisibility(-0.2f), 1e-6f);
            Assert.AreEqual(0f, SkyMath.StarVisibility(-0.05f), 1e-6f);
            Assert.AreEqual(1.35f, SkyMath.SunDiscScale(0f, 1.35f), 1e-5f);
            Assert.AreEqual(1f, SkyMath.SunDiscScale(0.8f, 1.35f), 1e-5f);
        }

        [Test]
        public void Moon_RunsOppositeTheSun_AndIsUpAtNight()
        {
            for (int i = 0; i < 200; i++)
            {
                float t = i / 200f;
                Vector3 sun = SkyMath.SunDirection(t, AxisTilt, Azimuth);
                Vector3 moon = SkyMath.MoonDirection(t, MoonTilt, MoonAzimuth, MoonLead);
                Assert.AreEqual(1f, moon.magnitude, 1e-4f);
                Assert.Less(Vector3.Dot(sun, moon), -0.55f, "t = " + t);
            }
            Assert.Greater(SkyMath.MoonDirection(0f, MoonTilt, MoonAzimuth, MoonLead).y, 0.3f);
            Assert.Greater(SkyMath.MoonDirection(0.75f, MoonTilt, MoonAzimuth, MoonLead).y, 0f, "already up at sunset");
            Assert.Less(SkyMath.MoonDirection(0.5f, MoonTilt, MoonAzimuth, MoonLead).y, 0f);
            Assert.AreEqual(0f, SkyMath.MoonLit(0f), 1e-6f);
            Assert.AreEqual(1f, SkyMath.MoonLit(0.5f), 1e-6f);
            Assert.AreEqual(SkyMath.MoonLit(0.3f), SkyMath.MoonLit(0.7f), 1e-5f);
        }

        [Test]
        public void MoonLight_TakesOverOnlyWhileTheSunIsDown_Continuously()
        {
            Assert.AreEqual(0f, SkyMath.MoonLightBlend(0f), 1e-6f);
            Assert.AreEqual(0f, SkyMath.MoonLightBlend(0.5f), 1e-6f);
            Assert.AreEqual(1f, SkyMath.MoonLightBlend(-0.45f), 1e-6f);
            float prev = SkyMath.MoonLightBlend(SkyMath.SunDirection(0f, AxisTilt, Azimuth).y);
            for (float t = Step; t <= 1f; t += Step)
            {
                float v = SkyMath.MoonLightBlend(SkyMath.SunDirection(t, AxisTilt, Azimuth).y);
                Assert.Less(Mathf.Abs(v - prev), 0.08f, "t = " + t);
                prev = v;
            }
        }

        [Test]
        public void Apparent_IsTheIdentityOnAFlatWorld_AndCountsElevationFromTheLimb()
        {
            Vector3 dir = new Vector3(0.3f, 0.2f, 0.9f).normalized;
            Assert.Less((SkyMath.Apparent(dir, Vector3.down, 0f) - dir).magnitude, 1e-5f);

            // Planet of angular radius 50 degrees straight below: the limb is 40 degrees under eye level.
            float cosLimb = Mathf.Cos(50f * Mathf.Deg2Rad);
            Vector3 horizon = SkyMath.Apparent(Vector3.forward, Vector3.down, cosLimb);
            Assert.AreEqual(1f, horizon.magnitude, 1e-4f);
            Assert.AreEqual(cosLimb, Vector3.Dot(horizon, Vector3.down), 1e-4f, "a body on the true horizon sits on the limb");
            Vector3 low = SkyMath.Apparent(new Vector3(0f, Mathf.Sin(0.2f), Mathf.Cos(0.2f)), Vector3.down, cosLimb);
            Assert.Less(Vector3.Dot(low, Vector3.down), cosLimb, "above the limb");
            Assert.AreEqual(0.2f, Mathf.Acos(Vector3.Dot(low, Vector3.down)) - Mathf.Acos(cosLimb), 1e-3f, "by its true elevation");
            Assert.Greater(low.z, 0f, "same azimuth");
            Assert.AreEqual(0f, low.x, 1e-5f);
            Vector3 below = SkyMath.Apparent(new Vector3(0f, -Mathf.Sin(0.2f), Mathf.Cos(0.2f)), Vector3.down, cosLimb);
            Assert.Greater(Vector3.Dot(below, Vector3.down), cosLimb, "a set sun is behind the planet");

            // Camera behind and above its focus: the centre lies ahead and below, the limb is lower ahead than behind.
            Vector3 tilted = new Vector3(0f, -0.94f, 0.342f).normalized;
            foreach (float az in new[] { 0f, 1f, 2.5f, 4f })
            {
                var level = new Vector3(Mathf.Sin(az), 0f, Mathf.Cos(az));
                Vector3 onLimb = SkyMath.Apparent(level, tilted, cosLimb);
                Assert.AreEqual(cosLimb, Vector3.Dot(onLimb, tilted), 1e-3f, "true horizon -> limb at azimuth " + az);
                Assert.AreEqual(0f, Vector3.Cross(new Vector3(onLimb.x, 0f, onLimb.z).normalized, level).magnitude, 1e-3f);
                Vector3 up15 = SkyMath.Apparent(Quaternion.AngleAxis(-15f, Vector3.Cross(Vector3.up, level)) * level, tilted, cosLimb);
                Assert.Less(Vector3.Dot(up15, tilted), cosLimb - 0.05f, "15 degrees up stays clear of the limb at azimuth " + az);
            }

            // Continuous along the sun's path.
            Vector3 prev = SkyMath.Apparent(SkyMath.SunDirection(0f, AxisTilt, Azimuth), Vector3.down, cosLimb);
            for (float t = Step; t <= 1f; t += Step)
            {
                Vector3 now = SkyMath.Apparent(SkyMath.SunDirection(t, AxisTilt, Azimuth), Vector3.down, cosLimb);
                Assert.Less((now - prev).magnitude, 0.03f, "t = " + t);
                prev = now;
            }
        }

        [Test]
        public void StarField_TurnsWithTheSun()
        {
            Vector3 axis = SkyMath.CelestialAxis(AxisTilt, Azimuth);
            Assert.AreEqual(1f, axis.magnitude, 1e-4f);
            Vector3 sun0 = SkyMath.SunDirection(0f, AxisTilt, Azimuth);
            Assert.AreEqual(0f, Vector3.Dot(axis, sun0), 1e-4f);
            for (int i = 1; i < 20; i++)
            {
                float t = i / 20f;
                Vector3 turned = Quaternion.AngleAxis(t * 360f, axis) * sun0;
                Assert.Less((turned - SkyMath.SunDirection(t, AxisTilt, Azimuth)).magnitude, 1e-3f, "t = " + t);
            }
        }

        [Test]
        public void ShootingStar_IsRareShortAndAboveTheHorizon()
        {
            int lit = 0, samples = 0;
            for (float clock = 0f; clock < 700f; clock += 0.05f)
            {
                samples++;
                if (!SkyMath.ShootingStar(clock, 7f, 0.6f, 0.9f, 9f, out Vector3 head, out Vector3 tail, out float intensity)) continue;
                lit++;
                Assert.AreEqual(1f, head.magnitude, 1e-3f);
                Assert.AreEqual(1f, tail.magnitude, 1e-3f);
                Assert.Greater(tail.y, 0f);
                Assert.LessOrEqual(head.y, tail.y + 1e-4f, "falls");
                Assert.LessOrEqual(intensity, 1f);
                Assert.Less(Vector3.Angle(head, tail), 12f);
            }
            Assert.Greater(lit, 0);
            Assert.Less(lit / (float)samples, 0.12f);
            Assert.IsFalse(SkyMath.ShootingStar(3f, 0f, 1f, 1f, 9f, out _, out _, out _));
        }

        [Test]
        public void Storm_GreysAndDarkensButKeepsAlphaStrengthsSane()
        {
            var palette = new SkyPalette();
            var tint = new Color(0.6f, 0.62f, 0.68f);
            SkyKey clear = palette.Evaluate(0.75f);
            SkyKey storm = SkyMath.ApplyStorm(clear, 1f, 0.75f, tint, tint);
            Assert.Less(SkyMath.Luminance(storm.horizon), SkyMath.Luminance(clear.horizon));
            Assert.Less(storm.horizon.r - storm.horizon.b, clear.horizon.r - clear.horizon.b);
            Assert.Less(storm.sunGlow.a, clear.sunGlow.a);
            Assert.Less(SkyMath.Luminance(storm.cloudDark), SkyMath.Luminance(clear.cloudDark));
            SkyKey none = SkyMath.ApplyStorm(clear, 0f, 0.75f, tint, tint);
            Assert.AreEqual(clear.horizon, none.horizon);
        }
    }
}
