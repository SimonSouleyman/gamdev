using Drift.Core;
using Drift.Islands;
using UnityEngine;

namespace Drift.Visuals
{
    // Cozy only: a second pool of schools that keeps the camera's picture of the sea busy. It is not seeded from the
    // world grid; it fills the ground footprint of the camera (four viewport corners projected onto the sea) up to a
    // target count, spawning beside the picture and heading into it, and retires schools once they are out of sight.
    // Seven kinds with their own outlines (FishShapes), depths, speeds and formations; all drawn into the same mesh,
    // culled per school against the camera frustum and capped by cozyAmbientVertexBudget.
    // Seen from the high cozy camera a 0.28 u fish is a few pixels, so in cozy every school (these and the grid ones)
    // is drawn larger the farther the camera looks from (ViewScale).
    public partial class FishSystem
    {
        [Header("Gemütlich: mehr Fische im Bild")]
        [Tooltip("So viele zusätzliche Schwärme verschiedener Arten schwimmen höchstens im und am Bild (nur Gemütlich).")]
        [Range(0, 24)] public int cozyAmbientSchools = 20;
        [Tooltip("Sichtbare Meeresfläche (u²) je zusätzlichem Schwarm: kleiner = mehr Schwärme im Bild.")]
        public float cozyAmbientAreaPerSchool = 90f;
        [Tooltip("So viele zusätzliche Schwärme hält das Bild mindestens, auch wenn nur wenig Meer zu sehen ist.")]
        [Range(0, 24)] public int cozyAmbientMin = 8;
        [Tooltip("Bis zu dieser Bildhöhe (0 unten, 1 oben) werden Schwärme verteilt; weiter hinten sind sie zu klein.")]
        [Range(0.3f, 1f)] public float cozyAmbientViewTop = 0.85f;
        [Tooltip("Kameraabstand (u), ab dem Fische größer gezeichnet werden, damit man sie von weit oben noch erkennt.")]
        public float cozyViewRefDistance = 14f;
        [Range(0f, 1f)] public float cozyViewScaleExponent = 0.6f;
        [Range(1f, 4f)] public float cozyViewScaleMax = 2.4f;
        [Tooltip("Höchstens so viele Fisch-Vertices für die zusätzlichen Schwärme (Handy-Budget).")]
        public int cozyAmbientVertexBudget = 9000;
        [Tooltip("Nach so vielen Sekunden (min, max) verschwindet ein Schwarm, sobald er außer Sicht ist.")]
        public Vector2 cozyAmbientLifetime = new Vector2(70f, 150f);
        [Tooltip("Ein- und Ausblenden (s) eines Schwarms, der im Bild auftaucht.")]
        public float cozyAmbientFade = 2f;

        enum Form : byte { Cloud, Line, Vee, Loner }

        struct AmbientKind
        {
            public FishSpecies species;
            public float weight;
            public int min, max;
            public float size, depth, speed, spread, wagFreq, turn, response;
            public int shape;
            public Form form;
            public bool coast;
            public float jumpMin, jumpMax, jumpScale;
        }

        static AmbientKind K(FishSpecies sp, float weight, int min, int max, float size, float depth, float speed, float spread,
                             float wagFreq, float turn, float response, int shape, Form form, bool coast,
                             float jumpMin = 0f, float jumpMax = 0f, float jumpScale = 1f) =>
            new AmbientKind
            {
                species = sp, weight = weight, min = min, max = max, size = size, depth = depth, speed = speed, spread = spread,
                wagFreq = wagFreq, turn = turn, response = response, shape = shape, form = form, coast = coast,
                jumpMin = jumpMin, jumpMax = jumpMax, jumpScale = jumpScale,
            };

