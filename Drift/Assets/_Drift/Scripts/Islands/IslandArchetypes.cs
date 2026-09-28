using UnityEngine;

namespace Drift.Islands
{
    // Shape families of the streamed world. Every generator is a pure function of (archetype, radius, seed):
    // "radius" is the nominal half extent the streamer spaces islands by, the land area is
    // AreaFactor * pi * radius^2. Heights stay inside what the life systems and the terrain shader expect:
    // beach below 0.35, tree line 1.9, rock from 2.0, never snow (3.4, reserved for volcano cones).
    public static class IslandArchetypes
    {
        // Floor of lagoons, sandbank shelves and the water between cluster islets: above the terrain clip
        // (-0.45, so it shows as turquoise shallows) and below Island.DetectContact's shelf threshold
        // (-0.3, so sailing over it is no collision).
        public const float Shallow = -0.4f;
        public const float MaxPeak = 3.2f;
        public const float FineCellBelowRadius = 3f;

        public static float CellSize(float radius) => radius < FineCellBelowRadius ? 0.25f : 0.5f;

        // Mean land area / (pi * radius^2), measured over 25-40 seeds at radius 1.5, 4 and 8 (single islands
        // scatter by about +-25 %; Tests/IslandArchetypeTests keeps the table honest).
        public static float AreaFactor(IslandArchetype a)
        {
            switch (a)
            {
                case IslandArchetype.Blob: return 0.85f;
                case IslandArchetype.Ridge: return 0.49f;
                case IslandArchetype.Crescent: return 0.50f;
                case IslandArchetype.TwinPeak: return 0.59f;
                case IslandArchetype.Sandbank: return 0.44f;
                case IslandArchetype.Mesa: return 0.89f;
                case IslandArchetype.Archipelago: return 0.40f;
                case IslandArchetype.Stack: return 0.59f;
                default: return 0.79f;
            }
        }

        static float Extent(IslandArchetype a)
        {
            switch (a)
            {
                case IslandArchetype.Ridge: return 1.9f;
                case IslandArchetype.Sandbank: return 2.0f;
                case IslandArchetype.Crescent: return 1.5f;
                default: return 1.75f;
            }
        }

        public static IslandShape Create(IslandArchetype type, float radius, int seed, float cell, float heightScale = 1f)
        {
            if (type == IslandArchetype.Classic) return IslandShape.CreateBlob(radius, seed, cell, heightScale);

            var g = new Gen(type, radius, seed, heightScale);
            float ext = Extent(type) * radius + 1f;
            int n = Mathf.CeilToInt(2f * ext / cell) + 1;
            var origin = new Vector2(-(n - 1) * cell * 0.5f, -(n - 1) * cell * 0.5f);
            var s = new IslandShape(cell, n, n, origin);
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                    s.h[j * n + i] = Mathf.Min(g.Height(s.CellPos(i, j)), MaxPeak);

            s = Crop(s, 2);
            // Bodies turn about their local origin: put it on the land centroid (a crescent's is off-centre).
            s.origin -= s.LandCentroid();
            return s;
        }

        static IslandShape Crop(IslandShape s, int pad)
        {
            int i0 = s.nx, i1 = -1, j0 = s.nz, j1 = -1;
            for (int j = 0; j < s.nz; j++)
                for (int i = 0; i < s.nx; i++)
                {
                    if (s.h[j * s.nx + i] <= IslandShape.Sea + 0.01f) continue;
                    if (i < i0) i0 = i;
                    if (i > i1) i1 = i;
                    if (j < j0) j0 = j;
                    if (j > j1) j1 = j;
                }
            if (i1 < 0) return s;
            i0 = Mathf.Max(0, i0 - pad); j0 = Mathf.Max(0, j0 - pad);
            i1 = Mathf.Min(s.nx - 1, i1 + pad); j1 = Mathf.Min(s.nz - 1, j1 + pad);
            if (i0 == 0 && j0 == 0 && i1 == s.nx - 1 && j1 == s.nz - 1) return s;

            var c = new IslandShape(s.cell, i1 - i0 + 1, j1 - j0 + 1, s.CellPos(i0, j0));
            for (int j = 0; j < c.nz; j++)
                for (int i = 0; i < c.nx; i++)
                    c.h[j * c.nx + i] = s.h[(j + j0) * s.nx + i + i0];
            return c;
        }

