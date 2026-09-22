using System;
using System.Collections.Generic;
using Drift.Core;
using Drift.Islands;
using UnityEngine;

namespace Drift.Visuals
{
    public enum EncounterKind { None, Dolphins, Whale, Flotsam }

    // Adventure only: a sea animal that was picked up on the track and now swims alongside the island.
    public enum CompanionKind { Whale, Turtle, FishShoal }

    public struct EncounterConditions
    {
        public float speed;
        public bool openWater;
        public float storm;
        public float night;
        public bool adventure;
        // Something is already happening around the player (an escort, a surfacing whale, flotsam ahead).
        public bool busy;
    }

    // What collecting a piece of flotsam is worth. Cozy: a little buoyancy back (share of the full sink time) or a
    // message in a bottle with a watching hint; Adventure: buoyancy is the whole survival resource there (islands
    // are obstacles, nothing else lifts you any more), plus a short speed boost.
    public struct FlotsamReward
    {
        public float buoyancy;
        public float boostSeconds;
        public bool bottleNote;
    }

    // Pure pacing of the travel encounters: a timer that only runs while the player really drives (slower at night,
    // halted in a heavy storm, held while an encounter is still going) and picks what comes next from what the
    // conditions allow, never the same kind twice in a row unless nothing else fits.
    public sealed class EncounterPacer
    {
        public float minGap = 20f;
        public float maxGap = 40f;
        public float firstGapMin = 10f;
        public float firstGapMax = 20f;
        public float driveSpeed = 1.6f;
        public float dolphinSpeed = 2.4f;
        public float nightFactor = 0.6f;
        public float stormCalm = 0.35f;
        public float stormHalt = 0.7f;
        public float whaleNightLimit = 0.6f;
        public float whaleMinGap = 150f;
        public float retrySeconds = 4f;
        public float dolphinWeight = 0.4f;
        public float whaleWeight = 0.22f;
        public float flotsamWeight = 0.38f;

        uint _seed;
        float _timer;
        float _clock;
        float _lastWhale = -1e9f;

        public float Timer => _timer;
        public float Clock => _clock;
        public float DrivenSeconds { get; private set; }
        public EncounterKind Last { get; private set; }
        public int Fired { get; private set; }

        public EncounterPacer(uint seed)
        {
            Reset(seed);
        }

        public void Reset(uint seed)
        {
            _seed = seed == 0u ? 1u : seed;
            _clock = 0f;
            _lastWhale = -1e9f;
            DrivenSeconds = 0f;
            Last = EncounterKind.None;
            Fired = 0;
            _timer = Mathf.Lerp(firstGapMin, firstGapMax, SeaMath.Rand(_seed, 1));
        }

        // Driving-seconds per real second: 0 while resting or in a heavy storm, slower at night.
        public float Rate(in EncounterConditions c)
        {
            if (c.speed < driveSpeed || c.storm >= stormHalt) return 0f;
            return Mathf.Lerp(1f, nightFactor, Mathf.Clamp01(c.night));
        }

        public bool Allowed(EncounterKind k, in EncounterConditions c)
        {
            switch (k)
            {
                case EncounterKind.Dolphins:
                    return c.openWater && c.speed >= dolphinSpeed && c.storm < stormCalm;
                case EncounterKind.Whale:
                    return !c.adventure && c.openWater && c.storm < stormCalm && c.night < whaleNightLimit
                        && _clock - _lastWhale >= whaleMinGap;
                case EncounterKind.Flotsam:
                    // In Adventure the ring lays out its own steady stream of flotsam (Encounters.TickTrack).
                    return !c.adventure && c.storm < stormHalt;
                default:
                    return false;
            }
        }

        float Weight(EncounterKind k) =>
            k == EncounterKind.Dolphins ? dolphinWeight : k == EncounterKind.Whale ? whaleWeight : flotsamWeight;

        public EncounterKind Pick(in EncounterConditions c, float roll)
        {
            float total = 0f;
            int options = 0;
            for (int k = 1; k <= 3; k++)
                if (Allowed((EncounterKind)k, c)) options++;
            if (options == 0) return EncounterKind.None;
            for (int k = 1; k <= 3; k++)
            {
                var kind = (EncounterKind)k;
                if (!Allowed(kind, c) || (options > 1 && kind == Last)) continue;
                total += Weight(kind);
            }
            if (total <= 0f) return EncounterKind.None;
            float r = Mathf.Clamp01(roll) * total;
            EncounterKind pick = EncounterKind.None;
            for (int k = 1; k <= 3; k++)
            {
                var kind = (EncounterKind)k;
                if (!Allowed(kind, c) || (options > 1 && kind == Last)) continue;
                pick = kind;
                r -= Weight(kind);
                if (r < 0f) break;
            }
            return pick;
        }

