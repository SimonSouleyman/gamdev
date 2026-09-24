using System;
using System.Collections.Generic;
using Drift.Core;
using Drift.Islands;
using Drift.Life;
using Drift.SaveSystem;
using Drift.UI;
using Drift.Visuals;
using UnityEngine;
using UnityEngine.UI;

namespace Drift.Bridge
{
    public enum TapTargetKind { Animal, Critter, Seal, Flock, SeaGroup, Landmark, Campfire }

    [Flags]
    public enum TapGather
    {
        None = 0,
        Animals = 1,
        Critters = 2,
        Seals = 4,
        Flocks = 8,
        Sea = 16,
        Landmarks = 32,
        // The campfire in the middle of every village: a tap watches the settlement.
        Campfires = 64,
        // What WatchTools.Tap looks for once no animal, critter or seal is under the finger.
        Extras = Flocks | Sea | Landmarks | Campfires,
        All = Animals | Critters | Seals | Flocks | Sea | Landmarks | Campfires,
    }

    // One thing a tap can pick: enough to find it again next frame (most of them move) and to key a cooldown.
    public struct TapTarget
    {
        public TapTargetKind kind;
        public Island island;
        public Component system;
        // Animal: herd, member. Critter / seal / flock / sea group: slot. Landmark: building index. Campfire: building
        // index, village index.
        public int a, b;
        // The point a tap is measured against (an animal at body height, a lighthouse at half its height).
        public Vector3 world;
        // World height of the subject; the sparkle sits on top of it.
        public float size;

        // The same subject keeps its key while it moves (a herd, not one of its animals).
        public int Key => ((system != null ? system.GetHashCode() : 0) * 397) ^ ((int)kind * 7919 + a * 31);
    }

    // What can be tapped in the world, shared by WatchTools.Tap and the sparkle so both always mean the same things:
    // herd animals and visible critters on islands in reach whose life is not in the Far tier, seals that are out,
    // flocks, the sea animals one can see (dolphins, turtles, rays, whales at the surface) and the landmarks
    // (lighthouse, harbour) and village campfires of the islands in reach.
    public static class TapTargets
    {
        public static bool InReach(Island island, Vector2 camXZ, Vector2 focusXZ, float range)
        {
            float reach = range + island.BoundingRadius;
            Vector2 ip = island.PlanarPosition;
            return (ip - camXZ).sqrMagnitude <= reach * reach || (ip - focusXZ).sqrMagnitude <= reach * reach;
        }

        static bool PointInReach(Vector2 p, Vector2 camXZ, Vector2 focusXZ, float range) =>
            (p - camXZ).sqrMagnitude <= range * range || (p - focusXZ).sqrMagnitude <= range * range;

        public static bool CritterVisible(IslandCrittersSystem critters, int i) =>
            !critters.DyingOf(i) && critters.FadeOf(i) >= 0.5f && critters.StateOf(i) != CritterState.Hidden;

        public static Vector3 CritterWorld(Island island, IslandCrittersSystem critters, int i)
        {
            Vector2 p = critters.PositionOf(i);
            return island.transform.TransformPoint(p.x, Mathf.Max(0f, island.SampleHeight(p)), p.y);
        }

        // The tap point of one animal: its body, not its feet (young ones are smaller).
        public static Vector3 AnimalWorld(Island island, IslandHerdSystem herds, int herd, int member)
        {
            Vector3 local = herds.AnimalLocalPosition(herd, member);
            local.y += herds.BodyLength(herd) * 0.35f * herds.AnimalSizeFactor(herd, member);
            return island.transform.TransformPoint(local);
        }

        public static bool SealTappable(SeaLifeSystem sea, int i) => sea.SealActive(i) && sea.SealStateOf(i) != 0;

        public static Vector3 SealWorld(SeaLifeSystem sea, int i)
        {
            Vector2 p = sea.SealPosition(i);
            return new Vector3(p.x, 0.2f, p.y);
        }

        // Sea animals worth a tap: the ones one can actually see. A whale counts while it is up (state 0 = deep).
        public static bool SeaGroupTappable(SeaLifeSystem sea, int i)
        {
            if (!sea.GroupActive(i) || sea.GroupFade(i) < 0.5f || sea.GroupIsPickup(i)) return false;
            switch (sea.GroupKind(i))
            {
                case SeaLifeSystem.Kind.Dolphins:
                case SeaLifeSystem.Kind.Turtle:
                case SeaLifeSystem.Kind.Ray: return true;
                case SeaLifeSystem.Kind.Whale:
                case SeaLifeSystem.Kind.WhalePod: return sea.GroupState(i) != 0;
                default: return false;
            }
        }

        static float SeaSize(SeaLifeSystem.Kind kind) =>
            kind == SeaLifeSystem.Kind.Whale || kind == SeaLifeSystem.Kind.WhalePod ? 2.5f : kind == SeaLifeSystem.Kind.Dolphins ? 1f : 0.5f;

