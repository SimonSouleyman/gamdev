using System;
using System.Collections.Generic;
using Drift.Islands;
using Drift.Tectonics;
using Drift.Visuals;
using UnityEngine;

namespace Drift.EditorTools.Playtest
{
    // A player with human limits: it re-plans a few times a second, its hands follow the plan only after a
    // reaction delay, the stick wobbles and now and then its attention lapses. It steers through
    // SessionScreens.ScreenOverride, i.e. a SCREEN direction mapped by the game exactly like stick and tilt.
    public class HumanBot
    {
        public string name = "bot";
        public float reaction = 0.25f;
        public float noise = 0.08f;
        public float decideEvery = 0.12f;
        public float lapseEvery = 0f;
        public float lapseLength = 0.5f;
        public float lookahead = 40f;

        // World XZ direction the player wants (length 0..1); null = hands off.
        public Func<Vector2> plan;

        readonly Queue<(float t, Vector2 v)> _hands = new();
        Vector2 _out, _planned;
        float _nextDecide, _nextLapse, _lapseLeft;
        int _frame = -1;
        System.Random _rnd;

        public static HumanBot Good() => new HumanBot { name = "good", reaction = 0.2f, noise = 0.05f, decideEvery = 0.1f, lookahead = 45f };
        public static HumanBot Poor() => new HumanBot { name = "poor", reaction = 0.45f, noise = 0.2f, decideEvery = 0.25f, lapseEvery = 7f, lapseLength = 0.7f, lookahead = 22f };
        public static HumanBot Casual() => new HumanBot { name = "casual", reaction = 0.3f, noise = 0.1f, decideEvery = 0.2f, lookahead = 30f };

        public void Reset(int seed)
        {
            _rnd = new System.Random(seed);
            _hands.Clear();
            _out = _planned = Vector2.zero;
            _nextDecide = 0f;
            _nextLapse = lapseEvery > 0f ? lapseEvery * (0.5f + (float)_rnd.NextDouble()) : float.MaxValue;
            _lapseLeft = 0f;
        }

        float R() => (float)_rnd.NextDouble() * 2f - 1f;

        // Screen direction for this frame; cameraYawDeg = where the camera looks (what the player sees).
        public Vector2 Screen(float now, float dt, float cameraYawDeg)
        {
            if (_frame == Time.frameCount) return _out;
            _frame = Time.frameCount;
            if (_rnd == null) Reset(1);
            if (now >= _nextLapse)
            {
                _lapseLeft = lapseLength * (0.6f + 0.8f * (float)_rnd.NextDouble());
                _nextLapse = now + lapseEvery * (0.5f + (float)_rnd.NextDouble());
            }
            if (_lapseLeft > 0f) _lapseLeft -= dt;
            else if (now >= _nextDecide)
            {
                _nextDecide = now + decideEvery;
                Vector2 w = plan != null ? plan() : Vector2.zero;
                Vector2 s = ToScreen(w, cameraYawDeg);
                if (s.sqrMagnitude > 1e-4f) s += new Vector2(R(), R()) * noise;
                _planned = Vector2.ClampMagnitude(s, 1f);
                _hands.Enqueue((now + reaction, _planned));
            }
            while (_hands.Count > 0 && _hands.Peek().t <= now) _out = _hands.Dequeue().v;
            return _out;
        }

        // Inverse of TiltMath.ToWorld.
        public static Vector2 ToScreen(Vector2 world, float yawDeg)
        {
            float rad = yawDeg * Mathf.Deg2Rad;
            float sin = Mathf.Sin(rad), cos = Mathf.Cos(rad);
            return new Vector2(cos * world.x - sin * world.y, sin * world.x + cos * world.y);
        }

        // ------------------------------------------------------------------ plans

        // Cozy: head for the nearest island to absorb (stops within arrive distance of nothing).
        public static Func<Vector2> Explorer(Island player, float maxArea = float.MaxValue)
        {
            return () =>
            {
                if (player == null) return Vector2.zero;
                Vector2 p = player.PlanarPosition;
                Island best = null;
                float bestD = float.MaxValue;
                var all = Island.All;
                for (int i = 0; i < all.Count; i++)
                {
                    var o = all[i];
                    if (o == null || o == player || o.LandArea <= 0.5f || o.LandArea > maxArea || o.IsEmerging) continue;
                    float d = (o.PlanarPosition - p).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = o; }
                }
                if (best == null) return Vector2.zero;
                Vector2 to = best.PlanarPosition - p;
                return to.sqrMagnitude > 1e-4f ? to.normalized : Vector2.zero;
            };
        }

