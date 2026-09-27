using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Drift.Core;
using Drift.Life;
using UnityEngine;

namespace Drift.SaveSystem
{
    // Where a photo task's move shows up: a herd member's signature activity, a critter state, the firefly wave of an
    // island, a diving seabird or a songbird flock in its murmuration.
    public enum PhotoMoveSource { Herd, Critter, FireflyWave, SeabirdDive, Murmur }

    public sealed class PhotoTask
    {
        public int index;
        public LifeKind kind;
        // CollectionCatalog index of the species.
        public int entry;
        public PhotoMoveSource source;
        public AnimalActivity activity;
        public CritterState critterState;
        // "Schlammtanz", "beim Schlammtanz", "Flamingo beim Schlammtanz", "Die Flamingos tanzen im Schlamm!"
        public string move, phrase, title, hint;
    }

    // "Fotoaufgaben": one per species that has a move of its own (the signature moves of the herds, the special moves
    // of the critters and the two bird flights). Species that are not in the collection catalog get none, and neither
    // do the sea animals: they have no move to catch.
    public static class PhotoTaskCatalog
    {
        static readonly PhotoTask[] Tasks;
        static readonly int[] ByKind = new int[64];

        public static int Count => Tasks.Length;
        // Bit (int)LifeKind of every kind with a task.
        public static ulong Mask { get; private set; }

        static PhotoTaskCatalog()
        {
            for (int i = 0; i < ByKind.Length; i++) ByKind[i] = -1;
            var list = new List<PhotoTask>();
            for (int i = 0; i < CollectionCatalog.Count; i++)
            {
                var e = CollectionCatalog.At(i);
                if (e.type != CollectType.Animal) continue;
                var act = IslandHerdSystem.SignatureOf(e.life);
                if (!IslandHerdSystem.IsSignature(act) || !HerdMove(act, out string move, out string phrase)) continue;
                Add(list, e.life, PhotoMoveSource.Herd, act, CritterState.Idle, move, phrase);
            }
            Add(list, LifeKind.Crab, PhotoMoveSource.Critter, AnimalActivity.None, CritterState.Wave, "Winkertanz", "beim Winkertanz");
            Add(list, LifeKind.Turtle, PhotoMoveSource.Critter, AnimalActivity.None, CritterState.Nest, "Nestbau", "beim Nestbau");
            Add(list, LifeKind.Butterfly, PhotoMoveSource.Critter, AnimalActivity.None, CritterState.Spiral, "Spiraltanz", "beim Spiraltanz");
            Add(list, LifeKind.Firefly, PhotoMoveSource.FireflyWave, AnimalActivity.None, CritterState.Idle, "Lichtwelle", "bei der Lichtwelle");
            Add(list, LifeKind.Seabird, PhotoMoveSource.SeabirdDive, AnimalActivity.None, CritterState.Idle, "Stoßtauchen", "beim Stoßtauchen");
            Add(list, LifeKind.Bird, PhotoMoveSource.Murmur, AnimalActivity.None, CritterState.Idle, "Schwarmtanz", "beim Schwarmtanz");
            Tasks = list.ToArray();
        }

        static void Add(List<PhotoTask> list, LifeKind kind, PhotoMoveSource source, AnimalActivity act, CritterState state, string move, string phrase)
        {
            int entry = CollectionCatalog.IndexOf(kind);
            if (entry < 0 || ByKind[(int)kind] >= 0) return;
            ByKind[(int)kind] = list.Count;
            Mask |= 1UL << (int)kind;
            list.Add(new PhotoTask
            {
                index = list.Count, kind = kind, entry = entry, source = source, activity = act, critterState = state,
                move = move, phrase = phrase, title = LifeNames.Of(kind) + " " + phrase, hint = HintOf(kind),
            });
        }

        // The owner's German names of the signature moves.
        static bool HerdMove(AnimalActivity act, out string move, out string phrase)
        {
            switch (act)
            {
                case AnimalActivity.Zigzag: move = "Hakenschlagen"; break;
                case AnimalActivity.Carousel: move = "Schafkreisel"; break;
                case AnimalActivity.Rear: move = "Aufrichten"; break;
                case AnimalActivity.Scratch: move = "Scheuern"; break;
                case AnimalActivity.Snuggle: move = "Kuschelstern"; break;
                case AnimalActivity.StampDance: move = "Schlammtanz"; break;
                case AnimalActivity.NeckDuel: move = "Halsrecken"; break;
                case AnimalActivity.SkyCall: move = "Himmelsruf"; break;
                case AnimalActivity.Crater: move = "Kraterscharren"; break;
                case AnimalActivity.TailChase: move = "Schwanzjagd"; break;
                case AnimalActivity.WarDance: move = "Kriegstanz"; break;
                case AnimalActivity.Groom: move = "Fellpflege"; break;
                case AnimalActivity.Necking: move = "Halskampf"; break;
                default: move = phrase = null; return false;
            }
            phrase = (act == AnimalActivity.TailChase || act == AnimalActivity.Groom ? "bei der " : "beim ") + move;
            return true;
        }

