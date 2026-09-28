using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Idle gestures (IslandHerdSystem.Idle.cs, owner 2026-09-24: "Die Tiere sollen immer irgendwas tun, wenn sie stehen
    // bleiben ... nicht alle das Gleiche gleichzeitig"): standing animals keep doing small things, each on its own
    // timer, the feet stay on the ground, the head is given back afterwards, sleepers and far islands are left alone.
    public class HerdIdleTests
    {
        readonly List<GameObject> _objects = new();
        float _night, _dist;

        static readonly LifeKind[] Kinds =
        {
            LifeKind.Hare, LifeKind.Sheep, LifeKind.Goat, LifeKind.Ox, LifeKind.Capybara, LifeKind.Flamingo, LifeKind.Tortoise,
            LifeKind.Penguin, LifeKind.Reindeer, LifeKind.ArcticFox, LifeKind.Meerkat, LifeKind.Zebra, LifeKind.Giraffe
        };

        [SetUp]
        public void SetUp()
        {
            _night = 0f;
            _dist = 0f;
            LifeLod.DistanceProvider = _ => _dist;
            LifeEnvironment.NightProvider = () => _night;
            LifeEnvironment.TimeOfDayProvider = null;
            LifeEnvironment.PointOfInterest = null;
            LifeEnvironment.ViewDistanceProvider = null;
            LifeEnvironment.StormProvider = null;
            IslandLifeSystem.ResetSeasonReference();
        }

        [TearDown]
        public void TearDown()
        {
            LifeLod.DistanceProvider = null;
            LifeEnvironment.NightProvider = null;
            LifeEnvironment.ViewDistanceProvider = null;
            IslandLifeSystem.ResetSeasonReference();
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        // One herd on a flat island of its biome; errands, play and wandering are switched off so it just stands.
        IslandHerdSystem Stage(LifeKind kind, int size, int seed = 7)
        {
            bool shore = kind == LifeKind.Flamingo || kind == LifeKind.Penguin || kind == LifeKind.Capybara;
            var go = new GameObject("Idle" + kind);
            go.SetActive(false);
            var surface = go.AddComponent<FakeIslandSurface>();
            surface.radius = 7f;
            surface.beach = shore ? 2.5f : 0f;
            surface.height = kind == LifeKind.Goat ? 1.6f : 1f;
            surface.biome = IslandHerdSystem.HomeBiomeOf(kind);
            go.AddComponent<IslandLifeSystem>().seed = seed;
            var herds = go.AddComponent<IslandHerdSystem>();
            herds.seed = seed;
            go.SetActive(true);
            _objects.Add(go);
            herds.ClearHerds();
            Assert.GreaterOrEqual(herds.AddHerd(kind, Vector2.zero, size), 0, "no ground for " + kind);
            herds.behaviourRate = 0f;
            herds.playRate = 0f;
            herds.growthChance = 0f;
            Hold(herds);
            return herds;
        }

        // Keeps the herd standing: its wander target is its own centre.
        static void Hold(IslandHerdSystem herds)
        {
            var f = typeof(IslandHerdSystem).GetField("_herds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var list = (System.Collections.IList)f.GetValue(herds);
            foreach (var h in list)
            {
                var t = h.GetType();
                var c = t.GetField("center").GetValue(h);
                t.GetField("target").SetValue(h, c);
                t.GetField("wait").SetValue(h, 1e6f);
            }
        }

        static void Run(IslandHerdSystem herds, float seconds, float dt = 0.05f)
        {
            int n = Mathf.CeilToInt(seconds / dt);
            for (int i = 0; i < n; i++) herds.Step(dt);
        }

        static Mesh MeshOf(IslandHerdSystem herds)
        {
            var t = herds.transform.Find("Herds");
            return t != null ? t.GetComponent<MeshFilter>().sharedMesh : null;
        }

        [Test]
        public void StandingHerd_MembersDoDifferentThings_AtDifferentTimes()
        {
            var herds = Stage(LifeKind.Sheep, 10);
            Run(herds, 5f);
            var seen = new HashSet<IdleAction>();
            int samples = 0, varied = 0, busySamples = 0, animalSamples = 0, busyAnimals = 0, pairsBoth = 0, pairsLock = 0;
            int n = herds.HerdSize(0);
            var acts = new IdleAction[n];
            var prog = new float[n];
            for (int s = 0; s < 120; s++)
            {
                Run(herds, 0.5f);
                int busy = 0;
                var kinds = new HashSet<IdleAction>();
                bool phases = false;
                float firstProg = -1f;
                for (int m = 0; m < n; m++)
                {
                    acts[m] = herds.AnimalIdle(0, m);
                    prog[m] = herds.AnimalIdleProgress(0, m);
                    Assert.AreNotEqual(AnimalState.Walk, herds.AnimalStateOf(0, m), "the herd is held in place");
                    animalSamples++;
                    if (acts[m] == IdleAction.None) continue;
                    busy++;
                    busyAnimals++;
                    seen.Add(acts[m]);
                    kinds.Add(acts[m]);
                    if (firstProg < 0f) firstProg = prog[m];
                    else if (Mathf.Abs(prog[m] - firstProg) > 0.1f) phases = true;
                }
                for (int m = 0; m + 1 < n; m++)
                {
                    if (acts[m] == IdleAction.None || acts[m + 1] == IdleAction.None) continue;
                    pairsBoth++;
                    if (acts[m] == acts[m + 1] && Mathf.Abs(prog[m] - prog[m + 1]) < 0.05f) pairsLock++;
                }
                samples++;
                if (busy >= 3)
                {
                    busySamples++;
                    if (kinds.Count >= 2 || phases) varied++;
                }
            }
            Assert.GreaterOrEqual(seen.Count, 6, "sheep show most of the gestures: " + string.Join(",", seen));
            Assert.Greater(busyAnimals, animalSamples * 0.3f, "a standing animal is mostly doing something (" + busyAnimals + "/" + animalSamples + ")");
            Assert.Greater(busySamples, samples / 2, "usually several animals are busy at once");
            Assert.GreaterOrEqual(varied, busySamples * 0.95f, "never the whole herd doing the same thing in step");
            Assert.Less(pairsLock, Mathf.Max(3, pairsBoth / 20), "neighbours are not in lockstep (" + pairsLock + "/" + pairsBoth + ")");
            Assert.Greater(herds.IdleActions, 60);
        }

        [Test]
        public void EveryKind_HasGestures_AndItsOwnWhereTheModelAllows()
        {
            foreach (var kind in Kinds)
            {
                var herds = Stage(kind, kind == LifeKind.ArcticFox ? 2 : kind == LifeKind.Tortoise ? 3 : 6, 11 + (int)kind);
                var seen = new HashSet<IdleAction>();
                bool pose = false, tilt = false, turn = false;
                for (int s = 0; s < 300; s++)
                {
                    Run(herds, 0.2f);
                    for (int m = 0; m < herds.HerdSize(0); m++)
                    {
                        var act = herds.AnimalIdle(0, m);
                        if (act == IdleAction.None) continue;
                        seen.Add(act);
                        if (act == IdleAction.Special && herds.AnimalShownPose(0, m) != 0) pose = true;
                        if (Mathf.Abs(herds.AnimalIdlePitch(0, m)) > 2f || Mathf.Abs(herds.AnimalIdleRoll(0, m)) > 2f) tilt = true;
                        if (Mathf.Abs(herds.AnimalIdleYaw(0, m)) > 10f) turn = true;
                    }
                }
                Assert.GreaterOrEqual(seen.Count, 4, kind + ": " + string.Join(",", seen));
                Assert.IsTrue(turn, kind + " looks around");
                Assert.IsTrue(tilt || kind == LifeKind.Tortoise, kind + " bends, stretches or shakes");
                bool special = kind == LifeKind.Hare || kind == LifeKind.Meerkat || kind == LifeKind.Flamingo || kind == LifeKind.Tortoise
                               || kind == LifeKind.Penguin || kind == LifeKind.Giraffe;
                Assert.AreEqual(special, seen.Contains(IdleAction.Special), kind + ": own gesture");
                if (kind == LifeKind.Meerkat || kind == LifeKind.Flamingo || kind == LifeKind.Tortoise || kind == LifeKind.Penguin)
                    Assert.IsTrue(pose, kind + " shows its pose (upright / one leg / in the shell / flippers out)");
                foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
                _objects.Clear();
            }
        }

        [Test]
        public void Gestures_KeepTheFeetOnTheGround_AndTheAnimalsInPlace()
        {
            foreach (var kind in new[] { LifeKind.Sheep, LifeKind.Hare, LifeKind.ArcticFox, LifeKind.Giraffe, LifeKind.Ox })
            {
                var herds = Stage(kind, kind == LifeKind.ArcticFox ? 2 : 6, 23);
                float ground = 1f;
                int n = herds.HerdSize(0);
                var start = new Vector2[n];
                for (int m = 0; m < n; m++) start[m] = herds.AnimalPosition(0, m);
                float lowest = float.MaxValue, lowMax = float.MinValue;
                int builds = herds.MeshBuilds;
                for (int s = 0; s < 400; s++)
                {
                    Run(herds, 0.1f);
                    var mesh = MeshOf(herds);
                    Assert.IsNotNull(mesh);
                    var verts = mesh.vertices;
                    float lo = float.MaxValue;
                    foreach (var v in verts) lo = Mathf.Min(lo, v.y);
                    lowest = Mathf.Min(lowest, lo);
                    lowMax = Mathf.Max(lowMax, lo);
                    for (int m = 0; m < n; m++)
                    {
                        Assert.LessOrEqual(Mathf.Abs(herds.AnimalIdlePitch(0, m)), 50f);
                        Assert.LessOrEqual(Mathf.Abs(herds.AnimalIdleRoll(0, m)), 15f);
                    }
                }
                Assert.Greater(herds.MeshBuilds, builds, kind + ": gestures are drawn");
                Assert.GreaterOrEqual(lowest, ground - 0.035f, kind + ": nothing sinks into the ground");
                Assert.LessOrEqual(lowMax, ground + 0.005f, kind + ": nobody floats (the feet stay down)");
                for (int m = 0; m < n; m++)
                    Assert.Less(Vector2.Distance(start[m], herds.AnimalPosition(0, m)), herds.BodyLength(0) * 0.6f, kind + ": gestures happen on the spot");
                foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
                _objects.Clear();
            }
        }

        [Test]
        public void Gestures_GiveTheHeadBack_WhenSwitchedOff()
        {
            var herds = Stage(LifeKind.Goat, 6);
            Run(herds, 40f);
            herds.idleRate = 0f;
            Run(herds, 0.1f);
            // The shader's head pitch targets are the state machine's again: graze, look, stretch, rest, sleep.
            var allowed = new[] { 10f, -7f, -14f, 5f, 14f, 0f };
            for (int m = 0; m < herds.HerdSize(0); m++)
            {
                Assert.AreEqual(IdleAction.None, herds.AnimalIdle(0, m));
                Assert.AreEqual(0f, herds.AnimalIdleYaw(0, m));
                Assert.AreEqual(0f, herds.AnimalIdlePitch(0, m));
                Assert.AreEqual(0f, herds.AnimalIdleRoll(0, m));
                float p = herds.AnimalHeadPitchTarget(0, m);
                Assert.IsTrue(System.Array.IndexOf(allowed, p) >= 0, "head pitch " + p + " left over from a gesture");
            }
        }

        [Test]
        public void Sleepers_StayAsleep_AndFarIslandsSkipGestures()
        {
            var herds = Stage(LifeKind.Sheep, 8);
            herds.watchersPerHerd = 0;
            _night = 1f;
            Run(herds, 150f);
            Assert.AreEqual(herds.AnimalCount, herds.SleepingCount);
            int builds = herds.MeshBuilds, started = herds.IdleActions;
            Run(herds, 20f);
            Assert.AreEqual(started, herds.IdleActions, "no gestures in their sleep");
            Assert.AreEqual(builds, herds.MeshBuilds, "a sleeping herd still rebuilds nothing");

            _night = 0f;
            var mid = Stage(LifeKind.Sheep, 8, 31);
            _dist = 70f;
            Run(mid, 2f);
            Assert.AreEqual(LifeTier.Mid, mid.Tier);
            started = mid.IdleActions;
            Run(mid, 20f);
            Assert.AreEqual(started, mid.IdleActions, "the mid tier shows no gestures");
            for (int m = 0; m < mid.HerdSize(0); m++) Assert.AreEqual(0f, mid.AnimalIdleYaw(0, m));
        }

        [Test]
        public void Watchers_LookAroundAtNight()
        {
            var herds = Stage(LifeKind.Sheep, 8);
            _night = 1f;
            Run(herds, 60f);
            bool watcherBusy = false;
            for (int s = 0; s < 100 && !watcherBusy; s++)
            {
                Run(herds, 0.2f);
                for (int m = 0; m < herds.HerdSize(0); m++)
                {
                    var act = herds.AnimalIdle(0, m);
                    if (herds.AnimalStateOf(0, m) == AnimalState.Sleep) Assert.AreEqual(IdleAction.None, act);
                    if (herds.IsWatcher(0, m) && act != IdleAction.None)
                    {
                        Assert.IsTrue(act == IdleAction.Look || act == IdleAction.Glance || act == IdleAction.Sniff, "calm gestures only at night: " + act);
                        watcherBusy = true;
                    }
                }
            }
            Assert.IsTrue(watcherBusy, "the watcher keeps looking around");
        }
    }
}
