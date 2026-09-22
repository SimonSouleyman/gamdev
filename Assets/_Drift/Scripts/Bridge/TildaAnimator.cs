using UnityEngine;

namespace Drift.Bridge
{
    // What her face does, as plain numbers; every pose has its own and the animator eases between them.
    public struct TildaFace
    {
        // Brows: lift in model units, tilt in degrees (positive: inner end up, the kind / worried way).
        public float browLiftL, browLiftR, browTiltL, browTiltR;
        // Upper lids 0 (shut) .. 1 (wide open), lower lids 0 (down) .. 1 (pushed up by a big smile).
        public float lidL, lidR, lowerLid;
        // Where she looks, -1..1: +x is the viewer's right, +y up.
        public Vector2 look;
        // Mouth: width factor, curvature of the smile (1 = her usual smile, 0 = flat).
        public float mouthWidth, smile;
        // Glasses slipping down her nose, in model units.
        public float glassesSlip;

        public static TildaFace For(TildaPose pose, float side)
        {
            var f = new TildaFace { lidL = 0.72f, lidR = 0.66f, lowerLid = 0.16f, mouthWidth = 1f, smile = 1.1f, browLiftL = 0.02f, browLiftR = 0.008f, browTiltL = 6f, browTiltR = 6f };
            switch (pose)
            {
                case TildaPose.Talk:
                    f.lidL = 0.9f; f.lidR = 0.86f; f.browLiftL = 0.03f; f.browLiftR = 0.022f; f.browTiltL = f.browTiltR = 4f;
                    break;
                case TildaPose.Wave:
                    f.lidL = f.lidR = 1f; f.browLiftL = 0.075f; f.browLiftR = 0.065f; f.browTiltL = f.browTiltR = 7f;
                    f.lowerLid = 0.3f; f.mouthWidth = 1.12f; f.smile = 1.15f;
                    break;
                case TildaPose.Cheer:
                    f.lidL = f.lidR = 0.7f; f.lowerLid = 1f; f.browLiftL = f.browLiftR = 0.09f; f.browTiltL = f.browTiltR = 10f;
                    f.mouthWidth = 1.2f; f.smile = 0.95f;
                    break;
                case TildaPose.Sleepy:
                    f.lidL = f.lidR = 0f; f.lowerLid = 0.05f; f.browLiftL = f.browLiftR = -0.035f; f.browTiltL = f.browTiltR = 9f;
                    f.mouthWidth = 0.42f; f.smile = 0.75f; f.glassesSlip = 0.085f; f.look = new Vector2(0f, -0.5f);
                    break;
                case TildaPose.Present:
                    f.lidL = f.lidR = 0.92f; f.look = new Vector2(0.75f * side, 0.1f);
                    f.browLiftL = side < 0f ? 0.085f : 0.015f; f.browLiftR = side < 0f ? 0.015f : 0.085f;
                    f.browTiltL = side < 0f ? 8f : -3f; f.browTiltR = side < 0f ? -3f : 8f;
                    f.mouthWidth = 1.05f;
                    break;
                case TildaPose.Comfort:
                    f.lidL = f.lidR = 0.62f; f.lowerLid = 0.3f; f.browLiftL = f.browLiftR = 0.035f; f.browTiltL = f.browTiltR = 19f;
                    f.mouthWidth = 0.78f; f.smile = 0.7f; f.look = new Vector2(0f, -0.15f);
                    break;
            }
            return f;
        }

        public static TildaFace Lerp(TildaFace a, TildaFace b, float k)
        {
            return new TildaFace
            {
                browLiftL = Mathf.Lerp(a.browLiftL, b.browLiftL, k), browLiftR = Mathf.Lerp(a.browLiftR, b.browLiftR, k),
                browTiltL = Mathf.Lerp(a.browTiltL, b.browTiltL, k), browTiltR = Mathf.Lerp(a.browTiltR, b.browTiltR, k),
                lidL = Mathf.Lerp(a.lidL, b.lidL, k), lidR = Mathf.Lerp(a.lidR, b.lidR, k), lowerLid = Mathf.Lerp(a.lowerLid, b.lowerLid, k),
                look = Vector2.Lerp(a.look, b.look, k),
                mouthWidth = Mathf.Lerp(a.mouthWidth, b.mouthWidth, k), smile = Mathf.Lerp(a.smile, b.smile, k),
                glassesSlip = Mathf.Lerp(a.glassesSlip, b.glassesSlip, k),
            };
        }
    }

