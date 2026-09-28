using System;
using Drift.Audio;
using NUnit.Framework;

namespace Drift.Tests
{
    public class AudioPhase6Tests
    {
        const int Sr = 48000;

        static float[] Render(LifeSynth s, float seconds, int channels = 2, int chunkFrames = 1024)
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

        static float Peak(float[] x, int from = 0)
        {
            float p = 0f;
            for (int i = from; i < x.Length; i++)
            {
                float a = Math.Abs(x[i]);
                if (float.IsNaN(x[i]) || float.IsInfinity(x[i])) return float.NaN;
                if (a > p) p = a;
            }
            return p;
        }

        static float Rms(float[] x, int from, int to)
        {
            double s = 0;
            for (int i = from; i < to; i++) s += x[i] * x[i];
            return (float)Math.Sqrt(s / Math.Max(1, to - from));
        }

        // Left channel of an interleaved stereo buffer.
        static float[] Left(float[] stereo)
        {
            var l = new float[stereo.Length / 2];
            for (int i = 0; i < l.Length; i++) l[i] = stereo[2 * i];
            return l;
        }

        // Share of the spectral energy inside [lowHz, highHz] over `windows` 1024-point DFTs spread across [from, to).
        static float BandShare(float[] mono, int from, int to, float lowHz, float highHz, int windows = 16)
        {
            const int N = 1024;
            double band = 0, total = 0;
            int span = Math.Max(1, (to - from - N) / windows);
            int loBin = (int)(lowHz * N / Sr), hiBin = (int)(highHz * N / Sr);
            for (int w = 0; w < windows; w++)
            {
                int start = from + w * span;
                if (start + N > to) break;
                for (int k = 1; k < N / 2; k++)
                {
                    double re = 0, im = 0;
                    double step = 2.0 * Math.PI * k / N;
                    for (int n = 0; n < N; n++)
                    {
                        double v = mono[start + n];
                        re += v * Math.Cos(step * n);
                        im -= v * Math.Sin(step * n);
                    }
                    double e = re * re + im * im;
                    total += e;
                    if (k >= loBin && k <= hiBin) band += e;
                }
            }
            return total > 0 ? (float)(band / total) : 0f;
        }

        // Rising edges of the 5 ms RMS envelope: quiet (3 windows below `floor`) then above `on`.
        static int Onsets(float[] mono, float on = 0.01f, float floor = 0.004f)
        {
            int win = Sr / 200;
            int count = 0, quiet = 0;
            for (int start = 0; start + win <= mono.Length; start += win)
            {
                float r = Rms(mono, start, start + win);
                if (r < floor) quiet++;
                else
                {
                    if (r > on && quiet >= 3) count++;
                    quiet = 0;
                }
            }
            return count;
        }

        static LifeSynth Make() => new LifeSynth(Sr);

        // ------------------------------------------------------------ stability

        [Test]
        public void Renders_WithoutNaN_OrClipping_ForEveryDensityCombination()
        {
            for (int mask = 0; mask < 16; mask++)
            {
                var s = Make();
                s.BirdDensity = (mask & 1) != 0 ? 1f : 0f;
                s.CricketDensity = (mask & 2) != 0 ? 1f : 0f;
                s.RustleAmount = (mask & 4) != 0 ? 1f : 0f;
                s.LapAmount = (mask & 8) != 0 ? 1f : 0f;
                var x = Render(s, 10f);
                float peak = Peak(x);
                Assert.False(float.IsNaN(peak), $"NaN for mask {mask}");
                Assert.LessOrEqual(peak, 0.9f, $"peak for mask {mask}");
            }
        }

        [Test]
        public void HalfDensities_StayInsideThePeakLimit()
        {
            var s = Make();
            s.BirdDensity = s.CricketDensity = s.RustleAmount = s.LapAmount = 0.5f;
            s.SeabirdAmount = s.FrogAmount = s.SheepAmount = s.OxAmount = 0.5f;
            var x = Render(s, 10f);
            float peak = Peak(x);
            Assert.False(float.IsNaN(peak));
            Assert.LessOrEqual(peak, 0.9f);
            Assert.Greater(peak, 0.01f, "half densities are audible");
        }

