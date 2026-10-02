using Drift.Core;
using UnityEngine;

namespace Drift.Life
{
    // Where a herd finds food, shade and shelter. IslandLifeSystem answers in the game; tests install a fake
    // (IslandHerdSystem.PastureOverride) so they do not depend on how the vegetation grid turns into food.
    public interface IHerdPasture
    {
        float FoodAt(Vector2 local, Diet diet);
        float Graze(Vector2 local, Diet diet, float amount);
        bool TryFindFood(Vector2 from, Diet diet, float radius, float minFood, System.Random rnd, out Vector2 target);
        float ShadeAt(Vector2 local);
        bool TryFindShelter(Vector2 from, float radius, System.Random rnd, out Vector2 target);
    }

    // Needs layer, "Futter und Wetter" (v0.6.8, owner-approved; nobody dies of it, a herd never shrinks):
    //   Hunger    every herd gets hungry over herd time (hungerFullTime to full, slower at night) and eats where its
    //             diet grows: standing on food it nibbles along (passiveFeedRate), a hungry one grazes in a bout
    //             (Errand.Graze, grazeBout s at most, then the old errands roll again). Fish eaters feed on their
    //             shore errands instead (Drink / Wade / Slide), giraffes browse a tree (Errand.Browse).
    //   Migrate   a hungry herd on bare ground walks to the best patch within a species-sized range and grazes there;
    //             without one it wanders slower (hungryPace) and bears fewer young (1 - hungerBirths * hunger).
    //   Shade     at noon herds on hot land go under a tree for a while (once per noon); in rain everyone does
    //             (Errand.Shelter, goal Shade / Shelter). Sheep keep their own shade errand (StartShade).
    //   Scatter   a strong gust (the island's storm above scatterStorm) breaks a huddling herd apart for a few
    //             seconds; the huddle pulls it back together. The huddle cancels every errand, so the scatter runs
    //             on member goals (act Flee, which CancelErrand leaves alone) and a herd timer, not as an errand.
    // Draws come from a stream of their own (_needRnd), so the island's other behaviours keep their sequences.
    public partial class IslandHerdSystem
    {
        [Header("Futter und Wetter")]
        [Tooltip("Stärke der Bedürfnisse (Hunger, Schatten, Schutz, Sturm). 0 = aus, die Herden leben wie vorher.")]
        [Range(0f, 2f)] public float needsRate = 1f;
        [Tooltip("So viele Sekunden braucht eine Herde ohne Futter, bis sie ganz hungrig ist (nachts dreimal so lange).")]
        [Range(60f, 1200f)] public float hungerFullTime = 420f;
        [Tooltip("Ab diesem Hunger grast eine Herde auf Futter gezielt (Weidegang), Fischfresser gehen ans Ufer.")]
        [Range(0f, 1f)] public float grazeHunger = 0.35f;
        [Tooltip("Ab diesem Hunger zieht eine Herde ohne Futter unter sich zur nächsten guten Weide.")]
        [Range(0f, 1f)] public float migrateHunger = 0.5f;
        [Tooltip("So viel Futter (0–1) muss unter der Herde wachsen, damit sie dort frisst.")]
        [Range(0f, 1f)] public float grazeMinFood = 0.3f;
        [Tooltip("So viel Futter (0–1) muss eine neue Weide haben, damit sich der Weg lohnt.")]
        [Range(0f, 1f)] public float migrateMinFood = 0.45f;
        [Tooltip("Suchweite (Einheiten) nach einer neuen Weide: kleine Arten (Hase) – große Arten (Ochse).")]
        public Vector2 migrateRange = new Vector2(6f, 14f);
        [Tooltip("Länge eines Weidegangs (s, von–bis); satt endet er früher. Danach kommen wieder die anderen Besorgungen dran.")]
        public Vector2 grazeBout = new Vector2(30f, 50f);
        [Tooltip("Sättigung je Sekunde im Weidegang (und beim Laubfressen).")]
        [Range(0f, 0.1f)] public float boutFeedRate = 0.016f;
        [Tooltip("Sättigung je Sekunde, wenn eine Herde einfach auf Futter steht.")]
        [Range(0f, 0.05f)] public float passiveFeedRate = 0.002f;
        [Tooltip("Sättigung je Sekunde für Fischfresser bei einer Uferbesorgung (Trinken, Waten, Rutschen).")]
        [Range(0f, 0.1f)] public float fishFeedRate = 0.02f;
        [Tooltip("Was ein Tier je Sekunde Weidegang abfrisst (Vegetationsstufe je Zelle). Mit der Weidespur der Insel (grazeMarkGain 8: nach 0,125 Stufen abgeweidet) weidet eine Achterherde eine Zelle mit 0,0002 in etwa 80 s ab.")]
        [Range(0f, 0.005f)] public float grazeBite = 0.0002f;
        [Tooltip("Wandertempo einer hungrigen Herde, die keine Weide findet (× normal).")]
        [Range(0.3f, 1f)] public float hungryPace = 0.7f;
        [Tooltip("Geburten × (1 − das × Hunger): hungrige Herden bekommen weniger Junge, sterben aber nie.")]
        [Range(0f, 1f)] public float hungerBirths = 0.8f;
        [Tooltip("Mittag: Chance pro Denk-Takt, dass eine Herde auf heißem Land Schatten unter Bäumen sucht (gemäßigtes Land 40 %, Nordland nie).")]
        [Range(0f, 1f)] public float noonShadeChance = 0.3f;
        [Tooltip("Ab diesem Regen (0–1) suchen alle Herden Schutz unter Bäumen; leichter Inselregen zählt auch.")]
        [Range(0f, 1f)] public float shelterRain = 0.4f;
        [Tooltip("Chance pro Denk-Takt, dass eine Herde im Regen Schutz sucht.")]
        [Range(0f, 1f)] public float shelterChance = 0.5f;
        [Tooltip("So weit (Einheiten) sucht eine Herde nach Schatten oder Schutz.")]
        [Range(2f, 20f)] public float shelterRange = 8f;
        [Tooltip("So lange (s, von–bis) ruht eine Herde unter dem Baum.")]
        public Vector2 shelterStay = new Vector2(20f, 45f);
        [Tooltip("Pause (s, von–bis) nach einem Weidegang, Weidewechsel oder Schutzgang, bevor das nächste Bedürfnis dran ist.")]
        public Vector2 needCooldown = new Vector2(30f, 60f);
        [Tooltip("Ab dieser Sturmstärke auf der Insel reißen Böen die kauernden Herden kurz auseinander (0 = nie).")]
        [Range(0f, 1f)] public float scatterStorm = 0.6f;
        [Tooltip("Abstand (s, von–bis) zwischen zwei Böen, die Herden zerstreuen.")]
        public Vector2 scatterGap = new Vector2(20f, 40f);
        [Tooltip("Chance je Bö, dass eine Herde auseinanderstiebt.")]
        [Range(0f, 1f)] public float scatterChance = 0.6f;
        [Tooltip("So lange (s, von–bis) läuft eine Herde auseinander, bevor sie sich wieder sammelt.")]
        public Vector2 scatterTime = new Vector2(3f, 5f);

