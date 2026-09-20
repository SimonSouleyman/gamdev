using System;
using UnityEngine;

namespace Drift.Core
{
    public enum LifeTier { Near, Mid, Far }

    // Planar distance from the camera focus, used by every life system to pick its simulation tier.
    // The coordinator (IslandChaseCamera) installs the provider; without one the main camera is used and
    // without a camera everything counts as near, so tests get full behaviour by default.
    public static class LifeLod
    {
        public static Func<Vector3, float> DistanceProvider;

        public static float Distance(Vector3 worldPosition)
        {
            var p = DistanceProvider;
            if (p != null) return p(worldPosition);
            var cam = Camera.main;
            if (cam == null) return 0f;
            Vector3 c = cam.transform.position;
            float dx = c.x - worldPosition.x, dz = c.z - worldPosition.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static LifeTier Tier(float distance, float nearDistance, float midDistance) =>
            distance < nearDistance ? LifeTier.Near : distance < midDistance ? LifeTier.Mid : LifeTier.Far;
    }
}
