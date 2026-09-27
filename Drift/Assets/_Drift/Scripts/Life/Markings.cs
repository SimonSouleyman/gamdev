using UnityEngine;

namespace Drift.Life
{
    // Detail animal template with its procedural markings (AnimalModels.Rig.Finish).
    public class AnimalTemplate : PlantTemplate
    {
        public Vector4[] marks;
    }

    // Procedural coat markings drawn by Shaders/DriftMarkings.hlsl (Drift/Animal, Drift/Critter). Per vertex UV3 =
    //   (u, v, w, code): u, v = pattern coordinates in template units (along / around the part, or x / z for shells
    //   and wings; for Tip u runs 0 at the root to 1 at the tip), w = how far the face points to the animal's back
    //   (0 belly .. 1 back, countershading), code = pattern * 8 + level (strength 0..7) + seed * SeedScale.
    // The template bakes pattern, level and coordinates; the batch adds the individual's seed. Template units keep a
    // pattern on the body through every pose and animation channel, and a young animal (baked smaller) keeps it.
    public static class Markings
    {
        public const int None = 0, Fur = 1, Fluff = 2, Wool = 3, Skin = 4, Zebra = 5, Giraffe = 6, Scutes = 7, Hide = 8,
            Meerkat = 9, Feather = 10, Tip = 11, Speckle = 12, WingYellow = 13, WingWhite = 14, WingBlue = 15, Coarse = 16;
        public const int FullLevel = 7;
        public const float SeedScale = 0.9f;

        public static float Code(int pattern, int level) => pattern * 8 + Mathf.Clamp(level, 0, FullLevel);
        public static int PatternOf(float code) => Mathf.FloorToInt(code + 1e-3f) / 8;
        public static int LevelOf(float code) => Mathf.FloorToInt(code + 1e-3f) % 8;
        public static float SeedOf(float code) => (code - Mathf.Floor(code + 1e-3f)) / SeedScale;
        public static float SeedCode(float seed) => Mathf.Repeat(seed, 1f) * SeedScale;
        // A stable 0..1 seed from a per-individual value (the herd uses each animal's phase, critters theirs).
        public static float Seed(float phase) => Mathf.Repeat(phase * 0.618034f + 0.137f, 1f);
        // Tip marking level from where the dark tip starts along the part (0 root .. 1 tip).
        public static int TipLevel(float start) => Mathf.Clamp(Mathf.RoundToInt((1f - start) * 10f), 0, FullLevel);

        public static Vector4[] Of(PlantTemplate tpl) => tpl is AnimalTemplate a ? a.marks : null;

        public static int CoatOf(LifeKind kind)
        {
            switch (kind)
            {
                case LifeKind.Hare: case LifeKind.Goat: case LifeKind.Reindeer: return Fur;
                case LifeKind.Sheep: return Wool;
                case LifeKind.Ox: return Hide;
                case LifeKind.Capybara: return Coarse;
                case LifeKind.Flamingo: case LifeKind.Penguin: return Feather;
                case LifeKind.Tortoise: return Scutes;
                case LifeKind.ArcticFox: return Fluff;
                case LifeKind.Zebra: return Zebra;
                case LifeKind.Giraffe: return Giraffe;
                case LifeKind.Meerkat: return Meerkat;
                default: return None;
            }
        }

        // axis: the part's long axis (u), dorsal: the side of the part that faces the animal's back.
        internal static Vector4 Coat(Vector3 p, Vector3 n, int pattern, int level, Vector3 axis, Vector3 dorsal)
        {
            float u, v;
            if (pattern == Scutes) { u = p.x; v = p.z; }
            else { u = Vector3.Dot(p, axis); v = Vector3.Dot(p, dorsal) - Mathf.Abs(p.x); }
            float w = Mathf.Clamp01(0.5f + 0.5f * Vector3.Dot(n, dorsal));
            return new Vector4(u, v, w, pattern == None ? 0f : Code(pattern, level));
        }

        internal static Vector4 TipMark(Vector3 p, Vector3 root, Vector3 tip, int level)
        {
            Vector3 d = tip - root;
            float t = Mathf.Clamp01(Vector3.Dot(p - root, d) / Mathf.Max(1e-8f, d.sqrMagnitude));
            return new Vector4(t, 0f, 0.5f, Code(Tip, level));
        }

        static bool Near(Color lin, Color srgb)
        {
            Color l = srgb.linear;
            return Mathf.Abs(lin.r - l.r) < 0.004f && Mathf.Abs(lin.g - l.g) < 0.004f && Mathf.Abs(lin.b - l.b) < 0.004f;
        }

        static readonly Vector4[][] SimpleCache = new Vector4[AnimalModels.SpeciesCount * LifeMeshes.Variants][];

