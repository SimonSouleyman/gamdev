using Drift.Core;
using UnityEngine;

namespace Drift.Life
{
    // How two species get along when their herds share an island (IslandHerdSystem.Relations.cs).
    public enum HerdRelation { Ignore, Avoid, Mingle, Follow, Guard }

    // Relations between species (v0.6.8, "Beziehungen zwischen Arten"). Nothing hunts and nobody flees from another
    // species; a relation only changes where a calm herd goes and what it watches:
    //   Mingle  two herds within mingleRange walk to a shared spot and graze interleaved (Errand.Mingle, both herds)
    //   Follow  the follower now and then trails the lead's herd at followGap (goal only, no errand)
    //   Guard   a sentry stands at the edge of the herd and watches the other species while it is in range (Errand.Guard)
    //   Avoid   the herd picks its wander targets avoidDistance away from the other one, at its normal pace (goal only);
    //           penguins have the coast in the morning, reindeer in the evening: the other one makes room
    //   Gather  the dusk truce: once per dusk the herds near one shore spot come down to drink side by side (Errand.Gather)
    // Every start comes from Think (RelationsThink, after the needs layer) for a herd without errand and without a goal of
    // another layer; partner checks run once a second (StepRelations), the pair scan only in Think. Draws come from a
    // stream of their own (_relRnd), so the island's other behaviours keep their sequences.
    public partial class IslandHerdSystem
    {
        [Header("Beziehungen zwischen Arten")]
        [Tooltip("Chance pro Denk-Takt (1 s), dass sich zwei befreundete Herden (Zebra+Giraffe, Hase+Erdmännchen, Capybara+Schildkröte) in mingleRange zusammentun und gemischt grasen.")]
        [Range(0f, 1f)] public float mingleRate = 0.03f;
        [Range(2f, 20f)] public float mingleRange = 10f;
        [Tooltip("So lange (s, von–bis) grasen die beiden Herden gemischt.")]
        public Vector2 mingleTime = new Vector2(40f, 90f);
        [Tooltip("Pause (s, von–bis) einer Herde nach dem gemischten Grasen.")]
        public Vector2 mingleCooldown = new Vector2(60f, 120f);
        [Tooltip("Längster Weg (s) zum gemeinsamen Platz – langsame Arten (Schildkröte) mischen sich nur aus der Nähe.")]
        [Range(5f, 60f)] public float mingleApproachTime = 30f;
        [Tooltip("Chance pro Denk-Takt, dass die Schafe einer Ziegenherde in followRange hinterherziehen (die Ziegen führen).")]
        [Range(0f, 1f)] public float followChance = 0.08f;
        [Range(2f, 25f)] public float followRange = 12f;
        [Tooltip("Abstand (Einheiten, Mitte zu Mitte), den die folgende Herde hinter der führenden hält.")]
        [Range(1f, 8f)] public float followGap = 3f;
        public Vector2 followTime = new Vector2(25f, 50f);
        public Vector2 followCooldown = new Vector2(40f, 90f);
        [Tooltip("Chance pro Denk-Takt, dass eine Herde eine Wache aufstellt, wenn eine beobachtete Art (Hase: Polarfuchs; Erdmännchen: jede andere Art) in guardRange ist.")]
        [Range(0f, 1f)] public float guardChance = 0.3f;
        [Range(2f, 20f)] public float guardRange = 10f;
        public Vector2 guardTime = new Vector2(20f, 40f);
        public Vector2 guardCooldown = new Vector2(45f, 90f);
        [Tooltip("Abstand (Einheiten, Mitte zu Mitte), den Rentiere und Pinguine voneinander halten – ruhig, ohne Flucht.")]
        [Range(2f, 15f)] public float avoidDistance = 6f;
        [Tooltip("Waffenstillstand an der Wasserstelle: Chance pro Denk-Takt in der Abenddämmerung, dass eine Herde das abendliche Treffen am Ufer beginnt (höchstens eins pro Insel und Abend).")]
        [Range(0f, 1f)] public float gatherChance = 0.05f;
        [Tooltip("Nachtwert (von–bis), in dem die Dämmerung zum Treffen am Wasser einlädt.")]
        public Vector2 gatherNight = new Vector2(0.3f, 0.6f);
        [Range(2f, 30f)] public float gatherJoinRange = 15f;
        public Vector2 gatherTime = new Vector2(30f, 60f);
        [Range(1f, 2f)] public float gatherPace = 1.3f;
        [Tooltip("Längster Weg (s) zum Treffen; wer weiter weg ist, bleibt, wo er ist.")]
        [Range(5f, 90f)] public float gatherApproachTime = 40f;
        [Tooltip("Höchstens dieser Anteil der Zeit einer Herde gehört dem Mischen, Folgen und Wachehalten: nach jeder solchen Beziehung ruht die Herde mindestens so lange, dass der Anteil nicht überschritten wird (Begegnungen und eigene Besorgungen sollen weiter vorkommen).")]
        [Range(0.1f, 0.8f)] public float relationShare = 0.35f;

