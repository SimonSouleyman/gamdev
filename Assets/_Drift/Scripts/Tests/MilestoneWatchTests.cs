using System.Collections.Generic;
using Drift.Bridge;
using Drift.Core;
using Drift.Life;
using Drift.SaveSystem;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Owner, 2026-09-24: tapping a milestone toast watches the lighthouse / harbour like an animal, every notice stays
    // 3 s longer, and tappable things sparkle now and then.
    public class MilestoneWatchTests
    {
        readonly List<GameObject> _objects = new();

        [SetUp]
        public void SetUp()
        {
            GameModes.Set(GameMode.Cozy);
            LifeLod.DistanceProvider = _ => 0f;
            // Summer: the v0.6.8 seasons layer (courtship in spring, huddles in winter) stays out of these checks.
            LifeEnvironment.SeasonProvider = () => 0.375f;
            LifeEnvironment.NightProvider = () => 0f;
        }

        [TearDown]
        public void TearDown()
        {
            LifeLod.DistanceProvider = null;
            LifeEnvironment.NightProvider = null;
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        IslandSettlementSystem MakeSettlement(float radius, int seed, bool mature)
        {
            var go = new GameObject("WatchLandmarkIsle");
            go.SetActive(false);
            go.transform.position = new Vector3(30f, 0f, -12f);
            var surface = go.AddComponent<FakeIslandSurface>();
            surface.radius = radius;
            surface.beach = 2f;
            var life = go.AddComponent<IslandLifeSystem>();
            life.seed = seed;
            go.SetActive(true);
            _objects.Add(go);
            if (mature) life.Simulate(900f, 10f);
            go.SetActive(false);
            var s = go.AddComponent<IslandSettlementSystem>();
            s.seed = seed;
            s.prehistory = false;
            go.SetActive(true);
            return s;
        }

        static void Run(IslandSettlementSystem s, float seconds, float dt = 0.25f)
        {
            int n = Mathf.CeilToInt(seconds / dt);
            for (int i = 0; i < n; i++) s.Step(dt);
        }

        // ------------------------------------------------------------ watching a landmark

        [Test]
        public void ALighthouseIsAStillSubjectFramedAtItsFoot()
        {
            var s = MakeSettlement(3f, 41, false);
            Assert.IsNull(WatchSubjects.OfLandmark(s, BuildingKind.Lighthouse), "nothing to watch before it is built");
            Assert.Less(WatchSubjects.LandmarkIndex(s, BuildingKind.Lighthouse), 0);

            Assert.IsTrue(s.TryBuildLandmark(BuildingKind.Lighthouse));
            var subject = WatchSubjects.OfLandmark(s, BuildingKind.Lighthouse);
            Assert.IsNotNull(subject, "a site can already be watched going up");
            Assert.IsTrue(subject.still);
            Assert.AreEqual("Leuchtturm", subject.label);
            Assert.Greater(subject.lift, 0f, "aimed part way up the tower");
            Assert.GreaterOrEqual(subject.radius, 0.6f);
            Assert.IsTrue(subject.gone.Contains("Leuchtturm"));

            Run(s, 300f);
            Assert.IsTrue(subject.focus(out Vector3 f));
            int i = WatchSubjects.LandmarkIndex(s, BuildingKind.Lighthouse);
            Assert.AreEqual(BuildingState.Done, s.BuildingStateOf(i), "the finished tower is preferred");
            Vector2 local = s.BuildingPositionOf(i);
            Vector3 foot = s.transform.TransformPoint(local.x, s.GetComponent<FakeIslandSurface>().SampleHeight(local), local.y);
            Assert.Less((f - foot).magnitude, 0.01f, "the focus is the tower's foot in world space");
        }

        [Test]
        public void TheHarbourIsFramedOnItsJettyAndTheTargetFollowsIt()
        {
            var s = MakeSettlement(8f, 47, true);
            Run(s, 200f);
            Assert.IsTrue(s.TryBuildLandmark(BuildingKind.Dock));
            Run(s, 300f);
            var subject = WatchSubjects.OfLandmark(s, BuildingKind.Dock);
            Assert.IsNotNull(subject);
            Assert.AreEqual("Hafen", subject.label);
            Assert.IsTrue(subject.focus(out Vector3 f));
            Assert.IsTrue(s.TryGetDockWorld(out Vector3 end, out _));
            Assert.Less(new Vector2(f.x - end.x, f.z - end.z).magnitude, s.BuildingHeightOf(WatchSubjects.LandmarkIndex(s, BuildingKind.Dock)) * 4f + 1f,
                "between the jetty's root and its end");

            var target = new TapTarget { kind = TapTargetKind.Landmark, system = s, a = 0, b = (int)BuildingKind.Dock };
            Assert.IsTrue(TapTargets.Refresh(ref target), "a tap target finds its landmark by kind");
            Assert.AreEqual(WatchSubjects.LandmarkIndex(s, BuildingKind.Dock), target.a);
            Assert.Greater(target.world.y, f.y - 0.01f, "measured at half height");
            var watched = TapTargets.SubjectOf(target);
            Assert.IsNotNull(watched);
            Assert.AreEqual("Hafen", watched.label);
        }

        [Test]
        public void TheFestivalGroundNeedsAVillage()
        {
            var bare = MakeSettlement(3f, 41, false);
            Assert.IsNull(WatchSubjects.OfFestival(bare), "no village, no festival ground");
            bare.TryBuildLandmark(BuildingKind.Lighthouse);
            Assert.IsNull(WatchSubjects.OfFestival(bare), "a lighthouse village is not where the folk dance");

            var s = MakeSettlement(8f, 47, true);
            Run(s, 200f);
            Assume.That(s.VillageCount > 0);
            var subject = WatchSubjects.OfFestival(s);
            Assert.IsNotNull(subject);
            Assert.IsTrue(subject.still);
            Assert.IsTrue(subject.focus(out _));
        }

        [Test]
        public void AMilestoneWithoutAnythingToSeeOffersNoSubject()
        {
            Assert.IsNull(WatchSubjects.OfMilestone(Milestone.Lighthouse, null, null));
            Assert.IsFalse(WatchSubjects.MilestoneHasSubject(Milestone.Seabirds, null, null), "no flocks, no seabirds");
            Assert.IsNull(WatchSubjects.OfMilestone(Milestone.Seabirds, null, null));
            Assert.IsNull(WatchSubjects.OfMilestone(Milestone.Festival, null, null));
        }

        // ------------------------------------------------------------ the toast queue

        [Test]
        public void MilestoneToastsQueueInsteadOfReplacingEachOther()
        {
            var q = new MilestoneToastQueue { showSeconds = 8.5f, gapSeconds = 0.5f };
            q.Push(Milestone.Harbour);
            q.Push(Milestone.Seabirds);
            q.Push(Milestone.Harbour);
            Assert.AreEqual(2, q.Pending, "a milestone is queued once");
            Assert.IsTrue(q.Tick(0f));
            Assert.AreEqual((int)Milestone.Harbour, q.Current);
            Assert.IsFalse(q.Tick(8f), "still up after 8 s");
            Assert.IsTrue(q.Tick(0.6f), "gone after 8.5 s");
            Assert.IsFalse(q.Showing);
            Assert.IsFalse(q.Tick(0.3f), "a short gap");
            Assert.IsTrue(q.Tick(0.3f));
            Assert.AreEqual((int)Milestone.Seabirds, q.Current, "then the next one");

            q.Dismiss();
            Assert.IsFalse(q.Showing, "a tap takes it away at once");
            Assert.AreEqual(0, q.Pending);
            q.Clear();
            Assert.IsFalse(q.Tick(1f));
        }

        [Test]
        public void EveryNoticeStaysThreeSecondsLonger()
        {
            var go = new GameObject("NoticeDefaults");
            go.SetActive(false);
            _objects.Add(go);
            var watch = go.AddComponent<WatchTools>();
            var milestones = go.AddComponent<MilestoneToasts>();
            Assert.AreEqual(4f + 3f, watch.popupShowSeconds, 1e-4f, "animal card");
            Assert.AreEqual(4f + 3f, watch.newsStrongSeconds, 1e-4f, "Neu auf deiner Insel");
            Assert.AreEqual(2.6f + 3f, watch.newsSubtleSeconds, 1e-4f, "Zum ersten Mal gesehen");
            Assert.AreEqual(2.6f + 3f, watch.noticeSeconds, 1e-4f, "… ist gerade nicht in der Nähe");
            Assert.AreEqual(4f + 3f, watch.photoHintSeconds, 1e-4f, "photo task hint");
            Assert.AreEqual(5.5f + 3f, milestones.toastShowSeconds, 1e-4f, "milestone");
        }

        // ------------------------------------------------------------ still framing

        [Test]
        public void AStillSubjectIsCircledOnlyWhileTheCameraIsLeftAlone()
        {
            Assert.AreEqual(0f, WatchFraming.StillOrbitRate(0f, 1.5f, 1.5f, 5f));
            Assert.AreEqual(0f, WatchFraming.StillOrbitRate(1.4f, 1.5f, 1.5f, 5f));
            float mid = WatchFraming.StillOrbitRate(2.25f, 1.5f, 1.5f, 5f);
            Assert.Greater(mid, 0f);
            Assert.Less(mid, 5f, "eases in");
            Assert.AreEqual(5f, WatchFraming.StillOrbitRate(10f, 1.5f, 1.5f, 5f), 1e-4f);

            var rig = new PhotoRig();
            rig.FromPose(new Vector3(0f, 5f, -5f), Vector3.zero);
            Vector3 p0 = rig.Pivot(Vector3.zero);
            rig.pivotLift = 0.3f;
            Assert.AreEqual(p0.y + 0.3f, rig.Pivot(Vector3.zero).y, 1e-5f, "the pivot is raised, not moved sideways");
            rig.Pose(Vector3.zero, out Vector3 pos, out _);
            Assert.AreEqual(rig.distance, (pos - rig.Pivot(Vector3.zero)).magnitude, 1e-3f);
        }

        // ------------------------------------------------------------ sparkle timing

        [Test]
        public void SparklesKeepTheirRhythmAndLetEachSubjectRest()
        {
            var s = new SparkleSchedule(7) { interval = 3.5f, jitter = 0.35f, cooldown = 12f, retry = 1f };
            s.Reset(7);
            Assert.LessOrEqual(s.Due, 3.5f * 1.35f * 0.5f + 1e-4f, "the first one comes early");
            float t = 0f;
            while (!s.Tick(0.05f)) t += 0.05f;
            s.Started(42);
            Assert.IsTrue(s.Cooling(42));
            Assert.IsFalse(s.Cooling(43));
            float gap = s.Due - s.Clock;
            Assert.GreaterOrEqual(gap, 3.5f * 0.65f - 1e-3f);
            Assert.LessOrEqual(gap, 3.5f * 1.35f + 1e-3f);

            for (int i = 0; i < 230; i++) s.Tick(0.05f);
            Assert.IsTrue(s.Cooling(42), "still resting after 11.5 s");
            for (int i = 0; i < 20; i++) s.Tick(0.05f);
            Assert.IsFalse(s.Cooling(42), "free again after 12 s");

            s.Missed();
            Assert.AreEqual(1f, s.Due - s.Clock, 1e-4f, "nothing in view: look again soon");
        }

        [Test]
        public void AStarSwellsAndFades()
        {
            Assert.AreEqual(0f, TapSparkles.StarEnvelope(0f, 0.1f, 0.5f));
            Assert.AreEqual(0f, TapSparkles.StarEnvelope(0.6f, 0.1f, 0.5f), "over at start + length");
            Assert.AreEqual(1f, TapSparkles.StarEnvelope(0.1f + 0.15f, 0.1f, 0.5f), 1e-4f, "full at 30 %");
            Assert.Greater(TapSparkles.StarEnvelope(0.1f + 0.05f, 0.1f, 0.5f), 0f);
            Assert.Less(TapSparkles.StarEnvelope(0.1f + 0.45f, 0.1f, 0.5f), 0.2f);
            var sprite = TapSparkles.StarSprite;
            Assert.IsNotNull(sprite);
            Assert.AreSame(sprite, TapSparkles.StarSprite, "drawn once");
            var px = sprite.texture.GetPixel(64, 64);
            Assert.Greater(px.a, 0.95f, "a bright core");
            Assert.Less(sprite.texture.GetPixel(4, 4).a, 0.05f, "clear corners");
        }
    }
}
