using System;
using Drift.Core;
using Drift.Islands;
using Drift.Tectonics;
using UnityEngine;

namespace Drift.Visuals
{
    public enum WorldEventKind { None, Rainbow, WhaleMigration, Aurora, Meteors }

    // Everything the scheduler needs to know about the world right now. Pure data, so the pacing can be tested
    // without a scene.
    public struct WorldEventConditions
    {
        // sin(sun elevation): 1 = zenith, 0 = on the horizon, negative = below it.
        public float sunHeight;
        // How dark the sky is (DayNightCycle.StarVisibility) and how much moonlight washes it out (0 new moon).
        public float stars;
        public float moon;
        // Storm at the player and the distance to the nearest live storm centre (float.MaxValue = none anywhere).
        public float storm;
        public float stormDistance;
        // Seconds since the player's own weather last cleared up; float.MaxValue while it never rained.
        public float sinceStorm;
        public bool openWater;
        public bool adventure;
    }

    // Rare, time-limited spectacles: a rainbow over the sea after a storm, a line of whales migrating past, the
    // northern lights and a meteor shower at night. Only one at a time, each with its own weather / time-of-day
    // window, a cooldown afterwards and a much longer one before the same one comes round again.
    //
    // Pure: the gap between two spectacles is `cooldown` plus an exponentially distributed wait with the mean
    // 60 / chancePerMinute seconds. The wait only runs while at least one spectacle is possible, so a night-only
    // event does not "expire" unseen during the day.
    public sealed class WorldEventScheduler
    {
        public float chancePerMinute = 0.4f;
        public float cooldownSeconds = 210f;
        public float sameKindCooldown = 480f;
        public float firstWaitMin = 25f;
        public float firstWaitMax = 90f;

        // Windows. Rainbow: the sun has to be low enough for the bow (42 degrees round the antisolar point) to stand
        // above the horizon, and high enough to light it; the player must be out of the rain but near it.
        public float rainbowSunMin = 0.3f;
        public float rainbowSunMax = 0.6f;
        public float rainbowStormMax = 0.32f;
        public float rainbowStormRange = 220f;
        public float rainbowAfterStorm = 70f;
        public float whaleNightMax = 0.85f;
        public float whaleStormMax = 0.4f;
        public float auroraStars = 0.85f;
        public float auroraMoonMax = 0.75f;
        public float auroraStormMax = 0.35f;
        public float meteorStars = 0.95f;
        public float meteorMoonMax = 0.6f;
        public float meteorStormMax = 0.3f;

        public float rainbowWeight = 1f;
        public float whaleWeight = 1f;
        public float auroraWeight = 1f;
        public float meteorWeight = 0.75f;

        uint _seed;
        float _wait;
        float _clock;
        readonly float[] _lastOf = new float[5];
        int _rolls;

        public float Clock => _clock;
        public float Wait => _wait;
        public WorldEventKind Active { get; private set; }
        public float ActiveLeft { get; private set; }
        public WorldEventKind Last { get; private set; }
        public int Fired { get; private set; }
        public bool Busy => Active != WorldEventKind.None;

        public WorldEventScheduler(uint seed)
        {
            Reset(seed);
        }

        public void Reset(uint seed)
        {
            _seed = seed == 0u ? 1u : seed;
            _clock = 0f;
            _rolls = 0;
            Fired = 0;
            Active = WorldEventKind.None;
            ActiveLeft = 0f;
            Last = WorldEventKind.None;
            for (int i = 0; i < _lastOf.Length; i++) _lastOf[i] = -1e9f;
            _wait = Mathf.Lerp(firstWaitMin, Mathf.Max(firstWaitMin, firstWaitMax), SeaMath.Rand(_seed, 3));
        }

        public float SecondsSince(WorldEventKind k) => _clock - _lastOf[(int)k];

