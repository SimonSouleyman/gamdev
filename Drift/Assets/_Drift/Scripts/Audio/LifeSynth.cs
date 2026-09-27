using System;

namespace Drift.Audio
{
    // Habitat layer: songbirds and seabirds by day, crickets and frogs at night, leaves in the wind, shore
    // lapping and rare sheep/ox calls. The main thread only writes the density fields (LifeSoundMix via
    // AudioDirector, every 0.5 s); everything is synthesised per 64-sample block on the audio thread with no
    // allocation. Slow parameters (glides, envelopes of the long calls) are evaluated once per block and
    // ramped per sample, so the per-sample work is a few table lookups per active voice.
    public sealed class LifeSynth : ISynthSource
    {
        public float BirdDensity;
        public float SeabirdAmount;
        public float CricketDensity;
        public float RustleAmount;
        public float LapAmount;
        public float FrogAmount;
        public float SheepAmount;
        public float OxAmount;
        public float Duck;
        public float Volume = 1f;
        public float DuckDepth = 0.5f;

        public float BirdGain = 0.11f;
        public float SeabirdGain = 0.12f;
        public float CricketGain = 0.05f;
        public float RustleGain = 0.22f;
        public float LapGain = 0.3f;
        public float FrogGain = 0.09f;
        public float CallGain = 0.14f;
        public float RestMin = 2f;
        public float RestMax = 8f;

        public int ChirpOnsets { get; private set; }
        public int MotifsSung { get; private set; }
        public int SeabirdCries { get; private set; }
        public int CricketChirps { get; private set; }
        public int FrogBloops { get; private set; }
        public int SheepCalls { get; private set; }
        public int OxCalls { get; private set; }
        public int Gusts { get; private set; }
        public int Laps { get; private set; }
        public float DuckLevel => _duckSm;
        public int SampleRate => _sr;

        public const int Singers = 3;
        const int Block = 64;
        static readonly float[] Degrees = { 0f, 2f, 4f, 7f, 9f, 12f, 14f, 16f };

        struct Singer
        {
            public int notesLeft, deg;
            public bool inNote;
            public float wait, baseHz, noteDur, gapMin, gapMax, fmDepth, amp, panL, panR;
            public float p, inc, glideMul, envPos, envInc, pm, pmInc;
        }

        struct Cricket
        {
            public bool on, gate;
            public float pc, incC, pp, incP, gateT, lvl, target, panL, panR;
        }

        // Sheep and ox share one voice: a harmonic stack with tremolo, an arc or fall in pitch and one
        // formant band-pass; the parameters make the difference.
        struct Call
        {
            public bool on;
            public int kind;
            public float t, dur, f0, arc, fall, tremDepth, attack, amp, panL, panR;
            public float p, inc, pt, ptInc, low, band, f, q, lvl;
        }

        int _sr;
        float _invSr, _t;
        readonly float[] _mixL = new float[Block];
        readonly float[] _mixR = new float[Block];
        Noise _rng = new Noise(0xB1BD5EEDu);
        Noise _nL = new Noise(0x51A7E5u);
        Noise _nR = new Noise(0x7E57A11u);
        Noise _lapL = new Noise(0xC0A57u);
        Noise _lapR = new Noise(0x5EA51DEu);

        float _birdSm, _seabirdSm, _cricketSm, _rustleSm, _lapSm, _frogSm, _sheepSm, _oxSm, _duckSm, _master;

        readonly Singer[] _singers = new Singer[Singers];
        readonly Cricket[] _crickets = new Cricket[2];
        Call _call;

        bool _sbOn;
        float _sbWait = 4f, _sbT, _sbDur, _sbF0, _sbP, _sbInc, _sbPr, _sbPrInc, _sbLvl, _sbAmp, _sbPanL, _sbPanR;

