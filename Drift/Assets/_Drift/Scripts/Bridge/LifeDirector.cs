using System;
using Drift.Core;
using Drift.Islands;
using Drift.Life;
using Drift.SaveSystem;
using Drift.Visuals;
using UnityEngine;

namespace Drift.Bridge
{
    // What the LifeDirector can start. Values index the pacer's weight / cooldown arrays.
    public enum LifeNudge
    {
        None = -1,
        HerdSignature, HerdPlay, HerdSpecies, HerdWander, Flock, Critter, Fireflies, Fish, Dolphins, Whale, SeaShow,
        SleepStir,
        // Appended 2026-09-24: two herds near each other meet (IslandHerdSystem.TryStartMeeting).
        HerdMeeting,
    }

    // Pure pacing of the cozy "something every 10 s" rhythm: remembers when the last noticed moment happened, asks
    // for a nudge once nothing was noticed for a jittered 5.5-7.5 s, and orders the kinds to try by weight, never
    // the one that ran last and none still cooling down. Only once the gap gets long (lastResortGap) a repeat and
    // a shorter cooldown are allowed, so the rhythm holds even when a single kind is all there is to see.
    public sealed class LifePacer
    {
        public const int Count = 13;

        public float nudgeMin = 5f;
        public float nudgeMax = 6.8f;
        public float retrySeconds = 1f;
        public float graceSeconds = 2.5f;
        public float lastResortGap = 7.5f;
        public float lastResortCooldown = 4f;
        public readonly float[] weights = new float[Count];
        public readonly float[] cooldowns = new float[Count];

        System.Random _rnd;
        float _clock, _lastNoticed, _due, _nextTry;
        readonly float[] _lastAt = new float[Count];
        readonly float[] _w = new float[Count];

        public float Clock => _clock;
        public float SinceNoticed => _clock - _lastNoticed;
        public float Due => _due;
        public float NextTry => _nextTry;
        public bool LastResort => SinceNoticed >= lastResortGap;
        public LifeNudge Last { get; private set; }
        public int Fired { get; private set; }
        public int Failed { get; private set; }
        public int NoticedCount { get; private set; }
        public float LastGap { get; private set; }
        public float LongestGap { get; private set; }

        public LifePacer(int seed)
        {
            float[] w = { 3f, 2.5f, 2f, 0.7f, 1.5f, 1f, 2f, 1f, 1.5f, 1.5f, 2f, 1.5f, 2.5f };
            float[] c = { 20f, 12f, 25f, 15f, 20f, 8f, 16f, 5f, 12f, 30f, 20f, 8f, 20f };
            Array.Copy(w, weights, Count);
            Array.Copy(c, cooldowns, Count);
            Reset(seed);
        }

        public void Reset(int seed)
        {
            _rnd = new System.Random(seed);
            _clock = 0f;
            _lastNoticed = 0f;
            _nextTry = 0f;
            Last = LifeNudge.None;
            Fired = Failed = NoticedCount = 0;
            LastGap = LongestGap = 0f;
            for (int i = 0; i < Count; i++) _lastAt[i] = -1e9f;
            _due = NextDue();
        }

        float NextDue() => Mathf.Lerp(nudgeMin, Mathf.Max(nudgeMin, nudgeMax), (float)_rnd.NextDouble());

        // Starts a fresh wait without counting a gap (the game was paused, a menu covered the view).
        public void Rearm()
        {
            _lastNoticed = _clock;
            _nextTry = _clock;
            _due = NextDue();
        }

        // Something was noticed near the camera: natural or nudged, it restarts the wait.
        public void Notice()
        {
            LastGap = SinceNoticed;
            if (LastGap > LongestGap) LongestGap = LastGap;
            NoticedCount++;
            _lastNoticed = _clock;
            _due = NextDue();
        }

        // Advances the clock; true when a nudge should be tried now.
        public bool Tick(float dt)
        {
            _clock += Mathf.Max(0f, dt);
            return SinceNoticed >= _due && _clock >= _nextTry;
        }

        public float SecondsSince(LifeNudge k) => k == LifeNudge.None ? float.MaxValue : _clock - _lastAt[(int)k];

        public bool Available(LifeNudge k, bool lastResort)
        {
            if (k == LifeNudge.None) return false;
            int i = (int)k;
            if (weights[i] <= 0f) return false;
            float since = _clock - _lastAt[i];
            if (lastResort) return since >= Mathf.Min(cooldowns[i], lastResortCooldown);
            return k != Last && since >= cooldowns[i];
        }

        // Fills `buffer` with the kinds to try, in weighted random order without repeats; returns how many.
        public int Order(LifeNudge[] buffer)
        {
            bool lastResort = LastResort;
            float total = 0f;
            for (int k = 0; k < Count; k++)
            {
                _w[k] = Available((LifeNudge)k, lastResort) ? weights[k] : 0f;
                total += _w[k];
            }
            int n = 0;
            while (total > 1e-5f && n < buffer.Length)
            {
                float r = (float)_rnd.NextDouble() * total;
                int pick = -1;
                for (int k = 0; k < Count; k++)
                {
                    if (_w[k] <= 0f) continue;
                    pick = k;
                    r -= _w[k];
                    if (r < 0f) break;
                }
                if (pick < 0) break;
                buffer[n++] = (LifeNudge)pick;
                total -= _w[pick];
                _w[pick] = 0f;
            }
            return n;
        }

        // A nudge really started: remember it and give its report a moment to arrive before trying again.
        public void Confirm(LifeNudge k)
        {
            if (k == LifeNudge.None) return;
            Last = k;
            _lastAt[(int)k] = _clock;
            Fired++;
            _nextTry = _clock + Mathf.Max(0f, graceSeconds);
        }

        // Nothing fitted into the view: look again shortly.
        public void Fail()
        {
            Failed++;
            _nextTry = _clock + Mathf.Max(0.05f, retrySeconds);
        }
    }

    // How often herd moments may become a line in the news chip: a gap after any of them and a longer cooldown per
    // goal, so a migrating herd is news and not a ticker. Pure so it can be tested.
    public sealed class MomentToastGate
    {
        public float gapSeconds = 40f;
        public float cooldownSeconds = 150f;

        static readonly int GoalCount = Enum.GetValues(typeof(HerdGoal)).Length;
        readonly float[] _lastAt = new float[GoalCount];
        float _last;

        public int Shown { get; private set; }

        public MomentToastGate() => Reset();

