using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Owner, 2026-09-22: "im herdebeobachten modus zittern die bäume zu stark." The sway the chase camera sees is
    // unchanged; the closer the camera gets, the smaller and slower the fast flutter becomes, so a watched tree
    // breathes instead of shaking.
    public class VegetationWindTests
    {
        [SetUp]
        public void SetUp()
        {
            LifeEnvironment.WindProvider = null;
            LifeEnvironment.StormProvider = null;
            IslandLifeSystem.ClearViewDistance();
        }

        [TearDown]
        public void TearDown()
        {
            LifeEnvironment.WindProvider = null;
            LifeEnvironment.StormProvider = null;
            IslandLifeSystem.ClearViewDistance();
            IslandLifeSystem.PushWind(true);
        }

        // The sliders live on the component; the wind is one global, so the curve reads a static mirror of them.
        static void Defaults()
        {
            var go = new GameObject("WindSettings");
            go.SetActive(false);
            go.AddComponent<IslandLifeSystem>().ApplyWindSettings();
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Curve_FarIsUntouched_NearIsCalm_AndRisesMonotonically()
        {
            Defaults();
            IslandLifeSystem.SwayScales(200f, out float amp, out float flut, out float speed);
            Assert.AreEqual(1f, amp, 1e-5f, "the chase camera sees the wind it always saw");
            Assert.AreEqual(1f, flut, 1e-5f);
            Assert.AreEqual(1f, speed, 1e-5f);

            IslandLifeSystem.SwayScales(2f, out amp, out flut, out speed);
            Assert.Less(amp, 0.6f, "a tree two metres away leans about half as far");
            Assert.Less(flut, 0.35f, "the fast flutter almost disappears");
            Assert.Less(speed, 0.5f, "and what is left of it runs slowly");
            Assert.Greater(amp, 0.05f, "it still moves - not a frozen model");
            Assert.Greater(speed, 0.05f);

            // No step anywhere: a camera pulling back must not make the wind jump.
            float prevAmp = 0f, prevSpeed = 0f;
            for (float d = 0f; d <= 60f; d += 0.5f)
            {
                IslandLifeSystem.SwayScales(60f - d, out float a, out _, out float s);
                if (d > 0f)
                {
                    Assert.LessOrEqual(a, prevAmp + 1e-4f, "amplitude falls as the camera comes closer");
                    Assert.LessOrEqual(s, prevSpeed + 1e-4f);
                }
                prevAmp = a;
                prevSpeed = s;
            }
            Assert.AreEqual(0f, IslandLifeSystem.CalmAmount(1000f, 4f, 26f), 1e-5f);
            Assert.AreEqual(1f, IslandLifeSystem.CalmAmount(0.5f, 4f, 26f), 1e-5f);
            Assert.AreEqual(1f, IslandLifeSystem.CalmAmount(4f, 4f, 26f), 1e-5f);
            // A degenerate range (both sliders equal) still answers, it does not divide by zero.
            Assert.AreEqual(1f, IslandLifeSystem.CalmAmount(3f, 10f, 10f), 1e-5f);
            Assert.AreEqual(0f, IslandLifeSystem.CalmAmount(30f, 10f, 10f), 1e-5f);
        }

        [Test]
        public void PushWind_ReportedDistanceDrivesTheGlobal_AndThePhasesOnlyRunForward()
        {
            Defaults();
            LifeEnvironment.WindProvider = () => new Vector2(1f, 0f);
            LifeEnvironment.StormProvider = () => 0f;

            IslandLifeSystem.ReportViewDistance(2f);
            IslandLifeSystem.PushWind(true);
            var near = IslandLifeSystem.LastSway;
            Assert.AreEqual(2f, IslandLifeSystem.ViewDistance, 1e-4f);
            Assert.Less(near.x, 1f, "the close camera calms the lean");
            Assert.Less(near.w, 1f, "and the flutter");
            // The wind vector itself is untouched, so everything else it drives keeps its old look.
            Assert.AreEqual(1f, IslandLifeSystem.LastWind.x, 1e-5f);
            Assert.AreEqual(0f, IslandLifeSystem.LastWind.z, 1e-5f);

            IslandLifeSystem.ReportViewDistance(120f);
            IslandLifeSystem.PushWind(true);
            var far = IslandLifeSystem.LastSway;
            Assert.AreEqual(1f, far.x, 1e-4f);
            Assert.AreEqual(1f, far.w, 1e-4f);
            // A forced re-push inside the same frame must not advance the phases twice (eval screenshots).
            Assert.AreEqual(near.y, far.y, 1e-6f);
            Assert.AreEqual(near.z, far.z, 1e-6f);
            Assert.That(far.y, Is.InRange(0f, Mathf.PI * 2f));
            Assert.That(far.z, Is.InRange(0f, Mathf.PI * 2f));

            IslandLifeSystem.ClearViewDistance();
            Assert.AreEqual(1000f, IslandLifeSystem.ViewDistance, 1e-3f, "no report means no camera is close");
        }

        [Test]
        public void Shader_KeepsTheWindChannelsItReads()
        {
            var mat = IslandLifeSystem.VegetationMaterial;
            Assert.AreEqual("Drift/Vegetation", mat.shader.name);
            Assert.Greater(mat.GetFloat("_WindBend"), 0f, "the material still carries the bend the calm scales");
            Assert.GreaterOrEqual(mat.GetFloat("_WindFlutter"), 0f);
        }
    }
}
