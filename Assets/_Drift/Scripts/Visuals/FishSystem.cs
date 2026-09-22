using Drift.Core;
using Drift.Islands;
using UnityEngine;
using UnityEngine.Rendering;

namespace Drift.Visuals
{
    // Schools of small fish in the shallows around islands. Schools are seeded from a world grid
    // (wrapped like the island chunks) so the same school reappears at the same spot; per-fish
    // motion is a pure function of (school seed, index, clock) so nothing per fish is stored.
    // All fish are one dynamic mesh under a "FishSchools" child, rebuilt at rebuildRate.
    // Three species (small silver sardines, gold reef fish, a few large blue mackerel) and, rarely in open
    // water, a bait ball: a big tight swirling sardine school other systems can look up (TryGetBaitBall).
    [ExecuteAlways]
    [DefaultExecutionOrder(210)]
    public class FishSystem : MonoBehaviour
    {
        public WaterFeedback water;
        public int maxSchools = 10;
        public int minFish = 8;
        public int maxFish = 20;
        public float spawnRadius = 45f;
        public float recycleRadius = 58f;
        public float cellSize = 12f;
        public float worldPeriod = 660f;
        [Range(0f, 1f)] public float density = 0.7f;
        [Range(0f, 1f)] public float openWaterChance = 0.55f;
        public int maxSchoolsPerIsland = 3;
        public float islandSearchRadius = 20f;
        public float swimDepth = -0.15f;
        public float fishLength = 0.28f;
        public float schoolSpeed = 0.9f;
        public float shoreHeight = -0.5f;
        public float shoreBand = 0.12f;
        public float turnRate = 2.5f;
        public float scatterRadius = 3f;
        public float scatterDuration = 3f;
        public float scatterSpeed = 3f;
        public float playerMoveThreshold = 0.4f;
        public float jumpIntervalMin = 6f;
        public float jumpIntervalMax = 16f;
        public float jumpDuration = 0.75f;
        public float jumpHeight = 0.45f;
        public float rebuildRate = 15f;
        public int seed = 7;
        public Color silverColor = new Color(0.78f, 0.86f, 0.94f);
        public Color goldColor = new Color(1f, 0.68f, 0.32f);
        [Range(0f, 1f)] public float goldChance = 0.25f;
        public Color blueColor = new Color(0.36f, 0.56f, 0.78f);
        [Range(0f, 1f)] public float bigFishChance = 0.2f;
        public float bigFishScale = 1.9f;
        public int bigFishMin = 3;
        public int bigFishMax = 7;
        public int maxBaitBalls = 1;
        public int baitBallFish = 44;
        [Range(0f, 1f)] public float baitBallChance = 0.3f;
        public float baitBallRadius = 1.7f;
        public float baitBallSpin = 1.3f;

        [Header("Abenteuer-Beute")]
        [Tooltip("So viele Fischschwärme dürfen gleichzeitig neben der Insel herschwimmen.")]
        [Range(0, 3)] public int maxEscortSchools = 1;
        [Tooltip("So viele Schwärme warten höchstens gleichzeitig auf der Bahn.")]
        [Range(0, 3)] public int maxPickupSchools = 1;
        [Tooltip("So lange (s) begleitet ein eingesammelter Schwarm die Insel.")]
        [Range(5f, 60f)] public float escortSeconds = 22f;
        [Tooltip("Abstand (u) zwischen Inselrand und begleitendem Schwarm.")]
        [Range(1f, 12f)] public float escortGap = 2.5f;
        [Tooltip("Zusätzliche Reichweite (u) um den Inselrand, in der ein wartender Schwarm eingesammelt wird.")]
        [Range(0f, 8f)] public float pickupGrab = 2.5f;

        const string ObjName = "FishSchools";
        const int VertsPerFish = 7;
        const int IndicesPerFish = 9;

        struct School
        {
            public bool active;
            public long key;
            public uint seed;
            public Vector2 pos, dir;
            public int count;
            public Island island;
            public float orbitSign;
            public float scatter;
            public Vector2 scatterFrom;
            public float jumpTimer, jumpT;
            public int jumper;
            public Color color;
            public float retarget;
            public FishSpecies species;
            public float size;
            public bool ball;
            // Adventure: waiting on the track to be run over, then escorting the island for `escort` seconds.
            public bool pickup;
            public float escort, side;
        }

