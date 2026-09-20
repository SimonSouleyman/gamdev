using UnityEngine;

namespace Drift.Visuals
{
    // Drifting cloud shadows as global shader uniforms (see Shaders/DriftClouds.hlsl): the water,
    // island terrain and vegetation shaders all sample the same procedural cloud field, so one
    // cloud darkens sea and land together. Wind and storm intensity come from WaterFeedback.
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
        public float cloudSize = 70f;
        public float driftSpeed = 1.2f;
        public float minDrift = 0.25f;
        public float coverSmoothing = 0.6f;
        [Range(-1f, 1f)] public float coverOverride = -1f;

        static readonly int CoverId = Shader.PropertyToID("_CloudCover");
        static readonly int OffsetId = Shader.PropertyToID("_CloudOffset");
        static readonly int ScaleId = Shader.PropertyToID("_CloudScale");
        static readonly int StrengthId = Shader.PropertyToID("_CloudShadowStrength");

        Vector2 _offset;
        float _cover;
        float _clock;

        public float Cover => _cover;
        public Vector2 Offset => _offset;
        public Vector2 Wind { get; private set; }
        public float Clock => _clock;

        void OnEnable()
        {
            Resolve();
            Step(0f);
        }

        void OnDisable()
        {
            Shader.SetGlobalFloat(CoverId, 0f);
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
            float scale = cloudSize > 0.01f ? 1f / cloudSize : 0.01f;
            _offset -= drift * (dt * scale);

            float slow = Mathf.Sin(_clock * (Mathf.PI * 2f / Mathf.Max(1f, variationPeriod)))
                       * 0.6f + Mathf.Sin(_clock * (Mathf.PI * 2f / Mathf.Max(1f, variationPeriod * 0.37f)) + 1.3f) * 0.4f;
            float target = Mathf.Clamp01(baseCover + coverVariation * slow + stormCover * storm);
            if (coverOverride >= 0f) target = coverOverride;
            float k = dt > 0f ? 1f - Mathf.Exp(-coverSmoothing * dt) : 1f;
            _cover = Mathf.Lerp(_cover, target, k);

            Shader.SetGlobalFloat(CoverId, _cover);
            Shader.SetGlobalVector(OffsetId, new Vector4(_offset.x, _offset.y, 0f, 0f));
            Shader.SetGlobalFloat(ScaleId, scale);
            Shader.SetGlobalFloat(StrengthId, shadowStrength);
        }
    }
}
