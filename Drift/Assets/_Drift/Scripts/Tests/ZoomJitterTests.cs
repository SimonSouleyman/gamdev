using System.Collections.Generic;
using Drift.Bridge;
using Drift.Core;
using Drift.Islands;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // 2026-09-25, owner: "Prüfe bei allen Tieren/Pflanzen, ob die mit rangezoomter Kamera zittern" and "Wenn man Vögel
    // beobachtet, ist die Kamera nicht immer auf der richtigen Höhe". Meshes baked at 15 Hz slide between two bakes
    // (CloseUpMotion + DriftMotion.hlsl) or are rebuilt as often as the pixel budget asks; a watched flock is framed
    // at its own height and size.
    public class ZoomJitterTests
    {
        readonly List<GameObject> _objects = new();

        [SetUp]
        public void SetUp()
        {
            LifeLod.DistanceProvider = _ => 0f;
            LifeEnvironment.NightProvider = () => 0f;
        }

        [TearDown]
        public void TearDown()
        {
            LifeLod.DistanceProvider = null;
            LifeEnvironment.NightProvider = null;
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        // ------------------------------------------------------------ the motion channel's clock

        [Test]
        public void MotionCode_RoundTripsWrapsAndIgnoresMeshesWithoutTheChannel()
        {
            float w = CloseUpMotion.Encode(10f, 0.1f);
            Assert.AreEqual(0f, CloseUpMotion.Progress(w, 10f), 1e-3f, "starts where it was drawn");
            // The round trip through w loses a little of the bake time: a fresh bake never reads as finished.
            for (float c = 100f; c < 127f; c += 0.3137f)
                Assert.Less(CloseUpMotion.Progress(CloseUpMotion.Encode(c, 1f / 12f), c), 0.01f, "fresh bake at " + c);
            Assert.AreEqual(0.5f, CloseUpMotion.Progress(w, 10.05f), 0.01f, "half way after half the duration");
            Assert.AreEqual(1f, CloseUpMotion.Progress(w, 10.2f), 1e-5f, "then stands at the bake");

            // A bake just before the clock wraps keeps sliding after it.
            float late = CloseUpMotion.Encode(CloseUpMotion.Period - 0.05f, 0.1f);
            Assert.AreEqual(0.5f, CloseUpMotion.Progress(late, 0f), 0.02f);

            // No channel reads 0 (or 1 on GLES): never a slide from the origin.
            Assert.AreEqual(1f, CloseUpMotion.Progress(0f, 3f));
            Assert.AreEqual(1f, CloseUpMotion.Progress(1f, 0.0001f));
            Assert.Greater(CloseUpMotion.Encode(0f, 0.001f), CloseUpMotion.CodeScale, "the shortest slide is still a valid code");

            // Precision: the largest code keeps sub-millisecond resolution in a float.
            float big = CloseUpMotion.Encode(CloseUpMotion.Period - 0.001f, 10f);
            float ulp = big * 1.2e-7f;
            Assert.Less(ulp, 0.001f);
            Assert.AreEqual(CloseUpMotion.Period - 0.001f, CloseUpMotion.Wrap(CloseUpMotion.Period * 1000.0 - 0.001), 1e-3f);
        }

        [Test]
        public void Interval_EveryFrameCloseUpTheSlowRateFarAway()
        {
            // Watch camera 3 u from a hopping hare on a 1920-px screen: far more than the budget at 15 Hz.
            float ppu = 1920f / (2f * Mathf.Tan(30f * Mathf.Deg2Rad)) / 3f;
            float close = CloseUpMotion.Interval(ppu, 1.3f, 1f / 15f, 0.6f);
            Assert.Less(close, 1f / 60f, "close up that is every frame");
            float far = CloseUpMotion.Interval(ppu / 100f, 0.05f, 1f / 15f, 0.6f);
            Assert.AreEqual(1f / 15f, far, 1e-6f, "a slow mover far away keeps the slow rate");
            Assert.AreEqual(1f / 15f, CloseUpMotion.Interval(0f, 1f, 1f / 15f, 0.6f), "no camera: unchanged");
            float mid = CloseUpMotion.Interval(ppu / 10f, 0.3f, 1f / 15f, 0.6f);
            Assert.AreEqual(0.6f, 0.3f * mid * ppu / 10f, 1e-3f, "in between: exactly the budget per rebuild");
        }

        // ------------------------------------------------------------ slides are continuous

        // A hare hopping on at 1.3 u/s along a curve with bakes every `gaps` frames at 60 fps; the drawn path's largest
        // deviation from its neighbours' middle after `warm` frames, and its largest single-frame step.
        static void SlidePath(int[] gaps, int frames, int warm, out float worst, out float biggestStep, out MotionTrack track, out double end)
        {
            track = new MotionTrack();
            double t = 0;
            const float dt = 1f / 60f;
            int next = 0, gap = 0;
            var shown = new List<Vector3>();
            for (int frame = 0; frame < frames; frame++)
            {
                if (frame == next)
                {
                    var target = new BakeTransform(new Vector3((float)t * 1.3f, 0f, 0.2f * (float)(t * t)), 10f * (float)t, 1f);
                    track.Next(target, t, 1.25f / 15f, 0.75f, out _);
                    next += gaps[gap++ % gaps.Length];
                }
                shown.Add(track.Shown(t).pos);
                t += dt;
            }
            end = t;
            worst = biggestStep = 0f;
            for (int i = 1; i < shown.Count - 1; i++)
            {
                biggestStep = Mathf.Max(biggestStep, (shown[i] - shown[i - 1]).magnitude);
                if (i < warm) continue;
                Vector3 mid = (shown[i - 1] + shown[i + 1]) * 0.5f;
                worst = Mathf.Max(worst, (shown[i] - mid).magnitude);
            }
        }

        [Test]
        public void MotionTrack_ShowsASmoothPathAcrossIrregularBakes()
        {
            // The herd mesh's own cadence at 60 fps: every 4th or 5th frame. 15-Hz stepping had a residual of half a
            // step, 1.3 / 15 / 2 = 0.043 u; the slide leaves only the path's own curvature.
            SlidePath(new[] { 4, 5, 4, 4, 5, 4 }, 120, 30, out float worst, out float steps, out _, out _);
            Assert.Less(worst, 0.002f, "no steps left in the path");
            Assert.Less(steps, 1.3f * 1.5f / 60f, "never much faster than the animal itself");

            // Early and late bakes (a hitch): the path may pause for a frame but never jumps.
            SlidePath(new[] { 4, 5, 4, 4, 2, 5, 4, 7, 4, 4, 5, 4 }, 120, 30, out worst, out steps, out var track, out double t);
            Assert.Less(steps, 1.3f * 2f / 60f, "no jump after an early or a late bake");
            Assert.Less(worst, 0.043f * 0.5f);

            // A teleport (scrambled ashore) is shown as a jump, not a zip across the island.
            bool slides = track.Next(new BakeTransform(new Vector3(50f, 0f, 0f), 0f, 1f), t, 0.08f, 0.75f, out var start);
            Assert.IsFalse(slides);
            Assert.AreEqual(50f, start.pos.x, 1e-5f);
            // Standing still: nothing to slide, the mesh writes no motion code.
            Assert.IsFalse(track.Next(new BakeTransform(new Vector3(50f, 0f, 0f), 0f, 1f), t + 1.0, 0.08f, 0.75f, out _));
        }

        [Test]
        public void TemplateBatch_MotionChannelHoldsThePreviousPoseOnlyWhereAsked()
        {
            var tpl = LifeMeshes.GetTemplate(LifeKind.Sheep, 0);
            var batch = new TemplateBatch();
            batch.Begin();
            batch.UseMotion();
            batch.Add(tpl, new Vector3(5f, 0f, 0f), 0f, 1f);
            int first = batch.VertexCount;
            batch.AddAnimal(tpl, new Vector3(1f, 0.5f, 2f), 90f, 0.2f, new AnimalPose());
            batch.SetMotion(tpl, new Vector3(0.9f, 0.5f, 2f), 80f, 0.2f, 0f, 0f, CloseUpMotion.Encode(3f, 0.08f));
            var mesh = new Mesh();
            batch.Apply(mesh);
            var uv4 = new List<Vector4>();
            mesh.GetUVs(4, uv4);
            Assert.AreEqual(mesh.vertexCount, uv4.Count, "the channel covers every vertex");
            for (int i = 0; i < first; i++) Assert.AreEqual(0f, uv4[i].w, "a plain Add stands still");
            // The previous pose is the same template at the old transform.
            var check = new TemplateBatch();
            check.Begin();
            check.Add(tpl, new Vector3(0.9f, 0.5f, 2f), 80f, 0.2f);
            var old = new Mesh();
            check.Apply(old);
            var oldV = old.vertices;
            for (int i = 0; i < oldV.Length; i++)
            {
                Vector4 p = uv4[first + i];
                Assert.AreEqual(oldV[i].x, p.x, 1e-4f);
                Assert.AreEqual(oldV[i].y, p.y, 1e-4f);
                Assert.AreEqual(oldV[i].z, p.z, 1e-4f);
                Assert.Greater(p.w, CloseUpMotion.CodeScale);
            }

            // A batch without UseMotion uploads no fifth channel (vegetation, near flocks, edit mode).
            batch.Begin();
            batch.Add(tpl, Vector3.zero, 0f, 1f);
            batch.Apply(mesh);
            mesh.GetUVs(4, uv4);
            Assert.AreEqual(0, uv4.Count);
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(old);
        }

        [Test]
        public void SetPhase_GivesEachFarBirdItsOwnBeat()
        {
            var tpl = Markings.BirdTemplate(LifeKind.Bird, 1);
            var batch = new TemplateBatch();
            batch.Begin();
            batch.Add(tpl, Vector3.zero, 0f, 1f, 0f, 0f, true);
            batch.SetPhase(tpl, 1.5f);
            batch.Add(tpl, Vector3.one, 0f, 1f, 0f, 0f, true);
            batch.SetPhase(tpl, 4f);
            var mesh = new Mesh();
            batch.Apply(mesh);
            var uv0 = new List<Vector4>();
            mesh.GetUVs(0, uv0);
            int n = tpl.vertices.Length;
            Assert.AreEqual(1.5f, uv0[0].y, 1e-6f);
            Assert.AreEqual(4f, uv0[n].y, 1e-6f);
            Assert.AreEqual(0f, uv0[n].x, "no wind bend on a bird");

            // A later bake without phases (only seabirds far away) must not keep the old channel.
            batch.Begin();
            batch.Add(tpl, Vector3.zero, 0f, 1f);
            batch.Apply(mesh);
            Assert.IsFalse(mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0));
            Object.DestroyImmediate(mesh);
        }

        // ------------------------------------------------------------ flocks

        Island MakePlayer(Vector2 pos)
        {
            var go = new GameObject("JitterTestPlayer");
            go.SetActive(false);
            go.transform.position = new Vector3(pos.x, 0f, pos.y);
            var isl = go.AddComponent<Island>();
            isl.useKeyboardInput = true;
            isl.landRadius = 6f;
            isl.shapeSeed = 12349;
            go.SetActive(true);
            _objects.Add(go);
            return isl;
        }

        FlockSystem MakeFlocks(Island player)
        {
            var go = new GameObject("JitterTestFlocks");
            go.SetActive(false);
            var flocks = go.AddComponent<FlockSystem>();
            flocks.player = player;
            flocks.spawnMin = 40f;
            flocks.spawnMax = 60f;
            go.SetActive(true);
            _objects.Add(go);
            return flocks;
        }

        [Test]
        public void FlockView_IsTheMiddleOfTheDrawnBirds()
        {
            var player = MakePlayer(new Vector2(-31000f, 29000f));
            var flocks = MakeFlocks(player);
            for (int i = 0; i < 40; i++) flocks.Step(0.05f);
            for (int f = 0; f < flocks.FlockCount; f++)
            {
                Assert.IsTrue(flocks.TryGetView(f, out Vector3 c, out float spread));
                Vector3 sum = Vector3.zero;
                int n = flocks.BirdCountOf(f);
                for (int b = 0; b < n; b++) sum += flocks.BirdPosition(f, b);
                Assert.Less((sum / n - c).magnitude, 1e-3f, "flock " + f);
                for (int b = 0; b < n; b++) Assert.LessOrEqual((flocks.BirdPosition(f, b) - c).magnitude, spread + 1e-4f);
                Assert.Greater(spread, 0f);
                // In the air the middle is up where the birds are, not on the sea under them.
                Assert.Greater(c.y, 1f, "flock " + f + " flies at its height");
            }
        }

        [Test]
        public void WatchedFlock_IsAnAirSubjectFramedOnItsBirds()
        {
            var player = MakePlayer(new Vector2(-33000f, 27000f));
            var flocks = MakeFlocks(player);
            for (int i = 0; i < 40; i++) flocks.Step(0.05f);
            int f = -1;
            for (int i = 0; i < flocks.FlockCount && f < 0; i++) if (!flocks.IsSeabird(i)) f = i;
            var subject = WatchSubjects.OfFlock(flocks, f);
            Assert.IsNotNull(subject);
            Assert.IsTrue(subject.air);
            Assert.IsNull(subject.ground);
            Assert.IsTrue(subject.focus(out Vector3 focus));
            flocks.TryGetView(f, out Vector3 middle, out float spread);
            Assert.Less((focus - middle).magnitude, 1e-3f, "the first look is at the birds' middle");
            Assert.AreEqual(spread + 0.8f, subject.radius, 0.05f, "the framing radius follows the flock's size");
        }
    }
}
