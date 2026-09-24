using UnityEngine;

namespace Drift.Visuals
{
    // Flat top-view fish outlines for FishSystem's cozy sea-life layer. Each vertex is (x across, y along the
    // heading, tail-wag weight) in fish lengths; roles pick the colour (FishShapes.Role*). Duplicated vertices at the
    // same spot give hard colour edges (stripes, spots, fins).
    internal sealed class FishShape
    {
        public readonly Vector3[] verts;
        public readonly byte[] roles;
        public readonly int[] tris;

        public FishShape(Vector3[] verts, byte[] roles, int[] tris)
        {
            this.verts = verts;
            this.roles = roles;
            this.tris = tris;
        }

        public int VertexCount => verts.Length;
        public int IndexCount => tris.Length;
    }

    internal static class FishShapes
    {
        public const byte Body = 0, Accent = 1, Tail = 2, Nose = 3, Back = 4, Side = 5;
        public const int RoleCount = 6;

        public const int Slim = 0, Striped = 1, Tuna = 2, Needle = 3, Disc = 4, Grouper = 5;

        // Declared before All: static initializers run in text order and the shape builders use it.
        static readonly int[] ThreeRowBody =
        {
            0, 1, 2, 0, 2, 3,
            1, 4, 5, 1, 5, 2, 2, 5, 6, 2, 6, 3,
            4, 7, 8, 4, 8, 5, 5, 8, 9, 5, 9, 6,
        };

        public static readonly FishShape[] All = { MakeSlim(), MakeStriped(), MakeTuna(), MakeNeedle(), MakeDisc(), MakeGrouper() };

        static FishShape MakeSlim()
        {
            return new FishShape(
                new[]
                {
                    new Vector3(0f, 0.5f, 0f), new Vector3(-0.17f, 0.05f, 0f), new Vector3(0.17f, 0.05f, 0f),
                    new Vector3(0f, -0.3f, 0.3f), new Vector3(0f, -0.25f, 0.25f),
                    new Vector3(-0.15f, -0.5f, 0.6f), new Vector3(0.15f, -0.5f, 0.6f),
                },
                new[] { Nose, Body, Body, Tail, Tail, Tail, Tail },
                new[] { 0, 1, 3, 0, 3, 2, 4, 5, 6 });
        }

        // Reef fish: a deep diamond body with two dark bands.
        static FishShape MakeStriped()
        {
            float Wag(float y) => Mathf.Max(0f, -y - 0.05f) * 1.1f;
            Vector3 P(float x, float y) => new Vector3(x, y, Wag(y));
            var v = new[]
            {
                new Vector3(0f, 0.5f, 0f), P(-0.2f, 0.26f), P(0.2f, 0.26f),                      // head 0-2
                P(-0.2f, 0.26f), P(0.2f, 0.26f), P(-0.24f, 0.16f), P(0.24f, 0.16f),            // band 3-6
                P(-0.24f, 0.16f), P(0.24f, 0.16f), P(-0.27f, 0.02f), P(0.27f, 0.02f),          // mid 7-12
                P(-0.24f, -0.08f), P(0.24f, -0.08f),
                P(-0.24f, -0.08f), P(0.24f, -0.08f), P(-0.17f, -0.17f), P(0.17f, -0.17f),      // band 13-16
                P(-0.17f, -0.17f), P(0.17f, -0.17f), P(-0.05f, -0.3f), P(0.05f, -0.3f),        // rear 17-20
                P(0f, -0.28f), new Vector3(-0.2f, -0.5f, 0.6f), new Vector3(0.2f, -0.5f, 0.6f), // tail 21-23
            };
            var r = new[]
            {
                Nose, Body, Body,
                Accent, Accent, Accent, Accent,
                Body, Body, Body, Body, Body, Body,
                Accent, Accent, Accent, Accent,
                Body, Body, Body, Body,
                Tail, Tail, Tail,
            };
            var t = new[]
            {
                0, 1, 2,
                3, 5, 6, 3, 6, 4,
                7, 9, 10, 7, 10, 8, 9, 11, 12, 9, 12, 10,
                13, 15, 16, 13, 16, 14,
                17, 19, 20, 17, 20, 18,
                21, 22, 23,
            };
            return new FishShape(v, r, t);
        }

        // Rows of (side, back, side) vertices: a dark back line that fades to lighter flanks.
        static void Rows(Vector3[] v, byte[] r, int at, float y, float halfWidth, float wag)
        {
            v[at] = new Vector3(-halfWidth, y, wag); r[at] = Side;
            v[at + 1] = new Vector3(0f, y, wag); r[at + 1] = Back;
            v[at + 2] = new Vector3(halfWidth, y, wag); r[at + 2] = Side;
        }

        static int[] Concat(int[] a, int[] b)
        {
            var c = new int[a.Length + b.Length];
            a.CopyTo(c, 0);
            b.CopyTo(c, a.Length);
            return c;
        }

