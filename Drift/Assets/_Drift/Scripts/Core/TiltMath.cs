using System;
using UnityEngine;

namespace Drift.Core
{
    // Tuning of the tilt steering, kept as plain data so the sliders, the save of the settings and the tests
    // all talk about the same numbers.
    [Serializable]
    public struct TiltSettings
    {
        [Tooltip("Wie weit du kippen musst, bis die Insel mit vollem Tempo fährt. Größer = empfindlicher.")]
        [Range(0f, 1f)] public float sensitivity;
        [Tooltip("Wie schief du das Handy halten darfst, ohne dass sich etwas bewegt (Anteil des vollen Ausschlags).")]
        [Range(0f, 0.5f)] public float deadZone;
        [Tooltip("Zeitkonstante der Glättung in Sekunden - gleicht das Zittern der Hand aus.")]
        [Range(0f, 0.6f)] public float smoothing;
        [Tooltip("Wie schnell die Mitte der gehaltenen Haltung nachwandert (pro Sekunde, 0 = gar nicht).")]
        [Range(0f, 1f)] public float neutralFollow;
        [Tooltip("Kennlinie: über 1 reagiert die Insel bei kleinem Kippen feiner.")]
        [Range(0.5f, 3f)] public float response;

        // The slider ends of the full-throttle tilt angle: a lazy hold at sensitivity 0, a flick of the wrist at 1.
        public const float LooseTiltDeg = 40f;
        public const float TightTiltDeg = 10f;

        public static TiltSettings Default => new TiltSettings
        {
            sensitivity = 0.5f,
            deadZone = 0.18f,
            smoothing = 0.12f,
            neutralFollow = 0.04f,
            response = 1.3f
        };

        // The tilt angle (degrees away from the calibrated middle) that means full speed.
        public float FullTiltDeg => Mathf.Lerp(LooseTiltDeg, TightTiltDeg, Mathf.Clamp01(sensitivity));

        // What the player actually has to keep still, in degrees - the number the settings screen shows.
        public float DeadZoneDeg => FullTiltDeg * Mathf.Clamp(deadZone, 0f, 0.9f);
    }

    // The pure side of the tilt steering. Everything here is math on a gravity direction, so it is testable
    // without a phone: the Editor feeds a fake gravity vector through exactly the same chain.
    //
    // Convention: gravity is the unit vector that points earthwards, expressed in DEVICE axes (X = the phone's
    // right edge in portrait, Y = its top edge, Z = out of the screen). That is what UnityEngine.InputSystem's
    // GravitySensor/Accelerometer report (flat on the table, screen up: (0, 0, -1); portrait upright: (0, -1, 0)).
    // Nothing depends on the absolute pose, because the neutral is calibrated - only on the rotation from the
    // calibrated middle to the current pose.
    public static class TiltMath
    {
        public static readonly Vector3 FlatGravity = new Vector3(0f, 0f, -1f);

        // The rotation from the calibrated middle to the current pose, as degrees of tilt along the device's
        // own axes: x = the right edge dipped down, y = the far edge dipped down. Independent of how far the
        // player leans back, which the naive "difference of the gravity components" is not (held upright, a
        // pitch barely changes the y component at all).
        public static Vector2 DeviceTilt(Vector3 neutral, Vector3 gravity)
        {
            if (neutral.sqrMagnitude < 1e-8f || gravity.sqrMagnitude < 1e-8f) return Vector2.zero;
            Vector3 n = neutral.normalized, g = gravity.normalized;
            Vector3 axis = Vector3.Cross(n, g);
            float sin = axis.magnitude;
            if (sin < 1e-6f) return Vector2.zero;
            axis /= sin;
            float ang = Mathf.Atan2(sin, Mathf.Clamp(Vector3.Dot(n, g), -1f, 1f)) * Mathf.Rad2Deg;
            return new Vector2(-axis.y, axis.x) * ang;
        }

        // Device axes -> screen axes (x right, y up the screen). The phone reports in its own frame, which only
        // matches the screen while the screen is portrait.
        public static Vector2 ToScreen(Vector2 deviceTilt, ScreenOrientation orientation)
        {
            switch (orientation)
            {
                case ScreenOrientation.PortraitUpsideDown: return new Vector2(-deviceTilt.x, -deviceTilt.y);
                case ScreenOrientation.LandscapeLeft: return new Vector2(-deviceTilt.y, deviceTilt.x);
                case ScreenOrientation.LandscapeRight: return new Vector2(deviceTilt.y, -deviceTilt.x);
                default: return deviceTilt;
            }
        }

        // Dead zone and speed curve: degrees of tilt -> a direction of length 0..1 (0 = hold still, 1 = full speed).
        public static Vector2 Shape(Vector2 tiltDeg, in TiltSettings s)
        {
            float mag = tiltDeg.magnitude;
            if (mag < 1e-5f) return Vector2.zero;
            float norm = Mathf.Clamp01(mag / Mathf.Max(1f, s.FullTiltDeg));
            float dead = Mathf.Clamp(s.deadZone, 0f, 0.9f);
            if (norm <= dead) return Vector2.zero;
            float t = (norm - dead) / (1f - dead);
            return tiltDeg / mag * Mathf.Pow(t, Mathf.Max(0.1f, s.response));
        }

        // Neutral + current pose -> the steering direction on screen, length 0..1. The whole chain except the
        // smoothing and the drift-follow, which need a state to live in.
        public static Vector2 Evaluate(Vector3 neutral, Vector3 gravity, ScreenOrientation orientation, in TiltSettings s)
        {
            return Shape(ToScreen(DeviceTilt(neutral, gravity), orientation), s);
        }

