using UnityEngine;

namespace Drift.Visuals
{
    // The two rims of the adventure ring (Drift.Islands.RingWorld). The world ends there, but nothing is drawn
    // standing up: the only sign is a foam line lying flat on the water, so the sea closes into the edge instead of
    // showing a wall. One mesh in band-local coordinates: x across (0 = band centre), z along from -length/2 to
    // +length/2, built a little longer than one circumference and re-centred on the focus in whole panel steps, so
    // the ring always closes and the foam stays put on the edge.
    // UV0 = (along the rim in units, distance inward from the lip in units), UV1.y = 0 at the lip, 1 at the inner
    // end of the band. Shaders/RingRim.shader does the rest.
    public static class RingRims
    {
        public const int PanelsPerGroup = 8;
        // The foam sits just inside the lip, so the edge fold of DriftCurve never moves it.
        const float Inset = 0.15f;

        // The whole mesh moves in steps of this length, so the foam stays on the edge.
        public static float Snap(float segment) => Mathf.Max(0.5f, segment) * PanelsPerGroup;

        public static Mesh Build(float halfWidth, float length, float foamWidth, float segment)
        {
            segment = Mathf.Max(0.5f, segment);
            int n = Mathf.Max(1, Mathf.CeilToInt(length / segment / PanelsPerGroup) * PanelsPerGroup);
            float z0 = -0.5f * n * segment;
            foamWidth = Mathf.Clamp(foamWidth, 0.5f, Mathf.Max(1f, halfWidth * 0.5f));
            // Across the strip: dense at the lip where the crest sits, coarse where it fades into open water.
            float[] steps = { 0f, 0.12f, 0.3f, 0.6f, 1f };
            int rows = steps.Length - 1;
            int quads = n * rows * 2;
            var verts = new Vector3[quads * 4];
            var norms = new Vector3[quads * 4];
            var uv0 = new Vector2[quads * 4];
            var uv1 = new Vector2[quads * 4];
            var tris = new int[quads * 6];
            int v = 0, t = 0;

            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? 1f : -1f;
                float lip = (halfWidth - Inset) * sx;
                for (int k = 0; k < n; k++)
                {
                    float za = z0 + k * segment, zb = za + segment;
                    for (int r = 0; r < rows; r++)
                    {
                        float i0 = steps[r] * foamWidth, i1 = steps[r + 1] * foamWidth;
                        float x0 = lip - i0 * sx, x1 = lip - i1 * sx;
                        verts[v] = new Vector3(x0, 0.03f, za);
                        verts[v + 1] = new Vector3(x1, 0.03f, za);
                        verts[v + 2] = new Vector3(x1, 0.03f, zb);
                        verts[v + 3] = new Vector3(x0, 0.03f, zb);
                        uv0[v] = new Vector2(za, i0); uv0[v + 1] = new Vector2(za, i1);
                        uv0[v + 2] = new Vector2(zb, i1); uv0[v + 3] = new Vector2(zb, i0);
                        uv1[v] = new Vector2(sx, steps[r]); uv1[v + 1] = new Vector2(sx, steps[r + 1]);
                        uv1[v + 2] = new Vector2(sx, steps[r + 1]); uv1[v + 3] = new Vector2(sx, steps[r]);
                        for (int q = 0; q < 4; q++) norms[v + q] = Vector3.up;
                        tris[t++] = v; tris[t++] = v + 1; tris[t++] = v + 2;
                        tris[t++] = v; tris[t++] = v + 2; tris[t++] = v + 3;
                        v += 4;
                    }
                }
            }

            var mesh = new Mesh { name = "RingRims", hideFlags = HideFlags.DontSave };
            mesh.indexFormat = verts.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uv0;
            mesh.uv2 = uv1;
            mesh.triangles = tris;
            // Culled by its unbent bounds; the ring culling box of CurvedWorld keeps it anyway.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            return mesh;
        }
    }
}
