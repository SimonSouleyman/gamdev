using Drift.Core;
using UnityEngine;

namespace Drift.Life
{
    // How two herds that meet interact (IslandHerdSystem.Meetings.cs).
    public enum MeetPattern { Greet, Tag, Shove, Trek, RingDance }

    // Herd meetings (2026-09-24, owner: "Gruppen von Tieren sollen miteinander interagieren, wenn sie sich treffen.
    // Mache dafür 5 verschiedene Bewegungsmuster."): two calm, awake herds of any species within meetRange walk
    // towards each other (Errand.Meet, stage 0: each herd walks its own share of the gap at its own pace), then the
    // lead herd runs the pattern for both (all members owned by the meeting, stepped from the lead's MoveHerd):
    //   Greet      the front animals walk out and meet nose to nose, sniff, nod, rub cheeks; the others lean in
    //   Tag        young (else one or two adults of each herd) chase each other round both herds, turn at half time
    //   Shove      two leaders push head to head in small shoves, two rounds; the others watch from two arcs
    //   Trek       both herds walk off as one long single file (the initiator in front), then fork apart
    //   RingDance  the two herds orbit each other as groups, then line up side by side and bow together
    // Every spot is checked with OkSpot / CanStepMember for the species that stands there; a pattern whose ground
    // does not fit falls back to the greeting. Startle, fire, rising water, storm, night, the far tier and reshapes
    // that spoil a spot cancel it like every errand (CancelErrand -> AbortMeeting ends it for both herds). At the end
    // both herds re-centre on their members and walk apart until they are herdSpacing + radii apart again.
    // Draws come from a stream of their own (_meetRnd), so the island's other behaviours keep their sequences.
    public partial class IslandHerdSystem
    {
        [Header("Begegnungen zweier Herden")]
        [Tooltip("Chance pro Denk-Takt (1 s), dass eine ruhige Herde tagsüber eine Nachbarherde in Reichweite trifft: Begrüßen, Fangenspiel, Kräftemessen, gemeinsamer Zug oder Kreistanz.")]
        [Range(0f, 0.1f)] public float meetRate = 0.006f;
        [Tooltip("So nah (Einheiten, Mitte zu Mitte) muss eine andere Herde sein, damit sich die beiden von selbst treffen.")]
        [Range(1f, 12f)] public float meetRange = 4f;
        [Tooltip("Reichweite, in der der Regisseur eine Begegnung anstößt.")]
        [Range(1f, 15f)] public float meetDirectorRange = 6f;
        [Tooltip("Abklingzeit einer Herde nach einer Begegnung (s, von–bis).")]
        public Vector2 meetCooldown = new Vector2(70f, 130f);
        [Tooltip("Pause (s, von–bis) zwischen zwei Begegnungen auf derselben Insel, die von selbst beginnen.")]
        public Vector2 meetGap = new Vector2(10f, 20f);
        [Tooltip("Höchstens so viele Begegnungen gleichzeitig beginnen auf einer Insel von selbst.")]
        [Range(1, 6)] public int maxMeetings = 2;
        [Tooltip("Tempo, mit dem zwei Herden aufeinander zugehen (× Wandertempo).")]
        [Range(1f, 3f)] public float meetApproachPace = 1.5f;
        [Tooltip("So lange (s) dürfen zwei Herden höchstens brauchen, um zueinander zu finden (vom Regisseur angestoßen: 1,5-mal so lange).")]
        [Range(2f, 20f)] public float meetApproachTime = 8f;

        const int MeetApproach = 0, MeetGather = 1, MeetPerform = 2;

        sealed class Meeting
        {
            public Herd a, b;
            public MeetPattern pattern;
            public int phase, step;
            public bool natural, ended, flipped;
            // t = seconds in the phase, t2 = seconds in a sub-step, total = length of the performance.
            public float t, t2, total, lastStep, want;
            // mid = the meeting point (ring / chase centre, trail start), axis = from a towards b, dir = trail direction.
            public Vector2 mid, axis, dir;
            public Animal leadA, leadB;
            // radius of the chase / orbit, speed (angular for chase and orbit, linear for the trek), head = trail
            // length walked by the head, sign = turning direction, turn = chase gap in radians / orbit angle total.
            public float radius, speed, head, sign, turn, hold, angA, angB, len;
        }

        System.Random _meetRnd;
        float _meetNextAt;
        int _lastMeet = -1;
        readonly int[] _meetStarted = new int[5];
        readonly int[] _meetDone = new int[5];
        Animal[] _meetPick = new Animal[64];
        float[] _meetKey = new float[64];

        public int MeetingsStarted { get; private set; }
        public int MeetingsCompleted { get; private set; }
        public int MeetingsAborted { get; private set; }
        public int MeetingsStartedOf(MeetPattern p) => _meetStarted[(int)p];
        public int MeetingsCompletedOf(MeetPattern p) => _meetDone[(int)p];

        public static bool IsMeeting(AnimalActivity act) => act >= AnimalActivity.Greet && act <= AnimalActivity.RingDance;

        public int ActiveMeetings
        {
            get
            {
                int n = 0;
                foreach (var h in _herds) if (h.meeting != null && h.meeting.a == h && !h.meeting.ended) n++;
                return n;
            }
        }

        // -1 when the herd is not in a meeting.
        public int HerdMeetingPartner(int index)
        {
            var h = _herds[index];
            var m = h.meeting;
            if (m == null) return -1;
            return _herds.IndexOf(m.a == h ? m.b : m.a);
        }

        public int HerdMeetingPattern(int index) => _herds[index].meeting != null ? (int)_herds[index].meeting.pattern : -1;
        // 0 = walking towards each other, 1 = taking places, 2 = performing; -1 outside of a meeting.
        public int HerdMeetingPhase(int index) => _herds[index].meeting != null ? _herds[index].meeting.phase : -1;

        float MRand()
        {
            _meetRnd ??= new System.Random(HerdSeed ^ 0x3C6EF35);
            return (float)_meetRnd.NextDouble();
        }

        float MRand(float a, float b) => a + MRand() * (b - a);

        float Len(Herd h, Animal a) => h.spec.body * animalScale * a.scale * SizeFactor(a);
        float BodyOf(Herd h) => h.spec.body * animalScale;
        static bool Calm(Herd h) => h.fleeTimer <= 0f && !h.fleeing && !h.refuging;

        static bool Strong(LifeKind k) =>
            k == LifeKind.Goat || k == LifeKind.Ox || k == LifeKind.Reindeer || k == LifeKind.Giraffe || k == LifeKind.Zebra;

        // ---------------------------------------------------------- start

        // Starts a meeting of the herd with `partnerIndex` (-1: the nearest free herd within meetDirectorRange) in
        // `pattern` (-1: chosen by weight). The cozy LifeDirector and the tests call it; false when the herds are busy,
        // asleep or too far apart, or the ground between them does not fit.
        public bool TryStartMeeting(int herdIndex, int partnerIndex = -1, int pattern = -1)
        {
            if (!HerdFree(herdIndex)) return false;
            var a = _herds[herdIndex];
            if (!MeetFree(a, false)) return false;
            Herd b = null;
            if (partnerIndex >= 0)
            {
                if (partnerIndex >= _herds.Count || partnerIndex == herdIndex || !MeetFree(_herds[partnerIndex], false)) return false;
                b = _herds[partnerIndex];
            }
            return StartMeeting(a, b, pattern, false);
        }