        // Advances the pacing; returns the encounter to start now (None most of the time). The caller answers with
        // Confirm when it started, or leaves it: the pacer then tries again after retrySeconds of driving.
        public EncounterKind Tick(float dt, in EncounterConditions c)
        {
            _clock += dt;
            if (c.busy) return EncounterKind.None;
            float rate = Rate(c);
            if (rate <= 0f) return EncounterKind.None;
            DrivenSeconds += dt;
            _timer -= dt * rate;
            if (_timer > 0f) return EncounterKind.None;
            var k = Pick(c, SeaMath.Rand(_seed, 100 + Fired * 7 + (int)(DrivenSeconds * 3f)));
            _timer = retrySeconds;
            return k;
        }

        public void Confirm(EncounterKind k)
        {
            Last = k;
            Fired++;
            if (k == EncounterKind.Whale) _lastWhale = _clock;
            _timer = Mathf.Lerp(minGap, maxGap, SeaMath.Rand(_seed, 17 + Fired * 13));
        }

        public static FlotsamReward RewardFor(ShipSystem.FlotsamKind kind, bool adventure)
        {
            var r = new FlotsamReward();
            if (adventure)
            {
                // The buoyancy is the survival resource and stays as it was; the push is what the run is about, so
                // a piece is worth a real surge (owner: "die eingesammelten gegenstände sollen auch mehr boost geben").
                switch (kind)
                {
                    case ShipSystem.FlotsamKind.Crate: r.buoyancy = 0.09f; r.boostSeconds = 3f; break;
                    case ShipSystem.FlotsamKind.Barrel: r.buoyancy = 0.08f; r.boostSeconds = 2.8f; break;
                    case ShipSystem.FlotsamKind.Bottle: r.buoyancy = 0.06f; r.boostSeconds = 2.2f; break;
                    case ShipSystem.FlotsamKind.PalmLog: r.buoyancy = 0.06f; r.boostSeconds = 2.2f; break;
                    case ShipSystem.FlotsamKind.Buoy: return r;
                    default: r.buoyancy = 0.05f; r.boostSeconds = 2.4f; break;
                }
                return r;
            }
            switch (kind)
            {
                case ShipSystem.FlotsamKind.Barrel:
                case ShipSystem.FlotsamKind.Crate:
                    r.buoyancy = 0.04f;
                    break;
                case ShipSystem.FlotsamKind.Bottle:
                    r.buoyancy = 0.01f;
                    r.bottleNote = true;
                    break;
                case ShipSystem.FlotsamKind.Buoy:
                    break;
                default:
                    r.buoyancy = 0.02f;
                    break;
            }
            return r;
        }

        static readonly string[] Notes =
        {
            "Flaschenpost: „Folge den Delfinen – sie kennen die schönsten Wege.“",
            "Flaschenpost: „Wer langsam treibt, hört die Wale atmen.“",
            "Flaschenpost: „Nachts leuchten die Quallen wie kleine Laternen.“",
            "Flaschenpost: „Wo Möwen kreisen, tanzt ein Fischschwarm.“",
            "Flaschenpost: „Grüß die Schildkröten von mir, sie reisen von Insel zu Insel.“",
            "Flaschenpost: „Hinter jedem Sturm wartet ruhiges Wasser.“",
            "Flaschenpost: „Halt mal an und schau zu – das Meer erzählt viel.“"
        };

        public static int NoteCount => Notes.Length;
        public static string BottleNote(uint seed) => Notes[(int)(SeaMath.Hash(seed, 71u) % (uint)Notes.Length)];

        // A point ahead of the player on its course: `lead` beyond the bow, `lateral` to the right (negative = left).
        public static Vector2 AheadPoint(Vector2 pos, Vector2 dir, float radius, float lead, float lateral)
        {
            Vector2 f = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.up;
            Vector2 right = new Vector2(f.y, -f.x);
            return pos + f * (radius + lead) + right * lateral;
        }
    }

    // Travel encounters around the moving player (owner: "die Fahrt zwischen den Inseln ist langweilig"): every
    // 20-40 s of real driving a dolphin pod joins the bow wave, a whale surfaces beside the course (Cozy only) or a
    // little cargo of flotsam drifts ahead to be collected by running it over. Only directs: the animals and pieces
    // are SeaLifeSystem / ShipSystem entries, so tapping, the journal, audio and the meshes all stay as they are.
    [ExecuteAlways]
    [DefaultExecutionOrder(214)]
    public class Encounters : MonoBehaviour
    {
        public WaterFeedback water;
        public SeaLifeSystem seaLife;
        public ShipSystem ships;
        public FishSystem fish;
        public int seed = 7;