        // Adds everything of the asked kinds that could be tapped right now. A herd adds one animal
        // (member = memberPick modulo its size) instead of all of them. No allocation once `into` has grown.
        public static void Gather(List<TapTarget> into, Vector2 camXZ, Vector2 focusXZ, float range, FlockSystem flocks,
                                  SeaLifeSystem sea, TapGather what, int memberPick = 0)
        {
            bool life = (what & (TapGather.Animals | TapGather.Critters | TapGather.Landmarks | TapGather.Campfires)) != 0;
            var all = Island.All;
            for (int k = 0; life && k < all.Count; k++)
            {
                var island = all[k];
                if (island == null || !island.isActiveAndEnabled || island.IsSunk || !InReach(island, camXZ, focusXZ, range)) continue;
                if ((what & TapGather.Animals) != 0 && island.TryGetComponent(out IslandHerdSystem herds) && herds.isActiveAndEnabled && herds.Tier != LifeTier.Far)
                {
                    for (int h = 0; h < herds.HerdCount; h++)
                    {
                        int n = herds.HerdSize(h);
                        if (n == 0) continue;
                        int m = (int)((uint)(memberPick + h * 7) % (uint)n);
                        into.Add(new TapTarget
                        {
                            kind = TapTargetKind.Animal, island = island, system = herds, a = h, b = m,
                            world = AnimalWorld(island, herds, h, m), size = herds.BodyLength(h) * 0.6f,
                        });
                    }
                }
                if ((what & TapGather.Critters) != 0 && island.TryGetComponent(out IslandCrittersSystem critters) && critters.isActiveAndEnabled && critters.Tier != LifeTier.Far)
                {
                    for (int i = 0; i < critters.CritterCount; i++)
                    {
                        if (!CritterVisible(critters, i)) continue;
                        into.Add(new TapTarget
                        {
                            kind = TapTargetKind.Critter, island = island, system = critters, a = i,
                            world = CritterWorld(island, critters, i), size = 0.2f,
                        });
                    }
                }
                if ((what & (TapGather.Landmarks | TapGather.Campfires)) != 0 && island.TryGetComponent(out IslandSettlementSystem settlement) && settlement.isActiveAndEnabled)
                {
                    for (int i = 0; i < settlement.BuildingCount; i++)
                    {
                        var kind = settlement.BuildingKindOf(i);
                        if (kind == BuildingKind.Campfire)
                        {
                            if ((what & TapGather.Campfires) == 0 || !WatchSubjects.CampfireStands(settlement, i)) continue;
                            into.Add(new TapTarget
                            {
                                kind = TapTargetKind.Campfire, island = island, system = settlement, a = i, b = settlement.BuildingVillageOf(i),
                                world = WatchSubjects.CampfireWorld(settlement, i), size = WatchSubjects.CampfireSparkleSize,
                            });
                            continue;
                        }
                        if ((what & TapGather.Landmarks) == 0) continue;
                        if (!WatchSubjects.IsLandmark(kind) || WatchSubjects.LandmarkIndex(settlement, kind) != i) continue;
                        Vector3 foot = WatchSubjects.LandmarkWorld(settlement, i, out float height);
                        into.Add(new TapTarget
                        {
                            kind = TapTargetKind.Landmark, island = island, system = settlement, a = i, b = (int)kind,
                            world = foot + Vector3.up * (height * 0.5f), size = height,
                        });
                    }
                }
            }
            if ((what & TapGather.Flocks) != 0 && flocks != null && flocks.isActiveAndEnabled)
            {
                for (int i = 0; i < flocks.FlockCount; i++)
                {
                    if (flocks.BirdCountOf(i) == 0) continue;
                    Vector2 p = flocks.PositionOf(i);
                    if (!PointInReach(p, camXZ, focusXZ, range)) continue;
                    into.Add(new TapTarget { kind = TapTargetKind.Flock, system = flocks, a = i, world = new Vector3(p.x, flocks.HeightOf(i), p.y), size = 1f });
                }
            }
            if (sea == null || !sea.isActiveAndEnabled) return;
            if ((what & TapGather.Seals) != 0)
            {
                for (int i = 0; i < sea.SealSlots; i++)
                {
                    if (!SealTappable(sea, i)) continue;
                    Vector3 w = SealWorld(sea, i);
                    if (!PointInReach(new Vector2(w.x, w.z), camXZ, focusXZ, range)) continue;
                    into.Add(new TapTarget { kind = TapTargetKind.Seal, system = sea, a = i, world = w, size = 0.5f });
                }
            }
            if ((what & TapGather.Sea) != 0)
            {
                for (int i = 0; i < sea.GroupSlots; i++)
                {
                    if (!SeaGroupTappable(sea, i)) continue;
                    Vector2 p = sea.GroupPosition(i);
                    if (!PointInReach(p, camXZ, focusXZ, range)) continue;
                    into.Add(new TapTarget { kind = TapTargetKind.SeaGroup, system = sea, a = i, b = (int)sea.GroupKind(i), world = new Vector3(p.x, 0.2f, p.y), size = SeaSize(sea.GroupKind(i)) });
                }
            }
        }

