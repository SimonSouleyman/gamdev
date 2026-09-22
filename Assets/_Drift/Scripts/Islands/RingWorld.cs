using System.Collections.Generic;
using Drift.Core;
using Drift.Tectonics;
using UnityEngine;

namespace Drift.Islands
{
    // What a single adventure run has been through, for the game-over screen and the HUD. Reset whenever the ring is
    // filled anew (a fresh run); the counters are raised by RingWorld and by Encounters when a piece is collected.
    public static class AdventureRunStats
    {
        public static int Hits, Dodges, Flotsam;

        public static void Reset()
        {
            Hits = 0;
            Dodges = 0;
            Flotsam = 0;
        }
    }

    // The band of the adventure ring in planar coordinates: z runs along the ring and never wraps in the simulation
    // (the player keeps a continuous coordinate), x runs across and ends at the two rims at centerX +- halfWidth.
    // The ring look is a vertex bend (Shaders/DriftCurve.hlsl, DriftRingWS): a point dz ahead of the focus lands at
    // the angle dz / Radius on a circle whose axis hangs Radius above the focus, so the band climbs into the sky
    // ahead and behind and dz and dz + Circumference are drawn at the same place.
    [System.Serializable]
    public struct RingGeometry
    {
        public float centerX;
        public float halfWidth;
        public float circumference;

        public RingGeometry(float centerX, float halfWidth, float circumference)
        {
            this.centerX = centerX;
            this.halfWidth = halfWidth;
            this.circumference = circumference;
        }

        public float Radius => circumference / (2f * Mathf.PI);
        public float HalfLength => circumference * 0.5f;
        public float MinX => centerX - halfWidth;
        public float MaxX => centerX + halfWidth;

        // z - refZ taken the short way round the ring, in [-C/2, C/2).
        public float AlongDelta(float z, float refZ)
        {
            float d = z - refZ;
            if (circumference <= 0f) return d;
            return d - circumference * Mathf.Floor(d / circumference + 0.5f);
        }

        // The copy of z (z + k * C) closest to refZ.
        public float WrapNear(float z, float refZ) => refZ + AlongDelta(z, refZ);

        public bool Inside(Vector2 p, float margin) => p.x >= MinX + margin && p.x <= MaxX - margin;

        // Keeps x inside the band, margin away from the rim (at most 90 % of the half width, so a huge island still
        // has a centre line to sit on). Used for the player, the obstacle islands and the flotsam alike.
        public Vector2 ClampAcross(Vector2 p, float margin)
        {
            margin = Mathf.Clamp(margin, 0f, halfWidth * 0.9f);
            p.x = Mathf.Clamp(p.x, MinX + margin, MaxX - margin);
            return p;
        }

        // Mirrors DriftRingWS in Shaders/DriftCurve.hlsl: where a world position is drawn for a view focused on
        // focus (identity at the focus). Across the band the world ends at the rims - anything beyond is folded
        // onto the rim plane, so the sea closes into the edge.
        public Vector3 Bend(Vector3 world, Vector2 focus)
        {
            if (circumference <= 0f || world.y < -1000f) return world;
            float r = Radius;
            float x = Mathf.Clamp(world.x, MinX, MaxX);
            float dz = Mathf.Clamp(world.z - focus.y, -HalfLength, HalfLength);
            float y = Mathf.Min(world.y, 0.5f * r);
            float t = dz / r;
            float h = Mathf.Sin(0.5f * t);
            float versin = 2f * h * h;
            return new Vector3(x, y + (r - y) * versin, focus.y + (r - y) * Mathf.Sin(t));
        }
    }