        [Header("Takt")]
        [Tooltip("Kürzeste Fahrzeit (s) zwischen zwei Begegnungen.")]
        public float minGap = 20f;
        [Tooltip("Längste Fahrzeit (s) zwischen zwei Begegnungen.")]
        public float maxGap = 40f;
        [Tooltip("Ab dieser Geschwindigkeit (u/s) zählt die Fahrt für den Takt.")]
        public float driveSpeed = 1.6f;
        [Tooltip("Delfine kommen erst ab dieser Geschwindigkeit (u/s) an den Bug.")]
        public float dolphinSpeed = 2.4f;
        [Tooltip("So viel Abstand (u) muss vor dem Bug bis zur nächsten Insel frei sein (offenes Meer).")]
        public float openWaterMargin = 18f;
        [Tooltip("Nachts läuft der Takt nur mit diesem Anteil.")]
        [Range(0.1f, 1f)] public float nightFactor = 0.6f;
        [Tooltip("Mindestabstand (s) zwischen zwei Wal-Begegnungen.")]
        public float whaleMinGap = 150f;

        [Header("Begegnungen")]
        [Tooltip("So lange (s) begleiten die Delfine die Insel.")]
        public float dolphinRideSeconds = 24f;
        [Tooltip("Der Wal taucht so viele Sekunden Fahrt voraus auf.")]
        public float whaleLeadSeconds = 9f;
        [Tooltip("Seitlicher Abstand (u) des Wals vom Inselrand, zusätzlich zum festen Wal-Sicherheitsabstand (12 u).")]
        public float whaleLateral = 6f;
        [Tooltip("Treibgut liegt so viele Sekunden Fahrt voraus.")]
        public float flotsamLeadSeconds = 6f;

        [Header("Treibgut im Abenteuer")]
        [Tooltip("So weit (u) voraus legt der Ring neues Treibgut auf die Bahn. Der Abstand zwischen zwei Gruppen kommt aus der Schwierigkeit (RingWorld).")]
        [Range(20f, 120f)] public float trackLookahead = 62f;
        [Tooltip("Anteil der Gruppen, die als Linie durch eine Lücke neben einer Insel führen: verlockend, aber eng.")]
        [Range(0f, 1f)] public float trackGapLines = 0.45f;
        [Tooltip("Anteil der Gruppen, die entlang einer Plattengrenze liegen: einsammeln, während man surft.")]
        [Range(0f, 1f)] public float trackSeamLines = 0.3f;
        [Tooltip("Abstand (u) der einzelnen Teile innerhalb einer Linie.")]
        [Range(2f, 12f)] public float trackLineStep = 4.5f;

        [Header("Abenteuer-Beute")]
        [Tooltip("Abstand (u) zwischen zwei Meerestieren auf der Bahn: Wal, Schildkröte oder Fischschwarm zum Einsammeln.")]
        [Range(40f, 400f)] public float animalSpacing = 110f;
        [Tooltip("Anteil Wale, danach Schildkröten, der Rest ist ein Fischschwarm.")]
        [Range(0f, 1f)] public float animalWhaleShare = 0.3f;
        [Tooltip("Anteil Schildkröten (nach den Walen).")]
        [Range(0f, 1f)] public float animalTurtleShare = 0.3f;
        [Tooltip("Zusätzliches Tempo je mitschwimmendem Tier (0,2 = +20 %).")]
        [Range(0f, 0.5f)] public float companionBoost = 0.18f;
        [Tooltip("So viele Begleiter zählen höchstens für das Tempo.")]
        [Range(1, 5)] public int maxCompanionStack = 3;

        [Header("Schiffe im Abenteuer")]
        [Tooltip("So lange (s) bremst ein Rempler gegen ein Schiff die Insel aus.")]
        [Range(0f, 4f)] public float shipStaggerSeconds = 1.1f;
        [Tooltip("Auf so viel Tempo fällt die Insel beim Rempler (0,45 = 45 %).")]
        [Range(0.1f, 1f)] public float shipStaggerFactor = 0.45f;

        [Header("Belohnung")]
        [Tooltip("Meldet beim Einsammeln einer Flaschenpost einen kleinen Hinweistext (Event BottleNoteFound).")]
        public bool announceBottleNotes = true;

        // Collected flotsam (kind, world position of the splash). Raised by ShipSystem when the player runs a piece over.
        public static event Action<ShipSystem.FlotsamKind, Vector3> FlotsamCollected;
        // An encounter was started by the director (audio stings, HUD hints).
        public static event Action<EncounterKind, Vector3> Started;
        // A message in a bottle was opened: German text for a toast.
        public static event Action<string> BottleNoteFound;
        // Adventure: a sea animal joined the island as an escort (toast, audio sting).
        public static event Action<CompanionKind, Vector3> CompanionJoined;
        // Adventure: the island rammed a ship - no reward, only the bump (audio, HUD, camera shake).
        public static event Action<ShipSystem.ShipKind, Vector3> ShipHit;

        public static int CompanionsTotal { get; private set; }
        public static int ShipHits { get; private set; }
        public static CompanionKind LastCompanion { get; private set; }

        public static int CollectedTotal { get; private set; }
        public static ShipSystem.FlotsamKind LastCollected { get; private set; }
        public static FlotsamReward RewardFor(ShipSystem.FlotsamKind kind) => EncounterPacer.RewardFor(kind, GameModes.IsAdventure);

