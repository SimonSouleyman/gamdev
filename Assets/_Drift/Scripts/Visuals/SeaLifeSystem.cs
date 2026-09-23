using Drift.Core;
using Drift.Islands;
using UnityEngine;
using UnityEngine.Rendering;

namespace Drift.Visuals
{
    // Larger sea life around the player: dolphin pods, whales (pods of 2-4 with calves, solitary bulls that
    // breach), sea turtles, jellyfish swarms, rays, resting
    // gulls, seaweed, flying-fish bursts and gulls circling over FishSystem's bait ball. Groups are seeded from
    // a wrapped world grid like the fish (same cell => same animals on every copy of the torus), live in a
    // fixed pool, steer around Island.All and scale in/out at the range edge. Two dynamic meshes: "SeaLifeUnder"
    // (Drift/Fish: drawn after the opaque water with the underwater tint) and "SeaLifeAbove" (Drift/VertexColor:
    // everything that breaks the surface). The water material is opaque, so nothing submerged may live in the
    // opaque mesh — it would only show up as a turquoise "shallow" patch through the depth texture.
    // SeaLifeSystem.Show.cs adds what plays near the camera: seals at the player's coast, surfacing turtles, dolphin
    // passes across the view, TryShowNear and the cozy pacing.
    [ExecuteAlways]
    [DefaultExecutionOrder(212)]
    public partial class SeaLifeSystem : MonoBehaviour
    {
        public enum Kind { Dolphins, Whale, Turtle, Jellies, Ray, Gulls, Seaweed, WhalePod }

        public WaterFeedback water;
        public FishSystem fish;
        public ShipSystem ships;
        public int seed = 23;
        public float cellSize = 30f;
        public float worldPeriod = 660f;
        public float spawnRadius = 90f;
        public float fadeBand = 15f;
        public float recycleRadius = 108f;
        [Range(0f, 1f)] public float density = 0.62f;
        public int maxGroups = 14;
        public int maxDolphinPods = 2;
        public int maxWhales = 2;
        public int maxWhalePods = 2;
        public int maxTurtles = 4;
        public int maxJellySwarms = 3;
        public int maxRays = 4;
        public int maxGullRafts = 2;
        public int maxSeaweed = 4;
        public float respawnSeconds = 90f;
        [Range(0f, 1f)] public float respawnMinRange = 0.5f;
        public float rebuildRate = 15f;
        public float detailDistance = 70f;
        public float splashDistance = 75f;
        public int maxUnderVerts = 2000;
        public int maxAboveVerts = 2000;

        public float dolphinLength = 1.5f;
        public float dolphinCruise = 2.2f;
        public float dolphinSprint = 12f;
        public float dolphinJumpHeight = 1.05f;
        public float dolphinJumpDuration = 1.05f;
        public float bowRideRange = 40f;
        public float bowRideSeconds = 20f;
        public float whaleLength = 6.5f;
        public float whaleSpeed = 1.1f;
        public float whaleKeepAway = 10f;
        public float whaleBullScale = 1.25f;
        public float whaleCalfScale = 0.5f;
        public float whaleHomeRange = 55f;
        public float whaleDeepSecondsMin = 22f;
        public float whaleDeepSecondsMax = 40f;
        public float breachIntervalMin = 80f;
        public float breachIntervalMax = 180f;
        public float turtleLength = 0.8f;
        public float turtleSpeed = 0.9f;
        public float jellySize = 0.3f;
        public float raySpan = 1.7f;
        public float raySpeed = 1.6f;
        public float flyingFishIntervalMin = 10f;
        public float flyingFishIntervalMax = 22f;
        public Color dolphinColor = new Color(0.31f, 0.46f, 0.64f);
        public Color whaleColor = new Color(0.2f, 0.27f, 0.36f);
        public Color seaweedColor = new Color(0.16f, 0.42f, 0.2f);

        [Header("Walwanderung (Schauspiel)")]
        [Tooltip("Tempo (u/s) der Wale, solange sie in der Reihe wandern; unterwegs sind sie zügiger als beim Bummeln.")]
        [Range(0.6f, 4f)] public float whaleMigrationSpeed = 1.8f;
        [Tooltip("Kürzeste und längste Tauchzeit (s) einer wandernden Gruppe: kurz genug, dass die Reihe immer irgendwo bläst.")]
        public Vector2 whaleMigrationDive = new Vector2(7f, 15f);

        [Header("Abenteuer-Beute")]
        [Tooltip("So viele Meerestiere dürfen gleichzeitig neben der Insel herschwimmen (der Fischschwarm zählt extra).")]
        [Range(0, 4)] public int maxCompanions = 2;
        [Tooltip("So viele eingesammelte Tiere warten höchstens gleichzeitig auf der Bahn.")]
        [Range(0, 4)] public int maxPickups = 2;
        [Tooltip("So lange (s) begleitet ein eingesammeltes Tier die Insel.")]
        [Range(5f, 60f)] public float companionSeconds = 22f;
        [Tooltip("Abstand (u) zwischen Inselrand und Begleiter.")]
        [Range(1f, 12f)] public float companionGap = 3.5f;
        [Tooltip("Wie weit der Begleiter vorausschwimmt (Anteil des Seitenabstands): 0 = genau daneben, 1 = weit vorn.")]
        [Range(0f, 1.5f)] public float companionLead = 0.55f;
        [Tooltip("Höchsttempo (u/s), mit dem ein Begleiter aufschließt.")]
        [Range(4f, 30f)] public float companionCatchUp = 16f;
        [Tooltip("Zusätzliche Reichweite (u) um den Inselrand, in der ein wartendes Tier eingesammelt wird.")]
        [Range(0f, 8f)] public float pickupGrab = 2.5f;
        [Tooltip("Tempo (u/s), mit dem ein wartendes Tier auf der Bahn treibt.")]
        [Range(0f, 3f)] public float pickupDrift = 0.5f;

        [System.NonSerialized] public Vector2 debugPlayerVelocity;
        [System.NonSerialized] public float debugNight = -1f;

        const string UnderName = "SeaLifeUnder";
        const string AboveName = "SeaLifeAbove";
        const int MaxPuffs = 64;
        const int MaxDolphins = 6;

        struct Group
        {
            public bool active;
            public long key;
            public uint seed;
            public Kind kind;
            public Vector2 pos, dir;
            public float speed, fade;
            public int state, count;
            public float timer, cooldown, phase, jump, wander;
            public Island target;
            // Whales: flags bit 0 = the pod has a calf, bit 1 = this dive is synchronized, bit 5 (MigrateFlag) =
            // part of a migrating line, and then jump holds its fixed heading as an angle; breach = seconds until
            // a bull may leap again; depth 0..1 = dived deep to let something pass overhead.
            public int flags;
            public float breach, depth;
            // Adventure: seconds left as a companion swimming alongside the player (CompanionFlag).
            public float companion;
            // Dolphins passing across the view (state 4): where they are heading.
            public Vector2 goal;
        }

        struct Puff
        {
            public Vector3 pos, vel;
            public float age, life, size, gravity;
            public Color color;
        }

        Group[] _groups;
        readonly Puff[] _puffs = new Puff[MaxPuffs];
        readonly long[] _spent = new long[32];
        readonly float[] _spentAt = new float[32];
        int _spentCount;
        bool _scanned;
        readonly SeaObstacles _obstacles = new();
        readonly Vector3[] _whaleCircles = new Vector3[SeaObstacles.Max + 12];
        int _whaleCircleCount;
        SeaBatch _under, _above;
        Mesh _underMesh, _aboveMesh;
        GameObject _underGo, _aboveGo;
        Material _underMat, _aboveMat;
        float _clock, _rebuildTimer, _scanTimer, _obstacleTimer;
        int _editTick;
        bool _dirty;
        Island _player;
        Vector2 _playerPos, _playerVel;
        float _playerRadius, _playerSpeed, _night, _storm, _period;
        Vector2 _wind;

        bool _ffActive;
        Vector2 _ffStart, _ffDir;
        float _ffT, _ffTimer = 6f, _ffLen = 5.5f, _ffDur = 1.3f;
        int _ffCount, _ffHops = 1;
        uint _ffSeed = 1;

        Vector2 _gullCenter;
        float _gullFade, _gullDiveT = -1f, _gullDiveTimer = 5f;
        int _gullDiver;
        bool _gullHasBall;

        static SeaTemplate _tDolphin, _tDolphinLow, _tWhale, _tWhaleLow, _tWhaleBreach, _tWhaleHump, _tWhaleFluke, _tTurtle, _tFlipper, _tJelly, _tJellyLow, _tGull, _tGullWing;

        public float Clock => _clock;
        public int VertexCount => (_under != null ? _under.vc : 0) + (_above != null ? _above.vc : 0);
        public int UnderVertexCount => _under != null ? _under.vc : 0;
        public int AboveVertexCount => _above != null ? _above.vc : 0;
        public int GroupSlots => _groups != null ? _groups.Length : 0;
        public bool GroupActive(int i) => _groups[i].active;
        public Kind GroupKind(int i) => _groups[i].kind;
        public Vector2 GroupPosition(int i) => _groups[i].pos;
        public int GroupState(int i) => _groups[i].state;
        public int GroupMembers(int i) => _groups[i].active ? _groups[i].count : 0;
        public float GroupFade(int i) => _groups[i].fade;
        public int ActiveGroups => CountGroups(-1);
        public int DolphinCount => CountMembers(Kind.Dolphins);
        // Every whale in range: solitary bulls plus pod members (calves included).
        public int WhaleCount => CountMembers(Kind.Whale) + CountMembers(Kind.WhalePod);
        public int WhaleLonerCount => CountGroups((int)Kind.Whale);
        public int WhalePodCount => CountGroups((int)Kind.WhalePod);
        public int WhalePodMembers => CountMembers(Kind.WhalePod);
        public int WhaleSurfacings { get; private set; }
        public int WhaleBreaches { get; private set; }

        public int WhaleCalfCount
        {
            get
            {
                int n = 0;
                if (_groups != null)
                    for (int i = 0; i < _groups.Length; i++)
                        if (_groups[i].active && _groups[i].kind == Kind.WhalePod && (_groups[i].flags & 1) != 0) n++;
                return n;
            }
        }

        public bool WhaleBreaching
        {
            get
            {
                if (_groups == null) return false;
                for (int i = 0; i < _groups.Length; i++)
                    if (_groups[i].active && _groups[i].kind == Kind.Whale && _groups[i].state == 3) return true;
                return false;
            }
        }

        public bool GroupHasCalf(int i) => _groups[i].active && _groups[i].kind == Kind.WhalePod && (_groups[i].flags & 1) != 0;

        // Journal / tap name of one animal of a group (a pod's calf, a solitary bull).
        public SeaKind MemberSeaKind(int group, int member)
        {
            ref Group g = ref _groups[group];
            if (g.kind == Kind.Whale) return SeaKind.WhaleBull;
            if (g.kind == Kind.WhalePod && IsCalf(ref g, member)) return SeaKind.WhaleCalf;
            return SeaKindOf(g.kind);
        }
        public int TurtleCount => CountGroups((int)Kind.Turtle);
        public int JellyfishCount => CountMembers(Kind.Jellies);
        public int RayCount => CountMembers(Kind.Ray);
        public int RestingGullCount => CountMembers(Kind.Gulls);
        public bool FlyingFishActive => _ffActive;
        public bool GullsOverBaitBall => _gullFade > 0.05f;
        public Vector2 BaitBallGullCenter => _gullCenter;

        public bool WhaleVisible
        {
            get
            {
                if (_groups == null) return false;
                for (int i = 0; i < _groups.Length; i++)
                    if (_groups[i].active && IsWhale(_groups[i].kind) && _groups[i].state != 0 && _groups[i].fade > 0.3f) return true;
                return false;
            }
        }

        public bool DolphinsBowRiding
        {
            get
            {
                if (_groups == null) return false;
                for (int i = 0; i < _groups.Length; i++)
                    if (_groups[i].active && _groups[i].kind == Kind.Dolphins && _groups[i].state == 1) return true;
                return false;
            }
        }

        public static SeaKind SeaKindOf(Kind k)
        {
            switch (k)
            {
                case Kind.Dolphins: return SeaKind.Dolphin;
                case Kind.Whale: return SeaKind.Whale;
                case Kind.WhalePod: return SeaKind.Whale;
                case Kind.Turtle: return SeaKind.SeaTurtle;
                case Kind.Jellies: return SeaKind.Jellyfish;
                case Kind.Ray: return SeaKind.Ray;
                case Kind.Gulls: return SeaKind.RestingGull;
                default: return SeaKind.Seaweed;
            }
        }

        // World positions of every individual of a kind (journal / tap integration). Returns the number written.
        public int PositionsOf(Kind kind, Vector3[] buffer)
        {
            int n = 0;
            if (_groups == null || buffer == null) return 0;
            for (int i = 0; i < _groups.Length && n < buffer.Length; i++)
            {
                if (!_groups[i].active || _groups[i].kind != kind || _groups[i].fade < 0.3f) continue;
                ref Group g = ref _groups[i];
                for (int m = 0; m < g.count && n < buffer.Length; m++)
                {
                    Vector2 w = MemberPos(ref g, m, out float y);
                    buffer[n++] = new Vector3(w.x, y, w.y);
                }
            }
            return n;
        }

        int CountGroups(int kind)
        {
            int n = 0;
            if (_groups != null)
                for (int i = 0; i < _groups.Length; i++)
                    if (_groups[i].active && (kind < 0 || (int)_groups[i].kind == kind)) n++;
            return n;
        }

        int CountMembers(Kind kind)
        {
            int n = 0;
            if (_groups != null)
                for (int i = 0; i < _groups.Length; i++)
                    if (_groups[i].active && _groups[i].kind == kind) n += _groups[i].count;
            return n;
        }

        // ---- lifecycle ----

        void OnEnable()
        {
            EnsureArrays();
            Resolve();
            _dirty = true;
            Step(0f);
        }

        void OnDisable()
        {
            DestroyChildren();
            if (_underMesh != null) DestroyImmediate(_underMesh);
            if (_aboveMesh != null) DestroyImmediate(_aboveMesh);
            if (_underMat != null) DestroyImmediate(_underMat);
            if (_aboveMat != null) DestroyImmediate(_aboveMat);
            _underMesh = _aboveMesh = null;
            _underMat = _aboveMat = null;
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
            if (ships == null) ships = GetComponent<ShipSystem>();
            if (ships == null) ships = FindAnyObjectByType<ShipSystem>();
            var streamer = FindAnyObjectByType<WorldStreamer>();
            _period = streamer != null && streamer.WorldSize > 0f ? streamer.WorldSize : worldPeriod;
        }

        void EnsureArrays()
        {
            maxGroups = Mathf.Clamp(maxGroups, 1, 24);
            if (_groups == null || _groups.Length != maxGroups) _groups = new Group[maxGroups];
            maxUnderVerts = Mathf.Clamp(maxUnderVerts, 256, 4000);
            maxAboveVerts = Mathf.Clamp(maxAboveVerts, 256, 4000);
            if (_under == null || _under.verts.Length != maxUnderVerts) _under = new SeaBatch(maxUnderVerts, false);
            if (_above == null || _above.verts.Length != maxAboveVerts) _above = new SeaBatch(maxAboveVerts, true);
            if (_period <= 0f) _period = worldPeriod;
            if (_tDolphin == null) BuildTemplates();
        }

