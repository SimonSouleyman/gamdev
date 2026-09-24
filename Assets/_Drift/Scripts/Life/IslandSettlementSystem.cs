using System;
using System.Collections.Generic;
using Drift.Core;
using UnityEngine;

namespace Drift.Life
{
    [Serializable]
    public class SettlementSaveData
    {
        // Per village: stage plus VillageStride floats (centre x, z, age, stage age). Per building: kind, variant,
        // state (BuildingState), village index, the kind it is being upgraded from (-1 = none) plus BuildingStride
        // floats (x, z, yaw, progress, timer, burn). Settlers are derived from the finished homes; only their
        // number is kept for the journal.
        public const int VillageStride = 4;
        public const int BuildingStride = 6;
        // 1 per village that only carries a milestone landmark (Leuchtturm, Hafen) and never grows. Absent in
        // every older file, which reads null = no landmark villages.
        public int[] vLandmark;
        public int[] vStage;
        public float[] v;
        public int[] bKind, bVariant, bState, bVillage, bFrom;
        public float[] b;
        public float foundTimer;
        public int settlers;
        // buildingScale the positions were placed with; 0 = a file from before the small houses (scale 1.5).
        public float scale;
    }

    public enum SettlementStage { None, Camp, Hamlet, Village, Town }
    public enum BuildingState { Site, Done, Upgrading, Burning, Charred, Sinking }
    public enum SettlerState { Idle, Walk, Chop, Farm, Fish, Build, Fetch, Gather, Indoors, Celebrate }

