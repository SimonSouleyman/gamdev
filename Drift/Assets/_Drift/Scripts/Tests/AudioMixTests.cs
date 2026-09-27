using System;
using System.Collections.Generic;
using System.Text;
using Drift.Audio;
using NUnit.Framework;
using static Drift.Tests.ImpactAudioTests;

namespace Drift.Tests
{
    // Loudness of every sound layer at the gains the game really plays it with, measured against the music
    // (owner 2026-09-25: "Soundeffekte ... nicht lauter als die Musik", adventure storms and wind above all).
    // Loudness is BS.1770-style: K-weighted, integrated over the render (no gating) and the loudest 400 ms window.
    public class AudioMixTests
    {
        // AudioDirector's output gains, identical in code and in Planet.unity (2026-09-25).
        public const float MusicOut = 0.6f, SfxOut = 0.8f, LifeOut = 0.5f;

        public enum Kind { Music, Bed, OneShot, Life }

        public struct Level
        {
            public float rmsDb, peakDb, lufs, maxMomentary;
        }

        // What an effect is heard against: calm cozy music, the build-up before a merge (the tension tracker has
        // raised the music by then), or the adventure's first stage (its quietest).
        public enum Reference { CozyCalm, CozyBuild, Adventure }

        public sealed class Scenario
        {
            public string name;
            public Kind kind;
            public Reference reference;
            // Seconds scale: 1 for the report, shorter in the tests (one test class has to finish within an eval call).
            public Func<float, float[]> render;
        }

        // ------------------------------------------------------------ measurement

        public static float Db(double x) => x > 1e-12 ? (float)(20.0 * Math.Log10(x)) : -240f;

        public static Level Measure(float[] st)
        {
            int frames = st.Length / 2;
            var sq = new double[frames];
            KWeighted(st, 0, sq);
            KWeighted(st, 1, sq);
            double sum = 0;
            for (int i = 0; i < frames; i++) sum += sq[i];
            double best = 0;
            int win = Sr * 4 / 10, hop = Sr / 10;
            if (frames >= win)
            {
                double w = 0;
                for (int i = 0; i < win; i++) w += sq[i];
                best = w;
                for (int start = 0; start + win + hop <= frames; start += hop)
                {
                    for (int i = 0; i < hop; i++) w += sq[start + win + i] - sq[start + i];
                    if (w > best) best = w;
                }
                best /= win;
            }
            else best = sum / Math.Max(1, frames);
            return new Level
            {
                rmsDb = Db(Rms(st, 0, st.Length)),
                peakDb = Db(Peak(st)),
                lufs = Lufs(sum / Math.Max(1, frames)),
                maxMomentary = Lufs(best),
            };
        }

        static float Lufs(double meanSquareSum) => meanSquareSum > 1e-20 ? (float)(-0.691 + 10.0 * Math.Log10(meanSquareSum)) : -200f;

        // The two BS.1770 stages at 48 kHz (head shelf +4 dB above ~1.5 kHz, RLB high-pass at ~38 Hz); adds y^2 of one channel.
        static void KWeighted(float[] st, int ch, double[] sq)
        {
            double b0 = 1.53512485958697, b1 = -2.69169618940638, b2 = 1.19839281085285, a1 = -1.69065929318241, a2 = 0.73248077421585;
            double c0 = 1.0, c1 = -2.0, c2 = 1.0, d1 = -1.99004745483398, d2 = 0.99007225036621;
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0, u1 = 0, u2 = 0, z1 = 0, z2 = 0;
            int frames = st.Length / 2;
            for (int i = 0; i < frames; i++)
            {
                double x = st[2 * i + ch];
                double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                double z = c0 * y + c1 * u1 + c2 * u2 - d1 * z1 - d2 * z2;
                u2 = u1; u1 = y; z2 = z1; z1 = z;
                sq[i] += z * z;
            }
        }

        // Renders `seconds` in 1024-frame chunks, calling perChunk(time) before each, and applies the output gain.
        public static float[] RenderAt(ISynthSource s, float seconds, float gain, Action<float> perChunk = null)
        {
            int frames = (int)(seconds * Sr);
            var all = new float[frames * 2];
            var buf = new float[2048];
            int done = 0;
            float t = 0f;
            while (done < frames)
            {
                perChunk?.Invoke(t);
                s.Render(buf, 2, Sr);
                int n = Math.Min(1024, frames - done);
                for (int i = 0; i < n * 2; i++) all[done * 2 + i] = buf[i] * gain;
                done += n;
                t += 1024f / Sr;
            }
            return all;
        }

