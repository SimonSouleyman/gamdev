using System;

namespace Drift.Audio
{
    public sealed class MusicSynth : ISynthSource
    {
        public const float BaseBpm = 56f;
        public const float MaxBpm = 112f;

        public float Tension;
        public float Volume = 1f;
        public float BuildThreshold = 0.72f;
        public float CalmSeconds = 4f;
        public float BassLevel = 0.15f;
        public float PadLevel = 0.075f;
        public float PluckLevel = 0.17f;
        public float ImpactLevel = 0.34f;
        public float ImpactSwellLevel = 0.08f;
        public float DelayMix = 0.28f;
        public float DelayFeedback = 0.4f;

        public float CurrentBpm { get; private set; } = BaseBpm;
        public float EffectiveTension { get; private set; }
        public int NoteOnsets { get; private set; }
        public int PadOnsets { get; private set; }
        public int BeatCount { get; private set; }
        public int ImpactsPlayed { get; private set; }
        public int SampleRate => _sr;

        const int Block = 64;
        const int PadVoices = 8;
        const int PluckVoices = 8;

        static readonly int[] ChordPad = { 60, 64, 67, 57, 60, 64, 62, 67, 69, 55, 62, 67 };
        static readonly int[] ChordBass = { 36, 33, 38, 43 };
        static readonly int[] ChordRoot = { 0, 9, 2, 7 };
        static readonly int[] Penta = { 0, 2, 4, 7, 9 };

        struct Pad
        {
            public float p, pd, inc, incd, env, attackInc, panL, panR;
            public int state;
        }

        struct Pluck
        {
            public float p, baseInc, inc, env, attackInc, decay, amp, panL, panR, vibPhase;
            public int state;
        }

        int _sr;
        float _invSr;
        readonly float[] _mixL = new float[Block];
        readonly float[] _mixR = new float[Block];
        float[] _delL, _delR;
        int _delLenL, _delLenR, _delPosL, _delPosR;
        float _dampL, _dampR;

        readonly Pad[] _pads = new Pad[PadVoices];
        readonly Pluck[] _plucks = new Pluck[PluckVoices];
        Noise _rng = new Noise(0x5EED1234u);
        Noise _noise = new Noise(0xC0FFEEu);

        float _tSm, _calm, _brightness, _master;
        float _beatPhase, _nextStep;
        int _step, _chord = 3, _melDeg = 4, _arpIndex;
        bool _wasBuild;

        float _bassP, _bassHz, _bassTargetHz, _bassInc;
        float _padRelease, _pluckA3, _padA2, _padA3;

        float _pendingImpact;
        float _imp0, _imp1, _imp2, _impInc0, _impInc1, _impInc2, _impEnv, _impLevel, _impDecay, _impAmp, _impRise;
        float _swellEnv, _swellLevel, _swellDecay, _swellRise, _swellAmp, _swellLp, _swellCoef;
        int _swellState;

        public MusicSynth(int sampleRate = 48000)
        {
            Prepare(sampleRate);
        }

        void Prepare(int sampleRate)
        {
            if (sampleRate <= 0) sampleRate = 48000;
            _sr = sampleRate;
            _invSr = 1f / sampleRate;
            _delLenL = (int)(sampleRate * 0.34f);
            _delLenR = (int)(sampleRate * 0.41f);
            _delL = new float[_delLenL];
            _delR = new float[_delLenR];
            _delPosL = _delPosR = 0;
            _padRelease = SynthMath.DecayCoef(0.7f, sampleRate);
            _swellCoef = SynthMath.OnePoleCoef(2500f, sampleRate);
            _bassHz = _bassTargetHz = SynthMath.MidiToHz(ChordBass[0]);
            _bassInc = _bassHz * _invSr;
            _nextStep = 0f;
        }

        public void Impact(float intensity)
        {
            intensity = SynthMath.Clamp01(intensity);
            if (intensity > _pendingImpact) _pendingImpact = intensity;
        }

        public void Render(float[] buffer, int channels, int sampleRate)
        {
            if (channels <= 0) return;
            if (sampleRate != _sr) Prepare(sampleRate);
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
        }

