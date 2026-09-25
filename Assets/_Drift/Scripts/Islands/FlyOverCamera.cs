using UnityEngine;

namespace Drift.Islands
{
    // The camera over the finished Pangäa, driven like a map app on a phone held upright. It looks at a focus point
    // on the ground from a yaw, a pitch and a distance:
    // - one finger grabs the ground and drags it (the point under the finger stays under the finger), with a fling;
    // - two fingers pinch (distance, about the spot between them), twist (yaw about that spot) and, moved up or down
    //   together, tilt the view;
    // - FlyTo glides to a subject (ease in-out, a little arc on long hops), then follows it and circles slowly;
    // - without input for idleDelay seconds the view circles its focus slowly.
    // v0.6.3/0.6.4 flew the camera itself with a stick and turned the view in place on a drag: the owner found
    // circling the island and going from one thing to the next "sehr schwierig und unintuitiv" (v0.6.5 test).
    // A plain class, not a component: PangaeaFinale owns one, drives it from its own LateUpdate and hands the
    // camera back to IslandChaseCamera when the free look ends.
    public sealed class FlyOverCamera
    {
        public struct Input
        {
            // W/A/S/D as a screen direction (length 0..1): slides the focus that way.
            public Vector2 move;
            // One finger (or the left mouse button) grabbed the ground at panFrom and is now at panTo (screen pixels).
            public bool pan;
            public Vector2 panFrom, panTo;
            // Two fingers: their midpoint went from pinchFrom to pinchTo, their spread changed by pinchScale (> 1 =
            // apart = closer; 0 counts as 1) and they turned by twistDeg (counter-clockwise on the screen).
            public bool pinch;
            public Vector2 pinchFrom, pinchTo;
            public float pinchScale, twistDeg;
            // Two fingers moved up (+) or down the screen together, in pixels: up flattens the view.
            public float tiltPixels;
            // Right mouse drag / keys: turns and tilts the view about the focus (degrees).
            public float yawDeg, pitchDeg;
            // Wheel / Q-E: distance factor (> 1 = further away), 0 = untouched. About zoomPoint when zoomAtPoint.
            public float zoomFactor;
            public bool zoomAtPoint;
            public Vector2 zoomPoint;
            // A finger or button is down: the idle orbit, a fling and a flight to a subject stop.
            public bool touching;
            // Every finger was lifted since the last frame: a quick drag flings on.
            public bool released;
            // A tap (read by PangaeaFinale, which picks the subject; the camera itself ignores it).
            public bool tap;
            public Vector2 tapPos;

            public bool Manipulates =>
                pan || pinch || tiltPixels != 0f || yawDeg != 0f || pitchDeg != 0f || move.sqrMagnitude > 1e-6f ||
                (zoomFactor > 0f && zoomFactor != 1f);
        }

        [System.Serializable]
        public class Settings
        {
            [Tooltip("Flachster Blick (Grad über dem Horizont nach unten).")]
            [Range(10f, 60f)] public float minPitch = 25f;
            [Tooltip("Steilster Blick nach unten (90 = senkrecht von oben).")]
            [Range(45f, 89f)] public float maxPitch = 80f;
            [Tooltip("Geringster Abstand der Kamera vom Blickpunkt.")]
            [Range(2f, 30f)] public float minDistance = 5f;
            [Tooltip("Größter Abstand: mindestens so weit, sonst dreimal der Inselradius (eine große Pangäa passt ganz ins Bild).")]
            [Range(20f, 400f)] public float maxDistance = 90f;
            [Tooltip("Wie weit der Blickpunkt über die Insel hinaus darf (Vielfaches des Inselradius, vom Inselmittelpunkt aus).")]
            [Range(1f, 3f)] public float reach = 1.25f;
            [Tooltip("Zusätzlicher Spielraum über die Küste hinaus (Einheiten).")]
            [Range(0f, 100f)] public float reachMargin = 30f;
            [Tooltip("Mindestabstand der Kamera über dem Boden.")]
            [Range(0.5f, 10f)] public float clearance = 2f;