        // Where the target is now; false once it is gone (the herd died out, the critter hid, the whale dived).
        public static bool Refresh(ref TapTarget t)
        {
            switch (t.kind)
            {
                case TapTargetKind.Animal:
                {
                    var herds = t.system as IslandHerdSystem;
                    if (herds == null || t.island == null || !herds.isActiveAndEnabled || t.a >= herds.HerdCount || t.b >= herds.HerdSize(t.a)) return false;
                    t.world = AnimalWorld(t.island, herds, t.a, t.b);
                    return true;
                }
                case TapTargetKind.Critter:
                {
                    var critters = t.system as IslandCrittersSystem;
                    if (critters == null || t.island == null || !critters.isActiveAndEnabled || t.a >= critters.CritterCount || !CritterVisible(critters, t.a)) return false;
                    t.world = CritterWorld(t.island, critters, t.a);
                    return true;
                }
                case TapTargetKind.Seal:
                {
                    var sea = t.system as SeaLifeSystem;
                    if (sea == null || !SealTappable(sea, t.a)) return false;
                    t.world = SealWorld(sea, t.a);
                    return true;
                }
                case TapTargetKind.Flock:
                {
                    var flocks = t.system as FlockSystem;
                    if (flocks == null || t.a >= flocks.FlockCount || flocks.BirdCountOf(t.a) == 0) return false;
                    Vector2 p = flocks.PositionOf(t.a);
                    t.world = new Vector3(p.x, flocks.HeightOf(t.a), p.y);
                    return true;
                }
                case TapTargetKind.SeaGroup:
                {
                    var sea = t.system as SeaLifeSystem;
                    if (sea == null || t.a >= sea.GroupSlots || !SeaGroupTappable(sea, t.a) || (int)sea.GroupKind(t.a) != t.b) return false;
                    Vector2 p = sea.GroupPosition(t.a);
                    t.world = new Vector3(p.x, 0.2f, p.y);
                    return true;
                }
                case TapTargetKind.Campfire:
                {
                    var settlement = t.system as IslandSettlementSystem;
                    if (settlement == null || !settlement.isActiveAndEnabled) return false;
                    int i = WatchSubjects.CampfireOf(settlement, t.b, t.a);
                    if (i < 0) return false;
                    t.a = i;
                    t.world = WatchSubjects.CampfireWorld(settlement, i);
                    t.size = WatchSubjects.CampfireSparkleSize;
                    return true;
                }
                default:
                {
                    var settlement = t.system as IslandSettlementSystem;
                    if (settlement == null || !settlement.isActiveAndEnabled) return false;
                    int i = WatchSubjects.LandmarkIndex(settlement, (BuildingKind)t.b);
                    if (i < 0) return false;
                    t.a = i;
                    t.world = WatchSubjects.LandmarkWorld(settlement, i, out float height) + Vector3.up * (height * 0.5f);
                    t.size = height;
                    return true;
                }
            }
        }

        // The watch subject a tap on the target starts (herds and critters normally go through their card first).
        public static WatchSubject SubjectOf(in TapTarget t)
        {
            switch (t.kind)
            {
                case TapTargetKind.Animal: return WatchSubjects.OfHerd(t.island, t.system as IslandHerdSystem, t.a);
                case TapTargetKind.Critter:
                {
                    var critters = t.system as IslandCrittersSystem;
                    if (critters == null || t.a >= critters.CritterCount) return null;
                    int entry = CollectionCatalog.IndexOf(critters.KindOf(t.a));
                    return entry < 0 ? null : WatchSubjects.OfCritter(CollectionCatalog.At(entry), t.island, critters, t.a);
                }
                case TapTargetKind.Seal: return WatchSubjects.OfSeal(t.system as SeaLifeSystem, t.a);
                case TapTargetKind.Flock: return WatchSubjects.OfFlock(t.system as FlockSystem, t.a);
                case TapTargetKind.SeaGroup: return WatchSubjects.OfSeaGroup(t.system as SeaLifeSystem, t.a);
                case TapTargetKind.Campfire: return WatchSubjects.OfCampfire(t.system as IslandSettlementSystem, t.a);
                default: return WatchSubjects.OfLandmark(t.system as IslandSettlementSystem, (BuildingKind)t.b);
            }
        }
    }

    // Pure timing of the sparkle: when the next one is due and which subjects are still cooling down. No
    // allocation after construction.
    public sealed class SparkleSchedule
    {
        readonly int[] _keys;
        readonly float[] _at;
        int _cursor;
        System.Random _rnd;
        float _clock, _due;

        public float interval = 3.5f;
        public float jitter = 0.35f;
        public float cooldown = 12f;
        // After a round without anything to sparkle, look again this soon.
        public float retry = 1f;

        public float Clock => _clock;
        public float Due => _due;

