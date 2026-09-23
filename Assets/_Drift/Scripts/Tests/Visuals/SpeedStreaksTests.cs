using Drift.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class SpeedStreaksTests
    {
        [Test]
        public void Target_IsZeroWhileCruising_AndClearOnABoost()
        {
            Assert.AreEqual(0f, SpeedStreaks.Target(0f, 0f, 0f, 1f, 1f, 0.6f, 0.2f), 1e-6f);
            Assert.AreEqual(1f, SpeedStreaks.Target(1f, 0f, 0f, 1f, 1f, 0.6f, 0.2f), 1e-6f);
            Assert.AreEqual(0.6f, SpeedStreaks.Target(0f, 1f, 0f, 1f, 1f, 0.6f, 0.2f), 1e-6f, "a pickup flares them");
            Assert.AreEqual(0.2f, SpeedStreaks.Target(0f, 0f, 1f, 1f, 1f, 0.6f, 0.2f), 1e-6f, "a hint at top speed");
            Assert.AreEqual(0.5f, SpeedStreaks.Target(1f, 0f, 0f, 0.5f, 1f, 0.6f, 0.2f), 1e-6f, "cozy plays it softer");
        }

        [Test]
        public void Mesh_HasOneQuadPerStreak_SpreadAllRound()
        {
            var mesh = new Mesh();
            try
            {
                SpeedStreaks.BuildMesh(mesh, 24);
                Assert.AreEqual(96, mesh.vertexCount);
                Assert.AreEqual(144, mesh.triangles.Length);
                var uv = new System.Collections.Generic.List<Vector4>();
                mesh.GetUVs(0, uv);
                float min = float.MaxValue, max = float.MinValue;
                for (int i = 0; i < uv.Count; i += 4)
                {
                    min = Mathf.Min(min, uv[i].x);
                    max = Mathf.Max(max, uv[i].x);
                    Assert.That(uv[i].y, Is.InRange(0f, 1f), "seed");
                }
                Assert.Less(min, 0.3f);
                Assert.Greater(max, 2f * Mathf.PI - 0.3f, "angles cover the whole circle");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