        void DestroyChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i);
                if (c.name == UnderName || c.name == AboveName) DestroyImmediate(c.gameObject);
            }
            _underGo = _aboveGo = null;
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

        void EnsureObjects()
        {
            if (_underGo == null) _underGo = EnsureChild(UnderName);
            if (_aboveGo == null) _aboveGo = EnsureChild(AboveName);
            if (_underMat == null)
            {
                var shader = Shader.Find("Drift/Fish");
                if (shader == null) Debug.LogError("SeaLifeSystem: shader 'Drift/Fish' not found.");
                _underMat = new Material(shader) { name = "SeaLifeUnder", hideFlags = HideFlags.HideAndDontSave };
            }
            if (_aboveMat == null)
            {
                var shader = Shader.Find("Drift/VertexColor");
                if (shader == null) Debug.LogError("SeaLifeSystem: shader 'Drift/VertexColor' not found.");
                _aboveMat = new Material(shader) { name = "SeaLifeAbove", hideFlags = HideFlags.HideAndDontSave };
                _aboveMat.SetFloat("_Cull", 0f);
            }
            if (_underMesh == null) { _underMesh = new Mesh { name = UnderName, hideFlags = HideFlags.DontSave }; _underMesh.MarkDynamic(); }
            if (_aboveMesh == null) { _aboveMesh = new Mesh { name = AboveName, hideFlags = HideFlags.DontSave }; _aboveMesh.MarkDynamic(); }
            _underGo.GetComponent<MeshRenderer>().sharedMaterial = _underMat;
            _underGo.GetComponent<MeshFilter>().sharedMesh = _underMesh;
            _aboveGo.GetComponent<MeshRenderer>().sharedMaterial = _aboveMat;
            _aboveGo.GetComponent<MeshFilter>().sharedMesh = _aboveMesh;
        }

        // ---- templates ----

        static void BuildTemplates()
        {
            if (_tDolphin != null) return;
            Color dTop = Color.white, dBelly = new Color(2.3f, 1.95f, 1.6f);
            var rings = new[]
            {
                new SeaShape.Ring(0.5f, 0f, 0f, -0.01f), new SeaShape.Ring(0.4f, 0.035f, 0.03f, -0.01f),
                new SeaShape.Ring(0.27f, 0.095f, 0.1f), new SeaShape.Ring(0.03f, 0.125f, 0.14f),
                new SeaShape.Ring(-0.27f, 0.07f, 0.085f), new SeaShape.Ring(-0.46f, 0.02f, 0.03f)
            };
            _tDolphin = DolphinShape(rings, 5, dTop, dBelly);
            var low = new[]
            {
                new SeaShape.Ring(0.5f, 0f, 0f), new SeaShape.Ring(0.27f, 0.095f, 0.1f),
                new SeaShape.Ring(0f, 0.125f, 0.13f), new SeaShape.Ring(-0.46f, 0.02f, 0.03f)
            };
            _tDolphinLow = DolphinShape(low, 4, dTop, dTop);

            var wr = new[]
            {
                new SeaShape.Ring(0.5f, 0f, 0f), new SeaShape.Ring(0.43f, 0.085f, 0.075f), new SeaShape.Ring(0.2f, 0.13f, 0.12f),
                new SeaShape.Ring(-0.05f, 0.12f, 0.11f), new SeaShape.Ring(-0.3f, 0.06f, 0.06f), new SeaShape.Ring(-0.46f, 0.018f, 0.022f)
            };
            var w = new SeaShape();
            w.Loft(wr, 6, 0f, Mathf.PI * 2f, (n, z) => Color.white);
            w.SheetTri(new Vector3(0f, 0f, -0.44f), new Vector3(-0.2f, 0f, -0.56f), new Vector3(-0.02f, 0f, -0.5f), Color.white, Vector3.up);
            w.SheetTri(new Vector3(0f, 0f, -0.44f), new Vector3(0.02f, 0f, -0.5f), new Vector3(0.2f, 0f, -0.56f), Color.white, Vector3.up);
            w.SheetTri(new Vector3(-0.11f, -0.03f, 0.22f), new Vector3(-0.27f, -0.05f, 0.05f), new Vector3(-0.1f, -0.03f, 0.1f), Color.white, Vector3.up);
            w.SheetTri(new Vector3(0.11f, -0.03f, 0.22f), new Vector3(0.1f, -0.03f, 0.1f), new Vector3(0.27f, -0.05f, 0.05f), Color.white, Vector3.up);
            _tWhale = w.Build();

            var wl = new SeaShape();
            var wlr = new[]
            {
                new SeaShape.Ring(0.5f, 0f, 0f), new SeaShape.Ring(0.3f, 0.12f, 0.11f),
                new SeaShape.Ring(-0.1f, 0.115f, 0.105f), new SeaShape.Ring(-0.46f, 0.018f, 0.022f)
            };
            wl.Loft(wlr, 4, 0f, Mathf.PI * 2f, (n, z) => Color.white);
            wl.SheetTri(new Vector3(0f, 0f, -0.44f), new Vector3(-0.2f, 0f, -0.56f), new Vector3(0.2f, 0f, -0.56f), Color.white, Vector3.up);
            _tWhaleLow = wl.Build();

            // Breaching bull, drawn opaque: pale pleated throat, long white-edged pectoral fins.
            var wb = new SeaShape();
            Color belly = new Color(4.2f, 3.2f, 2.5f), back = Color.white, flank = new Color(1.5f, 1.5f, 1.5f);
            wb.Loft(wr, 6, 0f, Mathf.PI * 2f, (n, z) => n.y < -0.45f ? (z > -0.1f ? belly : flank) : n.y < 0.1f && z > 0.15f ? flank : back);
            wb.SheetTri(new Vector3(0f, 0f, -0.44f), new Vector3(-0.22f, 0f, -0.57f), new Vector3(-0.02f, 0f, -0.5f), back, Vector3.up);
            wb.SheetTri(new Vector3(0f, 0f, -0.44f), new Vector3(0.02f, 0f, -0.5f), new Vector3(0.22f, 0f, -0.57f), back, Vector3.up);
            wb.SheetTri(new Vector3(-0.1f, -0.04f, 0.24f), new Vector3(-0.36f, -0.1f, 0.0f), new Vector3(-0.1f, -0.04f, 0.1f), belly, Vector3.up);
            wb.SheetTri(new Vector3(0.1f, -0.04f, 0.24f), new Vector3(0.1f, -0.04f, 0.1f), new Vector3(0.36f, -0.1f, 0.0f), belly, Vector3.up);
            wb.SheetTri(new Vector3(0f, 0.105f, -0.12f), new Vector3(0f, 0.15f, -0.2f), new Vector3(0f, 0.09f, -0.25f), back, Vector3.right);
            _tWhaleBreach = wb.Build();

            var hr = new[]
            {
                new SeaShape.Ring(0.3f, 0f, 0f), new SeaShape.Ring(0.2f, 0.075f, 0.035f), new SeaShape.Ring(0f, 0.105f, 0.06f),
                new SeaShape.Ring(-0.2f, 0.075f, 0.045f), new SeaShape.Ring(-0.34f, 0f, 0f)
            };
            var hump = new SeaShape();
            hump.Loft(hr, 5, 0f, Mathf.PI, (n, z) => n.y > 0.75f ? new Color(1.25f, 1.25f, 1.25f) : Color.white);
            hump.SheetTri(new Vector3(0f, 0.04f, -0.16f), new Vector3(0f, 0.085f, -0.24f), new Vector3(0f, 0.035f, -0.27f), new Color(0.85f, 0.85f, 0.85f), Vector3.right);
            _tWhaleHump = hump.Build();

            var fl = new SeaShape();
            fl.Prism(new Vector3(0f, -0.35f, 0f), 0.085f, 0.03f, 0.75f, 5, Color.white, Color.white, false);
            Color fc = new Color(0.9f, 0.9f, 0.9f), fd = new Color(0.75f, 0.75f, 0.75f);
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                Vector3 root = new Vector3(0f, 0.33f, 0f), notch = new Vector3(0f, 0.43f, 0f), tip = new Vector3(sgn * 0.44f, 0.57f, 0f);
                Vector3 lead = new Vector3(sgn * 0.2f, 0.36f, 0f);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 ridge = new Vector3(sgn * 0.17f, 0.43f, side * 0.06f);
                    fl.center = new Vector3(sgn * 0.17f, 0.43f, 0f);
                    fl.Tri(root, lead, ridge, side > 0 ? fc : fd);
                    fl.Tri(lead, tip, ridge, side > 0 ? fc : fd);
                    fl.Tri(tip, notch, ridge, side > 0 ? fd : fc);
                    fl.Tri(notch, root, ridge, side > 0 ? fd : fc);
                }
            }
            _tWhaleFluke = fl.Build();

            var tr = new[]
            {
                new SeaShape.Ring(0.32f, 0f, 0f), new SeaShape.Ring(0.2f, 0.2f, 0.07f), new SeaShape.Ring(-0.02f, 0.27f, 0.11f),
                new SeaShape.Ring(-0.26f, 0.19f, 0.07f), new SeaShape.Ring(-0.37f, 0f, 0f)
            };
            var tu = new SeaShape();
            Color shellTop = new Color(0.34f, 0.44f, 0.2f), shellSide = new Color(0.5f, 0.42f, 0.22f), skin = new Color(0.56f, 0.66f, 0.38f);
            tu.Loft(tr, 5, 0f, Mathf.PI, (n, z) => n.y > 0.6f ? shellTop : shellSide);
            tu.Sheet(new Vector3(-0.055f, 0.03f, 0.3f), new Vector3(-0.045f, 0.03f, 0.47f), new Vector3(0.045f, 0.03f, 0.47f), new Vector3(0.055f, 0.03f, 0.3f), skin, Vector3.up);
            tu.SheetTri(new Vector3(-0.03f, 0.02f, -0.35f), new Vector3(0f, 0.02f, -0.46f), new Vector3(0.03f, 0.02f, -0.35f), skin, Vector3.up);
            _tTurtle = tu.Build();
            var fp = new SeaShape();
            fp.Sheet(new Vector3(0f, 0f, 0.05f), new Vector3(0.2f, 0f, 0.1f), new Vector3(0.36f, 0f, -0.04f), new Vector3(0.03f, 0f, -0.05f), skin, Vector3.up);
            _tFlipper = fp.Build();

            _tJelly = JellyShape(true);
            _tJellyLow = JellyShape(false);

            var gr = new[]
            {
                new SeaShape.Ring(0.26f, 0f, 0f, 0.07f), new SeaShape.Ring(0.13f, 0.085f, 0.075f, 0.05f),
                new SeaShape.Ring(-0.12f, 0.095f, 0.08f, 0.04f), new SeaShape.Ring(-0.38f, 0f, 0f, 0.12f)
            };
            var gu = new SeaShape();
            Color white = new Color(0.97f, 0.97f, 0.95f), grey = new Color(0.66f, 0.7f, 0.76f);
            gu.Loft(gr, 4, 0f, Mathf.PI * 2f, (n, z) => n.y > 0.5f ? grey : white);
            gu.Box(new Vector3(0f, 0.17f, 0.2f), new Vector3(0.09f, 0.09f, 0.1f), white);
            gu.SheetTri(new Vector3(-0.02f, 0.17f, 0.25f), new Vector3(0f, 0.15f, 0.34f), new Vector3(0.02f, 0.17f, 0.25f), new Color(1f, 0.72f, 0.2f), Vector3.up);
            _tGull = gu.Build();
            var gw = new SeaShape();
            gw.Sheet(new Vector3(0.05f, 0.1f, 0.1f), new Vector3(0.4f, 0.1f, 0.05f), new Vector3(0.62f, 0.1f, -0.1f), new Vector3(0.05f, 0.1f, -0.1f), grey, Vector3.up);
            _tGullWing = gw.Build();
            BuildShowTemplates();
        }

        static SeaTemplate DolphinShape(SeaShape.Ring[] rings, int segs, Color top, Color belly)
        {
            var s = new SeaShape();
            s.Loft(rings, segs, 0f, Mathf.PI * 2f, (n, z) => n.y < -0.35f ? belly : top);
            s.SheetTri(new Vector3(0f, 0.12f, 0.06f), new Vector3(0f, 0.3f, -0.12f), new Vector3(0f, 0.1f, -0.16f), new Color(0.85f, 0.85f, 0.85f), Vector3.right);
            s.SheetTri(new Vector3(-0.09f, -0.06f, 0.2f), new Vector3(-0.27f, -0.13f, 0.06f), new Vector3(-0.09f, -0.06f, 0.1f), top, Vector3.up);
            s.SheetTri(new Vector3(0.09f, -0.06f, 0.2f), new Vector3(0.09f, -0.06f, 0.1f), new Vector3(0.27f, -0.13f, 0.06f), top, Vector3.up);
            s.SheetTri(new Vector3(0f, 0f, -0.44f), new Vector3(-0.17f, 0f, -0.57f), new Vector3(0f, 0f, -0.51f), top, Vector3.up);
            s.SheetTri(new Vector3(0f, 0f, -0.44f), new Vector3(0f, 0f, -0.51f), new Vector3(0.17f, 0f, -0.57f), top, Vector3.up);
            return s.Build();
        }

        // Shared-vertex bell (the Fish shader ignores normals); tentacles are listed first so the bell draws over them.
        static SeaTemplate JellyShape(bool tentacles)
        {
            const int S = 6;
            int tv = tentacles ? 12 : 0;
            var v = new Vector3[tv + 1 + S * 2];
            var c = new Color[v.Length];
            var t = new int[(tentacles ? 12 : 0) + S * 9];
            int ti = 0;
            if (tentacles)
                for (int k = 0; k < 4; k++)
                {
                    float a = k * Mathf.PI * 0.5f + 0.6f;
                    Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 side = new Vector3(-d.z, 0f, d.x) * 0.16f;
                    v[k * 3] = d * 0.45f - side; v[k * 3 + 1] = d * 0.45f + side; v[k * 3 + 2] = d * 0.75f + new Vector3(0f, -2.2f, 0f);
                    c[k * 3] = c[k * 3 + 1] = new Color(1f, 1f, 1f, 0.55f);
                    c[k * 3 + 2] = new Color(1f, 1f, 1f, 0.1f);
                    t[ti++] = k * 3; t[ti++] = k * 3 + 1; t[ti++] = k * 3 + 2;
                }
            int b = tv;
            v[b] = new Vector3(0f, 0.75f, 0f);
            c[b] = new Color(1.25f, 1.25f, 1.25f, 1f);
            for (int i = 0; i < S; i++)
            {
                float a = i * Mathf.PI * 2f / S;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[b + 1 + i] = d * 0.7f + Vector3.up * 0.45f;
                c[b + 1 + i] = new Color(1f, 1f, 1f, 0.9f);
                v[b + 1 + S + i] = d;
                c[b + 1 + S + i] = new Color(0.8f, 0.8f, 0.8f, 0.6f);
            }
            for (int i = 0; i < S; i++)
            {
                int j = (i + 1) % S;
                t[ti++] = b; t[ti++] = b + 1 + j; t[ti++] = b + 1 + i;
                t[ti++] = b + 1 + i; t[ti++] = b + 1 + j; t[ti++] = b + 1 + S + j;
                t[ti++] = b + 1 + i; t[ti++] = b + 1 + S + j; t[ti++] = b + 1 + S + i;
            }
            return new SeaTemplate { v = v, n = new Vector3[v.Length], c = c, t = t };
        }

        // ---- spawning / recycling ----

        bool IsOccupied(long key)
        {
            for (int i = 0; i < _groups.Length; i++) if (_groups[i].active && _groups[i].key == key) return true;
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

        public static Kind KindForRoll(float r)
        {
            if (r < 0.14f) return Kind.Dolphins;
            if (r < 0.2f) return Kind.Whale;
            if (r < 0.28f) return Kind.WhalePod;
            if (r < 0.4f) return Kind.Turtle;
            if (r < 0.52f) return Kind.Jellies;
            if (r < 0.64f) return Kind.Ray;
            if (r < 0.73f) return Kind.Gulls;
            return Kind.Seaweed;
        }

        public int CapOf(Kind k)
        {
            switch (k)
            {
                case Kind.Dolphins: return maxDolphinPods;
                case Kind.Whale: return Mathf.Min(maxWhales, SeaMath.MaxWhaleLonersInRange);
                case Kind.WhalePod: return Mathf.Min(maxWhalePods, SeaMath.MaxWhalePodsInRange);
                case Kind.Turtle: return maxTurtles;
                case Kind.Jellies: return maxJellySwarms;
                case Kind.Ray: return maxRays;
                case Kind.Gulls: return maxGullRafts;
                default: return maxSeaweed;
            }
        }

        // What a world cell holds, independent of the pool: same answer on every copy of the torus.
        public bool CellContent(int cx, int cy, out Kind kind, out Vector2 point, out uint h)
        {
            h = SeaMath.CellSeed(seed, cx, cy, SeaMath.WrapCells(_period > 0f ? _period : worldPeriod, cellSize));
            point = SeaMath.CellPoint(cx, cy, cellSize, h);
            kind = KindForRoll(SeaMath.Rand(h, 3));
            return SeaMath.Rand(h, 2) <= density;
        }

        void Scan()
        {
            float far = recycleRadius + _playerRadius;
            for (int i = 0; i < _groups.Length; i++)
            {
                if (!_groups[i].active) continue;
                // A migrating line is kept whole: its trailing groups are still on their way in, and they are
                // released to the usual recycling again the moment the spectacle ends.
                if ((_groups[i].flags & MigrateFlag) != 0) continue;
                if ((_groups[i].pos - _playerPos).sqrMagnitude > far * far)
                {
                    MarkSpent(_groups[i].key);
                    _groups[i].active = false;
                    _groups[i].target = null;
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
                    if (!CellContent(cx, cy, out Kind kind, out Vector2 sp, out uint h)) continue;
                    float d2 = (sp - _playerPos).sqrMagnitude;
                    if (d2 > near * near || (_scanned && d2 < near * near * respawnMinRange * respawnMinRange)) continue;
                    long key = SeaMath.Key(cx, cy);
                    if (IsOccupied(key) || IsSpent(key)) continue;
                    if (CountGroups((int)kind) >= CapOf(kind)) continue;
                    if (IsWhale(kind) && !SeaMath.WhaleSpawnAllowed(kind == Kind.WhalePod, WhalePodCount, WhaleLonerCount, maxWhalePods, maxWhales)) continue;
                    if (_obstacles.Inside(sp, kind == Kind.WhalePod ? SeaMath.WhaleClearance + 2.7f * whaleLength : kind == Kind.Whale ? SeaMath.WhaleClearance + whaleLength : 3f)) { MarkSpent(key); continue; }
                    int slot = -1;
                    for (int i = 0; i < _groups.Length; i++) if (!_groups[i].active) { slot = i; break; }
                    if (slot < 0) { _scanned = true; return; }
                    Activate(slot, key, sp, h, kind);
                }
            _scanned = true;
        }

        void Activate(int slot, long key, Vector2 sp, uint h, Kind kind)
        {
            ref Group g = ref _groups[slot];
            g = default;
            g.active = true;
            g.key = key;
            g.seed = h;
            g.kind = kind;
            g.pos = sp;
            float a = SeaMath.Rand(h, 4) * Mathf.PI * 2f;
            g.dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            g.phase = SeaMath.Rand(h, 5) * 20f;
            g.fade = 0f;
            g.jump = -1f;
            switch (kind)
            {
                case Kind.Dolphins:
                    g.count = 3 + (int)(SeaMath.Rand(h, 6) * 3.999f);
                    g.speed = dolphinCruise;
                    g.jump = -(2f + 5f * SeaMath.Rand(h, 7));
                    break;
                case Kind.Whale:
                    g.count = 1;
                    g.speed = whaleSpeed;
                    g.timer = 3f + 10f * SeaMath.Rand(h, 7);
                    g.breach = 20f + 35f * SeaMath.Rand(h, 9);
                    break;
                case Kind.WhalePod:
                    g.count = SeaMath.WhalePodSize(h, out bool calf);
                    g.flags = calf ? 1 : 0;
                    g.speed = whaleSpeed * 0.9f;
                    g.timer = 3f + 10f * SeaMath.Rand(h, 7);
                    break;
                case Kind.Turtle:
                    g.count = 1;
                    g.speed = turtleSpeed;
                    g.timer = 0f;
                    break;
                case Kind.Jellies:
                    g.count = 6 + (int)(SeaMath.Rand(h, 6) * 4.999f);
                    g.speed = 0.12f;
                    break;
                case Kind.Ray:
                    g.count = SeaMath.Rand(h, 6) < 0.4f ? 2 : 1;
                    g.speed = raySpeed;
                    g.jump = -Mathf.Lerp(rayLeapInterval.x, Mathf.Max(rayLeapInterval.x, rayLeapInterval.y), SeaMath.Rand(h, 7));
                    break;
                case Kind.Gulls:
                    g.count = 3 + (int)(SeaMath.Rand(h, 6) * 2.999f);
                    g.speed = 0.15f;
                    break;
                default:
                    g.count = 5 + (int)(SeaMath.Rand(h, 6) * 3.999f);
                    g.speed = 0f;
                    break;
            }
            _dirty = true;
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
                _playerVel = Application.isPlaying ? _player.PlanarVelocity : Vector2.zero;
            }
            else
            {
                _player = null;
                _playerRadius = 0f;
                _playerVel = Vector2.zero;
            }
            if (debugPlayerVelocity.sqrMagnitude > 0f) _playerVel = debugPlayerVelocity;
            _playerSpeed = _playerVel.magnitude;
            _night = debugNight >= 0f ? debugNight : LifeEnvironment.NightAmount;
            _storm = water != null ? water.Storm : LifeEnvironment.Storm;
            _wind = water != null ? water.Wind : LifeEnvironment.Wind;
            UpdateView();

            _obstacleTimer -= dt;
            if (_obstacleTimer <= 0f || dt <= 0f)
            {
                _obstacles.Refresh(_player, _playerPos, recycleRadius + _playerRadius + 30f);
                _obstacleTimer = 0.5f;
            }
            else _obstacles.Update();

            _scanTimer -= dt;
            if (_scanTimer <= 0f || dt <= 0f)
            {
                Scan();
                _scanTimer = 0.5f;
            }

            bool any = false;
            bool circles = false;
            for (int i = 0; i < _groups.Length; i++)
            {
                if (!_groups[i].active) continue;
                any = true;
                if (dt > 0f && !circles && IsWhale(_groups[i].kind)) { BuildWhaleCircles(); circles = true; }
                StepGroup(ref _groups[i], dt);
            }
            if (GameModes.IsAdventure && _player != null) CollectPickups();
            if (dt > 0f)
            {
                StepFlyingFish(dt);
                StepBaitGulls(dt);
                StepShow(dt);
                StepPuffs(dt);
            }
            else StepBaitGulls(0f);

            _rebuildTimer += dt;
            float interval = rebuildRate > 0f ? 1f / rebuildRate : 0f;
            if (_dirty || (_rebuildTimer >= interval && (any || _ffActive || _gullFade > 0f || SealCount > 0 || _under.vc + _above.vc > 0)))
            {
                _rebuildTimer = 0f;
                _dirty = false;
                Rebuild();
            }
        }

        Vector2 Steer(Vector2 pos, Vector2 desired, float lookAhead, float margin, bool ignorePlayer)
        {
            float saved = _obstacles.circles[0].z;
            if (ignorePlayer) _obstacles.circles[0].z = -1000f;
            Vector2 r = SeaMath.SteerAvoid(pos, desired, _obstacles.circles, _obstacles.count, lookAhead, margin);
            _obstacles.circles[0].z = saved;
            return r;
        }

        static Vector2 Rotate(Vector2 v, float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        static void Turn(ref Group g, Vector2 desired, float rate, float dt)
        {
            Vector2 nd = Vector2.Lerp(g.dir, desired, 1f - Mathf.Exp(-rate * dt));
            g.dir = nd.sqrMagnitude > 1e-6f ? nd.normalized : desired;
        }

        Vector2 Wander(ref Group g, float amount) =>
            Rotate(g.dir, (Mathf.Sin(_clock * 0.13f + g.phase) + Mathf.Sin(_clock * 0.31f + g.phase * 1.7f)) * amount);

        void StepGroup(ref Group g, float dt)
        {
            float edge = (g.pos - _playerPos).magnitude - _playerRadius;
            float target = SeaMath.EdgeFade(edge, spawnRadius - fadeBand, spawnRadius);
            if (g.kind == Kind.Gulls) target *= 1f - Mathf.Clamp01(_storm * 1.6f);
            g.fade = dt > 0f ? Mathf.MoveTowards(g.fade, target, dt * 0.9f) : target;
            if (dt <= 0f) return;
            if (g.cooldown > 0f) g.cooldown -= dt;
            // A pickup waiting on the track and a companion swimming alongside keep their own animation (a whale
            // still breathes) but never their own course: their position is driven, see StepEscort.
            bool escort = (g.flags & (PickupFlag | CompanionFlag)) != 0;

            switch (g.kind)
            {
                case Kind.Dolphins: StepDolphins(ref g, dt, edge); break;
                case Kind.Whale: StepWhale(ref g, dt); break;
                case Kind.WhalePod: StepWhale(ref g, dt); break;
                case Kind.Turtle: StepTurtle(ref g, dt); break;
                case Kind.Jellies:
                {
                    Vector2 drift = g.dir * g.speed + _wind * 0.08f;
                    Vector2 want = drift.sqrMagnitude > 1e-6f ? drift.normalized : g.dir;
                    Vector2 s = Steer(g.pos, want, 4f, 4.5f, false);
                    float sp = Vector2.Dot(s, want) < 0.7f ? 1.4f : drift.magnitude;
                    g.pos += s * (sp * dt);
                    if ((g.flags & SeenFlag) == 0 && g.fade > 0.8f && InView(g.pos, 0.04f))
                    {
                        g.flags |= SeenFlag;
                        if (Moments.Listening) Moments.Report(MomentKind.JellySwarm, new Vector3(g.pos.x, -0.3f, g.pos.y));
                    }
                    break;
                }
                case Kind.Ray:
                {
                    Vector2 want = Wander(ref g, 0.5f);
                    float speed = g.speed;
                    if (_playerSpeed > 0.4f && edge < 5f)
                    {
                        Vector2 away = g.pos - _playerPos;
                        if (away.sqrMagnitude > 1e-4f) want = away.normalized;
                        speed *= 2.5f;
                    }
                    Turn(ref g, Steer(g.pos, want, 10f, 3f, false), 1.2f, dt);
                    g.pos += g.dir * (speed * dt);
                    StepRayLeap(ref g, dt);
                    break;
                }
                case Kind.Gulls:
                {
                    Vector2 want = Wander(ref g, 0.8f);
                    float speed = g.speed;
                    int was = g.state;
                    g.state = 0;
                    if (edge < 6f)
                    {
                        Vector2 away = g.pos - _playerPos;
                        if (away.sqrMagnitude > 1e-4f) want = away.normalized;
                        speed = 2.2f + _playerSpeed;
                        g.state = 1;
                        if (was == 0 && g.cooldown <= 0f && g.fade > 0.5f)
                        {
                            g.cooldown = 8f;
                            if (Moments.Listening) Moments.Report(MomentKind.GullsTakeOff, new Vector3(g.pos.x, 0.3f, g.pos.y));
                        }
                    }
                    Turn(ref g, Steer(g.pos, want, 5f, 3f, false), g.state == 1 ? 3f : 0.6f, dt);
                    g.pos += g.dir * (speed * dt);
                    break;
                }
            }
            if (escort) StepEscort(ref g, dt);
        }

        void StepDolphins(ref Group g, float dt, float edge)
        {
            Vector2 want;
            float speed = dolphinCruise;
            Vector2 velDir = _playerSpeed > 1e-3f ? _playerVel / _playerSpeed : g.dir;
            switch (g.state)
            {
                case 1:
                {
                    g.timer -= dt;
                    Vector2 side = new Vector2(velDir.y, -velDir.x) * (Mathf.Sin(_clock * 0.4f + g.phase) * 2.5f);
                    Vector2 goal = _playerPos + velDir * (_playerRadius + 5f) + side;
                    if ((g.flags & EscortFlag) != 0)
                    {
                        // Escorting pods weave between the bow wave and the front flanks so they stay in view.
                        float weave = Mathf.Sin(_clock * 0.22f + g.phase);
                        Vector2 flank = new Vector2(velDir.y, -velDir.x) * (weave * (2.5f + 0.55f * _playerRadius));
                        goal = _playerPos + velDir * (_playerRadius * (1f - 0.35f * Mathf.Abs(weave)) + 4.5f) + flank;
                    }
                    Vector2 to = goal - g.pos;
                    float d = to.magnitude;
                    want = d > 1.5f ? (to / d + velDir * 0.4f).normalized : velDir;
                    speed = d > 1.5f ? Mathf.Min(dolphinSprint, _playerSpeed + 1.5f + d * 1.2f) : Mathf.Max(_playerSpeed, 1f);
                    if (_playerSpeed < 0.6f) g.wander += dt; else g.wander = 0f;
                    if (g.timer <= 0f || g.wander > 2.5f || _player == null)
                    {
                        g.state = 2;
                        g.timer = 5f;
                        g.cooldown = (g.flags & EscortFlag) != 0 ? 60f : 25f;
                        g.wander = 0f;
                        g.flags &= ~EscortFlag;
                    }
                    break;
                }
                case 2:
                {
                    g.timer -= dt;
                    Vector2 away = g.pos - _playerPos;
                    want = away.sqrMagnitude > 1e-4f ? (away.normalized + new Vector2(-velDir.y, velDir.x) * 0.6f).normalized : g.dir;
                    speed = dolphinCruise * 1.8f;
                    if (g.timer <= 0f) g.state = 0;
                    break;
                }
                case 4:
                {
                    // Passing across the view (TryShowNear): straight to the far side, leaping all the way.
                    g.timer -= dt;
                    Vector2 to = g.goal - g.pos;
                    float d = to.magnitude;
                    want = d > 1e-3f ? to / d : g.dir;
                    speed = dolphinCruise * 2.4f;
                    if (d < 2.5f || g.timer <= 0f)
                    {
                        g.state = 0;
                        g.cooldown = 30f;
                    }
                    break;
                }
                case 3:
                {
                    g.timer -= dt;
                    Vector2 r = g.pos - _playerPos;
                    float rl = Mathf.Max(0.5f, r.magnitude);
                    r /= rl;
                    float radial = Mathf.Clamp((_playerRadius + 5.5f - rl) * 0.5f, -1f, 1f);
                    want = (new Vector2(-r.y, r.x) + r * radial).normalized;
                    speed = dolphinCruise * 1.3f;
                    if (g.timer <= 0f || _player == null || _playerSpeed > 1.2f)
                    {
                        g.state = _playerSpeed > 1.2f && _player != null ? 1 : 0;
                        g.timer = bowRideSeconds;
                        g.cooldown = g.state == 0 ? (Cozy ? 18f : 30f) : 0f;
                    }
                    break;
                }
                default:
                {
                    want = Wander(ref g, 0.35f);
                    float ride = Cozy ? Mathf.Max(bowRideRange, cozyDolphinRange) : bowRideRange;
                    if (_player != null && g.cooldown <= 0f && edge < ride)
                    {
                        if (_playerSpeed > 1.2f)
                        {
                            g.state = 1;
                            g.timer = bowRideSeconds * (0.7f + 0.6f * SeaMath.Rand(g.seed, 40 + (int)_clock));
                        }
                        else if (edge < ride * 0.6f)
                        {
                            g.state = 3;
                            g.timer = 14f;
                        }
                    }
                    break;
                }
            }
            Turn(ref g, Steer(g.pos, want, 8f, 2f, false), g.state == 1 ? 3.5f : 1.6f, dt);
            g.speed = Mathf.MoveTowards(g.speed, speed, 6f * dt);
            g.pos += g.dir * (g.speed * dt);

            float series = dolphinJumpDuration + (g.count - 1) * 0.32f;
            if (g.jump >= 0f)
            {
                float before = g.jump;
                if (before == 0f && Moments.Listening) Moments.Report(MomentKind.DolphinJump, new Vector3(g.pos.x, 0f, g.pos.y));
                g.jump += dt;
                for (int m = 0; m < g.count; m++)
                {
                    float end = m * 0.32f + dolphinJumpDuration;
                    if (before < end && g.jump >= end)
                    {
                        Vector2 w = MemberPos(ref g, m, out _);
                        Splash(w, 0.75f, 5);
                    }
                }
                if (g.jump >= series)
                {
                    float nightMul = 1f + _night;
                    float jr = SeaMath.Rand(g.seed, 60 + (int)_clock);
                    float gap = g.state == 1 ? 2.2f + 2f * jr : g.state == 4 ? 1.1f + jr : Cozy && g.state == 3 ? 3.5f + 2.5f * jr : 6f + 8f * jr;
                    g.jump = -gap * nightMul;
                }
            }
            else
            {
                g.jump = Mathf.Min(g.jump + dt * (g.state == 0 ? Pace(g.pos) : 1f), 0f);
                if (g.jump >= 0f && _obstacles.Inside(g.pos + g.dir * (g.speed * series), 1f)) g.jump = -2f;
            }
        }

        // ---- whales: pods (Kind.WhalePod, 2-4, often with a calf) and solitary bulls (Kind.Whale) ----
        // state 0 deep, 1 breathing at the surface (members one after the other), 2 fluke dive, 3 breach (bulls).

        const float WhaleBreath = 4.5f;
        const float CalfBreath = 3.4f;
        const float WhaleDive = 3.2f;
        const float BreachSeconds = 2.8f;

        static bool IsWhale(Kind k) => k == Kind.Whale || k == Kind.WhalePod;

        static bool IsCalf(ref Group g, int m) => g.kind == Kind.WhalePod && SeaMath.IsWhaleCalf(m, g.count, (g.flags & 1) != 0);

        float WhaleScale(ref Group g, int m)
        {
            if (g.kind == Kind.Whale) return whaleBullScale;
            if (IsCalf(ref g, m)) return whaleCalfScale;
            return m == 0 ? 1f : 0.94f - 0.04f * m;
        }

        static float BreathLag(ref Group g, int m) => g.kind == Kind.WhalePod ? SeaMath.WhaleBreathLag(m, g.count, (g.flags & 1) != 0) : 0f;

        static float DiveLag(ref Group g, int m) => g.kind != Kind.WhalePod || (g.flags & 2) != 0 ? 0f : IsCalf(ref g, m) ? 0.5f : m * 0.9f;

        static float MaxLag(ref Group g, bool dive)
        {
            float lag = 0f;
            for (int m = 0; m < g.count; m++) lag = Mathf.Max(lag, dive ? DiveLag(ref g, m) : BreathLag(ref g, m));
            return lag;
        }

        // Islands (the player with its extra keep-away) and ships as circles; whales hold WhaleClearance from all.
        void BuildWhaleCircles()
        {
            int n = 0;
            for (int i = 0; i < _obstacles.count; i++)
            {
                Vector3 c = _obstacles.circles[i];
                if (i == 0 && c.z > 0f) c.z += Mathf.Max(0f, whaleKeepAway + 6f - SeaMath.WhaleClearance);
                _whaleCircles[n++] = c;
            }
            if (ships != null)
                for (int i = 0; i < ships.ShipSlots && n < _whaleCircles.Length; i++)
                {
                    if (!ships.ShipActive(i) || ships.ShipIsBeached(i)) continue;
                    Vector2 p = ships.ShipPosition(i);
                    _whaleCircles[n++] = new Vector3(p.x, p.y, ships.ShipKindOf(i) == ShipSystem.ShipKind.TradingCog ? 3f : 1.5f);
                }
            _whaleCircleCount = n;
        }

        // True when an island or a ship is clearly inside the clearance of any animal of the group.
        bool WhaleThreat(ref Group g)
        {
            float keep = SeaMath.WhaleClearance * 0.8f + whaleLength * 0.25f;
            for (int m = 0; m < g.count; m++)
            {
                Vector2 pos = WhaleMemberPos(ref g, m, out _);
                for (int i = 0; i < _whaleCircleCount; i++)
                {
                    Vector3 c = _whaleCircles[i];
                    if (c.z <= 0f) continue;
                    float dx = pos.x - c.x, dy = pos.y - c.y, r = c.z + keep;
                    if (dx * dx + dy * dy < r * r) return true;
                }
            }
            return false;
        }

        // How far the animals of a group reach out from its position (formation plus half a body).
        float WhaleReach(ref Group g)
        {
            if (g.kind != Kind.WhalePod) return whaleLength * whaleBullScale * 0.5f;
            float far = 0f;
            for (int m = 1; m < g.count; m++) far = Mathf.Max(far, SeaMath.WhalePodOffset(m, g.count, (g.flags & 1) != 0, g.seed).magnitude);
            return (far + 0.5f) * whaleLength + 0.5f;
        }

        void StepWhale(ref Group g, float dt)
        {
            bool pod = g.kind == Kind.WhalePod;
            bool migrate = (g.flags & MigrateFlag) != 0;
            float reach = WhaleReach(ref g);
            // An adventure pickup/companion stays up where you can see it: the island right beside it is not a
            // threat to dive from, it never breaches and the breathing cycle just keeps running.
            bool escort = (g.flags & (PickupFlag | CompanionFlag)) != 0;
            if (escort)
            {
                if (g.state != 1) { g.state = 1; g.wander = 0f; }
                if (g.wander > 2f * WhaleBreath) g.wander -= WhaleBreath;
                g.timer = Mathf.Max(g.timer, 2f);
            }
            // Something came closer than the whales allow (a fast island, a ship): dive deep and let it pass overhead.
            bool threat = !escort && WhaleThreat(ref g);
            g.depth = Mathf.MoveTowards(g.depth, threat ? 1f : 0f, dt * (threat ? 0.9f : 0.3f));

            float playerEdge = (_playerPos - g.pos).magnitude - _playerRadius;
            if (g.state != 3)
            {
                // A migrating line keeps its heading (stored in jump): it only bends round what is in the way and
                // comes back onto the course afterwards, and it never drifts home to the player.
                Vector2 want = migrate ? new Vector2(Mathf.Cos(g.jump), Mathf.Sin(g.jump)) : Wander(ref g, 0.2f);
                if (!migrate && _player != null && playerEdge > whaleHomeRange)
                {
                    Vector2 home = (_playerPos - g.pos).normalized;
                    want = (want + home * (0.9f * Mathf.Clamp01((playerEdge - whaleHomeRange) / 25f))).normalized;
                }
                // The formation trails behind the leader, so the leader keeps the clearance plus most of its reach.
                Vector2 s = SeaMath.SteerAvoid(g.pos, want, _whaleCircles, _whaleCircleCount, 24f + reach, SeaMath.WhaleClearance + 0.7f * reach);
                Turn(ref g, s, migrate ? 0.35f : 0.5f, dt);
                g.pos += g.dir * (g.speed * (g.state == 2 ? 0.5f : threat ? 1.5f : 1f) * dt);
            }

            g.timer -= dt;
            switch (g.state)
            {
                case 0:
                    if (threat) g.timer = Mathf.Max(g.timer, 4f);
                    if (!pod && !migrate)
                    {
                        g.breach -= dt;
                        if (g.breach <= 0f)
                        {
                            bool clear = !threat && !_obstacles.Inside(g.pos + g.dir * 5f, SeaMath.WhaleClearance);
                            if (_player != null && SeaMath.BreachAllowed(_storm, playerEdge, clear))
                            {
                                g.state = 3;
                                g.wander = 0f;
                                g.breach = Mathf.Lerp(breachIntervalMin, breachIntervalMax, SeaMath.Rand(g.seed, 95 + (int)_clock)) * (Cozy ? cozyBreachScale : 1f);
                                WhaleBreaches++;
                                if (Moments.Listening) Moments.Report(MomentKind.WhaleBreach, new Vector3(g.pos.x, 0f, g.pos.y));
                                break;
                            }
                            g.breach = 6f;
                        }
                    }
                    if (g.timer <= 0f && !threat)
                    {
                        g.state = 1;
                        g.wander = 0f;
                        g.timer = 3f * WhaleBreath + MaxLag(ref g, false);
                        if (playerEdge < 60f) WhaleSurfacings++;
                        if (Moments.Listening) Moments.Report(MomentKind.WhaleSurface, new Vector3(g.pos.x, 0f, g.pos.y));
                    }
                    break;
                case 1:
                {
                    g.wander += dt;
                    for (int m = 0; m < g.count; m++)
                    {
                        bool calf = IsCalf(ref g, m);
                        float period = calf ? CalfBreath : WhaleBreath;
                        float t = g.wander - BreathLag(ref g, m);
                        if (t < 0f || t >= (calf ? 4f : 3f) * period) continue;
                        float ta = t % period;
                        if (ta < 0.9f || ta >= 1.6f) continue;
                        int n0 = (int)(Mathf.Max(ta - dt, 0.9f) / 0.07f), n1 = (int)(ta / 0.07f);
                        for (int k = n0; k < n1; k++) Spout(ref g, m, k);
                    }
                    if (g.timer <= 0f || threat)
                    {
                        g.state = 2;
                        g.wander = 0f;
                        g.flags &= ~2;
                        if (threat || SeaMath.Rand(g.seed, 85 + (int)_clock) < 0.4f) g.flags |= 2;
                        g.timer = WhaleDive + MaxLag(ref g, true);
                        if (Moments.Listening)
                        {
                            Vector2 tail = g.pos - g.dir * (whaleLength * WhaleScale(ref g, 0) * 0.3f);
                            Moments.Report(MomentKind.WhaleFluke, new Vector3(tail.x, 0.8f, tail.y));
                        }
                    }
                    break;
                }
                case 2:
                {
                    float before = g.wander;
                    g.wander += dt;
                    for (int m = 0; m < g.count; m++)
                    {
                        float slap = 2.45f + DiveLag(ref g, m);
                        if (before >= slap || g.wander < slap) continue;
                        Vector2 w = WhaleMemberPos(ref g, m, out _);
                        bool calf = IsCalf(ref g, m);
                        Splash(w - g.dir * (whaleLength * WhaleScale(ref g, m) * 0.4f), calf ? 0.5f : 1f, calf ? 5 : 12);
                    }
                    if (g.timer <= 0f)
                    {
                        g.state = 0;
                        g.timer = migrate
                            ? Mathf.Lerp(whaleMigrationDive.x, Mathf.Max(whaleMigrationDive.x, whaleMigrationDive.y), SeaMath.Rand(g.seed, 80 + (int)_clock))
                            : Mathf.Lerp(whaleDeepSecondsMin, whaleDeepSecondsMax, SeaMath.Rand(g.seed, 80 + (int)_clock));
                    }
                    break;
                }
                default:
                {
                    float before = g.wander / BreachSeconds;
                    g.wander += dt;
                    float t = g.wander / BreachSeconds;
                    g.pos += g.dir * (2.2f * dt);
                    float len = whaleLength * whaleBullScale;
                    if (before < 0.08f && t >= 0.08f) Splash(g.pos + g.dir * (len * 0.3f), 0.7f, 8);
                    if (before < 0.8f && t >= 0.8f)
                    {
                        Splash(g.pos + g.dir * (len * 0.15f), 1f, 18);
                        Vector2 side = new Vector2(g.dir.y, -g.dir.x);
                        for (int k = 0; k < 10; k++)
                        {
                            uint h = SeaMath.Hash(g.seed, (uint)(k * 13 + (int)(_clock * 10f)));
                            Vector2 at = g.pos + g.dir * (len * (SeaMath.Rand(h, 0) - 0.4f) * 0.8f) + side * ((SeaMath.Rand(h, 1) - 0.5f) * 2.2f);
                            Vector3 vel = new Vector3((SeaMath.Rand(h, 2) - 0.5f) * 3.4f, 3.2f + 2.6f * SeaMath.Rand(h, 3), (SeaMath.Rand(h, 4) - 0.5f) * 3.4f);
                            EmitPuff(new Vector3(at.x, 0.1f, at.y), vel, 1.3f, 0.22f + 0.2f * SeaMath.Rand(h, 5), 5f);
                        }
                    }
                    if (before < 0.93f && t >= 0.93f) Splash(g.pos + g.dir * (len * 0.4f), 0.8f, 6);
                    if (t >= 1f)
                    {
                        g.state = 0;
                        g.timer = 16f + 10f * SeaMath.Rand(g.seed, 81 + (int)_clock);
                    }
                    break;
                }
            }
        }

        // A calf cannot hold its breath as long: between the pod's surfacings it comes up for a quick one of its own.
        float CalfPeek(ref Group g)
        {
            float c = (_clock + g.phase) % 12f;
            return c < 2.8f ? 0.9f * Mathf.Sin(c / 2.8f * Mathf.PI) : 0f;
        }

        // 0 = deep, 1 = back fully out.
        float WhaleSurface(ref Group g, int m)
        {
            float s;
            switch (g.state)
            {
                case 0:
                    s = IsCalf(ref g, m) ? CalfPeek(ref g) : 0f;
                    break;
                case 1:
                {
                    bool calf = IsCalf(ref g, m);
                    float period = calf ? CalfBreath : WhaleBreath;
                    float t = g.wander - BreathLag(ref g, m);
                    if (t < 0f) { s = 0f; break; }
                    if (t >= (calf ? 4f : 3f) * period) { s = 0.35f; break; }
                    float low = t < period ? 0f : 0.35f;
                    float c = t % period;
                    if (c < 1f) s = Mathf.Lerp(low, 1f, Mathf.SmoothStep(0f, 1f, c));
                    else if (c < period - 1.9f) s = 1f;
                    else if (c < period - 0.9f) s = Mathf.Lerp(1f, 0.35f, Mathf.SmoothStep(0f, 1f, c - (period - 1.9f)));
                    else s = 0.35f;
                    break;
                }
                case 2:
                    s = 0.35f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, g.wander - DiveLag(ref g, m)) / 1.2f));
                    break;
                default:
                    s = 0f;
                    break;
            }
            return s * (1f - g.depth);
        }

        // 0..1 height of the raised fluke during the dive, dropping fast for the slap.
        float WhaleFluke(ref Group g, int m)
        {
            if (g.state != 2) return 0f;
            float t = g.wander - DiveLag(ref g, m);
            if (t < 0f) return 0f;
            if (t < 1.2f) return Mathf.SmoothStep(0f, 1f, t / 1.2f);
            if (t < 2.2f) return 1f;
            if (t < 2.5f) return 1f - (t - 2.2f) / 0.3f;
            return 0f;
        }

        // Body centre of a breaching bull: up to two units above the water at the top of the leap.
        float BreachHeight(ref Group g) => Mathf.Lerp(-3.6f, 2f, Mathf.Sin(Mathf.Clamp01(g.wander / BreachSeconds) * Mathf.PI)) * (whaleLength * whaleBullScale / 8.125f);

        Vector2 WhaleMemberPos(ref Group g, int m, out float y)
        {
            Vector2 w = g.pos;
            if (g.kind == Kind.WhalePod && m > 0)
            {
                Vector2 right = new Vector2(g.dir.y, -g.dir.x);
                Vector2 o = SeaMath.WhalePodOffset(m, g.count, (g.flags & 1) != 0, g.seed) * whaleLength;
                o.x += Mathf.Sin(_clock * 0.23f + m * 1.9f + g.phase) * 0.35f;
                o.y += Mathf.Sin(_clock * 0.17f + m * 2.7f + g.phase) * 0.4f;
                w += right * o.x + g.dir * o.y;
            }
            if (g.state == 3) { y = BreachHeight(ref g); return w; }
            bool calf = IsCalf(ref g, m);
            y = Mathf.Lerp(calf ? -0.85f : -1.1f, calf ? -0.3f : -0.5f, WhaleSurface(ref g, m));
            y = Mathf.Lerp(y, -2.1f, g.depth);
            return w;
        }

        void Spout(ref Group g, int m, int k)
        {
            float scale = WhaleScale(ref g, m);
            Vector2 w = WhaleMemberPos(ref g, m, out _);
            Vector2 head = w + g.dir * (whaleLength * scale * 0.2f);
            uint h = SeaMath.Hash(g.seed, (uint)(k * 7 + m * 131 + (int)(_clock * 10f)));
            float power = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01((scale - 0.5f) * 2f)) * (scale > 1.05f ? 1.15f : 1f);
            Vector3 vel = new Vector3((SeaMath.Rand(h, 0) - 0.5f) * 0.9f, (2.6f + SeaMath.Rand(h, 1) * 1.2f) * power, (SeaMath.Rand(h, 2) - 0.5f) * 0.9f);
            vel.x += _wind.x * 0.4f; vel.z += _wind.y * 0.4f;
            EmitPuff(new Vector3(head.x, 0.35f * scale, head.y), vel, 1.6f * power, (0.26f + 0.16f * SeaMath.Rand(h, 3)) * power, 1.6f);
        }

        void StepTurtle(ref Group g, float dt)
        {
            g.timer -= dt;
            bool show = (g.flags & ShowFlag) != 0;
            if (show)
            {
                g.breach -= dt;
                // A visitor paddles along the player's coast for a while; it cannot keep up with a moving island.
                if (g.breach <= 0f || _player == null || g.target != _player || _playerSpeed > 1.6f)
                {
                    g.flags &= ~ShowFlag;
                    show = false;
                    g.state = 0;
                    g.target = null;
                    g.timer = 0f;
                }
            }
            if (g.target != null && (!g.target.isActiveAndEnabled || g.target.IsSunk)) g.target = null;
            if (g.target != null && g.target == _player && _playerSpeed > 1.6f && (g.flags & (PickupFlag | CompanionFlag)) == 0)
            {
                g.target = null;
                g.state = 0;
                g.timer = 0f;
            }
            if (g.state == 0 && (g.target == null || g.timer <= 0f))
            {
                uint h = SeaMath.Hash(g.seed, (uint)(_clock * 0.1f) + 3u);
                bool home = Cozy && _player != null && (g.flags & (PickupFlag | CompanionFlag)) == 0 && g.target != _player
                    && _playerSpeed < 1f && SeaMath.Rand(h, 1) < cozyTurtleToPlayer && (_playerPos - g.pos).sqrMagnitude < 90f * 90f;
                g.target = home ? _player : PickIsland(g.pos, 160f, h, g.target);
                g.timer = 60f;
            }
            Vector2 want;
            bool coast = g.target != null && g.target == _player;
            if (g.target != null)
            {
                Vector2 c = g.target.PlanarPosition;
                Vector2 r = g.pos - c;
                float rl = Mathf.Max(0.5f, r.magnitude);
                r /= rl;
                float ring = g.target.BoundingRadius + 3f;
                if (g.state == 0)
                {
                    Vector2 goal = c + r * ring;
                    Vector2 to = goal - g.pos;
                    want = to.sqrMagnitude > 1e-4f ? to.normalized : g.dir;
                    if (rl < ring + 1.5f) { g.state = 1; g.timer = 10f + 10f * SeaMath.Rand(g.seed, 90 + (int)_clock); }
                }
                else
                {
                    // Round the player's island the turtle follows the real shelf, where the camera sees it.
                    float radial = coast
                        ? Mathf.Clamp((g.target.SampleHeight(g.target.ToLocal(g.pos)) - turtleCoastDepth) / 0.35f, -1f, 1f)
                        : Mathf.Clamp((ring - rl) * 0.6f, -1f, 1f);
                    if (coast && rl > ring) radial = Mathf.Min(radial, -0.8f);
                    want = (new Vector2(-r.y, r.x) + r * radial).normalized;
                    if (!show && g.timer <= 0f) { g.state = 0; g.timer = 0f; }
                }
            }
            else
            {
                g.state = 0;
                want = Wander(ref g, 0.4f);
            }
            float speed = g.speed * (g.state == 1 ? (show ? 0.65f : 0.5f) : 1f);
            if (!coast && _playerSpeed > 0.4f && (g.pos - _playerPos).magnitude - _playerRadius < 3f) speed *= 2.2f;
            Turn(ref g, Steer(g.pos, want, 6f, 2.5f, coast && g.state == 1), 1.3f, dt);
            g.pos += g.dir * (speed * dt);

            float breath = SeaShow.TurtleBreath(_clock, g.phase, TurtleRate(ref g));
            if (SeaShow.Surfaced(g.wander, breath) && g.fade > 0.5f)
            {
                Vector2 head = g.pos + g.dir * (turtleLength * 0.4f);
                Splash(head, 0.2f, show ? 3 : 1);
                if (Moments.Listening) Moments.Report(MomentKind.TurtleBreath, new Vector3(head.x, 0.05f, head.y));
            }
            g.wander = breath;
        }

        float TurtleRate(ref Group g) => (g.flags & ShowFlag) != 0 ? showTurtleBreathRate : 0.22f;

        // 0 down, 1 shell out of the water: rises early in the breath so the turtle stays up for a few seconds.
        float TurtleLift(ref Group g) => Mathf.Clamp01(SeaShow.TurtleBreath(_clock, g.phase, TurtleRate(ref g)) * 2.5f);

        Island PickIsland(Vector2 from, float range, uint h, Island except)
        {
            int n = 0;
            for (int i = 1; i < _obstacles.count; i++)
            {
                var isl = _obstacles.islands[i];
                if (isl == null || isl == except || isl.IsEmerging) continue;
                if ((isl.PlanarPosition - from).sqrMagnitude > range * range) continue;
                n++;
            }
            if (n == 0) return null;
            int pick = (int)(h % (uint)n);
            for (int i = 1; i < _obstacles.count; i++)
            {
                var isl = _obstacles.islands[i];
                if (isl == null || isl == except || isl.IsEmerging) continue;
                if ((isl.PlanarPosition - from).sqrMagnitude > range * range) continue;
                if (pick-- == 0) return isl;
            }
            return null;
        }

        // ---- individuals ----

        // Planar position and height of member m; a pure function of the group state and the clock.
        Vector2 MemberPos(ref Group g, int m, out float y)
        {
            Vector2 right = new Vector2(g.dir.y, -g.dir.x);
            uint h = SeaMath.Hash(g.seed, (uint)m + 11u);
            switch (g.kind)
            {
                case Kind.Dolphins:
                {
                    int row = (m + 1) / 2;
                    float side = (m & 1) == 1 ? -1f : 1f;
                    float ox = side * row * 0.95f + Mathf.Sin(_clock * 0.7f + m * 1.9f) * 0.2f;
                    float oy = -row * 1.25f + Mathf.Sin(_clock * 0.5f + m * 2.3f) * 0.25f;
                    float jt = DolphinJumpT(ref g, m);
                    y = jt >= 0f
                        ? -0.3f + (dolphinJumpHeight + 0.3f) * Mathf.Sin(jt * Mathf.PI)
                        : -0.32f + 0.17f * Mathf.Sin(_clock * 1.7f + m * 1.3f + g.phase);
                    return g.pos + right * ox + g.dir * oy;
                }
                case Kind.Jellies:
                {
                    float a = SeaMath.Rand(h, 0) * Mathf.PI * 2f + _clock * 0.03f;
                    float rr = (0.6f + 2.8f * Mathf.Sqrt(SeaMath.Rand(h, 1)));
                    float ph = SeaMath.Rand(h, 2) * 6.28f;
                    y = -0.42f + 0.12f * Mathf.Sin(_clock * 0.9f + ph) - 0.5f * _storm;
                    return g.pos + new Vector2(Mathf.Cos(a) * rr + Mathf.Sin(_clock * 0.23f + ph) * 0.3f, Mathf.Sin(a) * rr + Mathf.Cos(_clock * 0.19f + ph) * 0.3f);
                }
                case Kind.Ray:
                    y = -0.6f;
                    return g.pos + right * (m * 1.6f) - g.dir * (m * 1.1f);
                case Kind.Gulls:
                {
                    float a = SeaMath.Rand(h, 0) * Mathf.PI * 2f;
                    float rr = 0.5f + 1.3f * SeaMath.Rand(h, 1);
                    Vector2 w = g.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                    y = 0.02f + 0.05f * SeaMath.Swell(w, _clock);
                    return w;
                }
                case Kind.Seaweed:
                {
                    float a = SeaMath.Rand(h, 0) * Mathf.PI * 2f;
                    float rr = 1.9f * Mathf.Sqrt(SeaMath.Rand(h, 1));
                    y = -0.3f;
                    return g.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                }
                case Kind.Turtle:
                {
                    y = Mathf.Lerp(-0.34f, (g.flags & ShowFlag) != 0 ? -0.01f : -0.04f, TurtleLift(ref g));
                    return g.pos;
                }
                default:
                    return WhaleMemberPos(ref g, m, out y);
            }
        }

        float DolphinJumpT(ref Group g, int m)
        {
            if (g.jump < 0f) return -1f;
            float t = (g.jump - m * 0.32f) / Mathf.Max(0.05f, dolphinJumpDuration);
            return t >= 0f && t < 1f ? t : -1f;
        }

        // ---- flying fish, bait-ball gulls, spray ----

        void StepFlyingFish(float dt)
        {
            if (_ffActive)
            {
                float before = _ffT;
                _ffT += dt;
                float lead = _ffT / _ffDur;
                if (_ffHops > 1 && lead < 1f && SeaShow.HopIndex(lead, _ffHops) != SeaShow.HopIndex(before / _ffDur, _ffHops))
                    Splash(_ffStart + _ffDir * (_ffLen * lead), 0.25f, 0);
                float total = _ffDur + _ffCount * 0.09f;
                if (_ffT >= total)
                {
                    _ffActive = false;
                    Splash(_ffStart + _ffDir * _ffLen, 0.45f, 0);
                }
                return;
            }
            _ffTimer -= dt;
            if (_ffTimer > 0f) return;
            _ffSeed = SeaMath.Hash(_ffSeed, (uint)(_clock * 13f) + 7u);
            Vector2 iv = Cozy ? cozyFlyingFishInterval : new Vector2(flyingFishIntervalMin, flyingFishIntervalMax);
            _ffTimer = Mathf.Lerp(iv.x, Mathf.Max(iv.x, iv.y), SeaMath.Rand(_ffSeed, 0));
            if (_night > 0.5f || _storm > 0.45f) return;
            // Cozy: the burst skips right across the picture instead of somewhere around the island.
            if (Cozy && _player != null && FlyingFishAcross(_viewCenter, _viewRadius, _ffSeed)) return;
            float a = SeaMath.Rand(_ffSeed, 1) * Mathf.PI * 2f;
            Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            if (_playerSpeed > 1f) d = (_playerVel / _playerSpeed + d * 0.7f).normalized;
            Vector2 start = _playerPos + d * (_playerRadius + 7f + 18f * SeaMath.Rand(_ffSeed, 2));
            float b = (SeaMath.Rand(_ffSeed, 3) - 0.5f) * 2.4f;
            Vector2 dir = Rotate(d, 1.57f + b);
            if (_obstacles.Inside(start, 2f) || _obstacles.Inside(start + dir * 6f, 2f)) return;
            BeginFlyingFish(start, dir, 5 + (int)(SeaMath.Rand(_ffSeed, 4) * 3.999f), 1, 5.5f, 1.3f);
        }

        void BeginFlyingFish(Vector2 start, Vector2 dir, int count, int hops, float length, float duration)
        {
            _ffActive = true;
            _ffStart = start;
            _ffDir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right;
            _ffT = 0f;
            _ffCount = count;
            _ffHops = Mathf.Max(1, hops);
            _ffLen = length;
            _ffDur = Mathf.Max(0.3f, duration);
            Splash(start, 0.4f, 0);
            if (Moments.Listening)
            {
                Vector2 mid = _ffStart + _ffDir * (_ffLen * 0.5f);
                Moments.Report(MomentKind.FlyingFish, new Vector3(mid.x, 0.4f, mid.y));
            }
            _dirty = true;
        }

        // Starts a flying-fish burst at a spot right away (verification).
        public void TriggerFlyingFish(Vector2 start, Vector2 dir) => BeginFlyingFish(start, dir, 7, 1, 5.5f, 1.3f);

        void StepBaitGulls(float dt)
        {
            _gullHasBall = false;
            if (fish != null && _night < 0.6f && _storm < 0.5f)
            {
                float best = float.MaxValue;
                Vector2 ball = default;
                for (int n = 0; n < 2; n++)
                {
                    if (!fish.TryGetBaitBall(n, out Vector2 p, out _)) break;
                    float d = (p - _playerPos).sqrMagnitude;
                    if (d >= best) continue;
                    best = d;
                    ball = p;
                    _gullHasBall = true;
                }
                if (_gullHasBall)
                    _gullCenter = _gullFade <= 0f || dt <= 0f ? ball : Vector2.Lerp(_gullCenter, ball, 1f - Mathf.Exp(-2f * dt));
            }
            if (dt <= 0f) { _gullFade = _gullHasBall ? 1f : 0f; return; }
            _gullFade = Mathf.MoveTowards(_gullFade, _gullHasBall ? 1f : 0f, dt * 0.5f);
            if (_gullFade <= 0f) return;
            if (_gullDiveT >= 0f)
            {
                float before = _gullDiveT;
                _gullDiveT += dt / 2.4f;
                if (before < 0.5f && _gullDiveT >= 0.5f) Splash(BaitGullPos(_gullDiver, out _, out _), 0.5f, 3);
                if (_gullDiveT >= 1f) _gullDiveT = -1f;
            }
            else
            {
                _gullDiveTimer -= dt;
                if (_gullDiveTimer <= 0f && _gullHasBall)
                {
                    _gullDiveT = 0f;
                    _gullDiver = (int)(SeaMath.Rand(_ffSeed, 20 + (int)_clock) * 4.999f);
                    _gullDiveTimer = 5f + 6f * SeaMath.Rand(_ffSeed, 30 + (int)_clock);
                    if (Moments.Listening)
                    {
                        Vector2 at = BaitGullPos(_gullDiver, out float gh, out _);
                        Moments.Report(MomentKind.GullDive, new Vector3(at.x, gh, at.y));
                    }
                }
            }
        }

        const int BaitGulls = 5;

        Vector2 BaitGullPos(int i, out float height, out Vector2 forward)
        {
            float a = _clock * 0.75f + i * (Mathf.PI * 2f / BaitGulls) + Mathf.Sin(_clock * 0.3f + i) * 0.5f;
            float r = 2.6f + 0.9f * Mathf.Sin(_clock * 0.41f + i * 2.1f);
            height = 2.7f + 0.6f * Mathf.Sin(_clock * 0.5f + i * 1.3f);
            if (i == _gullDiver && _gullDiveT >= 0f)
            {
                float k = Mathf.Sin(_gullDiveT * Mathf.PI);
                height = Mathf.Lerp(height, 0.12f, k);
                r *= 1f - 0.75f * k;
            }
            Vector2 radial = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            forward = new Vector2(-radial.y, radial.x);
            return _gullCenter + radial * r;
        }

        // Splash ring on the water plus spray droplets (also used by ShipSystem when a boat runs aground).
        public void SprayAt(Vector2 pos, float strength, int spray) => Splash(pos, strength, spray);

        void Splash(Vector2 pos, float strength, int spray)
        {
            float d = LifeLod.Distance(new Vector3(pos.x, 0f, pos.y));
            if (d > splashDistance) return;
            if (water != null) water.Splash(pos, strength);
            for (int i = 0; i < spray; i++)
            {
                uint h = SeaMath.Hash((uint)(pos.x * 31f) + (uint)i * 977u, (uint)(_clock * 60f));
                float a = i * (Mathf.PI * 2f / spray) + SeaMath.Rand(h, 0);
                float out_ = (0.7f + 0.8f * SeaMath.Rand(h, 1)) * (0.6f + strength);
                Vector3 vel = new Vector3(Mathf.Cos(a) * out_, (1.6f + 1.4f * SeaMath.Rand(h, 2)) * (0.6f + strength), Mathf.Sin(a) * out_);
                EmitPuff(new Vector3(pos.x, 0.05f, pos.y), vel, 0.7f + 0.3f * strength, 0.07f + 0.09f * strength, 6f);
            }
        }

        static readonly Color SprayColor = new Color(1.25f, 1.3f, 1.35f, 1f);

        void EmitPuff(Vector3 pos, Vector3 vel, float life, float size, float gravity) => EmitPuff(pos, vel, life, size, gravity, SprayColor);

        void EmitPuff(Vector3 pos, Vector3 vel, float life, float size, float gravity, Color color)
        {
            int slot = -1;
            float oldest = -1f;
            for (int i = 0; i < _puffs.Length; i++)
            {
                if (_puffs[i].life <= 0f) { slot = i; break; }
                float k = _puffs[i].age / _puffs[i].life;
                if (k > oldest) { oldest = k; slot = i; }
            }
            _puffs[slot] = new Puff { pos = pos, vel = vel, age = 0f, life = life, size = size, gravity = gravity, color = color };
        }

        void StepPuffs(float dt)
        {
            for (int i = 0; i < _puffs.Length; i++)
            {
                if (_puffs[i].life <= 0f) continue;
                ref Puff p = ref _puffs[i];
                p.age += dt;
                p.vel.y -= p.gravity * dt;
                p.vel.x *= 1f - 0.8f * dt; p.vel.z *= 1f - 0.8f * dt;
                p.pos += p.vel * dt;
                if (p.age >= p.life || (p.pos.y < -0.05f && p.vel.y < 0f)) p.life = 0f;
            }
        }

        // Verification helpers: force a behaviour on the first matching group.

        public bool TriggerDolphinJump(int group = -1)
        {
            bool any = false;
            for (int i = 0; i < _groups.Length; i++)
                if (_groups[i].active && _groups[i].kind == Kind.Dolphins && (group < 0 || group == i)) { _groups[i].jump = 0f; any = true; }
            return any;
        }

        public bool TriggerWhaleSurface(int group = -1)
        {
            bool any = false;
            for (int i = 0; i < _groups.Length; i++)
                if (_groups[i].active && IsWhale(_groups[i].kind) && (group < 0 || group == i))
                {
                    _groups[i].state = 1; _groups[i].wander = 0f; _groups[i].timer = 3f * WhaleBreath + MaxLag(ref _groups[i], false);
                    any = true;
                }
            return any;
        }

        public bool TriggerWhaleDive(int group = -1, bool synchronized = false)
        {
            bool any = false;
            for (int i = 0; i < _groups.Length; i++)
                if (_groups[i].active && IsWhale(_groups[i].kind) && (group < 0 || group == i))
                {
                    _groups[i].state = 2; _groups[i].wander = 0f;
                    _groups[i].flags = synchronized ? _groups[i].flags | 2 : _groups[i].flags & ~2;
                    _groups[i].timer = WhaleDive + MaxLag(ref _groups[i], true);
                    any = true;
                }
            return any;
        }

        public bool TriggerWhaleBreach(int group = -1)
        {
            bool any = false;
            for (int i = 0; i < _groups.Length; i++)
                if (_groups[i].active && _groups[i].kind == Kind.Whale && (group < 0 || group == i))
                {
                    _groups[i].state = 3; _groups[i].wander = 0f;
                    WhaleBreaches++;
                    any = true;
                }
            return any;
        }

        // ---- travel encounters (Encounters.cs directs, these place the animals) ----

        const int EscortFlag = 16;

        public bool GroupIsEscort(int i) => _groups[i].active && _groups[i].kind == Kind.Dolphins && (_groups[i].flags & EscortFlag) != 0;

        // A pod joins the moving player at the bow for `seconds`: an idle pod in range is recruited, otherwise a new
        // one arrives from behind on one flank (sprinting to catch up). Returns the group slot or -1.
        public int StartDolphinEscort(float seconds, bool rightSide)
        {
            EnsureArrays();
            if (_player == null) return -1;
            Vector2 fwd = PlayerCourse();
            int slot = -1;
            float best = float.MaxValue;
            for (int i = 0; i < _groups.Length; i++)
            {
                ref Group g = ref _groups[i];
                if (!g.active || g.kind != Kind.Dolphins || g.state == 2) continue;
                float d = (g.pos - _playerPos).magnitude - _playerRadius;
                if (d > spawnRadius * 0.8f || d >= best) continue;
                best = d;
                slot = i;
            }
            if (slot < 0)
            {
                Vector2 right = new Vector2(fwd.y, -fwd.x);
                for (int attempt = 0; attempt < 4 && slot < 0; attempt++)
                {
                    float sgn = ((attempt & 1) == 0) == rightSide ? 1f : -1f;
                    float back = attempt < 2 ? 12f : 4f;
                    Vector2 p = _playerPos - fwd * (_playerRadius * 0.3f + back) + right * (sgn * (_playerRadius + 16f));
                    if (Island.PositionConstraint != null) p = Island.PositionConstraint(p);
                    if (_obstacles.Inside(p, 3f)) continue;
                    slot = SpawnAt(Kind.Dolphins, p, fwd);
                    _groups[slot].fade = 0f;
                }
                if (slot < 0) return -1;
            }
            ref Group e = ref _groups[slot];
            e.state = 1;
            e.timer = Mathf.Max(4f, seconds);
            e.cooldown = 0f;
            e.wander = 0f;
            e.flags |= EscortFlag;
            if (e.jump < 0f) e.jump = Mathf.Max(e.jump, -1.5f);
            _dirty = true;
            return slot;
        }

        Vector2 PlayerCourse()
        {
            Vector2 fwd = _playerSpeed > 0.05f ? _playerVel / _playerSpeed : new Vector2(_player.BodyForward.x, _player.BodyForward.z);
            return fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector2.up;
        }

        // A whale (a solitary bull, or a pod with a chance of a calf) surfaces beside the player's course: `lead` ahead
        // of the bow and `lateral` beyond the whale clearance off its flank, breathing after `surfaceIn` seconds, then
        // the fluke dive. A whale already in range ahead is asked to come up instead. Returns the group slot or -1
        // when there was no open water for it.
        public int StartWhaleEncounter(float lead, float lateral, float surfaceIn, bool pod, bool rightSide)
        {
            EnsureArrays();
            if (_player == null) return -1;
            Vector2 fwd = PlayerCourse();
            for (int i = 0; i < _groups.Length; i++)
            {
                ref Group g = ref _groups[i];
                if (!g.active || !IsWhale(g.kind) || g.fade < 0.3f) continue;
                Vector2 to = g.pos - _playerPos;
                if (to.magnitude - _playerRadius > 70f || Vector2.Dot(to, fwd) < -10f) continue;
                if (g.state == 0) g.timer = Mathf.Min(g.timer, surfaceIn);
                return i;
            }
            Kind kind = pod ? Kind.WhalePod : Kind.Whale;
            if (!SeaMath.WhaleSpawnAllowed(pod, WhalePodCount, WhaleLonerCount, maxWhalePods, maxWhales))
            {
                kind = pod ? Kind.Whale : Kind.WhalePod;
                if (!SeaMath.WhaleSpawnAllowed(!pod, WhalePodCount, WhaleLonerCount, maxWhalePods, maxWhales)) return -1;
            }
            float reach = kind == Kind.WhalePod ? 2.7f * whaleLength : whaleLength * whaleBullScale;
            Vector2 right = new Vector2(fwd.y, -fwd.x);
            float side0 = rightSide ? 1f : -1f;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                float sgn = (attempt & 1) == 0 ? side0 : -side0;
                float l = lead * (attempt < 2 ? 1f : attempt < 4 ? 0.7f : 1.35f);
                Vector2 p = _playerPos + fwd * (_playerRadius + l) + right * (sgn * (_playerRadius + SeaMath.WhaleClearance + lateral));
                if (_obstacles.Inside(p, SeaMath.WhaleClearance + reach)) continue;
                int slot = SpawnAt(kind, p, Rotate(fwd, sgn * -0.25f));
                ref Group g = ref _groups[slot];
                g.fade = 0f;
                g.state = 0;
                g.timer = Mathf.Max(0.5f, surfaceIn);
                g.breach = Mathf.Max(g.breach, 30f);
                return slot;
            }
            return -1;
        }

        // ---- adventure: sea animals to pick up and to be escorted by (Encounters lays them on the track) ----

        // A pickup swims slowly along the band waiting to be run over; from then on it is a companion that swims
        // alongside the island and pushes it on (Encounters turns the count into the boost). Flag 4 = right flank.
        const int PickupFlag = 64;
        const int CompanionFlag = 128;
        const int RightFlag = 4;

        public bool GroupIsPickup(int i) => _groups != null && _groups[i].active && (_groups[i].flags & PickupFlag) != 0;
        public bool GroupIsCompanion(int i) => _groups != null && _groups[i].active && (_groups[i].flags & CompanionFlag) != 0;
        public float CompanionSecondsLeft(int i) => GroupIsCompanion(i) ? Mathf.Max(0f, _groups[i].companion) : 0f;
        public int PickupsCollected { get; private set; }

        public int PickupCount => CountFlag(PickupFlag);
        public int CompanionCount => CountFlag(CompanionFlag);

        int CountFlag(int flag)
        {
            int n = 0;
            if (_groups != null)
                for (int i = 0; i < _groups.Length; i++)
                    if (_groups[i].active && (_groups[i].flags & flag) != 0) n++;
            return n;
        }

        // How far the animals of a group reach out from its position (the lane beside the island, the grab range).
        float EscortReach(ref Group g) => IsWhale(g.kind) ? WhaleReach(ref g) : 0.7f;

        // Lays a whale or a sea turtle on the track, waiting to be collected. -1 when there is no room for it.
        public int SpawnPickup(Kind kind, Vector2 pos, Vector2 dir)
        {
            EnsureArrays();
            if (kind != Kind.Whale && kind != Kind.Turtle) return -1;
            if (PickupCount >= maxPickups) return -1;
            int slot = SpawnAt(kind, pos, dir);
            if (slot < 0) return -1;
            ref Group g = ref _groups[slot];
            g.flags |= PickupFlag;
            g.fade = 0f;
            g.depth = 0f;
            g.wander = 0f;
            g.breach = 1e6f;
            g.target = null;
            if (IsWhale(kind)) { g.state = 1; g.timer = 1e6f; }
            else { g.state = 0; g.timer = 1e6f; }
            _dirty = true;
            return slot;
        }

        // The player ran a waiting animal over: it swims up to a free flank and stays for `seconds`.
        public bool MakeCompanion(int slot, float seconds)
        {
            if (_groups == null || slot < 0 || slot >= _groups.Length || !_groups[slot].active) return false;
            ref Group g = ref _groups[slot];
            if ((g.flags & CompanionFlag) != 0) return false;
            g.flags &= ~PickupFlag;
            g.flags |= CompanionFlag;
            if (RightFlankTaken()) g.flags &= ~RightFlag; else g.flags |= RightFlag;
            g.companion = Mathf.Max(2f, seconds);
            g.cooldown = 0f;
            g.target = null;
            PickupsCollected++;
            Vector3 at = new Vector3(g.pos.x, 0.05f, g.pos.y);
            Splash(g.pos, IsWhale(g.kind) ? 1f : 0.6f, IsWhale(g.kind) ? 14 : 8);
            Sparkle(at, 1f, 10);
            Encounters.NotifyCompanion(IsWhale(g.kind) ? CompanionKind.Whale : CompanionKind.Turtle, at);
            _dirty = true;
            return true;
        }

        bool RightFlankTaken()
        {
            for (int i = 0; i < _groups.Length; i++)
                if (_groups[i].active && (_groups[i].flags & CompanionFlag) != 0 && (_groups[i].flags & RightFlag) != 0) return true;
            return false;
        }

        // Adventure only: every waiting animal the island has reached becomes a companion. Returns how many joined.
        public int CollectPickups() => _player != null ? CollectPickupsAt(_playerPos, _playerRadius) : 0;

        public int CollectPickupsAt(Vector2 pos, float radius)
        {
            if (_groups == null) return 0;
            int joined = 0;
            for (int i = 0; i < _groups.Length; i++)
            {
                ref Group g = ref _groups[i];
                if (!g.active || (g.flags & PickupFlag) == 0) continue;
                if (CompanionCount >= maxCompanions) break;
                float reach = radius + pickupGrab + EscortReach(ref g);
                if ((g.pos - pos).sqrMagnitude > reach * reach) continue;
                if (MakeCompanion(i, companionSeconds)) joined++;
            }
            return joined;
        }

        // Pickups and companions are driven straight to a goal instead of steering for themselves: a pickup that
        // dodged the island could never be collected, and a companion has to hold its lane beside the bow.
        void StepEscort(ref Group g, float dt)
        {
            bool companion = (g.flags & CompanionFlag) != 0;
            Vector2 fwd = _player != null ? PlayerCourse() : g.dir;
            Vector2 goal;
            float speed;
            if (companion)
            {
                g.companion -= dt;
                if (g.companion <= 0f || _player == null) { EndCompanion(ref g); return; }
                Vector2 right = new Vector2(fwd.y, -fwd.x);
                float lane = _playerRadius + companionGap + EscortReach(ref g);
                float sway = Mathf.Sin(_clock * 0.35f + g.phase);
                // A little ahead of the bow, not exactly abreast: from the chase camera that is where you see it.
                goal = _playerPos + right * (((g.flags & RightFlag) != 0 ? 1f : -1f) * lane)
                     + fwd * (lane * companionLead + sway * 2.5f);
                speed = Mathf.Min(companionCatchUp, Mathf.Max(2f, _playerSpeed + 3f));
            }
            else
            {
                goal = g.pos + g.dir * 4f;
                speed = pickupDrift;
            }
            Vector2 to = goal - g.pos;
            float d = to.magnitude;
            Vector2 want = d > 0.2f ? to / d : fwd;
            Turn(ref g, companion && d < 3f ? fwd : want, companion ? 3.5f : 1.2f, dt);
            g.pos += want * Mathf.Min(d, speed * dt);
            if (Island.PositionConstraint != null) g.pos = Island.PositionConstraint(g.pos);
        }

        // The escort is over: the animal turns back into an ordinary one and leaves the way dolphins do.
        void EndCompanion(ref Group g)
        {
            g.flags &= ~(CompanionFlag | PickupFlag);
            g.companion = 0f;
            g.cooldown = 30f;
            g.breach = Mathf.Max(20f, g.breach > 1e5f ? 30f : g.breach);
            g.timer = IsWhale(g.kind) ? 2f : 0f;
            _dirty = true;
        }

        // ---- whale migration (WorldEvents directs it) ----

        const int MigrateFlag = 32;

        public bool GroupIsMigrating(int i) => _groups != null && _groups[i].active && (_groups[i].flags & MigrateFlag) != 0;

        // Groups of the line that are still drawn (they fade out as they leave the range).
        public int MigrationGroups
        {
            get
            {
                int n = 0;
                if (_groups != null)
                    for (int i = 0; i < _groups.Length; i++)
                        if (_groups[i].active && (_groups[i].flags & MigrateFlag) != 0 && _groups[i].fade > 0.05f) n++;
                return n;
            }
        }

        // Middle of the line, for a hint / an edge arrow. Zero while nothing migrates.
        public Vector2 MigrationCenter
        {
            get
            {
                Vector2 sum = Vector2.zero;
                int n = 0;
                if (_groups != null)
                    for (int i = 0; i < _groups.Length; i++)
                        if (_groups[i].active && (_groups[i].flags & MigrateFlag) != 0) { sum += _groups[i].pos; n++; }
                return n > 0 ? sum / n : Vector2.zero;
            }
        }

        // A line of whale pods and bulls crossing the sea ahead of the player: they hold one heading, surface one
        // group after the other (a row of spouts you can see from far off) and never breach out of the line.
        // `side` picks the flank, `lateral` how far off the course the line passes. Returns how many groups were
        // placed (0 = no room at all).
        public int StartWhaleMigration(Vector2 course, float side, int groups, float spacing, float lateral, float seconds)
        {
            EnsureArrays();
            if (_player == null) return 0;
            Vector2 f = course.sqrMagnitude > 1e-6f ? course.normalized : Vector2.up;
            Vector2 right = new Vector2(f.y, -f.x);
            float sgn = side >= 0f ? 1f : -1f;
            // About 55 degrees across the course: the line comes past from one side and carries on ahead, so
            // following it is a real detour but never a chase.
            Vector2 heading = Rotate(f, sgn * -0.95f);
            float angle = Mathf.Atan2(heading.y, heading.x);
            int n = Mathf.Clamp(groups, 2, 6);
            float step = Mathf.Max(8f, spacing);
            uint h0 = SeaMath.Hash((uint)seed + 77u, (uint)Mathf.Abs(_clock * 7f) + 1u);
            Vector2 centre = _playerPos + f * (_playerRadius + 24f) + right * (sgn * Mathf.Max(12f, lateral));
            int placed = 0;
            for (int i = 0; i < n; i++)
            {
                bool pod = SeaMath.Rand(h0, i * 5 + 2) < 0.7f;
                float clear = SeaMath.WhaleClearance + (pod ? 1.4f : 0.6f) * whaleLength;
                // Shifted back along the heading, so most of the line still has to come past; an island in the
                // way only moves that one animal a little further along the line.
                Vector2 p = Vector2.zero;
                bool room = false;
                for (int attempt = 0; attempt < 4 && !room; attempt++)
                {
                    float slide = attempt == 0 ? 0f : (attempt == 1 ? 0.45f : attempt == 2 ? -0.45f : 0.9f);
                    p = centre + heading * ((i - (n - 1) * 0.6f + slide) * step)
                      + right * ((SeaMath.Rand(h0, i * 5 + 1) - 0.5f) * 9f);
                    if (Island.PositionConstraint != null) p = Island.PositionConstraint(p);
                    room = !_obstacles.Inside(p, clear);
                }
                if (!room) continue;
                int slot = SpawnAt(pod ? Kind.WhalePod : Kind.Whale, p, heading);
                ref Group g = ref _groups[slot];
                g.flags |= MigrateFlag;
                if (pod)
                {
                    g.count = 2 + (int)(SeaMath.Rand(h0, i * 5 + 3) * 2.999f);
                    if (SeaMath.Rand(h0, i * 5 + 4) < 0.6f) g.flags |= 1;
                }
                g.jump = angle;
                g.speed = whaleMigrationSpeed * (pod ? 0.95f : 1.05f);
                g.fade = 0f;
                g.state = 0;
                g.depth = 0f;
                g.breach = Mathf.Max(60f, seconds);
                // The groups blow one after the other, from the head of the line backwards.
                g.timer = 2f + i * 3.5f;
                placed++;
            }
            _dirty = true;
            MigrationSpawned = placed;
            return placed;
        }

        // How many groups the last migration managed to place (verification / tuning).
        public int MigrationSpawned { get; private set; }

        // Lets the line go: the whales keep swimming, but from now on as ordinary whales (they wander, drift back
        // to the player and are recycled at the range edge like any other group).
        public void EndWhaleMigration()
        {
            if (_groups == null) return;
            for (int i = 0; i < _groups.Length; i++)
            {
                if (!_groups[i].active || (_groups[i].flags & MigrateFlag) == 0) continue;
                _groups[i].flags &= ~MigrateFlag;
                _groups[i].jump = -1f;
                _groups[i].speed = _groups[i].kind == Kind.WhalePod ? whaleSpeed * 0.9f : whaleSpeed;
                _groups[i].breach = Mathf.Max(_groups[i].breach, 30f);
            }
        }

        // Golden glints rising from the water (flotsam collected, a bottle winking in the sun).
        public void Sparkle(Vector3 pos, float strength, int count)
        {
            if (LifeLod.Distance(pos) > splashDistance) return;
            float glow = 1f + 3f * _night;
            Color gold = new Color(3.4f * glow, 2.7f * glow, 1.2f * glow, 1f);
            for (int i = 0; i < count; i++)
            {
                uint h = SeaMath.Hash((uint)(pos.x * 17f) + (uint)i * 613u, (uint)(_clock * 60f) + 5u);
                float a = i * (Mathf.PI * 2f / Mathf.Max(1, count)) + SeaMath.Rand(h, 0);
                float r = (0.3f + 0.5f * SeaMath.Rand(h, 1)) * strength;
                Vector3 p = pos + new Vector3(Mathf.Cos(a) * r, 0.1f + 0.3f * SeaMath.Rand(h, 3), Mathf.Sin(a) * r);
                Vector3 vel = new Vector3(Mathf.Cos(a) * 0.35f, 0.9f + 0.9f * SeaMath.Rand(h, 2), Mathf.Sin(a) * 0.35f) * strength;
                EmitPuff(p, vel, 0.8f + 0.5f * SeaMath.Rand(h, 4), (0.05f + 0.04f * SeaMath.Rand(h, 5)) * Mathf.Max(0.6f, strength), -0.3f, gold);
            }
            _dirty = true;
        }

        // Places a pod of a given make-up at a spot (verification).
        public int SpawnWhalePod(Vector2 pos, Vector2 dir, int count, bool calf)
        {
            int slot = SpawnAt(Kind.WhalePod, pos, dir);
            _groups[slot].count = Mathf.Clamp(count, 2, 4);
            _groups[slot].flags = calf ? 1 : 0;
            return slot;
        }

        // Places a group of the given kind at a spot, replacing the farthest group if the pool is full (verification).
        public int SpawnAt(Kind kind, Vector2 pos, Vector2 dir)
        {
            EnsureArrays();
            int slot = -1;
            float far = -1f;
            int fallback = 0;
            float fallbackFar = -1f;
            for (int i = 0; i < _groups.Length; i++)
            {
                if (!_groups[i].active) { slot = i; break; }
                float d = (_groups[i].pos - _playerPos).sqrMagnitude;
                if (d > fallbackFar) { fallbackFar = d; fallback = i; }
                // The line of a running migration is the spectacle, a companion is the player's own reward:
                // replace anything else first.
                if ((_groups[i].flags & (MigrateFlag | CompanionFlag)) != 0) continue;
                if (d > far) { far = d; slot = i; }
            }
            if (slot < 0) slot = fallback;
            uint h = SeaMath.Hash((uint)seed, (uint)(pos.x * 13f + pos.y * 7f) + (uint)kind);
            Activate(slot, long.MinValue + slot, pos, h, kind);
            _groups[slot].dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right;
            _groups[slot].fade = 1f;
            return slot;
        }

        // ---- mesh ----

        void Rebuild()
        {
            EnsureObjects();
            _under.Clear();
            _above.Clear();

            for (int i = 0; i < _groups.Length; i++)
            {
                if (!_groups[i].active || _groups[i].fade <= 0.01f) continue;
                ref Group g = ref _groups[i];
                bool detail = LifeLod.Distance(new Vector3(g.pos.x, 0f, g.pos.y)) < detailDistance;
                switch (g.kind)
                {
                    case Kind.Dolphins: DrawDolphins(ref g, detail); break;
                    case Kind.Whale: DrawWhales(ref g); break;
                    case Kind.WhalePod: DrawWhales(ref g); break;
                    case Kind.Turtle: DrawTurtle(ref g, detail); break;
                    case Kind.Jellies: DrawJellies(ref g, detail); break;
                    case Kind.Ray: DrawRays(ref g); break;
                    case Kind.Gulls: DrawGulls(ref g); break;
                    default: DrawSeaweed(ref g); break;
                }
            }
            if (ships != null && _night > 0.3f) DrawLanternGlow();
            if (_ffActive) DrawFlyingFish();
            if (_gullFade > 0.01f) DrawBaitGulls();
            if (SealCount > 0) DrawSeals();
            for (int i = 0; i < _puffs.Length; i++)
            {
                if (_puffs[i].life <= 0f) continue;
                float k = _puffs[i].age / _puffs[i].life;
                _above.Octa(_puffs[i].pos, _puffs[i].size * (k < 0.2f ? 0.5f + 2.5f * k : 1f - 0.8f * (k - 0.2f) / 0.8f), _puffs[i].color);
            }

            float ext = (recycleRadius + _playerRadius) * 2f + 20f;
            var bounds = new Bounds(new Vector3(_playerPos.x, 0f, _playerPos.y), new Vector3(ext, 12f, ext));
            _under.Apply(_underMesh, bounds);
            _above.Apply(_aboveMesh, bounds);
        }

        // Soft warm pools of light on the water under lit ship lanterns (alpha-faded disc in the blended mesh).
        void DrawLanternGlow()
        {
            float k = Mathf.Clamp01((_night - 0.3f) / 0.4f);
            for (int i = 0; i < ships.ShipSlots; i++)
            {
                if (!ships.TryGetLantern(i, out Vector3 lp, out float size)) continue;
                float flick = 0.9f + 0.1f * Mathf.Sin(_clock * 9f + i * 2.1f);
                Color inner = new Color(9f, 4.2f, 0.9f, 0.4f * k * flick), outer = new Color(9f, 3.6f, 0.7f, 0f);
                _under.Disc(new Vector3(lp.x, 0.05f, lp.z), 2.1f * size, 2.1f * size, Vector2.right, 8, inner, outer);
            }
        }

        void DrawDolphins(ref Group g, bool detail)
        {
            float len = dolphinLength * g.fade;
            Color body = dolphinColor;
            Color deep = new Color(body.r * 0.62f, body.g * 0.75f, body.b * 0.95f, 0.78f);
            for (int m = 0; m < g.count; m++)
            {
                Vector2 w = MemberPos(ref g, m, out float y);
                float jt = DolphinJumpT(ref g, m);
                float msize = len * (1f - 0.06f * m);
                if (jt >= 0f)
                {
                    float pitch = Mathf.Cos(jt * Mathf.PI) * 1.05f;
                    float roll = Mathf.Sin(jt * Mathf.PI) * 0.25f * ((m & 1) == 0 ? 1f : -1f);
                    SeaBatch.Basis(g.dir, pitch, roll, out Vector3 r, out Vector3 u, out Vector3 f);
                    _above.Add(detail ? _tDolphin : _tDolphinLow, new Vector3(w.x, y, w.y), r, u, f, new Vector3(msize, msize, msize), body);
                }
                else
                {
                    float pitch = Mathf.Cos(_clock * 1.7f + m * 1.3f + g.phase) * 0.18f;
                    SeaBatch.Basis(g.dir, pitch, 0f, out Vector3 r, out Vector3 u, out Vector3 f);
                    _under.AddFlat(_tDolphinLow, new Vector3(w.x, y, w.y), r, u, f, new Vector3(msize, msize, msize), deep);
                }
            }
        }

        void DrawWhales(ref Group g)
        {
            bool near = LifeLod.Distance(new Vector3(g.pos.x, 0f, g.pos.y)) < 32f;
            Color c = g.kind == Kind.Whale ? new Color(whaleColor.r * 0.88f, whaleColor.g * 0.88f, whaleColor.b * 0.9f) : whaleColor;
            for (int m = 0; m < g.count; m++)
            {
                float scale = WhaleScale(ref g, m);
                float len = whaleLength * scale * g.fade;
                Vector2 w = WhaleMemberPos(ref g, m, out float y);
                Vector2 dir = g.kind == Kind.WhalePod ? Rotate(g.dir, Mathf.Sin(_clock * 0.21f + m * 2.1f + g.phase) * 0.09f) : g.dir;

                if (g.state == 3)
                {
                    float t = Mathf.Clamp01(g.wander / BreachSeconds);
                    float st = t * t * (3f - 2f * t);
                    float twist = Mathf.Clamp01((t - 0.25f) / 0.6f);
                    float side = (g.seed & 2u) == 0u ? 1f : -1f;
                    SeaBatch.Basis(dir, Mathf.Lerp(1.05f, -0.3f, st), side * 1.5f * twist * twist * (3f - 2f * twist), out Vector3 br, out Vector3 bu, out Vector3 bf);
                    _above.Add(_tWhaleBreach, new Vector3(w.x, y, w.y), br, bu, bf, new Vector3(len, len, len), c);
                    continue;
                }

                float s = WhaleSurface(ref g, m);
                bool calf = IsCalf(ref g, m);
                Color body = calf ? new Color(c.r * 1.3f, c.g * 1.3f, c.b * 1.25f) : c;
                float diveT = g.state == 2 ? Mathf.Max(0f, g.wander - DiveLag(ref g, m)) : 0f;
                float pitch = g.state == 2 ? -0.25f * Mathf.Clamp01(diveT / 1.2f) : 0f;
                SeaBatch.Basis(dir, pitch, 0f, out Vector3 r, out Vector3 u, out Vector3 f);
                float tail = Mathf.Sin(_clock * 1.1f + g.phase + m * 1.7f) * 0.05f;
                Color deep = new Color(body.r * 0.55f, body.g * 0.7f, body.b * 0.9f, Mathf.Lerp(0.38f, 0.9f, s) * (1f - 0.5f * g.depth));
                _under.AddFlat(near && !calf ? _tWhale : _tWhaleLow, new Vector3(w.x, y + tail, w.y), r, u, f, new Vector3(len, len, len), deep);

                if (s > 0.36f)
                {
                    float rise = (s - 0.35f) / 0.65f;
                    Vector3 hp = new Vector3(w.x, -0.03f, w.y) + new Vector3(dir.x, 0f, dir.y) * (len * 0.04f);
                    SeaBatch.Basis(dir, 0f, 0f, out r, out u, out f);
                    _above.Add(_tWhaleHump, hp, r, u, f, new Vector3(len * (0.75f + 0.25f * rise), len * rise, len * (0.7f + 0.3f * rise)), body);
                }
                float fl = WhaleFluke(ref g, m);
                if (fl > 0.01f)
                {
                    Vector2 tp = w - dir * (len * 0.3f);
                    float lean = diveT > 2.2f ? -1.2f * (1f - fl) : 0.25f * (1f - fl);
                    SeaBatch.Basis(dir, lean, 0f, out r, out u, out f);
                    float fs = len * 0.42f;
                    _above.Add(_tWhaleFluke, new Vector3(tp.x, -0.75f * fs * (1f - fl) - 0.05f, tp.y), r, u, f, new Vector3(fs, fs, fs), body);
                }
            }
        }

        void DrawTurtle(ref Group g, bool detail)
        {
            MemberPos(ref g, 0, out float y);
            bool show = (g.flags & ShowFlag) != 0;
            float len = turtleLength * g.fade * (show ? 1.2f : 1f);
            float lift = TurtleLift(ref g);
            // Only once the shell really breaks the surface is it drawn opaque (the depth foam rings it); a body
            // still under water in the opaque mesh would show as a turquoise patch.
            bool up = lift > 0.8f;
            SeaBatch.Basis(g.dir, up ? 0.2f * lift : 0f, 0f, out Vector3 r, out Vector3 u, out Vector3 f);
            Vector3 p = new Vector3(g.pos.x, y, g.pos.y);
            Color tint = new Color(1f, 1f, 1f, 0.95f);
            if (detail || up)
            {
                float sweep = Mathf.Sin(_clock * 2.2f + g.phase) * 0.5f;
                for (int k = 0; k < 4; k++)
                {
                    bool front = k < 2;
                    float side = (k & 1) == 0 ? 1f : -1f;
                    float a = front ? 0.25f + sweep : -0.9f - sweep * 0.4f;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    Vector3 fr = (r * ca + f * sa) * side, ff = f * ca - r * sa;
                    Vector3 root = p + r * (side * 0.17f * len) + f * ((front ? 0.17f : -0.22f) * len) + u * (0.02f * len);
                    root.y = Mathf.Min(root.y, -0.06f);
                    float fs = len * (front ? 1f : 0.6f);
                    _under.Add(_tFlipper, root, fr, u, ff, new Vector3(fs, fs, fs), tint);
                }
            }
            if (up) _above.Add(_tTurtle, p, r, u, f, new Vector3(len, len, len), Color.white);
            else _under.Add(_tTurtle, p, r, u, f, new Vector3(len, len, len), tint);
        }

        void DrawJellies(ref Group g, bool detail)
        {
            uint sp = g.seed % 3u;
            Color c = sp == 0 ? new Color(1f, 0.62f, 0.82f) : sp == 1 ? new Color(0.74f, 0.62f, 1f) : new Color(0.6f, 0.95f, 1f);
            // The Fish shader multiplies by the (dim, blue) night light and the water tint, so "emissive" is a
            // vertex colour boosted far past 1 with the night light's hue divided back out.
            float glow = 1f + 8f * _night;
            float alpha = Mathf.Lerp(0.6f, 1f, _night) * (1f - 0.6f * _storm);
            Color col = new Color(c.r * glow * Mathf.Lerp(1f, 1.9f, _night), c.g * glow * Mathf.Lerp(1f, 1.45f, _night), c.b * glow, alpha);
            Color halo = new Color(col.r, col.g, col.b, 0.34f * _night * (1f - 0.6f * _storm));
            Color haloEdge = new Color(col.r, col.g, col.b, 0f);
            var t = detail ? _tJelly : _tJellyLow;
            for (int m = 0; m < g.count; m++)
            {
                Vector2 w = MemberPos(ref g, m, out float y);
                uint h = SeaMath.Hash(g.seed, (uint)m + 11u);
                float pulse = Mathf.Sin(_clock * 1.6f + SeaMath.Rand(h, 2) * 6.28f);
                float size = jellySize * (0.7f + 0.6f * SeaMath.Rand(h, 3)) * g.fade;
                if (detail && _night > 0.3f) _under.Disc(new Vector3(w.x, y - 0.05f, w.y), size * 2.6f, size * 2.6f, Vector2.right, 6, halo, haloEdge);
                _under.Add(t, new Vector3(w.x, y, w.y), Vector3.right, Vector3.up, Vector3.forward,
                    new Vector3(size * (1f + 0.16f * pulse), size * (1f - 0.2f * pulse), size * (1f + 0.16f * pulse)), col);
            }
        }

        static readonly Color RayBody = new Color(0.1f, 0.14f, 0.2f, 0.8f), RayMid = new Color(0.17f, 0.22f, 0.3f, 0.85f);
        static readonly Color RayTop = new Color(0.2f, 0.25f, 0.33f, 1f), RayRidge = new Color(0.3f, 0.36f, 0.45f, 1f);

        void DrawRays(ref Group g)
        {
            Vector3 f = new Vector3(g.dir.x, 0f, g.dir.y), r = new Vector3(g.dir.y, 0f, -g.dir.x);
            for (int m = 0; m < g.count; m++)
            {
                Vector2 w = MemberPos(ref g, m, out float y);
                float span = raySpan * (1f - 0.2f * m) * g.fade;
                if (m == 0 && g.jump >= 0f)
                {
                    float ly = SeaShow.RayLeap(g.jump, rayLeapHeight, out float pitch, out float roll);
                    SeaBatch.Basis(g.dir, pitch, roll, out Vector3 lr, out Vector3 lu, out Vector3 lf);
                    float beat = Mathf.Sin(_clock * 7f + g.phase) * 0.3f * span;
                    Vector3 lp = new Vector3(w.x, ly, w.y);
                    if (ly > -0.05f) RayShape(_above, lp, lf, lr, lu, span, beat, RayTop, RayRidge, m);
                    else RayShape(_under, lp, lf, lr, lu, span, beat, RayBody, RayMid, m);
                    continue;
                }
                float flap = Mathf.Sin(_clock * 1.8f + g.phase + m) * 0.16f * span;
                if (!RayShape(_under, new Vector3(w.x, y, w.y), f, r, Vector3.up, span, flap, RayBody, RayMid, m)) return;
            }
        }

        bool RayShape(SeaBatch b, Vector3 p, Vector3 f, Vector3 r, Vector3 u, float span, float flap, Color body, Color mid, int m)
        {
            if (!b.Fits(8, 15)) return false;
            int i = b.vc;
            var v = b.verts; var c = b.cols; var t = b.tris;
            v[i] = p + f * (0.45f * span);
            v[i + 1] = p - r * (0.5f * span) + u * flap - f * (0.05f * span);
            v[i + 2] = p + r * (0.5f * span) + u * flap - f * (0.05f * span);
            v[i + 3] = p - f * (0.35f * span);
            v[i + 4] = p + f * (0.05f * span) + u * (0.04f * span);
            v[i + 5] = p - f * (0.33f * span) - r * (0.025f * span);
            v[i + 6] = p - f * (0.33f * span) + r * (0.025f * span);
            v[i + 7] = p - f * (1.05f * span) + r * (Mathf.Sin(_clock * 1.3f + m) * 0.08f * span);
            c[i] = body; c[i + 1] = body; c[i + 2] = body; c[i + 3] = body; c[i + 4] = mid; c[i + 5] = body; c[i + 6] = body; c[i + 7] = body;
            if (b.norms != null) for (int k = 0; k < 8; k++) b.norms[i + k] = u;
            int n = b.tc;
            t[n++] = i; t[n++] = i + 4; t[n++] = i + 1;
            t[n++] = i; t[n++] = i + 2; t[n++] = i + 4;
            t[n++] = i + 1; t[n++] = i + 4; t[n++] = i + 3;
            t[n++] = i + 4; t[n++] = i + 2; t[n++] = i + 3;
            t[n++] = i + 5; t[n++] = i + 6; t[n++] = i + 7;
            b.tc = n;
            b.vc += 8;
            return true;
        }

        void DrawGulls(ref Group g)
        {
            bool fleeing = g.state == 1;
            for (int m = 0; m < g.count; m++)
            {
                Vector2 w = MemberPos(ref g, m, out float y);
                uint h = SeaMath.Hash(g.seed, (uint)m + 11u);
                Vector2 d = fleeing ? g.dir : Rotate(g.dir, (SeaMath.Rand(h, 4) - 0.5f) * 2.5f + Mathf.Sin(_clock * 0.2f + m) * 0.4f);
                float pitch = 0.06f * Mathf.Sin(_clock * 1.1f + m * 2f);
                float roll = 0.08f * Mathf.Sin(_clock * 0.9f + m * 1.4f);
                SeaBatch.Basis(d, pitch, roll, out Vector3 r, out Vector3 u, out Vector3 f);
                float size = 0.62f * g.fade;
                Vector3 p = new Vector3(w.x, y, w.y);
                _above.Add(_tGull, p, r, u, f, size);
                if (fleeing)
                {
                    float flap = Mathf.Sin(_clock * 16f + m * 1.7f) * 0.7f + 0.35f;
                    Vector3 wr = r * Mathf.Cos(flap) + u * Mathf.Sin(flap);
                    Vector3 wu = u * Mathf.Cos(flap) - r * Mathf.Sin(flap);
                    _above.Add(_tGullWing, p, wr, wu, f, size);
                    _above.Add(_tGullWing, p, -(r * Mathf.Cos(flap) - u * Mathf.Sin(flap)), u * Mathf.Cos(flap) + r * Mathf.Sin(flap), f, size);
                }
            }
        }

        void DrawSeaweed(ref Group g)
        {
            Color a = new Color(seaweedColor.r, seaweedColor.g, seaweedColor.b, 0.62f);
            Color b = new Color(seaweedColor.r * 1.5f, seaweedColor.g * 1.25f, seaweedColor.b * 0.9f, 0.5f);
            for (int m = 0; m < g.count; m++)
            {
                Vector2 w = MemberPos(ref g, m, out float y);
                uint h = SeaMath.Hash(g.seed, (uint)m + 11u);
                float ang = SeaMath.Rand(h, 4) * 6.28f + Mathf.Sin(_clock * 0.5f + m) * 0.25f;
                float len = (0.9f + 0.9f * SeaMath.Rand(h, 5)) * g.fade, wid = 0.2f * g.fade;
                Vector3 d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)), n = new Vector3(-d.z, 0f, d.x);
                Vector3 p = new Vector3(w.x, y, w.y);
                Vector3 bend = n * (Mathf.Sin(_clock * 0.8f + m * 1.3f) * 0.2f * len);
                if (!_under.Fits(5, 9)) return;
                int i = _under.vc;
                var v = _under.verts; var c = _under.cols; var t = _under.tris;
                v[i] = p - n * (wid * 0.5f); v[i + 1] = p + n * (wid * 0.5f);
                v[i + 2] = p + d * (len * 0.55f) + n * wid + bend * 0.5f; v[i + 3] = p + d * (len * 0.55f) - n * wid + bend * 0.5f;
                v[i + 4] = p + d * len + bend;
                c[i] = a; c[i + 1] = a; c[i + 2] = b; c[i + 3] = b; c[i + 4] = b;
                int k = _under.tc;
                t[k++] = i; t[k++] = i + 1; t[k++] = i + 2;
                t[k++] = i; t[k++] = i + 2; t[k++] = i + 3;
                t[k++] = i + 3; t[k++] = i + 2; t[k++] = i + 4;
                _under.tc = k;
                _under.vc += 5;
            }
        }

        void DrawFlyingFish()
        {
            Color body = new Color(0.45f, 0.62f, 0.85f), wing = new Color(0.8f, 0.9f, 1f);
            Vector2 right2 = new Vector2(_ffDir.y, -_ffDir.x);
            for (int i = 0; i < _ffCount; i++)
            {
                float t = (_ffT - i * 0.09f) / _ffDur;
                if (t <= 0f || t >= 1f) continue;
                Vector2 w = _ffStart + right2 * ((i - _ffCount * 0.5f) * 0.45f) + _ffDir * (t * _ffLen + ((i * 37) % 5) * 0.2f);
                float y = SeaShow.SkipHeight(t, _ffHops, 0.5f + 0.08f * ((i * 13) % 4), out float hopT);
                float pitch = Mathf.Cos(hopT * Mathf.PI) * 0.45f;
                SeaBatch.Basis(_ffDir, pitch, 0f, out Vector3 r, out Vector3 u, out Vector3 f);
                Vector3 p = new Vector3(w.x, y, w.y);
                float len = 0.44f;
                _above.Quad(p + f * (0.5f * len), p + r * (0.17f * len), p - f * (0.5f * len), p - r * (0.17f * len), body, u);
                float flutter = Mathf.Sin(_clock * 30f + i) * 0.15f;
                _above.Tri(p + f * (0.2f * len), p + r * (0.62f * len) - f * (0.2f * len) + u * ((0.12f + flutter) * len), p - f * (0.1f * len), wing, u);
                _above.Tri(p + f * (0.2f * len), p - f * (0.1f * len), p - r * (0.62f * len) - f * (0.2f * len) + u * ((0.12f + flutter) * len), wing, u);
            }
        }

        void DrawBaitGulls()
        {
            Color body = new Color(0.97f, 0.97f, 0.95f), wing = new Color(0.72f, 0.76f, 0.82f), tip = new Color(0.25f, 0.27f, 0.3f);
            for (int i = 0; i < BaitGulls; i++)
            {
                Vector2 w = BaitGullPos(i, out float hgt, out Vector2 fwd2);
                bool diving = i == _gullDiver && _gullDiveT >= 0f;
                float pitch = diving ? -Mathf.Cos(_gullDiveT * Mathf.PI) * 0.9f : 0f;
                SeaBatch.Basis(fwd2, pitch, diving ? 0f : -0.35f, out Vector3 r, out Vector3 u, out Vector3 f);
                Vector3 p = new Vector3(w.x, hgt * _gullFade + 6f * (1f - _gullFade), w.y);
                float s = 0.55f * Mathf.Clamp01(_gullFade * 1.5f);
                float flap = Mathf.Sin(_clock * (diving ? 3f : 5f) + i * 1.9f) * (diving ? 0.1f : 0.32f);
                _above.Quad(p + f * (0.45f * s), p + r * (0.1f * s), p - f * (0.45f * s), p - r * (0.1f * s), body, u);
                Vector3 lt = p - r * (0.95f * s) + u * (flap * s) - f * (0.12f * s), rt = p + r * (0.95f * s) + u * (flap * s) - f * (0.12f * s);
                Vector3 lm = p - r * (0.5f * s) + u * (flap * 0.6f * s) + f * (0.1f * s), rm = p + r * (0.5f * s) + u * (flap * 0.6f * s) + f * (0.1f * s);
                _above.Quad(p + f * (0.2f * s), lm, p - r * (0.5f * s) + u * (flap * 0.6f * s) - f * (0.2f * s), p - f * (0.15f * s), wing, u);
                _above.Quad(p + f * (0.2f * s), p - f * (0.15f * s), p + r * (0.5f * s) + u * (flap * 0.6f * s) - f * (0.2f * s), rm, wing, u);
                _above.Tri(lm, lt, p - r * (0.5f * s) + u * (flap * 0.6f * s) - f * (0.2f * s), tip, u);
                _above.Tri(rm, p + r * (0.5f * s) + u * (flap * 0.6f * s) - f * (0.2f * s), rt, tip, u);
            }
        }
    }
}
