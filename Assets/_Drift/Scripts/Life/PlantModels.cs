using UnityEngine;

namespace Drift.Life
{
    // Plant templates of the tropical, nordic and savanna biomes. Same budget as the temperate ones in
    // LifeMeshes (ground cover <= 24 verts, flowers <= 36, shrubs <= 60, trees <= 72), same units (drawn at
    // IslandLifeSystem.plantScale), wind sway from the template height unless Sway says otherwise.
    public static class PlantModels
    {
        // Succession role (= per-cell counter slot) a kind fills when it has to be guessed from the kind alone:
        // foreign plants that arrived with a merge, old callers of LifeMeshes.PlantSlot.
        public static int Role(LifeKind kind)
        {
            switch (kind)
            {
                case LifeKind.Grass: case LifeKind.Fern: case LifeKind.Lichen: case LifeKind.Mushroom: case LifeKind.DryGrass:
                    return BiomeSpec.RoleGround;
                case LifeKind.Flower: case LifeKind.Hibiscus: case LifeKind.Heather: case LifeKind.Aloe:
                    return BiomeSpec.RoleFlower;
                case LifeKind.Bush: case LifeKind.Banana: case LifeKind.Juniper: case LifeKind.ThornBush: case LifeKind.TermiteMound:
                    return BiomeSpec.RoleShrub;
                case LifeKind.Tree: case LifeKind.Bamboo: case LifeKind.JungleTree: case LifeKind.Spruce: case LifeKind.Birch:
                case LifeKind.Acacia: case LifeKind.Baobab:
                    return BiomeSpec.RoleTree;
                case LifeKind.Palm: return BiomeSpec.RolePalm;
                case LifeKind.Reed: return BiomeSpec.RoleReed;
                default: return -1;
            }
        }

        public static bool IsPlant(LifeKind kind) => Role(kind) >= 0;

        // Multiplier on the template's height-based sway weights: mounds and bottle trunks do not wave.
        public static float Sway(LifeKind kind)
        {
            switch (kind)
            {
                case LifeKind.TermiteMound: return 0f;
                case LifeKind.Baobab: return 0.15f;
                case LifeKind.Mushroom: case LifeKind.Lichen: case LifeKind.Aloe: return 0.25f;
                case LifeKind.Bamboo: return 1.3f;
                default: return 1f;
            }
        }

        static Color Hsv(float h, float s, float v) => Color.HSVToRGB(Mathf.Repeat(h, 1f), s, v);

        static void Blob(ShapeBuilder b, Vector3 center, float radius, float up, float down, int segments, Color c)
        {
            b.Cone(center, Vector3.up, radius, up, segments, c);
            b.Cone(center, Vector3.down, radius, down, segments, c * 0.9f);
        }

        // An arching leaf: one triangle rising from the base, one drooping to the tip (6 verts), facing up.
        static void Leaf(ShapeBuilder b, Vector3 from, Vector3 dir, float length, float rise, float droop, float width, Color c)
        {
            Vector3 side = new Vector3(-dir.z, 0f, dir.x) * width;
            Vector3 mid = from + dir * (length * 0.55f) + Vector3.up * rise;
            Vector3 tip = from + dir * length + Vector3.up * (rise - droop);
            Vector3 up = Vector3.up + dir * 0.3f;
            b.Fin(from, mid + side, mid - side, up, c);
            b.Fin(mid + side, tip, mid - side, up, c * 0.9f);
        }