        [Test]
        public void AllVoicesAtOnce_FireOverALongRun_AndStayBelowThePeakLimit()
        {
            var s = Make();
            s.BirdDensity = s.SeabirdAmount = s.CricketDensity = s.RustleAmount = 1f;
            s.LapAmount = s.FrogAmount = s.SheepAmount = s.OxAmount = 1f;
            var x = Render(s, 40f);
            float peak = Peak(x);
            Assert.False(float.IsNaN(peak));
            Assert.LessOrEqual(peak, 0.9f);
            Assert.Greater(s.ChirpOnsets, 20, "songbirds");
            Assert.Greater(s.MotifsSung, 5, "motifs");
            Assert.Greater(s.SeabirdCries, 1, "seabirds");
            Assert.Greater(s.CricketChirps, 10, "crickets");
            Assert.Greater(s.Gusts, 1, "gusts");
            Assert.Greater(s.Laps, 10, "laps");
            Assert.Greater(s.FrogBloops, 5, "frogs");
            Assert.Greater(s.SheepCalls, 0, "sheep");
            Assert.Greater(s.OxCalls, 0, "ox");
        }

        [Test]
        public void ZeroDensity_IsExactlySilent()
        {
            var s = Make();
            var x = Render(s, 5f);
            Assert.AreEqual(0f, Peak(x));
            Assert.AreEqual(0, s.ChirpOnsets);
            Assert.AreEqual(0, s.CricketChirps);
            Assert.AreEqual(0, s.Laps);
        }

        [Test]
        public void Render_HandlesMonoAndOddBufferSizes()
        {
            var s = Make();
            s.BirdDensity = s.CricketDensity = s.RustleAmount = s.LapAmount = 1f;
            var mono = Render(s, 2f, 1, 1000);
            Assert.False(float.IsNaN(Peak(mono)));
            Assert.Greater(Peak(mono), 0f);
            var odd = new float[3 * 777];
            s.Render(odd, 3, Sr);
            Assert.False(float.IsNaN(Peak(odd)));
        }

        // ------------------------------------------------------------ character

        [Test]
        public void NightCrickets_PutTheirEnergyBetween3And6kHz()
        {
            var s = Make();
            s.CricketDensity = 1f;
            var x = Left(Render(s, 10f));
            int from = 3 * Sr;
            Assert.Greater(Rms(x, from, x.Length), 0.003f, "crickets are audible");
            float share = BandShare(x, from, x.Length, 3000f, 6000f);
            Assert.Greater(share, 0.8f, "3-6 kHz share");
            Assert.Greater(s.CricketChirps, 3);
        }

        [Test]
        public void DayBirds_ProduceChirpOnsets()
        {
            var s = Make();
            s.BirdDensity = 1f;
            var x = Left(Render(s, 10f));
            int onsets = Onsets(x);
            Assert.GreaterOrEqual(onsets, 5, "audible onsets");
            Assert.Greater(s.ChirpOnsets, 10, "notes sung");
            Assert.GreaterOrEqual(s.ChirpOnsets, onsets, "the counter never undercounts what is audible");
            float share = BandShare(x, 0, x.Length, 2000f, 7000f);
            Assert.Greater(share, 0.9f, "chirps sit in the 2-7 kHz band");
        }

        [Test]
        public void LowBirdDensity_SingsLessThanHighDensity()
        {
            var lo = Make(); lo.BirdDensity = 0.2f;
            var hi = Make(); hi.BirdDensity = 1f;
            Render(lo, 30f);
            Render(hi, 30f);
            Assert.Greater(hi.ChirpOnsets, lo.ChirpOnsets * 2, $"lo {lo.ChirpOnsets} hi {hi.ChirpOnsets}");
        }

        [Test]
        public void Rustle_SitsInTheLeafBand_AndLap_IsLow()
        {
            var r = Make(); r.RustleAmount = 1f;
            var xr = Left(Render(r, 6f));
            Assert.Greater(BandShare(xr, 2 * Sr, xr.Length, 600f, 3500f), 0.7f, "rustle 0.6-3.5 kHz");

            var l = Make(); l.LapAmount = 1f;
            var xl = Left(Render(l, 6f));
            Assert.Greater(BandShare(xl, 2 * Sr, xl.Length, 100f, 1600f), 0.7f, "lapping below 1.6 kHz");
            Assert.Greater(l.Laps, 1);
        }