        public bool Allowed(WorldEventKind k, in WorldEventConditions c)
        {
            if (k == WorldEventKind.None) return false;
            if (_clock - _lastOf[(int)k] < sameKindCooldown) return false;
            switch (k)
            {
                case WorldEventKind.Rainbow:
                    return c.sunHeight >= rainbowSunMin && c.sunHeight <= rainbowSunMax && c.storm <= rainbowStormMax
                        && (c.stormDistance <= rainbowStormRange || c.sinceStorm <= rainbowAfterStorm);
                case WorldEventKind.WhaleMigration:
                    return !c.adventure && c.openWater && c.storm <= whaleStormMax && c.stars <= whaleNightMax;
                case WorldEventKind.Aurora:
                    return c.stars >= auroraStars && c.moon <= auroraMoonMax && c.storm <= auroraStormMax;
                case WorldEventKind.Meteors:
                    return c.stars >= meteorStars && c.moon <= meteorMoonMax && c.storm <= meteorStormMax;
                default:
                    return false;
            }
        }

        public bool AnyAllowed(in WorldEventConditions c)
        {
            for (int k = 1; k <= 4; k++) if (Allowed((WorldEventKind)k, c)) return true;
            return false;
        }

        float Weight(WorldEventKind k)
        {
            switch (k)
            {
                case WorldEventKind.Rainbow: return rainbowWeight;
                case WorldEventKind.WhaleMigration: return whaleWeight;
                case WorldEventKind.Aurora: return auroraWeight;
                default: return meteorWeight;
            }
        }

        // Weighted pick among what the conditions allow; the one that just ran is skipped while anything else fits.
        public WorldEventKind Pick(in WorldEventConditions c, float roll)
        {
            int options = 0;
            for (int k = 1; k <= 4; k++) if (Allowed((WorldEventKind)k, c)) options++;
            if (options == 0) return WorldEventKind.None;
            float total = 0f;
            for (int k = 1; k <= 4; k++)
            {
                var kind = (WorldEventKind)k;
                if (!Allowed(kind, c) || (options > 1 && kind == Last)) continue;
                total += Mathf.Max(0f, Weight(kind));
            }
            if (total <= 0f) return WorldEventKind.None;
            float r = Mathf.Clamp01(roll) * total;
            WorldEventKind pick = WorldEventKind.None;
            for (int k = 1; k <= 4; k++)
            {
                var kind = (WorldEventKind)k;
                if (!Allowed(kind, c) || (options > 1 && kind == Last)) continue;
                pick = kind;
                r -= Mathf.Max(0f, Weight(kind));
                if (r < 0f) break;
            }
            return pick;
        }

        // Advances the pacing and the running spectacle. Returns the one to start now (None most of the time); the
        // caller answers with Confirm once it really placed it, or leaves it and the scheduler retries shortly.
        public WorldEventKind Tick(float dt, in WorldEventConditions c)
        {
            _clock += dt;
            if (Active != WorldEventKind.None)
            {
                ActiveLeft -= dt;
                if (ActiveLeft <= 0f) End();
                return WorldEventKind.None;
            }
            if (!AnyAllowed(c)) return WorldEventKind.None;
            _wait -= dt;
            if (_wait > 0f) return WorldEventKind.None;
            _wait = 4f;
            return Pick(c, SeaMath.Rand(_seed, 500 + _rolls++ * 13));
        }

        public void Confirm(WorldEventKind k, float lifetime)
        {
            if (k == WorldEventKind.None) return;
            Active = k;
            ActiveLeft = Mathf.Max(1f, lifetime);
            Last = k;
            _lastOf[(int)k] = _clock;
            Fired++;
        }

        public void End()
        {
            Active = WorldEventKind.None;
            ActiveLeft = 0f;
            _wait = Mathf.Max(0f, cooldownSeconds) + NextWait();
        }

        // Exponential wait with mean 60 / chancePerMinute, so the chance really is "chancePerMinute per minute"
        // of a minute in which a spectacle is possible, independent of the frame rate.
        float NextWait()
        {
            float rate = Mathf.Max(0.0001f, chancePerMinute) / 60f;
            float u = Mathf.Clamp(SeaMath.Rand(_seed, 900 + _rolls++ * 7), 0.0005f, 0.9995f);
            return -Mathf.Log(1f - u) / rate;
        }
    }

    // The director: watches the weather, the time of day and the player, starts one spectacle at a time and takes
    // it away again. The spectacles themselves live in RainbowArc (a mesh arc drawn with the sky),
    // SeaLifeSystem.StartWhaleMigration (a line of whales) and two additions to DriftSky.hlsl (northern lights,
    // meteor shower) that are switched off completely while nothing runs.
    [ExecuteAlways]
    [DefaultExecutionOrder(216)]
    public class WorldEvents : MonoBehaviour
    {
        public WaterFeedback water;
        public SeaLifeSystem seaLife;
        public DayNightCycle dayNight;
        public Material rainbowMaterial;
        public int seed = 41;