        // ------------------------------------------------------------ what the game plays

        // Same arithmetic as AudioDirector.Update for the wind/water/flow/surf beds (scene values).
        public static void DriveBeds(SfxSynth s, float t, float speedN, float drive, float flow, float surf, bool adventure)
        {
            AudioDirector.ApplyBeds(s, AudioDirector.BedInputs.SceneDefaults(speedN, drive, flow, surf, t, adventure));
        }

        public static float[] Beds(uint seed, float seconds, float speedN, float drive, float flow, float surf, bool adventure)
        {
            var s = new SfxSynth(Sr, seed);
            RenderAt(s, 2f, 1f, t => DriveBeds(s, t, speedN, drive, flow, surf, adventure));
            return RenderAt(s, seconds, SfxOut, t => DriveBeds(s, t + 2f, speedN, drive, flow, surf, adventure));
        }

        static SfxSynth Quiet(uint seed, bool adventure)
        {
            var s = new SfxSynth(Sr, seed);
            AudioDirector.ApplyBeds(s, AudioDirector.BedInputs.SceneDefaults(0f, 0f, 0f, 0f, 0f, adventure));
            s.WindGain = 0f; s.WhistleGain = 0f; s.WaterGain = 0f;
            return s;
        }

        public static float[] OneShot(uint seed, float seconds, bool adventure, Action<SfxSynth> fire)
        {
            var s = Quiet(seed, adventure);
            RenderAt(s, 0.1f, 1f);
            fire(s);
            return RenderAt(s, seconds, SfxOut);
        }

        public static float[] Life(float seconds, Action<LifeSynth> set)
        {
            var l = new LifeSynth(Sr);
            set(l);
            RenderAt(l, 3f, 1f);
            return RenderAt(l, seconds, LifeOut);
        }

        public static float[] CozyMusic(float tension, float seconds)
        {
            var m = new MusicSynth(Sr) { Tension = tension };
            RenderAt(m, 4f, 1f);
            return RenderAt(m, seconds, MusicOut);
        }

        public static float[] AdventureMusic(float metres, float seconds)
        {
            var m = new AdventureMusicSynth(Sr) { Distance = metres };
            if (metres > 0f) m.JumpTo(metres);
            RenderAt(m, 1f, 1f);
            return RenderAt(m, seconds, MusicOut);
        }

