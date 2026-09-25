using System.Collections.Generic;
using Drift.Bridge;
using Drift.Core;
using Drift.Islands;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // 2026-09-23 play test: "die Vögel zittern beim Beobachten" and the watch camera framing (herd under the buttons,
    // low camera, trees in the way).
    public class WatchFramingTests
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

        // ------------------------------------------------------------ framing maths

        [Test]
        public void Distance_KeepsSmallAnimalsReadableAndFramesBigHerds()
        {
            // Portrait phone: the width is the short side.
            float t = WatchFraming.ShortHalfTan(60f, 9f / 16f);
            Assert.AreEqual(Mathf.Tan(30f * Mathf.Deg2Rad) * 9f / 16f, t, 1e-5f);
            Assert.AreEqual(Mathf.Tan(30f * Mathf.Deg2Rad), WatchFraming.ShortHalfTan(60f, 16f / 9f), 1e-5f, "landscape: the height");

            // A hare (0.05 u) in a small herd: the readability limit brings the camera in.
            float hare = WatchFraming.Distance(0.6f, 0.05f, 60f, 9f / 16f, 0.55f, 44f);
            float pixels = 0.05f / (2f * hare * t) * 1080f;
            Assert.AreEqual(44f, pixels, 0.5f, "the hare is drawn at the minimum size");
            Assert.Less(hare, 0.6f / (0.55f * t));

            // A wide herd of big animals: fitted, and then the animal is larger than the minimum anyway.
            float big = WatchFraming.Distance(3f, 0.5f, 60f, 9f / 16f, 0.55f, 44f);
            Assert.AreEqual(3f / (0.55f * t), big, 1e-3f);
            Assert.Greater(0.5f / (2f * big * t) * 1080f, 44f);

            Assert.AreEqual(3f / (0.55f * t), WatchFraming.Distance(3f, 0f, 60f, 9f / 16f, 0.55f, 44f), 1e-3f, "no body, no limit");
        }

        [Test]
        public void Lift_PutsThePivotAtTheWantedScreenHeight()
        {
            Assert.AreEqual(0f, WatchFraming.LiftDegrees(60f, 0.5f), 1e-5f);
            Assert.AreEqual((0.64f + 0.05f) * 0.5f, WatchFraming.FreeBandCenter(0.36f, 0.05f), 1e-5f);
            Assert.AreEqual(0.5f, WatchFraming.FreeBandCenter(0f, 0f), 1e-5f);

            var go = new GameObject("FramingCam");
            _objects.Add(go);
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = 60f;
            cam.aspect = 9f / 16f;
            Vector3 pivot = new Vector3(3f, 0f, 5f);
            var dir = Quaternion.Euler(55f, 30f, 0f) * Vector3.forward;
            go.transform.position = pivot - dir * 4f;
            float wanted = WatchFraming.FreeBandCenter(0.36f, 0.05f);
            go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(-WatchFraming.LiftDegrees(60f, wanted), 0f, 0f);
            Vector3 vp = cam.WorldToViewportPoint(pivot);
            Assert.AreEqual(wanted, vp.y, 1e-3f, "below the buttons, in the middle of the free band");
            Assert.AreEqual(0.5f, vp.x, 1e-3f, "still centred sideways");
        }

        [Test]
        public void CandidateYaws_StartAtThePreferredOneAndAlternate()
        {
            Assert.AreEqual(10f, WatchFraming.CandidateYaw(10f, 0, 30f), 1e-5f);
            Assert.AreEqual(40f, WatchFraming.CandidateYaw(10f, 1, 30f), 1e-5f);
            Assert.AreEqual(-20f, WatchFraming.CandidateYaw(10f, 2, 30f), 1e-5f);
            Assert.AreEqual(70f, WatchFraming.CandidateYaw(10f, 3, 30f), 1e-5f);
            Assert.AreEqual(-50f, WatchFraming.CandidateYaw(10f, 4, 30f), 1e-5f);
            // Twelve candidates cover the circle once.
            var seen = new HashSet<int>();
            for (int i = 0; i < 12; i++) seen.Add(Mathf.RoundToInt(Mathf.Repeat(WatchFraming.CandidateYaw(0f, i, 30f), 360f)) % 360);
            Assert.AreEqual(12, seen.Count);
        }

        [Test]
        public void Occlusion_CountsOnlyTreesInTheSightLine()
        {
            // Yaw 0: the camera looks north (+z), so it stands south of the subject.
            Vector3 subject = Vector3.zero;
            float cost(Vector3 tree) => WatchFraming.Occlusion(0f, 30f, 6f, subject, 0.5f, new[] { tree }, 1);
            Assert.Greater(cost(new Vector3(0f, 1.2f, -1f)), 0.5f, "a tall tree between camera and herd");
            Assert.AreEqual(0f, cost(new Vector3(0f, 1.2f, 1f)), "behind the herd");
            Assert.AreEqual(0f, cost(new Vector3(2f, 1.2f, -1f)), "beside the sight line");
            Assert.AreEqual(0f, cost(new Vector3(0f, 0.3f, -1f)), "a low bush under the sight line");
            Assert.AreEqual(0f, cost(new Vector3(0f, 1.2f, -9f)), "behind the camera");
            // The steeper the camera looks down, the less the same tree matters.
            float low = WatchFraming.Occlusion(0f, 25f, 4f, subject, 0.5f, new[] { new Vector3(0f, 0.8f, -1f) }, 1);
            float steep = WatchFraming.Occlusion(0f, 60f, 4f, subject, 0.5f, new[] { new Vector3(0f, 0.8f, -1f) }, 1);
            Assert.Greater(low, 0f);
            Assert.AreEqual(0f, steep);
        }

        [Test]
        public void PickYaw_TurnsAwayFromAWallOfTreesButOnlyWhenItHelps()
        {
            Vector3 subject = Vector3.zero;
            Assert.AreEqual(0f, WatchFraming.PickYaw(0f, 40f, 5f, subject, 0.6f, new Vector3[0], 0, 12, 1.5f), 1e-5f, "nothing in the way");

            // A row of trees south of the herd, where the camera would stand.
            var trees = new List<Vector3>();
            for (float x = -1.5f; x <= 1.5f; x += 0.5f) trees.Add(new Vector3(x, 1.5f, -1f));
            float yaw = WatchFraming.PickYaw(0f, 40f, 5f, subject, 0.6f, trees.ToArray(), trees.Count, 12, 1.5f);
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(0f, yaw)), 45f, "the camera went round the trees");
            Assert.AreEqual(0f, WatchFraming.Occlusion(yaw, 40f, 5f, subject, 0.6f, trees.ToArray(), trees.Count), 1e-5f);

            // Hills count through extraCost: the preferred side blocked by the ground alone also turns.
            var hills = new float[12];
            hills[0] = 3f;
            float byHill = WatchFraming.PickYaw(0f, 40f, 5f, subject, 0.6f, new Vector3[0], 0, 12, 1.5f, hills);
            Assert.AreEqual(30f, Mathf.Abs(Mathf.DeltaAngle(0f, byHill)), 1e-3f, "the smallest turn that clears it");
        }

        // ------------------------------------------------------------ smooth birds close to the camera

        Island MakePlayer(Vector2 pos)
        {
            var go = new GameObject("FramingTestPlayer");
            go.SetActive(false);
            go.transform.position = new Vector3(pos.x, 0f, pos.y);
            var isl = go.AddComponent<Island>();
            isl.useKeyboardInput = true;
            isl.landRadius = 4f;
            isl.shapeSeed = 12349;
            go.SetActive(true);
            _objects.Add(go);
            return isl;
        }

        FlockSystem MakeFlocks(Island player, Transform viewer)
        {
            var go = new GameObject("FramingTestFlocks");
            go.SetActive(false);
            var flocks = go.AddComponent<FlockSystem>();
            flocks.player = player;
            flocks.spawnMin = 40f;
            flocks.spawnMax = 60f;
            flocks.viewer = viewer;
            go.SetActive(true);
            _objects.Add(go);
            return flocks;
        }

        static Mesh MeshOf(FlockSystem flocks, string name)
        {
            var t = flocks.transform.Find(name);
            return t != null ? t.GetComponent<MeshFilter>().sharedMesh : null;
        }

        [Test]
        public void NearFlock_IsRedrawnEveryFrameAndMovesSmoothly()
        {
            var player = MakePlayer(new Vector2(-30000f, 30000f));
            var viewerGo = new GameObject("FramingTestViewer");
            _objects.Add(viewerGo);
            var flocks = MakeFlocks(player, viewerGo.transform);
            viewerGo.transform.position = new Vector3(0f, 10000f, 0f);
            flocks.Step(0.05f);
            Assert.AreEqual(0, flocks.NearFlockCount, "nothing near a camera far away");

            int f = -1;
            for (int i = 0; i < flocks.FlockCount && f < 0; i++) if (!flocks.IsSeabird(i)) f = i;
            Assert.GreaterOrEqual(f, 0);
            Vector2 fp = flocks.PositionOf(f);
            viewerGo.transform.position = new Vector3(fp.x, flocks.HeightOf(f) + 3f, fp.y - 3f);
            flocks.Step(1f / 60f);
            flocks.FlushNear();
            Assert.IsTrue(flocks.IsNear(f));
            Assert.GreaterOrEqual(flocks.NearFlockCount, 1);

            var near = MeshOf(flocks, "FlocksNear");
            Assert.IsNotNull(near);
            int farBuilds = flocks.MeshBuilds, nearBuilds = flocks.NearMeshBuilds;
            int moving = 0;
            float biggest = 0f;
            Vector3 prev = near.vertices[0];
            for (int frame = 0; frame < 60; frame++)
            {
                // Keep the camera with the flock, as the watch camera would.
                fp = flocks.PositionOf(f);
                viewerGo.transform.position = new Vector3(fp.x, flocks.HeightOf(f) + 3f, fp.y - 3f);
                flocks.Step(1f / 60f);
                flocks.FlushNear();
                Vector3 v = near.vertices[0];
                float step = (v - prev).magnitude;
                if (step > 1e-5f) moving++;
                biggest = Mathf.Max(biggest, step);
                prev = v;
            }
            Assert.AreEqual(60, flocks.NearMeshBuilds - nearBuilds, "one near rebuild per frame");
            Assert.AreEqual(60, moving, "the bird moves in every frame, not every fourth");
            Assert.Less(biggest, 0.3f, "no jumps like the old 15-Hz steps (~0.5 u at cruise speed)");
            Assert.LessOrEqual(flocks.MeshBuilds - farBuilds, 60 / 4 + 2, "the far mesh keeps its own rate");
            Assert.AreEqual(0, near.colors[0].a, 1e-6f, "the near birds carry no shader flap weight");

            // Walking away empties the near mesh and hands the flock back.
            viewerGo.transform.position = new Vector3(0f, 10000f, 0f);
            flocks.Step(1f / 60f);
            flocks.FlushNear();
            Assert.AreEqual(0, flocks.NearFlockCount);
            Assert.AreEqual(0, near.vertexCount);
        }

        [Test]
        public void FlapFrames_MoveOnlyTheWingsAndBeatEvenly()
        {
            var body = Markings.BirdTemplate(LifeKind.Bird, 1);
            var up = FlockSystem.FlapFrame(1, 6, 0.13f);   // a quarter of the beat: wings fully up
            var down = FlockSystem.FlapFrame(1, 18, 0.13f);
            Assert.AreSame(up, FlockSystem.FlapFrame(1, 6, 0.13f), "cached");
            float maxLift = 0f;
            for (int i = 0; i < body.vertices.Length; i++)
            {
                float dy = up.vertices[i].y - body.vertices[i].y;
                if (body.wing[i] <= 0f) Assert.AreEqual(0f, dy, 1e-6f, "the body stays rigid");
                Assert.AreEqual(-dy, down.vertices[i].y - body.vertices[i].y, 1e-5f);
                maxLift = Mathf.Max(maxLift, dy);
            }
            Assert.Greater(maxLift, 0.05f, "the wing tips beat");
            Assert.LessOrEqual(maxLift, 0.13f + 1e-4f);

            // One beat per 1/perSecond seconds whatever the bird's position; every frame of the beat gets its turn.
            Assert.AreEqual(FlockSystem.FlapFrameAt(1.3f, 0f, 2.5f), FlockSystem.FlapFrameAt(1.3f, 0.4f, 2.5f));
            var frames = new HashSet<int>();
            for (int i = 0; i < 240; i++) frames.Add(FlockSystem.FlapFrameAt(0.7f, i / 600f, 2.5f));
            Assert.AreEqual(24, frames.Count);
        }

        // ------------------------------------------------------------ a flock in the air (2026-09-25)

        [Test]
        public void AirFraming_PitchFollowsTheHeightAndDistanceTheSpread()
        {
            Assert.AreEqual(50f, WatchFraming.AirPitch(0f, 50f, 30f, 4f), 1e-4f, "landed: from above");
            Assert.AreEqual(30f, WatchFraming.AirPitch(9f, 50f, 30f, 4f), 1e-4f, "high up: from the side");
            float prev = 90f;
            for (float h = 0f; h <= 5f; h += 0.25f)
            {
                float p = WatchFraming.AirPitch(h, 50f, 30f, 4f);
                Assert.LessOrEqual(p, prev + 1e-4f, "flatter the higher the birds fly");
                prev = p;
            }
            float tight = WatchFraming.AirDistance(0.5f, 60f, 9f / 16f, 0.6f, 4f);
            float wide = WatchFraming.AirDistance(4f, 60f, 9f / 16f, 0.6f, 4f);
            Assert.AreEqual(4f, tight, 1e-4f, "never closer than the minimum");
            Assert.Greater(wide, 10f, "a spread-out flock is framed from farther away");
            Assert.AreEqual(4f / (0.6f * WatchFraming.ShortHalfTan(60f, 9f / 16f)), wide, 1e-3f);
        }

        [Test]
        public void ShiftFraming_KeepsThePlayersOwnZoomAndTilt()
        {
            var rig = new PhotoRig { minDistance = 1f, maxDistance = 100f };
            rig.SetHome(0f, 40f, 10f);
            rig.GoHome();
            rig.Zoom(0.5f);            // the player zoomed in to half
            rig.Orbit(0f, 50f);        // and tilted 10 degrees steeper
            rig.ShiftFraming(2f, -5f); // the flock spread to twice its size and climbed
            Assert.AreEqual(10f, rig.distanceTarget, 1e-4f, "half of the new automatic distance");
            Assert.AreEqual(45f, rig.pitchTarget, 1e-4f, "10 degrees above the new automatic pitch");
            Assert.AreEqual(20f, rig.homeDistance, 1e-4f, "R returns to the new automatic framing");
            Assert.AreEqual(35f, rig.homePitch, 1e-4f);
        }
    }
}
