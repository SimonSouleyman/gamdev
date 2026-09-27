using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Drift.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Drift.SaveSystem
{
    // One finished cozy run (a Pangäa): what the run journal ("Durchgangs-Tagebuch") shows.
    [Serializable]
    public class RunRecord
    {
        public string date;          // ISO local time the run was completed
        public int seed;             // world seed
        public float playSeconds;    // time played in the run (all sessions)
        public int islandsMerged;
        public float landArea;       // final area of the Pangäa
        public int speciesSeen;      // species in the discovery journal when the run ended
        public int photos;           // photos taken during the run
        public string image;         // file name of the space picture (inside ImageFolder), empty if none
        public string name;          // the Pangäa's name (IslandNames.ForSeed); empty in records written before names
    }

    [Serializable]
    class RunJournalFile
    {
        public int version = RunJournal.FileVersion;
        public List<RunRecord> records = new();
    }

    // Coordinator-owned API (2026-09-22). PangaeaFinale adds a record when a run completes; the run journal UI lists
    // them. The records live in run_journal.json and the pictures as PNGs in RunJournal/, both in persistentDataPath
    // (or RootOverride). Read lazily on first access; every change is written on a worker, in order.
    public static class RunJournal
    {
        public const int FileVersion = 1;
        public const string FileName = "run_journal.json";
        public const string ImageFolderName = "RunJournal";
        public const int PictureMaxSide = 512;

        static readonly List<RunRecord> _records = new();
        static bool _loaded;
        static string _root;
        static Task _write;

        public static IReadOnlyList<RunRecord> Records
        {
            get
            {
                EnsureLoaded();
                return _records;
            }
        }

        public static event Action Changed;
        // Raised by any menu button that wants the run journal opened, with the page (Gemütlich or Abenteuer) to show;
        // the run journal UI listens.
        public static event Action<GameMode> OpenRequested;

        public static void RequestOpen() => RequestOpen(GameMode.Cozy);
        public static void RequestOpen(GameMode page) => OpenRequested?.Invoke(page);

        // Tests point this at a temporary folder so they never touch the owner's journal; null = persistentDataPath.
        // Changing it drops the records in memory; they are read again from the new root on the next access.
        public static string RootOverride
        {
            get => _root;
            set
            {
                WaitForWrite();
                _root = value;
                _records.Clear();
                _loaded = false;
            }
        }

        public static string Root => !string.IsNullOrEmpty(_root) ? _root : Application.persistentDataPath;
        public static string FilePath => Path.Combine(Root, FileName);
        public static string ImageFolder => Path.Combine(Root, ImageFolderName);
        public static bool Loaded => _loaded;

        // Forgets the records in memory; the next access reads the file again.
        public static void Reload()
        {
            WaitForWrite();
            _records.Clear();
            _loaded = false;
        }

        // Blocks until the background writes have finished. Cheap when none is running.
        public static void WaitForWrite()
        {
            var t = _write;
            if (t == null) return;
            try { t.Wait(); } catch (Exception) { }
            if (_write == t) _write = null;
        }

        // What a record is called on screen: its generated name, or the old "Pangäa #n" for records from before
        // names existed (and for the finale, which knows the number only after the record was added).
        public static string TitleOf(RunRecord record, int number) =>
            record != null && !string.IsNullOrEmpty(record.name) ? record.name : "Pangäa #" + Mathf.Max(1, number);

        // Chronological number of a record ("Pangäa #n"), 1 for the first run ever; 0 when it is not in the journal.
        public static int NumberOf(RunRecord record)
        {
            EnsureLoaded();
            int i = _records.IndexOf(record);
            return i >= 0 ? i + 1 : 0;
        }

        // Stores the record and, when given, the picture (the caller keeps ownership of the texture).
        public static void Add(RunRecord record, Texture2D picture)
        {
            if (record == null) return;
            EnsureLoaded();
            if (string.IsNullOrEmpty(record.date)) record.date = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);

            Color32[] pixels = null;
            int width = 0, height = 0;
            if (picture != null && ReadPixels(picture, out pixels, out width, out height))
                record.image = UniqueImageName(record.date);
            else if (picture != null) record.image = "";

            _records.Add(record);
            string json = Serialize();
            string file = FilePath, folder = ImageFolder, image = pixels != null ? record.image : null;
            Enqueue(() =>
            {
                if (image != null) WritePicture(pixels, width, height, Path.Combine(folder, image));
                WriteText(file, json);
            });
            Changed?.Invoke();
        }

        // The picture of a record, or null. The caller destroys the returned texture.
        public static Texture2D LoadPicture(RunRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.image)) return null;
            WaitForWrite();
            string path = Path.Combine(ImageFolder, Path.GetFileName(record.image));
            if (!File.Exists(path)) return null;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, name = record.image };
                if (tex.LoadImage(File.ReadAllBytes(path), true)) return tex;
                Dispose(tex);
            }
            catch (Exception e)
            {
                Debug.LogWarning("RunJournal: picture load failed: " + e.Message);
            }
            return null;
        }

        // Empties the journal: the records file and the pictures are deleted.
        public static void Clear()
        {
            WaitForWrite();
            _records.Clear();
            _loaded = true;
            string file = FilePath, folder = ImageFolder;
            Enqueue(() =>
            {
                if (File.Exists(file)) File.Delete(file);
                if (Directory.Exists(folder))
                    foreach (var png in Directory.GetFiles(folder, "*.png")) File.Delete(png);
            });
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- storage

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnPlaySessionStart()
        {
            // Without a domain reload the statics survive into the next Play session; read the file afresh.
            WaitForWrite();
            _records.Clear();
            _loaded = false;
            Application.quitting -= WaitForWrite;
            Application.quitting += WaitForWrite;
        }

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _records.Clear();
            string path = FilePath;
            if (!File.Exists(path)) return;
            try
            {
                var data = JsonUtility.FromJson<RunJournalFile>(File.ReadAllText(path));
                if (data == null || data.records == null) throw new InvalidDataException("no records");
                foreach (var r in data.records)
                    if (r != null) _records.Add(r);
            }
            catch (Exception e)
            {
                // Kept aside instead of overwritten by the next Add: a later version may still read it.
                Debug.LogWarning("RunJournal: unreadable journal, starting a new one: " + e.Message);
                _records.Clear();
                try { File.Copy(path, path + ".corrupt", true); } catch (Exception) { }
            }
        }

        static string Serialize()
        {
            var data = new RunJournalFile();
            data.records.AddRange(_records);
            return JsonUtility.ToJson(data, true);
        }

        static void Enqueue(Action work)
        {
            var previous = _write;
            _write = Task.Run(() =>
            {
                if (previous != null) try { previous.Wait(); } catch (Exception) { }
                try { work(); }
                catch (Exception e) { Debug.LogWarning("RunJournal: write failed: " + e.Message); }
            });
        }

        static string UniqueImageName(string date)
        {
            string stamp = DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t.ToString("yyyyMMdd_HHmmss") : DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string name = "pangaea_" + stamp + ".png";
            for (int n = 2; Taken(name); n++) name = "pangaea_" + stamp + "_" + n + ".png";
            return name;
        }

        static bool Taken(string name)
        {
            foreach (var r in _records)
                if (r.image == name) return true;
            return File.Exists(Path.Combine(ImageFolder, name));
        }

        // Main thread: only the raw pixels are copied here; scaling and PNG encoding run on the worker. A texture
        // that is not CPU-readable goes through a temporary render texture first.
        static bool ReadPixels(Texture2D picture, out Color32[] pixels, out int width, out int height)
        {
            pixels = null;
            width = height = 0;
            try
            {
                if (picture.isReadable)
                {
                    pixels = picture.GetPixels32();
                    width = picture.width;
                    height = picture.height;
                    return pixels != null && pixels.Length == width * height && width > 0;
                }
                float scale = Mathf.Min(1f, PictureMaxSide / (float)Mathf.Max(picture.width, picture.height));
                width = Mathf.Max(1, Mathf.RoundToInt(picture.width * scale));
                height = Mathf.Max(1, Mathf.RoundToInt(picture.height * scale));
                var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var prev = RenderTexture.active;
                Graphics.Blit(picture, rt);
                RenderTexture.active = rt;
                var copy = new Texture2D(width, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                copy.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                copy.Apply(false);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                pixels = copy.GetPixels32();
                Dispose(copy);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("RunJournal: picture not stored: " + e.Message);
                pixels = null;
                return false;
            }
        }

        // Box filter down to PictureMaxSide on the longer side. Plain arrays only, so it may run on a worker.
        public static Color32[] Downscale(Color32[] src, int w, int h, int maxSide, out int dw, out int dh)
        {
            if (w <= maxSide && h <= maxSide)
            {
                dw = w;
                dh = h;
                return src;
            }
            float scale = maxSide / (float)Mathf.Max(w, h);
            dw = Mathf.Max(1, Mathf.RoundToInt(w * scale));
            dh = Mathf.Max(1, Mathf.RoundToInt(h * scale));
            var dst = new Color32[dw * dh];
            for (int y = 0; y < dh; y++)
            {
                int y0 = y * h / dh, y1 = Mathf.Max(y0 + 1, (y + 1) * h / dh);
                for (int x = 0; x < dw; x++)
                {
                    int x0 = x * w / dw, x1 = Mathf.Max(x0 + 1, (x + 1) * w / dw);
                    int r = 0, g = 0, b = 0, a = 0;
                    for (int sy = y0; sy < y1; sy++)
                    {
                        int row = sy * w;
                        for (int sx = x0; sx < x1; sx++)
                        {
                            var c = src[row + sx];
                            r += c.r; g += c.g; b += c.b; a += c.a;
                        }
                    }
                    int n = (y1 - y0) * (x1 - x0);
                    dst[y * dw + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
                }
            }
            return dst;
        }

        static void WritePicture(Color32[] pixels, int w, int h, string path)
        {
            var small = Downscale(pixels, w, h, PictureMaxSide, out int dw, out int dh);
            var rgb = new byte[dw * dh * 3];
            for (int i = 0, k = 0; i < small.Length; i++)
            {
                rgb[k++] = small[i].r;
                rgb[k++] = small[i].g;
                rgb[k++] = small[i].b;
            }
            byte[] png = ImageConversion.EncodeArrayToPNG(rgb, GraphicsFormat.R8G8B8_SRGB, (uint)dw, (uint)dh);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, png);
        }

        static void WriteText(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (!File.Exists(path)) File.Move(tmp, path);
            else
            {
                try { File.Replace(tmp, path, null); }
                catch (IOException)
                {
                    File.Copy(tmp, path, true);
                    File.Delete(tmp);
                }
            }
        }

        static void Dispose(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
