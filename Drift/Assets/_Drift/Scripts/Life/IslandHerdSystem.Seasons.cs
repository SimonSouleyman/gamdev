using System;
using System.Collections.Generic;
using Drift.Core;
using UnityEngine;

namespace Drift.Life
{
    // Seasons and climate seams (v0.6.8, "Jahreszeiten und Verschmelzen"). LifeEnvironment.Season (-1 = no seasons, then
    // only the climate parts below act) splits the year into four:
    //   Frühling  courtship: calm herds start extra plays (goal Court for the play), births x springBirths
    //   Sommer    normal life
    //   Herbst    hoarding: hares and meerkats walk to a stash spot and dig there in turns (Errand.Hoard);
    //             reindeer graze longer at each stop
    //   Winter    cold-sensitive species (all but reindeer, penguin, arctic fox) huddle at dusk and through part of the
    //             night, walk slower, births x winterBirths; penguins huddle by day too, because they like it
    // A cold-sensitive herd on cold ground (ClimateAt < -coldClimate) huddles at dusk in any season. Arctic herds on hot
    // ground look for tree cover (TryFindShelter), and any herd standing in a climate far from its biome's
    // (|ClimateAt - preferred| > climateMismatch) gets its next wander target moved to better climate within climateRange.
    // The winter huddle is no errand: MoveHerd cancels every errand at night (but the oxen's circle), so it lives in the
    // herd's hold and squeezes the members' formation offsets (the storm huddle's placement without its agitation),
    // which are given back when it ends. Nothing here starts while another layer holds the herd (errand or goal set),
    // and nothing but the huddle starts at dusk or night. Draws come from a stream of their own (_seasonRnd).
    public partial class IslandHerdSystem
    {
        [Header("Jahreszeiten und Klima")]
        [Tooltip("Frühling: Chance pro Denk-Takt, dass eine ruhige, stehende Herde ein Balzspiel beginnt (zusätzlich zum normalen Spielen, × playRate).")]
        [Range(0f, 0.3f)] public float courtRate = 0.15f;
        [Tooltip("Frühling: höchstens ein Balzspiel je Herde in so vielen Sekunden.")]
        [Range(0f, 300f)] public float courtGap = 60f;
        [Tooltip("Frühling: Geburten × so viel.")]
        [Range(1f, 2f)] public float springBirths = 1.75f;
        [Tooltip("Winter: Geburten × so viel.")]
        [Range(0f, 1f)] public float winterBirths = 0.5f;
        [Tooltip("Herbst: Chance pro Denk-Takt, dass Hasen oder Erdmännchen zu einem Vorratsplatz ziehen und dort graben.")]
        [Range(0f, 0.2f)] public float hoardRate = 0.06f;
        [Tooltip("Herbst: so lange (s, von–bis) gräbt ein Tier am Vorratsplatz, dann das nächste.")]
        public Vector2 hoardDig = new Vector2(10f, 20f);
        [Tooltip("Herbst: so lange (s, von–bis) bleibt die Herde am Vorratsplatz.")]
        public Vector2 hoardStay = new Vector2(30f, 45f);
        [Tooltip("Herbst: Chance je Halt, dass Rentiere länger grasen (Winterspeck).")]
        [Range(0f, 1f)] public float feastChance = 0.5f;
        [Tooltip("Herbst: so viele Sekunden (von–bis) grasen Rentiere dann länger.")]
        public Vector2 feastExtra = new Vector2(8f, 16f);
        [Tooltip("Winter (oder kaltes Land): Chance je Abend, dass sich eine kälteempfindliche Herde in der Dämmerung zusammenkuschelt.")]
        [Range(0f, 1f)] public float winterHuddleChance = 0.6f;
        [Tooltip("Längste Dauer (s) eines Winterkuschelns; spätestens im Morgengrauen ist es vorbei.")]
        [Range(20f, 400f)] public float winterHuddleMax = 80f;
        [Tooltip("Wie eng die Herde beim Kuscheln steht (× normaler Abstand).")]
        [Range(0.3f, 1f)] public float huddleGather = 0.6f;
        [Tooltip("Winter: Chance pro Denk-Takt, dass Pinguine sich auch tagsüber zusammenkuscheln (sie mögen das).")]
        [Range(0f, 0.2f)] public float penguinHuddleRate = 0.012f;
        [Tooltip("Winter: Wandertempo kälteempfindlicher Arten (× normal).")]
        [Range(0.5f, 1f)] public float winterPace = 0.8f;
        [Tooltip("Unter diesem Klimawert (-1 kalt … +1 heiß) frieren kälteempfindliche Arten auch außerhalb des Winters.")]
        [Range(0f, 1f)] public float coldClimate = 0.3f;
        [Tooltip("Über diesem Klimawert suchen Polartiere Schatten unter Bäumen.")]
        [Range(0f, 1f)] public float hotClimate = 0.3f;
        [Tooltip("Chance pro Denk-Takt, dass eine Polar-Herde auf heißem Land Schatten sucht.")]
        [Range(0f, 0.3f)] public float shadeSeekRate = 0.05f;
        [Tooltip("Weicht das Klima unter einer Herde um mehr als das von dem ihres Bioms ab, zieht sie zum besseren Klima.")]
        [Range(0.2f, 1.5f)] public float climateMismatch = 0.6f;
        [Tooltip("So weit (Einheiten) sucht eine Herde nach passenderem Klima.")]
        [Range(2f, 15f)] public float climateRange = 8f;