        // Feeding is booked in steps of this much herd time, not every frame.
        const float NeedTick = 1f;

        partial class Herd
        {
            // Needs layer, runtime only (never saved): hunger 0 fed .. 1 hungry, seeded on the first tick; needCool =
            // seconds until the next need may send the herd somewhere; needErrand / needGoal = the errand this layer
            // started and the goal it shows; scatterT = seconds left of a storm scatter.
            public float hunger, needCool, needT, scatterT;
            public bool needSeeded, starving, noonShaded, fishTrip;
            public Errand needErrand;
            public HerdGoal needGoal, needGoalSet;
            public int biteAt;
        }

        // Tests install a fake pasture; null = the island's IslandLifeSystem.
        [System.NonSerialized] public IHerdPasture PastureOverride;

        System.Random _needRnd;
        float _gustT = -1f;

        public int GrazeBouts { get; private set; }
        public int Migrations { get; private set; }
        public int MigrationsArrived { get; private set; }
        public int ShadeTrips { get; private set; }
        public int ShelterTrips { get; private set; }
        public int FishTrips { get; private set; }
        public int Scatters { get; private set; }
        public int FoodSearchesFailed { get; private set; }

        public float HerdHunger(int index) => _herds[index].hunger;
        // 0 fed .. 1 very hungry (the watch card's "satt / hungrig / sehr hungrig"); -1 for an invalid index.
        public float HungerOf(int herd) => herd >= 0 && herd < _herds.Count ? _herds[herd].hunger : -1f;
        public bool HerdStarving(int index) => _herds[index].starving;
        public void SetHerdHunger(int index, float hunger)
        {
            var h = _herds[index];
            h.needSeeded = true;
            h.hunger = Mathf.Clamp01(hunger);
        }
        // What the needs layer does to the herd's birth chance right now.
        public float NeedsGrowthFactor(int index)
        {
            float f = 1f;
            GrowthFactorNeeds(_herds[index], ref f);
            return f;
        }

