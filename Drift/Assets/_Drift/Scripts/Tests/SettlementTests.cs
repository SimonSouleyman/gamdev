using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class SettlementTests
    {
        readonly List<GameObject> _objects = new();
        float _night;

        [SetUp]
        public void SetUp()
        {
            LifeLod.DistanceProvider = _ => 0f;
            _night = 0f;
            LifeEnvironment.NightProvider = () => _night;
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

        // A flat-topped fake island with a sloping beach; mature = the life grid was fast-forwarded to woods.
        IslandSettlementSystem Make(float radius, int seed, string name = "Isle", bool mature = true, float beach = 2f, bool prehistory = false, int character = 0, Vector3 position = default)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.position = position;
            var surface = go.AddComponent<FakeIslandSurface>();
            surface.radius = radius;
            surface.beach = beach;
            surface.character = character;
            var life = go.AddComponent<IslandLifeSystem>();
            life.seed = seed;
            go.SetActive(true);
            _objects.Add(go);
            // The settlement joins after the fast-forward, which would otherwise hand it 150 s of catch-up.
            if (mature) life.Simulate(900f, 10f);
            go.SetActive(false);
            var s = go.AddComponent<IslandSettlementSystem>();
            s.seed = seed;
            s.prehistory = prehistory;
            go.SetActive(true);
            return s;
        }

        static FakeIslandSurface Surface(IslandSettlementSystem s) => s.GetComponent<FakeIslandSurface>();

        static void Run(IslandSettlementSystem s, float seconds, float dt = 0.25f)
        {
            int n = Mathf.CeilToInt(seconds / dt);
            for (int i = 0; i < n; i++) s.Step(dt);
        }

        static void AssertCaps(IslandSettlementSystem s)
        {
            Assert.LessOrEqual(s.StructureCount, s.maxStructures);
            Assert.LessOrEqual(s.PlotCount, s.maxPlots);
            Assert.LessOrEqual(s.SettlerCount, s.maxSettlers);
            Assert.LessOrEqual(s.VillageCount, s.maxVillages);
            Assert.LessOrEqual(s.MeshVertexCount, 12000);
        }

        static void AssertValidGround(IslandSettlementSystem s)
        {
            var surface = Surface(s);
            for (int i = 0; i < s.BuildingCount; i++)
            {
                var kind = s.BuildingKindOf(i);
                Vector2 p = s.BuildingPositionOf(i);
                float h = surface.SampleHeight(p);
                if (kind == BuildingKind.Dock)
                {
                    Assert.That(h, Is.InRange(-0.25f, 0.4f), "a dock starts at the waterline");
                    continue;
                }
                Assert.GreaterOrEqual(h, s.siteMinHeight - 1e-4f, kind + " below the height band");
                if (kind != BuildingKind.Lighthouse && kind != BuildingKind.Shrine && kind != BuildingKind.Field)
                {
                    Assert.LessOrEqual(h, s.siteMaxHeight + 1e-4f, kind + " above the height band");
                    Assert.IsTrue(s.IsValidSite(p, s.BuildingRadiusOf(i)), kind + " on invalid ground (slope or beach) at " + p);
                }
                if (kind != BuildingKind.Lighthouse)
                    for (int a = 0; a < 8; a++)
                    {
                        Vector2 q = p + new Vector2(Mathf.Cos(a * Mathf.PI / 4f), Mathf.Sin(a * Mathf.PI / 4f)) * s.beachMargin;
                        Assert.GreaterOrEqual(surface.SampleHeight(q), s.beachHeight - 1e-4f, kind + " on the beach");
                    }
                for (int j = i + 1; j < s.BuildingCount; j++)
                {
                    float d = (s.BuildingPositionOf(j) - p).magnitude;
                    Assert.GreaterOrEqual(d, s.BuildingRadiusOf(i) + s.BuildingRadiusOf(j) - 1e-3f, kind + " overlaps " + s.BuildingKindOf(j));
                }
            }
        }

        // ------------------------------------------------------------ founding

        [Test]
        public void NoSettlement_OnSmallOrImmatureIslands()
        {
            var small = Make(3f, 201, "Small");
            Assert.Less(Surface(small).LandArea, small.foundingArea);
            var young = Make(7f, 202, "Young", mature: false, character: 1);
            Assert.Less(young.MatureFraction(), young.matureFraction, "a newborn volcano has no woods yet");
            Run(small, small.foundingDelay * 3f, 0.5f);
            Run(young, young.foundingDelay * 3f, 0.5f);
            Assert.AreEqual(0, small.VillageCount);
            Assert.AreEqual(0, small.BuildingCount);
            Assert.AreEqual(SettlementStage.None, small.Stage);
            Assert.AreEqual("Unbesiedelt", small.StageName);
            Assert.AreEqual(0, young.VillageCount);
            Assert.AreEqual(0, young.FoundTimer, "the founding clock only runs on eligible islands");
            Assert.AreEqual(0, small.transform.childCount - ChildCount(small, "Vegetation"), "an unsettled island gets no settlement objects");
        }

        static int ChildCount(Component c, string name)
        {
            int n = 0;
            for (int i = 0; i < c.transform.childCount; i++) if (c.transform.GetChild(i).name == name) n++;
            return n;
        }

        [Test]
        public void Founding_AfterTheDelayOnABigMatureIsland()
        {
            var s = Make(5f, 203);
            Assert.GreaterOrEqual(Surface(s).LandArea, s.foundingArea);
            Assert.GreaterOrEqual(s.MatureFraction(), s.matureFraction);
            Run(s, s.foundingDelay * 0.5f, 0.5f);
            Assert.AreEqual(0, s.VillageCount, "not before the founding delay");
            Run(s, s.foundingDelay * 0.5f + 2f, 0.5f);
            Assert.AreEqual(1, s.VillageCount);
            Assert.AreEqual(SettlementStage.Camp, s.Stage);
            Assert.AreEqual("Lager", s.StageName);
            Assert.AreEqual(1, s.CountOf(BuildingKind.Campfire));
            Assert.That(s.SettlerCount, Is.InRange(2, 4));
            Run(s, 150f, 0.5f);
            Assert.AreEqual(2, s.CountOf(BuildingKind.Tent), "a camp is a fire and two tents");
            Assert.AreEqual(3, s.BuildingCount);
            Assert.That(s.SettlerCount, Is.InRange(2, 4));
            AssertValidGround(s);
            AssertCaps(s);
            Assert.Greater(s.StaticVertexCount, 0);
            Assert.Greater(s.FolkVertexCount, 0);
        }

        // ------------------------------------------------------------ stages

        [Test]
        public void Stages_AdvanceMonotonicallyUpToTheAreaAndStayInsideTheCaps()
        {
            var town = Make(7f, 204, "Town");
            var hamlet = Make(4f, 205, "Hamlet");
            Assert.GreaterOrEqual(Surface(town).LandArea, town.townArea);
            Assert.That(Surface(hamlet).LandArea, Is.InRange(hamlet.hamletArea, hamlet.villageArea - 0.1f));
            var last = SettlementStage.None;
            var seen = new HashSet<SettlementStage>();
            for (int i = 0; i < 400; i++)
            {
                town.StepGrowth(30f);
                hamlet.StepGrowth(30f);
                Assert.GreaterOrEqual(town.Stage, last, "a stage is never lost");
                last = town.Stage;
                seen.Add(last);
                AssertCaps(town);
                AssertCaps(hamlet);
            }
            Assert.AreEqual(SettlementStage.Town, town.Stage);
            Assert.AreEqual("Stadt", town.StageName);
            Assert.AreEqual(5, seen.Count, "None, Lager, Weiler, Dorf, Stadt were all passed through");
            Assert.AreEqual(SettlementStage.Hamlet, hamlet.Stage, "a 50-area island stops at the hamlet");
            Assert.AreEqual("Weiler", hamlet.StageName);
            Assert.AreEqual(0, town.CountOf(BuildingKind.Tent), "tents were rebuilt as huts");
            Assert.AreEqual(0, town.CountOf(BuildingKind.Hut), "huts were rebuilt in stone");
            Assert.Greater(town.CountOf(BuildingKind.StoneHouse, true), 0);
            Assert.AreEqual(1, town.CountOf(BuildingKind.Windmill, true));
            Assert.AreEqual(1, town.CountOf(BuildingKind.Lighthouse, true));
            Assert.AreEqual(1, town.CountOf(BuildingKind.Market, true));
            Assert.GreaterOrEqual(town.CountOf(BuildingKind.Dock, true), 1);
            Assert.That(town.StructureCount, Is.InRange(18, town.maxStructures));
            Assert.Greater(hamlet.CountOf(BuildingKind.Hut, true), 2);
            Assert.AreEqual(0, hamlet.CountOf(BuildingKind.House) + hamlet.CountOf(BuildingKind.Windmill));
            AssertValidGround(town);
            AssertValidGround(hamlet);
            Assert.IsTrue(town.TryGetDockWorld(out Vector3 dock, out Vector3 sea));
            Assert.Less(Surface(town).SampleHeight(new Vector2(dock.x, dock.z)), 0f, "the jetty ends over water");
            Assert.Greater(Vector3.Dot(sea, new Vector3(dock.x, 0f, dock.z).normalized), 0.5f, "and points out to sea");

            town.Step(0.3f);
            Run(town, 3f, 0.1f);
            Assert.AreEqual(town.maxSettlers, town.SettlerCount, "a town is home to the full folk");
            AssertCaps(town);
        }

        [Test]
        public void Construction_GoesThroughFoundationScaffoldAndFinish()
        {
            var s = Make(5f, 206);
            Run(s, s.foundingDelay + 1f, 0.5f);
            int tent = -1;
            for (int t = 0; t < 400 && tent < 0; t++)
            {
                s.Step(0.25f);
                for (int i = 0; i < s.BuildingCount; i++) if (s.BuildingKindOf(i) == BuildingKind.Tent) tent = i;
            }
            Assert.GreaterOrEqual(tent, 0);
            Assert.AreEqual(BuildingState.Site, s.BuildingStateOf(tent));
            float last = s.BuildingProgressOf(tent);
            int builds = s.StaticMeshBuilds;
            var verts = new HashSet<int>();
            while (s.BuildingStateOf(tent) == BuildingState.Site)
            {
                s.Step(0.25f);
                Assert.GreaterOrEqual(s.BuildingProgressOf(tent), last);
                last = s.BuildingProgressOf(tent);
                verts.Add(s.StaticVertexCount);
            }
            Assert.AreEqual(BuildingState.Done, s.BuildingStateOf(tent));
            Assert.GreaterOrEqual(verts.Count, 3, "foundation, scaffold and body show up as different meshes");
            Assert.LessOrEqual(s.StaticMeshBuilds - builds, 2 * 60 + 10, "the static mesh only follows construction steps");
        }

        // ------------------------------------------------------------ hazards

        [Test]
        public void Sinking_RemovesDrownedBuildingsAndTheFolkRebuild()
        {
            var s = Make(6f, 207, "Sinker", beach: 5f);
            s.Simulate(1500f);
            Assert.GreaterOrEqual(s.Stage, SettlementStage.Hamlet);
            int before = s.BuildingCount;
            Assert.Greater(before, 5);
            var surface = Surface(s);
            surface.height = 0.02f;
            surface.version++;
            s.Step(0.1f);
            // The jetty may stay: the waterline it stands on has not moved.
            int sinking = 0, docks = s.CountOf(BuildingKind.Dock);
            for (int i = 0; i < s.BuildingCount; i++) if (s.BuildingStateOf(i) == BuildingState.Sinking) sinking++;
            Assert.GreaterOrEqual(sinking, before - docks, "everything on land stands under water now");
            Run(s, s.sinkTime + 2f);
            Assert.LessOrEqual(s.BuildingCount, docks, "drowned buildings sink away");
            Assert.AreEqual(0, s.SettlerCount, "nobody stays on drowned ground");
            Assert.AreEqual(1, s.VillageCount, "the village waits for new ground");

            surface.height = 1f;
            surface.version++;
            Run(s, 400f, 0.5f);
            Assert.Greater(s.BuildingCount, 1, "the folk rebuild on the risen ground");
            Assert.GreaterOrEqual(s.Stage, SettlementStage.Hamlet, "the stage survives");
            AssertValidGround(s);
        }

        [Test]
        public void Fire_CharsWoodenBuildingsAndTheyAreRebuilt()
        {
            var s = Make(5f, 208);
            var life = s.GetComponent<IslandLifeSystem>();
            s.Simulate(1500f);
            int hut = -1, well = -1;
            for (int i = 0; i < s.BuildingCount; i++)
            {
                if (s.BuildingKindOf(i) == BuildingKind.Hut && s.BuildingStateOf(i) == BuildingState.Done) hut = i;
                if (s.BuildingKindOf(i) == BuildingKind.Well && s.BuildingStateOf(i) == BuildingState.Done) well = i;
            }
            Assert.GreaterOrEqual(hut, 0);
            Assert.GreaterOrEqual(well, 0);
            s.rebuildDelay = 20f;
            Assert.IsTrue(life.IgniteAt(s.BuildingPositionOf(hut)));
            life.IgniteAt(s.BuildingPositionOf(well));
            s.Step(0.6f);
            Assert.AreEqual(BuildingState.Burning, s.BuildingStateOf(hut));
            Assert.AreEqual(BuildingState.Done, s.BuildingStateOf(well), "stone does not burn");
            Run(s, 8f);
            Assert.AreEqual(BuildingState.Charred, s.BuildingStateOf(hut));
            int settlers = s.SettlerCount;
            // The fire burns out in the life system, then the rebuild delay runs.
            for (int t = 0; t < 300; t++) { life.Step(0.2f); s.Step(0.2f); }
            Assert.That(s.BuildingStateOf(hut), Is.EqualTo(BuildingState.Site).Or.EqualTo(BuildingState.Done), "the charred frame becomes a building site again");
            Run(s, 200f, 0.5f);
            Assert.AreEqual(BuildingState.Done, s.BuildingStateOf(hut));
            Assert.AreEqual(BuildingKind.Hut, s.BuildingKindOf(hut));
            Assert.GreaterOrEqual(s.SettlerCount, settlers - 1, "nobody dies in a fire");
        }

        [Test]
        public void Settlers_GatherAtTheFireSleepIndoorsShelterFromStormsAndWave()
        {
            var s = Make(5f, 209);
            s.Simulate(1500f);
            Run(s, 5f, 0.1f);
            Assert.Greater(s.SettlerCount, 4);
            bool waved = false, walked = false;
            for (int t = 0; t < 600; t++)
            {
                s.Step(0.1f);
                for (int i = 0; i < s.SettlerCount; i++)
                {
                    waved |= s.SettlerWaving(i);
                    walked |= s.SettlerStateOf(i) == SettlerState.Walk;
                    Assert.GreaterOrEqual(Surface(s).SampleHeight(s.SettlerPositionOf(i)), -0.81f);
                }
            }
            Assert.IsTrue(waved, "with the camera this close somebody waves");
            Assert.IsTrue(walked);

            _night = 0.7f;
            Run(s, 90f, 0.1f);
            int atFire = 0;
            for (int i = 0; i < s.SettlerCount; i++) if (s.SettlerStateOf(i) == SettlerState.Gather) atFire++;
            Assert.Greater(atFire, s.SettlerCount / 2, "the evening is spent by the fire");

            _night = 1f;
            Run(s, 90f, 0.1f);
            int indoors = 0;
            for (int i = 0; i < s.SettlerCount; i++) if (!s.SettlerVisible(i)) indoors++;
            Assert.Greater(indoors, s.SettlerCount / 2, "deep night is spent indoors");

            _night = 0f;
            Run(s, 40f, 0.1f);
            for (int i = 0; i < s.SettlerCount; i++) Assert.IsTrue(s.SettlerVisible(i), "everybody is up by day");
            Surface(s).storm = 1f;
            Run(s, 90f, 0.1f);
            indoors = 0;
            for (int i = 0; i < s.SettlerCount; i++) if (!s.SettlerVisible(i)) indoors++;
            Assert.Greater(indoors, s.SettlerCount / 2, "a storm sends them indoors");
            Surface(s).storm = 0f;

            LifeLod.DistanceProvider = _ => 20f;
            Run(s, 15f, 0.1f);
            for (int t = 0; t < 100; t++)
            {
                s.Step(0.1f);
                for (int i = 0; i < s.SettlerCount; i++) Assert.IsFalse(s.SettlerWaving(i), "nobody waves at a distant camera");
            }
        }

        // ------------------------------------------------------------ tiers and budget

        [Test]
        public void FarTier_GrowsStatisticallyWithoutSettlerWorkOrMeshChurn()
        {
            var s = Make(6f, 210);
            LifeLod.DistanceProvider = _ => 1000f;
            Run(s, 1500f, 1f);
            Assert.AreEqual(LifeTier.Far, s.Tier);
            Assert.GreaterOrEqual(s.Stage, SettlementStage.Hamlet, "growth goes on far away");
            Assert.AreEqual(0, s.SettlerSteps, "no per-settler work in the far tier");
            Assert.AreEqual(0, s.FolkVertexCount);
            Assert.LessOrEqual(s.StaticMeshBuilds, 1, "hidden and far: nothing is meshed");

            LifeLod.DistanceProvider = _ => (s.detailDistance + s.simDistance) * 0.5f;
            int steps = s.SettlerSteps;
            Run(s, 10f, 0.05f);
            Assert.AreEqual(LifeTier.Mid, s.Tier);
            Assert.Greater(s.SettlerSteps, steps);
            Assert.LessOrEqual(s.SettlerSteps - steps, (Mathf.CeilToInt(10f / s.midStepInterval) + 1) * s.maxSettlers, "mid steps at the low rate");

            // Close to the camera the pixel budget may ask for every frame (CloseUpMotion, 2026-09-25); without movers
            // fast enough to step visibly the base rate holds.
            LifeLod.DistanceProvider = _ => 0f;
            s.folkCloseUpSpeed = 0f;
            int folk = s.FolkMeshBuilds;
            Run(s, 10f, 0.02f);
            Assert.LessOrEqual(s.FolkMeshBuilds - folk, Mathf.CeilToInt(10f / s.folkMeshInterval) + 1, "the folk mesh rebuilds at 10 Hz at most");
            Assert.Greater(s.FolkVertexCount, 0);
        }

        [Test]
        public void VertexBudget_TemplatesAndAFullTownStayInside()
        {
            for (int k = 0; k < SettlementMeshes.KindCount; k++)
            {
                var spec = SettlementMeshes.Spec((BuildingKind)k);
                Assert.That(spec.MaxVerts, Is.InRange(30, 200), ((BuildingKind)k) + " vertex count");
                if (spec.scaffold != null) Assert.LessOrEqual(spec.scaffold.vertices.Length + spec.foundation.vertices.Length, 260);
            }
            for (int v = 0; v < SettlementMeshes.SettlerVariants; v++)
                for (int pose = 0; pose < 2; pose++)
                    Assert.LessOrEqual(SettlementMeshes.Settler(v, pose).vertices.Length, 40);

            var s = Make(7f, 211);
            s.Simulate(12000f, 30f);
            Assert.AreEqual(SettlementStage.Town, s.Stage);
            _night = 0.7f;
            Run(s, 30f, 0.1f);
            Assert.AreEqual(s.maxSettlers, s.SettlerCount);
            Assert.LessOrEqual(s.MeshVertexCount, 12000);
            Assert.Greater(s.StaticVertexCount, 1500);
            Assert.AreEqual("Drift/VertexColor", s.transform.Find("Settlement").GetComponent<MeshRenderer>().sharedMaterial.shader.name);
        }

        [Test]
        public void Clearing_TakesTreesOffTheVillageGroundAndTintsIt()
        {
            var s = Make(5f, 212);
            var life = s.GetComponent<IslandLifeSystem>();
            Assert.AreSame(s, life.Settlement);
            s.Simulate(1500f);
            life.Simulate(60f, 5f);
            int checkedBuildings = 0;
            for (int i = 0; i < s.BuildingCount; i++)
            {
                var kind = s.BuildingKindOf(i);
                if (kind == BuildingKind.Dock || kind == BuildingKind.Lighthouse || kind == BuildingKind.Shrine) continue;
                Assert.IsTrue(s.Clears(s.BuildingPositionOf(i)));
                checkedBuildings++;
            }
            Assert.Greater(checkedBuildings, 3);
            for (int p = 0; p < life.PlantCount; p++)
            {
                if (life.PlantDyingOf(p)) continue;
                var kind = life.PlantKindOf(p);
                if (kind != LifeKind.Tree && kind != LifeKind.Bush) continue;
                Vector2 cell = new Vector2(Mathf.Floor(life.PlantPositionOf(p).x / life.cellSize) + 0.5f, Mathf.Floor(life.PlantPositionOf(p).y / life.cellSize) + 0.5f) * life.cellSize;
                Assert.IsFalse(s.Clears(cell), kind + " left standing in the clearing at " + life.PlantPositionOf(p));
            }
            Assert.IsFalse(s.Clears(new Vector2(100f, 100f)));
        }

        // ------------------------------------------------------------ scale

        [Test]
        public void Scale_HousesAreAThirdOfTheOldSizeAndEverythingFollows()
        {
            var s = Make(7f, 220, "SmallTown");
            Assert.That(s.buildingScale, Is.InRange(0.5f, 0.6f), "a third of the old 1.5, never more than half");
            s.Simulate(12000f, 30f);
            Assert.AreEqual(SettlementStage.Town, s.Stage);
            Assert.GreaterOrEqual(s.StructureCount, 24, "the small houses leave room for a bigger town");
            AssertValidGround(s);
            AssertCaps(s);

            float house = 0f, lighthouse = 0f, furthest = 0f;
            int hut = -1;
            for (int i = 0; i < s.BuildingCount; i++)
            {
                var kind = s.BuildingKindOf(i);
                if (kind == BuildingKind.House || kind == BuildingKind.StoneHouse) { house = s.BuildingHeightOf(i); hut = i; }
                if (kind == BuildingKind.Lighthouse) lighthouse = s.BuildingHeightOf(i);
                if (kind != BuildingKind.Dock && kind != BuildingKind.Lighthouse && kind != BuildingKind.Shrine)
                    furthest = Mathf.Max(furthest, (s.BuildingPositionOf(i) - s.VillageCenter(0)).magnitude);
            }
            Assert.That(house, Is.InRange(0.15f, 0.2f), "a house is a small thing under 1 u trees");
            Assert.That(s.SettlerHeight / house, Is.InRange(0.22f, 0.36f), "a settler is a quarter to a third of a house");
            Assert.Greater(lighthouse, house * 2.5f, "the lighthouse stays a landmark");
            Assert.Less(furthest, 3.2f, "a whole town fits on a few units of ground");

            // Herd footprint: inside the walls, not a body length beside them.
            Vector2 p = s.BuildingPositionOf(hut);
            Assert.IsTrue(s.Blocks(p));
            Assert.Less(s.BuildingRadiusOf(hut), 0.2f);
            int blockedRing = 0;
            for (int a = 0; a < 8; a++)
                if (s.Blocks(p + new Vector2(Mathf.Cos(a * Mathf.PI / 4f), Mathf.Sin(a * Mathf.PI / 4f)) * 0.35f)) blockedRing++;
            Assert.LessOrEqual(blockedRing, 4, "0.35 u from a house is free ground unless a neighbour stands there");

            // Clearing: the cells under the buildings and their closest neighbours, not the wood round the town.
            var life = s.GetComponent<IslandLifeSystem>();
            int cleared = 0, clearing = 0;
            for (float z = -7f + life.cellSize * 0.5f; z < 7f; z += life.cellSize)
                for (float x = -7f + life.cellSize * 0.5f; x < 7f; x += life.cellSize)
                    if (s.Clears(new Vector2(x, z))) cleared++;
            for (int i = 0; i < s.BuildingCount; i++)
            {
                var kind = s.BuildingKindOf(i);
                if (kind != BuildingKind.Dock && kind != BuildingKind.Lighthouse && kind != BuildingKind.Shrine) clearing++;
            }
            Assert.Greater(cleared, 0);
            Assert.LessOrEqual(cleared, clearing, "on average less than one 1.2 u cell per building");

            // The jetty still ends over the water, its mooring spot a jetty length out from the shore end.
            Assert.IsTrue(s.TryGetDockWorld(out Vector3 dock, out Vector3 sea));
            float nearest = float.MaxValue;
            for (int i = 0; i < s.BuildingCount; i++)
                if (s.BuildingKindOf(i) == BuildingKind.Dock)
                    nearest = Mathf.Min(nearest, (new Vector2(dock.x, dock.z) - s.BuildingPositionOf(i)).magnitude);
            Assert.That(nearest, Is.EqualTo(0.6f * s.ScaleOf(BuildingKind.Dock)).Within(1e-3f));
            Assert.That(dock.y, Is.InRange(0.03f, 0.08f), "the deck stays above the waves");
            Assert.Less(Surface(s).SampleHeight(new Vector2(dock.x, dock.z)), 0f);

            // Night: every lit building carries a halo, so the windows read from the chase camera.
            Assert.AreEqual(0, s.GlowHaloCount, "no halos by day");
            _night = 0.75f;
            Run(s, 1f, 0.1f);
            Assert.Greater(s.GlowHaloCount, s.StructureCount / 2);
            Assert.LessOrEqual(s.GlowVertexCount, 400);
            Assert.AreEqual(1f, s.transform.Find("SettlementGlow").GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_GlowMode"));
        }

        [Test]
        public void OldSaves_WithBigHousesAreDrawnTogetherRoundTheHearth()
        {
            var a = Make(6f, 221, "OldWorld");
            a.buildingScale = 1.5f;
            a.beachMargin = 0.5f;
            a.Simulate(2600f);
            Assert.GreaterOrEqual(a.Stage, SettlementStage.Hamlet);
            var saved = JsonUtility.FromJson<LifeSaveData>(JsonUtility.ToJson(a.GetComponent<IslandLifeSystem>().Capture()));
            Assert.AreEqual(1.5f, saved.settlement.scale);
            saved.settlement.scale = 0f;

            var b = Make(6f, 222, "NewWorld");
            b.GetComponent<IslandLifeSystem>().Restore(saved);
            Assert.AreEqual(a.BuildingCount, b.BuildingCount);
            float ratio = b.buildingScale / 1.5f;
            int moved = 0;
            for (int i = 0; i < b.BuildingCount; i++)
            {
                var kind = b.BuildingKindOf(i);
                float before = (a.BuildingPositionOf(i) - a.VillageCenter(0)).magnitude, after = (b.BuildingPositionOf(i) - b.VillageCenter(0)).magnitude;
                if (kind == BuildingKind.Dock || kind == BuildingKind.Lighthouse || kind == BuildingKind.Shrine)
                {
                    Assert.AreEqual(a.BuildingPositionOf(i), b.BuildingPositionOf(i), kind + " keeps its shore");
                    continue;
                }
                Assert.LessOrEqual(after, before + 1e-4f);
                if (before > 0.05f && after < before * (ratio + 0.05f)) moved++;
            }
            Assert.Greater(moved, 3, "the houses moved in towards the hearth");
            for (int i = 0; i < b.BuildingCount; i++)
                for (int j = i + 1; j < b.BuildingCount; j++)
                    Assert.GreaterOrEqual((b.BuildingPositionOf(i) - b.BuildingPositionOf(j)).magnitude, (b.BuildingRadiusOf(i) + b.BuildingRadiusOf(j)) * 0.9f, "overlap after the rescale");
            Run(b, 5f);
            AssertCaps(b);
        }

        // ------------------------------------------------------------ persistence and merges

        [Test]
        public void Save_RoundTripKeepsVillagesAndBuildings_OldSavesLoadUnsettled()
        {
            var a = Make(6f, 213, "A");
            a.Simulate(2600f);
            Assert.GreaterOrEqual(a.Stage, SettlementStage.Hamlet);
            var lifeA = a.GetComponent<IslandLifeSystem>();
            var json = JsonUtility.ToJson(lifeA.Capture());
            var saved = JsonUtility.FromJson<LifeSaveData>(json);
            Assert.IsNotNull(saved.settlement);
            Assert.AreEqual(a.BuildingCount, saved.settlement.bKind.Length);

            var b = Make(6f, 214, "B", prehistory: true);
            b.GetComponent<IslandLifeSystem>().Restore(saved);
            Assert.AreEqual(a.VillageCount, b.VillageCount);
            Assert.AreEqual(a.Stage, b.Stage);
            Assert.AreEqual(a.BuildingCount, b.BuildingCount);
            for (int i = 0; i < a.BuildingCount; i++)
            {
                Assert.AreEqual(a.BuildingKindOf(i), b.BuildingKindOf(i));
                Assert.AreEqual(a.BuildingStateOf(i), b.BuildingStateOf(i));
                Assert.AreEqual(a.BuildingPositionOf(i), b.BuildingPositionOf(i));
                Assert.AreEqual(a.BuildingYawOf(i), b.BuildingYawOf(i), 1e-4f);
                Assert.AreEqual(a.BuildingProgressOf(i), b.BuildingProgressOf(i), 1e-5f);
            }
            Assert.AreEqual(a.SettlerCount, b.SettlerCount, "the folk follow from the homes");
            Run(b, 5f);
            AssertCaps(b);

            // Offline catch-up: the life system's fast-forward advances construction too.
            int done = 0, doneAfter = 0;
            for (int i = 0; i < b.BuildingCount; i++) if (b.BuildingStateOf(i) == BuildingState.Done) done++;
            b.GetComponent<IslandLifeSystem>().Simulate(1800f, 10f);
            for (int i = 0; i < b.BuildingCount; i++) if (b.BuildingStateOf(i) == BuildingState.Done) doneAfter++;
            Assert.Greater(doneAfter, done);

            // A file from before the settlements has no block: the island loads unsettled, prehistory or not.
            saved.settlement = null;
            var old = JsonUtility.FromJson<LifeSaveData>(JsonUtility.ToJson(saved));
            var c = Make(6f, 215, "C", prehistory: true);
            c.GetComponent<IslandLifeSystem>().Restore(old);
            Assert.AreEqual(0, c.VillageCount);
            Assert.AreEqual(0, c.BuildingCount);
            Assert.AreEqual(0, c.SettlerCount);
            Assert.AreEqual(SettlementStage.None, c.Stage);
        }

        [Test]
        public void Prehistory_IsDeterministicPerSeedAndSkipsVolcanoes()
        {
            int settled = 0;
            for (int seed = 300; seed < 312; seed++)
            {
                var a = Make(6f, seed, "P" + seed, mature: false, prehistory: true);
                var b = Make(6f, seed, "Q" + seed, mature: false, prehistory: true);
                Assert.AreEqual(a.Stage, b.Stage);
                Assert.AreEqual(a.BuildingCount, b.BuildingCount);
                for (int i = 0; i < a.BuildingCount; i++) Assert.AreEqual(a.BuildingPositionOf(i), b.BuildingPositionOf(i));
                if (a.VillageCount > 0) settled++;
                AssertValidGround(a);
                AssertCaps(a);
            }
            Assert.That(settled, Is.InRange(5, 12), "most big islands of the world are already settled");
            var volcano = Make(6f, 300, "Volcano", mature: false, prehistory: true, character: 1);
            Assert.AreEqual(0, volcano.VillageCount);
        }

        [Test]
        public void AbsorbFrom_TwoTownsStayInsideTheIslandCaps()
        {
            var host = Make(7f, 218, "HostTown");
            var guest = Make(7f, 219, "GuestTown", position: new Vector3(13f, 0f, 0f));
            host.Simulate(12000f, 30f);
            guest.Simulate(12000f, 30f);
            Assert.AreEqual(SettlementStage.Town, guest.Stage);
            host.AbsorbFrom(guest);
            AssertCaps(host);
            Assert.AreEqual(2, host.VillageCount);
            Assert.AreEqual(2, host.CountOf(BuildingKind.Campfire), "both hearths stay");
            for (int i = 0; i < 200; i++) { host.StepGrowth(30f); AssertCaps(host); }
            Run(host, 5f, 0.1f);
            AssertCaps(host);
        }

        [Test]
        public void AbsorbFrom_CarriesTheGuestVillageOverWithTheLocalShiftAndEverybodyCelebrates()
        {
            var host = Make(4.2f, 216, "Host");
            var guest = Make(4.2f, 217, "Guest", position: new Vector3(8f, 0f, 1f));
            guest.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            host.Simulate(1500f);
            guest.Simulate(1500f);
            int hostBuildings = host.BuildingCount, guestBuildings = guest.BuildingCount;
            Assert.Greater(guestBuildings, 3);
            var world = new List<Vector3>();
            var yaws = new List<float>();
            for (int i = 0; i < guestBuildings; i++)
            {
                Vector2 p = guest.BuildingPositionOf(i);
                world.Add(guest.transform.TransformPoint(p.x, 0f, p.y));
                yaws.Add(guest.BuildingYawOf(i) + 90f);
            }
            Vector2 hostFirst = host.BuildingPositionOf(0);
            Run(host, 3f, 0.1f);
            Run(guest, 3f, 0.1f);
            int hostFolk = host.SettlerCount, guestFolk = guest.SettlerCount;
            Assert.Greater(guestFolk, 0);

            Vector2 shift = new Vector2(-1.5f, 0.25f);
            var hostLife = host.GetComponent<IslandLifeSystem>();
            hostLife.ShiftLocal(shift);
            Assert.AreEqual(hostFirst + shift, host.BuildingPositionOf(0), "the host's own buildings follow the recentring");
            hostLife.AbsorbFrom(guest.GetComponent<IslandLifeSystem>());

            Assert.AreEqual(0, guest.BuildingCount);
            Assert.AreEqual(0, guest.VillageCount);
            Assert.AreEqual(2, host.VillageCount, "two villages stay separate");
            AssertCaps(host);
            int carried = 0;
            for (int g = 0; g < world.Count; g++)
                for (int i = hostBuildings; i < host.BuildingCount; i++)
                {
                    Vector2 p = host.BuildingPositionOf(i);
                    Vector3 w = host.transform.TransformPoint(p.x, 0f, p.y);
                    if ((w - world[g]).magnitude > 1e-3f) continue;
                    carried++;
                    Assert.AreEqual(0f, Mathf.DeltaAngle(yaws[g], host.BuildingYawOf(i)), 1e-3f, "headings turn with the guest island");
                    Assert.AreEqual(1, host.BuildingVillageOf(i));
                }
            Assert.AreEqual(guestBuildings, carried, "every guest building kept its world position");
            Assert.AreEqual(hostBuildings + guestBuildings, host.BuildingCount);
            Assert.AreEqual(hostFolk + guestFolk, host.SettlerCount, "the guest's folk move over too");
            Assert.AreEqual(0, guest.SettlerCount);
            Assert.IsTrue(host.Celebrating);
            Run(host, 12f, 0.1f);
            int party = 0;
            for (int i = 0; i < host.SettlerCount; i++)
                if (host.SettlerStateOf(i) == SettlerState.Celebrate || host.SettlerStateOf(i) == SettlerState.Walk) party++;
            // The fake host surface ends at its own shore, so only the host's folk can actually walk to the party.
            Assert.GreaterOrEqual(party, hostFolk - 1);
            Run(host, host.celebrateTime + 5f, 0.1f);
            Assert.IsFalse(host.Celebrating);
            for (int i = 0; i < host.SettlerCount; i++) Assert.AreNotEqual(SettlerState.Celebrate, host.SettlerStateOf(i));
            AssertCaps(host);
        }
    }
}
