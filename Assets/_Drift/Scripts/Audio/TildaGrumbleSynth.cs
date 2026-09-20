using System;

namespace Drift.Audio
{
    public enum GrumbleMood
    {
        Warm,
        // Greeting, a new island: brighter, the sentence melody rises.
        Cheerful,
        // A solved step: quick, bouncing little giggles.
        Giggly,
        // Game over: low, slow and quiet.
        Soft,
        // Dozing: a slow snore-mumble.
        Sleepy,
    }

    // The clock that the typewriter and the grumble share: a character takes 1 / charsPerSecond, punctuation adds a
    // breath. Pure and allocation free.
    public static class GrumbleTiming
    {
        public const float SentencePause = 0.34f, CommaPause = 0.12f;

        public static bool EndsSentence(char c) => c == '.' || c == '!' || c == '?' || c == '…';

        static bool IsBreath(char c) => c == ',' || c == ';' || c == ':' || c == '–' || c == '—';

        static int LastContent(string text)
        {
            for (int i = text.Length - 1; i >= 0; i--) if (char.IsLetterOrDigit(text[i])) return i;
            return -1;
        }

        // The pause after text[k]; a run of marks ("?!", "...") pauses once, and nothing pauses after the last word.
        public static float PauseAfter(string text, int k, int lastContent)
        {
            if (k >= lastContent || k + 1 >= text.Length) return 0f;
            char c = text[k], next = text[k + 1];
            if (EndsSentence(c)) return EndsSentence(next) ? 0f : SentencePause;
            if (IsBreath(c)) return next == ' ' ? CommaPause : 0f;
            return 0f;
        }

        // Seconds until the first `index` characters are on screen.
        public static float TimeOfChar(string text, int index, float charsPerSecond)
        {
            if (string.IsNullOrEmpty(text) || index <= 0) return 0f;
            float step = 1f / Math.Max(1f, charsPerSecond), t = 0f;
            int last = LastContent(text), n = Math.Min(index, text.Length);
            for (int k = 0; k < n; k++)
            {
                t += step;
                if (k < n - 1) t += PauseAfter(text, k, last);
            }
            return t;
        }

        public static float Duration(string text, float charsPerSecond) =>
            string.IsNullOrEmpty(text) ? 0f : TimeOfChar(text, text.Length, charsPerSecond);

        // How many characters are on screen after `time` seconds.
        public static int CharsAt(string text, float time, float charsPerSecond)
        {
            if (string.IsNullOrEmpty(text) || time <= 0f) return 0;
            float step = 1f / Math.Max(1f, charsPerSecond), t = 0f;
            int last = LastContent(text);
            for (int k = 0; k < text.Length; k++)
            {
                t += step;
                if (t > time) return k;
                t += PauseAfter(text, k, last);
            }
            return text.Length;
        }
    }

    public struct GrumbleSyllable
    {
        public float start, length;
        // Hz at the start and the change over the syllable.
        public float pitch, glide;
        // Formants at the start and at the end (two vowels mumbled into one grunt glide from one to the other).
        public float f1, f2, f3, f1End, f2End;
        public float gain;
        // Soft consonant noise at the onset: strength, centre frequency, decay.
        public float tick, tickHz, tickSeconds;
        // A closed-mouth "mh" instead of a vowel.
        public bool hum;
    }

    // Turns a text into grunts: one per vowel group of the text at the moment the typewriter reaches it, thinned out
    // to what an old lady can mumble, with a sentence melody and a little "mh-hm" where a sentence ends.
    public sealed class GrumblePlan
    {
        public const int MaxSyllables = 160;
        public const float MinPitch = 142f, MaxPitch = 232f;

        public readonly GrumbleSyllable[] Items = new GrumbleSyllable[MaxSyllables];
        public int Count { get; private set; }
        public int Hums { get; private set; }
        // Seconds until the last grunt has faded.
        public float Duration { get; private set; }
        public GrumbleMood Mood { get; private set; }

        struct Voice
        {
            public float pitch, spacing, length, gain, tick, neutral, rise;
        }

