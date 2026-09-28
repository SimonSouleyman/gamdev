using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Drift.Core;
using UnityEngine;

namespace Drift.SaveSystem
{
    // One finished adventure run: a line of the "Abenteuer" page of the run journal.
    [Serializable]
    public class AdventureRun
    {
        public string date;      // ISO local time the run ended
        public float metres;
        public int level;        // "Stufe" reached, 0 when unknown
        public int hits, dodges, flotsam;
        public bool record;      // the run set a new best distance
    }

    [Serializable]
    class AdventureRunLogFile
    {
        public int version = AdventureRunLog.FileVersion;
        public List<AdventureRun> runs = new();
    }

    // The adventure runs, oldest first, capped at MaxRuns (the oldest drop out). A small JSON file next to the saves,
    // written at once like BestDistances; tests point PathOverride at a temporary file. The cozy run journal
    // (RunJournal) is a separate file and never touched here.
    public static class AdventureRunLog
    {
        public const int FileVersion = 1;
        public const string FileName = "adventure_runs.json";
        public const int MaxRuns = 200;

        static readonly List<AdventureRun> s_runs = new();
        static bool s_loaded;
        static string s_pathOverride;

        public static event Action Changed;

        // null = the real file in persistentDataPath. Changing it drops the runs in memory.
        public static string PathOverride
        {
            get => s_pathOverride;
            set
            {
                s_pathOverride = value;
                Reload();
            }
        }

        public static string FilePath => !string.IsNullOrEmpty(s_pathOverride) ? s_pathOverride : Path.Combine(Application.persistentDataPath, FileName);

        public static IReadOnlyList<AdventureRun> Runs
        {
            get
            {
                EnsureLoaded();
                return s_runs;
            }
        }

        public static void Reload()
        {
            s_runs.Clear();
            s_loaded = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnPlaySessionStart() => Reload();

        public static AdventureRun Make(float metres, int level, int hits, int dodges, int flotsam, bool record, DateTime when) => new AdventureRun
        {
            date = when.ToString("s", CultureInfo.InvariantCulture),
            metres = float.IsNaN(metres) || float.IsInfinity(metres) ? 0f : Mathf.Max(0f, metres),
            level = Mathf.Max(0, level),
            hits = Mathf.Max(0, hits),
            dodges = Mathf.Max(0, dodges),
            flotsam = Mathf.Max(0, flotsam),
            record = record,
        };

        public static void Add(AdventureRun run)
        {
            if (run == null) return;
            EnsureLoaded();
            if (string.IsNullOrEmpty(run.date)) run.date = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);
            s_runs.Add(run);
            Cap(s_runs, MaxRuns);
            Write(Serialize(s_runs));
            Changed?.Invoke();
        }

        public static void Clear()
        {
            s_runs.Clear();
            s_loaded = true;
            try
            {
                string path = FilePath;
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("AdventureRunLog: could not delete " + FilePath + ": " + e.Message);
            }
            Changed?.Invoke();
        }

        // "Abenteuer-Rekord zurücksetzen": the adventure best distance and the adventure runs; Gemütlich keeps its own.
        public static void ResetAdventure()
        {
            BestDistances.Clear(GameMode.Adventure);
            Clear();
        }

        // Drops the oldest entries beyond max.
        public static void Cap(List<AdventureRun> runs, int max)
        {
            if (runs == null) return;
            int extra = runs.Count - Mathf.Max(0, max);
            if (extra > 0) runs.RemoveRange(0, extra);
        }

        // A missing, empty or unreadable text reads as no runs.
        public static List<AdventureRun> Parse(string json)
        {
            var list = new List<AdventureRun>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            try
            {
                var data = JsonUtility.FromJson<AdventureRunLogFile>(json);
                if (data?.runs != null)
                    foreach (var r in data.runs)
                        if (r != null) list.Add(r);
            }
            catch (Exception e)
            {
                Debug.LogWarning("AdventureRunLog: unreadable log, starting a new one: " + e.Message);
                list.Clear();
            }
            return list;
        }

        public static string Serialize(List<AdventureRun> runs)
        {
            var data = new AdventureRunLogFile();
            if (runs != null) data.runs.AddRange(runs);
            return JsonUtility.ToJson(data);
        }

        static void EnsureLoaded()
        {
            if (s_loaded) return;
            s_loaded = true;
            s_runs.Clear();
            try
            {
                string path = FilePath;
                if (File.Exists(path)) s_runs.AddRange(Parse(File.ReadAllText(path)));
            }
            catch (Exception e)
            {
                Debug.LogWarning("AdventureRunLog: could not read " + FilePath + ": " + e.Message);
            }
            Cap(s_runs, MaxRuns);
        }

        static void Write(string json)
        {
            try
            {
                string path = FilePath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("AdventureRunLog: could not write " + FilePath + ": " + e.Message);
            }
        }
    }
}
