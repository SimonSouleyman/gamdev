using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class LifePhase2Tests
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

        IslandHerdSystem Make(float radius, int seed, string name = "Isle")
        {
            var go = new GameObject(name);
            go.SetActive(false);
            var surface = go.AddComponent<FakeIslandSurface>();
            surface.radius = radius;
            go.AddComponent<IslandLifeSystem>().seed = seed;
            var herds = go.AddComponent<IslandHerdSystem>();
            herds.seed = seed;
            go.SetActive(true);
            _objects.Add(go);
            return herds;
        }

        static void Run(IslandHerdSystem herds, float seconds, float dt = 0.05f)
        {
            int n = Mathf.CeilToInt(seconds / dt);
            for (int i = 0; i < n; i++) herds.Step(dt);
        }

        static HashSet<AnimalState> StatesSeen(IslandHerdSystem herds, float seconds, float dt = 0.05f)
        {
            var seen = new HashSet<AnimalState>();
            int n = Mathf.CeilToInt(seconds / dt);
            for (int i = 0; i < n; i++)
            {
                herds.Step(dt);
                for (int h = 0; h < herds.HerdCount; h++)
                    for (int m = 0; m < herds.HerdSize(h); m++) seen.Add(herds.AnimalStateOf(h, m));
            }
            return seen;
        }

        // ------------------------------------------------------------ state machine

        [Test]
        public void LifeEnvironment_FallsBackToDayWithoutProvider()
        {
            LifeEnvironment.NightProvider = null;
            Assert.AreEqual(0f, LifeEnvironment.NightAmount);
            LifeEnvironment.NightProvider = () => 0.75f;
            Assert.AreEqual(0.75f, LifeEnvironment.NightAmount);
        }

        [Test]
        public void Herds_DaytimeMixesGrazingLookingWalkingAndResting()
        {
            var herds = Make(8f, 41);
            Assert.Greater(herds.AnimalCount, 10);
            var seen = StatesSeen(herds, 240f);
            Assert.IsTrue(seen.Contains(AnimalState.Graze), "graze");
            Assert.IsTrue(seen.Contains(AnimalState.Look), "look up");
            Assert.IsTrue(seen.Contains(AnimalState.Walk), "walk");
            Assert.IsTrue(seen.Contains(AnimalState.Rest), "rest");
            Assert.IsFalse(seen.Contains(AnimalState.Sleep), "nobody sleeps by day");
            Assert.AreEqual(0, herds.SleepingCount);
        }

        [Test]
        public void Herds_StartGrazingByDayAndAsleepByNight()
        {
            var day = Make(8f, 42, "Day");
            for (int h = 0; h < day.HerdCount; h++)
                for (int m = 0; m < day.HerdSize(h); m++) Assert.AreEqual(AnimalState.Graze, day.AnimalStateOf(h, m));

            _night = 1f;
            var night = Make(8f, 42, "Night");
            Assert.Greater(night.SleepingFraction, 0.7f);
            for (int h = 0; h < night.HerdCount; h++)
                for (int m = 0; m < night.HerdSize(h); m++)
                    Assert.AreEqual(night.IsWatcher(h, m) ? AnimalState.Graze : AnimalState.Sleep, night.AnimalStateOf(h, m));
        }

        [Test]
        public void Herds_TimersOnlyAdvanceWithTime()
        {
            var herds = Make(8f, 43);
            Run(herds, 20f);
            int changes = herds.StateChanges, builds = herds.MeshBuilds;
            for (int i = 0; i < 50; i++) herds.Step(0f);
            Assert.AreEqual(changes, herds.StateChanges);
            Assert.AreEqual(builds, herds.MeshBuilds);
        }

        // ------------------------------------------------------------ night and dawn

        [Test]
        public void Herds_NightPutsMostAnimalsToSleepAndStopsWandering()
        {
            var herds = Make(8f, 44);
            Run(herds, 30f);
            _night = 1f;
            Run(herds, 120f);
            Assert.Greater(herds.SleepingFraction, 0.7f);
            int watchers = 0;
            for (int h = 0; h < herds.HerdCount; h++)
                for (int m = 0; m < herds.HerdSize(h); m++)
                {
                    bool watcher = herds.IsWatcher(h, m);
                    if (watcher) watchers++;
                    if (herds.AnimalStateOf(h, m) == AnimalState.Sleep) Assert.IsFalse(watcher, "a watcher fell asleep");
                }
            Assert.Greater(watchers, 0, "herds of six or more keep a watcher");
            Assert.AreEqual(0, herds.PlayingHerds);

            var centres = new List<Vector2>();
            for (int h = 0; h < herds.HerdCount; h++) centres.Add(herds.HerdCenter(h));
            Run(herds, 60f);
            for (int h = 0; h < herds.HerdCount; h++) Assert.AreEqual(centres[h], herds.HerdCenter(h), "herd " + h + " wandered at night");
        }

        [Test]
        public void Herds_DawnWakesEveryoneWithinTheStagger()
        {
            var herds = Make(8f, 45);
            _night = 1f;
            Run(herds, 120f);
            Assert.Greater(herds.SleepingFraction, 0.7f);
            _night = 0f;
            var seen = new HashSet<AnimalState>();
            int steps = Mathf.CeilToInt(20f / 0.05f);
            for (int i = 0; i < steps; i++)
            {
                herds.Step(0.05f);
                for (int h = 0; h < herds.HerdCount; h++)
                    for (int m = 0; m < herds.HerdSize(h); m++) seen.Add(herds.AnimalStateOf(h, m));
            }
            Assert.IsTrue(seen.Contains(AnimalState.Look), "waking animals stretch and look up");
            Assert.Greater(herds.SleepingCount, 0, "waking is staggered, not everyone is up after 20 s");
            Run(herds, herds.dawnStaggerMax + 5f);
            Assert.AreEqual(0, herds.SleepingCount);
            var centres = new List<Vector2>();
            for (int h = 0; h < herds.HerdCount; h++) centres.Add(herds.HerdCenter(h));
            Run(herds, 60f);
            bool moved = false;
            for (int h = 0; h < herds.HerdCount && !moved; h++) moved = herds.HerdCenter(h) != centres[h];
            Assert.IsTrue(moved, "herds wander again by day");
        }

        [Test]
        public void Herds_SleepersDoNotRebuildTheMesh()
        {
            var herds = Make(8f, 46);
            herds.watchersPerHerd = 0;
            _night = 1f;
            Run(herds, 150f);
            Assert.AreEqual(herds.AnimalCount, herds.SleepingCount);
            int builds = herds.MeshBuilds, changes = herds.StateChanges;
            Run(herds, 10f);
            Assert.AreEqual(changes, herds.StateChanges);
            Assert.AreEqual(builds, herds.MeshBuilds, "a sleeping herd must not rebuild");
        }

        [Test]
        public void Herds_IdleRebuildsOnlyOnStateChanges()
        {
            var herds = Make(8f, 47);
            herds.playRate = 0f;
            _night = 1f;
            Run(herds, 150f);
            int builds = herds.MeshBuilds, changes = herds.StateChanges;
            Run(herds, 30f);
            int newBuilds = herds.MeshBuilds - builds, newChanges = herds.StateChanges - changes;
            Assert.Greater(newChanges, 0, "watchers keep changing state");
            Assert.LessOrEqual(newBuilds, newChanges + 1);
            Assert.Less(newBuilds, 30f / herds.meshInterval * 0.25f, "an idle herd must stay far below the 15 Hz rebuild cap");
        }

        [Test]
        public void Herds_StartleWakesSleepersAndTheyReturnToSleep()
        {
            var herds = Make(8f, 48);
            _night = 1f;
            Run(herds, 120f);
            Assert.Greater(herds.SleepingFraction, 0.7f);
            herds.Startle(Vector2.zero, 3f);
            herds.Step(0.05f);
            Assert.AreEqual(0, herds.SleepingCount, "startled animals get up");
            Assert.Greater(herds.FleeingHerds, 0);
            Run(herds, 150f);
            Assert.Greater(herds.SleepingFraction, 0.7f, "the herd settles again after the scare");
        }

        [Test]
        public void Herds_StormHuddleStandsEveryoneUp()
        {
            var herds = Make(8f, 49);
            _night = 1f;
            Run(herds, 120f);
            Assert.Greater(herds.SleepingFraction, 0.7f);
            herds.Agitation = 1f;
            Run(herds, 2f);
            Assert.IsTrue(herds.Huddling);
            Assert.AreEqual(0, herds.RestingCount);
            for (int h = 0; h < herds.HerdCount; h++)
                for (int m = 0; m < herds.HerdSize(h); m++)
                    Assert.AreEqual(AnimalState.Look, herds.AnimalStateOf(h, m));
            herds.Agitation = 0f;
            Run(herds, 150f);
            Assert.Greater(herds.SleepingFraction, 0.7f);
        }

        // ------------------------------------------------------------ play

        [Test]
        public void Herds_PlayPairsAreAtMostOnePerHerdAndRejoin()
        {
            var herds = Make(8f, 50);
            herds.playRate = 6f;
            int plays = 0, maxPlaying = 0;
            int steps = Mathf.CeilToInt(300f / 0.05f);
            for (int i = 0; i < steps; i++)
            {
                herds.Step(0.05f);
                for (int h = 0; h < herds.HerdCount; h++)
                {
                    int p = herds.HerdPlayingAnimals(h);
                    Assert.LessOrEqual(p, 2, "more than one pair playing in herd " + h);
                    maxPlaying = Mathf.Max(maxPlaying, p);
                }
                plays = herds.PlaysStarted;
            }
            Assert.Greater(plays, 0, "no play started in 300 s");
            Assert.AreEqual(2, maxPlaying);
            herds.playRate = 0f;
            // A herd regroups whenever it stops, so the tightest radius seen over a minute is the formation.
            var tightest = new float[herds.HerdCount];
            for (int h = 0; h < herds.HerdCount; h++) tightest[h] = float.MaxValue;
            for (int i = 0; i < 1200; i++)
            {
                herds.Step(0.05f);
                for (int h = 0; h < herds.HerdCount; h++) tightest[h] = Mathf.Min(tightest[h], herds.HerdRadius(h));
            }
            Assert.AreEqual(0, herds.PlayingAnimals, "pairs rejoin the herd");
            var surface = herds.GetComponent<FakeIslandSurface>();
            for (int h = 0; h < herds.HerdCount; h++)
            {
                float bound = herds.BodyLength(h) * herds.formationSpacing * Mathf.Sqrt(herds.HerdSize(h) + 1) + 0.05f;
                Assert.LessOrEqual(tightest[h], bound, "herd " + h + " did not regroup after play");
                Assert.LessOrEqual(herds.HerdRadius(h), bound + herds.BodyLength(h) * 6f, "herd " + h + " is strung out");
                for (int m = 0; m < herds.HerdSize(h); m++) Assert.Greater(surface.SampleHeight(herds.AnimalPosition(h, m)), 0f);
            }
        }

        [Test]
        public void Herds_NoPlayAtNight()
        {
            var herds = Make(8f, 51);
            herds.playRate = 6f;
            _night = 1f;
            int before = herds.PlaysStarted;
            Run(herds, 200f);
            Assert.AreEqual(before, herds.PlaysStarted);
        }

        // ------------------------------------------------------------ tiers and persistence

        [Test]
        public void Herds_FarTierSeedsPlausibleStatesWhenComingNear()
        {
            var herds = Make(8f, 52);
            LifeLod.DistanceProvider = _ => 1000f;
            Run(herds, 30f);
            Assert.AreEqual(LifeTier.Far, herds.Tier);
            _night = 1f;
            Run(herds, 30f);
            LifeLod.DistanceProvider = _ => 0f;
            herds.Step(0.05f);
            Assert.AreEqual(LifeTier.Near, herds.Tier);
            Assert.Greater(herds.SleepingFraction, 0.7f, "an island arriving at night starts asleep");
            LifeLod.DistanceProvider = _ => 1000f;
            _night = 0f;
            Run(herds, 30f);
            LifeLod.DistanceProvider = _ => 0f;
            herds.Step(0.05f);
            Assert.AreEqual(0, herds.SleepingCount, "an island arriving by day starts awake");
        }

        [Test]
        public void Herds_MidTierRunsTheStateMachine()
        {
            var herds = Make(8f, 53);
            LifeLod.DistanceProvider = _ => (herds.detailDistance + herds.simDistance) * 0.5f;
            var seen = StatesSeen(herds, 240f, 1f / 60f);
            Assert.AreEqual(LifeTier.Mid, herds.Tier);
            Assert.IsTrue(seen.Contains(AnimalState.Look));
            Assert.IsTrue(seen.Contains(AnimalState.Rest));
            Assert.IsTrue(seen.Contains(AnimalState.Walk));
        }

        [Test]
        public void Herds_RestoreSeedsSleepFromTheCurrentNight()
        {
            var a = Make(8f, 54, "A");
            Run(a, 30f);
            var saved = a.Capture();
            Assert.Greater(saved.Count, 0);

            _night = 1f;
            var b = Make(8f, 55, "B");
            b.Restore(saved);
            Assert.AreEqual(a.HerdCount, b.HerdCount);
            Assert.Greater(b.SleepingFraction, 0.7f);

            _night = 0f;
            var c = Make(8f, 56, "C");
            c.Restore(saved);
            Assert.AreEqual(0, c.SleepingCount);
            Assert.AreEqual(0, c.PlayingAnimals);
        }

        [Test]
        public void HerdMesh_CarriesPoseChannels()
        {
            var herds = Make(8f, 57);
            Assert.IsTrue(herds.MeshHasAnimationData);
            var mesh = herds.transform.Find("Herds").GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(4, mesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord0));
            Assert.AreEqual(4, mesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord1));
            Assert.AreEqual(4, mesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord2));
            var uv2 = new List<Vector4>();
            mesh.GetUVs(2, uv2);
            bool grazing = false;
            foreach (var v in uv2) if (Mathf.Approximately(v.y, 10f) && v.w == 0f) grazing = true;
            Assert.IsTrue(grazing, "grazing animals bake a 10 degree head pitch and rest 0");
            // 120 simple animals stay under 10k; the detail LOD adds up to 40 detailed ones (camera at distance 0 here).
            Assert.LessOrEqual(herds.MeshVertexCount, 17000);
        }
    }
}