        public enum FishSpecies { Sardine, Gold, Mackerel }

        School[] _schools;
        readonly long[] _spent = new long[16];
        int _spentCount;
        Vector3[] _verts;
        Color[] _cols;
        int[] _tris;
        int _vc, _tc;
        Mesh _mesh;
        GameObject _go;
        Material _mat;
        float _clock, _rebuildTimer, _spawnTimer;
        int _editTick;
        bool _dirty;
        Vector2 _playerPos, _playerVel, _playerFwd = Vector2.up;
        float _playerRadius, _playerSpeed;

        public float Clock => _clock;
        public int VertexCount => _vc;
        public int ActiveSchools
        {
            get
            {
                int n = 0;
                if (_schools != null) for (int i = 0; i < _schools.Length; i++) if (_schools[i].active) n++;
                return n;
            }
        }
        public int SchoolSlots => _schools != null ? _schools.Length : 0;
        public bool SchoolActive(int i) => _schools[i].active;
        public Vector2 SchoolPosition(int i) => _schools[i].pos;
        public FishSpecies SchoolSpecies(int i) => _schools[i].species;
        public int SchoolFishCount(int i) => _schools[i].active ? _schools[i].count : 0;
        public bool IsBaitBall(int i) => _schools[i].active && _schools[i].ball;
        const float PickupKeepRadius = 130f;

        public bool SchoolIsPickup(int i) => _schools != null && _schools[i].active && _schools[i].pickup;
        public bool SchoolIsEscort(int i) => _schools != null && _schools[i].active && _schools[i].escort > 0f;
        public int PickupsCollected { get; private set; }

        public int PickupSchoolCount
        {
            get
            {
                int n = 0;
                if (_schools != null) for (int i = 0; i < _schools.Length; i++) if (_schools[i].active && _schools[i].pickup) n++;
                return n;
            }
        }

        public int EscortCount
        {
            get
            {
                int n = 0;
                if (_schools != null) for (int i = 0; i < _schools.Length; i++) if (_schools[i].active && _schools[i].escort > 0f) n++;
                return n;
            }
        }

        public int BaitBallCount
        {
            get
            {
                int n = 0;
                if (_schools != null) for (int i = 0; i < _schools.Length; i++) if (_schools[i].active && _schools[i].ball) n++;
                return n;
            }
        }

        // The n-th active bait ball (seabirds, fishing boats and dolphins gather over it).
        public bool TryGetBaitBall(int n, out Vector2 pos, out float radius)
        {
            if (_schools != null)
                for (int i = 0; i < _schools.Length; i++)
                {
                    if (!_schools[i].active || !_schools[i].ball) continue;
                    if (n-- > 0) continue;
                    pos = _schools[i].pos;
                    radius = baitBallRadius;
                    return true;
                }
            pos = default;
            radius = 0f;
            return false;
        }

        public int FishCount
        {
            get
            {
                int n = 0;
                if (_schools != null) for (int i = 0; i < _schools.Length; i++) if (_schools[i].active) n += _schools[i].count;
                return n;
            }
        }

        void OnEnable()
        {
            EnsureArrays();
            Resolve();
            _dirty = true;
            Step(0f);
        }

        void OnDisable()
        {
            DestroyChild();
            if (_mesh != null) DestroyImmediate(_mesh);
            if (_mat != null) DestroyImmediate(_mat);
            _mesh = null;
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
        }

        void EnsureArrays()
        {
            maxSchools = Mathf.Clamp(maxSchools, 1, 16);
            maxFish = Mathf.Max(maxFish, 1);
            minFish = Mathf.Clamp(minFish, 1, maxFish);
            if (_schools == null || _schools.Length != maxSchools) _schools = new School[maxSchools];
            maxBaitBalls = Mathf.Clamp(maxBaitBalls, 0, 2);
            baitBallFish = Mathf.Clamp(baitBallFish, 0, 80);
            int fish = maxSchools * maxFish + maxBaitBalls * baitBallFish;
            if (_verts == null || _verts.Length < fish * VertsPerFish)
            {
                _verts = new Vector3[fish * VertsPerFish];
                _cols = new Color[fish * VertsPerFish];
                _tris = new int[fish * IndicesPerFish];
            }
        }

