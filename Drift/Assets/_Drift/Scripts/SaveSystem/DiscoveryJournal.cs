using System;
using Drift.Core;
using Drift.Life;
using UnityEngine;

namespace Drift.SaveSystem
{
    // The species table of save versions up to 5. Only read: Restore turns such a block into "seen" entries.
    public enum JournalSpecies { Hare, Sheep, Goat, Ox, Bird, Seabird, Crab, Turtle, Butterfly, Firefly, Fish }

    [Serializable]
    public class JournalSaveData
    {
        // v5 block (11 species, seen only). Never written any more.
        public int[] species;
        public float[] firstSeen;
        public int youngBorn;
        public int firesSeen;
        // v6 block: one slot per entry that is at least seen. ids are CollectEntry.id, states CollectState.
        public int[] ids;
        public int[] states;
        public float[] seenAt;
        public float[] collectedAt;
        public int[] best;
        public int bestStage;
    }

    // The collection album. Every entry of CollectionCatalog goes unknown -> seen (it was near the camera focus) ->
    // collected (it lives, or has lived, on the player's island) and never back; next to that the run times of both
    // steps, the best count ever on the island and the count right now. Pure C#, saved as one block of the save file;
    // a file without the block starts empty.
    // Two layers: the run layer (RunStateOf, RunSeenCount, ...: what this run met, which is what the save file holds)
    // and the album layer (StateOf, SeenCount, ...: what the UI shows). Without a JournalBook both are the same. With
    // one attached (AttachBook) the album layer is everything ever met across runs: Reset and Restore only clear the
    // run layer and the album keeps the book, and whatever a run or an old save adds flows into the book.
    public sealed class DiscoveryJournal
    {
        // Real-world clock for the album dates; tests pin it.
        public static Func<DateTime> Clock = () => DateTime.Now;

        static readonly LifeKind[] LegacyLife =
        {
            LifeKind.Hare, LifeKind.Sheep, LifeKind.Goat, LifeKind.Ox, LifeKind.Bird, LifeKind.Seabird,
            LifeKind.Crab, LifeKind.Turtle, LifeKind.Butterfly, LifeKind.Firefly,
        };

        readonly int _n = CollectionCatalog.Count;
        readonly byte[] _state, _run;
        // Run times; -1 when the step happened in an earlier run (known from the book).
        readonly float[] _seenAt, _collectedAt;
        readonly DateTime[] _seenOn, _collectedOn;
        readonly int[] _best, _current;
        ulong _unseenLife, _runUnseenLife, _present;
        JournalBook _book;

        public int YoungBorn { get; private set; }
        public int FiresSeen { get; private set; }
        // Highest SettlementStage the player's island reached in this run, as its number.
        public int BestStage { get; private set; }
        // Bumped on every change so a panel can refresh only when something happened.
        public int Version { get; private set; }
        public int SeenCount { get; private set; }
        public int CollectedCount { get; private set; }
        // This run only: entries met, entries that lived on the island, and entries met for the very first time
        // ("neu in diesem Durchgang"; counted since the run was started or loaded).
        public int RunSeenCount { get; private set; }
        public int RunCollectedCount { get; private set; }
        public int NewThisRun { get; private set; }
        public JournalBook Book => _book;
        public bool HasBook => _book != null;
        // False until the first ReportPresence after a Reset or Restore: what the island holds at that moment is
        // where the run starts from, not news.
        public bool Baselined { get; private set; }
        // Bit (int)LifeKind of the catalogued life kinds nobody has seen yet; 0 lets a scan skip the islands.
        public ulong UnseenLifeMask => _unseenLife;
        // The complement: every catalogued life kind seen in any run the album knows of.
        public ulong SeenLifeMask => CollectionCatalog.LifeMask & ~_unseenLife;
        // The same for this run: what a sighting scan still has to look for.
        public ulong RunUnseenLifeMask => _runUnseenLife;
        public bool RunAllSeen => RunSeenCount == _n;
        // Bit (int)LifeKind of what the last ReportPresence found on the island.
        public ulong PresentMask => _present;
        public bool AllSeen => SeenCount == _n;
        public bool Complete => CollectedCount == CollectionCatalog.CollectibleCount && AllSeen;

