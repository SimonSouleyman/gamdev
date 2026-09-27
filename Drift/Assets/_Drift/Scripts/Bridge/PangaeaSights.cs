using System.Collections.Generic;
using Drift.Core;
using Drift.Islands;
using Drift.Life;
using UnityEngine;

namespace Drift.Bridge
{
    public enum SightKind { Herd, Flock, Lighthouse, Harbour, Village, Summit }

    // One stop of the "Sehenswürdigkeiten" tour over the finished Pangäa.
    public struct Sight
    {
        public SightKind kind;
        // Herd: its species and the herd index at the last rebuild; flock: the flock index; landmark / village: the
        // building index (the campfire for a village).
        public LifeKind species;
        public int index;
        public int village;
        public bool seabird;
        public int size;
        public string name;
        public Vector3 world;
        // Clockwise from north round the island's centre: the tour goes round the island, so "next" is a neighbour.
        public float angle;
    }

    // The things worth flying to on the finished Pangäa, in a ring round the island: one herd per species (the
    // biggest), the flocks (one per kind), the lighthouse, the harbour, every village at its campfire and the
    // highest summit. The fly-over's ‹ › buttons step through them. Rebuilt every few seconds (herds wander, villages
    // grow); the current stop keeps its place across a rebuild. No allocation once the list has grown, apart from the
    // label string when the shown stop changes.
    public sealed class PangaeaSights
    {
        public const string SummitName = "Gipfel";
        // A summit lower than this is no mountain worth a stop.
        public float summitMinHeight = 1f;
        public const int SummitGrid = 40;

        readonly List<Sight> _list = new List<Sight>(32);
        int _index = -1;
        int _summitVersion = int.MinValue;
        Island _summitIsland;
        Vector2 _summitLocal;
        float _summitHeight;

        public int Count => _list.Count;
        // The stop the view is at (or last flew to), -1 = none.
        public int Index => _index;
        public Sight this[int i] => _list[i];
        public bool HasSummit => _summitIsland != null && _summitHeight >= summitMinHeight;
        public Vector2 SummitLocal => _summitLocal;

        public void Clear()
        {
            _list.Clear();
            _index = -1;
        }

        public void Select(int i) => _index = i >= 0 && i < _list.Count ? i : -1;

        public void Rebuild(Island island, FlockSystem flocks)
        {
            bool had = _index >= 0 && _index < _list.Count;
            Sight current = had ? _list[_index] : default;
            _list.Clear();
            _index = -1;
            if (island == null) return;
            Vector2 centre = island.PlanarPosition;

            if (island.TryGetComponent(out IslandHerdSystem herds) && herds.isActiveAndEnabled)
            {
                for (int h = 0; h < herds.HerdCount; h++)
                {
                    int size = herds.HerdSize(h);
                    if (size == 0) continue;
                    var kind = herds.HerdKind(h);
                    int at = -1;
                    for (int k = 0; k < _list.Count; k++) if (_list[k].kind == SightKind.Herd && _list[k].species == kind) { at = k; break; }
                    if (at >= 0 && _list[at].size >= size) continue;
                    Vector2 c = herds.HerdCenter(h);
                    var s = new Sight
                    {
                        kind = SightKind.Herd, species = kind, index = h, size = size, name = LifeNames.Plural(kind),
                        world = island.transform.TransformPoint(c.x, Mathf.Max(0f, island.SampleHeight(c)), c.y),
                    };
                    if (at >= 0) _list[at] = s;
                    else _list.Add(s);
                }
            }

            if (flocks != null && flocks.isActiveAndEnabled)
            {
                float reach = island.BoundingRadius + 25f;
                int bestLand = -1, bestSea = -1;
                float dLand = float.MaxValue, dSea = float.MaxValue;
                for (int i = 0; i < flocks.FlockCount; i++)
                {
                    if (flocks.BirdCountOf(i) == 0) continue;
                    float d = (flocks.PositionOf(i) - centre).sqrMagnitude;
                    if (d > reach * reach) continue;
                    if (flocks.IsSeabird(i)) { if (d < dSea) { dSea = d; bestSea = i; } }
                    else if (d < dLand) { dLand = d; bestLand = i; }
                }
                if (bestLand >= 0) AddFlock(flocks, bestLand, false);
                if (bestSea >= 0) AddFlock(flocks, bestSea, true);
            }

            if (island.TryGetComponent(out IslandSettlementSystem settlement) && settlement.isActiveAndEnabled)
            {
                AddLandmark(settlement, BuildingKind.Lighthouse, SightKind.Lighthouse);
                AddLandmark(settlement, BuildingKind.Dock, SightKind.Harbour);
                for (int v = 0; v < settlement.VillageCount; v++)
                {
                    var stage = settlement.VillageStage(v);
                    if (stage == SettlementStage.None) continue;
                    int fire = WatchSubjects.CampfireOf(settlement, v);
                    if (fire < 0) continue;
                    _list.Add(new Sight
                    {
                        kind = SightKind.Village, index = fire, village = v, name = VillageName(stage),
                        world = WatchSubjects.CampfireWorld(settlement, fire),
                    });
                }
            }

            if (_summitIsland != island || _summitVersion != island.Version) FindSummit(island);
            if (HasSummit)
                _list.Add(new Sight
                {
                    kind = SightKind.Summit, index = -1, name = SummitName,
                    world = island.transform.TransformPoint(_summitLocal.x, _summitHeight, _summitLocal.y),
                });

            SortRing(centre);
            if (had) _index = Find(current);
        }

