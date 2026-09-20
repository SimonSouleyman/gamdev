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
        public float followLerp = 5f;
        public float lookLerp = 7f;
        public float referenceRadius = 3f;
        public float zoomExponent = 0.85f;
        public float shakeAmount = 0.12f;
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

        public float Zoom => _zoom;
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
            _zoomTarget = _zoom = Mathf.Clamp(z, zoomMin, zoomMax);
        }

        public float ZoomTarget => _zoomTarget;

        // Eases to an absolute zoom (SetZoom jumps).
        public void ZoomTo(float z)
        {
            _zoomTarget = Mathf.Clamp(z, zoomMin, zoomMax);
        }

        public void ZoomBy(float factor)
        {
            _zoomTarget = Mathf.Clamp(_zoomTarget * factor, zoomMin, zoomMax);
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
            _zoomTarget = Mathf.Clamp(_zoomTarget, zoomMin, zoomMax);
            _zoom = Mathf.Lerp(_zoom, _zoomTarget, 1f - Mathf.Exp(-zoomSmooth * dt));
        }

        void OnEnable()
        {
            Island.Impact += OnImpact;
            LifeLod.DistanceProvider = PlanarDistance;
            LifeEnvironment.ViewDistanceProvider = ViewDistance;
            LifeEnvironment.PointOfInterest = NearestOtherIsland;
        }

        void OnDisable()
        {
            Island.Impact -= OnImpact;
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

        void LateUpdate()
        {
            if (target == null) return;
            if (Suspended)
            {
                ApplyNearClip(Mathf.Clamp01(SuspendedClose));
                _hasLastFocus = false;
                _easeT = 1f;
                return;
            }

            if (Application.isPlaying) ReadZoomInput(Time.deltaTime);

            DesiredPose(out Vector3 desiredPos, out Quaternion desiredRot, out float scale, out Vector3 focus);

            if (_easeT < 1f)
            {
                // Capped so a hitch on the hand-back frame cannot turn the ease into a snap.
                _easeT = Mathf.Min(1f, _easeT + Mathf.Min(Time.unscaledDeltaTime, 0.05f) / Mathf.Max(0.01f, _easeSeconds));
                EasePose(focus + _easeFromOffset, _easeFromRot, desiredPos, desiredRot, _easeT, out Vector3 easedPos, out Quaternion easedRot);
                transform.SetPositionAndRotation(easedPos, easedRot);
                _lastFocus = focus;
                _hasLastFocus = true;
                ApplyNearClip(Mathf.Lerp(_easeFromClose, CloseBlend(_zoom), Mathf.SmoothStep(0f, 1f, _easeT)));
                return;
            }

            // Close up the follow lag (speed / followLerp, over a unit at full speed) is larger than the
            // camera distance, so the camera is carried along with the focus and only the offset is smoothed.
            float close = CloseBlend(_zoom);
            if (_hasLastFocus && close > 0f)
            {
                Vector3 carried = focus - _lastFocus;
                if (carried.sqrMagnitude < 25f) transform.position += carried * close;
            }
            _lastFocus = focus;
            _hasLastFocus = true;
            ApplyNearClip(close);

            float posT = 1f - Mathf.Exp(-followLerp * Time.deltaTime);
            float rotT = 1f - Mathf.Exp(-lookLerp * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desiredPos, posT);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, rotT);

            if (_shake > 0.001f)
            {
                float t = Time.time * 28f;
                Vector3 offset = new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, Mathf.PerlinNoise(t, t) - 0.5f);
                transform.position += offset * (_shake * shakeAmount * scale);
                _shake *= Mathf.Exp(-shakeDecay * Time.deltaTime);
            }
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

            // back = -heading: the body may turn under the camera (merges, drift), the view never does.
            FollowPose(focus, ground.Normal, -ground.Forward, radius, referenceRadius, zoomExponent, height, distanceBehind, _zoom, out pos, out rot, out scale);

            Vector3 look = focus + ground.Normal * (0.4f * _zoom);
            float need = RequiredHeight(ground, pos, look, Mathf.Lerp(groundClearance, closeGroundClearance, close), close);
            if (pos.y < need)
            {
                pos.y = need;
                rot = Quaternion.LookRotation((look - pos).normalized, ground.Normal);
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
            scale = Mathf.Pow(Mathf.Max(1f, radius / Mathf.Max(0.01f, referenceRadius)), zoomExponent);
            scale = Mathf.Lerp(scale, 1f, close);
            float shrink = Mathf.Lerp(1f, 0.5f, close);
            Vector3 lookAt = focus + up * (0.4f * zoom);
            pos = focus + up * (height * scale * zoom * shrink) + back * (distanceBehind * scale * Mathf.Pow(zoom, 0.65f) * shrink);
            rot = Quaternion.LookRotation((lookAt - pos).normalized, up);
        }

        // Jumps to the follow pose without smoothing (after a load or a world reset).
        public void SnapToTarget()
        {
            if (target == null) return;
            DesiredPose(out Vector3 pos, out Quaternion rot, out _, out Vector3 focus);
            transform.position = pos;
            transform.rotation = rot;
            _lastFocus = focus;
            _hasLastFocus = true;
            ApplyNearClip(CloseBlend(_zoom));
            _shake = 0f;
            _easeT = 1f;
        }
    }
}
