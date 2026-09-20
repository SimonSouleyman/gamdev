using System;

namespace Drift.Audio
{
    // Wind, water wash, sinking pulse and the one-shots. An impact is rock, not a tone: a soft body hit, a
    // stochastic grind of hundreds of tiny resonator grains per second (power-law amplitudes, stick-slip
    // density), a few inharmonic cracks, a brown rumble with a small-speaker body band and a debris tail.
    // Everything that is not wind/water lives on one "bus" that costs nothing while it is silent.
    public sealed class SfxSynth : ISynthSource
    {
        public const int LayerBody = 1, LayerCrunch = 2, LayerCracks = 4, LayerRumble = 8, LayerDebris = 16, LayerAll = 31;

        public float WindAmount;
        public float WaterAmount;
        public float SinkWarning;
        // 0..1, held by the main thread while two islands drive into each other before the merge.
        public float GrindAmount;
        public float Volume = 1f;
        public float WindGain = 0.55f;
        public float WhistleGain = 0.05f;
        public float WaterGain = 0.5f;
        public float ImpactGain = 0.9f;
        public float WarningGain = 0.3f;
        public float WarningPeriod = 1.5f;
        public float BodyLevel = 0.15f;
        public float CrunchLevel = 0.85f;
        public float BedLevel = 0.09f;
        public float CrackLevel = 0.5f;
        public float RumbleLevel = 0.11f;
        public float BodyBandLevel = 0.055f;
        public float DebrisLevel = 0.2f;
        public float OneShotLevel = 0.24f;
        public int Layers = LayerAll;

        public float Gust { get; private set; }
        public int ImpactsPlayed { get; private set; }
        public int GrainsPlayed { get; private set; }
        public int CracksPlayed { get; private set; }
        public int PlopsPlayed { get; private set; }
        public int WarningPulses { get; private set; }
        public int BeachingsPlayed { get; private set; }
        public int SplashesPlayed { get; private set; }
        public int BlowsPlayed { get; private set; }
        public int ActiveGrains => _liveGrains;
        public bool BusActive => _busActive;
        public float LastImpactSeconds { get; private set; }
        public int SampleRate => _sr;

        const int Block = 64;
        const int MaxGrains = 32;
        const int MaxNoise = 6;
        const int MaxPlops = 6;
        const int MaxSched = 24;
        const int KindCrack = 1, KindPlop = 2, KindSplash = 3, KindCreak = 4, KindSprinkle = 5;

        // Two-pole resonator rung by a 0.3-1.5 ms noise burst: a decaying, slightly noisy ping.
        struct Grain
        {
            public float y1, y2, a1, a2, r, w, glide, exc, excDec, gl, gr;
            public int life, delay, excLen, nzOff;
        }

        struct NoiseVoice
        {
            public bool on;
            public int state, hold;
            public float low, band, f, fMul, fMin, q, lvl, atkInc, dec, amp, lowMix, gl, gr;
        }

        struct Plop
        {
            public bool on;
            public int state;
            public float p, inc, mul, env, atkInc, dec, amp, gl, gr;
        }

        static int _instances;

        int _sr;
        float _invSr, _t;
        readonly float[] _mixL = new float[Block];
        readonly float[] _mixR = new float[Block];
        readonly float[] _busL = new float[Block];
        readonly float[] _busR = new float[Block];
        readonly float[] _nz = new float[Block];
        Noise _nL = new Noise(0x1234567u);
        Noise _nR = new Noise(0x7654321u);
        Noise _rng;

        float _wLp1L, _wLp2L, _wLp1R, _wLp2R, _wCoef, _wAmp, _wAmpTarget;
        float _whLow, _whBand, _whF, _whAmp;
        float _waLpL, _waHpL, _waLpR, _waHpR, _waLpCoef, _waHpCoef, _waAmp, _waAmpTarget;

        float _warnP, _warnInc, _warnT, _warnEnv, _warnLvl, _warnDecay, _warnAmt;
        bool _pendingWarn;

        float _pendingImpact, _pendingDuration, _pendingBeach, _pendingSplash;
        bool _pendingBlow;

        readonly Grain[] _grains = new Grain[MaxGrains];
        readonly NoiseVoice[] _noise = new NoiseVoice[MaxNoise];
        readonly Plop[] _plops = new Plop[MaxPlops];
        readonly float[] _schedTime = new float[MaxSched];
        readonly float[] _schedArg = new float[MaxSched];
        readonly int[] _schedKind = new int[MaxSched];
        int _schedCount, _liveGrains, _liveNoise, _livePlops;
        bool _busActive;
        float _clock;

        bool _evOn;
        float _evT, _evDur, _evInt, _evTail, _evEnd, _grindSm;
        float _slip, _slipTarget, _slipHold;
        float _sprT, _sprDens, _sprLoHz, _sprHiHz, _sprT60Lo, _sprT60Hi, _sprAmp;

        bool _contOn;
        float _rum1, _rum2, _rumHp, _rumHp2, _rumC1, _rumC2, _rumCHp, _rumNorm, _rumLvl, _rumTarget;
        float _bodyLow, _bodyBand, _bodyF, _bodyNorm, _bodyLvl, _bodyTarget;
        float _bedHiL, _bedLoL, _bedHiR, _bedLoR, _bedCHi, _bedCLo, _bedLvl, _bedTarget;
        float _tremP1, _tremP2;

        bool _crOn;
        float _crT, _crDur, _crF0, _crF1, _crWob, _crP, _crInc, _crCyc, _crLow, _crBand, _crF, _crLvl, _crTarget, _crAmp;

        public SfxSynth(int sampleRate = 48000, uint seed = 0u)
        {
            if (seed == 0u)
                seed = (uint)Environment.TickCount * 2654435761u + (uint)System.Threading.Interlocked.Increment(ref _instances) * 0x9E3779B9u;
            _rng = new Noise(seed);
            Prepare(sampleRate);
        }

