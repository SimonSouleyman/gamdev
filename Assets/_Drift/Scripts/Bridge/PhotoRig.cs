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

    // The watch camera's start framing, as pure maths (2026-09-23 play test: the herd sat at the top edge under the
    // buttons, the camera looked low across the grass and trees filled the picture).
    public static class WatchFraming
    {
        // Tangent of half the shorter picture side's view angle (portrait: the width, landscape: the height).
        public static float ShortHalfTan(float verticalFovDeg, float aspect) =>
            Mathf.Tan(Mathf.Clamp(verticalFovDeg, 1f, 170f) * 0.5f * Mathf.Deg2Rad) * Mathf.Clamp(aspect, 0.05f, 1f);

        // Camera distance at which a subject of `radius` fills `fill` of half the shorter picture side, but never so
        // far that an animal `body` units long is drawn smaller than `minBodyPixels` per 1080 px of that side.
        public static float Distance(float radius, float body, float verticalFovDeg, float aspect, float fill, float minBodyPixels)
        {
            float t = ShortHalfTan(verticalFovDeg, aspect);
            float fit = Mathf.Max(0f, radius) / (Mathf.Clamp(fill, 0.05f, 1f) * t);
            if (body <= 0f || minBodyPixels <= 0f) return fit;
            return Mathf.Min(fit, body * 540f / (t * minBodyPixels));
        }

        // How many degrees the camera tilts up from looking straight at the pivot so the pivot is drawn at
        // `viewportY` (0 bottom, 1 top) instead of the middle.
        public static float LiftDegrees(float verticalFovDeg, float viewportY)
        {
            float t = Mathf.Tan(Mathf.Clamp(verticalFovDeg, 1f, 170f) * 0.5f * Mathf.Deg2Rad);
            return Mathf.Atan((0.5f - Mathf.Clamp01(viewportY)) * 2f * t) * Mathf.Rad2Deg;
        }

        // Middle of the free band between a bottom margin and the lower edge of controls covering the top
        // `topCovered` of the picture (fractions of the picture height).
        public static float FreeBandCenter(float topCovered, float bottomMargin)
        {
            float top = 1f - Mathf.Clamp01(topCovered);
            float bottom = Mathf.Clamp(bottomMargin, 0f, top);
            return (top + bottom) * 0.5f;
        }

        // The preferred yaw first, then alternately to the right and left in growing steps, so a tie keeps the
        // smallest turn.
        public static float CandidateYaw(float preferredDeg, int index, float stepDeg)
        {
            if (index <= 0) return preferredDeg;
            int k = (index + 1) / 2;
            return preferredDeg + ((index & 1) == 1 ? k : -k) * stepDeg;
        }

        // How much the occluders (x/z world position, y world height of the top: trees) block the sight line from
        // a camera at yaw/pitch/distance down to the subject: each one standing between them within the subject's
        // radius and reaching above the line counts up to 1 (less towards the edge of the corridor).
        public static float Occlusion(float yawDeg, float pitchDeg, float distance, Vector3 subject, float radius, Vector3[] occluders, int count)
        {
            if (occluders == null) return 0f;
            float yr = yawDeg * Mathf.Deg2Rad;
            float toCamX = -Mathf.Sin(yr), toCamZ = -Mathf.Cos(yr);
            float pr = Mathf.Clamp(pitchDeg, 1f, 89f) * Mathf.Deg2Rad;
            float horizontal = distance * Mathf.Cos(pr), rise = Mathf.Tan(pr);
            float width = Mathf.Max(0.1f, radius) + 0.35f;
            float cost = 0f;
            count = Mathf.Min(count, occluders.Length);
            for (int i = 0; i < count; i++)
            {
                Vector3 o = occluders[i];
                float rx = o.x - subject.x, rz = o.z - subject.z;
                float along = rx * toCamX + rz * toCamZ;
                if (along < 0.15f || along > horizontal + 0.5f) continue;
                float side = Mathf.Abs(rx * toCamZ - rz * toCamX);
                if (side >= width) continue;
                float poke = o.y - (subject.y + along * rise);
                if (poke <= 0f) continue;
                cost += (1f - side / width) * Mathf.Min(1f, 0.3f + poke * 2f);
            }
            return cost;
        }

        // The candidate yaw with the clearest view; turning away from the preferred yaw costs turnPenalty per
        // half turn, extraCost[i] (optional, e.g. hills in the way) is added to candidate i.
        public static float PickYaw(float preferredDeg, float pitchDeg, float distance, Vector3 subject, float radius,
                                    Vector3[] occluders, int count, int candidates, float turnPenalty, float[] extraCost = null)
        {
            candidates = Mathf.Max(1, candidates);
            float step = 360f / candidates;
            float best = preferredDeg, bestCost = float.MaxValue;
            for (int i = 0; i < candidates; i++)
            {
                float yaw = CandidateYaw(preferredDeg, i, step);
                float cost = Occlusion(yaw, pitchDeg, distance, subject, radius, occluders, count)
                             + turnPenalty * Mathf.Abs(Mathf.DeltaAngle(preferredDeg, yaw)) / 180f;
                if (extraCost != null && i < extraCost.Length) cost += extraCost[i];
                if (cost >= bestCost - 1e-4f) continue;
                bestCost = cost;
                best = yaw;
            }
            return best;
        }
    }
}