        void Control(float dt)
        {
            float imp = _pendingImpact;
            if (imp > 0f)
            {
                _pendingImpact = 0f;
                StartImpact(imp);
            }
            if (_calm > 0f)
            {
                _calm -= dt / CalmSeconds;
                if (_calm < 0f) _calm = 0f;
            }

            float target = SynthMath.Clamp01(Tension) * (1f - _calm);
            float tau = target > _tSm ? 0.4f : 1.6f;
            _tSm += (target - _tSm) * SynthMath.SmoothCoef(tau, dt);
            EffectiveTension = _tSm;
            CurrentBpm = BaseBpm + (MaxBpm - BaseBpm) * _tSm;
            _brightness = _tSm;
            _padA2 = 0.12f + 0.35f * _brightness;
            _padA3 = 0.04f + 0.22f * _brightness;
            _pluckA3 = 0.05f + 0.3f * _brightness;
            _master = Volume * (0.65f + 0.35f * _tSm);

            _beatPhase += CurrentBpm * (1f / 60f) * dt;
            while (_beatPhase >= _nextStep)
            {
                Step();
                _nextStep += 0.25f;
            }

            _bassHz += (_bassTargetHz - _bassHz) * SynthMath.SmoothCoef(0.25f, dt);
            _bassInc = _bassHz * _invSr;

            for (int v = 0; v < PluckVoices; v++)
            {
                if (_plucks[v].state == 0) continue;
                float vp = _plucks[v].vibPhase + 5.5f * dt;
                if (vp >= 1f) vp -= 1f;
                _plucks[v].vibPhase = vp;
                _plucks[v].inc = _plucks[v].baseInc * (1f + 0.004f * SynthMath.Sine(vp));
            }
        }

        void Step()
        {
            bool bar = (_step & 15) == 0;
            bool beat = (_step & 3) == 0;
            bool eighth = (_step & 1) == 0;
            if (beat) BeatCount++;
            if (bar)
            {
                _chord = (_chord + 1) & 3;
                TriggerChord();
            }

            bool build = _tSm > BuildThreshold;
            if (build && !_wasBuild) _arpIndex = 0;
            _wasBuild = build;

            if (build)
            {
                int i = _arpIndex % 10;
                int midi = 60 + ChordRoot[_chord] + Penta[i % 5] + 12 * (i / 5);
                PlayPluck(midi, 0.75f, 0.35f + 0.25f * _rng.Next01(), (i - 5) * 0.12f);
                _arpIndex++;
            }
            else if (eighth)
            {
                float p = 0.12f + 0.4f * _tSm + (beat ? 0.08f : 0f);
                if (_rng.Next01() < p)
                {
                    _melDeg += _rng.Range(5) - 2;
                    if (_melDeg < 0) _melDeg = 0; else if (_melDeg > 9) _melDeg = 9;
                    int midi = 72 + Penta[_melDeg % 5] + 12 * (_melDeg / 5);
                    float decay = 1.4f - 0.8f * _tSm;
                    PlayPluck(midi, 0.8f + 0.2f * _rng.Next01(), decay, _rng.Next() * 0.6f);
                }
            }
            _step++;
        }

        void TriggerChord()
        {
            for (int v = 0; v < PadVoices; v++)
                if (_pads[v].state == 1 || _pads[v].state == 2) _pads[v].state = 3;

            for (int k = 0; k < 3; k++)
            {
                int slot = FreePad();
                float hz = SynthMath.MidiToHz(ChordPad[_chord * 3 + k]);
                ref Pad pad = ref _pads[slot];
                pad.p = 0f;
                pad.pd = 0.25f;
                pad.inc = hz * _invSr;
                pad.incd = pad.inc * 1.0035f;
                pad.env = 0f;
                pad.attackInc = 1f / (1.3f * _sr);
                SynthMath.Pan((k - 1) * 0.55f, out pad.panL, out pad.panR);
                pad.state = 1;
            }
            _bassTargetHz = SynthMath.MidiToHz(ChordBass[_chord]);
            PadOnsets++;
        }

