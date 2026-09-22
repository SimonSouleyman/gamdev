using System.Collections.Generic;
using Drift.Tectonics;
using UnityEngine;

namespace Drift.Visuals
{
    // The plate currents around the view as a small RGBA32 texture for Drift/Water:
    //   rg = plate velocity / maxSpeed * 0.5 + 0.5, b = closeness to the nearest plate boundary (1 on it, 0 from
    //   edgeWidth on), a = closing speed across that boundary / maxSpeed * 0.5 + 0.5 (> 0.5 = the plates converge).
    // Plates are the Voronoi cells of their positions (PlateSystem.NearestPlate), so the fill only needs the plate
    // sites near the view: collected once per refresh with a coarse NearestPlate scan, then every texel is a plain
    // nearest-site search over that short list.
    public class CurrentField
    {
        public readonly int Resolution;
        public Texture2D Texture { get; private set; }
        public Vector2 Origin { get; private set; }
        public float Size { get; private set; }
        public float MaxSpeed { get; private set; }
        public int SiteCount => _sitePos.Count;

        readonly Color32[] _pixels;
        readonly List<Vector2> _sitePos = new();
        readonly List<Vector2> _siteVel = new();
        readonly List<PlateSystem.Plate> _plates = new();
        readonly FillScratch _scratch = new();

        public CurrentField(int resolution)
        {
            Resolution = Mathf.Clamp(resolution, 4, 256);
            _pixels = new Color32[Resolution * Resolution];
        }

        public Texture2D EnsureTexture()
        {
            if (Texture != null) return Texture;
            Texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false, true)
            {
                name = "Drift Current Field",
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            return Texture;
        }

        public void Release()
        {
            if (Texture == null) return;
            if (Application.isPlaying) Object.Destroy(Texture);
            else Object.DestroyImmediate(Texture);
            Texture = null;
        }

        // Refills the texture around centre (world xz). The origin snaps to whole texels so a texel always covers the
        // same patch of sea and the boundaries do not crawl while the view moves.
        public void Refresh(PlateSystem plates, Vector2 centre, float size, float maxSpeed, float edgeWidth)
        {
            EnsureTexture();
            float texel = size / Resolution;
            Origin = new Vector2(Mathf.Floor(centre.x / texel), Mathf.Floor(centre.y / texel)) * texel - Vector2.one * (size * 0.5f);
            Size = size;
            MaxSpeed = maxSpeed;
            CollectSites(plates, Origin, size, edgeWidth);
            Fill(_pixels, Resolution, Origin, texel, _sitePos, _siteVel, maxSpeed, edgeWidth, _scratch);
            Texture.SetPixelData(_pixels, 0);
            Texture.Apply(false, false);
        }

        void CollectSites(PlateSystem plates, Vector2 origin, float size, float margin)
        {
            _plates.Clear();
            _sitePos.Clear();
            _siteVel.Clear();
            if (plates == null) return;
            float step = Mathf.Max(4f, Mathf.Min(plates.cellSize / 6f, size / 8f));
            int n = Mathf.CeilToInt((size + 2f * margin) / step);
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n; j++)
                {
                    var p = plates.NearestPlate(origin + new Vector2(i * step - margin, j * step - margin));
                    if (p == null || _plates.Contains(p)) continue;
                    _plates.Add(p);
                    _sitePos.Add(p.position);
                    _siteVel.Add(p.velocity);
                }
        }

        public static Color32 Encode(Vector2 velocity, float maxSpeed, float edge, float closing)
        {
            float inv = maxSpeed > 1e-4f ? 0.5f / maxSpeed : 0f;
            return new Color32(
                ToByte(0.5f + velocity.x * inv),
                ToByte(0.5f + velocity.y * inv),
                ToByte(edge),
                ToByte(0.5f + closing * inv));
        }

        public static Vector2 DecodeVelocity(Color32 c, float maxSpeed) =>
            new Vector2(c.r / 255f * 2f - 1f, c.g / 255f * 2f - 1f) * maxSpeed;

