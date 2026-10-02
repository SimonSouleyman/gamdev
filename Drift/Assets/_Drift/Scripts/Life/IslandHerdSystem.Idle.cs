using Drift.Core;
using UnityEngine;

namespace Drift.Life
{
    // Idle gestures (2026-09-24, owner: "Die Tiere sollen immer irgendwas tun, wenn sie stehen bleiben ... um die Gegend
    // gucken, sich strecken, sich kurz setzen, nach unten beugen usw. Die einzelnen Tiere in der Herde sollten nicht alle
    // das Gleiche gleichzeitig machen."). A layer on top of the Graze/Look/Rest state machine: every standing animal picks
    // its own next gesture on its own timer (own random stream, so the herd's main stream and every older behaviour stay
    // as they were). Head and rest changes go through the shader's pitch/rest blend (one rebuild each); body turns, tilts
    // and rolls are baked offsets (iYaw / iPitch about a foot pivot / iRoll) evaluated per step, which is why the layer
    // only runs in the near tier: there an animal with a gesture costs what a walking one costs, elsewhere it is off.
    public enum IdleAction : byte { None, Look, Glance, Stretch, Sit, Sniff, Shake, Turn, Special }

    public partial class IslandHerdSystem
    {
        [Header("Kleine Gesten im Stehen (Umschauen, Strecken, Hinsetzen, Schnuppern, Schütteln, Umdrehen ...)")]
        [Tooltip("Wie oft stehende Tiere eine kleine Geste zeigen (1 = normal, 0 = aus). Nur in der Nähe der Kamera.")]
        [Range(0f, 3f)] public float idleRate = 1f;
        [Tooltip("Pause zwischen zwei Gesten eines Tiers in Sekunden (zufällig zwischen min und max, geteilt durch die Rate).")]
        [Range(0.2f, 5f)] public float idlePauseMin = 0.6f;
        [Range(0.5f, 10f)] public float idlePauseMax = 3.5f;

        public int IdleActions { get; private set; }

        System.Random _idleRnd;
        const int IdleOff = -1, IdleFull = 0, IdleLying = 1, IdleGoal = 2;
        const float SitRest = 0.3f, SniffPitch = 14f, NeckDownPitch = 22f, BowHeadPitch = 8f, IdleEaseOut = 0.4f, IdleYawEase = 3f;

        // The steady head nod of grazing (the shader's _GrazeSwing, 6 deg by default) was the "Wippen" the owner saw on
        // every standing animal; with the gestures on top it is toned down on the runtime fallback material only (an
        // assigned material asset keeps its own value).
        const float GrazeSwing = 4f;
        static Material _tunedMaterial;

        static void TuneMaterial(Material m)
        {
            if (m == null || m == _tunedMaterial) return;
            if (m.HasFloat("_GrazeSwing")) m.SetFloat("_GrazeSwing", GrazeSwing);
            _tunedMaterial = m;
        }

        float IRand() => (float)(_idleRnd ??= new System.Random(HerdSeed ^ 0x1D1E5)).NextDouble();
        float IRand(float a, float b) => a + IRand() * (b - a);

        public IdleAction AnimalIdle(int herd, int member) => _herds[herd].members[member].idle;
        public float AnimalIdleProgress(int herd, int member)
        {
            var a = _herds[herd].members[member];
            return a.idle == IdleAction.None || a.idleDur <= 0f ? 0f : a.idleT / a.idleDur;
        }
        // What the gesture layer adds to the bake: yaw (gesture + a turn still easing out), body pitch, roll.
        public float AnimalIdleYaw(int herd, int member) => _herds[herd].members[member].iYaw + _herds[herd].members[member].yawLag;
        public float AnimalIdlePitch(int herd, int member) => _herds[herd].members[member].iPitch;
        public float AnimalIdleRoll(int herd, int member) => _herds[herd].members[member].iRoll;
        // The template pose the mesh shows (AnimalPose is the activity's pose without the gestures).
        public int AnimalShownPose(int herd, int member)
        {
            var a = _herds[herd].members[member];
            return a.idlePose != 0 ? a.idlePose : PoseOf(_herds[herd], a);
        }
        public float AnimalHeadPitchTarget(int herd, int member) => _herds[herd].members[member].pitchTo;

