using Drift.Core;
using Drift.Life;
using Drift.SaveSystem;
using UnityEngine;

namespace Drift.Bridge
{
    public enum JournalGlyph { Animal, Wader, Tree, Sprout, Crab, Turtle, Butterfly, Firefly, Bird, Fish, Whale, Jelly, Boat, Check }

    // Little white silhouettes for the album cards, drawn once per domain from soft signed-distance shapes in a
    // -1..1 box (y up), like UiSprites' icons; one tint colours them.
    public static class JournalGlyphs
    {
        const int Size = 96;
        static readonly Sprite[] Sprites = new Sprite[System.Enum.GetValues(typeof(JournalGlyph)).Length];
        static readonly Vector2[] Tail = { new Vector2(-0.38f, 0f), new Vector2(-0.86f, 0.4f), new Vector2(-0.86f, -0.4f) };
        static readonly Vector2[] Fluke = { new Vector2(-0.5f, 0.02f), new Vector2(-0.92f, 0.42f), new Vector2(-0.78f, 0f), new Vector2(-0.92f, -0.3f) };
        static readonly Vector2[] Hull = { new Vector2(-0.8f, -0.2f), new Vector2(0.8f, -0.2f), new Vector2(0.52f, -0.62f), new Vector2(-0.52f, -0.62f) };
        static readonly Vector2[] Sail = { new Vector2(0.04f, 0.8f), new Vector2(0.62f, -0.06f), new Vector2(0.04f, -0.06f) };
        static readonly Vector2[] Jib = { new Vector2(-0.1f, 0.56f), new Vector2(-0.1f, -0.06f), new Vector2(-0.52f, -0.06f) };

        public static Sprite Of(JournalGlyph glyph)
        {
            int i = (int)glyph;
            return Sprites[i] != null ? Sprites[i] : (Sprites[i] = Make(glyph));
        }

        public static JournalGlyph For(CollectEntry e)
        {
            switch (e.type)
            {
                case CollectType.Animal:
                    return e.life == LifeKind.Flamingo || e.life == LifeKind.Penguin ? JournalGlyph.Wader
                        : e.life == LifeKind.Tortoise ? JournalGlyph.Turtle : JournalGlyph.Animal;
                case CollectType.Plant:
                {
                    int slot = LifeMeshes.PlantSlot(e.life);
                    return slot == BiomeSpec.RoleTree || slot == BiomeSpec.RolePalm ? JournalGlyph.Tree : JournalGlyph.Sprout;
                }
                case CollectType.Critter:
                    return e.life == LifeKind.Crab ? JournalGlyph.Crab : e.life == LifeKind.Turtle ? JournalGlyph.Turtle
                        : e.life == LifeKind.Butterfly ? JournalGlyph.Butterfly : JournalGlyph.Firefly;
                case CollectType.Bird: return JournalGlyph.Bird;
                case CollectType.Boat: return JournalGlyph.Boat;
                default:
                    return e.hasSea && (e.sea == SeaKind.Whale || e.sea == SeaKind.WhaleCalf || e.sea == SeaKind.WhaleBull || e.sea == SeaKind.Dolphin) ? JournalGlyph.Whale
                        : e.hasSea && e.sea == SeaKind.Jellyfish ? JournalGlyph.Jelly
                        : e.hasSea && e.sea == SeaKind.SeaTurtle ? JournalGlyph.Turtle : JournalGlyph.Fish;
            }
        }

        static float Disc(Vector2 p, float x, float y, float r) => (p - new Vector2(x, y)).magnitude - r;

        static float Capsule(Vector2 p, float ax, float ay, float bx, float by, float r)
        {
            Vector2 a = new Vector2(ax, ay), ab = new Vector2(bx - ax, by - ay);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude - r;
        }