        public void Reset()
        {
            _last = -1e9f;
            for (int i = 0; i < _lastAt.Length; i++) _lastAt[i] = -1e9f;
            Shown = 0;
        }

        public bool Allow(HerdGoal goal, float clock)
        {
            int i = (int)goal;
            if (goal == HerdGoal.None || i < 0 || i >= _lastAt.Length) return false;
            return clock - _last >= gapSeconds && clock - _lastAt[i] >= cooldownSeconds;
        }

        public void Note(HerdGoal goal, float clock)
        {
            int i = (int)goal;
            if (i < 0 || i >= _lastAt.Length) return;
            _last = clock;
            _lastAt[i] = clock;
            Shown++;
        }
    }

    // The cozy rhythm keeper: listens to everything a watcher would notice (Moments, world events, encounters,
    // milestones, merges) with the playtest recorder's rule - on screen and at least 20 px tall - and, when the
    // view has been quiet for a jittered few seconds, starts something in front of the camera: a herd's signature
    // move, a game or its species' routine (the followed herd first while watching), a murmur or a dive of a flock,
    // a critter's move, a firefly wave by night, a fish jump, dolphins, a whale coming up, or a sea event from
    // SeaLifeSystem.TryShowNear. Cozy mode only, while playing (also while watching), never in the Pangäa finale.
    public class LifeDirector : MonoBehaviour
    {
        public GameSession session;
        public WatchTools watch;
        public PangaeaFinale finale;
        public FlockSystem flocks;
        public FishSystem fish;
        public SeaLifeSystem seaLife;
        public int seed = 53;

        [Header("Takt")]
        [Tooltip("Frühester Anstoß (s): so lange darf es in der Nähe der Kamera ruhig bleiben, bevor der Regisseur etwas beginnen lässt.")]
        [Range(2f, 15f)] public float nudgeMin = 5f;
        [Tooltip("Spätester Anstoß (s). Dazwischen wird zufällig gewählt, damit es nicht wie ein Metronom wirkt.")]
        [Range(2f, 15f)] public float nudgeMax = 6.8f;
        [Tooltip("Passt gerade nichts ins Bild, wird nach so vielen Sekunden erneut gesucht.")]
        [Range(0.2f, 5f)] public float retrySeconds = 1f;
        [Tooltip("Nach einem Anstoß so lange (s) warten, bis das Ereignis gemeldet wurde, bevor ein weiterer versucht wird.")]
        [Range(0.5f, 6f)] public float graceSeconds = 2.5f;
        [Tooltip("Ab dieser Lücke (s) darf auch dieselbe Art wie zuletzt noch einmal angestoßen werden (mit kurzer Abklingzeit).")]
        [Range(4f, 15f)] public float lastResortGap = 7.5f;
        [Tooltip("Kürzeste Abklingzeit (s) einer Art, wenn die Lücke schon lang ist.")]
        [Range(1f, 15f)] public float lastResortCooldown = 4f;

        [Header("Sichtbarkeit")]
        [Tooltip("So groß (px) muss ein Ereignis auf dem Bildschirm sein, damit es als bemerkt zählt (Regel des Spieltest-Rekorders).")]
        [Range(5f, 60f)] public float noticePx = Moments.NoticePx;
        [Tooltip("So groß (px) muss ein Tier oder Schwarm sein, damit der Regisseur es anstößt (etwas Reserve, weil es sich bewegt).")]
        [Range(5f, 80f)] public float pickPx = 26f;
        [Tooltip("So weit (Anteil der Bildbreite/-höhe) muss ein angestoßenes Tier vom Bildrand entfernt sein.")]
        [Range(0f, 0.3f)] public float pickMargin = 0.1f;
        [Tooltip("Beim Beobachten einer Herde werden die Herden-Anstöße so viel stärker gewichtet.")]
        [Range(1f, 5f)] public float watchHerdBoost = 2f;

        [Header("Weite Sicht")]
        [Tooltip("Unter so vielen Pixeln pro Einheit in der Bildmitte gilt die Sicht als weit (riesige Insel, Standard-Zoom): dann werden nur große Ereignisse angestoßen – Vogelschwärme, Wale, Delfine, Flugfische, Glühwürmchen-Wellen.")]
        [Range(5f, 60f)] public float farViewPx = 18f;
        [Tooltip("In der weiten Sicht genügt diese Größe (px) zum Anstoßen: die Entfernung zum Tier ändert sich dort kaum.")]
        [Range(5f, 80f)] public float farPickPx = 22f;
        [Tooltip("In der weiten Sicht werden die großen Ereignisse so viel stärker gewichtet.")]
        [Range(1f, 5f)] public float farBigBoost = 2f;
        [Tooltip("In der weiten Sicht ruft der Regisseur tagsüber höchstens so oft (s) einen Singvogelschwarm zur Insel im Bild, wenn keiner da ist.")]
        [Range(5f, 60f)] public float flockCallInterval = 15f;
        [Tooltip("Abklingzeit (s) der Glühwürmchen-Wellen in der weiten Sicht (dort gibt es nachts wenig anderes Großes).")]
        [Range(0f, 60f)] public float farFireflyCooldown = 10f;

        [Header("Nacht")]
        [Tooltip("Ab dieser Dunkelheit (LifeEnvironment.NightAmount) gilt es als Nacht: Herden schlafen und werden nie für eine Vorstellung geweckt, Singvögel und Krabben ruhen.")]
        [Range(0.2f, 0.9f)] public float nightFrom = 0.5f;
        [Tooltip("Nachts werden Glühwürmchen-Wellen so viel stärker gewichtet.")]
        [Range(0f, 5f)] public float nightFireflyBoost = 2f;
        [Tooltip("Nachts werden Fische, Delfine, Wale und Meeres-Ereignisse so viel stärker gewichtet.")]
        [Range(0f, 5f)] public float nightSeaBoost = 1.5f;

        [Header("Meldungen")]
        [Tooltip("Tut eine Herde im Bild etwas Besonderes (zieht zur neuen Weide, sucht Schutz vor dem Regen, kuschelt gegen die Kälte, alle treffen sich am Wasser …), sagt eine kurze Meldung, was los ist. Tippen darauf beobachtet die Herde.")]
        public bool momentToasts = true;
        [Tooltip("Mindestabstand (s) zwischen zwei solchen Meldungen.")]
        [Range(5f, 300f)] public float momentToastGap = 40f;
        [Tooltip("So lange (s) kommt dieselbe Meldung (z. B. \"zieht zur neuen Weide\") nicht noch einmal.")]
        [Range(10f, 900f)] public float momentToastCooldown = 150f;
        [Tooltip("So groß (px) muss die Herde auf dem Bildschirm sein, damit ihr Ereignis gemeldet wird.")]
        [Range(5f, 60f)] public float momentToastPx = 14f;