    // Adventure mode ("Abenteuer"): the Halo-like closed ring. While GameModes is Adventure it
    //  - switches the cozy WorldStreamer off (and back on in Cozy) and runs the RingIslandSpawner instead,
    //  - holds the player inside the band (Island.PositionConstraint) and keeps every obstacle island inside it and
    //    within half a circumference of the view (moving it by exactly one circumference is invisible),
    //  - lays the plates out along the band (PlateSystem.SetRingLayout) so there are surf lanes,
    //  - lowers the chase camera so the band climbing into the sky ahead is in view,
    //  - and runs the difficulty: the longer the run lasts, the more obstacle islands lie on the track, the more of
    //    them drift across it, the faster the base pace, the faster the island sinks and the rarer the flotsam
    //    (Encounters reads FlotsamSpacing). Islands are never merged in Adventure (IslandWorld) - they are dodged.
    // CurvedWorld reads Active, bends the world into the ring (the sea ends at the rims) and lays the foam line of
    // Visuals/RingRims on the water there. The rims themselves are invisible: there is nothing to fall off, a run
    // only ever ends by sinking.
    [ExecuteAlways]
    [DefaultExecutionOrder(-40)]
    [RequireComponent(typeof(RingIslandSpawner))]
    public class RingWorld : MonoBehaviour
    {
        [Header("Ring")]
        [Tooltip("Umfang des Rings in Einheiten: so weit fährt man einmal herum.")]
        [Range(300f, 1500f)] public float circumference = 640f;
        [Tooltip("Breite des befahrbaren Bands zwischen den beiden Randwänden.")]
        [Range(40f, 140f)] public float bandWidth = 56f;
        [Tooltip("Wie weit die Hindernisinseln mindestens vom Rand wegbleiben (Anteil ihres Radius).")]
        [Range(0f, 1.2f)] public float islandWallMargin = 0.9f;

        [Header("Rand des Bands")]
        [Tooltip("Wie weit die Spielerinsel mindestens vom Rand wegbleibt (Anteil ihres Radius). Hier endet die Welt – unsichtbar, aber fest.")]
        [Range(0f, 1.2f)] public float playerWallMargin = 1f;
        [Tooltip("Wie weit vor dem Rand es schäumt, sanft zurückschiebt und der Hinweis kommt.")]
        [Range(0f, 30f)] public float edgeWarnWidth = 10f;
        [Tooltip("Sanfter Schub zurück auf die Bahn (u/s) direkt am Rand. Davor wächst er quadratisch an.")]
        [Range(0f, 12f)] public float edgePush = 3f;

        [Header("Abenteuer-Schwierigkeit")]
        [Tooltip("Sekunden, bis die Schwierigkeit ihr Höchstmaß erreicht hat. Danach bleibt sie dort.")]
        [Range(30f, 900f)] public float difficultyRampSeconds = 300f;
        [Tooltip("Alle so viele Sekunden zeigt die Anzeige eine Stufe mehr.")]
        [Range(10f, 180f)] public float levelSeconds = 45f;
        [Tooltip("So viele Hindernisinseln kommen bis zur höchsten Stufe zusätzlich auf den Ring.")]
        [Range(0, 30)] public int extraIslandsAtMax = 10;
        [Tooltip("Anteil der Inseln, die quer über die Bahn treiben – am Anfang.")]
        [Range(0f, 1f)] public float driftShareStart = 0f;
        [Tooltip("Anteil der Inseln, die quer über die Bahn treiben – bei höchster Stufe.")]
        [Range(0f, 1f)] public float driftShareMax = 0.45f;
        [Tooltip("Tempo (u/s) der quer treibenden Inseln am Anfang.")]
        [Range(0f, 8f)] public float driftSpeedStart = 1.4f;
        [Tooltip("Tempo (u/s) der quer treibenden Inseln bei höchster Stufe.")]
        [Range(0f, 8f)] public float driftSpeedMax = 3.4f;
        [Tooltip("Abstand (u) zwischen zwei Treibgut-Gruppen auf der Bahn am Anfang.")]
        [Range(8f, 120f)] public float flotsamSpacingStart = 24f;
        [Tooltip("Abstand (u) zwischen zwei Treibgut-Gruppen bei höchster Stufe – Treibgut wird seltener.")]
        [Range(8f, 200f)] public float flotsamSpacingMax = 60f;
        [Tooltip("Wie viel schneller die Insel bei höchster Stufe sinkt (Faktor).")]
        [Range(0.5f, 3f)] public float sinkScaleMax = 1.5f;
        [Tooltip("So nah (u zwischen den Inselrändern) zählt ein Vorbeifahren als knapp ausgewichen.")]
        [Range(0f, 20f)] public float dodgeGap = 6f;

