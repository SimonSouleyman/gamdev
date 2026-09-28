using System;
using System.Collections.Generic;
using Drift.Core;
using UnityEngine;

namespace Drift.Life
{
    [Serializable]
    public class HerdSaveData
    {
        public int kind;
        public float cx, cz, tx, tz, wait, grow;
        // x, z, offsetX, offsetZ, yaw, scale per member.
        public float[] m;
        public int[] variant;
        // Phase 3, per member: growth 0..1 (1 = adult), age in herd seconds, parent member index (-1 = none).
        // Files from before have none of these (null or empty) and load as all-adult herds.
        public float[] growth, age;
        public int[] parent;
    }

    [Serializable]
    public class LifeSaveData
    {
        public float originX, originZ, noiseX, noiseZ, cellSize;
        public int nx, nz;
        public float age;
        public float[] stage, burn, fireT;
        public List<HerdSaveData> herds;
        // Phase 4 residents (crabs, turtles); files from before have an empty block and repopulate from seed.
        public CritterSaveData critters;
        // Phase 5 colour season 0..1 (spring -> summer -> autumn -> back); files from before read 0.
        public float season;
        // Island folk (IslandSettlementSystem); files from before have no block and load unsettled.
        public SettlementSaveData settlement;
        // Plants of other biomes that came with a merge, two ints each: kind | variant << 8 | slot << 16, and the
        // position relative to the grid origin in 2 cm steps (x | z << 16). Native plants are regrown from the
        // stage grid and never stored; files from before have no block.
        public int[] foreign;
        // The biome of every land cell, run-length encoded in the grid's own order: count << 4 | (int)LifeBiome.
        // Raw bytes would cost ~2 characters per cell in the JSON; a merged island holds two or three large
        // regions, so the runs are two orders of magnitude smaller. Files from before have no block and load
        // with every cell set to the island's own seed biome.
        public int[] biomeRuns;
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class IslandLifeSystem : MonoBehaviour
    {
        const string VegObjectName = "Vegetation";
        const int K = LifeMeshes.PlantKinds;
        const int SlotGrass = 0, SlotFlower = 1, SlotBush = 2, SlotTree = 3, SlotPalm = 4, SlotReed = 5;
        const float ReemergeStage = 0.02f;
        // Grass/flowers shaded out by trees shrink away over ~10 s rather than popping.
        const float CanopyFadeRate = 0.1f;

        public float timeScale = 6f;
        public float tickInterval = 0.2f;
        public float cellSize = 1.2f;
        public float growthRate = 0.008f;
        // Lightning strikes per real second at full storm on an island of area 50 (scales with area).
        public float lightningRate = 0.035f;
        public float strikeDuration = 0.3f;
        public float fireDuration = 20f;
        // Owner 2026-09-25: one strike burns a patch, not the whole island. The old rule (every burning cell lights
        // each neighbour at 0.1/life-s for its whole ~20 s) set ~90 % of the neighbours alight, far above the
        // percolation threshold, so one fire ate every connected stand. Now each strike opens a fire patch with a
        // cell budget (firePatchShare of the land, firePatchCells min..max, ±30 %) and a radius from that budget;
        // spread fades towards the rim (1 - (d/R)^2, reaching further downwind), is damped by the storm's rain and
        // stops once the budget is used up. Grass burns fast and short, woods slow and long.
        [Header("Blitzfeuer")]
        [Tooltip("Anteil der Landzellen, den ein Blitzeinschlag höchstens abbrennt.")]
        [Range(0.02f, 0.4f)] public float firePatchShare = 0.1f;
        [Tooltip("Mindest- und Höchstzahl Zellen pro Blitzfeuer (1 Zelle ≈ 1,4 Fläche).")]
        public Vector2Int firePatchCells = new Vector2Int(2, 40);
        [Tooltip("Ausbreitung zur Nachbarzelle pro Lebenssekunde am Einschlag (fällt zum Rand auf 0).")]
        [Range(0.01f, 0.5f)] public float fireSpreadRate = 0.05f;
        [Tooltip("Wie stark der Sturmregen die Ausbreitung dämpft (bei voller Sturmstärke).")]
        [Range(0f, 1f)] public float fireRainDamping = 0.45f;
        [Tooltip("Wie viel weiter das Feuer mit dem Wind läuft (0 = rund).")]
        [Range(0f, 1.5f)] public float fireWindStretch = 0.6f;
        [Tooltip("Gras: Brenndauer (Anteil) und Ausbreitung (Faktor) gegenüber Wald.")]
        public Vector2 grassFire = new Vector2(0.45f, 1.6f);
        // Tiers by planar camera distance (LifeLod): near < detailDistance ticks at tickInterval and refreshes
        // mesh/tint at meshInterval/tintInterval; mid < simDistance doubles both intervals; far only ticks the
        // succession at farTickFactor times the interval and never touches mesh or tint.
        public float simDistance = 110f;
        public float hideDistance = 260f;
        public float detailDistance = 45f;
        public float meshInterval = 0.15f;
        public float tintInterval = 0.5f;
        public float farTickFactor = 8f;
        public int maxPlants = 3000;
        public int seed = 1;
        public float plantScale = 0.55f;
        public float volcanicFertility = 1.2f;
        public float volcanicInitStage = 0.2f;
        public float ancientInitStage = 1.15f;
        public float barrenFertility = 0.4f;
        public float barrenInitStage = 0.35f;

        // Phase 5. A shore cell is a land cell with water in its 8-neighbourhood (islands are plateaus with steep
        // rims, so a height band alone finds almost no cells). Palms stand below every third shore cell on the
        // beach (ground between palmMinHeight and palmMaxHeight, found by walking downhill from the cell), reeds
        // at the waterline (<= reedMaxHeight) below every second one. Both are capped per island and, like every
        // plant, only die by fire or drowning.
        public int maxPalms = 24;
        public int maxReeds = 40;
        public float palmMinHeight = 0.05f;
        public float palmMaxHeight = 0.35f;
        public float reedMaxHeight = 0.12f;
        // Fire scars: burn fades per life-second (0.9 after a fire -> 0 in ~720 life-seconds = 2 real minutes)
        // while fresh grass on the scar is drawn as small bright shoots that normalise with it.
        public float burnFadeRate = 0.00125f;
        // Trees and palms spawn as saplings at saplingScale and reach full size after treeGrowTime life-seconds;
        // the mesh picks the growth up in growthRebuildStep steps.
        public float treeGrowTime = 240f;
        public float saplingScale = 0.3f;
        public float growthRebuildStep = 0.05f;
        // Bloom: flowers scale 0.5..1.2 and brighten with a per-cell factor that cycles every bloomPeriod real
        // seconds (on the life clock, so offline time advances it) with a noise offset per cell, evaluated every
        // bloomInterval and baked into the mesh once any cell moved more than bloomRebuildStep.
        public float bloomPeriod = 240f;
        public float bloomInterval = 1f;
        public float bloomRebuildStep = 0.08f;
        // Colour seasons: one cycle per seasonPeriod real seconds of life time, amplitude <= 25 % at 1.
        public float seasonPeriod = 1200f;
        public float seasonAmplitude = 1f;
        public float seasonRebuildStep = 0.02f;
        // Vertex budget of the vegetation mesh. Islands whose land cells at old growth would exceed it (about
        // fullCellVerts per cell: two trees and 1.4 bushes) thin their grass/bush/tree density uniformly
        // (stochastic rounding per cell, so the thinning is even, not row by row); spawns stop as a last resort
        // and a merge fades the guest's cheapest surplus first. Nothing else ever removes a plant.
        public int maxVegetationVerts = 60000;
        public int fullCellVerts = 190;

        // The wind that looks alive from the chase camera shakes the trees when the camera stands two metres away
        // (watching a herd, photo mode). The sway therefore calms down between windFar and windNear: the lean keeps
        // breathing at windCalmAmplitude, the fast flutter drops to windCalmFlutter of its size and to
        // windCalmSpeed of its rate. Beyond windFar nothing changes.
        [Header("Wind (Pflanzen)")]
        [Tooltip("Ab dieser Kameraentfernung (Einheiten) weht der Wind unverändert.")]
        [Range(5f, 80f)] public float windFar = 26f;
        [Tooltip("Bei dieser Kameraentfernung ist das Schwanken am ruhigsten (Herde beobachten).")]
        [Range(0.5f, 20f)] public float windNear = 4f;
        [Tooltip("Stärke des Schwankens ganz nah (1 = unverändert).")]
        [Range(0.05f, 1f)] public float windCalmAmplitude = 0.45f;
        [Tooltip("Stärke des schnellen Flatterns ganz nah (1 = unverändert).")]
        [Range(0f, 1f)] public float windCalmFlutter = 0.22f;
        [Tooltip("Tempo des schnellen Flatterns ganz nah (1 = unverändert).")]
        [Range(0.05f, 1f)] public float windCalmSpeed = 0.35f;
        // Species of another biome (they only ever arrive with a merge) hold on like this: a cell that has room
        // for a plant of a role takes the foreign species with foreignReseedChance when one grows in it or next to
        // it, and every foreignSpreadInterval life-seconds one foreign plant seeds a suitable neighbour cell - into
        // a gap, or, while its species counts fewer than foreignStand plants, in place of a native one. Nothing
        // foreign ever comes from nothing, so a species whose last plant burns is gone.
        public float foreignReseedChance = 0.5f;
        public float foreignSpreadInterval = 60f;
        public int foreignStand = 12;
        public int maxSavedForeign = 600;

        static readonly Color VolcanicTint = new Color(0.5f, 0.42f, 0.4f, 0.65f);
        static readonly Color BarrenTint = new Color(1.35f, 1.2f, 0.72f, 1f);

        int Character => _surface != null ? _surface.Character : 0;

        float FertilityFactor => Character == 1 ? volcanicFertility : Character == 3 ? barrenFertility : 1f;

        class Plant
        {
            public LifeKind kind;
            public int variant;
            public Vector2 pos;
            public float yaw;
            public float jitter;
            public float phase;
            public float scaleTarget;
            public float scale;
            public float growth;
            // Trees and palms: 0 sapling .. 1 full size, advanced by life time; the mesh holds bakedMaturity.
            public float maturity = 1f;
            public float bakedMaturity = 1f;
            // Grass that came up on a fire scar: drawn as a small, extra green shoot while the cell's burn lasts.
            public bool shoot;
            public bool dying;
            public float fade = 1f;
            // Fade per second once dying: 1.6 burnt, 0.8 drowned, CanopyFadeRate when the canopy replaces it.
            public float fadeRate = 0.8f;
            public float burnT;
            public int cell = -1;
            // Succession role (per-cell counter) the plant fills; a palm is a tree inland and a shore palm on the beach.
            public int slot;
        }

        struct Import
        {
            public Vector2 pos;
            public float stage, burn, fireT;
            // The guest's biome for that cell: absorbed land keeps what grew on it.
            public byte biome;
        }

        static readonly TemplateBatch Batch = new();
        static readonly float[] SwayFactor = { 1f, 1f, 0.5f, 0.35f, 0.85f, 1f };
        static readonly int[] KindScratch = new int[LifeMeshes.KindCount];

        // Global wind for Drift/VertexColor and Drift/Vegetation (xy = wind * (1 + 1.5 storm), z = storm), pushed once per frame by
        // whichever island steps first; force re-pushes inside one editor frame (eval screenshots).
        static readonly int LifeWindId = Shader.PropertyToID("_LifeWind");
        // Drift/Vegetation only: (sway amplitude, gust phase, flutter phase, flutter amplitude). The two phases are
        // accumulated here instead of being read off _Time in the shader, because their rate changes with the camera
        // distance and t * rate would jump by the whole elapsed time whenever the rate moved.
        static readonly int LifeSwayId = Shader.PropertyToID("_LifeWindSway");
        static int _windFrame = -1;
        static float _gustPhase, _flutterPhase;
        public static Vector4 LastWind { get; private set; }
        public static Vector4 LastSway { get; private set; } = new Vector4(1f, 0f, 0f, 1f);

        // Season of the island that ticked last: a freshly populated island starts here instead of at 0, so
        // streamed-in neighbours share the player's season (all islands advance at the same rate).
        public static float SeasonReference { get; private set; }
        public static void ResetSeasonReference() => SeasonReference = 0f;

        readonly List<Plant> _plants = new();
        readonly List<Import> _pending = new();
        IIslandSurface _surface;
        System.Random _rnd;
        Vector2 _noiseOff;

        int _nx, _nz;
        Vector2 _origin;
        float[] _stage, _burn, _fireT, _fert, _maxStage, _height, _bloomPhase, _bloomBaked, _burnBaked;
        bool[] _land, _shore;
        int[] _counts, _quota;
        Color[] _cellColor;
        // (int)LifeBiome per cell, same indexing as _stage/_burn/_land: the biome is a property of the LAND, so
        // land absorbed in a merge keeps growing what its own biome brings forth. BiomeUnset while a fresh cell
        // waits for FillUnknownBiomes to hand it its neighbours' biome.
        byte[] _cellBiome;
        const byte BiomeUnset = 255;
        readonly int[] _biomeCells = new int[Biomes.Count];
        int _biomesPresent;
        ulong _nativeMask;
        int _dominantBiome;
        int _landCells;
        int _foreignCount;
        float _spreadTimer;
        // Per cell and role: 1 + (int)kind of a living foreign plant there, 0 = none (only filled while the island
        // holds foreign plants at all).
        byte[] _foreignAt;
        bool _hasGrid;
        int _gridVersion = -1;
        // Set by AbsorbFrom: the merged island's mesh has been rebuilt with white (untinted) vertex colours and
        // its new land has no biome yet. Cleared by the first ApplyTint after the merge, which must happen before
        // that mesh is drawn - see RefreshAfterMerge.
        bool _mergeRefresh;
        float _age;
        float _season, _bakedSeason;
        int _liveVerts;

        Vector2 _strikePos;
        float _strikeT;

        // A strike's fire: origin (grid-local, shifts with ShiftLocal), radius, downwind direction * stretch, cell
        // budget and cells lit so far. Cells point at their patch through _firePatch (-1 = none: fires that came
        // from a save or a merge just burn out in place).
        struct FirePatch
        {
            public Vector2 origin, wind;
            public float radius;
            public int budget, burned;
        }
        const int MaxFirePatches = 16;
        readonly FirePatch[] _patches = new FirePatch[MaxFirePatches];
        int _nextPatch;
        sbyte[] _firePatch;
        readonly List<int> _spreadTo = new();

        GameObject _vegGo;
        MeshFilter _vegFilter;
        MeshRenderer _vegRenderer;
        Mesh _vegMesh;
        static readonly Color[] WhiteCell = { Color.white };

        float _tickTimer, _meshTimer, _tintTimer, _bloomTimer;
        bool _dirty, _tintDirty;

        public int CreatureCount => _plants.Count;
        public int PlantCount => _plants.Count;
        public int LivePlantCount
        {
            get
            {
                int n = 0;
                foreach (var p in _plants) if (!p.dying) n++;
                return n;
            }
        }
        public int LandCells => _landCells;
        // Set by the sibling IslandSettlementSystem: its clearing takes bushes and trees off the cells under the
        // buildings (SyncPlants) and tints their ground (CellColor).
        public IslandSettlementSystem Settlement { get; set; }
        // Life-seconds simulated since the grid was populated (fresh from seed or restored).
        public float LifeAge => _age;
        public float Season => _season;
        public int GridBuilds { get; private set; }
        public int MeshBuilds { get; private set; }
        public int Ticks { get; private set; }
        public LifeTier Tier { get; private set; }
        public bool HasGrid => _hasGrid;
        public int StrikeCount { get; private set; }
        public int IgnitionCount { get; private set; }
        public Vector2 LastStrike => _strikePos;
        public bool StrikeVisible => _strikeT > 0f;
        public int MeshVertexCount => _vegMesh != null ? _vegMesh.vertexCount : 0;
        public bool HasMesh => _vegMesh != null;
        public Mesh VegetationMesh => _vegMesh;
        // Template vertices of the living plants (what the next rebuild draws at most, the bolt aside).
        public int LiveVertexEstimate => _liveVerts;

        // The four temperate kinds stand for their succession role here (CountOf(Tree) = everything that grows as
        // a tree, whatever the biome calls it), so butterflies, shade seekers and the sound scout work on every
        // island; CountOfKind is the exact count.
        static bool Matches(Plant p, LifeKind query) =>
            p.kind == query || ((int)query <= SlotTree && p.slot == (int)query && p.kind != LifeKind.Palm);

        public int CountOf(LifeKind kind)
        {
            int n = 0;
            foreach (var p in _plants) if (!p.dying && Matches(p, kind)) n++;
            return n;
        }

        public int CountOfKind(LifeKind kind)
        {
            int n = 0;
            foreach (var p in _plants) if (p.kind == kind && !p.dying) n++;
            return n;
        }

        // ---- collection hooks (journal). Bit (int)LifeKind is set while at least one plant of the kind lives here.
        public ulong PlantsPresent
        {
            get
            {
                ulong mask = 0;
                foreach (var p in _plants) if (!p.dying) mask |= 1UL << (int)p.kind;
                return mask;
            }
        }

        public bool HasPlant(LifeKind kind) => (PlantsPresent & (1UL << (int)kind)) != 0;

        // ---- per-cell biome. The island-level Biome is the biome of most of its land (HUD, journal); a merged
        // island answers BiomesPresent / CellsOfBiome for the rest ("Gemäßigt + Tropisch").
        public int Biome => _hasGrid && _landCells > 0 ? _dominantBiome : _surface != null ? _surface.Biome : 0;
        public LifeBiome DominantBiome => (LifeBiome)Biome;
        public string BiomeName => LifeNames.OfBiome(Biome);
        public BiomeSpec BiomeSpec => Biomes.Of(Biome);
        // Bit (int)LifeBiome per biome that at least one land cell carries.
        public int BiomesPresent => _hasGrid && _biomesPresent != 0 ? _biomesPresent : 1 << (_surface != null ? _surface.Biome : 0);
        public int CellsOfBiome(LifeBiome biome)
        {
            int b = (int)biome;
            return b >= 0 && b < _biomeCells.Length ? _biomeCells[b] : 0;
        }

        public LifeBiome BiomeAtCell(int i, int j)
        {
            if (!_hasGrid || i < 0 || j < 0 || i >= _nx || j >= _nz) return DominantBiome;
            int idx = j * _nx + i;
            byte b = _cellBiome[idx];
            return _land[idx] && b < Biomes.Count ? (LifeBiome)b : DominantBiome;
        }

        // The biome of the land under a local position (off the grid: the island's dominant biome). One byte
        // array read - herds found their species with it. A point on the beach can sit in a cell whose centre is
        // already water, so such a cell answers with the land next to it instead of an unwritten value.
        public int BiomeAt(Vector2 localPos)
        {
            if (!_hasGrid) return Biome;
            int i = Mathf.FloorToInt((localPos.x - _origin.x) / cellSize);
            int j = Mathf.FloorToInt((localPos.y - _origin.y) / cellSize);
            if (i < 0 || j < 0 || i >= _nx || j >= _nz) return _dominantBiome;
            int idx = j * _nx + i;
            if (_land[idx]) return _cellBiome[idx] < Biomes.Count ? _cellBiome[idx] : _dominantBiome;
            for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    int ni = i + di, nj = j + dj;
                    if (ni < 0 || nj < 0 || ni >= _nx || nj >= _nz) continue;
                    int n = nj * _nx + ni;
                    if (_land[n] && _cellBiome[n] < Biomes.Count) return _cellBiome[n];
                }
            return _dominantBiome;
        }