        // Think's roll: a calm herd by day, off cooldown, settled on its island.
        bool MeetThink(Herd herd)
        {
            if (meetRate <= 0f || behaviourRate <= 0f || _night > 0.3f || herd.settleT > 0f) return false;
            if (_stepClock < herd.meetAt || _stepClock < _meetNextAt) return false;
            if (MRand() >= meetRate * behaviourRate) return false;
            return StartMeeting(herd, null, -1, true);
        }

        bool MeetFree(Herd h, bool natural)
        {
            if (h.members.Count == 0 || h.meeting != null || h.errand != Errand.None) return false;
            if (h.fleeing || h.fleeTimer > 0f || h.refuging || h.diving || h.playT > 0f || h.stampT > 0f || h.tuckT > 0f) return false;
            if (natural && (h.settleT > 0f || _stepClock < h.meetAt)) return false;
            foreach (var a in h.members) if (a.hidden || a.shore || a.state == AnimalState.Sleep) return false;
            return ValidTarget(h.spec, H(h.center));
        }

        Herd FindPartner(Herd a, float range, bool natural)
        {
            Herd best = null;
            float bestD = range * range;
            foreach (var o in _herds)
            {
                if (o == a) continue;
                float d = (o.center - a.center).sqrMagnitude;
                if (d >= bestD || !MeetFree(o, natural)) continue;
                bestD = d;
                best = o;
            }
            return best;
        }

        MeetPattern PickPattern(Herd a, Herd b)
        {
            int young = 0;
            foreach (var x in a.members) if (Young(x) && !x.hidden) young++;
            foreach (var x in b.members) if (Young(x) && !x.hidden) young++;
            bool tort = a.spec.kind == LifeKind.Tortoise || b.spec.kind == LifeKind.Tortoise;
            float greet = 3f;
            float tag = tort ? 0f : (young >= 2 ? 3.5f : 1.3f) * (a.spec.play > 0f && b.spec.play > 0f ? 1f : 0.4f);
            float shove = Strong(a.spec.kind) && Strong(b.spec.kind) ? 3f : a.spec == b.spec ? (tort ? 0.3f : 0.7f) : 0f;
            float trek = (a.members.Count + b.members.Count >= 5 ? 2f : 0.8f) * (tort ? 0.2f : 1f);
            float ring = tort ? 0.4f : 2f;
            switch (_lastMeet)
            {
                case 0: greet *= 0.35f; break;
                case 1: tag *= 0.35f; break;
                case 2: shove *= 0.35f; break;
                case 3: trek *= 0.35f; break;
                case 4: ring *= 0.35f; break;
            }
            float r = MRand() * (greet + tag + shove + trek + ring);
            if ((r -= greet) < 0f) return MeetPattern.Greet;
            if ((r -= tag) < 0f) return MeetPattern.Tag;
            if ((r -= shove) < 0f) return MeetPattern.Shove;
            if ((r -= trek) < 0f) return MeetPattern.Trek;
            return MeetPattern.RingDance;
        }

        // Centre distance the two herds walk to before the pattern starts.
        float MeetDistance(Herd a, Herd b, MeetPattern p)
        {
            float ra = RadiusOf(a), rb = RadiusOf(b), body = Mathf.Max(BodyOf(a), BodyOf(b));
            switch (p)
            {
                case MeetPattern.Greet: return ra + rb + 3f * body;
                case MeetPattern.Trek: return ra + rb + body;
                case MeetPattern.RingDance: return 0.6f * (ra + rb) + 2f * body;
                default: return ra + rb + 2f * body;
            }
        }

        // Every 0.25 u of the straight way is ground the herd centre may cross: above the species' shore margin (below it
        // MoveHerd's rising-shore rule would call the herd uphill and end the meeting) and not burning.
        bool WalkableLine(Species s, Vector2 from, Vector2 to)
        {
            Vector2 d = to - from;
            int n = Mathf.CeilToInt(d.magnitude / 0.25f);
            for (int i = 1; i <= n; i++)
            {
                Vector2 p = from + d * (i / (float)n);
                if (!ValidTarget(s, H(p)) || Burning(p)) return false;
            }
            return true;
        }

        bool StartMeeting(Herd a, Herd b, int pattern, bool natural)
        {
            if (natural && ActiveMeetings >= maxMeetings) return false;
            if (b == null) b = FindPartner(a, natural ? meetRange : meetDirectorRange, natural);
            if (b == null)
            {
                if (natural) a.meetAt = _stepClock + MRand(8f, 16f);
                return false;
            }
            var p = pattern >= 0 && pattern <= 4 ? (MeetPattern)pattern : PickPattern(a, b);
            Vector2 ca = a.center, cb = b.center, d = cb - ca;
            float dist = d.magnitude;
            Vector2 u = dist > 1e-3f ? d / dist : Ang(MRand(0f, 6.28f));
            float want = MeetDistance(a, b, p);
            float va = a.spec.speed * meetApproachPace, vb = b.spec.speed * meetApproachPace;
            float move = dist - want, fa = va / (va + vb);
            Vector2 sa = ca + u * (move * fa), sb = cb - u * (move * (1f - fa));
            if (Mathf.Abs(move * fa) < 0.3f) sa = ca;
            if (Mathf.Abs(move * (1f - fa)) < 0.3f) sb = cb;
            float time = Mathf.Max((sa - ca).magnitude / va, (sb - cb).magnitude / vb);
            bool ok = time <= meetApproachTime * (natural ? 1f : 1.5f)
                && (sa == ca || (OkSpot(a.spec, sa) && WalkableLine(a.spec, ca, sa)))
                && (sb == cb || (OkSpot(b.spec, sb) && WalkableLine(b.spec, cb, sb)));
            if (!ok)
            {
                if (natural) a.meetAt = _stepClock + MRand(10f, 20f);
                return false;
            }
            var m = new Meeting { a = a, b = b, pattern = p, natural = natural, phase = MeetApproach, axis = u, mid = (sa + sb) * 0.5f, want = want, lastStep = _stepClock };
            PrepareMeet(a, sa, m);
            PrepareMeet(b, sb, m);
            MeetingsStarted++;
            _meetStarted[(int)p]++;
            _lastMeet = (int)p;
            if (natural) _meetNextAt = _stepClock + MRand(meetGap.x, Mathf.Max(meetGap.x, meetGap.y));
            if (Moments.Listening && _surface != null) Moments.Report(MomentKind.Meeting, transform, m.mid, H(m.mid));
            return true;
        }

        void PrepareMeet(Herd h, Vector2 spot, Meeting m)
        {
            EndPlay(h, false);
            ReleaseLookout(h);
            StopDig(h);
            foreach (var x in h.members)
            {
                if (x.act == AnimalActivity.None || x.hasGoal) continue;
                x.act = AnimalActivity.None;
                x.pitch = x.lift = x.roll = 0f;
                x.step = 0;
            }
            h.errand = Errand.Meet;
            h.errandStage = 0;
            h.meeting = m;
            h.target = spot;
            h.wait = 0f;
        }

