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
        // Hz at the start and the change over the syllable; arch adds a rise-and-fall (Hz at the middle).
        public float pitch, glide, arch;
        // Formants at the start and at the end (two vowels mumbled into one grunt, or a diphthong, glide from one to
        // the other).
        public float f1, f2, f3, f1End, f2End;
        public float gain;
        // Soft consonant noise at the onset: strength, centre frequency, decay.
        public float tick, tickHz, tickSeconds;
        // Where the consonant leaves the formants (its locus) and how long they take to reach the vowel; 0 = none.
        public float f1On, f2On, onset;
        // Seconds of closed-mouth murmur (m, n) before the vowel opens.
        public float nasal;
        // 0..1 air in the voice (h, sighs, giggles) and a quick happy vibrato.
        public float breath, trill;
        // A closed-mouth "mh" instead of a vowel.
        public bool hum;
        // Not a syllable of the text: the "mh-hm" after a sentence, a thoughtful "hmm", a giggle.
        public bool extra;
    }

    // Turns a text into grunts: one per vowel group of the text at the moment the typewriter reaches it, thinned out
    // to what an old lady can mumble, with a sentence melody and a little "mh-hm" where a sentence ends. The text
    // decides the rest: "?" rises (a W-question falls), "!" is higher and bouncier, "..." trails off into a thoughtful
    // "hmm"; interjections get their own sound ("Oh!" surprised, "Juhu" a happy trill, "Hihi" a giggle, "Ach je" a
    // sigh, "Hmm" a hum); consonants shape the onset (plosive bursts, hissing fricatives, nasal murmurs, formant
    // transitions) and the line's own hash gives it a register, tempo and melody range of its own, so a line always
    // sounds the same and two lines never quite alike.
    public sealed class GrumblePlan
    {
        public const int MaxSyllables = 160;
        public const float MinPitch = 142f, MaxPitch = 232f;

        public readonly GrumbleSyllable[] Items = new GrumbleSyllable[MaxSyllables];
        public int Count { get; private set; }
        // Items that are not a syllable of the text (hums, giggles, the thoughtful "hmm").
        public int Extras { get; private set; }
        // Seconds until the last grunt has faded.
        public float Duration { get; private set; }
        public GrumbleMood Mood { get; private set; }
        public uint Seed { get; private set; }

        enum Kind { Statement, Question, WQuestion, Exclaim, Trail }
        enum Word { Plain, Hum, Giggle, Surprise, Joy, Sigh, Hesitate }

        struct Voice
        {
            public float pitch, spacing, length, gain, tick, neutral, rise;
        }

        static readonly string[] SurpriseWords = { "oh", "oha", "huch", "hopla", "ups", "ui", "oi", "wow", "boah", "ah", "aha" };
        static readonly string[] JoyWords = { "juhu", "juchu", "hura", "yay", "jipie", "hui", "super", "tol", "prima", "klase", "yeah" };
        static readonly string[] SighWords = { "ach", "hach", "oje", "ohje", "ojemine", "uf", "puh", "tja", "naja", "schade", "leider", "och", "ohweh" };
        static readonly string[] HesitateWords = { "äh", "ähm", "öhm", "ehm" };
        static readonly string[] WWords = { "wer", "wie", "was", "wo", "wann", "warum", "wieso", "weshalb", "welche", "welcher", "welches", "wohin", "woher", "womit" };

        Voice _v;
        uint _seed;
        float _lineSt, _range;
        // Current sentence and word.
        Kind _kind;
        bool _sentenceJoy, _sentenceSad;
        int _sentenceGrunts;
        Word _word;
        int _wordFrom, _wordTo, _wordSyllables, _wordSyllable, _humChain;
        bool _giggled;

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

        // FNV-1a over the letters and digits: the same line always gets the same voice, and the same words keep it
        // whatever the punctuation does to the melody.
        public static uint SeedOf(string text)
        {
            uint h = 2166136261u;
            if (text != null)
                for (int i = 0; i < text.Length; i++)
                    if (char.IsLetterOrDigit(text[i])) { h ^= text[i]; h *= 16777619u; }
            return h == 0u ? 1u : h;
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
                // Schwa, the unstressed "-e", "-en", "-er".
                default: f1 = 520f; f2 = 1500f; f3 = 2550f; break;
            }
        }

        float Hash01(int a, int b)
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ _seed ^ 0x9E3779B9u;
            h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
            return (h & 0xFFFF) / 65535f;
        }

        static float Semitones(float st) => (float)Math.Pow(2.0, st / 12.0);

        // Above 200 Hz the pitch bends softly towards the ceiling instead of hitting it: stacked lifts ("Juhu!" in an
        // exclamation) keep their shape instead of flattening at 232 Hz.
        static float ClampPitch(float hz)
        {
            const float knee = 200f;
            if (hz > knee) hz = knee + (MaxPitch - knee) * (float)Math.Tanh((hz - knee) / (MaxPitch - knee));
            return hz < MinPitch ? MinPitch : hz;
        }

        static char Lower(string text, int i) => i >= 0 && i < text.Length ? char.ToLowerInvariant(text[i]) : ' ';

        public void Build(string text, GrumbleMood mood, float charsPerSecond)
        {
            Count = 0;
            Extras = 0;
            Duration = 0f;
            Mood = mood;
            Seed = 0u;
            if (string.IsNullOrEmpty(text)) return;
            _seed = SeedOf(text);
            Seed = _seed;
            _v = VoiceOf(mood);
            // Each line its own register, tempo, melody range and articulation - small enough to stay Tilda.
            _lineSt = 1.6f * (Hash01(1, -1) - 0.5f);
            _v.spacing *= 0.93f + 0.14f * Hash01(2, -1);
            _range = 0.85f + 0.35f * Hash01(3, -1);
            _v.neutral = Math.Min(0.85f, Math.Max(0.25f, _v.neutral + 0.12f * (Hash01(4, -1) - 0.5f)));

            float step = 1f / Math.Max(1f, charsPerSecond), t = 0f;
            int n = text.Length;
            int lastContent = -1;
            for (int i = n - 1; i >= 0; i--) if (char.IsLetterOrDigit(text[i])) { lastContent = i; break; }

            int sentenceFrom = 0, sentenceTo = BeginSentence(text, 0), sentenceIndex = 0;
            float lastStart = -10f;
            bool inGroup = false, wordStart = true;
            _word = Word.Plain;
            _humChain = 0;
            for (int i = 0; i < n; i++)
            {
                char c = text[i];
                if (char.IsLetter(c) && (i == 0 || !char.IsLetter(text[i - 1]))) BeginWord(text, i, t, mood);
                bool vowel = IsVowel(c);
                if (vowel && !inGroup)
                {
                    if (_word == Word.Giggle)
                    {
                        // The next vowel gets a grunt of its own rather than stretching the last "hi".
                        if (!_giggled) { Giggle(text, t); _giggled = true; lastStart = -10f; }
                    }
                    else
                    {
                        float pos = sentenceTo > sentenceFrom ? (i - sentenceFrom) / (float)(sentenceTo - sentenceFrom) : 0f;
                        // Interjections and the last syllable of a question keep a grunt of their own ("Ju-hu", "...mit?").
                        bool own = (_word != Word.Plain && _word != Word.Hum)
                                   || ((_kind == Kind.Question) && _wordTo >= sentenceTo && _wordSyllable >= _wordSyllables - 1);
                        if (t - lastStart >= _v.spacing || Count == 0 || own)
                        {
                            Add(text, i, t, pos, wordStart, sentenceIndex, mood);
                            lastStart = t;
                        }
                        else Merge(c, t);
                    }
                    _wordSyllable++;
                    wordStart = false;
                }
                inGroup = vowel;
                if (c == ' ' || c == '-') wordStart = true;

                t += step;
                if (GrumbleTiming.EndsSentence(c) && (i + 1 >= n || !GrumbleTiming.EndsSentence(text[i + 1])))
                {
                    EndSentence(t, i >= lastContent, i, mood);
                    sentenceFrom = i + 1;
                    sentenceTo = BeginSentence(text, i + 1);
                    sentenceIndex++;
                    wordStart = true;
                }
                t += GrumbleTiming.PauseAfter(text, i, lastContent);
            }
            for (int k = 0; k < Count; k++) Duration = Math.Max(Duration, Items[k].start + Items[k].length);
        }

        // Finds the end of the sentence starting at `from` and what kind it is; returns the index of its first mark.
        int BeginSentence(string text, int from)
        {
            int to = text.Length;
            for (int i = from; i < text.Length; i++) if (GrumbleTiming.EndsSentence(text[i])) { to = i; break; }
            char end = to < text.Length ? text[to] : '.';
            char next = to + 1 < text.Length ? text[to + 1] : ' ';
            if (end == '?') _kind = next == '!' ? Kind.Exclaim : FirstWordIsW(text, from, to) ? Kind.WQuestion : Kind.Question;
            else if (end == '!') _kind = Kind.Exclaim;
            else if (end == '…' || (end == '.' && next == '.')) _kind = Kind.Trail;
            else _kind = Kind.Statement;
            _sentenceJoy = _sentenceSad = false;
            _sentenceGrunts = 0;
            return to;
        }

        static bool FirstWordIsW(string text, int from, int to)
        {
            int a = from;
            while (a < to && !char.IsLetter(text[a])) a++;
            int b = a;
            while (b < to && char.IsLetter(text[b])) b++;
            foreach (var w in WWords) if (Same(text, a, b, w)) return true;
            return false;
        }

        // Case-insensitive, and a run of one letter counts once: "Juhuuu" is "juhu", "Hmmm" is "hm", "Oooh" is "oh".
        static bool Same(string text, int from, int to, string word)
        {
            int i = from, k = 0;
            while (i < to && k < word.Length)
            {
                char a = char.ToLowerInvariant(text[i]), b = word[k];
                if (a != b) return false;
                while (i < to && char.ToLowerInvariant(text[i]) == a) i++;
                while (k < word.Length && word[k] == b) k++;
            }
            return i >= to && k >= word.Length;
        }

        static bool AnyOf(string text, int from, int to, string[] words)
        {
            foreach (var w in words) if (Same(text, from, to, w)) return true;
            return false;
        }

        void BeginWord(string text, int from, float t, GrumbleMood mood)
        {
            int to = from;
            int syllables = 0;
            bool inGroup = false, onlyHum = true;
            while (to < text.Length && char.IsLetter(text[to]))
            {
                char c = char.ToLowerInvariant(text[to]);
                bool v = IsVowel(c);
                if (v && !inGroup) syllables++;
                inGroup = v;
                if (c != 'h' && c != 'm' && c != 'n') onlyHum = false;
                to++;
            }
            bool chained = from > 0 && text[from - 1] == '-' && _word == Word.Hum;
            _wordFrom = from;
            _wordTo = to;
            _wordSyllables = syllables;
            _wordSyllable = 0;
            _giggled = false;
            _word = Word.Plain;
            if (syllables == 0)
            {
                if (onlyHum && to - from >= 2)
                {
                    _word = Word.Hum;
                    _humChain = chained ? _humChain + 1 : 0;
                    WordHum(t, to - from, mood);
                }
                return;
            }
            if (mood == GrumbleMood.Sleepy) return;
            if (IsGiggle(text, from, to)) _word = Word.Giggle;
            else if (AnyOf(text, from, to, SurpriseWords)) _word = Word.Surprise;
            else if (AnyOf(text, from, to, JoyWords)) { _word = Word.Joy; _sentenceJoy = true; }
            else if (AnyOf(text, from, to, SighWords)) { _word = Word.Sigh; _sentenceSad = true; }
            else if (AnyOf(text, from, to, HesitateWords)) _word = Word.Hesitate;
        }

        // "hihi", "hehehe", "haha", "hähä": h + vowel, at least twice.
        static bool IsGiggle(string text, int from, int to)
        {
            int len = to - from;
            if (len < 4 || (len & 1) != 0) return false;
            for (int i = from; i < to; i += 2)
                if (char.ToLowerInvariant(text[i]) != 'h' || !IsVowel(text[i + 1])) return false;
            return true;
        }

        // Keeps the grunts in order and apart: nothing starts less than 75 ms after the one before.
        float Place(float t)
        {
            if (Count == 0) return t;
            float earliest = Items[Count - 1].start + 0.075f;
            return t > earliest ? t : earliest;
        }

        void Add(string text, int i, float t, float pos, bool wordStart, int sentence, GrumbleMood mood)
        {
            if (Count >= MaxSyllables) return;
            t = Place(t);
            CloseBefore(t);
            var v = _v;
            char c0 = Lower(text, i), c1 = Lower(text, i + 1);
            bool lastOfWord = _wordSyllable >= _wordSyllables - 1;
            bool endOfWord = i + 1 >= _wordTo || (i + 2 >= _wordTo && !IsVowel(c1));
            // Unstressed "-e", "-en", "-er", "-el": a short, quiet schwa.
            bool schwa = c0 == 'e' && !wordStart && _wordSyllable > 0 && endOfWord;
            Formants(schwa ? ' ' : c0, out float f1, out float f2, out float f3);
            float e1 = f1, e2 = f2, lengthMul = 1f;
            // Diphthongs glide ("ei" a -> i, "au" a -> u, "eu" o -> ü); doubled vowels, "ie" and a lengthening h are long.
            if ((c0 == 'e' || c0 == 'a') && (c1 == 'i' || c1 == 'y')) { Formants('a', out f1, out f2, out f3); Formants('i', out e1, out e2, out _); lengthMul = 1.2f; }
            else if (c0 == 'a' && c1 == 'u') { Formants('a', out f1, out f2, out f3); Formants('u', out e1, out e2, out _); lengthMul = 1.2f; }
            else if ((c0 == 'e' || c0 == 'ä') && c1 == 'u') { Formants('o', out f1, out f2, out f3); Formants('ü', out e1, out e2, out _); lengthMul = 1.2f; }
            else if (c1 == c0 || (c0 == 'i' && c1 == 'e') || (c1 == 'h' && !IsVowel(Lower(text, i + 2)) && i + 2 < _wordTo)) lengthMul = 1.25f;
            const float n1 = 520f, n2 = 1500f, n3 = 2550f;
            f1 += (n1 - f1) * v.neutral; f2 += (n2 - f2) * v.neutral; f3 += (n3 - f3) * v.neutral;
            e1 += (n1 - e1) * v.neutral; e2 += (n2 - e2) * v.neutral;

            // Melody in semitones: the sentence starts a little high and drifts to where the mood takes it; the
            // first syllable of a word is stressed, the kind of sentence bends it, and no two grunts are quite alike.
            // A question does not sink first: its tail has to climb.
            float fall = _kind == Kind.Question ? 0.3f : 1f;
            float st = _lineSt + _range * (1.5f * (1f - pos) + fall * v.rise * pos);
            float stress = 1.1f, gain = v.gain, length = v.length * lengthMul * (0.85f + 0.3f * Hash01(i, 3));
            float glide = mood == GrumbleMood.Sleepy ? -14f : mood == GrumbleMood.Giggly ? 10f : (Hash01(i, 5) - 0.6f) * 12f;
            float breath = 0f, arch = 0f, trill = 0f;
            switch (_kind)
            {
                case Kind.Question:
                    if (pos > 0.65f) st += 9f * (pos - 0.65f) / 0.35f;
                    if (pos > 0.85f) glide = Math.Abs(glide) + 10f;
                    break;
                case Kind.WQuestion:
                    // "Wo bist du?" falls like a statement, from higher up, and only lifts a little at the very end.
                    st += 2.5f * (1f - pos);
                    if (pos > 0.85f) st += 2f * (pos - 0.85f) / 0.15f;
                    break;
                case Kind.Exclaim:
                    st += 1.2f;
                    stress = 1.6f;
                    length *= 0.9f;
                    gain *= 1.08f;
                    if (wordStart) arch = 0.035f * v.pitch;
                    break;
                case Kind.Trail:
                    st -= 1.5f * pos;
                    length *= 1.2f;
                    gain *= 1f - 0.3f * pos;
                    breath = 0.12f * pos;
                    break;
            }
            if (wordStart) st += stress;
            if (schwa) { st -= 0.6f; length *= 0.75f; gain *= 0.8f; }
            // A comma keeps the listener waiting: the word before it lifts.
            if (lastOfWord && _wordTo < text.Length && text[_wordTo] == ',') { st += 1.5f; glide = Math.Abs(glide) + 6f; }
            st += 1.6f * (Hash01(i, sentence) - 0.5f);
            if (mood == GrumbleMood.Giggly) st += (Count & 1) == 0 ? 2.2f : -1.2f;

            switch (_word)
            {
                case Word.Surprise:
                    // "Oh!": jumps up and falls a fifth, drawn out.
                    if (_wordSyllable == 0) { st += 5f; length = Math.Min(0.45f, length * 2f); breath += 0.1f; }
                    break;
                case Word.Joy:
                    if (lastOfWord) { st += 2f; length = Math.Min(0.4f, length * 1.35f); trill = 1f; arch = 0.06f * v.pitch; }
                    else st += 0.5f;
                    break;
                case Word.Sigh:
                    st -= 1f; length = Math.Min(0.45f, length * 1.3f); breath += 0.3f; gain *= 0.9f;
                    break;
                case Word.Hesitate:
                    st -= 1f; length = Math.Min(0.4f, length * 2f); gain *= 0.75f; breath += 0.05f; glide = 0f;
                    f1 = e1 = n1; f2 = e2 = n2;
                    break;
            }

            var s = new GrumbleSyllable
            {
                start = t, length = length,
                pitch = ClampPitch(v.pitch * Semitones(st)),
                f1 = f1, f2 = f2, f3 = f3, f1End = e1, f2End = e2,
                gain = gain * (wordStart ? 1f : 0.86f),
                breath = breath, arch = arch, trill = trill,
            };
            s.glide = _word == Word.Surprise && _wordSyllable == 0 ? -0.3f * s.pitch : glide;
            if (_word == Word.Sigh) s.glide = -0.12f * s.pitch;
            Onset(text, i, v.tick, ref s);
            Items[Count++] = s;
            _sentenceGrunts++;
        }

        // Two vowels closer together than she can mumble: the earlier grunt is drawn out and slides to the second.
        void Merge(char vowel, float t)
        {
            if (Count == 0) return;
            ref var s = ref Items[Count - 1];
            if (s.hum || s.extra) return;
            Formants(vowel, out float f1, out float f2, out _);
            s.f1End = f1 + (520f - f1) * _v.neutral;
            s.f2End = f2 + (1500f - f2) * _v.neutral;
            s.length = Math.Min(_v.length * 1.7f, Math.Max(s.length, t - s.start + _v.length * 0.55f));
        }

        // A grunt ends a moment before the next one starts, so the rhythm of the sentence stays audible.
        void CloseBefore(float t)
        {
            if (Count == 0) return;
            ref var s = ref Items[Count - 1];
            float room = t - s.start - 0.028f;
            if (s.length > room) s.length = Math.Max(0.045f, room);
        }

        // The consonant before the vowel: a burst, a hiss or a murmur, and where it leaves the formants.
        static void Onset(string text, int i, float scale, ref GrumbleSyllable s)
        {
            char b1 = Lower(text, i - 1), b2 = Lower(text, i - 2), b3 = Lower(text, i - 3);
            bool wordInitial = !char.IsLetter(b2);
            if (b1 == 'h' && b2 == 'c' && b3 == 's') { Noise(ref s, 0.36f, 2700f, 0.05f, scale); Locus(ref s, 300f, 1650f, 0.04f); return; }
            if (b1 == 'h' && b2 == 'c')
            {
                // "ich" hisses high, "ach" rasps low.
                char before = Lower(text, i - 3);
                bool front = before == 'e' || before == 'i' || before == 'ä' || before == 'ö' || before == 'ü' || !char.IsLetter(before);
                if (front) Noise(ref s, 0.3f, 3600f, 0.04f, scale);
                else { Noise(ref s, 0.28f, 1300f, 0.045f, scale); s.breath += 0.08f; }
                Locus(ref s, 300f, front ? 1900f : 1200f, 0.04f);
                return;
            }
            switch (b1)
            {
                case 'p': Noise(ref s, 0.5f, 1100f, 0.008f, scale); Locus(ref s, 260f, 850f, 0.04f); break;
                case 'b': Noise(ref s, 0.22f, 900f, 0.005f, scale); Locus(ref s, 260f, 850f, 0.035f); break;
                case 't': Noise(ref s, 0.5f, 3600f, 0.01f, scale); Locus(ref s, 280f, 1750f, 0.035f); break;
                case 'd': Noise(ref s, 0.22f, 3300f, 0.006f, scale); Locus(ref s, 280f, 1750f, 0.035f); break;
                case 'k': case 'c': case 'q': case 'g':
                    Noise(ref s, b1 == 'g' ? 0.22f : 0.5f, 2000f, b1 == 'g' ? 0.007f : 0.012f, scale);
                    // The velar pinch sits just above a front vowel's F2 and low for a back vowel.
                    Locus(ref s, 280f, s.f2 > 1500f ? Math.Min(2300f, s.f2 * 1.12f) : 1200f, 0.04f);
                    break;
                case 'z': Noise(ref s, 0.45f, 4200f, 0.03f, scale); Locus(ref s, 300f, 1700f, 0.035f); break;
                case 's': case 'ß': case 'x':
                    // A German s before a vowel at the start of a word is voiced and softer.
                    if (wordInitial && b1 == 's') Noise(ref s, 0.22f, 4600f, 0.03f, scale);
                    else Noise(ref s, 0.32f, 5000f, 0.035f, scale);
                    Locus(ref s, 300f, 1700f, 0.035f);
                    break;
                case 'f': case 'v': Noise(ref s, 0.2f, 3000f, 0.03f, scale); Locus(ref s, 280f, 900f, 0.04f); break;
                case 'w': Noise(ref s, 0.1f, 2800f, 0.02f, scale); Locus(ref s, 280f, 850f, 0.05f); break;
                case 'h': Noise(ref s, 0.2f, 1500f, 0.03f, scale); s.breath += 0.15f; break;
                case 'j': Locus(ref s, 280f, 2250f, 0.055f); break;
                case 'l': Locus(ref s, 350f, 1150f, 0.04f); break;
                case 'r': Noise(ref s, 0.07f, 1100f, 0.02f, scale); Locus(ref s, 450f, 1250f, 0.035f); break;
                case 'm': s.nasal = 0.045f; Locus(ref s, 270f, 1000f, 0.03f); break;
                case 'n': s.nasal = 0.04f; Locus(ref s, 270f, 1600f, 0.03f); break;
                default:
                    // A vowel at the start of a word: the soft German glottal onset.
                    if (char.IsLetter(b1)) Noise(ref s, 0.1f, 1800f, 0.007f, scale);
                    else Noise(ref s, 0.08f, 900f, 0.004f, scale);
                    break;
            }
        }

        static void Noise(ref GrumbleSyllable s, float strength, float hz, float seconds, float scale)
        {
            s.tick = strength * scale;
            s.tickHz = hz;
            s.tickSeconds = seconds;
        }

        static void Locus(ref GrumbleSyllable s, float f1, float f2, float seconds)
        {
            s.f1On = f1;
            s.f2On = f2;
            s.onset = seconds;
        }

        GrumbleSyllable Extra(float start, float length, float st, float glide, float gain, bool hum)
        {
            var s = new GrumbleSyllable
            {
                start = start, length = length, pitch = ClampPitch(_v.pitch * Semitones(_lineSt + st)), glide = glide,
                f1 = hum ? 270f : 450f, f2 = hum ? 1050f : 1850f, f3 = 2300f, gain = _v.gain * gain, hum = hum, extra = true,
            };
            s.f1End = s.f1;
            s.f2End = s.f2;
            return s;
        }

        void Push(GrumbleSyllable s)
        {
            if (Count >= MaxSyllables) return;
            s.start = Place(s.start);
            CloseBefore(s.start);
            Items[Count++] = s;
            Extras++;
        }

        // A word without a vowel ("Hmm", "Mh-hm"): short and low, rising when it answers the one before, long and
        // thoughtful on its own.
        void WordHum(float t, int letters, GrumbleMood mood)
        {
            if (mood == GrumbleMood.Sleepy || mood == GrumbleMood.Soft) { Push(Extra(t, 0.36f, -1f, -16f, 0.8f, true)); return; }
            if (_humChain > 0) Push(Extra(t, 0.17f, 3.5f, 8f, 0.8f, true));
            else if (letters >= 3)
            {
                var s = Extra(t, 0.34f, 0.5f, 0f, 0.8f, true);
                s.arch = 0.05f * s.pitch;
                s.glide = -0.06f * s.pitch;
                Push(s);
            }
            else Push(Extra(t, 0.13f, -1f, 0f, 0.8f, true));
        }

        // "Hihi": quick, airy little pulses stepping down, whatever the typewriter does.
        void Giggle(string text, float t)
        {
            int pulses = Math.Min(5, Math.Max(2, _wordSyllables));
            float at = t;
            for (int k = 0; k < pulses && Count < MaxSyllables; k++)
            {
                at = Place(k == 0 ? t : at + 0.085f);
                CloseBefore(at);
                char vowel = Lower(text, _wordFrom + 1 + 2 * k);
                Formants(vowel, out float f1, out float f2, out float f3);
                f1 += (520f - f1) * 0.3f; f2 += (1500f - f2) * 0.3f;
                var s = new GrumbleSyllable
                {
                    start = at, length = 0.062f, pitch = ClampPitch(_v.pitch * Semitones(_lineSt + 4f - 1.1f * k)), glide = -12f,
                    f1 = f1, f2 = f2, f3 = f3, f1End = f1, f2End = f2, gain = _v.gain * 0.8f, breath = 0.45f,
                };
                Noise(ref s, 0.2f, 1500f, 0.02f, _v.tick);
                Items[Count++] = s;
                _sentenceGrunts++;
            }
        }

        // What she adds in the breath after a sentence: sad and sleepy moods sigh, "..." ends in a thoughtful hmm, a
        // question sometimes in a curious "hm?", an exclamation in a giggle or a bright "mh-hm!", a statement in the
        // happy little "mh-hm" or a content "hm".
        void EndSentence(float t, bool final, int i, GrumbleMood mood)
        {
            if (_sentenceGrunts == 0 || Count == 0 || Items[Count - 1].extra) return;
            float r = Hash01(i, 7), r2 = Hash01(i, 11);
            // After the last grunt has finished (a drawn-out "Oh!" keeps its length); no room left, no extra.
            ref var last = ref Items[Count - 1];
            float at = Math.Max(t + 0.04f, last.start + last.length + 0.03f);
            if (!final && at > t + 0.12f) return;
            if (mood == GrumbleMood.Soft || mood == GrumbleMood.Sleepy || (_sentenceSad && !_sentenceJoy))
            {
                if (final || r > 0.62f) Push(Extra(at, final ? 0.42f : 0.24f, -1f, -16f, 0.8f, true));
                return;
            }
            switch (_kind)
            {
                case Kind.Trail:
                {
                    var s = Extra(at, final ? 0.38f : 0.26f, 0f, 0f, 0.75f, true);
                    s.arch = 0.04f * s.pitch;
                    s.glide = -0.07f * s.pitch;
                    Push(s);
                    return;
                }
                case Kind.Question:
                case Kind.WQuestion:
                    if (r > (final ? 0.4f : 0.65f)) Push(Extra(at, 0.16f, 1f, 18f, 0.75f, true));
                    return;
                case Kind.Exclaim:
                    if (!final && r <= 0.5f) return;
                    if (_sentenceJoy || mood == GrumbleMood.Giggly || r2 > 0.6f)
                    {
                        int pulses = mood == GrumbleMood.Giggly ? 3 : 2;
                        for (int k = 0; k < pulses; k++)
                        {
                            var s = Extra(at + 0.085f * k, 0.06f, Math.Max(0f, _v.rise) + 4.5f - k, -10f, 0.7f, false);
                            s.breath = 0.45f;
                            Push(s);
                        }
                    }
                    else
                    {
                        Push(Extra(at, 0.1f, 0.8f, 0f, 0.8f, true));
                        Push(Extra(at + 0.125f, final ? 0.2f : 0.14f, 6.8f, 10f, 0.8f, true));
                    }
                    return;
                default:
                    if (!final && r <= 0.62f) return;
                    if (r2 > 0.72f && mood != GrumbleMood.Giggly)
                    {
                        // A content little "hm", falling.
                        Push(Extra(at, final ? 0.2f : 0.15f, 0.5f, -10f, 0.8f, true));
                        return;
                    }
                    int parts = mood == GrumbleMood.Giggly ? 3 : 2;
                    float x = at;
                    for (int k = 0; k < parts; k++)
                    {
                        float length = k == parts - 1 ? (final ? 0.2f : 0.14f) : 0.1f;
                        float st = parts == 3 ? 1f + 2f * k : k == 0 ? -1f : 3.5f;
                        Push(Extra(x, length, st, k == parts - 1 ? 8f : 0f, 0.8f, true));
                        x += length + 0.025f;
                    }
                    return;
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

        static float Smooth01(float x) => x <= 0f ? 0f : x >= 1f ? 1f : x * x * (3f - 2f * x);

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

            float u = 0f, env = 0f, target = _f0, closed = _s.hum ? 1f : 0f;
            if (_inSyllable)
            {
                float el = _time - _s.start;
                u = el / Math.Max(0.02f, _s.length);
                if (u >= 1f) { _inSyllable = false; u = 1f; }
                // Quick to open, slower to close: a grunt, not a beep.
                env = SynthMath.Sine(0.5f * (float)Math.Pow(u < 0f ? 0f : u, 0.62));
                target = _s.pitch + _s.glide * u + _s.arch * SynthMath.Sine(0.5f * u);
                // m / n: the lips (or the tongue) stay shut for a moment, then the vowel opens.
                if (!_s.hum && _s.nasal > 0f) closed = 1f - Smooth01((el - _s.nasal) / 0.025f);
                float open = (el - (_s.hum ? 0f : _s.nasal)) / 0.035f;
                open = open < 0f ? 0f : open > 1f ? 1f : open;
                float f1 = (_s.f1 + (_s.f1End - _s.f1) * u) * (0.62f + 0.38f * open);
                float f2 = _s.f2 + (_s.f2End - _s.f2) * u;
                // The consonant's locus: the formants start where the mouth was and move to the vowel.
                if (_s.onset > 0f && _s.f2On > 0f)
                {
                    float on = Smooth01((el - _s.nasal) / _s.onset);
                    f1 = _s.f1On + (f1 - _s.f1On) * on;
                    f2 = _s.f2On + (f2 - _s.f2On) * on;
                }
                if (closed > 0f)
                {
                    f1 += (270f - f1) * closed;
                    f2 += ((_s.f2On > 0f ? _s.f2On : 1050f) - f2) * closed;
                }
                _c1 = SynthMath.SvfCoef(f1, _sr);
                _c2 = SynthMath.SvfCoef(f2, _sr);
                _c3 = SynthMath.SvfCoef(_s.f3, _sr);
            }

            _clock += n * _invSr;
            if (_clock > 600f) _clock -= 600f;
            float trill = _inSyllable ? _s.trill : 0f;
            float vibrato = 1f + 0.011f * SynthMath.Sine(_clock * 5.3f) + 0.006f * SynthMath.Sine(_clock * 2.1f + 0.3f)
                            + 0.03f * trill * SynthMath.Sine(_clock * 8.5f);
            float tremor = 1f + 0.05f * SynthMath.Sine(_clock * 6.1f + 0.5f);
            float gain = _inSyllable ? _s.gain * env * tremor * (_s.hum ? 1f : 1f - 0.3f * closed) : 0f;
            float g2 = 0.85f + (0.12f - 0.85f) * closed, g3 = 0.3f + (0.03f - 0.3f) * closed, body = 0.5f + (0.85f - 0.5f) * closed;
            float breath = _inSyllable ? _s.breath : 0f;
            float voiced = 1f - 0.4f * breath, aspir = 0.5f * breath;
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

                float white = _noise.Next();
                // Falling saw with its step rounded off (polyBLEP), then tilted: a soft glottal pulse train.
                float saw = 1f - 2f * _phase;
                if (_phase < inc) { float x = _phase / inc; saw -= -(x * x) + 2f * x - 1f; }
                else if (_phase > 1f - inc) { float x = (_phase - 1f) / inc; saw -= x * x + 2f * x + 1f; }
                _srcLp += (saw - _srcLp) * _srcCoef;
                // Breath: the same formants, excited by air instead of the vocal folds.
                float src = _srcLp * voiced + white * aspir;

                float h1 = src - _l1 - q1 * _b1; _b1 += _c1 * h1; _l1 += _c1 * _b1;
                float h2 = src - _l2 - q2 * _b2; _b2 += _c2 * h2; _l2 += _c2 * _b2;
                float h3 = src - _l3 - q3 * _b3; _b3 += _c3 * h3; _l3 += _c3 * _b3;
                _bodyLp += (src - _bodyLp) * _bodyCoef;

                float sub = SynthMath.Sine(_subPhase);
                float voice = (_b1 * q1 * 2.2f + _b2 * q2 * 2.2f * g2 + _b3 * q3 * 2.2f * g3 + _bodyLp * body) * (1f - 0.08f * (0.5f + 0.5f * sub));

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