        EncounterPacer _pacer;
        Island _player;
        float _activeLeft;
        EncounterKind _active;
        int _started;

        public EncounterPacer Pacer => _pacer;
        public EncounterKind Active => _activeLeft > 0f ? _active : EncounterKind.None;
        public float ActiveLeft => _activeLeft;
        public int StartedCount => _started;
        public EncounterConditions LastConditions { get; private set; }

        internal static void NotifyCollected(ShipSystem.FlotsamKind kind, Vector3 pos, uint pieceSeed)
        {
            CollectedTotal++;
            LastCollected = kind;
            if (GameModes.IsAdventure && ShipSystem.IsCollectible(kind)) AdventureRunStats.Flotsam++;
            FlotsamCollected?.Invoke(kind, pos);
            ApplyReward(RewardFor(kind));
            if (kind == ShipSystem.FlotsamKind.Bottle && BottleNoteFound != null)
            {
                var inst = _instance;
                if (inst == null || inst.announceBottleNotes) BottleNoteFound(EncounterPacer.BottleNote(pieceSeed));
            }
            if (_instance != null && _instance._active == EncounterKind.Flotsam && _instance.ships != null && _instance.ships.RouteFlotsamLeft == 0)
                _instance._activeLeft = Mathf.Min(_instance._activeLeft, 2f);
        }

        // A sea animal was run over and now escorts the island (SeaLifeSystem / FishSystem call this).
        internal static void NotifyCompanion(CompanionKind kind, Vector3 pos)
        {
            CompanionsTotal++;
            LastCompanion = kind;
            CompanionJoined?.Invoke(kind, pos);
        }

        // The island drove into a ship: ships are obstacles in Adventure, never a reward. Costs speed, not buoyancy
        // (owner: the obstacle islands stay the hard danger).
        public static void NotifyShipHit(ShipSystem.ShipKind kind, Vector3 pos)
        {
            ShipHits++;
            var inst = _instance;
            Island player = FindPlayer();
            if (player != null && inst != null) player.Stagger(inst.shipStaggerSeconds, inst.shipStaggerFactor);
            ShipHit?.Invoke(kind, pos);
        }

        static Island FindPlayer()
        {
            Island player = _instance != null ? _instance._player : null;
            if (player != null) return player;
            var all = Island.All;
            for (int i = 0; i < all.Count; i++) if (all[i] != null && all[i].useKeyboardInput) return all[i];
            return null;
        }

        static Encounters _instance;

        [Tooltip("Schub-Faktor, den Treibgut im Abenteuer gibt (die Dauer kommt aus der Belohnung).")]
        [Range(1f, 2f)] public float flotsamBoostFactor = 1.55f;

        static void ApplyReward(FlotsamReward reward)
        {
            Island player = FindPlayer();
            if (player == null) return;
            if (reward.buoyancy > 0f) player.AddBuoyancy(reward.buoyancy);
            if (reward.boostSeconds > 0f) player.SpeedBoost(reward.boostSeconds, _instance != null ? _instance.flotsamBoostFactor : 1.35f);
        }

        void OnEnable()
        {
            _instance = this;
            Resolve();
            EnsurePacer();
        }

        void OnDisable()
        {
            if (_instance == this) _instance = null;
        }

        void Resolve()
        {
            if (water == null) water = GetComponent<WaterFeedback>();
            if (water == null) water = FindAnyObjectByType<WaterFeedback>();
            if (seaLife == null) seaLife = GetComponent<SeaLifeSystem>();
            if (seaLife == null) seaLife = FindAnyObjectByType<SeaLifeSystem>();
            if (ships == null) ships = GetComponent<ShipSystem>();
            if (ships == null) ships = FindAnyObjectByType<ShipSystem>();
            if (fish == null) fish = GetComponent<FishSystem>();
            if (fish == null) fish = FindAnyObjectByType<FishSystem>();
        }

        void EnsurePacer()
        {
            if (_pacer == null) _pacer = new EncounterPacer((uint)seed * 2654435761u + (uint)Environment.TickCount);
            _pacer.minGap = minGap;
            _pacer.maxGap = Mathf.Max(minGap, maxGap);
            _pacer.driveSpeed = driveSpeed;
            _pacer.dolphinSpeed = dolphinSpeed;
            _pacer.nightFactor = nightFactor;
            _pacer.whaleMinGap = whaleMinGap;
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            EnsurePacer();
            if (water == null || seaLife == null || ships == null) Resolve();
            _player = water != null ? water.FindPlayer() : null;
            if (_activeLeft > 0f) _activeLeft -= dt;
            if (_player == null || _player.IsSunk) return;
            TickTrack();
            TickCompanions();

            var c = Conditions();
            LastConditions = c;
            var k = _pacer.Tick(dt, c);
            if (k != EncounterKind.None && Begin(k)) _pacer.Confirm(k);
        }

