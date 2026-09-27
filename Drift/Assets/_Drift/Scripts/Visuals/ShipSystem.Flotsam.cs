using Drift.Core;
using Drift.Islands;
using UnityEngine;

namespace Drift.Visuals
{
    // Flotsam, seeded per wrapped world cell in Cozy and drawn into the ship mesh: driftwood, barrels, message bottles, palm
    // logs with a coconut (only near islands) and flagged buoys on the bank of a nearby island, plus the crates and
    // cargo Encounters lays on the player's course ("route" pieces that wink in the sun). The player collects every
    // piece but the buoys by running it over (hop, splash, golden sparkle, Encounters.FlotsamCollected); other
    // islands just shove it aside. In Adventure the seeded pieces stay away: there Encounters lays a steady stream of
    // route flotsam along the ring, and collecting it is the only way back up. Rock stacks were left out on
    // purpose: a static rock cannot be kept from clipping through the (ever growing) player island or a drifting
    // AI island without either a collider or making it vanish, and both would read as a bug.
    public partial class ShipSystem
    {
        public enum FlotsamKind { Driftwood, Barrel, Buoy, Bottle, PalmLog, Crate }

        public int flotsamSeed = 59;
        public float flotsamCell = 22f;
        public float flotsamSpawnRadius = 70f;
        public float flotsamRecycleRadius = 84f;
        [Range(0f, 1f)] public float flotsamDensity = 0.55f;
        public int maxFlotsam = 12;
        [Tooltip("Im Abenteuer liegt das Treibgut als Strom auf der Bahn (Encounters) – dafür braucht es mehr Plätze.")]
        [Range(0, 32)] public int adventureMaxFlotsam = 28;
        public float flotsamDrift = 0.05f;
        public int maxBuoys = 3;
        [Tooltip("Die Spielerinsel sammelt Treibgut ein, wenn sie darüberfährt (sonst wird es nur beiseitegeschoben).")]
        public bool collectFlotsam = true;
        [Tooltip("Sekunden, die das eingesammelte Stück hüpft und schrumpft.")]
        public float collectSeconds = 0.6f;
        [Tooltip("Abstand (s) zwischen zwei Glitzern auf Treibgut, das eine Begegnung auf den Kurs gelegt hat.")]
        public float routeGlintInterval = 1.6f;
        [Tooltip("Abenteuer: Treibgut auf dem Kurs wird so viel größer gezeichnet, damit es aus der Fahrt heraus erkennbar ist.")]
        [Range(1f, 2f)] public float adventureRouteScale = 1.35f;
        [Tooltip("Wie hoch Treibgut auf dem Kurs sanft auf und ab schaukelt (Einheiten).")]
        [Range(0f, 0.3f)] public float routeBob = 0.07f;

        public const float FlotsamCollectHeight = -0.62f;

        struct Flotsam
        {
            public bool active;
            public long key;
            public uint seed;
            public FlotsamKind kind;
            public Vector2 pos;
            public float fade, heading;
            // Encounter cargo on the player's course; collect >= 0 = being picked up (0..1).
            public bool route;
            public float collect, glint;
        }

        Flotsam[] _flotsam;
        readonly long[] _flotSpent = new long[32];
        int _flotSpentCount;

        static SeaTemplate _tLog, _tBarrel, _tBuoy, _tBottle, _tPalmLog, _tCrate;

        public int FlotsamSlots => _flotsam != null ? _flotsam.Length : 0;
        public bool FlotsamActive(int i) => _flotsam[i].active;
        public bool FlotsamIsRoute(int i) => _flotsam[i].active && _flotsam[i].route;
        public bool FlotsamCollecting(int i) => _flotsam[i].active && _flotsam[i].collect >= 0f;
        public int FlotsamCollectedTotal { get; private set; }
        public static bool IsCollectible(FlotsamKind k) => k != FlotsamKind.Buoy;

        // Encounter cargo still floating (not yet collected or drifted out of range).
        public int RouteFlotsamLeft
        {
            get
            {
                int n = 0;
                if (_flotsam != null) for (int i = 0; i < _flotsam.Length; i++) if (_flotsam[i].active && _flotsam[i].route && _flotsam[i].collect < 0f) n++;
                return n;
            }
        }
        public FlotsamKind FlotsamKindOf(int i) => _flotsam[i].kind;
        public Vector2 FlotsamPosition(int i) => _flotsam[i].pos;
        // 0..1 how far the piece has faded in (it fades out again towards the edge of the spawn radius).
        public float FlotsamFade(int i) => _flotsam[i].active ? _flotsam[i].fade : 0f;