        static string HintOf(LifeKind kind)
        {
            switch (kind)
            {
                case LifeKind.Hare: return "Ein Hase schlägt Haken!";
                case LifeKind.Sheep: return "Die Schafe drehen ihren Kreisel!";
                case LifeKind.Goat: return "Eine Ziege richtet sich auf!";
                case LifeKind.Ox: return "Ein Ochse scheuert sich am Baum!";
                case LifeKind.Capybara: return "Die Wasserschweine kuscheln im Stern!";
                case LifeKind.Flamingo: return "Die Flamingos tanzen im Schlamm!";
                case LifeKind.Tortoise: return "Zwei Riesenschildkröten recken die Hälse!";
                case LifeKind.Penguin: return "Die Pinguine rufen zum Himmel!";
                case LifeKind.Reindeer: return "Ein Rentier scharrt einen Krater!";
                case LifeKind.ArcticFox: return "Ein Polarfuchs jagt seinen Schwanz!";
                case LifeKind.Meerkat: return "Die Erdmännchen tanzen den Kriegstanz!";
                case LifeKind.Zebra: return "Zwei Zebras pflegen sich das Fell!";
                case LifeKind.Giraffe: return "Zwei Giraffen kämpfen mit den Hälsen!";
                case LifeKind.Crab: return "Eine Krabbe winkt mit den Scheren!";
                case LifeKind.Turtle: return "Eine Schildkröte baut ihr Nest!";
                case LifeKind.Butterfly: return "Schmetterlinge tanzen im Spiralflug!";
                case LifeKind.Firefly: return "Eine Lichtwelle läuft durch die Glühwürmchen!";
                case LifeKind.Seabird: return "Ein Seevogel stößt ins Meer!";
                case LifeKind.Bird: return "Ein Vogelschwarm tanzt am Himmel!";
                default: return LifeNames.Plural(kind) + " zeigen ihren Tanz!";
            }
        }

        public static PhotoTask At(int index) => Tasks[index];

        // -1 for kinds without a task.
        public static int IndexOf(LifeKind kind)
        {
            int i = (int)kind;
            return i >= 0 && i < ByKind.Length ? ByKind[i] : -1;
        }

        public static int IndexOfEntry(int catalogIndex)
        {
            if (catalogIndex < 0 || catalogIndex >= CollectionCatalog.Count) return -1;
            var e = CollectionCatalog.At(catalogIndex);
            return e.hasLife ? IndexOf(e.life) : -1;
        }

        public static string DoneText(int[] tasks, int listed, int total)
        {
            if (total == 1 && listed >= 1) return "Fotoaufgabe erfüllt: " + Tasks[tasks[0]].title + "!";
            return total + " Fotoaufgaben erfüllt!";
        }
    }

    [Serializable]
    public class PhotoTaskSaveData
    {
        public int version = 1;
        // (int)LifeKind of every task done, when it was done ("yyyy-MM-dd HH:mm") and the file name of its picture.
        public int[] ids;
        public string[] dates;
        public string[] thumbs;
    }

    // Which photo tasks are done, across all runs (the journal of species and photos stays, like the LifeBook). Its own
    // small JSON file next to the saves plus one picture per task in a folder beside it; a missing or broken file is an
    // empty book. Without a directory the book lives in memory only (Edit Mode preview).
    public sealed class PhotoTaskBook
    {
        public const string FileName = "drift_phototasks.json";
        public const string ThumbFolder = "PhotoTasks";
        const string DateFormat = "yyyy-MM-dd HH:mm";

        readonly bool[] _done = new bool[PhotoTaskCatalog.Count];
        readonly DateTime[] _when = new DateTime[PhotoTaskCatalog.Count];
        readonly string[] _thumb = new string[PhotoTaskCatalog.Count];

