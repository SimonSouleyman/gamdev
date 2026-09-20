using System;
using System.Collections.Generic;
using Drift.Islands;
using UnityEngine;

namespace Drift.Audio
{
    public sealed class TensionTracker
    {
        public float HorizonSeconds = 8f;
        public float MinClosing = 0.3f;
        public float AttackSeconds = 0.15f;
        public float ReleaseSeconds = 2f;

        public float Raw { get; private set; }
        public float Value { get; private set; }

        public static float Compute(float ax, float ay, float avx, float avy, float ar,
                                    float bx, float by, float bvx, float bvy, float br,
                                    float horizonSeconds = 8f, float minClosing = 0.3f)
        {
            float dx = bx - ax, dy = by - ay;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            if (dist < 1e-4f) return 1f;
            float nx = dx / dist, ny = dy / dist;
            float closing = (avx - bvx) * nx + (avy - bvy) * ny;
            if (closing < minClosing) return 0f;
            float gap = dist - (ar + br);
            if (gap <= 0f) return 1f;
            float ttc = gap / Math.Max(closing, 0.1f);
            float t = 1f - ttc / horizonSeconds;
            return t < 0f ? 0f : (t > 1f ? 1f : t);
        }

        public static float Compute(Vector2 aPos, Vector2 aVel, float aRadius, Vector2 bPos, Vector2 bVel, float bRadius,
                                    float horizonSeconds = 8f, float minClosing = 0.3f) =>
            Compute(aPos.x, aPos.y, aVel.x, aVel.y, aRadius, bPos.x, bPos.y, bVel.x, bVel.y, bRadius, horizonSeconds, minClosing);

        public float Update(float raw, float dt)
        {
            Raw = raw;
            float tau = raw > Value ? AttackSeconds : ReleaseSeconds;
            Value += (raw - Value) * SynthMath.SmoothCoef(tau, dt);
            if (Value < 1e-4f) Value = 0f;
            return Value;
        }

        public void Reset()
        {
            Raw = 0f;
            Value = 0f;
        }

        public float EvaluateRaw(Island player, IReadOnlyList<Island> all)
        {
            if (player == null || all == null) return 0f;
            Vector2 pPos = player.PlanarPosition, pVel = player.PlanarVelocity;
            float pR = player.BoundingRadius;
            float best = 0f;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (o == null || ReferenceEquals(o, player) || o.IsSunk || o.IsEmerging) continue;
                float t = Compute(pPos, pVel, pR, o.PlanarPosition, o.PlanarVelocity, o.BoundingRadius, HorizonSeconds, MinClosing);
                if (t > best) best = t;
            }
            return best;
        }

        public float Evaluate(Island player, IReadOnlyList<Island> all, float dt) => Update(EvaluateRaw(player, all), dt);
    }
}
