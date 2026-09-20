using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Drift.Islands;
using Drift.Life;
using Drift.Tectonics;
using Drift.Visuals;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Drift.EditorTools
{
    // Measurement helper behind Docs/PERF_BASELINE.md. Editor-only, driven from `unity command eval`
    // (e.g. `return Drift.EditorTools.PerfBaseline.Systems();`). Never call it in Play Mode: the
    // benches step the live systems and move islands, then put positions back themselves.
    public static class PerfBaseline
    {
        const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        public struct Sample
        {
            public string name;
            public double avgMs, minMs, maxMs;
            public long bytes;
            public int gcs, iters;
        }

        public const string Header = "| Call | avg ms | min ms | max ms | bytes/call |\n|---|---:|---:|---:|---:|";

        public static Sample Bench(string name, Action a, int iters = 60, int warm = 3)
        {
            for (int i = 0; i < warm; i++) a();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var sw = new Stopwatch();
            double min = double.MaxValue, max = 0, total = 0;
            int c0 = GC.CollectionCount(0);
            long m0 = GC.GetTotalMemory(false);
            for (int i = 0; i < iters; i++)
            {
                sw.Restart();
                a();
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds;
                total += ms;
                if (ms < min) min = ms;
                if (ms > max) max = ms;
            }
            long m1 = GC.GetTotalMemory(false);
            int gcs = GC.CollectionCount(0) - c0;
            long bytes = (m1 - m0) / iters;
            if (gcs > 0)
            {
                // A collection ran mid-loop: fall back to one isolated call for the allocation figure.
                GC.Collect();
                m0 = GC.GetTotalMemory(false);
                a();
                bytes = GC.GetTotalMemory(false) - m0;
            }
            return new Sample { name = name, avgMs = total / iters, minMs = min, maxMs = max, bytes = bytes, gcs = gcs, iters = iters };
        }

        public static string Row(Sample s) =>
            $"| {s.name} | {s.avgMs:F3} | {s.minMs:F3} | {s.maxMs:F3} | {s.bytes}{(s.gcs > 0 ? " (single call, GC ran)" : "")} |";

        static Action Method(object target, string name)
        {
            var m = target.GetType().GetMethod(name, Priv, null, Type.EmptyTypes, null);
            if (m == null) throw new MissingMethodException(target.GetType().Name, name);
            return (Action)Delegate.CreateDelegate(typeof(Action), target, m);
        }

        public static Island Player()
        {
            foreach (var i in Island.All) if (i != null && i.useKeyboardInput) return i;
            return null;
        }

        static float DistToCam(Transform t)
        {
            var c = Camera.main;
            if (c == null) return 0f;
            Vector3 a = c.transform.position, b = t.position;
            return new Vector2(a.x - b.x, a.z - b.z).magnitude;
        }

        // ------------------------------------------------------------ per-frame CPU

        public static string Systems(float dt = 1f / 60f, int iters = 60)
        {
            var sb = new StringBuilder();
            var islands = new List<Island>();
            foreach (var i in Island.All) if (i != null && i.isActiveAndEnabled) islands.Add(i);
            var player = Player();
            var herds = new List<IslandHerdSystem>();
            var lifes = new List<IslandLifeSystem>();
            int animals = 0, herdCount = 0, plants = 0, near = 0;
            foreach (var isl in islands)
            {
                var h = isl.GetComponent<IslandHerdSystem>();
                var l = isl.GetComponent<IslandLifeSystem>();
                if (h != null) { herds.Add(h); animals += h.AnimalCount; herdCount += h.HerdCount; }
                if (l != null) { lifes.Add(l); plants += l.PlantCount; }
                if (DistToCam(isl.transform) < 100f) near++;
            }
            sb.AppendLine($"islands={islands.Count} near(<100u)={near} herds={herdCount} animals={animals} plants={plants} playerArea={(player != null ? player.LandArea : 0f):F1} dt={dt:F4} iters={iters}");
            sb.AppendLine(Header);

            var pHerd = player != null ? player.GetComponent<IslandHerdSystem>() : null;
            var pLife = player != null ? player.GetComponent<IslandLifeSystem>() : null;

            sb.AppendLine(Row(Bench("IslandHerdSystem.Step (all islands)", () => { foreach (var h in herds) h.Step(dt); }, iters)));
            if (pHerd != null)
            {
                sb.AppendLine(Row(Bench("IslandHerdSystem.Step (player)", () => pHerd.Step(dt), iters)));
                sb.AppendLine(Row(Bench("IslandHerdSystem.RebuildMesh (player)", Method(pHerd, "RebuildMesh"), iters)));
            }
            sb.AppendLine(Row(Bench("IslandLifeSystem.Step (all islands)", () => { foreach (var l in lifes) l.Step(dt); }, iters)));
            if (pLife != null)
            {
                sb.AppendLine(Row(Bench("IslandLifeSystem.Step (player)", () => pLife.Step(dt), iters)));
                sb.AppendLine(Row(Bench("IslandLifeSystem.Tick (player, ldt=1.2)", () => pLife.Tick(1.2f), iters)));
                sb.AppendLine(Row(Bench("IslandLifeSystem.RebuildVegetationMesh (player)", Method(pLife, "RebuildVegetationMesh"), iters)));
                sb.AppendLine(Row(Bench("IslandLifeSystem.ApplyTint (player)", Method(pLife, "ApplyTint"), iters)));
            }

            var flock = Object.FindAnyObjectByType<FlockSystem>();
            if (flock != null)
            {
                sb.AppendLine(Row(Bench("FlockSystem.Step", () => flock.Step(dt), iters)));
                sb.AppendLine(Row(Bench("FlockSystem.RebuildMesh", Method(flock, "RebuildMesh"), iters)));
            }
            sb.Append(Critters(dt, iters));
            var fish = Object.FindAnyObjectByType<FishSystem>();
            if (fish != null)
            {
                sb.AppendLine(Row(Bench("FishSystem.Step", () => fish.Step(dt), iters)));
                sb.AppendLine(Row(Bench("FishSystem.Rebuild", Method(fish, "Rebuild"), iters)));
            }
            var water = Object.FindAnyObjectByType<WaterFeedback>();
            if (water != null) sb.AppendLine(Row(Bench("WaterFeedback.Step", () => water.Step(dt), iters)));
            var clouds = Object.FindAnyObjectByType<CloudShadows>();
            if (clouds != null) sb.AppendLine(Row(Bench("CloudShadows.Step", () => clouds.Step(dt), iters)));
            var plates = PlateSystem.Instance;
            if (plates != null)
            {
                sb.AppendLine(Row(Bench("PlateSystem.Step", () => plates.Step(dt), iters)));
                sb.AppendLine(Row(Bench("PlateSystem.ComputeBorders", Method(plates, "ComputeBorders"), iters)));
                sb.AppendLine(Row(Bench("PlateSystem.RebuildBorderMesh", Method(plates, "RebuildBorderMesh"), iters)));
            }
            var storms = StormSystem.Instance;
            if (storms != null) sb.AppendLine(Row(Bench("StormSystem.Step", () => storms.Step(dt), iters)));
            var world = Object.FindAnyObjectByType<IslandWorld>();
            if (world != null) sb.AppendLine(Row(Bench("IslandWorld.Step", () => world.Step(dt), iters)));
            var streamer = Object.FindAnyObjectByType<WorldStreamer>();
            if (streamer != null) sb.AppendLine(Row(Bench("WorldStreamer.StreamAround(false)", () => streamer.StreamAround(false), iters)));

            // Tick moves islands with the plate current; positions go back afterwards.
            var saved = new List<Vector2>(islands.Count);
            foreach (var i in islands) saved.Add(i.PlanarPosition);
            sb.AppendLine(Row(Bench("Island.Tick (all islands)", () => { foreach (var i in islands) i.Tick(Vector2.zero, dt); }, iters)));
            if (player != null)
            {
                sb.AppendLine(Row(Bench("Island.Tick (player)", () => player.Tick(Vector2.zero, dt), iters)));
                sb.AppendLine(Row(Bench("Island.AdvanceSink (player)", () => player.AdvanceSink(dt), iters)));
                sb.AppendLine(Row(Bench("Island.RebuildMesh (player)", Method(player, "RebuildMesh"), iters)));
                sb.AppendLine(Row(Bench("Island.RecomputeStats (player)", Method(player, "RecomputeStats"), iters)));
            }
            for (int k = 0; k < islands.Count; k++) if (islands[k] != null) islands[k].SetPlanarPosition(saved[k]);
            return sb.ToString();
        }

        // Phase 4: IslandCrittersSystem step over all islands, plus step and rebuild per near island (rows only,
        // no header, so Systems() can append it). Islands without the component are skipped.
        public static string Critters(float dt = 1f / 60f, int iters = 60, bool header = false)
        {
            var sb = new StringBuilder();
            if (header) sb.AppendLine(Header);
            var all = new List<IslandCrittersSystem>();
            int crabs = 0, turtles = 0, butterflies = 0, fireflies = 0, verts = 0;
            foreach (var isl in Island.All)
            {
                if (isl == null || !isl.isActiveAndEnabled) continue;
                var c = isl.GetComponent<IslandCrittersSystem>();
                if (c == null) continue;
                all.Add(c);
                crabs += c.CrabCount; turtles += c.TurtleCount; butterflies += c.ButterflyCount; fireflies += c.FireflyCount;
                verts += c.MeshVertexCount;
            }
            if (all.Count == 0) return header ? sb.AppendLine("| (no IslandCrittersSystem in the scene) | | | | |").ToString() : "";
            sb.AppendLine(Row(Bench($"IslandCrittersSystem.Step (all {all.Count} islands; crabs {crabs} turtles {turtles} butterflies {butterflies} fireflies {fireflies}, {verts} verts)", () => { foreach (var c in all) c.Step(dt); }, iters)));
            foreach (var c in all)
            {
                if (c.Tier != Drift.Core.LifeTier.Near) continue;
                string n = $"{c.name} (crabs {c.CrabCount} turtles {c.TurtleCount} butterflies {c.ButterflyCount} fireflies {c.FireflyCount}, {c.MeshVertexCount} verts)";
                sb.AppendLine(Row(Bench("IslandCrittersSystem.Step " + n, () => c.Step(dt), iters)));
                sb.AppendLine(Row(Bench("IslandCrittersSystem.RebuildMesh " + n, Method(c, "RebuildMesh"), iters)));
            }
            return sb.ToString();
        }

        public static string PerIsland(float dt = 1f / 60f, int iters = 60)
        {
            var sb = new StringBuilder();
            sb.AppendLine("| Island | kind | area | dist | herds | animals | plants | herd verts | veg verts | terrain verts | Herd.Step ms | Herd.RebuildMesh ms | Life.Step ms | Life.RebuildVeg ms | Life.Tick ms |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var isl in new List<Island>(Island.All))
            {
                if (isl == null || !isl.isActiveAndEnabled) continue;
                var h = isl.GetComponent<IslandHerdSystem>();
                var l = isl.GetComponent<IslandLifeSystem>();
                var mf = isl.GetComponent<MeshFilter>();
                if (h == null || l == null) continue;
                var hs = Bench("", () => h.Step(dt), iters);
                var hr = Bench("", Method(h, "RebuildMesh"), iters);
                var ls = Bench("", () => l.Step(dt), iters);
                var lr = Bench("", Method(l, "RebuildVegetationMesh"), iters);
                var lt = Bench("", () => l.Tick(1.2f), 10);
                sb.AppendLine($"| {isl.name} | {isl.kind} | {isl.LandArea:F0} | {DistToCam(isl.transform):F0} | {h.HerdCount} | {h.AnimalCount} | {l.PlantCount} | {h.MeshVertexCount} | {l.MeshVertexCount} | {(mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : 0)} | {hs.avgMs:F3} | {hr.avgMs:F3} | {ls.avgMs:F3} | {lr.avgMs:F3} | {lt.avgMs:F3} |");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------ rendering

        static void Walk(Transform t, List<Transform> into)
        {
            into.Add(t);
            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), into);
        }

        static string Category(string meshName)
        {
            switch (meshName)
            {
                case "IslandTerrain": return "island terrain";
                case "Vegetation": return "vegetation";
                case "Herds": return "herds";
                case "Flocks": return "flocks";
                case "Critters": return "critters";
                case "FishSchools": return "fish";
                case "PlateBorders": return "plate borders";
                case "Plane": return "water";
                default: return "other:" + meshName;
            }
        }

        class Cat
        {
            public int count, visible, enabled, verts, tris, maxVerts;
            public long bytes;
            public string shader = "", biggest = "";
        }

        public static string Meshes()
        {
            var sb = new StringBuilder();
            var all = new List<Transform>();
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects()) Walk(go.transform, all);
            var cam = Camera.main;
            var planes = cam != null ? GeometryUtility.CalculateFrustumPlanes(cam) : null;
            var cats = new SortedDictionary<string, Cat>();
            int renderers = 0, inFrustum = 0, shadowCasters = 0;
            foreach (var t in all)
            {
                var mf = t.GetComponent<MeshFilter>();
                var mr = t.GetComponent<MeshRenderer>();
                if (mf == null || mr == null || mf.sharedMesh == null) continue;
                var m = mf.sharedMesh;
                string cat = Category(m.name);
                if (!cats.TryGetValue(cat, out var c)) cats[cat] = c = new Cat();
                int tris = 0;
                for (int s = 0; s < m.subMeshCount; s++) tris += (int)(m.GetIndexCount(s) / 3);
                bool on = mr.enabled && t.gameObject.activeInHierarchy;
                bool vis = on && planes != null && GeometryUtility.TestPlanesAABB(planes, mr.bounds) && m.vertexCount > 0;
                c.count++;
                if (on) c.enabled++;
                if (vis) c.visible++;
                c.verts += m.vertexCount;
                c.tris += tris;
                c.bytes += Profiler.GetRuntimeMemorySizeLong(m);
                if (m.vertexCount > c.maxVerts) { c.maxVerts = m.vertexCount; c.biggest = t.parent != null ? t.parent.name + "/" + t.name : t.name; }
                if (mr.sharedMaterial != null && mr.sharedMaterial.shader != null) c.shader = mr.sharedMaterial.shader.name;
                renderers++;
                if (vis) inFrustum++;
                if (vis && mr.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) shadowCasters++;
            }
            sb.AppendLine($"scene renderers with a mesh={renderers} enabled+in main-camera frustum={inFrustum} of which shadow casters={shadowCasters}");
            sb.AppendLine("| Mesh type | renderers | enabled | in frustum | total verts | total tris | max verts (object) | mesh bytes | shader |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---|---:|---|");
            int tv = 0, tt = 0;
            long tb = 0;
            foreach (var kv in cats)
            {
                var c = kv.Value;
                tv += c.verts; tt += c.tris; tb += c.bytes;
                sb.AppendLine($"| {kv.Key} | {c.count} | {c.enabled} | {c.visible} | {c.verts} | {c.tris} | {c.maxVerts} ({c.biggest}) | {c.bytes} | {c.shader} |");
            }
            sb.AppendLine($"| total | | | {inFrustum} | {tv} | {tt} | | {tb} | |");
            return sb.ToString();
        }

        public static string RenderStats()
        {
            var sb = new StringBuilder();
            var t = typeof(Editor).Assembly.GetType("UnityEditor.UnityStats");
            if (t == null) return "UnityStats not found";
            string[] names = { "drawCalls", "setPassCalls", "srpBatcherDrawCalls", "standardDrawCalls", "standardInstancedDrawCalls", "nullGeometryDrawCalls", "dynamicBatchedDrawCalls", "staticBatchedDrawCalls", "instancedBatchedDrawCalls", "triangles", "vertices", "shadowCasters", "renderTextureChanges", "visibleSkinnedMeshes" };
            foreach (var n in names)
            {
                var p = t.GetProperty(n, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null) sb.Append(n + "=" + p.GetValue(null) + " ");
            }
            return sb.ToString();
        }

        public static string Materials()
        {
            var sb = new StringBuilder();
            var all = new List<Transform>();
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects()) Walk(go.transform, all);
            var seen = new HashSet<Material>();
            var su = typeof(ShaderUtil);
            var code = su.GetMethod("GetSRPBatcherCompatibilityCode", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var reason = su.GetMethod("GetSRPBatcherCompatibilityIssueReason", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            sb.AppendLine($"SRP batcher enabled in GraphicsSettings={UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching} pipeline={(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name : "null")} quality={QualitySettings.names[QualitySettings.GetQualityLevel()]}");
            sb.AppendLine("| Material | shader | GPU instancing | SRP batcher compatible | property block | renderers |");
            sb.AppendLine("|---|---|---|---|---|---:|");
            var counts = new Dictionary<Material, int>();
            var mpb = new Dictionary<Material, bool>();
            foreach (var t in all)
            {
                var r = t.GetComponent<Renderer>();
                if (r == null || r.sharedMaterial == null) continue;
                counts[r.sharedMaterial] = counts.TryGetValue(r.sharedMaterial, out var n) ? n + 1 : 1;
                if (r.HasPropertyBlock()) mpb[r.sharedMaterial] = true;
            }
            foreach (var kv in counts)
            {
                var m = kv.Key;
                if (!seen.Add(m)) continue;
                string compat = "?";
                if (code != null && m.shader != null)
                {
                    int c = (int)code.Invoke(null, new object[] { m.shader, 0 });
                    compat = c == 0 ? "yes" : "no: " + (reason != null ? reason.Invoke(null, new object[] { m.shader, 0, c }) : c.ToString());
                }
                sb.AppendLine($"| {m.name} | {(m.shader != null ? m.shader.name : "null")} | {m.enableInstancing} | {compat} | {(mpb.ContainsKey(m) ? "yes" : "no")} | {kv.Value} |");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------ memory

        public static string Memory()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"GC.GetTotalMemory={GC.GetTotalMemory(false) / 1048576f:F1} MB monoUsed={Profiler.GetMonoUsedSizeLong() / 1048576f:F1} MB monoHeap={Profiler.GetMonoHeapSizeLong() / 1048576f:F1} MB totalAllocated={Profiler.GetTotalAllocatedMemoryLong() / 1048576f:F1} MB totalReserved={Profiler.GetTotalReservedMemoryLong() / 1048576f:F1} MB");
            var meshes = Resources.FindObjectsOfTypeAll<Mesh>();
            var ours = new SortedDictionary<string, (int n, long bytes, int verts)>();
            long total = 0;
            foreach (var m in meshes)
            {
                total += Profiler.GetRuntimeMemorySizeLong(m);
                string cat = Category(m.name);
                if (cat.StartsWith("other")) continue;
                ours.TryGetValue(cat, out var e);
                ours[cat] = (e.n + 1, e.bytes + Profiler.GetRuntimeMemorySizeLong(m), e.verts + m.vertexCount);
            }
            sb.AppendLine($"live Mesh objects (Resources.FindObjectsOfTypeAll, includes editor/built-in meshes)={meshes.Length} totalRuntimeBytes={total / 1048576f:F1} MB");
            sb.AppendLine("| Drift mesh type | live meshes | verts | runtime bytes |");
            sb.AppendLine("|---|---:|---:|---:|");
            foreach (var kv in ours) sb.AppendLine($"| {kv.Key} | {kv.Value.n} | {kv.Value.verts} | {kv.Value.bytes} |");
            return sb.ToString();
        }

        // ------------------------------------------------------------ managed allocation

        // Mono's GC.GetTotalMemory only moves when a collection runs and GetAllocatedBytesForCurrentThread
        // returns 0 here, so allocations are read from the profiler's per-frame counter instead: AllocRun
        // remembers which profiler frame it ran in, AllocRead (a later eval) fetches that frame's total.
        // Compare n = 0 against n = 2000 of the same action; the eval machinery's own allocation cancels out.
        static Unity.Profiling.ProfilerRecorder _rec;
        static long _mark = -1;

        public static string AllocBegin()
        {
            Profiler.enabled = true;
            if (_rec.Valid) _rec.Dispose();
            _rec = new Unity.Profiling.ProfilerRecorder(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame", 200000);
            _rec.Start();
            return $"recorder valid={_rec.Valid} profiler={Profiler.enabled}";
        }

        public static string AllocEnd()
        {
            if (_rec.Valid) _rec.Dispose();
            Profiler.enabled = false;
            return "stopped";
        }

        public static string AllocRead()
        {
            if (!_rec.Valid) return "no recorder";
            long v = _mark >= 0 && _rec.Count > _mark ? _rec.GetSample((int)_mark).Value : -1;
            return $"count={_rec.Count} mark={_mark} bytesInFrame={v}";
        }

        public static string AllocRun(int which, int n)
        {
            float dt = 1f / 60f;
            var islands = new List<Island>();
            foreach (var i in Island.All) if (i != null && i.isActiveAndEnabled) islands.Add(i);
            var player = Player();
            var herds = new List<IslandHerdSystem>();
            var lifes = new List<IslandLifeSystem>();
            foreach (var isl in islands)
            {
                var h = isl.GetComponent<IslandHerdSystem>();
                var l = isl.GetComponent<IslandLifeSystem>();
                if (h != null) herds.Add(h);
                if (l != null) lifes.Add(l);
            }
            var pHerd = player != null ? player.GetComponent<IslandHerdSystem>() : null;
            var pLife = player != null ? player.GetComponent<IslandLifeSystem>() : null;
            var flock = Object.FindAnyObjectByType<FlockSystem>();
            var fish = Object.FindAnyObjectByType<FishSystem>();
            var water = Object.FindAnyObjectByType<WaterFeedback>();
            var clouds = Object.FindAnyObjectByType<CloudShadows>();
            var plates = PlateSystem.Instance;
            var world = Object.FindAnyObjectByType<IslandWorld>();
            var streamer = Object.FindAnyObjectByType<WorldStreamer>();
            var saved = new List<Vector2>(islands.Count);
            foreach (var i in islands) saved.Add(i.PlanarPosition);
            byte[] keep = null;

            Action a;
            string name;
            switch (which)
            {
                case 1: name = "new byte[1024]"; a = () => { keep = new byte[1024]; keep[0] = 1; }; break;
                case 2: name = "IslandHerdSystem.Step (all)"; a = () => { foreach (var h in herds) h.Step(dt); }; break;
                case 3: name = "IslandLifeSystem.Step (all)"; a = () => { foreach (var l in lifes) l.Step(dt); }; break;
                case 4: name = "FlockSystem.Step"; a = () => flock.Step(dt); break;
                case 5: name = "FishSystem.Step"; a = () => fish.Step(dt); break;
                case 6: name = "WaterFeedback.Step"; a = () => water.Step(dt); break;
                case 7: name = "CloudShadows.Step"; a = () => clouds.Step(dt); break;
                case 8: name = "PlateSystem.Step"; a = () => plates.Step(dt); break;
                case 9: name = "IslandWorld.Step"; a = () => world.Step(dt); break;
                case 10: name = "WorldStreamer.StreamAround(false)"; a = () => streamer.StreamAround(false); break;
                case 11: name = "Island.Tick (all)"; a = () => { foreach (var i in islands) i.Tick(Vector2.zero, dt); }; break;
                case 12: name = "IslandHerdSystem.RebuildMesh (player)"; a = Method(pHerd, "RebuildMesh"); break;
                case 13: name = "IslandLifeSystem.RebuildVegetationMesh (player)"; a = Method(pLife, "RebuildVegetationMesh"); break;
                case 14: name = "IslandHerdSystem.Step (player)"; a = () => pHerd.Step(dt); break;
                case 15: name = "IslandLifeSystem.Step (player)"; a = () => pLife.Step(dt); break;
                case 16: name = "FlockSystem.RebuildMesh"; a = Method(flock, "RebuildMesh"); break;
                case 17: name = "FishSystem.Rebuild"; a = Method(fish, "Rebuild"); break;
                case 18: name = "Island.RebuildMesh (player)"; a = Method(player, "RebuildMesh"); break;
                case 19: name = "IslandLifeSystem.Tick (player)"; a = () => pLife.Tick(1.2f); break;
                case 20: name = "Island.AdvanceSink (player)"; a = () => player.AdvanceSink(dt); break;
                case 21: name = "StormSystem.Step"; a = () => StormSystem.Instance.Step(dt); break;
                case 22: name = "IslandLifeSystem.ApplyTint (player)"; a = Method(pLife, "ApplyTint"); break;
                case 23:
                {
                    var critters = new List<IslandCrittersSystem>();
                    foreach (var isl in islands) { var c = isl.GetComponent<IslandCrittersSystem>(); if (c != null) critters.Add(c); }
                    name = "IslandCrittersSystem.Step (all)"; a = () => { foreach (var c in critters) c.Step(dt); };
                    break;
                }
                case 24:
                {
                    var pc = player != null ? player.GetComponent<IslandCrittersSystem>() : null;
                    name = "IslandCrittersSystem.RebuildMesh (player)"; a = pc != null ? Method(pc, "RebuildMesh") : () => { };
                    break;
                }
                default: name = "nothing"; a = () => { }; break;
            }
            _mark = _rec.Valid ? _rec.Count : -1;
            for (int i = 0; i < n; i++) a();
            for (int k = 0; k < islands.Count; k++) if (islands[k] != null) islands[k].SetPlanarPosition(saved[k]);
            return $"{name} x{n} mark={_mark} keep={(keep != null ? keep.Length : 0)}";
        }

        // ------------------------------------------------------------ big island

        // Merges the largest remaining AI islands into the player until it reaches targetArea. Each guest
        // is parked touching the player on a golden-angle spiral so the result stays roughly round.
        public static string BuildBigIsland(float targetArea)
        {
            var sb = new StringBuilder();
            var player = Player();
            if (player == null) return "no player";
            int merges = 0;
            while (player.LandArea < targetArea && merges < 60)
            {
                Island best = null;
                foreach (var i in Island.All)
                    if (i != null && i != player && i.isActiveAndEnabled && !i.IsEmerging && (best == null || i.LandArea > best.LandArea)) best = i;
                if (best == null) break;
                float ang = merges * 2.399963f;
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                best.SetPlanarPosition(player.PlanarPosition + dir * (player.BoundingRadius + best.BoundingRadius - 1.5f));
                float before = player.LandArea;
                string guest = $"{best.name} ({best.kind}, area {best.LandArea:F0})";
                player.MergeFrom(best, 2f, 0f);
                player.FinishUplift();
                merges++;
                sb.AppendLine($"merge {merges}: {guest} area {before:F0} -> {player.LandArea:F0}, bounds {player.LocalBounds.size.x:F0}x{player.LocalBounds.size.y:F0}, radius {player.BoundingRadius:F1}");
            }
            return sb.ToString();
        }

        public static string RestoreWorld()
        {
            var player = Player();
            if (player != null) player.ResetToStart();
            var streamer = Object.FindAnyObjectByType<WorldStreamer>();
            if (streamer != null) { streamer.ResetWorld(); streamer.StreamAround(true); }
            var flock = Object.FindAnyObjectByType<FlockSystem>();
            if (flock != null) flock.Reseed(flock.seed);
            var plates = PlateSystem.Instance;
            if (plates != null) { plates.enabled = false; plates.enabled = true; }
            var cam = Object.FindAnyObjectByType<IslandChaseCamera>();
            if (cam != null) cam.SnapToTarget();
            return $"player {(player != null ? player.PlanarPosition.ToString() : "none")} area {(player != null ? player.LandArea : 0f):F1} seed {(player != null ? player.shapeSeed : 0)} islands={Island.All.Count} chunks={(streamer != null ? streamer.LoadedChunks : 0)}";
        }
    }
}