        public int ActiveFlotsam
        {
            get
            {
                int n = 0;
                if (_flotsam != null) for (int i = 0; i < _flotsam.Length; i++) if (_flotsam[i].active) n++;
                return n;
            }
        }

        public int CountFlotsam(FlotsamKind k)
        {
            int n = 0;
            if (_flotsam != null) for (int i = 0; i < _flotsam.Length; i++) if (_flotsam[i].active && _flotsam[i].kind == k) n++;
            return n;
        }

        public static SeaKind SeaKindOf(FlotsamKind k)
        {
            switch (k)
            {
                case FlotsamKind.Driftwood: return SeaKind.Driftwood;
                case FlotsamKind.Barrel: return SeaKind.Barrel;
                case FlotsamKind.Buoy: return SeaKind.Buoy;
                case FlotsamKind.Bottle: return SeaKind.Bottle;
                case FlotsamKind.Crate: return SeaKind.Barrel;
                default: return SeaKind.PalmLog;
            }
        }

        void EnsureFlotsam()
        {
            maxFlotsam = Mathf.Clamp(maxFlotsam, 0, 32);
            int slots = GameModes.IsAdventure ? Mathf.Clamp(Mathf.Max(maxFlotsam, adventureMaxFlotsam), 0, 32) : maxFlotsam;
            if (_flotsam == null || _flotsam.Length != slots) _flotsam = new Flotsam[slots];
        }