        public static List<Scenario> Scenarios()
        {
            var list = new List<Scenario>();
            void Add(string name, Kind kind, Reference r, Func<float, float[]> render) => list.Add(new Scenario { name = name, kind = kind, reference = r, render = render });
            const Reference Calm = Reference.CozyCalm, Build = Reference.CozyBuild, Adv = Reference.Adventure;

            Add("music cozy calm (tension 0)", Kind.Music, Calm, k => CozyMusic(0f, 20f * k));
            Add("music cozy (tension 0.3)", Kind.Music, Calm, k => CozyMusic(0.3f, 20f * k));
            Add("music cozy build (tension 0.85)", Kind.Music, Build, k => CozyMusic(0.85f, 12f * k));
            Add("music adventure 0 m", Kind.Music, Adv, k => AdventureMusic(0f, 16f * k));
            Add("music adventure 3000 m", Kind.Music, Adv, k => AdventureMusic(3000f, 16f * k));
            Add("music adventure 8000 m", Kind.Music, Adv, k => AdventureMusic(8000f, 16f * k));

            Add("cozy idle: wind + water", Kind.Bed, Calm, k => Beds(11u, 23f * k, 0f, 0f, 0f, 0f, false));
            Add("cozy cruise 60 %: wind + water", Kind.Bed, Calm, k => Beds(12u, 23f * k, 0.6f, 0.4f, 0f, 0f, false));
            Add("cozy full speed + current", Kind.Bed, Calm, k => Beds(13u, 23f * k, 1f, 1.2f, 1f, 0f, false));
            Add("cozy surfing a boundary", Kind.Bed, Calm, k => Beds(14u, 23f * k, 1f, 1.2f, 0f, 0.8f, false));
            Add("adventure cruise (no surf)", Kind.Bed, Adv, k => Beds(15u, 23f * k, 0.95f, 1.2f, 0f, 0f, true));
            Add("adventure cruise + surf 0.8", Kind.Bed, Adv, k => Beds(16u, 23f * k, 0.95f, 1.2f, 0f, 0.8f, true));
            Add("adventure full surf + current", Kind.Bed, Adv, k => Beds(17u, 23f * k, 1f, 1.5f, 1f, 1f, true));

            Add("sinking warning", Kind.OneShot, Calm, k => OneShot(18u, 6f, false, s => s.SinkWarning = 1f));
            Add("thunder close (adventure)", Kind.OneShot, Adv, k => OneShot(21u, 7f, true, s => s.Thunder(1f, true, 0f)));
            Add("thunder far (adventure)", Kind.OneShot, Adv, k => OneShot(22u, 7f, true, s => s.Thunder(0.7f, false, 0f)));
            Add("thunder close (cozy)", Kind.OneShot, Calm, k => OneShot(23u, 7f, false, s => s.Thunder(1f, true, 0f)));
            Add("bump (adventure hit, I 0.9)", Kind.OneShot, Adv, k => OneShot(24u, 4f, true, s => s.Impact(0.9f, 0.35f)));
            Add("merge small (I 0.3) + splash", Kind.OneShot, Build, k => OneShot(25u, 5f, false, s => { s.Impact(0.3f, 1.6f); s.BigSplash(0.7f * 0.3f); }));
            Add("merge big (I 0.7) + splash", Kind.OneShot, Build, k => OneShot(26u, 7f, false, s => { s.Impact(0.7f, 2.6f); s.BigSplash(0.7f * 0.7f); }));
            Add("merge huge (I 1) + splash", Kind.OneShot, Build, k => OneShot(27u, 8f, false, s => { s.Impact(1f, 3.5f); s.BigSplash(0.7f); }));
            Add("grind held (drive-in)", Kind.OneShot, Build, k => OneShot(28u, 4f, false, s => s.GrindAmount = 0.9f));
            Add("whale breach splash", Kind.OneShot, Calm, k => OneShot(29u, 4f, false, s => s.BigSplash(1f)));
            Add("whale blow (spout)", Kind.OneShot, Calm, k => OneShot(30u, 3f, false, s => s.WhaleBlow()));
            Add("ship beached", Kind.OneShot, Calm, k => OneShot(31u, 3f, false, s => s.ShipBeached(1f)));

            Add("life: day, lots of birds + sheep", Kind.Life, Calm, k => Life(20f * k, l => { l.BirdDensity = 1f; l.SeabirdAmount = 0.6f; l.RustleAmount = 0.4f; l.LapAmount = 0.8f; l.SheepAmount = 1f; }));
            Add("life: night crickets + frogs", Kind.Life, Calm, k => Life(20f * k, l => { l.CricketDensity = 1f; l.FrogAmount = 1f; l.RustleAmount = 0.3f; l.LapAmount = 0.8f; }));
            Add("life: storm (rustle 1)", Kind.Life, Calm, k => Life(20f * k, l => { l.RustleAmount = 1f; l.BirdDensity = 0.3f; }));
            return list;
        }

        public static Level MusicReference(Reference r, float scale = 1f)
        {
            switch (r)
            {
                case Reference.CozyBuild: return Measure(CozyMusic(0.85f, 12f * scale));
                case Reference.Adventure: return Measure(AdventureMusic(0f, 16f * scale));
                default: return Measure(CozyMusic(0f, 20f * scale));
            }
        }