        static readonly AmbientKind[] Kinds =
        {
            K(FishSpecies.Silverling, 0.2f, 28, 60, 0.6f, -0.1f, 0.45f, 1.05f, 11f, 0.35f, 1.5f, FishShapes.Slim, Form.Cloud, false),
            K(FishSpecies.Glitter, 0.13f, 14, 26, 0.45f, -0.02f, 0.9f, 0.8f, 13f, 0.9f, 3f, FishShapes.Slim, Form.Cloud, false, 5f, 12f, 0.45f),
            K(FishSpecies.Reef, 0.22f, 6, 14, 1f, -0.05f, 0.7f, 0.7f, 10f, 0.4f, 2.5f, FishShapes.Striped, Form.Cloud, true),
            K(FishSpecies.Tuna, 0.12f, 3, 6, 3f, -0.35f, 1.7f, 1f, 6f, 0.12f, 1.2f, FishShapes.Tuna, Form.Vee, false, 18f, 40f, 1.3f),
            K(FishSpecies.Needlefish, 0.12f, 4, 9, 2f, -0.03f, 1.1f, 1f, 5f, 0.5f, 2f, FishShapes.Needle, Form.Line, false),
            K(FishSpecies.Sunfish, 0.08f, 1, 2, 5f, -0.03f, 0.25f, 1f, 2.5f, 0.15f, 0.6f, FishShapes.Disc, Form.Loner, false),
            K(FishSpecies.Grouper, 0.07f, 1, 2, 3.6f, -0.3f, 0.35f, 1f, 3f, 0.2f, 0.8f, FishShapes.Grouper, Form.Loner, true),
        };

        static readonly Color[] ReefBodies =
        {
            new Color(1f, 0.84f, 0.18f), new Color(0.22f, 0.46f, 1f), new Color(1f, 0.5f, 0.12f), new Color(0.86f, 0.32f, 0.78f),
        };
        static readonly Color[] ReefBands =
        {
            new Color(0.12f, 0.12f, 0.22f), new Color(1f, 0.86f, 0.2f), new Color(1f, 1f, 1f), new Color(1f, 0.9f, 0.3f),
        };

        Vector2 _q0, _q1, _q2, _q3, _viewCenter;
        float _viewRadius, _viewArea, _viewScale = 1f;
        bool _viewOk, _viewOverride;
        uint _ambientSerial;
        int _ambientInView, _ambientTarget, _ambientDrawn;
        readonly Plane[] _planes = new Plane[6];
        readonly Color[] _roleCols = new Color[FishShapes.RoleCount];

        int AmbientSlots => Cozy ? Mathf.Clamp(cozyAmbientSchools, 0, 24) : 0;
        float MainScale => Cozy ? _viewScale : 1f;

        public float ViewScale => _viewScale;
        public Vector2 ViewCenter => _viewCenter;
        public float ViewRadius => _viewRadius;
        public int AmbientTarget => _ambientTarget;
        public int AmbientInView => _ambientInView;
        public int AmbientDrawn => _ambientDrawn;
        public bool IsAmbient(int i) => _schools != null && _schools[i].active && _schools[i].ambient;
        public bool InView(Vector2 p) => _viewOk && InQuad(p);

        public int AmbientActive
        {
            get
            {
                int n = 0;
                if (_schools != null) for (int i = 0; i < _schools.Length; i++) if (_schools[i].active && _schools[i].ambient) n++;
                return n;
            }
        }

        // Tests / tools: a fixed rectangular view instead of the main camera's footprint (no frustum culling then).
        public void SetViewOverride(Vector2 center, Vector2 halfSize, float viewScale)
        {
            _viewOverride = true;
            _q0 = center + new Vector2(-halfSize.x, -halfSize.y);
            _q1 = center + new Vector2(halfSize.x, -halfSize.y);
            _q2 = center + new Vector2(halfSize.x, halfSize.y);
            _q3 = center + new Vector2(-halfSize.x, halfSize.y);
            FinishQuad();
            _viewScale = Mathf.Max(1f, viewScale);
            _viewOk = true;
        }

        public void ClearViewOverride()
        {
            _viewOverride = false;
            _viewOk = false;
        }

        // ---- view ----