        static float Oval(Vector2 p, float x, float y, float hx, float hy)
        {
            // Rounded box with the largest possible radius: close enough to an ellipse at this size.
            float r = Mathf.Min(hx, hy);
            float qx = Mathf.Max(Mathf.Abs(p.x - x) - (hx - r), 0f), qy = Mathf.Max(Mathf.Abs(p.y - y) - (hy - r), 0f);
            return Mathf.Sqrt(qx * qx + qy * qy) - r;
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

        static float Min(float a, float b, float c) => Mathf.Min(a, Mathf.Min(b, c));

        static float Distance(JournalGlyph glyph, Vector2 p)
        {
            float mx = Mathf.Abs(p.x);
            var m = new Vector2(mx, p.y);
            switch (glyph)
            {
                case JournalGlyph.Animal:
                {
                    float d = Min(Oval(p, -0.08f, 0.02f, 0.56f, 0.3f), Disc(p, 0.56f, 0.34f, 0.25f), Capsule(p, 0.5f, 0.5f, 0.42f, 0.8f, 0.075f));
                    d = Min(d, Capsule(p, -0.42f, -0.1f, -0.42f, -0.7f, 0.1f), Capsule(p, 0.24f, -0.1f, 0.24f, -0.7f, 0.1f));
                    return Mathf.Min(d, Disc(p, -0.7f, 0.16f, 0.11f));
                }
                case JournalGlyph.Wader:
                {
                    float d = Min(Oval(p, -0.06f, 0f, 0.4f, 0.3f), Capsule(p, 0.22f, 0.12f, 0.3f, 0.62f, 0.085f), Disc(p, 0.34f, 0.68f, 0.17f));
                    d = Min(d, Capsule(p, 0.46f, 0.66f, 0.66f, 0.56f, 0.06f), Capsule(p, -0.12f, -0.2f, -0.12f, -0.8f, 0.055f));
                    return Mathf.Min(d, Capsule(p, 0.06f, -0.2f, 0.06f, -0.8f, 0.055f));
                }
                case JournalGlyph.Tree:
                    return Min(Min(Disc(p, 0f, 0.34f, 0.46f), Disc(m, 0.36f, 0.04f, 0.34f), Disc(p, 0f, 0f, 0.3f)),
                        Capsule(p, 0f, -0.78f, 0f, -0.1f, 0.11f), Capsule(p, -0.3f, -0.8f, 0.3f, -0.8f, 0.05f));
                case JournalGlyph.Sprout:
                    return Min(Min(Capsule(p, 0f, -0.74f, 0f, 0.05f, 0.065f), Capsule(p, -0.08f, 0f, -0.5f, 0.46f, 0.2f), Capsule(p, 0.08f, -0.18f, 0.5f, 0.2f, 0.17f)),
                        Capsule(p, -0.5f, -0.8f, 0.5f, -0.8f, 0.055f), Disc(p, 0f, 0.1f, 0.08f));
                case JournalGlyph.Crab:
                {
                    float d = Min(Oval(p, 0f, -0.12f, 0.5f, 0.32f), Disc(m, 0.66f, 0.42f, 0.2f), Capsule(m, 0.42f, 0.02f, 0.64f, 0.3f, 0.07f));
                    d = Min(d, Capsule(m, 0.4f, -0.3f, 0.78f, -0.5f, 0.055f), Capsule(m, 0.3f, -0.38f, 0.56f, -0.72f, 0.055f));
                    d = Mathf.Min(d, Capsule(m, 0.16f, 0.16f, 0.2f, 0.4f, 0.06f));
                    // The open claw.
                    return Mathf.Max(d, -Capsule(m, 0.66f, 0.46f, 0.86f, 0.72f, 0.06f));
                }
                case JournalGlyph.Turtle:
                {
                    float d = Min(Oval(p, -0.06f, 0.04f, 0.52f, 0.36f), Disc(p, 0.66f, 0.02f, 0.18f), Capsule(p, -0.56f, -0.04f, -0.76f, -0.12f, 0.06f));
                    return Min(d, Capsule(p, 0.26f, -0.26f, 0.42f, -0.56f, 0.11f), Capsule(p, -0.32f, -0.26f, -0.46f, -0.54f, 0.1f));
                }
                case JournalGlyph.Butterfly:
                    return Min(Disc(m, 0.4f, 0.26f, 0.36f), Disc(m, 0.3f, -0.34f, 0.25f),
                        Mathf.Min(Capsule(p, 0f, -0.5f, 0f, 0.46f, 0.085f), Capsule(m, 0.04f, 0.5f, 0.2f, 0.78f, 0.035f)));
                case JournalGlyph.Firefly:
                {
                    float d = Min(Oval(p, 0f, -0.2f, 0.24f, 0.36f), Disc(p, 0f, 0.26f, 0.16f), Capsule(m, 0.12f, 0.06f, 0.56f, 0.4f, 0.12f));
                    for (int i = 0; i < 5; i++)
                    {
                        float a = (200f + i * 35f) * Mathf.Deg2Rad;
                        d = Mathf.Min(d, Capsule(p, Mathf.Cos(a) * 0.52f, -0.3f + Mathf.Sin(a) * 0.52f, Mathf.Cos(a) * 0.72f, -0.3f + Mathf.Sin(a) * 0.72f, 0.045f));
                    }
                    return d;
                }
                case JournalGlyph.Bird:
                    return Min(Capsule(m, 0f, -0.1f, 0.42f, 0.3f, 0.1f), Capsule(m, 0.42f, 0.3f, 0.84f, 0.04f, 0.085f), Disc(p, 0f, -0.14f, 0.15f));
                case JournalGlyph.Fish:
                    return Mathf.Max(Min(Oval(p, 0.14f, 0f, 0.56f, 0.34f), Polygon(p, Tail) - 0.06f, Capsule(p, 0.1f, 0.3f, -0.1f, 0.52f, 0.08f)), -Disc(p, 0.42f, 0.08f, 0.07f));
                case JournalGlyph.Whale:
                {
                    float d = Min(Oval(p, 0.12f, -0.06f, 0.66f, 0.34f), Polygon(p, Fluke) - 0.06f, Capsule(p, 0.05f, 0.24f, -0.12f, 0.48f, 0.07f));
                    d = Min(d, Capsule(p, 0.4f, 0.5f, 0.3f, 0.78f, 0.045f), Capsule(p, 0.4f, 0.5f, 0.54f, 0.76f, 0.045f));
                    return Mathf.Max(d, -Disc(p, 0.5f, 0f, 0.06f));
                }
                case JournalGlyph.Jelly:
                {
                    float d = Mathf.Max(Disc(p, 0f, 0.16f, 0.56f), -(p.y - 0.1f));
                    d = Mathf.Min(d, Capsule(p, -0.46f, 0.1f, 0.46f, 0.1f, 0.1f));
                    for (int i = 0; i < 4; i++)
                    {
                        float x = -0.36f + i * 0.24f;
                        d = Mathf.Min(d, Capsule(p, x, 0.05f, x + (i % 2 == 0 ? 0.08f : -0.08f), -0.74f + (i % 2) * 0.16f, 0.055f));
                    }
                    return d;
                }
                case JournalGlyph.Boat:
                    return Min(Min(Polygon(p, Hull) - 0.06f, Polygon(p, Sail) - 0.04f, Polygon(p, Jib) - 0.04f),
                        Capsule(p, -0.03f, -0.2f, -0.03f, 0.84f, 0.04f), Capsule(p, -0.9f, -0.84f, 0.9f, -0.84f, 0.045f));
                default:
                    return Mathf.Min(Capsule(p, -0.52f, -0.04f, -0.16f, -0.42f, 0.15f), Capsule(p, -0.16f, -0.42f, 0.56f, 0.42f, 0.15f));
            }
        }

        static Sprite Make(JournalGlyph glyph)
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
                name = "JournalGlyph" + glyph,
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