        public SparkleSchedule(int seed, int memory = 32)
        {
            _keys = new int[Mathf.Max(1, memory)];
            _at = new float[_keys.Length];
            Reset(seed);
        }

        public void Reset(int seed)
        {
            _rnd = new System.Random(seed);
            _clock = 0f;
            _cursor = 0;
            for (int i = 0; i < _at.Length; i++) _at[i] = float.NegativeInfinity;
            _due = NextGap() * 0.5f;
        }

        float NextGap()
        {
            float j = Mathf.Clamp01(jitter);
            return Mathf.Max(0.2f, interval * (1f + j * (2f * (float)_rnd.NextDouble() - 1f)));
        }

        // Advances the clock; true when a sparkle should be started now.
        public bool Tick(float dt)
        {
            _clock += Mathf.Max(0f, dt);
            return _clock >= _due;
        }

        public bool Cooling(int key)
        {
            for (int i = 0; i < _keys.Length; i++)
                if (_keys[i] == key && _clock - _at[i] < cooldown) return true;
            return false;
        }

        // A sparkle was started on `key`: the next one is due after a jittered interval.
        public void Started(int key)
        {
            _keys[_cursor] = key;
            _at[_cursor] = _clock;
            _cursor = (_cursor + 1) % _keys.Length;
            _due = _clock + NextGap();
        }

        // Nothing was there to sparkle.
        public void Missed() => _due = _clock + Mathf.Max(0.1f, retry);

        public int Range(int count) => count <= 1 ? 0 : _rnd.Next(count);
    }

    // "Die klickbaren Dinge sollen ab und zu funkeln": every few seconds one random tappable subject in view (an
    // animal, a critter, a flock, a seal or dolphin, the lighthouse or harbour, a village campfire) twinkles with a few stars for under
    // a second, and the same subject waits subjectCooldown before its next turn. Drawn in screen space on its own
    // overlay canvas under every other game canvas, so it keeps the same readable size at every camera distance;
    // 12 pooled images with one sprite (one batch), the canvas is off while nothing twinkles, and nothing is
    // allocated per frame. Off on the title, in the pause menu, photo mode, the journal, the album and the finale.
    // The same stars also twinkle on request anywhere (SparkleAt, every mode - the cozy tap sparkles stay cozy-only)
    // and all over the player's island while the adventure whale boost runs (owner, v0.6.5: "die Insel soll während
    // des Walboosts funkeln"). Stars are placed in LateUpdate after the chase camera moved: at race pace a star placed
    // with the last frame's camera sat a unit ahead of the island.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(900)]
    public class TapSparkles : MonoBehaviour
    {
        const string CanvasName = "TapSparkleCanvas";
        const int MaxBursts = 3, StarsPerBurst = 4, MaxFree = 8;

        public WatchTools watch;
        public GameSession session;
        public PangaeaFinale finale;
        public int sortingOrder = 1;
        public int seed = 71;

        [Header("Funkeln")]
        [Tooltip("Im Mittel alle so viele Sekunden funkelt ein antippbares Ding im Bild (Tier, Vogelschwarm, Meeresbesucher, Leuchtturm, Hafen, Lagerfeuer).")]
        [Range(1f, 20f)] public float interval = 3.5f;
        [Tooltip("Zufällige Abweichung vom Takt (Anteil des Takts).")]
        [Range(0f, 0.9f)] public float intervalJitter = 0.35f;
        [Tooltip("Frühestens nach so vielen Sekunden funkelt dasselbe Ding wieder.")]
        [Range(3f, 60f)] public float subjectCooldown = 12f;
        [Tooltip("Dauer eines Funkelns (Sekunden).")]
        [Range(0.3f, 2f)] public float sparkleSeconds = 0.9f;
        [Tooltip("Größe des großen Sterns in Canvas-Einheiten (1080er Raster) - auf jeder Zoomstufe gleich groß.")]
        [Range(24f, 200f)] public float starSize = 110f;
        [Tooltip("Rand (Anteil des Bildes), in dem nichts funkelt.")]
        [Range(0f, 0.3f)] public float screenMargin = 0.07f;
        [Tooltip("Oberer Bereich (Anteil des Bildes) unter HUD und Meldungen, in dem nichts funkelt.")]
        [Range(0f, 0.5f)] public float topMargin = 0.18f;
        [Tooltip("Auch das gerade beobachtete Tier funkelt.")]
        public bool sparkleWatched;
        public bool debugLog;

        [Header("Wal-Schub (Abenteuer)")]
        [Tooltip("Solange der Wal-Schub läuft, funkelt die Insel: alle so viele Sekunden ein neues Funkeln an einer zufälligen Stelle auf ihr oder an ihrer Küste.")]
        [Range(0.05f, 1f)] public float whaleSparkleEvery = 0.14f;
        [Tooltip("Größe eines Insel-Funkelns (Anteil der Sterngröße oben).")]
        [Range(0.2f, 1.5f)] public float whaleSparkleScale = 0.85f;
        [Tooltip("Dauer eines Insel-Funkelns (Sekunden).")]
        [Range(0.2f, 2f)] public float whaleSparkleSeconds = 0.6f;