    // Island folk ("Insulaner"): a big, mature island is settled and the settlement grows Lager -> Weiler -> Dorf
    // -> Stadt, every building going up visibly (foundation, scaffold, finished). Pure decoration: nothing here
    // touches island physics. Two meshes per island, both Drift/VertexColor: "Settlement" holds the buildings and
    // is rebuilt only on a construction step, a shape change or a night step; "SettlementFolk" holds settlers,
    // sails, boats, flames and the lighthouse beam and is rebuilt at <= 10 Hz in the near tier only.
    // Time is real seconds (IslandLifeSystem.Simulate hands over life-seconds / timeScale for the offline catch-up).
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class IslandSettlementSystem : MonoBehaviour, Drift.Core.IDockProvider, Drift.Core.IShoreBlocker
    {
        const string StaticName = "Settlement";
        const string FolkName = "SettlementFolk";
        const string GlowName = "SettlementGlow";
        // Walk jitters, ring sizes and margins below were tuned at the original buildingScale 1.5 and follow it.
        const float LegacyScale = 1.5f;
        const float DeckHeight = 0.072f;
        const float DeckLength = 0.6f;

        public int seed = 1;
        // Tiers (LifeLod): near = settlers every frame, folk mesh at folkMeshInterval; mid = settlers every
        // midStepInterval, folk mesh at a third of the rate; far = only StepGrowth every farStepInterval, no
        // settler work and both meshes frozen (the static one carries sails and boats at rest instead).
        public float simDistance = 100f;
        public float hideDistance = 260f;
        public float detailDistance = 45f;
        public float folkMeshInterval = 0.1f;
        public float staticMeshInterval = 0.25f;
        public float midStepInterval = 0.25f;
        public float growthInterval = 0.5f;
        public float farStepInterval = 2f;
        public float farMeshInterval = 10f;

        // Founding: area and mature ground (life stage >= woods on matureFraction of the land cells) for
        // foundingDelay seconds. Later stages need their area, the same mature ground, stageAge seconds in the
        // current stage and most of its plan finished. A stage is never lost again.
        public float foundingArea = 35f;
        public float hamletArea = 45f;
        public float villageArea = 70f;
        public float townArea = 110f;
        public float matureFraction = 0.3f;
        public float foundingDelay = 90f;
        public float hamletAge = 240f;
        public float villageAge = 480f;
        public float townAge = 720f;
        public float planDoneFraction = 0.7f;
        public float buildTimeScale = 0.8f;
        public float rebuildDelay = 120f;
        public float retryInterval = 120f;

        // Building ground: height band, slope (rise over run across the footprint) and a ring of probes at
        // beachMargin that must all stay above beachHeight, which keeps houses off the beach and the rim.
        public float siteMinHeight = 0.3f;
        public float siteMaxHeight = 1.5f;
        public float maxSlope = 0.35f;
        public float beachMargin = 0.35f;
        public float beachHeight = 0.25f;
        public float drownHeight = 0.05f;
        [Header("Umzug bei steigendem Wasser")]
        [Tooltip("Liegt der Dorfplatz weniger als so hoch über der Mindesthöhe für Bauplätze, zieht das Dorf auf höheren Grund.")]
        [Range(0f, 1f)] public float moveUpMargin = 0.2f;
        [Tooltip("Verschnaufpause (Sekunden), nachdem Häuser versunken sind, bevor am neuen Platz gebaut wird.")]
        [Range(0f, 120f)] public float regroupRest = 25f;
        [Tooltip("Wie weit (Einheiten) das Dorf für einen neuen Platz suchen darf.")]
        [Range(4f, 40f)] public float moveUpRange = 20f;
        public float sinkTime = 5f;
        // A village whose ground drowned waits dormant for new ground; below this area it is given up.
        public float dissolveArea = 8f;
        // A life cell loses its bushes and trees (the clearing) and gets the village-green ground tint when a
        // building stands in it or reaches into it by clearReach of its radius plus clearMargin; docks,
        // lighthouses and shrines clear nothing. With the small houses that is the building's own cell and a
        // neighbour it nearly touches - a town clears a handful of cells, not a whole wood.
        public float clearReach = 1f;
        public float clearMargin = 0.12f;
        // Templates are modelled with a 0.25 u hut. Trees stand about 1 u tall; a house is a small thing under
        // them (hut 0.14 u, house 0.18 u, a settler 0.05 u = a good quarter of a house). The lighthouse stays a
        // landmark and the jetty long enough for its boat: both are drawn larger than the houses by their factor.
        public float buildingScale = 0.55f;
        public float settlerScale = 0.75f;
        public float lighthouseScale = 1.4f;
        public float dockScale = 1.25f;
        // Lanes: free ground every building keeps round its footprint, and how far a village spreads for its
        // size - small houses packed wall to wall read as one blob from the chase camera.
        public float siteGap = 0.06f;
        public float villageSpread = 1.35f;
        // At night every lit building carries a soft additive halo (IslandCrittersSystem.SharedGlowMaterial, one
        // more draw call per settled island, only at night) that never gets smaller on screen than windowMinAngle,
        // so the windows of the small houses still read from the chase camera.
        public float windowHalo = 0.85f;
        public float windowMinAngle = 0.0026f;
        public float lampMinAngle = 0.0055f;

        // Hard caps per island, also after merges: roofed structures, plots (gardens, fields), villages, folk.
        public int maxStructures = 32;
        public int maxPlots = 6;
        public int maxVillages = 3;
        public int maxSettlers = 24;

        public float settlerSpeed = 0.06f;
        public float waveDistance = 3f;
        public float gatherNight = 0.5f;
        public float sleepNight = 0.85f;
        public float shelterStorm = 0.35f;
        public float celebrateTime = 30f;
        public float partyRunTime = 10f;

        [Header("Fest mit Lampions (Meilenstein)")]
        [Tooltip("Feiern die Insulaner abends ein Fest? Setzt der Meilenstein „Fest“ (15 Inseln); sonst aus.")]
        public bool festivals;
        [Tooltip("Ab welcher Abenddämmerung (0 = heller Tag, 1 = tiefe Nacht) das Fest beginnt.")]
        [Range(0f, 1f)] public float festivalStartNight = 0.45f;
        [Tooltip("Bis zu welcher Nachttiefe gefeiert wird; danach gehen alle schlafen.")]
        [Range(0f, 1f)] public float festivalEndNight = 0.96f;
        [Tooltip("Wie viele Lampions im Kreis um den Festplatz stehen (dazwischen hängen kleine Lichterketten).")]
        [Range(4, 12)] public int lanternPoles = 8;
        [Tooltip("Wie weit der Lampionkreis um den Festplatz steht, über die Größe der tanzenden Gruppe hinaus.")]
        [Range(0f, 1f)] public float lanternMargin = 0.28f;

        [Header("Hafen (Fischerhütte am Steg)")]
        [Tooltip("Freier Umkreis (in Hausgrößen) um die Fischerhütte samt Tonnen, Kisten und Seilen, den kein anderes Gebäude betritt.")]
        [Range(0.2f, 0.6f)] public float harbourReserve = 0.32f;
        [Tooltip("Um so viel näher (Einheiten) muss ein Ufer ohne Platz für die Hütte sein, damit der Steg trotzdem dort gebaut wird.")]
        [Range(0f, 6f)] public float hutlessPenalty = 2.5f;
        [Tooltip("Wie weit (Grad) die Hütte sich zum Steg hin dreht.")]
        [Range(0f, 30f)] public float hutTurn = 8f;
        [Tooltip("Mindesthöhe über dem Wasser für jede Ecke der Hütte.")]
        [Range(0f, 0.3f)] public float hutDryHeight = 0.06f;

        [Header("Meilenstein-Bauwerke")]
        [Tooltip("Bauzeit-Faktor für Leuchtturm und Hafen aus einem Meilenstein: kleiner = der Spieler sieht sie gleich wachsen.")]
        [Range(0.02f, 1f)] public float landmarkBuildScale = 0.12f;
        // Streamed islands start with some history: a seeded share of prehistoryMax seconds is simulated in
        // Repopulate (the player's island should have this off).
        public bool prehistory = true;
        public float prehistoryMax = 5400f;

        public static readonly Color VillageGround = new Color(1.28f, 1.2f, 0.7f, 1f);
        static readonly Color Char = new Color(0.07f, 0.06f, 0.05f).linear;
        static readonly Color WindowGlow = new Color(7.5f, 4.4f, 1.3f);
        static readonly Color LampGlow = new Color(7f, 5.6f, 2.4f);
        static readonly Color FireGlow = new Color(5.5f, 1.9f, 0.3f);
        static readonly Color FireLight = new Color(3.2f, 1.3f, 0.3f);
        static readonly Color WindowHaloColor = new Color(1.05f, 0.62f, 0.2f);
        static readonly Color LampHaloColor = new Color(1.3f, 1.05f, 0.5f);
        static readonly Color FireHaloColor = new Color(1.2f, 0.45f, 0.1f);
        static readonly Color LanternGlow = new Color(6.5f, 3.2f, 1.1f);
        // Paper colours of the lanterns, in the order they hang round the festival ground.
        static readonly Color[] LanternPaper =
        {
            new Color(1f, 0.72f, 0.3f), new Color(1f, 0.45f, 0.35f), new Color(1f, 0.86f, 0.5f), new Color(0.95f, 0.55f, 0.6f)
        };
        static readonly Color FireGroundColor = new Color(0.3f, 0.12f, 0.03f);
        static readonly string[] StageNames = { "Unbesiedelt", "Lager", "Weiler", "Dorf", "Stadt" };
        static readonly int[] StageSettlers = { 0, 4, 8, 16, 24 };
        static readonly int[] StageSites = { 0, 1, 2, 2, 3 };

        const BuildingKind F = BuildingKind.Campfire, T = BuildingKind.Tent, H_ = BuildingKind.Hut, W = BuildingKind.Well,
            G = BuildingKind.Garden, D = BuildingKind.Dock, Ho = BuildingKind.House, Ha = BuildingKind.Hall, Wi = BuildingKind.Windmill,
            L = BuildingKind.Lighthouse, S = BuildingKind.StoneHouse, M = BuildingKind.Market, Fi = BuildingKind.Field, Sh = BuildingKind.Shrine;

        // Cumulative build order per stage: the n-th entry of a kind is wanted while the village has fewer than n.
        static readonly BuildingKind[][] Plans =
        {
            new BuildingKind[0],
            new[] { F, T, T },
            new[] { F, H_, H_, W, H_, G, D, H_, G, H_ },
            new[] { F, H_, H_, W, H_, G, D, H_, G, H_, Ho, Ho, Wi, Ho, Ha, G, Ho, L, Ho, Ho, H_, Ho, H_, Ho },
            new[] { F, W, D, Wi, Ha, L, G, G, G, Ho, Ho, Ho, Ho, Ho, Ho, Ho, Ho, S, S, M, S, Fi, Ho, S, D, S, Fi, Ho, Sh, Fi, S, Ho, S, W, Ho }
        };

        class Village
        {
            public Vector2 center;
            public SettlementStage stage;
            public float age, stageAge, retry, radius;
            public int blocked;
            // No buildable ground at the moment (the island is nearly under water): nothing is built and nobody
            // lives here until a shape change offers a site again. The stage is kept.
            public bool dormant;
            // After homes went under: seconds of rest before building again, and how many folk the village keeps
            // although their homes are gone (until new homes hold them again).
            public float restT;
            public int keep;
            // Holds nothing but a milestone landmark: no plan, no stage, no folk, and it does not keep a real
            // village from being founded next to it.
            public bool landmark;
        }

        class Building
        {
            public BuildingKind kind;
            public int from = -1;
            public int variant;
            public Vector2 pos;
            public float yaw, progress, baked, timer, burn, phase;
            public BuildingState state;
            public Village village;
            // A milestone building: it goes up quickly and the island caps never take it away again.
            public bool landmark;
            // Jetties only: the spot of the fisherman's hut at its land end (HutSpot), -1 = no room for one. Not
            // saved - picked again from the ground on load and after every shape change.
            public int harbour = -1;
        }

        class Settler
        {
            public Vector2 pos, target;
            public float yaw, timer, phase, scale, wave, waveCooldown, walkTime, speed;
            public int variant;
            public SettlerState state, next;
            public Building home, goal;
            public Village village;
            public bool carrying, leaving, hasVia;
            public Vector2 via;
        }

        readonly List<Village> _villages = new();
        readonly List<Building> _buildings = new();
        readonly List<Settler> _settlers = new();
        static readonly int[] Have = new int[SettlementMeshes.KindCount];

        IIslandSurface _surface;
        IslandLifeSystem _life;
        System.Random _rnd;
        int _version = -1;
        bool _populated, _assumeMature, _statistical;
        float _foundTimer, _growTimer, _stepTimer, _folkTimer, _staticTimer, _clock, _night, _bakedNight = -1f;
        float _celebrate, _sailAngle, _waveTimer, _matureCache, _matureTimer, _potential = -1f;
        bool _staticDirty, _staticBuilt, _staticFar;
        Vector2 _meet;
        bool _festival;
        // World position of every lit lantern plus its paper colour index in w; filled with the static mesh and
        // read again by the glow mesh right after it.
        readonly List<Vector4> _lanterns = new();

        GameObject _staticGo, _folkGo, _glowGo;
        MeshRenderer _staticRenderer, _folkRenderer, _glowRenderer;
        Mesh _staticMesh, _folkMesh, _glowMesh;
        readonly TemplateBatch _batch = new();
        readonly GlowBatch _glow = new();

        public LifeTier Tier { get; private set; }
        public int StaticMeshBuilds { get; private set; }
        public int FolkMeshBuilds { get; private set; }
        public int SettlerSteps { get; private set; }
        public int Placements { get; private set; }
        public int BuildingCount => _buildings.Count;
        public int SettlerCount => _settlers.Count;
        public int VillageCount => _villages.Count;
        public float FoundTimer => _foundTimer;
        public bool Celebrating => _celebrate > 0f;
        // The evening festival is on: the folk dance round the lantern ring instead of going to bed.
        public bool Festival => _festival;
        public int FestivalsHeld { get; private set; }
        public int LanternCount => _lanterns.Count;
        public Vector2 FestivalGround => _meet;
        public int StaticVertexCount => _staticMesh != null ? _staticMesh.vertexCount : 0;
        public int FolkVertexCount => _folkMesh != null ? _folkMesh.vertexCount : 0;
        public int MeshVertexCount => StaticVertexCount + FolkVertexCount;
        public int GlowVertexCount => _glowMesh != null ? _glowMesh.vertexCount : 0;
        public int GlowHaloCount => _glowMesh != null ? _glow.HaloCount : 0;

        // World scale a kind is drawn and spaced with.
        public float ScaleOf(BuildingKind kind) =>
            buildingScale * (kind == BuildingKind.Lighthouse ? lighthouseScale : kind == BuildingKind.Dock ? dockScale : 1f);
        float Unit => buildingScale / LegacyScale;

        public SettlementStage Stage
        {
            get
            {
                var s = SettlementStage.None;
                foreach (var v in _villages) if (v.stage > s) s = v.stage;
                return s;
            }
        }

        public string StageName => StageNames[(int)Stage];
        public static string NameOf(SettlementStage stage) => StageNames[(int)stage];
        public SettlementStage VillageStage(int i) => _villages[i].stage;
        public Vector2 VillageCenter(int i) => _villages[i].center;
        public BuildingKind BuildingKindOf(int i) => _buildings[i].kind;
        public BuildingState BuildingStateOf(int i) => _buildings[i].state;
        public Vector2 BuildingPositionOf(int i) => _buildings[i].pos;
        public float BuildingYawOf(int i) => _buildings[i].yaw;
        public float BuildingProgressOf(int i) => _buildings[i].progress;
        public float BuildingRadiusOf(int i) => SettlementMeshes.Spec(_buildings[i].kind).radius * ScaleOf(_buildings[i].kind);
        // Ridge / lamp height above the ground in world units.
        public float BuildingHeightOf(int i) => SettlementMeshes.Spec(_buildings[i].kind).height * ScaleOf(_buildings[i].kind);
        public float SettlerHeight => SettlementMeshes.SettlerHeight * settlerScale;
        public int BuildingVillageOf(int i) => _villages.IndexOf(_buildings[i].village);
        public SettlerState SettlerStateOf(int i) => _settlers[i].state;
        public Vector2 SettlerPositionOf(int i) => _settlers[i].pos;
        public bool SettlerWaving(int i) => _settlers[i].wave > 0f;
        public bool SettlerVisible(int i) => _settlers[i].state != SettlerState.Indoors && _settlers[i].scale > 0.01f;

        public Vector3 SettlerWorldPosition(int i)
        {
            var s = _settlers[i];
            return transform.TransformPoint(s.pos.x, GroundY(s.pos), s.pos.y);
        }

        public int CountOf(BuildingKind kind, bool doneOnly = false)
        {
            int n = 0;
            foreach (var b in _buildings)
                if (b.kind == kind && b.state != BuildingState.Sinking && (!doneOnly || b.state == BuildingState.Done)) n++;
            return n;
        }

        public int StructureCount
        {
            get
            {
                int n = 0;
                foreach (var b in _buildings) if (!SettlementMeshes.Spec(b.kind).plot && b.state != BuildingState.Sinking) n++;
                return n;
            }
        }

        public int PlotCount
        {
            get
            {
                int n = 0;
                foreach (var b in _buildings) if (SettlementMeshes.Spec(b.kind).plot && b.state != BuildingState.Sinking) n++;
                return n;
            }
        }

        // Jetty end and the direction out to sea in world space, for ships that want to moor.
        public bool TryGetDockWorld(out Vector3 pos, out Vector3 seaDir)
        {
            foreach (var b in _buildings)
            {
                if (b.kind != BuildingKind.Dock || b.state != BuildingState.Done) continue;
                float dk = ScaleOf(BuildingKind.Dock);
                Vector2 f = Fwd(b.yaw), e = b.pos + f * (DeckLength * dk);
                pos = transform.TransformPoint(e.x, DeckHeight * dk, e.y);
                seaDir = transform.TransformDirection(new Vector3(f.x, 0f, f.y));
                return true;
            }
            pos = default;
            seaDir = default;
            return false;
        }

        // True inside the footprint of a roofed building (for systems that want to walk around them).
        public bool BlocksShore(Vector2 local, float radius) => Blocks(local) || Blocks(local + new Vector2(radius, 0f)) || Blocks(local - new Vector2(radius, 0f)) || Blocks(local + new Vector2(0f, radius)) || Blocks(local - new Vector2(0f, radius));
        public bool Blocks(Vector2 local)
        {
            foreach (var b in _buildings)
            {
                var spec = SettlementMeshes.Spec(b.kind);
                if (b.harbour >= 0)
                {
                    HutSpot(b, b.harbour, out Vector2 hut, out _, out _);
                    float hr = SettlementMeshes.HutHalfX * buildingScale;
                    if ((hut - local).sqrMagnitude < hr * hr) return true;
                }
                if (spec.plot || b.kind == BuildingKind.Dock) continue;
                float r = spec.radius * 0.8f * ScaleOf(b.kind);
                if ((b.pos - local).sqrMagnitude < r * r) return true;
            }
            return false;
        }

        // IslandLifeSystem asks this per life cell: inside the clearing no bushes or trees stand and the ground
        // takes the village tint.
        public bool Clears(Vector2 local)
        {
            if (_buildings.Count == 0) return false;
            float half = (_life != null ? _life.cellSize : 1.2f) * 0.5f;
            foreach (var v in _villages)
            {
                if ((v.center - local).sqrMagnitude > v.radius * v.radius) continue;
                foreach (var b in _buildings)
                {
                    if (b.village != v) continue;
                    var spec = SettlementMeshes.Spec(b.kind);
                    if (b.harbour >= 0)
                    {
                        HutSpot(b, b.harbour, out Vector2 hut, out _, out _);
                        float hr = half + SettlementMeshes.HutWidth * buildingScale * clearReach + clearMargin;
                        if (Mathf.Abs(hut.x - local.x) < hr && Mathf.Abs(hut.y - local.y) < hr) return true;
                    }
                    if (spec.natural) continue;
                    float reach = half + spec.radius * buildingScale * clearReach + clearMargin;
                    if (Mathf.Abs(b.pos.x - local.x) < reach && Mathf.Abs(b.pos.y - local.y) < reach) return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- lifecycle

        void OnEnable()
        {
            _surface = GetComponent<IIslandSurface>();
            _life = GetComponent<IslandLifeSystem>();
            if (_life != null) _life.Settlement = this;
            if (_surface != null && _surface.LandArea > 0f) Repopulate();
        }

        void OnDisable()
        {
            if (_life != null && _life.Settlement == this) _life.Settlement = null;
        }

        void OnDestroy()
        {
            DestroyMesh(ref _staticMesh);
            DestroyMesh(ref _folkMesh);
            DestroyMesh(ref _glowMesh);
        }

        static void DestroyMesh(ref Mesh m)
        {
            if (m == null) return;
            if (Application.isPlaying) Destroy(m);
            else DestroyImmediate(m);
            m = null;
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            Step(Time.deltaTime);
        }

        float Rand() => (float)_rnd.NextDouble();
        float Rand(float a, float b) => a + Rand() * (b - a);
        float H(Vector2 p) => _surface.SampleHeight(p);
        static Vector2 Fwd(float yaw)
        {
            float r = yaw * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
        }
        static float YawOf(Vector2 dir) => Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        int SettlementSeed => unchecked(seed * 31 + 0x5E77);

        bool Bind()
        {
            if (_surface == null) _surface = GetComponent<IIslandSurface>();
            if (_surface == null) return false;
            if (_life == null)
            {
                _life = GetComponent<IslandLifeSystem>();
                if (_life != null) _life.Settlement = this;
            }
            _rnd ??= new System.Random(SettlementSeed);
            return true;
        }

        // Fresh state from the seed; a repeat call for the same surface Version is a no-op. With prehistory the
        // island may already carry a settlement of a seeded age (never on a newborn volcano).
        public void Repopulate()
        {
            if (!Bind()) return;
            if (_populated && _version == _surface.Version) return;
            _rnd = new System.Random(SettlementSeed);
            ClearAll();
            _version = _surface.Version;
            _populated = true;
            _night = LifeEnvironment.NightAmount;
            if (prehistory && _surface.Character != 1 && _surface.LandArea >= foundingArea)
            {
                float r = Rand();
                _assumeMature = true;
                if (r > 0.2f && MatureFraction() >= matureFraction && TryFound())
                    Simulate((r - 0.2f) / 0.8f * prehistoryMax, 30f);
                _assumeMature = false;
            }
            _staticDirty = true;
            if (!Application.isPlaying) RebuildMeshes();
        }

        void ClearAll()
        {
            _villages.Clear();
            _buildings.Clear();
            _settlers.Clear();
            _lanterns.Clear();
            _festival = false;
            _foundTimer = 0f;
            _celebrate = 0f;
            _potential = -1f;
            _matureTimer = 0f;
            _staticDirty = true;
        }

        // ---------------------------------------------------------------- ground

        // Share of the land that can carry woods at all (the life system's fertile height band), sampled on a
        // coarse grid; stands in for the mature share when there is no life grid or history is assumed.
        float PotentialFraction()
        {
            if (_potential >= 0f) return _potential;
            Rect b = _surface.LocalBounds;
            int land = 0, fertile = 0;
            for (float z = b.yMin + 0.6f; z < b.yMax; z += 1.2f)
                for (float x = b.xMin + 0.6f; x < b.xMax; x += 1.2f)
                {
                    float h = H(new Vector2(x, z));
                    if (h <= 0.12f) continue;
                    land++;
                    if (h >= 0.35f && h < 1.9f) fertile++;
                }
            _potential = land > 0 ? (float)fertile / land : 0f;
            return _potential;
        }

        public float MatureFraction()
        {
            if (_assumeMature || _life == null || !_life.HasGrid) return PotentialFraction();
            _life.GetHudStats(out _, out _, out _, out int woods, out int old, out _);
            return _life.LandCells > 0 ? (float)(woods + old) / _life.LandCells : 0f;
        }

        float SlopeAt(Vector2 p, float e)
        {
            float dx = H(p + new Vector2(e, 0f)) - H(p - new Vector2(e, 0f));
            float dz = H(p + new Vector2(0f, e)) - H(p - new Vector2(0f, e));
            return Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) / (2f * e);
        }

        public bool IsValidSite(Vector2 p, float radius) => IsValidSite(p, radius, siteMinHeight, siteMaxHeight, maxSlope, true);

        // Cheapest tests first (one height sample, then the overlap test without any): placing a whole town is
        // some thousand candidates, and the height samples are what costs.
        bool IsValidSite(Vector2 p, float radius, float minH, float maxH, float slopeMax, bool beach, bool free = false)
        {
            float h = H(p);
            if (h < minH || h > maxH) return false;
            if (free && !IsFree(p, radius)) return false;
            if (SlopeAt(p, Mathf.Max(radius, 0.12f)) > slopeMax) return false;
            if (!beach) return true;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                if (H(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * beachMargin) < beachHeight) return false;
            }
            return true;
        }

        // Tents and huts are later rebuilt as bigger houses on the same spot, so they reserve that room.
        float ReserveRadius(BuildingKind kind)
        {
            float r = SettlementMeshes.Spec(kind).radius;
            if (kind == BuildingKind.Tent || kind == BuildingKind.Hut) r = Mathf.Max(r, SettlementMeshes.Spec(BuildingKind.StoneHouse).radius);
            return r * ScaleOf(kind);
        }

        bool IsFree(Vector2 p, float radius, Building ignore = null)
        {
            foreach (var b in _buildings)
            {
                if (b == ignore) continue;
                float r = radius + ReserveRadius(b.kind) + 0.04f * Unit + siteGap;
                if ((b.pos - p).sqrMagnitude < r * r) return false;
                if (b.harbour < 0) continue;
                HutSpot(b, b.harbour, out Vector2 hut, out _, out _);
                r = radius + harbourReserve * buildingScale + 0.04f * Unit + siteGap;
                if ((hut - p).sqrMagnitude < r * r) return false;
            }
            return true;
        }

        float DeckAt(Vector2 p)
        {
            foreach (var b in _buildings)
            {
                if (b.kind != BuildingKind.Dock || b.state == BuildingState.Sinking) continue;
                Vector2 f = Fwd(b.yaw), d = p - b.pos;
                float lz = Vector2.Dot(d, f), lx = Vector2.Dot(d, new Vector2(f.y, -f.x));
                float dk = ScaleOf(BuildingKind.Dock);
                if (lz > -0.07f * dk && lz < (DeckLength + 0.08f) * dk && Mathf.Abs(lx) < 0.08f * dk) return DeckHeight * dk;
            }
            return float.MinValue;
        }

        float GroundY(Vector2 p) => Mathf.Max(H(p), DeckAt(p));

        // ---------------------------------------------------------------- founding and placement

        int RealVillageCount
        {
            get
            {
                int n = 0;
                foreach (var v in _villages) if (!v.landmark) n++;
                return n;
            }
        }

        bool TryFound()
        {
            if (RealVillageCount >= maxVillages) return false;
            Rect b = _surface.LocalBounds;
            float best = float.MinValue;
            Vector2 site = default;
            float r = 0.3f * buildingScale;
            int evaluated = 0;
            for (int t = 0; t < 64; t++)
            {
                Vector2 p = new Vector2(b.xMin + Rand() * b.width, b.yMin + Rand() * b.height);
                if (evaluated >= 12) break;
                bool far = true;
                foreach (var v in _villages) if (!v.landmark && (v.center - p).sqrMagnitude < 36f) far = false;
                if (!far || !IsValidSite(p, r)) continue;
                evaluated++;
                int room = 0;
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI * 0.25f + 0.3f;
                    float hr = H(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (1.2f * buildingScale));
                    if (hr >= siteMinHeight && hr <= siteMaxHeight) room++;
                }
                float score = room * 0.25f - SlopeAt(p, 0.3f) * 2f - Mathf.Abs(H(p) - 0.8f) * 0.5f - p.magnitude * 0.04f;
                if (_life != null && _life.HasGrid && _life.StageAt(p) >= 0.5f) score += 0.4f;
                if (score > best) { best = score; site = p; }
            }
            if (best == float.MinValue) return false;
            var village = new Village { center = site, stage = SettlementStage.Camp };
            _villages.Add(village);
            _foundTimer = 0f;
            var fire = AddBuilding(village, BuildingKind.Campfire, site, Rand(0f, 360f));
            fire.progress = 0.3f;
            for (int i = 0; i < 2 && _settlers.Count < maxSettlers; i++) SpawnSettler(village, site + new Vector2(Rand(-0.2f, 0.2f), Rand(-0.2f, 0.2f)) * Unit);
            return true;
        }

        Building AddBuilding(Village v, BuildingKind kind, Vector2 pos, float yaw)
        {
            var b = new Building
            {
                kind = kind, pos = pos, yaw = yaw, village = v, state = BuildingState.Site,
                variant = _rnd.Next(SettlementMeshes.Variants), phase = Rand(0f, 6.28f)
            };
            _buildings.Add(b);
            if (kind == BuildingKind.Dock) b.harbour = ChooseHarbour(b);
            Placements++;
            RecomputeRadius(v);
            _staticDirty = true;
            return b;
        }

        void RecomputeRadius(Village v)
        {
            float r = 0f;
            foreach (var b in _buildings)
                if (b.village == v) r = Mathf.Max(r, (b.pos - v.center).magnitude);
            v.radius = r + 1f;
        }

        int CountIn(Village v)
        {
            int n = 0;
            foreach (var b in _buildings) if (b.village == v) n++;
            return n;
        }

        // The next kind the village's plan asks for, -1 when the plan is complete, blocked or the island caps
        // are reached.
        int NextWanted(Village v)
        {
            Array.Clear(Have, 0, Have.Length);
            foreach (var b in _buildings)
                if (b.village == v && b.state != BuildingState.Sinking) Have[(int)b.kind]++;
            var plan = Plans[(int)v.stage];
            bool structures = StructureCount < maxStructures, plots = PlotCount < maxPlots;
            for (int i = 0; i < plan.Length; i++)
            {
                int k = (int)plan[i];
                int n = 0;
                for (int j = 0; j <= i; j++) if ((int)plan[j] == k) n++;
                if (Have[k] >= n) continue;
                if ((v.blocked & (1 << k)) != 0) continue;
                var spec = SettlementMeshes.Spec(plan[i]);
                bool upgrade = UpgradeSource(v, plan[i]) != null;
                if (!upgrade && (spec.plot ? !plots : !structures)) continue;
                return k;
            }
            return -1;
        }

        Building UpgradeSource(Village v, BuildingKind kind)
        {
            BuildingKind from;
            if (kind == BuildingKind.Hut) from = BuildingKind.Tent;
            else if (kind == BuildingKind.StoneHouse) from = BuildingKind.Hut;
            else return null;
            foreach (var b in _buildings)
                if (b.village == v && b.kind == from && b.state == BuildingState.Done) return b;
            return null;
        }

        bool StartBuilding(Village v, BuildingKind kind)
        {
            var src = UpgradeSource(v, kind);
            if (src != null)
            {
                src.from = (int)src.kind;
                src.kind = kind;
                src.state = BuildingState.Upgrading;
                src.progress = src.baked = 0f;
                _staticDirty = true;
                return true;
            }
            if (!FindPlace(v, kind, out Vector2 pos, out float yaw)) return false;
            AddBuilding(v, kind, pos, yaw);
            return true;
        }

        // A milestone landmark (Leuchtturm, Hafen) on the player's island. It joins the village that is already
        // there - a harbour belongs to its folk - and otherwise gets a village of its own that never grows, so
        // it is placed on valid ground, saved, sunk and rebuilt exactly like every other building. True when one
        // stands or is going up; the milestone director simply asks again later if the ground was not ready.
        public bool TryBuildLandmark(BuildingKind kind)
        {
            if (!Bind()) return false;
            if (CountOf(kind) > 0) return true;
            Village host = null;
            foreach (var v in _villages)
            {
                if (v.dormant || v.landmark) continue;
                if (host == null || v.center.sqrMagnitude < host.center.sqrMagnitude) host = v;
            }
            if (host == null)
                foreach (var v in _villages) if (v.landmark && !v.dormant) { host = v; break; }
            if (host != null && StartBuilding(host, kind))
            {
                MarkLandmark(kind);
                return true;
            }
            // A landmark village whose building sank with the ground has nothing left to hold: it goes, and the
            // site is picked again. Without this the retry would add one empty village every time.
            for (int i = _villages.Count - 1; i >= 0; i--)
                if (_villages[i].landmark && CountIn(_villages[i]) == 0) _villages.RemoveAt(i);
            Rect b = _surface.LocalBounds;
            float r = 0.3f * buildingScale;
            float best = float.MinValue;
            Vector2 site = default;
            for (int t = 0; t < 48; t++)
            {
                Vector2 p = new Vector2(b.xMin + Rand() * b.width, b.yMin + Rand() * b.height);
                if (!IsValidSite(p, r)) continue;
                float score = H(p) - p.magnitude * 0.02f;
                if (score <= best) continue;
                best = score;
                site = p;
            }
            if (best == float.MinValue) return false;
            var own = new Village { center = site, stage = SettlementStage.None, landmark = true };
            _villages.Add(own);
            if (!StartBuilding(own, kind))
            {
                _villages.Remove(own);
                return false;
            }
            MarkLandmark(kind);
            return true;
        }

        void MarkLandmark(BuildingKind kind)
        {
            for (int i = _buildings.Count - 1; i >= 0; i--)
                if (_buildings[i].kind == kind) { _buildings[i].landmark = true; return; }
        }

        bool FindPlace(Village v, BuildingKind kind, out Vector2 pos, out float yaw)
        {
            switch (kind)
            {
                case BuildingKind.Dock: return FindShore(v, false, out pos, out yaw);
                case BuildingKind.Lighthouse: return FindShore(v, true, out pos, out yaw);
            }
            float radius = ReserveRadius(kind);
            int n = CountIn(v);
            float best = float.MaxValue;
            pos = default;
            yaw = 0f;
            bool shrine = kind == BuildingKind.Shrine, field = kind == BuildingKind.Field, garden = kind == BuildingKind.Garden;
            for (int pass = 0; pass < 2 && best == float.MaxValue; pass++)
            {
                float reach = (0.6f + 0.3f * Mathf.Sqrt(n)) * (pass == 0 ? 1f : 1.8f) * buildingScale * villageSpread;
                for (int t = 0; t < 28; t++)
                {
                    float a = Rand(0f, Mathf.PI * 2f);
                    Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    Vector2 p;
                    if (garden && TryRandomHome(v, out var home))
                        p = home.pos + dir * (ReserveRadius(home.kind) + radius + 0.06f * Unit);
                    else if (shrine) p = v.center + dir * Rand(1.2f, 5f);
                    else p = v.center + dir * (0.25f * buildingScale + radius + Rand() * reach);
                    bool ok = shrine ? IsValidSite(p, radius, siteMinHeight, 2.8f, 0.6f, true, true)
                        : field ? IsValidSite(p, radius, siteMinHeight, siteMaxHeight + 0.4f, 0.6f, true, true)
                        : IsValidSite(p, radius, siteMinHeight, siteMaxHeight, maxSlope, true, true);
                    if (!ok) continue;
                    float d = (p - v.center).magnitude;
                    float score = kind == BuildingKind.Windmill ? -H(p) * 3f + d * 0.3f
                        : shrine ? -H(p) * 3f + d * 0.1f
                        : field ? d - SlopeAt(p, radius) * 2f
                        : d + SlopeAt(p, radius) * 2f - H(p) * 0.4f;
                    if (score >= best) continue;
                    best = score;
                    pos = p;
                }
            }
            if (best == float.MaxValue) return false;
            Vector2 toCenter = v.center - pos;
            if (field)
            {
                const float e = 0.3f;
                Vector2 grad = new Vector2(H(pos + new Vector2(e, 0f)) - H(pos - new Vector2(e, 0f)), H(pos + new Vector2(0f, e)) - H(pos - new Vector2(0f, e)));
                yaw = grad.sqrMagnitude > 1e-5f ? YawOf(grad) : Rand(0f, 360f);
            }
            else if (kind == BuildingKind.Windmill) yaw = YawOf(-toCenter) + Rand(-20f, 20f);
            else yaw = (toCenter.sqrMagnitude > 1e-4f ? YawOf(toCenter) : Rand(0f, 360f)) + Rand(-14f, 14f);
            return true;
        }

        bool TryRandomHome(Village v, out Building home)
        {
            home = null;
            int seen = 0;
            foreach (var b in _buildings)
            {
                if (b.village != v || SettlementMeshes.Spec(b.kind).homes == 0) continue;
                seen++;
                if (_rnd.Next(seen) == 0) home = b;
            }
            return home != null;
        }

        // Marches outward from the village in 16 directions to the waterline. Docks take the nearest gentle
        // shore (not below a cliff, deep water ahead, away from another dock); the lighthouse the highest firm
        // ground a few steps behind the shore.
        bool FindShore(Village v, bool lighthouse, out Vector2 pos, out float yaw)
        {
            pos = default;
            yaw = 0f;
            float best = float.MaxValue;
            float off = Rand(0f, Mathf.PI * 2f);
            float reach = _surface.BoundingRadius * 2f + 2f;
            for (int i = 0; i < 16; i++)
            {
                float a = off + i * Mathf.PI * 2f / 16f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float dist = 0.4f;
                Vector2 last = v.center;
                bool found = false;
                for (; dist < reach; dist += 0.3f)
                {
                    Vector2 p = v.center + dir * dist;
                    if (H(p) < 0.04f) { found = true; break; }
                    last = p;
                }
                if (!found) continue;
                Vector2 lo = last, hi = v.center + dir * dist;
                for (int k = 0; k < 5; k++)
                {
                    Vector2 mid = (lo + hi) * 0.5f;
                    if (H(mid) < 0.04f) hi = mid; else lo = mid;
                }
                if (lighthouse)
                {
                    for (float back = 0.25f; back <= 0.75f; back += 0.125f)
                    {
                        Vector2 p = lo - dir * back;
                        float r = ReserveRadius(BuildingKind.Lighthouse);
                        if (!IsValidSite(p, r, siteMinHeight, 3f, 0.9f, false) || !IsFree(p, r)) continue;
                        float score = -H(p);
                        if (score < best) { best = score; pos = p; yaw = YawOf(-dir); }
                        break;
                    }
                    continue;
                }
                float dk = ScaleOf(BuildingKind.Dock);
                if (H(lo - dir * 0.35f) > 0.9f || H(lo + dir * 0.6f * dk) > -0.1f) continue;
                if (!IsFree(lo + dir * 0.3f * dk, 0.3f * dk)) continue;
                bool apart = true;
                foreach (var b in _buildings)
                    if (b.kind == BuildingKind.Dock && (b.pos - lo).sqrMagnitude < 4f) apart = false;
                if (!apart) continue;
                // A shore with room for the fisherman's hut wins over a nearer one without.
                float d = (lo - v.center).magnitude + (HasHarbourRoom(lo, YawOf(dir)) ? 0f : hutlessPenalty);
                if (d < best) { best = d; pos = lo; yaw = YawOf(dir); }
            }
            return best != float.MaxValue;
        }

        // ---------------------------------------------------------------- harbour

        // Fisherman's hut spots behind a jetty's shore end in buildingScale units of the jetty frame (harbour index =
        // (back * 2 + side) * 2 + lateral): nearest the water first, the jetty's own side (variant) before the other.
        static readonly float[] HutBacks = { 0.2f, 0.3f, 0.42f, 0.56f, 0.72f };
        static readonly float[] HutLats = { 0.42f, 0.52f };
        const int HutSpotCount = 20;
        // Platform corners (template units) the hut is levelled on; the step between them must stay within the stilts.
        const float HutProbeX = 0.112f, HutProbeZ = 0.13f, HutMaxStep = 0.3f;
        // Clutter round the hut in its frame (template units: x away from the jetty, z out to sea, yaw offset, prop).
        static readonly Vector4[] HarbourLayout =
        {
            new Vector4(-0.2f, 0.07f, 0f, (int)SettlementMeshes.HarbourProp.Barrels),
            new Vector4(0.245f, -0.07f, 12f, (int)SettlementMeshes.HarbourProp.Crates),
            new Vector4(0.07f, 0.215f, 0f, (int)SettlementMeshes.HarbourProp.Coil),
            new Vector4(0.23f, 0.13f, 35f, (int)SettlementMeshes.HarbourProp.Pot)
        };

        static Vector2 Rot(Vector2 o, float yaw)
        {
            Vector2 f = Fwd(yaw);
            return new Vector2(f.y * o.x + f.x * o.y, -f.x * o.x + f.y * o.y);
        }

        void HutSpotAt(Vector2 root, float dockYaw, int variant, int spot, out Vector2 pos, out float yaw, out int side)
        {
            int lat = spot & 1, other = (spot >> 1) & 1, back = Mathf.Min(spot >> 2, HutBacks.Length - 1);
            side = ((variant & 1) == 0 ? 1 : -1) * (other == 0 ? 1 : -1);
            Vector2 f = Fwd(dockYaw), r = new Vector2(f.y, -f.x);
            pos = root - f * (HutBacks[back] * buildingScale) + r * (side * HutLats[lat] * buildingScale);
            yaw = dockYaw - side * hutTurn;
        }

        void HutSpot(Building b, int spot, out Vector2 pos, out float yaw, out int side) => HutSpotAt(b.pos, b.yaw, b.variant, spot, out pos, out yaw, out side);

        // The hut stands level on its highest platform corner (a little sunk in), on stilts down the slope.
        float HutBase(Vector2 hut, float yaw)
        {
            float hi = H(hut);
            for (int i = 0; i < 4; i++)
                hi = Mathf.Max(hi, H(hut + Rot(new Vector2((i & 1) == 0 ? -HutProbeX : HutProbeX, (i & 2) == 0 ? -HutProbeZ : HutProbeZ) * buildingScale, yaw)));
            return hi - 0.02f * buildingScale;
        }

        Vector3 HutPoint(Vector2 hut, float yaw, int side, float baseY, Vector3 local)
        {
            Vector2 o = Rot(new Vector2(local.x * side, local.z) * buildingScale, yaw);
            return new Vector3(hut.x + o.x, baseY + local.y * buildingScale, hut.y + o.y);
        }

        // Dry, not too steep for the plinth, off every jetty and clear of the other buildings (and their huts).
        bool HutFitsAt(Vector2 root, float dockYaw, int variant, int spot, Building ignore)
        {
            HutSpotAt(root, dockYaw, variant, spot, out Vector2 hut, out float yaw, out _);
            float lo = H(hut);
            if (lo < hutDryHeight) return false;
            float hi = lo;
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -1f : 1f, sz = (i & 2) == 0 ? -1f : 1f;
                float h = H(hut + Rot(new Vector2(sx * HutProbeX, sz * HutProbeZ) * buildingScale, yaw));
                lo = Mathf.Min(lo, h);
                hi = Mathf.Max(hi, h);
                if (DeckAt(hut + Rot(new Vector2(sx * SettlementMeshes.HutHalfX, sz * SettlementMeshes.HutHalfZ) * buildingScale, yaw)) != float.MinValue) return false;
            }
            if (lo < hutDryHeight || hi - lo > HutMaxStep * buildingScale) return false;
            return IsFree(hut, harbourReserve * buildingScale, ignore);
        }

