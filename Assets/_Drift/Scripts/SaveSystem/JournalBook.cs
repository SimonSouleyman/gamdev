using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Drift.SaveSystem
{
    [Serializable]
    public class JournalBookData
    {
        public int version = 1;
        // One slot per entry that is at least seen: CollectEntry.id, CollectState, the days of both steps
        // ("yyyy-MM-dd HH:mm", "" when unknown) and the best count ever on an island.
        public int[] ids;
        public int[] states;
        public string[] seen;
        public string[] collected;
        public int[] best;
    }

    // The species journal across all runs ("Tagebuch mit Arten und Fotos bleibt"): every catalog entry ever seen or
    // collected, its days and its record count. Its own small JSON file in persistentDataPath, independent of the
    // per-run save; DiscoveryJournal.AttachBook merges both ways. A missing or broken file is an empty book; without a
    // directory the book lives in memory only.
    public sealed class JournalBook
    {
        public const string FileName = "drift_journal.json";
        const string DateFormat = "yyyy-MM-dd HH:mm";

        readonly byte[] _state = new byte[CollectionCatalog.Count];
        readonly DateTime[] _seenOn = new DateTime[CollectionCatalog.Count];
        readonly DateTime[] _collectedOn = new DateTime[CollectionCatalog.Count];
        readonly int[] _best = new int[CollectionCatalog.Count];

        public string Directory { get; }
        public string FilePath => string.IsNullOrEmpty(Directory) ? null : Path.Combine(Directory, FileName);
        public static string DefaultDirectory => Application.persistentDataPath;
        // Bumped whenever the book learns something; a caller saves when it differs from the version it wrote.
        public int Version { get; private set; }
        public int SeenCount { get; private set; }
        public int CollectedCount { get; private set; }

        public JournalBook(string directory) => Directory = directory;

        public CollectState StateOf(int index) => (CollectState)_state[index];
        public DateTime SeenOn(int index) => _seenOn[index];
        public DateTime CollectedOn(int index) => _collectedOn[index];
        public int BestOf(int index) => _best[index];

        // Raises one entry; false when it knew all of it already.
        public bool Learn(int index, CollectState state, DateTime seenOn, DateTime collectedOn, int best)
        {
            if (index < 0 || index >= _state.Length || state == CollectState.Unknown) return false;
            if (state == CollectState.Collected && !CollectionCatalog.At(index).collectible) state = CollectState.Seen;
            bool changed = false;
            if (_state[index] == (byte)CollectState.Unknown)
            {
                _state[index] = (byte)CollectState.Seen;
                _seenOn[index] = seenOn;
                SeenCount++;
                changed = true;
            }
            else if (_seenOn[index] == DateTime.MinValue && seenOn != DateTime.MinValue)
            {
                _seenOn[index] = seenOn;
                changed = true;
            }
            if (state == CollectState.Collected && _state[index] != (byte)CollectState.Collected)
            {
                _state[index] = (byte)CollectState.Collected;
                _collectedOn[index] = collectedOn;
                CollectedCount++;
                changed = true;
            }
            if (best > _best[index])
            {
                _best[index] = best;
                changed = true;
            }
            if (changed) Version++;
            return changed;
        }

        // Everything the journal's album knows; true when the book learnt something.
        public bool Absorb(DiscoveryJournal j)
        {
            bool changed = false;
            for (int i = 0; i < _state.Length; i++)
            {
                var st = j.StateOf(i);
                if (st == CollectState.Unknown) continue;
                changed |= Learn(i, st, j.SeenOn(i), j.CollectedOn(i), j.BestOf(i));
            }
            return changed;
        }

        public void Clear()
        {
            Array.Clear(_state, 0, _state.Length);
            Array.Clear(_seenOn, 0, _seenOn.Length);
            Array.Clear(_collectedOn, 0, _collectedOn.Length);
            Array.Clear(_best, 0, _best.Length);
            SeenCount = CollectedCount = 0;
            Version++;
        }

        static string Stamp(DateTime t) => t == DateTime.MinValue ? "" : t.ToString(DateFormat, CultureInfo.InvariantCulture);

        static DateTime Parse(string[] a, int k) =>
            a != null && k < a.Length && DateTime.TryParseExact(a[k], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : DateTime.MinValue;

        public JournalBookData Capture()
        {
            int n = SeenCount;
            var d = new JournalBookData { ids = new int[n], states = new int[n], seen = new string[n], collected = new string[n], best = new int[n] };
            int k = 0;
            for (int i = 0; i < _state.Length; i++)
            {
                if (_state[i] == 0) continue;
                d.ids[k] = CollectionCatalog.At(i).id;
                d.states[k] = _state[i];
                d.seen[k] = Stamp(_seenOn[i]);
                d.collected[k] = Stamp(_collectedOn[i]);
                d.best[k] = _best[i];
                k++;
            }
            return d;
        }

        // null restores an empty book; unknown ids are skipped.
        public void Restore(JournalBookData d)
        {
            Clear();
            if (d?.ids == null) return;
            for (int k = 0; k < d.ids.Length; k++)
            {
                int i = CollectionCatalog.IndexOfId(d.ids[k]);
                int st = d.states != null && k < d.states.Length ? d.states[k] : (int)CollectState.Seen;
                st = Mathf.Clamp(st, (int)CollectState.Seen, (int)CollectState.Collected);
                Learn(i, (CollectState)st, Parse(d.seen, k), Parse(d.collected, k), d.best != null && k < d.best.Length ? Mathf.Max(0, d.best[k]) : 0);
            }
        }

        public void Load()
        {
            string path = FilePath;
            JournalBookData d = null;
            if (path != null && File.Exists(path))
            {
                try
                {
                    d = JsonUtility.FromJson<JournalBookData>(File.ReadAllText(path));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("JournalBook: could not read " + path + ": " + e.Message);
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
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("JournalBook: could not write " + path + ": " + e.Message);
                return false;
            }
        }
    }
}
