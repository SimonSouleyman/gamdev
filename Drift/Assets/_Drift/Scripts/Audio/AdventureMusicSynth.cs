using System;

namespace Drift.Audio
{
    // Abenteuer's music, happy-tropical and procedural like MusicSynth: marimba (1:4:10 mallet partials), a steel
    // pan lead, a plucky syncopated FM bass and a light kit (kick, shaker, clap, open hat, rim, congas).
    // Every StageMetres of RunDistance is a "stage": +TempoPerStage tempo (capped at TempoCap) and a new layer or
    // colour (chord loop, key, fresh motif). A stage never changes mid-bar: once the distance is reached, the last
    // bar of the running 4-bar chord loop becomes a fill that glides the tempo and the next bar line starts the new
    // stage on the loop's first chord (at most ~4 bars late). Inside a stage the music
    // alternates 8-bar A/B sections with a small fill at each section end. While Holding (Tilda's start briefing)
    // only marimba and shaker play; the release drops the beat in after a fill.
    // Audio thread: no allocations, fixed voice pools, silent voices cost nothing.
    public sealed class AdventureMusicSynth : ISynthSource
    {
        public const float BaseBpm = 112f;
        public const float StageMetres = 1000f;
        public const float TempoPerStage = 0.04f;
        public const float TempoCap = 0.30f;

        public const int LKick = 1, LShaker = 2, LBass = 4, LComp = 8, LMarimbaLead = 16, LClap = 32, LSteel = 64,
            LHat = 128, LConga = 256, LArp = 512, LSteelOctave = 1024;
        const int IntroLayers = LComp | LShaker;

        // Main-thread inputs (plain field writes, read once per 64-sample block).
        public float Distance;
        public bool Holding;
        public float Volume = 1f;

        public float KickLevel = 0.36f;
        public float BassLevel = 0.2f;
        public float CompLevel = 0.075f;
        public float MarimbaLeadLevel = 0.15f;
        public float ArpLevel = 0.05f;
        public float SteelLevel = 0.12f;
        public float ShakerLevel = 0.15f;
        public float HatLevel = 0.08f;
        public float ClapLevel = 0.14f;
        public float RimLevel = 0.07f;
        public float CongaLevel = 0.12f;
        public float SplashLevel = 0.045f;
        public float DelayMix = 0.22f;
        public float DelayFeedback = 0.3f;
        public float Swing = 0.1f;
        public float Master = 0.95f;

        public float CurrentBpm { get; private set; } = BaseBpm;
        public int Stage { get; private set; }
        public int TargetStage { get; private set; }
        public int ChordLoop { get; private set; }
        public int Key { get; private set; }
        public int Layers { get; private set; } = LayersAt(0);
        public bool Intro { get; private set; }
        public bool InFill => _fillBar;
        // 0 = A, 1 = B.
        public int Section => (_barInStage >> 3) & 1;
        public int Bars { get; private set; }
        public int StageChanges { get; private set; }
        // Step index (16ths since the restart) the last stage change landed on: always a multiple of 16.
        public int LastStageChangeStep { get; private set; } = -1;
        public int NoteOnsets { get; private set; }
        public int DrumHits { get; private set; }
        public long SamplesRendered { get; private set; }
        public int SampleRate => _sr;

        public static int StageAt(float metres) => metres > 0f ? (int)(metres / StageMetres) : 0;
        public static float TempoFactor(int stage) => 1f + Math.Min(TempoCap, TempoPerStage * Math.Max(0, stage));
        public static float BpmForStage(int stage) => BaseBpm * TempoFactor(stage);
        public static float BpmAt(float metres) => BpmForStage(StageAt(metres));

        // What each stage adds; past the table the loop and key keep turning so every 1000 m still sounds new.
        static readonly int[] StageLoop = { 0, 0, 0, 1, 1, 2, 2, 0, 1 };
        static readonly int[] StageKey = { 0, 0, 0, 0, 2, 2, 5, 5, 7 };
        static readonly int[] StageLayers =
        {
            LKick | LShaker | LBass | LComp | LMarimbaLead,
            LKick | LShaker | LBass | LComp | LMarimbaLead | LClap,
            LKick | LShaker | LBass | LComp | LClap | LSteel,
            LKick | LShaker | LBass | LComp | LClap | LSteel | LHat,
            LKick | LShaker | LBass | LComp | LClap | LSteel | LHat | LConga,
            LKick | LShaker | LBass | LComp | LClap | LSteel | LHat | LConga | LArp,
            LKick | LShaker | LBass | LComp | LClap | LSteel | LHat | LConga | LArp,
            LKick | LShaker | LBass | LComp | LClap | LSteel | LHat | LConga | LArp | LSteelOctave,
            LKick | LShaker | LBass | LComp | LClap | LSteel | LHat | LConga | LArp | LSteelOctave,
        };
        static readonly int[] LateKeys = { 0, 2, 5, 7 };

