using UnityEngine;

namespace Drift.Islands
{
    // The camera the player flies over their finished Pangäa. Once the last island is merged the island itself
    // stops taking any steering (GameSession locks it and PangaeaFinale pins it) and this drives the main camera
    // instead: a free flight. The stick and W/A/S/D fly the CAMERA across the island (the tilt is ignored here: the
    // owner holds the phone differently over the finished island; up the screen is
    // straight ahead), a drag turns the view where the camera stands, pinch and Q/E change the flying height.
    // The first version moved a focus point and kept the camera behind it: a drag then orbited that point, which at
    // the start is the middle of the island - on the phone it felt nailed to the island centre (owner, v0.6.2).
    // A plain class, not a component: PangaeaFinale owns one, drives it from its own LateUpdate and hands the
    // camera back to IslandChaseCamera when the free look ends.
    public sealed class FlyOverCamera
    {
        public struct Input
        {
            // Screen direction of the stick / keys, length 0..1.
            public Vector2 move;
            // Drag since the last frame, in pixels. Dragging right swings the view round to the left (the finger
            // pulls the world along), dragging up lifts it towards the horizon.
            public Vector2 look;
            // Pinch / scroll / Q-E as a factor on the height (> 1 = higher), 0 = untouched.
            public float zoomFactor;
        }

        [System.Serializable]
        public class Settings
        {
            [Tooltip("Fluggeschwindigkeit (Einheiten pro Sekunde) in der Bezugshöhe; höher oben fliegt die Kamera entsprechend schneller, damit das Bild gleich schnell vorbeizieht.")]
            [Range(2f, 40f)] public float moveSpeed = 12f;
            [Tooltip("Bezugshöhe für die Fluggeschwindigkeit.")]
            [Range(4f, 60f)] public float speedHeight = 15f;
            [Tooltip("Wie träge die Bewegung anfährt und ausrollt (klein = sehr sanft).")]
            [Range(0.5f, 8f)] public float moveResponse = 3f;
            [Tooltip("Wie weit ein Ziehen über den Bildschirm dreht (Grad pro Pixel).")]
            [Range(0.02f, 0.6f)] public float lookDegPerPixel = 0.12f;
            [Tooltip("Wie träge der Blick dem Ziehen folgt.")]
            [Range(1f, 20f)] public float lookResponse = 12f;
            [Tooltip("Flachster Blick nach unten (Grad): klein = fast bis zum Horizont.")]
            [Range(0f, 45f)] public float minPitch = 8f;
            [Tooltip("Steilster Blick nach unten (Grad): 90 wäre senkrecht von oben.")]
            [Range(45f, 89f)] public float maxPitch = 85f;
            [Tooltip("Geringste Flughöhe über dem Meer (über Bergen bleibt die Kamera immer darüber).")]
            [Range(1f, 30f)] public float minHeight = 3f;
            [Tooltip("Größte Flughöhe: mindestens so hoch, sonst zweieinhalb Inselradien (eine große Pangäa passt also ganz ins Bild).")]
            [Range(20f, 400f)] public float maxHeight = 60f;
            [Tooltip("Wie weit die Kamera über die Insel hinaus fliegen darf (Vielfaches des Inselradius, vom Inselmittelpunkt aus).")]
            [Range(1f, 3f)] public float reach = 1.25f;
            [Tooltip("Zusätzlicher Spielraum über die Küste hinaus (Einheiten).")]
            [Range(0f, 100f)] public float reachMargin = 30f;
            [Tooltip("Mindestabstand der Kamera über dem Boden.")]
            [Range(0.5f, 10f)] public float clearance = 2f;
        }

        // Replaced by PangaeaFinale with its own serialized instance, so the sliders live in the inspector.
        public Settings settings = new Settings();

        Vector2 _pos, _vel;
        float _height, _yaw, _pitch, _yawTarget, _pitchTarget, _floor;
        bool _hasFloor;

        public Vector3 Position { get; private set; }
        public Quaternion Rotation { get; private set; }
        // Where the view meets sea level (what the vegetation calms its wind by).
        public Vector3 LookPoint { get; private set; }
        // Flying height above sea level, the value pinch changes.
        public float Height => _height;
        public float Yaw => _yaw;
        public float Pitch => _pitch;
        // Where the camera itself is over the map - this is what the stick moves.
        public Vector2 PlanarPosition => _pos;

