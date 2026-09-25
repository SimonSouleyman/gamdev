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

    // The breath before every adventure race (owner, 2026-09-25: "einen kurzen Moment, bevor die Runde anfängt ...
    // zum Durchatmen"): GameSession arms it when a run starts playing, and while it runs the race is held exactly like
    // Tilda's start hold (RingWorld.StartHeld: island and obstacles pinned, no sinking, no distance, no clock). It
    // only counts down while nothing else holds the start, so after the briefing it simply follows it - one hold,
    // handed over, never two. Pull is the chase camera's share of the wide start framing: 1 for the first holdShare
    // of the intro, then an ease-in-out glide to 0 = the race framing exactly when the race starts.
    public class AdventureIntro
    {
        float _total, _left;

        public bool Holding => _left > 0f;
        public float Seconds => _total;
        public float Elapsed => Holding ? _total - _left : _total;
        public float Pull => Holding ? PullAt(Elapsed, _total, HoldShare) : 0f;

        // Share of the intro the camera rests in the wide framing before it glides in.
        public const float HoldShare = 0.18f;

        public void Arm(float seconds)
        {
            _total = Mathf.Max(0f, seconds);
            _left = _total;
        }

        public void Cancel() => _left = 0f;

        // One frame; waiting = something else holds the start (the briefing, a pause). True in the frame it ends.
        public bool Step(float dt, bool waiting)
        {
            if (_left <= 0f || waiting || dt <= 0f) return false;
            _left -= dt;
            if (_left > 0f) return false;
            _left = 0f;
            return true;
        }

        public static float PullAt(float elapsed, float total, float holdShare)
        {
            if (total <= 0f) return 0f;
            float t = Mathf.Clamp01(elapsed / total), h = Mathf.Clamp(holdShare, 0f, 0.95f);
            if (t <= h) return 1f;
            float s = (t - h) / (1f - h);
            return 1f - s * s * (3f - 2f * s);
        }
    }

    // The score of an adventure run: how far the island got along the track (+z). Only new ground counts - a bounce
    // back off an island and the metres driven again afterwards are not counted twice - and only while counting is
    // on (the race runs); a jump of more than TeleportDistance (a reset, a load) starts from the new spot.
    public struct RunOdometer
    {
        public const float TeleportDistance = 60f;

        float _front, _last;
        bool _started;

        public float Distance { get; set; }

        public void Reset()
        {
            Distance = 0f;
            _started = false;
        }

        public void Step(float z, bool counting)
        {
            if (!_started || Mathf.Abs(z - _last) > TeleportDistance)
            {
                _started = true;
                _front = z;
            }
            else if (z > _front)
            {
                if (counting) Distance += z - _front;
                _front = z;
            }
            _last = z;
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
    //  - and runs the difficulty: the further the island gets, the more obstacle islands lie on the track (extra
    //    islands per level, and volcanoes rising ahead of the player), the more of them drift across it, the faster
    //    the base pace, the faster the island sinks and the rarer the flotsam (Encounters reads FlotsamSpacing).
    //    Islands are never merged in Adventure (IslandWorld) - they are dodged.
    // The score is the distance (RunDistance) and the levels follow it. Every level is longer by as much as its base
    // pace is higher, so at the base pace each still lasts levelSeconds - the seconds-based knobs below (difficulty
    // ramp, volcano schedule) are read as "seconds at the base pace" (ProgressSeconds).
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
        [Tooltip("Sekunden bei Grundtempo, bis die Schwierigkeit ihr Höchstmaß erreicht hat. Danach bleibt sie dort.")]
        [Range(30f, 900f)] public float difficultyRampSeconds = 300f;
        [Tooltip("Länge der Stufe 1 in Metern (45 s bei Grundtempo). Jede weitere Stufe ist um so viel länger, wie ihr Grundtempo höher ist.")]
        [Range(50f, 2000f)] public float levelDistance = 340f;
        [Tooltip("So viele Sekunden dauert eine Stufe bei Grundtempo (rechnet die Strecke in Schwierigkeit und Vulkane um).")]
        [Range(10f, 180f)] public float levelSeconds = 45f;
        [Tooltip("So viele Hindernisinseln kommen mit jeder Stufe zusätzlich auf den Ring (weit weg, sie steigen dort aus dem Meer). 1,5 = drei je zwei Stufen.")]
        [Range(0f, 5f)] public float extraIslandsPerLevel = 2f;
        [Tooltip("Mehr zusätzliche Hindernisinseln kommen nie auf den Ring.")]
        [Range(0, 30)] public int extraIslandsAtMax = 14;
        [Tooltip("Ab dieser Stufe steigen Vulkane vor der Insel aus dem Meer – sichtbar, mit einer freien Spur daneben.")]
        [Range(1, 20)] public int volcanoFirstLevel = 2;
        [Tooltip("So viele Vulkane steigen je Stufe auf (0,5 = jede zweite Stufe einer). Sie bleiben bis zum Ende der Runde.")]
        [Range(0f, 4f)] public float volcanoesPerLevel = 1f;
        [Tooltip("Mehr Vulkane liegen nie gleichzeitig auf dem Ring.")]
        [Range(0, 20)] public int maxVolcanoes = 8;
        [Tooltip("Anteil der Inseln, die quer über die Bahn treiben – am Anfang.")]
        [Range(0f, 1f)] public float driftShareStart = 0f;
        [Tooltip("Anteil der Inseln, die quer über die Bahn treiben – bei höchster Stufe.")]
        [Range(0f, 1f)] public float driftShareMax = 0.45f;
        [Tooltip("Tempo (u/s) der quer treibenden Inseln am Anfang.")]
        [Range(0f, 8f)] public float driftSpeedStart = 1.4f;
        [Tooltip("Tempo (u/s) der quer treibenden Inseln bei höchster Stufe.")]
        [Range(0f, 8f)] public float driftSpeedMax = 3.4f;
        [Tooltip("Abstand (u) zwischen zwei Treibgut-Gruppen auf der Bahn am Anfang. Selten, dafür wertvoll: jedes Stück gibt viel Auftrieb und einen spürbaren Schub.")]
        [Range(8f, 160f)] public float flotsamSpacingStart = 72f;
        [Tooltip("Abstand (u) zwischen zwei Treibgut-Gruppen bei höchster Stufe – Treibgut wird seltener.")]
        [Range(8f, 240f)] public float flotsamSpacingMax = 120f;
        [Tooltip("Grundtempo des Sinkens im Abenteuer (Faktor auf allen Stufen; v0.6.3: 15 % schneller).")]
        [Range(0.5f, 2f)] public float sinkSpeed = 1.15f;
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

        [Header("Schwung")]
        [Tooltip("Bei vollem Schwung ist die Insel um diesen Anteil schneller (1 = doppeltes Tempo). Grund- und Höchsttempo und das Lenken wachsen gleich mit.")]
        [Range(0f, 2f)] public float momentumSpeedGain = 1f;
        [Tooltip("So viel Schwung (Anteil) bringt jedes eingesammelte Schub-Teil. Eine Gruppe (1-3 Teile) nach der anderen: nach 5-6 Gruppen ist der Schwung fast voll.")]
        [Range(0f, 0.5f)] public float momentumPerBoost = 0.13f;
        [Tooltip("So viel Schwung pro Sekunde bringt das Surfen an einer Plattengrenze bei voller Stärke. Surfen allein trägt etwa bis momentumSurfRate / (ln2 / Halbwertszeit).")]
        [Range(0f, 0.2f)] public float momentumSurfRate = 0.03f;
        [Tooltip("Erst ab dieser Surf-Stärke zählt das Surfen für den Schwung.")]
        [Range(0f, 1f)] public float momentumSurfStart = 0.35f;
        [Tooltip("In so vielen Sekunden ohne Schub verliert die Insel die Hälfte ihres Schwungs (während eines Schubs bleibt er).")]
        [Range(2f, 60f)] public float momentumHalfLife = 10f;
        [Tooltip("Anteil des Schwungs, den ein Treffer an einer Insel kostet.")]
        [Range(0f, 1f)] public float momentumHitLoss = 0.5f;

        [Header("Kamera im Abenteuer")]
        [Tooltip("Kamera flacher stellen, damit das Band vorne sichtbar in den Himmel steigt.")]
        public bool adjustCamera = true;
        [Tooltip("Kamerahöhe über der Insel im Abenteuer (normal 9). Niedrig = man sieht das Band vorne in den Himmel steigen.")]
        [Range(1f, 12f)] public float cameraHeight = 1.6f;
        [Tooltip("Kameraabstand hinter der Insel im Abenteuer (normal 7).")]
        [Range(3f, 24f)] public float cameraDistance = 11f;

        public Island player;
        public WorldStreamer streamer;

        public static RingWorld Active { get; private set; }

        public RingGeometry Geometry => new RingGeometry(_centerX, bandWidth * 0.5f, circumference);
        public RingIslandSpawner Spawner => _spawner != null ? _spawner : (_spawner = GetComponent<RingIslandSpawner>());
        public bool IsApplied => _applied;

        // How close the player is to a rim right now (0 = out on the band, 1 = pressed against the edge). Drives the
        // foam line, the soft push back and the HUD hint.
        public float EdgeWarning { get; private set; }

        // The player slipped past an obstacle island with less than dodgeGap to spare (HUD cheer).
        public static event System.Action<Island> Dodged;

        // Tilda's adventure briefing holds the race at the start line (GameSession.StartHold writes it - the Bridge
        // is out of this assembly's reach): the island waits where it is, the obstacles stand still, and neither the
        // distance nor the clock count. The race starts the moment the flag drops.
        public static bool StartHeld;
        // The start intro's camera pull (AdventureIntro.Pull, written by GameSession): 1 = the wide start framing,
        // 0 = the race framing. IslandChaseCamera reads it.
        public static float IntroPull;
        // The hold on the start line ended and the race runs (after the briefing and the intro): the HUD's "Los!".
        public static event System.Action RaceStarted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            StartHeld = false;
            IntroPull = 0f;
        }

        // True while the hold is in force on the running ring.
        public bool Holding => _holding;

        // Seconds the race has really run (not while held, paused or sunk), and the score: metres travelled along
        // the track in that time.
        public float RunSeconds { get; private set; }
        public float RunDistance => _odometer.Distance;
        // How far into the difficulty curve the run is, in seconds at the base pace (see the class comment).
        public float ProgressSeconds => ProgressAt(RunDistance, out _);
        public float Difficulty => difficultyRampSeconds > 0f ? Mathf.Clamp01(ProgressSeconds / difficultyRampSeconds) : 1f;
        // The same ramp on the race clock (seconds really raced), for what must not speed up with the player's pace.
        public float TimeDifficulty => difficultyRampSeconds > 0f ? Mathf.Clamp01(RunSeconds / difficultyRampSeconds) : 1f;
        public int Level => LevelAt(RunDistance);
        // Counts up with every fresh fill of the ring: a new run (listeners reset their own state).
        public int FillCount { get; private set; }
        // Distance along the track between two flotsam groups right now (Visuals/Encounters lays them out).
        public float FlotsamSpacing => Mathf.Lerp(flotsamSpacingStart, Mathf.Max(flotsamSpacingStart, flotsamSpacingMax), Difficulty);
        public float DriftShare => Mathf.Lerp(driftShareStart, driftShareMax, Difficulty);
        public float DriftSpeed => Mathf.Lerp(driftSpeedStart, driftSpeedMax, Difficulty);
        // Pace: base and top speed go up a step with every level, not smoothly with the clock - that is what makes
        // "Stufe N" mean something ("die geschwindigkeit soll mit jedem level schneller werden").
        public float Cruise => CruiseAt(Level);
        public float PaceScale => PaceScaleAt(Level);
        public float CruiseAt(int level) => Mathf.Min(Mathf.Max(cruiseStart, cruiseMax), cruiseStart + cruisePerLevel * (Mathf.Max(1, level) - 1));
        public float PaceScaleAt(int level) => Mathf.Min(Mathf.Max(speedScaleStart, speedScaleMax), speedScaleStart + speedPerLevel * (Mathf.Max(1, level) - 1));

        // Metres of a level: level 1 is levelDistance, a later one is longer by its base pace (cruise x top speed)
        // over the first level's, so at the base pace every level lasts the same levelSeconds.
        public float LevelLength(int level)
        {
            float first = Mathf.Max(1e-4f, CruiseAt(1) * PaceScaleAt(1));
            return Mathf.Max(1f, levelDistance) * CruiseAt(level) * PaceScaleAt(level) / first;
        }

        // Where a level begins, in metres from the start.
        public float LevelStartDistance(int level)
        {
            float d = 0f;
            int walked = Mathf.Min(level, MaxWalkedLevels);
            for (int k = 1; k < walked; k++) d += LevelLength(k);
            if (level > MaxWalkedLevels) d += (level - MaxWalkedLevels) * LevelLength(MaxWalkedLevels);
            return d;
        }

        public int LevelAt(float distance)
        {
            ProgressAt(distance, out int level);
            return level;
        }

        // Distance -> seconds at the base pace (every whole level counts levelSeconds) and the level it lies in.
        // Past MaxWalkedLevels the pace has long reached its caps, so the rest is one division.
        public float ProgressAt(float distance, out int level)
        {
            float d = Mathf.Max(0f, distance);
            float secs = Mathf.Max(1f, levelSeconds);
            int k = 1;
            for (; k < MaxWalkedLevels; k++)
            {
                float len = LevelLength(k);
                if (d < len)
                {
                    level = k;
                    return (k - 1 + d / len) * secs;
                }
                d -= len;
            }
            float last = LevelLength(k);
            level = k + Mathf.FloorToInt(d / last);
            return (k - 1 + d / last) * secs;
        }

        // The inverse: how far a run at the base pace gets in this many seconds (tests, the debug menu).
        public float DistanceForProgress(float seconds)
        {
            float t = Mathf.Max(0f, seconds) / Mathf.Max(1f, levelSeconds);
            int whole = Mathf.FloorToInt(t);
            return LevelStartDistance(whole + 1) + (t - whole) * LevelLength(whole + 1);
        }

        const int MaxWalkedLevels = 64;
        public int DriftingIslands { get; private set; }
        // The obstacles grow level by level ("mehr Inseln/Vulkane mit der Zeit"): extra islands on the far side of the
        // ring, and volcanoes that rise ahead of the player from volcanoFirstLevel on, spread evenly over each level.
        public int ExtraIslandsFor(int level) =>
            Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(0f, extraIslandsPerLevel) * Mathf.Max(0, level - 1) + 1e-4f), 0, Mathf.Max(0, extraIslandsAtMax));
        public int VolcanoesFor(float runSeconds)
        {
            float start = (Mathf.Max(1, volcanoFirstLevel) - 1) * Mathf.Max(1f, levelSeconds);
            if (volcanoesPerLevel <= 0f || runSeconds < start) return 0;
            int n = 1 + Mathf.FloorToInt((runSeconds - start) * volcanoesPerLevel / Mathf.Max(1f, levelSeconds) + 1e-4f);
            return Mathf.Clamp(n, 0, Mathf.Max(0, maxVolcanoes));
        }
        public int VolcanoTarget => VolcanoesFor(ProgressSeconds);

        // Tests and the debug menu: jump to a point of the difficulty curve.
        public void SetRunDistance(float metres) => _odometer.Distance = Mathf.Max(0f, metres);

        // One step of the "Schwung": held while a boost runs, otherwise it halves every momentumHalfLife seconds;
        // surfing a plate boundary (above momentumSurfStart) feeds it all the while. Pickups add momentumPerBoost
        // each (Encounters), a hit costs momentumHitLoss of it (Island.Bump).
        public float StepMomentum(float m, float dt, bool boosting, float surf)
        {
            if (dt <= 0f) return Mathf.Clamp01(m);
            if (!boosting) m *= Mathf.Exp(-0.6931472f / Mathf.Max(0.1f, momentumHalfLife) * dt);
            float s = Mathf.Clamp01((surf - momentumSurfStart) / Mathf.Max(0.05f, 1f - momentumSurfStart));
            m += momentumSurfRate * s * dt;
            return Mathf.Clamp01(m);
        }

        // A boost was collected (flotsam, whale): the chain builds the momentum.
        public void BoostCollected(Island island, float pieces = 1f)
        {
            if (island != null && _applied && island == player) island.AddMomentum(momentumPerBoost * Mathf.Max(0f, pieces));
        }

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
        readonly Dictionary<Island, Vector2> _pinned = new();
        System.Func<Vector2, Vector2> _constraint;
        float _travelDir = 1f, _pruneTimer, _volcanoRetry;
        RunOdometer _odometer;
        bool _holding;
        Vector2 _holdPos;

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
                    player.EscortFactor = 1f;
                    player.SetMomentum(0f);
                    player.EdgePush = Vector2.zero;
                    player.EdgeWarning = 0f;
                }
                EdgeWarning = 0f;
                _drift.Clear();
                _passDz.Clear();
                _lastHit.Clear();
                _pinned.Clear();
                _holding = false;
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
            if (Application.isPlaying) StepRace(Time.deltaTime);
        }

        void BeginRun()
        {
            RunSeconds = 0f;
            _odometer.Reset();
            _holding = false;
            _pinned.Clear();
            FillCount++;
            AdventureRunStats.Reset();
            _drift.Clear();
            _passDz.Clear();
            _lastHit.Clear();
            _travelDir = 1f;
            _volcanoRetry = 0f;
            Spawner.ExtraIslands = 0;
            if (player != null)
            {
                player.AdventureSpeedScale = speedScaleStart;
                player.AdventureCruise = 0f;
                player.AdventureSinkScale = 1f;
                player.AdventureTrack = Vector2.up;
                player.SetMomentum(0f);
            }
        }

        void OnBumped(Island hit, Island obstacle, Vector2 contact, float strength)
        {
            if (!_applied || hit != player) return;
            AdventureRunStats.Hits++;
            if (obstacle != null) _lastHit[obstacle] = RunSeconds;
        }

        // One frame of the race (Update runs it in Play Mode; tests call it directly): the start hold, the distance,
        // and the whole difficulty curve in one place - the pace of the player, how fast it sinks, how many obstacles
        // lie on the ring and how many of them drift across it.
        public void StepRace(float dt)
        {
            if (player == null) return;
            var g = Geometry;
            Vector2 pp = player.PlanarPosition;
            bool held = StartHeld;
            if (held != _holding)
            {
                bool released = _holding;
                _holding = held;
                _holdPos = pp;
                _pinned.Clear();
                if (released && !player.IsSunk && !Island.InputLocked) RaceStarted?.Invoke();
            }
            if (held)
            {
                player.SetSelfVelocity(Vector2.zero);
                if ((pp - _holdPos).sqrMagnitude > 1e-8f) player.SetPlanarPosition(pp = _holdPos);
            }
            // The island races whenever the player has the controls; the clock and the distance only count while
            // the island may sink as well (GameSession lets it exactly while the race really runs).
            bool racing = dt > 0f && !held && !player.IsSunk && !Island.InputLocked;
            bool running = racing && player.sinkEnabled;
            if (running) RunSeconds += dt;
            _odometer.Step(pp.y, running);
            float d = Difficulty;
            player.AdventureSpeedScale = PaceScale;
            // The sink ramp follows the clock, not the distance: with momentum ("Schwung") a run covers the same metres
            // in much less time, and a distance-driven ramp made the island sink faster the better it was driven
            // (good bot 174 s -> 63 s). Obstacles and pace stay on the distance.
            player.AdventureSinkScale = Mathf.Max(0.1f, sinkSpeed) * Mathf.Lerp(1f, sinkScaleMax, Mathf.Min(d, TimeDifficulty));
            player.AdventureCruise = racing ? Cruise : 0f;
            player.MomentumGain = momentumSpeedGain;
            player.MomentumHitLoss = momentumHitLoss;
            if (racing) player.SetMomentum(StepMomentum(player.Momentum, dt, player.Boosting, player.SurfStrength));
            else if (!held && player.IsSunk) player.SetMomentum(0f);
            // The band runs along z; the race always goes that way, so the heading, the camera and the surf lanes
            // all agree on where "ahead" is.
            player.AdventureTrack = Vector2.up;
            Spawner.ExtraIslands = ExtraIslandsFor(Level);
            StepEdge(g, pp, racing);

            _islands.Clear();
            Spawner.CollectIslands(_islands);
            if (held) PinIslands();
            else StepDrift(g, dt);
            StepPasses(g, pp);
            if (running) StepVolcanoes(g, pp, dt);

            _pruneTimer -= dt;
            if (_pruneTimer > 0f) return;
            _pruneTimer = 3f;
            Prune(_drift);
            Prune(_passDz);
            Prune(_lastHit);
            Prune(_pinned);
        }

        // During the start hold nothing on the track moves: every obstacle stays where it was when the hold began,
        // whatever the plate lanes or its own drift would do, so nothing can run into the waiting island.
        void PinIslands()
        {
            for (int i = 0; i < _islands.Count; i++)
            {
                var isl = _islands[i];
                if (isl == null || !isl.isActiveAndEnabled) continue;
                isl.SetSelfVelocity(Vector2.zero);
                if (!_pinned.TryGetValue(isl, out Vector2 at)) _pinned[isl] = isl.PlanarPosition;
                else if ((isl.PlanarPosition - at).sqrMagnitude > 1e-8f) isl.SetPlanarPosition(at);
            }
        }

        // One volcano at a time rises ahead of the player whenever the level asks for more than there are; a spot that
        // would close the track (or lies in a storm) is refused by the spawner and tried again a moment later.
        void StepVolcanoes(RingGeometry g, Vector2 pp, float dt)
        {
            _volcanoRetry -= dt;
            if (_volcanoRetry > 0f || Spawner.VolcanoCount >= VolcanoTarget) return;
            _volcanoRetry = 0.4f;
            float speed = Mathf.Max(4f, Mathf.Abs(player.TrackSpeed));
            if (Spawner.TryRaiseVolcano(g, pp, _travelDir, speed, player.BoundingRadius) != null) _volcanoRetry = 2f;
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
        // so the island is never squashed onto the edge fold of the bend. During the start hold it keeps the island
        // on its spot: the plate current, a bump or the surf cannot move it there either.
        public Vector2 ClampPlayer(Vector2 p)
        {
            if (_holding) return _holdPos;
            float margin = player != null ? player.BoundingRadius * playerWallMargin : 0f;
            return Geometry.ClampAcross(p, margin);
        }

        void Prune<T>(Dictionary<Island, T> map)
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
                // A volcano stands on its rift: it never drifts, so the free lane it was placed beside stays free.
                if (isl.isVolcano || Hash01(isl.shapeSeed, 3) >= share)
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
                    // Gliding through an island in the grace after a hit is not a dodge either.
                    if (!hit && !player.HitGrace && !player.Ghosting && gap < dodgeGap)
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
