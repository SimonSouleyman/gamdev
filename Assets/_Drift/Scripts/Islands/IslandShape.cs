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
        public float sink;

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

        public float HeightRaw(int i, int j)
        {
            int k = j * nx + i;
            float v = h[k];
            if (uplift != null) v += uplift[k] * upliftWeight;
            return v;
        }

        public float Height(int i, int j) => HeightRaw(i, j) - sink;

        public float SampleRaw(Vector2 p) => SampleInternal(p, 0f);

        public float Sample(Vector2 p) => SampleInternal(p, sink);

        float SampleInternal(Vector2 p, float sinkOffset)
        {
            float fx = (p.x - origin.x) / cell;
            float fz = (p.y - origin.y) / cell;
            if (fx < 0f || fz < 0f || fx > nx - 1 || fz > nz - 1) return Sea;
            int i = Mathf.Min((int)fx, nx - 2);
            int j = Mathf.Min((int)fz, nz - 2);
            float tx = fx - i;
            float tz = fz - j;
            float a = HeightRaw(i, j), b = HeightRaw(i + 1, j), c = HeightRaw(i, j + 1), d = HeightRaw(i + 1, j + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz) - sinkOffset;
        }

        // Folds the sink offset into the heights so the displayed surface stays put while sink becomes 0.
        public void BakeSink()
        {
            if (Mathf.Abs(sink) < 1e-6f) return;
            for (int k = 0; k < h.Length; k++) h[k] -= sink;
            sink = 0f;
        }

        public float MaxHeightRaw()
        {
            float m = 0f;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                    m = Mathf.Max(m, HeightRaw(i, j));
            return m;
        }

        public void Bake()
        {
            if (uplift == null) return;
            for (int k = 0; k < h.Length; k++) h[k] += uplift[k] * upliftWeight;
            uplift = null;
            upliftWeight = 1f;
        }

        // Two thresholds, one definition each: land is anything above the waterline (LandCentroid, LandAxes,
        // Compactness, LandBounds()); the shelf is everything that is not flat sea floor, i.e. the beach slope
        // under water too, which is what a merge grid has to cover.
        public const float ShelfAboveSea = 0.15f;
        public static bool IsLand(float height) => height > 0f;
        public static bool IsShelf(float height) => height > Sea + ShelfAboveSea;

        public Rect LandBounds() => LandBounds(-Sea);

        public Rect ShelfBounds() => LandBounds(ShelfAboveSea);

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

        // Principal axes of the land above water: minor = the thin direction, elongation 0 (disc) .. 1 (line).
        public void LandAxes(out Vector2 minor, out float elongation)
        {
            double sx = 0, sz = 0; int n = 0;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    if (Height(i, j) <= 0f) continue;
                    Vector2 p = CellPos(i, j);
                    sx += p.x; sz += p.y; n++;
                }
            minor = Vector2.up; elongation = 0f;
            if (n < 4) return;
            double mx = sx / n, mz = sz / n, cxx = 0, czz = 0, cxz = 0;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    if (Height(i, j) <= 0f) continue;
                    Vector2 p = CellPos(i, j);
                    double dx = p.x - mx, dz = p.y - mz;
                    cxx += dx * dx; czz += dz * dz; cxz += dx * dz;
                }
            double tr = cxx + czz, det = cxx * czz - cxz * cxz;
            double disc = System.Math.Sqrt(System.Math.Max(0, tr * tr * 0.25 - det));
            double lMax = tr * 0.5 + disc, lMin = tr * 0.5 - disc;
            if (lMax <= 1e-9) return;
            elongation = (float)(1.0 - System.Math.Max(0, lMin) / lMax);
            double theta = 0.5 * System.Math.Atan2(2.0 * cxz, cxx - czz);
            Vector2 major = new Vector2((float)System.Math.Cos(theta), (float)System.Math.Sin(theta));
            minor = new Vector2(-major.y, major.x);
        }

        // Radial profile of the land above water: reach[k] = distance from the local origin to the last
        // land along the ray at k * 360/len degrees (yaw convention: 0 = +y/forward, 90 = +x/right).
        // Unlike a support function it sees the notches between merged bodies.
        public void LandReach(float[] reach)
        {
            int n = reach.Length;
            float step = cell * 0.5f;
            float maxR = new Vector2(Mathf.Max(Mathf.Abs(origin.x), Mathf.Abs(origin.x + (nx - 1) * cell)),
                Mathf.Max(Mathf.Abs(origin.y), Mathf.Abs(origin.y + (nz - 1) * cell))).magnitude;
            for (int k = 0; k < n; k++)
            {
                float a = k * 2f * Mathf.PI / n;
                Vector2 dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                float last = 0f;
                for (float r = 0f; r <= maxR; r += step)
                    if (Sample(dir * r) > 0f) last = r;
                reach[k] = last;
            }
        }

        // 1 for a disc, towards 0 for a thin line; perimeter counted from land cells with a sea neighbour.
        // Calibrated on 2026-09-20: fresh blob raw 1.17 → 0.88, two islands in a line 0.61 → 0.46.
        const float compactnessScale = 0.75f;

        public float Compactness()
        {
            int area = 0, edge = 0;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    if (Height(i, j) <= 0f) continue;
                    area++;
                    bool border = i == 0 || j == 0 || i == nx - 1 || j == nz - 1
                        || Height(i - 1, j) <= 0f || Height(i + 1, j) <= 0f || Height(i, j - 1) <= 0f || Height(i, j + 1) <= 0f;
                    if (border) edge++;
                }
            if (edge == 0) return 1f;
            // Rasterised discs count more edge cells than 2πR; the factor puts a fresh blob near 0.9.
            return 4f * Mathf.PI * area * compactnessScale / ((float)edge * edge);
        }

        public Vector2 LandCentroid()
        {
            Vector2 sum = Vector2.zero;
            int n = 0;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    if (!IsLand(h[j * nx + i])) continue;
                    sum += CellPos(i, j);
                    n++;
                }
            return n > 0 ? sum / n : Vector2.zero;
        }

        public static IslandShape CreateBlob(float radius, int seed, float cell, float heightScale = 1f)
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
                    s.h[j * n + i] = Mathf.Lerp(baseH * heightScale, Sea, t);
                }
            return s;
        }

        public static IslandShape CreateVolcano(float radius, int seed, float cell)
        {
            float ext = radius * 1.7f + 1f;
            int n = Mathf.CeilToInt(2f * ext / cell) + 1;
            Vector2 origin = new Vector2(-(n - 1) * cell * 0.5f, -(n - 1) * cell * 0.5f);
            var s = new IslandShape(cell, n, n, origin);

            var rnd = new System.Random(seed);
            float p1 = (float)rnd.NextDouble() * 6.28f, p2 = (float)rnd.NextDouble() * 6.28f, p3 = (float)rnd.NextDouble() * 6.28f;
            Vector2 off = new Vector2((float)rnd.NextDouble() * 100f, (float)rnd.NextDouble() * 100f);

            float peak = 0.95f * radius + 1.6f;
            const float craterT = 0.17f;
            float craterDepth = peak * 0.3f;

            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = s.CellPos(i, j);
                    float ang = Mathf.Atan2(p.y, p.x);
                    float rr = radius * (1f + 0.16f * (0.5f * Mathf.Sin(3f * ang + p1) + 0.3f * Mathf.Sin(5f * ang + p2) + 0.2f * Mathf.Sin(7f * ang + p3)));
                    float t = p.magnitude / Mathf.Max(0.01f, rr);

                    float cone = peak * Mathf.Pow(Mathf.Clamp01(1f - t), 1.15f);
                    if (t < craterT)
                    {
                        float c = t / craterT;
                        cone -= craterDepth * (1f - c * c);
                    }
                    cone += 0.35f * (Mathf.PerlinNoise(off.x + p.x * 0.9f, off.y + p.y * 0.9f) - 0.5f) * Mathf.Clamp01(1f - t);
                    s.h[j * n + i] = Mathf.Lerp(cone, Sea, Smooth(0.9f, 1.15f, t));
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

    // Hypsometric curve of an island's raw land (cells with HeightRaw > 0): which share of it is still above
    // water at a given sink depth, and the inverse. Piecewise linear over Bins height classes, so the two
    // directions are exact inverses of each other; rebuilt only when the raw heights change, never while sinking.
    public sealed class IslandHypsometry
    {
        public const int Bins = 256;

        readonly float[] _above = new float[Bins + 1];

        public float Top { get; private set; }
        public int LandCells { get; private set; }

        public void Build(IslandShape s)
        {
            float top = 0f;
            int land = 0;
            for (int j = 0; j < s.nz; j++)
                for (int i = 0; i < s.nx; i++)
                {
                    float v = s.HeightRaw(i, j);
                    if (v <= 0f) continue;
                    land++;
                    if (v > top) top = v;
                }
            System.Array.Clear(_above, 0, _above.Length);
            Top = top;
            LandCells = land;
            if (land == 0) return;

            float scale = Bins / top;
            for (int j = 0; j < s.nz; j++)
                for (int i = 0; i < s.nx; i++)
                {
                    float v = s.HeightRaw(i, j);
                    if (v <= 0f) continue;
                    _above[Mathf.Min(Bins - 1, (int)(v * scale))] += 1f;
                }
            float run = 0f;
            for (int b = Bins - 1; b >= 0; b--)
            {
                run += _above[b];
                _above[b] = run;
            }
        }

        public float LandFractionAt(float depth)
        {
            if (LandCells == 0 || depth >= Top) return 0f;
            if (depth <= 0f) return 1f;
            float x = depth / Top * Bins;
            int b = Mathf.Min((int)x, Bins - 1);
            return Mathf.Lerp(_above[b], _above[b + 1], x - b) / LandCells;
        }

        public float DepthAt(float landFraction)
        {
            if (LandCells == 0) return 0f;
            float target = Mathf.Clamp01(landFraction) * LandCells;
            if (target <= 0f) return Top;
            if (target >= LandCells) return 0f;
            int lo = 0, hi = Bins;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (_above[mid] >= target) lo = mid;
                else hi = mid;
            }
            float a = _above[lo], c = _above[hi];
            float t = a > c ? (a - target) / (a - c) : 0f;
            return (lo + t) * Top / Bins;
        }
    }
}
