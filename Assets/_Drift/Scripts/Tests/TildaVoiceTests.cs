using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Drift.Audio;
using Drift.Bridge;
using Drift.SaveSystem;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class TildaVoiceTests
    {
        // ---------------------------------------------------------------- remarks

        [Test]
        public void KeysAreUniqueAndTextsAreShortEnoughForABubble()
        {
            var seen = new HashSet<string>();
            foreach (var line in TildaVoiceLines.All())
            {
                Assert.IsTrue(Regex.IsMatch(line.Key, "^[a-z0-9_]+$"), line.Key);
                Assert.IsTrue(seen.Add(line.Key), "duplicate key " + line.Key);
                Assert.IsFalse(string.IsNullOrWhiteSpace(line.Value), line.Key);
                Assert.AreEqual(line.Value, TildaVoiceLines.TextOf(line.Key));
                Assert.LessOrEqual(TildaBubble.Pages(line.Value, TildaBubble.RemarkMaxChars).Count, 2, line.Key + " is a remark, not a speech");
                float seconds = TildaVoice.LengthOf(line.Key);
                Assert.IsTrue(seconds > 2f && seconds < 14f, line.Key + ": " + seconds + " s");
            }
            Assert.IsNull(TildaVoiceLines.TextOf("no_such_line"));
            Assert.AreEqual(0f, TildaVoice.LengthOf("no_such_line"));
        }

        [Test]
        public void EveryKeyTheGameAsksForIsALine()
        {
            var wanted = new List<string> { TildaVoiceLines.Greeting, TildaVoiceLines.Pause, TildaVoiceLines.GameOver, TildaVoiceLines.NewIsland, TildaVoiceLines.VoiceOn };
            for (int i = 0; i < TildaVoiceLines.IdleCount + 2; i++) wanted.Add(TildaVoiceLines.Idle(i));
            foreach (var key in wanted) Assert.IsNotNull(TildaVoiceLines.TextOf(key), key);
            Assert.AreEqual(GrumbleMood.Soft, TildaVoiceLines.MoodOf(TildaVoiceLines.GameOver));
            Assert.AreEqual(GrumbleMood.Cheerful, TildaVoiceLines.MoodOf(TildaVoiceLines.Greeting));
            Assert.AreEqual(TutorialModel.StepCount, (int)TutorialStep.Done);
        }

        [Test]
        public void NothingLoadsTheOldVoiceClips()
        {
            Assert.IsTrue(TildaVoice.ToggleLabel.StartsWith("Tildas Grummeln"));
            foreach (var field in typeof(TildaVoiceLines).GetFields()) Assert.AreNotEqual("ResourceFolder", field.Name);
        }

        // ---------------------------------------------------------------- shared clock

        const string Sample = "Prima, du fährst! Mit dem Mausrad gehst du näher heran. Oder siehst du lieber mehr vom Meer?";

        [Test]
        public void TypewriterAndGrumbleShareOneClock()
        {
            const float cps = 45f;
            float total = GrumbleTiming.Duration(Sample, cps);
            Assert.Greater(total, Sample.Length / cps + 2f * GrumbleTiming.SentencePause - 0.01f, "sentences and the comma take a breath");
            Assert.AreEqual(0, GrumbleTiming.CharsAt(Sample, 0f, cps));
            Assert.AreEqual(Sample.Length, GrumbleTiming.CharsAt(Sample, total + 0.001f, cps));
            int last = 0;
            for (float t = 0f; t < total; t += 0.02f)
            {
                int n = GrumbleTiming.CharsAt(Sample, t, cps);
                Assert.GreaterOrEqual(n, last);
                last = n;
            }
            for (int i = 1; i <= Sample.Length; i += 7)
                Assert.AreEqual(i, GrumbleTiming.CharsAt(Sample, GrumbleTiming.TimeOfChar(Sample, i, cps) + 1e-4f, cps), "char " + i);
            Assert.AreEqual(0f, GrumbleTiming.Duration("", cps));
            Assert.AreEqual(0f, GrumbleTiming.Duration(null, cps));
        }

        // ---------------------------------------------------------------- grumble plan

        [Test]
        public void SyllableCountFollowsTheText()
        {
            var plan = new GrumblePlan();
            plan.Build("Ja.", GrumbleMood.Warm, 45f);
            int tiny = plan.Count - plan.Hums;
            Assert.AreEqual(1, tiny);
            Assert.GreaterOrEqual(plan.Hums, 1, "a sentence ends in a little mh-hm");

            plan.Build(Sample, GrumbleMood.Warm, 45f);
            int grunts = plan.Count - plan.Hums, syllables = GrumblePlan.SyllablesIn(Sample);
            Assert.AreEqual(23, syllables);
            Assert.LessOrEqual(grunts, syllables);
            Assert.GreaterOrEqual(grunts, syllables / 2, "about every syllable, thinned to what she can mumble");

            plan.Build(Sample + " " + Sample, GrumbleMood.Warm, 45f);
            Assert.Greater(plan.Count - plan.Hums, grunts * 3 / 2, "twice the text, about twice the grunts");

            plan.Build(Sample, GrumbleMood.Warm, 12f);
            Assert.AreEqual(syllables, plan.Count - plan.Hums, "typed slowly, every syllable gets its grunt");

            plan.Build("", GrumbleMood.Warm, 45f);
            Assert.AreEqual(0, plan.Count);
            plan.Build("...", GrumbleMood.Warm, 45f);
            Assert.AreEqual(0, plan.Count);
        }

        [Test]
        public void GruntsFollowTheTypewriterAndStayInAnOldLadysRange()
        {
            var plan = new GrumblePlan();
            foreach (GrumbleMood mood in Enum.GetValues(typeof(GrumbleMood)))
            {
                plan.Build(Sample, mood, 45f);
                Assert.Greater(plan.Count, 3, mood.ToString());
                float typed = GrumbleTiming.Duration(Sample, 45f);
                for (int i = 0; i < plan.Count; i++)
                {
                    var s = plan.Items[i];
                    Assert.IsTrue(s.pitch >= GrumblePlan.MinPitch && s.pitch <= GrumblePlan.MaxPitch, mood + " pitch " + s.pitch);
                    Assert.IsTrue(s.length >= 0.04f && s.length < 0.75f, mood + " length " + s.length);
                    if (i > 0) Assert.GreaterOrEqual(s.start, plan.Items[i - 1].start + plan.Items[i - 1].length - 1e-3f, "one voice: grunts do not overlap");
                    if (!s.hum) Assert.LessOrEqual(s.start, typed + 0.01f);
                    Assert.IsTrue(s.f1 > 200f && s.f1 < 900f && s.f2 > 700f && s.f2 < 2400f);
                }
                Assert.LessOrEqual(plan.Duration, typed + 1.2f, mood + ": the grumble ends with the text");
            }
        }

        static float MeanPitch(GrumbleMood mood)
        {
            var plan = new GrumblePlan();
            plan.Build(Sample, mood, 45f);
            float sum = 0f;
            for (int i = 0; i < plan.Count; i++) sum += plan.Items[i].pitch;
            return sum / plan.Count;
        }

        [Test]
        public void EveryMoodHasItsOwnFlavour()
        {
            float warm = MeanPitch(GrumbleMood.Warm), cheerful = MeanPitch(GrumbleMood.Cheerful), giggly = MeanPitch(GrumbleMood.Giggly);
            float soft = MeanPitch(GrumbleMood.Soft), sleepy = MeanPitch(GrumbleMood.Sleepy);
            Assert.Greater(cheerful, warm + 5f);
            Assert.Greater(giggly, warm + 10f);
            Assert.Less(soft, warm - 8f);
            Assert.Less(sleepy, soft);

            var a = new GrumblePlan();
            var b = new GrumblePlan();
            a.Build(Sample, GrumbleMood.Warm, 45f);
            b.Build(Sample, GrumbleMood.Sleepy, 45f);
            Assert.Less(b.Count, a.Count / 2 + 2, "asleep she only mumbles now and then");
            a.Build("Hallo du! Hallo du!", GrumbleMood.Cheerful, 45f);
            Assert.Greater(a.Items[a.Count - 1].pitch, a.Items[0].pitch * 0.98f, "cheerful ends up");
        }

        // ---------------------------------------------------------------- grumble synth

        static float[] Render(TildaGrumbleSynth synth, float seconds, int rate = 48000, int channels = 2, int buffer = 1024, List<float> levels = null)
        {
            var all = new List<float>();
            var block = new float[buffer * channels];
            int blocks = Mathf.CeilToInt(seconds * rate / buffer);
            for (int k = 0; k < blocks; k++)
            {
                synth.Render(block, channels, rate);
                for (int i = 0; i < block.Length; i += channels) all.Add(block[i]);
                levels?.Add(synth.Level);
            }
            return all.ToArray();
        }

        [Test]
        public void TheGrumbleIsCleanAndNeverLoud()
        {
            foreach (GrumbleMood mood in Enum.GetValues(typeof(GrumbleMood)))
            {
                var synth = new TildaGrumbleSynth(48000);
                var plan = synth.Speak(Sample, mood, 45f);
                Assert.IsNotNull(plan);
                var audio = Render(synth, plan.Duration + 0.5f);
                float peak = 0f;
                double sum = 0.0;
                foreach (float v in audio)
                {
                    Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), mood + ": NaN");
                    peak = Mathf.Max(peak, Mathf.Abs(v));
                    sum += v * (double)v;
                }
                Assert.Less(peak, 0.9f, mood + " peak");
                Assert.Greater(peak, 0.12f, mood + " is audible");
                Assert.Greater(Math.Sqrt(sum / audio.Length), 0.01, mood + " rms");
                Assert.IsFalse(synth.Sounding, mood + ": she falls silent after the text");
                float tail = 0f;
                for (int i = audio.Length - 2000; i < audio.Length; i++) tail = Mathf.Max(tail, Mathf.Abs(audio[i]));
                Assert.Less(tail, 0.01f, mood + " tail");
            }
        }

        [Test]
        public void SwitchedOffSheIsSilent()
        {
            var synth = new TildaGrumbleSynth(48000) { Enabled = false };
            Assert.IsNull(synth.Speak(Sample, GrumbleMood.Warm, 45f));
            var levels = new List<float>();
            foreach (float v in Render(synth, 1f, 48000, 2, 1024, levels)) Assert.AreEqual(0f, v);
            foreach (float l in levels) Assert.AreEqual(0f, l);

            synth.Enabled = true;
            synth.Speak(Sample, GrumbleMood.Warm, 45f);
            Render(synth, 0.3f);
            synth.Enabled = false;
            foreach (float v in Render(synth, 0.2f)) Assert.AreEqual(0f, v);
            Assert.AreEqual(0f, synth.Level);
            Assert.IsFalse(synth.Sounding);
        }

        [Test]
        public void TheEnvelopeDrivesTheMouth()
        {
            var synth = new TildaGrumbleSynth(48000);
            var plan = synth.Speak("Hallo! Na du.", GrumbleMood.Warm, 12f);
            var levels = new List<float>();
            Render(synth, plan.Duration + 0.6f, 48000, 2, 512, levels);
            float dt = 512f / 48000f;
            float inGrunt = 0f, inPause = 1f;
            int opened = 0;
            bool open = false;
            for (int k = 0; k < levels.Count; k++)
            {
                float t = (k + 1) * dt;
                Assert.IsTrue(levels[k] >= 0f && levels[k] <= 1f);
                for (int i = 0; i < plan.Count; i++)
                {
                    var s = plan.Items[i];
                    if (t > s.start + 0.3f * s.length && t < s.start + 0.7f * s.length) inGrunt = Mathf.Max(inGrunt, levels[k]);
                }
                if (t > plan.Duration + 0.35f) inPause = Mathf.Min(inPause, levels[k]);
                bool now = levels[k] > 0.3f;
                if (now && !open) opened++;
                open = now;
            }
            Assert.Greater(inGrunt, 0.6f, "a grunt opens her mouth");
            Assert.AreEqual(0f, inPause, "silence shuts it");
            Assert.IsTrue(opened >= 2 && opened <= plan.Count, "the mouth opens per grunt: " + opened + " of " + plan.Count);

            var animatorLevel = Mathf.Clamp01(inGrunt);
            Assert.Greater(0.04f + 0.96f * animatorLevel, 0.6f);
        }

        [Test]
        public void ANewTextTakesOverWithoutAClick()
        {
            var synth = new TildaGrumbleSynth(48000);
            synth.Speak(Sample, GrumbleMood.Warm, 45f);
            var first = Render(synth, 0.4f);
            synth.Speak("Juhu!", GrumbleMood.Giggly, 45f);
            var second = Render(synth, 1.5f);
            Assert.AreEqual(2, synth.LinesStarted);
            float jump = 0f;
            for (int i = 1; i < second.Length; i++) jump = Mathf.Max(jump, Mathf.Abs(second[i] - second[i - 1]));
            Assert.Less(jump, 0.25f, "no step in the waveform");
            Assert.Less(Mathf.Abs(second[0] - first[first.Length - 1]), 0.25f);
            synth.Stop();
            Render(synth, 0.2f);
            Assert.IsFalse(synth.Sounding);
        }

        [Test]
        public void ItRendersAtOtherRatesAndInMono()
        {
            var synth = new TildaGrumbleSynth(44100);
            var plan = synth.Speak("Guten Morgen, mein Schatz!", GrumbleMood.Cheerful, 40f);
            var audio = Render(synth, plan.Duration + 0.3f, 22050, 1, 256);
            float peak = 0f;
            foreach (float v in audio) { Assert.IsFalse(float.IsNaN(v)); peak = Mathf.Max(peak, Mathf.Abs(v)); }
            Assert.IsTrue(peak > 0.1f && peak < 0.9f, "peak " + peak);
            Assert.AreEqual(22050, synth.SampleRate);
        }

        // ---------------------------------------------------------------- bubble text

        [Test]
        public void KeyWordsSurviveTheTypewriter()
        {
            string rich = "Drück " + TildaBubble.Key("Start") + " jetzt.";
            Assert.AreEqual("Drück Start jetzt.", TildaBubble.Plain(rich));
            Assert.AreEqual(rich, TildaBubble.Typed(rich, 99));
            Assert.AreEqual("Dr<color=#00000000>ück <b>Start</b> jetzt.</color>", TildaBubble.Typed(rich, 2));
            string mid = TildaBubble.Typed(rich, 8);
            Assert.AreEqual("Drück <b><color=" + TildaBubble.KeyColor + ">St</color></b><color=#00000000><b>art</b> jetzt.</color>", mid);
            foreach (int n in new[] { 0, 1, 6, 7, 11, 12, 18 })
                Assert.AreEqual(TildaBubble.Plain(rich), TildaBubble.Plain(TildaBubble.Typed(rich, n)), "cut at " + n + " keeps every character (no re-wrapping)");
        }

        [Test]
        public void LongTextsArePagedAtSentenceEnds()
        {
            foreach (bool touch in new[] { false, true })
                for (var step = TutorialStep.Greeting; step < TutorialStep.Done; step++)
                {
                    string rich = TutorialGuide.TextFor(step, touch);
                    foreach (int max in new[] { TildaBubble.RemarkMaxChars, 114, 150 })
                    {
                        var pages = TildaBubble.Pages(rich, max);
                        Assert.IsTrue(pages.Count >= 1 && pages.Count <= 3, step + ": " + pages.Count + " pages");
                        string joined = string.Join(" ", pages);
                        Assert.AreEqual(TildaBubble.Plain(rich), TildaBubble.Plain(joined), step + " loses nothing");
                        foreach (var page in pages)
                        {
                            Assert.LessOrEqual(TildaBubble.Plain(page).Length, max, step.ToString());
                            Assert.AreEqual(Count(page, "<b>"), Count(page, "</b>"), "tags stay paired: " + page);
                            Assert.AreEqual(Count(page, "<color"), Count(page, "</color>"), page);
                        }
                    }
                }
            var two = TildaBubble.Pages("Eins zwei drei vier. Fünf sechs sieben acht neun zehn. Elf zwölf.", 40);
            Assert.AreEqual(3, two.Count);
            Assert.AreEqual("Eins zwei drei vier.", two[0]);
            Assert.AreEqual("Fünf sechs sieben acht neun zehn.", two[1]);
            Assert.AreEqual(1, TildaBubble.Pages("Kurz.", 40).Count);
            Assert.AreEqual(0, TildaBubble.Pages("", 40).Count);
        }

        static int Count(string s, string what)
        {
            int n = 0;
            for (int i = s.IndexOf(what, StringComparison.Ordinal); i >= 0; i = s.IndexOf(what, i + what.Length, StringComparison.Ordinal)) n++;
            return n;
        }

        [Test]
        public void TheTailPointsFromTheNearestEdgeTowardsHerMouth()
        {
            var box = new Rect(0f, 0f, 700f, 300f);
            TildaBubble.TailGeometry(box, new Vector2(-140f, 120f), 40f, out var basePoint, out float angle, out float length);
            Assert.AreEqual(TildaBubble.TailInset, basePoint.x, 0.01f, "left edge");
            Assert.AreEqual(120f, basePoint.y, 0.01f);
            Assert.AreEqual(-90f, angle, 0.5f, "the tip (down at rest) turns to the left");
            Assert.AreEqual(140f + TildaBubble.TailInset - 40f, length, 0.5f);

            TildaBubble.TailGeometry(box, new Vector2(650f, -900f), 0f, out basePoint, out angle, out length);
            Assert.AreEqual(TildaBubble.TailInset, basePoint.y, 0.01f, "bottom edge");
            Assert.Less(basePoint.x, 700f - 48f, "clear of the rounded corner");
            Assert.AreEqual(TildaBubble.MaxTail + TildaBubble.TailInset, length, 0.01f, "never a spear");
        }

        [Test]
        public void ARemarkStaysOnScreenBesideHer()
        {
            foreach (var canvas in new[] { Landscape, Portrait, new Vector2(2560f, 1920f) })
                foreach (var panel in new[] { TitlePanel, new Vector2(880f, 1360f) })
                {
                    var l = TildaPresenter.Compute(canvas, panel, 0.6f);
                    if (l.placement == TildaPresenter.Placement.Hidden) continue;
                    const float height = 300f;
                    var r = TildaPresenter.Remark(l, canvas, panel, height);
                    Assert.IsTrue(r.width >= TildaPresenter.MinRemarkWidth && r.width <= TildaPresenter.RemarkWidth);
                    Assert.GreaterOrEqual(r.corner.x - r.width, -canvas.x * 0.5f, canvas + " left edge");
                    Assert.LessOrEqual(r.corner.y + height, canvas.y * 0.5f, canvas + " top edge");
                    Assert.Less(r.corner.x, r.mouth.x, "left of her mouth, the tail reaches over");
                    if (l.placement == TildaPresenter.Placement.Side) Assert.Less(r.corner.x, -panel.x * 0.5f, "clear of the panel");
                }
        }

        // ---------------------------------------------------------------- face

        static float Distance(TildaFace a, TildaFace b) =>
            Mathf.Abs(a.lidL - b.lidL) + Mathf.Abs(a.lowerLid - b.lowerLid) + 10f * Mathf.Abs(a.browLiftL - b.browLiftL) + 10f * Mathf.Abs(a.browLiftR - b.browLiftR)
            + 0.05f * Mathf.Abs(a.browTiltL - b.browTiltL) + 0.05f * Mathf.Abs(a.browTiltR - b.browTiltR) + (a.look - b.look).magnitude
            + Mathf.Abs(a.mouthWidth - b.mouthWidth) + Mathf.Abs(a.smile - b.smile) + 5f * Mathf.Abs(a.glassesSlip - b.glassesSlip);

        [Test]
        public void EveryPoseHasItsOwnFace()
        {
            var poses = (TildaPose[])Enum.GetValues(typeof(TildaPose));
            for (int i = 0; i < poses.Length; i++)
                for (int j = i + 1; j < poses.Length; j++)
                    Assert.Greater(Distance(TildaFace.For(poses[i], 1f), TildaFace.For(poses[j], 1f)), 0.2f, poses[i] + " vs " + poses[j]);
            Assert.AreEqual(0f, TildaFace.For(TildaPose.Sleepy, 1f).lidL, "asleep her eyes are shut");
            Assert.Greater(TildaFace.For(TildaPose.Present, 1f).look.x, 0.5f, "she looks at what she presents");
            Assert.Less(TildaFace.For(TildaPose.Present, -1f).look.x, -0.5f);
            Assert.Greater(TildaFace.For(TildaPose.Comfort, 1f).browTiltL, 12f, "worried brows: inner ends up");
            var idle = TildaFace.For(TildaPose.Idle, 1f);
            Assert.AreNotEqual(idle.lidL, idle.lidR, "a face, not a mirror image");
            var mid = TildaFace.Lerp(idle, TildaFace.For(TildaPose.Wave, 1f), 0.5f);
            Assert.IsTrue(mid.lidL > idle.lidL && mid.lidL < 1f);
        }

        [Test]
        public void TheBubbleTailKnowsWhereHerMouthIs()
        {
            var go = new GameObject("TildaMouthProbe", typeof(RectTransform), typeof(UnityEngine.UI.RawImage)) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var view = go.AddComponent<TildaView>();
                foreach (bool full in new[] { false, true })
                {
                    view.SetFullBody(full);
                    TildaPortrait.RenderNow(TildaPose.Idle, false, 0f);
                    var measured = TildaPortrait.MeasureMouth();
                    Assert.IsTrue(measured.HasValue, "the rig exists while a view is enabled");
                    Vector2 expected = TildaPortrait.MouthIn(full);
                    Assert.AreEqual(expected.x, measured.Value.x, 0.02f, "x, full body " + full);
                    Assert.AreEqual(expected.y, measured.Value.y, 0.02f, "y, full body " + full);
                }
                var parts = TildaPortrait.Parts;
                Assert.IsNotNull(parts.glasses);
                Assert.IsNotNull(parts.browL);
                Assert.IsNotNull(parts.flower);
                Assert.IsNotNull(parts.curl);
                Assert.Less(parts.vertexCount, 20000, "still a light model");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ---------------------------------------------------------------- queue rules

        [Test]
        public void ARequestOnSilenceStartsWithTheNextTick()
        {
            var q = new TildaVoiceQueue();
            Assert.IsFalse(q.Busy);
            Assert.IsTrue(q.Request("a", VoicePriority.Queue));
            Assert.AreEqual("a", q.Tick(0.016f, false));
            Assert.AreEqual("a", q.Current);
            Assert.IsNull(q.Tick(0.016f, true));
            Assert.AreEqual(1f, q.Gain);
        }

        [Test]
        public void ANewStepInterruptsWithAShortFade()
        {
            var q = new TildaVoiceQueue();
            q.Request("old", VoicePriority.Interrupt);
            q.Tick(0.016f, false);
            q.Request("waiting", VoicePriority.Queue);

            Assert.IsTrue(q.Request("new", VoicePriority.Interrupt));
            Assert.IsTrue(q.Fading);
            Assert.IsNull(q.Tick(0.05f, true));
            Assert.IsTrue(q.Gain < 1f && q.Gain > 0f);
            Assert.AreEqual("old", q.Current);
            Assert.IsNull(q.Tick(0.05f, true));
            Assert.AreEqual("new", q.Tick(0.06f, true), "after 0.15 s the new line starts");
            Assert.IsFalse(q.Fading);
            Assert.AreEqual(1f, q.Gain);
            Assert.IsNull(q.Tick(0.016f, true));
            Assert.IsNull(q.Tick(0.016f, false), "the interrupt dropped the waiting line");
            Assert.IsFalse(q.Busy);
        }

        [Test]
        public void InterruptingWithTheLineAlreadySpokenIsIgnored()
        {
            var q = new TildaVoiceQueue();
            q.Request("a", VoicePriority.Interrupt);
            q.Tick(0.016f, false);
            Assert.IsFalse(q.Request("a", VoicePriority.Interrupt));
            Assert.IsFalse(q.Fading);
        }

        [Test]
        public void QueuedLinesFollowInOrderWithoutDuplicatesUpToTheCap()
        {
            var q = new TildaVoiceQueue();
            q.Request("cheer", VoicePriority.Interrupt);
            Assert.AreEqual("cheer", q.Tick(0.016f, false));
            Assert.IsTrue(q.Request("step", VoicePriority.Queue));
            Assert.IsFalse(q.Request("step", VoicePriority.Queue));
            Assert.IsFalse(q.Request("cheer", VoicePriority.Queue));
            Assert.IsTrue(q.Request("b", VoicePriority.Queue));
            Assert.IsFalse(q.Request("c", VoicePriority.Queue), "cap of " + TildaVoiceQueue.MaxQueued);
            Assert.AreEqual(2, q.Waiting);

            Assert.IsNull(q.Tick(1f, true));
            Assert.AreEqual("step", q.Tick(0.016f, false), "the remark is over, the next line starts");
            Assert.AreEqual("b", q.Tick(0.016f, false));
            Assert.IsNull(q.Tick(0.016f, false));
        }

        [Test]
        public void IdleRemarksOnlyFillSilence()
        {
            var q = new TildaVoiceQueue();
            Assert.IsTrue(q.Request("idle_1", VoicePriority.IfSilent));
            Assert.IsFalse(q.Request("idle_2", VoicePriority.IfSilent), "one is already about to start");
            q.Tick(0.016f, false);
            Assert.IsFalse(q.Request("idle_2", VoicePriority.IfSilent));
            Assert.IsNull(q.Tick(0.016f, false));
            Assert.IsTrue(q.Request("idle_2", VoicePriority.IfSilent));
            Assert.IsFalse(q.Request(null, VoicePriority.Interrupt));
            Assert.IsFalse(q.Request("", VoicePriority.Queue));
        }

        [Test]
        public void AMenuSilencesOnlyItsOwnLines()
        {
            var q = new TildaVoiceQueue();
            q.Request("greeting", VoicePriority.Queue);
            q.Tick(0.016f, false);
            q.Request("tut_greeting", VoicePriority.Interrupt);

            q.Stop(true, "greeting", "idle_");
            Assert.IsTrue(q.Fading);
            Assert.AreEqual("tut_greeting", q.Tick(0.2f, true), "the tutorial line survives the title's hush");

            q.Stop(true, "help_");
            Assert.IsFalse(q.Fading);
            q.Request("cheer_1", VoicePriority.Queue);
            q.Stop(true);
            Assert.IsTrue(q.Fading);
            Assert.IsNull(q.Tick(0.2f, true));
            Assert.IsFalse(q.Busy);
        }

        [Test]
        public void ALineNobodyShowsMakesRoomForTheNextLine()
        {
            var q = new TildaVoiceQueue();
            q.Request("missing", VoicePriority.Queue);
            q.Request("next", VoicePriority.Queue);
            Assert.AreEqual("missing", q.Tick(0.016f, false));
            q.Fail();
            Assert.AreEqual("next", q.Tick(0.016f, false));
        }

        // ---------------------------------------------------------------- presenter layout

        static readonly Vector2 Landscape = new Vector2(1920f * 1920f / 1080f, 1920f), Portrait = new Vector2(1080f, 1920f);
        static readonly Vector2 TitlePanel = new Vector2(920f, 1540f);

        [Test]
        public void OnAWideScreenSheStandsBesideThePanelClearOfMinimapAndPanel()
        {
            var l = TildaPresenter.Compute(Landscape, TitlePanel, 0.6f);
            Assert.AreEqual(TildaPresenter.Placement.Side, l.placement);
            Assert.LessOrEqual(l.size, 0.6f * Landscape.y + 0.01f);
            Assert.Greater(l.size, 0.45f * Landscape.y, "she is big: " + l.size);

            float bodyRight = l.center.x + TildaPresenter.BodyHalfWidth * l.size, bodyLeft = l.center.x - TildaPresenter.BodyHalfWidth * l.size;
            Assert.LessOrEqual(bodyRight, -TitlePanel.x * 0.5f - 1f, "does not touch the panel");
            Assert.GreaterOrEqual(bodyLeft, -Landscape.x * 0.5f + 40f + 340f, "clear of the minimap");
            float feet = l.center.y - (0.5f - TildaPresenter.FeetFraction) * l.size + Landscape.y * 0.5f;
            Assert.IsTrue(feet >= 40f && feet <= 40f + 0.06f * l.size, "feet near the bottom edge: " + feet);
            float shadowBottom = l.center.y - (0.5f - TildaPresenter.GroundFraction + 0.5f * TildaPresenter.ShadowHeight) * l.size + Landscape.y * 0.5f;
            Assert.GreaterOrEqual(shadowBottom, 0f, "her shadow stays on screen");
        }

        [Test]
        public void ASmallerShareMakesHerSmaller()
        {
            float title = TildaPresenter.Compute(Landscape, TitlePanel, 0.6f).size;
            float pause = TildaPresenter.Compute(Landscape, new Vector2(880f, 1360f), 0.42f).size;
            Assert.Less(pause, title);
            Assert.AreEqual(0.42f * Landscape.y, pause, 0.5f);
        }

        [Test]
        public void OnAPortraitPhoneShePeeksOverThePanelsTopEdge()
        {
            var l = TildaPresenter.Compute(Portrait, TitlePanel, 0.6f);
            Assert.AreEqual(TildaPresenter.Placement.Peek, l.placement);
            float half = TildaPresenter.BodyHalfWidth * l.size;
            Assert.IsTrue(l.center.x >= 0f && l.center.x + half <= TitlePanel.x * 0.5f - 48f, "over the straight part of the panel edge");
            Assert.IsTrue(l.size >= TildaPresenter.MinPeekSize && l.size <= TildaPresenter.MaxPeekSize);
            float cut = l.center.y - 0.5f * l.size + TildaPresenter.PeekCut * l.size;
            Assert.AreEqual(TitlePanel.y * 0.5f, cut, 0.5f, "the panel edge cuts her below the mouth");
            float crater = l.center.y - 0.5f * l.size + TildaPresenter.PeekBodyTop * l.size;
            Assert.LessOrEqual(crater, Portrait.y * 0.5f, "her crater stays on screen");
        }

        [Test]
        public void TheAnleitungKeepsItsSmallTildaInPortrait()
        {
            Assert.AreEqual(TildaPresenter.Placement.Hidden, TildaPresenter.Compute(Portrait, HelpScreen.PanelSize, 0.56f, false).placement);
            Assert.AreEqual(TildaPresenter.Placement.Side, TildaPresenter.Compute(Landscape, HelpScreen.PanelSize, 0.56f, false).placement);
            Assert.AreEqual(TildaPresenter.Placement.Hidden, TildaPresenter.Compute(Portrait, new Vector2(960f, 1800f), 0.5f).placement, "no room above the panel");
        }

        [Test]
        public void NewPosesKeepTheOldOnesInPlace()
        {
            Assert.AreEqual(0, (int)TildaPose.Idle);
            Assert.AreEqual(4, (int)TildaPose.Sleepy);
            Assert.IsTrue(Enum.IsDefined(typeof(TildaPose), "Present"));
            Assert.IsTrue(Enum.IsDefined(typeof(TildaPose), "Comfort"));
        }
    }
}