        // Torpedo with a forked tail and yellow pectoral fins.
        static FishShape MakeTuna()
        {
            var v = new Vector3[21];
            var r = new byte[21];
            v[0] = new Vector3(0f, 0.5f, 0f); r[0] = Back;
            Rows(v, r, 1, 0.28f, 0.11f, 0f);
            Rows(v, r, 4, 0.02f, 0.15f, 0f);
            Rows(v, r, 7, -0.25f, 0.07f, 0.15f);
            v[10] = new Vector3(0f, -0.36f, 0.3f); r[10] = Back;
            v[11] = new Vector3(0f, -0.34f, 0.3f); r[11] = Tail;
            v[12] = new Vector3(-0.24f, -0.54f, 0.6f); r[12] = Tail;
            v[13] = new Vector3(0f, -0.43f, 0.45f); r[13] = Tail;
            v[14] = new Vector3(0.24f, -0.54f, 0.6f); r[14] = Tail;
            v[15] = new Vector3(-0.1f, 0.06f, 0f); v[16] = new Vector3(-0.3f, -0.08f, 0f); v[17] = new Vector3(-0.08f, -0.06f, 0f);
            v[18] = new Vector3(0.1f, 0.06f, 0f); v[19] = new Vector3(0.3f, -0.08f, 0f); v[20] = new Vector3(0.08f, -0.06f, 0f);
            for (int i = 15; i < 21; i++) r[i] = Accent;
            var t = Concat(ThreeRowBody, new[] { 7, 10, 8, 8, 10, 9, 11, 12, 13, 11, 13, 14, 15, 16, 17, 18, 19, 20 });
            return new FishShape(v, r, t);
        }

        // Needlefish: long and thin with a pointed snout.
        static FishShape MakeNeedle()
        {
            var v = new Vector3[13];
            var r = new byte[13];
            v[0] = new Vector3(0f, 0.62f, 0f); r[0] = Nose;
            Rows(v, r, 1, 0.34f, 0.045f, 0f);
            Rows(v, r, 4, 0f, 0.06f, 0.04f);
            Rows(v, r, 7, -0.32f, 0.045f, 0.12f);
            v[10] = new Vector3(0f, -0.42f, 0.22f); r[10] = Tail;
            v[11] = new Vector3(-0.09f, -0.55f, 0.4f); r[11] = Tail;
            v[12] = new Vector3(0.09f, -0.55f, 0.4f); r[12] = Tail;
            var t = Concat(ThreeRowBody, new[] { 7, 10, 8, 8, 10, 9, 10, 11, 12 });
            return new FishShape(v, r, t);
        }

        // Ocean sunfish basking on its side at the surface: a round disc with two long fins.
        static FishShape MakeDisc()
        {
            const int n = 10;
            var v = new Vector3[1 + n + 6];
            var r = new byte[v.Length];
            v[0] = new Vector3(0f, 0.02f, 0f); r[0] = Side;
            for (int k = 0; k < n; k++)
            {
                float a = k * Mathf.PI * 2f / n;
                float y = 0.02f + 0.34f * Mathf.Cos(a);
                v[1 + k] = new Vector3(0.3f * Mathf.Sin(a), y, y < -0.2f ? 0.08f : 0f);
                r[1 + k] = Body;
            }
            int f = 1 + n;
            v[f] = new Vector3(0.26f, -0.02f, 0f); v[f + 1] = new Vector3(0.64f, -0.12f, 0.3f); v[f + 2] = new Vector3(0.22f, -0.15f, 0f);
            v[f + 3] = new Vector3(-0.26f, -0.02f, 0f); v[f + 4] = new Vector3(-0.64f, -0.12f, 0.3f); v[f + 5] = new Vector3(-0.22f, -0.15f, 0f);
            for (int i = f; i < f + 6; i++) r[i] = Back;
            var t = new int[n * 3 + 6];
            for (int k = 0; k < n; k++)
            {
                t[k * 3] = 0;
                t[k * 3 + 1] = 1 + k;
                t[k * 3 + 2] = 1 + (k + 1) % n;
            }
            t[n * 3] = f; t[n * 3 + 1] = f + 1; t[n * 3 + 2] = f + 2;
            t[n * 3 + 3] = f + 3; t[n * 3 + 4] = f + 4; t[n * 3 + 5] = f + 5;
            return new FishShape(v, r, t);
        }

        // Heavy wide body, rounded tail and a few pale spots.
        static FishShape MakeGrouper()
        {
            var v = new Vector3[25];
            var r = new byte[25];
            v[0] = new Vector3(0f, 0.5f, 0f); r[0] = Body;
            Rows(v, r, 1, 0.3f, 0.19f, 0f);
            Rows(v, r, 4, 0.02f, 0.26f, 0f);
            Rows(v, r, 7, -0.22f, 0.19f, 0.1f);
            v[10] = new Vector3(-0.08f, -0.32f, 0.15f); r[10] = Body;
            v[11] = new Vector3(0.08f, -0.32f, 0.15f); r[11] = Body;
            v[12] = new Vector3(0f, -0.3f, 0.15f); r[12] = Tail;
            v[13] = new Vector3(-0.2f, -0.47f, 0.45f); r[13] = Tail;
            v[14] = new Vector3(0f, -0.53f, 0.5f); r[14] = Tail;
            v[15] = new Vector3(0.2f, -0.47f, 0.45f); r[15] = Tail;
            Vector2[] spots = { new Vector2(-0.1f, 0.12f), new Vector2(0.09f, -0.04f), new Vector2(-0.05f, -0.15f) };
            for (int s = 0; s < 3; s++)
            {
                int b = 16 + s * 3;
                Vector2 c = spots[s];
                float w = c.y < -0.1f ? 0.05f : 0f;
                v[b] = new Vector3(c.x - 0.035f, c.y - 0.025f, w);
                v[b + 1] = new Vector3(c.x + 0.035f, c.y - 0.025f, w);
                v[b + 2] = new Vector3(c.x, c.y + 0.035f, w);
                r[b] = r[b + 1] = r[b + 2] = Accent;
            }
            var t = Concat(ThreeRowBody, new[] { 7, 10, 8, 8, 10, 11, 8, 11, 9, 12, 13, 14, 12, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24 });
            return new FishShape(v, r, t);
        }
    }
}
