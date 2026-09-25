using UnityEngine;

namespace Drift.Visuals
{
    // The clouds' shadow on the sea as a small R8 texture, so Drift/Water and Drift/WaterFlecks sample it
    // (CloudShadowTex in Shaders/DriftClouds.hlsl) instead of hashing one to four clump cells in every sea pixel. The
    // puffs, islands and animals keep the analytic CloudShadow; outside the texture the sea falls back to it too.
    //
    // The texture lives in CLOUD space (q = (xz + shift) * scale + offset, one clump cell = 1): the wind's drift and
    // the sun's shift move the shadow inside the shader, and the content only depends on the cover. It is refilled
    // when the cover has changed or the view has left the middle of the covered cells, and faded in over the previous
    // fill, so a cover that changes fast (a storm moving in) still grows the shadows smoothly.
    //
    // It stores each clump's linear ramp (CloudField.ClumpRampLinear, unclamped between RampMin and RampMax) instead of
    // the density: the ramp is linear in the distance, so bilinear filtering reproduces it almost exactly and the
    // saturate + smoothstep applied in the shader restore the soft edge; a max over the clumps commutes with both.
    // Clamped to 0..1 before filtering, the flat top and foot of the ramp got rounded off by a texel on either side.
    public class CloudShadowTexture
    {
        // 20 texels per clump cell (0.9 u at the default 18 u spacing): the six soft lobes round a clump's rim are about
        // three texels apart, and at 16 they came out a few percent flatter.
        public const int TexelsPerCell = 20;
        // Refill once the cover has moved this much (a growing clump's radius then changes by ~5 %, faded in over FadeTime).
        public const float CoverTolerance = 0.004f;
        // Below this cover the analytic path already returns "no cloud" at once: nothing to fill.
        public const float MinCover = 0.001f;
        // How long a new fill takes to replace the previous one on screen (seconds).
        public const float FadeTime = 0.2f;
        // Encoded ramp range: byte 0 = RampMin (and "no clump"), 255 = RampMax (a clump's centre is exactly 1.5).
        // Must match CloudShadowTex in Shaders/DriftClouds.hlsl.
        public const float RampMin = -1f, RampMax = 1.5f;

        public int Resolution { get; }
        public float SizeCells => Resolution / (float)TexelsPerCell;
        public float Texel => 1f / TexelsPerCell;

        public Texture2D Current { get; private set; }
        public Texture2D Previous { get; private set; }
        // xy = cloud-space origin (cells), z = 1 / size (cells), w = 1 when valid.
        public Vector4 CurrentParams { get; private set; }
        // Same for the previous fill, but w = its remaining weight (0 = not shown any more).
        public Vector4 PreviousParams { get; private set; }
        public float FilledCover { get; private set; } = -1f;
        public int FillCount { get; private set; }
        public bool Busy => _filling;

        readonly byte[] _back;
        Vector2 _nextOrigin;
        float _nextCover;
        // Cell rows (cloud-space y) of the fill in progress; they can be negative, hence the separate flag.
        int _row, _rowEnd;
        bool _filling;
        int _cellX0, _cellX1;
        bool _pending;
        float _fade = 1f;

        public CloudShadowTexture(int resolution)
        {
            Resolution = Mathf.Clamp(resolution, 16, 512);
            _back = new byte[Resolution * Resolution];
        }

        public void Release()
        {
            Destroy(Current);
            Destroy(Previous);
            Current = Previous = null;
            CurrentParams = PreviousParams = Vector4.zero;
            FilledCover = -1f;
            _filling = false;
            _pending = false;
        }

