using Drift.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The cloud clump field shared by the cloud puffs, their shadows and the storms (Drift.Visuals.CloudField mirrors
    // Shaders/DriftClouds.hlsl).
    public class CloudTests
    {
        static float BruteDensity(Vector2 q, float cover)
        {
            Vector2 b = new Vector2(Mathf.Floor(q.x), Mathf.Floor(q.y));
            float d = 0f;
            for (int y = -2; y <= 2; y++)
            for (int x = -2; x <= 2; x++)
                d = Mathf.Max(d, CloudField.ClumpDensity(q, b + new Vector2(x, y), cover));
            return d;
        }

        [Test]
        public void NoCover_NoClouds()
        {
            for (int i = 0; i < 200; i++)
                Assert.AreEqual(0f, CloudField.Density(new Vector2(i * 0.37f, i * 0.91f), 0f));
        }

        [Test]
        public void NearestFourCells_SeeEveryClump()
        {
            var rnd = new System.Random(3);
            foreach (float cover in new[] { 0.2f, 0.4f, 0.7f, 1f })
                for (int i = 0; i < 4000; i++)
                {
                    var q = new Vector2((float)rnd.NextDouble() * 60f - 30f, (float)rnd.NextDouble() * 60f - 30f);
                    Assert.AreEqual(BruteDensity(q, cover), CloudField.Density(q, cover), 1e-5f, $"q={q} cover={cover}");
                }
        }

        [Test]
        public void MoreCover_MoreSky()
        {
            float prev = -1f;
            foreach (float cover in new[] { 0.1f, 0.3f, 0.5f, 0.8f })
            {
                float sum = 0f;
                for (int y = 0; y < 80; y++)
                for (int x = 0; x < 80; x++)
                    sum += CloudField.Density(new Vector2(x * 0.25f, y * 0.25f), cover) > 0.5f ? 1f : 0f;
                float share = sum / 6400f;
                Assert.Greater(share, prev, $"cover {cover}");
                prev = share;
            }
            // Fair weather stays broken up into separate clumps, a storm cover still leaves gaps between them.
            Assert.Less(prev, 0.6f);
        }

        [Test]
        public void EveryPuff_SitsOverItsShadow()
        {
            const float cell = 18f;
            int checkedPuffs = 0;
            for (int y = 0; y < 12; y++)
            for (int x = 0; x < 12; x++)
            {
                var id = new Vector2(x, y);
                var c = CloudField.ClumpAt(id, 0.6f);
                if (c.radius < 0.05f) continue;
                for (int k = 0; k < CloudShadows.PuffsPerClump; k++)
                {
                    CloudField.PuffLayout(k, CloudField.Hash(id + new Vector2(0.37f, 0.37f)), c.axis, c.aspect, c.radius * cell, out Vector2 off, out float size, out _);
                    Vector2 q = c.centre + off / cell;
                    Assert.Greater(CloudField.Density(q, 0.6f), 0.35f, $"clump {id} puff {k}");
                    Assert.Greater(size, 0f);
                    checkedPuffs++;
                }
            }
            Assert.Greater(checkedPuffs, 100);
        }

        [Test]
        public void PuffMesh_OneQuadPerPuffSlot()
        {
            var mesh = CloudShadows.BuildPuffMesh();
            try
            {
                int quads = CloudShadows.GridSize * CloudShadows.GridSize * CloudShadows.PuffsPerClump;
                Assert.AreEqual(quads * 4, mesh.vertexCount);
                Assert.AreEqual(quads * 6, mesh.triangles.Length);
                Assert.Less(mesh.vertexCount, 65535);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
