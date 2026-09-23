using System.Collections.Generic;
using Drift.Core;
using Drift.Islands;
using Drift.Tectonics;
using UnityEngine;

namespace Drift.Visuals
{
    // What a lightning strike on the player island costs, kept pure so the rule can be tested without a scene.
    // Nothing here stops or sinks the island: a hit brakes it for a moment and takes a little buoyancy.
    public static class StormStrike
    {
        // Below this the island is only at the fringe of the storm and the bolts stay over the open sea.
        public const float MinIntensity = 0.05f;

        // Does this bolt of a storm go for the island instead of the water? One roll (0..1) per bolt; the harder the
        // storm blows over the island, the likelier it is, and never while the cooldown after the last hit runs.
        public static bool AimsAtIsland(float intensityOnIsland, float cooldownLeft, float share, float roll) =>
            intensityOnIsland > MinIntensity && cooldownLeft <= 0f && share > 0f && roll < share * intensityOnIsland;

        // Where such a bolt comes down: somewhere on the island, the square root spreading the draws evenly over it.
        public static Vector2 Point(Vector2 island, float radius, float angle, float unit) =>
            island + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (Mathf.Max(0f, radius) * 0.75f * Mathf.Sqrt(Mathf.Clamp01(unit)));

        // A far-off, weak bolt costs half of what a full strike costs.
        public static float Scale(float strength) => 0.5f + 0.5f * Mathf.Clamp01(strength);
        public static float SlowSeconds(float seconds, float strength) => Mathf.Max(0f, seconds) * Scale(strength);
        public static float BuoyancyLoss(float loss, float strength) => Mathf.Max(0f, loss) * Scale(strength);
    }

    // Makes the storms of StormSystem visible from afar: an irregular cluster of dark, churning cloud clumps (the
    // puffs of the fair-weather clouds, see CloudShadows), rain shafts under them, rougher and darker water (the water
    // shader reads _DriftStorms) and lightning. Over the player island the
    // clouds and the rain keep an eye open, so a storm never hides the island itself.
    //
    // Lightning: storms strike the sea on their own (lightningRate); IslandLifeSystem still strikes islands at its
    // old rate (and may start a fire). Every strike goes through LifeEnvironment.ReportLightning, which this
    // component draws (bolt + scene flash via Flash / DayNightCycle) and AudioDirector turns into thunder.
    [ExecuteAlways]
    [DefaultExecutionOrder(140)]
    public class StormVisuals : MonoBehaviour
    {
        public Material stormMaterial;
        public Material boltMaterial;

        [Header("Wolken")]
        [Tooltip("Wolkenhöhe, falls es keine CloudShadows gibt (sonst liegen die Sturmwolken auf deren Wolkenschicht).")]
        [Range(4f, 30f)] public float cloudHeight = 13f;
        [Tooltip("Ausdehnung des Wolkenhaufens als Anteil des Sturmradius.")]
        [Range(0.3f, 1.6f)] public float cloudRadius = 1.05f;
        public Color cloudColor = new Color(0.24f, 0.26f, 0.31f);
        [Range(0f, 1f)] public float cloudAlpha = 0.88f;
        [Tooltip("Grundtempo, mit dem die Wolkenballen um das Sturmzentrum ziehen (rad/s, innen schneller als außen).")]
        [Range(0f, 0.3f)] public float cloudSpin = 0.06f;
        [Tooltip("Wolkenballen pro Sturm.")]
        [Range(4, 24)] public int clumpsPerStorm = 14;
        [Tooltip("Größe eines Wolkenballens als Anteil des Sturmradius.")]
        [Range(0.08f, 0.4f)] public float clumpSize = 0.22f;

