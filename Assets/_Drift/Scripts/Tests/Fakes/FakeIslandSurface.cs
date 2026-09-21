using System;
using Drift.Core;
using UnityEngine;

namespace Drift.Tests
{
    public class FakeIslandSurface : MonoBehaviour, IIslandSurface
    {
        public float radius = 6f;
        public float height = 1f;
        // Width of a sloping beach inside the radius: the ground falls from height to 0 at the radius over this
        // distance, so a shore band (Phase 4 crabs, turtles) exists; 0 keeps the old flat-top island.
        public float beach = 0f;
        public int version;
        public int character;
        public int tintCalls;

        public Transform SurfaceTransform => transform;
        public Rect LocalBounds => new Rect(-radius, -radius, radius * 2f, radius * 2f);
        public float BoundingRadius => radius;
        public float LandArea => Mathf.PI * radius * radius;
        public int Version => version;
        public int Character => character;
        public int biome;
        public int Biome => biome;
        public float storm;
        public float StormIntensity => storm;
        public float speed;
        public float Speed => speed;

        public float SampleHeight(Vector2 localXZ)
        {
            float r = localXZ.magnitude;
            if (r >= radius) return -0.8f;
            if (beach <= 0f || r <= radius - beach) return height;
            return Mathf.Lerp(height, 0f, (r - (radius - beach)) / beach);
        }

        public void ApplyGroundTint(Color[] cellColors, int cellsX, int cellsZ, Vector2 gridOrigin, float gridCell)
        {
            tintCalls++;
        }
    }
}
