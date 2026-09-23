using Drift.Islands;
using Drift.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The 2026-09-23 GPU pass: night water light, haze start scaled with the view, far plane for far cameras and the
    // coast field texture.
    public class GpuPassTests
    {
        static readonly Color Noon = new Color(1f, 0.97f, 0.9f);
        static readonly Color Night = new Color(0.5f, 0.62f, 0.95f);

        [Test]
        public void WaterLightIsWhiteByDayAndMoonlitAtNight()
        {
            Assert.AreEqual(Color.white, DayNightCycle.NightWaterLight(Noon, 1.2f, Noon, 1.2f, 0f), "noon");
            Assert.AreEqual(Color.white, DayNightCycle.NightWaterLight(new Color(1f, 0.6f, 0.35f), 1f, Noon, 1.2f, 0f), "golden hour, not yet night");
            Color n = DayNightCycle.NightWaterLight(Night, 0.5f, Noon, 1.2f, 1f);
            Assert.Less(n.r, 0.2f);
            Assert.Less(n.g, 0.25f);
            Assert.Less(n.b, 0.6f);
            Assert.Greater(n.b, n.r, "moonlight stays bluish");
            Color half = DayNightCycle.NightWaterLight(Night, 0.5f, Noon, 1.2f, 0.5f);
            Assert.Greater(half.g, n.g);
            Assert.Less(half.g, 1f);
        }

        [Test]
        public void HazeStartKeepsCloseViewsAndClearsFarOnes()
        {
            // Fresh island at zoom 1: limb 139, haze from ~28 u; focus 8 u away, camera 14 u from it -> unchanged.
            Assert.AreEqual(28f, CurvedWorld.HazeStart(28f, 76f, 139f, 8f, 14f, 0.7f, 0.25f), 1e-4f);
            // Continent: limb 207, haze 72..176; focus 36 u away, camera 80 u from it -> starts at 36 + 56.
            Assert.AreEqual(92f, CurvedWorld.HazeStart(72f, 176f, 207f, 36f, 80f, 0.7f, 0.25f), 1e-4f);
            // A very far camera never pushes the start into the last hazeMinBand of the fade.
            Assert.AreEqual(300f - 0.25f * 350f, CurvedWorld.HazeStart(120f, 300f, 350f, 150f, 900f, 0.7f, 0.25f), 1e-4f);
            // Switched off.
            Assert.AreEqual(72f, CurvedWorld.HazeStart(72f, 176f, 207f, 36f, 80f, 0f, 0.25f), 1e-4f);
        }

        [Test]
        public void FarPlaneReachesTheLimbOfAFarPlanet()
        {
            const float invR = 1f / 260f;
            float far = CurvedWorld.FarPlaneFor(new Vector3(0f, 800f, -800f), Vector2.zero, invR);
            Assert.Greater(far, 1000f, "the camera of the soak test sat beyond the default 1000 u far plane");
            Assert.Less(far, 1500f);
            float close = CurvedWorld.FarPlaneFor(new Vector3(0f, 10f, -8f), Vector2.zero, 1f / 890f);
            Assert.Less(close, 1000f, "normal play keeps the scene's far plane");
            Assert.AreEqual(0f, CurvedWorld.FarPlaneFor(new Vector3(0f, 10f, -8f), Vector2.zero, 0f));
        }

        [Test]
        public void CoastFieldTextureMatchesTheColourPath()
        {
            var go = new GameObject("GpuPassTests_Island") { hideFlags = HideFlags.DontSave };
            var field = new CoastField();
            try
            {
                var island = go.AddComponent<Island>();
                island.useKeyboardInput = false;
                island.landRadius = 4f;
                island.RegenerateShape();
                Assert.IsTrue(field.Refresh(island));
                int res = field.Resolution;
                Color[] tex = field.Texture.GetPixels();
                var h = new float[res * res];
                float texel = field.Texel;
                for (int j = 0; j < res; j++)
                    for (int i = 0; i < res; i++)
                        h[j * res + i] = island.SampleHeight(new Vector2(field.Origin.x + (i + 0.5f) * texel, field.Origin.y + (j + 0.5f) * texel));
                var reference = new Color[res * res];
                CoastField.Fill(reference, h, new float[res * res], res, texel);
                for (int k = 0; k < res * res; k += 7)
                {
                    // Half floats: 11 bits of mantissa.
                    Assert.AreEqual(reference[k].r, tex[k].r, Mathf.Max(0.01f, Mathf.Abs(reference[k].r) * 2e-3f), "distance " + k);
                    Assert.AreEqual(reference[k].g, tex[k].g, 2e-3f, "normal x " + k);
                    Assert.AreEqual(reference[k].b, tex[k].b, 2e-3f, "normal z " + k);
                    Assert.AreEqual(1f, tex[k].a, 1e-6f);
                }
                int c = (res / 2) * res + res / 2;
                Assert.Less(tex[c].r, 0f, "the island centre is land");
                Assert.Greater(tex[0].r, 0f, "the corner is sea");
            }
            finally
            {
                field.Release();
                Object.DestroyImmediate(go);
            }
        }
    }
}