        public static int LoopAt(int stage) => stage < StageLoop.Length ? StageLoop[Math.Max(0, stage)] : stage % 3;
        public static int KeyAt(int stage) => stage < StageKey.Length ? StageKey[Math.Max(0, stage)] : LateKeys[stage & 3];
        public static int LayersAt(int stage) => StageLayers[Math.Min(Math.Max(0, stage), StageLayers.Length - 1)];

        // Chord loops, one chord per bar: I-IV-V-IV (major), I-bVII-IV-I (mixolydian), I-vi-IV-V (major).
        static readonly int[] LoopRoots = { 0, 5, 7, 5, 0, 10, 5, 0, 0, 9, 5, 7 };
        static readonly bool[] LoopMinor = { false, false, false, false, false, false, false, false, false, true, false, false };
        static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 };
        static readonly int[] Mixo = { 0, 2, 4, 5, 7, 9, 10 };

        // 16-step patterns: interval above the chord root, int.MinValue = rest.
        const int R = int.MinValue;
        static readonly int[] BassA = { 0, R, R, 0, R, R, 7, R, 0, R, R, 12, R, R, 7, R };
        static readonly int[] BassB = { 0, R, R, 12, R, R, 0, R, 7, R, 0, R, 12, R, 7, R };
        static readonly float[] ShakerVel = { 0.45f, 0.2f, 1f, 0.3f };
        static readonly bool[] CompA = { false, false, true, false, false, false, true, false, false, false, true, false, false, false, true, false };
        static readonly bool[] CompB = { true, false, false, true, false, false, true, false, false, false, true, false, false, true, false, false };
        static readonly int[] CongaPat = { 0, 0, 2, 2, 0, 0, 1, 0, 0, 0, 2, 2, 0, 0, 1, 1 };

        // Melody rhythms (bar 1 busier, bar 2 sparser) as 16-bit step masks, bit i = step i.
        static readonly int[] RhythmBusy = { 0x1549, 0x494D, 0x5449, 0x0D4C, 0x1249 };
        static readonly int[] RhythmSparse = { 0x0149, 0x0115, 0x1141, 0x0144, 0x0049 };

        const int Block = 64;
        const int Mallets = 12;
        const int Steels = 4;
        const int Rest = -128;
        // A tonal voice stops below this amplitude (about -70 dBFS after the music volume).
        const float VoiceFloor = 3e-4f;

        struct Mallet
        {
            public float p, inc, e1, e4, e10, d1, d4, d10, gl, gr;
            public bool on;
        }

        struct Steel
        {
            public float p, q, inc, qinc, e1, e2, e3, eq, d1, d2, d3, dq, gl, gr;
            public bool on;
        }

        struct Bass
        {
            public float p, inc, env, fm, dec, fmDec, rel, amp;
            public bool on, released;
        }

        int _sr;
        float _invSr;
        readonly float[] _mixL = new float[Block];
        readonly float[] _mixR = new float[Block];
        readonly float[] _sendL = new float[Block];
        readonly float[] _sendR = new float[Block];
        float[] _delL, _delR;
        int _delLenL, _delLenR, _delPosL, _delPosR;
        float _dampL, _dampR;

        readonly Mallet[] _mallets = new Mallet[Mallets];
        readonly Steel[] _steels = new Steel[Steels];
        readonly Bass[] _bass = new Bass[2];
        int _bassNext;
        readonly sbyte[] _motifA = new sbyte[32];
        readonly sbyte[] _motifB = new sbyte[32];
        int _lastA, _lastB;
        Noise _noise = new Noise(0xA11CE5u);
        Noise _rng = new Noise(0x7A0B1Cu);

        // Drums: kick, shaker, hat, clap, rim, two congas, splash.
        float _kP, _kEnv, _kPitch, _kClick, _kAmp, _kDec, _kPitchDec, _kClickDec;
        float _shEnv, _shDec, _shLp, _shAmp, _shCoef;
        float _hhEnv, _hhDec, _hhLp, _hhAmp, _hhCoef;
        float _clEnv, _clAmp, _clBurstDec, _clTailDec, _clLow, _clBand, _clF;
        int _clT, _clBurstLen;
        float _rmP1, _rmP2, _rmEnv, _rmDec, _rmAmp, _rmInc1, _rmInc2;
        float _cgP, _cgInc, _cgBend, _cgEnv, _cgDec, _cgSlap, _cgAmp, _cgBendDec, _cgSlapDec;
        float _spEnv, _spDec, _spLp, _spAmp;

        float _beatPhase, _nextStep, _bpmFrom, _bpmTo, _fillStartBeat;
        int _step, _barInStage, _pendingStage, _runSeed;
        bool _fillBar, _sectionFill, _introRelease;
        float _duck, _duckTarget;
        volatile bool _restartPending;
        volatile float _jumpPending = -1f;