    // Poses Tilda purely through her part transforms; the caller supplies the clock (unscaled time), so she keeps
    // moving while Time.timeScale is 0. snap skips the easing and the blink for a still picture.
    public sealed class TildaAnimator
    {
        const float RestArm = -40f;
        const float LidShut = -78f, LidOpen = 80f, LowDown = -74f, LowUp = 34f;
        const float LookYaw = 22f, LookPitch = 15f, Converge = 17f, RestPitch = 10f;
        const float GlancePeriod = 5.3f, GlintPeriod = 7.4f, GlintSeconds = 0.55f;
        const float SweepPeriod = 3.3f, SweepSeconds = 0.6f;

        readonly TildaParts _p;
        float _armL = RestArm, _armR = RestArm, _open;
        float _nextBlink = -1f, _blinkUntil, _yaw;
        int _blinks;
        TildaFace _face = TildaFace.For(TildaPose.Idle, 1f);

        // 0..1 loudness of what Tilda grumbles right now; below 0 the mouth runs on the talk cycle.
        public float VoiceLevel = -1f;
        // Where the thing she presents is: +1 on the viewer's right, -1 on the left.
        public float PresentSide = 1f;

        public TildaFace Face => _face;
        public float MouthOpen => _open;

        public TildaAnimator(TildaParts parts)
        {
            _p = parts;
        }

        static float Smooth(float from, float to, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, x));

        static float Hash01(int n)
        {
            uint h = (uint)n * 2654435761u;
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
            return (h & 0xFFFF) / 65535f;
        }

