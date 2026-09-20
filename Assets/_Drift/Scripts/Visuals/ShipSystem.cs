using Drift.Core;
using Drift.Islands;
using Drift.Tectonics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Drift.Visuals
{
    // Boats and flotsam around the player, one dynamic Drift/VertexColor mesh ("SeaShips"). Ships are seeded from
    // a wrapped world grid, sail between islands (mooring at an IDockProvider dock when the island has one,
    // otherwise anchoring where the real shelf gets shallow), steer around Island.All and the player, furl
    // their sails in storms and light a lantern at night. They are scenery without colliders: an island that
    // reaches a boat faster than it can get away throws it on its beach, where it rides along in the island's
    // body space until it refloats (ShipSystem.Beaching.cs); slower contacts shove the hull aside.
    // Flotsam lives in ShipSystem.Flotsam.cs and shares the mesh.
    [ExecuteAlways]
    [DefaultExecutionOrder(213)]
    public partial class ShipSystem : MonoBehaviour
    {
        public enum ShipKind { SailBoat, FishingBoat, TradingCog, RowBoat }
        public enum ShipState { Sailing, Moored, Fishing, Crossing, Beached }

        public WaterFeedback water;
        public FishSystem fish;
        public SeaLifeSystem seaLife;
        public int seed = 41;
        public float cellSize = 60f;
        public float worldPeriod = 660f;
        public float spawnRadius = 100f;
        public float fadeBand = 15f;
        public float recycleRadius = 120f;
        [Range(0f, 1f)] public float density = 0.8f;
        public int maxShips = 6;
        public int maxSailBoats = 3;
        public int maxFishingBoats = 2;
        public int maxCogs = 1;
        public int maxRowBoats = 2;
        public float cogMinDistance = 45f;
        public float respawnSeconds = 90f;
        [Range(0f, 1f)] public float respawnMinRange = 0.5f;
        public float rebuildRate = 15f;
        public float detailDistance = 70f;
        public int maxVerts = 2400;
        public float sailSpeed = 1.8f;
        public float fishingSpeed = 1.3f;
        public float cogSpeed = 1.6f;
        public float rowSpeed = 0.7f;
        public float stormSpeedFactor = 0.3f;
        public float playerMargin = 3f;
        public float islandMargin = 2.5f;
        public float hullDraft = -0.5f;
        public float anchorDepth = -0.7f;
        public float mooredSecondsMin = 15f;
        public float mooredSecondsMax = 32f;
        public float fishingSecondsMin = 25f;
        public float fishingSecondsMax = 45f;
        public float wakeLife = 2.6f;
        public Color lanternColor = new Color(3.2f, 2.3f, 0.9f);
        public int maxBeachedPerIsland = 4;
        public int maxRideVerts = 1100;
        public float wakeBobRange = 16f;
        public float earlyTackLead = 1.5f;
        public float playerDockMaxSpeed = 0.4f;

        // Edit mode treats every island as standing still; verification turns the real velocities on.
        [System.NonSerialized] public bool debugLiveVelocity;
        [System.NonSerialized] public float debugNight = -1f;
        [System.NonSerialized] public float debugStorm = -1f;

        const string ObjName = "SeaShips";
        // Hulls riding the player island live in a second mesh in the island's local space; its transform copies
        // the island's every frame, so they follow a fast island smoothly between the 15 Hz rebuilds.
        const string RideName = "SeaShipsRiding";
        const int WakePoints = 6;

        struct Ship
        {
            public bool active;
            public long key;
            public uint seed;
            public ShipKind kind;
            public ShipState state;
            public Vector2 pos, dir, dest, dockDir, course;
            public float speed, fade, timer, retarget, cooldown, sail, heel, storm, wakeDist;
            public bool sailsDown, lantern, hasDock;
            public Island target;
            public int wakeHead;
            // Riding an island (moored or beached): pose in the host's body space, world pose derived every step.
            public Island host;
            public Vector2 local, localDir, localFrom, localTo, localDirFrom, localDirTo;
            public int beachPhase;
            public float beachT, beachDuration, restRoll, restPitch, squeeze, grace, overdue, react, wakeBob, wakeBobTarget, net;
            public bool hauling;
        }

        Ship[] _ships;
        Vector2[] _wakePos, _wakeSide;
        float[] _wakeAge;
        readonly long[] _spent = new long[24];
        readonly float[] _spentAt = new float[24];
        int _spentCount;
        bool _scanned;
        readonly SeaObstacles _obstacles = new();
        readonly Vector3[] _steer = new Vector3[SeaObstacles.Max];
        SeaBatch _batch, _ride;
        Mesh _mesh, _rideMesh;
        GameObject _go, _rideGo;
        Material _mat;
        float _clock, _rebuildTimer, _scanTimer, _obstacleTimer;
        int _editTick;
        bool _dirty;
        Island _player;
        Vector2 _playerPos, _playerVel, _wind;
        float _playerRadius, _playerSpeed, _night, _period;
        bool _playerDockOpen, _anyMover;

        static SeaTemplate _tSailHull, _tSailRig, _tMainSail, _tJib, _tFishHull, _tFishRig, _tFishSail, _tCogHull, _tCogRig, _tCogSail,
            _tRowHull, _tRower, _tOar, _tLantern, _tFlag;

        public float Clock => _clock;
        public int VertexCount => (_batch != null ? _batch.vc : 0) + (_ride != null ? _ride.vc : 0);
        public int ShipSlots => _ships != null ? _ships.Length : 0;
        public bool ShipActive(int i) => _ships[i].active;
        public ShipKind ShipKindOf(int i) => _ships[i].kind;
        public ShipState ShipStateOf(int i) => _ships[i].state;
        public Vector2 ShipPosition(int i) => _ships[i].pos;
        public Vector2 ShipHeading(int i) => _ships[i].dir;
        public bool ShipSailsDown(int i) => _ships[i].active && (_ships[i].sailsDown || _ships[i].state == ShipState.Moored || _ships[i].state == ShipState.Fishing || _ships[i].state == ShipState.Beached);
        public bool ShipLanternOn(int i) => _ships[i].active && _ships[i].lantern;
        public bool ShipDocked(int i) => _ships[i].active && _ships[i].state == ShipState.Moored && _ships[i].hasDock;
        public Island ShipTarget(int i) => _ships[i].target;
        public float ShipFade(int i) => _ships[i].fade;
        public int ActiveShips => CountShips(-1);
        public int CountOf(ShipKind k) => CountShips((int)k);

        public static SeaKind SeaKindOf(ShipKind k)
        {
            switch (k)
            {
                case ShipKind.SailBoat: return SeaKind.SailBoat;
                case ShipKind.FishingBoat: return SeaKind.FishingBoat;
                case ShipKind.TradingCog: return SeaKind.TradingCog;
                default: return SeaKind.RowBoat;
            }
        }

        // Lantern of ship i while it is lit (SeaLifeSystem paints the glow on the water); size scales with the ship.
        public bool TryGetLantern(int i, out Vector3 pos, out float size)
        {
            pos = default;
            size = 0f;
            if (_ships == null || i < 0 || i >= _ships.Length || !_ships[i].active || !_ships[i].lantern || _ships[i].fade < 0.2f) return false;
            ref Ship s = ref _ships[i];
            Vector2 p = s.pos + s.dir * LanternOffset(s.kind);
            pos = new Vector3(p.x, 0.6f, p.y);
            size = (s.kind == ShipKind.TradingCog ? 1.6f : s.kind == ShipKind.RowBoat ? 0.7f : 1f) * s.fade;
            return true;
        }

        static float LanternOffset(ShipKind k) => k == ShipKind.TradingCog ? -2.5f : k == ShipKind.RowBoat ? 0.62f : k == ShipKind.FishingBoat ? -0.9f : -0.98f;

        // World positions of the visible ships of a kind (journal / tap integration). Returns the number written.
        public int PositionsOf(ShipKind kind, Vector3[] buffer)
        {
            int n = 0;
            if (_ships == null || buffer == null) return 0;
            for (int i = 0; i < _ships.Length && n < buffer.Length; i++)
                if (_ships[i].active && _ships[i].kind == kind && _ships[i].fade > 0.3f)
                    buffer[n++] = new Vector3(_ships[i].pos.x, 0.3f, _ships[i].pos.y);
            return n;
        }

        int CountShips(int kind)
        {
            int n = 0;
            if (_ships != null)
                for (int i = 0; i < _ships.Length; i++)
                    if (_ships[i].active && (kind < 0 || (int)_ships[i].kind == kind)) n++;
            return n;
        }

        // ---- lifecycle ----

        void OnEnable()
        {
            EnsureArrays();
            Resolve();
            Island.Merged -= OnIslandMerged;
            Island.Merged += OnIslandMerged;
            _dirty = true;
            Step(0f);
        }

        void OnDisable()
        {
            Island.Merged -= OnIslandMerged;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i);
                if (c.name == ObjName || c.name == RideName) DestroyImmediate(c.gameObject);
            }
            _go = null;
            _rideGo = null;
            if (_mesh != null) DestroyImmediate(_mesh);
            if (_rideMesh != null) DestroyImmediate(_rideMesh);
            if (_mat != null) DestroyImmediate(_mat);
            _mesh = null;
            _rideMesh = null;
            _mat = null;
        }

        void Update()
        {
            if (!Application.isPlaying)
            {
                if (_dirty || (_editTick++ % 30) == 0) Step(0f);
                return;
            }
            Step(Time.deltaTime);
        }

        void Resolve()
        {
            if (water == null) water = GetComponent<WaterFeedback>();
            if (water == null) water = FindAnyObjectByType<WaterFeedback>();
            if (fish == null) fish = GetComponent<FishSystem>();
            if (fish == null) fish = FindAnyObjectByType<FishSystem>();
            if (seaLife == null) seaLife = GetComponent<SeaLifeSystem>();
            if (seaLife == null) seaLife = FindAnyObjectByType<SeaLifeSystem>();
            var streamer = FindAnyObjectByType<WorldStreamer>();
            _period = streamer != null && streamer.WorldSize > 0f ? streamer.WorldSize : worldPeriod;
        }

        void EnsureArrays()
        {
            maxShips = Mathf.Clamp(maxShips, 1, 12);
            if (_ships == null || _ships.Length != maxShips)
            {
                _ships = new Ship[maxShips];
                _wakePos = new Vector2[maxShips * WakePoints];
                _wakeSide = new Vector2[maxShips * WakePoints];
                _wakeAge = new float[maxShips * WakePoints];
                for (int i = 0; i < _wakeAge.Length; i++) _wakeAge[i] = 1000f;
            }
            maxVerts = Mathf.Clamp(maxVerts, 256, 4000);
            if (_batch == null || _batch.verts.Length != maxVerts) _batch = new SeaBatch(maxVerts, true);
            maxRideVerts = Mathf.Clamp(maxRideVerts, 0, maxVerts);
            if (_ride == null || _ride.verts.Length != Mathf.Max(1, maxRideVerts)) _ride = new SeaBatch(Mathf.Max(1, maxRideVerts), true);
            if (_period <= 0f) _period = worldPeriod;
            EnsureFlotsam();
            // Guarded here, not inside: the builders' lambdas capture locals, so merely entering them allocates a closure.
            if (_tSailHull == null) BuildTemplates();
            if (_tLog == null) BuildFlotsamTemplates();
        }

        GameObject EnsureChild(string childName)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                var c = transform.GetChild(i);
                if (c.name == childName) return c.gameObject;
            }
            var go = new GameObject(childName);
            go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        void EnsureObject()
        {
            if (_go == null) _go = EnsureChild(ObjName);
            if (_rideGo == null) _rideGo = EnsureChild(RideName);
            if (_mat == null)
            {
                var shader = Shader.Find("Drift/VertexColor");
                if (shader == null) Debug.LogError("ShipSystem: shader 'Drift/VertexColor' not found.");
                _mat = new Material(shader) { name = "SeaShips", hideFlags = HideFlags.HideAndDontSave };
                _mat.SetFloat("_Cull", 0f);
            }
            if (_mesh == null) { _mesh = new Mesh { name = ObjName, hideFlags = HideFlags.DontSave }; _mesh.MarkDynamic(); }
            if (_rideMesh == null) { _rideMesh = new Mesh { name = RideName, hideFlags = HideFlags.DontSave }; _rideMesh.MarkDynamic(); }
            _go.GetComponent<MeshRenderer>().sharedMaterial = _mat;
            _go.GetComponent<MeshFilter>().sharedMesh = _mesh;
            _rideGo.GetComponent<MeshRenderer>().sharedMaterial = _mat;
            _rideGo.GetComponent<MeshFilter>().sharedMesh = _rideMesh;
        }

        // ---- templates (real units, bow towards +z, waterline at y = 0) ----

        static readonly Color Wood = new Color(0.55f, 0.38f, 0.22f);
        static readonly Color DarkWood = new Color(0.36f, 0.24f, 0.14f);
        static readonly Color Deck = new Color(0.76f, 0.6f, 0.4f);

        // z stations from stern to bow, half beam and sheer height per station.
        // The deck goes into `deckShape` (the untinted rig template) so the per-ship hull colour does not stain the planks.
        static void Hull(SeaShape s, SeaShape deckShape, float[] z, float[] beam, float[] sheer, float keel, float chine, Color side, Color trim, Color deck, float deckDrop)
        {
            s.center = new Vector3(0f, (sheer[0] + keel) * 0.5f, 0f);
            int n = z.Length;
            for (int i = 0; i + 1 < n; i++)
            {
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    Vector3 g0 = new Vector3(sgn * beam[i], sheer[i], z[i]), g1 = new Vector3(sgn * beam[i + 1], sheer[i + 1], z[i + 1]);
                    Vector3 w0 = new Vector3(sgn * beam[i] * 0.92f, sheer[i] * 0.45f, z[i]), w1 = new Vector3(sgn * beam[i + 1] * 0.92f, sheer[i + 1] * 0.45f, z[i + 1]);
                    Vector3 c0 = new Vector3(sgn * beam[i] * chine, keel, z[i]), c1 = new Vector3(sgn * beam[i + 1] * chine, keel, z[i + 1] - (i + 2 == n ? 0.12f * (z[n - 1] - z[0]) : 0f));
                    s.Quad(g0, g1, w1, w0, trim);
                    s.Quad(w0, w1, c1, c0, side);
                }
                float d0 = sheer[i] - deckDrop, d1 = sheer[i + 1] - deckDrop;
                deckShape.Sheet(new Vector3(-beam[i] * 0.97f, d0, z[i]), new Vector3(-beam[i + 1] * 0.97f, d1, z[i + 1]),
                    new Vector3(beam[i + 1] * 0.97f, d1, z[i + 1]), new Vector3(beam[i] * 0.97f, d0, z[i]), deck, Vector3.up);
            }
            s.center = new Vector3(0f, 0f, z[0] + 0.3f);
            s.Quad(new Vector3(-beam[0], sheer[0], z[0]), new Vector3(beam[0], sheer[0], z[0]),
                new Vector3(beam[0] * chine, keel, z[0]), new Vector3(-beam[0] * chine, keel, z[0]), side);
        }

        static void Mast(SeaShape s, Vector3 foot, float height, float thick, Color col) =>
            s.Prism(foot, thick * 0.6f, thick * 0.45f, height, 3, col, col, false);

        // Boom / yard / gaff as two crossed strips: 8 vertices instead of a 20-vertex box.
        static void Spar(SeaShape s, Vector3 a, Vector3 b, float thick, Color col)
        {
            Vector3 d = (b - a).normalized;
            Vector3 u = Mathf.Abs(d.y) > 0.9f ? Vector3.right : Vector3.up;
            Vector3 side = Vector3.Cross(d, u).normalized * (thick * 0.5f);
            Vector3 up = Vector3.Cross(side, d).normalized * (thick * 0.5f);
            s.Sheet(a - side, a + side, b + side, b - side, col, up.normalized);
            s.Sheet(a - up, a + up, b + up, b - up, col * 0.85f, side.normalized);
        }

        static void BuildTemplates()
        {
            if (_tSailHull != null) return;
            Color white = Color.white;

            var sh = new SeaShape();
            var sr = new SeaShape();
            Hull(sh, sr, new[] { -1.05f, -0.55f, 0.1f, 0.65f, 1.15f }, new[] { 0.26f, 0.36f, 0.4f, 0.28f, 0f },
                new[] { 0.3f, 0.26f, 0.25f, 0.3f, 0.42f }, -0.16f, 0.4f, white, new Color(0.55f, 0.55f, 0.55f), Deck, 0.07f);
            sr.Box(new Vector3(0f, 0.3f, -0.35f), new Vector3(0.36f, 0.16f, 0.5f), new Color(0.9f, 0.85f, 0.75f));
            _tSailHull = sh.Build();
            Mast(sr, new Vector3(0f, 0.2f, 0.2f), 2.5f, 0.07f, Wood);
            Spar(sr, new Vector3(0f, 0.72f, 0.2f), new Vector3(0f, 0.72f, -0.95f), 0.06f, Wood);
            _tSailRig = sr.Build();
            var ms = new SeaShape();
            Vector3 tack = new Vector3(0f, 0f, -0.06f), head = new Vector3(0f, 1.85f, -0.06f), clew = new Vector3(0f, 0f, -1.1f), belly = new Vector3(0.2f, 0.6f, -0.5f);
            Vector3 sn = Vector3.right;
            ms.SheetTri(tack, head, belly, white, sn);
            ms.SheetTri(head, clew, belly, new Color(0.93f, 0.93f, 0.93f), sn);
            ms.SheetTri(tack, belly, clew, new Color(0.86f, 0.86f, 0.86f), sn);
            _tMainSail = ms.Build();
            var jb = new SeaShape();
            jb.SheetTri(new Vector3(0f, 0f, 0.95f), new Vector3(0f, 1.75f, 0.05f), new Vector3(0.12f, 0.15f, 0.12f), new Color(0.95f, 0.95f, 0.95f), sn);
            _tJib = jb.Build();

            var fh = new SeaShape();
            var fr = new SeaShape();
            Hull(fh, fr, new[] { -0.95f, -0.5f, 0.1f, 0.6f, 0.95f }, new[] { 0.34f, 0.43f, 0.45f, 0.32f, 0f },
                new[] { 0.34f, 0.3f, 0.3f, 0.36f, 0.5f }, -0.18f, 0.45f, white, new Color(0.95f, 0.92f, 0.85f), Deck, 0.08f);
            _tFishHull = fh.Build();
            fr.Box(new Vector3(0f, 0.5f, -0.45f), new Vector3(0.5f, 0.5f, 0.55f), new Color(0.93f, 0.9f, 0.82f));
            fr.Box(new Vector3(0f, 0.79f, -0.45f), new Vector3(0.6f, 0.08f, 0.68f), new Color(0.62f, 0.25f, 0.2f));
            Mast(fr, new Vector3(0f, 0.25f, 0.25f), 1.5f, 0.06f, Wood);
            Spar(fr, new Vector3(0f, 1.45f, 0.35f), new Vector3(0f, 1.45f, -0.35f), 0.05f, Wood);
            fr.Box(new Vector3(0.18f, 0.32f, 0.55f), new Vector3(0.2f, 0.16f, 0.2f), new Color(0.62f, 0.47f, 0.28f));
            Mast(fr, new Vector3(0f, 0.3f, -0.9f), 0.75f, 0.035f, DarkWood);
            _tFishRig = fr.Build();
            var fs = new SeaShape();
            fs.Sheet(new Vector3(0f, 0f, 0.3f), new Vector3(0f, 0f, -0.4f), new Vector3(0.1f, -0.95f, -0.45f), new Vector3(0.1f, -0.95f, 0.25f), white, sn);
            _tFishSail = fs.Build();

            var ch = new SeaShape();
            var cr = new SeaShape();
            Hull(ch, cr, new[] { -2.5f, -1.6f, -0.3f, 1.1f, 2f, 2.7f }, new[] { 0.62f, 0.85f, 0.95f, 0.85f, 0.5f, 0f },
                new[] { 1.05f, 0.75f, 0.62f, 0.7f, 0.95f, 1.25f }, -0.3f, 0.5f, white, DarkWood, Deck, 0.12f);
            ch.Box(new Vector3(0f, 1.2f, -2.05f), new Vector3(1.3f, 0.42f, 1.05f), Wood);
            ch.Box(new Vector3(0f, 1.2f, 2.0f), new Vector3(0.85f, 0.3f, 0.8f), Wood);
            _tCogHull = ch.Build();
            Mast(cr, new Vector3(0f, 0.5f, 0.1f), 4.6f, 0.13f, Wood);
            Spar(cr, new Vector3(-1.45f, 4.25f, 0.1f), new Vector3(1.45f, 4.25f, 0.1f), 0.11f, Wood);
            cr.Prism(new Vector3(0f, 4.55f, 0.1f), 0.2f, 0.27f, 0.28f, 5, DarkWood, DarkWood, false);
            cr.SheetTri(new Vector3(0f, 5.1f, 0.1f), new Vector3(0f, 5.45f, 0.1f), new Vector3(0f, 5.27f, -0.55f), new Color(0.85f, 0.2f, 0.2f), sn);
            _tCogRig = cr.Build();
            var cs = new SeaShape();
            Vector3 fwdN = Vector3.forward;
            for (int k = 0; k < 4; k++)
            {
                float x0 = -1.35f + k * 0.675f, x1 = x0 + 0.675f;
                float b0 = 0.35f * (1f - Mathf.Abs(x0 / 1.35f) * Mathf.Abs(x0 / 1.35f)), b1 = 0.35f * (1f - Mathf.Abs(x1 / 1.35f) * Mathf.Abs(x1 / 1.35f));
                Color stripe = (k & 1) == 0 ? new Color(0.96f, 0.93f, 0.85f) : new Color(0.78f, 0.25f, 0.2f);
                cs.Sheet(new Vector3(x0, 0f, 0f), new Vector3(x1, 0f, 0f), new Vector3(x1, -1.4f, b1), new Vector3(x0, -1.4f, b0), stripe, fwdN);
                cs.Sheet(new Vector3(x0, -1.4f, b0), new Vector3(x1, -1.4f, b1), new Vector3(x1 * 0.93f, -2.75f, b1 * 0.4f), new Vector3(x0 * 0.93f, -2.75f, b0 * 0.4f), stripe * 0.92f, fwdN);
            }
            _tCogSail = cs.Build();

            var rh = new SeaShape();
            var rw = new SeaShape();
            Hull(rh, rw, new[] { -0.62f, -0.3f, 0.15f, 0.45f, 0.72f }, new[] { 0.2f, 0.27f, 0.28f, 0.2f, 0f },
                new[] { 0.2f, 0.18f, 0.18f, 0.2f, 0.27f }, -0.1f, 0.45f, white, DarkWood, new Color(0.5f, 0.36f, 0.22f), 0.13f);
            rw.Sheet(new Vector3(-0.25f, 0.15f, -0.26f), new Vector3(-0.25f, 0.15f, -0.14f), new Vector3(0.25f, 0.15f, -0.14f), new Vector3(0.25f, 0.15f, -0.26f), Deck, Vector3.up);
            rw.Sheet(new Vector3(-0.21f, 0.15f, 0.24f), new Vector3(-0.21f, 0.15f, 0.36f), new Vector3(0.21f, 0.15f, 0.36f), new Vector3(0.21f, 0.15f, 0.24f), Deck, Vector3.up);
            _tRowHull = rh.Build();
            rw.Box(new Vector3(0f, 0.33f, -0.2f), new Vector3(0.2f, 0.34f, 0.16f), new Color(0.3f, 0.45f, 0.7f));
            rw.Box(new Vector3(0f, 0.58f, -0.2f), new Vector3(0.14f, 0.14f, 0.14f), new Color(0.93f, 0.75f, 0.6f));
            rw.Sheet(new Vector3(-0.13f, 0.66f, -0.33f), new Vector3(-0.13f, 0.66f, -0.07f), new Vector3(0.13f, 0.66f, -0.07f), new Vector3(0.13f, 0.66f, -0.33f), new Color(0.85f, 0.75f, 0.4f), Vector3.up);
            _tRower = rw.Build();
            var oar = new SeaShape();
            oar.Sheet(new Vector3(0f, 0.02f, 0.015f), new Vector3(0.62f, 0.02f, 0.015f), new Vector3(0.62f, 0.02f, -0.015f), new Vector3(0f, 0.02f, -0.015f), Wood, Vector3.up);
            oar.Sheet(new Vector3(0.62f, 0.02f, 0.05f), new Vector3(0.86f, 0.02f, 0.05f), new Vector3(0.86f, 0.02f, -0.05f), new Vector3(0.62f, 0.02f, -0.05f), Deck, Vector3.up);
            _tOar = oar.Build();

            var la = new SeaShape();
            la.Box(Vector3.zero, new Vector3(0.13f, 0.16f, 0.13f), white);
            _tLantern = la.Build();

            var fg = new SeaShape();
            Spar(fg, Vector3.zero, new Vector3(0f, 0.75f, 0f), 0.035f, Wood);
            fg.SheetTri(new Vector3(0f, 0.75f, 0f), new Vector3(0f, 0.5f, 0f), new Vector3(0f, 0.62f, -0.42f), new Color(0.95f, 0.3f, 0.2f), Vector3.right);
            _tFlag = fg.Build();
        }

        // ---- spawning / recycling ----

        bool IsOccupied(long key)
        {
            for (int i = 0; i < _ships.Length; i++) if (_ships[i].active && _ships[i].key == key) return true;
            return false;
        }

        bool IsSpent(long key)
        {
            for (int i = 0; i < _spentCount; i++) if (_spent[i] == key) return true;
            return false;
        }

        void MarkSpent(long key)
        {
            if (_spentCount == _spent.Length)
            {
                for (int i = 1; i < _spent.Length; i++) { _spent[i - 1] = _spent[i]; _spentAt[i - 1] = _spentAt[i]; }
                _spentCount--;
            }
            _spent[_spentCount] = key;
            _spentAt[_spentCount++] = _clock;
        }

        // A cell whose inhabitants wandered off stays empty until the player has left it behind or, for a player
        // who stays put, until respawnSeconds have passed (the newcomers then only appear far out, see Scan).
        void CleanSpent()
        {
            float far = recycleRadius + _playerRadius + cellSize;
            for (int i = _spentCount - 1; i >= 0; i--)
            {
                int cx = (int)(_spent[i] >> 32), cy = (int)_spent[i];
                Vector2 c = new Vector2((cx + 0.5f) * cellSize, (cy + 0.5f) * cellSize);
                if ((c - _playerPos).sqrMagnitude <= far * far && _clock - _spentAt[i] < respawnSeconds) continue;
                for (int j = i + 1; j < _spentCount; j++) { _spent[j - 1] = _spent[j]; _spentAt[j - 1] = _spentAt[j]; }
                _spentCount--;
            }
        }

        public static ShipKind KindForRoll(float r)
        {
            if (r < 0.42f) return ShipKind.SailBoat;
            if (r < 0.67f) return ShipKind.FishingBoat;
            if (r < 0.88f) return ShipKind.RowBoat;
            return ShipKind.TradingCog;
        }

        public int CapOf(ShipKind k)
        {
            switch (k)
            {
                case ShipKind.SailBoat: return maxSailBoats;
                case ShipKind.FishingBoat: return maxFishingBoats;
                case ShipKind.TradingCog: return maxCogs;
                default: return maxRowBoats;
            }
        }

        // What a world cell holds, independent of the pool: same answer on every copy of the torus.
        public bool CellContent(int cx, int cy, out ShipKind kind, out Vector2 point, out uint h)
        {
            h = SeaMath.CellSeed(seed, cx, cy, SeaMath.WrapCells(_period > 0f ? _period : worldPeriod, cellSize));
            point = SeaMath.CellPoint(cx, cy, cellSize, h);
            kind = KindForRoll(SeaMath.Rand(h, 3));
            return SeaMath.Rand(h, 2) <= density;
        }

        void Scan()
        {
            float far = recycleRadius + _playerRadius;
            for (int i = 0; i < _ships.Length; i++)
            {
                if (!_ships[i].active) continue;
                if ((_ships[i].pos - _playerPos).sqrMagnitude > far * far)
                {
                    MarkSpent(_ships[i].key);
                    _ships[i].active = false;
                    _ships[i].target = null;
                    _ships[i].host = null;
                    _dirty = true;
                }
            }
            CleanSpent();

            float near = spawnRadius + _playerRadius;
            int x0 = Mathf.FloorToInt((_playerPos.x - near) / cellSize), x1 = Mathf.FloorToInt((_playerPos.x + near) / cellSize);
            int y0 = Mathf.FloorToInt((_playerPos.y - near) / cellSize), y1 = Mathf.FloorToInt((_playerPos.y + near) / cellSize);
            for (int cy = y0; cy <= y1; cy++)
                for (int cx = x0; cx <= x1; cx++)
                {
                    if (!CellContent(cx, cy, out ShipKind kind, out Vector2 sp, out uint h)) continue;
                    float d2 = (sp - _playerPos).sqrMagnitude;
                    if (d2 > near * near || (_scanned && d2 < near * near * respawnMinRange * respawnMinRange)) continue;
                    long key = SeaMath.Key(cx, cy);
                    if (IsOccupied(key) || IsSpent(key)) continue;
                    if (CountShips((int)kind) >= CapOf(kind)) continue;
                    if (kind == ShipKind.TradingCog && d2 < (cogMinDistance + _playerRadius) * (cogMinDistance + _playerRadius)) continue;
                    if (_obstacles.Inside(sp, 4f)) { MarkSpent(key); continue; }
                    if (kind == ShipKind.RowBoat && NearestIsland(sp, 45f) == null) continue;
                    int slot = -1;
                    for (int i = 0; i < _ships.Length; i++) if (!_ships[i].active) { slot = i; break; }
                    if (slot < 0) { _scanned = true; return; }
                    Activate(slot, key, sp, h, kind);
                }
            _scanned = true;
        }

        void Activate(int slot, long key, Vector2 sp, uint h, ShipKind kind)
        {
            ref Ship s = ref _ships[slot];
            s = default;
            s.active = true;
            s.key = key;
            s.seed = h;
            s.kind = kind;
            s.pos = sp;
            float a = SeaMath.Rand(h, 4) * Mathf.PI * 2f;
            s.dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            s.course = s.dir;
            s.state = kind == ShipKind.TradingCog ? ShipState.Crossing : ShipState.Sailing;
            s.sail = 1f;
            s.speed = 0f;
            if (kind == ShipKind.RowBoat)
            {
                s.target = NearestIsland(sp, 45f);
                if (s.target != null)
                {
                    Vector2 r = sp - s.target.PlanarPosition;
                    if (r.sqrMagnitude < 0.01f) r = Vector2.right;
                    r.Normalize();
                    s.pos = s.target.PlanarPosition + r * (s.target.BoundingRadius + 1f);
                    s.dir = new Vector2(-r.y, r.x);
                }
            }
            for (int k = 0; k < WakePoints; k++) _wakeAge[slot * WakePoints + k] = 1000f;
            _dirty = true;
        }

        // Places a ship at a spot, replacing the farthest one if the pool is full (verification).
        public int SpawnAt(ShipKind kind, Vector2 pos, Vector2 dir)
        {
            EnsureArrays();
            int slot = -1;
            float far = -1f;
            for (int i = 0; i < _ships.Length; i++)
            {
                if (!_ships[i].active) { slot = i; break; }
                float d = (_ships[i].pos - _playerPos).sqrMagnitude;
                if (d > far) { far = d; slot = i; }
            }
            uint h = SeaMath.Hash((uint)seed, (uint)(pos.x * 13f + pos.y * 7f) + (uint)kind);
            Activate(slot, long.MinValue + slot, pos, h, kind);
            _ships[slot].pos = pos;
            _ships[slot].dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right;
            _ships[slot].course = _ships[slot].dir;
            _ships[slot].fade = 1f;
            return slot;
        }

        Island NearestIsland(Vector2 pos, float range)
        {
            Island best = null;
            float bestD = range;
            for (int i = 1; i < _obstacles.count; i++)
            {
                var isl = _obstacles.islands[i];
                if (isl == null || isl.IsEmerging) continue;
                float d = (isl.PlanarPosition - pos).magnitude - isl.BoundingRadius;
                if (d < bestD) { bestD = d; best = isl; }
            }
            return best;
        }

        // Destinations stay inside the visible range around the player, so traffic keeps circulating near it
        // instead of sailing off and leaving the sea empty.
        bool Reachable(int index, Vector2 from, float range, Island except)
        {
            Island isl = _obstacles.islands[index];
            // The player island only counts while it rests and owns a jetty.
            if (index == 0 && !_playerDockOpen) return false;
            if (isl == null || isl == except || isl.IsEmerging) return false;
            if ((isl.PlanarPosition - from).sqrMagnitude > range * range) return false;
            float leash = spawnRadius - fadeBand + _playerRadius;
            return (isl.PlanarPosition - _playerPos).sqrMagnitude < leash * leash;
        }

        Island PickIsland(Vector2 from, float range, uint h, Island except)
        {
            int n = 0;
            for (int i = 0; i < _obstacles.count; i++) if (Reachable(i, from, range, except)) n++;
            if (n == 0) return null;
            int pick = (int)(h % (uint)n);
            for (int i = 0; i < _obstacles.count; i++)
                if (Reachable(i, from, range, except) && pick-- == 0) return _obstacles.islands[i];
            return null;
        }

        // Sends a ship to an island right away (verification).
        public void SetTarget(int ship, Island isl)
        {
            _ships[ship].target = isl;
            _ships[ship].state = ShipState.Sailing;
            _ships[ship].host = null;
            _ships[ship].hasDock = false;
            _ships[ship].cooldown = 0f;
            _ships[ship].retarget = 0f;
        }

        int ObstacleIndex(Island isl)
        {
            if (isl == null) return -1;
            for (int i = 0; i < _obstacles.count; i++) if (_obstacles.islands[i] == isl) return i;
            return -1;
        }

        // ---- stepping ----

        public void Step(float dt)
        {
            EnsureArrays();
            if (water == null) Resolve();
            _clock += dt;

            _player = water != null ? water.FindPlayer() : null;
            if (_player != null && !_player.IsSunk)
            {
                _playerPos = _player.PlanarPosition;
                _playerRadius = _player.BoundingRadius;
                _playerVel = Vel(_player);
            }
            else
            {
                _player = null;
                _playerRadius = 0f;
                _playerVel = Vector2.zero;
            }
            _playerSpeed = _playerVel.magnitude;
            _night = debugNight >= 0f ? debugNight : LifeEnvironment.NightAmount;
            _wind = water != null ? water.Wind : LifeEnvironment.Wind;

            _obstacleTimer -= dt;
            if (_obstacleTimer <= 0f || dt <= 0f)
            {
                _obstacles.Refresh(_player, _playerPos, recycleRadius + _playerRadius + 60f);
                _obstacleTimer = 0.5f;
                _playerDockOpen = _player != null && _playerSpeed < playerDockMaxSpeed
                    && _player.TryGetComponent<IDockProvider>(out var pd) && pd.TryGetDockWorld(out _, out _);
            }
            else _obstacles.Update();
            BuildSteer();

            _scanTimer -= dt;
            if (_scanTimer <= 0f || dt <= 0f)
            {
                Scan();
                ScanFlotsam();
                _scanTimer = 0.5f;
            }

            bool any = false;
            for (int i = 0; i < _ships.Length; i++)
            {
                if (!_ships[i].active) continue;
                any = true;
                StepShip(i, dt);
            }
            any |= StepFlotsam(dt);
            SyncRide();

            _rebuildTimer += dt;
            float interval = rebuildRate > 0f ? 1f / rebuildRate : 0f;
            if (_dirty || (_rebuildTimer >= interval && (any || _batch.vc > 0)))
            {
                _rebuildTimer = 0f;
                _dirty = false;
                Rebuild();
            }
        }

        float StormAt(Vector2 pos)
        {
            if (debugStorm >= 0f) return debugStorm;
            var storms = StormSystem.Instance;
            if (storms != null) return storms.IntensityAt(pos);
            return LifeEnvironment.Storm;
        }

        static Vector2 Rotate(Vector2 v, float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        void StepShip(int idx, float dt)
        {
            ref Ship s = ref _ships[idx];
            float edge = (s.pos - _playerPos).magnitude - _playerRadius;
            float fadeTarget = SeaMath.EdgeFade(edge, spawnRadius - fadeBand, spawnRadius);
            s.fade = dt > 0f ? Mathf.MoveTowards(s.fade, fadeTarget, dt * 0.8f) : fadeTarget;

            s.retarget -= dt;
            if (s.retarget <= 0f || dt <= 0f)
            {
                s.retarget = 0.5f;
                s.storm = StormAt(s.pos);
                s.sailsDown = SeaMath.SailsDown(s.storm, s.sailsDown);
                s.lantern = SeaMath.LanternOn(_night, s.lantern);
                if (s.target != null && (!s.target.isActiveAndEnabled || s.target.IsSunk)) s.target = null;
                if (s.target != null && s.target == _player && s.state == ShipState.Sailing && _playerSpeed > 1f) s.target = null;
                Disturbance(ref s);
                if (dt > 0f)
                {
                    Plan(ref s);
                    if (s.state == ShipState.Beached) CheckBeachSpot(idx);
                }
            }
            if (dt <= 0f)
            {
                if (HostAlive(s.host) && (s.state == ShipState.Beached || s.state == ShipState.Moored)) FollowHost(ref s);
                return;
            }
            if (s.cooldown > 0f) s.cooldown -= dt;
            if (s.grace > 0f) s.grace -= dt;
            if (s.react > 0f) s.react -= dt;
            s.timer -= dt;
            s.wakeBob = Mathf.MoveTowards(s.wakeBob, s.wakeBobTarget, dt * 0.8f);
            s.net = Mathf.MoveTowards(s.net, s.state == ShipState.Fishing && !s.hauling ? 1f : 0f, dt * 0.45f);

            bool furled = s.sailsDown || s.state == ShipState.Moored || s.state == ShipState.Fishing;
            s.sail = Mathf.MoveTowards(s.sail, s.state == ShipState.Beached ? 0.3f : furled ? 0f : 1f, dt * 0.6f);

            if (s.state == ShipState.Beached)
            {
                StepBeached(idx, dt);
                s.heel = Mathf.Lerp(s.heel, 0f, 1f - Mathf.Exp(-3f * dt));
                for (int k = 0; k < WakePoints; k++) _wakeAge[idx * WakePoints + k] += dt;
                return;
            }

            Vector2 want = s.dir;
            float speed = 0f;
            int ignore = -1;
            switch (s.state)
            {
                case ShipState.Crossing:
                    want = s.course;
                    speed = cogSpeed;
                    break;
                case ShipState.Moored:
                    StepMoored(ref s, dt);
                    break;
                case ShipState.Fishing:
                {
                    Vector2 to = s.dest - s.pos;
                    float d = to.magnitude;
                    if (d > 2.5f) { want = to / d; speed = 0.45f; }
                    else { want = Rotate(s.dir, 0.15f * dt); speed = 0.08f; }
                    if (s.timer <= 0f)
                    {
                        s.state = ShipState.Sailing;
                        s.cooldown = s.hauling ? 35f : 25f;
                        s.hauling = false;
                    }
                    break;
                }
                default:
                    StepSailing(ref s, out want, out speed, out ignore);
                    break;
            }

            if (s.state != ShipState.Moored)
            {
                if (s.sailsDown) speed *= stormSpeedFactor;
                Vector2 steer = SteerFor(ref s, want, ignore);
                float turn = s.kind == ShipKind.TradingCog ? 0.5f : s.kind == ShipKind.RowBoat ? 1.6f : 1f;
                Vector2 nd = Vector2.Lerp(s.dir, steer, 1f - Mathf.Exp(-turn * dt));
                s.dir = nd.sqrMagnitude > 1e-6f ? nd.normalized : steer;
                s.speed = Mathf.MoveTowards(s.speed, speed, (speed > s.speed ? 0.5f : 1.2f) * dt);

                Vector2 next2 = s.pos + s.dir * (s.speed * dt);
                if (s.speed > 0f && _obstacles.Shallow(next2 + s.dir * 0.8f, hullDraft, out Island bank))
                {
                    Vector2 away = s.pos - bank.PlanarPosition;
                    if (away.sqrMagnitude > 1e-4f) s.dir = Vector2.Lerp(s.dir, away.normalized, 1f - Mathf.Exp(-3f * dt)).normalized;
                    s.speed *= Mathf.Exp(-2f * dt);
                    if (s.state == ShipState.Sailing && s.kind != ShipKind.RowBoat && bank == s.target && s.cooldown <= 0f)
                        Moor(ref s, bank, Mathf.Lerp(mooredSecondsMin, mooredSecondsMax, SeaMath.Rand(s.seed, 75 + (int)_clock)));
                }
                else s.pos = next2;
            }
            else s.speed = 0f;

            if (Contact(idx, dt)) return;

            Vector2 right = new Vector2(s.dir.y, -s.dir.x);
            float wr = Vector2.Dot(_wind, right);
            s.heel = Mathf.Lerp(s.heel, Mathf.Clamp(-wr * 0.22f, -0.32f, 0.32f) * s.sail * (s.kind == ShipKind.RowBoat ? 0f : 1f), 1f - Mathf.Exp(-1.5f * dt));
            if (s.speed > 0.25f && s.fade > 0.2f)
            {
                s.wakeDist += s.speed * dt;
                float spacing = s.kind == ShipKind.TradingCog ? 1.4f : 0.7f;
                if (s.wakeDist >= spacing)
                {
                    s.wakeDist = 0f;
                    int w = idx * WakePoints + s.wakeHead;
                    s.wakeHead = (s.wakeHead + 1) % WakePoints;
                    _wakePos[w] = s.pos - s.dir * HalfLength(s.kind);
                    _wakeSide[w] = right;
                    _wakeAge[w] = 0f;
                }
            }
            for (int k = 0; k < WakePoints; k++) _wakeAge[idx * WakePoints + k] += dt;
        }

        // Moving islands are steered around where they will be in a moment, so boats tack away visibly early.
        void BuildSteer()
        {
            _anyMover = false;
            for (int i = 0; i < _obstacles.count; i++)
            {
                Vector3 c = _obstacles.circles[i];
                Island isl = _obstacles.islands[i];
                if (isl != null && c.z > 0f)
                {
                    Vector2 v = i == 0 ? _playerVel : Vel(isl);
                    float sp = v.magnitude;
                    if (sp > 0.3f)
                    {
                        _anyMover = true;
                        Vector2 lead = Vector2.ClampMagnitude(v * earlyTackLead, 8f);
                        c.x += lead.x;
                        c.y += lead.y;
                        c.z += Mathf.Min(5f, sp);
                    }
                }
                _steer[i] = c;
            }
        }

        Vector2 SteerFor(ref Ship s, Vector2 want, int ignore)
        {
            bool cog = s.kind == ShipKind.TradingCog;
            Vector3 savedPlayer = _steer[0];
            if (savedPlayer.z > 0f) _steer[0].z = savedPlayer.z + (cog ? cogMinDistance * 0.6f : Mathf.Max(0f, playerMargin - islandMargin));
            Vector3 savedTarget = default;
            if (ignore >= 0) { savedTarget = ignore == 0 ? savedPlayer : _steer[ignore]; _steer[ignore].z = -1000f; }
            float look = (cog ? 25f : 12f) + (_anyMover && s.kind == ShipKind.SailBoat ? 8f : 0f);
            Vector2 steer = SeaMath.SteerAvoid(s.pos, want, _steer, _obstacles.count, look, cog ? islandMargin + 3f : islandMargin);
            if (ignore >= 0) _steer[ignore] = savedTarget;
            _steer[0] = savedPlayer;
            return steer;
        }

        // Twice a second per ship: how hard the wake of a passing island rocks it, and whether a fishing boat
        // has to haul in its nets and make way.
        void Disturbance(ref Ship s)
        {
            float bob = 0f;
            bool threat = false;
            for (int i = 0; i < _obstacles.count; i++)
            {
                Island isl = _obstacles.islands[i];
                if (isl == null || isl == s.host) continue;
                Vector2 v = i == 0 ? _playerVel : Vel(isl);
                float sp = v.magnitude;
                if (sp < 0.3f) continue;
                Vector3 c = _obstacles.circles[i];
                Vector2 to = s.pos - new Vector2(c.x, c.y);
                float dist = to.magnitude - c.z;
                if (dist > Mathf.Max(wakeBobRange, 20f)) continue;
                bob = Mathf.Max(bob, Mathf.Clamp01(sp / 4f) * Mathf.Clamp01(1f - dist / Mathf.Max(1f, wakeBobRange)));
                if (sp > 0.8f && dist < 20f && Vector2.Dot(v, to) > 0f) threat = true;
            }
            s.wakeBobTarget = bob;
            if (threat && s.state == ShipState.Fishing && !s.hauling)
            {
                s.hauling = true;
                s.timer = Mathf.Min(s.timer, 2.2f);
                s.target = null;
            }
        }

        static float HalfLength(ShipKind k) => k == ShipKind.TradingCog ? 2.4f : k == ShipKind.RowBoat ? 0.6f : 1f;

        // Twice a second: choose targets / destinations.
        void Plan(ref Ship s)
        {
            if (s.state != ShipState.Sailing) return;
            if (s.kind == ShipKind.FishingBoat && s.cooldown <= 0f && !s.sailsDown && fish != null)
            {
                float best = 80f * 80f;
                bool found = false;
                for (int i = 0; i < fish.SchoolSlots; i++)
                {
                    if (!fish.SchoolActive(i)) continue;
                    Vector2 p = fish.SchoolPosition(i);
                    float d = (p - s.pos).sqrMagnitude * (fish.IsBaitBall(i) ? 0.25f : 1f);
                    if (d >= best || _obstacles.Inside(p, 1f)) continue;
                    best = d;
                    s.dest = p;
                    found = true;
                }
                if (found)
                {
                    s.target = null;
                    if ((s.dest - s.pos).sqrMagnitude < 3.5f * 3.5f)
                    {
                        s.state = ShipState.Fishing;
                        s.timer = Mathf.Lerp(fishingSecondsMin, fishingSecondsMax, SeaMath.Rand(s.seed, 50 + (int)_clock));
                    }
                    return;
                }
            }
            if (s.kind == ShipKind.RowBoat)
            {
                if (s.target == null) s.target = NearestIsland(s.pos, 60f);
                return;
            }
            if (s.target == null)
            {
                s.target = PickIsland(s.pos, 230f, SeaMath.Hash(s.seed, (uint)_clock + 5u), null);
                s.hasDock = false;
            }
            if (s.target == null)
            {
                Vector2 home = _playerPos - s.pos;
                float leash = spawnRadius - fadeBand - 10f + _playerRadius;
                if (home.sqrMagnitude > leash * leash) s.course = Rotate(home.normalized, 0.9f);
                s.dest = s.pos + s.course * 30f;
                return;
            }
            if (s.target.TryGetComponent<IDockProvider>(out var dock) && dock.TryGetDockWorld(out Vector3 dp, out Vector3 sea))
            {
                s.hasDock = true;
                s.dockDir = new Vector2(sea.x, sea.z).normalized;
                s.dest = new Vector2(dp.x, dp.z) + s.dockDir * 1.3f;
            }
            else
            {
                s.hasDock = false;
                s.dest = s.target.PlanarPosition;
            }
        }

        void StepSailing(ref Ship s, out Vector2 want, out float speed, out int ignore)
        {
            ignore = -1;
            float baseSpeed = s.kind == ShipKind.FishingBoat ? fishingSpeed : s.kind == ShipKind.RowBoat ? rowSpeed : sailSpeed;
            if (s.kind != ShipKind.RowBoat)
                baseSpeed *= 0.8f + 0.25f * Mathf.Clamp(Vector2.Dot(_wind, s.dir), -1f, 1f);
            speed = baseSpeed;

            if (s.kind == ShipKind.RowBoat)
            {
                if (s.target == null) { want = Rotate(s.dir, Mathf.Sin(_clock * 0.2f + s.seed % 7u) * 0.3f); return; }
                Vector2 c = s.target.PlanarPosition;
                Vector2 r = s.pos - c;
                float rl = Mathf.Max(0.5f, r.magnitude);
                r /= rl;
                bool shallow = _obstacles.Shallow(s.pos - r * 2.2f, anchorDepth, out _);
                float radial = shallow ? 0.7f : rl > s.target.BoundingRadius + 3f ? -1f : -0.45f;
                Vector2 tang = new Vector2(-r.y, r.x) * ((s.seed & 1u) == 0 ? 1f : -1f);
                want = (tang + r * radial).normalized;
                ignore = ObstacleIndex(s.target);
                if ((s.lantern || s.sailsDown) && shallow)
                {
                    Moor(ref s, s.target, 10f);
                    speed = 0f;
                }
                return;
            }

            Vector2 to = s.dest - s.pos;
            float d = to.magnitude;
            want = d > 1e-3f ? to / d : s.dir;
            if (s.target == null) return;
            float reach = (s.target.PlanarPosition - s.pos).magnitude - s.target.BoundingRadius;
            if (reach < 14f && s.cooldown <= 0f) ignore = ObstacleIndex(s.target);
            if (s.cooldown > 0f) return;
            bool arrived = s.hasDock
                ? d < 0.8f
                : reach < 0f && (_obstacles.Shallow(s.pos + s.dir * 2.2f, anchorDepth, out _) || d < s.target.BoundingRadius * 0.35f);
            if (d < 6f || reach < 2f) speed = Mathf.Min(speed, 0.35f + d * 0.12f);
            if (arrived)
            {
                Moor(ref s, s.target, Mathf.Lerp(mooredSecondsMin, mooredSecondsMax, SeaMath.Rand(s.seed, 70 + (int)_clock)));
                speed = 0f;
            }
        }

        // ---- mesh ----

        static readonly Color[] HullColors =
        {
            new Color(0.93f, 0.9f, 0.82f), new Color(0.72f, 0.3f, 0.24f), new Color(0.25f, 0.5f, 0.58f),
            new Color(0.24f, 0.32f, 0.5f), new Color(0.85f, 0.68f, 0.3f), new Color(0.35f, 0.55f, 0.35f)
        };

        static readonly Color[] SailColors =
        {
            new Color(0.98f, 0.96f, 0.9f), new Color(0.95f, 0.85f, 0.7f), new Color(0.9f, 0.45f, 0.35f), new Color(0.98f, 0.96f, 0.9f)
        };

        void Rebuild()
        {
            EnsureObject();
            _ride.Clear();
            _ride.limit = Mathf.Min(_ride.verts.Length, maxRideVerts);
            _batch.Clear();
            for (int i = 0; i < _ships.Length; i++)
                if (_ships[i].active && _ships[i].fade > 0.01f && Rides(ref _ships[i])) DrawShip(i, true);
            _batch.limit = maxVerts - _ride.vc;
            for (int i = 0; i < _ships.Length; i++)
                if (_ships[i].active && _ships[i].fade > 0.01f && !Rides(ref _ships[i])) DrawShip(i, false);
            DrawFlotsam();
            float ext = (recycleRadius + _playerRadius) * 2f + 20f;
            _batch.Apply(_mesh, new Bounds(new Vector3(_playerPos.x, 0f, _playerPos.y), new Vector3(ext, 16f, ext)));
            float rext = _playerRadius * 2f + 30f;
            _ride.Apply(_rideMesh, new Bounds(Vector3.zero, new Vector3(rext, 16f, rext)));
            SyncRide();
        }

        void DrawShip(int idx, bool ride)
        {
            ref Ship s = ref _ships[idx];
            SeaBatch b = ride ? _ride : _batch;
            float k = s.fade;
            bool detail = LifeLod.Distance(new Vector3(s.pos.x, 0f, s.pos.y)) < detailDistance;
            float half = HalfLength(s.kind);
            float amp = (s.kind == ShipKind.TradingCog ? 0.05f : 0.07f) * (1f + 2.2f * s.storm + 1.8f * s.wakeBob);
            float chop = s.wakeBob * 0.6f;
            float hMid = SeaMath.Swell(s.pos, _clock) + chop * Mathf.Sin(_clock * 3.1f + idx * 1.7f);
            float hBow = SeaMath.Swell(s.pos + s.dir * half, _clock) + chop * Mathf.Sin(_clock * 3.1f + idx * 1.7f + 1.1f);
            Vector2 right2 = new Vector2(s.dir.y, -s.dir.x);
            float hSide = SeaMath.Swell(s.pos + right2 * (half * 0.5f), _clock) + chop * Mathf.Sin(_clock * 2.7f + idx * 0.9f);
            float pitch = (hBow - hMid) * amp / half * 1.6f;
            float roll = (hMid - hSide) * amp / (half * 0.5f) * 1.2f + s.heel;
            float y = hMid * amp;
            bool aground = s.state == ShipState.Beached && HostAlive(s.host);
            float ground = 0f;
            float react = 0f;
            if (aground || ride)
            {
                float hostY = s.host.transform.position.y;
                if (aground)
                {
                    // The hull tips over and stops heaving as the beach lifts it out of the water.
                    float bed = s.host.SampleHeight(s.local) + hostY + KeelClear(s.kind) * k;
                    ground = Mathf.Clamp01((bed - y + 0.05f) / 0.15f);
                    ground = ground * ground * (3f - 2f * ground);
                    y = Mathf.Max(y, bed);
                    pitch = Mathf.Lerp(pitch, s.restPitch, ground);
                    roll = Mathf.Lerp(roll, s.restRoll, ground);
                    react = Mathf.Clamp01(s.react / 3f);
                }
                if (ride) y -= hostY;
            }
            Vector2 basisDir = ride ? s.localDir : s.dir;
            if (basisDir.sqrMagnitude < 1e-6f) basisDir = Vector2.up;
            SeaBatch.Basis(basisDir, pitch, roll, out Vector3 r, out Vector3 u, out Vector3 f);
            Vector3 p = ride ? new Vector3(s.local.x, y, s.local.y) : new Vector3(s.pos.x, y, s.pos.y);
            Vector3 scale = new Vector3(k, k, k);
            Color hull = HullColors[(int)(s.seed % (uint)HullColors.Length)];
            Color sailCol = SailColors[(int)((s.seed >> 8) % (uint)SailColors.Length)];
            float flick = 0.92f + 0.08f * Mathf.Sin(_clock * 11f + idx * 2.3f);
            float lampGain = flick * (1f + 2.2f * _night);
            bool lit = s.lantern || react > 0f;
            Color lamp = lit ? new Color(lanternColor.r * lampGain * (1f + 0.6f * _night), lanternColor.g * lampGain, lanternColor.b * lampGain * (1f - 0.55f * _night), 1f) : new Color(0.3f, 0.27f, 0.2f, 1f);
            // Crew reaction of a stranded boat: the lantern is swung about and a signal flag waved.
            Vector3 swing = r * (Mathf.Sin(_clock * 5.2f + idx) * 0.16f * react * k) + u * (Mathf.Abs(Mathf.Cos(_clock * 5.2f + idx)) * 0.08f * react * k);
            float sailH = Mathf.Lerp(0.1f, 1f, s.sail);
            float wr = Mathf.Clamp(Vector2.Dot(_wind.sqrMagnitude > 1e-6f ? _wind.normalized : Vector2.right, right2), -1f, 1f);
            if (aground) wr = Mathf.Lerp(wr, Mathf.Sin(_clock * 1.3f + idx) * 0.35f, ground);
            float lee = wr >= 0f ? 1f : -1f;
            Vector3 flagAt = p;

            switch (s.kind)
            {
                case ShipKind.SailBoat:
                {
                    b.Add(_tSailHull, p, r, u, f, scale, hull);
                    b.Add(_tSailRig, p, r, u, f, scale, Color.white);
                    float a = wr * 0.85f;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    Vector3 sf = f * ca - r * sa, srt = r * ca + f * sa;
                    Vector3 boom = p + (u * 0.78f + f * 0.2f) * k;
                    b.Add(_tMainSail, boom, srt * lee, u, sf, new Vector3(k, k * sailH, k), sailCol);
                    if (detail && s.sail > 0.15f && !aground) b.Add(_tJib, p + u * (0.5f * k), r * lee, u, f, new Vector3(k, k * s.sail, k), sailCol);
                    b.Add(_tLantern, p + (u * 0.55f - f * 0.98f) * k + swing, r, u, f, scale, lamp);
                    flagAt = p + (u * 0.3f - f * 0.6f) * k;
                    break;
                }
                case ShipKind.FishingBoat:
                {
                    b.Add(_tFishHull, p, r, u, f, scale, hull);
                    b.Add(_tFishRig, p, r, u, f, scale, Color.white);
                    float a = wr * 0.5f;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    Vector3 sf = f * ca - r * sa, srt = r * ca + f * sa;
                    b.Add(_tFishSail, p + (u * 1.43f + f * 0.0f) * k, srt * lee, u, sf, new Vector3(k, k * sailH, k), new Color(0.72f, 0.42f, 0.28f));
                    b.Add(_tLantern, p + (u * 1.1f - f * 0.9f) * k + swing, r, u, f, scale, lamp);
                    flagAt = p + (u * 0.83f - f * 0.45f) * k;
                    if (detail && s.net > 0.05f && !ride)
                    {
                        // Net floats trailing in an arc off the stern; hauled in before the boat makes way.
                        Color cork = new Color(0.95f, 0.55f, 0.2f);
                        for (int n = 0; n < 4; n++)
                        {
                            float ang = (n - 1.5f) * 0.42f;
                            float reach = (1.3f + 1.5f * s.net) * k;
                            Vector2 fp = s.pos - s.dir * (Mathf.Cos(ang) * reach) + right2 * (Mathf.Sin(ang) * reach * 1.2f);
                            b.Octa(new Vector3(fp.x, 0.03f + 0.05f * amp * SeaMath.Swell(fp, _clock), fp.y), 0.09f * k * Mathf.Clamp01(s.net * 3f), cork);
                        }
                    }
                    break;
                }
                case ShipKind.TradingCog:
                {
                    b.Add(_tCogHull, p, r, u, f, scale, new Color(0.5f, 0.34f, 0.2f));
                    b.Add(_tCogRig, p, r, u, f, scale, Color.white);
                    float a = wr * 0.35f;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    Vector3 sf = f * ca - r * sa, srt = r * ca + f * sa;
                    b.Add(_tCogSail, p + (u * 4.2f + f * 0.17f) * k, srt, u, sf, new Vector3(k, k * sailH, k * Mathf.Lerp(0.2f, 1f, s.sail)), Color.white);
                    b.Add(_tLantern, p + (u * 1.6f - f * 2.5f) * k + swing, r, u, f, scale * 1.5f, lamp);
                    b.Add(_tLantern, p + (u * 1.5f + f * 2.35f) * k, r, u, f, scale * 1.2f, lamp);
                    flagAt = p + (u * 1.4f - f * 1.7f + r * 0.4f) * k;
                    break;
                }
                default:
                {
                    b.Add(_tRowHull, p, r, u, f, scale, hull);
                    b.Add(_tRower, p, r, u, f, scale, Color.white);
                    if (detail)
                    {
                        bool rowing = s.speed > 0.1f;
                        float ph = _clock * 2.4f + idx;
                        float sweep = rowing ? Mathf.Sin(ph) * 0.55f : -0.9f;
                        float dip = rowing ? Mathf.Max(0f, Mathf.Cos(ph)) * 0.22f - 0.2f : 0.1f;
                        float cs = Mathf.Cos(sweep), sn = Mathf.Sin(sweep);
                        float cd = Mathf.Cos(dip), sd = Mathf.Sin(dip);
                        Vector3 pivot = p + (u * 0.2f - f * 0.05f) * k;
                        Vector3 or = (r * cs + f * sn) * cd + u * sd;
                        b.Add(_tOar, pivot + r * (0.22f * k), or, u, f * cs - r * sn, scale, Color.white);
                        Vector3 ol = (-r * cs + f * sn) * cd + u * sd;
                        b.Add(_tOar, pivot - r * (0.22f * k), ol, u, f * cs + r * sn, scale, Color.white);
                    }
                    b.Add(_tLantern, p + (u * 0.36f + f * 0.62f) * k + swing, r, u, f, scale * 0.8f, lamp);
                    flagAt = p + (u * 0.6f - f * 0.2f + r * 0.12f) * k;
                    break;
                }
            }

            if (aground && detail && FlagWaving(ref s))
            {
                float wave = Mathf.Sin(_clock * 6.5f + idx * 1.3f);
                float fa = wave * 0.9f, lean = 0.35f * Mathf.Cos(_clock * 6.5f + idx * 1.3f);
                float cfa = Mathf.Cos(fa), sfa = Mathf.Sin(fa);
                Vector3 fr = r * cfa + f * sfa, ff = f * cfa - r * sfa;
                Vector3 fu = (Vector3.up + fr * lean).normalized;
                float fs = (s.kind == ShipKind.TradingCog ? 1.5f : s.kind == ShipKind.RowBoat ? 0.8f : 1f) * k;
                b.Add(_tFlag, flagAt, fr, fu, ff, new Vector3(fs, fs, fs), Color.white);
            }

            if (!detail || aground) return;
            Color foam = new Color(1.05f, 1.08f, 1.1f, 1f);
            float wscale = s.kind == ShipKind.TradingCog ? 2.2f : s.kind == ShipKind.RowBoat ? 0.55f : 1f;
            Vector2 prevPos = s.pos - s.dir * (half * 0.85f), prevSide = right2;
            float prevT = 0f;
            for (int w = 0; w < WakePoints; w++)
            {
                int wi = idx * WakePoints + (s.wakeHead - 1 - w + WakePoints * 2) % WakePoints;
                float age = _wakeAge[wi];
                if (age >= wakeLife) break;
                float t = age / wakeLife;
                Vector2 curPos = _wakePos[wi], curSide = _wakeSide[wi];
                if ((curPos - prevPos).sqrMagnitude > 0.01f)
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        float s0 = (0.2f + 0.6f * prevT) * wscale * sgn, s1 = (0.2f + 0.6f * t) * wscale * sgn;
                        float w0 = 0.17f * (1f - prevT) * wscale * k * sgn, w1 = 0.17f * (1f - t) * wscale * k * sgn;
                        Vector2 a0 = prevPos + prevSide * s0, a1 = prevPos + prevSide * (s0 + w0);
                        Vector2 b0 = curPos + curSide * s1, b1 = curPos + curSide * (s1 + w1);
                        _batch.Quad(new Vector3(a0.x, 0.035f, a0.y), new Vector3(a1.x, 0.035f, a1.y),
                            new Vector3(b1.x, 0.035f, b1.y), new Vector3(b0.x, 0.035f, b0.y), foam, Vector3.up);
                    }
                prevPos = curPos; prevSide = curSide; prevT = t;
            }
        }
    }
}
