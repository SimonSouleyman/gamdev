using System.Collections.Generic;
using System.IO;
using System.Linq;
using Drift.Islands;
using Drift.SaveSystem;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class IslandArchetypeTests
    {
        static readonly IslandArchetype[] Streamed =
        {
            IslandArchetype.Blob, IslandArchetype.Ridge, IslandArchetype.Crescent, IslandArchetype.TwinPeak,
            IslandArchetype.Sandbank, IslandArchetype.Mesa, IslandArchetype.Archipelago, IslandArchetype.Stack
        };

        readonly List<GameObject> _objects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        static float MinRadius(IslandArchetype a) => a == IslandArchetype.Crescent ? 3f : 1.2f;

        static float LandArea(IslandShape s) => s.h.Count(v => v > 0f) * s.cell * s.cell;

        [Test]
        public void Classic_IsTheOriginalBlob()
        {
            var a = IslandArchetypes.Create(IslandArchetype.Classic, 5f, 11, 0.5f);
            var b = IslandShape.CreateBlob(5f, 11, 0.5f);
            Assert.AreEqual(b.origin, a.origin);
            Assert.AreEqual(b.h, a.h);
        }

        [Test]
        public void Create_IsDeterministicPerSeed_AndArchetypesDiffer()
        {
            foreach (var type in Streamed)
            {
                var a = IslandArchetypes.Create(type, 5f, 4711, 0.5f);
                var b = IslandArchetypes.Create(type, 5f, 4711, 0.5f);
                Assert.AreEqual(a.nx, b.nx, type.ToString());
                Assert.AreEqual(a.origin, b.origin, type.ToString());
                Assert.AreEqual(a.h, b.h, type.ToString());
                var c = IslandArchetypes.Create(type, 5f, 4712, 0.5f);
                Assert.That(a.h.SequenceEqual(c.h), Is.False, type + " ignores the seed");
            }
        }

        [Test]
        public void Heights_StayInsideWhatLifeAndTheTerrainShaderExpect()
        {
            foreach (var type in Streamed)
                foreach (float r in new[] { MinRadius(type), 4f, 12f })
                    for (int seed = 1; seed <= 6; seed++)
                    {
                        float cell = IslandArchetypes.CellSize(r);
                        var s = IslandArchetypes.Create(type, r, seed * 7919, cell);
                        string tag = $"{type} r={r} seed={seed}";
                        Assert.GreaterOrEqual(s.h.Min(), IslandShape.Sea - 1e-5f, tag);
                        Assert.LessOrEqual(s.h.Max(), IslandArchetypes.MaxPeak + 1e-5f, tag + ": snow belongs to volcanoes");
                        Assert.Greater(LandArea(s), 1f, tag + ": no land");
                        Assert.That(s.h.Any(v => v > 0.02f && v < 0.35f), tag + ": no beach band");
                        Assert.Less(s.LandCentroid().magnitude, 0.05f + cell, tag + ": origin is the land centroid");

                        float reach = 0f;
                        for (int j = 0; j < s.nz; j++)
                            for (int i = 0; i < s.nx; i++)
                                if (s.h[j * s.nx + i] > 0f) reach = Mathf.Max(reach, s.CellPos(i, j).magnitude);
                        Assert.LessOrEqual(reach, 1.4f * r + cell, tag + ": land outside the radius the streamer spaces by");
                    }
        }

        [Test]
        public void AreaFactor_MatchesTheGeneratedLand()
        {
            foreach (var type in Streamed)
            {
                const float r = 5f;
                const int seeds = 24;
                float sum = 0f;
                for (int seed = 1; seed <= seeds; seed++)
                    sum += LandArea(IslandArchetypes.Create(type, r, seed * 104729 + (int)type, 0.5f));
                float factor = sum / seeds / (Mathf.PI * r * r);
                Assert.AreEqual(IslandArchetypes.AreaFactor(type), factor, 0.15f * IslandArchetypes.AreaFactor(type), type.ToString());
            }
        }

        [Test]
        public void Shapes_AreDistinct_RidgeIsLong_CrescentIsHollow_StackIsTall()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                IslandArchetypes.Create(IslandArchetype.Ridge, 5f, seed, 0.5f).LandAxes(out _, out float ridge);
                IslandArchetypes.Create(IslandArchetype.Blob, 5f, seed, 0.5f).LandAxes(out _, out float blob);
                Assert.Greater(ridge, 0.6f, "ridge elongation, seed " + seed);
                Assert.Less(blob, 0.6f, "blob elongation, seed " + seed);

                var crescent = IslandArchetypes.Create(IslandArchetype.Crescent, 6f, seed, 0.5f);
                Assert.Less(crescent.Compactness(), IslandArchetypes.Create(IslandArchetype.Blob, 6f, seed, 0.5f).Compactness());

                var stack = IslandArchetypes.Create(IslandArchetype.Stack, 1.8f, seed, 0.25f);
                Assert.Greater(stack.h.Max(), 1.6f, "stack height, seed " + seed);
                Assert.Less(IslandArchetypes.Create(IslandArchetype.Sandbank, 4f, seed, 0.5f).h.Max(), 0.5f, "sandbank height");
            }
        }

        WorldStreamer MakeStreamer(int seed, int layout = WorldStreamer.WorldGenVersion)
        {
            var go = new GameObject("TestStreamer");
            go.SetActive(false);
            _objects.Add(go);
            var ws = go.AddComponent<WorldStreamer>();
            ws.seed = seed;
            ws.UseLayout(layout);
            ws.RestoreState(new Vector2(3f, 4f), null);
            return ws;
        }

        [Test]
        public void Plan_IsDeterministicPerWorldSeedAndSlot(
            [Values(WorldStreamer.WorldGenVersion, WorldStreamer.LegacyWorldGenVersion)] int layout)
        {
            var a = MakeStreamer(4242, layout).WorldSlots();
            var b = MakeStreamer(4242, layout).WorldSlots();
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].pos, b[i].pos);
                Assert.AreEqual(a[i].radius, b[i].radius);
                Assert.AreEqual(a[i].archetype, b[i].archetype);
            }
            var c = MakeStreamer(4243, layout).WorldSlots();
            Assert.That(a.Count != c.Count || Enumerable.Range(0, a.Count).Any(i => a[i].pos != c[i].pos));
        }

        [Test]
        // The big world of old saves (the small world has its own counts, CozyWorldTests).
        public void Plan_HasManyIslets_AFewLargeIslands_AndEveryArchetype()
        {
            var ws = MakeStreamer(777, WorldStreamer.LegacyWorldGenVersion);
            Assert.AreEqual(660f, ws.WorldSize);
            var slots = ws.WorldSlots();
            int tiny = slots.Count(s => s.radius < 2f);
            int large = slots.Count(s => s.radius >= 9f);
            Assert.Greater(slots.Count, 120);
            Assert.Greater(tiny, slots.Count / 4, "islets");
            Assert.That(large, Is.InRange(1, slots.Count / 10), "large islands");
            Assert.LessOrEqual(slots.Max(s => s.radius), 14f);
            foreach (var type in Streamed)
                Assert.That(slots.Any(s => s.archetype == type), type + " never planned");
            Assert.That(slots.All(s => s.archetype != IslandArchetype.Classic));
            Assert.That(slots.All(s => s.EstimatedArea > 0f));
        }

        [Test]
        public void Plan_KeepsIslandsApart_AlsoAcrossChunkAndWorldEdges(
            [Values(WorldStreamer.WorldGenVersion, WorldStreamer.LegacyWorldGenVersion)] int layout)
        {
            var ws = MakeStreamer(777, layout);
            var slots = ws.WorldSlots();
            float w = ws.WorldSize;
            for (int i = 0; i < slots.Count; i++)
                for (int j = i + 1; j < slots.Count; j++)
                {
                    Vector2 d = slots[i].pos - slots[j].pos;
                    d.x -= w * Mathf.Round(d.x / w);
                    d.y -= w * Mathf.Round(d.y / w);
                    Assert.Greater(d.magnitude, 1.4f * (slots[i].radius + slots[j].radius), $"slots {i} and {j}");
                }
        }

        [Test]
        public void Save_CarriesTheWorldGenVersion_OldLayoutsStayContinuable_UnknownOnesNot()
        {
            Assert.AreEqual(0, JsonUtility.FromJson<SaveGame>("{\"version\":5,\"worldSeed\":7}").worldGenVersion, "old files read 0");

            string path = Path.Combine(Application.temporaryCachePath, "drift_worldgen_test.json");
            try
            {
                var save = new SaveGame
                {
                    worldGenVersion = WorldStreamer.WorldGenVersion,
                    player = new IslandSaveData { nx = 1, nz = 1, heights = new[] { 1f } }
                };
                File.WriteAllText(path, JsonUtility.ToJson(save));
                Assert.IsTrue(SaveManager.Peek(path, out _, out _));

                // A world started before the small world keeps playing in its big layout.
                save.worldGenVersion = WorldStreamer.LegacyWorldGenVersion;
                File.WriteAllText(path, JsonUtility.ToJson(save));
                Assert.IsTrue(SaveManager.Peek(path, out _, out _));

                foreach (int unknown in new[] { 0, 1, WorldStreamer.WorldGenVersion + 1 })
                {
                    save.worldGenVersion = unknown;
                    File.WriteAllText(path, JsonUtility.ToJson(save));
                    Assert.IsFalse(SaveManager.Peek(path, out _, out _), "layout " + unknown);
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
