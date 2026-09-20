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

    // The collection album of one run. Every entry of CollectionCatalog goes unknown -> seen (it was near the
    // camera focus) -> collected (it lives, or has lived, on the player's island) and never back; next to that
    // the run times of both steps, the best count ever on the island and the count right now. Pure C#, saved
    // as one block of the save file; a file without the block starts empty.
    public sealed class DiscoveryJournal
    {
        static readonly LifeKind[] LegacyLife =
        {
            LifeKind.Hare, LifeKind.Sheep, LifeKind.Goat, LifeKind.Ox, LifeKind.Bird, LifeKind.Seabird,
            LifeKind.Crab, LifeKind.Turtle, LifeKind.Butterfly, LifeKind.Firefly,
        };

        readonly int _n = CollectionCatalog.Count;
        readonly byte[] _state;
        readonly float[] _seenAt, _collectedAt;
        readonly int[] _best, _current;
        ulong _unseenLife, _present;

        public int YoungBorn { get; private set; }
        public int FiresSeen { get; private set; }
        // Highest SettlementStage the player's island reached in this run, as its number.
        public int BestStage { get; private set; }
        // Bumped on every change so a panel can refresh only when something happened.
        public int Version { get; private set; }
        public int SeenCount { get; private set; }
        public int CollectedCount { get; private set; }
        // False until the first ReportPresence after a Reset or Restore: what the island holds at that moment is
        // where the run starts from, not news.
        public bool Baselined { get; private set; }
        // Bit (int)LifeKind of the catalogued life kinds nobody has seen yet; 0 lets a scan skip the islands.
        public ulong UnseenLifeMask => _unseenLife;
        // Bit (int)LifeKind of what the last ReportPresence found on the island.
        public ulong PresentMask => _present;
        public bool AllSeen => SeenCount == _n;
        public bool Complete => CollectedCount == CollectionCatalog.CollectibleCount && AllSeen;

        public DiscoveryJournal()
        {
            _state = new byte[_n];
            _seenAt = new float[_n];
            _collectedAt = new float[_n];
            _best = new int[_n];
            _current = new int[_n];
            _unseenLife = CollectionCatalog.LifeMask;
        }

        public static int LegacyIndex(JournalSpecies s) =>
            s == JournalSpecies.Fish ? CollectionCatalog.FishIndex
            : (int)s >= 0 && (int)s < LegacyLife.Length ? CollectionCatalog.IndexOf(LegacyLife[(int)s]) : -1;

        public CollectState StateOf(int index) => (CollectState)_state[index];
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

        public bool MarkSeen(int index, float time)
        {
            if (index < 0 || index >= _n || _state[index] != (byte)CollectState.Unknown) return false;
            Promote(index, CollectState.Seen, time);
            return true;
        }

        public bool MarkSeen(LifeKind kind, float time) => MarkSeen(CollectionCatalog.IndexOf(kind), time);
        public bool MarkSeen(SeaKind kind, float time) => MarkSeen(CollectionCatalog.IndexOf(kind), time);

        // Every unseen kind of the mask (bit = (int)LifeKind) at once; returns the bits that were new.
        public ulong MarkSeen(ulong lifeMask, float time)
        {
            ulong fresh = lifeMask & _unseenLife;
            for (ulong rest = fresh; rest != 0; rest &= rest - 1) MarkSeen(CollectionCatalog.IndexOf((LifeKind)LowestBit(rest)), time);
            return fresh;
        }

        // False for what cannot live on an island and for what is collected already. Collecting something
        // nobody had seen before counts as seeing it in the same moment.
        public bool MarkCollected(int index, float time)
        {
            if (index < 0 || index >= _n || _state[index] == (byte)CollectState.Collected) return false;
            if (!CollectionCatalog.At(index).collectible) return false;
            Promote(index, CollectState.Collected, time);
            return true;
        }

        public bool MarkCollected(LifeKind kind, float time) => MarkCollected(CollectionCatalog.IndexOf(kind), time);

        void Promote(int index, CollectState to, float time)
        {
            time = Mathf.Max(0f, time);
            if (_state[index] == (byte)CollectState.Unknown)
            {
                _seenAt[index] = time;
                SeenCount++;
                var e = CollectionCatalog.At(index);
                if (e.hasLife) _unseenLife &= ~(1UL << (int)e.life);
            }
            if (to == CollectState.Collected)
            {
                _collectedAt[index] = time;
                CollectedCount++;
            }
            _state[index] = (byte)to;
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

        public void Reset()
        {
            for (int i = 0; i < _n; i++)
            {
                _state[i] = 0;
                _seenAt[i] = _collectedAt[i] = 0f;
                _best[i] = _current[i] = 0;
            }
            SeenCount = CollectedCount = 0;
            _unseenLife = CollectionCatalog.LifeMask;
            _present = 0;
            Baselined = false;
            YoungBorn = 0;
            FiresSeen = 0;
            BestStage = 0;
            Version++;
        }

        public JournalSaveData Capture()
        {
            int n = SeenCount;
            var d = new JournalSaveData
            {
                ids = new int[n], states = new int[n], seenAt = new float[n], collectedAt = new float[n], best = new int[n],
                youngBorn = YoungBorn, firesSeen = FiresSeen, bestStage = BestStage,
            };
            int k = 0;
            for (int i = 0; i < _n; i++)
            {
                if (_state[i] == (byte)CollectState.Unknown) continue;
                d.ids[k] = CollectionCatalog.At(i).id;
                d.states[k] = _state[i];
                d.seenAt[k] = _seenAt[i];
                d.collectedAt[k] = _collectedAt[i];
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
                    if (i < 0 || _state[i] != 0) continue;
                    int state = Mathf.Clamp(At(d.states, k, (int)CollectState.Seen), (int)CollectState.Seen, (int)CollectState.Collected);
                    if (state == (int)CollectState.Collected && !CollectionCatalog.At(i).collectible) state = (int)CollectState.Seen;
                    float seen = Mathf.Max(0f, At(d.seenAt, k, 0f));
                    Promote(i, CollectState.Seen, seen);
                    if (state == (int)CollectState.Collected) Promote(i, CollectState.Collected, Mathf.Max(seen, At(d.collectedAt, k, 0f)));
                    _best[i] = Mathf.Max(0, At(d.best, k, 0));
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