        public AdventureMusicSynth(int sampleRate = 48000)
        {
            Prepare(sampleRate);
            ResetState(0);
        }

        void Prepare(int sampleRate)
        {
            if (sampleRate <= 0) sampleRate = 48000;
            _sr = sampleRate;
            _invSr = 1f / sampleRate;
            _delLenL = (int)(sampleRate * 0.375f);
            _delLenR = (int)(sampleRate * 0.25f);
            _delL = new float[_delLenL];
            _delR = new float[_delLenR];
            _delPosL = _delPosR = 0;
            _kDec = SynthMath.DecayCoef(0.13f, _sr);
            _kPitchDec = SynthMath.DecayCoef(0.03f, _sr);
            _kClickDec = SynthMath.DecayCoef(0.002f, _sr);
            _shDec = SynthMath.DecayCoef(0.028f, _sr);
            _shCoef = SynthMath.OnePoleCoef(5500f, _sr);
            _hhDec = SynthMath.DecayCoef(0.075f, _sr);
            _hhCoef = SynthMath.OnePoleCoef(7500f, _sr);
            _clBurstDec = SynthMath.DecayCoef(0.0035f, _sr);
            _clTailDec = SynthMath.DecayCoef(0.07f, _sr);
            _clBurstLen = (int)(0.009f * _sr);
            _clF = SynthMath.SvfCoef(1300f, _sr);
            _rmDec = SynthMath.DecayCoef(0.014f, _sr);
            _rmInc1 = 1750f * _invSr;
            _rmInc2 = 520f * _invSr;
            _cgBendDec = SynthMath.DecayCoef(0.02f, _sr);
            _cgSlapDec = SynthMath.DecayCoef(0.004f, _sr);
            _spDec = SynthMath.DecayCoef(0.4f, _sr);
        }

        // Fresh run: back to stage 0 (the intro if Holding is set when the audio thread picks it up).
        public void Restart() => _restartPending = true;

        // Straight to the stage of `metres`, without a fill (tests, and a run resumed mid-way).
        public void JumpTo(float metres) => _jumpPending = Math.Max(0f, metres);

        // A bump in the race: the music ducks for a moment instead of changing course.
        public void Impact(float intensity)
        {
            float d = 0.45f * SynthMath.Clamp01(intensity);
            if (d > _duckTarget) _duckTarget = d;
        }

        void ResetState(int stage)
        {
            for (int i = 0; i < Mallets; i++) _mallets[i].on = false;
            for (int i = 0; i < Steels; i++) _steels[i].on = false;
            _bass[0].on = _bass[1].on = false;
            _kEnv = _kClick = _shEnv = _hhEnv = _clEnv = _rmEnv = _cgEnv = _cgSlap = _spEnv = 0f;
            _clT = int.MaxValue;
            Array.Clear(_delL, 0, _delL.Length);
            Array.Clear(_delR, 0, _delR.Length);
            _dampL = _dampR = 0f;
            _beatPhase = 0f;
            _nextStep = 0f;
            _step = 0;
            _barInStage = 0;
            Bars = 0;
            _fillBar = _sectionFill = _introRelease = false;
            _duck = _duckTarget = 0f;
            _runSeed++;
            ApplyStage(stage);
            TargetStage = stage;
            CurrentBpm = _bpmFrom = _bpmTo = BpmForStage(stage);
            Intro = Holding && stage == 0;
        }

        void ApplyStage(int stage)
        {
            Stage = stage;
            ChordLoop = LoopAt(stage);
            Key = KeyAt(stage);
            Layers = LayersAt(stage);
            _barInStage = 0;
            BuildMotif(_motifA, stage * 2, true, out _lastA);
            BuildMotif(_motifB, stage * 2 + 1, false, out _lastB);
        }

        // A two-bar motif as scale degrees (Rest = silent). Strong steps are snapped to the chord when played, so
        // the same motif fits every bar of the loop; phrases 2 and 4 of a section answer it (see MelodyDegree).
        void BuildMotif(sbyte[] motif, int salt, bool busy, out int last)
        {
            var rng = new Noise((uint)(0x9E3779B9u * (uint)(salt + 1) + 0x632BE5ABu * (uint)_runSeed));
            int m1 = busy ? RhythmBusy[rng.Range(RhythmBusy.Length)] : RhythmSparse[rng.Range(RhythmSparse.Length)];
            int m2 = RhythmSparse[rng.Range(RhythmSparse.Length)];
            int deg = busy ? 4 : 7;
            last = 0;
            for (int s = 0; s < 32; s++)
            {
                int mask = s < 16 ? m1 : m2;
                if ((mask & (1 << (s & 15))) == 0) { motif[s] = Rest; continue; }
                int move = rng.Range(7) - 3;
                if (move == 3 || move == -3) move /= 3;
                deg += move;
                if (deg < 0) deg = 1; else if (deg > 11) deg = 10;
                motif[s] = (sbyte)deg;
                last = s;
            }
        }