        [Header("Regen")]
        public Color rainColor = new Color(0.62f, 0.68f, 0.78f);
        [Range(0f, 1f)] public float rainAlpha = 0.55f;
        [Tooltip("Anteil der Wolkenballen, unter denen es regnet.")]
        [Range(0f, 1f)] public float rainShare = 0.7f;
        [Tooltip("Gemütlich: Regen blendet zwischen diesen Abständen zur Kamera ein (Einheiten) – direkt vor der Kamera kein Regenvorhang.")]
        public Vector2 cozyRainFade = new Vector2(12f, 38f);
        [Tooltip("Gemütlich: Anteil der Regenstreifen, die nah an der Kamera noch fallen (0 = keine, 1 = alle).")]
        [Range(0f, 1f)] public float cozyRainNearShare = 0.2f;
        [Tooltip("Gemütlich: ab diesem Abstand zur Kamera regnet es wieder in allen Streifen.")]
        [Range(10f, 200f)] public float cozyRainFullAt = 70f;
        [Tooltip("Abenteuer: Regen blendet zwischen diesen Abständen zur Kamera ein.")]
        public Vector2 adventureRainFade = new Vector2(6f, 18f);

        [Header("Nachts")]
        [Tooltip("Nachts bekommen Sturmwolken einen mondhellen Rand und leuchten ab und zu von innen, damit sie sich vom dunklen Meer abheben (0 = aus).")]
        [Range(0f, 1f)] public float nightReadability = 1f;
        [Tooltip("Breite des weichen Lichthofs um einen Blitz (Vielfaches der Blitzbreite).")]
        [Range(1f, 10f)] public float boltGlowWidth = 5f;
        [Tooltip("Helligkeit des Lichthofs um einen Blitz bei Tag; nachts doppelt so hell.")]
        [Range(0f, 1f)] public float boltGlow = 0.22f;

        [Header("Handy")]
        [Tooltip("Sparmodus für schwache Grafikchips: weniger Wolkenballen und Wolkenbäusche pro Sturm, das feinste Rauschen fällt weg. Auto = auf dem Handy (und mit der Handy-Grafik im Editor).")]
        public RingReadability.LiteMode liteMode = RingReadability.LiteMode.Auto;
        [Tooltip("Wolkenballen pro Sturm im Sparmodus.")]
        [Range(4, 24)] public int liteClumpsPerStorm = 9;

        [Header("Auge über deiner Insel")]
        [Tooltip("So viel Abstand (über den Inselrand hinaus) bleibt über der Spielerinsel frei von Wolken und Regen.")]
        [Range(0f, 40f)] public float eyeMargin = 8f;

        [Header("Blitze")]
        [Tooltip("Blitze pro Sekunde ins Meer bei voller Sturmstärke.")]
        [Range(0f, 3f)] public float lightningRate = 0.7f;
        [Range(0.1f, 1.5f)] public float boltLife = 0.5f;
        [Range(0.05f, 1.5f)] public float boltWidth = 0.35f;
        public Color boltColor = new Color(0.8f, 0.87f, 1f);
        [Tooltip("Stärke des Aufleuchtens der Szene bei einem nahen Blitz.")]
        [Range(0f, 2f)] public float flashStrength = 0.8f;
        [Tooltip("Ab dieser Entfernung zur Kamera ist das Aufleuchten nicht mehr zu spüren.")]
        [Range(20f, 400f)] public float flashRange = 160f;

        [Header("Blitzeinschlag auf die Insel (Abenteuer)")]
        [Tooltip("Anteil der Blitze, die auf die Spielerinsel gehen, solange sie im Sturm fährt (0 = nie).")]
        [Range(0f, 1f)] public float playerStrikeShare = 0.55f;
        [Tooltip("Sekunden, die ein Treffer die Insel ausbremst.")]
        [Range(0f, 4f)] public float strikeSlowSeconds = 1.2f;
        [Tooltip("Auf so viel vom Tempo bremst ein Treffer (0,75 = ein Viertel langsamer).")]
        [Range(0.2f, 1f)] public float strikeSlowFactor = 0.75f;
        [Tooltip("Anteil des Auftriebs, den ein Treffer kostet – die Insel sackt leicht ab.")]
        [Range(0f, 0.2f)] public float strikeBuoyancyLoss = 0.03f;
        [Tooltip("Kürzeste Zeit zwischen zwei Treffern auf die Insel.")]
        [Range(0.5f, 20f)] public float strikeCooldown = 5f;

        public const int MaxStorms = 4;
        const int RainSegments = 12;

