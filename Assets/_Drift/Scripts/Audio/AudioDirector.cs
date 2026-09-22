using Drift.Core;
using Drift.Islands;
using UnityEngine;

namespace Drift.Audio
{
    public sealed class AudioDirector : MonoBehaviour
    {
        public static AudioDirector Instance { get; private set; }

        [Range(0f, 1f)] public float musicVolume = 0.6f;
        [Range(0f, 1f)] public float sfxVolume = 0.8f;
        [Range(0f, 1f)] public float lifeVolume = 0.5f;
        public bool musicEnabled = true;
        public bool sfxEnabled = true;
        public bool lifeEnabled = true;
        public float lifeUpdateInterval = 0.5f;
        public float lifeRange = 40f;
        public float lifeDuckDepth = 0.5f;
        public float lifeDuckTension = 0.7f;
        public float lifeDuckImpactSeconds = 3f;
        public float impactHearingRange = 70f;
        public float oneShotHearingRange = 45f;
        [Tooltip("Bis zu dieser Entfernung (Einheiten) ist Donner zu hören.")]
        public float thunderRange = 260f;
        [Tooltip("So viele Einheiten legt der Schall pro Sekunde zurück: ferne Blitze donnern später.")]
        public float thunderSpeed = 90f;
        public bool grindFromContact = true;
        public float grindPollInterval = 0.1f;
        // New name on purpose: the 0.35 that scenes serialised for the spoken voice must not survive; a grumble ducks gently.
        [Range(0f, 1f)] public float grumbleDuckDepth = 0.15f;
        public float voiceDuckSeconds = 0.25f;
        public bool useClipStreaming = false;
        public float filterWatchdogSeconds = 2f;
        public float sinkWarningBuoyancy = 0.35f;
        public float windBase = 0.25f;
        public float windFromSpeed = 0.5f;
        public float windLfoPeriod = 23f;
        public float windLfoDepth = 0.15f;
        public float tensionHorizonSeconds = 8f;
        public float tensionOverride = -1f;

        [Header("Tempo und Strömung")]
        [Tooltip("Hörbare Rückmeldung für Tempo, Strömung und Surfen. Aus = nur Wind und Wasser wie bisher.")]
        public bool speedFeelEnabled = true;
        [Tooltip("Lautstärke des Rauschens, das anschwillt, während du mit der Strömung fährst.")]
        [Range(0f, 1f)] public float flowVolume = 0.5f;
        [Tooltip("Lautstärke des steigenden Tons, solange du eine Plattengrenze surfst.")]
        [Range(0f, 1f)] public float surfVolume = 0.45f;
        [Tooltip("Wie viel lauter das Wasserrauschen bei vollem Tempo wird.")]
        [Range(0f, 1f)] public float speedWaterBoost = 0.35f;
        [Tooltip("Zusätzlicher Platscher beim Verschmelzen (die Gischt). 0 = aus.")]
        [Range(0f, 1f)] public float mergeSplash = 0.7f;

        public MusicSynth Music { get; private set; }
        public SfxSynth Sfx { get; private set; }
        public LifeSynth Life { get; private set; }
        public LifeSoundScout Scout { get; private set; }
        public TensionTracker Tracker { get; private set; }
        public float LifeDuck { get; private set; }
        // 0..1, set while Tilda grumbles (Bridge.TildaVoice): music, sfx and life drop by grumbleDuckDepth.
        public static float VoiceDuck { get; set; }
        public float VoiceDuckGain => 1f - grumbleDuckDepth * _voiceDuck;
        public float Tension => Tracker != null ? Tracker.Value : 0f;
        public float PlayerSpeedNormalized { get; private set; }
        public bool FilterFallbackActive { get; private set; }
        public Island Player => _player;
        public float SpeedDrive => _feel.Drive;
        public float FlowAmount => _feel.Flow;
        public float SurfAmount => _feel.Surf;

        SynthAudioOutput _musicOut, _sfxOut, _lifeOut;
        Island _player;
        FlockSystem _flocks;
        float _lfoT, _playTime, _lifeTimer, _flockSearchTimer, _impactDuck, _voiceDuck;
        bool _subscribed;
        IslandWorld _world;
        float _grindTimer, _worldSearchTimer, _grindOverride, _heardIntensity;
        readonly SpeedFeel.Tracker _feel = new();

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            Instance = this;
            EnsureSynths();
            _musicOut = GetOrCreateOutput("Music", Music);
            _sfxOut = GetOrCreateOutput("Sfx", Sfx);
            _lifeOut = GetOrCreateOutput("Life", Life);
            if (!_subscribed)
            {
                Island.Impact += OnImpact;
                Island.Merged += OnMerged;
                Island.Bumped += OnBumped;
                LifeEnvironment.LightningStruck += OnLightning;
                _subscribed = true;
            }
        }

