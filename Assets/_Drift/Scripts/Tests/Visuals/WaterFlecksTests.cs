using Drift.Visuals;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Drift.Tests
{
    public class WaterFlecksTests
    {
        static readonly float[] Heights = { 0.5f, 3f, 8f, 15f, 40f, 120f, 400f, 1500f };
        static readonly float[] Reaches = { 30f, 90f, 250f, 900f, 4000f, float.PositiveInfinity };

        // The vertex shader's own test, sampled over every sea point a camera at that height can see before the haze.
        [Test]
        public void VisibleLevels_ContainEveryLevelTheShaderCanKeep()
        {
            foreach (float h in Heights)
                foreach (float reach in Reaches)
                {
                    WaterFlecks.VisibleLevels(h, reach, out int first, out int last);
                    float maxR = float.IsInfinity(reach) ? 1e5f : reach;
                    for (int s = 0; s <= 4000; s++)
                    {
                        float r = maxR * s / 4000f;
                        float lc = Mathf.Log(Mathf.Max(Mathf.Sqrt(h * h + r * r), 1f) / 12f, 2f);
                        for (int i = 0; i < WaterFlecks.Levels; i++)
                        {
                            float lv = WaterFlecks.LevelMin + i;
                            float gridReach = (WaterFlecks.GridRadius + 1f) * 2f * Mathf.Pow(2f, lv);
                            if (Mathf.Abs(lc - lv) > 1.35f || r > gridReach) continue;
                            Assert.That(i >= first && i <= last, $"h {h}, reach {reach}: level {lv} shows at {r} u but [{first}, {last}] is drawn");
                        }
                    }
                }
        }

        [Test]
        public void VisibleLevels_DropTheLevelsOutOfReach()
        {
            // Chase camera: nothing finer than lv -1, nothing coarser than lv 5.
            WaterFlecks.VisibleLevels(15f, 150f, out int first, out int last);
            Assert.AreEqual(2, first);
            Assert.AreEqual(8, last);
            // Close to the water the finest level shows, the coarse ones do not.
            WaterFlecks.VisibleLevels(3f, 60f, out first, out last);
            Assert.AreEqual(0, first);
            Assert.AreEqual(6, last);
            // Zoomed far out only the coarse end is left.
            WaterFlecks.VisibleLevels(300f, 1500f, out first, out last);
            Assert.AreEqual(7, first);
            Assert.AreEqual(WaterFlecks.Levels - 1, last);
            // Without haze nothing bounds the far end.
            WaterFlecks.VisibleLevels(15f, float.PositiveInfinity, out first, out last);
            Assert.AreEqual(2, first);
            Assert.AreEqual(WaterFlecks.Levels - 1, last);
            // Far above every level's band: nothing to draw.
            WaterFlecks.VisibleLevels(1e6f, 2e6f, out first, out last);
            Assert.Greater(first, last);
        }

        [Test]
        public void FogReach_IsTheFullHazeDistancePlusTheHazeCentreOffset()
        {
            var fog = new Vector4(100f, 1f / 50f, 10f, 0f);
            Assert.AreEqual(160f, WaterFlecks.FogReach(fog, Vector2.zero, 0f), 1e-3f);
            Assert.IsTrue(float.IsPositiveInfinity(WaterFlecks.FogReach(new Vector4(1e6f, 0f, 0f, 0f), Vector2.zero, 0f)), "no haze");
            Assert.IsTrue(float.IsPositiveInfinity(WaterFlecks.FogReach(Vector4.zero, Vector2.zero, 0f)), "haze unset");
            Assert.IsTrue(float.IsPositiveInfinity(WaterFlecks.FogReach(fog, Vector2.zero, -3000f)), "below the curve guard");
        }

        [Test]
        public void IndexRange_TilesTheWholeMeshLevelByLevel()
        {
            WaterFlecks.IndexRange(0, WaterFlecks.Levels - 1, out int start, out int count);
            Assert.AreEqual(0, start);
            Assert.AreEqual(WaterFlecks.QuadCount * 6, count);
            WaterFlecks.IndexRange(0, 2, out int s0, out int c0);
            WaterFlecks.IndexRange(3, 7, out int s1, out int c1);
            Assert.AreEqual(s0 + c0, s1);
            Assert.AreEqual(WaterFlecks.QuadCount * 6 / WaterFlecks.Levels * 5, c1);
            WaterFlecks.IndexRange(5, 4, out _, out int empty);
            Assert.AreEqual(0, empty);
        }

        [Test]
        public void Mesh_AcceptsALevelRangeAfterUpload()
        {
            var mesh = WaterFlecks.BuildMesh();
            try
            {
                Assert.AreEqual(WaterFlecks.QuadCount * 4, mesh.vertexCount);
                WaterFlecks.IndexRange(2, 8, out int start, out int count);
                mesh.SetSubMesh(0, new SubMeshDescriptor(start, count) { firstVertex = start / 6 * 4, vertexCount = count / 6 * 4 },
                    MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                var d = mesh.GetSubMesh(0);
                Assert.AreEqual(start, d.indexStart);
                Assert.AreEqual(count, d.indexCount);
                Assert.Greater(mesh.bounds.size.x, 1e5f, "the flecks are placed by the shader: the bounds stay huge");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