        void DestroyChild()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i);
                if (c.name == ObjName) DestroyImmediate(c.gameObject);
            }
            _go = null;
        }

        void EnsureObject()
        {
            if (_go != null) return;
            for (int i = 0; i < transform.childCount; i++)
            {
                var c = transform.GetChild(i);
                if (c.name == ObjName) { _go = c.gameObject; break; }
            }
            if (_go == null)
            {
                _go = new GameObject(ObjName);
                _go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                _go.transform.SetParent(transform, false);
                _go.AddComponent<MeshFilter>();
                var mr = _go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            if (_mat == null)
            {
                var shader = Shader.Find("Drift/Fish");
                if (shader == null) Debug.LogError("FishSystem: shader 'Drift/Fish' not found.");
                _mat = new Material(shader) { name = "Fish", hideFlags = HideFlags.HideAndDontSave };
            }
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "FishSchools", hideFlags = HideFlags.DontSave };
                _mesh.MarkDynamic();
            }
            _go.GetComponent<MeshRenderer>().sharedMaterial = _mat;
            _go.GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        // ---- hashing ----

        static uint Hash(uint a, uint b)
        {
            uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u) * 0x85EBCA77u;
            h ^= h >> 15; h *= 0xC2B2AE3Du;
            h ^= h >> 13; h *= 0x27D4EB2Fu;
            h ^= h >> 16;
            return h;
        }

        static float Rand(uint h, int k) => (Hash(h, (uint)k) & 0xFFFFFFu) / 16777216f;

        static int Mod(int a, int n) => ((a % n) + n) % n;

        static long Key(int cx, int cy) => ((long)cx << 32) | (uint)cy;

        uint CellSeed(int cx, int cy)
        {
            int wrap = Mathf.Max(1, Mathf.RoundToInt(worldPeriod / Mathf.Max(0.1f, cellSize)));
            return Hash(Hash((uint)seed, (uint)Mod(cx, wrap)), (uint)Mod(cy, wrap));
        }

        Vector2 SpawnPoint(int cx, int cy, uint h)
        {
            return new Vector2((cx + 0.2f + 0.6f * Rand(h, 0)) * cellSize, (cy + 0.2f + 0.6f * Rand(h, 1)) * cellSize);
        }

        // ---- islands / player ----

        Island FindPlayer()
        {
            if (water != null) return water.FindPlayer();
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var isl = all[i];
                if (isl != null && isl.useKeyboardInput && isl.isActiveAndEnabled) return isl;
            }
            return null;
        }

        int SchoolsOn(Island isl, int except)
        {
            int n = 0;
            for (int i = 0; i < _schools.Length; i++)
                if (i != except && _schools[i].active && _schools[i].island == isl) n++;
            return n;
        }

        Island NearestIsland(Vector2 pos, float radius, bool enforceLimit, int self)
        {
            var all = Island.All;
            Island best = null;
            float bestD = radius;
            for (int i = 0; i < all.Count; i++)
            {
                var isl = all[i];
                if (isl == null || !isl.isActiveAndEnabled || isl.IsSunk || isl.IsEmerging) continue;
                float d = (isl.PlanarPosition - pos).magnitude - isl.BoundingRadius;
                if (d >= bestD) continue;
                if (enforceLimit && SchoolsOn(isl, self) >= maxSchoolsPerIsland) continue;
                best = isl;
                bestD = d;
            }
            return best;
        }

        // ---- spawning / recycling ----

        bool IsOccupied(long key)
        {
            for (int i = 0; i < _schools.Length; i++) if (_schools[i].active && _schools[i].key == key) return true;
            return false;
        }

        bool IsSpent(long key)
        {
            for (int i = 0; i < _spentCount; i++) if (_spent[i] == key) return true;
            return false;
        }

        void MarkSpent(long key)
        {
            if (_spentCount < _spent.Length) _spent[_spentCount++] = key;
            else
            {
                for (int i = 1; i < _spent.Length; i++) _spent[i - 1] = _spent[i];
                _spent[_spent.Length - 1] = key;
            }
        }

        void CleanSpent()
        {
            float far2 = recycleRadius * recycleRadius;
            for (int i = _spentCount - 1; i >= 0; i--)
            {
                int cx = (int)(_spent[i] >> 32), cy = (int)_spent[i];
                Vector2 c = new Vector2((cx + 0.5f) * cellSize, (cy + 0.5f) * cellSize);
                if ((c - _playerPos).sqrMagnitude <= far2) continue;
                for (int j = i + 1; j < _spentCount; j++) _spent[j - 1] = _spent[j];
                _spentCount--;
            }
        }

        void Scan()
        {
            float far2 = recycleRadius * recycleRadius;
            for (int i = 0; i < _schools.Length; i++)
            {
                if (!_schools[i].active) continue;
                // A shoal laid on the track waits further out than the seeded ones, and an escort is glued to the
                // island: neither may be recycled at the ordinary range.
                float keep = _schools[i].pickup || _schools[i].escort > 0f ? PickupKeepRadius * PickupKeepRadius : far2;
                if ((_schools[i].pos - _playerPos).sqrMagnitude > keep)
                {
                    MarkSpent(_schools[i].key);
                    _schools[i].active = false;
                    _schools[i].island = null;
                    _schools[i].pickup = false;
                    _schools[i].escort = 0f;
                    _dirty = true;
                }
            }
            CleanSpent();

            float near2 = spawnRadius * spawnRadius;
            int x0 = Mathf.FloorToInt((_playerPos.x - spawnRadius) / cellSize);
            int x1 = Mathf.FloorToInt((_playerPos.x + spawnRadius) / cellSize);
            int y0 = Mathf.FloorToInt((_playerPos.y - spawnRadius) / cellSize);
            int y1 = Mathf.FloorToInt((_playerPos.y + spawnRadius) / cellSize);
            for (int cy = y0; cy <= y1; cy++)
                for (int cx = x0; cx <= x1; cx++)
                {
                    uint h = CellSeed(cx, cy);
                    if (Rand(h, 2) > density) continue;
                    Vector2 sp = SpawnPoint(cx, cy, h);
                    if ((sp - _playerPos).sqrMagnitude > near2) continue;
                    long key = Key(cx, cy);
                    if (IsOccupied(key) || IsSpent(key)) continue;
                    int slot = -1;
                    for (int i = 0; i < _schools.Length; i++) if (!_schools[i].active) { slot = i; break; }
                    if (slot < 0) return;
                    if (!Activate(slot, key, sp, h)) MarkSpent(key);
                }
        }

        bool Activate(int slot, long key, Vector2 sp, uint h)
        {
            var isl = NearestIsland(sp, islandSearchRadius, true, slot);
            if (isl == null && Rand(h, 3) > openWaterChance) return false;

            ref School s = ref _schools[slot];
            s.active = true;
            s.key = key;
            s.seed = h;
            s.island = isl;
            s.orbitSign = Rand(h, 4) < 0.5f ? -1f : 1f;
            s.count = minFish + (int)(Rand(h, 5) * (maxFish - minFish + 0.999f));
            s.ball = false;
            s.ball = isl == null && maxBaitBalls > 0 && baitBallFish > 0 && Rand(h, 10) < baitBallChance && BaitBallCount < maxBaitBalls;
            s.species = FishSpecies.Sardine;
            s.size = 1f;
            s.scatter = 0f;
            s.jumpT = -1f;
            s.jumpTimer = Mathf.Lerp(jumpIntervalMin, jumpIntervalMax, Rand(h, 6));
            s.retarget = 1f;
            float pick = Rand(h, 7);
            bool gold = !s.ball && pick < goldChance;
            bool big = !s.ball && !gold && pick < goldChance + bigFishChance;
            if (s.ball) s.count = baitBallFish;
            else if (gold) s.species = FishSpecies.Gold;
            else if (big)
            {
                s.species = FishSpecies.Mackerel;
                s.size = bigFishScale;
                s.count = Mathf.Min(maxFish, bigFishMin + (int)(Rand(h, 5) * (bigFishMax - bigFishMin + 0.999f)));
            }
            else s.size = 0.8f;
            Color c = gold ? goldColor : big ? blueColor : s.ball ? new Color(silverColor.r * 0.72f, silverColor.g * 0.82f, silverColor.b, 1f) : silverColor;
            float v = 0.9f + 0.2f * Rand(h, 8);
            s.color = new Color(c.r * v, c.g * v, c.b * (gold ? v : 1f), 1f);

            if (isl != null)
            {
                Vector2 r = sp - isl.PlanarPosition;
                if (r.sqrMagnitude < 0.01f) r = Vector2.right;
                r.Normalize();
                s.pos = isl.PlanarPosition + r * (isl.BoundingRadius + 1f);
                s.dir = new Vector2(-r.y, r.x) * s.orbitSign;
            }
            else
            {
                s.pos = sp;
                float a = Rand(h, 9) * Mathf.PI * 2f;
                s.dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }
            _dirty = true;
            return true;
        }

        // Puts a bait ball at a spot, replacing the farthest school if the pool is full (debug / verification).
        public int SpawnBaitBall(Vector2 pos)
        {
            EnsureArrays();
            if (maxBaitBalls <= 0 || baitBallFish <= 0) return -1;
            int slot = -1;
            float far = -1f;
            for (int i = 0; i < _schools.Length; i++)
            {
                if (_schools[i].active && _schools[i].ball) { slot = i; break; }
                if (!_schools[i].active) { slot = i; break; }
                float d = (_schools[i].pos - _playerPos).sqrMagnitude;
                if (d > far) { far = d; slot = i; }
            }
            ref School s = ref _schools[slot];
            s = default;
            s.active = true;
            s.key = long.MinValue + slot;
            s.seed = Hash((uint)seed, (uint)(pos.x * 13f + pos.y * 7f));
            s.pos = pos;
            s.dir = Vector2.right;
            s.orbitSign = 1f;
            s.ball = true;
            s.count = baitBallFish;
            s.size = 0.8f;
            s.species = FishSpecies.Sardine;
            s.jumpT = -1f;
            s.jumpTimer = jumpIntervalMin;
            s.retarget = 1f;
            s.color = new Color(silverColor.r, silverColor.g, silverColor.b, 1f);
            _dirty = true;
            return slot;
        }

        // ---- adventure: a shoal to pick up on the track (Encounters lays it out) ----

        // A shoal waiting on the band: it drifts slowly, never scatters, and joins the island when run over.
        // -1 when there is no room for another one.
        public int SpawnPickupShoal(Vector2 pos, Vector2 dir)
        {
            EnsureArrays();
            if (maxPickupSchools <= 0 || PickupSchoolCount >= maxPickupSchools) return -1;
            int slot = -1;
            float far = -1f;
            for (int i = 0; i < _schools.Length; i++)
            {
                if (!_schools[i].active) { slot = i; break; }
                if (_schools[i].pickup || _schools[i].escort > 0f) continue;
                float d = (_schools[i].pos - _playerPos).sqrMagnitude;
                if (d > far) { far = d; slot = i; }
            }
            if (slot < 0) return -1;
            uint h = Hash((uint)seed + 909u, (uint)(pos.x * 13f + pos.y * 7f));
            ref School s = ref _schools[slot];
            s = default;
            s.active = true;
            s.key = long.MinValue + 200 + slot;
            s.seed = h;
            s.pos = pos;
            s.dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.up;
            s.orbitSign = Rand(h, 4) < 0.5f ? -1f : 1f;
            s.count = Mathf.Clamp(maxFish, minFish, maxFish);
            s.species = FishSpecies.Gold;
            s.size = 1.8f;
            s.pickup = true;
            s.jumpT = -1f;
            s.jumpTimer = jumpIntervalMin * 0.5f;
            s.retarget = 1e6f;
            s.color = goldColor;
            _dirty = true;
            return slot;
        }

        // Adventure only: every waiting shoal the island has reached starts escorting it. Returns how many joined.
        public int CollectPickups() => CollectPickupsAt(_playerPos, _playerRadius);

        public int CollectPickupsAt(Vector2 pos, float radius)
        {
            if (_schools == null) return 0;
            int joined = 0;
            for (int i = 0; i < _schools.Length; i++)
            {
                ref School s = ref _schools[i];
                if (!s.active || !s.pickup) continue;
                if (EscortCount >= maxEscortSchools) break;
                float reach = radius + pickupGrab + 1.5f;
                if ((s.pos - pos).sqrMagnitude > reach * reach) continue;
                s.pickup = false;
                s.escort = Mathf.Max(2f, escortSeconds);
                s.side = EscortSideTaken(1f) ? -1f : 1f;
                s.scatter = 0f;
                PickupsCollected++;
                joined++;
                if (water != null) water.Splash(s.pos, 0.6f);
                Encounters.NotifyCompanion(CompanionKind.FishShoal, new Vector3(s.pos.x, 0.05f, s.pos.y));
                _dirty = true;
            }
            return joined;
        }

        bool EscortSideTaken(float side)
        {
            for (int i = 0; i < _schools.Length; i++)
                if (_schools[i].active && _schools[i].escort > 0f && _schools[i].side == side) return true;
            return false;
        }

        // Starts a jump in the given school right away (debug / verification); returns false if inactive.
        public bool TriggerJump(int school)
        {
            if (_schools == null || school < 0 || school >= _schools.Length || !_schools[school].active) return false;
            ref School s = ref _schools[school];
            s.jumpTimer = 0f;
            s.jumpT = -1f;
            return true;
        }

        // ---- per-fish motion ----

        // x = across the school heading, y = along it. Purely a function of seed/index/time.
        static Vector2 FishLocal(uint schoolSeed, int i, int count, float clock, float scatter, out float phase, out float size)
        {
            uint h = Hash(schoolSeed, (uint)i + 101u);
            float a = Rand(h, 0) * Mathf.PI * 2f;
            float rr = Mathf.Sqrt(Rand(h, 1));
            float radius = 0.55f + count * 0.055f;
            phase = Rand(h, 2) * Mathf.PI * 2f;
            size = 0.85f + 0.3f * Rand(h, 3);
            Vector2 o = new Vector2(Mathf.Cos(a) * rr * radius * 0.8f, Mathf.Sin(a) * rr * radius * 1.4f);
            o.x += Mathf.Sin(clock * 1.1f + phase) * 0.12f;
            o.y += Mathf.Sin(clock * 0.7f + phase * 1.7f) * 0.18f;
            return o * (1f + scatter * 2.2f);
        }

        Vector2 FishWorld(ref School s, int i, out Vector2 forward, out float phase, out float size)
        {
            if (s.ball)
            {
                uint h = Hash(s.seed, (uint)i + 101u);
                phase = Rand(h, 2) * Mathf.PI * 2f;
                size = 0.75f + 0.25f * Rand(h, 3);
                float rr = baitBallRadius * (0.35f + 0.65f * Mathf.Sqrt(Rand(h, 1))) * (1f + s.scatter * 1.6f);
                rr *= 1f + 0.12f * Mathf.Sin(_clock * 0.9f + phase);
                float a = Rand(h, 0) * Mathf.PI * 2f + _clock * baitBallSpin * s.orbitSign * (0.75f + 0.5f * Rand(h, 4));
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                forward = new Vector2(-sa, ca) * s.orbitSign;
                return s.pos + new Vector2(ca, sa) * rr;
            }
            Vector2 o = FishLocal(s.seed, i, s.count, _clock, s.scatter, out phase, out size);
            Vector2 right = new Vector2(s.dir.y, -s.dir.x);
            float wig = Mathf.Sin(_clock * 1.1f + phase) * 0.22f;
            float cs = Mathf.Cos(wig), sn = Mathf.Sin(wig);
            forward = new Vector2(s.dir.x * cs - s.dir.y * sn, s.dir.x * sn + s.dir.y * cs);
            return s.pos + right * o.x + s.dir * o.y;
        }

        // ---- stepping ----

        public void Step(float dt)
        {
            EnsureArrays();
            if (water == null) Resolve();
            _clock += dt;

            var p = FindPlayer();
            if (p != null && !p.IsSunk)
            {
                _playerPos = p.PlanarPosition;
                _playerRadius = p.BoundingRadius;
                _playerVel = Application.isPlaying ? p.PlanarVelocity : Vector2.zero;
                _playerSpeed = _playerVel.magnitude;
                _playerFwd = _playerSpeed > 0.05f ? _playerVel / _playerSpeed : new Vector2(p.BodyForward.x, p.BodyForward.z);
                if (_playerFwd.sqrMagnitude < 1e-6f) _playerFwd = Vector2.up;
            }
            else
            {
                _playerRadius = 0f;
                _playerSpeed = 0f;
                _playerVel = Vector2.zero;
            }

            _spawnTimer -= dt;
            if (_spawnTimer <= 0f || dt <= 0f)
            {
                Scan();
                _spawnTimer = 0.5f;
            }

            if (dt > 0f)
                for (int i = 0; i < _schools.Length; i++)
                    if (_schools[i].active) StepSchool(i, dt);
            if (GameModes.IsAdventure && _playerRadius > 0f) CollectPickups();

            _rebuildTimer += dt;
            float interval = rebuildRate > 0f ? 1f / rebuildRate : 0f;
            if (_dirty || _rebuildTimer >= interval)
            {
                _rebuildTimer = 0f;
                _dirty = false;
                Rebuild();
            }
        }

        void StepSchool(int idx, float dt)
        {
            ref School s = ref _schools[idx];
            bool escortish = s.pickup || s.escort > 0f;

            s.retarget -= dt;
            if (s.retarget <= 0f)
            {
                s.island = s.ball ? null : NearestIsland(s.pos, islandSearchRadius, false, idx);
                s.retarget = 1f;
            }

            Vector2 dp = s.pos - _playerPos;
            float pr = _playerRadius + scatterRadius;
            if (!escortish && s.scatter <= 0f && _playerSpeed > playerMoveThreshold && dp.sqrMagnitude < pr * pr)
            {
                s.scatter = 1f;
                s.scatterFrom = _playerPos;
            }

            Vector2 desired;
            float speed;
            // Adventure: a shoal waiting on the track must not flee the island (it could never be collected), and
            // an escorting one holds its lane beside the bow until its time is up.
            if (escortish)
            {
                if (s.escort > 0f)
                {
                    s.escort -= dt;
                    if (s.escort <= 0f || _playerRadius <= 0f) { s.escort = 0f; s.island = null; }
                }
                if (s.escort > 0f)
                {
                    Vector2 right = new Vector2(_playerFwd.y, -_playerFwd.x);
                    float lane = _playerRadius + escortGap;
                    Vector2 goal = _playerPos + right * (s.side * lane)
                                 + _playerFwd * (lane * 0.55f + Mathf.Sin(_clock * 0.5f + s.seed % 7u) * 2f);
                    Vector2 to = goal - s.pos;
                    float d = to.magnitude;
                    desired = d > 0.3f ? to / d : _playerFwd;
                    speed = Mathf.Min(d / Mathf.Max(0.016f, dt), _playerSpeed + 4f);
                }
                else
                {
                    desired = s.dir;
                    speed = schoolSpeed * (s.pickup ? 0.5f : 1f);
                }
            }
            else if (s.scatter > 0f)
            {
                s.scatter = Mathf.Max(0f, s.scatter - dt / Mathf.Max(0.1f, scatterDuration));
                Vector2 away = s.pos - s.scatterFrom;
                desired = away.sqrMagnitude > 1e-4f ? away.normalized : s.dir;
                speed = schoolSpeed + scatterSpeed * s.scatter;
            }
            else if (s.island != null)
            {
                Vector2 c = s.island.PlanarPosition;
                Vector2 r = s.pos - c;
                float rl = r.magnitude;
                if (rl < 0.5f) { r = Vector2.right; rl = 1f; }
                r /= rl;
                float h = s.island.SampleHeight(s.island.ToLocal(s.pos));
                float radial = Mathf.Clamp((h - shoreHeight) / Mathf.Max(0.01f, shoreBand), -1f, 1f);
                if (rl > s.island.BoundingRadius + 4f) radial = -1f;
                Vector2 tang = new Vector2(-r.y, r.x) * s.orbitSign;
                desired = (tang + r * (radial * 1.2f)).normalized;
                speed = schoolSpeed;
            }
            else
            {
                float a = dt * 0.25f * s.orbitSign;
                float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                desired = new Vector2(s.dir.x * cs - s.dir.y * sn, s.dir.x * sn + s.dir.y * cs);
                speed = schoolSpeed * (s.ball ? 0.25f : 0.7f);
            }

            Vector2 nd = Vector2.Lerp(s.dir, desired, 1f - Mathf.Exp(-turnRate * dt));
            s.dir = nd.sqrMagnitude > 1e-6f ? nd.normalized : desired;
            s.pos += s.dir * (speed * dt);

            if (s.jumpT >= 0f)
            {
                s.jumpT += dt / Mathf.Max(0.05f, jumpDuration);
                if (s.jumpT >= 1f)
                {
                    s.jumpT = -1f;
                    Vector2 w = FishWorld(ref s, s.jumper, out _, out _, out _);
                    if (water != null) water.Splash(w, 1f);
                }
            }
            else
            {
                s.jumpTimer -= dt;
                if (s.jumpTimer <= 0f)
                {
                    s.jumpT = 0f;
                    s.jumper = (int)(Rand(s.seed, 500 + Mathf.FloorToInt(_clock)) * s.count) % s.count;
                    s.jumpTimer = Mathf.Lerp(jumpIntervalMin, jumpIntervalMax, Rand(s.seed, 700 + Mathf.FloorToInt(_clock)));
                    // A shoal lying on the track keeps jumping: that flicker is what you steer towards.
                    if (s.pickup) s.jumpTimer = 1.2f + 1.2f * Rand(s.seed, 900 + Mathf.FloorToInt(_clock));
                    Vector2 w = FishWorld(ref s, s.jumper, out _, out _, out _);
                    if (water != null) water.Splash(w, 0.5f);
                }
            }
        }

        // ---- mesh ----

        void Rebuild()
        {
            EnsureObject();
            _vc = 0;
            _tc = 0;
            for (int si = 0; si < _schools.Length; si++)
            {
                if (!_schools[si].active) continue;
                ref School s = ref _schools[si];
                Color body = s.color;
                Color tail = new Color(body.r * 0.75f, body.g * 0.75f, body.b * 0.8f, 1f);
                Color nose = new Color(Mathf.Min(1f, body.r * 1.1f), Mathf.Min(1f, body.g * 1.1f), Mathf.Min(1f, body.b * 1.1f), 1f);
                for (int i = 0; i < s.count; i++)
                {
                    Vector2 w = FishWorld(ref s, i, out Vector2 f2, out float phase, out float size);
                    float len = fishLength * size * s.size;
                    float y = swimDepth * (s.size > 1.2f ? 2.2f : 1f);
                    float pitch = 0f;
                    if (s.jumpT >= 0f && i == s.jumper)
                    {
                        float jt = s.jumpT;
                        y += Mathf.Sin(jt * Mathf.PI) * jumpHeight;
                        w += f2 * (jt * 0.6f);
                        pitch = Mathf.Cos(jt * Mathf.PI) * 0.9f;
                    }
                    float cp = Mathf.Cos(pitch), sp = Mathf.Sin(pitch);
                    Vector3 fwd = new Vector3(f2.x * cp, sp, f2.y * cp);
                    Vector3 right = new Vector3(f2.y, 0f, -f2.x);
                    Vector3 pos = new Vector3(w.x, y, w.y);
                    float wag = Mathf.Sin(_clock * 9f + phase * 3f) * 0.22f * (1f + s.scatter);

                    int b = _vc;
                    _verts[b + 0] = pos + fwd * (0.5f * len);
                    _verts[b + 1] = pos - right * (0.17f * len) + fwd * (0.05f * len);
                    _verts[b + 2] = pos + right * (0.17f * len) + fwd * (0.05f * len);
                    _verts[b + 3] = pos - fwd * (0.3f * len) + right * (wag * 0.3f * len);
                    _verts[b + 4] = pos - fwd * (0.25f * len) + right * (wag * 0.25f * len);
                    _verts[b + 5] = pos - fwd * (0.5f * len) + right * ((wag * 0.6f - 0.15f) * len);
                    _verts[b + 6] = pos - fwd * (0.5f * len) + right * ((wag * 0.6f + 0.15f) * len);
                    _cols[b + 0] = nose;
                    _cols[b + 1] = body;
                    _cols[b + 2] = body;
                    _cols[b + 3] = tail;
                    _cols[b + 4] = tail;
                    _cols[b + 5] = tail;
                    _cols[b + 6] = tail;
                    int t = _tc;
                    _tris[t + 0] = b + 0; _tris[t + 1] = b + 1; _tris[t + 2] = b + 3;
                    _tris[t + 3] = b + 0; _tris[t + 4] = b + 3; _tris[t + 5] = b + 2;
                    _tris[t + 6] = b + 4; _tris[t + 7] = b + 5; _tris[t + 8] = b + 6;
                    _vc += VertsPerFish;
                    _tc += IndicesPerFish;
                }
            }

            _mesh.Clear(false);
            _mesh.SetVertices(_verts, 0, _vc);
            _mesh.SetColors(_cols, 0, _vc);
            _mesh.SetTriangles(_tris, 0, _tc, 0, false);
            float ext = recycleRadius * 2f + 10f;
            _mesh.bounds = new Bounds(new Vector3(_playerPos.x, 0f, _playerPos.y), new Vector3(ext, 4f, ext));
        }
    }
}