        [Header("Abenteuer-Fahrt")]
        [Tooltip("Grundtempo auf Stufe 1: so viel vom Höchsttempo hält die Insel von allein. Langsamer wird sie nur beim Bremsen.")]
        [Range(0.1f, 1f)] public float cruiseStart = 0.62f;
        [Tooltip("Um so viel steigt das Grundtempo mit jeder Stufe.")]
        [Range(0f, 0.2f)] public float cruisePerLevel = 0.03f;
        [Tooltip("Obergrenze für das Grundtempo.")]
        [Range(0.1f, 1f)] public float cruiseMax = 0.88f;
        [Tooltip("Höchsttempo auf Stufe 1 (Faktor auf das normale Inseltempo).")]
        [Range(0.5f, 4f)] public float speedScaleStart = 1.45f;
        [Tooltip("Um diesen Faktor wird die Insel mit jeder Stufe schneller – das Kernstück des Rennens.")]
        [Range(0f, 0.6f)] public float speedPerLevel = 0.18f;
        [Tooltip("Obergrenze für das Höchsttempo (Faktor).")]
        [Range(0.5f, 5f)] public float speedScaleMax = 3f;

        [Header("Kamera im Abenteuer")]
        [Tooltip("Kamera flacher stellen, damit das Band vorne sichtbar in den Himmel steigt.")]
        public bool adjustCamera = true;
        [Tooltip("Kamerahöhe über der Insel im Abenteuer (normal 9). Niedrig = man sieht das Band vorne in den Himmel steigen.")]
        [Range(1f, 12f)] public float cameraHeight = 1.8f;
        [Tooltip("Kameraabstand hinter der Insel im Abenteuer (normal 7).")]
        [Range(3f, 24f)] public float cameraDistance = 12f;

        public Island player;
        public WorldStreamer streamer;

        public static RingWorld Active { get; private set; }

        public RingGeometry Geometry => new RingGeometry(_centerX, bandWidth * 0.5f, circumference);
        public RingIslandSpawner Spawner => _spawner != null ? _spawner : (_spawner = GetComponent<RingIslandSpawner>());
        public bool IsApplied => _applied;

        // How close the player is to a rim right now (0 = out on the band, 1 = pressed against the edge). Drives the
        // foam line, the soft push back and the HUD hint.
        public float EdgeWarning { get; private set; }

        // The player slipped past an obstacle island with less than dodgeGap to spare (HUD cheer, tutorial step).
        public static event System.Action<Island> Dodged;

        // Seconds this run has been under way (paused while the session is not playing) and the difficulty 0..1.
        public float RunSeconds { get; private set; }
        public float Difficulty => difficultyRampSeconds > 0f ? Mathf.Clamp01(RunSeconds / difficultyRampSeconds) : 1f;
        public int Level => 1 + Mathf.FloorToInt(RunSeconds / Mathf.Max(1f, levelSeconds));
        // Counts up with every fresh fill of the ring: a new run (listeners reset their own state).
        public int FillCount { get; private set; }
        // Distance along the track between two flotsam groups right now (Visuals/Encounters lays them out).
        public float FlotsamSpacing => Mathf.Lerp(flotsamSpacingStart, Mathf.Max(flotsamSpacingStart, flotsamSpacingMax), Difficulty);
        public float DriftShare => Mathf.Lerp(driftShareStart, driftShareMax, Difficulty);
        public float DriftSpeed => Mathf.Lerp(driftSpeedStart, driftSpeedMax, Difficulty);
        // Pace: base and top speed go up a step with every level, not smoothly with the clock - that is what makes
        // "Stufe N" mean something ("die geschwindigkeit soll mit jedem level schneller werden").
        public float Cruise => Mathf.Min(Mathf.Max(cruiseStart, cruiseMax), cruiseStart + cruisePerLevel * (Level - 1));
        public float PaceScale => Mathf.Min(Mathf.Max(speedScaleStart, speedScaleMax), speedScaleStart + speedPerLevel * (Level - 1));
        public int DriftingIslands { get; private set; }

        // Tests and the debug menu: jump to a point of the difficulty curve.
        public void SetRunSeconds(float seconds) => RunSeconds = Mathf.Max(0f, seconds);

        RingIslandSpawner _spawner;
        IslandChaseCamera _camera;
        bool _applied;
        bool _needsFill;
        bool _streamerWasEnabled;
        bool _cameraSaved;
        float _savedHeight, _savedDistance;
        float _centerX;
        Vector2 _lastPlayerPos;
        bool _hasLastPlayerPos;
        readonly List<Island> _islands = new();
        readonly Dictionary<Island, float> _drift = new(), _passDz = new(), _lastHit = new();
        readonly List<Island> _forget = new();
        System.Func<Vector2, Vector2> _constraint;
        float _travelDir = 1f, _pruneTimer;

