using System.Collections.Generic;
using Drift.Islands;
using UnityEngine;
using UnityEngine.Rendering;

namespace Drift.Visuals
{
    // The fair-weather clouds: puffy clumps ("Schaefchenwolken") drifting with the wind over the whole map, and their
    // shadows. Both come from one procedural clump field (Shaders/DriftClouds.hlsl, mirrored by CloudField) driven by
    // the globals pushed here, so every cloud in the sky has its shadow on the sea, the islands and the animals.
    // The puffs are one static mesh (Drift/Clouds): the shader picks the clump of each slot, so nothing is rebuilt.
    // Wind and storm intensity come from WaterFeedback.
    [ExecuteAlways]
    [DefaultExecutionOrder(205)]
    public class CloudShadows : MonoBehaviour
    {
        public WaterFeedback water;
        [Range(0f, 1f)] public float baseCover = 0.28f;
        [Range(0f, 0.5f)] public float coverVariation = 0.16f;
        public float variationPeriod = 170f;
        [Range(0f, 1f)] public float stormCover = 0.55f;
        [Range(0f, 0.6f)] public float shadowStrength = 0.3f;
        public float driftSpeed = 1.2f;
        public float minDrift = 0.25f;
        public float coverSmoothing = 0.6f;
        [Range(-1f, 1f)] public float coverOverride = -1f;

        [Header("Schäfchenwolken")]
        [Tooltip("Material mit dem Shader Drift/Clouds. Leer = nur Wolkenschatten, keine Wolken am Himmel.")]
        public Material cloudMaterial;
        [Tooltip("Abstand der Wolkenballen (Welteinheiten pro Rasterzelle, je Zelle höchstens ein Ballen).")]
        [Range(10f, 80f)] public float clumpSpacing = 18f;
        [Tooltip("Wolkenhöhe als Anteil der Kamerahöhe: beim Hineinzoomen sinken die Wolken mit, bleiben aber sichtbar.")]
        [Range(0.2f, 1f)] public float heightFactor = 0.42f;
        [Tooltip("Kleinste / größte Wolkenhöhe über dem Meer.")]
        public Vector2 heightRange = new Vector2(3.5f, 14f);
        [Tooltip("Schatten fallen versetzt in Richtung der Sonne (0 = senkrecht unter der Wolke).")]
        [Range(0f, 1f)] public float sunShift = 1f;

        [Header("Abenteuer-Ring")]
        [Tooltip("Die Bahn vor der Insel bleibt wolkenfrei: halbe Breite des freien Streifens quer zur Bahn (Einheiten, um die Insel herum).")]
        [Range(0f, 40f)] public float ringClearHalfWidth = 11f;
        [Tooltip("Über diese Breite blenden die Wolken neben dem freien Streifen wieder ein.")]
        [Range(0.5f, 30f)] public float ringClearFade = 7f;
        [Tooltip("So weit vor der Insel bleibt die Bahn frei (0 = aus: Wolken überall).")]
        [Range(0f, 300f)] public float ringClearAhead = 75f;
        [Tooltip("Über diese Strecke dahinter kommen die Wolken als Kulisse zurück.")]
        [Range(1f, 150f)] public float ringClearFadeLength = 45f;
        [Tooltip("Stärke der Wolkenschatten auf dem Ring: über der Bahn hängen dort kaum Wolken, dunkle Flecken ohne Wolke sähen wie Hindernisse aus.")]
        [Range(0f, 0.6f)] public float ringShadowStrength = 0.08f;

        public const int GridSize = 21, PuffsPerClump = 9;

        static readonly int CoverId = Shader.PropertyToID("_CloudCover");
        static readonly int OffsetId = Shader.PropertyToID("_CloudOffset");
        static readonly int ScaleId = Shader.PropertyToID("_CloudScale");
        static readonly int StrengthId = Shader.PropertyToID("_CloudShadowStrength");
        static readonly int ShiftId = Shader.PropertyToID("_CloudShadowShift");
        static readonly int FocusId = Shader.PropertyToID("_CloudFocus");
        static readonly int HeightId = Shader.PropertyToID("_CloudHeight");
        static readonly int GridHalfId = Shader.PropertyToID("_CloudGridHalf");
        static readonly int RingClearId = Shader.PropertyToID("_CloudRingClear");