        public void Apply(TildaPose pose, bool talking, float t, float dt, bool snap)
        {
            bool sleepy = pose == TildaPose.Sleepy, cheer = pose == TildaPose.Cheer, wave = pose == TildaPose.Wave;
            bool present = pose == TildaPose.Present, comfort = pose == TildaPose.Comfort;
            bool voiced = VoiceLevel >= 0f;
            float side = PresentSide < 0f ? -1f : 1f;
            talking = (talking || pose == TildaPose.Talk) && (!sleepy || voiced);

            float breathe = Mathf.Sin(t * (sleepy ? 1.15f : 2.1f));
            float amp = sleepy ? 0.034f : 0.02f;
            float jump = cheer ? Mathf.Abs(Mathf.Sin(t * 5.4f)) : 0f;
            float squash = cheer ? 0.07f * (0.45f - jump) : 0f;
            _p.body.localScale = new Vector3(1f + amp * breathe + squash, 1f - amp * 1.3f * breathe - squash * 1.2f, 1f + amp * breathe + squash);
            _p.root.localPosition = Vector3.up * (0.025f * Mathf.Sin(t * 2.1f + 0.6f) + 0.24f * jump);

            float nod = sleepy ? 0f : comfort ? -3f + 1.6f * Mathf.Sin(t * 1.6f) : talking ? 2.6f * Mathf.Sin(t * 5.2f) : 0f;
            // She faces -Z, so a negative yaw turns her face to the viewer's right.
            float yawTarget = sleepy ? 0f : present ? -side * 17f + 3f * Mathf.Sin(t * 0.8f) : 5f * Mathf.Sin(t * 0.8f);
            float tilt = sleepy ? 3f + Mathf.Sin(t * 1.15f) : wave ? -3f : present ? -side * 2.5f : comfort ? 4.5f + 1.2f * Mathf.Sin(t * 1.1f) : 1.5f * Mathf.Sin(t * 1.3f);
            _yaw = snap ? yawTarget : Mathf.Lerp(_yaw, yawTarget, 1f - Mathf.Exp(-7f * Mathf.Max(0f, dt)));
            _p.root.localRotation = Quaternion.Euler(nod, _yaw, tilt);

            float targetL = RestArm + 3f * breathe, targetR = targetL;
            if (wave) targetR = 58f + 22f * Mathf.Sin(t * 8.5f);
            else if (present)
            {
                float reach = 14f + 5f * Mathf.Sin(t * 1.7f) + (talking ? 7f * Mathf.Sin(t * 3.4f) : 0f);
                if (side > 0f) targetR = reach;
                else targetL = reach;
            }
            else if (comfort) targetL = targetR = -12f + 4f * Mathf.Sin(t * 1.4f);
            else if (cheer)
            {
                targetR = 60f + 10f * Mathf.Sin(t * 13f);
                targetL = 60f - 10f * Mathf.Sin(t * 13f);
            }
            else if (sleepy) targetL = targetR = -58f + 2f * breathe;

            float cycle = 0.12f + 0.88f * Mathf.Abs(Mathf.Sin(t * 9.5f)) * (0.65f + 0.35f * Mathf.Sin(t * 3.1f));
            float spoken = voiced ? Mathf.Clamp01(VoiceLevel) : cycle;
            // Asleep she breathes through a little round mouth; a grumble in her sleep opens it a bit more.
            float snore = 0.22f + 0.16f * breathe;
            float targetOpen = sleepy ? (talking ? Mathf.Max(snore, 0.5f * spoken) : snore)
                : cheer ? (voiced ? Mathf.Max(0.55f, spoken) : 1f)
                : talking ? (voiced ? 0.04f + 0.96f * spoken : cycle)
                : wave ? 0.42f : 0f;

            float ease = snap ? 1f : 1f - Mathf.Exp(-14f * Mathf.Max(0f, dt));
            float easeMouth = snap ? 1f : 1f - Mathf.Exp(-30f * Mathf.Max(0f, dt));
            float easeFace = snap ? 1f : 1f - Mathf.Exp(-9f * Mathf.Max(0f, dt));
            _armL = Mathf.Lerp(_armL, targetL, ease);
            _armR = Mathf.Lerp(_armR, targetR, ease);
            _open = Mathf.Lerp(_open, targetOpen, easeMouth);
            _p.armL.localRotation = Quaternion.Euler(0f, -12f, 0f) * Quaternion.Euler(0f, 0f, -_armL);
            _p.armR.localRotation = Quaternion.Euler(0f, 12f, 0f) * Quaternion.Euler(0f, 0f, _armR);

            var target = TildaFace.For(talking && (pose == TildaPose.Idle) ? TildaPose.Talk : pose, side);
            Liven(ref target, pose, talking, t);
            _face = TildaFace.Lerp(_face, target, easeFace);

            _p.mouth.localScale = new Vector3(_face.mouthWidth * (1f + 0.1f * _open), 1f, 1f);
            _p.smile.localScale = new Vector3(1f, _face.smile * (1f + 1.9f * _open), 1f);
            SetActive(_p.mouthFill, _open > 0.02f);
            _p.mouthFill.localScale = new Vector3(1f, _face.smile * (1f + 1.9f * _open) * Smooth(0f, 0.25f, _open), 1f);

            Eyes(t, snap, cheer || sleepy);
            Brows(t, talking);
            Glasses(t, jump, snap);
            Shades(t, jump, snap);
            Extras(t, jump, breathe);

            float pulse = Mathf.Sin(t * 3.3f);
            _p.lava.localScale = new Vector3(1f, cheer ? 2.2f + 0.4f * Mathf.Sin(t * 13f) : 1f + 0.35f * pulse, 1f);

            Puffs(t, sleepy, cheer);
            Zs(t, sleepy);
            Sparks(t, cheer);
        }

        // The small life on top of a pose: glances to the side, brows that stress what she says.
        void Liven(ref TildaFace f, TildaPose pose, bool talking, float t)
        {
            if (pose == TildaPose.Idle || pose == TildaPose.Talk)
            {
                int n = Mathf.FloorToInt(t / GlancePeriod);
                float into = t - n * GlancePeriod;
                if (into < 1.1f && Hash01(n) > 0.35f)
                {
                    // While she explains, her glances go to what she explains.
                    float dir = talking ? Mathf.Sign(PresentSide) : (Hash01(n + 77) > 0.5f ? 1f : -1f);
                    f.look = new Vector2(0.8f * dir, 0.25f * (Hash01(n + 31) - 0.4f));
                    f.browLiftL += 0.015f;
                }
            }
            if (talking && pose != TildaPose.Sleepy)
            {
                float stress = Mathf.Max(0f, Mathf.Sin(t * 2.3f)) * Mathf.Max(0f, Mathf.Sin(t * 0.9f + 1f));
                f.browLiftL += 0.04f * stress;
                f.browLiftR += 0.03f * stress;
            }
        }

