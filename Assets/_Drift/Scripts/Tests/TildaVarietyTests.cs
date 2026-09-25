using System;
using System.Diagnostics;
using Drift.Audio;
using NUnit.Framework;

namespace Drift.Tests
{
    // Tilda's grumble has more than one way to sound (owner 2026-09-25: "mehr Variation ... mehr Laute"): the kind of
    // sentence, interjections, consonant onsets and a per-line voice all come from the text.
    public class TildaVarietyTests
    {
        static GrumblePlan Plan(string text, GrumbleMood mood = GrumbleMood.Warm, float cps = 40f)
        {
            var p = new GrumblePlan();
            p.Build(text, mood, cps);
            return p;
        }

        static GrumbleSyllable FirstGrunt(GrumblePlan p)
        {
            for (int i = 0; i < p.Count; i++) if (!p.Items[i].extra) return p.Items[i];
            Assert.Fail("no grunt");
            return default;
        }

        static GrumbleSyllable LastGrunt(GrumblePlan p)
        {
            for (int i = p.Count - 1; i >= 0; i--) if (!p.Items[i].extra) return p.Items[i];
            Assert.Fail("no grunt");
            return default;
        }

        static float MeanGruntPitch(GrumblePlan p)
        {
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < p.Count; i++) if (!p.Items[i].extra) { sum += p.Items[i].pitch; n++; }
            return sum / Math.Max(1, n);
        }

        static float St(float a, float b) => 12f * (float)Math.Log(a / b, 2.0);

