using System.Collections.Generic;
using Drift.Life;
using UnityEngine;

namespace Drift.Bridge
{
    // Present: one arm held out towards the menu beside her, body turned that way. Comfort: both arms a little open,
    // head tilted, soft eyes (game over).
    public enum TildaPose { Idle, Talk, Wave, Cheer, Sleepy, Present, Comfort }

    public sealed class TildaParts
    {
        public Transform root, body, lava;
        // Per eye: the ball carries iris and pupil and turns to look, the lids are sphere shells that turn about
        // the eye's X axis. "L" is the viewer's left.
        public Transform ballL, ballR, lidUpL, lidUpR, lidLowL, lidLowR;
        public Transform browL, browR, glasses, glintL, glintR, flower, curl;
        public Vector3 browRestL, browRestR, glassesRest;
        public Quaternion browRotL, browRotR;
        public float lensRadius;
        public Transform mouth, smile, mouthFill, armL, armR;
        public Transform[] puffs, zs, sparks;
        public int vertexCount, rendererCount;
    }

    // Tilda, the friendly little island volcano: low-poly vertex-colour meshes built at runtime on Drift/VertexColor
    // with _LightAmount 0. Her shading is baked into the vertex colours from a fixed key light, so the scene's day /
    // night cycle and storms never darken her. She faces -Z; every animated part is its own child transform.
    public static class TildaModel
    {
        public const float CraterY = 1.86f;
        const int Segments = 18;
        const float FaceLift = 0.022f;

        static readonly float[] RingY = { 0.00f, 0.07f, 0.13f, 0.21f, 0.28f, 0.50f, 0.75f, 1.00f, 1.22f, 1.42f, 1.60f, 1.74f, 1.84f, 1.90f };
        static readonly float[] RingR = { 1.54f, 1.41f, 1.30f, 1.17f, 1.07f, 1.04f, 0.97f, 0.87f, 0.76f, 0.65f, 0.55f, 0.48f, 0.44f, 0.40f };
        static readonly Color[] BandColor =
        {
            new Color(0.95f, 0.85f, 0.62f), new Color(0.42f, 0.68f, 0.33f), new Color(0.48f, 0.73f, 0.35f), new Color(0.56f, 0.79f, 0.39f),
            new Color(0.78f, 0.59f, 0.44f), new Color(0.74f, 0.55f, 0.41f), new Color(0.70f, 0.51f, 0.39f), new Color(0.64f, 0.46f, 0.36f),
            new Color(0.56f, 0.39f, 0.32f), new Color(0.48f, 0.33f, 0.28f), new Color(0.41f, 0.28f, 0.25f), new Color(0.35f, 0.24f, 0.22f),
            new Color(0.31f, 0.21f, 0.2f),
        };

        static readonly Color EyeDark = new Color(0.1f, 0.07f, 0.1f);
        static readonly Color LavaHot = new Color(1f, 0.93f, 0.4f);
        static readonly Color LavaWarm = new Color(1f, 0.56f, 0.13f);
        static readonly Vector3 KeyLight = new Vector3(-0.45f, 0.72f, -0.53f).normalized;

        public static Material CreateMaterial()
        {
            var shader = Shader.Find("Drift/VertexColor");
            if (shader == null)
            {
                Debug.LogError("TildaModel: shader 'Drift/VertexColor' not found.");
                return null;
            }
            var m = new Material(shader) { name = "TildaVertexColor", hideFlags = HideFlags.HideAndDontSave };
            m.SetFloat("_LightAmount", 0f);
            return m;
        }

        // ---------------------------------------------------------------- surface

        static float Wobble(float angleDeg, float y)
        {
            float w = Mathf.Clamp01(1f - y / 0.28f);
            float a = angleDeg * Mathf.Deg2Rad;
            return 1f + w * (0.06f * Mathf.Sin(3f * a + 1.1f) + 0.035f * Mathf.Sin(5f * a + 0.4f));
        }

        static float RadiusAt(float y)
        {
            if (y <= RingY[0]) return RingR[0];
            for (int i = 1; i < RingY.Length; i++)
                if (y <= RingY[i]) return Mathf.Lerp(RingR[i - 1], RingR[i], (y - RingY[i - 1]) / (RingY[i] - RingY[i - 1]));
            return RingR[RingR.Length - 1];
        }