        [Header("Takt")]
        [Tooltip("Wie oft pro Minute ein Schauspiel beginnt, solange Wetter und Tageszeit überhaupt eines zulassen.")]
        [Range(0f, 4f)] public float chancePerMinute = 0.4f;
        [Tooltip("Ruhepause (s) nach einem Schauspiel, bevor das nächste beginnen darf.")]
        [Range(0f, 600f)] public float cooldownSeconds = 210f;
        [Tooltip("So lange (s) dauert es mindestens, bis dasselbe Schauspiel wieder vorkommt.")]
        [Range(0f, 1800f)] public float sameKindCooldown = 480f;
        [Tooltip("Frühestes und spätestes erstes Schauspiel (s) nach dem Start eines Durchgangs.")]
        public Vector2 firstWait = new Vector2(25f, 90f);

        [Header("Dauer")]
        [Tooltip("So lange steht der Regenbogen (s).")]
        [Range(15f, 300f)] public float rainbowSeconds = 80f;
        [Tooltip("So lange zieht die Walwanderung vorbei (s).")]
        [Range(20f, 300f)] public float migrationSeconds = 95f;
        [Tooltip("So lange leuchtet das Polarlicht (s).")]
        [Range(20f, 400f)] public float auroraSeconds = 120f;
        [Tooltip("So lange fällt der Meteorschauer (s).")]
        [Range(15f, 300f)] public float meteorSeconds = 70f;
        [Tooltip("Ein- und Ausblendzeit (s) am Anfang und Ende jedes Schauspiels.")]
        [Range(1f, 20f)] public float fadeSeconds = 6f;

        [Header("Häufigkeit der einzelnen Schauspiele")]
        [Range(0f, 3f)] public float rainbowWeight = 1f;
        [Range(0f, 3f)] public float whaleWeight = 1f;
        [Range(0f, 3f)] public float auroraWeight = 1f;
        [Range(0f, 3f)] public float meteorWeight = 0.75f;

        [Header("Regenbogen")]
        [Tooltip("Der Bogen steht dem Sonnenstand gegenüber; so weit (0..1) dreht er sich zusätzlich zum Sturm hin.")]
        [Range(0f, 1f)] public float rainbowStormBias = 0.35f;
        [Tooltip("So weit (u) darf der Sturm entfernt sein, damit ein Regenbogen entsteht.")]
        [Range(40f, 400f)] public float rainbowStormRange = 220f;
        [Tooltip("So lange (s) nach dem eigenen Regen kann noch ein Regenbogen kommen.")]
        [Range(0f, 300f)] public float rainbowAfterStorm = 70f;
        [Tooltip("Leuchtkraft des Bogens.")]
        [Range(0f, 2f)] public float rainbowBrightness = 1f;
        [Tooltip("Zweiter, blasserer Bogen außen herum (Nebenregenbogen).")]
        [Range(0f, 1f)] public float rainbowSecondary = 0.42f;

        [Header("Walwanderung")]
        [Tooltip("So viele Wal-Gruppen ziehen in der Reihe.")]
        [Range(2, 6)] public int migrationGroups = 4;
        [Tooltip("Abstand (u) zwischen zwei Gruppen der Reihe.")]
        [Range(10f, 60f)] public float migrationSpacing = 22f;
        [Tooltip("Seitlicher Abstand (u), in dem die Reihe am Kurs der Insel vorbeizieht.")]
        [Range(10f, 90f)] public float migrationLateral = 32f;

        [Header("Polarlicht")]
        [Tooltip("Helligkeit des Polarlichts.")]
        [Range(0f, 2f)] public float auroraBrightness = 1f;
        [Tooltip("Wie hoch der Fuß des Vorhangs über dem Horizont steht (klein = er steht auf dem Meer).")]
        [Range(0.01f, 0.3f)] public float auroraHeight = 0.04f;
        [Tooltip("Höhe des Vorhangs: wie weit er von seinem Fuß aus in den Himmel reicht.")]
        [Range(0.05f, 0.9f)] public float auroraThickness = 0.26f;
        [Tooltip("Wie schnell der Vorhang wabert.")]
        [Range(0f, 1f)] public float auroraDrift = 0.28f;
        public Color auroraLow = new Color(0.32f, 1f, 0.62f);
        public Color auroraHigh = new Color(0.58f, 0.36f, 0.95f);
        [Tooltip("Wie stark das Polarlicht das Umgebungslicht einfärbt (0 = gar nicht).")]
        [Range(0f, 0.5f)] public float auroraAmbient = 0.1f;