        void Eyes(float t, bool snap, bool noBlink)
        {
            if (_nextBlink < 0f || _nextBlink > t + 10f) _nextBlink = t + 2.5f;
            if (!snap && !noBlink && t >= _nextBlink)
            {
                _blinkUntil = t + 0.14f;
                _blinks++;
                _nextBlink = t + 3f + 3f * Mathf.Repeat(_blinks * 0.618f, 1f);
            }
            bool blink = !snap && !noBlink && t < _blinkUntil;
            float lidL = blink ? 0.04f : _face.lidL, lidR = blink ? 0.04f : _face.lidR;
            float low = blink ? Mathf.Max(_face.lowerLid, 0.3f) : _face.lowerLid;

            // Kind eyes: the lids slope down a little towards the outside.
            _p.lidUpL.localRotation = Quaternion.Euler(0f, 0f, -7f) * Quaternion.Euler(-Mathf.Lerp(LidShut, LidOpen, lidL), 0f, 0f);
            _p.lidUpR.localRotation = Quaternion.Euler(0f, 0f, 7f) * Quaternion.Euler(-Mathf.Lerp(LidShut, LidOpen, lidR), 0f, 0f);
            var lowRot = Quaternion.Euler(-Mathf.Lerp(LowDown, LowUp, low), 0f, 0f);
            _p.lidLowL.localRotation = _p.lidLowR.localRotation = lowRot;

            // Eye-local +X is the viewer's left and a positive pitch looks down.
            // Each eye sits square on her round flank, so at rest the balls turn inwards to meet the viewer's gaze.
            float pitch = -_face.look.y * LookPitch + RestPitch, yaw = -_face.look.x * LookYaw;
            _p.ballL.localRotation = Quaternion.Euler(pitch, yaw - Converge, 0f);
            _p.ballR.localRotation = Quaternion.Euler(pitch, yaw + Converge, 0f);
        }

        void Brows(float t, bool talking)
        {
            float wiggle = 0.004f * Mathf.Sin(t * 1.7f);
            _p.browL.localPosition = _p.browRestL + Vector3.up * (_face.browLiftL + wiggle);
            _p.browR.localPosition = _p.browRestR + Vector3.up * (_face.browLiftR - wiggle);
            _p.browL.localRotation = _p.browRotL * Quaternion.Euler(0f, 0f, -_face.browTiltL);
            _p.browR.localRotation = _p.browRotR * Quaternion.Euler(0f, 0f, _face.browTiltR);
        }

        void Glasses(float t, float jump, bool snap)
        {
            float bounce = 0.035f * jump;
            _p.glasses.localPosition = _p.glassesRest + Vector3.down * (_face.glassesSlip - bounce);
            _p.glasses.localRotation = Quaternion.Euler(_face.glassesSlip * 90f, 0f, _face.glassesSlip > 0.02f ? -4f : 0f);

            // A streak of light wanders across both lenses every few seconds and rests small near the rim.
            float into = Mathf.Repeat(t + 1.9f, GlintPeriod);
            float s = snap || into > GlintSeconds ? 0.8f : Mathf.Lerp(-0.85f, 0.8f, Smooth(0f, GlintSeconds, into));
            float r = _p.lensRadius;
            float chord = 2f * r * Mathf.Sqrt(Mathf.Max(0.02f, 1f - s * s)) * 0.62f;
            // Glint-local +X is the viewer's left.
            var pos = new Vector3(s * r * 0.92f, 0f, 0f);
            _p.glintL.localPosition = _p.glintR.localPosition = pos;
            _p.glintL.localScale = _p.glintR.localScale = new Vector3(1f, chord, 1f);
        }