        [Header("Verweise")]
        [Tooltip("So oft (s) werden fehlende Verweise (Kamera, Systeme) neu gesucht.")]
        [Range(0.5f, 10f)] public float resolveInterval = 2f;

        [Header("Gewichte")]
        [Tooltip("Die eigene Bewegung der Art (Haken schlagen, Karussell, Halskampf …).")]
        [Range(0f, 5f)] public float signatureWeight = 3f;
        [Tooltip("Ein Spiel der Herde: Fangen, Wettrennen der Jungen, Kräftemessen.")]
        [Range(0f, 5f)] public float playWeight = 2.5f;
        [Tooltip("Die Gewohnheit der Art: Bauchrutschen, Parade, Bad, Blätter zupfen, Galopp, Wache, Mäusesprung.")]
        [Range(0f, 5f)] public float speciesWeight = 2f;
        [Tooltip("Bummeln, Nachbarn besuchen, zum Grasen ausschwärmen.")]
        [Range(0f, 5f)] public float wanderWeight = 0.7f;
        [Tooltip("Ein Vogelschwarm tanzt (Singvögel) oder stößt ins Wasser (Seevögel).")]
        [Range(0f, 5f)] public float flockWeight = 1.5f;
        [Tooltip("Krabbe winkt, Schildkröte geht zum Nest, Schmetterlinge tanzen.")]
        [Range(0f, 5f)] public float critterWeight = 1f;
        [Tooltip("Nachts: eine Welle durch die Glühwürmchen.")]
        [Range(0f, 5f)] public float fireflyWeight = 2f;
        [Tooltip("Ein Fisch springt aus einem Schwarm.")]
        [Range(0f, 5f)] public float fishWeight = 1f;
        [Tooltip("Delfine springen.")]
        [Range(0f, 5f)] public float dolphinWeight = 1.5f;
        [Tooltip("Ein Wal taucht zum Atmen auf.")]
        [Range(0f, 5f)] public float whaleWeight = 1.5f;
        [Tooltip("Ein Ereignis im Meer aus SeaLifeSystem.TryShowNear.")]
        [Range(0f, 5f)] public float seaShowWeight = 2f;
        [Tooltip("Nachts: ein schlafendes Tier hebt kurz den Kopf und schläft weiter (die Herde wird nicht geweckt).")]
        [Range(0f, 5f)] public float sleepStirWeight = 1.5f;
        [Tooltip("Zwei nahe Herden begegnen sich: Begrüßen, Fangenspiel, Kräftemessen, gemeinsamer Zug, Kreistanz.")]
        [Range(0f, 5f)] public float meetingWeight = 2.5f;

        [Header("Abklingzeiten (s)")]
        [Range(0f, 120f)] public float signatureCooldown = 20f;
        [Range(0f, 120f)] public float playCooldown = 12f;
        [Range(0f, 120f)] public float speciesCooldown = 25f;
        [Range(0f, 120f)] public float wanderCooldown = 15f;
        [Range(0f, 120f)] public float flockCooldown = 20f;
        [Range(0f, 120f)] public float critterCooldown = 8f;
        [Range(0f, 120f)] public float fireflyCooldown = 16f;
        [Range(0f, 120f)] public float fishCooldown = 5f;
        [Range(0f, 120f)] public float dolphinCooldown = 12f;
        [Range(0f, 120f)] public float whaleCooldown = 30f;
        [Range(0f, 120f)] public float seaShowCooldown = 20f;
        [Range(0f, 120f)] public float sleepStirCooldown = 8f;
        [Range(0f, 120f)] public float meetingCooldown = 20f;

        // The playtest recorder's subject size for an encounter.
        const float EncounterSize = 4f;
        // A herd's size on screen for the moment-toast rule, and how far from the moment's point its herd may stand.
        const float HerdToastSize = 1.5f, HerdToastReach = 8f;
        static readonly AnimalActivity[] WanderMoves = { AnimalActivity.Stroll, AnimalActivity.Visit, AnimalActivity.Spread };

        LifePacer _pacer;
        System.Random _rng;
        readonly LifeNudge[] _order = new LifeNudge[LifePacer.Count];
        Camera _cam;
        float _resolveT;
        bool _active;
        int _momentSerial;
        Vector3 _camPos;
        float _pxAtOne;
        float _pxAtView = 100f;
        bool _far;
        float _viewT, _callWait;
        Vector2 _viewCenter;
        float _viewRadius;
        bool _hasView;
        readonly MomentToastGate _toastGate = new();
        float _toastClock;

        public LifePacer Pacer => _pacer;
        public bool Running => _active;
        public LifeNudge LastNudge { get; private set; } = LifeNudge.None;
        public int Nudges { get; private set; }
        public float SinceNoticed => _pacer != null ? _pacer.SinceNoticed : 0f;
        public bool Night => LifeEnvironment.NightAmount >= nightFrom;
        // Pixels per unit where the view's middle meets the water, and whether that counts as the far view.
        public float PxAtView => _pxAtView;
        public bool FarView => _far;
        public int FlocksCalled { get; private set; }
        public MomentToastGate ToastGate => _toastGate;
        public string LastMomentToast { get; private set; } = "";
        float PickPx => _far ? Mathf.Min(pickPx, farPickPx) : pickPx;

        void OnEnable()
        {
            EnsurePacer();
            _resolveT = 0f;
            _active = false;
            Moments.Seen += OnMoment;
            WorldEvents.Started += OnWorldEvent;
            Encounters.Started += OnEncounter;
            Milestones.Celebrated += OnMilestone;
            Island.Merged += OnMerged;
        }

        void OnDisable()
        {
            Moments.Seen -= OnMoment;
            WorldEvents.Started -= OnWorldEvent;
            Encounters.Started -= OnEncounter;
            Milestones.Celebrated -= OnMilestone;
            Island.Merged -= OnMerged;
            _active = false;
        }

        void Resolve()
        {
            if (session == null) session = FindAnyObjectByType<GameSession>();
            if (watch == null) watch = FindAnyObjectByType<WatchTools>();
            if (finale == null) finale = FindAnyObjectByType<PangaeaFinale>();
            if (flocks == null) flocks = FindAnyObjectByType<FlockSystem>();
            if (fish == null) fish = FindAnyObjectByType<FishSystem>();
            if (seaLife == null) seaLife = FindAnyObjectByType<SeaLifeSystem>();
            if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
        }