        [Header("Meteorschauer")]
        [Tooltip("Meteore pro Minute auf dem Höhepunkt des Schauers.")]
        [Range(2f, 90f)] public float meteorsPerMinute = 45f;
        [Tooltip("Helligkeit der Meteore.")]
        [Range(0f, 3f)] public float meteorBrightness = 1.15f;
        [Tooltip("Länge der Schweife (Grad am Himmel).")]
        [Range(2f, 40f)] public float meteorLength = 13f;

        public const int MaxMeteors = 3;

        // A spectacle began / ended. `at` is a world point to look at, or Vector3.zero when it fills the whole sky
        // (northern lights, meteors). Subscribers: toasts, a soft Tilda remark, the run journal, an edge arrow.
        public static event Action<WorldEventKind, Vector3> Started;
        public static event Action<WorldEventKind> Ended;

        public static WorldEventKind Running { get; private set; }
        public static float RunningLeft { get; private set; }
        public static int StartedTotal { get; private set; }
        // Colour the northern lights add to the ambient light; DayNightCycle adds it like a lightning flash.
        public static Color AmbientBoost { get; private set; }

        public static string German(WorldEventKind k)
        {
            switch (k)
            {
                case WorldEventKind.Rainbow: return "Regenbogen";
                case WorldEventKind.WhaleMigration: return "Walwanderung";
                case WorldEventKind.Aurora: return "Polarlicht";
                case WorldEventKind.Meteors: return "Meteorschauer";
                default: return "";
            }
        }

        // What a journal entry / toast says once the player has seen one ("Regenbogen gesehen").
        public static string SeenLine(WorldEventKind k) => k == WorldEventKind.None ? "" : German(k) + " gesehen";

        static readonly int AuroraId = Shader.PropertyToID("_SkyAurora");
        static readonly int AuroraDirId = Shader.PropertyToID("_SkyAuroraDir");
        static readonly int AuroraRightId = Shader.PropertyToID("_SkyAuroraRight");
        static readonly int AuroraLowId = Shader.PropertyToID("_SkyAuroraLow");
        static readonly int AuroraHighId = Shader.PropertyToID("_SkyAuroraHigh");
        static readonly int MeteorParamsId = Shader.PropertyToID("_SkyMeteorParams");
        static readonly int MeteorsId = Shader.PropertyToID("_SkyMeteors");
        static readonly int MeteorDirsId = Shader.PropertyToID("_SkyMeteorDirs");

        WorldEventScheduler _sched;
        RainbowArc _rainbow;
        Island _player;
        float _sinceStorm = float.MaxValue;
        float _strength, _age;
        Vector3 _focus;
        bool _hasFocus;
        readonly Vector4[] _meteors = new Vector4[MaxMeteors];
        readonly Vector4[] _meteorDirs = new Vector4[MaxMeteors];
        Vector3 _radiant = Vector3.up;
        float _meteorAcc;
        uint _meteorSeed = 1u;
        int _meteorNext, _meteorCount;

        public WorldEventScheduler Scheduler => _sched;
        public WorldEventKind Active => _sched != null ? _sched.Active : WorldEventKind.None;
        public float ActiveLeft => _sched != null ? _sched.ActiveLeft : 0f;
        // 0..1 fade of the running spectacle (rises at the start, falls at the end).
        public float Strength => _strength;
        public WorldEventConditions LastConditions { get; private set; }
        public bool TryGetFocus(out Vector3 world) { world = _focus; return _hasFocus && _sched != null && _sched.Busy; }
        // Where in the sky a sky-wide spectacle is: the middle of the aurora's curtain, the radiant of the shower,
        // the middle of the rainbow's arc. Zero-length while nothing runs.
        public Vector3 SkyDirection => _sched != null && _sched.Active == WorldEventKind.Rainbow ? ArcAxis()
            : _sched != null && _sched.Busy ? _radiant : Vector3.zero;
        public RainbowArc Rainbow => _rainbow;