        void OnEnable()
        {
            GameModes.Changed -= OnModeChanged;
            GameModes.Changed += OnModeChanged;
            PlateSystem.Restored -= OnPlatesRestored;
            PlateSystem.Restored += OnPlatesRestored;
            Island.Bumped -= OnBumped;
            Island.Bumped += OnBumped;
            Sync();
        }

        void OnDisable()
        {
            GameModes.Changed -= OnModeChanged;
            PlateSystem.Restored -= OnPlatesRestored;
            Island.Bumped -= OnBumped;
            // Children cannot be destroyed while this object is being deactivated; they go inactive with it and
            // the spawner clears them when it is enabled again.
            if (_applied) Apply(false, false);
        }

        void OnModeChanged(GameMode mode) => Sync();

        // GameSession.Restart restores the plates first, then resets the player: the ring is refilled on the next
        // update, around wherever the player is by then.
        void OnPlatesRestored()
        {
            if (_applied) _needsFill = true;
        }

        void Sync()
        {
            bool want = GameModes.IsAdventure && isActiveAndEnabled;
            if (want != _applied) Apply(want);
        }

        void Resolve()
        {
            if (player == null)
                foreach (var i in Island.All)
                    if (i != null && i.useKeyboardInput) { player = i; break; }
            if (player == null)
                foreach (var i in FindObjectsByType<Island>())
                    if (i.useKeyboardInput) { player = i; break; }
            if (streamer == null) streamer = FindAnyObjectByType<WorldStreamer>();
            if (_camera == null) _camera = FindAnyObjectByType<IslandChaseCamera>();
        }

        void Apply(bool on, bool clearIslands = true)
        {
            Resolve();
            _applied = on;
            if (on)
            {
                _centerX = player != null ? player.startPlanarPosition.x : 0f;
                Active = this;
                if (streamer != null)
                {
                    _streamerWasEnabled = streamer.enabled;
                    streamer.enabled = false;
                    streamer.ResetWorld();
                }
                // The world ends at the rims: the player is held inside the band, the obstacle islands are kept
                // there by KeepIslandsOnRing. Nothing can leave it, and nothing solid is drawn there.
                Island.PositionConstraint = _constraint ??= ClampPlayer;
                ApplyPlates();
                if (adjustCamera && _camera != null)
                {
                    if (!_cameraSaved)
                    {
                        _savedHeight = _camera.height;
                        _savedDistance = _camera.distanceBehind;
                        _cameraSaved = true;
                    }
                    _camera.height = cameraHeight;
                    _camera.distanceBehind = cameraDistance;
                }
                _needsFill = true;
                _hasLastPlayerPos = false;
            }
            else
            {
                if (Active == this) Active = null;
                if (_constraint != null && Island.PositionConstraint == _constraint) Island.PositionConstraint = null;
                if (clearIslands) Spawner.Clear();
                var plates = PlateSystem.Instance;
                if (plates != null && plates.RingLayout) plates.ClearRingLayout();
                if (player != null)
                {
                    player.AdventureSpeedScale = 1f;
                    player.AdventureCruise = 0f;
                    player.AdventureSinkScale = 1f;
                    player.AdventureTrack = Vector2.up;
                    player.EdgePush = Vector2.zero;
                    player.EdgeWarning = 0f;
                }
                EdgeWarning = 0f;
                _drift.Clear();
                _passDz.Clear();
                _lastHit.Clear();
                if (_cameraSaved && _camera != null)
                {
                    _camera.height = _savedHeight;
                    _camera.distanceBehind = _savedDistance;
                }
                _cameraSaved = false;
                if (streamer != null && _streamerWasEnabled && !streamer.enabled) streamer.enabled = true;
                _streamerWasEnabled = false;
            }
        }

        void ApplyPlates()
        {
            var plates = PlateSystem.Instance;
            if (plates == null) return;
            var g = Geometry;
            if (!plates.RingLayout || !plates.RingMatches(g.centerX, g.halfWidth, g.circumference))
                plates.SetRingLayout(g.centerX, g.halfWidth, g.circumference);
        }

        // The view's focus: islands are kept within half a circumference of it, which is exactly the part of the
        // ring the bend draws once (DriftRingWS folds everything beyond onto the antipode).
        public float ReferenceZ()
        {
            var cam = Camera.main;
            if (cam != null) return WaterFollower.ViewFocus(cam.transform).y;
            return player != null ? player.PlanarPosition.y : 0f;
        }