        float _rLpL, _rLp2L, _rHpL, _rLpR, _rLp2R, _rHpR, _rLpCoef, _rHpCoef, _rLvl, _rTarget, _flurry, _flurryTarget, _flurryHold;
        float _lLp1L, _lLp2L, _lHpL, _lLp1R, _lLp2R, _lHpR, _lLpCoef, _lHpCoef, _lLvl, _lTarget, _lapT, _lapPeriod = 1.8f, _lapStrength = 0.8f;

        bool _frOn;
        float _frWait = 2f, _frP, _frInc, _frMul, _frEnvPos, _frEnvInc, _frAmp, _frPanL, _frPanR, _frDouble;

        float _callWaitSheep = 8f, _callWaitOx = 15f;

        public LifeSynth(int sampleRate = 48000)
        {
            Prepare(sampleRate);
        }

        void Prepare(int sampleRate)
        {
            if (sampleRate <= 0) sampleRate = 48000;
            _sr = sampleRate;
            _invSr = 1f / sampleRate;
            _rHpCoef = SynthMath.OnePoleCoef(800f, sampleRate);
            _rLpCoef = SynthMath.OnePoleCoef(1800f, sampleRate);
            _lLpCoef = SynthMath.OnePoleCoef(1100f, sampleRate);
            _lHpCoef = SynthMath.OnePoleCoef(250f, sampleRate);
            for (int k = 0; k < Singers; k++)
            {
                ref Singer s = ref _singers[k];
                s.baseHz = 2300f + 650f * k + 500f * _rng.Next01();
                s.deg = 2 + _rng.Range(3);
                s.wait = 0.5f + 2f * _rng.Next01();
                s.inNote = false;
                s.notesLeft = 0;
                SynthMath.Pan(-0.6f + 0.6f * k + 0.3f * _rng.Next(), out s.panL, out s.panR);
            }
            for (int k = 0; k < 2; k++)
            {
                ref Cricket c = ref _crickets[k];
                c.incC = (k == 0 ? 4200f : 4340f) * _invSr;
                c.incP = (k == 0 ? 31f : 27f) * _invSr;
                c.gateT = 0.3f + 0.5f * _rng.Next01();
                SynthMath.Pan(k == 0 ? -0.4f : 0.4f, out c.panL, out c.panR);
            }
            _sbPrInc = 28f * _invSr;
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
            _t += dt;
            if (_t > 3600f) _t -= 3600f;
            float smooth = SynthMath.SmoothCoef(0.8f, dt);
            _birdSm += (SynthMath.Clamp01(BirdDensity) - _birdSm) * smooth;
            _seabirdSm += (SynthMath.Clamp01(SeabirdAmount) - _seabirdSm) * smooth;
            _cricketSm += (SynthMath.Clamp01(CricketDensity) - _cricketSm) * smooth;
            _rustleSm += (SynthMath.Clamp01(RustleAmount) - _rustleSm) * smooth;
            _lapSm += (SynthMath.Clamp01(LapAmount) - _lapSm) * smooth;
            _frogSm += (SynthMath.Clamp01(FrogAmount) - _frogSm) * smooth;
            _sheepSm += (SynthMath.Clamp01(SheepAmount) - _sheepSm) * smooth;
            _oxSm += (SynthMath.Clamp01(OxAmount) - _oxSm) * smooth;
            float duck = SynthMath.Clamp01(Duck);
            _duckSm += (duck - _duckSm) * SynthMath.SmoothCoef(duck > _duckSm ? 0.25f : 1.2f, dt);
            _master = Volume * (1f - DuckDepth * _duckSm);

            ControlBirds(dt);
            ControlSeabird(dt);
            ControlCrickets(dt);
            ControlRustle(dt);
            ControlLap(dt);
            ControlFrog(dt);
            ControlCalls(dt);
        }

        // ------------------------------------------------------------ songbirds

