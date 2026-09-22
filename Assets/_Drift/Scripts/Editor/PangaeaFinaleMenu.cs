using System.IO;
using Drift.Bridge;
using UnityEditor;
using UnityEngine;

namespace Drift.EditorTools
{
    // Edit-mode check of the Pangäa finale: renders the space view of the open scene into Temp/finale_space.png.
    public static class PangaeaFinaleMenu
    {
        public const string DefaultPath = "Temp/finale_space.png";

        [MenuItem("Drift/Finale/Weltkugel-Vorschau speichern")]
        static void SaveSpaceView() => Render(DefaultPath);

        public static string Render(string path, float yawDeg = float.NaN, int size = 0)
        {
            var finale = Object.FindAnyObjectByType<PangaeaFinale>();
            GameObject temp = null;
            if (finale == null)
            {
                temp = new GameObject("PangaeaFinalePreview") { hideFlags = HideFlags.HideAndDontSave };
                finale = temp.AddComponent<PangaeaFinale>();
            }
            try
            {
                string full = Path.GetFullPath(path);
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var tex = finale.PreviewSpaceView(full, yawDeg, size);
                if (tex == null) return "no picture (main camera / player island missing)";
                string info = $"{full} {tex.width}x{tex.height}";
                if (!finale.editorPreview) Object.DestroyImmediate(tex);
                return info;
            }
            finally
            {
                if (temp != null) Object.DestroyImmediate(temp);
            }
        }
    }
}
