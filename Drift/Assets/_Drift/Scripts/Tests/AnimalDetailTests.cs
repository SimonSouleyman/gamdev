using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class AnimalDetailTests
    {
        readonly List<GameObject> _objects = new();
        float _night, _camDist;
        Vector3 _cam;
        bool _useCam;

        [SetUp]
        public void SetUp()
        {
            _camDist = 0f;
            _useCam = false;
            LifeLod.DistanceProvider = p => _useCam ? Vector2.Distance(new Vector2(p.x, p.z), new Vector2(_cam.x, _cam.z)) : _camDist;
            _night = 0f;
            LifeEnvironment.NightProvider = () => _night;
            LifeEnvironment.TimeOfDayProvider = null;
            LifeEnvironment.PointOfInterest = null;
            LifeEnvironment.ViewDistanceProvider = null;
        }

        [TearDown]
        public void TearDown()
        {
            LifeLod.DistanceProvider = null;
            LifeEnvironment.NightProvider = null;
            LifeEnvironment.TimeOfDayProvider = null;
            LifeEnvironment.PointOfInterest = null;
            LifeEnvironment.ViewDistanceProvider = null;
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        IslandHerdSystem Make(float radius, int seed, bool hill = false, bool mature = false, string name = "Isle")
        {
            var go = new GameObject(name);
            go.SetActive(false);
            if (hill) go.AddComponent<FakeHillSurface>().radius = radius;
            else go.AddComponent<FakeIslandSurface>().radius = radius;
            var life = go.AddComponent<IslandLifeSystem>();
            life.seed = seed;
            var herds = go.AddComponent<IslandHerdSystem>();
            herds.seed = seed;
            // The detail budget needs the crowded islands these tests were written for (before the 3-herds-per-species cap).
            herds.maxHerdsPerSpecies = 10;
            go.SetActive(true);
            _objects.Add(go);
            if (mature) life.Simulate(600f, 10f);
            return herds;
        }

        static void Run(IslandHerdSystem herds, float seconds, float dt = 0.05f)
        {
            int n = Mathf.CeilToInt(seconds / dt);
            for (int i = 0; i < n; i++) herds.Step(dt);
        }

        static float Height(IslandHerdSystem herds, Vector2 p) => herds.GetComponent<IIslandSurface>().SampleHeight(p);

        // ------------------------------------------------------------ templates

        [Test]
        public void DetailTemplates_CarryEveryAnimationChannelInsideTheVertexBudget()
        {
            foreach (var kind in new[] { LifeKind.Hare, LifeKind.Sheep, LifeKind.Goat, LifeKind.Ox })
                for (int v = 0; v < LifeMeshes.Variants; v++)
                    foreach (bool young in new[] { false, true })
                    {
                        var t = LifeMeshes.GetDetailTemplate(kind, v, young);
                        string id = kind + "/" + v + (young ? "/young" : "");
                        int n = t.vertices.Length;
                        Assert.GreaterOrEqual(n, 200, id);
                        Assert.LessOrEqual(n, 400, id);
                        Assert.Greater(n, LifeMeshes.GetTemplate(kind, v).vertices.Length * 3, id + " is not more detailed than the simple template");
                        Assert.AreEqual(n, t.lever.Length, id);
                        Assert.AreEqual(n, t.up.Length, id);
                        Assert.AreEqual(n, t.leg.Length, id);
                        Assert.AreEqual(n, t.normals.Length, id);
                        Assert.AreEqual(n, t.colors.Length, id);
                        float maxLever = 0f, maxLeg = 0f, maxUp = 0f;
                        for (int i = 0; i < n; i++)
                        {
                            maxLever = Mathf.Max(maxLever, t.lever[i]);
                            maxLeg = Mathf.Max(maxLeg, Mathf.Abs(t.leg[i]));
                            maxUp = Mathf.Max(maxUp, t.up[i]);
                            if (t.vertices[i].y < 1e-4f)
                            {
                                Assert.AreEqual(0f, t.up[i], 1e-4f, id + ": a foot must stay on the ground when the animal lies down");
                                Assert.AreEqual(0f, t.lever[i], 1e-4f, id + ": a foot must stay planted when the head dips");
                            }
                            // The deepest graze swing and the sleeping pose keep the whole head above the ground (the
                            // head is what carries a lever above 0.35; a belly may sink into the grass).
                            if (t.lever[i] < 0.35f) continue;
                            float graze = t.vertices[i].y - Mathf.Sin(16f * Mathf.Deg2Rad) * t.lever[i];
                            float sleep = t.vertices[i].y - AnimalModels.RestLower * t.up[i] - Mathf.Sin(14f * Mathf.Deg2Rad) * t.lever[i];
                            Assert.GreaterOrEqual(graze, -0.02f, id + ": vertex " + i + " grazes below the ground");
                            Assert.GreaterOrEqual(sleep, -0.03f, id + ": vertex " + i + " sleeps below the ground");
                        }
                        Assert.Greater(maxLever, 0.3f, id + ": the head needs a pitch lever");
                        Assert.AreEqual(t.legLength / AnimalModels.RestLower, maxUp, t.legLength * 2f, id + ": resting lowers the body by about the leg length");
                        if (kind != LifeKind.Hare) Assert.Greater(maxLeg, 0f, id + ": walking legs need a step lift");
                    }
        }

        [Test]
        public void DetailTemplates_YoungHaveBiggerHeads()
        {
            foreach (var kind in new[] { LifeKind.Hare, LifeKind.Sheep, LifeKind.Goat, LifeKind.Ox })
            {
                var adult = LifeMeshes.GetDetailTemplate(kind, 0, false);
                var young = LifeMeshes.GetDetailTemplate(kind, 0, true);
                Assert.AreEqual(adult.vertices.Length, young.vertices.Length);
                float za = 0f, zy = 0f;
                foreach (var v in adult.vertices) za = Mathf.Max(za, v.z);
                foreach (var v in young.vertices) zy = Mathf.Max(zy, v.z);
                Assert.Greater(zy, za, kind + ": the young's head reaches further forward");
            }
        }

        [Test]
        public void DetailMesh_BakesPoseChannelsAndTheLegLift()
        {
            var herds = Make(6f, 81);
            herds.ClearHerds();
            Assert.GreaterOrEqual(herds.AddHerd(LifeKind.Sheep, Vector2.zero, 6), 0);
            // An unseen renderer (there is no camera here) rebuilds at hiddenMeshInterval.
            Run(herds, herds.hiddenMeshInterval + 0.5f);
            Assert.AreEqual(6, herds.DetailedCount);
            Assert.IsTrue(herds.MeshHasAnimationData);
            var mesh = herds.transform.Find("Herds").GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(6 * LifeMeshes.GetDetailTemplate(LifeKind.Sheep, 0, false).vertices.Length, mesh.vertexCount);
            Assert.AreEqual(4, mesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord0));
            Assert.AreEqual(4, mesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord2));
            bool legs = false;
            foreach (var c in mesh.colors) if (Mathf.Abs(c.a) > 1e-5f) legs = true;
            Assert.IsTrue(legs, "detail legs carry their step lift in the colour alpha");

            _camDist = 50f;
            Run(herds, herds.hiddenMeshInterval + 0.5f);
            Assert.AreEqual(0, herds.DetailedCount);
            mesh = herds.transform.Find("Herds").GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(6 * LifeMeshes.GetTemplate(LifeKind.Sheep, 0).vertices.Length, mesh.vertexCount);
            foreach (var c in mesh.colors) Assert.AreEqual(0f, c.a, "simple templates have no leg animation");
        }

        // ------------------------------------------------------------ LOD selection

        [Test]
        public void DetailLod_HasHysteresisBetweenNearAndFar()
        {
            var herds = Make(6f, 82);
            _camDist = 20f;
            Run(herds, 1f);
            Assert.AreEqual(0, herds.DetailedCount);
            _camDist = herds.detailNear - 1f;
            Run(herds, 1f);
            int detailed = herds.DetailedCount;
            Assert.Greater(detailed, 0);
            Assert.LessOrEqual(detailed, herds.maxDetailed);
            _camDist = (herds.detailNear + herds.detailFar) * 0.5f;
            Run(herds, 1f);
            Assert.AreEqual(detailed, herds.DetailedCount, "inside the band a detailed animal stays detailed");
            _camDist = herds.detailFar + 1f;
            Run(herds, 1f);
            Assert.AreEqual(0, herds.DetailedCount);
            _camDist = (herds.detailNear + herds.detailFar) * 0.5f;
            Run(herds, 1f);
            Assert.AreEqual(0, herds.DetailedCount, "inside the band a simple animal stays simple");
        }

        [Test]
        public void DetailLod_BudgetKeepsTheNearestAnimals()
        {
            var herds = Make(8f, 83);
            Assert.Greater(herds.AnimalCount, herds.maxDetailed + 20);
            _useCam = true;
            _cam = herds.transform.position + new Vector3(3f, 4f, 0f);
            Run(herds, 1f);
            Assert.LessOrEqual(herds.DetailedCount, herds.maxDetailed);
            Assert.GreaterOrEqual(herds.DetailedCount, herds.maxDetailed - 6, "the vertex budget may cost a few of the forty");
            float farthestDetailed = 0f, nearestSimple = float.MaxValue;
            for (int h = 0; h < herds.HerdCount; h++)
                for (int m = 0; m < herds.HerdSize(h); m++)
                {
                    float d = Vector2.Distance(herds.AnimalPosition(h, m), new Vector2(3f, 0f));
                    if (herds.IsDetailed(h, m)) farthestDetailed = Mathf.Max(farthestDetailed, d);
                    else nearestSimple = Mathf.Min(nearestSimple, d);
                }
            float stick = herds.detailFar - herds.detailNear;
            Assert.LessOrEqual(farthestDetailed, nearestSimple + stick + 0.3f, "the budget goes to the nearest animals");
            Assert.LessOrEqual(farthestDetailed, herds.detailFar);
        }

        [Test]
        public void DetailLod_RebuildsOnlyWhenTheSetChanges()
        {
            var herds = Make(8f, 84);
            herds.watchersPerHerd = 0;
            _night = 1f;
            Run(herds, 150f);
            Assert.AreEqual(herds.AnimalCount, herds.SleepingCount);
            int builds = herds.MeshBuilds, changes = herds.DetailChanges;
            for (int i = 0; i < 200; i++)
            {
                _camDist = 2f + 5f * Mathf.PingPong(i * 0.05f, 1f);
                herds.Step(0.05f);
            }
            Assert.AreEqual(changes, herds.DetailChanges, "moving the camera inside the near range changes nothing");
            Assert.AreEqual(builds, herds.MeshBuilds, "no rebuild while the detailed set is the same");

            _camDist = 30f;
            Run(herds, 1f);
            Assert.AreEqual(changes + 1, herds.DetailChanges);
            Assert.AreEqual(builds + 1, herds.MeshBuilds, "one rebuild for the whole change of set");

            // A camera panning right across the island: at most one set change per check, one rebuild per change.
            _useCam = true;
            builds = herds.MeshBuilds;
            changes = herds.DetailChanges;
            float seconds = 10f;
            int steps = Mathf.CeilToInt(seconds / 0.02f);
            for (int i = 0; i < steps; i++)
            {
                _cam = herds.transform.position + new Vector3(Mathf.Lerp(-25f, 25f, i / (float)steps), 5f, 1f);
                herds.Step(0.02f);
            }
            int newChanges = herds.DetailChanges - changes;
            Assert.Greater(newChanges, 0);
            Assert.LessOrEqual(newChanges, Mathf.CeilToInt(seconds / herds.detailCheckInterval));
            Assert.LessOrEqual(herds.MeshBuilds - builds, newChanges, "panning may only rebuild when the detailed set changed");
            Assert.AreEqual(0, herds.DetailedCount);
        }

        [Test]
        public void DetailLod_FortyDetailedAnimalsStayInsideTheVertexBudget()
        {
            var herds = Make(8f, 85);
            Assert.GreaterOrEqual(herds.AnimalCount, 100);
            Run(herds, 1f);
            Assert.LessOrEqual(herds.DetailedCount, herds.maxDetailed);
            Assert.GreaterOrEqual(herds.DetailedCount, herds.maxDetailed - 6);
            Assert.LessOrEqual(herds.MeshVertexCount, 16000);
            // Whatever the species mix: the detailed share is capped in vertices, the rest are simple templates.
            Assert.LessOrEqual(herds.maxDetailVertices + (herds.maxAnimals - 34) * 78, 17200);
            herds.maxDetailed = 10;
            Run(herds, 1f);
            Assert.AreEqual(10, herds.DetailedCount);
        }

        // ------------------------------------------------------------ behaviours

        [Test]
        public void Goats_PostALookoutOnTheHighestGroundInReach()
        {
            var herds = Make(8f, 86, hill: true);
            herds.ClearHerds();
            var centre = new Vector2(3.5f, 0f);
            int g = herds.AddHerd(LifeKind.Goat, centre, 5);
            Assert.GreaterOrEqual(g, 0);
            float best = -1f, herdGround = 0f;
            int maxLookouts = 0;
            for (int i = 0; i < 6000 && best < 0f; i++)
            {
                herds.Step(0.05f);
                int lookouts = 0;
                for (int m = 0; m < herds.HerdSize(0); m++)
                {
                    if (herds.ActivityOf(0, m) != AnimalActivity.Lookout) continue;
                    lookouts++;
                    if (!herds.AnimalArrived(0, m)) continue;
                    best = Height(herds, herds.AnimalPosition(0, m));
                    herdGround = Height(herds, herds.HerdCenter(0));
                    Assert.AreEqual(AnimalState.Look, herds.AnimalStateOf(0, m));
                    Assert.AreEqual("hält Ausschau vom Grat", herds.MoodOf(0, m));
                }
                maxLookouts = Mathf.Max(maxLookouts, lookouts);
            }
            Assert.Greater(best, 0f, "no goat reached a lookout within five minutes");
            Assert.AreEqual(1, maxLookouts, "one lookout per herd");
            Assert.GreaterOrEqual(best, herdGround + herds.lookoutMinRise);
            Assert.Greater(best, 2.5f, "the ridge of the test hill is within reach, the lookout should be near the top");
        }

        [Test]
        public void Oxen_WadeOnlyInTheShallowsAndNeverDrown()
        {
            LifeEnvironment.TimeOfDayProvider = () => 0.5f;
            var herds = Make(8f, 87, hill: true);
            herds.ClearHerds();
            Assert.GreaterOrEqual(herds.AddHerd(LifeKind.Ox, new Vector2(5.5f, 0f), 4), 0);
            bool waded = false;
            for (int i = 0; i < 6000; i++)
            {
                herds.Step(0.05f);
                Assert.AreEqual(4, herds.AnimalCount, "an ox drowned at " + i * 0.05f + " s");
                for (int m = 0; m < herds.HerdSize(0); m++)
                {
                    float h = Height(herds, herds.AnimalPosition(0, m));
                    Assert.GreaterOrEqual(h, -0.06f, "an ox went in deeper than the wade floor");
                    if (herds.ActivityOf(0, m) != AnimalActivity.Wade || !herds.AnimalArrived(0, m)) continue;
                    waded = true;
                    Assert.LessOrEqual(h, 0.03f, "a wading ox stands in the shallow band");
                }
            }
            Assert.IsTrue(waded, "no ox waded in five minutes of noon");
            LifeEnvironment.TimeOfDayProvider = () => 0f;
            _night = 1f;
            Run(herds, 60f);
            Assert.AreEqual(4, herds.AnimalCount);
            Assert.AreEqual(0, herds.CountActivity(AnimalActivity.Wade), "oxen leave the water for the night");
            for (int m = 0; m < herds.HerdSize(0); m++) Assert.Greater(Height(herds, herds.AnimalPosition(0, m)), 0.1f, "back on dry ground");
        }

        [Test]
        public void Hares_DigABurrowDiveIntoItAndComeBackOut()
        {
            var herds = Make(6f, 88);
            herds.ClearHerds();
            Assert.GreaterOrEqual(herds.AddHerd(LifeKind.Hare, Vector2.zero, 8), 0);
            for (int i = 0; i < 4000 && herds.BurrowCount == 0; i++) herds.Step(0.05f);
            Assert.AreEqual(1, herds.BurrowCount, "the hares dug no burrow in 200 s");
            Assert.Less((herds.BurrowPosition(0) - herds.HerdCenter(0)).magnitude, 1.5f);
            int vertsBefore = herds.MeshVertexCount;

            herds.Startle(new Vector2(5f, 0f), 3f);
            int maxHidden = 0;
            for (int i = 0; i < 160; i++)
            {
                herds.Step(0.05f);
                maxHidden = Mathf.Max(maxHidden, herds.HiddenCount);
            }
            Assert.Greater(maxHidden, 4, "most of the herd should be down the burrow");
            Assert.Less(herds.MeshVertexCount, vertsBefore, "hidden hares are not drawn");
            Run(herds, 40f);
            Assert.AreEqual(0, herds.HiddenCount, "everyone is back out");
            Assert.AreEqual(8, herds.AnimalCount);
            for (int m = 0; m < 8; m++) Assert.Greater(Height(herds, herds.AnimalPosition(0, m)), 0f);

            herds.Agitation = 1f;
            Run(herds, 8f);
            Assert.Greater(herds.HiddenCount, 4, "a storm sends them down too");
            Run(herds, 30f);
            Assert.Greater(herds.HiddenCount, 4, "and they stay down while it lasts");
            herds.Agitation = 0f;
            Run(herds, 40f);
            Assert.AreEqual(0, herds.HiddenCount);
        }

        [Test]
        public void Burrows_RoundTripThroughTheSaveAndOldSavesStillLoad()
        {
            var a = Make(6f, 89, name: "A");
            a.ClearHerds();
            a.AddHerd(LifeKind.Hare, Vector2.zero, 6);
            a.AddHerd(LifeKind.Sheep, new Vector2(2f, 2f), 5);
            for (int i = 0; i < 4000 && a.BurrowCount == 0; i++) a.Step(0.05f);
            Assert.AreEqual(1, a.BurrowCount);
            var saved = JsonUtility.FromJson<LifeSaveData>(JsonUtility.ToJson(new LifeSaveData { herds = a.Capture() })).herds;

            var b = Make(6f, 90, name: "B");
            b.Restore(saved);
            Assert.AreEqual(a.AnimalCount, b.AnimalCount);
            Assert.AreEqual(1, b.BurrowCount);
            Assert.AreEqual(a.BurrowPosition(0).x, b.BurrowPosition(0).x, 1e-4f);
            Assert.AreEqual(a.BurrowPosition(0).y, b.BurrowPosition(0).y, 1e-4f);

            foreach (var d in saved) System.Array.Resize(ref d.m, d.variant.Length * 6);
            var c = Make(6f, 91, name: "C");
            c.Restore(saved);
            Assert.AreEqual(a.AnimalCount, c.AnimalCount);
            Assert.AreEqual(0, c.BurrowCount, "a file from before the burrows simply has none");
        }

        [Test]
        public void Sheep_GatherUnderATreeWhenItRainsOrAtNoon()
        {
            foreach (bool rain in new[] { true, false })
            {
                var herds = Make(6f, rain ? 92 : 93, mature: true, name: rain ? "Rain" : "Noon");
                var life = herds.GetComponent<IslandLifeSystem>();
                Assert.Greater(life.CountOf(LifeKind.Tree), 0, "the test island grew no trees");
                herds.ClearHerds();
                Assert.GreaterOrEqual(herds.AddHerd(LifeKind.Sheep, new Vector2(1f, 0.5f), 7), 0);
                if (rain) herds.Agitation = 0.15f;
                else LifeEnvironment.TimeOfDayProvider = () => 0.5f;
                Assert.AreEqual(rain, herds.Raining);
                bool gathered = false;
                float nearest = float.MaxValue;
                for (int i = 0; i < 2400 && !gathered; i++)
                {
                    herds.Step(0.05f);
                    if (herds.CountActivity(AnimalActivity.Shade) == 0) continue;
                    nearest = life.NearestPlantDistance(LifeKind.Tree, herds.HerdCenter(0));
                    gathered = nearest < 0.5f;
                }
                Assert.IsTrue(gathered, (rain ? "rain" : "noon") + ": the sheep did not gather under a tree (nearest " + nearest + ")");
                float loose = herds.BodyLength(0) * herds.formationSpacing * Mathf.Sqrt(herds.HerdSize(0));
                Run(herds, 10f);
                Assert.Less(herds.HerdRadius(0), loose * 0.75f, "under the tree the herd stands tighter than its usual formation");
                LifeEnvironment.TimeOfDayProvider = null;
            }
        }

        [Test]
        public void Sheep_WalkInALineBehindTheirLeader()
        {
            var herds = Make(7f, 94);
            herds.ClearHerds();
            Assert.GreaterOrEqual(herds.AddHerd(LifeKind.Sheep, Vector2.zero, 7), 0);
            int maxLine = 0;
            float narrowest = float.MaxValue;
            Vector2 last = herds.HerdCenter(0);
            for (int i = 0; i < 2400; i++)
            {
                herds.Step(0.05f);
                Vector2 c = herds.HerdCenter(0);
                Vector2 heading = c - last;
                last = c;
                int line = herds.CountActivity(AnimalActivity.Line);
                maxLine = Mathf.Max(maxLine, line);
                if (line < herds.HerdSize(0) || heading.sqrMagnitude < 1e-10f) continue;
                heading.Normalize();
                Vector2 perp = new Vector2(-heading.y, heading.x);
                float widest = 0f;
                for (int m = 0; m < herds.HerdSize(0); m++)
                    widest = Mathf.Max(widest, Mathf.Abs(Vector2.Dot(herds.AnimalPosition(0, m) - c, perp)));
                narrowest = Mathf.Min(narrowest, widest);
            }
            Assert.Less(narrowest, herds.BodyLength(0) * 1.2f, "once everyone has caught up, the herd is a single file");
            Assert.GreaterOrEqual(maxLine, 5, "a walking sheep herd strings out behind its leader");
        }

        [Test]
        public void Herds_LineUpToWatchAPointOfInterest()
        {
            Assert.IsFalse(LifeEnvironment.TryPointOfInterest(Vector3.zero, 100f, out _), "no provider, nothing to see");
            var herds = Make(7f, 95);
            Vector3 poi = herds.transform.position + new Vector3(20f, 0f, 0f);
            LifeEnvironment.PointOfInterest = (Vector3 from, float max, out Vector3 p) => { p = poi; return (poi - from).magnitude <= max; };
            bool watched = false;
            for (int i = 0; i < 2400 && !watched; i++)
            {
                herds.Step(0.05f);
                for (int h = 0; h < herds.HerdCount && !watched; h++)
                    for (int m = 0; m < herds.HerdSize(h); m++)
                    {
                        if (herds.ActivityOf(h, m) != AnimalActivity.Watch || !herds.AnimalArrived(h, m)) continue;
                        Vector2 to = new Vector2(20f, 0f) - herds.AnimalPosition(h, m);
                        float want = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
                        Assert.Less(Mathf.Abs(Mathf.DeltaAngle(want, herds.AnimalYaw(h, m))), 20f, "a watcher faces the point of interest");
                        Assert.AreEqual(AnimalState.Look, herds.AnimalStateOf(h, m));
                        watched = true;
                        break;
                    }
            }
            Assert.IsTrue(herds.HasPointOfInterest);
            Assert.IsTrue(watched, "nobody watched the passing island within two minutes");
            LifeEnvironment.PointOfInterest = null;
            Run(herds, 20f);
            Assert.IsFalse(herds.HasPointOfInterest);
            Assert.AreEqual(0, herds.CountActivity(AnimalActivity.Watch));
        }

        [Test]
        public void Behaviours_DoNotBreakNightSleeping()
        {
            LifeEnvironment.TimeOfDayProvider = () => 0.5f;
            var herds = Make(8f, 96, hill: true, mature: true);
            herds.behaviourRate = 3f;
            herds.Agitation = 0.15f;
            Run(herds, 120f);
            int animals = herds.AnimalCount;
            herds.Agitation = 0f;
            LifeEnvironment.TimeOfDayProvider = () => 0f;
            _night = 1f;
            Run(herds, 150f);
            Assert.AreEqual(animals, herds.AnimalCount, "nobody was lost on an errand");
            Assert.Greater(herds.SleepingFraction, 0.7f);
            Assert.AreEqual(0, herds.HiddenCount);
            foreach (AnimalActivity act in System.Enum.GetValues(typeof(AnimalActivity)))
                if (act != AnimalActivity.None) Assert.AreEqual(0, herds.CountActivity(act), act + " at night");
            var centres = new List<Vector2>();
            for (int h = 0; h < herds.HerdCount; h++) centres.Add(herds.HerdCenter(h));
            Run(herds, 30f);
            for (int h = 0; h < herds.HerdCount; h++) Assert.AreEqual(centres[h], herds.HerdCenter(h), "herd " + h + " wandered at night");
            _night = 0f;
            Run(herds, herds.dawnStaggerMax + 10f);
            Assert.AreEqual(0, herds.SleepingCount);
            Assert.AreEqual(animals, herds.AnimalCount);
        }

        [Test]
        public void Herds_DrinkAtTheShoreAtDawn()
        {
            var herds = Make(8f, 97, hill: true);
            herds.ClearHerds();
            Assert.GreaterOrEqual(herds.AddHerd(LifeKind.Sheep, new Vector2(0f, 6f), 6), 0);
            herds.behaviourRate = 1.5f;
            _night = 1f;
            Run(herds, 100f);
            _night = 0f;
            bool drank = false;
            for (int i = 0; i < 3000 && !drank; i++)
            {
                herds.Step(0.05f);
                for (int m = 0; m < herds.HerdSize(0); m++)
                {
                    if (herds.ActivityOf(0, m) != AnimalActivity.Drink || !herds.AnimalArrived(0, m)) continue;
                    float h = Height(herds, herds.AnimalPosition(0, m));
                    Assert.Greater(h, 0.03f, "drinkers stay above the drowning line");
                    Assert.Less(h, 0.2f, "drinkers stand at the water");
                    drank = true;
                }
            }
            Assert.IsTrue(drank, "the herd did not go down to drink after dawn");
            Run(herds, 60f);
            Assert.AreEqual(6, herds.AnimalCount);
        }

        [Test]
        public void Moods_EveryActivityHasGermanText()
        {
            foreach (AnimalActivity act in System.Enum.GetValues(typeof(AnimalActivity)))
                foreach (AnimalState st in System.Enum.GetValues(typeof(AnimalState)))
                    Assert.IsNotEmpty(IslandHerdSystem.MoodText(st, act), st + "/" + act);
            Assert.AreEqual("grast", IslandHerdSystem.MoodText(AnimalState.Graze, AnimalActivity.None));
            Assert.AreEqual("versteckt sich im Bau", IslandHerdSystem.MoodText(AnimalState.Look, AnimalActivity.Burrowed));
            var herds = Make(6f, 98);
            for (int h = 0; h < herds.HerdCount; h++)
                for (int m = 0; m < herds.HerdSize(h); m++) Assert.AreEqual("grast", herds.MoodOf(h, m));
            herds.Startle(Vector2.zero, 3f);
            herds.Step(0.05f);
            Assert.AreEqual("flieht", herds.MoodOf(0, 0));
        }
    }
}