        sealed class Gen
        {
            readonly IslandArchetype _type;
            readonly float _r, _hs;
            readonly float _p1, _p2, _p3, _wob, _reliefFreq, _hillFreq;
            readonly int _f1, _f2, _f3;
            readonly Vector2 _off;
            readonly float _stretch, _bend, _width, _crest, _hill, _top, _tilt;
            readonly float _ringR, _ringW, _gap;
            readonly Vector2[] _c;
            readonly float[] _cr, _ch;

            public Gen(IslandArchetype type, float radius, int seed, float heightScale)
            {
                _type = type;
                _r = radius;
                _hs = heightScale;
                var rnd = new System.Random(seed);
                float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

                _p1 = R(0f, 6.28f); _p2 = R(0f, 6.28f); _p3 = R(0f, 6.28f);
                _f1 = rnd.Next(2, 5); _f2 = rnd.Next(5, 8); _f3 = rnd.Next(8, 12);
                _wob = R(0.12f, 0.32f);
                _off = new Vector2(R(50f, 150f), R(50f, 150f));
                _reliefFreq = R(0.25f, 0.5f);
                _hillFreq = R(0.16f, 0.3f);
                float big = IslandShape.Smooth(3f, 8f, radius);

                switch (type)
                {
                    case IslandArchetype.Blob:
                        _stretch = R(1f, 1.4f);
                        _hill = R(0.4f, 1.6f) * big;
                        break;
                    case IslandArchetype.Ridge:
                        _width = R(0.36f, 0.5f);
                        _bend = R(-0.4f, 0.4f);
                        _crest = Mathf.Clamp(0.4f * radius, 0.5f, 1.8f) * R(0.7f, 1.1f);
                        _wob *= 0.8f;
                        break;
                    case IslandArchetype.Crescent:
                        _ringW = Mathf.Lerp(0.42f, 0.28f, big) + R(-0.03f, 0.03f);
                        _ringR = 1f - 0.9f * _ringW;
                        // Every third one is an atoll: a narrow pass instead of an open bay.
                        _gap = (R(0f, 1f) < 0.33f ? R(12f, 22f) : R(45f, 95f)) * Mathf.Deg2Rad;
                        _wob *= 0.7f;
                        break;
                    case IslandArchetype.TwinPeak:
                    {
                        _c = new[] { new Vector2(-0.42f * radius, 0f), new Vector2(0.5f * radius, R(-0.18f, 0.18f) * radius) };
                        _cr = new[] { R(0.62f, 0.72f) * radius, R(0.45f, 0.6f) * radius };
                        float p = Mathf.Clamp(0.42f * radius, 0.7f, 2f) * R(0.85f, 1.1f);
                        _ch = new[] { p, p * R(0.55f, 0.85f) };
                        _wob *= 0.8f;
                        break;
                    }
                    case IslandArchetype.Sandbank:
                        _stretch = R(1.5f, 2.4f);
                        _bend = R(-0.45f, 0.45f);
                        _wob = Mathf.Min(0.36f, _wob * 1.3f);
                        break;
                    case IslandArchetype.Mesa:
                        _width = R(0.7f, 0.95f);
                        _top = R(1.1f, 1.55f);
                        _tilt = R(-0.2f, 0.2f);
                        _wob = R(0.05f, 0.12f);
                        break;
                    case IslandArchetype.Archipelago:
                    {
                        int k = radius < 3f ? rnd.Next(2, 4) : rnd.Next(2, 5);
                        _c = new Vector2[k];
                        _cr = new float[k];
                        _ch = new float[k];
                        float a0 = R(0f, 6.28f);
                        for (int i = 0; i < k; i++)
                        {
                            float a = a0 + i * 6.28f / k + R(-0.35f, 0.35f);
                            float rho = R(0.5f, 0.7f) * radius;
                            _c[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rho;
                            _cr[i] = (i == 0 ? R(0.44f, 0.54f) : R(0.26f, 0.42f)) * radius;
                            _ch[i] = R(0.7f, 1.1f);
                        }
                        break;
                    }
                    case IslandArchetype.Stack:
                    {
                        int k = rnd.Next(1, 4);
                        _c = new Vector2[k];
                        _cr = new float[k];
                        _ch = new float[k];
                        float peak = Mathf.Clamp(1.2f + 0.9f * radius, 1.6f, 3.1f) * R(0.85f, 1f);
                        float a0 = R(0f, 6.28f);
                        for (int i = 0; i < k; i++)
                        {
                            if (i == 0)
                            {
                                _c[i] = new Vector2(R(-0.1f, 0.1f), R(-0.1f, 0.1f)) * radius;
                                _cr[i] = R(0.64f, 0.74f) * radius;
                                _ch[i] = peak;
                                continue;
                            }
                            float a = a0 + i * R(1.6f, 2.6f);
                            _c[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (R(0.75f, 0.95f) * radius);
                            _cr[i] = R(0.25f, 0.38f) * radius;
                            _ch[i] = peak * R(0.4f, 0.7f);
                        }
                        _wob = R(0.1f, 0.2f);
                        break;
                    }
                }
            }

            float Wobble(float ang, int i = 0) => 1f + _wob * (0.5f * Mathf.Sin(_f1 * ang + _p1 + i * 1.7f)
                + 0.3f * Mathf.Sin(_f2 * ang + _p2 + i * 2.3f) + 0.2f * Mathf.Sin(_f3 * ang + _p3 + i * 0.9f));

            float Relief(Vector2 p) => 0.5f + 0.35f * Mathf.PerlinNoise(_off.x + p.x * _reliefFreq, _off.y + p.y * _reliefFreq)
                + 0.15f * Mathf.PerlinNoise(_off.x + p.x * 1.3f, _off.y + p.y * 1.3f);

            float Ridged(Vector2 p, float f) => 1f - Mathf.Abs(2f * Mathf.PerlinNoise(_off.x + 40f + p.x * f, _off.y + 40f + p.y * f) - 1f);

            static float Shore(float top, float floor, float d, float a, float b) => Mathf.Lerp(top, floor, IslandShape.Smooth(a, b, d));

            public float Height(Vector2 p)
            {
                switch (_type)
                {
                    case IslandArchetype.Ridge: return Ridge(p);
                    case IslandArchetype.Crescent: return Crescent(p);
                    case IslandArchetype.TwinPeak: return TwinPeak(p);
                    case IslandArchetype.Sandbank: return Sandbank(p);
                    case IslandArchetype.Mesa: return Mesa(p);
                    case IslandArchetype.Archipelago: return Archipelago(p);
                    case IslandArchetype.Stack: return Stack(p);
                    default: return Blob(p);
                }
            }

            float Blob(Vector2 p)
            {
                float sq = Mathf.Sqrt(_stretch);
                var q = new Vector2(p.x / sq, p.y * sq);
                float d = q.magnitude / (_r * Wobble(Mathf.Atan2(q.y, q.x)));
                float top = Relief(p) + _hill * Ridged(p, _hillFreq) * (1f - IslandShape.Smooth(0.3f, 0.8f, d));
                return Shore(top * _hs, IslandShape.Sea, d, 0.7f, 1.15f);
            }

            float Ridge(Vector2 p)
            {
                float a = 1.35f * _r, b = _width * _r;
                float x = p.x / a;
                float z0 = _bend * _r * (x * x - 0.33f);
                float z = (p.y - z0) / (b * Wobble(Mathf.Atan2(p.y, p.x)));
                float d = Mathf.Sqrt(x * x + z * z);
                float spine = (1f - IslandShape.Smooth(0f, 1f, Mathf.Abs(z))) * Mathf.Clamp01(1f - x * x);
                float top = 0.8f * Relief(p) + _crest * spine * (0.6f + 0.4f * Ridged(p, 0.5f));
                return Shore(top * _hs, IslandShape.Sea, d, 0.7f, 1.15f);
            }

            float Crescent(Vector2 p)
            {
                float rho = p.magnitude;
                float ang = Mathf.Atan2(p.x, p.y);
                float da = Mathf.Abs(ang);
                float w0 = _ringW * _r, rc = _ringR * _r;
                // Thickest opposite the opening, thinning towards the horns; the horns end round because
                // the arc distance into the gap is measured in ring widths like the radial distance.
                float w = w0 * (0.62f + 0.55f * IslandShape.Smooth(_gap, Mathf.PI, da)) * Wobble(ang);
                float across = Mathf.Abs(rho - rc) / w;
                float along = Mathf.Max(0f, _gap + 0.8f * w / rc - da) * rc / w;
                float d = Mathf.Sqrt(across * across + along * along);
                // The lagoon ends at the line between the two horns, not at the full ring circle.
                float chord = rc * Mathf.Cos(_gap);
                float open = Mathf.Max(IslandShape.Smooth(rc - 0.3f * w0, rc + 0.9f * w0, rho),
                    IslandShape.Smooth(chord - 0.15f * _r, chord + 0.3f * _r, p.y));
                float floor = Mathf.Lerp(Shallow, IslandShape.Sea, open);
                return Shore(0.75f * Relief(p) * _hs, floor, d, 0.6f, 1.15f);
            }

            float TwinPeak(Vector2 p)
            {
                const float k = 5f;
                float sum = 0f, hills = 0f;
                for (int i = 0; i < 2; i++)
                {
                    Vector2 q = p - _c[i];
                    float dist = q.magnitude;
                    sum += Mathf.Exp(-k * dist / (_cr[i] * Wobble(Mathf.Atan2(q.y, q.x), i)));
                    float e = dist / (0.55f * _cr[i]);
                    hills += _ch[i] * Mathf.Exp(-e * e);
                }
                float d = -Mathf.Log(Mathf.Max(1e-9f, sum)) / k;
                float top = 0.75f * Relief(p) + hills * (0.85f + 0.15f * Ridged(p, 0.6f));
                return Shore(top * _hs, IslandShape.Sea, d, 0.7f, 1.15f);
            }

            float Sandbank(Vector2 p)
            {
                float a = 1.25f * _r, b = a / _stretch;
                float x = p.x / a;
                float z0 = _bend * _r * (x * x - 0.33f);
                float z = (p.y - z0) / b;
                float d = Mathf.Sqrt(x * x + z * z) / Wobble(Mathf.Atan2(p.y, p.x));
                float top = 0.2f + 0.22f * Mathf.PerlinNoise(_off.x + p.x * 0.6f, _off.y + p.y * 0.6f);
                float h = Mathf.Lerp(top * Mathf.Lerp(1f, _hs, 0.5f), Shallow, IslandShape.Smooth(0.55f, 0.95f, d));
                return Mathf.Lerp(h, IslandShape.Sea, IslandShape.Smooth(1f, 1.35f, d));
            }

            float Mesa(Vector2 p)
            {
                float a = 0.95f * _r, b = _width * _r;
                float ex = Mathf.Abs(p.x / a), ez = Mathf.Abs(p.y / b);
                float e = Mathf.Pow(ex * ex * ex + ez * ez * ez, 1f / 3f) / Wobble(Mathf.Atan2(p.y, p.x));
                float top = _top + _tilt * p.x / _r + 0.14f * (Mathf.PerlinNoise(_off.x + p.x * 0.7f, _off.y + p.y * 0.7f) - 0.5f);
                float h = Mathf.Lerp(top * _hs, 0.22f, IslandShape.Smooth(0.72f, 0.86f, e));
                return Mathf.Lerp(h, IslandShape.Sea, IslandShape.Smooth(0.92f, 1.2f, e));
            }

            float Archipelago(Vector2 p)
            {
                float d = float.MaxValue, scale = 1f;
                for (int i = 0; i < _c.Length; i++)
                {
                    Vector2 q = p - _c[i];
                    float di = q.magnitude / (_cr[i] * Wobble(Mathf.Atan2(q.y, q.x), i));
                    if (di < d) { d = di; scale = _ch[i]; }
                }
                float bank = p.magnitude / (_r * (1f + 1.5f * (Wobble(Mathf.Atan2(p.y, p.x), 5) - 1f)));
                float floor = Mathf.Lerp(Shallow, IslandShape.Sea, IslandShape.Smooth(0.8f, 1.15f, bank));
                return Shore(0.8f * Relief(p) * scale * _hs, floor, d, 0.65f, 1.15f);
            }

            float Stack(Vector2 p)
            {
                float h = IslandShape.Sea;
                for (int i = 0; i < _c.Length; i++)
                {
                    Vector2 q = p - _c[i];
                    float e = q.magnitude / (_cr[i] * Wobble(Mathf.Atan2(q.y, q.x), i));
                    float top = _ch[i] * (0.82f + 0.18f * Ridged(p, 1.1f));
                    float hi = Mathf.Lerp(top * _hs, 0.12f, IslandShape.Smooth(0.62f, 0.9f, e));
                    hi = Mathf.Lerp(hi, IslandShape.Sea, IslandShape.Smooth(0.95f, 1.25f, e));
                    if (hi > h) h = hi;
                }
                return h;
            }
        }
    }
}
