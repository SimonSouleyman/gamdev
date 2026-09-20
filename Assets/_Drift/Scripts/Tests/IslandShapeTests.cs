using System.Linq;
using Drift.Islands;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class IslandShapeTests
    {
        static IslandShape Grid5() => new IslandShape(1f, 5, 5, new Vector2(-2f, -2f));

        [Test]
        public void NewShape_IsAllSea()
        {
            var s = Grid5();
            Assert.That(s.h.All(v => v == IslandShape.Sea));
            Assert.AreEqual(IslandShape.Sea, s.Sample(Vector2.zero));
        }

        [Test]
        public void CreateBlob_SameSeedIsIdentical()
        {
            var a = IslandShape.CreateBlob(5f, 11, 0.5f);
            var b = IslandShape.CreateBlob(5f, 11, 0.5f);
            Assert.AreEqual(a.nx, b.nx);
            Assert.AreEqual(a.nz, b.nz);
            Assert.AreEqual(a.origin, b.origin);
            Assert.AreEqual(a.h, b.h);
        }

        [Test]
        public void CreateBlob_DifferentSeedsDiffer()
        {
            var a = IslandShape.CreateBlob(5f, 11, 0.5f);
            var b = IslandShape.CreateBlob(5f, 12, 0.5f);
            Assert.AreEqual(a.h.Length, b.h.Length);
            Assert.That(a.h.SequenceEqual(b.h), Is.False);
        }

        [Test]
        public void CreateBlob_GridIsSquareAndCenteredOnOrigin()
        {
            var s = IslandShape.CreateBlob(5f, 3, 0.5f);
            Assert.AreEqual(s.nx, s.nz);
            Assert.AreEqual(s.nx * s.nz, s.h.Length);
            Rect b = s.Bounds;
            Assert.AreEqual(0f, b.center.x, 1e-4f);
            Assert.AreEqual(0f, b.center.y, 1e-4f);
        }

        [Test]
        public void CreateBlob_HeightsStayBetweenSeaAndPlateau()
        {
            var s = IslandShape.CreateBlob(6f, 5, 0.5f);
            foreach (float v in s.h)
            {
                Assert.GreaterOrEqual(v, IslandShape.Sea - 1e-5f);
                Assert.LessOrEqual(v, 1.1f);
            }
        }

        [Test]
        public void CreateBlob_CenterIsLandAndCornersAreSea()
        {
            var s = IslandShape.CreateBlob(6f, 5, 0.5f);
            Assert.Greater(s.Sample(Vector2.zero), 0.3f);
            Assert.AreEqual(IslandShape.Sea, s.h[0]);
            Assert.AreEqual(IslandShape.Sea, s.h[s.h.Length - 1]);
        }

        [Test]
        public void CreateBlob_LargerRadiusHasMoreLand()
        {
            int Land(IslandShape s) => s.h.Count(v => v > 0f);
            Assert.Greater(Land(IslandShape.CreateBlob(8f, 2, 0.5f)), Land(IslandShape.CreateBlob(4f, 2, 0.5f)) * 2);
        }

        [Test]
        public void Sample_IsBilinearInsideAndSeaOutside()
        {
            var s = new IslandShape(2f, 2, 2, Vector2.zero);
            s.h[0] = 0f; s.h[1] = 2f; s.h[2] = 4f; s.h[3] = 6f;
            Assert.AreEqual(3f, s.Sample(new Vector2(1f, 1f)), 1e-5f);
            Assert.AreEqual(0f, s.Sample(new Vector2(0f, 0f)), 1e-5f);
            Assert.AreEqual(6f, s.Sample(new Vector2(2f, 2f)), 1e-5f);
            Assert.AreEqual(1f, s.Sample(new Vector2(1f, 0f)), 1e-5f);
            Assert.AreEqual(IslandShape.Sea, s.Sample(new Vector2(-0.1f, 1f)));
            Assert.AreEqual(IslandShape.Sea, s.Sample(new Vector2(1f, 2.1f)));
        }

        [Test]
        public void Height_AddsWeightedUplift()
        {
            var s = new IslandShape(1f, 3, 3, Vector2.zero);
            for (int k = 0; k < 9; k++) s.h[k] = 1f;
            s.uplift = new float[9];
            for (int k = 0; k < 9; k++) s.uplift[k] = 2f;
            s.upliftWeight = 0.5f;
            Assert.AreEqual(2f, s.Height(1, 1), 1e-5f);
            Assert.AreEqual(2f, s.Sample(new Vector2(1f, 1f)), 1e-5f);
            s.upliftWeight = 0f;
            Assert.AreEqual(1f, s.Height(1, 1), 1e-5f);
        }

        [Test]
        public void Bake_FoldsUpliftIntoHeightsAndClearsIt()
        {
            var s = new IslandShape(1f, 3, 3, Vector2.zero);
            for (int k = 0; k < 9; k++) s.h[k] = 1f;
            s.uplift = new float[9];
            for (int k = 0; k < 9; k++) s.uplift[k] = 2f;
            s.upliftWeight = 0.5f;
            float before = s.Sample(new Vector2(1f, 1f));

            s.Bake();

            Assert.IsNull(s.uplift);
            Assert.AreEqual(1f, s.upliftWeight);
            Assert.That(s.h.All(v => Mathf.Approximately(v, 2f)));
            Assert.AreEqual(before, s.Sample(new Vector2(1f, 1f)), 1e-5f);
        }

        [Test]
        public void Bake_WithoutUpliftIsANoOp()
        {
            var s = new IslandShape(1f, 3, 3, Vector2.zero);
            for (int k = 0; k < 9; k++) s.h[k] = 0.25f;
            s.upliftWeight = 0.3f;
            s.Bake();
            Assert.That(s.h.All(v => v == 0.25f));
            Assert.AreEqual(0.3f, s.upliftWeight);
        }

        [Test]
        public void LandBounds_CoversCellsAboveThreshold()
        {
            var s = Grid5();
            s.h[1 * 5 + 1] = 1f;
            s.h[2 * 5 + 3] = 0.5f;
            Rect r = s.LandBounds(0f);
            Assert.AreEqual(-1f, r.xMin, 1e-5f);
            Assert.AreEqual(-1f, r.yMin, 1e-5f);
            Assert.AreEqual(1f, r.xMax, 1e-5f);
            Assert.AreEqual(0f, r.yMax, 1e-5f);
        }

        [Test]
        public void LandBounds_RespectsAboveSeaMargin()
        {
            var s = Grid5();
            s.h[0] = -0.7f;
            Assert.AreEqual(new Vector2(-2f, -2f), s.LandBounds(0.05f).min);
            Rect none = s.LandBounds(0.2f);
            Assert.AreEqual(0f, none.width);
            Assert.AreEqual(0f, none.height);
            Assert.AreEqual(s.origin, none.position);
        }

        [Test]
        public void LandCentroid_IsAverageOfCellsAboveZero()
        {
            var s = Grid5();
            s.h[1 * 5 + 1] = 1f;
            s.h[1 * 5 + 3] = 1f;
            s.h[4 * 5 + 4] = 0f;
            Vector2 c = s.LandCentroid();
            Assert.AreEqual(0f, c.x, 1e-5f);
            Assert.AreEqual(-1f, c.y, 1e-5f);
        }

        [Test]
        public void LandCentroid_WithNoLandIsZero()
        {
            Assert.AreEqual(Vector2.zero, Grid5().LandCentroid());
        }

        [Test]
        public void LandCentroid_OfBlobIsNearItsCenter()
        {
            var s = IslandShape.CreateBlob(6f, 9, 0.5f);
            Assert.Less(s.LandCentroid().magnitude, 6f * 0.3f);
        }

        [Test]
        public void Smooth_ClampsAndHitsMidpoint()
        {
            Assert.AreEqual(0f, IslandShape.Smooth(0f, 1f, -5f));
            Assert.AreEqual(1f, IslandShape.Smooth(0f, 1f, 5f));
            Assert.AreEqual(0.5f, IslandShape.Smooth(0f, 1f, 0.5f), 1e-6f);
        }

        [Test]
        public void FillMesh_OneVertexPerCellNeverBelowSea()
        {
            var s = IslandShape.CreateBlob(4f, 1, 0.5f);
            var m = new Mesh();
            try
            {
                s.FillMesh(m);
                Assert.AreEqual(s.nx * s.nz, m.vertexCount);
                Assert.Greater(m.triangles.Length, 0);
                Assert.AreEqual(0, m.triangles.Length % 3);
                foreach (var v in m.vertices) Assert.GreaterOrEqual(v.y, IslandShape.Sea - 1e-5f);
            }
            finally
            {
                Object.DestroyImmediate(m);
            }
        }
    }
}