        void EnsurePacer()
        {
            if (_pacer == null)
            {
                int s = seed * 7919 + Environment.TickCount;
                _pacer = new LifePacer(s);
                _rng = new System.Random(s ^ 0x5bd1e995);
            }
            _pacer.nudgeMin = nudgeMin;
            _pacer.nudgeMax = Mathf.Max(nudgeMin, nudgeMax);
            _pacer.retrySeconds = retrySeconds;
            _pacer.graceSeconds = graceSeconds;
            _pacer.lastResortGap = lastResortGap;
            _pacer.lastResortCooldown = lastResortCooldown;
            bool herdWatch = watch != null && watch.Watched != null && watch.Watched.herds != null;
            bool night = Night;
            float fireflyCd = _far ? Mathf.Min(fireflyCooldown, farFireflyCooldown) : fireflyCooldown;
            Set(LifeNudge.HerdSignature, signatureWeight, signatureCooldown, night, herdWatch);
            Set(LifeNudge.HerdPlay, playWeight, playCooldown, night, herdWatch);
            Set(LifeNudge.HerdSpecies, speciesWeight, speciesCooldown, night, herdWatch);
            Set(LifeNudge.HerdWander, wanderWeight, wanderCooldown, night, herdWatch);
            Set(LifeNudge.Flock, flockWeight, flockCooldown, night, herdWatch);
            Set(LifeNudge.Critter, critterWeight, critterCooldown, night, herdWatch);
            Set(LifeNudge.Fireflies, fireflyWeight, fireflyCd, night, herdWatch);
            Set(LifeNudge.Fish, fishWeight, fishCooldown, night, herdWatch);
            Set(LifeNudge.Dolphins, dolphinWeight, dolphinCooldown, night, herdWatch);
            Set(LifeNudge.Whale, whaleWeight, whaleCooldown, night, herdWatch);
            Set(LifeNudge.SeaShow, seaShowWeight, seaShowCooldown, night, herdWatch);
            Set(LifeNudge.SleepStir, sleepStirWeight, sleepStirCooldown, night, herdWatch);
            Set(LifeNudge.HerdMeeting, meetingWeight, meetingCooldown, night, herdWatch);
        }

        void Set(LifeNudge k, float weight, float cooldown, bool night, bool herdWatch)
        {
            _pacer.weights[(int)k] = weight * WeightFactor(k, night, herdWatch, watchHerdBoost, nightFireflyBoost, nightSeaBoost, _far, farBigBoost);
            _pacer.cooldowns[(int)k] = cooldown;
        }

        public static bool IsHerdNudge(LifeNudge k) =>
            k == LifeNudge.HerdSignature || k == LifeNudge.HerdPlay || k == LifeNudge.HerdSpecies || k == LifeNudge.HerdWander
            || k == LifeNudge.HerdMeeting;

        // By night the herds sleep (they are never woken for a show), crabs and turtles rest: what is awake or glows
        // gets the weight instead - fireflies, the sea and a sleeper stirring. Watching a herd favours its moves.
        public static float WeightFactor(LifeNudge k, bool night, bool herdWatch, float watchBoost, float fireflyBoost, float seaBoost)
        {
            if (IsHerdNudge(k)) return night ? 0f : herdWatch ? watchBoost : 1f;
            switch (k)
            {
                case LifeNudge.Critter: return night ? 0f : 1f;
                case LifeNudge.SleepStir: return night ? herdWatch ? watchBoost : 1f : 0f;
                case LifeNudge.Fireflies: return night ? fireflyBoost : 0f;
                case LifeNudge.Fish:
                case LifeNudge.Dolphins:
                case LifeNudge.Whale:
                case LifeNudge.SeaShow: return night ? seaBoost : 1f;
                default: return 1f;
            }
        }

        // The far view (a huge island at the default zoom, ~12 px per unit): animals, critters, single fish and
        // sleepers are a few pixels there, so only the big subjects stay in play, boosted.
        public static float WeightFactor(LifeNudge k, bool night, bool herdWatch, float watchBoost, float fireflyBoost, float seaBoost, bool far, float farBoost)
        {
            float f = WeightFactor(k, night, herdWatch, watchBoost, fireflyBoost, seaBoost);
            if (!far) return f;
            return IsBigNudge(k) ? f * farBoost : 0f;
        }

        public static bool IsBigNudge(LifeNudge k) =>
            k == LifeNudge.Flock || k == LifeNudge.Whale || k == LifeNudge.Dolphins || k == LifeNudge.SeaShow || k == LifeNudge.Fireflies;

        // The smallest subject (u) that still reaches `px` at `pxPerUnit`: what TryShowNear may start.
        public static float MinSubjectSize(float px, float pxPerUnit) => px / Mathf.Max(0.01f, pxPerUnit);

        void Update()
        {
            if (!Application.isPlaying) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            _toastClock += Mathf.Max(0f, dt);
            _resolveT -= dt;
            if (_resolveT <= 0f)
            {
                _resolveT = resolveInterval;
                Resolve();
            }
            if (_pacer == null) EnsurePacer();
            if (!Allowed())
            {
                _active = false;
                return;
            }
            if (!_active)
            {
                _active = true;
                _pacer.Rearm();
            }
            _callWait -= dt;
            _viewT -= dt;
            if (_viewT <= 0f)
            {
                _viewT = 2f;
                PrepareView();
                if (_far && !Night) CallFlock();
            }
            if (_pacer.Tick(dt)) Attempt();
        }

        bool Allowed()
        {
            if (GameModes.IsAdventure || session == null || _cam == null) return false;
            // The Pangäa's free look is watching too; only the finale flight and the fly-over pause the director.
            if (session.Current != GameSession.State.Playing) return false;
            if (finale != null && (finale.Active || finale.FlyingOver)) return false;
            if (watch != null && (watch.Capturing || watch.JournalOpen || watch.AlbumOpen)) return false;
            return true;
        }

        // ------------------------------------------------------------------ noticed

        bool Noticed(Vector3 world, float size) => Moments.Noticed(_cam, world, size, 0.02f, noticePx);

        void OnMoment(MomentKind kind, Vector3 world)
        {
            _momentSerial++;
            if (_active && Noticed(world, Moments.SubjectSize(kind))) _pacer.Notice();
            if (_active) TryMomentToast(kind, world);
        }

        // ------------------------------------------------------------------ moment toasts

