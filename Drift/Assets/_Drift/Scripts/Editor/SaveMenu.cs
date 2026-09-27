using System.IO;
using UnityEditor;
using UnityEngine;

namespace Drift.EditorTools
{
    public static class SaveMenu
    {
        static string SavePath => Path.Combine(Application.persistentDataPath, "drift_save.json");

        [MenuItem("Drift/Save/Delete Save File")]
        public static void DeleteSave()
        {
            if (File.Exists(SavePath))
            {
                File.Delete(SavePath);
                Debug.Log($"[Drift] Deleted save: {SavePath}");
            }
            else
            {
                Debug.Log($"[Drift] No save file at {SavePath}");
            }
        }

        [MenuItem("Drift/Save/Show Save Folder")]
        public static void ShowFolder() => EditorUtility.RevealInFinder(Application.persistentDataPath);
    }
}
