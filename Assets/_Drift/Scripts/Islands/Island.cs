using System;
using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using Drift.Tectonics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Drift.Islands
{
    [ExecuteAlways]
    public class Island : MonoBehaviour, IIslandSurface, IPlateRider
    {
        static readonly List<Island> Registry = new();

        public static IReadOnlyList<Island> All => Registry;

        public static event Action<float> Impact;
        public static event Action<Island, Island, float> Merged;
        // Adventure: the player bumped into an obstacle island (player, obstacle, contact in world xz, 0..1 strength).
        // Only the counted hits (outside the cooldown) are reported.
        public static event Action<Island, Island, Vector2, float> Bumped;
        // When set and returning a non-zero vector, replaces keyboard input for the player island (touch UI).
        public static Func<Vector2> InputProvider;
        // Direct-direction steering (tilt, and the same scheme for touch and WASD): a WORLD-space XZ direction,
        // length 0..1 = share of full thrust. The island drives straight that way without turning into it first
        // ("es gibt kein vorne, sie treibt einfach nur in die Richtung").
        public static Func<Vector2> DirectionProvider;
        public static bool DirectionSteering;
        public static bool InputLocked;

        public float landRadius = 3f;
        public float cellSize = 0.5f;
        [Tooltip("Höchsttempo der Insel aus eigener Kraft (kleine Insel; große sind etwas langsamer).")]
        [Range(2f, 25f)] public float moveSpeed = 8f;
        [Tooltip("Schub beim Gasgeben (u/s²). Höher = die Insel ist schneller auf Tempo.")]
        public float acceleration = 9f;
        [Tooltip("Wie stark große, träge Inseln langsamer beschleunigen (0 = gar nicht, 1 = voll mit der Wendigkeit).")]
        [Range(0f, 1f)] public float accelAgilityExponent = 0.35f;
        public float dragBase = 0.45f;
        public float turnRateDegPerSec = 90f;
        public float turnResponse = 3f;
        public float turnEaseIn = 0.35f;
        public float velocityAlign = 2.5f;
        [Tooltip("Richtungssteuerung: mit wie viel Grad pro Sekunde die Blickrichtung (und damit die Kamera) der Fahrtrichtung nachzieht. 0 = gar nicht.")]
        [Range(0f, 90f)] public float directionHeadingFollow = 0f;
        [Tooltip("Richtungssteuerung: welchen Anteil des Höchsttempos ein ganz leichter Ausschlag gibt. Voller Ausschlag ist immer volles Tempo.")]
        [Range(0.05f, 1f)] public float directionMinSpeedShare = 0.35f;
        [Tooltip("Wie weit sich die Insel in eine Kurve legt (Grad). 0 = aus.")]
        [Range(0f, 12f)] public float leanIntoTurns = 3.5f;
        [Tooltip("Wie schnell die Insel sich in die Kurve legt und wieder aufrichtet.")]
        [Range(0.5f, 10f)] public float leanResponse = 3f;
        public float driftRotation = 2f;
        public float driftPeriod = 40f;
        // Merge turn = a critically damped angular spring. bodyTurnTime / bodyTurnMaxRate: seconds to get within
        // 5 % of the target and yaw rate cap (deg/s) for a start-sized island (bodyTurnRefArea); the ...Huge
        // values hold from bodyTurnHugeArea on, in between it follows the square root of the Agility.
        // v0.6.3 -> v0.6.4 (owner: "zu stark und zu ruckartig"): half as fast (times x2, rate caps /2) and a third
        // of the angle (bodyTurnAmount).
        public float bodyTurnTime = 5f;
        public float bodyTurnTimeHuge = 10f;
        public float bodyTurnMaxRate = 35f;
        public float bodyTurnMaxRateHuge = 17.5f;
        [Range(0f, 1f)] public float bodyTurnAmount = 1f / 3f;
        public float bodyTurnRefArea = 24f;
        public float bodyTurnHugeArea = 2000f;
        public float bodyTurnCooldown = 8f;
        public float bodyTurnRapidAngle = 25f;
        public float bodyTurnMaxQueued = 40f;
        public float agilityArea = 40f;
        public bool useKeyboardInput = true;
        public int shapeSeed = 12345;
        public float heightOffset = 0f;
        public float bobAmplitude = 0.05f;
        public float carryResponse = 2.5f;
        [Tooltip("Wie schnell die Insel den Surf-Schub an einer Plattengrenze aufnimmt und wieder verliert.")]
        [Range(0.2f, 10f)] public float surfResponse = 2f;
        public bool isVolcano;
        public IslandKind kind = IslandKind.Regular;
        public IslandArchetype archetype = IslandArchetype.Classic;
        public float impactRetention = 0.8f;
        public float impactDrag = 0.5f;
        public bool sinkEnabled = true;
        // Seconds from full buoyancy to sunk for a round island in calm water (SinkSecondsForArea):
        // sinkSecondsHuge + (sinkSecondsSmall - sinkSecondsHuge) * sinkHalfArea / (sinkHalfArea + area).
        public float sinkSecondsSmall = 95f;
        public float sinkSecondsHuge = 10f;
        public float sinkHalfArea = 250f;
        // Land still above water = Buoyancy ^ sinkLandExponent (below 1: the shore goes first, the rest late).
        public float sinkLandExponent = 0.4f;
        // A guest restores RefloatFactor * (guestArea / hostArea) ^ refloatExponent of the full buoyancy.
        public float refloatExponent = 0.55f;
        // Land may never vanish faster than this share of the island per second. Without it the last ~15 % of
        // the land went under between two 0.5 s mesh refreshes (Buoyancy^0.4 falls off a cliff near zero) and
        // the run ended while the island still looked whole. It also gives the timer a visible last act: once
        // the buoyancy bar is empty the remaining hilltops keep sinking at this rate until they are gone.
        public float maxLandLossPerSecond = 0.05f;
        // Sunk means sunk: the run only ends when no cell is above water any more (one cell = 0.25 area).
        public float sunkArea = 0.2f;
        public Vector2 startPlanarPosition = Vector2.zero;
        public float stormSinkMultiplier = 1.6f;
        public float elongatedSinkMultiplier = 1.6f;
        public const float DefaultBarrenHeightScale = 0.55f;
        public const float DefaultBarrenRadiusScale = 0.8f;
        public float barrenHeightScale = DefaultBarrenHeightScale;
        public float barrenRadiusScale = DefaultBarrenRadiusScale;
        public float startleDuration = 3f;

        [Header("Sinken (Gemütlich)")]
        [Tooltip("Wie schnell die Insel im gemütlichen Modus sinkt (1 = so schnell wie im Abenteuer bei gleicher Landfläche, kleiner = gemächlicher). Große Inseln sinken schneller, kleine langsamer; untergehen kann sie nie.")]
        [Range(0f, 3f)] public float cozySinkSpeed = 0.6f;
        [Tooltip("Anteil des gesammelten Landes, der zu Beginn der Reise mindestens über Wasser bleibt. Darunter sinkt die Insel nicht weiter (nie kleiner als die Startinsel).")]
        [Range(0f, 1f)] public float cozyKeepShareStart = 0.5f;
        [Tooltip("Anteil des gesammelten Landes, der mindestens über Wasser bleibt, wenn alle Inseln der Welt vereint sind. Dazwischen wächst die Mindestgröße mit dem Anteil der vereinten Inseln.")]
        [Range(0f, 1f)] public float cozyKeepShareEnd = 0.9f;
        [Tooltip("Sanftes Ausrollen: in diesem Anteil der Landfläche über der Mindestgröße wird das Sinken immer langsamer, bis es ganz aufhört.")]
        [Range(0.01f, 0.5f)] public float cozyEaseShare = 0.12f;
        [Tooltip("Liegt die Insel unter ihrer Mindestgröße (weil die Reise vorangekommen ist), taucht sie in etwa so vielen Sekunden bis dorthin wieder auf.")]
        [Range(1f, 120f)] public float cozyRiseSeconds = 20f;

        [Header("Sinken und Schub (Abenteuer)")]
        [Tooltip("Im Abenteuer sinkt die Insel auf Zeit, und die Runde ist verloren, wenn sie untergeht. Aus = die Insel sinkt gar nicht (zum Ausprobieren).")]
        public bool adventureSinking = true;

        [Header("Abenteuer-Fahrt")]
        [Tooltip("Bremsen: wie viel vom Grundtempo beim vollen Zurückziehen mindestens übrig bleibt. Ganz anhalten oder rückwärts fahren geht im Abenteuer nie.")]
        [Range(0.1f, 1f)] public float adventureBrakeMin = 0.45f;
        [Tooltip("Wie kräftig gebremst wird: Vielfaches des normalen Schubs.")]
        [Range(0.5f, 6f)] public float adventureBrakeForce = 2.2f;
        [Tooltip("Lenken: seitliches Tempo bei vollem Ausschlag als Anteil des Höchsttempos. Größer = die Insel zieht schneller quer über die Bahn.")]
        [Range(0.2f, 1.6f)] public float adventureSteerShare = 0.9f;

        [Header("Zusammenstoß mit Hindernissen (Abenteuer)")]
        [Tooltip("Anteil des vollen Auftriebs, den ein Zusammenstoß mit einer Insel kostet. Mehrere Treffer sind zu überleben, Ausweichen lohnt sich trotzdem deutlich.")]
        [Range(0f, 0.6f)] public float hitBuoyancyLoss = 0.18f;
        [Tooltip("Anteil des Tempos längs der Küste, der nach einem Treffer bleibt.")]
        [Range(0f, 1f)] public float hitSpeedKeep = 0.35f;
        [Tooltip("Wie kräftig die Insel abprallt (0 = sie bleibt kleben, 1 = so schnell zurück, wie sie gekommen ist).")]
        [Range(0f, 1.5f)] public float hitBounce = 0.7f;
        [Tooltip("Mindesttempo (u/s), mit dem die Insel vom Hindernis wegprallt.")]
        [Range(0f, 12f)] public float hitMinBounce = 5f;
        [Tooltip("Sekunden nach einem Treffer, in denen dieselbe Berührung nicht noch einmal zählt.")]
        [Range(0f, 4f)] public float hitCooldown = 1.2f;
        [Tooltip("Sekunden nach einem Treffer ohne Grundtempo, damit man nicht sofort wieder hineinfährt.")]
        [Range(0f, 2f)] public float hitStun = 0.6f;
        [Tooltip("Wie schnell (Anteil des Landes pro Sekunde) die Insel nach einem Treffer auf ihre neue Tiefe absackt.")]
        [Range(0.02f, 1f)] public float hitSinkRate = 0.15f;
        [Tooltip("Sekunden nach einem Treffer, in denen die Insel durch Hindernisse hindurchgleitet (blinkt): ein Fehler soll keine Trefferserie auslösen. 0 = aus.")]
        [Range(0f, 4f)] public float hitGrace = 2.2f;
        [Tooltip("Seitlicher Schub (u/s) nach einem Treffer, weg vom Hindernis – die Insel kommt frei, statt gleich wieder hineinzufahren.")]
        [Range(0f, 16f)] public float hitSidestep = 7f;

        [Header("Bergbildung")]
        [Tooltip("Sekunden, in denen das neue Bergland nach einem Zusammenstoß aufsteigt (kleiner = schneller). Höhere Berge brauchen etwas länger.")]
        [Range(0.5f, 30f)] public float upliftDuration = DefaultUpliftDuration;
        [Tooltip("Wie stark ein Zusammenstoß das Land hebt (1 = normal, kleiner = flachere Berge).")]
        [Range(0.2f, 2f)] public float mountainHeight = DefaultMountainHeight;
        [Tooltip("Wie weit sich die Hebung über die Insel verteilt: 1 = schmaler Grat an der Nahtstelle, größer = breites, sanftes Hügelland über die ganze Insel.")]
        [Range(1f, 4f)] public float upliftSpread = DefaultUpliftSpread;
        [Tooltip("Steilste erlaubte Hangneigung in Grad. Steilere Hänge rutschen nach einem Zusammenstoß langsam ab; Strände und Küstenlinie bleiben unberührt.")]
        [Range(10f, 70f)] public float maxSlope = DefaultMaxSlope;
        [Tooltip("Steilste Neigung eines Vulkankegels in Grad; gilt auch für Gipfel oberhalb der Schneegrenze.")]
        [Range(10f, 70f)] public float volcanoMaxSlope = DefaultVolcanoMaxSlope;
        public const float DefaultUpliftDuration = 7f;
        public const float DefaultMountainHeight = 1f;
        public const float DefaultUpliftSpread = 2.5f;
        public const float DefaultMaxSlope = 30f;
        public const float DefaultVolcanoMaxSlope = 45f;

        IslandShape _shape;
        IslandHerdSystem _herds;
        Mesh _mesh;
        Color[] _colors;
        Vector2 _pos;
        Vector2 _carry;
        Vector2 _surf;
        Vector2 _selfVel;
        Vector2 _driveDir;
        float _lean;
        float _turnVel;
        float _clock;
        float _driftPhase;
        float _bodyYaw;
        Vector2 _bodyFwd2 = Vector2.up;
        Vector2 _bodyRight2 = Vector2.right;
        [NonSerialized] float _turnFeed;
        [NonSerialized] float _turnError;
        [NonSerialized] float _turnRate;
        [NonSerialized] float _lastMergeClock = -1e9f;
        [NonSerialized] IslandHypsometry _hypso;
        [NonSerialized] bool _hypsoDirty = true;
        [NonSerialized] float _buoy = 1f;
        float _upliftT = 1f;
        float _upliftMeshTimer, _upliftHypsoTimer, _upliftVersionTimer;
        float _upliftDur = 1.5f;
        float _bobPhase;
        int _version;
        float _boundRadius;
        float _area;
        float _areaRaw;
        float _sinkMeshTimer;
        float _sinkVersionAccum;
        float _emergeTime;
        float _emergeSpeed;
        [NonSerialized] float _boostLeft;
        [NonSerialized] float _boostStrength = 1f;
        [NonSerialized] float _boostDuration = 1f;
        [NonSerialized] float _startArea;
        [NonSerialized] float _hitCooldownLeft, _hitStunLeft, _hitSinkLeft, _hitGraceLeft;
        [NonSerialized] float _staggerLeft, _staggerFactor = 1f;
        [NonSerialized] float _ghostLeft, _ghostTouch;

        // The ring's rim (RingWorld, Adventure only): near the edge of the band the water foams and pushes gently
        // back towards the middle; the hard stop is PositionConstraint. Never set in Cozy.
        [NonSerialized] public Vector2 EdgePush;
        [NonSerialized] public float EdgeWarning;

        // Adventure only, written by RingWorld as the difficulty rises (1 / 0 / 1 = as the sliders say).
        [NonSerialized] public float AdventureSpeedScale = 1f;
        // Share of the top speed the island keeps up by itself in Adventure (the ring's base pace).
        [NonSerialized] public float AdventureCruise;
        [NonSerialized] public float AdventureSinkScale = 1f;
        // The direction of travel along the adventure track (RingWorld writes it; +z until then).
        [NonSerialized] public Vector2 AdventureTrack = Vector2.up;
        // Adventure: the steady extra pace of the sea animals swimming alongside (Visuals/Encounters writes it every
        // frame, 1 = nobody). A factor on the top speed like AdventureSpeedScale - deliberately not a SpeedBoost, so
        // Boosting stays the rare flotsam event instead of being on whenever an escort swims along.
        [NonSerialized] public float EscortFactor = 1f;
        // Adventure "Schwung" (0..1): chained boost pickups and surfing build it up, RingWorld lets it decay and a hit
        // halves it. The top speed (pace, steering and thrust alike) grows by MomentumGain x Momentum - owner: the top
        // speed doubles, the boosts themselves stay as strong as they were (see the boost block in Tick).
        [NonSerialized] public float MomentumGain = 1f;
        [NonSerialized] public float MomentumHitLoss = 0.5f;
        public float Momentum { get; private set; }
        public void SetMomentum(float value) => Momentum = Mathf.Clamp01(value);
        public void AddMomentum(float amount) => Momentum = Mathf.Clamp01(Momentum + Mathf.Max(0f, amount));
        public float MomentumScale => AdventurePlayer ? 1f + Mathf.Max(0f, MomentumGain) * Momentum : 1f;

        public Vector3 Normal { get; private set; } = Vector3.up;
        // Heading: steering, thrust and the chase camera. Only player input turns it.
        public Vector3 Forward { get; private set; } = Vector3.forward;
        // The heightfield/transform is yawed by BodyYaw relative to the heading (merges and the ambient
        // drift turn the body, never the heading), so local space = body space everywhere.
        public float BodyYaw => _bodyYaw;
        public Vector3 BodyForward => new Vector3(_bodyFwd2.x, 0f, _bodyFwd2.y);

        public event Action Sunk;
        public bool IsSunk { get; private set; }
        public float Sink => _shape != null ? _shape.sink : 0f;
        public float LandFraction => _areaRaw > 0f ? Mathf.Clamp01(_area / _areaRaw) : 1f;
        public float FullArea => _areaRaw;

        // Which rules apply: GameModes.Current unless a test pins it.
        [NonSerialized] public GameMode? ModeOverride;
        public GameMode Mode => ModeOverride ?? GameModes.Current;

        // Share of the world's planned islands already merged (0..1); WorldStreamer keeps it current for the player.
        // Raises the cozy minimum size from cozyKeepShareStart to cozyKeepShareEnd.
        [NonSerialized] public float RunProgress;

        // Land area of the start island (the cozy floor never goes below it); taken when the player's start shape is
        // generated, a restored save keeps the value of the shape generated before it.
        public float StartArea
        {
            get => _startArea > 0f ? _startArea : _areaRaw;
            set => _startArea = Mathf.Max(0f, value);
        }

        // Adventure with sinking on: the run is lost when the island goes under. Cozy never sinks completely.
        public bool SinkIsLethal => Mode == GameMode.Adventure && adventureSinking;

        // Cozy: the land area the island keeps above water whatever happens; 0 in Adventure.
        public float SinkFloorArea
        {
            get
            {
                if (Mode != GameMode.Cozy || !useKeyboardInput) return 0f;
                float keep = Mathf.Lerp(cozyKeepShareStart, cozyKeepShareEnd, Mathf.Clamp01(RunProgress));
                return Mathf.Min(_areaRaw, Mathf.Max(StartArea, keep * _areaRaw));
            }
        }

        float FloorShare => _areaRaw > 0f ? Mathf.Clamp01(SinkFloorArea / _areaRaw) : 0f;

        // 1 = fully afloat, 0 = as low as the island can go (cozy: resting on its minimum size, adventure: sunk).
        public float SinkHeadroom
        {
            get
            {
                if (!useKeyboardInput || _shape == null) return LandFraction;
                SyncBuoyancy();
                float floor = BuoyancyAtLandShare(FloorShare);
                if (floor >= 0.999f) return 1f;
                return Mathf.Clamp01((_buoy - floor) / (1f - floor));
            }
        }

        // Cozy: the island has settled on its minimum size and does not sink any further.
        public bool SinkResting => Mode == GameMode.Cozy && useKeyboardInput && SinkHeadroom < 0.02f;

        // Where the HUD bar of a cozy island stands when it rests on its minimum: above every sinking warning
        // (HUD 0.55, audio 0.35), because in cozy there is nothing to warn about.
        public const float CozyBarAtRest = 0.6f;

        // The player's buoyancy: in Adventure the share of its sink time that is left (1 = afloat, 0 = sunk; it drains
        // linearly, the HUD bar is a timer); in Cozy the bar runs from 1 down to CozyBarAtRest when the island rests
        // on its minimum size (SinkHeadroom is the plain 0..1 value). Every other island reports its land share.
        public float Buoyancy
        {
            get
            {
                if (!useKeyboardInput || _shape == null) return LandFraction;
                SyncBuoyancy();
                return Mode == GameMode.Cozy ? Mathf.Lerp(CozyBarAtRest, 1f, SinkHeadroom) : _buoy;
            }
        }

        // The internal buoyancy state (land above water = RawBuoyancy ^ sinkLandExponent), whatever the mode.
        public float RawBuoyancy
        {
            get
            {
                if (!useKeyboardInput || _shape == null) return LandFraction;
                SyncBuoyancy();
                return _buoy;
            }
        }

        public float SinkSecondsForArea(float area) => SinkBalance.SecondsForArea(area, sinkSecondsSmall, sinkSecondsHuge, sinkHalfArea);
        // Storm and form penalty on top of the size curve.
        // In the race a storm may only push the island off its line, never speed up the sinking (owner: "die stürme
        // sollen nur die insel abdriften lassen").
        public float SinkMultiplier => Mathf.Lerp(1f, stormSinkMultiplier, AdventureRacing ? 0f : StormIntensity)
            * Mathf.Lerp(elongatedSinkMultiplier, 1f, Compactness);
        // Full-buoyancy sink time of this island as it is now (size, storm, form, and the adventure difficulty).
        public float SinkSecondsFull => SinkSecondsForArea(_areaRaw) / Mathf.Max(0.01f, SinkMultiplier * (AdventurePlayer ? Mathf.Max(0.1f, AdventureSinkScale) : 1f));

        // The player island in Adventure: an obstacle course, no merges, a base pace and a harder sink as time goes on.
        public bool AdventurePlayer => useKeyboardInput && Mode == GameMode.Adventure;

        // Estimate at the current rate; infinite while sinking is held, when it cannot sink the island (Cozy,
        // Adventure with sinking off) or for islands that never sink.
        public float SinkSecondsLeft
        {
            get
            {
                if (!sinkEnabled || !useKeyboardInput || _shape == null || !SinkIsLethal) return float.PositiveInfinity;
                return IsSunk ? 0f : Buoyancy * SinkSecondsFull;
            }
        }

        // A timed speed boost (Adventure: flotsam), fading out in the last third.
        public bool Boosting => _boostLeft > 0f;
        public float BoostRemaining => Mathf.Max(0f, _boostLeft);
        // Multiplier on the top speed right now (1 = no boost); for the HUD and effects.
        public float BoostFactor => _boostLeft > 0f ? 1f + (Mathf.Max(1f, _boostStrength) - 1f) * BoostEnvelope : 1f;
        float BoostEnvelope => Mathf.Clamp01(_boostLeft / Mathf.Max(0.01f, BoostFadeShare * _boostDuration));
        const float BoostFadeShare = 0.35f;
        const float BoostResponse = 2.5f;

        // A temporary speed boost: the top speed times factor for seconds (fading out in the last third), the island
        // surging forward on its own. A boost during another one keeps the stronger factor and the longer time.
        public void SpeedBoost(float seconds, float factor)
        {
            if (seconds <= 0f || factor <= 1f) return;
            _boostStrength = Mathf.Max(factor, BoostFactor);
            _boostLeft = Mathf.Max(seconds, _boostLeft);
            _boostDuration = _boostLeft;
        }

        // A temporary slow-down (lightning, scraping a ship): for seconds the island may only reach speedFactor of
        // its speed. It is not a stop and it is not cancelled by the ring's cruise floor - the floor is scaled by it
        // too, so a staggered island really is slower than one that just lets go of the stick. The harsher and the
        // longer of two overlapping staggers wins.
        public void Stagger(float seconds, float speedFactor)
        {
            if (seconds <= 0f) return;
            float f = Mathf.Clamp(speedFactor, 0.05f, 1f);
            if (_staggerLeft > 0f)
            {
                _staggerFactor = Mathf.Min(_staggerFactor, f);
                _staggerLeft = Mathf.Max(_staggerLeft, seconds);
            }
            else
            {
                _staggerFactor = f;
                _staggerLeft = seconds;
            }
        }

        public bool Staggered => _staggerLeft > 0f;
        public float StaggerRemaining => Mathf.Max(0f, _staggerLeft);
        // 1 = free, below 1 = the share of the speed the island may reach right now.
        public float StaggerFactor => _staggerLeft > 0f ? Mathf.Clamp(_staggerFactor, 0.05f, 1f) : 1f;

        // Refloats the player by share (0..1) of the full buoyancy, like a merge does, clamped at fully afloat.
        // No effect when the island cannot sink (not the player, or Adventure with sinking off).
        public void AddBuoyancy(float share)
        {
            if (!useKeyboardInput || _shape == null || share <= 0f || IsSunk) return;
            if (Mode == GameMode.Adventure && !adventureSinking) return;
            SyncBuoyancy();
            float before = _shape.sink;
            _buoy = Mathf.Clamp01(_buoy + share);
            _shape.sink = _hypso.DepthAt(LandShareAt(_buoy));
            if (Mathf.Abs(_shape.sink - before) < 1e-5f) return;
            RecomputeStats();
            if (_mesh != null) _shape.RefreshHeights(_mesh, false);
            else RebuildMesh();
            _sinkMeshTimer = 0f;
            _sinkVersionAccum = 0f;
            _version++;
        }

        // Takes share (0..1) of the full buoyancy away (a hit against an obstacle island). The bar answers at once;
        // the waterline follows at hitSinkRate, so the land visibly sinks a step instead of jumping.
        // Brings the island to a real standstill (own drive, plate carry and surf): the finished Pangäa must not
        // drift on during the fly-over, and the water must not draw a bow wave for a current it no longer feels.
        public void StopDrift()
        {
            _selfVel = Vector2.zero;
            _carry = Vector2.zero;
            _surf = Vector2.zero;
        }

        public void RemoveBuoyancy(float share)
        {
            if (!useKeyboardInput || _shape == null || share <= 0f || IsSunk) return;
            if (Mode == GameMode.Adventure && !adventureSinking) return;
            SyncBuoyancy();
            _buoy = Mathf.Clamp01(_buoy - share);
            LastHitLoss = share;
            _hitSinkLeft = HitSinkSeconds;
        }

        const float HitSinkSeconds = 2.5f;

        // Share of the full buoyancy the last hit cost.
        public float LastHitLoss { get; private set; }
        // Counted hits against obstacle islands since the island was (re)generated.
        public int Hits { get; private set; }
        public float HitCooldownLeft => Mathf.Max(0f, _hitCooldownLeft);
        public bool HitStunned => _hitStunLeft > 0f;
        // Right after a counted hit the island glides through obstacles for hitGrace seconds (the HUD/visuals may
        // let it blink): one mistake used to cascade into a hit series, because the slowed, stunned island was
        // carried straight back into the same coast (play test: up to 5 hits in 19 s).
        public bool HitGrace => _hitGraceLeft > 0f;
        public float HitGraceRemaining => Mathf.Max(0f, _hitGraceLeft);

        // Adventure whale boost: for its seconds the island passes through obstacle islands untouched - no bounce, no
        // hit, no buoyancy lost. It never ends inside a coast: every contact keeps it alive for GraceHold more.
        public bool Ghosting => _ghostLeft > 0f;
        public float GhostRemaining => Mathf.Max(0f, _ghostLeft);
        // Gliding through an island right now (spray, the whale's cue).
        public bool GhostPassing => _ghostLeft > 0f && _ghostTouch > 0f;
        public void Ghost(float seconds)
        {
            if (seconds > 0f) _ghostLeft = Mathf.Max(_ghostLeft, seconds);
        }

        // Adventure: the player ran into an obstacle island. Bounces off along the contact normal (always at least
        // hitMinBounce, so even a slow scrape pushes clear), keeps only hitSpeedKeep of the speed along the coast and
        // - outside the cooldown - costs hitBuoyancyLoss of the buoyancy, then gets a sideways shove clear of the
        // obstacle and hitGrace seconds of gliding through. Returns true when the hit counted.
        public bool Bump(Island obstacle, Vector2 contactLocal, float dt)
        {
            if (obstacle == null || _shape == null) return false;
            if (_ghostLeft > 0f)
            {
                _ghostLeft = Mathf.Max(_ghostLeft, GraceHold);
                _ghostTouch = GraceHold;
                return false;
            }
            // Gliding through: the grace does not run out while the island is still inside a coast, or it would
            // end in the middle of a big island and count the next hit at once.
            if (_hitGraceLeft > 0f)
            {
                _hitGraceLeft = Mathf.Max(_hitGraceLeft, GraceHold);
                return false;
            }
            Vector2 contact = ToWorld(contactLocal);
            Vector2 n = _pos - contact;
            if (n.sqrMagnitude < 1e-4f) n = _pos - obstacle._pos;
            n = n.sqrMagnitude > 1e-6f ? n.normalized : -Forward2;

            float closing = Mathf.Max(0f, -Vector2.Dot(PlanarVelocity - obstacle.PlanarVelocity, n));
            // The plate current and the surf keep pushing into the island otherwise, and the contact never ends.
            float carryIn = Vector2.Dot(_carry, n);
            if (carryIn < 0f) _carry -= n * carryIn;
            float surfIn = Vector2.Dot(_surf, n);
            if (surfIn < 0f) _surf -= n * surfIn;

            float selfN = Vector2.Dot(_selfVel, n);
            float bounce = Mathf.Max(hitMinBounce, closing * hitBounce);
            if (selfN < bounce) _selfVel += n * (bounce - selfN);
            if (dt > 0f) _pos += n * (Mathf.Max(1f, hitMinBounce) * dt);
            ApplyTransform();

            if (_hitCooldownLeft > 0f) return false;
            _hitCooldownLeft = hitCooldown;
            _hitStunLeft = hitStun;
            _hitGraceLeft = Mathf.Max(0f, hitGrace);
            Hits++;
            Momentum *= Mathf.Clamp01(1f - MomentumHitLoss);
            selfN = Vector2.Dot(_selfVel, n);
            _selfVel = n * selfN + (_selfVel - n * selfN) * Mathf.Clamp01(hitSpeedKeep);
            Sidestep(obstacle);
            _boostLeft = 0f;
            RemoveBuoyancy(hitBuoyancyLoss);
            if (_herds == null) _herds = GetComponent<IslandHerdSystem>();
            if (_herds != null) _herds.Startle(contactLocal, startleDuration);
            float intensity = Mathf.Clamp01(0.35f + closing / 14f);
            Impact?.Invoke(intensity);
            Bumped?.Invoke(this, obstacle, contact, intensity);
            return true;
        }

        const float GraceHold = 0.2f;

        // The shove clear after a hit: across the track in the race (along it the ring keeps its own pace), across
        // the heading otherwise; towards the side of the obstacle the island already is on, unless the rim of the
        // band leaves no room there. A dead-centre hit picks a side from the obstacle's seed, so it stays deterministic.
        void Sidestep(Island obstacle)
        {
            if (hitSidestep <= 0f) return;
            Vector2 track = AdventureRacing ? TrackDirection : Forward2;
            Vector2 across = new Vector2(track.y, -track.x);
            float off = Vector2.Dot(_pos - obstacle._pos, across);
            float side = off >= 0f ? 1f : -1f;
            if (Mathf.Abs(off) < 0.25f) side = ((obstacle.shapeSeed ^ Hits) & 1) == 0 ? 1f : -1f;
            float need = obstacle.BoundingRadius + _boundRadius - Mathf.Abs(off);
            if (PositionConstraint != null && need > 0f)
            {
                Vector2 want = _pos + across * (side * need);
                if ((PositionConstraint(want) - want).sqrMagnitude > 1f) side = -side;
            }
            float lateral = Vector2.Dot(_selfVel, across);
            if (lateral * side < hitSidestep) _selfVel += across * (side * hitSidestep - lateral);
        }

        // The ring world ends at its rims and holds the island inside (RingWorld.PositionConstraint): there is no way
        // off the band, so an adventure run can only ever end by sinking. Kept so the game-over screen, which asks
        // how the run ended, still compiles.
        public bool LostOverEdge => false;

        // Share of the full buoyancy the last absorbed island gave back (after the kind factor, before the clamp at full).
        public float LastRefloat { get; private set; }

        public float RefloatShareFor(Island guest) =>
            SinkBalance.RefloatShare(guest._area, _areaRaw, refloatExponent, guest.RefloatFactor);
        public float StormIntensity { get; private set; }
        // 1 = disc, towards 0 = line (IslandShape.Compactness); drives the elongation sink penalty.
        public float Compactness { get; private set; } = 1f;
        public Vector2 StormGust { get; private set; }
        public float Yaw => Mathf.Atan2(Forward.x, Forward.z) * Mathf.Rad2Deg;
        public bool IsBodyTurning => Mathf.Abs(BodyTurnRemaining) > 1e-3f || Mathf.Abs(_turnRate) > 1e-3f;
        // Signed yaw rate of the merge turn in degrees per second (clockwise positive).
        public float BodyTurnRate => _turnRate;
        // Signed angle the last merge queued (clockwise positive), after the rapid-merge scaling and the cap.
        public float LastBodyTurn { get; private set; }

        public float BodyTurnRemaining => _turnFeed + _turnError;

        public float RefloatFactor => RefloatFactorOf(kind);

        public static float RefloatFactorOf(IslandKind k)
        {
            switch (k)
            {
                case IslandKind.Volcanic: return 1.6f;
                case IslandKind.Barren: return 0.7f;
                default: return 1f;
            }
        }

        // Deterministic per seed so a streamed island has the same character in every copy of the world.
        public static IslandKind KindForSeed(int seed)
        {
            uint h = unchecked((uint)seed * 2654435761u + 0x9E3779B9u);
            h ^= h >> 15; h *= 0x85EBCA6Bu; h ^= h >> 13;
            float r = (h & 0xFFFFFFu) / (float)0x1000000;
            if (r < 0.15f) return IslandKind.Ancient;
            if (r < 0.30f) return IslandKind.Barren;
            return IslandKind.Regular;
        }

        public IslandKind DeriveKind()
        {
            if (isVolcano) return IslandKind.Volcanic;
            if (useKeyboardInput) return IslandKind.Regular;
            return KindForSeed(shapeSeed);
        }

        public Vector2 PlanarVelocity => _selfVel + _carry + _surf;
        // Motion against the water around it: its own drive plus the surf. The plate current moves island and water
        // alike, so it is left out (the wake and the bow foam are drawn from this).
        public Vector2 WaterVelocity => _selfVel + _surf;
        // 0..1: how hard the player island is riding a plate boundary right now.
        public float SurfStrength { get; private set; }
        public float CellArea => _shape != null ? _shape.cell * _shape.cell : 0.25f;
        public bool IsEmerging => _emergeTime > 0f;
        public float EmergeRemaining => Mathf.Max(0f, _emergeTime);

        public void ApplyImpactDrag(float dt) => _selfVel *= Mathf.Exp(-impactDrag * dt);
        public bool IsUplifting => _upliftT < 1f;
        public float Agility => AgilityFor(_area);
        public float AgilityFor(float area) => Mathf.Pow(agilityArea / (agilityArea + Mathf.Max(0f, area)), 0.6f);
        public float MaxSpeed => moveSpeed * (0.55f + 0.45f * Agility) * SpeedScale;
        float SpeedScale => AdventurePlayer ? Mathf.Max(0.1f, AdventureSpeedScale) * Mathf.Max(1f, EscortFactor) * MomentumScale : 1f;

        // Adventure is a race: the island always runs along the track and the input only steers sideways and
        // brakes. Whatever the steering scheme (direct direction or the old wheel), it comes down to the same
        // two numbers, so both play exactly the same.
        public bool AdventureRacing => AdventurePlayer && AdventureCruise > 0f && !IsSunk;

        // Unit direction of travel along the ring.
        public Vector2 TrackDirection
        {
            get
            {
                float len = AdventureTrack.magnitude;
                return len > 1e-4f ? AdventureTrack / len : Vector2.up;
            }
        }

        // The pace the ring keeps up by itself right now (u/s) and the slowest the brake can ever get.
        public float AdventureBaseSpeed => Mathf.Clamp01(AdventureCruise) * MaxSpeed * StaggerFactor;
        public float AdventureBrakeSpeed => AdventureBaseSpeed * adventureBrakeMin;
        // Sideways speed at full deflection; it grows with the top speed, so the line the island can weave over
        // the band stays the same while everything gets faster from level to level.
        public float AdventureSteerSpeed => adventureSteerShare * MaxSpeed;
        // Speed along the track right now - what the race is actually about.
        public float TrackSpeed => Vector2.Dot(PlanarVelocity, TrackDirection);

        const float RaceHeadingRate = 240f;
        const float RaceSteerResponse = 4f;

        public float MaxHeight
        {
            get
            {
                float m = 0f;
                for (int j = 0; j < _shape.nz; j++)
                    for (int i = 0; i < _shape.nx; i++)
                        m = Mathf.Max(m, _shape.Height(i, j));
                return m;
            }
        }

        // IIslandSurface
        public Transform SurfaceTransform => transform;
        public Rect LocalBounds => _shape.Bounds;
        public float SinkDepth => _shape != null ? _shape.sink : 0f;
        public float BoundingRadius => _boundRadius;
        public float LandArea => _area;
        public int Version => _version;
        public int Character => (int)kind;
        public int Biome => (int)BiomeOf(this);

        // The player's start island is temperate; every other island draws its biome from its shape seed.
        public static LifeBiome BiomeOf(Island island) => island.useKeyboardInput ? LifeBiome.Temperate : BiomeForSeed(island.shapeSeed);

        public static LifeBiome BiomeForSeed(int seed)
        {
            uint h = unchecked((uint)seed * 2246822519u + 0x85EBCA6Bu);
            h ^= h >> 13; h *= 0xC2B2AE35u; h ^= h >> 16;
            float r = (h & 0xFFFFFFu) / (float)0x1000000;
            if (r < 0.34f) return LifeBiome.Temperate;
            if (r < 0.58f) return LifeBiome.Tropical;
            if (r < 0.80f) return LifeBiome.Nordic;
            return LifeBiome.Savanna;
        }
        public float Speed => _selfVel.magnitude;
        public float SampleHeight(Vector2 localXZ) => _shape.Sample(localXZ);

        // Radial land profile in BODY space (reach[k] = distance to the last land along k * 360/len degrees,
        // 0 = body forward, 90 = body right); rotate by Yaw + BodyYaw for world space. Used by the water wake.
        public void LandReach(float[] reach)
        {
            if (_shape != null) _shape.LandReach(reach);
        }

        // IPlateRider
        public Vector2 PlanarPosition => _pos;
        // Optional clamp for the player's planar position (null = open sea). Set by the adventure ring world.
        public static Func<Vector2, Vector2> PositionConstraint;
        public Vector2 SelfVelocity => _selfVel;
        public float Mass => Mathf.Max(1f, _area);

        void OnEnable()
        {
            Normal = Vector3.up;
            Vector3 f = Vector3.ProjectOnPlane(transform.forward, Normal);
            f = f.sqrMagnitude > 0.001f ? f.normalized : Vector3.forward;
            // The transform carries heading + body yaw; a re-enable must not fold the body yaw into the heading.
            Forward = (Quaternion.AngleAxis(-_bodyYaw, Vector3.up) * f).normalized;
            _bodyYaw = 0f;
            _driveDir = Vector2.zero;
            ClearBodyTurns();
            _pos = new Vector2(transform.position.x, transform.position.z);
            _bobPhase = (Mathf.Abs(shapeSeed) % 97) * 0.13f;
            _driftPhase = (Mathf.Abs(shapeSeed) % 1000) * 0.00628f;
            _upliftDur = upliftDuration;
            IsSunk = false;

            GenerateShape();
            ApplyTransform();

            if (!Registry.Contains(this)) Registry.Add(this);
            PlateSystem.Register(this);
        }

        void OnDisable()
        {
            Registry.Remove(this);
            PlateSystem.Unregister(this);
        }

        void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
            _mesh = null;
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            Vector2 input = Vector2.zero;
            Vector2 drive = Vector2.zero;
            if (useKeyboardInput && !InputLocked)
            {
                if (DirectionSteering)
                {
                    if (DirectionProvider != null) drive = DirectionProvider();
                    if (drive.sqrMagnitude < 1e-6f) drive = KeysToDirection(ReadKeyboardInput());
                }
                else
                {
                    if (InputProvider != null) input = InputProvider();
                    if (input.sqrMagnitude < 1e-4f) input = ReadKeyboardInput();
                }
            }
            if (IsSunk) { input = Vector2.zero; drive = Vector2.zero; }
            Tick(input, drive, Time.deltaTime);
            AdvanceUplift(Time.deltaTime);
            AdvanceEmergence(Time.deltaTime);
            AdvanceSink(Time.deltaTime);
        }

        static Vector2 ReadKeyboardInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return Vector2.zero;
            float turn = 0f, throttle = 0f;
            if (kb.dKey.isPressed) turn += 1f;
            if (kb.aKey.isPressed) turn -= 1f;
            if (kb.wKey.isPressed) throttle += 1f;
            if (kb.sKey.isPressed) throttle -= 1f;
            return new Vector2(turn, throttle);
        }

        // Direct-direction steering for the keyboard: WASD name a direction on the screen, not a turn. The chase
        // camera looks along the heading, so the heading's own frame IS the screen frame.
        Vector2 KeysToDirection(Vector2 keys)
        {
            if (keys.sqrMagnitude < 1e-4f) return Vector2.zero;
            Vector2 f = Forward2;
            Vector2 r = new Vector2(f.y, -f.x);
            return Vector2.ClampMagnitude(r * keys.x + f * keys.y, 1f);
        }

        static float DriftWave(float t) => 0.65f * Mathf.Sin(t) + 0.35f * Mathf.Sin(t * 0.41f + 1.7f);

        public void Tick(Vector2 input, float dt) => Tick(input, Vector2.zero, dt);

        // driveDir: a world-space XZ direction of travel, length 0..1 = share of full thrust. While
        // DirectionSteering is on it replaces the input outright - the island pushes straight that way without
        // turning into it first. The heading is what the chase camera looks along, so it must NOT chase the
        // input: that would turn the very frame the direction is given in and make a held tilt circle for ever.
        public void Tick(Vector2 input, Vector2 driveDir, float dt)
        {
            bool direct = DirectionSteering && useKeyboardInput;
            bool race = AdventureRacing;
            Vector2 stick = Vector2.ClampMagnitude(driveDir, 1f);
            float push = 0f;
            if (direct)
            {
                if (_driveDir.sqrMagnitude < 1e-6f) _driveDir = Forward2;
                float len = driveDir.magnitude;
                if (len > 1e-4f)
                {
                    _driveDir = driveDir / len;
                    push = Mathf.Min(1f, len);
                }
                input = new Vector2(0f, push);
            }
            Vector2 drive2 = direct ? _driveDir : Forward2;
            float ag = Agility;
            float sq = Mathf.Sqrt(ag);
            _clock += dt;
            if (_hitCooldownLeft > 0f) _hitCooldownLeft = Mathf.Max(0f, _hitCooldownLeft - dt);
            if (_hitStunLeft > 0f) _hitStunLeft = Mathf.Max(0f, _hitStunLeft - dt);
            if (_hitGraceLeft > 0f) _hitGraceLeft = Mathf.Max(0f, _hitGraceLeft - dt);
            if (_ghostLeft > 0f) _ghostLeft = Mathf.Max(0f, _ghostLeft - dt);
            if (_ghostTouch > 0f) _ghostTouch = Mathf.Max(0f, _ghostTouch - dt);
            if (_staggerLeft > 0f) _staggerLeft = Mathf.Max(0f, _staggerLeft - dt);

            // The race reads both schemes into one pair of numbers: sideways = steer, backwards = brake. Pushing
            // forwards asks for more than the base pace, up to the top speed; it can never ask for less.
            Vector2 track = Vector2.up, across2 = Vector2.right;
            float steer = 0f, ahead = 0f, brake = 0f;
            if (race)
            {
                track = TrackDirection;
                across2 = new Vector2(track.y, -track.x);
                float along;
                if (direct)
                {
                    steer = Vector2.Dot(stick, across2);
                    along = Vector2.Dot(stick, track);
                }
                else
                {
                    steer = input.x;
                    along = input.y;
                }
                steer = Mathf.Clamp(steer, -1f, 1f);
                ahead = Mathf.Clamp01(along);
                brake = Mathf.Clamp01(-along);
                drive2 = track;
                _driveDir = track;
            }

            if (race)
            {
                // The chase camera looks along the heading, so in the race the heading lies on the track: left and
                // right on the screen are left and right on the band, in either steering scheme.
                _turnVel = 0f;
                float toA = Mathf.Atan2(track.x, track.y) * Mathf.Rad2Deg;
                float a = Mathf.MoveTowardsAngle(Yaw, toA, RaceHeadingRate * Mathf.Max(0f, dt)) * Mathf.Deg2Rad;
                Forward = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            }
            else
            {
                // Steering: a turn builds up linearly over turnEaseIn (angle grows quadratically, so it starts
                // soft) and winds down exponentially, both only mildly slower for heavy islands; the top rate
                // still scales with the full Agility.
                float targetTurn = input.x * turnRateDegPerSec * ag;
                if (Mathf.Abs(targetTurn) > Mathf.Abs(_turnVel) && targetTurn * _turnVel >= 0f)
                    _turnVel = Mathf.MoveTowards(_turnVel, targetTurn, turnRateDegPerSec * sq * dt / Mathf.Max(0.05f, turnEaseIn));
                else
                    _turnVel = Mathf.Lerp(_turnVel, targetTurn, 1f - Mathf.Exp(-2f * turnResponse * sq * dt));

                float yawStep = _turnVel * dt;
                if (Mathf.Abs(yawStep) > 1e-5f)
                    Forward = (Quaternion.AngleAxis(yawStep, Vector3.up) * Forward).normalized;
                if (direct && push > 0f && directionHeadingFollow > 0f)
                {
                    float toA = Mathf.Atan2(_driveDir.x, _driveDir.y) * Mathf.Rad2Deg;
                    float fromA = Mathf.Atan2(Forward.x, Forward.z) * Mathf.Rad2Deg;
                    float a = Mathf.MoveTowardsAngle(fromA, toA, directionHeadingFollow * ag * dt) * Mathf.Deg2Rad;
                    Forward = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                }
            }

            // Ambient yaw drift: a seeded slow double sine (bounded by driftRotation * Agility) so islands
            // never sit perfectly still. It turns the body only: W always drives straight ahead.
            float drift = driftRotation * ag * DriftWave(_clock * (2f * Mathf.PI / Mathf.Max(1f, driftPeriod)) + _driftPhase);
            _bodyYaw = Mathf.DeltaAngle(0f, _bodyYaw + drift * dt + StepBodyTurns(dt));

            // Thrust scales only gently with size (owner: "fühlt sich sehr träge an"); the drag keeps the full agility,
            // so a big island still glides on long after the throttle is released.
            float thrust = acceleration * SpeedScale * Mathf.Pow(ag, accelAgilityExponent);
            float max = MaxSpeed;
            float cap;
            if (race)
            {
                float stag = StaggerFactor;
                float top = max * stag;
                float baseSpeed = Mathf.Clamp01(AdventureCruise) * top;
                // Braking drops to a share of the base pace and no further: the island never stops and never runs
                // backwards. A stagger (lightning, a ship) lowers base and top alike, so the floor cannot undo it.
                float want = _hitStunLeft > 0f
                    ? baseSpeed * adventureBrakeMin
                    : brake > 0f
                        ? baseSpeed * Mathf.Lerp(1f, adventureBrakeMin, brake)
                        : Mathf.Lerp(baseSpeed, top, ahead);
                float along = Vector2.Dot(_selfVel, track);
                float side = Vector2.Dot(_selfVel, across2);
                if (along < want) along = Mathf.MoveTowards(along, want, thrust * dt);
                else if (brake > 0f && _hitStunLeft <= 0f) along = Mathf.MoveTowards(along, want, thrust * adventureBrakeForce * dt);
                // Above the wanted pace (a boost running out) the island coasts down on its drag; a stagger,
                // though, has to bite at once, so it pulls the speed down at the brake's rate.
                else along = Mathf.Max(want, along - (_staggerLeft > 0f
                    ? thrust * adventureBrakeForce * dt
                    : along * (1f - Mathf.Exp(-dragBase * ag * dt))));
                // Right after a hit the bounce off the island is left alone, so it really pushes clear.
                if (_hitStunLeft > 0f) side *= Mathf.Exp(-dragBase * ag * dt);
                else side = Mathf.MoveTowards(side, steer * AdventureSteerSpeed * stag, AdventureSteerSpeed * RaceSteerResponse * dt);
                _selfVel = track * along + across2 * side;
                cap = Mathf.Max(top, _selfVel.magnitude);
            }
            else
            {
                if (_hitStunLeft > 0f) input.y = Mathf.Min(input.y, 0f);
                _selfVel += drive2 * (input.y * thrust * dt);
                _selfVel *= Mathf.Exp(-dragBase * ag * dt);
                // The keel pulls the velocity onto the direction the island is driven in. In direct mode a phone held
                // level means "let it glide": nothing drags the momentum onto a heading the player never chose.
                if (!direct) AlignVelocity(dt, Forward2, true);
                else if (push > 0f) AlignVelocity(dt, _driveDir, false);
                // Direct steering: how far the stick or the phone is pushed is how fast the island goes - a small
                // deflection used to reach the full speed all the same. Only the player's own top speed is scaled;
                // the boost and the surf reference stay on the unscaled one.
                cap = direct && push > 0f ? max * Mathf.Lerp(directionMinSpeedShare, 1f, push) : max;
                cap *= StaggerFactor;
            }
            if (_boostLeft > 0f)
            {
                // The boost surges the island along the direction it is driven in, towards the boosted top speed. It
                // adds its share of the top speed WITHOUT the momentum: chained boosts raise the speed through the
                // momentum, the single boost does not get stronger with it.
                max += (BoostFactor - 1f) * max / MomentumScale;
                cap = Mathf.Max(cap, max);
                Vector2 f2 = drive2;
                float along = Vector2.Dot(_selfVel, f2);
                if (along < max) _selfVel += f2 * ((max - along) * (1f - Mathf.Exp(-BoostResponse * BoostEnvelope * dt)));
                _boostLeft = Mathf.Max(0f, _boostLeft - dt);
            }
            if (_selfVel.sqrMagnitude > cap * cap) _selfVel = _selfVel.normalized * cap;

            // Cozy: the plate interior only carries gently (owner: "selbst gegen die Strömung möchte ich vorankommen");
            // the strong push is reserved for the boundaries (SurfVelocity). The adventure keeps the full lane current.
            var plates = PlateSystem.Instance;
            Vector2 plateVel = plates == null ? Vector2.zero
                : Mode == GameMode.Adventure ? plates.SampleVelocity(_pos) : plates.CarryVelocity(_pos, MaxSpeed);

            var storms = StormSystem.Instance;
            float storm = 0f;
            Vector2 gust = Vector2.zero;
            if (storms != null)
            {
                storm = storms.IntensityAt(_pos);
                if (storm > 0f) gust = storms.GustAt(_pos, storms.Clock);
                // On the ring the gust only pushes across the track; along it the race keeps its own pace.
                if (storm > 0f && AdventureRacing) gust -= AdventureTrack * Vector2.Dot(gust, AdventureTrack);
            }
            StormIntensity = storm;
            StormGust = gust;

            _carry = Vector2.Lerp(_carry, plateVel + gust, 1f - Mathf.Exp(-carryResponse * dt));

            Vector2 surf = Vector2.zero;
            float surfStrength = 0f;
            if (useKeyboardInput && PlateSystem.Instance != null)
            {
                Vector2 f2 = drive2;
                float drive = Mathf.Clamp01(Vector2.Dot(_selfVel, f2) / Mathf.Max(0.1f, 0.35f * max));
                surf = PlateSystem.Instance.SurfVelocity(_pos, f2, _boundRadius, drive, out surfStrength);
                // The momentum makes the whole island faster, the push it takes from a plate boundary included.
                surf *= MomentumScale;
            }
            SurfStrength = surfStrength;
            _surf = Vector2.Lerp(_surf, surf, 1f - Mathf.Exp(-surfResponse * dt));
            _pos += (_selfVel + _carry + _surf + EdgePush) * dt;
            // A clamp on the planar position (cozy: none; the adventure ring holds the player inside its band).
            if (useKeyboardInput && PositionConstraint != null)
            {
                Vector2 kept = PositionConstraint(_pos);
                if ((kept - _pos).sqrMagnitude > 1e-8f)
                {
                    Vector2 n = (kept - _pos).normalized;
                    float into = Vector2.Dot(_selfVel, n);
                    if (into < 0f) _selfVel -= n * into;
                    _pos = kept;
                }
            }

            if (_herds == null) _herds = GetComponent<IslandHerdSystem>();
            if (_herds != null) _herds.Agitation = storm;

            // Lean into the turn: the body rolls towards the inside of a curve, the more the faster it runs.
            // In the race there is no turn to read, so the sideways pull stands in for it.
            float bankRate = race ? steer * turnRateDegPerSec : _turnVel;
            float leanWant = SpeedFeel.Bank(bankRate, turnRateDegPerSec,
                SpeedFeel.Drive(PlanarVelocity.magnitude, MaxSpeed), leanIntoTurns);
            _lean = dt > 0f ? Mathf.Lerp(_lean, leanWant, 1f - Mathf.Exp(-leanResponse * dt)) : leanWant;

            ApplyTransform();
        }

        // Keel effect: self-velocity swings toward the heading (speed preserved) so a turn redirects the
        // island instead of letting it slide sideways on the old course.
        // allowReverse keeps a backwards-moving island backwards (the heading has a front); a driven direction
        // has none, so there it pulls the momentum around the other way too.
        void AlignVelocity(float dt, Vector2 goal2, bool allowReverse)
        {
            float speed = _selfVel.magnitude;
            if (speed < 0.05f || velocityAlign <= 0f) return;
            Vector2 goal = allowReverse && Vector2.Dot(_selfVel, goal2) < 0f ? -goal2 : goal2;
            Vector2 dir = _selfVel / speed;
            dir += (goal - dir) * (1f - Mathf.Exp(-velocityAlign * dt));
            if (dir.sqrMagnitude > 1e-6f) _selfVel = dir.normalized * speed;
        }

        // The body, not the heading, answers a merge. Direction = the torque of the off-centre hit, judged
        // in heading space: a contact right of the nose turns the body clockwise (seen from above), left of
        // it counter-clockwise, so the struck side swings to the rear the short way; a dead-centre hit turns
        // the nose towards the side with less land. Magnitude = 90..180 degrees in that direction, picking
        // the angle after which the land reaches least from the centroid along the heading
        // (IslandShape.LandReach, body space, smoothed over +-15 degrees; the minor principal axis for an
        // oval, the notch between bodies for a cluster), ties to the smaller turn, so a round island turns
        // exactly 90. Merges in quick succession turn less: the angle is lerp(bodyTurnRapidAngle, full,
        // timeSinceLastMerge / bodyTurnCooldown) and the queued total never exceeds bodyTurnMaxQueued.
        // Heading and velocity are untouched: the player keeps driving straight.
        const float BodyTurnMin = 90f;
        const float BodyTurnStep = 5f;
        const float DeadCentre = 0.1f;
        static readonly float[] Reach = new float[72];
        static readonly float[] ReachSmooth = new float[72];

        // +1 = clockwise seen from above (Unity yaw), -1 = counter-clockwise. heading and contactDir are
        // planar world vectors (x, z); contactDir points from the island centre to the contact.
        public static float TorqueSign(Vector2 heading, Vector2 contactDir, float landLeft, float landRight, bool tieClockwise)
        {
            float len = contactDir.magnitude;
            if (len > 1e-4f)
            {
                float side = (heading.y * contactDir.x - heading.x * contactDir.y) / len;
                if (Mathf.Abs(side) > DeadCentre) return Mathf.Sign(side);
            }
            float total = landLeft + landRight;
            if (total > 0f && Mathf.Abs(landLeft - landRight) > 0.02f * total) return landRight < landLeft ? 1f : -1f;
            return tieClockwise ? 1f : -1f;
        }

        void LandSides(out float left, out float right)
        {
            left = 0f;
            right = 0f;
            Vector2 r2 = Right2;
            float bx = Vector2.Dot(_bodyRight2, r2), by = Vector2.Dot(_bodyFwd2, r2);
            for (int j = 0; j < _shape.nz; j++)
                for (int i = 0; i < _shape.nx; i++)
                {
                    if (_shape.Height(i, j) <= 0f) continue;
                    Vector2 p = _shape.CellPos(i, j);
                    float side = p.x * bx + p.y * by;
                    if (side > 0.25f) right += 1f;
                    else if (side < -0.25f) left += 1f;
                }
        }

        void BeginBodyTurn(Vector2 contactLocal)
        {
            RefreshBodyBasis();
            Vector2 cw = _bodyRight2 * contactLocal.x + _bodyFwd2 * contactLocal.y;
            LandSides(out float landLeft, out float landRight);
            float sign = TorqueSign(Forward2, cw, landLeft, landRight, ((shapeSeed + _version) & 1) == 0);
            float pending = BodyTurnRemaining;

            _shape.LandReach(Reach);
            int n = Reach.Length;
            float mean = 0f;
            for (int k = 0; k < n; k++)
            {
                float sum = 0f;
                for (int o = -3; o <= 3; o++) sum += Reach[(k + o + n) % n];
                ReachSmooth[k] = sum / 7f;
                mean += Reach[k];
            }
            mean = Mathf.Max(0.1f, mean / n);

            float full = BodyTurnMin, best = float.MaxValue;
            for (float a = BodyTurnMin; a <= 180f; a += BodyTurnStep)
            {
                // Body-local direction that faces the heading once this turn (and the pending ones) is done.
                float facing = Mathf.Repeat(-(_bodyYaw + pending + sign * a), 360f);
                int front = Mathf.RoundToInt(facing * n / 360f) % n;
                int rear = (front + n / 2) % n;
                float cost = (1.5f * ReachSmooth[front] + ReachSmooth[rear]) / mean + 0.02f * (a - BodyTurnMin) / 90f;
                if (cost < best) { best = cost; full = a; }
            }

            float w = bodyTurnCooldown > 0f ? Mathf.Clamp01((_clock - _lastMergeClock) / bodyTurnCooldown) : 1f;
            _lastMergeClock = _clock;
            float angle = sign * Mathf.Lerp(Mathf.Min(bodyTurnRapidAngle, full), full, w) * Mathf.Clamp01(bodyTurnAmount);
            if (Mathf.Abs(pending) > 1f)
                angle = Mathf.Clamp(pending + angle, -bodyTurnMaxQueued, bodyTurnMaxQueued) - pending;
            LastBodyTurn = angle;
            AddBodyTurn(angle);
        }

        void AddBodyTurn(float angle)
        {
            if (bodyTurnTime <= 0f)
            {
                _bodyYaw = Mathf.DeltaAngle(0f, _bodyYaw + angle);
                return;
            }
            _turnFeed += angle;
        }

        // 0 for a start-sized island (bodyTurnRefArea) or smaller, 1 from a continent (bodyTurnHugeArea) on.
        public float BodyTurnSluggishness
        {
            get
            {
                float reference = Mathf.Max(1e-4f, AgilityFor(bodyTurnRefArea));
                float huge = 1f - Mathf.Sqrt(Mathf.Clamp01(AgilityFor(bodyTurnHugeArea) / reference));
                if (huge <= 1e-4f) return 0f;
                return Mathf.Clamp01((1f - Mathf.Sqrt(Mathf.Clamp01(Agility / reference))) / huge);
            }
        }
        public float BodyTurnSettleTime => Mathf.Lerp(bodyTurnTime, bodyTurnTimeHuge, BodyTurnSluggishness);
        public float BodyTurnRateCap => Mathf.Lerp(bodyTurnMaxRate, bodyTurnMaxRateHuge, BodyTurnSluggishness);

        // Three equal real poles: 1 - e^-s (1 + s + s²/2) reaches 95 % at s = omega * t = 6.296 (99 % at 8.406).
        const float SettleOmega = 6.296f;
        const float BodyTurnSubstep = 0.02f;
        const int BodyTurnMaxSubsteps = 16;
        const float BodyTurnSnapAngle = 0.02f;
        const float BodyTurnSnapRate = 0.1f;

        // The impact feeds its angle through a first-order lag into a critically damped spring (same pole), so
        // the yaw rate starts at zero with zero slope, swells, and decays onto the target without overshoot.
        // Each substep is the closed-form solution, hence stable for any dt; merges simply add to the feed.
        float StepBodyTurns(float dt)
        {
            if (_turnFeed == 0f && _turnError == 0f && _turnRate == 0f) return 0f;
            float settle = BodyTurnSettleTime;
            if (settle <= 0f)
            {
                float rest = _turnFeed + _turnError;
                ClearBodyTurns();
                return rest;
            }
            if (dt <= 0f) return 0f;

            float w = SettleOmega / settle;
            float cap = Mathf.Max(1f, BodyTurnRateCap);
            int n = Mathf.Clamp(Mathf.CeilToInt(dt / BodyTurnSubstep), 1, BodyTurnMaxSubsteps);
            float h = dt / n;
            float decay = Mathf.Exp(-w * h);
            float turned = 0f;
            for (int k = 0; k < n; k++)
            {
                float fed = _turnFeed * (1f - decay);
                _turnFeed -= fed;
                float y0 = _turnError + fed;
                float v0 = -_turnRate;
                float c = v0 + w * y0;
                float y1 = (y0 + c * h) * decay;
                float rate = -(v0 - w * c * h) * decay;
                if (Mathf.Abs(rate) > cap)
                {
                    rate = Mathf.Sign(rate) * cap;
                    y1 = y0 - rate * h;
                    // Only a huge substep can carry the capped rate past the target.
                    if (y1 * y0 < 0f) { y1 = 0f; rate = 0f; }
                }
                turned += y0 - y1;
                _turnError = y1;
                _turnRate = rate;
            }
            if (Mathf.Abs(_turnFeed + _turnError) < BodyTurnSnapAngle && Mathf.Abs(_turnRate) < BodyTurnSnapRate)
            {
                turned += _turnFeed + _turnError;
                ClearBodyTurns();
            }
            return turned;
        }

        void ClearBodyTurns()
        {
            _turnFeed = 0f;
            _turnError = 0f;
            _turnRate = 0f;
        }

        public void SetBodyYaw(float degrees)
        {
            _bodyYaw = Mathf.DeltaAngle(0f, degrees);
            ClearBodyTurns();
            ApplyTransform();
        }

        public void SetPlanarPosition(Vector2 p)
        {
            _pos = p;
            ApplyTransform();
        }

        public void SetSelfVelocity(Vector2 v) => _selfVel = v;

        void ApplyTransform()
        {
            float bob = Application.isPlaying ? bobAmplitude * Mathf.Sin(Time.time * 1.3f + _bobPhase) : 0f;
            RefreshBodyBasis();
            transform.position = new Vector3(_pos.x, heightOffset + bob, _pos.y);
            var rot = Quaternion.LookRotation(BodyForward, Normal);
            // Only the transform leans into a curve; the heightfield frame stays planar, so contacts are unaffected.
            if (Mathf.Abs(_lean) > 0.01f) rot = Quaternion.AngleAxis(_lean, BodyForward) * rot;
            transform.rotation = rot;
        }

        void RefreshBodyBasis()
        {
            float rad = _bodyYaw * Mathf.Deg2Rad;
            float sn = Mathf.Sin(rad), cs = Mathf.Cos(rad);
            _bodyFwd2 = new Vector2(Forward.x * cs + Forward.z * sn, Forward.z * cs - Forward.x * sn).normalized;
            _bodyRight2 = new Vector2(_bodyFwd2.y, -_bodyFwd2.x);
        }

        Vector2 Right2 => new Vector2(Forward.z, -Forward.x);
        Vector2 Forward2 => new Vector2(Forward.x, Forward.z);

        // Local space is the body (heightfield) space, identical to the transform's XZ frame.
        public Vector2 ToWorld(Vector2 local) => _pos + _bodyRight2 * local.x + _bodyFwd2 * local.y;

        public Vector2 ToLocal(Vector2 world)
        {
            Vector2 d = world - _pos;
            return new Vector2(Vector2.Dot(d, _bodyRight2), Vector2.Dot(d, _bodyFwd2));
        }

        // ---- shape / mesh ----

        [ContextMenu("Regenerate Shape")]
        public void RegenerateShape() => GenerateShape();

        // A heightfield built ahead of time off the main thread (WorldStreamer). GenerateShape takes it only when
        // it was built from exactly the parameters it would use itself, otherwise it generates as always.
        public sealed class PrebuiltShape
        {
            public IslandArchetype archetype;
            public float radius, cell, heightScale;
            public int seed;
            public IslandShape shape;

            public bool Matches(IslandArchetype a, float r, int s, float c, float hs) =>
                shape != null && a == archetype && s == seed && r == radius && c == cell && hs == heightScale;

            // Thread-safe: pure arithmetic and Mathf.PerlinNoise, no UnityEngine.Object.
            public static PrebuiltShape Build(IslandArchetype a, float r, int s, float c, float hs) => new PrebuiltShape
            {
                archetype = a, radius = r, seed = s, cell = c, heightScale = hs,
                shape = IslandArchetypes.Create(a, r, s, c, hs),
            };

            // The parameters GenerateShape uses for a streamed (non-player, non-volcano) island with default fields.
            public static PrebuiltShape BuildForStreamed(IslandArchetype a, float landRadius, int s, float c)
            {
                bool barren = KindForSeed(s) == IslandKind.Barren;
                return Build(a, barren ? landRadius * DefaultBarrenRadiusScale : landRadius, s, c, barren ? DefaultBarrenHeightScale : 1f);
            }
        }

        [NonSerialized] public PrebuiltShape Prebuilt;

        void GenerateShape()
        {
            kind = DeriveKind();
            bool barren = kind == IslandKind.Barren;
            float radius = barren ? landRadius * barrenRadiusScale : landRadius;
            float heightScale = barren ? barrenHeightScale : 1f;
            if (isVolcano)
            {
                _shape = IslandShape.CreateVolcano(landRadius, shapeSeed, cellSize);
                _shape.LimitSlopes(IslandShape.SlopeRule.Degrees(volcanoMaxSlope, volcanoMaxSlope, 0f), VolcanoErosionSteps);
            }
            else if (Prebuilt != null && Prebuilt.Matches(archetype, radius, shapeSeed, cellSize, heightScale)) _shape = Prebuilt.shape;
            else _shape = IslandArchetypes.Create(archetype, radius, shapeSeed, cellSize, heightScale);
            Prebuilt = null;
            _emergeTime = 0f;
            _upliftT = 1f;
            _carry = Vector2.zero;
            _surf = Vector2.zero;
            _colors = null;
            _hypsoDirty = true;
            _buoy = 1f;
            _boostLeft = 0f;
            LastRefloat = 0f;
            ClearHits();
            RecomputeStats();
            if (useKeyboardInput) _startArea = _areaRaw;
            RebuildMesh();
            _version++;
            NotifyShapeGenerated();
        }

        // Exactly once per generated shape (Repopulate is idempotent per Version, so the life systems'
        // own OnEnable calls that follow become no-ops instead of building everything twice).
        // A disabled component is skipped: WorldStreamer switches the life systems of a fresh island on one per
        // frame, and each populates itself from its own OnEnable then.
        void NotifyShapeGenerated()
        {
            var life = GetComponent<IslandLifeSystem>();
            if (life != null && life.enabled) life.Repopulate();
            var herds = GetComponent<IslandHerdSystem>();
            if (herds != null && herds.enabled) herds.Repopulate();
            var critters = GetComponent<IslandCrittersSystem>();
            if (critters != null && critters.enabled) critters.Repopulate();
            var settlement = GetComponent<IslandSettlementSystem>();
            if (settlement != null && settlement.enabled) settlement.Repopulate();
        }

        void RecomputeStats()
        {
            int count = 0, rawCount = 0;
            float maxR = 0f;
            for (int j = 0; j < _shape.nz; j++)
                for (int i = 0; i < _shape.nx; i++)
                {
                    if (_shape.HeightRaw(i, j) > 0f) rawCount++;
                    if (_shape.Height(i, j) <= 0f) continue;
                    count++;
                    maxR = Mathf.Max(maxR, _shape.CellPos(i, j).magnitude);
                }
            _areaRaw = rawCount * _shape.cell * _shape.cell;
            _area = count * _shape.cell * _shape.cell;
            _boundRadius = maxR + _shape.cell;
            Compactness = Mathf.Clamp01(_shape.Compactness());
        }

        void RebuildMesh()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            if (GetComponent<MeshRenderer>() == null) gameObject.AddComponent<MeshRenderer>();
            // After a domain reload the field is null but the filter still holds the old DontSave mesh:
            // reuse it instead of leaking one mesh per recompile.
            if (_mesh == null && mf.sharedMesh != null && mf.sharedMesh.name == "IslandTerrain") _mesh = mf.sharedMesh;
            if (_mesh == null) _mesh = new Mesh { name = "IslandTerrain", hideFlags = HideFlags.DontSave };
            _shape.FillMesh(_mesh);
            if (_colors == null || _colors.Length != _shape.nx * _shape.nz)
            {
                _colors = new Color[_shape.nx * _shape.nz];
                for (int i = 0; i < _colors.Length; i++) _colors[i] = Color.white;
            }
            _mesh.SetColors(_colors);
            mf.sharedMesh = _mesh;
        }

        // The filter of IslandLifeSystem.GroundTintAt, written out per channel: no delegate, no Color.Lerp calls.
        public void ApplyGroundTint(Color[] cells, int gx, int gz, Vector2 gridOrigin, float gridCell)
        {
            if (_mesh == null || _shape == null || cells == null || gx <= 0 || gz <= 0 || cells.Length < gx * gz) return;
            int nx = _shape.nx, nz = _shape.nz;
            int n = nx * nz;
            if (_colors == null || _colors.Length != n) _colors = new Color[n];
            float inv = 1f / gridCell;
            for (int j = 0, k = 0; j < nz; j++)
            {
                float fz = (_shape.origin.y + j * _shape.cell - gridOrigin.y) * inv - 0.5f;
                int j0 = Mathf.FloorToInt(fz);
                float tz = fz - j0;
                int r0 = Mathf.Clamp(j0, 0, gz - 1) * gx, r1 = Mathf.Clamp(j0 + 1, 0, gz - 1) * gx;
                for (int i = 0; i < nx; i++, k++)
                {
                    float fx = (_shape.origin.x + i * _shape.cell - gridOrigin.x) * inv - 0.5f;
                    int i0 = Mathf.FloorToInt(fx);
                    float tx = fx - i0;
                    int c0 = Mathf.Clamp(i0, 0, gx - 1), c1 = Mathf.Clamp(i0 + 1, 0, gx - 1);
                    Color a = cells[r0 + c0], b = cells[r0 + c1], c = cells[r1 + c0], d = cells[r1 + c1];
                    float abr = a.r + (b.r - a.r) * tx, abg = a.g + (b.g - a.g) * tx, abb = a.b + (b.b - a.b) * tx, aba = a.a + (b.a - a.a) * tx;
                    float cdr = c.r + (d.r - c.r) * tx, cdg = c.g + (d.g - c.g) * tx, cdb = c.b + (d.b - c.b) * tx, cda = c.a + (d.a - c.a) * tx;
                    _colors[k] = new Color(abr + (cdr - abr) * tz, abg + (cdg - abg) * tz, abb + (cdb - abb) * tz, aba + (cda - aba) * tz);
                }
            }
            _mesh.SetColors(_colors);
        }

        // Adventure: buoyancy drains linearly over SinkSecondsForArea(full area) (times storm and the form penalty: a
        // stretched island sinks faster, the incentive to grow from all sides) until the island is gone.
        // Cozy: a size regulator, no timer. The rate follows the land above water right now (SinkSecondsForArea of
        // LandArea, times cozySinkSpeed), eases out over cozyEaseShare and stops at SinkFloorArea; below it (the floor
        // rose with the run's progress) the island floats back up over cozyRiseSeconds.
        // Either way the sink depth follows from the island's own hypsometric curve, so the timing holds whatever the
        // terrain: plateau, ridges or a volcano.
        public void AdvanceSink(float dt)
        {
            if (!sinkEnabled || !useKeyboardInput || IsSunk || _shape == null || _emergeTime > 0f) return;
            bool cozy = Mode == GameMode.Cozy;
            if (!cozy && !adventureSinking) return;
            SyncBuoyancy();
            float before = _shape.sink;
            if (cozy)
            {
                float floorShare = FloorShare;
                float floor = BuoyancyAtLandShare(floorShare);
                if (_buoy > floor)
                {
                    float ease = Mathf.Clamp01((LandShareAt(_buoy) - floorShare) / Mathf.Max(0.01f, cozyEaseShare));
                    float rate = Mathf.Max(0f, cozySinkSpeed) * SinkMultiplier / Mathf.Max(0.1f, SinkSecondsForArea(_area));
                    _buoy = Mathf.Max(floor, _buoy - dt * rate * ease);
                }
                else _buoy = Mathf.Min(floor, _buoy + dt / Mathf.Max(0.1f, cozyRiseSeconds));
            }
            else _buoy = Mathf.Max(0f, _buoy - dt / Mathf.Max(0.1f, SinkSecondsFull));
            float share = LandShareAt(_buoy);
            // After a hit the waterline is allowed to climb faster for a moment, so the price of the crash is seen.
            float loss = Mathf.Max(0f, maxLandLossPerSecond);
            if (_hitSinkLeft > 0f)
            {
                _hitSinkLeft = Mathf.Max(0f, _hitSinkLeft - dt);
                loss = Mathf.Max(loss, hitSinkRate);
            }
            float held = _hypso.LandFractionAt(before) - loss * dt;
            _shape.sink = _hypso.DepthAt(Mathf.Max(share, held));
            _sinkVersionAccum += Mathf.Abs(_shape.sink - before);
            RefreshSink(dt, _hitSinkLeft > 0f ? 0.2f : 0.5f, !cozy);
        }

        void ClearEdge()
        {
            EdgePush = Vector2.zero;
            EdgeWarning = 0f;
        }

        void ClearHits()
        {
            ClearEdge();
            Hits = 0;
            LastHitLoss = 0f;
            _hitCooldownLeft = 0f;
            _hitStunLeft = 0f;
            _hitSinkLeft = 0f;
            _hitGraceLeft = 0f;
            _ghostLeft = _ghostTouch = 0f;
            Momentum = 0f;
        }

        float LandShareAt(float buoyancy) => Mathf.Pow(Mathf.Clamp01(buoyancy), Mathf.Max(0.05f, sinkLandExponent));
        float BuoyancyAtLandShare(float share) => Mathf.Pow(Mathf.Clamp01(share), 1f / Mathf.Max(0.05f, sinkLandExponent));

        // The raw heights only change with the shape (generate, restore, merge, uplift), never while sinking.
        // The sink depth is the saved state: after a restore the buoyancy is read back from it. A merge and
        // its uplift keep the player's buoyancy instead and move the depth, because the ridge erodes the rest
        // of the island and a fixed depth would silently cost buoyancy.
        void RebuildHypsometry(bool keepBuoyancy)
        {
            if (_hypso == null) _hypso = new IslandHypsometry();
            _hypso.Build(_shape);
            _hypsoDirty = false;
            if (keepBuoyancy && useKeyboardInput) _shape.sink = _hypso.DepthAt(LandShareAt(_buoy));
            else _buoy = BuoyancyAtLandShare(_hypso.LandFractionAt(_shape.sink));
        }

        void SyncBuoyancy()
        {
            if (_hypsoDirty || _hypso == null) RebuildHypsometry(false);
        }

        // Starts the island fully submerged and lets it rise to its real height (new volcanic islands).
        public void BeginEmergence(float duration)
        {
            if (_shape == null) return;
            BeginEmergence(duration, _shape.MaxHeightRaw() + 0.5f);
        }

        // Resumes from a given sink offset (restored half-risen volcanoes) instead of re-submerging.
        public void BeginEmergence(float duration, float startSink)
        {
            if (_shape == null) return;
            float depth = Mathf.Max(0f, startSink);
            _shape.sink = depth;
            _hypsoDirty = true;
            _emergeSpeed = depth / Mathf.Max(0.01f, duration);
            _emergeTime = duration;
            _sinkMeshTimer = 0f;
            _sinkVersionAccum = 0f;
            RecomputeStats();
            RebuildMesh();
            _version++;
        }

        public void AdvanceEmergence(float dt)
        {
            if (_emergeTime <= 0f || _shape == null) return;
            _emergeTime -= dt;
            _sinkVersionAccum += _emergeSpeed * dt;
            _hypsoDirty = true;
            if (_emergeTime <= 0f)
            {
                _emergeTime = 0f;
                _shape.sink = 0f;
                _sinkMeshTimer = 99f;
            }
            else _shape.sink = Mathf.Max(0f, _shape.sink - _emergeSpeed * dt);
            RefreshSink(dt, 0.25f, false);
        }

        void RefreshSink(float dt, float interval, bool checkSunk)
        {
            _sinkMeshTimer += dt;
            if (_sinkMeshTimer < interval) return;

            _sinkMeshTimer = 0f;
            RecomputeStats();
            if (_mesh != null) _shape.RefreshHeights(_mesh, false);
            else RebuildMesh();
            if (_sinkVersionAccum >= 0.02f)
            {
                _sinkVersionAccum = 0f;
                _upliftVersionTimer = 0f;
                _version++;
            }
            if (checkSunk && _area <= sunkArea)
            {
                IsSunk = true;
                _selfVel = Vector2.zero;
                Sunk?.Invoke();
            }
        }

        public void ResetToStart()
        {
            IsSunk = false;
            _sinkMeshTimer = 0f;
            _sinkVersionAccum = 0f;
            _emergeTime = 0f;
            _selfVel = Vector2.zero;
            _turnVel = 0f;
            _lean = 0f;
            _bodyYaw = 0f;
            ClearBodyTurns();
            _clock = 0f;
            _lastMergeClock = -1e9f;
            LastBodyTurn = 0f;
            StormIntensity = 0f;
            StormGust = Vector2.zero;
            _pos = startPlanarPosition;
            Forward = Vector3.forward;
            RefreshBodyBasis();
            GenerateShape();
            ApplyTransform();
        }

        // The highland of a merge rises over several seconds, and with a merge every few seconds that is most of
        // the time on a big island. Rebuilding the whole terrain mesh and the hypsometric curve every frame cost
        // 1 ms on desktop (~4 ms on a phone): the slow rise is redrawn at 15 Hz, the depth that keeps the player's
        // buoyancy constant is re-derived at 4 Hz, and the life systems (which rebuild on every Version) hear of
        // it on the same half-second cadence as sinking. The last step is always a full, exact rebuild.
        const float UpliftMeshInterval = 1f / 15f;
        const float UpliftHypsoInterval = 0.25f;
        const float UpliftVersionInterval = 0.5f;
        const int MergeErosionSteps = 12;
        const int VolcanoErosionSteps = 0;

        public void AdvanceUplift(float dt)
        {
            if (_upliftT >= 1f) return;
            if (useKeyboardInput) SyncBuoyancy();
            _upliftT = _upliftDur <= 0f ? 1f : Mathf.Min(1f, _upliftT + dt / _upliftDur);
            // Ease in and out: the land starts to rise without a jolt and settles softly.
            _shape.upliftWeight = _upliftT * _upliftT * (3f - 2f * _upliftT);
            bool done = _upliftT >= 1f;
            _upliftHypsoTimer += dt;
            _upliftMeshTimer += dt;
            _upliftVersionTimer += dt;
            if (done || _upliftHypsoTimer >= UpliftHypsoInterval)
            {
                _upliftHypsoTimer = 0f;
                // Only flagged when it is rebuilt right here: a dirty flag left standing makes the next
                // SyncBuoyancy rebuild it the other way round (buoyancy from depth) and the rise costs buoyancy.
                _hypsoDirty = true;
                if (useKeyboardInput) RebuildHypsometry(true);
            }
            if (done)
            {
                _shape.Bake();
                RecomputeStats();
                _version++;
                _upliftMeshTimer = 0f;
                _upliftVersionTimer = 0f;
                RebuildMesh();
                return;
            }
            if (_upliftVersionTimer >= UpliftVersionInterval)
            {
                _upliftVersionTimer = 0f;
                // Shares the sinking cadence instead of adding a second one on top of it (RefreshSink resets
                // the uplift timer when it bumps).
                _sinkVersionAccum = 0f;
                RecomputeStats();
                _version++;
                _upliftMeshTimer = UpliftMeshInterval;
            }
            if (_upliftMeshTimer < UpliftMeshInterval) return;
            _upliftMeshTimer = 0f;
            if (_mesh != null) _shape.RefreshHeights(_mesh, true);
            else RebuildMesh();
        }

        public void FinishUplift() => AdvanceUplift(_upliftDur + 1f);

        public IslandSaveData Capture()
        {
            var heights = new float[_shape.nx * _shape.nz];
            for (int j = 0; j < _shape.nz; j++)
                for (int i = 0; i < _shape.nx; i++)
                {
                    int k = j * _shape.nx + i;
                    heights[k] = _shape.h[k] + (_shape.uplift != null ? _shape.uplift[k] : 0f);
                }
            return new IslandSaveData
            {
                posX = _pos.x, posZ = _pos.y,
                yaw = Mathf.Atan2(Forward.x, Forward.z) * Mathf.Rad2Deg,
                // A turn still in flight is saved as finished, so a reload lands on the intended orientation.
                bodyYaw = Mathf.DeltaAngle(0f, _bodyYaw + BodyTurnRemaining),
                velX = _selfVel.x, velZ = _selfVel.y,
                landRadius = landRadius, cell = _shape.cell, sink = _shape.sink, shapeSeed = shapeSeed,
                isVolcano = isVolcano, kind = (int)kind, archetype = (int)archetype,
                nx = _shape.nx, nz = _shape.nz,
                originX = _shape.origin.x, originZ = _shape.origin.y,
                heights = heights
            };
        }

        public void Restore(IslandSaveData d)
        {
            landRadius = d.landRadius;
            shapeSeed = d.shapeSeed;
            isVolcano = d.isVolcano;
            kind = (IslandKind)d.kind;
            archetype = (IslandArchetype)d.archetype;
            _lastMergeClock = -1e9f;
            _pos = new Vector2(d.posX, d.posZ);
            Forward = Quaternion.Euler(0f, d.yaw, 0f) * Vector3.forward;
            _selfVel = new Vector2(d.velX, d.velZ);
            _turnVel = 0f;
            _lean = 0f;
            _carry = Vector2.zero;
            _surf = Vector2.zero;
            _bodyYaw = Mathf.DeltaAngle(0f, d.bodyYaw);
            ClearBodyTurns();
            RefreshBodyBasis();
            _emergeTime = 0f;
            _sinkMeshTimer = 0f;
            _sinkVersionAccum = 0f;
            _boostLeft = 0f;
            ClearHits();
            StormIntensity = 0f;
            StormGust = Vector2.zero;

            _shape = new IslandShape(d.cell, d.nx, d.nz, new Vector2(d.originX, d.originZ));
            Array.Copy(d.heights, _shape.h, d.heights.Length);
            _shape.sink = d.sink;
            _hypsoDirty = true;
            LastRefloat = 0f;
            IsSunk = false;
            _upliftT = 1f;
            _colors = null;
            RecomputeStats();
            RebuildMesh();
            ApplyTransform();
            _version++;
        }

        // ---- collisions ----

        public static bool Near(Island a, Island b) =>
            Vector2.Distance(a._pos, b._pos) < a._boundRadius + b._boundRadius;

        public bool DetectContact(Island other, out Vector2 contactLocal, out int cells) =>
            DetectContact(other, out contactLocal, out cells, out _);

        // cells counts guest land over the host's shelf (first touch), deepCells only land over land.
        public bool DetectContact(Island other, out Vector2 contactLocal, out int cells, out int deepCells)
        {
            contactLocal = Vector2.zero;
            cells = 0;
            deepCells = 0;
            var b = other._shape;
            Vector2 sum = Vector2.zero;
            for (int j = 0; j < b.nz; j++)
                for (int i = 0; i < b.nx; i++)
                {
                    if (b.Height(i, j) <= 0.05f) continue;
                    Vector2 pa = ToLocal(other.ToWorld(b.CellPos(i, j)));
                    float ha = _shape.Sample(pa);
                    if (ha <= -0.3f) continue;
                    cells++;
                    sum += pa;
                    if (ha > 0.05f) deepCells++;
                }
            if (cells > 0) contactLocal = sum / cells;
            return cells > 0;
        }

        const float MergeHeightPerRootEnergy = 0.13f;
        const float MaxShave = 0.25f;

        // The highland a merge raises, returned as the uplift to add to merged.h (the land as it is now) so that it
        // ends at goal (where both bodies were heading) plus the new rise, slope-limited. The rise is a very broad
        // Gaussian around the contact (upliftSpread times the old ridge widths) with rolling noise, faded in over a
        // ramp from the beach band so the shore stays low. Its volume, minus the land hidden in the overlap, is
        // taken from the whole island in proportion to each cell's height above the beach band: the highest ground
        // gives most, so repeated merges do not pile up, and the beach band and the coastline are never touched.
        float[] BroadUplift(IslandShape merged, float[] goal, Vector2 contact, Vector2 normal, Vector2 tang, float H, float minR,
            float water, float overlap, int guestSeed)
        {
            int count = goal.Length;
            var rule = IslandShape.SlopeRule.Degrees(maxSlope, volcanoMaxSlope, water);
            float floor = rule.Floor;
            float spread = Mathf.Max(0.5f, upliftSpread);
            float w = spread * (0.7f * minR + 1f);
            float L = spread * (1.6f * minR + 1.8f);
            float coastFade = Mathf.Max(2f, 2.5f * H / rule.tan);
            var dist = merged.DistanceToBelow(goal, floor);
            float deepest = 0f;
            for (int k = 0; k < count; k++) if (dist[k] > deepest) deepest = dist[k];
            // The ramp reaches full height inside even a small island's heart.
            coastFade = Mathf.Min(coastFade, Mathf.Max(1f, 0.7f * deepest));
            Vector2 noiseOff = new Vector2((shapeSeed & 1023) * 0.37f, (guestSeed & 1023) * 0.11f);

            var kernel = new float[count];
            for (int j = 0; j < merged.nz; j++)
                for (int i = 0; i < merged.nx; i++)
                {
                    int k = j * merged.nx + i;
                    if (dist[k] <= 0f) continue;
                    Vector2 p = merged.CellPos(i, j);
                    Vector2 rel = p - contact;
                    float s = Vector2.Dot(rel, normal) / w, u = Vector2.Dot(rel, tang) / L;
                    float n1 = Mathf.PerlinNoise(noiseOff.x + p.x * 0.18f, noiseOff.y + p.y * 0.18f);
                    float n2 = Mathf.PerlinNoise(noiseOff.x + 31f + p.x * 0.5f, noiseOff.y + 17f + p.y * 0.5f);
                    float rolling = 0.7f + 0.4f * n1 + 0.2f * (n2 - 0.5f);
                    kernel[k] = Mathf.Exp(-s * s - u * u) * rolling * IslandShape.Smooth(0f, coastFade, dist[k]);
                }
            // The distance to the shore has a sharp crest along the middle of every body, which read as tent roofs.
            merged.BoxBlur(kernel, Mathf.Clamp(Mathf.RoundToInt(0.3f * coastFade / merged.cell), 1, 6), 2);
            float sumK = 0f, sumV = 0f;
            for (int k = 0; k < count; k++)
            {
                if (dist[k] <= 0f) { kernel[k] = 0f; continue; }
                kernel[k] *= IslandShape.Smooth(0f, 1f, dist[k]);
                sumK += kernel[k];
                sumV += goal[k] - floor;
            }

            float amp = H;
            if (sumK > 0f && overlap > amp * sumK) amp = Mathf.Min(overlap / sumK, 2f * H);
            float shave = sumV > 0f ? Mathf.Clamp((amp * sumK - overlap) / sumV, 0f, MaxShave) : 0f;
            var f = new float[count];
            for (int k = 0; k < count; k++)
                f[k] = dist[k] > 0f ? goal[k] + amp * kernel[k] - shave * (goal[k] - floor) : goal[k];
            merged.LimitSlopes(f, rule, MergeErosionSteps);
            for (int k = 0; k < count; k++) f[k] -= merged.h[k];
            return f;
        }

        public void MergeFrom(Island other, float closingSpeed, float convergence)
        {
            // Neither body is baked: a hit in the middle of a rising uplift builds on where the land is heading
            // (SampleGoal) but starts the new rise from where it is now, so the rest of the first rise is
            // neither lost nor popped in.
            var a = _shape;
            var b = other._shape;

            // Heights below are read raw (without sink). The host keeps its sink as an offset, so the
            // guest has to be re-based onto that same offset or a half-risen volcano would jump up.
            // Refloat is relative to the host: a guest of a tenth of its area gives back about a quarter of
            // the buoyancy, an equal one all of it, an islet next to nothing for a continent.
            LastRefloat = RefloatShareFor(other);
            float mergedSink = 0f, mergedBuoy = 1f;
            bool floats = useKeyboardInput || a.sink > 0f;
            if (floats)
            {
                SyncBuoyancy();
                mergedBuoy = Mathf.Clamp01(_buoy + LastRefloat);
                mergedSink = _hypso.DepthAt(LandShareAt(mergedBuoy));
            }
            b.BakeSink();
            other._emergeTime = 0f;

            DetectContact(other, out Vector2 contact, out int contactCells);
            Vector2 otherCenter = ToLocal(other._pos);
            Vector2 n = otherCenter.sqrMagnitude > 1e-4f ? otherCenter.normalized : Vector2.right;
            if (contactCells == 0) contact = otherCenter * 0.5f;
            Vector2 tang = new Vector2(-n.y, n.x);
            float minR = Mathf.Min(_boundRadius, other._boundRadius);
            float hostRadius = _boundRadius;
            Vector2 oldPos = _pos;

            float mh = Mathf.Max(1f, _area), mg = Mathf.Max(1f, other._area);
            float mu = mh * mg / (mh + mg);
            float vc = Mathf.Max(0.5f, closingSpeed);
            float energy = 0.5f * mu * vc * vc;
            // Low, broad highland instead of a ridge at the seam: amplitude H, spread over the whole island.
            float H = Mathf.Clamp(MergeHeightPerRootEnergy * mountainHeight * Mathf.Sqrt(energy) * (1f + 0.25f * convergence),
                0.15f * mountainHeight, mountainHeight * (0.45f + 0.12f * minR));
            _upliftDur = upliftDuration * (1f + 0.25f * H);
            Vector2 momentum = (_selfVel * mh + other._selfVel * mg) / (mh + mg) * impactRetention;

            Rect ra = a.ShelfBounds();
            Vector2 min = ra.min, max = ra.max;
            for (int j = 0; j < b.nz; j++)
                for (int i = 0; i < b.nx; i++)
                {
                    if (!IslandShape.IsShelf(b.h[j * b.nx + i])) continue;
                    Vector2 pa = ToLocal(other.ToWorld(b.CellPos(i, j)));
                    min = Vector2.Min(min, pa);
                    max = Vector2.Max(max, pa);
                }
            float margin = 3f * a.cell;
            min -= Vector2.one * margin;
            max += Vector2.one * margin;

            var origin = new Vector2(
                a.origin.x + Mathf.Floor((min.x - a.origin.x) / a.cell) * a.cell,
                a.origin.y + Mathf.Floor((min.y - a.origin.y) / a.cell) * a.cell);
            int nx = Mathf.CeilToInt((max.x - origin.x) / a.cell) + 1;
            int nz = Mathf.CeilToInt((max.y - origin.y) / a.cell) + 1;
            var merged = new IslandShape(a.cell, nx, nz, origin);
            var goal = new float[nx * nz];
            bool aRising = a.uplift != null, bRising = b.uplift != null;
            float overlap = 0f;

            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i;
                    Vector2 p = merged.CellPos(i, j);
                    Vector2 pb = other.ToLocal(ToWorld(p));
                    float hA = a.SampleRaw(p), gA = aRising ? a.SampleGoal(p) : hA;
                    float hB = b.SampleRaw(pb), gB = bRising ? b.SampleGoal(pb) : hB;
                    // Only the guest's shelf/land is re-based; its flat sea floor must stay the Sea
                    // sentinel or a deep-sunk host turns the guest's whole grid into a plateau.
                    hB = hB > IslandShape.Sea + 1e-3f ? hB + mergedSink : IslandShape.Sea;
                    gB = gB > IslandShape.Sea + 1e-3f ? gB + mergedSink : IslandShape.Sea;
                    // No bridge fill: a merge never invents land beyond the two inputs. Where the bodies overlap
                    // (the drive-in phase guarantees ~10 % of the smaller island) the hidden land is not stacked
                    // into a cliff on the spot; its volume goes into the broad uplift below.
                    merged.h[k] = Mathf.Max(hA, hB);
                    goal[k] = Mathf.Max(gA, gB);
                    overlap += Mathf.Max(0f, Mathf.Min(gA, gB));
                }

            float[] up = BroadUplift(merged, goal, contact, n, tang, H, minR, mergedSink, overlap, other.shapeSeed);

            merged.uplift = up;
            merged.upliftWeight = 0f;
            merged.sink = mergedSink;
            _shape = merged;
            _upliftT = _upliftDur <= 0f ? 1f : 0f;
            _upliftVersionTimer = 0f;

            Vector2 c = _shape.LandCentroid();
            _pos = ToWorld(c);
            _shape.origin -= c;
            ApplyTransform();

            _selfVel = momentum;
            // Left or right of the nose is judged from where the host's centre was when it was hit; seen from
            // the merged centroid an equal-sized guest's contact point lies dead centre.
            BeginBodyTurn(contact);
            _colors = null;

            var life = GetComponent<IslandLifeSystem>();
            if (life != null)
            {
                life.ShiftLocal(-c);
                life.AbsorbFrom(other.GetComponent<IslandLifeSystem>());
            }

            var herds = GetComponent<IslandHerdSystem>();
            if (herds != null)
            {
                herds.ShiftLocal(-c);
                herds.AbsorbFrom(other.GetComponent<IslandHerdSystem>());
                herds.Startle(contact - c, startleDuration);
            }

            var plates = PlateSystem.Instance;
            if (plates != null) plates.Impulse(_pos, momentum * 0.4f + (_bodyRight2 * n.x + _bodyFwd2 * n.y) * (vc * 0.2f));
            Impact?.Invoke(Mathf.Clamp01(energy / 220f));
            Merged?.Invoke(this, other, energy);

            other.enabled = false;
            other.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(other.gameObject);
            else DestroyImmediate(other.gameObject);

            if (_upliftT >= 1f) _shape.Bake();
            _hypsoDirty = true;
            if (floats)
            {
                _buoy = mergedBuoy;
                RebuildHypsometry(true);
            }
            RecomputeStats();
            RebuildMesh();
            _version++;
            // The merged mesh carries white (untinted) vertex colours; tint it now, not at the next life tick,
            // or the new island is drawn in raw grass green for about a dozen frames.
            if (life != null) life.RefreshAfterMerge();
        }
    }

    // The designed difficulty curve, pure so it can be asserted: how long an island of a given size floats
    // and how much of its buoyancy a guest gives back.
    public static class SinkBalance
    {
        public static float SecondsForArea(float area, float secondsSmall, float secondsHuge, float halfArea)
        {
            float half = Mathf.Max(1f, halfArea);
            return secondsHuge + (secondsSmall - secondsHuge) * half / (half + Mathf.Max(0f, area));
        }

        public static float RefloatShare(float guestArea, float hostArea, float exponent, float kindFactor)
        {
            if (guestArea <= 0f) return 0f;
            float ratio = Mathf.Clamp01(guestArea / Mathf.Max(1f, hostArea));
            return Mathf.Clamp01(kindFactor * Mathf.Pow(ratio, Mathf.Max(0.05f, exponent)));
        }
    }
}