        void OnEnable()
        {
            Resolve();
            EnsureScheduler();
            EnsureRainbow();
            PushSky();
        }

        void OnDisable()
        {
            StopAll();
            if (_rainbow != null) _rainbow.Destroy();
            _rainbow = null;
        }

        void OnDestroy()
        {
            if (_rainbow != null) _rainbow.Destroy();
            _rainbow = null;
        }

        void Resolve()
        {
            if (water == null) water = GetComponent<WaterFeedback>();
            if (water == null) water = FindAnyObjectByType<WaterFeedback>();
            if (seaLife == null) seaLife = GetComponent<SeaLifeSystem>();
            if (seaLife == null) seaLife = FindAnyObjectByType<SeaLifeSystem>();
            if (dayNight == null) dayNight = GetComponent<DayNightCycle>();
            if (dayNight == null) dayNight = FindAnyObjectByType<DayNightCycle>();
        }

        void EnsureRainbow()
        {
            _rainbow ??= new RainbowArc();
            _rainbow.parent = transform;
        }

        void EnsureScheduler()
        {
            _sched ??= new WorldEventScheduler((uint)seed * 2654435761u + 17u);
            _sched.chancePerMinute = chancePerMinute;
            _sched.cooldownSeconds = cooldownSeconds;
            _sched.sameKindCooldown = sameKindCooldown;
            _sched.firstWaitMin = firstWait.x;
            _sched.firstWaitMax = Mathf.Max(firstWait.x, firstWait.y);
            _sched.rainbowStormRange = rainbowStormRange;
            _sched.rainbowAfterStorm = rainbowAfterStorm;
            _sched.rainbowWeight = rainbowWeight;
            _sched.whaleWeight = whaleWeight;
            _sched.auroraWeight = auroraWeight;
            _sched.meteorWeight = meteorWeight;
        }

        void Update()
        {
            Tick(Application.isPlaying ? Time.deltaTime : 0f);
        }

        public void Tick(float dt)
        {
            if (water == null || seaLife == null || dayNight == null) Resolve();
            EnsureScheduler();
            EnsureRainbow();

            var c = Conditions(dt);
            LastConditions = c;
            var before = _sched.Active;
            var start = _sched.Tick(dt, c);
            if (before != WorldEventKind.None && _sched.Active == WorldEventKind.None) Finish(before);
            if (start != WorldEventKind.None) Begin(start);

            AdvanceActive(dt);
            PushSky();
        }

        WorldEventConditions Conditions(float dt)
        {
            _player = water != null ? water.FindPlayer() : null;
            float storm = water != null ? water.Storm : LifeEnvironment.Storm;
            if (storm > 0.12f) _sinceStorm = 0f;
            else if (_sinceStorm < 1e8f) _sinceStorm += dt;

            float stormDist = float.MaxValue;
            var storms = StormSystem.Instance;
            if (storms != null && _player != null && storms.NearestStorm(_player.PlanarPosition, out StormData s))
                stormDist = (s.center - _player.PlanarPosition).magnitude;

            float stars = dayNight != null ? dayNight.StarVisibility : LifeEnvironment.NightAmount;
            float moon = 0f;
            if (dayNight != null) moon = SkyMath.MoonLit(dayNight.moonPhase) * Mathf.Clamp01(dayNight.MoonDirection.y * 3f + 0.2f);

            return new WorldEventConditions
            {
                sunHeight = dayNight != null ? dayNight.SunHeight : 0.5f,
                stars = stars,
                moon = Mathf.Clamp01(moon),
                storm = storm,
                stormDistance = stormDist,
                sinceStorm = _sinceStorm,
                openWater = OpenWater(),
                adventure = GameModes.IsAdventure
            };
        }

        bool OpenWater()
        {
            if (_player == null) return false;
            Vector2 pos = _player.PlanarPosition;
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var isl = all[i];
                if (isl == null || isl == _player || !isl.isActiveAndEnabled || isl.IsSunk) continue;
                float reach = isl.BoundingRadius + _player.BoundingRadius + 30f;
                if ((isl.PlanarPosition - pos).sqrMagnitude < reach * reach) return false;
            }
            return true;
        }

