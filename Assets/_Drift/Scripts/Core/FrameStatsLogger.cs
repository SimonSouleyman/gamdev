using UnityEngine;
using UnityEngine.Profiling;

namespace Drift.Core
{
    // Development builds on a phone write a frame-time summary to the log every few seconds ("Drift-FPS ..."), so an
    // adb logcat recording of a play session shows the frame rate - Android's own frame statistics (gfxinfo,
    // SurfaceFlinger latency) report nothing for Unity's Vulkan surface.
    public sealed class FrameStatsLogger : MonoBehaviour
    {
        public const float Interval = 10f;
        const int MaxFrames = 2048;

        readonly float[] _ms = new float[MaxFrames];
        int _count;
        float _elapsed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Debug.isDebugBuild || !Application.isMobilePlatform) return;
            var go = new GameObject("FrameStatsLogger") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<FrameStatsLogger>();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_count < MaxFrames) _ms[_count++] = dt * 1000f;
            _elapsed += dt;
            if (_elapsed < Interval) return;
            Debug.Log(Summary(_ms, _count, _elapsed) + " mode " + GameModes.Current
                + " mono " + (Profiler.GetMonoUsedSizeLong() >> 20) + "MB");
            _count = 0;
            _elapsed = 0f;
        }

        // "Drift-FPS avg 58.9 p95 21.3ms max 48.0ms slow 3/590": slow = frames over 33 ms (below 30 fps).
        public static string Summary(float[] ms, int count, float seconds)
        {
            if (count <= 0 || seconds <= 0f) return "Drift-FPS none";
            float max = 0f;
            int slow = 0;
            for (int i = 0; i < count; i++)
            {
                if (ms[i] > max) max = ms[i];
                if (ms[i] > 33.4f) slow++;
            }
            var sorted = new float[count];
            System.Array.Copy(ms, sorted, count);
            System.Array.Sort(sorted);
            float p95 = sorted[Mathf.Min(count - 1, (int)(count * 0.95f))];
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Drift-FPS avg {0:F1} p95 {1:F1}ms max {2:F1}ms slow {3}/{4}", count / seconds, p95, max, slow, count);
        }
    }
}
