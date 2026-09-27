using System.Collections.Generic;
using UnityEngine;

namespace Drift.Life
{
    // Detail LOD templates for the four herd species (250-310 verts instead of 60-78): legs, ears, tail, snout,
    // eyes, horns. They drive the unchanged Drift/Animal channels with per-vertex values instead of the plain
    // z / y of the simple templates:
    //   lever  body = z; feet 0 (planted) rising to z at the hip; head = one common lever (so it moves as a
    //          block, the snout a little more) scaled so that the lowest head vertex just reaches the ground
    //          at the deepest graze swing (pitch 10 + 6 degrees); neck vertices blend body -> head.
    //   up     body = legLength / RestLower + squash * (y - legLength): resting lowers the body by exactly the
    //          leg length (plus a little squash) while the feet (up 0) stay put, so the legs fold away; the
    //          head's value puts its lowest vertex on the ground at the sleep pitch.
    //   leg    signed step lift in template units (diagonal pairs +/-), 0 at the hip, baked into colour alpha.
    public static partial class AnimalModels
    {
        // Must match _RestLower on the animal material.
        public const float RestLower = 0.38f;
        const float GrazeSin = 0.2756f;
        const float SleepSin = 0.2419f;
        const float HeadTilt = 1.5f;
        const float MaxHeadLever = 0.9f;

        public const int SpeciesCount = 13;
        // Pose variants of the biome animals, baked as templates of their own because the shader only knows
        // pitch and rest: flamingo on one leg with the head tucked, tortoise drawn into its shell, meerkat
        // upright on its hind legs, penguin flat on its belly. Every other kind has pose 0 only. PoseCall: the
        // penguin's sky call (beak to the sky, flippers spread) - the only signature move that needs a model of its
        // own; the others are baked pitch / roll / lift of the default model (IslandHerdSystem.RebuildMesh).
        public const int PoseDefault = 0, PoseSpecial = 1, PoseCall = 2;

        static readonly PlantTemplate[,,,] Cache = new PlantTemplate[SpeciesCount, LifeMeshes.Variants, 2, 3];

        public static bool Has(LifeKind kind) => (kind >= LifeKind.Hare && kind <= LifeKind.Ox) || (kind >= LifeKind.Capybara && kind <= LifeKind.Meerkat);

        public static int IndexOf(LifeKind kind) => kind <= LifeKind.Ox ? kind - LifeKind.Hare : 4 + (kind - LifeKind.Capybara);

        public static bool HasSpecialPose(LifeKind kind) =>
            kind == LifeKind.Flamingo || kind == LifeKind.Tortoise || kind == LifeKind.Meerkat || kind == LifeKind.Penguin;

        public static PlantTemplate Get(LifeKind kind, int variant, bool young) => Get(kind, variant, young, PoseDefault);

        public static PlantTemplate Get(LifeKind kind, int variant, bool young, int pose)
        {
            if (!Has(kind)) return LifeMeshes.GetTemplate(kind, variant);
            variant = Mathf.Abs(variant) % LifeMeshes.Variants;
            if (pose == PoseSpecial && !HasSpecialPose(kind)) pose = PoseDefault;
            if (pose == PoseCall && kind != LifeKind.Penguin) pose = PoseDefault;
            if (pose < PoseDefault || pose > PoseCall) pose = PoseDefault;
            ref PlantTemplate slot = ref Cache[IndexOf(kind), variant, young ? 1 : 0, pose];
            if (slot == null) slot = kind <= LifeKind.Ox ? Build(kind, variant, young) : BuildBiome(kind, variant, young, pose);
            return slot;
        }

        enum PartKind { Body, Head, Neck, Leg }

        struct Part
        {
            public PartKind kind;
            public int start, end, group;
            public Vector3 a, b;
        }

