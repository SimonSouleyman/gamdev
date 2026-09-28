using System.Collections.Generic;
using UnityEngine;

namespace Drift.UI
{
    public enum UiIcon { Pause, Book, Camera, Album, Close, Back, Home, Dice }

    // All UI shapes are generated once per domain as white signed-distance sprites and tinted by the Image colour.
    // Sprites use 100 pixels per unit, so one texel is one canvas unit and a border of N texels is a corner radius of N.
    public static class UiSprites
    {
        public const int ShadowBlur = 36;
        public const int ShadowRadius = 64;
        public const int CardRadius = 36;
        public const int PanelRadius = 64;
        public const int SmallRadius = 18;
        const int IconSize = 128, SquircleSize = 256;
        const float SquirclePower = 4f;

        static Sprite _circle, _softShadow, _softCircle, _highlight, _ring, _panelRing, _circleRing, _innerShadow, _arrow;
        static Sprite _squircle, _squircleShadow, _squircleRing, _squircleInner;
        static readonly Sprite[] Icons = new Sprite[8];
        static readonly Dictionary<int, Sprite> Pills = new Dictionary<int, Sprite>();
        static readonly Dictionary<int, Sprite> PillSheens = new Dictionary<int, Sprite>();
        static readonly Dictionary<int, Sprite> PillShadows = new Dictionary<int, Sprite>();

        public static Sprite Circle => _circle != null ? _circle : (_circle = MakeCircle(128, 0f));
        public static Sprite CircleRing => _circleRing != null ? _circleRing : (_circleRing = MakeCircle(128, 5f));
        public static Sprite Rounded => RoundedOf(CardRadius);
        public static Sprite RoundedSmall => RoundedOf(SmallRadius);
        public static Sprite RoundedLarge => RoundedOf(PanelRadius);
        // Blurred rounded rectangle: the body lies ShadowBlur inside the sprite edge, so a shadow image has to
        // overhang its panel by ShadowBlur on every side.
        public static Sprite SoftShadow => _softShadow != null ? _softShadow : (_softShadow = MakeRounded(2 * (ShadowRadius + ShadowBlur) + 8, ShadowRadius, ShadowBlur, 0f));
        public static Sprite SoftCircle => _softCircle != null ? _softCircle : (_softCircle = MakeSoftCircle(128));
        public static Sprite TopHighlight => _highlight != null ? _highlight : (_highlight = MakeHighlight(2 * PanelRadius + 8, PanelRadius));
        public static Sprite Ring => _ring != null ? _ring : (_ring = MakeRounded(2 * CardRadius + 8, CardRadius, 0f, 6f));
        // The gentle line just inside a panel's edge; made for an image inset by PanelInset.
        public const float PanelInset = 7f;
        public static Sprite PanelRing => _panelRing != null ? _panelRing : (_panelRing = MakeRounded(2 * PanelRadius + 8, PanelRadius - (int)PanelInset, 0f, 3f));
        // Darkens towards the edge of a Rounded window, so a picture seems to lie a little below its frame.
        public static Sprite InnerShadow => _innerShadow != null ? _innerShadow : (_innerShadow = MakeInnerShadow(2 * CardRadius + 40, CardRadius, 20f));
        public static Sprite Arrow => _arrow != null ? _arrow : (_arrow = MakeArrow(96));

        // Superellipse family for square frames (minimap); not sliced, so use them on square rects only.
        public static Sprite Squircle => _squircle != null ? _squircle : (_squircle = MakeSquircle(0));
        public static Sprite SquircleShadow => _squircleShadow != null ? _squircleShadow : (_squircleShadow = MakeSquircle(1));
        public static Sprite SquircleRing => _squircleRing != null ? _squircleRing : (_squircleRing = MakeSquircle(2));
        public static Sprite SquircleInner => _squircleInner != null ? _squircleInner : (_squircleInner = MakeSquircle(3));
        // Share of a SquircleShadow image that is the body; the rest is the blurred overhang.
        public const float SquircleShadowBody = 0.78f;

        public static Sprite RoundedOf(int radius)
        {
            radius = Mathf.Clamp(radius, 1, 96);
            if (!Pills.TryGetValue(radius, out var sprite) || sprite == null) Pills[radius] = sprite = MakeRounded(2 * radius + 8, radius, 0f, 0f);
            return sprite;
        }