        bool HutFits(Building b, int spot) => HutFitsAt(b.pos, b.yaw, b.variant, spot, b);

        int ChooseHarbour(Building b)
        {
            for (int spot = 0; spot < HutSpotCount; spot++)
                if (HutFits(b, spot)) return spot;
            return -1;
        }

        bool HasHarbourRoom(Vector2 root, float dockYaw)
        {
            for (int spot = 0; spot < HutSpotCount; spot++)
                if (HutFitsAt(root, dockYaw, 0, spot, null)) return true;
            return false;
        }

        // A piece of clutter on its own ground; false where that ground is under water or on a jetty.
        bool HarbourPropAt(Vector2 hut, float yaw, int side, int i, out Vector3 pos, out float propYaw)
        {
            Vector4 l = HarbourLayout[i];
            Vector2 q = hut + Rot(new Vector2(l.x * side, l.y) * buildingScale, yaw);
            float h = H(q);
            pos = new Vector3(q.x, h, q.y);
            propYaw = yaw + l.z * side;
            return h >= 0.03f && DeckAt(q) == float.MinValue;
        }

        void AddHarbour(Building b)
        {
            HutSpot(b, b.harbour, out Vector2 hut, out float yaw, out int side);
            float k = buildingScale, baseY = HutBase(hut, yaw);
            float charAmount = 0f, glow = 0f, drop = 0f, tilt = 0f;
            bool props = true;
            switch (b.state)
            {
                case BuildingState.Done: glow = GlowOf(b); break;
                case BuildingState.Burning: charAmount = b.burn * 0.85f; break;
                case BuildingState.Charred: charAmount = 0.9f; break;
                case BuildingState.Sinking:
                    tilt = 1f - Mathf.Clamp01(b.timer / Mathf.Max(0.1f, sinkTime));
                    drop = tilt * tilt * (SettlementMeshes.HutHeight + 0.12f) * k;
                    break;
                default:
                {
                    // The hut goes up with the second half of the jetty's work, the clutter arrives when it is done.
                    float up = Smooth(0.35f, 1f, b.progress);
                    if (up <= 0.01f) return;
                    drop = (1f - up) * (SettlementMeshes.HutHeight + 0.05f) * k;
                    props = false;
                    break;
                }
            }
            Vector3 pos = new Vector3(hut.x, baseY - drop, hut.y);
            var body = SettlementMeshes.FishHut(side);
            var lights = SettlementMeshes.FishHutGlow(side);
            if (tilt > 0f)
            {
                _batch.Add(body, pos, yaw, k, 9f * tilt * side, 5f * tilt, null, 0f, default, 0f);
                _batch.Add(lights, pos, yaw, k, 9f * tilt * side, 5f * tilt, null, 0f, default, 0f);
            }
            else
            {
                _batch.AddPlant(body, pos, yaw, k, 0f, 0f, Color.white, Char, charAmount);
                if (charAmount > 0f) _batch.AddPlant(lights, pos, yaw, k, 0f, 0f, Color.white, Char, charAmount);
                else _batch.AddPlant(lights, pos, yaw, k, 0f, 0f, Color.white, WindowGlow, glow);
            }
            if (!props) return;
            float dk = ScaleOf(BuildingKind.Dock);
            if (tilt > 0f) _batch.Add(SettlementMeshes.JettyGear, new Vector3(b.pos.x, -tilt * tilt * 0.22f * dk, b.pos.y), b.yaw, dk, 9f * tilt, 5f * tilt, null, 0f, default, 0f);
            else _batch.AddPlant(SettlementMeshes.JettyGear, new Vector3(b.pos.x, 0f, b.pos.y), b.yaw, dk, 0f, 0f, Color.white, Char, charAmount);
            for (int i = 0; i < HarbourLayout.Length; i++)
            {
                if (!HarbourPropAt(hut, yaw, side, i, out Vector3 p, out float py)) continue;
                p.y -= drop;
                _batch.AddPlant(SettlementMeshes.Prop((SettlementMeshes.HarbourProp)HarbourLayout[i].w), p, py, k, 0f, 0f, Color.white, Char, charAmount);
            }
        }