        float LifetimeOf(WorldEventKind k)
        {
            switch (k)
            {
                case WorldEventKind.Rainbow: return rainbowSeconds;
                case WorldEventKind.WhaleMigration: return migrationSeconds;
                case WorldEventKind.Aurora: return auroraSeconds;
                default: return meteorSeconds;
            }
        }

        // Starts a spectacle now, whatever the pacing says (verification, debug menu). False when there was no room.
        public bool Trigger(WorldEventKind k)
        {
            Resolve();
            EnsureScheduler();
            EnsureRainbow();
            if (_sched.Busy) StopAll();
            _player = water != null ? water.FindPlayer() : null;
            return Begin(k);
        }

        bool Begin(WorldEventKind k)
        {
            _hasFocus = false;
            _focus = Vector3.zero;
            switch (k)
            {
                case WorldEventKind.Rainbow:
                {
                    if (dayNight == null) return false;
                    _rainbow.Begin(ArcAxis(), rainbowMaterial);
                    break;
                }
                case WorldEventKind.WhaleMigration:
                {
                    if (seaLife == null || _player == null) return false;
                    Vector2 course = PlayerCourse();
                    float side = SeaMath.Rand(SeaMath.Hash((uint)seed, (uint)_sched.Fired + 3u), 1) < 0.5f ? 1f : -1f;
                    int placed = seaLife.StartWhaleMigration(course, side, migrationGroups, migrationSpacing,
                        migrationLateral, migrationSeconds);
                    if (placed <= 0) return false;
                    Vector2 mid = seaLife.MigrationCenter;
                    _focus = new Vector3(mid.x, 0f, mid.y);
                    _hasFocus = true;
                    break;
                }
                case WorldEventKind.Aurora:
                case WorldEventKind.Meteors:
                {
                    uint h = SeaMath.Hash((uint)seed + 11u, (uint)(_sched.Fired * 31 + (int)k));
                    float a = SeaMath.Rand(h, 1) * Mathf.PI * 2f;
                    if (k == WorldEventKind.Aurora)
                    {
                        _radiant = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    }
                    else
                    {
                        // A low radiant: from the chase camera only the strip just above the horizon is sky at all.
                        float el = Mathf.Lerp(0.1f, 0.34f, SeaMath.Rand(h, 2));
                        float ch = Mathf.Sqrt(Mathf.Max(0f, 1f - el * el));
                        _radiant = new Vector3(Mathf.Cos(a) * ch, el, Mathf.Sin(a) * ch);
                        _meteorSeed = h | 1u;
                        _meteorAcc = 0f;
                        _meteorNext = 0;
                        for (int i = 0; i < MaxMeteors; i++) _meteors[i] = Vector4.zero;
                    }
                    break;
                }
                default:
                    return false;
            }
            _sched.Confirm(k, LifetimeOf(k));
            _age = 0f;
            _strength = 0f;
            Running = k;
            RunningLeft = _sched.ActiveLeft;
            StartedTotal++;
            Started?.Invoke(k, _focus);
            return true;
        }

        void Finish(WorldEventKind k)
        {
            switch (k)
            {
                case WorldEventKind.Rainbow: if (_rainbow != null) _rainbow.End(); break;
                case WorldEventKind.WhaleMigration: if (seaLife != null) seaLife.EndWhaleMigration(); break;
            }
            _strength = 0f;
            _hasFocus = false;
            Running = WorldEventKind.None;
            RunningLeft = 0f;
            AmbientBoost = Color.black;
            Ended?.Invoke(k);
        }

        // Ends whatever runs without the graceful fade (disable, mode change).
        public void StopAll()
        {
            var k = _sched != null ? _sched.Active : WorldEventKind.None;
            if (_sched != null) _sched.End();
            if (k != WorldEventKind.None) Finish(k);
            if (_rainbow != null) _rainbow.End();
            _strength = 0f;
            PushSky();
        }

