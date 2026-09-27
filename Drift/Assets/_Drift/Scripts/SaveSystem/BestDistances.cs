using System;
using System.Globalization;
using System.IO;
using Drift.Core;
using UnityEngine;

namespace Drift.SaveSystem
{
    // The furthest distance per game mode (Abenteuer scores by the metres travelled along the ring). A tiny JSON file
    // next to the saves; tests point PathOverride at a temporary file so the owner's record is never touched.
    // The file name is the one of the old best times: those were kept under "seconds" and are simply not read any more.
    public static class BestDistances
    {
        public const string FileName = "drift_best_times.json";

        [Serializable]
        sealed class Data
        {
            public float[] metres = new float[0];
        }

        public struct Result
        {
            public float metres;
            public float previous;
            public float best;
            public bool isRecord;
            // A record, but there was no earlier distance to beat.
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
            return i >= 0 && i < d.metres.Length ? Mathf.Max(0f, d.metres[i]) : 0f;
        }

        public static bool Has(GameMode mode) => Get(mode) > 0f;

        // Records a finished run; only a longer distance replaces the best one.
        public static Result Submit(GameMode mode, float metres)
        {
            float prev = Get(mode);
            var r = new Result { metres = Mathf.Max(0f, metres), previous = prev, best = prev };
            if (!(metres > prev) || float.IsNaN(metres) || float.IsInfinity(metres)) return r;
            var d = Load();
            int i = (int)mode;
            if (d.metres.Length <= i) Array.Resize(ref d.metres, i + 1);
            d.metres[i] = metres;
            r.best = metres;
            r.isRecord = true;
            Write(d);
            Changed?.Invoke(mode);
            return r;
        }

        public static void Clear(GameMode mode)
        {
            var d = Load();
            int i = (int)mode;
            if (i >= d.metres.Length || d.metres[i] <= 0f) return;
            d.metres[i] = 0f;
            Write(d);
            Changed?.Invoke(mode);
        }

        // German digit grouping without relying on the device's culture data: "0 m", "987 m", "1.234 m", "12.345 m".
        static readonly NumberFormatInfo Grouped = new NumberFormatInfo { NumberGroupSeparator = ".", NumberDecimalSeparator = ",", NumberGroupSizes = new[] { 3 } };

        public static string Format(float metres) => Metres(metres).ToString("#,0", Grouped) + " m";

        // Whole metres shown for a distance (the HUD rebuilds its text only when this changes).
        public static int Metres(float metres) => float.IsNaN(metres) || metres <= 0f ? 0 : metres >= int.MaxValue ? int.MaxValue : (int)Math.Floor(metres);

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
                    if (d != null && d.metres != null) s_data = d;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("BestDistances: could not read " + FilePath + ": " + e.Message);
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
                Debug.LogWarning("BestDistances: could not write " + FilePath + ": " + e.Message);
            }
        }
    }
}
