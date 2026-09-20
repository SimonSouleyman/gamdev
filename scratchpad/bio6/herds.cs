// Which species stands on which biome of the merged island, and what a fresh repopulation founds.
var list = new System.Collections.Generic.List<Drift.Islands.Island>();
System.Action<UnityEngine.Transform> walk = null;
walk = tr =>
{
    var isl = tr.GetComponent<Drift.Islands.Island>();
    if (isl != null) list.Add(isl);
    for (int i = 0; i < tr.childCount; i++) walk(tr.GetChild(i));
};
foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()) walk(root.transform);
Drift.Islands.Island player = null;
foreach (var i in list) if (i.useKeyboardInput) player = i;
var life = player.GetComponent<Drift.Life.IslandLifeSystem>();
var herds = player.GetComponent<Drift.Life.IslandHerdSystem>();
var sb = new System.Text.StringBuilder();

System.Action<string> dump = label =>
{
    var tally = new System.Collections.Generic.Dictionary<string, int>();
    int matched = 0;
    for (int h = 0; h < herds.HerdCount; h++)
    {
        var kind = herds.HerdKind(h);
        int ground = life.BiomeAt(herds.HerdCenter(h));
        int home = Drift.Life.IslandHerdSystem.HomeBiomeOf(kind);
        if (home == ground) matched++;
        string k = (Drift.Core.LifeBiome)ground + ":" + kind;
        tally[k] = tally.TryGetValue(k, out int n) ? n + 1 : 1;
    }
    sb.Append(label).Append(" herds=").Append(herds.HerdCount).Append(" onOwnBiome=").Append(matched).Append("  ");
    foreach (var kv in tally) sb.Append(kv.Key).Append('x').Append(kv.Value).Append(' ');
    sb.Append('\n');
};

dump("after the merges ");
// Force a fresh founding round on the merged land.
var surface = player;
var v = typeof(Drift.Islands.Island).GetField("_version", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
v.SetValue(player, (int)v.GetValue(player) + 1);
herds.Repopulate();
dump("fresh repopulate");
sb.Append("plants: ");
foreach (Drift.Life.LifeKind k in System.Enum.GetValues(typeof(Drift.Life.LifeKind)))
    if (life.CountOfKind(k) > 0) sb.Append(Drift.Life.LifeNames.Of(k)).Append('=').Append(life.CountOfKind(k)).Append(' ');
return sb.ToString();