        static Voice VoiceOf(GrumbleMood mood)
        {
            switch (mood)
            {
                case GrumbleMood.Cheerful: return new Voice { pitch = 168f, spacing = 0.108f, length = 0.15f, gain = 1f, tick = 1f, neutral = 0.4f, rise = 3.5f };
                case GrumbleMood.Giggly: return new Voice { pitch = 169f, spacing = 0.09f, length = 0.1f, gain = 0.95f, tick = 0.8f, neutral = 0.35f, rise = 1.5f };
                case GrumbleMood.Soft: return new Voice { pitch = 153f, spacing = 0.15f, length = 0.21f, gain = 0.72f, tick = 0.4f, neutral = 0.55f, rise = -2.5f };
                case GrumbleMood.Sleepy: return new Voice { pitch = 151f, spacing = 0.34f, length = 0.4f, gain = 0.6f, tick = 0f, neutral = 0.8f, rise = -3f };
                default: return new Voice { pitch = 170f, spacing = 0.118f, length = 0.16f, gain = 0.9f, tick = 0.8f, neutral = 0.45f, rise = -2.5f };
            }
        }

        public static bool IsVowel(char c)
        {
            switch (char.ToLowerInvariant(c))
            {
                case 'a': case 'e': case 'i': case 'o': case 'u': case 'y':
                case 'ä': case 'ö': case 'ü':
                    return true;
                default:
                    return false;
            }
        }

        // Vowel groups of the text: what a reader would count as syllables.
        public static int SyllablesIn(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int n = 0;
            bool inGroup = false;
            foreach (char c in text)
            {
                bool v = IsVowel(c);
                if (v && !inGroup) n++;
                inGroup = v;
            }
            return n;
        }

        static void Formants(char vowel, out float f1, out float f2, out float f3)
        {
            switch (char.ToLowerInvariant(vowel))
            {
                case 'a': f1 = 760f; f2 = 1300f; f3 = 2600f; break;
                case 'e': f1 = 500f; f2 = 1950f; f3 = 2650f; break;
                case 'i': case 'y': f1 = 320f; f2 = 2250f; f3 = 3000f; break;
                case 'o': f1 = 480f; f2 = 880f; f3 = 2550f; break;
                case 'u': f1 = 340f; f2 = 820f; f3 = 2400f; break;
                case 'ä': f1 = 620f; f2 = 1800f; f3 = 2600f; break;
                case 'ö': f1 = 440f; f2 = 1500f; f3 = 2400f; break;
                case 'ü': f1 = 320f; f2 = 1700f; f3 = 2350f; break;
                default: f1 = 520f; f2 = 1500f; f3 = 2550f; break;
            }
        }

        static float Hash01(int a, int b)
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ 0x9E3779B9u;
            h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
            return (h & 0xFFFF) / 65535f;
        }

        static float Semitones(float st) => (float)Math.Pow(2.0, st / 12.0);

        static float ClampPitch(float hz) => hz < MinPitch ? MinPitch : hz > MaxPitch ? MaxPitch : hz;

        public void Build(string text, GrumbleMood mood, float charsPerSecond)
        {
            Count = 0;
            Hums = 0;
            Duration = 0f;
            Mood = mood;
            if (string.IsNullOrEmpty(text)) return;
            var v = VoiceOf(mood);
            float step = 1f / Math.Max(1f, charsPerSecond), t = 0f;
            int n = text.Length;
            int lastContent = -1;
            for (int i = n - 1; i >= 0; i--) if (char.IsLetterOrDigit(text[i])) { lastContent = i; break; }

            int sentenceFrom = 0, sentenceTo = SentenceEnd(text, 0), sentenceIndex = 0;
            float lastStart = -10f;
            bool inGroup = false, wordStart = true;
            for (int i = 0; i < n; i++)
            {
                char c = text[i];
                bool vowel = IsVowel(c);
                if (vowel && !inGroup)
                {
                    float pos = sentenceTo > sentenceFrom ? (i - sentenceFrom) / (float)(sentenceTo - sentenceFrom) : 0f;
                    bool question = sentenceTo < n && text[sentenceTo] == '?';
                    if (t - lastStart >= v.spacing || Count == 0) Add(text, i, t, pos, question, wordStart, sentenceIndex, v, mood);
                    else Merge(c, t, v);
                    if (t - lastStart >= v.spacing) lastStart = t;
                    wordStart = false;
                }
                inGroup = vowel;
                if (c == ' ' || c == '-') wordStart = true;

                t += step;
                if (GrumbleTiming.EndsSentence(c) && (i + 1 >= n || !GrumbleTiming.EndsSentence(text[i + 1])))
                {
                    bool final = i >= lastContent;
                    if (Count > 0 && (final || Hash01(i, 7) > 0.62f)) Hum(t, v, mood, final);
                    sentenceFrom = i + 1;
                    sentenceTo = SentenceEnd(text, i + 1);
                    sentenceIndex++;
                    wordStart = true;
                }
                t += GrumbleTiming.PauseAfter(text, i, lastContent);
            }
            for (int k = 0; k < Count; k++) Duration = Math.Max(Duration, Items[k].start + Items[k].length);
        }

