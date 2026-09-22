using Drift.Islands;
using UnityEngine;

namespace Drift.Visuals
{
    // The player island's real coastline as a small signed-distance texture for Drift/Water:
    //   r = distance to the waterline in world units (negative on land), gb = outward normal, a = 1.
    // Built in the island's BODY space over its land bounds plus a margin, so it only has to be rebuilt when
    // the shape changes - the shader rotates the pixel into body space with the island's yaw.
    //
    // Why it exists: everything the water drew around the player used to come from the bounding radius or from
    // a 32-entry radial profile around the island's centre. Both are circles/stars: on a branched island they
    // fill the notches between the arms, and the wake they anchored became a white box with straight edges.
    // A distance field sees the real outline, notches included.
    public class CoastField
    {
        public const float Far = 1e6f;

        public int Resolution { get; private set; }
        public Texture2D Texture { get; private set; }
        // Body-space position of the field's lower-left corner and its edge length.
        public Vector2 Origin { get; private set; }
        public float Size { get; private set; }
        public float Texel => Size / Mathf.Max(Resolution, 1);
        public float Margin { get; private set; }

        float[] _height = System.Array.Empty<float>();
        float[] _dist = System.Array.Empty<float>();
        Color[] _pixels = System.Array.Empty<Color>();

        public void Release()
        {
            if (Texture == null) return;
            if (Application.isPlaying) Object.Destroy(Texture);
            else Object.DestroyImmediate(Texture);
            Texture = null;
        }

        // Field resolution for an island of this size: about one texel per 0.6 u while that stays affordable,
        // in three steps so a growing island does not reallocate the texture every merge.
        public static int ResolutionFor(float size) => size <= 48f ? 64 : size <= 96f ? 96 : 128;

        // How far past the land the field reaches. The wake and the speed lines are clamped to it, so it has to
        // be wide enough for both and is capped so a continent does not blow the texel size up.
        public static float MarginFor(float radius) => Mathf.Clamp(0.9f * radius + 12f, 18f, 52f);

        // Rebuilds the field around the island. Returns false when there is nothing to draw.
        public bool Refresh(Island island)
        {
            if (island == null || !island.isActiveAndEnabled) return false;
            float r = Mathf.Max(island.BoundingRadius, 1f);
            Margin = MarginFor(r);
            Size = 2f * (r + Margin);
            int res = ResolutionFor(Size);
            if (Texture == null || Resolution != res)
            {
                Release();
                Resolution = res;
                var format = SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf) ? TextureFormat.RGBAHalf : TextureFormat.RGBAFloat;
                Texture = new Texture2D(res, res, format, false, true)
                {
                    name = "Drift Coast Field",
                    hideFlags = HideFlags.DontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                _height = new float[res * res];
                _dist = new float[res * res];
                _pixels = new Color[res * res];
            }
            Origin = new Vector2(-0.5f * Size, -0.5f * Size);
            float texel = Texel;
            for (int j = 0; j < res; j++)
            {
                float z = Origin.y + (j + 0.5f) * texel;
                for (int i = 0; i < res; i++)
                    _height[j * res + i] = island.SampleHeight(new Vector2(Origin.x + (i + 0.5f) * texel, z));
            }
            Fill(_pixels, _height, _dist, res, texel);
            Texture.SetPixels(_pixels);
            Texture.Apply(false, false);
            return true;
        }

        // Pure core: heights (> 0 = land) on a res x res grid of `texel` spacing become
        // (signed distance to the waterline, outward normal x, outward normal y, 1) per texel.
        // The seed distance of a texel next to the waterline is the first-order distance to the zero crossing
        // (height / |gradient|), so the shore is resolved far below one texel; the rest is a two-pass chamfer.
        public static void Fill(Color[] dst, float[] height, float[] dist, int res, float texel)
        {
            float diag = texel * 1.41421356f;
            for (int j = 0; j < res; j++)
                for (int i = 0; i < res; i++)
                {
                    int k = j * res + i;
                    bool land = height[k] > 0f;
                    bool edge = i > 0 && (height[k - 1] > 0f) != land;
                    edge = edge || (i < res - 1 && (height[k + 1] > 0f) != land);
                    edge = edge || (j > 0 && (height[k - res] > 0f) != land);
                    edge = edge || (j < res - 1 && (height[k + res] > 0f) != land);
                    if (!edge) { dist[k] = Far; continue; }
                    float gx = (height[Mathf.Min(i + 1, res - 1) + j * res] - height[Mathf.Max(i - 1, 0) + j * res])
                               / ((Mathf.Min(i + 1, res - 1) - Mathf.Max(i - 1, 0)) * texel);
                    float gz = (height[i + Mathf.Min(j + 1, res - 1) * res] - height[i + Mathf.Max(j - 1, 0) * res])
                               / ((Mathf.Min(j + 1, res - 1) - Mathf.Max(j - 1, 0)) * texel);
                    float g = Mathf.Sqrt(gx * gx + gz * gz);
                    dist[k] = g > 1e-5f ? Mathf.Min(Mathf.Abs(height[k]) / g, diag) : 0f;
                }
            for (int j = 0; j < res; j++)
                for (int i = 0; i < res; i++)
                {
                    int k = j * res + i;
                    float d = dist[k];
                    if (i > 0) d = Mathf.Min(d, dist[k - 1] + texel);
                    if (j > 0) d = Mathf.Min(d, dist[k - res] + texel);
                    if (i > 0 && j > 0) d = Mathf.Min(d, dist[k - res - 1] + diag);
                    if (i < res - 1 && j > 0) d = Mathf.Min(d, dist[k - res + 1] + diag);
                    dist[k] = d;
                }
            for (int j = res - 1; j >= 0; j--)
                for (int i = res - 1; i >= 0; i--)
                {
                    int k = j * res + i;
                    float d = dist[k];
                    if (i < res - 1) d = Mathf.Min(d, dist[k + 1] + texel);
                    if (j < res - 1) d = Mathf.Min(d, dist[k + res] + texel);
                    if (i < res - 1 && j < res - 1) d = Mathf.Min(d, dist[k + res + 1] + diag);
                    if (i > 0 && j < res - 1) d = Mathf.Min(d, dist[k + res - 1] + diag);
                    dist[k] = d;
                }
            // Signs only once the sweeps are done: the backward sweep reads its own results.
            for (int k = 0; k < res * res; k++) if (height[k] > 0f) dist[k] = -dist[k];
            for (int j = 0; j < res; j++)
                for (int i = 0; i < res; i++)
                {
                    int k = j * res + i;
                    int il = Mathf.Max(i - 1, 0), ir = Mathf.Min(i + 1, res - 1);
                    int jd = Mathf.Max(j - 1, 0), ju = Mathf.Min(j + 1, res - 1);
                    // The distance grows away from the land, so its gradient IS the outward normal.
                    float nx = dist[ir + j * res] - dist[il + j * res];
                    float nz = dist[i + ju * res] - dist[i + jd * res];
                    float l = Mathf.Sqrt(nx * nx + nz * nz);
                    if (l > 1e-5f) { nx /= l; nz /= l; }
                    else { nx = 0f; nz = 1f; }
                    // Clamped so an island with no land at all (Far everywhere) still stores a finite half float.
                    dst[k] = new Color(Mathf.Clamp(dist[k], -999f, 999f), nx, nz, 1f);
                }
        }
    }
}
