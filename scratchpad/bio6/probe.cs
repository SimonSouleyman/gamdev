// How tight are the clusters on a small island across seeds, at founding vs after 200 steps?
Drift.Core.LifeLod.DistanceProvider = _ => 0f;
var sb = new System.Text.StringBuilder();
int bad0 = 0, bad200 = 0, n = 0;
float worst0 = -99f, worst200 = -99f;
for (int seed = 1; seed <= 40; seed++)
{
    var go = new UnityEngine.GameObject("Probe" + seed);
    go.SetActive(false);
    var surface = go.AddComponent<Drift.Tests.FakeIslandSurface>();
    surface.radius = 4f;
    var life = go.AddComponent<Drift.Life.IslandLifeSystem>();
    life.seed = seed;
    var herds = go.AddComponent<Drift.Life.IslandHerdSystem>();
    herds.seed = seed;
    go.SetActive(true);
    float o0 = -99f;
    for (int h = 0; h < herds.HerdCount; h++)
        o0 = UnityEngine.Mathf.Max(o0, herds.HerdRadius(h) - (herds.BodyLength(h) * herds.formationSpacing * UnityEngine.Mathf.Sqrt(herds.HerdSize(h) + 1) + 0.05f));
    for (int t = 0; t < 200; t++) herds.Step(0.05f);
    float o2 = -99f;
    for (int h = 0; h < herds.HerdCount; h++)
        o2 = UnityEngine.Mathf.Max(o2, herds.HerdRadius(h) - (herds.BodyLength(h) * herds.formationSpacing * UnityEngine.Mathf.Sqrt(herds.HerdSize(h) + 1) + 0.05f));
    n++;
    if (o0 > 0f) bad0++;
    if (o2 > 0f) { bad200++; sb.Append(seed).Append("=").Append(o2.ToString("F3")).Append(" "); }
    worst0 = UnityEngine.Mathf.Max(worst0, o0);
    worst200 = UnityEngine.Mathf.Max(worst200, o2);
    UnityEngine.Object.DestroyImmediate(go);
}
Drift.Core.LifeLod.DistanceProvider = null;
return "seeds=" + n + " overAtFounding=" + bad0 + " (worst " + worst0.ToString("F3") + ")  overAfter200=" + bad200 + " (worst " + worst200.ToString("F3") + ")\n" + sb;