        // Native to at least one biome this island carries. After a merge the guest's land is part of the
        // island, so what grows on it is at home here - only a species whose biome is gone is still "foreign".
        public bool NativeHere(LifeKind kind) => (_nativeMask & (1UL << (int)kind)) != 0;
        // A plant species none of this island's biomes brings forth: it came with a merge and its ground did not.
        public bool IsForeign(LifeKind kind) => LifeMeshes.IsPlant(kind) && !NativeHere(kind);
        public int ForeignPlantCount
        {
            get
            {
                int n = 0;
                foreach (var p in _plants) if (!p.dying && !NativeHere(p.kind)) n++;
                return n;
            }
        }
        public int ForeignSpreads { get; private set; }

        // Whether the position lies in a shore cell or next to one (coastal herds choose their ground with it).
        public bool NearShore(Vector2 localPos)
        {
            if (!_hasGrid) return true;
            int ci = Mathf.FloorToInt((localPos.x - _origin.x) / cellSize);
            int cj = Mathf.FloorToInt((localPos.y - _origin.y) / cellSize);
            for (int j = cj - 1; j <= cj + 1; j++)
                for (int i = ci - 1; i <= ci + 1; i++)
                    if (i >= 0 && j >= 0 && i < _nx && j < _nz && _shore[j * _nx + i]) return true;
            return false;
        }

        public LifeKind PlantKindOf(int i) => _plants[i].kind;
        public bool PlantIsTall(int i) => _plants[i].slot == SlotTree || _plants[i].slot == SlotPalm;
        public Vector2 PlantPositionOf(int i) => _plants[i].pos;
        public int PlantVariantOf(int i) => _plants[i].variant;
        public float PlantMaturityOf(int i) => _plants[i].maturity;
        public bool PlantDyingOf(int i) => _plants[i].dying;
        public bool PlantIsShoot(int i) => _plants[i].shoot && !_plants[i].dying && _plants[i].cell >= 0 && _burn[_plants[i].cell] > 0.2f;

