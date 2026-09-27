using UnityEngine;

namespace Drift.Core
{
    // Close-up smoothness for meshes the CPU bakes at a lower rate than the frame (herds, critters, fish, sea life,
    // folk, flocks). Owner, 2026-09-25: "Prüfe bei allen Tieren/Pflanzen, ob die mit rangezoomter Kamera zittern" -
    // a hare hopping at 1.3 u/s in a 15 Hz mesh stepped 0.09 u every fourth frame, ~60 px with the watch camera 3 u
    // away. Two tools:
    //  - Interval(): how often such a mesh has to be rebuilt so a mover never steps more than PixelBudget pixels on
    //    screen between two rebuilds. Close to the camera that is every frame, far away the slow rate stays.
    //  - Motion channel (Shaders/DriftMotion.hlsl): a mesh may carry each vertex's position of the previous bake
    //    plus Encode(duration) and the shader slides from there to the baked position, so a 15 Hz mesh moves
    //    smoothly at any frame rate without a rebuild per frame. The clock is wrapped (Period) so it keeps its
    //    precision on phones however long the app runs.
    public static class CloseUpMotion
    {
        public const float Period = 128f;
        // Durations are stored in whole 1/60 s steps (1..MaxCode) above the wrapped clock.
        public const float CodeScale = 256f;
        public const int MaxCode = 30;
        public static float PixelBudget = 0.6f;

        static readonly int ClockId = Shader.PropertyToID("_DriftMotionClock");
        static int _frame = -1;
        static float _clock, _pxPerUnit;

        // Refreshes the shader clock once per frame; every system that bakes a motion channel calls it in its step.
        public static void Tick()
        {
            int f = Time.frameCount;
            if (f == _frame) return;
            _frame = f;
            _clock = Wrap(Time.timeAsDouble);
            Shader.SetGlobalFloat(ClockId, _clock);
            var cam = Camera.main;
            _pxPerUnit = cam != null && cam.fieldOfView > 0.1f
                ? Screen.height / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) : 0f;
        }

        public static float Wrap(double seconds) => (float)(seconds - System.Math.Floor(seconds / Period) * Period);

        // The motion channel's w for a bake now that slides over `duration` seconds. Anything below CodeScale means
        // "no slide" (a mesh without the channel reads 0, or 1 on GLES).
        public static float Encode(float duration)
        {
            Tick();
            return Encode(_clock, duration);
        }

        public static float Encode(float clock, float duration)
        {
            int code = Mathf.Clamp(Mathf.RoundToInt(duration * 60f), 1, MaxCode);
            return 1f + clock + CodeScale * code;
        }

        // C# twin of DriftMotion.hlsl: how far (0..1) a slide encoded as w has come at `clock`.
        public static float Progress(float w, float clock)
        {
            if (w < CodeScale) return 1f;
            float code = Mathf.Floor(w / CodeScale);
            float at = w - code * CodeScale - 1f;
            // w carries the bake time a fraction of a millisecond off: a tiny negative age is "just baked" (wrapping
            // it made every fresh bake flash to its end for one frame), only a big one means the clock wrapped.
            float age = clock - at;
            if (age < -1f) age += Period;
            return Mathf.Clamp01(age / Mathf.Max(code / 60f, 1e-3f));
        }

        // Screen pixels per world unit at `distance` from the main camera (0 without a camera).
        public static float PixelsPerUnit(float distance)
        {
            Tick();
            return _pxPerUnit / Mathf.Max(0.3f, distance);
        }

        // Rebuild interval so a mover at `speed` u/s, `distance` from the camera, steps at most PixelBudget pixels per
        // rebuild; never longer than slowInterval. Below a frame it simply means "every frame".
        public static float Interval(float distance, float speed, float slowInterval) =>
            Interval(PixelsPerUnit(distance), speed, slowInterval, PixelBudget);

        public static float Interval(float pixelsPerUnit, float speed, float slowInterval, float budget)
        {
            if (slowInterval <= 0f || speed <= 0f || pixelsPerUnit <= 0f) return slowInterval;
            float px = speed * slowInterval * pixelsPerUnit;
            return px <= budget ? slowInterval : slowInterval * Mathf.Max(0f, budget) / px;
        }
    }
}
