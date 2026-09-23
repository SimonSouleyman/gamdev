using Drift.Core;
using Drift.Islands;
using UnityEngine;

namespace Drift.Visuals
{
    // What plays near the camera. Cozy mode only (GameModes.IsAdventure off) paces dolphins and rays faster while
    // they are in the picture, sends flying fish across it, lets turtles visit the player's coast and brings
    // visitors (a seal or a turtle) to the coast the camera looks at when none is there. Seals are their own small
    // pool (not a Kind: the journal and the watch tools know no seal yet): they pop their heads up along the
    // coast, look round at the island, dive and come up elsewhere, and haul out onto a flat beach strip while the
    // island rests. TryShowNear starts one of these (or a dolphin pass, a ray leap, a whale blow, a fish jump)
    // inside a circle right away; the cozy LifeDirector calls it when the view has been quiet for a while.
    public partial class SeaLifeSystem
    {
        [Header("Gemütlich: Leben vor der Kamera")]
        [Tooltip("So viel schneller (x) zählen Delfine und Rochen im Bild bis zum nächsten Sprung (nur Gemütlich).")]
        [Range(1f, 4f)] public float cozyInViewPace = 2.2f;
        [Tooltip("Faktor auf die Pause zwischen zwei Walsprüngen (nur Gemütlich): kleiner = öfter.")]
        [Range(0.2f, 1f)] public float cozyBreachScale = 0.6f;
        [Tooltip("Reichweite (u) ab Inselrand, aus der Delfine zur Insel kommen (nur Gemütlich; sonst gilt bowRideRange).")]
        [Range(20f, 90f)] public float cozyDolphinRange = 60f;
        [Tooltip("Pause (s) zwischen zwei Flugfisch-Schwärmen, die quer durchs Bild springen (nur Gemütlich).")]
        public Vector2 cozyFlyingFishInterval = new Vector2(8f, 16f);
        [Tooltip("Anteil der Meeresschildkröten, die die ruhende Insel des Spielers statt einer fremden besuchen (nur Gemütlich).")]
        [Range(0f, 1f)] public float cozyTurtleToPlayer = 0.35f;
        [Tooltip("Pause (s), nach der ein Besucher (Robbe oder Schildkröte) an die Küste im Bild kommt, wenn gerade keiner da ist (nur Gemütlich).")]
        public Vector2 cozyVisitInterval = new Vector2(20f, 35f);
        [Tooltip("Über diesem Tempo (u/s) der eigenen Insel kommen keine Besucher an die Küste.")]
        [Range(0.5f, 6f)] public float visitMaxSpeed = 2f;

        [Header("Robben")]
        [Tooltip("So viele Robben dürfen gleichzeitig an einer Küste sein.")]
        [Range(0, 2)] public int maxSeals = 1;
        [Tooltip("Länge (u) einer Robbe.")]
        [Range(0.6f, 2.5f)] public float sealLength = 1.3f;
        [Tooltip("So lange (s) bleibt eine Robbe an der Küste, bevor sie weiterzieht.")]
        public Vector2 sealStay = new Vector2(60f, 100f);
        [Tooltip("So lange (s) schaut eine Robbe mit dem Kopf über Wasser umher.")]
        public Vector2 sealLookSeconds = new Vector2(3.5f, 5.5f);
        [Tooltip("So lange (s) taucht sie mindestens zwischen zwei Auftauchern.")]
        public Vector2 sealDiveSeconds = new Vector2(3f, 6f);
        [Tooltip("So lange (s) liegt sie am Strand, wenn sie an Land robbt.")]
        public Vector2 sealRestSeconds = new Vector2(14f, 26f);
        [Tooltip("Chance, dass eine Robbe nach dem Abtauchen an einen flachen Strand robbt (nur wenn die Insel ruht).")]
        [Range(0f, 1f)] public float sealHaulOutChance = 0.4f;
        [Tooltip("Ab diesem Inseltempo (u/s) verlässt eine Robbe die Küste; vom Strand rutscht sie schon ab 0,6 u/s ins Wasser.")]
        [Range(1f, 8f)] public float sealKeepUp = 3.5f;
        [Tooltip("Grundfarbe der Robbe (Rücken dunkler, Bauch heller).")]
        public Color sealColor = new Color(0.5f, 0.48f, 0.45f);

        [Header("Schildkröten an der Küste")]
        [Tooltip("So lange (s) paddelt eine besuchende Schildkröte an der eigenen Küste entlang.")]
        public Vector2 showTurtleStay = new Vector2(45f, 75f);
        [Tooltip("Atemtakt (1/s) einer besuchenden Schildkröte: größer = sie taucht öfter auf.")]
        [Range(0.1f, 1.5f)] public float showTurtleBreathRate = 0.45f;
        [Tooltip("Wassertiefe (u unter dem Spiegel), in der Schildkröten der Küste des Spielers folgen.")]
        [Range(-2f, -0.2f)] public float turtleCoastDepth = -0.7f;

        [Header("Rochensprünge")]
        [Tooltip("Pause (s) zwischen zwei Sprüngen eines Rochens.")]
        public Vector2 rayLeapInterval = new Vector2(25f, 50f);
        [Tooltip("Sprunghöhe (u) eines Rochens.")]
        [Range(0.3f, 3f)] public float rayLeapHeight = 1.2f;
        [Tooltip("Dauer (s) eines Rochensprungs.")]
        [Range(0.6f, 3f)] public float rayLeapSeconds = 1.5f;

        [Header("Auftritte auf Abruf (TryShowNear)")]
        [Tooltip("Pause (s), bevor dieselbe Art wieder auf Abruf auftritt: Robbe, Delfine, Flugfische, Schildkröte, Rochen, Wal, Fische.")]
        public float[] showCooldowns = { 30f, 35f, 10f, 35f, 20f, 60f, 8f };

