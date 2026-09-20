// False-colour map of the per-cell biome (top) and of the ground tint the terrain actually shows (bottom).
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
if (player == null) return "no player island";
var life = player.GetComponent<Drift.Life.IslandLifeSystem>();
var b = player.LocalBounds;

int w = 520, h = 520;
var tex = new UnityEngine.Texture2D(w, h * 2, UnityEngine.TextureFormat.RGB24, false);
var key = new UnityEngine.Color[]
{
    new UnityEngine.Color(0.45f, 0.78f, 0.35f),  // Temperate
    new UnityEngine.Color(0.10f, 0.62f, 0.45f),  // Tropical
    new UnityEngine.Color(0.85f, 0.92f, 1.00f),  // Nordic
    new UnityEngine.Color(0.92f, 0.66f, 0.20f)   // Savanna
};
var sea = new UnityEngine.Color(0.06f, 0.16f, 0.30f);
var grass = new UnityEngine.Color(0.36f, 0.62f, 0.28f).linear;
for (int y = 0; y < h; y++)
    for (int x = 0; x < w; x++)
    {
        var p = new UnityEngine.Vector2(b.xMin + b.width * (x + 0.5f) / w, b.yMin + b.height * (y + 0.5f) / h);
        bool land = player.SampleHeight(p) > 0.12f;
        tex.SetPixel(x, y + h, land ? key[life.BiomeAt(p)] : sea);
        if (!land) { tex.SetPixel(x, y, sea); continue; }
        var t = life.GroundTintAt(p);
        var shown = new UnityEngine.Color(t.r * grass.r, t.g * grass.g, t.b * grass.b).gamma;
        tex.SetPixel(x, y, shown);
    }
tex.Apply();
string path = "C:/Users/Home/Documents/Unity/Drift/Temp/bio6_biomemap.png";
System.IO.File.WriteAllBytes(path, UnityEngine.ImageConversion.EncodeToPNG(tex));
UnityEngine.Object.DestroyImmediate(tex);
return path + " bounds=" + b + " cells T/Tr/N/S="
    + life.CellsOfBiome(Drift.Core.LifeBiome.Temperate) + "/"
    + life.CellsOfBiome(Drift.Core.LifeBiome.Tropical) + "/"
    + life.CellsOfBiome(Drift.Core.LifeBiome.Nordic) + "/"
    + life.CellsOfBiome(Drift.Core.LifeBiome.Savanna)
    + " dominant=" + life.DominantBiome + " name=" + life.BiomeName;