        static Vector3 Radial(float angleDeg)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a), 0f, -Mathf.Cos(a));
        }

        static Vector3 Surface(float angleDeg, float y, float lift, out Vector3 normal)
        {
            float slope = (RadiusAt(y + 0.02f) - RadiusAt(y - 0.02f)) / 0.04f;
            Vector3 radial = Radial(angleDeg);
            normal = (radial - Vector3.up * slope).normalized;
            return radial * (RadiusAt(y) * Wobble(angleDeg, y)) + Vector3.up * y + normal * lift;
        }

        static float Hash(int a, int b)
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
            h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
            return (h & 0xFFFF) / 65535f;
        }

        // ---------------------------------------------------------------- build

        public static TildaParts Build(Transform parent, Material material, int layer)
        {
            var parts = new TildaParts();
            var made = new List<MeshRenderer>();
            parts.root = Node(parent, "Tilda", layer);
            parts.body = Node(parts.root, "Body", layer);

            Part(parts.body, "Rock", BuildRock(), material, layer, made);
            Part(parts.body, "Glow", BuildGlow(), material, layer, made);
            parts.lava = Part(parts.body, "Lava", BuildLava(), material, layer, made);
            parts.lava.localPosition = new Vector3(0f, CraterY + 0.035f, 0f);

            Part(parts.body, "Face", BuildFaceDetails(), material, layer, made);
            BuildEye(parts, true, material, layer, made);
            BuildEye(parts, false, material, layer, made);
            BuildBrow(parts, true, material, layer, made);
            BuildBrow(parts, false, material, layer, made);
            BuildGlasses(parts, material, layer, made);
            BuildFlower(parts, material, layer, made);
            BuildCurl(parts, material, layer, made);

            parts.mouth = Node(parts.body, "Mouth", layer);
            Orient(parts.mouth, 0f, MouthY, FaceLift);
            parts.smile = Part(parts.mouth, "Smile", BuildSmile(), material, layer, made);
            Part(parts.mouth, "Dimples", BuildDimples(), material, layer, made);
            parts.mouthFill = Part(parts.mouth, "Open", BuildMouthFill(), material, layer, made);

            parts.armL = Part(parts.body, "ArmL", BuildArm(-1f), material, layer, made);
            parts.armR = Part(parts.body, "ArmR", BuildArm(1f), material, layer, made);
            parts.armL.localPosition = Surface(-80f, 0.95f, -0.06f, out _);
            parts.armR.localPosition = Surface(80f, 0.95f, -0.06f, out _);

            parts.puffs = new Transform[4];
            for (int i = 0; i < parts.puffs.Length; i++) parts.puffs[i] = Part(parts.root, "Puff" + i, BuildPuff(i), material, layer, made);
            parts.zs = new Transform[3];
            for (int i = 0; i < parts.zs.Length; i++) parts.zs[i] = Part(parts.root, "Z" + i, BuildZ(), material, layer, made);
            parts.sparks = new Transform[7];
            for (int i = 0; i < parts.sparks.Length; i++) parts.sparks[i] = Part(parts.root, "Spark" + i, BuildSpark(i), material, layer, made);

            foreach (var r in made) parts.vertexCount += r.GetComponent<MeshFilter>().sharedMesh.vertexCount;
            parts.rendererCount = made.Count;
            return parts;
        }

        static Transform Node(Transform parent, string name, int layer)
        {
            var go = new GameObject(name) { layer = layer, hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild };
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static Transform Part(Transform parent, string name, Mesh mesh, Material material, int layer, List<MeshRenderer> made)
        {
            var t = Node(parent, name, layer);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = t.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            made.Add(r);
            return t;
        }

        // Local +Z of a face part is the surface normal; with her facing -Z, local +X is the viewer's left.
        static void Orient(Transform t, float angleDeg, float y, float lift)
        {
            t.localPosition = Surface(angleDeg, y, lift, out var normal);
            t.localRotation = Quaternion.LookRotation(normal, Vector3.up);
        }

        static Mesh Lit(ShapeBuilder b, string name, float dark = 0.52f, float bright = 1.1f) => Lit(b, name, dark, bright, Quaternion.identity);

        // frame: the rotation of the part's node in body space, for meshes built in a node's own frame.
        static Mesh Lit(ShapeBuilder b, string name, float dark, float bright, Quaternion frame)
        {
            var m = b.ToMesh(name);
            var normals = m.normals;
            var colors = m.colors;
            Vector3 light = Quaternion.Inverse(frame) * KeyLight;
            for (int i = 0; i < colors.Length; i++)
            {
                float k = Mathf.Lerp(dark, bright, Mathf.Clamp01(Vector3.Dot(normals[i], light) * 0.5f + 0.5f));
                colors[i] = new Color(colors[i].r * k, colors[i].g * k, colors[i].b * k, 1f);
            }
            m.colors = colors;
            return m;
        }

        // ---------------------------------------------------------------- body

        static void Band(ShapeBuilder b, float y0, float r0, float y1, float r1, Color col, int band, bool inward = false)
        {
            float step = 360f / Segments;
            for (int j = 0; j < Segments; j++)
            {
                float a0 = (j - 0.5f) * step, a1 = (j + 0.5f) * step;
                Vector3 p00 = Radial(a0) * (r0 * Wobble(a0, y0)) + Vector3.up * y0;
                Vector3 p01 = Radial(a1) * (r0 * Wobble(a1, y0)) + Vector3.up * y0;
                Vector3 p10 = Radial(a0) * (r1 * Wobble(a0, y1)) + Vector3.up * y1;
                Vector3 p11 = Radial(a1) * (r1 * Wobble(a1, y1)) + Vector3.up * y1;
                Vector3 outward = Radial(j * step) * (inward ? -1f : 1f) + Vector3.up * 0.6f;
                Color c = col * (0.965f + 0.07f * Hash(band, j));
                b.Fin(p00, p01, p11, outward, c);
                b.Fin(p00, p11, p10, outward, c);
            }
        }

        static Mesh BuildRock()
        {
            var b = new ShapeBuilder();
            for (int i = 0; i < RingY.Length - 1; i++) Band(b, RingY[i], RingR[i], RingY[i + 1], RingR[i + 1], BandColor[i], i);
            Band(b, 1.9f, 0.4f, 1.9f, 0.33f, new Color(0.38f, 0.26f, 0.23f), 20);

            Tree(b, -33f, 0.17f, 1f, 0);
            Tree(b, 35f, 0.16f, 0.9f, 1);
            Tree(b, 57f, 0.12f, 0.62f, 2);
            Tree(b, -118f, 0.15f, 0.95f, 1);
            Tree(b, 128f, 0.15f, 0.85f, 0);
            Bush(b, -56f, 0.13f);
            Bush(b, 14f, 0.08f);
            Flower(b, -14f, 0.1f, new Color(1f, 0.6f, 0.78f));
            Flower(b, -5f, 0.17f, new Color(1f, 0.9f, 0.35f));
            Flower(b, 24f, 0.13f, new Color(0.98f, 0.97f, 1f));
            Flower(b, -45f, 0.19f, new Color(1f, 0.9f, 0.35f));
            Flower(b, 46f, 0.09f, new Color(1f, 0.6f, 0.78f));
            Flower(b, -24f, 0.2f, new Color(0.98f, 0.97f, 1f));
            b.Sphere(Surface(0f, 0.955f, 0.015f, out _), new Vector3(0.082f, 0.07f, 0.075f), new Color(0.86f, 0.55f, 0.46f), 1);
            return Lit(b, "TildaRock");
        }

        static void Tree(ShapeBuilder b, float angle, float y, float size, int variant)
        {
            Vector3 p = Surface(angle, y, -0.02f, out _);
            Color g = variant == 0 ? new Color(0.2f, 0.5f, 0.27f) : variant == 1 ? new Color(0.26f, 0.56f, 0.26f) : new Color(0.3f, 0.6f, 0.3f);
            b.Cone(p, Vector3.up, 0.045f * size, 0.22f * size, 4, new Color(0.45f, 0.3f, 0.18f));
            b.Cone(p + Vector3.up * 0.13f * size, Vector3.up, 0.19f * size, 0.27f * size, 6, g);
            b.Cone(p + Vector3.up * 0.28f * size, Vector3.up, 0.14f * size, 0.27f * size, 6, g * 1.18f);
        }

        static void Bush(ShapeBuilder b, float angle, float y)
        {
            Vector3 p = Surface(angle, y, 0f, out _);
            Color g = new Color(0.34f, 0.62f, 0.3f);
            b.Cone(p, Vector3.up, 0.13f, 0.14f, 6, g);
            b.Cone(p + Radial(angle + 60f) * 0.1f, Vector3.up, 0.085f, 0.1f, 5, g * 1.15f);
        }

        static void Flower(ShapeBuilder b, float angle, float y, Color head)
        {
            Vector3 p = Surface(angle, y, 0f, out _);
            b.Cone(p, Vector3.up, 0.018f, 0.1f, 3, new Color(0.3f, 0.58f, 0.24f));
            b.Cone(p + Vector3.up * 0.1f, Vector3.up, 0.06f, 0.05f, 5, head);
            b.Cone(p + Vector3.up * 0.1f, Vector3.down, 0.06f, 0.035f, 5, head * 0.88f);
        }

        // Crater wall and lava dribbles glow: they skip the baked key light.
        static Mesh BuildGlow()
        {
            var b = new ShapeBuilder();
            Band(b, 1.9f, 0.33f, CraterY - 0.12f, 0.27f, new Color(0.86f, 0.36f, 0.12f), 30, true);
            Drip(b, -42f, 0.2f, 1.5f);
            Drip(b, 31f, 0.15f, 1.69f);
            Drip(b, 84f, 0.18f, 1.57f);
            Drip(b, -110f, 0.18f, 1.62f);
            Drip(b, 170f, 0.2f, 1.5f);
            return b.ToMesh("TildaGlow");
        }

        static void Drip(ShapeBuilder b, float angle, float halfWidth, float endY)
        {
            float w = halfWidth / 0.4f * Mathf.Rad2Deg;
            Vector3 up = Vector3.up * 0.012f;
            Vector3 inL = Radial(angle - w) * 0.3f + Vector3.up * 1.9f + up;
            Vector3 inR = Radial(angle + w) * 0.3f + Vector3.up * 1.9f + up;
            Vector3 prevL = Radial(angle - w) * 0.415f + Vector3.up * 1.9f + up;
            Vector3 prevR = Radial(angle + w) * 0.415f + Vector3.up * 1.9f + up;
            b.Fin(inL, inR, prevR, Vector3.up, LavaHot);
            b.Fin(inL, prevR, prevL, Vector3.up, LavaHot);

            const int steps = 5;
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                float y = Mathf.Lerp(1.9f, endY, t);
                float narrow = Mathf.Lerp(1f, 0.7f, Mathf.Sqrt(t));
                float r = RadiusAt(y);
                float wa = halfWidth * narrow / r * Mathf.Rad2Deg;
                Vector3 l = Surface(angle - wa, y, 0.02f, out _);
                Vector3 rr = Surface(angle + wa, y, 0.02f, out var n);
                Color c = Color.Lerp(LavaHot, LavaWarm, t);
                b.Fin(prevL, prevR, rr, n, c);
                b.Fin(prevL, rr, l, n, c);
                prevL = l;
                prevR = rr;
            }
            float tipW = halfWidth * 0.36f / RadiusAt(endY) * Mathf.Rad2Deg;
            Vector3 tipL = Surface(angle - tipW, endY - halfWidth * 0.5f, 0.02f, out _);
            Vector3 tipR = Surface(angle + tipW, endY - halfWidth * 0.5f, 0.02f, out _);
            Vector3 tip = Surface(angle, endY - halfWidth * 0.72f, 0.02f, out var tn);
            b.Fin(prevL, prevR, tipR, tn, LavaWarm);
            b.Fin(prevL, tipR, tipL, tn, LavaWarm);
            b.Fin(tipL, tipR, tip, tn, LavaWarm);
        }

        static Mesh BuildLava()
        {
            var b = new ShapeBuilder();
            const int seg = 12;
            for (int j = 0; j < seg; j++)
            {
                float a0 = j * 360f / seg, a1 = (j + 1) * 360f / seg;
                Vector3 c = Vector3.up * 0.13f;
                Vector3 m0 = Radial(a0) * 0.17f + Vector3.up * 0.1f, m1 = Radial(a1) * 0.17f + Vector3.up * 0.1f;
                Vector3 o0 = Radial(a0) * 0.335f, o1 = Radial(a1) * 0.335f;
                b.Fin(c, m0, m1, Vector3.up, LavaHot * (0.97f + 0.06f * Hash(40, j)));
                Color ring = Color.Lerp(LavaHot, LavaWarm, 0.75f) * (0.95f + 0.1f * Hash(41, j));
                b.Fin(m0, o0, o1, Vector3.up, ring);
                b.Fin(m0, o1, m1, Vector3.up, ring);
            }
            return b.ToMesh("TildaLava");
        }

        // ---------------------------------------------------------------- face

        static void Disc(ShapeBuilder b, Vector2 c, float rx, float ry, float z, int seg, Color col)
        {
            Vector3 centre = new Vector3(c.x, c.y, z);
            for (int j = 0; j < seg; j++)
            {
                float a0 = j * Mathf.PI * 2f / seg, a1 = (j + 1) * Mathf.PI * 2f / seg;
                b.Fin(centre, centre + new Vector3(Mathf.Cos(a0) * rx, Mathf.Sin(a0) * ry, 0f), centre + new Vector3(Mathf.Cos(a1) * rx, Mathf.Sin(a1) * ry, 0f), Vector3.forward, col);
            }
        }

        // A band along (rx cos t, ySign * ry sin t) with round caps.
        static void Arc(ShapeBuilder b, float rx, float ry, float ySign, float half, float fromDeg, float toDeg, int seg, float z, Color col)
        {
            Vector3 P(float t, float off) => new Vector3(Mathf.Cos(t) * (rx + off), ySign * Mathf.Sin(t) * (ry + off), z);
            for (int j = 0; j < seg; j++)
            {
                float t0 = Mathf.Lerp(fromDeg, toDeg, j / (float)seg) * Mathf.Deg2Rad;
                float t1 = Mathf.Lerp(fromDeg, toDeg, (j + 1) / (float)seg) * Mathf.Deg2Rad;
                b.Fin(P(t0, -half), P(t0, half), P(t1, half), Vector3.forward, col);
                b.Fin(P(t0, -half), P(t1, half), P(t1, -half), Vector3.forward, col);
            }
            Vector3 e0 = P(fromDeg * Mathf.Deg2Rad, 0f), e1 = P(toDeg * Mathf.Deg2Rad, 0f);
            Disc(b, e0, half, half, z, 8, col);
            Disc(b, e1, half, half, z, 8, col);
        }

        const float EyeAngle = 22.5f, EyeY = 1.07f, BrowY = 1.325f, MouthY = 0.79f;
        // Radial layers of an eye, in units of the eye radius: the 24 bit depth buffer keeps them apart.
        const float IrisR = 1.03f, PupilR = 1.06f, ShineR = 1.09f, LidLowR = 1.13f, LidUpR = 1.17f;

        static readonly Color Sclera = new Color(1f, 0.97f, 0.9f);
        static readonly Color IrisCore = new Color(1f, 0.82f, 0.3f);
        static readonly Color IrisRim = new Color(0.7f, 0.34f, 0.1f);
        static readonly Color LidSkin = new Color(0.7f, 0.49f, 0.4f);
        static readonly Color LidLowSkin = new Color(0.69f, 0.5f, 0.39f);
        static readonly Color Crease = new Color(0.4f, 0.25f, 0.22f);
        static readonly Color Wrinkle = new Color(0.5f, 0.33f, 0.28f);
        static readonly Color Lichen = new Color(0.66f, 0.79f, 0.45f);
        static readonly Color Gold = new Color(1f, 0.82f, 0.36f);

        // theta: towards the eye's X poles, phi: about the X axis from the front (+Z) up to +Y.
        static Vector3 OnEye(float thetaDeg, float phiDeg, float r)
        {
            float t = thetaDeg * Mathf.Deg2Rad, p = phiDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(t), Mathf.Cos(t) * Mathf.Sin(p), Mathf.Cos(t) * Mathf.Cos(p)) * r;
        }

        static void Shell(ShapeBuilder b, float r, float phiFrom, float phiTo, int segPhi, Color col)
        {
            const int segTheta = 10;
            for (int i = 0; i < segTheta; i++)
                for (int j = 0; j < segPhi; j++)
                {
                    float t0 = Mathf.Lerp(-90f, 90f, i / (float)segTheta), t1 = Mathf.Lerp(-90f, 90f, (i + 1) / (float)segTheta);
                    float p0 = Mathf.Lerp(phiFrom, phiTo, j / (float)segPhi), p1 = Mathf.Lerp(phiFrom, phiTo, (j + 1) / (float)segPhi);
                    Vector3 a = OnEye(t0, p0, r), bb = OnEye(t1, p0, r), c = OnEye(t1, p1, r), d = OnEye(t0, p1, r);
                    Vector3 outward = a + bb + c + d;
                    if (i < segTheta - 1) b.Fin(a, bb, c, outward, col);
                    if (i > 0) b.Fin(a, c, d, outward, col);
                }
        }

        // A cap of the sphere around `towards`, coloured in rings from the centre to the rim.
        static void Cap(ShapeBuilder b, Vector3 towards, float r, float halfAngleDeg, int rings, int seg, Color centre, Color rim)
        {
            var q = Quaternion.FromToRotation(Vector3.forward, towards.normalized);
            Vector3 P(float thetaDeg, float a)
            {
                float t = thetaDeg * Mathf.Deg2Rad;
                return q * (new Vector3(Mathf.Sin(t) * Mathf.Cos(a), Mathf.Sin(t) * Mathf.Sin(a), Mathf.Cos(t)) * r);
            }
            for (int i = 0; i < rings; i++)
            {
                float th0 = halfAngleDeg * i / rings, th1 = halfAngleDeg * (i + 1) / rings;
                Color col = Color.Lerp(centre, rim, rings == 1 ? 0f : i / (float)(rings - 1));
                for (int j = 0; j < seg; j++)
                {
                    float a0 = j * Mathf.PI * 2f / seg, a1 = (j + 1) * Mathf.PI * 2f / seg;
                    Vector3 p00 = P(th0, a0), p01 = P(th0, a1), p10 = P(th1, a0), p11 = P(th1, a1);
                    Vector3 outward = p10 + p11;
                    b.Fin(p00, p10, p11, outward, col);
                    if (i > 0) b.Fin(p00, p11, p01, outward, col);
                }
            }
        }

        // The eye node is scaled to the eye's radii, so everything inside is built on a unit sphere and the ball and
        // the lids can turn like the real thing. Her left eye (viewer's left) is a touch bigger than the right.
        static void BuildEye(TildaParts parts, bool left, Material material, int layer, List<MeshRenderer> made)
        {
            var eye = Node(parts.body, left ? "EyeL" : "EyeR", layer);
            Orient(eye, left ? -EyeAngle : EyeAngle, left ? EyeY : EyeY + 0.012f, 0f);
            eye.localScale = left ? new Vector3(0.178f, 0.198f, 0.1f) : new Vector3(0.166f, 0.186f, 0.096f);

            var w = new ShapeBuilder();
            w.Sphere(Vector3.zero, Vector3.one, Sclera, 2);
            Cap(w, new Vector3(0.36f, 0.5f, 1f), ShineR, 15f, 1, 8, Color.white, Color.white);
            Cap(w, new Vector3(-0.3f, -0.34f, 1f), ShineR, 6.5f, 1, 6, new Color(1f, 0.96f, 0.86f), Color.white);
            Part(eye, "White", w.ToMesh("TildaEyeWhite"), material, layer, made);

            var i = new ShapeBuilder();
            Cap(i, Vector3.forward, IrisR, 41f, 3, 14, IrisCore, IrisRim);
            Cap(i, Vector3.forward, PupilR, 20f, 1, 12, EyeDark, EyeDark);
            var ball = Part(eye, "Ball", i.ToMesh("TildaEyeBall"), material, layer, made);

            var u = new ShapeBuilder();
            Shell(u, LidUpR, 0f, 175f, 7, LidSkin);
            Shell(u, LidUpR + 0.01f, -10f, 0f, 1, EyeDark);
            var up = Part(eye, "LidUp", u.ToMesh("TildaLidUp"), material, layer, made);

            var l = new ShapeBuilder();
            Shell(l, LidLowR, -175f, 0f, 7, LidLowSkin);
            Shell(l, LidLowR + 0.01f, 0f, 5f, 1, Crease);
            var low = Part(eye, "LidLow", l.ToMesh("TildaLidLow"), material, layer, made);

            if (left) { parts.ballL = ball; parts.lidUpL = up; parts.lidLowL = low; }
            else { parts.ballR = ball; parts.lidUpR = up; parts.lidLowR = low; }
        }

        // Thick brows of pale lichen tufts, biggest at the inner end. Local +X is the viewer's left.
        static void BuildBrow(TildaParts parts, bool left, Material material, int layer, List<MeshRenderer> made)
        {
            var brow = Node(parts.body, left ? "BrowL" : "BrowR", layer);
            Orient(brow, left ? -EyeAngle - 1f : EyeAngle + 1f, left ? BrowY : BrowY + 0.03f, 0.035f);
            float outer = left ? 1f : -1f;
            var b = new ShapeBuilder();
            for (int k = 0; k < 6; k++)
            {
                float u = k / 5f;
                float x = outer * Mathf.Lerp(-0.175f, 0.2f, u);
                float y = 0.06f * Mathf.Sin(u * Mathf.PI * 0.9f) - 0.05f * u * u;
                float size = Mathf.Lerp(0.092f, 0.046f, u) * (1f + 0.16f * (Hash(60 + (left ? 0 : 9), k) - 0.5f));
                Color c = Lichen * (0.92f + 0.16f * Hash(61, k + (left ? 0 : 5)));
                b.Sphere(new Vector3(x, y, 0.012f), new Vector3(size * 1.3f, size * 0.8f, size * 0.75f), c, 0);
            }
            b.Sphere(new Vector3(outer * -0.1f, 0.095f, 0.01f), new Vector3(0.05f, 0.045f, 0.04f), Lichen * 1.05f, 0);
            Part(brow, "Tufts", Lit(b, "TildaBrow", 0.74f, 1.08f, brow.localRotation), material, layer, made);
            if (left) { parts.browL = brow; parts.browRestL = brow.localPosition; parts.browRotL = brow.localRotation; }
            else { parts.browR = brow; parts.browRestR = brow.localPosition; parts.browRotR = brow.localRotation; }
        }

        static void Ring(ShapeBuilder b, Vector3 c, Vector3 n, float radius, float thick, int seg, Color col)
        {
            Vector3 t = Vector3.Cross(Vector3.up, n).normalized, u = Vector3.Cross(n, t);
            for (int j = 0; j < seg; j++)
            {
                float a0 = j * Mathf.PI * 2f / seg, a1 = (j + 1) * Mathf.PI * 2f / seg;
                Vector3 p0 = c + (t * Mathf.Cos(a0) + u * Mathf.Sin(a0)) * radius, p1 = c + (t * Mathf.Cos(a1) + u * Mathf.Sin(a1)) * radius;
                b.Tube(p0, p1, n, new Vector2(thick, thick), new Vector2(thick, thick), 4, col, false, false);
            }
        }

        // Round wire glasses low on her nose; the pivot is the nose bridge, so they can slip and bounce.
        static void BuildGlasses(TildaParts parts, Material material, int layer, List<MeshRenderer> made)
        {
            const float radius = 0.232f, thick = 0.03f, lift = 0.11f, y = EyeY - 0.035f;
            parts.lensRadius = radius;
            Vector3 pivot = Surface(0f, y, lift, out _);
            var g = Node(parts.body, "Glasses", layer);
            g.localPosition = pivot;
            parts.glasses = g;
            parts.glassesRest = pivot;

            var b = new ShapeBuilder();
            for (int side = -1; side <= 1; side += 2)
            {
                Surface(side * EyeAngle * 0.55f, y, 0f, out var n);
                Vector3 c = Surface(side * (EyeAngle + 0.5f), y, lift, out _) - pivot;
                c.z = 0f;
                Ring(b, c, n, radius, thick, 20, Gold);

                Vector3 across = Vector3.Cross(Vector3.up, n).normalized * -side;
                Vector3 hinge = c + across * radius;
                Vector3 ear = Surface(side * 64f, y + 0.07f, 0.0f, out _) - pivot;
                b.Tube(hinge, ear, Vector3.up, new Vector2(thick, thick), new Vector2(thick * 0.8f, thick * 0.8f), 4, Gold * 0.92f, true, false);

                var glint = Node(g, side < 0 ? "GlintL" : "GlintR", layer);
                glint.localPosition = c + n * 0.012f;
                glint.localRotation = Quaternion.LookRotation(n, Vector3.up) * Quaternion.Euler(0f, 0f, -32f);
                var s = new ShapeBuilder();
                Color shine = new Color(1f, 1f, 0.97f);
                s.Fin(new Vector3(-0.013f, -0.5f, 0f), new Vector3(0.013f, -0.5f, 0f), new Vector3(0.013f, 0.5f, 0f), Vector3.forward, shine);
                s.Fin(new Vector3(-0.013f, -0.5f, 0f), new Vector3(0.013f, 0.5f, 0f), new Vector3(-0.013f, 0.5f, 0f), Vector3.forward, shine);
                s.Fin(new Vector3(0.03f, -0.4f, 0f), new Vector3(0.038f, -0.4f, 0f), new Vector3(0.038f, 0.4f, 0f), Vector3.forward, shine);
                s.Fin(new Vector3(0.03f, -0.4f, 0f), new Vector3(0.038f, 0.4f, 0f), new Vector3(0.03f, 0.4f, 0f), Vector3.forward, shine);
                var streak = Part(glint, "Streak", s.ToMesh("TildaGlint"), material, layer, made);
                if (side < 0) parts.glintL = streak; else parts.glintR = streak;
            }
            // Bridge: a little arch over the nose between the inner rims.
            Surface(0f, y, 0f, out var fn);
            Vector3 cl = Surface(-(EyeAngle + 0.5f), y, lift, out _) - pivot, cr = Surface(EyeAngle + 0.5f, y, lift, out _) - pivot;
            cl.z = cr.z = 0f;
            Vector3 il = cl + Vector3.right * (radius * 0.93f) + Vector3.up * 0.05f, ir = cr - Vector3.right * (radius * 0.93f) + Vector3.up * 0.05f;
            Vector3 prev = il;
            for (int k = 1; k <= 5; k++)
            {
                float u = k / 5f;
                Vector3 p = Vector3.Lerp(il, ir, u) + Vector3.up * (0.035f * Mathf.Sin(u * Mathf.PI)) + fn * (0.03f * Mathf.Sin(u * Mathf.PI));
                b.Tube(prev, p, fn, new Vector2(thick, thick), new Vector2(thick, thick), 4, Gold, false, false);
                prev = p;
            }
            Part(g, "Frame", Lit(b, "TildaGlasses", 0.78f, 1.12f), material, layer, made);
        }

        // A frangipani tucked behind her "ear" on the viewer's left of the crater rim.
        static void BuildFlower(TildaParts parts, Material material, int layer, List<MeshRenderer> made)
        {
            var f = Node(parts.body, "Flower", layer);
            Vector3 pos = Surface(-47f, 1.66f, 0.03f, out var n);
            f.localPosition = pos;
            f.localRotation = Quaternion.LookRotation((n * 0.55f + Vector3.back * 0.75f + Vector3.up * 0.25f).normalized, Vector3.up) * Quaternion.Euler(0f, 0f, 12f);
            parts.flower = f;

            var b = new ShapeBuilder();
            Color leaf = new Color(0.33f, 0.62f, 0.3f);
            b.Sphere(new Vector3(0.17f, -0.1f, -0.03f), new Vector3(0.15f, 0.06f, 0.03f), leaf, 0);
            b.Sphere(new Vector3(0.05f, -0.19f, -0.03f), new Vector3(0.06f, 0.13f, 0.03f), leaf * 1.12f, 0);
            for (int k = 0; k < 5; k++)
            {
                float a = (90f + k * 72f) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                b.Sphere(dir * 0.115f + Vector3.forward * 0.012f, new Vector3(0.105f, 0.105f, 0.03f), new Color(1f, 0.98f, 0.95f), 1);
                b.Sphere(dir * 0.05f + Vector3.forward * 0.03f, new Vector3(0.055f, 0.055f, 0.022f), new Color(1f, 0.83f, 0.3f), 0);
            }
            b.Sphere(Vector3.forward * 0.045f, new Vector3(0.04f, 0.04f, 0.03f), new Color(1f, 0.62f, 0.2f), 0);
            Part(f, "Bloom", Lit(b, "TildaFlower", 0.8f, 1.06f, f.localRotation), material, layer, made);
        }

        // One dribble of lava ends in a cheeky curl over her forehead (viewer's right). It glows: no baked light.
        static void BuildCurl(TildaParts parts, Material material, int layer, List<MeshRenderer> made)
        {
            var c = Node(parts.body, "Curl", layer);
            Orient(c, 31f, 1.6f, 0.035f);
            parts.curl = c;
            var b = new ShapeBuilder();
            const int steps = 16;
            Vector3 centre = new Vector3(-0.1f, -0.12f, 0.02f);
            Vector3 prev = new Vector3(0f, 0.1f, -0.02f);
            float prevSize = 0.1f;
            for (int k = 0; k <= steps; k++)
            {
                float u = k / (float)steps;
                float a = (62f - 470f * u) * Mathf.Deg2Rad;
                float r = Mathf.Lerp(0.16f, 0.035f, u);
                Vector3 p = centre + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * r + Vector3.forward * (0.05f * u);
                float size = Mathf.Lerp(0.095f, 0.04f, u);
                b.Tube(prev, p, Vector3.forward, new Vector2(prevSize, prevSize * 0.8f), new Vector2(size, size * 0.8f), 5, Color.Lerp(LavaHot, LavaWarm, 0.25f + 0.75f * u), k == 0, k == steps);
                prev = p;
                prevSize = size;
            }
            Part(c, "Lava", b.ToMesh("TildaCurl"), material, layer, made);
        }

        static void Stroke(ShapeBuilder b, Vector2 from, Vector2 to, float half, Color col)
        {
            Vector2 d = (to - from).normalized, nrm = new Vector2(-d.y, d.x) * half;
            b.Fin(from - nrm, from + nrm, to + nrm * 0.45f, Vector3.forward, col);
            b.Fin(from - nrm, to + nrm * 0.45f, to - nrm * 0.45f, Vector3.forward, col);
            Disc(b, from, half, half, 0f, 6, col);
        }

        // Moves what was built since `from` out of its flat local frame onto her face (unlit parts only: the
        // normals stay behind).
        static void Settle(ShapeBuilder b, int from, float angleDeg, float y, float lift, float roll = 0f)
        {
            Vector3 pos = Surface(angleDeg, y, lift, out var n);
            var rot = Quaternion.LookRotation(n, Vector3.up) * Quaternion.Euler(0f, 0f, roll);
            for (int i = from; i < b.VertexCount; i++) b.SetVertex(i, pos + rot * b.VertexAt(i));
        }

        // Rosy freckled cheeks, crow's feet and the little creases of someone who has laughed for centuries.
        static Mesh BuildFaceDetails()
        {
            var b = new ShapeBuilder();
            Color freckle = new Color(0.5f, 0.31f, 0.25f);
            for (int side = -1; side <= 1; side += 2)
            {
                int at = b.VertexCount;
                Disc(b, Vector2.zero, 0.135f, 0.088f, 0f, 12, new Color(1f, 0.55f, 0.53f));
                Disc(b, new Vector2(0.01f, 0.006f), 0.085f, 0.05f, 0.003f, 10, new Color(1f, 0.63f, 0.6f));
                Settle(b, at, side * 40f, 0.83f, FaceLift);

                at = b.VertexCount;
                Disc(b, new Vector2(-0.05f, 0.0f), 0.02f, 0.02f, 0f, 6, freckle);
                Disc(b, new Vector2(0.015f, 0.035f), 0.017f, 0.017f, 0f, 6, freckle);
                Disc(b, new Vector2(0.065f, -0.008f), 0.019f, 0.019f, 0f, 6, freckle);
                Settle(b, at, side * 33f, 0.935f, FaceLift + 0.004f, side * 8f);

                // Local +X is the viewer's left, so the outer corner of the viewer's-left eye lies towards +X.
                at = b.VertexCount;
                float o = -side;
                Stroke(b, new Vector2(0f, 0.03f), new Vector2(o * 0.06f, 0.065f), 0.011f, Wrinkle);
                Stroke(b, new Vector2(0.006f * o, -0.005f), new Vector2(o * 0.07f, -0.005f), 0.011f, Wrinkle);
                Stroke(b, new Vector2(0f, -0.04f), new Vector2(o * 0.058f, -0.075f), 0.01f, Wrinkle);
                Settle(b, at, side * (EyeAngle + 17.5f), EyeY - 0.005f, FaceLift);
            }
            return b.ToMesh("TildaFace");
        }

        const float MouthHalf = 0.175f, MouthDepth = 0.07f;

        static Mesh BuildSmile()
        {
            var b = new ShapeBuilder();
            Arc(b, MouthHalf, MouthDepth, -1f, 0.021f, -10f, 190f, 12, 0.004f, new Color(0.22f, 0.09f, 0.09f));
            return b.ToMesh("TildaSmile");
        }

        // Dimples at the corners of the mouth: they widen with it but do not stretch when it opens.
        static Mesh BuildDimples()
        {
            var b = new ShapeBuilder();
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 c = new Vector2(side * (MouthHalf + 0.03f), 0.012f);
                for (int k = 0; k < 4; k++)
                {
                    float a0 = (-55f + 110f * k / 4f) * Mathf.Deg2Rad, a1 = (-55f + 110f * (k + 1) / 4f) * Mathf.Deg2Rad;
                    Vector2 p0 = c + new Vector2(side * Mathf.Cos(a0), Mathf.Sin(a0)) * 0.05f, p1 = c + new Vector2(side * Mathf.Cos(a1), Mathf.Sin(a1)) * 0.05f;
                    Vector2 q0 = c + new Vector2(side * Mathf.Cos(a0), Mathf.Sin(a0)) * 0.068f, q1 = c + new Vector2(side * Mathf.Cos(a1), Mathf.Sin(a1)) * 0.068f;
                    b.Fin(new Vector3(p0.x, p0.y, 0.004f), new Vector3(q0.x, q0.y, 0.004f), new Vector3(q1.x, q1.y, 0.004f), Vector3.forward, Crease);
                    b.Fin(new Vector3(p0.x, p0.y, 0.004f), new Vector3(q1.x, q1.y, 0.004f), new Vector3(p1.x, p1.y, 0.004f), Vector3.forward, Crease);
                }
            }
            return b.ToMesh("TildaDimples");
        }

        // Inside her mouth it glows like the crater, and she has exactly one tooth left.
        static Mesh BuildMouthFill()
        {
            var b = new ShapeBuilder();
            const int seg = 12;
            Color inside = new Color(0.36f, 0.07f, 0.09f);
            for (int j = 0; j < seg; j++)
            {
                float t0 = j * Mathf.PI / seg, t1 = (j + 1) * Mathf.PI / seg;
                b.Fin(Vector3.zero, new Vector3(Mathf.Cos(t0) * MouthHalf, -Mathf.Sin(t0) * MouthDepth, 0f), new Vector3(Mathf.Cos(t1) * MouthHalf, -Mathf.Sin(t1) * MouthDepth, 0f), Vector3.forward, inside);
            }
            Disc(b, new Vector2(0f, -MouthDepth * 0.72f), 0.105f, MouthDepth * 0.27f, 0.003f, 10, LavaWarm);
            Disc(b, new Vector2(0f, -MouthDepth * 0.78f), 0.06f, MouthDepth * 0.16f, 0.005f, 8, LavaHot);
            Color tooth = new Color(1f, 0.98f, 0.9f);
            Vector3 a = new Vector3(0.018f, 0f, 0.006f), c = new Vector3(0.078f, 0f, 0.006f);
            Vector3 d = new Vector3(0.074f, -MouthDepth * 0.4f, 0.006f), e = new Vector3(0.024f, -MouthDepth * 0.42f, 0.006f);
            b.Fin(a, c, d, Vector3.forward, tooth);
            b.Fin(a, d, e, Vector3.forward, tooth);
            return b.ToMesh("TildaMouthOpen");
        }

        // ---------------------------------------------------------------- arms, smoke, extras

        static Mesh BuildArm(float side)
        {
            var b = new ShapeBuilder();
            Color rock = new Color(0.72f, 0.53f, 0.4f);
            b.Sphere(new Vector3(0.25f * side, 0f, 0f), new Vector3(0.31f, 0.15f, 0.15f), rock, 1);
            b.Sphere(new Vector3(0.56f * side, 0f, 0f), new Vector3(0.19f, 0.19f, 0.18f), rock * 1.07f, 1);
            b.Sphere(new Vector3(0.53f * side, 0.18f, -0.03f), new Vector3(0.075f, 0.09f, 0.075f), rock * 1.07f, 0);
            return Lit(b, "TildaArm", 0.6f, 1.08f);
        }

        static Mesh BuildPuff(int i)
        {
            var b = new ShapeBuilder();
            Color c = Color.Lerp(new Color(1f, 0.99f, 0.96f), new Color(0.86f, 0.87f, 0.9f), Hash(50, i));
            b.Sphere(Vector3.zero, new Vector3(1f, 0.9f, 0.9f), c, 1);
            b.Sphere(new Vector3(0.75f, -0.2f, 0.1f), Vector3.one * 0.62f, c * 0.97f, 0);
            b.Sphere(new Vector3(-0.7f, -0.25f, -0.1f), Vector3.one * 0.55f, c * 0.97f, 0);
            return Lit(b, "TildaPuff", 0.72f, 1.04f);
        }

        static Mesh BuildZ()
        {
            var b = new ShapeBuilder();
            Color c = new Color(0.66f, 0.88f, 0.98f);
            Vector3 f = Vector3.back;
            void Quad(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
            {
                b.Fin(p0, p1, p2, f, c);
                b.Fin(p0, p2, p3, f, c);
            }
            Quad(new Vector2(-0.4f, 0.5f), new Vector2(0.4f, 0.5f), new Vector2(0.4f, 0.3f), new Vector2(-0.4f, 0.3f));
            Quad(new Vector2(0.4f, 0.3f), new Vector2(0.06f, 0.3f), new Vector2(-0.4f, -0.3f), new Vector2(-0.06f, -0.3f));
            Quad(new Vector2(-0.4f, -0.3f), new Vector2(0.4f, -0.3f), new Vector2(0.4f, -0.5f), new Vector2(-0.4f, -0.5f));
            return b.ToMesh("TildaZ");
        }

        static Mesh BuildSpark(int i)
        {
            var b = new ShapeBuilder();
            b.Sphere(Vector3.zero, Vector3.one, i % 2 == 0 ? LavaHot : LavaWarm, 0);
            return b.ToMesh("TildaSpark");
        }
    }
}