        // The herd reached its meeting spot (ArriveErrand); it stands and waits for the other one.
        void ArriveMeeting(Herd herd)
        {
            herd.errandStage = 1;
            herd.wait = Mathf.Max(herd.wait, 1f);
        }

        bool MeetingSurvivesReshape(Herd herd)
        {
            var s = herd.spec;
            if (herd.errandStage == 0 && !ValidTarget(s, H(herd.target))) return false;
            foreach (var x in herd.members)
            {
                if (!IsMeeting(x.act)) continue;
                if (!Valid(s, H(x.pos))) return false;
                if (!x.arrived && !ValidTarget(s, H(x.goal))) return false;
            }
            return true;
        }

        void ShiftMeeting(Meeting m, Vector2 delta) => m.mid += delta;

        // ---------------------------------------------------------- stepping

        // From MoveHerd for a herd on Errand.Meet: both herds stand while the meeting runs, the lead herd steps it.
        bool MeetHerd(Herd herd, float dt)
        {
            var m = herd.meeting;
            if (m == null || m.ended)
            {
                CancelErrand(herd);
                return true;
            }
            if (herd.errandStage == 1 && herd.wait < 0.5f) herd.wait = 0.5f;
            if (m.a != herd)
            {
                // The lead herd is gone (merged away, drowned, dropped by the caps): end it from here.
                if (m.a == null || m.a.meeting != m || _stepClock - m.lastStep > 1.5f)
                {
                    EndMeeting(m, false);
                    return true;
                }
                return false;
            }
            return StepMeeting(m, dt);
        }

        bool StepMeeting(Meeting m, float dt)
        {
            m.lastStep = _stepClock;
            var a = m.a;
            var b = m.b;
            if (b == null || b.meeting != m || b.errand != Errand.Meet || b.members.Count == 0 || !_herds.Contains(b))
            {
                EndMeeting(m, false);
                return true;
            }
            m.t += dt;
            switch (m.phase)
            {
                case MeetApproach:
                {
                    bool there = a.errandStage == 1 && b.errandStage == 1;
                    float limit = meetApproachTime * (m.natural ? 1f : 1.5f) + 3f;
                    if (!there && m.t < limit) return false;
                    if (!there && (a.center - b.center).magnitude > m.want * 1.6f + 0.5f)
                    {
                        EndMeeting(m, false);
                        return true;
                    }
                    StandStill(a);
                    StandStill(b);
                    if (!Begin(m, m.pattern) && (m.pattern == MeetPattern.Greet || !Begin(m, MeetPattern.Greet)))
                    {
                        EndMeeting(m, false);
                        return true;
                    }
                    m.phase = MeetGather;
                    m.t = 0f;
                    return true;
                }
                case MeetGather:
                {
                    bool changed = false;
                    bool all = GatherStep(a, dt, ref changed) & GatherStep(b, dt, ref changed);
                    if (!all && m.t < 6f) return changed;
                    SettleGather(a);
                    SettleGather(b);
                    m.phase = MeetPerform;
                    m.t = m.t2 = 0f;
                    StartPerform(m);
                    return true;
                }
                default:
                {
                    bool done = false;
                    bool changed = Perform(m, dt, ref done);
                    if (!done) return changed;
                    EndMeeting(m, true);
                    return true;
                }
            }
        }

        void StandStill(Herd h)
        {
            h.errandStage = 1;
            h.target = h.center;
            h.wait = Mathf.Max(h.wait, 1f);
        }

        // Owned members walk to their places; true once all stand there.
        bool GatherStep(Herd h, float dt, ref bool changed)
        {
            bool all = true;
            foreach (var x in h.members)
            {
                if (!IsMeeting(x.act) || x.hidden) continue;
                if (x.arrived)
                {
                    if (Mathf.Abs(Mathf.DeltaAngle(x.yaw, x.baseYaw)) > 1f)
                    {
                        x.yaw = Mathf.LerpAngle(x.yaw, x.baseYaw, 1f - Mathf.Exp(-6f * dt));
                        changed = true;
                    }
                    continue;
                }
                all = false;
                changed = true;
                float pace = x.act == AnimalActivity.Tag ? 2f : 1.3f;
                if (WalkTo(h, x, x.goal, pace, dt)) Arrive(x);
                else if (x.blocked > 1.5f)
                {
                    x.goal = x.pos;
                    Arrive(x);
                }
            }
            return all;
        }

        void Arrive(Animal x)
        {
            x.arrived = true;
            x.blocked = 0f;
            SetMoving(x, false);
            SetState(x, AnimalState.Look, 999f, LookPitch, true);
        }

        void SettleGather(Herd h)
        {
            foreach (var x in h.members)
            {
                if (!IsMeeting(x.act) || x.hidden) continue;
                if (!x.arrived)
                {
                    x.goal = x.pos;
                    Arrive(x);
                }
                x.yaw = x.baseYaw;
            }
        }

        // Makes the member part of the meeting with a place to walk to and a heading to take there.
        void Own(Animal x, AnimalActivity act, Vector2 goal, float yaw)
        {
            x.act = act;
            x.hasGoal = false;
            x.goal = goal;
            x.from = goal;
            x.baseYaw = yaw;
            x.step = MoveWalk;
            x.pitch = x.lift = x.roll = 0f;
            x.blocked = x.sigT = x.actT = 0f;
            x.arrived = (goal - x.pos).sqrMagnitude < 1e-6f;
            if (x.arrived) Arrive(x);
        }

        void OwnRest(Herd h, AnimalActivity act, Vector2 face)
        {
            foreach (var x in h.members)
            {
                if (x.hidden || IsMeeting(x.act)) continue;
                Vector2 to = face - x.pos;
                Own(x, act, x.pos, to.sqrMagnitude > 1e-6f ? YawOf(to) : x.yaw);
            }
        }

        void ReleaseMembers(Herd h)
        {
            foreach (var x in h.members)
            {
                if (!IsMeeting(x.act)) continue;
                ClearGoal(x);
                SetMoving(x, false);
                SetState(x, AnimalState.Look, MRand(1f, 2.5f), LookPitch, true);
            }
        }

        // The adult (else anyone) nearest to `toward`.
        Animal Front(Herd h, Vector2 toward)
        {
            Animal best = null;
            float bd = float.MaxValue;
            bool bestAdult = false;
            foreach (var x in h.members)
            {
                if (x.hidden) continue;
                bool adult = !Young(x);
                float d = (x.pos - toward).sqrMagnitude;
                if (best == null || (adult && !bestAdult) || (adult == bestAdult && d < bd))
                {
                    best = x;
                    bd = d;
                    bestAdult = adult;
                }
            }
            return best;
        }

        void EnsurePick(int n)
        {
            if (_meetPick.Length >= n) return;
            _meetPick = new Animal[n * 2];
            _meetKey = new float[n * 2];
        }

