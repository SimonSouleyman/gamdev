using UnityEngine;

namespace Drift.Bridge
{
    // One frame of camera input, device-free so it can be injected from tests and eval. Pointer deltas are
    // pixels, the key axes are -1..1 while held (keyYaw +1 = D: the camera travels to its right around the
    // subject, keyPitch +1 = W: higher, keyZoom +1 = Q: closer), zoomFactor multiplies the distance (0 = none).
    public struct OrbitInput
    {
        public float orbitX, orbitY;
        public float panX, panY;
        public float zoomFactor;
        public float keyYaw, keyPitch, keyZoom;
        public bool reset, center;

        public bool Any => orbitX != 0f || orbitY != 0f || panX != 0f || panY != 0f || (zoomFactor > 0f && zoomFactor != 1f)
            || keyYaw != 0f || keyPitch != 0f || keyZoom != 0f || reset || center;
    }

    // The orbit camera shared by photo mode and herd following, as pure state. The rig never stores a world
    // position: the pivot is the subject (handed in every frame) plus a planar offset in world axes, so the
    // camera travels with a drifting island or a wandering herd and the island's body rotation does not matter.
    // Input moves the targets, Step eases the live values towards them (the caller passes unscaled time).
    public sealed class PhotoRig
    {
        public float minPitch = 8f, maxPitch = 85f;
        public float minDistance = 1.5f, maxDistance = 80f;
        // How far the pivot may be panned away from the subject; 0 switches panning off.
        public float panRadius;
        public float orbitDegPerPixel = 0.2f;
        // Pan speed grows with the distance so a drag moves the picture by about the same fraction at every zoom.
        public float panPerPixelPerUnit = 0.0016f;
        public float keyYawSpeed = 80f, keyPitchSpeed = 45f, keyZoomSpeed = 1.2f;
        public float smoothing = 10f;
        public float heightSmoothing = 6f;
        // A camera flight (taking over towards the home orbit, R) starts at this gentler rate and is back at
        // `smoothing` after transitionSeconds, so direct input stays crisp but the big moves do not whip.
        public float transitionSmoothing = 3f;
        public float transitionSeconds = 1.2f;

        public float yaw, pitch = 45f, distance = 12f;
        public Vector2 offset;
        public float height;
        public float yawTarget, pitchTarget = 45f, distanceTarget = 12f;
        public Vector2 offsetTarget;
        public float homeYaw, homePitch = 45f, homeDistance = 12f;
        float _transition = 1f;

        // Camera forward for the current yaw/pitch (positive pitch looks down).
        public Vector3 Direction => Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;

        // Distance limits from the size of the subject: the floor is how close a close-up may get, the far end
        // keeps the whole subject (island or herd) comfortably in frame.
        public static void DistanceLimits(float subjectRadius, float floor, out float min, out float max)
        {
            min = Mathf.Max(0.5f, floor);
            max = Mathf.Max(min * 8f, Mathf.Max(0f, subjectRadius) * 7f + 15f);
        }

        public void SetLimits(float subjectRadius, float floor, float panLimit)
        {
            DistanceLimits(subjectRadius, floor, out minDistance, out maxDistance);
            panRadius = Mathf.Max(0f, panLimit);
            ClampTargets();
        }

        void ClampTargets()
        {
            pitchTarget = Mathf.Clamp(pitchTarget, minPitch, maxPitch);
            distanceTarget = Mathf.Clamp(distanceTarget, minDistance, maxDistance);
            offsetTarget = Vector2.ClampMagnitude(offsetTarget, panRadius);
        }

        // Takes over a camera that already stands somewhere: the live values reproduce that position around
        // the subject exactly (no jump, the offset starts at zero), the targets are the same orbit within the limits.
        public void FromPose(Vector3 cameraPosition, Vector3 subject)
        {
            offset = offsetTarget = Vector2.zero;
            height = subject.y;
            OrbitOf(subject - cameraPosition, ref yaw, ref pitch, ref distance);
            yawTarget = yaw;
            pitchTarget = pitch;
            distanceTarget = distance;
            _transition = 1f;
            ClampTargets();
        }

