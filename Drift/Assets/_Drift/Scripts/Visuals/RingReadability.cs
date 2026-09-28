using UnityEngine;

namespace Drift.Visuals
{
    // Pure rules that keep the picture readable: the cloud-free stretch of the adventure track, the storm puff budget
    // on phones, how close to the camera rain may fall, and how bright a pickup's beacon burns. The shaders mirror the
    // parts they need (Clouds.shader RingCorridor, Storm.shader _DriftRainNear, FlotsamBeacon.shader); tests use these.
    public static class RingReadability
    {
        public enum LiteMode { Auto, An, Aus }

        // Auto: phones, and the Editor while it renders with the phone pipeline asset (play tests switch to it).
        public static bool IsLite(LiteMode mode, bool mobilePlatform, string pipelineName)
        {
            if (mode == LiteMode.An) return true;
            if (mode == LiteMode.Aus) return false;
            return mobilePlatform || (pipelineName != null && pipelineName.IndexOf("Mobile", System.StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ------------------------------------------------------------ clouds over the adventure track

        // Mirror of RingCorridor in Clouds.shader. across = puff x minus the player's x, ahead = distance along the
        // track in the direction the camera looks (negative = behind the player). clear = CloudShadows.RingClear
        // (x = clear half width, y = its fade, z = clear distance ahead, w = its fade; z = 0 = off).
        // 0 = the puff hangs over the track in front of the player and is hidden, 1 = scenery.
        public const float BehindStart = -25f, BehindEnd = -40f;

        public static float CloudCorridor(float across, float ahead, Vector4 clear)
        {
            if (clear.z <= 0f) return 1f;
            float side = SmoothStep(clear.x, clear.x + Mathf.Max(clear.y, 1e-3f), Mathf.Abs(across));
            float along = SmoothStep(clear.z, clear.z + Mathf.Max(clear.w, 1e-3f), ahead)
                        + (1f - SmoothStep(BehindEnd, BehindStart, ahead));
            return Mathf.Clamp01(Mathf.Max(side, along));
        }

        // ------------------------------------------------------------ storm puffs

        // Clumps per storm: the lite path caps them.
        public static int StormClumps(int clumps, bool lite, int liteClumps) =>
            Mathf.Max(1, lite ? Mathf.Min(clumps, Mathf.Max(1, liteClumps)) : clumps);

        // Puff k of a clump (CloudField.PuffLayout order: 0..5 the ring, 6 the centre, 7..8 the tops). The lite path
        // keeps every other ring puff and lets the kept ones grow to close the gaps.
        public static bool KeepStormPuff(int k, bool lite) => !lite || k >= 6 || (k & 1) == 0;
        public static float StormPuffGrow(int k, bool lite) => lite && k < 6 ? 1.3f : 1f;

        public static int StormPuffsPerClump(bool lite)
        {
            int n = 0;
            for (int k = 0; k < CloudShadows.PuffsPerClump; k++) if (KeepStormPuff(k, lite)) n++;
            return n;
        }

        // ------------------------------------------------------------ rain near the camera

        // _DriftRainNear for Storm.shader: x..y = the rain fades in with the distance to the camera, z = share of the
        // streak lanes still falling right in front of the camera, w = distance at which every lane is back.
        public static Vector4 RainNear(Vector2 fade, float nearShare, float fullAt)
        {
            float x = Mathf.Max(0f, fade.x), y = Mathf.Max(x + 0.5f, fade.y);
            // The shader keeps a lane when its hash is above the threshold: 0.35 = every lane of the normal rain.
            float threshold = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(nearShare));
            return new Vector4(x, y, threshold, Mathf.Max(y, fullAt));
        }

        // Share of the rain alpha left at a distance from the camera (the fade in Storm.shader).
        public static float RainVisible(Vector4 near, float camDistance) => SmoothStep(near.x, near.y, camDistance);

        // ------------------------------------------------------------ pickups

        // How bright the beacon of a pickup burns: its fade-in, gone while it is being collected, full at night and a
        // share of that by day (the ring on the water still reads on a bright sea).
        public static float BeaconStrength(float fade, float collect, float night, float dayShare)
        {
            float k = Mathf.Clamp01(fade);
            if (collect >= 0f) k *= 1f - SmoothStep(0f, 0.6f, collect);
            return k * Mathf.Lerp(Mathf.Clamp01(dayShare), 1f, Mathf.Clamp01(night));
        }

        static float SmoothStep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }
}