        public void Render(float[] buffer, int channels, int sampleRate)
        {
            if (channels <= 0) return;
            if (sampleRate != _sr) { Prepare(sampleRate); ResetState(Stage); }
            int frames = buffer.Length / channels;
            int done = 0;
            while (done < frames)
            {
                int n = frames - done;
                if (n > Block) n = Block;
                Control(n * _invSr);
                RenderBlock(n);
                SynthMath.WriteInterleaved(buffer, channels, done, _mixL, _mixR, n);
                done += n;
            }
            SamplesRendered += frames;
        }

        void Control(float dt)
        {
            if (_restartPending)
            {
                _restartPending = false;
                ResetState(0);
            }
            float jump = _jumpPending;
            if (jump >= 0f)
            {
                _jumpPending = -1f;
                ResetState(StageAt(jump));
                Intro = false;
            }

            TargetStage = StageAt(Distance);
            if (Intro && !Holding) _introRelease = true;

            float bpm;
            if (_fillBar)
            {
                float t = (_beatPhase - _fillStartBeat) * 0.25f;
                if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
                bpm = _bpmFrom + (_bpmTo - _bpmFrom) * t;
            }
            else bpm = BpmForStage(Stage);
            CurrentBpm = bpm;

            _beatPhase += bpm * (1f / 60f) * dt;
            while (_beatPhase >= _nextStep)
            {
                Step();
                float sw = Swing * 0.25f;
                _nextStep += (_step & 1) == 1 ? 0.25f + sw : 0.25f - sw;
            }
            if (_beatPhase > 4096f)
            {
                _beatPhase -= 4096f;
                _nextStep -= 4096f;
                _fillStartBeat -= 4096f;
            }

            _duck += (_duckTarget - _duck) * SynthMath.SmoothCoef(0.03f, dt);
            _duckTarget *= (float)Math.Exp(-dt / 0.8f);
        }

        void Step()
        {
            int s = _step & 15;
            if (s == 0) BarStart();

            int layers = Intro ? IntroLayers : Layers;
            bool sectionEnd = (_barInStage & 7) == 7 && !_fillBar;
            bool fillHalf = _fillBar && s >= 8;
            int chord = _barInStage & 3;
            int loop = ChordLoop;
            int root = LoopRoots[loop * 4 + chord];
            bool minor = LoopMinor[loop * 4 + chord];
            int section = Section;

            // ---- drums
            if ((layers & LKick) != 0 && !(fillHalf && s > 8) && (s & 3) == 0)
                Kick(s == 0 || s == 8 ? 1f : 0.7f);
            if ((layers & LShaker) != 0 && !fillHalf)
                Shaker(ShakerVel[s & 3] * (Intro ? 0.6f : 1f));
            if ((layers & LClap) != 0 && (s == 4 || s == 12) && !fillHalf) Clap(1f);
            if ((layers & LHat) != 0 && (s & 3) == 2 && !fillHalf) Hat(s == 14 && sectionEnd ? 1.3f : 1f);
            if ((layers & LConga) != 0 && !fillHalf)
            {
                int c = CongaPat[s];
                if (section == 1 && s == 7) c = 2;
                if (c != 0) Conga(c == 2, c == 2 ? 0.8f : 1f);
                if (s == 3 || s == 11) Rim(0.6f);
            }
            if (sectionEnd && s >= 12 && !Intro) Rim(0.45f + 0.15f * (s - 12));
            if (fillHalf)
            {
                // The stage fill: a conga/rim roll that builds, then the new stage lands on the next bar line.
                float v = 0.5f + 0.07f * (s - 8);
                if ((s & 1) == 0) Conga(s < 12, v); else Rim(v);
                if (s == 12 || s == 14 || s == 15) Clap(0.7f + 0.1f * (s - 12));
            }

            // ---- bass
            if ((layers & LBass) != 0 && !(fillHalf && s >= 12))
            {
                int iv = section == 0 ? BassA[s] : BassB[s];
                if (iv != R) PlayBass(33 + ((Key + root - 9) % 12 + 12) % 12 + iv, s == 0 ? 1f : 0.8f);
            }

            // ---- marimba comp (double stops) / fill arpeggio
            if (fillHalf)
                PlayMallet(ChordTone(Key + root, minor, s - 8, 60), 0.9f * MarimbaLeadLevel, (s - 12) * 0.12f);
            else if ((layers & LComp) != 0)
            {
                bool hit = section == 0 ? CompA[s] : CompB[s];
                if (hit)
                {
                    int lo = Voice(Key + root + (minor ? 3 : 4), 57), hi = Voice(Key + root + 7, lo + 1);
                    float pan = ((s >> 1) & 1) == 0 ? -0.35f : 0.35f;
                    PlayMallet(lo, CompLevel, pan);
                    PlayMallet(hi, CompLevel * 0.85f, -pan);
                }
                if ((layers & LArp) != 0 && section == 1 && (s & 1) == 1)
                    PlayMallet(ChordTone(Key + root, minor, (s >> 1) & 7, 69), ArpLevel, 0.5f - 0.12f * ((s >> 1) & 7));
            }

            // ---- melody
            if (!_fillBar && !Intro)
            {
                bool steel = (layers & LSteel) != 0;
                bool marimbaLead = (layers & LMarimbaLead) != 0;
                if (steel || marimbaLead)
                {
                    int deg = MelodyDegree(s, section);
                    if (deg != Rest)
                    {
                        int midi = MelodyMidi(deg, root, minor, (s & 3) == 0);
                        if (steel)
                        {
                            float v = (s & 3) == 0 ? 1f : 0.8f;
                            PlaySteel(midi, SteelLevel * v, 0.15f);
                            if ((layers & LSteelOctave) != 0 && section == 1) PlaySteel(midi + 12, SteelLevel * 0.35f * v, -0.3f);
                        }
                        else PlayMallet(midi, MarimbaLeadLevel * ((s & 3) == 0 ? 1f : 0.8f), 0.1f);
                    }
                }
            }
            _step++;
        }