        void UpdateView(float dt)
        {
            if (!Cozy)
            {
                _viewScale = 1f;
                _viewOk = false;
                return;
            }
            if (_viewOverride)
            {
                _viewOk = true;
                return;
            }
            var cam = Camera.main;
            if (cam == null)
            {
                _viewOk = false;
                return;
            }
            float top = cozyAmbientViewTop;
            _q0 = Ground(cam, 0f, 0f);
            _q1 = Ground(cam, 1f, 0f);
            _q2 = Ground(cam, 1f, top);
            _q3 = Ground(cam, 0f, top);
            FinishQuad();
            Vector3 cp = cam.transform.position;
            Vector2 mid = Ground(cam, 0.5f, 0.5f);
            float dist = new Vector3(mid.x - cp.x, cp.y, mid.y - cp.z).magnitude;
            float target = Mathf.Clamp(Mathf.Pow(Mathf.Max(1f, dist / Mathf.Max(1f, cozyViewRefDistance)), cozyViewScaleExponent), 1f, cozyViewScaleMax);
            _viewScale = dt > 0f && _viewOk ? Mathf.Lerp(_viewScale, target, 1f - Mathf.Exp(-2f * dt)) : target;
            _viewOk = true;
        }

        static Vector2 Ground(Camera cam, float x, float y)
        {
            const float far = 140f;
            Ray r = cam.ViewportPointToRay(new Vector3(x, y, 0f));
            Vector3 o = r.origin, d = r.direction;
            if (d.y < -0.01f && o.y > 0f)
            {
                float t = Mathf.Min(-o.y / d.y, far);
                return new Vector2(o.x + d.x * t, o.z + d.z * t);
            }
            Vector2 h = new Vector2(d.x, d.z);
            if (h.sqrMagnitude < 1e-6f) h = Vector2.up;
            return new Vector2(o.x, o.z) + h.normalized * far;
        }