        // The hut and the clutter by building i (a jetty), island local: (x, z, footprint radius, ground y), the hut
        // first. Empty for anything else or a jetty without room for a hut.
        public int HarbourParts(int index, List<Vector4> parts)
        {
            parts.Clear();
            if (index < 0 || index >= _buildings.Count || !Bind()) return 0;
            var b = _buildings[index];
            if (b.harbour < 0) return 0;
            HutSpot(b, b.harbour, out Vector2 hut, out float yaw, out int side);
            parts.Add(new Vector4(hut.x, hut.y, SettlementMeshes.HutHalfX * 1.4142f * buildingScale, HutBase(hut, yaw)));
            for (int i = 0; i < HarbourLayout.Length; i++)
                if (HarbourPropAt(hut, yaw, side, i, out Vector3 p, out _))
                    parts.Add(new Vector4(p.x, p.z, SettlementMeshes.HarbourPropRadius((SettlementMeshes.HarbourProp)HarbourLayout[i].w) * buildingScale, p.y));
            return parts.Count;
        }

        public bool HasHarbourHut(int index) => index >= 0 && index < _buildings.Count && _buildings[index].harbour >= 0;

        static readonly List<Vector4> ViewParts = new();

        // World centre and radius of a whole harbour (jetty, boat, fisherman's hut and its clutter) for a camera that
        // wants all of it in view; without a hut it is the jetty and its boat.
        public bool TryGetHarbourView(int index, out Vector3 centre, out float radius)
        {
            centre = default;
            radius = 0f;
            if (index < 0 || index >= _buildings.Count || _buildings[index].kind != BuildingKind.Dock || !Bind()) return false;
            var b = _buildings[index];
            float dk = ScaleOf(BuildingKind.Dock);
            Vector2 f = Fwd(b.yaw);
            Vector2 min = b.pos, max = b.pos;
            void Grow(Vector2 p, float r)
            {
                min = Vector2.Min(min, p - Vector2.one * r);
                max = Vector2.Max(max, p + Vector2.one * r);
            }
            Grow(b.pos, 0.07f * dk);
            Grow(b.pos + f * (DeckLength * dk), 0.07f * dk);
            Grow(b.pos + Rot(new Vector2(0.12f, 0.44f), b.yaw) * dk, 0.13f * dk);
            float y = DeckHeight * dk;
            if (HarbourParts(index, ViewParts) > 0)
            {
                foreach (var part in ViewParts) Grow(new Vector2(part.x, part.y), part.z);
                y = 0.5f * (y + ViewParts[0].w + 0.45f * SettlementMeshes.HutHeight * buildingScale);
            }
            Vector2 c = (min + max) * 0.5f;
            centre = transform.TransformPoint(c.x, y, c.y);
            radius = (max - min).magnitude * 0.5f * transform.lossyScale.x;
            return true;
        }

        // ---------------------------------------------------------------- growth

        float AreaOf(SettlementStage s) => s == SettlementStage.Hamlet ? hamletArea : s == SettlementStage.Village ? villageArea : s == SettlementStage.Town ? townArea : foundingArea;
        float AgeOf(SettlementStage s) => s == SettlementStage.Hamlet ? hamletAge : s == SettlementStage.Village ? villageAge : townAge;

        float CachedMature(float dt)
        {
            _matureTimer -= dt;
            if (_matureTimer <= 0f)
            {
                _matureTimer = 5f;
                _matureCache = MatureFraction();
            }
            return _matureCache;
        }

        // Founding, stages, construction, fire and sinking timers; cheap enough for every tier and for the
        // fast-forward (no per-settler work in here).
        public void StepGrowth(float dt)
        {
            if (!Bind() || dt <= 0f) return;
            float area = _surface.LandArea;
            if (_villages.Count == 0)
            {
                StepFounding(dt, area, CachedMature(dt));
                return;
            }

            for (int i = _buildings.Count - 1; i >= 0; i--)
            {
                var b = _buildings[i];
                var spec = SettlementMeshes.Spec(b.kind);
                switch (b.state)
                {
                    case BuildingState.Site:
                    case BuildingState.Upgrading:
                        b.progress += dt / Mathf.Max(1f, spec.buildTime * buildTimeScale * (b.landmark ? landmarkBuildScale : 1f));
                        if (b.progress >= 1f)
                        {
                            b.progress = b.baked = 1f;
                            b.state = BuildingState.Done;
                            b.from = -1;
                            _staticDirty = true;
                        }
                        else if (b.progress - b.baked >= 0.02f) { b.baked = b.progress; _staticDirty = true; }
                        break;
                    case BuildingState.Done:
                        if (!_statistical && spec.burns && _life != null && _life.BurnAt(b.pos) >= 0.99f)
                        {
                            b.state = BuildingState.Burning;
                            b.burn = 0f;
                            Evict(b);
                            _staticDirty = true;
                        }
                        break;
                    case BuildingState.Burning:
                        b.burn += dt / 6f;
                        _staticDirty = true;
                        if (b.burn >= 1f) { b.burn = 1f; b.state = BuildingState.Charred; b.timer = rebuildDelay; }
                        break;
                    case BuildingState.Charred:
                        b.timer -= dt;
                        if (b.timer <= 0f && (_life == null || _life.BurnAt(b.pos) < 0.95f))
                        {
                            b.state = BuildingState.Site;
                            b.progress = b.baked = 0.2f;
                            b.burn = 0f;
                            b.from = -1;
                            _staticDirty = true;
                        }
                        break;
                    case BuildingState.Sinking:
                        b.timer -= dt;
                        _staticDirty = true;
                        if (b.timer <= 0f)
                        {
                            _buildings.RemoveAt(i);
                            RecomputeRadius(b.village);
                        }
                        break;
                }
            }

            float mature = CachedMature(dt);
            // A milestone landmark alone is no settlement: the folk still have to find the island themselves.
            if (RealVillageCount == 0) StepFounding(dt, area, mature);
            for (int vi = _villages.Count - 1; vi >= 0; vi--)
            {
                var v = _villages[vi];
                if (v.landmark) continue;
                v.age += dt;
                v.stageAge += dt;
                v.retry -= dt;
                // A failed search is the expensive one, so the fast-forward tries every kind once per stage only.
                if (v.retry <= 0f) { v.retry = retryInterval; if (!_statistical) v.blocked = 0; }
                TryAdvance(v, area, mature);

                if (v.dormant) continue;
                if (v.restT > 0f) { v.restT -= dt; continue; }
                int sites = 0;
                foreach (var b in _buildings)
                    if (b.village == v && (b.state == BuildingState.Site || b.state == BuildingState.Upgrading)) sites++;
                if (sites >= StageSites[(int)v.stage]) continue;
                int k = NextWanted(v);
                if (k < 0) continue;
                if (!StartBuilding(v, (BuildingKind)k)) v.blocked |= 1 << k;
            }
        }

        void StepFounding(float dt, float area, float mature)
        {
            if (area < foundingArea || mature < matureFraction) { _foundTimer = 0f; return; }
            _foundTimer += dt;
            if (_foundTimer >= foundingDelay && !TryFound()) _foundTimer = foundingDelay - 30f;
        }

