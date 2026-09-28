using System;

namespace Drift.Audio
{
    public interface ISynthSource
    {
        void Render(float[] buffer, int channels, int sampleRate);
    }

    public static class SynthMath
    {
        public const int SineSize = 4096;
        const int SineMask = SineSize - 1;
        public static readonly float[] SineTable = BuildSine();

        static float[] BuildSine()
        {
            var t = new float[SineSize];
            for (int i = 0; i < SineSize; i++) t[i] = (float)Math.Sin(i * (2.0 * Math.PI / SineSize));
            return t;
        }

        // Any non-negative phase works (integer part is masked away), so harmonics can be read as Sine(p * k).
        public static float Sine(float phase) => SineTable[(int)(phase * SineSize) & SineMask];

        public static float MidiToHz(int midi) => 440f * (float)Math.Pow(2.0, (midi - 69) / 12.0);

        public static float OnePoleCoef(float cutoffHz, int sampleRate) =>
            1f - (float)Math.Exp(-2.0 * Math.PI * cutoffHz / sampleRate);

        public static float DecayCoef(float tauSeconds, int sampleRate) =>
            (float)Math.Exp(-1.0 / (tauSeconds * sampleRate));

        public static float SmoothCoef(float tauSeconds, float dt) =>
            tauSeconds <= 0f ? 1f : 1f - (float)Math.Exp(-dt / tauSeconds);

        public static float SvfCoef(float cutoffHz, int sampleRate)
        {
            float f = 2f * (float)Math.Sin(Math.PI * cutoffHz / sampleRate);
            return f > 0.9f ? 0.9f : f;
        }

        // Linear below 0.6, then u/(1+u) knee: continuous in value and slope, bounded below 1.0.
        public static float SoftClip(float x)
        {
            if (x > 0.6f)
            {
                float u = (x - 0.6f) * 2.5f;
                return 0.6f + 0.4f * u / (1f + u);
            }
            if (x < -0.6f)
            {
                float u = (-x - 0.6f) * 2.5f;
                return -0.6f - 0.4f * u / (1f + u);
            }
            return x;
        }

        public static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);

        public static void Pan(float pan, out float l, out float r)
        {
            l = (float)Math.Sqrt(0.5f * (1f - pan));
            r = (float)Math.Sqrt(0.5f * (1f + pan));
        }

        public static void WriteInterleaved(float[] buffer, int channels, int frameOffset, float[] l, float[] r, int frames)
        {
            if (channels == 1)
            {
                for (int i = 0; i < frames; i++) buffer[frameOffset + i] = 0.5f * (l[i] + r[i]);
                return;
            }
            int idx = frameOffset * channels;
            for (int i = 0; i < frames; i++)
            {
                buffer[idx] = l[i];
                buffer[idx + 1] = r[i];
                for (int c = 2; c < channels; c++) buffer[idx + c] = 0f;
                idx += channels;
            }
        }
    }

    public struct Noise
    {
        uint _s;

        public Noise(uint seed) { _s = seed == 0 ? 0x9E3779B9u : seed; }

        public float Next()
        {
            _s = _s * 1664525u + 1013904223u;
            return (_s >> 8) * (1f / 8388608f) - 1f;
        }

        public float Next01()
        {
            _s = _s * 1664525u + 1013904223u;
            return (_s >> 8) * (1f / 16777216f);
        }

        public int Range(int n)
        {
            int v = (int)(Next01() * n);
            return v >= n ? n - 1 : v;
        }
    }
}