        public static bool Build(ShapeBuilder b, LifeKind kind, int v)
        {
            switch (kind)
            {
                // ------------------------------------------------------------ tropical
                case LifeKind.Fern:
                {
                    Color g = Hsv(0.34f + 0.02f * v, 0.72f, 0.42f + 0.05f * v);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI * 0.5f + 0.5f * v;
                        Leaf(b, Vector3.zero, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), 0.3f, 0.15f, 0.1f, 0.055f, g * (0.9f + 0.07f * i));
                    }
                    return true;
                }
                case LifeKind.Hibiscus:
                {
                    Color[] petals = { new Color(0.95f, 0.12f, 0.2f), new Color(1f, 0.4f, 0.65f), new Color(1f, 0.72f, 0.15f) };
                    Color stem = new Color(0.16f, 0.45f, 0.2f);
                    b.Cone(Vector3.zero, new Vector3(0.05f, 1f, 0f), 0.03f, 0.3f, 3, stem);
                    Vector3 c = new Vector3(0.015f, 0.31f, 0f);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * Mathf.PI * 0.4f, a0 = a - 0.55f, a1 = a + 0.55f;
                        Vector3 p0 = c + new Vector3(Mathf.Cos(a0), 0.35f, Mathf.Sin(a0)) * 0.15f;
                        Vector3 p1 = c + new Vector3(Mathf.Cos(a1), 0.35f, Mathf.Sin(a1)) * 0.15f;
                        b.Fin(c, p0, p1, Vector3.up, petals[v] * (0.92f + 0.04f * i));
                    }
                    b.Cone(c, Vector3.up, 0.025f, 0.1f, 3, new Color(1f, 0.9f, 0.3f));
                    Leaf(b, new Vector3(0f, 0.08f, 0f), new Vector3(-0.8f, 0f, 0.6f), 0.16f, 0.05f, 0.04f, 0.045f, stem * 1.2f);
                    return true;
                }
                case LifeKind.Banana:
                {
                    Color stem = new Color(0.45f, 0.55f, 0.22f);
                    Color leaf = Hsv(0.27f + 0.015f * v, 0.7f, 0.62f + 0.05f * v);
                    b.Cone(Vector3.zero, Vector3.up, 0.07f, 0.4f, 4, stem);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * Mathf.PI * 0.4f + 0.3f * v;
                        Leaf(b, new Vector3(0f, 0.34f, 0f), new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), 0.46f, 0.2f + 0.03f * (i % 2), 0.22f, 0.11f, leaf * (0.9f + 0.05f * i));
                    }
                    return true;
                }
                case LifeKind.Bamboo:
                {
                    Color cane = Hsv(0.2f + 0.02f * v, 0.55f, 0.7f);
                    Color leaf = Hsv(0.26f, 0.6f, 0.62f);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * 1.2566f + 0.4f * v;
                        Vector3 foot = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.09f;
                        Vector3 lean = new Vector3(Mathf.Cos(a) * 0.16f, 1f, Mathf.Sin(a) * 0.16f).normalized;
                        float h = 1.15f + 0.1f * ((i * 7 + v) % 4);
                        b.Cone(foot, lean, 0.028f, h, 3, cane * (0.92f + 0.04f * i));
                        Vector3 top = foot + lean * (h * 0.82f);
                        Vector3 outDir = new Vector3(Mathf.Cos(a), -0.35f, Mathf.Sin(a));
                        Vector3 side = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.07f;
                        b.Fin(top, top + outDir * 0.26f + side, top + outDir * 0.26f - side, Vector3.up, leaf);
                    }
                    return true;
                }
                case LifeKind.JungleTree:
                {
                    Color trunk = new Color(0.36f, 0.27f, 0.2f);
                    Color g = Hsv(0.36f + 0.015f * v, 0.72f, 0.3f + 0.03f * v);
                    b.Cone(Vector3.zero, Vector3.up, 0.09f, 0.95f, 4, trunk);
                    Blob(b, new Vector3(0f, 0.92f, 0f), 0.56f, 0.24f, 0.16f, 6, g);
                    Blob(b, new Vector3(0.12f, 1.14f, -0.06f), 0.32f, 0.2f, 0.1f, 4, g * 1.22f);
                    return true;
                }
                // ------------------------------------------------------------ nordic
                case LifeKind.Lichen:
                {
                    Color[] pale = { new Color(0.72f, 0.8f, 0.68f), new Color(0.8f, 0.82f, 0.74f), new Color(0.66f, 0.76f, 0.72f) };
                    b.Cone(Vector3.zero, Vector3.up, 0.15f, 0.06f, 5, pale[v]);
                    b.Cone(new Vector3(0.13f, 0f, 0.06f), Vector3.up, 0.08f, 0.045f, 3, pale[v] * 1.08f);
                    return true;
                }
                case LifeKind.Mushroom:
                {
                    Color[] caps = { new Color(0.86f, 0.12f, 0.1f), new Color(0.62f, 0.38f, 0.2f), new Color(0.92f, 0.5f, 0.14f) };
                    Color stem = new Color(0.95f, 0.93f, 0.86f);
                    b.Cone(Vector3.zero, Vector3.up, 0.04f, 0.2f, 3, stem);
                    b.Cone(new Vector3(0f, 0.11f, 0f), Vector3.up, 0.12f, 0.1f, 4, caps[v]);
                    b.Cone(new Vector3(0.1f, 0.02f, 0.05f), Vector3.up, 0.065f, 0.07f, 3, caps[v] * 0.92f);
                    return true;
                }
                case LifeKind.Heather:
                {
                    Color[] bloom = { new Color(0.72f, 0.36f, 0.78f), new Color(0.86f, 0.42f, 0.7f), new Color(0.6f, 0.4f, 0.85f) };
                    Color green = new Color(0.22f, 0.36f, 0.26f);
                    b.Cone(Vector3.zero, Vector3.up, 0.14f, 0.09f, 4, green);
                    b.Cone(new Vector3(0.02f, 0.04f, 0f), new Vector3(0.1f, 1f, 0f), 0.06f, 0.2f, 3, bloom[v]);
                    b.Cone(new Vector3(-0.07f, 0.03f, 0.05f), new Vector3(-0.25f, 1f, 0.2f), 0.05f, 0.17f, 3, bloom[v] * 0.9f);
                    b.Cone(new Vector3(0.05f, 0.03f, -0.07f), new Vector3(0.2f, 1f, -0.3f), 0.05f, 0.16f, 3, bloom[v] * 1.08f);
                    return true;
                }
                case LifeKind.Juniper:
                {
                    Color g = Hsv(0.42f + 0.02f * v, 0.5f, 0.3f + 0.03f * v);
                    b.Cone(Vector3.zero, Vector3.up, 0.24f, 0.3f, 5, g * 0.9f);
                    b.Cone(new Vector3(0f, 0.12f, 0f), Vector3.up, 0.17f, 0.5f + 0.06f * v, 5, g);
                    return true;
                }
                case LifeKind.Spruce:
                {
                    Color trunk = new Color(0.3f, 0.2f, 0.14f);
                    Color g = Hsv(0.45f + 0.015f * v, 0.55f, 0.27f + 0.02f * v);
                    Color snowCap = new Color(0.95f, 0.97f, 1f);
                    float s = 1f + 0.08f * v;
                    b.Cone(Vector3.zero, Vector3.up, 0.055f, 0.4f, 3, trunk);
                    b.Cone(new Vector3(0f, 0.2f, 0f), Vector3.up, 0.36f, 0.5f * s, 5, g);
                    b.Cone(new Vector3(0f, 0.52f * s, 0f), Vector3.up, 0.28f, 0.46f * s, 5, g * 1.12f);
                    b.Cone(new Vector3(0f, 0.84f * s, 0f), Vector3.up, 0.2f, 0.42f * s, 5, g * 1.22f);
                    b.Cone(new Vector3(0f, 1.06f * s, 0f), Vector3.up, 0.115f, 0.24f * s, 4, snowCap);
                    return true;
                }
                case LifeKind.Birch:
                {
                    Color bark = new Color(0.94f, 0.93f, 0.88f);
                    Color g = Hsv(0.2f + 0.02f * v, 0.6f, 0.72f);
                    Vector3 lean = new Vector3(0.06f * (v - 1), 1f, 0.04f).normalized;
                    b.Cone(Vector3.zero, lean, 0.05f, 1.05f, 4, bark);
                    b.Fin(lean * 0.3f + new Vector3(0.04f, 0f, 0.03f), lean * 0.36f + new Vector3(0.045f, 0f, 0.02f), lean * 0.33f + new Vector3(-0.01f, 0f, 0.055f), new Vector3(1f, 0f, 1f), new Color(0.12f, 0.12f, 0.12f));
                    b.Fin(lean * 0.55f + new Vector3(-0.035f, 0f, 0.03f), lean * 0.6f + new Vector3(-0.035f, 0f, 0.025f), lean * 0.57f + new Vector3(0.01f, 0f, 0.045f), new Vector3(-1f, 0f, 1f), new Color(0.12f, 0.12f, 0.12f));
                    Blob(b, lean * 0.82f + new Vector3(0.05f, 0f, 0f), 0.27f, 0.32f, 0.18f, 5, g);
                    Blob(b, lean * 0.62f + new Vector3(-0.14f, 0f, 0.08f), 0.17f, 0.2f, 0.12f, 4, g * 1.1f);
                    return true;
                }
                // ------------------------------------------------------------ savanna
                case LifeKind.DryGrass:
                {
                    Color straw = Hsv(0.125f + 0.012f * v, 0.42f, 0.96f);
                    b.Cone(Vector3.zero, new Vector3(0.08f, 1f, 0f), 0.05f, 0.36f, 3, straw);
                    b.Cone(new Vector3(0.07f, 0f, 0.05f), new Vector3(-0.12f, 1f, 0.08f), 0.045f, 0.3f, 3, straw * 0.9f);
                    b.Cone(new Vector3(-0.05f, 0f, -0.06f), new Vector3(-0.05f, 1f, -0.14f), 0.04f, 0.26f, 3, straw * 1.06f);
                    return true;
                }
                case LifeKind.Aloe:
                {
                    Color leaf = Hsv(0.36f + 0.02f * v, 0.35f, 0.55f);
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * 2.094f + 0.5f * v;
                        b.Cone(Vector3.zero, new Vector3(Mathf.Cos(a) * 0.9f, 1f, Mathf.Sin(a) * 0.9f), 0.05f, 0.24f, 3, leaf * (0.92f + 0.06f * i));
                    }
                    b.Cone(Vector3.zero, new Vector3(0.04f, 1f, 0.02f), 0.018f, 0.34f, 3, new Color(0.5f, 0.4f, 0.25f));
                    b.Cone(new Vector3(0.012f, 0.3f, 0.006f), Vector3.up, 0.04f, 0.14f, 3, new Color(1f, 0.35f, 0.12f));
                    return true;
                }
                case LifeKind.ThornBush:
                {
                    Color g = Hsv(0.2f + 0.02f * v, 0.35f, 0.5f);
                    Color twig = new Color(0.42f, 0.34f, 0.26f);
                    b.Cone(Vector3.zero, new Vector3(0.3f, 1f, 0.1f), 0.03f, 0.3f, 3, twig);
                    b.Cone(Vector3.zero, new Vector3(-0.4f, 1f, -0.2f), 0.03f, 0.28f, 3, twig);
                    Blob(b, new Vector3(0f, 0.26f, 0f), 0.32f, 0.12f, 0.08f, 5, g);
                    return true;
                }
                case LifeKind.TermiteMound:
                {
                    Color earth = Hsv(0.045f + 0.01f * v, 0.62f, 0.62f);
                    b.Cone(Vector3.zero, new Vector3(0.05f, 1f, 0f), 0.22f, 0.62f + 0.1f * v, 5, earth);
                    b.Cone(new Vector3(0.14f, 0f, 0.06f), new Vector3(0.1f, 1f, 0.05f), 0.13f, 0.36f, 4, earth * 0.92f);
                    return true;
                }
                case LifeKind.Acacia:
                {
                    Color trunk = new Color(0.4f, 0.3f, 0.2f);
                    Color g = Hsv(0.2f + 0.02f * v, 0.55f, 0.56f + 0.04f * v);
                    Vector3 lean = new Vector3(0.12f * (v - 1), 1f, 0.08f).normalized;
                    b.Cone(Vector3.zero, lean, 0.06f, 0.8f, 4, trunk);
                    Vector3 fork = lean * 0.5f;
                    b.Cone(fork, new Vector3(0.7f, 1f, -0.2f), 0.035f, 0.42f, 3, trunk);
                    b.Cone(fork, new Vector3(-0.6f, 1f, 0.4f), 0.035f, 0.4f, 3, trunk);
                    Vector3 crown = lean * 0.78f;
                    b.Cone(crown, Vector3.up, 0.56f, 0.1f, 7, g);
                    b.Cone(crown, Vector3.down, 0.56f, 0.05f, 7, g * 0.8f);
                    return true;
                }
                case LifeKind.Baobab:
                {
                    Color bark = new Color(0.56f, 0.47f, 0.4f);
                    Color g = Hsv(0.26f, 0.5f, 0.5f);
                    b.Tube(Vector3.zero, new Vector3(0f, 0.75f, 0f), Vector3.forward, new Vector2(0.42f, 0.42f), new Vector2(0.3f, 0.3f), 6, bark, false, true);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI * 0.5f + 0.6f * v;
                        Vector3 dir = new Vector3(Mathf.Cos(a) * 0.8f, 1f, Mathf.Sin(a) * 0.8f).normalized;
                        Vector3 from = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.08f + Vector3.up * 0.72f;
                        b.Cone(from, dir, 0.05f, 0.34f, 3, bark * 0.92f);
                        b.Cone(from + dir * 0.3f, Vector3.up, 0.15f, 0.08f, 3, g * (0.9f + 0.06f * i));
                    }
                    return true;
                }
            }
            return false;
        }
    }
}
