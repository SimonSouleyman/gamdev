using System;
using System.Collections.Generic;
using System.IO;
using Drift.Core;
using UnityEngine;

namespace Drift.SaveSystem
{
    // "Nur Arten, Pflanzen und Fotoaufgaben" or "Alles (auch Fotoalbum und Chronik der Durchgänge)".
    public enum JournalResetScope { SpeciesAndTasks, Everything }

    // What a reset works on. The live objects are optional (null = only the files); a null directory touches no file.
    public sealed class JournalResetTargets
    {
        // persistentDataPath, or a test folder.
        public string directory;
        // The photo album's folder; null = directory/Photos.
        public string photoDirectory;
        public DiscoveryJournal journal;
        public JournalBook book;
        public LifeBook lifeBook;
        public PhotoTaskBook tasks;
        public string lifeBookKey = LifeBook.PrefKey;
        // The cozy run save; null = directory/drift_save.json.
        public string saveFile;
        public SaveManager saveManager;
        // Delete the run in progress instead of only emptying its journal layer (the one-time update reset).
        public bool dropRunSave;
        // RunJournal is static and has its own root (RunJournal.RootOverride in tests); false leaves it alone.
        public bool runJournal = true;
    }

    // Empties the journal: the album (the cross-run JournalBook file drift_journal.json, the LifeBook in PlayerPrefs, the
    // album layer of the live DiscoveryJournal), the photo tasks (drift_phototasks.json + PhotoTasks/), the journal layer
    // of the run in progress (in memory and in the save file) and, for Everything, the photo album (Photos/drift_*) and
    // the run chronicle (run_journal.json + RunJournal/*.png). The best distances and the settings stay.
    // Also the one-time "everything" reset of v0.6.5 (AutoKey), which runs before the first scene loads, so neither the
    // species pool nor the journal UI ever read the old album.
    public static class JournalReset
    {
        public const string PhotoFolderName = "Photos";
        public const string PhotoPrefix = "drift_";
        public const string AutoKey = "drift_reset_v065";

        public const string SpeciesLabel = "Nur Arten, Pflanzen und Fotoaufgaben";
        public const string EverythingLabel = "Alles";
        public const string EverythingNote = "(auch Fotoalbum und Chronik der Durchgänge)";

        public static bool AutoResetDue => PlayerPrefs.GetInt(AutoKey, 0) == 0;

        // Returns what it cleared ("drift_journal.json", "PlayerPrefs drift_lifebook", ...) for logs and tests.
        public static List<string> Run(JournalResetScope scope, JournalResetTargets t)
        {
            var cleared = new List<string>();
            if (t == null) return cleared;
            bool files = !string.IsNullOrEmpty(t.directory);

            // The album layer lives in the book once one is attached: detach, empty both, attach the empty book again.
            if (t.journal != null)
            {
                var attached = t.journal.Book;
                t.journal.AttachBook(null);
                t.journal.Reset();
                if (attached != null)
                {
                    attached.Clear();
                    t.journal.AttachBook(attached);
                }
                cleared.Add("journal (memory)");
            }
            if (t.book != null) t.book.Clear();
            if (files && DeleteFile(Path.Combine(t.directory, JournalBook.FileName))) cleared.Add(JournalBook.FileName);

            if (t.lifeBook != null) t.lifeBook.Clear();
            if (!string.IsNullOrEmpty(t.lifeBookKey) && PlayerPrefs.HasKey(t.lifeBookKey))
            {
                PlayerPrefs.DeleteKey(t.lifeBookKey);
                cleared.Add("PlayerPrefs " + t.lifeBookKey);
            }

            if (t.tasks != null) t.tasks.Clear();
            if (files)
            {
                if (DeleteFile(Path.Combine(t.directory, PhotoTaskBook.FileName))) cleared.Add(PhotoTaskBook.FileName);
                int n = DeleteFiles(Path.Combine(t.directory, PhotoTaskBook.ThumbFolder), "*");
                if (n > 0) cleared.Add(PhotoTaskBook.ThumbFolder + "/ (" + n + ")");
            }

            string save = !string.IsNullOrEmpty(t.saveFile) ? t.saveFile
                : files ? Path.Combine(t.directory, GameModes.SaveFile(GameMode.Cozy)) : null;
            if (t.saveManager != null) t.saveManager.WaitForWrite();
            if (t.dropRunSave)
            {
                if (t.saveManager != null) t.saveManager.DeleteSave();
                if (save != null && DeleteFile(save)) cleared.Add(Path.GetFileName(save));
                if (save != null) DeleteFile(save + ".tmp");
            }
            else if (save != null && ClearSaveJournal(save)) cleared.Add(Path.GetFileName(save) + " (journal)");

            if (scope == JournalResetScope.Everything)
            {
                if (files)
                {
                    string photos = !string.IsNullOrEmpty(t.photoDirectory) ? t.photoDirectory : Path.Combine(t.directory, PhotoFolderName);
                    int n = DeleteFiles(photos, PhotoPrefix + "*");
                    if (n > 0) cleared.Add(PhotoFolderName + "/ (" + n + ")");
                }
                if (t.runJournal)
                {
                    bool had = RunJournal.Records.Count > 0 || File.Exists(RunJournal.FilePath);
                    RunJournal.Clear();
                    RunJournal.WaitForWrite();
                    if (had) cleared.Add(RunJournal.FileName + " + " + RunJournal.ImageFolderName + "/");
                }
            }
            PlayerPrefs.Save();
            return cleared;
        }

        // Empties the journal block of a run save and keeps everything else of it; false without a readable file.
        public static bool ClearSaveJournal(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            try
            {
                var data = JsonUtility.FromJson<SaveGame>(File.ReadAllText(path));
                if (data == null) return false;
                data.journal = new JournalSaveData();
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(data));
                File.Copy(tmp, path, true);
                File.Delete(tmp);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("JournalReset: could not clear the journal of " + path + ": " + e.Message);
                return false;
            }
        }

        // The update to v0.6.5 starts every journal afresh, once per install.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoResetOnFirstStart()
        {
            if (!AutoResetDue) return;
            var cleared = RunAuto(Application.persistentDataPath);
            Debug.Log("Tagebuch einmalig zurückgesetzt (v0.6.5): " + (cleared.Count > 0 ? string.Join(", ", cleared) : "nichts vorhanden"));
        }

        // The one-time reset itself (the flag is set afterwards); public so a test can run it on a temporary folder.
        public static List<string> RunAuto(string directory, string lifeBookKey = LifeBook.PrefKey, string flagKey = AutoKey, bool runJournal = true)
        {
            var cleared = Run(JournalResetScope.Everything, new JournalResetTargets
            {
                directory = directory, lifeBookKey = lifeBookKey, dropRunSave = true, runJournal = runJournal,
            });
            PlayerPrefs.SetInt(flagKey, 1);
            PlayerPrefs.Save();
            return cleared;
        }

        static bool DeleteFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("JournalReset: could not delete " + path + ": " + e.Message);
                return false;
            }
        }

        static int DeleteFiles(string folder, string pattern)
        {
            int n = 0;
            try
            {
                if (!Directory.Exists(folder)) return 0;
                foreach (var f in Directory.GetFiles(folder, pattern))
                {
                    File.Delete(f);
                    n++;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("JournalReset: could not empty " + folder + ": " + e.Message);
            }
            return n;
        }
    }
}
