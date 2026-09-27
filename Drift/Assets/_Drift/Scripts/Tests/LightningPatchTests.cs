using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Owner 2026-09-25: a lightning strike burns a patch of the island, never the whole island.
    public class LightningPatchTests
    {
        readonly List<GameObject> _objects = new();

        [SetUp]
        public void SetUp()
        {
            LifeLod.DistanceProvider = _ => 0f;
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

        // Ancient islands start as old growth everywhere: one unbroken dry stand, the worst case for a fire.
        IslandLifeSystem Make(float radius, int seed, int character = 2)
        {
            var go = new GameObject("PatchIsle");
            go.SetActive(false);
            var surface = go.AddComponent<FakeIslandSurface>();
            surface.radius = radius;
            surface.character = character;
            var life = go.AddComponent<IslandLifeSystem>();
            life.seed = seed;
            life.maxVegetationVerts = 8000;
            go.SetActive(true);
            _objects.Add(go);
            return life;
        }

        // Every land cell centre of the life grid (the grid starts at floor(-r / cell) * cell).
        static List<Vector2> Cells(IslandLifeSystem life, float radius)
        {
            var list = new List<Vector2>();
            float cs = life.cellSize;
            float o = Mathf.Floor(-radius / cs) * cs;
            int n = Mathf.CeilToInt((radius - o) / cs) + 1;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    var p = new Vector2(o + (i + 0.5f) * cs, o + (j + 0.5f) * cs);
                    if (p.magnitude < radius) list.Add(p);
                }
            return list;
        }

        static int Burning(IslandLifeSystem life)
        {
            life.GetStats(out _, out int burning, out _);
            return burning;
        }

        static void BurnOut(IslandLifeSystem life, float step = 1.2f, float max = 600f)
        {
            for (float t = 0f; t < max && Burning(life) > 0; t += step) life.Tick(step);
        }

        static int Scarred(IslandLifeSystem life, List<Vector2> cells)
        {
            int n = 0;
            foreach (var c in cells) if (life.BurnAt(c) > 0.5f) n++;
            return n;
        }

        // Connected groups (8-neighbourhood) of scarred cells.
        static int Patches(IslandLifeSystem life, List<Vector2> cells)
        {
            var scar = new List<Vector2>();
            foreach (var c in cells) if (life.BurnAt(c) > 0.5f) scar.Add(c);
            var seen = new bool[scar.Count];
            float link = life.cellSize * 1.5f;
            int groups = 0;
            var stack = new Stack<int>();
            for (int s = 0; s < scar.Count; s++)
            {
                if (seen[s]) continue;
                groups++;
                seen[s] = true;
                stack.Push(s);
                while (stack.Count > 0)
                {
                    int a = stack.Pop();
                    for (int b = 0; b < scar.Count; b++)
                        if (!seen[b] && (scar[a] - scar[b]).magnitude < link) { seen[b] = true; stack.Push(b); }
                }
            }
            return groups;
        }

        [Test]
        public void OneStrike_BurnsOnlyAPatchAndDiesOut()
        {
            const float r = 13f;
            float maxShare = 0f, allowed = 1f;
            for (int seed = 0; seed < 4; seed++)
            {
                var life = Make(r, 700 + seed);
                var cells = Cells(life, r);
                int ign = life.IgnitionCount;
                Assert.IsTrue(life.StrikeAt(new Vector2(1f, -2f)));
                Assert.AreEqual(1, life.StrikeCount);
                Assert.AreEqual(ign + 1, life.IgnitionCount, "old growth catches fire");
                BurnOut(life);
                Assert.AreEqual(0, Burning(life), "the fire dies out on its own");
                int scar = Scarred(life, cells);
                Assert.GreaterOrEqual(scar, 1);
                Assert.LessOrEqual(scar, life.LastFireBudget, "never more than the strike's budget");
                Assert.AreEqual(life.LastFireBurned, scar);
                Assert.LessOrEqual(life.LastFireBudget, life.firePatchCells.y);
                Assert.AreEqual(1, Patches(life, cells), "one strike, one patch");
                Assert.AreEqual(ign + 1, life.IgnitionCount, "spreading is not a new lightning fire (journal: Blitzfeuer gesehen)");
                maxShare = Mathf.Max(maxShare, scar / (float)cells.Count);
                allowed = life.firePatchShare * 1.3f + 0.01f;
                // Everything burnt lies within the patch radius (stretched downwind) of the strike.
                float reach = life.LastFireRadius * (1f + life.fireWindStretch) + life.cellSize;
                foreach (var c in cells)
                    if (life.BurnAt(c) > 0.5f) Assert.Less((c - new Vector2(1f, -2f)).magnitude, reach);
                _objects.Remove(life.gameObject);
                Object.DestroyImmediate(life.gameObject);
            }
            Assert.LessOrEqual(maxShare, allowed, "a strike takes a patch, not the island: " + maxShare);
        }

        [Test]
        public void SeveralStrikes_BurnSeveralSeparatePatches()
        {
            const float r = 14f;
            var life = Make(r, 811);
            var cells = Cells(life, r);
            Assert.IsTrue(life.StrikeAt(new Vector2(-8f, 0f)));
            int a = life.LastFireBudget;
            Assert.IsTrue(life.StrikeAt(new Vector2(8f, 1f)));
            int b = life.LastFireBudget;
            Assert.IsTrue(life.StrikeAt(new Vector2(0f, 9f)));
            int c = life.LastFireBudget;
            BurnOut(life);
            int scar = Scarred(life, cells);
            Assert.LessOrEqual(scar, a + b + c);
            Assert.AreEqual(3, Patches(life, cells), "three strikes far apart leave three scars");
            Assert.Less(scar, cells.Count * 0.5f, "most of the island still stands");
            Assert.AreEqual(3, life.IgnitionCount);
        }

        [Test]
        public void StormRain_DampsTheSpread()
        {
            const float r = 13f;
            int dry = 0, wet = 0;
            for (int seed = 0; seed < 4; seed++)
            {
                foreach (bool rain in new[] { false, true })
                {
                    var life = Make(r, 900 + seed);
                    // A generous budget so the rain, not the cap, decides.
                    life.firePatchShare = 0.3f;
                    life.firePatchCells = new Vector2Int(3, 200);
                    life.lightningRate = 0f;
                    life.GetComponent<FakeIslandSurface>().storm = rain ? 1f : 0f;
                    life.StrikeAt(Vector2.zero);
                    BurnOut(life);
                    int n = Scarred(life, Cells(life, r));
                    if (rain) wet += n; else dry += n;
                    _objects.Remove(life.gameObject);
                    Object.DestroyImmediate(life.gameObject);
                }
            }
            Assert.Less(wet, dry, "rain in the storm keeps fires smaller");
        }

        [Test]
        public void Grass_BurnsOutFasterThanWoods()
        {
            var woods = Make(5f, 31, 2);
            var grass = Make(5f, 31, 3);
            woods.growthRate = grass.growthRate = 0f;
            woods.fireSpreadRate = grass.fireSpreadRate = 0f;
            var p = new Vector2(0.6f, 0.6f);
            Assert.Greater(woods.StageAt(p), 0.9f);
            Assert.Less(grass.StageAt(p), 0.36f);
            Assert.IsTrue(woods.IgniteAt(p));
            Assert.IsTrue(grass.IgniteAt(p));
            for (int t = 0; t < 12; t++) { woods.Tick(1f); grass.Tick(1f); }
            Assert.AreEqual(0, Burning(grass), "a grass fire is over quickly");
            Assert.Greater(Burning(woods), 0, "a stand of trees burns on");
            BurnOut(woods);
            Assert.AreEqual(0, Burning(woods));
        }

        [Test]
        public void Strike_OnBareGround_OnlyFlashes()
        {
            var life = Make(6f, 12, 3);
            life.growthRate = 0f;
            // Barren ground at init stage <= 0.35: pick a cell that is below the burn threshold.
            Vector2 bare = Vector2.zero;
            bool found = false;
            foreach (var c in Cells(life, 6f))
                if (life.StageAt(c) <= 0.3f) { bare = c; found = true; break; }
            Assert.IsTrue(found);
            Assert.IsTrue(life.StrikeAt(bare));
            Assert.AreEqual(1, life.StrikeCount);
            Assert.AreEqual(0, life.IgnitionCount);
            Assert.IsTrue(life.StrikeVisible);
            Assert.IsFalse(life.StrikeAt(new Vector2(50f, 50f)), "no strike off the island");
        }

        [Test]
        public void Wind_CarriesTheFireDownwind()
        {
            const float r = 13f;
            float down = 0f;
            for (int seed = 0; seed < 4; seed++)
            {
                LifeEnvironment.WindProvider = () => new Vector2(1.5f, 0f);
                var life = Make(r, 950 + seed);
                life.firePatchCells = new Vector2Int(20, 20);
                life.fireWindStretch = 1.2f;
                life.StrikeAt(Vector2.zero);
                BurnOut(life);
                foreach (var c in Cells(life, r))
                    if (life.BurnAt(c) > 0.5f) down += c.x;
                _objects.Remove(life.gameObject);
                Object.DestroyImmediate(life.gameObject);
            }
            Assert.Greater(down, 0f, "the scar leans downwind (+x)");
        }
    }
}
