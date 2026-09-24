using Drift.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Drift.Islands
{
    public class IslandChaseCamera : MonoBehaviour
    {
        public Island target;
        public float distanceBehind = 7f;
        public float height = 9f;
        [Tooltip("Feste Blickrichtung: die Kamera dreht sich nie mit der Insel (Norden bleibt oben). Gilt für die Richtungssteuerung; mit der alten Lenksteuerung schaut die Kamera wie früher der Insel hinterher.")]
        public bool fixedOrientation = true;
        [Tooltip("In welche Richtung die feste Kamera schaut (Grad, 0 = nach Norden / +Z). Der Abenteuer-Ring erwartet 0.")]
        [Range(0f, 360f)] public float viewYawDeg = 0f;

        [Header("Kamera dreht mit (Gemütlich)")]
        [Tooltip("Wie schnell sich die Kamera in die Fahrtrichtung dreht: 1 = in etwa einer Sekunde eingeschwenkt. 0 = feste Nordsicht wie bisher.")]
        [Range(0f, 3f)] public float courseFollow = 1f;
        [Tooltip("Höchstes Drehtempo der Kamera (Grad pro Sekunde), damit eine Kehrtwende nie ruckt.")]
        [Range(5f, 180f)] public float courseTurnMax = 90f;
        [Tooltip("Erst ab diesem Tempo (u/s) dreht die Kamera mit; langsamer und im Stand bleibt sie stehen.")]
        [Range(0f, 4f)] public float courseMinSpeed = 1.2f;
        [Tooltip("Abenteuer-Ring: so viele Grad schaut die Kamera nach oben, damit der aufsteigende Ring im Bild ist. Weniger = mehr Wasser unter der Insel, das vorbeirauscht (Tempo), mehr = weiter voraus sehen.")]
        [Range(0f, 30f)] public float ringPitchUp = 7f;
        [Tooltip("Abenteuer-Ring: wie stark die Kamera entlang der Strecke statt hinter die Insel schaut (0 = nur Inselrichtung).")]
        [Range(0f, 1f)] public float ringAlongBias = 0.7f;
        [Tooltip("Abenteuer-Ring: Mindestabstand der Kamera zu den Randwänden.")]
        [Range(0f, 10f)] public float ringWallMargin = 3f;
        public float followLerp = 5f;
        [Tooltip("Abenteuer-Ring: wie eng die Kamera seitlich an der Insel bleibt (pro Sekunde). Größer = die Insel bleibt beim seitlichen Lenken mittiger im Bild.")]
        [Range(0f, 60f)] public float ringLateralFollow = 28f;
        public float lookLerp = 7f;
        public float referenceRadius = 3f;
        public float zoomExponent = 0.85f;
        // Halved when the shake stopped being fed back into the smoothed pose: the old loop re-shook an
        // already shaken position, which roughly doubled every impact before it decayed.
        public float shakeAmount = 0.07f;
        public float shakeDecay = 3f;

        public float zoomMin = 0.08f;
        public float zoomMax = 3f;
        public float groundClearance = 0.9f;
        public float closeGroundClearance = 0.22f;
        public float nearClipFar = 0.3f;
        public float nearClipClose = 0.05f;
        public float scrollZoomSpeed = 0.0012f;
        public float keyZoomSpeed = 1.2f;
        public float zoomSmooth = 8f;

        [Header("Tempogefühl")]
        [Tooltip("Tempogefühl der Kamera: Blickwinkel und Abstand wachsen mit dem Tempo, bei Höchsttempo wackelt das Bild ganz leicht, in Kurven legt es sich etwas. Aus = ruhige Kamera wie bisher.")]
        public bool speedFeelEnabled = true;
        [Tooltip("Grundblickwinkel der Kamera in Grad. 0 = der Wert, den die Kamera beim Start hat.")]
        [Range(0f, 90f)] public float baseFieldOfView = 0f;
        [Tooltip("So viele Grad öffnet der Blickwinkel bei vollem Tempo zusätzlich. 0 = aus.")]
        [Range(0f, 25f)] public float speedFovGain = 4f;
        [Tooltip("Um diesen Anteil zieht sich die Kamera bei vollem Tempo weiter zurück (0,3 = 30 % mehr Abstand).")]
        [Range(0f, 1f)] public float speedPullBack = 0.14f;
        [Tooltip("Wie schnell Blickwinkel und Abstand dem Tempo folgen (klein = träge, groß = sofort).")]
        [Range(0.5f, 10f)] public float speedFeelResponse = 3.5f;
        [Tooltip("Stärke des leichten Rüttelns bei Höchsttempo. 0 = aus.")]
        [Range(0f, 0.5f)] public float speedShake = 0.018f;
        [Tooltip("Ab diesem Anteil des Höchsttempos fängt das Rütteln an (1 = erst ab eigenem Höchsttempo, darüber trägt die Strömung).")]
        [Range(0f, 1.4f)] public float speedShakeStart = 0.75f;
        [Tooltip("Wie weit sich das Bild in eine Kurve legt (Grad). 0 = aus.")]
        [Range(0f, 10f)] public float turnRoll = 1.2f;

        [Header("Tempogefühl im Abenteuer")]
        [Tooltip("Abenteuer: so viele Grad öffnet der Blickwinkel bei vollem Tempo zusätzlich (statt des Werts oben).")]
        [Range(0f, 12f)] public float ringSpeedFovGain = 4.5f;
        [Tooltip("Abenteuer: um diesen Anteil rückt die Kamera bei vollem Tempo näher und tiefer heran, statt zurückzuweichen. Mit dem weiteren Blickwinkel rauscht das Wasser schneller vorbei, die Insel bleibt gleich groß.")]
        [Range(0f, 0.3f)] public float ringSpeedDolly = 0.12f;
        [Tooltip("Abenteuer: so viele Grad öffnet der Blickwinkel bei vollem Schwung zusätzlich (das Tempo selbst ist dann bis doppelt so hoch).")]
        [Range(0f, 8f)] public float ringMomentumFov = 3f;
        [Tooltip("Abenteuer: bis zu diesem Tempo (u/s) bleibt die Kamera weich hinter der Insel zurück; schneller (Schwung, Wal) holt sie entlang der Strecke auf, damit die Insel nicht im Bild schrumpft.")]
        [Range(5f, 80f)] public float ringLagSpeed = 24f;
        [Tooltip("Abenteuer, Wal-Schub (die Insel fährt durch Inseln): so viele Einheiten steigt die Kamera, damit man von oben sieht, wie die Insel hindurchgleitet, statt dass ein Berg das Bild füllt.")]
        [Range(0f, 12f)] public float ringGhostLift = 5f;

        [Header("Schub")]
        [Tooltip("So viele Grad weiter wird der Blickwinkel, solange ein voller Schub läuft (klingt mit dem Schub aus).")]
        [Range(0f, 10f)] public float boostFovGain = 4f;
        [Tooltip("Schub-Faktor, der als voller Schub zählt (Treibgut gibt 1,55).")]
        [Range(1.05f, 2.5f)] public float boostReference = 1.55f;
        [Tooltip("Blickwinkel-Stoß beim Einsammeln eines Schubs (Grad). Schwillt weich an und ab, nie ein Ruck.")]
        [Range(0f, 10f)] public float boostFovKick = 3.5f;
        [Tooltip("Wie lange der Stoß beim Einsammeln eines Schubs dauert – Abklingzeit in Sekunden, der Höhepunkt kommt nach etwa 0,7 × dieser Zeit.")]
        [Range(0.05f, 1f)] public float boostKickSeconds = 0.4f;
        [Tooltip("Im Gemütlich-Modus wirken Schub-Blickwinkel, Stoß und Tempostreifen nur so stark (Surfen, Strömung).")]
        [Range(0f, 1f)] public float cozyBoostScale = 0.5f;

        [Header("Knapp vorbei (Abenteuer)")]
        [Tooltip("Blickwinkel-Stoß (Grad), wenn eine Hindernisinsel knapp vorbeigeht.")]
        [Range(0f, 8f)] public float dodgeFovKick = 2.5f;
        [Tooltip("So weit wird die Kamera beim knappen Vorbeifahren kurz von der Insel weggeschubst (Einheiten).")]
        [Range(0f, 1.5f)] public float dodgeSway = 0.3f;
        [Tooltip("Wie lange der Schubs beim knappen Vorbeifahren dauert – Abklingzeit in Sekunden, der Höhepunkt kommt nach etwa 0,7 × dieser Zeit.")]
        [Range(0.05f, 1f)] public float dodgeKickSeconds = 0.35f;

        [Header("Große Inseln (Gemütlich)")]
        [Tooltip("Bis zu diesem Inselradius wächst der Kameraabstand wie bisher mit der Insel, darüber nur noch gedämpft – so bleiben Tiere auf großen Inseln erkennbar. Zoomen zeigt weiterhin die ganze Insel.")]
        [Range(3f, 60f)] public float framingKneeRadius = 10f;
        [Tooltip("Wie stark der Abstand oberhalb des Knicks noch mitwächst (1 = wie früher, 0 = gar nicht mehr).")]
        [Range(0f, 1f)] public float framingSoftExponent = 0.5f;
        [Tooltip("Steht die Insel eine Weile still, fährt die Kamera auf diesen Anteil des Abstands heran, damit man das Leben sieht (1 = aus). Beim Lenken fährt sie wieder zurück.")]
        [Range(0.3f, 1f)] public float idleCloseIn = 0.7f;
        [Tooltip("Nach so vielen Sekunden Stillstand fährt die Kamera heran.")]
        [Range(0.5f, 20f)] public float idleDelay = 4f;
        [Tooltip("So viele Sekunden braucht das Heranfahren (sanft).")]
        [Range(0.3f, 10f)] public float idleInSeconds = 2.5f;
        [Tooltip("So viele Sekunden braucht das Zurückfahren, sobald gelenkt wird.")]
        [Range(0.1f, 3f)] public float idleOutSeconds = 0.6f;
        [Tooltip("Langsamer als das (u/s) gilt die Insel als stillstehend.")]
        [Range(0.1f, 4f)] public float idleSpeed = 1.2f;
        [Tooltip("Erst ab diesem Inselradius fährt die Kamera im Stillstand heran (voll ab dem Doppelten); kleine Inseln sind nah genug.")]
        [Range(1f, 30f)] public float idleMinRadius = 6f;

        [Header("Verschmelzen")]
        [Tooltip("Blickwinkel-Stoß beim Verschmelzen (Grad).")]
        [Range(0f, 20f)] public float mergeFovKick = 6f;
        [Tooltip("Wie weit die Kamera beim Verschmelzen kurz heranfährt (Anteil des Abstands).")]
        [Range(0f, 0.5f)] public float mergeZoomIn = 0.12f;
        [Tooltip("Wie kräftig die Kamera beim Verschmelzen wackelt. 0 = nur das übliche Rütteln.")]
        [Range(0f, 1.5f)] public float mergeShake = 0.8f;
        [Tooltip("In wie vielen Sekunden der Stoß beim Verschmelzen abklingt.")]
        [Range(0.05f, 1.5f)] public float mergeKickSeconds = 0.35f;

        float _shake;
        float _zoom = 1f;
        float _zoomTarget = 1f;
        System.Func<Vector3> _followPos;
        float _followRadius;
        Island _followGround;
        Camera _cam;
        Vector3 _lastFocus;
        bool _hasLastFocus;
        float _easeT = 1f, _easeSeconds = 0.6f, _easeFromClose;
        Vector3 _easeFromOffset;
        Quaternion _easeFromRot;
        readonly SpeedFeel.Tracker _feel = new();
        float _framing, _mergeKick, _turnRateDeg, _lastHeadingDeg, _capturedFov = 60f;
        bool _hasHeading, _hasCapturedFov;
        // The smoothed chase position WITHOUT the shake: the shake is an offset added on top of it every frame.
        // Adding it to the transform and smoothing from there again makes the amplitude frame-rate dependent,
        // and at dt = 0 (Time.timeScale 0 while the pause menu is up) nothing pulls the camera back at all -
        // it walked away from the island for as long as the menu stayed open.
        Vector3 _pose;
        bool _hasPose;
        float _viewYaw, _steerYaw;
        float _boostFeel, _boostKick, _lastBoostLeft, _lastBoostFactor = 1f, _dodgeKick, _dodgeSide, _momentumFeel, _ghostFeel;
        float _idleSeconds, _lifeZoom = 1f;

        public float Zoom => _zoom;
        // True while the view does not turn with the island's body: the island never turns under the direct
        // steering, so the view follows a compass direction of its own (north, or the course). The old
        // steering wheel needs the camera behind the heading instead.
        public bool ViewIsFixed => fixedOrientation && Island.DirectionSteering;
        // The cozy open sea swings the view onto the course; the adventure ring must keep looking along the
        // track (viewYawDeg 0 plus ringAlongBias), so it never follows.
        public bool CourseFollowActive =>
            FollowsCourse(ViewIsFixed, courseFollow, RingWorld.Active != null, GameModes.Current == GameMode.Cozy);

        public static bool FollowsCourse(bool viewIsFixed, float courseFollow, bool onRing, bool cozy) =>
            viewIsFixed && courseFollow > 0f && !onRing && cozy;
        // Where the camera looks (degrees). Read from the intent, not from the transform: the ring's pitch and
        // the turn roll leak into transform.eulerAngles.y.
        public float ViewYawDeg => ViewIsFixed ? _viewYaw : transform.eulerAngles.y;
        public Vector3 ViewForward => Quaternion.Euler(0f, ViewYawDeg, 0f) * Vector3.forward;
        // The yaw the steering maps screen directions through - NOT the view yaw while a direction is held.
        // The view swings onto the course, and a frame that swung with it would turn the very direction being
        // held: the island would circle for ever. So the frame is frozen for as long as the player points
        // somewhere and only catches up with the view once the stick (or the phone) is let go - by then the
        // view looks along the course, so "up the screen" is where the island is going.
        public float SteerYawDeg => ViewIsFixed ? _steerYaw : transform.eulerAngles.y;
        // Set every frame by whoever feeds the direction (SessionScreens): true while a direction is given.
        public bool SteerHeld { get; set; }
        // Set every frame by SessionScreens: true while the tilt steers. A phone is never "let go" the way a stick
        // is, so the frozen steering frame and a view swinging onto the course drifted apart: the same tilt then
        // pointed somewhere else on the screen and the view turned like a boat's (owner, phone test v0.6.2: "als ob
        // die Insel erst noch drehen muss"). Held, the view keeps its compass direction and every screen direction
        // stays where it is.
        public bool HoldCourse { get; set; }
        // courseFollow 1 settles the view in about a second.
        public float CourseResponse => 3f * Mathf.Max(0f, courseFollow);
        // The speed feel this camera is showing, for tests and for anyone who wants to match it.
        public float SpeedDrive => _feel.Drive;
        public float FlowAmount => _feel.Flow;
        public float SurfAmount => _feel.Surf;
        public float TurnRateDegPerSec => _turnRateDeg;
        public float MergeKick => _mergeKick;
        // Share of a full boost running right now (smoothed, 0 while suspended or zoomed close).
        public float BoostFeel => _boostFeel;
        // The island's "Schwung" as the ring camera shows it (smoothed, 0 off the ring / while suspended).
        public float MomentumFeel => _momentumFeel;
        // The swell of the last boost pickup and of the last near miss (0..1, see SpeedFeel.Bump).
        public float BoostPunch => SpeedFeel.Bump(_boostKick);
        public float DodgePunch => SpeedFeel.Bump(_dodgeKick);
        // 0 at puttering speed, 1 at the top of the framing: the share that also drives the rumble.
        public float TopSpeedShare => SpeedFeel.ShakeAmount(_framing, speedShakeStart);
        // Cozy plays the boost effects softer (surf kicks and the flow push are small rewards, not a race).
        public float BoostModeScale => RingWorld.Active != null ? 1f : cozyBoostScale;
        // The idle "life zoom" multiplier on the chase distance (1 = none).
        public float LifeZoom => _lifeZoom;
        public float BaseFieldOfView => baseFieldOfView > 0f ? baseFieldOfView : _capturedFov;
        float SpeedFovGain => RingWorld.Active != null ? ringSpeedFovGain : speedFovGain;
        // What the camera would set the field of view to right now (base + speed + boost + kicks).
        public float FieldOfViewTarget => BaseFieldOfView + SpeedFovGain * _framing + ringMomentumFov * _momentumFeel + mergeFovKick * _mergeKick
            + BoostModeScale * (boostFovGain * _boostFeel + boostFovKick * BoostPunch) + dodgeFovKick * DodgePunch;
        // Screen effects of the speed feel (streaks) only show while the chase camera itself drives the view.
        public bool FeelVisible => !Suspended && _followPos == null && speedFeelEnabled && CloseBlend(_zoom) < 1f;
        public bool HasFollowOverride => _followPos != null;
        public Island FollowGround => _followGround;
        // While suspended the camera transform is left alone (photo mode drives it directly); the LOD
        // provider and impact hook stay installed, unlike disabling the component.
        public bool Suspended { get; set; }
        // The close-up blend (0..1) of whoever drives the camera while suspended: keeps the near clip plane easing.
        public float SuspendedClose { get; set; }
        public bool Easing => _easeT < 1f;

        // Hands the camera back after a suspension without a jump: over `seconds` (unscaled) the pose blends from
        // where the camera stands, carried along with the focus, into the chase pose.
        public void ResumeEased(float seconds = 0.6f)
        {
            Suspended = false;
            if (target == null || seconds <= 0f) { SnapToTarget(); return; }
            DesiredPose(out _, out _, out _, out Vector3 focus);
            _easeFromOffset = transform.position - focus;
            _easeFromRot = transform.rotation;
            _easeFromClose = SuspendedClose;
            _easeSeconds = seconds;
            _easeT = 0f;
        }

        // One smoothing step of the shake-free pose. At dt <= 0 - the pause menu holds Time.timeScale at 0 -
        // it stands still instead of snapping, and the shake is added to the RESULT of this, never fed back
        // into it: otherwise nothing pulls the camera back and it drifts off the island frame by frame.
        public static Vector3 SmoothPose(Vector3 pose, Vector3 desired, float lerpPerSecond, float dt) =>
            dt > 0f ? Vector3.Lerp(pose, desired, 1f - Mathf.Exp(-lerpPerSecond * dt)) : pose;

        public static void EasePose(Vector3 fromPos, Quaternion fromRot, Vector3 toPos, Quaternion toRot, float t, out Vector3 pos, out Quaternion rot)
        {
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            pos = Vector3.Lerp(fromPos, toPos, s);
            rot = Quaternion.Slerp(fromRot, toRot, s);
        }

        // Frames worldPos (a herd centre, say) instead of the target island. radius sets the framing scale
        // like an island's bounding radius; ground is the island whose terrain the camera stays above
        // (the target when null). Zoom input keeps working and the target island keeps steering.
        public void SetFollowOverride(System.Func<Vector3> worldPos, float radius, Island ground = null)
        {
            _followPos = worldPos;
            _followRadius = Mathf.Max(0.5f, radius);
            _followGround = ground;
        }

        public void ClearFollowOverride()
        {
            _followPos = null;
            _followGround = null;
        }

        public void SetZoom(float z)
        {
            _zoomTarget = _zoom = Mathf.Clamp(z, zoomMin, ZoomMaxEffective);
        }

        public float ZoomTarget => _zoomTarget;

        // The softened framing of big islands must not take the overview away: the zoom range grows by exactly
        // what the soft radius took off, so zooming all the way out still shows as much as it used to.
        public float ZoomMaxEffective =>
            zoomMax * (target != null ? OverviewZoomFactor(target.BoundingRadius, framingKneeRadius, framingSoftExponent, referenceRadius, zoomExponent) : 1f);

        public static float FramingScale(float radius, float referenceRadius, float zoomExponent) =>
            Mathf.Pow(Mathf.Max(1f, radius / Mathf.Max(0.01f, referenceRadius)), zoomExponent);

        public static float OverviewZoomFactor(float radius, float knee, float soft, float referenceRadius, float zoomExponent) =>
            FramingScale(radius, referenceRadius, zoomExponent) /
            FramingScale(SpeedFeel.SoftRadius(radius, knee, soft), referenceRadius, zoomExponent);

        // Eases to an absolute zoom (SetZoom jumps).
        public void ZoomTo(float z)
        {
            _zoomTarget = Mathf.Clamp(z, zoomMin, ZoomMaxEffective);
        }

        public void ZoomBy(float factor)
        {
            _zoomTarget = Mathf.Clamp(_zoomTarget * factor, zoomMin, ZoomMaxEffective);
        }

        void ReadZoomInput(float dt)
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                float sc = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(sc) > 0.01f) _zoomTarget *= Mathf.Exp(-sc * scrollZoomSpeed);
            }
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.qKey.isPressed || kb.numpadPlusKey.isPressed) _zoomTarget *= Mathf.Exp(-keyZoomSpeed * dt);
                if (kb.eKey.isPressed || kb.numpadMinusKey.isPressed) _zoomTarget *= Mathf.Exp(keyZoomSpeed * dt);
            }
            _zoomTarget = Mathf.Clamp(_zoomTarget, zoomMin, ZoomMaxEffective);
            _zoom = Mathf.Lerp(_zoom, _zoomTarget, 1f - Mathf.Exp(-zoomSmooth * dt));
        }

        void OnEnable()
        {
            Island.Impact += OnImpact;
            Island.Merged += OnMerged;
            RingWorld.Dodged += OnDodged;
            if (_cam == null) _cam = GetComponent<Camera>();
            // Only ever read once: the finale re-enables this component after it has written its own field of
            // view, and capturing that would ratchet the base up run after run.
            if (_cam != null && !_hasCapturedFov) { _capturedFov = _cam.fieldOfView; _hasCapturedFov = true; }
            _feel.Reset();
            _framing = 0f;
            _mergeKick = 0f;
            ResetKicks();
            _hasHeading = false;
            _hasPose = false;
            _viewYaw = _steerYaw = viewYawDeg;
            SteerHeld = false;
            HoldCourse = false;
            LifeLod.DistanceProvider = PlanarDistance;
            LifeEnvironment.ViewDistanceProvider = ViewDistance;
            LifeEnvironment.PointOfInterest = NearestOtherIsland;
        }

        void OnDisable()
        {
            Island.Impact -= OnImpact;
            Island.Merged -= OnMerged;
            RingWorld.Dodged -= OnDodged;
            if (_cam != null) _cam.fieldOfView = BaseFieldOfView;
            if (LifeLod.DistanceProvider == (System.Func<Vector3, float>)PlanarDistance) LifeLod.DistanceProvider = null;
            if (LifeEnvironment.ViewDistanceProvider == (System.Func<Vector3, float>)ViewDistance) LifeEnvironment.ViewDistanceProvider = null;
            if (LifeEnvironment.PointOfInterest == (LifeEnvironment.PointOfInterestProvider)NearestOtherIsland) LifeEnvironment.PointOfInterest = null;
        }

        // The life tiers are keyed on the planar distance to the camera so a high camera over a big island
        // still counts the island under it as near.
        float PlanarDistance(Vector3 p)
        {
            Vector3 c = transform.position;
            float dx = c.x - p.x, dz = c.z - p.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        float ViewDistance(Vector3 p) => Vector3.Distance(transform.position, p);

        // What herds line up to watch: the nearest other island whose shore is within reach.
        static bool NearestOtherIsland(Vector3 from, float maxDistance, out Vector3 point)
        {
            point = default;
            float best = maxDistance;
            bool found = false;
            Vector2 f = new Vector2(from.x, from.z);
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var isl = all[i];
                if (isl == null || !isl.isActiveAndEnabled) continue;
                float centre = Vector2.Distance(isl.PlanarPosition, f);
                if (centre < isl.BoundingRadius) continue;
                float d = centre - isl.BoundingRadius;
                if (d >= best) continue;
                best = d;
                point = isl.transform.position;
                found = true;
            }
            return found;
        }

        void OnImpact(float intensity)
        {
            _shake = Mathf.Max(_shake, intensity);
        }

        // The player's own merge is the highlight: a short field-of-view punch, a dolly kick towards the
        // contact and extra shake - no slow motion.
        void OnMerged(Island host, Island guest, float energy)
        {
            if (target == null || (host != target && guest != target)) return;
            float k = Mathf.Clamp01(0.45f + energy / 220f);
            _mergeKick = Mathf.Max(_mergeKick, k);
            _shake = Mathf.Max(_shake, mergeShake * k);
        }

        // A near miss: a short swell of the field of view and a sway away from the island that just went past.
        void OnDodged(Island obstacle)
        {
            if (target == null || obstacle == null || Suspended) return;
            _dodgeKick = 1f;
            Vector3 d = obstacle.transform.position - target.transform.position;
            _dodgeSide = Vector3.Dot(d, transform.right) >= 0f ? -1f : 1f;
        }

        void ResetKicks()
        {
            _boostFeel = _boostKick = _dodgeKick = _momentumFeel = _ghostFeel = 0f;
            _lastBoostLeft = target != null ? target.BoostRemaining : 0f;
            _lastBoostFactor = target != null ? target.BoostFactor : 1f;
            _idleSeconds = 0f;
            _lifeZoom = 1f;
        }

        // A boost that was just collected shows as BoostRemaining jumping up; the flow push's short refreshes
        // (0.4 s from <= 0.25 s left) stay below MinPickupGain and never punch.
        const float MinPickupGain = 0.5f;

        void StepBoost(float dt)
        {
            float left = target.BoostRemaining, factor = target.BoostFactor;
            bool visible = FeelVisible;
            if (visible && left > _lastBoostLeft + MinPickupGain)
                _boostKick = Mathf.Max(_boostKick, SpeedFeel.PickupStrength(_lastBoostFactor, factor, boostReference, 0.3f));
            _lastBoostLeft = left;
            _lastBoostFactor = factor;
            float want = visible ? SpeedFeel.BoostShare(factor, boostReference) * (1f - CloseBlend(_zoom)) : 0f;
            _boostFeel = dt > 0f ? Mathf.Lerp(_boostFeel, want, 1f - Mathf.Exp(-4f * dt)) : want;
            float mom = visible && RingWorld.Active != null ? target.Momentum * (1f - CloseBlend(_zoom)) : 0f;
            _momentumFeel = dt > 0f ? Mathf.Lerp(_momentumFeel, mom, 1f - Mathf.Exp(-1.5f * dt)) : mom;
            float ghost = visible && RingWorld.Active != null && target.Ghosting ? 1f : 0f;
            _ghostFeel = dt > 0f ? Mathf.Lerp(_ghostFeel, ghost, 1f - Mathf.Exp(-(ghost > _ghostFeel ? 3f : 1.2f) * dt)) : ghost;
            _boostKick = SpeedFeel.Decay(_boostKick, dt, boostKickSeconds);
            _dodgeKick = SpeedFeel.Decay(_dodgeKick, dt, dodgeKickSeconds);
        }

        // Cozy "life zoom": after idleDelay seconds without moving or steering the camera closes in on a big island
        // so its animals are more than a few pixels tall, and backs out as soon as the island is steered again.
        // Only at the default zoom: a player who zoomed out on purpose wants the overview.
        void StepLifeZoom(float dt)
        {
            bool eligible = Application.isPlaying && GameModes.Current == GameMode.Cozy && RingWorld.Active == null
                && _followPos == null && !Island.InputLocked && Mathf.Abs(_zoomTarget - 1f) < 0.35f && idleCloseIn < 1f;
            bool idle = eligible && !SteerHeld && target.SelfVelocity.sqrMagnitude < idleSpeed * idleSpeed;
            _idleSeconds = idle ? _idleSeconds + Mathf.Max(0f, dt) : 0f;
            float want = idle && _idleSeconds >= idleDelay ? SpeedFeel.IdleCloseIn(target.BoundingRadius, idleCloseIn, idleMinRadius) : 1f;
            _lifeZoom = dt > 0f ? SpeedFeel.Approach(_lifeZoom, want, dt, idleOutSeconds, idleInSeconds) : want;
        }

        // Signed yaw rate of the course, measured here so the camera needs nothing private from Island. Under
        // direct-direction steering the heading never turns - the course the island is actually driven along
        // is what a curve looks like, so that is what the bank reads.
        void UpdateTurnRate(float dt)
        {
            Vector3 f = target.Forward;
            bool moving = true;
            if (Island.DirectionSteering)
            {
                Vector2 v = target.SelfVelocity;
                moving = v.sqrMagnitude > 0.04f;
                if (moving) f = new Vector3(v.x, 0f, v.y);
            }
            // Drifting to a halt must not spin the measured course: the last real one is held.
            float ang = moving ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : _lastHeadingDeg;
            if (_hasHeading && dt > 1e-5f)
            {
                float raw = Mathf.DeltaAngle(_lastHeadingDeg, ang) / dt;
                _turnRateDeg = Mathf.Lerp(_turnRateDeg, raw, 1f - Mathf.Exp(-6f * dt));
            }
            else if (!_hasHeading) _turnRateDeg = 0f;
            _lastHeadingDeg = ang;
            _hasHeading = true;
        }

        // The course the view swings onto: the direction the island is really travelling in. Below minSpeed
        // there is no course to speak of, so the view holds still instead of spinning while the island glides
        // to a stop. Reading the VELOCITY (not the input) is what keeps the loop stable: the steering frame is
        // fed back from the view, so a view that chased the input would chase itself.
        public static bool CourseYaw(Vector2 velocity, float minSpeed, out float yawDeg)
        {
            yawDeg = 0f;
            float min = Mathf.Max(0.01f, minSpeed);
            if (velocity.sqrMagnitude < min * min) return false;
            yawDeg = Mathf.Atan2(velocity.x, velocity.y) * Mathf.Rad2Deg;
            return true;
        }

        // One smoothing step towards targetYaw: exponential (response 3 settles in about a second) and capped
        // at maxDegPerSecond so even a full turnabout stays a slow swing.
        public static float FollowYaw(float yaw, float targetYaw, float response, float maxDegPerSecond, float dt)
        {
            if (dt <= 0f) return yaw;
            float step = Mathf.DeltaAngle(yaw, targetYaw) * (1f - Mathf.Exp(-Mathf.Max(0f, response) * dt));
            float cap = Mathf.Max(0f, maxDegPerSecond) * dt;
            return Mathf.Repeat(yaw + Mathf.Clamp(step, -cap, cap), 360f);
        }

        const float SteerCatchUp = 8f;

        // The steering frame: frozen while a direction is held (so a held direction is a straight line),
        // catching up with the view within a few tenths of a second after it is released.
        public static float SteerYaw(float steerYaw, float viewYaw, bool held, float dt) =>
            held ? steerYaw : FollowYaw(steerYaw, viewYaw, SteerCatchUp, 720f, dt);

        // One frame of the course follow: the view swings onto the travel, the steering frame stays frozen while a
        // direction is held. holdCourse (the tilt) keeps the view where it is; the frame then simply is the view.
        public static void StepYaws(ref float viewYaw, ref float steerYaw, Vector2 velocity, bool held, bool holdCourse,
            float minSpeed, float response, float maxDegPerSecond, float dt)
        {
            if (!holdCourse && CourseYaw(velocity, minSpeed, out float course))
                viewYaw = FollowYaw(viewYaw, course, response, maxDegPerSecond, dt);
            steerYaw = SteerYaw(steerYaw, viewYaw, held && !holdCourse, dt);
        }

        void StepViewYaw(float dt)
        {
            // In the Editor the slider IS the view: nothing is driving, so nothing would ever pull the yaw back.
            if (!CourseFollowActive || !Application.isPlaying)
            {
                _viewYaw = _steerYaw = viewYawDeg;
                return;
            }
            StepYaws(ref _viewYaw, ref _steerYaw, target.SelfVelocity, SteerHeld, HoldCourse, courseMinSpeed, CourseResponse, courseTurnMax, dt);
        }

        void StepFeel(float dt)
        {
            _feel.Step(target, dt);
            float want = speedFeelEnabled && _followPos == null ? SpeedFeel.Framing(_feel.Drive) * (1f - CloseBlend(_zoom)) : 0f;
            _framing = dt > 0f ? Mathf.Lerp(_framing, want, 1f - Mathf.Exp(-speedFeelResponse * dt)) : want;
            if (_mergeKick > 0f)
                _mergeKick = dt > 0f ? _mergeKick * Mathf.Exp(-dt / Mathf.Max(0.05f, mergeKickSeconds)) : _mergeKick;
            if (_mergeKick < 0.001f) _mergeKick = 0f;
            StepBoost(dt);
            StepLifeZoom(dt);
            UpdateTurnRate(dt);
            StepViewYaw(dt);
        }

        void ApplyFov()
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            if (_cam == null) return;
            _cam.fieldOfView = Mathf.Clamp(FieldOfViewTarget, 5f, 170f);
        }

        void LateUpdate()
        {
            if (target == null) return;
            if (Suspended)
            {
                ApplyNearClip(Mathf.Clamp01(SuspendedClose));
                _hasLastFocus = false;
                _hasHeading = false;
                _easeT = 1f;
                _framing = 0f;
                _mergeKick = 0f;
                ResetKicks();
                _feel.Reset();
                _hasPose = false;
                if (_cam == null) _cam = GetComponent<Camera>();
                if (_cam != null) _cam.fieldOfView = BaseFieldOfView;
                return;
            }

            float feelDt = Application.isPlaying ? Time.deltaTime : 0f;
            // Paused (Time.timeScale 0): the world stands still, so the camera does too. Stepping the speed
            // feel here would snap the framing to the frozen velocity, and every smoothing step below is a
            // no-op at dt = 0 while the shake offset would keep pushing - that is what pulled the view away
            // from the island as soon as a menu opened.
            if (Application.isPlaying && feelDt <= 0f)
            {
                ApplyFov();
                return;
            }
            if (Application.isPlaying) ReadZoomInput(feelDt);
            StepFeel(feelDt);
            ApplyFov();

            DesiredPose(out Vector3 desiredPos, out Quaternion desiredRot, out float scale, out Vector3 focus);
            if (!_hasPose) { _pose = transform.position; _hasPose = true; }

            if (_easeT < 1f)
            {
                // Capped so a hitch on the hand-back frame cannot turn the ease into a snap.
                _easeT = Mathf.Min(1f, _easeT + Mathf.Min(Time.unscaledDeltaTime, 0.05f) / Mathf.Max(0.01f, _easeSeconds));
                EasePose(focus + _easeFromOffset, _easeFromRot, desiredPos, desiredRot, _easeT, out Vector3 easedPos, out Quaternion easedRot);
                _pose = easedPos;
                transform.SetPositionAndRotation(easedPos, easedRot);
                _lastFocus = focus;
                _hasLastFocus = true;
                ApplyNearClip(Mathf.Lerp(_easeFromClose, CloseBlend(_zoom), Mathf.SmoothStep(0f, 1f, _easeT)));
                ReportView();
                return;
            }

            // Close up the follow lag (speed / followLerp, over a unit at full speed) is larger than the
            // camera distance, so the camera is carried along with the focus and only the offset is smoothed.
            float close = CloseBlend(_zoom);
            if (_hasLastFocus && close > 0f)
            {
                Vector3 carried = focus - _lastFocus;
                if (carried.sqrMagnitude < 25f) _pose += carried * close;
            }
            _lastFocus = focus;
            _hasLastFocus = true;
            ApplyNearClip(close);

            _pose = SmoothPose(_pose, desiredPos, followLerp, feelDt);
            // Across the ring the soft follow lagged ~speed/followLerp = 2-3 u behind a sideways dash, and a portrait
            // phone sees only ~3 u either side of the island at race distance: the island slid off the screen edge
            // (owner, first APK test). Across the track the camera stays close; along it the lag keeps the speed feel.
            if (RingWorld.Active != null && _followPos == null && ringLateralFollow > 0f)
                _pose.x = Mathf.Lerp(_pose.x, desiredPos.x, 1f - Mathf.Exp(-ringLateralFollow * feelDt));
            // Along the track the lag is speed / followLerp: at double speed or in the whale's x2.5 it put the camera
            // 6-10 u further back and the island shrank on screen. Above ringLagSpeed the follow tightens with the
            // speed, so the lag tops out at ringLagSpeed / followLerp while a surge still reads as pulling ahead.
            if (RingWorld.Active != null && _followPos == null && ringLagSpeed > 0f)
            {
                float extra = Mathf.Abs(target.PlanarVelocity.y) / ringLagSpeed - 1f;
                if (extra > 0f) _pose.z = Mathf.Lerp(_pose.z, desiredPos.z, 1f - Mathf.Exp(-followLerp * extra * feelDt));
            }
            Quaternion rot = Quaternion.Slerp(transform.rotation, desiredRot, 1f - Mathf.Exp(-lookLerp * feelDt));

            Vector3 shake = Vector3.zero;
            if (_shake > 0.001f)
            {
                float t = Time.time * 28f;
                Vector3 offset = new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, Mathf.PerlinNoise(t, t) - 0.5f);
                shake += offset * (_shake * shakeAmount * scale);
                _shake *= Mathf.Exp(-shakeDecay * feelDt);
            }

            // Top-speed motion: slow (6.5 Hz) and only sideways/up in view space, so it reads as the island
            // working against the water and never as a handheld camera.
            float rumble = speedFeelEnabled ? SpeedFeel.ShakeAmount(_framing, speedShakeStart) * speedShake : 0f;
            if (rumble > 1e-4f)
            {
                float t = Time.time * 6.5f;
                Vector3 o = new Vector3(Mathf.PerlinNoise(t, 3.7f) - 0.5f, (Mathf.PerlinNoise(5.1f, t) - 0.5f) * 0.6f, 0f);
                shake += rot * o * (rumble * scale);
            }
            float sway = DodgePunch * dodgeSway * _dodgeSide;
            if (Mathf.Abs(sway) > 1e-4f) shake += rot * new Vector3(sway * scale, 0f, 0f);
            transform.SetPositionAndRotation(_pose + shake, rot);
            ReportView();
        }

        // The vegetation calms its wind by how close the view is; the chase camera knows its own distance
        // exactly. Only while it really drives: suspended, the watch tools (or the fly-over) report their own.
        void ReportView()
        {
            if (target != null)
                Drift.Life.IslandLifeSystem.ReportViewDistance(Vector3.Distance(transform.position, target.transform.position));
        }

        void ApplyNearClip(float close)
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            if (_cam != null) _cam.nearClipPlane = Mathf.Lerp(nearClipFar, nearClipClose, close);
        }

        void DesiredPose(out Vector3 pos, out Quaternion rot, out float scale, out Vector3 focus)
        {
            Island ground = target;
            focus = target.transform.position;
            float radius = target.BoundingRadius;
            float close = CloseBlend(_zoom);
            if (_followPos != null)
            {
                focus = _followPos();
                radius = _followRadius;
                if (_followGround != null) ground = _followGround;
            }
            // Close up the island surface, not sea level, is what gets centred (a follow focus already sits on it).
            else if (close > 0f)
                focus.y += Mathf.Max(0f, target.SampleHeight(Vector2.zero)) * close;

            // With the direct-direction steering the view is nailed to a compass direction: the island drifts
            // any way it likes and never turns, so there is no "behind it" to sit in - and a view that turned
            // would turn the frame the steering is given in and make a held direction curve for ever.
            // Otherwise back = -heading: the body may turn under the camera (merges, drift), the view never does.
            Vector3 back = ViewIsFixed ? -ViewForward : -ground.Forward;
            var ring = _followPos == null ? RingWorld.Active : null;
            if (ring != null)
            {
                // On the ring the camera looks mostly along the track: across it, it would sit outside the band in
                // the rim walls and see the climbing ring rolled sideways.
                float along = back.z >= 0f ? 1f : -1f;
                back = Vector3.Slerp(back, new Vector3(0f, back.y, along), ringAlongBias).normalized;
            }
            // Speed framing: the camera drops back and lifts a little while the island really runs (on the ring it
            // moves in instead, see RingDolly), and the merge kick dollies it in for a moment. Both leave the ring
            // clamp and the terrain clamp below alone. The idle life zoom closes in on a resting big island.
            float kick = (1f - mergeZoomIn * _mergeKick) * _lifeZoom;
            float dist, high;
            if (ring != null)
            {
                float dolly = SpeedFeel.RingDolly(_framing, ringSpeedDolly);
                dist = distanceBehind * dolly * kick;
                high = height * dolly * kick;
                // The whale's ghost ride: up and a little back, looking down on the island gliding through.
                float lift = Mathf.SmoothStep(0f, 1f, _ghostFeel);
                high += ringGhostLift * lift;
                dist *= 1f + 0.12f * lift;
            }
            else
            {
                dist = distanceBehind * (1f + speedPullBack * _framing) * kick;
                high = height * (1f + 0.45f * speedPullBack * _framing) * kick;
            }
            float framed = SpeedFeel.SoftRadius(radius, framingKneeRadius, framingSoftExponent);
            FollowPose(focus, ground.Normal, back, framed, referenceRadius, zoomExponent, high, dist, _zoom, out pos, out rot, out scale);
            if (ring != null)
            {
                var g = ring.Geometry;
                float x = Mathf.Clamp(pos.x, g.MinX + ringWallMargin, g.MaxX - ringWallMargin);
                if (x != pos.x)
                {
                    pos.x = x;
                    rot = Quaternion.LookRotation((focus - pos).normalized, ground.Normal);
                }
            }

            Vector3 look = focus + ground.Normal * (0.4f * _zoom);
            float need = RequiredHeight(ground, pos, look, Mathf.Lerp(groundClearance, closeGroundClearance, close), close);
            if (pos.y < need)
            {
                pos.y = need;
                rot = Quaternion.LookRotation((look - pos).normalized, ground.Normal);
            }
            // On the adventure ring the view tilts up so the band climbing into the sky ahead is in the picture
            // (portrait phones otherwise see only sea). Part of the pose, so tap picking and labels stay right.
            if (_followPos == null && RingWorld.Active != null) rot *= Quaternion.Euler(-ringPitchUp * (1f - close), 0f, 0f);

            // Lean into the turn. Last, because the clamps above rebuild the rotation from scratch.
            if (speedFeelEnabled && turnRoll > 0f && _followPos == null)
            {
                float roll = SpeedFeel.Bank(_turnRateDeg, Mathf.Max(1f, target.turnRateDegPerSec), _framing, turnRoll) * (1f - close);
                if (Mathf.Abs(roll) > 0.01f) rot *= Quaternion.Euler(0f, 0f, roll);
            }
        }

        // The lowest camera height at pos that clears ground's terrain (float.MinValue over open water).
        // Sample 0 keeps the camera above the terrain under it; close up two more samples keep the sight
        // line to the look point clear of a rise in between. Shared with the orbit camera of the watch tools.
        public static float RequiredHeight(Island ground, Vector3 pos, Vector3 look, float clearance, float close)
        {
            float baseY = ground.transform.position.y;
            float need = float.MinValue;
            int samples = close > 0f ? 3 : 1;
            for (int s = 0; s < samples; s++)
            {
                float t = s * 0.3f;
                Vector3 p = Vector3.Lerp(pos, look, t);
                float h = ground.SampleHeight(ground.ToLocal(new Vector2(p.x, p.z)));
                if (h <= -0.2f) continue;
                float top = baseY + h + (s == 0 ? clearance : clearance * 0.5f);
                float req = (top - look.y * t) / (1f - t);
                if (s > 0) req = Mathf.Lerp(pos.y, req, close);
                need = Mathf.Max(need, req);
            }
            return need;
        }

        public const float CloseZoomStart = 0.4f;
        public const float CloseZoomEnd = 0.08f;

        // 0 at zoom >= CloseZoomStart (the classic framing), 1 at CloseZoomEnd: the close-up shaping.
        public static float CloseBlend(float zoom) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CloseZoomEnd, CloseZoomStart, zoom));

        // The chase pose without the terrain clamp: behind-and-above the focus, framing scaled by the
        // radius against referenceRadius and by the zoom (closer views drop the pitch). Below
        // CloseZoomStart the island-size scale fades to 1 (a close-up is equally close on a continent) and
        // height and distance shrink by up to half, ending ~0.8 u from the focus at a ~26 degree pitch.
        public static void FollowPose(Vector3 focus, Vector3 up, Vector3 back, float radius, float referenceRadius, float zoomExponent,
            float height, float distanceBehind, float zoom, out Vector3 pos, out Quaternion rot, out float scale)
        {
            float close = CloseBlend(zoom);
            scale = Mathf.Lerp(FramingScale(radius, referenceRadius, zoomExponent), 1f, close);
            float shrink = Mathf.Lerp(1f, 0.5f, close);
            Vector3 lookAt = focus + up * (0.4f * zoom);
            pos = focus + up * (height * scale * zoom * shrink) + back * (distanceBehind * scale * Mathf.Pow(zoom, 0.65f) * shrink);
            rot = Quaternion.LookRotation((lookAt - pos).normalized, up);
        }

        // Jumps to the follow pose without smoothing (after a load or a world reset).
        public void SnapToTarget()
        {
            if (target == null) return;
            _feel.Reset();
            _framing = 0f;
            _mergeKick = 0f;
            ResetKicks();
            _hasHeading = false;
            _turnRateDeg = 0f;
            // A snap happens when a world is (re)started or loaded: with the island at rest there is no course
            // to keep, so the view goes back to its compass direction. Under way (a hand-back from photo mode)
            // it keeps the course it had.
            if (!CourseFollowActive || !CourseYaw(target.SelfVelocity, courseMinSpeed, out _)) _viewYaw = _steerYaw = viewYawDeg;
            ApplyFov();
            DesiredPose(out Vector3 pos, out Quaternion rot, out _, out Vector3 focus);
            transform.position = pos;
            transform.rotation = rot;
            _pose = pos;
            _hasPose = true;
            _lastFocus = focus;
            _hasLastFocus = true;
            ApplyNearClip(CloseBlend(_zoom));
            _shake = 0f;
            _easeT = 1f;
        }
    }
}