        int FreePad()
        {
            int best = 0;
            float bestEnv = 2f;
            for (int v = 0; v < PadVoices; v++)
            {
                if (_pads[v].state == 0) return v;
                if (_pads[v].state == 3 && _pads[v].env < bestEnv) { bestEnv = _pads[v].env; best = v; }
            }
            return best;
        }

        void PlayPluck(int midi, float amp, float decaySeconds, float pan)
        {
            int slot = 0;
            float bestEnv = 2f;
            for (int v = 0; v < PluckVoices; v++)
            {
                if (_plucks[v].state == 0) { slot = v; break; }
                if (_plucks[v].env < bestEnv) { bestEnv = _plucks[v].env; slot = v; }
            }
            ref Pluck pl = ref _plucks[slot];
            pl.p = 0f;
            pl.baseInc = SynthMath.MidiToHz(midi) * _invSr;
            pl.inc = pl.baseInc;
            pl.env = 0f;
            pl.attackInc = 1f / (0.005f * _sr);
            pl.decay = SynthMath.DecayCoef(decaySeconds * 0.25f, _sr);
            pl.amp = amp * PluckLevel;
            pl.vibPhase = 0f;
            if (pan < -0.9f) pan = -0.9f; else if (pan > 0.9f) pan = 0.9f;
            SynthMath.Pan(pan, out pl.panL, out pl.panR);
            pl.state = 1;
            NoteOnsets++;
        }

        void StartImpact(float intensity)
        {
            _calm = 1f;
            _chord = 0;
            TriggerChord();
            _step = 1;
            _nextStep = _beatPhase + 0.25f;

            float root = SynthMath.MidiToHz(ChordBass[0]);
            _imp0 = _imp1 = _imp2 = 0f;
            _impInc0 = root * _invSr;
            _impInc1 = root * 1.5f * _invSr;
            _impInc2 = root * 2f * _invSr;
            _impEnv = 1f;
            _impLevel = 0f;
            _impDecay = SynthMath.DecayCoef(1.3f, _sr);
            _impAmp = ImpactLevel * (0.4f + 0.6f * intensity);
            // The resolving chord blooms over ~0.25 s, after the rock has spoken: the hit itself belongs to SfxSynth.
            _impRise = 1f - SynthMath.DecayCoef(0.12f, _sr);

            _swellEnv = 1f;
            _swellLevel = 0f;
            _swellState = 1;
            _swellRise = 1f / (0.12f * _sr);
            _swellDecay = SynthMath.DecayCoef(0.3f, _sr);
            _swellAmp = ImpactSwellLevel * (0.3f + 0.7f * intensity);
            ImpactsPlayed++;
        }

