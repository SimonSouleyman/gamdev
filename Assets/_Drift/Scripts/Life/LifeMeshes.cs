using UnityEngine;

namespace Drift.Life
{
    // Phase 4 kinds come after Bird so nothing indexed by the old values (plant counters, herd species) moves;
    // the Phase 5 plants (Palm, Reed) come last for the same reason and map to plant slots via LifeMeshes.PlantSlot.
    // Biome kinds (herd animals, then plants) are appended behind them: saves store the numeric value.
    public enum LifeKind
    {
        Grass, Flower, Bush, Tree, Hare, Sheep, Goat, Ox, Bird, Crab, Turtle, Butterfly, Firefly, Seabird, Palm, Reed,
        Capybara, Flamingo, Tortoise, Reindeer, Penguin, ArcticFox, Zebra, Giraffe, Meerkat,
        Fern, Hibiscus, Banana, Bamboo, JungleTree, Lichen, Mushroom, Heather, Juniper, Spruce, Birch,
        DryGrass, Aloe, ThornBush, TermiteMound, Acacia, Baobab
    }

    public class PlantTemplate
    {
        public Vector3[] vertices;
        public Vector3[] normals;
        public Color[] colors;
        public int[] triangles;
        public float[] sway;
        public float[] wing;
        // Detail animal templates only (AnimalModels), in template units; null on everything else, where
        // TemplateBatch.AddAnimal falls back to lever = z, up = y, no leg lift.
        public float[] lever;
        public float[] up;
        public float[] leg;
        // Height of the body's roll axis above the ground once it lies down (wallowing oxen).
        public float rollAxis;
        public float legLength;
    }

    public static class LifeMeshes
    {
        public const int Variants = 3;
        // Phase 5 flowers: FlowerColours colours x FlowerShapes shapes (variant = shape * FlowerColours + colour).
        public const int FlowerColours = 6;
        public const int FlowerShapes = 3;
        public const int FlowerVariants = FlowerColours * FlowerShapes;
        public const int KindCount = 42;
        const int MaxVariants = FlowerVariants;
        // Plant kinds in the order IslandLifeSystem counts them per cell.
        public const int PlantKinds = 6;

        static readonly Mesh[,] MeshCache = new Mesh[KindCount, MaxVariants];
        static readonly PlantTemplate[,] TemplateCache = new PlantTemplate[KindCount, MaxVariants];

        public static int VariantsOf(LifeKind kind) => kind == LifeKind.Flower ? FlowerVariants : Variants;

        // The succession role a plant kind fills (grass-like 0, flower 1, shrub 2, tree 3, shore palm 4, reed 5).
        public static int PlantSlot(LifeKind kind)
        {
            int role = PlantModels.Role(kind);
            return role >= 0 ? role : (int)kind;
        }

        public static bool IsPlant(LifeKind kind) => PlantModels.IsPlant(kind);
        public static bool IsHerdAnimal(LifeKind kind) => AnimalModels.Has(kind);

        public static LifeKind PlantKindOfSlot(int slot) => slot == 4 ? LifeKind.Palm : slot == 5 ? LifeKind.Reed : (LifeKind)slot;

        public static int FlowerColour(int variant) => Mathf.Abs(variant) % FlowerColours;
        public static int FlowerShape(int variant) => (Mathf.Abs(variant) / FlowerColours) % FlowerShapes;
        static Material _material;
        static Material _flockMaterial;
        static Material _animalMaterial;
        static Material _critterMaterial;

        // Crabs, turtles, butterflies and fireflies (IslandCrittersSystem) share Drift/Critter: vertex colours
        // plus flutter/bob/glow channels. Cull off because wings and legs are single flat triangles.
        public static Material CritterMaterial
        {
            get
            {
                if (_critterMaterial == null)
                {
                    var shader = Shader.Find("Drift/Critter");
                    if (shader == null)
                    {
                        Debug.LogError("LifeMeshes: shader 'Drift/Critter' not found, critters fall back to Drift/VertexColor.");
                        return Material;
                    }
                    _critterMaterial = new Material(shader) { name = "CritterVertexColor", hideFlags = HideFlags.HideAndDontSave };
                    _critterMaterial.SetFloat("_LightAmount", 1f);
                    _critterMaterial.SetFloat("_Cull", 0f);
                }
                return _critterMaterial;
            }
        }