        public int ShootCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _plants.Count; i++) if (PlantIsShoot(i)) n++;
                return n;
            }
        }

        public int SaplingCount
        {
            get
            {
                int n = 0;
                foreach (var p in _plants) if (!p.dying && p.maturity < 1f) n++;
                return n;
            }
        }

        public float StageAt(Vector2 localPos)
        {
            if (!_hasGrid) return 0f;
            int idx = CellIndex(localPos);
            return idx < 0 ? 0f : _stage[idx];
        }

        public float BurnAt(Vector2 localPos)
        {
            if (!_hasGrid) return 0f;
            int idx = CellIndex(localPos);
            return idx < 0 ? 0f : _burn[idx];
        }

        // The bloom factor the mesh currently shows for the cell (0.5 without a grid or off the grid).
        public float BloomAt(Vector2 localPos)
        {
            if (!_hasGrid) return 0.5f;
            int idx = CellIndex(localPos);
            return idx < 0 ? 0.5f : _bloomBaked[idx];
        }

        public Color GroundTintAt(Vector2 localPos) => TintAt(localPos);

        // Lights the cell under localPos (tests, debugging); false if there is no land or it already burns.
        public bool IgniteAt(Vector2 localPos)
        {
            if (!_hasGrid) return false;
            int idx = CellIndex(localPos);
            if (idx < 0 || !_land[idx] || _fireT[idx] > 0f) return false;
            Ignite(idx, OpenPatch(idx));
            IgnitionCount++;
            _dirty = true;
            return true;
        }

        // A lightning strike on the cell under localPos (tests, debugging): bolt, thunder report and - on
        // vegetation - a fire patch, exactly like a storm's strike. False off the land.
        public bool StrikeAt(Vector2 localPos)
        {
            if (!_hasGrid) return false;
            int idx = CellIndex(localPos);
            if (idx < 0 || !_land[idx]) return false;
            Strike(idx);
            return true;
        }

        // Cells the most recent fire patch may burn at most / has lit so far (0 before the first fire).
        public int LastFireBudget => _patches[(_nextPatch + MaxFirePatches - 1) % MaxFirePatches].budget;
        public int LastFireBurned => _patches[(_nextPatch + MaxFirePatches - 1) % MaxFirePatches].burned;
        public float LastFireRadius => _patches[(_nextPatch + MaxFirePatches - 1) % MaxFirePatches].radius;

        // One living plant of the kind picked uniformly (reservoir sampling, no allocation), optionally only
        // within range of `near` (range <= 0 = anywhere). Butterflies choose their flowers with this.
        public bool TryRandomPlant(LifeKind kind, System.Random rnd, Vector2 near, float range, out Vector2 pos)
        {
            pos = default;
            int seen = 0;
            float r2 = range * range;
            for (int i = 0; i < _plants.Count; i++)
            {
                var p = _plants[i];
                if (p.dying || !Matches(p, kind)) continue;
                if (range > 0f && (p.pos - near).sqrMagnitude > r2) continue;
                seen++;
                if (rnd.Next(seen) == 0) pos = p.pos;
            }
            return seen > 0;
        }

        public float NearestPlantDistance(LifeKind kind, Vector2 from)
        {
            float best = float.MaxValue;
            foreach (var p in _plants)
                if (!p.dying && Matches(p, kind)) best = Mathf.Min(best, (p.pos - from).magnitude);
            return best;
        }

        public bool FireNear(Vector2 localPos, float radius)
        {
            if (!_hasGrid) return false;
            int r = Mathf.CeilToInt(radius / cellSize);
            int ci = Mathf.FloorToInt((localPos.x - _origin.x) / cellSize);
            int cj = Mathf.FloorToInt((localPos.y - _origin.y) / cellSize);
            for (int j = cj - r; j <= cj + r; j++)
                for (int i = ci - r; i <= ci + r; i++)
                {
                    if (i < 0 || j < 0 || i >= _nx || j >= _nz) continue;
                    if (_fireT[j * _nx + i] > 0f) return true;
                }
            return false;
        }

        public void GetStats(out float meanStage, out int burning, out int landCells)
        {
            float sum = 0f;
            burning = 0;
            landCells = 0;
            if (_land == null) { meanStage = 0f; return; }
            for (int i = 0; i < _land.Length; i++)
            {
                if (!_land[i]) continue;
                landCells++;
                sum += _stage[i];
                if (_fireT[i] > 0f) burning++;
            }
            meanStage = landCells > 0 ? sum / landCells : 0f;
        }

        public void GetHudStats(out int bare, out int plains, out int shrub, out int woods, out int oldGrowth, out int burning)
        {
            bare = plains = shrub = woods = oldGrowth = burning = 0;
            if (_land == null) return;
            for (int i = 0; i < _land.Length; i++)
            {
                if (!_land[i]) continue;
                float s = _stage[i];
                if (_fireT[i] > 0f) burning++;
                if (s < 0.15f) bare++;
                else if (s < 0.4f) plains++;
                else if (s < 0.75f) shrub++;
                else if (s < 1.05f) woods++;
                else oldGrowth++;
            }
        }

        public void GetStageCounts(out int bare, out int plains, out int shrub, out int forest)
        {
            bare = plains = shrub = forest = 0;
            if (_land == null) return;
            for (int i = 0; i < _land.Length; i++)
            {
                if (!_land[i]) continue;
                float s = _stage[i];
                if (s < 0.15f) bare++;
                else if (s < 0.4f) plains++;
                else if (s < 0.75f) shrub++;
                else forest++;
            }
        }

        // Fraction of land cells that currently hold at least one living plant.
        public float PlantedFraction
        {
            get
            {
                if (!_hasGrid || _landCells == 0) return 0f;
                int planted = 0;
                for (int i = 0; i < _land.Length; i++)
                {
                    if (!_land[i]) continue;
                    for (int k = 0; k < K; k++)
                        if (_counts[i * K + k] > 0) { planted++; break; }
                }
                return (float)planted / _landCells;
            }
        }

        void OnEnable()
        {
            _surface = GetComponent<IIslandSurface>();
            RecountBiomes();
            if (_surface != null && _surface.LandArea > 0f) Repopulate();
        }

        void OnDestroy()
        {
            if (_vegMesh == null) return;
            if (Application.isPlaying) Destroy(_vegMesh);
            else DestroyImmediate(_vegMesh);
            _vegMesh = null;
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            Step(Time.deltaTime);
        }

        // Island.MergeFrom throws the terrain's vertex colours away (the merged mesh has a new vertex count) and
        // rebuilds the mesh white, i.e. the shader's raw _Grass on every cell and the wrong ground texture with it.
        // The grid and the tint used to wait for the next Tick (up to tickInterval), so the merged island was drawn
        // in that one flat green for ~12 frames. LateUpdate runs after IslandWorld.Update and before anything is
        // rendered, whatever order the two Updates ran in, so the first frame of the merged island is already
        // tinted. It costs one bool test per island per frame and no extra grid build: the rebuild it does here is
        // the one the next Tick would have done.
        void LateUpdate()
        {
            if (_mergeRefresh) RefreshAfterMerge();
        }

        // Re-grids the merged island and pushes the tint, at most once per merge. Island.MergeFrom may call this
        // directly at its end (same effect, one frame earlier than LateUpdate would be in the worst case).
        public void RefreshAfterMerge()
        {
            _mergeRefresh = false;
            if (_surface == null) _surface = GetComponent<IIslandSurface>();
            if (_surface == null) return;
            if (!_hasGrid || _surface.Version != _gridVersion) RebuildGrid();
            ApplyTint();
            // ShiftLocal moved every plant by the new centroid while the transform moved the other way, so until the
            // next meshInterval rebuild the old mesh stood offset (mostly inside the terrain) and the guest's plants
            // were gone with its GameObject: for ~10 frames the island looked bare. Rebuilt in the merge frame instead,
            // also on hidden or far islands, whose Step would not rebuild it before they come into view again.
            if (_dirty && _hasGrid)
            {
                RebuildVegetationMesh();
                _dirty = false;
            }
            var critters = GetComponent<IslandCrittersSystem>();
            if (critters != null) critters.RefreshAfterMerge();
        }

        float Rand() => (float)_rnd.NextDouble();
        float Rand(float a, float b) => a + Rand() * (b - a);

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static uint CellHash(int idx) => (uint)idx * 2654435761u;

        // ------------------------------------------------------------ wind

        const float FarView = 1000f;
        // Rates of the two sway terms in the shader, kept here so the phases can be integrated.
        const float GustRate = 0.6f, FlutterRate = 3.2f, FlutterStormRate = 3f;

        // How far the camera is from what it looks at. A camera that goes close to a creature (watching a herd,
        // photo mode) reports its own distance; otherwise it is the main camera's distance to the ground under
        // its view, which is what the chase camera frames.
        public static float ViewDistance { get; private set; } = FarView;

        static float _reportedView = -1f;
        static int _reportedFrame = int.MinValue;

        public static void ReportViewDistance(float distance)
        {
            _reportedView = Mathf.Max(0f, distance);
            _reportedFrame = Time.frameCount;
        }

        public static void ClearViewDistance()
        {
            _reportedView = -1f;
            _reportedFrame = int.MinValue;
            ViewDistance = FarView;
        }

        // Static mirror of the sliders of whichever island pushes the wind (the wind itself is global too).
        static float _windFar = 26f, _windNear = 4f, _calmAmplitude = 0.45f, _calmFlutter = 0.22f, _calmSpeed = 0.35f;

        // 0 far away (full wind), 1 at windNear and closer (as calm as it gets), smooth in between.
        public static float CalmAmount(float viewDistance, float near, float far)
        {
            if (far <= near) return viewDistance <= near ? 1f : 0f;
            return Smooth(far, near, viewDistance);
        }

        public static float CalmAmount(float viewDistance) => CalmAmount(viewDistance, _windNear, _windFar);

        // The three sway scales at a camera distance: how far the plant leans, how big the fast flutter on top of
        // it is, and how quickly that flutter runs. 1/1/1 is the old look.
        public static void SwayScales(float viewDistance, out float amplitude, out float flutter, out float speed)
        {
            float c = CalmAmount(viewDistance);
            amplitude = Mathf.Lerp(1f, _calmAmplitude, c);
            flutter = Mathf.Lerp(1f, _calmFlutter, c);
            speed = Mathf.Lerp(1f, _calmSpeed, c);
        }

        static float EstimateViewDistance()
        {
            var cam = Camera.main;
            if (cam == null) return FarView;
            var tr = cam.transform;
            Vector3 p = tr.position, d = tr.forward;
            if (p.y <= 0.05f) return 0f;
            if (d.y > -0.02f) return FarView;
            return Mathf.Min(FarView, p.y / -d.y);
        }

        // The wind is one global for every island, so the sliders of whichever island steps first this frame decide.
        // Every island carries the same values; this only exists so the Inspector can tune them.
        public void ApplyWindSettings()
        {
            _windFar = Mathf.Max(windNear + 0.1f, windFar);
            _windNear = windNear;
            _calmAmplitude = windCalmAmplitude;
            _calmFlutter = windCalmFlutter;
            _calmSpeed = windCalmSpeed;
        }

        public static void PushWind(bool force = false)
        {
            int f = Time.frameCount;
            bool newFrame = f != _windFrame;
            if (!force && !newFrame) return;
            Vector2 w = LifeEnvironment.Wind;
            float s = LifeEnvironment.Storm;
            float mag = 1f + 1.5f * s;
            LastWind = new Vector4(w.x * mag, w.y * mag, s, 0f);
            Shader.SetGlobalVector(LifeWindId, LastWind);

            ViewDistance = f - _reportedFrame <= 1 && _reportedView >= 0f ? _reportedView : EstimateViewDistance();
            SwayScales(ViewDistance, out float amp, out float flut, out float speed);
            if (newFrame)
            {
                _windFrame = f;
                // A forced push inside the same frame must not advance the phases twice (eval screenshots).
                float dt = Mathf.Clamp(Application.isPlaying ? Time.deltaTime : Time.unscaledDeltaTime, 0f, 0.1f);
                _gustPhase = Mathf.Repeat(_gustPhase + dt * GustRate, Mathf.PI * 2f);
                _flutterPhase = Mathf.Repeat(_flutterPhase + dt * (FlutterRate + FlutterStormRate * s) * speed, Mathf.PI * 2f);
            }
            LastSway = new Vector4(Mathf.Max(1e-4f, amp), _gustPhase, _flutterPhase, flut);
            Shader.SetGlobalVector(LifeSwayId, LastSway);
        }

        // ---------------------------------------------------------------- setup

        // Fresh population from the seed. A second call for the same surface Version is a no-op (Island.OnEnable
        // and this component's OnEnable both call it); sinking and merges never come through here - Tick
        // re-grids those with the existing life carried over.
        public void Repopulate()
        {
            if (_surface == null) _surface = GetComponent<IIslandSurface>();
            if (_surface == null) return;
            if (_hasGrid && _gridVersion == _surface.Version) return;

            _rnd = new System.Random(seed);
            _noiseOff = new Vector2(Rand() * 100f, Rand() * 100f);

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (child.GetComponent<CreatureMarker>() == null) continue;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }

            _plants.Clear();
            _pending.Clear();
            _hasGrid = false;
            _age = 0f;
            _season = SeasonReference;
            _strikeT = 0f;
            RebuildGrid();
            SyncPlants(true, 0f);
            EvaluateBloom();
            RebuildVegetationMesh();
            ApplyTint();
            _dirty = false;
        }

        int CellIndex(Vector2 p)
        {
            int i = Mathf.FloorToInt((p.x - _origin.x) / cellSize);
            int j = Mathf.FloorToInt((p.y - _origin.y) / cellSize);
            if (i < 0 || j < 0 || i >= _nx || j >= _nz) return -1;
            return j * _nx + i;
        }

        Vector2 CellCenter(int i, int j) => _origin + new Vector2((i + 0.5f) * cellSize, (j + 0.5f) * cellSize);

        void CellProps(int idx, float h)
        {
            _height[idx] = h;
            _land[idx] = h > 0.12f;
            if (!_land[idx]) { _fert[idx] = 0f; _maxStage[idx] = 0f; return; }
            if (h < 0.35f) { _fert[idx] = 0.35f; _maxStage[idx] = 0.4f; }
            else if (h < 1.9f) { _fert[idx] = 1f; _maxStage[idx] = 1.3f; }
            else if (h < 2.8f) { _fert[idx] = Mathf.Lerp(0.6f, 0.15f, (h - 1.9f) / 0.9f); _maxStage[idx] = 0.6f; }
            else { _fert[idx] = 0.05f; _maxStage[idx] = 0.15f; }
            _fert[idx] *= FertilityFactor;
        }

        float InitStage(Vector2 p)
        {
            float s = Mathf.Clamp01(0.08f + 0.95f * Mathf.PerlinNoise(_noiseOff.x + p.x * 0.22f, _noiseOff.y + p.y * 0.22f));
            switch (Character)
            {
                case 1: return s * volcanicInitStage;
                case 2: return Mathf.Max(s, ancientInitStage);
                case 3: return s * barrenInitStage;
                default: return s;
            }
        }

        // Spatially coherent phase offset so the bloom travels across a meadow instead of flickering per cell.
        float BloomPhase(Vector2 p) =>
            Mathf.PerlinNoise(_noiseOff.x + 37.1f + p.x * 0.09f, _noiseOff.y + 53.7f + p.y * 0.09f) * Mathf.PI * 3f;

        void AllocCells(int n)
        {
            _stage = new float[n]; _burn = new float[n]; _fireT = new float[n];
            _firePatch = new sbyte[n];
            for (int i = 0; i < n; i++) _firePatch[i] = -1;
            _fert = new float[n]; _maxStage = new float[n]; _land = new bool[n]; _shore = new bool[n];
            _height = new float[n]; _bloomPhase = new float[n]; _bloomBaked = new float[n]; _burnBaked = new float[n];
            _counts = new int[n * K]; _quota = new int[n * K];
            _foreignAt = new byte[n * K];
            _cellColor = new Color[n];
            _cellBiome = new byte[n];
            for (int i = 0; i < n; i++) _bloomBaked[i] = 0.5f;
        }

        // Counts the land of every biome; the dominant one answers Biome/BiomeName and the union of the present
        // biomes' species is what counts as native here. Once per grid build, never per frame.
        void RecountBiomes()
        {
            Array.Clear(_biomeCells, 0, _biomeCells.Length);
            if (_cellBiome != null && _land != null)
                for (int i = 0; i < _land.Length; i++)
                {
                    if (!_land[i]) continue;
                    byte b = _cellBiome[i];
                    if (b < _biomeCells.Length) _biomeCells[b]++;
                }
            _biomesPresent = 0;
            _nativeMask = 0UL;
            int best = -1;
            for (int b = 0; b < _biomeCells.Length; b++)
            {
                if (_biomeCells[b] == 0) continue;
                _biomesPresent |= 1 << b;
                _nativeMask |= Biomes.Of(b).mask;
                if (best < 0 || _biomeCells[b] > _biomeCells[best]) best = b;
            }
            if (best < 0)
            {
                best = _surface != null ? _surface.Biome : 0;
                if (best < 0 || best >= Biomes.Count) best = 0;
                _nativeMask = Biomes.Of(best).mask;
            }
            _dominantBiome = best;
        }

        // Land that has just appeared (a merge's guest cells excepted, they bring their own) takes the biome of
        // the nearest cell that has one, so a new rim or a land bridge continues the ground it grew out of
        // instead of snapping back to the host's biome. Two sweeps per round propagate in all four directions.
        void FillUnknownBiomes()
        {
            int unknown = 0, known = 0;
            for (int i = 0; i < _land.Length; i++)
            {
                if (!_land[i]) continue;
                if (_cellBiome[i] >= Biomes.Count) unknown++;
                else known++;
            }
            if (unknown == 0) return;
            if (known == 0)
            {
                byte own = (byte)(_surface != null && _surface.Biome >= 0 && _surface.Biome < Biomes.Count ? _surface.Biome : 0);
                for (int i = 0; i < _land.Length; i++) if (_land[i]) _cellBiome[i] = own;
                return;
            }
            for (int round = 0; round < 64 && unknown > 0; round++)
            {
                int before = unknown;
                for (int j = 0; j < _nz; j++)
                    for (int i = 0; i < _nx; i++)
                        if (TakeNeighbourBiome(i, j)) unknown--;
                for (int j = _nz - 1; j >= 0; j--)
                    for (int i = _nx - 1; i >= 0; i--)
                        if (TakeNeighbourBiome(i, j)) unknown--;
                if (unknown == before) break;
            }
            if (unknown > 0)
            {
                byte own = (byte)(_surface != null && _surface.Biome >= 0 && _surface.Biome < Biomes.Count ? _surface.Biome : 0);
                for (int i = 0; i < _land.Length; i++) if (_land[i] && _cellBiome[i] >= Biomes.Count) _cellBiome[i] = own;
            }
        }

        bool TakeNeighbourBiome(int i, int j)
        {
            int idx = j * _nx + i;
            if (!_land[idx] || _cellBiome[idx] < Biomes.Count) return false;
            if (i > 0 && Take(idx, idx - 1)) return true;
            if (i + 1 < _nx && Take(idx, idx + 1)) return true;
            if (j > 0 && Take(idx, idx - _nx)) return true;
            if (j + 1 < _nz && Take(idx, idx + _nx)) return true;
            return false;
        }

        bool Take(int idx, int from)
        {
            if (!_land[from] || _cellBiome[from] >= Biomes.Count) return false;
            _cellBiome[idx] = _cellBiome[from];
            return true;
        }

        void RebuildGrid()
        {
            Rect b = _surface.LocalBounds;
            Vector2 newOrigin = new Vector2(Mathf.Floor(b.xMin / cellSize) * cellSize, Mathf.Floor(b.yMin / cellSize) * cellSize);
            int nx = Mathf.CeilToInt((b.xMax - newOrigin.x) / cellSize) + 1;
            int nz = Mathf.CeilToInt((b.yMax - newOrigin.y) / cellSize) + 1;

            if (_hasGrid && nx == _nx && nz == _nz && newOrigin == _origin && _pending.Count == 0)
            {
                RefreshGridInPlace();
                return;
            }

            var oStage = _stage; var oBurn = _burn; var oFire = _fireT; var oLand = _land; var oPatch = _firePatch;
            var oBloom = _bloomBaked; var oBurnBaked = _burnBaked; var oBiome = _cellBiome;
            int onx = _nx, onz = _nz; Vector2 oOrigin = _origin;
            bool hadGrid = _hasGrid;

            _origin = newOrigin; _nx = nx; _nz = nz;
            int n = nx * nz;
            AllocCells(n);
            _landCells = 0;

            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int idx = j * nx + i;
                    Vector2 p = CellCenter(i, j);
                    CellProps(idx, _surface.SampleHeight(p));
                    _bloomPhase[idx] = BloomPhase(p);
                    if (!_land[idx]) continue;
                    _landCells++;

                    int oi = -1;
                    if (hadGrid)
                    {
                        int ci = Mathf.FloorToInt((p.x - oOrigin.x) / cellSize);
                        int cj = Mathf.FloorToInt((p.y - oOrigin.y) / cellSize);
                        if (ci >= 0 && cj >= 0 && ci < onx && cj < onz && oLand[cj * onx + ci]) oi = cj * onx + ci;
                    }
                    if (oi >= 0)
                    {
                        _stage[idx] = oStage[oi]; _burn[idx] = oBurn[oi]; _fireT[idx] = oFire[oi];
                        if (oPatch != null) _firePatch[idx] = oPatch[oi];
                        _bloomBaked[idx] = oBloom[oi]; _burnBaked[idx] = oBurnBaked[oi];
                        _cellBiome[idx] = oBiome != null ? oBiome[oi] : BiomeUnset;
                    }
                    else
                    {
                        // Land that appears on an existing grid (land bridge, refloated shore) starts bare and grows in.
                        _stage[idx] = hadGrid ? ReemergeStage : InitStage(p);
                        _cellBiome[idx] = BiomeUnset;
                    }
                    _stage[idx] = Mathf.Min(_stage[idx], _maxStage[idx]);
                }

            // A merge's guest cells: stage, scars and - the point of the whole thing - the guest's biome, but only
            // where the host had no land of its own. The host's ground keeps what grew on it.
            foreach (var imp in _pending)
            {
                int idx = CellIndex(imp.pos);
                if (idx < 0 || !_land[idx]) continue;
                _stage[idx] = Mathf.Min(imp.stage, _maxStage[idx]);
                _burn[idx] = imp.burn;
                _fireT[idx] = imp.fireT;
                if (_cellBiome[idx] >= Biomes.Count && imp.biome < Biomes.Count) _cellBiome[idx] = imp.biome;
            }
            _pending.Clear();
            FillUnknownBiomes();

            ComputeShore();
            RecellPlants();
            FinishGrid();
        }

        public bool IsShoreCell(Vector2 localPos)
        {
            if (!_hasGrid) return false;
            int idx = CellIndex(localPos);
            return idx >= 0 && _shore[idx];
        }

        void ComputeShore()
        {
            for (int j = 0; j < _nz; j++)
                for (int i = 0; i < _nx; i++)
                {
                    int idx = j * _nx + i;
                    bool shore = false;
                    if (_land[idx])
                        for (int dj = -1; dj <= 1 && !shore; dj++)
                            for (int di = -1; di <= 1 && !shore; di++)
                            {
                                int ni = i + di, nj = j + dj;
                                shore = ni < 0 || nj < 0 || ni >= _nx || nj >= _nz || !_land[nj * _nx + ni];
                            }
                    _shore[idx] = shore;
                }
        }

        // Walks downhill from a shore cell's centre (with a little sideways scatter) until the ground lies in
        // [minH, maxH], at most 1.5 cells away: the beach and the waterline are thin bands below the rim.
        bool TryDownhill(Vector2 c, float minH, float maxH, out Vector2 pos)
        {
            const float e = 0.3f;
            pos = c;
            Vector2 grad = new Vector2(
                _surface.SampleHeight(c + new Vector2(e, 0f)) - _surface.SampleHeight(c - new Vector2(e, 0f)),
                _surface.SampleHeight(c + new Vector2(0f, e)) - _surface.SampleHeight(c - new Vector2(0f, e)));
            if (grad.sqrMagnitude < 1e-8f) return false;
            Vector2 down = -grad.normalized;
            Vector2 side = new Vector2(-down.y, down.x) * (Rand(-0.45f, 0.45f) * cellSize);
            for (int t = 0; t < 12; t++)
            {
                pos = c + down * ((0.1f + 0.125f * t) * cellSize) + side;
                float h = _surface.SampleHeight(pos);
                if (h >= minH && h <= maxH) return true;
            }
            return false;
        }

        // Sinking bumps the Version every ~2 s without changing the grid extents: recompute land/fertility in
        // place instead of reallocating, keep every stage that is still above water.
        void RefreshGridInPlace()
        {
            _landCells = 0;
            for (int j = 0; j < _nz; j++)
                for (int i = 0; i < _nx; i++)
                {
                    int idx = j * _nx + i;
                    bool was = _land[idx];
                    CellProps(idx, _surface.SampleHeight(CellCenter(i, j)));
                    if (!_land[idx])
                    {
                        _stage[idx] = 0f; _burn[idx] = 0f; _fireT[idx] = 0f;
                        continue;
                    }
                    _landCells++;
                    // Refloated ground takes its biome from the land around it, like every new cell.
                    if (!was) { _stage[idx] = ReemergeStage; _burn[idx] = 0f; _fireT[idx] = 0f; _cellBiome[idx] = BiomeUnset; }
                    _stage[idx] = Mathf.Min(_stage[idx], _maxStage[idx]);
                }
            FillUnknownBiomes();
            ComputeShore();
            Array.Clear(_counts, 0, _counts.Length);
            RecellPlants();
            FinishGrid();
        }

        void RecellPlants()
        {
            for (int k = _plants.Count - 1; k >= 0; k--)
            {
                var p = _plants[k];
                p.cell = CellIndex(p.pos);
                if (p.cell < 0 || !_land[p.cell]) { p.dying = true; p.cell = -1; continue; }
                if (!p.dying) _counts[p.cell * K + p.slot]++;
            }
        }

        void FinishGrid()
        {
            RecountBiomes();
            GridBuilds++;
            _hasGrid = true;
            _gridVersion = _surface.Version;
            _dirty = true;
            _tintDirty = true;
            // A rebuilt grid means the island mesh was rebuilt too (its colours reset), so the tint
            // must not wait for the regular interval.
            _tintTimer = tintInterval;
        }

        // ------------------------------------------------------------ simulation

        public void Step(float dt)
        {
            if (_surface == null) _surface = GetComponent<IIslandSurface>();
            if (_surface == null || !_hasGrid) return;
            // A merge that LateUpdate has not caught up with yet (a headless run, a disabled component) must not
            // survive into a second frame of untinted ground.
            if (_mergeRefresh) RefreshAfterMerge();
            // The island that pushes the wind this frame is the one whose sliders the push uses.
            if (Time.frameCount != _windFrame) ApplyWindSettings();
            PushWind();

            float dist = LifeLod.Distance(transform.position);
            if (_vegRenderer != null) _vegRenderer.enabled = dist < hideDistance;
            Tier = LifeLod.Tier(dist, detailDistance, simDistance);

            _tickTimer += dt;
            float interval = Tier == LifeTier.Far ? tickInterval * farTickFactor : tickInterval;
            if (_tickTimer >= interval)
            {
                float ldt = _tickTimer * timeScale;
                _tickTimer = 0f;
                Tick(ldt);
            }

            if (Tier == LifeTier.Far) return;

            // Beyond detailDistance the growth animation is sub-pixel, so those islands refresh at half rate.
            float lod = Tier == LifeTier.Mid ? 2f : 1f;
            _tintTimer += dt;
            if (_tintDirty && _tintTimer >= tintInterval * lod) ApplyTint();
            _bloomTimer += dt;
            if (_bloomTimer >= bloomInterval * lod)
            {
                _bloomTimer = 0f;
                EvaluateBloom();
            }
            // Growth and fade lerps only matter when they reach the mesh, so they advance by the accumulated
            // time right before a rebuild instead of every frame.
            _meshTimer += dt;
            if (_meshTimer >= meshInterval * lod)
            {
                AnimatePlants(_meshTimer);
                _meshTimer = 0f;
                if (_dirty)
                {
                    RebuildVegetationMesh();
                    _dirty = false;
                }
            }
        }

        public void Tick(float ldt)
        {
            if (_surface.Version != _gridVersion) RebuildGrid();
            Ticks++;
            _age += ldt;
            if (seasonPeriod > 0f) _season = Mathf.Repeat(_season + ldt / (seasonPeriod * Mathf.Max(timeScale, 1e-3f)), 1f);
            SeasonReference = _season;

            float storm = _surface.StormIntensity;
            if (storm > 0f && _landCells > 0)
            {
                float realDt = ldt / Mathf.Max(0.01f, timeScale);
                if (Rand() < lightningRate * storm * (_surface.LandArea / 50f) * realDt) Strike();
            }
            float spreadDt = ldt * (1f - fireRainDamping * Mathf.Clamp01(storm));
            _spreadTo.Clear();

            for (int j = 0; j < _nz; j++)
                for (int i = 0; i < _nx; i++)
                {
                    int idx = j * _nx + i;
                    if (!_land[idx]) continue;

                    if (_fireT[idx] > 0f)
                    {
                        _fireT[idx] -= ldt;
                        _burn[idx] = 1f;
                        int pi = _firePatch[idx];
                        if (pi >= 0 && _patches[pi].burned < _patches[pi].budget) SpreadFrom(i, j, pi, spreadDt);
                        if (_fireT[idx] <= 0f) { _stage[idx] = 0.02f; _burn[idx] = 0.9f; _firePatch[idx] = -1; }
                        continue;
                    }

                    float grow = growthRate * _fert[idx] * ldt * (1f + _burn[idx]);
                    _stage[idx] = Mathf.Min(_stage[idx] + grow, _maxStage[idx]);
                    _burn[idx] = Mathf.Max(0f, _burn[idx] - burnFadeRate * ldt);
                }
            // Lit after the sweep, so a fresh cell never spreads in the tick that lit it (the old in-sweep ignition
            // let a fire run several cells towards +x/+z within one tick).
            for (int s = 0; s < _spreadTo.Count; s++)
            {
                int n = _spreadTo[s] / MaxFirePatches, pi = _spreadTo[s] % MaxFirePatches;
                if (_fireT[n] > 0f || _patches[pi].burned >= _patches[pi].budget) continue;
                Ignite(n, pi);
            }

            SyncPlants(false, ldt);
            if (_foreignCount > 0 && foreignSpreadInterval > 0f)
            {
                _spreadTimer += ldt;
                if (_spreadTimer >= foreignSpreadInterval)
                {
                    _spreadTimer = 0f;
                    SpreadForeign();
                }
            }
            _tintDirty = true;
        }

        void Strike()
        {
            int idx = -1;
            for (int tries = 0; tries < 24 && idx < 0; tries++)
            {
                int c = _rnd.Next(_land.Length);
                if (_land[c]) idx = c;
            }
            if (idx < 0) return;
            Strike(idx);
        }

        void Strike(int idx)
        {
            StrikeCount++;
            _strikePos = CellCenter(idx % _nx, idx / _nx) + new Vector2(Rand(-0.4f, 0.4f), Rand(-0.4f, 0.4f)) * cellSize;
            _strikeT = strikeDuration;
            if (Application.isPlaying)
            {
                Vector3 w = transform.TransformPoint(_strikePos.x, Mathf.Max(0f, _surface.SampleHeight(_strikePos)), _strikePos.y);
                LifeEnvironment.ReportLightning(w, 1f);
            }
            if (_fireT[idx] <= 0f && _stage[idx] > 0.3f) { Ignite(idx, OpenPatch(idx)); IgnitionCount++; }
            _dirty = true;
            _meshTimer = meshInterval;
        }

        int OpenPatch(int idx)
        {
            int pi = _nextPatch;
            _nextPatch = (_nextPatch + 1) % MaxFirePatches;
            // A slot is only reused 16 strikes later; whatever of its old fire still burns just burns out in place.
            for (int c = 0; c < _firePatch.Length; c++) if (_firePatch[c] == pi) _firePatch[c] = -1;
            int lo = Mathf.Max(1, firePatchCells.x), hi = Mathf.Max(lo, firePatchCells.y);
            int budget = Mathf.Clamp(Mathf.RoundToInt(firePatchShare * _landCells * Rand(0.7f, 1.3f)), lo, hi);
            Vector2 w = LifeEnvironment.Wind;
            Vector3 wl = transform.InverseTransformDirection(new Vector3(w.x, 0f, w.y));
            Vector2 dir = new Vector2(wl.x, wl.z);
            float k = fireWindStretch * Mathf.Clamp01(w.magnitude / 1.5f);
            _patches[pi] = new FirePatch
            {
                origin = CellCenter(idx % _nx, idx / _nx),
                wind = dir.sqrMagnitude > 1e-8f ? dir.normalized * k : Vector2.zero,
                radius = cellSize * (0.6f + Mathf.Sqrt(1.4f * budget / Mathf.PI)),
                budget = budget,
                burned = 0,
            };
            return pi;
        }

        // Grass (stage 0.3) burns grassFire.x as long as woods (0.9+) and catches grassFire.y times as fast.
        float WoodShare(int idx) => Smooth(0.3f, 0.9f, _stage[idx]);

        void Ignite(int idx, int pi)
        {
            _fireT[idx] = fireDuration * Mathf.Lerp(grassFire.x, 1.1f, WoodShare(idx)) * Rand(0.8f, 1.2f);
            _burn[idx] = 1f;
            _firePatch[idx] = (sbyte)pi;
            if (pi >= 0) _patches[pi].burned++;
        }

        // 1 at the strike, falling to 0 at the patch rim; downwind the rim lies (1 + stretch) times further out.
        float PatchReach(in FirePatch p, Vector2 pos)
        {
            Vector2 o = pos - p.origin;
            float k = p.wind.magnitude;
            if (k > 1e-4f)
            {
                Vector2 dir = p.wind / k;
                float a = Vector2.Dot(o, dir);
                o += dir * ((a > 0f ? a / (1f + k) : a * (1f + k)) - a);
            }
            float q = o.sqrMagnitude / Mathf.Max(1e-4f, p.radius * p.radius);
            return q >= 1f ? 0f : 1f - q;
        }

        void SpreadFrom(int i, int j, int pi, float dt)
        {
            if (!(dt > 0f)) return;
            for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    if (di == 0 && dj == 0) continue;
                    int ni = i + di, nj = j + dj;
                    if (ni < 0 || nj < 0 || ni >= _nx || nj >= _nz) continue;
                    int n = nj * _nx + ni;
                    if (!_land[n] || _fireT[n] > 0f || _stage[n] <= 0.3f || _burn[n] >= 0.5f) continue;
                    float reach = PatchReach(_patches[pi], CellCenter(ni, nj));
                    if (reach <= 0f) continue;
                    float rate = fireSpreadRate * reach * Mathf.Lerp(grassFire.y, 1f, WoodShare(n)) * (di != 0 && dj != 0 ? 0.5f : 1f);
                    if (Rand() < 1f - Mathf.Exp(-rate * dt)) _spreadTo.Add(n * MaxFirePatches + pi);
                }
        }

        static readonly int[] Desired = new int[K];

        // Succession only grows, with one exception: bush and tree counts are non-decreasing in the stage (a
        // tree dies only by fire or drowning), but grass and flowers are generated AND removed like trees are
        // added - grass peaks in plains/shrub and falls to 0 under woods/old growth, flowers peak in plains -
        // because the canopy replaces them. Removal goes through the quota path below and fades over ~10 s.
        // Palms and reeds (Phase 5) follow the tree rule: island caps only limit spawns.
        void SyncPlants(bool initial, float ldt)
        {
            Array.Clear(_quota, 0, _quota.Length);
            var desired = Desired;
            bool changed = false;

            int palms = 0, reeds = 0, verts = 0, foreign = 0;
            for (int p = 0; p < _plants.Count; p++)
            {
                var pl = _plants[p];
                if (pl.dying) continue;
                verts += LifeMeshes.GetTemplate(pl.kind, pl.variant).vertices.Length;
                if (pl.slot == SlotPalm) palms++;
                else if (pl.slot == SlotReed) reeds++;
                if (!NativeHere(pl.kind)) foreign++;
            }
            _liveVerts = verts;
            if (foreign > 0 || _foreignCount > 0)
            {
                Array.Clear(_foreignAt, 0, _foreignAt.Length);
                if (foreign > 0)
                    for (int p = 0; p < _plants.Count; p++)
                    {
                        var pl = _plants[p];
                        if (!pl.dying && pl.cell >= 0 && !NativeHere(pl.kind)) _foreignAt[pl.cell * K + pl.slot] = (byte)(1 + (int)pl.kind);
                    }
            }
            _foreignCount = foreign;
            float density = Mathf.Clamp01(maxVegetationVerts / (float)Mathf.Max(1, _landCells * fullCellVerts));

            for (int j = 0; j < _nz; j++)
                for (int i = 0; i < _nx; i++)
                {
                    int idx = j * _nx + i;
                    if (!_land[idx]) continue;

                    if (_fireT[idx] > 0f)
                    {
                        for (int k = 0; k < K; k++) _quota[idx * K + k] = _counts[idx * K + k];
                        continue;
                    }

                    // The cell's own biome decides what belongs here, so absorbed land keeps growing its own.
                    var biome = Biomes.Of(_cellBiome[idx]);
                    DesiredCounts(biome, idx, density, desired);
                    if (palms >= maxPalms) desired[SlotPalm] = 0;
                    if (reeds >= maxReeds) desired[SlotReed] = 0;
                    if (Settlement != null && Settlement.Clears(CellCenter(i, j))) { desired[SlotGrass] = Mathf.Max(desired[SlotGrass], 1); desired[SlotBush] = 0; desired[SlotTree] = 0; }

                    for (int k = 0; k < K; k++)
                    {
                        int diff = desired[k] - _counts[idx * K + k];
                        if (diff > 0)
                        {
                            for (int d = 0; d < diff && _plants.Count < maxPlants; d++)
                            {
                                if (!TryKindFor(biome, k, idx, i, j, !initial, out LifeKind spawnKind)) break;
                                if (!Spawn(spawnKind, k, idx, i, j, initial)) continue;
                                changed = true;
                                if (k == SlotPalm) palms++;
                                else if (k == SlotReed) reeds++;
                            }
                        }
                        else if (diff < 0 && k < SlotPalm)
                        {
                            _quota[idx * K + k] = -diff;
                        }
                    }
                }

            for (int p = 0; p < _plants.Count; p++)
            {
                var pl = _plants[p];
                if (pl.dying || pl.cell < 0) continue;
                int q = pl.cell * K + pl.slot;
                if (_fireT[pl.cell] > 0f)
                {
                    pl.dying = true;
                    pl.burnT = 1f;
                    pl.fadeRate = 0.7f;
                    _counts[q]--;
                    changed = true;
                }
                else if (_quota[q] > 0 && NativeHere(pl.kind))
                {
                    // Only native plants make way for the canopy: a collected species stays where it stands.
                    _quota[q]--;
                    pl.dying = true;
                    pl.fadeRate = CanopyFadeRate;
                    _counts[q]--;
                    changed = true;
                }
                else
                {
                    pl.scaleTarget = TargetScale(pl, _stage[pl.cell]);
                    if (pl.maturity < 1f && ldt > 0f)
                    {
                        pl.maturity = Mathf.Min(1f, pl.maturity + ldt / Mathf.Max(treeGrowTime, 1e-3f));
                        if (pl.maturity - pl.bakedMaturity >= growthRebuildStep || pl.maturity >= 1f)
                        {
                            pl.bakedMaturity = pl.maturity;
                            changed = true;
                        }
                    }
                }
            }
            // Scale-target drift is picked up by AnimatePlants, which only dirties the mesh once a
            // plant actually moves; spawns and deaths need the rebuild regardless.
            if (changed) _dirty = true;
        }

        // What a land cell should hold of every role at its stage: the succession rules, scaled by the biome.
        void DesiredCounts(BiomeSpec biome, int idx, float density, int[] desired)
        {
            float s = _stage[idx];
            float f = Mathf.Clamp01(_fert[idx] * 1.4f);
            float fg = Mathf.Clamp01(_fert[idx] * 2f);
            uint hash = CellHash(idx);
            float frac = ((hash >> 4) & 0xFF) / 256f;
            bool shore = _shore[idx];
            desired[SlotGrass] = Thin(3f * Smooth(0f, 0.12f, s) * (1f - biome.canopyClears * Smooth(0.6f, 0.95f, s)) * fg, biome.groundDensity, density, frac);
            desired[SlotFlower] = (hash >> 8) % (uint)biome.flowerEvery == 0 && s > 0.12f && s < biome.flowerMaxStage ? 1 : 0;
            desired[SlotBush] = Thin(1.4f * Smooth(0.2f, 0.55f, s) * f, biome.shrubDensity, density, frac);
            desired[SlotTree] = Thin(2f * Smooth(0.5f, 1.15f, s) * f, biome.treeDensity, density, Mathf.Repeat(frac + 0.37f, 1f));
            desired[SlotPalm] = biome.shorePalms && shore && s > 0.08f && (hash >> 12) % 3 == 1 ? 1 : 0;
            desired[SlotReed] = biome.shoreReeds && shore && s > 0.05f && (hash >> 16) % 2 == 0 ? 1 : 0;
        }

        // The species a free place of the role goes to: a foreign one growing in the cell or next to it takes it
        // with foreignReseedChance, otherwise the biome decides (trees by a coarse noise so stands form, the rest
        // by the cell hash).
        bool TryKindFor(BiomeSpec biome, int role, int idx, int ci, int cj, bool reseed, out LifeKind kind)
        {
            if (reseed && _foreignCount > 0 && foreignReseedChance > 0f)
            {
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        int ni = ci + di, nj = cj + dj;
                        if (ni < 0 || nj < 0 || ni >= _nx || nj >= _nz) continue;
                        byte f = _foreignAt[(nj * _nx + ni) * K + role];
                        if (f == 0) continue;
                        if (Rand() < foreignReseedChance) { kind = (LifeKind)(f - 1); return true; }
                        di = dj = 2;
                    }
            }
            float r;
            if (role == SlotTree)
            {
                Vector2 c = CellCenter(ci, cj);
                r = Mathf.Repeat(Mathf.PerlinNoise(_noiseOff.x + 71.3f + c.x * 0.13f, _noiseOff.y + 19.7f + c.y * 0.13f) * 2.5f, 1f);
            }
            else r = ((CellHash(idx) >> 18) & 0xFF) / 256f;
            return biome.TryPick(role, r, out kind);
        }

        // One foreign plant seeds a neighbour cell that suits its role: into a free place, or - while its species is
        // still rare here - in place of a native plant of the same role. Only ever from a living foreign plant.
        void SpreadForeign()
        {
            Plant source = null;
            int seen = 0;
            for (int p = 0; p < _plants.Count; p++)
            {
                var pl = _plants[p];
                if (pl.dying || pl.cell < 0 || NativeHere(pl.kind) || pl.slot >= SlotPalm) continue;
                seen++;
                if (_rnd.Next(seen) == 0) source = pl;
            }
            if (source == null) return;
            int ci = source.cell % _nx + _rnd.Next(3) - 1, cj = source.cell / _nx + _rnd.Next(3) - 1;
            if (ci < 0 || cj < 0 || ci >= _nx || cj >= _nz) return;
            int idx = cj * _nx + ci;
            if (!_land[idx] || _fireT[idx] > 0f) return;
            if (Settlement != null && source.slot >= SlotBush && Settlement.Clears(CellCenter(ci, cj))) return;
            float density = Mathf.Clamp01(maxVegetationVerts / (float)Mathf.Max(1, _landCells * fullCellVerts));
            DesiredCounts(Biomes.Of(_cellBiome[idx]), idx, density, Desired);
            int role = source.slot;
            if (Desired[role] <= 0) return;
            if (_counts[idx * K + role] >= Desired[role])
            {
                if (CountOfKind(source.kind) >= foreignStand) return;
                Plant native = null;
                for (int p = 0; p < _plants.Count; p++)
                {
                    var pl = _plants[p];
                    if (!pl.dying && pl.cell == idx && pl.slot == role && NativeHere(pl.kind)) { native = pl; break; }
                }
                if (native == null) return;
                native.dying = true;
                native.fadeRate = CanopyFadeRate;
                _counts[idx * K + role]--;
                _liveVerts -= LifeMeshes.GetTemplate(native.kind, native.variant).vertices.Length;
            }
            if (!Spawn(source.kind, role, idx, ci, cj, false)) return;
            ForeignSpreads++;
            _dirty = true;
        }

        // Below full density the count is rounded stochastically per cell (mean = x * density), so a big island
        // thins evenly; at full density the old round() keeps every existing island exactly as it was.
        static int Thin(float x, float density, float frac) =>
            density < 1f ? Mathf.FloorToInt(x * density + frac) : Mathf.RoundToInt(x);

        // A biome whose role density is not 1 (sparse savanna trees, lush tropical shrubs) rounds the same way.
        static int Thin(float x, float biomeDensity, float density, float frac) =>
            biomeDensity == 1f ? Thin(x, density, frac) : Mathf.FloorToInt(x * biomeDensity * Mathf.Min(density, 1f) + frac);

        static float TargetScale(Plant p, float stage)
        {
            if (p.kind == LifeKind.Palm) return p.jitter * (p.slot == SlotTree ? 1.15f : 1f);
            if (p.kind == LifeKind.TermiteMound) return p.jitter;
            switch (p.slot)
            {
                // Old growth (stage past 1.0) carries a slightly larger canopy on top of the stage scaling.
                case SlotTree: return Mathf.Lerp(0.35f, 1.3f, Smooth(0.5f, 1.3f, stage)) * (1f + 0.08f * Smooth(1f, 1.15f, stage)) * p.jitter;
                case SlotBush: return Mathf.Lerp(0.55f, 1.2f, Smooth(0.2f, 0.9f, stage)) * p.jitter;
                default: return p.jitter;
            }
        }

        bool Spawn(LifeKind kind, int slot, int cellIdx, int ci, int cj, bool initial)
        {
            int variant = kind == LifeKind.Flower
                ? (int)((CellHash(cellIdx) >> 20) % LifeMeshes.FlowerColours) + LifeMeshes.FlowerColours * (int)((CellHash(cellIdx) >> 24) % LifeMeshes.FlowerShapes)
                : _rnd.Next(LifeMeshes.Variants);
            int tplVerts = LifeMeshes.GetTemplate(kind, variant).vertices.Length;
            if (_liveVerts + tplVerts > maxVegetationVerts) return false;

            Vector2 c = CellCenter(ci, cj);
            Vector2 pos = c;
            bool ok = false;
            if (slot == SlotPalm) ok = TryDownhill(c, palmMinHeight, palmMaxHeight, out pos);
            else if (slot == SlotReed) ok = TryDownhill(c, -0.03f, reedMaxHeight, out pos);
            else
                for (int t = 0; t < 4 && !ok; t++)
                {
                    pos = c + new Vector2(Rand(-0.5f, 0.5f), Rand(-0.5f, 0.5f)) * cellSize;
                    float h = _surface.SampleHeight(pos);
                    ok = h > 0.15f && h < 3.2f;
                }
            if (!ok) return false;

            bool woody = slot == SlotTree || slot == SlotPalm;
            var pl = new Plant
            {
                kind = kind,
                slot = slot,
                variant = variant,
                pos = pos,
                yaw = Rand(0f, 360f),
                jitter = Rand(0.85f, 1.2f),
                phase = Rand(0f, 6.2832f),
                cell = cellIdx,
                growth = initial ? 1f : 0f,
                maturity = initial || !woody ? 1f : 0f,
                bakedMaturity = initial || !woody ? 1f : 0f,
                shoot = kind == LifeKind.Grass && !initial && _burn[cellIdx] > 0.25f,
            };
            pl.scaleTarget = TargetScale(pl, _stage[cellIdx]);
            pl.scale = initial ? pl.scaleTarget : 0f;
            _plants.Add(pl);
            _counts[cellIdx * K + slot]++;
            _liveVerts += tplVerts;
            return true;
        }

        void AnimatePlants(float dt)
        {
            bool changed = false;
            if (_strikeT > 0f) { _strikeT -= dt; changed = true; }
            for (int i = _plants.Count - 1; i >= 0; i--)
            {
                var p = _plants[i];
                if (p.dying)
                {
                    p.fade -= dt * p.fadeRate;
                    changed = true;
                    if (p.fade <= 0f)
                    {
                        _plants[i] = _plants[_plants.Count - 1];
                        _plants.RemoveAt(_plants.Count - 1);
                    }
                    continue;
                }
                if (p.growth < 1f) { p.growth = Mathf.Min(1f, p.growth + dt * 0.7f); changed = true; }
                float goal = p.scaleTarget * (p.growth * p.growth * (3f - 2f * p.growth));
                if (Mathf.Abs(goal - p.scale) > 0.002f)
                {
                    p.scale = Mathf.Lerp(p.scale, goal, 1f - Mathf.Exp(-3f * dt));
                    changed = true;
                }
            }
            if (changed) _dirty = true;
        }

        float BloomAngle => bloomPeriod > 0f ? _age / (bloomPeriod * Mathf.Max(timeScale, 1e-3f)) * Mathf.PI * 2f : 0f;

        // Per-cell bloom and scar factors the mesh should show; a rebuild is only requested when a cell that
        // holds the affected plants moved more than the step, or the season moved seasonRebuildStep.
        void EvaluateBloom()
        {
            if (!_hasGrid) return;
            bool changed = false;
            float a = BloomAngle;
            for (int idx = 0; idx < _land.Length; idx++)
            {
                if (!_land[idx]) continue;
                float b = 0.5f + 0.5f * Mathf.Sin(a + _bloomPhase[idx]);
                if (Mathf.Abs(b - _bloomBaked[idx]) > bloomRebuildStep)
                {
                    _bloomBaked[idx] = b;
                    if (_counts[idx * K + SlotFlower] > 0) changed = true;
                }
                float bu = _burn[idx];
                if (Mathf.Abs(bu - _burnBaked[idx]) > bloomRebuildStep)
                {
                    _burnBaked[idx] = bu;
                    if (_counts[idx * K + SlotGrass] > 0) changed = true;
                }
            }
            if (Mathf.Abs(_season - _bakedSeason) > seasonRebuildStep)
            {
                _bakedSeason = _season;
                changed = true;
            }
            if (changed) _dirty = true;
        }

        // ------------------------------------------------------------- rendering

        void EnsureVegObject()
        {
            if (_vegGo != null && _vegGo.transform.parent == transform) return;
            _vegGo = null;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (child.name != VegObjectName) continue;
                if (_vegGo == null) _vegGo = child;
                else if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
            if (_vegGo == null)
            {
                _vegGo = new GameObject(VegObjectName);
                _vegGo.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                _vegGo.transform.SetParent(transform, false);
                _vegGo.AddComponent<MeshFilter>();
                var mr = _vegGo.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            _vegFilter = _vegGo.GetComponent<MeshFilter>();
            _vegRenderer = _vegGo.GetComponent<MeshRenderer>();
            _vegRenderer.sharedMaterial = VegetationMaterial;
            // After a domain reload the child still references the previous mesh; reuse it rather than leak it.
            if (_vegMesh == null) _vegMesh = _vegFilter.sharedMesh;
            if (_vegMesh == null) _vegMesh = new Mesh { name = "Vegetation", hideFlags = HideFlags.DontSave };
            _vegFilter.sharedMesh = _vegMesh;
        }

        // Drift/Vegetation with the Poly Haven detail arrays (Textures/Plants/Resources/DriftVegetation.mat, loaded
        // from Resources because streamed islands get this component at runtime); the flat Drift/VertexColor
        // material if it is missing.
        const string VegetationMaterialPath = "DriftVegetation";
        static Material _vegetationMaterial;
        public static Material VegetationMaterial
        {
            get
            {
                if (_vegetationMaterial == null)
                {
                    _vegetationMaterial = Resources.Load<Material>(VegetationMaterialPath);
                    if (_vegetationMaterial == null)
                    {
                        Debug.LogWarning("IslandLifeSystem: Resources/" + VegetationMaterialPath + " not found, vegetation is drawn flat.");
                        _vegetationMaterial = LifeMeshes.Material;
                    }
                }
                return _vegetationMaterial;
            }
        }

        static readonly Color Char = new Color(0.07f, 0.06f, 0.05f).linear;
        static readonly Color ShootColour = new Color(0.8f, 1.3f, 0.65f);

        // Season keys (multipliers) at 0 spring, 0.25 summer, 0.5 autumn, 0.75 late autumn, wrapping back.
        static readonly Color[] SeasonGround = { new Color(1.0f, 1.06f, 0.92f), new Color(0.93f, 1.0f, 0.86f), new Color(1.16f, 1.02f, 0.76f), new Color(1.06f, 0.98f, 0.84f) };
        static readonly Color[] SeasonTree = { new Color(1.0f, 1.06f, 0.96f), new Color(0.9f, 1.0f, 0.9f), new Color(1.22f, 0.96f, 0.76f), new Color(1.1f, 0.94f, 0.8f) };
        static readonly Color[] SeasonGrass = { new Color(1.0f, 1.06f, 0.92f), new Color(0.94f, 1.0f, 0.88f), new Color(1.18f, 1.04f, 0.76f), new Color(1.08f, 0.98f, 0.8f) };

        public static Color SeasonColour(Color[] keys, float season, float amplitude)
        {
            float f = Mathf.Repeat(season, 1f) * keys.Length;
            int i = Mathf.FloorToInt(f);
            float t = f - i;
            t = t * t * (3f - 2f * t);
            Color c = Color.Lerp(keys[i % keys.Length], keys[(i + 1) % keys.Length], t);
            return new Color(1f + (c.r - 1f) * amplitude, 1f + (c.g - 1f) * amplitude, 1f + (c.b - 1f) * amplitude, 1f);
        }

        public Color GroundSeasonColour => SeasonColour(SeasonGround, _season, seasonAmplitude);
        public Color TreeSeasonColour => SeasonColour(SeasonTree, _season, seasonAmplitude);

        static readonly Color[] TreeSeasons = new Color[Biomes.Count];
        static readonly Color[] GrassSeasons = new Color[Biomes.Count];
        static readonly Color[] ReedSeasons = new Color[Biomes.Count];

        void RebuildVegetationMesh()
        {
            EnsureVegObject();
            MeshBuilds++;
            Batch.Begin();
            // One season key set per biome: a plant is tinted by the season of the ground it stands on, so the
            // nordic half of a merged island keeps its own muted autumn while the tropical half barely moves.
            for (int b = 0; b < Biomes.Count; b++)
            {
                float amp = seasonAmplitude * Biomes.Of(b).seasonAmplitude;
                TreeSeasons[b] = SeasonColour(SeasonTree, _season, amp);
                GrassSeasons[b] = SeasonColour(SeasonGrass, _season, amp);
                ReedSeasons[b] = Color.Lerp(Color.white, GrassSeasons[b], 0.5f);
            }
            for (int k = 0; k < _plants.Count; k++)
            {
                var p = _plants[k];
                float scale = p.scale * Mathf.Clamp01(p.fade) * plantScale;
                Color cs = Color.white;
                int cell = p.cell;
                int pb = cell >= 0 ? _cellBiome[cell] : _dominantBiome;
                if (pb >= Biomes.Count) pb = _dominantBiome;
                Color treeSeason = TreeSeasons[pb], grassSeason = GrassSeasons[pb], reedSeason = ReedSeasons[pb];
                switch (p.slot)
                {
                    case SlotTree:
                        scale *= Mathf.Lerp(saplingScale, 1f, p.bakedMaturity);
                        if (p.kind != LifeKind.Palm && p.kind != LifeKind.Spruce && p.kind != LifeKind.Baobab) cs = treeSeason;
                        break;
                    case SlotBush:
                        if (p.kind != LifeKind.TermiteMound) cs = treeSeason;
                        break;
                    case SlotPalm:
                        scale *= Mathf.Lerp(saplingScale, 1f, p.bakedMaturity);
                        break;
                    case SlotGrass:
                        if (p.kind == LifeKind.Mushroom) break;
                        cs = grassSeason;
                        if (p.shoot && cell >= 0)
                        {
                            float sh = Mathf.Clamp01(_burnBaked[cell] / 0.9f);
                            scale *= Mathf.Lerp(1f, 0.6f, sh);
                            cs = Color.Lerp(cs, ShootColour, sh);
                        }
                        break;
                    case SlotFlower:
                    {
                        float b = cell >= 0 ? _bloomBaked[cell] : 0.5f;
                        scale *= Mathf.Lerp(0.5f, 1.2f, b);
                        float bright = Mathf.Lerp(0.8f, 1.15f, b);
                        cs = new Color(bright, bright, bright, 1f);
                        break;
                    }
                    case SlotReed:
                        cs = reedSeason;
                        break;
                }
                if (scale < 0.005f) continue;
                var tpl = LifeMeshes.GetTemplate(p.kind, p.variant);
                if (tpl.part == null) tpl.part = PlantModels.Parts(p.kind, tpl);
                float h = _surface.SampleHeight(p.pos);
                if (p.slot == SlotReed) h = Mathf.Max(h, 0f);
                Batch.AddPlant(tpl, new Vector3(p.pos.x, h, p.pos.y), p.yaw, scale, SwayFactor[p.slot], p.phase, cs, Char, p.burnT);
            }
            if (_strikeT > 0f)
            {
                float h = Mathf.Max(0f, _surface.SampleHeight(_strikePos));
                Batch.Add(LifeMeshes.Bolt, new Vector3(_strikePos.x, h, _strikePos.y), 0f, 1f);
            }
            Batch.Apply(_vegMesh);
        }

        void ApplyTint()
        {
            if (_hasGrid)
            {
                for (int b = 0; b < _groundSeasons.Length; b++)
                    _groundSeasons[b] = SeasonColour(SeasonGround, _season, seasonAmplitude * Biomes.Of(b).seasonAmplitude);
                for (int i = 0; i < _cellColor.Length; i++) _cellColor[i] = CellColor(i);
                BlendBiomeBorders();
            }
            if (_hasGrid) _surface.ApplyGroundTint(_cellColor, _nx, _nz, _origin, cellSize);
            else _surface.ApplyGroundTint(WhiteCell, 1, 1, Vector2.zero, 1f);
            _tintDirty = false;
            _tintTimer = 0f;
            _mergeRefresh = false;
        }

        // Where two biomes meet, a cell's colour is averaged with its four neighbours' (own colour weighted
        // double) so the palettes wash into each other instead of drawing a checkerboard edge - TintAt's bilinear
        // filter then spreads the step over two cells. Only cells on a border pay for it, and only on the tint
        // interval, so a single-biome island is bit for bit what it was.
        void BlendBiomeBorders()
        {
            if (_biomesPresent == 0 || (_biomesPresent & (_biomesPresent - 1)) == 0) return;
            for (int j = 0; j < _nz; j++)
                for (int i = 0; i < _nx; i++)
                {
                    int idx = j * _nx + i;
                    if (!_land[idx]) continue;
                    byte b = _cellBiome[idx];
                    bool border = (i > 0 && _land[idx - 1] && _cellBiome[idx - 1] != b)
                        || (i + 1 < _nx && _land[idx + 1] && _cellBiome[idx + 1] != b)
                        || (j > 0 && _land[idx - _nx] && _cellBiome[idx - _nx] != b)
                        || (j + 1 < _nz && _land[idx + _nx] && _cellBiome[idx + _nx] != b);
                    if (!border) continue;
                    Color acc = _cellColor[idx] * 2f;
                    float w = 2f;
                    if (i > 0 && _land[idx - 1]) { acc += CellColor(idx - 1); w += 1f; }
                    if (i + 1 < _nx && _land[idx + 1]) { acc += CellColor(idx + 1); w += 1f; }
                    if (j > 0 && _land[idx - _nx]) { acc += CellColor(idx - _nx); w += 1f; }
                    if (j + 1 < _nz && _land[idx + _nx]) { acc += CellColor(idx + _nx); w += 1f; }
                    _cellColor[idx] = acc / w;
                }
        }

        static readonly Color Burnt = new Color(0.16f, 0.13f, 0.11f);

        readonly Color[] _groundSeasons = new Color[Biomes.Count];

        // The cell's OWN biome's ground palette over the succession stage (BiomeSpec.GroundColour),
        // snow on the high ground of nordic land - whichever island that land now belongs to.
        Color CellColor(int idx)
        {
            if (!_land[idx]) return Color.white;
            float s = _stage[idx];
            int bi = _cellBiome[idx];
            if (bi >= Biomes.Count) bi = _dominantBiome;
            var biome = Biomes.Of(bi);
            Color gs = _groundSeasons[bi];
            Color c = biome.GroundColour(s);
            c.r *= gs.r; c.g *= gs.g; c.b *= gs.b;
            if (biome.snowLine > 0f)
            {
                float snow = Mathf.Clamp01((_height[idx] - biome.snowLine) / 0.5f);
                if (biome.snowPatches > 0f && _height[idx] > 0.3f)
                {
                    Vector2 p = CellCenter(idx % _nx, idx / _nx);
                    float drift = Mathf.PerlinNoise(_noiseOff.x + 11.9f + p.x * 0.3f, _noiseOff.y + 83.1f + p.y * 0.3f);
                    snow = Mathf.Max(snow, Smooth(1f - biome.snowPatches - 0.12f, 1f - biome.snowPatches + 0.05f, drift * 1.25f));
                }
                c = Color.Lerp(c, biome.snow, snow);
            }
            if (Settlement != null && Settlement.Clears(CellCenter(idx % _nx, idx / _nx))) c = Color.Lerp(c, IslandSettlementSystem.VillageGround, 0.8f);
            c = Color.Lerp(c, Burnt, Mathf.Clamp01(_burn[idx]));
            c.a = 1f - 0.85f * Mathf.Clamp01(_burn[idx]);
            int ch = Character;
            if (ch == 1) { c.r *= VolcanicTint.r; c.g *= VolcanicTint.g; c.b *= VolcanicTint.b; c.a *= VolcanicTint.a; }
            else if (ch == 3) { c.r *= BarrenTint.r; c.g *= BarrenTint.g; c.b *= BarrenTint.b; }
            return c;
        }

        Color TintAt(Vector2 p)
        {
            if (!_hasGrid) return Color.white;
            float fx = (p.x - _origin.x) / cellSize - 0.5f;
            float fz = (p.y - _origin.y) / cellSize - 0.5f;
            int i0 = Mathf.FloorToInt(fx), j0 = Mathf.FloorToInt(fz);
            float tx = fx - i0, tz = fz - j0;
            Color a = CellColorSafe(i0, j0), b = CellColorSafe(i0 + 1, j0);
            Color c = CellColorSafe(i0, j0 + 1), d = CellColorSafe(i0 + 1, j0 + 1);
            Color r = Color.Lerp(Color.Lerp(a, b, tx), Color.Lerp(c, d, tx), tz);
            return r;
        }

        Color CellColorSafe(int i, int j)
        {
            i = Mathf.Clamp(i, 0, _nx - 1);
            j = Mathf.Clamp(j, 0, _nz - 1);
            return _cellColor[j * _nx + i];
        }

        // -------------------------------------------------------------- merging

        // The critters ride along here (like the herds ride along in Capture/Restore) so Island only has to
        // know the life system.
        public void ShiftLocal(Vector2 delta)
        {
            _origin += delta;
            _strikePos += delta;
            for (int i = 0; i < MaxFirePatches; i++) _patches[i].origin += delta;
            foreach (var p in _plants) p.pos += delta;
            _dirty = true;
            var critters = GetComponent<IslandCrittersSystem>();
            if (critters != null) critters.ShiftLocal(delta);
            var settlement = GetComponent<IslandSettlementSystem>();
            if (settlement != null) settlement.ShiftLocal(delta);
        }

        public void AbsorbFrom(IslandLifeSystem other)
        {
            if (other == null || other == this || !other._hasGrid) return;
            var critters = GetComponent<IslandCrittersSystem>();
            if (critters != null) critters.AbsorbFrom(other.GetComponent<IslandCrittersSystem>());
            var settlement = GetComponent<IslandSettlementSystem>();
            if (settlement != null) settlement.AbsorbFrom(other.GetComponent<IslandSettlementSystem>());

            int palms = 0, reeds = 0;
            foreach (var p in _plants) if (!p.dying) { if (p.slot == SlotPalm) palms++; else if (p.slot == SlotReed) reeds++; }
            int first = _plants.Count;
            Array.Clear(KindScratch, 0, KindScratch.Length);
            foreach (var p in other._plants)
            {
                Vector3 w = other.transform.TransformPoint(p.pos.x, 0f, p.pos.y);
                Vector3 l = transform.InverseTransformPoint(w);
                p.pos = new Vector2(l.x, l.z);
                p.cell = -1;
                // The per-island palm/reed caps hold after a merge like the herd caps do: the surplus guests fade.
                if (!p.dying && p.slot == SlotPalm && ++palms > maxPalms) { p.dying = true; p.fadeRate = 0.8f; }
                else if (!p.dying && p.slot == SlotReed && ++reeds > maxReeds) { p.dying = true; p.fadeRate = 0.8f; }
                else if (!p.dying)
                {
                    _liveVerts += LifeMeshes.GetTemplate(p.kind, p.variant).vertices.Length;
                    if (!NativeHere(p.kind)) { KindScratch[(int)p.kind]++; _foreignCount++; }
                }
                _plants.Add(p);
            }
            other._plants.Clear();
            // Over the vertex budget the guest's plants fade in order of cost to lose: grass and flowers first
            // (they regrow with the stage), then bushes, then trees; the density rule keeps the regrowth sparse.
            for (int pass = 0; pass < 3 && _liveVerts > maxVegetationVerts; pass++)
                for (int i = first; i < _plants.Count && _liveVerts > maxVegetationVerts; i++)
                {
                    var p = _plants[i];
                    if (p.dying) continue;
                    bool pick = pass == 0 ? p.slot == SlotGrass || p.slot == SlotFlower
                        : pass == 1 ? p.slot == SlotBush || p.slot == SlotReed
                        : true;
                    if (!pick) continue;
                    // The budget never takes a collected species down to nothing: its last foreignStand plants stay.
                    if (!NativeHere(p.kind))
                    {
                        if (KindScratch[(int)p.kind] <= foreignStand) continue;
                        KindScratch[(int)p.kind]--;
                    }
                    p.dying = true;
                    p.fadeRate = 0.8f;
                    _liveVerts -= LifeMeshes.GetTemplate(p.kind, p.variant).vertices.Length;
                }

            // The guest's cells ride over with their biome: the land the player just swallowed keeps growing and
            // tinting as what it was, however long the merged island lives on.
            byte guestBiome = (byte)(other._surface != null ? other._surface.Biome : other._dominantBiome);
            for (int j = 0; j < other._nz; j++)
                for (int i = 0; i < other._nx; i++)
                {
                    int idx = j * other._nx + i;
                    if (!other._land[idx]) continue;
                    Vector2 c = other.CellCenter(i, j);
                    Vector3 w = other.transform.TransformPoint(c.x, 0f, c.y);
                    Vector3 l = transform.InverseTransformPoint(w);
                    byte b = other._cellBiome != null && other._cellBiome[idx] < Biomes.Count ? other._cellBiome[idx] : guestBiome;
                    _pending.Add(new Import { pos = new Vector2(l.x, l.z), stage = other._stage[idx], burn = other._burn[idx], fireT = other._fireT[idx], biome = b });
                }
            _gridVersion = -1;
            _dirty = true;
            _mergeRefresh = true;
        }

        // ---------------------------------------------------------- persistence

        // Includes the sibling IslandHerdSystem's herds, so SaveManager / IslandSaveUtil persist both through
        // this one call pair.
        // Null while there is no grid (no surface yet, or an island without land), which the callers treat
        // as "nothing to save".
        public LifeSaveData Capture()
        {
            if (!_hasGrid || _stage == null) return null;
            var d = new LifeSaveData
            {
                originX = _origin.x, originZ = _origin.y, noiseX = _noiseOff.x, noiseZ = _noiseOff.y,
                cellSize = cellSize, nx = _nx, nz = _nz, age = _age, season = _season,
                stage = (float[])_stage.Clone(), burn = (float[])_burn.Clone(), fireT = (float[])_fireT.Clone()
            };
            d.foreign = CaptureForeign();
            d.biomeRuns = CaptureBiomeRuns();
            var herds = GetComponent<IslandHerdSystem>();
            if (herds != null) d.herds = herds.Capture();
            var critters = GetComponent<IslandCrittersSystem>();
            if (critters != null) d.critters = critters.Capture();
            var settlement = GetComponent<IslandSettlementSystem>();
            if (settlement != null) d.settlement = settlement.Capture();
            return d;
        }

        // Run-length encoding of _cellBiome over the whole grid (water cells carry the last biome so they do not
        // cut a run in two): count << 4 | biome. A single-biome island is one run, a merged one a handful per
        // row; the raw byte array would be ~2 JSON characters per cell instead.
        int[] CaptureBiomeRuns()
        {
            if (_cellBiome == null || _cellBiome.Length == 0) return null;
            int runs = 1;
            byte prev = Carrier(0, 0);
            for (int i = 1; i < _cellBiome.Length; i++)
            {
                byte b = Carrier(i, prev);
                if (b != prev) { runs++; prev = b; }
            }
            var data = new int[runs];
            int k = 0, count = 1;
            prev = Carrier(0, 0);
            for (int i = 1; i < _cellBiome.Length; i++)
            {
                byte b = Carrier(i, prev);
                if (b == prev) { count++; continue; }
                data[k++] = count << 4 | prev;
                prev = b;
                count = 1;
            }
            data[k] = count << 4 | prev;
            return data;
        }

        // Water cells carry the previous land cell's value so a rim of sea does not cut every run in two.
        byte Carrier(int i, byte prev)
        {
            byte b = _cellBiome[i];
            if (_land != null && i < _land.Length && !_land[i]) b = BiomeUnset;
            return b < Biomes.Count ? b : (byte)(prev < Biomes.Count ? prev : 0);
        }

        // Old files have no block: every cell takes the island's own seed biome, which is exactly what the
        // island was before the biome became a property of the land.
        void RestoreBiomeRuns(int[] runs)
        {
            byte own = (byte)(_surface != null && _surface.Biome >= 0 && _surface.Biome < Biomes.Count ? _surface.Biome : 0);
            if (runs == null || runs.Length == 0)
            {
                for (int i = 0; i < _cellBiome.Length; i++) _cellBiome[i] = own;
                return;
            }
            int at = 0;
            for (int r = 0; r < runs.Length && at < _cellBiome.Length; r++)
            {
                int count = runs[r] >> 4;
                byte b = (byte)(runs[r] & 0xF);
                if (b >= Biomes.Count) b = own;
                if (count <= 0) continue;
                int end = Mathf.Min(_cellBiome.Length, at + count);
                while (at < end) _cellBiome[at++] = b;
            }
            while (at < _cellBiome.Length) _cellBiome[at++] = own;
        }

        static readonly int[] SaveOrder = { SlotTree, SlotPalm, SlotBush, SlotFlower, SlotGrass, SlotReed };

        // Trees first, then shrubs, flowers and ground cover, so a big merge keeps what takes longest to regrow
        // when maxSavedForeign cuts the list; the spread brings the ground cover back.
        int[] CaptureForeign()
        {
            int total = 0;
            foreach (var p in _plants) if (!p.dying && !NativeHere(p.kind)) total++;
            if (total == 0) return null;
            int n = Mathf.Min(total, maxSavedForeign);
            var data = new int[n * 2];
            int k = 0;
            for (int pass = 0; pass < SaveOrder.Length && k < n; pass++)
            {
                int role = SaveOrder[pass];
                foreach (var p in _plants)
                {
                    if (k >= n) break;
                    if (p.dying || p.slot != role || NativeHere(p.kind)) continue;
                    int qx = Mathf.Clamp(Mathf.RoundToInt((p.pos.x - _origin.x) / 0.02f), 0, 0xFFFF);
                    int qz = Mathf.Clamp(Mathf.RoundToInt((p.pos.y - _origin.y) / 0.02f), 0, 0x7FFF);
                    data[k * 2] = (int)p.kind | (p.variant & 0xFF) << 8 | p.slot << 16;
                    data[k * 2 + 1] = qx | qz << 16;
                    k++;
                }
            }
            return data;
        }

        // Before SyncPlants regrows the natives, so the foreign plants hold their places in the per-cell counts.
        void RestoreForeign(int[] data)
        {
            _foreignCount = 0;
            if (data == null) return;
            for (int k = 0; k + 1 < data.Length; k += 2)
            {
                int kind = data[k] & 0xFF, variant = (data[k] >> 8) & 0xFF, slot = (data[k] >> 16) & 0xFF;
                if (kind >= LifeMeshes.KindCount || slot >= K || !LifeMeshes.IsPlant((LifeKind)kind)) continue;
                var pos = _origin + new Vector2((data[k + 1] & 0xFFFF) * 0.02f, ((data[k + 1] >> 16) & 0x7FFF) * 0.02f);
                int cell = CellIndex(pos);
                if (cell < 0 || !_land[cell] || _plants.Count >= maxPlants) continue;
                var pl = new Plant
                {
                    kind = (LifeKind)kind, slot = slot, variant = variant % LifeMeshes.VariantsOf((LifeKind)kind), pos = pos, cell = cell,
                    yaw = Rand(0f, 360f), jitter = Rand(0.85f, 1.2f), phase = Rand(0f, 6.2832f), growth = 1f
                };
                pl.scaleTarget = TargetScale(pl, _stage[cell]);
                pl.scale = pl.scaleTarget;
                _plants.Add(pl);
                _counts[cell * K + slot]++;
                _foreignCount++;
            }
        }

        public void Restore(LifeSaveData d)
        {
            if (_surface == null) _surface = GetComponent<IIslandSurface>();
            if (_surface == null || d == null || d.stage == null || d.stage.Length == 0 || d.nx * d.nz != d.stage.Length) return;
            _rnd ??= new System.Random(seed);
            _noiseOff = new Vector2(d.noiseX, d.noiseZ);
            cellSize = d.cellSize;
            _origin = new Vector2(d.originX, d.originZ);
            _nx = d.nx; _nz = d.nz;
            _age = d.age;
            _season = Mathf.Repeat(d.season, 1f);
            SeasonReference = _season;
            int n = _nx * _nz;
            AllocCells(n);
            _stage = (float[])d.stage.Clone(); _burn = (float[])d.burn.Clone(); _fireT = (float[])d.fireT.Clone();
            _landCells = 0;
            for (int j = 0; j < _nz; j++)
                for (int i = 0; i < _nx; i++)
                {
                    int idx = j * _nx + i;
                    Vector2 p = CellCenter(i, j);
                    CellProps(idx, _surface.SampleHeight(p));
                    _bloomPhase[idx] = BloomPhase(p);
                    if (_land[idx]) { _landCells++; _stage[idx] = Mathf.Min(_stage[idx], _maxStage[idx]); }
                }
            RestoreBiomeRuns(d.biomeRuns);
            RecountBiomes();
            ComputeShore();
            _plants.Clear();
            _pending.Clear();
            _strikeT = 0f;
            _hasGrid = true;
            _gridVersion = _surface.Version;
            RestoreForeign(d.foreign);
            SyncPlants(true, 0f);
            EvaluateBloom();
            RebuildVegetationMesh();
            ApplyTint();
            _dirty = false;

            var herds = GetComponent<IslandHerdSystem>();
            if (herds != null)
            {
                if (d.herds != null && d.herds.Count > 0) herds.Restore(d.herds);
                else herds.Repopulate();
            }
            var critters = GetComponent<IslandCrittersSystem>();
            if (critters != null)
            {
                if (d.critters != null && d.critters.kind != null && d.critters.kind.Length > 0) critters.Restore(d.critters);
                else critters.Repopulate();
            }
            var settlement = GetComponent<IslandSettlementSystem>();
            if (settlement != null) settlement.Restore(d.settlement);
        }

        public void CatchUp(float lifeSeconds)
        {
            if (lifeSeconds > 0.5f) Simulate(lifeSeconds, 10f);
        }

        // A non-positive or NaN step falls back to one life-second; otherwise the loop would never end.
        public void Simulate(float lifeSeconds, float step = 1f)
        {
            if (!_hasGrid || !(lifeSeconds > 0f)) return;
            if (!(step > 0f)) step = 1f;
            float t = 0f;
            while (t < lifeSeconds)
            {
                Tick(step);
                t += step;
            }
            // Anything that died during the fast-forward is long gone; the slow canopy fade must not linger.
            for (int i = _plants.Count - 1; i >= 0; i--)
                if (_plants[i].dying)
                {
                    _plants[i] = _plants[_plants.Count - 1];
                    _plants.RemoveAt(_plants.Count - 1);
                }
            for (int i = 0; i < 8; i++) AnimatePlants(0.5f);
            EvaluateBloom();
            RebuildVegetationMesh();
            ApplyTint();
            _dirty = false;
            // Herds run on real seconds; the fast-forward hands them the same span in their own time.
            var herds = GetComponent<IslandHerdSystem>();
            if (herds != null) herds.CatchUp(lifeSeconds / Mathf.Max(timeScale, 1e-3f));
            var settlement = GetComponent<IslandSettlementSystem>();
            if (settlement != null) settlement.CatchUp(lifeSeconds / Mathf.Max(timeScale, 1e-3f));
        }
    }
}
