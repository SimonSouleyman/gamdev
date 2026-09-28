using Drift.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The cloud shadow texture the sea samples instead of hashing clumps per pixel (Drift.Visuals.CloudShadowTexture,
    // CloudShadowTex in Shaders/DriftClouds.hlsl), and the wake's soft cap for big islands (WaterFeedback).
    public class CloudShadowTests
    {
        [Test]
        public void Ramp_IsTheDensityBeforeItsSmoothstep()
        {
            var rnd = new System.Random(5);
            foreach (float cover in new[] { 0.2f, 0.5f, 1f })
                for (int i = 0; i < 3000; i++)
                {
                    var id = new Vector2(rnd.Next(-40, 40), rnd.Next(-40, 40));
                    var q = id + new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble());
                    var c = CloudField.ClumpAt(id, cover);
                    Assert.AreEqual(CloudField.ClumpDensity(q, id, cover), CloudField.SmoothStep01(CloudField.ClumpRamp(q, c)), 1e-4f,
                        $"id={id} q={q} cover={cover}");
                }
        }

        [Test]
        public void Texture_ShowsTheAnalyticShadow()
        {
            var tex = new CloudShadowTexture(256);
            try
            {
                var rnd = new System.Random(11);
                foreach (float cover in new[] { 0.2f, 0.45f, 0.83f })
                {
                    var centre = new Vector2(37.3f, -12.8f);
                    tex.Step(cover, centre, 0f, 4);
                    Assert.Greater(tex.CurrentParams.w, 0f);
                    var origin = new Vector2(tex.CurrentParams.x, tex.CurrentParams.y);
                    float maxErr = 0f, sumErr = 0f, covered = 0f, tinyErr = 0f;
                    const int n = 6000;
                    for (int i = 0; i < n; i++)
                    {
                        var q = origin + new Vector2(0.2f + (float)rnd.NextDouble() * (tex.SizeCells - 0.4f),
                                                     0.2f + (float)rnd.NextDouble() * (tex.SizeCells - 0.4f));
                        float want = CloudField.Density(q, cover);
                        float got = CloudShadowTexture.SampleDensity(tex.Buffer, tex.Resolution, origin, tex.Texel, q);
                        Assert.GreaterOrEqual(got, 0f);
                        // A clump still growing in (or out) with a radius under ~2.5 texels: the flat top of its ramp is
                        // narrower than a texel, so the texture shows it a little lighter in the middle. Judged separately.
                        if (NearTinyClump(q, cover)) { tinyErr = Mathf.Max(tinyErr, Mathf.Abs(got - want)); continue; }
                        float err = Mathf.Abs(got - want);
                        maxErr = Mathf.Max(maxErr, err);
                        sumErr += err;
                        if (want > 0.5f) covered++;
                    }
                    // Density error 0.09 is 0.027 of brightness at the default shadow strength 0.3 (the worst texel,
                    // on a rim lobe); on average it is a hundredth of that.
                    Assert.Less(maxErr, 0.09f, $"cover {cover}");
                    Assert.Less(sumErr / n, 0.003f, $"cover {cover}");
                    Assert.Greater(covered, 0f, $"cover {cover}: no clouds at all");
                    Assert.Less(tinyErr, 0.75f, $"cover {cover}");
                }
            }
            finally { tex.Release(); }
        }

        static bool NearTinyClump(Vector2 q, float cover)
        {
            Vector2 b = new Vector2(Mathf.Floor(q.x), Mathf.Floor(q.y));
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                var c = CloudField.ClumpAt(b + new Vector2(x, y), cover);
                if (c.radius <= 0f || c.radius >= 0.15f) continue;
                float reach = c.radius * 1.05f * c.aspect + 2f / CloudShadowTexture.TexelsPerCell;
                if ((q - c.centre).sqrMagnitude < reach * reach) return true;
            }
            return false;
        }

        [Test]
        public void Texture_RefillsOnlyWhenNeeded()
        {
            var tex = new CloudShadowTexture(128);
            try
            {
                var centre = new Vector2(3.1f, 4.2f);
                Assert.IsTrue(tex.NeedsRefill(0.3f, centre));
                tex.Step(0.3f, centre, 0f, 4);
                Assert.AreEqual(1, tex.FillCount);
                Assert.IsFalse(tex.NeedsRefill(0.3f, centre));
                Assert.IsFalse(tex.NeedsRefill(0.3f + CloudShadowTexture.CoverTolerance * 0.5f, centre + Vector2.one * 0.5f));
                Assert.IsTrue(tex.NeedsRefill(0.31f, centre));
                Assert.IsTrue(tex.NeedsRefill(0.3f, centre + new Vector2(tex.SizeCells * 0.2f, 0f)));
                // No cover: the shader's analytic path already returns "no cloud", the texture is switched off.
                tex.Step(0f, centre, 0f, 4);
                Assert.AreEqual(0f, tex.CurrentParams.w);

                // Two fills share one texel lattice, so a recentred fill matches the old one where they overlap.
                Vector2 o1 = tex.OriginFor(centre), o2 = tex.OriginFor(centre + new Vector2(2.37f, -1.91f));
                Vector2 d = (o2 - o1) * CloudShadowTexture.TexelsPerCell;
                Assert.AreEqual(Mathf.Round(d.x), d.x, 1e-3f);
                Assert.AreEqual(Mathf.Round(d.y), d.y, 1e-3f);
            }
            finally { tex.Release(); }
        }

        [Test]
        public void Texture_InPlay_SpreadsTheFillAndFadesItIn()
        {
            var tex = new CloudShadowTexture(128);
            try
            {
                // Negative cloud-space rows on purpose: the fill once mistook them for "idle" and never finished.
                var centre = new Vector2(-5f, -9f);
                int frames = 0;
                while (tex.CurrentParams.w <= 0f && frames < 100) { tex.Step(0.4f, centre, 1f / 60f, 2); frames++; }
                Assert.Greater(frames, 2, "a fill is spread over frames");
                Assert.Less(frames, 100);
                Assert.AreEqual(0f, tex.PreviousParams.w, "nothing to fade from on the first fill");

                // A changed cover refills and fades the new fill in over the old one.
                frames = 0;
                while (tex.FillCount < 2 && frames < 100) { tex.Step(0.45f, centre, 1f / 60f, 2); frames++; }
                Assert.AreEqual(2, tex.FillCount);
                Assert.Greater(tex.PreviousParams.w, 0.9f);
                for (int i = 0; i < 30; i++) tex.Step(0.45f, centre, 1f / 60f, 2);
                Assert.AreEqual(0f, tex.PreviousParams.w, 1e-5f);
                Assert.AreEqual(0.45f, tex.FilledCover, 1e-6f);
            }
            finally { tex.Release(); }
        }

        [Test]
        public void Wake_SmallIslandsUnchanged_BigOnesStayABand()
        {
            const float soft = 16f;
            for (float w = 0.5f; w <= 15.5f; w += 0.25f)
            {
                Assert.AreEqual(0.9f * w + 2f, WaterFeedback.WakeReach(w, soft), 1e-5f, $"width {w}");
                Assert.AreEqual(0f, WaterFeedback.WakeDamping(w, soft, 0.45f), $"width {w}");
            }
            float prev = WaterFeedback.WakeReach(0.5f, soft);
            for (float w = 1f; w <= 300f; w += 0.5f)
            {
                float r = WaterFeedback.WakeReach(w, soft);
                Assert.GreaterOrEqual(r, prev, "the reach never shrinks as the island grows");
                Assert.Less(r - prev, 0.9f * 0.5f + 1e-4f, "no jump at the joint");
                prev = r;
            }
            // A continent 100 u across trailed ~37 u of churn (capped only by the coast field) at full strength.
            Assert.Less(WaterFeedback.WakeReach(100f, soft), 26f);
            Assert.AreEqual(0.45f, WaterFeedback.WakeDamping(100f, soft, 0.45f), 1e-4f);
            Assert.Greater(WaterFeedback.WakeDamping(30f, soft, 0.45f), 0f);
            Assert.Less(WaterFeedback.WakeDamping(30f, soft, 0.45f), 0.45f);
        }
    }
}