        void BarStart()
        {
            if (_step > 0) { _barInStage++; Bars++; }
            if (_fillBar)
            {
                _fillBar = false;
                if (_introRelease) { Intro = false; _introRelease = false; }
                if (_pendingStage != Stage)
                {
                    ApplyStage(_pendingStage);
                    StageChanges++;
                    LastStageChangeStep = _step;
                }
                else _barInStage = 0;
                Splash(1f);
                return;
            }
            // A new stage waits for the last bar of the 4-bar chord loop, so it always starts on the loop's first chord.
            bool stageDue = !Intro && TargetStage > Stage && (_barInStage & 3) == 3;
            if (stageDue || (Intro && _introRelease))
            {
                _fillBar = true;
                _pendingStage = stageDue ? Stage + 1 : Stage;
                _bpmFrom = BpmForStage(Stage);
                _bpmTo = BpmForStage(_pendingStage);
                _fillStartBeat = _beatPhase;
            }
        }

        // Scale degree for this step of the section: bars alternate the motif's two halves; the second phrase of
        // every four bars answers it a third lower and comes home to the tonic.
        int MelodyDegree(int s, int section)
        {
            sbyte[] motif = section == 0 ? _motifA : _motifB;
            int last = section == 0 ? _lastA : _lastB;
            int ms = ((_barInStage & 1) << 4) | s;
            int deg = motif[ms];
            if (deg == Rest) return Rest;
            bool answer = (_barInStage & 2) != 0;
            if (answer && ms >= 20)
            {
                if (ms == last) return deg >= 4 ? 7 : 0;
                deg -= 2;
                if (deg < 0) deg += 7;
            }
            return deg;
        }

        int MelodyMidi(int deg, int chordRoot, bool minor, bool strong)
        {
            int[] scale = ChordLoop == 1 ? Mixo : Major;
            int baseMidi = 72 + Key - (Key > 4 ? 12 : 0);
            int midi = baseMidi + scale[deg % 7] + 12 * (deg / 7);
            if (!strong) return midi;
            int pc = ((midi - Key - chordRoot) % 12 + 12) % 12;
            int third = minor ? 3 : 4;
            if (pc == 0 || pc == third || pc == 7) return midi;
            // Nearest chord tone, downward on a tie.
            for (int d = 1; d <= 2; d++)
            {
                int down = ((pc - d) % 12 + 12) % 12, up = (pc + d) % 12;
                if (down == 0 || down == third || down == 7) return midi - d;
                if (up == 0 || up == third || up == 7) return midi + d;
            }
            return midi;
        }

        // The i-th tone of the arpeggiated chord (root, third, fifth, up the octaves) from `floor` on; pitch classes
        // are absolute (Key already added).
        static int ChordTone(int root, bool minor, int i, int floor)
        {
            int iv = (i % 3) switch { 0 => 0, 1 => minor ? 3 : 4, _ => 7 };
            return Voice(root + iv, floor) + 12 * (i / 3);
        }

        // The lowest pitch of pitch class `pc` (relative to C) at or above `floor`.
        static int Voice(int pc, int floor) => floor + (((pc - floor) % 12) + 12) % 12;

        // ------------------------------------------------------------ voices