        // Takes over exactly where the chase camera stands, so the hand-over is not a cut.
        public void Reset(Vector3 camPos, Quaternion camRot, float groundY)
        {
            Vector3 f = camRot * Vector3.forward;
            _pos = new Vector2(camPos.x, camPos.z);
            _height = Mathf.Max(settings.minHeight, camPos.y - groundY);
            _yaw = _yawTarget = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
            _pitch = _pitchTarget = Mathf.Clamp(pitch, settings.minPitch, settings.maxPitch);
            _vel = Vector2.zero;
            _hasFloor = false;
            Position = camPos;
            Rotation = camRot;
            LookPoint = camPos + f * Distance(camPos.y - groundY, _pitch);
        }

        // island is the finished Pangäa: its centre and radius give the bounds, its heightfield the floor.
        public void Step(Input input, float dt, Island island)
        {
            dt = Mathf.Clamp(dt, 0f, 0.1f);
            float radius = island != null ? island.BoundingRadius : 20f;
            Vector2 centre = island != null ? island.PlanarPosition : Vector2.zero;
            float groundY = island != null ? island.transform.position.y : 0f;
            float top = MaxHeight(radius, settings.maxHeight);

            if (input.zoomFactor > 0f) _height *= input.zoomFactor;
            _height = Mathf.Clamp(_height, settings.minHeight, top);

            _yawTarget -= input.look.x * settings.lookDegPerPixel;
            _pitchTarget = Mathf.Clamp(_pitchTarget - input.look.y * settings.lookDegPerPixel, settings.minPitch, settings.maxPitch);
            _yaw = SmoothAngle(_yaw, _yawTarget, settings.lookResponse, dt);
            _pitch = Mathf.Lerp(_pitch, _pitchTarget, Step01(settings.lookResponse, dt));

            Vector2 want = Screen2World(Vector2.ClampMagnitude(input.move, 1f), _yaw) *
                           SpeedAt(_height, settings.speedHeight, settings.moveSpeed);
            _vel = Vector2.Lerp(_vel, want, Step01(settings.moveResponse, dt));
            _pos = ClampToReach(_pos + _vel * dt, centre, Reach(radius, settings.reach, settings.reachMargin));

            // Never inside the hill below: the floor rises at once and sinks back gently, so a ridge passing
            // underneath lifts the view instead of cutting through it.
            float ground = island != null ? Mathf.Max(0f, island.SampleHeight(island.ToLocal(_pos))) : 0f;
            float floor = groundY + ground + settings.clearance;
            _floor = !_hasFloor || floor > _floor ? floor : Mathf.Lerp(_floor, floor, Step01(1.5f, dt));
            _hasFloor = true;

            var pos = new Vector3(_pos.x, Mathf.Max(groundY + _height, _floor), _pos.y);
            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Position = pos;
            Rotation = rot;
            LookPoint = pos + rot * Vector3.forward * Distance(pos.y - groundY, _pitch);
        }

        // ---------------------------------------------------------------- pure helpers

        public static float Step01(float response, float dt) => dt > 0f ? 1f - Mathf.Exp(-Mathf.Max(0f, response) * dt) : 0f;

        public static float SmoothAngle(float angle, float target, float response, float dt) =>
            dt > 0f ? angle + Mathf.DeltaAngle(angle, target) * Step01(response, dt) : angle;

        public static Vector2 Forward(float yawDeg)
        {
            float rad = yawDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        }

        // A screen direction through the camera's own yaw: up the screen is straight ahead.
        public static Vector2 Screen2World(Vector2 screen, float yawDeg)
        {
            Vector2 f = Forward(yawDeg);
            var r = new Vector2(f.y, -f.x);
            return r * screen.x + f * screen.y;
        }

        // How far along the view the sea is for a given height and downward pitch.
        public static float Distance(float height, float pitchDeg) =>
            Mathf.Max(0f, height) / Mathf.Sin(Mathf.Clamp(pitchDeg, 5f, 90f) * Mathf.Deg2Rad);

        // The picture slides by at the same pace at any height: the speed grows with the height.
        public static float SpeedAt(float height, float speedHeight, float speed) =>
            speed * Mathf.Clamp(height / Mathf.Max(1f, speedHeight), 0.4f, 8f);

        public static float MaxHeight(float radius, float floor) => Mathf.Max(floor, 2.5f * Mathf.Max(0f, radius));

        public static float Reach(float radius, float factor, float pad) => Mathf.Max(0f, radius) * Mathf.Max(1f, factor) + Mathf.Max(0f, pad);

        // Stay near the island: past the reach the camera simply stops, it is never pushed back.
        public static Vector2 ClampToReach(Vector2 pos, Vector2 centre, float reach)
        {
            Vector2 d = pos - centre;
            float len = d.magnitude;
            return len <= reach || len < 1e-4f ? pos : centre + d * (reach / len);
        }
    }
}