        // The sport shades bounce on a hop, sag a little when she dozes and shimmer: the hues drift with time and
        // with the turn of her head like a mirror, and a streak flashes across.
        void Shades(float t, float jump, bool snap)
        {
            if (_p.shades == null || !_p.shades.gameObject.activeSelf) return;
            float slip = _face.glassesSlip * 0.3f;
            _p.shades.localPosition = _p.shadesRest + Vector3.down * (slip - 0.03f * jump);
            _p.shades.localRotation = Quaternion.Euler(slip * 60f, 0f, slip > 0.01f ? 3f : 0f);

            float into = Mathf.Repeat(t + 0.4f, SweepPeriod);
            float sweep = snap || into > SweepSeconds ? 3f : Mathf.Lerp(-1.7f, 1.7f, Smooth(0f, SweepSeconds, into));
            float shimmer = 0.04f * Mathf.Sin(t * 1.25f) + 0.02f * Mathf.Sin(t * 3.1f) + 0.004f * _yaw;
            var uv = _p.shadesUV;
            var colors = _p.shadesColors;
            for (int i = 0; i < colors.Length; i++) colors[i] = TildaModel.ShadesColor(uv[i], shimmer, sweep);
            _p.shadesLens.colors = colors;
        }

        void Extras(float t, float jump, float breathe)
        {
            _p.flower.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(t * 2.4f) + 0.12f * jump);
            _p.curl.localScale = new Vector3(1f, 1f + 0.05f * breathe - 0.16f * jump, 1f);
        }

        void Puffs(float t, bool sleepy, bool cheer)
        {
            float period = cheer ? 1f : 3.4f;
            int n = _p.puffs.Length;
            for (int i = 0; i < n; i++)
            {
                var puff = _p.puffs[i];
                SetActive(puff, !sleepy);
                if (sleepy) continue;
                float p = Mathf.Repeat(t / period + i / (float)n, 1f);
                float s = 0.19f * Smooth(0f, 0.18f, p) * (1f - Smooth(0.7f, 1f, p)) * (0.75f + 0.6f * p);
                puff.localPosition = new Vector3(0.1f * Mathf.Sin(p * 6f + i * 1.9f) + 0.22f * p * p, TildaModel.CraterY + 0.05f + 1.2f * p, 0.05f * Mathf.Cos(i * 2.3f));
                puff.localScale = Vector3.one * Mathf.Max(0.0001f, s);
            }
        }

        void Zs(float t, bool sleepy)
        {
            int n = _p.zs.Length;
            for (int i = 0; i < n; i++)
            {
                var z = _p.zs[i];
                SetActive(z, sleepy);
                if (!sleepy) continue;
                float p = Mathf.Repeat(t / 3.6f + i / (float)n, 1f);
                float s = (0.15f + 0.24f * p) * Smooth(0f, 0.1f, p) * (1f - Smooth(0.86f, 1f, p));
                z.localPosition = new Vector3(0.3f + 0.6f * p + 0.06f * Mathf.Sin(p * 7f), TildaModel.CraterY + 0.2f + 1.1f * p, -0.35f);
                z.localRotation = Quaternion.Euler(0f, 0f, 8f - 22f * p);
                z.localScale = Vector3.one * Mathf.Max(0.0001f, s);
            }
        }

        void Sparks(float t, bool cheer)
        {
            int n = _p.sparks.Length;
            for (int i = 0; i < n; i++)
            {
                var spark = _p.sparks[i];
                SetActive(spark, cheer);
                if (!cheer) continue;
                float p = Mathf.Repeat(t / 0.75f + i * 0.381f, 1f);
                float tau = p * 0.75f;
                float a = i * 51.4f * Mathf.Deg2Rad;
                float reach = 0.7f + 0.35f * Mathf.Repeat(i * 0.618f, 1f);
                spark.localPosition = new Vector3(Mathf.Sin(a) * reach * p, TildaModel.CraterY + 0.05f + 3.5f * tau - 3.4f * tau * tau, -Mathf.Cos(a) * reach * p * 0.4f - 0.1f);
                spark.localScale = Vector3.one * (0.085f * (1f - 0.6f * p));
            }
        }

        static void SetActive(Transform t, bool on)
        {
            if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
        }
    }
}
