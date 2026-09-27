using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Procedural coat markings (Markings, Shaders/DriftMarkings.hlsl): the templates bake pattern coordinates and a
    // code per vertex, the batches add each individual's seed into UV3 without touching the animation channels.
    public class MarkingsTests
    {
        static readonly LifeKind[] Herd =
        {
            LifeKind.Hare, LifeKind.Sheep, LifeKind.Goat, LifeKind.Ox, LifeKind.Capybara, LifeKind.Flamingo, LifeKind.Tortoise,
            LifeKind.Reindeer, LifeKind.Penguin, LifeKind.ArcticFox, LifeKind.Zebra, LifeKind.Giraffe, LifeKind.Meerkat
        };

        readonly List<GameObject> _objects = new();
        float _camDist;

        [SetUp]
        public void SetUp()
        {
            _camDist = 0f;
            LifeLod.DistanceProvider = p => _camDist;
            LifeEnvironment.NightProvider = () => 0f;
            LifeEnvironment.TimeOfDayProvider = null;
            LifeEnvironment.PointOfInterest = null;
            LifeEnvironment.ViewDistanceProvider = null;
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

        IslandHerdSystem Make(int seed)
        {
            var go = new GameObject("MarkIsle");
            go.SetActive(false);
            go.AddComponent<FakeIslandSurface>().radius = 6f;
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

        [Test]
        public void Code_RoundTripsPatternLevelAndSeed()
        {
            for (int pattern = 0; pattern <= Markings.Coarse; pattern++)
                for (int level = 0; level <= Markings.FullLevel; level++)
                    foreach (float seed in new[] { 0f, 0.37f, 0.999f })
                    {
                        float code = Markings.Code(pattern, level) + Markings.SeedCode(seed);
                        Assert.AreEqual(pattern, Markings.PatternOf(code), "pattern " + pattern);
                        Assert.AreEqual(level, Markings.LevelOf(code), "level " + level);
                        Assert.AreEqual(seed, Markings.SeedOf(code), 2e-3f, "seed");
                    }
            Assert.AreEqual(Markings.Seed(1.3f), Markings.Seed(1.3f), "a seed is stable");
            Assert.AreNotEqual(Markings.Seed(0.9f * 97.3f), Markings.Seed(1.1f * 97.3f));
        }

        [Test]
        public void DetailTemplates_CarryTheirCoatAndPlainAccents()
        {
            foreach (var kind in Herd)
                for (int v = 0; v < LifeMeshes.Variants; v++)
                    foreach (bool young in new[] { false, true })
                        for (int pose = 0; pose <= AnimalModels.PoseCall; pose++)
                        {
                            var t = LifeMeshes.GetDetailTemplate(kind, v, young, pose);
                            string id = kind + "/" + v + (young ? "/young" : "") + "/pose" + pose;
                            var marks = Markings.Of(t);
                            Assert.IsNotNull(marks, id);
                            Assert.AreEqual(t.vertices.Length, marks.Length, id);
                            int coat = 0, plain = 0;
                            foreach (var m in marks)
                            {
                                Assert.IsFalse(float.IsNaN(m.x) || float.IsNaN(m.y) || float.IsNaN(m.z), id);
                                Assert.GreaterOrEqual(m.z, 0f, id);
                                Assert.LessOrEqual(m.z, 1f, id);
                                Assert.AreEqual(0f, m.w - Mathf.Floor(m.w), 1e-4f, id + ": templates carry no seed");
                                int p = Markings.PatternOf(m.w);
                                Assert.LessOrEqual(p, Markings.Coarse, id);
                                if (p == Markings.CoatOf(kind)) coat++;
                                if (p == Markings.None) plain++;
                            }
                            Assert.Greater(coat, marks.Length / 4, id + ": the body wears the species' coat");
                            Assert.Greater(plain, 0, id + ": eyes stay plain");
                        }
        }

        [Test]
        public void DetailTemplates_MarkTheSpeciesDetails()
        {
            Assert.IsTrue(Has(LifeMeshes.GetDetailTemplate(LifeKind.Hare, 0, false), Markings.Tip), "hare ear tips");
            Assert.IsTrue(Has(LifeMeshes.GetDetailTemplate(LifeKind.Flamingo, 0, false), Markings.Tip), "flamingo flight feathers");
            Assert.IsTrue(Has(LifeMeshes.GetDetailTemplate(LifeKind.Ox, 0, false), Markings.Tip), "ox horn tips / tail tuft");
            Assert.IsTrue(Has(LifeMeshes.GetDetailTemplate(LifeKind.Sheep, 0, false), Markings.Skin), "dark sheep face and legs");
            Assert.IsTrue(Has(LifeMeshes.GetDetailTemplate(LifeKind.Tortoise, 0, false), Markings.Skin), "tortoise skin");
            // The stripes and patches are the shader's now: no black band tubes or decal quads in the coat colour.
            var zebra = LifeMeshes.GetDetailTemplate(LifeKind.Zebra, 0, false);
            var zm = Markings.Of(zebra);
            for (int i = 0; i < zm.Length; i++)
                if (Markings.PatternOf(zm[i].w) == Markings.Zebra)
                    Assert.Greater(zebra.colors[i].r, 0.3f, "zebra coat is the white base, vertex " + i);
            // Marked on the rest shape: the belly-slide penguin has the standing penguin's coordinates on its body.
            var stand = Markings.Of(LifeMeshes.GetDetailTemplate(LifeKind.Penguin, 0, false, 0));
            var slide = Markings.Of(LifeMeshes.GetDetailTemplate(LifeKind.Penguin, 0, false, 1));
            Assert.AreEqual(stand.Length, slide.Length);
            // Legs, body and belly come before the flippers, which point elsewhere in the slide.
            for (int i = 0; i < 120; i++) Assert.AreEqual(stand[i].x, slide[i].x, 1e-4f, "penguin mark " + i);
        }

        static bool Has(PlantTemplate t, int pattern)
        {
            foreach (var m in Markings.Of(t))
                if (Markings.PatternOf(m.w) == pattern) return true;
            return false;
        }

        [Test]
        public void SimpleTemplates_GetMarksThatLeaveHornsPlain()
        {
            foreach (var kind in Herd)
                for (int v = 0; v < LifeMeshes.Variants; v++)
                {
                    var t = LifeMeshes.GetTemplate(kind, v);
                    var marks = Markings.For(kind, v, t);
                    Assert.IsNotNull(marks, kind.ToString());
                    Assert.AreEqual(t.vertices.Length, marks.Length, kind.ToString());
                    Assert.AreSame(marks, Markings.SimpleMarks(kind, v), "cached");
                }
            var gm = Markings.SimpleMarks(LifeKind.Goat, 0);
            int plain = 0;
            for (int i = 0; i < gm.Length; i++) if (Markings.PatternOf(gm[i].w) == Markings.None) plain++;
            Assert.AreEqual(18, plain, "the two horn cones (3 faces x 3 verts each) stay plain");
            Assert.IsNull(Markings.For(LifeKind.Bird, 0, LifeMeshes.GetTemplate(LifeKind.Bird, 0)));
        }

        [Test]
        public void HerdMesh_CarriesMarksWithOneSeedPerIndividual()
        {
            var herds = Make(91);
            herds.ClearHerds();
            Assert.GreaterOrEqual(herds.AddHerd(LifeKind.Sheep, Vector2.zero, 6), 0);
            Run(herds, herds.hiddenMeshInterval + 0.5f);
            Assert.AreEqual(6, herds.DetailedCount);
            var mesh = herds.transform.Find("Herds").GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(4, mesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord3));
            var uv3 = new List<Vector4>();
            mesh.GetUVs(3, uv3);
            Assert.AreEqual(mesh.vertexCount, uv3.Count);
            int n = LifeMeshes.GetDetailTemplate(LifeKind.Sheep, 0, false).vertices.Length;
            var seeds = new HashSet<int>();
            bool wool = false;
            for (int a = 0; a < 6; a++)
            {
                float seed = -1f;
                for (int i = a * n; i < (a + 1) * n; i++)
                {
                    int p = Markings.PatternOf(uv3[i].w);
                    if (p == Markings.Wool) wool = true;
                    if (p == Markings.None) continue;
                    float s = Markings.SeedOf(uv3[i].w);
                    if (seed < 0f) seed = s;
                    Assert.AreEqual(seed, s, 1e-3f, "one seed per animal");
                }
                seeds.Add(Mathf.RoundToInt(seed * 1000f));
            }
            Assert.IsTrue(wool);
            Assert.Greater(seeds.Count, 3, "a herd is not a row of clones");

            // The far LOD keeps its coat too.
            _camDist = 50f;
            Run(herds, herds.hiddenMeshInterval + 0.5f);
            Assert.AreEqual(0, herds.DetailedCount);
            mesh = herds.transform.Find("Herds").GetComponent<MeshFilter>().sharedMesh;
            mesh.GetUVs(3, uv3);
            bool farWool = false;
            foreach (var m in uv3) if (Markings.PatternOf(m.w) == Markings.Wool) farWool = true;
            Assert.IsTrue(farWool);
        }

        [Test]
        public void TemplateBatch_OnlyAnimalBatchesCarryTheMarkingChannel()
        {
            var plants = new TemplateBatch();
            plants.Begin();
            var grass = LifeMeshes.GetTemplate(LifeKind.Grass, 0);
            plants.AddPlant(grass, Vector3.zero, 0f, 1f, 1f, 0f, Color.white, default, 0f);
            Assert.AreEqual(1, plants.UvChannels);

            var herd = new TemplateBatch();
            herd.Begin();
            herd.Add(LifeMeshes.Burrow, Vector3.zero, 0f, 1f);
            var sheep = LifeMeshes.GetDetailTemplate(LifeKind.Sheep, 0, false);
            herd.AddAnimal(sheep, Vector3.one, 0f, 0.2f, new AnimalPose(), default, 0f, 0f, 0f, Markings.Of(sheep), 0.5f);
            herd.Add(AnimalModels.Egret, Vector3.one, 0f, 0.2f);
            Assert.AreEqual(4, herd.UvChannels);
            var mesh = new Mesh();
            try
            {
                herd.Apply(mesh);
                var uv3 = new List<Vector4>();
                mesh.GetUVs(3, uv3);
                int burrow = LifeMeshes.Burrow.vertices.Length;
                for (int i = 0; i < burrow; i++) Assert.AreEqual(Vector4.zero, uv3[i], "burrow is unmarked");
                for (int i = burrow + sheep.vertices.Length; i < uv3.Count; i++) Assert.AreEqual(Vector4.zero, uv3[i], "egret is unmarked");
                var marks = Markings.Of(sheep);
                for (int i = 0; i < sheep.vertices.Length; i++)
                {
                    Assert.AreEqual(marks[i].x, uv3[burrow + i].x, 1e-5f);
                    Assert.AreEqual(Markings.PatternOf(marks[i].w), Markings.PatternOf(uv3[burrow + i].w));
                }
                var uv0 = new List<Vector4>();
                mesh.GetUVs(0, uv0);
                Assert.AreEqual(sheep.lever[5] * 0.2f, uv0[burrow + 5].x, 1e-5f, "animation channels unchanged");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void Critters_AndBirds_GetTheirMarkings()
        {
            Assert.IsTrue(HasCritter(LifeKind.Crab, 0, Markings.Speckle));
            Assert.IsTrue(HasCritter(LifeKind.Turtle, 1, Markings.Scutes));
            for (int v = 0; v < LifeMeshes.Variants; v++) Assert.IsTrue(HasCritter(LifeKind.Butterfly, v, Markings.WingYellow + v));
            Assert.IsNull(Markings.CritterMarks(LifeKind.Firefly, 0));

            var cb = new CritterBatch();
            cb.Begin();
            var crab = LifeMeshes.GetTemplate(LifeKind.Crab, 0);
            cb.Add(crab, Vector3.zero, 0f, 0.06f, 0f, 0f, 0f, 0f, 1f, 0, 0f, Markings.CritterMarks(LifeKind.Crab, 0), 0.25f);
            cb.AddDisc(Make(3).GetComponent<IIslandSurface>(), Vector2.zero, 0.2f, 0.01f, Color.white, 0f);
            var mesh = new Mesh();
            try
            {
                cb.Apply(mesh);
                var uv3 = new List<Vector4>();
                mesh.GetUVs(3, uv3);
                Assert.AreEqual(Markings.Speckle, Markings.PatternOf(uv3[0].w));
                Assert.AreEqual(0.25f, Markings.SeedOf(uv3[0].w), 2e-3f);
                Assert.AreEqual(Vector4.zero, uv3[uv3.Count - 1], "sand discs stay plain");
            }
            finally { Object.DestroyImmediate(mesh); }

            var bird = LifeMeshes.GetTemplate(LifeKind.Bird, 0);
            var marked = Markings.BirdTemplate(LifeKind.Bird, 0);
            Assert.AreSame(marked, Markings.BirdTemplate(LifeKind.Bird, 0));
            Assert.AreSame(bird.wing, marked.wing, "flap weights unchanged");
            Assert.AreEqual(bird.vertices.Length, marked.colors.Length);
            bool darkerTip = false;
            for (int i = 0; i < bird.vertices.Length; i++)
                if (Mathf.Abs(bird.vertices[i].x) > 0.3f && marked.colors[i].r < bird.colors[i].r * 0.6f) darkerTip = true;
            Assert.IsTrue(darkerTip, "songbirds get dark primaries");
        }

        static bool HasCritter(LifeKind kind, int v, int pattern)
        {
            var marks = Markings.CritterMarks(kind, v);
            Assert.AreEqual(LifeMeshes.GetTemplate(kind, v).vertices.Length, marks.Length, kind.ToString());
            foreach (var m in marks) if (Markings.PatternOf(m.w) == pattern) return true;
            return false;
        }

        [Test]
        public void Shaders_ExposeTheMarkingsSwitch()
        {
            Assert.IsTrue(LifeMeshes.AnimalMaterial.HasProperty("_Markings"));
            Assert.IsTrue(LifeMeshes.CritterMaterial.HasProperty("_Markings"));
            Assert.AreEqual(1f, LifeMeshes.AnimalMaterial.GetFloat("_Markings"));
        }
    }
}
