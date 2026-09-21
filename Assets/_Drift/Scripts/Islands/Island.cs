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
        // When set and returning a non-zero vector, replaces keyboard input for the player island (touch UI).
        public static Func<Vector2> InputProvider;
        public static bool InputLocked;

        public float landRadius = 3f;
        public float cellSize = 0.5f;
        public float moveSpeed = 8f;
        public float acceleration = 3.6f;
        public float dragBase = 0.45f;
        public float turnRateDegPerSec = 90f;
        public float turnResponse = 3f;
        public float turnEaseIn = 0.35f;
        public float velocityAlign = 2.5f;
        public float driftRotation = 2f;
        public float driftPeriod = 40f;
        // Merge turn = a critically damped angular spring. bodyTurnTime / bodyTurnMaxRate: seconds to get within
        // 5 % of the target and yaw rate cap (deg/s) for a start-sized island (bodyTurnRefArea); the ...Huge
        // values hold from bodyTurnHugeArea on, in between it follows the square root of the Agility.
        public float bodyTurnTime = 2.5f;
        public float bodyTurnTimeHuge = 5f;
        public float bodyTurnMaxRate = 70f;
        public float bodyTurnMaxRateHuge = 35f;
        public float bodyTurnRefArea = 24f;
        public float bodyTurnHugeArea = 2000f;
        public float bodyTurnCooldown = 8f;
        public float bodyTurnRapidAngle = 25f;
        public float bodyTurnMaxQueued = 120f;
        public float agilityArea = 40f;
        public bool useKeyboardInput = true;
        public int shapeSeed = 12345;
        public float heightOffset = 0f;
        public float bobAmplitude = 0.05f;
        public float carryResponse = 2.5f;
        public float upliftDuration = 1.5f;
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

        IslandShape _shape;
        IslandHerdSystem _herds;
        Mesh _mesh;
        Color[] _colors;
        Vector2 _pos;
        Vector2 _carry;
        Vector2 _selfVel;
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
        float _upliftMeshTimer, _upliftHypsoTimer;
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

        // The player's buoyancy is the share of its sink time that is left (1 = afloat, 0 = sunk): it drains
        // linearly, the HUD bar is a timer. Every other island reports its land share.
        public float Buoyancy
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
        public float SinkMultiplier => Mathf.Lerp(1f, stormSinkMultiplier, StormIntensity) * Mathf.Lerp(elongatedSinkMultiplier, 1f, Compactness);
        // Full-buoyancy sink time of this island as it is now (size, storm, form).
        public float SinkSecondsFull => SinkSecondsForArea(_areaRaw) / Mathf.Max(0.01f, SinkMultiplier);

        // Estimate at the current rate; infinite while sinking is held or for islands that never sink.
        public float SinkSecondsLeft
        {
            get
            {
                if (!sinkEnabled || !useKeyboardInput || _shape == null) return float.PositiveInfinity;
                return IsSunk ? 0f : Buoyancy * SinkSecondsFull;
            }
        }

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

        public Vector2 PlanarVelocity => _selfVel + _carry;
        public float CellArea => _shape != null ? _shape.cell * _shape.cell : 0.25f;
        public bool IsEmerging => _emergeTime > 0f;
        public float EmergeRemaining => Mathf.Max(0f, _emergeTime);

        public void ApplyImpactDrag(float dt) => _selfVel *= Mathf.Exp(-impactDrag * dt);
        public bool IsUplifting => _upliftT < 1f;
        public float Agility => AgilityFor(_area);
        public float AgilityFor(float area) => Mathf.Pow(agilityArea / (agilityArea + Mathf.Max(0f, area)), 0.6f);
        public float MaxSpeed => moveSpeed * (0.55f + 0.45f * Agility);

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
            if (useKeyboardInput && !InputLocked)
            {
                if (InputProvider != null) input = InputProvider();
                if (input.sqrMagnitude < 1e-4f) input = ReadKeyboardInput();
            }
            if (IsSunk) input = Vector2.zero;
            Tick(input, Time.deltaTime);
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

        static float DriftWave(float t) => 0.65f * Mathf.Sin(t) + 0.35f * Mathf.Sin(t * 0.41f + 1.7f);

        public void Tick(Vector2 input, float dt)
        {
            float ag = Agility;
            float sq = Mathf.Sqrt(ag);
            _clock += dt;

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

            // Ambient yaw drift: a seeded slow double sine (bounded by driftRotation * Agility) so islands
            // never sit perfectly still. It turns the body only: W always drives straight ahead.
            float drift = driftRotation * ag * DriftWave(_clock * (2f * Mathf.PI / Mathf.Max(1f, driftPeriod)) + _driftPhase);
            _bodyYaw = Mathf.DeltaAngle(0f, _bodyYaw + drift * dt + StepBodyTurns(dt));

            _selfVel += new Vector2(Forward.x, Forward.z) * (input.y * acceleration * ag * dt);
            _selfVel *= Mathf.Exp(-dragBase * ag * dt);
            AlignVelocity(dt);
            float max = MaxSpeed;
            if (_selfVel.sqrMagnitude > max * max) _selfVel = _selfVel.normalized * max;

            Vector2 plateVel = PlateSystem.Instance != null ? PlateSystem.Instance.SampleVelocity(_pos) : Vector2.zero;

            var storms = StormSystem.Instance;
            float storm = 0f;
            Vector2 gust = Vector2.zero;
            if (storms != null)
            {
                storm = storms.IntensityAt(_pos);
                if (storm > 0f) gust = storms.GustAt(_pos, storms.Clock);
            }
            StormIntensity = storm;
            StormGust = gust;

            _carry = Vector2.Lerp(_carry, plateVel + gust, 1f - Mathf.Exp(-carryResponse * dt));
            _pos += (_selfVel + _carry) * dt;

            if (_herds == null) _herds = GetComponent<IslandHerdSystem>();
            if (_herds != null) _herds.Agitation = storm;

            ApplyTransform();
        }

        // Keel effect: self-velocity swings toward the heading (speed preserved) so a turn redirects the
        // island instead of letting it slide sideways on the old course.
        void AlignVelocity(float dt)
        {
            float speed = _selfVel.magnitude;
            if (speed < 0.05f || velocityAlign <= 0f) return;
            Vector2 f2 = Forward2;
            Vector2 goal = Vector2.Dot(_selfVel, f2) >= 0f ? f2 : -f2;
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
            float angle = sign * Mathf.Lerp(Mathf.Min(bodyTurnRapidAngle, full), full, w);
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
            transform.rotation = Quaternion.LookRotation(BodyForward, Normal);
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
            if (isVolcano) _shape = IslandShape.CreateVolcano(landRadius, shapeSeed, cellSize);
            else if (Prebuilt != null && Prebuilt.Matches(archetype, radius, shapeSeed, cellSize, heightScale)) _shape = Prebuilt.shape;
            else _shape = IslandArchetypes.Create(archetype, radius, shapeSeed, cellSize, heightScale);
            Prebuilt = null;
            _emergeTime = 0f;
            _upliftT = 1f;
            _carry = Vector2.zero;
            _colors = null;
            _hypsoDirty = true;
            _buoy = 1f;
            LastRefloat = 0f;
            RecomputeStats();
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

        // Buoyancy drains linearly over SinkSecondsForArea(full area) (times storm and the form penalty: a
        // stretched island sinks faster, the incentive to grow from all sides). The sink depth follows from the
        // island's own hypsometric curve, so the timing holds whatever the terrain: plateau, ridges or a volcano.
        public void AdvanceSink(float dt)
        {
            if (!sinkEnabled || !useKeyboardInput || IsSunk || _shape == null || _emergeTime > 0f) return;
            SyncBuoyancy();
            float before = _shape.sink;
            _buoy = Mathf.Max(0f, _buoy - dt / Mathf.Max(0.1f, SinkSecondsFull));
            float share = LandShareAt(_buoy);
            float held = _hypso.LandFractionAt(before) - Mathf.Max(0f, maxLandLossPerSecond) * dt;
            _shape.sink = _hypso.DepthAt(Mathf.Max(share, held));
            _sinkVersionAccum += Mathf.Abs(_shape.sink - before);
            RefreshSink(dt, 0.5f, true);
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

        // The ridge of a merge rises over a few seconds, and with a merge every few seconds that is most of the
        // time on a big island. Rebuilding the whole terrain mesh and the hypsometric curve every frame cost 1 ms
        // on desktop (~4 ms on a phone): the ridge is now redrawn at 30 Hz and the depth that keeps the player's
        // buoyancy constant is re-derived at 10 Hz. The last step is always a full, exact rebuild.
        const float UpliftMeshInterval = 1f / 30f;
        const float UpliftHypsoInterval = 0.1f;

        public void AdvanceUplift(float dt)
        {
            if (_upliftT >= 1f) return;
            if (useKeyboardInput) SyncBuoyancy();
            _upliftT = _upliftDur <= 0f ? 1f : Mathf.Min(1f, _upliftT + dt / _upliftDur);
            _shape.upliftWeight = _upliftT * (2f - _upliftT);
            bool done = _upliftT >= 1f;
            _upliftHypsoTimer += dt;
            _upliftMeshTimer += dt;
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
                RebuildMesh();
                return;
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
            _carry = Vector2.zero;
            _bodyYaw = Mathf.DeltaAngle(0f, d.bodyYaw);
            ClearBodyTurns();
            RefreshBodyBasis();
            _emergeTime = 0f;
            _sinkMeshTimer = 0f;
            _sinkVersionAccum = 0f;
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

        public void MergeFrom(Island other, float closingSpeed, float convergence)
        {
            _shape.Bake();
            other._shape.Bake();
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
            // Broad, low ridge: a typical hit now peaks ~1 unit above the plateau (rock-topped hill, never
            // snow), so a merge reads as new usable land rather than a spike.
            float H = Mathf.Clamp(0.2f * Mathf.Sqrt(energy) * (1f + 0.25f * convergence), 0.4f, 0.8f + 0.25f * minR);
            _upliftDur = upliftDuration + 0.2f * H;
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
            var up = new float[nx * nz];

            float w = 0.7f * minR + 1.0f;
            float L = 1.6f * minR + 1.8f;
            Vector2 noiseOff = new Vector2(shapeSeed * 0.37f, other.shapeSeed * 0.11f);

            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i;
                    Vector2 p = merged.CellPos(i, j);
                    float hA = a.SampleRaw(p);
                    float hB = b.SampleRaw(other.ToLocal(ToWorld(p)));
                    // Only the guest's shelf/land is re-based; its flat sea floor must stay the Sea
                    // sentinel or a deep-sunk host turns the guest's whole grid into a plateau.
                    hB = hB > IslandShape.Sea + 1e-3f ? hB + mergedSink : IslandShape.Sea;
                    // Volume is conserved: where the two bodies overlap (the drive-in phase guarantees
                    // ~10 % of the smaller island) the overlapped land stacks instead of being dropped.
                    // No bridge fill — a merge never invents land beyond the two inputs.
                    float hh = Mathf.Max(hA, hB) + Mathf.Max(0f, Mathf.Min(hA, hB));

                    Vector2 rel = p - contact;
                    float s = Vector2.Dot(rel, n);
                    float u = Vector2.Dot(rel, tang);
                    merged.h[k] = hh;
                    float g = Mathf.Exp(-(s / w) * (s / w)) * Mathf.Exp(-(u / L) * (u / L));
                    float sf = s / (2.2f * w), uf = u / (1.5f * L);
                    float flank = 0.25f * Mathf.Exp(-sf * sf) * Mathf.Exp(-uf * uf);
                    float ridged = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(noiseOff.x + p.x * 0.8f, noiseOff.y + p.y * 0.8f) - 1f);
                    float landMask = IslandShape.Smooth(-0.2f, 0.3f, hh);
                    up[k] = H * (g * (0.8f + 0.2f * ridged) + flank) * landMask;
                }

            // The ridge takes its volume from the rest of the island: the same volume is shaved off
            // every land cell, so the shore creeps inward — higher means less wide.
            float upVolume = 0f, landArea = 0f;
            for (int k = 0; k < up.Length; k++)
            {
                upVolume += up[k];
                if (merged.h[k] > 0f) landArea += 1f;
            }
            if (landArea > 0f)
            {
                float erode = upVolume / landArea;
                for (int k = 0; k < up.Length; k++)
                    up[k] -= erode * IslandShape.Smooth(-0.3f, 0.2f, merged.h[k]);
            }

            merged.uplift = up;
            merged.upliftWeight = 0f;
            merged.sink = mergedSink;
            _shape = merged;
            _upliftT = _upliftDur <= 0f ? 1f : 0f;

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