        void ControlBirds(float dt)
        {
            float d = _birdSm;
            for (int k = 0; k < Singers; k++)
            {
                ref Singer s = ref _singers[k];
                if (s.inNote) continue;
                s.wait -= dt;
                if (s.wait > 0f) continue;
                if (s.notesLeft > 0) { StartNote(ref s); continue; }
                bool allowed = d > 0.02f + (float)k / Singers;
                if (!allowed) { s.wait = 0.5f; continue; }
                StartMotif(ref s, d);
            }
        }

        void StartMotif(ref Singer s, float density)
        {
            s.notesLeft = 2 + _rng.Range(4);
            s.noteDur = 0.045f + 0.09f * _rng.Next01();
            s.gapMin = 0.02f + 0.05f * _rng.Next01();
            s.gapMax = s.gapMin + 0.03f + 0.09f * _rng.Next01();
            s.fmDepth = _rng.Next01() < 0.4f ? 0.004f + 0.008f * _rng.Next01() : 0.0015f;
            s.pmInc = (30f + 90f * _rng.Next01()) * _invSr;
            s.amp = BirdGain * (0.6f + 0.4f * _rng.Next01());
            MotifsSung++;
            StartNote(ref s);
        }

        void StartNote(ref Singer s)
        {
            int prev = s.deg;
            int step = _rng.Range(4);
            step = step < 2 ? step - 2 : step - 1;
            if (_rng.Next01() < 0.15f) step *= 2;
            if (s.notesLeft < 4 && _rng.Next01() < 0.2f) step = 0;
            int deg = prev + step;
            if (deg < 0 || deg >= Degrees.Length) deg = prev - step;
            if (deg < 0) deg = 0; else if (deg >= Degrees.Length) deg = Degrees.Length - 1;
            s.deg = deg;
            float hz = s.baseHz * (float)Math.Pow(2.0, Degrees[deg] / 12.0);
            float glideSemi = _rng.Next() * 3f;
            float endHz = hz * (float)Math.Pow(2.0, glideSemi / 12.0);
            float samples = s.noteDur * _sr;
            s.inc = hz * _invSr;
            s.glideMul = (float)Math.Pow(endHz / hz, 1.0 / samples);
            s.envPos = 0f;
            s.envInc = 1f / samples;
            s.p = 0f;
            s.pm = 0f;
            s.inNote = true;
            s.notesLeft--;
            ChirpOnsets++;
        }

        void EndNote(ref Singer s)
        {
            s.inNote = false;
            if (s.notesLeft > 0)
            {
                s.wait = s.gapMin + (s.gapMax - s.gapMin) * _rng.Next01();
                return;
            }
            float d = _birdSm;
            float rest = RestMax + (RestMin - RestMax) * d;
            s.wait = rest * (0.6f + 0.8f * _rng.Next01());
        }

        // ------------------------------------------------------------ seabird cry

        void ControlSeabird(float dt)
        {
            float a = _seabirdSm;
            if (!_sbOn)
            {
                if (a <= 0.02f) { if (_sbWait < 3f) _sbWait = 3f; return; }
                _sbWait -= dt;
                if (_sbWait > 0f) return;
                _sbOn = true;
                _sbT = 0f;
                _sbDur = 0.5f + 0.4f * _rng.Next01();
                _sbF0 = 1500f + 600f * _rng.Next01();
                _sbAmp = SeabirdGain * (0.7f + 0.3f * _rng.Next01()) * (0.5f + 0.5f * a);
                _sbP = 0f; _sbPr = 0f; _sbLvl = 0f;
                SynthMath.Pan(_rng.Next() * 0.7f, out _sbPanL, out _sbPanR);
                float wait = 16f + (6f - 16f) * a;
                _sbWait = wait * (0.6f + 0.8f * _rng.Next01());
                SeabirdCries++;
            }
            _sbT += dt;
            float u = _sbT / _sbDur;
            if (u >= 1f) { _sbOn = false; return; }
            float hz = u < 0.25f ? _sbF0 * (0.85f + 0.15f * (u / 0.25f)) : _sbF0 * (float)Math.Pow(0.45, (u - 0.25f) / 0.75f);
            _sbInc = hz * _invSr;
        }

