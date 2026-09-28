using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Owner, 2026-09-24: "Der Hafen ist im Moment nur ein Steg" - a fisherman's hut at the land end of the jetty with
    // barrels, crates, rope and a lobster pot round it.
    public class HarbourTests
    {
        readonly List<GameObject> _objects = new();
        readonly List<Vector4> _parts = new();

        [SetUp]
        public void SetUp()
        {
            LifeLod.DistanceProvider = _ => 0f;
            LifeEnvironment.NightProvider = () => 0f;
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

        IslandSettlementSystem Make(float radius, float beach, int seed, bool mature)
        {
            var go = new GameObject("HarbourIsle");
            go.SetActive(false);
            go.transform.position = new Vector3(-20f, 0f, 14f);
            var surface = go.AddComponent<FakeIslandSurface>();
            surface.radius = radius;
            surface.beach = beach;
            var life = go.AddComponent<IslandLifeSystem>();
            life.seed = seed;
            go.SetActive(true);
            _objects.Add(go);
            if (mature) life.Simulate(900f, 10f);
            go.SetActive(false);
            var s = go.AddComponent<IslandSettlementSystem>();
            s.seed = seed;
            s.prehistory = false;
            go.SetActive(true);
            return s;
        }

        static void Run(IslandSettlementSystem s, float seconds, float dt = 0.25f)
        {
            int n = Mathf.CeilToInt(seconds / dt);
            for (int i = 0; i < n; i++) s.Step(dt);
        }

        static int DockIndex(IslandSettlementSystem s)
        {
            for (int i = 0; i < s.BuildingCount; i++)
                if (s.BuildingKindOf(i) == BuildingKind.Dock && s.BuildingStateOf(i) == BuildingState.Done) return i;
            return -1;
        }

        IslandSettlementSystem Harbour(float radius, float beach, int seed, bool village)
        {
            var s = Make(radius, beach, seed, village);
            if (village) Run(s, 200f);
            Assert.IsTrue(s.TryBuildLandmark(BuildingKind.Dock));
            Run(s, 300f);
            Assume.That(DockIndex(s) >= 0, "the jetty is finished");
            return s;
        }

        // Jetty frame of a point: lateral offset from the jetty's axis and distance out to sea from its shore end.
        static void JettyFrame(IslandSettlementSystem s, int dock, Vector2 p, out float lx, out float lz)
        {
            float yaw = s.BuildingYawOf(dock) * Mathf.Deg2Rad;
            Vector2 f = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)), d = p - s.BuildingPositionOf(dock);
            lz = Vector2.Dot(d, f);
            lx = Vector2.Dot(d, new Vector2(f.y, -f.x));
        }

        void AssertHarbourPlaced(IslandSettlementSystem s)
        {
            int dock = DockIndex(s);
            Assert.IsTrue(s.HasHarbourHut(dock), "the jetty got its fisherman's hut");
            int n = s.HarbourParts(dock, _parts);
            Assert.GreaterOrEqual(n, 4, "the hut and most of its clutter stand on dry ground");
            var surface = s.GetComponent<FakeIslandSurface>();
            float dk = s.ScaleOf(BuildingKind.Dock);
            float deckHalf = 0.06f * dk;
            for (int i = 0; i < n; i++)
            {
                Vector4 part = _parts[i];
                Vector2 p = new Vector2(part.x, part.y);
                string what = i == 0 ? "the hut" : "prop " + i;
                Assert.GreaterOrEqual(surface.SampleHeight(p), i == 0 ? s.hutDryHeight : 0.03f, what + " stands in the water");
                Assert.That(part.w, Is.EqualTo(surface.SampleHeight(p)).Within(i == 0 ? 0.1f : 1e-4f), what + " sits on its ground");
                JettyFrame(s, dock, p, out float lx, out float lz);
                Assert.Less(lz, 0.1f * dk, what + " is on the land side of the jetty");
                Assert.Greater(Mathf.Abs(lx) - part.z, deckHalf + 0.015f, what + " keeps the jetty and the walkway to it free");
                for (int j = 0; j < s.BuildingCount; j++)
                {
                    if (j == dock || s.BuildingStateOf(j) == BuildingState.Sinking) continue;
                    float d = (s.BuildingPositionOf(j) - p).magnitude;
                    Assert.Greater(d, part.z + s.BuildingRadiusOf(j) * 0.8f, what + " overlaps " + s.BuildingKindOf(j));
                }
            }
        }

        [Test]
        public void TheHarbourJettyGetsAFishermansHutOnDryLand()
        {
            var s = Harbour(8f, 2f, 47, true);
            AssertHarbourPlaced(s);
            Assert.IsTrue(s.BlocksShore(new Vector2(_parts[0].x, _parts[0].y), 0.01f), "ships and walkers go round the hut");
        }

        [Test]
        public void AHarbourWithoutAVillageGetsItsHutToo()
        {
            var s = Harbour(4f, 2f, 41, false);
            AssertHarbourPlaced(s);
        }

        [Test]
        public void TheHutStandsLevelOnSlopingGround()
        {
            var s = Harbour(8f, 2f, 47, true);
            int dock = DockIndex(s);
            Assume.That(s.HarbourParts(dock, _parts) > 0);
            var surface = s.GetComponent<FakeIslandSurface>();
            Vector2 hut = new Vector2(_parts[0].x, _parts[0].y);
            float k = s.buildingScale, lo = float.MaxValue, hi = float.MinValue;
            for (int a = 0; a < 8; a++)
            {
                float h = surface.SampleHeight(hut + new Vector2(Mathf.Cos(a * Mathf.PI / 4f), Mathf.Sin(a * Mathf.PI / 4f)) * (0.1f * k));
                lo = Mathf.Min(lo, h);
                hi = Mathf.Max(hi, h);
            }
            Assert.Greater(hi - lo, 0.01f, "the test beach slopes under the hut");
            float floor = _parts[0].w;
            Assert.LessOrEqual(hi - floor, 0.03f * k, "the floor is not buried on the uphill side");
            Assert.LessOrEqual(floor - lo, SettlementMeshes.HutStilts * k, "the stilts reach the ground on the downhill side");
        }

        [Test]
        public void ASteepShoreKeepsItsBareJetty()
        {
            var s = Make(8f, 0.6f, 47, true);
            Run(s, 200f);
            Assert.IsTrue(s.TryBuildLandmark(BuildingKind.Dock));
            Run(s, 300f);
            int dock = DockIndex(s);
            Assume.That(dock >= 0, "a jetty stands on the steep shore");
            Assert.IsFalse(s.HasHarbourHut(dock), "no room for a level hut on a cliff-like beach");
            Assert.AreEqual(0, s.HarbourParts(dock, _parts));
        }

        [Test]
        public void TheVillageGrowsAroundTheHarbourNotIntoIt()
        {
            var s = Harbour(8f, 2f, 47, true);
            Run(s, 1500f, 0.5f);
            AssertHarbourPlaced(s);
        }

        [Test]
        public void TheHutComesBackAfterALoad()
        {
            var s = Harbour(8f, 2f, 47, true);
            int dock = DockIndex(s);
            s.HarbourParts(dock, _parts);
            Vector4 hut = _parts[0];
            var data = s.Capture();
            s.Restore(data);
            dock = DockIndex(s);
            Assert.IsTrue(s.HasHarbourHut(dock));
            s.HarbourParts(dock, _parts);
            Assert.Less((new Vector2(_parts[0].x, _parts[0].y) - new Vector2(hut.x, hut.y)).magnitude, 1e-4f, "same spot after a load");
        }

        [Test]
        public void TheWatchViewTakesInJettyAndHut()
        {
            var s = Harbour(8f, 2f, 47, true);
            int dock = DockIndex(s);
            Assert.IsTrue(s.TryGetHarbourView(dock, out Vector3 centre, out float radius));
            Assert.IsTrue(s.TryGetDockWorld(out Vector3 end, out _));
            Assert.LessOrEqual(new Vector2(end.x - centre.x, end.z - centre.z).magnitude, radius + 1e-3f, "the jetty end is in view");
            s.HarbourParts(dock, _parts);
            for (int i = 0; i < _parts.Count; i++)
            {
                Vector3 w = s.transform.TransformPoint(_parts[i].x, _parts[i].w, _parts[i].y);
                Assert.LessOrEqual(new Vector2(w.x - centre.x, w.z - centre.z).magnitude + _parts[i].z, radius + 1e-3f, "part " + i + " is in view");
            }
            Assert.Less(radius, 1.2f, "still a close look");
            Assert.IsFalse(s.TryGetHarbourView(dock == 0 ? 1 : 0, out _, out _), "only jetties have a harbour view");
        }

        [Test]
        public void TheHutLanternGlowsAtNight()
        {
            float night = 0f;
            LifeEnvironment.NightProvider = () => night;
            var s = Harbour(4f, 2f, 41, false);
            Assume.That(s.HasHarbourHut(DockIndex(s)));
            s.RebuildMeshes();
            Assert.AreEqual(0, s.GlowHaloCount, "no halo by day");
            night = 0.9f;
            Run(s, 20f);
            s.RebuildMeshes();
            Assert.AreEqual(1, s.GlowHaloCount, "the lantern by the door is the jetty's one light");
        }

        [Test]
        public void TheHarbourStaysLowPoly()
        {
            int hut = SettlementMeshes.FishHut(1).vertices.Length + SettlementMeshes.FishHutGlow(1).vertices.Length;
            int props = 0;
            for (int i = 0; i < SettlementMeshes.HarbourPropCount; i++) props += SettlementMeshes.Prop((SettlementMeshes.HarbourProp)i).vertices.Length;
            Assert.LessOrEqual(hut, 520, "hut");
            Assert.LessOrEqual(props, 400, "clutter");
            Assert.LessOrEqual(SettlementMeshes.JettyGear.vertices.Length, 150, "bollard, line and coil on the jetty");

            var s = Harbour(8f, 2f, 47, true);
            Assert.LessOrEqual(s.MeshVertexCount, 12000);
        }

        [Test]
        public void BothHutsFaceOutwards()
        {
            for (int side = -1; side <= 1; side += 2)
                foreach (var t in new[] { SettlementMeshes.FishHut(side), SettlementMeshes.FishHutGlow(side) })
                    for (int i = 0; i < t.triangles.Length; i += 3)
                    {
                        Vector3 a = t.vertices[t.triangles[i]], b = t.vertices[t.triangles[i + 1]], c = t.vertices[t.triangles[i + 2]];
                        Vector3 n = Vector3.Cross(b - a, c - a);
                        if (n.sqrMagnitude < 1e-12f) continue;
                        Assert.Greater(Vector3.Dot(n, t.normals[t.triangles[i]]), 0f, "triangle " + i + " of the side " + side + " hut is wound inwards");
                    }
            var right = SettlementMeshes.FishHut(1).vertices;
            var left = SettlementMeshes.FishHut(-1).vertices;
            Assert.AreEqual(right.Length, left.Length);
            Assert.AreEqual(-right[5].x, left[5].x, 1e-6f, "the other side's hut is the mirror image");
        }
    }
}