        // GoalStep has no case for Guard / Gather yet (its default drops the goal): the sentry shows the Sentry look and
        // the gathering drinks.
        const AnimalActivity MingleAct = AnimalActivity.Visit, GuardAct = AnimalActivity.Sentry, GatherAct = AnimalActivity.Drink;

        enum RelMode { None, Mingle, Follow, Guard, Avoid, Gather }

        partial class Herd
        {
            // Relations layer: the other herd of the running relation, its start and end, when the next Mingle / Follow /
            // Guard may begin (stepClock times), the once-a-second partner check and the mingle interleave parity.
            public Herd rel;
            public RelMode relMode;
            public float relStart, relEnd, relAt, relCheckT;
            public int relSlot;
        }

        // Explicit pairs. Follow: the second follows the first. Guard: the second posts the sentry. Then: Ox ignores
        // everyone, a meerkat guards against every other species, all else ignore each other.
        static readonly (LifeKind a, LifeKind b, HerdRelation r)[] RelationPairs =
        {
            (LifeKind.Zebra, LifeKind.Giraffe, HerdRelation.Mingle),
            (LifeKind.Goat, LifeKind.Sheep, HerdRelation.Follow),
            (LifeKind.Hare, LifeKind.Meerkat, HerdRelation.Mingle),
            (LifeKind.Reindeer, LifeKind.Penguin, HerdRelation.Avoid),
            (LifeKind.ArcticFox, LifeKind.Hare, HerdRelation.Guard),
            (LifeKind.Capybara, LifeKind.Tortoise, HerdRelation.Mingle),
        };

        static HerdRelation[,] _relations;

        public static HerdRelation RelationOf(LifeKind a, LifeKind b)
        {
            var m = _relations ??= BuildRelations();
            int i = (int)a, j = (int)b, n = m.GetLength(0);
            return i >= 0 && j >= 0 && i < n && j < n ? m[i, j] : HerdRelation.Ignore;
        }

