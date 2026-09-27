using System.Collections.Generic;
using Drift.Core;
using Drift.Islands;
using Drift.Life;
using Drift.SaveSystem;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The cozy milestones: 3 islands -> Leuchtturm, 6 -> Hafen, 10 -> Seevögel over the high ground,
    // 15 -> Fest with lanterns.
    public class MilestoneTests
    {
        readonly List<GameObject> _objects = new();
        readonly List<Milestone> _fired = new();
        float _night;

        [SetUp]
        public void SetUp()
        {
            GameModes.Set(GameMode.Cozy);
            Milestones.Reset();
            _fired.Clear();
            Milestones.Celebrated += OnCelebrated;
            LifeLod.DistanceProvider = _ => 0f;
            _night = 0f;
            LifeEnvironment.NightProvider = () => _night;
        }

        [TearDown]
        public void TearDown()
        {
            Milestones.Celebrated -= OnCelebrated;
            Milestones.Reset();
            GameModes.Set(GameMode.Cozy);
            LifeLod.DistanceProvider = null;
            LifeEnvironment.NightProvider = null;
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        void OnCelebrated(Milestone m) => _fired.Add(m);

        // ------------------------------------------------------------ triggering

        [Test]
        public void EachMilestoneFiresOnceAtItsIslandCount()
        {
            Assert.AreEqual(0, Milestones.Check(2));
            Assert.IsFalse(Milestones.Has(Milestone.Lighthouse));

            Assert.AreEqual(1, Milestones.Check(3));
            Assert.AreEqual(new[] { Milestone.Lighthouse }, _fired.ToArray());
            Assert.IsTrue(Milestones.Has(Milestone.Lighthouse));

            Assert.AreEqual(0, Milestones.Check(3), "no second time");
            Assert.AreEqual(0, Milestones.Check(5));
            Assert.AreEqual(1, Milestones.Check(6));
            Assert.AreEqual(Milestone.Harbour, _fired[_fired.Count - 1]);
            Assert.AreEqual(2, _fired.Count);
        }

        [Test]
        public void SeveralMilestonesAtOnceFireInOrder()
        {
            Assert.AreEqual(4, Milestones.Check(15), "a loaded jump reaches all four");
            Assert.AreEqual(new[] { Milestone.Lighthouse, Milestone.Harbour, Milestone.Seabirds, Milestone.Festival }, _fired.ToArray());
            Assert.AreEqual(0, Milestones.Check(20));
        }

        [Test]
        public void AbenteuerHasNoMilestones()
        {
            GameModes.Set(GameMode.Adventure);
            Assert.IsFalse(Milestones.Enabled);
            Assert.AreEqual(0, Milestones.Check(20));
            Assert.AreEqual(0, _fired.Count);
            Assert.IsFalse(Milestones.Has(Milestone.Lighthouse));
        }

        [Test]
        public void NextTextNamesTheNextMilestone()
        {
            Assert.AreEqual("Nächster Meilenstein: Leuchtturm – noch 3 Inseln", Milestones.NextText(0));
            Assert.AreEqual("Nächster Meilenstein: Hafen – noch 3 Inseln", Milestones.NextText(3));
            Assert.AreEqual("Nächster Meilenstein: Seevögel – noch 1 Insel", Milestones.NextText(9));
            Assert.AreEqual("Nächster Meilenstein: Fest – noch 1 Insel", Milestones.NextText(14));
            Assert.IsNull(Milestones.NextText(15));
        }

        [Test]
        public void TheLighthouseOpensTheMinimap()
        {
            Assert.AreEqual(Milestones.NearMapRange, Milestones.MapRange);
            Milestones.Check(3);
            Assert.IsTrue(float.IsPositiveInfinity(Milestones.MapRange));
        }

        // ------------------------------------------------------------ save

        [Test]
        public void RestoreBringsMilestonesBackWithoutCelebrating()
        {
            Milestones.Check(7);
            _fired.Clear();
            int mask = Milestones.Capture();
            Milestones.Reset();

            Milestones.Restore(mask, 7);
            Assert.IsTrue(Milestones.Has(Milestone.Lighthouse));
            Assert.IsTrue(Milestones.Has(Milestone.Harbour));
            Assert.IsFalse(Milestones.Has(Milestone.Seabirds));
            Assert.AreEqual(0, _fired.Count, "loading is not a celebration");
            Assert.AreEqual(0, Milestones.Check(7), "and nothing fires afterwards either");
        }

        [Test]
        public void ANewRunDropsTheMaskOfTheOldOne()
        {
            Milestones.Check(15);
            Milestones.Restore(Milestones.Capture(), 0);
            Assert.AreEqual(0, Milestones.Mask, "a fresh run starts without milestones");
            Assert.AreEqual(1, Milestones.Check(3), "and reaches them again");
        }

        [Test]
        public void AnOldSaveDerivesItsMilestonesFromTheIslandCount()
        {
            // A file written before the milestones has no such key at all.
            var old = JsonUtility.FromJson<SaveGame>("{\"version\":6,\"stats\":{\"islandsAbsorbed\":11}}");
            Assert.AreEqual(0, old.milestones);
            Milestones.Restore(old.milestones, old.stats.islandsAbsorbed);
            Assert.IsTrue(Milestones.Has(Milestone.Seabirds));
            Assert.IsFalse(Milestones.Has(Milestone.Festival));
            Assert.AreEqual(0, _fired.Count);
        }

        [Test]
        public void TheMaskSurvivesTheSaveFileRoundTrip()
        {
            Milestones.Check(10);
            var data = new SaveGame { milestones = Milestones.Capture() };
            data.stats.islandsAbsorbed = 10;
            var back = JsonUtility.FromJson<SaveGame>(JsonUtility.ToJson(data));
            Assert.AreEqual(data.milestones, back.milestones);
            Milestones.Reset();
            Milestones.Restore(back.milestones, back.stats.islandsAbsorbed);
            Assert.AreEqual(data.milestones, Milestones.Mask);
            Assert.AreEqual(6, SaveGame.CurrentVersion, "the milestone field needs no version bump");
        }

        // ------------------------------------------------------------ Leuchtturm and Hafen

        IslandSettlementSystem MakeSettlement(float radius, int seed, bool mature, float beach = 2f)
        {
            var go = new GameObject("MilestoneIsle");
            go.SetActive(false);
            var surface = go.AddComponent<FakeIslandSurface>();
            surface.radius = radius;
            surface.beach = beach;
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

        static void AssertOnValidGround(IslandSettlementSystem s, BuildingKind kind)
        {
            var surface = s.GetComponent<FakeIslandSurface>();
            for (int i = 0; i < s.BuildingCount; i++)
            {
                if (s.BuildingKindOf(i) != kind) continue;
                float h = surface.SampleHeight(s.BuildingPositionOf(i));
                Assert.GreaterOrEqual(h, kind == BuildingKind.Dock ? -0.25f : s.siteMinHeight, kind + " stands on dry ground");
                Assert.LessOrEqual(h, kind == BuildingKind.Dock ? 0.4f : 3f);
                return;
            }
            Assert.Fail("no " + kind + " on the island");
        }

        [Test]
        public void TheLighthouseIsBuiltOnAnIslandWithoutAnyVillage()
        {
            var s = MakeSettlement(3f, 41, false);
            Assert.AreEqual(SettlementStage.None, s.Stage);
            Assert.IsTrue(s.TryBuildLandmark(BuildingKind.Lighthouse));
            Assert.AreEqual(1, s.CountOf(BuildingKind.Lighthouse));
            Assert.AreEqual(1, s.VillageCount, "a village of its own that never grows");

            Assert.IsTrue(s.TryBuildLandmark(BuildingKind.Lighthouse), "asking again changes nothing");
            Assert.AreEqual(1, s.CountOf(BuildingKind.Lighthouse), "and builds no second one");

            Run(s, 600f);
            Assert.AreEqual(1, s.CountOf(BuildingKind.Lighthouse, true), "it is finished");
            Assert.AreEqual(0, s.SettlerCount, "nobody moves in because of a lighthouse");
            Assert.AreEqual(SettlementStage.None, s.Stage, "the island stays unsettled");
            AssertOnValidGround(s, BuildingKind.Lighthouse);
        }

        [Test]
        public void AMilestoneLandmarkGoesUpQuickly()
        {
            var s = MakeSettlement(3f, 43, false);
            s.TryBuildLandmark(BuildingKind.Lighthouse);
            float normal = SettlementMeshes.Spec(BuildingKind.Lighthouse).buildTime * s.buildTimeScale;
            Run(s, normal * 0.5f);
            Assert.AreEqual(1, s.CountOf(BuildingKind.Lighthouse, true), "a milestone is not a 4-minute building site");
        }

        [Test]
        public void TheHarbourJoinsTheVillageThatIsThere()
        {
            var s = MakeSettlement(8f, 47, true);
            Run(s, 200f);
            Assert.AreEqual(1, s.VillageCount, "the island is settled");
            Assert.IsTrue(s.TryBuildLandmark(BuildingKind.Dock));
            Assert.AreEqual(1, s.VillageCount, "no extra village for the harbour");
            Run(s, 300f);
            Assert.GreaterOrEqual(s.CountOf(BuildingKind.Dock, true), 1);
            AssertOnValidGround(s, BuildingKind.Dock);
            Assert.IsTrue(s.TryGetDockWorld(out _, out _), "ships find a mooring");
        }

        [Test]
        public void ALandmarkSurvivesSaveAndLoad()
        {
            var s = MakeSettlement(3f, 51, false);
            s.TryBuildLandmark(BuildingKind.Lighthouse);
            Run(s, 600f);
            var data = s.Capture();
            var json = JsonUtility.FromJson<SettlementSaveData>(JsonUtility.ToJson(data));

            var other = MakeSettlement(3f, 51, false);
            other.Restore(json);
            Assert.AreEqual(1, other.CountOf(BuildingKind.Lighthouse));
            Assert.AreEqual(1, other.VillageCount);
            Run(other, 600f);
            Assert.AreEqual(0, other.SettlerCount, "the landmark village stays a landmark village");
            Assert.AreEqual(SettlementStage.None, other.Stage);
            Assert.AreEqual(1, other.CountOf(BuildingKind.Lighthouse));
        }

        [Test]
        public void AnOlderSaveHasNoLandmarkVillages()
        {
            var s = MakeSettlement(8f, 53, true);
            Run(s, 200f);
            var data = s.Capture();
            data.vLandmark = null;
            var other = MakeSettlement(8f, 53, true);
            other.Restore(JsonUtility.FromJson<SettlementSaveData>(JsonUtility.ToJson(data)));
            Assert.AreEqual(s.VillageCount, other.VillageCount);
            Assert.GreaterOrEqual((int)other.Stage, (int)SettlementStage.Camp, "an old village is still a real village");
        }

        [Test]
        public void ADrownedLandmarkIsBuiltAgain()
        {
            var s = MakeSettlement(6f, 57, false);
            Assert.IsTrue(s.TryBuildLandmark(BuildingKind.Lighthouse));
            Run(s, 600f);
            var surface = s.GetComponent<FakeIslandSurface>();

            // The island shrinks under the lighthouse: it tips over and sinks away like every other building.
            surface.radius = 2f;
            surface.version++;
            Run(s, 30f);
            Assert.AreEqual(0, s.CountOf(BuildingKind.Lighthouse), "the old one is gone");
            // The director simply keeps asking; that must not pile up empty villages.
            for (int i = 0; i < 4; i++) s.TryBuildLandmark(BuildingKind.Lighthouse);
            Assert.IsTrue(s.TryBuildLandmark(BuildingKind.Lighthouse), "and a new one is built on what is left");
            Assert.AreEqual(1, s.VillageCount);
            Run(s, 600f);
            Assert.AreEqual(1, s.CountOf(BuildingKind.Lighthouse, true));
            AssertOnValidGround(s, BuildingKind.Lighthouse);
        }

        // ------------------------------------------------------------ Fest with lanterns

        [Test]
        public void TheFestivalOnlyHappensInTheEveningAndOnlyAfterTheMilestone()
        {
            var s = MakeSettlement(8f, 61, true);
            Run(s, 200f);
            Assert.Greater(s.SettlerCount, 0);

            _night = 0.7f;
            Run(s, 2f);
            Assert.IsFalse(s.Festival, "without the milestone nobody celebrates");

            s.festivals = true;
            _night = 0.1f;
            Run(s, 2f);
            Assert.IsFalse(s.Festival, "not in broad daylight");

            _night = 0.7f;
            Run(s, 2f);
            Assert.IsTrue(s.Festival, "the evening festival is on");
            Assert.IsTrue(s.Celebrating, "and the folk are dancing");
            Assert.Greater(s.LanternCount, 0, "lanterns hang round the festival ground");
            Assert.Greater(s.GlowHaloCount, 0, "and they are lit");

            _night = 0.1f;
            Run(s, 2f);
            Assert.IsFalse(s.Festival);
            Assert.AreEqual(0, s.LanternCount);
        }

        [Test]
        public void TheFestivalCostsNoExtraDrawCallsAndStaysWithinBudget()
        {
            var s = MakeSettlement(10f, 63, true);
            Run(s, 900f);
            s.festivals = true;
            _night = 0.7f;
            Run(s, 3f);
            Assert.IsTrue(s.Festival);
            Assert.LessOrEqual(s.MeshVertexCount, 12000);
            int builds = s.StaticMeshBuilds;
            Run(s, 5f);
            Assert.LessOrEqual(s.StaticMeshBuilds - builds, 25, "a running festival is baked, not rebuilt every frame");
        }

        // ------------------------------------------------------------ Seevögel over the high ground

        Island MakeIsland(Vector2 pos, int seed, float radius, bool player)
        {
            var go = new GameObject(player ? "MilestonePlayer" : "MilestoneIsle_" + seed);
            go.SetActive(false);
            go.transform.position = new Vector3(pos.x, 0f, pos.y);
            var isl = go.AddComponent<Island>();
            isl.useKeyboardInput = player;
            isl.landRadius = radius;
            isl.shapeSeed = seed;
            go.SetActive(true);
            _objects.Add(go);
            return isl;
        }

        [Test]
        public void HomeSeabirdsCircleTheSummitOfTheirOwnIsland()
        {
            Vector2 origin = new Vector2(-41000f, 41000f);
            var player = MakeIsland(origin, 12347, 7f, true);
            MakeIsland(origin + new Vector2(-60f, 20f), 991, 5f, false);
            var go = new GameObject("MilestoneFlocks");
            go.SetActive(false);
            var flocks = go.AddComponent<FlockSystem>();
            flocks.player = player;
            flocks.spawnMin = 30f;
            flocks.spawnMax = 50f;
            flocks.seabirdDivesPerMinute = 0f;
            go.SetActive(true);
            _objects.Add(go);
            flocks.Step(0.05f);

            Assert.AreEqual(0, flocks.HomeFlockCount, "off until the milestone");
            flocks.SetSeabirdHome(player, 2);
            Assert.AreEqual(2, flocks.HomeFlockCount);

            int home = -1;
            for (int i = 0; i < flocks.FlockCount; i++) if (flocks.IsHomeFlock(i)) home = i;
            Assert.GreaterOrEqual(home, 0);

            for (int t = 0; t < 4000 && flocks.StateOf(home) != FlockSystem.FlockState.Orbit; t++) flocks.Step(0.05f);
            Assert.AreEqual(FlockSystem.FlockState.Orbit, flocks.StateOf(home), "it reaches the island and circles");
            Assert.AreEqual(player, flocks.TargetOf(home), "and never leaves it");

            // Let it settle onto its circle, then watch a whole lap.
            for (int t = 0; t < 200; t++) flocks.Step(0.05f);
            float peak = player.MaxHeight;
            float reach = player.BoundingRadius * flocks.homeOrbitRadius + 4f;
            for (int t = 0; t < 400; t++)
            {
                flocks.Step(0.05f);
                float d = (flocks.PositionOf(home) - flocks.SummitOf(player)).magnitude;
                Assert.LessOrEqual(d, reach, "it circles the summit, not the whole island");
                Assert.Greater(flocks.HeightOf(home), player.transform.position.y + peak, "above the high ground");
                Assert.AreEqual(player, flocks.TargetOf(home));
            }
        }
    }
}