            [Tooltip("Wie lange die Karte nach einem schnellen Wischen weiterrollt (größer = kürzer).")]
            [Range(1f, 10f)] public float flingDecay = 3.5f;
            [Tooltip("Größtes Weiterrollen, in Blickabständen pro Sekunde.")]
            [Range(0.5f, 8f)] public float flingMax = 3f;
            [Tooltip("Zwei Finger über die ganze Bildhöhe nach oben/unten neigen die Ansicht um so viele Grad.")]
            [Range(30f, 300f)] public float tiltPerScreen = 120f;
            [Tooltip("Wie sanft Mausrad und Q/E zoomen.")]
            [Range(2f, 30f)] public float zoomResponse = 12f;
            [Tooltip("W/A/S/D: Geschwindigkeit in Blickabständen pro Sekunde.")]
            [Range(0.1f, 3f)] public float keySpeed = 0.9f;
            [Range(1f, 20f)] public float keyResponse = 6f;

            [Tooltip("So viele Sekunden ohne Berührung, dann kreist die Kamera langsam um ihren Blickpunkt.")]
            [Range(0.5f, 20f)] public float idleDelay = 4f;
            [Tooltip("Kreisgeschwindigkeit (Grad pro Sekunde).")]
            [Range(0f, 20f)] public float orbitDegPerSecond = 4f;
            [Tooltip("So lange braucht das Kreisen, bis es in Fahrt ist (Sekunden).")]
            [Range(0.1f, 5f)] public float orbitRamp = 1.2f;

            [Tooltip("Dauer des Flugs zu einem Ziel: kurze Sprünge (Sekunden) …")]
            [Range(0.3f, 3f)] public float flySecondsMin = 1.0f;
            [Tooltip("… und lange Sprünge quer über die Insel.")]
            [Range(0.3f, 4f)] public float flySecondsMax = 1.5f;
            [Tooltip("Anteil der halben kurzen Bildseite, den das Ziel am Ende füllt.")]
            [Range(0.1f, 1f)] public float flyFill = 0.5f;
            [Tooltip("Geringster Abstand beim Hinfliegen.")]
            [Range(3f, 40f)] public float flyMinDistance = 6f;
            [Tooltip("Ein Tier einer Herde soll am Ziel mindestens so viele Pixel lang sein (kleine Tiere werden näher gezeigt).")]
            [Range(0f, 120f)] public float flyBodyPixels = 40f;
            [Tooltip("Blickwinkel beim Hinfliegen, wenn das Ziel keinen eigenen hat (Grad).")]
            [Range(25f, 80f)] public float flyPitch = 50f;
            [Tooltip("Wie eng die Kamera einem Ziel folgt, das sich bewegt (eine Herde).")]
            [Range(0.5f, 10f)] public float trackResponse = 2.5f;
            [Tooltip("Wie schnell der Blickpunkt der Geländehöhe folgt.")]
            [Range(0.5f, 10f)] public float groundFollow = 2.5f;
        }

        enum Mode { Free, FlyTo, Track }

        // Replaced by PangaeaFinale with its own serialized instance, so the sliders live in the inspector.
        public Settings settings = new Settings();

        Vector3 _focus, _target, _fromFocus, _zoomPivot;
        float _yaw, _pitch, _dist;
        float _fromPitch, _fromDist, _toPitch, _toDist, _flyT, _flyDur, _bump;
        float _idle, _orbitSpeed, _zoomPending, _floor;
        bool _hasFloor, _zoomAtPivot, _panned;
        Vector2 _panVel, _fling, _keyVel;
        Mode _mode;
        float _fov = 60f, _screenW = 1080f, _screenH = 1920f;

        public Vector3 Position { get; private set; }
        public Quaternion Rotation { get; private set; }
        // Where the view is centred (the focus on the ground).
        public Vector3 LookPoint => _focus;
        public Vector3 Focus => _focus;
        public float Distance => _dist;
        public float Yaw => _yaw;
        public float Pitch => _pitch;
        // Camera height above sea level.
        public float Height => Position.y;
        public Vector2 PlanarPosition => new Vector2(Position.x, Position.z);
        public bool FlyingTo => _mode == Mode.FlyTo;
        public bool Tracking => _mode == Mode.Track;
        public bool Following => _mode != Mode.Free;
        public float IdleSeconds => _idle;
        public float OrbitSpeed => _orbitSpeed;