        static HerdRelation[,] BuildRelations()
        {
            int n = System.Enum.GetValues(typeof(LifeKind)).Length;
            var m = new HerdRelation[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    m[i, j] = RelationRule((LifeKind)i, (LifeKind)j);
            return m;
        }

        static HerdRelation RelationRule(LifeKind a, LifeKind b)
        {
            if (a == b || !IsHerdSpecies(a) || !IsHerdSpecies(b)) return HerdRelation.Ignore;
            foreach (var p in RelationPairs)
                if ((p.a == a && p.b == b) || (p.a == b && p.b == a)) return p.r;
            if (a == LifeKind.Ox || b == LifeKind.Ox) return HerdRelation.Ignore;
            if (a == LifeKind.Meerkat || b == LifeKind.Meerkat) return HerdRelation.Guard;
            return HerdRelation.Ignore;
        }

        public static bool FollowsLead(LifeKind self, LifeKind lead)
        {
            foreach (var p in RelationPairs)
                if (p.r == HerdRelation.Follow && p.a == lead && p.b == self) return true;
            return false;
        }

        public static bool GuardsAgainst(LifeKind self, LifeKind other)
        {
            if (RelationOf(self, other) != HerdRelation.Guard) return false;
            foreach (var p in RelationPairs)
                if (p.r == HerdRelation.Guard && (p.a == self || p.b == self) && (p.a == other || p.b == other)) return p.b == self;
            return self == LifeKind.Meerkat;
        }

        // 1 = the species has the shared coast in the morning, 2 = in the evening, 0 = no preference.
        static int CoastHours(LifeKind k) => k == LifeKind.Penguin ? 1 : k == LifeKind.Reindeer ? 2 : 0;

        // Whether `self` makes room for `other` now; without a clock both do.
        public static bool YieldsTo(LifeKind self, LifeKind other, float timeOfDay)
        {
            if (timeOfDay < 0f) return true;
            return CoastHours(self) != (timeOfDay < 0.5f ? 1 : 2);
        }

        System.Random _relRnd;
        readonly int[] _relStarts = new int[6];
        bool _relSawDay, _gatherOn, _gatherDone;
        Vector2 _gatherDry, _gatherDir;
        float _gatherEnd;
        int _gatherSlots;

        public int GatheringsStarted { get; private set; }
        public bool GatheringActive => _gatherOn;
        public Vector2 GatherSpot => _gatherDry;

        // How often a relation began on this island (Mingle counts once per pair, Gather once per joining herd).
        public int RelationStarts(HerdGoal goal)
        {
            switch (goal)
            {
                case HerdGoal.Mingle: return _relStarts[(int)RelMode.Mingle];
                case HerdGoal.Follow: return _relStarts[(int)RelMode.Follow];
                case HerdGoal.Guard: return _relStarts[(int)RelMode.Guard];
                case HerdGoal.Avoid: return _relStarts[(int)RelMode.Avoid];
                case HerdGoal.Gather: return _relStarts[(int)RelMode.Gather];
                default: return 0;
            }
        }

        public static bool IsRelationGoal(HerdGoal g) =>
            g == HerdGoal.Mingle || g == HerdGoal.Guard || g == HerdGoal.Gather || g == HerdGoal.Avoid || g == HerdGoal.Follow;

        float RRand()
        {
            _relRnd ??= new System.Random(HerdSeed ^ 0x2545F49);
            return (float)_relRnd.NextDouble();
        }

        float RRand(float a, float b) => a + RRand() * (b - a);
        float RRand(Vector2 range) => RRand(range.x, Mathf.Max(range.x, range.y));

        bool Alive(Herd h) => h != null && h.members.Count > 0 && _herds.Contains(h);

        // Free for a relation: no errand, no other layer's goal, calm, awake; social = also settled and off cooldown.
        bool RelFree(Herd h, bool social)
        {
            if (h.members.Count == 0 || h.errand != Errand.None || h.meeting != null) return false;
            if (h.goal != HerdGoal.None && h.goal != HerdGoal.Follow && h.goal != HerdGoal.Avoid) return false;
            if (h.fleeing || h.fleeTimer > 0f || h.refuging || h.diving || h.playT > 0f || h.stampT > 0f || h.tuckT > 0f) return false;
            if (social && (h.settleT > 0f || _stepClock < h.relAt)) return false;
            return !AnySleeping(h);
        }

        bool Dusk
        {
            get
            {
                if (!_relSawDay || _night < gatherNight.x || _night >= Mathf.Min(gatherNight.y, sleepThreshold)) return false;
                float t = LifeEnvironment.TimeOfDay;
                return t < 0f || t >= 0.5f;
            }
        }

        partial void TickRelations(float dt)
        {
            float night = LifeEnvironment.NightAmount;
            if (night < 0.15f)
            {
                _relSawDay = true;
                _gatherDone = false;
            }
            else if (night > sleepThreshold) _relSawDay = false;
            if (_gatherOn && _stepClock >= _gatherEnd) _gatherOn = false;
        }

        // ---------------------------------------------------------- think

        partial void RelationsThink(Herd herd, bool standing, ref bool started)
        {
            if (behaviourRate <= 0f || !RelFree(herd, false)) return;
            float rate = behaviourRate;
            if (Dusk && !Rising)
            {
                if (_gatherOn)
                {
                    if (TryJoinGather(herd)) { started = true; return; }
                }
                else if (!_gatherDone && RRand() < gatherChance * rate && StartGather(herd)) { started = true; return; }
            }

            var kind = herd.spec.kind;
            float tod = LifeEnvironment.TimeOfDay;
            bool walking = herd.wait <= 0f;
            Herd avoid = null, guard = null, mingle = null, lead = null;
            float dAvoid = float.MaxValue, dGuard = guardRange, dMingle = mingleRange, dLead = followRange;
            foreach (var o in _herds)
            {
                if (o == herd || o.members.Count == 0) continue;
                var r = RelationOf(kind, o.spec.kind);
                if (r == HerdRelation.Ignore) continue;
                float d = (o.center - herd.center).magnitude;
                switch (r)
                {
                    case HerdRelation.Avoid:
                        if (!YieldsTo(kind, o.spec.kind, tod)) break;
                        float near = walking ? SegmentDistance(herd.center, herd.target, o.center) : d;
                        if (Mathf.Min(near, d) < avoidDistance && d < dAvoid) { avoid = o; dAvoid = d; }
                        break;
                    case HerdRelation.Guard:
                        if (d < dGuard && GuardsAgainst(kind, o.spec.kind)) { guard = o; dGuard = d; }
                        break;
                    case HerdRelation.Mingle:
                        if (d < dMingle && RelFree(o, true)) { mingle = o; dMingle = d; }
                        break;
                    case HerdRelation.Follow:
                        if (d < dLead && FollowsLead(kind, o.spec.kind)) { lead = o; dLead = d; }
                        break;
                }
            }

            if (avoid != null && MakeRoom(herd, avoid)) { started = true; return; }
            if (guard != null && herd.relMode != RelMode.Avoid && _stepClock >= herd.relAt && herd.members.Count >= 2
                && RRand() < guardChance * rate && StartGuard(herd, guard)) { started = true; return; }
            if (mingle != null && herd.relMode != RelMode.Avoid && herd.settleT <= 0f && _stepClock >= herd.relAt
                && RRand() < mingleRate * rate && StartMingle(herd, mingle)) { started = true; return; }
            if (lead != null) { if (FollowThink(herd, lead)) started = true; }
            else if (herd.relMode == RelMode.Follow) EndRelation(herd);
        }

        static float SegmentDistance(Vector2 a, Vector2 b, Vector2 p)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            return (a + ab * t - p).magnitude;
        }

