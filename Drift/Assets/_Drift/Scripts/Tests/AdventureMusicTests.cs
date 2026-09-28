using System;
using Drift.Audio;
using NUnit.Framework;
using static Drift.Tests.ImpactAudioTests;

namespace Drift.Tests
{
    public class AdventureMusicTests
    {
        const float Base = AdventureMusicSynth.BaseBpm;

        static AdventureMusicSynth Make(float metres = 0f)
        {
            var m = new AdventureMusicSynth(Sr);
            m.Distance = metres;
            if (metres > 0f) m.JumpTo(metres);
            return m;
        }

        // Renders in 1024-frame chunks until `done` holds (checked after every chunk) or `maxSeconds` ran out.
        static bool RenderUntil(AdventureMusicSynth m, Func<bool> done, float maxSeconds, Action<AdventureMusicSynth> perChunk = null)
        {
            var buf = new float[2048];
            int chunks = (int)(maxSeconds * Sr / 1024);
            for (int c = 0; c < chunks; c++)
            {
                m.Render(buf, 2, Sr);
                perChunk?.Invoke(m);
                if (done()) return true;
            }
            return false;
        }

        static float BarSeconds(float bpm) => 4f * 60f / bpm;

        [Test]
        public void Tempo_StartsClearlyFasterThanCozy_AndRisesFourPercentPer1000m_CappedAt30()
        {
            Assert.GreaterOrEqual(Base, MusicSynth.BaseBpm * 1.8f);
            Assert.AreEqual(Base, AdventureMusicSynth.BpmAt(0f), 1e-3f);
            Assert.AreEqual(Base, AdventureMusicSynth.BpmAt(999f), 1e-3f);
            Assert.AreEqual(Base * 1.04f, AdventureMusicSynth.BpmAt(1000f), 1e-3f);
            Assert.AreEqual(Base * 1.20f, AdventureMusicSynth.BpmAt(5000f), 1e-3f);
            Assert.AreEqual(Base * 1.30f, AdventureMusicSynth.BpmAt(10000f), 1e-3f);
            Assert.AreEqual(Base * 1.30f, AdventureMusicSynth.BpmAt(1e6f), 1e-3f);
            for (int s = 1; s < 20; s++)
                Assert.GreaterOrEqual(AdventureMusicSynth.BpmForStage(s), AdventureMusicSynth.BpmForStage(s - 1));
        }

        [Test]
        public void EveryStageBringsSomethingNew()
        {
            for (int s = 1; s < 24; s++)
            {
                bool same = AdventureMusicSynth.LoopAt(s) == AdventureMusicSynth.LoopAt(s - 1)
                    && AdventureMusicSynth.KeyAt(s) == AdventureMusicSynth.KeyAt(s - 1)
                    && AdventureMusicSynth.LayersAt(s) == AdventureMusicSynth.LayersAt(s - 1);
                Assert.False(same, $"stage {s} sounds like stage {s - 1}");
            }
            // Layers only ever get added, never taken away.
            for (int s = 1; s < 24; s++)
            {
                int prev = AdventureMusicSynth.LayersAt(s - 1), cur = AdventureMusicSynth.LayersAt(s);
                Assert.AreEqual(prev & ~AdventureMusicSynth.LMarimbaLead, prev & cur & ~AdventureMusicSynth.LMarimbaLead, $"stage {s}");
            }
        }

        [Test]
        public void SynthTempo_MatchesTheDistance_At0_1000_5000_10000m()
        {
            foreach (float metres in new[] { 0f, 1000f, 5000f, 10000f })
            {
                var m = Make(metres);
                Render(m, 0.2f);
                Assert.AreEqual(AdventureMusicSynth.BpmAt(metres), m.CurrentBpm, 1e-2f, $"{metres} m");
                Assert.AreEqual(AdventureMusicSynth.StageAt(metres), m.Stage, $"{metres} m");
            }
        }

        [Test]
        public void StageChange_LandsOnABarLine_AfterAOneBarFillThatGlidesTheTempo()
        {
            var m = Make();
            Render(m, 1f);
            m.Distance = 1000f;
            bool sawFill = false;
            float lo = float.MaxValue, hi = 0f;
            bool changed = RenderUntil(m, () => m.StageChanges > 0, 5f * BarSeconds(Base) + 1f, x =>
            {
                if (!x.InFill) return;
                sawFill = true;
                lo = Math.Min(lo, x.CurrentBpm);
                hi = Math.Max(hi, x.CurrentBpm);
            });
            Assert.True(changed, "the stage never changed");
            Assert.True(sawFill, "no fill before the change");
            Assert.AreEqual(0, m.LastStageChangeStep % 16, "stage change off the bar line");
            Assert.AreEqual(1, m.Stage);
            Assert.Greater(hi, lo + 1f, "the tempo jumped instead of gliding through the fill");
            Render(m, 0.1f);
            Assert.AreEqual(AdventureMusicSynth.BpmForStage(1), m.CurrentBpm, 1e-2f);
            Assert.AreEqual(0, m.Section);
        }

        [Test]
        public void ADistanceJump_ClimbsOneStageAtATime()
        {
            var m = Make();
            m.Distance = 3000f;
            bool reached = RenderUntil(m, () => m.Stage == 3, 3 * 5 * BarSeconds(Base) + 2f);
            Assert.True(reached, $"stuck at stage {m.Stage}");
            Assert.AreEqual(3, m.StageChanges);
            Assert.AreEqual(0, m.LastStageChangeStep % 16);
        }

