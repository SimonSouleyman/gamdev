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
        // The vertex shader keeps a quad while |log2(max(d, 1) / 12) - lv| <= 1.35 (d = camera to cell centre); the slack
        // covers the frame the haze values lag behind the camera.
        const float LevelReach = 1.35f + 0.25f;
        // DRIFT_CURVE_GUARD_Y in DriftCurve.hlsl: nothing below it is hazed.
        const float FogGuardY = -1000f;
        const int IndicesPerLevel = 2 * 6;
        const MeshUpdateFlags SubMeshFlags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;

        static readonly int FoamColorId = Shader.PropertyToID("_FoamColor");
        static readonly int FogParamsId = Shader.PropertyToID("_CurveFogParams");
        static readonly int CellsPerGrid = CountCells();
        static readonly Bounds Everywhere = new Bounds(Vector3.zero, Vector3.one * 1e6f);
        static Camera[] s_cams = new Camera[8];
        static Vector4 s_mainFog;
        static int s_mainFogFrame = -100;
        static bool s_hooked;

        GameObject _go;
        MeshRenderer _renderer;
        Material _mat;
        Mesh _mesh;
        int _first = -1, _last = -1;

        public MeshRenderer Renderer => _renderer;
        public int FirstLevel => _first;
        public int LastLevel => _last;

        public static int QuadCount => Levels * 2 * CellsPerGrid;

        static int CountCells()
        {
            int n = 0, r = Mathf.CeilToInt(GridRadius);
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                    if (x * x + y * y <= GridRadius * GridRadius) n++;
            return n;
        }

        // Level indices [first, last] (0 = LevelMin) whose quads can pass the vertex shader for a camera camHeight above
        // the sea that sees no fleck farther than fogReach (planar) from itself. Every sea point is at least camHeight
        // away; first > last means none.
        public static void VisibleLevels(float camHeight, float fogReach, out int first, out int last)
        {
            float h = Mathf.Abs(camHeight);
            float lo = Mathf.Log(Mathf.Max(h, 1f) / 12f, 2f) - LevelReach;
            first = Mathf.Max(0, Mathf.CeilToInt(lo) - LevelMin);
            last = Levels - 1;
            if (float.IsInfinity(fogReach) || float.IsNaN(fogReach) || fogReach > 1e7f) return;
            float far = Mathf.Sqrt(h * h + fogReach * fogReach);
            float hi = Mathf.Log(Mathf.Max(far, 1f) / 12f, 2f) + LevelReach;
            last = Mathf.Min(Levels - 1, Mathf.FloorToInt(hi) - LevelMin);
        }

        // Planar distance from the camera past which DriftFogAmount (_CurveFogParams: start, 1 / (end - start), centre)
        // is above the vertex shader's 0.985 cut; infinity without haze.
        public static float FogReach(Vector4 fogParams, Vector2 camXZ, float seaY)
        {
            if (fogParams.y <= 0f || seaY < FogGuardY) return float.PositiveInfinity;
            return fogParams.x + 1f / fogParams.y + Vector2.Distance(camXZ, new Vector2(fogParams.z, fogParams.w));
        }

        // The mesh's quads are ordered level by level (BuildMesh), so a level range is one index range.
        public static void IndexRange(int first, int last, out int start, out int count)
        {
            first = Mathf.Clamp(first, 0, Levels);
            last = Mathf.Clamp(last, first - 1, Levels - 1);
            int perLevel = IndicesPerLevel * CellsPerGrid;
            start = first * perLevel;
            count = (last - first + 1) * perLevel;
        }

        public void Sync(Renderer water, MaterialPropertyBlock block, bool visible)
        {
            if (water == null) return;
            if (!Ensure(water)) return;
            if (visible) visible = CullLevels();
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
            if (_mesh != filter.sharedMesh)
            {
                _mesh = filter.sharedMesh;
                _first = _last = -1;
            }
            return true;
        }

        // Most of the 12 levels are far outside their distance band at any zoom: only the index range of the levels a
        // rendering camera can show is drawn. Other game cameras (the island preview) get no haze bound.
        bool CullLevels()
        {
            int first = 0, last = Levels - 1;
            if (Application.isPlaying)
            {
                if (!s_hooked)
                {
                    RenderPipelineManager.endCameraRendering += OnEndCamera;
                    s_hooked = true;
                }
                float seaY = _go.transform.position.y;
                first = Levels;
                last = -1;
                var main = Camera.main;
                if (main != null)
                {
                    bool fresh = Time.frameCount - s_mainFogFrame <= 2;
                    Widen(main, seaY, fresh ? s_mainFog : Vector4.zero, ref first, ref last);
                }
                int n = Camera.allCamerasCount;
                if (s_cams.Length < n) s_cams = new Camera[n + 4];
                n = Camera.GetAllCameras(s_cams);
                int mask = 1 << _go.layer;
                for (int i = 0; i < n; i++)
                {
                    var c = s_cams[i];
                    s_cams[i] = null;
                    if (c == main || c.cameraType != CameraType.Game || (c.cullingMask & mask) == 0) continue;
                    Widen(c, seaY, Vector4.zero, ref first, ref last);
                }
                if (first > last) return false;
            }
            if (first == _first && last == _last) return true;
            IndexRange(first, last, out int start, out int count);
            _mesh.SetSubMesh(0, new SubMeshDescriptor(start, count)
            {
                bounds = Everywhere,
                firstVertex = start / 6 * 4,
                vertexCount = count / 6 * 4,
            }, SubMeshFlags);
            _first = first;
            _last = last;
            return true;
        }

        static void Widen(Camera cam, float seaY, Vector4 fog, ref int first, ref int last)
        {
            Vector3 p = cam.transform.position;
            VisibleLevels(p.y - seaY, FogReach(fog, new Vector2(p.x, p.z), seaY), out int f, out int l);
            if (f > l) return;
            first = Mathf.Min(first, f);
            last = Mathf.Max(last, l);
        }

        // CurvedWorld sets the haze per camera before it renders; right after the main camera it is still the main one's.
        static void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != Camera.main) return;
            s_mainFog = Shader.GetGlobalVector(FogParamsId);
            s_mainFogFrame = Time.frameCount;
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
            mesh.bounds = Everywhere;
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
