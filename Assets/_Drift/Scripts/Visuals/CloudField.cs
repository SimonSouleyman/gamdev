using UnityEngine;

namespace Drift.Visuals
{
    // C# mirror of the cloud clumps in Shaders/DriftClouds.hlsl (DriftHash, DriftCloudClump, CloudDensity): gameplay
    // and tests can ask where the clouds are without reading the GPU. Keep the two in sync.
    public static class CloudField
    {
        public struct Clump
        {
            public Vector2 centre;   // cloud space (cells)
            public float radius;     // cells, 0 = no clump
            public Vector2 axis;
            public float aspect;
        }

        static float Frac(float x) => x - Mathf.Floor(x);

        public static float Hash(Vector2 p)
        {
            float x = Frac(p.x * 123.34f), y = Frac(p.y * 456.21f);
            float d = x * (x + 45.32f) + y * (y + 45.32f);
            x += d; y += d;
            return Frac(x * y);
        }

        public static Clump ClumpAt(Vector2 id, float cover)
        {
            float h1 = Hash(id);
            float h2 = Hash(id + new Vector2(17.31f, 5.17f));
            float h3 = Hash(id + new Vector2(3.71f, 41.9f));
            float h4 = Hash(id + new Vector2(29.3f, 11.1f));
            float share = Mathf.Clamp01(cover * 1.9f);
            float grow = Mathf.Clamp01((share - h1) * 6f);
            float a = (h2 + h4) * 3.14159f;
            return new Clump
            {
                radius = (0.16f + 0.12f * h2) * grow,
                centre = id + new Vector2(0.3f + 0.4f * h3, 0.3f + 0.4f * h4),
                axis = new Vector2(Mathf.Cos(a), Mathf.Sin(a)),
                aspect = 1f + 0.5f * h3,
            };
        }

        public static float ClumpDensity(Vector2 q, Vector2 id, float cover)
        {
            var c = ClumpAt(id, cover);
            if (c.radius < 1e-4f) return 0f;
            Vector2 d = q - c.centre;
            float lx = Vector2.Dot(d, c.axis) / c.aspect;
            float ly = Vector2.Dot(d, new Vector2(-c.axis.y, c.axis.x));
            float len2 = lx * lx + ly * ly;
            float len = Mathf.Sqrt(len2);
            float c2 = lx * lx / Mathf.Max(len2, 1e-8f);
            float t6 = ((32f * c2 - 48f) * c2 + 18f) * c2 - 1f;
            float edge = c.radius * (0.95f + 0.05f * t6);
            return 1f - SmoothStep(edge * 0.35f, edge * 1.05f, len);
        }

        // Cloud-space density at q (1 = under a cloud), from the 2x2 nearest cells like the shader.
        public static float Density(Vector2 q, float cover)
        {
            if (cover < 0.001f) return 0f;
            Vector2 b = new Vector2(Mathf.Floor(q.x - 0.5f), Mathf.Floor(q.y - 0.5f));
            float d = 0f;
            for (int j = 0; j < 4; j++)
                d = Mathf.Max(d, ClumpDensity(q, b + new Vector2(j & 1, j >> 1), Mathf.Clamp01(cover)));
            return d;
        }

        // The same from world XZ with the CloudShadows state (scale = 1 / cell size, offset in cells).
        public static float DensityAtWorld(Vector2 wp, float cover, float scale, Vector2 offset, Vector2 shift)
        {
            return Density((wp + shift) * scale + offset, cover);
        }

        // Puff k (0..8) of a clump of world radius r: mirror of DriftPuffLayout in Shaders/DriftCloudPuff.hlsl.
        public static void PuffLayout(int k, float seed, Vector2 axis, float aspect, float r, out Vector2 offset, out float size, out float height)
        {
            float h = Hash(new Vector2(seed, k * 7.13f + 1.7f));
            Vector2 side = new Vector2(-axis.y, axis.x);
            if (k < 6)
            {
                float h2 = Frac(h * 7.31f);
                float a = k * 1.0471976f + (h - 0.5f) * 0.9f;
                float l = 0.44f + 0.2f * h2;
                offset = (axis * (Mathf.Cos(a) * aspect * l) + side * (Mathf.Sin(a) * l)) * r;
                size = (0.28f + 0.24f * Frac(h * 13.7f)) * r;
                height = (0.02f + 0.16f * h) * r;
            }
            else if (k == 6)
            {
                offset = Vector2.zero;
                size = 0.56f * r;
                height = 0.3f * r;
            }
            else
            {
                float s = k == 7 ? -1f : 1f;
                offset = axis * (s * (0.22f + 0.1f * h) * aspect * r) + side * ((h - 0.5f) * 0.2f * r);
                size = (0.36f + 0.1f * h) * r;
                height = (0.45f + 0.12f * h) * r;
            }
        }

        static float SmoothStep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }
}
