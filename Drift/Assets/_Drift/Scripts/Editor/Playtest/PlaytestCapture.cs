using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Drift.EditorTools.Playtest
{
    // Game view resolution for the phone view, and image sheets built from the runner's screenshots: a filmstrip
    // per clip (evenly spaced frames) and a contact sheet over any list of images.
    public static class PlaytestCapture
    {
        [MenuItem("Drift/Playtest/Game view: phone portrait 1080x1920")]
        public static void PhonePortrait() => SetGameViewSize(1080, 1920, "Drift phone portrait");

        public static string SetGameViewSize(int w, int h, string label)
        {
            var asm = typeof(Editor).Assembly;
            var sizesType = asm.GetType("UnityEditor.GameViewSizes");
            var single = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = single.GetProperty("instance").GetValue(null);
            var groupType = sizesType.GetProperty("currentGroupType").GetValue(sizes);
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new[] { groupType });
            var gType = group.GetType();
            int total = (int)gType.GetMethod("GetTotalCount").Invoke(group, null);
            int index = -1;
            for (int i = 0; i < total; i++)
            {
                var s = gType.GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
                var st = s.GetType();
                if ((int)st.GetProperty("width").GetValue(s) == w && (int)st.GetProperty("height").GetValue(s) == h
                    && st.GetProperty("sizeType").GetValue(s).ToString() == "FixedResolution") { index = i; break; }
            }
            if (index < 0)
            {
                var sizeType = asm.GetType("UnityEditor.GameViewSize");
                var enumType = asm.GetType("UnityEditor.GameViewSizeType");
                var fixedRes = Enum.Parse(enumType, "FixedResolution");
                var size = Activator.CreateInstance(sizeType, fixedRes, w, h, label);
                gType.GetMethod("AddCustomSize").Invoke(group, new[] { size });
                index = (int)gType.GetMethod("GetTotalCount").Invoke(group, null) - 1;
            }
            var gvType = asm.GetType("UnityEditor.GameView");
            var gv = EditorWindow.GetWindow(gvType, false, null, false);
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var cb = gvType.GetMethod("SizeSelectionCallback", F);
            if (cb != null) cb.Invoke(gv, new object[] { index, null });
            else gvType.GetProperty("selectedSizeIndex", F)?.SetValue(gv, index);
            gv.Repaint();
            return $"game view size index {index} ({w}x{h})";
        }

        public static string Filmstrip(string clipDir, int frames = 8, int cols = 4, int cellW = 270)
        {
            var files = Directory.GetFiles(clipDir, "f*.png");
            Array.Sort(files, StringComparer.Ordinal);
            if (files.Length == 0) return "no frames in " + clipDir;
            var pick = new List<string>();
            for (int i = 0; i < frames; i++) pick.Add(files[Mathf.Min(files.Length - 1, Mathf.RoundToInt(i * (files.Length - 1) / (float)Mathf.Max(1, frames - 1)))]);
            string outPath = clipDir.TrimEnd('/', '\\') + ".jpg";
            return Sheet(pick, cols, cellW, outPath);
        }

        public static string Sheet(IList<string> images, int cols, int cellW, string outPath)
        {
            if (images.Count == 0) return "no images";
            var probe = new Texture2D(2, 2);
            probe.LoadImage(File.ReadAllBytes(images[0]));
            int cellH = Mathf.RoundToInt(cellW * probe.height / (float)probe.width);
            UnityEngine.Object.DestroyImmediate(probe);
            int rows = Mathf.CeilToInt(images.Count / (float)cols);
            const int Pad = 4;
            var sheet = new Texture2D(cols * cellW + (cols + 1) * Pad, rows * cellH + (rows + 1) * Pad, TextureFormat.RGB24, false);
            var fill = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(24, 24, 28, 255);
            sheet.SetPixels32(fill);
            var rt = RenderTexture.GetTemporary(cellW, cellH, 0, RenderTextureFormat.ARGB32);
            var cell = new Texture2D(cellW, cellH, TextureFormat.RGB24, false);
            var prev = RenderTexture.active;
            for (int i = 0; i < images.Count; i++)
            {
                var src = new Texture2D(2, 2);
                if (!src.LoadImage(File.ReadAllBytes(images[i]))) { UnityEngine.Object.DestroyImmediate(src); continue; }
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                cell.ReadPixels(new Rect(0, 0, cellW, cellH), 0, 0);
                cell.Apply();
                int c = i % cols, r = rows - 1 - i / cols;
                sheet.SetPixels(Pad + c * (cellW + Pad), Pad + r * (cellH + Pad), cellW, cellH, cell.GetPixels());
                UnityEngine.Object.DestroyImmediate(src);
            }
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            sheet.Apply();
            File.WriteAllBytes(outPath, sheet.EncodeToJPG(88));
            UnityEngine.Object.DestroyImmediate(sheet);
            UnityEngine.Object.DestroyImmediate(cell);
            return outPath;
        }
    }
}
