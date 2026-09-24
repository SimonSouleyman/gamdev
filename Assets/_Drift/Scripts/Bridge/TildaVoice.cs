using System;
using System.Collections.Generic;
using Drift.Audio;
using UnityEngine;

namespace Drift.Bridge
{
    public enum VoicePriority
    {
        // Only when Tilda is silent and nothing waits; otherwise the remark is dropped.
        IfSilent,
        // After what she is saying now (at most MaxQueued lines wait, no duplicates).
        Queue,
        // Fades the current line out over FadeSeconds and starts at once; waiting lines are dropped.
        Interrupt,
    }

    // Which remark is up when, as plain state: TildaVoice tells it whether the current one is still showing and
    // starts what Tick returns.
    public sealed class TildaVoiceQueue
    {
        public const float FadeSeconds = 0.15f;
        public const int MaxQueued = 2;

        readonly List<string> _waiting = new List<string>();
        string _next;
        float _fade = 1f;

        public string Current { get; private set; }
        public bool Fading { get; private set; }
        public float Gain => Fading ? Mathf.Clamp01(_fade) : 1f;
        public int Waiting => _waiting.Count + (_next != null ? 1 : 0);
        public bool Busy => Current != null || _next != null || _waiting.Count > 0;

        public bool Request(string key, VoicePriority priority)
        {
            if (string.IsNullOrEmpty(key)) return false;
            switch (priority)
            {
                case VoicePriority.Interrupt:
                    if (key == Current && !Fading) return false;
                    _waiting.Clear();
                    _next = key;
                    if (Current != null && !Fading) BeginFade();
                    return true;
                case VoicePriority.Queue:
                    if ((key == Current && !Fading) || key == _next || _waiting.Contains(key)) return false;
                    if (Current == null && _next == null) _next = key;
                    else if (_waiting.Count < MaxQueued) _waiting.Add(key);
                    else return false;
                    return true;
                default:
                    if (Busy) return false;
                    _next = key;
                    return true;
            }
        }

        // prefixes: only lines whose key starts with one of them (a menu silences its own lines, not another's).
        public void Stop(bool fade, params string[] prefixes)
        {
            _waiting.RemoveAll(k => Matches(k, prefixes));
            if (Matches(_next, prefixes)) _next = null;
            if (Current == null || !Matches(Current, prefixes)) return;
            if (fade) { if (!Fading) BeginFade(); }
            else End();
        }

        static bool Matches(string key, string[] prefixes)
        {
            if (key == null) return false;
            if (prefixes == null || prefixes.Length == 0) return true;
            foreach (var p in prefixes) if (!string.IsNullOrEmpty(p) && key.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }

        // Nobody could show the line: drop it so the next one gets its turn.
        public void Fail() => End();

        // Returns the key to start now, or null. showing: Current is still on screen.
        public string Tick(float dt, bool showing)
        {
            if (Current != null)
            {
                if (Fading)
                {
                    _fade -= Mathf.Max(0f, dt) / FadeSeconds;
                    if (_fade <= 0f || !showing) End();
                }
                else if (!showing) End();
            }
            if (Current != null) return null;
            if (_next == null && _waiting.Count > 0)
            {
                _next = _waiting[0];
                _waiting.RemoveAt(0);
            }
            if (_next == null) return null;
            Current = _next;
            _next = null;
            return Current;
        }

        void BeginFade()
        {
            Fading = true;
            _fade = 1f;
        }

        void End()
        {
            Current = null;
            Fading = false;
            _fade = 1f;
        }
    }

    // A remark that is about to start. Whoever has Tilda on screen puts it into her speech bubble and sets `shown`;
    // a line nobody shows is dropped.
    public sealed class TildaLine
    {
        public string key, text;
        public GrumbleMood mood;
        public float seconds;
        public bool shown;
    }