        void AdvanceActive(float dt)
        {
            var k = _sched.Active;
            if (k == WorldEventKind.None)
            {
                _strength = Mathf.MoveTowards(_strength, 0f, dt / Mathf.Max(0.5f, fadeSeconds));
                Running = WorldEventKind.None;
                RunningLeft = 0f;
                AmbientBoost = Color.black;
                if (_rainbow != null) _rainbow.Step(dt, _strength, ArcAxis(), rainbowMaterial, rainbowBrightness, rainbowSecondary);
                return;
            }
            _age += dt;
            float fade = Mathf.Max(0.5f, fadeSeconds);
            _strength = Mathf.Clamp01(Mathf.Min(_age / fade, _sched.ActiveLeft / fade));
            Running = k;
            RunningLeft = _sched.ActiveLeft;

            if (k == WorldEventKind.WhaleMigration && seaLife != null)
            {
                Vector2 mid = seaLife.MigrationCenter;
                _focus = new Vector3(mid.x, 0f, mid.y);
                _hasFocus = seaLife.MigrationGroups > 0;
            }
            if (k == WorldEventKind.Meteors) StepMeteors(dt);
            if (_rainbow != null)
                _rainbow.Step(dt, k == WorldEventKind.Rainbow ? _strength : 0f, ArcAxis(), rainbowMaterial, rainbowBrightness, rainbowSecondary);
        }

        Vector2 PlayerCourse()
        {
            if (_player == null) return Vector2.up;
            Vector2 v = _player.PlanarVelocity;
            if (seaLife != null && seaLife.debugPlayerVelocity.sqrMagnitude > 0f) v = seaLife.debugPlayerVelocity;
            if (v.sqrMagnitude > 0.04f) return v.normalized;
            Vector3 f = _player.BodyForward;
            Vector2 flat = new Vector2(f.x, f.z);
            return flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector2.up;
        }

        // The bow stands on the circle 42 degrees round the point opposite the sun; a share of the way it turns
        // towards the storm, so it really stands over the rain instead of wherever the sun happens to put it.
        public Vector3 ArcAxis()
        {
            Vector3 sun = dayNight != null ? dayNight.TrueSunDirection : Vector3.up;
            Vector3 anti = -sun;
            if (anti.sqrMagnitude < 1e-6f) anti = Vector3.down;
            anti.Normalize();
            var storms = StormSystem.Instance;
            if (rainbowStormBias > 0f && storms != null && _player != null
                && storms.NearestStorm(_player.PlanarPosition, out StormData s))
            {
                Vector2 to = s.center - _player.PlanarPosition;
                if (to.sqrMagnitude > 4f)
                {
                    to.Normalize();
                    Vector2 flat = new Vector2(anti.x, anti.z);
                    float len = flat.magnitude;
                    if (len > 1e-4f)
                    {
                        Vector2 turned = Vector2.Lerp(flat / len, to, Mathf.Clamp01(rainbowStormBias));
                        if (turned.sqrMagnitude > 1e-6f)
                        {
                            turned = turned.normalized * len;
                            anti = new Vector3(turned.x, anti.y, turned.y).normalized;
                        }
                    }
                }
            }
            return anti;
        }

        // _meteors[i] = head direction (xyz) and how much of its life is left (w, 1 -> 0); _meteorLife the whole
        // life in seconds, _meteorDirs the direction it streaks away from the radiant in (w = length factor).
        readonly float[] _meteorLife = new float[MaxMeteors];

        void StepMeteors(float dt)
        {
            for (int i = 0; i < MaxMeteors; i++)
            {
                if (_meteors[i].w <= 0f) continue;
                Vector4 m = _meteors[i];
                m.w -= dt / Mathf.Max(0.1f, _meteorLife[i]);
                _meteors[i] = m.w > 0f ? m : Vector4.zero;
            }
            _meteorAcc += dt * (meteorsPerMinute / 60f) * _strength;
            while (_meteorAcc >= 1f)
            {
                _meteorAcc -= 1f;
                Launch();
            }
        }