        // Race: pick the lateral line with the least danger inside the lookahead, reachable in time, and brake
        // if nothing is clear right in front.
        public static Func<Vector2> Racer(Island player, HumanBot bot, ShipSystem ships)
        {
            return () =>
            {
                var ring = RingWorld.Active;
                if (player == null || ring == null || !player.AdventureRacing) return Vector2.zero;
                var geo = ring.Geometry;
                Vector2 p = player.PlanarPosition;
                Vector2 track = player.TrackDirection;
                Vector2 across = new Vector2(track.y, -track.x);
                float sign = track.y >= 0f ? 1f : -1f;
                float v = Mathf.Max(1f, player.TrackSpeed);
                float steerV = Mathf.Max(0.5f, player.AdventureSteerSpeed);
                // Collisions and the game's own dodge count use the whole island with its shallow rim.
                float pr = player.BoundingRadius * 0.85f;
                float margin = pr + 1.5f;
                float lo = geo.MinX + margin, hi = geo.MaxX - margin;
                const int N = 17;
                float bestX = p.x, bestScore = float.MaxValue, hereDanger = 0f, nearestThreat = float.MaxValue;
                for (int c = 0; c < N; c++)
                {
                    float xc = Mathf.Lerp(lo, hi, c / (N - 1f));
                    float danger = 0f, reward = 0f;
                    void Consider(Vector2 pos, float r, bool good, float velX = 0f)
                    {
                        float along = geo.AlongDelta(pos.y, p.y) * sign;
                        if (along < -r || along > bot.lookahead) return;
                        float tt = Mathf.Max(0f, along) / v;
                        float xAt = p.x + Mathf.Clamp(xc - p.x, -steerV * tt, steerV * tt);
                        float gap = Mathf.Abs(pos.x + velX * tt - xAt) - r - pr;
                        if (good) { if (gap < 1.5f) reward += 1f / (1f + along * 0.05f); return; }
                        if (gap < 2f)
                        {
                            float w = (2f - gap) / (1f + tt);
                            danger += w;
                            if (c == 0) nearestThreat = Mathf.Min(nearestThreat, along);
                        }
                    }
                    var all = Island.All;
                    for (int i = 0; i < all.Count; i++)
                    {
                        var o = all[i];
                        if (o == null || o == player || o.LandArea <= 0f) continue;
                        Consider(o.PlanarPosition, o.BoundingRadius * 0.85f, false, o.PlanarVelocity.x);
                    }
                    if (ships != null)
                    {
                        for (int i = 0; i < ships.ShipSlots; i++)
                            if (ships.ShipActive(i)) Consider(ships.ShipPosition(i), 2f, false);
                        for (int i = 0; i < ships.FlotsamSlots; i++)
                            if (ships.FlotsamActive(i) && ShipSystem.IsCollectible(ships.FlotsamKindOf(i)))
                                Consider(ships.FlotsamPosition(i), 0.5f, true);
                    }
                    var storms = StormSystem.Instance;
                    if (storms != null)
                        for (int i = 0; i < storms.Storms.Count; i++)
                            if (storms.Storms[i].IsRing) Consider(storms.Storms[i].center, storms.Storms[i].radius * 0.4f, false);
                    float score = danger * 10f - reward * 2f + Mathf.Abs(xc - p.x) * 0.05f;
                    if (Mathf.Abs(xc - p.x) < (hi - lo) / (N - 1f)) hereDanger = Mathf.Max(hereDanger, danger);
                    if (score < bestScore) { bestScore = score; bestX = xc; }
                }
                float lateral = Mathf.Clamp((bestX - p.x) * across.x / 2.5f, -1f, 1f);
                float brake = bestScore > 8f ? -0.8f : 0f;
                return across * lateral + track * brake;
            };
        }
    }
}