    // Tilda does not speak: she grumbles. A procedural synth (Audio.TildaGrumbleSynth, no audio assets) mumbles along
    // with whatever text her speech bubble types, syllable by syllable, and its loudness moves her mouth. Two ways in:
    // Say(key) queues one of her short remarks (TildaVoiceLines) and raises LineStarted so a bubble shows it, and
    // Murmur(text) is what a bubble calls for the page it starts typing. "Tildas Grummeln: aus" (PlayerPrefs
    // drift_tilda_voice) silences the synth; the bubbles still appear.
    [DisallowMultipleComponent]
    public sealed class TildaVoice : MonoBehaviour
    {
        public const string PrefKey = "drift_tilda_voice";
        const string OutputName = "TildaGrumble";
        const float WatchdogSeconds = 1.5f;

        [Range(0f, 1f)] public float volume = 0.8f;

        public static event Action<TildaLine> LineStarted;
        public static event Action<string> LineEnded;

        static TildaVoice s_instance;
        static bool s_enabled = true, s_prefRead;
        static readonly TildaLine s_line = new TildaLine();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnPlaySessionStart()
        {
            s_prefRead = false;
            s_instance = null;
        }

        readonly TildaVoiceQueue _queue = new TildaVoiceQueue();
        TildaGrumbleSynth _synth;
        SynthAudioOutput _output;
        float _lineEnd, _sinceMurmur = -1f;
        string _shownKey;

        public static bool Enabled
        {
            get
            {
                if (!s_prefRead)
                {
                    s_prefRead = true;
                    s_enabled = PlayerPrefs.GetInt(PrefKey, 1) != 0;
                }
                return s_enabled;
            }
            set
            {
                bool was = Enabled;
                s_enabled = value;
                if (was == value) return;
                if (Application.isPlaying)
                {
                    PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
                    PlayerPrefs.Save();
                }
                if (s_instance != null && s_instance._synth != null)
                {
                    s_instance._synth.Enabled = value;
                    if (!value) s_instance._synth.Stop();
                }
            }
        }

        public static string ToggleLabel => Enabled ? "Tildas Grummeln: an" : "Tildas Grummeln: aus";

        // Flips the setting; switched back on she answers with a happy little grumble.
        public static void Toggle()
        {
            Enabled = !Enabled;
            if (Enabled) Say(TildaVoiceLines.VoiceOn, VoicePriority.Interrupt);
        }

        // One of her remarks is up (whether or not the grumble is switched on).
        public static bool Speaking => s_instance != null && s_instance._queue.Current != null && !s_instance._queue.Fading;
        public static string CurrentKey => s_instance != null ? s_instance._queue.Current : null;
        // The synth is making sound right now, and how loud (0..1): her mouth.
        public static bool Grumbling => s_instance != null && s_instance._synth != null && Enabled && s_instance._synth.Sounding;
        public static float Level => Grumbling ? s_instance._synth.Level : 0f;
        public static TildaVoiceQueue Queue => s_instance != null ? s_instance._queue : null;
        public static TildaGrumbleSynth Synth => s_instance != null ? s_instance._synth : null;

        static TildaVoice Instance
        {
            get
            {
                if (s_instance != null || !Application.isPlaying) return s_instance;
                s_instance = FindAnyObjectByType<TildaVoice>();
                if (s_instance != null) return s_instance;
                var host = FindAnyObjectByType<SessionScreens>();
                var go = new GameObject("TildaVoice") { hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild };
                if (host != null) go.transform.SetParent(host.transform, false);
                s_instance = go.AddComponent<TildaVoice>();
                return s_instance;
            }
        }

        // Queues one of her remarks; true when it was accepted. The bubble and the grumble follow with LineStarted.
        public static bool Say(string key, VoicePriority priority = VoicePriority.Interrupt)
        {
            if (!Application.isPlaying || TildaVoiceLines.TextOf(key) == null) return false;
            var voice = Instance;
            return voice != null && voice._queue.Request(key, priority);
        }

        // Ends what she is saying; with prefixes only lines whose key starts with one of them.
        public static void Hush(params string[] prefixes)
        {
            if (s_instance != null) s_instance._queue.Stop(true, prefixes);
        }

