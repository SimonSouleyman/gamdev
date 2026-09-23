using System;
using System.Collections.Generic;
using System.Globalization;
using Drift.Core;
using Drift.Islands;
using Drift.SaveSystem;
using Drift.UI;
using Drift.Visuals;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Drift.Bridge
{
    // The end of a cozy run, in two steps. Once every island of the world is part of the player's Pangäa, a small
    // banner says so and the run simply keeps going (GameSession.PangaeaReached): the owner can drive on, watch the
    // animals, take photos and open the journal for as long as they like, and nothing can end the run meanwhile.
    // Only the banner's "Weiter" completes it (GameSession.CompleteRun); then the camera lifts off into space until
    // the curved world reads as a globe, orbits it slowly, takes the run's picture for the run journal and shows
    // the "Pangäa vollendet!" screen. Leaving to the title and continuing later confirms the finished world again,
    // so the banner is simply back.
    // The globe is nothing but CurvedWorld's bend with a small radius seen from far away: the finale only drives
    // public fields (minRadius, haze, skyGradient) and, on top of what CurvedWorld pushes, the sky's zenith colour
    // and star visibility, so the sky above the limb turns into night space with a thin atmosphere rim.
    // Everything it changes is restored when the session leaves RunComplete (new game, title).
    [ExecuteAlways]
    [DefaultExecutionOrder(200)]
    public class PangaeaFinale : MonoBehaviour
    {
        public GameSession session;
        public WorldStreamer streamer;
        public SessionScreens screens;
        public IslandChaseCamera chaseCamera;
        public CurvedWorld curvedWorld;
        public DayNightCycle dayNight;
        public SaveManager saveManager;
        public Island player;

        [Header("Erkennung")]
        [Tooltip("So lange (Sekunden) müssen alle Inseln verschmolzen sein, bevor der Durchgang als vollendet gilt.")]
        public float confirmSeconds = 1.5f;
        [Tooltip("Wie oft (Sekunden) der Fortschritt der Welt geprüft wird.")]
        public float checkInterval = 0.25f;

        [Header("Hinweis „Pangäa vollendet“")]
        [Tooltip("Überschrift des kleinen Hinweises, der nach der letzten Insel erscheint.")]
        public string bannerTitle = "Pangäa vollendet!";
        [Tooltip("Zweite Zeile des Hinweises: sie sagt, wie der Rundflug über die fertige Insel gesteuert wird.")]
        public string bannerBody = DefaultBannerBody;
        [Tooltip("Beschriftung des kleinen Knopfes, der den Flug ins All startet.")]
        public string bannerButton = "Weiter";
        [Tooltip("Abstand des Hinweises vom oberen Bildrand.")]
        public float bannerTop = 520f;
        [Tooltip("Vorschau des Hinweises im Editor ohne Play Mode.")]
        public bool editorBanner;

        [Header("Über die Insel fliegen")]
        [Tooltip("Nach der letzten Insel steht die Pangäa still und du fliegst mit der Kamera über sie. Aus = die Kamera bleibt wie bisher an der Insel.")]
        public bool flyOverEnabled = true;
        public FlyOverCamera.Settings flyOver = new FlyOverCamera.Settings();
        [Tooltip("Wie sanft die Kamera zur Insel zurückblendet, wenn der Rundflug endet (Sekunden).")]
        [Range(0f, 2f)] public float flyReturnSeconds = 0.7f;

        [Header("Flug ins All")]
        [Tooltip("Dauer des Aufstiegs von der Verfolgerkamera bis zur Weltkugel (Sekunden).")]
        public float liftSeconds = 6f;
        [Tooltip("Langsamer Umlauf um die Weltkugel, bevor der Abschlussbildschirm erscheint (Sekunden).")]
        public float orbitSeconds = 4f;
        [Tooltip("Nach so vielen Sekunden Umlauf wird das Bild für das Durchgangs-Tagebuch aufgenommen.")]
        public float captureAfter = 1.5f;
        [Tooltip("Umlaufgeschwindigkeit der Kamera um die Weltkugel (Grad pro Sekunde).")]
        public float orbitDegreesPerSecond = 6f;
        [Tooltip("Radius der Weltkugel im Verhältnis zum Radius der Pangäa.")]
        public float globeScale = 1.9f;
        public float minGlobeRadius = 60f;
        public float maxGlobeRadius = 260f;
        [Tooltip("Anteil des halben Bildwinkels, den die Weltkugel am Bildschirm einnimmt.")]
        [Range(0.3f, 1.6f)] public float globeFill = 1.15f;
        [Tooltip("Neigung der Kamera gegen die Senkrechte über der Pangäa (Grad).")]
        [Range(0f, 30f)] public float finalTilt = 9f;
        [Tooltip("Bildwinkel der Kamera im All.")]
        public float fieldOfView = 50f;

        [Header("Weltraum")]
        public Color spaceColor = new Color(0.02f, 0.03f, 0.07f);
        [Range(0f, 3f)] public float spaceStars = 1.3f;
        [Range(0f, 1f)] public float spaceHaze = 0.55f;
        [Tooltip("Steilheit des Himmelsverlaufs im All: größer = dünnerer Atmosphärensaum.")]
        public float spaceSkyGradient = 90f;
        [Tooltip("Dreht die Tageszeit während des Flugs sanft auf helles Licht, damit die Pangäa gut zu sehen ist.")]
        public bool brightenDay = true;
        [Range(0f, 1f)] public float finaleTimeOfDay = 0.45f;
        [Tooltip("Wolkendecke im All (0..1, -1 = Wolken unverändert lassen).")]
        [Range(-1f, 1f)] public float spaceCloudCover = 0.1f;
        [Tooltip("Höchste Wolkenhöhe im All, damit die Wolken auf der kleinen Weltkugel nicht abheben.")]
        public float spaceCloudHeight = 4f;

        [Header("Bild und Anzeige")]
        [Tooltip("Breite des Bildes für das Durchgangs-Tagebuch (Querformat 16:9).")]
        public int pictureWidth = 1024;
        [Tooltip("Füllgrad der Weltkugel in der Bildhöhe.")]
        [Range(0.4f, 1.4f)] public float pictureFill = 0.95f;
        public int sortingOrder = 20;
        public bool editorPreview;

        enum Phase { Idle, Lift, Orbit, Done }

        const string CanvasName = "PangaeaFinaleCanvas";
        static readonly int ZenithId = Shader.PropertyToID("_CurveSkyZenith");
        static readonly int StarsId = Shader.PropertyToID("_SkyStars");

        Phase _phase = Phase.Idle;
        float _held, _checkTimer, _phaseTime, _yaw, _skyWeight;
        bool _captured, _subscribed;
        GameSession _subscribedTo;
        float _globeRadius, _startDist, _endDist, _startTilt;
        Vector3 _startLook;
        Texture2D _picture;
        RunRecord _record;
        DateTime? _runStart;

        // Saved state, restored by End().
        bool _saved, _chaseWasEnabled;
        float _minRadius, _viewFar, _haze, _skyGradient, _fov, _near, _far, _cloudCover;
        Vector2 _cloudHeight;
        CloudShadows _clouds;
        Camera _cam;

        readonly FlyOverCamera _fly = new FlyOverCamera();
        bool _flying, _hasPinned;
        Vector2 _pinned;

        Canvas _canvas;
        GameObject _blocker, _screen;
        RawImage _pictureView;
        Text _statsText, _seedText, _nameText;
        RectTransform _banner;
        CanvasGroup _bannerGroup;
        Text _bannerTitle, _bannerBody;
        float _bannerAlpha;

        public bool Active => _phase != Phase.Idle;
        // True while the player is flying the camera over their finished island.
        public bool FlyingOver => _flying;
        public FlyOverCamera FlyCamera => _fly;
        // True while the free-look banner is on screen (and readable): the run is finished but still running.
        public bool BannerVisible => _banner != null && _banner.gameObject.activeSelf && _bannerAlpha > 0.5f;
        public Texture2D Picture => _picture;
        public RunRecord Record => _record;

        // ---------------------------------------------------------------- completion rule (pure)

        // A cozy run is complete when the world has islands at all and none of them is left unmerged, and only
        // while a game is actually being played.
        public static bool AllMerged(bool cozy, bool playing, int worldIslands, int islandsLeft) =>
            cozy && playing && worldIslands > 0 && islandsLeft <= 0;

        // The free-look banner is up while the finished Pangäa is being looked at: not during a pause, not while
        // the watch tools own the screen (photo mode, following a herd) and not once the flight has started.
        public static bool ShowBanner(bool pangaeaReached, bool playing, bool watching, bool flightActive) =>
            pangaeaReached && playing && !watching && !flightActive;

        // The fly-over runs from the last merge until the flight starts. A pause keeps it (the camera simply
        // stands still), the watch tools take the camera off it for as long as they need it, and the finale
        // takes it over where it stands.
        public static bool FlyOverActive(bool pangaeaReached, bool inGame, bool watching, bool flightActive, bool enabled) =>
            enabled && pangaeaReached && inGame && !watching && !flightActive;

        // The condition has to hold for `seconds` in a row (a streaming hiccup or a reload in between resets it).
        public static bool Confirm(bool allMerged, ref float held, float dt, float seconds)
        {
            if (!allMerged) { held = 0f; return false; }
            held += Mathf.Max(0f, dt);
            return held >= seconds;
        }

        // Globe radius for a Pangäa of the given bounding radius.
        public static float GlobeRadius(float pangaeaRadius, float scale, float min, float max) =>
            Mathf.Clamp(Mathf.Max(0f, pangaeaRadius) * scale, min, Mathf.Max(min, max));

        // Distance from the tangent point (the Pangäa) to a camera tilted by tiltDeg from the vertical, so the
        // sphere of radius r (centre r below the tangent point) spans angularRadiusDeg.
        public static float OrbitDistance(float r, float tiltDeg, float angularRadiusDeg)
        {
            float a = Mathf.Clamp(angularRadiusDeg, 1f, 89f) * Mathf.Deg2Rad;
            float d = r / Mathf.Sin(a);
            float c = Mathf.Cos(Mathf.Clamp(tiltDeg, 0f, 89f) * Mathf.Deg2Rad);
            return Mathf.Max(1f, -r * c + Mathf.Sqrt(Mathf.Max(0f, r * r * c * c - r * r + d * d)));
        }

        // Camera pose around the look point: yaw about the vertical, tilt from the vertical, distance along the ray.
        public static void OrbitPose(Vector3 look, float yawDeg, float tiltDeg, float dist, out Vector3 pos, out Quaternion rot)
        {
            float yaw = yawDeg * Mathf.Deg2Rad, tilt = Mathf.Clamp(tiltDeg, 0f, 89f) * Mathf.Deg2Rad;
            var outward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            pos = look + (outward * Mathf.Sin(tilt) + Vector3.up * Mathf.Cos(tilt)) * dist;
            rot = Quaternion.LookRotation((look - pos).normalized, -outward);
        }

        public static string FormatStats(RunRecord r)
        {
            if (r == null) return "";
            int secs = Mathf.FloorToInt(Mathf.Max(0f, r.playSeconds));
            string time = secs >= 3600 ? $"{secs / 3600}:{secs / 60 % 60:00}:{secs % 60:00}" : $"{secs / 60}:{secs % 60:00}";
            return $"Spielzeit   {time}\n" +
                   $"Inseln vereint   {r.islandsMerged}\n" +
                   $"Landmasse   {r.landArea.ToString("F0", CultureInfo.InvariantCulture)}\n" +
                   $"Arten gesehen   {r.speciesSeen}\n" +
                   $"Fotos   {r.photos}";
        }

        // ---------------------------------------------------------------- lifecycle

        void OnEnable()
        {
            Build();
            _phase = Phase.Idle;
            _held = 0f;
            if (Application.isPlaying) Resolve();
            RenderPipelineManager.beginContextRendering -= OnBeginContext;
            RenderPipelineManager.beginContextRendering += OnBeginContext;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContext;
            EndFly(true);
            if (Active) End();
            Unsubscribe();
            UiStyle.DestroyChildrenNamed(transform, CanvasName);
            _canvas = null;
        }

        void Resolve()
        {
            if (session == null) session = FindAnyObjectByType<GameSession>();
            if (session != null)
            {
                if (player == null) player = session.player;
                if (streamer == null) streamer = session.streamer;
                if (saveManager == null) saveManager = session.saveManager;
                if (chaseCamera == null) chaseCamera = session.chaseCamera;
            }
            if (player == null)
                foreach (var i in Island.All) if (i != null && i.useKeyboardInput) { player = i; break; }
            if (streamer == null) streamer = FindAnyObjectByType<WorldStreamer>();
            if (screens == null) screens = FindAnyObjectByType<SessionScreens>();
            if (chaseCamera == null) chaseCamera = FindAnyObjectByType<IslandChaseCamera>();
            if (curvedWorld == null) curvedWorld = CurvedWorld.Active != null ? CurvedWorld.Active : FindAnyObjectByType<CurvedWorld>();
            if (dayNight == null && curvedWorld != null) dayNight = curvedWorld.dayNight;
            if (saveManager == null) saveManager = FindAnyObjectByType<SaveManager>();
            Subscribe();
        }

        void Subscribe()
        {
            if (_subscribed && _subscribedTo == session) return;
            Unsubscribe();
            if (session == null) return;
            session.RunCompleted += OnRunCompleted;
            session.StateChanged += OnStateChanged;
            _subscribedTo = session;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (_subscribed && _subscribedTo != null)
            {
                _subscribedTo.RunCompleted -= OnRunCompleted;
                _subscribedTo.StateChanged -= OnStateChanged;
            }
            _subscribed = false;
            _subscribedTo = null;
        }

        void OnRunCompleted() => Begin();

        void OnStateChanged(GameSession.State s)
        {
            if (s == GameSession.State.Playing && session != null)
            {
                // A fresh run starts its clock now; a continued one counts back over the time it was already played.
                if (session.Stats.timeSurvived < 1f || !_runStart.HasValue)
                    _runStart = DateTime.Now - TimeSpan.FromSeconds(Mathf.Max(0f, session.Stats.timeSurvived));
            }
            if (s == GameSession.State.Title) _runStart = null;
            if (s != GameSession.State.RunComplete && Active) End();
        }

        void Update()
        {
            if (!Application.isPlaying)
            {
                ShowScreen(editorPreview, editorPreview);
                ApplyBannerTexts();
                SetBanner(editorBanner, 1f);
                if (editorPreview && _record == null) FillScreen(SampleRecord(), _picture);
                return;
            }
            Resolve();
            if (session == null) return;

            StepBanner(Time.unscaledDeltaTime);
            StepFreeLook();
            if (!Active)
            {
                if (session.IsRunComplete) { Begin(); return; }
                CheckCompletion(Time.unscaledDeltaTime);
            }
        }

        // ---------------------------------------------------------------- the fly-over over the finished island

        void StepFreeLook()
        {
            bool watching = session.WatchInputHold;
            bool want = FlyOverActive(session.PangaeaReached, session.Current == GameSession.State.Playing || session.IsPaused,
                watching, Active, flyOverEnabled) && chaseCamera != null && player != null && Camera.main != null;
            if (!want)
            {
                // Yielding to photo mode or a followed herd leaves the camera to them - they suspended it
                // themselves and hand it back when they are done, and the fly-over simply takes it again.
                if (_flying) EndFly(!watching);
                return;
            }
            if (!_flying) BeginFly();
            HoldIsland();
        }

        // The Pangäa stands still while it is being looked at: no steering (GameSession locks it) and no drift
        // either, so no plate current carries the island out from under the camera.
        void HoldIsland()
        {
            if (player == null) return;
            if (!_hasPinned)
            {
                _pinned = player.PlanarPosition;
                _hasPinned = true;
            }
            player.SetSelfVelocity(Vector2.zero);
            if ((player.PlanarPosition - _pinned).sqrMagnitude > 1e-8f) player.SetPlanarPosition(_pinned);
            player.StopDrift();
        }

        void BeginFly()
        {
            var cam = Camera.main;
            _flying = true;
            _hasPinned = false;
            _fly.settings = flyOver;
            _fly.Reset(cam.transform.position, cam.transform.rotation, player.transform.position.y);
            chaseCamera.Suspended = true;
        }

        void EndFly(bool handBack)
        {
            if (!_flying) return;
            _flying = false;
            _hasPinned = false;
            if (!handBack || chaseCamera == null) return;
            if (Application.isPlaying) chaseCamera.ResumeEased(flyReturnSeconds);
            else
            {
                chaseCamera.Suspended = false;
                chaseCamera.SnapToTarget();
            }
        }

        void DriveFly()
        {
            var cam = Camera.main;
            if (cam == null || player == null) { EndFly(true); return; }
            bool running = session != null && session.Current == GameSession.State.Playing;
            var input = running && screens != null ? screens.ReadFlyInput() : default;
            _fly.Step(input, running ? Time.unscaledDeltaTime : 0f, player);
            cam.transform.SetPositionAndRotation(_fly.Position, _fly.Rotation);
            // The chase camera is suspended, so the vegetation would have to guess how close the view is.
            Drift.Life.IslandLifeSystem.ReportViewDistance(Vector3.Distance(_fly.Position, _fly.LookPoint));
        }

        void StepBanner(float dt)
        {
            bool show = ShowBanner(session.PangaeaReached, session.Current == GameSession.State.Playing, session.WatchInputHold, Active);
            if (!show && _bannerAlpha <= 0f) return;
            _bannerAlpha = Mathf.MoveTowards(_bannerAlpha, show ? 1f : 0f, Mathf.Max(0f, dt) / 0.3f);
            SetBanner(_bannerAlpha > 0.001f, _bannerAlpha);
        }

        void CheckCompletion(float dt)
        {
            bool cozy = GameModes.Current == GameMode.Cozy;
            bool playing = session.Current == GameSession.State.Playing;
            if (!cozy || !playing || streamer == null || session.PangaeaReached) { _held = 0f; _checkTimer = 0f; return; }
            _checkTimer += dt;
            if (_checkTimer < checkInterval) return;
            float step = _checkTimer;
            _checkTimer = 0f;
            int world = streamer.WorldSlots().Count;
            streamer.Progress(out _, out int left);
            if (Confirm(AllMerged(cozy, playing, world, left), ref _held, step, confirmSeconds))
            {
                _held = 0f;
                // Not the finale yet: the run keeps running and the banner offers "Weiter".
                session.PangaeaReached = true;
                TildaVoice.Say("pangaea_ready", VoicePriority.Queue);
            }
        }

        // ---------------------------------------------------------------- the flight

        void Begin()
        {
            if (Active) return;
            Resolve();
            _cam = Camera.main;
            if (_cam == null || player == null) return;
            // The flight lifts off from wherever the fly-over left the camera, so it hands nothing back - but the
            // chase camera must not stay suspended, or it would never follow again after the finale.
            _flying = false;
            _hasPinned = false;
            if (chaseCamera != null) chaseCamera.Suspended = false;
            _record = MakeRecord();
            _captured = false;
            DestroyPicture();
            _bannerAlpha = 0f;
            SetBanner(false, 0f);

            // Disable first: the chase camera widens the field of view with speed, and the saved state must be the
            // resting one, not a speed-widened frame.
            if (chaseCamera != null) chaseCamera.enabled = false;
            SaveState();

            StartFlight(_cam);
            _phase = Phase.Lift;
            _phaseTime = 0f;
            ShowScreen(true, false);
        }

        // The flight starts exactly in the current pose: an orbit around the point the camera looks at, at the
        // Pangäa's distance; that point then slides onto the Pangäa.
        void StartFlight(Camera cam)
        {
            Transform ct = cam.transform;
            _startDist = Mathf.Max(1f, Vector3.Distance(ct.position, PangaeaPoint()));
            _startLook = ct.position + ct.forward * _startDist;
            Vector3 off = ct.position - _startLook;
            _yaw = Mathf.Atan2(off.x, off.z) * Mathf.Rad2Deg;
            _startTilt = Mathf.Acos(Mathf.Clamp(off.y / _startDist, -1f, 1f)) * Mathf.Rad2Deg;
            _globeRadius = GlobeRadius(player.BoundingRadius, globeScale, minGlobeRadius, maxGlobeRadius);
            _endDist = OrbitDistance(_globeRadius, finalTilt, ScreenGlobeAngle(cam));
        }

        // s = eased progress of the lift (0 = start pose, 1 = orbit around the globe).
        void FlightPose(float s, float yaw, out Vector3 pos, out Quaternion rot, out float dist)
        {
            dist = Mathf.Exp(Mathf.Lerp(Mathf.Log(_startDist), Mathf.Log(_endDist), s));
            float tilt = Mathf.Lerp(_startTilt, finalTilt, s);
            Vector3 target = Vector3.Lerp(_startLook, PangaeaPoint(), Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s * 2.5f)));
            OrbitPose(target, yaw, tilt, dist, out pos, out rot);
        }

        void End()
        {
            _phase = Phase.Idle;
            _skyWeight = 0f;
            RestoreState();
            ShowScreen(false, false);
            SetBanner(false, 0f);
            DestroyPicture();
            _record = null;
            _held = 0f;
        }

        void LateUpdate()
        {
            if (!Application.isPlaying) return;
            if (_flying && !Active) DriveFly();
            if (!Active || _phase == Phase.Idle) return;
            if (_cam == null || player == null) { End(); return; }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _phaseTime += dt;

            float s = 1f;
            if (_phase == Phase.Lift)
            {
                s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_phaseTime / Mathf.Max(0.1f, liftSeconds)));
                if (_phaseTime >= liftSeconds) { _phase = Phase.Orbit; _phaseTime = 0f; }
            }
            _yaw += orbitDegreesPerSecond * s * dt;
            FlightPose(s, _yaw, out Vector3 pos, out Quaternion rot, out float dist);
            _cam.transform.SetPositionAndRotation(pos, rot);
            ApplySpace(s, dist);

            if (brightenDay && dayNight != null)
            {
                // Always forwards (a short time-lapse through the night), never back through the evening.
                float ahead = Mathf.Repeat(finaleTimeOfDay - dayNight.TimeOfDay, 1f);
                if (ahead > 1e-3f && ahead < 0.999f) dayNight.TimeOfDay = dayNight.TimeOfDay + ahead * (1f - Mathf.Exp(-1.2f * dt));
            }

            if (_phase == Phase.Orbit)
            {
                if (!_captured && _phaseTime >= captureAfter) CaptureAndStore();
                if (_phaseTime >= orbitSeconds)
                {
                    if (!_captured) CaptureAndStore();
                    _phase = Phase.Done;
                    FillScreen(_record, _picture);
                    ShowScreen(true, true);
                }
            }
        }

        Vector3 PangaeaPoint()
        {
            Vector2 p = player.PlanarPosition;
            return new Vector3(p.x, 0f, p.y);
        }

        // Half the smaller of the two view angles, times globeFill: the globe fits portrait and landscape screens.
        float ScreenGlobeAngle(Camera cam)
        {
            float v = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            float h = v * Mathf.Max(0.1f, cam.aspect);
            return Mathf.Atan(Mathf.Min(v, h)) * Mathf.Rad2Deg * globeFill;
        }

        void ApplySpace(float s, float dist)
        {
            if (curvedWorld != null)
            {
                curvedWorld.minRadius = Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(1f, _minRadius)), Mathf.Log(_globeRadius), s));
                // minRadius only holds beyond viewFar: a small globe is seen from closer than the default 120 u.
                curvedWorld.viewFar = Mathf.Lerp(_viewFar, Mathf.Min(_viewFar, Mathf.Max(curvedWorld.viewNear + 1f, _endDist * 0.8f)), s);
                curvedWorld.haze = Mathf.Lerp(_haze, spaceHaze, s);
                curvedWorld.skyGradient = Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(0.1f, _skyGradient)), Mathf.Log(Mathf.Max(0.1f, spaceSkyGradient)), Smooth(0.3f, 1f, s)));
            }
            if (_clouds != null && spaceCloudCover >= 0f)
            {
                _clouds.coverOverride = s > 0.3f ? spaceCloudCover : _cloudCover;
                float top = Mathf.Lerp(_cloudHeight.y, Mathf.Min(_cloudHeight.y, spaceCloudHeight), s);
                _clouds.heightRange = new Vector2(Mathf.Min(_cloudHeight.x, top), top);
            }
            _skyWeight = Smooth(0.2f, 0.85f, s);
            _cam.fieldOfView = Mathf.Lerp(_fov, fieldOfView, s);
            _cam.nearClipPlane = Mathf.Max(_near, Mathf.Lerp(_near, dist * 0.02f, s));
            _cam.farClipPlane = Mathf.Max(_far, (dist + 3f * _globeRadius) * 1.3f);
        }

        static float Smooth(float from, float to, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, x));

        void SaveState()
        {
            _saved = true;
            _clouds = CloudShadows.Instance;
            if (_clouds != null)
            {
                _cloudCover = _clouds.coverOverride;
                _cloudHeight = _clouds.heightRange;
            }
            _chaseWasEnabled = chaseCamera == null || chaseCamera.enabled;
            _fov = _cam.fieldOfView;
            _near = _cam.nearClipPlane;
            _far = _cam.farClipPlane;
            if (curvedWorld != null)
            {
                _minRadius = curvedWorld.minRadius;
                _viewFar = curvedWorld.viewFar;
                _haze = curvedWorld.haze;
                _skyGradient = curvedWorld.skyGradient;
            }
        }

        void RestoreState(bool camera = true)
        {
            if (!_saved) return;
            _saved = false;
            if (_cam != null)
            {
                _cam.fieldOfView = _fov;
                _cam.nearClipPlane = _near;
                _cam.farClipPlane = _far;
            }
            if (curvedWorld != null)
            {
                curvedWorld.minRadius = _minRadius;
                curvedWorld.viewFar = _viewFar;
                curvedWorld.haze = _haze;
                curvedWorld.skyGradient = _skyGradient;
            }
            if (_clouds != null && spaceCloudCover >= 0f)
            {
                _clouds.coverOverride = _cloudCover;
                _clouds.heightRange = _cloudHeight;
            }
            if (camera && chaseCamera != null)
            {
                if (_chaseWasEnabled) chaseCamera.enabled = true;
                if (chaseCamera.target == null) chaseCamera.target = player;
                chaseCamera.SnapToTarget();
            }
        }

        // After CurvedWorld pushed this frame's palette (LateUpdate), before any camera renders: the zenith sinks
        // into space and the stars come out, whatever the time of day. CurvedWorld sets both only in Refresh, so
        // they survive its per-camera callback.
        void OnBeginContext(ScriptableRenderContext ctx, List<Camera> cams) => PushSky();

        void PushSky()
        {
            if (_skyWeight <= 0f) return;
            Vector4 z = Shader.GetGlobalVector(ZenithId);
            Color space = spaceColor.linear;
            Shader.SetGlobalVector(ZenithId, Vector4.Lerp(z, new Vector4(space.r, space.g, space.b, z.w), _skyWeight));
            Vector4 st = Shader.GetGlobalVector(StarsId);
            st.x = Mathf.Lerp(st.x, spaceStars, _skyWeight);
            Shader.SetGlobalVector(StarsId, st);
        }

        // ---------------------------------------------------------------- picture and record

        RunRecord MakeRecord()
        {
            var st = session != null ? session.Stats : null;
            float play = st != null ? st.timeSurvived : 0f;
            var journal = saveManager != null ? saveManager.Journal : null;
            return new RunRecord
            {
                date = DateTime.Now.ToString("s", CultureInfo.InvariantCulture),
                seed = session != null ? session.WorldSeed : 0,
                playSeconds = play,
                islandsMerged = st != null ? st.islandsAbsorbed : 0,
                landArea = player != null ? player.LandArea : 0f,
                speciesSeen = journal != null ? journal.RunSeenCount : 0,
                photos = CountPhotos(_runStart ?? DateTime.Now - TimeSpan.FromSeconds(play)),
                image = "",
                name = IslandNames.ForSeed(session != null && session.WorldSeed != 0 ? session.WorldSeed : (int)(DateTime.Now.Ticks % int.MaxValue)),
            };
        }

        static int CountPhotos(DateTime since)
        {
            try
            {
                var list = new List<PhotoEntry>();
                PhotoLibrary.List(PhotoLibrary.DefaultDirectory, list);
                int n = 0;
                for (int i = 0; i < list.Count; i++) if (list[i].taken >= since.AddSeconds(-2)) n++;
                return n;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        void CaptureAndStore()
        {
            _captured = true;
            DestroyPicture();
            _picture = RenderPicture(_cam, pictureWidth, PictureHeight(pictureWidth), PictureFov());
            RunJournal.Add(_record, _picture);
            // Stored: a restart from here must not offer the finished world again (and store it twice).
            if (saveManager != null) saveManager.DeleteSaveFile();
        }

        static int PictureHeight(int width) => Mathf.Max(36, Mathf.RoundToInt(width * 9f / 16f));

        float PictureFov()
        {
            float a = Mathf.Asin(Mathf.Clamp01(_globeRadius / Mathf.Max(_globeRadius + 1f, GlobeCentreDistance(_endDist))));
            return Mathf.Clamp(2f * Mathf.Atan(Mathf.Tan(a) / Mathf.Max(0.1f, pictureFill)) * Mathf.Rad2Deg, 10f, 100f);
        }

        float GlobeCentreDistance(float dist)
        {
            float c = Mathf.Cos(finalTilt * Mathf.Deg2Rad);
            return Mathf.Sqrt(dist * dist + _globeRadius * _globeRadius + 2f * dist * _globeRadius * c);
        }

        // Renders the camera as it stands (with the bend, since it is the main camera) into a texture; fov is vertical.
        public Texture2D RenderPicture(Camera cam, int width, int height, float fov)
        {
            width = Mathf.Clamp(width, 64, 2048);
            height = Mathf.Clamp(height, 36, 2048);
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            float prevFov = cam.fieldOfView;
            var prevActive = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                cam.targetTexture = rt;
                cam.fieldOfView = fov;
                if (curvedWorld != null) curvedWorld.Refresh();
                PushSky();
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false) { name = "PangaeaPicture", hideFlags = HideFlags.DontSave };
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
            }
            finally
            {
                RenderTexture.active = prevActive;
                cam.targetTexture = prevTarget;
                cam.fieldOfView = prevFov;
                cam.ResetAspect();
                RenderTexture.ReleaseTemporary(rt);
            }
            return tex;
        }

        void DestroyPicture()
        {
            if (_pictureView != null && _pictureView.texture == _picture) _pictureView.texture = null;
            if (_picture != null)
            {
                if (Application.isPlaying) Destroy(_picture);
                else DestroyImmediate(_picture);
            }
            _picture = null;
        }

        // ---------------------------------------------------------------- edit-mode preview

        // Renders the finale's space view of the current scene without Play Mode (same camera, bend and sky
        // overrides as the orbit), optionally writes it as PNG, and puts every touched value back.
        // progress < 1 shows that moment of the lift, starting from the current camera pose (yawDeg is then ignored).
        public Texture2D PreviewSpaceView(string pngPath = null, float yawDeg = float.NaN, int size = 0, float progress = 1f)
        {
            Resolve();
            var cam = Camera.main;
            if (cam == null || player == null) { Debug.LogWarning("PangaeaFinale: no main camera or player island."); return null; }
            if (Active) return null;
            _cam = cam;
            var t = cam.transform;
            Vector3 camPos = t.position;
            Quaternion camRot = t.rotation;
            bool day = brightenDay && dayNight != null;
            float time = day ? dayNight.TimeOfDay : 0f;
            if (day)
            {
                dayNight.TimeOfDay = finaleTimeOfDay;
                dayNight.Apply();
            }
            SaveState();
            try
            {
                StartFlight(cam);
                float s = Mathf.Clamp01(progress);
                float yaw = s < 1f || float.IsNaN(yawDeg) ? _yaw : yawDeg;
                FlightPose(s, yaw, out Vector3 pos, out Quaternion rot, out float dist);
                t.SetPositionAndRotation(pos, rot);
                ApplySpace(s, dist);
                if (_clouds != null) _clouds.Step(0f);
                int w = size > 0 ? size : pictureWidth;
                var tex = RenderPicture(cam, w, PictureHeight(w), s < 1f ? cam.fieldOfView : PictureFov());
                if (tex != null && !string.IsNullOrEmpty(pngPath)) System.IO.File.WriteAllBytes(pngPath, tex.EncodeToPNG());
                if (editorPreview)
                {
                    DestroyPicture();
                    _picture = tex;
                    FillScreen(SampleRecord(), _picture);
                }
                return tex;
            }
            finally
            {
                _skyWeight = 0f;
                t.SetPositionAndRotation(camPos, camRot);
                RestoreState(false);
                if (_clouds != null) _clouds.Step(0f);
                if (day)
                {
                    dayNight.TimeOfDay = time;
                    dayNight.Apply();
                }
                if (curvedWorld != null) curvedWorld.Refresh();
            }
        }

        RunRecord SampleRecord()
        {
            int seed = session != null && !session.LegacySeeds ? session.WorldSeed : 482913;
            return new RunRecord
            {
                date = DateTime.Now.ToString("s", CultureInfo.InvariantCulture),
                seed = seed,
                playSeconds = 1086f,
                islandsMerged = 19,
                landArea = player != null ? player.LandArea : 1840f,
                speciesSeen = 9,
                photos = 4,
                name = IslandNames.ForSeed(seed),
            };
        }

        // ---------------------------------------------------------------- overlay

        const float ButtonWidth = 680f;
        static readonly Vector2 PanelSize = new Vector2(880f, 1540f);
        static readonly Vector2 BannerSize = new Vector2(880f, 188f);
        static readonly Vector2 BannerButton = new Vector2(248f, 96f);

        public RectTransform ScreenRoot => _screen != null ? (RectTransform)_screen.transform : null;
        public Canvas OverlayCanvas => _canvas;

        void Build()
        {
            UiStyle.DestroyChildrenNamed(transform, CanvasName);
            _canvas = UiStyle.Canvas(transform, CanvasName, sortingOrder, true, out var root);
            // Swallows every tap during the flight, so nothing underneath reacts while the camera is away.
            _blocker = UiStyle.Scrim(root, "InputBlocker", new Color(0f, 0f, 0f, 0f)).gameObject;

            BuildBanner(root);

            var screen = UiStyle.Scrim(root, "FinaleScreen", UiStyle.DimLight);
            var panel = UiStyle.Panel(screen, "Panel", PanelSize, true).Center(Vector2.zero, PanelSize);
            UiStyle.FadeIn(screen.gameObject, panel);
            _screen = screen.gameObject;

            UiStyle.FitWidth(Line(panel, "Pangäa vollendet!", 86, UiStyle.Sand, -26f, 116f, true), 0.5f);
            // The world's own name stands where the seed used to; the seed stays underneath in small print.
            _nameText = UiStyle.FitWidth(Line(panel, "", 54, UiStyle.Cream, -138f, 66f, true), 0.5f);
            _seedText = Line(panel, "", UiStyle.Caption, UiStyle.Muted, -204f, 38f);
            var pic = new Vector2(760f, 400f);
            _pictureView = UiStyle.Picture(panel, "Picture", pic, out var frame);
            frame.TopCenter(new Vector2(0f, -248f), pic);
            var card = UiStyle.Card(panel, "Stats", new Vector2(760f, 330f)).TopCenter(new Vector2(0f, -672f), new Vector2(760f, 330f));
            _statsText = UiStyle.Label(card, "", 40, UiStyle.Cream, TextAnchor.MiddleCenter);
            _statsText.rectTransform.Stretch(UiStyle.Gap, UiStyle.GapSmall, UiStyle.Gap, UiStyle.GapSmall);
            _statsText.lineSpacing = UiStyle.Lines(1.2f);

            var wide = new Vector2(ButtonWidth, UiStyle.ButtonHeight);
            var b = UiStyle.PrimaryButton(panel, "NewWorld", "Neue Welt", wide, OnNewWorld);
            ((RectTransform)b.transform).TopCenter(new Vector2(0f, -1036f), wide);
            var j = UiStyle.SecondaryButton(panel, "RunJournal", "Durchgangs-Tagebuch", wide, () => RunJournal.RequestOpen());
            ((RectTransform)j.transform).TopCenter(new Vector2(0f, -1196f), wide);
            UiStyle.FitWidth(UiStyle.LabelOf(j));
            var h = UiStyle.SecondaryButton(panel, "Title", "Zum Titel", wide, OnTitle);
            ((RectTransform)h.transform).TopCenter(new Vector2(0f, -1356f), wide);
            UiStyle.ButtonIcon(h, UiIcon.Home);

            _blocker.SetActive(false);
            _screen.SetActive(false);
        }

        // The free-look banner: a HUD card that floats over the world, takes no input of its own (only its little
        // "Weiter" button does) and never covers the map or the hints in the bottom corners.
        void BuildBanner(RectTransform root)
        {
            _banner = UiStyle.Rect(root, "PangaeaBanner");
            _banner.TopCenter(new Vector2(0f, -Mathf.Max(0f, bannerTop)), BannerSize);
            UiStyle.Shape(_banner, "Body", UiSprites.RoundedFor(BannerSize), UiStyle.GlassDense).rectTransform.Stretch();

            float right = BannerSize.x - 28f - BannerButton.x;
            _bannerTitle = UiStyle.FitWidth(UiStyle.Label(_banner, bannerTitle, UiStyle.Subheading, UiStyle.Sand, TextAnchor.MiddleLeft, true));
            _bannerTitle.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(36f, -62f), new Vector2(right - 60f, 56f));
            _bannerBody = UiStyle.FitWidth(UiStyle.Label(_banner, BannerBody, UiStyle.Caption, UiStyle.CreamSoft, TextAnchor.MiddleLeft));
            _bannerBody.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(36f, -124f), new Vector2(right - 60f, 44f));

            var go = UiStyle.PrimaryButton(_banner, "BannerContinue", bannerButton, BannerButton, OnBannerContinue);
            ((RectTransform)go.transform).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-28f, 0f), BannerButton);

            _bannerGroup = _banner.gameObject.AddComponent<CanvasGroup>();
            _bannerGroup.alpha = 0f;
            _banner.gameObject.SetActive(false);
        }

        void OnBannerContinue()
        {
            if (session == null) return;
            // The state change to RunComplete raises RunCompleted, which starts the flight.
            session.CompleteRun();
        }

        // The scene still carries the old "look around in peace" line, which says nothing about flying the
        // camera; like SessionScreens' tagline it is replaced until someone writes their own.
        public const string DefaultBannerBody = "Flieg über deine Insel: Stick bewegt, Ziehen schaut um, Tiere antippen.";
        const string LegacyBannerBody = "Schau dich in Ruhe um.";

        public string BannerBody => string.IsNullOrEmpty(bannerBody) || bannerBody == LegacyBannerBody ? DefaultBannerBody : bannerBody;

        void ApplyBannerTexts()
        {
            if (_banner == null) return;
            _banner.TopCenter(new Vector2(0f, -Mathf.Max(0f, bannerTop)), BannerSize);
            if (_bannerTitle != null && _bannerTitle.text != bannerTitle) _bannerTitle.text = bannerTitle;
            if (_bannerBody != null && _bannerBody.text != BannerBody) _bannerBody.text = BannerBody;
        }

        void SetBanner(bool on, float alpha)
        {
            if (_banner == null) return;
            _bannerAlpha = Mathf.Clamp01(alpha);
            if (_banner.gameObject.activeSelf != on) _banner.gameObject.SetActive(on);
            if (_bannerGroup == null) return;
            _bannerGroup.alpha = _bannerAlpha;
            bool taps = on && _bannerAlpha > 0.6f;
            if (_bannerGroup.blocksRaycasts != taps) _bannerGroup.blocksRaycasts = taps;
        }

        static Text Line(RectTransform panel, string text, int size, Color color, float y, float height, bool bold = false)
        {
            var t = UiStyle.Label(panel, text, size, color, TextAnchor.UpperCenter, bold);
            t.rectTransform.TopCenter(new Vector2(0f, y), new Vector2(840f, height));
            return t;
        }

        void OnNewWorld()
        {
            if (session != null) session.StartNewGame(GameMode.Cozy);
        }

        void OnTitle()
        {
            if (session != null) session.ReturnToTitle();
        }

        void ShowScreen(bool blocker, bool screen)
        {
            if (_canvas == null) return;
            if (_blocker != null && _blocker.activeSelf != blocker) _blocker.SetActive(blocker);
            if (_screen != null && _screen.activeSelf != screen) _screen.SetActive(screen);
        }

        void FillScreen(RunRecord r, Texture2D picture)
        {
            if (_statsText != null) _statsText.text = FormatStats(r);
            if (_nameText != null) _nameText.text = r != null ? RunJournal.TitleOf(r, RunJournal.NumberOf(r)) : "";
            if (_seedText != null) _seedText.text = r != null ? (r.seed != 0 ? $"Welt #{r.seed}" : "Welt: Standard") : "";
            if (_pictureView != null && _pictureView.texture != picture) _pictureView.texture = picture;
        }
    }
}
