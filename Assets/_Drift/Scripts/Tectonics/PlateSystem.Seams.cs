using UnityEngine;

namespace Drift.Tectonics
{
    // The visible plate seam: a meandering curve over the straight Voronoi chord of a Border. This file is the one
    // source of truth for it - the ribbon mesh, tectonic events, storms and volcanoes all sit on SeamPoint.
    //   point(t) = lerp(p0, p1, t) + normal * amplitude * taper(t) * Meander(s),   s = s0 + t * length
    // s is the along coordinate relative to the midpoint of the two plates, so the bends stay put on the pair when
    // a junction (an end of the chord) wanders. The taper is 0 with zero slope at both ends, so the three borders
    // of a triple junction still meet in exactly one point. `normal`, the direction of t and the noise key come
    // from the wrapped core ids of the pair: identical in every copy of the torus, independent of a / b order.
    // Which plate carries an island still follows the straight chord; amplitude <= seamMeanderMax keeps the
    // ribbon within about one small island of it.
    public partial class PlateSystem
    {
        const float MeanderPeriod = 90f;
        const float WobblePeriod = 45f;
        const float WobbleFrequency = 1.93f;
        const float WobbleShare = 0.22f;
        const float TaperLength = 15f;

        public float PlateTime => _time;

        static int CoreOrder(Plate p, Plate q)
        {
            Vector2Int i = p.core.id, j = q.core.id;
            if (i.x != j.x) return i.x < j.x ? -1 : 1;
            if (i.y != j.y) return i.y < j.y ? -1 : 1;
            if (p.cell.x != q.cell.x) return p.cell.x < q.cell.x ? -1 : 1;
            return p.cell.y < q.cell.y ? -1 : p.cell.y > q.cell.y ? 1 : 0;
        }

        // e0 / e1 and pa / pb in any order: the curve only depends on the pair.
        public Border MakeBorder(Plate pa, Plate pb, Vector2 e0, Vector2 e1, bool open0, bool open1, BoundaryKind kind, float closing)
        {
            bool swap = CoreOrder(pa, pb) > 0;
            Plate lo = swap ? pb : pa, hi = swap ? pa : pb;
            Vector2 n = (hi.position - lo.position).normalized;
            Vector2 tan = new Vector2(n.y, -n.x);
            Vector2 mid = (pa.position + pb.position) * 0.5f;
            float s0 = Vector2.Dot(e0 - mid, tan), s1 = Vector2.Dot(e1 - mid, tan);
            if (s1 < s0)
            {
                (e0, e1) = (e1, e0);
                (s0, s1) = (s1, s0);
                (open0, open1) = (open1, open0);
            }
            float len = Mathf.Max(s1 - s0, 0f);

            // An end cut off by the border window is not a junction: no taper there, and the real border is longer
            // than the piece in the window, so its bends get the full amplitude (both only matter beyond the fade).
            bool open = open0 || open1;
            float amp = Mathf.Max(0f, open ? seamMeanderMax : Mathf.Min(seamMeanderMax, seamMeander * len));
            float edge = Mathf.Min(open ? len : len * 0.5f, Mathf.Max(TaperLength * 0.6f, Mathf.Min(TaperLength, 2.5f * amp)));

            int key = Hash(Hash(seed, lo.core.id.x, lo.core.id.y), hi.core.id.x, hi.core.id.y);
            return new Border
            {
                a = pa, b = pb, p0 = e0, p1 = e1, kind = kind, closing = closing,
                normal = n, s0 = s0, length = len,
                amplitude = len > 1e-3f ? amp : 0f,
                taper = edge > 1e-3f ? len / edge : 0f,
                freq = 1f / Mathf.Lerp(26f, 38f, Hash01(key, 11, 0)),
                phase = 64f * Hash01(key, 12, 0),
                key = key, open0 = open0, open1 = open1
            };
        }

        public static float Hash01(int k, int i, int j)
        {
            unchecked
            {
                uint h = (uint)(k + i * 73856093 + j * 19349663);
                h ^= h >> 16; h *= 0x7feb352du;
                h ^= h >> 15; h *= 0x846ca68bu;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / 16777216f;
            }
        }

        static float Smooth(float x)
        {
            x = x < 0f ? 0f : x > 1f ? 1f : x;
            return x * x * (3f - 2f * x);
        }

        static float Quintic(float x) => x * x * x * (x * (x * 6f - 15f) + 10f);

        static int Floor(float x)
        {
            int i = (int)x;
            return x < i ? i - 1 : i;
        }

        // -1..1. A lazy meander whose bends alternate sides at every lattice point (so it never goes straight and
        // never swims: over time only the depth of each bend breathes, 0.3..1) plus a smaller free wobble.
        public static float Meander(int key, float x, float time)
        {
            float tt = time / MeanderPeriod, tw = time / WobblePeriod;
            int j = Floor(tt), jw = Floor(tw);
            return Meander(key, x, j, Smooth(tt - j), jw, Smooth(tw - jw));
        }

