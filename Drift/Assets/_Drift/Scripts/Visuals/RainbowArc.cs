using UnityEngine;

namespace Drift.Visuals
{
    // The rainbow of a Drift.Visuals.WorldEvents spectacle: a real arc in the world, not an overlay. A rainbow sits
    // on the circle 42 degrees round the point opposite the sun, so it has no place of its own — it is drawn like
    // the sky, on a band of the unit sphere around whichever camera renders it (the vertex shader puts the band
    // just inside the far plane), and the shader takes the exact angle from the arc's axis per pixel. That way it
    // looks right from the chase camera at every zoom, islands in front of it hide it, the sea covers the part
    // below the horizon, and a photo shows it exactly as the game view does.
    //
    // One mesh (240 vertices, 240 triangles), one draw call, no allocation per frame; the renderer is off while
    // nothing runs.
    public sealed class RainbowArc
    {
        public const int Segments = 120;
        // The band has to hold both bows plus the bright sky inside the primary one.
        public const float InnerAngle = 30f;
        public const float OuterAngle = 58f;

        static readonly int StrengthId = Shader.PropertyToID("_ArcStrength");
        static readonly int PrimaryId = Shader.PropertyToID("_ArcPrimary");
        static readonly int SecondaryId = Shader.PropertyToID("_ArcSecondary");

        GameObject _go;
        MeshRenderer _renderer;
        Mesh _mesh;
        MaterialPropertyBlock _block;
        Material _fallback;
        float _strength;

        public bool Visible => _renderer != null && _renderer.enabled;
        public float Strength => _strength;
        public Transform Root => _go != null ? _go.transform : null;

        public Transform parent;

        public void Begin(Vector3 axis, Material material)
        {
            Ensure(material);
            Aim(axis);
        }

        public void End()
        {
            _strength = 0f;
            if (_renderer != null) _renderer.enabled = false;
        }

        public void Step(float dt, float strength, Vector3 axis, Material material, float brightness, float secondary)
        {
            _strength = Mathf.Clamp01(strength);
            if (_strength <= 0.001f)
            {
                if (_renderer != null) _renderer.enabled = false;
                return;
            }
            Ensure(material);
            Aim(axis);
            _renderer.enabled = true;
            _block ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_block);
            _block.SetFloat(StrengthId, _strength * Mathf.Max(0f, brightness));
            // x = outer (red) edge of the primary bow, y = its width, both in degrees.
            _block.SetVector(PrimaryId, new Vector4(42.4f, 2.3f, 0f, 0f));
            // x = inner (red) edge of the secondary bow, y = width, z = how bright it is against the primary.
            _block.SetVector(SecondaryId, new Vector4(50.4f, 3.4f, Mathf.Clamp01(secondary), 0f));
            _renderer.SetPropertyBlock(_block);
        }

        public void Destroy()
        {
            if (_mesh != null) Object.DestroyImmediate(_mesh);
            if (_fallback != null) Object.DestroyImmediate(_fallback);
            if (_go != null) Object.DestroyImmediate(_go);
            _mesh = null;
            _fallback = null;
            _go = null;
            _renderer = null;
        }

        void Aim(Vector3 axis)
        {
            if (_go == null) return;
            Vector3 a = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.down;
            Vector3 up = Mathf.Abs(a.y) > 0.97f ? Vector3.forward : Vector3.up;
            _go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(a, up));
            _go.transform.localScale = Vector3.one;
        }

        void Ensure(Material material)
        {
            if (_mesh == null) _mesh = Build();
            if (_go == null)
            {
                _go = new GameObject("RainbowArc") { hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild };
                if (parent != null) _go.transform.SetParent(parent, false);
                _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                _renderer = _go.AddComponent<MeshRenderer>();
                _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
                _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                _renderer.enabled = false;
            }
            Material mat = material;
            if (mat == null)
            {
                if (_fallback == null)
                {
                    var shader = Shader.Find("Drift/Rainbow");
                    if (shader != null) _fallback = new Material(shader) { name = "RainbowArc (runtime)", hideFlags = HideFlags.DontSave };
                }
                mat = _fallback;
            }
            if (mat != null && _renderer.sharedMaterial != mat) _renderer.sharedMaterial = mat;
        }

        // A band of the unit sphere around the local +Z axis: the vertices are directions, the vertex shader puts
        // them around the camera. Bounds are huge so the band is never culled (its real place is the camera's).
        public static Mesh Build()
        {
            var mesh = new Mesh { name = "RainbowArc", hideFlags = HideFlags.DontSave };
            var verts = new Vector3[Segments * 2];
            var uvs = new Vector2[Segments * 2];
            var tris = new int[Segments * 6];
            float cosIn = Mathf.Cos(InnerAngle * Mathf.Deg2Rad), sinIn = Mathf.Sin(InnerAngle * Mathf.Deg2Rad);
            float cosOut = Mathf.Cos(OuterAngle * Mathf.Deg2Rad), sinOut = Mathf.Sin(OuterAngle * Mathf.Deg2Rad);
            for (int s = 0; s < Segments; s++)
            {
                float phi = s * (Mathf.PI * 2f / Segments);
                float cx = Mathf.Cos(phi), sy = Mathf.Sin(phi);
                verts[s] = new Vector3(cx * sinIn, sy * sinIn, cosIn);
                verts[Segments + s] = new Vector3(cx * sinOut, sy * sinOut, cosOut);
                uvs[s] = new Vector2(0f, s / (float)Segments);
                uvs[Segments + s] = new Vector2(1f, s / (float)Segments);
            }
            for (int s = 0; s < Segments; s++)
            {
                int n = (s + 1) % Segments;
                int i = s * 6;
                tris[i] = s; tris[i + 1] = Segments + s; tris[i + 2] = Segments + n;
                tris[i + 3] = s; tris[i + 4] = Segments + n; tris[i + 5] = n;
            }
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 200000f);
            mesh.UploadMeshData(false);
            return mesh;
        }
    }
}