        void OnDisable()
        {
            if (_subscribed)
            {
                Island.Impact -= OnImpact;
                Island.Merged -= OnMerged;
                Island.Bumped -= OnBumped;
                LifeEnvironment.LightningStruck -= OnLightning;
                _subscribed = false;
            }
            if (Instance == this) Instance = null;
        }

        void EnsureSynths()
        {
            int sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            if (Music == null) Music = new MusicSynth(sr);
            if (Sfx == null) Sfx = new SfxSynth(sr);
            if (Life == null) Life = new LifeSynth(sr);
            if (Scout == null) Scout = new LifeSoundScout();
            if (Tracker == null) Tracker = new TensionTracker();
            Tracker.HorizonSeconds = tensionHorizonSeconds;
        }

        SynthAudioOutput GetOrCreateOutput(string childName, ISynthSource source)
        {
            var t = transform.Find(childName);
            GameObject go;
            if (t != null) go = t.gameObject;
            else
            {
                go = new GameObject(childName);
                go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                go.transform.SetParent(transform, false);
            }
            if (go.GetComponent<AudioSource>() == null) go.AddComponent<AudioSource>();
            var output = go.GetComponent<SynthAudioOutput>();
            if (output == null) output = go.AddComponent<SynthAudioOutput>();
            output.Begin(source, useClipStreaming);
            return output;
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            if (Music == null || _musicOut == null) OnEnable();
            float dt = Time.deltaTime;
            _playTime += dt;
            _lfoT += dt;

            var player = FindPlayer();
            float raw = tensionOverride >= 0f ? Mathf.Clamp01(tensionOverride)
                : (player != null ? Tracker.EvaluateRaw(player, Island.All) : 0f);
            Music.Tension = Tracker.Update(raw, dt);

            float speedN = 0f;
            float warn = 0f;
            if (player != null && !player.IsSunk && !Island.InputLocked)
            {
                speedN = Mathf.Clamp01(player.PlanarVelocity.magnitude / Mathf.Max(0.1f, player.MaxSpeed));
                float b = player.Buoyancy;
                if (b < sinkWarningBuoyancy)
                    warn = Mathf.Clamp01(0.3f + 0.7f * (sinkWarningBuoyancy - b) / sinkWarningBuoyancy);
            }
            PlayerSpeedNormalized = speedN;
            UpdateSpeedFeel(player, dt);
            Sfx.WaterAmount = Mathf.Clamp01(speedN * (1f + (speedFeelEnabled ? speedWaterBoost * _feel.Drive : 0f)));
            Sfx.WindAmount = Mathf.Clamp01(windBase + windFromSpeed * speedN
                + windLfoDepth * Mathf.Sin(_lfoT * (2f * Mathf.PI / Mathf.Max(1f, windLfoPeriod))));
            Sfx.SinkWarning = warn;

            UpdateGrind(player, dt);
            UpdateLife(player, speedN, dt);

            // Unscaled: she also grumbles in the pause menu, where deltaTime is 0.
            _voiceDuck = Mathf.MoveTowards(_voiceDuck, Mathf.Clamp01(VoiceDuck), Time.unscaledDeltaTime / Mathf.Max(0.01f, voiceDuckSeconds));
            float voiceGain = VoiceDuckGain;
            _musicOut.Gain = musicEnabled ? musicVolume * voiceGain : 0f;
            _sfxOut.Gain = sfxEnabled ? sfxVolume * voiceGain : 0f;
            _lifeOut.Gain = lifeEnabled ? lifeVolume * voiceGain : 0f;

            if (!FilterFallbackActive && !useClipStreaming && _playTime > filterWatchdogSeconds && _musicOut.FilterCalls == 0)
            {
                FilterFallbackActive = true;
                _musicOut.SwitchToClipStreaming();
                _sfxOut.SwitchToClipStreaming();
                _lifeOut.SwitchToClipStreaming();
                Debug.LogWarning("AudioDirector: OnAudioFilterRead never ran on a clip-less AudioSource; switched to streamed AudioClip output.");
            }
        }