        void TryAdvance(Village v, float area, float mature)
        {
            if (v.stage >= SettlementStage.Town) return;
            var next = v.stage + 1;
            if (area < AreaOf(next) || mature < matureFraction || v.stageAge < AgeOf(next)) return;
            var plan = Plans[(int)v.stage];
            int wanted = 0, done = 0;
            foreach (var k in plan) if ((v.blocked & (1 << (int)k)) == 0) wanted++;
            foreach (var b in _buildings) if (b.village == v && b.state == BuildingState.Done) done++;
            if (done < Mathf.CeilToInt(wanted * planDoneFraction)) return;
            v.stage = next;
            v.stageAge = 0f;
            v.blocked = 0;
        }

        // Offline catch-up and prehistory: growth only, then the folk the finished homes can hold.
        public void Simulate(float seconds, float step = 10f)
        {
            if (!Bind() || !(seconds > 0f)) return;
            if (!(step > 0f)) step = 10f;
            _statistical = true;
            for (float t = 0f; t < seconds; t += step) StepGrowth(Mathf.Min(step, seconds - t));
            _statistical = false;
            ReconcileSettlers(true);
            _staticDirty = true;
        }

        public void CatchUp(float seconds)
        {
            if (seconds > 0.5f) Simulate(seconds);
        }

        // ---------------------------------------------------------------- shape changes, fire, sinking

        void BeginSink(Building b)
        {
            if (b.state == BuildingState.Sinking) return;
            b.state = BuildingState.Sinking;
            b.timer = sinkTime;
            Evict(b);
            _staticDirty = true;
        }

        void Evict(Building b)
        {
            foreach (var s in _settlers)
            {
                if (s.goal == b) s.goal = null;
                if (s.home != b) continue;
                s.home = null;
                if (s.state == SettlerState.Indoors) { s.state = SettlerState.Idle; s.timer = 0f; s.pos = DoorOf(b); }
            }
        }

        void OnShapeChanged()
        {
            _potential = -1f;
            _matureTimer = 0f;
            _staticDirty = true;
            float sinkDepth = _surface.SinkDepth;
            bool rising = sinkDepth > _lastSink + 1e-4f;
            _lastSink = sinkDepth;
            // Folk count per village before anything sinks: a flooded home does not cost its people.
            foreach (var v in _villages)
            {
                int have = 0;
                foreach (var st in _settlers) if (st.village == v && !st.leaving) have++;
                v.keep = Mathf.Max(v.keep, have);
            }
            foreach (var b in _buildings)
            {
                if (b.state == BuildingState.Sinking) continue;
                float h = H(b.pos);
                bool sink;
                if (b.kind == BuildingKind.Dock) sink = h < -0.25f || h > 0.4f || H(b.pos + Fwd(b.yaw) * (0.5f * ScaleOf(BuildingKind.Dock))) > 0.03f;
                else sink = h < drownHeight;
                if (!sink) continue;
                BeginSink(b);
                if (b.village != null && HomesOfKind(b.kind) > 0 && regroupRest > 0f) b.village.restT = Mathf.Max(b.village.restT, regroupRest);
            }
            foreach (var b in _buildings)
                if (b.kind == BuildingKind.Dock && b.state != BuildingState.Sinking && (b.harbour < 0 || !HutFits(b, b.harbour)))
                    b.harbour = ChooseHarbour(b);
            for (int i = _villages.Count - 1; i >= 0; i--)
            {
                var v = _villages[i];
                v.blocked = 0;
                // The water is coming: the heart of the village moves up to high ground with room, and the folk
                // gather there, rest a while, then build again around it.
                // Only rising water moves a village: a merge that leaves it low but dry is no reason to pack up.
                if (H(v.center) >= siteMinHeight + (rising ? moveUpMargin : 0f)) { v.dormant = false; continue; }
                Vector2 old = v.center;
                bool moved = (rising && MoveUp(v)) || (H(v.center) < siteMinHeight * 0.7f ? Recenter(v) : H(v.center) >= siteMinHeight * 0.7f);
                v.dormant = !moved;
                if (v.dormant && _surface.LandArea < dissolveArea) RemoveVillage(v);
                else if (moved && (v.center - old).sqrMagnitude > 1f && regroupRest > 0f) v.restT = Mathf.Max(v.restT, regroupRest);
            }
            foreach (var s in _settlers)
            {
                if (s.leaving) continue;
                var v = s.village;
                bool wet = GroundY(s.pos) < drownHeight;
                if (v.dormant)
                {
                    if (wet) s.leaving = true;
                    continue;
                }
                bool homeless = s.home == null || s.home.state == BuildingState.Sinking;
                if (!wet && !(homeless && v.restT > 0f)) continue;
                // Standing in the water already: straight onto dry land. Otherwise walk up to the meeting place.
                if (wet) s.pos = NearestDry(s.pos, v.center);
                if (s.state == SettlerState.Indoors) s.state = SettlerState.Idle;
                s.home = null;
                Walk(s, RingSpot(v.center, s, 0.35f * Unit + Rand(0f, 0.25f) * Unit), SettlerState.Idle, null);
            }
        }

        float _lastSink;

        // Highest buildable ground with room around it within moveUpRange that can be reached without crossing water;
        // a little closer is worth a little lower. False when nothing is higher than where the village is now.
        bool MoveUp(Village v)
        {
            float here = H(v.center);
            float best = float.MinValue;
            Vector2 site = v.center;
            float r = 0.3f * buildingScale;
            for (int t = 0; t < 64; t++)
            {
                float a = Rand(0f, Mathf.PI * 2f);
                Vector2 p = v.center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Rand(1f, moveUpRange);
                float h = H(p);
                if (h <= here + 0.05f || !IsValidSite(p, r) || !DryPath(v.center, p)) continue;
                int room = 0;
                for (int i = 0; i < 8; i++)
                {
                    float ang = i * Mathf.PI * 0.25f + 0.3f;
                    float hr = H(p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (1.2f * buildingScale));
                    if (hr >= siteMinHeight && hr <= siteMaxHeight) room++;
                }
                float score = h + room * 0.12f - (p - v.center).magnitude * 0.02f;
                if (score > best) { best = score; site = p; }
            }
            if (best == float.MinValue) return false;
            v.center = site;
            RecomputeRadius(v);
            return true;
        }

