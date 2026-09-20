// Step / vegetation-rebuild cost and save size on a one-biome island vs the same island after two merges.
Drift.Core.LifeLod.DistanceProvider = _ => 0f;
var sb = new System.Text.StringBuilder();

System.Func<float, int, int, UnityEngine.GameObject> make = (r, seed, biome) =>
{
    var go = new UnityEngine.GameObject("Perf" + seed);
    go.SetActive(false);
    var s = go.AddComponent<Drift.Tests.FakeIslandSurface>();
    s.radius = r;
    s.biome = biome;
    var l = go.AddComponent<Drift.Life.IslandLifeSystem>();
    l.seed = seed;
    var h = go.AddComponent<Drift.Life.IslandHerdSystem>();
    h.seed = seed;
    go.SetActive(true);
    return go;
};

var rebuild = typeof(Drift.Life.IslandLifeSystem).GetMethod("RebuildVegetationMesh", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var applyTint = typeof(Drift.Life.IslandLifeSystem).GetMethod("ApplyTint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

System.Action<string, UnityEngine.GameObject> report = (label, go) =>
{
    var life = go.GetComponent<Drift.Life.IslandLifeSystem>();
    var herds = go.GetComponent<Drift.Life.IslandHerdSystem>();
    for (int i = 0; i < 60; i++) { life.Step(0.05f); herds.Step(0.05f); }
    var sw = new System.Diagnostics.Stopwatch();
    int m0 = life.MeshBuilds;
    long g0 = System.GC.GetAllocatedBytesForCurrentThread();
    sw.Restart();
    for (int i = 0; i < 400; i++) life.Step(0.05f);
    sw.Stop();
    long g1 = System.GC.GetAllocatedBytesForCurrentThread();
    int m1 = life.MeshBuilds;
    double step = sw.Elapsed.TotalMilliseconds / 400.0;

    sw.Restart();
    for (int i = 0; i < 40; i++) rebuild.Invoke(life, null);
    sw.Stop();
    double veg = sw.Elapsed.TotalMilliseconds / 40.0;

    sw.Restart();
    for (int i = 0; i < 40; i++) applyTint.Invoke(life, null);
    sw.Stop();
    double tint = sw.Elapsed.TotalMilliseconds / 40.0;

    sw.Restart();
    for (int i = 0; i < 200; i++) life.Tick(0.3f);
    sw.Stop();
    double tick = sw.Elapsed.TotalMilliseconds / 200.0;

    var cap = life.Capture();
    string json = UnityEngine.JsonUtility.ToJson(cap);
    int runs = cap.biomeRuns == null ? 0 : cap.biomeRuns.Length;
    int runsChars = 0;
    for (int i = 0; i < runs; i++) runsChars += cap.biomeRuns[i].ToString().Length + 1;
    int rawChars = 0;
    for (int i = 0; i < cap.stage.Length; i++) rawChars += 2; // "b," per cell, one digit
    sb.Append(label)
      .Append(" cells=").Append(cap.stage.Length)
      .Append(" land=").Append(life.LandCells)
      .Append(" biomes=").Append(System.Convert.ToString(life.BiomesPresent, 2))
      .Append(" plants=").Append(life.LivePlantCount)
      .Append(" verts=").Append(life.MeshVertexCount)
      .Append("\n   Step=").Append(step.ToString("F4")).Append(" ms")
      .Append("  Tick=").Append(tick.ToString("F4")).Append(" ms")
      .Append("  vegRebuild=").Append(veg.ToString("F3")).Append(" ms")
      .Append("  ApplyTint=").Append(tint.ToString("F3")).Append(" ms")
      .Append("  alloc/frame=").Append((g1 - g0) / 400.0).Append(" B").Append("  meshBuilds/400steps=").Append(m1 - m0)
      .Append("\n   json=").Append(json.Length).Append(" chars, biomeRuns=").Append(runs)
      .Append(" ints (~").Append(runsChars).Append(" chars) vs raw byte[] ~").Append(rawChars).Append(" chars\n");
};

// One biome, grown island (the code path a normal island takes).
var solo = make(18.6f, 2100, 0);
solo.GetComponent<Drift.Life.IslandLifeSystem>().Simulate(900f, 15f);
report("solo temperate r=18.6 ", solo);

// Same island after absorbing a nordic and a savanna neighbour.
var host = make(8f, 2100, 0);
var g1o = make(5f, 2101, 2);
g1o.transform.position = new UnityEngine.Vector3(13.6f, 0f, 0f);
var g2o = make(5f, 2102, 3);
g2o.transform.position = new UnityEngine.Vector3(-13.6f, 0f, 0f);
var hl = host.GetComponent<Drift.Life.IslandLifeSystem>();
hl.Simulate(900f, 15f);
g1o.GetComponent<Drift.Life.IslandLifeSystem>().Simulate(900f, 15f);
g2o.GetComponent<Drift.Life.IslandLifeSystem>().Simulate(900f, 15f);
hl.AbsorbFrom(g1o.GetComponent<Drift.Life.IslandLifeSystem>());
host.GetComponent<Drift.Life.IslandHerdSystem>().AbsorbFrom(g1o.GetComponent<Drift.Life.IslandHerdSystem>());
hl.AbsorbFrom(g2o.GetComponent<Drift.Life.IslandLifeSystem>());
host.GetComponent<Drift.Life.IslandHerdSystem>().AbsorbFrom(g2o.GetComponent<Drift.Life.IslandHerdSystem>());
host.GetComponent<Drift.Tests.FakeIslandSurface>().radius = 18.6f;
host.GetComponent<Drift.Tests.FakeIslandSurface>().version++;
hl.Tick(1f);
hl.Simulate(1200f, 15f);
report("merged 3 biomes r=18.6", host);

UnityEngine.Object.DestroyImmediate(solo);
UnityEngine.Object.DestroyImmediate(host);
UnityEngine.Object.DestroyImmediate(g1o);
UnityEngine.Object.DestroyImmediate(g2o);
Drift.Core.LifeLod.DistanceProvider = null;
return sb.ToString();