        public void SetLens(float fovDeg, float screenWidth, float screenHeight)
        {
            _fov = Mathf.Clamp(fovDeg, 5f, 150f);
            _screenW = Mathf.Max(1f, screenWidth);
            _screenH = Mathf.Max(1f, screenHeight);
        }

        // Takes over exactly where the camera stands, so the hand-over is not a cut: the focus is where the view's
        // centre meets the ground (sea level at groundY), the pitch is clamped into the map range about that point.
        public void Reset(Vector3 camPos, Quaternion camRot, float groundY)
        {
            Vector3 f = camRot * Vector3.forward;
            _yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
            float height = Mathf.Max(1f, camPos.y - groundY);
            float along = height / Mathf.Sin(Mathf.Clamp(pitch, 10f, 90f) * Mathf.Deg2Rad);
            _focus = camPos + new Vector3(f.x, 0f, f.z).normalized * (along * Mathf.Cos(Mathf.Clamp(pitch, 10f, 90f) * Mathf.Deg2Rad));
            _focus.y = groundY;
            _pitch = Mathf.Clamp(pitch, settings.minPitch, settings.maxPitch);
            _dist = Mathf.Max(settings.minDistance, along);
            _mode = Mode.Free;
            _idle = _orbitSpeed = _zoomPending = 0f;
            _panVel = _fling = _keyVel = Vector2.zero;
            _hasFloor = false;
            _panned = false;
            Compose(null, groundY);
        }