        struct Burst
        {
            public bool active;
            public TapTarget target;
            public float age;
            public float mirror;
            public float spin;
        }

        // Offsets (in star sizes), relative size, start and length (fractions of sparkleSeconds) of the four stars.
        static readonly Vector2[] StarOffset = { Vector2.zero, new Vector2(0.62f, 0.44f), new Vector2(-0.58f, 0.16f), new Vector2(0.18f, -0.52f) };
        static readonly float[] StarScale = { 1f, 0.5f, 0.42f, 0.36f };
        static readonly float[] StarStart = { 0f, 0.12f, 0.28f, 0.42f };
        static readonly float[] StarLength = { 0.78f, 0.55f, 0.5f, 0.5f };
        static readonly Color MainTint = new Color(1f, 1f, 1f, 1f);
        static readonly Color SmallTint = new Color(1f, 0.93f, 0.72f, 1f);
        static Sprite s_star;

        // A twinkle on request: at a world point, or pinned to a transform (local point) so it rides along with it.
        struct FreeBurst
        {
            public bool active, anchored;
            public Transform anchor;
            public Vector3 point;
            public float age, life, scale, mirror, spin;
        }

        Canvas _canvas;
        RectTransform _root;
        readonly Image[] _stars = new Image[(MaxBursts + MaxFree) * StarsPerBurst];
        readonly RectTransform[] _starRects = new RectTransform[(MaxBursts + MaxFree) * StarsPerBurst];
        readonly Burst[] _bursts = new Burst[MaxBursts];
        readonly FreeBurst[] _free = new FreeBurst[MaxFree];
        readonly System.Random _freeRnd = new System.Random(97);
        float _whaleSparkleTimer;
        bool _cozyOn, _freeOn;
        readonly List<TapTarget> _candidates = new(256);
        readonly List<int> _eligible = new(256);
        readonly int[] _kindCount = new int[(int)TapTargetKind.Campfire + 1];
        SparkleSchedule _schedule;
        float _lookup;

        public int Started { get; private set; }
        public int ActiveBursts
        {
            get
            {
                int n = 0;
                for (int i = 0; i < MaxBursts; i++) if (_bursts[i].active) n++;
                return n;
            }
        }
        public TapTarget LastTarget { get; private set; }
        public Vector2 LastScreen { get; private set; }
        public int LastCandidates { get; private set; }
        public int FreeStarted { get; private set; }
        public int ActiveFreeBursts
        {
            get
            {
                int n = 0;
                for (int i = 0; i < MaxFree; i++) if (_free[i].active) n++;
                return n;
            }
        }
        // Stars on screen right now (tap sparkles and free ones).
        public int VisibleStars
        {
            get
            {
                int n = 0;
                if (_canvas != null && _canvas.enabled)
                    for (int i = 0; i < _stars.Length; i++) if (_stars[i] != null && _stars[i].enabled) n++;
                return n;
            }
        }
        public static TapSparkles Active { get; private set; }

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            Build();
            _schedule = new SparkleSchedule(seed);
            Active = this;
        }

        void OnDisable()
        {
            if (_canvas != null) _canvas.enabled = false;
            for (int i = 0; i < MaxBursts; i++) _bursts[i].active = false;
            for (int i = 0; i < MaxFree; i++) _free[i].active = false;
            if (Active == this) Active = null;
        }

        void Build()
        {
            UiStyle.DestroyChildrenNamed(transform, CanvasName);
            _canvas = UiStyle.Canvas(transform, CanvasName, sortingOrder, false, out _root);
            var sprite = StarSprite;
            for (int i = 0; i < _stars.Length; i++)
            {
                var img = UiStyle.Shape(_root, "Star", sprite, Color.white);
                img.raycastTarget = false;
                _starRects[i] = img.rectTransform.Center(Vector2.zero, new Vector2(100f, 100f));
                img.enabled = false;
                _stars[i] = img;
            }
            _canvas.enabled = false;
        }

        void Resolve()
        {
            if (watch != null && session != null) return;
            _lookup -= Time.unscaledDeltaTime;
            if (_lookup > 0f) return;
            _lookup = 1f;
            if (watch == null && !TryGetComponent(out watch)) watch = FindAnyObjectByType<WatchTools>();
            if (session == null) session = FindAnyObjectByType<GameSession>();
            if (finale == null) finale = FindAnyObjectByType<PangaeaFinale>();
        }

        bool Allowed =>
            session != null && session.Current == GameSession.State.Playing && WatchRules.Allowed(WatchFeature.Watch)
            && watch != null && watch.isActiveAndEnabled && !watch.PhotoActive && !watch.JournalOpen && !watch.AlbumOpen
            && !watch.Capturing && !WatchTools.HudHidden && (finale == null || !finale.Active);

