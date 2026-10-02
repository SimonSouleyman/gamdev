using System.Collections.Generic;
using System.Text.RegularExpressions;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class LifePhase5Tests
    {
        readonly List<GameObject> _objects = new();

        [SetUp]
        public void SetUp()
        {
            LifeLod.DistanceProvider = _ => 0f;
            // Summer: the v0.6.8 seasons layer (courtship in spring, huddles in winter) stays out of these checks.
            LifeEnvironment.SeasonProvider = () => 0.375f;
            LifeEnvironment.NightProvider = () => 0f;
            LifeEnvironment.WindProvider = null;
            LifeEnvironment.StormProvider = null;
            IslandLifeSystem.ResetSeasonReference();
        }

        [TearDown]
        public void TearDown()
        {
            LifeLod.DistanceProvider = null;
            LifeEnvironment.NightProvider = null;
            LifeEnvironment.WindProvider = null;
            LifeEnvironment.StormProvider = null;
            IslandLifeSystem.ResetSeasonReference();
            IslandLifeSystem.PushWind(true);
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        IslandLifeSystem Make(float radius, int seed, string name = "Isle", float beach = 0f, float height = 1f, int character = 0, int maxVerts = 60000)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            var surface = go.AddComponent<FakeIslandSurface>();
            surface.radius = radius;
            surface.beach = beach;
            surface.height = height;
            surface.character = character;
            var life = go.AddComponent<IslandLifeSystem>();
            life.seed = seed;
            life.maxVegetationVerts = maxVerts;
            go.SetActive(true);
            _objects.Add(go);
            return life;
        }

        static FakeIslandSurface Surface(IslandLifeSystem l) => l.GetComponent<FakeIslandSurface>();

        static float Lum(Color c) => 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;

        static void Run(IslandLifeSystem l, float seconds, float dt = 0.05f)
        {
            int n = Mathf.CeilToInt(seconds / dt);
            for (int i = 0; i < n; i++) l.Step(dt);
        }

        // ------------------------------------------------------------ palms and reeds

        [Test]
        public void Palms_OnlyOnBeachCellsAndReeds_OnlyAtTheWaterline_BothCapped()
        {
            var life = Make(14f, 501, "Beach", 6f);
            var s = Surface(life);
            int palms = life.CountOf(LifeKind.Palm), reeds = life.CountOf(LifeKind.Reed);
            Assert.Greater(palms, 3, "a beach island grows palms");
            Assert.Greater(reeds, 3, "a beach island grows reeds");
            Assert.LessOrEqual(palms, life.maxPalms);
            Assert.LessOrEqual(reeds, life.maxReeds);
            for (int i = 0; i < life.PlantCount; i++)
            {
                if (life.PlantDyingOf(i)) continue;
                float h = s.SampleHeight(life.PlantPositionOf(i));
                if (life.PlantKindOf(i) == LifeKind.Palm)
                {
                    Assert.GreaterOrEqual(h, life.palmMinHeight - 1e-4f, "palm " + i + " stands in the water");
                    Assert.LessOrEqual(h, life.palmMaxHeight + 1e-4f, "palm " + i + " stands inland");
                }
                else if (life.PlantKindOf(i) == LifeKind.Reed)
                {
                    Assert.LessOrEqual(h, life.reedMaxHeight + 1e-4f, "reed " + i + " stands above the waterline");
                    Assert.GreaterOrEqual(h, -0.03f - 1e-4f, "reed " + i + " stands in deep water");
                }
                else Assert.GreaterOrEqual(h, 0.15f - 1e-4f, "an ordinary plant on the wet beach");
            }

            // The succession rule: without fire or drowning palms and reeds never go away.
            life.Simulate(600f);
            Assert.GreaterOrEqual(life.CountOf(LifeKind.Palm), palms);
            Assert.GreaterOrEqual(life.CountOf(LifeKind.Reed), reeds);
            Assert.LessOrEqual(life.CountOf(LifeKind.Palm), life.maxPalms);
            Assert.LessOrEqual(life.CountOf(LifeKind.Reed), life.maxReeds);

            var flat = Make(10f, 502, "Flat");
            Assert.AreEqual(0, flat.CountOf(LifeKind.Palm), "a cliff-edged island without a beach has no palms");
            Assert.AreEqual(0, flat.CountOf(LifeKind.Reed));

            var big = Make(20f, 503, "Big", 8f);
            Assert.AreEqual(big.maxPalms, big.CountOf(LifeKind.Palm), "a long shoreline hits the palm cap");
            Assert.GreaterOrEqual(big.CountOf(LifeKind.Reed), reeds, "and grows more reeds");
            Assert.LessOrEqual(big.CountOf(LifeKind.Reed), big.maxReeds);
        }

        [Test]
        public void Palms_OnePerThreeShoreCells_ReedsOnePerTwo()
        {
            var life = Make(14f, 504, "Beach", 6f);
            var s = Surface(life);
            int shoreCells = 0, inland = 0;
            var save = life.Capture();
            for (int j = 0; j < save.nz; j++)
                for (int i = 0; i < save.nx; i++)
                {
                    var c = new Vector2(save.originX + (i + 0.5f) * save.cellSize, save.originZ + (j + 0.5f) * save.cellSize);
                    if (s.SampleHeight(c) <= 0.12f) { Assert.IsFalse(life.IsShoreCell(c)); continue; }
                    if (life.IsShoreCell(c)) shoreCells++;
                    else inland++;
                    if (c.magnitude < 6f) Assert.IsFalse(life.IsShoreCell(c), "the plateau centre is not shore");
                }
            int palms = life.CountOf(LifeKind.Palm), reeds = life.CountOf(LifeKind.Reed);
            Assert.Greater(shoreCells, 10);
            Assert.Greater(inland, shoreCells, "the shore is a ring, not the whole island");
            Assert.GreaterOrEqual(palms, shoreCells / 6, "roughly one palm per three shore cells: " + palms + " on " + shoreCells);
            Assert.LessOrEqual(palms, Mathf.Min(life.maxPalms, shoreCells / 2 + 1));
            Assert.GreaterOrEqual(reeds, shoreCells / 4, "roughly one reed cluster per two shore cells: " + reeds + " on " + shoreCells);
            Assert.LessOrEqual(reeds, Mathf.Min(life.maxReeds, shoreCells * 2 / 3 + 1));
        }

        // ------------------------------------------------------------ flowers

        [Test]
        public void Flowers_ComeInSixColoursAndThreeShapes_AndButterfliesStillFindThem()
        {
            var life = Make(12f, 505, "Meadow");
            var colours = new HashSet<int>();
            var shapes = new HashSet<int>();
            int flowers = 0;
            for (int i = 0; i < life.PlantCount; i++)
            {
                if (life.PlantKindOf(i) != LifeKind.Flower || life.PlantDyingOf(i)) continue;
                flowers++;
                int v = life.PlantVariantOf(i);
                Assert.Less(v, LifeMeshes.FlowerVariants);
                colours.Add(LifeMeshes.FlowerColour(v));
                shapes.Add(LifeMeshes.FlowerShape(v));
            }
            Assert.Greater(flowers, 12);
            Assert.GreaterOrEqual(colours.Count, 5, "a meadow shows at least five colours");
            Assert.AreEqual(3, shapes.Count, "single, cluster and tall flowers");
            for (int v = 0; v < LifeMeshes.FlowerVariants; v++)
            {
                var t = LifeMeshes.GetTemplate(LifeKind.Flower, v);
                Assert.Greater(t.vertices.Length, 0);
                Assert.LessOrEqual(t.vertices.Length, 60, "flower shape " + v + " stays cheap");
            }

            var rnd = new System.Random(3);
            Assert.IsTrue(life.TryRandomPlant(LifeKind.Flower, rnd, default, 0f, out var pos), "butterflies can still pick a flower");
            Assert.Less(life.NearestPlantDistance(LifeKind.Flower, pos), 1e-4f);

            // The colour comes from the cell, so the same island regrows the same meadow.
            var twin = Make(12f, 505, "Twin");
            Assert.AreEqual(life.PlantCount, twin.PlantCount);
            for (int i = 0; i < life.PlantCount; i++)
            {
                Assert.AreEqual(life.PlantKindOf(i), twin.PlantKindOf(i));
                Assert.AreEqual(life.PlantVariantOf(i), twin.PlantVariantOf(i));
            }
        }

        // ------------------------------------------------------------ fire scars

        [Test]
        public void FireScar_DarkensTheGroundThenFadesWhileShootsComeUp()
        {
            // A young volcanic island (stage <= 0.2 everywhere) with growth paused: the fire cannot spread to the
            // neighbours (they never pass stage 0.3), so the scar is exactly the one cell that was lit.
            var life = Make(8f, 506, "Scar", 0f, 1f, 1);
            float growth = life.growthRate;
            life.growthRate = 0f;
            var p = new Vector2(0.6f, 0.6f);
            Color before = life.GroundTintAt(p);
            Assert.AreEqual(0, life.ShootCount);
            Assert.IsTrue(life.IgniteAt(p));
            Assert.IsFalse(life.IgniteAt(p), "a burning cell is not lit twice");
            life.Tick(1f);
            Assert.IsTrue(life.FireNear(p, 0.1f));
            life.Simulate(40f);
            Assert.IsFalse(life.FireNear(p, 0.1f), "the fire is out after 40 life-seconds");
            life.GetStats(out _, out int burning, out _);
            Assert.AreEqual(0, burning, "and did not spread");
            Assert.Greater(life.BurnAt(p), 0.8f, "a fresh scar");
            Color burnt = life.GroundTintAt(p);
            Assert.Less(Lum(burnt), Lum(before) * 0.5f, "the scar is charcoal dark: " + burnt + " vs " + before);
            life.growthRate = growth;
            life.Simulate(20f);
            Assert.Greater(life.ShootCount, 0, "fresh shoots come up on the scar");
            Color mid = burnt;
            life.Simulate(280f);
            float midBurn = life.BurnAt(p);
            Assert.Less(midBurn, 0.8f);
            Assert.Greater(midBurn, 0.3f, "the scar fades slowly, over about two real minutes");
            mid = life.GroundTintAt(p);
            Assert.Greater(Lum(mid), Lum(burnt), "the ground lightens as the scar fades");
            life.Simulate(600f);
            Assert.Less(life.BurnAt(p), 1e-3f, "the scar is gone");
            Assert.Greater(Lum(life.GroundTintAt(p)), Lum(burnt) * 2f);
            Assert.AreEqual(0, life.ShootCount, "shoots have normalised");
        }

        // ------------------------------------------------------------ bloom

        [Test]
        public void Bloom_VariesAcrossCellsAndOverTime_AndRebuildsOnlyOnTheInterval()
        {
            var life = Make(12f, 507, "Bloom", 0f, 0.3f);
            life.Simulate(600f);
            var flowerCells = new List<Vector2>();
            for (int i = 0; i < life.PlantCount; i++)
                if (life.PlantKindOf(i) == LifeKind.Flower && !life.PlantDyingOf(i)) flowerCells.Add(life.PlantPositionOf(i));
            Assert.Greater(flowerCells.Count, 12);
            float min = 1f, max = 0f;
            var first = new List<float>();
            foreach (var p in flowerCells)
            {
                float b = life.BloomAt(p);
                Assert.GreaterOrEqual(b, 0f);
                Assert.LessOrEqual(b, 1f);
                min = Mathf.Min(min, b); max = Mathf.Max(max, b);
                first.Add(b);
            }
            Assert.Greater(max - min, 0.3f, "cells bloom at different phases");

            life.Simulate(life.bloomPeriod * life.timeScale * 0.5f);
            float moved = 0f;
            for (int i = 0; i < flowerCells.Count; i++) moved += Mathf.Abs(life.BloomAt(flowerCells[i]) - first[i]);
            Assert.Greater(moved / flowerCells.Count, 0.2f, "half a period later the bloom has moved on");

            // Settled island: the only mesh work left is the bloom, once per bloomInterval at most.
            life.Step(0f);
            int builds = life.MeshBuilds;
            Run(life, 10f);
            Assert.LessOrEqual(life.MeshBuilds - builds, Mathf.CeilToInt(10f / life.bloomInterval) + 2, "bloom rebuilds stay on the interval");
            Assert.GreaterOrEqual(life.MeshBuilds - builds, 1, "and the bloom does reach the mesh");
        }

        // ------------------------------------------------------------ tree growth

        [Test]
        public void Saplings_GrowToFullSizeOverTreeGrowTime()
        {
            var life = Make(8f, 508, "Young", 0f, 1f, 1);
            Assert.AreEqual(0, life.SaplingCount, "initial plants are full size");
            Assert.AreEqual(0, life.CountOf(LifeKind.Tree), "a young volcanic island starts without trees");
            life.Simulate(150f);
            Assert.Greater(life.CountOf(LifeKind.Tree), 0, "trees came in");
            Assert.Greater(life.SaplingCount, 0, "and are still growing");
            float youngest = 1f;
            for (int i = 0; i < life.PlantCount; i++)
                if (life.PlantKindOf(i) == LifeKind.Tree) youngest = Mathf.Min(youngest, life.PlantMaturityOf(i));
            Assert.Less(youngest, 0.7f);
            int verts = life.MeshVertexCount;
            life.Simulate(life.treeGrowTime + 60f);
            Assert.AreEqual(0, life.SaplingCount, "every tree is full size after treeGrowTime");
            Assert.GreaterOrEqual(life.CountOf(LifeKind.Tree), 1);

            // Rebuild cadence: growth reaches the mesh in growthRebuildStep steps, not every tick.
            var slow = Make(8f, 509, "Slow", 0f, 1f, 1);
            slow.Simulate(100f);
            Assert.Greater(slow.SaplingCount, 0);
            slow.Step(0f);
            int builds = slow.MeshBuilds;
            Run(slow, 4f);
            Assert.LessOrEqual(slow.MeshBuilds - builds, 4f / slow.meshInterval + 1);
        }

        // ------------------------------------------------------------ seasons

        [Test]
        public void Season_AdvancesWithLifeTime_RoundTrips_OldSavesStartAtZero()
        {
            var a = Make(6f, 510, "A");
            Assert.AreEqual(0f, a.Season);
            a.Simulate(a.seasonPeriod * a.timeScale * 0.25f, 10f);
            Assert.AreEqual(0.25f, a.Season, 0.01f);
            var late = Make(6f, 511, "Late");
            Assert.AreEqual(a.Season, late.Season, 1e-4f, "a newly populated island joins the running season");

            var json = JsonUtility.ToJson(a.Capture());
            StringAssert.Contains("\"season\"", json);
            var b = Make(6f, 512, "B");
            b.Restore(JsonUtility.FromJson<LifeSaveData>(json));
            Assert.AreEqual(a.Season, b.Season, 1e-5f);

            var oldJson = Regex.Replace(Regex.Replace(json, ",\"season\":[^,}]+", ""), "\"season\":[^,}]+,", "");
            StringAssert.DoesNotContain("\"season\"", oldJson);
            var c = Make(6f, 513, "C");
            c.Restore(JsonUtility.FromJson<LifeSaveData>(oldJson));
            Assert.AreEqual(0f, c.Season);

            a.Simulate(a.seasonPeriod * a.timeScale * 0.8f, 10f);
            Assert.AreEqual(0.05f, a.Season, 0.01f, "the season wraps");

            Color spring = IslandLifeSystem.SeasonColour(new[] { Color.white, Color.white, Color.white, Color.white }, 0.3f, 1f);
            Assert.AreEqual(Color.white, spring);
            var seen = new HashSet<string>();
            for (float s = 0f; s < 1f; s += 0.05f)
            {
                var m = Make(4f, 600 + (int)(s * 100f), "S" + s);
                var f = m.GetType().GetField("_season", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                f.SetValue(m, s);
                Color g = m.GroundSeasonColour, t = m.TreeSeasonColour;
                foreach (var ch in new[] { g.r, g.g, g.b, t.r, t.g, t.b })
                {
                    Assert.GreaterOrEqual(ch, 0.75f, "season amplitude stays subtle");
                    Assert.LessOrEqual(ch, 1.25f);
                }
                seen.Add(g.ToString());
            }
            Assert.Greater(seen.Count, 10, "the ground tint drifts through the year");
            var spring0 = Make(4f, 700, "Spring");
            Assert.AreNotEqual(spring0.GroundSeasonColour, IslandLifeSystem.SeasonColour(new[] { new Color(1.16f, 1.02f, 0.72f) }, 0f, 1f));
        }

        // ------------------------------------------------------------ wind sway data

        [Test]
        public void Sway_DataOnVegetationVertices_NoneOnRigidBatches_WindGlobalFollowsProviders()
        {
            var life = Make(8f, 514, "Sway", 3f);
            var mesh = life.VegetationMesh;
            Assert.IsNotNull(mesh);
            var uv = new List<Vector4>();
            mesh.GetUVs(0, uv);
            Assert.AreEqual(mesh.vertexCount, uv.Count, "every vegetation vertex carries the sway channel");
            float maxW = 0f;
            var phases = new HashSet<float>();
            foreach (var u in uv)
            {
                Assert.GreaterOrEqual(u.x, 0f);
                Assert.LessOrEqual(u.x, 1f + 1e-4f);
                maxW = Mathf.Max(maxW, u.x);
                if (u.x > 0f) phases.Add(u.y);
            }
            Assert.Greater(maxW, 0.9f, "tops of grass and palms sway fully");
            Assert.Greater(phases.Count, 5, "plants get their own phase");
            Assert.AreEqual(0f, LifeMeshes.Material.GetFloat("_Sway"), "the legacy alpha sway is off for vegetation");

            var rigid = new TemplateBatch();
            rigid.Begin();
            rigid.Add(LifeMeshes.GetTemplate(LifeKind.Bird, 0), Vector3.zero, 0f, 1f, 0f, 0f, true);
            rigid.Add(LifeMeshes.GetTemplate(LifeKind.Bush, 0), Vector3.one, 0f, 1f);
            var rm = new Mesh();
            rigid.Apply(rm);
            var ruv = new List<Vector4>();
            rm.GetUVs(0, ruv);
            Assert.AreEqual(0, ruv.Count, "a flock or border batch has no sway channel and stays rigid");
            Object.DestroyImmediate(rm);
            Assert.AreEqual("Drift/Animal", LifeMeshes.AnimalMaterial.shader.name, "herds are drawn by the animal shader, which ignores wind");

            IslandLifeSystem.PushWind(true);
            Assert.AreEqual(LifeEnvironment.DefaultWind.x, IslandLifeSystem.LastWind.x, 1e-5f, "without a provider the default breeze blows");
            Assert.AreEqual(0f, IslandLifeSystem.LastWind.z);
            LifeEnvironment.WindProvider = () => new Vector2(1f, 0f);
            LifeEnvironment.StormProvider = () => 1f;
            IslandLifeSystem.PushWind(true);
            Assert.AreEqual(2.5f, IslandLifeSystem.LastWind.x, 1e-5f, "a full storm multiplies the wind by 2.5");
            Assert.AreEqual(0f, IslandLifeSystem.LastWind.y, 1e-5f);
            Assert.AreEqual(1f, IslandLifeSystem.LastWind.z, 1e-5f);
        }

        // ------------------------------------------------------------ budget, merges, idempotence

        [Test]
        public void Vegetation_StaysUnderTheVertexBudget_CapsHoldAfterMerge_RepopulateIdempotent()
        {
            var host = Make(20f, 515, "Host", 8f);
            var guest = Make(20f, 516, "Guest", 8f);
            host.Simulate(300f);
            guest.Simulate(300f);
            Assert.LessOrEqual(host.MeshVertexCount, 60000);
            Assert.LessOrEqual(host.LiveVertexEstimate, host.maxVegetationVerts);
            Assert.Greater(host.PlantCount, 300);
            Assert.AreEqual(1, host.GridBuilds);
            host.Repopulate();
            Assert.AreEqual(1, host.GridBuilds, "Repopulate stays a no-op for the same Version");

            host.AbsorbFrom(guest);
            Assert.AreEqual(0, guest.PlantCount);
            Surface(host).version++;
            host.Tick(1f);
            Assert.LessOrEqual(host.CountOf(LifeKind.Palm), host.maxPalms, "palm cap holds after a merge");
            Assert.LessOrEqual(host.CountOf(LifeKind.Reed), host.maxReeds, "reed cap holds after a merge");
            for (int i = 0; i < 12; i++) host.Step(0.15f);
            Assert.LessOrEqual(host.MeshVertexCount, 60000 + 42);

            var tight = Make(14f, 517, "Tight", 6f, 1f, 0, 3000);
            tight.Simulate(300f);
            Assert.LessOrEqual(tight.LiveVertexEstimate, 3000);
            Assert.LessOrEqual(tight.MeshVertexCount, 3000 + 42, "spawns stop at the vertex budget");
            Assert.Greater(tight.PlantCount, 20);
        }

        [Test]
        public void Templates_PalmAndReedAreCheap()
        {
            for (int v = 0; v < LifeMeshes.Variants; v++)
            {
                Assert.LessOrEqual(LifeMeshes.GetTemplate(LifeKind.Palm, v).vertices.Length, 36);
                Assert.LessOrEqual(LifeMeshes.GetTemplate(LifeKind.Reed, v).vertices.Length, 48);
                float top = 0f;
                foreach (var p in LifeMeshes.GetTemplate(LifeKind.Palm, v).vertices) top = Mathf.Max(top, p.y);
                Assert.Greater(top, 0.9f, "a palm towers over a bush");
            }
            Assert.AreEqual(LifeMeshes.PlantKinds, LifeMeshes.PlantSlot(LifeKind.Reed) + 1);
            Assert.AreEqual(LifeKind.Palm, LifeMeshes.PlantKindOfSlot(LifeMeshes.PlantSlot(LifeKind.Palm)));
            Assert.AreEqual((int)LifeKind.Tree, LifeMeshes.PlantSlot(LifeKind.Tree), "the old plant slots did not move");
        }
    }
}