        static byte ToByte(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);

        // Pure fill: texel (i, j) (row-major, j = rows along world z) samples the texel centre
        // origin + (i + 0.5, j + 0.5) * texel. With no sites the texture reads as still water.
        public static void Fill(Color32[] dst, int res, Vector2 origin, float texel, IReadOnlyList<Vector2> sitePos,
            IReadOnlyList<Vector2> siteVel, float maxSpeed, float edgeWidth)
        {
            var scratch = new FillScratch();
            Fill(dst, res, origin, texel, sitePos, siteVel, maxSpeed, edgeWidth, scratch);
        }

        public class FillScratch
        {
            public float[] px = System.Array.Empty<float>(), pz = System.Array.Empty<float>();
            public float[] invSep = System.Array.Empty<float>(), closing = System.Array.Empty<float>();
            public Color32[] inner = System.Array.Empty<Color32>();

            public void Ensure(int n)
            {
                if (px.Length >= n) return;
                px = new float[n]; pz = new float[n];
                invSep = new float[n * n]; closing = new float[n * n];
                inner = new Color32[n];
            }
        }

        public static void Fill(Color32[] dst, int res, Vector2 origin, float texel, IReadOnlyList<Vector2> sitePos,
            IReadOnlyList<Vector2> siteVel, float maxSpeed, float edgeWidth, FillScratch s)
        {
            int count = sitePos.Count;
            if (count == 0)
            {
                var still = Encode(Vector2.zero, maxSpeed, 0f, 0f);
                for (int i = 0; i < res * res; i++) dst[i] = still;
                return;
            }
            s.Ensure(count);
            float[] px = s.px, pz = s.pz, invSep = s.invSep, closingT = s.closing;
            for (int k = 0; k < count; k++)
            {
                px[k] = sitePos[k].x;
                pz[k] = sitePos[k].y;
                s.inner[k] = Encode(siteVel[k], maxSpeed, 0f, 0f);
            }
            for (int a = 0; a < count; a++)
                for (int b = 0; b < count; b++)
                {
                    Vector2 d = sitePos[b] - sitePos[a];
                    float sep = d.magnitude;
                    invSep[a * count + b] = a == b || sep < 1e-4f ? 0f : 0.5f / sep;
                    closingT[a * count + b] = a == b || sep < 1e-4f ? 0f : -Vector2.Dot(siteVel[b] - siteVel[a], d / sep);
                }
            float invEdge = edgeWidth > 1e-4f ? 1f / edgeWidth : 0f;
            for (int j = 0; j < res; j++)
            {
                float z = origin.y + (j + 0.5f) * texel;
                for (int i = 0; i < res; i++)
                {
                    float x = origin.x + (i + 0.5f) * texel;
                    int best = 0;
                    float bestD = float.MaxValue;
                    for (int k = 0; k < count; k++)
                    {
                        float dx = px[k] - x, dz = pz[k] - z;
                        float d = dx * dx + dz * dz;
                        if (d < bestD) { bestD = d; best = k; }
                    }
                    if (invEdge <= 0f)
                    {
                        dst[j * res + i] = s.inner[best];
                        continue;
                    }

                    // Distance to the Voronoi edge with site k: (|x - s_k|^2 - |x - s_best|^2) / (2 |s_k - s_best|).
                    float edgeD = edgeWidth;
                    int other = -1;
                    int row = best * count;
                    for (int k = 0; k < count; k++)
                    {
                        float inv = invSep[row + k];
                        if (inv == 0f) continue;
                        float dx = px[k] - x, dz = pz[k] - z;
                        float e = (dx * dx + dz * dz - bestD) * inv;
                        if (e < edgeD) { edgeD = e; other = k; }
                    }
                    if (other < 0)
                    {
                        dst[j * res + i] = s.inner[best];
                        continue;
                    }
                    float edge = Mathf.Clamp01(1f - edgeD * invEdge);
                    dst[j * res + i] = Encode(siteVel[best], maxSpeed, edge, closingT[row + other]);
                }
            }
        }
    }
}