        static int SentenceEnd(string text, int from)
        {
            for (int i = from; i < text.Length; i++) if (GrumbleTiming.EndsSentence(text[i])) return i;
            return text.Length;
        }

        void Add(string text, int i, float t, float pos, bool question, bool wordStart, int sentence, Voice v, GrumbleMood mood)
        {
            if (Count >= MaxSyllables) return;
            CloseBefore(t);
            Formants(text[i], out float f1, out float f2, out float f3);
            const float n1 = 520f, n2 = 1500f, n3 = 2550f;
            f1 += (n1 - f1) * v.neutral; f2 += (n2 - f2) * v.neutral; f3 += (n3 - f3) * v.neutral;

            // Melody in semitones: the sentence starts a little high and drifts to where the mood takes it; the
            // first syllable of a word is stressed, a question lifts its tail, and no two grunts are quite alike.
            float st = 1.5f * (1f - pos) + v.rise * pos;
            if (wordStart) st += 1.1f;
            if (question && pos > 0.7f) st += 9f * (pos - 0.7f) / 0.3f;
            st += 1.6f * (Hash01(i, sentence) - 0.5f);
            if (mood == GrumbleMood.Giggly) st += (Count & 1) == 0 ? 2.2f : -1.2f;

            var s = new GrumbleSyllable
            {
                start = t, length = v.length * (0.85f + 0.3f * Hash01(i, 3)),
                pitch = ClampPitch(v.pitch * Semitones(st)),
                f1 = f1, f2 = f2, f3 = f3, f1End = f1, f2End = f2,
                gain = v.gain * (wordStart ? 1f : 0.86f),
            };
            s.glide = mood == GrumbleMood.Sleepy ? -14f : mood == GrumbleMood.Giggly ? 10f : (Hash01(i, 5) - 0.6f) * 12f;
            char before = i > 0 ? char.ToLowerInvariant(text[i - 1]) : ' ';
            Consonant(before, v.tick, ref s);
            Items[Count++] = s;
        }

        // Two vowels closer together than she can mumble: the earlier grunt is drawn out and slides to the second.
        void Merge(char vowel, float t, Voice v)
        {
            if (Count == 0) return;
            ref var s = ref Items[Count - 1];
            if (s.hum) return;
            Formants(vowel, out float f1, out float f2, out _);
            s.f1End = f1 + (520f - f1) * v.neutral;
            s.f2End = f2 + (1500f - f2) * v.neutral;
            s.length = Math.Min(v.length * 1.7f, Math.Max(s.length, t - s.start + v.length * 0.55f));
        }

        // A grunt ends a moment before the next one starts, so the rhythm of the sentence stays audible.
        void CloseBefore(float t)
        {
            if (Count == 0) return;
            ref var s = ref Items[Count - 1];
            float room = t - s.start - 0.028f;
            if (s.length > room) s.length = Math.Max(0.045f, room);
        }