        static void BuildFlotsamTemplates()
        {
            if (_tLog != null) return;
            Color bark = new Color(0.47f, 0.36f, 0.25f), cut = new Color(0.8f, 0.66f, 0.45f);
            var log = new SeaShape();
            var lr = new[]
            {
                new SeaShape.Ring(-0.75f, 0f, 0f), new SeaShape.Ring(-0.75f, 0.12f, 0.12f),
                new SeaShape.Ring(0.7f, 0.09f, 0.09f), new SeaShape.Ring(0.7f, 0f, 0f)
            };
            log.Loft(lr, 5, 0f, Mathf.PI * 2f, (n, z) => Mathf.Abs(n.z) > 0.7f ? cut : n.y > 0.3f ? bark * 1.15f : bark);
            log.Prism(new Vector3(0.1f, 0.05f, 0.2f), 0.035f, 0.02f, 0.3f, 3, bark, bark, false);
            _tLog = log.Build();

            var bar = new SeaShape();
            Color stave = new Color(0.62f, 0.43f, 0.24f), hoop = new Color(0.25f, 0.24f, 0.24f), lid = new Color(0.75f, 0.58f, 0.36f);
            var br = new[]
            {
                new SeaShape.Ring(-0.32f, 0f, 0f), new SeaShape.Ring(-0.32f, 0.2f, 0.2f), new SeaShape.Ring(-0.2f, 0.245f, 0.245f),
                new SeaShape.Ring(0.2f, 0.245f, 0.245f), new SeaShape.Ring(0.32f, 0.2f, 0.2f), new SeaShape.Ring(0.32f, 0f, 0f)
            };
            bar.Loft(br, 5, 0f, Mathf.PI * 2f, (n, z) => Mathf.Abs(n.z) > 0.7f ? lid : Mathf.Abs(z) > 0.22f ? hoop : stave);
            _tBarrel = bar.Build();

            var bu = new SeaShape();
            Color red = new Color(0.86f, 0.22f, 0.18f), white = new Color(0.96f, 0.95f, 0.92f);
            bu.Prism(new Vector3(0f, -0.2f, 0f), 0.24f, 0.3f, 0.32f, 5, red, red, false);
            bu.Prism(new Vector3(0f, 0.12f, 0f), 0.3f, 0.12f, 0.3f, 5, white, white, false);
            bu.Prism(new Vector3(0f, 0.42f, 0f), 0.12f, 0.05f, 0.25f, 5, red, red, true);
            bu.Prism(new Vector3(0f, 0.65f, 0f), 0.025f, 0.02f, 0.7f, 3, new Color(0.3f, 0.3f, 0.32f), Color.black, false);
            bu.SheetTri(new Vector3(0f, 1.34f, 0f), new Vector3(0f, 1.08f, 0f), new Vector3(0f, 1.2f, -0.38f), new Color(1f, 0.8f, 0.2f), Vector3.right);
            _tBuoy = bu.Build();

            var bo = new SeaShape();
            Color glass = new Color(0.35f, 0.72f, 0.5f), cork = new Color(0.78f, 0.6f, 0.38f), paper = new Color(0.97f, 0.93f, 0.8f);
            var bor = new[]
            {
                new SeaShape.Ring(-0.2f, 0f, 0f), new SeaShape.Ring(-0.2f, 0.085f, 0.085f), new SeaShape.Ring(0.06f, 0.085f, 0.085f),
                new SeaShape.Ring(0.14f, 0.035f, 0.035f), new SeaShape.Ring(0.24f, 0.035f, 0.035f), new SeaShape.Ring(0.24f, 0f, 0f)
            };
            bo.Loft(bor, 4, 0f, Mathf.PI * 2f, (n, z) => z > 0.2f ? cork : z > -0.12f && z < 0.02f && n.y > 0f ? paper : glass);
            _tBottle = bo.Build();

            var pl = new SeaShape();
            Color palm = new Color(0.6f, 0.5f, 0.34f), frond = new Color(0.33f, 0.6f, 0.25f), frondDry = new Color(0.62f, 0.62f, 0.3f);
            var pr = new[]
            {
                new SeaShape.Ring(-0.95f, 0f, 0f), new SeaShape.Ring(-0.95f, 0.13f, 0.13f),
                new SeaShape.Ring(0.85f, 0.085f, 0.085f), new SeaShape.Ring(0.85f, 0f, 0f)
            };
            pl.Loft(pr, 5, 0f, Mathf.PI * 2f, (n, z) => Mathf.Abs(n.z) > 0.7f ? palm * 1.3f : n.y > 0.3f ? palm * 1.12f : palm);
            pl.SheetTri(new Vector3(0f, 0.06f, 0.8f), new Vector3(0.5f, 0.03f, 1.35f), new Vector3(0.05f, 0.03f, 1.55f), frond, Vector3.up);
            pl.SheetTri(new Vector3(0f, 0.06f, 0.8f), new Vector3(-0.1f, 0.03f, 1.5f), new Vector3(-0.6f, 0.03f, 1.2f), frondDry, Vector3.up);
            pl.SheetTri(new Vector3(0f, 0.06f, 0.8f), new Vector3(0.75f, 0.03f, 0.95f), new Vector3(0.55f, 0.03f, 1.25f), frond, Vector3.up);
            _tPalmLog = pl.Build();

            var cr = new SeaShape();
            Color plank = new Color(0.72f, 0.54f, 0.32f), slat = new Color(0.46f, 0.32f, 0.19f), rope = new Color(0.86f, 0.78f, 0.56f);
            cr.Box(new Vector3(0f, 0.02f, 0f), new Vector3(0.56f, 0.44f, 0.56f), plank, true);
            cr.Box(new Vector3(0f, 0.2f, 0f), new Vector3(0.6f, 0.07f, 0.6f), slat);
            cr.Box(new Vector3(0f, -0.13f, 0f), new Vector3(0.6f, 0.07f, 0.6f), slat);
            cr.Box(new Vector3(0f, 0.25f, 0f), new Vector3(0.08f, 0.02f, 0.62f), rope);
            _tCrate = cr.Build();
        }

        bool FlotOccupied(long key)
        {
            for (int i = 0; i < _flotsam.Length; i++) if (_flotsam[i].active && _flotsam[i].key == key) return true;
            return false;
        }

        bool FlotSpent(long key)
        {
            for (int i = 0; i < _flotSpentCount; i++) if (_flotSpent[i] == key) return true;
            return false;
        }

        void FlotMarkSpent(long key)
        {
            if (_flotSpentCount < _flotSpent.Length) { _flotSpent[_flotSpentCount++] = key; return; }
            for (int i = 1; i < _flotSpent.Length; i++) _flotSpent[i - 1] = _flotSpent[i];
            _flotSpent[_flotSpent.Length - 1] = key;
        }