        public const int Spring = 0, Summer = 1, Autumn = 2, Winter = 3;
        static readonly string[] SeasonNames = { "Frühling", "Sommer", "Herbst", "Winter" };
        // Below this much night the evening's huddle roll is armed again; a huddle ends once the night falls under it.
        const float DayNight = 0.1f, HuddleDawn = 0.2f;

        enum SeasonHold { None, Huddle, Court, Hoard, Feast, ClimateWalk, ShadeWalk, ShadeStay }

        partial class Herd
        {
            // What the seasons layer holds the herd for, its clock / spot, and the climate under the herd (sampled each s).
            public SeasonHold hold;
            public float holdT, climate, climateT, nudgeCool, hoardCool, courtCool, digGap;
            public Vector2 holdSpot, feastTarget;
            public bool nightRolled, dayHuddle;
            // Huddle: who was squeezed, their own offsets and the factor, so the formation can be given back.
            public List<Animal> huddled;
            public List<Vector2> huddleOff;
            public float huddleK = 1f;
        }

        System.Random _seasonRnd;
        int _seasonNow = -1;
        bool _anyHold;
        Func<Vector2, float> _climateOf;
        Func<Vector2, bool> _okFor;
        Species _okSpec;

        public int CourtshipsStarted { get; private set; }
        public int HoardsStarted { get; private set; }
        public int HuddlesStarted { get; private set; }
        public int ClimateNudges { get; private set; }

        // 0 spring, 1 summer, 2 autumn, 3 winter from the 0..1 year; -1 without seasons.
        public static int SeasonOf(float season) => season < 0f ? -1 : Mathf.Clamp((int)(Mathf.Repeat(season, 1f) * 4f), 0, 3);
        public static string SeasonName(int season) => season >= 0 && season < SeasonNames.Length ? SeasonNames[season] : "";
        public static string CurrentSeasonName => SeasonName(SeasonOf(LifeEnvironment.Season));
        public int CurrentSeason => _seasonNow;

        // -1 cold .. +1 hot: what the species' home biome is like (arctic cold, tropical and savanna hot, temperate mild).
        public static float PreferredClimate(int biome) => biome == 2 ? -1f : biome == 1 || biome == 3 ? 1f : 0f;
        public static bool ColdSensitive(LifeKind kind) => kind != LifeKind.Reindeer && kind != LifeKind.Penguin && kind != LifeKind.ArcticFox;
        public float SeasonGrowthFactor(int season) => season == Spring ? springBirths : season == Winter ? winterBirths : 1f;

        // Where a herd standing in the wrong climate drifts to: of two rings of eight spots (half and full range) the
        // one whose climate is closest to the preferred, if it is clearly better than here; nearer wins a tie.
        public static bool TryClimateNudge(Vector2 from, float preferred, Func<Vector2, float> climate, Func<Vector2, bool> ok,
            float range, float phase, out Vector2 target)
        {
            target = from;
            float best = Mathf.Abs(climate(from) - preferred) - 0.15f;
            bool found = false;
            for (int ring = 1; ring <= 2; ring++)
                for (int k = 0; k < 8; k++)
                {
                    float ang = phase + (k + ring * 0.5f) * (Mathf.PI * 0.25f);
                    Vector2 c = from + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (range * ring * 0.5f);
                    if (ok != null && !ok(c)) continue;
                    float miss = Mathf.Abs(climate(c) - preferred);
                    if (miss >= best) continue;
                    best = miss;
                    target = c;
                    found = true;
                }
            return found;
        }