        System.Random NeedRnd => _needRnd ??= new System.Random(HerdSeed ^ 0x4E33D5);
        float NeedRand() => (float)NeedRnd.NextDouble();
        float NeedRand(float a, float b) => a + NeedRand() * (b - a);
        float NeedRand(Vector2 range) => NeedRand(range.x, range.y);

        // ---------------------------------------------------------- pasture

        float Food(Vector2 p, Diet diet) => PastureOverride != null ? PastureOverride.FoodAt(p, diet) : _life != null ? _life.FoodAt(p, diet) : 1f;
        float ShadeHere(Vector2 p) => PastureOverride != null ? PastureOverride.ShadeAt(p) : _life != null ? _life.ShadeAt(p) : 0f;

        void Bite(Vector2 p, Diet diet, float amount)
        {
            if (amount <= 0f) return;
            if (PastureOverride != null) PastureOverride.Graze(p, diet, amount);
            else if (_life != null) _life.Graze(p, diet, amount);
        }

        bool FindFood(Vector2 from, Diet diet, float radius, out Vector2 target)
        {
            target = from;
            if (PastureOverride != null) return PastureOverride.TryFindFood(from, diet, radius, migrateMinFood, NeedRnd, out target);
            return _life != null && _life.TryFindFood(from, diet, radius, migrateMinFood, NeedRnd, out target);
        }

        // The densest cover in range; without one (or before the life system knows cover) a tree, as StartShade picks it.
        bool FindShelter(Herd herd, out Vector2 spot)
        {
            spot = herd.center;
            if (PastureOverride != null) return PastureOverride.TryFindShelter(herd.center, shelterRange, NeedRnd, out spot);
            if (_life == null) return false;
            if (_life.TryFindShelter(herd.center, shelterRange, NeedRnd, out spot)) return true;
            if (!_life.TryRandomPlant(LifeKind.Tree, NeedRnd, herd.center, shelterRange, out Vector2 tree)
                && !_life.TryRandomPlant(LifeKind.Palm, NeedRnd, herd.center, shelterRange, out tree)) return false;
            Vector2 away = herd.center - tree;
            spot = tree + (away.sqrMagnitude > 1e-4f ? away.normalized : new Vector2(1f, 0f)) * 0.15f;
            return true;
        }

        // Pulls a spot back towards the herd until it is ground the species may stand on and walk to in a straight line.
        bool Reachable(Herd herd, ref Vector2 spot)
        {
            var s = herd.spec;
            for (int k = 0; k < 4; k++)
            {
                Vector2 p = Vector2.Lerp(herd.center, spot, 1f - 0.2f * k);
                if (!OkSpot(s, p) || !CoastOk(s, p) || !WalkableLine(s, herd.center, p)) continue;
                spot = p;
                return true;
            }
            return false;
        }

        float MigrateReach(Species s) => Mathf.Lerp(migrateRange.x, migrateRange.y, Mathf.InverseLerp(0.26f, 0.66f, s.body));