        // For tests and hand-made tours: add stops, then Arrange puts them in their ring (the current one kept).
        public void Add(Sight s) => _list.Add(s);

        public void Arrange(Vector2 centre)
        {
            bool had = _index >= 0 && _index < _list.Count;
            Sight current = had ? _list[_index] : default;
            SortRing(centre);
            if (had) _index = Find(current);
        }

        void SortRing(Vector2 centre)
        {
            for (int i = 0; i < _list.Count; i++)
            {
                var s = _list[i];
                s.angle = AngleAround(centre, s.world);
                _list[i] = s;
            }
            _list.Sort(ByAngle.Instance);
        }

        void AddFlock(FlockSystem flocks, int i, bool seabird)
        {
            Vector2 p = flocks.PositionOf(i);
            _list.Add(new Sight
            {
                kind = SightKind.Flock, index = i, seabird = seabird, name = LifeNames.Plural(seabird ? LifeKind.Seabird : LifeKind.Bird),
                world = new Vector3(p.x, flocks.HeightOf(i), p.y),
            });
        }

        void AddLandmark(IslandSettlementSystem s, BuildingKind building, SightKind kind)
        {
            int i = WatchSubjects.LandmarkIndex(s, building);
            if (i < 0) return;
            _list.Add(new Sight
            {
                kind = kind, index = i, name = WatchSubjects.LandmarkName(building),
                world = WatchSubjects.LandmarkWorld(s, i, out _),
            });
        }

        // The highest point of the heightfield on a coarse grid; only when the island's shape changed.
        void FindSummit(Island island)
        {
            _summitIsland = island;
            _summitVersion = island.Version;
            _summitHeight = float.MinValue;
            Rect b = island.LocalBounds;
            for (int j = 0; j <= SummitGrid; j++)
            {
                for (int i = 0; i <= SummitGrid; i++)
                {
                    var p = new Vector2(Mathf.Lerp(b.xMin, b.xMax, i / (float)SummitGrid), Mathf.Lerp(b.yMin, b.yMax, j / (float)SummitGrid));
                    float h = island.SampleHeight(p);
                    if (h <= _summitHeight) continue;
                    _summitHeight = h;
                    _summitLocal = p;
                }
            }
        }

