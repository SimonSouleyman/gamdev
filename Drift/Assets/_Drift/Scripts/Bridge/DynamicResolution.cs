using UnityEngine;
using UnityEngine.Rendering;

namespace Drift.Bridge
{
    // Phones only: keeps the GPU inside the 60 fps budget by moving the URP render scale between minScale and the
    // scale the pipeline asset was authored with. Measured on the Fairphone 6 (2026-09-25): GPU-bound, ~18 ms per frame
    // in cozy and ~23 ms on the title at scale 0.8, 13 ms at 0.6 - so a slightly softer picture buys a steady 60 fps.
    // Reads the GPU time from FrameTimingManager (PlayerSettings "Frame Timing Stats" on); where a device reports
    // none it falls back to the frame time while the CPU is clearly not the limit.
    public sealed class DynamicResolution : MonoBehaviour
    {
        // Down only when frames would really start to miss the 16.7 ms vsync; up only with clear headroom (one up step
        // costs ~8 % more GPU time). A first version aimed at 15 ms and sank to 0.62 although 60 fps held at 0.8.
        public const float DownMs = 16.3f, UpMs = 13.5f;
        public const float MinScale = 0.6f;
        const float Window = 0.75f, DownHold = 1.5f, UpHold = 4f, StepDown = 0.05f, StepUp = 0.025f;

        readonly FrameTiming[] _timing = new FrameTiming[1];
        object _asset;
        System.Reflection.PropertyInfo _scaleProp;
        float _maxScale, _scale, _window, _sum, _sinceChange;
        int _n;

        public static float CurrentScale { get; private set; } = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Application.isMobilePlatform) return;
            // The GPU probe measures fixed settings: it must not be fought by the controller.
            if (System.IO.File.Exists(System.IO.Path.Combine(Application.persistentDataPath, "perfprobe"))) return;
            var go = new GameObject("DynamicResolution") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<DynamicResolution>();
        }

        void Start()
        {
            var rp = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
            _scaleProp = rp != null ? rp.GetType().GetProperty("renderScale") : null;
            if (_scaleProp == null) { enabled = false; return; }
            _asset = rp;
            _maxScale = Mathf.Clamp((float)_scaleProp.GetValue(rp), MinScale, 1f);
            _scale = _maxScale;
            CurrentScale = _scale;
        }

        void OnDestroy()
        {
            if (_scaleProp != null && _asset != null) _scaleProp.SetValue(_asset, _maxScale);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _sinceChange += dt;
            _window += dt;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timing) > 0)
            {
                double gpu = _timing[0].gpuFrameTime;
                // No GPU time on this device: nothing to steer by. (The frame time is no substitute - at a steady
                // 60 Hz it reads 16.7 ms, over DownMs, and sank the scale to the minimum on a smooth game.)
                if (gpu > 0) { _sum += (float)gpu; _n++; }
            }
            if (_window < Window) return;
            float avg = _n > 0 ? _sum / _n : 0f;
            _window = _sum = 0f;
            _n = 0;
            if (avg <= 0f) return;
            float next = _scale;
            if (avg > DownMs && _sinceChange > DownHold) next = Mathf.Max(MinScale, _scale - StepDown);
            else if (avg < UpMs && _sinceChange > UpHold) next = Mathf.Min(_maxScale, _scale + StepUp);
            if (Mathf.Approximately(next, _scale)) return;
            _scale = next;
            _sinceChange = 0f;
            CurrentScale = _scale;
            _scaleProp.SetValue(_asset, _scale);
            if (Debug.isDebugBuild) Debug.Log("Drift-RES scale " + _scale.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " gpu " + avg.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "ms");
        }
    }
}
