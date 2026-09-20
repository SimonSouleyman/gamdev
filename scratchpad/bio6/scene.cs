var sb = new System.Text.StringBuilder();
var islands = UnityEngine.Object.FindObjectsByType<Drift.Islands.Island>(UnityEngine.FindObjectsInactive.Include);
sb.Append("islands=").Append(islands.Length).Append('\n');
foreach (var i in islands)
{
    sb.Append(i.name)
      .Append(" seed=").Append(i.shapeSeed)
      .Append(" biome=").Append((Drift.Core.LifeBiome)i.Biome)
      .Append(" kind=").Append(i.Character)
      .Append(" area=").Append(i.LandArea.ToString("F1"))
      .Append(" pos=").Append(i.transform.position.ToString("F1"))
      .Append(" player=").Append(i.useKeyboardInput)
      .Append(" life=").Append(i.GetComponent<Drift.Life.IslandLifeSystem>() != null)
      .Append('\n');
}
var streamer = UnityEngine.Object.FindAnyObjectByType<Drift.Islands.WorldStreamer>();
sb.Append("streamer=").Append(streamer != null ? streamer.name : "none").Append('\n');
var cam = UnityEngine.Camera.main;
sb.Append("cam=").Append(cam != null ? cam.name + " " + cam.transform.position.ToString("F1") : "none").Append('\n');
return sb.ToString();