        static float Meander(int key, float x, int j, float g, int jw, float gw)
        {
            int i = Floor(x);
            float f = Quintic(x - i);
            float a0 = Hash01(key, i, j), a1 = Hash01(key, i + 1, j);
            a0 += (Hash01(key, i, j + 1) - a0) * g;
            a1 += (Hash01(key, i + 1, j + 1) - a1) * g;
            float sign = (i & 1) == 0 ? 1f : -1f;
            float v0 = sign * (0.3f + 0.7f * a0), v1 = -sign * (0.3f + 0.7f * a1);
            float big = v0 + (v1 - v0) * f;

            float xw = x * WobbleFrequency + 17.3f;
            int iw = Floor(xw);
            float fw = Quintic(xw - iw);
            int kw = key ^ 0x5bd1e995;
            float w0 = Hash01(kw, iw, jw), w1 = Hash01(kw, iw + 1, jw);
            w0 += (Hash01(kw, iw, jw + 1) - w0) * gw;
            w1 += (Hash01(kw, iw + 1, jw + 1) - w1) * gw;
            float wob = (w0 + (w1 - w0) * fw) * 2f - 1f;

            return (1f - WobbleShare) * big + WobbleShare * wob;
        }

        // -1..1, pinch and swell of the ribbon along the seam.
        static float WidthNoise(int key, float x)
        {
            int i = Floor(x);
            float f = x - i;
            f = f * f * (3f - 2f * f);
            int k = key ^ 0x2545f491;
            float h0 = Hash01(k, i, 0);
            return (h0 + (Hash01(k, i + 1, 0) - h0) * f) * 2f - 1f;
        }

        float _meanderTime = float.NaN, _meanderG, _wobbleG;
        int _meanderJ, _wobbleJ;

        // Signed distance of the seam from its chord along b.normal at t (0 = p0, 1 = p1).
        public float SeamOffset(in Border b, float t)
        {
            if (b.amplitude <= 0f) return 0f;
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            float k = 1f;
            if (!b.open0) k *= Smooth(t * b.taper);
            if (!b.open1) k *= Smooth((1f - t) * b.taper);
            if (k <= 0f) return 0f;
            if (_meanderTime != _time)
            {
                _meanderTime = _time;
                float tt = _time / MeanderPeriod, tw = _time / WobblePeriod;
                _meanderJ = Floor(tt);
                _wobbleJ = Floor(tw);
                _meanderG = Smooth(tt - _meanderJ);
                _wobbleG = Smooth(tw - _wobbleJ);
            }
            return b.amplitude * k * Meander(b.key, (b.s0 + t * b.length) * b.freq + b.phase, _meanderJ, _meanderG, _wobbleJ, _wobbleG);
        }

        public Vector2 SeamPoint(in Border b, float t) => b.p0 + (b.p1 - b.p0) * t + b.normal * SeamOffset(b, t);

        Vector2 SeamDerivative(in Border b, float t)
        {
            float h = 0.5f / Mathf.Max(b.length, 1f);
            return (SeamPoint(b, t + h) - SeamPoint(b, t - h)) / (2f * h);
        }

        // Unit tangent, always pointing from p0 to p1.
        public Vector2 SeamTangent(in Border b, float t)
        {
            Vector2 d = SeamDerivative(b, t);
            float m = d.magnitude;
            return m > 1e-5f ? d / m : new Vector2(b.normal.y, -b.normal.x);
        }

        // Arc length of the curve (>= the chord b.length).
        public float SeamLength(in Border b)
        {
            int n = Mathf.Clamp(Mathf.CeilToInt(b.length / 3f), 1, 64);
            float sum = 0f;
            Vector2 prev = SeamPoint(b, 0f);
            for (int i = 1; i <= n; i++)
            {
                Vector2 p = SeamPoint(b, (float)i / n);
                sum += (p - prev).magnitude;
                prev = p;
            }
            return sum;
        }

        // t after walking `distance` units of arc length along the seam from t (negative = towards p0). Unclamped:
        // a result outside 0..1 means the walk left the border.
        public float SeamAdvance(in Border b, float t, float distance)
        {
            int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(distance) / 2f), 1, 64);
            float ds = distance / n;
            for (int i = 0; i < n; i++)
                t += ds / Mathf.Max(SeamDerivative(b, t).magnitude, 1e-3f);
            return t;
        }

        // Closest point q = SeamPoint(b, t) of the seam to p. False when p lies past an end of the border (t is
        // then clamped to that end).
        public bool ClosestOnSeam(in Border b, Vector2 p, out float t, out Vector2 q)
        {
            Vector2 chord = b.p1 - b.p0;
            float raw = Vector2.Dot(p - b.p0, chord) / Mathf.Max(chord.sqrMagnitude, 1e-6f);
            t = Mathf.Clamp01(raw);
            for (int it = 0; it < 4; it++)
            {
                Vector2 d = SeamDerivative(b, t);
                float step = Vector2.Dot(p - SeamPoint(b, t), d) / Mathf.Max(d.sqrMagnitude, 1e-6f);
                raw = t + step;
                t = Mathf.Clamp01(raw);
                if (Mathf.Abs(step) * b.length < 0.005f) break;
            }
            q = SeamPoint(b, t);
            return raw >= 0f && raw <= 1f;
        }

        // The border whose seam passes closest to pos (within `within` units).
        public bool NearestSeam(Vector2 pos, float within, out Border seam, out float t, out Vector2 q)
        {
            seam = default;
            t = 0f;
            q = pos;
            float best = within;
            bool found = false;
            for (int i = 0; i < Borders.Count; i++)
            {
                var b = Borders[i];
                if (ChordDistance(b, pos) > best + b.amplitude) continue;
                ClosestOnSeam(b, pos, out float bt, out Vector2 bq);
                float d = Vector2.Distance(pos, bq);
                if (d >= best) continue;
                best = d;
                seam = b;
                t = bt;
                q = bq;
                found = true;
            }
            return found;
        }
    }
}