        // island is the finished Pangäa: its centre and radius give the bounds, its heightfield the floor.
        public void Step(Input input, float dt, Island island)
        {
            dt = Mathf.Clamp(dt, 0f, 0.1f);
            float radius = island != null ? island.BoundingRadius : 20f;
            Vector2 centre = island != null ? island.PlanarPosition : Vector2.zero;
            float groundY = island != null ? island.transform.position.y : 0f;
            float maxDist = MaxDistance(radius, settings.maxDistance);
            float reach = Reach(radius, settings.reach, settings.reachMargin);

            bool user = input.Manipulates || input.touching;
            if (user)
            {
                _idle = 0f;
                _orbitSpeed = 0f;
                _mode = Mode.Free;
            }
            else _idle += dt;
            if (input.touching || input.pan || input.pinch) _fling = Vector2.zero;

            Vector3 before = _focus;
            if (input.pan) Pan(input.panFrom, input.panTo);
            if (input.pinch)
            {
                Pan(input.pinchFrom, input.pinchTo);
                Vector3 pivot = Hit(input.pinchTo, _focus.y);
                float scale = input.pinchScale > 0f ? input.pinchScale : 1f;
                if (Mathf.Abs(scale - 1f) > 1e-6f) ZoomAbout(pivot, 1f / scale, maxDist);
                if (input.twistDeg != 0f) RotateAbout(pivot, input.twistDeg);
            }
            if (input.pan || input.pinch)
            {
                // The fling is the drag's recent speed; a finger that stopped before lifting leaves nothing to fling.
                if (dt > 0f)
                {
                    Vector2 v = new Vector2(_focus.x - before.x, _focus.z - before.z) / dt;
                    _panVel = Vector2.Lerp(_panVel, v, Step01(18f, dt));
                }
                _panned = true;
            }
            else if (input.touching && dt > 0f) _panVel = Vector2.Lerp(_panVel, Vector2.zero, Step01(18f, dt));
            if (input.released && !input.touching)
            {
                float cap = settings.flingMax * _dist;
                if (_panned && _panVel.magnitude > 0.08f * _dist) _fling = Vector2.ClampMagnitude(_panVel, cap);
                _panVel = Vector2.zero;
                _panned = false;
            }

            if (input.tiltPixels != 0f) _pitch -= input.tiltPixels / _screenH * settings.tiltPerScreen;
            if (input.pitchDeg != 0f) _pitch += input.pitchDeg;
            _pitch = Mathf.Clamp(_pitch, settings.minPitch, settings.maxPitch);
            if (input.yawDeg != 0f) _yaw = Mathf.Repeat(_yaw + input.yawDeg, 360f);

            if (input.zoomFactor > 0f && input.zoomFactor != 1f)
            {
                _zoomPending += Mathf.Log(input.zoomFactor);
                _zoomAtPivot = input.zoomAtPoint;
                if (_zoomAtPivot) _zoomPivot = Hit(input.zoomPoint, _focus.y);
            }
            if (Mathf.Abs(_zoomPending) > 1e-5f)
            {
                float step = _zoomPending * Step01(settings.zoomResponse, dt);
                _zoomPending -= step;
                ZoomAbout(_zoomAtPivot ? _zoomPivot : _focus, Mathf.Exp(step), maxDist);
                if (Mathf.Abs(_zoomPending) <= 1e-5f) _zoomPending = 0f;
            }

            Vector2 keyWant = Screen2World(Vector2.ClampMagnitude(input.move, 1f), _yaw) * (settings.keySpeed * _dist);
            _keyVel = Vector2.Lerp(_keyVel, keyWant, Step01(settings.keyResponse, dt));
            if (_fling.sqrMagnitude > 0f)
            {
                _fling *= Mathf.Exp(-settings.flingDecay * dt);
                if (_fling.magnitude < 0.02f * _dist) _fling = Vector2.zero;
            }
            Vector2 slide = (_keyVel + _fling) * dt;
            _focus.x += slide.x;
            _focus.z += slide.y;

            if (_mode == Mode.FlyTo)
            {
                _flyT += dt;
                float s = Ease(_flyDur > 0f ? _flyT / _flyDur : 1f);
                _focus = Vector3.Lerp(_fromFocus, _target, s);
                _dist = Mathf.Exp(Mathf.Lerp(Mathf.Log(_fromDist), Mathf.Log(_toDist), s)) * (1f + _bump * Mathf.Sin(Mathf.PI * s));
                _pitch = Mathf.Lerp(_fromPitch, _toPitch, s);
                if (_flyT >= _flyDur)
                {
                    _mode = Mode.Track;
                    // Arrived: circle the subject at once instead of waiting for the idle delay.
                    _idle = Mathf.Max(_idle, settings.idleDelay);
                }
            }
            else if (_mode == Mode.Track) _focus = Vector3.Lerp(_focus, _target, Step01(settings.trackResponse, dt));
            else if (!input.touching && !input.pan && !input.pinch)
            {
                // A focus left in the air (a flock) or on a hill sinks to the ground under it again, gently.
                float ground = GroundAt(island, new Vector2(_focus.x, _focus.z), groundY);
                _focus.y = Mathf.Lerp(_focus.y, ground, Step01(settings.groundFollow, dt));
            }

            bool orbit = !input.touching && _mode != Mode.FlyTo && _idle >= settings.idleDelay;
            float wantOrbit = orbit ? settings.orbitDegPerSecond : 0f;
            _orbitSpeed = Mathf.MoveTowards(_orbitSpeed, wantOrbit, settings.orbitDegPerSecond / Mathf.Max(0.05f, settings.orbitRamp) * dt);
            _yaw = Mathf.Repeat(_yaw + _orbitSpeed * dt, 360f);

            Vector2 fp = ClampToReach(new Vector2(_focus.x, _focus.z), centre, reach);
            _focus.x = fp.x;
            _focus.z = fp.y;
            if (_mode != Mode.FlyTo) _dist = Mathf.Clamp(_dist, settings.minDistance, maxDist);
            else _dist = Mathf.Clamp(_dist, settings.minDistance, maxDist * 1.5f);
            Compose(island, groundY, dt);
        }

