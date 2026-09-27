using System;
using UnityEngine;

namespace Drift.Core
{
    // One row of the sky palette. Colours are authored in sRGB; alpha of sunGlow / antiGlow is the strength of
    // the warm tint on the sun's side and of the violet band opposite it.
    [Serializable]
    public struct SkyKey
    {
        [Range(0f, 1f)] public float time;
        public Color zenith;
        public Color horizon;
        public Color sunGlow;
        public Color antiGlow;
        public Color sunDisc;
        public Color cloudLit;
        public Color cloudDark;
        public Color ambient;
        public Color waterDeep;

        public SkyKey(float time, Color zenith, Color horizon, Color sunGlow, Color antiGlow, Color sunDisc,
            Color cloudLit, Color cloudDark, Color ambient, Color waterDeep)
        {
            this.time = time;
            this.zenith = zenith;
            this.horizon = horizon;
            this.sunGlow = sunGlow;
            this.antiGlow = antiGlow;
            this.sunDisc = sunDisc;
            this.cloudLit = cloudLit;
            this.cloudDark = cloudDark;
            this.ambient = ambient;
            this.waterDeep = waterDeep;
        }

        public static SkyKey Lerp(in SkyKey a, in SkyKey b, float u)
        {
            return new SkyKey(Mathf.Lerp(a.time, b.time, u),
                Color.LerpUnclamped(a.zenith, b.zenith, u), Color.LerpUnclamped(a.horizon, b.horizon, u),
                Color.LerpUnclamped(a.sunGlow, b.sunGlow, u), Color.LerpUnclamped(a.antiGlow, b.antiGlow, u),
                Color.LerpUnclamped(a.sunDisc, b.sunDisc, u), Color.LerpUnclamped(a.cloudLit, b.cloudLit, u),
                Color.LerpUnclamped(a.cloudDark, b.cloudDark, u), Color.LerpUnclamped(a.ambient, b.ambient, u),
                Color.LerpUnclamped(a.waterDeep, b.waterDeep, u));
        }
    }

    // The shared sky palette: every colour the sky, the horizon haze, the water tints and the flat ambient take
    // over a day, as keys on a cyclic time axis (0 = midnight, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset).
    // Evaluate() interpolates between the two neighbouring keys (wrapping 1 -> 0), so nothing can pop.
    [Serializable]
    public class SkyPalette
    {
        // Keys must be sorted by time. 0 = straight lines between keys, 1 = smoothstep (colours rest on each key).
        public SkyKey[] keys = DefaultKeys();
        [Range(0f, 1f)] public float easing = 0.5f;

        static Color C(float r, float g, float b, float a = 1f) => new Color(r, g, b, a);