        // The herd's visible members sorted by key (insertion sort into the shared scratch arrays); returns the count.
        int SortMembers(Herd h, Vector2 origin, Vector2 axis, bool byDistance)
        {
            EnsurePick(h.members.Count);
            int n = 0;
            foreach (var x in h.members)
            {
                if (x.hidden) continue;
                Vector2 r = x.pos - origin;
                float key = byDistance ? r.sqrMagnitude : Vector2.Dot(r, axis);
                int k = n++;
                while (k > 0 && _meetKey[k - 1] > key)
                {
                    _meetKey[k] = _meetKey[k - 1];
                    _meetPick[k] = _meetPick[k - 1];
                    k--;
                }
                _meetKey[k] = key;
                _meetPick[k] = x;
            }
            return n;
        }

        // ---------------------------------------------------------- end

        // Ends the meeting for both herds (danger, night or a spot gone: CancelErrand of either herd).
        void AbortMeeting(Herd herd)
        {
            if (herd.meeting != null && !herd.meeting.ended) EndMeeting(herd.meeting, false);
            else
            {
                herd.meeting = null;
                herd.errand = Errand.None;
                herd.errandStage = 0;
                ReleaseMembers(herd);
            }
        }

        void EndMeeting(Meeting m, bool completed)
        {
            if (m.ended) return;
            m.ended = true;
            var a = m.a;
            var b = m.b;
            bool aIn = a != null && a.meeting == m, bIn = b != null && b.meeting == m;
            if (aIn) LeaveMeeting(a);
            if (bIn) LeaveMeeting(b);
            if (aIn && bIn) Separate(a, b);
            else if (aIn && b != null) Separate(a, b);
            else if (bIn && a != null) Separate(b, a);
            if (completed)
            {
                MeetingsCompleted++;
                _meetDone[(int)m.pattern]++;
            }
            else MeetingsAborted++;
            _meshDirty = true;
        }

        void LeaveMeeting(Herd h)
        {
            ReleaseMembers(h);
            h.meeting = null;
            h.errand = Errand.None;
            h.errandStage = 0;
            h.meetAt = _stepClock + MRand(meetCooldown.x, Mathf.Max(meetCooldown.x, meetCooldown.y));
            if (!Calm(h)) return;
            Vector2 sum = Vector2.zero;
            int n = 0;
            foreach (var x in h.members)
            {
                if (x.hidden) continue;
                sum += x.pos;
                n++;
            }
            if (n > 0 && Valid(h.spec, H(sum / n))) h.center = sum / n;
            h.target = h.center;
            h.wait = MRand(1f, 3f);
        }

        // Both walk away from each other until they are herdSpacing (or their radii) apart again, plus a margin.
        void Separate(Herd a, Herd b)
        {
            Vector2 d = a.center - b.center;
            float dist = d.magnitude;
            Vector2 u = dist > 1e-3f ? d / dist : Ang(MRand(0f, 6.28f));
            float need = Mathf.Max(herdSpacing, RadiusOf(a) + RadiusOf(b)) + 0.4f;
            float push = Mathf.Max(0f, need - dist) * 0.5f + 0.5f;
            bool calmA = a.meeting == null && Calm(a), calmB = b.meeting == null && Calm(b);
            if (calmA) Walkaway(a, u, calmB ? push : push * 2f);
            if (calmB) Walkaway(b, -u, calmA ? push : push * 2f);
        }

        void Walkaway(Herd h, Vector2 dir, float dist)
        {
            for (int k = 0; k < 7; k++)
            {
                float ang = ((k + 1) >> 1) * 35f * ((k & 1) == 0 ? 1f : -1f) * Mathf.Deg2Rad;
                Vector2 t = h.center + Rotate(dir, ang) * dist;
                if (!OkSpot(h.spec, t) || !WalkableLine(h.spec, h.center, t)) continue;
                h.target = t;
                h.wait = 0f;
                return;
            }
            h.target = NewTarget(h);
            h.wait = 0f;
        }

        // ---------------------------------------------------------- the five patterns: places

        bool Begin(Meeting m, MeetPattern p)
        {
            ReleaseMembers(m.a);
            ReleaseMembers(m.b);
            m.pattern = p;
            m.leadA = m.leadB = null;
            m.flipped = false;
            m.step = 0;
            m.hold = m.head = 0f;
            Vector2 d = m.b.center - m.a.center;
            if (d.sqrMagnitude > 1e-6f) m.axis = d.normalized;
            m.mid = (m.a.center + m.b.center) * 0.5f;
            bool ok;
            switch (p)
            {
                case MeetPattern.Greet: ok = BeginGreet(m); break;
                case MeetPattern.Tag: ok = BeginTag(m); break;
                case MeetPattern.Shove: ok = BeginShove(m); break;
                case MeetPattern.Trek: ok = BeginTrek(m); break;
                default: ok = BeginRing(m); break;
            }
            if (!ok)
            {
                ReleaseMembers(m.a);
                ReleaseMembers(m.b);
            }
            return ok;
        }

        bool BeginGreet(Meeting m)
        {
            var a = m.a;
            var b = m.b;
            var la = Front(a, b.center);
            var lb = Front(b, a.center);
            if (la == null || lb == null) return false;
            Vector2 mid = (la.pos + lb.pos) * 0.5f;
            Vector2 u = lb.pos - la.pos;
            u = u.sqrMagnitude > 1e-6f ? u.normalized : m.axis;
            Vector2 ga = mid - u * (Len(a, la) * 0.47f), gb = mid + u * (Len(b, lb) * 0.47f);
            if (!OkSpot(a.spec, ga) || !OkSpot(b.spec, gb)) return false;
            m.leadA = la;
            m.leadB = lb;
            m.mid = mid;
            m.axis = u;
            Own(la, AnimalActivity.Greet, ga, YawOf(u));
            Own(lb, AnimalActivity.Greet, gb, YawOf(-u));
            LeanIn(a, mid);
            LeanIn(b, mid);
            m.total = MRand(7.5f, 10f);
            return true;
        }

        // The rest of the herd takes a step or two towards the greeting and watches it.
        void LeanIn(Herd h, Vector2 mid)
        {
            foreach (var x in h.members)
            {
                if (x.hidden || IsMeeting(x.act)) continue;
                float len = Len(h, x);
                Vector2 to = mid - x.pos;
                float d = to.magnitude;
                Vector2 g = x.pos;
                if (d > 1e-4f)
                {
                    g = x.pos + to / d * Mathf.Min(1.2f * len, Mathf.Max(0f, d - 2.5f * len));
                    if (!OkSpot(h.spec, g)) g = x.pos;
                }
                Vector2 face = mid - g;
                Own(x, AnimalActivity.Greet, g, face.sqrMagnitude > 1e-6f ? YawOf(face) : x.yaw);
            }
        }