        void FinishQuad()
        {
            _viewCenter = (_q0 + _q1 + _q2 + _q3) * 0.25f;
            _viewRadius = Mathf.Max(Mathf.Max((_q0 - _viewCenter).magnitude, (_q1 - _viewCenter).magnitude),
                                    Mathf.Max((_q2 - _viewCenter).magnitude, (_q3 - _viewCenter).magnitude));
            _viewArea = 0.5f * Mathf.Abs(Cross(_q0, _q1) + Cross(_q1, _q2) + Cross(_q2, _q3) + Cross(_q3, _q0));
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static float Side(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        bool InQuad(Vector2 p)
        {
            float c0 = Side(_q0, _q1, p), c1 = Side(_q1, _q2, p), c2 = Side(_q2, _q3, p), c3 = Side(_q3, _q0, p);
            return (c0 >= 0f && c1 >= 0f && c2 >= 0f && c3 >= 0f) || (c0 <= 0f && c1 <= 0f && c2 <= 0f && c3 <= 0f);
        }

        Vector2 QuadPoint(float u, float v) => Vector2.LerpUnclamped(Vector2.LerpUnclamped(_q0, _q1, u), Vector2.LerpUnclamped(_q3, _q2, u), v);

        // ---- spawning ----

        void ScanAmbient()
        {
            int slots = _schools.Length;
            float keep = _viewRadius * 1.5f + 6f;
            float near = _viewRadius * 1.1f + 2f;
            int nearCount = 0, inView = 0;
            for (int i = maxSchools; i < slots; i++)
            {
                ref School s = ref _schools[i];
                if (!s.active || !s.ambient) continue;
                bool vis = InQuad(s.pos);
                float d2 = (s.pos - _viewCenter).sqrMagnitude;
                if (d2 > keep * keep || (!vis && s.age > s.life) || (s.leaving && s.fade <= 0f))
                {
                    s.active = false;
                    s.ambient = false;
                    s.island = null;
                    _dirty = true;
                    continue;
                }
                if (d2 <= near * near) nearCount++;
                if (vis) inView++;
            }
            int cap = AmbientSlots;
            // A few slots stay free for schools that are leaving the picture but are not retired yet.
            int most = Mathf.Max(0, cap - 3);
            _ambientTarget = Mathf.Clamp(Mathf.RoundToInt(_viewArea / Mathf.Max(1f, cozyAmbientAreaPerSchool)), Mathf.Min(cozyAmbientMin, most), most);
            bool sparse = inView < _ambientTarget / 2;
            int spawns = sparse ? 4 : 2;
            for (int k = 0; k < spawns && nearCount < _ambientTarget; k++)
            {
                int slot = -1;
                for (int i = maxSchools; i < slots; i++) if (!_schools[i].active) { slot = i; break; }
                if (slot < 0) break;
                if (!SpawnAmbient(slot, sparse)) continue;
                nearCount++;
                if (InQuad(_schools[slot].pos)) inView++;
            }
            _ambientInView = inView;
        }

        static int PickKind(float r, bool openOnly)
        {
            float total = 0f;
            for (int i = 0; i < Kinds.Length; i++) if (!openOnly || !Kinds[i].coast) total += Kinds[i].weight;
            r *= total;
            for (int i = 0; i < Kinds.Length; i++)
            {
                if (openOnly && Kinds[i].coast) continue;
                r -= Kinds[i].weight;
                if (r <= 0f) return i;
            }
            return 0;
        }

        static float BiomeWeight(LifeBiome b, FishSpecies sp)
        {
            if (sp != FishSpecies.Reef) return b == LifeBiome.Nordic ? 0.5f : 1f;
            switch (b)
            {
                case LifeBiome.Tropical: return 1f;
                case LifeBiome.Savanna: return 0.6f;
                case LifeBiome.Temperate: return 0.4f;
                default: return 0.12f;
            }
        }

        bool SpawnAmbient(int slot, bool allowInView)
        {
            _ambientSerial++;
            uint h = Hash((uint)seed * 7919u + 17u, _ambientSerial);
            int kind = PickKind(Rand(h, 20), false);
            Island isl = null;
            Vector2 pos = default, dir;
            float orbit = Rand(h, 4) < 0.5f ? -1f : 1f;
            if (Kinds[kind].coast)
            {
                isl = PickCoastIsland(h, Kinds[kind].species, out pos);
                if (isl == null) kind = PickKind(Rand(h, 23), true);
            }
            if (isl != null)
            {
                Vector2 r = (pos - isl.PlanarPosition).normalized;
                dir = new Vector2(-r.y, r.x) * orbit;
            }
            else
            {
                if (!OpenWaterPoint(h, allowInView, out pos)) return false;
                if (InQuad(pos))
                {
                    float a = Rand(h, 9) * Mathf.PI * 2f;
                    dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                }
                else
                {
                    dir = QuadPoint(0.2f + 0.6f * Rand(h, 21), 0.15f + 0.6f * Rand(h, 22)) - pos;
                    dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.up;
                }
            }

            ref AmbientKind k = ref Kinds[kind];
            ref School s = ref _schools[slot];
            s = default;
            s.active = true;
            s.ambient = true;
            s.kind = kind;
            s.species = k.species;
            s.key = long.MinValue + 1000 + slot;
            s.seed = h;
            s.pos = pos;
            s.dir = dir;
            s.island = isl;
            s.orbitSign = orbit;
            s.count = k.min + (int)(Rand(h, 5) * (k.max - k.min + 0.999f));
            s.size = k.size;
            s.jumpT = -1f;
            s.jumpTimer = k.jumpMin > 0f ? Mathf.Lerp(k.jumpMin, k.jumpMax, Rand(h, 6)) : 1e9f;
            s.retarget = Rand(h, 7);
            s.life = Mathf.Lerp(cozyAmbientLifetime.x, Mathf.Max(cozyAmbientLifetime.x, cozyAmbientLifetime.y), Rand(h, 8));
            s.fade = Application.isPlaying ? 0f : 1f;
            AmbientColors(k.species, h, out s.color, out s.accent);
            _dirty = true;
            return true;
        }

        static void AmbientColors(FishSpecies sp, uint h, out Color body, out Color accent)
        {
            float v = 0.92f + 0.16f * Rand(h, 12);
            switch (sp)
            {
                case FishSpecies.Silverling:
                    body = new Color(0.72f * v, 0.82f * v, 0.93f, 1f);
                    accent = body;
                    return;
                case FishSpecies.Glitter:
                    body = new Color(0.86f, 0.93f, 1f, 1f);
                    accent = new Color(0.55f, 0.85f, 0.95f, 1f);
                    return;
                case FishSpecies.Reef:
                    int p = (int)(Rand(h, 13) * ReefBodies.Length) % ReefBodies.Length;
                    body = ReefBodies[p] * v;
                    body.a = 1f;
                    accent = ReefBands[p];
                    return;
                case FishSpecies.Tuna:
                    body = new Color(0.4f * v, 0.5f * v, 0.66f * v, 1f);
                    accent = new Color(1f, 0.82f, 0.22f, 1f);
                    return;
                case FishSpecies.Needlefish:
                    body = new Color(0.46f * v, 0.74f * v, 0.66f * v, 1f);
                    accent = body;
                    return;
                case FishSpecies.Sunfish:
                    body = new Color(0.7f * v, 0.73f * v, 0.78f * v, 1f);
                    accent = body;
                    return;
                default:
                    body = new Color(0.66f * v, 0.42f * v, 0.24f * v, 1f);
                    accent = new Color(0.95f, 0.84f, 0.62f, 1f);
                    return;
            }
        }

        bool IsShallow(Vector2 p, float margin)
        {
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var isl = all[i];
                if (isl == null || !isl.isActiveAndEnabled || isl.IsSunk) continue;
                float r = isl.BoundingRadius + 1.5f;
                if ((isl.PlanarPosition - p).sqrMagnitude > r * r) continue;
                if (isl.SampleHeight(isl.ToLocal(p)) > shoreHeight - margin) return true;
            }
            return false;
        }

        bool OpenWaterPoint(uint h, bool allowInView, out Vector2 p)
        {
            for (int t = 0; t < 5; t++)
            {
                int b = 30 + t * 4;
                float region = Rand(h, b), a = Rand(h, b + 1), c = Rand(h, b + 2);
                float u, v;
                if (allowInView && region < 0.6f) { u = 0.05f + 0.9f * a; v = 0.05f + 0.9f * c; }
                else
                {
                    float side = Rand(h, b + 3);
                    if (side < 0.35f) { u = -0.05f - 0.25f * a; v = c; }
                    else if (side < 0.7f) { u = 1.05f + 0.25f * a; v = c; }
                    else if (side < 0.85f) { u = a; v = -0.05f - 0.2f * c; }
                    else { u = a; v = 1.02f + 0.12f * c; }
                }
                p = QuadPoint(u, v);
                if (!allowInView && InQuad(p)) continue;
                if (IsShallow(p, 0.3f)) continue;
                return true;
            }
            p = default;
            return false;
        }

        // An island near the picture (warm ones preferred for reef fish) and a spot just off its beach.
        Island PickCoastIsland(uint h, FishSpecies sp, out Vector2 pos)
        {
            pos = default;
            var all = Island.All;
            Island best = null;
            float bestKey = -1f;
            for (int i = 0; i < all.Count; i++)
            {
                var isl = all[i];
                if (isl == null || !isl.isActiveAndEnabled || isl.IsSunk || isl.IsEmerging) continue;
                float reach = _viewRadius + isl.BoundingRadius * 0.5f;
                if ((isl.PlanarPosition - _viewCenter).sqrMagnitude > reach * reach) continue;
                float w = BiomeWeight(Island.BiomeOf(isl), sp);
                float key = Mathf.Pow(Mathf.Max(1e-4f, Rand(h, 60 + i)), 1f / w);
                if (key > bestKey) { bestKey = key; best = isl; }
            }
            if (best == null || bestKey < 0.35f) return null;
            Vector2 c = best.PlanarPosition;
            for (int t = 0; t < 3; t++)
            {
                float a = Rand(h, 40 + t) * Mathf.PI * 2f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                for (float r = best.BoundingRadius + 1f; r > 0.5f; r -= 0.75f)
                {
                    if (best.SampleHeight(best.ToLocal(c + d * r)) <= shoreHeight - shoreBand) continue;
                    pos = c + d * (r + 0.8f);
                    return best;
                }
            }
            return null;
        }

        // ---- motion ----

        void StepAmbient(int idx, float dt)
        {
            ref School s = ref _schools[idx];
            ref AmbientKind k = ref Kinds[s.kind];
            float fadeT = Mathf.Max(0.05f, cozyAmbientFade);
            s.age += dt;
            s.fade = s.leaving ? Mathf.Max(0f, s.fade - dt / fadeT) : Mathf.Min(1f, s.fade + dt / fadeT);

            s.retarget -= dt;
            bool check = s.retarget <= 0f;
            if (check)
            {
                s.retarget = 1f;
                if (s.island != null && (!s.island.isActiveAndEnabled || s.island.IsSunk)) s.island = null;
            }

            Vector2 dp = s.pos - _playerPos;
            float pr = _playerRadius + scatterRadius;
            if (s.scatter <= 0f && _playerSpeed > playerMoveThreshold && dp.sqrMagnitude < pr * pr)
            {
                s.scatter = 1f;
                s.scatterFrom = _playerPos;
            }

            float pace = Mathf.Sqrt(_viewScale);
            float speed = k.speed * pace;
            Vector2 desired;
            if (s.scatter > 0f)
            {
                s.scatter = Mathf.Max(0f, s.scatter - dt / Mathf.Max(0.1f, scatterDuration));
                Vector2 away = s.pos - s.scatterFrom;
                desired = away.sqrMagnitude > 1e-4f ? away.normalized : s.dir;
                speed += scatterSpeed * s.scatter;
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
            }
            else
            {
                if (check && IsShallow(s.pos + s.dir * (3f + 2f * _viewScale), 0.3f)) s.avoid = 1.5f;
                if (s.avoid > 0f)
                {
                    s.avoid -= dt;
                    desired = new Vector2(-s.dir.y, s.dir.x) * s.orbitSign;
                }
                else
                {
                    float turn = k.turn * Mathf.Sin(_clock * 0.23f + (s.seed & 1023u) * 0.013f) * dt;
                    float cs = Mathf.Cos(turn), sn = Mathf.Sin(turn);
                    desired = new Vector2(s.dir.x * cs - s.dir.y * sn, s.dir.x * sn + s.dir.y * cs);
                }
            }
            if (s.species == FishSpecies.Glitter) speed *= 0.5f + 1.4f * Mathf.Max(0f, Mathf.Sin(_clock * 1.9f + (s.seed & 255u) * 0.1f));

            Vector2 nd = Vector2.Lerp(s.dir, desired, 1f - Mathf.Exp(-k.response * dt));
            s.dir = nd.sqrMagnitude > 1e-6f ? nd.normalized : desired;
            s.pos += s.dir * (speed * dt);

            if (s.jumpT >= 0f)
            {
                s.jumpT += dt / Mathf.Max(0.05f, jumpDuration);
                if (s.jumpT >= 1f)
                {
                    s.jumpT = -1f;
                    if (water != null) water.Splash(AmbientFishWorld(ref s, s.jumper, out _, out _, out _), 0.6f * k.jumpScale);
                }
            }
            else
            {
                s.jumpTimer -= dt;
                if (s.jumpTimer <= 0f)
                {
                    s.jumpT = 0f;
                    int t = Mathf.FloorToInt(_clock);
                    s.jumper = (int)(Rand(s.seed, 500 + t) * s.count) % s.count;
                    s.jumpTimer = k.jumpMin > 0f ? Mathf.Lerp(k.jumpMin, k.jumpMax, Rand(s.seed, 700 + t)) : 1e9f;
                    if (s.burst > 0)
                    {
                        s.burst--;
                        s.jumpTimer = 0.35f + 0.3f * Rand(s.seed, 950 + t);
                    }
                    Vector2 w = AmbientFishWorld(ref s, s.jumper, out _, out _, out _);
                    if (water != null) water.Splash(w, 0.35f * k.jumpScale);
                    // Only the big leapers count as a moment; the glitter hops are background.
                    if (k.jumpScale >= 1f && Moments.Listening) Moments.Report(MomentKind.FishJump, new Vector3(w.x, 0f, w.y));
                }
            }
        }

        float AmbientLength(ref School s) => fishLength * s.size * _viewScale;

        Vector2 AmbientFishWorld(ref School s, int i, out Vector2 forward, out float phase, out float size)
        {
            ref AmbientKind k = ref Kinds[s.kind];
            uint h = Hash(s.seed, (uint)i + 101u);
            phase = Rand(h, 2) * Mathf.PI * 2f;
            size = 0.85f + 0.3f * Rand(h, 3);
            float L = AmbientLength(ref s);
            float spreadOut = 1f + s.scatter * 2.2f;
            Vector2 right = new Vector2(s.dir.y, -s.dir.x);
            Vector2 o;
            float wig;
            switch (k.form)
            {
                case Form.Line:
                {
                    float spacing = L * 0.85f;
                    float amp = 0.35f * spacing;
                    float ph = _clock * 1.4f + i * 0.6f + (s.seed & 63u) * 0.1f;
                    o = new Vector2(amp * Mathf.Sin(ph), (s.count - 1) * spacing * 0.5f - i * spacing) * spreadOut;
                    Vector2 f = new Vector2(-amp * 0.6f * Mathf.Cos(ph) / spacing, 1f).normalized;
                    forward = right * f.x + s.dir * f.y;
                    size = 0.92f + 0.16f * Rand(h, 3);
                    return s.pos + right * o.x + s.dir * o.y;
                }
                case Form.Vee:
                {
                    int j = (i + 1) / 2;
                    float side = (i & 1) == 0 ? 1f : -1f;
                    o = new Vector2(side * j * 0.75f * L, -j * 0.55f * L + Mathf.Sin(_clock * 0.8f + phase) * 0.1f * L) * spreadOut;
                    wig = Mathf.Sin(_clock * 0.9f + phase) * 0.06f;
                    break;
                }
                case Form.Loner:
                {
                    int j = (i + 1) / 2;
                    float side = (i & 1) == 0 ? 1f : -1f;
                    o = new Vector2(side * j * 1.2f * L, -j * 1f * L) * spreadOut;
                    size = 0.9f + 0.2f * Rand(h, 3);
                    wig = Mathf.Sin(_clock * 0.3f + phase) * 0.15f;
                    break;
                }
                default:
                {
                    o = FishLocal(s.seed, i, s.count, _clock, s.scatter, out phase, out size) * (k.spread * _viewScale);
                    if (s.species == FishSpecies.Glitter)
                        o += new Vector2(Mathf.Sin(_clock * 7f + phase * 5f), Mathf.Cos(_clock * 6.3f + phase * 3f)) * (0.08f * _viewScale);
                    wig = Mathf.Sin(_clock * 1.1f + phase) * (s.species == FishSpecies.Glitter ? 0.45f : 0.22f);
                    break;
                }
            }
            float cw = Mathf.Cos(wig), sw = Mathf.Sin(wig);
            forward = new Vector2(s.dir.x * cw - s.dir.y * sw, s.dir.x * sw + s.dir.y * cw);
            return s.pos + right * o.x + s.dir * o.y;
        }

        float AmbientExtent(ref School s)
        {
            float L = AmbientLength(ref s) * 1.2f;
            float r;
            switch (Kinds[s.kind].form)
            {
                case Form.Line: r = s.count * L * 0.5f; break;
                case Form.Vee: r = (s.count + 1) * 0.5f * L; break;
                case Form.Loner: r = 1.5f * L; break;
                default: r = (0.55f + s.count * 0.055f) * 1.4f * Kinds[s.kind].spread * _viewScale + 0.3f; break;
            }
            return (r * (1f + s.scatter * 2.2f) + L) + 1f;
        }

        // ---- mesh ----

        void RebuildAmbient()
        {
            _ambientDrawn = 0;
            if (!Cozy || _schools.Length <= maxSchools) return;
            var cam = _viewOverride ? null : Camera.main;
            if (cam != null) GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            bool playing = Application.isPlaying;
            int vcap = _verts.Length, tcap = _tris.Length;
            for (int si = maxSchools; si < _schools.Length; si++)
            {
                ref School s = ref _schools[si];
                if (!s.active || !s.ambient) continue;
                ref AmbientKind k = ref Kinds[s.kind];
                var shape = FishShapes.All[k.shape];
                if (_vc + s.count * shape.VertexCount > vcap || _tc + s.count * shape.IndexCount > tcap) continue;
                if (cam != null)
                {
                    float e = AmbientExtent(ref s);
                    var b = new Bounds(new Vector3(s.pos.x, k.depth, s.pos.y), new Vector3(e * 2f, 1f + e * 0.3f, e * 2f));
                    if (!GeometryUtility.TestPlanesAABB(_planes, b)) continue;
                }
                _ambientDrawn++;

                float alpha = playing ? s.fade : 1f;
                Color body = s.color;
                _roleCols[FishShapes.Body] = body;
                _roleCols[FishShapes.Accent] = s.accent;
                _roleCols[FishShapes.Tail] = new Color(body.r * 0.75f, body.g * 0.75f, body.b * 0.8f, 1f);
                _roleCols[FishShapes.Nose] = new Color(body.r * 1.12f, body.g * 1.12f, body.b * 1.12f, 1f);
                _roleCols[FishShapes.Back] = new Color(body.r * 0.55f, body.g * 0.55f, body.b * 0.6f, 1f);
                _roleCols[FishShapes.Side] = Color.Lerp(body, Color.white, 0.35f);
                for (int c = 0; c < FishShapes.RoleCount; c++) _roleCols[c].a = alpha;

                bool glitter = s.species == FishSpecies.Glitter;
                float L = AmbientLength(ref s);
                var sv = shape.verts;
                var sr = shape.roles;
                var st = shape.tris;
                for (int i = 0; i < s.count; i++)
                {
                    Vector2 w = AmbientFishWorld(ref s, i, out Vector2 f2, out float phase, out float size);
                    float len = L * size;
                    float y = k.depth;
                    float pitch = 0f;
                    if (s.jumpT >= 0f && i == s.jumper)
                    {
                        float jt = s.jumpT;
                        y += Mathf.Sin(jt * Mathf.PI) * jumpHeight * k.jumpScale * _viewScale;
                        w += f2 * (jt * 0.6f * _viewScale);
                        pitch = Mathf.Cos(jt * Mathf.PI) * 0.9f;
                    }
                    float cp = Mathf.Cos(pitch), sp = Mathf.Sin(pitch);
                    Vector3 fwd = new Vector3(f2.x * cp, sp, f2.y * cp);
                    Vector3 right = new Vector3(f2.y, 0f, -f2.x);
                    Vector3 pos = new Vector3(w.x, y, w.y);
                    float wag = Mathf.Sin(_clock * k.wagFreq + phase * 3f) * 0.22f * (1f + s.scatter);
                    float flash = 1f;
                    if (glitter)
                    {
                        float fl = Mathf.Sin(_clock * 2.3f + phase * 5f);
                        if (fl > 0.8f) flash = 1f + (fl - 0.8f) * 9f;
                    }

                    int b = _vc;
                    for (int v = 0; v < sv.Length; v++)
                    {
                        Vector3 t = sv[v];
                        _verts[b + v] = pos + right * ((t.x + wag * t.z) * len) + fwd * (t.y * len);
                        Color col = _roleCols[sr[v]];
                        if (flash > 1f) { col.r *= flash; col.g *= flash; col.b *= flash; }
                        _cols[b + v] = col;
                    }
                    for (int q = 0; q < st.Length; q++) _tris[_tc + q] = b + st[q];
                    _vc += sv.Length;
                    _tc += st.Length;
                }
            }
        }
    }
}
