var session = UnityEngine.Object.FindAnyObjectByType<Drift.SaveSystem.GameSession>();
string what = "none";
if (session != null) { session.Restart(); what = "GameSession.Restart"; }
var streamer = UnityEngine.Object.FindAnyObjectByType<Drift.Islands.WorldStreamer>();
if (streamer != null) { streamer.ResetWorld(); streamer.StreamAround(true); what += " + ResetWorld/StreamAround"; }
var chase = UnityEngine.Object.FindAnyObjectByType<Drift.Islands.IslandChaseCamera>();
if (chase != null) { chase.SnapToTarget(); what += " + SnapToTarget"; }
var list = new System.Collections.Generic.List<Drift.Islands.Island>();
System.Action<UnityEngine.Transform> walk = null;
walk = tr =>
{
    var isl = tr.GetComponent<Drift.Islands.Island>();
    if (isl != null) list.Add(isl);
    for (int i = 0; i < tr.childCount; i++) walk(tr.GetChild(i));
};
foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()) walk(root.transform);
string p = "no player";
foreach (var i in list)
    if (i.useKeyboardInput)
        p = "player area=" + i.LandArea.ToString("F1") + " biome=" + (Drift.Core.LifeBiome)i.Biome
            + " biomesPresent=" + System.Convert.ToString(i.GetComponent<Drift.Life.IslandLifeSystem>().BiomesPresent, 2)
            + " pos=" + i.PlanarPosition.ToString("F1");
return what + "; islands=" + list.Count + "; " + p;