        // "Sichtbarkeit": a herd that follows one of the environment rules near the camera says so in the news chip.
        // Returns whether a line was shown (public so tests and eval take the same route as a reported moment).
        public bool TryMomentToast(MomentKind kind, Vector3 world)
        {
            var goal = ToastGoalOf(kind);
            if (!momentToasts || watch == null || goal == HerdGoal.None) return false;
            _toastGate.gapSeconds = momentToastGap;
            _toastGate.cooldownSeconds = momentToastCooldown;
            if (!_toastGate.Allow(goal, _toastClock)) return false;
            if (!Moments.Noticed(_cam, world, HerdToastSize, 0.02f, momentToastPx)) return false;
            string plural = null, other = null;
            WatchSubject subject = null;
            if (FindHerdAt(world, goal, HerdToastReach, out var island, out var herds, out int herd, out int mate))
            {
                plural = LifeNames.Plural(herds.HerdKind(herd));
                if (mate >= 0) other = LifeNames.Plural(herds.HerdKind(mate));
                var watched = watch.Watched;
                // Already watching that herd: the line explains what is on screen, there is nowhere to fly.
                if (watched == null || watched.herds != herds || watched.herd != herd) subject = WatchSubjects.OfHerd(island, herds, herd);
            }
            string text = MomentToastText(goal, plural, other);
            if (!watch.ShowMoment(text, subject)) return false;
            _toastGate.Note(goal, _toastClock);
            LastMomentToast = text;
            return true;
        }

        // The herd a moment at `world` is about: the nearest herd middle within `reach` on a loaded island, one that
        // has the moment's goal counting as twice as close. `mate` = the nearest herd of another kind on the same
        // island within twice the reach (who a mixed herd mingles with), -1 for none.
        public static bool FindHerdAt(Vector3 world, HerdGoal goal, float reach, out Island island, out IslandHerdSystem herds, out int herd, out int mate)
        {
            island = null;
            herds = null;
            herd = mate = -1;
            float best = float.MaxValue;
            var p = new Vector2(world.x, world.z);
            var all = Island.All;
            for (int k = 0; k < all.Count; k++)
            {
                var isl = all[k];
                if (isl == null || !isl.isActiveAndEnabled || isl.IsSunk) continue;
                if (!isl.TryGetComponent(out IslandHerdSystem hs) || !hs.enabled) continue;
                for (int h = 0; h < hs.HerdCount; h++)
                {
                    if (hs.HerdSize(h) == 0) continue;
                    float d = (HerdPlanar(isl, hs, h) - p).magnitude;
                    if (d > reach) continue;
                    if (goal != HerdGoal.None && hs.GoalOf(h) == goal) d *= 0.5f;
                    if (d >= best) continue;
                    best = d;
                    island = isl;
                    herds = hs;
                    herd = h;
                }
            }
            if (herds == null) return false;
            var kind = herds.HerdKind(herd);
            var at = HerdPlanar(island, herds, herd);
            float bestMate = float.MaxValue;
            for (int h = 0; h < herds.HerdCount; h++)
            {
                if (h == herd || herds.HerdSize(h) == 0 || herds.HerdKind(h) == kind) continue;
                float d = (HerdPlanar(island, herds, h) - at).magnitude;
                if (d > 2f * reach) continue;
                if (goal != HerdGoal.None && herds.GoalOf(h) == goal) d *= 0.5f;
                if (d >= bestMate) continue;
                bestMate = d;
                mate = h;
            }
            return true;
        }

        static Vector2 HerdPlanar(Island island, IslandHerdSystem herds, int herd)
        {
            var c = herds.HerdCenter(herd);
            var w = island.transform.TransformPoint(new Vector3(c.x, 0f, c.y));
            return new Vector2(w.x, w.z);
        }

        // MomentKind -> the goal a toast speaks about, matched by name (MomentKind.Migrate -> HerdGoal.Migrate), so
        // the mapping holds whatever order the moment kinds are appended in.
        // TODO(v0.6.8 integration): once MomentKind has Migrate, Gather, Mingle, Huddle, Court and Shelter this
        // can become a plain switch on the enum.
        static HerdGoal[] _momentGoals;

        public static HerdGoal ToastGoalOf(MomentKind kind)
        {
            if (_momentGoals == null)
            {
                var values = (MomentKind[])Enum.GetValues(typeof(MomentKind));
                int max = 0;
                foreach (var v in values) max = Mathf.Max(max, (int)v);
                var map = new HerdGoal[max + 1];
                foreach (var v in values) if ((int)v >= 0) map[(int)v] = ToastGoalOfName(v.ToString());
                _momentGoals = map;
            }
            int i = (int)kind;
            return i >= 0 && i < _momentGoals.Length ? _momentGoals[i] : HerdGoal.None;
        }

        // The moment kinds (by enum name) that become a toast.
        public static HerdGoal ToastGoalOfName(string momentName)
        {
            switch (momentName)
            {
                case "Migrate": return HerdGoal.Migrate;
                case "Gather": return HerdGoal.Gather;
                case "Mingle": return HerdGoal.Mingle;
                case "Huddle": return HerdGoal.Huddle;
                case "Court": return HerdGoal.Court;
                case "Shelter": return HerdGoal.Shelter;
                default: return HerdGoal.None;
            }
        }

        // The toast line for a goal; `plural` / `otherPlural` are the species names ("Zebras"), null when unknown -
        // the line then speaks of "die Herde", it is never empty for a toast goal. "" for any other goal.
        public static string MomentToastText(HerdGoal goal, string plural, string otherPlural)
        {
            bool known = !string.IsNullOrEmpty(plural);
            switch (goal)
            {
                case HerdGoal.Migrate: return known ? "Die " + plural + " ziehen zur neuen Weide" : "Eine Herde zieht zur neuen Weide";
                case HerdGoal.Gather: return "Alle treffen sich am Wasser";
                case HerdGoal.Mingle:
                    return known && !string.IsNullOrEmpty(otherPlural) && otherPlural != plural
                        ? plural + " und " + otherPlural + " ziehen zusammen"
                        : "Zwei Herden ziehen zusammen";
                case HerdGoal.Huddle: return known ? "Die " + plural + " kuscheln gegen die Kälte" : "Die Herde kuschelt gegen die Kälte";
                case HerdGoal.Court: return known ? "Paarungszeit bei den " + DativePlural(plural) : "Paarungszeit auf der Insel";
                case HerdGoal.Shelter: return known ? "Die " + plural + " suchen Schutz vor dem Regen" : "Die Herde sucht Schutz vor dem Regen";
                default: return "";
            }
        }

