using System.Collections.Generic;
using Drift.Bridge;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Owner, 2026-09-24 (cozy): no tilt in the Pangäa fly-over, the watch camera turns with the island, campfires
    // can be tapped to watch their settlement, notices 50 % larger.
    public class CozyUxTests
    {
        readonly List<GameObject> _objects = new();

        [SetUp]
        public void SetUp()
        {
            GameModes.Set(GameMode.Cozy);
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

        // ------------------------------------------------------------ fly-over input

        [Test]
        public void TheFlyOverIgnoresTheTiltAndHasNoStick()
        {
            Assert.IsTrue(SessionScreens.TiltSteers(true, false), "the tilt steers the island as before");
            Assert.IsFalse(SessionScreens.TiltSteers(true, true), "never over the finished Pangäa");
            Assert.IsFalse(SessionScreens.TiltSteers(false, false));

            Assert.IsFalse(SessionScreens.StickShown(true, false), "tilt steering hides the stick during a run");
            Assert.IsFalse(SessionScreens.StickShown(true, true), "the fly-over is driven like a map: no stick");
            Assert.IsFalse(SessionScreens.StickShown(false, true));
            Assert.IsTrue(SessionScreens.StickShown(false, false));

            StringAssert.DoesNotContain("Kippen", PangaeaFinale.TiltBannerBody);
            StringAssert.StartsWith("Ziehen verschiebt", PangaeaFinale.BannerBodyFor(true, true), "a tilt player gets the map gestures too");
        }

        [Test]
        public void TheFlyOverReadsTheStickNotTheTilt()
        {
            var go = new GameObject("FlyInput");
            _objects.Add(go);
            var screens = go.AddComponent<SessionScreens>();
            SessionScreens.ScreenOverride = () => new Vector2(0f, 2f);
            try
            {
                Assert.AreEqual(1f, screens.ReadFlyScreen().magnitude, 1e-4f, "clamped like the stick");
            }
            finally { SessionScreens.ScreenOverride = null; }
            Assert.AreEqual(Vector2.zero, screens.ReadFlyScreen(), "nothing held, no tilt in between");
        }

        // ------------------------------------------------------------ watch camera turns with the island

        [Test]
        public void TheOrbitTurnsWithTheGroundAndKeepsTheFraming()
        {
            var rig = new PhotoRig();
            rig.SetLimits(3f, 1.5f, 5f);
            rig.FromPose(new Vector3(0f, 8f, -10f), new Vector3(0f, 0f, 0f));
            rig.SetHome(rig.yaw, rig.pitch, rig.distance);
            rig.offset = rig.offsetTarget = new Vector2(1f, 0.5f);

            // The subject sits 6 units east of the island centre; the island turns 25 degrees.
            Vector3 centre = new Vector3(-6f, 0f, 0f), subject = Vector3.zero;
            rig.Pose(subject, out Vector3 before, out Quaternion rotBefore);
            const float turn = 25f;
            var spin = Quaternion.Euler(0f, turn, 0f);
            Vector3 moved = centre + spin * (subject - centre);
            rig.TurnBy(turn);
            rig.Pose(moved, out Vector3 after, out Quaternion rotAfter);

            // Seen from the subject, the camera stands exactly where the island's turn carried it.
            Vector3 expected = centre + spin * (before - centre);
            Assert.Less((after - expected).magnitude, 1e-3f, "the camera rode along with the ground");
            Assert.Less(Quaternion.Angle(spin * rotBefore, rotAfter), 0.01f, "and looks the same way relative to it");
            Assert.AreEqual(rig.yaw, rig.yawTarget, 1e-4f, "no easing left to catch up");
            Assert.AreEqual(Mathf.DeltaAngle(0f, rig.homeYaw), Mathf.DeltaAngle(0f, rig.yaw), 1e-3f, "R returns to the turned home");

            Vector2 v = new Vector2(3f, -2f);
            Vector3 q = Quaternion.Euler(0f, 40f, 0f) * new Vector3(v.x, 0f, v.y);
            Vector2 r = PhotoRig.RotateXZ(v, 40f);
            Assert.AreEqual(q.x, r.x, 1e-4f);
            Assert.AreEqual(q.z, r.y, 1e-4f);
        }

        // ------------------------------------------------------------ campfires

        IslandSettlementSystem MakeSettlement(float radius, int seed)
        {
            var go = new GameObject("CampfireIsle");
            go.SetActive(false);
            go.transform.position = new Vector3(20f, 0f, 8f);
            var surface = go.AddComponent<FakeIslandSurface>();
            surface.radius = radius;
            surface.beach = 2f;
            var life = go.AddComponent<IslandLifeSystem>();
            life.seed = seed;
            go.SetActive(true);
            _objects.Add(go);
            life.Simulate(900f, 10f);
            go.SetActive(false);
            var s = go.AddComponent<IslandSettlementSystem>();
            s.seed = seed;
            s.prehistory = false;
            go.SetActive(true);
            for (int i = 0; i < 800; i++) s.Step(0.25f);
            return s;
        }

        static int FirstCampfire(IslandSettlementSystem s)
        {
            for (int i = 0; i < s.BuildingCount; i++) if (WatchSubjects.CampfireStands(s, i)) return i;
            return -1;
        }

        [Test]
        public void ACampfireWatchesItsSettlement()
        {
            var s = MakeSettlement(8f, 47);
            Assume.That(s.VillageCount > 0, "the island founded a village");
            int fire = FirstCampfire(s);
            Assert.GreaterOrEqual(fire, 0, "every village has its campfire");
            int village = s.BuildingVillageOf(fire);
            Assert.AreEqual(fire, WatchSubjects.CampfireOf(s, village));

            var subject = WatchSubjects.OfCampfire(s, fire);
            Assert.IsNotNull(subject);
            Assert.IsTrue(subject.still, "framed and circled like a landmark");
            Assert.AreEqual(WatchSubjects.SettlementPitch, subject.pitch);
            StringAssert.EndsWith("am Lagerfeuer", subject.label);
            Assert.AreEqual(WatchSubjects.SettlementLabel(s.VillageStage(village)), subject.label);
            Assert.GreaterOrEqual(subject.radius, 2.2f, "the frame holds the village round the fire");
            Assert.IsTrue(subject.focus(out Vector3 f));
            Vector2 local = s.BuildingPositionOf(fire);
            Vector3 foot = s.transform.TransformPoint(local.x, s.GetComponent<FakeIslandSurface>().SampleHeight(local), local.y);
            Assert.Less(new Vector2(f.x - foot.x, f.z - foot.z).magnitude, 0.01f, "centred on the fire");

            Assert.AreEqual("Dorf am Lagerfeuer", WatchSubjects.SettlementLabel(SettlementStage.Village));
            Assert.AreEqual("Zeltlager am Lagerfeuer", WatchSubjects.SettlementLabel(SettlementStage.Camp));
            Assert.IsNull(WatchSubjects.OfCampfire(s, -1));
        }

        [Test]
        public void CampfiresAreTapAndSparkleCandidates()
        {
            // TapTargets.Gather itself needs the sea and flock systems (another assembly); the Play Mode check taps one.
            Assert.AreEqual(TapGather.Campfires, TapGather.Extras & TapGather.Campfires, "a tap looks at campfires");
            Assert.AreEqual(TapGather.Campfires, TapGather.All & TapGather.Campfires, "and so does the sparkle");

            var s = MakeSettlement(8f, 47);
            Assume.That(CountFires(s) > 0, "the island founded a village");
            int fire = FirstCampfire(s);
            var t = new TapTarget { kind = TapTargetKind.Campfire, system = s, a = -1, b = s.BuildingVillageOf(fire) };
            Assert.IsTrue(TapTargets.Refresh(ref t), "found again by its village");
            Assert.AreEqual(fire, t.a);
            Assert.Greater(t.size, 0f, "the sparkle sits above the flames");
            Vector3 w = WatchSubjects.CampfireWorld(s, fire);
            Assert.Less((t.world - w).magnitude, 1e-4f);
            var subject = TapTargets.SubjectOf(t);
            Assert.IsNotNull(subject);
            Assert.IsTrue(subject.still);
            StringAssert.EndsWith("am Lagerfeuer", subject.label);

            var gone = new TapTarget { kind = TapTargetKind.Campfire, system = s, a = -1, b = 99 };
            Assert.IsFalse(TapTargets.Refresh(ref gone), "no such village");
        }

        static int CountFires(IslandSettlementSystem s)
        {
            int n = 0;
            for (int i = 0; i < s.BuildingCount; i++) if (WatchSubjects.CampfireStands(s, i)) n++;
            return n;
        }

        // ------------------------------------------------------------ notices 50 % larger

        [Test]
        public void NoticesAreHalfAgainAsLargeAndStillFitThePortraitScreen()
        {
            Assert.AreEqual(76f * 1.5f, WatchTools.NewsSize.y, 1e-3f);
            Assert.AreEqual(51, WatchTools.NewsFontStrong);
            Assert.AreEqual(45, WatchTools.NewsFontSubtle);
            Assert.AreEqual(42f * 1.5f, WatchTools.NewsIconSize, 1e-3f);
            Assert.AreEqual(30f * 1.5f, WatchTools.NewsGoSize, 1e-3f);
            Assert.AreEqual(84f * 1.5f, MilestoneToasts.ToastSize.y, 1e-3f);
            Assert.AreEqual(54, MilestoneToasts.ToastFont, "UiStyle.Body 36 x 1.5");
            Assert.AreEqual(30f * 1.5f, MilestoneToasts.GoSize, 1e-3f);

            // 1116 x 2484 portrait: the 1080 x 1920 canvas (Expand) is 1080 wide; UiStyle.Margin 40.
            const float margin = 40f;
            float width = 1080f - 2f * margin;
            Assert.LessOrEqual(WatchTools.NewsSize.x, width);
            Assert.LessOrEqual(MilestoneToasts.ToastSize.x, width);

            // Under the round journal and photo buttons (pause 120, then two more with 28 between), then the toast.
            float buttonsBottom = -(margin + 296f + 120f);
            Assert.LessOrEqual(WatchTools.NewsTop, buttonsBottom - 8f);
            Assert.LessOrEqual(MilestoneToasts.ToastTop, WatchTools.NewsTop - WatchTools.NewsSize.y - 8f);
            // While watching: under the return button, herd chip and orbit hint (to -676), then the toast.
            Assert.LessOrEqual(WatchTools.NewsTopWatching, -690f);
            Assert.LessOrEqual(MilestoneToasts.ToastTopWatching, WatchTools.NewsTopWatching - WatchTools.NewsSize.y - 8f);
            Assert.Greater(MilestoneToasts.ToastTopWatching - MilestoneToasts.ToastSize.y, -1920f * 0.6f, "clear of Tilda and the map below");
        }

        [Test]
        public void ALongNoticeWrapsOntoASecondLineInsteadOfShrinking()
        {
            var go = new GameObject("NoticeChipTest", typeof(RectTransform));
            _objects.Add(go);
            var chip = (RectTransform)go.transform;
            chip.sizeDelta = MilestoneToasts.ToastSize;
            var label = NoticeChip.WrapLabel(chip, null, MilestoneToasts.ToastFont, Color.white);
            var rt = label.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(36f, 0f);
            rt.offsetMax = new Vector2(-105f, 0f);

            label.text = "Leuchtturm!";
            Assert.AreEqual(MilestoneToasts.ToastSize.y, NoticeChip.Fit(chip, label, MilestoneToasts.ToastSize.y), 0.5f, "one line keeps the chip");
            label.text = "Meilenstein: Dein Leuchtturm steht! Sein Licht zeigt dir ferne Inseln.";
            float h = NoticeChip.Fit(chip, label, MilestoneToasts.ToastSize.y);
            Assert.Greater(h, MilestoneToasts.ToastSize.y, "two lines grow the chip");
            Assert.AreEqual(h, chip.sizeDelta.y, 0.5f);
            Assert.AreEqual(1f, rt.localScale.x, 1e-4f, "the text keeps its size");
            Assert.AreEqual(MilestoneToasts.ToastFont, label.fontSize);
        }
    }
}