        // Weights of Look, Glance, Stretch, Sit, Sniff, Shake, Turn, Special (index = IdleAction).
        static float[] IdleWeights(LifeKind kind)
        {
            switch (kind)
            {
                case LifeKind.Hare: return new[] { 0f, 3f, 1f, 0.8f, 0f, 2.5f, 0.6f, 1.5f, 2f };
                case LifeKind.Sheep: return new[] { 0f, 3f, 1.5f, 0.8f, 0.6f, 2f, 1.2f, 1.5f, 0f };
                case LifeKind.Goat: return new[] { 0f, 4f, 1.5f, 1f, 0.4f, 2f, 1f, 1.5f, 0f };
                case LifeKind.Ox: return new[] { 0f, 2.5f, 1.5f, 1f, 0f, 2f, 1.2f, 1f, 0f };
                case LifeKind.Capybara: return new[] { 0f, 2.5f, 1.5f, 0.8f, 1.5f, 2f, 1f, 1f, 0f };
                case LifeKind.Flamingo: return new[] { 0f, 2.5f, 1.5f, 0.6f, 0f, 2f, 1f, 1f, 3f };
                case LifeKind.Tortoise: return new[] { 0f, 2f, 0.5f, 0.6f, 0f, 2f, 0f, 1f, 1.5f };
                case LifeKind.Penguin: return new[] { 0f, 3f, 2f, 0f, 0f, 1f, 1.5f, 1.5f, 2.5f };
                case LifeKind.Reindeer: return new[] { 0f, 3f, 1.5f, 1f, 0.3f, 3f, 1f, 1.5f, 0f };
                case LifeKind.ArcticFox: return new[] { 0f, 3f, 1f, 1.5f, 2f, 2f, 1f, 1.5f, 0f };
                case LifeKind.Zebra: return new[] { 0f, 3f, 2f, 1f, 0.2f, 2f, 1.5f, 1.5f, 0f };
                case LifeKind.Giraffe: return new[] { 0f, 3f, 1.5f, 0.5f, 0f, 1f, 0.3f, 1f, 2f };
                case LifeKind.Meerkat: return new[] { 0f, 1.5f, 1.5f, 0.5f, 1f, 2f, 0.8f, 1.5f, 3.5f };
                default: return new[] { 0f, 3f, 1.5f, 1f, 0.8f, 2f, 0.8f, 1.5f, 0f };
            }
        }

        // Template z of the front and hind feet (lowest vertices of the simple model): a bow tilts about the hind
        // feet, sitting and the back stretch about the front feet, so one pair always stays on the ground.
        static void Feet(Species s)
        {
            var verts = LifeMeshes.GetTemplate(s.kind, 0).vertices;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < verts.Length; i++) { lo = Mathf.Min(lo, verts[i].y); hi = Mathf.Max(hi, verts[i].y); }
            float cut = lo + (hi - lo) * 0.12f;
            float zf = float.MinValue, zh = float.MaxValue;
            for (int i = 0; i < verts.Length; i++)
            {
                if (verts[i].y > cut) continue;
                zf = Mathf.Max(zf, verts[i].z);
                zh = Mathf.Min(zh, verts[i].z);
            }
            if (zf < zh) { zf = 0.15f; zh = -0.15f; }
            s.frontZ = zf;
            s.hindZ = zh;
            s.feetKnown = true;
        }

        // Activities that leave the animal standing about: the goal ones (watch line, visit, spread, the oxen's circle)
        // get the calm gestures, the shade bunch (no goal) all of them.
        static bool IdleCompatible(AnimalActivity act) =>
            act == AnimalActivity.Watch || act == AnimalActivity.Visit || act == AnimalActivity.Spread || act == AnimalActivity.Circle
            || act == AnimalActivity.Shade || act == AnimalActivity.Graze || act == AnimalActivity.Shelter;