        // "bei den Schafen", "bei den Hasen", "bei den Zebras": the dative plural adds an -n unless it ends in -n or -s.
        public static string DativePlural(string plural)
        {
            if (string.IsNullOrEmpty(plural)) return "";
            char last = plural[plural.Length - 1];
            return last == 'n' || last == 's' ? plural : plural + "n";
        }

        void OnWorldEvent(WorldEventKind kind, Vector3 at)
        {
            if (_active) _pacer.Notice();
        }

        void OnEncounter(EncounterKind kind, Vector3 world)
        {
            if (_active && Noticed(world, EncounterSize)) _pacer.Notice();
        }

        void OnMilestone(Milestone m)
        {
            if (_active) _pacer.Notice();
        }

        void OnMerged(Island host, Island guest, float speed)
        {
            if (_active && session != null && host == session.player) _pacer.Notice();
        }

        // ------------------------------------------------------------------ nudging

        void Attempt()
        {
            PrepareView();
            EnsurePacer();
            int n = _pacer.Order(_order);
            for (int i = 0; i < n; i++)
            {
                if (!TryNudge(_order[i])) continue;
                _pacer.Confirm(_order[i]);
                LastNudge = _order[i];
                Nudges++;
                return;
            }
            _pacer.Fail();
        }

        void PrepareView()
        {
            _camPos = _cam.transform.position;
            _pxAtOne = Moments.PxPerUnit(1f, _cam.pixelHeight, _cam.fieldOfView);
            _hasView = SeaPoint(new Vector3(0.5f, 0.5f, 0f), out Vector2 c);
            _viewCenter = c;
            _viewRadius = 30f;
            if (_hasView && SeaPoint(new Vector3(Mathf.Max(0.02f, pickMargin), 0.5f, 0f), out Vector2 edge))
                _viewRadius = Mathf.Clamp((edge - c).magnitude, 5f, 120f);
            _pxAtView = _hasView ? Moments.PxPerUnit(Vector3.Distance(_camPos, new Vector3(c.x, 0f, c.y)), _cam.pixelHeight, _cam.fieldOfView) : 100f;
            _far = _hasView && _pxAtView < farViewPx;
        }

        // Where a viewport ray meets the sea plane (y = 0), planar.
        bool SeaPoint(Vector3 viewport, out Vector2 planar)
        {
            var ray = _cam.ViewportPointToRay(viewport);
            planar = default;
            if (ray.direction.y > -1e-3f) return false;
            float k = -ray.origin.y / ray.direction.y;
            Vector3 p = ray.origin + ray.direction * k;
            planar = new Vector2(p.x, p.z);
            return true;
        }

        bool Pickable(Vector3 world, float size) => Moments.Noticed(_cam, world, size, pickMargin, PickPx);

        // Whole islands too far away for a subject of `size` to reach pickPx are skipped.
        bool IslandInReach(Island island, float size)
        {
            if (island == null || !island.isActiveAndEnabled || island.IsSunk || island.LandArea <= 0f) return false;
            float reach = size * _pxAtOne / Mathf.Max(1f, PickPx);
            return Vector3.Distance(_camPos, island.transform.position) - island.BoundingRadius <= reach;
        }

        bool TryNudge(LifeNudge k)
        {
            switch (k)
            {
                case LifeNudge.HerdSignature:
                case LifeNudge.HerdPlay:
                case LifeNudge.HerdSpecies:
                case LifeNudge.HerdWander:
                case LifeNudge.HerdMeeting: return TryHerds(k);
                case LifeNudge.Flock: return TryFlock();
                case LifeNudge.Critter: return TryCritters();
                case LifeNudge.Fireflies: return TryFireflies();
                case LifeNudge.Fish: return TryFish();
                case LifeNudge.Dolphins: return TryDolphins();
                case LifeNudge.Whale: return TryWhale();
                case LifeNudge.SeaShow: return TrySeaShow();
                case LifeNudge.SleepStir: return TrySleepStir();
                default: return false;
            }
        }

        // The routine a species shows on its own (IslandHerdSystem.Think), started on demand; None when it has none
        // beyond its signature move.
        public static AnimalActivity SpeciesMove(LifeKind kind)
        {
            switch (kind)
            {
                case LifeKind.Penguin: return AnimalActivity.Slide;
                case LifeKind.Flamingo: return AnimalActivity.Parade;
                case LifeKind.Capybara: return AnimalActivity.Soak;
                case LifeKind.Giraffe: return AnimalActivity.Browse;
                case LifeKind.Zebra:
                case LifeKind.Reindeer: return AnimalActivity.Stampede;
                case LifeKind.Goat:
                case LifeKind.Meerkat: return AnimalActivity.Sentry;
                case LifeKind.ArcticFox: return AnimalActivity.Pounce;
                default: return AnimalActivity.None;
            }
        }

        // Species routines whose start IslandHerdSystem does not report itself (the director reports them).
        public static bool ReportsItself(AnimalActivity act) =>
            act != AnimalActivity.Stampede && act != AnimalActivity.Sentry && act != AnimalActivity.Pounce && act != AnimalActivity.Tuck;

        static MomentKind HerdMoment(LifeNudge k) =>
            k == LifeNudge.HerdSignature ? MomentKind.Signature : k == LifeNudge.HerdPlay ? MomentKind.Play
            : k == LifeNudge.HerdMeeting ? MomentKind.Meeting : MomentKind.Errand;

        bool TryHerds(LifeNudge k)
        {
            var w = watch != null ? watch.Watched : null;
            if (w != null && w.herds != null && w.ground != null && TryHerd(w.ground, w.herds, watch.FollowedHerd, k)) return true;
            float size = Moments.SubjectSize(HerdMoment(k));
            var all = Island.All;
            int count = all.Count;
            if (count == 0) return false;
            int start = _rng.Next(count);
            for (int j = 0; j < count; j++)
            {
                var island = all[(start + j) % count];
                if (!IslandInReach(island, size)) continue;
                if (!island.TryGetComponent(out IslandHerdSystem herds) || !herds.isActiveAndEnabled) continue;
                int hc = herds.HerdCount;
                if (hc == 0) continue;
                int hs = _rng.Next(hc);
                for (int h = 0; h < hc; h++)
                    if (TryHerd(island, herds, (hs + h) % hc, k)) return true;
            }
            return false;
        }

