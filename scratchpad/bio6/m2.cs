// Stages one real merge of the player island with the named guest.
string guestName = "Island_0_0_0";
float dirX = -1f, dirZ = 0.2f;

var list = new System.Collections.Generic.List<Drift.Islands.Island>();
System.Action<UnityEngine.Transform> walk = null;
walk = tr =>
{
    var isl = tr.GetComponent<Drift.Islands.Island>();
    if (isl != null) list.Add(isl);
    for (int i = 0; i < tr.childCount; i++) walk(tr.GetChild(i));
};
foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()) walk(root.transform);

Drift.Islands.Island player = null, guest = null;
foreach (var i in list)
{
    if (i.useKeyboardInput) player = i;
    if (i.name == guestName) guest = i;
}
if (player == null) return "no player island";
if (guest == null) return "no guest " + guestName;

var life = player.GetComponent<Drift.Life.IslandLifeSystem>();
var gl = guest.GetComponent<Drift.Life.IslandLifeSystem>();
if (gl != null) gl.Simulate(900f, 20f);

var dir = new UnityEngine.Vector2(dirX, dirZ).normalized;
guest.SetPlanarPosition(player.PlanarPosition + dir * (player.BoundingRadius + guest.BoundingRadius) * 0.85f);
string before = "guest " + guestName + " biome=" + (Drift.Core.LifeBiome)guest.Biome + " area=" + guest.LandArea.ToString("F1");
player.MergeFrom(guest, 4f, 0.5f);
player.FinishUplift();
life.Tick(1f);
return before + " -> player area=" + player.LandArea.ToString("F1")
    + " cells=" + life.LandCells
    + " biomes=" + System.Convert.ToString(life.BiomesPresent, 2)
    + " dominant=" + life.DominantBiome
    + " temperate=" + life.CellsOfBiome(Drift.Core.LifeBiome.Temperate)
    + " tropical=" + life.CellsOfBiome(Drift.Core.LifeBiome.Tropical)
    + " nordic=" + life.CellsOfBiome(Drift.Core.LifeBiome.Nordic)
    + " savanna=" + life.CellsOfBiome(Drift.Core.LifeBiome.Savanna);