        public DiscoveryJournal()
        {
            _state = new byte[_n];
            _run = new byte[_n];
            _seenAt = new float[_n];
            _collectedAt = new float[_n];
            _seenOn = new DateTime[_n];
            _collectedOn = new DateTime[_n];
            _best = new int[_n];
            _current = new int[_n];
            _unseenLife = _runUnseenLife = CollectionCatalog.LifeMask;
        }

        // Links the cross-run book: what it knows joins the album at once, and what the album knows goes into the book.
        public void AttachBook(JournalBook book)
        {
            if (book == _book) return;
            _book = book;
            if (book == null) return;
            book.Absorb(this);
            LearnFromBook();
            Version++;
        }

        void LearnFromBook()
        {
            for (int i = 0; i < _n; i++)
            {
                var st = _book.StateOf(i);
                if (st == CollectState.Unknown) continue;
                if (_state[i] == (byte)CollectState.Unknown)
                {
                    _state[i] = (byte)CollectState.Seen;
                    _seenAt[i] = -1f;
                    _seenOn[i] = _book.SeenOn(i);
                    SeenCount++;
                    var e = CollectionCatalog.At(i);
                    if (e.hasLife) _unseenLife &= ~(1UL << (int)e.life);
                }
                if (st == CollectState.Collected && _state[i] != (byte)CollectState.Collected && CollectionCatalog.At(i).collectible)
                {
                    _state[i] = (byte)CollectState.Collected;
                    _collectedAt[i] = -1f;
                    _collectedOn[i] = _book.CollectedOn(i);
                    CollectedCount++;
                }
                _best[i] = Mathf.Max(_best[i], _book.BestOf(i));
            }
        }

        public static int LegacyIndex(JournalSpecies s) =>
            s == JournalSpecies.Fish ? CollectionCatalog.FishIndex
            : (int)s >= 0 && (int)s < LegacyLife.Length ? CollectionCatalog.IndexOf(LegacyLife[(int)s]) : -1;

        public CollectState StateOf(int index) => (CollectState)_state[index];
        public CollectState RunStateOf(int index) => (CollectState)_run[index];
        // Seen or collected in an earlier run only (the run times are then unknown; SeenOn/CollectedOn hold the day).
        public bool SeenBefore(int index) => _state[index] != 0 && _seenAt[index] < 0f;
        public bool CollectedBefore(int index) => _state[index] == (byte)CollectState.Collected && _collectedAt[index] < 0f;
        public DateTime SeenOn(int index) => _seenOn[index];
        public DateTime CollectedOn(int index) => _collectedOn[index];
        public CollectState StateOf(LifeKind kind) => StateOfOrUnknown(CollectionCatalog.IndexOf(kind));
        public CollectState StateOf(SeaKind kind) => StateOfOrUnknown(CollectionCatalog.IndexOf(kind));
        CollectState StateOfOrUnknown(int index) => index >= 0 ? (CollectState)_state[index] : CollectState.Unknown;

        public float SeenAt(int index) => _seenAt[index];
        public float CollectedAt(int index) => _collectedAt[index];
        public int BestOf(int index) => _best[index];
        public int CurrentOf(int index) => _current[index];

        // Whether the entry lives on the island right now (collectibles only).
        public bool PresentNow(int index)
        {
            var e = CollectionCatalog.At(index);
            return e.collectible && ((_present & (1UL << (int)e.life)) != 0 || _current[index] > 0);
        }

        public int SeenIn(CollectSection section) => CountIn(section, CollectState.Seen);
        public int CollectedIn(CollectSection section) => CountIn(section, CollectState.Collected);

        int CountIn(CollectSection section, CollectState atLeast)
        {
            var entries = CollectionCatalog.EntriesOf(section);
            int n = 0;
            for (int i = 0; i < entries.Length; i++) if (_state[entries[i]] >= (byte)atLeast) n++;
            return n;
        }

        // Bit (int)LifeKind of everything collected so far (what the permanent LifeBook adds up).
        public ulong CollectedLifeMask
        {
            get
            {
                ulong mask = 0;
                for (int i = 0; i < _n; i++)
                    if (_state[i] == (byte)CollectState.Collected) mask |= 1UL << (int)CollectionCatalog.At(i).life;
                return mask;
            }
        }