        bool TryHerd(Island island, IslandHerdSystem herds, int h, LifeNudge k)
        {
            if (island == null || herds == null || h < 0 || h >= herds.HerdCount || herds.HerdSize(h) == 0 || !herds.HerdFree(h)) return false;
            Vector2 c = herds.HerdCenter(h);
            Vector3 world = island.transform.TransformPoint(new Vector3(c.x, island.SampleHeight(c), c.y));
            if (!Pickable(world, Moments.SubjectSize(HerdMoment(k)))) return false;
            var kind = herds.HerdKind(h);
            switch (k)
            {
                case LifeNudge.HerdSignature:
                {
                    var sig = IslandHerdSystem.SignatureOf(kind);
                    return sig != AnimalActivity.None && herds.StartChoreography(h, sig);
                }
                case LifeNudge.HerdPlay:
                    return herds.TryStartPlay(h);
                case LifeNudge.HerdSpecies:
                {
                    var move = SpeciesMove(kind);
                    if (move == AnimalActivity.None) return false;
                    int before = _momentSerial;
                    if (!herds.StartChoreography(h, move)) return false;
                    if (!ReportsItself(move) && _momentSerial == before) Moments.Report(MomentKind.Errand, world);
                    return true;
                }
                case LifeNudge.HerdMeeting:
                    // The herd in view walks over to its nearest free neighbour (within meetDirectorRange).
                    return herds.TryStartMeeting(h);
                case LifeNudge.HerdWander:
                {
                    int s = _rng.Next(WanderMoves.Length);
                    for (int t = 0; t < WanderMoves.Length; t++)
                        if (herds.StartChoreography(h, WanderMoves[(s + t) % WanderMoves.Length])) return true;
                    return false;
                }
                default:
                    return false;
            }
        }

        bool TryFlock()
        {
            if (flocks == null || !flocks.isActiveAndEnabled) return false;
            int n = flocks.FlockCount;
            if (n == 0) return false;
            bool day = LifeEnvironment.NightAmount < 0.5f;
            int start = _rng.Next(n);
            for (int j = 0; j < n; j++)
            {
                int i = (start + j) % n;
                bool sea = flocks.IsSeabird(i);
                if (!sea)
                {
                    var st = flocks.StateOf(i);
                    if (!day || (st != FlockSystem.FlockState.Orbit && st != FlockSystem.FlockState.Cruise)) continue;
                }
                Vector2 p = flocks.PositionOf(i);
                // The murmur is reported at 4 u (FlockSystem.StartMurmur), a dive where the bird is.
                Vector3 world = sea ? new Vector3(p.x, flocks.HeightOf(i), p.y) : new Vector3(p.x, 4f, p.y);
                if (!Pickable(world, Moments.SubjectSize(sea ? MomentKind.BirdDive : MomentKind.BirdMurmur))) continue;
                if (flocks.TryStartSpecialMove(i)) return true;
            }
            return false;
        }

        bool TryCritters()
        {
            float size = Moments.SubjectSize(MomentKind.Butterflies);
            var all = Island.All;
            int count = all.Count;
            if (count == 0) return false;
            int start = _rng.Next(count);
            for (int j = 0; j < count; j++)
            {
                var island = all[(start + j) % count];
                if (!IslandInReach(island, size)) continue;
                if (!island.TryGetComponent(out IslandCrittersSystem critters) || !critters.isActiveAndEnabled) continue;
                int n = critters.CritterCount;
                if (n == 0) continue;
                int cs = _rng.Next(n);
                for (int t = 0; t < n; t++)
                {
                    int i = (cs + t) % n;
                    var kind = critters.KindOf(i);
                    if (kind == LifeKind.Firefly || critters.DyingOf(i) || critters.StateOf(i) == CritterState.Hidden) continue;
                    Vector2 p = critters.PositionOf(i);
                    Vector3 world = island.transform.TransformPoint(new Vector3(p.x, island.SampleHeight(p), p.y));
                    float s = Moments.SubjectSize(kind == LifeKind.Butterfly ? MomentKind.Butterflies : MomentKind.CritterMove);
                    if (!Pickable(world, s)) continue;
                    if (critters.TryStartSpecialMove(i)) return true;
                }
            }
            return false;
        }

        bool TryFireflies()
        {
            if (!Night) return false;
            float size = Moments.SubjectSize(MomentKind.FireflyWave);
            // Watching: the wave starts right at the subject (round a sleeping herd), visitors fly in if needed.
            var w = watch != null ? watch.Watched : null;
            var ground = w != null ? w.ground : null;
            if (ground != null && IslandInReach(ground, size))
            {
                Vector2 local;
                int fh = watch.FollowedHerd;
                if (w.herds != null && fh >= 0 && fh < w.herds.HerdCount) local = w.herds.HerdCenter(fh);
                else
                {
                    Vector3 f = ground.transform.InverseTransformPoint(watch.FollowFocus);
                    local = new Vector2(f.x, f.z);
                }
                if (TryFirefliesAt(ground, local, size)) return true;
            }
            var all = Island.All;
            int count = all.Count;
            if (count == 0) return false;
            int start = _rng.Next(count);
            // A glow spot of the swarm in view first, then the middle of the view on whichever island it falls.
            for (int j = 0; j < count; j++)
            {
                var island = all[(start + j) % count];
                if (TryFirefliesOn(island, size)) return true;
            }
            if (!_hasView) return false;
            for (int j = 0; j < count; j++)
            {
                var island = all[(start + j) % count];
                if (!IslandInReach(island, size)) continue;
                if (TryFirefliesAt(island, island.ToLocal(_viewCenter), size)) return true;
            }
            return false;
        }

        bool TryFirefliesAt(Island island, Vector2 local, float size)
        {
            float h = island.SampleHeight(local);
            if (h <= 0.05f) return false;
            if (!island.TryGetComponent(out IslandCrittersSystem critters) || !critters.isActiveAndEnabled) return false;
            if (!Pickable(island.transform.TransformPoint(new Vector3(local.x, h, local.y)), size)) return false;
            return critters.TriggerFireflyWaveAt(local);
        }