        public static FlotsamKind FlotsamForRoll(float r)
        {
            if (r < 0.28f) return FlotsamKind.Driftwood;
            if (r < 0.43f) return FlotsamKind.Barrel;
            if (r < 0.6f) return FlotsamKind.Buoy;
            if (r < 0.8f) return FlotsamKind.Bottle;
            return FlotsamKind.PalmLog;
        }

        // What a world cell holds before island proximity is applied; the same on every copy of the torus.
        public bool FlotsamCellContent(int cx, int cy, out FlotsamKind kind, out Vector2 point, out uint h)
        {
            h = SeaMath.CellSeed(flotsamSeed, cx, cy, SeaMath.WrapCells(_period > 0f ? _period : worldPeriod, flotsamCell));
            point = SeaMath.CellPoint(cx, cy, flotsamCell, h);
            kind = FlotsamForRoll(SeaMath.Rand(h, 3));
            return SeaMath.Rand(h, 2) <= flotsamDensity;
        }

        void ScanFlotsam()
        {
            if (_flotsam.Length == 0) return;
            float far = flotsamRecycleRadius + _playerRadius;
            for (int i = 0; i < _flotsam.Length; i++)
            {
                if (!_flotsam[i].active) continue;
                if ((_flotsam[i].pos - _playerPos).sqrMagnitude > far * far)
                {
                    FlotMarkSpent(_flotsam[i].key);
                    _flotsam[i].active = false;
                    _dirty = true;
                }
            }
            float keep = far + flotsamCell;
            for (int i = _flotSpentCount - 1; i >= 0; i--)
            {
                int cx = (int)(_flotSpent[i] >> 32), cy = (int)_flotSpent[i];
                Vector2 c = new Vector2((cx + 0.5f) * flotsamCell, (cy + 0.5f) * flotsamCell);
                if ((c - _playerPos).sqrMagnitude <= keep * keep) continue;
                for (int j = i + 1; j < _flotSpentCount; j++) _flotSpent[j - 1] = _flotSpent[j];
                _flotSpentCount--;
            }

            // The ring lays out its own stream of flotsam along the track (Encounters): no seeded pieces on top of it.
            if (GameModes.IsAdventure) return;

            float near = flotsamSpawnRadius + _playerRadius;
            int x0 = Mathf.FloorToInt((_playerPos.x - near) / flotsamCell), x1 = Mathf.FloorToInt((_playerPos.x + near) / flotsamCell);
            int y0 = Mathf.FloorToInt((_playerPos.y - near) / flotsamCell), y1 = Mathf.FloorToInt((_playerPos.y + near) / flotsamCell);
            for (int cy = y0; cy <= y1; cy++)
                for (int cx = x0; cx <= x1; cx++)
                {
                    if (!FlotsamCellContent(cx, cy, out FlotsamKind kind, out Vector2 sp, out uint h)) continue;
                    if ((sp - _playerPos).sqrMagnitude > near * near) continue;
                    long key = SeaMath.Key(cx, cy);
                    if (FlotOccupied(key) || FlotSpent(key)) continue;
                    if (_obstacles.Inside(sp, 0.5f)) { FlotMarkSpent(key); continue; }
                    Island isl = NearestIsland(sp, 30f);
                    if (kind == FlotsamKind.Buoy)
                    {
                        if (CountFlotsam(FlotsamKind.Buoy) >= maxBuoys) continue;
                        if (isl == null) kind = FlotsamKind.Driftwood;
                        else
                        {
                            Vector2 r = sp - isl.PlanarPosition;
                            r = r.sqrMagnitude > 1e-4f ? r.normalized : Vector2.right;
                            sp = isl.PlanarPosition + r * (isl.BoundingRadius + 1.5f);
                            if (_obstacles.Inside(sp, 0.5f)) { FlotMarkSpent(key); continue; }
                        }
                    }
                    else if (kind == FlotsamKind.PalmLog && isl == null) kind = FlotsamKind.Bottle;
                    int slot = -1;
                    for (int i = 0; i < _flotsam.Length; i++) if (!_flotsam[i].active) { slot = i; break; }
                    if (slot < 0) return;
                    _flotsam[slot] = new Flotsam
                    {
                        active = true, key = key, seed = h, kind = kind, pos = sp, fade = 0f,
                        heading = SeaMath.Rand(h, 4) * Mathf.PI * 2f, collect = -1f
                    };
                    _dirty = true;
                }
        }

