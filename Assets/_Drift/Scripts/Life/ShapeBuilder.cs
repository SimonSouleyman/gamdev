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

        public ShapeBuilder Box(Vector3 center, Vector3 size, Color col)
        {
            Vector3 h = size * 0.5f;
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            float[] half = { h.x, h.y, h.z };
            for (int a = 0; a < 3; a++)
            {
                int ua = (a + 1) % 3, va = (a + 2) % 3;
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
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