        static float S(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        static float Env(float t, float dur, float edge) => S(Mathf.Min(t, dur - t) / edge);

        // Left, hold, over to the right, hold, back.
        static float LookTrack(float u, float a, float b)
        {
            if (u < 0.22f) return a * S(u / 0.22f);
            if (u < 0.45f) return a;
            if (u < 0.68f) return Mathf.Lerp(a, b, S((u - 0.45f) / 0.23f));
            if (u < 0.8f) return b;
            return b * (1f - S((u - 0.8f) / 0.2f));
        }

        // A yaw set by the state machine or an errand is shown as a turn: the jump goes into yawLag, which eases out.
        void TurnTo(Animal a, float yaw)
        {
            a.yawLag = Mathf.DeltaAngle(0f, a.yawLag + Mathf.DeltaAngle(yaw, a.yaw));
            a.yaw = yaw;
        }

        float IdlePause(Herd herd, Animal a)
        {
            float k = Young(a) ? 0.7f : 1f;
            if (herd.spec.kind == LifeKind.Tortoise) k *= 1.8f;
            if (a.state == AnimalState.Rest) k *= 2.5f;
            return IRand(idlePauseMin, Mathf.Max(idlePauseMin, idlePauseMax)) * k / Mathf.Max(0.05f, idleRate);
        }

        // Top of the member loop: a gesture something else took over stops (its turn keeps easing out), and what is
        // left of one eases out. Off (not the near tier) everything is dropped at once.
        bool IdleSettle(Animal a, bool on, float dt)
        {
            bool changed = false;
            if (a.idle != IdleAction.None && (!on || a.hidden || a.dived || a.state == AnimalState.Play || (a.act != AnimalActivity.None && !IdleCompatible(a.act))))
                changed = EndIdle(null, a, false);
            if (!on)
            {
                if (a.yawLag != 0f || a.iYaw != 0f || a.iPitch != 0f || a.iRoll != 0f)
                {
                    a.yawLag = a.iYaw = a.iPitch = a.iRoll = 0f;
                    changed = true;
                }
                return changed;
            }
            if (a.yawLag != 0f)
            {
                a.yawLag *= Mathf.Exp(-IdleYawEase * dt);
                if (Mathf.Abs(a.yawLag) < 0.5f) a.yawLag = 0f;
                changed = true;
            }
            if (a.idle == IdleAction.None && (a.iPitch != 0f || a.iRoll != 0f))
            {
                float k = Mathf.Exp(-8f * dt);
                a.iPitch *= k;
                a.iRoll *= k;
                if (Mathf.Abs(a.iPitch) < 0.2f) a.iPitch = 0f;
                if (Mathf.Abs(a.iRoll) < 0.2f) a.iRoll = 0f;
                changed = true;
            }
            return changed;
        }

        bool StepIdle(Herd herd, Animal a, int index, float dt, int mode)
        {
            if (mode == IdleOff) return a.idle != IdleAction.None && EndIdle(herd, a, false);
            if (a.idle == IdleAction.None)
            {
                a.idleNext -= dt;
                if (a.idleNext > 0f) return false;
                return StartIdle(herd, a, index, mode);
            }
            // Lying down or getting up in the middle of a gesture: it eases out.
            bool lying = a.state == AnimalState.Rest || a.state == AnimalState.Sleep;
            bool wasLying = a.idleState == AnimalState.Rest || a.idleState == AnimalState.Sleep;
            if (lying != wasLying && a.idleT < a.idleDur - IdleEaseOut) a.idleT = a.idleDur - IdleEaseOut;
            a.idleT += dt;
            if (a.idleT >= a.idleDur) return EndIdle(herd, a, true);
            return EvalIdle(herd, a);
        }

        bool Allowed(IdleAction k, int mode, bool lying, bool night)
        {
            if (lying || mode == IdleLying) return k == IdleAction.Look || k == IdleAction.Glance;
            if (night && k != IdleAction.Look && k != IdleAction.Glance && k != IdleAction.Sniff) return false;
            if (mode == IdleGoal) return k == IdleAction.Look || k == IdleAction.Glance || k == IdleAction.Shake || k == IdleAction.Stretch;
            return true;
        }

        IdleAction PickIdle(Species s, int mode, bool lying, bool night)
        {
            var w = s.idleW;
            float total = 0f;
            for (int k = 1; k < w.Length; k++) if (Allowed((IdleAction)k, mode, lying, night)) total += w[k];
            if (total <= 0f) return IdleAction.None;
            float r = IRand() * total;
            for (int k = 1; k < w.Length; k++)
            {
                if (!Allowed((IdleAction)k, mode, lying, night)) continue;
                r -= w[k];
                if (r <= 0f) return (IdleAction)k;
            }
            return IdleAction.Look;
        }

        static bool NeighbourDoes(Herd herd, int index, IdleAction pick) =>
            pick != IdleAction.None && ((index > 0 && herd.members[index - 1].idle == pick)
                || (index + 1 < herd.members.Count && herd.members[index + 1].idle == pick));

        bool StartIdle(Herd herd, Animal a, int index, int mode)
        {
            var s = herd.spec;
            s.idleW ??= IdleWeights(s.kind);
            if (!s.feetKnown) Feet(s);
            bool lying = a.state == AnimalState.Rest || a.state == AnimalState.Sleep;
            bool night = _night > sleepThreshold;
            var pick = PickIdle(s, mode, lying, night);
            // Neighbours in the herd list stand next to each other: one that is already doing it makes a second draw, and
            // when that is taken by a neighbour too the animal waits a moment instead of joining in step.
            if (pick != IdleAction.None && NeighbourDoes(herd, index, pick))
            {
                pick = PickIdle(s, mode, lying, night);
                if (NeighbourDoes(herd, index, pick))
                {
                    a.idleNext = IdlePause(herd, a) * 0.5f;
                    return false;
                }
            }
            if (pick == IdleAction.None)
            {
                a.idleNext = IdlePause(herd, a);
                return false;
            }
            bool head = mode != IdleGoal;
            float slow = s.kind == LifeKind.Tortoise ? 1.6f : Young(a) ? 0.8f : 1f;
            float side = IRand() < 0.5f ? -1f : 1f;
            a.idle = pick;
            a.idleT = 0f;
            a.idleStage = 0;
            a.idleState = a.state;
            a.idleHead = a.idleRest = false;
            a.idlePose = 0;
            a.iPivot = 0f;
            float amp = lying ? 0.4f : mode == IdleGoal ? 0.6f : 1f;
            switch (pick)
            {
                case IdleAction.Look:
                    a.idleDur = IRand(2.2f, 4f) * slow;
                    a.idleA = side * IRand(25f, 50f) * amp;
                    a.idleB = -side * IRand(20f, 45f) * amp;
                    if (head) IdleHead(a, LookPitch, true);
                    break;
                case IdleAction.Glance:
                {
                    a.idleDur = IRand(1.5f, 3f) * slow;
                    Animal near = null;
                    float best = float.MaxValue;
                    foreach (var m in herd.members)
                    {
                        if (m == a || m.hidden || m.dived) continue;
                        float d = (m.pos - a.pos).sqrMagnitude;
                        if (d < best) { best = d; near = m; }
                    }
                    float turn = side * IRand(20f, 40f);
                    if (near != null && best > 1e-6f)
                    {
                        Vector2 to = near.pos - a.pos;
                        turn = Mathf.Clamp(Mathf.DeltaAngle(a.yaw + a.yawLag, Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg), -75f, 75f);
                    }
                    a.idleA = turn * (lying ? 0.5f : 1f);
                    if (head) IdleHead(a, LookPitch, true);
                    break;
                }
                case IdleAction.Stretch:
                    a.idleDur = IRand(2.4f, 3.4f) * slow;
                    a.idleA = IRand(9f, 13f);
                    a.idleB = IRand(7f, 10f);
                    if (head) IdleHead(a, BowHeadPitch, false);
                    break;
                case IdleAction.Sit:
                    a.idleDur = IRand(3f, 6f) * slow;
                    a.idleA = IRand(17f, 24f);
                    IdleHead(a, LookPitch, true);
                    a.idleRest = true;
                    a.restTo = SitRest;
                    break;
                case IdleAction.Sniff:
                    a.idleDur = IRand(2f, 4f) * slow;
                    a.idleA = side * IRand(10f, 18f);
                    if (head) IdleHead(a, SniffPitch, false);
                    break;
                case IdleAction.Shake:
                    a.idleDur = IRand(0.9f, 1.3f);
                    a.idleA = side * IRand(10f, 14f);
                    if (head) IdleHead(a, StretchPitch, true);
                    break;
                case IdleAction.Turn:
                {
                    a.idleDur = IRand(1.2f, 2.2f) * slow;
                    float by = side * IRand(40f, 130f);
                    a.idleA = by;
                    a.yaw = Mathf.Repeat(a.yaw + by, 360f);
                    a.iYaw -= by;
                    SetMoving(a, true);
                    break;
                }
                case IdleAction.Special:
                    StartSpecial(s, a, side);
                    break;
            }
            IdleActions++;
            EvalIdle(herd, a);
            return true;
        }

        void StartSpecial(Species s, Animal a, float side)
        {
            switch (s.kind)
            {
                case LifeKind.Hare:
                    // Up on the hind legs for a look round (Männchen), smaller than the zigzag's.
                    a.idleDur = IRand(2f, 3.5f);
                    a.idleA = IRand(36f, 48f);
                    a.idleB = side * IRand(20f, 35f);
                    IdleHead(a, LookPitch, true);
                    break;
                case LifeKind.Meerkat:
                    // Upright sentry pose, looking left and right.
                    a.idleDur = IRand(2.5f, 5f);
                    a.idleA = side * IRand(30f, 50f);
                    a.idleB = -side * IRand(25f, 45f);
                    a.idlePose = AnimalModels.PoseSpecial;
                    break;
                case LifeKind.Flamingo:
                    a.idleDur = IRand(4f, 8f);
                    a.idlePose = AnimalModels.PoseSpecial;
                    break;
                case LifeKind.Tortoise:
                    a.idleDur = IRand(2.5f, 5f);
                    a.idlePose = AnimalModels.PoseSpecial;
                    break;
                case LifeKind.Penguin:
                    a.idleDur = IRand(1.4f, 2.2f);
                    a.idleA = side * IRand(4f, 7f);
                    break;
                case LifeKind.Giraffe:
                    a.idleDur = IRand(3f, 5f);
                    IdleHead(a, NeckDownPitch, false);
                    break;
                default:
                    a.idleDur = 1f;
                    break;
            }
        }

        // A head keyframe through the shader's pitch blend; the first one remembers what to give back at the end.
        void IdleHead(Animal a, float pitch, bool alert)
        {
            if (!a.idleHead)
            {
                a.idleBasePitch = a.pitchTo;
                a.idleBaseAlert = a.alert;
                a.idleHead = true;
            }
            Stamp(a);
            a.pitchTo = pitch;
            a.alert = alert;
            a.idleHeadPitch = pitch;
        }

        bool HeadStillMine(Animal a) => a.idleHead && a.pitchTo == a.idleHeadPitch && a.state == a.idleState;

        bool EvalIdle(Herd herd, Animal a)
        {
            var s = herd.spec;
            float t = a.idleT, dur = Mathf.Max(0.1f, a.idleDur), u = Mathf.Clamp01(t / dur);
            float yaw = 0f, pitch = 0f, roll = 0f, pivot = 0f;
            int pose = a.idlePose;
            bool changed = false;
            switch (a.idle)
            {
                case IdleAction.Look:
                    yaw = LookTrack(u, a.idleA, a.idleB);
                    break;
                case IdleAction.Glance:
                    yaw = a.idleA * Env(t, dur, 0.35f);
                    break;
                case IdleAction.Stretch:
                    if (u < 0.5f)
                    {
                        pitch = a.idleA * S(Mathf.Min(u, 0.5f - u) / 0.14f);
                        pivot = s.hindZ;
                    }
                    else
                    {
                        pitch = -a.idleB * S(Mathf.Min(u - 0.5f, 1f - u) / 0.14f);
                        pivot = s.frontZ;
                        if (a.idleStage == 0)
                        {
                            a.idleStage = 1;
                            if (HeadStillMine(a)) { IdleHead(a, StretchPitch, true); changed = true; }
                        }
                    }
                    break;
                case IdleAction.Sit:
                    pitch = -a.idleA * Env(t, dur, 0.5f);
                    pivot = s.frontZ;
                    if (a.idleStage == 0 && t > dur - 0.6f)
                    {
                        // Up again: the rest blend has to start before the tilt is gone.
                        a.idleStage = 1;
                        if (a.idleRest && a.restTo == SitRest && a.state == a.idleState) { Stamp(a); a.restTo = 0f; changed = true; }
                        a.idleRest = false;
                    }
                    break;
                case IdleAction.Sniff:
                    yaw = a.idleA * Mathf.Sin(u * Mathf.PI * 3f) * Env(t, dur, 0.4f);
                    pitch = 3f * Env(t, dur, 0.4f);
                    pivot = s.hindZ;
                    if (a.idleStage == 0 && u >= 0.55f)
                    {
                        a.idleStage = 1;
                        if (HeadStillMine(a)) { IdleHead(a, 4f, false); changed = true; }
                    }
                    break;
                case IdleAction.Shake:
                {
                    float e = Env(t, dur, 0.2f);
                    roll = a.idleA * Mathf.Sin(t * Mathf.PI * 4.4f) * e;
                    yaw = 5f * Mathf.Sin(t * Mathf.PI * 4.4f + 1f) * e;
                    break;
                }
                case IdleAction.Turn:
                    yaw = -a.idleA * (1f - S(u));
                    break;
                case IdleAction.Special:
                    switch (s.kind)
                    {
                        case LifeKind.Hare:
                            pitch = -a.idleA * Env(t, dur, 0.35f);
                            pivot = s.hindZ;
                            yaw = a.idleB * Mathf.Sin(u * Mathf.PI * 2f) * Env(t, dur, 0.35f);
                            break;
                        case LifeKind.Meerkat:
                            yaw = LookTrack(u, a.idleA, a.idleB);
                            break;
                        case LifeKind.Penguin:
                            // Flapping: flippers out (the call pose) and in again, a little waddle with it.
                            pose = t < dur * 0.75f && ((int)(t / 0.28f) & 1) == 0 ? AnimalModels.PoseCall : 0;
                            roll = a.idleA * Mathf.Sin(t * Mathf.PI * 3.6f) * Env(t, dur, 0.25f);
                            break;
                    }
                    break;
            }
            a.iPivot = pivot;
            if (Mathf.Abs(yaw - a.iYaw) > 0.05f || Mathf.Abs(pitch - a.iPitch) > 0.05f || Mathf.Abs(roll - a.iRoll) > 0.05f)
            {
                a.iYaw = yaw;
                a.iPitch = pitch;
                a.iRoll = roll;
                changed = true;
            }
            if (pose != a.idlePose)
            {
                a.idlePose = pose;
                changed = true;
            }
            return changed;
        }

        // natural = ran its course; otherwise the turn left over eases out through yawLag and the tilt decays.
        bool EndIdle(Herd herd, Animal a, bool natural)
        {
            if (a.idle == IdleAction.Turn) SetMoving(a, false);
            if (a.idleHead)
            {
                if (HeadStillMine(a))
                {
                    Stamp(a);
                    a.pitchTo = a.idleBasePitch;
                    a.alert = a.idleBaseAlert;
                }
                a.idleHead = false;
            }
            if (a.idleRest)
            {
                if (a.restTo == SitRest && a.state == a.idleState)
                {
                    Stamp(a);
                    a.restTo = 0f;
                }
                a.idleRest = false;
            }
            a.yawLag = Mathf.DeltaAngle(0f, a.yawLag + a.iYaw);
            a.iYaw = 0f;
            if (natural) a.iPitch = a.iRoll = 0f;
            a.idlePose = 0;
            a.idle = IdleAction.None;
            a.idleNext = herd != null ? IdlePause(herd, a) : IRand(idlePauseMin, idlePauseMax);
            return true;
        }

        void ResetIdle(Animal a)
        {
            a.idle = IdleAction.None;
            a.idleHead = a.idleRest = false;
            a.idlePose = 0;
            a.iYaw = a.iPitch = a.iRoll = a.yawLag = 0f;
            a.idleNext = IRand(0.2f, Mathf.Max(0.3f, idlePauseMax));
        }
    }
}