        void Prepare(int sampleRate)
        {
            if (sampleRate <= 0) sampleRate = 48000;
            _sr = sampleRate;
            _invSr = 1f / sampleRate;
            _waLpCoef = SynthMath.OnePoleCoef(2600f, sampleRate);
            _waHpCoef = SynthMath.OnePoleCoef(450f, sampleRate);
            _warnDecay = SynthMath.DecayCoef(0.4f, sampleRate);
            _warnInc = 48f * _invSr;
            _warnT = WarningPeriod;

            // Rumble and body band hold nothing above ~400 Hz, so they run at half the sample rate.
            int half = sampleRate / 2;
            _rumC1 = SynthMath.OnePoleCoef(90f, half);
            _rumC2 = SynthMath.OnePoleCoef(120f, half);
            _rumCHp = SynthMath.OnePoleCoef(45f, half);
            _bodyF = SynthMath.SvfCoef(210f, half);
            _bodyNorm = BandNorm(210f, 0.5f);
            _rumNorm = MeasureRumbleNorm();
            ResetBus();
        }

        // The brown 40-120 Hz chain has no handy closed form for its level, so it is measured once.
        float MeasureRumbleNorm()
        {
            var n = new Noise(0xB0D1E5u);
            float a = 0f, b = 0f, hp = 0f, hp2 = 0f;
            double sum = 0;
            int warm = _sr / 8, count = _sr / 2, total = count + warm;
            for (int i = 0; i < total; i++)
            {
                float x = 0.25f * (n.Next() + n.Next() + n.Next() + n.Next());
                a += _rumC1 * (x - a);
                b += _rumC2 * (a - b);
                hp += _rumCHp * (b - hp);
                float y = b - hp;
                hp2 += _rumCHp * (y - hp2);
                y -= hp2;
                if (i >= warm) sum += y * y;
            }
            double rms = Math.Sqrt(sum / count);
            return rms > 1e-9 ? (float)(1.0 / rms) : 1f;
        }

        // Gain that brings a Chamberlin band-pass of the shared noise (variance 1/6) to unit RMS.
        float BandNorm(float hz, float q) => (float)Math.Sqrt(6.0 * q * _sr / (Math.PI * hz));

        public static float DefaultImpactSeconds(float intensity) => 1f + 1.2f * SynthMath.Clamp01(intensity);

        public void Impact(float intensity) => Impact(intensity, 0f);

        // duration: how long the two masses visibly grind (the uplift); <= 0 derives it from the intensity.
        public void Impact(float intensity, float duration)
        {
            intensity = SynthMath.Clamp01(intensity);
            if (intensity <= 0f) return;
            if (intensity >= _pendingImpact)
            {
                _pendingDuration = duration;
                _pendingImpact = intensity;
            }
        }

        public void ShipBeached(float intensity)
        {
            intensity = SynthMath.Clamp01(intensity);
            if (intensity > _pendingBeach) _pendingBeach = intensity;
        }

        public void BigSplash(float intensity)
        {
            intensity = SynthMath.Clamp01(intensity);
            if (intensity > _pendingSplash) _pendingSplash = intensity;
        }

        public void WhaleBlow()
        {
            _pendingBlow = true;
        }

        public void TriggerWarningPulse()
        {
            _pendingWarn = true;
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
                float dt = n * _invSr;
                Control(dt);
                ControlBus(dt);
                RenderBlock(n);
                SynthMath.WriteInterleaved(buffer, channels, done, _mixL, _mixR, n);
                done += n;
            }
        }

        void Control(float dt)
        {
            _t += dt;
            if (_t > 3600f) _t -= 3600f;
            float wind = SynthMath.Clamp01(WindAmount);
            float water = SynthMath.Clamp01(WaterAmount);
            float smooth = SynthMath.SmoothCoef(0.3f, dt);

            float gust = 0.5f + 0.5f * (0.6f * (float)Math.Sin(_t * (2.0 * Math.PI * 0.07)) + 0.4f * (float)Math.Sin(_t * (2.0 * Math.PI * 0.19) + 1.7));
            gust = SynthMath.Clamp01(gust);
            Gust = gust;
            _wCoef = SynthMath.OnePoleCoef(220f + 700f * gust + 500f * wind, _sr);
            _wAmpTarget = WindGain * (0.15f + 0.85f * wind) * (0.35f + 0.65f * gust);
            _wAmp += (_wAmpTarget - _wAmp) * smooth;
            _whF = SynthMath.SvfCoef(500f + 900f * gust, _sr);
            _whAmp = WhistleGain * gust * wind;

            float lfo = 0.5f + 0.5f * (0.6f * (float)Math.Sin(_t * (2.0 * Math.PI * 0.31)) + 0.4f * (float)Math.Sin(_t * (2.0 * Math.PI * 0.53) + 2.1));
            _waAmpTarget = WaterGain * (0.05f + 0.95f * water) * (0.5f + 0.5f * lfo);
            _waAmp += (_waAmpTarget - _waAmp) * smooth;

            float warn = SynthMath.Clamp01(SinkWarning);
            _warnAmt += (warn - _warnAmt) * smooth;
            if (warn > 0f)
            {
                _warnT += dt;
                if (_warnT >= WarningPeriod) { _warnT -= WarningPeriod; _warnEnv = 1f; WarningPulses++; }
            }
            else _warnT = WarningPeriod;
            if (_pendingWarn) { _pendingWarn = false; _warnEnv = 1f; WarningPulses++; if (_warnAmt < 1f) _warnAmt = 1f; }
        }

        // ------------------------------------------------------------ bus control

