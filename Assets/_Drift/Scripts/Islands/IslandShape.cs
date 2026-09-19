using System.Collections.Generic;
using UnityEngine;

namespace Drift.Islands
{
    public class IslandShape
    {
        public const float Sea = -0.8f;

        public float cell;
        public int nx, nz;
        public Vector2 origin;
        public float[] h;
        public float[] uplift;
        public float upliftWeight = 1f;

        Vector3[] _verts;
        readonly List<int> _tris = new();

        public IslandShape(float cell, int nx, int nz, Vector2 origin)
        {
            this.cell = cell;
            this.nx = nx;
            this.nz = nz;
            this.origin = origin;
            h = new float[nx * nz];
            for (int i = 0; i < h.Length; i++) h[i] = Sea;
        }

        public Rect Bounds => new Rect(origin, new Vector2((nx - 1) * cell, (nz - 1) * cell));

        public Vector2 CellPos(int i, int j) => new Vector2(origin.x + i * cell, origin.y + j * cell);

        public float Height(int i, int j)
        {
            int k = j * nx + i;
            float v = h[k];
            if (uplift != null) v += uplift[k] * upliftWeight;
            return v;
        }

        public float Sample(Vector2 p)
        {
            float fx = (p.x - origin.x) / cell;
            float fz = (p.y - origin.y) / cell;
            if (fx < 0f || fz < 0f || fx > nx - 1 || fz > nz - 1) return Sea;
            int i = Mathf.Min((int)fx, nx - 2);
            int j = Mathf.Min((int)fz, nz - 2);
            float tx = fx - i;
            float tz = fz - j;
            float a = Height(i, j), b = Height(i + 1, j), c = Height(i, j + 1), d = Height(i + 1, j + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        public void Bake()
        {
            if (uplift == null) return;
            for (int k = 0; k < h.Length; k++) h[k] += uplift[k] * upliftWeight;
            uplift = null;
            upliftWeight = 1f;
        }

        public Rect LandBounds(float aboveSea)
        {
            float t = Sea + aboveSea;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            bool any = false;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    if (h[j * nx + i] <= t) continue;
                    any = true;
                    Vector2 p = CellPos(i, j);
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
            return any ? Rect.MinMaxRect(min.x, min.y, max.x, max.y) : new Rect(origin, Vector2.zero);
        }

        public Vector2 LandCentroid()
        {
            Vector2 sum = Vector2.zero;
            int n = 0;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    if (h[j * nx + i] <= 0f) continue;
                    sum += CellPos(i, j);
                    n++;
                }
            return n > 0 ? sum / n : Vector2.zero;
        }

        public static IslandShape CreateBlob(float radius, int seed, float cell)
        {
            float ext = radius * 1.7f + 1f;
            int n = Mathf.CeilToInt(2f * ext / cell) + 1;
            Vector2 origin = new Vector2(-(n - 1) * cell * 0.5f, -(n - 1) * cell * 0.5f);
            var s = new IslandShape(cell, n, n, origin);

            var rnd = new System.Random(seed);
            float p1 = (float)rnd.NextDouble() * 6.28f, p2 = (float)rnd.NextDouble() * 6.28f, p3 = (float)rnd.NextDouble() * 6.28f;
            Vector2 off = new Vector2((float)rnd.NextDouble() * 100f, (float)rnd.NextDouble() * 100f);

            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = s.CellPos(i, j);
                    float ang = Mathf.Atan2(p.y, p.x);
                    float rr = radius * (1f + 0.2f * (0.5f * Mathf.Sin(3f * ang + p1) + 0.3f * Mathf.Sin(5f * ang + p2) + 0.2f * Mathf.Sin(8f * ang + p3)));
                    float d = p.magnitude / Mathf.Max(0.01f, rr);
                    float baseH = 0.5f + 0.35f * Mathf.PerlinNoise(off.x + p.x * 0.35f, off.y + p.y * 0.35f)
                                  + 0.15f * Mathf.PerlinNoise(off.x + p.x * 1.3f, off.y + p.y * 1.3f);
                    float t = Smooth(0.7f, 1.15f, d);
                    s.h[j * n + i] = Mathf.Lerp(baseH, Sea, t);
                }
            return s;
        }

        public static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        public void FillMesh(Mesh m)
        {
            int count = nx * nz;
            if (_verts == null || _verts.Length != count) _verts = new Vector3[count];
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                    _verts[j * nx + i] = new Vector3(origin.x + i * cell, Mathf.Max(Height(i, j), Sea), origin.y + j * cell);

            _tris.Clear();
            for (int j = 0; j < nz - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    float hMax = Mathf.Max(Mathf.Max(Height(i, j), Height(i + 1, j)), Mathf.Max(Height(i, j + 1), Height(i + 1, j + 1)));
                    if (hMax < -0.55f) continue;
                    int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                    _tris.Add(a); _tris.Add(c); _tris.Add(b);
                    _tris.Add(b); _tris.Add(c); _tris.Add(d);
                }

            m.Clear();
            if (count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(_verts);
            m.SetTriangles(_tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
        }
    }
}