        // 0 cold .. 1 hot: the biome under the herd, warmed or cooled by the island's climate there.
        float Heat(Herd herd)
        {
            float b;
            switch ((LifeBiome)BiomeAt(herd.center))
            {
                case LifeBiome.Tropical:
                case LifeBiome.Savanna: b = 1f; break;
                case LifeBiome.Nordic: b = 0f; break;
                default: b = 0.4f; break;
            }
            if (_life != null) b += 0.6f * _life.ClimateAt(herd.center);
            return Mathf.Clamp01(b);
        }

        bool Wet(Herd herd)
        {
            if (Raining) return true;
            Vector2 c = herd.center;
            return LifeEnvironment.RainAt(transform.TransformPoint(new Vector3(c.x, _surface.SampleHeight(c), c.y))) > shelterRain;
        }

        static bool NeedsGoal(HerdGoal g) =>
            g == HerdGoal.Graze || g == HerdGoal.Migrate || g == HerdGoal.Shade || g == HerdGoal.Shelter || g == HerdGoal.Scatter;

        static bool FishErrand(Errand e) =>
            e == Errand.Drink || e == Errand.Wade || e == Errand.Slide || e == Errand.Soak || e == Errand.Parade || e == Errand.Signature;

        // ---------------------------------------------------------- hooks

        partial void TickNeeds(float dt)
        {
            int n = _herds.Count;
            if (n == 0) return;
            bool far = Tier == LifeTier.Far;
            bool noon = Noon;
            float rise = needsRate > 0f && hungerFullTime > 0f && !far ? dt / hungerFullTime * (_night > sleepThreshold ? 0.3f : 1f) : 0f;
            bool gust = Gust(dt, far);
            for (int i = 0; i < n; i++)
            {
                var h = _herds[i];
                if (!h.needSeeded)
                {
                    h.needSeeded = true;
                    h.hunger = NeedRand(0f, 0.25f);
                    h.needT = NeedRand(0f, NeedTick);
                }
                h.hunger = Mathf.Min(1f, h.hunger + rise);
                if (h.needCool > 0f) h.needCool -= dt;
                if (!noon) h.noonShaded = false;
                if (h.needErrand != Errand.None && h.errand != h.needErrand)
                {
                    h.needErrand = Errand.None;
                    h.fishTrip = false;
                    h.needCool = Mathf.Max(h.needCool, NeedRand(needCooldown));
                }
                if (h.scatterT > 0f) StepScatter(h, dt);
                else if (gust && NeedRand() < scatterChance) StartScatter(h);
                KeepGoal(h);
            }
        }

        partial void NeedsThink(Herd herd, bool standing, ref bool started)
        {
            if (needsRate <= 0f || herd.needCool > 0f || herd.members.Count == 0 || herd.scatterT > 0f) return;
            if (WeatherThink(herd) || HungerThink(herd, standing)) started = true;
        }

        partial void ArriveNeeds(Herd herd)
        {
            switch (herd.errand)
            {
                case Errand.Migrate:
                    MigrationsArrived++;
                    BeginGraze(herd);
                    break;
                case Errand.Graze:
                    BeginGraze(herd);
                    break;
                case Errand.Shelter:
                    ArriveShelter(herd);
                    break;
                default:
                    herd.wait = NeedRand(1f, 2f);
                    break;
            }
        }

        partial void StepNeeds(Herd herd, float dt, ref float mul)
        {
            if (needsRate <= 0f) return;
            if (herd.starving && herd.errand == Errand.None) mul *= hungryPace;
            herd.needT += dt;
            if (herd.needT < NeedTick) return;
            float t = herd.needT;
            herd.needT = 0f;
            Feed(herd, t);
        }

        partial void GrowthFactorNeeds(Herd herd, ref float factor)
        {
            if (needsRate > 0f) factor *= 1f - hungerBirths * Mathf.Clamp01(herd.hunger);
        }

