using System.Collections.Generic;
using Drift.Core;
using UnityEngine;

namespace Drift.Life
{
    public enum AnimalState { Graze, Look, Walk, Rest, Sleep, Play }

    // What an animal is up to on top of its AnimalState (which stays the pose / the six basic moods). Flee and
    // Huddle are derived from the herd, the rest is set by the behaviours in IslandHerdSystem.
    // Values are only ever appended (WatchTools and tests index moods by them). From Slide on: the biome
    // choreographies, each a sequence of steps (IslandHerdSystem.ActivityStep).
    public enum AnimalActivity
    {
        None, Drink, Wade, Wallow, Lookout, Spar, Race, Binky, Dig, Burrowed, Line, Shade, Watch, Huddle, Flee,
        Slide, Parade, OneLeg, Browse, Stampede, Soak, Tuck, Circle, Sentry, Pounce, HopChain, Shake,
        // Patterns every species knows (2026-09-21).
        Stroll, Visit, Spread,
        // Signature moves: one per species, no other species has it (IslandHerdSystem.Signature.cs).
        Zigzag, Carousel, Rear, Scratch, Snuggle, StampDance, NeckDuel, SkyCall, Crater, TailChase, WarDance, Groom, Necking
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    public partial class IslandHerdSystem : MonoBehaviour
    {
        const string ObjName = "Herds";
        // Ground below this is under water: a member standing there is gone.
        const float DrownHeight = 0.03f;
        // Head pitch targets in degrees, positive = head down (the shader dips the head end by lever * sin).
        const float GrazePitch = 10f, LookPitch = -7f, StretchPitch = -14f, RestPitch = 5f, SleepPitch = 14f, WalkPitch = 0f;
        const float DrinkPitch = 13f, ButtPitch = 12f;
        const float BrowsePitch = -24f, ChewPitch = -17f, NoseDivePitch = 58f, BellyPitch = 84f, UprightPitch = -68f;
        // Drinkers stand on the wet sand above DrownHeight; waders (oxen) stand with their feet in the water
        // down to WadeFloor and are exempt from drowning while they carry the shore flag.
        const float DrinkLow = 0.045f, DrinkHigh = 0.13f, WadeFloor = -0.06f, WadeHigh = 0.02f;

        static readonly string[] StateMoods = { "grast", "schaut auf", "wandert", "ruht", "schläft", "spielt" };
        static readonly string[] ActivityMoods =
        {
            "", "trinkt am Ufer", "kühlt sich im flachen Wasser ab", "wälzt sich im Staub", "hält Ausschau vom Grat",
            "misst seine Kräfte", "rennt mit den anderen Jungen um die Wette", "macht Luftsprünge", "gräbt einen Bau",
            "versteckt sich im Bau", "läuft im Gänsemarsch", "sucht Schutz unter einem Baum", "schaut in die Ferne",
            "drängt sich im Sturm zusammen", "flieht",
            "rutscht auf dem Bauch zum Wasser", "marschiert im Gleichschritt", "steht auf einem Bein",
            "zupft Blätter aus der Baumkrone", "galoppiert mit der Herde", "badet – nur der Kopf schaut heraus",
            "hat sich in den Panzer zurückgezogen", "bildet einen Schutzkreis um die Jungen", "hält Wache auf den Hinterbeinen",
            "macht einen Mäusesprung", "springt über den Hügel – eins nach dem anderen", "schüttelt sich trocken",
            "bummelt am Ufer entlang", "besucht die Nachbarherde", "schwärmt zum Grasen aus",
            "macht Männchen und schlägt Haken", "läuft mit der Herde im Kreis", "stellt sich auf die Hinterbeine",
            "scheuert sich am Baum", "kuschelt im Stern – ein Vogel sitzt obenauf", "trippelt im Kreis und seiht den Schlamm",
            "reckt den Hals im Kräftemessen", "reckt den Schnabel zum Himmel und ruft", "scharrt nach Flechten",
            "jagt den eigenen Schwanz", "hüpft im Kriegstanz", "krault einem anderen Zebra das Fell", "schwingt den Hals im Halskampf"
        };

        // Steps of the choreographies as ActivityStep reports them.
        public const int SlideQueue = 0, SlideFlop = 1, SlideGlide = 2, SlideDive = 3, SlideSurface = 4, SlideShake = 5;
        public const int ParadeGather = 0, ParadeFlag = 1, ParadeMarch = 2, ParadeOneLeg = 3;
        public const int BrowseWalk = 0, BrowseStretch = 1, BrowseChew = 2, BrowseSway = 3;
        public const int StampedeGallop = 0, StampedeHeadsUp = 1;
        public const int SoakWalk = 0, SoakLoaf = 1;
        public const int TuckHide = 0, TuckPeek = 1;
        public const int CircleWalk = 0, CircleGuard = 1;
        public const int PounceStalk = 0, PounceLeap = 1, PounceHeadIn = 2, PounceShake = 3;

        enum Errand { None, Drink, Wade, Shade, Watch, Slide, Parade, Browse, Soak, Circle, Stroll, Visit, Spread, Signature }
        enum PlayKind { Chase, Spar, Race }

        public int seed = 1;
        // Tiers by planar camera distance (LifeLod): near < detailDistance runs every frame and rebuilds the
        // mesh at meshInterval when an animal moved or changed state; mid < simDistance steps at
        // midStepInterval and rebuilds at twice the interval; far only drifts herd centres every
        // farStepInterval and never rebuilds unless the renderer is visible (then hiddenMeshInterval).
        public float simDistance = 100f;
        public float hideDistance = 260f;
        public float detailDistance = 45f;
        public float meshInterval = 1f / 15f;
        public float hiddenMeshInterval = 1f;
        public float midStepInterval = 0.1f;
        public float farStepInterval = 0.5f;
        [SerializeField] Material animalMaterial;
        public float huddleThreshold = 0.3f;
        public float fleeSpeed = 2.6f;
        public float fleeDistance = 7f;
        public float ancientHerdSize = 1.5f;
        public float barrenHerdSize = 0.6f;
        public float animalScale = 0.175f;
        public float areaPerHerd = 7f;
        public float minHerdArea = 4f;
        public int maxHerds = 40;
        // Hard cap per island (animals are 60-78 verts each, so ~9k verts worst case in one mesh).
        public int maxAnimals = 120;
        // Herds spawn and choose wander targets at least this far from other herds, so they read as separate clusters.
        public float herdSpacing = 1.6f;
        // Formation radius in body lengths: member k sits at ~formationSpacing * body * sqrt(k), which keeps the
        // cluster's density constant as a herd grows.
        public float formationSpacing = 2.2f;
        // Herds pick targets at least this much above their species' lowest ground and walk uphill once the
        // rising shore comes closer than that.
        public float shoreMargin = 0.2f;
        // Births are the growth path inside a herd (new herds only come with new land, see _peakArea): every
        // growthInterval a calm herd standing by day on mature ground bears one young with growthChance, never a
        // second while one is below youngIndependence grown. A young starts at youngScale of the adult size,
        // paler by youngTintAmount, and grows linearly over the species' grow time; its mesh is re-baked only
        // when its growth moved growthRebuildStep since the last bake (a hare every ~10 s, an ox every ~30 s).
        public float growthInterval = 45f;
        public float growthChance = 0.5f;
        public float youngScale = 0.45f;
        public float youngIndependence = 0.7f;
        public float growthRebuildStep = 0.04f;
        public float youngPlayRate = 3f;
        public float youngTintAmount = 0.45f;
        public Color youngTint = new Color(1f, 0.98f, 0.94f);
        // Night (LifeEnvironment.NightAmount): above sleepThreshold herds settle and non-watchers fall asleep;
        // below wakeThreshold sleepers get up, each after its own 5..dawnStaggerMax s, and the herd waits for them.
        public float sleepThreshold = 0.6f;
        public float wakeThreshold = 0.45f;
        public float dawnStaggerMax = 60f;
        // Must match _TransitionTime on the animal material; only used to pick up an interrupted pose blend.
        public float transitionTime = 0.8f;
        // Watchers stay awake at night: member 0 of a herd with >= 6 animals, and the middle one from 12 on.
        public int watchersPerHerd = 1;
        // Play: at most one pair per herd chases in a loop round the herd centre; rate multiplier on the species.
        public float playRate = 1f;
        public float playSpeed = 1.8f;
        public float playDurationMin = 4f;
        public float playDurationMax = 8f;
        // Detail LOD: an animal closer than detailNear to the camera (LifeEnvironment.ViewDistance, which is
        // LifeLod.Distance unless a view provider is installed) is baked from its AnimalModels template and falls
        // back to the simple one beyond detailFar; at most maxDetailed per island, nearest first, and a detailed
        // animal keeps its place against a newcomer that is less than the hysteresis band closer. The set is
        // re-evaluated every detailCheckInterval and the mesh only rebuilt when it changed.
        public float detailNear = 9f;
        public float detailFar = 12f;
        public int maxDetailed = 40;
        // Second budget on the same nearest-first list, so forty oxen cost no more than forty hares would.
        public int maxDetailVertices = 10400;
        public float detailCheckInterval = 0.25f;
        public float youngModelGrowth = 0.6f;
        // Behaviours (see Think): every rate below is multiplied by behaviourRate, 0 switches them all off.
        public float behaviourRate = 1f;
        [Header("Bewegungsmuster aller Arten (Chance pro Denk-Takt, nur tagsüber und ohne andere Besorgung)")]
        [Tooltip("Uferbummel: die Herde zieht in Zweierreihe mehrere Etappen am Ufer entlang.")]
        [Range(0f, 0.2f)] public float strollRate = 0.012f;
        [Tooltip("Nachbarbesuch: die Herde läuft zu einer anderen Herde, ein Teil mischt sich darunter.")]
        [Range(0f, 0.2f)] public float visitRate = 0.012f;
        [Tooltip("Ausschwärmen: die Herde fächert sich weit auf, grast verteilt und sammelt sich wieder.")]
        [Range(0f, 0.2f)] public float spreadRate = 0.02f;
        [Range(2f, 20f)] public float visitRange = 10f;
        [Header("Eigene Bewegung jeder Art")]
        [Tooltip("Chance pro Denk-Takt, dass eine stehende Herde tagsüber ihre arteigene Bewegung zeigt (Hakenschlagen, Schafkreisel, Aufrichten, Scheuern, Kuschelstern, Schlammtanz, Halsrecken, Himmelsruf, Kraterscharren, Schwanzjagd, Kriegstanz, Fellpflege, Halskampf).")]
        [Range(0f, 0.2f)] public float signatureRate = 0.02f;
        [Header("Flucht vor dem Wasser (sinkende Insel)")]
        [Tooltip("Mindesthöhe über dem Wasser, die eine Herde bei steigendem Wasser hält. Liegt ihr Boden tiefer, zieht sie zum nächsten sicheren Fleck ihres Landstücks – nicht gleich auf den Gipfel.")]
        [Range(0.05f, 1.5f)] public float refugeStart = 0.45f;
        [Tooltip("Vorlauf in Sekunden: so viele Sekunden des aktuellen Sinktempos hält eine Herde zusätzlich Abstand zum Wasser (schnelles Sinken = größerer Abstand).")]
        [Range(0f, 120f)] public float refugeLead = 30f;
        [Tooltip("Wie stark fliehende Herden Abstand zu anderen Herden halten (in Einheiten Umweg, 0 = alle zum nächsten sicheren Fleck). Erst wenn der sichere Streifen voll ist, rücken sie zusammen.")]
        [Range(0f, 6f)] public float refugeSpread = 3f;
        [Tooltip("Tempo der Flucht (× normales Wandertempo).")]
        [Range(1f, 4f)] public float refugeSpeed = 2.2f;
        [Range(1f, 5f)] public float strollLeg = 2.4f;
        public float thinkInterval = 1f;
        public float shoreSearch = 7f;
        public float shoreWalk = 3f;
        public float rainThreshold = 0.1f;
        public float noonWindow = 0.07f;
        public float shadeRange = 6f;
        public float watchRange = 15f;
        public float lookoutRange = 5f;
        public float lookoutMinRise = 0.12f;
        public float burrowReach = 5f;
        public float burrowScale = 0.11f;
        public int maxBurrows = 6;
        public float lineSpacing = 0.9f;

        // 0..1, set by the island each frame from the storm intensity; above huddleThreshold herds stop wandering.
        public float Agitation;

        class Species
        {
            public LifeKind kind;
            public float minArea, weight;
            public int sizeMin, sizeMax, maxSize;
            // body: template length (world length = body * animalScale); speed in units/s.
            public float speed, minH, maxH, body, hop;
            // Idle timings in seconds; stop = how long the herd stays put between wanders.
            public float grazeMin, grazeMax, lookMin, lookMax, lookChance, restChance, restMin, restMax, stopMin, stopMax;
            // Play starts per second per herd (x3 at dusk and dawn).
            public float play;
            // Seconds of herd time a young needs to reach adult size.
            public float grow;
            // Hopping species travel in bursts of burstMin..burstMax s at burstSpeed x speed, then pause pauseMin..pauseMax s.
            public bool hops;
            public float burstMin, burstMax, pauseMin, pauseMax, burstSpeed;
            // (int)LifeBiome whose islands bring the species forth; elsewhere it only arrives with a merge.
            public int biome;
            // Coastal species choose their ground next to shore cells and may stand closer to the water.
            public bool coastal;
            public float grazePitch = GrazePitch;
            // Flamingos rest and sleep standing (on one leg, AnimalModels.PoseSpecial) instead of lying down.
            public bool standsToRest;
        }

        static readonly Species[] Specs =
        {
            new Species { kind = LifeKind.Hare,  minArea = 4f,  weight = 3f, sizeMin = 6, sizeMax = 10, maxSize = 16, speed = 0.5f,   minH = 0.15f, maxH = 2.0f, body = 0.26f, hop = 0.09f,
                          grazeMin = 2f, grazeMax = 5f, lookMin = 1f, lookMax = 2.5f, lookChance = 0.5f, restChance = 0.15f, restMin = 8f, restMax = 20f, stopMin = 4f, stopMax = 10f, play = 0.02f, grow = 240f,
                          hops = true, burstMin = 0.8f, burstMax = 1.5f, pauseMin = 0.4f, pauseMax = 1.1f, burstSpeed = 2.6f },
            new Species { kind = LifeKind.Sheep, minArea = 15f, weight = 2f, sizeMin = 5, sizeMax = 8,  maxSize = 14, speed = 0.225f, minH = 0.15f, maxH = 2.2f, body = 0.48f, hop = 0.02f,
                          grazeMin = 6f, grazeMax = 14f, lookMin = 1.5f, lookMax = 3f, lookChance = 0.25f, restChance = 0.35f, restMin = 15f, restMax = 40f, stopMin = 10f, stopMax = 25f, play = 0.006f, grow = 480f },
            new Species { kind = LifeKind.Goat,  minArea = 30f, weight = 1f, sizeMin = 4, sizeMax = 6,  maxSize = 10, speed = 0.275f, minH = 0.9f,  maxH = 3.4f, body = 0.38f, hop = 0.03f,
                          grazeMin = 4f, grazeMax = 9f, lookMin = 2f, lookMax = 4f, lookChance = 0.45f, restChance = 0.25f, restMin = 10f, restMax = 25f, stopMin = 8f, stopMax = 18f, play = 0.012f, grow = 480f },
            new Species { kind = LifeKind.Ox,    minArea = 50f, weight = 1f, sizeMin = 3, sizeMax = 5,  maxSize = 8,  speed = 0.175f, minH = 0.15f, maxH = 1.9f, body = 0.66f, hop = 0f,
                          grazeMin = 10f, grazeMax = 22f, lookMin = 2f, lookMax = 5f, lookChance = 0.2f, restChance = 0.45f, restMin = 15f, restMax = 40f, stopMin = 15f, stopMax = 40f, play = 0.002f, grow = 720f },
            new Species { kind = LifeKind.Capybara, biome = 1, coastal = true, minArea = 4f, weight = 3f, sizeMin = 4, sizeMax = 7, maxSize = 12, speed = 0.2f, minH = 0.15f, maxH = 1.6f, body = 0.5f, hop = 0.015f,
                          grazeMin = 5f, grazeMax = 12f, lookMin = 1.5f, lookMax = 3f, lookChance = 0.25f, restChance = 0.5f, restMin = 15f, restMax = 40f, stopMin = 10f, stopMax = 25f, play = 0.008f, grow = 360f },
            new Species { kind = LifeKind.Flamingo, biome = 1, coastal = true, standsToRest = true, grazePitch = 38f, minArea = 10f, weight = 2f, sizeMin = 6, sizeMax = 10, maxSize = 16, speed = 0.2f, minH = 0.12f, maxH = 1.2f, body = 0.4f, hop = 0.01f,
                          grazeMin = 4f, grazeMax = 9f, lookMin = 1.5f, lookMax = 3f, lookChance = 0.3f, restChance = 0.3f, restMin = 10f, restMax = 25f, stopMin = 10f, stopMax = 22f, play = 0.004f, grow = 480f },
            new Species { kind = LifeKind.Tortoise, biome = 1, minArea = 20f, weight = 1f, sizeMin = 2, sizeMax = 3, maxSize = 5, speed = 0.05f, minH = 0.15f, maxH = 1.5f, body = 0.6f, hop = 0f,
                          grazeMin = 12f, grazeMax = 25f, lookMin = 3f, lookMax = 6f, lookChance = 0.2f, restChance = 0.5f, restMin = 20f, restMax = 50f, stopMin = 20f, stopMax = 50f, play = 0f, grow = 900f },
            new Species { kind = LifeKind.Reindeer, biome = 2, minArea = 15f, weight = 2f, sizeMin = 5, sizeMax = 8, maxSize = 14, speed = 0.26f, minH = 0.15f, maxH = 2.6f, body = 0.52f, hop = 0.025f,
                          grazeMin = 6f, grazeMax = 14f, lookMin = 2f, lookMax = 4f, lookChance = 0.3f, restChance = 0.3f, restMin = 15f, restMax = 35f, stopMin = 10f, stopMax = 22f, play = 0.008f, grow = 480f },
            new Species { kind = LifeKind.Penguin, biome = 2, coastal = true, grazePitch = 3f, minArea = 4f, weight = 3f, sizeMin = 6, sizeMax = 10, maxSize = 16, speed = 0.16f, minH = 0.12f, maxH = 1.4f, body = 0.3f, hop = 0.012f,
                          grazeMin = 3f, grazeMax = 7f, lookMin = 1.5f, lookMax = 3f, lookChance = 0.45f, restChance = 0.2f, restMin = 8f, restMax = 20f, stopMin = 6f, stopMax = 14f, play = 0.015f, grow = 300f },
            new Species { kind = LifeKind.ArcticFox, biome = 2, minArea = 25f, weight = 1f, sizeMin = 2, sizeMax = 2, maxSize = 4, speed = 0.34f, minH = 0.15f, maxH = 2.8f, body = 0.42f, hop = 0.03f,
                          grazeMin = 3f, grazeMax = 6f, lookMin = 2f, lookMax = 4f, lookChance = 0.5f, restChance = 0.3f, restMin = 12f, restMax = 30f, stopMin = 6f, stopMax = 14f, play = 0.01f, grow = 360f },
            new Species { kind = LifeKind.Zebra, biome = 3, minArea = 15f, weight = 2f, sizeMin = 5, sizeMax = 8, maxSize = 14, speed = 0.28f, minH = 0.15f, maxH = 2.0f, body = 0.56f, hop = 0.03f,
                          grazeMin = 6f, grazeMax = 14f, lookMin = 2f, lookMax = 4f, lookChance = 0.3f, restChance = 0.2f, restMin = 12f, restMax = 30f, stopMin = 10f, stopMax = 22f, play = 0.006f, grow = 480f },
            new Species { kind = LifeKind.Giraffe, biome = 3, grazePitch = 3f, minArea = 40f, weight = 1f, sizeMin = 3, sizeMax = 4, maxSize = 7, speed = 0.22f, minH = 0.15f, maxH = 1.9f, body = 0.6f, hop = 0.012f,
                          grazeMin = 8f, grazeMax = 16f, lookMin = 3f, lookMax = 6f, lookChance = 0.35f, restChance = 0.1f, restMin = 10f, restMax = 25f, stopMin = 14f, stopMax = 30f, play = 0.002f, grow = 720f },
            new Species { kind = LifeKind.Meerkat, biome = 3, minArea = 4f, weight = 3f, sizeMin = 6, sizeMax = 10, maxSize = 16, speed = 0.42f, minH = 0.15f, maxH = 2.0f, body = 0.34f, hop = 0.03f,
                          grazeMin = 2f, grazeMax = 5f, lookMin = 1f, lookMax = 2.5f, lookChance = 0.5f, restChance = 0.15f, restMin = 8f, restMax = 20f, stopMin = 5f, stopMax = 12f, play = 0.02f, grow = 240f },
        };

        class Animal
        {
            public Vector2 pos, offset;
            public float yaw, phase, scale;
            public int variant;
            public bool moving, alert, gaitOn = true;
            public AnimalState state;
            public float timer, gaitT, t0, pitchFrom, pitchTo, restFrom, restTo;
            // growth 1 = adult. A young's offset is relative to its parent's formation slot (Slot), an adult's
            // to the herd centre; bakedGrowth is what the mesh currently shows.
            public float growth = 1f, bakedGrowth = 1f, age;
            public Animal parent;
            // Behaviour layer: goal replaces the formation slot while hasGoal; shore marks a visit below the
            // species' ground (relaxed stepping, no drowning above WadeFloor) until the animal is back up.
            public AnimalActivity act;
            public Vector2 goal;
            public bool hasGoal, arrived, hidden, detailed, wantDetail, shore;
            public float actT, roll, blocked, wallowIn;
            // Choreography layer: step within the activity, dived = under water (not drawn), pose = template
            // variant (AnimalModels.PoseSpecial), pitch = baked body pitch in degrees, lineTravel for the hop chain.
            public int step;
            public bool dived, stands;
            public float pitch, lift, baseYaw, lineTravel;
            // from = a position (pounce take-off, parade place, the centre a signature move turns about); dir = a
            // heading the signature moves keep (dash direction, the flank to the tree).
            public Vector2 from, dir;
            // Timer / angle of a signature move.
            public float sigT;
        }

        class Herd
        {
            public Species spec;
            public readonly List<Animal> members = new();
            public Vector2 center, target, fleeFrom;
            public float wait, fleeTimer, growTimer, playT, playAng;
            public int playA = -1, playB = -1;
            public bool fleeing;
            public Errand errand;
            // stage 0 = walking to the errand spot, 1 = there.
            public int errandStage;
            public Vector2 errandDir;
            public float errandCool, watchCool, lookoutCool, thinkT, lineDist, sparT;
            public bool walking, diving, dawnDrink;
            public Animal lookout, digger;
            public PlayKind playKind;
            public int sparPhase, sparRounds;
            public Vector2 sparMid, sparAxis, burrow;
            public float errandT;
            // Choreographies: errandPos = the spot in the water / the tree, choreoStep/choreoT = the herd-level
            // phase (parade), stamp* = the stampede arc, tuckT = tortoises in their shells.
            public Vector2 errandPos, stampC;
            public int choreoStep;
            public float choreoT, stampR, stampDist, stampDir, stampT, tuckT, hopAt;
            public bool circled;
            // Stroll: legs still to walk, side of the shore (+1/-1). Visit: the herd being visited.
            public int legs, side;
            public Herd visit;
            // Seconds until the refuge (highest ground of its land piece) is looked up again while water rises.
            public float refugeT;
            public bool refuging;
            // Signature moves: the one or two animals at the centre of it (the capybara with the bird, the digger,
            // the pair), turns left, and a herd-level phase (carousel angle, bird timer, pair rotation).
            public Animal sigA, sigB;
            public int sigTurns;
            public float sigPhase, sigR;
            // A herd that has just come into being (spawn, merge, load) settles for this long before it shows its
            // signature move, so a fresh island first reads as calm grazing clusters.
            public float settleT = 30f;
        }

        readonly List<Herd> _herds = new();
        IIslandSurface _surface;
        IslandLifeSystem _life;
        System.Random _rnd;
        int _version = -1;
        bool _populated;
        // Herds are only added while the island grows past its previous peak (merge, volcano rising), so a
        // sinking island never spawns replacements for drowned herds.
        float _peakArea;
        float _meshTimer, _stepTimer, _stepClock, _night;
        bool _meshDirty;
        readonly List<Vector2> _burrows = new();
        float _detailTimer, _poiTimer;
        int _detailedCount;
        bool _wasNight, _hasPoi;
        Vector2 _poiLocal;
        Animal[] _lodPick = new Animal[40];
        float[] _lodKey = new float[40];
        int[] _lodVerts = new int[40];
        LifeTier _lastTier = LifeTier.Near;

        GameObject _go;
        MeshFilter _filter;
        MeshRenderer _renderer;
        Mesh _mesh;
        readonly TemplateBatch _batch = new();
        static readonly bool[] Excluded = new bool[Specs.Length];
        static readonly int[] Misses = new int[Specs.Length];
        // Below this the ground is water or the wet rim: no herd is founded there (the species' own minH still
        // decides where it may stand).
        const float GroundFloor = 0.12f;

        // Shared pose clock the shader blends transitions against (_LifeClock): the most advanced island's
        // accumulated step time, so it also runs under eval loops where Time does not move.
        static float _clock;
        static readonly int LifeClockId = Shader.PropertyToID("_LifeClock");
        public static float Clock => _clock;

        public int HerdCount => _herds.Count;

        void Moment(MomentKind kind, Herd herd)
        {
            if (Moments.Listening && _surface != null) Moments.Report(kind, transform, herd.center, _surface.SampleHeight(herd.center));
        }

        public int AnimalCount
        {
            get
            {
                int n = 0;
                foreach (var h in _herds) n += h.members.Count;
                return n;
            }
        }

        public int SpeciesCount
        {
            get
            {
                ulong mask = SpeciesPresent;
                int n = 0;
                while (mask != 0) { mask &= mask - 1; n++; }
                return n;
            }
        }

        // ---- collection hooks (journal). Bit (int)LifeKind is set while at least one animal of the kind lives here.
        public ulong SpeciesPresent
        {
            get
            {
                ulong mask = 0;
                foreach (var h in _herds) if (h.members.Count > 0) mask |= 1UL << (int)h.spec.kind;
                return mask;
            }
        }

        public bool HasSpecies(LifeKind kind) => (SpeciesPresent & (1UL << (int)kind)) != 0;

        public int AnimalsOf(LifeKind kind)
        {
            int n = 0;
            foreach (var h in _herds) if (h.spec.kind == kind) n += h.members.Count;
            return n;
        }

        public int HerdsOf(LifeKind kind)
        {
            int n = 0;
            foreach (var h in _herds) if (h.spec.kind == kind && h.members.Count > 0) n++;
            return n;
        }

        // The island's dominant biome (the one most of its land carries), for the HUD and the journal.
        public int Biome => _life != null ? _life.Biome : _surface != null ? _surface.Biome : 0;
        public string BiomeName => LifeNames.OfBiome(Biome);
        // Bit (int)LifeBiome per biome this island's land carries (one before a merge, two or three after).
        public int BiomesPresent => _life != null ? _life.BiomesPresent : 1 << (_surface != null ? _surface.Biome : 0);
        // The biome of the ground at a local position: a new herd's species comes from the land it is founded on.
        public int BiomeAt(Vector2 localPos) => _life != null ? _life.BiomeAt(localPos) : Biome;
        // A species that belongs to none of the biomes this island carries: it came with a merge but its ground
        // did not, so only its own herds keep it going (births breed true, Rebalance never adds it).
        public bool IsForeign(LifeKind kind)
        {
            var s = SpecFor((int)kind);
            return s != null && (BiomesPresent & (1 << s.biome)) == 0;
        }

        public static bool IsHerdSpecies(LifeKind kind) => SpecFor((int)kind) != null;
        public static int HomeBiomeOf(LifeKind kind)
        {
            var s = SpecFor((int)kind);
            return s != null ? s.biome : -1;
        }

        public int FleeingHerds
        {
            get
            {
                int n = 0;
                foreach (var h in _herds) if (h.fleeTimer > 0f) n++;
                return n;
            }
        }

        public int SleepingCount
        {
            get
            {
                int n = 0;
                foreach (var h in _herds) foreach (var a in h.members) if (a.state == AnimalState.Sleep) n++;
                return n;
            }
        }

        public int RestingCount
        {
            get
            {
                int n = 0;
                foreach (var h in _herds) foreach (var a in h.members) if (a.state == AnimalState.Rest || a.state == AnimalState.Sleep) n++;
                return n;
            }
        }

        public float SleepingFraction => AnimalCount > 0 ? (float)SleepingCount / AnimalCount : 0f;

        public int PlayingHerds
        {
            get
            {
                int n = 0;
                foreach (var h in _herds) if (h.playT > 0f) n++;
                return n;
            }
        }

        public int PlayingAnimals
        {
            get
            {
                int n = 0;
                foreach (var h in _herds) foreach (var a in h.members) if (a.state == AnimalState.Play) n++;
                return n;
            }
        }

        public int YoungCount
        {
            get
            {
                int n = 0;
                foreach (var h in _herds) foreach (var a in h.members) if (a.growth < 1f) n++;
                return n;
            }
        }

        public bool Huddling => Agitation >= huddleThreshold;
        public int Populations { get; private set; }
        public int MeshBuilds { get; private set; }
        public int StateChanges { get; private set; }
        public int PlaysStarted { get; private set; }
        public int YoungPlays { get; private set; }
        public int Races { get; private set; }
        public int Spars { get; private set; }
        public int ErrandsStarted { get; private set; }
        public int Drowned { get; private set; }
        public int RefugeMoves { get; private set; }
        public int Dives { get; private set; }
        public int Births { get; private set; }
        public LifeTier Tier { get; private set; }
        public int MeshVertexCount => _mesh != null ? _mesh.vertexCount : 0;
        public bool HasMesh => _mesh != null;
        public bool MeshHasAnimationData => _mesh != null && _mesh.vertexCount > 0
            && _mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0)
            && _mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1)
            && _mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord2);
        public Material AnimalMaterial => animalMaterial != null ? animalMaterial : LifeMeshes.AnimalMaterial;

        int Character => _surface != null ? _surface.Character : 0;

        public Vector2 HerdCenter(int index) => _herds[index].center;
        public Vector2 HerdTarget(int index) => _herds[index].target;
        public float HerdWait(int index) => _herds[index].wait;
        public LifeKind HerdKind(int index) => _herds[index].spec.kind;
        public int HerdSize(int index) => _herds[index].members.Count;
        public int HerdPlayingAnimals(int index)
        {
            int n = 0;
            foreach (var a in _herds[index].members) if (a.state == AnimalState.Play) n++;
            return n;
        }

        public int HerdYoungCount(int index)
        {
            int n = 0;
            foreach (var a in _herds[index].members) if (a.growth < 1f) n++;
            return n;
        }

        public int HerdMaxSize(int index) => _herds[index].spec.maxSize;
        public float HerdGrowTime(int index) => _herds[index].spec.grow;

        public Vector2 AnimalPosition(int herd, int member) => _herds[herd].members[member].pos;
        public float AnimalYaw(int herd, int member) => _herds[herd].members[member].yaw;
        public AnimalState AnimalStateOf(int herd, int member) => _herds[herd].members[member].state;
        public bool AnimalMoving(int herd, int member) => _herds[herd].members[member].moving;
        public bool IsWatcher(int herd, int member) => Watcher(_herds[herd], member);
        public float AnimalGrowth(int herd, int member) => _herds[herd].members[member].growth;
        public bool IsYoung(int herd, int member) => _herds[herd].members[member].growth < 1f;
        // Island-local position with the ground height, i.e. what the mesh bake places the animal at.
        public Vector3 AnimalLocalPosition(int herd, int member)
        {
            var p = _herds[herd].members[member].pos;
            return new Vector3(p.x, _surface != null ? _surface.SampleHeight(p) : 0f, p.y);
        }
        public float AnimalAge(int herd, int member) => _herds[herd].members[member].age;
        public float AnimalSizeFactor(int herd, int member) => SizeFactor(_herds[herd].members[member]);
        public int AnimalParent(int herd, int member)
        {
            var h = _herds[herd];
            var p = h.members[member].parent;
            return p == null ? -1 : h.members.IndexOf(p);
        }
        public float HerdSpeed(int index) => _herds[index].spec.speed;

        // ---- behaviour layer (tap popup, tests). A hidden animal sits in a burrow and is not drawn.
        public bool IsHidden(int herd, int member) => _herds[herd].members[member].hidden || _herds[herd].members[member].dived;
        public bool IsDived(int herd, int member) => _herds[herd].members[member].dived;
        // Step within a choreography (the Slide*/Parade*/Browse*/... constants); 0 outside of one.
        public int ActivityStep(int herd, int member)
        {
            var h = _herds[herd];
            var a = h.members[member];
            return a.act == AnimalActivity.Stampede || a.act == AnimalActivity.Parade ? h.choreoStep : a.step;
        }
        public int AnimalPose(int herd, int member) => PoseOf(_herds[herd], _herds[herd].members[member]);
        public float AnimalBakedPitch(int herd, int member) => _herds[herd].members[member].pitch;
        public float AnimalBakedRoll(int herd, int member) => _herds[herd].members[member].roll;
        public float AnimalLift(int herd, int member) => _herds[herd].members[member].lift;
        public bool IsDetailed(int herd, int member) => _herds[herd].members[member].detailed;
        public bool AnimalArrived(int herd, int member) => _herds[herd].members[member].hasGoal && _herds[herd].members[member].arrived;
        public AnimalActivity ActivityOf(int herd, int member) => Activity(_herds[herd], _herds[herd].members[member]);
        public string MoodOf(int herd, int member) => MoodText(_herds[herd].members[member].state, ActivityOf(herd, member));

        public static string MoodText(AnimalState state, AnimalActivity activity)
        {
            int a = (int)activity, st = (int)state;
            if (a > 0 && a < ActivityMoods.Length) return ActivityMoods[a];
            return st >= 0 && st < StateMoods.Length ? StateMoods[st] : "";
        }

        AnimalActivity Activity(Herd h, Animal a)
        {
            if (a.hidden) return AnimalActivity.Burrowed;
            if (a.act != AnimalActivity.None) return a.act;
            if (a.stands && (a.state == AnimalState.Rest || a.state == AnimalState.Sleep)) return AnimalActivity.OneLeg;
            if (h.fleeTimer > 0f || h.fleeing) return AnimalActivity.Flee;
            if (Agitation >= huddleThreshold && a.state == AnimalState.Look) return AnimalActivity.Huddle;
            return AnimalActivity.None;
        }

        public int DetailedCount => _detailedCount;
        public int DetailChanges { get; private set; }
        public int HiddenCount
        {
            get
            {
                int n = 0;
                foreach (var h in _herds) foreach (var a in h.members) if (a.hidden) n++;
                return n;
            }
        }

        public int CountActivity(AnimalActivity activity)
        {
            int n = 0;
            foreach (var h in _herds) foreach (var a in h.members) if (Activity(h, a) == activity) n++;
            return n;
        }

        public int BurrowCount => _burrows.Count;
        public Vector2 BurrowPosition(int index) => _burrows[index];
        public bool HasPointOfInterest => _hasPoi;
        public bool Raining => Agitation >= rainThreshold && Agitation < huddleThreshold;

        // A herd of `size` adults of the kind at the island-local centre, inside the caps; -1 when the caps or
        // the ground say no. Settlements and tests use it; the seeded population never does.
        public int AddHerd(LifeKind kind, Vector2 center, int size)
        {
            if (_surface == null || _rnd == null) return -1;
            var s = SpecFor((int)kind);
            if (s == null || _herds.Count >= maxHerds || AnimalCount + size > maxAnimals || size < 1) return -1;
            if (!Valid(s, _surface.SampleHeight(center))) return -1;
            var herd = new Herd { spec = s, center = center, target = center, wait = Rand(s.stopMin * 0.3f, s.stopMax), growTimer = Rand(0f, growthInterval), thinkT = Mathf.Repeat(_herds.Count * 0.37f, 1f) * thinkInterval };
            for (int m = 0; m < Mathf.Min(size, s.maxSize); m++) AddMember(herd);
            for (int m = 0; m < herd.members.Count; m++) InitState(herd, herd.members[m], m);
            _herds.Add(herd);
            _meshDirty = true;
            return _herds.Count - 1;
        }

        public void ClearHerds()
        {
            _herds.Clear();
            _burrows.Clear();
            _detailedCount = 0;
            _meshDirty = true;
        }
        public float BodyLength(int index) => _herds[index].spec.body * animalScale;

        // Largest member distance from the herd centre, for checking that a herd reads as a cluster.
        public float HerdRadius(int index)
        {
            var h = _herds[index];
            float r = 0f;
            foreach (var a in h.members) r = Mathf.Max(r, (a.pos - h.center).magnitude);
            return r;
        }

        void OnEnable()
        {
            _surface = GetComponent<IIslandSurface>();
            _life = GetComponent<IslandLifeSystem>();
            if (_surface != null && _surface.LandArea > 0f) Repopulate();
        }

        void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
            _mesh = null;
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            Step(Time.deltaTime);
        }

        float Rand() => (float)_rnd.NextDouble();
        float Rand(float a, float b) => a + Rand() * (b - a);

        static bool Valid(Species s, float h) => h >= s.minH && h <= s.maxH;
        float Margin(Species s) => s.coastal ? Mathf.Min(shoreMargin, 0.06f) : shoreMargin;
        bool ValidTarget(Species s, float h) => h >= s.minH + Margin(s) && h <= s.maxH;
        bool CoastOk(Species s, Vector2 p) => !s.coastal || _life == null || _life.NearShore(p);
        // Walking is allowed onto any ground the species tolerates, and always uphill, so a herd caught on a
        // sinking beach can still climb out.
        static bool CanStep(Species s, float next, float current) => next >= s.minH || next > current + 1e-4f;

        bool TryRandomPos(Species s, out Vector2 p)
        {
            Rect b = _surface.LocalBounds;
            for (int i = 0; i < 40; i++)
            {
                p = new Vector2(b.xMin + Rand() * b.width, b.yMin + Rand() * b.height);
                if (ValidTarget(s, _surface.SampleHeight(p)) && !Burning(p) && (i >= 24 || !NearOtherHerd(p, null)) && (i >= 30 || CoastOk(s, p))) return true;
            }
            p = default;
            return false;
        }

        // Also true under settlement buildings, so every target search keeps herds out of the houses.
        bool Burning(Vector2 p) => _life != null && (_life.FireNear(p, 1.5f) || (_life.Settlement != null && _life.Settlement.Blocks(p)));

        // Other herds' centres and targets both count, so two herds do not walk into the same spot.
        bool NearOtherHerd(Vector2 p, Herd self)
        {
            float r2 = herdSpacing * herdSpacing;
            foreach (var h in _herds)
            {
                if (h == self) continue;
                if ((h.center - p).sqrMagnitude < r2 || (h.target - p).sqrMagnitude < r2) return true;
            }
            return false;
        }

        public int DesiredHerds()
        {
            float area = _surface.LandArea;
            if (area < minHerdArea) return 0;
            int n = Mathf.Max(1, Mathf.RoundToInt(area / areaPerHerd));
            if (Character == 2) n = Mathf.RoundToInt(n * 1.4f);
            return Mathf.Min(n, maxHerds);
        }

        float HerdSizeFactor => Character == 2 ? ancientHerdSize : Character == 3 ? barrenHerdSize : 1f;

        // Fresh herds from the seed; a repeat call for the same surface Version is a no-op. Sinking and merges
        // go through Step (Relocate + Rebalance), which only ever adds herds.
        public void Repopulate()
        {
            if (_surface == null) _surface = GetComponent<IIslandSurface>();
            if (_surface == null) return;
            _life ??= GetComponent<IslandLifeSystem>();
            if (_populated && _rnd != null && _version == _surface.Version) return;
            _rnd = new System.Random(HerdSeed);
            _herds.Clear();
            _version = _surface.Version;
            _populated = true;
            _peakArea = _surface.LandArea;
            _night = LifeEnvironment.NightAmount;
            Populations++;
            _burrows.Clear();
            _wasNight = _night > sleepThreshold;
            Rebalance();
            UpdateDetail();
            RebuildMesh();
            _meshTimer = 0f;
            _meshDirty = false;
        }

        Species PickSpecies(int biome)
        {
            float area = _surface.LandArea;
            float minScale = Character == 2 ? 0.7f : 1f;
            float total = 0f;
            for (int i = 0; i < Specs.Length; i++)
                if (!Excluded[i] && Specs[i].biome == biome && area >= Specs[i].minArea * minScale) total += Specs[i].weight;
            if (total <= 0f) return null;
            float r = Rand() * total;
            for (int i = 0; i < Specs.Length; i++)
            {
                if (Excluded[i] || Specs[i].biome != biome || area < Specs[i].minArea * minScale) continue;
                r -= Specs[i].weight;
                if (r <= 0f) return Specs[i];
            }
            return null;
        }

        // One bit set in BiomesPresent: the island is all one biome, as every island is before its first merge.
        bool OneBiome
        {
            get
            {
                int m = BiomesPresent;
                return m == 0 || (m & (m - 1)) == 0;
            }
        }

        void Rebalance()
        {
            int want = DesiredHerds();
            if (_herds.Count >= want) return;
            System.Array.Clear(Excluded, 0, Excluded.Length);
            System.Array.Clear(Misses, 0, Misses.Length);
            bool one = OneBiome;
            int guard = 0, rounds = one ? 32 : 64;
            while (_herds.Count < want && AnimalCount + 2 <= maxAnimals && guard++ < rounds)
            {
                Species s;
                Vector2 c;
                if (one)
                {
                    // Nothing to choose between: species first, then a spot, exactly as before the land carried
                    // the biome (same draws from _rnd, so every one-biome island keeps the herds it always had).
                    s = PickSpecies(Biome);
                    if (s == null) break;
                    if (!TryRandomPos(s, out c))
                    {
                        // No ground for this species (e.g. goats on a flat island): leave it out of this round.
                        Excluded[System.Array.IndexOf(Specs, s)] = true;
                        continue;
                    }
                }
                else if (!TryFound(out s, out c)) break;
                SpawnHerd(s, c);
            }
        }

        // A merged island: the ground decides the herd. A spot is drawn first, then the species comes from the
        // biome of the cell it falls on, so the island keeps producing reindeer on its nordic part and zebras on
        // its savanna part instead of the host's animals everywhere. A species that keeps landing on ground it
        // cannot use (goats on a flat island) drops out of this round after a few misses, as it did before.
        bool TryFound(out Species spec, out Vector2 pos)
        {
            Rect b = _surface.LocalBounds;
            for (int i = 0; i < 48; i++)
            {
                Vector2 p = new Vector2(b.xMin + Rand() * b.width, b.yMin + Rand() * b.height);
                float h = _surface.SampleHeight(p);
                if (h < GroundFloor || Burning(p)) continue;
                if (i < 30 && NearOtherHerd(p, null)) continue;
                var s = PickSpecies(BiomeAt(p));
                if (s == null) continue;
                if (!ValidTarget(s, h) || (i < 38 && !CoastOk(s, p)))
                {
                    int si = System.Array.IndexOf(Specs, s);
                    if (si >= 0 && ++Misses[si] >= 6) Excluded[si] = true;
                    continue;
                }
                spec = s;
                pos = p;
                return true;
            }
            spec = null;
            pos = default;
            return false;
        }

        void SpawnHerd(Species s, Vector2 c)
        {
            var herd = new Herd { spec = s, center = c, target = c, wait = Rand(s.stopMin * 0.3f, s.stopMax), growTimer = Rand(0f, growthInterval), thinkT = Mathf.Repeat(_herds.Count * 0.37f, 1f) * thinkInterval };
            int maxByArea = Mathf.Max(3, Mathf.FloorToInt(_surface.LandArea / 2f));
            int n = Mathf.Min(Mathf.Max(2, Mathf.RoundToInt(_rnd.Next(s.sizeMin, s.sizeMax + 1) * HerdSizeFactor)), Mathf.Min(maxByArea, s.maxSize));
            n = Mathf.Min(n, maxAnimals - AnimalCount);
            for (int m = 0; m < n; m++) AddMember(herd);
            // Watchers depend on the final herd size, so the poses are seeded once the herd is complete.
            for (int m = 0; m < herd.members.Count; m++) InitState(herd, herd.members[m], m);
            _herds.Add(herd);
        }

        void AddMember(Herd herd)
        {
            var s = herd.spec;
            Vector2 off = FormationOffset(herd, herd.members.Count);
            var a = new Animal { offset = off, pos = herd.center + off, yaw = Rand(0f, 360f), phase = Rand(0f, 6.28f), variant = _rnd.Next(LifeMeshes.Variants), scale = Rand(0.85f, 1.15f), age = s.grow };
            if (!Valid(s, _surface.SampleHeight(a.pos))) a.pos = herd.center;
            InitState(herd, a, herd.members.Count);
            herd.members.Add(a);
        }

        // The life seed on the same island is the same shapeSeed; the multiplier keeps the two streams apart.
        int HerdSeed => unchecked(seed * 31 + 0x5A17);

        // ---------------------------------------------------------- young

        static bool Young(Animal a) => a.growth < 1f;
        float SizeFactor(Animal a) => Mathf.Lerp(youngScale, 1f, a.growth);

        // Where a member belongs: adults on their formation slot, a young half to three quarters of a body
        // beside its parent's slot (not its parent's position, so a playing parent does not drag it along).
        Vector2 Slot(Herd herd, Animal a, float gather) =>
            a.parent != null ? herd.center + a.parent.offset * gather + a.offset : herd.center + a.offset * gather;

        Vector2 YoungOffset(Herd herd)
        {
            float ang = Rand(0f, Mathf.PI * 2f);
            return new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (herd.spec.body * animalScale * Rand(0.5f, 0.75f));
        }

        Animal RandomAdult(Herd herd, Animal except)
        {
            int adults = 0;
            foreach (var m in herd.members) if (m != except && !Young(m)) adults++;
            if (adults == 0) return null;
            int k = _rnd.Next(adults);
            foreach (var m in herd.members) if (m != except && !Young(m) && k-- == 0) return m;
            return null;
        }

        // A young without a (living, adult) parent picks a new one, or takes a formation slot of its own.
        void Adopt(Herd herd, Animal a, int index)
        {
            a.parent = RandomAdult(herd, a);
            a.offset = a.parent != null ? YoungOffset(herd) : FormationOffset(herd, index);
        }

        void Orphans(Herd herd)
        {
            for (int i = 0; i < herd.members.Count; i++)
            {
                var a = herd.members[i];
                if (a.parent != null && (Young(a.parent) || !herd.members.Contains(a.parent))) Adopt(herd, a, i);
            }
        }

        void Promote(Herd herd, Animal a, int index)
        {
            a.growth = 1f;
            a.parent = null;
            a.offset = FormationOffset(herd, index);
        }

        bool HasAwakeYoung(Herd herd)
        {
            foreach (var a in herd.members) if (Young(a) && IsAwake(a)) return true;
            return false;
        }

        // Growth is linear in age; the mesh only follows in growthRebuildStep steps and at adulthood.
        bool Grow(Herd herd, float dt)
        {
            bool changed = false;
            float growTime = Mathf.Max(herd.spec.grow, 1f);
            for (int i = 0; i < herd.members.Count; i++)
            {
                var a = herd.members[i];
                if (!Young(a)) continue;
                a.age += dt;
                a.growth = Mathf.Min(1f, a.age / growTime);
                if (!Young(a)) { Promote(herd, a, i); changed = true; }
                else if (a.growth - a.bakedGrowth >= growthRebuildStep) changed = true;
            }
            return changed;
        }

        // calm: not startled, fleeing, on a rising shore or huddling. The mature-ground rule (StageAt) is the
        // old "+1 member" growth condition and keeps barren or freshly risen land from breeding.
        bool TryBirth(Herd herd, float dt, bool calm)
        {
            herd.growTimer += dt;
            if (herd.growTimer < growthInterval) return false;
            herd.growTimer = 0f;
            if (!calm || _night >= wakeThreshold || Agitation >= huddleThreshold) return false;
            var s = herd.spec;
            if (herd.members.Count < 2 || herd.members.Count >= s.maxSize || AnimalCount >= maxAnimals) return false;
            if (_life == null || _life.StageAt(herd.center) <= 0.95f) return false;
            foreach (var a in herd.members) if (a.growth < youngIndependence) return false;
            if (Rand() >= growthChance) return false;
            Bear(herd);
            return true;
        }

        void Bear(Herd herd)
        {
            var s = herd.spec;
            var parent = RandomAdult(herd, null);
            var a = new Animal
            {
                parent = parent, growth = 0f, bakedGrowth = 0f, age = 0f,
                phase = Rand(0f, 6.28f), scale = Rand(0.85f, 1.15f),
                variant = parent != null ? parent.variant : _rnd.Next(LifeMeshes.Variants),
                yaw = parent != null ? parent.yaw : Rand(0f, 360f)
            };
            a.offset = parent != null ? YoungOffset(herd) : FormationOffset(herd, herd.members.Count);
            a.pos = Slot(herd, a, 1f);
            if (!Valid(s, _surface.SampleHeight(a.pos))) a.pos = parent != null ? parent.pos : herd.center;
            InitState(herd, a, herd.members.Count);
            herd.members.Add(a);
            Births++;
        }

        // Offline catch-up (IslandLifeSystem.Simulate hands over the span in herd seconds): young age and
        // may come of age, nothing is born while the app is closed.
        public void CatchUp(float seconds)
        {
            if (!(seconds > 0f) || _rnd == null || _surface == null) return;
            bool changed = false;
            foreach (var h in _herds) if (Grow(h, seconds)) changed = true;
            if (!changed) return;
            UpdateDetail();
            RebuildMesh();
            _meshTimer = 0f;
            _meshDirty = false;
        }

        // ---------------------------------------------------------- animal states

        bool Watcher(Herd h, int index)
        {
            if (watchersPerHerd <= 0) return false;
            int n = h.members.Count;
            return (index == 0 && n >= 6) || (watchersPerHerd > 1 && n >= 12 && index == n / 2);
        }

        // A plausible settled pose for a new, restored or far-then-near animal: asleep at night, grazing by day.
        void InitState(Herd herd, Animal a, int index)
        {
            var s = herd.spec;
            bool asleep = _night > sleepThreshold && !Watcher(herd, index);
            a.stands = s.standsToRest;
            a.state = asleep ? AnimalState.Sleep : AnimalState.Graze;
            a.timer = asleep ? Rand(5f, dawnStaggerMax) : Rand(s.grazeMin, s.grazeMax);
            a.pitchFrom = a.pitchTo = asleep ? (a.stands ? 0f : SleepPitch) : s.grazePitch;
            a.restFrom = a.restTo = asleep && !a.stands ? 1f : 0f;
            a.step = 0;
            a.dived = false;
            a.pitch = a.lift = 0f;
            a.alert = false;
            a.moving = false;
            a.gaitOn = true;
            a.t0 = _clock - 10f;
            a.act = AnimalActivity.None;
            a.hasGoal = a.arrived = a.hidden = a.shore = false;
            a.roll = a.wallowIn = a.blocked = 0f;
        }

        void SeedStates()
        {
            foreach (var h in _herds)
            {
                EndPlay(h, false);
                ResetBehaviour(h);
                for (int i = 0; i < h.members.Count; i++) InitState(h, h.members[i], i);
            }
            _meshDirty = true;
        }

        // Freezes the blend that is in progress as the new "from" so a change mid-transition never pops.
        void Stamp(Animal a)
        {
            float k = transitionTime > 0f ? Mathf.Clamp01((_clock - a.t0) / transitionTime) : 1f;
            a.pitchFrom = Mathf.Lerp(a.pitchFrom, a.pitchTo, k);
            a.restFrom = Mathf.Lerp(a.restFrom, a.restTo, k);
            a.t0 = _clock;
        }

        bool SetState(Animal a, AnimalState st, float timer, float pitch, bool alert)
        {
            a.timer = timer;
            bool lying = st == AnimalState.Rest || st == AnimalState.Sleep;
            if (lying && a.stands) pitch = 0f;
            if (a.state == st && a.pitchTo == pitch && a.alert == alert) return false;
            Stamp(a);
            a.state = st;
            a.pitchTo = pitch;
            a.restTo = lying && !a.stands ? 1f : 0f;
            a.alert = alert;
            StateChanges++;
            return true;
        }

        bool SetMoving(Animal a, bool moving)
        {
            if (a.moving == moving) return false;
            a.moving = moving;
            Stamp(a);
            return true;
        }

        // Idle states while the herd stands: graze -> look up / rest -> graze; at night non-watchers go
        // rest -> sleep and sleepers only count down their dawn stagger once the night is over.
        bool IdleStep(Herd herd, Animal a, int index, float dt)
        {
            var s = herd.spec;
            if (a.state == AnimalState.Sleep)
            {
                if (_night >= wakeThreshold) return false;
                a.timer -= dt;
                if (a.timer > 0f) return false;
                return SetState(a, AnimalState.Look, Rand(2f, 4f), StretchPitch, true);
            }
            a.timer -= dt;
            if (a.timer > 0f) return false;
            if (_night > sleepThreshold && !Watcher(herd, index))
            {
                if (a.state == AnimalState.Rest) return SetState(a, AnimalState.Sleep, Rand(5f, dawnStaggerMax), SleepPitch, false);
                if (Rand() < 0.5f) return SetState(a, AnimalState.Rest, Rand(3f, 8f), RestPitch, false);
                return SetState(a, AnimalState.Graze, Rand(2f, 6f), s.grazePitch, false);
            }
            bool canRest = herd.wait > s.restMin * 0.6f;
            // Young graze in shorter bouts and look about more; play is where their surplus goes (StartPlay).
            float graze = Young(a) ? 0.6f : 1f;
            switch (a.state)
            {
                case AnimalState.Graze:
                    if (Rand() < s.lookChance)
                    {
                        a.yaw = Mathf.Repeat(a.yaw + Rand(-35f, 35f), 360f);
                        return SetState(a, AnimalState.Look, Rand(s.lookMin, s.lookMax), Rand() < 0.3f ? StretchPitch : LookPitch, true);
                    }
                    if (canRest && Rand() < s.restChance)
                    {
                        if (s.kind == LifeKind.Ox && Rand() < 0.4f * behaviourRate) a.wallowIn = Rand(2f, 5f);
                        return SetState(a, AnimalState.Rest, Rand(s.restMin, s.restMax), RestPitch, false);
                    }
                    return SetState(a, AnimalState.Graze, Rand(s.grazeMin, s.grazeMax) * graze, s.grazePitch, false);
                case AnimalState.Look:
                    if (canRest && Rand() < s.restChance * 0.5f) return SetState(a, AnimalState.Rest, Rand(s.restMin, s.restMax), RestPitch, false);
                    return SetState(a, AnimalState.Graze, Rand(s.grazeMin, s.grazeMax) * graze, s.grazePitch, false);
                case AnimalState.Rest:
                    return SetState(a, AnimalState.Look, Rand(1f, 2.5f), LookPitch, true);
                default:
                    return Rand() < 0.4f
                        ? SetState(a, AnimalState.Look, Rand(s.lookMin, s.lookMax), LookPitch, true)
                        : SetState(a, AnimalState.Graze, Rand(s.grazeMin, s.grazeMax) * graze, s.grazePitch, false);
            }
        }

        bool AnySleeping(Herd herd)
        {
            foreach (var a in herd.members) if (a.state == AnimalState.Sleep) return true;
            return false;
        }

        // ---------------------------------------------------------- play

        bool IsAwake(Animal a) => a.state == AnimalState.Graze || a.state == AnimalState.Look || a.state == AnimalState.Walk;

        // Two or more awake young race as a pack, goats spar in adult pairs, everything else is the chase:
        // awake young take the pair first (it is their game), random awake members fill the rest.
        void StartPlay(Herd herd)
        {
            int n = herd.members.Count;
            int a = -1, b = -1;
            if (behaviourRate > 0f && (herd.spec.kind == LifeKind.Goat || herd.spec.kind == LifeKind.Reindeer) && Rand() < 0.6f)
            {
                for (int i = 0; i < 12 && b < 0; i++)
                {
                    int k = _rnd.Next(n);
                    var m = herd.members[k];
                    if (!IsAwake(m) || Young(m) || m.act != AnimalActivity.None || k == a) continue;
                    if (a < 0) a = k; else b = k;
                }
                if (b >= 0) { StartSpar(herd, a, b); return; }
                a = b = -1;
            }
            int racers = 0;
            for (int i = 0; i < n; i++)
            {
                var m = herd.members[i];
                if (!Young(m) || !IsAwake(m) || m.act != AnimalActivity.None) continue;
                racers++;
                if (a < 0) a = i; else if (b < 0) b = i;
            }
            bool race = behaviourRate > 0f && racers >= 2;
            for (int i = 0; i < 8 && b < 0; i++)
            {
                int k = _rnd.Next(n);
                if (!IsAwake(herd.members[k]) || herd.members[k].act != AnimalActivity.None || k == a) continue;
                if (a < 0) a = k; else b = k;
            }
            if (a < 0 || b < 0) return;
            if (Young(herd.members[a]) || Young(herd.members[b])) YoungPlays++;
            herd.playA = a;
            herd.playB = b;
            herd.playKind = race ? PlayKind.Race : PlayKind.Chase;
            herd.playT = Rand(playDurationMin, playDurationMax);
            Vector2 rel = herd.members[a].pos - herd.center;
            herd.playAng = rel.sqrMagnitude > 1e-6f ? Mathf.Atan2(rel.y, rel.x) : Rand(0f, 6.28f);
            int joined = 0;
            for (int i = 0; i < n; i++)
            {
                var m = herd.members[i];
                bool inPair = i == a || i == b;
                if (!inPair && !(race && joined < 4 && Young(m) && IsAwake(m) && m.act == AnimalActivity.None)) continue;
                joined++;
                SetState(m, AnimalState.Play, 0f, WalkPitch, false);
                SetMoving(m, true);
                if (race) m.act = AnimalActivity.Race;
            }
            PlaysStarted++;
            if (race) Races++;
            Moment(race ? MomentKind.Race : MomentKind.Play, herd);
        }

        void StartSpar(Herd herd, int a, int b)
        {
            var ma = herd.members[a];
            var mb = herd.members[b];
            herd.playA = a;
            herd.playB = b;
            herd.playKind = PlayKind.Spar;
            herd.playT = 25f;
            herd.sparMid = (ma.pos + mb.pos) * 0.5f;
            Vector2 axis = mb.pos - ma.pos;
            herd.sparAxis = axis.sqrMagnitude > 1e-6f ? axis.normalized : new Vector2(1f, 0f);
            herd.sparPhase = 0;
            herd.sparRounds = _rnd.Next(3, 6);
            herd.sparT = 0.5f;
            ma.act = mb.act = AnimalActivity.Spar;
            SetState(ma, AnimalState.Play, 0f, WalkPitch, false);
            SetState(mb, AnimalState.Play, 0f, WalkPitch, false);
            PlaysStarted++;
            Spars++;
            Moment(MomentKind.Spar, herd);
        }

        void EndPlay(Herd herd, bool look = true)
        {
            foreach (var a in herd.members)
            {
                if (a.state != AnimalState.Play) continue;
                SetMoving(a, false);
                if (a.act == AnimalActivity.Spar || a.act == AnimalActivity.Race) a.act = AnimalActivity.None;
                if (look) SetState(a, AnimalState.Look, Rand(1f, 2f), LookPitch, true);
            }
            herd.playA = herd.playB = -1;
            herd.playT = 0f;
        }

        bool MoveToward(Species s, Animal a, Vector2 goal, float step, float stopDist)
        {
            Vector2 to = goal - a.pos;
            float d = to.magnitude;
            if (d <= stopDist) return true;
            Vector2 next = a.pos + to / d * Mathf.Min(step, d - stopDist);
            if (!CanStep(s, _surface.SampleHeight(next), _surface.SampleHeight(a.pos))) return false;
            a.pos = next;
            a.yaw = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
            return true;
        }

        // The runner circles the herd at formation radius + 2.5 bodies, the chaser follows it and stops a
        // body length short; blocked ground or an expired timer ends the game and both look up, then walk back.
        // A race is the same loop, wider and faster, with every racer chasing the one in front of it.
        void StepPlay(Herd herd, float dt)
        {
            int n = herd.members.Count;
            if (herd.playA < 0 || herd.playA >= n || herd.playB < 0 || herd.playB >= n || herd.playA == herd.playB) { EndPlay(herd); return; }
            var runner = herd.members[herd.playA];
            var chaser = herd.members[herd.playB];
            if (runner.state != AnimalState.Play || chaser.state != AnimalState.Play) { EndPlay(herd); return; }
            herd.playT -= dt;
            if (herd.playT <= 0f) { EndPlay(herd); return; }
            if (herd.playKind == PlayKind.Spar) { StepSpar(herd, runner, chaser, dt); return; }
            var s = herd.spec;
            float body = s.body * animalScale;
            bool race = herd.playKind == PlayKind.Race;
            float r = body * formationSpacing * Mathf.Sqrt(n) + body * (race ? 3.5f : 2.5f);
            float v = s.speed * playSpeed * (race ? 1.25f : 1f);
            herd.playAng += v / r * dt;
            Vector2 goal = herd.center + new Vector2(Mathf.Cos(herd.playAng), Mathf.Sin(herd.playAng)) * r;
            if (!MoveToward(s, runner, goal, v * dt, body * 0.1f)) { EndPlay(herd); return; }
            if (!race)
            {
                if (!MoveToward(s, chaser, runner.pos, v * 1.05f * dt, body * 1.2f)) EndPlay(herd);
                return;
            }
            var ahead = runner;
            foreach (var m in herd.members)
            {
                if (m == runner || m.state != AnimalState.Play) continue;
                if (!MoveToward(s, m, ahead.pos, v * 1.05f * dt, body * 0.9f)) { EndPlay(herd); return; }
                ahead = m;
            }
        }

        // Head-butting: both back off to 1.5 bodies from the middle, paw for a moment, charge with the head down
        // to half a body, hold the clash, and again for three to five rounds; they face each other throughout.
        void StepSpar(Herd herd, Animal a, Animal b, float dt)
        {
            if (herd.spec.kind == LifeKind.Reindeer) { StepWrestle(herd, a, b, dt); return; }
            var s = herd.spec;
            float body = s.body * animalScale;
            bool charge = herd.sparPhase == 1;
            float gap = body * (charge ? 0.5f : 1.5f);
            float v = s.speed * (charge ? 3.2f : 1.1f) * dt;
            bool ra = StepTo(s, a, herd.sparMid - herd.sparAxis * gap, v, out bool blockedA);
            bool rb = StepTo(s, b, herd.sparMid + herd.sparAxis * gap, v, out bool blockedB);
            if (blockedA || blockedB) { EndPlay(herd); return; }
            a.yaw = Mathf.Atan2(herd.sparAxis.x, herd.sparAxis.y) * Mathf.Rad2Deg;
            b.yaw = Mathf.Repeat(a.yaw + 180f, 360f);
            float pitch = charge ? ButtPitch : WalkPitch;
            SetState(a, AnimalState.Play, 0f, pitch, false);
            SetState(b, AnimalState.Play, 0f, pitch, false);
            SetMoving(a, !ra);
            SetMoving(b, !rb);
            if (!ra || !rb) return;
            herd.sparT -= dt;
            if (herd.sparT > 0f) return;
            if (charge && --herd.sparRounds <= 0) { EndPlay(herd); return; }
            herd.sparPhase = charge ? 0 : 1;
            herd.sparT = charge ? 0.6f : 0.35f;
        }

        // Reindeer do not charge like goats: they lock antlers, then shove each other to and fro while the locked
        // pair slowly twists round, legs working, for six to nine seconds.
        void StepWrestle(Herd herd, Animal a, Animal b, float dt)
        {
            var s = herd.spec;
            float body = s.body * animalScale;
            float gap = body * 0.5f;
            if (herd.sparPhase == 0)
            {
                float v = s.speed * 1.1f * dt;
                bool ra = StepTo(s, a, herd.sparMid - herd.sparAxis * gap, v, out bool blockedA);
                bool rb = StepTo(s, b, herd.sparMid + herd.sparAxis * gap, v, out bool blockedB);
                if (blockedA || blockedB) { EndPlay(herd); return; }
                a.yaw = Mathf.Atan2(herd.sparAxis.x, herd.sparAxis.y) * Mathf.Rad2Deg;
                b.yaw = Mathf.Repeat(a.yaw + 180f, 360f);
                SetState(a, AnimalState.Play, 0f, ButtPitch, false);
                SetState(b, AnimalState.Play, 0f, ButtPitch, false);
                SetMoving(a, !ra);
                SetMoving(b, !rb);
                if (!ra || !rb) return;
                herd.sparPhase = 1;
                herd.sparT = Rand(6f, 9f);
                herd.playAng = 0f;
                return;
            }
            herd.sparT -= dt;
            if (herd.sparT <= 0f) { EndPlay(herd); return; }
            herd.playAng += dt;
            float twist = herd.playAng * 0.35f * ((herd.sparRounds & 1) == 0 ? 1f : -1f);
            float cs = Mathf.Cos(twist), sn = Mathf.Sin(twist);
            Vector2 axis = new Vector2(herd.sparAxis.x * cs - herd.sparAxis.y * sn, herd.sparAxis.x * sn + herd.sparAxis.y * cs);
            Vector2 mid = herd.sparMid + axis * (Mathf.Sin(herd.playAng * 1.9f) * body * 0.3f);
            Vector2 pa = mid - axis * gap, pb = mid + axis * gap;
            if (!CanStep(s, _surface.SampleHeight(pa), _surface.SampleHeight(a.pos)) || !CanStep(s, _surface.SampleHeight(pb), _surface.SampleHeight(b.pos))) { EndPlay(herd); return; }
            a.pos = pa;
            b.pos = pb;
            a.yaw = Mathf.Atan2(axis.x, axis.y) * Mathf.Rad2Deg;
            b.yaw = Mathf.Repeat(a.yaw + 180f, 360f);
            SetState(a, AnimalState.Play, 0f, ButtPitch + 6f, false);
            SetState(b, AnimalState.Play, 0f, ButtPitch + 6f, false);
            SetMoving(a, true);
            SetMoving(b, true);
        }

        // Moves without turning; true once the goal is reached.
        bool StepTo(Species s, Animal a, Vector2 goal, float step, out bool blocked)
        {
            blocked = false;
            Vector2 to = goal - a.pos;
            float d = to.magnitude;
            if (d <= 1e-4f) return true;
            Vector2 next = a.pos + to / d * Mathf.Min(step, d);
            if (!CanStep(s, _surface.SampleHeight(next), _surface.SampleHeight(a.pos))) { blocked = true; return false; }
            a.pos = next;
            return d <= step;
        }

        // ---------------------------------------------------------- detail LOD

        bool UpdateDetail() => UpdateDetail(LifeLod.Distance(transform.position));

        // Picks the detailed set (see detailNear); true when it changed, which is the only time the LOD costs a
        // mesh rebuild. The island-level early-out uses the planar tier distance, which is never larger than a
        // view distance, so an island out of range costs one comparison.
        bool UpdateDetail(float islandDistance)
        {
            if (_surface == null) return false;
            bool changed = false;
            if (maxDetailed <= 0 || islandDistance - _surface.BoundingRadius > detailFar)
            {
                if (_detailedCount == 0) return false;
                foreach (var h in _herds)
                    foreach (var a in h.members)
                        if (a.detailed) { a.detailed = false; changed = true; }
                _detailedCount = 0;
                if (changed) DetailChanges++;
                return changed;
            }
            if (_lodPick.Length < maxDetailed)
            {
                _lodPick = new Animal[maxDetailed];
                _lodKey = new float[maxDetailed];
                _lodVerts = new int[maxDetailed];
            }
            int cap = maxDetailed, count = 0;
            float stick = detailFar - detailNear;
            var m = transform.localToWorldMatrix;
            foreach (var h in _herds)
                foreach (var a in h.members)
                {
                    a.wantDetail = false;
                    if (a.hidden || a.dived) continue;
                    float d = LifeEnvironment.ViewDistance(m.MultiplyPoint3x4(new Vector3(a.pos.x, 0f, a.pos.y)));
                    if (!(d < detailNear || (a.detailed && d < detailFar))) continue;
                    float key = a.detailed ? d - stick : d;
                    if (count == cap && key >= _lodKey[cap - 1]) continue;
                    int k = count < cap ? count++ : cap - 1;
                    while (k > 0 && _lodKey[k - 1] > key)
                    {
                        _lodKey[k] = _lodKey[k - 1];
                        _lodPick[k] = _lodPick[k - 1];
                        _lodVerts[k] = _lodVerts[k - 1];
                        k--;
                    }
                    _lodKey[k] = key;
                    _lodPick[k] = a;
                    _lodVerts[k] = LifeMeshes.GetDetailTemplate(h.spec.kind, a.variant, a.growth < youngModelGrowth).vertices.Length;
                }
            int kept = 0, verts = 0;
            while (kept < count && verts + _lodVerts[kept] <= maxDetailVertices)
            {
                verts += _lodVerts[kept];
                _lodPick[kept++].wantDetail = true;
            }
            System.Array.Clear(_lodPick, 0, count);
            count = kept;
            foreach (var h in _herds)
                foreach (var a in h.members)
                    if (a.wantDetail != a.detailed) { a.detailed = a.wantDetail; changed = true; }
            _detailedCount = count;
            if (changed) DetailChanges++;
            return changed;
        }

        // ---------------------------------------------------------- behaviours

        bool Noon
        {
            get
            {
                float t = LifeEnvironment.TimeOfDay;
                return t >= 0f && Mathf.Abs(t - 0.5f) < noonWindow;
            }
        }

        static bool ErrandAct(AnimalActivity act) =>
            act == AnimalActivity.Drink || act == AnimalActivity.Wade || act == AnimalActivity.Shade || act == AnimalActivity.Watch
            || act == AnimalActivity.Slide || act == AnimalActivity.Parade || act == AnimalActivity.OneLeg || act == AnimalActivity.Browse
            || act == AnimalActivity.Soak || act == AnimalActivity.Circle
            || act == AnimalActivity.Stroll || act == AnimalActivity.Visit || act == AnimalActivity.Spread
            || IsSignature(act);

        static void ClearGoal(Animal a)
        {
            a.hasGoal = a.arrived = a.dived = false;
            a.blocked = 0f;
            a.step = 0;
            a.pitch = a.lift = a.roll = 0f;
            a.act = AnimalActivity.None;
            if (a.timer > 100f) a.timer = 1f;
        }

        void SetGoal(Animal a, Vector2 goal, AnimalActivity act)
        {
            a.goal = goal;
            a.hasGoal = true;
            a.arrived = false;
            a.blocked = 0f;
            a.act = act;
        }

        void CancelErrand(Herd herd)
        {
            if (herd.errand == Errand.None) return;
            if (herd.errand == Errand.Watch) herd.watchCool = Rand(40f, 60f);
            else herd.errandCool = Rand(40f, 70f);
            herd.errand = Errand.None;
            herd.errandStage = 0;
            herd.visit = null;
            herd.sigA = herd.sigB = null;
            foreach (var a in herd.members) if (ErrandAct(a.act)) ClearGoal(a);
        }

        void ReleaseLookout(Herd herd)
        {
            if (herd.lookout == null) return;
            if (herd.lookout.act == AnimalActivity.Lookout || herd.lookout.act == AnimalActivity.Sentry) ClearGoal(herd.lookout);
            herd.lookout = null;
            herd.lookoutCool = Rand(8f, 15f);
        }

        void StopDig(Herd herd)
        {
            if (herd.digger == null) return;
            if (herd.digger.act == AnimalActivity.Dig) herd.digger.act = AnimalActivity.None;
            herd.digger = null;
        }

        // Everything the behaviour layer holds for a herd; far-tier drifting, re-seeding and merges start clean.
        void ResetBehaviour(Herd herd)
        {
            herd.errand = Errand.None;
            herd.errandStage = 0;
            herd.diving = herd.walking = false;
            herd.lookout = herd.digger = null;
            herd.visit = null;
            herd.sigA = herd.sigB = null;
            herd.choreoStep = 0;
            herd.choreoT = herd.stampT = herd.tuckT = 0f;
            foreach (var a in herd.members)
            {
                a.act = AnimalActivity.None;
                a.hasGoal = a.arrived = a.hidden = a.shore = a.dived = false;
                a.roll = a.wallowIn = a.pitch = a.lift = 0f;
                a.step = 0;
            }
        }

        // Goal-driven members may step below their species' ground: waders down to the wade floor, shore
        // visitors (and anyone on the way back from there) onto the wet sand.
        bool CanStepMember(Species s, Animal a, float next, float current)
        {
            float floor = s.minH;
            if (a.shore) floor = a.act == AnimalActivity.Drink ? DrinkLow - 0.005f : WadeFloor + 0.005f;
            return next >= floor || next > current + 1e-4f;
        }

        // From `from` along dir to the first ground at or below hi (10 cm steps, then 2 cm), and on towards the
        // middle of the band; false when the ground drops straight past lo (a cliff) or the band is out of reach.
        bool TryWaterline(Vector2 from, Vector2 dir, float lo, float hi, float maxDist, out Vector2 p)
        {
            p = default;
            int coarse = Mathf.CeilToInt(maxDist / 0.1f);
            for (int i = 0; i <= coarse; i++)
            {
                if (_surface.SampleHeight(from + dir * (0.1f * i)) > hi) continue;
                float start = Mathf.Max(0f, 0.1f * (i - 1));
                float mid = (lo + hi) * 0.5f;
                bool found = false;
                for (int k = 0; k <= 12; k++)
                {
                    Vector2 c = from + dir * (start + 0.02f * k);
                    float h = _surface.SampleHeight(c);
                    if (h > hi) continue;
                    if (h < lo) break;
                    p = c;
                    found = true;
                    if (h <= mid) break;
                }
                return found;
            }
            return false;
        }

        // The nearest shore in eight directions: dry = the last ground the herd itself may stand on (the herd
        // centre never leaves valid ground, so the rising-shore rule stays quiet), water = a spot in the band
        // at most shoreWalk further on for the members to walk down to.
        bool TryFindShore(Herd herd, float lo, float hi, out Vector2 dry, out Vector2 water, out Vector2 dir)
        {
            var s = herd.spec;
            dry = water = dir = default;
            float best = float.MaxValue;
            float phase = Rand(0f, 6.28f);
            for (int k = 0; k < 8; k++)
            {
                float ang = phase + k * (Mathf.PI * 0.25f);
                Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Vector2 lastDry = herd.center;
                for (float t = 0.4f; t <= shoreSearch && t < best; t += 0.4f)
                {
                    Vector2 c = herd.center + d * t;
                    float h = _surface.SampleHeight(c);
                    if (h > s.maxH || Burning(c)) break;
                    if (ValidTarget(s, h)) { lastDry = c; continue; }
                    if ((c - lastDry).magnitude > shoreWalk) break;
                    if (h > hi) continue;
                    if (TryWaterline(c - d * 0.4f, d, lo, hi, 0.45f, out Vector2 w) && (w - lastDry).magnitude <= shoreWalk)
                    {
                        best = t;
                        dry = lastDry;
                        water = w;
                        dir = d;
                    }
                    break;
                }
            }
            return best < float.MaxValue;
        }

        bool StartShoreErrand(Herd herd, Errand errand)
        {
            bool wade = errand != Errand.Drink;
            if (!TryFindShore(herd, wade ? WadeFloor + 0.015f : DrinkLow, wade ? WadeHigh : DrinkHigh, out Vector2 dry, out Vector2 water, out Vector2 dir))
            {
                herd.errandCool = Rand(15f, 30f);
                return false;
            }
            herd.errand = errand;
            herd.errandStage = 0;
            herd.errandDir = dir;
            herd.errandPos = water;
            herd.target = dry;
            herd.wait = 0f;
            ErrandsStarted++;
            Moment(MomentKind.Errand, herd);
            return true;
        }

        bool StartShade(Herd herd)
        {
            var s = herd.spec;
            if (_life == null) return false;
            if (!_life.TryRandomPlant(LifeKind.Tree, _rnd, herd.center, shadeRange * 0.5f, out Vector2 tree)
                && !_life.TryRandomPlant(LifeKind.Tree, _rnd, herd.center, shadeRange, out tree))
            {
                herd.errandCool = Rand(10f, 20f);
                return false;
            }
            Vector2 away = herd.center - tree;
            Vector2 spot = tree + (away.sqrMagnitude > 1e-4f ? away.normalized : new Vector2(1f, 0f)) * 0.15f;
            if (!OkSpot(s, spot))
            {
                herd.errandCool = Rand(10f, 20f);
                return false;
            }
            herd.errand = Errand.Shade;
            herd.errandStage = 0;
            herd.target = spot;
            herd.wait = 0f;
            ErrandsStarted++;
            Moment(MomentKind.Errand, herd);
            return true;
        }

        // The herd reached its errand spot: the members get their places.
        void ArriveErrand(Herd herd)
        {
            herd.errandStage = 1;
            switch (herd.errand)
            {
                case Errand.Drink:
                    herd.wait = Rand(12f, 20f);
                    AssignShoreGoals(herd, DrinkLow, DrinkHigh, AnimalActivity.Drink, true);
                    break;
                case Errand.Wade:
                    herd.wait = Rand(25f, 45f);
                    AssignShoreGoals(herd, WadeFloor + 0.015f, WadeHigh, AnimalActivity.Wade, false);
                    break;
                case Errand.Shade:
                    herd.wait = Rand(25f, 45f);
                    foreach (var a in herd.members) if (a.act == AnimalActivity.None || a.act == AnimalActivity.Line) a.act = AnimalActivity.Shade;
                    break;
                case Errand.Soak:
                    herd.wait = Rand(30f, 50f);
                    AssignShoreGoals(herd, WadeFloor + 0.015f, WadeHigh, AnimalActivity.Soak, true);
                    break;
                case Errand.Slide:
                    herd.wait = Rand(24f, 32f);
                    AssignSlide(herd);
                    break;
                case Errand.Parade:
                    herd.wait = 60f;
                    herd.choreoStep = ParadeGather;
                    herd.choreoT = 0f;
                    AssignShoreGoals(herd, WadeFloor + 0.015f, WadeHigh, AnimalActivity.Parade, true, 1.05f);
                    foreach (var a in herd.members) if (a.act == AnimalActivity.Parade) a.from = a.goal;
                    break;
                case Errand.Browse:
                    herd.wait = Rand(22f, 35f);
                    AssignBrowse(herd);
                    break;
                case Errand.Stroll:
                    // Each arrival is the start of the next leg; the last one ends in a short rest.
                    if (NextStrollLeg(herd))
                    {
                        herd.errandStage = 0;
                        herd.wait = Rand(0.2f, 1f);
                    }
                    else herd.wait = Rand(4f, 8f);
                    break;
                case Errand.Visit:
                    herd.wait = Rand(18f, 28f);
                    AssignVisit(herd);
                    break;
                case Errand.Signature:
                    ArriveSignature(herd);
                    break;
            }
        }

        // ---------------------------------------------------------- patterns of every species

        // Along the shore in legs of strollLeg: from a point that far along the coast and a little inland, the
        // last ground the herd may stand on towards the water is the next stop, so the walk follows the coastline.
        bool StartStroll(Herd herd)
        {
            if (herd.members.Count < 2 || !TryFindCoast(herd, out Vector2 dry, out Vector2 dir))
            {
                herd.errandCool = Rand(15f, 30f);
                return false;
            }
            herd.errand = Errand.Stroll;
            herd.errandStage = 0;
            herd.errandDir = dir;
            herd.target = dry;
            herd.wait = 0f;
            herd.legs = 3 + _rnd.Next(3);
            herd.side = Rand() < 0.5f ? -1 : 1;
            ErrandsStarted++;
            Moment(MomentKind.Errand, herd);
            return true;
        }

        // The nearest edge of the herd's own ground towards lower land - the coast, not a mountain side - in eight
        // directions out to twice shoreSearch. dir points from the herd's ground towards the water.
        bool TryFindCoast(Herd herd, out Vector2 dry, out Vector2 dir)
        {
            var sp = herd.spec;
            dry = dir = default;
            float best = float.MaxValue;
            float phase = Rand(0f, 6.28f);
            for (int k = 0; k < 8; k++)
            {
                float ang = phase + k * (Mathf.PI * 0.25f);
                Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Vector2 last = herd.center;
                for (float t = 0.3f; t <= shoreSearch * 2f && t < best; t += 0.3f)
                {
                    Vector2 c = herd.center + d * t;
                    float h = _surface.SampleHeight(c);
                    if (ValidTarget(sp, h) && !Burning(c)) { last = c; continue; }
                    if (h < sp.minH + Margin(sp))
                    {
                        best = t;
                        dry = last;
                        dir = d;
                    }
                    break;
                }
            }
            return best < float.MaxValue;
        }

        bool NextStrollLeg(Herd herd)
        {
            if (herd.legs <= 0) return false;
            var sp = herd.spec;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Vector2 along = new Vector2(-herd.errandDir.y, herd.errandDir.x) * herd.side;
                Vector2 probe = herd.center + along * strollLeg - herd.errandDir * 1.2f;
                if (ValidTarget(sp, _surface.SampleHeight(probe)) && !Burning(probe))
                {
                    Vector2 lastDry = probe;
                    for (float t = 0.3f; t <= shoreSearch; t += 0.3f)
                    {
                        Vector2 c = probe + herd.errandDir * t;
                        float h = _surface.SampleHeight(c);
                        if (!ValidTarget(sp, h) || Burning(c)) break;
                        lastDry = c;
                    }
                    Vector2 step = lastDry - herd.center;
                    if (step.sqrMagnitude > 0.25f)
                    {
                        // The coast turns: the water now lies across the direction just walked.
                        Vector2 toWater = lastDry - probe;
                        if (toWater.sqrMagnitude > 0.04f) herd.errandDir = Vector2.Lerp(herd.errandDir, toWater.normalized, 0.5f).normalized;
                        herd.target = lastDry;
                        herd.legs--;
                        return true;
                    }
                }
                herd.side = -herd.side;
            }
            return false;
        }

        // To a neighbouring herd within visitRange (any species), stopping at the edge of it.
        bool StartVisit(Herd herd)
        {
            if (herd.members.Count < 2 || _herds.Count < 2) { herd.errandCool = Rand(20f, 40f); return false; }
            Herd best = null;
            float bestD = visitRange * visitRange;
            foreach (var other in _herds)
            {
                if (other == herd || other.members.Count == 0 || other.fleeing || other.errand == Errand.Visit) continue;
                float d = (other.center - herd.center).sqrMagnitude;
                if (d >= bestD) continue;
                bestD = d;
                best = other;
            }
            if (best == null) { herd.errandCool = Rand(20f, 40f); return false; }
            Vector2 away = herd.center - best.center;
            float dist = away.magnitude;
            Vector2 dir = dist > 1e-3f ? away / dist : new Vector2(1f, 0f);
            float edge = RadiusOf(best) + RadiusOf(herd) * 0.6f + 0.3f;
            Vector2 spot = best.center + dir * Mathf.Min(edge, Mathf.Max(0.5f, dist));
            if (!OkSpot(herd.spec, spot)) { herd.errandCool = Rand(15f, 30f); return false; }
            herd.errand = Errand.Visit;
            herd.errandStage = 0;
            herd.visit = best;
            herd.errandDir = -dir;
            herd.errandPos = best.center;
            herd.target = spot;
            herd.wait = 0f;
            ErrandsStarted++;
            Moment(MomentKind.Errand, herd);
            return true;
        }

        // About half of the grown-ups walk in among the visited herd; everyone faces it. The host waits as long.
        void AssignVisit(Herd herd)
        {
            var host = herd.visit;
            if (host == null || !_herds.Contains(host) || host.members.Count == 0) { herd.wait = Rand(2f, 4f); return; }
            host.wait = Mathf.Max(host.wait, herd.wait);
            herd.errandPos = host.center;
            float r = RadiusOf(host);
            int k = 0;
            foreach (var a in herd.members)
            {
                if (a.hidden) continue;
                if (!Young(a) && (k++ & 1) == 0)
                {
                    for (int tries = 0; tries < 4; tries++)
                    {
                        float ang = Rand(0f, Mathf.PI * 2f);
                        Vector2 g = host.center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (r * Rand(0.2f, 0.85f));
                        if (!OkSpot(herd.spec, g)) continue;
                        SetGoal(a, g, AnimalActivity.Visit);
                        break;
                    }
                }
                if (!a.hasGoal && !Young(a))
                {
                    a.act = AnimalActivity.Visit;
                    Vector2 to = host.center - a.pos;
                    if (to.sqrMagnitude > 1e-4f) a.yaw = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
                }
            }
            YoungFollow(herd, AnimalActivity.Visit);
        }

        // Fan out in an arc ahead of the herd, each grown-up to its own patch, and graze there a while.
        bool StartSpread(Herd herd)
        {
            int n = herd.members.Count;
            if (n < 3) return false;
            var sp = herd.spec;
            float reach = RadiusOf(herd) * 2.4f + 1f;
            float baseAng = Rand(0f, Mathf.PI * 2f);
            int placed = 0, adults = 0;
            foreach (var a in herd.members) if (!a.hidden && !Young(a)) adults++;
            if (adults < 3) return false;
            int k = 0;
            foreach (var a in herd.members)
            {
                if (a.hidden || Young(a)) continue;
                float f = adults > 1 ? k++ / (float)(adults - 1) - 0.5f : 0f;
                float ang = baseAng + f * 2.6f;
                for (int tries = 0; tries < 4; tries++)
                {
                    Vector2 g = herd.center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (reach * Rand(0.65f, 1.05f) * (1f - 0.2f * tries));
                    if (!OkSpot(sp, g)) continue;
                    SetGoal(a, g, AnimalActivity.Spread);
                    placed++;
                    break;
                }
            }
            YoungFollow(herd, AnimalActivity.Spread);
            if (placed < 2)
            {
                foreach (var a in herd.members) if (a.act == AnimalActivity.Spread) ClearGoal(a);
                herd.errandCool = Rand(15f, 30f);
                return false;
            }
            herd.errand = Errand.Spread;
            herd.errandStage = 1;
            herd.errandDir = new Vector2(Mathf.Cos(baseAng), Mathf.Sin(baseAng));
            herd.target = herd.center;
            herd.wait = Rand(20f, 32f);
            ErrandsStarted++;
            Moment(MomentKind.Errand, herd);
            return true;
        }

        // Errands at the waterline depend on exactly where the water is, so any reshape ends them; the others go on
        // while every spot they use is still ground the species may stand on.
        bool SurvivesReshape(Herd herd)
        {
            switch (herd.errand)
            {
                case Errand.None:
                    return true;
                case Errand.Signature:
                    return SignatureSurvivesReshape(herd);
                case Errand.Stroll:
                case Errand.Visit:
                case Errand.Spread:
                case Errand.Shade:
                case Errand.Browse:
                case Errand.Circle:
                    var sp = herd.spec;
                    if (!ValidTarget(sp, _surface.SampleHeight(herd.target))) return false;
                    foreach (var a in herd.members)
                        if (a.hasGoal && !ValidTarget(sp, _surface.SampleHeight(a.goal))) return false;
                    return true;
                default:
                    return false;
            }
        }

        // A young never stays behind when its parent walks off on a pattern: it gets a spot at the parent's side.
        void YoungFollow(Herd herd, AnimalActivity act)
        {
            foreach (var a in herd.members)
            {
                if (a.hidden || !Young(a) || a.parent == null || a.hasGoal || !a.parent.hasGoal || a.parent.act != act) continue;
                float body = herd.spec.body * animalScale;
                for (int tries = 0; tries < 4; tries++)
                {
                    float ang = Rand(0f, Mathf.PI * 2f);
                    Vector2 g = a.parent.goal + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (body * 0.7f);
                    if (!OkSpot(herd.spec, g)) continue;
                    SetGoal(a, g, act);
                    break;
                }
            }
        }

        float RadiusOf(Herd herd) =>
            herd.spec.body * animalScale * formationSpacing * Mathf.Sqrt(Mathf.Max(1, herd.members.Count));

        // Side by side along the water: each member walks from its place in the row straight down to the band.
        void AssignShoreGoals(Herd herd, float lo, float hi, AnimalActivity act, bool youngToo, float spacing = 1.4f)
        {
            float body = herd.spec.body * animalScale;
            Vector2 dir = herd.errandDir;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            int n = 0;
            foreach (var a in herd.members) if (!a.hidden && (youngToo || !Young(a))) n++;
            int k = 0;
            foreach (var a in herd.members)
            {
                if (a.hidden || (!youngToo && Young(a))) continue;
                float off = (k++ - (n - 1) * 0.5f) * body * spacing;
                if (!TryWaterline(herd.center + perp * off, dir, lo, hi, shoreWalk, out Vector2 p)) continue;
                SetGoal(a, p, act);
                a.step = 0;
                a.shore = true;
            }
        }

        // Something is passing (LifeEnvironment.PointOfInterest): the herd lines up across the direction and
        // watches it for a while.
        bool StartWatch(Herd herd)
        {
            var s = herd.spec;
            Vector2 to = _poiLocal - herd.center;
            if (to.sqrMagnitude < 1f) return false;
            Vector2 dir = to.normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            float body = s.body * animalScale;
            int n = 0;
            foreach (var a in herd.members) if (IsAwake(a) && a.act == AnimalActivity.None) n++;
            if (n == 0) return false;
            int k = 0;
            foreach (var a in herd.members)
            {
                if (!IsAwake(a) || a.act != AnimalActivity.None) continue;
                Vector2 p = herd.center + dir * (body * 1.2f) + perp * ((k++ - (n - 1) * 0.5f) * body * 1.3f);
                if (!Valid(s, _surface.SampleHeight(p))) continue;
                SetGoal(a, p, AnimalActivity.Watch);
            }
            herd.errand = Errand.Watch;
            herd.errandStage = 1;
            herd.errandDir = dir;
            herd.errandT = Rand(8f, 14f);
            herd.wait = Mathf.Max(herd.wait, herd.errandT + 1f);
            ErrandsStarted++;
            Moment(MomentKind.Errand, herd);
            return true;
        }

        // Hill climb from the herd centre in half-unit steps: every step is uphill, so the way is walkable.
        bool TryFindLookout(Herd herd, out Vector2 top)
        {
            var s = herd.spec;
            top = herd.center;
            float h0 = _surface.SampleHeight(top), hTop = h0;
            for (int iter = 0; iter < 14; iter++)
            {
                Vector2 best = top;
                float bestH = hTop;
                for (int i = 0; i < 8; i++)
                {
                    float ang = i * (Mathf.PI * 0.25f);
                    Vector2 c = top + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 0.45f;
                    if ((c - herd.center).sqrMagnitude > lookoutRange * lookoutRange) continue;
                    float h = _surface.SampleHeight(c);
                    if (h > bestH && h <= s.maxH && !Burning(c)) { best = c; bestH = h; }
                }
                if (bestH <= hTop) break;
                top = best;
                hTop = bestH;
            }
            return hTop >= h0 + lookoutMinRise;
        }

        bool StartLookout(Herd herd)
        {
            Animal pick = null;
            foreach (var a in herd.members)
                if (!Young(a) && IsAwake(a) && a.act == AnimalActivity.None) { pick = a; break; }
            bool sentry = herd.spec.kind == LifeKind.Meerkat;
            Vector2 top = default;
            if (pick != null && sentry && !TryFindLookout(herd, out top))
            {
                // No rise nearby: the sentry posts itself beside the nearest termite mound, else where it stands.
                top = pick.pos;
                if (_life != null && _life.TryRandomPlant(LifeKind.TermiteMound, _rnd, herd.center, lookoutRange, out Vector2 mound))
                {
                    Vector2 away = herd.center - mound;
                    Vector2 spot = mound + (away.sqrMagnitude > 1e-4f ? away.normalized : new Vector2(1f, 0f)) * 0.14f;
                    if (Valid(herd.spec, _surface.SampleHeight(spot))) top = spot;
                }
            }
            else if (pick == null || (!sentry && !TryFindLookout(herd, out top)))
            {
                herd.lookoutCool = Rand(10f, 20f);
                return false;
            }
            SetGoal(pick, top, sentry ? AnimalActivity.Sentry : AnimalActivity.Lookout);
            herd.lookout = pick;
            return true;
        }

        Animal RandomIdle(Herd herd, bool adultsOnly)
        {
            int n = herd.members.Count;
            for (int i = 0; i < 6; i++)
            {
                var a = herd.members[_rnd.Next(n)];
                if (a.act == AnimalActivity.None && !a.hidden && (a.state == AnimalState.Graze || a.state == AnimalState.Look) && !(adultsOnly && Young(a))) return a;
            }
            return null;
        }

        bool TryNearestBurrow(Vector2 from, float reach, out Vector2 burrow)
        {
            burrow = default;
            float best = reach * reach;
            bool any = false;
            foreach (var b in _burrows)
            {
                float d = (b - from).sqrMagnitude;
                if (d > best) continue;
                best = d;
                burrow = b;
                any = true;
            }
            return any;
        }

        void AddBurrow(Vector2 p)
        {
            if (maxBurrows <= 0) return;
            while (_burrows.Count >= maxBurrows) _burrows.RemoveAt(0);
            _burrows.Add(p);
        }

        void PruneBurrows()
        {
            for (int i = _burrows.Count - 1; i >= 0; i--)
                if (_surface.SampleHeight(_burrows[i]) < DrownHeight + 0.05f) _burrows.RemoveAt(i);
        }

        bool StartDig(Herd herd)
        {
            var a = RandomIdle(herd, true);
            if (a == null) return false;
            a.act = AnimalActivity.Dig;
            a.actT = Rand(4f, 6f);
            SetState(a, AnimalState.Graze, a.actT + 1f, GrazePitch, true);
            herd.digger = a;
            herd.wait = Mathf.Max(herd.wait, a.actT + 1.5f);
            return true;
        }

        void FinishDig(Herd herd, Animal a)
        {
            float rad = a.yaw * Mathf.Deg2Rad;
            Vector2 p = a.pos + new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * (herd.spec.body * animalScale * 1.1f);
            if (!Valid(herd.spec, _surface.SampleHeight(p))) p = a.pos;
            AddBurrow(p);
            herd.digger = null;
            StartBinky(a);
        }

        void StartBinky(Animal a)
        {
            a.act = AnimalActivity.Binky;
            a.actT = 1.05f;
            Stamp(a);
        }

        // Hares bolt for the nearest burrow instead of across the island.
        bool StartDive(Herd herd)
        {
            if (!herd.spec.hops || herd.diving || behaviourRate <= 0f) return herd.diving;
            if (!TryNearestBurrow(herd.center, burrowReach, out Vector2 burrow)) return false;
            EndPlay(herd, false);
            StopDig(herd);
            herd.diving = true;
            herd.burrow = burrow;
            foreach (var a in herd.members) if (!a.hidden) SetGoal(a, burrow, AnimalActivity.Burrowed);
            Dives++;
            Moment(MomentKind.AnimalDive, herd);
            return true;
        }

        bool Emerge(Herd herd, Animal a, float dt)
        {
            a.actT -= dt;
            if (a.actT > 0f) return false;
            var s = herd.spec;
            float ang = Rand(0f, 6.28f);
            Vector2 p = herd.burrow + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (s.body * animalScale * Rand(1f, 1.8f));
            a.pos = Valid(s, _surface.SampleHeight(p)) ? p : herd.burrow;
            a.yaw = Rand(0f, 360f);
            a.hidden = false;
            a.act = AnimalActivity.None;
            SetState(a, AnimalState.Look, Rand(1.5f, 3f), StretchPitch, true);
            return true;
        }

        // Binky: three big hops on the spot with a full twist. Wallow: a lying ox rolls from side to side; only
        // a detailed animal shows the roll, so only that one costs rebuilds.
        bool SoloStep(Herd herd, Animal a, float dt, bool ok)
        {
            a.actT -= dt;
            if (!ok || a.actT <= 0f)
            {
                a.roll = 0f;
                a.wallowIn = 0f;
                a.act = AnimalActivity.None;
                Stamp(a);
                return true;
            }
            if (a.act == AnimalActivity.Binky)
            {
                a.yaw = Mathf.Repeat(a.yaw + 343f * dt, 360f);
                return true;
            }
            float total = -a.wallowIn;
            float env = Mathf.Clamp01(Mathf.Min(a.actT, total - a.actT) * 1.5f);
            a.roll = 75f * env * Mathf.Sin((total - a.actT) * 3.7f);
            return a.detailed;
        }

        // What a member does once it stands on its goal. Returns whether the mesh needs a rebuild.
        bool GoalStep(Herd herd, Animal a, float dt)
        {
            bool changed = false;
            bool first = !a.arrived;
            a.arrived = true;
            switch (a.act)
            {
                case AnimalActivity.Burrowed:
                    a.hidden = true;
                    a.hasGoal = false;
                    a.actT = Rand(4f, 10f);
                    SetMoving(a, false);
                    return true;
                case AnimalActivity.Drink:
                case AnimalActivity.Wade:
                    if (first)
                    {
                        a.yaw = Mathf.Atan2(herd.errandDir.x, herd.errandDir.y) * Mathf.Rad2Deg;
                        SetState(a, AnimalState.Graze, Rand(3f, 7f), DrinkPitch, false);
                        return true;
                    }
                    a.timer -= dt;
                    if (a.timer > 0f) return false;
                    return a.pitchTo == DrinkPitch
                        ? SetState(a, AnimalState.Look, Rand(1.5f, 3f), LookPitch, true)
                        : SetState(a, AnimalState.Graze, Rand(3f, 7f), DrinkPitch, false);
                case AnimalActivity.Watch:
                    if (first)
                    {
                        a.yaw = Mathf.Atan2(herd.errandDir.x, herd.errandDir.y) * Mathf.Rad2Deg;
                        changed = true;
                    }
                    if (SetState(a, AnimalState.Look, 1f, LookPitch, true)) changed = true;
                    return changed;
                case AnimalActivity.Soak:
                    if (!first) return false;
                    a.yaw = Mathf.Atan2(herd.errandDir.x, herd.errandDir.y) * Mathf.Rad2Deg + Rand(-50f, 50f);
                    a.step = SoakLoaf;
                    SetState(a, AnimalState.Rest, 999f, RestPitch, false);
                    return true;
                case AnimalActivity.Slide:
                    return SlideStep(herd, a, dt, first);
                case AnimalActivity.Parade:
                    if (first)
                    {
                        a.yaw = a.baseYaw = Mathf.Atan2(herd.errandDir.x, herd.errandDir.y) * Mathf.Rad2Deg;
                        SetState(a, AnimalState.Look, 999f, LookPitch, false);
                        return true;
                    }
                    if (herd.choreoStep != ParadeFlag) return false;
                    a.yaw = a.baseYaw + 38f * Mathf.Sin(herd.choreoT * 2.4f);
                    return true;
                case AnimalActivity.OneLeg:
                    return false;
                case AnimalActivity.Visit:
                case AnimalActivity.Spread:
                    if (first)
                    {
                        Vector2 look = a.act == AnimalActivity.Visit ? herd.errandPos - a.pos : a.pos - herd.center;
                        if (look.sqrMagnitude > 1e-4f) a.yaw = Mathf.Atan2(look.x, look.y) * Mathf.Rad2Deg + Rand(-35f, 35f);
                        SetState(a, AnimalState.Graze, Rand(3f, 7f), GrazePitch, false);
                        return true;
                    }
                    a.timer -= dt;
                    if (a.timer > 0f) return false;
                    if (a.state == AnimalState.Graze) return SetState(a, AnimalState.Look, Rand(1.5f, 3.5f), LookPitch, true);
                    a.yaw = Mathf.Repeat(a.yaw + Rand(-40f, 40f), 360f);
                    return SetState(a, AnimalState.Graze, Rand(4f, 8f), GrazePitch, false);
                case AnimalActivity.Browse:
                    return BrowseStep(herd, a, dt, first);
                case AnimalActivity.Circle:
                    if (first)
                    {
                        a.step = CircleGuard;
                        Vector2 outDir = a.pos - herd.center;
                        if (!Young(a) && outDir.sqrMagnitude > 1e-6f) a.yaw = Mathf.Atan2(outDir.x, outDir.y) * Mathf.Rad2Deg;
                        if (Young(a)) SetState(a, AnimalState.Rest, Rand(20f, 40f), RestPitch, false);
                        else SetState(a, AnimalState.Look, Rand(5f, 10f), LookPitch, true);
                        return true;
                    }
                    return IdleStep(herd, a, herd.members.IndexOf(a), dt);
                case AnimalActivity.Sentry:
                case AnimalActivity.Lookout:
                    if (first)
                    {
                        Vector2 outward = a.pos - herd.center;
                        if (outward.sqrMagnitude > 1e-6f) a.yaw = Mathf.Atan2(outward.x, outward.y) * Mathf.Rad2Deg;
                        SetState(a, AnimalState.Look, Rand(4f, 9f), LookPitch, true);
                        return true;
                    }
                    a.timer -= dt;
                    if (a.timer > 0f) return false;
                    a.yaw = Mathf.Repeat(a.yaw + Rand(-50f, 50f), 360f);
                    a.timer = Rand(4f, 9f);
                    return true;
                default:
                    ClearGoal(a);
                    return false;
            }
        }

        // Once per thinkInterval for a calm herd by day: errands first (they walk the herd somewhere), then
        // the things a standing herd does.
        bool Think(Herd herd, bool standing)
        {
            var s = herd.spec;
            float rate = behaviourRate;
            // While the water rises nobody walks down to it: the errands at the waterline wait, the rest only use
            // ground in the safe band (OkSpot).
            bool shoreOk = !Rising;
            if (herd.dawnDrink)
            {
                herd.dawnDrink = false;
                if (shoreOk && StartShoreErrand(herd, Errand.Drink)) return true;
            }
            if (herd.errandCool <= 0f)
            {
                if (s.kind == LifeKind.Sheep && (Raining || Noon) && StartShade(herd)) return true;
                if (s.kind == LifeKind.Ox)
                {
                    float chance = Noon ? 0.25f : LifeEnvironment.TimeOfDay < 0f ? 0.03f : 0.008f;
                    if (shoreOk && Rand() < chance * rate && StartShoreErrand(herd, Errand.Wade)) return true;
                    if (!herd.circled && _night > duskThreshold && CanCircle(herd) && StartCircle(herd)) return true;
                }
                if (shoreOk)
                    switch (s.kind)
                    {
                        case LifeKind.Capybara:
                            if (Rand() < (Noon ? 0.3f : 0.035f) * rate && StartShoreErrand(herd, Errand.Soak)) return true;
                            break;
                        case LifeKind.Penguin:
                            if (Rand() < 0.05f * rate && StartShoreErrand(herd, Errand.Slide)) return true;
                            break;
                        case LifeKind.Flamingo:
                            if (Rand() < 0.04f * rate && StartShoreErrand(herd, Errand.Parade)) return true;
                            break;
                    }
                if (s.kind == LifeKind.Giraffe && Rand() < 0.06f * rate && StartBrowse(herd)) return true;
                // The species' own move: only by full day, from a herd that stands a while.
                if (standing && herd.wait > 4f && _night < 0.1f && herd.settleT <= 0f && Rand() < signatureRate * rate && StartSignature(herd)) return true;
                float roll = Rand();
                float stroll = shoreOk ? strollRate * rate : 0f, visit = stroll + visitRate * rate, spread = visit + spreadRate * rate;
                if (roll < stroll) { if (StartStroll(herd)) return true; }
                else if (roll < visit) { if (StartVisit(herd)) return true; }
                else if (roll < spread && standing) { if (StartSpread(herd)) return true; }
            }
            if (!standing || herd.wait < 4f) return false;
            if (_hasPoi && herd.watchCool <= 0f && Rand() < 0.35f * rate && StartWatch(herd)) return true;
            if ((s.kind == LifeKind.Goat || s.kind == LifeKind.Meerkat) && herd.lookout == null && herd.lookoutCool <= 0f && herd.wait > 6f && herd.members.Count >= 2) return StartLookout(herd);
            if ((s.kind == LifeKind.Zebra || s.kind == LifeKind.Reindeer) && herd.members.Count >= 3 && herd.errandCool <= 0f && Rand() < 0.015f * rate) return StartStampede(herd);
            if (s.kind == LifeKind.ArcticFox && Rand() < 0.08f * rate)
            {
                var fox = RandomIdle(herd, false);
                if (fox != null) { StartPounce(fox); return true; }
            }
            if (s.hops && herd.digger == null)
            {
                if (!TryNearestBurrow(herd.center, burrowReach * 0.7f, out _) && Rand() < 0.3f * rate) return StartDig(herd);
                if (Tier == LifeTier.Near && Rand() < 0.04f * rate)
                {
                    var a = RandomIdle(herd, false);
                    if (a != null) { StartBinky(a); return true; }
                }
            }
            return false;
        }

        void PollPointOfInterest()
        {
            _hasPoi = false;
            if (behaviourRate <= 0f || _surface == null) return;
            if (!LifeEnvironment.TryPointOfInterest(transform.position, _surface.BoundingRadius + watchRange, out Vector3 world)) return;
            Vector3 l = transform.InverseTransformPoint(world);
            _poiLocal = new Vector2(l.x, l.z);
            _hasPoi = true;
        }

        // ---------------------------------------------------------- choreographies

        // Night amount from which the oxen close their circle; below sleepThreshold so Think still runs.
        public float duskThreshold = 0.3f;

        // Special template pose (AnimalModels.PoseSpecial) the animal is shown in right now.
        int PoseOf(Herd herd, Animal a)
        {
            switch (herd.spec.kind)
            {
                case LifeKind.Flamingo: return a.act == AnimalActivity.OneLeg || a.state == AnimalState.Rest || a.state == AnimalState.Sleep ? 1 : 0;
                case LifeKind.Tortoise: return a.act == AnimalActivity.Tuck && a.step == TuckHide ? 1 : 0;
                // Meerkats get up on their hind legs for every look around, the sentry for as long as it stands.
                case LifeKind.Meerkat: return (a.act == AnimalActivity.Sentry && a.arrived) || (a.state == AnimalState.Look && !a.moving && a.pitch == 0f) ? 1 : 0;
                case LifeKind.Penguin:
                    if (a.act == AnimalActivity.SkyCall && a.step == MovePerform) return AnimalModels.PoseCall;
                    return a.act == AnimalActivity.Slide && (a.step == SlideFlop || a.step == SlideGlide) ? 1 : 0;
                default: return 0;
            }
        }

        // Penguins: each gets a lane from the top of the bank (goal) down to the water (from), and a turn.
        void AssignSlide(Herd herd)
        {
            float body = herd.spec.body * animalScale;
            Vector2 dir = herd.errandDir;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            int n = 0;
            foreach (var a in herd.members) if (!a.hidden) n++;
            int k = 0;
            foreach (var a in herd.members)
            {
                if (a.hidden) continue;
                Vector2 top = herd.center + perp * ((k - (n - 1) * 0.5f) * body * 1.6f);
                if (!Valid(herd.spec, _surface.SampleHeight(top)) || !TryWaterline(top, dir, WadeFloor + 0.015f, WadeHigh, shoreWalk, out Vector2 water)) { k++; continue; }
                SetGoal(a, top, AnimalActivity.Slide);
                a.from = water;
                a.step = SlideQueue;
                a.actT = 0.6f + k * 0.8f;
                k++;
            }
        }

        // waddle to the lane -> wait for the turn -> flop -> glide down on the belly -> dive -> surface -> shake.
        bool SlideStep(Herd herd, Animal a, float dt, bool first)
        {
            switch (a.step)
            {
                case SlideQueue:
                    if (first)
                    {
                        a.yaw = Mathf.Atan2(herd.errandDir.x, herd.errandDir.y) * Mathf.Rad2Deg;
                        SetState(a, AnimalState.Look, 999f, LookPitch, true);
                        return true;
                    }
                    a.actT -= dt;
                    if (a.actT > 0f) return false;
                    a.step = SlideFlop;
                    a.actT = 0.45f;
                    Stamp(a);
                    return true;
                case SlideFlop:
                    a.actT -= dt;
                    if (a.actT > 0f) return false;
                    a.step = SlideGlide;
                    a.goal = a.from;
                    a.arrived = false;
                    a.shore = true;
                    Slides++;
                    return true;
                case SlideGlide:
                    a.step = SlideDive;
                    a.dived = true;
                    a.actT = Rand(1.5f, 3f);
                    return true;
                case SlideDive:
                    a.actT -= dt;
                    if (a.actT > 0f) return false;
                    a.dived = false;
                    a.step = SlideSurface;
                    a.actT = 0.9f;
                    a.yaw = Mathf.Repeat(a.yaw + 180f, 360f);
                    SetState(a, AnimalState.Look, 999f, StretchPitch, true);
                    return true;
                case SlideSurface:
                    a.actT -= dt;
                    if (a.actT > 0f) return false;
                    a.step = SlideShake;
                    a.actT = 1.1f;
                    a.baseYaw = a.yaw;
                    return true;
                default:
                    // A penguin shakes the water off by wobbling from flipper to flipper (a roll), not with the
                    // capybara's twist about its own axis.
                    a.actT -= dt;
                    a.roll = 16f * Mathf.Sin(_clock * 15f);
                    if (a.actT <= 0f)
                    {
                        a.yaw = a.baseYaw;
                        ClearGoal(a);
                    }
                    return true;
            }
        }

        // Flamingos in a row in the shallows: gather -> heads up, turning left and right together -> march along
        // the water line and back in step -> everyone on one leg.
        bool ParadeStep(Herd herd, float dt)
        {
            herd.choreoT += dt;
            int waiting = 0, row = 0;
            foreach (var a in herd.members)
                if (a.act == AnimalActivity.Parade || a.act == AnimalActivity.OneLeg) { row++; if (!a.arrived) waiting++; }
            if (row == 0) { CancelErrand(herd); return true; }
            Vector2 perp = new Vector2(-herd.errandDir.y, herd.errandDir.x);
            switch (herd.choreoStep)
            {
                case ParadeGather:
                    if (waiting > 0 && herd.choreoT < 12f) return false;
                    herd.choreoStep = ParadeFlag;
                    herd.choreoT = 0f;
                    float phase = Rand(0f, 6.28f);
                    foreach (var a in herd.members)
                    {
                        if (a.act != AnimalActivity.Parade) continue;
                        if (!a.arrived) { ClearGoal(a); continue; }
                        a.phase = phase;
                        SetState(a, AnimalState.Look, 999f, StretchPitch, false);
                    }
                    return true;
                case ParadeFlag:
                    if (herd.choreoT < 5f) return false;
                    herd.choreoStep = ParadeMarch;
                    herd.choreoT = 0f;
                    return true;
                case ParadeMarch:
                {
                    const float lap = 5f, reach = 0.45f;
                    if (herd.choreoT >= lap * 2f)
                    {
                        herd.choreoStep = ParadeOneLeg;
                        herd.choreoT = 0f;
                        herd.errandT = Rand(10f, 16f);
                        foreach (var a in herd.members)
                        {
                            if (a.act != AnimalActivity.Parade) continue;
                            a.goal = a.from;
                            a.act = AnimalActivity.OneLeg;
                            a.yaw = a.baseYaw;
                            SetMoving(a, false);
                            SetState(a, AnimalState.Rest, 999f, 0f, false);
                        }
                        return true;
                    }
                    float t = Mathf.Repeat(herd.choreoT, lap) / lap;
                    float off = reach * (t < 0.25f ? t * 4f : t < 0.75f ? 2f - t * 4f : t * 4f - 4f);
                    foreach (var a in herd.members)
                        if (a.act == AnimalActivity.Parade) a.goal = a.from + perp * off;
                    return false;
                }
                default:
                    if (herd.choreoT < herd.errandT) return false;
                    foreach (var a in herd.members)
                        if (a.act == AnimalActivity.OneLeg) SetState(a, AnimalState.Look, Rand(1f, 2.5f), StretchPitch, true);
                    CancelErrand(herd);
                    return true;
            }
        }

        bool StartBrowse(Herd herd)
        {
            var s = herd.spec;
            if (_life == null || (!_life.TryRandomPlant(LifeKind.Acacia, _rnd, herd.center, shadeRange, out Vector2 tree)
                && !_life.TryRandomPlant(LifeKind.Tree, _rnd, herd.center, shadeRange, out tree)))
            {
                herd.errandCool = Rand(10f, 20f);
                return false;
            }
            Vector2 away = herd.center - tree;
            Vector2 spot = tree + (away.sqrMagnitude > 1e-4f ? away.normalized : new Vector2(1f, 0f)) * 0.3f;
            if (!OkSpot(s, spot))
            {
                herd.errandCool = Rand(10f, 20f);
                return false;
            }
            herd.errand = Errand.Browse;
            herd.errandStage = 0;
            herd.errandPos = tree;
            herd.target = spot;
            herd.wait = 0f;
            ErrandsStarted++;
            Moment(MomentKind.Errand, herd);
            return true;
        }

        // Adults ring the tree, young stay in their places beside them.
        void AssignBrowse(Herd herd)
        {
            var s = herd.spec;
            int n = 0;
            foreach (var a in herd.members) if (!Young(a) && IsAwake(a)) n++;
            if (n == 0) return;
            Vector2 to = herd.center - herd.errandPos;
            float start = Mathf.Atan2(to.y, to.x);
            float ring = Mathf.Max(0.26f, s.body * animalScale * 0.55f * n / Mathf.PI);
            int k = 0;
            foreach (var a in herd.members)
            {
                if (Young(a) || !IsAwake(a)) continue;
                float ang = start + (k++ - (n - 1) * 0.5f) * (Mathf.PI * 2f / Mathf.Max(n, 5));
                Vector2 p = herd.errandPos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * ring;
                if (!Valid(s, _surface.SampleHeight(p))) continue;
                SetGoal(a, p, AnimalActivity.Browse);
                a.step = BrowseWalk;
            }
        }

        // stretch the neck up into the crown -> chew (the head bobs) -> look about, swaying -> stretch again.
        bool BrowseStep(Herd herd, Animal a, float dt, bool first)
        {
            Vector2 to = herd.errandPos - a.pos;
            float faceTree = to.sqrMagnitude > 1e-6f ? Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg : a.yaw;
            if (first || a.step == BrowseWalk)
            {
                a.yaw = faceTree;
                a.step = BrowseStretch;
                SetState(a, AnimalState.Look, 1.6f, BrowsePitch, false);
                return true;
            }
            a.timer -= dt;
            switch (a.step)
            {
                case BrowseStretch:
                    if (a.timer > 0f) return false;
                    a.step = BrowseChew;
                    a.actT = Rand(4f, 6f);
                    SetState(a, AnimalState.Look, 0.6f, ChewPitch, false);
                    return true;
                case BrowseChew:
                    a.actT -= dt;
                    if (a.actT <= 0f)
                    {
                        a.step = BrowseSway;
                        a.yaw = Mathf.Repeat(faceTree + Rand(-30f, 30f), 360f);
                        SetState(a, AnimalState.Look, Rand(2f, 3f), LookPitch, true);
                        return true;
                    }
                    if (a.timer > 0f) return false;
                    return SetState(a, AnimalState.Look, 0.6f, a.pitchTo == BrowsePitch ? ChewPitch : BrowsePitch, false);
                default:
                    if (a.timer > 0f) return false;
                    a.yaw = faceTree;
                    a.step = BrowseStretch;
                    SetState(a, AnimalState.Look, 1.4f, BrowsePitch, false);
                    return true;
            }
        }

        // The herd bolts along a wide circle that stays on its own ground, all the way round, and settles with
        // every head up.
        bool StartStampede(Herd herd)
        {
            var s = herd.spec;
            float radius = Mathf.Clamp(_surface.BoundingRadius * 0.3f, 1.1f, 3.2f);
            for (int tries = 0; tries < 6; tries++)
            {
                float ang = Rand(0f, 6.28f);
                Vector2 c = herd.center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius;
                bool ok = true;
                for (int i = 0; i < 12 && ok; i++)
                {
                    float t = i * (Mathf.PI / 6f);
                    Vector2 p = c + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * radius;
                    ok = OkSpot(s, p);
                }
                if (!ok) { radius *= 0.8f; continue; }
                EndPlay(herd, false);
                ReleaseLookout(herd);
                herd.stampC = c;
                herd.stampR = radius;
                herd.stampDir = Rand() < 0.5f ? 1f : -1f;
                herd.stampDist = 0f;
                herd.stampT = 40f;
                herd.choreoStep = StampedeGallop;
                herd.wait = 0f;
                foreach (var a in herd.members) if (!a.hidden) a.act = AnimalActivity.Stampede;
                Stampedes++;
                return true;
            }
            herd.errandCool = Rand(20f, 40f);
            return false;
        }

        void EndStampede(Herd herd, bool settle)
        {
            if (herd.stampT <= 0f) return;
            if (settle && herd.choreoStep == StampedeGallop)
            {
                herd.choreoStep = StampedeHeadsUp;
                herd.stampT = 3.5f;
                herd.wait = 3.5f;
                herd.target = herd.center;
                foreach (var a in herd.members)
                    if (a.act == AnimalActivity.Stampede) SetState(a, AnimalState.Look, 3.5f, LookPitch, true);
                return;
            }
            herd.stampT = 0f;
            herd.choreoStep = 0;
            herd.errandCool = Rand(60f, 120f);
            foreach (var a in herd.members) if (a.act == AnimalActivity.Stampede) a.act = AnimalActivity.None;
        }

        // Keeps the herd's target a little ahead on the circle; true while it gallops.
        bool StepStampede(Herd herd, float dt)
        {
            herd.stampT -= dt;
            if (herd.choreoStep == StampedeHeadsUp)
            {
                if (herd.stampT <= 0f) EndStampede(herd, false);
                return false;
            }
            if (herd.stampT <= 0f || herd.stampDist >= herd.stampR * Mathf.PI * 2f * 0.92f)
            {
                EndStampede(herd, true);
                return false;
            }
            Vector2 rel = herd.center - herd.stampC;
            float ang = Mathf.Atan2(rel.y, rel.x) + herd.stampDir * (0.9f / herd.stampR);
            herd.target = herd.stampC + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * herd.stampR;
            herd.wait = 0f;
            return true;
        }

        bool CanCircle(Herd herd)
        {
            int adults = 0, young = 0;
            foreach (var a in herd.members) { if (Young(a)) young++; else if (!a.hidden) adults++; }
            return young >= 1 && adults >= 3;
        }

        // Dusk: the adults close a ring around the young and face outwards; the herd spends the night like that.
        bool StartCircle(Herd herd)
        {
            var s = herd.spec;
            float body = s.body * animalScale;
            int adults = 0;
            foreach (var a in herd.members) if (!Young(a) && !a.hidden) adults++;
            if (adults < 3) return false;
            float ring = Mathf.Max(body * 1.5f, body * 0.62f * adults / Mathf.PI);
            int k = 0, y = 0;
            foreach (var a in herd.members)
            {
                if (a.hidden) continue;
                Vector2 p;
                if (Young(a))
                {
                    float ya = y++ * 2.4f;
                    p = herd.center + new Vector2(Mathf.Cos(ya), Mathf.Sin(ya)) * (body * 0.35f);
                }
                else
                {
                    float ang = k++ * (Mathf.PI * 2f / adults);
                    p = herd.center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * ring;
                }
                if (!Valid(s, _surface.SampleHeight(p))) continue;
                SetGoal(a, p, AnimalActivity.Circle);
                a.step = CircleWalk;
            }
            herd.errand = Errand.Circle;
            herd.errandStage = 1;
            herd.circled = true;
            herd.wait = Mathf.Max(herd.wait, 45f);
            ErrandsStarted++;
            Moment(MomentKind.Errand, herd);
            Circles++;
            return true;
        }

        void StartPounce(Animal a)
        {
            a.act = AnimalActivity.Pounce;
            a.step = PounceStalk;
            a.actT = 1.6f;
            SetState(a, AnimalState.Look, 999f, RestPitch, true);
            Pounces++;
        }

        // stalk (frozen, listening) -> leap in a high arc -> nose-dive into the ground, tail up -> shake it off.
        bool PounceStep(Herd herd, Animal a, float dt, bool ok)
        {
            if (!ok)
            {
                a.pitch = a.lift = 0f;
                a.step = 0;
                a.timer = 1f;
                a.act = AnimalActivity.None;
                Stamp(a);
                return true;
            }
            a.actT -= dt;
            float body = herd.spec.body * animalScale;
            switch (a.step)
            {
                case PounceStalk:
                    if (a.actT > 0f) return false;
                    a.step = PounceLeap;
                    a.actT = 0.55f;
                    a.from = a.pos;
                    float rad = a.yaw * Mathf.Deg2Rad;
                    a.goal = a.pos + new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * (body * 1.7f);
                    if (!Valid(herd.spec, _surface.SampleHeight(a.goal))) a.goal = a.pos;
                    return true;
                case PounceLeap:
                {
                    float k = 1f - Mathf.Clamp01(a.actT / 0.55f);
                    a.pos = Vector2.Lerp(a.from, a.goal, k);
                    a.lift = Mathf.Sin(k * Mathf.PI) * body * 1.3f;
                    a.pitch = Mathf.Lerp(-30f, NoseDivePitch, k * k);
                    if (a.actT <= 0f)
                    {
                        a.step = PounceHeadIn;
                        a.actT = 1.3f;
                        a.lift = -body * 0.12f;
                        a.pitch = NoseDivePitch;
                    }
                    return true;
                }
                case PounceHeadIn:
                    if (a.actT > 0f) return false;
                    a.step = PounceShake;
                    a.actT = 0.9f;
                    a.pitch = a.lift = 0f;
                    a.baseYaw = a.yaw;
                    SetState(a, AnimalState.Look, 2f, StretchPitch, true);
                    return true;
                default:
                    // The fox shakes the snow off its face by nodding (a pitch), unlike the capybara's twist.
                    a.pitch = 13f * Mathf.Sin(_clock * 17f);
                    if (a.actT <= 0f)
                    {
                        a.yaw = a.baseYaw;
                        a.pitch = 0f;
                        a.step = 0;
                        a.act = AnimalActivity.None;
                    }
                    return true;
            }
        }

        void StartShake(Animal a)
        {
            a.act = AnimalActivity.Shake;
            a.actT = 1.2f;
            a.baseYaw = a.yaw;
            SetState(a, AnimalState.Look, 2f, StretchPitch, true);
        }

        bool ShakeStep(Animal a, float dt)
        {
            a.actT -= dt;
            a.yaw = a.baseYaw + 22f * Mathf.Sin(_clock * 23f);
            if (a.actT > 0f) return true;
            a.yaw = a.baseYaw;
            a.act = AnimalActivity.None;
            return true;
        }

        // Startled tortoises do not run: everything goes into the shell, and a head peeks out again at the end.
        void Tuck(Herd herd, float seconds)
        {
            herd.tuckT = seconds + Rand(3f, 6f);
            herd.target = herd.center;
            foreach (var a in herd.members)
            {
                a.act = AnimalActivity.Tuck;
                a.step = TuckHide;
                SetMoving(a, false);
                SetState(a, AnimalState.Rest, 999f, RestPitch, false);
            }
            Tucks++;
            _meshDirty = true;
        }

        bool StepTuck(Herd herd, float dt)
        {
            herd.tuckT -= dt;
            herd.wait = Mathf.Max(herd.wait, 0.5f);
            bool changed = false;
            foreach (var a in herd.members)
            {
                if (a.act != AnimalActivity.Tuck) continue;
                if (herd.tuckT <= 0f)
                {
                    a.act = AnimalActivity.None;
                    a.step = 0;
                    a.timer = Rand(1f, 2f);
                    changed = true;
                }
                else if (herd.tuckT < 2.5f && a.step == TuckHide)
                {
                    a.step = TuckPeek;
                    SetState(a, AnimalState.Look, 999f, LookPitch, true);
                    changed = true;
                }
            }
            return changed;
        }

        public int Slides { get; private set; }
        public int Stampedes { get; private set; }
        public int Circles { get; private set; }
        public int Pounces { get; private set; }
        public int Tucks { get; private set; }
        public int HopChains { get; private set; }

        // Starts a choreography on the herd right away, without its trigger (tests, staged screenshots); false when
        // the species or the ground does not allow it.
        public bool StartChoreography(int herdIndex, AnimalActivity which)
        {
            if (_surface == null || _rnd == null || herdIndex < 0 || herdIndex >= _herds.Count) return false;
            var herd = _herds[herdIndex];
            if (herd.errand != Errand.None || herd.stampT > 0f || herd.tuckT > 0f) return false;
            switch (which)
            {
                case AnimalActivity.Slide: return StartShoreErrand(herd, Errand.Slide);
                case AnimalActivity.Parade: return StartShoreErrand(herd, Errand.Parade);
                case AnimalActivity.Soak: return StartShoreErrand(herd, Errand.Soak);
                case AnimalActivity.Browse: return StartBrowse(herd);
                case AnimalActivity.Stampede: return StartStampede(herd);
                case AnimalActivity.Circle: return CanCircle(herd) && StartCircle(herd);
                case AnimalActivity.Sentry: return herd.lookout == null && StartLookout(herd);
                case AnimalActivity.Tuck: Tuck(herd, 3f); return true;
                case AnimalActivity.Stroll: return StartStroll(herd);
                case AnimalActivity.Visit: return StartVisit(herd);
                case AnimalActivity.Spread: return StartSpread(herd);
                case AnimalActivity.Pounce:
                    foreach (var a in herd.members)
                        if (!Young(a) && a.act == AnimalActivity.None && IsAwake(a)) { StartPounce(a); return true; }
                    return false;
                default:
                    return IsSignature(which) && SignatureOf(herd.spec.kind) == which && StartSignature(herd);
            }
        }

        // The herd could start something now (the cozy LifeDirector asks before it nudges one): awake, calm, not
        // fleeing and not busy with an errand, a game, a stampede or its shells. Night errands are cancelled at once.
        public bool HerdFree(int herdIndex)
        {
            if (_surface == null || _rnd == null || herdIndex < 0 || herdIndex >= _herds.Count) return false;
            var herd = _herds[herdIndex];
            if (herd.members.Count == 0 || herd.fleeing || herd.errand != Errand.None || herd.playT > 0f || herd.stampT > 0f || herd.tuckT > 0f) return false;
            if (_night > sleepThreshold || Agitation >= huddleThreshold || behaviourRate <= 0f) return false;
            return !AnySleeping(herd);
        }

        // Starts a game (chase, race of the young, goat/reindeer spar) on a standing herd right away; false when it
        // is busy, walking or has no two awake members to play.
        public bool TryStartPlay(int herdIndex)
        {
            if (!HerdFree(herdIndex)) return false;
            var herd = _herds[herdIndex];
            if (herd.members.Count < 2 || herd.spec.play <= 0f || herd.wait <= 0f) return false;
            StartPlay(herd);
            return herd.playT > 0f;
        }

        // A sleeper stirs without waking anyone: it lifts its head for a few seconds and IdleStep lays it back to
        // sleep (a resting animal goes to sleep again at night). Young ones first; false when nobody lies asleep.
        public bool TryStirInSleep(int herdIndex)
        {
            if (_surface == null || _rnd == null || herdIndex < 0 || herdIndex >= _herds.Count) return false;
            var herd = _herds[herdIndex];
            int n = herd.members.Count;
            if (n == 0 || herd.fleeing || _night <= sleepThreshold) return false;
            Animal pick = null;
            int start = _rnd.Next(n);
            for (int k = 0; k < n; k++)
            {
                var a = herd.members[(start + k) % n];
                if (a.state != AnimalState.Sleep || a.hidden || a.dived || a.stands) continue;
                if (pick == null || (Young(a) && !Young(pick))) pick = a;
            }
            if (pick == null) return false;
            SetState(pick, AnimalState.Rest, Rand(2.5f, 4f), StretchPitch, false);
            _meshDirty = true;
            SleepStirs++;
            if (Moments.Listening) Moments.Report(MomentKind.SleepStir, transform, pick.pos, _surface.SampleHeight(pick.pos));
            return true;
        }

        public int SleepStirs { get; private set; }

        // ---------------------------------------------------------- stepping

        public void Step(float dt)
        {
            if (_surface == null) _surface = GetComponent<IIslandSurface>();
            if (_surface == null || _rnd == null) return;

            _stepClock += dt;
            _refAge += dt;
            float sinkDepth = _surface.SinkDepth;
            if (sinkDepth > _lastSink + 1e-4f)
            {
                // The pace comes from the rises themselves (the island reports its sink in half-second steps).
                float span = _stepClock - _sinkChangeAt;
                if (_sinkChangeAt >= 0f && span > 0.05f)
                {
                    float rate = (sinkDepth - _sinkAtChange) / span;
                    _sinkRate = _sinkRate > 0f ? Mathf.Lerp(_sinkRate, rate, 0.3f) : rate;
                }
                _sinkChangeAt = _stepClock;
                _sinkAtChange = sinkDepth;
                _risingT = 6f;
            }
            else
            {
                _risingT -= dt;
                if (_risingT <= 0f) { _sinkRate = 0f; _sinkChangeAt = -1f; }
            }
            _lastSink = sinkDepth;
            if (_stepClock > _clock)
            {
                _clock = _stepClock;
                Shader.SetGlobalFloat(LifeClockId, _clock);
            }
            _night = LifeEnvironment.NightAmount;
            if (_night > sleepThreshold) _wasNight = true;
            else if (_wasNight && _night < wakeThreshold)
            {
                _wasNight = false;
                if (behaviourRate > 0f) foreach (var h in _herds) h.dawnDrink = Rand() < 0.7f * behaviourRate;
            }

            float dist = LifeLod.Distance(transform.position);
            if (_renderer != null) _renderer.enabled = dist < hideDistance;
            Tier = LifeLod.Tier(dist, detailDistance, simDistance);
            if (Tier != _lastTier)
            {
                if (_lastTier == LifeTier.Far) SeedStates();
                _lastTier = Tier;
            }

            if (_surface.Version != _version)
            {
                _version = _surface.Version;
                Relocate();
                // A sinking player island reports a new shape every half second. Cancelling every errand on each of
                // those meant that on the player's own island hardly any errand ever reached its end; now only an
                // errand that the new shape actually spoils is dropped.
                foreach (var h in _herds)
                {
                    if (!SurvivesReshape(h)) CancelErrand(h);
                    ReleaseLookout(h);
                }
                PruneBurrows();
                if (_surface.LandArea > _peakArea + 0.5f)
                {
                    _peakArea = _surface.LandArea;
                    Rebalance();
                }
                _meshDirty = true;
                _meshTimer = meshInterval;
            }

            _detailTimer += dt;
            if (_detailTimer >= detailCheckInterval)
            {
                _detailTimer = 0f;
                if (UpdateDetail(dist)) _meshDirty = true;
            }
            if (Tier != LifeTier.Far)
            {
                _poiTimer += dt;
                if (_poiTimer >= 2f)
                {
                    _poiTimer = 0f;
                    PollPointOfInterest();
                }
            }

            _stepTimer += dt;
            _meshTimer += dt;
            float interval;
            if (Tier == LifeTier.Far)
            {
                if (_stepTimer >= farStepInterval)
                {
                    DriftHerds(_stepTimer);
                    _stepTimer = 0f;
                }
                // A hidden far batch is never rebuilt; a visible one only often enough that its stale bounds
                // cannot keep it culled once the animals wander into view.
                if (_renderer == null || !_renderer.isVisible) return;
                interval = hiddenMeshInterval;
            }
            else
            {
                // No behaviour step without time: Step(0) runs every frame while the game is paused, and members
                // catching up with their slot used to drop from walk to graze in that frozen frame.
                if (Tier == LifeTier.Near ? dt > 0f : _stepTimer >= midStepInterval)
                {
                    float stepDt = Tier == LifeTier.Near ? dt : _stepTimer;
                    _stepTimer = 0f;
                    for (int i = _herds.Count - 1; i >= 0; i--)
                    {
                        var herd = _herds[i];
                        if (MoveHerd(herd, stepDt)) _meshDirty = true;
                        if (herd.members.Count == 0) _herds.RemoveAt(i);
                    }
                }
                bool visible = _renderer == null || _renderer.isVisible;
                interval = !visible ? hiddenMeshInterval : Tier == LifeTier.Near ? meshInterval : meshInterval * 2f;
            }

            if (_meshDirty && _meshTimer >= interval)
            {
                _meshTimer = 0f;
                _meshDirty = false;
                RebuildMesh();
            }
        }

        // Far tier: herd centres walk their target line at species speed and members are re-seated on their
        // formation slots, so a herd that comes back into range is where it would have wandered to. Their
        // states are only re-seeded when the island comes near again (SeedStates).
        void DriftHerds(float elapsed)
        {
            for (int i = _herds.Count - 1; i >= 0; i--)
            {
                var herd = _herds[i];
                if (herd.members.Count == 0) { _herds.RemoveAt(i); continue; }
                var s = herd.spec;
                herd.fleeTimer = Mathf.Max(0f, herd.fleeTimer - elapsed);
                herd.fleeing = false;
                if (herd.errand != Errand.None || herd.diving || herd.lookout != null || herd.digger != null || herd.stampT > 0f || herd.tuckT > 0f) ResetBehaviour(herd);
                if (Grow(herd, elapsed)) _meshDirty = true;
                if (TryBirth(herd, elapsed, herd.fleeTimer <= 0f)) _meshDirty = true;
                if (herd.wait > 0f || _night > sleepThreshold)
                {
                    herd.wait = Mathf.Max(0f, herd.wait - elapsed);
                    continue;
                }
                Vector2 to = herd.target - herd.center;
                float d = to.magnitude;
                float move = s.speed * elapsed;
                float yaw = d > 1e-4f ? Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg : herd.members[0].yaw;
                if (d <= move)
                {
                    herd.center = herd.target;
                    herd.wait = Rand(s.stopMin, s.stopMax);
                    herd.target = NewTarget(herd);
                }
                else herd.center += to / d * move;
                foreach (var a in herd.members)
                {
                    a.pos = Slot(herd, a, 1f);
                    if (!Valid(s, _surface.SampleHeight(a.pos))) a.pos = herd.center;
                    a.yaw = yaw;
                    a.moving = false;
                }
                _meshDirty = true;
            }
        }

        public void Startle(Vector2 fromLocal, float seconds = 3f)
        {
            if (_surface == null || _rnd == null) return;
            foreach (var herd in _herds)
            {
                CancelErrand(herd);
                ReleaseLookout(herd);
                StopDig(herd);
                EndStampede(herd, false);
                foreach (var a in herd.members)
                    if (a.act == AnimalActivity.Pounce || a.act == AnimalActivity.Shake || a.act == AnimalActivity.HopChain) { a.act = AnimalActivity.None; a.pitch = a.lift = 0f; a.step = 0; }
                if (herd.spec.kind == LifeKind.Tortoise && behaviourRate > 0f)
                {
                    EndPlay(herd, false);
                    Tuck(herd, seconds);
                    continue;
                }
                herd.fleeTimer = seconds;
                herd.fleeFrom = fromLocal;
                herd.wait = 0f;
                herd.target = StartDive(herd) ? herd.burrow : FleeTarget(herd, fromLocal);
            }
        }

        Vector2 FleeTarget(Herd herd, Vector2 from)
        {
            Vector2 away = herd.center - from;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : new Vector2(Mathf.Cos(Rand(0f, 6.28f)), Mathf.Sin(Rand(0f, 6.28f)));
            float far = Mathf.Min(fleeDistance, WanderRange);
            for (int i = 0; i < 12; i++)
            {
                float spread = Rand(-0.6f, 0.6f) * (1f + i * 0.15f);
                Vector2 dir = new Vector2(
                    away.x * Mathf.Cos(spread) - away.y * Mathf.Sin(spread),
                    away.x * Mathf.Sin(spread) + away.y * Mathf.Cos(spread));
                float dist = Rand(far * 0.6f, far) * (1f - 0.6f * i / 11f);
                Vector2 c = herd.center + dir * dist;
                if (ValidTarget(herd.spec, _surface.SampleHeight(c)) && !Burning(c)) return c;
            }
            return herd.center;
        }

        // ---------------------------------------------------------- refuge from rising water

        // The island's land, cut into connected pieces, with the highest spot of each. Rebuilt at most once a second
        // while the shape keeps changing (a sinking island reports a new shape every half second). A herd in danger
        // walks to the nearest ground of its own piece that lies safely above the water (a grid BFS over the piece,
        // so the way is dry), preferring spots away from other herds; only when that band is full or gone does it
        // climb to the top. Herds on a piece that has been cut off stay on its top until it is gone too.
        float[] _refH;
        int[] _refComp, _refQueue, _refDist, _refMark;
        readonly List<Vector2> _refTop = new();
        readonly List<float> _refTopH = new();
        int _refNx, _refNz, _refVersion = -1, _refGen;
        float _refCell, _refAge = 99f, _refSink;
        Vector2 _refOrigin;

        // Seconds of "the water is rising": set whenever the island's sink depth grows. Only then do herds flee -
        // a herd walking down to drink or wade is not in danger. _sinkRate = the current pace in units/s.
        float _risingT, _lastSink, _sinkRate, _sinkChangeAt = -1f, _sinkAtChange;
        bool Rising => _risingT > 0f;
        public bool WaterRising => Rising;

        // Ground at least this high above the water is safe for now: the fixed margin plus refugeLead seconds of
        // the current sinking pace, so a fast-sinking island keeps its herds further up.
        public float SafeHeight => DrownHeight + refugeStart + (Rising ? Mathf.Min(_sinkRate * refugeLead, 1.5f) : 0f);
        bool SafeGround(float h) => !Rising || h >= SafeHeight;
        // Somewhere a herd or a member may be sent: the species' own ground, no fire or houses, and while the water
        // rises only inside the safe band.
        bool OkSpot(Species s, Vector2 p)
        {
            float h = _surface.SampleHeight(p);
            return ValidTarget(s, h) && SafeGround(h) && !Burning(p);
        }

        bool Refuge(Herd herd, float hc, float dt)
        {
            if (!Rising && !herd.refuging) return false;
            float safe = SafeHeight;
            if (hc >= safe && !herd.refuging) return false;
            herd.refugeT -= dt;
            if (herd.refuging && hc >= safe && (herd.target - herd.center).sqrMagnitude < 0.09f)
            {
                herd.refuging = false;
                return false;
            }
            if (herd.refugeT > 0f) return herd.refuging;
            herd.refugeT = 1f;
            // A herd on its way keeps its spot while the spot stays safe; re-planning every second would make the
            // herds that flee together swap places back and forth.
            if (herd.refuging && _surface.SampleHeight(herd.target) >= safe + 0.02f) return true;
            if (!TryRefugeSpot(herd, hc, safe, out Vector2 spot))
            {
                herd.refuging = false;
                return false;
            }
            if (!herd.refuging) RefugeMoves++;
            herd.refuging = true;
            CancelErrand(herd);
            EndStampede(herd, false);
            herd.target = spot;
            herd.wait = 0f;
            return true;
        }

        bool TryRefugeSpot(Herd herd, float hc, float safe, out Vector2 spot)
        {
            spot = default;
            if (_refH == null || (_refVersion != _surface.Version && _refAge >= 1f)) BuildRefugeMap();
            if (_refH == null) return false;
            int start = RefugeCell(herd.center);
            if (start < 0) return false;
            var s = herd.spec;
            // The map may be up to a second old; sinking lowers every cell alike.
            float drop = _surface.SinkDepth - _refSink;
            float need = Mathf.Max(safe + 0.08f, s.minH + Margin(s));
            if (++_refGen == int.MaxValue)
            {
                System.Array.Clear(_refMark, 0, _refMark.Length);
                _refGen = 1;
            }
            int head = 0, tail = 0, best = -1;
            float bestScore = float.MaxValue;
            _refQueue[tail++] = start;
            _refMark[start] = _refGen;
            _refDist[start] = 0;
            while (head < tail)
            {
                int c = _refQueue[head++];
                float d = _refDist[c] * _refCell;
                // Breadth first, so the distance only grows: nothing further on can beat the best score.
                if (d >= bestScore) break;
                float h = _refH[c] - drop;
                if (h >= need && h <= s.maxH)
                {
                    Vector2 p = _refOrigin + new Vector2(c % _refNx, c / _refNx) * _refCell;
                    float score = d + Crowd(herd, p);
                    if (score < bestScore && !Burning(p)) { bestScore = score; best = c; }
                }
                int i = c % _refNx, j = c / _refNx, dn = _refDist[c] + 1;
                if (i > 0) RefugeVisit(c - 1, dn, ref tail);
                if (i < _refNx - 1) RefugeVisit(c + 1, dn, ref tail);
                if (j > 0) RefugeVisit(c - _refNx, dn, ref tail);
                if (j < _refNz - 1) RefugeVisit(c + _refNx, dn, ref tail);
            }
            if (best >= 0)
            {
                spot = _refOrigin + new Vector2(best % _refNx, best / _refNx) * _refCell;
                return true;
            }
            // No room left in the safe band: up to the top of the piece, as close together as it takes.
            if (!TryRefugeTop(herd, out Vector2 top, out float topH) || topH <= hc + 0.02f) return false;
            spot = top;
            return true;
        }

        void RefugeVisit(int c, int dist, ref int tail)
        {
            if (_refComp[c] < 0 || _refMark[c] == _refGen) return;
            _refMark[c] = _refGen;
            _refDist[c] = dist;
            _refQueue[tail++] = c;
        }

        // Detour (in units) a spot costs for lying on or near another herd - where it stands or where it is going.
        float Crowd(Herd herd, Vector2 p)
        {
            if (refugeSpread <= 0f) return 0f;
            float pen = 0f, own = RadiusOf(herd);
            foreach (var h in _herds)
            {
                if (h == herd || h.members.Count == 0) continue;
                float r = herdSpacing + own + RadiusOf(h);
                float d = Mathf.Min((h.center - p).magnitude, (h.target - p).magnitude);
                if (d < r) pen += refugeSpread * (1f - d / r);
            }
            return pen;
        }

        bool TryRefugeTop(Herd herd, out Vector2 top, out float topH)
        {
            top = default;
            topH = 0f;
            if (_refH == null || (_refVersion != _surface.Version && _refAge >= 1f)) BuildRefugeMap();
            if (_refH == null) return false;
            int cell = RefugeCell(herd.center);
            if (cell < 0) return false;
            int comp = _refComp[cell];
            top = _refTop[comp];
            topH = _refTopH[comp];
            // Herds share a hill without standing inside each other: each gets its own spot on the top ring.
            int k = _herds.IndexOf(herd);
            float r = RadiusOf(herd) + 0.3f;
            for (int t = 0; t < 6; t++)
            {
                float ang = (k * 2.39996f) + t * 1.05f;
                Vector2 q = top + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r * Mathf.Min(1f, 0.35f * (k % 5));
                float hq = _surface.SampleHeight(q);
                if (hq >= topH - 0.25f && hq > DrownHeight + 0.05f) { top = q; break; }
            }
            topH = _surface.SampleHeight(top);
            return true;
        }

        int RefugeCell(Vector2 p)
        {
            int ci = Mathf.RoundToInt((p.x - _refOrigin.x) / _refCell), cj = Mathf.RoundToInt((p.y - _refOrigin.y) / _refCell);
            for (int r = 0; r <= 3; r++)
                for (int dj = -r; dj <= r; dj++)
                    for (int di = -r; di <= r; di++)
                    {
                        if (Mathf.Max(Mathf.Abs(di), Mathf.Abs(dj)) != r) continue;
                        int i = ci + di, j = cj + dj;
                        if (i < 0 || j < 0 || i >= _refNx || j >= _refNz) continue;
                        int c = j * _refNx + i;
                        if (_refComp[c] >= 0) return c;
                    }
            return -1;
        }

        void BuildRefugeMap()
        {
            _refVersion = _surface.Version;
            _refAge = 0f;
            Rect b = _surface.LocalBounds;
            _refCell = Mathf.Max(0.4f, Mathf.Max(b.width, b.height) / 90f);
            _refNx = Mathf.Max(2, Mathf.CeilToInt(b.width / _refCell) + 1);
            _refNz = Mathf.Max(2, Mathf.CeilToInt(b.height / _refCell) + 1);
            _refOrigin = b.min;
            int n = _refNx * _refNz;
            if (_refH == null || _refH.Length != n)
            {
                _refH = new float[n];
                _refComp = new int[n];
                _refQueue = new int[n];
                _refDist = new int[n];
                _refMark = new int[n];
                _refGen = 0;
            }
            _refSink = _surface.SinkDepth;
            float land = DrownHeight + 0.02f;
            for (int j = 0, c = 0; j < _refNz; j++)
                for (int i = 0; i < _refNx; i++, c++)
                {
                    _refH[c] = _surface.SampleHeight(_refOrigin + new Vector2(i, j) * _refCell);
                    _refComp[c] = _refH[c] > land ? int.MaxValue : -1;
                }
            _refTop.Clear();
            _refTopH.Clear();
            for (int start = 0; start < n; start++)
            {
                if (_refComp[start] != int.MaxValue) continue;
                int id = _refTop.Count;
                int head = 0, tail = 0, best = start;
                _refQueue[tail++] = start;
                _refComp[start] = id;
                while (head < tail)
                {
                    int c = _refQueue[head++];
                    if (_refH[c] > _refH[best]) best = c;
                    int i = c % _refNx, j = c / _refNx;
                    if (i > 0 && _refComp[c - 1] == int.MaxValue) { _refComp[c - 1] = id; _refQueue[tail++] = c - 1; }
                    if (i < _refNx - 1 && _refComp[c + 1] == int.MaxValue) { _refComp[c + 1] = id; _refQueue[tail++] = c + 1; }
                    if (j > 0 && _refComp[c - _refNx] == int.MaxValue) { _refComp[c - _refNx] = id; _refQueue[tail++] = c - _refNx; }
                    if (j < _refNz - 1 && _refComp[c + _refNx] == int.MaxValue) { _refComp[c + _refNx] = id; _refQueue[tail++] = c + _refNx; }
                }
                _refTop.Add(_refOrigin + new Vector2(best % _refNx, best / _refNx) * _refCell);
                _refTopH.Add(_refH[best]);
            }
        }

        // Shape changed: drop drowned members, re-centre herds whose centre went under, keep everything else.
        void Relocate()
        {
            for (int i = _herds.Count - 1; i >= 0; i--)
            {
                var herd = _herds[i];
                Drown(herd);
                if (herd.members.Count == 0) { _herds.RemoveAt(i); continue; }
                if (_surface.SampleHeight(herd.center) < DrownHeight)
                {
                    Vector2 sum = Vector2.zero;
                    foreach (var a in herd.members) sum += a.pos;
                    herd.center = sum / herd.members.Count;
                    herd.target = herd.center;
                    herd.wait = 0f;
                }
            }
        }

        void Drown(Herd herd)
        {
            int before = herd.members.Count;
            for (int m = herd.members.Count - 1; m >= 0; m--)
            {
                var a = herd.members[m];
                if (_surface.SampleHeight(a.pos) >= (a.shore ? WadeFloor : DrownHeight)) continue;
                // The water came faster than the animal walked: it scrambles onto the nearest dry ground. Only an
                // animal whose own patch of land is gone (nothing dry within reach) is lost.
                if (TryAshore(a.pos, out var dry)) { a.pos = dry; Scrambled++; continue; }
                herd.members.RemoveAt(m);
                Drowned++;
            }
            if (herd.members.Count != before) Orphans(herd);
        }

        public int Scrambled { get; private set; }

        bool TryAshore(Vector2 p, out Vector2 dry)
        {
            for (int ring = 1; ring <= 10; ring++)
            {
                float r = ring * 0.2f;
                for (int k = 0; k < 12; k++)
                {
                    float a = (k + (ring & 1) * 0.5f) * (Mathf.PI / 6f);
                    Vector2 q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (_surface.SampleHeight(q) >= DrownHeight + 0.05f) { dry = q; return true; }
                }
            }
            dry = p;
            return false;
        }

        // Wander range shrinks with the island so herds on small islands still move.
        float WanderRange => Mathf.Clamp(_surface.BoundingRadius * 0.9f, 1.5f, 9f);

        Vector2 NewTarget(Herd herd)
        {
            float far = WanderRange;
            // While the water rises a herd wanders only inside the safe band - or, below it, never further down.
            float floor = Rising ? Mathf.Min(SafeHeight, _surface.SampleHeight(herd.center)) : float.MinValue;
            for (int i = 0; i < 14; i++)
            {
                float ang = Rand(0f, Mathf.PI * 2f);
                float dist = Rand(Mathf.Min(1f, far * 0.5f), far) * (1f - 0.5f * i / 13f);
                Vector2 c = herd.center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
                float h = _surface.SampleHeight(c);
                if (ValidTarget(herd.spec, h) && !Burning(c) && (i >= 9 || !NearOtherHerd(c, herd)) && (i >= 10 || CoastOk(herd.spec, c)) && (i >= 11 || h >= floor)) return c;
            }
            return herd.center;
        }

        // The highest ground within a couple of units - where a herd heads when the shore rises under it.
        Vector2 UphillTarget(Herd herd)
        {
            Vector2 best = herd.center;
            float bestH = _surface.SampleHeight(herd.center);
            float phase = Rand(0f, 6.28f);
            for (int ring = 1; ring <= 2; ring++)
                for (int i = 0; i < 8; i++)
                {
                    float ang = phase + i * (Mathf.PI * 0.25f);
                    Vector2 c = herd.center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (1.5f * ring);
                    float h = _surface.SampleHeight(c);
                    if (h > bestH && h <= herd.spec.maxH && !Burning(c)) { best = c; bestH = h; }
                }
            return best;
        }

        // Returns whether any animal changed position, heading, gait, state or membership, i.e. whether the
        // mesh needs a rebuild; a herd of idle animals costs a rebuild only when one of them changes state
        // because the animation and the pose blends live in the shader.
        bool MoveHerd(Herd herd, float dt)
        {
            var s = herd.spec;
            int before = herd.members.Count;
            int scrambled = Scrambled;
            Drown(herd);
            bool changed = herd.members.Count != before || Scrambled != scrambled;
            if (herd.members.Count == 0) return changed;

            float mul = 1f;
            float body0 = s.body * animalScale;
            bool startled = herd.fleeTimer > 0f;
            if (startled)
            {
                herd.fleeTimer -= dt;
                mul = fleeSpeed;
                herd.wait = 0f;
            }

            if (_life != null && _life.FireNear(herd.center, 1.5f))
            {
                if (!herd.fleeing)
                {
                    herd.fleeing = true;
                    herd.wait = 0f;
                    herd.target = NewTarget(herd);
                }
                mul = Mathf.Max(mul, 2.3f);
            }
            else herd.fleeing = false;

            float hc = _surface.SampleHeight(herd.center);
            if (!startled && !herd.fleeing && Refuge(herd, hc, dt)) mul = Mathf.Max(mul, refugeSpeed);
            bool shore = hc < s.minH + Margin(s) && !herd.refuging;
            if (shore && !startled && _surface.SampleHeight(herd.target) < s.minH + Margin(s))
            {
                herd.target = UphillTarget(herd);
                herd.wait = 0f;
                mul = Mathf.Max(mul, 1.6f);
            }

            bool calm = !startled && !herd.fleeing && !shore;
            bool huddle = calm && Agitation >= huddleThreshold;
            if (huddle && herd.wait < 0.5f) herd.wait = 0.5f;
            bool night = _night > sleepThreshold;
            bool safe = calm && !huddle;
            if (_night < 0.1f) herd.circled = false;
            // The oxen's circle is the one errand that lasts through the night.
            if (!safe || (night && herd.errand != Errand.Circle))
            {
                CancelErrand(herd);
                ReleaseLookout(herd);
                StopDig(herd);
                if (huddle) StartDive(herd);
            }
            if (!safe || night) EndStampede(herd, false);
            if (herd.tuckT > 0f)
            {
                if (herd.fleeing) { herd.tuckT = 0f; foreach (var a in herd.members) if (a.act == AnimalActivity.Tuck) { a.act = AnimalActivity.None; a.step = 0; a.timer = 1f; } changed = true; }
                else if (StepTuck(herd, dt)) changed = true;
            }
            bool galloping = herd.stampT > 0f && StepStampede(herd, dt);
            // Zebras gallop in a bunch; reindeer trot in a wedge (see the member loop), a little slower.
            if (galloping) mul = Mathf.Max(mul, s.kind == LifeKind.Reindeer ? 2.4f : 3.2f);
            if (herd.errand == Errand.Parade && herd.errandStage == 1 && ParadeStep(herd, dt)) changed = true;
            if (herd.errand == Errand.Signature && herd.errandStage == 1 && StepSignature(herd, dt)) changed = true;
            if (herd.diving && safe && herd.wait < 1f) herd.wait = 1f;
            if (herd.errand == Errand.Shade && herd.errandStage == 1 && herd.wait < 2f && (Raining || Noon)) herd.wait = 2f;
            if (herd.errand == Errand.Watch)
            {
                herd.errandT -= dt;
                if (herd.errandT <= 0f || !_hasPoi) CancelErrand(herd);
            }
            herd.errandCool = Mathf.Max(0f, herd.errandCool - dt);
            herd.settleT -= dt;
            herd.watchCool = Mathf.Max(0f, herd.watchCool - dt);
            herd.lookoutCool = Mathf.Max(0f, herd.lookoutCool - dt);
            bool standing = herd.wait > 0f;
            if (behaviourRate > 0f && safe && !night && !herd.refuging && herd.errand == Errand.None && !herd.diving && herd.playT <= 0f && herd.stampT <= 0f && herd.tuckT <= 0f)
            {
                herd.thinkT -= dt;
                if (herd.thinkT <= 0f)
                {
                    herd.thinkT = thinkInterval;
                    if (!AnySleeping(herd) && Think(herd, standing)) changed = true;
                }
            }
            // A settled herd (night, or members still asleep at dawn) never starts a wander.
            if (calm && herd.wait < 1f && (_night > sleepThreshold || AnySleeping(herd))) herd.wait = 1f;

            bool moving = false;
            bool tried = false;
            Vector2 heading = Vector2.zero;
            if (herd.wait > 0f)
            {
                herd.wait -= dt;
            }
            else
            {
                tried = true;
                if (herd.errandStage == 1) CancelErrand(herd);
                Vector2 to = herd.target - herd.center;
                float d = to.magnitude;
                if (d < 0.3f)
                {
                    if (startled) herd.target = herd.diving ? herd.burrow : FleeTarget(herd, herd.fleeFrom);
                    else
                    {
                        herd.wait = Rand(s.stopMin, s.stopMax);
                        herd.target = NewTarget(herd);
                        if (herd.errand != Errand.None) ArriveErrand(herd);
                    }
                }
                else
                {
                    Vector2 next = herd.center + to / d * Mathf.Min(s.speed * mul * dt, d);
                    if (CanStep(s, _surface.SampleHeight(next), hc))
                    {
                        float moved = (next - herd.center).magnitude;
                        herd.lineDist += moved;
                        if (galloping) herd.stampDist += moved;
                        herd.center = next;
                        moving = true;
                        heading = to / d;
                    }
                    else
                    {
                        CancelErrand(herd);
                        herd.target = startled ? (herd.diving ? herd.burrow : FleeTarget(herd, herd.fleeFrom)) : NewTarget(herd);
                    }
                }
            }
            if (galloping && !moving) EndStampede(herd, true);
            if (moving)
            {
                if (!herd.walking)
                {
                    herd.lineDist = 0f;
                    herd.hopAt = body0 * (6f + herd.members.Count % 4);
                    foreach (var a in herd.members) a.lineTravel = 0f;
                }
                ReleaseLookout(herd);
                StopDig(herd);
            }
            herd.walking = moving;

            if (herd.playT > 0f)
            {
                if (!calm || huddle || moving) EndPlay(herd);
                else StepPlay(herd, dt);
                changed = true;
            }
            else if (calm && !huddle && !moving && herd.wait > 3f && _night < sleepThreshold && herd.members.Count >= 3 && s.play > 0f && herd.errand != Errand.Signature)
            {
                float rate = s.play * playRate * (_night > 0.1f ? 3f : 1f) * (HasAwakeYoung(herd) ? youngPlayRate : 1f);
                if (Rand() < rate * dt)
                {
                    StartPlay(herd);
                    changed = true;
                }
            }

            // Members catch up with their slot within a frame, so the herd's own heading has to drive the walk
            // animation and yaw while it travels; only a real lag (falling back, re-gathering) turns them by itself.
            // A member with a goal (shore, lookout, watch line, burrow) walks there instead of to its slot; sheep
            // on the move fall in behind member 0, each joining once the line has passed its place.
            float body = s.body * animalScale;
            float gather = huddle ? 1f - 0.45f * Mathf.Clamp01(Agitation) : herd.errand == Errand.Shade && herd.errandStage == 1 ? 0.55f : galloping ? 0.75f : 1f;
            float turn = 1f - Mathf.Exp(-4f * dt);
            bool strolling = herd.errand == Errand.Stroll;
            // Single file is the sheep's own way of walking; a stroll along the shore goes two abreast (any species).
            bool lineWalk = moving && safe && !galloping && (s.kind == LifeKind.Sheep || strolling) && behaviourRate > 0f;
            bool wedge = galloping && s.kind == LifeKind.Reindeer;
            Vector2 linePerp = new Vector2(-heading.y, heading.x);
            bool burrowBound = false;
            for (int i = 0; i < herd.members.Count; i++)
            {
                var a = herd.members[i];
                if (a.hidden)
                {
                    burrowBound = true;
                    if (safe && Emerge(herd, a, dt)) changed = true;
                    continue;
                }
                if (a.state == AnimalState.Play)
                {
                    if (herd.playT > 0f) continue;
                    SetState(a, AnimalState.Look, Rand(1f, 2f), LookPitch, true);
                    changed = true;
                }
                if (IsSignature(a.act))
                {
                    if (SignatureStep(herd, a, dt)) changed = true;
                    continue;
                }
                if (a.act == AnimalActivity.Burrowed) burrowBound = true;
                if (a.act == AnimalActivity.Binky || a.act == AnimalActivity.Wallow)
                {
                    if (SoloStep(herd, a, dt, safe && !moving && (a.act == AnimalActivity.Binky || a.state == AnimalState.Rest))) changed = true;
                    continue;
                }
                if (a.act == AnimalActivity.Pounce)
                {
                    if (PounceStep(herd, a, dt, safe && !moving)) changed = true;
                    continue;
                }
                if (a.act == AnimalActivity.Shake)
                {
                    if (!safe) a.act = AnimalActivity.None;
                    else { if (ShakeStep(a, dt)) changed = true; continue; }
                }
                if (a.act == AnimalActivity.Tuck) continue;
                if (a.act == AnimalActivity.HopChain)
                {
                    a.actT -= dt;
                    if (a.actT <= 0f || !lineWalk) { a.act = AnimalActivity.None; changed = true; }
                }
                if (a.act == AnimalActivity.Dig)
                {
                    a.actT -= dt;
                    if (a.actT <= 0f)
                    {
                        FinishDig(herd, a);
                        changed = true;
                        continue;
                    }
                }
                if (a.wallowIn > 0f && a.state == AnimalState.Rest && safe && !moving)
                {
                    a.wallowIn -= dt;
                    if (a.wallowIn <= 0f)
                    {
                        a.act = AnimalActivity.Wallow;
                        a.actT = Rand(3.5f, 5.5f);
                        a.wallowIn = -a.actT;
                        Stamp(a);
                        changed = true;
                        continue;
                    }
                }

                bool waiting = false;
                Vector2 desired;
                if (a.hasGoal) desired = a.goal;
                else if (lineWalk)
                {
                    int rank = a.parent != null ? Mathf.Max(0, herd.members.IndexOf(a.parent)) : i;
                    bool pairs = strolling;
                    float back = (pairs ? rank >> 1 : rank) * body * lineSpacing;
                    if (herd.lineDist >= back)
                    {
                        desired = herd.center - heading * back;
                        if (pairs) desired += linePerp * (body * ((rank & 1) == 0 ? -0.45f : 0.45f));
                        if (a.parent != null) desired += linePerp * (body * 0.6f);
                        // Counting sheep: everyone jumps at the same spot of the path as the line passes it.
                        float travel = herd.lineDist - back;
                        if (s.kind == LifeKind.Sheep && a.lineTravel < herd.hopAt && travel >= herd.hopAt && a.act != AnimalActivity.HopChain)
                        {
                            a.act = AnimalActivity.HopChain;
                            a.actT = 0.45f;
                            HopChains++;
                            changed = true;
                        }
                        a.lineTravel = travel;
                        if (a.act != AnimalActivity.HopChain) a.act = strolling ? AnimalActivity.Stroll : AnimalActivity.Line;
                    }
                    else
                    {
                        desired = a.pos;
                        waiting = true;
                    }
                }
                else if (wedge)
                {
                    // A wedge behind the leader, like geese: rank r trots r bodies back and r bodies to its side.
                    int rank = (i + 1) >> 1;
                    float side = (i & 1) == 1 ? 1f : -1f;
                    desired = herd.center - heading * (rank * body * 1.3f) + linePerp * (side * rank * body * 1.05f);
                }
                else desired = Slot(herd, a, gather);
                if (!lineWalk && (a.act == AnimalActivity.Line || (a.act == AnimalActivity.Stroll && !strolling))) a.act = AnimalActivity.None;

                Vector2 toD = desired - a.pos;
                float d = toD.magnitude;
                bool lying = a.state == AnimalState.Rest || a.state == AnimalState.Sleep;
                bool memberMoving = false;
                Vector2 face = heading;
                bool gaitOn = true;
                if (s.hops && (moving || a.hasGoal))
                {
                    a.gaitT -= dt;
                    // A hare that has fallen five bodies behind cuts its pause short, so a travelling herd
                    // strings out over a body length or two, never across the island.
                    if (a.gaitT <= 0f || (!a.gaitOn && (d > body * 5f || a.act == AnimalActivity.Burrowed)))
                    {
                        a.gaitOn = !a.gaitOn;
                        a.gaitT = a.gaitOn ? Rand(s.burstMin, s.burstMax) : Rand(s.pauseMin, s.pauseMax);
                    }
                    gaitOn = a.gaitOn;
                }
                else a.gaitOn = true;
                float stop = a.hasGoal ? body * 0.12f : body * 0.25f;
                if (d > stop && gaitOn && !waiting && (!lying || d > body * 2f || a.hasGoal))
                {
                    float catchUp = s.hops ? s.burstSpeed : a.hasGoal ? 1.7f : 1.35f;
                    float pace = a.act == AnimalActivity.Burrowed ? Mathf.Max(mul, 2.3f) : a.act == AnimalActivity.Slide && a.step == SlideGlide ? 4.5f : mul;
                    Vector2 next = a.pos + toD / d * Mathf.Min(s.speed * pace * catchUp * dt, d - stop * 0.5f);
                    if (next == a.pos || CanStepMember(s, a, _surface.SampleHeight(next), _surface.SampleHeight(a.pos)))
                    {
                        bool stepped = next != a.pos;
                        if (stepped) { a.pos = next; changed = true; }
                        if (stepped && (d > body || a.hasGoal)) { memberMoving = true; face = toD; }
                        a.blocked = 0f;
                    }
                    else if (a.hasGoal && a.act != AnimalActivity.Parade)
                    {
                        a.blocked += dt;
                        if (a.blocked > 2f)
                        {
                            if (herd.lookout == a) ReleaseLookout(herd);
                            else ClearGoal(a);
                        }
                    }
                }
                if (moving && gaitOn && !waiting && !a.hasGoal) memberMoving = true;
                if (memberMoving && face != Vector2.zero)
                {
                    float yaw = Mathf.LerpAngle(a.yaw, Mathf.Atan2(face.x, face.y) * Mathf.Rad2Deg, turn);
                    if (yaw != a.yaw) { a.yaw = yaw; changed = true; }
                }
                if (SetMoving(a, memberMoving)) changed = true;
                if (a.hasGoal && !memberMoving && d <= stop * 1.5f) { if (GoalStep(herd, a, dt)) changed = true; }
                else if (memberMoving || (moving && !waiting && !a.hasGoal)) { if (SetState(a, AnimalState.Walk, 0f, wedge ? LookPitch : WalkPitch, false)) changed = true; }
                else if (a.hasGoal || a.act == AnimalActivity.Dig) { }
                else if (!safe) { if (SetState(a, AnimalState.Look, 0f, LookPitch, true)) changed = true; }
                else if (IdleStep(herd, a, i, dt)) changed = true;
                if (a.shore && !a.hasGoal && _surface.SampleHeight(a.pos) >= s.minH)
                {
                    a.shore = false;
                    if (s.kind == LifeKind.Capybara && safe && behaviourRate > 0f && a.act == AnimalActivity.None) { StartShake(a); changed = true; }
                }
                if (a.act == AnimalActivity.Stampede && herd.stampT <= 0f) a.act = AnimalActivity.None;
            }
            if (herd.diving && !burrowBound) herd.diving = false;
            // Only a failed move attempt earns a retry pause; a plain countdown passing zero must fall
            // through to the wander branch next step (this used to trap herds in a 0.5 s loop forever).
            if (tried && !moving && herd.wait <= 0f && !startled) herd.wait = 0.5f;

            if (Grow(herd, dt)) changed = true;
            if (TryBirth(herd, dt, calm && !huddle)) changed = true;
            return changed;
        }

        void EnsureObject()
        {
            if (_go != null && _go.transform.parent == transform) return;
            _go = null;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (child.name != ObjName) continue;
                if (_go == null) _go = child;
                else if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
            if (_go == null)
            {
                _go = new GameObject(ObjName);
                _go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                _go.transform.SetParent(transform, false);
                _go.AddComponent<MeshFilter>();
                var mr = _go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            _filter = _go.GetComponent<MeshFilter>();
            _renderer = _go.GetComponent<MeshRenderer>();
            _renderer.sharedMaterial = AnimalMaterial;
            // After a domain reload the child still references the previous mesh; reuse it rather than leak it.
            if (_mesh == null) _mesh = _filter.sharedMesh;
            if (_mesh == null) _mesh = new Mesh { name = "Herds", hideFlags = HideFlags.DontSave };
            _filter.sharedMesh = _mesh;
        }

        // Positions and headings plus the pose channels; hop, head movement, breathing and the pose blends are
        // per-vertex data for Drift/Animal.
        void RebuildMesh()
        {
            EnsureObject();
            MeshBuilds++;
            _batch.Begin();
            var burrowTpl = LifeMeshes.Burrow;
            foreach (var b in _burrows)
                _batch.Add(burrowTpl, new Vector3(b.x, _surface.SampleHeight(b), b.y), Mathf.Repeat(b.x * 37f + b.y * 91f, 360f), burrowScale);
            foreach (var herd in _herds)
            {
                var s = herd.spec;
                float hop = s.hop * animalScale;
                foreach (var a in herd.members)
                {
                    a.bakedGrowth = a.growth;
                    if (a.hidden || a.dived) continue;
                    float h = _surface.SampleHeight(a.pos);
                    int special = PoseOf(herd, a);
                    var tpl = a.detailed ? LifeMeshes.GetDetailTemplate(s.kind, a.variant, a.growth < youngModelGrowth, special) : LifeMeshes.GetTemplate(s.kind, a.variant);
                    float size = SizeFactor(a);
                    float scale = a.scale * animalScale * size;
                    bool binky = a.act == AnimalActivity.Binky;
                    bool jump = a.act == AnimalActivity.HopChain;
                    bool dance = a.act == AnimalActivity.WarDance && a.step == MovePerform;
                    float hopMul = binky ? 6f : jump ? 9f : dance ? 7f : a.act == AnimalActivity.Zigzag && a.moving ? 2.5f
                        : a.act == AnimalActivity.Stampede && a.moving ? (s.kind == LifeKind.Reindeer ? 1.4f : 3f) : 1f;
                    var pose = new AnimalPose
                    {
                        // The war dance bounces the whole group in step: one shared phase.
                        phase = dance ? herd.sigPhase : a.phase, hopAmplitude = (jump || dance ? Mathf.Max(hop, 0.02f * animalScale) : hop) * size * hopMul,
                        moving = a.moving || binky || jump ? 1f : 0f, alert = a.alert ? 1f : 0f,
                        sleep = a.state == AnimalState.Sleep || (a.stands && a.state == AnimalState.Rest) ? 1f : 0f, t0 = a.t0,
                        pitchFrom = a.pitchFrom, pitchTo = a.pitchTo, restFrom = a.restFrom, restTo = a.restTo
                    };
                    var pos = new Vector3(a.pos.x, h + a.lift, a.pos.y);
                    float roll = 0f, pitch = a.pitch;
                    if (pitch < 0f && RearPivot(s.kind, a.act, out float pivotZ))
                    {
                        // Rearing up (a sitting-up hare, a goat on its hind legs) turns about the hind feet, not the
                        // middle of the body, so they stay on the ground.
                        float pr = pitch * Mathf.Deg2Rad, yr = a.yaw * Mathf.Deg2Rad;
                        float dz = pivotZ * (1f - Mathf.Cos(pr)), dy = pivotZ * Mathf.Sin(pr);
                        pos += new Vector3(Mathf.Sin(yr) * dz * scale, dy * scale, Mathf.Cos(yr) * dz * scale);
                    }
                    if (special == AnimalModels.PoseCall && !a.detailed) pitch = -18f;
                    if (special == 1 && s.kind == LifeKind.Penguin)
                    {
                        // Belly slide: the detail pose is a template of its own, the simple box is tipped over.
                        if (!a.detailed) { pitch = BellyPitch; pos.y += 0.1f * scale; }
                        pose.pitchFrom = pose.pitchTo = pose.restFrom = pose.restTo = 0f;
                        pose.moving = pose.alert = 0f;
                    }
                    else if (special != 0 && s.kind == LifeKind.Meerkat && !a.detailed) { pitch = UprightPitch; pos.y += 0.12f * scale; }
                    else if (a.detailed && a.moving && s.kind == LifeKind.Penguin) roll = 11f * Mathf.Sin(_clock * 9f + a.phase);
                    if (a.roll != 0f && a.act != AnimalActivity.Wallow) roll = a.roll;
                    if (pitch != 0f) { pose.pitchFrom = pose.pitchTo = 0f; pose.moving = 0f; }
                    if (a.act == AnimalActivity.Wallow && a.detailed && tpl.lever != null)
                    {
                        // The pose is baked instead of blended: lying = lowered by the leg length (the legs start
                        // out below ground and come up as the body rolls about its own axis).
                        roll = a.roll;
                        float rad = roll * Mathf.Deg2Rad, sn = Mathf.Sin(rad), cs = Mathf.Cos(rad);
                        float axisY = tpl.legLength + tpl.rollAxis;
                        float lift = tpl.rollAxis * (Mathf.Abs(sn) + Mathf.Abs(cs) - 1f);
                        float yawRad = a.yaw * Mathf.Deg2Rad;
                        Vector3 right = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad));
                        pos += right * (sn * axisY * scale) + Vector3.up * ((tpl.rollAxis + lift - cs * axisY) * scale);
                        pose.pitchFrom = pose.pitchTo = 0f;
                        pose.restFrom = pose.restTo = 0f;
                        pose.moving = pose.alert = 0f;
                        pose.sleep = 1f;
                    }
                    _batch.AddAnimal(tpl, pos, a.yaw, scale, pose, youngTint, youngTintAmount * (1f - a.growth), roll, pitch,
                        Markings.For(s.kind, a.variant, tpl), Markings.Seed(a.scale * 97.3f));
                    if (a == herd.sigA && a.act == AnimalActivity.Snuggle && a.step == MovePerform && a.sigT >= 1f)
                    {
                        // The capybara's passenger: a little egret on the back of the one in the middle of the star.
                        float back = a.detailed ? 0.23f : 0.19f;
                        float yr = a.yaw * Mathf.Deg2Rad;
                        var birdPos = new Vector3(a.pos.x - Mathf.Sin(yr) * 0.05f * scale, h + back * scale, a.pos.y - Mathf.Cos(yr) * 0.05f * scale);
                        _batch.Add(AnimalModels.Egret, birdPos, a.yaw, scale);
                    }
                }
            }
            _batch.Apply(_mesh);
        }

        public void ShiftLocal(Vector2 delta)
        {
            foreach (var h in _herds)
            {
                h.center += delta;
                h.target += delta;
                h.burrow += delta;
                h.sparMid += delta;
                h.errandPos += delta;
                h.stampC += delta;
                foreach (var a in h.members)
                {
                    a.pos += delta;
                    a.goal += delta;
                    a.from += delta;
                }
            }
            for (int i = 0; i < _burrows.Count; i++) _burrows[i] += delta;
            if (_hasPoi) _poiLocal += delta;
        }

        public void AbsorbFrom(IslandHerdSystem other)
        {
            if (other == null || other == this) return;
            foreach (var h in other._herds)
            {
                EndPlay(h, false);
                ResetBehaviour(h);
                h.center = Convert(other.transform, h.center);
                h.target = h.center;
                h.fleeTimer = 0f;
                foreach (var a in h.members) a.pos = Convert(other.transform, a.pos);
                _herds.Add(h);
            }
            other._herds.Clear();
            foreach (var b in other._burrows) AddBurrow(Convert(other.transform, b));
            other._burrows.Clear();
            EnforceCaps();
            _meshDirty = true;
        }

        // maxHerds / maxAnimals hold after merges too. The surplus always comes from the species with the most
        // animals (its smallest herd), so a merge never wipes out the few oxen or goats: a surplus herd joins
        // the nearest herd of its species while that one has room, otherwise it goes; then herds are trimmed
        // or dropped the same way until the animal cap holds, which keeps a 600-area island in one ~7k-vertex mesh.
        void EnforceCaps()
        {
            int guard = 0;
            while (_herds.Count > maxHerds && guard++ < 512)
            {
                var surplus = SurplusHerd();
                var host = NearestSameSpecies(surplus);
                if (host != null && host.members.Count + surplus.members.Count <= host.spec.maxSize)
                {
                    EndPlay(host, false);
                    int first = host.members.Count;
                    foreach (var a in surplus.members)
                    {
                        if (a.parent == null) a.offset = FormationOffset(host, host.members.Count);
                        host.members.Add(a);
                    }
                    // Young keep their parent (it moved with them); their slot is only known once every adult has one.
                    for (int i = first; i < host.members.Count; i++)
                    {
                        var a = host.members[i];
                        a.pos = Slot(host, a, 1f);
                        if (!Valid(host.spec, _surface.SampleHeight(a.pos))) a.pos = host.center;
                    }
                    surplus.members.Clear();
                }
                _herds.Remove(surplus);
            }
            guard = 0;
            while (AnimalCount > maxAnimals && _herds.Count > 0 && guard++ < 512)
            {
                var surplus = SurplusHerd();
                int over = AnimalCount - maxAnimals;
                if (surplus.members.Count > over + 1)
                {
                    EndPlay(surplus, false);
                    surplus.members.RemoveRange(surplus.members.Count - over, over);
                    Orphans(surplus);
                    break;
                }
                _herds.Remove(surplus);
            }
        }

        static readonly int[] SpeciesAnimals = new int[Specs.Length];
        static readonly int[] SpeciesHerds = new int[Specs.Length];

        Herd SurplusHerd()
        {
            System.Array.Clear(SpeciesAnimals, 0, SpeciesAnimals.Length);
            System.Array.Clear(SpeciesHerds, 0, SpeciesHerds.Length);
            foreach (var h in _herds)
            {
                int si = System.Array.IndexOf(Specs, h.spec);
                SpeciesAnimals[si] += h.members.Count;
                SpeciesHerds[si]++;
            }
            // A species down to its last herd is spared while another still has several: a merge must not cost
            // the player a collected species.
            int most = -1;
            for (int i = 0; i < SpeciesAnimals.Length; i++)
                if (SpeciesHerds[i] > 1 && (most < 0 || SpeciesAnimals[i] > SpeciesAnimals[most])) most = i;
            if (most < 0)
            {
                most = 0;
                for (int i = 1; i < SpeciesAnimals.Length; i++) if (SpeciesAnimals[i] > SpeciesAnimals[most]) most = i;
            }
            Herd best = null;
            foreach (var h in _herds)
                if (h.spec == Specs[most] && (best == null || h.members.Count < best.members.Count)) best = h;
            return best;
        }

        Herd NearestSameSpecies(Herd of)
        {
            Herd best = null;
            float bestD = float.MaxValue;
            foreach (var h in _herds)
            {
                if (h == of || h.spec != of.spec) continue;
                float d = (h.center - of.center).sqrMagnitude;
                if (d < bestD) { bestD = d; best = h; }
            }
            return best;
        }

        Vector2 FormationOffset(Herd herd, int index)
        {
            float ang = Rand(0f, Mathf.PI * 2f);
            float r = herd.spec.body * animalScale * formationSpacing * Mathf.Sqrt(index + Rand());
            return new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
        }

        Vector2 Convert(Transform from, Vector2 p)
        {
            Vector3 w = from.TransformPoint(p.x, 0f, p.y);
            Vector3 l = transform.InverseTransformPoint(w);
            return new Vector2(l.x, l.z);
        }

        // ---------------------------------------------------------- persistence

        // Animal states and timers are transient: Restore re-seeds them from the night at load time.
        public List<HerdSaveData> Capture()
        {
            var list = new List<HerdSaveData>(_herds.Count);
            foreach (var h in _herds)
            {
                int n = h.members.Count;
                var d = new HerdSaveData
                {
                    kind = (int)h.spec.kind,
                    cx = h.center.x, cz = h.center.y, tx = h.target.x, tz = h.target.y,
                    wait = h.wait, grow = h.growTimer,
                    m = new float[n * 6], variant = new int[n],
                    growth = new float[n], age = new float[n], parent = new int[n]
                };
                for (int i = 0; i < n; i++)
                {
                    var a = h.members[i];
                    d.m[i * 6] = a.pos.x; d.m[i * 6 + 1] = a.pos.y;
                    d.m[i * 6 + 2] = a.offset.x; d.m[i * 6 + 3] = a.offset.y;
                    d.m[i * 6 + 4] = a.yaw; d.m[i * 6 + 5] = a.scale;
                    d.variant[i] = a.variant;
                    d.growth[i] = a.growth;
                    d.age[i] = a.age;
                    d.parent[i] = a.parent != null ? h.members.IndexOf(a.parent) : -1;
                }
                list.Add(d);
            }
            // Burrows ride behind the first herd's member block (x, z pairs): HerdSaveData has no field for them,
            // and every reader takes min(variant.Length, m.Length / 6) members, so files stay readable both ways.
            if (list.Count > 0 && _burrows.Count > 0)
            {
                var first = list[0];
                int n = first.variant.Length;
                System.Array.Resize(ref first.m, n * 6 + _burrows.Count * 2);
                for (int i = 0; i < _burrows.Count; i++)
                {
                    first.m[n * 6 + i * 2] = _burrows[i].x;
                    first.m[n * 6 + i * 2 + 1] = _burrows[i].y;
                }
            }
            return list;
        }

        static Species SpecFor(int kind)
        {
            foreach (var s in Specs) if ((int)s.kind == kind) return s;
            return null;
        }

        public void Restore(List<HerdSaveData> saved)
        {
            if (_surface == null) _surface = GetComponent<IIslandSurface>();
            if (_surface == null || saved == null) return;
            _life ??= GetComponent<IslandLifeSystem>();
            _rnd ??= new System.Random(HerdSeed);
            _night = LifeEnvironment.NightAmount;
            _herds.Clear();
            _burrows.Clear();
            if (saved.Count > 0 && saved[0] != null && saved[0].m != null && saved[0].variant != null)
            {
                var first = saved[0];
                for (int i = first.variant.Length * 6; i + 1 < first.m.Length && _burrows.Count < maxBurrows; i += 2)
                    _burrows.Add(new Vector2(first.m[i], first.m[i + 1]));
            }
            foreach (var d in saved)
            {
                if (d == null) continue;
                var s = SpecFor(d.kind);
                if (s == null || d.m == null || d.variant == null) continue;
                var herd = new Herd
                {
                    spec = s,
                    center = new Vector2(d.cx, d.cz), target = new Vector2(d.tx, d.tz),
                    wait = d.wait, growTimer = d.grow
                };
                int n = Mathf.Min(d.variant.Length, d.m.Length / 6);
                bool hasGrowth = d.growth != null && d.growth.Length >= n;
                bool hasAge = d.age != null && d.age.Length >= n;
                bool hasParent = d.parent != null && d.parent.Length >= n;
                for (int i = 0; i < n; i++)
                {
                    var a = new Animal
                    {
                        pos = new Vector2(d.m[i * 6], d.m[i * 6 + 1]),
                        offset = new Vector2(d.m[i * 6 + 2], d.m[i * 6 + 3]),
                        yaw = d.m[i * 6 + 4], scale = d.m[i * 6 + 5],
                        variant = d.variant[i], phase = Rand(0f, 6.28f),
                        growth = hasGrowth ? Mathf.Clamp01(d.growth[i]) : 1f
                    };
                    a.age = hasAge ? Mathf.Max(0f, d.age[i]) : a.growth * s.grow;
                    a.bakedGrowth = a.growth;
                    herd.members.Add(a);
                }
                for (int i = 0; i < n; i++)
                {
                    var a = herd.members[i];
                    if (!Young(a)) continue;
                    int p = hasParent ? d.parent[i] : -1;
                    if (p >= 0 && p < n && p != i && !Young(herd.members[p])) a.parent = herd.members[p];
                    else Adopt(herd, a, i);
                }
                if (herd.members.Count > 0) _herds.Add(herd);
            }
            _version = _surface.Version;
            _populated = true;
            _peakArea = _surface.LandArea;
            Relocate();
            PruneBurrows();
            EnforceCaps();
            SeedStates();
            UpdateDetail();
            RebuildMesh();
            _meshTimer = 0f;
            _meshDirty = false;
        }
    }
}