        // Frame-rate independent exponential smoothing of the gravity direction; tau is the time constant in
        // seconds. Smoothing the pose (not the output) keeps the dead zone edge from chattering.
        public static Vector3 Smooth(Vector3 current, Vector3 sample, float tau, float dt)
        {
            if (current.sqrMagnitude < 1e-8f || tau <= 1e-4f || dt <= 0f) return sample.sqrMagnitude > 1e-8f ? sample.normalized : current;
            if (sample.sqrMagnitude < 1e-8f) return current;
            Vector3 v = Vector3.Lerp(current, sample.normalized, 1f - Mathf.Exp(-dt / tau));
            return v.sqrMagnitude > 1e-8f ? v.normalized : current;
        }

        // The middle wanders very slowly towards how the phone is actually being held, so a slowly changing
        // posture does not become a permanent pull. It only follows while the island is near a standstill
        // (output 0..1): holding a direction must not erode the direction you are holding.
        public static Vector3 FollowNeutral(Vector3 neutral, Vector3 gravity, float rate, float output, float dt)
        {
            if (rate <= 0f || dt <= 0f || gravity.sqrMagnitude < 1e-8f) return neutral;
            float k = 1f - Mathf.Exp(-rate * Mathf.Clamp01(1f - Mathf.Abs(output)) * dt);
            Vector3 n = Vector3.Lerp(neutral, gravity.normalized, k);
            return n.sqrMagnitude > 1e-8f ? n.normalized : neutral;
        }

        // Four keys (W/A/S/D) or any four-way pad as a direction on the SCREEN: x right, y up the screen,
        // length 0..1. A diagonal is normalised, so holding two keys is not faster than one.
        public static Vector2 KeysToScreen(bool left, bool right, bool up, bool down)
        {
            var v = new Vector2((right ? 1f : 0f) - (left ? 1f : 0f), (up ? 1f : 0f) - (down ? 1f : 0f));
            return v.sqrMagnitude > 1f ? v.normalized : v;
        }

        // A thumbstick / tilt reading as a steering direction: never longer than full throttle.
        public static Vector2 ClampStick(Vector2 screenDir) =>
            screenDir.sqrMagnitude > 1f ? screenDir.normalized : screenDir;

        // Screen direction -> world XZ direction, turned by the camera's yaw: "up the screen" is always away
        // from the camera. Length is kept, so a 0..1 direction stays a 0..1 direction.
        public static Vector2 ToWorld(Vector2 screenDir, float cameraYawDeg)
        {
            float rad = cameraYawDeg * Mathf.Deg2Rad;
            float sin = Mathf.Sin(rad), cos = Mathf.Cos(rad);
            return new Vector2(cos * screenDir.x + sin * screenDir.y, -sin * screenDir.x + cos * screenDir.y);
        }

        // A world direction served through the old (turn, throttle) input: turn hard towards it, throttle with
        // what is left pointing forwards. Only the stop-gap until the island can be given a direction outright
        // (Island.DirectionProvider) - this is the turn-then-accelerate scheme that feels sluggish.
        public static Vector2 AsTurnThrottle(Vector2 worldDir, Vector2 forward, float fullTurnDeg = 45f)
        {
            float mag = worldDir.magnitude;
            if (mag < 1e-5f || forward.sqrMagnitude < 1e-8f) return Vector2.zero;
            Vector2 d = worldDir / mag;
            Vector2 f = forward.normalized;
            // Negated: SignedAngle counts counter-clockwise in its own plane, and this plane is XZ seen from
            // above, where the island's positive turn (input.x = D) goes clockwise.
            float angle = -Vector2.SignedAngle(f, d);
            float turn = Mathf.Clamp(angle / Mathf.Max(1f, fullTurnDeg), -1f, 1f);
            float along = Vector2.Dot(f, d);
            return new Vector2(turn, Mathf.Min(1f, mag) * Mathf.Max(0.15f, along));
        }

        // How steeply the calibrated middle is held: 0 = flat on the table, 90 = the phone stands upright. A
        // rotation around the gravity vector cannot be measured at all, so at 90 the left/right axis is dead -
        // the settings screen warns from WarnSteepDeg on.
        public const float WarnSteepDeg = 72f;

        public static float SteepnessDeg(Vector3 neutral)
        {
            if (neutral.sqrMagnitude < 1e-8f) return 0f;
            return Vector3.Angle(neutral.normalized, FlatGravity);
        }

        // The inverse of DeviceTilt: the pose that a given tilt would be read from. The Editor's fake tilt
        // (mouse / arrow keys) builds its gravity with this, so it runs through the same chain as a real phone.
        public static Vector3 PoseFor(Vector3 neutral, Vector2 deviceTiltDeg)
        {
            Vector3 n = neutral.sqrMagnitude > 1e-8f ? neutral.normalized : FlatGravity;
            float ang = deviceTiltDeg.magnitude;
            if (ang < 1e-5f) return n;
            Vector3 axis = new Vector3(deviceTiltDeg.y, -deviceTiltDeg.x, 0f) / ang;
            // An axis parallel to the neutral carries no tilt; fall back to the device plane in that case.
            Vector3 perp = axis - n * Vector3.Dot(axis, n);
            if (perp.sqrMagnitude > 1e-6f) axis = perp.normalized;
            return (Quaternion.AngleAxis(ang, axis) * n).normalized;
        }
    }
}