        class Rig
        {
            public readonly ShapeBuilder b = new();
            readonly List<Part> _parts = new();
            public float legLen, squash = 0.3f, headScale = 1f, legLift = 0.4f, maxHeadLever = MaxHeadLever;
            public Vector3 headPivot;
            // Rotates the finished model about the x axis through the origin and shifts it (penguin on its belly);
            // the legs then count as body because their hip/foot heights no longer mean anything.
            public float bakePitch;
            public Vector3 bakeOffset;
            // Tilts the head about its pivot first (the sliding penguin looks ahead, not into the ground).
            public float headPitch;
            // Markings (see Markings): the coat of body / head / neck / legs (-1 = the body's), the leg's strength at the
            // foot relative to the hip, and an upright body (meerkat on its hind legs: long axis y, back = -z).
            public int coat = Markings.Fur, headCoat = -1, neckCoat = -1, legCoat = -1;
            public float legFoot = 1f;
            public bool upright;
            readonly List<(int start, int end, int pattern, int level, Vector3 root, Vector3 tip)> _paints = new();
            int _mark;

            // Overrides the markings of the vertices added since `from` (eyes, horns, beaks, muzzles stay plain).
            public void Paint(int from, int pattern, int level = Markings.FullLevel) => _paints.Add((from, b.VertexCount, pattern, level, default, default));

            public void Plain(int from) => Paint(from, Markings.None);

            // A cone whose end darkens from `start` (0 root .. 1 tip): hare ears, horn tips, tail tufts, flight feathers.
            public void TipCone(Vector3 root, Vector3 dir, float radius, float length, int sides, Color col, float start)
            {
                int from = b.VertexCount;
                b.Cone(root, dir, radius, length, sides, col);
                _paints.Add((from, b.VertexCount, Markings.Tip, Markings.TipLevel(start), root, root + dir.normalized * length));
            }

            void Close(PartKind kind, Vector3 a, Vector3 bb, int group)
            {
                _parts.Add(new Part { kind = kind, start = _mark, end = b.VertexCount, a = a, b = bb, group = group });
                _mark = b.VertexCount;
            }

            public void Body() => Close(PartKind.Body, default, default, 0);
            public void Head() => Close(PartKind.Head, default, default, 0);
            public void Neck(Vector3 from, Vector3 to) => Close(PartKind.Neck, from, to, 0);

            public void Leg(Vector3 hip, Vector3 foot, float size, float footSize, int sides, Color col, int group)
            {
                b.Tube(hip, foot, Vector3.forward, new Vector2(size, size), new Vector2(footSize, footSize), sides, col, false, false);
                Close(PartKind.Leg, hip, foot, group);
            }

            public void Eyes(Vector3 headA, Vector3 headB, float widthA, float widthB, float t, float lift, float size, Color col)
            {
                int from = b.VertexCount;
                EyePatches(headA, headB, widthA, widthB, t, lift, size, col);
                Plain(from);
            }

            void EyePatches(Vector3 headA, Vector3 headB, float widthA, float widthB, float t, float lift, float size, Color col)
            {
                Vector3 c = Vector3.Lerp(headA, headB, t);
                float x = Mathf.Lerp(widthA, widthB, t) * 0.5f + size * 0.45f;
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    Vector3 p = new Vector3(x * sgn, c.y + lift, c.z);
                    b.Patch(p + Vector3.up * size, p + Vector3.forward * size, p - Vector3.up * size, p - Vector3.forward * size, Vector3.right * sgn, col);
                }
            }

            static float Along(Vector3 p, Vector3 a, Vector3 bb)
            {
                Vector3 ab = bb - a;
                float l2 = ab.sqrMagnitude;
                return l2 < 1e-8f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / l2);
            }

            float BodyUp(float y) => legLen / RestLower + squash * (y - legLen);

