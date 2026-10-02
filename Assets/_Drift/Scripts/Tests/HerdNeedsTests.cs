using System;
using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Drift.Tests
{
    // Needs layer (IslandHerdSystem.Needs.cs, "Futter und Wetter"): hunger rises and falls with grazing and is capped in
    // bouts, a hungry herd on bare ground walks to the greener patch and grazes there, hunger slows births but never
    // costs an animal, herds on hot land rest in the shade at noon (once) and everyone shelters in the rain, a storm gust
    // scatters a huddle briefly (never at night), fish eaters feed on their shore errands. A fake pasture stands in for
    // the vegetation grid so the tests do not depend on how IslandLifeSystem turns plants into food.
    public class HerdNeedsTests
    {
        readonly List<GameObject> _objects = new();
        float _night;

        sealed class FakePasture : IHerdPasture
        {
            public float baseFood;
            public readonly List<Vector3> patches = new();
            public readonly List<Vector2> trees = new();
            public float grazed;
            readonly Dictionary<Vector2Int, float> _left = new();
            const float Cell = 1.2f;

            static Vector2Int CellOf(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell));
            static Vector2 Centre(Vector2Int c) => new Vector2((c.x + 0.5f) * Cell, (c.y + 0.5f) * Cell);

            float Initial(Vector2Int c)
            {
                Vector2 m = Centre(c);
                float f = baseFood;
                foreach (var p in patches) if ((m - new Vector2(p.x, p.y)).magnitude <= p.z) f = Mathf.Max(f, 1f);
                return f;
            }

            float At(Vector2Int c) => _left.TryGetValue(c, out float f) ? f : Initial(c);

            public float FoodAt(Vector2 local, Diet diet) => At(CellOf(local));

            public float Graze(Vector2 local, Diet diet, float amount)
            {
                var c = CellOf(local);
                float f = At(c), eaten = Mathf.Min(f, amount);
                _left[c] = f - eaten;
                grazed += eaten;
                return eaten;
            }

            public bool TryFindFood(Vector2 from, Diet diet, float radius, float minFood, System.Random rnd, out Vector2 target)
            {
                target = from;
                var c0 = CellOf(from);
                int r = Mathf.CeilToInt(radius / Cell);
                float best = -1f;
                for (int j = -r; j <= r; j++)
                    for (int i = -r; i <= r; i++)
                    {
                        var c = new Vector2Int(c0.x + i, c0.y + j);
                        Vector2 m = Centre(c);
                        float d = (m - from).magnitude;
                        float f = At(c);
                        if (d > radius || f < minFood) continue;
                        float score = f - d * 0.01f;
                        if (score <= best) continue;
                        best = score;
                        target = m;
                    }
                return best >= 0f;
            }

            public float ShadeAt(Vector2 local)
            {
                foreach (var t in trees) if ((t - local).magnitude < 0.8f) return 1f;
                return 0f;
            }

            public bool TryFindShelter(Vector2 from, float radius, System.Random rnd, out Vector2 target)
            {
                target = from;
                float best = radius;
                bool any = false;
                foreach (var t in trees)
                {
                    float d = (t - from).magnitude;
                    if (d > best) continue;
                    best = d;
                    target = t;
                    any = true;
                }
                return any;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _night = 0f;
            SpeciesPool.Clear();
            LifeLod.DistanceProvider = _ => 0f;
            LifeEnvironment.NightProvider = () => _night;
            LifeEnvironment.TimeOfDayProvider = null;
            LifeEnvironment.RainProvider = null;
            LifeEnvironment.StormProvider = null;
            LifeEnvironment.PointOfInterest = null;
            LifeEnvironment.ViewDistanceProvider = null;
            IslandLifeSystem.ResetSeasonReference();
        }

        [TearDown]
        public void TearDown()
        {
            SpeciesPool.Clear();
            LifeLod.DistanceProvider = null;
            LifeEnvironment.NightProvider = null;
            LifeEnvironment.TimeOfDayProvider = null;
            LifeEnvironment.RainProvider = null;
            LifeEnvironment.PointOfInterest = null;
            LifeEnvironment.ViewDistanceProvider = null;
            IslandLifeSystem.ResetSeasonReference();
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        // A flat plateau (radius 9, height 1.6) or a small island with a sloping beach; one herd, the pasture installed,
        // the other wander patterns switched off so only the needs send the herd anywhere.
        IslandHerdSystem Make(FakePasture pasture, LifeKind kind, Vector2 at, int size, LifeBiome biome = LifeBiome.Temperate, bool beach = false)
        {
            var go = new GameObject("Needs" + kind);
            go.SetActive(false);
            var s = go.AddComponent<FakeIslandSurface>();
            s.radius = beach ? 6f : 9f;
            s.height = beach ? 1f : 1.6f;
            s.beach = beach ? 1.5f : 0f;
            s.biome = (int)biome;
            go.AddComponent<IslandLifeSystem>().seed = 2600 + (int)kind;
            var herds = go.AddComponent<IslandHerdSystem>();
            herds.seed = 2600 + (int)kind;
            go.SetActive(true);
            _objects.Add(go);
            herds.ClearHerds();
            Assert.GreaterOrEqual(herds.AddHerd(kind, at, size), 0, "no ground for " + kind);
            herds.strollRate = herds.visitRate = herds.spreadRate = herds.signatureRate = 0f;
            herds.meetRate = 0f;
            herds.playRate = 0f;
            herds.PastureOverride = pasture;
            herds.SetHerdHunger(0, 0f);
            return herds;
        }

        static void Run(IslandHerdSystem herds, float seconds, float dt = 0.05f)
        {
            int n = Mathf.CeilToInt(seconds / dt);
            for (int i = 0; i < n; i++) herds.Step(dt);
        }

        // Seconds until the condition held (stepping at 0.05 s), or -1 when it never did within maxSeconds.
        static float RunUntil(IslandHerdSystem herds, Func<bool> done, float maxSeconds)
        {
            for (float t = 0f; t < maxSeconds; t += 0.05f)
            {
                if (done()) return t;
                herds.Step(0.05f);
            }
            return done() ? maxSeconds : -1f;
        }

        [Test]
        public void Hunger_RisesWithoutFood_FallsInACappedGrazingBout()
        {
            var pasture = new FakePasture { baseFood = 0f };
            var herds = Make(pasture, LifeKind.Sheep, Vector2.zero, 8);
            herds.SetHerdHunger(0, 0.1f);
            Run(herds, 60f);
            Assert.AreEqual(0.1f + 60f / herds.hungerFullTime, herds.HerdHunger(0), 0.01f, "hunger rises at 1/hungerFullTime per second");

            pasture.baseFood = 1f;
            herds.SetHerdHunger(0, 0.8f);
            Assert.GreaterOrEqual(RunUntil(herds, () => herds.GoalOf(0) == HerdGoal.Graze, 90f), 0f, "a hungry herd on food never grazed");
            Assert.Greater(herds.CountActivity(AnimalActivity.Graze), 0, "the grown-ups show the grazing activity");
            Assert.AreEqual(1, herds.GrazeBouts);
            float start = herds.HerdHunger(0);
            float bout = RunUntil(herds, () => herds.GoalOf(0) != HerdGoal.Graze, herds.grazeBout.y + 3f);
            Assert.GreaterOrEqual(bout, 0f, "the grazing bout did not end within grazeBout");
            Assert.Less(herds.HerdHunger(0), start - herds.boutFeedRate * herds.grazeBout.x * 0.8f, "grazing did not still the hunger");
            Assert.Greater(pasture.grazed, 0f, "the herd took nothing from the pasture");
            Assert.AreEqual(0, herds.CountActivity(AnimalActivity.Graze), "the bout's activity ends with it");
            Assert.AreEqual(8, herds.AnimalCount);
        }

        [Test]
        public void HungryHerd_MigratesToTheGreenerPatch_AndGrazesThere()
        {
            var pasture = new FakePasture { baseFood = 0.1f };
            pasture.patches.Add(new Vector3(5f, 0f, 1.5f));
            var herds = Make(pasture, LifeKind.Sheep, new Vector2(-3f, 0f), 6);
            herds.SetHerdHunger(0, 0.7f);
            Assert.GreaterOrEqual(RunUntil(herds, () => herds.GoalOf(0) == HerdGoal.Migrate, 5f), 0f, "the hungry herd did not set off");
            Assert.AreEqual(1, herds.Migrations);
            Assert.GreaterOrEqual(RunUntil(herds, () => herds.GoalOf(0) == HerdGoal.Graze, 90f), 0f, "the herd never arrived to graze");
            Assert.AreEqual(1, herds.MigrationsArrived);
            Vector2 c = herds.HerdCenter(0);
            Assert.Greater(c.x, 2.5f, "the herd grazes on the patch, not where it started");
            Assert.GreaterOrEqual(pasture.FoodAt(c, Diet.Grass), 0.3f, "it stands on food");
            float before = herds.HerdHunger(0);
            Run(herds, 15f);
            Assert.Less(herds.HerdHunger(0), before - 0.1f, "grazing on the new patch stills the hunger");
            Assert.AreEqual(6, herds.AnimalCount);
        }

        [Test]
        public void Hunger_SlowsBirthsButNeverCostsAnAnimal()
        {
            var pasture = new FakePasture { baseFood = 0f };
            var herds = Make(pasture, LifeKind.Hare, Vector2.zero, 8);
            herds.SetHerdHunger(0, 0f);
            Assert.AreEqual(1f, herds.NeedsGrowthFactor(0), 1e-4f);
            herds.SetHerdHunger(0, 0.5f);
            Assert.AreEqual(1f - herds.hungerBirths * 0.5f, herds.NeedsGrowthFactor(0), 1e-4f);
            herds.SetHerdHunger(0, 1f);
            Assert.AreEqual(1f - herds.hungerBirths, herds.NeedsGrowthFactor(0), 1e-4f);
            Run(herds, 240f);
            Assert.IsTrue(herds.HerdStarving(0), "no food anywhere: the herd counts as starving (slower wander)");
            Assert.Greater(herds.FoodSearchesFailed, 0);
            Assert.AreEqual(0, herds.Migrations);
            Assert.AreEqual(8, herds.AnimalCount, "nobody dies of hunger");
            Assert.LessOrEqual(herds.HerdHunger(0), 1f);
        }

        [Test]
        public void Noon_HerdsOnHotLandRestInTheShade_OncePerNoon()
        {
            LifeEnvironment.TimeOfDayProvider = () => 0.5f;
            var pasture = new FakePasture { baseFood = 1f };
            pasture.trees.Add(new Vector2(3f, 2f));
            var herds = Make(pasture, LifeKind.Zebra, Vector2.zero, 6, LifeBiome.Savanna);
            Assert.GreaterOrEqual(RunUntil(herds, () => herds.GoalOf(0) == HerdGoal.Shade, 30f), 0f, "nobody looked for shade at noon");
            Assert.GreaterOrEqual(RunUntil(herds, () => herds.CountActivity(AnimalActivity.Shade) > 0, 40f), 0f, "the herd never reached the tree");
            Assert.Less((herds.HerdCenter(0) - new Vector2(3f, 2f)).magnitude, 0.5f, "it rests under the tree");
            Assert.GreaterOrEqual(RunUntil(herds, () => herds.GoalOf(0) != HerdGoal.Shade, herds.shelterStay.y + 5f), 0f, "the rest under the tree never ended");
            Run(herds, 60f);
            Assert.AreEqual(1, herds.ShadeTrips, "one shade rest per noon");

            // Nordic land: no shade-seeking even at noon.
            LifeEnvironment.TimeOfDayProvider = () => 0.5f;
            var cold = Make(pasture, LifeKind.Reindeer, new Vector2(-2f, -2f), 5, LifeBiome.Nordic);
            Run(cold, 60f);
            Assert.AreEqual(0, cold.ShadeTrips);
        }

        [Test]
        public void Rain_EveryoneSheltersUnderTrees()
        {
            LifeEnvironment.RainProvider = _ => 1f;
            var pasture = new FakePasture { baseFood = 1f };
            pasture.trees.Add(new Vector2(-3f, 1f));
            var herds = Make(pasture, LifeKind.Ox, Vector2.zero, 4);
            Assert.GreaterOrEqual(RunUntil(herds, () => herds.GoalOf(0) == HerdGoal.Shelter, 15f), 0f, "nobody sought shelter in the rain");
            Assert.GreaterOrEqual(RunUntil(herds, () => herds.CountActivity(AnimalActivity.Shelter) > 0, 60f), 0f, "the herd never reached the tree");
            Assert.Less((herds.HerdCenter(0) - new Vector2(-3f, 1f)).magnitude, 0.5f);
            Assert.AreEqual(1, herds.ShelterTrips);
            LifeEnvironment.RainProvider = null;
            Assert.GreaterOrEqual(RunUntil(herds, () => herds.GoalOf(0) == HerdGoal.None, herds.shelterStay.y + 5f), 0f);
            Assert.AreEqual(0, herds.CountActivity(AnimalActivity.Shelter));
        }

        [Test]
        public void StormGust_ScattersTheHuddleBriefly_ThenItRegroups_NeverAtNight()
        {
            var pasture = new FakePasture { baseFood = 1f };
            var herds = Make(pasture, LifeKind.Sheep, Vector2.zero, 8);
            herds.scatterChance = 1f;
            Run(herds, 2f);
            herds.Agitation = 1f;
            Assert.IsTrue(herds.Huddling);
            Assert.GreaterOrEqual(RunUntil(herds, () => herds.Scatters > 0, 5f), 0f, "the gust scattered nobody");
            float huddle = herds.BodyLength(0) * herds.formationSpacing * Mathf.Sqrt(8f) * 0.55f;
            float widest = 0f;
            for (int i = 0; i < 100; i++)
            {
                herds.Step(0.05f);
                widest = Mathf.Max(widest, herds.HerdRadius(0));
            }
            Assert.Greater(widest, huddle * 1.5f, "the herd did not spread out");
            Run(herds, herds.scatterTime.y + 6f);
            Assert.AreEqual(0, herds.CountActivity(AnimalActivity.Flee), "the scatter ended");
            Assert.Less(herds.HerdRadius(0), huddle * 1.4f, "the herd huddles together again");
            Assert.AreEqual(8, herds.AnimalCount);

            _night = 1f;
            var night = Make(pasture, LifeKind.Sheep, new Vector2(3f, 3f), 8);
            night.scatterChance = 1f;
            Run(night, 60f);
            night.Agitation = 1f;
            Run(night, 60f);
            Assert.AreEqual(0, night.Scatters, "a storm at night does not scatter sleeping herds");
        }

        [Test]
        public void FishEaters_FeedOnTheirShoreErrands()
        {
            var pasture = new FakePasture { baseFood = 0f };
            var herds = Make(pasture, LifeKind.ArcticFox, new Vector2(4.2f, 0f), 2, LifeBiome.Nordic, beach: true);
            herds.SetHerdHunger(0, 0.6f);
            Assert.GreaterOrEqual(RunUntil(herds, () => herds.FishTrips > 0, 5f), 0f, "the hungry foxes did not go to the shore");
            Assert.AreEqual(HerdGoal.Graze, herds.GoalOf(0));
            Assert.AreEqual(0, herds.Migrations, "fish eaters do not migrate for grass");
            Assert.GreaterOrEqual(RunUntil(herds, () => herds.GoalOf(0) == HerdGoal.None, 60f), 0f, "the shore errand never ended");
            Assert.Less(herds.HerdHunger(0), 0.6f - 0.15f, "feeding at the shore stilled the hunger");
            Assert.AreEqual(2, herds.AnimalCount);
        }
    }
}
