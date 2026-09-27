using UnityEngine;

namespace Drift.Life
{
    // The fisherman's hut at the land end of a jetty and the clutter round it (owner, 2026-09-24: "Der Hafen ist im
    // Moment nur ein Steg"). Template units like the houses, drawn at buildingScale; IslandSettlementSystem sets every
    // piece on its own ground height. The hut looks out to sea along +z with its jetty on the -x side; FishHut(-1) is
    // the mirror image for a hut on the jetty's other side.
    public static partial class SettlementMeshes
    {
        public const float HutWidth = 0.2f, HutDepth = 0.18f;
        // How far the stilts reach below the floor: the hut stands on the highest corner of its ground and the
        // stilts carry it down the slope (on level ground they are simply buried).
        public const float HutStilts = 0.36f;
        // Footprint half extents with the roof overhang (free ground, walkers, dry ground checks).
        public const float HutHalfX = 0.15f, HutHalfZ = 0.15f;
        public const float HutHeight = 0.3f;
        public static readonly Vector3 HutLantern = new Vector3(-0.085f, 0.1f, 0.125f);
        public static readonly Vector3 HutChimney = new Vector3(0.055f, 0.275f, -0.035f);

        public enum HarbourProp { Barrels, Crates, Coil, Pot }
        public const int HarbourPropCount = 4;
        // Footprint radius of each prop in template units.
        static readonly float[] PropRadius = { 0.07f, 0.075f, 0.036f, 0.055f };
        public static float HarbourPropRadius(HarbourProp p) => PropRadius[(int)p];

        static readonly Color Rope = new Color(0.86f, 0.76f, 0.54f);
        static readonly Color Teal = new Color(0.36f, 0.62f, 0.66f);
        static readonly Color Cream = new Color(0.93f, 0.9f, 0.8f);
        static readonly Color RoofRed = new Color(0.8f, 0.3f, 0.2f);
        static readonly Color BarrelWood = new Color(0.62f, 0.42f, 0.24f);

        static PlantTemplate[] _hut, _hutGlow, _props;
        static PlantTemplate _jettyGear;

        public static PlantTemplate FishHut(int side)
        {
            if (_hut == null) BuildHut();
            return _hut[side < 0 ? 1 : 0];
        }

        public static PlantTemplate FishHutGlow(int side)
        {
            if (_hut == null) BuildHut();
            return _hutGlow[side < 0 ? 1 : 0];
        }

        public static PlantTemplate Prop(HarbourProp p)
        {
            if (_props == null)
                _props = new[] { Make(Barrels), Make(Crates), Make(b => RopeCoil(b, Vector3.zero, 0.034f, 0.024f)), Make(LobsterPot) };
            return _props[(int)p];
        }

        // Harbour gear on the jetty itself, in the jetty's frame (IslandSettlementSystem draws it at the jetty's scale):
        // a bollard the boat is tied to - the line runs to the bow of the boat at (0.12, 0, 0.44), yaw +6 - and a coil
        // of rope beside the walkway.
        public static PlantTemplate JettyGear => _jettyGear ??= Make(b =>
        {
            Frustum(b, new Vector3(0.047f, 0f, 0.43f), 0.013f, 0.011f, 0.06f, 0.1f, 5, new Color(0.3f, 0.3f, 0.32f), true);
            Post(b, new Vector3(0.047f, 0.09f, 0.43f), new Vector3(0.134f, 0.046f, 0.558f), 0.0035f, Rope);
            RopeCoil(b, new Vector3(-0.032f, 0.07f, 0.29f), 0.022f, 0.016f);
        });

        static void BuildHut()
        {
            var body = new ShapeBuilder();
            var glow = new ShapeBuilder();
            Hut(body, glow);
            var b = ToTemplate(body, null);
            var g = ToTemplate(glow, null);
            _hut = new[] { b, MirrorX(b) };
            _hutGlow = new[] { g, MirrorX(g) };
        }