        // Yaw, pitch and distance of a camera that looks along toSubject; a degenerate vector leaves them alone.
        public static void OrbitOf(Vector3 toSubject, ref float yawDeg, ref float pitchDeg, ref float dist)
        {
            if (toSubject.sqrMagnitude < 1e-6f) return;
            dist = toSubject.magnitude;
            Vector3 d = toSubject / dist;
            pitchDeg = Mathf.Asin(Mathf.Clamp(-d.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (Mathf.Abs(d.x) + Mathf.Abs(d.z) > 1e-5f) yawDeg = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        // Where R brings the camera back to.
        public void SetHome(float yawDeg, float pitchDeg, float dist)
        {
            homeYaw = yawDeg;
            homePitch = Mathf.Clamp(pitchDeg, minPitch, maxPitch);
            homeDistance = Mathf.Clamp(dist, minDistance, maxDistance);
        }

        public void HomeFromTargets() => SetHome(yawTarget, pitchTarget, distanceTarget);

        public void GoHome()
        {
            // The short way round, however many turns the player has made.
            yawTarget = yaw + Mathf.DeltaAngle(yaw, homeYaw);
            pitchTarget = homePitch;
            distanceTarget = homeDistance;
            offsetTarget = Vector2.zero;
            ClampTargets();
            _transition = 0f;
        }

        public void Center() => offsetTarget = Vector2.zero;

        public void Orbit(float dxPixels, float dyPixels)
        {
            yawTarget += dxPixels * orbitDegPerPixel;
            pitchTarget = Mathf.Clamp(pitchTarget + dyPixels * orbitDegPerPixel, minPitch, maxPitch);
        }

        public void Zoom(float factor)
        {
            if (factor > 0f) distanceTarget = Mathf.Clamp(distanceTarget * factor, minDistance, maxDistance);
        }

        // Drags the pivot across the ground plane along the camera's flattened right and forward axes; the
        // offset is relative to the subject and never leaves panRadius.
        public void Pan(float dxPixels, float dyPixels)
        {
            if (panRadius <= 0f) return;
            float rad = yaw * Mathf.Deg2Rad;
            Vector2 right = new Vector2(Mathf.Cos(rad), -Mathf.Sin(rad));
            Vector2 forward = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            float k = distance * panPerPixelPerUnit;
            offsetTarget = Vector2.ClampMagnitude(offsetTarget - right * (dxPixels * k) - forward * (dyPixels * k), panRadius);
        }

        public void Step(in OrbitInput input, float dt)
        {
            dt = Mathf.Max(0f, dt);
            if (input.reset) GoHome();
            if (input.center) Center();
            if (input.orbitX != 0f || input.orbitY != 0f) Orbit(input.orbitX, input.orbitY);
            if (input.keyYaw != 0f) yawTarget -= Mathf.Clamp(input.keyYaw, -1f, 1f) * keyYawSpeed * dt;
            if (input.keyPitch != 0f) pitchTarget = Mathf.Clamp(pitchTarget + Mathf.Clamp(input.keyPitch, -1f, 1f) * keyPitchSpeed * dt, minPitch, maxPitch);
            if (input.zoomFactor > 0f) Zoom(input.zoomFactor);
            if (input.keyZoom != 0f) Zoom(Mathf.Exp(-Mathf.Clamp(input.keyZoom, -1f, 1f) * keyZoomSpeed * dt));
            if (input.panX != 0f || input.panY != 0f) Pan(input.panX, input.panY);
            if (panRadius <= 0f) offsetTarget = Vector2.zero;

            _transition = Mathf.Min(1f, _transition + dt / Mathf.Max(0.01f, transitionSeconds));
            float k = 1f - Mathf.Exp(-Mathf.Lerp(transitionSmoothing, smoothing, _transition * _transition) * dt);
            yaw = Mathf.Lerp(yaw, yawTarget, k);
            pitch = Mathf.Lerp(pitch, pitchTarget, k);
            distance = Mathf.Lerp(distance, distanceTarget, k);
            offset = Vector2.Lerp(offset, offsetTarget, k);
        }

        // The ground height under the pivot is eased so panning across a cliff does not kick the camera.
        public void EaseHeight(float groundY, float dt) => height = Mathf.Lerp(height, groundY, 1f - Mathf.Exp(-heightSmoothing * Mathf.Max(0f, dt)));

        public Vector2 PivotPlanar(Vector3 subject) => new Vector2(subject.x + offset.x, subject.z + offset.y);

        public Vector3 Pivot(Vector3 subject) => new Vector3(subject.x + offset.x, height, subject.z + offset.y);

        public void Pose(Vector3 subject, out Vector3 position, out Quaternion rotation)
        {
            Vector3 dir = Direction;
            position = Pivot(subject) - dir * distance;
            rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        // 0 far away, 1 at close-up distance: drives the near clip plane and the lower ground clearance.
        public static float CloseBlend(float distance) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 5.5f, distance));
    }
}