        bool BeginTag(Meeting m)
        {
            var a = m.a;
            var b = m.b;
            Vector2 c = m.mid;
            float body = Mathf.Max(BodyOf(a), BodyOf(b));
            // Runners: the young of both herds (up to four, taking turns between the herds), topped up with the adult
            // nearest to the other herd so both herds run.
            EnsurePick(8);
            int n = 0, na = 0, nb = 0;
            for (int i = 0; n < 4 && (i < a.members.Count || i < b.members.Count); i++)
            {
                if (i < a.members.Count && n < 4 && Runner(a.members[i], true)) { _meetPick[n] = a.members[i]; _meetKey[n++] = 0f; na++; }
                if (i < b.members.Count && n < 4 && Runner(b.members[i], true)) { _meetPick[n] = b.members[i]; _meetKey[n++] = 1f; nb++; }
            }
            if (na == 0 && n < 4) { var x = Front(a, b.center); if (x != null) { _meetPick[n] = x; _meetKey[n++] = 0f; na++; } }
            if (nb == 0 && n < 4) { var x = Front(b, a.center); if (x != null) { _meetPick[n] = x; _meetKey[n++] = 1f; nb++; } }
            if (n < 2 || na == 0 || nb == 0) return false;
            float dist = (a.center - b.center).magnitude;
            float big = Mathf.Max(RadiusOf(a), RadiusOf(b));
            float r = dist * 0.5f + big + 1.2f * body, rMin = dist * 0.5f + 0.5f * big + 0.5f * body;
            bool fits = false;
            for (int t = 0; t < 3 && !fits; t++)
            {
                fits = true;
                for (int k = 0; k < 16 && fits; k++)
                {
                    Vector2 p = c + Ang(k * (Mathf.PI / 8f)) * r;
                    if (!OkSpot(a.spec, p) || !OkSpot(b.spec, p)) fits = false;
                }
                if (!fits) r = Mathf.Lerp(r, rMin, 0.5f + 0.25f * t);
            }
            if (!fits) return false;
            // Taking turns: A, B, A, B round the ring, a couple of bodies apart.
            float gap = 2.4f * body / r;
            Vector2 first = _meetPick[0].pos - c;
            float th0 = first.sqrMagnitude > 1e-6f ? Mathf.Atan2(first.y, first.x) : 0f;
            int ia = 0, ib = 0, rank = 0;
            for (int k = 0; k < n; k++)
            {
                bool fromA = rank % 2 == 0 ? ia < na : ib >= nb;
                Animal x = null;
                int seen = 0;
                for (int j = 0; j < n; j++)
                {
                    if ((_meetKey[j] == 0f) != fromA) continue;
                    if (seen++ == (fromA ? ia : ib)) { x = _meetPick[j]; break; }
                }
                if (fromA) ia++; else ib++;
                float ang = th0 - rank * gap;
                Own(x, AnimalActivity.Tag, c + Ang(ang) * r, YawOf(Tangent(ang, 1f)));
                x.sigT = ang;
                x.actT = rank++;
            }
            OwnRest(a, AnimalActivity.Cheer, c);
            OwnRest(b, AnimalActivity.Cheer, c);
            m.mid = c;
            m.radius = r;
            m.turn = gap;
            m.sign = 1f;
            float v = Mathf.Clamp(Mathf.Min(a.spec.speed, b.spec.speed) * 2.6f, 0.2f, 1.4f);
            m.speed = v / r;
            m.total = MRand(8f, 12f);
            m.len = n;
            return true;
        }

        bool Runner(Animal x, bool youngOnly) => !x.hidden && (!youngOnly || Young(x)) && x.state != AnimalState.Sleep;

        bool BeginShove(Meeting m)
        {
            var a = m.a;
            var b = m.b;
            var la = Front(a, b.center);
            var lb = Front(b, a.center);
            if (la == null || lb == null) return false;
            Vector2 u = m.axis;
            Vector2 mid = m.mid;
            float lenA = Len(a, la), lenB = Len(b, lb), body = Mathf.Max(BodyOf(a), BodyOf(b));
            Vector2 ga = mid - u * (lenA * 0.46f), gb = mid + u * (lenB * 0.46f);
            if (!OkSpot(a.spec, ga) || !OkSpot(b.spec, gb)) return false;
            m.leadA = la;
            m.leadB = lb;
            Own(la, AnimalActivity.Shove, ga, YawOf(u));
            Own(lb, AnimalActivity.Shove, gb, YawOf(-u));
            // Spectators on two arcs of up to 140 degrees, each herd on its own side, as wide as the bigger side needs.
            int ra = a.members.Count - 1, rb = b.members.Count - 1;
            float span = 2.45f;
            float rw = Mathf.Max((lenA + lenB) * 0.5f + 1.8f * body, Mathf.Max(ra, rb) * 1.2f * body / span);
            Arc(a, mid, -u, rw, span, AnimalActivity.Cheer);
            Arc(b, mid, u, rw, span, AnimalActivity.Cheer);
            m.radius = rw;
            m.sign = MRand() < 0.5f ? 1f : -1f;
            m.total = MRand(7f, 10f);
            m.len = body;
            return true;
        }

        // The herd's free members on an arc round `mid` facing it, in the order they stand, so no paths cross.
        void Arc(Herd h, Vector2 mid, Vector2 side, float r, float span, AnimalActivity act)
        {
            Vector2 across = new Vector2(-side.y, side.x);
            int n = SortMembers(h, mid, across, false);
            int free = 0;
            for (int i = 0; i < n; i++) if (!IsMeeting(_meetPick[i].act)) _meetPick[free++] = _meetPick[i];
            float step = free > 1 ? Mathf.Min(span / (free - 1), 1.5f * BodyOf(h) / r) : 0f;
            float baseAng = Mathf.Atan2(side.y, side.x);
            for (int k = 0; k < free; k++)
            {
                var x = _meetPick[k];
                // Sorted along `across` (= +90 degrees from side): the first stands at the most negative angle.
                float ang = baseAng + (k - (free - 1) * 0.5f) * step;
                Vector2 g = mid + Ang(ang) * r;
                if (!OkSpot(h.spec, g)) g = x.pos;
                Vector2 face = mid - g;
                Own(x, act, g, face.sqrMagnitude > 1e-6f ? YawOf(face) : x.yaw);
            }
            System.Array.Clear(_meetPick, 0, n);
        }

        bool BeginTrek(Meeting m)
        {
            var a = m.a;
            var b = m.b;
            Vector2 p0 = m.mid;
            if (!OkSpot(a.spec, p0) || !OkSpot(b.spec, p0)) return false;
            float v = Mathf.Max(0.05f, Mathf.Min(a.spec.speed, b.spec.speed) * 1.25f);
            float want = Mathf.Clamp(v * MRand(7f, 9f), 1.2f, 4.2f);
            const float splitRoom = 0.9f;
            Vector2 side = new Vector2(-m.axis.y, m.axis.x);
            if (MRand() < 0.5f) side = -side;
            Vector2 best = side;
            float bestLen = 0f;
            for (int k = 0; k < 12; k++)
            {
                float ang = ((k + 1) >> 1) * 30f * ((k & 1) == 0 ? 1f : -1f) * Mathf.Deg2Rad;
                Vector2 w = Rotate(side, ang);
                float len = 0f;
                for (float s = 0.2f; s <= want + splitRoom + 1e-3f; s += 0.2f)
                {
                    Vector2 p = p0 + w * s;
                    if (!OkSpot(a.spec, p) || !OkSpot(b.spec, p)) break;
                    len = s;
                }
                if (len > bestLen) { bestLen = len; best = w; }
                if (len >= want + splitRoom - 0.2f) break;
            }
            float L = Mathf.Min(want, bestLen - splitRoom);
            if (L < 1f) return false;
            // One long file: the initiating herd in front (its member nearest the start leads), then the other herd.
            float back = 0f, prevLen = 0f;
            int rank = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                var h = pass == 0 ? a : b;
                int n = SortMembers(h, p0, Vector2.zero, true);
                for (int i = 0; i < n; i++)
                {
                    var x = _meetPick[i];
                    float len = Len(h, x);
                    if (rank > 0) back += 1.25f * Mathf.Max(len, prevLen);
                    prevLen = len;
                    if (rank == 0)
                    {
                        Own(x, AnimalActivity.Trek, p0, YawOf(best));
                        m.leadA = x;
                    }
                    else
                    {
                        Vector2 face = p0 - x.pos;
                        Own(x, AnimalActivity.Trek, x.pos, face.sqrMagnitude > 1e-6f ? YawOf(face) : x.yaw);
                    }
                    x.actT = back;
                    x.sigT = pass;
                    rank++;
                }
                System.Array.Clear(_meetPick, 0, n);
            }
            m.dir = best;
            m.len = L;
            m.speed = v;
            m.head = 0f;
            m.total = L / v + back / v + 12f;
            return true;
        }

