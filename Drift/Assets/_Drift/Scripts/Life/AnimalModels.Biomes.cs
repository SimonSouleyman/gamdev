using UnityEngine;

namespace Drift.Life
{
    // Tropical, nordic and savanna herd animals: the simple box templates (60-130 verts, LifeMeshes.GetTemplate)
    // and the detail rigs (230-370 verts) with their special poses.
    public static partial class AnimalModels
    {
        internal static readonly Color[] CapybaraFur = { new Color(0.56f, 0.38f, 0.22f), new Color(0.64f, 0.46f, 0.27f), new Color(0.46f, 0.31f, 0.2f) };
        internal static readonly Color[] FlamingoPink = { new Color(1f, 0.5f, 0.6f), new Color(1f, 0.66f, 0.7f), new Color(0.98f, 0.4f, 0.48f) };
        internal static readonly Color[] TortoiseShell = { new Color(0.36f, 0.33f, 0.17f), new Color(0.44f, 0.37f, 0.2f), new Color(0.28f, 0.31f, 0.2f) };
        internal static readonly Color TortoiseSkin = new Color(0.52f, 0.5f, 0.32f);
        internal static readonly Color[] ReindeerHide = { new Color(0.5f, 0.41f, 0.34f), new Color(0.62f, 0.55f, 0.48f), new Color(0.4f, 0.33f, 0.29f) };
        internal static readonly Color ReindeerPale = new Color(0.92f, 0.9f, 0.84f);
        internal static readonly Color[] PenguinBack = { new Color(0.09f, 0.1f, 0.14f), new Color(0.16f, 0.19f, 0.26f), new Color(0.07f, 0.07f, 0.09f) };
        internal static readonly Color PenguinWhite = new Color(0.97f, 0.97f, 0.95f);
        internal static readonly Color PenguinOrange = new Color(0.98f, 0.6f, 0.14f);
        internal static readonly Color[] FoxFur = { new Color(0.97f, 0.97f, 0.99f), new Color(0.7f, 0.75f, 0.82f), new Color(0.94f, 0.9f, 0.82f) };
        internal static readonly Color[] ZebraWhite = { new Color(0.98f, 0.98f, 0.98f), new Color(0.96f, 0.93f, 0.87f), new Color(0.9f, 0.91f, 0.94f) };
        internal static readonly Color[] ZebraBlack = { new Color(0.09f, 0.09f, 0.1f), new Color(0.2f, 0.15f, 0.12f), new Color(0.12f, 0.12f, 0.17f) };
        internal static readonly Color[] GiraffeBase = { new Color(0.96f, 0.83f, 0.55f), new Color(0.98f, 0.89f, 0.66f), new Color(0.9f, 0.76f, 0.5f) };
        internal static readonly Color[] GiraffeSpot = { new Color(0.62f, 0.35f, 0.15f), new Color(0.72f, 0.46f, 0.2f), new Color(0.5f, 0.29f, 0.14f) };
        internal static readonly Color[] MeerkatFur = { new Color(0.78f, 0.7f, 0.58f), new Color(0.66f, 0.6f, 0.52f), new Color(0.86f, 0.78f, 0.64f) };