        const int ShowFlag = 256;
        const int SeenFlag = 512;

        const int SealUnder = 0, SealLook = 1, SealBeach = 2, SealSlide = 3, SealClimb = 4, SealLeave = 5;
        const int MaxSeals = 2;
        const float SealSwim = 2.4f;
        const float SealClimbSeconds = 1.4f;
        const float SealSlideSeconds = 1f;
        const float SealWaterDepth = -0.45f;

        struct Seal
        {
            public bool active, climb;
            public int state, pops;
            public Island host;
            // Host body space: where it swims (local, goal), the beach strip and the inland direction there.
            public Vector2 local, goal, beach, beachDir;
            public Vector2 pos, dir;
            public float t, dur, stay, fade, phase;
            public uint seed;
        }

        readonly Seal[] _seals = new Seal[MaxSeals];
        readonly float[] _profile = new float[48];
        readonly float[] _showCd = new float[SeaShow.KindCount];
        float _visitTimer = 12f;
        uint _showSeed = 91;
        Camera _cam;
        bool _hasView;
        Vector2 _viewCenter, _viewFwd = Vector2.up;
        float _viewRadius = 20f;

        static SeaTemplate _tSeal, _tSealLow, _tSealHead;

        static bool Cozy => !GameModes.IsAdventure;

        // The part of the sea the camera shows, as a circle round the middle of its footprint on the water.
        public Vector2 ViewCenter => _viewCenter;
        public float ViewRadius => _viewRadius;