        bool BeginRing(Meeting m)
        {
            var a = m.a;
            var b = m.b;
            float r0 = Mathf.Max(0.3f, (a.center - b.center).magnitude * 0.5f);
            float rc = 0.55f * Mathf.Max(RadiusOf(a), RadiusOf(b)) + Mathf.Max(BodyOf(a), BodyOf(b));
            // The orbit round the middle of the two herds, else a tighter one, else one shifted a little off the
            // shore or the slope that spoils it (the groups walk to their start places first anyway).
            Vector2 side = new Vector2(-m.axis.y, m.axis.x);
            Vector2 c = m.mid;
            float r = r0;
            bool fits = false;
            for (int t = 0; t < 10 && !fits; t++)
            {
                r = t < 5 ? r0 : Mathf.Min(r0, Mathf.Max(0.3f, rc + 0.5f * Mathf.Max(BodyOf(a), BodyOf(b))));
                int o = t % 5;
                c = m.mid + (o == 1 ? side : o == 2 ? -side : o == 3 ? m.axis : o == 4 ? -m.axis : Vector2.zero) * 0.6f;
                fits = RingFits(a, b, c, r, rc);
            }
            if (!fits) return false;
            m.mid = c;
            Vector2 toA = a.center - c;
            if (toA.sqrMagnitude < 1e-6f) toA = -m.axis;
            m.angA = Mathf.Atan2(toA.y, toA.x);
            m.angB = m.angA + Mathf.PI;
            m.sign = MRand() < 0.5f ? 1f : -1f;
            m.radius = r;
            float v = Mathf.Clamp(Mathf.Min(a.spec.speed, b.spec.speed) * 1.6f, 0.08f, 0.9f);
            m.speed = v / r;
            m.turn = Mathf.Clamp(m.speed * MRand(6f, 8f), Mathf.PI, Mathf.PI * 2f);
            PlaceGroup(a, c + Ang(m.angA) * r, m.angA, m.sign, rc);
            PlaceGroup(b, c + Ang(m.angB) * r, m.angB, m.sign, rc);
            m.total = m.turn / m.speed + 12f;
            return true;
        }

        bool RingFits(Herd a, Herd b, Vector2 c, float r, float rc)
        {
            for (int k = 0; k < 12; k++)
            {
                Vector2 dir = Ang(k * (Mathf.PI / 6f));
                Vector2 outer = c + dir * (r + rc), inner = c + dir * Mathf.Max(0f, r - rc);
                if (!OkSpot(a.spec, outer) || !OkSpot(b.spec, outer) || !OkSpot(a.spec, inner) || !OkSpot(b.spec, inner)) return false;
            }
            return true;
        }

        // Each member keeps its place in the herd, pulled in to 55 %, as its offset from the dancing group's middle.
        void PlaceGroup(Herd h, Vector2 groupC, float ang, float sign, float maxOff)
        {
            float yaw = YawOf(Tangent(ang, sign));
            foreach (var x in h.members)
            {
                if (x.hidden) continue;
                Vector2 off = (x.pos - h.center) * 0.55f;
                if (off.magnitude > maxOff) off = off.normalized * maxOff;
                Vector2 g = groupC + off;
                if (!OkSpot(h.spec, g)) { g = groupC; off = Vector2.zero; }
                Own(x, AnimalActivity.RingDance, g, yaw);
                x.dir = off;
            }
        }

        // ---------------------------------------------------------- the five patterns: performing

        void StartPerform(Meeting m)
        {
            switch (m.pattern)
            {
                case MeetPattern.Greet:
                    // Every onlooker nods once, each at its own moment.
                    Delays(m.a, 1.5f, 5f);
                    Delays(m.b, 1.5f, 5f);
                    break;
                case MeetPattern.Shove:
                    Delays(m.a, 0.5f, 2.5f);
                    Delays(m.b, 0.5f, 2.5f);
                    break;
            }
        }

        void Delays(Herd h, float lo, float hi)
        {
            foreach (var x in h.members)
            {
                if (!IsMeeting(x.act)) continue;
                x.actT = MRand(lo, hi);
                x.sigT = 0f;
            }
        }

        bool Perform(Meeting m, float dt, ref bool done)
        {
            switch (m.pattern)
            {
                case MeetPattern.Greet: return GreetStep(m, dt, ref done);
                case MeetPattern.Tag: return TagStep(m, dt, ref done);
                case MeetPattern.Shove: return ShoveStep(m, dt, ref done);
                case MeetPattern.Trek: return TrekStep(m, dt, ref done);
                default: return RingStep(m, dt, ref done);
            }
        }

        // Moves toward p by at most maxStep; false when the ground says no.
        bool Glide(Herd h, Animal x, Vector2 p, float maxStep)
        {
            Vector2 d = p - x.pos;
            float n = d.magnitude;
            if (n < 1e-5f) return true;
            Vector2 next = n > maxStep ? x.pos + d / n * maxStep : p;
            if (!CanStepMember(h.spec, x, H(next), H(x.pos))) return false;
            x.pos = next;
            return true;
        }

        bool Lead(Animal x, Herd h) => x != null && x.act != AnimalActivity.None && IsMeeting(x.act) && h.members.Contains(x);

        // Nose to nose: sniffing (head low, face wobbling), three nods each (the other a beat later), a cheek rub, done.
        bool GreetStep(Meeting m, float dt, ref bool done)
        {
            float t = m.t;
            if (!Lead(m.leadA, m.a) || !Lead(m.leadB, m.b)) { done = true; return true; }
            GreetLead(m.leadA, t, 0f, m.total);
            GreetLead(m.leadB, t, 0.22f, m.total);
            Onlookers(m, m.a, t);
            Onlookers(m, m.b, t);
            done = t >= m.total;
            return true;
        }

        void GreetLead(Animal x, float t, float o, float total)
        {
            if (t < 2.4f)
            {
                x.yaw = x.baseYaw + 14f * Mathf.Sin(t * 7f + o * 5f);
                SetState(x, AnimalState.Look, 999f, 4f, false);
            }
            else if (t < 5.2f)
            {
                x.yaw = x.baseYaw;
                int k = Mathf.FloorToInt((t - 2.4f - o) / 0.45f);
                SetState(x, AnimalState.Look, 999f, k < 0 ? 4f : (k & 1) == 0 ? 18f : -12f, true);
            }
            else if (t < total - 1.2f)
            {
                x.yaw = x.baseYaw + 24f * Mathf.Sin((t - 5.2f) * 2.4f + o * 3f);
                SetState(x, AnimalState.Look, 999f, LookPitch, true);
            }
            else
            {
                x.yaw = Mathf.LerpAngle(x.yaw, x.baseYaw, 0.2f);
                SetState(x, AnimalState.Look, 999f, LookPitch, true);
            }
        }