        [Test]
        public void Duck_HalvesTheLevel()
        {
            var s = Make();
            s.RustleAmount = 1f;
            var a = Left(Render(s, 6f));
            float before = Rms(a, 4 * Sr, a.Length);
            s.Duck = 1f;
            var b = Left(Render(s, 6f));
            float after = Rms(b, 4 * Sr, b.Length);
            float ratio = after / before;
            Assert.That(ratio, Is.InRange(0.35f, 0.65f), $"ratio {ratio}");
        }

        // ------------------------------------------------------------ density mapping

        [Test]
        public void Density_IsMonotonicAndClamped()
        {
            Assert.AreEqual(0f, LifeSoundMix.Density(0f, 10f));
            Assert.AreEqual(0f, LifeSoundMix.Density(-5f, 10f));
            Assert.AreEqual(0f, LifeSoundMix.Density(5f, 0f));
            float prev = 0f;
            for (int c = 0; c <= 400; c++)
            {
                float d = LifeSoundMix.Density(c, 10f);
                Assert.GreaterOrEqual(d, prev, $"count {c}");
                Assert.LessOrEqual(d, 1f);
                prev = d;
            }
            Assert.Greater(LifeSoundMix.Density(10f, 10f), 0.8f);
            Assert.AreEqual(1f, LifeSoundMix.Density(1e9f, 10f));

            prev = 1f;
            for (int i = 0; i <= 100; i++)
            {
                float p = LifeSoundMix.Proximity(i, 8f, 40f);
                Assert.LessOrEqual(p, prev, $"distance {i}");
                Assert.That(p, Is.InRange(0f, 1f));
                prev = p;
            }
            Assert.AreEqual(1f, LifeSoundMix.Proximity(0f, 8f, 40f));
            Assert.AreEqual(0f, LifeSoundMix.Proximity(40f, 8f, 40f));
        }

        [Test]
        public void Mix_GatesVoicesByNightWindStormAndSpeed()
        {
            var c = new HabitatCounts
            {
                songbirds = 8f, seabirds = 3f, forestCells = 20f, meadowCells = 30f, trees = 30f,
                reeds = 10f, sheepHerds = 2f, oxHerds = 1f, shoreLength = 17f, awake = 1f
            };
            var s = Make();

            LifeSoundMix.Apply(c, 0f, 0.6f, 0f, 0f, s);
            Assert.Greater(s.BirdDensity, 0.5f);
            Assert.Greater(s.SeabirdAmount, 0.5f);
            Assert.AreEqual(0f, s.CricketDensity);
            Assert.AreEqual(0f, s.FrogAmount);
            Assert.Greater(s.SheepAmount, 0.3f);
            Assert.Greater(s.OxAmount, 0.3f);
            Assert.Greater(s.LapAmount, 0.5f);
            float calmRustle = s.RustleAmount;

            LifeSoundMix.Apply(c, 1f, 0.6f, 0f, 0f, s);
            Assert.AreEqual(0f, s.BirdDensity);
            Assert.AreEqual(0f, s.SeabirdAmount);
            Assert.Greater(s.CricketDensity, 0.5f);
            Assert.AreEqual(0f, s.SheepAmount);

            LifeSoundMix.Apply(c, 0.5f, 0.6f, 0f, 0f, s);
            Assert.Greater(s.FrogAmount, 0.5f, "frogs at dusk");

            LifeSoundMix.Apply(c, 0f, 1.5f, 1f, 0f, s);
            Assert.Greater(s.RustleAmount, calmRustle, "storm rustles more");
            Assert.Less(s.BirdDensity, 0.4f, "birds quiet in a storm");

            LifeSoundMix.Apply(c, 0f, 0.6f, 0f, 1f, s);
            Assert.AreEqual(0f, s.LapAmount, "no lapping at full speed");

            c.awake = 0f;
            LifeSoundMix.Apply(c, 0f, 0.6f, 0f, 0f, s);
            Assert.AreEqual(0f, s.SheepAmount, "sleeping herds do not call");

            LifeSoundMix.Apply(default, 0f, 0.6f, 0f, 0f, s);
            Assert.AreEqual(0f, s.BirdDensity + s.SeabirdAmount + s.CricketDensity + s.RustleAmount + s.LapAmount + s.FrogAmount + s.SheepAmount + s.OxAmount);
        }
    }
}