        // Table of every scenario (from eval: Drift.Tests.AudioMixTests.Report()).
        public static string Report(float scale = 1f)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var refs = new Level[3];
            for (int r = 0; r < 3; r++) refs[r] = MusicReference((Reference)r, scale);
            var sb = new StringBuilder();
            sb.AppendLine("name | kind | against | LUFS | maxM | RMS dB | peak dB | LUFS vs music | maxM vs music maxM | peak vs music peak");
            foreach (var sc in Scenarios())
            {
                var l = Measure(sc.render(scale));
                var r = refs[(int)sc.reference];
                sb.AppendLine(string.Format(ci, "{0} | {1} | {2} | {3:0.0} | {4:0.0} | {5:0.0} | {6:0.0} | {7:+0.0;-0.0} | {8:+0.0;-0.0} | {9:+0.0;-0.0}",
                    sc.name, sc.kind, sc.reference, l.lufs, l.maxMomentary, l.rmsDb, l.peakDb, l.lufs - r.lufs, l.maxMomentary - r.maxMomentary, l.peakDb - r.peakDb));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------ the rules

        const float TestScale = 0.5f;
        static Level[] s_refs;

        static Level RefOf(Reference r)
        {
            if (s_refs == null)
            {
                s_refs = new Level[3];
                for (int k = 0; k < 3; k++) s_refs[k] = MusicReference((Reference)k);
            }
            return s_refs[(int)r];
        }

        [Test]
        public void Beds_SitAtLeast6LuUnderTheMusic_AndStayAudible()
        {
            foreach (var sc in Scenarios())
            {
                if (sc.kind != Kind.Bed) continue;
                var l = Measure(sc.render(TestScale));
                var r = RefOf(sc.reference);
                Assert.LessOrEqual(l.lufs, r.lufs - 6f, sc.name);
                Assert.LessOrEqual(l.peakDb, r.peakDb, sc.name + " peak");
                if (sc.name.Contains("cruise")) Assert.Greater(l.lufs, r.lufs - 16f, sc.name + " is still heard");
            }
        }

        [Test]
        public void OneShots_NeverOutshoutTheMusic()
        {
            foreach (var sc in Scenarios())
            {
                if (sc.kind != Kind.OneShot) continue;
                var l = Measure(sc.render(TestScale));
                var r = RefOf(sc.reference);
                Assert.LessOrEqual(l.maxMomentary, r.maxMomentary, sc.name + " loudest 400 ms");
                Assert.LessOrEqual(l.peakDb, r.peakDb, sc.name + " peak");
                Assert.Greater(l.maxMomentary, r.maxMomentary - 14f, sc.name + " is still heard");
            }
        }

        [Test]
        public void Life_StaysUnderTheMusic()
        {
            foreach (var sc in Scenarios())
            {
                if (sc.kind != Kind.Life) continue;
                var l = Measure(sc.render(TestScale));
                Assert.LessOrEqual(l.lufs, RefOf(sc.reference).lufs - 6f, sc.name);
            }
        }

        // Owner: "vor allem beim Abenteuermodus" - at the same speed and surf the adventure's beds and hits are quieter.
        [Test]
        public void TheAdventure_IsQuieterThanCozyAtTheSameSpeed()
        {
            var cozy = Measure(Beds(40u, 8f, 0.95f, 1.2f, 0f, 0.8f, false));
            var adv = Measure(Beds(40u, 8f, 0.95f, 1.2f, 0f, 0.8f, true));
            Assert.LessOrEqual(adv.lufs, cozy.lufs - 2.5f);
            var hitCozy = Measure(OneShot(41u, 3f, false, s => s.Impact(0.9f, 0.35f)));
            var hitAdv = Measure(OneShot(41u, 3f, true, s => s.Impact(0.9f, 0.35f)));
            Assert.Less(hitAdv.maxMomentary, hitCozy.maxMomentary - 1f);
        }

        // A listenable 30 s mix of one mode: music + beds + life, with the typical events on top.
        public static float[] Mix(bool adventure, uint seed = 5u)
        {
            float seconds = 30f;
            var music = adventure ? AdventureMusic(0f, seconds) : CozyMusic(0.2f, seconds);
            var s = new SfxSynth(Sr, seed);
            float speed = adventure ? 0.95f : 0.6f, drive = adventure ? 1.2f : 0.4f;
            RenderAt(s, 2f, 1f, t => DriveBeds(s, t, speed, drive, 0f, 0f, adventure));
            float last = -1f;
            var sfx = RenderAt(s, seconds, SfxOut, t =>
            {
                bool At(float x) => last < x && t >= x;
                float surf = adventure && (t % 10f) < 7f ? 0.8f : 0f;
                DriveBeds(s, t + 2f, speed, drive, 0f, surf, adventure);
                if (At(6f)) { if (adventure) s.Impact(0.9f, 0.35f); else { s.Impact(0.6f, 2.2f); s.BigSplash(0.42f); } }
                if (At(14f)) s.Thunder(1f, true, 0f);
                if (At(20f)) s.WhaleBlow();
                if (At(24f) && adventure) s.Impact(0.8f, 0.35f);
                last = t;
            });
            var life = Life(seconds, l => { l.BirdDensity = adventure ? 0f : 0.7f; l.RustleAmount = adventure ? 0.6f : 0.3f; l.LapAmount = adventure ? 0f : 0.5f; });
            var mix = new float[music.Length];
            for (int i = 0; i < mix.Length; i++) mix[i] = music[i] + sfx[i] + life[i];
            return mix;
        }
    }
}
