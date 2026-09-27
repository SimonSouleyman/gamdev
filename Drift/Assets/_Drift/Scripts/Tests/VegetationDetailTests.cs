using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Plant material detail (Drift/Vegetation): per-vertex surface parts in the templates and in UV0.w of the
    // vegetation mesh, and the Resources material with its Poly Haven texture arrays.
    public class VegetationDetailTests
    {
        readonly List<GameObject> _objects = new();

        [SetUp]
        public void SetUp()
        {
            LifeLod.DistanceProvider = _ => 0f;
            LifeEnvironment.NightProvider = () => 0f;
            IslandLifeSystem.ResetSeasonReference();
        }

        [TearDown]
        public void TearDown()
        {
            LifeLod.DistanceProvider = null;
            LifeEnvironment.NightProvider = null;
            IslandLifeSystem.ResetSeasonReference();
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        static int[] I(params byte[] parts) => System.Array.ConvertAll(parts, b => (int)b);

        static HashSet<int> PartsOf(LifeKind kind, int variant, out PlantTemplate tpl)
        {
            tpl = LifeMeshes.GetTemplate(kind, variant);
            var set = new HashSet<int>();
            foreach (float p in PlantModels.Parts(kind, tpl)) set.Add((int)p);
            return set;
        }

        [Test]
        public void Parts_BarkOnTrunks_FoliageOnCrowns_PetalsSnowAndMoundsStayFlat()
        {
            for (int v = 0; v < LifeMeshes.Variants; v++)
            {
                CollectionAssert.AreEquivalent(I(PlantModels.PartBark, PlantModels.PartFoliage), PartsOf(LifeKind.Tree, v, out _), "tree " + v);
                CollectionAssert.AreEquivalent(I(PlantModels.PartPalmBark, PlantModels.PartFoliage), PartsOf(LifeKind.Palm, v, out _), "palm " + v);
                CollectionAssert.AreEquivalent(I(PlantModels.PartBlade), PartsOf(LifeKind.Grass, v, out _), "grass " + v);
                CollectionAssert.AreEquivalent(I(PlantModels.PartFlat), PartsOf(LifeKind.Mushroom, v, out _), "mushroom " + v);
                CollectionAssert.AreEquivalent(I(PlantModels.PartFlat), PartsOf(LifeKind.TermiteMound, v, out _), "termite mound " + v);
                CollectionAssert.AreEquivalent(I(PlantModels.PartFlat, PlantModels.PartFoliage), PartsOf(LifeKind.Hibiscus, v, out _), "hibiscus petals stay flat " + v);
                CollectionAssert.AreEquivalent(I(PlantModels.PartBark, PlantModels.PartFoliage), PartsOf(LifeKind.Baobab, v, out _), "baobab " + v);
            }

            // The spruce's snow cap and the birch's dark marks keep their flat colour.
            var spruce = PartsOf(LifeKind.Spruce, 0, out var sp);
            CollectionAssert.AreEquivalent(I(PlantModels.PartFlat, PlantModels.PartBark, PlantModels.PartFoliage), spruce);
            var parts = PlantModels.Parts(LifeKind.Spruce, sp);
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] == PlantModels.PartFlat) Assert.Greater(sp.colors[i].b, 0.8f, "only the white snow cap is flat");
            CollectionAssert.AreEquivalent(I(PlantModels.PartFlat, PlantModels.PartPalmBark, PlantModels.PartFoliage), PartsOf(LifeKind.Birch, 1, out _));

            foreach (LifeKind kind in System.Enum.GetValues(typeof(LifeKind)))
            {
                if (!PlantModels.IsPlant(kind)) continue;
                foreach (int p in PartsOf(kind, 0, out _))
                    Assert.That(p, Is.InRange(0, 4), kind + " part");
            }
        }

        [Test]
        public void VegetationMesh_CarriesPartsInUv0W_AndUsesTheVegetationMaterial()
        {
            var go = new GameObject("VegDetail");
            go.SetActive(false);
            var surface = go.AddComponent<FakeIslandSurface>();
            surface.radius = 7f;
            surface.beach = 2f;
            var life = go.AddComponent<IslandLifeSystem>();
            life.seed = 777;
            go.SetActive(true);
            _objects.Add(go);
            life.Simulate(1500f, 10f);

            var mesh = life.VegetationMesh;
            Assert.Greater(mesh.vertexCount, 1000);
            var uv = new List<Vector4>();
            mesh.GetUVs(0, uv);
            Assert.AreEqual(mesh.vertexCount, uv.Count);
            var seen = new HashSet<int>();
            foreach (var u in uv)
            {
                Assert.AreEqual(Mathf.Round(u.w), u.w, 1e-5f, "part ids are whole numbers");
                Assert.That(u.w, Is.InRange(0f, 4f));
                Assert.GreaterOrEqual(u.x, 0f, "the sway channel is untouched");
                seen.Add((int)u.w);
            }
            Assert.IsTrue(seen.Contains(PlantModels.PartBark) && seen.Contains(PlantModels.PartFoliage) && seen.Contains(PlantModels.PartBlade),
                "a grown temperate island shows bark, foliage and blades");

            var mat = go.transform.Find("Vegetation").GetComponent<MeshRenderer>().sharedMaterial;
            Assert.AreSame(IslandLifeSystem.VegetationMaterial, mat);
        }

        [Test]
        public void VegetationMaterial_LoadsFromResources_WithFourLayerArrays()
        {
            var mat = IslandLifeSystem.VegetationMaterial;
            Assert.AreEqual("Drift/Vegetation", mat.shader.name);
            var detail = mat.GetTexture("_DetailArray") as Texture2DArray;
            var normal = mat.GetTexture("_NormalArray") as Texture2DArray;
            Assert.IsNotNull(detail, "detail array assigned");
            Assert.IsNotNull(normal, "normal array assigned");
            Assert.AreEqual(4, detail.depth);
            Assert.AreEqual(4, normal.depth);
            Assert.LessOrEqual(detail.width, 1024);
            Assert.Greater(detail.mipmapCount, 1, "mipmaps against shimmer");
            Assert.AreEqual(1f, mat.GetFloat("_DetailAmount"));
            Assert.AreEqual(0f, LifeMeshes.Material.GetFloat("_Sway"), "the shared vertex-colour material is unchanged");
        }
    }
}