        void BeginRelation(Herd herd, RelMode mode, Herd other, HerdGoal goal)
        {
            if (herd.relMode != RelMode.None && herd.relMode != mode) EndRelation(herd);
            herd.relMode = mode;
            herd.rel = other;
            herd.goal = goal;
            herd.relCheckT = 1f;
            herd.relStart = _stepClock;
            _relStarts[(int)mode]++;
        }

        // Ends the herd's relation; an errand of the layer still running is cancelled and the herd soon walks on.
        void EndRelation(Herd herd)
        {
            var mode = herd.relMode;
            float rest = (_stepClock - herd.relStart) * (1f / Mathf.Max(0.05f, relationShare) - 1f);
            herd.relMode = RelMode.None;
            herd.rel = null;
            switch (mode)
            {
                case RelMode.Mingle:
                    herd.relAt = _stepClock + Mathf.Max(RRand(mingleCooldown), rest);
                    EndRelationErrand(herd, Errand.Mingle);
                    break;
                case RelMode.Guard:
                    herd.relAt = _stepClock + Mathf.Max(RRand(guardCooldown), rest);
                    if (herd.lookout != null && herd.lookout.act == GuardAct) ReleaseLookout(herd);
                    EndRelationErrand(herd, Errand.Guard);
                    break;
                case RelMode.Gather:
                    EndRelationErrand(herd, Errand.Gather);
                    break;
                case RelMode.Follow:
                    herd.relAt = _stepClock + Mathf.Max(RRand(followCooldown), rest);
                    if (herd.goal == HerdGoal.Follow) herd.goal = HerdGoal.None;
                    break;
                case RelMode.Avoid:
                    if (herd.goal == HerdGoal.Avoid) herd.goal = HerdGoal.None;
                    break;
            }
        }

        void EndRelationErrand(Herd herd, Errand errand)
        {
            if (herd.errand != errand) return;
            bool there = herd.errandStage == 1;
            CancelErrand(herd);
            if (there) herd.wait = Mathf.Min(herd.wait, RRand(2f, 6f));
        }