        // Riding the current swells a band of rushing water, surfing a plate boundary sings a rising tone that
        // stops the moment the boundary is lost - the two things you cannot see from the chase camera.
        void UpdateSpeedFeel(Island player, float dt)
        {
            _feel.Step(Island.InputLocked ? null : player, dt);
            if (Sfx == null) return;
            bool on = speedFeelEnabled && !Island.InputLocked;
            // The amount drives the pitch/brightness, the gain only the loudness: the sliders must not move the tone.
            Sfx.FlowAmount = on ? _feel.Flow : 0f;
            Sfx.SurfAmount = on ? _feel.Surf : 0f;
            Sfx.FlowGain = SfxSynth.FlowGainFull * flowVolume;
            Sfx.SurfGain = SfxSynth.SurfGainFull * surfVolume;
        }

        // Densities are re-read from the scene every lifeUpdateInterval; the duck follows tension and impacts
        // every frame (a float write, the synth smooths it on its own thread).
        void UpdateLife(Island player, float speedN, float dt)
        {
            if (_impactDuck > 0f)
            {
                _impactDuck -= dt / Mathf.Max(0.1f, lifeDuckImpactSeconds);
                if (_impactDuck < 0f) _impactDuck = 0f;
            }
            float tensionDuck = lifeDuckTension < 1f ? Mathf.Clamp01((Tracker.Value - lifeDuckTension) / (1f - lifeDuckTension)) : 0f;
            LifeDuck = Mathf.Max(_impactDuck, tensionDuck);
            Life.Duck = LifeDuck;
            Life.DuckDepth = lifeDuckDepth;

            _lifeTimer += dt;
            if (_lifeTimer < lifeUpdateInterval) return;
            _lifeTimer = 0f;

            if (_flocks == null)
            {
                _flockSearchTimer -= lifeUpdateInterval;
                if (_flockSearchTimer <= 0f)
                {
                    _flocks = FindAnyObjectByType<FlockSystem>();
                    _flockSearchTimer = 5f;
                }
            }
            Scout.Evaluate(player, Island.All, _flocks, lifeRange);
            LifeSoundMix.Apply(Scout.Counts, LifeEnvironment.NightAmount, LifeEnvironment.Wind.magnitude,
                LifeEnvironment.Storm, Island.InputLocked ? 0f : speedN, Life);
        }