        // Places a piece at a spot, replacing the farthest one if the pool is full (verification).
        public int SpawnFlotsamAt(FlotsamKind kind, Vector2 pos) => PlaceFlotsam(kind, pos, false);

        // Encounter cargo on the player's course: fades in, winks in the sun until collected. -1 when the spot is on land.
        public int SpawnRouteFlotsam(FlotsamKind kind, Vector2 pos)
        {
            EnsureArrays();
            if (_obstacles.Shallow(pos, FlotsamCollectHeight, out _)) return -1;
            return PlaceFlotsam(kind, pos, true);
        }

        int PlaceFlotsam(FlotsamKind kind, Vector2 pos, bool route)
        {
            EnsureArrays();
            if (_flotsam.Length == 0) return -1;
            int slot = -1;
            float far = -1f;
            for (int i = 0; i < _flotsam.Length; i++)
            {
                if (!_flotsam[i].active) { slot = i; break; }
                if (_flotsam[i].route && !route) continue;
                float d = (_flotsam[i].pos - _playerPos).sqrMagnitude;
                if (d > far) { far = d; slot = i; }
            }
            if (slot < 0) return -1;
            uint h = SeaMath.Hash((uint)flotsamSeed, (uint)(pos.x * 13f + pos.y * 7f) + (uint)kind);
            _flotsam[slot] = new Flotsam
            {
                active = true, key = long.MinValue + 100 + slot, seed = h, kind = kind, pos = pos, fade = route ? 0f : 1f,
                heading = SeaMath.Rand(h, 4) * 6.28f, route = route, collect = -1f, glint = route ? 0.4f + SeaMath.Rand(h, 5) : 0f
            };
            _dirty = true;
            return slot;
        }

        // The player picks a piece up: it hops out of the water and shrinks away with a splash and a golden sparkle.
        public bool CollectFlotsam(int i)
        {
            if (_flotsam == null || i < 0 || i >= _flotsam.Length) return false;
            ref Flotsam fl = ref _flotsam[i];
            if (!fl.active || fl.collect >= 0f || !IsCollectible(fl.kind)) return false;
            fl.collect = 0f;
            FlotsamCollectedTotal++;
            _dirty = true;
            Vector3 at = new Vector3(fl.pos.x, 0.05f, fl.pos.y);
            if (seaLife != null && seaLife.isActiveAndEnabled)
            {
                seaLife.SprayAt(fl.pos, 0.35f, 5);
                seaLife.Sparkle(at, fl.kind == FlotsamKind.Bottle ? 1.2f : 1f, fl.kind == FlotsamKind.Bottle ? 12 : 9);
            }
            else if (water != null) water.Splash(fl.pos, 0.35f);
            Encounters.NotifyCollected(fl.kind, at, fl.seed);
            return true;
        }

        bool StepFlotsam(float dt)
        {
            bool any = false;
            for (int i = 0; i < _flotsam.Length; i++)
            {
                if (!_flotsam[i].active) continue;
                any = true;
                ref Flotsam fl = ref _flotsam[i];
                if (fl.collect >= 0f)
                {
                    if (dt <= 0f) continue;
                    fl.collect += dt / Mathf.Max(0.05f, collectSeconds);
                    fl.pos += _playerVel * dt;
                    fl.heading += 5f * dt;
                    if (fl.collect >= 1f)
                    {
                        if (!fl.route) FlotMarkSpent(fl.key);
                        fl.active = false;
                        _dirty = true;
                    }
                    continue;
                }
                float edge = (fl.pos - _playerPos).magnitude - _playerRadius;
                float target = SeaMath.EdgeFade(edge, flotsamSpawnRadius - 12f, flotsamSpawnRadius);
                fl.fade = dt > 0f ? Mathf.MoveTowards(fl.fade, target, dt * 0.9f) : target;
                if (dt <= 0f) continue;
                if (fl.kind != FlotsamKind.Buoy) fl.pos += _wind * (flotsamDrift * dt);
                // Past a rim of the ring the bend would fold the piece flat onto the edge.
                if (_onRing) fl.pos = _ringGeo.ClampAcross(fl.pos, 1.5f);
                fl.heading += Mathf.Sin(_clock * 0.17f + i) * 0.05f * dt;
                if (fl.route)
                {
                    fl.glint -= dt;
                    if (fl.glint <= 0f)
                    {
                        fl.glint = routeGlintInterval * (0.8f + 0.4f * SeaMath.Rand(fl.seed, (int)(_clock * 3f)));
                        if (seaLife != null && seaLife.isActiveAndEnabled && fl.fade > 0.5f)
                            seaLife.Sparkle(new Vector3(fl.pos.x, 0.12f, fl.pos.y), 0.45f, 2);
                    }
                }
                if (_obstacles.Shallow(fl.pos, FlotsamCollectHeight, out Island over))
                {
                    if (over == _player && collectFlotsam && IsCollectible(fl.kind) && CollectFlotsam(i)) continue;
                    Vector2 away = fl.pos - over.PlanarPosition;
                    float l = away.magnitude;
                    away = l > 1e-3f ? away / l : Vector2.right;
                    fl.pos += away * ((2.5f + over.PlanarVelocity.magnitude + (over == _player ? _playerSpeed : 0f)) * dt);
                    if (_onRing) fl.pos = _ringGeo.ClampAcross(fl.pos, 1.5f);
                    fl.heading += 0.8f * dt;
                }
            }
            return any;
        }