        // ---------------------------------------------------------- step

        partial void StepRelations(Herd herd, float dt, ref float mul)
        {
            switch (herd.relMode)
            {
                case RelMode.None:
                    // A goal of this layer left behind by a reset (far tier, re-seeding) without its relation.
                    if (IsRelationGoal(herd.goal) && !IsEnvironmentErrand(herd.errand)) herd.goal = HerdGoal.None;
                    return;
                case RelMode.Mingle: if (herd.errand != Errand.Mingle) { EndRelation(herd); return; } break;
                case RelMode.Guard: if (herd.errand != Errand.Guard) { EndRelation(herd); return; } break;
                case RelMode.Gather: if (herd.errand != Errand.Gather) { EndRelation(herd); return; } break;
                case RelMode.Follow: if (herd.errand != Errand.None || herd.goal != HerdGoal.Follow) { EndRelation(herd); return; } break;
                case RelMode.Avoid: if (herd.errand != Errand.None || herd.goal != HerdGoal.Avoid) { EndRelation(herd); return; } break;
            }
            if (herd.relMode == RelMode.Gather && herd.errandStage == 0) mul = Mathf.Max(mul, gatherPace);

            herd.relCheckT -= dt;
            if (herd.relCheckT > 0f) return;
            herd.relCheckT = 1f;
            var other = herd.rel;
            switch (herd.relMode)
            {
                case RelMode.Mingle:
                    if (!Alive(other) || other.relMode != RelMode.Mingle || other.rel != herd || (herd.errandStage == 0 && _stepClock > herd.relEnd - 15f))
                        EndRelation(herd);
                    break;
                case RelMode.Guard:
                    if (!Alive(other) || (other.center - herd.center).sqrMagnitude > Sq(guardRange * 1.25f)) { EndRelation(herd); break; }
                    herd.errandPos = other.center;
                    if (herd.lookout == null)
                    {
                        var a = PickSentry(herd);
                        if (a != null) PostSentry(herd, a);
                    }
                    AimSentry(herd);
                    break;
                case RelMode.Follow:
                    if (_stepClock >= herd.relEnd || !Alive(other) || (other.center - herd.center).sqrMagnitude > Sq(followRange * 1.5f) || LeadBusy(other))
                        EndRelation(herd);
                    break;
                case RelMode.Avoid:
                    if (herd.wait > 0f || _stepClock >= herd.relEnd) EndRelation(herd);
                    break;
                case RelMode.Gather:
                    if (herd.errandStage == 0 && _stepClock > _gatherEnd - 8f) EndRelation(herd);
                    break;
            }
        }

        static float Sq(float x) => x * x;

        // ---------------------------------------------------------- arrive

        partial void ArriveRelations(Herd herd)
        {
            switch (herd.errand)
            {
                case Errand.Mingle:
                    ArriveMingle(herd);
                    break;
                case Errand.Gather:
                    herd.wait = Mathf.Max(_gatherEnd - _stepClock, 10f);
                    herd.errandDir = _gatherDir;
                    AssignShoreGoals(herd, DrinkLow, DrinkHigh, GatherAct, true);
                    break;
                case Errand.Guard:
                    herd.wait = Mathf.Max(herd.relEnd - _stepClock, 2f);
                    break;
            }
        }

        // ---------------------------------------------------------- mingle

        public bool TryStartMingle(int herdIndex, int partnerIndex)
        {
            if (_surface == null || _rnd == null || herdIndex < 0 || partnerIndex < 0 || herdIndex >= _herds.Count || partnerIndex >= _herds.Count || herdIndex == partnerIndex) return false;
            var a = _herds[herdIndex];
            var b = _herds[partnerIndex];
            if (RelationOf(a.spec.kind, b.spec.kind) != HerdRelation.Mingle || !RelFree(a, false) || !RelFree(b, false)) return false;
            return StartMingle(a, b);
        }