        // ------------------------------------------------------------ crickets

        void ControlCrickets(float dt)
        {
            float d = _cricketSm;
            float lfo = 0.75f + 0.25f * (float)Math.Sin(_t * (2.0 * Math.PI * 0.08));
            for (int k = 0; k < 2; k++)
            {
                ref Cricket c = ref _crickets[k];
                bool active = d > (k == 0 ? 0.02f : 0.4f);
                if (!active)
                {
                    c.target = 0f;
                    if (c.lvl < 1e-5f) { c.on = false; c.lvl = 0f; }
                    continue;
                }
                c.on = true;
                c.gateT -= dt;
                if (c.gateT <= 0f)
                {
                    c.gate = !c.gate;
                    if (c.gate)
                    {
                        c.gateT = 0.25f + 0.5f * _rng.Next01();
                        c.incC = (k == 0 ? 4200f : 4340f) * (0.98f + 0.04f * _rng.Next01()) * _invSr;
                        CricketChirps++;
                    }
                    else c.gateT = 0.6f + (0.15f - 0.6f) * d + 0.4f * _rng.Next01();
                }
                c.target = c.gate ? CricketGain * (0.3f + 0.7f * d) * lfo : 0f;
            }
        }

        // ------------------------------------------------------------ leaves in the wind

        void ControlRustle(float dt)
        {
            float r = _rustleSm;
            float gust = 0.5f + 0.5f * (0.6f * (float)Math.Sin(_t * (2.0 * Math.PI * 0.05) + 0.9) + 0.4f * (float)Math.Sin(_t * (2.0 * Math.PI * 0.13)));
            if (r > 0.02f)
            {
                if (_flurryHold > 0f)
                {
                    _flurryHold -= dt;
                    if (_flurryHold <= 0f) _flurryTarget = 0f;
                }
                else if (_rng.Next01() < dt * (0.12f + 0.3f * r))
                {
                    _flurryTarget = 1f;
                    _flurryHold = 0.3f + 0.4f * _rng.Next01();
                    Gusts++;
                }
            }
            else _flurryTarget = 0f;
            _flurry += (_flurryTarget - _flurry) * SynthMath.SmoothCoef(_flurryTarget > _flurry ? 0.3f : 1.2f, dt);
            float bright = SynthMath.Clamp01(gust + 0.5f * _flurry);
            _rLpCoef = SynthMath.OnePoleCoef(1400f + 1100f * bright, _sr);
            _rTarget = RustleGain * r * (0.25f + 0.75f * gust) * (1f + 0.8f * _flurry);
        }

        // ------------------------------------------------------------ shore lapping

        void ControlLap(float dt)
        {
            float a = _lapSm;
            _lapT += dt;
            if (_lapT >= _lapPeriod)
            {
                _lapT = 0f;
                _lapPeriod = 1.4f + 1.2f * _rng.Next01();
                _lapStrength = 0.6f + 0.4f * _rng.Next01();
                if (a > 0.02f) Laps++;
            }
            float env;
            if (_lapT < 0.3f) { float x = _lapT / 0.3f; env = x * x * (3f - 2f * x); }
            else env = (float)Math.Exp(-(_lapT - 0.3f) / 0.45f);
            _lTarget = a > 0.02f ? LapGain * a * env * _lapStrength : 0f;
        }

        // ------------------------------------------------------------ frog bloops at dusk