        EncounterConditions Conditions()
        {
            Vector2 vel = _player.PlanarVelocity;
            float speed = vel.magnitude;
            if (seaLife != null && seaLife.debugPlayerVelocity.sqrMagnitude > 0f)
            {
                vel = seaLife.debugPlayerVelocity;
                speed = vel.magnitude;
            }
            bool busy = _activeLeft > 0f;
            if (seaLife != null && (seaLife.DolphinsBowRiding || seaLife.WhaleBreaching)) busy = true;
            return new EncounterConditions
            {
                speed = speed,
                openWater = OpenWater(vel),
                storm = water != null ? water.Storm : LifeEnvironment.Storm,
                night = seaLife != null && seaLife.debugNight >= 0f ? seaLife.debugNight : LifeEnvironment.NightAmount,
                adventure = GameModes.IsAdventure,
                busy = busy
            };
        }

        bool OpenWater(Vector2 vel)
        {
            Vector2 pos = _player.PlanarPosition;
            float r = _player.BoundingRadius;
            Vector2 ahead = EncounterPacer.AheadPoint(pos, vel, r, 10f, 0f);
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var isl = all[i];
                if (isl == null || isl == _player || !isl.isActiveAndEnabled || isl.IsSunk) continue;
                float reach = isl.BoundingRadius + openWaterMargin;
                if ((isl.PlanarPosition - ahead).sqrMagnitude < reach * reach) return false;
                if ((isl.PlanarPosition - pos).sqrMagnitude < (reach + r) * (reach + r)) return false;
            }
            return true;
        }

        // Starts an encounter now regardless of the pacing (verification, debug menu). Returns false when there was
        // no place for it (no player, no open water for the whale, pools unavailable).
        public bool Trigger(EncounterKind k)
        {
            Resolve();
            if (water != null) _player = water.FindPlayer();
            if (_player == null || _player.IsSunk) return false;
            bool ok = Begin(k);
            if (ok) _pacer?.Confirm(k);
            return ok;
        }

        bool Begin(EncounterKind k)
        {
            Vector2 pos = _player.PlanarPosition;
            Vector2 vel = _player.PlanarVelocity;
            if (seaLife != null && seaLife.debugPlayerVelocity.sqrMagnitude > 0f) vel = seaLife.debugPlayerVelocity;
            float speed = vel.magnitude;
            Vector2 dir = speed > 0.05f ? vel / speed : new Vector2(_player.BodyForward.x, _player.BodyForward.z);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.up;
            float r = _player.BoundingRadius;
            uint h = SeaMath.Hash((uint)seed, (uint)(_started * 31 + (_pacer != null ? _pacer.Fired : 0) * 7 + (int)(Time.realtimeSinceStartup * 10f)));
            Vector3 at;
            switch (k)
            {
                case EncounterKind.Dolphins:
                {
                    if (seaLife == null) return false;
                    int slot = seaLife.StartDolphinEscort(dolphinRideSeconds * (0.8f + 0.4f * SeaMath.Rand(h, 1)), SeaMath.Rand(h, 2) < 0.5f);
                    if (slot < 0) return false;
                    Vector2 p = seaLife.GroupPosition(slot);
                    at = new Vector3(p.x, 0f, p.y);
                    _activeLeft = dolphinRideSeconds + 8f;
                    break;
                }
                case EncounterKind.Whale:
                {
                    if (seaLife == null) return false;
                    float lead = Mathf.Max(22f, speed * whaleLeadSeconds);
                    float surfaceIn = speed > 0.5f ? Mathf.Clamp(lead / speed - 5f, 1.5f, 8f) : 3f;
                    int slot = seaLife.StartWhaleEncounter(lead, whaleLateral, surfaceIn, SeaMath.Rand(h, 3) < 0.4f, SeaMath.Rand(h, 4) < 0.5f);
                    if (slot < 0) return false;
                    Vector2 p = seaLife.GroupPosition(slot);
                    at = new Vector3(p.x, 0f, p.y);
                    _activeLeft = 30f;
                    break;
                }
                case EncounterKind.Flotsam:
                {
                    if (ships == null) return false;
                    float lead = Mathf.Max(16f, speed * flotsamLeadSeconds);
                    int placed = PlaceCargo(pos, dir, r, lead, h);
                    if (placed == 0) return false;
                    Vector2 p = EncounterPacer.AheadPoint(pos, dir, r, lead, 0f);
                    at = new Vector3(p.x, 0f, p.y);
                    _activeLeft = (speed > 0.5f ? lead / speed : 10f) + 6f;
                    break;
                }
                default:
                    return false;
            }
            _active = k;
            _started++;
            Started?.Invoke(k, at);
            return true;
        }