        void ResetBus()
        {
            for (int v = 0; v < MaxGrains; v++) _grains[v] = default;
            for (int v = 0; v < MaxNoise; v++) _noise[v] = default;
            for (int v = 0; v < MaxPlops; v++) _plops[v] = default;
            _schedCount = _liveGrains = _liveNoise = _livePlops = 0;
            _evOn = false;
            _crOn = false;
            _sprT = 0f;
            _grindSm = 0f;
            _clock = 0f;
            _busActive = false;
            ClearContinuous();
        }

        void ClearContinuous()
        {
            _contOn = false;
            _rum1 = _rum2 = _rumHp = _rumHp2 = 0f;
            _bodyLow = _bodyBand = _bedHiL = _bedLoL = _bedHiR = _bedLoR = 0f;
            _rumLvl = _rumTarget = _bodyLvl = _bodyTarget = _bedLvl = _bedTarget = 0f;
        }

        void ControlBus(float dt)
        {
            float imp = _pendingImpact;
            if (imp > 0f)
            {
                float dur = _pendingDuration;
                _pendingImpact = 0f;
                _pendingDuration = 0f;
                StartImpact(imp, dur);
            }
            float beach = _pendingBeach;
            if (beach > 0f) { _pendingBeach = 0f; StartBeached(beach); }
            float splash = _pendingSplash;
            if (splash > 0f) { _pendingSplash = 0f; StartSplash(splash, true); SplashesPlayed++; }
            if (_pendingBlow) { _pendingBlow = false; StartBlow(); }

            float grindTarget = SynthMath.Clamp01(GrindAmount);
            if (grindTarget > 0f || _grindSm > 0f)
            {
                _grindSm += (grindTarget - _grindSm) * SynthMath.SmoothCoef(grindTarget > _grindSm ? 0.12f : 0.35f, dt);
                if (grindTarget <= 0f && _grindSm < 0.003f) _grindSm = 0f;
            }

            bool any = _evOn || _grindSm > 0f || _schedCount > 0 || _sprT > 0f || _crOn || _contOn
                       || _liveGrains > 0 || _liveNoise > 0 || _livePlops > 0;
            if (!any)
            {
                _busActive = false;
                _clock = 0f;
                return;
            }
            _busActive = true;
            _clock += dt;
            FireScheduled();

            float g = 0f, rEnv = 0f, I = _evInt;
            if (_evOn)
            {
                _evT += dt;
                float t = _evT, D = _evDur;
                if (t >= _evEnd) _evOn = false;
                else if (t < D)
                {
                    g = 0.3f + 0.7f * Smooth01(t / 0.4f);
                    rEnv = Smooth01(t / (0.55f * D));
                }
                else
                {
                    g = (float)Math.Exp(-(t - D) / 0.5f);
                    rEnv = (float)Math.Exp(-(t - D) / 0.7f);
                }

                if (_evOn && (Layers & LayerDebris) != 0 && t > 0.7f * D)
                {
                    float fade = 1f - (t - D) / _evTail;
                    if (fade > 1f) fade = 1f;
                    if (fade > 0f)
                    {
                        float rise = (t - 0.7f * D) / 0.3f;
                        if (rise > 1f) rise = 1f;
                        float e = (5f + 20f * I) * rise * fade * (float)Math.Sqrt(fade) * dt;
                        if (_rng.Next01() < e)
                        {
                            float v = _rng.Next01();
                            SpawnGrain(1200f * (float)Math.Pow(2.75, _rng.Next01()), 0.003f + 0.006f * _rng.Next01(),
                                DebrisLevel * (0.5f + 0.5f * I) * (0.25f + 0.75f * v * v), 0.95f * _rng.Next(), _rng.Range(Block), 0f);
                        }
                    }
                }
            }

            float evAmt = 0f, evDens = 0f, evBright = 0f;
            if (_evOn && (Layers & LayerCrunch) != 0)
            {
                evAmt = (0.45f + 0.55f * I) * (0.35f + 0.65f * g);
                evDens = (100f + 400f * I) * g;
                evBright = g * (0.55f + 0.45f * I);
            }
            float gr = _grindSm;
            float grAmt = gr * (0.45f + 0.3f * gr), grDens = gr * (80f + 170f * gr), grBright = 0.5f * gr;
            float amt = evAmt > grAmt ? evAmt : grAmt;
            float dens = evDens > grDens ? evDens : grDens;
            float bright = evBright > grBright ? evBright : grBright;

            if (dens > 0.5f)
            {
                // Stick-slip: the grind comes in irregular surges rather than as a steady hiss.
                _slipHold -= dt;
                if (_slipHold <= 0f)
                {
                    _slipHold = 0.03f + 0.12f * _rng.Next01();
                    float v = _rng.Next01();
                    _slipTarget = 0.1f + 1.8f * v * v;
                }
                _slip += (_slipTarget - _slip) * SynthMath.SmoothCoef(0.02f, dt);
                float e = dens * _slip * dt;
                while (e > 0f)
                {
                    if (_rng.Next01() < e) SpawnCrunch(amt, bright, _rng.Range(Block), _evOn ? 1f : 0.5f);
                    e -= 1f;
                }
            }

            if (_sprT > 0f)
            {
                _sprT -= dt;
                if (_rng.Next01() < _sprDens * dt)
                    SpawnGrain(_sprLoHz * (float)Math.Pow(_sprHiHz / _sprLoHz, _rng.Next01()),
                        _sprT60Lo + (_sprT60Hi - _sprT60Lo) * _rng.Next01(), _sprAmp * (0.3f + 0.7f * _rng.Next01()),
                        0.8f * _rng.Next(), _rng.Range(Block), 0f);
            }

            float rumble = 0f;
            if ((Layers & LayerRumble) != 0)
            {
                float evR = _evOn ? (0.25f + 0.75f * I) * rEnv : 0f;
                rumble = evR > 0.22f * gr ? evR : 0.22f * gr;
            }
            if (rumble > 1e-4f || amt > 1e-4f || _contOn)
            {
                _tremP1 += 2.6f * dt; if (_tremP1 >= 1f) _tremP1 -= 1f;
                _tremP2 += 4.1f * dt; if (_tremP2 >= 1f) _tremP2 -= 1f;
                float trem = 1f - 0.4f * (0.5f + 0.5f * (0.6f * SynthMath.Sine(_tremP1) + 0.4f * SynthMath.Sine(_tremP2)));
                float slipN = _slip * (1f / 1.9f);
                _rumTarget = RumbleLevel * _rumNorm * rumble * trem;
                _bodyTarget = BodyBandLevel * _bodyNorm * rumble * (0.55f + 0.45f * slipN);
                float bedHz = 500f + 900f * bright;
                // Friction bed = difference of two one-pole low-passes; its variance for white input has a closed form.
                float ca = SynthMath.OnePoleCoef(1.7f * bedHz, _sr), cb = SynthMath.OnePoleCoef(0.6f * bedHz, _sr);
                float bedVar = (ca / (2f - ca) + cb / (2f - cb) - 2f * ca * cb / (ca + cb - ca * cb)) * (1f / 6f);
                _bedCHi = ca;
                _bedCLo = cb;
                _bedTarget = BedLevel / (float)Math.Sqrt(bedVar) * amt * (0.3f + 0.7f * slipN);
                bool live = _rumTarget > 1e-5f || _bodyTarget > 1e-5f || _bedTarget > 1e-5f
                            || _rumLvl > 1e-5f || _bodyLvl > 1e-5f || _bedLvl > 1e-5f;
                if (live) _contOn = true;
                else if (_contOn) ClearContinuous();
            }

            if (_crOn)
            {
                _crT += dt;
                if (_crT >= _crDur) { _crOn = false; _crLow = _crBand = _crLvl = _crTarget = 0f; }
                else
                {
                    float k = _crT / _crDur;
                    _crWob += (_rng.Next() - _crWob) * 0.2f;
                    _crInc = (_crF0 + (_crF1 - _crF0) * k) * (1f + 0.07f * _crWob) * _invSr;
                    _crTarget = _crAmp * SynthMath.Sine(0.5f * k);
                }
            }
        }