        static void Consonant(char c, float scale, ref GrumbleSyllable s)
        {
            switch (c)
            {
                case 'p': case 't': case 'k': case 'b': case 'd': case 'g': case 'c': case 'q':
                    s.tick = 0.5f * scale; s.tickHz = 2300f; s.tickSeconds = 0.009f; break;
                case 's': case 'z': case 'ß': case 'f': case 'v': case 'x': case 'j':
                    s.tick = 0.3f * scale; s.tickHz = 4300f; s.tickSeconds = 0.024f; break;
                case 'h':
                    s.tick = 0.22f * scale; s.tickHz = 1500f; s.tickSeconds = 0.03f; break;
                case 'm': case 'n': case 'l': case 'r': case 'w':
                    s.tick = 0f; break;
                default:
                    s.tick = 0.1f * scale; s.tickHz = 1800f; s.tickSeconds = 0.007f; break;
            }
        }

        // The happy little "mh-hm" in the breath after a sentence; sad and sleepy moods only sigh.
        void Hum(float t, Voice v, GrumbleMood mood, bool final)
        {
            CloseBefore(t + 0.03f);
            bool sigh = mood == GrumbleMood.Soft || mood == GrumbleMood.Sleepy;
            int parts = sigh ? 1 : mood == GrumbleMood.Giggly ? 3 : 2;
            float at = t + 0.04f;
            for (int k = 0; k < parts && Count < MaxSyllables; k++)
            {
                float length = sigh ? (final ? 0.42f : 0.24f) : k == parts - 1 ? (final ? 0.2f : 0.14f) : 0.1f;
                float st = sigh ? -1f : parts == 3 ? 1f + 2f * k : k == 0 ? -1f : 3.5f;
                Items[Count++] = new GrumbleSyllable
                {
                    start = at, length = length, pitch = ClampPitch(v.pitch * Semitones(st)), glide = sigh ? -16f : k == parts - 1 ? 8f : 0f,
                    f1 = 270f, f2 = 1050f, f3 = 2300f, f1End = 270f, f2End = 1050f, gain = v.gain * 0.8f, hum = true,
                };
                Hums++;
                at += length + 0.025f;
            }
        }
    }

    // Tilda's voice: no words, a warm elderly grumble. A glottal saw runs through three formant band-passes picked
    // from the vowels of the text, with tremor, a subharmonic growl, soft consonant ticks and a low rumble. The main
    // thread hands over a plan (three preallocated slots, so the audio thread never reads what is being written);
    // Render runs on the audio thread and allocates nothing. Level is the loudness it actually produced: her mouth.
    public sealed class TildaGrumbleSynth : ISynthSource
    {
        const int Block = 32;
        const float FadeSeconds = 0.02f;

        public volatile bool Enabled = true;
        public float Volume = 1f;

        readonly GrumblePlan[] _slots = { new GrumblePlan(), new GrumblePlan(), new GrumblePlan() };
        volatile int _active = -1, _pending = -1;
        volatile bool _stop;
        volatile float _level;
        volatile int _started;

        int _sr;
        float _invSr, _time, _clock, _fade = 1f;
        int _index;
        bool _inSyllable;
        GrumbleSyllable _s;
        readonly float[] _mix = new float[Block];
        Noise _noise = new Noise(0x71DA5EEDu);

        float _phase, _subPhase, _f0, _srcLp, _bodyLp, _outLp;
        float _l1, _b1, _l2, _b2, _l3, _b3, _c1, _c2, _c3;
        float _tickEnv, _tickDecay, _tickLow, _tickBand, _tickCoef, _tickAmp;
        float _rumbleLp, _rumbleLp2, _jitter, _follower;
        float _srcCoef, _bodyCoef, _outCoef, _rumbleCoef, _jitterCoef, _attackCoef, _releaseCoef;

        public TildaGrumbleSynth(int sampleRate = 48000)
        {
            Prepare(sampleRate);
        }

        // 0..1, how loud she is right now.
        public float Level => _level;
        public bool Sounding => _active >= 0 || _pending >= 0;
        public int LinesStarted => _started;
        public int SampleRate => _sr;

        void Prepare(int sampleRate)
        {
            if (sampleRate <= 0) sampleRate = 48000;
            _sr = sampleRate;
            _invSr = 1f / sampleRate;
            _srcCoef = SynthMath.OnePoleCoef(2600f, sampleRate);
            _bodyCoef = SynthMath.OnePoleCoef(420f, sampleRate);
            _outCoef = SynthMath.OnePoleCoef(4200f, sampleRate);
            _rumbleCoef = SynthMath.OnePoleCoef(95f, sampleRate);
            _jitterCoef = SynthMath.OnePoleCoef(9f, sampleRate);
            _attackCoef = SynthMath.OnePoleCoef(60f, sampleRate);
            _releaseCoef = SynthMath.OnePoleCoef(7f, sampleRate);
        }

        // Main thread. Returns the plan that will sound (its Duration tells how long), or null when switched off.
        public GrumblePlan Speak(string text, GrumbleMood mood, float charsPerSecond)
        {
            if (!Enabled || string.IsNullOrEmpty(text)) return null;
            int a = _active, p = _pending;
            int slot = 0;
            while (slot == a || slot == p) slot++;
            var plan = _slots[slot];
            plan.Build(text, mood, charsPerSecond);
            if (plan.Count == 0) return null;
            _stop = false;
            _pending = slot;
            return plan;
        }

        public void Stop()
        {
            _pending = -1;
            _stop = true;
        }

        public void Render(float[] buffer, int channels, int sampleRate)
        {
            if (channels <= 0) return;
            if (sampleRate != _sr) Prepare(sampleRate);
            if (!Enabled)
            {
                Array.Clear(buffer, 0, buffer.Length);
                _active = -1;
                _pending = -1;
                _level = 0f;
                _follower = 0f;
                return;
            }
            int frames = buffer.Length / channels;
            int done = 0;
            while (done < frames)
            {
                int n = frames - done;
                if (n > Block) n = Block;
                RenderBlock(n);
                SynthMath.WriteInterleaved(buffer, channels, done, _mix, _mix, n);
                done += n;
            }
            float level = _follower * 3.4f;
            _level = level > 1f ? 1f : level < 0.02f ? 0f : level;
        }

        void Switch()
        {
            int next = _pending;
            if (next >= 0)
            {
                _pending = -1;
                _active = next;
                _time = 0f;
                _index = 0;
                _inSyllable = false;
                _started++;
            }
            else _active = -1;
            _stop = false;
            _fade = 1f;
        }

        void RenderBlock(int n)
        {
            bool leaving = _pending >= 0 || _stop;
            if (leaving && (_active < 0 || !_inSyllable)) { Switch(); leaving = false; }
            int slot = _active;
            if (slot < 0)
            {
                for (int i = 0; i < n; i++)
                {
                    _mix[i] = 0f;
                    _follower += (0f - _follower) * _releaseCoef;
                }
                return;
            }
            var plan = _slots[slot];

            if (!_inSyllable && _index < plan.Count && _time >= plan.Items[_index].start)
            {
                _s = plan.Items[_index++];
                _inSyllable = true;
                if (_f0 < 60f) _f0 = _s.pitch;
                if (_s.tick > 0f)
                {
                    _tickEnv = 1f;
                    _tickAmp = _s.tick;
                    _tickDecay = SynthMath.DecayCoef(Math.Max(0.003f, _s.tickSeconds), _sr);
                    _tickCoef = SynthMath.SvfCoef(_s.tickHz, _sr);
                }
            }

            float u = 0f, env = 0f, target = _f0;
            if (_inSyllable)
            {
                u = (_time - _s.start) / Math.Max(0.02f, _s.length);
                if (u >= 1f) { _inSyllable = false; u = 1f; }
                // Quick to open, slower to close: a grunt, not a beep.
                env = SynthMath.Sine(0.5f * (float)Math.Pow(u < 0f ? 0f : u, 0.62));
                target = _s.pitch + _s.glide * u;
                float open = u * _s.length / 0.035f;
                if (open > 1f) open = 1f;
                float f1 = (_s.f1 + (_s.f1End - _s.f1) * u) * (0.62f + 0.38f * open);
                float f2 = _s.f2 + (_s.f2End - _s.f2) * u;
                _c1 = SynthMath.SvfCoef(f1, _sr);
                _c2 = SynthMath.SvfCoef(f2, _sr);
                _c3 = SynthMath.SvfCoef(_s.f3, _sr);
            }

            _clock += n * _invSr;
            if (_clock > 600f) _clock -= 600f;
            float vibrato = 1f + 0.011f * SynthMath.Sine(_clock * 5.3f) + 0.006f * SynthMath.Sine(_clock * 2.1f + 0.3f);
            float tremor = 1f + 0.05f * SynthMath.Sine(_clock * 6.1f + 0.5f);
            float gain = _inSyllable ? _s.gain * env * tremor : 0f;
            bool hum = _s.hum;
            float g2 = hum ? 0.12f : 0.85f, g3 = hum ? 0.03f : 0.3f, body = hum ? 0.85f : 0.5f;
            const float q1 = 0.17f, q2 = 0.13f, q3 = 0.11f;
            float fadeStep = leaving ? _invSr / FadeSeconds : 0f;
            float glideCoef = SynthMath.OnePoleCoef(28f, _sr);

            for (int i = 0; i < n; i++)
            {
                _f0 += (target - _f0) * glideCoef;
                _jitter += (_noise.Next() - _jitter) * _jitterCoef;
                float inc = _f0 * vibrato * (1f + 0.02f * _jitter) * _invSr;
                _phase += inc;
                if (_phase >= 1f) _phase -= 1f;
                _subPhase += inc * 0.5f;
                if (_subPhase >= 1f) _subPhase -= 1f;

                // Falling saw with its step rounded off (polyBLEP), then tilted: a soft glottal pulse train.
                float saw = 1f - 2f * _phase;
                if (_phase < inc) { float x = _phase / inc; saw -= -(x * x) + 2f * x - 1f; }
                else if (_phase > 1f - inc) { float x = (_phase - 1f) / inc; saw -= x * x + 2f * x + 1f; }
                _srcLp += (saw - _srcLp) * _srcCoef;
                float src = _srcLp;

                float h1 = src - _l1 - q1 * _b1; _b1 += _c1 * h1; _l1 += _c1 * _b1;
                float h2 = src - _l2 - q2 * _b2; _b2 += _c2 * h2; _l2 += _c2 * _b2;
                float h3 = src - _l3 - q3 * _b3; _b3 += _c3 * h3; _l3 += _c3 * _b3;
                _bodyLp += (src - _bodyLp) * _bodyCoef;

                float sub = SynthMath.Sine(_subPhase);
                float voice = (_b1 * q1 * 2.2f + _b2 * q2 * 2.2f * g2 + _b3 * q3 * 2.2f * g3 + _bodyLp * body) * (1f - 0.08f * (0.5f + 0.5f * sub));

                float white = _noise.Next();
                _rumbleLp += (white - _rumbleLp) * _rumbleCoef;
                _rumbleLp2 += (_rumbleLp - _rumbleLp2) * _rumbleCoef;
                float under = 0.07f * sub + 1.2f * _rumbleLp2;

                float tick = 0f;
                if (_tickEnv > 1e-4f)
                {
                    float th = white - _tickLow - 0.6f * _tickBand;
                    _tickBand += _tickCoef * th;
                    _tickLow += _tickCoef * _tickBand;
                    tick = _tickBand * _tickEnv * _tickAmp * 0.22f;
                    _tickEnv *= _tickDecay;
                }

                float dry = (voice + under) * gain * 0.62f + tick;
                _outLp += (dry - _outLp) * _outCoef;
                if (leaving)
                {
                    _fade -= fadeStep;
                    if (_fade < 0f) _fade = 0f;
                }
                float o = SynthMath.SoftClip(_outLp * Volume * _fade) * 0.88f;
                _mix[i] = o;
                float mag = o < 0f ? -o : o;
                _follower += (mag - _follower) * (mag > _follower ? _attackCoef : _releaseCoef);
            }

            _time += n * _invSr;
            if (leaving && _fade <= 0f)
            {
                _inSyllable = false;
                Switch();
            }
            else if (!_inSyllable && _index >= plan.Count && _time > plan.Duration + 0.05f && _pending < 0) _active = -1;
        }
    }
}