        // A sliced sprite shrinks its borders per axis, so a wide-radius pill on a thin bar ends in lens-shaped
        // points. Pills therefore come per height, with the radius being exactly half of it.
        public static Sprite PillOf(float height) => RoundedOf(PillRadius(height));

        public static Sprite PillHighlightOf(float height)
        {
            int r = PillRadius(height);
            if (!PillSheens.TryGetValue(r, out var sprite) || sprite == null) PillSheens[r] = sprite = MakeHighlight(2 * r + 8, r);
            return sprite;
        }

        // Soft shadow of a pill of that height; like SoftShadow the image overhangs the body by ShadowBlur.
        public static Sprite PillShadowOf(float height)
        {
            int r = PillRadius(height);
            if (!PillShadows.TryGetValue(r, out var sprite) || sprite == null) PillShadows[r] = sprite = MakeRounded(2 * (r + ShadowBlur) + 8, r, ShadowBlur, 0f);
            return sprite;
        }

        // Rounded shape for a rect of that size: the card radius where it fits, else a pill.
        public static Sprite RoundedFor(Vector2 size)
        {
            float min = Mathf.Min(size.x, size.y);
            return min > 0f && min < 2f * CardRadius + 8f ? PillOf(min) : Rounded;
        }

        public static Sprite Icon(UiIcon icon)
        {
            int i = (int)icon;
            return Icons[i] != null ? Icons[i] : (Icons[i] = MakeIcon(icon));
        }

        static int PillRadius(float height) => Mathf.Clamp(Mathf.FloorToInt(height * 0.5f), 1, 96);

        static float RoundedDistance(float x, float y, float half, float radius)
        {
            float qx = Mathf.Max(Mathf.Abs(x) - (half - radius), 0f);
            float qy = Mathf.Max(Mathf.Abs(y) - (half - radius), 0f);
            return Mathf.Sqrt(qx * qx + qy * qy) - radius;
        }