        static float Smooth01(float x)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            return x * x * (3f - 2f * x);
        }

        void Schedule(int kind, float delay, float arg)
        {
            if (_schedCount >= MaxSched) return;
            _schedKind[_schedCount] = kind;
            _schedTime[_schedCount] = _clock + delay;
            _schedArg[_schedCount] = arg;
            _schedCount++;
        }

        void Unschedule(int kindA, int kindB)
        {
            int w = 0;
            for (int k = 0; k < _schedCount; k++)
            {
                if (_schedKind[k] == kindA || _schedKind[k] == kindB) continue;
                _schedKind[w] = _schedKind[k]; _schedTime[w] = _schedTime[k]; _schedArg[w] = _schedArg[k];
                w++;
            }
            _schedCount = w;
        }

        void FireScheduled()
        {
            int k = 0;
            while (k < _schedCount)
            {
                if (_schedTime[k] > _clock) { k++; continue; }
                int kind = _schedKind[k];
                float arg = _schedArg[k];
                int last = --_schedCount;
                _schedKind[k] = _schedKind[last]; _schedTime[k] = _schedTime[last]; _schedArg[k] = _schedArg[last];
                switch (kind)
                {
                    case KindCrack: SpawnCrack(arg); break;
                    case KindPlop: SpawnWaterPlop(arg); break;
                    case KindSplash: StartSplash(arg, false); break;
                    case KindCreak: StartCreak(0.45f + 0.3f * _rng.Next01(), 125f, 72f, 950f, arg); break;
                    case KindSprinkle: Sprinkle(0.9f, 34f, 1500f, 3200f, 0.006f, 0.014f, arg); break;
                }
            }
        }

        // ------------------------------------------------------------ events

        void StartImpact(float I, float duration)
        {
            if (duration <= 0f) duration = DefaultImpactSeconds(I);
            if (duration < 0.5f) duration = 0.5f; else if (duration > 6f) duration = 6f;
            _busActive = true;
            _evOn = true;
            _evT = 0f;
            _evInt = I;
            _evDur = duration;
            _evTail = 2f + I;
            _evEnd = duration + _evTail;
            LastImpactSeconds = _evEnd;
            _slipHold = 0f;
            _slip = 1f;
            Unschedule(KindCrack, KindPlop);

            if ((Layers & LayerBody) != 0)
            {
                float lvl = BodyLevel * (0.5f + 0.5f * I);
                StartNoise(170f + 60f * _rng.Next01(), 1f, 0f, 0.9f, 0.5f, 0.003f, 0f, 0.05f + 0.04f * I, lvl, 0f);
                StartPlop(60f - 12f * I, 0.999995f, 0.004f, 0.06f + 0.035f * I, lvl * 1.3f, 0f);
            }
            if ((Layers & LayerCrunch) != 0)
            {
                int n = 5 + (int)(14f * I);
                for (int k = 0; k < n; k++)
                    SpawnCrunch(0.6f + 0.4f * I, 0.8f, (int)(0.03f * _sr * _rng.Next01()), 1f);
            }
            if ((Layers & LayerCracks) != 0)
            {
                int n = 3 + (int)(5f * I + _rng.Next01());
                if (n > 8) n = 8;
                float window = duration < 1f ? duration : 1f;
                float amp = 0.35f + 0.65f * I;
                Schedule(KindCrack, 0.02f * _rng.Next01(), amp);
                for (int k = 1; k < n; k++)
                {
                    float u = _rng.Next01();
                    Schedule(KindCrack, 0.06f + (window - 0.06f) * u * (float)Math.Sqrt(u), amp * (0.55f + 0.45f * _rng.Next01()));
                }
            }
            if ((Layers & LayerDebris) != 0)
            {
                int n = 1 + (int)(3.2f * I + _rng.Next01());
                for (int k = 0; k < n; k++)
                    Schedule(KindPlop, 0.5f * duration + (0.5f * duration + 0.8f * _evTail) * _rng.Next01(), 0.5f + 0.5f * I);
            }
            ImpactsPlayed++;
        }

        void StartBeached(float I)
        {
            _busActive = true;
            float lvl = OneShotLevel * (0.5f + 0.5f * I);
            StartCreak(0.3f + 0.25f * _rng.Next01(), 66f, 108f, 760f, lvl);
            Schedule(KindCreak, 0.28f + 0.15f * _rng.Next01(), lvl * 0.8f);
            Sprinkle(0.45f + 0.3f * I, 330f, 1200f, 3000f, 0.002f, 0.006f, lvl * 0.55f);
            StartNoise(1900f, 0.9992f, 1300f, 1f, 0f, 0.05f, 0.18f + 0.15f * I, 0.14f, lvl * 0.22f, 0.3f * _rng.Next());
            Schedule(KindSplash, 0.12f + 0.1f * _rng.Next01(), 0.2f + 0.25f * I);
            BeachingsPlayed++;
        }

        void StartSplash(float amount, bool big)
        {
            _busActive = true;
            float lvl = OneShotLevel * amount;
            float pan = 0.4f * _rng.Next();
            StartNoise(850f + 300f * _rng.Next01(), 0.9985f, 420f, 1.1f, 0.5f, 0.004f + 0.012f * amount, 0.02f, 0.09f + 0.22f * amount, lvl * 0.75f, pan);
            StartNoise(2000f + 400f * _rng.Next01(), 0.9995f, 1400f, 0.9f, 0f, 0.03f, 0.05f + 0.1f * amount, 0.2f + 0.35f * amount, lvl * 0.3f, -pan);
            if (amount > 0.45f) StartPlop(74f, 0.99999f, 0.006f, 0.08f, lvl * 0.9f, 0f);
            int n = big ? 1 + (int)(3f * amount + _rng.Next01()) : 1;
            for (int k = 0; k < n; k++) Schedule(KindPlop, 0.08f + 0.6f * _rng.Next01(), 0.4f + 0.6f * amount);
            if (big) Schedule(KindSprinkle, 0.2f, lvl * 0.4f);
        }

        void StartBlow()
        {
            _busActive = true;
            float lvl = OneShotLevel;
            StartNoise(1700f, 0.9991f, 1050f, 0.8f, 0f, 0.05f, 0.3f, 0.35f, lvl * 0.3f, 0.2f * _rng.Next());
            StartNoise(520f, 0.9996f, 380f, 0.6f, 0.3f, 0.03f, 0.15f, 0.2f, lvl * 0.2f, 0f);
            Schedule(KindSprinkle, 0.35f, lvl * 0.3f);
            BlowsPlayed++;
        }

        void Sprinkle(float seconds, float density, float loHz, float hiHz, float t60Lo, float t60Hi, float amp)
        {
            _sprT = seconds; _sprDens = density; _sprLoHz = loHz; _sprHiHz = hiHz;
            _sprT60Lo = t60Lo; _sprT60Hi = t60Hi; _sprAmp = amp;
        }

        void StartCreak(float seconds, float fromHz, float toHz, float formantHz, float amp)
        {
            _crOn = true;
            _crT = 0f;
            _crDur = seconds;
            _crF0 = fromHz * (0.9f + 0.2f * _rng.Next01());
            _crF1 = toHz * (0.9f + 0.2f * _rng.Next01());
            _crF = SynthMath.SvfCoef(formantHz * (0.9f + 0.2f * _rng.Next01()), _sr);
            _crInc = _crF0 * _invSr;
            _crP = 0f;
            _crCyc = 1f;
            _crLow = _crBand = _crLvl = 0f;
            _crAmp = amp * 0.9f;
        }

        // ------------------------------------------------------------ voices

        void SpawnCrunch(float amt, float bright, int delay, float cap)
        {
            // Pareto amplitudes: ~5 % of the grains reach full level and read as cracks, the median is 1/5 of that.
            float a = 0.11f * (float)Math.Pow(_rng.Next01() + 1e-4f, -0.75);
            if (a > cap) a = cap;
            float top = 1300f + 2200f * bright;
            float hz = 330f * (float)Math.Pow(top / 330f, _rng.Next01()) * (1f - 0.4f * a);
            // Rock is heavily damped: 3-28 cycles to -60 dB (Q 1.4-13), so low grains thud and high ones tick
            // instead of ringing like glass; big grains ring a little longer.
            float v = _rng.Next01();
            float t60 = (3f + 25f * v * v * v) / hz * (1f + 0.8f * a);
            if (t60 < 0.003f) t60 = 0.003f; else if (t60 > 0.06f) t60 = 0.06f;
            // Energy per grain goes with its ring time (~1/hz); the tilt keeps the 1-3 kHz "crunch" legible.
            float tilt = (float)Math.Pow(hz * (1f / 700f), 0.6);
            if (tilt < 0.7f) tilt = 0.7f; else if (tilt > 2f) tilt = 2f;
            float pan = _rng.Next();
            SpawnGrain(hz, t60, CrunchLevel * amt * a * tilt, 0.95f * pan * (1.5f - 0.5f * pan * pan), delay, 0f);
        }

        void SpawnCrack(float amp)
        {
            float u = _rng.Next01();
            float hz = 800f * (float)Math.Pow(3.125, u);
            float t60 = 0.02f + (0.04f - 0.025f * u) * _rng.Next01();
            float drop = 0.08f + 0.1f * _rng.Next01();
            float glide = (float)Math.Pow(1.0 - drop, Block / (double)(t60 * _sr));
            float pan = 0.6f * _rng.Next();
            float a = CrackLevel * amp;
            int delay = _rng.Range(Block);
            SpawnGrain(hz, t60, a, pan, delay, glide);
            SpawnGrain(hz * 1.53f, t60 * 0.7f, a * 0.5f, pan, delay, glide);
            if (hz * 2.31f < 3900f) SpawnGrain(hz * 2.31f, t60 * 0.5f, a * 0.28f, pan, delay, glide);
            SpawnGrain(2400f + 1100f * _rng.Next01(), 0.003f, a * 0.4f, pan, delay, 0f);
            CracksPlayed++;
        }

        void SpawnGrain(float hz, float t60, float amp, float pan, int delay, float glide)
        {
            int slot = 0, least = int.MaxValue;
            for (int v = 0; v < MaxGrains; v++)
            {
                int life = _grains[v].life;
                if (life <= 0) { slot = v; break; }
                if (life < least) { least = life; slot = v; }
            }
            ref Grain g = ref _grains[slot];
            if (hz > 0.2f * _sr) hz = 0.2f * _sr;
            int lifeSamples = (int)(t60 * _sr);
            if (lifeSamples < 48) lifeSamples = 48;
            float w = 2f * (float)Math.PI * hz * _invSr;
            float r = (float)Math.Exp(-6.9 / lifeSamples);
            int m = (int)(_sr * (0.0003f + 0.0012f * _rng.Next01()));
            if (m < 4) m = 4;
            g.r = r; g.w = w; g.glide = glide;
            g.a1 = 2f * r * (float)Math.Cos(w);
            g.a2 = -r * r;
            g.y1 = g.y2 = 0f;
            g.excLen = m;
            g.exc = amp * (float)Math.Sin(w) * 3f / (float)Math.Sqrt(m) * 1.4142f;
            g.excDec = g.exc / m;
            g.life = lifeSamples;
            g.delay = delay;
            g.nzOff = _rng.Range(Block);
            SynthMath.Pan(pan, out g.gl, out g.gr);
            _liveGrains++;
            GrainsPlayed++;
        }

        void StartNoise(float hz, float fMulPerBlock, float minHz, float q, float lowMix, float attack, float hold, float tau, float rms, float pan)
        {
            int slot = 0;
            float least = float.MaxValue;
            for (int v = 0; v < MaxNoise; v++)
            {
                if (!_noise[v].on) { slot = v; break; }
                if (_noise[v].lvl < least) { least = _noise[v].lvl; slot = v; }
            }
            ref NoiseVoice nv = ref _noise[slot];
            nv.on = true;
            nv.state = 1;
            nv.low = nv.band = nv.lvl = 0f;
            nv.f = SynthMath.SvfCoef(hz, _sr);
            nv.fMul = fMulPerBlock;
            nv.fMin = SynthMath.SvfCoef(minHz > 0f ? minHz : hz, _sr);
            nv.q = q;
            nv.lowMix = lowMix;
            nv.atkInc = 1f / (Math.Max(0.001f, attack) * _sr);
            nv.hold = (int)(hold * _sr);
            nv.dec = SynthMath.DecayCoef(tau, _sr);
            nv.amp = rms * BandNorm(hz, q);
            SynthMath.Pan(pan, out nv.gl, out nv.gr);
            nv.gl *= 1.4142f; nv.gr *= 1.4142f;
            _liveNoise++;
        }

        void SpawnWaterPlop(float amount)
        {
            float hz = 260f + 280f * _rng.Next01();
            float t = 0.05f + 0.06f * _rng.Next01();
            float rise = 1.6f + 0.6f * _rng.Next01();
            StartPlop(hz, (float)Math.Pow(rise, 1.0 / (t * _sr)), 0.003f, t * 0.4f,
                DebrisLevel * 0.8f * amount * (0.6f + 0.4f * _rng.Next01()), 0.7f * _rng.Next());
            PlopsPlayed++;
        }

        void StartPlop(float hz, float mulPerSample, float attack, float tau, float amp, float pan)
        {
            int slot = 0;
            float least = float.MaxValue;
            for (int v = 0; v < MaxPlops; v++)
            {
                if (!_plops[v].on) { slot = v; break; }
                if (_plops[v].env < least) { least = _plops[v].env; slot = v; }
            }
            ref Plop p = ref _plops[slot];
            p.on = true;
            p.state = 1;
            p.p = 0f;
            p.inc = hz * _invSr;
            p.mul = mulPerSample;
            p.env = 0f;
            p.atkInc = 1f / (attack * _sr);
            p.dec = SynthMath.DecayCoef(tau, _sr);
            p.amp = amp * 1.4142f;
            SynthMath.Pan(pan, out p.gl, out p.gr);
            _livePlops++;
        }

        // ------------------------------------------------------------ render

        void RenderBlock(int n)
        {
            float[] L = _mixL, R = _mixR, nz = _nz;
            float wc = _wCoef, wAmp = _wAmp;
            float lp1L = _wLp1L, lp2L = _wLp2L, lp1R = _wLp1R, lp2R = _wLp2R;
            float whLow = _whLow, whBand = _whBand, whF = _whF, whAmp = _whAmp;
            float waLpL = _waLpL, waHpL = _waHpL, waLpR = _waLpR, waHpR = _waHpR, wlc = _waLpCoef, whc = _waHpCoef, waAmp = _waAmp;
            bool warn = _warnAmt > 1e-4f || _warnLvl > 1e-4f;
            float wP = _warnP, wInc = _warnInc, wEnv = _warnEnv, wLvl = _warnLvl, wDec = _warnDecay, wGain = WarningGain * _warnAmt;

            for (int i = 0; i < n; i++)
            {
                float xL = _nL.Next(), xR = _nR.Next();

                lp1L += wc * (xL - lp1L); lp2L += wc * (lp1L - lp2L);
                lp1R += wc * (xR - lp1R); lp2R += wc * (lp1R - lp2R);
                float outL = lp2L * wAmp, outR = lp2R * wAmp;

                float xm = 0.5f * (xL + xR);
                nz[i] = xm;
                whLow += whF * whBand;
                float whHigh = xm - whLow - 0.15f * whBand;
                whBand += whF * whHigh;
                float wh = whBand * whAmp;
                outL += wh; outR += wh;

                waLpL += wlc * (xL - waLpL); waHpL += whc * (waLpL - waHpL);
                waLpR += wlc * (xR - waLpR); waHpR += whc * (waLpR - waHpR);
                outL += (waLpL - waHpL) * waAmp;
                outR += (waLpR - waHpR) * waAmp;

                if (warn)
                {
                    wLvl += (wEnv - wLvl) * 0.0005f;
                    wEnv *= wDec;
                    float s = (SynthMath.Sine(wP) + 0.5f * SynthMath.Sine(wP * 2f)) * wLvl * wGain;
                    wP += wInc; if (wP >= 1f) wP -= 1f;
                    outL += s; outR += s;
                }
                L[i] = outL;
                R[i] = outR;
            }

            _wLp1L = lp1L; _wLp2L = lp2L; _wLp1R = lp1R; _wLp2R = lp2R;
            _whLow = whLow; _whBand = whBand;
            _waLpL = waLpL; _waHpL = waHpL; _waLpR = waLpR; _waHpR = waHpR;
            _warnP = wP; _warnEnv = wEnv; _warnLvl = wLvl;

            float vol = Volume;
            if (!_busActive)
            {
                // SoftClip is linear below 0.6 and too large for Mono to inline: only the rare loud sample pays the call.
                for (int i = 0; i < n; i++)
                {
                    float l = L[i] * vol, r = R[i] * vol;
                    L[i] = l > 0.6f || l < -0.6f ? SynthMath.SoftClip(l) : l;
                    R[i] = r > 0.6f || r < -0.6f ? SynthMath.SoftClip(r) : r;
                }
                return;
            }

            float[] bL = _busL, bR = _busR;
            if (_contOn) RenderContinuous(n, bL, bR, nz);
            else for (int i = 0; i < n; i++) { bL[i] = 0f; bR[i] = 0f; }

            if (_liveGrains > 0) RenderGrains(n, bL, bR, nz);
            if (_liveNoise > 0) RenderNoise(n, bL, bR, nz);
            if (_livePlops > 0) RenderPlops(n, bL, bR);
            if (_crOn) RenderCreak(n, bL, bR);

            // The bus is limited on its own (< 0.85) so that wind and water on top still end below 0.9.
            float ig = ImpactGain;
            for (int i = 0; i < n; i++)
            {
                float l = bL[i] * ig, r = bR[i] * ig;
                if (l > 0.6f || l < -0.6f) l = SynthMath.SoftClip(l);
                if (r > 0.6f || r < -0.6f) r = SynthMath.SoftClip(r);
                l = (L[i] + 0.85f * l) * vol;
                r = (R[i] + 0.85f * r) * vol;
                L[i] = l > 0.6f || l < -0.6f ? SynthMath.SoftClip(l) : l;
                R[i] = r > 0.6f || r < -0.6f ? SynthMath.SoftClip(r) : r;
            }
        }

        // Rumble and body band are mono (centred); the friction bed is one band of noise per ear.
        void RenderContinuous(int n, float[] bL, float[] bR, float[] nz)
        {
            float inv = 1f / n;
            if (_rumLvl > 1e-5f || _rumTarget > 1e-5f || _bodyLvl > 1e-5f || _bodyTarget > 1e-5f)
            {
                float a = _rum1, b = _rum2, hp = _rumHp, hp2 = _rumHp2, c1 = _rumC1, c2 = _rumC2, ch = _rumCHp;
                float rl = _rumLvl, drl = (_rumTarget - _rumLvl) * inv;
                float boLow = _bodyLow, boBand = _bodyBand, boF = _bodyF, bol = _bodyLvl, dbol = (_bodyTarget - _bodyLvl) * inv;
                drl *= 2f; dbol *= 2f;
                for (int i = 0; i < n; i += 2)
                {
                    int j = i + 1 < n ? i + 1 : i;
                    float x = 0.5f * (nz[i] + nz[j]);
                    a += c1 * (x - a);
                    b += c2 * (a - b);
                    hp += ch * (b - hp);
                    float rum = b - hp;
                    hp2 += ch * (rum - hp2);
                    rum -= hp2;
                    boLow += boF * boBand;
                    boBand += boF * (x - boLow - 0.5f * boBand);
                    float centre = rum * rl + boBand * bol;
                    bL[i] = centre; bR[i] = centre;
                    bL[j] = centre; bR[j] = centre;
                    rl += drl; bol += dbol;
                }
                _rum1 = a; _rum2 = b; _rumHp = hp; _rumHp2 = hp2;
                _bodyLow = boLow; _bodyBand = boBand;
            }
            else
            {
                for (int i = 0; i < n; i++) { bL[i] = 0f; bR[i] = 0f; }
                _rum1 = _rum2 = _rumHp = _rumHp2 = _bodyLow = _bodyBand = 0f;
            }

            // The bed reads the block backwards (left) and as a difference (right): same generator, unrelated signals.
            if (_bedLvl > 1e-5f || _bedTarget > 1e-5f)
            {
                float hiL = _bedHiL, loL = _bedLoL, hiR = _bedHiR, loR = _bedLoR, cHi = _bedCHi, cLo = _bedCLo;
                float bel = _bedLvl, dbel = (_bedTarget - _bedLvl) * inv;
                for (int i = 0; i < n; i++)
                {
                    float x = nz[i], xb = nz[n - 1 - i], xr = (x - xb) * 0.7071f;
                    hiL += cHi * (xb - hiL); loL += cLo * (xb - loL);
                    hiR += cHi * (xr - hiR); loR += cLo * (xr - loR);
                    bL[i] += (hiL - loL) * bel;
                    bR[i] += (hiR - loR) * bel;
                    bel += dbel;
                }
                _bedHiL = hiL; _bedLoL = loL; _bedHiR = hiR; _bedLoR = loR;
            }
            else _bedHiL = _bedLoL = _bedHiR = _bedLoR = 0f;
            _rumLvl = _rumTarget; _bodyLvl = _bodyTarget; _bedLvl = _bedTarget;
        }

        void RenderGrains(int n, float[] bL, float[] bR, float[] nz)
        {
            int live = 0;
            for (int v = 0; v < MaxGrains; v++)
            {
                if (_grains[v].life <= 0) continue;
                ref Grain g = ref _grains[v];
                int i = g.delay;
                if (i >= n) { g.delay = i - n; live++; continue; }
                g.delay = 0;
                if (g.glide != 0f)
                {
                    g.w *= g.glide;
                    g.a1 = 2f * g.r * (float)Math.Cos(g.w);
                }
                float y1 = g.y1, y2 = g.y2, a1 = g.a1, a2 = g.a2, gl = g.gl, gr = g.gr;
                int start = i;
                if (g.excLen > 0)
                {
                    int m = i + g.excLen;
                    if (m > n) m = n;
                    float x = g.exc, dx = g.excDec;
                    int off = g.nzOff;
                    for (; i < m; i++)
                    {
                        float y = a1 * y1 + a2 * y2 + nz[(i + off) & (Block - 1)] * x;
                        y2 = y1; y1 = y;
                        bL[i] += y * gl; bR[i] += y * gr;
                        x -= dx;
                    }
                    g.exc = x;
                    g.excLen -= m - start;
                }
                for (; i < n; i++)
                {
                    float y = a1 * y1 + a2 * y2;
                    y2 = y1; y1 = y;
                    bL[i] += y * gl; bR[i] += y * gr;
                }
                g.life -= n - start;
                if (g.life <= 0) { g.life = 0; g.y1 = g.y2 = 0f; }
                else { g.y1 = y1; g.y2 = y2; live++; }
            }
            _liveGrains = live;
        }

        void RenderNoise(int n, float[] bL, float[] bR, float[] nz)
        {
            int live = 0;
            for (int v = 0; v < MaxNoise; v++)
            {
                if (!_noise[v].on) continue;
                ref NoiseVoice nv = ref _noise[v];
                float f = nv.f * nv.fMul;
                if (f < nv.fMin) f = nv.fMin;
                nv.f = f;
                float low = nv.low, band = nv.band, q = nv.q, lvl = nv.lvl, atk = nv.atkInc, dec = nv.dec, lowMix = nv.lowMix;
                float gl = nv.gl * nv.amp, gr = nv.gr * nv.amp;
                int state = nv.state, hold = nv.hold, off = v * 11;
                for (int i = 0; i < n; i++)
                {
                    if (state == 1) { lvl += atk; if (lvl >= 1f) { lvl = 1f; state = 2; } }
                    else if (state == 2) { if (--hold <= 0) state = 3; }
                    else lvl *= dec;
                    float x = nz[(i + off) & (Block - 1)];
                    low += f * band;
                    band += f * (x - low - q * band);
                    float s = (band + lowMix * low) * lvl;
                    bL[i] += s * gl; bR[i] += s * gr;
                }
                if (state == 3 && lvl < 1e-3f) nv = default;
                else
                {
                    nv.low = low; nv.band = band; nv.lvl = lvl; nv.state = state; nv.hold = hold;
                    live++;
                }
            }
            _liveNoise = live;
        }

        void RenderPlops(int n, float[] bL, float[] bR)
        {
            int live = 0;
            for (int v = 0; v < MaxPlops; v++)
            {
                if (!_plops[v].on) continue;
                ref Plop pl = ref _plops[v];
                float p = pl.p, inc = pl.inc, mul = pl.mul, env = pl.env, atk = pl.atkInc, dec = pl.dec;
                float gl = pl.gl * pl.amp, gr = pl.gr * pl.amp;
                int state = pl.state;
                for (int i = 0; i < n; i++)
                {
                    if (state == 1) { env += atk; if (env >= 1f) { env = 1f; state = 2; } }
                    else env *= dec;
                    float s = SynthMath.Sine(p) * env;
                    bL[i] += s * gl; bR[i] += s * gr;
                    p += inc; if (p >= 1f) p -= 1f;
                    inc *= mul;
                }
                if (state == 2 && env < 1e-3f) pl = default;
                else
                {
                    pl.p = p; pl.inc = inc; pl.env = env; pl.state = state;
                    live++;
                }
            }
            _livePlops = live;
        }

        // Stick-slip wood: a narrow pulse train whose pitch wanders, with dropped cycles, through one formant.
        void RenderCreak(int n, float[] bL, float[] bR)
        {
            float p = _crP, inc = _crInc, cyc = _crCyc, low = _crLow, band = _crBand, f = _crF;
            float lvl = _crLvl, dl = (_crTarget - _crLvl) / n;
            for (int i = 0; i < n; i++)
            {
                p += inc;
                if (p >= 1f) { p -= 1f; cyc = _rng.Next01() < 0.18f ? 0.3f : 1f; }
                float x = (p < 0.12f ? 1f - p * 8.333f : 0f) * cyc - 0.06f * cyc;
                low += f * band;
                band += f * (x - low - 0.22f * band);
                float s = (band + 0.3f * low) * lvl;
                bL[i] += s; bR[i] += s;
                lvl += dl;
            }
            _crP = p; _crCyc = cyc; _crLow = low; _crBand = band; _crLvl = _crTarget;
        }
    }
}