        [Test]
        public void StartHold_PlaysTheIntro_AndTheReleaseDropsTheBeatAfterAFill()
        {
            var m = Make();
            m.Holding = true;
            m.Restart();
            Render(m, 3f);
            Assert.True(m.Intro);
            var intro = Render(m, 4f);
            m.Holding = false;
            bool released = RenderUntil(m, () => !m.Intro, 2f * BarSeconds(Base) + 0.5f);
            Assert.True(released);
            var full = Render(m, 4f);
            // The kick and the bass only come in after the release.
            float introLow = BandShare(ImpactAudioTests.Mono(intro), 0, intro.Length / 2, 30f, 150f);
            float fullLow = BandShare(ImpactAudioTests.Mono(full), 0, full.Length / 2, 30f, 150f);
            Assert.Greater(fullLow, introLow * 3f, $"intro {introLow:0.000} full {fullLow:0.000}");
            Assert.AreEqual(0, m.Stage);
        }

        [Test]
        public void SectionsAlternate_EveryEightBars()
        {
            var m = Make();
            bool sawB = RenderUntil(m, () => m.Section == 1, 9f * BarSeconds(Base));
            Assert.True(sawB);
            Assert.AreEqual(8, m.Bars);
            bool backToA = RenderUntil(m, () => m.Section == 0, 9f * BarSeconds(Base));
            Assert.True(backToA);
            Assert.AreEqual(16, m.Bars);
        }

        [Test]
        public void Output_DoesNotClip_AndSitsAtTheCozyMusicsLevel()
        {
            var cozy = new MusicSynth(Sr) { Tension = 0.3f };
            Render(cozy, 2f);
            var c = Render(cozy, 16f);
            float cozyRms = Rms(c, 0, c.Length);
            foreach (float metres in new[] { 0f, 3000f, 6000f, 9000f })
            {
                var m = Make(metres);
                var x = Render(m, 20f);
                float peak = Peak(x);
                float rms = Rms(x, 0, x.Length);
                Assert.False(float.IsNaN(peak), $"NaN at {metres} m");
                Assert.LessOrEqual(peak, 0.9f, $"peak at {metres} m");
                Assert.Greater(rms, cozyRms * 0.7f, $"{metres} m: rms {rms:0.000} vs cozy {cozyRms:0.000}");
                Assert.Less(rms, cozyRms * 1.5f, $"{metres} m: rms {rms:0.000} vs cozy {cozyRms:0.000}");
            }
        }

        [Test]
        public void Render_AllocatesNothing()
        {
            var m = Make();
            m.Distance = 2500f;
            var buf = new float[2048];
            for (int i = 0; i < 50; i++) m.Render(buf, 2, Sr);
            long bytes = AllocationsDuring(() =>
            {
                for (int i = 0; i < 400; i++) m.Render(buf, 2, Sr);
                m.Impact(1f);
                m.Restart();
                m.Holding = true;
                for (int i = 0; i < 100; i++) m.Render(buf, 2, Sr);
                m.Holding = false;
                for (int i = 0; i < 200; i++) m.Render(buf, 2, Sr);
            });
            if (bytes < 0) Assert.Inconclusive("profiler recorder unavailable");
            Assert.AreEqual(0, bytes);
        }

        [Test]
        public void CpuCost_StaysNearTheCozyMusic()
        {
            var cozy = new MusicSynth(Sr) { Tension = 0.3f };
            var adv = Make(8000f);
            MsPerSecond(cozy, 1f);
            MsPerSecond(adv, 1f);
            double c = MsPerSecond(cozy, 4f), a = MsPerSecond(adv, 4f);
            Assert.Less(a, Math.Max(2.5 * c, 12.0), $"adventure {a:0.00} ms/s, cozy {c:0.00} ms/s");
        }

        // The surf cue used to be a 165-415 Hz tone plus a fifth; held for most of a run it sounded like an engine.
        [Test]
        public void SurfCue_IsNoiseNotATone()
        {
            var s = new SfxSynth(Sr, 4242u) { WindGain = 0f, WaterGain = 0f, WhistleGain = 0f };
            s.SurfGain = SfxSynth.SurfGainFull;
            foreach (float surf in new[] { 0.4f, 0.8f, 1f })
            {
                s.SurfAmount = surf;
                Render(s, 1f);
                var x = ImpactAudioTests.Mono(Render(s, 4f));
                var spec = Spectrum(x, 0, x.Length);
                double below500 = BandEnergy(spec, 20f, 500f), total = BandEnergy(spec, 20f, 20000f);
                Assert.Less(below500 / total, 0.1, $"surf {surf}: low share");
                Assert.Less(PeakProminenceDb(spec, 60f, 1500f), 12.0, $"surf {surf}: tonal peak");
            }
        }

        // Highest spectral line in [lo, hi] against the median of its +-40-bin neighbourhood, in dB.
        static double PeakProminenceDb(double[] spec, float lo, float hi)
        {
            int size = spec.Length * 2;
            int k0 = (int)(lo * size / Sr), k1 = (int)(hi * size / Sr);
            var nb = new double[81];
            double best = 0;
            for (int k = Math.Max(41, k0); k <= k1 && k < spec.Length - 41; k++)
            {
                for (int j = -40; j <= 40; j++) nb[j + 40] = spec[k + j];
                Array.Sort(nb);
                double med = nb[40];
                if (med <= 0) continue;
                double prom = 10.0 * Math.Log10(spec[k] / med);
                if (prom > best) best = prom;
            }
            return best;
        }
    }
}
