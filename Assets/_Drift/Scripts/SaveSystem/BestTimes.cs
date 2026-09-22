using System;
using System.IO;
using Drift.Core;
using UnityEngine;

namespace Drift.SaveSystem
{
    // The longest time survived per game mode (Abenteuer scores by it). A tiny JSON file next to the saves;
    // tests point PathOverride at a temporary file so the owner's record is never touched.
    public static class BestTimes
    {
        public const string FileName = "drift_best_times.json";

        [Serializable]
        sealed class Data
        {
            public float[] seconds = new float[0];
        }

        public struct Result
        {
            public float seconds;
            public float previous;
            public float best;
            public bool isRecord;
            // A record, but there was no earlier time to beat.
            public bool IsFirst => isRecord && previous <= 0f;
            public bool BeatPrevious => isRecord && previous > 0f;
        }

        static string s_pathOverride;
        static Data s_data;

        public static event Action<GameMode> Changed;

        // null = the real file in persistentDataPath.
        public static string PathOverride
        {
            get => s_pathOverride;
            set
            {
                s_pathOverride = value;
                s_data = null;
            }
        }

        public static string FilePath => !string.IsNullOrEmpty(s_pathOverride) ? s_pathOverride : Path.Combine(Application.persistentDataPath, FileName);

        public static float Get(GameMode mode)
        {
            var d = Load();
            int i = (int)mode;
            return i >= 0 && i < d.seconds.Length ? Mathf.Max(0f, d.seconds[i]) : 0f;
        }

        public static bool Has(GameMode mode) => Get(mode) > 0f;

        // Records a finished run; only a longer time replaces the best one.
        public static Result Submit(GameMode mode, float seconds)
        {
            float prev = Get(mode);
            var r = new Result { seconds = Mathf.Max(0f, seconds), previous = prev, best = prev };
            if (!(seconds > prev) || float.IsNaN(seconds) || float.IsInfinity(seconds)) return r;
            var d = Load();
            int i = (int)mode;
            if (d.seconds.Length <= i) Array.Resize(ref d.seconds, i + 1);
            d.seconds[i] = seconds;
            r.best = seconds;
            r.isRecord = true;
            Write(d);
            Changed?.Invoke(mode);
            return r;
        }

        public static void Clear(GameMode mode)
        {
            var d = Load();
            int i = (int)mode;
            if (i >= d.seconds.Length || d.seconds[i] <= 0f) return;
            d.seconds[i] = 0f;
            Write(d);
            Changed?.Invoke(mode);
        }

        // "3:42", "12:05", "1:02:09".
        public static string Format(float seconds)
        {
            if (float.IsNaN(seconds) || seconds < 0f) seconds = 0f;
            long s = (long)Math.Floor(seconds);
            long h = s / 3600, m = s / 60 % 60, sec = s % 60;
            return h > 0 ? $"{h}:{m:00}:{sec:00}" : $"{m}:{sec:00}";
        }

        static Data Load()
        {
            if (s_data != null) return s_data;
            s_data = new Data();
            try
            {
                string path = FilePath;
                if (File.Exists(path))
                {
                    var d = JsonUtility.FromJson<Data>(File.ReadAllText(path));
                    if (d != null && d.seconds != null) s_data = d;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("BestTimes: could not read " + FilePath + ": " + e.Message);
            }
            return s_data;
        }

        static void Write(Data d)
        {
            try
            {
                string path = FilePath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(d));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("BestTimes: could not write " + FilePath + ": " + e.Message);
            }
        }
    }
}