        static void Destroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        Texture2D Create()
        {
            return new Texture2D(Resolution, Resolution, TextureFormat.R8, false, true)
            {
                name = "Drift Cloud Shadow",
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
        }

        // The region origin (cells) for a view centred on qCentre: snapped to whole texels, so two fills always share
        // one texel lattice and a recentred fill shows exactly what the previous one showed where they overlap.
        public Vector2 OriginFor(Vector2 qCentre)
        {
            float half = SizeCells * 0.5f;
            return new Vector2(Mathf.Round(qCentre.x * TexelsPerCell) / TexelsPerCell - half,
                               Mathf.Round(qCentre.y * TexelsPerCell) / TexelsPerCell - half);
        }

        public bool NeedsRefill(float cover, Vector2 qCentre)
        {
            if (cover < MinCover) return CurrentParams.w > 0f;
            if (CurrentParams.w <= 0f || Current == null) return true;
            if (Mathf.Abs(cover - FilledCover) > CoverTolerance) return true;
            float half = SizeCells * 0.5f;
            Vector2 centre = new Vector2(CurrentParams.x + half, CurrentParams.y + half);
            // A quarter of the half-size of slack before recentring: the view still sits well inside.
            return Mathf.Max(Mathf.Abs(qCentre.x - centre.x), Mathf.Abs(qCentre.y - centre.y)) > half * 0.25f;
        }

        // One step per frame. dt <= 0 (edit mode, tests) fills and shows at once; in Play Mode a fill is spread over
        // frames (cellRowsPerStep clump rows each) and faded in over FadeTime.
        public void Step(float cover, Vector2 qCentre, float dt, int cellRowsPerStep)
        {
            if (dt <= 0f)
            {
                _filling = false;
                _pending = false;
                if (!NeedsRefill(cover, qCentre)) return;
                if (cover < MinCover) { Invalidate(); return; }
                Begin(cover, qCentre);
                FillCells(_row, _rowEnd + 1);
                _filling = false;
                Swap();
                _fade = 1f;
                PreviousParams = new Vector4(PreviousParams.x, PreviousParams.y, PreviousParams.z, 0f);
                return;
            }

            if (_fade < 1f)
            {
                _fade = Mathf.Min(1f, _fade + dt / FadeTime);
                PreviousParams = new Vector4(PreviousParams.x, PreviousParams.y, PreviousParams.z, 1f - _fade);
            }
            if (_pending)
            {
                // The finished fill waits until the previous fade is complete, so nothing ever jumps.
                if (_fade < 1f) return;
                Swap();
                _pending = false;
                // The very first fill has nothing to fade from.
                _fade = PreviousParams.z > 0f ? 0f : 1f;
                PreviousParams = new Vector4(PreviousParams.x, PreviousParams.y, PreviousParams.z, 1f - _fade);
                return;
            }
            if (_filling)
            {
                int end = Mathf.Min(_rowEnd + 1, _row + Mathf.Max(1, cellRowsPerStep));
                FillCells(_row, end);
                _row = end;
                if (_row > _rowEnd)
                {
                    _filling = false;
                    _pending = true;
                }
                return;
            }
            if (!NeedsRefill(cover, qCentre)) return;
            if (cover < MinCover) { Invalidate(); return; }
            Begin(cover, qCentre);
        }

        void Invalidate()
        {
            CurrentParams = Vector4.zero;
            PreviousParams = Vector4.zero;
            FilledCover = -1f;
            _fade = 1f;
        }

        void Begin(float cover, Vector2 qCentre)
        {
            _nextOrigin = OriginFor(qCentre);
            _nextCover = cover;
            System.Array.Clear(_back, 0, _back.Length);
            float size = SizeCells;
            _cellX0 = Mathf.FloorToInt(_nextOrigin.x) - 1;
            _cellX1 = Mathf.FloorToInt(_nextOrigin.x + size) + 1;
            _row = Mathf.FloorToInt(_nextOrigin.y) - 1;
            _rowEnd = Mathf.FloorToInt(_nextOrigin.y + size) + 1;
            _filling = true;
        }

        void FillCells(int row0, int row1)
        {
            for (int cy = row0; cy < row1; cy++)
                for (int cx = _cellX0; cx <= _cellX1; cx++)
                    Stamp(_back, Resolution, _nextOrigin, Texel, CloudField.ClumpAt(new Vector2(cx, cy), _nextCover));
        }

        void Swap()
        {
            (Current, Previous) = (Previous, Current);
            if (Current == null) Current = Create();
            Current.SetPixelData(_back, 0);
            Current.Apply(false, false);
            PreviousParams = CurrentParams;
            CurrentParams = new Vector4(_nextOrigin.x, _nextOrigin.y, 1f / SizeCells, 1f);
            FilledCover = _nextCover;
            FillCount++;
        }

        public static byte Encode(float ramp) =>
            (byte)(Mathf.Clamp01((ramp - RampMin) / (RampMax - RampMin)) * 255f + 0.5f);

        public static float Decode(float b) => RampMin + b / 255f * (RampMax - RampMin);

        // Pure core: writes max(existing, ramp of clump c) into every texel the clump can reach plus two texels (so
        // the texels on either side of its rim hold the true slope). Texel (i, j) (row-major, j along cloud-space y)
        // samples origin + (i + 0.5, j + 0.5) * texel, the centre a bilinear lookup at uv = (q - origin) / size sees.
        public static void Stamp(byte[] dst, int res, Vector2 origin, float texel, in CloudField.Clump c)
        {
            if (c.radius < 1e-4f) return;
            float reach = c.radius * 1.05f * c.aspect + 2f * texel;
            float inv = 1f / texel;
            int i0 = Mathf.Max(0, Mathf.FloorToInt((c.centre.x - reach - origin.x) * inv - 0.5f));
            int i1 = Mathf.Min(res - 1, Mathf.CeilToInt((c.centre.x + reach - origin.x) * inv - 0.5f));
            int j0 = Mathf.Max(0, Mathf.FloorToInt((c.centre.y - reach - origin.y) * inv - 0.5f));
            int j1 = Mathf.Min(res - 1, Mathf.CeilToInt((c.centre.y + reach - origin.y) * inv - 0.5f));
            // CloudField.ClumpRampLinear inlined (this loop is the whole cost of a fill).
            float ax = c.axis.x, ay = c.axis.y, invAspect = 1f / c.aspect, r = c.radius;
            float scale = 255f / (RampMax - RampMin);
            for (int j = j0; j <= j1; j++)
            {
                float dy = origin.y + (j + 0.5f) * texel - c.centre.y;
                int row = j * res;
                for (int i = i0; i <= i1; i++)
                {
                    float dx = origin.x + (i + 0.5f) * texel - c.centre.x;
                    float lx = (dx * ax + dy * ay) * invAspect;
                    float ly = dy * ax - dx * ay;
                    float len2 = lx * lx + ly * ly;
                    float len = Mathf.Sqrt(len2);
                    float c2 = lx * lx / Mathf.Max(len2, 1e-8f);
                    float t6 = ((32f * c2 - 48f) * c2 + 18f) * c2 - 1f;
                    float edge = r * (0.95f + 0.05f * t6);
                    float v = (1.05f * edge - len) / (0.7f * edge);
                    float e = (v - RampMin) * scale;
                    if (e <= 0f) continue;
                    byte b = e >= 255f ? (byte)255 : (byte)(e + 0.5f);
                    if (b > dst[row + i]) dst[row + i] = b;
                }
            }
        }

        // What the shader reconstructs at cloud-space q from a filled buffer (bilinear, clamped, then smoothstep);
        // -1 outside. For tests.
        public static float SampleDensity(byte[] src, int res, Vector2 origin, float texel, Vector2 q)
        {
            float x = (q.x - origin.x) / texel - 0.5f, y = (q.y - origin.y) / texel - 0.5f;
            if (x < 0f || y < 0f || x > res - 1 || y > res - 1) return -1f;
            int ix = Mathf.Min((int)x, res - 2), iy = Mathf.Min((int)y, res - 2);
            float fx = x - ix, fy = y - iy;
            float a = src[iy * res + ix], b = src[iy * res + ix + 1];
            float c = src[(iy + 1) * res + ix], d = src[(iy + 1) * res + ix + 1];
            float ramp = Decode(Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy));
            return CloudField.SmoothStep01(ramp);
        }

        // The filled buffer of the last Begin (tests).
        public byte[] Buffer => _back;
    }
}