        float SRand(float a, float b) => a + (float)_seasonRnd.NextDouble() * (b - a);
        bool SChance(float p) => _seasonRnd.NextDouble() < p;
        float ClimateHere(Vector2 p) => _life != null ? _life.ClimateAt(p) : 0f;
        bool Cold(Herd herd) => _seasonNow == Winter || herd.climate < -coldClimate;

        static HerdGoal GoalOfHold(SeasonHold hold)
        {
            switch (hold)
            {
                case SeasonHold.Huddle: return HerdGoal.Huddle;
                case SeasonHold.Court: return HerdGoal.Court;
                case SeasonHold.Hoard: case SeasonHold.Feast: return HerdGoal.Hoard;
                case SeasonHold.None: return HerdGoal.None;
                default: return HerdGoal.Climate;
            }
        }

        void SetHold(Herd herd, SeasonHold hold)
        {
            herd.hold = hold;
            herd.goal = GoalOfHold(hold);
            _anyHold = true;
        }

        void EndHold(Herd herd)
        {
            var was = herd.hold;
            if (was == SeasonHold.None) return;
            herd.hold = SeasonHold.None;
            if (was == SeasonHold.Huddle && !AnySleeping(herd)) Unhuddle(herd);
            if (herd.goal == GoalOfHold(was)) herd.goal = HerdGoal.None;
            if (was == SeasonHold.Hoard && herd.errand == Errand.Hoard) CancelErrand(herd);
        }

        partial void TickSeasons(float dt)
        {
            _seasonRnd ??= new System.Random(unchecked(seed * 131 + 0x5EA5));
            _seasonNow = SeasonOf(LifeEnvironment.Season);
            // The far tier never steps the herds: whatever the layer holds ends there (DriftHerds drops errands too).
            if (_anyHold && Tier == LifeTier.Far)
            {
                foreach (var h in _herds) EndHold(h);
                _anyHold = false;
            }
        }

        partial void GrowthFactorSeasons(Herd herd, ref float factor)
        {
            if (_seasonNow >= 0) factor *= SeasonGrowthFactor(_seasonNow);
        }

        partial void SeasonsThink(Herd herd, bool standing, ref bool started)
        {
            // A herd the layer holds (dusk huddle, longer grazing, a walk to better climate) does nothing else meanwhile.
            if (herd.hold != SeasonHold.None) { started = true; return; }
            if (herd.errand != Errand.None || herd.goal != HerdGoal.None || behaviourRate <= 0f || _seasonRnd == null) return;
            var s = herd.spec;
            float rate = behaviourRate;
            bool day = _night < duskThreshold;
            if (day && herd.nudgeCool <= 0f && StartClimate(herd)) { started = true; return; }
            switch (_seasonNow)
            {
                case Spring:
                    if (standing && herd.wait > 3f && herd.courtCool <= 0f && herd.members.Count >= 3 && s.play > 0f && SChance(Mathf.Min(courtRate * playRate, 0.3f) * rate))
                    {
                        StartPlay(herd);
                        if (herd.playT > 0f)
                        {
                            SetHold(herd, SeasonHold.Court);
                            herd.courtCool = courtGap;
                            CourtshipsStarted++;
                            Moment(MomentKind.Court, herd);
                            started = true;
                        }
                    }
                    break;
                case Autumn:
                    if (!day || !standing) break;
                    if ((s.kind == LifeKind.Hare || s.kind == LifeKind.Meerkat) && herd.wait > 4f && herd.hoardCool <= 0f && SChance(hoardRate * rate))
                    {
                        if (StartHoard(herd)) started = true;
                    }
                    else if (s.kind == LifeKind.Reindeer && herd.feastTarget != herd.target)
                    {
                        // One roll per stop (the wander target changes with every stop).
                        herd.feastTarget = herd.target;
                        if (SChance(feastChance * rate))
                        {
                            herd.holdT = SRand(feastExtra.x, feastExtra.y);
                            herd.wait += herd.holdT;
                            SetHold(herd, SeasonHold.Feast);
                            started = true;
                        }
                    }
                    break;
                case Winter:
                    if (s.kind == LifeKind.Penguin && day && standing && herd.wait > 4f && SChance(penguinHuddleRate * rate))
                    {
                        StartHuddle(herd, huddleGather * 0.85f, SRand(25f, 45f), true);
                        started = true;
                    }
                    break;
            }
        }