        void PlayMallet(int midi, float amp, float pan)
        {
            int slot = 0;
            float best = float.MaxValue;
            for (int v = 0; v < Mallets; v++)
            {
                if (!_mallets[v].on) { slot = v; best = -1f; break; }
                if (_mallets[v].e1 < best) { best = _mallets[v].e1; slot = v; }
            }
            ref Mallet m = ref _mallets[slot];
            float hz = SynthMath.MidiToHz(midi);
            m.p = 0f;
            m.inc = hz * _invSr;
            m.e1 = amp;
            m.e4 = amp * 0.3f;
            m.e10 = hz * 10f < 0.45f * _sr ? amp * 0.09f : 0f;
            float tau = 0.38f * (float)Math.Sqrt(262f / hz);
            m.d1 = SynthMath.DecayCoef(tau < 0.14f ? 0.14f : (tau > 0.55f ? 0.55f : tau), _sr);
            m.d4 = SynthMath.DecayCoef(0.06f, _sr);
            m.d10 = SynthMath.DecayCoef(0.014f, _sr);
            SynthMath.Pan(pan < -0.9f ? -0.9f : (pan > 0.9f ? 0.9f : pan), out m.gl, out m.gr);
            m.on = true;
            NoteOnsets++;
        }

        void PlaySteel(int midi, float amp, float pan)
        {
            int slot = 0;
            float best = float.MaxValue;
            for (int v = 0; v < Steels; v++)
            {
                if (!_steels[v].on) { slot = v; break; }
                if (_steels[v].e1 < best) { best = _steels[v].e1; slot = v; }
            }
            ref Steel st = ref _steels[slot];
            float hz = SynthMath.MidiToHz(midi);
            st.p = 0f;
            st.q = 0f;
            st.inc = hz * _invSr;
            st.qinc = hz * 4.18f < 0.45f * _sr ? hz * 4.18f * _invSr : 0f;
            st.e1 = amp;
            st.e2 = amp * 0.55f;
            st.e3 = amp * 0.3f;
            st.eq = st.qinc > 0f ? amp * 0.14f : 0f;
            st.d1 = SynthMath.DecayCoef(0.55f, _sr);
            st.d2 = SynthMath.DecayCoef(0.3f, _sr);
            st.d3 = SynthMath.DecayCoef(0.16f, _sr);
            st.dq = SynthMath.DecayCoef(0.05f, _sr);
            SynthMath.Pan(pan, out st.gl, out st.gr);
            st.on = true;
            NoteOnsets++;
        }

        void PlayBass(int midi, float vel)
        {
            ref Bass old = ref _bass[_bassNext ^ 1];
            if (old.on) old.released = true;
            ref Bass b = ref _bass[_bassNext];
            b.p = 0f;
            b.inc = SynthMath.MidiToHz(midi) * _invSr;
            b.env = 1f;
            b.fm = 0.32f;
            b.amp = BassLevel * vel;
            b.dec = SynthMath.DecayCoef(0.2f, _sr);
            b.fmDec = SynthMath.DecayCoef(0.07f, _sr);
            b.rel = SynthMath.DecayCoef(0.012f, _sr);
            b.released = false;
            b.on = true;
            _bassNext ^= 1;
            NoteOnsets++;
        }

        void Kick(float vel) { _kP = 0f; _kEnv = 1f; _kPitch = 1f; _kClick = 1f; _kAmp = KickLevel * vel; DrumHits++; }
        void Shaker(float vel) { _shEnv = 1f; _shAmp = ShakerLevel * vel; DrumHits++; }
        void Hat(float vel) { _hhEnv = 1f; _hhAmp = HatLevel * vel; DrumHits++; }
        void Clap(float vel) { _clT = 0; _clEnv = 1f; _clAmp = ClapLevel * vel; DrumHits++; }
        void Rim(float vel) { _rmP1 = _rmP2 = 0f; _rmEnv = 1f; _rmAmp = RimLevel * vel; DrumHits++; }
        void Splash(float vel)
        {
            if (((Intro ? IntroLayers : Layers) & LKick) == 0) return;
            _spEnv = 1f;
            _spAmp = SplashLevel * vel;
        }

        void Conga(bool high, float vel)
        {
            _cgP = 0f;
            _cgInc = (high ? 330f : 220f) * _invSr;
            _cgBend = 0.18f;
            _cgEnv = 1f;
            _cgSlap = high ? 1f : 0.5f;
            _cgDec = SynthMath.DecayCoef(high ? 0.1f : 0.15f, _sr);
            _cgAmp = CongaLevel * vel;
            DrumHits++;
        }

        // ------------------------------------------------------------ render

