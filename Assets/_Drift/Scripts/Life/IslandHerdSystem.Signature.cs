using UnityEngine;

namespace Drift.Life
{
    // Signature moves (2026-09-21, owner: "für alle Tiere auf den Inseln eine weitere, komplett neue und einzigartige
    // Bewegung für jede Art"): one per species, used by no other species, each with its own look. They run as an
    // errand (Errand.Signature), so startle, fire, rising water, a storm huddle, night and the far tier cancel them
    // like every errand; every spot they use is checked with OkSpot (the species' own ground, no fire or houses, the
    // safe band while the water rises) and every step with CanStepMember. Steps as ActivityStep reports them:
    // MoveWalk = getting into place (the hare: sitting up), MovePerform = the move itself, MoveFinish = the way
    // out of it, MoveQueue = a reindeer waiting for its turn at the crater.
    //   hare      Zigzag     Männchen, then a zigzag dash out and back (sharp turns, big bounds)
    //   sheep     Carousel   the whole herd walks nose to tail round a ring, leaning into the turn
    //   goat      Rear       walks to a bush and rears up on its hind legs to nibble, two or three times
    //   ox        Scratch    stands flank-on at a tree and rubs to and fro, leaning into the trunk
    //   capybara  Snuggle    lies down in a star, heads together; an egret rides on one back and hops about
    //   flamingo  StampDance treads little circles in the shallows, head down in the mud
    //   tortoise  NeckDuel   two face each other and stretch up tall; the lower one gives way and walks off
    //   penguin   SkyCall    a wave runs through the colony: bow, beak to the sky, flippers out, swaying
    //   reindeer  Crater     one paws a feeding crater, the next shoulders it aside and takes over
    //   fox       TailChase  chases its own tail in a tight spin, then flops down
    //   meerkat   WarDance   the group packs tight and bounces forward and back in step
    //   zebra     Groom      two stand head to tail, nibbling each other's withers, slowly turning
    //   giraffe   Necking    two stand side by side and swing their necks at each other
    public partial class IslandHerdSystem
    {
        public const int MoveWalk = 0, MovePerform = 1, MoveFinish = 2, MoveQueue = 3;
        const float MannchenPitch = -62f, RearPitch = -56f, NeckUpPitch = -34f, BowPitch = 30f, CraterPitch = 16f, StampPitch = 40f;

        public int SignatureMoves { get; private set; }

        public static bool IsSignature(AnimalActivity act) => act >= AnimalActivity.Zigzag && act <= AnimalActivity.Necking;

        public static AnimalActivity SignatureOf(LifeKind kind)
        {
            switch (kind)
            {
                case LifeKind.Hare: return AnimalActivity.Zigzag;
                case LifeKind.Sheep: return AnimalActivity.Carousel;
                case LifeKind.Goat: return AnimalActivity.Rear;
                case LifeKind.Ox: return AnimalActivity.Scratch;
                case LifeKind.Capybara: return AnimalActivity.Snuggle;
                case LifeKind.Flamingo: return AnimalActivity.StampDance;
                case LifeKind.Tortoise: return AnimalActivity.NeckDuel;
                case LifeKind.Penguin: return AnimalActivity.SkyCall;
                case LifeKind.Reindeer: return AnimalActivity.Crater;
                case LifeKind.ArcticFox: return AnimalActivity.TailChase;
                case LifeKind.Meerkat: return AnimalActivity.WarDance;
                case LifeKind.Zebra: return AnimalActivity.Groom;
                case LifeKind.Giraffe: return AnimalActivity.Necking;
                default: return AnimalActivity.None;
            }
        }

        // Template z of the hind feet the rearing species turn about (RebuildMesh).
        static bool RearPivot(LifeKind kind, AnimalActivity act, out float z)
        {
            z = kind == LifeKind.Hare && act == AnimalActivity.Zigzag ? -0.12f : kind == LifeKind.Goat && act == AnimalActivity.Rear ? -0.15f : 0f;
            return z != 0f;
        }