        // Arctic herds on hot ground walk into tree cover; any herd far from its home climate moves its next wander
        // target towards a better one. A failed search waits a while before the next.
        bool StartClimate(Herd herd)
        {
            var s = herd.spec;
            float preferred = PreferredClimate(s.biome);
            bool hot = s.biome == 2 && herd.climate > hotClimate;
            bool wrong = Mathf.Abs(herd.climate - preferred) > climateMismatch;
            if (!hot && !wrong) return false;
            if (hot && SChance(shadeSeekRate * behaviourRate) && _life != null
                && _life.TryFindShelter(herd.center, shadeRange, _seasonRnd, out Vector2 shade) && OkSpot(s, shade))
            {
                WalkTo(herd, shade, SeasonHold.ShadeWalk);
                return true;
            }
            if (!wrong) return false;
            _climateOf ??= ClimateHere;
            _okFor ??= p => OkSpot(_okSpec, p);
            _okSpec = s;
            if (TryClimateNudge(herd.center, preferred, _climateOf, _okFor, climateRange, SRand(0f, 6.28f), out Vector2 better))
            {
                WalkTo(herd, better, SeasonHold.ClimateWalk);
                ClimateNudges++;
                return true;
            }
            herd.nudgeCool = SRand(20f, 40f);
            return false;
        }

        void WalkTo(Herd herd, Vector2 spot, SeasonHold hold)
        {
            herd.target = herd.holdSpot = spot;
            herd.wait = Mathf.Min(herd.wait, SRand(0.5f, 2f));
            herd.nudgeCool = SRand(20f, 40f);
            SetHold(herd, hold);
        }

        // A stash spot 1.5..3.5 units off, on the species' own ground and in the climate closest to its home.
        bool StartHoard(Herd herd)
        {
            var s = herd.spec;
            float preferred = PreferredClimate(s.biome);
            Vector2 best = herd.center;
            float bestMiss = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                float ang = SRand(0f, Mathf.PI * 2f);
                Vector2 c = herd.center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * SRand(1.5f, 3.5f);
                if (!OkSpot(s, c) || (i < 5 && NearOtherHerd(c, herd))) continue;
                float miss = Mathf.Abs(ClimateHere(c) - preferred);
                if (miss >= bestMiss) continue;
                bestMiss = miss;
                best = c;
            }
            if (bestMiss == float.MaxValue)
            {
                herd.hoardCool = SRand(20f, 40f);
                return false;
            }
            herd.errand = Errand.Hoard;
            herd.errandStage = 0;
            herd.target = best;
            herd.wait = 0f;
            herd.hoardCool = SRand(40f, 80f);
            SetHold(herd, SeasonHold.Hoard);
            ErrandsStarted++;
            HoardsStarted++;
            Moment(MomentKind.Errand, herd);
            return true;
        }

        partial void ArriveSeasons(Herd herd)
        {
            if (herd.errand == Errand.Hoard)
            {
                herd.wait = SRand(hoardStay.x, hoardStay.y);
                herd.digGap = SRand(0.5f, 2f);
            }
            else herd.wait = SRand(2f, 4f);
        }

        void StartHuddle(Herd herd, float k, float seconds, bool byDay)
        {
            Unhuddle(herd);
            herd.huddled ??= new List<Animal>();
            herd.huddleOff ??= new List<Vector2>();
            herd.huddled.Clear();
            herd.huddleOff.Clear();
            foreach (var a in herd.members)
            {
                herd.huddled.Add(a);
                herd.huddleOff.Add(a.offset);
                a.offset *= k;
            }
            herd.huddleK = k;
            herd.holdT = seconds;
            herd.dayHuddle = byDay;
            if (herd.playT > 0f) EndPlay(herd);
            SetHold(herd, SeasonHold.Huddle);
            HuddlesStarted++;
            Moment(MomentKind.Huddle, herd);
        }

        // A member whose offset was set anew meanwhile (adopted, promoted, folded in) keeps the new one. Sleepers are
        // never handed back their offsets: a slot that jumps by more than two bodies makes a lying animal get up and walk,
        // so a huddle that ends in the night leaves the pile as it is until the herd is awake (StepSeasons).
        void Unhuddle(Herd herd)
        {
            if (herd.huddled == null) return;
            for (int i = 0; i < herd.huddled.Count; i++)
            {
                var a = herd.huddled[i];
                if ((a.offset - herd.huddleOff[i] * herd.huddleK).sqrMagnitude < 1e-8f) a.offset = herd.huddleOff[i];
            }
            herd.huddled.Clear();
            herd.huddleOff.Clear();
            herd.huddleK = 1f;
        }