        // The goal follows what the herd is doing for this layer; a goal another layer set is left alone.
        void KeepGoal(Herd h)
        {
            HerdGoal want = HerdGoal.None;
            if (h.scatterT > 0f) want = HerdGoal.Scatter;
            else
                switch (h.errand)
                {
                    case Errand.Graze: want = HerdGoal.Graze; break;
                    case Errand.Migrate: want = HerdGoal.Migrate; break;
                    case Errand.Shelter: want = h.needGoal == HerdGoal.Shade ? HerdGoal.Shade : HerdGoal.Shelter; break;
                    case Errand.Shade: want = HerdGoal.Shade; break;
                    case Errand.Browse: want = HerdGoal.Graze; break;
                    default:
                        if (h.fishTrip && h.errand == h.needErrand) want = HerdGoal.Graze;
                        break;
                }
            if (want != HerdGoal.None)
            {
                if (h.goal == HerdGoal.None || h.goal == h.needGoalSet) h.goal = h.needGoalSet = want;
            }
            else if (h.goal != HerdGoal.None && h.goal == h.needGoalSet && NeedsGoal(h.goal))
            {
                h.goal = h.needGoalSet = HerdGoal.None;
            }
        }

        void ShowGoal(Herd herd, HerdGoal goal)
        {
            if (herd.goal == HerdGoal.None || herd.goal == herd.needGoalSet || NeedsGoal(herd.goal)) herd.goal = herd.needGoalSet = goal;
        }

        // ---------------------------------------------------------- hunger

        bool HungerThink(Herd herd, bool standing)
        {
            var s = herd.spec;
            float hunger = herd.hunger;
            if (hunger < grazeHunger) return false;
            if (s.diet == Diet.Fish)
            {
                if (Rising) return false;
                Errand e = s.kind == LifeKind.Flamingo ? Errand.Wade : s.kind == LifeKind.Penguin ? Errand.Slide : Errand.Drink;
                if (!StartShoreErrand(herd, e))
                {
                    herd.starving = hunger >= migrateHunger;
                    herd.needCool = NeedRand(15f, 25f);
                    FoodSearchesFailed++;
                    return false;
                }
                herd.fishTrip = true;
                herd.needErrand = e;
                ShowGoal(herd, HerdGoal.Graze);
                FishTrips++;
                return true;
            }
            if (Food(herd.center, s.diet) >= grazeMinFood)
            {
                herd.starving = false;
                // A very hungry herd stops on the way to eat (tortoises hardly ever stand otherwise).
                if ((!standing || herd.wait < 2f) && hunger < migrateHunger) return false;
                if (s.diet == Diet.Leaves && StartBrowse(herd))
                {
                    herd.needErrand = Errand.Browse;
                    ShowGoal(herd, HerdGoal.Graze);
                    GrazeBouts++;
                    return true;
                }
                BeginGraze(herd);
                return true;
            }
            if (hunger < migrateHunger) return false;
            if (StartMigrate(herd)) return true;
            herd.starving = true;
            herd.needCool = NeedRand(15f, 25f);
            FoodSearchesFailed++;
            return false;
        }

        bool StartMigrate(Herd herd)
        {
            var s = herd.spec;
            if (!FindFood(herd.center, s.diet, MigrateReach(s), out Vector2 spot) || !Reachable(herd, ref spot)) return false;
            if ((spot - herd.center).sqrMagnitude < 0.25f) return false;
            herd.errand = Errand.Migrate;
            herd.errandStage = 0;
            herd.target = spot;
            herd.wait = 0f;
            herd.needErrand = Errand.Migrate;
            ShowGoal(herd, HerdGoal.Migrate);
            Migrations++;
            ErrandsStarted++;
            Moment(MomentKind.Migrate, herd);
            return true;
        }

        // A grazing bout where the herd stands (or where its migration ended): heads down, at most grazeBout long.
        // All grown-ups put their heads down, every other one carries the activity; the young and the rest stay free
        // for play, watching and the idle moves (StartPlay and the like only pick members without an activity).
        void BeginGraze(Herd herd)
        {
            var s = herd.spec;
            herd.errand = Errand.Graze;
            herd.errandStage = 1;
            herd.wait = NeedRand(grazeBout);
            herd.needErrand = Errand.Graze;
            herd.starving = false;
            ShowGoal(herd, HerdGoal.Graze);
            GrazeBouts++;
            int k = 0;
            foreach (var a in herd.members)
            {
                if (a.hidden || a.hasGoal || Young(a) || (a.act != AnimalActivity.None && a.act != AnimalActivity.Line)) continue;
                a.act = (k++ & 1) == 0 ? AnimalActivity.Graze : AnimalActivity.None;
                if (a.state == AnimalState.Graze || a.state == AnimalState.Look || a.state == AnimalState.Walk)
                    SetState(a, AnimalState.Graze, NeedRand(s.grazeMin, s.grazeMax), s.grazePitch, false);
            }
            _meshDirty = true;
        }