            Vector4[] BakeMarks()
            {
                var marks = new Vector4[b.VertexCount];
                foreach (var part in _parts)
                {
                    int pattern = coat;
                    Vector3 axis = Vector3.forward, dorsal = Vector3.up;
                    switch (part.kind)
                    {
                        case PartKind.Head:
                            if (headCoat >= 0) pattern = headCoat;
                            break;
                        case PartKind.Neck:
                            if (neckCoat >= 0) pattern = neckCoat;
                            axis = (part.b - part.a).normalized;
                            dorsal = Vector3.Cross(axis, Vector3.right).normalized;
                            if (dorsal.y < 0f) dorsal = -dorsal;
                            break;
                        case PartKind.Leg:
                            if (legCoat >= 0) pattern = legCoat;
                            axis = Vector3.up;
                            dorsal = Vector3.forward;
                            break;
                        default:
                            if (upright) { axis = Vector3.up; dorsal = Vector3.back; }
                            break;
                    }
                    for (int i = part.start; i < part.end; i++)
                    {
                        Vector3 p = b.VertexAt(i);
                        int level = Markings.FullLevel;
                        if (part.kind == PartKind.Leg)
                        {
                            float t = Mathf.Clamp01((p.y - part.b.y) / Mathf.Max(1e-4f, part.a.y - part.b.y));
                            level = Mathf.RoundToInt(Markings.FullLevel * Mathf.Lerp(legFoot, 1f, t));
                        }
                        marks[i] = Markings.Coat(p, b.NormalAt(i), pattern, level, axis, dorsal);
                    }
                }
                foreach (var paint in _paints)
                    for (int i = paint.start; i < paint.end; i++)
                        marks[i] = paint.pattern == Markings.Tip
                            ? Markings.TipMark(b.VertexAt(i), paint.root, paint.tip, paint.level)
                            : Markings.Coat(b.VertexAt(i), b.NormalAt(i), paint.pattern, paint.level, Vector3.forward, Vector3.up);
                return marks;
            }

