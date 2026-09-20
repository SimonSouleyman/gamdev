using UnityEngine;

namespace Drift.Life
{
    public enum BuildingKind { Campfire, Tent, Hut, Well, Garden, Dock, House, Hall, Windmill, Lighthouse, StoneHouse, Market, Field, Shrine }

    public class BuildingSpec
    {
        public BuildingKind kind;
        // Footprint (x, z), body height and the spacing radius other buildings keep clear.
        public float width, depth, height, radius;
        public float buildTime;
        public bool burns, plot, natural;
        public int homes;
        public PlantTemplate[] body;
        public PlantTemplate[] glow;
        public PlantTemplate foundation, scaffold;
        public int MaxVerts
        {
            get
            {
                int m = 0;
                for (int v = 0; v < body.Length; v++)
                    m = Mathf.Max(m, body[v].vertices.Length + (glow[v] != null ? glow[v].vertices.Length : 0));
                return m;
            }
        }
    }

    // Templates of the island folk's buildings, props and settlers (IslandSettlementSystem), in world units at
    // scale 1: a settler is 0.065 u tall, a hut 0.24 x 0.28 x 0.25, the lighthouse 0.7 (the system draws them at
    // buildingScale 0.55 / settlerScale 0.75). Everything is built from
    // flat triangles through ShapeBuilder.Fin, so winding is decided by the outward vector of each face.
    // Windows and lamps live in a separate "glow" template that the system blends towards an HDR warm colour
    // with the night; flags carry a sway weight for the wind channel of Drift/VertexColor.
    public static class SettlementMeshes
    {
        public const int KindCount = 14;
        public const int Variants = 3;
        public const int SettlerVariants = 8;
        public const float SettlerHeight = 0.065f;

        static BuildingSpec[] _specs;
        static PlantTemplate[,] _settlers;
        static PlantTemplate _log, _rod, _flame, _sails, _boat, _beam, _smoke;

        static readonly Color Wood = new Color(0.62f, 0.45f, 0.28f);
        static readonly Color DarkWood = new Color(0.36f, 0.24f, 0.14f);
        static readonly Color Thatch = new Color(0.86f, 0.72f, 0.38f);
        static readonly Color Stone = new Color(0.72f, 0.7f, 0.66f);
        static readonly Color White = new Color(0.94f, 0.91f, 0.84f);
        static readonly Color Window = new Color(0.2f, 0.25f, 0.33f);
        static readonly Color Soil = new Color(0.42f, 0.3f, 0.2f);

        public static BuildingSpec Spec(BuildingKind kind)
        {
            if (_specs == null) BuildSpecs();
            return _specs[(int)kind];
        }

        public static PlantTemplate Settler(int variant, int pose)
        {
            if (_settlers == null)
            {
                _settlers = new PlantTemplate[SettlerVariants, 2];
                for (int v = 0; v < SettlerVariants; v++)
                    for (int p = 0; p < 2; p++) _settlers[v, p] = BuildSettler(v, p);
            }
            return _settlers[Mathf.Abs(variant) % SettlerVariants, Mathf.Clamp(pose, 0, 1)];
        }

        public static PlantTemplate Log => _log ??= Make(b => Post(b, new Vector3(-0.3f, 0.66f, 0.08f), new Vector3(0.3f, 0.66f, 0.08f), 0.07f, DarkWood));
        public static PlantTemplate Rod => _rod ??= Make(b => Quad2(b, new Vector3(0.16f, 0.5f, 0.1f), new Vector3(0.2f, 0.5f, 0.1f), new Vector3(0.22f, 1.15f, 1.1f), new Vector3(0.18f, 1.15f, 1.1f), Vector3.up, DarkWood));

        public static PlantTemplate Flame => _flame ??= Make(b =>
        {
            b.Cone(new Vector3(0f, 0.02f, 0f), Vector3.up, 0.032f, 0.095f, 4, new Color(1f, 0.5f, 0.12f));
            b.Cone(new Vector3(0.004f, 0.02f, 0.003f), Vector3.up, 0.017f, 0.06f, 3, new Color(1f, 0.9f, 0.4f));
        });

        public static PlantTemplate Smoke => _smoke ??= Make(b =>
        {
            b.Cone(Vector3.zero, Vector3.up, 0.02f, 0.012f, 6, new Color(0.95f, 0.95f, 0.95f));
            b.Cone(Vector3.zero, Vector3.down, 0.02f, 0.01f, 6, new Color(0.85f, 0.85f, 0.87f));
        });

        // Four lattice blades in the local XY plane facing +Z; the system rolls them about Z.
        public static PlantTemplate Sails => _sails ??= Make(b =>
        {
            Color cloth = new Color(0.95f, 0.92f, 0.82f);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f;
                Vector3 r = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Vector3 s = new Vector3(-r.y, r.x, 0f);
                Quad2(b, r * 0.02f - s * 0.007f, r * 0.29f - s * 0.007f, r * 0.29f + s * 0.007f, r * 0.02f + s * 0.007f, Vector3.forward, DarkWood);
                Quad2(b, r * 0.08f + s * 0.007f, r * 0.28f + s * 0.007f, r * 0.28f + s * 0.075f, r * 0.08f + s * 0.06f, Vector3.forward, cloth);
            }
        });

