using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Drift.Audio;
using NUnit.Framework;
using Unity.Profiling;

namespace Drift.Tests
{
    public class ImpactAudioTests
    {
        public const int Sr = 48000;

        // ------------------------------------------------------------ helpers (public: also driven from eval)

        public static SfxSynth Make(uint seed, bool ambient = false)
        {
            var s = new SfxSynth(Sr, seed);
            if (ambient) { s.WindAmount = 1f; s.WaterAmount = 1f; }
            else { s.WindGain = 0f; s.WaterGain = 0f; s.WhistleGain = 0f; }
            return s;
        }

        public static float[] Render(ISynthSource s, float seconds, int channels = 2, int chunkFrames = 1024)
        {
            int frames = (int)(seconds * Sr);
            var all = new float[frames * channels];
            var buf = new float[chunkFrames * channels];
            int done = 0;
            while (done < frames)
            {
                int n = Math.Min(chunkFrames, frames - done);
                var b = n == chunkFrames ? buf : new float[n * channels];
                s.Render(b, channels, Sr);
                Array.Copy(b, 0, all, done * channels, n * channels);
                done += n;
            }
            return all;
        }

        public static float[] RenderImpact(uint seed, float intensity, float seconds, float duration = 0f, bool ambient = false, int layers = SfxSynth.LayerAll)
        {
            var s = Make(seed, ambient);
            s.Layers = layers;
            if (ambient) Render(s, 1f);
            s.Impact(intensity, duration);
            return Render(s, seconds);
        }

        public static float Peak(float[] x)
        {
            float p = 0f;
            for (int i = 0; i < x.Length; i++)
            {
                if (float.IsNaN(x[i]) || float.IsInfinity(x[i])) return float.NaN;
                float a = Math.Abs(x[i]);
                if (a > p) p = a;
            }
            return p;
        }

        public static float Rms(float[] x, int from, int to)
        {
            double s = 0;
            if (to > x.Length) to = x.Length;
            for (int i = from; i < to; i++) s += x[i] * x[i];
            return (float)Math.Sqrt(s / Math.Max(1, to - from));
        }

        public static float[] Mono(float[] stereo)
        {
            var m = new float[stereo.Length / 2];
            for (int i = 0; i < m.Length; i++) m[i] = 0.5f * (stereo[2 * i] + stereo[2 * i + 1]);
            return m;
        }

        public static float[] Channel(float[] stereo, int c)
        {
            var m = new float[stereo.Length / 2];
            for (int i = 0; i < m.Length; i++) m[i] = stereo[2 * i + c];
            return m;
        }

