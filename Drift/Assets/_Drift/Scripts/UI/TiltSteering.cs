using Drift.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Drift.UI
{
    // Tilt steering for phones: holding the phone the way it was calibrated means stop, tipping it in a
    // direction drives the island that way, the further the faster. There is no steering wheel - the tilt names
    // a direction on the screen and the island simply drifts that way (Island.DirectionProvider, wired up by
    // SessionScreens, which turns the screen direction into a world direction with the camera's yaw).
    //
    // Everything measurable lives in Drift.Core.TiltMath; this component only feeds it the sensor (or, in the
    // Editor, a fake pose from the mouse or the arrow keys) and keeps the calibrated middle.
    public class TiltSteering : MonoBehaviour
    {
        public enum FakeTilt { Aus, Pfeiltasten, Maus }

        const string OnKey = "drift_tilt_on";
        const string SensKey = "drift_tilt_sens";
        const string DeadKey = "drift_tilt_dead";

        [Header("Neigungssteuerung")]
        [Tooltip("Einstellungen der Neigungssteuerung. Empfindlichkeit und Ruhezone werden im Pausenmenü gesetzt und gespeichert.")]
        public TiltSettings settings = TiltSettings.Default;
        [Tooltip("Wird von der Pause gesetzt: aus = die Insel wird gerade nicht gesteuert (Pause, Fotomodus, Tagebuch).")]
        public bool inputEnabled = true;
        [Tooltip("Solange das Einstellungsfenster offen ist, wird weiter gemessen (für die Vorschau), aber nicht gesteuert.")]
        public bool previewing;
        [Tooltip("Nur im Editor: erzeugt eine gefälschte Handy-Haltung, damit die ganze Kette ohne Handy läuft. Pfeiltasten kippen, Maus = rechte Maustaste halten und ziehen.")]
        public FakeTilt editorFakeTilt = FakeTilt.Aus;
        [Tooltip("In so vielen Sekunden kippt die gefälschte Haltung von der Mitte bis zum vollen Ausschlag.")]
        [Range(0.05f, 2f)] public float fakeRampSeconds = 0.35f;
        [Tooltip("Zeitkonstante, mit der aus einem rohen Beschleunigungssensor die Schwerkraft herausgefiltert wird.")]
        [Range(0.05f, 1.5f)] public float accelerometerTau = 0.35f;

        Vector3 _pose, _neutral;
        Vector3 _accLow;
        Vector2 _fake;
        bool _calibrated;
        bool _sensorsOn;
        bool _prefsRead;
        bool _resumeRecenter;
        bool _wasActive;

        // The steering direction on screen (x right, y up the screen), length 0..1. Live while Sampling.
        public Vector2 Direction { get; private set; }
        // The unshaped tilt in degrees (screen axes) - what the settings preview shows.
        public Vector2 TiltDegrees { get; private set; }
        public Vector3 Neutral => _neutral;
        public float NeutralSteepnessDeg => TiltMath.SteepnessDeg(_neutral);
        public bool NeutralTooSteep => _calibrated && NeutralSteepnessDeg > TiltMath.WarnSteepDeg;
        public bool Faking => editorFakeTilt != FakeTilt.Aus && !Application.isMobilePlatform;
        public bool SensorAvailable => GravitySensor.current != null || Accelerometer.current != null || AttitudeSensor.current != null;
        public bool Available => SensorAvailable || Faking;
        public bool On
        {
            get { ReadPrefs(); return _on; }
            set
            {
                ReadPrefs();
                if (_on == value) return;
                _on = value;
                PlayerPrefs.SetInt(OnKey, value ? 1 : 0);
                PlayerPrefs.Save();
                if (value) _calibrated = false;
            }
        }
        bool _on;

        // Measuring runs while the steering is on or the settings screen wants a preview.
        public bool Sampling => Application.isPlaying && Available && (On || previewing);
        // Actually steering the island.
        public bool Active => Sampling && On && inputEnabled;

        public void SaveTuning()
        {
            PlayerPrefs.SetFloat(SensKey, settings.sensitivity);
            PlayerPrefs.SetFloat(DeadKey, settings.deadZone);
            PlayerPrefs.Save();
        }

        // "Mitte setzen": the pose right now becomes the zero. Called at the start of a run as well, so a run
        // always starts from however the player happens to be holding the phone.
        public void Calibrate()
        {
            Vector3 g = ReadGravity(0f);
            if (g.sqrMagnitude < 1e-6f) { _calibrated = false; return; }
            _pose = g;
            _neutral = g;
            _calibrated = true;
            Direction = Vector2.zero;
            TiltDegrees = Vector2.zero;
        }

        // Dropped calibration: the next frame that reads a pose takes it as the middle.
        public void Recalibrate() => _calibrated = false;

        void ReadPrefs()
        {
            if (_prefsRead) return;
            _prefsRead = true;
            _on = PlayerPrefs.GetInt(OnKey, 0) != 0;
            settings.sensitivity = PlayerPrefs.GetFloat(SensKey, settings.sensitivity);
            settings.deadZone = PlayerPrefs.GetFloat(DeadKey, settings.deadZone);
        }

        // Back from the background the phone is usually held differently. The game comes back paused, so the pose that
        // counts is the one when the tilt steers again (the pause menu's "Weiter"), not the first frame after the return.
        public static bool ResumeRecenterDue(bool pending, bool active, bool wasActive) => pending && active && !wasActive;

        void OnApplicationPause(bool paused)
        {
            if (!paused) OnReturnedFromBackground();
        }

        // As in GameSession: devices report the return through OnApplicationPause, the Editor and desktop players only
        // through the focus callback.
        void OnApplicationFocus(bool focused)
        {
            if (focused && (Application.isEditor || !Application.isMobilePlatform)) OnReturnedFromBackground();
        }

        void OnReturnedFromBackground()
        {
            if (!Application.isPlaying || !On) return;
            _resumeRecenter = true;
            if (Active) Recalibrate();
        }

        void OnEnable()
        {
            ReadPrefs();
            _calibrated = false;
            _resumeRecenter = false;
            _wasActive = false;
            Direction = Vector2.zero;
            TiltDegrees = Vector2.zero;
        }

        void OnDisable()
        {
            Direction = Vector2.zero;
            TiltDegrees = Vector2.zero;
            EnableSensors(false);
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            bool sampling = Sampling;
            bool active = Active;
            if (ResumeRecenterDue(_resumeRecenter, active, _wasActive))
            {
                _resumeRecenter = false;
                _calibrated = false;
            }
            _wasActive = active;
            EnableSensors(sampling && !Faking);
            if (!sampling)
            {
                Direction = Vector2.zero;
                TiltDegrees = Vector2.zero;
                _calibrated = false;
                return;
            }

            // Unscaled: the settings screen previews the tilt while the game is paused.
            float dt = Mathf.Min(0.1f, Time.unscaledDeltaTime);
            Vector3 g = ReadGravity(dt);
            if (g.sqrMagnitude < 1e-6f)
            {
                Direction = Vector2.zero;
                TiltDegrees = Vector2.zero;
                return;
            }
            if (!_calibrated)
            {
                _pose = g;
                _neutral = g;
                _calibrated = true;
            }
            _pose = TiltMath.Smooth(_pose, g, settings.smoothing, dt);
            Vector2 screen = TiltMath.ToScreen(TiltMath.DeviceTilt(_neutral, _pose), Orientation);
            TiltDegrees = screen;
            Direction = TiltMath.Shape(screen, settings);
            _neutral = TiltMath.FollowNeutral(_neutral, _pose, settings.neutralFollow, Direction.magnitude, dt);
        }

        // The fake pose is built in device axes, so it is portrait by construction; a real sensor reports in the
        // device's own frame, which only matches the screen while the screen is portrait.
        ScreenOrientation Orientation
        {
            get
            {
                if (Faking) return ScreenOrientation.Portrait;
                var o = Screen.orientation;
                if (o == ScreenOrientation.AutoRotation) return Screen.width > Screen.height ? ScreenOrientation.LandscapeLeft : ScreenOrientation.Portrait;
                return o;
            }
        }

        Vector3 ReadGravity(float dt)
        {
            if (Faking) return TiltMath.PoseFor(TiltMath.FlatGravity, UpdateFake(dt));

            var gravity = GravitySensor.current;
            if (gravity != null && gravity.enabled)
            {
                Vector3 v = gravity.gravity.ReadValue();
                if (v.sqrMagnitude > 0.04f) return v.normalized;
            }
            // Unity Remote and older devices only give the raw accelerometer: low-pass it to get the gravity out.
            var accel = Accelerometer.current;
            if (accel != null && accel.enabled)
            {
                Vector3 v = accel.acceleration.ReadValue();
                if (v.sqrMagnitude > 0.04f)
                {
                    _accLow = _accLow.sqrMagnitude < 1e-6f || dt <= 0f ? v : Vector3.Lerp(_accLow, v, 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, accelerometerTau)));
                    if (_accLow.sqrMagnitude > 1e-6f) return _accLow.normalized;
                }
            }
            var attitude = AttitudeSensor.current;
            if (attitude != null && attitude.enabled)
            {
                Quaternion q = attitude.attitude.ReadValue();
                if (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w > 0.5f) return (Quaternion.Inverse(q) * Vector3.down).normalized;
            }
            return Vector3.zero;
        }

        Vector2 UpdateFake(float dt)
        {
            float full = settings.FullTiltDeg * 1.25f;
            if (editorFakeTilt == FakeTilt.Maus)
            {
                var mouse = Mouse.current;
                if (mouse == null || !mouse.rightButton.isPressed) { _fake = Vector2.zero; return _fake; }
                Vector2 p = mouse.position.ReadValue();
                float half = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) * 0.5f);
                _fake = Vector2.ClampMagnitude(new Vector2(p.x - Screen.width * 0.5f, p.y - Screen.height * 0.5f) / half, 1f) * full;
                return _fake;
            }

            var kb = Keyboard.current;
            Vector2 want = Vector2.zero;
            if (kb != null)
            {
                if (kb.rightArrowKey.isPressed) want.x += 1f;
                if (kb.leftArrowKey.isPressed) want.x -= 1f;
                if (kb.upArrowKey.isPressed) want.y += 1f;
                if (kb.downArrowKey.isPressed) want.y -= 1f;
            }
            float step = full * dt / Mathf.Max(0.05f, fakeRampSeconds);
            _fake = Vector2.MoveTowards(_fake, Vector2.ClampMagnitude(want, 1f) * full, step);
            return _fake;
        }

        // While on this runs every frame on purpose: a sensor that only shows up later (Unity Remote connecting)
        // still gets enabled.
        void EnableSensors(bool on)
        {
            if (!on && !_sensorsOn) return;
            _sensorsOn = on;
            SetDevice(GravitySensor.current, on);
            SetDevice(Accelerometer.current, on);
            SetDevice(AttitudeSensor.current, on);
        }

        static void SetDevice(InputDevice device, bool on)
        {
            if (device == null) return;
            if (on)
            {
                if (!device.enabled) InputSystem.EnableDevice(device);
            }
            else if (device.enabled) InputSystem.DisableDevice(device);
        }
    }
}
