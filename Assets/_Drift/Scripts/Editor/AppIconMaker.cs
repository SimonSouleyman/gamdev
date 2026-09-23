using System.IO;
using Drift.Islands;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace Drift.EditorTools
{
    // The app icon is a render of the start island on the sea (owner, 2026-09-23): a temporary camera looks at the
    // player island from above, the picture becomes the default icon and the Android adaptive icon.
    public static class AppIconMaker
    {
        const string Dir = "Assets/_Drift/Textures/AppIcon";
        const string IconPath = Dir + "/DriftIcon.png";
        const string ClearPath = Dir + "/DriftIconClear.png";

        [MenuItem("Drift/App-Symbol aus der Startinsel erzeugen")]
        public static string Make() => Make(1024, 48f, 0.52f, 210f);

        // fill = share of the icon width the island takes (Android's adaptive mask keeps the middle ~66 %).
        public static string Make(int size, float pitchDeg, float fill, float yawDeg)
        {
            Island player = null;
            foreach (var i in Island.All) if (i != null && i.useKeyboardInput) { player = i; break; }
            if (player == null) return "no player island";

            Directory.CreateDirectory(Dir);
            var go = new GameObject("AppIconCamera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            var main = Camera.main;
            if (main != null) cam.CopyFrom(main);
            cam.fieldOfView = 30f;
            float radius = Mathf.Max(3f, player.BoundingRadius);
            float dist = radius / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(0.1f, fill);
            Vector3 center = player.transform.position + Vector3.up * 0.5f;
            var rot = Quaternion.Euler(pitchDeg, yawDeg, 0f);
            go.transform.SetPositionAndRotation(center - rot * Vector3.forward * dist, rot);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = dist * 6f;

            var rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.aspect = 1f;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(go);
            File.WriteAllBytes(IconPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            var clear = new Texture2D(432, 432, TextureFormat.RGBA32, false);
            var px = new Color32[432 * 432];
            clear.SetPixels32(px);
            File.WriteAllBytes(ClearPath, clear.EncodeToPNG());
            Object.DestroyImmediate(clear);

            AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(ClearPath, ImportAssetOptions.ForceUpdate);
            foreach (var p in new[] { IconPath, ClearPath })
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath(p);
                imp.textureType = TextureImporterType.Default;
                imp.mipmapEnabled = false;
                imp.alphaIsTransparency = p == ClearPath;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            var empty = AssetDatabase.LoadAssetAtPath<Texture2D>(ClearPath);

            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            var android = NamedBuildTarget.Android;
            // Adaptive: the whole picture is the background layer (the launcher masks it), the foreground stays empty.
            var adaptive = PlayerSettings.GetPlatformIcons(android, AndroidPlatformIconKind.Adaptive);
            foreach (var i in adaptive) i.SetTextures(icon, empty);
            PlayerSettings.SetPlatformIcons(android, AndroidPlatformIconKind.Adaptive, adaptive);
            AssetDatabase.SaveAssets();
            return IconPath;
        }
    }
}