        // Marks for the simple (far LOD) herd templates of LifeMeshes: the species' coat on the body, horns, beaks and
        // feet left plain (recognised by their colours), dark faces and skin with the fine skin grain only.
        public static Vector4[] SimpleMarks(LifeKind kind, int variant)
        {
            if (!AnimalModels.Has(kind)) return null;
            variant = Mathf.Abs(variant) % LifeMeshes.Variants;
            ref Vector4[] slot = ref SimpleCache[AnimalModels.IndexOf(kind) * LifeMeshes.Variants + variant];
            if (slot != null) return slot;
            var tpl = LifeMeshes.GetTemplate(kind, variant);
            int coat = CoatOf(kind);
            var marks = new Vector4[tpl.vertices.Length];
            for (int i = 0; i < marks.Length; i++)
            {
                Color c = tpl.colors[i];
                int pattern = coat;
                if (Near(c, LifeMeshes.Horn) || Near(c, LifeMeshes.Horn * 0.8f) || Near(c, new Color(0.85f, 0.8f, 0.7f))
                    || Near(c, new Color(0.15f, 0.1f, 0.1f)) || Near(c, AnimalModels.PenguinOrange)) pattern = None;
                else if (kind == LifeKind.Sheep && Near(c, LifeMeshes.SheepDark)) pattern = Skin;
                else if (kind == LifeKind.Tortoise && Near(c, AnimalModels.TortoiseSkin)) pattern = Skin;
                marks[i] = Coat(tpl.vertices[i], tpl.normals[i], pattern, FullLevel, Vector3.forward, Vector3.up);
            }
            return slot = marks;
        }

        // Herd marks for whichever template the herd drew: a detail template brings its own.
        public static Vector4[] For(LifeKind kind, int variant, PlantTemplate tpl)
        {
            var own = Of(tpl);
            if (own != null) return own;
            var simple = SimpleMarks(kind, variant);
            return simple != null && simple.Length == tpl.vertices.Length ? simple : null;
        }

        static readonly Vector4[][] CritterCache = new Vector4[3 * LifeMeshes.Variants][];

        // Critter marks (x, z, up-ness, code) for CritterBatch: speckled crab carapace, scuted sea-turtle shell (scaled
        // so its plates match the tortoise's), one wing pattern per butterfly colour.
        public static Vector4[] CritterMarks(LifeKind kind, int variant)
        {
            int k = kind == LifeKind.Crab ? 0 : kind == LifeKind.Turtle ? 1 : kind == LifeKind.Butterfly ? 2 : -1;
            if (k < 0) return null;
            variant = Mathf.Abs(variant) % LifeMeshes.Variants;
            ref Vector4[] slot = ref CritterCache[k * LifeMeshes.Variants + variant];
            if (slot != null) return slot;
            var tpl = LifeMeshes.GetTemplate(kind, variant);
            var marks = new Vector4[tpl.vertices.Length];
            Color body = tpl.colors[0];
            for (int i = 0; i < marks.Length; i++)
            {
                Vector3 p = tpl.vertices[i];
                float w = Mathf.Clamp01(0.5f + 0.5f * tpl.normals[i].y);
                bool isBody = tpl.colors[i] == body;
                switch (k)
                {
                    case 0:
                        marks[i] = isBody ? new Vector4(p.x, p.z, w, Code(Speckle, FullLevel)) : default;
                        break;
                    case 1:
                        marks[i] = isBody ? new Vector4(p.x * 0.6f, p.z * 0.6f, w, Code(Scutes, FullLevel)) : new Vector4(p.z, p.y, w, Code(Skin, FullLevel));
                        break;
                    default:
                        marks[i] = new Vector4(p.x, p.z, 1f, Code(WingYellow + variant, FullLevel));
                        break;
                }
            }
            return slot = marks;
        }

        static readonly PlantTemplate[] BirdCache = new PlantTemplate[2 * LifeMeshes.Variants];

        // Flock birds (Drift/VertexColor, no marking channel): dark primaries and a paler belly baked into a copy of
        // the template's vertex colours; songbirds get darker wing ends, gulls black wing tips.
        public static PlantTemplate BirdTemplate(LifeKind kind, int variant)
        {
            var tpl = LifeMeshes.GetTemplate(kind, variant);
            if (kind != LifeKind.Bird && kind != LifeKind.Seabird) return tpl;
            variant = Mathf.Abs(variant) % LifeMeshes.Variants;
            ref PlantTemplate slot = ref BirdCache[(kind == LifeKind.Seabird ? LifeMeshes.Variants : 0) + variant];
            if (slot != null) return slot;
            var cols = (Color[])tpl.colors.Clone();
            for (int i = 0; i < cols.Length; i++)
            {
                Vector3 p = tpl.vertices[i];
                float ax = Mathf.Abs(p.x);
                Color c = cols[i];
                if (kind == LifeKind.Bird)
                {
                    if (ax > 0.3f) c *= 0.42f;
                    else if (ax < 0.11f && p.y < -0.02f && p.z > -0.12f) c = Color.Lerp(c, new Color(0.9f, 0.88f, 0.82f).linear, 0.45f);
                }
                else
                {
                    if (ax > 0.59f && variant < 2 && cols[i] != tpl.colors[0]) c = Color.Lerp(c, new Color(0.08f, 0.08f, 0.09f).linear, 0.6f);
                }
                c.a = cols[i].a;
                cols[i] = c;
            }
            return slot = new PlantTemplate
            {
                vertices = tpl.vertices, normals = tpl.normals, colors = cols, triangles = tpl.triangles,
                sway = tpl.sway, wing = tpl.wing, part = tpl.part
            };
        }
    }
}
