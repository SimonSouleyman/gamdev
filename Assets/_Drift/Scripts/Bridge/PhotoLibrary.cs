using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Drift.Bridge
{
    public struct PhotoEntry
    {
        public string path, thumbPath, name;
        public DateTime taken;
    }

    // The photo folder as plain files: drift_yyyyMMdd_HHmmss.png plus a small drift_..._thumb.jpg next to it.
    // Everything takes the directory as a parameter so tests run against a temp folder.
    public static class PhotoLibrary
    {
        public const string Prefix = Drift.SaveSystem.JournalReset.PhotoPrefix;
        public const string Extension = ".png";
        public const string ThumbSuffix = "_thumb.jpg";
        public const string StampFormat = "yyyyMMdd_HHmmss";
        public const int ThumbSize = 256;

        static readonly string[] Months =
        {
            "Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember",
        };

        public static string DefaultDirectory => Path.Combine(Application.persistentDataPath, Drift.SaveSystem.JournalReset.PhotoFolderName);

        public static string FileNameFor(DateTime time) => Prefix + time.ToString(StampFormat, CultureInfo.InvariantCulture) + Extension;

        public static string ThumbPathOf(string photoPath) =>
            Path.Combine(Path.GetDirectoryName(photoPath) ?? "", Path.GetFileNameWithoutExtension(photoPath) + ThumbSuffix);

        // Two photos within the same second get _2, _3 ... so nothing is ever overwritten.
        public static string UniquePath(string directory, DateTime time)
        {
            string path = Path.Combine(directory, FileNameFor(time));
            for (int i = 2; File.Exists(path) && i < 1000; i++)
                path = Path.Combine(directory, Prefix + time.ToString(StampFormat, CultureInfo.InvariantCulture) + "_" + i + Extension);
            return path;
        }

        public static bool TryParseDate(string fileName, out DateTime taken)
        {
            taken = default;
            if (string.IsNullOrEmpty(fileName)) return false;
            string name = Path.GetFileNameWithoutExtension(fileName);
            if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || name.Length < Prefix.Length + StampFormat.Length) return false;
            return DateTime.TryParseExact(name.Substring(Prefix.Length, StampFormat.Length), StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out taken);
        }

        // Newest first. Files whose name carries no date fall back to their write time.
        public static void List(string directory, List<PhotoEntry> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;
            string[] files;
            try { files = Directory.GetFiles(directory, Prefix + "*" + Extension); }
            catch (Exception) { return; }
            foreach (var f in files)
            {
                if (!TryParseDate(f, out var taken))
                {
                    try { taken = File.GetLastWriteTime(f); }
                    catch (Exception) { taken = DateTime.MinValue; }
                }
                into.Add(new PhotoEntry { path = f, thumbPath = ThumbPathOf(f), name = Path.GetFileName(f), taken = taken });
            }
            into.Sort(Newest);
        }

        static int Newest(PhotoEntry a, PhotoEntry b)
        {
            int c = b.taken.CompareTo(a.taken);
            return c != 0 ? c : string.CompareOrdinal(b.name, a.name);
        }

        public static int Count(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return 0;
            try { return Directory.GetFiles(directory, Prefix + "*" + Extension).Length; }
            catch (Exception) { return 0; }
        }

        public static bool Delete(PhotoEntry entry)
        {
            if (string.IsNullOrEmpty(entry.path)) return false;
            try
            {
                if (File.Exists(entry.path)) File.Delete(entry.path);
                if (!string.IsNullOrEmpty(entry.thumbPath) && File.Exists(entry.thumbPath)) File.Delete(entry.thumbPath);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("PhotoLibrary: delete failed: " + e.Message);
                return false;
            }
        }

        public static int PageCount(int photos, int perPage) => Mathf.Max(1, (photos + perPage - 1) / Mathf.Max(1, perPage));

        public static string CountLabel(int photos) => photos <= 0 ? "Noch keine Fotos" : photos == 1 ? "1 Foto" : photos + " Fotos";

        public static string FormatDate(DateTime t) =>
            t == DateTime.MinValue ? "" : t.Day + ". " + Months[t.Month - 1] + " " + t.Year + "  ·  " + t.ToString("HH:mm", CultureInfo.InvariantCulture) + " Uhr";

        // Size of the thumbnail for a picture: the longer side becomes maxSide, never upscaled.
        public static Vector2Int ThumbDimensions(int width, int height, int maxSide)
        {
            if (width <= 0 || height <= 0) return new Vector2Int(1, 1);
            float k = Mathf.Min(1f, maxSide / (float)Mathf.Max(width, height));
            return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(width * k)), Mathf.Max(1, Mathf.RoundToInt(height * k)));
        }

        // Centre crop (as a RawImage uvRect) that fills a square cell with a picture of any aspect.
        public static Rect SquareCrop(int width, int height)
        {
            if (width <= 0 || height <= 0) return new Rect(0f, 0f, 1f, 1f);
            if (width >= height)
            {
                float w = height / (float)width;
                return new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }
            float h = width / (float)height;
            return new Rect(0f, (1f - h) * 0.5f, 1f, h);
        }

        public static Texture2D MakeThumbnail(Texture source, int maxSide)
        {
            var size = ThumbDimensions(source.width, source.height, maxSide);
            var rt = RenderTexture.GetTemporary(size.x, size.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var thumb = new Texture2D(size.x, size.y, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            thumb.ReadPixels(new Rect(0f, 0f, size.x, size.y), 0, 0);
            thumb.Apply(false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return thumb;
        }

        // Writes the PNG and its thumbnail; the caller keeps ownership of the screenshot texture.
        public static PhotoEntry Save(Texture2D screenshot, string directory, DateTime time)
        {
            Directory.CreateDirectory(directory);
            string path = UniquePath(directory, time);
            File.WriteAllBytes(path, screenshot.EncodeToPNG());
            var entry = new PhotoEntry { path = path, thumbPath = ThumbPathOf(path), name = Path.GetFileName(path), taken = time };
            // The thumbnail is made from the file, not from the screenshot texture: in a linear-colour project the
            // captured texture's sRGB flag does not match its bytes and a blit from it comes out too bright.
            var written = LoadTexture(path);
            if (written != null)
            {
                WriteThumbnail(written, entry.thumbPath);
                Dispose(written);
            }
            return entry;
        }

        public static void WriteThumbnail(Texture source, string thumbPath)
        {
            Texture2D thumb = null;
            try
            {
                thumb = MakeThumbnail(source, ThumbSize);
                File.WriteAllBytes(thumbPath, thumb.EncodeToJPG(85));
            }
            catch (Exception e)
            {
                Debug.LogWarning("PhotoLibrary: thumbnail failed: " + e.Message);
            }
            finally
            {
                Dispose(thumb);
            }
        }

        // The small picture of an entry. Photos from before the album existed have none yet: it is made from
        // the full picture once and stored next to it.
        public static Texture2D LoadThumbnail(PhotoEntry entry, bool storeMissing = true)
        {
            var thumb = LoadTexture(entry.thumbPath);
            if (thumb != null) return thumb;
            var full = LoadTexture(entry.path);
            if (full == null) return null;
            try
            {
                thumb = MakeThumbnail(full, ThumbSize);
                if (storeMissing && !string.IsNullOrEmpty(entry.thumbPath)) File.WriteAllBytes(entry.thumbPath, thumb.EncodeToJPG(85));
            }
            catch (Exception e)
            {
                Debug.LogWarning("PhotoLibrary: thumbnail failed: " + e.Message);
            }
            finally
            {
                Dispose(full);
            }
            return thumb;
        }

        public static Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                if (tex.LoadImage(File.ReadAllBytes(path), true)) return tex;
                Dispose(tex);
            }
            catch (Exception e)
            {
                Debug.LogWarning("PhotoLibrary: load failed: " + e.Message);
            }
            return null;
        }

        public static void Dispose(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
