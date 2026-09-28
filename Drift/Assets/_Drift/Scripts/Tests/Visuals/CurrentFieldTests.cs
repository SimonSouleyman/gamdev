using System.Collections.Generic;
using Drift.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class CurrentFieldTests
    {
        const int Res = 16;
        const float Texel = 4f;
        const float MaxSpeed = 8f;
        static readonly Vector2 Origin = new Vector2(-32f, -32f);

        static Color32[] FillTwoPlates(Vector2 velLeft, Vector2 velRight, float edgeWidth = 8f)
        {
            var pos = new List<Vector2> { new Vector2(-20f, 0f), new Vector2(20f, 0f) };
            var vel = new List<Vector2> { velLeft, velRight };
            var dst = new Color32[Res * Res];
            CurrentField.Fill(dst, Res, Origin, Texel, pos, vel, MaxSpeed, edgeWidth);
            return dst;
        }

        static Color32 At(Color32[] px, int i, int j) => px[j * Res + i];

        [Test]
        public void EachTexelCarriesTheVelocityOfItsOwnPlate()
        {
            var px = FillTwoPlates(new Vector2(3f, 0f), new Vector2(0f, -2f));
            Vector2 left = CurrentField.DecodeVelocity(At(px, 1, 8), MaxSpeed);
            Vector2 right = CurrentField.DecodeVelocity(At(px, 14, 3), MaxSpeed);
            Assert.AreEqual(3f, left.x, 0.05f);
            Assert.AreEqual(0f, left.y, 0.05f);
            Assert.AreEqual(0f, right.x, 0.05f);
            Assert.AreEqual(-2f, right.y, 0.05f);
        }

        [Test]
        public void EdgeChannelPeaksOnTheBoundaryAndFadesAway()
        {
            var px = FillTwoPlates(Vector2.zero, Vector2.zero, 8f);
            // Texel centres at x = -2 and +2 are 2 u from the bisector x = 0, x = -6 is 6 u, x = -30 is 30 u.
            Assert.AreEqual(0.75f, At(px, 7, 5).b / 255f, 0.01f);
            Assert.AreEqual(0.75f, At(px, 8, 5).b / 255f, 0.01f);
            Assert.AreEqual(0.25f, At(px, 6, 5).b / 255f, 0.01f);
            Assert.AreEqual(0, At(px, 0, 5).b);
        }

        [Test]
        public void ClosingChannelTellsConvergingFromDivergingPlates()
        {
            var towards = FillTwoPlates(new Vector2(2f, 0f), new Vector2(-2f, 0f));
            var apart = FillTwoPlates(new Vector2(-2f, 0f), new Vector2(2f, 0f));
            Assert.Greater(At(towards, 7, 5).a, 170);
            Assert.Less(At(apart, 7, 5).a, 85);
            Assert.AreEqual(128, At(towards, 0, 5).a, 1);
        }

        [Test]
        public void VelocityBeyondTheRangeIsClampedAndNoSitesMeansStillWater()
        {
            var px = FillTwoPlates(new Vector2(50f, 0f), Vector2.zero);
            Assert.AreEqual(MaxSpeed, CurrentField.DecodeVelocity(At(px, 0, 0), MaxSpeed).x, 0.05f);

            var empty = new Color32[Res * Res];
            CurrentField.Fill(empty, Res, Origin, Texel, new List<Vector2>(), new List<Vector2>(), MaxSpeed, 8f);
            Vector2 v = CurrentField.DecodeVelocity(empty[37], MaxSpeed);
            Assert.Less(v.magnitude, 0.05f);
            Assert.AreEqual(0, empty[37].b);
        }
    }
}