        static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { double t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2.0 * Math.PI / len;
                double wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = a + len / 2;
                        double xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi;
                        re[a] += xr; im[a] += xi;
                        double ncr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = ncr;
                    }
                }
            }
        }

        // Hann-windowed, half-overlapped power spectrum of [from, to), summed over the windows.
        public static double[] Spectrum(float[] mono, int from, int to, int size = 8192)
        {
            var acc = new double[size / 2];
            var re = new double[size];
            var im = new double[size];
            if (to > mono.Length) to = mono.Length;
            for (int start = from; start + size <= to; start += size / 2)
            {
                for (int i = 0; i < size; i++)
                {
                    re[i] = mono[start + i] * (0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / size));
                    im[i] = 0;
                }
                Fft(re, im);
                for (int k = 0; k < size / 2; k++) acc[k] += re[k] * re[k] + im[k] * im[k];
            }
            return acc;
        }

        public static double BandEnergy(double[] spec, float lowHz, float highHz)
        {
            int size = spec.Length * 2;
            double e = 0;
            for (int k = 1; k < spec.Length; k++)
            {
                double hz = (double)k * Sr / size;
                if (hz >= lowHz && hz < highHz) e += spec[k];
            }
            return e;
        }

        public static float BandShare(float[] mono, int from, int to, float lowHz, float highHz)
        {
            var spec = Spectrum(mono, from, to);
            double total = BandEnergy(spec, 0f, Sr);
            return total > 0 ? (float)(BandEnergy(spec, lowHz, highHz) / total) : 0f;
        }

        // Shares per 1/3-octave band, centres 25 Hz * 2^(k/3), k = 0..29 (25 Hz .. 20 kHz).
        public static float[] ThirdOctaveShares(float[] mono, int from, int to)
        {
            var spec = Spectrum(mono, from, to);
            double total = BandEnergy(spec, 0f, Sr);
            var shares = new float[30];
            for (int k = 0; k < shares.Length; k++)
            {
                double c = 25.0 * Math.Pow(2.0, k / 3.0);
                shares[k] = total > 0 ? (float)(BandEnergy(spec, (float)(c / Math.Pow(2, 1 / 6.0)), (float)(c * Math.Pow(2, 1 / 6.0))) / total) : 0f;
            }
            return shares;
        }

        // Sharp onsets: 2.5 ms RMS of the differentiated signal (rumble drops out) jumping above 1.5x the mean
        // of the preceding 20 ms and above `floor`, with a 15 ms refractory time.
        public static int Onsets(float[] mono, int from, int to, float floor = 0.015f, float ratio = 1.5f, int lookback = 8)
        {
            int win = Sr / 400;
            if (to > mono.Length) to = mono.Length;
            int count = (to - from) / win;
            var env = new float[Math.Max(0, count)];
            for (int w = 0; w < count; w++)
            {
                double s = 0;
                int a = from + w * win;
                for (int i = Math.Max(1, a); i < a + win; i++) { float d = mono[i] - mono[i - 1]; s += d * d; }
                env[w] = (float)Math.Sqrt(s / win);
            }
            int onsets = 0, refractory = 0;
            for (int w = 0; w < count; w++)
            {
                if (refractory > 0) { refractory--; continue; }
                double prev = 0;
                int m = 0;
                for (int k = Math.Max(0, w - lookback); k < w; k++) { prev += env[k]; m++; }
                prev = m > 0 ? prev / m : 0;
                if (env[w] > floor && env[w] > ratio * prev) { onsets++; refractory = 6; }
            }
            return onsets;
        }

        // Seconds until the last 50 ms window above `floor` RMS.
        public static float AudibleSeconds(float[] mono, float floor = 0.003f)
        {
            int win = Sr / 20;
            float last = 0f;
            for (int start = 0; start + win <= mono.Length; start += win)
                if (Rms(mono, start, start + win) > floor) last = (start + win) / (float)Sr;
            return last;
        }

        public static float[] Envelope(float[] mono, int win)
        {
            var e = new float[mono.Length / win];
            for (int w = 0; w < e.Length; w++) e[w] = Rms(mono, w * win, (w + 1) * win);
            return e;
        }

        public static float Correlation(float[] a, float[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            double ma = 0, mb = 0;
            for (int i = 0; i < n; i++) { ma += a[i]; mb += b[i]; }
            ma /= n; mb /= n;
            double sab = 0, saa = 0, sbb = 0;
            for (int i = 0; i < n; i++)
            {
                double x = a[i] - ma, y = b[i] - mb;
                sab += x * y; saa += x * x; sbb += y * y;
            }
            return saa > 0 && sbb > 0 ? (float)(sab / Math.Sqrt(saa * sbb)) : 0f;
        }

        // The fine structure of the 5 ms envelope: divided by its own 250 ms moving average, so the shared
        // swell-grind-crumble shape drops out and only the event pattern is compared.
        public static float[] Texture(float[] mono, float fromSeconds, float toSeconds)
        {
            var window = new float[(int)((toSeconds - fromSeconds) * Sr)];
            Array.Copy(mono, (int)(fromSeconds * Sr), window, 0, window.Length);
            var e = Envelope(window, Sr / 200);
            var t = new float[e.Length];
            for (int i = 0; i < e.Length; i++)
            {
                double s = 0;
                int m = 0;
                for (int k = Math.Max(0, i - 25); k < Math.Min(e.Length, i + 25); k++) { s += e[k]; m++; }
                double mean = s / Math.Max(1, m);
                t[i] = mean > 1e-6 ? (float)(e[i] / mean) : 0f;
            }
            return t;
        }

        // Standard deviation of the left/right balance (-1..1) over the audible 5 ms windows: 0 = everything centred.
        public static float PanSpread(float[] stereo, float floor = 0.003f)
        {
            float[] l = Channel(stereo, 0), r = Channel(stereo, 1);
            int win = Sr / 200, m = 0;
            double sum = 0, sq = 0;
            for (int start = 0; start + win <= l.Length; start += win)
            {
                float a = Rms(l, start, start + win), b = Rms(r, start, start + win);
                if (a + b < 2f * floor) continue;
                double bal = (b - a) / (a + b);
                sum += bal; sq += bal * bal; m++;
            }
            if (m < 2) return 0f;
            double mean = sum / m;
            return (float)Math.Sqrt(Math.Max(0, sq / m - mean * mean));
        }

        public static void WriteWav(string path, float[] interleaved, int channels = 2)
        {
            using var fs = new FileStream(path, FileMode.Create);
            using var w = new BinaryWriter(fs);
            int bytes = interleaved.Length * 2;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)channels);
            w.Write(Sr); w.Write(Sr * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            for (int i = 0; i < interleaved.Length; i++)
            {
                float v = interleaved[i];
                if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                w.Write((short)Math.Round(v * 32767f));
            }
        }

        // Managed allocations while `action` runs, read from the profiler counters (GC.GetAllocatedBytesForCurrentThread
        // returns 0 in this Mono). Best of `tries`, so another thread allocating in between does not fail the test.
        public static long AllocationsDuring(Action action, int tries = 3)
        {
            long best = long.MaxValue;
            for (int t = 0; t < tries; t++)
            {
                using var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count");
                if (!rec.Valid) return -1;
                long before = rec.CurrentValue;
                action();
                long d = rec.CurrentValue - before;
                if (d < best) best = d;
                if (best == 0) break;
            }
            return best;
        }

        // Milliseconds of CPU per second of rendered audio.
        public static double MsPerSecond(ISynthSource s, float seconds, Action<int> perChunk = null)
        {
            var buf = new float[1024 * 2];
            int chunks = (int)(seconds * Sr / 1024);
            var sw = Stopwatch.StartNew();
            for (int c = 0; c < chunks; c++)
            {
                perChunk?.Invoke(c);
                s.Render(buf, 2, Sr);
            }
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds / (chunks * 1024.0 / Sr);
        }

        // ------------------------------------------------------------ stability and level

        [Test]
        public void Impact_HasNoNaN_AndStaysBelowThePeakLimit_WithWindAndWaterOnTop()
        {
            foreach (float intensity in new[] { 0.1f, 0.5f, 1f })
                for (uint seed = 1; seed <= 3; seed++)
                {
                    var x = RenderImpact(seed * 7919u, intensity, 6f, 0f, true);
                    float peak = Peak(x);
                    Assert.False(float.IsNaN(peak), $"NaN at {intensity}");
                    Assert.LessOrEqual(peak, 0.9f, $"peak at {intensity} seed {seed}");
                }
        }

        [Test]
        public void EverythingAtOnce_StaysBelowThePeakLimit()
        {
            var s = Make(99u, true);
            s.SinkWarning = 1f;
            s.GrindAmount = 1f;
            Render(s, 1f);
            s.Impact(1f, 3f);
            s.ShipBeached(1f);
            s.BigSplash(1f);
            s.WhaleBlow();
            var x = Render(s, 6f);
            float peak = Peak(x);
            Assert.False(float.IsNaN(peak));
            Assert.LessOrEqual(peak, 0.9f);
        }

        [Test]
        public void Impact_IsAudible_AndFallsSilentAgain()
        {
            var s = Make(5u);
            Assert.AreEqual(0f, Peak(Render(s, 0.5f)), "silent before");
            Assert.False(s.BusActive);
            s.Impact(0.5f);
            var x = Mono(Render(s, 8f));
            Assert.Greater(Rms(x, 0, Sr), 0.02f, "first second");
            Assert.AreEqual(1, s.ImpactsPlayed);
            Assert.AreEqual(0f, Peak(Render(s, 0.5f)), "silent after");
            Assert.False(s.BusActive, "the bus costs nothing once the debris has settled");
        }

        [Test]
        public void Render_HandlesMonoAndOddBufferSizes()
        {
            var s = Make(6u, true);
            s.Impact(1f);
            var mono = Render(s, 2f, 1, 1000);
            Assert.False(float.IsNaN(Peak(mono)));
            Assert.Greater(Peak(mono), 0f);
            var odd = new float[3 * 777];
            s.Render(odd, 3, Sr);
            Assert.False(float.IsNaN(Peak(odd)));
            var other = new float[2 * 512];
            s.Render(other, 2, 44100);
            Assert.False(float.IsNaN(Peak(other)));
        }

        // ------------------------------------------------------------ character

        [Test]
        public void Grind_IsBroadbandRock_NotALowTone()
        {
            foreach (float intensity in new[] { 0.1f, 0.5f, 1f })
            {
                var x = Mono(RenderImpact(11u, intensity, 6f, 2.5f));
                int from = (int)(0.4f * Sr), to = (int)(2.5f * Sr);
                float mid = BandShare(x, from, to, 200f, 4000f);
                Assert.GreaterOrEqual(mid, 0.55f, $"200 Hz - 4 kHz share at {intensity}");
                var bands = ThirdOctaveShares(x, from, to);
                float top = 0f;
                for (int k = 0; k < bands.Length; k++) top = Math.Max(top, bands[k]);
                Assert.LessOrEqual(top, 0.35f, $"largest 1/3-octave share at {intensity}");
                Assert.Less(BandShare(x, from, to, 6000f, 24000f), 0.05f, $"no harsh highs at {intensity}");
                Assert.Greater(BandShare(x, from, to, 30f, 300f), 0.08f, $"the rumble and its body band are there at {intensity}");
            }
        }

        [Test]
        public void Cracks_AndOnsets_GrowWithIntensity()
        {
            int prevCracks = 0, prevOnsets = 0, prevGrains = 0;
            foreach (float intensity in new[] { 0.1f, 0.5f, 1f })
            {
                int cracks = 0, onsets = 0, grains = 0;
                for (uint seed = 1; seed <= 4; seed++)
                {
                    var s = Make(seed * 104729u);
                    s.Impact(intensity, 2f);
                    var x = Mono(Render(s, 3f));
                    cracks += s.CracksPlayed;
                    grains += s.GrainsPlayed;
                    onsets += Onsets(x, 0, x.Length);
                    Assert.That(s.CracksPlayed, Is.InRange(3, 8));
                }
                Assert.Greater(cracks, prevCracks, $"cracks at {intensity}");
                Assert.Greater(onsets, prevOnsets, $"audible onsets at {intensity}");
                Assert.Greater(grains, prevGrains * 3 / 2, $"grains at {intensity}");
                prevCracks = cracks; prevOnsets = onsets; prevGrains = grains;
            }
        }

        [Test]
        public void GrindRuns_AtHundredsOfGrainsPerSecond()
        {
            var s = Make(21u);
            s.Layers = SfxSynth.LayerCrunch;
            s.Impact(1f, 3f);
            Render(s, 3f);
            Assert.Greater(s.GrainsPlayed / 3f, 150f);
            Assert.Less(s.GrainsPlayed / 3f, 750f);
        }

        [Test]
        public void Duration_GrowsWithIntensity_AndFollowsTheGivenMergeTime()
        {
            float prev = 0f;
            foreach (float intensity in new[] { 0.1f, 0.5f, 1f })
            {
                var x = Mono(RenderImpact(31u, intensity, 9f));
                float secs = AudibleSeconds(x);
                Assert.Greater(secs, prev + 0.3f, $"audible seconds at {intensity}");
                Assert.Greater(secs, 1.5f, "even a bump crumbles for a moment");
                prev = secs;
            }

            var shortMerge = Mono(RenderImpact(32u, 0.6f, 5f, 1f));
            var longMerge = Mono(RenderImpact(32u, 0.6f, 5f, 3f));
            float a = Rms(shortMerge, 2 * Sr, 3 * Sr), b = Rms(longMerge, 2 * Sr, 3 * Sr);
            Assert.Greater(b, 2.5f * a, $"a 3 s merge still grinds in its third second ({a} vs {b})");
        }

        [Test]
        public void TwoImpacts_NeverSoundAlike()
        {
            var s = Make(41u);
            s.Impact(0.7f, 2f);
            var a = Mono(Render(s, 8f));
            s.Impact(0.7f, 2f);
            var b = Mono(Render(s, 8f));
            Assert.Less(Math.Abs(Correlation(a, b)), 0.6f, "waveform");
            Assert.Less(Correlation(Texture(a, 0.1f, 3.5f), Texture(b, 0.1f, 3.5f)), 0.6f, "event pattern");

            var u1 = new SfxSynth(Sr);
            var u2 = new SfxSynth(Sr);
            u1.WindGain = u1.WaterGain = u2.WindGain = u2.WaterGain = 0f;
            u1.Impact(0.7f); u2.Impact(0.7f);
            var c = Mono(Render(u1, 3f));
            var d = Mono(Render(u2, 3f));
            Assert.Less(Math.Abs(Correlation(c, d)), 0.6f, "two unseeded synths");
        }

        [Test]
        public void Grains_AreScatteredAcrossTheStereoField_AndTheRumbleIsCentred()
        {
            var crunch = RenderImpact(51u, 1f, 2f, 2f, false, SfxSynth.LayerCrunch);
            Assert.Less(Correlation(Channel(crunch, 0), Channel(crunch, 1)), 0.75f, "crunch L/R correlation");
            Assert.Greater(PanSpread(crunch), 0.12f, "the grains jump between the ears");
            var rumble = RenderImpact(51u, 1f, 2f, 2f, false, SfxSynth.LayerRumble);
            Assert.Greater(Correlation(Channel(rumble, 0), Channel(rumble, 1)), 0.999f, "rumble L/R correlation");
            Assert.Less(PanSpread(rumble), 0.01f, "the rumble stays in the middle");
            var rm = Mono(rumble);
            Assert.Greater(BandShare(rm, Sr / 2, 2 * Sr, 30f, 330f), 0.8f, "rumble + body band sit below 330 Hz");
            Assert.Greater(BandShare(rm, Sr / 2, 2 * Sr, 140f, 330f), 0.2f, "the small-speaker body band is there");
            Assert.Less(BandShare(rm, Sr / 2, 2 * Sr, 0f, 30f), 0.08f, "nothing piles up below 30 Hz");
        }

        [Test]
        public void GrindAmount_GrindsWhileHeld_AndStopsAfterwards()
        {
            var s = Make(61u);
            s.GrindAmount = 0.8f;
            var x = Mono(Render(s, 2f));
            Assert.Greater(Rms(x, Sr, 2 * Sr), 0.01f);
            Assert.Greater(s.GrainsPlayed, 50);
            Assert.Greater(BandShare(x, Sr / 2, 2 * Sr, 200f, 4000f), 0.55f);
            s.GrindAmount = 0f;
            Render(s, 3f);
            Assert.AreEqual(0f, Peak(Render(s, 0.5f)));
            Assert.False(s.BusActive);
        }

        [Test]
        public void OneShots_Sound_AndEnd()
        {
            var s = Make(71u);
            s.ShipBeached(0.8f);
            var beach = Mono(Render(s, 3f));
            Assert.Greater(Rms(beach, 0, Sr / 2), 0.01f, "beaching");
            Assert.AreEqual(1, s.BeachingsPlayed);
            Assert.False(s.BusActive, "beaching ended");

            s.BigSplash(1f);
            var splash = Mono(Render(s, 4f));
            Assert.Greater(Rms(splash, 0, Sr / 2), 0.02f, "splash");
            Assert.Greater(s.PlopsPlayed, 0);
            Assert.False(s.BusActive, "splash ended");
            var quiet = Make(71u);
            quiet.BigSplash(0.2f);
            var small = Mono(Render(quiet, 4f));
            Assert.Greater(Rms(splash, 0, Sr / 2), 1.5f * Rms(small, 0, Sr / 2), "a heavier splash is louder");

            s.WhaleBlow();
            var blow = Mono(Render(s, 4f));
            Assert.Greater(Rms(blow, 0, Sr / 2), 0.01f, "blow");
            Assert.Greater(BandShare(blow, 0, Sr, 600f, 4000f), 0.6f, "the spout is an airy mid hiss");
            Assert.AreEqual(1, s.BlowsPlayed);
            Assert.False(s.BusActive, "blow ended");
            Assert.LessOrEqual(Math.Max(Peak(beach), Math.Max(Peak(splash), Peak(blow))), 0.9f);
        }

        // ------------------------------------------------------------ cost

        [Test]
        public void Render_AllocatesNothing_AfterWarmUp()
        {
            var s = Make(81u, true);
            var buf = new float[1024 * 2];
            s.Impact(1f);
            for (int c = 0; c < 20; c++) s.Render(buf, 2, Sr);

            long allocs = AllocationsDuring(() =>
            {
                for (int c = 0; c < 400; c++)
                {
                    if (c % 100 == 0) { s.Impact(1f, 2f); s.ShipBeached(1f); s.BigSplash(1f); s.WhaleBlow(); }
                    s.Render(buf, 2, Sr);
                }
            });
            if (allocs < 0)
            {
                int gc0 = GC.CollectionCount(0);
                for (int c = 0; c < 4000; c++) { if (c % 100 == 0) s.Impact(1f, 2f); s.Render(buf, 2, Sr); }
                Assert.AreEqual(gc0, GC.CollectionCount(0), "no collection while rendering");
                return;
            }
            Assert.AreEqual(0, allocs, "managed allocations while rendering");
        }

        // Best of `runs`: ms per second of audio for wind + water alone, during the densest 3 s of a continental
        // impact, during its tail and during a small bump.
        public static void MeasureCpu(int runs, out double idle, out double grind, out double tail, out double bump)
        {
            var s = Make(91u, true);
            MsPerSecond(s, 1f);
            idle = grind = tail = bump = double.MaxValue;
            for (int r = 0; r < runs; r++)
            {
                idle = Math.Min(idle, MsPerSecond(s, 1f));
                s.Impact(1f, 3f);
                grind = Math.Min(grind, MsPerSecond(s, 3f));
                tail = Math.Min(tail, MsPerSecond(s, 3f));
                MsPerSecond(s, 1f);
                s.Impact(0.2f);
                bump = Math.Min(bump, MsPerSecond(s, 1.2f));
                MsPerSecond(s, 4f);
            }
        }

        public static string CpuReport(int runs = 3)
        {
            MeasureCpu(runs, out double idle, out double grind, out double tail, out double bump);
            return $"ms per second of audio, best of {runs}: wind+water {idle:F2}, continental grind {grind:F2} (+{grind - idle:F2}), " +
                   $"its tail {tail:F2} (+{tail - idle:F2}), small bump {bump:F2} (+{bump - idle:F2})";
        }

        [Test]
        public void Cpu_AnImpactIsCheap_AndIdleCostsNothingExtra()
        {
            MeasureCpu(3, out double idle, out double grind, out double tail, out double bump);
            Console.WriteLine($"wind+water {idle:F2} ms/s, continental grind +{grind - idle:F2}, tail +{tail - idle:F2}, bump +{bump - idle:F2}");
            Assert.Less(grind - idle, 12.0, "extra ms per second of audio during the grind (target 4, generous for a shared Editor)");
            Assert.Less(tail - idle, 12.0, "extra ms per second of audio in the tail");

            var s = Make(92u, true);
            s.Impact(1f);
            Render(s, 8f);
            Assert.False(s.BusActive, "idle again: only wind and water are rendered");
        }
    }
}