            public PlantTemplate Finish(string name)
            {
                // Marked on the rest shape, before the head tilt / bake pitch, so a pose keeps the coat in place.
                var marks = BakeMarks();
                if (headPitch != 0f)
                {
                    Quaternion hq = Quaternion.Euler(headPitch, 0f, 0f);
                    foreach (var part in _parts)
                    {
                        if (part.kind != PartKind.Head) continue;
                        for (int i = part.start; i < part.end; i++)
                        {
                            b.SetVertex(i, headPivot + hq * (b.VertexAt(i) - headPivot));
                            b.SetNormal(i, hq * b.NormalAt(i));
                        }
                    }
                }
                if (bakePitch != 0f)
                {
                    Quaternion q = Quaternion.Euler(bakePitch, 0f, 0f);
                    for (int i = 0; i < b.VertexCount; i++)
                    {
                        b.SetVertex(i, q * b.VertexAt(i) + bakeOffset);
                        b.SetNormal(i, q * b.NormalAt(i));
                    }
                    headPivot = q * headPivot + bakeOffset;
                    for (int k = 0; k < _parts.Count; k++)
                    {
                        var part = _parts[k];
                        if (part.kind == PartKind.Leg) part.kind = PartKind.Body;
                        part.a = q * part.a + bakeOffset;
                        part.b = q * part.b + bakeOffset;
                        _parts[k] = part;
                    }
                }
                if (headScale != 1f)
                    foreach (var part in _parts)
                    {
                        if (part.kind != PartKind.Head && part.kind != PartKind.Neck) continue;
                        for (int i = part.start; i < part.end; i++)
                        {
                            Vector3 p = b.VertexAt(i);
                            float w = part.kind == PartKind.Head ? 1f : Along(p, part.a, part.b);
                            b.SetVertex(i, Vector3.Lerp(p, headPivot + (p - headPivot) * headScale, w));
                        }
                    }

                int n = b.VertexCount;
                float f = float.MaxValue;
                foreach (var part in _parts)
                {
                    if (part.kind != PartKind.Head) continue;
                    for (int i = part.start; i < part.end; i++)
                    {
                        Vector3 p = b.VertexAt(i);
                        float raw = 1f + HeadTilt * (p.z - headPivot.z);
                        if (raw > 0.05f) f = Mathf.Min(f, p.y / (GrazeSin * raw));
                    }
                }
                if (f == float.MaxValue) f = 0f;
                // Long-necked heads (goat, ox) would need a lever that also lifts them absurdly high on a
                // look-up; they graze with the muzzle a little above the grass instead.
                float rawMax = 0f;
                foreach (var part in _parts)
                    if (part.kind == PartKind.Head)
                        for (int i = part.start; i < part.end; i++) rawMax = Mathf.Max(rawMax, 1f + HeadTilt * (b.VertexAt(i).z - headPivot.z));
                if (rawMax > 0f) f = Mathf.Min(f, maxHeadLever / rawMax);
                float headUp = float.MaxValue;
                foreach (var part in _parts)
                {
                    if (part.kind != PartKind.Head) continue;
                    for (int i = part.start; i < part.end; i++)
                    {
                        Vector3 p = b.VertexAt(i);
                        float lv = f * (1f + HeadTilt * (p.z - headPivot.z));
                        headUp = Mathf.Min(headUp, (p.y - SleepSin * lv - 0.004f) / RestLower);
                    }
                }
                headUp = Mathf.Min(headUp, legLen / RestLower);

                var lever = new float[n];
                var up = new float[n];
                var leg = new float[n];
                foreach (var part in _parts)
                    for (int i = part.start; i < part.end; i++)
                    {
                        Vector3 p = b.VertexAt(i);
                        float headLever = f * (1f + HeadTilt * (p.z - headPivot.z));
                        switch (part.kind)
                        {
                            case PartKind.Body:
                                lever[i] = p.z;
                                up[i] = BodyUp(p.y);
                                break;
                            case PartKind.Head:
                                lever[i] = headLever;
                                up[i] = headUp;
                                break;
                            case PartKind.Neck:
                            {
                                float t = Along(p, part.a, part.b);
                                lever[i] = Mathf.Lerp(p.z, headLever, t);
                                up[i] = Mathf.Lerp(BodyUp(p.y), headUp, t);
                                break;
                            }
                            default:
                            {
                                float t = Mathf.Clamp01((p.y - part.b.y) / Mathf.Max(1e-4f, part.a.y - part.b.y));
                                lever[i] = p.z * t;
                                up[i] = BodyUp(part.a.y) * t;
                                leg[i] = part.group * (1f - t) * legLen * legLift;
                                break;
                            }
                        }
                    }

                var m = b.ToMesh(name);
                var tpl = new AnimalTemplate
                {
                    vertices = m.vertices, normals = m.normals, colors = m.colors, triangles = m.triangles,
                    lever = lever, up = up, leg = leg, legLength = legLen, marks = marks
                };
                tpl.sway = new float[n];
                tpl.wing = tpl.sway;
                float top = 0f;
                foreach (var part in _parts)
                    if (part.kind == PartKind.Body)
                        for (int i = part.start; i < part.end; i++) top = Mathf.Max(top, tpl.vertices[i].y);
                tpl.rollAxis = (top - legLen) * 0.5f;
                Object.DestroyImmediate(m);
                return tpl;
            }
        }

        static float Luminance(Color c) => 0.3f * c.r + 0.6f * c.g + 0.1f * c.b;

        static PlantTemplate _egret;

        // The little white egret that rides on a capybara in the snuggle star (static, ~40 verts, same template
        // units as the animals; drawn with TemplateBatch.Add, so its animation channels are zero).
        public static PlantTemplate Egret
        {
            get
            {
                if (_egret != null) return _egret;
                var b = new ShapeBuilder();
                Color white = new Color(0.97f, 0.97f, 0.94f), yellow = new Color(0.98f, 0.78f, 0.2f);
                b.Lump(new Vector3(0f, 0.05f, -0.01f), new Vector3(0.035f, 0.032f, 0.06f), white);
                b.Cone(new Vector3(0f, 0.055f, -0.06f), new Vector3(0f, -0.2f, -1f), 0.022f, 0.05f, 3, white * 0.94f);
                b.Lump(new Vector3(0f, 0.1f, 0.045f), new Vector3(0.022f, 0.022f, 0.024f), white);
                b.Cone(new Vector3(0f, 0.098f, 0.065f), new Vector3(0f, -0.1f, 1f), 0.009f, 0.04f, 3, yellow);
                var m = b.ToMesh("Egret");
                _egret = new PlantTemplate { vertices = m.vertices, normals = m.normals, colors = m.colors, triangles = m.triangles };
                _egret.sway = new float[_egret.vertices.Length];
                _egret.wing = _egret.sway;
                Object.DestroyImmediate(m);
                return _egret;
            }
        }

        static void FourLegs(Rig r, float x, float zFront, float zRear, float hipY, float size, float footSize, Color col, int sides = 4)
        {
            r.Leg(new Vector3(x, hipY, zFront), new Vector3(x, 0f, zFront), size, footSize, sides, col, 1);
            r.Leg(new Vector3(-x, hipY, zFront), new Vector3(-x, 0f, zFront), size, footSize, sides, col, -1);
            r.Leg(new Vector3(x, hipY, zRear), new Vector3(x, 0f, zRear), size, footSize, sides, col, -1);
            r.Leg(new Vector3(-x, hipY, zRear), new Vector3(-x, 0f, zRear), size, footSize, sides, col, 1);
        }

        static PlantTemplate Build(LifeKind kind, int v, bool young)
        {
            var r = new Rig { headScale = young ? 1.32f : 1f };
            var b = r.b;
            Color eyeDark = new Color(0.06f, 0.05f, 0.05f);
            Color eyeLight = new Color(0.95f, 0.93f, 0.85f);
            switch (kind)
            {
                case LifeKind.Sheep:
                {
                    Color wool = LifeMeshes.SheepWool[v];
                    Color dark = LifeMeshes.SheepDark;
                    r.coat = Markings.Wool;
                    r.headCoat = r.legCoat = Markings.Skin;
                    r.legLen = 0.12f;
                    FourLegs(r, 0.1f, 0.13f, -0.15f, 0.18f, 0.07f, 0.05f, dark, 3);
                    b.Sphere(new Vector3(0f, 0.27f, -0.02f), new Vector3(0.2f, 0.165f, 0.26f), wool, 0);
                    b.Lump(new Vector3(0f, 0.36f, -0.11f), new Vector3(0.15f, 0.085f, 0.16f), wool * 0.96f);
                    b.Lump(new Vector3(0f, 0.26f, 0.16f), new Vector3(0.17f, 0.16f, 0.12f), wool * 1.03f);
                    b.Cone(new Vector3(0f, 0.28f, -0.25f), new Vector3(0f, -0.7f, -0.7f), 0.04f, 0.1f, 3, wool * 0.95f);
                    r.Body();
                    Vector3 ha = new Vector3(0f, 0.32f, 0.2f), hb = new Vector3(0f, 0.265f, 0.41f);
                    r.headPivot = ha;
                    b.Tube(ha, hb, Vector3.up, new Vector2(0.13f, 0.14f), new Vector2(0.085f, 0.085f), 4, dark);
                    int topknot = b.VertexCount;
                    b.Lump(new Vector3(0f, 0.39f, 0.23f), new Vector3(0.075f, 0.05f, 0.07f), wool);
                    r.Paint(topknot, Markings.Wool);
                    b.Cone(new Vector3(0.06f, 0.345f, 0.25f), new Vector3(1f, -0.2f, -0.15f), 0.028f, 0.1f, 3, dark * 1.3f);
                    b.Cone(new Vector3(-0.06f, 0.345f, 0.25f), new Vector3(-1f, -0.2f, -0.15f), 0.028f, 0.1f, 3, dark * 1.3f);
                    r.Eyes(ha, hb, 0.13f, 0.085f, 0.42f, 0.02f, 0.016f, eyeLight);
                    r.Head();
                    break;
                }
                case LifeKind.Goat:
                {
                    Color c = LifeMeshes.GoatHide[v];
                    r.legLen = 0.19f;
                    FourLegs(r, 0.07f, 0.13f, -0.14f, 0.25f, 0.06f, 0.042f, c * 0.78f, 3);
                    b.Tube(new Vector3(0f, 0.3f, -0.2f), new Vector3(0f, 0.32f, 0.18f), Vector3.up, new Vector2(0.2f, 0.22f), new Vector2(0.22f, 0.25f), 6, c);
                    b.Cone(new Vector3(0f, 0.39f, -0.2f), new Vector3(0f, 0.75f, -0.65f), 0.03f, 0.08f, 3, c * 0.9f);
                    r.Body();
                    Vector3 na = new Vector3(0f, 0.35f, 0.12f), nb = new Vector3(0f, 0.49f, 0.23f);
                    b.Tube(na, nb, Vector3.up, new Vector2(0.1f, 0.14f), new Vector2(0.085f, 0.1f), 4, c * 0.97f, false, false);
                    r.Neck(na, nb);
                    Vector3 ha = new Vector3(0f, 0.5f, 0.18f), hb = new Vector3(0f, 0.43f, 0.37f);
                    r.headPivot = nb;
                    b.Tube(ha, hb, Vector3.up, new Vector2(0.1f, 0.11f), new Vector2(0.06f, 0.06f), 4, c * 0.95f);
                    r.TipCone(new Vector3(0.03f, 0.545f, 0.21f), new Vector3(0.3f, 0.6f, -1f), 0.022f, 0.16f, 3, LifeMeshes.Horn, 0.55f);
                    r.TipCone(new Vector3(-0.03f, 0.545f, 0.21f), new Vector3(-0.3f, 0.6f, -1f), 0.022f, 0.16f, 3, LifeMeshes.Horn, 0.55f);
                    b.Cone(new Vector3(0.05f, 0.505f, 0.2f), new Vector3(1f, 0.05f, -0.35f), 0.022f, 0.08f, 3, c * 0.85f);
                    b.Cone(new Vector3(-0.05f, 0.505f, 0.2f), new Vector3(-1f, 0.05f, -0.35f), 0.022f, 0.08f, 3, c * 0.85f);
                    b.Cone(new Vector3(0f, 0.415f, 0.31f), new Vector3(0f, -1f, -0.2f), 0.025f, 0.1f, 3, c * 0.7f);
                    r.Eyes(ha, hb, 0.1f, 0.06f, 0.4f, 0.015f, 0.014f, Luminance(c) > 0.4f ? eyeDark : eyeLight);
                    r.Head();
                    break;
                }
                case LifeKind.Ox:
                {
                    Color c = LifeMeshes.OxHide[v];
                    r.coat = Markings.Hide;
                    r.legLen = 0.27f;
                    FourLegs(r, 0.13f, 0.2f, -0.22f, 0.34f, 0.1f, 0.075f, c * 0.82f);
                    b.Tube(new Vector3(0f, 0.44f, -0.32f), new Vector3(0f, 0.47f, 0.22f), Vector3.up, new Vector2(0.38f, 0.38f), new Vector2(0.45f, 0.47f), 6, c);
                    b.Lump(new Vector3(0f, 0.67f, 0.08f), new Vector3(0.14f, 0.11f, 0.19f), c * 1.08f);
                    r.TipCone(new Vector3(0f, 0.56f, -0.32f), new Vector3(0f, -1f, -0.12f), 0.028f, 0.34f, 3, c * 0.7f, 0.62f);
                    r.Body();
                    Vector3 ha = new Vector3(0f, 0.54f, 0.22f), hb = new Vector3(0f, 0.43f, 0.55f);
                    r.headPivot = ha;
                    b.Tube(ha, hb, Vector3.up, new Vector2(0.23f, 0.24f), new Vector2(0.15f, 0.13f), 4, c * 0.92f);
                    Color muzzle = Color.Lerp(c, new Color(0.85f, 0.75f, 0.65f), 0.55f);
                    int snout = b.VertexCount;
                    b.Tube(new Vector3(0f, 0.428f, 0.54f), new Vector3(0f, 0.41f, 0.61f), Vector3.up, new Vector2(0.155f, 0.125f), new Vector2(0.125f, 0.095f), 4, muzzle, false, true);
                    r.Paint(snout, Markings.Skin);
                    r.TipCone(new Vector3(0.1f, 0.61f, 0.33f), new Vector3(1f, 0.55f, 0.15f), 0.035f, 0.22f, 3, LifeMeshes.Horn, 0.6f);
                    r.TipCone(new Vector3(-0.1f, 0.61f, 0.33f), new Vector3(-1f, 0.55f, 0.15f), 0.035f, 0.22f, 3, LifeMeshes.Horn, 0.6f);
                    b.Cone(new Vector3(0.11f, 0.55f, 0.29f), new Vector3(1f, -0.15f, -0.25f), 0.03f, 0.1f, 3, c * 0.8f);
                    b.Cone(new Vector3(-0.11f, 0.55f, 0.29f), new Vector3(-1f, -0.15f, -0.25f), 0.03f, 0.1f, 3, c * 0.8f);
                    r.Eyes(ha, hb, 0.23f, 0.15f, 0.4f, 0.03f, 0.02f, Luminance(c) > 0.25f ? eyeDark : eyeLight);
                    r.Head();
                    break;
                }
                default:
                {
                    Color c = LifeMeshes.HareFur[v];
                    r.legLen = 0.035f;
                    r.squash = 0.5f;
                    r.legLift = 0f;
                    r.Leg(new Vector3(0.04f, 0.07f, 0.07f), new Vector3(0.04f, 0f, 0.085f), 0.04f, 0.032f, 3, c * 0.9f, 0);
                    r.Leg(new Vector3(-0.04f, 0.07f, 0.07f), new Vector3(-0.04f, 0f, 0.085f), 0.04f, 0.032f, 3, c * 0.9f, 0);
                    b.Sphere(new Vector3(0f, 0.105f, -0.03f), new Vector3(0.08f, 0.078f, 0.13f), c, 0);
                    b.Lump(new Vector3(0f, 0.085f, -0.09f), new Vector3(0.108f, 0.082f, 0.085f), c * 0.95f);
                    int scut = b.VertexCount;
                    b.Lump(new Vector3(0f, 0.1f, -0.17f), new Vector3(0.038f, 0.038f, 0.038f), new Color(0.97f, 0.96f, 0.93f));
                    r.Paint(scut, Markings.Skin);
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                        b.Tube(new Vector3(0.066f * sgn, 0.018f, -0.12f), new Vector3(0.066f * sgn, 0.018f, 0.005f), Vector3.up,
                            new Vector2(0.042f, 0.036f), new Vector2(0.036f, 0.03f), 3, c * 0.88f, false, true);
                    r.Body();
                    Vector3 ha = new Vector3(0f, 0.175f, 0.06f), hb = new Vector3(0f, 0.155f, 0.2f);
                    r.headPivot = ha;
                    b.Tube(ha, hb, Vector3.up, new Vector2(0.1f, 0.1f), new Vector2(0.055f, 0.06f), 4, c * 1.05f);
                    r.TipCone(new Vector3(0.028f, 0.215f, 0.085f), new Vector3(0.12f, 1f, -0.32f), 0.03f, 0.18f, 3, c * 0.92f, 0.7f);
                    r.TipCone(new Vector3(-0.028f, 0.215f, 0.085f), new Vector3(-0.12f, 1f, -0.32f), 0.03f, 0.18f, 3, c * 0.92f, 0.7f);
                    r.Eyes(ha, hb, 0.1f, 0.055f, 0.45f, 0.012f, 0.013f, eyeDark);
                    r.Head();
                    break;
                }
            }
            return r.Finish(kind + (young ? "_young_" : "_detail_") + v);
        }
    }
}