        // Free twinkles show in every mode while a run is on screen (not on the title, paused, hidden HUD, finale).
        bool FreeAllowed =>
            session != null && session.Current == GameSession.State.Playing && !WatchTools.HudHidden
            && (finale == null || !finale.Active) && (watch == null || (!watch.PhotoActive && !watch.Capturing));

        void Update()
        {
            if (!Application.isPlaying || _canvas == null) return;
            Resolve();
            var cam = Camera.main;
            float dt = Time.unscaledDeltaTime;
            _cozyOn = Allowed && cam != null;
            if (_cozyOn)
            {
                _schedule.interval = interval;
                _schedule.jitter = intervalJitter;
                _schedule.cooldown = subjectCooldown;
                if (_schedule.Tick(dt) && !TryStart(cam)) _schedule.Missed();
            }
            else StopCozy();
            _freeOn = FreeAllowed && cam != null;
            if (_freeOn) EmitWhaleSparkles(dt);
            else StopFree();
        }

        void LateUpdate()
        {
            if (!Application.isPlaying || _canvas == null) return;
            var cam = Camera.main;
            bool any = false;
            if (cam != null)
            {
                float dt = Time.unscaledDeltaTime;
                if (_cozyOn) any |= StepBursts(cam, dt);
                if (_freeOn) any |= StepFree(cam, dt);
            }
            if (_canvas.enabled != any) _canvas.enabled = any;
        }

        void StopCozy()
        {
            for (int i = 0; i < MaxBursts; i++) _bursts[i].active = false;
            for (int i = 0; i < MaxBursts * StarsPerBurst; i++) if (_stars[i].enabled) _stars[i].enabled = false;
        }

        void StopFree()
        {
            _whaleSparkleTimer = 0f;
            for (int i = 0; i < MaxFree; i++) _free[i].active = false;
            for (int i = MaxBursts * StarsPerBurst; i < _stars.Length; i++) if (_stars[i].enabled) _stars[i].enabled = false;
        }

        // A one-off twinkle of the same stars at a world point - pinned to `anchor` when given, so it rides along with
        // a moving island. `scale` 1 = the tap sparkle's size, `seconds` <= 0 = sparkleSeconds. Works in every mode
        // while a run is on screen; false when all free twinkles are busy.
        public bool SparkleAt(Vector3 world, float scale = 1f, Transform anchor = null, float seconds = 0f)
        {
            if (!Application.isPlaying || _canvas == null) return false;
            int slot = -1;
            for (int i = 0; i < MaxFree; i++) if (!_free[i].active) { slot = i; break; }
            if (slot < 0) return false;
            _free[slot] = new FreeBurst
            {
                active = true, anchored = anchor != null, anchor = anchor,
                point = anchor != null ? anchor.InverseTransformPoint(world) : world,
                age = 0f, life = seconds > 0f ? seconds : sparkleSeconds, scale = Mathf.Max(0.05f, scale),
                mirror = _freeRnd.Next(2) == 0 ? 1f : -1f, spin = _freeRnd.Next(90) - 45f,
            };
            FreeStarted++;
            return true;
        }

        // The whale boost (the island's ghost ride) makes the whole island twinkle: a new twinkle every
        // whaleSparkleEvery seconds on its top or right on its coast, riding along with it.
        void EmitWhaleSparkles(float dt)
        {
            var player = session != null ? session.player : null;
            if (player == null || !GameModes.IsAdventure || !player.Ghosting || player.IsSunk)
            {
                _whaleSparkleTimer = 0f;
                return;
            }
            _whaleSparkleTimer -= dt;
            if (_whaleSparkleTimer > 0f) return;
            _whaleSparkleTimer = Mathf.Max(0f, _whaleSparkleTimer + Mathf.Max(0.05f, whaleSparkleEvery));
            Vector3 local = IslandSparkleSpot(player);
            float scale = whaleSparkleScale * (0.75f + 0.5f * (float)_freeRnd.NextDouble());
            SparkleAt(player.transform.TransformPoint(local), scale, player.transform, whaleSparkleSeconds);
        }

        // A random spot on the island (local): every other one on its coast, the rest anywhere on the land.
        Vector3 IslandSparkleSpot(Island island)
        {
            float r = Mathf.Max(0.5f, island.BoundingRadius);
            float a = (float)_freeRnd.NextDouble() * Mathf.PI * 2f;
            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            if (_freeRnd.Next(2) == 0)
            {
                for (float d = r; d > 0.1f; d -= r * 0.1f)
                {
                    Vector2 p = dir * d;
                    if (island.SampleHeight(p) > 0.02f) return new Vector3(p.x, 0.25f, p.y);
                }
            }
            for (int k = 0; k < 4; k++)
            {
                Vector2 p = dir * (r * 0.85f * Mathf.Sqrt((float)_freeRnd.NextDouble()));
                float h = island.SampleHeight(p);
                if (h > 0.02f) return new Vector3(p.x, h + 0.35f, p.y);
                a = (float)_freeRnd.NextDouble() * Mathf.PI * 2f;
                dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }
            return new Vector3(0f, Mathf.Max(0f, island.SampleHeight(Vector2.zero)) + 0.35f, 0f);
        }