        // Runtime fallback for IslandHerdSystem.animalMaterial; Assets/_Drift/Materials/Animal.mat carries
        // the same defaults for tuning in the inspector.
        public static Material AnimalMaterial
        {
            get
            {
                if (_animalMaterial == null)
                {
                    var shader = Shader.Find("Drift/Animal");
                    if (shader == null)
                    {
                        Debug.LogError("LifeMeshes: shader 'Drift/Animal' not found, animals fall back to Drift/VertexColor.");
                        return Material;
                    }
                    _animalMaterial = new Material(shader) { name = "AnimalVertexColor", hideFlags = HideFlags.HideAndDontSave };
                    _animalMaterial.SetFloat("_LightAmount", 1f);
                }
                return _animalMaterial;
            }
        }

        public static Material Material
        {
            get
            {
                if (_material == null)
                {
                    // Vegetation sways from the wind channels (TemplateBatch.AddPlant, UV0) since Phase 5; the
                    // legacy alpha sway stays at 0 so nothing moves twice.
                    _material = Make("LifeVertexColor");
                    _material.SetFloat("_Sway", 0f);
                    _material.SetFloat("_SwaySpeed", 2.2f);
                    _material.SetFloat("_WindBend", 0.45f);
                    _material.SetFloat("_WindFlutter", 0.18f);
                }
                return _material;
            }
        }

        public static Material FlockMaterial
        {
            get
            {
                if (_flockMaterial == null)
                {
                    _flockMaterial = Make("FlockVertexColor");
                    _flockMaterial.SetFloat("_Sway", 0f);
                    _flockMaterial.SetFloat("_Flap", 0.13f);
                    _flockMaterial.SetFloat("_FlapSpeed", 16f);
                }
                return _flockMaterial;
            }
        }

        static Material Make(string name)
        {
            var shader = Shader.Find("Drift/VertexColor");
            if (shader == null) Debug.LogError("LifeMeshes: shader 'Drift/VertexColor' not found.");
            var m = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
            m.SetFloat("_LightAmount", 1f);
            return m;
        }

        public static Mesh Get(LifeKind kind, int variant)
        {
            variant = Mathf.Abs(variant) % VariantsOf(kind);
            ref Mesh slot = ref MeshCache[(int)kind, variant];
            if (slot == null) slot = Build(kind, variant);
            return slot;
        }

        public static PlantTemplate GetTemplate(LifeKind kind, int variant)
        {
            variant = Mathf.Abs(variant) % VariantsOf(kind);
            ref PlantTemplate slot = ref TemplateCache[(int)kind, variant];
            if (slot == null)
            {
                Mesh m = Get(kind, variant);
                var t = new PlantTemplate
                {
                    vertices = m.vertices,
                    normals = m.normals,
                    colors = m.colors,
                    triangles = m.triangles
                };
                float maxY = 0.001f;
                foreach (var v in t.vertices) maxY = Mathf.Max(maxY, v.y);
                t.sway = new float[t.vertices.Length];
                t.wing = new float[t.vertices.Length];
                float swayScale = PlantModels.Sway(kind);
                for (int i = 0; i < t.sway.Length; i++)
                {
                    t.sway[i] = Mathf.Clamp01(t.vertices[i].y / maxY) * swayScale;
                    t.wing[i] = Mathf.Clamp01((Mathf.Abs(t.vertices[i].x) - 0.09f) / 0.2f);
                }
                slot = t;
            }
            return slot;
        }

        // Shared by the simple templates below and the detail templates (AnimalModels), so an animal keeps its
        // colour when it changes LOD.
        internal static readonly Color[] HareFur = { new Color(0.62f, 0.45f, 0.3f), new Color(0.6f, 0.6f, 0.62f), new Color(0.78f, 0.66f, 0.45f) };
        internal static readonly Color[] SheepWool = { new Color(0.96f, 0.94f, 0.88f), new Color(0.86f, 0.82f, 0.74f), new Color(0.5f, 0.44f, 0.4f) };
        internal static readonly Color SheepDark = new Color(0.2f, 0.18f, 0.18f);
        internal static readonly Color[] GoatHide = { new Color(0.92f, 0.92f, 0.9f), new Color(0.6f, 0.56f, 0.5f), new Color(0.3f, 0.26f, 0.24f) };
        internal static readonly Color[] OxHide = { new Color(0.32f, 0.22f, 0.16f), new Color(0.45f, 0.32f, 0.22f), new Color(0.18f, 0.15f, 0.14f) };
        internal static readonly Color Horn = new Color(0.9f, 0.86f, 0.75f);