        // Each herd walks its share of the gap (by its pace) to a spot beside the other's, so both arrive together.
        bool StartMingle(Herd a, Herd b)
        {
            Vector2 d = b.center - a.center;
            float dist = d.magnitude;
            Vector2 u = dist > 1e-3f ? d / dist : Ang(RRand(0f, 6.28f));
            float va = a.spec.speed, vb = b.spec.speed;
            Vector2 mid = a.center + u * (dist * va / (va + vb));
            float off = 0.25f * Mathf.Min(RadiusOf(a), RadiusOf(b));
            Vector2 ta = mid - u * off, tb = mid + u * off;
            float time = Mathf.Max((ta - a.center).magnitude / va, (tb - b.center).magnitude / vb);
            bool ok = time <= mingleApproachTime && OkSpot(a.spec, ta) && OkSpot(b.spec, tb) && OkSpot(a.spec, mid) && OkSpot(b.spec, mid)
                && WalkableLine(a.spec, a.center, ta) && WalkableLine(b.spec, b.center, tb);
            if (!ok)
            {
                a.relAt = _stepClock + RRand(10f, 20f);
                return false;
            }
            float end = _stepClock + time + RRand(mingleTime);
            BeginMingle(a, b, ta, mid, end, 0);
            BeginMingle(b, a, tb, mid, end, 1);
            _relStarts[(int)RelMode.Mingle]--;
            Moment(MomentKind.Mingle, a);
            return true;
        }

        void BeginMingle(Herd h, Herd other, Vector2 target, Vector2 mid, float end, int slot)
        {
            ReleaseLookout(h);
            StopDig(h);
            BeginRelation(h, RelMode.Mingle, other, HerdGoal.Mingle);
            h.errand = Errand.Mingle;
            h.errandStage = 0;
            h.errandPos = mid;
            h.target = target;
            h.wait = 0f;
            h.relEnd = end;
            h.relSlot = slot;
            ErrandsStarted++;
        }

        // Both herds fill one sunflower disc about the shared spot: the first pairs of slots alternate between the two
        // herds, the larger herd takes the rest, so the two species stand interleaved.
        void ArriveMingle(Herd herd)
        {
            var other = herd.rel;
            if (!Alive(other) || other.relMode != RelMode.Mingle || other.rel != herd)
            {
                herd.wait = RRand(2f, 4f);
                return;
            }
            herd.wait = Mathf.Max(herd.relEnd - _stepClock, 8f);
            Vector2 mid = herd.errandPos;
            int mine = Visible(herd), theirs = Visible(other);
            int pairs = Mathf.Min(mine, theirs), total = Mathf.Max(1, mine + theirs);
            float body = Mathf.Max(BodyOf(herd), BodyOf(other));
            float radius = formationSpacing * body * Mathf.Sqrt(total) * 0.75f;
            float phase = Mathf.Repeat(mid.x * 1.7f + mid.y * 2.3f, Mathf.PI * 2f);
            int j = 0;
            foreach (var a in herd.members)
            {
                if (a.hidden) continue;
                int k = j < pairs ? 2 * j + herd.relSlot : 2 * pairs + (j - pairs);
                j++;
                Vector2 dir = Ang(phase + k * 2.39996f);
                float r = radius * Mathf.Sqrt((k + 0.5f) / total);
                for (int tries = 0; tries < 3; tries++)
                {
                    Vector2 g = mid + dir * (r * (1f - 0.35f * tries));
                    if (!OkSpot(herd.spec, g)) continue;
                    SetGoal(a, g, MingleAct);
                    break;
                }
            }
        }

        static int Visible(Herd h)
        {
            int n = 0;
            foreach (var a in h.members) if (!a.hidden) n++;
            return n;
        }

        // ---------------------------------------------------------- follow

        // Errands that need their space: nobody walks up behind a meeting, a signature move or a choreography.
        static bool LeadBusy(Herd lead) =>
            lead.meeting != null || lead.errand == Errand.Meet || lead.errand == Errand.Signature || lead.errand == Errand.Slide
            || lead.errand == Errand.Parade || lead.stampT > 0f || lead.playT > 0f || lead.fleeing || lead.fleeTimer > 0f;

