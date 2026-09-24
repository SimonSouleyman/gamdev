using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace Drift.Islands
{
    [ExecuteAlways]
    public class WorldStreamer : MonoBehaviour
    {
        public Island player;
        public Material islandMaterial;
        public int seed = 777;
        [Tooltip("Kantenlänge eines Streaming-Abschnitts. Die Welt ist chunkSize · worldChunks breit und wiederholt sich dann (PlateSystem.gridPeriod · cellSize muss gleich groß sein).")]
        public float chunkSize = 76.5f;
        [Tooltip("Abschnitte pro Weltseite.")]
        public int worldChunks = 4;
        public int loadRadius = 1;
        public int unloadMargin = 1;
        // Bump when Plan() changes what it generates for a given seed: consumed slot keys and overrides of
        // an older layout no longer fit. Saves carry the layout they were made in; SaveManager continues every
        // layout this class can still build (IsKnownLayout) and rejects the rest.
        public const int WorldGenVersion = 4;
        // The first small world (2026-09-22): 4 x 90 u = 360 u, same 20 islands. A run started in it keeps its
        // size, because every planned position would move in the smaller one.
        public const int MediumWorldGenVersion = 3;
        const float MediumChunkSize = 90f;
        // The big world before 2026-09-22: 6 x 6 chunks of 110 u, planned chunk by chunk (~166 islands). Old saves
        // keep playing in it (UseLayout); new games get the small world.
        public const int LegacyWorldGenVersion = 2;
        const float LegacyChunkSize = 110f;
        const int LegacyWorldChunks = 6;

        public static bool IsKnownLayout(int version) =>
            version == WorldGenVersion || version == MediumWorldGenVersion || version == LegacyWorldGenVersion;

        [Header("Kleine Welt")]
        [Tooltip("Inseln in der ganzen Welt (ohne Vulkane): so viele vereint man zur Pangäa.")]
        [Range(4, 60)] public int worldIslands = 20;
        [Tooltip("Davon winzige Inselchen (Radius 1–2).")]
        [Range(0, 20)] public int worldIslets = 3;
        [Tooltip("Davon sehr große Inseln (Radius 9–13).")]
        [Range(0, 6)] public int worldLarge = 2;
        [Tooltip("Davon große Inseln (Radius 6–9).")]
        [Range(0, 12)] public int worldBig = 4;
        [Tooltip("Davon mittlere Inseln (Radius 3–6); der Rest ist klein (Radius 2–3).")]
        [Range(0, 30)] public int worldMedium = 6;
        [Tooltip("Flächenfaktor für die kleinen Inseln und Inselchen (v0.6.4: doppelte Mindestgröße, Radius × Wurzel daraus). Mittlere und große Inseln bleiben gleich.")]
        [Range(1f, 4f)] public float smallIslandAreaScale = 2f;
        [Tooltip("Mindestabstand der Inselmitten als Anteil eines gleichmäßigen Rasters (Weltbreite / Wurzel der Inselzahl): größer = gleichmäßiger verteilt und längere Fahrten zwischen den Inseln.")]
        [Range(0f, 1f)] public float worldSpacing = 0.7f;

        [Header("Alte große Welt (nur für alte Spielstände)")]
        // Per chunk: minPerChunk..maxPerChunk main islands (small 2-3, medium 3-6, big 6-9, a few large
        // 9-14) plus minIslets..maxIslets tiny ones (radius 1-2), some of them satellites of a main island.
        public int minPerChunk = 2;
        public int maxPerChunk = 4;
        public int minIslets = 1;
        public int maxIslets = 2;
        public float satelliteChance = 0.5f;
        public float largeShare = 0.05f;
        public float bigShare = 0.12f;
        public float mediumShare = 0.43f;
        [Header("Streaming")]
        public float startExclusion = 18f;
        public int spawnsPerFrame = 1;
        public float dirtyDistance = 2f;
        public float dirtyAreaFraction = 0.2f;
        public float dirtyHeight = 0.1f;

        public struct WorldSlot
        {
            public Vector2 pos;
            public float radius;
            public bool consumed;
            public IslandArchetype archetype;
            public float areaFactor;
            public float EstimatedArea => areaFactor * Mathf.PI * radius * radius;
        }

        class Slot
        {
            public Vector2 pos;
            public float radius;
            public IslandArchetype archetype;
            public int shapeSeed;
            public int index;
            public Island island;
            public bool spawned;
            public float baseArea;
            public float baseMaxHeight;
            public int baseVersion;
            // The heightfield, generated on a worker as soon as the chunk is planned; Spawn waits for it.
            public System.Threading.Tasks.Task<Island.PrebuiltShape> prebuild;
        }

        class Chunk
        {
            public Vector2Int coord;
            public readonly List<Slot> slots = new();
        }

        readonly Dictionary<Vector2Int, Chunk> _chunks = new();
        readonly HashSet<long> _consumed = new();
        readonly Dictionary<long, IslandSaveData> _overrides = new();
        readonly List<Vector2Int> _toRemove = new();
        readonly List<WorldSlot> _worldSlots = new();
        bool _worldSlotsDirty = true;
        Vector2 _startPos;
        bool _started;

        // The layout the current world is planned in (WorldGenVersion, or LegacyWorldGenVersion for an old save).
        public int Layout { get; private set; } = WorldGenVersion;
        public bool IsLegacyLayout => Layout == LegacyWorldGenVersion;
        public float ChunkSize => IsLegacyLayout ? LegacyChunkSize : Layout == MediumWorldGenVersion ? MediumChunkSize : chunkSize;
        public int WorldChunks => IsLegacyLayout ? LegacyWorldChunks : worldChunks;

        public float WorldSize => WorldChunks > 0 ? WorldChunks * ChunkSize : 0f;

        // A chunk and its copy one world width away must never be loaded at once (the same islands twice): the
        // loaded span, 2 * loadRadius + 1 plus the unload margin, has to fit into the world.
        int LoadRadius => WorldChunks > 0 ? Mathf.Clamp(loadRadius, 0, (WorldChunks - 1) / 2) : loadRadius;
        int UnloadMargin => WorldChunks > 0 ? Mathf.Clamp(unloadMargin, 0, WorldChunks - 2 * LoadRadius - 1) : unloadMargin;
        public int LoadedChunks => _chunks.Count;
        public Vector2 StartPosition => _startPos;
        public int OverrideCount => _overrides.Count;

        // Destroy is deferred, so during the merge frame an absorbed guest is still non-null but already
        // inactive; treating it as live would capture a pose override and resurrect it on load.
        static bool Gone(Island i) => i == null || !i.gameObject.activeSelf;

        public long[] ConsumedKeys()
        {
            var a = new long[_consumed.Count];
            _consumed.CopyTo(a);
            return a;
        }

        public void RestoreState(Vector2 startPos, long[] consumed)
        {
            _startPos = startPos;
            _started = true;
            _consumed.Clear();
            if (consumed != null) foreach (var k in consumed) _consumed.Add(k);
            _worldSlotsDirty = true;
        }

        void Consume(long key)
        {
            if (_consumed.Add(key)) _worldSlotsDirty = true;
        }

        public void RestoreOverrides(List<IslandSaveData> overrides)
        {
            _overrides.Clear();
            if (overrides == null) return;
            foreach (var d in overrides)
                if (d != null) _overrides[d.slotKey] = d;
        }

        // Overrides cover every streamed island that differs from its plan: the loaded ones are re-evaluated,
        // unloaded ones keep the entry captured when they streamed out.
        public List<IslandSaveData> CaptureOverrides()
        {
            foreach (var chunk in _chunks.Values)
                foreach (var slot in chunk.slots)
                {
                    if (!slot.spawned) continue;
                    long key = Key(chunk.coord, slot.index);
                    if (Gone(slot.island)) { Consume(key); _overrides.Remove(key); continue; }
                    var d = CaptureIfDirty(slot, key);
                    if (d != null) _overrides[key] = d;
                    else _overrides.Remove(key);
                }
            foreach (var k in _consumed) _overrides.Remove(k);
            return new List<IslandSaveData>(_overrides.Values);
        }

        public bool IsSlotDirty(Island island)
        {
            foreach (var chunk in _chunks.Values)
                foreach (var slot in chunk.slots)
                    if (slot.island == island) return !Gone(island) && (ShapeDirty(slot) || PoseDirty(slot));
            return false;
        }

        bool ShapeDirty(Slot slot)
        {
            var isl = slot.island;
            if (isl.IsUplifting) return true;
            if (isl.Version == slot.baseVersion) return false;
            if (Mathf.Abs(isl.LandArea - slot.baseArea) > dirtyAreaFraction * Mathf.Max(1f, slot.baseArea)) return true;
            return Mathf.Abs(isl.MaxHeight - slot.baseMaxHeight) > dirtyHeight;
        }

        bool PoseDirty(Slot slot)
        {
            var isl = slot.island;
            return WrapDistance(isl.PlanarPosition, slot.pos) > dirtyDistance || isl.SelfVelocity.magnitude > 0.5f;
        }

        IslandSaveData CaptureIfDirty(Slot slot, long key)
        {
            bool shape = ShapeDirty(slot);
            var life = IslandSaveUtil.CaptureLife(slot.island, shape);
            // Life that has lived a while (herds, succession) is worth keeping even on a clean slot.
            if (!shape && !PoseDirty(slot) && life == null) return null;
            var d = shape ? slot.island.Capture() : IslandSaveUtil.CapturePose(slot.island);
            d.slotKey = key;
            d.emergeRemaining = 0f;
            d.life = life;
            return d;
        }

        public int LiveIslands
        {
            get
            {
                int n = 0;
                foreach (var c in _chunks.Values)
                    foreach (var s in c.slots)
                        if (s.spawned && !Gone(s.island)) n++;
                return n;
            }
        }

        // Every island of the (finite) world, once, with wrapped world coordinates in [0, WorldSize).
        void EnsureStarted()
        {
            if (_started) return;
            if (player == null) player = FindPlayer();
            if (player == null) return;
            _started = true;
            _startPos = new Vector2(player.transform.position.x, player.transform.position.z);
            _worldSlotsDirty = true;
        }

        // Cached: the plan only depends on the seed, the start position and the consumed set, and the HUD
        // asks for it several times per refresh.
        public IReadOnlyList<WorldSlot> WorldSlots()
        {
            EnsureStarted();
            SweepConsumed();
            if (!_worldSlotsDirty) return _worldSlots;
            _worldSlotsDirty = false;
            _worldSlots.Clear();
            if (WorldChunks <= 0) return _worldSlots;
            for (int cx = 0; cx < WorldChunks; cx++)
                for (int cz = 0; cz < WorldChunks; cz++)
                {
                    var chunk = Plan(new Vector2Int(cx, cz));
                    foreach (var s in chunk.slots)
                        _worldSlots.Add(new WorldSlot
                        {
                            pos = s.pos,
                            radius = Island.KindForSeed(s.shapeSeed) == IslandKind.Barren ? s.radius * 0.8f : s.radius,
                            archetype = s.archetype,
                            areaFactor = IslandArchetypes.AreaFactor(s.archetype),
                            consumed = _consumed.Contains(Key(chunk.coord, s.index))
                        });
                }
            return _worldSlots;
        }

        // islandsLeft counts the planned slots whose key is not consumed. A slot is consumed once its streamed island
        // is gone, i.e. merged into another island (the player, or an AI island that is then counted in its place);
        // this sweeps the loaded slots first, so the count is exact in the very frame of a merge. Hence islandsLeft
        // is 0 exactly when every planned island of the world has been merged away. Volcanoes are not planned slots
        // and never count.
        public void Progress(out float remainingArea, out int islandsLeft)
        {
            remainingArea = 0f;
            islandsLeft = 0;
            foreach (var s in WorldSlots())
            {
                if (s.consumed) continue;
                remainingArea += s.EstimatedArea;
                islandsLeft++;
            }
        }

        public int WorldIslandCount => WorldSlots().Count;

        // Share of the planned islands already merged away (0..1), by count.
        public float MergedShare
        {
            get
            {
                var slots = WorldSlots();
                if (slots.Count == 0) return 0f;
                int done = 0;
                for (int i = 0; i < slots.Count; i++) if (slots[i].consumed) done++;
                return done / (float)slots.Count;
            }
        }

        void SweepConsumed()
        {
            foreach (var chunk in _chunks.Values)
                foreach (var slot in chunk.slots)
                    if (slot.spawned && Gone(slot.island)) Consume(Key(chunk.coord, slot.index));
        }

        void OnEnable() => ResetWorld();

        // Switches to another world layout (an old save): clears the world like ResetWorld. ResetWorld itself goes
        // back to the current layout, so a new game always gets the small world.
        public void UseLayout(int version)
        {
            if (!IsKnownLayout(version)) version = WorldGenVersion;
            if (version == Layout) return;
            ResetWorld();
            Layout = version;
        }

        public void ResetWorld()
        {
            Layout = WorldGenVersion;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
            _chunks.Clear();
            _consumed.Clear();
            _overrides.Clear();
            _started = false;
            _worldSlotsDirty = true;
        }

        void Update()
        {
            if (player == null) player = FindPlayer();
            if (player == null) return;
            if (!Application.isPlaying)
            {
                // WorldSlots()/Progress() (HUD, tools) may have started the plan before the first editor tick.
                if (!_started || _chunks.Count == 0) StreamAround(true);
            }
            else StreamAround(false);
            player.RunProgress = MergedShare;
        }

        static Island FindPlayer()
        {
            foreach (var i in Island.All) if (i != null && i.useKeyboardInput) return i;
            return null;
        }

        int Wrap(int c) => WorldChunks > 0 ? ((c % WorldChunks) + WorldChunks) % WorldChunks : c;

        long Key(Vector2Int c, int idx) => ((long)Wrap(c.x) << 40) ^ ((long)Wrap(c.y) << 20) ^ idx;

        static int Hash(int a, int b, int c)
        {
            unchecked { return a * 73856093 ^ b * 19349663 ^ c * 83492791; }
        }

        Vector2 WrapDelta(Vector2 a, Vector2 b)
        {
            Vector2 d = a - b;
            float w = WorldSize;
            if (w > 0f)
            {
                d.x -= w * Mathf.Round(d.x / w);
                d.y -= w * Mathf.Round(d.y / w);
            }
            return d;
        }

        float WrapDistance(Vector2 a, Vector2 b) => WrapDelta(a, b).magnitude;

        public void StreamAround(bool immediate)
        {
            if (player == null) player = FindPlayer();
            if (player == null) return;
            if (!_started)
            {
                _started = true;
                _startPos = new Vector2(player.transform.position.x, player.transform.position.z);
                _worldSlotsDirty = true;
            }

            Vector2 pp = new Vector2(player.transform.position.x, player.transform.position.z);
            float size = ChunkSize;
            int load = LoadRadius;
            var pc = new Vector2Int(Mathf.FloorToInt(pp.x / size), Mathf.FloorToInt(pp.y / size));

            for (int dx = -load; dx <= load; dx++)
                for (int dz = -load; dz <= load; dz++)
                {
                    var c = new Vector2Int(pc.x + dx, pc.y + dz);
                    if (!_chunks.ContainsKey(c))
                        using (PlanMarker.Auto())
                        {
                            var chunk = Plan(c);
                            _chunks[c] = chunk;
                            if (!immediate) Prebuild(chunk);
                        }
                }

            _toRemove.Clear();
            foreach (var kv in _chunks)
            {
                var c = kv.Key;
                if (Mathf.Max(Mathf.Abs(c.x - pc.x), Mathf.Abs(c.y - pc.y)) > load + UnloadMargin)
                {
                    using (UnloadMarker.Auto()) Unload(kv.Value);
                    _toRemove.Add(c);
                }
            }
            foreach (var c in _toRemove) _chunks.Remove(c);

            // One unit of work per frame: either the next life system of a freshly spawned island, or a spawn.
            if (immediate) FlushStaging();
            else
                using (StageMarker.Auto())
                    if (StageNext()) return;

            int budget = immediate ? int.MaxValue : spawnsPerFrame;
            foreach (var chunk in _chunks.Values)
                foreach (var slot in chunk.slots)
                {
                    if (slot.spawned)
                    {
                        if (Gone(slot.island)) Consume(Key(chunk.coord, slot.index));
                        continue;
                    }
                    if (budget <= 0) return;
                    if (_consumed.Contains(Key(chunk.coord, slot.index))) { slot.spawned = true; continue; }
                    if (!immediate && slot.prebuild != null && !slot.prebuild.IsCompleted) continue;
                    using (SpawnMarker.Auto()) Spawn(chunk, slot, immediate);
                    budget--;
                }
        }

        // Island generation was the largest single cost of streaming (up to 4 ms for a big island, more with the
        // mesh), so the heightfields of a newly planned chunk are built on workers while the player is still
        // a chunk away; Spawn then only has to take the finished shape.
        void Prebuild(Chunk chunk)
        {
            foreach (var slot in chunk.slots)
            {
                if (slot.spawned || _consumed.Contains(Key(chunk.coord, slot.index))) continue;
                var type = slot.archetype;
                float radius = slot.radius, cell = IslandArchetypes.CellSize(slot.radius);
                int seed = slot.shapeSeed;
                slot.prebuild = System.Threading.Tasks.Task.Run(() => Island.PrebuiltShape.BuildForStreamed(type, radius, seed, cell));
            }
        }

        // A fresh island used to be generated, planted, stocked with herds, critters and a settlement in one
        // frame: up to 6 ms on desktop, a dropped frame or two on a phone every time an island streamed in
        // while driving. The life systems now start disabled and are switched on one per frame.
        readonly List<Island> _staging = new();

        static readonly ProfilerMarker PlanMarker = new("WorldStreamer.Plan");
        static readonly ProfilerMarker UnloadMarker = new("WorldStreamer.Unload");
        static readonly ProfilerMarker SpawnMarker = new("WorldStreamer.Spawn");
        static readonly ProfilerMarker StageMarker = new("WorldStreamer.Stage");

        bool StageNext()
        {
            while (_staging.Count > 0)
            {
                var island = _staging[0];
                var next = island != null && island.isActiveAndEnabled ? NextDisabledLife(island) : null;
                if (next == null)
                {
                    _staging.RemoveAt(0);
                    continue;
                }
                next.enabled = true;
                return true;
            }
            return false;
        }

        void FlushStaging()
        {
            foreach (var island in _staging)
            {
                if (island == null) continue;
                for (var next = NextDisabledLife(island); next != null; next = NextDisabledLife(island)) next.enabled = true;
            }
            _staging.Clear();
        }

        // In dependency order: herds and critters read the plant grid, the settlement reads both.
        static Behaviour NextDisabledLife(Island island)
        {
            Behaviour b = island.GetComponent<Drift.Life.IslandLifeSystem>();
            if (b != null && !b.enabled) return b;
            b = island.GetComponent<Drift.Life.IslandHerdSystem>();
            if (b != null && !b.enabled) return b;
            b = island.GetComponent<Drift.Life.IslandCrittersSystem>();
            if (b != null && !b.enabled) return b;
            b = island.GetComponent<Drift.Life.IslandSettlementSystem>();
            if (b != null && !b.enabled) return b;
            return null;
        }

        // Archetype weights per size class, in IslandArchetype order from Blob on
        // (Blob, Ridge, Crescent, TwinPeak, Sandbank, Mesa, Archipelago, Stack). A crescent needs a ring a
        // few cells wide and a stack is a small thing, hence the zeros.
        static readonly int[] IsletWeights = { 35, 0, 0, 0, 20, 0, 15, 30 };
        static readonly int[] SmallWeights = { 30, 16, 0, 10, 14, 12, 10, 8 };
        static readonly int[] MediumWeights = { 26, 16, 15, 14, 6, 10, 13, 0 };
        static readonly int[] BigWeights = { 28, 18, 15, 18, 0, 8, 13, 0 };
        static readonly int[] LargeWeights = { 38, 15, 10, 25, 0, 0, 12, 0 };

        static IslandArchetype Pick(System.Random rnd, int[] weights)
        {
            int total = 0;
            for (int i = 0; i < weights.Length; i++) total += weights[i];
            int roll = rnd.Next(total);
            for (int i = 0; i < weights.Length; i++)
            {
                roll -= weights[i];
                if (roll < 0) return (IslandArchetype)(i + 1);
            }
            return IslandArchetype.Blob;
        }

        void RollMain(System.Random rnd, out float radius, out IslandArchetype type)
        {
            float u = (float)rnd.NextDouble(), t = (float)rnd.NextDouble();
            if (u < largeShare) { radius = 9f + 5f * t * t; type = Pick(rnd, LargeWeights); }
            else if (u < largeShare + bigShare) { radius = 6f + 3f * t; type = Pick(rnd, BigWeights); }
            else if (u < largeShare + bigShare + mediumShare) { radius = 3f + 3f * t; type = Pick(rnd, MediumWeights); }
            else { radius = 2f + t; type = Pick(rnd, SmallWeights); }
        }

        float SmallScale => Mathf.Sqrt(Mathf.Max(1f, smallIslandAreaScale));

        static void RollIslet(System.Random rnd, out float radius, out IslandArchetype type)
        {
            radius = 1.2f + 0.8f * Mathf.Pow((float)rnd.NextDouble(), 1.3f);
            type = Pick(rnd, IsletWeights);
            if (type == IslandArchetype.Archipelago) radius = Mathf.Max(radius, 1.6f);
        }

        // The plan of a chunk depends only on its wrapped coordinates, so the world repeats seamlessly.
        Chunk Plan(Vector2Int coord) => IsLegacyLayout ? PlanLegacy(coord) : PlanFromWorld(coord);

        // The small world: the whole torus is planned at once (GlobalPlan) and each chunk takes the islands whose
        // centre lies in it, moved onto this copy of the world.
        Chunk PlanFromWorld(Vector2Int coord)
        {
            var chunk = new Chunk { coord = coord };
            var wrapped = new Vector2Int(Wrap(coord.x), Wrap(coord.y));
            Vector2 shift = (Vector2)(coord - wrapped) * ChunkSize;
            foreach (var g in GlobalPlan())
                if (g.chunk == wrapped)
                    chunk.slots.Add(new Slot { pos = g.pos + shift, radius = g.radius, archetype = g.archetype, shapeSeed = g.shapeSeed, index = g.index });
            return chunk;
        }

        struct PlannedIsland
        {
            public Vector2 pos;
            public float radius;
            public IslandArchetype archetype;
            public int shapeSeed, index;
            public Vector2Int chunk;
        }

        readonly List<PlannedIsland> _plan = new();
        int _planKey;
        bool _planValid;

        int PlanKey()
        {
            unchecked
            {
                int h = Hash(seed, worldIslands, worldIslets);
                h = h * 31 + Hash(worldLarge, worldBig, worldMedium);
                h = h * 31 + worldSpacing.GetHashCode();
                h = h * 31 + ChunkSize.GetHashCode();
                h = h * 31 + WorldChunks * 7919 + Layout;
                h = h * 31 + startExclusion.GetHashCode();
                h = h * 31 + (_started ? _startPos.GetHashCode() : 0);
                return h;
            }
        }

        // worldIslands islands in fixed size classes (worldLarge / worldBig / worldMedium, the rest small, worldIslets
        // of them islets), largest first, each at a random spot of the torus at least worldSpacing * W / sqrt(n)
        // from every other centre (half that for islets) and never closer than the old pair gaps. When no spot is
        // found the spacing relaxes step by step, so the count is exact.
        List<PlannedIsland> GlobalPlan()
        {
            int key = PlanKey();
            if (_planValid && key == _planKey) return _plan;
            _plan.Clear();
            float w = WorldSize;
            if (w <= 0f) return _plan;

            // Keyed on the layout, so a run planned in the 360 u world keeps exactly the islands it had.
            var rnd = new System.Random(Hash(seed, 0x51A11, Layout));
            int n = Mathf.Max(1, worldIslands);
            int islets = Mathf.Clamp(worldIslets, 0, n);
            int mains = n - islets;
            int large = Mathf.Clamp(worldLarge, 0, mains);
            int big = Mathf.Clamp(worldBig, 0, mains - large);
            int medium = Mathf.Clamp(worldMedium, 0, mains - large - big);

            var radii = new float[n];
            var types = new IslandArchetype[n];
            var seeds = new int[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)rnd.NextDouble();
                if (i < large) { radii[i] = 9f + 4f * t * t; types[i] = Pick(rnd, LargeWeights); }
                else if (i < large + big) { radii[i] = 6f + 3f * t; types[i] = Pick(rnd, BigWeights); }
                else if (i < large + big + medium) { radii[i] = 3f + 3f * t; types[i] = Pick(rnd, MediumWeights); }
                else if (i < mains) { radii[i] = (2f + t) * SmallScale; types[i] = Pick(rnd, SmallWeights); }
                else { RollIslet(rnd, out radii[i], out types[i]); radii[i] *= SmallScale; }
                seeds[i] = rnd.Next();
            }
            for (int i = 1; i < n; i++)
                for (int k = i; k > 0 && radii[k] > radii[k - 1]; k--)
                {
                    (radii[k], radii[k - 1]) = (radii[k - 1], radii[k]);
                    (types[k], types[k - 1]) = (types[k - 1], types[k]);
                    (seeds[k], seeds[k - 1]) = (seeds[k - 1], seeds[k]);
                }

            float spacing = Mathf.Max(0f, worldSpacing) * w / Mathf.Sqrt(n);
            float size = ChunkSize;
            for (int i = 0; i < n; i++)
            {
                float r = radii[i];
                bool islet = r < 2f;
                float need = islet ? 0.5f * spacing : spacing;
                for (int attempt = 0; attempt < 600; attempt++)
                {
                    if (attempt > 0 && attempt % 60 == 0) need *= 0.85f;
                    var pos = new Vector2((float)rnd.NextDouble() * w, (float)rnd.NextDouble() * w);
                    if (_started && WrapDistance(pos, _startPos) < startExclusion + 1.4f * r) continue;
                    bool clash = false;
                    foreach (var o in _plan)
                    {
                        float sum = o.radius + r;
                        float gap = islet || o.radius < 2f ? sum * 1.4f + 5f : sum * 1.8f + 6f;
                        if (WrapDistance(o.pos, pos) < Mathf.Max(gap, need)) { clash = true; break; }
                    }
                    if (clash) continue;
                    var chunk = new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(pos.x / size), 0, WorldChunks - 1),
                        Mathf.Clamp(Mathf.FloorToInt(pos.y / size), 0, WorldChunks - 1));
                    _plan.Add(new PlannedIsland { pos = pos, radius = r, archetype = types[i], shapeSeed = seeds[i], index = i, chunk = chunk });
                    break;
                }
            }
            _planValid = true;
            _planKey = key;
            return _plan;
        }

        Chunk PlanLegacy(Vector2Int coord)
        {
            var chunk = new Chunk { coord = coord };
            var rnd = new System.Random(Hash(seed, Wrap(coord.x), Wrap(coord.y)));
            Vector2 min = new Vector2(coord.x, coord.y) * ChunkSize;

            int mains = rnd.Next(minPerChunk, maxPerChunk + 1);
            var radii = new float[mains];
            var types = new IslandArchetype[mains];
            for (int i = 0; i < mains; i++) RollMain(rnd, out radii[i], out types[i]);
            // Largest first, so the big ones get their room and the rest fills in around them.
            for (int i = 1; i < mains; i++)
                for (int k = i; k > 0 && radii[k] > radii[k - 1]; k--)
                {
                    (radii[k], radii[k - 1]) = (radii[k - 1], radii[k]);
                    (types[k], types[k - 1]) = (types[k - 1], types[k]);
                }

            int index = 0;
            for (int i = 0; i < mains; i++) TryPlace(chunk, rnd, min, radii[i], types[i], index++, null);

            int mainCount = chunk.slots.Count;
            int islets = rnd.Next(minIslets, maxIslets + 1);
            for (int i = 0; i < islets; i++)
            {
                RollIslet(rnd, out float radius, out var type);
                bool satellite = rnd.NextDouble() < satelliteChance;
                Slot parent = satellite && mainCount > 0 ? chunk.slots[rnd.Next(mainCount)] : null;
                TryPlace(chunk, rnd, min, radius, type, index++, parent);
            }
            return chunk;
        }

        void TryPlace(Chunk chunk, System.Random rnd, Vector2 min, float radius, IslandArchetype type, int index, Slot parent)
        {
            // Land reaches up to ~1.4 radii (ridges, clusters): keep that inside the chunk so neighbours
            // planned independently never overlap.
            float size = ChunkSize;
            float margin = Mathf.Max(10f, 1.4f * radius + 2f);
            for (int attempt = 0; attempt < 24; attempt++)
            {
                float a = (float)rnd.NextDouble(), b = (float)rnd.NextDouble();
                Vector2 pos;
                if (parent != null)
                {
                    float ang = a * 2f * Mathf.PI;
                    float dist = (parent.radius + radius) * 1.4f + 5f + 8f * b;
                    pos = parent.pos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
                    Vector2 rel = pos - min;
                    if (rel.x < margin || rel.y < margin || rel.x > size - margin || rel.y > size - margin) continue;
                }
                else pos = min + new Vector2(Mathf.Lerp(margin, size - margin, a), Mathf.Lerp(margin, size - margin, b));

                if (_started && WrapDistance(pos, _startPos) < startExclusion + 1.4f * radius) continue;

                bool islet = radius < 2f;
                bool clash = false;
                foreach (var o in chunk.slots)
                {
                    float sum = o.radius + radius;
                    float gap = islet || o.radius < 2f ? sum * 1.4f + 5f : sum * 1.8f + 6f;
                    if (Vector2.Distance(o.pos, pos) < gap) { clash = true; break; }
                }
                if (clash) continue;

                chunk.slots.Add(new Slot { pos = pos, radius = radius, archetype = type, shapeSeed = rnd.Next(), index = index });
                return;
            }
        }

        void Spawn(Chunk chunk, Slot slot, bool immediate)
        {
            long key = Key(chunk.coord, slot.index);
            _overrides.TryGetValue(key, out var saved);

            var go = new GameObject($"Island_{Wrap(chunk.coord.x)}_{Wrap(chunk.coord.y)}_{slot.index}");
            go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(slot.pos.x, 0f, slot.pos.y);
            go.transform.rotation = Quaternion.Euler(0f, saved != null ? saved.yaw : (slot.shapeSeed & 1023) * 0.35f, 0f);

            var island = go.AddComponent<Island>();
            island.useKeyboardInput = false;
            island.landRadius = slot.radius;
            island.archetype = slot.archetype;
            island.cellSize = IslandArchetypes.CellSize(slot.radius);
            island.shapeSeed = slot.shapeSeed;
            if (slot.prebuild != null && slot.prebuild.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                island.Prebuilt = slot.prebuild.Result;
            slot.prebuild = null;
            var life = go.AddComponent<Drift.Life.IslandLifeSystem>();
            var herds = go.AddComponent<Drift.Life.IslandHerdSystem>();
            var critters = go.AddComponent<Drift.Life.IslandCrittersSystem>();
            var settlement = go.AddComponent<Drift.Life.IslandSettlementSystem>();
            life.seed = herds.seed = critters.seed = settlement.seed = slot.shapeSeed;
            // A saved island restores its life right below, so only a fresh one is staged.
            bool staged = !immediate && saved == null;
            if (staged) life.enabled = herds.enabled = critters.enabled = settlement.enabled = false;
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = islandMaterial;
            go.SetActive(true);
            if (staged) _staging.Add(island);

            slot.baseArea = island.LandArea;
            slot.baseMaxHeight = island.MaxHeight;
            slot.baseVersion = island.Version;
            slot.island = island;
            slot.spawned = true;

            if (saved != null)
            {
                // The override is keyed by wrapped chunk, so its position is re-based onto this copy of the world -
                // on a copy: the entry may still sit in a SaveGame that a background save is writing out.
                saved = saved.ShallowCopy();
                Vector2 delta = WrapDelta(new Vector2(saved.posX, saved.posZ), slot.pos);
                Vector2 p = slot.pos + delta;
                saved.posX = p.x;
                saved.posZ = p.y;
                IslandSaveUtil.ApplySaved(island, saved);
                _overrides.Remove(key);
            }
        }

        void Unload(Chunk chunk)
        {
            foreach (var slot in chunk.slots)
            {
                if (!slot.spawned) continue;
                long key = Key(chunk.coord, slot.index);
                if (Gone(slot.island))
                {
                    Consume(key);
                    _overrides.Remove(key);
                    continue;
                }
                var d = CaptureIfDirty(slot, key);
                if (d != null) _overrides[key] = d;
                else _overrides.Remove(key);
                var go = slot.island.gameObject;
                go.SetActive(false);
                if (Application.isPlaying) Destroy(go);
                else DestroyImmediate(go);
            }
        }
    }
}