        static PlantTemplate _burrow;

        // Hare burrow (IslandHerdSystem): a low earth mound with a dark entrance on its +z side, 42 verts.
        public static PlantTemplate Burrow
        {
            get
            {
                if (_burrow == null)
                {
                    var b = new ShapeBuilder();
                    Color earth = new Color(0.34f, 0.24f, 0.16f);
                    b.Cone(new Vector3(0f, 0f, -0.1f), Vector3.up, 0.5f, 0.3f, 6, earth);
                    b.Lump(new Vector3(0f, 0.07f, 0.26f), new Vector3(0.21f, 0.17f, 0.2f), new Color(0.04f, 0.03f, 0.03f));
                    var m = b.ToMesh("Burrow");
                    _burrow = new PlantTemplate { vertices = m.vertices, normals = m.normals, colors = m.colors, triangles = m.triangles };
                    _burrow.sway = new float[_burrow.vertices.Length];
                    _burrow.wing = _burrow.sway;
                    Object.DestroyImmediate(m);
                }
                return _burrow;
            }
        }

        public static PlantTemplate GetDetailTemplate(LifeKind kind, int variant, bool young) =>
            AnimalModels.Get(kind, Mathf.Abs(variant) % Variants, young);

        // pose: AnimalModels.PoseDefault / PoseSpecial (one-legged flamingo, tucked tortoise, upright meerkat,
        // penguin on its belly); kinds without a special pose return the default one.
        public static PlantTemplate GetDetailTemplate(LifeKind kind, int variant, bool young, int pose) =>
            AnimalModels.Get(kind, Mathf.Abs(variant) % Variants, young, pose);

        static Color Hsv(float h, float s, float v) => Color.HSVToRGB(Mathf.Repeat(h, 1f), s, v);

        static PlantTemplate _bolt;

        // Lightning marker: a thin spike from 4.5 units up to the ground plus a flat flash disc (42 verts).
        public static PlantTemplate Bolt
        {
            get
            {
                if (_bolt == null)
                {
                    var b = new ShapeBuilder();
                    b.Cone(new Vector3(0f, 4.5f, 0f), Vector3.down, 0.12f, 4.5f, 4, new Color(1.6f, 1.6f, 2.2f));
                    b.Cone(new Vector3(0f, 0.06f, 0f), Vector3.up, 0.7f, 0.06f, 6, new Color(1.9f, 1.8f, 1.1f));
                    var m = b.ToMesh("Bolt");
                    _bolt = new PlantTemplate { vertices = m.vertices, normals = m.normals, colors = m.colors, triangles = m.triangles };
                    _bolt.sway = new float[_bolt.vertices.Length];
                    _bolt.wing = _bolt.sway;
                    Object.DestroyImmediate(m);
                }
                return _bolt;
            }
        }

        // Plants draw at ~0.55x and animals at ~0.175x scale (a hare is ~0.05 units long), so everything is
        // boxes and cones: a blob is a double cone (12-36 verts) instead of an icosphere (60), and an animal is
        // a bottomless body box plus head (60 verts; goat/ox add 18 for horns) so 120 animals stay under ~9k verts.
        static void Blob(ShapeBuilder b, Vector3 center, float radius, float up, float down, int segments, Color c)
        {
            b.Cone(center, Vector3.up, radius, up, segments, c);
            b.Cone(center, Vector3.down, radius, down, segments, c * 0.9f);
        }

