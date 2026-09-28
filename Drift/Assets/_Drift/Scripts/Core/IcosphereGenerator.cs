using System.Collections.Generic;
using UnityEngine;

namespace Drift.Core
{
    public static class IcosphereGenerator
    {
        public struct IcosphereData
        {
            public Vector3[] Vertices;
            public int[] Triangles;
            public int[] TriangleChunkId;
        }

        static readonly int[,] BaseFaces =
        {
            {0,11,5},{0,5,1},{0,1,7},{0,7,10},{0,10,11},
            {1,5,9},{5,11,4},{11,10,2},{10,7,6},{7,1,8},
            {3,9,4},{3,4,2},{3,2,6},{3,6,8},{3,8,9},
            {4,9,5},{2,4,11},{6,2,10},{8,6,7},{9,8,1}
        };

        public static IcosphereData Generate(int subdivisions)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var vertices = new List<Vector3>
            {
                new Vector3(-1,  t,  0).normalized, new Vector3( 1,  t,  0).normalized,
                new Vector3(-1, -t,  0).normalized, new Vector3( 1, -t,  0).normalized,
                new Vector3( 0, -1,  t).normalized, new Vector3( 0,  1,  t).normalized,
                new Vector3( 0, -1, -t).normalized, new Vector3( 0,  1, -t).normalized,
                new Vector3( t,  0, -1).normalized, new Vector3( t,  0,  1).normalized,
                new Vector3(-t,  0, -1).normalized, new Vector3(-t,  0,  1).normalized,
            };

            var midpointCache = new Dictionary<long, int>();

            int GetMidpoint(int i0, int i1)
            {
                long a = i0 < i1 ? i0 : i1;
                long b = i0 < i1 ? i1 : i0;
                long key = (a << 32) + b;
                if (midpointCache.TryGetValue(key, out int cached)) return cached;

                Vector3 mid = ((vertices[i0] + vertices[i1]) * 0.5f).normalized;
                int idx = vertices.Count;
                vertices.Add(mid);
                midpointCache[key] = idx;
                return idx;
            }

            var work = new List<(int a, int b, int c, int chunkId)>();
            for (int f = 0; f < 20; f++)
                work.Add((BaseFaces[f, 0], BaseFaces[f, 1], BaseFaces[f, 2], f));

            for (int s = 0; s < subdivisions; s++)
            {
                var next = new List<(int, int, int, int)>(work.Count * 4);
                foreach (var (a, b, c, chunkId) in work)
                {
                    int ab = GetMidpoint(a, b);
                    int bc = GetMidpoint(b, c);
                    int ca = GetMidpoint(c, a);
                    next.Add((a, ab, ca, chunkId));
                    next.Add((b, bc, ab, chunkId));
                    next.Add((c, ca, bc, chunkId));
                    next.Add((ab, bc, ca, chunkId));
                }
                work = next;
            }

            var triangles = new int[work.Count * 3];
            var chunkIds = new int[work.Count];
            for (int i = 0; i < work.Count; i++)
            {
                triangles[i * 3 + 0] = work[i].a;
                triangles[i * 3 + 1] = work[i].b;
                triangles[i * 3 + 2] = work[i].c;
                chunkIds[i] = work[i].chunkId;
            }

            return new IcosphereData
            {
                Vertices = vertices.ToArray(),
                Triangles = triangles,
                TriangleChunkId = chunkIds
            };
        }
    }
}