        [Test]
        public void TheSameLineSoundsTheSame_AndOtherLinesDiffer()
        {
            var a = Plan("Das Meer ist heute schön.");
            var b = Plan("Das Meer ist heute schön.");
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a.Items[i].pitch, b.Items[i].pitch);
                Assert.AreEqual(a.Items[i].start, b.Items[i].start);
            }
            var c = Plan("Das Meer ist heute blau.");
            var d = Plan("Der Wind ist heute mild.");
            Assert.AreNotEqual(a.Seed, c.Seed);
            float diff = 0f;
            int n = Math.Min(a.Count, Math.Min(c.Count, d.Count));
            for (int i = 0; i < n; i++) diff += Math.Abs(a.Items[i].pitch - c.Items[i].pitch) + Math.Abs(a.Items[i].pitch - d.Items[i].pitch);
            Assert.Greater(diff / n, 4f, "different lines, different melodies");
        }

        [Test]
        public void QuestionsRise_WQuestionsFall_ExclamationsSitHigher()
        {
            var q = Plan("Kommst du heute mit?");
            Assert.Greater(St(LastGrunt(q).pitch, FirstGrunt(q).pitch), 2f, "a yes/no question rises");
            Assert.Greater(LastGrunt(q).glide, 0f);

            var w = Plan("Wo ist denn die Insel?");
            Assert.Less(St(LastGrunt(w).pitch, FirstGrunt(w).pitch), 0f, "a W-question falls");

            float statement = MeanGruntPitch(Plan("Das ist eine schöne Insel."));
            float exclaim = MeanGruntPitch(Plan("Das ist eine schöne Insel!"));
            Assert.Greater(St(exclaim, statement), 1.2f, "an exclamation is higher");
        }

        [Test]
        public void InterjectionsHaveTheirOwnSound()
        {
            var oh = Plan("Oh! Da ist was.");
            var o = oh.Items[0];
            Assert.Less(o.glide, -0.2f * o.pitch, "a surprised oh falls");
            Assert.GreaterOrEqual(o.length, 0.2f);
            Assert.Greater(o.pitch, FirstGrunt(Plan("Na, da ist was.")).pitch);

            var hmm = Plan("Hmm.");
            Assert.Greater(hmm.Count, 0);
            Assert.AreEqual(hmm.Count, hmm.Extras, "no vowel, only a hum");
            Assert.IsTrue(hmm.Items[0].hum);
            Assert.GreaterOrEqual(hmm.Items[0].length, 0.3f, "a thoughtful hmm is long");
            Assert.Greater(hmm.Items[0].arch, 0f);

            var mhm = Plan("Mh-hm!");
            Assert.AreEqual(2, mhm.Count);
            Assert.Greater(mhm.Items[1].pitch, mhm.Items[0].pitch * 1.15f, "mh-hm answers upwards");

            var hihi = Plan("Hihi! Das war lustig.");
            int pulses = 0;
            for (int i = 0; i < hihi.Count; i++)
                if (!hihi.Items[i].extra && hihi.Items[i].breath >= 0.4f && hihi.Items[i].length < 0.08f) pulses++;
            Assert.GreaterOrEqual(pulses, 2, "a giggle is quick airy pulses");

            var juhu = Plan("Juhu, geschafft!");
            bool trill = false;
            for (int i = 0; i < juhu.Count; i++) trill |= juhu.Items[i].trill > 0.5f;
            Assert.IsTrue(trill, "joy trills");

            var ach = Plan("Ach je, schade.");
            Assert.GreaterOrEqual(ach.Items[0].breath, 0.3f, "a sigh is breathy");
            Assert.Less(ach.Items[0].glide, 0f);
        }

        [Test]
        public void ATrailingSentenceEndsInAThoughtfulHmm()
        {
            var p = Plan("Ich weiß nicht so recht...");
            var last = p.Items[p.Count - 1];
            Assert.IsTrue(last.extra && last.hum);
            Assert.Greater(last.arch, 0f);
            Assert.Greater(last.length, 0.25f);
        }

        [Test]
        public void ConsonantsShapeTheOnset()
        {
            float Hz(string t) => Plan(t).Items[0].tickHz;
            Assert.Less(Hz("Papa."), 1500f, "p bursts low");
            Assert.Greater(Hz("Tata."), 3000f, "t bursts high");
            Assert.IsTrue(Hz("Kaka.") > 1500f && Hz("Kaka.") < 2600f, "k in between");
            Assert.Greater(Hz("Sasa."), 4000f, "s hisses");
            Assert.IsTrue(Hz("Schade.") > 2200f && Hz("Schade.") < 3200f, "sch hushes");
            Assert.Greater(Plan("Mama.").Items[0].nasal, 0.03f, "m murmurs first");
            Assert.Greater(Plan("Nana.").Items[0].nasal, 0.03f);
            Assert.AreEqual(1150f, Plan("Lala.").Items[0].f2On, 1f);
            Assert.Greater(Plan("Jaja.").Items[0].f2On, 2000f, "j glides from i");
            Assert.Greater(Plan("Hallo.").Items[0].breath, 0.1f, "h is breathy");
            var ei = Plan("Mein Bein.").Items[0];
            Assert.Greater(ei.f2End, ei.f2 + 200f, "ei glides towards i");
            var au = Plan("Baum.").Items[0];
            Assert.Less(au.f2End, au.f2 - 150f, "au glides towards u");
        }

        static readonly string[] Lines =
        {
            "Oh! Ein Leuchtturm! Jetzt findest du auch die Inseln hinterm Horizont.",
            "Hmm... Such dir ruhig eine Welt-Nummer aus. Jede ist ein anderes Meer.",
            "Hihi! Neuer Rekord! Da beschlägt mir glatt die Brille.",
            "Juhu, alles vereint! Bleib ruhig noch ein bisschen.",
            "Ach je, versunken. Das macht nichts – wir fangen einfach neu an.",
            "Wo ist denn die nächste Insel? Kommst du mit?",
            "Mh-hm! Da bin ich wieder.",
        };

        [Test]
        public void EveryNewSoundRendersCleanly_WithoutAllocating()
        {
            foreach (GrumbleMood mood in Enum.GetValues(typeof(GrumbleMood)))
                foreach (var line in Lines)
                {
                    var synth = new TildaGrumbleSynth(48000);
                    var plan = synth.Speak(line, mood, 40f);
                    Assert.IsNotNull(plan, line);
                    for (int i = 1; i < plan.Count; i++)
                        Assert.GreaterOrEqual(plan.Items[i].start, plan.Items[i - 1].start + plan.Items[i - 1].length - 1e-3f, mood + " " + line + ": overlap at " + i);
                    var audio = ImpactAudioTests.Render(synth, plan.Duration + 0.5f);
                    float peak = ImpactAudioTests.Peak(audio);
                    Assert.False(float.IsNaN(peak), line);
                    Assert.Less(peak, 0.9f, mood + " " + line);
                    Assert.Greater(peak, 0.1f, mood + " " + line);
                    Assert.IsFalse(synth.Sounding, line);
                    float jump = 0f;
                    for (int i = 2; i < audio.Length; i += 2) jump = Math.Max(jump, Math.Abs(audio[i] - audio[i - 2]));
                    Assert.Less(jump, 0.2f, mood + " " + line + ": click");
                }

            var s = new TildaGrumbleSynth(48000);
            var buf = new float[2048];
            s.Speak(Lines[0], GrumbleMood.Warm, 40f);
            for (int i = 0; i < 20; i++) s.Render(buf, 2, 48000);
            long bytes = ImpactAudioTests.AllocationsDuring(() => { for (int i = 0; i < 100; i++) s.Render(buf, 2, 48000); });
            if (bytes >= 0) Assert.AreEqual(0, bytes);
        }

        [Test]
        public void StaysCheapOnTheAudioThread()
        {
            var s = new TildaGrumbleSynth(48000);
            var buf = new float[2048];
            var sw = Stopwatch.StartNew();
            int blocks = 0;
            foreach (var line in Lines)
            {
                var plan = s.Speak(line, GrumbleMood.Cheerful, 40f);
                int n = (int)((plan.Duration + 0.1f) * 48000 / 1024);
                for (int i = 0; i < n; i++) s.Render(buf, 2, 48000);
                blocks += n;
            }
            sw.Stop();
            double msPerSecond = sw.Elapsed.TotalMilliseconds / (blocks * 1024.0 / 48000.0);
            Assert.Less(msPerSecond, 15.0, $"{msPerSecond:0.0} ms per second of audio");
        }
    }
}
