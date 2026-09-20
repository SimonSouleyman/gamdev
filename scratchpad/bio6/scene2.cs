var streamer = UnityEngine.Object.FindAnyObjectByType<Drift.Islands.WorldStreamer>();
streamer.StreamAround(true);
var sb = new System.Text.StringBuilder();
var list = new System.Collections.Generic.List<Drift.Islands.Island>();
System.Action<UnityEngine.Transform> walk = null;
walk = tr =>
{
    var isl = tr.GetComponent<Drift.Islands.Island>();
    if (isl != null) list.Add(isl);
    for (int i = 0; i < tr.childCount; i++) walk(tr.GetChild(i));
};
foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()) walk(root.transform);
sb.Append("islands=").Append(list.Count).Append(" live=").Append(streamer.LiveIslands).Append('\n');
foreach (var i in list)
    sb.Append(i.name).Append(" seed=").Append(i.shapeSeed)
      .Append(" biome=").Append((Drift.Core.LifeBiome)i.Biome)
      .Append(" area=").Append(i.LandArea.ToString("F1"))
      .Append(" pos=").Append(i.PlanarPosition.ToString("F1"))
      .Append(" player=").Append(i.useKeyboardInput).Append('\n');
return sb.ToString();