        public static bool BuildSimple(ShapeBuilder b, LifeKind kind, int v)
        {
            switch (kind)
            {
                case LifeKind.Capybara:
                {
                    Color c = CapybaraFur[v];
                    b.Box(new Vector3(0f, 0.17f, -0.02f), new Vector3(0.27f, 0.27f, 0.46f), c, false);
                    b.Box(new Vector3(0f, 0.25f, 0.29f), new Vector3(0.18f, 0.18f, 0.24f), c * 1.05f, false);
                    return true;
                }
                case LifeKind.Flamingo:
                {
                    Color c = FlamingoPink[v];
                    b.Box(new Vector3(0f, 0.62f, -0.03f), new Vector3(0.18f, 0.18f, 0.34f), c);
                    b.Cone(new Vector3(0f, 0.55f, 0f), Vector3.down, 0.035f, 0.55f, 3, c * 0.85f);
                    b.Cone(new Vector3(0f, 0.66f, 0.1f), new Vector3(0f, 1f, 0.22f), 0.06f, 0.46f, 3, c);
                    b.Cone(new Vector3(0f, 1.08f, 0.15f), new Vector3(0f, -0.5f, 1f), 0.06f, 0.13f, 3, c * 0.95f);
                    b.Cone(new Vector3(0f, 1.04f, 0.23f), new Vector3(0f, -0.8f, 0.6f), 0.03f, 0.1f, 3, new Color(0.15f, 0.1f, 0.1f));
                    return true;
                }
                case LifeKind.Tortoise:
                {
                    b.Cone(new Vector3(0f, 0.08f, 0f), Vector3.up, 0.32f, 0.3f, 6, TortoiseShell[v]);
                    b.Cone(new Vector3(0f, 0.08f, 0f), Vector3.down, 0.32f, 0.08f, 6, TortoiseShell[v] * 0.75f);
                    b.Box(new Vector3(0f, 0.17f, 0.37f), new Vector3(0.12f, 0.11f, 0.17f), TortoiseSkin, false);
                    return true;
                }
                case LifeKind.Reindeer:
                {
                    Color c = ReindeerHide[v];
                    b.Box(new Vector3(0f, 0.38f, -0.02f), new Vector3(0.23f, 0.33f, 0.5f), c, false);
                    b.Box(new Vector3(0f, 0.6f, 0.32f), new Vector3(0.12f, 0.14f, 0.22f), ReindeerPale * 0.92f, false);
                    b.Cone(new Vector3(0.04f, 0.66f, 0.28f), new Vector3(0.55f, 1f, -0.3f), 0.03f, 0.34f, 3, LifeMeshes.Horn * 0.8f);
                    b.Cone(new Vector3(-0.04f, 0.66f, 0.28f), new Vector3(-0.55f, 1f, -0.3f), 0.03f, 0.34f, 3, LifeMeshes.Horn * 0.8f);
                    return true;
                }
                case LifeKind.Penguin:
                {
                    Color c = PenguinBack[v];
                    b.Box(new Vector3(0f, 0.26f, 0f), new Vector3(0.23f, 0.5f, 0.2f), c, false);
                    b.Patch(new Vector3(-0.08f, 0.45f, 0.103f), new Vector3(0.08f, 0.45f, 0.103f), new Vector3(0.08f, 0.05f, 0.103f), new Vector3(-0.08f, 0.05f, 0.103f), Vector3.forward, PenguinWhite);
                    b.Box(new Vector3(0f, 0.6f, 0.01f), new Vector3(0.17f, 0.18f, 0.17f), c, false);
                    b.Cone(new Vector3(0f, 0.59f, 0.09f), new Vector3(0f, -0.15f, 1f), 0.04f, 0.12f, 3, PenguinOrange);
                    return true;
                }
                case LifeKind.ArcticFox:
                {
                    Color c = FoxFur[v];
                    b.Box(new Vector3(0f, 0.18f, 0f), new Vector3(0.2f, 0.2f, 0.34f), c, false);
                    b.Box(new Vector3(0f, 0.31f, 0.23f), new Vector3(0.15f, 0.13f, 0.17f), c, false);
                    b.Cone(new Vector3(0f, 0.24f, -0.16f), new Vector3(0f, 0.2f, -1f), 0.11f, 0.34f, 4, c * 0.97f);
                    return true;
                }
                case LifeKind.Zebra:
                {
                    Color w = ZebraWhite[v], k = ZebraBlack[v];
                    b.Box(new Vector3(0f, 0.4f, 0f), new Vector3(0.23f, 0.34f, 0.52f), w, false);
                    b.Box(new Vector3(0f, 0.66f, 0.34f), new Vector3(0.11f, 0.15f, 0.24f), Color.Lerp(w, k, 0.25f), false);
                    return true;
                }
                case LifeKind.Giraffe:
                {
                    Color c = GiraffeBase[v];
                    for (int sx = -1; sx <= 1; sx += 2)
                    {
                        b.Cone(new Vector3(0.09f * sx, 0.7f, 0.17f), Vector3.down, 0.045f, 0.7f, 3, c * 0.95f);
                        b.Cone(new Vector3(0.09f * sx, 0.7f, -0.19f), Vector3.down, 0.045f, 0.7f, 3, c * 0.95f);
                    }
                    b.Box(new Vector3(0f, 0.8f, -0.03f), new Vector3(0.25f, 0.28f, 0.5f), c);
                    b.Tube(new Vector3(0f, 0.88f, 0.17f), new Vector3(0f, 1.6f, 0.47f), Vector3.forward, new Vector2(0.15f, 0.18f), new Vector2(0.08f, 0.09f), 3, c, false, false);
                    b.Cone(new Vector3(0f, 1.63f, 0.42f), new Vector3(0f, -0.25f, 1f), 0.075f, 0.3f, 4, c);
                    return true;
                }
                case LifeKind.Meerkat:
                {
                    Color c = MeerkatFur[v];
                    b.Box(new Vector3(0f, 0.1f, 0f), new Vector3(0.11f, 0.13f, 0.3f), c, false);
                    b.Box(new Vector3(0f, 0.19f, 0.18f), new Vector3(0.1f, 0.09f, 0.13f), c * 0.92f, false);
                    b.Cone(new Vector3(0f, 0.11f, -0.15f), new Vector3(0f, 0.15f, -1f), 0.03f, 0.26f, 3, c * 0.7f);
                    return true;
                }
            }
            return false;
        }

        static void TwoLegs(Rig r, float x, float z, float hipY, float size, float footSize, Color col, float footZ = 0f)
        {
            r.Leg(new Vector3(x, hipY, z), new Vector3(x, 0f, z + footZ), size, footSize, 3, col, 1);
            r.Leg(new Vector3(-x, hipY, z), new Vector3(-x, 0f, z + footZ), size, footSize, 3, col, -1);
        }

