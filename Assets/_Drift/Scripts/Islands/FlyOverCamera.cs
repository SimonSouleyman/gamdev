using UnityEngine;

namespace Drift.Islands
{
    // The camera the player flies over their finished Pangäa. Once the last island is merged the island itself
    // stops taking any steering (GameSession locks it and PangaeaFinale pins it) and this drives the main camera
    // instead: the stick, W/A/S/D and the tilt slide the view across the island, a drag looks around it, pinch
    // and Q/E change the height.
    // What the input really moves is the FOCUS - the spot on the island the camera looks at - and the camera is
    // placed above and behind it. So whatever the player does, the island stays in the middle of the screen
    // instead of being left behind, and the bounds only have to keep the focus near the coast.
    // A plain class, not a component: PangaeaFinale owns one, drives it from its own LateUpdate and hands the
    // camera back to IslandChaseCamera when the free look ends.
    public sealed class FlyOverCamera
    {
        public struct Input
        {
            // Screen direction of the stick / keys / tilt, length 0..1.
            public Vector2 move;
            // Drag since the last frame, in pixels. The view follows the finger: dragging right swings the view
            // round to the left, dragging up lifts it towards the horizon.
            public Vector2 look;
            // Pinch / scroll / Q-E as a factor on the height (> 1 = further away), 0 = untouched.
            public float zoomFactor;
        }

        [System.Serializable]
        public class Settings
        {
            [Tooltip("Wie schnell der Blick über die Insel gleitet (Einheiten pro Sekunde in halber Höhe).")]
            [Range(2f, 40f)] public float moveSpeed = 11f;
            [Tooltip("Wie träge die Bewegung anfährt und ausrollt (klein = sehr sanft).")]
            [Range(0.5f, 8f)] public float moveResponse = 2.4f;
            [Tooltip("Wie weit ein Ziehen über den Bildschirm dreht (Grad pro Pixel).")]
            [Range(0.02f, 0.6f)] public float lookDegPerPixel = 0.13f;
            [Tooltip("Wie träge der Blick dem Ziehen folgt.")]
            [Range(1f, 20f)] public float lookResponse = 9f;
            [Tooltip("Flachster Blickwinkel nach unten (Grad): kleiner = die Kamera steht weiter hinten.")]
            [Range(10f, 45f)] public float minPitch = 25f;
            [Tooltip("Steilster Blickwinkel nach unten (Grad): 90 wäre senkrecht von oben.")]
            [Range(45f, 85f)] public float maxPitch = 78f;
            [Tooltip("Geringste Flughöhe über der Insel.")]
            [Range(2f, 30f)] public float minHeight = 6f;
            [Tooltip("Größte Flughöhe: mindestens so hoch, sonst gut zwei Inselradien (eine große Pangäa passt also ganz ins Bild).")]
            [Range(20f, 400f)] public float maxHeight = 45f;
            [Tooltip("Wie weit der Blick über die Küste hinausgleiten darf (Vielfaches des Inselradius).")]
            [Range(1f, 3f)] public float reachFactor = 1.15f;
            [Tooltip("Zusätzlicher Spielraum über die Küste hinaus (Einheiten).")]
            [Range(0f, 60f)] public float reachPad = 10f;
            [Tooltip("Mindestabstand der Kamera über dem Boden.")]
            [Range(0.5f, 10f)] public float clearance = 2.5f;
        }

        // Replaced by PangaeaFinale with its own serialized instance, so the sliders live in the inspector.
        public Settings settings = new Settings();

        Vector2 _focus, _vel;
        float _height, _yaw, _pitch, _yawTarget, _pitchTarget;

        public Vector3 Position { get; private set; }
        public Quaternion Rotation { get; private set; }
        public Vector3 LookPoint { get; private set; }
        public float Height => _height;
        public float Yaw => _yaw;
        public float Pitch => _pitch;
        // The spot on the island the camera looks at - this is what the stick moves.
        public Vector2 Focus => _focus;
        public Vector2 PlanarPosition => new Vector2(Position.x, Position.z);

        // Takes over exactly where the chase camera stands, so the hand-over is not a cut: same height, same
        // direction, and the focus is where that view already meets the ground.
        public void Reset(Vector3 camPos, Quaternion camRot, float groundY)
        {
            Vector3 f = camRot * Vector3.forward;
            _height = Mathf.Max(settings.minHeight, camPos.y - groundY);
            _yaw = _yawTarget = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
            _pitch = _pitchTarget = Mathf.Clamp(pitch, settings.minPitch, settings.maxPitch);
            _focus = new Vector2(camPos.x, camPos.z) + Forward(_yaw) * Distance(_height, _pitch);
            _vel = Vector2.zero;
            Position = camPos;
            Rotation = camRot;
            LookPoint = new Vector3(_focus.x, groundY, _focus.y);
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
                           SpeedAt(_height, settings.minHeight, top, settings.moveSpeed);
            _vel = Vector2.Lerp(_vel, want, Step01(settings.moveResponse, dt));
            _focus += _vel * dt;
            _focus = ClampToReach(_focus, centre, Reach(radius, settings.reachFactor, settings.reachPad));

            // The focus sits on the land (sea level over water), so flying over a mountain keeps it centred.
            float surface = island != null ? Mathf.Max(0f, island.SampleHeight(island.ToLocal(_focus))) : 0f;
            var look = new Vector3(_focus.x, groundY + surface, _focus.y);
            Vector2 back = _focus - Forward(_yaw) * Distance(_height, _pitch);
            var pos = new Vector3(back.x, groundY + surface + _height, back.y);
            // Never inside the hill the camera is flying over.
            if (island != null)
            {
                float need = IslandChaseCamera.RequiredHeight(island, pos, look, settings.clearance, 0f);
                if (need > float.MinValue && pos.y < need) pos.y = need;
            }
            Position = pos;
            LookPoint = look;
            Rotation = Quaternion.LookRotation((look - pos).normalized, Vector3.up);
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

        // A screen direction through the camera's own yaw: up the screen is away from the camera.
        public static Vector2 Screen2World(Vector2 screen, float yawDeg)
        {
            Vector2 f = Forward(yawDeg);
            var r = new Vector2(f.y, -f.x);
            return r * screen.x + f * screen.y;
        }

        // How far behind the focus the camera sits for a given height and downward pitch.
        public static float Distance(float height, float pitchDeg) =>
            Mathf.Max(0f, height) / Mathf.Tan(Mathf.Clamp(pitchDeg, 5f, 89f) * Mathf.Deg2Rad);

        // High up the island slides by slowly, so the fly-over speeds up with the height.
        public static float SpeedAt(float height, float minHeight, float maxHeight, float speed)
        {
            float t = Mathf.InverseLerp(minHeight, Mathf.Max(minHeight + 1f, maxHeight), height);
            return speed * Mathf.Lerp(0.45f, 1.6f, t);
        }

        public static float MaxHeight(float radius, float floor) => Mathf.Max(floor, 2.2f * Mathf.Max(0f, radius));

        public static float Reach(float radius, float factor, float pad) => Mathf.Max(0f, radius) * Mathf.Max(1f, factor) + Mathf.Max(0f, pad);

        // Stay over the island: past the reach the view simply stops sliding, it is never pushed back.
        public static Vector2 ClampToReach(Vector2 pos, Vector2 centre, float reach)
        {
            Vector2 d = pos - centre;
            float len = d.magnitude;
            return len <= reach || len < 1e-4f ? pos : centre + d * (reach / len);
        }
    }
}