        void Launch()
        {
            int slot = -1;
            for (int i = 0; i < MaxMeteors; i++) if (_meteors[i].w <= 0f) { slot = i; break; }
            if (slot < 0) { slot = _meteorNext; _meteorNext = (_meteorNext + 1) % MaxMeteors; }
            uint h = SeaMath.Hash(_meteorSeed, (uint)(_meteorCount++ * 97 + slot * 7 + (int)(_age * 11f)));
            // A shower radiates from one point: the streak starts a random angle off the radiant and runs away
            // from it, the further out the longer, exactly as a real shower looks.
            Vector3 up = Mathf.Abs(_radiant.y) > 0.9f ? Vector3.forward : Vector3.up;
            Vector3 right = Vector3.Normalize(Vector3.Cross(up, _radiant));
            Vector3 fwd = Vector3.Cross(_radiant, right);
            float a = SeaMath.Rand(h, 1) * Mathf.PI * 2f;
            float spread = Mathf.Lerp(0.1f, 0.8f, SeaMath.Rand(h, 2));
            Vector3 off = (right * Mathf.Cos(a) + fwd * Mathf.Sin(a)) * spread;
            Vector3 head = Vector3.Normalize(_radiant + off);
            // One that would light up under the horizon is turned to the other side of the radiant instead: half a
            // shower is always below the sea, and none of it would ever be seen.
            if (head.y < 0.04f) head = Vector3.Normalize(_radiant - off);
            Vector3 away = head - _radiant * Vector3.Dot(head, _radiant);
            away = away.sqrMagnitude > 1e-8f ? away.normalized : right;
            _meteorDirs[slot] = new Vector4(away.x, away.y, away.z, Mathf.Lerp(0.45f, 1f, spread));
            _meteors[slot] = new Vector4(head.x, head.y, head.z, 1f);
            _meteorLife[slot] = Mathf.Lerp(0.7f, 1.45f, SeaMath.Rand(h, 4));
        }

        // The two sky additions are off unless their global is non-zero (see DriftSky.hlsl), so the idle case only
        // has to clear them once instead of pushing zeros every frame.
        // Starts true so the first push always clears whatever a previous session left in the globals.
        bool _skyOn = true;

        void PushSky()
        {
            bool aurora = _sched != null && _sched.Active == WorldEventKind.Aurora && _strength > 0.001f;
            bool meteors = _sched != null && _sched.Active == WorldEventKind.Meteors;
            if (!aurora && !meteors && !_skyOn)
            {
                if (AmbientBoost != Color.black) AmbientBoost = Color.black;
                return;
            }
            _skyOn = aurora || meteors;
            if (aurora)
            {
                Vector3 f = _radiant.sqrMagnitude > 1e-6f ? _radiant.normalized : Vector3.forward;
                Vector3 r = Vector3.Normalize(Vector3.Cross(Vector3.up, f));
                float s = _strength * auroraBrightness;
                Shader.SetGlobalVector(AuroraId, new Vector4(s, auroraHeight, Mathf.Max(0.02f, auroraThickness), auroraDrift));
                Shader.SetGlobalVector(AuroraDirId, new Vector4(f.x, f.y, f.z, 0f));
                Shader.SetGlobalVector(AuroraRightId, new Vector4(r.x, r.y, r.z, 0f));
                Shader.SetGlobalVector(AuroraLowId, Lin(auroraLow));
                Shader.SetGlobalVector(AuroraHighId, Lin(auroraHigh));
                Color amb = Color.Lerp(auroraLow, auroraHigh, 0.35f) * (auroraAmbient * _strength);
                AmbientBoost = new Color(amb.r, amb.g, amb.b, 0f);
            }
            else
            {
                Shader.SetGlobalVector(AuroraId, Vector4.zero);
                if (AmbientBoost != Color.black) AmbientBoost = Color.black;
            }

            if (meteors)
            {
                float width = 2f * Mathf.Sin(0.5f * Mathf.Deg2Rad * 0.55f);
                Shader.SetGlobalVector(MeteorParamsId, new Vector4(meteorBrightness * _strength, width,
                    2f * Mathf.Sin(0.5f * Mathf.Deg2Rad * Mathf.Max(1f, meteorLength)), 0f));
                for (int i = 0; i < MaxMeteors; i++)
                {
                    Vector4 m = _meteors[i];
                    // The shader gets the head direction and how far along its life it is (0 = just lit, 1 = gone).
                    _meteorPush[i] = new Vector4(m.x, m.y, m.z, Mathf.Clamp01(m.w));
                }
                Shader.SetGlobalVectorArray(MeteorsId, _meteorPush);
                Shader.SetGlobalVectorArray(MeteorDirsId, _meteorDirs);
            }
            else
            {
                Shader.SetGlobalVector(MeteorParamsId, Vector4.zero);
            }
        }

        readonly Vector4[] _meteorPush = new Vector4[MaxMeteors];

        static Vector4 Lin(Color c) { Color l = c.linear; return new Vector4(l.r, l.g, l.b, 0f); }
    }
}