        // Glides to a subject: the focus to `focus`, the distance and pitch to the framing, the yaw kept (the map
        // does not spin under the player). SetTarget keeps a moving subject up to date; afterwards the camera
        // follows it and circles it slowly until the player touches the view.
        public void FlyTo(Vector3 focus, float distance, float pitch, float maxDistance)
        {
            _fromFocus = _focus;
            _fromPitch = _pitch;
            _fromDist = Mathf.Max(0.1f, _dist);
            _target = focus;
            _toDist = Mathf.Clamp(distance, settings.minDistance, Mathf.Max(settings.minDistance, maxDistance));
            _toPitch = Mathf.Clamp(pitch, settings.minPitch, settings.maxPitch);
            float hop = new Vector2(focus.x - _focus.x, focus.z - _focus.z).magnitude;
            float span = Mathf.Max(_fromDist, _toDist);
            _flyDur = FlySeconds(hop, span, settings.flySecondsMin, settings.flySecondsMax);
            // A long hop rises a little on the way, so the ground below does not rush past.
            _bump = Mathf.Clamp01(hop / (_fromDist + _toDist) - 0.5f) * 0.5f;
            _flyT = 0f;
            _mode = Mode.FlyTo;
            _fling = _keyVel = _panVel = Vector2.zero;
            _zoomPending = 0f;
            _orbitSpeed = 0f;
            _idle = 0f;
        }

        public void SetTarget(Vector3 focus) => _target = focus;

        // The subject is gone (or the player took over): the view stays where it is.
        public void StopFollowing()
        {
            if (_mode == Mode.Free) return;
            _mode = Mode.Free;
        }

        // The distance that shows a subject of this radius at `fill` of half the short picture side.
        public float FrameDistance(float radius, float fill)
        {
            float tanV = Mathf.Tan(_fov * 0.5f * Mathf.Deg2Rad);
            float tanShort = tanV * Mathf.Min(1f, _screenW / _screenH);
            return Mathf.Max(settings.flyMinDistance, Mathf.Max(0.1f, radius) / (tanShort * Mathf.Clamp(fill, 0.05f, 1f)));
        }

        // The distance at which something bodyLength long is minPixels long on the screen (at the picture's centre).
        public float BodyDistance(float bodyLength, float minPixels)
        {
            float tanV = Mathf.Tan(_fov * 0.5f * Mathf.Deg2Rad);
            return Mathf.Max(0f, bodyLength) * (_screenH * 0.5f) / (tanV * Mathf.Max(1f, minPixels));
        }

        // ---------------------------------------------------------------- direct manipulation

        // The ground under `from` moves to `to`: the focus slides by the difference of the two points where the finger
        // rays meet the focus plane, measured in the pose the picture was drawn with.
        void Pan(Vector2 from, Vector2 to)
        {
            if ((from - to).sqrMagnitude < 1e-6f) return;
            Vector3 a = Hit(from, _focus.y), b = Hit(to, _focus.y);
            _focus.x += a.x - b.x;
            _focus.z += a.z - b.z;
            ComposeRaw();
        }

        // Scales the whole rig about a point on the focus plane: that point keeps its place on the screen.
        void ZoomAbout(Vector3 pivot, float factor, float maxDist)
        {
            float want = Mathf.Clamp(_dist * factor, settings.minDistance, maxDist);
            float k = want / Mathf.Max(1e-4f, _dist);
            _focus.x = pivot.x + (_focus.x - pivot.x) * k;
            _focus.z = pivot.z + (_focus.z - pivot.z) * k;
            _dist = want;
            ComposeRaw();
        }

        // Turns the whole rig about the vertical through a point on the focus plane.
        void RotateAbout(Vector3 pivot, float deg)
        {
            Vector3 off = Quaternion.Euler(0f, deg, 0f) * new Vector3(_focus.x - pivot.x, 0f, _focus.z - pivot.z);
            _focus.x = pivot.x + off.x;
            _focus.z = pivot.z + off.z;
            _yaw = Mathf.Repeat(_yaw + deg, 360f);
            ComposeRaw();
        }

        // Where the ray through a screen pixel meets the horizontal plane at planeY. A ray at or above the horizon
        // is bent down to just below it and the hit is capped, so a drag near the top of a flat view stays finite.
        public Vector3 Hit(Vector2 screen, float planeY)
        {
            Vector3 dir = RayDirection(screen);
            Vector3 origin = Position;
            float h = origin.y - planeY;
            if (dir.y > -0.03f)
            {
                dir.y = -0.03f;
                dir.Normalize();
            }
            float t = h > 0f ? h / -dir.y : 0f;
            t = Mathf.Min(t, Mathf.Max(20f, _dist * 8f));
            Vector3 p = origin + dir * t;
            p.y = planeY;
            return p;
        }