        void Onlookers(Meeting m, Herd h, float t)
        {
            foreach (var x in h.members)
            {
                if (!IsMeeting(x.act) || x.actT <= 0f || x == m.leadA || x == m.leadB) continue;
                if (x.sigT == 0f && t >= x.actT) { SetState(x, AnimalState.Look, 999f, 16f, true); x.sigT = 1f; }
                else if (x.sigT == 1f && t >= x.actT + 0.55f) { SetState(x, AnimalState.Look, 999f, LookPitch, true); x.sigT = 2f; }
            }
        }

        // Round the ring, each runner a couple of bodies behind the one it chases, the gaps breathing in and out;
        // at half time they stop, turn round and the last one becomes the one chased.
        bool TagStep(Meeting m, float dt, ref bool done)
        {
            float t = m.t;
            int n = (int)m.len;
            if (!m.flipped && t >= m.total * 0.5f)
            {
                m.flipped = true;
                m.sign = -m.sign;
                m.hold = 0.55f;
                foreach (var x in m.a.members) if (x.act == AnimalActivity.Tag) x.actT = n - 1 - x.actT;
                foreach (var x in m.b.members) if (x.act == AnimalActivity.Tag) x.actT = n - 1 - x.actT;
            }
            Animal leader = null;
            Herd leaderHerd = null;
            FindRank(m.a, 0f, ref leader, ref leaderHerd);
            FindRank(m.b, 0f, ref leader, ref leaderHerd);
            if (leader == null) { done = true; return true; }
            if (m.hold > 0f)
            {
                m.hold -= dt;
                TurnRunners(m.a, m, dt);
                TurnRunners(m.b, m, dt);
                done = false;
                return true;
            }
            float w = m.speed;
            leader.sigT += m.sign * w * dt * (1f + 0.2f * Mathf.Sin(t * 1.3f));
            bool blocked = !RunTo(m.a, m, leader, dt, w, t) | !RunTo(m.b, m, leader, dt, w, t);
            Watch(m.a, leader.pos, dt);
            Watch(m.b, leader.pos, dt);
            done = blocked || t >= m.total + 0.55f;
            return true;
        }

        void FindRank(Herd h, float rank, ref Animal found, ref Herd herd)
        {
            foreach (var x in h.members)
                if (x.act == AnimalActivity.Tag && x.actT == rank) { found = x; herd = h; }
        }

        void TurnRunners(Herd h, Meeting m, float dt)
        {
            foreach (var x in h.members)
            {
                if (x.act != AnimalActivity.Tag) continue;
                SetMoving(x, false);
                x.yaw = Mathf.LerpAngle(x.yaw, YawOf(Tangent(x.sigT, m.sign)), 1f - Mathf.Exp(-10f * dt));
            }
        }