        // 0..1 scene flash of the brightest live bolt, read by DayNightCycle (and the shaders as _LightningFlash).
        public static float Flash { get; private set; }
        public int BoltsStruck { get; private set; }
        // Bolts that came down on the player island (they slow it and cost it a little buoyancy).
        public int PlayerStrikes { get; private set; }
        public float StrikeCooldownLeft => _strikeCooldown;
        // The storms shown this frame (x, z centre, radius, 0..1 intensity), for the minimap.
        public static int StormCount { get; private set; }
        static readonly Vector4[] Shown = new Vector4[MaxStorms];
        public static Vector4 StormAt(int i) => Shown[i];
        public int LiveBolts => _bolts.Count;

        static readonly int StormsId = Shader.PropertyToID("_DriftStorms");
        static readonly int StormCountId = Shader.PropertyToID("_DriftStormCount");
        static readonly int EyeId = Shader.PropertyToID("_StormEye");
        static readonly int FlashId = Shader.PropertyToID("_LightningFlash");
        static readonly int PuffLiteId = Shader.PropertyToID("_DriftPuffLite");
        static readonly int NightId = Shader.PropertyToID("_DriftStormNight");
        static readonly int RainNearId = Shader.PropertyToID("_DriftRainNear");
        static readonly StormClumpOrder FarFirst = new();

        struct Bolt
        {
            public Vector3 top, bottom;
            public float age, strength;
            public int seed;
        }

        readonly List<Bolt> _bolts = new();
        readonly Vector4[] _stormData = new Vector4[MaxStorms];
        readonly List<Vector3> _v = new();
        readonly List<Color> _c = new();
        readonly List<Vector4> _uv0 = new(), _uv1 = new();
        readonly List<int> _t = new();
        readonly float[] _strikeAcc = new float[MaxStorms];
        Mesh _cloudMesh, _boltMesh;
        MeshRenderer _cloudRenderer, _boltRenderer;
        float _rebuildTimer;
        int _shownStorms = -1;
        bool _boltsShown = true;
        // Whether the phone path is on (RingReadability.IsLite) and how many puffs the last rebuild made.
        public bool Lite { get; private set; }
        public int PuffsBuilt { get; private set; }
        System.Random _rnd = new System.Random(7717);
        Island _player;
        float _strikeCooldown;

        void OnEnable()
        {
            LifeEnvironment.LightningStruck += OnLightning;
        }

        void OnDisable()
        {
            LifeEnvironment.LightningStruck -= OnLightning;
            Flash = 0f;
            Shader.SetGlobalFloat(FlashId, 0f);
            Shader.SetGlobalFloat(StormCountId, 0f);
            Shader.SetGlobalFloat(NightId, 0f);
            StormCount = 0;
            _bolts.Clear();
        }

        void OnDestroy()
        {
            if (_cloudMesh != null) DestroyImmediate(_cloudMesh);
            if (_boltMesh != null) DestroyImmediate(_boltMesh);
        }

        void Update()
        {
            Step(Application.isPlaying ? Time.deltaTime : 0f);
        }

        public void Step(float dt)
        {
            EnsureObjects();
            var storms = StormSystem.Instance;
            int n = 0;
            if (storms != null)
            {
                var list = storms.Storms;
                for (int i = 0; i < list.Count && n < MaxStorms; i++)
                {
                    var s = list[i];
                    float intensity = s.strength * s.Envelope(storms.fadeTime);
                    if (intensity <= 0.001f) continue;
                    _stormData[n++] = new Vector4(s.center.x, s.center.y, s.radius, intensity);
                }
            }
            for (int i = n; i < MaxStorms; i++) _stormData[i] = Vector4.zero;
            System.Array.Copy(_stormData, Shown, MaxStorms);
            StormCount = n;
            Shader.SetGlobalVectorArray(StormsId, _stormData);
            Shader.SetGlobalFloat(StormCountId, n);

            if (_player == null)
            {
                var all = Island.All;
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && all[i].useKeyboardInput) { _player = all[i]; break; }
            }
            if (_player != null)
            {
                Vector2 p = _player.PlanarPosition;
                float r = _player.BoundingRadius;
                Shader.SetGlobalVector(EyeId, new Vector4(p.x, p.y, r + eyeMargin * 0.5f, r + eyeMargin * 1.5f + 2f));
            }

