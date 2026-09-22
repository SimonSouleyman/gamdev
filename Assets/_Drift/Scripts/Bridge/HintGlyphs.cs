using UnityEngine;

namespace Drift.Bridge
{
    public enum HintGlyph { Sparkle, Flame, Volcano, Party, Pointer }

    // White silhouettes for the island hints, drawn once per domain like JournalGlyphs (soft signed distances in a
    // -1..1 box, y up); the Image colour tints them.
    public static class HintGlyphs
    {
        const int Size = 96;
        static readonly Sprite[] Sprites = new Sprite[System.Enum.GetValues(typeof(HintGlyph)).Length];
        static readonly Vector2[] Cone = { new Vector2(-0.86f, -0.72f), new Vector2(0.86f, -0.72f), new Vector2(0.28f, 0.18f), new Vector2(-0.28f, 0.18f) };
        static readonly Vector2[] Pennant = { new Vector2(-0.16f, 0.1f), new Vector2(0.16f, 0.1f), new Vector2(0f, -0.26f) };
        // Points right; IslandHints rotates it towards the island.
        static readonly Vector2[] Tip = { new Vector2(0.9f, 0f), new Vector2(0.2f, 0.42f), new Vector2(0.2f, -0.42f) };

        public static Sprite Of(HintGlyph glyph)
        {
            int i = (int)glyph;
            return Sprites[i] != null ? Sprites[i] : (Sprites[i] = Make(glyph));
        }

        static float Disc(Vector2 p, float x, float y, float r) => (p - new Vector2(x, y)).magnitude - r;

        static float Capsule(Vector2 p, float ax, float ay, float bx, float by, float r)
        {
            Vector2 a = new Vector2(ax, ay), ab = new Vector2(bx - ax, by - ay);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude - r;
        }

        static float Polygon(Vector2 p, Vector2[] v)
        {
            float d = float.MaxValue;
            bool inside = false;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
            {
                Vector2 ab = v[i] - v[j];
                float t = Mathf.Clamp01(Vector2.Dot(p - v[j], ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
                d = Mathf.Min(d, (p - (v[j] + ab * t)).magnitude);
                if ((v[i].y > p.y) != (v[j].y > p.y) && p.x < (v[j].x - v[i].x) * (p.y - v[i].y) / (v[j].y - v[i].y) + v[i].x) inside = !inside;
            }
            return inside ? -d : d;
        }

        // A drop: round bottom of radius r at (x, y), narrowing to a point at (tx, ty).
        static float Drop(Vector2 p, float x, float y, float r, float tx, float ty)
        {
            Vector2 a = new Vector2(x, y), ab = new Vector2(tx - x, ty - y);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude - r * (1f - t) * (1f - t * 0.35f);
        }

        // Four-point star with concave sides (|x|^k + |y|^k with k < 1).
        static float Star(Vector2 p, float x, float y, float r)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x - x), Mathf.Abs(p.y - y)) / r;
            float s = Mathf.Pow(Mathf.Pow(q.x, 0.55f) + Mathf.Pow(q.y, 0.55f), 1f / 0.55f);
            return (s - 1f) * r * 0.45f;
        }

        static float Distance(HintGlyph glyph, Vector2 p)
        {
            switch (glyph)
            {
                case HintGlyph.Sparkle:
                    return Mathf.Min(Star(p, -0.1f, -0.08f, 0.8f), Mathf.Min(Star(p, 0.58f, 0.58f, 0.34f), Disc(p, -0.62f, 0.62f, 0.1f)));
                case HintGlyph.Flame:
                {
                    // An outer tongue with a curled tip and a small inner flame cut out of it.
                    float outer = Drop(p, 0f, -0.34f, 0.52f, 0.12f, 0.9f);
                    float side = Drop(p, -0.42f, -0.28f, 0.24f, -0.52f, 0.34f);
                    float inner = Drop(p, 0.02f, -0.46f, 0.22f, 0.04f, 0.08f);
                    return Mathf.Max(Mathf.Min(outer, side), -inner);
                }
                case HintGlyph.Volcano:
                {
                    float d = Polygon(p, Cone) - 0.04f;
                    d = Mathf.Max(d, -Disc(p, 0f, 0.3f, 0.2f));
                    // Smoke puffs over the crater.
                    d = Mathf.Min(d, Disc(p, -0.08f, 0.42f, 0.13f));
                    d = Mathf.Min(d, Disc(p, 0.14f, 0.6f, 0.17f));
                    return Mathf.Min(d, Disc(p, 0.44f, 0.78f, 0.15f));
                }
                case HintGlyph.Party:
                {
                    // A garland between two posts, with pennants hanging from it.
                    float d = Mathf.Min(Capsule(p, -0.82f, -0.8f, -0.82f, 0.6f, 0.07f), Capsule(p, 0.82f, -0.8f, 0.82f, 0.6f, 0.07f));
                    float sag = 0.5f - 0.3f * (1f - p.x * p.x / 0.67f);
                    d = Mathf.Min(d, Mathf.Abs(p.y - sag) - 0.045f + Mathf.Max(0f, Mathf.Abs(p.x) - 0.82f));
                    for (int i = 0; i < 4; i++)
                    {
                        float x = -0.5f + i * 0.333f;
                        float y = 0.5f - 0.3f * (1f - x * x / 0.67f);
                        d = Mathf.Min(d, Polygon(p - new Vector2(x, y), Pennant) - 0.03f);
                    }
                    return d;
                }
                default:
                    return Polygon(p, Tip) - 0.08f;
            }
        }

        static Sprite Make(HintGlyph glyph)
        {
            var px = new Color32[Size * Size];
            float c = (Size - 1) * 0.5f, scale = Size * 0.5f;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float d = Distance(glyph, new Vector2((x - c) / scale, (y - c) / scale)) * scale;
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(0.5f - d) * 255f));
                }
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "HintGlyph" + glyph,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