        // A grazing bout gives way to whatever the director, a choreography or a meeting wants to start (for HerdFree,
        // StartChoreography and MeetFree, which otherwise only take herds without an errand).
        bool NeedsYields(Herd herd) => herd.errand == Errand.Graze;

        void YieldNeeds(Herd herd)
        {
            if (NeedsYields(herd)) CancelErrand(herd);
        }

        // Booked every NeedTick: what the herd ate (hunger) and what it took from the ground (the pasture).
        void Feed(Herd herd, float t)
        {
            var s = herd.spec;
            if (s.diet == Diet.Fish)
            {
                if (herd.errandStage == 1 && FishErrand(herd.errand)) herd.hunger = Mathf.Max(0f, herd.hunger - fishFeedRate * t);
                return;
            }
            bool bout = herd.errand == Errand.Graze || (herd.errand == Errand.Browse && herd.errandStage == 1);
            bool idle = herd.wait > 0f && !herd.walking && (herd.errand == Errand.None || herd.errand == Errand.Spread);
            if (!bout && !idle) return;
            // A bout starts on grazeMinFood and goes on down to half of it, so a poor patch still gives a bout to see.
            if (Food(herd.center, s.diet) < (bout ? grazeMinFood * 0.5f : grazeMinFood))
            {
                // Eaten bare: the bout ends and the herd walks on.
                if (herd.errand == Errand.Graze) herd.wait = Mathf.Min(herd.wait, 1f);
                return;
            }
            herd.starving = false;
            float rate = bout ? boutFeedRate : passiveFeedRate;
            herd.hunger = Mathf.Max(0f, herd.hunger - rate * t);
            float bite = grazeBite * (boutFeedRate > 0f ? rate / boutFeedRate : 1f) * herd.members.Count * t;
            Bite(herd.center, s.diet, bite * 0.5f);
            int n = herd.members.Count;
            herd.biteAt = (herd.biteAt + 1) % n;
            Bite(herd.members[herd.biteAt].pos, s.diet, bite * 0.25f);
            Bite(herd.members[(herd.biteAt + n / 2) % n].pos, s.diet, bite * 0.25f);
            if (herd.errand == Errand.Graze && herd.hunger <= 0.02f) herd.wait = Mathf.Min(herd.wait, NeedRand(2f, 4f));
        }

        // ---------------------------------------------------------- shade and shelter

        bool WeatherThink(Herd herd)
        {
            // Sheep have their own shade errand (StartShade) for noon and rain; this is the rule for everyone else.
            if (herd.spec.kind == LifeKind.Sheep) return false;
            if (Wet(herd)) return NeedRand() < shelterChance * needsRate && StartShelter(herd, HerdGoal.Shelter);
            if (!Noon || herd.noonShaded) return false;
            float heat = Heat(herd);
            if (heat <= 0f || NeedRand() >= noonShadeChance * heat * needsRate) return false;
            if (ShadeHere(herd.center) >= 0.3f) return false;
            return StartShelter(herd, HerdGoal.Shade);
        }

        bool StartShelter(Herd herd, HerdGoal goal)
        {
            if (!FindShelter(herd, out Vector2 spot) || !Reachable(herd, ref spot))
            {
                herd.needCool = NeedRand(10f, 20f);
                return false;
            }
            herd.errand = Errand.Shelter;
            herd.errandStage = 0;
            herd.target = spot;
            herd.wait = 0f;
            herd.needErrand = Errand.Shelter;
            herd.needGoal = goal;
            ShowGoal(herd, goal);
            if (goal == HerdGoal.Shade)
            {
                herd.noonShaded = true;
                ShadeTrips++;
            }
            else ShelterTrips++;
            ErrandsStarted++;
            Moment(goal == HerdGoal.Shelter ? MomentKind.Shelter : MomentKind.Errand, herd);
            return true;
        }