        public static PlantTemplate Boat => _boat ??= Make(b =>
        {
            Color hull = new Color(0.55f, 0.3f, 0.2f);
            Color inside = new Color(0.78f, 0.62f, 0.4f);
            float l = 0.125f, w = 0.05f, h = 0.04f;
            Vector3 bow = new Vector3(0f, h, l), stern = new Vector3(0f, h, -l * 0.8f);
            Vector3 pl = new Vector3(-w, h, 0.01f), pr = new Vector3(w, h, 0.01f), keel = new Vector3(0f, -0.012f, 0f);
            b.Fin(bow, pl, keel, new Vector3(-1f, -0.3f, 0.5f), hull);
            b.Fin(pl, stern, keel, new Vector3(-1f, -0.3f, -0.5f), hull);
            b.Fin(bow, keel, pr, new Vector3(1f, -0.3f, 0.5f), hull);
            b.Fin(pr, keel, stern, new Vector3(1f, -0.3f, -0.5f), hull);
            Vector3 drop = Vector3.down * 0.012f;
            b.Fin(bow + drop, pr + drop, pl + drop, Vector3.up, inside);
            b.Fin(pl + drop, pr + drop, stern + drop, Vector3.up, inside);
            Quad(b, new Vector3(-w * 0.8f, h - 0.004f, -0.02f), new Vector3(w * 0.8f, h - 0.004f, -0.02f), new Vector3(w * 0.8f, h - 0.004f, 0f), new Vector3(-w * 0.8f, h - 0.004f, 0f), Vector3.up, DarkWood);
        });