        public int SealCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < MaxSeals; i++) if (_seals[i].active) n++;
                return n;
            }
        }

        public int SealSlots => MaxSeals;
        public bool SealActive(int i) => _seals[i].active;
        public Vector2 SealPosition(int i) => _seals[i].pos;
        // 0 under water, 1 head up, 2 lying on the beach, 3 sliding in, 4 hauling out, 5 leaving.
        public int SealStateOf(int i) => _seals[i].active ? _seals[i].state : -1;
        public int SealPops(int i) => _seals[i].pops;
        public bool SealOnBeach => AnySeal(SealBeach);

        bool AnySeal(int state)
        {
            for (int i = 0; i < MaxSeals; i++) if (_seals[i].active && _seals[i].state == state) return true;
            return false;
        }

        public int ShowTurtleCount
        {
            get
            {
                int n = 0;
                if (_groups != null)
                    for (int i = 0; i < _groups.Length; i++)
                        if (_groups[i].active && _groups[i].kind == Kind.Turtle && (_groups[i].flags & ShowFlag) != 0) n++;
                return n;
            }
        }

        public float ShowCooldownLeft(SeaShowKind k) => Mathf.Max(0f, _showCd[(int)k]);

        // ---- view ----

        void UpdateView()
        {
            if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
            _hasView = _cam != null;
            if (!_hasView)
            {
                _viewCenter = _playerPos;
                _viewFwd = Vector2.up;
                _viewRadius = _playerRadius + 15f;
                return;
            }
            Transform t = _cam.transform;
            Vector3 o = t.position;
            Vector2 near = SeaShow.WaterHit(o, _cam.ViewportPointToRay(new Vector3(0.5f, 0f, 0f)).direction, 120f);
            Vector2 far = SeaShow.WaterHit(o, _cam.ViewportPointToRay(new Vector3(0.5f, 1f, 0f)).direction, 120f);
            Vector3 fw = t.forward;
            Vector2 f = new Vector2(fw.x, fw.z);
            if (f.sqrMagnitude < 1e-4f) f = new Vector2(t.up.x, t.up.z);
            _viewFwd = f.sqrMagnitude > 1e-8f ? f.normalized : Vector2.up;
            _viewCenter = (near + far) * 0.5f;
            float halfLen = (far - near).magnitude * 0.5f;
            float halfWide = SeaShow.HalfWidthAt(Vector3.Distance(o, new Vector3(far.x, 0f, far.y)), _cam.fieldOfView, _cam.aspect);
            _viewRadius = Mathf.Max(4f, Mathf.Max(halfLen, halfWide));
        }

        // A point on the water inside the picture (margin in viewport units; negative = a little outside counts).
        bool InView(Vector2 p, float margin)
        {
            if (!_hasView || _cam == null) return (p - _viewCenter).sqrMagnitude < _viewRadius * _viewRadius;
            Vector3 v = _cam.WorldToViewportPoint(new Vector3(p.x, 0f, p.y));
            return v.z > 0f && v.x > margin && v.x < 1f - margin && v.y > margin && v.y < 1f - margin;
        }

        float ViewHalfWidth(Vector2 p)
        {
            if (!_hasView || _cam == null) return 6f;
            float d = Vector3.Distance(_cam.transform.position, new Vector3(p.x, 0f, p.y));
            return SeaShow.HalfWidthAt(d, _cam.fieldOfView, _cam.aspect);
        }

        // How fast a countdown to the next jump runs: cozy, in the picture it runs faster.
        float Pace(Vector2 p) => Cozy && cozyInViewPace > 1f && InView(p, -0.05f) ? cozyInViewPace : 1f;

        bool OpenWater(Vector2 p, float depth) => !_obstacles.Shallow(p, depth, out _);

        // Along a direction out from an island's centre: the first water deeper than `depth` past its shore (body
        // space) and, when the last land before it is a flat strip just above the waterline, that beach spot.
        bool CoastSpot(Island isl, Vector2 worldDir, float depth, out Vector2 waterLocal, out Vector2 beachLocal, out bool hasBeach)
        {
            waterLocal = beachLocal = default;
            hasBeach = false;
            float l = worldDir.magnitude;
            if (isl == null || l < 1e-5f) return false;
            worldDir /= l;
            Vector2 c = isl.PlanarPosition;
            int n = _profile.Length;
            float step = (isl.BoundingRadius + 4f) / (n - 1);
            for (int i = 0; i < n; i++) _profile[i] = isl.SampleHeight(isl.ToLocal(c + worldDir * (i * step)));
            if (!SeaShow.FindCoast(_profile, n, 0.02f, 0.16f, depth, out int beach, out int water)) return false;
            Vector2 wp = c + worldDir * (water * step + 0.35f);
            if (_obstacles.Shallow(wp, depth + 0.1f, out _)) return false;
            waterLocal = isl.ToLocal(wp);
            if (beach >= 0)
            {
                hasBeach = true;
                beachLocal = isl.ToLocal(c + worldDir * (beach * step));
            }
            return true;
        }

        // A coast inside the circle (and the picture) with water deeper than `depth` off it: the island and the
        // direction from its centre. `only` limits the search to one island.
        bool CoastIn(Vector2 center, float radius, uint h, float depth, Island only, out Island isl, out Vector2 dir)
        {
            for (int i = 0; i < _obstacles.count; i++)
            {
                var c = _obstacles.islands[i];
                if (c == null || c.IsEmerging || c.IsSunk || (only != null && c != only)) continue;
                Vector2 to = center - c.PlanarPosition;
                float d = to.magnitude;
                if (d > radius + c.BoundingRadius) continue;
                Vector2 baseDir = d > 0.5f ? to / d : _viewFwd;
                float spread = d > c.BoundingRadius ? 0.6f : 1.1f;
                for (int a = 0; a < 4; a++)
                {
                    Vector2 dd = SeaShow.ArcDirection(baseDir, spread, SeaMath.Rand(h, 60 + a + i * 4));
                    if (!CoastSpot(c, dd, depth, out Vector2 wl, out _, out _)) continue;
                    Vector2 wp = c.ToWorld(wl);
                    if ((wp - center).sqrMagnitude > radius * radius) continue;
                    if (_hasView && !InView(wp, 0.06f)) continue;
                    isl = c;
                    dir = dd;
                    return true;
                }
            }
            isl = null;
            dir = default;
            return false;
        }

        // ---- stepping ----

        void StepShow(float dt)
        {
            for (int i = 0; i < _showCd.Length; i++) if (_showCd[i] > 0f) _showCd[i] -= dt;
            StepSeals(dt);
            if (!Cozy || _player == null) return;
            _visitTimer -= dt;
            if (_visitTimer > 0f) return;
            _showSeed = SeaMath.Hash(_showSeed, (uint)(_clock * 17f) + 5u);
            _visitTimer = Mathf.Lerp(cozyVisitInterval.x, Mathf.Max(cozyVisitInterval.x, cozyVisitInterval.y), SeaMath.Rand(_showSeed, 0));
            if (_playerSpeed > visitMaxSpeed || _storm > 0.6f || SealCount > 0 || ShowTurtleCount > 0) return;
            Vector2 d = SeaShow.ArcDirection(_viewFwd, 1f, SeaMath.Rand(_showSeed, 1));
            bool seal = SeaMath.Rand(_showSeed, 2) < 0.6f;
            bool started = seal
                ? StartSeal(_player, d, false) || StartShowTurtle(_player, d, 2f)
                : StartShowTurtle(_player, d, 2f) || StartSeal(_player, d, false);
            if (!started) _visitTimer = 4f;
        }

        void StepRayLeap(ref Group g, float dt)
        {
            if (g.jump >= 0f)
            {
                float before = g.jump;
                g.jump += dt / Mathf.Max(0.3f, rayLeapSeconds);
                if (before == 0f)
                {
                    Splash(g.pos, 0.5f, 5);
                    if (Moments.Listening) Moments.Report(MomentKind.RayLeap, new Vector3(g.pos.x, rayLeapHeight * 0.5f, g.pos.y));
                }
                if (before < 0.88f && g.jump >= 0.88f) Splash(g.pos + g.dir * 0.4f, 0.8f, 10);
                if (g.jump >= 1f)
                    g.jump = -Mathf.Lerp(rayLeapInterval.x, Mathf.Max(rayLeapInterval.x, rayLeapInterval.y), SeaMath.Rand(g.seed, 120 + (int)_clock));
                return;
            }
            g.jump = Mathf.Min(0f, g.jump + dt * Pace(g.pos));
            if (g.jump >= 0f && (_storm > 0.5f || g.fade < 0.6f || _obstacles.Shallow(g.pos, -0.8f, out _))) g.jump = -4f;
        }

        // ---- seals ----

        bool StartSeal(Island host, Vector2 worldDir, bool popNow)
        {
            if (host == null || maxSeals <= 0 || SealCount >= Mathf.Min(maxSeals, MaxSeals)) return false;
            int slot = -1;
            for (int i = 0; i < MaxSeals; i++) if (!_seals[i].active) { slot = i; break; }
            if (slot < 0) return false;
            if (!CoastSpot(host, worldDir, SealWaterDepth, out Vector2 wl, out _, out _)) return false;
            _showSeed = SeaMath.Hash(_showSeed, (uint)(_clock * 29f) + 11u);
            ref Seal s = ref _seals[slot];
            s = default;
            s.active = true;
            s.host = host;
            s.seed = _showSeed;
            s.phase = SeaMath.Rand(s.seed, 1) * 6.283f;
            s.stay = Mathf.Lerp(sealStay.x, Mathf.Max(sealStay.x, sealStay.y), SeaMath.Rand(s.seed, 2));
            s.goal = wl;
            // A visitor arrives from a little further out, under water; on request it simply pops up.
            s.local = popNow ? wl : wl * ((wl.magnitude + 3f) / Mathf.Max(0.5f, wl.magnitude));
            s.pos = host.ToWorld(s.local);
            Vector2 r = s.pos - host.PlanarPosition;
            s.dir = r.sqrMagnitude > 1e-4f ? new Vector2(-r.y, r.x).normalized : Vector2.right;
            if (popNow)
            {
                s.fade = 1f;
                BeginLook(ref s);
            }
            else
            {
                s.state = SealUnder;
                s.dur = 1.5f;
                s.fade = 0f;
            }
            _dirty = true;
            return true;
        }

        void StepSeals(float dt)
        {
            for (int i = 0; i < MaxSeals; i++)
            {
                if (!_seals[i].active) continue;
                ref Seal s = ref _seals[i];
                if (s.state != SealLeave && (s.host == null || !s.host.isActiveAndEnabled || s.host.IsSunk)) Leave(ref s);
                s.t += dt;
                s.stay -= dt;
                if (s.state != SealLeave) s.fade = Mathf.Min(1f, s.fade + dt * 1.5f);
                float hostSpeed = s.host != null ? s.host.PlanarVelocity.magnitude : 0f;
                switch (s.state)
                {
                    case SealUnder:
                    {
                        Vector2 before = s.host.ToWorld(s.local);
                        s.local = SeaShow.PolarStep(s.local, s.goal, SealSwim * dt);
                        s.pos = s.host.ToWorld(s.local);
                        Vector2 mv = s.pos - before;
                        if (mv.sqrMagnitude > 1e-8f) s.dir = Vector2.Lerp(s.dir, mv.normalized, 1f - Mathf.Exp(-6f * dt)).normalized;
                        if (hostSpeed > sealKeepUp) { Leave(ref s); break; }
                        if (s.t < s.dur || (s.local - s.goal).sqrMagnitude > 0.04f) break;
                        if (s.climb) BeginClimb(ref s);
                        else BeginLook(ref s);
                        break;
                    }
                    case SealLook:
                        s.pos = s.host.ToWorld(s.local);
                        if (hostSpeed > sealKeepUp)
                        {
                            Splash(s.pos, 0.3f, 2);
                            Leave(ref s);
                            break;
                        }
                        if (s.t >= s.dur)
                        {
                            Splash(s.pos, 0.25f, 2);
                            NextDive(ref s, hostSpeed);
                        }
                        break;
                    case SealClimb:
                        s.pos = s.host.ToWorld(Vector2.Lerp(s.local, s.beach, Mathf.Clamp01(s.t / SealClimbSeconds)));
                        if (hostSpeed > 0.6f) { BeginSlide(ref s, 1f - Mathf.Clamp01(s.t / SealClimbSeconds)); break; }
                        if (s.t >= SealClimbSeconds)
                        {
                            s.state = SealBeach;
                            s.t = 0f;
                            s.dur = Mathf.Lerp(sealRestSeconds.x, Mathf.Max(sealRestSeconds.x, sealRestSeconds.y), SeaMath.Rand(s.seed, 30 + s.pops));
                            if (Moments.Listening)
                            {
                                Vector2 bl = s.beach;
                                Moments.Report(MomentKind.SealHaulOut, s.host.transform, bl, s.host.SampleHeight(bl) + 0.2f);
                            }
                        }
                        break;
                    case SealBeach:
                    {
                        s.pos = s.host.ToWorld(s.beach);
                        float bh = s.host.SampleHeight(s.beach);
                        if (hostSpeed > 0.6f || s.t >= s.dur || bh < -0.05f || bh > 0.3f || s.host.IsUplifting) BeginSlide(ref s, 0f);
                        break;
                    }
                    case SealSlide:
                    {
                        float k = Mathf.Clamp01(s.t / SealSlideSeconds);
                        s.pos = s.host.ToWorld(Vector2.Lerp(s.beach, s.local, k));
                        if (s.t >= SealSlideSeconds)
                        {
                            Splash(s.pos, 0.6f, 6);
                            s.climb = false;
                            NextDive(ref s, hostSpeed);
                        }
                        break;
                    }
                    default:
                        s.pos += s.dir * (SealSwim * dt);
                        s.fade -= dt * 0.4f;
                        if (s.fade <= 0f)
                        {
                            s.active = false;
                            s.host = null;
                        }
                        break;
                }
            }
        }

        void BeginLook(ref Seal s)
        {
            s.state = SealLook;
            s.t = 0f;
            s.pops++;
            s.dur = Mathf.Lerp(sealLookSeconds.x, Mathf.Max(sealLookSeconds.x, sealLookSeconds.y), SeaMath.Rand(s.seed, 10 + s.pops));
            s.pos = s.host.ToWorld(s.local);
            Splash(s.pos, 0.3f, 3);
            if (Moments.Listening) Moments.Report(MomentKind.SealPopUp, new Vector3(s.pos.x, 0.25f, s.pos.y));
            _dirty = true;
        }

        void BeginClimb(ref Seal s)
        {
            s.state = SealClimb;
            s.t = 0f;
            Splash(s.pos, 0.35f, 3);
        }

        // k: how far along the way back into the water it already is (a climb broken off halfway).
        void BeginSlide(ref Seal s, float k)
        {
            s.state = SealSlide;
            s.t = Mathf.Clamp01(k) * SealSlideSeconds;
            s.climb = false;
        }

        void NextDive(ref Seal s, float hostSpeed)
        {
            s.state = SealUnder;
            s.t = 0f;
            s.dur = Mathf.Lerp(sealDiveSeconds.x, Mathf.Max(sealDiveSeconds.x, sealDiveSeconds.y), SeaMath.Rand(s.seed, 20 + s.pops));
            s.climb = false;
            if (s.stay <= 0f)
            {
                Leave(ref s);
                return;
            }
            Vector2 c = s.host.PlanarPosition;
            Vector2 d0 = s.pos - c;
            d0 = d0.sqrMagnitude > 1e-4f ? d0.normalized : _viewFwd;
            float u = SeaMath.Rand(s.seed, 40 + s.pops);
            // Round the player's island it comes up again on the side the camera looks at, never far from the last spot.
            Vector2 want = s.host == _player && _hasView ? SeaShow.ArcDirection(_viewFwd, 1f, u) : SeaShow.ArcDirection(d0, 0.8f, u);
            float turn = Mathf.Clamp(Vector2.SignedAngle(d0, want) * Mathf.Deg2Rad, -1.2f, 1.2f);
            Vector2 d = Rotate(d0, turn);
            bool rest = s.pops >= 2 && hostSpeed < 0.3f && SeaMath.Rand(s.seed, 50 + s.pops) < sealHaulOutChance;
            if (CoastSpot(s.host, d, SealWaterDepth, out Vector2 wl, out Vector2 bl, out bool hasBeach))
            {
                s.goal = wl;
                if (rest && hasBeach && (bl - wl).sqrMagnitude > 0.04f)
                {
                    s.climb = true;
                    s.beach = bl;
                    s.beachDir = (bl - wl).normalized;
                }
            }
            else s.goal = s.local;
        }

        void Leave(ref Seal s)
        {
            if (s.host != null)
            {
                Vector2 away = s.pos - s.host.PlanarPosition;
                if (away.sqrMagnitude > 1e-4f) s.dir = away.normalized;
            }
            s.state = SealLeave;
            s.t = 0f;
            s.host = null;
        }

        // ---- turtles visiting the player's coast ----

        bool StartShowTurtle(Island host, Vector2 worldDir, float surfaceIn)
        {
            if (host == null || host != _player || _groups == null) return false;
            if (!CoastSpot(host, worldDir, turtleCoastDepth, out Vector2 wl, out _, out _)) return false;
            Vector2 p = host.ToWorld(wl);
            Vector2 r = p - host.PlanarPosition;
            r = r.sqrMagnitude > 1e-4f ? r.normalized : Vector2.up;
            int slot = SpawnAt(Kind.Turtle, p, new Vector2(-r.y, r.x));
            ref Group g = ref _groups[slot];
            g.flags |= ShowFlag;
            g.target = host;
            g.state = 1;
            g.timer = 1e6f;
            g.breach = Mathf.Lerp(showTurtleStay.x, Mathf.Max(showTurtleStay.x, showTurtleStay.y), SeaMath.Rand(g.seed, 13));
            g.phase = SeaShow.TurtleSurfacePhase(_clock, showTurtleBreathRate, Mathf.Max(0.2f, surfaceIn));
            g.wander = 0f;
            g.fade = 0.25f;
            _dirty = true;
            return true;
        }

        // ---- on request ----

        // Starts something visible in the sea inside the circle (what the camera looks at) right now: the roll picks
        // where in the rotation Seal, DolphinPass, FlyingFish, Turtle, RayLeap, WhaleBlow, FishJump to begin; kinds
        // on cooldown or without a fitting spot are skipped. Only water spots, never on land. False when nothing fits.
        public bool TryShowNear(Vector2 center, float radius, int roll) => TryShowNear(center, radius, roll, 0f);

        // minSize (u): only shows whose subject is at least that big (SeaShow.SubjectSize). The LifeDirector passes
        // what a subject needs to reach its pixel threshold at the view's distance, so a far camera over a huge
        // island gets a whale blow, a dolphin pass or flying fish instead of a seal it could not see.
        public bool TryShowNear(Vector2 center, float radius, int roll, float minSize)
        {
            EnsureArrays();
            if (_groups == null) return false;
            radius = Mathf.Max(2f, radius);
            int mask = SeaShow.ReadyMask(_showCd);
            uint h = SeaMath.Hash((uint)roll * 2654435761u ^ (uint)seed, (uint)(_clock * 31f) + 17u);
            for (int i = 0; i < SeaShow.KindCount; i++)
            {
                var k = SeaShow.Order(roll, i);
                if (!SeaShow.Has(mask, k) || SeaShow.SubjectSize(k) < minSize || !TryShow(k, center, radius, h, minSize >= 1f)) continue;
                _showCd[(int)k] = showCooldowns != null && (int)k < showCooldowns.Length ? showCooldowns[(int)k] : 20f;
                LastShow = k;
                _dirty = true;
                return true;
            }
            return false;
        }

        public SeaShowKind LastShow { get; private set; }

        bool TryShow(SeaShowKind k, Vector2 center, float radius, uint h, bool far = false)
        {
            switch (k)
            {
                case SeaShowKind.Seal: return ShowSeal(center, radius, h);
                case SeaShowKind.DolphinPass: return ShowDolphins(center, radius, h);
                case SeaShowKind.FlyingFish: return _night < 0.5f && _storm < 0.45f && FlyingFishAcross(center, radius, h);
                case SeaShowKind.Turtle: return _player != null && _playerSpeed < 1.2f && CoastIn(center, radius, h, turtleCoastDepth, _player, out _, out Vector2 d) && StartShowTurtle(_player, d, 1.2f);
                case SeaShowKind.RayLeap: return ShowRayLeap(center, radius, h);
                case SeaShowKind.WhaleBlow: return ShowWhale(center, radius, h, far ? 8 : 4);
                default: return fish != null && fish.TriggerJumpNear(center, radius, 2);
            }
        }

        bool ShowSeal(Vector2 center, float radius, uint h)
        {
            // A seal already at that coast comes up in the picture instead of a second one.
            for (int i = 0; i < MaxSeals; i++)
            {
                ref Seal s = ref _seals[i];
                if (!s.active || s.state != SealUnder || s.host == null) continue;
                if (!CoastIn(center, radius, h, SealWaterDepth, s.host, out _, out Vector2 dir)) continue;
                if (!CoastSpot(s.host, dir, SealWaterDepth, out Vector2 wl, out _, out _)) continue;
                if ((s.host.ToWorld(wl) - s.pos).sqrMagnitude > 64f) continue;
                s.goal = wl;
                s.climb = false;
                s.t = s.dur;
                s.stay = Mathf.Max(s.stay, 20f);
                return true;
            }
            return CoastIn(center, radius, h, SealWaterDepth, null, out Island isl, out Vector2 d) && StartSeal(isl, d, true);
        }

        bool FlyingFishAcross(Vector2 center, float radius, uint h)
        {
            if (_ffActive) return false;
            Vector2 right = new Vector2(_viewFwd.y, -_viewFwd.x);
            for (int a = 0; a < 5; a++)
            {
                Vector2 mid = SeaShow.DiscPoint(center, radius * 0.8f, h, 10 + a * 3);
                if (!InView(mid, 0.12f) || !OpenWater(mid, -0.4f)) continue;
                float half = Mathf.Clamp(ViewHalfWidth(mid) + 1.5f, 3f, 9f);
                float sgn = SeaMath.Rand(h, 30 + a) < 0.5f ? 1f : -1f;
                Vector2 dir = Rotate(right * sgn, (SeaMath.Rand(h, 40 + a) - 0.5f) * 0.7f);
                Vector2 start = mid - dir * half;
                if (!OpenWater(start, -0.4f) || !OpenWater(mid + dir * half, -0.4f)
                    || !OpenWater(mid - dir * (half * 0.5f), -0.4f) || !OpenWater(mid + dir * (half * 0.5f), -0.4f)) continue;
                BeginFlyingFish(start, dir, 6 + (int)(SeaMath.Rand(h, 50) * 4.999f), 3, 2f * half, Mathf.Clamp(half / 2.2f, 1.4f, 3.4f));
                return true;
            }
            return false;
        }

        bool ShowDolphins(Vector2 center, float radius, uint h)
        {
            // Under way they join the bow wave instead (Encounters' escort, from behind on one flank).
            if (_player != null && _playerSpeed > 1.2f) return StartDolphinEscort(12f, (h & 1u) == 0u) >= 0;
            Vector2 across = new Vector2(_viewFwd.y, -_viewFwd.x) * ((h & 2u) == 0u ? 1f : -1f);
            for (int a = 0; a < 5; a++)
            {
                Vector2 mid = SeaShow.DiscPoint(center, radius * 0.8f, h, 80 + a * 2);
                if (!InView(mid, 0.1f) || _obstacles.Inside(mid, 3f)) continue;
                float half = Mathf.Clamp(ViewHalfWidth(mid) + 5f, 7f, 20f);
                Vector2 start = mid - across * half, goal = mid + across * half;
                if (_obstacles.Inside(start, 2.5f) || _obstacles.Inside(goal, 2.5f)) continue;
                int slot = -1;
                for (int i = 0; i < _groups.Length; i++)
                {
                    ref Group p = ref _groups[i];
                    if (!p.active || p.kind != Kind.Dolphins || p.state == 1 || (p.pos - start).sqrMagnitude > 18f * 18f) continue;
                    slot = i;
                    break;
                }
                if (slot < 0)
                {
                    slot = SpawnAt(Kind.Dolphins, start, across);
                    _groups[slot].fade = 0f;
                }
                ref Group g = ref _groups[slot];
                g.state = 4;
                g.goal = goal;
                g.timer = 2f * half / Mathf.Max(0.5f, dolphinCruise * 2.4f) + 6f;
                g.cooldown = 0f;
                g.flags &= ~EscortFlag;
                if (g.jump < 0f) g.jump = Mathf.Max(g.jump, -0.8f);
                g.dir = across;
                _dirty = true;
                return true;
            }
            return false;
        }

        bool ShowRayLeap(Vector2 center, float radius, uint h)
        {
            if (_storm > 0.5f) return false;
            for (int i = 0; i < _groups.Length; i++)
            {
                ref Group g = ref _groups[i];
                if (!g.active || g.kind != Kind.Ray || g.fade < 0.6f || g.jump >= 0f) continue;
                if ((g.pos - center).sqrMagnitude > radius * radius || !InView(g.pos, 0.08f) || !OpenWater(g.pos, -0.8f)) continue;
                g.jump = 0f;
                return true;
            }
            for (int a = 0; a < 5; a++)
            {
                Vector2 p = SeaShow.DiscPoint(center, radius * 0.85f, h, 100 + a * 2);
                if (!InView(p, 0.1f) || !OpenWater(p, -0.9f) || !OpenWater(p + _viewFwd * 2f, -0.9f)) continue;
                int slot = SpawnAt(Kind.Ray, p, Rotate(_viewFwd, (SeaMath.Rand(h, 110 + a) - 0.5f) * 3f));
                ref Group g = ref _groups[slot];
                g.count = 1;
                g.jump = 0f;
                return true;
            }
            return false;
        }

        // A far view has little open water between the big coasts in the picture: it gets more tries for a spot.
        bool ShowWhale(Vector2 center, float radius, uint h, int tries = 4)
        {
            if (_groups == null) return false;
            for (int i = 0; i < _groups.Length; i++)
            {
                ref Group g = ref _groups[i];
                if (!g.active || !IsWhale(g.kind) || g.state != 0 || g.fade < 0.5f || g.depth > 0.3f) continue;
                if ((g.flags & (MigrateFlag | PickupFlag | CompanionFlag)) != 0) continue;
                if ((g.pos - center).magnitude > radius + 0.5f * WhaleReach(ref g)) continue;
                if (_hasView && !InView(g.pos, -0.1f)) continue;
                g.timer = Mathf.Min(g.timer, 0.2f);
                return true;
            }
            for (int a = 0; a < tries; a++)
            {
                Vector2 p = SeaShow.DiscPoint(center, radius, h, a < 4 ? 120 + a * 2 : 160 + a * 2);
                if (_hasView && !InView(p, 0.08f)) continue;
                bool pod = SeaMath.Rand(h, a < 4 ? 130 + a : 180 + a) < 0.5f;
                if (!SeaMath.WhaleSpawnAllowed(pod, WhalePodCount, WhaleLonerCount, maxWhalePods, maxWhales))
                {
                    pod = !pod;
                    if (!SeaMath.WhaleSpawnAllowed(pod, WhalePodCount, WhaleLonerCount, maxWhalePods, maxWhales)) return false;
                }
                float reach = pod ? 2.7f * whaleLength : whaleLength * whaleBullScale;
                if (_obstacles.Inside(p, SeaMath.WhaleClearance + reach)) continue;
                int slot = SpawnAt(pod ? Kind.WhalePod : Kind.Whale, p, new Vector2(_viewFwd.y, -_viewFwd.x) * ((h & 4u) == 0u ? 1f : -1f));
                ref Group g = ref _groups[slot];
                g.fade = 0.2f;
                g.state = 0;
                g.timer = 1.5f;
                g.breach = Mathf.Max(g.breach, 30f);
                return true;
            }
            return false;
        }

        // ---- verification helpers ----

        // A seal pops up at the player's coast on the side the camera looks at.
        public bool TriggerSeal() => _player != null && StartSeal(_player, _viewFwd, true);

        public bool TriggerShowTurtle() => _player != null && StartShowTurtle(_player, _viewFwd, 0.5f);

        public bool TriggerRayLeap(int group = -1)
        {
            bool any = false;
            for (int i = 0; i < _groups.Length; i++)
                if (_groups[i].active && _groups[i].kind == Kind.Ray && (group < 0 || group == i)) { _groups[i].jump = 0f; any = true; }
            return any;
        }

        // ---- templates / drawing ----

        static void BuildShowTemplates()
        {
            Color back = new Color(0.78f, 0.78f, 0.8f), flank = Color.white, belly = new Color(1.45f, 1.4f, 1.35f), dark = new Color(0.12f, 0.12f, 0.12f);
            var rings = new[]
            {
                new SeaShape.Ring(0.5f, 0f, 0f, 0.14f), new SeaShape.Ring(0.46f, 0.045f, 0.04f, 0.14f),
                new SeaShape.Ring(0.38f, 0.085f, 0.08f, 0.15f), new SeaShape.Ring(0.29f, 0.075f, 0.07f, 0.13f),
                new SeaShape.Ring(0.15f, 0.14f, 0.12f, 0.12f), new SeaShape.Ring(-0.05f, 0.16f, 0.135f, 0.135f),
                new SeaShape.Ring(-0.25f, 0.11f, 0.09f, 0.09f), new SeaShape.Ring(-0.4f, 0.045f, 0.04f, 0.045f)
            };
            var s = new SeaShape();
            s.Loft(rings, 6, 0f, Mathf.PI * 2f, (n, z) => n.y > 0.55f ? back : n.y < -0.35f ? belly : flank);
            for (int sg = -1; sg <= 1; sg += 2)
            {
                s.SheetTri(new Vector3(sg * 0.12f, 0.04f, 0.2f), new Vector3(sg * 0.26f, 0f, 0.08f), new Vector3(sg * 0.12f, 0.03f, 0.08f), flank, Vector3.up);
                s.SheetTri(new Vector3(0f, 0.045f, -0.38f), new Vector3(sg * 0.13f, 0.03f, -0.56f), new Vector3(sg * 0.02f, 0.04f, -0.48f), back, Vector3.up);
                s.SheetTri(new Vector3(sg * 0.086f, 0.165f, 0.41f), new Vector3(sg * 0.086f, 0.19f, 0.37f), new Vector3(sg * 0.086f, 0.155f, 0.36f), dark, new Vector3(sg, 0f, 0f));
            }
            s.SheetTri(new Vector3(-0.02f, 0.165f, 0.475f), new Vector3(0.02f, 0.165f, 0.475f), new Vector3(0f, 0.15f, 0.505f), dark, Vector3.up);
            _tSeal = s.Build();

            var lo = new SeaShape();
            var lr = new[]
            {
                new SeaShape.Ring(0.5f, 0f, 0f, 0.14f), new SeaShape.Ring(0.3f, 0.08f, 0.075f, 0.13f),
                new SeaShape.Ring(-0.05f, 0.16f, 0.135f, 0.135f), new SeaShape.Ring(-0.4f, 0.045f, 0.04f, 0.045f)
            };
            lo.Loft(lr, 4, 0f, Mathf.PI * 2f, (n, z) => Color.white);
            lo.SheetTri(new Vector3(0f, 0.045f, -0.38f), new Vector3(-0.13f, 0.03f, -0.56f), new Vector3(0.13f, 0.03f, -0.56f), Color.white, Vector3.up);
            _tSealLow = lo.Build();

            // Bottling: the neck straight up out of the water (origin well below the surface), the head looking along +z.
            var hd = new SeaShape();
            hd.Prism(new Vector3(0f, -0.35f, -0.02f), 0.12f, 0.085f, 0.47f, 6, flank, flank, false);
            var hr = new[]
            {
                new SeaShape.Ring(-0.11f, 0f, 0f, 0.15f), new SeaShape.Ring(-0.08f, 0.08f, 0.075f, 0.15f),
                new SeaShape.Ring(0.02f, 0.1f, 0.09f, 0.15f), new SeaShape.Ring(0.12f, 0.065f, 0.06f, 0.13f),
                new SeaShape.Ring(0.2f, 0.035f, 0.032f, 0.12f), new SeaShape.Ring(0.23f, 0f, 0f, 0.12f)
            };
            hd.Loft(hr, 6, 0f, Mathf.PI * 2f, (n, z) => n.y > 0.55f ? back : n.y < -0.4f ? belly : flank);
            for (int sg = -1; sg <= 1; sg += 2)
                hd.SheetTri(new Vector3(sg * 0.086f, 0.165f, 0.07f), new Vector3(sg * 0.086f, 0.195f, 0.03f), new Vector3(sg * 0.086f, 0.16f, 0.01f), dark, new Vector3(sg, 0f, 0f));
            hd.SheetTri(new Vector3(-0.022f, 0.15f, 0.205f), new Vector3(0.022f, 0.15f, 0.205f), new Vector3(0f, 0.138f, 0.232f), dark, Vector3.up);
            _tSealHead = hd.Build();
        }

        void DrawSeals()
        {
            Color deep = new Color(sealColor.r * 0.55f, sealColor.g * 0.65f, sealColor.b * 0.85f, 0.75f);
            for (int i = 0; i < MaxSeals; i++)
            {
                if (!_seals[i].active) continue;
                ref Seal s = ref _seals[i];
                float len = sealLength * Mathf.Clamp01(s.fade);
                if (len < 0.02f) continue;
                Vector3 scale = new Vector3(len, len, len);
                switch (s.state)
                {
                    case SealLook:
                    {
                        float rise = SeaShow.SealRise(s.t, s.dur);
                        // It looks round at the island (and so, most of the time, at the camera).
                        Vector2 face = s.host != null ? s.host.PlanarPosition - s.pos : s.dir;
                        face = face.sqrMagnitude > 1e-4f ? face.normalized : Vector2.up;
                        Vector2 look = Rotate(face, SeaShow.SealLook(s.t, s.phase) * rise);
                        SeaBatch.Basis(face, 0.9f * rise, 0f, out Vector3 br, out Vector3 bu, out Vector3 bf);
                        Vector2 body = s.pos - face * (0.3f * len);
                        _under.AddFlat(_tSealLow, new Vector3(body.x, -0.5f, body.y), br, bu, bf, scale, deep);
                        if (rise > 0.3f)
                        {
                            SeaBatch.Basis(look, 0.08f * Mathf.Sin(_clock * 1.7f + s.phase), 0f, out Vector3 r, out Vector3 u, out Vector3 f);
                            float y = Mathf.Lerp(-0.32f, -0.03f, rise) * len + 0.03f * Mathf.Sin(_clock * 2f + s.phase);
                            _above.Add(_tSealHead, new Vector3(s.pos.x, y, s.pos.y), r, u, f, scale, sealColor);
                        }
                        break;
                    }
                    case SealBeach:
                    case SealClimb:
                    case SealSlide:
                    {
                        if (s.host == null) break;
                        float k = s.state == SealBeach ? 1f : Mathf.Clamp01(s.t / (s.state == SealClimb ? SealClimbSeconds : SealSlideSeconds));
                        if (s.state == SealSlide) k = 1f - k;
                        Vector2 l = Vector2.Lerp(s.local, s.beach, k);
                        Transform ht = s.host.transform;
                        Vector3 p = ht.TransformPoint(new Vector3(l.x, Mathf.Max(s.host.SampleHeight(l), -0.22f), l.y));
                        Vector3 wd = ht.TransformDirection(new Vector3(s.beachDir.x, 0f, s.beachDir.y));
                        Vector2 d2 = new Vector2(wd.x, wd.z);
                        d2 = d2.sqrMagnitude > 1e-6f ? d2.normalized : s.dir;
                        float half = 0.35f * len;
                        float slope = Mathf.Atan2(s.host.SampleHeight(l + s.beachDir * half) - s.host.SampleHeight(l - s.beachDir * half), 2f * half);
                        // Resting it lifts its head now and then (pivoting on the tail); hauling out it wriggles.
                        float lift = 0f;
                        if (s.state == SealBeach)
                        {
                            float cyc = (s.t + s.phase) % 5f;
                            lift = cyc < 1.6f ? Mathf.Sin(cyc / 1.6f * Mathf.PI) * 0.3f : 0f;
                        }
                        float wiggle = s.state == SealBeach ? 0f : Mathf.Sin(_clock * 9f + s.phase) * 0.12f;
                        SeaBatch.Basis(d2, Mathf.Clamp(slope, -0.6f, 0.6f) + lift, wiggle, out Vector3 r, out Vector3 u, out Vector3 f);
                        p += Vector3.up * (Mathf.Sin(lift) * 0.4f * len);
                        _above.Add(_tSeal, p, r, u, f, scale, sealColor);
                        break;
                    }
                    default:
                    {
                        SeaBatch.Basis(s.dir, 0.06f * Mathf.Sin(_clock * 1.5f + s.phase), 0f, out Vector3 r, out Vector3 u, out Vector3 f);
                        _under.AddFlat(_tSealLow, new Vector3(s.pos.x, -0.42f, s.pos.y), r, u, f, scale, new Color(deep.r, deep.g, deep.b, deep.a * Mathf.Clamp01(s.fade)));
                        break;
                    }
                }
            }
        }
    }
}