        void ControlFrog(float dt)
        {
            float a = _frogSm;
            if (_frOn) return;
            if (a <= 0.02f) { if (_frWait < 1f) _frWait = 1f; return; }
            _frWait -= dt;
            if (_frWait > 0f) return;
            float dur = 0.09f + 0.07f * _rng.Next01();
            float hz = 520f + 260f * _rng.Next01();
            float endHz = hz * (0.42f + 0.1f * _rng.Next01());
            float samples = dur * _sr;
            _frInc = hz * _invSr;
            _frMul = (float)Math.Pow(endHz / hz, 1.0 / samples);
            _frEnvPos = 0f;
            _frEnvInc = 1f / samples;
            _frP = 0f;
            _frAmp = FrogGain * (0.6f + 0.4f * _rng.Next01()) * (0.5f + 0.5f * a);
            if (_frDouble <= 0f) SynthMath.Pan(_rng.Next() * 0.8f, out _frPanL, out _frPanR);
            _frOn = true;
            FrogBloops++;
            if (_frDouble <= 0f && _rng.Next01() < 0.35f) { _frDouble = 1f; _frWait = 0.12f + 0.08f * _rng.Next01(); }
            else
            {
                _frDouble = 0f;
                float wait = 6f + (1.5f - 6f) * a;
                _frWait = wait * (0.5f + _rng.Next01());
            }
        }

        // ------------------------------------------------------------ sheep and ox

        void ControlCalls(float dt)
        {
            if (!_call.on)
            {
                if (_sheepSm > 0.02f)
                {
                    _callWaitSheep -= dt;
                    if (_callWaitSheep <= 0f)
                    {
                        StartCall(0, _sheepSm);
                        float wait = 40f + (15f - 40f) * _sheepSm;
                        _callWaitSheep = wait * (0.6f + 0.8f * _rng.Next01());
                    }
                }
                else if (_callWaitSheep < 5f) _callWaitSheep = 5f;
                if (!_call.on && _oxSm > 0.02f)
                {
                    _callWaitOx -= dt;
                    if (_callWaitOx <= 0f)
                    {
                        StartCall(1, _oxSm);
                        float wait = 70f + (30f - 70f) * _oxSm;
                        _callWaitOx = wait * (0.6f + 0.8f * _rng.Next01());
                    }
                }
                else if (_oxSm <= 0.02f && _callWaitOx < 8f) _callWaitOx = 8f;
                if (!_call.on) return;
            }
            ref Call c = ref _call;
            c.t += dt;
            float u = c.t / c.dur;
            if (u >= 1f) { c.on = false; return; }
            float pitch = c.f0 * (1f + c.arc * SynthMath.Sine(0.5f * u)) * (1f - c.fall * u);
            c.inc = pitch * _invSr;
        }

        void StartCall(int kind, float amount)
        {
            ref Call c = ref _call;
            c.on = true;
            c.kind = kind;
            c.t = 0f;
            c.p = 0f; c.pt = 0f; c.low = 0f; c.band = 0f; c.lvl = 0f;
            float level = CallGain * (0.7f + 0.3f * _rng.Next01()) * (0.5f + 0.5f * amount);
            if (kind == 0)
            {
                c.dur = 0.5f + 0.4f * _rng.Next01();
                c.f0 = 230f + 100f * _rng.Next01();
                c.arc = 0.08f;
                c.fall = 0.03f;
                c.tremDepth = 0.55f;
                c.ptInc = (8f + 3f * _rng.Next01()) * _invSr;
                c.attack = 0.08f;
                c.f = SynthMath.SvfCoef(850f + 200f * _rng.Next01(), _sr);
                c.q = 0.6f;
                c.amp = level;
                SheepCalls++;
            }
            else
            {
                c.dur = 1.0f + 0.6f * _rng.Next01();
                c.f0 = 95f + 30f * _rng.Next01();
                c.arc = 0.03f;
                c.fall = 0.12f;
                c.tremDepth = 0.2f;
                c.ptInc = 4f * _invSr;
                c.attack = 0.15f;
                c.f = SynthMath.SvfCoef(330f + 80f * _rng.Next01(), _sr);
                c.q = 0.5f;
                c.amp = level * 1.3f;
                OxCalls++;
            }
            c.inc = c.f0 * _invSr;
            SynthMath.Pan(_rng.Next() * 0.5f, out c.panL, out c.panR);
        }

        // ------------------------------------------------------------ render