        static void Ears(ShapeBuilder b, float x, float y, float z, Vector3 dir, float radius, float length, Color col)
        {
            b.Cone(new Vector3(x, y, z), dir, radius, length, 3, col);
            b.Cone(new Vector3(-x, y, z), new Vector3(-dir.x, dir.y, dir.z), radius, length, 3, col);
        }

        static PlantTemplate BuildBiome(LifeKind kind, int v, bool young, int pose)
        {
            var r = new Rig { headScale = young ? 1.32f : 1f };
            var b = r.b;
            Color eyeDark = new Color(0.06f, 0.05f, 0.05f);
            Color eyeLight = new Color(0.95f, 0.93f, 0.85f);
            Vector3 up = Vector3.up, fwd = Vector3.forward;
            bool special = pose == PoseSpecial;
            bool call = pose == PoseCall;
            switch (kind)
            {
                case LifeKind.Capybara:
                {
                    Color c = CapybaraFur[v];
                    r.coat = Markings.Coarse;
                    r.legLen = 0.1f;
                    FourLegs(r, 0.085f, 0.13f, -0.16f, 0.15f, 0.075f, 0.06f, c * 0.72f);
                    b.Tube(new Vector3(0f, 0.235f, -0.25f), new Vector3(0f, 0.245f, 0.17f), up, new Vector2(0.27f, 0.26f), new Vector2(0.25f, 0.25f), 6, c);
                    b.Lump(new Vector3(0f, 0.25f, -0.2f), new Vector3(0.137f, 0.128f, 0.1f), c * 0.96f);
                    r.Body();
                    Vector3 ha = new Vector3(0f, 0.3f, 0.13f), hb = new Vector3(0f, 0.275f, 0.43f);
                    r.headPivot = ha;
                    b.Tube(ha, hb, up, new Vector2(0.185f, 0.185f), new Vector2(0.145f, 0.135f), 4, c * 1.05f);
                    int nose = b.VertexCount;
                    b.Patch(new Vector3(-0.045f, 0.31f, 0.433f), new Vector3(0.045f, 0.31f, 0.433f), new Vector3(0.04f, 0.255f, 0.433f), new Vector3(-0.04f, 0.255f, 0.433f), fwd, c * 0.45f);
                    r.Plain(nose);
                    Ears(b, 0.065f, 0.385f, 0.2f, new Vector3(0.4f, 1f, -0.2f), 0.028f, 0.05f, c * 0.75f);
                    r.Eyes(ha, hb, 0.185f, 0.145f, 0.4f, 0.055f, 0.014f, eyeDark);
                    r.Head();
                    break;
                }
                case LifeKind.Flamingo:
                {
                    Color c = FlamingoPink[v];
                    Color legCol = new Color(0.95f, 0.45f, 0.5f);
                    r.coat = Markings.Feather;
                    r.legCoat = Markings.Skin;
                    r.legLen = 0.5f;
                    r.legLift = 0.25f;
                    r.squash = 0.1f;
                    if (special)
                    {
                        r.Leg(new Vector3(0.01f, 0.54f, 0f), new Vector3(0.01f, 0f, 0f), 0.035f, 0.03f, 3, legCol, 0);
                        int tucked = b.VertexCount;
                        b.Tube(new Vector3(-0.04f, 0.56f, -0.02f), new Vector3(-0.04f, 0.43f, 0.07f), fwd, new Vector2(0.035f, 0.035f), new Vector2(0.03f, 0.03f), 3, legCol, false, false);
                        b.Tube(new Vector3(-0.04f, 0.43f, 0.07f), new Vector3(-0.04f, 0.52f, -0.06f), fwd, new Vector2(0.03f, 0.03f), new Vector2(0.025f, 0.025f), 3, legCol, false, true);
                        r.Paint(tucked, Markings.Skin);
                    }
                    else TwoLegs(r, 0.04f, 0f, 0.54f, 0.035f, 0.03f, legCol, 0.01f);
                    b.Sphere(new Vector3(0f, 0.63f, -0.03f), new Vector3(0.105f, 0.1f, 0.19f), c, 0);
                    b.Cone(new Vector3(0f, 0.64f, -0.19f), new Vector3(0f, -0.25f, -1f), 0.055f, 0.13f, 3, c * 0.95f);
                    r.TipCone(new Vector3(0f, 0.685f, -0.1f), new Vector3(0f, 0.05f, -1f), 0.06f, 0.17f, 3, new Color(0.78f, 0.18f, 0.3f), 0.42f);
                    r.Body();
                    Vector3 na = new Vector3(0f, 0.67f, 0.11f);
                    Vector3 beakDark = new Vector3(0.12f, 0.08f, 0.08f);
                    if (special)
                    {
                        Vector3 nm = new Vector3(0f, 0.86f, 0.12f), nb = new Vector3(0f, 0.8f, -0.02f);
                        b.Tube(na, nm, fwd, new Vector2(0.06f, 0.06f), new Vector2(0.045f, 0.045f), 4, c, false, false);
                        b.Tube(nm, nb, fwd, new Vector2(0.045f, 0.045f), new Vector2(0.045f, 0.045f), 4, c, false, false);
                        r.Neck(na, nb);
                        Vector3 ha = nb, hb = new Vector3(0f, 0.75f, -0.15f);
                        r.headPivot = nb;
                        b.Tube(ha, hb, up, new Vector2(0.07f, 0.075f), new Vector2(0.05f, 0.05f), 4, c);
                        int bill = b.VertexCount;
                        b.Cone(hb, new Vector3(0f, -0.2f, -1f), 0.03f, 0.1f, 3, new Color(beakDark.x, beakDark.y, beakDark.z));
                        r.Plain(bill);
                        r.Eyes(ha, hb, 0.07f, 0.05f, 0.4f, 0.012f, 0.011f, new Color(0.95f, 0.85f, 0.3f));
                        r.maxHeadLever = 0.01f;
                    }
                    else
                    {
                        Vector3 nm = new Vector3(0f, 0.95f, 0.2f), nb = new Vector3(0f, 1.08f, 0.14f);
                        b.Tube(na, nm, fwd, new Vector2(0.06f, 0.06f), new Vector2(0.045f, 0.045f), 4, c, false, false);
                        b.Tube(nm, nb, fwd, new Vector2(0.045f, 0.045f), new Vector2(0.045f, 0.045f), 4, c, false, false);
                        r.Neck(na, nb);
                        Vector3 ha = nb, hb = new Vector3(0f, 1.03f, 0.27f);
                        r.headPivot = nb;
                        b.Tube(ha, hb, up, new Vector2(0.07f, 0.075f), new Vector2(0.05f, 0.05f), 4, c);
                        Vector3 beak = new Vector3(0f, -0.8f, 0.6f).normalized;
                        int bill = b.VertexCount;
                        b.Cone(hb, beak, 0.03f, 0.12f, 3, new Color(0.96f, 0.86f, 0.82f));
                        b.Cone(hb + beak * 0.06f, beak, 0.022f, 0.07f, 3, new Color(beakDark.x, beakDark.y, beakDark.z));
                        r.Plain(bill);
                        r.Eyes(ha, hb, 0.07f, 0.05f, 0.4f, 0.012f, 0.011f, new Color(0.95f, 0.85f, 0.3f));
                        r.maxHeadLever = 1.1f;
                    }
                    r.Head();
                    break;
                }
                case LifeKind.Tortoise:
                {
                    Color shell = TortoiseShell[v];
                    Color skin = TortoiseSkin;
                    float drop = special ? 0.075f : 0f;
                    r.coat = Markings.Scutes;
                    r.headCoat = r.legCoat = Markings.Skin;
                    r.legLen = special ? 0.005f : 0.075f;
                    r.squash = 0.12f;
                    r.legLift = 0.5f;
                    if (special) FourLegs(r, 0.15f, 0.15f, -0.15f, 0.03f, 0.07f, 0.07f, skin * 0.8f);
                    else FourLegs(r, 0.18f, 0.18f, -0.18f, 0.11f, 0.1f, 0.085f, skin * 0.85f);
                    b.Sphere(new Vector3(0f, 0.25f - drop, 0f), new Vector3(0.27f, 0.2f, 0.33f), shell, 0);
                    b.Lump(new Vector3(0f, 0.37f - drop, 0f), new Vector3(0.16f, 0.09f, 0.2f), shell * 1.3f);
                    int tail = b.VertexCount;
                    b.Cone(new Vector3(0f, 0.14f - drop, -0.31f), new Vector3(0f, -0.3f, -1f), 0.03f, special ? 0.02f : 0.08f, 3, skin);
                    r.Paint(tail, Markings.Skin);
                    r.Body();
                    Vector3 ha = special ? new Vector3(0f, 0.11f, 0.17f) : new Vector3(0f, 0.18f, 0.26f);
                    Vector3 hb = special ? new Vector3(0f, 0.115f, 0.32f) : new Vector3(0f, 0.25f, 0.5f);
                    r.headPivot = ha;
                    b.Tube(ha, hb, up, new Vector2(0.1f, 0.09f), new Vector2(0.09f, 0.08f), 4, skin);
                    r.Eyes(ha, hb, 0.1f, 0.09f, 0.72f, 0.015f, 0.012f, eyeDark);
                    if (special) r.maxHeadLever = 0.01f;
                    r.Head();
                    break;
                }
                case LifeKind.Reindeer:
                {
                    Color c = ReindeerHide[v];
                    Color pale = ReindeerPale;
                    r.legLen = 0.27f;
                    FourLegs(r, 0.085f, 0.16f, -0.17f, 0.34f, 0.07f, 0.05f, c * 0.5f, 3);
                    b.Tube(new Vector3(0f, 0.42f, -0.26f), new Vector3(0f, 0.44f, 0.2f), up, new Vector2(0.24f, 0.25f), new Vector2(0.25f, 0.28f), 6, c);
                    b.Lump(new Vector3(0f, 0.45f, -0.27f), new Vector3(0.09f, 0.09f, 0.04f), pale);
                    b.Lump(new Vector3(0f, 0.37f, 0.2f), new Vector3(0.115f, 0.14f, 0.1f), pale);
                    r.Body();
                    Vector3 na = new Vector3(0f, 0.46f, 0.16f), nb = new Vector3(0f, 0.64f, 0.3f);
                    b.Tube(na, nb, up, new Vector2(0.12f, 0.17f), new Vector2(0.09f, 0.1f), 4, pale * 0.95f, false, false);
                    r.Neck(na, nb);
                    Vector3 ha = new Vector3(0f, 0.66f, 0.26f), hb = new Vector3(0f, 0.58f, 0.48f);
                    r.headPivot = nb;
                    b.Tube(ha, hb, up, new Vector2(0.11f, 0.12f), new Vector2(0.07f, 0.065f), 4, c * 0.95f);
                    int nose = b.VertexCount;
                    b.Patch(new Vector3(-0.025f, 0.6f, 0.484f), new Vector3(0.025f, 0.6f, 0.484f), new Vector3(0.025f, 0.565f, 0.484f), new Vector3(-0.025f, 0.565f, 0.484f), fwd, eyeDark);
                    r.Plain(nose);
                    Ears(b, 0.055f, 0.67f, 0.28f, new Vector3(1f, 0.25f, -0.3f), 0.025f, 0.09f, c * 0.85f);
                    Color horn = LifeMeshes.Horn * 0.8f;
                    int antlers = b.VertexCount;
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        Vector3 root = new Vector3(0.035f * sgn, 0.71f, 0.27f);
                        Vector3 dir = new Vector3(0.5f * sgn, 1f, -0.4f).normalized;
                        if (young) { b.Cone(root, dir, 0.02f, 0.07f, 3, horn); continue; }
                        b.Cone(root, dir, 0.024f, 0.36f, 3, horn);
                        b.Cone(root + dir * 0.1f, new Vector3(0.25f * sgn, 0.5f, 1f), 0.016f, 0.15f, 3, horn);
                        b.Cone(root + dir * 0.23f, new Vector3(-0.3f * sgn, 1f, 0.35f), 0.016f, 0.14f, 3, horn);
                    }
                    r.Plain(antlers);
                    r.Eyes(ha, hb, 0.11f, 0.07f, 0.4f, 0.015f, 0.014f, eyeDark);
                    r.Head();
                    break;
                }
                case LifeKind.Penguin:
                {
                    Color c = PenguinBack[v];
                    r.coat = Markings.Feather;
                    r.legCoat = Markings.None;
                    r.legLen = 0.035f;
                    r.squash = 0.35f;
                    r.legLift = 0.7f;
                    TwoLegs(r, 0.06f, 0.025f, 0.06f, 0.07f, 0.1f, PenguinOrange);
                    b.Tube(new Vector3(0f, 0.04f, 0f), new Vector3(0f, 0.5f, 0.01f), fwd, new Vector2(0.26f, 0.22f), new Vector2(0.17f, 0.15f), 6, c);
                    b.Lump(new Vector3(0f, 0.27f, 0.07f), new Vector3(0.09f, 0.2f, 0.055f), PenguinWhite);
                    // Flippers: along the body, forward on the belly slide, spread wide and raised for the sky call.
                    Vector3 flipper = call ? new Vector3(1f, 0.35f, -0.15f) : special ? new Vector3(1f, -0.3f, -0.05f) : new Vector3(0.25f, -1f, -0.05f);
                    b.Cone(new Vector3(0.12f, 0.43f, 0f), flipper, 0.05f, 0.27f, 3, c * 1.3f);
                    b.Cone(new Vector3(-0.12f, 0.43f, 0f), new Vector3(-flipper.x, flipper.y, flipper.z), 0.05f, 0.27f, 3, c * 1.3f);
                    b.Cone(new Vector3(0f, 0.09f, -0.1f), new Vector3(0f, -0.15f, -1f), 0.05f, 0.09f, 3, c);
                    r.Body();
                    r.headPivot = new Vector3(0f, 0.5f, 0f);
                    Vector3 hc = new Vector3(0f, 0.59f, 0.01f);
                    b.Sphere(hc, new Vector3(0.095f, 0.1f, 0.1f), c, 0);
                    r.Eyes(hc, hc, 0.16f, 0.16f, 0f, -0.005f, 0.036f, PenguinWhite);
                    r.Eyes(hc + fwd * 0.012f, hc + fwd * 0.012f, 0.19f, 0.19f, 0f, 0.008f, 0.013f, eyeDark);
                    int bill = b.VertexCount;
                    b.Cone(new Vector3(0f, 0.58f, 0.09f), new Vector3(0f, -0.15f, 1f), 0.036f, 0.12f, 3, PenguinOrange);
                    r.Plain(bill);
                    r.Head();
                    if (special)
                    {
                        r.bakePitch = 84f;
                        r.headPitch = -62f;
                        r.bakeOffset = new Vector3(0f, 0.135f, -0.3f);
                        r.maxHeadLever = 0.01f;
                    }
                    else if (call)
                    {
                        // Head thrown back, beak pointing at the sky.
                        r.headPitch = -58f;
                        r.maxHeadLever = 0.01f;
                    }
                    break;
                }
                case LifeKind.ArcticFox:
                {
                    Color c = FoxFur[v];
                    r.coat = Markings.Fluff;
                    r.legLen = 0.13f;
                    FourLegs(r, 0.065f, 0.11f, -0.12f, 0.18f, 0.055f, 0.045f, c * 0.9f, 3);
                    b.Tube(new Vector3(0f, 0.21f, -0.19f), new Vector3(0f, 0.23f, 0.14f), up, new Vector2(0.2f, 0.19f), new Vector2(0.2f, 0.21f), 6, c);
                    b.Tube(new Vector3(0f, 0.22f, -0.17f), new Vector3(0f, 0.27f, -0.42f), up, new Vector2(0.08f, 0.08f), new Vector2(0.19f, 0.19f), 4, c * 0.95f, false, false);
                    b.Cone(new Vector3(0f, 0.27f, -0.42f), new Vector3(0f, 0.2f, -1f), 0.134f, 0.17f, 4, c * 1.02f);
                    r.Body();
                    Vector3 na = new Vector3(0f, 0.24f, 0.12f), nb = new Vector3(0f, 0.33f, 0.2f);
                    b.Tube(na, nb, up, new Vector2(0.11f, 0.13f), new Vector2(0.1f, 0.1f), 4, c, false, false);
                    r.Neck(na, nb);
                    Vector3 ha = new Vector3(0f, 0.345f, 0.15f), hb = new Vector3(0f, 0.31f, 0.3f), hs = new Vector3(0f, 0.295f, 0.385f);
                    r.headPivot = nb;
                    b.Tube(ha, hb, up, new Vector2(0.18f, 0.15f), new Vector2(0.075f, 0.068f), 4, c);
                    b.Tube(hb, hs, up, new Vector2(0.068f, 0.058f), new Vector2(0.04f, 0.036f), 4, c * 0.98f, false, true);
                    int nose = b.VertexCount;
                    b.Patch(hs + new Vector3(-0.017f, 0.016f, 0.002f), hs + new Vector3(0.017f, 0.016f, 0.002f), hs + new Vector3(0.017f, -0.012f, 0.002f), hs + new Vector3(-0.017f, -0.012f, 0.002f), fwd, eyeDark);
                    r.Plain(nose);
                    Ears(b, 0.06f, 0.4f, 0.18f, new Vector3(0.3f, 1f, -0.1f), 0.045f, 0.12f, c * 0.9f);
                    r.Eyes(ha, hb, 0.18f, 0.075f, 0.5f, 0.015f, 0.014f, eyeDark);
                    r.Head();
                    break;
                }
                case LifeKind.Zebra:
                {
                    Color w = ZebraWhite[v], k = ZebraBlack[v];
                    r.coat = Markings.Zebra;
                    r.legLen = 0.29f;
                    FourLegs(r, 0.085f, 0.17f, -0.18f, 0.36f, 0.07f, 0.05f, w * 0.92f, 3);
                    const int bands = 5;
                    for (int i = 0; i < bands; i++)
                    {
                        float za = -0.27f + 0.096f * i, zb = za + 0.096f;
                        float wa = 0.24f + 0.015f * Mathf.Sin(i * 1.1f);
                        b.Tube(new Vector3(0f, 0.45f, za), new Vector3(0f, 0.45f, zb), up, new Vector2(wa, 0.27f), new Vector2(wa, 0.27f), 4, w, i == 0, i == bands - 1);
                    }
                    r.TipCone(new Vector3(0f, 0.52f, -0.27f), new Vector3(0f, -1f, -0.3f), 0.025f, 0.3f, 3, w, 0.55f);
                    r.Body();
                    Vector3 na = new Vector3(0f, 0.5f, 0.17f), nb = new Vector3(0f, 0.73f, 0.33f), nmid = (na + nb) * 0.5f;
                    b.Tube(na, nmid, up, new Vector2(0.13f, 0.2f), new Vector2(0.115f, 0.155f), 4, w, false, false);
                    b.Tube(nmid, nb, up, new Vector2(0.115f, 0.155f), new Vector2(0.1f, 0.12f), 4, w, false, false);
                    Vector3 crest = nmid + new Vector3(0f, 0.15f, -0.1f);
                    int mane = b.VertexCount;
                    b.Fin(na + new Vector3(0f, 0.08f, -0.06f), crest, nb + new Vector3(0f, 0.05f, -0.05f), Vector3.right, k);
                    b.Fin(na + new Vector3(0f, 0.08f, -0.06f), crest, nb + new Vector3(0f, 0.05f, -0.05f), Vector3.left, k);
                    r.Plain(mane);
                    r.Neck(na, nb);
                    Vector3 ha = new Vector3(0f, 0.75f, 0.29f), hb = new Vector3(0f, 0.64f, 0.53f);
                    r.headPivot = nb;
                    b.Tube(ha, hb, up, new Vector2(0.12f, 0.14f), new Vector2(0.085f, 0.085f), 4, w);
                    int muzzle = b.VertexCount;
                    b.Tube(hb + new Vector3(0f, 0.002f, -0.012f), new Vector3(0f, 0.612f, 0.59f), up, new Vector2(0.09f, 0.09f), new Vector2(0.072f, 0.066f), 4, k, false, true);
                    r.Plain(muzzle);
                    Ears(b, 0.05f, 0.78f, 0.31f, new Vector3(0.45f, 1f, -0.2f), 0.025f, 0.09f, w * 0.9f);
                    r.Eyes(ha, hb, 0.12f, 0.085f, 0.4f, 0.02f, 0.015f, eyeDark);
                    r.Head();
                    break;
                }
                case LifeKind.Giraffe:
                {
                    Color c = GiraffeBase[v], s = GiraffeSpot[v];
                    r.coat = Markings.Giraffe;
                    r.legFoot = 0.15f;
                    r.legLen = 0.62f;
                    r.legLift = 0.18f;
                    r.maxHeadLever = 1.5f;
                    FourLegs(r, 0.09f, 0.17f, -0.19f, 0.72f, 0.075f, 0.05f, c * 0.95f, 3);
                    b.Tube(new Vector3(0f, 0.76f, -0.26f), new Vector3(0f, 0.86f, 0.2f), up, new Vector2(0.24f, 0.26f), new Vector2(0.27f, 0.34f), 4, c);
                    b.Cone(new Vector3(0f, 0.84f, -0.27f), new Vector3(0f, -1f, -0.25f), 0.02f, 0.38f, 3, c * 0.8f);
                    int tuft = b.VertexCount;
                    b.Cone(new Vector3(0f, 0.5f, -0.36f), new Vector3(0f, -1f, -0.1f), 0.035f, 0.12f, 3, s * 0.6f);
                    r.Plain(tuft);
                    r.Body();
                    Vector3 na = new Vector3(0f, 0.88f, 0.17f), nb = new Vector3(0f, 1.6f, 0.47f);
                    b.Tube(na, nb, up, new Vector2(0.15f, 0.2f), new Vector2(0.085f, 0.09f), 4, c, false, false);
                    int mane = b.VertexCount;
                    b.Fin(na + new Vector3(0f, 0.12f, -0.08f), Vector3.Lerp(na, nb, 0.5f) + new Vector3(0f, 0.06f, -0.09f), nb + new Vector3(0f, 0.02f, -0.05f), Vector3.right, s * 0.8f);
                    b.Fin(na + new Vector3(0f, 0.12f, -0.08f), Vector3.Lerp(na, nb, 0.5f) + new Vector3(0f, 0.06f, -0.09f), nb + new Vector3(0f, 0.02f, -0.05f), Vector3.left, s * 0.8f);
                    r.Plain(mane);
                    r.Neck(na, nb);
                    Vector3 ha = new Vector3(0f, 1.63f, 0.43f), hb = new Vector3(0f, 1.55f, 0.7f);
                    r.headPivot = nb;
                    b.Tube(ha, hb, up, new Vector2(0.115f, 0.125f), new Vector2(0.075f, 0.07f), 4, c);
                    int nose = b.VertexCount;
                    b.Patch(hb + new Vector3(-0.03f, 0.03f, 0.003f), hb + new Vector3(0.03f, 0.03f, 0.003f), hb + new Vector3(0.03f, -0.03f, 0.003f), hb + new Vector3(-0.03f, -0.03f, 0.003f), fwd, s * 0.7f);
                    Ears(b, 0.03f, 1.685f, 0.46f, new Vector3(0.12f, 1f, -0.2f), 0.016f, 0.09f, s * 0.8f);
                    r.Plain(nose);
                    Ears(b, 0.055f, 1.655f, 0.45f, new Vector3(1f, 0.2f, -0.25f), 0.026f, 0.1f, c * 0.95f);
                    r.Eyes(ha, hb, 0.115f, 0.075f, 0.35f, 0.02f, 0.015f, eyeDark);
                    r.Head();
                    break;
                }
                default:
                {
                    Color c = MeerkatFur[v];
                    Color dark = new Color(0.2f, 0.15f, 0.12f);
                    r.coat = Markings.Meerkat;
                    r.headCoat = r.legCoat = Markings.Fur;
                    if (special)
                    {
                        r.legLen = 0.02f;
                        r.squash = 0.1f;
                        r.legLift = 0f;
                        r.Leg(new Vector3(0.045f, 0.1f, -0.02f), new Vector3(0.045f, 0f, 0.01f), 0.055f, 0.045f, 3, c * 0.85f, 0);
                        r.Leg(new Vector3(-0.045f, 0.1f, -0.02f), new Vector3(-0.045f, 0f, 0.01f), 0.055f, 0.045f, 3, c * 0.85f, 0);
                        b.Tube(new Vector3(0f, 0.06f, -0.025f), new Vector3(0f, 0.4f, 0.015f), fwd, new Vector2(0.115f, 0.105f), new Vector2(0.09f, 0.085f), 6, c);
                        b.Lump(new Vector3(0f, 0.23f, 0.04f), new Vector3(0.045f, 0.13f, 0.03f), c * 1.15f);
                        b.Cone(new Vector3(0.042f, 0.33f, 0.04f), new Vector3(0f, -1f, 0.4f), 0.018f, 0.12f, 3, c * 0.8f);
                        b.Cone(new Vector3(-0.042f, 0.33f, 0.04f), new Vector3(0f, -1f, 0.4f), 0.018f, 0.12f, 3, c * 0.8f);
                        r.TipCone(new Vector3(0f, 0.05f, -0.06f), new Vector3(0f, -0.14f, -1f), 0.025f, 0.26f, 3, c * 0.7f, 0.7f);
                        r.upright = true;
                        r.Body();
                        Vector3 ha = new Vector3(0f, 0.435f, -0.02f), hb = new Vector3(0f, 0.425f, 0.12f);
                        r.headPivot = ha;
                        b.Tube(ha, hb, up, new Vector2(0.1f, 0.09f), new Vector2(0.045f, 0.04f), 4, c);
                        int noseUp = b.VertexCount;
                        b.Patch(hb + new Vector3(-0.014f, 0.012f, 0.002f), hb + new Vector3(0.014f, 0.012f, 0.002f), hb + new Vector3(0.014f, -0.01f, 0.002f), hb + new Vector3(-0.014f, -0.01f, 0.002f), fwd, dark);
                        Ears(b, 0.045f, 0.46f, 0f, new Vector3(1f, 0.3f, -0.2f), 0.018f, 0.035f, dark);
                        r.Plain(noseUp);
                        r.Eyes(ha, hb, 0.1f, 0.045f, 0.45f, 0.008f, 0.02f, dark);
                        r.maxHeadLever = 0.3f;
                        r.Head();
                        break;
                    }
                    r.legLen = 0.06f;
                    FourLegs(r, 0.04f, 0.09f, -0.1f, 0.09f, 0.04f, 0.03f, c * 0.85f, 3);
                    b.Tube(new Vector3(0f, 0.115f, -0.16f), new Vector3(0f, 0.125f, 0.11f), up, new Vector2(0.1f, 0.1f), new Vector2(0.1f, 0.105f), 6, c);
                    r.TipCone(new Vector3(0f, 0.12f, -0.16f), new Vector3(0f, 0.15f, -1f), 0.025f, 0.26f, 3, c * 0.7f, 0.7f);
                    r.Body();
                    Vector3 na = new Vector3(0f, 0.14f, 0.09f), nb = new Vector3(0f, 0.2f, 0.15f);
                    b.Tube(na, nb, up, new Vector2(0.08f, 0.09f), new Vector2(0.075f, 0.075f), 4, c, false, false);
                    r.Neck(na, nb);
                    Vector3 h0 = new Vector3(0f, 0.215f, 0.12f), h1 = new Vector3(0f, 0.19f, 0.25f);
                    r.headPivot = nb;
                    b.Tube(h0, h1, up, new Vector2(0.1f, 0.09f), new Vector2(0.045f, 0.04f), 4, c);
                    int nose = b.VertexCount;
                    b.Patch(h1 + new Vector3(-0.014f, 0.012f, 0.002f), h1 + new Vector3(0.014f, 0.012f, 0.002f), h1 + new Vector3(0.014f, -0.01f, 0.002f), h1 + new Vector3(-0.014f, -0.01f, 0.002f), fwd, dark);
                    Ears(b, 0.045f, 0.245f, 0.13f, new Vector3(1f, 0.3f, -0.2f), 0.018f, 0.035f, dark);
                    r.Plain(nose);
                    r.Eyes(h0, h1, 0.1f, 0.045f, 0.45f, 0.008f, 0.02f, dark);
                    r.Head();
                    break;
                }
            }
            return r.Finish(kind + (young ? "_young_" : "_detail_") + v + (special ? "_pose" : call ? "_call" : ""));
        }
    }
}