        void RenderBlock(int n)
        {
            float[] L = _mixL, R = _mixR, SL = _sendL, SR = _sendR;
            for (int i = 0; i < n; i++) { L[i] = 0f; R[i] = 0f; SL[i] = 0f; SR[i] = 0f; }

            for (int v = 0; v < Mallets; v++)
            {
                if (!_mallets[v].on) continue;
                ref Mallet m = ref _mallets[v];
                float p = m.p, inc = m.inc, e1 = m.e1, e4 = m.e4, e10 = m.e10, d1 = m.d1, d4 = m.d4, d10 = m.d10, gl = m.gl, gr = m.gr;
                // The upper partials are gone after ~0.1 s: the long tail only pays for the fundamental.
                if (e4 < 1e-5f && e10 < 1e-5f)
                {
                    for (int i = 0; i < n; i++)
                    {
                        float s = SynthMath.Sine(p) * e1;
                        e1 *= d1;
                        p += inc; if (p >= 1f) p -= 1f;
                        L[i] += s * gl; R[i] += s * gr;
                        SL[i] += s * gl; SR[i] += s * gr;
                    }
                }
                else
                {
                    for (int i = 0; i < n; i++)
                    {
                        float s = SynthMath.Sine(p) * e1 + SynthMath.Sine(p * 4f) * e4 + SynthMath.Sine(p * 10f) * e10;
                        e1 *= d1; e4 *= d4; e10 *= d10;
                        p += inc; if (p >= 1f) p -= 1f;
                        L[i] += s * gl; R[i] += s * gr;
                        SL[i] += s * gl; SR[i] += s * gr;
                    }
                }
                m.p = p; m.e1 = e1; m.e4 = e4; m.e10 = e10;
                if (e1 < VoiceFloor) m.on = false;
            }

            for (int v = 0; v < Steels; v++)
            {
                if (!_steels[v].on) continue;
                ref Steel st = ref _steels[v];
                float p = st.p, q = st.q, inc = st.inc, qinc = st.qinc, e1 = st.e1, e2 = st.e2, e3 = st.e3, eq = st.eq;
                float d1 = st.d1, d2 = st.d2, d3 = st.d3, dq = st.dq, gl = st.gl, gr = st.gr;
                if (e2 < 1e-5f && e3 < 1e-5f && eq < 1e-5f)
                {
                    for (int i = 0; i < n; i++)
                    {
                        float s = SynthMath.Sine(p) * e1;
                        e1 *= d1;
                        p += inc; if (p >= 1f) p -= 1f;
                        L[i] += s * gl; R[i] += s * gr;
                        SL[i] += s * gr; SR[i] += s * gl;
                    }
                }
                else
                {
                    for (int i = 0; i < n; i++)
                    {
                        float s = SynthMath.Sine(p) * e1 + SynthMath.Sine(p * 2f) * e2 + SynthMath.Sine(p * 3f) * e3 + SynthMath.Sine(q) * eq;
                        e1 *= d1; e2 *= d2; e3 *= d3; eq *= dq;
                        p += inc; if (p >= 1f) p -= 1f;
                        q += qinc; if (q >= 1f) q -= 1f;
                        L[i] += s * gl; R[i] += s * gr;
                        SL[i] += s * gr; SR[i] += s * gl;
                    }
                }
                st.p = p; st.q = q; st.e1 = e1; st.e2 = e2; st.e3 = e3; st.eq = eq;
                if (e1 < VoiceFloor) st.on = false;
            }

            for (int v = 0; v < 2; v++)
            {
                if (!_bass[v].on) continue;
                ref Bass b = ref _bass[v];
                float p = b.p, inc = b.inc, env = b.env, fm = b.fm, amp = b.amp;
                float dec = b.released ? b.rel : b.dec, fmDec = b.fmDec;
                for (int i = 0; i < n; i++)
                {
                    float s = SynthMath.Sine(p + fm * SynthMath.Sine(p)) * env * amp;
                    env *= dec; fm = 0.06f + (fm - 0.06f) * fmDec;
                    p += inc; if (p >= 1f) p -= 1f;
                    L[i] += s; R[i] += s;
                }
                b.p = p; b.env = env; b.fm = fm;
                if (env < 1e-4f) b.on = false;
            }

            bool drums = _kEnv > 1e-3f || _shEnv > 1e-4f || _hhEnv > 1e-4f || _clEnv > 1e-4f || _rmEnv > 1e-4f
                || _cgEnv > 1e-4f || _spEnv > 1e-4f;
            if (drums) RenderDrums(n, L, R);

            float fb = DelayFeedback, mix = DelayMix;
            float master = Master * Volume * (1f - _duck);
            float[] dL = _delL, dR = _delR;
            int posL = _delPosL, posR = _delPosR, lenL = _delLenL, lenR = _delLenR;
            float dampL = _dampL, dampR = _dampR;
            for (int i = 0; i < n; i++)
            {
                float tapL = dL[posL], tapR = dR[posR];
                dampL += 0.35f * (tapL - dampL);
                dampR += 0.35f * (tapR - dampR);
                dL[posL] = SL[i] + dampR * fb;
                dR[posR] = SR[i] + dampL * fb;
                if (++posL >= lenL) posL = 0;
                if (++posR >= lenR) posR = 0;
                float l = (L[i] + mix * dampL) * master, r = (R[i] + mix * dampR) * master;
                L[i] = l > 0.6f || l < -0.6f ? SynthMath.SoftClip(l) : l;
                R[i] = r > 0.6f || r < -0.6f ? SynthMath.SoftClip(r) : r;
            }
            _delPosL = posL; _delPosR = posR; _dampL = dampL; _dampR = dampR;
        }

