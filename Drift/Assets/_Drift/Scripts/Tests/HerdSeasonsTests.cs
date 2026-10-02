using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The seasons layer (IslandHerdSystem.Seasons.cs): four seasons from LifeEnvironment.Season with their German names,
    // births faster in spring and slower in winter, the winter huddle at dusk that ends with the night or the season,
    // courtship plays in spring, hoarding meerkats in autumn, and the climate nudge towards the herd's home climate.
    public class HerdSeasonsTests
    {
        readonly List<GameObject> _objects = new();
        float _night, _season;

        [SetUp]
        public void SetUp()
        {
            _night = 0f;
            _season = -1f;
            LifeLod.DistanceProvider = _ => 0f;
            LifeEnvironment.NightProvider = () => _night;
            LifeEnvironment.SeasonProvider = () => _season;
            LifeEnvironment.TimeOfDayProvider = null;
            LifeEnvironment.PointOfInterest = null;
            LifeEnvironment.ViewDistanceProvider = null;
            LifeEnvironment.WindProvider = null;
            LifeEnvironment.StormProvider = null;
            IslandLifeSystem.ResetSeasonReference();
        }

        [TearDown]
        public void TearDown()
        {
            LifeLod.DistanceProvider = null;
            LifeEnvironment.NightProvider = null;
            LifeEnvironment.SeasonProvider = null;
            LifeEnvironment.TimeOfDayProvider = null;
            LifeEnvironment.PointOfInterest = null;
            LifeEnvironment.ViewDistanceProvider = null;
            IslandLifeSystem.ResetSeasonReference();
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        IslandHerdSystem Stage(LifeKind kind, int size, int seed)
        {
            var go = new GameObject("Seasons" + kind);
            go.SetActive(false);
            var s = go.AddComponent<FakeIslandSurface>();
            s.radius = 9f;
            s.height = 1.2f;
            go.AddComponent<IslandLifeSystem>().seed = seed;
            var herds = go.AddComponent<IslandHerdSystem>();
            herds.seed = seed;
            go.SetActive(true);
            _objects.Add(go);
            herds.ClearHerds();
            Assert.GreaterOrEqual(herds.AddHerd(kind, Vector2.zero, size), 0, "no ground for " + kind);
            herds.strollRate = herds.visitRate = herds.spreadRate = herds.signatureRate = 0f;
            herds.meetRate = 0f;
            herds.playRate = 0f;
            herds.behaviourRate = 0f;
            Run(herds, 0.5f);
            herds.behaviourRate = 1f;
            return herds;
        }

        static void Run(IslandHerdSystem herds, float seconds, float dt = 0.05f)
        {
            int n = Mathf.CeilToInt(seconds / dt);
            for (int i = 0; i < n; i++) herds.Step(dt);
        }

        static float MeanSpread(IslandHerdSystem herds)
        {
            float sum = 0f;
            Vector2 c = herds.HerdCenter(0);
            for (int m = 0; m < herds.HerdSize(0); m++) sum += (herds.AnimalPosition(0, m) - c).magnitude;
            return sum / Mathf.Max(1, herds.HerdSize(0));
        }

        [Test]
        public void SeasonOf_QuartersOfTheYearWithGermanNames()
        {
            Assert.AreEqual(-1, IslandHerdSystem.SeasonOf(-1f));
            Assert.AreEqual(0, IslandHerdSystem.SeasonOf(0f));
            Assert.AreEqual(0, IslandHerdSystem.SeasonOf(0.2499f));
            Assert.AreEqual(1, IslandHerdSystem.SeasonOf(0.25f));
            Assert.AreEqual(1, IslandHerdSystem.SeasonOf(0.4999f));
            Assert.AreEqual(2, IslandHerdSystem.SeasonOf(0.5f));
            Assert.AreEqual(2, IslandHerdSystem.SeasonOf(0.7499f));
            Assert.AreEqual(3, IslandHerdSystem.SeasonOf(0.75f));
            Assert.AreEqual(3, IslandHerdSystem.SeasonOf(0.9999f));
            Assert.AreEqual(0, IslandHerdSystem.SeasonOf(1f));
            Assert.AreEqual(1, IslandHerdSystem.SeasonOf(1.3f));
            Assert.AreEqual("Frühling", IslandHerdSystem.SeasonName(IslandHerdSystem.Spring));
            Assert.AreEqual("Sommer", IslandHerdSystem.SeasonName(IslandHerdSystem.Summer));
            Assert.AreEqual("Herbst", IslandHerdSystem.SeasonName(IslandHerdSystem.Autumn));
            Assert.AreEqual("Winter", IslandHerdSystem.SeasonName(IslandHerdSystem.Winter));
            Assert.AreEqual("", IslandHerdSystem.SeasonName(-1));
            _season = 0.6f;
            Assert.AreEqual("Herbst", IslandHerdSystem.CurrentSeasonName);
            var herds = Stage(LifeKind.Sheep, 5, 3);
            Assert.AreEqual(IslandHerdSystem.Autumn, herds.CurrentSeason);
        }

        [Test]
        public void GrowthFactor_FasterInSpringSlowerInWinter()
        {
            var herds = Stage(LifeKind.Sheep, 5, 4);
            float spring = herds.SeasonGrowthFactor(IslandHerdSystem.Spring);
            Assert.That(spring, Is.InRange(1.5f, 2f));
            Assert.AreEqual(1f, herds.SeasonGrowthFactor(IslandHerdSystem.Summer));
            Assert.AreEqual(1f, herds.SeasonGrowthFactor(IslandHerdSystem.Autumn));
            Assert.AreEqual(0.5f, herds.SeasonGrowthFactor(IslandHerdSystem.Winter), 1e-5f);
            Assert.AreEqual(1f, herds.SeasonGrowthFactor(-1));
        }

        [Test]
        public void WinterHuddle_StartsAtDuskAndEndsWithTheSeason()
        {
            var herds = Stage(LifeKind.Sheep, 7, 5);
            herds.winterHuddleChance = 1f;
            _season = 0.85f;
            Run(herds, 3f);
            Assert.AreEqual(HerdGoal.None, herds.GoalOf(0), "no huddle by day");
            float open = MeanSpread(herds);

            _night = 0.4f;
            Run(herds, 1f);
            Assert.AreEqual(HerdGoal.Huddle, herds.GoalOf(0), "winter dusk: the sheep huddle");
            Assert.AreEqual(1, herds.HuddlesStarted);
            Run(herds, 10f);
            Assert.AreEqual(HerdGoal.Huddle, herds.GoalOf(0));
            Assert.Less(MeanSpread(herds), open * 0.85f, "huddling herds stand closer together");

            _season = 0.1f;
            Run(herds, 1f);
            Assert.AreEqual(HerdGoal.None, herds.GoalOf(0), "spring ends the winter huddle");
            Run(herds, 30f);
            Assert.AreEqual(1, herds.HuddlesStarted, "one roll per evening");

            // Winter again, a new evening: and the morning ends it.
            _season = 0.9f;
            _night = 0f;
            Run(herds, 2f);
            _night = 0.45f;
            Run(herds, 2f);
            Assert.AreEqual(HerdGoal.Huddle, herds.GoalOf(0));
            _night = 0.05f;
            Run(herds, 1f);
            Assert.AreEqual(HerdGoal.None, herds.GoalOf(0), "dawn ends the huddle");
        }

        [Test]
        public void WinterHuddle_OnlyColdSensitiveSpeciesAndPenguins()
        {
            _season = 0.85f;
            var reindeer = Stage(LifeKind.Reindeer, 6, 6);
            var penguins = Stage(LifeKind.Penguin, 7, 7);
            var noSeasons = Stage(LifeKind.Sheep, 6, 8);
            reindeer.winterHuddleChance = penguins.winterHuddleChance = noSeasons.winterHuddleChance = 1f;
            Run(reindeer, 1f);
            Run(penguins, 1f);
            _night = 0.4f;
            Run(reindeer, 2f);
            Run(penguins, 2f);
            Assert.AreNotEqual(HerdGoal.Huddle, reindeer.GoalOf(0), "reindeer do not mind the cold");
            Assert.AreEqual(0, reindeer.HuddlesStarted);
            Assert.AreEqual(HerdGoal.Huddle, penguins.GoalOf(0), "penguins huddle because they like it");

            _season = -1f;
            Run(noSeasons, 3f);
            Assert.AreNotEqual(HerdGoal.Huddle, noSeasons.GoalOf(0), "without seasons nothing fires");
            Assert.AreEqual(0, noSeasons.HuddlesStarted);
        }

        [Test]
        public void Courtship_RaisesPlayStartsInSpring()
        {
            _season = 0.4f;
            var summer = Stage(LifeKind.Hare, 8, 9);
            summer.playRate = 1f;
            Run(summer, 240f, 0.1f);
            _season = 0.1f;
            var spring = Stage(LifeKind.Hare, 8, 9);
            spring.playRate = 1f;
            bool courted = false;
            int stale = 0, maxStale = 0;
            for (int i = 0; i < 2400; i++)
            {
                spring.Step(0.1f);
                if (spring.GoalOf(0) == HerdGoal.Court) courted = true;
                stale = spring.GoalOf(0) == HerdGoal.Court && spring.PlayingHerds == 0 ? stale + 1 : 0;
                maxStale = Mathf.Max(maxStale, stale);
            }
            Assert.AreEqual(0, summer.CourtshipsStarted);
            Assert.GreaterOrEqual(spring.CourtshipsStarted, 2, "spring courtship starts plays");
            Assert.LessOrEqual(spring.CourtshipsStarted, Mathf.CeilToInt(240f / spring.courtGap) + 1, "one courtship per herd per courtGap");
            Assert.Greater(spring.PlaysStarted, summer.PlaysStarted + 1, "more plays in spring");
            Assert.IsTrue(courted, "the herd shows the Court goal while it courts");
            Assert.LessOrEqual(maxStale, 1, "the Court goal ends with the play");
        }

        [Test]
        public void Hoarding_MeerkatsDigAtAStashInAutumnOnly()
        {
            _season = 0.4f;
            var summer = Stage(LifeKind.Meerkat, 8, 10);
            summer.hoardRate = 1f;
            Run(summer, 60f, 0.1f);
            Assert.AreEqual(0, summer.HoardsStarted);

            _season = 0.6f;
            var autumn = Stage(LifeKind.Meerkat, 8, 10);
            autumn.hoardRate = 1f;
            bool hoarding = false, dug = false, ended = false;
            for (int i = 0; i < 2400; i++)
            {
                autumn.Step(0.1f);
                bool now = autumn.GoalOf(0) == HerdGoal.Hoard;
                if (now) hoarding = true;
                if (now && autumn.CountActivity(AnimalActivity.Dig) > 0) dug = true;
                if (hoarding && !now) ended = true;
            }
            Assert.IsTrue(hoarding, "autumn meerkats go hoarding");
            Assert.IsTrue(dug, "one of them digs at the stash");
            Assert.IsTrue(ended, "the hoarding ends again");
        }

        [Test]
        public void ClimateNudge_ColdHerdMovesTowardsTheColdSide()
        {
            Assert.AreEqual(-1f, IslandHerdSystem.PreferredClimate((int)LifeBiome.Nordic));
            Assert.AreEqual(1f, IslandHerdSystem.PreferredClimate((int)LifeBiome.Tropical));
            Assert.AreEqual(1f, IslandHerdSystem.PreferredClimate((int)LifeBiome.Savanna));
            Assert.AreEqual(0f, IslandHerdSystem.PreferredClimate((int)LifeBiome.Temperate));

            // Cold in the west, hot in the east, as across the seam of an arctic and a tropical island.
            System.Func<Vector2, float> seam = p => Mathf.Clamp(p.x / 6f, -1f, 1f);
            Vector2 from = new Vector2(2f, 0.5f);
            Assert.IsTrue(IslandHerdSystem.TryClimateNudge(from, -1f, seam, null, 8f, 0.3f, out Vector2 cold));
            Assert.Less(cold.x, from.x - 2f, "the arctic herd heads for the cold side");
            Assert.LessOrEqual((cold - from).magnitude, 8.01f);
            Assert.IsTrue(IslandHerdSystem.TryClimateNudge(from, 1f, seam, null, 8f, 0.3f, out Vector2 hot));
            Assert.Greater(hot.x, from.x + 2f, "the tropical herd heads for the warm side");

            // Already at home, or no better climate within reach: no nudge.
            Assert.IsFalse(IslandHerdSystem.TryClimateNudge(new Vector2(-8f, 0f), -1f, seam, null, 8f, 0.3f, out _));
            Assert.IsFalse(IslandHerdSystem.TryClimateNudge(from, -1f, _ => 0f, null, 8f, 0.3f, out _));
            // Only ground the species may stand on counts.
            Assert.IsTrue(IslandHerdSystem.TryClimateNudge(from, -1f, seam, p => p.x >= -3f, 8f, 0.3f, out Vector2 kept));
            Assert.GreaterOrEqual(kept.x, -3f);
            Assert.Less(kept.x, from.x);
        }
    }
}
