using System;
using Drift.Core;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Owner 2026-09-25: "Die Insel ist nachts sehr dunkel ... hätte ich gerne nachts noch einen Mond, der scheint."
    // The pure parts of the moonlit night (SkyMath); DayNightCycle composes them.
    public class NightTests
    {
        const float MoonTilt = 52f, MoonAzimuth = 65f, MoonLead = 0.3f;

        [Test]
        public void YawElevation_RoundTrips()
        {
            for (int i = 0; i < 24; i++)
            {
                float yaw = -180f + i * 15f + 3f;
                float el = (i % 7 - 3) * 0.2f;
                Vector3 d = SkyMath.FromYawElevation(yaw, el);
                Assert.AreEqual(1f, d.magnitude, 1e-4f);
                Assert.AreEqual(0f, Mathf.DeltaAngle(yaw, SkyMath.YawOf(d)), 1e-3f);
                Assert.AreEqual(el, Mathf.Asin(d.y), 1e-4f);
            }
            Assert.AreEqual(0f, SkyMath.YawOf(Vector3.forward), 1e-4f);
            Assert.AreEqual(90f, SkyMath.YawOf(Vector3.right), 1e-4f);
        }

        [Test]
        public void MoonAt_KeepsTheOrbitsRiseAndSet_ButTakesTheBearing()
        {
            for (float t = 0f; t < 1f; t += 0.01f)
            {
                Vector3 orbit = SkyMath.MoonDirection(t, MoonTilt, MoonAzimuth, MoonLead);
                Vector3 m = SkyMath.MoonAt(orbit, 123f);
                Assert.AreEqual(orbit.y, m.y, 1e-4f, "same elevation at t " + t);
                if (Mathf.Abs(m.y) < 0.999f) Assert.AreEqual(0f, Mathf.DeltaAngle(123f, SkyMath.YawOf(m)), 0.01f);
            }
        }

        [Test]
        public void CapElevation_LowersOnlyAMoonAboveTheCap_KeepingItsBearing()
        {
            Vector3 high = SkyMath.FromYawElevation(30f, 40f * Mathf.Deg2Rad);
            Vector3 c = SkyMath.CapElevation(high, 25f);
            Assert.AreEqual(25f, Mathf.Asin(c.y) * Mathf.Rad2Deg, 0.01f);
            Assert.AreEqual(30f, SkyMath.YawOf(c), 0.01f);
            Vector3 low = SkyMath.FromYawElevation(30f, 10f * Mathf.Deg2Rad);
            Assert.AreEqual(low, SkyMath.CapElevation(low, 25f));
        }

        [Test]
        public void MoonKeyLight_IsNeverFlat_AndFollowsTheMoonsBearing()
        {
            Vector3 low = SkyMath.FromYawElevation(40f, 10f * Mathf.Deg2Rad);
            Vector3 k = SkyMath.MoonKeyLight(low, 50f, 0f);
            Assert.AreEqual(50f, Mathf.Asin(k.y) * Mathf.Rad2Deg, 0.01f, "a low moon lights from 50 degrees up");
            Assert.AreEqual(40f, SkyMath.YawOf(k), 0.01f);
            Vector3 side = SkyMath.MoonKeyLight(low, 50f, 90f);
            Assert.AreEqual(130f, SkyMath.YawOf(side), 0.01f);
            Vector3 high = SkyMath.FromYawElevation(40f, 70f * Mathf.Deg2Rad);
            Assert.AreEqual(70f, Mathf.Asin(SkyMath.MoonKeyLight(high, 50f, 0f).y) * Mathf.Rad2Deg, 0.01f, "a high moon keeps its height");
        }

        [Test]
        public void LiftAmbient_RaisesOnlyAtNight_NeverDarkens_KeepsAlpha()
        {
            var day = new Color(0.55f, 0.62f, 0.72f, 1f);
            var night = new Color(0.30f, 0.34f, 0.48f, 1f);
            var floor = new Color(0.42f, 0.48f, 0.62f, 1f);
            Assert.AreEqual(day, SkyMath.LiftAmbient(day, floor, 0f));
            Assert.AreEqual(day, SkyMath.LiftAmbient(day, floor, 1f), "a brighter ambient is kept");
            Color lifted = SkyMath.LiftAmbient(night, floor, 1f);
            Assert.AreEqual(floor.r, lifted.r, 1e-5f);
            Assert.AreEqual(floor.b, lifted.b, 1e-5f);
            Assert.AreEqual(1f, lifted.a, "alpha stays 1 (an alpha of 2 once showed up as a scene change on every save)");
            Color half = SkyMath.LiftAmbient(night, floor, 0.5f);
            Assert.Greater(half.g, night.g);
            Assert.Less(half.g, floor.g);
        }

        [Test]
        public void MoonPhase_StaysBright_AndOnlyStartsOverWhileTheMoonIsDown()
        {
            const float min = 0.3f, max = 0.7f;
            float p = 0.5f;
            for (int i = 0; i < 5000; i++)
            {
                bool down = (i / 400) % 2 == 1;
                float next = SkyMath.AdvanceMoonPhase(p, 0.0007f, min, max, down);
                Assert.That(next, Is.InRange(min, max));
                if (next < p) Assert.IsTrue(down, "the phase only starts over while the moon is down");
                p = next;
            }
            Assert.GreaterOrEqual(SkyMath.MoonLit(min), 0.65f, "at least a thick half moon");
            // An old save's thin crescent is pulled into the range.
            Assert.AreEqual(min, SkyMath.AdvanceMoonPhase(0.05f, 0.001f, min, max, true));
            Assert.AreEqual(min, SkyMath.AdvanceMoonPhase(0.05f, 0.001f, min, max, false));
            Assert.AreEqual(0.1f, SkyMath.AdvanceMoonPhase(0.95f, 0.15f, 0f, 0f, false), 1e-5f, "no range: the old full cycle");
        }

        // Reviewer 2026-09-25: "Gefahren nachts kaum lesbar". Drift.Visuals.NightReadability lives next to DayNightCycle;
        // this assembly does not reference Drift.Visuals, so it is reached by reflection.
        static readonly Type Readability = Type.GetType("Drift.Visuals.NightReadability, Drift.Visuals");

        static T Call<T>(string name, params object[] args)
        {
            Assert.IsNotNull(Readability, "Drift.Visuals.NightReadability");
            return (T)Readability.GetMethod(name).Invoke(null, args);
        }

        [Test]
        public void ShoreRim_IsInvisibleByDay_OffInCozy_FullAtAnAdventureNight()
        {
            Assert.AreEqual(0f, Call<float>("ShoreRimStrength", 0f, true, 1f, 0f), "day");
            Assert.AreEqual(0f, Call<float>("ShoreRimStrength", 1f, false, 1f, 0f), "cozy");
            Assert.AreEqual(1f, Call<float>("ShoreRimStrength", 1f, true, 1f, 0f), 1e-6f);
            Assert.AreEqual(0.3f, Call<float>("ShoreRimStrength", 0.5f, false, 1f, 0.6f), 1e-6f, "a cozy share scales too");
            Assert.AreEqual(1f, Call<float>("ShoreRimStrength", 2f, true, 3f, 0f), 1e-6f, "clamped");
        }

        [Test]
        public void ShoreRimShape_OnlyAlongTheWaterline_BrightestEdgeOn()
        {
            Assert.AreEqual(0f, Call<float>("ShoreRimShape", -0.5f, 0f), "cut-away sea floor");
            Assert.AreEqual(0f, Call<float>("ShoreRimShape", 0.8f, 0f), "grass and hills stay unlit");
            float atSea = Call<float>("ShoreRimShape", 0f, 1f);
            Assert.Greater(atSea, 0.2f, "a faint band where the shore faces the camera");
            Assert.Greater(Call<float>("ShoreRimShape", 0f, 0f), atSea, "stronger where the shore turns edge-on");
            Assert.AreEqual(1f, Call<float>("ShoreRimShape", 0f, 0f), 1e-5f);
            Assert.Greater(atSea, Call<float>("ShoreRimShape", 0.5f, 1f), "fades up the beach");
        }

        [Test]
        public void StormPuffLift_RaisesOnlyAtNight_NeverDarkens()
        {
            var col = new Color(0.04f, 0.05f, 0.3f, 0.7f);
            var floor = new Color(0.12f, 0.14f, 0.21f, 1f);
            Assert.AreEqual(col, Call<Color>("StormPuffLift", col, floor, 0f), "the day look is unchanged");
            Color n = Call<Color>("StormPuffLift", col, floor, 1f);
            Assert.AreEqual(floor.r, n.r, 1e-6f);
            Assert.AreEqual(floor.g, n.g, 1e-6f);
            Assert.AreEqual(col.b, n.b, 1e-6f, "a brighter channel is kept");
            Assert.AreEqual(col.a, n.a, "coverage untouched");
        }

        [Test]
        public void RainSideFade_IsSoftAtTheShaftsOutline()
        {
            Assert.AreEqual(0f, Call<float>("RainSideFade", 0f, 0.15f, 0.7f), "edge-on wall: no hard box outline");
            Assert.AreEqual(1f, Call<float>("RainSideFade", 1f, 0.15f, 0.7f));
            Assert.AreEqual(1f, Call<float>("RainSideFade", -0.9f, 0.15f, 0.7f), "the back wall counts like the front");
            float prev = 0f;
            for (int i = 0; i <= 20; i++)
            {
                float f = Call<float>("RainSideFade", i / 20f, 0.15f, 0.7f);
                Assert.GreaterOrEqual(f, prev);
                prev = f;
            }
        }
    }
}
