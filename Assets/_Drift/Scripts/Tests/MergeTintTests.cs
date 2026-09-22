using System.Collections.Generic;
using Drift.Core;
using Drift.Islands;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Island.MergeFrom drops the terrain's vertex colours (the merged mesh has a new vertex count) and rebuilds
    // the mesh white, which the terrain shader draws as raw _Grass on every cell - and, since the shader reads its
    // ground texture back from the tint, with the wrong texture too. The tint has to be back before that mesh is
    // first drawn, not at the next life tick.
    public class MergeTintTests
    {
        readonly List<GameObject> _objects = new();
        System.Func<Vector3, float> _oldDistance;

        [SetUp]
        public void SetUp()
        {
            _oldDistance = LifeLod.DistanceProvider;
            LifeLod.DistanceProvider = _ => 0f;
        }

        [TearDown]
        public void TearDown()
        {
            LifeLod.DistanceProvider = _oldDistance;
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        Island Make(Vector2 pos, int shapeSeed, float radius, int lifeSeed)
        {
            var go = new GameObject("MergeTint_" + shapeSeed);
            go.SetActive(false);
            go.transform.position = new Vector3(pos.x, 0f, pos.y);
            var isl = go.AddComponent<Island>();
            isl.useKeyboardInput = false;
            isl.sinkEnabled = false;
            isl.landRadius = radius;
            isl.shapeSeed = shapeSeed;
            isl.carryResponse = 0f;
            var life = go.AddComponent<IslandLifeSystem>();
            life.seed = lifeSeed;
            go.SetActive(true);
            _objects.Add(go);
            return isl;
        }

        // A plain island of that biome: volcanic and barren ground carries a whole-island tint factor on top of the
        // biome palette (CellColor's Character), which would blur the red-versus-blue test below.
        static int SeedOf(LifeBiome biome)
        {
            for (int s = 1; s < 5000; s++)
                if (Island.BiomeForSeed(s) == biome && Island.KindForSeed(s) == IslandKind.Regular) return s;
            return 1;
        }

        // Host = temperate, guest = nordic: the two ground palettes are on opposite sides of red-versus-blue
        // (temperate always r > b, nordic always b > r), and untinted white sits exactly between them.
        int _buildsBeforeMerge;

        Island StageMerge(out IslandLifeSystem life, out Vector2 guestCentre)
        {
            var host = Make(Vector2.zero, SeedOf(LifeBiome.Temperate), 8f, 11);
            var guest = Make(new Vector2(11.5f, 0f), SeedOf(LifeBiome.Nordic), 5f, 12);
            life = host.GetComponent<IslandLifeSystem>();
            life.Simulate(400f, 10f);
            guest.GetComponent<IslandLifeSystem>().Simulate(400f, 10f);

            var worldGo = new GameObject("MergeTint_World");
            _objects.Add(worldGo);
            var world = worldGo.AddComponent<IslandWorld>();
            var list = new List<Island> { host, guest };
            Vector3 guestWorld = guest.transform.position;

            _buildsBeforeMerge = life.GridBuilds;
            bool merged = false;
            for (int i = 0; i < 200 && !merged; i++) merged = world.Step(0.05f, list);
            Assert.IsTrue(merged, "the two islands never merged");

            Vector3 g = host.transform.InverseTransformPoint(guestWorld);
            guestCentre = new Vector2(g.x, g.z);
            return host;
        }

        static Mesh TerrainMesh(Island isl)
        {
            var mf = isl.GetComponent<MeshFilter>();
            Assert.IsTrue(mf != null && mf.sharedMesh != null, "the island has no terrain mesh");
            return mf.sharedMesh;
        }

        // True while the four samples a couple of cells away all share the cell's biome: the tint is blended
        // across a biome border on purpose, so only the interior of each half is asserted on.
        static bool Inside(IslandLifeSystem life, Vector2 p, int biome)
        {
            float d = life.cellSize * 2f;
            return life.BiomeAt(p + new Vector2(d, 0f)) == biome && life.BiomeAt(p - new Vector2(d, 0f)) == biome
                && life.BiomeAt(p + new Vector2(0f, d)) == biome && life.BiomeAt(p - new Vector2(0f, d)) == biome;
        }

        [Test]
        public void Merge_TheNewLandIsTintedBeforeTheFirstFrameIsDrawn_AndNeverShowsTheHostsGround()
        {
            var host = StageMerge(out var life, out Vector2 guestCentre);

            // One life frame: shorter than tickInterval, so the old code had not re-gridded yet and the terrain
            // still carried the white colours Island.RebuildMesh left behind.
            life.Step(1f / 60f);
            AssertEachHalfKeepsItsOwnGround(host, life, guestCentre);
        }

        // In the game the merge frame's own LateUpdate gets there first, before anything is rendered; drive exactly
        // that callback so the net itself is covered and not only the Update that would follow it.
        [Test]
        public void Merge_TheEndOfFrameNetTintsTheNewLandWithoutASingleUpdate()
        {
            var host = StageMerge(out var life, out Vector2 guestCentre);

            var lateUpdate = typeof(IslandLifeSystem).GetMethod("LateUpdate",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(lateUpdate, "IslandLifeSystem has no LateUpdate: a merge stays untinted until its next tick");
            lateUpdate.Invoke(life, null);

            AssertEachHalfKeepsItsOwnGround(host, life, guestCentre);
        }

        void AssertEachHalfKeepsItsOwnGround(Island host, IslandLifeSystem life, Vector2 guestCentre)
        {
            Assert.AreEqual((int)LifeBiome.Nordic, life.BiomeAt(guestCentre), "the absorbed land lost its biome");
            Color guestTint = life.GroundTintAt(guestCentre);
            Color hostTint = life.GroundTintAt(new Vector2(-5f, 0f));
            Assert.Greater(guestTint.b, guestTint.r * 1.5f, "the absorbed land is not tinted nordic yet: " + guestTint);
            Assert.Greater(hostTint.r, hostTint.b * 1.04f, "the host's own ground is not tinted temperate: " + hostTint);

            var mesh = TerrainMesh(host);
            var verts = mesh.vertices;
            var colors = mesh.colors;
            Assert.AreEqual(verts.Length, colors.Length, "the terrain mesh carries no vertex colours");

            int nordic = 0, temperate = 0, white = 0, wrongHalf = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                // Well above the waterline: the shore vertices blend with the white of the open sea cells.
                if (verts[i].y < 0.5f) continue;
                var p = new Vector2(verts[i].x, verts[i].z);
                int b = life.BiomeAt(p);
                if (!Inside(life, p, b)) continue;
                // Every nordic ground tint is blue over red by at least 1.77, every temperate one red over blue by
                // at least 1.09 (BiomeSpec), and the untinted white the merge leaves behind is exactly 1 either
                // way - so this separates "the guest's own ground", "the host's ground" and "no tint at all".
                Color c = colors[i];
                if (c == Color.white) white++;
                if (b == (int)LifeBiome.Nordic)
                {
                    nordic++;
                    if (c.b <= c.r * 1.5f) wrongHalf++;
                }
                else if (b == (int)LifeBiome.Temperate)
                {
                    temperate++;
                    if (c.r <= c.b * 1.04f) wrongHalf++;
                }
            }

            Assert.Greater(nordic, 30, "no absorbed land to check");
            Assert.Greater(temperate, 100, "no host land to check");
            Assert.AreEqual(0, white, "the merged terrain is still drawn untinted (raw _Grass) on " + white + " vertices");
            Assert.AreEqual(0, wrongHalf, wrongHalf + " of " + (nordic + temperate) + " land vertices carry the other half's ground colour");
        }

        [Test]
        public void Merge_TheEarlyTintCostsNoExtraGridBuild()
        {
            StageMerge(out var life, out _);

            // Exactly one re-grid for the merge - Island.MergeFrom asks for it straight away (so the first drawn
            // frame is tinted), and no tick afterwards repeats it.
            Assert.AreEqual(_buildsBeforeMerge + 1, life.GridBuilds, "the merge did not re-grid once");
            life.Step(1f / 60f);
            for (int i = 0; i < 30; i++) life.Step(1f / 60f);
            Assert.AreEqual(_buildsBeforeMerge + 1, life.GridBuilds, "the merged island was re-gridded a second time");
        }
    }
}