            _strikeCooldown = Mathf.Max(0f, _strikeCooldown - dt);
            if (dt > 0f) ScheduleStrikes(n, dt);

            bool lite = RingReadability.IsLite(liteMode, Application.isMobilePlatform, PipelineName());
            if (lite != Lite) _shownStorms = -1;
            Lite = lite;
            Shader.SetGlobalFloat(PuffLiteId, lite ? 1f : 0f);
            Shader.SetGlobalFloat(NightId, n > 0 ? LifeEnvironment.NightAmount * nightReadability : 0f);
            Shader.SetGlobalVector(RainNearId, GameModes.IsAdventure
                ? RingReadability.RainNear(adventureRainFade, 1f, adventureRainFade.y)
                : RingReadability.RainNear(cozyRainFade, cozyRainNearShare, cozyRainFullAt));

            // Nothing to show and nothing shown: no rebuild, no mesh upload.
            _rebuildTimer -= dt;
            if (n == 0 && _shownStorms == 0) _rebuildTimer = 0f;
            else if (n != _shownStorms || _rebuildTimer <= 0f)
            {
                _rebuildTimer = 0.1f;
                _shownStorms = n;
                BuildClouds(n);
            }
            StepBolts(dt);
        }

        // ------------------------------------------------------------ lightning

        void ScheduleStrikes(int n, float dt)
        {
            for (int i = 0; i < n; i++)
            {
                var d = _stormData[i];
                // Exponentially distributed waits (a Poisson process): lightningRate strikes per second at full
                // strength whatever the frame rate - a coin per frame would strike ten times as often at 500 fps.
                _strikeAcc[i] -= lightningRate * d.w * dt;
                if (_strikeAcc[i] > 0f) continue;
                _strikeAcc[i] = -Mathf.Log(1f - 0.999f * (float)_rnd.NextDouble());
                float strength = Mathf.Clamp01(0.6f + 0.4f * d.w);
                // Inside the storm the island itself is the tallest thing around: some of the bolts go for it.
                if (StormStrike.AimsAtIsland(IntensityOnPlayer(d), _strikeCooldown, playerStrikeShare, (float)_rnd.NextDouble()))
                {
                    Vector2 hit = StormStrike.Point(_player.PlanarPosition, _player.BoundingRadius,
                        (float)_rnd.NextDouble() * Mathf.PI * 2f, (float)_rnd.NextDouble());
                    LifeEnvironment.ReportLightning(new Vector3(hit.x, 0f, hit.y), strength);
                    continue;
                }
                if (TrySeaPoint(new Vector2(d.x, d.y), d.z * 0.8f, out Vector2 at))
                    LifeEnvironment.ReportLightning(new Vector3(at.x, 0f, at.y), strength);
            }
        }

        // How hard this storm blows over the player island (0 = the island is not in it). Only in Adventure: in the
        // cozy game a storm is weather, and nothing it does may cost the player anything.
        float IntensityOnPlayer(Vector4 storm)
        {
            if (_player == null || !GameModes.IsAdventure) return 0f;
            float d = Vector2.Distance(_player.PlanarPosition, new Vector2(storm.x, storm.y));
            if (d >= storm.z) return 0f;
            return storm.w * (1f - Mathf.SmoothStep(0.45f, 1f, d / Mathf.Max(1e-3f, storm.z)));
        }