        // The same stop in a rebuilt list: a herd by its species, a flock by its kind, a village by its village (or
        // the nearest campfire), the others by kind.
        public int Find(in Sight key)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < _list.Count; i++)
            {
                var s = _list[i];
                if (s.kind != key.kind) continue;
                switch (s.kind)
                {
                    case SightKind.Herd: if (s.species == key.species) return i; break;
                    case SightKind.Flock: if (s.seabird == key.seabird) return i; break;
                    case SightKind.Village:
                        if (s.village == key.village || s.index == key.index) return i;
                        float d = (s.world - key.world).sqrMagnitude;
                        if (d < bestD && d < 25f) { bestD = d; best = i; }
                        break;
                    default: return i;
                }
            }
            return best;
        }

        // Steps round the ring. Without a current stop it starts from where the view looks: the next stop clockwise
        // (or anticlockwise) from the focus.
        public int Step(int dir, Vector2 centre, Vector3 focus)
        {
            int n = _list.Count;
            if (n == 0) { _index = -1; return -1; }
            if (_index >= 0 && _index < n)
            {
                _index = ((_index + (dir >= 0 ? 1 : -1)) % n + n) % n;
                return _index;
            }
            float a = AngleAround(centre, focus);
            if (dir >= 0)
            {
                _index = 0;
                for (int i = 0; i < n; i++) if (_list[i].angle > a) { _index = i; break; }
            }
            else
            {
                _index = n - 1;
                for (int i = n - 1; i >= 0; i--) if (_list[i].angle < a) { _index = i; break; }
            }
            return _index;
        }

        // The stop nearest the view's centre (on the map, heights ignored), -1 = none.
        public int Nearest(Vector3 focus)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < _list.Count; i++)
            {
                Vector3 w = _list[i].world;
                float dx = w.x - focus.x, dz = w.z - focus.z, d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        // Where a tap on the bar flies: to the stop it names (back to it after a drag), without one to the stop
        // nearest the view's centre.
        public int TapIndex(Vector3 focus) => _index >= 0 && _index < _list.Count ? _index : Nearest(focus);

        // "Zebras (3/12)".
        public string Label(int i) => i >= 0 && i < _list.Count ? $"{_list[i].name} ({i + 1}/{_list.Count})" : "";

        public const string TapHint = "Tippen: hinfliegen";

        // "13 Ziele · Tippen: hinfliegen": the bar with no stop shown says that it can be tapped.
        public static string IdleLabel(int count) =>
            count == 1 ? $"1 Ziel · {TapHint}" : count > 0 ? $"{count} Ziele · {TapHint}" : "Nichts in Sicht";

        // What the bar shows: the current stop, else what the camera was sent to by a tap on the map, else the count.
        public string BarLabel(string subjectName) =>
            _index >= 0 && _index < _list.Count ? Label(_index) : !string.IsNullOrEmpty(subjectName) ? subjectName : IdleLabel(_list.Count);

        public static float AngleAround(Vector2 centre, Vector3 world) =>
            Mathf.Repeat(Mathf.Atan2(world.x - centre.x, world.z - centre.y) * Mathf.Rad2Deg, 360f);

        // The watch subject of a stop: what the camera flies to and follows.
        public WatchSubject SubjectOf(int i, Island island, FlockSystem flocks)
        {
            if (i < 0 || i >= _list.Count || island == null) return null;
            var s = _list[i];
            switch (s.kind)
            {
                case SightKind.Herd:
                {
                    if (!island.TryGetComponent(out IslandHerdSystem herds)) return null;
                    int h = s.index;
                    if (h < 0 || h >= herds.HerdCount || herds.HerdKind(h) != s.species || herds.HerdSize(h) == 0)
                    {
                        h = -1;
                        for (int k = 0; k < herds.HerdCount; k++)
                            if (herds.HerdKind(k) == s.species && herds.HerdSize(k) > 0 && (h < 0 || herds.HerdSize(k) > herds.HerdSize(h))) h = k;
                    }
                    var subject = WatchSubjects.OfHerd(island, herds, h);
                    if (subject != null) subject.label = s.name;
                    return subject;
                }
                case SightKind.Flock:
                {
                    if (flocks == null || s.index >= flocks.FlockCount || flocks.IsSeabird(s.index) != s.seabird) return null;
                    var subject = WatchSubjects.OfFlock(flocks, s.index);
                    if (subject != null) subject.label = s.name;
                    return subject;
                }
                case SightKind.Lighthouse:
                    return WatchSubjects.OfLandmark(island.GetComponent<IslandSettlementSystem>(), BuildingKind.Lighthouse);
                case SightKind.Harbour:
                    return WatchSubjects.OfLandmark(island.GetComponent<IslandSettlementSystem>(), BuildingKind.Dock);
                case SightKind.Village:
                    return WatchSubjects.OfCampfire(island.GetComponent<IslandSettlementSystem>(), s.index);
                default:
                    return SummitSubject(island, _summitLocal);
            }
        }

        public static WatchSubject SummitSubject(Island island, Vector2 local)
        {
            if (island == null) return null;
            var ground = island;
            return new WatchSubject
            {
                label = SummitName,
                ground = island,
                still = true,
                radius = 5f,
                pitch = 35f,
                focus = (out Vector3 f) =>
                {
                    f = default;
                    if (ground == null || !ground.isActiveAndEnabled || ground.IsSunk) return false;
                    f = ground.transform.TransformPoint(local.x, Mathf.Max(0f, ground.SampleHeight(local)), local.y);
                    return true;
                },
            };
        }

        static string[] _villageNames;

        static string VillageName(SettlementStage stage)
        {
            if (_villageNames == null)
            {
                var values = (SettlementStage[])System.Enum.GetValues(typeof(SettlementStage));
                int max = 0;
                foreach (var v in values) max = Mathf.Max(max, (int)v);
                _villageNames = new string[max + 1];
                foreach (var v in values) _villageNames[(int)v] = WatchSubjects.SettlementLabel(v);
            }
            int i = (int)stage;
            return i >= 0 && i < _villageNames.Length ? _villageNames[i] : WatchSubjects.SettlementLabel(stage);
        }

        sealed class ByAngle : IComparer<Sight>
        {
            public static readonly ByAngle Instance = new ByAngle();
            public int Compare(Sight x, Sight y) => x.angle.CompareTo(y.angle);
        }
    }
}