        public static SkyKey[] DefaultKeys()
        {
            var night = new SkyKey(0f, C(0.035f, 0.05f, 0.14f), C(0.10f, 0.14f, 0.29f), C(0.35f, 0.3f, 0.55f, 0f), C(0.25f, 0.22f, 0.45f, 0f),
                C(0.9f, 0.2f, 0.1f), C(0.17f, 0.21f, 0.36f), C(0.04f, 0.05f, 0.12f), C(0.30f, 0.34f, 0.48f), C(0.04f, 0.10f, 0.22f));
            var day = new SkyKey(0f, C(0.09f, 0.37f, 0.83f), C(0.64f, 0.82f, 0.98f), C(1f, 0.96f, 0.85f, 0.35f), C(0.8f, 0.85f, 1f, 0f),
                C(1f, 0.98f, 0.9f), C(1f, 1f, 1f), C(0.62f, 0.69f, 0.82f), C(0.55f, 0.62f, 0.72f), C(0.05f, 0.24f, 0.45f));
            SkyKey At(SkyKey k, float t) { k.time = t; return k; }
            var noon = At(day, 0.5f);
            noon.zenith = C(0.06f, 0.34f, 0.82f);
            noon.horizon = C(0.62f, 0.81f, 0.98f);

            return new[]
            {
                At(night, 0f),
                At(night, 0.17f),
                // First light: the sun is still 10 degrees down, the horizon on its side starts to glow.
                new SkyKey(0.215f, C(0.06f, 0.08f, 0.22f), C(0.36f, 0.28f, 0.46f), C(0.95f, 0.45f, 0.40f, 0.6f), C(0.35f, 0.3f, 0.55f, 0.2f),
                    C(1f, 0.3f, 0.15f), C(0.75f, 0.45f, 0.5f), C(0.07f, 0.07f, 0.17f), C(0.36f, 0.35f, 0.5f), C(0.05f, 0.10f, 0.24f)),
                // Sunrise: rosier and cooler than the sunset.
                new SkyKey(0.25f, C(0.20f, 0.30f, 0.60f), C(0.98f, 0.68f, 0.58f), C(1f, 0.55f, 0.30f, 1f), C(0.70f, 0.48f, 0.74f, 0.8f),
                    C(1f, 0.62f, 0.3f), C(1f, 0.70f, 0.62f), C(0.34f, 0.27f, 0.45f), C(0.55f, 0.44f, 0.46f), C(0.09f, 0.20f, 0.40f)),
                new SkyKey(0.29f, C(0.22f, 0.44f, 0.82f), C(0.92f, 0.84f, 0.76f), C(1f, 0.8f, 0.5f, 0.6f), C(0.8f, 0.7f, 0.85f, 0.3f),
                    C(1f, 0.93f, 0.7f), C(1f, 0.93f, 0.85f), C(0.55f, 0.58f, 0.72f), C(0.56f, 0.56f, 0.62f), C(0.06f, 0.23f, 0.44f)),
                At(day, 0.36f),
                noon,
                At(day, 0.64f),
                new SkyKey(0.71f, C(0.22f, 0.40f, 0.78f), C(1f, 0.80f, 0.58f), C(1f, 0.65f, 0.3f, 0.7f), C(0.85f, 0.65f, 0.8f, 0.35f),
                    C(1f, 0.88f, 0.6f), C(1f, 0.86f, 0.68f), C(0.5f, 0.47f, 0.62f), C(0.58f, 0.52f, 0.52f), C(0.07f, 0.22f, 0.43f)),
                // Sunset: orange at the horizon, violet opposite the sun.
                new SkyKey(0.75f, C(0.17f, 0.22f, 0.52f), C(1f, 0.58f, 0.36f), C(1f, 0.4f, 0.12f, 1f), C(0.66f, 0.40f, 0.70f, 0.9f),
                    C(1f, 0.5f, 0.2f), C(1f, 0.55f, 0.45f), C(0.28f, 0.2f, 0.4f), C(0.55f, 0.42f, 0.42f), C(0.10f, 0.20f, 0.40f)),
                new SkyKey(0.785f, C(0.08f, 0.10f, 0.28f), C(0.55f, 0.33f, 0.47f), C(0.95f, 0.35f, 0.25f, 0.8f), C(0.3f, 0.25f, 0.5f, 0.3f),
                    C(0.9f, 0.2f, 0.1f), C(0.7f, 0.35f, 0.45f), C(0.08f, 0.07f, 0.18f), C(0.4f, 0.35f, 0.48f), C(0.06f, 0.13f, 0.30f)),
                // Twilight: deep blue with a last violet glow where the sun went down.
                new SkyKey(0.83f, C(0.045f, 0.06f, 0.17f), C(0.17f, 0.19f, 0.40f), C(0.5f, 0.3f, 0.55f, 0.4f), C(0.25f, 0.22f, 0.45f, 0f),
                    C(0.9f, 0.2f, 0.1f), C(0.32f, 0.34f, 0.55f), C(0.035f, 0.045f, 0.12f), C(0.32f, 0.34f, 0.48f), C(0.04f, 0.10f, 0.23f)),
                At(night, 0.88f),
            };
        }