        bool TrySeaPoint(Vector2 c, float r, out Vector2 p)
        {
            // On the ring a bolt past the rim would be folded flat onto the edge by the bend.
            var ring = GameModes.IsAdventure ? RingWorld.Active : null;
            bool onRing = ring != null && ring.IsApplied;
            RingGeometry g = onRing ? ring.Geometry : default;
            for (int t = 0; t < 6; t++)
            {
                float a = (float)_rnd.NextDouble() * Mathf.PI * 2f;
                p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r * Mathf.Sqrt((float)_rnd.NextDouble()));
                if (onRing && Mathf.Abs(p.x - g.centerX) > g.halfWidth - 3f) continue;
                if (!OverLand(p)) return true;
            }
            p = default;
            return false;
        }

        static bool OverLand(Vector2 p)
        {
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var isl = all[i];
                if (isl == null || !isl.isActiveAndEnabled) continue;
                float reach = isl.BoundingRadius + 1f;
                if ((isl.PlanarPosition - p).sqrMagnitude > reach * reach) continue;
                if (isl.SampleHeight(isl.ToLocal(p)) > -0.2f) return true;
            }
            return false;
        }

        void OnLightning(Vector3 at, float strength)
        {
            if (!isActiveAndEnabled) return;
            BoltsStruck++;
            StrikePlayer(new Vector2(at.x, at.z), strength);
            if (_bolts.Count >= 8) _bolts.RemoveAt(0);
            float jitter = 2.5f;
            _bolts.Add(new Bolt
            {
                bottom = at,
                top = new Vector3(at.x + ((float)_rnd.NextDouble() - 0.5f) * jitter * 2f, CloudBase() + 0.5f, at.z + ((float)_rnd.NextDouble() - 0.5f) * jitter * 2f),
                strength = Mathf.Clamp01(strength),
                seed = _rnd.Next(),
            });
        }

        // A bolt that came down on the player island: it staggers (a short brake, never a stop) and the island loses
        // a little buoyancy, so it rides lower for a while. Adventure only, and never twice within strikeCooldown.
        void StrikePlayer(Vector2 at, float strength)
        {
            if (_player == null || _strikeCooldown > 0f || !GameModes.IsAdventure) return;
            float reach = _player.BoundingRadius;
            if ((at - _player.PlanarPosition).sqrMagnitude > reach * reach) return;
            _strikeCooldown = strikeCooldown;
            PlayerStrikes++;
            _player.Stagger(StormStrike.SlowSeconds(strikeSlowSeconds, strength), strikeSlowFactor);
            _player.RemoveBuoyancy(StormStrike.BuoyancyLoss(strikeBuoyancyLoss, strength));
        }

        // Three quick pulses: the classic flicker of a return stroke.
        float Pulse(float age)
        {
            float t = age / Mathf.Max(0.05f, boltLife);
            if (t >= 1f) return 0f;
            float p = t < 0.12f ? 1f : t < 0.22f ? 0.25f : t < 0.36f ? 0.9f : t < 0.48f ? 0.2f : t < 0.62f ? 0.65f : 0.65f * (1f - (t - 0.62f) / 0.38f);
            return Mathf.Clamp01(p);
        }

        void StepBolts(float dt)
        {
            float flash = 0f;
            Vector3 camPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            Vector2 focus = _player != null ? _player.PlanarPosition : new Vector2(camPos.x, camPos.z);
            for (int i = _bolts.Count - 1; i >= 0; i--)
            {
                var b = _bolts[i];
                b.age += dt;
                if (b.age >= boltLife) { _bolts.RemoveAt(i); continue; }
                _bolts[i] = b;
                float d = Vector2.Distance(new Vector2(b.bottom.x, b.bottom.z), focus);
                float near = 1f - Mathf.Clamp01(d / Mathf.Max(1f, flashRange));
                flash = Mathf.Max(flash, Pulse(b.age) * b.strength * near * near * flashStrength);
            }
            Flash = Mathf.Clamp01(flash);
            Shader.SetGlobalFloat(FlashId, Flash);
            BuildBolts(camPos);
        }

        void BuildBolts(Vector3 camPos)
        {
            if (_bolts.Count == 0 && !_boltsShown) return;
            _v.Clear(); _c.Clear(); _uv0.Clear(); _uv1.Clear(); _t.Clear();
            float glow = boltGlow * (1f + LifeEnvironment.NightAmount);
            for (int i = 0; i < _bolts.Count; i++)
            {
                var b = _bolts[i];
                uint rnd = (uint)b.seed | 1u;
                float a = Pulse(b.age) * b.strength;
                if (a <= 0.01f) continue;
                Color col = boltColor * (1.5f * a);
                col.a = 1f;
                // The soft glow first (same seed, so it follows the same jagged path), the hard core over it.
                if (glow > 0.001f)
                {
                    uint g = rnd;
                    Color gc = col * glow;
                    gc.a = 1f;
                    AddBoltPath(ref g, b.top, b.bottom, boltWidth * boltGlowWidth, gc, camPos, 14, 1.4f, true);
                }
                AddBoltPath(ref rnd, b.top, b.bottom, boltWidth, col, camPos, 14, 1.4f);
                // Two thin side branches out of the upper half.
                for (int k = 0; k < 2; k++)
                {
                    float t = 0.2f + 0.35f * Next(ref rnd);
                    Vector3 from = Vector3.Lerp(b.top, b.bottom, t);
                    Vector3 to = from + new Vector3((Next(ref rnd) - 0.5f) * 7f, -(2f + 3f * Next(ref rnd)), (Next(ref rnd) - 0.5f) * 7f);
                    AddBoltPath(ref rnd, from, to, boltWidth * 0.45f, col * 0.7f, camPos, 6, 0.8f);
                }
            }
            _boltMesh.Clear();
            _boltsShown = _v.Count > 0;
            _boltRenderer.enabled = _boltsShown;
            if (_v.Count == 0) return;
            _boltMesh.SetVertices(_v);
            _boltMesh.SetColors(_c);
            _boltMesh.SetUVs(0, _uv0);
            _boltMesh.SetUVs(1, _uv1);
            _boltMesh.SetTriangles(_t, 0, false);
            _boltMesh.RecalculateBounds();
            var bounds = _boltMesh.bounds;
            bounds.Expand(30f);
            _boltMesh.bounds = bounds;
        }

        // A jagged ribbon facing the camera: points along the line with lateral kinks, two vertices each.
        // xorshift: the same bolt shape every frame of its short life, without allocating a Random per frame.
        static float Next(ref uint x)
        {
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            return (x & 0xFFFFFFu) / (float)0x1000000;
        }

        void AddBoltPath(ref uint rnd, Vector3 from, Vector3 to, float width, Color col, Vector3 camPos, int steps, float jag, bool soft = false)
        {
            int start = _v.Count;
            Vector3 dir = (to - from).normalized;
            Vector3 side0 = Vector3.Cross(dir, Vector3.up);
            if (side0.sqrMagnitude < 1e-4f) side0 = Vector3.right;
            side0.Normalize();
            Vector3 side1 = Vector3.Cross(dir, side0);
            Vector3 prev = from;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector3 p = Vector3.Lerp(from, to, t);
                if (i > 0 && i < steps)
                    p += (side0 * (Next(ref rnd) - 0.5f) + side1 * (Next(ref rnd) - 0.5f)) * (2f * jag);
                Vector3 seg = i == 0 ? dir : (p - prev).normalized;
                Vector3 view = (camPos - p).normalized;
                Vector3 w = Vector3.Cross(seg, view);
                if (w.sqrMagnitude < 1e-6f) w = side0;
                w = w.normalized * (width * (1f - 0.4f * t) * 0.5f);
                AddBoltVertex(p - w, col, -1f, soft);
                AddBoltVertex(p + w, col, 1f, soft);
                if (i > 0)
                {
                    int k = start + (i - 1) * 2;
                    _t.Add(k); _t.Add(k + 2); _t.Add(k + 1);
                    _t.Add(k + 1); _t.Add(k + 2); _t.Add(k + 3);
                }
                prev = p;
            }
        }

        void AddBoltVertex(Vector3 p, Color col, float side, bool soft)
        {
            _v.Add(p);
            _c.Add(col);
            _uv0.Add(new Vector4(0f, 0f, 0f, 2f));
            _uv1.Add(new Vector4(side, soft ? 1f : 0f, 0f, 0f));
        }

        // ------------------------------------------------------------ clouds and rain

        struct StormClump
        {
            public Vector2 stormCentre, centre, axis;
            public float stormRadius, radius, aspect, spin, seed, env, dist;
            public bool rain;
        }

        // Far clumps first. A class instance, not a lambda: List.Sort(Comparison) wraps the delegate in a new
        // comparer on every call.
        sealed class StormClumpOrder : IComparer<StormClump>
        {
            public int Compare(StormClump x, StormClump y) => y.dist.CompareTo(x.dist);
        }

        // Object.name allocates a string: read only when the pipeline asset changes.
        UnityEngine.Rendering.RenderPipelineAsset _pipeSeen;
        string _pipeName;
        bool _pipeRead;

        string PipelineName()
        {
            var rp = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            if (!_pipeRead || rp != _pipeSeen)
            {
                _pipeSeen = rp;
                _pipeName = rp != null ? rp.name : null;
                _pipeRead = true;
            }
            return _pipeName;
        }

        readonly List<StormClump> _clumps = new();

        float CloudBase()
        {
            var clouds = CloudShadows.Instance;
            return clouds != null && clouds.isActiveAndEnabled ? clouds.Height + 0.8f : cloudHeight;
        }

        // An irregular cluster of the same puffy clumps as the fair-weather clouds: packed densest near the centre, its
        // outline pushed in and out by noise (never a disc), every clump orbiting the centre at its own pace (inner
        // ones faster), so the mass churns instead of turning like a plate. Rain falls under most clumps.
        void BuildClouds(int n)
        {
            _v.Clear(); _c.Clear(); _uv0.Clear(); _uv1.Clear(); _t.Clear();
            _clumps.Clear();
            Vector3 camPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            int clumps = RingReadability.StormClumps(clumpsPerStorm, Lite, liteClumpsPerStorm);
            // Fewer, fuller clumps in the lite path, so the mass keeps its size.
            float liteGrow = clumps < clumpsPerStorm ? Mathf.Sqrt(clumpsPerStorm / (float)Mathf.Max(1, clumps)) : 1f;
            for (int i = 0; i < n; i++)
            {
                var d = _stormData[i];
                var c = new Vector2(d.x, d.y);
                float env = Mathf.Clamp01(d.w);
                int seed = Mathf.FloorToInt(d.x * 13.1f + d.y * 7.7f);
                // xorshift instead of a System.Random per storm and rebuild (ten allocations a second).
                uint rnd = unchecked((uint)seed * 2654435761u) | 1u;
                float dir = (seed & 1) == 0 ? 1f : -1f;
                float phase = Next(ref rnd) * Mathf.PI * 2f;
                float r = d.z * cloudRadius;
                for (int j = 0; j < clumps; j++)
                {
                    float a = phase + j * 2.3999632f + (Next(ref rnd) - 0.5f) * 0.5f;
                    float outline = 0.62f + 0.55f * Mathf.PerlinNoise(seed * 0.013f + Mathf.Cos(a) * 0.9f + 3f, seed * 0.017f + Mathf.Sin(a) * 0.9f + 3f);
                    float u = Mathf.Sqrt((j + 0.5f) / clumps);
                    float dist = r * 0.8f * outline * u * (0.85f + 0.3f * Next(ref rnd));
                    float big = 1f - 0.45f * u;
                    float ax = Next(ref rnd) * Mathf.PI;
                    var cl = new StormClump
                    {
                        stormCentre = c,
                        stormRadius = d.z,
                        centre = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * dist,
                        radius = d.z * clumpSize * big * (0.8f + 0.4f * Next(ref rnd)) * (0.55f + 0.45f * env) * liteGrow,
                        axis = new Vector2(Mathf.Cos(ax), Mathf.Sin(ax)),
                        aspect = 1f + 0.4f * Next(ref rnd),
                        spin = cloudSpin * dir * (1.35f - 0.7f * u) * (0.8f + 0.4f * Next(ref rnd)),
                        seed = Next(ref rnd) * 97f,
                        env = env,
                        rain = Next(ref rnd) < rainShare,
                    };
                    Vector3 wc = new Vector3(cl.centre.x, 0f, cl.centre.y);
                    cl.dist = (wc - camPos).sqrMagnitude;
                    _clumps.Add(cl);
                }
            }
            // Far clumps first: the mesh is drawn in order and alpha-blended.
            _clumps.Sort(FarFirst);

            float baseY = CloudBase();
            int puffs = 0;
            for (int ci = 0; ci < _clumps.Count; ci++)
            {
                var cl = _clumps[ci];
                if (cl.rain)
                {
                    Color rc = rainColor;
                    rc.a = rainAlpha * cl.env;
                    AddRain(cl, cl.radius * 0.55f, baseY + 0.2f, rc);
                }
                for (int k = 0; k < CloudShadows.PuffsPerClump; k++)
                {
                    if (!RingReadability.KeepStormPuff(k, Lite)) continue;
                    puffs++;
                    CloudField.PuffLayout(k, cl.seed, cl.axis, cl.aspect, cl.radius, out Vector2 off, out float size, out float h);
                    // Storm towers: taller tops, fuller puffs.
                    h *= k >= 6 ? 2.2f : 1.4f;
                    size *= 1.15f * RingReadability.StormPuffGrow(k, Lite);
                    Color col = cloudColor * (0.9f + 0.2f * (h / Mathf.Max(0.01f, cl.radius)));
                    col.a = cloudAlpha * cl.env;
                    var p = new Vector3(cl.centre.x + off.x, baseY + h, cl.centre.y + off.y);
                    var uv0 = new Vector4(cl.stormCentre.x, cl.stormCentre.y, cl.spin, 0f);
                    int s = _v.Count;
                    for (int q = 0; q < 4; q++)
                        AddVertex(p, col, uv0, new Vector4((q & 1) == 0 ? -1f : 1f, (q & 2) == 0 ? -1f : 1f, size, cl.seed + k * 0.31f));
                    _t.Add(s); _t.Add(s + 2); _t.Add(s + 1);
                    _t.Add(s + 1); _t.Add(s + 2); _t.Add(s + 3);
                }
            }

            PuffsBuilt = puffs;
            _cloudMesh.Clear();
            _cloudRenderer.enabled = _v.Count > 0;
            if (_v.Count == 0) return;
            _cloudMesh.SetVertices(_v);
            _cloudMesh.SetColors(_c);
            _cloudMesh.SetUVs(0, _uv0);
            _cloudMesh.SetUVs(1, _uv1);
            _cloudMesh.SetTriangles(_t, 0, false);
            _cloudMesh.RecalculateBounds();
            var bounds = _cloudMesh.bounds;
            bounds.Expand(40f);
            _cloudMesh.bounds = bounds;
        }

        // A narrow rain shaft under one clump: an open cylinder, a little wider at the sea than under the cloud.
        void AddRain(in StormClump cl, float r, float top, Color col)
        {
            int start = _v.Count;
            var uv0 = new Vector4(cl.stormCentre.x, cl.stormCentre.y, cl.spin, 1f);
            float lanes = Mathf.Max(10f, Mathf.Round(2f * Mathf.PI * r / 0.45f));
            for (int k = 0; k <= RainSegments; k++)
            {
                float u = k / (float)RainSegments;
                float a = u * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 lo = cl.centre + dir * (r * 1.2f);
                Vector2 hi = cl.centre + dir * r;
                AddVertex(new Vector3(lo.x, -0.2f, lo.y), col, uv0, new Vector4(r * 1.2f, u * lanes, 0f, top));
                AddVertex(new Vector3(hi.x, top - 0.4f, hi.y), col, uv0, new Vector4(r * 1.2f, u * lanes, top, top));
            }
            for (int k = 0; k < RainSegments; k++)
            {
                int a = start + k * 2;
                _t.Add(a); _t.Add(a + 1); _t.Add(a + 2);
                _t.Add(a + 1); _t.Add(a + 3); _t.Add(a + 2);
            }
        }

        void AddVertex(Vector3 p, Color col, Vector4 uv0, Vector4 uv1)
        {
            _v.Add(p);
            _c.Add(col);
            _uv0.Add(uv0);
            _uv1.Add(uv1);
        }

        void EnsureObjects()
        {
            if (_cloudRenderer == null) _cloudRenderer = Child("StormClouds", stormMaterial, ref _cloudMesh);
            if (_boltRenderer == null) _boltRenderer = Child("StormBolts", boltMaterial, ref _boltMesh);
        }

        MeshRenderer Child(string name, Material mat, ref Mesh mesh)
        {
            var t = transform.Find(name);
            GameObject go = t != null ? t.gameObject : new GameObject(name);
            go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            // No "??" on components: a missing one is a fake-null object in the Editor, not null.
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null) mf = go.AddComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) mr = go.AddComponent<MeshRenderer>();
            if (mesh == null)
            {
                mesh = mf.sharedMesh != null && mf.sharedMesh.name == name ? mf.sharedMesh : new Mesh { name = name, hideFlags = HideFlags.DontSave };
                mesh.MarkDynamic();
            }
            mf.sharedMesh = mesh;
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }
    }
}