        static Mesh Build(LifeKind kind, int v)
        {
            var b = new ShapeBuilder();
            if (PlantModels.Build(b, kind, v) || AnimalModels.BuildSimple(b, kind, v)) return b.ToMesh(kind + "_" + v);
            switch (kind)
            {
                case LifeKind.Grass:
                {
                    Color g = Hsv(0.22f + 0.04f * v, 0.55f, 0.72f);
                    b.Cone(new Vector3(0f, 0f, 0f), new Vector3(0.05f, 1f, 0f), 0.05f, 0.24f, 3, g);
                    b.Cone(new Vector3(0.07f, 0f, 0.05f), new Vector3(-0.08f, 1f, 0.05f), 0.045f, 0.2f, 3, g * 0.92f);
                    break;
                }
                case LifeKind.Flower:
                {
                    // Six colours (per cell, IslandLifeSystem) x three shapes: single head, a cluster of three small
                    // heads, and a tall stem with a head and a bud. Butterflies only care about the kind.
                    Color[] heads =
                    {
                        new Color(1f, 0.55f, 0.75f), new Color(1f, 0.85f, 0.25f), new Color(0.95f, 0.95f, 1f),
                        new Color(0.55f, 0.6f, 1f), new Color(1f, 0.42f, 0.3f), new Color(0.8f, 0.55f, 0.95f)
                    };
                    Color head = heads[v % FlowerColours];
                    Color stem = new Color(0.25f, 0.55f, 0.2f);
                    switch (v / FlowerColours)
                    {
                        case 1:
                            b.Cone(new Vector3(0f, 0f, 0f), new Vector3(0.1f, 1f, 0f), 0.02f, 0.18f, 3, stem);
                            b.Cone(new Vector3(0.06f, 0f, 0.05f), new Vector3(-0.05f, 1f, 0.1f), 0.02f, 0.15f, 3, stem);
                            b.Cone(new Vector3(-0.05f, 0f, -0.05f), new Vector3(-0.1f, 1f, -0.05f), 0.02f, 0.16f, 3, stem);
                            b.Cone(new Vector3(0.02f, 0.17f, 0f), Vector3.up, 0.06f, 0.05f, 3, head);
                            b.Cone(new Vector3(0.05f, 0.14f, 0.065f), Vector3.up, 0.05f, 0.045f, 3, head * 0.92f);
                            b.Cone(new Vector3(-0.07f, 0.15f, -0.06f), Vector3.up, 0.055f, 0.045f, 3, head * 1.05f);
                            break;
                        case 2:
                            b.Cone(new Vector3(0f, 0f, 0f), new Vector3(0.04f, 1f, 0f), 0.025f, 0.38f, 3, stem);
                            Blob(b, new Vector3(0.015f, 0.4f, 0f), 0.07f, 0.06f, 0.035f, 4, head);
                            b.Cone(new Vector3(0.06f, 0.24f, 0.02f), new Vector3(0.5f, 1f, 0.2f), 0.035f, 0.08f, 3, head * 0.85f);
                            break;
                        default:
                            b.Cone(new Vector3(0f, 0f, 0f), Vector3.up, 0.025f, 0.24f, 3, stem);
                            Blob(b, new Vector3(0f, 0.26f, 0f), 0.08f, 0.05f, 0.04f, 4, head);
                            break;
                    }
                    break;
                }
                case LifeKind.Palm:
                {
                    // Beach palm: a leaning trunk and six flat fronds (single triangles facing up, drooping at the
                    // tips) so the crown reads from the chase camera; ~30 verts. Sways from the wind channels.
                    Color trunk = new Color(0.5f, 0.36f, 0.22f);
                    Color[] leaf = { new Color(0.3f, 0.62f, 0.28f), new Color(0.36f, 0.66f, 0.24f), new Color(0.26f, 0.56f, 0.3f) };
                    Vector3 lean = new Vector3(0.18f - 0.06f * v, 1f, 0.08f * v).normalized;
                    float height = 1.05f + 0.12f * v;
                    b.Cone(Vector3.zero, lean, 0.055f, height, 4, trunk);
                    Vector3 crown = lean * (height - 0.05f);
                    int fronds = 6;
                    for (int i = 0; i < fronds; i++)
                    {
                        float a = i * Mathf.PI * 2f / fronds + 0.4f * v;
                        Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        Vector3 side = new Vector3(-dir.z, 0f, dir.x) * 0.07f;
                        Vector3 tip = crown + dir * 0.52f + Vector3.down * 0.16f;
                        b.Fin(crown + side * 0.6f, tip + side, tip - side, Vector3.up + dir * 0.4f, leaf[v] * (0.92f + 0.03f * i));
                    }
                    break;
                }
                case LifeKind.Reed:
                {
                    // Waterline reeds: four thin stalks and one dark seed head (45 verts), drawn standing in the
                    // shallows; the whole cluster sways.
                    Color[] stalks = { new Color(0.42f, 0.6f, 0.26f), new Color(0.5f, 0.62f, 0.24f), new Color(0.38f, 0.58f, 0.3f) };
                    Color s = stalks[v];
                    Color headCol = new Color(0.36f, 0.24f, 0.14f);
                    b.Cone(new Vector3(0f, 0f, 0f), new Vector3(0.06f, 1f, 0.02f), 0.02f, 0.62f, 3, s);
                    b.Cone(new Vector3(0.08f, 0f, 0.05f), new Vector3(-0.04f, 1f, 0.08f), 0.018f, 0.52f, 3, s * 0.93f);
                    b.Cone(new Vector3(-0.07f, 0f, 0.04f), new Vector3(-0.08f, 1f, -0.04f), 0.018f, 0.56f, 3, s * 1.06f);
                    b.Cone(new Vector3(0.02f, 0f, -0.08f), new Vector3(0.05f, 1f, -0.09f), 0.02f, 0.48f, 3, s * 0.97f);
                    b.Cone(new Vector3(0.037f, 0.52f, 0.012f), new Vector3(0.06f, 1f, 0.02f), 0.028f, 0.12f, 3, headCol);
                    break;
                }
                case LifeKind.Bush:
                {
                    Color g = Hsv(0.27f + 0.03f * v, 0.62f, 0.5f + 0.08f * v);
                    Blob(b, new Vector3(0f, 0.2f, 0f), 0.3f, 0.2f, 0.18f, 6, g);
                    Blob(b, new Vector3(0.2f, 0.15f, 0.1f), 0.18f, 0.14f, 0.13f, 4, g * 1.15f);
                    break;
                }
                case LifeKind.Tree:
                {
                    Color trunk = new Color(0.4f, 0.27f, 0.15f);
                    b.Cone(new Vector3(0f, 0f, 0f), Vector3.up, 0.06f, 0.45f, 4, trunk);
                    if (v == 0)
                    {
                        Color g = Hsv(0.33f, 0.6f, 0.38f);
                        b.Cone(new Vector3(0f, 0.25f, 0f), Vector3.up, 0.38f, 0.55f, 6, g);
                        b.Cone(new Vector3(0f, 0.55f, 0f), Vector3.up, 0.28f, 0.5f, 6, g * 1.15f);
                    }
                    else if (v == 1)
                    {
                        Color g = Hsv(0.25f, 0.6f, 0.52f);
                        Blob(b, new Vector3(0f, 0.6f, 0f), 0.38f, 0.4f, 0.3f, 6, g);
                    }
                    else
                    {
                        Color g = Hsv(0.4f, 0.55f, 0.32f);
                        b.Cone(new Vector3(0f, 0.22f, 0f), Vector3.up, 0.34f, 0.42f, 5, g);
                        b.Cone(new Vector3(0f, 0.46f, 0f), Vector3.up, 0.27f, 0.4f, 5, g * 1.1f);
                        b.Cone(new Vector3(0f, 0.7f, 0f), Vector3.up, 0.2f, 0.4f, 5, g * 1.2f);
                    }
                    break;
                }
                case LifeKind.Hare:
                {
                    Color c = HareFur[v];
                    b.Box(new Vector3(0f, 0.1f, 0f), new Vector3(0.16f, 0.15f, 0.26f), c, false);
                    b.Box(new Vector3(0f, 0.17f, 0.14f), new Vector3(0.11f, 0.1f, 0.11f), c * 1.05f, false);
                    break;
                }
                case LifeKind.Sheep:
                {
                    b.Box(new Vector3(0f, 0.22f, 0f), new Vector3(0.38f, 0.34f, 0.48f), SheepWool[v], false);
                    b.Box(new Vector3(0f, 0.28f, 0.29f), new Vector3(0.11f, 0.12f, 0.13f), SheepDark, false);
                    break;
                }
                case LifeKind.Goat:
                {
                    Color c = GoatHide[v];
                    b.Box(new Vector3(0f, 0.24f, 0f), new Vector3(0.17f, 0.32f, 0.38f), c, false);
                    b.Box(new Vector3(0f, 0.46f, 0.26f), new Vector3(0.09f, 0.13f, 0.14f), c * 0.95f, false);
                    b.Cone(new Vector3(0.03f, 0.52f, 0.24f), new Vector3(0f, 1f, -0.5f), 0.02f, 0.13f, 3, new Color(0.85f, 0.8f, 0.7f));
                    b.Cone(new Vector3(-0.03f, 0.52f, 0.24f), new Vector3(0f, 1f, -0.5f), 0.02f, 0.13f, 3, new Color(0.85f, 0.8f, 0.7f));
                    break;
                }
                case LifeKind.Ox:
                {
                    Color c = OxHide[v];
                    b.Box(new Vector3(0f, 0.38f, 0f), new Vector3(0.42f, 0.5f, 0.66f), c, false);
                    b.Box(new Vector3(0f, 0.5f, 0.44f), new Vector3(0.22f, 0.22f, 0.26f), c * 0.9f, false);
                    b.Cone(new Vector3(0.12f, 0.6f, 0.46f), new Vector3(1f, 0.6f, 0f), 0.035f, 0.2f, 3, new Color(0.9f, 0.86f, 0.75f));
                    b.Cone(new Vector3(-0.12f, 0.6f, 0.46f), new Vector3(-1f, 0.6f, 0f), 0.035f, 0.2f, 3, new Color(0.9f, 0.86f, 0.75f));
                    break;
                }
                // Phase 4 critters (IslandCrittersSystem) are 6-24 verts each; wings, claws and legs are single
                // flat triangles facing up (the critter material culls nothing), drawn at 0.03-0.1 units.
                case LifeKind.Crab:
                {
                    Color[] shell = { new Color(0.85f, 0.3f, 0.14f), new Color(0.95f, 0.48f, 0.15f), new Color(0.78f, 0.22f, 0.18f) };
                    Color c = shell[v];
                    Color dark = c * 0.8f;
                    b.Cone(new Vector3(0f, 0.05f, 0f), Vector3.up, 0.5f, 0.14f, 4, c);
                    b.Fin(new Vector3(0.22f, 0.05f, 0.35f), new Vector3(0.6f, 0.05f, 0.8f), new Vector3(0.12f, 0.05f, 0.7f), Vector3.up, dark);
                    b.Fin(new Vector3(-0.22f, 0.05f, 0.35f), new Vector3(-0.12f, 0.05f, 0.7f), new Vector3(-0.6f, 0.05f, 0.8f), Vector3.up, dark);
                    b.Fin(new Vector3(-0.4f, 0.03f, 0.15f), new Vector3(-1f, 0.03f, 0.35f), new Vector3(-1f, 0.03f, -0.25f), Vector3.up, dark);
                    b.Fin(new Vector3(0.4f, 0.03f, 0.15f), new Vector3(1f, 0.03f, -0.25f), new Vector3(1f, 0.03f, 0.35f), Vector3.up, dark);
                    break;
                }
                case LifeKind.Turtle:
                {
                    Color[] shell = { new Color(0.16f, 0.34f, 0.14f), new Color(0.2f, 0.3f, 0.12f), new Color(0.12f, 0.28f, 0.18f) };
                    Color head = new Color(0.48f, 0.52f, 0.26f);
                    b.Cone(new Vector3(0f, 0.06f, 0f), Vector3.up, 0.45f, 0.3f, 5, shell[v]);
                    b.Cone(new Vector3(0f, 0.12f, 0.4f), Vector3.forward, 0.1f, 0.24f, 3, head);
                    break;
                }
                case LifeKind.Butterfly:
                {
                    Color[] wing = { new Color(1f, 0.85f, 0.2f), new Color(0.98f, 0.97f, 0.9f), new Color(0.45f, 0.6f, 1f) };
                    Color c = wing[v];
                    b.Fin(new Vector3(0f, 0f, 0.12f), new Vector3(-0.5f, 0f, 0.4f), new Vector3(-0.45f, 0f, -0.35f), Vector3.up, c);
                    b.Fin(new Vector3(0f, 0f, 0.12f), new Vector3(0.45f, 0f, -0.35f), new Vector3(0.5f, 0f, 0.4f), Vector3.up, c);
                    break;
                }
                case LifeKind.Firefly:
                {
                    Color glow = v == 1 ? new Color(1.2f, 1.1f, 0.45f) : new Color(1f, 1.35f, 0.45f);
                    b.Fin(new Vector3(0f, 0f, 0.5f), new Vector3(0.35f, 0f, 0f), new Vector3(0f, 0f, -0.5f), Vector3.up, glow);
                    b.Fin(new Vector3(0f, 0f, 0.5f), new Vector3(0f, 0f, -0.5f), new Vector3(-0.35f, 0f, 0f), Vector3.up, glow);
                    break;
                }
                case LifeKind.Seabird:
                {
                    // Gliding cliff bird for FlockSystem (48 verts instead of the 180-vert Bird): white or grey body,
                    // long flat wings whose tips carry the wing weight but are drawn without flap.
                    Color[] bodies = { new Color(0.96f, 0.96f, 0.98f), new Color(0.72f, 0.74f, 0.78f), new Color(0.95f, 0.95f, 0.95f) };
                    Color[] tips = { new Color(0.8f, 0.82f, 0.86f), new Color(0.5f, 0.52f, 0.56f), new Color(0.25f, 0.25f, 0.28f) };
                    Color body = bodies[v];
                    b.Cone(new Vector3(0f, 0f, 0f), Vector3.up, 0.09f, 0.07f, 4, body);
                    b.Cone(new Vector3(0f, 0f, 0f), Vector3.down, 0.09f, 0.06f, 4, body * 0.9f);
                    b.Fin(new Vector3(-0.06f, 0.02f, 0.08f), new Vector3(-0.6f, 0.04f, 0.03f), new Vector3(-0.06f, 0.02f, -0.1f), Vector3.up, body);
                    b.Fin(new Vector3(-0.6f, 0.04f, 0.03f), new Vector3(-0.6f, 0.04f, -0.12f), new Vector3(-0.06f, 0.02f, -0.1f), Vector3.up, tips[v]);
                    b.Fin(new Vector3(0.06f, 0.02f, 0.08f), new Vector3(0.06f, 0.02f, -0.1f), new Vector3(0.6f, 0.04f, 0.03f), Vector3.up, body);
                    b.Fin(new Vector3(0.6f, 0.04f, 0.03f), new Vector3(0.06f, 0.02f, -0.1f), new Vector3(0.6f, 0.04f, -0.12f), Vector3.up, tips[v]);
                    b.Fin(new Vector3(-0.07f, 0.01f, -0.22f), new Vector3(0.07f, 0.01f, -0.22f), new Vector3(0f, 0.01f, -0.08f), Vector3.up, tips[v]);
                    b.Cone(new Vector3(0f, 0f, 0.09f), Vector3.forward, 0.03f, 0.09f, 3, new Color(0.95f, 0.75f, 0.25f));
                    break;
                }
                default:
                {
                    Color[] bodies = { new Color(0.3f, 0.55f, 0.95f), new Color(0.95f, 0.35f, 0.3f), new Color(1f, 0.8f, 0.25f) };
                    Color body = bodies[v];
                    b.Sphere(Vector3.zero, new Vector3(0.1f, 0.08f, 0.16f), body, 0);
                    b.Box(new Vector3(0.19f, 0.02f, 0f), new Vector3(0.28f, 0.015f, 0.12f), body * 0.85f);
                    b.Box(new Vector3(-0.19f, 0.02f, 0f), new Vector3(0.28f, 0.015f, 0.12f), body * 0.85f);
                    b.Box(new Vector3(0f, 0f, -0.2f), new Vector3(0.07f, 0.01f, 0.1f), body * 0.8f);
                    b.Cone(new Vector3(0f, 0f, 0.15f), Vector3.forward, 0.035f, 0.09f, 4, new Color(1f, 0.7f, 0.2f));
                    break;
                }
            }
            return b.ToMesh(kind + "_" + v);
        }
    }
}