        // A little lost cargo in the player's path: a single piece, a bottle, or a crate with a barrel (and a plank).
        int PlaceCargo(Vector2 pos, Vector2 dir, float r, float lead, uint h)
        {
            float lateral = (SeaMath.Rand(h, 5) - 0.5f) * Mathf.Min(r, 6f);
            float roll = SeaMath.Rand(h, 6);
            Vector2 right = new Vector2(dir.y, -dir.x);
            Vector2 c = EncounterPacer.AheadPoint(pos, dir, r, lead, lateral);
            int n = 0;
            if (roll < 0.3f)
                n += ships.SpawnRouteFlotsam(ShipSystem.FlotsamKind.Bottle, Clamp(c)) >= 0 ? 1 : 0;
            else if (roll < 0.62f)
                n += ships.SpawnRouteFlotsam(SeaMath.Rand(h, 7) < 0.5f ? ShipSystem.FlotsamKind.Crate : ShipSystem.FlotsamKind.Barrel, Clamp(c)) >= 0 ? 1 : 0;
            else
            {
                n += ships.SpawnRouteFlotsam(ShipSystem.FlotsamKind.Crate, Clamp(c)) >= 0 ? 1 : 0;
                n += ships.SpawnRouteFlotsam(ShipSystem.FlotsamKind.Barrel, Clamp(c + right * 1.6f + dir * 1.1f)) >= 0 ? 1 : 0;
                if (SeaMath.Rand(h, 8) < 0.6f)
                    n += ships.SpawnRouteFlotsam(ShipSystem.FlotsamKind.Driftwood, Clamp(c - right * 1.5f + dir * 2.2f)) >= 0 ? 1 : 0;
            }
            return n;
        }

        static Vector2 Clamp(Vector2 p) => Island.PositionConstraint != null ? Island.PositionConstraint(p) : p;

        // ---------------------------------------------------------------- the adventure track

        // Adventure: flotsam is the only thing that lifts the island again, so instead of the travel encounters it is
        // a steady stream on the track - a group every RingWorld.FlotsamSpacing units ahead of the player (the gaps
        // grow with the difficulty). The groups are laid out to make lines worth driving: through the narrow gap
        // beside an island, along a plate boundary you can surf, or loose in open water.
        int _trackFill = -1;
        bool _trackReady;
        float _trackNext, _trackDir = 1f;
        int _trackGroups;
        uint _trackRoll = 1u;
        float _trackRadius = 3f;
        readonly List<Island> _trackNear = new();
        readonly List<Vector2> _trackFree = new();

        public int TrackGroups => _trackGroups;

        void TickTrack()
        {
            var ring = RingWorld.Active;
            if (ring == null || !ring.IsApplied || ships == null || !GameModes.IsAdventure) { _trackReady = false; _animalReady = false; return; }
            var g = ring.Geometry;
            Vector2 pp = _player.PlanarPosition;
            float dir = Mathf.Abs(_player.Forward.z) > 0.3f ? Mathf.Sign(_player.Forward.z) : _trackDir;
            if (ring.FillCount != _trackFill || dir != _trackDir)
            {
                _trackFill = ring.FillCount;
                _trackDir = dir;
                _trackReady = false;
            }
            if (!_trackReady)
            {
                _trackReady = true;
                _trackNext = pp.y + _trackDir * 25f;
            }
            LayTrack(g, pp, _trackDir, Mathf.Max(6f, ring.FlotsamSpacing), trackLookahead, _player.BoundingRadius);
        }

        // Lays out every group that is missing between the player and `lookahead` units ahead; returns how many it
        // placed. The frontier is kept between calls (TickTrack), so a group is never laid twice.
        public int LayTrack(RingGeometry g, Vector2 pp, float dir, float spacing, float lookahead, float playerRadius)
        {
            _trackDir = dir >= 0f ? 1f : -1f;
            _trackRadius = Mathf.Max(0.5f, playerRadius);
            // A restart teleports the player, and a turn puts the frontier behind it: the stream starts anew ahead.
            float ahead = (_trackNext - pp.y) * _trackDir;
            if (!_trackReady || ahead < 8f || ahead > lookahead + 4f * spacing + 40f)
            {
                _trackReady = true;
                _trackNext = pp.y + _trackDir * 25f;
            }
            int groups = 0;
            for (int guard = 0; guard < 8 && (pp.y + _trackDir * lookahead - _trackNext) * _trackDir > 0f; guard++)
            {
                PlaceTrackGroup(g, _trackNext);
                _trackNext += _trackDir * Mathf.Max(4f, spacing) * Mathf.Lerp(0.75f, 1.25f, Roll());
                groups++;
            }
            LayAnimals(g, pp, lookahead);
            return groups;
        }

        // ---- sea animals to pick up (owner: "wale/fische/schildkröten die nach einsammeln neben einem schwimmen") ----

        float _animalNext;
        bool _animalReady;
        int _trackAnimals;