        void Update()
        {
            Sync();
            if (!_applied) return;
            Resolve();
            if (player == null) return;
            ApplyPlates();
            var g = Geometry;

            Vector2 pp = player.PlanarPosition;
            // A reset or a load teleports the player: the ring is refilled around the new position.
            if (_hasLastPlayerPos && (pp - _lastPlayerPos).sqrMagnitude > 60f * 60f) _needsFill = true;
            _lastPlayerPos = pp;
            _hasLastPlayerPos = true;

            var spawner = Spawner;
            int worldSeed = streamer != null ? streamer.seed : spawner.seed;
            if (_needsFill)
            {
                _needsFill = false;
                BeginRun();
                spawner.ResetRing(g, pp, worldSeed, !Application.isPlaying);
            }
            KeepIslandsOnRing(g, ReferenceZ());
            spawner.Step(g, pp, !Application.isPlaying);
            if (Application.isPlaying) StepDifficulty(g, pp, Time.deltaTime);
        }

        void BeginRun()
        {
            RunSeconds = 0f;
            FillCount++;
            AdventureRunStats.Reset();
            _drift.Clear();
            _passDz.Clear();
            _lastHit.Clear();
            _travelDir = 1f;
            Spawner.ExtraIslands = 0;
            if (player != null)
            {
                player.AdventureSpeedScale = speedScaleStart;
                player.AdventureCruise = 0f;
                player.AdventureSinkScale = 1f;
                player.AdventureTrack = Vector2.up;
            }
        }

        void OnBumped(Island hit, Island obstacle, Vector2 contact, float strength)
        {
            if (!_applied || hit != player) return;
            AdventureRunStats.Hits++;
            if (obstacle != null) _lastHit[obstacle] = RunSeconds;
        }

        // The whole difficulty curve in one place: the pace of the player, how fast it sinks, how many obstacles lie
        // on the ring and how many of them drift across it. The run clock only runs while the race really runs
        // (GameSession lets the island sink exactly then).
        void StepDifficulty(RingGeometry g, Vector2 pp, float dt)
        {
            // The island races as soon as the player has the controls - the tutorial's first step already drives,
            // as Tilda says it does. The run clock (and with it the level) only starts once the island may sink.
            bool racing = dt > 0f && !player.IsSunk && !Island.InputLocked;
            bool running = racing && player.sinkEnabled;
            if (running) RunSeconds += dt;
            float d = Difficulty;
            player.AdventureSpeedScale = PaceScale;
            player.AdventureSinkScale = Mathf.Lerp(1f, sinkScaleMax, d);
            player.AdventureCruise = racing ? Cruise : 0f;
            // The band runs along z; the race always goes that way, so the heading, the camera and the surf lanes
            // all agree on where "ahead" is.
            player.AdventureTrack = Vector2.up;
            Spawner.ExtraIslands = Mathf.RoundToInt(extraIslandsAtMax * d);
            StepEdge(g, pp, racing);

            _islands.Clear();
            Spawner.CollectIslands(_islands);
            StepDrift(g, dt);
            StepPasses(g, pp);

            _pruneTimer -= dt;
            if (_pruneTimer > 0f) return;
            _pruneTimer = 3f;
            Prune(_drift);
            Prune(_passDz);
            Prune(_lastHit);
        }

        // Where the band ends there is nothing to see and nothing to fall over: the water foams (RingRims reads
        // EdgeWarning), pushes gently back towards the middle, and the HUD says so. The hard stop is the position
        // constraint below - a run can only ever end by sinking.
        public void StepEdge(RingGeometry g, Vector2 pp, bool running)
        {
            if (player == null) return;
            if (player.IsSunk || !running)
            {
                player.EdgePush = Vector2.zero;
                EdgeWarning = 0f;
                player.EdgeWarning = 0f;
                return;
            }
            float across = pp.x - g.centerX;
            float side = across >= 0f ? 1f : -1f;
            float toRim = g.halfWidth - Mathf.Abs(across);
            float warn = Mathf.Clamp01(1f - toRim / Mathf.Max(0.5f, edgeWarnWidth));
            EdgeWarning = warn;
            player.EdgeWarning = warn;
            player.EdgePush = new Vector2(-side * edgePush * warn * warn, 0f);
        }

        // Island.PositionConstraint for the player: it stays on the band, a whole island radius away from the rim,
        // so the island is never squashed onto the edge fold of the bend.
        Vector2 ClampPlayer(Vector2 p)
        {
            float margin = player != null ? player.BoundingRadius * playerWallMargin : 0f;
            return Geometry.ClampAcross(p, margin);
        }