        public Vector3 RayDirection(Vector2 screen)
        {
            float tanV = Mathf.Tan(_fov * 0.5f * Mathf.Deg2Rad);
            float x = (screen.x / _screenW * 2f - 1f) * tanV * (_screenW / _screenH);
            float y = (screen.y / _screenH * 2f - 1f) * tanV;
            return (Rotation * new Vector3(x, y, 1f)).normalized;
        }

        // The pixel a world point is drawn at in the current pose (z < 0: behind the camera).
        public Vector3 WorldToScreen(Vector3 world)
        {
            Vector3 local = Quaternion.Inverse(Rotation) * (world - Position);
            if (Mathf.Abs(local.z) < 1e-5f) return new Vector3(0f, 0f, local.z);
            float tanV = Mathf.Tan(_fov * 0.5f * Mathf.Deg2Rad);
            float x = local.x / local.z / (tanV * (_screenW / _screenH));
            float y = local.y / local.z / tanV;
            return new Vector3((x + 1f) * 0.5f * _screenW, (y + 1f) * 0.5f * _screenH, local.z);
        }

        // ---------------------------------------------------------------- pose

        void ComposeRaw()
        {
            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            var pos = _focus - rot * Vector3.forward * _dist;
            // Keeps the terrain lift of the last frame, so a drag over a hill measures in the pose that was drawn.
            if (_hasFloor && pos.y < _floor) pos.y = _floor;
            Position = pos;
            Rotation = Aim(pos, rot);
        }

        void Compose(Island island, float groundY, float dt = 0f)
        {
            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            var pos = _focus - rot * Vector3.forward * _dist;
            // Never inside the hill below: the floor rises at once and sinks back gently, so a ridge passing
            // underneath lifts the view (still aimed at the focus) instead of cutting through it.
            float floor = GroundAt(island, new Vector2(pos.x, pos.z), groundY) + settings.clearance;
            _floor = !_hasFloor || floor > _floor ? floor : Mathf.Lerp(_floor, floor, Step01(1.5f, dt));
            _hasFloor = true;
            if (pos.y < _floor) pos.y = _floor;
            Position = pos;
            Rotation = Aim(pos, rot);
        }

        Quaternion Aim(Vector3 pos, Quaternion rot)
        {
            Vector3 to = _focus - pos;
            Vector3 f = rot * Vector3.forward;
            // Only re-aimed when the floor lifted the camera off its rig.
            if ((to.normalized - f).sqrMagnitude < 1e-8f || to.sqrMagnitude < 1e-6f) return rot;
            return Quaternion.LookRotation(to, Vector3.up);
        }

        static float GroundAt(Island island, Vector2 planar, float groundY)
        {
            if (island == null) return groundY;
            return groundY + Mathf.Max(0f, island.SampleHeight(island.ToLocal(planar)));
        }

        // ---------------------------------------------------------------- pure helpers

        public static float Step01(float response, float dt) => dt > 0f ? 1f - Mathf.Exp(-Mathf.Max(0f, response) * dt) : 0f;

        // Smootherstep: zero speed and zero acceleration at both ends.
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        public static float FlySeconds(float hop, float span, float min, float max) =>
            Mathf.Lerp(min, Mathf.Max(min, max), Mathf.Clamp01(hop / Mathf.Max(1f, 2f * span)));

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

        public static float MaxDistance(float radius, float floor) => Mathf.Max(floor, 3f * Mathf.Max(0f, radius));

        public static float Reach(float radius, float factor, float pad) => Mathf.Max(0f, radius) * Mathf.Max(1f, factor) + Mathf.Max(0f, pad);

        // Stay near the island: past the reach the focus simply stops, it is never pushed back.
        public static Vector2 ClampToReach(Vector2 pos, Vector2 centre, float reach)
        {
            Vector2 d = pos - centre;
            float len = d.magnitude;
            return len <= reach || len < 1e-4f ? pos : centre + d * (reach / len);
        }
    }
}