        public int TrackAnimals => _trackAnimals;
        // Animals swimming alongside the island right now (whale / turtle from SeaLifeSystem, shoal from FishSystem).
        public int CompanionCount => (seaLife != null ? seaLife.CompanionCount : 0) + (fish != null ? fish.EscortCount : 0);
        // Animals still lying on the track ahead, waiting to be run over.
        public int PickupsOnTrack => (seaLife != null ? seaLife.PickupCount : 0) + (fish != null ? fish.PickupSchoolCount : 0);
        // The top-speed factor the current escort is worth (1 = nobody swimming along).
        public float CompanionFactor => 1f + companionBoost * Mathf.Min(maxCompanionStack, CompanionCount);

        // The escort pushes for as long as it is there: the boost is topped up every tick and fades out by itself
        // half a second after the last animal has left.
        const float CompanionHold = 0.6f;

        void TickCompanions()
        {
            if (!GameModes.IsAdventure || _player == null) return;
            if (CompanionCount <= 0) return;
            _player.SpeedBoost(CompanionHold, CompanionFactor);
        }

        // One animal every animalSpacing units along the band, in the widest free water there.
        void LayAnimals(RingGeometry g, Vector2 pp, float lookahead)
        {
            if (animalSpacing <= 0f || (seaLife == null && fish == null)) return;
            float ahead = (_animalNext - pp.y) * _trackDir;
            if (!_animalReady || ahead < 4f || ahead > lookahead + 2f * animalSpacing + 60f)
            {
                _animalReady = true;
                _animalNext = pp.y + _trackDir * Mathf.Max(24f, animalSpacing * 0.4f);
            }
            for (int guard = 0; guard < 3 && (pp.y + _trackDir * lookahead - _animalNext) * _trackDir > 0f; guard++)
            {
                PlaceAnimal(g, _animalNext);
                _animalNext += _trackDir * animalSpacing * Mathf.Lerp(0.8f, 1.2f, Roll());
            }
        }

        void PlaceAnimal(RingGeometry g, float z)
        {
            float roll = Roll();
            bool whale = roll < animalWhaleShare && seaLife != null;
            bool turtle = !whale && roll < animalWhaleShare + animalTurtleShare && seaLife != null;
            // A whale is long: it needs its own clearance from the islands and from the rim, or the ring bend
            // would fold the half that sticks out onto the band's edge plane.
            float clear = whale ? 6f : 3f;
            FreeSpans(g, z, clear);
            if (_trackFree.Count == 0) return;
            Vector2 span = _trackFree[Mathf.Min(_trackFree.Count - 1, Mathf.FloorToInt(Roll() * _trackFree.Count))];
            if (span.y - span.x < 2f * clear) return;
            Vector2 p = g.ClampAcross(new Vector2(Mathf.Lerp(span.x + clear, span.y - clear, Roll()), z), clear);
            Vector2 dir = new Vector2(0f, _trackDir);
            int slot = whale ? seaLife.SpawnPickup(SeaLifeSystem.Kind.Whale, p, dir)
                : turtle ? seaLife.SpawnPickup(SeaLifeSystem.Kind.Turtle, p, dir)
                : fish != null ? fish.SpawnPickupShoal(p, dir) : -1;
            if (slot >= 0) _trackAnimals++;
        }

        float Roll()
        {
            _trackRoll = SeaMath.Hash(_trackRoll, (uint)(seed * 2654435761u) + 0x9E3779B9u);
            return (_trackRoll & 0xFFFFFFu) / 16777216f;
        }

        void PlaceTrackGroup(RingGeometry g, float z)
        {
            _trackGroups++;
            float roll = Roll();
            bool island = CollectNear(g, z);
            if (island && roll < trackGapLines) { PlaceGapLine(g, z); return; }
            if (roll < trackGapLines + trackSeamLines && PlaceSeamLine(g, z)) return;
            PlaceLoose(g, z);
        }