        void RenderDrums(int n, float[] L, float[] R)
        {
            float kP = _kP, kEnv = _kEnv, kPitch = _kPitch, kClick = _kClick, kAmp = _kAmp;
            float shEnv = _shEnv, shLp = _shLp, shAmp = _shAmp;
            float hhEnv = _hhEnv, hhLp = _hhLp, hhAmp = _hhAmp;
            float clEnv = _clEnv, clLow = _clLow, clBand = _clBand, clAmp = _clAmp, clF = _clF;
            int clT = _clT, clLen = _clBurstLen;
            float rmP1 = _rmP1, rmP2 = _rmP2, rmEnv = _rmEnv, rmAmp = _rmAmp;
            float cgP = _cgP, cgInc = _cgInc, cgBend = _cgBend, cgEnv = _cgEnv, cgSlap = _cgSlap, cgAmp = _cgAmp;
            float spEnv = _spEnv, spLp = _spLp, spAmp = _spAmp;
            float kDec = _kDec, kPd = _kPitchDec, kCd = _kClickDec, shD = _shDec, shC = _shCoef, hhD = _hhDec, hhC = _hhCoef;
            float clBd = _clBurstDec, clTd = _clTailDec, rmD = _rmDec, cgD = _cgDec, cgBd = _cgBendDec, cgSd = _cgSlapDec, spD = _spDec;
            float invSr = _invSr;
            for (int i = 0; i < n; i++)
            {
                float x = _noise.Next();
                float l = 0f, r = 0f;

                if (kEnv > 1e-3f)
                {
                    float s = SynthMath.Sine(kP) * kEnv + x * kClick * 0.25f;
                    kP += (48f + 110f * kPitch) * invSr; if (kP >= 1f) kP -= 1f;
                    kEnv *= kDec; kPitch *= kPd; kClick *= kCd;
                    s *= kAmp;
                    l += s; r += s;
                }
                if (shEnv > 1e-4f)
                {
                    shLp += shC * (x - shLp);
                    float s = (x - shLp) * shEnv * shAmp;
                    shEnv *= shD;
                    l += s * 0.7f; r += s * 1.2f;
                }
                if (hhEnv > 1e-4f)
                {
                    hhLp += hhC * (x - hhLp);
                    float s = (x - hhLp) * hhEnv * hhAmp;
                    hhEnv *= hhD;
                    l += s * 1.2f; r += s * 0.7f;
                }
                if (clEnv > 1e-4f)
                {
                    if (clT < 3 * clLen && clT % clLen == 0) clEnv = 1f;
                    clLow += clF * clBand;
                    float hi = x - clLow - 0.6f * clBand;
                    clBand += clF * hi;
                    float s = clBand * clEnv * clAmp;
                    clEnv *= clT < 3 * clLen ? clBd : clTd;
                    if (clT == 3 * clLen - 1) clEnv = 0.7f;
                    clT++;
                    l += s; r += s;
                }
                if (rmEnv > 1e-4f)
                {
                    float s = (SynthMath.Sine(rmP1) + 0.6f * SynthMath.Sine(rmP2)) * rmEnv * rmAmp;
                    rmP1 += _rmInc1; if (rmP1 >= 1f) rmP1 -= 1f;
                    rmP2 += _rmInc2; if (rmP2 >= 1f) rmP2 -= 1f;
                    rmEnv *= rmD;
                    l += s * 1.1f; r += s * 0.8f;
                }
                if (cgEnv > 1e-4f)
                {
                    float s = (SynthMath.Sine(cgP) * cgEnv + x * cgSlap * 0.5f) * cgAmp;
                    cgP += cgInc * (1f + cgBend); if (cgP >= 1f) cgP -= 1f;
                    cgEnv *= cgD; cgBend *= cgBd; cgSlap *= cgSd;
                    l += s * 1.2f; r += s * 0.75f;
                }
                if (spEnv > 1e-4f)
                {
                    spLp += 0.5f * (x - spLp);
                    float s = (x - spLp) * spEnv * spAmp;
                    spEnv *= spD;
                    l += s; r += s;
                }
                L[i] += l; R[i] += r;
            }
            _kP = kP; _kEnv = kEnv; _kPitch = kPitch; _kClick = kClick;
            _shEnv = shEnv; _shLp = shLp;
            _hhEnv = hhEnv; _hhLp = hhLp;
            _clEnv = clEnv; _clLow = clLow; _clBand = clBand; _clT = clT;
            _rmP1 = rmP1; _rmP2 = rmP2; _rmEnv = rmEnv;
            _cgP = cgP; _cgBend = cgBend; _cgEnv = cgEnv; _cgSlap = cgSlap;
            _spEnv = spEnv; _spLp = spLp;
        }
    }
}
