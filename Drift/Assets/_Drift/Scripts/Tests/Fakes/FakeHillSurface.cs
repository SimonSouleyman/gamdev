using System;
using Drift.Core;
using UnityEngine;

namespace Drift.Tests
{
    // A cone: `peak` high in the middle, sea level at `radius`, and the same slope on under water, so there is
    // a ridge to climb, a beach and real shallows (FakeIslandSurface drops off a cliff at its radius).
    public class FakeHillSurface : MonoBehaviour, IIslandSurface
    {
        public float radius = 8f;
        public float peak = 3f;
        public int version;
        public int character;
        public float storm;
        public float speed;

        public Transform SurfaceTransform => transform;
        public Rect LocalBounds => new Rect(-radius, -radius, radius * 2f, radius * 2f);
        public float BoundingRadius => radius;
        public float LandArea => Mathf.PI * radius * radius;
        public int Version => version;
        public int Character => character;
        public int biome;
        public int Biome => biome;
        public float StormIntensity => storm;
        public float sinkDepth;
        public float SinkDepth => sinkDepth;
        public float Speed => speed;

        public float SampleHeight(Vector2 localXZ) => peak * (1f - localXZ.magnitude / radius) - sinkDepth;

        public void ApplyGroundTint(Color[] cellColors, int cellsX, int cellsZ, Vector2 gridOrigin, float gridCell) { }
    }
}