        void Prune(Dictionary<Island, float> map)
        {
            _forget.Clear();
            foreach (var kv in map) if (kv.Key == null || !kv.Key.isActiveAndEnabled) _forget.Add(kv.Key);
            foreach (var key in _forget) map.Remove(key);
        }

        // Drifting obstacles: a deterministic share of the islands slides across the band, turning at the rims and
        // before another island. Big islands drift more slowly. Their speed is set every frame, before Island.Tick.
        void StepDrift(RingGeometry g, float dt)
        {
            float share = DriftShare, speed = DriftSpeed;
            int drifting = 0;
            for (int i = 0; i < _islands.Count; i++)
            {
                var isl = _islands[i];
                if (isl == null || !isl.isActiveAndEnabled || isl.IsEmerging) continue;
                if (Hash01(isl.shapeSeed, 3) >= share)
                {
                    if (_drift.Remove(isl)) isl.SetSelfVelocity(Vector2.zero);
                    continue;
                }
                if (!_drift.TryGetValue(isl, out float sign)) sign = Hash01(isl.shapeSeed, 4) < 0.5f ? -1f : 1f;
                Vector2 p = isl.PlanarPosition;
                float margin = isl.BoundingRadius * islandWallMargin + 0.5f;
                if (p.x > g.MaxX - margin) sign = -1f;
                else if (p.x < g.MinX + margin) sign = 1f;
                else
                    for (int k = 0; k < _islands.Count; k++)
                    {
                        var other = _islands[k];
                        if (other == null || other == isl || !other.isActiveAndEnabled) continue;
                        float reach = isl.BoundingRadius + other.BoundingRadius;
                        if (Mathf.Abs(g.AlongDelta(other.PlanarPosition.y, p.y)) > reach) continue;
                        float dx = other.PlanarPosition.x - p.x;
                        if (Mathf.Abs(dx) < reach + 1.5f && Mathf.Sign(dx) == sign) { sign = -sign; break; }
                    }
                _drift[isl] = sign;
                drifting++;
                // A heavy island shoves its way across more slowly than an islet.
                float weight = Mathf.Clamp(4f / Mathf.Max(1f, isl.BoundingRadius), 0.4f, 1f);
                if (dt > 0f) isl.SetSelfVelocity(new Vector2(sign * speed * weight, 0f));
            }
            DriftingIslands = drifting;
        }

        // Counts a close pass as a dodge: the island was ahead and is behind now, its coast within dodgeGap, and it
        // was not hit on the way (a hit is not a dodge).
        void StepPasses(RingGeometry g, Vector2 pp)
        {
            Vector2 v = player.PlanarVelocity;
            if (Mathf.Abs(v.y) > 0.5f) _travelDir = Mathf.Sign(v.y);
            for (int i = 0; i < _islands.Count; i++)
            {
                var isl = _islands[i];
                if (isl == null || !isl.isActiveAndEnabled) continue;
                float reach = isl.BoundingRadius + player.BoundingRadius;
                float dz = g.AlongDelta(isl.PlanarPosition.y, pp.y) * _travelDir;
                if (_passDz.TryGetValue(isl, out float prev) && prev > 0f && dz <= 0f && prev < reach + 12f)
                {
                    float gap = Mathf.Abs(isl.PlanarPosition.x - pp.x) - reach;
                    bool hit = _lastHit.TryGetValue(isl, out float when) && RunSeconds - when < 3f;
                    if (!hit && gap < dodgeGap)
                    {
                        AdventureRunStats.Dodges++;
                        Dodged?.Invoke(isl);
                    }
                }
                _passDz[isl] = dz;
            }
        }

        static float Hash01(int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)(seed * 73856093 ^ salt * 19349663);
                h ^= h >> 16; h *= 0x7feb352du;
                h ^= h >> 15; h *= 0x846ca68bu;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / 16777216f;
            }
        }

        public void KeepIslandsOnRing(RingGeometry g, float refZ)
        {
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var isl = all[i];
                if (isl == null || isl.useKeyboardInput || !isl.isActiveAndEnabled) continue;
                Vector2 p = isl.PlanarPosition;
                Vector2 q = g.ClampAcross(new Vector2(p.x, g.WrapNear(p.y, refZ)), isl.BoundingRadius * islandWallMargin);
                if ((q - p).sqrMagnitude > 1e-6f) isl.SetPlanarPosition(q);
            }
        }
    }
}