        void DrawFlotsam()
        {
            for (int i = 0; i < _flotsam.Length; i++)
            {
                if (!_flotsam[i].active || _flotsam[i].fade <= 0.01f) continue;
                ref Flotsam fl = ref _flotsam[i];
                if (fl.kind == FlotsamKind.Bottle && !fl.route && LifeLod.Distance(new Vector3(fl.pos.x, 0f, fl.pos.y)) > detailDistance) continue;
                Vector2 d = new Vector2(Mathf.Cos(fl.heading), Mathf.Sin(fl.heading));
                float h0 = SeaMath.Swell(fl.pos, _clock), h1 = SeaMath.Swell(fl.pos + d * 0.6f, _clock);
                float ph = (fl.seed & 0xFFu) * 0.0245f;
                float k = fl.fade;
                Vector3 p = new Vector3(fl.pos.x, h0 * 0.06f, fl.pos.y);
                if (fl.route)
                {
                    // Cargo on the course bobs a little more than the swell alone, and on the ring it is drawn larger.
                    p.y += routeBob * Mathf.Sin(_clock * 2.1f + ph * 7f);
                    if (_onRing) k *= adventureRouteScale;
                }
                if (fl.collect >= 0f)
                {
                    float c = Mathf.Clamp01(fl.collect);
                    p.y += 0.9f * Mathf.Sin(c * Mathf.PI * 0.85f);
                    k *= 1f - Mathf.SmoothStep(0f, 1f, (c - 0.35f) / 0.65f);
                    if (k <= 0.01f) continue;
                }
                float pitch = (h1 - h0) * 0.25f, roll = Mathf.Sin(_clock * 0.9f + ph) * 0.12f;
                SeaTemplate t;
                switch (fl.kind)
                {
                    case FlotsamKind.Barrel: t = _tBarrel; p.y += 0.06f; roll += _clock * 0.15f; break;
                    case FlotsamKind.Buoy: t = _tBuoy; pitch = Mathf.Sin(_clock * 1.1f + ph) * 0.14f; roll = Mathf.Cos(_clock * 0.8f + ph) * 0.14f; break;
                    case FlotsamKind.Bottle: t = _tBottle; pitch += 0.55f; p.y += 0.02f + 0.03f * Mathf.Sin(_clock * 1.6f + ph); break;
                    case FlotsamKind.PalmLog: t = _tPalmLog; p.y += 0.02f; break;
                    case FlotsamKind.Crate: t = _tCrate; p.y += 0.04f; roll *= 0.6f; break;
                    default: t = _tLog; p.y += 0.02f; break;
                }
                SeaBatch.Basis(d, pitch, roll, out Vector3 r, out Vector3 u, out Vector3 f);
                _batch.Add(t, p, r, u, f, new Vector3(k, k, k), Color.white);
                if (fl.kind == FlotsamKind.PalmLog)
                    _batch.Octa(p + r * (0.55f * k) + f * (0.3f * k) + Vector3.up * (0.03f + 0.03f * Mathf.Sin(_clock * 1.3f + ph)), 0.13f * k, new Color(0.4f, 0.27f, 0.15f));
            }
            DrawBeacons();
        }
    }
}