        // Never wakes a herd: a sleeper lifts its head and lies down again (IslandHerdSystem.TryStirInSleep).
        bool TrySleepStir()
        {
            float size = Moments.SubjectSize(MomentKind.SleepStir);
            var w = watch != null ? watch.Watched : null;
            if (w != null && w.herds != null && w.ground != null && TryStir(w.ground, w.herds, watch.FollowedHerd, size)) return true;
            var all = Island.All;
            int count = all.Count;
            if (count == 0) return false;
            int start = _rng.Next(count);
            for (int j = 0; j < count; j++)
            {
                var island = all[(start + j) % count];
                if (!IslandInReach(island, size)) continue;
                if (!island.TryGetComponent(out IslandHerdSystem herds) || !herds.isActiveAndEnabled) continue;
                int hc = herds.HerdCount;
                if (hc == 0) continue;
                int hs = _rng.Next(hc);
                for (int h = 0; h < hc; h++)
                    if (TryStir(island, herds, (hs + h) % hc, size)) return true;
            }
            return false;
        }

        bool TryStir(Island island, IslandHerdSystem herds, int h, float size)
        {
            if (island == null || herds == null || h < 0 || h >= herds.HerdCount || herds.HerdSize(h) == 0) return false;
            Vector2 c = herds.HerdCenter(h);
            if (!Pickable(island.transform.TransformPoint(new Vector3(c.x, island.SampleHeight(c), c.y)), size)) return false;
            return herds.TryStirInSleep(h);
        }

        // The wave starts at a glow blob in view.
        bool TryFirefliesOn(Island island, float size)
        {
            if (!IslandInReach(island, size)) return false;
            if (!island.TryGetComponent(out IslandCrittersSystem critters) || !critters.isActiveAndEnabled) return false;
            int n = critters.GlowBlobs;
            if (critters.FireflyWaveActive || n <= 0) return false;
            int bs = _rng.Next(n);
            for (int t = 0; t < n; t++)
            {
                int b = (bs + t) % n;
                Vector2 p = critters.GlowBlobOf(b);
                if (!Pickable(island.transform.TransformPoint(new Vector3(p.x, island.SampleHeight(p), p.y)), size)) continue;
                return critters.TriggerFireflyWave(b);
            }
            return false;
        }

        bool TryFish()
        {
            if (fish == null || !fish.isActiveAndEnabled) return false;
            int n = fish.SchoolSlots;
            if (n == 0) return false;
            float size = Moments.SubjectSize(MomentKind.FishJump);
            int start = _rng.Next(n);
            for (int j = 0; j < n; j++)
            {
                int i = (start + j) % n;
                if (!fish.SchoolActive(i) || fish.SchoolIsPickup(i)) continue;
                Vector2 p = fish.SchoolPosition(i);
                if (!Pickable(new Vector3(p.x, 0f, p.y), size)) continue;
                if (fish.TriggerJump(i)) return true;
            }
            return false;
        }

        bool SeaGroupOk(int i, SeaLifeSystem.Kind want, bool whale)
        {
            if (!seaLife.GroupActive(i) || seaLife.GroupFade(i) < 0.9f) return false;
            if (seaLife.GroupIsPickup(i) || seaLife.GroupIsCompanion(i)) return false;
            var kind = seaLife.GroupKind(i);
            return whale ? kind == SeaLifeSystem.Kind.Whale || kind == SeaLifeSystem.Kind.WhalePod : kind == want;
        }

        bool TryDolphins()
        {
            if (seaLife == null || !seaLife.isActiveAndEnabled) return false;
            int n = seaLife.GroupSlots;
            if (n == 0) return false;
            float size = Moments.SubjectSize(MomentKind.DolphinJump);
            int start = _rng.Next(n);
            for (int j = 0; j < n; j++)
            {
                int i = (start + j) % n;
                if (!SeaGroupOk(i, SeaLifeSystem.Kind.Dolphins, false)) continue;
                Vector2 p = seaLife.GroupPosition(i);
                if (!Pickable(new Vector3(p.x, 0f, p.y), size)) continue;
                if (seaLife.TriggerDolphinJump(i)) return true;
            }
            return false;
        }

        bool TryWhale()
        {
            if (seaLife == null || !seaLife.isActiveAndEnabled) return false;
            int n = seaLife.GroupSlots;
            if (n == 0) return false;
            float size = Moments.SubjectSize(MomentKind.WhaleSurface);
            int start = _rng.Next(n);
            for (int j = 0; j < n; j++)
            {
                int i = (start + j) % n;
                // State 0 = deep; only a whale below the surface has something to show by coming up.
                if (!SeaGroupOk(i, SeaLifeSystem.Kind.Whale, true) || seaLife.GroupState(i) != 0) continue;
                Vector2 p = seaLife.GroupPosition(i);
                var world = new Vector3(p.x, 0f, p.y);
                if (!Pickable(world, size)) continue;
                int before = _momentSerial;
                if (!seaLife.TriggerWhaleSurface(i)) continue;
                // TriggerWhaleSurface sets the state directly, past the report in the whale's own step.
                if (_momentSerial == before) Moments.Report(MomentKind.WhaleSurface, world);
                return true;
            }
            return false;
        }

        bool TrySeaShow()
        {
            if (seaLife == null || !seaLife.isActiveAndEnabled || !_hasView) return false;
            return seaLife.TryShowNear(_viewCenter, _viewRadius, _rng.Next(), MinSubjectSize(PickPx, _pxAtView));
        }

        // Far view by day: when no songbird flock is in the picture, the nearest one under way is called to the
        // island the view rests on (FlockSystem.SetTarget: it flies over and circles it), so a murmur - five units
        // of swirling birds, one of the few things big enough out here - can start in view.
        void CallFlock()
        {
            if (_callWait > 0f || flocks == null || !flocks.isActiveAndEnabled || !_hasView) return;
            int n = flocks.FlockCount;
            float r2 = _viewRadius * _viewRadius;
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (flocks.IsSeabird(i)) continue;
                float d = (flocks.PositionOf(i) - _viewCenter).sqrMagnitude;
                if (d <= r2) return;
                var st = flocks.StateOf(i);
                if (st != FlockSystem.FlockState.Cruise && st != FlockSystem.FlockState.Orbit) continue;
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) return;
            var host = IslandUnder(_viewCenter);
            if (host == null || flocks.TargetOf(best) == host) return;
            flocks.SetTarget(best, host);
            FlocksCalled++;
            _callWait = flockCallInterval;
        }

        Island IslandUnder(Vector2 p)
        {
            var player = session != null ? session.player : null;
            if (Covers(player, p)) return player;
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != player && Covers(all[i], p)) return all[i];
            return player != null && !player.IsSunk ? player : null;
        }

        static bool Covers(Island island, Vector2 p) =>
            island != null && island.isActiveAndEnabled && !island.IsSunk && island.LandArea > 0f && island.SampleHeight(island.ToLocal(p)) > 0f;
    }
}