        public string Directory { get; }
        public int DoneCount { get; private set; }
        public static int Total => PhotoTaskCatalog.Count;
        public int Version { get; private set; }
        // Bit (int)LifeKind of every kind whose task is still open.
        public ulong OpenMask { get; private set; } = PhotoTaskCatalog.Mask;
        public bool AllDone => DoneCount == Total;

        public PhotoTaskBook(string directory) => Directory = directory;

        public static string DefaultDirectory => Application.persistentDataPath;

        public string FilePath => string.IsNullOrEmpty(Directory) ? null : Path.Combine(Directory, FileName);
        public string ThumbDirectory => string.IsNullOrEmpty(Directory) ? null : Path.Combine(Directory, ThumbFolder);

        public bool IsDone(int task) => task >= 0 && task < _done.Length && _done[task];
        public bool IsOpen(LifeKind kind) => (OpenMask & (1UL << (int)kind)) != 0;
        public DateTime DoneAt(int task) => IsDone(task) ? _when[task] : DateTime.MinValue;
        public string ThumbName(int task) => IsDone(task) ? _thumb[task] : null;

        public string ThumbPath(int task)
        {
            string name = ThumbName(task), dir = ThumbDirectory;
            return string.IsNullOrEmpty(name) || dir == null ? null : Path.Combine(dir, name);
        }

        // Where the picture of a task is written ("task_17.png", by LifeKind so it never changes).
        public string NewThumbPath(int task) => ThumbDirectory == null ? null : Path.Combine(ThumbDirectory, ThumbFileName(task));
        public static string ThumbFileName(int task) => "task_" + (int)PhotoTaskCatalog.At(task).kind + ".png";

        public string Line => "Fotoaufgaben " + DoneCount + "/" + Total;
        public static string DateLabel(DateTime t) => t == DateTime.MinValue ? "" : t.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        // False when the task was done already.
        public bool Complete(int task, DateTime when, string thumbName = null)
        {
            if (task < 0 || task >= _done.Length || _done[task]) return false;
            _done[task] = true;
            _when[task] = when;
            _thumb[task] = thumbName;
            DoneCount++;
            OpenMask &= ~(1UL << (int)PhotoTaskCatalog.At(task).kind);
            Version++;
            return true;
        }

        public void SetThumb(int task, string thumbName)
        {
            if (!IsDone(task)) return;
            _thumb[task] = thumbName;
            Version++;
        }

        public void Clear()
        {
            for (int i = 0; i < _done.Length; i++)
            {
                _done[i] = false;
                _when[i] = DateTime.MinValue;
                _thumb[i] = null;
            }
            DoneCount = 0;
            OpenMask = PhotoTaskCatalog.Mask;
            Version++;
        }

        public PhotoTaskSaveData Capture()
        {
            var d = new PhotoTaskSaveData { ids = new int[DoneCount], dates = new string[DoneCount], thumbs = new string[DoneCount] };
            int k = 0;
            for (int i = 0; i < _done.Length; i++)
            {
                if (!_done[i]) continue;
                d.ids[k] = (int)PhotoTaskCatalog.At(i).kind;
                d.dates[k] = _when[i].ToString(DateFormat, CultureInfo.InvariantCulture);
                d.thumbs[k] = _thumb[i] ?? "";
                k++;
            }
            return d;
        }

        // null restores an empty book; kinds without a task (any more) are skipped.
        public void Restore(PhotoTaskSaveData d)
        {
            Clear();
            if (d?.ids == null) return;
            for (int k = 0; k < d.ids.Length; k++)
            {
                int task = PhotoTaskCatalog.IndexOf((LifeKind)d.ids[k]);
                if (task < 0) continue;
                string date = d.dates != null && k < d.dates.Length ? d.dates[k] : null;
                if (!DateTime.TryParseExact(date, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime when)) when = DateTime.MinValue;
                string thumb = d.thumbs != null && k < d.thumbs.Length ? d.thumbs[k] : null;
                Complete(task, when, string.IsNullOrEmpty(thumb) || thumb.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? null : thumb);
            }
        }

        public void Load()
        {
            string path = FilePath;
            PhotoTaskSaveData d = null;
            if (path != null && File.Exists(path))
            {
                try
                {
                    d = JsonUtility.FromJson<PhotoTaskSaveData>(File.ReadAllText(path));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("PhotoTaskBook: could not read " + path + ": " + e.Message);
                }
            }
            Restore(d);
        }

        public bool Save()
        {
            string path = FilePath;
            if (path == null) return false;
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(Capture()));
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
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("PhotoTaskBook: could not write " + path + ": " + e.Message);
                return false;
            }
        }
    }
}