        // Under the tree: about half lie down, the rest stand about in the shade.
        void ArriveShelter(Herd herd)
        {
            var s = herd.spec;
            herd.wait = NeedRand(shelterStay);
            var act = herd.needGoal == HerdGoal.Shade ? AnimalActivity.Shade : AnimalActivity.Shelter;
            foreach (var a in herd.members)
            {
                if (a.hidden || a.hasGoal || (a.act != AnimalActivity.None && a.act != AnimalActivity.Line)) continue;
                a.act = act;
                if (IsAwake(a) && NeedRand() < 0.5f) SetState(a, AnimalState.Rest, NeedRand(s.restMin, s.restMax), RestPitch, false);
            }
            _meshDirty = true;
        }

        // ---------------------------------------------------------- storm scatter

        // True on the step a gust arrives: the first one 1-3 s after the storm passed scatterStorm, then scatterGap apart.
        bool Gust(float dt, bool far)
        {
            if (scatterStorm <= 0f || needsRate <= 0f || behaviourRate <= 0f || far || Agitation < scatterStorm || _night > sleepThreshold)
            {
                _gustT = -1f;
                return false;
            }
            if (_gustT < 0f)
            {
                _gustT = NeedRand(1f, 3f);
                return false;
            }
            _gustT -= dt;
            if (_gustT > 0f) return false;
            _gustT = NeedRand(scatterGap);
            return true;
        }

        void StartScatter(Herd herd)
        {
            var s = herd.spec;
            int n = herd.members.Count;
            if (n < 3 || herd.fleeTimer > 0f || herd.fleeing || herd.refuging || herd.diving || herd.meeting != null) return;
            if (herd.goal != HerdGoal.None && herd.goal != herd.needGoalSet) return;
            // Hares bolt for their burrow in a storm (StartDive) instead.
            if (s.hops && TryNearestBurrow(herd.center, burrowReach, out _)) return;
            foreach (var a in herd.members) if (a.hidden || a.dived || a.state == AnimalState.Sleep) return;
            float body = s.body * animalScale;
            float run = Mathf.Min(s.speed * (s.hops ? s.burstSpeed * 0.6f : 1.7f), 0.6f);
            int placed = 0;
            foreach (var a in herd.members)
            {
                if (a.hasGoal) continue;
                Vector2 rel = a.pos - herd.center;
                Vector2 dir = rel.sqrMagnitude > 1e-6f ? rel.normalized : new Vector2(Mathf.Cos(NeedRand(0f, 6.28f)), Mathf.Sin(NeedRand(0f, 6.28f)));
                dir = Rotate(dir, NeedRand(-0.5f, 0.5f));
                float dist = Mathf.Max(body * 2f, run * NeedRand(1.5f, 2.5f));
                for (int k = 0; k < 3; k++)
                {
                    Vector2 g = a.pos + dir * (dist * (1f - 0.3f * k));
                    if (!OkSpot(s, g)) continue;
                    SetGoal(a, g, AnimalActivity.Flee);
                    placed++;
                    break;
                }
            }
            if (placed < 2)
            {
                foreach (var a in herd.members) if (a.act == AnimalActivity.Flee && a.hasGoal) ClearGoal(a);
                return;
            }
            herd.scatterT = NeedRand(scatterTime);
            ShowGoal(herd, HerdGoal.Scatter);
            Scatters++;
            _meshDirty = true;
            Moment(MomentKind.Errand, herd);
        }

        void StepScatter(Herd herd, float dt)
        {
            herd.scatterT -= dt;
            bool calm = herd.fleeTimer <= 0f && !herd.fleeing && !herd.refuging && _night <= sleepThreshold;
            if (herd.scatterT > 0f && calm) return;
            herd.scatterT = 0f;
            foreach (var a in herd.members) if (a.act == AnimalActivity.Flee && a.hasGoal) ClearGoal(a);
            _meshDirty = true;
        }
    }
}