        bool FollowThink(Herd herd, Herd lead)
        {
            bool on = herd.relMode == RelMode.Follow && herd.rel == lead;
            if (!on && (herd.relMode != RelMode.None || herd.goal != HerdGoal.None || herd.settleT > 0f || _stepClock < herd.relAt
                        || RRand() >= followChance * behaviourRate)) return false;
            if (LeadBusy(lead))
            {
                if (on) EndRelation(herd);
                return false;
            }
            Vector2 to = lead.center - herd.center;
            Vector2 heading = lead.wait <= 0f && (lead.target - lead.center).sqrMagnitude > 0.09f ? (lead.target - lead.center).normalized
                : to.sqrMagnitude > 1e-4f ? to.normalized : Vector2.zero;
            float gap = Mathf.Max(followGap, (RadiusOf(lead) + RadiusOf(herd)) * 0.8f);
            Vector2 spot = lead.center - heading * gap;
            if (!OkSpot(herd.spec, spot))
            {
                spot = lead.center - (to.sqrMagnitude > 1e-4f ? to.normalized : heading) * gap;
                if (!OkSpot(herd.spec, spot))
                {
                    if (on) EndRelation(herd);
                    else herd.relAt = _stepClock + RRand(10f, 20f);
                    return false;
                }
            }
            if (!on)
            {
                BeginRelation(herd, RelMode.Follow, lead, HerdGoal.Follow);
                herd.relEnd = _stepClock + RRand(followTime);
            }
            bool moved = (spot - herd.target).sqrMagnitude > 0.25f;
            herd.target = spot;
            if (herd.wait > 0f && (spot - herd.center).sqrMagnitude > 1.44f)
            {
                herd.wait = 0f;
                moved = true;
            }
            return moved || !on;
        }

        // ---------------------------------------------------------- avoid

