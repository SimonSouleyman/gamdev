using Drift.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class CoastFieldTests
    {
        const int Res = 64;
        const float Texel = 0.5f;

        static Vector2 Pos(int i, int j) => new Vector2((i + 0.5f - Res * 0.5f) * Texel, (j + 0.5f - Res * 0.5f) * Texel);

        // heights > 0 are land; the value is the "terrain height", so a cone gives a real waterline at radius r.
        static Color[] Field(System.Func<Vector2, float> height)
        {
            var h = new float[Res * Res];
            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                    h[j * Res + i] = height(Pos(i, j));
            var dst = new Color[Res * Res];
            CoastField.Fill(dst, h, new float[Res * Res], Res, Texel);
            return dst;
        }

        static Color At(Color[] f, Vector2 p)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt(p.x / Texel + Res * 0.5f - 0.5f), 0, Res - 1);
            int j = Mathf.Clamp(Mathf.RoundToInt(p.y / Texel + Res * 0.5f - 0.5f), 0, Res - 1);
            return f[j * Res + i];
        }

        [Test]
        public void DiscGivesTheDistanceToItsWaterline()
        {
            const float r = 6f;
            var f = Field(p => (r - p.magnitude) * 0.4f);
            foreach (var p in new[] { new Vector2(9f, 0f), new Vector2(0f, -11f), new Vector2(8f, 8f), new Vector2(-14f, 3f) })
                Assert.AreEqual(p.magnitude - r, At(f, p).r, 0.6f, "outside at " + p);
            foreach (var p in new[] { new Vector2(0f, 0f), new Vector2(3f, 1f), new Vector2(-4f, 2f) })
                Assert.AreEqual(p.magnitude - r, At(f, p).r, 0.6f, "inside at " + p);
        }

        [Test]
        public void NormalPointsAwayFromTheLand()
        {
            var f = Field(p => (6f - p.magnitude) * 0.4f);
            foreach (var p in new[] { new Vector2(10f, 0f), new Vector2(0f, 9f), new Vector2(-7f, -7f) })
            {
                var c = At(f, p);
                Assert.Greater(Vector2.Dot(new Vector2(c.g, c.b), p.normalized), 0.9f, "normal at " + p);
            }
        }

        // The whole point of the field: between two arms of a branched island the water is CLOSE to land, while
        // a bounding circle (or a radial profile from the centre) reports it as inside the island.
        [Test]
        public void NotchBetweenTwoArmsStaysWater()
        {
            // Two arms out to +-x at z = 8, nothing in between: the notch at (0, 8) is open water.
            var f = Field(p =>
            {
                float a = 3.5f - (p - new Vector2(-8f, 8f)).magnitude;
                float b = 3.5f - (p - new Vector2(8f, 8f)).magnitude;
                float c = 4.5f - p.magnitude;
                return Mathf.Max(Mathf.Max(a, b), c) * 0.4f;
            });
            var notch = At(f, new Vector2(0f, 8.5f));
            Assert.Greater(notch.r, 1.5f, "the notch must read as water well away from the shore");
            Assert.Less(notch.r, 6f, "but still close to the arms around it");
            Assert.Less(At(f, new Vector2(8f, 8f)).r, 0f, "the arm itself is land");
            Assert.Less(At(f, Vector2.zero).r, 0f, "the body is land");
        }

        [Test]
        public void MarginAndResolutionGrowWithTheIsland()
        {
            Assert.AreEqual(18f, CoastField.MarginFor(1f), 0.01f);
            Assert.Greater(CoastField.MarginFor(30f), CoastField.MarginFor(10f));
            Assert.AreEqual(52f, CoastField.MarginFor(500f), 0.01f);
            Assert.AreEqual(64, CoastField.ResolutionFor(40f));
            Assert.AreEqual(128, CoastField.ResolutionFor(200f));
        }
    }
}