        bool StepFree(Camera cam, float dt)
        {
            bool any = false;
            for (int b = 0; b < MaxFree; b++)
            {
                ref FreeBurst burst = ref _free[b];
                int first = (MaxBursts + b) * StarsPerBurst;
                if (burst.active)
                {
                    burst.age += dt;
                    if (burst.age >= burst.life || (burst.anchored && burst.anchor == null)) burst.active = false;
                }
                Vector3 screen = default;
                if (burst.active)
                    screen = cam.WorldToScreenPoint(burst.anchored ? burst.anchor.TransformPoint(burst.point) : burst.point);
                if (!burst.active || screen.z <= 0f)
                {
                    HideStars(first);
                    continue;
                }
                any = true;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out Vector2 local);
                DrawStars(first, local, burst.age, burst.age / burst.life, starSize * burst.scale, burst.mirror, burst.spin);
            }
            return any;
        }

        void HideStars(int first)
        {
            for (int s = 0; s < StarsPerBurst; s++) if (_stars[first + s].enabled) _stars[first + s].enabled = false;
        }

        // The four stars of one twinkle around a canvas point, `u` 0..1 through it.
        void DrawStars(int first, Vector2 local, float age, float u, float size0, float mirror, float spin)
        {
            for (int s = 0; s < StarsPerBurst; s++)
            {
                var img = _stars[first + s];
                float k = StarEnvelope(u, StarStart[s], StarLength[s]);
                if (k <= 0.001f)
                {
                    if (img.enabled) img.enabled = false;
                    continue;
                }
                if (!img.enabled) img.enabled = true;
                float size = size0 * StarScale[s];
                // A quick flicker on top of the swell: a star twinkles, it does not just grow.
                float flicker = 0.88f + 0.12f * Mathf.Sin(age * 38f + s * 2.1f);
                var rt = _starRects[first + s];
                Vector2 off = StarOffset[s];
                off.x *= mirror;
                rt.anchoredPosition = local + off * size0;
                float scale = size / 100f * k * flicker;
                rt.localScale = new Vector3(scale, scale, 1f);
                rt.localRotation = Quaternion.Euler(0f, 0f, spin * 0.2f + 35f * (u - StarStart[s]) * mirror);
                var tint = s == 0 ? MainTint : SmallTint;
                tint.a = Mathf.Clamp01(k * 1.6f);
                img.color = tint;
            }
        }

        bool Busy(int key)
        {
            for (int i = 0; i < MaxBursts; i++) if (_bursts[i].active && _bursts[i].target.Key == key) return true;
            return false;
        }

        bool IsWatched(in TapTarget t)
        {
            var w = watch.Watched;
            if (w == null) return false;
            if (t.kind == TapTargetKind.Animal) return w.herds == t.system && w.herd == t.a;
            float r = Mathf.Max(1f, w.radius);
            return (t.world - watch.FollowFocus).sqrMagnitude < r * r;
        }

        // Starts a sparkle on one random tappable subject in view: first a kind at random among the kinds on screen
        // (so a meadow of butterflies does not drown the lighthouse), then one subject of that kind.
        public bool TryStart(Camera cam)
        {
            if (cam == null || watch == null) return false;
            int slot = -1;
            for (int i = 0; i < MaxBursts; i++) if (!_bursts[i].active) { slot = i; break; }
            if (slot < 0) return false;

            _candidates.Clear();
            Vector3 c = cam.transform.position;
            Vector2 camXZ = new Vector2(c.x, c.z);
            TapTargets.Gather(_candidates, camXZ, watch.TapFocusXZ, watch.tapRange, watch.flocks, watch.seaLife, TapGather.All, _schedule.Range(1 << 16));
            LastCandidates = _candidates.Count;

            _eligible.Clear();
            Array.Clear(_kindCount, 0, _kindCount.Length);
            float m = Mathf.Clamp(screenMargin, 0f, 0.45f), top = 1f - Mathf.Max(m, topMargin);
            for (int i = 0; i < _candidates.Count; i++)
            {
                var t = _candidates[i];
                Vector3 vp = cam.WorldToViewportPoint(t.world + Vector3.up * (t.size * 0.45f));
                if (vp.z <= 0f || vp.x < m || vp.x > 1f - m || vp.y < m || vp.y > top) continue;
                int key = t.Key;
                if (_schedule.Cooling(key) || Busy(key) || (!sparkleWatched && IsWatched(t))) continue;
                _eligible.Add(i);
                _kindCount[(int)t.kind]++;
            }
            if (_eligible.Count == 0) return false;

            int kinds = 0;
            for (int k = 0; k < _kindCount.Length; k++) if (_kindCount[k] > 0) kinds++;
            int pickKind = _schedule.Range(kinds), kind = -1;
            for (int k = 0; k < _kindCount.Length; k++)
            {
                if (_kindCount[k] == 0) continue;
                if (pickKind-- == 0) { kind = k; break; }
            }
            int pick = _schedule.Range(_kindCount[kind]), chosen = -1;
            for (int i = 0; i < _eligible.Count; i++)
            {
                if ((int)_candidates[_eligible[i]].kind != kind) continue;
                if (pick-- == 0) { chosen = _eligible[i]; break; }
            }
            if (chosen < 0) return false;

            var target = _candidates[chosen];
            _schedule.Started(target.Key);
            _bursts[slot] = new Burst
            {
                active = true, target = target, age = 0f,
                mirror = _schedule.Range(2) == 0 ? 1f : -1f,
                spin = _schedule.Range(90) - 45f,
            };
            Started++;
            LastTarget = target;
            if (debugLog) Debug.Log($"TapSparkles: {target.kind} ({_candidates.Count} tappable, {_eligible.Count} in view)");
            return true;
        }

        bool StepBursts(Camera cam, float dt)
        {
            bool any = false;
            float life = Mathf.Max(0.1f, sparkleSeconds);
            for (int b = 0; b < MaxBursts; b++)
            {
                ref Burst burst = ref _bursts[b];
                int first = b * StarsPerBurst;
                if (burst.active)
                {
                    burst.age += dt;
                    if (burst.age >= life || !TapTargets.Refresh(ref burst.target)) burst.active = false;
                }
                Vector3 screen = burst.active ? cam.WorldToScreenPoint(burst.target.world + Vector3.up * (burst.target.size * 0.45f)) : default;
                if (!burst.active || screen.z <= 0f)
                {
                    HideStars(first);
                    continue;
                }
                any = true;
                LastScreen = screen;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out Vector2 local);
                DrawStars(first, local, burst.age, burst.age / life, starSize, burst.mirror, burst.spin);
            }
            return any;
        }

        // 0..1..0 over one star's slice of the burst: a fast swell, a slower fade.
        public static float StarEnvelope(float u, float start, float length)
        {
            float x = (u - start) / Mathf.Max(0.01f, length);
            if (x <= 0f || x >= 1f) return 0f;
            return x < 0.3f ? Mathf.SmoothStep(0f, 1f, x / 0.3f) : 1f - Mathf.SmoothStep(0f, 1f, (x - 0.3f) / 0.7f);
        }

        // A four-point star with long thin rays, short diagonal glints, a white core and a warm golden glow, so it
        // reads on grass, sand and snow alike. Drawn once per domain.
        public static Sprite StarSprite
        {
            get
            {
                if (s_star != null) return s_star;
                const int size = 128;
                var px = new Color32[size * size];
                float c = (size - 1) * 0.5f;
                Color gold = new Color(1f, 0.74f, 0.22f);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float px01 = (x - c) / c, py01 = (y - c) / c;
                        float ax = Mathf.Abs(px01), ay = Mathf.Abs(py01);
                        float r = Mathf.Sqrt(px01 * px01 + py01 * py01);
                        // A filled star with concave sides (|x|^p + |y|^p)^(1/p) <= R, white inside and warm gold at
                        // its rim, plus a smaller diagonal star and a soft gold halo: bold enough to read on bright
                        // grass and sand, not only on the night sea.
                        float q = Mathf.Pow(Mathf.Pow(ax, 0.6f) + Mathf.Pow(ay, 0.6f), 1f / 0.6f);
                        float du = Mathf.Abs(px01 + py01) * 0.7071f, dv = Mathf.Abs(px01 - py01) * 0.7071f;
                        float qd = Mathf.Pow(Mathf.Pow(du, 0.6f) + Mathf.Pow(dv, 0.6f), 1f / 0.6f);
                        const float R = 0.94f, Rd = 0.48f;
                        float star = Mathf.Clamp01((R - q) / 0.035f);
                        float small = Mathf.Clamp01((Rd - qd) / 0.03f);
                        float body = Mathf.Max(star, small * 0.9f);
                        float halo = 0.75f * Mathf.Exp(-(r * r) / 0.06f) + 0.35f * Mathf.Exp(-Mathf.Max(0f, q - R) * 9f) * Mathf.Exp(-(r * r) / 0.5f);
                        float a = Mathf.Clamp01(body + Mathf.Clamp01(halo) * (1f - body));
                        float white = Mathf.Max(Mathf.Clamp01((R * 0.62f - q) / (R * 0.5f)), Mathf.Clamp01((Rd * 0.5f - qd) / (Rd * 0.5f)));
                        Color col = Color.Lerp(gold, Color.white, Mathf.Clamp01(white * 1.6f));
                        px[y * size + x] = new Color(col.r, col.g, col.b, a);
                    }
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "TapSparkleStar",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                tex.SetPixels32(px);
                tex.Apply(false, false);
                s_star = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                s_star.hideFlags = HideFlags.HideAndDontSave;
                return s_star;
            }
        }
    }
}
