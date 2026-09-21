using Drift.Islands;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The cheap paths added for phones must produce exactly what the slow paths they replace produced.
    public class FastPathTests
    {
        static IslandShape Hill(int n, float cell)
        {
            var shape = new IslandShape(cell, n, n, new Vector2(-n * cell * 0.5f, -n * cell * 0.5f));
            float c = (n - 1) * 0.5f;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float dx = (i - c) / c, dz = (j - c) / c;
                    shape.h[j * n + i] = 2.6f * (1f - Mathf.Sqrt(dx * dx + dz * dz)) + 0.3f * Mathf.PerlinNoise(i * 0.21f, j * 0.17f) - 0.9f;
                }
            return shape;
        }

        static void AssertSameMesh(Mesh a, Mesh b, string what)
        {
            var va = a.vertices;
            var vb = b.vertices;
            Assert.AreEqual(vb.Length, va.Length, what + ": vertex count");
            for (int k = 0; k < va.Length; k++) Assert.AreEqual(vb[k], va[k], what + ": vertex " + k);
            CollectionAssert.AreEqual(b.triangles, a.triangles, what + ": triangles");
        }

        [Test]
        public void SinkRefreshMatchesAFullRebuild()
        {
            var shape = Hill(41, 0.5f);
            var fast = new Mesh();
            var full = new Mesh();
            try
            {
                shape.FillMesh(fast);
                // Deep enough that vertices cross both the quad-culling depth and the sea-floor clamp.
                foreach (float sink in new[] { 0.05f, 0.4f, 1.1f, 1.9f, 0.2f })
                {
                    shape.sink = sink;
                    shape.RefreshHeights(fast, false);
                    shape.FillMesh(full);
                    AssertSameMesh(fast, full, "sink " + sink);
                    shape.FillMesh(fast);
                }
            }
            finally
            {
                Object.DestroyImmediate(fast);
                Object.DestroyImmediate(full);
            }
        }

        [Test]
        public void SinkRefreshKeepsTheVertexColours()
        {
            var shape = Hill(21, 0.5f);
            var mesh = new Mesh();
            try
            {
                shape.FillMesh(mesh);
                var colors = new Color[mesh.vertexCount];
                for (int k = 0; k < colors.Length; k++) colors[k] = new Color(k % 7 / 7f, 0.5f, 0.25f, 1f);
                mesh.SetColors(colors);
                shape.sink = 0.7f;
                shape.RefreshHeights(mesh, true);
                CollectionAssert.AreEqual(colors, mesh.colors);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void PrebuiltShapeIsTheShapeTheIslandWouldGenerate()
        {
            for (int seed = 11; seed < 200; seed += 37)
            {
                var type = (IslandArchetype)(1 + seed % 8);
                float radius = 3f + seed % 9, cell = IslandArchetypes.CellSize(radius);
                bool barren = Island.KindForSeed(seed) == IslandKind.Barren;
                float r = barren ? radius * Island.DefaultBarrenRadiusScale : radius;
                float hs = barren ? Island.DefaultBarrenHeightScale : 1f;

                var pre = Island.PrebuiltShape.BuildForStreamed(type, radius, seed, cell);
                var direct = IslandArchetypes.Create(type, r, seed, cell, hs);
                Assert.IsTrue(pre.Matches(type, r, seed, cell, hs), "seed " + seed + " would be thrown away");
                Assert.IsFalse(pre.Matches(type, r, seed + 1, cell, hs), "a prebuilt shape must not be used for another seed");
                CollectionAssert.AreEqual(direct.h, pre.shape.h, "seed " + seed);
            }
        }

        [Test]
        public void GroundTintIsTheBilinearCellFilter()
        {
            var go = new GameObject("TintTest") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var island = go.AddComponent<Island>();
                island.useKeyboardInput = false;
                island.landRadius = 3f;
                island.shapeSeed = 5;
                island.enabled = false;
                island.enabled = true;
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                Assert.IsNotNull(mesh);

                var cells = new[] { Color.red, Color.green, Color.blue, Color.white, Color.black, Color.yellow };
                Vector2 origin = new Vector2(-2f, -1.5f);
                const float cell = 1.25f;
                island.ApplyGroundTint(cells, 3, 2, origin, cell);

                var verts = mesh.vertices;
                var got = mesh.colors;
                for (int k = 0; k < verts.Length; k += 7)
                {
                    float fx = (verts[k].x - origin.x) / cell - 0.5f, fz = (verts[k].z - origin.y) / cell - 0.5f;
                    int i0 = Mathf.FloorToInt(fx), j0 = Mathf.FloorToInt(fz);
                    Color At(int i, int j) => cells[Mathf.Clamp(j, 0, 1) * 3 + Mathf.Clamp(i, 0, 2)];
                    Color want = Color.Lerp(Color.Lerp(At(i0, j0), At(i0 + 1, j0), fx - i0), Color.Lerp(At(i0, j0 + 1), At(i0 + 1, j0 + 1), fx - i0), fz - j0);
                    Assert.AreEqual(want.r, got[k].r, 1e-4f, "r at " + k);
                    Assert.AreEqual(want.g, got[k].g, 1e-4f, "g at " + k);
                    Assert.AreEqual(want.b, got[k].b, 1e-4f, "b at " + k);
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
