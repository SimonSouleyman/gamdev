using UnityEngine;

namespace Drift.Life
{
    public enum LifeKind { Grass, Flower, Bush, Tree, Grazer, Bird }

    public class PlantTemplate
    {
        public Vector3[] vertices;
        public Vector3[] normals;
        public Color[] colors;
        public int[] triangles;
        public float[] sway;
    }

    public static class LifeMeshes
    {
        public const int Variants = 3;

        static readonly Mesh[,] MeshCache = new Mesh[6, Variants];
        static readonly PlantTemplate[,] TemplateCache = new PlantTemplate[6, Variants];
        static Material _material;

        public static Material Material
        {
            get
            {
                if (_material == null)
                {
                    var shader = Shader.Find("Drift/VertexColor");
                    if (shader == null) Debug.LogError("LifeMeshes: shader 'Drift/VertexColor' not found.");
                    _material = new Material(shader) { name = "LifeVertexColor", hideFlags = HideFlags.HideAndDontSave };
                    _material.SetFloat("_LightAmount", 1f);
                    _material.SetFloat("_Sway", 0.07f);
                    _material.SetFloat("_SwaySpeed", 2.2f);
                }
                return _material;
            }
        }

        public static Mesh Get(LifeKind kind, int variant)
        {
            variant = Mathf.Abs(variant) % Variants;
            ref Mesh slot = ref MeshCache[(int)kind, variant];
            if (slot == null) slot = Build(kind, variant);
            return slot;
        }

        public static PlantTemplate GetTemplate(LifeKind kind, int variant)
        {
            variant = Mathf.Abs(variant) % Variants;
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
                for (int i = 0; i < t.sway.Length; i++) t.sway[i] = Mathf.Clamp01(t.vertices[i].y / maxY);
                slot = t;
            }
            return slot;
        }

        static Color Hsv(float h, float s, float v) => Color.HSVToRGB(Mathf.Repeat(h, 1f), s, v);

        static Mesh Build(LifeKind kind, int v)
        {
            var b = new ShapeBuilder();
            switch (kind)
            {
                case LifeKind.Grass:
                {
                    Color g = Hsv(0.22f + 0.04f * v, 0.55f, 0.72f);
                    b.Cone(new Vector3(0f, 0f, 0f), new Vector3(0.05f, 1f, 0f), 0.05f, 0.24f, 3, g);
                    b.Cone(new Vector3(0.08f, 0f, 0.05f), new Vector3(-0.08f, 1f, 0.05f), 0.045f, 0.19f, 3, g * 0.92f);
                    b.Cone(new Vector3(-0.06f, 0f, 0.07f), new Vector3(0.02f, 1f, -0.1f), 0.045f, 0.21f, 3, g * 1.08f);
                    break;
                }
                case LifeKind.Flower:
                {
                    Color[] heads = { new Color(1f, 0.55f, 0.75f), new Color(1f, 0.85f, 0.25f), new Color(0.95f, 0.95f, 1f) };
                    b.Cone(new Vector3(0f, 0f, 0f), Vector3.up, 0.025f, 0.24f, 3, new Color(0.25f, 0.55f, 0.2f));
                    b.Sphere(new Vector3(0f, 0.27f, 0f), new Vector3(0.09f, 0.06f, 0.09f), heads[v], 0);
                    break;
                }
                case LifeKind.Bush:
                {
                    Color g = Hsv(0.27f + 0.03f * v, 0.62f, 0.5f + 0.08f * v);
                    b.Sphere(new Vector3(0f, 0.2f, 0f), new Vector3(0.3f, 0.22f, 0.3f), g, 0);
                    b.Sphere(new Vector3(0.2f, 0.15f, 0.1f), new Vector3(0.2f, 0.16f, 0.2f), g * 1.15f, 0);
                    break;
                }
                case LifeKind.Tree:
                {
                    Color trunk = new Color(0.4f, 0.27f, 0.15f);
                    b.Box(new Vector3(0f, 0.16f, 0f), new Vector3(0.1f, 0.32f, 0.1f), trunk);
                    if (v == 0)
                    {
                        Color g = Hsv(0.33f, 0.6f, 0.38f);
                        b.Cone(new Vector3(0f, 0.25f, 0f), Vector3.up, 0.38f, 0.55f, 6, g);
                        b.Cone(new Vector3(0f, 0.55f, 0f), Vector3.up, 0.28f, 0.5f, 6, g * 1.15f);
                    }
                    else if (v == 1)
                    {
                        Color g = Hsv(0.25f, 0.6f, 0.52f);
                        b.Sphere(new Vector3(0f, 0.62f, 0f), new Vector3(0.38f, 0.36f, 0.38f), g, 0);
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
                case LifeKind.Grazer:
                {
                    Color[] bodies = { new Color(0.95f, 0.92f, 0.85f), new Color(0.6f, 0.42f, 0.28f), new Color(0.82f, 0.78f, 0.7f) };
                    Color body = bodies[v];
                    b.Box(new Vector3(0f, 0.2f, 0f), new Vector3(0.26f, 0.2f, 0.42f), body);
                    b.Box(new Vector3(0f, 0.3f, 0.26f), new Vector3(0.14f, 0.14f, 0.16f), body * 0.92f);
                    break;
                }
                default:
                {
                    Color[] bodies = { new Color(0.3f, 0.55f, 0.95f), new Color(0.95f, 0.35f, 0.3f), new Color(1f, 0.8f, 0.25f) };
                    Color body = bodies[v];
                    b.Sphere(Vector3.zero, new Vector3(0.11f, 0.09f, 0.17f), body, 0);
                    b.Box(new Vector3(0f, 0.02f, 0f), new Vector3(0.6f, 0.02f, 0.12f), body * 0.85f);
                    break;
                }
            }
            return b.ToMesh(kind + "_" + v);
        }
    }
}