        // True only for a first sighting ever (album layer); a kind known from an earlier run is still noted for this run.
        public bool MarkSeen(int index, float time)
        {
            if (index < 0 || index >= _n || _run[index] != (byte)CollectState.Unknown) return false;
            bool fresh = _state[index] == (byte)CollectState.Unknown;
            Promote(index, CollectState.Seen, time);
            return fresh;
        }

        public bool MarkSeen(LifeKind kind, float time) => MarkSeen(CollectionCatalog.IndexOf(kind), time);
        public bool MarkSeen(SeaKind kind, float time) => MarkSeen(CollectionCatalog.IndexOf(kind), time);

        // Every unseen kind of the mask (bit = (int)LifeKind) at once; returns the bits that were new.
        public ulong MarkSeen(ulong lifeMask, float time)
        {
            ulong fresh = lifeMask & _unseenLife;
            for (ulong rest = lifeMask & _runUnseenLife; rest != 0; rest &= rest - 1) MarkSeen(CollectionCatalog.IndexOf((LifeKind)LowestBit(rest)), time);
            return fresh;
        }

        // False for what cannot live on an island and for what this run collected already (true is news for the island
        // even when an earlier run had the kind). Collecting something nobody had seen counts as seeing it too.
        public bool MarkCollected(int index, float time)
        {
            if (index < 0 || index >= _n || _run[index] == (byte)CollectState.Collected) return false;
            if (!CollectionCatalog.At(index).collectible) return false;
            Promote(index, CollectState.Collected, time);
            return true;
        }

        public bool MarkCollected(LifeKind kind, float time) => MarkCollected(CollectionCatalog.IndexOf(kind), time);

        // Raises both layers to at least `to`.
        void Promote(int index, CollectState to, float time)
        {
            time = Mathf.Max(0f, time);
            var e = CollectionCatalog.At(index);
            ulong bit = e.hasLife ? 1UL << (int)e.life : 0UL;
            if (_state[index] == (byte)CollectState.Unknown)
            {
                _state[index] = (byte)CollectState.Seen;
                _seenAt[index] = time;
                _seenOn[index] = Clock();
                SeenCount++;
                NewThisRun++;
                _unseenLife &= ~bit;
            }
            if (to == CollectState.Collected && _state[index] != (byte)CollectState.Collected)
            {
                _state[index] = (byte)CollectState.Collected;
                _collectedAt[index] = time;
                _collectedOn[index] = Clock();
                CollectedCount++;
            }
            if (_run[index] == (byte)CollectState.Unknown)
            {
                _run[index] = (byte)CollectState.Seen;
                RunSeenCount++;
                _runUnseenLife &= ~bit;
            }
            if (to == CollectState.Collected && _run[index] != (byte)CollectState.Collected)
            {
                _run[index] = (byte)CollectState.Collected;
                RunCollectedCount++;
            }
            Version++;
        }

        // What lives on the player's island right now (herd species | plants | critters, bit = (int)LifeKind).
        // An unchanged mask costs one compare. Returns the bits that were collected by this report; losing a
        // species only clears its presence, the entry stays collected.
        public ulong ReportPresence(ulong lifeMask, float time)
        {
            lifeMask &= CollectionCatalog.CollectibleMask;
            bool first = !Baselined;
            Baselined = true;
            if (lifeMask == _present && !first) return 0;
            ulong gone = _present & ~lifeMask;
            _present = lifeMask;
            ulong fresh = 0;
            for (ulong rest = lifeMask; rest != 0; rest &= rest - 1)
            {
                int bit = LowestBit(rest);
                if (MarkCollected(CollectionCatalog.IndexOf((LifeKind)bit), time)) fresh |= 1UL << bit;
            }
            for (ulong rest = gone; rest != 0; rest &= rest - 1)
            {
                int index = CollectionCatalog.IndexOf((LifeKind)LowestBit(rest));
                if (index >= 0) _current[index] = 0;
            }
            Version++;
            return fresh;
        }

        // The count on the island right now; also keeps the record.
        public void SetCount(int index, int count)
        {
            if (index < 0 || index >= _n) return;
            count = Mathf.Max(0, count);
            if (count == _current[index]) return;
            _current[index] = count;
            if (count > _best[index]) _best[index] = count;
            Version++;
        }

        public void SetCount(LifeKind kind, int count) => SetCount(CollectionCatalog.IndexOf(kind), count);

        public void ReportStage(int stage)
        {
            if (stage <= BestStage) return;
            BestStage = stage;
            Version++;
        }