        void RenderBlock(int n)
        {
            float[] L = _mixL, R = _mixR;
            for (int i = 0; i < n; i++) { L[i] = 0f; R[i] = 0f; }

            float bp = _bassP, binc = _bassInc, bl = BassLevel;
            for (int i = 0; i < n; i++)
            {
                float s = (SynthMath.Sine(bp) + 0.45f * SynthMath.Sine(bp * 2f)) * bl;
                L[i] += s; R[i] += s;
                bp += binc; if (bp >= 1f) bp -= 1f;
            }
            _bassP = bp;

            float a2 = _padA2, a3 = _padA3, padLevel = PadLevel, rel = _padRelease;
            for (int v = 0; v < PadVoices; v++)
            {
                if (_pads[v].state == 0) continue;
                ref Pad pad = ref _pads[v];
                float p = pad.p, pd = pad.pd, inc = pad.inc, incd = pad.incd, env = pad.env;
                float pl = pad.panL * padLevel, pr = pad.panR * padLevel;
                int state = pad.state;
                for (int i = 0; i < n; i++)
                {
                    if (state == 1) { env += pad.attackInc; if (env >= 1f) { env = 1f; state = 2; } }
                    else if (state == 3) env *= rel;
                    float s = (SynthMath.Sine(p) + SynthMath.Sine(pd) + a2 * SynthMath.Sine(p * 2f) + a3 * SynthMath.Sine(p * 3f)) * env;
                    L[i] += s * pl; R[i] += s * pr;
                    p += inc; if (p >= 1f) p -= 1f;
                    pd += incd; if (pd >= 1f) pd -= 1f;
                }
                pad.p = p; pad.pd = pd; pad.env = env;
                pad.state = (state == 3 && env < 1e-3f) ? 0 : state;
            }

            float pa3 = _pluckA3;
            for (int v = 0; v < PluckVoices; v++)
            {
                if (_plucks[v].state == 0) continue;
                ref Pluck pk = ref _plucks[v];
                float p = pk.p, inc = pk.inc, env = pk.env, dec = pk.decay, ai = pk.attackInc;
                float pl = pk.panL * pk.amp, pr = pk.panR * pk.amp;
                int state = pk.state;
                for (int i = 0; i < n; i++)
                {
                    if (state == 1) { env += ai; if (env >= 1f) { env = 1f; state = 2; } }
                    else env *= dec;
                    float s = (SynthMath.Sine(p) + pa3 * SynthMath.Sine(p * 3f)) * env;
                    L[i] += s * pl; R[i] += s * pr;
                    p += inc; if (p >= 1f) p -= 1f;
                }
                pk.p = p; pk.env = env;
                pk.state = (state == 2 && env < 2e-4f) ? 0 : state;
            }

            if (_impEnv > 1e-4f)
            {
                float p0 = _imp0, p1 = _imp1, p2 = _imp2, env = _impEnv, lvl = _impLevel, amp = _impAmp, dec = _impDecay, rise = _impRise;
                for (int i = 0; i < n; i++)
                {
                    lvl += (env - lvl) * rise;
                    env *= dec;
                    float s = (SynthMath.Sine(p0) + 0.7f * SynthMath.Sine(p1) + 0.5f * SynthMath.Sine(p2)) * lvl * amp;
                    L[i] += s; R[i] += s;
                    p0 += _impInc0; if (p0 >= 1f) p0 -= 1f;
                    p1 += _impInc1; if (p1 >= 1f) p1 -= 1f;
                    p2 += _impInc2; if (p2 >= 1f) p2 -= 1f;
                }
                _imp0 = p0; _imp1 = p1; _imp2 = p2; _impEnv = env; _impLevel = lvl;
            }

            if (_swellState != 0)
            {
                float lvl = _swellLevel, env = _swellEnv, lp = _swellLp, c = _swellCoef, amp = _swellAmp;
                int state = _swellState;
                for (int i = 0; i < n; i++)
                {
                    if (state == 1) { lvl += _swellRise; if (lvl >= 1f) { lvl = 1f; state = 2; } }
                    else { lvl *= _swellDecay; }
                    float x = _noise.Next();
                    lp += c * (x - lp);
                    float s = (x - lp) * lvl * amp;
                    L[i] += s * 0.9f; R[i] += s * 1.1f;
                }
                _swellLevel = lvl; _swellEnv = env; _swellLp = lp;
                _swellState = (state == 2 && lvl < 1e-4f) ? 0 : state;
            }

            float fb = DelayFeedback, mix = DelayMix, master = _master;
            float[] dL = _delL, dR = _delR;
            int posL = _delPosL, posR = _delPosR, lenL = _delLenL, lenR = _delLenR;
            float dampL = _dampL, dampR = _dampR;
            for (int i = 0; i < n; i++)
            {
                float inL = L[i], inR = R[i];
                float tapL = dL[posL], tapR = dR[posR];
                dampL += 0.3f * (tapL - dampL);
                dampR += 0.3f * (tapR - dampR);
                dL[posL] = inL + dampR * fb;
                dR[posR] = inR + dampL * fb;
                if (++posL >= lenL) posL = 0;
                if (++posR >= lenR) posR = 0;
                L[i] = SynthMath.SoftClip((inL + mix * tapL) * master);
                R[i] = SynthMath.SoftClip((inR + mix * tapR) * master);
            }
            _delPosL = posL; _delPosR = posR; _dampL = dampL; _dampR = dampR;
        }
    }
}