        // The islands whose body reaches the stretch around z.
        bool CollectNear(RingGeometry g, float z)
        {
            _trackNear.Clear();
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var isl = all[i];
                if (isl == null || !isl.isActiveAndEnabled || isl.useKeyboardInput) continue;
                if (Mathf.Abs(g.AlongDelta(isl.PlanarPosition.y, z)) > isl.BoundingRadius + 6f) continue;
                _trackNear.Add(isl);
            }
            return _trackNear.Count > 0;
        }

        // The free stretches across the band at z, as (from, to) pairs in _trackFree.
        void FreeSpans(RingGeometry g, float z, float clearance)
        {
            _trackFree.Clear();
            float lo = g.MinX + 2f, hi = g.MaxX - 2f;
            _trackFree.Add(new Vector2(lo, hi));
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var isl = all[i];
                if (isl == null || !isl.isActiveAndEnabled || isl.useKeyboardInput) continue;
                float r = isl.BoundingRadius + clearance;
                float dz = g.AlongDelta(isl.PlanarPosition.y, z);
                if (Mathf.Abs(dz) >= r) continue;
                float half = Mathf.Sqrt(r * r - dz * dz);
                float a = isl.PlanarPosition.x - half, b = isl.PlanarPosition.x + half;
                for (int k = _trackFree.Count - 1; k >= 0; k--)
                {
                    Vector2 s = _trackFree[k];
                    if (b <= s.x || a >= s.y) continue;
                    _trackFree.RemoveAt(k);
                    if (a - s.x > 1.5f) _trackFree.Insert(k, new Vector2(s.x, a));
                    if (s.y - b > 1.5f) _trackFree.Insert(k, new Vector2(b, s.y));
                }
            }
        }

        // The narrowest passage that is still wide enough for the island: the line that pays but asks for good steering.
        void PlaceGapLine(RingGeometry g, float z)
        {
            float need = Mathf.Max(3f, _trackRadius * 0.8f);
            FreeSpans(g, z, 1.5f);
            float bestWidth = float.MaxValue, x = g.centerX;
            bool found = false;
            for (int i = 0; i < _trackFree.Count; i++)
            {
                float w = _trackFree[i].y - _trackFree[i].x;
                if (w < need || w >= bestWidth) continue;
                bestWidth = w;
                x = (_trackFree[i].x + _trackFree[i].y) * 0.5f;
                found = true;
            }
            if (!found) { PlaceLoose(g, z); return; }
            for (int k = -1; k <= 1; k++)
            {
                float pz = z + k * trackLineStep * _trackDir;
                FreeSpans(g, pz, 1.5f);
                Drop(g, new Vector2(NearestFree(x), pz), k == 0 ? ShipSystem.FlotsamKind.Crate : KindFor(Roll()));
            }
        }

        float NearestFree(float x)
        {
            float best = x, bestD = float.MaxValue;
            for (int i = 0; i < _trackFree.Count; i++)
            {
                float c = Mathf.Clamp(x, _trackFree[i].x, _trackFree[i].y);
                float d = Mathf.Abs(c - x);
                if (d >= bestD) continue;
                bestD = d;
                best = c;
            }
            return best;
        }

        // A line of pieces along the nearest plate boundary: collected while surfing it.
        bool PlaceSeamLine(RingGeometry g, float z)
        {
            var plates = Drift.Tectonics.PlateSystem.Instance;
            if (plates == null) return false;
            float x = Mathf.Lerp(g.MinX + 4f, g.MaxX - 4f, Roll());
            if (!plates.NearestSeam(new Vector2(x, z), g.halfWidth * 0.8f, out var seam, out float t, out Vector2 q)) return false;
            Vector2 tan = plates.SeamTangent(seam, t);
            if (Mathf.Abs(tan.y) < 0.5f) return false;
            if (Mathf.Sign(tan.y) != _trackDir) tan = -tan;
            int placed = 0;
            for (int k = 0; k < 3; k++)
            {
                Vector2 p = q + tan * (k * trackLineStep);
                FreeSpans(g, p.y, 1.5f);
                p.x = NearestFree(p.x);
                if (Drop(g, p, KindFor(Roll()))) placed++;
            }
            return placed > 0;
        }

        void PlaceLoose(RingGeometry g, float z)
        {
            FreeSpans(g, z, 1.5f);
            if (_trackFree.Count == 0) return;
            Vector2 span = _trackFree[Mathf.Min(_trackFree.Count - 1, Mathf.FloorToInt(Roll() * _trackFree.Count))];
            float x = Mathf.Lerp(span.x, span.y, Roll());
            Drop(g, new Vector2(x, z), KindFor(Roll()));
            if (Roll() >= 0.45f) return;
            float pz = z + trackLineStep * _trackDir;
            FreeSpans(g, pz, 1.5f);
            Drop(g, new Vector2(NearestFree(x + (Roll() - 0.5f) * 6f), pz), KindFor(Roll()));
        }

        static ShipSystem.FlotsamKind KindFor(float roll)
        {
            if (roll < 0.3f) return ShipSystem.FlotsamKind.Crate;
            if (roll < 0.6f) return ShipSystem.FlotsamKind.Barrel;
            if (roll < 0.8f) return ShipSystem.FlotsamKind.Bottle;
            return ShipSystem.FlotsamKind.Driftwood;
        }

        bool Drop(RingGeometry g, Vector2 p, ShipSystem.FlotsamKind kind)
        {
            p = g.ClampAcross(p, 1.5f);
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var isl = all[i];
                if (isl == null || !isl.isActiveAndEnabled || isl.useKeyboardInput) continue;
                Vector2 d = new Vector2(p.x - isl.PlanarPosition.x, g.AlongDelta(p.y, isl.PlanarPosition.y));
                if (d.sqrMagnitude < (isl.BoundingRadius + 1f) * (isl.BoundingRadius + 1f)) return false;
            }
            return ships.SpawnRouteFlotsam(kind, p) >= 0;
        }
    }
}