        public void AddYoungBorn(int n)
        {
            if (n <= 0) return;
            YoungBorn += n;
            Version++;
        }

        public void AddFiresSeen(int n)
        {
            if (n <= 0) return;
            FiresSeen += n;
            Version++;
        }

        // A new run. With a book attached the album keeps everything (the run's news goes into the book first) and
        // only the run layer starts empty.
        public void Reset()
        {
            if (_book != null) _book.Absorb(this);
            for (int i = 0; i < _n; i++)
            {
                _state[i] = _run[i] = 0;
                _seenAt[i] = _collectedAt[i] = 0f;
                _seenOn[i] = _collectedOn[i] = DateTime.MinValue;
                _best[i] = _current[i] = 0;
            }
            SeenCount = CollectedCount = RunSeenCount = RunCollectedCount = NewThisRun = 0;
            _unseenLife = _runUnseenLife = CollectionCatalog.LifeMask;
            if (_book != null) LearnFromBook();
            _present = 0;
            Baselined = false;
            YoungBorn = 0;
            FiresSeen = 0;
            BestStage = 0;
            Version++;
        }

        // The run layer (what this run met); the album of a book-backed journal lives in the book.
        public JournalSaveData Capture()
        {
            int n = RunSeenCount;
            var d = new JournalSaveData
            {
                ids = new int[n], states = new int[n], seenAt = new float[n], collectedAt = new float[n], best = new int[n],
                youngBorn = YoungBorn, firesSeen = FiresSeen, bestStage = BestStage,
            };
            int k = 0;
            for (int i = 0; i < _n; i++)
            {
                if (_run[i] == (byte)CollectState.Unknown) continue;
                d.ids[k] = CollectionCatalog.At(i).id;
                d.states[k] = _run[i];
                d.seenAt[k] = Mathf.Max(0f, _seenAt[i]);
                d.collectedAt[k] = Mathf.Max(0f, _collectedAt[i]);
                d.best[k] = _best[i];
                k++;
            }
            return d;
        }

        // null (a file without the block) restores an empty journal; unknown ids are skipped. A v5 block (species
        // without ids) comes back as "seen": whether those animals live on the island shows with the first scan.
        public void Restore(JournalSaveData d)
        {
            Reset();
            if (d == null) return;
            if (d.ids != null && d.ids.Length > 0)
            {
                for (int k = 0; k < d.ids.Length; k++)
                {
                    int i = CollectionCatalog.IndexOfId(d.ids[k]);
                    if (i < 0 || _run[i] != 0) continue;
                    int state = Mathf.Clamp(At(d.states, k, (int)CollectState.Seen), (int)CollectState.Seen, (int)CollectState.Collected);
                    if (state == (int)CollectState.Collected && !CollectionCatalog.At(i).collectible) state = (int)CollectState.Seen;
                    float seen = Mathf.Max(0f, At(d.seenAt, k, 0f));
                    Promote(i, CollectState.Seen, seen);
                    if (state == (int)CollectState.Collected) Promote(i, CollectState.Collected, Mathf.Max(seen, At(d.collectedAt, k, 0f)));
                    _best[i] = Mathf.Max(_best[i], At(d.best, k, 0));
                }
            }
            else if (d.species != null)
            {
                for (int k = 0; k < d.species.Length; k++)
                {
                    int s = d.species[k];
                    if (s < 0 || s > (int)JournalSpecies.Fish) continue;
                    MarkSeen(LegacyIndex((JournalSpecies)s), At(d.firstSeen, k, 0f));
                }
            }
            YoungBorn = Mathf.Max(0, d.youngBorn);
            FiresSeen = Mathf.Max(0, d.firesSeen);
            BestStage = Mathf.Max(0, d.bestStage);
            Version++;
        }

        static int At(int[] a, int k, int fallback) => a != null && k < a.Length ? a[k] : fallback;
        static float At(float[] a, int k, float fallback) => a != null && k < a.Length ? a[k] : fallback;

        static int LowestBit(ulong v)
        {
            int n = 0;
            while ((v & 1UL) == 0) { v >>= 1; n++; }
            return n;
        }

        public static int BitCount(ulong v)
        {
            int n = 0;
            while (v != 0) { v &= v - 1; n++; }
            return n;
        }

        public static string FormatTime(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }
    }
}
