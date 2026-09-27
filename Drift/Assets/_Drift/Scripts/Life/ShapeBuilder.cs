using System.Collections.Generic;
using Drift.Core;
using UnityEngine;

namespace Drift.Life
{
    public class ShapeBuilder
    {
        static readonly IcosphereGenerator.IcosphereData[] Ico = new IcosphereGenerator.IcosphereData[3];
        static readonly bool[] IcoReady = new bool[3];

        readonly List<Vector3> _v = new();
        readonly List<Vector3> _n = new();
        readonly List<Color> _c = new();
        readonly List<int> _t = new();

        // One flat triangle wound to face `outward` (wings, legs, claws of the tiny Phase 4 critters).
        public ShapeBuilder Fin(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Color col)
        {
            Tri(a, b, c, outward, col);
            return this;
        }

        void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Color col)
        {
            Vector3 cross = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(cross, outward) < 0f)
            {
                (b, c) = (c, b);
                cross = -cross;
            }
            Vector3 n = cross.normalized;
            Color lin = col.linear;
            int s = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c);
            _n.Add(n); _n.Add(n); _n.Add(n);
            _c.Add(lin); _c.Add(lin); _c.Add(lin);
            _t.Add(s); _t.Add(s + 1); _t.Add(s + 2);
        }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Color col)
        {
            Tri(a, b, c, outward, col);
            Tri(a, c, d, outward, col);
        }

        // bottom=false skips the -Y face (30 verts instead of 36) for parts that always sit on the ground.
        public ShapeBuilder Box(Vector3 center, Vector3 size, Color col, bool bottom = true)
        {
            Vector3 h = size * 0.5f;
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            float[] half = { h.x, h.y, h.z };
            for (int a = 0; a < 3; a++)
            {
                int ua = (a + 1) % 3, va = (a + 2) % 3;
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    if (!bottom && a == 1 && sgn < 0) continue;
                    Vector3 nrm = axes[a] * sgn;
                    Vector3 fc = center + nrm * half[a];
                    Vector3 u = axes[ua] * half[ua];
                    Vector3 v = axes[va] * half[va];
                    Quad(fc - u - v, fc + u - v, fc + u + v, fc - u + v, nrm, col);
                }
            }
            return this;
        }

        public ShapeBuilder Cone(Vector3 baseCenter, Vector3 axis, float radius, float height, int segments, Color col)
        {
            axis.Normalize();
            Vector3 t = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 bt = Vector3.Cross(axis, t);
            Vector3 apex = baseCenter + axis * height;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 p0 = baseCenter + (t * Mathf.Cos(a0) + bt * Mathf.Sin(a0)) * radius;
                Vector3 p1 = baseCenter + (t * Mathf.Cos(a1) + bt * Mathf.Sin(a1)) * radius;
                Vector3 mid = (t * Mathf.Cos((a0 + a1) * 0.5f) + bt * Mathf.Sin((a0 + a1) * 0.5f));
                Tri(p0, p1, apex, mid * height + axis * radius, col);
            }
            return this;
        }

        public ShapeBuilder Sphere(Vector3 center, Vector3 radii, Color col, int detail = 1)
        {
            detail = Mathf.Clamp(detail, 0, 2);
            if (!IcoReady[detail])
            {
                Ico[detail] = IcosphereGenerator.Generate(detail);
                IcoReady[detail] = true;
            }
            var ico = Ico[detail];
            for (int i = 0; i < ico.Triangles.Length; i += 3)
            {
                Vector3 a = ico.Vertices[ico.Triangles[i]];
                Vector3 b = ico.Vertices[ico.Triangles[i + 1]];
                Vector3 c = ico.Vertices[ico.Triangles[i + 2]];
                Vector3 outward = a + b + c;
                Tri(center + Vector3.Scale(a, radii), center + Vector3.Scale(b, radii), center + Vector3.Scale(c, radii), outward, col);
            }
            return this;
        }

        // A straight tube with an n-sided cross-section from a to b (6 verts per side, fan caps): sizeA/sizeB are
        // the full width (across) and height (along `up`) at each end. sides = 4 gives a tapered box whose faces
        // are square to `up`; other counts inscribe the polygon in the width/height ellipse.
        public ShapeBuilder Tube(Vector3 a, Vector3 b, Vector3 up, Vector2 sizeA, Vector2 sizeB, int sides, Color col, bool capA = true, bool capB = true)
        {
            Vector3 axis = (b - a).normalized;
            Vector3 side = Vector3.Cross(up, axis);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(Vector3.forward, axis);
            side.Normalize();
            Vector3 u = Vector3.Cross(axis, side);
            sides = Mathf.Max(3, sides);
            float s = sides == 4 ? 1.41421356f : 1f;
            Vector3 mid = (a + b) * 0.5f;
            for (int i = 0; i < sides; i++)
            {
                float a0 = (i + 0.5f) * Mathf.PI * 2f / sides, a1 = (i + 1.5f) * Mathf.PI * 2f / sides;
                Vector3 d0 = side * (Mathf.Cos(a0) * s * 0.5f), e0 = u * (Mathf.Sin(a0) * s * 0.5f);
                Vector3 d1 = side * (Mathf.Cos(a1) * s * 0.5f), e1 = u * (Mathf.Sin(a1) * s * 0.5f);
                Vector3 pa0 = a + d0 * sizeA.x + e0 * sizeA.y, pa1 = a + d1 * sizeA.x + e1 * sizeA.y;
                Vector3 pb0 = b + d0 * sizeB.x + e0 * sizeB.y, pb1 = b + d1 * sizeB.x + e1 * sizeB.y;
                Quad(pa0, pa1, pb1, pb0, (pa0 + pa1 + pb0 + pb1) * 0.25f - mid, col);
            }
            for (int end = 0; end < 2; end++)
            {
                if (end == 0 ? !capA : !capB) continue;
                Vector3 c = end == 0 ? a : b;
                Vector2 size = end == 0 ? sizeA : sizeB;
                Vector3 outward = end == 0 ? -axis : axis;
                Vector3 first = Vector3.zero, prev = Vector3.zero;
                for (int i = 0; i < sides; i++)
                {
                    float ang = (i + 0.5f) * Mathf.PI * 2f / sides;
                    Vector3 p = c + side * (Mathf.Cos(ang) * s * 0.5f * size.x) + u * (Mathf.Sin(ang) * s * 0.5f * size.y);
                    if (i == 0) first = p;
                    else if (i >= 2) Tri(first, prev, p, outward, col);
                    prev = p;
                }
            }
            return this;
        }

        // One flat quad facing `outward` (eyes, patches).
        public ShapeBuilder Patch(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Color col)
        {
            Quad(a, b, c, d, outward, col);
            return this;
        }

        // Eight-faced gem (24 verts): the cheapest closed lump for wool, humps and haunches.
        public ShapeBuilder Lump(Vector3 center, Vector3 radii, Color col)
        {
            Vector3 px = Vector3.right * radii.x, py = Vector3.up * radii.y, pz = Vector3.forward * radii.z;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        Tri(center + px * sx, center + py * sy, center + pz * sz, px * sx + py * sy + pz * sz, col * (sy < 0 ? 0.9f : 1f));
            return this;
        }

        public Vector3 VertexAt(int index) => _v[index];
        public void SetVertex(int index, Vector3 position) => _v[index] = position;
        public Vector3 NormalAt(int index) => _n[index];
        public void SetNormal(int index, Vector3 normal) => _n[index] = normal;

        public int VertexCount => _v.Count;

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            m.SetVertices(_v);
            m.SetNormals(_n);
            m.SetColors(_c);
            m.SetTriangles(_t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