        public SkyKey Evaluate(float timeOfDay)
        {
            var k = keys;
            if (k == null || k.Length == 0) k = keys = DefaultKeys();
            int n = k.Length;
            if (n == 1) return k[0];
            float t = Mathf.Repeat(timeOfDay, 1f);
            int hi = 0;
            while (hi < n && k[hi].time <= t) hi++;
            int lo = hi - 1;
            float t0, t1;
            if (lo < 0) { lo = n - 1; t0 = k[lo].time - 1f; } else t0 = k[lo].time;
            if (hi >= n) { hi = 0; t1 = k[0].time + 1f; } else t1 = k[hi].time;
            float span = t1 - t0;
            float u = span > 1e-6f ? Mathf.Clamp01((t - t0) / span) : 0f;
            u = Mathf.Lerp(u, u * u * (3f - 2f * u), easing);
            var r = SkyKey.Lerp(k[lo], k[hi], u);
            r.time = t;
            return r;
        }
    }

    public static class SkyMath
    {
        // Unit vector towards the sun: rises at 0.25 (east of the tilted orbit), culminates at 0.5, sets at 0.75.
        public static Vector3 SunDirection(float timeOfDay, float axisTiltDeg, float azimuthDeg)
        {
            return Orbit((timeOfDay - 0.25f) * Mathf.PI * 2f, axisTiltDeg, azimuthDeg);
        }

        // The moon runs roughly opposite the sun (lead > 0: it is already up when the sun sets) on its own,
        // usually flatter, orbit so it stays near the horizon where the camera can see it.
        public static Vector3 MoonDirection(float timeOfDay, float tiltDeg, float azimuthDeg, float leadRad)
        {
            return Orbit((timeOfDay - 0.25f) * Mathf.PI * 2f + Mathf.PI + leadRad, tiltDeg, azimuthDeg);
        }

        public static Vector3 Orbit(float angle, float tiltDeg, float azimuthDeg)
        {
            float tilt = tiltDeg * Mathf.Deg2Rad, az = azimuthDeg * Mathf.Deg2Rad;
            float x = Mathf.Cos(angle), s = Mathf.Sin(angle);
            float y = s * Mathf.Cos(tilt), z = s * Mathf.Sin(tilt);
            float ca = Mathf.Cos(az), sa = Mathf.Sin(az);
            return new Vector3(x * ca + z * sa, y, -x * sa + z * ca);
        }

        // Normal of the sun's orbit: the axis the star field turns around.
        public static Vector3 CelestialAxis(float axisTiltDeg, float azimuthDeg)
        {
            float tilt = axisTiltDeg * Mathf.Deg2Rad, az = azimuthDeg * Mathf.Deg2Rad;
            float y = -Mathf.Sin(tilt), z = Mathf.Cos(tilt);
            return new Vector3(z * Mathf.Sin(az), y, z * Mathf.Cos(az));
        }