        static Sprite MakeCircle(int size, float ring)
        {
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f, r = size * 0.5f - 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) - r;
                    float a = Mathf.Clamp01(0.5f - d);
                    if (ring > 0f) a *= Mathf.Clamp01(0.5f + d + ring);
                    px[y * size + x] = White(a);
                }
            return Finish(size, px, Vector4.zero);
        }

        static Sprite MakeSoftCircle(int size)
        {
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f, r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / r;
                    float a = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.55f, 1f, d));
                    px[y * size + x] = White(a);
                }
            return Finish(size, px, Vector4.zero);
        }

        // blur > 0 gives a soft falloff around a body inset by blur; ring > 0 keeps only an outline of that width.
        static Sprite MakeRounded(int size, int radius, float blur, float ring)
        {
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            float half = c - blur;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = RoundedDistance(x - c, y - c, half, radius);
                    float a;
                    if (blur > 0f) a = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(-blur, blur, d));
                    else if (ring > 0f) a = Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(0.5f + d + ring);
                    else a = Mathf.Clamp01(0.5f - d);
                    px[y * size + x] = White(a);
                }
            float b = radius + blur;
            return Finish(size, px, new Vector4(b, b, b, b));
        }

        static Sprite MakeInnerShadow(int size, int radius, float depth)
        {
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = RoundedDistance(x - c, y - c, c, radius);
                    float edge = 1f - Mathf.Clamp01(-d / depth);
                    // A little heavier under the top edge, as if lit from above.
                    float top = Mathf.Lerp(0.75f, 1.15f, y / (float)(size - 1));
                    px[y * size + x] = White(Mathf.Clamp01(0.5f - d) * edge * edge * top);
                }
            float b = Mathf.Max(radius, depth) + 2f;
            return Finish(size, px, new Vector4(b, b, b, b));
        }

        // A rounded body whose alpha fades from the top edge to nothing at the border line, so the sliced sprite
        // lays a fixed-height sheen over the top of any panel or button.
        static Sprite MakeHighlight(int size, int radius)
        {
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            int band = size / 2 - 2;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = RoundedDistance(x - c, y - c, c, radius);
                    float fromTop = (size - 1 - y) / (float)band;
                    float fade = 1f - Mathf.Clamp01(fromTop);
                    float a = Mathf.Clamp01(0.5f - d) * fade * fade;
                    px[y * size + x] = White(a);
                }
            return Finish(size, px, new Vector4(radius, 2f, radius, band));
        }

        // kind 0 body, 1 blurred shadow (body = SquircleShadowBody of the image), 2 ring, 3 inner shadow.
        static Sprite MakeSquircle(int kind)
        {
            int size = SquircleSize;
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            float r = kind == 1 ? c * SquircleShadowBody : c - 1f;
            float blur = c - r;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float ax = Mathf.Abs(x - c), ay = Mathf.Abs(y - c);
                    float sum = Mathf.Pow(ax, SquirclePower) + Mathf.Pow(ay, SquirclePower);
                    float f = Mathf.Pow(sum, 1f / SquirclePower);
                    // |grad f| lies between 0.84 and 1; dividing by it turns f - r into a good pixel distance.
                    float gx = Mathf.Pow(ax, SquirclePower - 1f), gy = Mathf.Pow(ay, SquirclePower - 1f);
                    float grad = sum > 1e-6f ? Mathf.Sqrt(gx * gx + gy * gy) / Mathf.Pow(sum, (SquirclePower - 1f) / SquirclePower) : 1f;
                    float d = (f - r) / Mathf.Max(0.5f, grad);
                    float a;
                    if (kind == 1) a = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(-blur, blur, d));
                    else if (kind == 2) a = Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(0.5f + d + 5f);
                    else if (kind == 3)
                    {
                        float edge = 1f - Mathf.Clamp01(-d / 22f);
                        a = Mathf.Clamp01(0.5f - d) * edge * edge * Mathf.Lerp(0.75f, 1.15f, y / (float)(size - 1));
                    }
                    else a = Mathf.Clamp01(0.5f - d);
                    px[y * size + x] = White(a);
                }
            return Finish(size, px, Vector4.zero);
        }

        // Navigation arrow pointing up: a kite with a notched tail, so its direction reads at any rotation.
        static Sprite MakeArrow(int size)
        {
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            float round = size * 0.1f;
            var poly = new[]
            {
                new Vector2(0f, size * 0.35f), new Vector2(size * 0.27f, -size * 0.29f),
                new Vector2(0f, -size * 0.14f), new Vector2(-size * 0.27f, -size * 0.29f),
            };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = PolygonDistance(new Vector2(x - c, y - c), poly) - round;
                    px[y * size + x] = White(Mathf.Clamp01(0.5f - d));
                }
            return Finish(size, px, Vector4.zero);
        }

        // ---------------------------------------------------------------- icons

        // Icons are drawn in a -1..1 box (y up) from soft signed-distance shapes; holes are cut out, so one tint does.
        static readonly Vector2[] Roof = { new Vector2(0f, 0.7f), new Vector2(0.72f, 0.02f), new Vector2(-0.72f, 0.02f) };
        static readonly Vector2[] Hill = { new Vector2(-0.34f, -0.36f), new Vector2(-0.02f, 0.02f), new Vector2(0.3f, -0.36f) };

        static float Box(Vector2 p, Vector2 center, Vector2 half, float radius)
        {
            float qx = Mathf.Max(Mathf.Abs(p.x - center.x) - (half.x - radius), 0f);
            float qy = Mathf.Max(Mathf.Abs(p.y - center.y) - (half.y - radius), 0f);
            float inside = Mathf.Min(Mathf.Max(Mathf.Abs(p.x - center.x) - (half.x - radius), Mathf.Abs(p.y - center.y) - (half.y - radius)), 0f);
            return Mathf.Sqrt(qx * qx + qy * qy) + inside - radius;
        }

        static float Disc(Vector2 p, Vector2 center, float radius) => (p - center).magnitude - radius;

        static float Capsule(Vector2 p, Vector2 a, Vector2 b, float radius) => SegmentDistance(p, a, b) - radius;

        static Vector2 Turn(Vector2 p, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, s = Mathf.Sin(r), c = Mathf.Cos(r);
            return new Vector2(c * p.x + s * p.y, -s * p.x + c * p.y);
        }

        static float IconDistance(UiIcon icon, Vector2 p)
        {
            switch (icon)
            {
                case UiIcon.Pause:
                    return Mathf.Min(Capsule(p, new Vector2(-0.3f, -0.4f), new Vector2(-0.3f, 0.4f), 0.17f),
                        Capsule(p, new Vector2(0.3f, -0.4f), new Vector2(0.3f, 0.4f), 0.17f));
                case UiIcon.Close:
                    return Mathf.Min(Capsule(p, new Vector2(-0.4f, -0.4f), new Vector2(0.4f, 0.4f), 0.15f),
                        Capsule(p, new Vector2(-0.4f, 0.4f), new Vector2(0.4f, -0.4f), 0.15f));
                case UiIcon.Back:
                {
                    var tip = new Vector2(-0.5f, 0f);
                    float d = Capsule(p, tip, new Vector2(0.55f, 0f), 0.14f);
                    d = Mathf.Min(d, Capsule(p, tip, new Vector2(-0.08f, 0.42f), 0.14f));
                    return Mathf.Min(d, Capsule(p, tip, new Vector2(-0.08f, -0.42f), 0.14f));
                }
                case UiIcon.Home:
                {
                    float d = Mathf.Min(PolygonDistance(p, Roof) - 0.12f, Box(p, new Vector2(0f, -0.32f), new Vector2(0.5f, 0.4f), 0.14f));
                    return Mathf.Max(d, -Box(p, new Vector2(0f, -0.5f), new Vector2(0.15f, 0.26f), 0.12f));
                }
                case UiIcon.Dice:
                {
                    var q = Turn(p, -10f);
                    float d = Box(q, Vector2.zero, new Vector2(0.68f, 0.68f), 0.26f);
                    d = Mathf.Max(d, -Disc(q, Vector2.zero, 0.125f));
                    for (int i = 0; i < 4; i++)
                        d = Mathf.Max(d, -Disc(q, new Vector2(i % 2 == 0 ? -0.34f : 0.34f, i < 2 ? -0.34f : 0.34f), 0.125f));
                    return d;
                }
                case UiIcon.Book:
                {
                    var q = Turn(p, 6f);
                    float d = Box(q, Vector2.zero, new Vector2(0.56f, 0.7f), 0.16f);
                    d = Mathf.Max(d, -Capsule(q, new Vector2(-0.3f, -0.5f), new Vector2(-0.3f, 0.5f), 0.05f));
                    return Mathf.Max(d, -Box(q, new Vector2(0.13f, 0.24f), new Vector2(0.25f, 0.14f), 0.09f));
                }
                case UiIcon.Camera:
                {
                    var lens = new Vector2(0f, -0.12f);
                    float d = Mathf.Min(Box(p, lens, new Vector2(0.8f, 0.52f), 0.22f), Box(p, new Vector2(-0.3f, 0.46f), new Vector2(0.26f, 0.16f), 0.1f));
                    d = Mathf.Max(d, -Disc(p, lens, 0.34f));
                    d = Mathf.Min(d, Disc(p, lens, 0.19f));
                    return Mathf.Max(d, -Disc(p, new Vector2(0.54f, 0.2f), 0.075f));
                }
                default:
                {
                    var front = new Vector2(0.06f, -0.1f);
                    float d = Box(p, front, new Vector2(0.62f, 0.5f), 0.16f);
                    d = Mathf.Max(d, -Box(p, front, new Vector2(0.46f, 0.34f), 0.08f));
                    d = Mathf.Min(d, Mathf.Max(PolygonDistance(p - front, Hill) - 0.06f, -(p.y - front.y + 0.34f)));
                    d = Mathf.Min(d, Disc(p, front + new Vector2(0.22f, 0.12f), 0.09f));
                    float back = Box(Turn(p - new Vector2(-0.06f, 0.1f), 12f), Vector2.zero, new Vector2(0.62f, 0.5f), 0.16f);
                    back = Mathf.Max(back, -(Box(p, front, new Vector2(0.62f, 0.5f), 0.16f) - 0.09f));
                    return Mathf.Min(d, back);
                }
            }
        }

        static Sprite MakeIcon(UiIcon icon)
        {
            int size = IconSize;
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f, scale = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = IconDistance(icon, new Vector2((x - c) / scale, (y - c) / scale)) * scale;
                    px[y * size + x] = White(Mathf.Clamp01(0.5f - d));
                }
            return Finish(size, px, Vector4.zero);
        }

        static float PolygonDistance(Vector2 p, Vector2[] v)
        {
            float d = float.MaxValue;
            bool inside = false;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
            {
                d = Mathf.Min(d, SegmentDistance(p, v[j], v[i]));
                if ((v[i].y > p.y) != (v[j].y > p.y) && p.x < (v[j].x - v[i].x) * (p.y - v[i].y) / (v[j].y - v[i].y) + v[i].x) inside = !inside;
            }
            return inside ? -d : d;
        }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }

        static Color32 White(float alpha) => new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));

        static Sprite Finish(int size, Color32[] px, Vector4 border)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }
    }
}