        Vector2 _offset;
        float _cover;
        float _clock;
        float _height = -1f;
        Vector2 _shift;
        Island _player;
        Mesh _mesh;
        MeshRenderer _renderer;

        public float Cover => _cover;
        public Vector2 Offset => _offset;
        public Vector2 Wind { get; private set; }
        public float Clock => _clock;
        public float Scale => 1f / Mathf.Max(1f, clumpSpacing);
        public Vector2 Shift => _shift;
        // Current height of the cloud layer (StormVisuals puts the storm clouds and the tops of its bolts there).
        public float Height => _height > 0f ? _height : heightRange.x;
        public static CloudShadows Instance { get; private set; }

        // 0..1 cloud density over a world point, as the shaders see it.
        public float DensityAt(Vector2 worldXZ) => CloudField.DensityAtWorld(worldXZ, _cover, Scale, _offset, _shift);

        // The cloud-free stretch of the adventure track as Drift/Clouds reads it (see RingReadability.CloudCorridor).
        public Vector4 RingClear => new Vector4(ringClearHalfWidth, ringClearFade, ringClearAhead, ringClearFadeLength);

        void OnEnable()
        {
            Instance = this;
            Resolve();
            Step(0f);
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            Shader.SetGlobalFloat(CoverId, 0f);
            Shader.SetGlobalVector(FocusId, Vector4.zero);
            if (_renderer != null) _renderer.enabled = false;
        }

        void OnDestroy()
        {
            if (_mesh != null) DestroyImmediate(_mesh);
        }

        void LateUpdate()
        {
            Step(Application.isPlaying ? Time.deltaTime : 0f);
        }

        void Resolve()
        {
            if (water == null) water = GetComponent<WaterFeedback>();
            if (water == null) water = FindAnyObjectByType<WaterFeedback>();
        }

        public void Step(float dt)
        {
            if (water == null) Resolve();
            _clock += dt;

            float storm = 0f;
            if (water != null)
            {
                Wind = water.Wind;
                storm = water.Storm;
            }
            else
            {
                float a = _clock * 0.02f;
                Wind = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.6f;
            }

            Vector2 drift = Wind * driftSpeed;
            float m = drift.magnitude;
            if (m < minDrift) drift = (m > 1e-4f ? drift / m : Vector2.right) * minDrift;
            float scale = Scale;
            _offset -= drift * (dt * scale);

            float slow = Mathf.Sin(_clock * (Mathf.PI * 2f / Mathf.Max(1f, variationPeriod)))
                       * 0.6f + Mathf.Sin(_clock * (Mathf.PI * 2f / Mathf.Max(1f, variationPeriod * 0.37f)) + 1.3f) * 0.4f;
            float target = Mathf.Clamp01(baseCover + coverVariation * slow + stormCover * storm);
            if (coverOverride >= 0f) target = coverOverride;
            float k = dt > 0f ? 1f - Mathf.Exp(-coverSmoothing * dt) : 1f;
            _cover = Mathf.Lerp(_cover, target, k);

            var cam = Camera.main;
            float targetHeight = Mathf.Clamp((cam != null ? cam.transform.position.y : 20f) * heightFactor, heightRange.x, Mathf.Max(heightRange.x, heightRange.y));
            float hk = dt > 0f && _height > 0f ? 1f - Mathf.Exp(-3f * dt) : 1f;
            _height = Mathf.Lerp(_height, targetHeight, hk);

            Vector2 shiftTarget = Vector2.zero;
            var sun = RenderSettings.sun;
            if (sun != null && sunShift > 0f)
            {
                Vector3 l = -sun.transform.forward;
                if (l.y > 0.02f)
                {
                    shiftTarget = new Vector2(l.x, l.z) / Mathf.Max(l.y, 0.35f) * (_height * sunShift);
                    shiftTarget *= Mathf.Clamp01(l.y * 8f);
                }
            }
            _shift = Vector2.Lerp(_shift, shiftTarget, hk);

            Shader.SetGlobalFloat(CoverId, _cover);
            Shader.SetGlobalVector(OffsetId, new Vector4(_offset.x, _offset.y, 0f, 0f));
            Shader.SetGlobalFloat(ScaleId, scale);
            var ring = RingWorld.Active;
            Shader.SetGlobalFloat(StrengthId, ring != null && ring.IsApplied ? ringShadowStrength : shadowStrength);
            Shader.SetGlobalVector(ShiftId, new Vector4(_shift.x, _shift.y, 0f, 0f));
            Shader.SetGlobalFloat(HeightId, _height);
            Shader.SetGlobalVector(FocusId, Focus(cam));
            Shader.SetGlobalFloat(GridHalfId, GridSize * 0.5f);
            Shader.SetGlobalVector(RingClearId, RingClear);

            UpdatePuffs();
        }