        // How long the remark stays up: typing plus reading time of every page. 0 for an unknown key.
        public static float LengthOf(string key)
        {
            string text = TildaVoiceLines.TextOf(key);
            return text == null ? 0f : TildaBubble.SecondsFor(text);
        }

        // Grumbles along with a text that a bubble starts typing now at charsPerSecond (plain text, no rich-text
        // tags). Returns the seconds of grumbling, 0 when switched off or outside Play Mode.
        public static float Murmur(string plainText, GrumbleMood mood, float charsPerSecond)
        {
            if (!Application.isPlaying || !Enabled) return 0f;
            var voice = Instance;
            if (voice == null) return 0f;
            voice.EnsureOutput();
            var plan = voice._synth.Speak(plainText, mood, charsPerSecond);
            voice._sinceMurmur = 0f;
            return plan != null ? plan.Duration : 0f;
        }

        public static void Quiet()
        {
            if (s_instance != null && s_instance._synth != null) s_instance._synth.Stop();
        }

        void OnEnable()
        {
            if (s_instance == null) s_instance = this;
        }

        void OnDisable()
        {
            _queue.Stop(false);
            EndShown();
            if (_synth != null) _synth.Stop();
            AudioDirector.VoiceDuck = 0f;
            if (s_instance == this) s_instance = null;
        }

        // Same mechanism as AudioDirector's Music / Sfx / Life children: a DontSave child with an AudioSource whose
        // SynthAudioOutput pulls the synth, as a filter or, where filters do not run, as a streamed clip.
        void EnsureOutput()
        {
            if (_output != null) return;
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            if (_synth == null) _synth = new TildaGrumbleSynth(rate) { Enabled = Enabled };
            var t = transform.Find(OutputName);
            GameObject go;
            if (t != null) go = t.gameObject;
            else
            {
                go = new GameObject(OutputName) { hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild };
                go.transform.SetParent(transform, false);
            }
            var source = go.GetComponent<AudioSource>();
            if (source == null) source = go.AddComponent<AudioSource>();
            _output = go.GetComponent<SynthAudioOutput>();
            if (_output == null) _output = go.AddComponent<SynthAudioOutput>();
            var director = AudioDirector.Instance;
            _output.Begin(_synth, director != null && (director.useClipStreaming || director.FilterFallbackActive));
            // She also grumbles in the pause menu.
            source.ignoreListenerPause = true;
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            bool showing = _queue.Current != null && Time.unscaledTime < _lineEnd;
            string start = _queue.Tick(Time.unscaledDeltaTime, showing);
            if (_queue.Current == null || _queue.Current != _shownKey) EndShown();
            if (start != null) Begin(start);

            // After a script reload in Play Mode the output component survives but the synth (not serialized) is gone.
            if (_output != null && _synth != null)
            {
                _output.Gain = volume;
                _synth.Volume = _queue.Gain;
                if (_sinceMurmur >= 0f && !_output.ClipMode)
                {
                    _sinceMurmur += Time.unscaledDeltaTime;
                    if (_sinceMurmur > WatchdogSeconds)
                    {
                        if (_output.FilterCalls == 0) _output.SwitchToClipStreaming();
                        _sinceMurmur = -1f;
                    }
                }
            }
            AudioDirector.VoiceDuck = Grumbling ? 1f : 0f;
        }

        void Begin(string key)
        {
            s_line.key = key;
            s_line.text = TildaVoiceLines.TextOf(key);
            s_line.mood = TildaVoiceLines.MoodOf(key);
            s_line.seconds = LengthOf(key);
            s_line.shown = false;
            if (s_line.text != null) LineStarted?.Invoke(s_line);
            if (!s_line.shown)
            {
                _queue.Fail();
                return;
            }
            _shownKey = key;
            _lineEnd = Time.unscaledTime + s_line.seconds;
        }

        void EndShown()
        {
            if (_shownKey == null) return;
            string key = _shownKey;
            _shownKey = null;
            LineEnded?.Invoke(key);
        }
    }
}