        // A calm wander target avoidDistance (plus a little) from the other herd, on this herd's side of it; never a
        // way that passes closer than the herd already is.
        bool MakeRoom(Herd herd, Herd other)
        {
            Vector2 away = herd.center - other.center;
            float d = away.magnitude;
            Vector2 u = d > 1e-3f ? away / d : Ang(RRand(0f, 6.28f));
            // A small island may not have avoidDistance to spare: then as far as it goes, if that is a step at all.
            for (int k = 0; k < 15; k++)
            {
                float want = (avoidDistance + 1f) * (1f - 0.2f * (k / 5));
                if (k >= 5 && want < d + 0.8f) break;
                int n = k % 5;
                float ang = ((n + 1) >> 1) * 0.5f * ((n & 1) == 1 ? 1f : -1f);
                Vector2 t = other.center + Rotate(u, ang) * (want + RRand(0f, 1f));
                if (!OkSpot(herd.spec, t)) continue;
                if (SegmentDistance(herd.center, t, other.center) < Mathf.Min(d, avoidDistance) - 0.3f) continue;
                if (herd.relMode != RelMode.Avoid)
                {
                    BeginRelation(herd, RelMode.Avoid, other, HerdGoal.Avoid);
                    herd.relEnd = _stepClock + 40f;
                }
                herd.rel = other;
                herd.target = t;
                herd.wait = 0f;
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------- guard

        bool StartGuard(Herd herd, Herd threat)
        {
            herd.errandPos = threat.center;
            var sentry = herd.lookout != null && !herd.lookout.hidden ? herd.lookout : PickSentry(herd);
            if (sentry == null)
            {
                herd.relAt = _stepClock + RRand(5f, 10f);
                return false;
            }
            BeginRelation(herd, RelMode.Guard, threat, HerdGoal.Guard);
            herd.errand = Errand.Guard;
            herd.errandStage = 1;
            herd.target = herd.center;
            herd.wait = RRand(guardTime);
            herd.relEnd = _stepClock + herd.wait;
            if (herd.lookout != sentry) PostSentry(herd, sentry);
            ErrandsStarted++;
            Moment(MomentKind.Errand, herd);
            return true;
        }

        Animal PickSentry(Herd herd)
        {
            Animal pick = null;
            float best = float.MaxValue;
            foreach (var a in herd.members)
            {
                if (Young(a) || a.hidden || !IsAwake(a) || a.act != AnimalActivity.None) continue;
                float d = (a.pos - herd.errandPos).sqrMagnitude;
                if (d < best) { best = d; pick = a; }
            }
            return pick;
        }

        // At the edge of the herd on the side of the watched herd.
        void PostSentry(Herd herd, Animal a)
        {
            Vector2 to = herd.errandPos - herd.center;
            Vector2 dir = to.sqrMagnitude > 1e-4f ? to.normalized : new Vector2(0f, 1f);
            Vector2 spot = herd.center + dir * (RadiusOf(herd) * 0.7f + BodyOf(herd));
            if (!OkSpot(herd.spec, spot)) spot = a.pos;
            SetGoal(a, spot, GuardAct);
            herd.lookout = a;
        }

        // The sentry keeps facing the watched herd (its own random look-arounds are held off while it guards).
        void AimSentry(Herd herd)
        {
            var a = herd.lookout;
            if (a == null || !a.hasGoal || !a.arrived) return;
            a.timer = Mathf.Max(a.timer, 3f);
            Vector2 to = herd.errandPos - a.pos;
            if (to.sqrMagnitude < 1e-4f) return;
            float yaw = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
            if (Mathf.Abs(Mathf.DeltaAngle(a.yaw, yaw)) < 15f) return;
            TurnTo(a, yaw);
            _meshDirty = true;
        }

        // ---------------------------------------------------------- gather (the dusk truce)

        public bool TryStartGather(int herdIndex)
        {
            if (_surface == null || _rnd == null || herdIndex < 0 || herdIndex >= _herds.Count || _gatherOn) return false;
            var h = _herds[herdIndex];
            return RelFree(h, false) && !Rising && StartGather(h);
        }

        bool StartGather(Herd herd)
        {
            if (!TryFindShore(herd, DrinkLow, DrinkHigh, out Vector2 dry, out _, out Vector2 dir)) return false;
            _gatherDry = dry;
            _gatherDir = dir;
            _gatherSlots = 0;
            _gatherEnd = float.MaxValue;
            float slowest = 0f;
            if (!JoinGather(herd, gatherApproachTime, ref slowest)) return false;
            foreach (var o in _herds)
                if (o != herd && RelFree(o, false) && (o.center - dry).sqrMagnitude <= Sq(gatherJoinRange)) JoinGather(o, gatherApproachTime, ref slowest);
            _gatherOn = true;
            _gatherDone = true;
            _gatherEnd = _stepClock + slowest + RRand(gatherTime);
            GatheringsStarted++;
            Moment(MomentKind.Gather, herd);
            return true;
        }

        // A herd that comes by while the gathering is on joins when it can still stand with the others a while.
        bool TryJoinGather(Herd herd)
        {
            if ((herd.center - _gatherDry).sqrMagnitude > Sq(gatherJoinRange)) return false;
            float slowest = 0f;
            return JoinGather(herd, _gatherEnd - _stepClock - 20f, ref slowest);
        }

        // The next free place along the shore (alternating sides of the first herd's spot), stepped inland until the
        // species may stand there with the drinking band still in reach.
        bool JoinGather(Herd h, float maxTime, ref float slowest)
        {
            if (maxTime <= 0f) return false;
            Vector2 perp = new Vector2(-_gatherDir.y, _gatherDir.x);
            float step = 2f * RadiusOf(h) + 0.8f;
            float pace = h.spec.speed * gatherPace;
            for (int tries = 0; tries < 6; tries++)
            {
                int k = _gatherSlots + tries;
                Vector2 p = _gatherDry + perp * (((k + 1) >> 1) * step * ((k & 1) == 1 ? 1f : -1f));
                for (float t = 0f; t <= 3f; t += 0.3f)
                {
                    Vector2 c = p - _gatherDir * t;
                    if (!OkSpot(h.spec, c)) continue;
                    if (!TryWaterline(c, _gatherDir, DrinkLow, DrinkHigh, shoreWalk, out _)) break;
                    float time = (c - h.center).magnitude / pace;
                    if (time > maxTime || !WalkableLine(h.spec, h.center, c)) break;
                    _gatherSlots = k + 1;
                    slowest = Mathf.Max(slowest, time);
                    ReleaseLookout(h);
                    StopDig(h);
                    BeginRelation(h, RelMode.Gather, null, HerdGoal.Gather);
                    h.errand = Errand.Gather;
                    h.errandStage = 0;
                    h.errandDir = _gatherDir;
                    h.target = c;
                    h.wait = 0f;
                    ErrandsStarted++;
                    return true;
                }
            }
            return false;
        }
    }
}