        bool RunTo(Herd h, Meeting m, Animal leader, float dt, float w, float t)
        {
            bool ok = true;
            foreach (var x in h.members)
            {
                if (x.act != AnimalActivity.Tag) continue;
                if (x != leader)
                {
                    float target = leader.sigT - m.sign * x.actT * m.turn * (1f + 0.45f * Mathf.Sin(t * 2.3f + x.actT * 1.7f));
                    float diff = Mathf.DeltaAngle(x.sigT * Mathf.Rad2Deg, target * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    float maxD = 1.7f * w * dt;
                    x.sigT += Mathf.Clamp(diff, -maxD, maxD);
                }
                Vector2 p = m.mid + Ang(x.sigT) * m.radius;
                if (!Glide(h, x, p, m.radius * w * 2.5f * dt + 0.02f)) ok = false;
                x.yaw = YawOf(Tangent(x.sigT, m.sign));
                SetMoving(x, true);
                SetState(x, AnimalState.Walk, 0f, WalkPitch, false);
            }
            return ok;
        }

        // Onlookers follow the one being chased with their eyes.
        void Watch(Herd h, Vector2 at, float dt)
        {
            float k = 1f - Mathf.Exp(-3f * dt);
            foreach (var x in h.members)
            {
                if (x.act != AnimalActivity.Cheer) continue;
                Vector2 to = at - x.pos;
                if (to.sqrMagnitude > 1e-6f) x.yaw = Mathf.LerpAngle(x.yaw, YawOf(to), k);
            }
        }

        // Head to head: heads down, small shoves to and fro with the legs working; a break (step back, heads up, a
        // shake), a second round that drifts to one side, then both step back and nod.
        bool ShoveStep(Meeting m, float dt, ref bool done)
        {
            var la = m.leadA;
            var lb = m.leadB;
            if (!Lead(la, m.a) || !Lead(lb, m.b)) { done = true; return true; }
            float t = m.t, L = m.len;
            float r1 = m.total * 0.42f, brk = r1 + 1.1f, r2 = m.total - 1.6f;
            Vector2 u = m.axis;
            float x = 0f, back = 0f, pitch = 18f;
            bool push = false;
            float shake = 0f;
            if (t < 0.6f) { }
            else if (t < r1)
            {
                float s = t - 0.6f;
                x = L * (0.26f * Mathf.Sin(2.1f * s) + 0.1f * Mathf.Sin(5.3f * s));
                push = true;
            }
            else if (t < brk)
            {
                float s = t - r1;
                back = 0.35f * L * Mathf.SmoothStep(0f, 1f, s / 0.4f);
                pitch = LookPitch;
                if (s < 0.7f) shake = 12f * Mathf.Sin(s * 14f);
            }
            else if (t < r2)
            {
                float s = t - brk;
                float k = Mathf.SmoothStep(0f, 1f, s / Mathf.Max(0.5f, r2 - brk));
                x = L * (0.22f * Mathf.Sin(2.6f * s + 1f) + 0.12f * Mathf.Sin(6.1f * s)) + L * 0.35f * k * m.sign;
                push = true;
            }
            else
            {
                float s = t - r2;
                back = 0.5f * L * Mathf.SmoothStep(0f, 1f, s / 0.5f);
                pitch = s > 0.6f && s < 1.1f ? 16f : LookPitch;
            }
            Vector2 pa = push ? la.goal + u * x : la.goal - u * back;
            Vector2 pb = push ? lb.goal + u * x : lb.goal + u * back;
            Glide(m.a, la, pa, L);
            Glide(m.b, lb, pb, L);
            la.yaw = la.baseYaw + 4f * Mathf.Sin(t * 7f) + shake;
            lb.yaw = lb.baseYaw - 4f * Mathf.Sin(t * 7f + 1f) - shake;
            SetMoving(la, push);
            SetMoving(lb, push);
            SetState(la, push ? AnimalState.Walk : AnimalState.Look, push ? 0f : 999f, pitch, !push);
            SetState(lb, push ? AnimalState.Walk : AnimalState.Look, push ? 0f : 999f, pitch, !push);
            Cheer(m.a, dt);
            Cheer(m.b, dt);
            done = t >= m.total;
            return true;
        }

        // Onlookers trample on the spot now and then and turn a little, as if urging them on.
        void Cheer(Herd h, float dt)
        {
            foreach (var x in h.members)
            {
                if (x.act != AnimalActivity.Cheer) continue;
                x.actT -= dt;
                if (x.actT > 0f) continue;
                if (!x.moving)
                {
                    SetMoving(x, true);
                    TurnTo(x, x.baseYaw + MRand(-14f, 14f));
                    x.actT = 0.4f;
                }
                else
                {
                    SetMoving(x, false);
                    x.actT = MRand(1.2f, 3f);
                }
            }
        }

        // One long file along the trail: each member joins once the head has passed its place in the line; at the
        // end of the trail the file forks, the front herd bearing left, the rear herd right, and each walks on a bit.
        bool TrekStep(Meeting m, float dt, ref bool done)
        {
            if (!Lead(m.leadA, m.a)) { done = true; return true; }
            float v = m.speed;
            if (m.step == 0)
            {
                m.head += v * dt;
                if (m.head >= m.len)
                {
                    m.head = m.len;
                    m.step = 1;
                    m.hold = 0f;
                    foreach (var x in m.a.members) if (x.act == AnimalActivity.Trek) x.from = x.pos;
                    foreach (var x in m.b.members) if (x.act == AnimalActivity.Trek) x.from = x.pos;
                }
            }
            else m.hold += dt;
            TrekHerd(m.a, m, dt);
            TrekHerd(m.b, m, dt);
            Recentre(m.a);
            Recentre(m.b);
            done = (m.step == 1 && m.hold >= 2.8f) || m.t >= m.total;
            return true;
        }

        void TrekHerd(Herd h, Meeting m, float dt)
        {
            float v = m.speed;
            float turn = 1f - Mathf.Exp(-8f * dt);
            foreach (var x in h.members)
            {
                if (x.act != AnimalActivity.Trek) continue;
                Vector2 target, face;
                if (m.step == 0)
                {
                    float s = m.head - x.actT;
                    if (s < 0f)
                    {
                        SetMoving(x, false);
                        continue;
                    }
                    target = m.mid + m.dir * s;
                    face = m.dir;
                }
                else
                {
                    Vector2 fork = Rotate(m.dir, (x.sigT == 0f ? 40f : -40f) * Mathf.Deg2Rad);
                    target = x.from + fork * (v * 0.9f * m.hold);
                    face = fork;
                }
                Vector2 to = target - x.pos;
                float d = to.magnitude;
                bool moved = d > 0.02f * BodyOf(h) && Glide(h, x, target, v * 2.2f * dt);
                if (d > BodyOf(h) * 0.5f) face = to;
                x.yaw = Mathf.LerpAngle(x.yaw, YawOf(face), turn);
                SetMoving(x, moved);
                SetState(x, moved ? AnimalState.Walk : AnimalState.Look, moved ? 0f : 999f, moved ? WalkPitch : LookPitch, !moved);
            }
        }

        void Recentre(Herd h)
        {
            Vector2 sum = Vector2.zero;
            int n = 0;
            foreach (var x in h.members)
            {
                if (x.hidden) continue;
                sum += x.pos;
                n++;
            }
            if (n == 0) return;
            Vector2 c = sum / n;
            if (Valid(h.spec, H(c))) h.center = h.target = c;
        }

        // The two groups circle their common middle (each keeping its shape and turning with the orbit), then every
        // member takes its place in one row, the two herds side by side, all facing the same way, and they bow twice.
        bool RingStep(Meeting m, float dt, ref bool done)
        {
            switch (m.step)
            {
                case 0:
                {
                    m.hold = Mathf.Min(1f, m.hold + dt * m.speed / m.turn);
                    float phi = m.sign * m.turn * Mathf.SmoothStep(0f, 1f, m.hold);
                    Orbit(m.a, m, m.angA, phi, dt);
                    Orbit(m.b, m, m.angB, phi, dt);
                    Recentre(m.a);
                    Recentre(m.b);
                    if (m.hold >= 1f)
                    {
                        m.step = 1;
                        m.t2 = 0f;
                        LineUp(m, phi);
                    }
                    done = m.t >= m.total;
                    return true;
                }
                case 1:
                {
                    m.t2 += dt;
                    bool changed = false;
                    bool all = GatherStep(m.a, dt, ref changed) & GatherStep(m.b, dt, ref changed);
                    if (all || m.t2 > 5f)
                    {
                        SettleGather(m.a);
                        SettleGather(m.b);
                        m.step = 2;
                        m.t2 = 0f;
                        changed = true;
                    }
                    done = m.t >= m.total;
                    return changed;
                }
                default:
                {
                    m.t2 += dt;
                    float s = m.t2;
                    float pitch = s < 0.7f ? 20f : s < 1.3f ? LookPitch : s < 2f ? 20f : LookPitch;
                    Bow(m.a, pitch);
                    Bow(m.b, pitch);
                    done = s >= 2.6f || m.t >= m.total;
                    return true;
                }
            }
        }

        void Orbit(Herd h, Meeting m, float ang0, float phi, float dt)
        {
            float ang = ang0 + phi;
            Vector2 groupC = m.mid + Ang(ang) * m.radius;
            float yaw = YawOf(Tangent(ang, m.sign));
            float k = 1f - Mathf.Exp(-8f * dt);
            float maxStep = h.spec.speed * 4f * dt + 0.01f;
            foreach (var x in h.members)
            {
                if (x.act != AnimalActivity.RingDance) continue;
                bool moved = Glide(h, x, groupC + Rotate(x.dir, phi), maxStep);
                x.yaw = Mathf.LerpAngle(x.yaw, yaw, k);
                SetMoving(x, moved);
                SetState(x, AnimalState.Walk, 0f, WalkPitch, false);
            }
        }

        void LineUp(Meeting m, float phi)
        {
            Vector2 q = Ang(m.angA + phi);
            Vector2 face = Tangent(m.angA + phi, m.sign);
            float yaw = YawOf(face);
            Row(m.a, m.mid, q, yaw);
            Row(m.b, m.mid, -q, yaw);
        }

        // The herd's members in a row from the middle outwards along `along`, ordered by where they stand.
        void Row(Herd h, Vector2 mid, Vector2 along, float yaw)
        {
            int n = SortMembers(h, mid, along, false);
            float sp = 1.3f * BodyOf(h);
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                var x = _meetPick[i];
                if (x.act != AnimalActivity.RingDance) continue;
                Vector2 g = mid + along * (0.5f * sp + (k++ + 0.5f) * sp);
                if (!OkSpot(h.spec, g)) g = x.pos;
                x.goal = g;
                x.baseYaw = yaw;
                x.arrived = false;
                x.blocked = 0f;
            }
            System.Array.Clear(_meetPick, 0, n);
        }

        void Bow(Herd h, float pitch)
        {
            foreach (var x in h.members)
                if (x.act == AnimalActivity.RingDance) SetState(x, AnimalState.Look, 999f, pitch, true);
        }
    }
}
