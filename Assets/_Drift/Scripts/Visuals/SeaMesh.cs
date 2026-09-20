using System;
using System.Collections.Generic;
using UnityEngine;

namespace Drift.Visuals
{
    // Flat-shaded low-poly template in local space (x right, y up, z forward), built once.
    public sealed class SeaTemplate
    {
        public Vector3[] v;
        public Vector3[] n;
        public Color[] c;
        public int[] t;
        public int VertexCount => v.Length;
    }

    // Builder for SeaTemplate. Every face gets its own vertices and a normal pointing away from `center`,
    // so winding mistakes cannot produce inward lighting (the sea materials draw two-sided).
    public sealed class SeaShape
    {
        readonly List<Vector3> _v = new();
        readonly List<Vector3> _n = new();
        readonly List<Color> _c = new();
        readonly List<int> _t = new();
        public Vector3 center;

        public SeaShape Tri(Vector3 a, Vector3 b, Vector3 c, Color col)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-10f) return this;
            n.Normalize();
            if (Vector3.Dot(n, (a + b + c) / 3f - center) < 0f)
            {
                n = -n;
                (b, c) = (c, b);
            }
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c);
            _n.Add(n); _n.Add(n); _n.Add(n);
            _c.Add(col); _c.Add(col); _c.Add(col);
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
            return this;
        }

        public SeaShape Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            if ((a - b).sqrMagnitude < 1e-10f) return Tri(a, c, d, col);
            if ((c - d).sqrMagnitude < 1e-10f) return Tri(a, b, c, col);
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-10f) n = Vector3.Cross(c - a, d - a);
            if (n.sqrMagnitude < 1e-10f) return this;
            n.Normalize();
            bool flip = Vector3.Dot(n, (a + b + c + d) * 0.25f - center) < 0f;
            if (flip) n = -n;
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            for (int k = 0; k < 4; k++) { _n.Add(n); _c.Add(col); }
            if (!flip) { _t.Add(i); _t.Add(i + 1); _t.Add(i + 2); _t.Add(i); _t.Add(i + 2); _t.Add(i + 3); }
            else { _t.Add(i); _t.Add(i + 2); _t.Add(i + 1); _t.Add(i); _t.Add(i + 3); _t.Add(i + 2); }
            return this;
        }

        // Thin two-sided sheet (sail, flag, fin) with an explicit normal.
        public SeaShape Sheet(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col, Vector3 normal)
        {
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            for (int k = 0; k < 4; k++) { _n.Add(normal); _c.Add(col); }
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2); _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
            return this;
        }

        public SeaShape SheetTri(Vector3 a, Vector3 b, Vector3 c, Color col, Vector3 normal)
        {
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c);
            for (int k = 0; k < 3; k++) { _n.Add(normal); _c.Add(col); }
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
            return this;
        }

        public SeaShape Box(Vector3 c, Vector3 size, Color col, bool bottom = false)
        {
            Vector3 saved = center;
            center = c;
            Vector3 h = size * 0.5f;
            Vector3 p000 = c + new Vector3(-h.x, -h.y, -h.z), p100 = c + new Vector3(h.x, -h.y, -h.z);
            Vector3 p010 = c + new Vector3(-h.x, h.y, -h.z), p110 = c + new Vector3(h.x, h.y, -h.z);
            Vector3 p001 = c + new Vector3(-h.x, -h.y, h.z), p101 = c + new Vector3(h.x, -h.y, h.z);
            Vector3 p011 = c + new Vector3(-h.x, h.y, h.z), p111 = c + new Vector3(h.x, h.y, h.z);
            Quad(p010, p011, p111, p110, col);
            Quad(p000, p010, p110, p100, col);
            Quad(p001, p101, p111, p011, col);
            Quad(p000, p001, p011, p010, col);
            Quad(p100, p110, p111, p101, col);
            if (bottom) Quad(p000, p100, p101, p001, col);
            center = saved;
            return this;
        }

        // Tapered prism along +y from baseCenter.
        public SeaShape Prism(Vector3 baseCenter, float r0, float r1, float height, int segs, Color side, Color cap, bool capTop = true)
        {
            Vector3 saved = center;
            center = baseCenter + Vector3.up * (height * 0.5f);
            Vector3 top = baseCenter + Vector3.up * height;
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2f / segs, a1 = (i + 1) * Mathf.PI * 2f / segs;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Quad(baseCenter + d0 * r0, top + d0 * r1, top + d1 * r1, baseCenter + d1 * r0, side);
                if (capTop && r1 > 1e-4f) Tri(top, top + d0 * r1, top + d1 * r1, cap);
            }
            center = saved;
            return this;
        }

        public struct Ring
        {
            public float z, halfWidth, halfHeight, y;
            public Ring(float z, float halfWidth, float halfHeight, float y = 0f)
            {
                this.z = z; this.halfWidth = halfWidth; this.halfHeight = halfHeight; this.y = y;
            }
        }

        // Body along z through elliptical rings; arcFrom..arcTo in radians around z (0 = +x, pi/2 = up).
        // colorOf gets the face normal and the z of the span centre.
        public SeaShape Loft(Ring[] rings, int segs, float arcFrom, float arcTo, Func<Vector3, float, Color> colorOf)
        {
            Vector3 saved = center;
            float midY = 0f;
            for (int r = 0; r < rings.Length; r++) midY += rings[r].y / rings.Length;
            center = new Vector3(0f, midY, (rings[0].z + rings[rings.Length - 1].z) * 0.5f);
            for (int r = 0; r + 1 < rings.Length; r++)
            {
                Ring A = rings[r], B = rings[r + 1];
                for (int s = 0; s < segs; s++)
                {
                    float a0 = Mathf.Lerp(arcFrom, arcTo, s / (float)segs), a1 = Mathf.Lerp(arcFrom, arcTo, (s + 1) / (float)segs);
                    Vector3 a = RingPoint(A, a0), b = RingPoint(A, a1), c = RingPoint(B, a1), d = RingPoint(B, a0);
                    Vector3 n = Vector3.Cross(c - a, d - b);
                    if (n.sqrMagnitude < 1e-12f) continue;
                    n.Normalize();
                    if (Vector3.Dot(n, (a + b + c + d) * 0.25f - center) < 0f) n = -n;
                    Quad(a, b, c, d, colorOf(n, (A.z + B.z) * 0.5f));
                }
            }
            center = saved;
            return this;
        }

        static Vector3 RingPoint(Ring r, float a) => new Vector3(Mathf.Cos(a) * r.halfWidth, r.y + Mathf.Sin(a) * r.halfHeight, r.z);

        public SeaTemplate Build() => new SeaTemplate { v = _v.ToArray(), n = _n.ToArray(), c = _c.ToArray(), t = _t.ToArray() };
    }

    // Preallocated dynamic mesh buffers; everything that does not fit is dropped silently (hard vertex cap).
    public sealed class SeaBatch
    {
        public readonly Vector3[] verts;
        public readonly Vector3[] norms;
        public readonly Color[] cols;
        public readonly int[] tris;
        public int vc, tc;
        // Vertices this batch may use; lower it when several batches share one budget.
        public int limit;
        readonly bool _normals;

        public SeaBatch(int maxVerts, bool normals)
        {
            verts = new Vector3[maxVerts];
            cols = new Color[maxVerts];
            norms = normals ? new Vector3[maxVerts] : null;
            tris = new int[maxVerts * 3];
            limit = maxVerts;
            _normals = normals;
        }

        public void Clear() { vc = 0; tc = 0; }

        public bool Fits(int nv, int nt) => vc + nv <= limit && vc + nv <= verts.Length && tc + nt <= tris.Length;

        public static void Basis(Vector2 dir, float pitch, float roll, out Vector3 right, out Vector3 up, out Vector3 fwd)
        {
            float cp = Mathf.Cos(pitch), sp = Mathf.Sin(pitch);
            fwd = new Vector3(dir.x * cp, sp, dir.y * cp);
            Vector3 flatRight = new Vector3(dir.y, 0f, -dir.x);
            Vector3 up0 = new Vector3(-dir.x * sp, cp, -dir.y * sp);
            float cr = Mathf.Cos(roll), sr = Mathf.Sin(roll);
            right = flatRight * cr + up0 * sr;
            up = up0 * cr - flatRight * sr;
        }

        public void Add(SeaTemplate t, Vector3 pos, Vector3 right, Vector3 up, Vector3 fwd, float scale) =>
            Add(t, pos, right, up, fwd, new Vector3(scale, scale, scale), Color.white);

        public void Add(SeaTemplate t, Vector3 pos, Vector3 right, Vector3 up, Vector3 fwd, Vector3 scale, Color mul)
        {
            int nv = t.v.Length, nt = t.t.Length;
            if (!Fits(nv, nt)) return;
            Vector3 rx = right * scale.x, uy = up * scale.y, fz = fwd * scale.z;
            int b = vc;
            for (int i = 0; i < nv; i++)
            {
                Vector3 p = t.v[i];
                verts[b + i] = new Vector3(
                    pos.x + rx.x * p.x + uy.x * p.y + fz.x * p.z,
                    pos.y + rx.y * p.x + uy.y * p.y + fz.y * p.z,
                    pos.z + rx.z * p.x + uy.z * p.y + fz.z * p.z);
                Color c = t.c[i];
                cols[b + i] = new Color(c.r * mul.r, c.g * mul.g, c.b * mul.b, c.a * mul.a);
            }
            if (_normals)
                for (int i = 0; i < nv; i++)
                {
                    Vector3 n = t.n[i];
                    norms[b + i] = new Vector3(
                        right.x * n.x + up.x * n.y + fwd.x * n.z,
                        right.y * n.x + up.y * n.y + fwd.y * n.z,
                        right.z * n.x + up.z * n.y + fwd.z * n.z);
                }
            int tb = tc;
            for (int i = 0; i < nt; i++) tris[tb + i] = b + t.t[i];
            vc += nv;
            tc += nt;
        }

        // One colour for the whole instance (underwater silhouettes); the template alpha still applies.
        public void AddFlat(SeaTemplate t, Vector3 pos, Vector3 right, Vector3 up, Vector3 fwd, Vector3 scale, Color flat)
        {
            int nv = t.v.Length, nt = t.t.Length;
            if (!Fits(nv, nt)) return;
            Vector3 rx = right * scale.x, uy = up * scale.y, fz = fwd * scale.z;
            int b = vc;
            for (int i = 0; i < nv; i++)
            {
                Vector3 p = t.v[i];
                verts[b + i] = new Vector3(
                    pos.x + rx.x * p.x + uy.x * p.y + fz.x * p.z,
                    pos.y + rx.y * p.x + uy.y * p.y + fz.y * p.z,
                    pos.z + rx.z * p.x + uy.z * p.y + fz.z * p.z);
                cols[b + i] = flat;
            }
            if (_normals) for (int i = 0; i < nv; i++) norms[b + i] = up;
            int tb = tc;
            for (int i = 0; i < nt; i++) tris[tb + i] = b + t.t[i];
            vc += nv;
            tc += nt;
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color col, Vector3 normal)
        {
            if (!Fits(3, 3)) return;
            int i = vc;
            verts[i] = a; verts[i + 1] = b; verts[i + 2] = c;
            cols[i] = col; cols[i + 1] = col; cols[i + 2] = col;
            if (_normals) { norms[i] = normal; norms[i + 1] = normal; norms[i + 2] = normal; }
            tris[tc] = i; tris[tc + 1] = i + 1; tris[tc + 2] = i + 2;
            vc += 3; tc += 3;
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col, Vector3 normal)
        {
            if (!Fits(4, 6)) return;
            int i = vc;
            verts[i] = a; verts[i + 1] = b; verts[i + 2] = c; verts[i + 3] = d;
            cols[i] = col; cols[i + 1] = col; cols[i + 2] = col; cols[i + 3] = col;
            if (_normals) { norms[i] = normal; norms[i + 1] = normal; norms[i + 2] = normal; norms[i + 3] = normal; }
            tris[tc] = i; tris[tc + 1] = i + 1; tris[tc + 2] = i + 2;
            tris[tc + 3] = i; tris[tc + 4] = i + 2; tris[tc + 5] = i + 3;
            vc += 4; tc += 6;
        }

        // Flat n-gon on the water (foam chips, glow discs), fan around the centre.
        public void Disc(Vector3 c, float rx, float rz, Vector2 dir, int segs, Color inner, Color outer)
        {
            if (!Fits(segs + 1, segs * 3)) return;
            int b = vc;
            verts[b] = c; cols[b] = inner;
            if (_normals) norms[b] = Vector3.up;
            Vector3 f = new Vector3(dir.x, 0f, dir.y), r = new Vector3(dir.y, 0f, -dir.x);
            for (int i = 0; i < segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs;
                verts[b + 1 + i] = c + r * (Mathf.Cos(a) * rx) + f * (Mathf.Sin(a) * rz);
                cols[b + 1 + i] = outer;
                if (_normals) norms[b + 1 + i] = Vector3.up;
                tris[tc++] = b;
                tris[tc++] = b + 1 + (i + 1) % segs;
                tris[tc++] = b + 1 + i;
            }
            vc += segs + 1;
        }

        // Six-vertex octahedron with radial normals (spray, spout droplets, lantern glints).
        public void Octa(Vector3 c, float r, Color col)
        {
            if (!Fits(6, 24)) return;
            int b = vc;
            verts[b] = c + new Vector3(0f, r, 0f); verts[b + 1] = c - new Vector3(0f, r, 0f);
            verts[b + 2] = c + new Vector3(r, 0f, 0f); verts[b + 3] = c - new Vector3(r, 0f, 0f);
            verts[b + 4] = c + new Vector3(0f, 0f, r); verts[b + 5] = c - new Vector3(0f, 0f, r);
            for (int i = 0; i < 6; i++) cols[b + i] = col;
            if (_normals)
            {
                norms[b] = Vector3.up; norms[b + 1] = Vector3.down;
                norms[b + 2] = Vector3.right; norms[b + 3] = Vector3.left;
                norms[b + 4] = Vector3.forward; norms[b + 5] = Vector3.back;
            }
            int t = tc;
            tris[t++] = b; tris[t++] = b + 4; tris[t++] = b + 2;
            tris[t++] = b; tris[t++] = b + 2; tris[t++] = b + 5;
            tris[t++] = b; tris[t++] = b + 5; tris[t++] = b + 3;
            tris[t++] = b; tris[t++] = b + 3; tris[t++] = b + 4;
            tris[t++] = b + 1; tris[t++] = b + 2; tris[t++] = b + 4;
            tris[t++] = b + 1; tris[t++] = b + 5; tris[t++] = b + 2;
            tris[t++] = b + 1; tris[t++] = b + 3; tris[t++] = b + 5;
            tris[t++] = b + 1; tris[t++] = b + 4; tris[t++] = b + 3;
            tc = t;
            vc += 6;
        }

        public void Apply(Mesh mesh, Bounds bounds)
        {
            mesh.Clear(false);
            mesh.SetVertices(verts, 0, vc);
            if (_normals) mesh.SetNormals(norms, 0, vc);
            mesh.SetColors(cols, 0, vc);
            mesh.SetTriangles(tris, 0, tc, 0, false);
            mesh.bounds = bounds;
        }
    }

    // Islands near the player as steering circles, shared by the sea systems. Index 0 is always the player slot
    // (radius 0 when there is none); island references stay so callers can sample the real shelf.
    public sealed class SeaObstacles
    {
        public const int Max = 24;
        public readonly Vector3[] circles = new Vector3[Max];
        public readonly Drift.Islands.Island[] islands = new Drift.Islands.Island[Max];
        public int count;

        public void Refresh(Drift.Islands.Island player, Vector2 around, float range)
        {
            count = 1;
            islands[0] = player;
            var all = Drift.Islands.Island.All;
            for (int i = 0; i < all.Count && count < Max; i++)
            {
                var isl = all[i];
                if (isl == null || isl == player || !isl.isActiveAndEnabled || isl.IsSunk) continue;
                float r = isl.BoundingRadius;
                if ((isl.PlanarPosition - around).magnitude - r > range) continue;
                islands[count++] = isl;
            }
            for (int i = count; i < Max; i++) islands[i] = null;
            Update();
        }

        public void Update()
        {
            for (int i = 0; i < count; i++)
            {
                var isl = islands[i];
                if (isl == null) { circles[i] = new Vector3(0f, 0f, -1000f); continue; }
                Vector2 p = isl.PlanarPosition;
                circles[i] = new Vector3(p.x, p.y, isl.BoundingRadius);
            }
        }

        public bool Inside(Vector2 p, float margin)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 c = circles[i];
                float dx = p.x - c.x, dy = p.y - c.y, r = c.z + margin;
                if (r > 0f && dx * dx + dy * dy < r * r) return true;
            }
            return false;
        }

        // True where some island's ground rises above `height` (the shelf a hull or fin would clip into).
        public bool Shallow(Vector2 p, float height, out Drift.Islands.Island hit)
        {
            for (int i = 0; i < count; i++)
            {
                var isl = islands[i];
                if (isl == null) continue;
                Vector3 c = circles[i];
                float dx = p.x - c.x, dy = p.y - c.y;
                if (dx * dx + dy * dy > c.z * c.z) continue;
                if (isl.SampleHeight(isl.ToLocal(p)) > height) { hit = isl; return true; }
            }
            hit = null;
            return false;
        }
    }
}
