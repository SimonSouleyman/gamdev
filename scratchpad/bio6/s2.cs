// Simulates the merged player island and renders a top-down picture with a temporary DontSave camera.
float grow = 5400f;
string path = "C:/Users/Home/Documents/Unity/Drift/Temp/bio6_merged_later.png";

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
var herds = player.GetComponent<Drift.Life.IslandHerdSystem>();
if (grow > 0f) life.Simulate(grow, 20f);
Drift.Life.IslandLifeSystem.PushWind(true);

int size = 1000;
var rt = new UnityEngine.RenderTexture(size, size, 24, UnityEngine.RenderTextureFormat.ARGB32);
rt.antiAliasing = 2;
var camGo = new UnityEngine.GameObject("Bio6Cam");
camGo.hideFlags = UnityEngine.HideFlags.DontSave;
var cam = camGo.AddComponent<UnityEngine.Camera>();
cam.orthographic = true;
cam.orthographicSize = player.BoundingRadius * 1.15f;
cam.nearClipPlane = 0.05f;
cam.farClipPlane = 200f;
cam.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
cam.backgroundColor = new UnityEngine.Color(0.06f, 0.20f, 0.30f);
cam.targetTexture = rt;
var c = player.transform.position;
camGo.transform.position = new UnityEngine.Vector3(c.x, c.y + 60f, c.z);
camGo.transform.rotation = UnityEngine.Quaternion.Euler(90f, 0f, 0f);

bool ok = true;
string how = "Render";
try { cam.Render(); }
catch (System.Exception)
{
    how = "SubmitRenderRequest";
    var req = new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest();
    if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req))
    {
        req.destination = rt;
        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
    }
    else ok = false;
}

var prev = UnityEngine.RenderTexture.active;
UnityEngine.RenderTexture.active = rt;
var tex = new UnityEngine.Texture2D(size, size, UnityEngine.TextureFormat.RGB24, false);
tex.ReadPixels(new UnityEngine.Rect(0, 0, size, size), 0, 0);
tex.Apply();
UnityEngine.RenderTexture.active = prev;
System.IO.File.WriteAllBytes(path, UnityEngine.ImageConversion.EncodeToPNG(tex));

cam.targetTexture = null;
UnityEngine.Object.DestroyImmediate(camGo);
UnityEngine.Object.DestroyImmediate(tex);
UnityEngine.Object.DestroyImmediate(rt);

return path + " via " + how + " ok=" + ok
    + " area=" + player.LandArea.ToString("F1")
    + " lifeAge=" + life.LifeAge.ToString("F0")
    + " plants=" + life.LivePlantCount + " verts=" + life.MeshVertexCount
    + " herds=" + herds.HerdCount + " animals=" + herds.AnimalCount
    + " cells T/Tr/N/S=" + life.CellsOfBiome(Drift.Core.LifeBiome.Temperate)
    + "/" + life.CellsOfBiome(Drift.Core.LifeBiome.Tropical)
    + "/" + life.CellsOfBiome(Drift.Core.LifeBiome.Nordic)
    + "/" + life.CellsOfBiome(Drift.Core.LifeBiome.Savanna);
