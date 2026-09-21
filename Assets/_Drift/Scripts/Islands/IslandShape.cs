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
        float[] _hNow;
        readonly List<int> _tris = new();
        Mesh _filled;

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

        public float HeightRaw(int i, int j) => HeightAt(i, j, upliftWeight);

        float HeightAt(int i, int j, float weight)
        {
            int k = j * nx + i;
            float v = h[k];
            if (uplift != null) v += uplift[k] * weight;
            return v;
        }

        public float Height(int i, int j) => HeightRaw(i, j) - sink;

        public float SampleRaw(Vector2 p) => SampleInternal(p, 0f, upliftWeight);

        // Where a rising uplift is heading: the raw height once it has fully risen.
        public float SampleGoal(Vector2 p) => SampleInternal(p, 0f, 1f);

        public float Sample(Vector2 p) => SampleInternal(p, sink, upliftWeight);

        // The plane of the triangle the point falls in, matching FillMesh's a-c-b / b-c-d split exactly.
        // Bilinear interpolation would be smoother but sits up to (b+c-a-d)/4 away from the surface that is
        // actually drawn, and everything that stands on the ground (animals, settlers, plants, the camera
        // clamp) reads its height from here: on a twisted quad that buried half an animal in the terrain.
        float SampleInternal(Vector2 p, float sinkOffset, float weight)
        {
            float fx = (p.x - origin.x) / cell;
            float fz = (p.y - origin.y) / cell;
            if (fx < 0f || fz < 0f || fx > nx - 1 || fz > nz - 1) return Sea;
            int i = Mathf.Min((int)fx, nx - 2);
            int j = Mathf.Min((int)fz, nz - 2);
            float tx = fx - i;
            float tz = fz - j;
            float a = HeightAt(i, j, weight), b = HeightAt(i + 1, j, weight), c = HeightAt(i, j + 1, weight), d = HeightAt(i + 1, j + 1, weight);
            float h = tx + tz <= 1f
                ? a + (b - a) * tx + (c - a) * tz
                : d + (c - d) * (1f - tx) + (b - d) * (1f - tz);
            return h - sinkOffset;
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

        // ---- slope limit ----

        // Height of the band above the waterline that the slope limit and the merge uplift never touch.
        public const float BeachBand = 0.3f;

        // tan of the steepest allowed hillside between two neighbouring grid points (axis or diagonal), judged
        // at the lower point: tan up to PeakFrom above the water, blending to peakTan by PeakTo (volcano tops).
        public struct SlopeRule
        {
            public const float PeakFrom = 2.6f;
            public const float PeakTo = 3.4f;

            public float tan, peakTan, water;

            public float Floor => water + BeachBand;

            public float Talus(float lower) => peakTan <= tan ? tan : tan + (peakTan - tan) * Smooth(PeakFrom, PeakTo, lower - water);

            public static SlopeRule Degrees(float degrees, float peakDegrees, float water) => new SlopeRule
            {
                tan = Mathf.Tan(Mathf.Clamp(degrees, 1f, 85f) * Mathf.Deg2Rad),
                peakTan = Mathf.Tan(Mathf.Clamp(Mathf.Max(degrees, peakDegrees), 1f, 85f) * Mathf.Deg2Rad),
                water = water,
            };
        }

        static readonly int[] NDi = { -1, 0, 1, -1, 1, -1, 0, 1 };
        static readonly int[] NDj = { -1, -1, -1, 0, 0, 1, 1, 1 };

        public float LimitSlopes(SlopeRule rule, int erosionSteps) => LimitSlopes(h, rule, erosionSteps);

        // Makes f (a field on this grid) obey the rule. Heights at or below rule.Floor (sea, shelf and the beach
        // band) are never changed and count as Floor, so the coastline keeps its exact shape and no land appears.
        // Above it, erosionSteps of thermal erosion let too steep material slide to lower neighbours (volume kept,
        // hills broaden instead of being cut off), then a clamp lowers whatever is still too steep. Only the cells
        // above the floor are visited, so a big island costs its highland, not its grid. Returns the volume the
        // clamp removed (height units times cells).
        public float LimitSlopes(float[] f, SlopeRule rule, int erosionSteps)
        {
            float floor = rule.Floor;
            int count = 0;
            for (int j = 1; j < nz - 1; j++)
                for (int i = 1; i < nx - 1; i++)
                    if (f[j * nx + i] > floor) count++;
            if (count == 0) return 0f;

            var active = new int[count];
            var isActive = new bool[f.Length];
            count = 0;
            for (int j = 1; j < nz - 1; j++)
                for (int i = 1; i < nx - 1; i++)
                {
                    int k = j * nx + i;
                    if (f[k] <= floor) continue;
                    active[count++] = k;
                    isActive[k] = true;
                }

            var off = new int[8];
            var dist = new float[8];
            for (int d = 0; d < 8; d++)
            {
                off[d] = NDj[d] * nx + NDi[d];
                dist[d] = (NDi[d] != 0 && NDj[d] != 0 ? 1.41421356f : 1f) * cell;
            }

            if (erosionSteps > 0)
            {
                var delta = new float[f.Length];
                var ex = new float[8];
                for (int step = 0; step < erosionSteps; step++)
                {
                    bool moved = false;
                    for (int a = 0; a < count; a++)
                    {
                        int k = active[a];
                        float hk = f[k], sum = 0f, most = 0f;
                        for (int d = 0; d < 8; d++)
                        {
                            ex[d] = 0f;
                            int n = k + off[d];
                            if (!isActive[n]) continue;
                            float hn = f[n];
                            if (hn >= hk) continue;
                            float e = hk - hn - dist[d] * rule.Talus(hn);
                            if (e <= 0f) continue;
                            ex[d] = e;
                            sum += e;
                            if (e > most) most = e;
                        }
                        if (sum <= 1e-5f) continue;
                        // Half the worst excess settles a single steep neighbour in one step; a little less keeps
                        // the simultaneous (Jacobi) updates of several donors from overshooting.
                        float move = 0.4f * most;
                        delta[k] -= move;
                        for (int d = 0; d < 8; d++)
                            if (ex[d] > 0f) delta[k + off[d]] += move * ex[d] / sum;
                        moved = true;
                    }
                    for (int a = 0; a < count; a++)
                    {
                        int k = active[a];
                        f[k] += delta[k];
                        delta[k] = 0f;
                    }
                    if (!moved) break;
                }
            }

            // Gauss-Seidel sweeps in raster order and back; each only lowers, so it converges from above.
            float removed = 0f;
            for (int pass = 0; pass < 24; pass++)
            {
                bool changed = false;
                bool forward = (pass & 1) == 0;
                for (int a = 0; a < count; a++)
                {
                    int k = active[forward ? a : count - 1 - a];
                    float hk = f[k], bound = hk;
                    for (int d = 0; d < 8; d++)
                    {
                        float g = f[k + off[d]];
                        if (g < floor) g = floor;
                        float lim = g + dist[d] * rule.Talus(g);
                        if (lim < bound) bound = lim;
                    }
                    if (bound < hk - 1e-6f)
                    {
                        f[k] = bound;
                        removed += hk - bound;
                        changed = true;
                    }
                }
                if (!changed) break;
            }
            return removed;
        }

        // Steepest slope (as tan) between neighbouring grid points of f, heights at or below rule.Floor counted
        // as Floor, and by how much it exceeds the rule at worst (<= 0 when the field obeys it).
        public float SteepestSlope(float[] f, SlopeRule rule, out float worstExcess)
        {
            float floor = rule.Floor, steepest = 0f;
            worstExcess = float.MinValue;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i;
                    for (int d = 4; d < 8; d++)
                    {
                        int i2 = i + NDi[d], j2 = j + NDj[d];
                        if (i2 < 0 || i2 >= nx || j2 >= nz) continue;
                        float p = Mathf.Max(f[k], floor), q = Mathf.Max(f[j2 * nx + i2], floor);
                        float hi = Mathf.Max(p, q), lo = Mathf.Min(p, q);
                        if (hi <= floor) continue;
                        float len = (NDi[d] != 0 && NDj[d] != 0 ? 1.41421356f : 1f) * cell;
                        float s = (hi - lo) / len;
                        if (s > steepest) steepest = s;
                        float e = s - rule.Talus(lo);
                        if (e > worstExcess) worstExcess = e;
                    }
                }
            if (worstExcess == float.MinValue) worstExcess = 0f;
            return steepest;
        }

        // The raw heights with the uplift at the given weight (1 = fully risen), as a fresh array.
        public float[] RawHeights(float weight)
        {
            var f = new float[h.Length];
            for (int k = 0; k < h.Length; k++) f[k] = uplift != null ? h[k] + uplift[k] * weight : h[k];
            return f;
        }

        // Separable box blur of a field on this grid, `passes` times (two approximate a Gaussian); edges clamp.
        public void BoxBlur(float[] f, int radius, int passes)
        {
            if (radius <= 0 || passes <= 0) return;
            var line = new float[Mathf.Max(nx, nz)];
            float inv = 1f / (2 * radius + 1);
            for (int pass = 0; pass < passes; pass++)
            {
                for (int j = 0; j < nz; j++)
                {
                    int row = j * nx;
                    for (int i = 0; i < nx; i++) line[i] = f[row + i];
                    float sum = 0f;
                    for (int o = -radius; o <= radius; o++) sum += line[Mathf.Clamp(o, 0, nx - 1)];
                    for (int i = 0; i < nx; i++)
                    {
                        f[row + i] = sum * inv;
                        sum += line[Mathf.Min(i + radius + 1, nx - 1)] - line[Mathf.Max(i - radius, 0)];
                    }
                }
                for (int i = 0; i < nx; i++)
                {
                    for (int j = 0; j < nz; j++) line[j] = f[j * nx + i];
                    float sum = 0f;
                    for (int o = -radius; o <= radius; o++) sum += line[Mathf.Clamp(o, 0, nz - 1)];
                    for (int j = 0; j < nz; j++)
                    {
                        f[j * nx + i] = sum * inv;
                        sum += line[Mathf.Min(j + radius + 1, nz - 1)] - line[Mathf.Max(j - radius, 0)];
                    }
                }
            }
        }

        // Chamfer distance (axis 1, diagonal sqrt 2, in world units) from every cell to the nearest cell at or
        // below `level`; 0 on those cells.
        public float[] DistanceToBelow(float[] f, float level)
        {
            var dist = new float[f.Length];
            const float Big = 1e6f;
            float ax = cell, dg = cell * 1.41421356f;
            for (int k = 0; k < f.Length; k++) dist[k] = f[k] <= level ? 0f : Big;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i;
                    float v = dist[k];
                    if (v == 0f) continue;
                    if (i > 0) v = Mathf.Min(v, dist[k - 1] + ax);
                    if (j > 0)
                    {
                        v = Mathf.Min(v, dist[k - nx] + ax);
                        if (i > 0) v = Mathf.Min(v, dist[k - nx - 1] + dg);
                        if (i < nx - 1) v = Mathf.Min(v, dist[k - nx + 1] + dg);
                    }
                    dist[k] = v;
                }
            for (int j = nz - 1; j >= 0; j--)
                for (int i = nx - 1; i >= 0; i--)
                {
                    int k = j * nx + i;
                    float v = dist[k];
                    if (v == 0f) continue;
                    if (i < nx - 1) v = Mathf.Min(v, dist[k + 1] + ax);
                    if (j < nz - 1)
                    {
                        v = Mathf.Min(v, dist[k + nx] + ax);
                        if (i < nx - 1) v = Mathf.Min(v, dist[k + nx + 1] + dg);
                        if (i > 0) v = Mathf.Min(v, dist[k + nx - 1] + dg);
                    }
                    dist[k] = v;
                }
            return dist;
        }

        public static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        public void FillMesh(Mesh m)
        {
            FillHeights(true);
            BuildQuads();
            m.Clear();
            if (_verts.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(_verts);
            m.SetTriangles(_tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            _filled = m;
        }

        // Heights changed but the grid did not (sinking, emerging, a ridge rising after a merge): re-uploads the
        // positions, the index buffer only when a vertex crossed the quad-culling depth, and keeps the vertex
        // colours. A full FillMesh clears the mesh, so every such step also paid for re-sending the colours.
        // Sinking and emerging move every vertex by the same amount, so the normals of the last full build stay
        // right (they only differ at the sea-floor clamp, deep under water) and recalcNormals can be false; that
        // turned a 2 ms desktop / ~10 ms phone rebuild every 0.5 s on a big sinking island into a third of it.
        public void RefreshHeights(Mesh m, bool recalcNormals)
        {
            if (m != _filled || _verts == null || _verts.Length != nx * nz || m.vertexCount != nx * nz)
            {
                FillMesh(m);
                return;
            }
            if (FillHeights(false))
            {
                BuildQuads();
                m.SetTriangles(_tris, 0, false);
            }
            m.SetVertices(_verts);
            if (recalcNormals) m.RecalculateNormals();
            m.RecalculateBounds();
        }

        const float CullDepth = -0.55f;

        // Returns whether any vertex crossed CullDepth, i.e. whether the set of drawn quads can have changed.
        bool FillHeights(bool full)
        {
            int count = nx * nz;
            if (_verts == null || _verts.Length != count) { _verts = new Vector3[count]; full = true; }
            if (_hNow == null || _hNow.Length != count) { _hNow = new float[count]; full = true; }
            bool crossed = full;
            var up = uplift;
            float w = upliftWeight, s = sink;
            for (int j = 0, k = 0; j < nz; j++)
            {
                float z = origin.y + j * cell;
                for (int i = 0; i < nx; i++, k++)
                {
                    // Same order as Height(): raw first, then the sink, so the mesh and Sample agree to the bit.
                    float y = h[k];
                    if (up != null) y += up[k] * w;
                    y -= s;
                    if (!full && (_hNow[k] < CullDepth) != (y < CullDepth)) crossed = true;
                    _hNow[k] = y;
                    if (full) _verts[k] = new Vector3(origin.x + i * cell, y > Sea ? y : Sea, z);
                    else _verts[k].y = y > Sea ? y : Sea;
                }
            }
            return crossed;
        }

        void BuildQuads()
        {
            _tris.Clear();
            for (int j = 0; j < nz - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                    if (_hNow[a] < CullDepth && _hNow[b] < CullDepth && _hNow[c] < CullDepth && _hNow[d] < CullDepth) continue;
                    _tris.Add(a); _tris.Add(c); _tris.Add(b);
                    _tris.Add(b); _tris.Add(c); _tris.Add(d);
                }
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