        // The formation offset a member has outside a huddle: what a save has to keep (Capture).
        Vector2 OwnOffset(Herd herd, Animal a)
        {
            if (herd.huddled == null || herd.huddled.Count == 0) return a.offset;
            int i = herd.huddled.IndexOf(a);
            return i >= 0 && (a.offset - herd.huddleOff[i] * herd.huddleK).sqrMagnitude < 1e-8f ? herd.huddleOff[i] : a.offset;
        }

        partial void StepSeasons(Herd herd, float dt, ref float mul)
        {
            herd.climateT -= dt;
            if (herd.climateT <= 0f)
            {
                herd.climateT = 1f;
                herd.climate = ClimateHere(herd.center);
            }
            herd.nudgeCool = Mathf.Max(0f, herd.nudgeCool - dt);
            herd.hoardCool = Mathf.Max(0f, herd.hoardCool - dt);
            herd.courtCool = Mathf.Max(0f, herd.courtCool - dt);
            bool coldOne = ColdSensitive(herd.spec.kind);
            if (herd.hold != SeasonHold.Huddle && herd.huddled != null && herd.huddled.Count > 0 && !AnySleeping(herd)) Unhuddle(herd);
            if (herd.hold != SeasonHold.None) StepHold(herd, dt);
            else if (_night < DayNight) herd.nightRolled = false;
            else if (_night >= duskThreshold && _night < sleepThreshold && !herd.nightRolled && herd.errand == Errand.None && herd.goal == HerdGoal.None
                     && herd.playT <= 0f && herd.stampT <= 0f && !herd.refuging && behaviourRate > 0f && _seasonRnd != null && !AnySleeping(herd))
            {
                // One roll per evening: the winter huddle, the only thing this layer starts at dusk or night.
                herd.nightRolled = true;
                bool wants = coldOne ? Cold(herd) : herd.spec.kind == LifeKind.Penguin && _seasonNow == Winter;
                if (wants && SChance(winterHuddleChance)) StartHuddle(herd, huddleGather, winterHuddleMax, false);
            }
            if (coldOne && Cold(herd) && !herd.refuging && herd.errand != Errand.Meet) mul *= winterPace;
        }

        void StepHold(Herd herd, float dt)
        {
            if (herd.goal != GoalOfHold(herd.hold)) { EndHold(herd); return; }
            switch (herd.hold)
            {
                case SeasonHold.Huddle:
                    herd.holdT -= dt;
                    bool still = herd.dayHuddle
                        ? _seasonNow == Winter && _night < duskThreshold
                        : (ColdSensitive(herd.spec.kind) ? Cold(herd) : _seasonNow == Winter) && _night >= HuddleDawn;
                    if (!still || herd.holdT <= 0f || herd.errand != Errand.None) { EndHold(herd); return; }
                    // Short waits keep the herd in place without letting the long-wait plays and rests in.
                    herd.wait = Mathf.Clamp(herd.wait, 1f, 2f);
                    if (herd.playT > 0f) EndPlay(herd);
                    break;
                case SeasonHold.Court:
                    if (herd.playT <= 0f) EndHold(herd);
                    break;
                case SeasonHold.Feast:
                    herd.holdT -= dt;
                    if (herd.holdT <= 0f || herd.wait <= 0f || herd.errand != Errand.None) EndHold(herd);
                    break;
                case SeasonHold.Hoard:
                    if (herd.errand != Errand.Hoard) { EndHold(herd); return; }
                    if (herd.errandStage != 1 || herd.digger != null) break;
                    herd.digGap -= dt;
                    if (herd.digGap > 0f) break;
                    herd.digGap = SRand(1f, 3f);
                    if (StartDig(herd))
                    {
                        var a = herd.digger;
                        a.actT = SRand(hoardDig.x, hoardDig.y);
                        a.timer = a.actT + 1f;
                    }
                    break;
                case SeasonHold.ClimateWalk:
                case SeasonHold.ShadeWalk:
                    if (herd.errand != Errand.None) { EndHold(herd); return; }
                    if (herd.target == herd.holdSpot) break;
                    // MoveHerd picked the next wander target on arrival; anything else re-targeted the herd.
                    if (herd.hold == SeasonHold.ShadeWalk && (herd.center - herd.holdSpot).sqrMagnitude < 0.25f)
                    {
                        herd.wait = SRand(20f, 35f);
                        herd.hold = SeasonHold.ShadeStay;
                    }
                    else EndHold(herd);
                    break;
                case SeasonHold.ShadeStay:
                    if (herd.wait <= 0f || herd.errand != Errand.None) EndHold(herd);
                    break;
            }
        }
    }
}