        Vector4 Focus(Camera cam)
        {
            if (_player == null || !_player.isActiveAndEnabled)
            {
                _player = null;
                var all = Island.All;
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && all[i].useKeyboardInput) { _player = all[i]; break; }
            }
            if (_player != null)
            {
                Vector2 p = _player.PlanarPosition;
                return new Vector4(p.x, 0f, p.y, Mathf.Max(1f, _player.BoundingRadius));
            }
            if (cam == null) return Vector4.zero;
            // No player: keep the clump grid round the point the camera looks at, without a clear view.
            Vector3 o = cam.transform.position, f = cam.transform.forward;
            float t = f.y < -0.05f ? -o.y / f.y : 50f;
            Vector3 g = o + f * Mathf.Min(t, 300f);
            return new Vector4(g.x, 0f, g.z, 0f);
        }

        // ------------------------------------------------------------ puff mesh

        void UpdatePuffs()
        {
            if (cloudMaterial == null)
            {
                if (_renderer != null) _renderer.enabled = false;
                return;
            }
            if (_renderer == null || _mesh == null) BuildPuffObject();
            _renderer.sharedMaterial = cloudMaterial;
            _renderer.enabled = _cover > 0.001f;
        }

        void BuildPuffObject()
        {
            const string name = "CloudPuffs";
            var t = transform.Find(name);
            GameObject go = t != null ? t.gameObject : new GameObject(name);
            go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null) mf = go.AddComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (_mesh == null) _mesh = BuildPuffMesh();
            mf.sharedMesh = _mesh;
            _renderer = mr;
        }

        public static Mesh BuildPuffMesh()
        {
            int half = GridSize / 2;
            int quads = GridSize * GridSize * PuffsPerClump;
            var v = new List<Vector3>(quads * 4);
            var uv0 = new List<Vector4>(quads * 4);
            var uv1 = new List<Vector4>(quads * 4);
            var tris = new List<int>(quads * 6);
            for (int z = -half; z <= half; z++)
            for (int x = -half; x <= half; x++)
            for (int k = 0; k < PuffsPerClump; k++)
            {
                int s = v.Count;
                for (int c = 0; c < 4; c++)
                {
                    v.Add(Vector3.zero);
                    uv0.Add(new Vector4(x, z, k, 0f));
                    uv1.Add(new Vector4((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, 0f, 0f));
                }
                tris.Add(s); tris.Add(s + 2); tris.Add(s + 1);
                tris.Add(s + 1); tris.Add(s + 2); tris.Add(s + 3);
            }
            var mesh = new Mesh { name = "CloudPuffs", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetTriangles(tris, 0, false);
            // Every vertex sits at the origin; the shader places them anywhere round the player.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            return mesh;
        }
    }
}
