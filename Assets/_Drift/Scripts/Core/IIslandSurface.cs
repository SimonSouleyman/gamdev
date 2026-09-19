using System;
using UnityEngine;

namespace Drift.Core
{
    public interface IIslandSurface
    {
        Transform SurfaceTransform { get; }
        Rect LocalBounds { get; }
        float BoundingRadius { get; }
        float LandArea { get; }
        int Version { get; }
        float SampleHeight(Vector2 localXZ);
        void ApplyGroundTint(Func<Vector2, Color> tintAtLocal);
    }
}