        // Mirrored across x = 0; the triangles are flipped so the faces still point outward.
        static PlantTemplate MirrorX(PlantTemplate t)
        {
            var m = new PlantTemplate
            {
                vertices = (Vector3[])t.vertices.Clone(), normals = (Vector3[])t.normals.Clone(), colors = (Color[])t.colors.Clone(),
                triangles = (int[])t.triangles.Clone(), sway = (float[])t.sway.Clone(), wing = (float[])t.wing.Clone()
            };
            for (int i = 0; i < m.vertices.Length; i++)
            {
                m.vertices[i].x = -m.vertices[i].x;
                m.normals[i].x = -m.normals[i].x;
            }
            for (int i = 0; i < m.triangles.Length; i += 3) (m.triangles[i + 1], m.triangles[i + 2]) = (m.triangles[i + 2], m.triangles[i + 1]);
            return m;
        }

        static void Hut(ShapeBuilder b, ShapeBuilder glow)
        {
            float w = HutWidth, d = HutDepth, wall = 0.13f, ridge = 0.245f, z = d * 0.5f;
            float slope = (ridge - wall) / (w * 0.5f);
            // A plank platform on stilts with a little porch under the roof, then weathered teal planks in three courses.
            Color deck = new Color(0.66f, 0.55f, 0.4f);
            b.Box(new Vector3(0f, -0.013f, 0.025f), new Vector3(w + 0.03f, 0.026f, d + 0.09f), deck);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = 0; sz < 2; sz++)
                    Post(b, new Vector3(0.1f * sx, -0.02f, sz == 0 ? -0.095f : 0.145f), new Vector3(0.1f * sx, -HutStilts, sz == 0 ? -0.095f : 0.145f), 0.009f, DarkWood);
            Walls(b, 0f, 0f, 0f, 0.046f, w, d, Teal * 1.04f);
            Walls(b, 0f, 0f, 0.046f, 0.09f, w, d, Teal * 0.9f);
            Walls(b, 0f, 0f, 0.09f, wall, w, d, Teal * 1.1f);
            // Red roof with a deep porch overhang at the front, dark boards underneath.
            float o = 0.035f, xl = -w * 0.5f - o, xr = w * 0.5f + o, ye = wall - o * slope, z0 = -z - 0.03f, z1 = z + 0.06f;
            Color under = new Color(0.4f, 0.3f, 0.22f);
            Quad(b, new Vector3(xl, ye, z0), new Vector3(xl, ye, z1), new Vector3(0f, ridge, z1), new Vector3(0f, ridge, z0), new Vector3(-1f, 1f, 0f), RoofRed);
            Quad(b, new Vector3(xr, ye, z0), new Vector3(xr, ye, z1), new Vector3(0f, ridge, z1), new Vector3(0f, ridge, z0), new Vector3(1f, 1f, 0f), RoofRed * 0.88f);
            Quad(b, new Vector3(xl, ye, z0), new Vector3(xl, ye, z1), new Vector3(0f, ridge, z1), new Vector3(0f, ridge, z0), new Vector3(1f, -1f, 0f), under);
            Quad(b, new Vector3(xr, ye, z0), new Vector3(xr, ye, z1), new Vector3(0f, ridge, z1), new Vector3(0f, ridge, z0), new Vector3(-1f, -1f, 0f), under);
            b.Fin(new Vector3(-w * 0.5f, wall, z), new Vector3(w * 0.5f, wall, z), new Vector3(0f, ridge, z), Vector3.forward, Cream);
            b.Fin(new Vector3(-w * 0.5f, wall, -z), new Vector3(w * 0.5f, wall, -z), new Vector3(0f, ridge, -z), Vector3.back, Cream * 0.94f);
            // Mustard door towards the jetty, a round porthole window in the gable.
            Quad(b, new Vector3(-0.06f, -0.02f, z + 0.002f), new Vector3(-0.01f, -0.02f, z + 0.002f), new Vector3(-0.01f, 0.095f, z + 0.002f), new Vector3(-0.06f, 0.095f, z + 0.002f), Vector3.forward, new Color(0.9f, 0.68f, 0.24f));
            RoundZ(b, new Vector3(0f, 0.178f, z + 0.002f), 0.034f, 6, White);
            RoundZ(glow, new Vector3(0f, 0.178f, z + 0.004f), 0.023f, 6, Window);
            WindowX(glow, w * 0.5f + 0.002f, 0.075f, 0.027f, 0.045f, 0.04f, 1f);
            // Brick chimney through the far roof side.
            b.Box(new Vector3(HutChimney.x, 0.212f, HutChimney.z), new Vector3(0.036f, 0.124f, 0.036f), new Color(0.64f, 0.4f, 0.33f), false);
            // Gold fish sign on a bracket at the sea-side corner, seen from both sides.
            float fx = 0.085f, fz = z + 0.045f, fy = 0.097f;
            Post(b, new Vector3(fx, 0.118f, z), new Vector3(fx, 0.118f, z + 0.08f), 0.005f, DarkWood);
            Color fish = new Color(0.98f, 0.64f, 0.2f);
            Quad2(b, new Vector3(fx, fy, fz + 0.036f), new Vector3(fx, fy + 0.018f, fz), new Vector3(fx, fy, fz - 0.022f), new Vector3(fx, fy - 0.018f, fz), Vector3.right, fish);
            b.Fin(new Vector3(fx, fy, fz - 0.018f), new Vector3(fx, fy + 0.017f, fz - 0.042f), new Vector3(fx, fy - 0.017f, fz - 0.042f), Vector3.right, fish * 0.92f);
            b.Fin(new Vector3(fx, fy, fz - 0.018f), new Vector3(fx, fy + 0.017f, fz - 0.042f), new Vector3(fx, fy - 0.017f, fz - 0.042f), Vector3.left, fish * 0.85f);
            // Red and white lifebuoy on the front wall.
            for (int i = 0; i < 6; i++)
            {
                float a0 = i * Mathf.PI / 3f, a1 = (i + 1) * Mathf.PI / 3f;
                Vector3 c = new Vector3(0.045f, 0.068f, z + 0.004f);
                Vector3 d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f), d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);
                Quad(b, c + d0 * 0.015f, c + d1 * 0.015f, c + d1 * 0.029f, c + d0 * 0.029f, Vector3.forward, i % 2 == 0 ? new Color(0.9f, 0.22f, 0.18f) : White);
            }
            // A fishing net drying on the jetty side, with orange floats along its hem.
            float nx = -w * 0.5f - 0.003f;
            Quad(b, new Vector3(nx, 0.122f, -0.07f), new Vector3(nx, 0.122f, 0.055f), new Vector3(nx, 0.045f, 0.035f), new Vector3(nx, 0.028f, -0.04f), Vector3.left, new Color(0.52f, 0.5f, 0.36f));
            Color cork = new Color(1f, 0.55f, 0.15f);
            for (int i = 0; i < 3; i++)
            {
                float fz0 = -0.03f + i * 0.028f, fy0 = 0.03f + i * 0.006f;
                b.Fin(new Vector3(nx - 0.002f, fy0 - 0.008f, fz0 - 0.008f), new Vector3(nx - 0.002f, fy0 - 0.008f, fz0 + 0.008f), new Vector3(nx - 0.002f, fy0 + 0.008f, fz0), Vector3.left, cork);
            }
            // Two oars with red blades leaning on the far wall.
            Color oar = new Color(0.84f, 0.68f, 0.45f), blade = new Color(0.86f, 0.3f, 0.24f);
            for (int i = 0; i < 2; i++)
            {
                float oz = -0.062f + i * 0.026f;
                Vector3 foot = new Vector3(w * 0.5f + 0.012f, 0f, oz), top = new Vector3(w * 0.5f + 0.004f, 0.108f, oz - 0.008f + i * 0.012f);
                Post(b, foot, top, 0.0045f, oar);
                Vector3 axis = (top - foot).normalized, bs = new Vector3(0f, 0f, 0.013f);
                Vector3 b0 = top - axis * 0.045f, b1 = top + axis * 0.004f;
                Quad2(b, b0 - bs, b0 + bs, b1 + bs, b1 - bs, new Vector3(1f, 0.2f, 0f), blade);
            }
            // Lantern by the door under the porch roof.
            Post(b, new Vector3(HutLantern.x, 0.132f, z), new Vector3(HutLantern.x, 0.132f, HutLantern.z + 0.004f), 0.004f, DarkWood);
            b.Cone(new Vector3(HutLantern.x, HutLantern.y + 0.02f, HutLantern.z), Vector3.up, 0.016f, 0.013f, 4, new Color(0.18f, 0.18f, 0.2f));
            glow.Cone(new Vector3(HutLantern.x, HutLantern.y, HutLantern.z), Vector3.up, 0.013f, 0.02f, 4, new Color(0.95f, 0.85f, 0.6f));
            glow.Cone(new Vector3(HutLantern.x, HutLantern.y, HutLantern.z), Vector3.down, 0.013f, 0.014f, 4, new Color(0.9f, 0.8f, 0.55f));
        }

        // A flat n-gon facing +z (the porthole and its frame).
        static void RoundZ(ShapeBuilder b, Vector3 c, float r, int seg, Color col)
        {
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg + Mathf.PI / seg, a1 = (i + 1) * Mathf.PI * 2f / seg + Mathf.PI / seg;
                b.Fin(c, c + new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * r, c + new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * r, Vector3.forward, col);
            }
        }

        // A coil of rope: a low ring whose top slopes into the hole in the middle (72 verts at 6 sides).
        static void RopeCoil(ShapeBuilder b, Vector3 c, float r, float h)
        {
            const int seg = 6;
            float inner = r * 0.38f;
            Frustum(b, c, r, r * 0.9f, -0.01f, h, seg, Rope * 0.88f, false);
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 top = Vector3.up * h, low = Vector3.up * (h * 0.55f);
                Quad(b, c + d0 * r * 0.9f + top, c + d1 * r * 0.9f + top, c + d1 * inner + low, c + d0 * inner + low, Vector3.up, (i % 2 == 0 ? Rope : Rope * 0.94f));
            }
        }

        static void Barrel(ShapeBuilder b, Vector3 c, float r, float h, Color col)
        {
            Frustum(b, c, r * 0.88f, r, -0.02f, h * 0.45f, 5, col * 0.92f, false);
            Frustum(b, c, r, r * 0.88f, h * 0.45f, h, 5, col, true);
        }

        static void Barrels(ShapeBuilder b)
        {
            Barrel(b, new Vector3(-0.034f, 0f, -0.01f), 0.031f, 0.078f, BarrelWood);
            Barrel(b, new Vector3(0.03f, 0f, 0.022f), 0.029f, 0.066f, BarrelWood * 0.85f);
        }

        static void Crates(ShapeBuilder b)
        {
            b.Box(new Vector3(-0.03f, 0.02f, 0f), new Vector3(0.07f, 0.08f, 0.07f), new Color(0.78f, 0.6f, 0.37f), false);
            b.Box(new Vector3(0.042f, 0.015f, 0.012f), new Vector3(0.064f, 0.07f, 0.064f), new Color(0.66f, 0.5f, 0.3f), false);
            b.Box(new Vector3(-0.022f, 0.09f, 0.004f), new Vector3(0.058f, 0.06f, 0.058f), new Color(0.84f, 0.67f, 0.42f), false);
        }

        // A wooden lobster pot: half a hexagon arched along z, a dark funnel in its front and a little buoy on top.
        static void LobsterPot(ShapeBuilder b)
        {
            Color slat = new Color(0.72f, 0.57f, 0.36f);
            float r = 0.032f, l = 0.045f;
            for (int i = 0; i < 3; i++)
            {
                float a0 = i * Mathf.PI / 3f, a1 = (i + 1) * Mathf.PI / 3f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0) * r, Mathf.Sin(a0) * r, 0f), d1 = new Vector3(Mathf.Cos(a1) * r, Mathf.Sin(a1) * r, 0f);
                Quad(b, d0 + Vector3.back * l, d1 + Vector3.back * l, d1 + Vector3.forward * l, d0 + Vector3.forward * l, (d0 + d1), slat * (i == 1 ? 1f : 0.9f));
            }
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 zc = Vector3.forward * (l * s);
                for (int i = 0; i < 3; i++)
                {
                    float a0 = i * Mathf.PI / 3f, a1 = (i + 1) * Mathf.PI / 3f;
                    b.Fin(zc, zc + new Vector3(Mathf.Cos(a0) * r, Mathf.Sin(a0) * r, 0f), zc + new Vector3(Mathf.Cos(a1) * r, Mathf.Sin(a1) * r, 0f), Vector3.forward * s, slat * 0.8f);
                }
            }
            RoundZ(b, new Vector3(0f, 0.013f, l + 0.002f), 0.011f, 6, new Color(0.2f, 0.17f, 0.14f));
            b.Cone(new Vector3(0f, r - 0.004f, 0.012f), Vector3.up, 0.011f, 0.024f, 4, new Color(0.92f, 0.25f, 0.2f));
        }
    }
}