        Island FindPlayer()
        {
            if (_player != null && !_player.IsSunk) return _player;
            Island sunkFallback = null;
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var isl = all[i];
                if (isl == null || !isl.useKeyboardInput) continue;
                if (!isl.IsSunk) { _player = isl; return isl; }
                sunkFallback = isl;
            }
            _player = sunkFallback;
            return _player;
        }

        public void SetTension(float tension) => tensionOverride = Mathf.Clamp01(tension);

        public void ClearTensionOverride() => tensionOverride = -1f;

        public void TriggerImpact(float intensity) => PlayImpact(Mathf.Clamp01(intensity), 0f);

        // duration: seconds the merge stays visibly in motion (the uplift); the grind lasts that long.
        public void TriggerImpact(float intensity, float duration) => PlayImpact(Mathf.Clamp01(intensity), duration);

        // Held by whoever knows that two masses are pushing into each other; adds to the contact polling below.
        public void SetGrind(float amount) => _grindOverride = Mathf.Clamp01(amount);

        public void TriggerShipBeached(float intensity)
        {
            if (Sfx != null) Sfx.ShipBeached(intensity);
        }

        public void TriggerShipBeached(float intensity, Vector2 planarPosition) =>
            TriggerShipBeached(intensity * Audibility(planarPosition, oneShotHearingRange));

        public void TriggerBigSplash(float intensity)
        {
            if (Sfx != null) Sfx.BigSplash(intensity);
        }

        public void TriggerBigSplash(float intensity, Vector2 planarPosition) =>
            TriggerBigSplash(intensity * Audibility(planarPosition, oneShotHearingRange));

        public void TriggerWhaleBlow()
        {
            if (Sfx != null) Sfx.WhaleBlow();
        }

        public void TriggerWhaleBlow(Vector2 planarPosition)
        {
            if (Audibility(planarPosition, oneShotHearingRange) > 0.25f) TriggerWhaleBlow();
        }

        void OnLightning(Vector3 at, float strength)
        {
            if (Sfx == null) return;
            var player = FindPlayer();
            float d = player != null ? Mathf.Max(0f, Vector2.Distance(new Vector2(at.x, at.z), player.PlanarPosition) - player.BoundingRadius) : 30f;
            float heard = strength * LifeSoundMix.Proximity(d, 12f, thunderRange);
            if (heard < 0.03f) return;
            Sfx.Thunder(heard, d < 45f, Mathf.Min(3f, d / Mathf.Max(1f, thunderSpeed)));
        }

        // 1 on the player's own shore, 0 beyond `range` units of open water.
        public float Audibility(Vector2 planarPosition, float range)
        {
            var player = FindPlayer();
            if (player == null) return 1f;
            float d = Vector2.Distance(planarPosition, player.PlanarPosition) - player.BoundingRadius;
            return LifeSoundMix.Proximity(d, 6f, range);
        }

        public static float EstimateMergeSeconds(Island host, float energy, float intensity)
        {
            float uplift = host != null ? Mathf.Min(host.upliftDuration, 2.5f) : 1.5f;
            float ridge = Mathf.Clamp(0.2f * Mathf.Sqrt(Mathf.Max(0f, energy)), 0.4f, 3f);
            return uplift + 0.2f * ridge + 0.8f * Mathf.Clamp01(intensity);
        }

        public void TriggerSinkWarningPulse()
        {
            if (Sfx != null) Sfx.TriggerWarningPulse();
        }

        // Island raises Impact (normalised intensity) and then Merged (who and how hard) for the same merge:
        // the first only parks the intensity, the second knows where it happened and how long the uplift runs.
        void OnImpact(float intensity) => _heardIntensity = intensity;

        // Adventure: the player bounced off an obstacle island - the crash is the same impact, but short.
        void OnBumped(Island player, Island obstacle, Vector2 contact, float strength)
        {
            _heardIntensity = 0f;
            PlayImpact(Mathf.Clamp01(strength), 0.35f);
        }

        void OnMerged(Island host, Island guest, float energy)
        {
            float intensity = _heardIntensity;
            _heardIntensity = 0f;
            if (intensity <= 0f) return;
            var player = FindPlayer();
            if (player != null && host != null && host != player && guest != player)
                intensity *= Audibility(host.PlanarPosition, impactHearingRange + host.BoundingRadius);
            if (intensity < 0.02f) return;
            PlayImpact(intensity, EstimateMergeSeconds(host, energy, intensity));
            // The spray ring WaterFeedback throws gets its water sound; the rock crunch alone read as dry.
            if (mergeSplash > 0f && Sfx != null) Sfx.BigSplash(mergeSplash * intensity);
        }

        void PlayImpact(float intensity, float duration)
        {
            if (intensity <= 0f) return;
            if (Music != null) Music.Impact(intensity);
            if (Sfx != null) Sfx.Impact(intensity, duration);
            _impactDuck = Mathf.Max(_impactDuck, Mathf.Clamp01(0.5f + 0.5f * intensity));
        }

        // While the player's island drives into another one (IslandWorld's drive-in phase, up to 2 s before the
        // merge) the rock already grinds. Polled at 10 Hz and only while IslandWorld reports a contact at all.
        void UpdateGrind(Island player, float dt)
        {
            _grindTimer -= dt;
            if (_grindTimer > 0f) return;
            _grindTimer = grindPollInterval;

            float grind = 0f;
            if (grindFromContact && player != null && !player.IsSunk)
            {
                if (_world == null)
                {
                    _worldSearchTimer -= grindPollInterval;
                    if (_worldSearchTimer <= 0f)
                    {
                        _world = FindAnyObjectByType<IslandWorld>();
                        _worldSearchTimer = 5f;
                    }
                }
                if (_world != null && _world.ActiveContacts > 0) grind = ContactGrind(player);
            }
            Sfx.GrindAmount = Mathf.Max(grind, _grindOverride);
        }

        static float ContactGrind(Island player)
        {
            float best = 0f;
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (o == null || ReferenceEquals(o, player) || !o.isActiveAndEnabled || o.IsEmerging) continue;
                if (!Island.Near(player, o)) continue;
                if (!player.DetectContact(o, out _, out int cells) || cells == 0) continue;
                Vector2 n = o.PlanarPosition - player.PlanarPosition;
                float closing = n.sqrMagnitude > 1e-4f ? Vector2.Dot(player.PlanarVelocity - o.PlanarVelocity, n.normalized) : 0f;
                float g = Mathf.Clamp01(0.35f + 0.65f * Mathf.Max(0f, closing) / Mathf.Max(0.1f, player.MaxSpeed));
                if (g > best) best = g;
            }
            return best;
        }
    }
}