        float H(Vector2 p) => _surface.SampleHeight(p);
        static Vector2 Ang(float rad) => new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        static Vector2 Heading(float yawDeg) => new Vector2(Mathf.Sin(yawDeg * Mathf.Deg2Rad), Mathf.Cos(yawDeg * Mathf.Deg2Rad));
        static float YawOf(Vector2 d) => Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
        static Vector2 Rotate(Vector2 v, float rad)
        {
            float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
            return new Vector2(v.x * cs - v.y * sn, v.x * sn + v.y * cs);
        }
        // Tangent of a circle walked with the angle growing (dir +1) or shrinking (dir -1).
        static Vector2 Tangent(float rad, float dir) => new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad)) * dir;

        // How far from its herd a member may go for a move of its own: the edge of the herd plus five bodies.
        float NearReach(Herd herd) => RadiusOf(herd) + herd.spec.body * animalScale * 5f;

        bool Free(Animal a) => !a.hidden && a.act == AnimalActivity.None && IsAwake(a);

        Animal NearestFreeAdult(Herd herd, Animal to)
        {
            Animal best = null;
            float bestD = float.MaxValue;
            foreach (var m in herd.members)
            {
                if (m == to || Young(m) || !Free(m)) continue;
                float d = (m.pos - to.pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = m; }
            }
            return best;
        }

        // ---------------------------------------------------------- start / stop

        bool StartSignature(Herd herd)
        {
            if (herd.errand != Errand.None || herd.members.Count == 0 || behaviourRate <= 0f) return false;
            var s = herd.spec;
            if (s.kind == LifeKind.Flamingo)
            {
                // The dance is in the water: a shore errand like the parade, never while the water rises.
                if (Rising || !StartShoreErrand(herd, Errand.Signature)) return false;
                SignatureMoves++;
                return true;
            }
            EndPlay(herd, false);
            ReleaseLookout(herd);
            StopDig(herd);
            herd.sigA = herd.sigB = null;
            herd.choreoStep = 0;
            herd.choreoT = herd.sigPhase = 0f;
            bool ok;
            switch (s.kind)
            {
                case LifeKind.Hare: ok = StartZigzag(herd); break;
                case LifeKind.Sheep: ok = StartCarousel(herd); break;
                case LifeKind.Goat: ok = StartRear(herd); break;
                case LifeKind.Ox: ok = StartScratch(herd); break;
                case LifeKind.Capybara: ok = StartSnuggle(herd); break;
                case LifeKind.Tortoise: ok = StartDuel(herd); break;
                case LifeKind.Penguin: ok = StartSkyCall(herd); break;
                case LifeKind.Reindeer: ok = StartCrater(herd); break;
                case LifeKind.ArcticFox: ok = StartTailChase(herd); break;
                case LifeKind.Meerkat: ok = StartWarDance(herd); break;
                case LifeKind.Zebra: ok = StartPair(herd, AnimalActivity.Groom); break;
                case LifeKind.Giraffe: ok = StartPair(herd, AnimalActivity.Necking); break;
                default: ok = false; break;
            }
            if (!ok)
            {
                foreach (var a in herd.members) if (IsSignature(a.act)) ClearGoal(a);
                herd.sigA = herd.sigB = null;
                herd.errandCool = Mathf.Max(herd.errandCool, Rand(10f, 20f));
                return false;
            }
            herd.errand = Errand.Signature;
            herd.errandStage = 1;
            herd.wait = Mathf.Max(herd.wait, 4f);
            SignatureMoves++;
            ErrandsStarted++;
            return true;
        }

        // The flamingos reached the dry spot above the shallows (ArriveErrand).
        void ArriveSignature(Herd herd)
        {
            herd.choreoStep = 0;
            herd.choreoT = 0f;
            herd.errandT = Rand(16f, 24f);
            herd.wait = 4f;
            AssignShoreGoals(herd, WadeFloor + 0.015f, WadeHigh, AnimalActivity.StampDance, true, 2.2f);
            foreach (var a in herd.members)
                if (a.act == AnimalActivity.StampDance)
                {
                    a.step = MoveWalk;
                    a.from = a.goal;
                }
        }

        void EndMove(Animal a)
        {
            ClearGoal(a);
            SetMoving(a, false);
            SetState(a, AnimalState.Look, Rand(1f, 2f), LookPitch, true);
        }

        void EndSignature(Herd herd)
        {
            foreach (var a in herd.members) if (IsSignature(a.act)) EndMove(a);
            CancelErrand(herd);
        }

        bool SignatureSurvivesReshape(Herd herd)
        {
            var sp = herd.spec;
            if (sp.kind == LifeKind.Flamingo) return false;
            foreach (var a in herd.members)
            {
                if (!IsSignature(a.act)) continue;
                if (!Valid(sp, H(a.pos))) return false;
                if ((a.step == MoveWalk || a.step == MoveQueue) && !ValidTarget(sp, H(a.goal))) return false;
            }
            return true;
        }

        // Walks a member towards p at pace x its species' speed; true once it is there.
        bool WalkTo(Herd herd, Animal a, Vector2 p, float pace, float dt)
        {
            var s = herd.spec;
            Vector2 to = p - a.pos;
            float d = to.magnitude;
            if (d <= s.body * animalScale * 0.08f)
            {
                SetMoving(a, false);
                return true;
            }
            float step = s.speed * pace * dt;
            Vector2 next = a.pos + to / d * Mathf.Min(step, d);
            if (!CanStepMember(s, a, H(next), H(a.pos)))
            {
                a.blocked += dt;
                SetMoving(a, false);
                return false;
            }
            a.blocked = 0f;
            a.pos = next;
            a.yaw = Mathf.LerpAngle(a.yaw, YawOf(to), 1f - Mathf.Exp(-8f * dt));
            SetMoving(a, true);
            SetState(a, AnimalState.Walk, 0f, WalkPitch, false);
            return d <= step;
        }

        // Herd-level part, once per step while the move runs. Returns whether the mesh needs a rebuild.
        bool StepSignature(Herd herd, float dt)
        {
            herd.choreoT += dt;
            int active = 0;
            foreach (var a in herd.members) if (IsSignature(a.act)) active++;
            if (active == 0)
            {
                CancelErrand(herd);
                return true;
            }
            herd.errandT -= dt;
            if (herd.errandT <= 0f)
            {
                EndSignature(herd);
                return true;
            }
            // The herd stays put while the move runs; the move ends itself (or errandT does).
            if (herd.wait < 0.5f) herd.wait = 0.5f;
            switch (herd.spec.kind)
            {
                case LifeKind.Sheep: return CarouselHerd(herd, dt);
                case LifeKind.Capybara: return SnuggleHerd(herd, dt);
                case LifeKind.Tortoise: return DuelHerd(herd);
                case LifeKind.Reindeer: return CraterHerd(herd);
                case LifeKind.Meerkat: return DanceHerd(herd);
                case LifeKind.Zebra:
                case LifeKind.Giraffe: return PairHerd(herd, dt);
                default: return false;
            }
        }

        // Member part: the move owns the animal (position, heading, pose) while it runs.
        bool SignatureStep(Herd herd, Animal a, float dt)
        {
            switch (a.act)
            {
                case AnimalActivity.Zigzag: return ZigzagStep(herd, a, dt);
                case AnimalActivity.Carousel: return CarouselStep(herd, a, dt);
                case AnimalActivity.Rear: return RearStep(herd, a, dt);
                case AnimalActivity.Scratch: return ScratchStep(herd, a, dt);
                case AnimalActivity.Snuggle: return SnuggleStep(herd, a, dt);
                case AnimalActivity.StampDance: return StampStep(herd, a, dt);
                case AnimalActivity.NeckDuel: return DuelStep(herd, a, dt);
                case AnimalActivity.SkyCall: return SkyCallStep(a, dt);
                case AnimalActivity.Crater: return CraterStep(herd, a, dt);
                case AnimalActivity.TailChase: return TailChaseStep(herd, a, dt);
                case AnimalActivity.WarDance: return DanceStep(herd, a, dt);
                default: return PairStep(herd, a, dt);
            }
        }

        // ---------------------------------------------------------- hare: Männchen and zigzag

        bool StartZigzag(Herd herd)
        {
            var a = RandomIdle(herd, true);
            if (a == null) return false;
            Vector2 away = a.pos - herd.center;
            a.dir = away.sqrMagnitude > 1e-6f ? away.normalized : Heading(a.yaw);
            a.yaw = YawOf(a.dir);
            a.act = AnimalActivity.Zigzag;
            a.step = MoveWalk;
            a.actT = Rand(1.2f, 2f);
            a.pitch = MannchenPitch;
            SetMoving(a, false);
            SetState(a, AnimalState.Look, 999f, LookPitch, true);
            herd.sigA = a;
            herd.sigTurns = 4 + _rnd.Next(2);
            herd.errandT = 20f;
            return true;
        }

        bool ZigzagStep(Herd herd, Animal a, float dt)
        {
            var s = herd.spec;
            if (a.step == MoveWalk)
            {
                a.actT -= dt;
                if (a.actT > 0f) return false;
                a.pitch = 0f;
                a.step = MovePerform;
                if (!NextZig(herd, a)) { EndMove(a); return true; }
                SetState(a, AnimalState.Walk, 0f, WalkPitch, false);
                return true;
            }
            float v = s.speed * 3f * dt;
            Vector2 to = a.goal - a.pos;
            float d = to.magnitude;
            if (d <= v)
            {
                a.pos = a.goal;
                if (--herd.sigTurns <= 0 || !NextZig(herd, a)) EndMove(a);
                return true;
            }
            Vector2 next = a.pos + to / d * v;
            if (!CanStepMember(s, a, H(next), H(a.pos))) { EndMove(a); return true; }
            a.pos = next;
            // Hooks are sharp: no easing into the new heading.
            a.yaw = YawOf(to);
            SetMoving(a, true);
            return true;
        }

        // The next hook: 50-72 degrees off the dash line, alternating sides; the last two swing back home.
        bool NextZig(Herd herd, Animal a)
        {
            var s = herd.spec;
            float body = s.body * animalScale;
            Vector2 home = herd.center - a.pos;
            Vector2 line = herd.sigTurns <= 2 && home.sqrMagnitude > 1e-4f ? home.normalized : a.dir;
            float side = (herd.sigTurns & 1) == 0 ? 1f : -1f;
            for (int t = 0; t < 2; t++, side = -side)
            {
                Vector2 p = a.pos + Rotate(line, side * Rand(50f, 72f) * Mathf.Deg2Rad) * (body * Rand(5f, 7f));
                if (!OkSpot(s, p) || !OkSpot(s, (p + a.pos) * 0.5f)) continue;
                a.goal = p;
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------- sheep: carousel

        bool StartCarousel(Herd herd)
        {
            var s = herd.spec;
            int n = 0;
            foreach (var a in herd.members) if (Free(a)) n++;
            if (n < 4) return false;
            float r = Mathf.Max(RadiusOf(herd) * 0.8f, s.body * animalScale * 1.8f);
            Vector2 c = herd.center;
            for (int k = 0; k < 12; k++)
                if (!OkSpot(s, c + Ang(k * (Mathf.PI / 6f)) * r)) return false;
            herd.errandPos = c;
            herd.sigR = r;
            herd.side = Rand() < 0.5f ? 1 : -1;
            int i = 0;
            foreach (var a in herd.members)
            {
                if (!Free(a)) continue;
                a.act = AnimalActivity.Carousel;
                a.step = MoveWalk;
                a.sigT = i++ * (Mathf.PI * 2f / n);
                a.goal = c + Ang(a.sigT) * r;
            }
            herd.errandT = 40f;
            return true;
        }

        bool CarouselHerd(Herd herd, float dt)
        {
            if (herd.choreoStep == 0)
            {
                int waiting = 0;
                foreach (var a in herd.members) if (a.act == AnimalActivity.Carousel && a.step == MoveWalk) waiting++;
                if (waiting > 0 && herd.choreoT < 8f) return false;
                herd.choreoStep = 1;
                herd.choreoT = 0f;
                herd.errandT = Rand(14f, 20f);
                return true;
            }
            herd.sigPhase += herd.side * herd.spec.speed * 1.2f / herd.sigR * dt;
            return false;
        }

        bool CarouselStep(Herd herd, Animal a, float dt)
        {
            var s = herd.spec;
            float ang = a.sigT + herd.sigPhase;
            Vector2 p = herd.errandPos + Ang(ang) * herd.sigR;
            if (a.step == MoveWalk)
            {
                if (WalkTo(herd, a, p, herd.choreoStep == 0 ? 1.5f : 2f, dt))
                {
                    a.step = MovePerform;
                    a.yaw = YawOf(Tangent(ang, herd.side));
                    SetMoving(a, false);
                    SetState(a, AnimalState.Look, 999f, LookPitch, false);
                }
                else if (a.blocked > 3f) EndMove(a);
                return true;
            }
            if (herd.choreoStep == 0) return false;
            if (!CanStepMember(s, a, H(p), H(a.pos))) { EndSignature(herd); return true; }
            a.pos = p;
            a.yaw = YawOf(Tangent(ang, herd.side));
            // Leaning into the ring, head low at the tail in front.
            a.roll = 9f * herd.side;
            SetMoving(a, true);
            SetState(a, AnimalState.Walk, 0f, 7f, false);
            return true;
        }

        // ---------------------------------------------------------- goat: rearing up at a bush

        bool StartRear(Herd herd)
        {
            var s = herd.spec;
            var a = RandomIdle(herd, true);
            if (a == null) return false;
            Vector2 spot = a.pos, face = Heading(a.yaw);
            // Only a bush at the edge of the herd: the goat must not wander off across the island for it.
            float reach = NearReach(herd);
            if (_life != null && (_life.TryRandomPlant(LifeKind.Bush, _rnd, herd.center, reach, out Vector2 plant) || _life.TryRandomPlant(LifeKind.Tree, _rnd, herd.center, reach, out plant)))
            {
                Vector2 away = a.pos - plant;
                Vector2 u = away.sqrMagnitude > 1e-4f ? away.normalized : new Vector2(1f, 0f);
                Vector2 p = plant + u * 0.12f;
                if (OkSpot(s, p)) { spot = p; face = -u; }
            }
            if (!OkSpot(s, spot)) return false;
            a.act = AnimalActivity.Rear;
            a.step = MoveWalk;
            a.goal = spot;
            a.dir = face;
            herd.sigA = a;
            herd.sigTurns = 2 + _rnd.Next(2);
            herd.errandT = 40f;
            return true;
        }

        bool RearStep(Herd herd, Animal a, float dt)
        {
            switch (a.step)
            {
                case MoveWalk:
                    if (!WalkTo(herd, a, a.goal, 1.2f, dt)) { if (a.blocked > 3f) EndMove(a); return true; }
                    a.step = MovePerform;
                    a.sigT = 0f;
                    a.yaw = a.baseYaw = YawOf(a.dir);
                    SetMoving(a, false);
                    SetState(a, AnimalState.Look, 999f, LookPitch, false);
                    return true;
                case MovePerform:
                {
                    const float rise = 0.45f, hold = 2.6f, fall = 0.35f;
                    a.sigT += dt;
                    float t = a.sigT;
                    if (t < 0f) return false;
                    if (t < rise) a.pitch = RearPitch * Mathf.SmoothStep(0f, 1f, t / rise);
                    else if (t < rise + hold)
                    {
                        // Up on the hind legs, nibbling: the head works left and right.
                        a.pitch = RearPitch;
                        a.yaw = a.baseYaw + 8f * Mathf.Sin(t * 5f);
                    }
                    else if (t < rise + hold + fall) a.pitch = RearPitch * (1f - (t - rise - hold) / fall);
                    else
                    {
                        a.pitch = 0f;
                        a.yaw = a.baseYaw;
                        if (--herd.sigTurns <= 0)
                        {
                            a.step = MoveFinish;
                            a.sigT = 0f;
                            SetState(a, AnimalState.Look, 999f, StretchPitch, true);
                        }
                        else a.sigT = -Rand(0.8f, 1.5f);
                    }
                    return true;
                }
                default:
                    a.sigT += dt;
                    if (a.sigT < 1.2f) return false;
                    EndMove(a);
                    return true;
            }
        }

        // ---------------------------------------------------------- ox: scratching at a tree

        bool StartScratch(Herd herd)
        {
            var s = herd.spec;
            var a = RandomIdle(herd, true);
            if (a == null || _life == null) return false;
            float reach = NearReach(herd);
            if (!_life.TryRandomPlant(LifeKind.Tree, _rnd, herd.center, reach, out Vector2 tree) && !_life.TryRandomPlant(LifeKind.Bush, _rnd, herd.center, reach, out tree)) return false;
            Vector2 away = a.pos - tree;
            Vector2 u = away.sqrMagnitude > 1e-4f ? away.normalized : new Vector2(1f, 0f);
            float body = s.body * animalScale;
            Vector2 spot = tree + u * (body * 0.45f);
            // Flank-on: the trunk lies on the ox's left.
            Vector2 fwd = new Vector2(-u.y, u.x);
            if (!OkSpot(s, spot) || !OkSpot(s, spot + fwd * (body * 0.4f)) || !OkSpot(s, spot - fwd * (body * 0.4f))) return false;
            a.act = AnimalActivity.Scratch;
            a.step = MoveWalk;
            a.goal = a.from = spot;
            a.dir = fwd;
            herd.sigA = a;
            herd.errandT = 40f;
            return true;
        }

        bool ScratchStep(Herd herd, Animal a, float dt)
        {
            var s = herd.spec;
            switch (a.step)
            {
                case MoveWalk:
                    if (!WalkTo(herd, a, a.goal, 1.2f, dt)) { if (a.blocked > 3f) EndMove(a); return true; }
                    a.step = MovePerform;
                    a.sigT = 0f;
                    a.actT = Rand(6f, 9f);
                    a.yaw = YawOf(a.dir);
                    SetState(a, AnimalState.Look, 999f, LookPitch - 5f, false);
                    return true;
                case MovePerform:
                {
                    a.sigT += dt;
                    Vector2 p = a.from + a.dir * (Mathf.Sin(a.sigT * 2.4f) * s.body * animalScale * 0.28f);
                    if (CanStepMember(s, a, H(p), H(a.pos))) a.pos = p;
                    a.roll = 12f + 4f * Mathf.Sin(a.sigT * 4.8f);
                    SetMoving(a, true);
                    if (a.sigT < a.actT) return true;
                    a.roll = 0f;
                    a.step = MoveFinish;
                    a.sigT = 0f;
                    SetMoving(a, false);
                    SetState(a, AnimalState.Look, 999f, StretchPitch, true);
                    return true;
                }
                default:
                    a.sigT += dt;
                    if (a.sigT < 1.5f) return false;
                    EndMove(a);
                    return true;
            }
        }

        // ---------------------------------------------------------- capybara: snuggle star with an egret

        bool StartSnuggle(Herd herd)
        {
            var s = herd.spec;
            float body = s.body * animalScale;
            Vector2 c = herd.center;
            if (!OkSpot(s, c)) return false;
            int n = 0;
            foreach (var a in herd.members) if (Free(a)) n++;
            if (n < 3) return false;
            float phase = Rand(0f, 6.28f);
            int k = 0, placed = 0;
            foreach (var a in herd.members)
            {
                if (!Free(a)) continue;
                Vector2 p = c + Ang(phase + k++ * (Mathf.PI * 2f / n)) * (body * (Young(a) ? 0.45f : 0.6f));
                if (!Valid(s, H(p)) || Burning(p)) continue;
                a.act = AnimalActivity.Snuggle;
                a.step = MoveWalk;
                a.goal = p;
                a.from = c;
                placed++;
                if (herd.sigA == null && !Young(a)) herd.sigA = a;
            }
            if (placed < 3 || herd.sigA == null) return false;
            herd.errandPos = c;
            herd.errandT = Rand(28f, 40f);
            return true;
        }

        // The egret hops over to another back every six to eight seconds.
        bool SnuggleHerd(Herd herd, float dt)
        {
            herd.sigPhase += dt;
            if (herd.sigPhase < 6f) return false;
            herd.sigPhase = Rand(-2f, 0f);
            int lying = 0;
            foreach (var a in herd.members) if (a.act == AnimalActivity.Snuggle && a.step == MovePerform && a.sigT >= 1f) lying++;
            if (lying < 2) return false;
            int pick = _rnd.Next(lying);
            foreach (var a in herd.members)
            {
                if (a.act != AnimalActivity.Snuggle || a.step != MovePerform || a.sigT < 1f || pick-- != 0) continue;
                if (a == herd.sigA) return false;
                herd.sigA = a;
                return true;
            }
            return false;
        }

        bool SnuggleStep(Herd herd, Animal a, float dt)
        {
            if (a.step == MoveWalk)
            {
                if (!WalkTo(herd, a, a.goal, 1.3f, dt)) { if (a.blocked > 3f) EndMove(a); return true; }
                a.step = MovePerform;
                a.sigT = 0f;
                Vector2 toMiddle = a.from - a.pos;
                if (toMiddle.sqrMagnitude > 1e-6f) a.yaw = YawOf(toMiddle);
                SetState(a, AnimalState.Rest, 999f, RestPitch + 4f, false);
                return true;
            }
            float before = a.sigT;
            a.sigT += dt;
            // The egret lands once its host has lain down (the rest blend takes 0.8 s).
            return a == herd.sigA && before < 1f && a.sigT >= 1f;
        }

        // ---------------------------------------------------------- flamingo: stamping dance in the mud

        bool StampStep(Herd herd, Animal a, float dt)
        {
            var s = herd.spec;
            if (a.step == MoveWalk)
            {
                if (!WalkTo(herd, a, a.from, 1.2f, dt)) { if (a.blocked > 3f) EndMove(a); return true; }
                a.step = MovePerform;
                a.sigT = Rand(0f, 6.28f);
                a.baseYaw = Rand() < 0.5f ? 1f : -1f;
                SetState(a, AnimalState.Graze, 999f, StampPitch, false);
                return true;
            }
            a.sigT += a.baseYaw * 1.9f * dt;
            Vector2 p = a.from + Ang(a.sigT) * (s.body * animalScale * 0.32f);
            if (CanStepMember(s, a, H(p), H(a.pos))) a.pos = p;
            a.yaw = YawOf(Tangent(a.sigT, a.baseYaw));
            SetMoving(a, true);
            return true;
        }

        // ---------------------------------------------------------- tortoise: neck-stretching duel

        bool StartDuel(Herd herd)
        {
            var s = herd.spec;
            var a = RandomIdle(herd, true);
            if (a == null) return false;
            var b = NearestFreeAdult(herd, a);
            if (b == null) return false;
            float body = s.body * animalScale;
            Vector2 mid = (a.pos + b.pos) * 0.5f;
            Vector2 axis = b.pos - a.pos;
            axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Heading(a.yaw);
            Vector2 pa = mid - axis * (body * 0.8f), pb = mid + axis * (body * 0.8f);
            if (!OkSpot(s, pa) || !OkSpot(s, pb)) return false;
            a.act = b.act = AnimalActivity.NeckDuel;
            a.step = b.step = MoveWalk;
            a.goal = pa;
            b.goal = pb;
            a.dir = axis;
            b.dir = -axis;
            herd.sigA = a;
            herd.sigB = b;
            herd.errandT = 60f;
            return true;
        }

        bool DuelHerd(Herd herd)
        {
            var a = herd.sigA;
            var b = herd.sigB;
            switch (herd.choreoStep)
            {
                case 0:
                {
                    if (a == null || b == null || a.act != AnimalActivity.NeckDuel || b.act != AnimalActivity.NeckDuel) { EndSignature(herd); return true; }
                    bool there = a.step == MovePerform && b.step == MovePerform;
                    if (!there && herd.choreoT < 25f) return false;
                    if (!there) { EndSignature(herd); return true; }
                    herd.choreoStep = 1;
                    herd.choreoT = 0f;
                    herd.sigPhase = Rand(4.5f, 6.5f);
                    SetState(a, AnimalState.Look, 999f, NeckUpPitch, true);
                    SetState(b, AnimalState.Look, 999f, NeckUpPitch, true);
                    return true;
                }
                case 1:
                {
                    if (a == null || b == null || a.act != AnimalActivity.NeckDuel || b.act != AnimalActivity.NeckDuel) { EndSignature(herd); return true; }
                    // Up on stretched legs, necks as high as they go.
                    float k = Mathf.Clamp01(herd.choreoT / 0.8f);
                    a.lift = TallLift(a) * k;
                    b.lift = TallLift(b) * k;
                    if (herd.choreoT < herd.sigPhase) return k < 1f;
                    // The lower neck gives way: that one turns and walks off, the winner holds on a moment.
                    var loser = Rand() < 0.5f ? a : b;
                    var winner = loser == a ? b : a;
                    loser.step = MoveFinish;
                    loser.lift = 0f;
                    Vector2 off = loser.pos - loser.dir * (herd.spec.body * animalScale * 3f);
                    loser.goal = OkSpot(herd.spec, off) ? off : loser.pos;
                    SetState(loser, AnimalState.Look, 999f, LookPitch, true);
                    herd.sigA = winner;
                    herd.sigB = loser;
                    herd.choreoStep = 2;
                    herd.choreoT = 0f;
                    return true;
                }
                default:
                    if (a == null || a.act != AnimalActivity.NeckDuel || herd.choreoT < 2.5f) return false;
                    EndMove(a);
                    return true;
            }
        }

        float TallLift(Animal a) => 0.07f * animalScale * a.scale * SizeFactor(a);

        bool DuelStep(Herd herd, Animal a, float dt)
        {
            switch (a.step)
            {
                case MoveWalk:
                    if (!WalkTo(herd, a, a.goal, 1.3f, dt)) { if (a.blocked > 3f) EndMove(a); return true; }
                    a.step = MovePerform;
                    a.yaw = YawOf(a.dir);
                    SetMoving(a, false);
                    SetState(a, AnimalState.Look, 999f, LookPitch, true);
                    return true;
                case MovePerform:
                    return false;
                default:
                    if (WalkTo(herd, a, a.goal, 1f, dt) || a.blocked > 3f) EndMove(a);
                    return true;
            }
        }

        // ---------------------------------------------------------- penguin: sky call running through the colony

        bool StartSkyCall(Herd herd)
        {
            int k = 0;
            foreach (var a in herd.members)
            {
                if (!Free(a)) continue;
                a.act = AnimalActivity.SkyCall;
                a.step = MoveWalk;
                a.sigT = -k * 0.35f;
                a.baseYaw = a.yaw;
                SetMoving(a, false);
                k++;
            }
            if (k < 2) return false;
            herd.errandT = k * 0.35f + 12f;
            return true;
        }

        bool SkyCallStep(Animal a, float dt)
        {
            float before = a.sigT;
            a.sigT += dt;
            switch (a.step)
            {
                case MoveWalk:
                    // Waiting for the wave to arrive, then a deep bow.
                    if (a.sigT < 0f) return false;
                    if (before < 0f) { SetState(a, AnimalState.Look, 999f, BowPitch, false); return true; }
                    if (a.sigT < 0.8f) return false;
                    a.step = MovePerform;
                    a.sigT = 0f;
                    SetState(a, AnimalState.Look, 999f, LookPitch, true);
                    return true;
                case MovePerform:
                    // Beak to the sky, flippers out (AnimalModels.PoseCall), swaying while it calls.
                    a.yaw = a.baseYaw + 14f * Mathf.Sin(a.sigT * 7f);
                    if (a.sigT < 3f) return true;
                    a.yaw = a.baseYaw;
                    a.step = MoveFinish;
                    a.sigT = 0f;
                    SetState(a, AnimalState.Look, 999f, StretchPitch, true);
                    return true;
                default:
                    if (a.sigT < 0.7f) return false;
                    EndMove(a);
                    return true;
            }
        }

        // ---------------------------------------------------------- reindeer: feeding crater, taking turns

        bool StartCrater(Herd herd)
        {
            var s = herd.spec;
            var a = RandomIdle(herd, true);
            if (a == null || NearestFreeAdult(herd, a) == null) return false;
            Vector2 fwd = Heading(a.yaw);
            Vector2 spot = a.pos + fwd * (s.body * animalScale * 0.6f);
            if (!OkSpot(s, spot)) spot = a.pos;
            if (!OkSpot(s, spot)) return false;
            herd.errandPos = spot;
            herd.errandDir = fwd;
            a.act = AnimalActivity.Crater;
            a.step = MoveWalk;
            a.goal = spot;
            herd.sigA = a;
            herd.sigTurns = 1 + _rnd.Next(2);
            herd.errandT = 50f;
            return true;
        }

        bool CraterHerd(Herd herd)
        {
            var s = herd.spec;
            float body = s.body * animalScale;
            var digger = herd.sigA;
            if (digger == null || digger.act != AnimalActivity.Crater) return false;
            Vector2 fwd = herd.errandDir, right = new Vector2(fwd.y, -fwd.x);
            if (herd.sigB != null)
            {
                var next = herd.sigB;
                if (next.act != AnimalActivity.Crater) { herd.sigB = null; return false; }
                if (next.step != MoveQueue || !next.arrived) return false;
                // The newcomer shoulders the digger aside and takes the crater over.
                Vector2 aside = herd.errandPos + right * (body * 1.4f);
                if (!OkSpot(s, aside)) aside = herd.errandPos - right * (body * 1.4f);
                if (!OkSpot(s, aside)) aside = digger.pos;
                digger.step = MoveFinish;
                digger.goal = aside;
                SetMoving(digger, false);
                next.step = MoveWalk;
                next.goal = herd.errandPos;
                next.arrived = false;
                herd.sigA = next;
                herd.sigB = null;
                return true;
            }
            if (digger.step != MovePerform || digger.sigT < 4.5f) return false;
            Animal cand = herd.sigTurns > 0 ? RandomIdle(herd, true) : null;
            if (cand == null) { EndMove(digger); return true; }
            herd.sigTurns--;
            Vector2 queue = herd.errandPos - fwd * (body * 0.9f) - right * (body * 0.8f);
            if (!OkSpot(s, queue)) queue = cand.pos;
            cand.act = AnimalActivity.Crater;
            cand.step = MoveQueue;
            cand.goal = queue;
            cand.arrived = false;
            herd.sigB = cand;
            return true;
        }

        bool CraterStep(Herd herd, Animal a, float dt)
        {
            switch (a.step)
            {
                case MoveWalk:
                    if (!WalkTo(herd, a, a.goal, 1.2f, dt)) { if (a.blocked > 3f) EndMove(a); return true; }
                    a.step = MovePerform;
                    a.sigT = 0f;
                    a.yaw = YawOf(herd.errandDir);
                    SetState(a, AnimalState.Graze, 999f, CraterPitch, false);
                    // Pawing: the legs trample (the shader's walk cycle) while the body stays over the crater.
                    SetMoving(a, true);
                    return true;
                case MovePerform:
                    a.sigT += dt;
                    return false;
                case MoveQueue:
                    if (a.arrived) return false;
                    if (!WalkTo(herd, a, a.goal, 1.2f, dt)) { if (a.blocked > 3f) EndMove(a); return true; }
                    a.arrived = true;
                    Vector2 look = herd.errandPos - a.pos;
                    if (look.sqrMagnitude > 1e-6f) a.yaw = YawOf(look);
                    SetState(a, AnimalState.Look, 999f, LookPitch, true);
                    return true;
                default:
                    if (!WalkTo(herd, a, a.goal, 1.4f, dt) && a.blocked <= 3f) return true;
                    EndMove(a);
                    return true;
            }
        }

        // ---------------------------------------------------------- arctic fox: chasing its own tail

        bool StartTailChase(Herd herd)
        {
            var s = herd.spec;
            var a = RandomIdle(herd, false);
            if (a == null) return false;
            float r = s.body * animalScale * 0.35f;
            float start = Rand(0f, 6.28f);
            Vector2 c = a.pos - Ang(start) * r;
            for (int k = 0; k < 8; k++)
                if (!OkSpot(s, c + Ang(k * (Mathf.PI / 4f)) * r)) return false;
            a.act = AnimalActivity.TailChase;
            a.step = MovePerform;
            a.from = c;
            a.sigT = start;
            a.baseYaw = Rand() < 0.5f ? 1f : -1f;
            a.actT = Rand(2.5f, 3.5f);
            SetState(a, AnimalState.Walk, 0f, WalkPitch, false);
            herd.sigA = a;
            herd.errandT = 20f;
            return true;
        }

        bool TailChaseStep(Herd herd, Animal a, float dt)
        {
            var s = herd.spec;
            if (a.step == MovePerform)
            {
                a.sigT += a.baseYaw * Mathf.PI * 2f * 1.3f * dt;
                Vector2 p = a.from + Ang(a.sigT) * (s.body * animalScale * 0.35f);
                if (CanStepMember(s, a, H(p), H(a.pos))) a.pos = p;
                a.yaw = YawOf(Tangent(a.sigT, a.baseYaw));
                a.roll = 16f * a.baseYaw;
                SetMoving(a, true);
                a.actT -= dt;
                if (a.actT > 0f) return true;
                // Dizzy: it flops down for a moment.
                a.roll = 0f;
                a.step = MoveFinish;
                a.actT = Rand(1.5f, 2.5f);
                SetMoving(a, false);
                SetState(a, AnimalState.Rest, 999f, RestPitch, false);
                return true;
            }
            a.actT -= dt;
            if (a.actT > 0f) return false;
            EndMove(a);
            return true;
        }

        // ---------------------------------------------------------- meerkat: war dance

        bool StartWarDance(Herd herd)
        {
            var s = herd.spec;
            Vector2 c = herd.center;
            Vector2 dir = Ang(Rand(0f, 6.28f));
            const float reach = 0.3f;
            if (!OkSpot(s, c) || !OkSpot(s, c + dir * reach) || !OkSpot(s, c - dir * reach)) return false;
            int k = 0;
            foreach (var a in herd.members)
            {
                if (!Free(a)) continue;
                a.act = AnimalActivity.WarDance;
                a.step = MoveWalk;
                a.arrived = false;
                // Packed tight: every place a third as far from the middle as it is now.
                a.dir = (a.pos - c) * 0.35f;
                k++;
            }
            if (k < 3) return false;
            herd.errandPos = c;
            herd.errandDir = dir;
            herd.sigPhase = Rand(0f, 6.28f);
            herd.errandT = 16f;
            return true;
        }

        bool DanceHerd(Herd herd)
        {
            if (herd.choreoStep != 0) return false;
            int waiting = 0;
            foreach (var a in herd.members) if (a.act == AnimalActivity.WarDance && !a.arrived) waiting++;
            if (waiting > 0 && herd.choreoT < 4f) return false;
            herd.choreoStep = 1;
            herd.choreoT = 0f;
            herd.errandT = Rand(6f, 9f);
            return true;
        }

        bool DanceStep(Herd herd, Animal a, float dt)
        {
            var s = herd.spec;
            float advance = herd.choreoStep == 1 ? Mathf.Sin(herd.choreoT * 1.3f) * 0.3f : 0f;
            Vector2 p = herd.errandPos + herd.errandDir * advance + a.dir;
            if (a.step == MoveWalk)
            {
                if (herd.choreoStep == 1)
                {
                    a.step = MovePerform;
                    a.yaw = YawOf(herd.errandDir);
                    SetState(a, AnimalState.Look, 999f, -12f, true);
                    SetMoving(a, true);
                    return true;
                }
                if (a.arrived) return false;
                if (WalkTo(herd, a, p, 1.3f, dt))
                {
                    a.arrived = true;
                    a.yaw = YawOf(herd.errandDir);
                }
                else if (a.blocked > 3f) EndMove(a);
                return true;
            }
            if (CanStepMember(s, a, H(p), H(a.pos))) a.pos = p;
            a.yaw = YawOf(herd.errandDir);
            SetMoving(a, true);
            return true;
        }

        // ---------------------------------------------------------- zebra grooming, giraffe necking (pairs)

        bool StartPair(Herd herd, AnimalActivity act)
        {
            var s = herd.spec;
            var a = RandomIdle(herd, true);
            if (a == null) return false;
            var b = NearestFreeAdult(herd, a);
            if (b == null) return false;
            float body = s.body * animalScale;
            Vector2 mid = (a.pos + b.pos) * 0.5f;
            Vector2 fwd = Heading(a.yaw);
            Vector2 perp = new Vector2(-fwd.y, fwd.x);
            // Grooming zebras stand side by side head to tail; necking giraffes side by side, both facing ahead.
            float w = act == AnimalActivity.Groom ? body * 0.24f : body * 0.42f;
            Vector2 pa = mid + perp * w, pb = mid - perp * w;
            if (!OkSpot(s, mid) || !OkSpot(s, pa) || !OkSpot(s, pb)) return false;
            a.act = b.act = act;
            a.step = b.step = MoveWalk;
            a.arrived = b.arrived = false;
            a.goal = pa;
            b.goal = pb;
            a.dir = fwd;
            b.dir = act == AnimalActivity.Groom ? -fwd : fwd;
            herd.sigA = a;
            herd.sigB = b;
            herd.errandPos = mid;
            herd.errandDir = fwd;
            herd.sigR = w;
            herd.errandT = 30f;
            return true;
        }

        bool PairHerd(Herd herd, float dt)
        {
            var a = herd.sigA;
            var b = herd.sigB;
            if (a == null || b == null || !IsSignature(a.act) || a.act != b.act) { EndSignature(herd); return true; }
            if (herd.choreoStep == 0)
            {
                bool there = a.arrived && b.arrived;
                if (!there && herd.choreoT < 12f) return false;
                if (!there) { EndSignature(herd); return true; }
                herd.choreoStep = 1;
                herd.choreoT = 0f;
                herd.errandT = a.act == AnimalActivity.Groom ? Rand(10f, 16f) : Rand(8f, 12f);
                a.step = b.step = MovePerform;
                a.sigT = 0f;
                b.sigT = 0.2f;
                return true;
            }
            // Grooming works along the flank: the pair turns slowly about its middle.
            if (a.act == AnimalActivity.Groom) herd.sigPhase += 0.22f * dt;
            return false;
        }

        bool PairStep(Herd herd, Animal a, float dt)
        {
            var s = herd.spec;
            bool first = a == herd.sigA;
            if (a.step == MoveWalk)
            {
                if (a.arrived) return false;
                if (!WalkTo(herd, a, a.goal, 1.3f, dt)) { if (a.blocked > 3f) EndMove(a); return true; }
                a.arrived = true;
                a.yaw = YawOf(a.dir);
                SetState(a, AnimalState.Look, 999f, LookPitch, false);
                return true;
            }
            a.sigT += dt;
            if (a.act == AnimalActivity.Groom)
            {
                Vector2 fwd = Rotate(herd.errandDir, herd.sigPhase);
                Vector2 perp = new Vector2(-fwd.y, fwd.x);
                Vector2 p = herd.errandPos + perp * (first ? herd.sigR : -herd.sigR);
                if (CanStepMember(s, a, H(p), H(a.pos))) a.pos = p;
                a.yaw = YawOf(first ? fwd : -fwd);
                // Leaning into the partner (it stands on the right), nibbling its withers in short dips.
                a.roll = -5f;
                if (a.sigT >= 0.45f)
                {
                    a.sigT = 0f;
                    SetState(a, AnimalState.Graze, 999f, a.pitchTo == 22f ? 4f : 22f, false);
                }
                return true;
            }
            // Necking: the long necks swing at each other, out of step (the partner of sigA is on its right).
            float swing = Mathf.Sin(herd.choreoT * 2.2f + (first ? 0f : 2.2f));
            a.roll = first ? -20f * swing : 20f * swing;
            if (a.pitchTo != -10f) SetState(a, AnimalState.Look, 999f, -10f, true);
            return true;
        }
    }
}
