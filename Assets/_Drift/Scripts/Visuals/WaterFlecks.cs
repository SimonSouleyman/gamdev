using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Drift.Visuals
{
    // The current's foam flecks as their own draw (Shaders/WaterFlecks.shader), owned by WaterFeedback: a child of the
    // water renderer with a static mesh of one quad per (level, phase, cell) around the camera. The vertex shader
    // throws away every quad whose fleck is not alive, so the GPU only shades the few pixels a fleck covers - the water
    // shader used to search four cells for one in every sea pixel.
    public sealed class WaterFlecks
    {
        // Must match FLECK_LEVEL_MIN in WaterFlecks.shader. Level lv has cells of 2 * 2^lv units and shows while
        // log2(camDist / 12) is within 1 of lv: -3 .. 8 covers the closest zoom to past the far clip.
        public const int LevelMin = -3;
        public const int Levels = 12;
        // Cells (of the level's size) around the camera's cell: a level fades out 12 cells away, the rest is room for
        // the grid drifting with a current that differs from the one under the camera.
        public const float GridRadius = 14.5f;
        public const string ObjName = "WaterFlecks";
        const string MeshName = "DriftWaterFlecks";
        const string ShaderName = "Drift/WaterFlecks";

        static readonly int FoamColorId = Shader.PropertyToID("_FoamColor");

        GameObject _go;
        MeshRenderer _renderer;
        Material _mat;

        public MeshRenderer Renderer => _renderer;

        public static int QuadCount => Levels * 2 * CellsPerGrid;

        static int CellsPerGrid
        {
            get
            {
                int n = 0, r = Mathf.CeilToInt(GridRadius);
                for (int y = -r; y <= r; y++)
                    for (int x = -r; x <= r; x++)
                        if (x * x + y * y <= GridRadius * GridRadius) n++;
                return n;
            }
        }

        public void Sync(Renderer water, MaterialPropertyBlock block, bool visible)
        {
            if (water == null) return;
            if (!Ensure(water)) return;
            if (_renderer.enabled != visible) _renderer.enabled = visible;
            if (_go.layer != water.gameObject.layer) _go.layer = water.gameObject.layer;
            var src = water.sharedMaterial;
            if (src != null && src.HasProperty(FoamColorId)) _mat.SetColor(FoamColorId, src.GetColor(FoamColorId));
            _renderer.SetPropertyBlock(block);
        }

        public void Release()
        {
            if (_go != null) Kill(_go);
            if (_mat != null) Kill(_mat);
            _go = null;
            _renderer = null;
            _mat = null;
        }

        bool Ensure(Renderer water)
        {
            if (_go == null)
            {
                // Found through the hierarchy: a plain field is empty after every recompile.
                var t = water.transform.Find(ObjName);
                if (t != null) _go = t.gameObject;
            }
            if (_mat == null && _go != null)
            {
                // After a domain reload the child keeps its runtime material: reuse it instead of leaking it.
                var old = _go.GetComponent<MeshRenderer>();
                if (old != null && old.sharedMaterial != null && old.sharedMaterial.shader != null && old.sharedMaterial.shader.name == ShaderName)
                    _mat = old.sharedMaterial;
            }
            if (_mat == null)
            {
                var shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    Debug.LogError("WaterFlecks: shader '" + ShaderName + "' not found.");
                    return false;
                }
                _mat = new Material(shader) { name = "WaterFlecks (runtime)", hideFlags = HideFlags.DontSave };
            }
            if (_go == null)
            {
                _go = new GameObject(ObjName) { hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild };
                _go.transform.SetParent(water.transform, false);
                _go.AddComponent<MeshFilter>();
                var mr = _go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            // Local zero on the water object: the shader reads the sea level from its own object matrix.
            if (_go.transform.localPosition != Vector3.zero) _go.transform.localPosition = Vector3.zero;
            var filter = _go.GetComponent<MeshFilter>();
            if (filter.sharedMesh == null || filter.sharedMesh.name != MeshName) filter.sharedMesh = BuildMesh();
            _renderer = _go.GetComponent<MeshRenderer>();
            if (_renderer.sharedMaterial != _mat) _renderer.sharedMaterial = _mat;
            return true;
        }

        // Quad corners in POSITION.xy, (cell offset x, y, level index, phase) in TEXCOORD0. Wound to face up; the shader
        // draws both sides anyway.
        public static Mesh BuildMesh()
        {
            var cells = new List<Vector2>();
            int r = Mathf.CeilToInt(GridRadius);
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                    if (x * x + y * y <= GridRadius * GridRadius) cells.Add(new Vector2(x, y));
            int quads = Levels * 2 * cells.Count;
            var pos = new Vector3[quads * 4];
            var info = new Vector4[quads * 4];
            var idx = new int[quads * 6];
            int q = 0;
            for (int l = 0; l < Levels; l++)
                for (int k = 0; k < 2; k++)
                    foreach (var c in cells)
                    {
                        int v = q * 4;
                        pos[v] = new Vector3(-1f, -1f, 0f);
                        pos[v + 1] = new Vector3(-1f, 1f, 0f);
                        pos[v + 2] = new Vector3(1f, 1f, 0f);
                        pos[v + 3] = new Vector3(1f, -1f, 0f);
                        var ci = new Vector4(c.x, c.y, l, k);
                        info[v] = info[v + 1] = info[v + 2] = info[v + 3] = ci;
                        int i = q * 6;
                        idx[i] = v; idx[i + 1] = v + 1; idx[i + 2] = v + 2;
                        idx[i + 3] = v; idx[i + 4] = v + 2; idx[i + 5] = v + 3;
                        q++;
                    }
            var mesh = new Mesh { name = MeshName, hideFlags = HideFlags.DontSave };
            mesh.indexFormat = pos.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = pos;
            mesh.SetUVs(0, info);
            mesh.SetIndices(idx, MeshTopology.Triangles, 0, false);
            // Placed around the camera by the shader: never culled.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            mesh.UploadMeshData(true);
            return mesh;
        }

        static void Kill(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
