using Drift.Islands;
using UnityEngine;
using UnityEngine.Rendering;

namespace Drift.Visuals
{
    // Thin streaks rushing outward at the edges of the screen while a boost runs (and a hint at top speed): the
    // on-screen answer to "Schub ×1.6". Reads the chase camera on the same object (boost share, pickup swell, top
    // speed) and draws one mesh straight in clip space for that camera only - no GameObject, no per-frame
    // allocation. Drawn under the HUD; nothing while suspended (photo/watch), following or zoomed close.
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(200)]
    public class SpeedStreaks : MonoBehaviour
    {
        [Tooltip("Tempostreifen am Bildrand bei Schub. Aus = keine.")]
        public bool streaksEnabled = true;
        [Tooltip("Material der Streifen (leer = Drift/SpeedStreaks).")]
        public Material material;
        [Tooltip("Wie viele Streifen gleichzeitig unterwegs sind.")]
        [Range(4, 64)] public int count = 26;
        [Tooltip("Deckkraft der Streifen bei voller Stärke.")]
        [Range(0f, 1f)] public float opacity = 0.4f;
        [Tooltip("Stärke bei vollem Schub.")]
        [Range(0f, 1.5f)] public float boostStrength = 1f;
        [Tooltip("Zusätzliche Stärke kurz nach dem Einsammeln eines Schubs.")]
        [Range(0f, 1.5f)] public float pickupStrength = 0.6f;
        [Tooltip("Stärke schon ohne Schub bei Höchsttempo (0 = nur beim Schub).")]
        [Range(0f, 1f)] public float topSpeedStrength = 0.2f;
        [Tooltip("Bildmitte bleibt frei: die Streifen beginnen erst so weit von der Fluchtrichtung weg (Anteil des Wegs zum Bildrand).")]
        [Range(0.1f, 1.2f)] public float innerRadius = 0.55f;
        [Tooltip("Länge eines Streifens (Anteil der halben Bildhöhe).")]
        [Range(0.05f, 1f)] public float length = 0.3f;
        [Tooltip("Breite eines Streifens (Anteil der halben Bildhöhe).")]
        [Range(0.001f, 0.03f)] public float width = 0.006f;
        [Tooltip("Wie schnell die Streifen nach außen ziehen (Durchläufe pro Sekunde bei voller Stärke).")]
        [Range(0.2f, 4f)] public float rate = 1.6f;
        [Tooltip("Wie schnell die Streifen kommen und gehen (klein = weich).")]
        [Range(0.5f, 12f)] public float response = 6f;

        static readonly int ParamsId = Shader.PropertyToID("_StreakParams");
        static readonly int ShapeId = Shader.PropertyToID("_StreakShape");
        const string ShaderName = "Drift/SpeedStreaks";

        Camera _cam;
        IslandChaseCamera _chase;
        Mesh _mesh;
        int _meshCount;
        Material _runtimeMat;
        MaterialPropertyBlock _block;
        float _clock;

        public float Amount { get; private set; }

        // How strong the streaks want to be (0..1+) from the chase camera's feel values.
        public static float Target(float boostFeel, float boostPunch, float topSpeedShare, float modeScale,
            float boostStrength, float pickupStrength, float topSpeedStrength) =>
            Mathf.Max(0f, modeScale * (boostStrength * boostFeel + pickupStrength * boostPunch) + topSpeedStrength * topSpeedShare);

        // Where the streaks radiate from (viewport 0..1): the far point along the travel direction, kept inside the
        // upper-middle of the screen (a steep cozy view would put it far off the top).
        public static Vector2 Focus(Camera cam, Vector3 travel)
        {
            Vector3 flat = new Vector3(travel.x, 0f, travel.z);
            if (flat.sqrMagnitude < 1e-6f) flat = new Vector3(cam.transform.forward.x, 0f, cam.transform.forward.z);
            if (flat.sqrMagnitude < 1e-6f) return new Vector2(0.5f, 0.6f);
            Vector3 v = cam.WorldToViewportPoint(cam.transform.position + flat.normalized * 400f);
            if (v.z <= 0f) return new Vector2(0.5f, 0.6f);
            return new Vector2(Mathf.Clamp(v.x, 0.3f, 0.7f), Mathf.Clamp(v.y, 0.45f, 0.7f));
        }

        void OnDisable()
        {
            Amount = 0f;
        }

        void OnDestroy()
        {
            if (_mesh != null) Kill(_mesh);
            if (_runtimeMat != null) Kill(_runtimeMat);
        }

        static void Kill(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        void LateUpdate()
        {
            if (!Application.isPlaying) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (_cam == null) _cam = GetComponent<Camera>();
            if (_chase == null) _chase = GetComponent<IslandChaseCamera>();

            float want = 0f;
            if (streaksEnabled && _chase != null && _chase.isActiveAndEnabled && _chase.target != null && _chase.FeelVisible)
                want = Target(_chase.BoostFeel, _chase.BoostPunch, _chase.TopSpeedShare, _chase.BoostModeScale,
                    boostStrength, pickupStrength, topSpeedStrength);
            Amount = Mathf.Lerp(Amount, Mathf.Min(1.5f, want), 1f - Mathf.Exp(-response * dt));
            if (Amount < 0.01f) return;

            var mat = ResolveMaterial();
            if (mat == null || _cam == null) return;
            EnsureMesh();

            _clock = Mathf.Repeat(_clock + dt * rate * (0.6f + 0.4f * Mathf.Min(1f, Amount)), 4096f);
            Vector2 focus = Focus(_cam, _chase.target.PlanarVelocity.sqrMagnitude > 0.25f
                ? new Vector3(_chase.target.PlanarVelocity.x, 0f, _chase.target.PlanarVelocity.y)
                : _chase.ViewForward);
            _block ??= new MaterialPropertyBlock();
            _block.SetVector(ParamsId, new Vector4(Mathf.Clamp01(Amount) * opacity, _clock, length, width));
            _block.SetVector(ShapeId, new Vector4(focus.x * 2f - 1f, focus.y * 2f - 1f, innerRadius, _cam.aspect));

            Transform t = _cam.transform;
            var rp = new RenderParams(mat)
            {
                camera = _cam,
                layer = gameObject.layer,
                matProps = _block,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                // Renderer culling still applies (CurvedWorld sets its own culling volumes): a small box just in
                // front of the camera is inside every one of them.
                worldBounds = new Bounds(t.position + t.forward * 2f, Vector3.one * 2f),
            };
            Graphics.RenderMesh(rp, _mesh, 0, Matrix4x4.identity);
        }

        Material ResolveMaterial()
        {
            if (material != null) return material;
            if (_runtimeMat == null)
            {
                var shader = Shader.Find(ShaderName);
                if (shader == null) return null;
                _runtimeMat = new Material(shader) { name = "SpeedStreaks (runtime)", hideFlags = HideFlags.DontSave };
            }
            return _runtimeMat;
        }

        void EnsureMesh()
        {
            int n = Mathf.Clamp(count, 4, 64);
            if (_mesh != null && _meshCount == n) return;
            if (_mesh == null) _mesh = new Mesh { name = "SpeedStreaks", hideFlags = HideFlags.DontSave };
            BuildMesh(_mesh, n);
            _meshCount = n;
        }

        // One quad per streak, spread evenly round the focus with a fixed jitter (deterministic, same every run).
        public static void BuildMesh(Mesh mesh, int n)
        {
            var verts = new Vector3[n * 4];
            var data = new Vector4[n * 4];
            var tris = new int[n * 6];
            float sector = 2f * Mathf.PI / n;
            for (int i = 0; i < n; i++)
            {
                float seed = Frac(Mathf.Sin((i + 1) * 78.233f) * 43758.5453f);
                float angle = (i + 0.5f) * sector + (seed - 0.5f) * sector * 0.5f;
                float len = 0.7f + 0.6f * Frac(seed * 13.7f);
                var d = new Vector4(angle, seed, len, sector);
                int v = i * 4;
                verts[v] = new Vector3(0f, -1f, 0f);
                verts[v + 1] = new Vector3(0f, 1f, 0f);
                verts[v + 2] = new Vector3(1f, -1f, 0f);
                verts[v + 3] = new Vector3(1f, 1f, 0f);
                data[v] = data[v + 1] = data[v + 2] = data[v + 3] = d;
                int k = i * 6;
                tris[k] = v; tris[k + 1] = v + 1; tris[k + 2] = v + 2;
                tris[k + 3] = v + 2; tris[k + 4] = v + 1; tris[k + 5] = v + 3;
            }
            mesh.Clear();
            mesh.vertices = verts;
            mesh.SetUVs(0, data);
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e4f);
        }

        static float Frac(float x) => x - Mathf.Floor(x);
    }
}