        bool DryPath(Vector2 from, Vector2 to)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt((to - from).magnitude / 0.4f));
            for (int i = 1; i < n; i++) if (H(Vector2.Lerp(from, to, i / (float)n)) < drownHeight) return false;
            return true;
        }

        Vector2 NearestDry(Vector2 p, Vector2 fallback)
        {
            for (int ring = 1; ring <= 8; ring++)
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI * 0.25f;
                    Vector2 q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (0.25f * ring);
                    if (GroundY(q) >= drownHeight) return q;
                }
            return fallback;
        }

        static int HomesOfKind(BuildingKind kind) => SettlementMeshes.Spec(kind).homes;

        // The heart of the village drowned: it moves to its highest standing building or to fresh valid ground
        // uphill within reach, so the folk rebuild higher up.
        bool Recenter(Village v)
        {
            float best = siteMinHeight;
            Vector2 c = default;
            bool found = false;
            foreach (var b in _buildings)
            {
                if (b.village != v || b.state == BuildingState.Sinking || SettlementMeshes.Spec(b.kind).natural) continue;
                float h = H(b.pos);
                if (h > best) { best = h; c = b.pos; found = true; }
            }
            for (int t = 0; t < 48 && !found; t++)
            {
                float a = Rand(0f, Mathf.PI * 2f);
                Vector2 p = v.center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Rand(0.5f, 8f);
                if (IsValidSite(p, 0.3f * buildingScale)) { c = p; found = true; }
            }
            if (!found) return false;
            v.center = c;
            RecomputeRadius(v);
            return true;
        }

        void RemoveVillage(Village v)
        {
            foreach (var b in _buildings) if (b.village == v) BeginSink(b);
            foreach (var s in _settlers) if (s.village == v) { s.leaving = true; s.home = null; s.goal = null; }
            _villages.Remove(v);
            _staticDirty = true;
        }

        // ---------------------------------------------------------------- stepping

        public void Step(float dt)
        {
            if (!Bind() || !_populated) return;
            _clock += dt;
            _night = LifeEnvironment.NightAmount;

            float dist = LifeLod.Distance(transform.position);
            bool visible = dist < hideDistance;
            if (_staticRenderer != null) _staticRenderer.enabled = visible;
            if (_folkRenderer != null) _folkRenderer.enabled = visible;
            if (_glowRenderer != null) _glowRenderer.enabled = visible && _glow.VertexCount > 0;
            var tier = LifeLod.Tier(dist, detailDistance, simDistance);
            if (tier != Tier && (tier == LifeTier.Far || Tier == LifeTier.Far)) _staticDirty = true;
            Tier = tier;

            if (_surface.Version != _version)
            {
                _version = _surface.Version;
                OnShapeChanged();
            }

            _growTimer += dt;
            if (_growTimer >= (Tier == LifeTier.Far ? farStepInterval : growthInterval))
            {
                StepGrowth(_growTimer);
                _growTimer = 0f;
            }
            if (_celebrate > 0f) _celebrate -= dt;
            StepFestival(dt);
            if (_villages.Count == 0 && _buildings.Count == 0 && _settlers.Count == 0)
            {
                if (_staticDirty && _staticBuilt) RebuildMeshes();
                return;
            }

            _staticTimer += dt;
            if (Tier == LifeTier.Far)
            {
                if (_staticDirty && visible && (!_staticBuilt || !_staticFar || _staticTimer >= farMeshInterval)) RebuildMeshes();
                return;
            }

            if (Tier == LifeTier.Near)
            {
                StepSettlers(dt);
            }
            else
            {
                _stepTimer += dt;
                if (_stepTimer >= midStepInterval)
                {
                    StepSettlers(_stepTimer);
                    _stepTimer = 0f;
                }
            }
            _sailAngle = Mathf.Repeat(_sailAngle + dt * 90f * LifeEnvironment.Wind.magnitude * (1f + 1.5f * LifeEnvironment.Storm), 360f);

            if (Mathf.Abs(_night - _bakedNight) > 0.08f) _staticDirty = true;
            if (_staticDirty && _staticTimer >= staticMeshInterval) RebuildStatic();
            _folkTimer += dt;
            if (_folkTimer >= (Tier == LifeTier.Near ? folkMeshInterval : folkMeshInterval * 3f))
            {
                _folkTimer = 0f;
                RebuildFolk();
            }
        }

        // ---------------------------------------------------------------- festival

        // Once the milestone is reached the folk celebrate every evening: from dusk until deep night they dance
        // round a ring of lanterns on the festival ground instead of going home. Keeping _celebrate topped up is
        // what moves them (the same party the folk throw after a merge).
        void StepFestival(float dt)
        {
            bool on = festivals && _settlers.Count > 0 && _night >= festivalStartNight && _night <= festivalEndNight
                      && _surface.StormIntensity <= shelterStorm;
            if (on != _festival)
            {
                _festival = on;
                _staticDirty = true;
                if (on)
                {
                    FestivalsHeld++;
                    UpdateMeet();
                    if (Moments.Listening) Moments.Report(MomentKind.Festival, transform, _meet, H(_meet));
                }
                else _lanterns.Clear();
            }
            if (_festival) _celebrate = Mathf.Max(_celebrate, 1f);
        }

        // Where the folk gather: the middle of the real villages, pulled onto standable ground.
        void UpdateMeet()
        {
            Vector2 sum = Vector2.zero;
            int n = 0;
            foreach (var v in _villages) if (!v.landmark) { sum += v.center; n++; }
            if (n == 0) return;
            _meet = sum / n;
            Vector2 fallback = _meet;
            foreach (var v in _villages) if (!v.landmark) { fallback = v.center; break; }
            for (int k = 0; k < 8 && !IsStandable(_meet); k++) _meet = Vector2.Lerp(_meet, fallback, 0.25f);
        }

        // Radius of the lantern ring: outside the dancing circle of the whole settlement.
        float LanternRadius => (0.2f + 0.035f * Mathf.Max(6, _settlers.Count)) * buildingScale + lanternMargin * buildingScale;

        // The poles with their lantern, plus two small lanterns on the line to the next pole. Only spots the
        // ground carries get one, so the ring opens up where the island falls away.
        void AddLanterns()
        {
            _lanterns.Clear();
            int poles = Mathf.Clamp(lanternPoles, 4, 12);
            float k = buildingScale, r = LanternRadius;
            float top = SettlementMeshes.LanternPoleHeight;
            Vector3 prev = default;
            bool prevOk = false, firstOk = false;
            Vector3 first = default;
            for (int i = 0; i <= poles; i++)
            {
                bool wrap = i == poles;
                int index = wrap ? 0 : i;
                float a = index * Mathf.PI * 2f / poles;
                Vector2 p = _meet + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                bool ok = IsStandable(p);
                Vector3 lamp = default;
                if (ok)
                {
                    float g = GroundY(p);
                    lamp = new Vector3(p.x, g + top * k, p.y);
                    if (!wrap)
                    {
                        _batch.AddPlant(SettlementMeshes.LanternPole, new Vector3(p.x, g, p.y), a * Mathf.Rad2Deg, k, 0f, 0f, Color.white, default, 0f);
                        AddLantern(lamp, index, 1f);
                        if (!firstOk) { first = lamp; firstOk = true; }
                    }
                    else if (firstOk) lamp = first;
                    else ok = false;
                }
                if (ok && prevOk)
                    for (int s = 1; s <= 2; s++)
                    {
                        float t = s / 3f;
                        Vector3 q = Vector3.Lerp(prev, lamp, t);
                        // The line sags between the poles; a lantern that would hang in the ground is dropped.
                        q.y -= Mathf.Sin(t * Mathf.PI) * 0.16f * k + 0.04f * k;
                        Vector2 flat = new Vector2(q.x, q.z);
                        if (!IsStandable(flat) || q.y < GroundY(flat) + 0.04f * k) continue;
                        AddLantern(q, index * 2 + s, 0.72f);
                    }
                prev = lamp;
                prevOk = ok;
            }
        }

        void AddLantern(Vector3 pos, int index, float size)
        {
            Color paper = LanternPaper[((index % LanternPaper.Length) + LanternPaper.Length) % LanternPaper.Length];
            float glow = 0.25f + 0.55f * Smooth(0.25f, 0.6f, _night);
            _batch.AddPlant(SettlementMeshes.Lantern, pos, index * 47f, buildingScale * size, 0f, 0f, paper, LanternGlow, glow);
            _lanterns.Add(new Vector4(pos.x, pos.y, pos.z, index % LanternPaper.Length));
        }

        // ---------------------------------------------------------------- settlers

        Settler SpawnSettler(Village v, Vector2 pos)
        {
            var s = new Settler
            {
                village = v, pos = pos, target = pos, yaw = Rand(0f, 360f), phase = Rand(0f, 6.28f),
                variant = _rnd.Next(SettlementMeshes.SettlerVariants), speed = Rand(0.85f, 1.2f),
                state = SettlerState.Idle, timer = Rand(0.5f, 3f), waveCooldown = Rand(0f, 4f)
            };
            _settlers.Add(s);
            return s;
        }

        int HomesOf(Building b)
        {
            if (b.state == BuildingState.Done) return SettlementMeshes.Spec(b.kind).homes;
            if (b.state == BuildingState.Upgrading && b.from >= 0) return SettlementMeshes.Spec((BuildingKind)b.from).homes;
            return 0;
        }

        public int Capacity(int village)
        {
            var v = _villages[village];
            int n = 2;
            foreach (var b in _buildings) if (b.village == v) n += HomesOf(b);
            return Mathf.Min(n, StageSettlers[(int)v.stage]);
        }

        // One arrival or departure per call keeps the folk in step with the homes; all at once after a
        // fast-forward or a restore. New arrivals step out of a home (or appear by the fire).
        void ReconcileSettlers(bool all)
        {
            int total = 0;
            foreach (var s in _settlers) if (!s.leaving) total++;
            for (int vi = 0; vi < _villages.Count; vi++)
            {
                var v = _villages[vi];
                int have = 0;
                foreach (var s in _settlers) if (s.village == v && !s.leaving) have++;
                int cap = Capacity(vi);
                if (cap >= v.keep) v.keep = 0;
                int want = v.dormant || v.landmark ? 0 : Mathf.Max(cap, Mathf.Min(v.keep, maxSettlers));
                while (have < want && total < maxSettlers)
                {
                    Building home = FreeHome(v);
                    var s = SpawnSettler(v, home != null ? DoorOf(home) : v.center + new Vector2(Rand(-0.2f, 0.2f), Rand(-0.2f, 0.2f)) * Unit);
                    s.home = home;
                    s.scale = all ? 1f : 0f;
                    if (all && (_night > sleepNight) && home != null) s.state = SettlerState.Indoors;
                    have++;
                    total++;
                    if (!all) break;
                }
                for (int i = _settlers.Count - 1; i >= 0 && have > want; i--)
                {
                    var s = _settlers[i];
                    if (s.village != v || s.leaving) continue;
                    s.leaving = true;
                    have--;
                    if (all) _settlers.RemoveAt(i);
                    else break;
                }
            }
            for (int i = _settlers.Count - 1; i >= 0; i--)
                if (!_villages.Contains(_settlers[i].village)) { if (all) _settlers.RemoveAt(i); else _settlers[i].leaving = true; }
        }

        Building FreeHome(Village v)
        {
            foreach (var b in _buildings)
            {
                if (b.village != v || HomesOf(b) == 0) continue;
                int used = 0;
                foreach (var s in _settlers) if (s.home == b) used++;
                if (used < HomesOf(b) + 1) return b;
            }
            return null;
        }

        Vector2 DoorOf(Building b)
        {
            var spec = SettlementMeshes.Spec(b.kind);
            if (b.kind == BuildingKind.Dock) return b.pos + Fwd(b.yaw) * (0.5f * ScaleOf(b.kind));
            return b.pos + Fwd(b.yaw) * ((spec.depth * 0.5f + 0.05f) * ScaleOf(b.kind));
        }

        Building FindBuilding(Village v, BuildingKind kind, bool done)
        {
            Building pick = null;
            int seen = 0;
            foreach (var b in _buildings)
            {
                if (b.village != v || b.kind != kind) continue;
                if (done ? b.state != BuildingState.Done : b.state != BuildingState.Site && b.state != BuildingState.Upgrading) continue;
                seen++;
                if (_rnd.Next(seen) == 0) pick = b;
            }
            return pick;
        }

        Building FindSite(Village v)
        {
            Building pick = null;
            int seen = 0;
            foreach (var b in _buildings)
            {
                if (b.village != v || (b.state != BuildingState.Site && b.state != BuildingState.Upgrading)) continue;
                seen++;
                if (_rnd.Next(seen) == 0) pick = b;
            }
            return pick;
        }

        Building RandomDone(Village v)
        {
            Building pick = null;
            int seen = 0;
            foreach (var b in _buildings)
            {
                if (b.village != v || b.state != BuildingState.Done || b.kind == BuildingKind.Dock) continue;
                seen++;
                if (_rnd.Next(seen) == 0) pick = b;
            }
            return pick;
        }

        // A spot by the trees outside the clearing (where the logs come from and the chopping happens).
        Vector2 WoodsPoint(Village v)
        {
            Vector2 fallback = v.center;
            for (int t = 0; t < 10; t++)
            {
                float a = Rand(0f, Mathf.PI * 2f);
                Vector2 p = v.center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Rand(0.4f, v.radius + 1f);
                if (H(p) < siteMinHeight || Blocks(p)) continue;
                fallback = p;
                if (Clears(p)) continue;
                if (_life == null || !_life.HasGrid || _life.StageAt(p) >= 0.6f) return p;
            }
            return fallback;
        }

        void Walk(Settler s, Vector2 target, SettlerState next, Building goal)
        {
            s.target = target;
            s.next = next;
            s.goal = goal;
            s.state = SettlerState.Walk;
            s.walkTime = 0f;
            s.hasVia = false;
            // Out onto a jetty only over its shore end, never across the water.
            if (goal != null && goal.kind == BuildingKind.Dock && DeckAt(s.pos) == float.MinValue)
            {
                s.hasVia = true;
                s.via = target;
                s.target = goal.pos;
            }
        }

        void PickJob(Settler s)
        {
            var v = s.village;
            s.carrying = false;
            if (v.restT > 0f)
            {
                // Catching their breath at the new place: stand together, now and then shuffle round.
                if ((s.pos - v.center).sqrMagnitude > 1.2f * Unit * Unit) Walk(s, RingSpot(v.center, s, 0.35f * Unit + Rand(0f, 0.25f) * Unit), SettlerState.Idle, null);
                else { s.state = SettlerState.Idle; s.timer = Rand(2f, 5f); s.yaw += Rand(-90f, 90f); }
                return;
            }
            float r = Rand();
            var site = FindSite(v);
            if (site != null && r < 0.5f)
            {
                var dock = Rand() < 0.3f ? FindBuilding(v, BuildingKind.Dock, true) : null;
                Walk(s, dock != null ? dock.pos + Fwd(dock.yaw) * (0.1f * Unit) : WoodsPoint(v), SettlerState.Fetch, site);
                return;
            }
            if (r < 0.62f) { Walk(s, WoodsPoint(v), SettlerState.Chop, null); return; }
            if (r < 0.76f)
            {
                var plot = FindBuilding(v, Rand() < 0.5f ? BuildingKind.Garden : BuildingKind.Field, true) ?? FindBuilding(v, BuildingKind.Garden, true);
                if (plot != null)
                {
                    var spec = SettlementMeshes.Spec(plot.kind);
                    Walk(s, plot.pos + new Vector2(Rand(-0.35f, 0.35f) * spec.width, Rand(-0.35f, 0.35f) * spec.depth) * buildingScale, SettlerState.Farm, plot);
                    return;
                }
            }
            else if (r < 0.86f)
            {
                var dock = FindBuilding(v, BuildingKind.Dock, true);
                bool taken = false;
                if (dock != null)
                    foreach (var o in _settlers) if (o != s && o.goal == dock && (o.state == SettlerState.Fish || o.next == SettlerState.Fish)) taken = true;
                if (dock != null && !taken)
                {
                    Walk(s, dock.pos + Fwd(dock.yaw) * ((DeckLength - 0.04f) * ScaleOf(BuildingKind.Dock)), SettlerState.Fish, dock);
                    return;
                }
            }
            var any = RandomDone(v);
            if (any != null && Rand() < 0.7f) { Walk(s, DoorOf(any) + new Vector2(Rand(-0.04f, 0.04f), Rand(-0.04f, 0.04f)) * Unit, SettlerState.Idle, any); return; }
            s.state = SettlerState.Idle;
            s.timer = Rand(1.5f, 4f);
            s.yaw += Rand(-60f, 60f);
        }

        Vector2 RingSpot(Vector2 centre, Settler s, float radius)
        {
            float a = s.phase + _settlers.IndexOf(s) * 2.399f;
            return centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        void Arrive(Settler s)
        {
            s.state = s.next;
            switch (s.next)
            {
                case SettlerState.Fetch: s.timer = Rand(1.5f, 3f); break;
                case SettlerState.Build: s.timer = Rand(5f, 9f); break;
                case SettlerState.Chop: s.timer = Rand(5f, 10f); break;
                case SettlerState.Farm: s.timer = Rand(6f, 12f); break;
                case SettlerState.Fish:
                    s.timer = Rand(10f, 25f);
                    if (s.goal != null) s.yaw = s.goal.yaw;
                    break;
                case SettlerState.Indoors: s.timer = Rand(0f, 15f); break;
                case SettlerState.Gather:
                case SettlerState.Celebrate:
                    s.timer = Rand(2f, 5f);
                    break;
                default: s.timer = Rand(2f, 6f); break;
            }
            if (s.next == SettlerState.Build) s.carrying = false;
            if (s.goal != null && (s.next == SettlerState.Build || s.next == SettlerState.Farm || s.next == SettlerState.Gather))
            {
                Vector2 to = s.goal.pos - s.pos;
                if (to.sqrMagnitude > 1e-5f) s.yaw = YawOf(to);
            }
        }

        public void StepSettlers(float dt)
        {
            if (_settlers.Count == 0 && _villages.Count == 0) return;
            ReconcileSettlers(false);
            bool storm = _surface.StormIntensity > shelterStorm;
            bool indoors = storm || _night > sleepNight;
            bool fire = !indoors && _night > gatherNight;
            bool party = _celebrate > 0f && !storm;
            bool waveCheck = false;
            _waveTimer -= dt;
            if (_waveTimer <= 0f) { _waveTimer = 0.5f; waveCheck = Tier == LifeTier.Near; }
            // The lantern ring stands where the folk gather, so a festival keeps the ground it started on.
            if (party && !_festival) UpdateMeet();

            for (int i = _settlers.Count - 1; i >= 0; i--)
            {
                var s = _settlers[i];
                SettlerSteps++;
                if (s.leaving)
                {
                    s.scale -= dt / 1.5f;
                    if (s.scale <= 0f) _settlers.RemoveAt(i);
                    continue;
                }
                if (s.scale < 1f) s.scale = Mathf.Min(1f, s.scale + dt / 1.5f);
                if (s.home == null || HomesOf(s.home) == 0) s.home = FreeHome(s.village);

                if (s.state == SettlerState.Indoors)
                {
                    if (indoors && !party) continue;
                    s.timer -= dt;
                    if (s.timer > 0f) continue;
                    s.state = SettlerState.Idle;
                    s.timer = Rand(0.5f, 2f);
                    if (s.home != null) { s.pos = DoorOf(s.home); s.yaw = s.home.yaw; }
                }

                if (waveCheck && s.wave <= 0f && !party)
                {
                    s.waveCooldown -= 0.5f;
                    if (s.waveCooldown <= 0f && s.state != SettlerState.Celebrate && !s.carrying
                        && LifeLod.Distance(transform.TransformPoint(s.pos.x, 0f, s.pos.y)) < waveDistance)
                    {
                        s.wave = Rand(1.4f, 2.2f);
                        s.waveCooldown = Rand(5f, 10f);
                    }
                }
                if (s.wave > 0f) { s.wave -= dt; continue; }

                if (party)
                {
                    if (s.state != SettlerState.Celebrate && !(s.state == SettlerState.Walk && s.next == SettlerState.Celebrate))
                        Walk(s, RingSpot(_meet, s, (0.2f + 0.035f * _settlers.Count) * buildingScale), SettlerState.Celebrate, null);
                }
                else if (indoors)
                {
                    if (s.home != null && s.home.state == BuildingState.Done)
                    {
                        if (!(s.state == SettlerState.Walk && s.next == SettlerState.Indoors)) Walk(s, DoorOf(s.home), SettlerState.Indoors, s.home);
                    }
                    else GoToFire(s);
                }
                else if (fire) GoToFire(s);
                else if (s.state == SettlerState.Gather || s.state == SettlerState.Celebrate) PickJob(s);

                if (s.state == SettlerState.Walk) { MoveSettler(s, dt); continue; }
                s.timer -= dt;
                if (s.timer > 0f) continue;
                switch (s.state)
                {
                    case SettlerState.Fetch:
                        if (s.goal != null && (s.goal.state == BuildingState.Site || s.goal.state == BuildingState.Upgrading))
                        {
                            s.carrying = true;
                            Walk(s, DoorOf(s.goal) + new Vector2(Rand(-0.05f, 0.05f), Rand(-0.05f, 0.05f)) * Unit, SettlerState.Build, s.goal);
                        }
                        else PickJob(s);
                        break;
                    case SettlerState.Gather:
                    case SettlerState.Celebrate:
                        s.timer = Rand(2f, 5f);
                        break;
                    default:
                        PickJob(s);
                        break;
                }
            }
        }

        void GoToFire(Settler s)
        {
            if (s.state == SettlerState.Gather || (s.state == SettlerState.Walk && s.next == SettlerState.Gather)) return;
            var f = FindBuilding(s.village, BuildingKind.Campfire, true);
            Vector2 c = f != null ? f.pos : s.village.center;
            Walk(s, RingSpot(c, s, (0.17f + 0.006f * _settlers.Count) * buildingScale), SettlerState.Gather, f);
        }

        bool IsStandable(Vector2 p) => GroundY(p) >= drownHeight;

        void MoveSettler(Settler s, float dt)
        {
            Vector2 to = s.target - s.pos;
            float d = to.magnitude;
            s.walkTime += dt;
            // Towards a party they run, and whoever has not arrived after partyRunTime cheers where he stands.
            bool toParty = s.next == SettlerState.Celebrate;
            if (toParty && s.walkTime > partyRunTime) { Arrive(s); return; }
            if (d < 0.02f * Unit || s.walkTime > 120f)
            {
                if (s.walkTime <= 120f) s.pos = s.target;
                if (s.hasVia && s.walkTime <= 120f)
                {
                    s.hasVia = false;
                    s.target = s.via;
                    return;
                }
                Arrive(s);
                return;
            }
            float step = Mathf.Min(settlerSpeed * s.speed * (s.carrying ? 0.75f : toParty ? 2.5f : 1f) * dt, d);
            Vector2 dir = to / d;
            Vector2 next = s.pos + dir * step;
            foreach (var b in _buildings)
            {
                if (b == s.goal || b == s.home && s.next == SettlerState.Indoors) continue;
                var spec = SettlementMeshes.Spec(b.kind);
                if (spec.plot || b.kind == BuildingKind.Dock) continue;
                float r = spec.radius * 0.8f * ScaleOf(b.kind);
                if ((s.target - b.pos).sqrMagnitude < r * r) continue;
                Vector2 off = next - b.pos;
                float od = off.magnitude;
                if (od >= r || od < 1e-4f) continue;
                Vector2 n = off / od;
                Vector2 tangent = new Vector2(-n.y, n.x);
                if (Vector2.Dot(tangent, dir) < 0f) tangent = -tangent;
                next = b.pos + n * r + tangent * step;
            }
            if (!IsStandable(next))
            {
                s.state = SettlerState.Idle;
                s.timer = Rand(0.5f, 2f);
                return;
            }
            Vector2 moved = next - s.pos;
            if (moved.sqrMagnitude > 1e-8f) s.yaw = Mathf.LerpAngle(s.yaw, YawOf(moved), 1f - Mathf.Exp(-10f * dt));
            s.pos = next;
        }

        // ---------------------------------------------------------------- meshes

        void EnsureObject(string objName, ref GameObject go, ref MeshRenderer renderer, ref Mesh mesh, Material material = null)
        {
            if (go == null || go.transform.parent != transform)
            {
                go = null;
                for (int i = transform.childCount - 1; i >= 0; i--)
                {
                    var child = transform.GetChild(i).gameObject;
                    if (child.name != objName) continue;
                    if (go == null) go = child;
                    else if (Application.isPlaying) Destroy(child);
                    else DestroyImmediate(child);
                }
                if (go == null)
                {
                    go = new GameObject(objName);
                    go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                    go.transform.SetParent(transform, false);
                    go.AddComponent<MeshFilter>();
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }
            }
            var filter = go.GetComponent<MeshFilter>();
            renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material != null ? material : LifeMeshes.Material;
            if (mesh == null) mesh = filter.sharedMesh;
            if (mesh == null) mesh = new Mesh { name = objName, hideFlags = HideFlags.DontSave };
            filter.sharedMesh = mesh;
        }

        bool HasChild(string objName)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                if (transform.GetChild(i).name == objName) return true;
            return false;
        }

        // An unsettled island gets no child objects at all (stale ones from before a domain reload are emptied).
        public void RebuildMeshes()
        {
            if (!Bind()) return;
            if (_buildings.Count == 0 && _settlers.Count == 0 && !HasChild(StaticName) && !HasChild(FolkName))
            {
                _staticDirty = false;
                return;
            }
            RebuildStatic();
            RebuildFolk();
        }

        float GlowOf(Building b)
        {
            float g = Smooth(0.35f, 0.7f, _night);
            if (g <= 0f) return 0f;
            bool always = b.kind == BuildingKind.Lighthouse || b.kind == BuildingKind.Shrine || b.kind == BuildingKind.Hall || b.kind == BuildingKind.Windmill;
            if (!always && _night > sleepNight + 0.05f && ((int)(b.phase * 100f) % 5) >= 2) g *= 0.12f;
            return g;
        }

        void RebuildStatic()
        {
            EnsureObject(StaticName, ref _staticGo, ref _staticRenderer, ref _staticMesh);
            StaticMeshBuilds++;
            _staticDirty = false;
            _staticBuilt = true;
            _staticTimer = 0f;
            _staticFar = Tier == LifeTier.Far;
            _bakedNight = _night;
            _batch.Begin();
            foreach (var b in _buildings)
            {
                var spec = SettlementMeshes.Spec(b.kind);
                float k = ScaleOf(b.kind);
                float ground = b.kind == BuildingKind.Dock ? 0f : H(b.pos);
                Vector3 pos = new Vector3(b.pos.x, ground, b.pos.y);
                float p = b.progress;
                switch (b.state)
                {
                    case BuildingState.Done:
                        AddBody(spec, b, pos, 1f, 0f, GlowOf(b));
                        break;
                    case BuildingState.Burning:
                        AddBody(spec, b, pos, 1f, b.burn * 0.85f, 0f);
                        break;
                    case BuildingState.Sinking:
                    {
                        float t = 1f - Mathf.Clamp01(b.timer / Mathf.Max(0.1f, sinkTime));
                        var tpl = spec.body[b.variant];
                        _batch.Add(tpl, pos + Vector3.down * (t * t * (spec.height + 0.12f) * k), b.yaw, k, 9f * t, 5f * t, null, 0f, default, 0f);
                        break;
                    }
                    case BuildingState.Charred:
                        if (spec.foundation != null)
                        {
                            _batch.AddPlant(spec.foundation, pos, b.yaw, k, 0f, 0f, Color.white, Char, 0.6f);
                            _batch.AddPlant(spec.scaffold, pos, b.yaw, k, 0f, 0f, Color.white, Char, 0.92f);
                        }
                        else AddBody(spec, b, pos, 1f, 0.9f, 0f);
                        break;
                    default:
                    {
                        if (spec.scaffold == null)
                        {
                            float s = Smooth(0f, 1f, p);
                            AddBody(spec, b, pos + Vector3.down * ((1f - s) * spec.height * 0.5f * k), Mathf.Max(0.05f, s), 0f, 0f);
                            break;
                        }
                        float fs = Smooth(0f, 0.08f, p);
                        if (fs > 0.01f) _batch.AddPlant(spec.foundation, pos, b.yaw, k * fs, 0f, 0f, Color.white, default, 0f);
                        float rise = Smooth(0.12f, 0.5f, p), shrink = 1f - Smooth(0.92f, 1f, p);
                        if (p > 0.12f && shrink > 0.01f)
                            _batch.AddPlant(spec.scaffold, pos + Vector3.down * ((1f - rise) * (spec.height + 0.06f) * k), b.yaw, k * shrink, 0f, 0f, Color.white, default, 0f);
                        float up = Smooth(0.5f, 0.92f, p);
                        if (b.from >= 0)
                        {
                            var old = SettlementMeshes.Spec((BuildingKind)b.from);
                            if (up < 0.99f)
                                _batch.AddPlant(old.body[b.variant], pos + Vector3.down * (up * (old.height + 0.08f) * k), b.yaw, k, 0f, 0f, Color.white, default, 0f);
                        }
                        if (up > 0f) AddBody(spec, b, pos + Vector3.down * ((1f - up) * (spec.height + 0.08f) * k), 1f, 0f, 0f);
                        break;
                    }
                }
                if (b.harbour >= 0) AddHarbour(b);
                if (_staticFar && b.state == BuildingState.Done) AddProps(b, pos, true);
            }
            if (_festival) AddLanterns(); else _lanterns.Clear();
            _batch.Apply(_staticMesh);
            RebuildGlow();
        }

        // Night halos: one per lit building at window height (the lamp of a lighthouse, the flame of the fire
        // plus a warm patch on the ground round it). Baked with the static mesh, so it costs nothing per frame,
        // and the child object only exists once a settled island has seen a night.
        void RebuildGlow()
        {
            _glow.Begin();
            float night = Smooth(0.35f, 0.7f, _night);
            var material = night > 0.02f ? IslandCrittersSystem.SharedGlowMaterial : null;
            if (material != null)
                foreach (var b in _buildings)
                {
                    if (b.state != BuildingState.Done) continue;
                    var spec = SettlementMeshes.Spec(b.kind);
                    float k = ScaleOf(b.kind);
                    float ground = b.kind == BuildingKind.Dock ? 0f : H(b.pos);
                    if (b.kind == BuildingKind.Campfire)
                    {
                        float fire = 0.4f + 0.6f * night;
                        _glow.AddHalo(new Vector3(b.pos.x, ground + 0.07f * k, b.pos.y), 0.3f * k, FireHaloColor * fire, b.phase, 0f, 0f, 0.35f, -1f, windowMinAngle * 1.3f);
                        _glow.AddBlob(_surface, b.pos, 1.1f * k + 0.25f, 0.04f, FireGroundColor * fire, b.phase, -1f);
                        continue;
                    }
                    if (b.harbour >= 0)
                    {
                        float hg = GlowOf(b);
                        if (hg > 0.02f)
                        {
                            HutSpot(b, b.harbour, out Vector2 hut, out float hutYaw, out int side);
                            Vector3 l = HutPoint(hut, hutYaw, side, HutBase(hut, hutYaw), SettlementMeshes.HutLantern);
                            _glow.AddHalo(l, 0.3f * buildingScale, LampHaloColor * hg, b.phase, 0f, 0f, 0.15f, -1f, windowMinAngle);
                        }
                    }
                    if (spec.glow[b.variant] == null) continue;
                    float g = GlowOf(b);
                    if (g <= 0.02f) continue;
                    bool lamp = b.kind == BuildingKind.Lighthouse;
                    float y = ground + (lamp ? SettlementMeshes.LampCentre.y : spec.height * 0.4f) * k;
                    float radius = (lamp ? 0.45f : Mathf.Max(spec.width, spec.depth) * windowHalo) * k;
                    _glow.AddHalo(new Vector3(b.pos.x, y, b.pos.y), radius, (lamp || b.kind == BuildingKind.Shrine ? LampHaloColor : WindowHaloColor) * g,
                        b.phase, 0f, 0f, 0.12f, -1f, lamp ? lampMinAngle : windowMinAngle);
                }
            // The lanterns carry their own light: they glow from dusk on, before the houses light up.
            if (_lanterns.Count > 0)
            {
                var mat = IslandCrittersSystem.SharedGlowMaterial;
                if (mat != null)
                {
                    material = mat;
                    // Kept well below the campfire: a ring of two dozen halos at full colour washes out white.
                    float lit = 0.22f + 0.38f * Smooth(0.2f, 0.55f, _night);
                    for (int i = 0; i < _lanterns.Count; i++)
                    {
                        Vector4 l = _lanterns[i];
                        Color paper = LanternPaper[(int)l.w % LanternPaper.Length];
                        _glow.AddHalo(new Vector3(l.x, l.y, l.z), 0.16f * buildingScale, paper * lit, i * 0.7f, 0f, 0f, 0.2f, -1f, windowMinAngle);
                    }
                }
            }
            if (_glow.VertexCount == 0 && _glowMesh == null && !HasChild(GlowName)) return;
            EnsureObject(GlowName, ref _glowGo, ref _glowRenderer, ref _glowMesh, material != null ? material : IslandCrittersSystem.SharedGlowMaterial);
            _glow.Apply(_glowMesh, 1.5f);
            _glowRenderer.enabled = _glow.VertexCount > 0 && (_staticRenderer == null || _staticRenderer.enabled);
        }

        void AddBody(BuildingSpec spec, Building b, Vector3 pos, float scale, float charAmount, float glow)
        {
            float k = ScaleOf(b.kind) * scale;
            _batch.AddPlant(spec.body[b.variant], pos, b.yaw, k, 1f, b.phase, Color.white, Char, charAmount);
            var g = spec.glow[b.variant];
            if (g == null) return;
            if (charAmount > 0f) _batch.AddPlant(g, pos, b.yaw, k, 0f, 0f, Color.white, Char, charAmount);
            else _batch.AddPlant(g, pos, b.yaw, k, 0f, 0f, Color.white, b.kind == BuildingKind.Lighthouse || b.kind == BuildingKind.Shrine ? LampGlow : WindowGlow, glow);
        }

        Vector3 Local(Building b, Vector3 pos, Vector3 offset)
        {
            Vector2 f = Fwd(b.yaw);
            float k = ScaleOf(b.kind);
            return pos + new Vector3((f.y * offset.x + f.x * offset.z) * k, offset.y * k, (-f.x * offset.x + f.y * offset.z) * k);
        }

        // Moving parts of a finished building. At rest (far tier) they are baked into the static mesh instead.
        void AddProps(Building b, Vector3 pos, bool rest)
        {
            float k = ScaleOf(b.kind);
            switch (b.kind)
            {
                case BuildingKind.Windmill:
                    _batch.Add(SettlementMeshes.Sails, Local(b, pos, SettlementMeshes.SailHub), b.yaw, k, rest ? 22f : _sailAngle + b.phase * 57f, 0f);
                    break;
                case BuildingKind.Dock:
                {
                    float bob = rest ? 0f : Mathf.Sin(_clock * 1.3f + b.phase) * 0.006f;
                    // The small boat must not be swamped by its own bob: it rides a little higher than modelled.
                    float roll = rest ? 0f : Mathf.Sin(_clock * 1.1f + b.phase) * 5f;
                    _batch.Add(SettlementMeshes.Boat, Local(b, pos, new Vector3(0.12f, 0.004f + bob, 0.44f)), b.yaw + 6f, k, roll, 0f);
                    if (rest || b.harbour < 0) break;
                    HutSpot(b, b.harbour, out Vector2 hut, out float hutYaw, out int side);
                    Vector3 chimney = HutPoint(hut, hutYaw, side, HutBase(hut, hutYaw), SettlementMeshes.HutChimney);
                    Vector2 wind = LifeEnvironment.Wind * 0.1f;
                    for (int i = 0; i < 2; i++)
                    {
                        float t = Mathf.Repeat(_clock * 0.16f + i * 0.5f + b.phase, 1f);
                        float size = (0.3f + 0.7f * t) * (1f - t * t) * buildingScale;
                        if (size < 0.04f * buildingScale) continue;
                        _batch.Add(SettlementMeshes.Smoke, chimney + new Vector3(wind.x * t * t, 0.03f + 0.25f * t, wind.y * t * t) * buildingScale, i * 70f, size);
                    }
                    break;
                }
                case BuildingKind.Campfire:
                {
                    if (rest) break;
                    float flick = 0.8f + 0.2f * _night + 0.13f * Mathf.Sin(_clock * 13f + b.phase) + 0.07f * Mathf.Sin(_clock * 23f);
                    _batch.Add(SettlementMeshes.Flame, pos, _clock * 40f, k * flick, 0f, 0f, null, 0f, FireGlow, 0.08f + 0.8f * _night);
                    for (int i = 0; i < 3; i++)
                    {
                        float t = Mathf.Repeat(_clock * 0.22f + i / 3f + b.phase, 1f);
                        float size = (0.4f + 0.9f * t) * (1f - t * t) * k;
                        if (size < 0.05f) continue;
                        Vector2 w = LifeEnvironment.Wind * 0.1f;
                        _batch.Add(SettlementMeshes.Smoke, pos + new Vector3(w.x * t * t, 0.1f + 0.22f * t, w.y * t * t) * k, i * 50f, size);
                    }
                    break;
                }
                case BuildingKind.Lighthouse:
                {
                    float g = Smooth(0.35f, 0.7f, _night);
                    if (rest || g <= 0.02f) break;
                    _batch.Add(SettlementMeshes.Beam, Local(b, pos, SettlementMeshes.LampCentre), _clock * 50f + b.phase * 57f, k * g, 0f, 0f, null, 0f, LampGlow, 1f);
                    break;
                }
            }
        }

        void RebuildFolk()
        {
            EnsureObject(FolkName, ref _folkGo, ref _folkRenderer, ref _folkMesh);
            FolkMeshBuilds++;
            _batch.Begin();
            if (Tier == LifeTier.Far)
            {
                _batch.Apply(_folkMesh);
                return;
            }
            float fireAmount = 0.3f * Smooth(gatherNight - 0.15f, gatherNight + 0.2f, _night);
            foreach (var b in _buildings)
            {
                if (b.state == BuildingState.Burning)
                {
                    var spec = SettlementMeshes.Spec(b.kind);
                    float bk = ScaleOf(b.kind);
                    Vector2 along = Fwd(b.yaw) * (spec.depth * 0.25f * bk);
                    for (int i = -1; i <= 1; i += 2)
                    {
                        float flick = 2.2f + 0.5f * Mathf.Sin(_clock * 11f + b.phase + i);
                        Vector2 q = b.pos + along * i;
                        _batch.Add(SettlementMeshes.Flame, new Vector3(q.x, H(b.pos) + spec.height * 0.8f * bk, q.y), _clock * 60f * i, bk * flick * (1f - 0.5f * b.burn), 0f, 0f, null, 0f, FireGlow, 0.12f + 0.7f * _night);
                    }
                    continue;
                }
                if (b.state != BuildingState.Done) continue;
                AddProps(b, new Vector3(b.pos.x, b.kind == BuildingKind.Dock ? 0f : H(b.pos), b.pos.y), false);
            }
            foreach (var s in _settlers)
            {
                if (s.state == SettlerState.Indoors || s.scale <= 0.01f) continue;
                float y = GroundY(s.pos), pitch = 0f;
                int pose = s.wave > 0f ? 1 : 0;
                switch (s.state)
                {
                    case SettlerState.Walk: y += Mathf.Abs(Mathf.Sin(_clock * 11f + s.phase)) * 0.003f * settlerScale; break;
                    case SettlerState.Chop:
                    case SettlerState.Build: pitch = 12f + 20f * Mathf.Sin(_clock * 7f + s.phase); break;
                    case SettlerState.Farm: pitch = 28f + 8f * Mathf.Sin(_clock * 2.5f + s.phase); break;
                    case SettlerState.Celebrate:
                        y += Mathf.Abs(Mathf.Sin(_clock * 6f + s.phase)) * 0.022f * settlerScale;
                        pose = 1;
                        break;
                }
                float yaw = pose == 1 ? s.yaw + Mathf.Sin(_clock * 9f + s.phase) * 14f : s.yaw;
                float scale = settlerScale * Mathf.Clamp01(s.scale);
                Vector3 pos = new Vector3(s.pos.x, y, s.pos.y);
                float warm = s.state == SettlerState.Gather ? fireAmount : 0f;
                _batch.Add(SettlementMeshes.Settler(s.variant, pose), pos, yaw, scale, 0f, pitch, null, 0f, FireLight, warm);
                if (s.carrying) _batch.Add(SettlementMeshes.Log, pos, yaw, scale);
                else if (s.state == SettlerState.Fish) _batch.Add(SettlementMeshes.Rod, pos, yaw, scale);
            }
            _batch.Apply(_folkMesh);
        }

        // ---------------------------------------------------------------- merging

        public void ShiftLocal(Vector2 delta)
        {
            foreach (var v in _villages) v.center += delta;
            foreach (var b in _buildings) b.pos += delta;
            foreach (var s in _settlers) { s.pos += delta; s.target += delta; }
            _meet += delta;
            _staticDirty = true;
        }

        // The guest's villages, buildings and folk move over with their positions and headings converted;
        // villages stay separate and the island caps decide what survives. Everybody celebrates.
        public void AbsorbFrom(IslandSettlementSystem other)
        {
            if (other == null || other == this || !Bind()) return;
            _populated = true;
            float dyaw = other.transform.eulerAngles.y - transform.eulerAngles.y;
            foreach (var v in other._villages)
            {
                v.center = Convert(other.transform, v.center);
                v.blocked = 0;
                if (_villages.Count < maxVillages) _villages.Add(v);
            }
            foreach (var b in other._buildings)
            {
                if (!_villages.Contains(b.village)) continue;
                b.pos = Convert(other.transform, b.pos);
                b.yaw += dyaw;
                _buildings.Add(b);
            }
            foreach (var s in other._settlers)
            {
                if (!_villages.Contains(s.village)) continue;
                s.pos = Convert(other.transform, s.pos);
                s.target = s.pos;
                s.yaw += dyaw;
                if (s.state == SettlerState.Walk) { s.state = SettlerState.Idle; s.timer = 0f; }
                _settlers.Add(s);
            }
            other._villages.Clear();
            other._buildings.Clear();
            other._settlers.Clear();
            other._staticDirty = true;
            EnforceCaps();
            foreach (var v in _villages) RecomputeRadius(v);
            if (_villages.Count > 0) _celebrate = celebrateTime;
            _foundTimer = Mathf.Max(_foundTimer, other._foundTimer);
            _staticDirty = true;
        }

        void EnforceCaps()
        {
            int structures = StructureCount, plots = PlotCount;
            for (int pass = 0; pass < 2; pass++)
                for (int i = _buildings.Count - 1; i >= 0 && (structures > maxStructures || plots > maxPlots); i--)
                {
                    var b = _buildings[i];
                    var spec = SettlementMeshes.Spec(b.kind);
                    if (b.state == BuildingState.Sinking || b.kind == BuildingKind.Campfire || b.landmark) continue;
                    // Unfinished work and tents go first, finished houses only if that was not enough.
                    if (pass == 0 && !spec.plot && b.state == BuildingState.Done && b.kind != BuildingKind.Tent) continue;
                    if (spec.plot ? plots <= maxPlots : structures <= maxStructures) continue;
                    if (spec.plot) plots--; else structures--;
                    Evict(b);
                    _buildings.RemoveAt(i);
                }
            for (int i = _settlers.Count - 1; i >= 0 && _settlers.Count > maxSettlers; i--) _settlers.RemoveAt(i);
        }

        Vector2 Convert(Transform from, Vector2 p)
        {
            Vector3 w = from.TransformPoint(p.x, 0f, p.y);
            Vector3 l = transform.InverseTransformPoint(w);
            return new Vector2(l.x, l.z);
        }

        // ---------------------------------------------------------------- persistence

        public SettlementSaveData Capture()
        {
            int nv = _villages.Count, nb = _buildings.Count;
            var d = new SettlementSaveData
            {
                vStage = new int[nv], vLandmark = new int[nv], v = new float[nv * SettlementSaveData.VillageStride],
                bKind = new int[nb], bVariant = new int[nb], bState = new int[nb], bVillage = new int[nb], bFrom = new int[nb],
                b = new float[nb * SettlementSaveData.BuildingStride], foundTimer = _foundTimer, settlers = _settlers.Count,
                scale = buildingScale
            };
            for (int i = 0; i < nv; i++)
            {
                var v = _villages[i];
                int o = i * SettlementSaveData.VillageStride;
                d.vStage[i] = (int)v.stage;
                d.vLandmark[i] = v.landmark ? 1 : 0;
                d.v[o] = v.center.x; d.v[o + 1] = v.center.y; d.v[o + 2] = v.age; d.v[o + 3] = v.stageAge;
            }
            for (int i = 0; i < nb; i++)
            {
                var b = _buildings[i];
                int o = i * SettlementSaveData.BuildingStride;
                d.bKind[i] = (int)b.kind; d.bVariant[i] = b.variant; d.bState[i] = (int)b.state;
                d.bVillage[i] = _villages.IndexOf(b.village); d.bFrom[i] = b.from;
                d.b[o] = b.pos.x; d.b[o + 1] = b.pos.y; d.b[o + 2] = b.yaw; d.b[o + 3] = b.progress; d.b[o + 4] = b.timer; d.b[o + 5] = b.burn;
            }
            return d;
        }

        // A missing or empty block (every file from before the settlements) means an unsettled island: no
        // prehistory is invented for a saved island.
        public void Restore(SettlementSaveData d)
        {
            if (!Bind()) return;
            _rnd = new System.Random(SettlementSeed);
            ClearAll();
            _version = _surface.Version;
            _populated = true;
            _night = LifeEnvironment.NightAmount;
            if (d != null && d.vStage != null && d.v != null && d.vStage.Length > 0)
            {
                _foundTimer = d.foundTimer;
                int nv = Mathf.Min(Mathf.Min(d.vStage.Length, d.v.Length / SettlementSaveData.VillageStride), maxVillages);
                for (int i = 0; i < nv; i++)
                {
                    int o = i * SettlementSaveData.VillageStride;
                    bool landmark = d.vLandmark != null && i < d.vLandmark.Length && d.vLandmark[i] != 0;
                    _villages.Add(new Village
                    {
                        // Only a landmark village may stand at stage None; an older file without a stage
                        // describes a real village, which is at least a camp.
                        stage = (SettlementStage)Mathf.Clamp(d.vStage[i], landmark ? 0 : 1, 4),
                        landmark = landmark,
                        center = new Vector2(d.v[o], d.v[o + 1]), age = d.v[o + 2], stageAge = d.v[o + 3]
                    });
                }
                int nb = d.bKind != null && d.b != null ? Mathf.Min(d.bKind.Length, d.b.Length / SettlementSaveData.BuildingStride) : 0;
                for (int i = 0; i < nb; i++)
                {
                    int vi = d.bVillage != null && i < d.bVillage.Length ? d.bVillage[i] : 0;
                    if (vi < 0 || vi >= _villages.Count || d.bKind[i] < 0 || d.bKind[i] >= SettlementMeshes.KindCount) continue;
                    int o = i * SettlementSaveData.BuildingStride;
                    var b = new Building
                    {
                        kind = (BuildingKind)d.bKind[i], village = _villages[vi],
                        variant = d.bVariant != null && i < d.bVariant.Length ? Mathf.Abs(d.bVariant[i]) % SettlementMeshes.Variants : 0,
                        state = d.bState != null && i < d.bState.Length ? (BuildingState)Mathf.Clamp(d.bState[i], 0, 5) : BuildingState.Done,
                        from = d.bFrom != null && i < d.bFrom.Length && d.bFrom[i] < SettlementMeshes.KindCount ? d.bFrom[i] : -1,
                        pos = new Vector2(d.b[o], d.b[o + 1]), yaw = d.b[o + 2], progress = Mathf.Clamp01(d.b[o + 3]), timer = d.b[o + 4], burn = d.b[o + 5],
                        phase = Rand(0f, 6.28f)
                    };
                    b.baked = b.progress;
                    // A building alone in a landmark village is the milestone landmark; one that joined a real
                    // village is simply one of its buildings again.
                    b.landmark = b.village.landmark;
                    if (b.state == BuildingState.Upgrading && b.from < 0) b.state = BuildingState.Site;
                    _buildings.Add(b);
                }
                Rescale(d.scale > 0f ? d.scale : LegacyScale);
                foreach (var v in _villages) RecomputeRadius(v);
                EnforceCaps();
                OnShapeChanged();
                ReconcileSettlers(true);
            }
            _staticDirty = true;
            if (!Application.isPlaying) RebuildMeshes();
        }

        // A village saved with bigger houses would stand scattered over its old ground plan: every building moves
        // towards its hearth by the ratio of the scales where the ground there carries it (the footprints shrink
        // by the same ratio, so nothing overlaps). Jetties, lighthouses and shrines keep their shore and summit.
        void Rescale(float savedScale)
        {
            float ratio = buildingScale / Mathf.Max(0.01f, savedScale);
            if (Mathf.Abs(ratio - 1f) < 0.02f) return;
            foreach (var b in _buildings)
            {
                if (SettlementMeshes.Spec(b.kind).natural || b.village == null) continue;
                Vector2 p = b.village.center + (b.pos - b.village.center) * ratio;
                bool plot = SettlementMeshes.Spec(b.kind).plot;
                if (IsValidSite(p, SettlementMeshes.Spec(b.kind).radius * ScaleOf(b.kind), siteMinHeight, siteMaxHeight + (plot ? 0.4f : 0f), plot ? 0.6f : maxSlope, true))
                    b.pos = p;
            }
        }
    }
}