        void RenderBlock(int n)
        {
            float[] L = _mixL, R = _mixR;
            for (int i = 0; i < n; i++) { L[i] = 0f; R[i] = 0f; }
            float invN = 1f / n;

            for (int k = 0; k < Singers; k++)
            {
                if (!_singers[k].inNote) continue;
                ref Singer s = ref _singers[k];
                float p = s.p, inc = s.inc, mul = s.glideMul, env = s.envPos, envInc = s.envInc, pm = s.pm, pmInc = s.pmInc, fm = s.fmDepth;
                float pl = s.panL * s.amp, pr = s.panR * s.amp;
                bool ended = false;
                for (int i = 0; i < n; i++)
                {
                    if (env >= 1f) { ended = true; break; }
                    float w = SynthMath.Sine(0.5f * env);
                    float v = SynthMath.Sine(p + fm * SynthMath.Sine(pm)) * w;
                    L[i] += v * pl; R[i] += v * pr;
                    p += inc; if (p >= 1f) p -= 1f;
                    inc *= mul;
                    pm += pmInc; if (pm >= 1f) pm -= 1f;
                    env += envInc;
                }
                s.p = p; s.inc = inc; s.envPos = env; s.pm = pm;
                if (ended) EndNote(ref s);
            }

            if (_sbOn || _sbLvl > 1e-5f)
            {
                float target = _sbOn ? EnvelopeOf(_sbT / _sbDur, 0.06f, 0.6f) * _sbAmp : 0f;
                float lvl = _sbLvl, step = (target - lvl) * invN;
                float p = _sbP, inc = _sbInc, pr = _sbPr, prInc = _sbPrInc;
                float pl = _sbPanL, prr = _sbPanR;
                for (int i = 0; i < n; i++)
                {
                    lvl += step;
                    float rasp = 1f - 0.35f * (0.5f + 0.5f * SynthMath.Sine(pr));
                    float v = (SynthMath.Sine(p) + 0.5f * SynthMath.Sine(p * 2f) + 0.25f * SynthMath.Sine(p * 3f)) * rasp * lvl;
                    L[i] += v * pl; R[i] += v * prr;
                    p += inc; if (p >= 1f) p -= 1f;
                    pr += prInc; if (pr >= 1f) pr -= 1f;
                }
                _sbP = p; _sbPr = pr; _sbLvl = lvl;
            }

            for (int k = 0; k < 2; k++)
            {
                if (!_crickets[k].on) continue;
                ref Cricket c = ref _crickets[k];
                float pc = c.pc, incC = c.incC, pp = c.pp, incP = c.incP, lvl = c.lvl, target = c.target;
                float pl = c.panL, pr = c.panR;
                for (int i = 0; i < n; i++)
                {
                    lvl += (target - lvl) * 0.002f;
                    float pulse = SynthMath.Sine(pp);
                    pulse = pulse > 0f ? pulse * pulse : 0f;
                    float v = SynthMath.Sine(pc) * pulse * lvl;
                    L[i] += v * pl; R[i] += v * pr;
                    pc += incC; if (pc >= 1f) pc -= 1f;
                    pp += incP; if (pp >= 1f) pp -= 1f;
                }
                c.pc = pc; c.pp = pp; c.lvl = lvl;
            }

            if (_rTarget > 1e-5f || _rLvl > 1e-5f)
            {
                float lvl = _rLvl, step = (_rTarget - lvl) * invN;
                float lpL = _rLpL, lp2L = _rLp2L, hpL = _rHpL, lpR = _rLpR, lp2R = _rLp2R, hpR = _rHpR, lc = _rLpCoef, hc = _rHpCoef;
                for (int i = 0; i < n; i++)
                {
                    lvl += step;
                    lpL += lc * (_nL.Next() - lpL); lp2L += lc * (lpL - lp2L); hpL += hc * (lp2L - hpL);
                    lpR += lc * (_nR.Next() - lpR); lp2R += lc * (lpR - lp2R); hpR += hc * (lp2R - hpR);
                    L[i] += (lp2L - hpL) * lvl;
                    R[i] += (lp2R - hpR) * lvl;
                }
                _rLpL = lpL; _rLp2L = lp2L; _rHpL = hpL; _rLpR = lpR; _rLp2R = lp2R; _rHpR = hpR; _rLvl = lvl;
            }

            if (_lTarget > 1e-5f || _lLvl > 1e-5f)
            {
                float lvl = _lLvl, step = (_lTarget - lvl) * invN;
                float l1L = _lLp1L, l2L = _lLp2L, hL = _lHpL, l1R = _lLp1R, l2R = _lLp2R, hR = _lHpR, lc = _lLpCoef, hc = _lHpCoef;
                for (int i = 0; i < n; i++)
                {
                    lvl += step;
                    l1L += lc * (_lapL.Next() - l1L); l2L += lc * (l1L - l2L); hL += hc * (l2L - hL);
                    l1R += lc * (_lapR.Next() - l1R); l2R += lc * (l1R - l2R); hR += hc * (l2R - hR);
                    L[i] += (l2L - hL) * lvl;
                    R[i] += (l2R - hR) * lvl;
                }
                _lLp1L = l1L; _lLp2L = l2L; _lHpL = hL; _lLp1R = l1R; _lLp2R = l2R; _lHpR = hR; _lLvl = lvl;
            }

            if (_frOn)
            {
                float p = _frP, inc = _frInc, mul = _frMul, env = _frEnvPos, envInc = _frEnvInc;
                float pl = _frPanL * _frAmp, pr = _frPanR * _frAmp;
                for (int i = 0; i < n; i++)
                {
                    if (env >= 1f) { _frOn = false; break; }
                    float v = (SynthMath.Sine(p) + 0.3f * SynthMath.Sine(p * 2f)) * SynthMath.Sine(0.5f * env);
                    L[i] += v * pl; R[i] += v * pr;
                    p += inc; if (p >= 1f) p -= 1f;
                    inc *= mul;
                    env += envInc;
                }
                _frP = p; _frInc = inc; _frEnvPos = env;
            }

            if (_call.on || _call.lvl > 1e-5f)
            {
                ref Call c = ref _call;
                float target = c.on ? EnvelopeOf(c.t / c.dur, c.attack, 0.65f) * c.amp : 0f;
                float lvl = c.lvl, step = (target - lvl) * invN;
                float p = c.p, inc = c.inc, pt = c.pt, ptInc = c.ptInc, low = c.low, band = c.band, f = c.f, q = c.q, trem = c.tremDepth;
                float pl = c.panL, pr = c.panR;
                for (int i = 0; i < n; i++)
                {
                    lvl += step;
                    float s = SynthMath.Sine(p) + 0.7f * SynthMath.Sine(p * 2f) + 0.5f * SynthMath.Sine(p * 3f) + 0.35f * SynthMath.Sine(p * 4f) + 0.25f * SynthMath.Sine(p * 5f);
                    low += f * band;
                    float high = s - low - q * band;
                    band += f * high;
                    float tr = 1f - trem * (0.5f + 0.5f * SynthMath.Sine(pt));
                    float v = (1.2f * band + 0.25f * s) * tr * lvl;
                    L[i] += v * pl; R[i] += v * pr;
                    p += inc; if (p >= 1f) p -= 1f;
                    pt += ptInc; if (pt >= 1f) pt -= 1f;
                }
                c.p = p; c.pt = pt; c.low = low; c.band = band; c.lvl = lvl;
            }

            float master = _master;
            for (int i = 0; i < n; i++)
            {
                L[i] = SynthMath.SoftClip(L[i] * master);
                R[i] = SynthMath.SoftClip(R[i] * master);
            }
        }

        static float EnvelopeOf(float u, float attack, float sustainEnd)
        {
            if (u < attack) return u / attack;
            if (u > sustainEnd) return (1f - u) / (1f - sustainEnd);
            return 1f;
        }
    }
}