        // Lighthouse sweep: two opposite flat wedges, both sides visible.
        public static PlantTemplate Beam => _beam ??= Make(b =>
        {
            Color c = new Color(1f, 0.95f, 0.7f);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 tipL = new Vector3(-0.09f, 0f, 1.1f * s), tipR = new Vector3(0.09f, 0f, 1.1f * s);
                b.Fin(Vector3.zero, tipL, tipR, Vector3.up, c);
                b.Fin(Vector3.zero, tipL, tipR, Vector3.down, c);
            }
        });

        // ---------------------------------------------------------------- primitives

        delegate void Shape(ShapeBuilder b);

        static PlantTemplate Make(Shape shape)
        {
            var b = new ShapeBuilder();
            shape(b);
            return ToTemplate(b, null);
        }

        static PlantTemplate ToTemplate(ShapeBuilder b, float[] sway)
        {
            var m = b.ToMesh("SettlementPart");
            var t = new PlantTemplate { vertices = m.vertices, normals = m.normals, colors = m.colors, triangles = m.triangles };
            t.sway = new float[t.vertices.Length];
            // A negative entry marks a flag vertex: its weight grows with the distance from the pole at x = 0.
            if (sway != null)
                for (int i = 0; i < sway.Length && i < t.sway.Length; i++)
                    if (sway[i] < 0f) t.sway[i] = Mathf.Clamp01(Mathf.Abs(t.vertices[i].x) / 0.11f) * 0.55f;
            t.wing = new float[t.vertices.Length];
            Object.DestroyImmediate(m);
            return t;
        }

        static void Quad(ShapeBuilder b, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 outward, Color c)
        {
            b.Fin(p0, p1, p2, outward, c);
            b.Fin(p0, p2, p3, outward, c);
        }

        static void Quad2(ShapeBuilder b, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 outward, Color c)
        {
            Quad(b, p0, p1, p2, p3, outward, c);
            Quad(b, p0, p1, p2, p3, -outward, c * 0.9f);
        }

        // Four side faces of a box from y0 to y1, no top or bottom (a roof or the ground covers them).
        static void Walls(ShapeBuilder b, float cx, float cz, float y0, float y1, float w, float d, Color c)
        {
            float x0 = cx - w * 0.5f, x1 = cx + w * 0.5f, z0 = cz - d * 0.5f, z1 = cz + d * 0.5f;
            Quad(b, new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.forward, c);
            Quad(b, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), Vector3.back, c * 0.94f);
            Quad(b, new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), Vector3.left, c * 0.97f);
            Quad(b, new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.right, c * 0.97f);
        }

        // Gable roof with the ridge along z: two roof faces with an overhang and the two gable triangles.
        static void Gable(ShapeBuilder b, float cx, float cz, float yEave, float yRidge, float w, float d, float over, Color roof, Color gable)
        {
            float slope = (yRidge - yEave) / (w * 0.5f);
            float xl = cx - w * 0.5f - over, xr = cx + w * 0.5f + over, ye = yEave - over * slope;
            float z0 = cz - d * 0.5f - over, z1 = cz + d * 0.5f + over;
            Quad(b, new Vector3(xl, ye, z0), new Vector3(xl, ye, z1), new Vector3(cx, yRidge, z1), new Vector3(cx, yRidge, z0), new Vector3(-1f, 1f, 0f), roof);
            Quad(b, new Vector3(xr, ye, z0), new Vector3(xr, ye, z1), new Vector3(cx, yRidge, z1), new Vector3(cx, yRidge, z0), new Vector3(1f, 1f, 0f), roof * 0.9f);
            b.Fin(new Vector3(cx - w * 0.5f, yEave, cz + d * 0.5f), new Vector3(cx + w * 0.5f, yEave, cz + d * 0.5f), new Vector3(cx, yRidge, cz + d * 0.5f), Vector3.forward, gable);
            b.Fin(new Vector3(cx - w * 0.5f, yEave, cz - d * 0.5f), new Vector3(cx + w * 0.5f, yEave, cz - d * 0.5f), new Vector3(cx, yRidge, cz - d * 0.5f), Vector3.back, gable * 0.94f);
        }

        // Tapered n-gon tower section; a flat face looks along +z when seg is even and phase is 0.
        static void Frustum(ShapeBuilder b, Vector3 c, float r0, float r1, float y0, float y1, int seg, Color col, bool cap)
        {
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg, am = (a0 + a1) * 0.5f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 outward = new Vector3(Mathf.Cos(am), 0f, Mathf.Sin(am));
                float shade = 0.92f + 0.08f * Mathf.Cos(am - 0.9f);
                Quad(b, c + d0 * r0 + Vector3.up * y0, c + d1 * r0 + Vector3.up * y0, c + d1 * r1 + Vector3.up * y1, c + d0 * r1 + Vector3.up * y1, outward, col * shade);
                if (cap) b.Fin(c + Vector3.up * y1, c + d0 * r1 + Vector3.up * y1, c + d1 * r1 + Vector3.up * y1, Vector3.up, col);
            }
        }

        static void Disc(ShapeBuilder b, Vector3 c, float r, int seg, Color col)
        {
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                b.Fin(c, c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r, c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r, Vector3.up, col);
            }
        }

        // Three-sided beam between two points (18 verts).
        static void Post(ShapeBuilder b, Vector3 p0, Vector3 p1, float r, Color col)
        {
            Vector3 axis = (p1 - p0).normalized;
            Vector3 t = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 bt = Vector3.Cross(axis, t);
            for (int i = 0; i < 3; i++)
            {
                float a0 = i * Mathf.PI * 2f / 3f + 0.5f, a1 = (i + 1) * Mathf.PI * 2f / 3f + 0.5f, am = (a0 + a1) * 0.5f;
                Vector3 d0 = (t * Mathf.Cos(a0) + bt * Mathf.Sin(a0)) * r, d1 = (t * Mathf.Cos(a1) + bt * Mathf.Sin(a1)) * r;
                Quad(b, p0 + d0, p0 + d1, p1 + d1, p1 + d0, t * Mathf.Cos(am) + bt * Mathf.Sin(am), col * (0.9f + 0.1f * i));
            }
        }

        // The houses are drawn at about half their modelled size (IslandSettlementSystem.buildingScale), where a
        // true-to-scale window is a speck: windows are modelled oversized so their night glow still reads.
        const float WindowGrow = 1.45f;

        static void WindowX(ShapeBuilder b, float x, float y, float z, float w, float h, float side)
        {
            w *= WindowGrow; h *= WindowGrow;
            Quad(b, new Vector3(x, y - h * 0.5f, z - w * 0.5f), new Vector3(x, y - h * 0.5f, z + w * 0.5f), new Vector3(x, y + h * 0.5f, z + w * 0.5f), new Vector3(x, y + h * 0.5f, z - w * 0.5f), Vector3.right * side, Window);
        }

        static void WindowZ(ShapeBuilder b, float x, float y, float z, float w, float h, float side)
        {
            w *= WindowGrow; h *= WindowGrow;
            Quad(b, new Vector3(x - w * 0.5f, y - h * 0.5f, z), new Vector3(x + w * 0.5f, y - h * 0.5f, z), new Vector3(x + w * 0.5f, y + h * 0.5f, z), new Vector3(x - w * 0.5f, y + h * 0.5f, z), Vector3.forward * side, Window);
        }

        static void Door(ShapeBuilder b, float z, float w, float h, Color c)
        {
            Quad(b, new Vector3(-w * 0.5f, -0.02f, z), new Vector3(w * 0.5f, -0.02f, z), new Vector3(w * 0.5f, h, z), new Vector3(-w * 0.5f, h, z), Vector3.forward, c);
        }

        // ---------------------------------------------------------------- settlers

        static PlantTemplate BuildSettler(int v, int pose)
        {
            Color[] tunic =
            {
                new Color(0.85f, 0.25f, 0.2f), new Color(0.25f, 0.45f, 0.85f), new Color(0.95f, 0.78f, 0.25f), new Color(0.3f, 0.65f, 0.35f),
                new Color(0.65f, 0.4f, 0.8f), new Color(0.95f, 0.55f, 0.2f), new Color(0.92f, 0.9f, 0.85f), new Color(0.2f, 0.62f, 0.65f)
            };
            Color[] skin = { new Color(0.96f, 0.78f, 0.62f), new Color(0.8f, 0.58f, 0.42f), new Color(0.58f, 0.4f, 0.28f) };
            Color[] hair = { new Color(0.25f, 0.17f, 0.1f), new Color(0.9f, 0.8f, 0.45f), new Color(0.12f, 0.1f, 0.1f), new Color(0.7f, 0.35f, 0.15f), new Color(0.85f, 0.85f, 0.85f) };
            var b = new ShapeBuilder();
            float k = SettlerHeight;
            b.Cone(Vector3.zero, Vector3.up, 0.31f * k, 0.72f * k, 4, tunic[v]);
            Vector3 head = new Vector3(0f, 0.78f * k, 0f);
            b.Cone(head, Vector3.up, 0.24f * k, 0.22f * k, 4, hair[(v * 3 + 1) % hair.Length]);
            b.Cone(head, Vector3.down, 0.24f * k, 0.22f * k, 3, skin[v % skin.Length]);
            Color arm = skin[v % skin.Length];
            // Pose 1 (waving, cheering): one raised arm, a single triangle seen from both sides (39 verts in all).
            if (pose >= 1)
            {
                Vector3 a0 = new Vector3(0.1f, 0.52f, 0f) * k, a1 = new Vector3(0.24f, 0.44f, 0f) * k, a2 = new Vector3(0.44f, 1.04f, 0f) * k;
                b.Fin(a0, a1, a2, Vector3.forward, arm);
                b.Fin(a0, a1, a2, Vector3.back, arm);
            }
            return ToTemplate(b, null);
        }

        // ---------------------------------------------------------------- buildings

        static void BuildSpecs()
        {
            _specs = new BuildingSpec[KindCount];
            Add(BuildingKind.Campfire, 0.16f, 0.16f, 0.06f, 0.2f, 30f, false, false, 0, false, Campfire);
            Add(BuildingKind.Tent, 0.17f, 0.22f, 0.13f, 0.17f, 45f, true, false, 1, true, Tent);
            Add(BuildingKind.Hut, 0.24f, 0.28f, 0.25f, 0.23f, 150f, true, false, 1, true, Hut);
            Add(BuildingKind.Well, 0.12f, 0.12f, 0.16f, 0.12f, 90f, false, false, 0, true, Well);
            Add(BuildingKind.Garden, 0.34f, 0.28f, 0.05f, 0.24f, 60f, false, true, 0, false, Garden);
            Add(BuildingKind.Dock, 0.12f, 0.7f, 0.1f, 0.3f, 150f, true, false, 0, false, Dock);
            Add(BuildingKind.House, 0.28f, 0.34f, 0.33f, 0.27f, 200f, true, false, 2, true, House);
            Add(BuildingKind.Hall, 0.4f, 0.56f, 0.42f, 0.4f, 300f, true, false, 2, true, Hall);
            Add(BuildingKind.Windmill, 0.2f, 0.2f, 0.46f, 0.3f, 300f, true, false, 0, true, Windmill);
            Add(BuildingKind.Lighthouse, 0.18f, 0.18f, 0.7f, 0.2f, 300f, false, false, 0, true, Lighthouse);
            Add(BuildingKind.StoneHouse, 0.27f, 0.32f, 0.33f, 0.27f, 240f, false, false, 2, true, StoneHouse);
            Add(BuildingKind.Market, 0.6f, 0.6f, 0.12f, 0.38f, 240f, true, false, 0, false, Market);
            Add(BuildingKind.Field, 0.5f, 0.48f, 0.15f, 0.36f, 90f, false, true, 0, false, Field);
            Add(BuildingKind.Shrine, 0.18f, 0.18f, 0.22f, 0.2f, 240f, false, false, 0, true, Shrine);
            Spec(BuildingKind.Dock).natural = true;
            Spec(BuildingKind.Lighthouse).natural = true;
            Spec(BuildingKind.Shrine).natural = true;
        }

        delegate void BodyShape(ShapeBuilder body, ShapeBuilder glow, int variant, out float[] sway);

        static void Add(BuildingKind kind, float w, float d, float h, float radius, float buildTime, bool burns, bool plot, int homes, bool scaffold, BodyShape shape)
        {
            var s = new BuildingSpec
            {
                kind = kind, width = w, depth = d, height = h, radius = radius, buildTime = buildTime, burns = burns, plot = plot, homes = homes,
                body = new PlantTemplate[Variants], glow = new PlantTemplate[Variants]
            };
            for (int v = 0; v < Variants; v++)
            {
                var body = new ShapeBuilder();
                var glow = new ShapeBuilder();
                shape(body, glow, v, out float[] sway);
                s.body[v] = ToTemplate(body, sway);
                s.glow[v] = glow.VertexCount > 0 ? ToTemplate(glow, null) : null;
            }
            if (scaffold)
            {
                s.foundation = Make(b =>
                {
                    b.Box(new Vector3(0f, -0.025f, 0f), new Vector3(w + 0.05f, 0.08f, d + 0.05f), Stone * 0.9f, false);
                    Quad(b, new Vector3(-w * 0.5f, 0.017f, -d * 0.5f), new Vector3(w * 0.5f, 0.017f, -d * 0.5f), new Vector3(w * 0.5f, 0.017f, d * 0.5f), new Vector3(-w * 0.5f, 0.017f, d * 0.5f), Vector3.up, Soil);
                });
                s.scaffold = Make(b =>
                {
                    float x = w * 0.5f + 0.02f, z = d * 0.5f + 0.02f, top = h * 0.92f;
                    Color pole = new Color(0.8f, 0.64f, 0.4f);
                    for (int sx = -1; sx <= 1; sx += 2)
                        for (int sz = -1; sz <= 1; sz += 2)
                            Post(b, new Vector3(x * sx, -0.06f, z * sz), new Vector3(x * sx, top, z * sz), 0.009f, pole);
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        Post(b, new Vector3(-x, top, z * sgn), new Vector3(x, top, z * sgn), 0.008f, pole);
                        Post(b, new Vector3(x * sgn, top, -z), new Vector3(x * sgn, top, z), 0.008f, pole);
                        Post(b, new Vector3(x * sgn, top * 0.12f, -z), new Vector3(x * sgn, top * 0.62f, z), 0.007f, pole * 0.9f);
                    }
                });
            }
            _specs[(int)kind] = s;
        }

        static void Campfire(ShapeBuilder b, ShapeBuilder glow, int v, out float[] sway)
        {
            sway = null;
            Frustum(b, Vector3.zero, 0.06f, 0.048f, -0.03f, 0.018f, 6, Stone * 0.85f, false);
            Disc(b, new Vector3(0f, 0.012f, 0f), 0.05f, 6, new Color(0.16f, 0.13f, 0.11f));
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f + v;
                Post(b, new Vector3(Mathf.Cos(a) * 0.04f, 0.01f, Mathf.Sin(a) * 0.04f), new Vector3(-Mathf.Cos(a) * 0.008f, 0.06f, -Mathf.Sin(a) * 0.008f), 0.009f, DarkWood);
            }
            for (int i = 0; i < 2; i++)
            {
                float a = 0.8f + i * 2.4f + v * 0.7f;
                Vector3 c = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.125f;
                Vector3 t = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.05f;
                Post(b, c - t + Vector3.up * 0.012f, c + t + Vector3.up * 0.012f, 0.014f, Wood * 0.9f);
            }
        }

        static void Tent(ShapeBuilder b, ShapeBuilder glow, int v, out float[] sway)
        {
            sway = null;
            Color[] canvas = { new Color(0.93f, 0.88f, 0.74f), new Color(0.75f, 0.85f, 0.92f), new Color(0.95f, 0.72f, 0.6f) };
            float w = 0.17f, d = 0.22f, h = 0.13f;
            Gable(b, 0f, 0f, -0.04f, h, w, d, 0f, canvas[v], canvas[v] * 0.85f);
            b.Fin(new Vector3(-0.035f, -0.02f, d * 0.5f + 0.002f), new Vector3(0.035f, -0.02f, d * 0.5f + 0.002f), new Vector3(0f, h * 0.75f, d * 0.5f + 0.002f), Vector3.forward, new Color(0.25f, 0.18f, 0.14f));
            Post(b, new Vector3(0f, h - 0.004f, -d * 0.5f - 0.025f), new Vector3(0f, h - 0.004f, d * 0.5f + 0.035f), 0.006f, DarkWood);
        }

        static void Hut(ShapeBuilder b, ShapeBuilder glow, int v, out float[] sway)
        {
            sway = null;
            Color[] walls = { new Color(0.86f, 0.76f, 0.58f), new Color(0.78f, 0.66f, 0.48f), new Color(0.9f, 0.82f, 0.66f) };
            Color[] roofs = { Thatch, new Color(0.82f, 0.62f, 0.3f), new Color(0.93f, 0.8f, 0.45f) };
            float w = 0.24f, d = 0.28f, wall = 0.12f, ridge = 0.25f;
            Walls(b, 0f, 0f, -0.07f, wall, w, d, walls[v]);
            Gable(b, 0f, 0f, wall, ridge, w, d, 0.035f, roofs[v], walls[v] * 0.95f);
            Door(b, d * 0.5f + 0.002f, 0.06f, 0.085f, DarkWood);
            float cx = v == 1 ? -0.06f : 0.06f;
            b.Box(new Vector3(cx, 0.235f, -0.06f), new Vector3(0.04f, 0.09f, 0.04f), Stone, false);
            WindowX(glow, w * 0.5f + 0.002f, 0.07f, 0.02f, 0.055f, 0.04f, 1f);
            WindowX(glow, -w * 0.5f - 0.002f, 0.07f, -0.03f, 0.055f, 0.04f, -1f);
            WindowZ(glow, 0f, 0.165f, d * 0.5f + 0.002f, 0.04f, 0.035f, 1f);
        }

        static void House(ShapeBuilder b, ShapeBuilder glow, int v, out float[] sway)
        {
            sway = null;
            Color[] roofs = { new Color(0.8f, 0.36f, 0.24f), new Color(0.36f, 0.47f, 0.62f), new Color(0.42f, 0.56f, 0.36f) };
            float w = 0.28f, d = 0.34f, wall = 0.17f, ridge = 0.33f, z = d * 0.5f + 0.002f;
            Walls(b, 0f, 0f, -0.07f, wall, w, d, White);
            Gable(b, 0f, 0f, wall, ridge, w, d, 0.035f, roofs[v], White * 0.97f);
            Door(b, z, 0.065f, 0.09f, DarkWood);
            Quad(b, new Vector3(-w * 0.5f, 0.1f, z), new Vector3(w * 0.5f, 0.1f, z), new Vector3(w * 0.5f, 0.115f, z), new Vector3(-w * 0.5f, 0.115f, z), Vector3.forward, DarkWood);
            Quad(b, new Vector3(-w * 0.5f, -0.02f, z), new Vector3(-w * 0.5f + 0.015f, -0.02f, z), new Vector3(-w * 0.5f + 0.015f, wall, z), new Vector3(-w * 0.5f, wall, z), Vector3.forward, DarkWood);
            Quad(b, new Vector3(w * 0.5f - 0.015f, -0.02f, z), new Vector3(w * 0.5f, -0.02f, z), new Vector3(w * 0.5f, wall, z), new Vector3(w * 0.5f - 0.015f, wall, z), Vector3.forward, DarkWood);
            b.Box(new Vector3(v == 2 ? 0.07f : -0.07f, 0.31f, -0.08f), new Vector3(0.045f, 0.1f, 0.045f), new Color(0.6f, 0.34f, 0.28f), false);
            WindowX(glow, w * 0.5f + 0.002f, 0.08f, 0.07f, 0.06f, 0.05f, 1f);
            WindowX(glow, w * 0.5f + 0.002f, 0.08f, -0.07f, 0.06f, 0.05f, 1f);
            WindowX(glow, -w * 0.5f - 0.002f, 0.08f, 0.07f, 0.06f, 0.05f, -1f);
            WindowX(glow, -w * 0.5f - 0.002f, 0.08f, -0.07f, 0.06f, 0.05f, -1f);
            WindowZ(glow, 0f, 0.215f, z, 0.05f, 0.045f, 1f);
        }

        static void Hall(ShapeBuilder b, ShapeBuilder glow, int v, out float[] sway)
        {
            Color[] roofs = { new Color(0.62f, 0.2f, 0.18f), new Color(0.25f, 0.36f, 0.56f), new Color(0.5f, 0.3f, 0.16f) };
            float w = 0.4f, d = 0.56f, wall = 0.2f, ridge = 0.42f, z = d * 0.5f + 0.002f;
            Walls(b, 0f, 0f, -0.07f, wall, w, d, new Color(0.76f, 0.58f, 0.38f));
            Gable(b, 0f, 0f, wall, ridge, w, d, 0.045f, roofs[v], White);
            Door(b, z, 0.11f, 0.13f, DarkWood);
            Gable(b, 0f, d * 0.5f + 0.05f, 0.15f, 0.2f, 0.2f, 0.1f, 0.01f, roofs[v] * 0.9f, White);
            Post(b, new Vector3(-0.09f, -0.06f, d * 0.5f + 0.09f), new Vector3(-0.09f, 0.15f, d * 0.5f + 0.09f), 0.011f, DarkWood);
            Post(b, new Vector3(0.09f, -0.06f, d * 0.5f + 0.09f), new Vector3(0.09f, 0.15f, d * 0.5f + 0.09f), 0.011f, DarkWood);
            Post(b, new Vector3(0f, ridge - 0.01f, d * 0.5f), new Vector3(0f, ridge + 0.17f, d * 0.5f), 0.006f, DarkWood);
            int flag = b.VertexCount;
            Color[] flags = { new Color(0.95f, 0.8f, 0.2f), new Color(0.9f, 0.3f, 0.25f), new Color(0.3f, 0.7f, 0.85f) };
            Quad2(b, new Vector3(0f, ridge + 0.1f, d * 0.5f), new Vector3(0.11f, ridge + 0.115f, d * 0.5f), new Vector3(0.11f, ridge + 0.155f, d * 0.5f), new Vector3(0f, ridge + 0.17f, d * 0.5f), Vector3.forward, flags[v]);
            sway = new float[b.VertexCount];
            for (int i = flag; i < sway.Length; i++) sway[i] = -1f;
            for (int s = -1; s <= 1; s += 2)
            {
                WindowX(glow, (w * 0.5f + 0.002f) * s, 0.09f, 0.13f, 0.07f, 0.06f, s);
                WindowX(glow, (w * 0.5f + 0.002f) * s, 0.09f, -0.13f, 0.07f, 0.06f, s);
            }
            WindowZ(glow, 0f, 0.28f, z, 0.06f, 0.06f, 1f);
        }

        static void Well(ShapeBuilder b, ShapeBuilder glow, int v, out float[] sway)
        {
            sway = null;
            Frustum(b, Vector3.zero, 0.058f, 0.052f, -0.05f, 0.05f, 6, Stone, false);
            Disc(b, new Vector3(0f, 0.035f, 0f), 0.05f, 6, new Color(0.25f, 0.5f, 0.75f));
            Post(b, new Vector3(-0.05f, 0.02f, 0f), new Vector3(-0.05f, 0.13f, 0f), 0.008f, DarkWood);
            Post(b, new Vector3(0.05f, 0.02f, 0f), new Vector3(0.05f, 0.13f, 0f), 0.008f, DarkWood);
            Color[] roofs = { new Color(0.8f, 0.36f, 0.24f), new Color(0.72f, 0.3f, 0.22f), new Color(0.85f, 0.45f, 0.25f) };
            Gable(b, 0f, 0f, 0.12f, 0.19f, 0.1f, 0.15f, 0.012f, roofs[v], DarkWood);
        }

        static void Garden(ShapeBuilder b, ShapeBuilder glow, int v, out float[] sway)
        {
            sway = null;
            float w = 0.34f, d = 0.28f;
            b.Box(new Vector3(0f, -0.02f, 0f), new Vector3(w, 0.07f, d), Soil, false);
            Color[] crops = { new Color(0.35f, 0.7f, 0.3f), new Color(0.6f, 0.78f, 0.3f), new Color(0.95f, 0.6f, 0.2f) };
            for (int r = 0; r < 3; r++)
            {
                float z = (r - 1) * 0.08f;
                Post(b, new Vector3(-w * 0.5f + 0.04f, 0.022f, z), new Vector3(w * 0.5f - 0.04f, 0.022f, z), 0.02f, crops[(r + v) % 3]);
            }
            Color fence = new Color(0.82f, 0.7f, 0.5f);
            float x0 = -w * 0.5f - 0.015f, x1 = w * 0.5f + 0.015f, z0 = -d * 0.5f - 0.015f, z1 = d * 0.5f + 0.015f, y0 = 0.03f, y1 = 0.048f;
            Quad2(b, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), Vector3.back, fence);
            Quad2(b, new Vector3(x0, y0, z1), new Vector3(-0.04f, y0, z1), new Vector3(-0.04f, y1, z1), new Vector3(x0, y1, z1), Vector3.forward, fence);
            Quad2(b, new Vector3(0.04f, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(0.04f, y1, z1), Vector3.forward, fence);
            Quad2(b, new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), Vector3.left, fence);
            Quad2(b, new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.right, fence);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    b.Cone(new Vector3(sx > 0 ? x1 : x0, -0.03f, sz > 0 ? z1 : z0), Vector3.up, 0.012f, 0.1f, 3, fence * 0.85f);
        }

        // Local +z points out to sea; the origin is the shore end at water level (the system places it at y = 0).
        static void Dock(ShapeBuilder b, ShapeBuilder glow, int v, out float[] sway)
        {
            sway = null;
            Color plank = new Color(0.72f, 0.56f, 0.38f);
            b.Box(new Vector3(0f, 0.062f, 0.27f), new Vector3(0.12f, 0.02f, 0.7f), plank);
            for (int i = 0; i < 3; i++)
                for (int s = -1; s <= 1; s += 2)
                    Post(b, new Vector3(0.06f * s, -0.3f, 0.12f + i * 0.24f), new Vector3(0.06f * s, 0.1f, 0.12f + i * 0.24f), 0.011f, DarkWood);
            b.Box(new Vector3(-0.025f, 0.09f, 0.5f), new Vector3(0.04f, 0.035f, 0.04f), Wood * 1.1f, false);
        }

        static void Windmill(ShapeBuilder b, ShapeBuilder glow, int v, out float[] sway)
        {
            sway = null;
            Color[] tower = { White, Stone, new Color(0.9f, 0.82f, 0.68f) };
            Color[] cap = { new Color(0.55f, 0.3f, 0.2f), new Color(0.62f, 0.2f, 0.18f), new Color(0.3f, 0.4f, 0.55f) };
            Frustum(b, Vector3.zero, 0.105f, 0.07f, -0.07f, 0.36f, 6, tower[v], false);
            b.Cone(new Vector3(0f, 0.36f, 0f), Vector3.up, 0.088f, 0.1f, 6, cap[v]);
            Quad(b, new Vector3(-0.03f, -0.02f, 0.091f), new Vector3(0.03f, -0.02f, 0.091f), new Vector3(0.03f, 0.085f, 0.083f), new Vector3(-0.03f, 0.085f, 0.083f), Vector3.forward, DarkWood);
            Post(b, new Vector3(0f, 0.33f, 0.04f), new Vector3(0f, 0.33f, 0.125f), 0.012f, DarkWood);
            Quad(glow, new Vector3(-0.02f, 0.18f, 0.0775f), new Vector3(0.02f, 0.18f, 0.0775f), new Vector3(0.02f, 0.225f, 0.0745f), new Vector3(-0.02f, 0.225f, 0.0745f), Vector3.forward, Window);
        }

        public static readonly Vector3 SailHub = new Vector3(0f, 0.33f, 0.135f);
        public static readonly Vector3 LampCentre = new Vector3(0f, 0.6f, 0f);

        static void Lighthouse(ShapeBuilder b, ShapeBuilder glow, int v, out float[] sway)
        {
            sway = null;
            Color[] band = { new Color(0.85f, 0.22f, 0.2f), new Color(0.22f, 0.4f, 0.7f), new Color(0.85f, 0.22f, 0.2f) };
            Frustum(b, Vector3.zero, 0.09f, 0.074f, -0.08f, 0.2f, 6, White, false);
            Frustum(b, Vector3.zero, 0.074f, 0.062f, 0.2f, 0.4f, 6, band[v], false);
            Frustum(b, Vector3.zero, 0.062f, 0.053f, 0.4f, 0.56f, 6, White, false);
            Disc(b, new Vector3(0f, 0.56f, 0f), 0.08f, 6, new Color(0.3f, 0.3f, 0.32f));
            b.Cone(new Vector3(0f, 0.64f, 0f), Vector3.up, 0.055f, 0.065f, 6, band[v]);
            Quad(b, new Vector3(-0.025f, -0.02f, 0.078f), new Vector3(0.025f, -0.02f, 0.078f), new Vector3(0.025f, 0.07f, 0.074f), new Vector3(-0.025f, 0.07f, 0.074f), Vector3.forward, DarkWood);
            Frustum(glow, Vector3.zero, 0.038f, 0.038f, 0.56f, 0.64f, 6, new Color(0.75f, 0.8f, 0.8f), false);
        }

        static void StoneHouse(ShapeBuilder b, ShapeBuilder glow, int v, out float[] sway)
        {
            sway = null;
            Color[] walls = { Stone, new Color(0.82f, 0.74f, 0.58f), new Color(0.64f, 0.64f, 0.68f) };
            Color[] roofs = { new Color(0.36f, 0.42f, 0.52f), new Color(0.8f, 0.4f, 0.26f), new Color(0.3f, 0.34f, 0.4f) };
            float w = 0.27f, d = 0.32f, wall = 0.18f, ridge = 0.33f, z = d * 0.5f + 0.002f;
            Walls(b, 0f, 0f, -0.07f, wall, w, d, walls[v]);
            Gable(b, 0f, 0f, wall, ridge, w, d, 0.03f, roofs[v], walls[v]);
            Door(b, z, 0.065f, 0.095f, new Color(0.3f, 0.36f, 0.5f));
            b.Box(new Vector3(0.07f, 0.31f, -0.07f), new Vector3(0.05f, 0.11f, 0.05f), walls[v] * 0.85f, false);
            float sx = w * 0.5f, sw = 0.11f;
            Quad(b, new Vector3(sx, -0.07f, -0.12f), new Vector3(sx + sw, -0.07f, -0.12f), new Vector3(sx + sw, 0.08f, -0.12f), new Vector3(sx, 0.13f, -0.12f), Vector3.back, walls[v] * 0.94f);
            Quad(b, new Vector3(sx, -0.07f, 0.06f), new Vector3(sx + sw, -0.07f, 0.06f), new Vector3(sx + sw, 0.08f, 0.06f), new Vector3(sx, 0.13f, 0.06f), Vector3.forward, walls[v]);
            Quad(b, new Vector3(sx + sw, -0.07f, -0.12f), new Vector3(sx + sw, -0.07f, 0.06f), new Vector3(sx + sw, 0.08f, 0.06f), new Vector3(sx + sw, 0.08f, -0.12f), Vector3.right, walls[v] * 0.97f);
            Quad(b, new Vector3(sx, 0.14f, -0.135f), new Vector3(sx + sw + 0.02f, 0.08f, -0.135f), new Vector3(sx + sw + 0.02f, 0.08f, 0.075f), new Vector3(sx, 0.14f, 0.075f), new Vector3(0.4f, 1f, 0f), roofs[v] * 0.92f);
            WindowX(glow, -w * 0.5f - 0.002f, 0.085f, 0.06f, 0.06f, 0.055f, -1f);
            WindowX(glow, -w * 0.5f - 0.002f, 0.085f, -0.07f, 0.06f, 0.055f, -1f);
            WindowZ(glow, 0.085f, 0.09f, z, 0.05f, 0.05f, 1f);
            WindowZ(glow, -0.085f, 0.09f, z, 0.05f, 0.05f, 1f);
            WindowZ(glow, 0f, 0.225f, z, 0.045f, 0.045f, 1f);
        }

        static void Market(ShapeBuilder b, ShapeBuilder glow, int v, out float[] sway)
        {
            sway = null;
            Color paving = new Color(0.82f, 0.76f, 0.62f);
            Frustum(b, Vector3.zero, 0.31f, 0.29f, -0.06f, 0.014f, 8, paving * 0.85f, true);
            Color[] awn = { new Color(0.9f, 0.3f, 0.25f), new Color(0.3f, 0.55f, 0.85f), new Color(0.95f, 0.8f, 0.3f) };
            for (int i = 0; i < 2; i++)
            {
                float a = 2.2f + i * 2.6f + v * 0.4f;
                Vector3 c = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.17f;
                Vector3 f = -c.normalized, r = new Vector3(f.z, 0f, -f.x);
                Vector3 up = Vector3.up;
                Vector3 c0 = c - r * 0.06f - f * 0.03f, c1 = c + r * 0.06f - f * 0.03f, c2 = c + r * 0.06f + f * 0.03f, c3 = c - r * 0.06f + f * 0.03f;
                Quad(b, c0 + up * 0.055f, c1 + up * 0.055f, c2 + up * 0.055f, c3 + up * 0.055f, Vector3.up, Wood * 1.1f);
                Quad(b, c3, c2, c2 + up * 0.055f, c3 + up * 0.055f, f, Wood);
                Quad(b, c0, c3, c3 + up * 0.055f, c0 + up * 0.055f, -r, Wood * 0.95f);
                Quad(b, c1, c2, c2 + up * 0.055f, c1 + up * 0.055f, r, Wood * 0.95f);
                b.Cone(c0, Vector3.up, 0.008f, 0.15f, 3, DarkWood);
                b.Cone(c1, Vector3.up, 0.008f, 0.15f, 3, DarkWood);
                Quad2(b, c0 + up * 0.14f - r * 0.015f, c1 + up * 0.14f + r * 0.015f, c2 + up * 0.105f + r * 0.015f + f * 0.03f, c3 + up * 0.105f - r * 0.015f + f * 0.03f, Vector3.up, awn[(i + v) % 3]);
            }
        }

        // Three terraces stepping up along +z (the system turns +z uphill).
        static void Field(ShapeBuilder b, ShapeBuilder glow, int v, out float[] sway)
        {
            sway = null;
            Color[] crop = { new Color(0.93f, 0.8f, 0.35f), new Color(0.5f, 0.75f, 0.3f), new Color(0.85f, 0.7f, 0.3f) };
            float w = 0.5f;
            for (int t = 0; t < 3; t++)
            {
                float z0 = -0.24f + t * 0.16f, z1 = z0 + 0.16f, y = 0.03f + t * 0.045f, x0 = -w * 0.5f, x1 = w * 0.5f;
                Quad(b, new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1), Vector3.up, crop[(t + v) % 3]);
                Quad(b, new Vector3(x0, -0.08f, z0), new Vector3(x1, -0.08f, z0), new Vector3(x1, y, z0), new Vector3(x0, y, z0), Vector3.back, Stone * 0.9f);
                Quad(b, new Vector3(x0, -0.08f, z0), new Vector3(x0, -0.08f, z1), new Vector3(x0, y, z1), new Vector3(x0, y, z0), Vector3.left, Stone * 0.85f);
                Quad(b, new Vector3(x1, -0.08f, z0), new Vector3(x1, -0.08f, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), Vector3.right, Stone * 0.85f);
            }
            Quad(b, new Vector3(-w * 0.5f, -0.08f, 0.24f), new Vector3(w * 0.5f, -0.08f, 0.24f), new Vector3(w * 0.5f, 0.12f, 0.24f), new Vector3(-w * 0.5f, 0.12f, 0.24f), Vector3.forward, Stone * 0.9f);
            for (int i = 0; i < 3; i++)
                b.Cone(new Vector3(-0.16f + i * 0.16f, 0.03f, -0.16f + (i % 2) * 0.03f), Vector3.up, 0.022f, 0.06f, 3, new Color(0.9f, 0.75f, 0.3f));
        }

        static void Shrine(ShapeBuilder b, ShapeBuilder glow, int v, out float[] sway)
        {
            sway = null;
            Color red = new Color(0.85f, 0.25f, 0.18f);
            b.Box(new Vector3(0f, -0.015f, 0f), new Vector3(0.2f, 0.07f, 0.2f), Stone, false);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Post(b, new Vector3(0.065f * sx, 0.02f, 0.065f * sz), new Vector3(0.065f * sx, 0.15f, 0.065f * sz), 0.01f, red);
            b.Cone(new Vector3(0f, 0.15f, 0f), Vector3.up, 0.14f, 0.075f, 4, new Color(0.3f, 0.34f, 0.4f));
            glow.Cone(new Vector3(0f, 0.08f, 0f), Vector3.up, 0.03f, 0.04f, 4, new Color(0.8f, 0.8f, 0.75f));
            glow.Cone(new Vector3(0f, 0.08f, 0f), Vector3.down, 0.03f, 0.035f, 4, new Color(0.7f, 0.7f, 0.66f));
        }
    }
}