        // 0 until the sun is 5 degrees below the horizon, 1 once it is 20 degrees down; depends on the sun height only, so it
        // is monotonic through dusk and dawn.
        public static float StarVisibility(float sunHeight)
        {
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.34f, -0.08f, sunHeight));
        }

        public static float SunDiscVisibility(float sunHeight)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.14f, -0.03f, sunHeight));
        }

        // The disc swells a little towards the horizon.
        public static float SunDiscScale(float sunHeight, float horizonScale)
        {
            return Mathf.Lerp(horizonScale, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.4f, sunHeight)));
        }

        // 0 = the sun lights the world, 1 = the moon does; the light swings over while it is dimmest.
        public static float MoonLightBlend(float sunHeight)
        {
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.4f, 0f, sunHeight));
        }

        // Lit fraction of the moon's disc for phase 0..1 (0 = new, 0.5 = full).
        public static float MoonLit(float phase)
        {
            return 0.5f - 0.5f * Mathf.Cos(phase * Mathf.PI * 2f);
        }

        // Where a celestial direction is drawn for a camera that sees the planet's limb below eye level: the
        // curved world's horizon is the limb (cosLimb = cosine of the planet's angular radius around centreDir),
        // so the body keeps its azimuth and its elevation counts from the limb in that azimuth. A body on the true
        // horizon sits exactly on the limb, a set one is behind the planet. Identity for the flat world (centre
        // straight down, cosLimb 0).
        public static Vector3 Apparent(Vector3 dir, Vector3 centreDir, float cosLimb)
        {
            var flat = new Vector2(dir.x, dir.z);
            float m = flat.magnitude;
            if (m < 1e-5f) return dir;
            flat /= m;
            float a = flat.x * centreDir.x + flat.y * centreDir.z, b = centreDir.y;
            float r = Mathf.Sqrt(a * a + b * b);
            if (r < 1e-5f) return dir;
            float limb = Mathf.Atan2(b, a) + Mathf.Acos(Mathf.Clamp(cosLimb / r, -1f, 1f));
            if (Mathf.Abs(limb) < 1e-5f) return dir;
            float el = Mathf.Min(Mathf.PI * 0.5f, Mathf.Atan2(dir.y, m) + limb);
            float c = Mathf.Cos(el);
            return new Vector3(flat.x * c, Mathf.Sin(el), flat.y * c);
        }

        // Degrees the limb lies below eye level straight ahead of a camera whose planet centre is centreDir.
        public static float LimbDepression(Vector3 centreDir, float cosLimb)
        {
            return 90f - Mathf.Acos(Mathf.Clamp(cosLimb, -1f, 1f)) * Mathf.Rad2Deg;
        }

        // How far the light has turned from the sun's colour to the moon's: later than NightAmount, so the last
        // sunlight is still warm while the disc touches the horizon.
        public static float LightNight(float sunHeight)
        {
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.2f, 0.03f, sunHeight));
        }

        public static float Luminance(Color c) => c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;

        // Compass yaw in degrees (0 = +z, 90 = +x) of a direction, and back from yaw + elevation (radians).
        public static float YawOf(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        public static Vector3 FromYawElevation(float yawDeg, float elevationRad)
        {
            float y = yawDeg * Mathf.Deg2Rad, c = Mathf.Cos(elevationRad);
            return new Vector3(Mathf.Sin(y) * c, Mathf.Sin(elevationRad), Mathf.Cos(y) * c);
        }

        // The moon keeps the elevation of its orbit (it rises and sets on its own arc) but stands at `yawDeg`: the
        // chase camera turns with the island's course, and a moon on a fixed compass bearing was behind it most nights.
        public static Vector3 MoonAt(Vector3 orbitDir, float yawDeg)
        {
            return FromYawElevation(yawDeg, Mathf.Asin(Mathf.Clamp(orbitDir.y, -1f, 1f)));
        }

        // The same bearing, at most maxElevationDeg up (a moon above the top of the view is lowered into it).
        public static Vector3 CapElevation(Vector3 dir, float maxElevationDeg)
        {
            float el = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f));
            float max = maxElevationDeg * Mathf.Deg2Rad;
            return el <= max ? dir : FromYawElevation(YawOf(dir), max);
        }

        // The moonlight as a key light: the moon's bearing turned by yawOffsetDeg, never lower than minElevationDeg (a
        // low moon grazes flat ground: sin 14 degrees lit it at a quarter) - the island reads best lit from above.
        public static Vector3 MoonKeyLight(Vector3 moonDir, float minElevationDeg, float yawOffsetDeg)
        {
            float el = Mathf.Max(Mathf.Asin(Mathf.Clamp(moonDir.y, -1f, 1f)), minElevationDeg * Mathf.Deg2Rad);
            return FromYawElevation(YawOf(moonDir) + yawOffsetDeg, el);
        }

        // The flat ambient at night: per channel raised to at least `floor`, as far as it is night. Alpha stays.
        public static Color LiftAmbient(Color ambient, Color floor, float night)
        {
            night = Mathf.Clamp01(night);
            return new Color(
                Mathf.Lerp(ambient.r, Mathf.Max(ambient.r, floor.r), night),
                Mathf.Lerp(ambient.g, Mathf.Max(ambient.g, floor.g), night),
                Mathf.Lerp(ambient.b, Mathf.Max(ambient.b, floor.b), night), ambient.a);
        }

        // The moon's phase moves on by `step` inside [min, max] only (a thin crescent gives no moonlight): past max it
        // waits until the moon is down and then starts over at min, so nobody sees the terminator jump.
        public static float AdvanceMoonPhase(float phase, float step, float min, float max, bool moonDown)
        {
            if (max <= min) return Mathf.Repeat(phase + step, 1f);
            if (phase < min || phase > max + 1e-4f) return moonDown ? min : Mathf.Clamp(phase, min, max);
            float next = phase + step;
            if (next <= max) return next;
            return moonDown ? min : max;
        }

        // Greys a colour and darkens it by the tint; what is dark already (the night sky) is darkened less, so a
        // night storm stays a readable indigo instead of going black.
        static Color Storm(Color c, float storm, float desaturate, Color tint)
        {
            float lum = Luminance(c);
            Color grey = new Color(lum, lum, lum, c.a);
            float w = storm * Mathf.Clamp01(0.25f + lum * 2.5f);
            Color d = Color.LerpUnclamped(c, grey, desaturate * w);
            Color m = Color.LerpUnclamped(Color.white, tint, w);
            return new Color(d.r * m.r, d.g * m.g, d.b * m.b, c.a);
        }

        // A storm greys and darkens whatever the time of day produced (so a night storm is darker than either).
        public static SkyKey ApplyStorm(SkyKey k, float storm, float desaturate, Color skyTint, Color ambientTint)
        {
            storm = Mathf.Clamp01(storm);
            if (storm <= 0f) return k;
            k.zenith = Storm(k.zenith, storm, desaturate, skyTint * 0.85f);
            k.horizon = Storm(k.horizon, storm, desaturate, skyTint);
            k.cloudLit = Storm(k.cloudLit, storm, desaturate, skyTint * 0.9f);
            k.cloudDark = Storm(k.cloudDark, storm, desaturate, skyTint * 0.6f);
            Color am = Color.LerpUnclamped(Color.white, ambientTint, storm);
            k.ambient = new Color(k.ambient.r * am.r, k.ambient.g * am.g, k.ambient.b * am.b, k.ambient.a);
            k.sunGlow.a *= 1f - 0.75f * storm;
            k.antiGlow.a *= 1f - 0.75f * storm;
            return k;
        }

        public static float Hash01(float n)
        {
            float s = Mathf.Sin(n * 127.1f + 311.7f) * 43758.5453f;
            return s - Mathf.Floor(s);
        }

        // One analytic streak: time is cut into slots of `period` seconds, a hash decides whether the slot has a
        // shooting star, where it starts and where it heads; it lives for `duration` at the start of the slot.
        // head / tail are unit directions (true sky, y up); intensity 0 = none.
        public static bool ShootingStar(float clock, float period, float chance, float duration, float lengthDeg,
            out Vector3 head, out Vector3 tail, out float intensity)
        {
            head = tail = Vector3.up;
            intensity = 0f;
            if (period <= 0f || duration <= 0f || chance <= 0f) return false;
            float slot = Mathf.Floor(clock / period);
            float local = clock - slot * period;
            if (local >= duration || Hash01(slot) > chance) return false;
            float az = Hash01(slot + 0.37f) * Mathf.PI * 2f;
            float el = Mathf.Lerp(25f, 55f, Hash01(slot + 0.71f)) * Mathf.Deg2Rad;
            var start = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
            Vector3 side = Vector3.Cross(Vector3.up, start).normalized;
            Vector3 down = Vector3.Cross(side, start);
            float slant = (Hash01(slot + 1.93f) - 0.5f) * 2.4f;
            Vector3 travel = (down + side * slant).normalized;
            float p = local / duration;
            float len = lengthDeg * Mathf.Deg2Rad;
            float reach = p * len * 2.2f;
            head = (start + travel * Mathf.Tan(reach)).normalized;
            tail = (start + travel * Mathf.Tan(Mathf.Max(0f, reach - len * Mathf.Sin(p * Mathf.PI)))).normalized;
            intensity = Mathf.Sin(p * Mathf.PI);
            return intensity > 0f;
        }
    }
}
