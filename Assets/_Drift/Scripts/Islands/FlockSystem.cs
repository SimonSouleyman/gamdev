using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using Drift.Tectonics;
using UnityEngine;

namespace Drift.Islands
{
    [ExecuteAlways]
    public class FlockSystem : MonoBehaviour
    {
        const string ObjName = "Flocks";

        public enum FlockState { Cruise, Orbit, Descend, Perched, TakeOff, Murmur }

        public Island player;
        public int flockCount = 6;
        public int minBirds = 4;
        public int maxBirds = 9;
        public float cruiseSpeed = 7f;
        public float altitude = 5f;
        public float spawnMin = 90f;
        public float spawnMax = 170f;
        public float leashDistance = 240f;
        public float landingMinArea = 60f;
        public float landChance = 0.6f;
        public float perchMin = 5f;
        public float perchMax = 10f;
        public float descentRate = 2.5f;
        public float climbRate = 3f;
        public float stormAvoidance = 0.3f;
        public int seed = 99;
        // The one bird mesh is rebuilt at meshInterval while a flock is within nearDistance of the camera
        // focus (LifeLod), at farMeshInterval otherwise; the flap itself is a shader effect.
        public float nearDistance = 60f;
        public float meshInterval = 1f / 15f;
        public float farMeshInterval = 0.2f;
        // Phase 4 seabirds: seabirdFlocks small flocks (LifeKind.Seabird, white/grey, gliding without flap)
        // that prefer cliff islands (Barren, Volcanic or a peak above cliffHeight), circle them tightly
        // (BoundingRadius + seabirdOrbitMargin, seabirdOrbitMin..Max s) at seabirdAltitude above the peak and
        // never land. Without a cliff island in range they circle any island like the other flocks.
        public int seabirdFlocks = 2;
        public int seabirdMinBirds = 2;
        public int seabirdMaxBirds = 3;
        public float seabirdSpeed = 5f;
        public float seabirdAltitude = 2.5f;
        public float seabirdOrbitMargin = 1.5f;
        public float seabirdOrbitMin = 20f;
        public float seabirdOrbitMax = 40f;
        public float cliffHeight = 1.7f;

        [Header("Besondere Bewegungen der Vögel")]
        [Tooltip("Stoßtauchen: so oft pro Minute (im Mittel) stürzt sich ein Seevogel eines kreisenden Schwarms steil ins Meer neben der Insel (Spritzring) und steigt wieder zu den anderen auf. Nur über Wasser, nicht im Sturm.")]
        [Range(0f, 10f)] public float seabirdDivesPerMinute = 2f;
        [Range(1.5f, 6f)] public float diveTime = 2.8f;
        [Tooltip("Schwarmtanz: Chance, dass ein Singvogelschwarm nach dem Kreisen über der Insel stehen bleibt und als wogende Achterwolke durcheinanderwirbelt, bevor er landet oder weiterzieht (nur tagsüber, ohne Sturm).")]
        [Range(0f, 1f)] public float murmurChance = 0.35f;
        [Range(4f, 30f)] public float murmurTime = 12f;
        [Range(1f, 6f)] public float murmurSize = 3f;
        [Tooltip("Seevögel kreisen im Uhrzeigersinn einzeln hintereinander auf einer Kette um die Klippe (die Singvögel als dichter Pulk gegen den Uhrzeigersinn): Abstand zweier Seevögel auf dem Kreis in Grad.")]
        [Range(5f, 60f)] public float seabirdChainSpacing = 26f;

        // A struct: a flock re-spawns every time it falls behind a fast island, and a class allocated one object
        // per bird per re-spawn (the largest steady source of garbage while driving).
        struct Bird
        {
            public Vector2 offset;
            public float phase, scale;
            public int variant;
            // Plunge-dive: seconds since it started (0 = not diving) and the spot in the target island's frame.
            public float dive;
            public Vector2 diveLocal;
        }

        class Flock
        {
            public Vector2 pos, vel;
            public float height, yaw, roll, orbitAngle, orbitTime, perchTime;
            public FlockState state;
            public Island target;
            public Vector2 perchLocal;
            public bool seabird;
            // orbitR: last orbit radius; form: 0..1 blend from the loose pulk into the seabird chain or the murmur
            // figure; murmurT: time in the murmur; diveWait: "per-minute" units to the next dive; murmured: this
            // visit had its murmur already.
            public float orbitR, form, murmurT, diveWait = -1f;
            public bool murmured;
            public readonly List<Bird> birds = new();
        }

        // Island.MaxHeight walks the whole heightfield, so the cliff test is cached per island and Version.
        struct CliffInfo
        {
            public Island island;
            public int version;
            public float peak;
        }

        readonly List<Flock> _flocks = new();
        readonly List<CliffInfo> _cliffs = new();
        readonly TemplateBatch _batch = new();
        System.Random _rnd;
        GameObject _go;
        Mesh _mesh;
        float _clock, _meshTimer;
        bool _init;

        public int FlockCount => _flocks.Count;
        public int MeshBuilds { get; private set; }

        public int SeabirdFlockCount
        {
            get
            {
                int n = 0;
                foreach (var f in _flocks) if (f.seabird) n++;
                return n;
            }
        }

        public int SeabirdCount
        {
            get
            {
                int n = 0;
                foreach (var f in _flocks) if (f.seabird) n += f.birds.Count;
                return n;
            }
        }

        public bool IsSeabird(int index) => _flocks[index].seabird;
        public int BirdCountOf(int index) => _flocks[index].birds.Count;

        public int BirdCount
        {
            get
            {
                int n = 0;
                foreach (var f in _flocks) n += f.birds.Count;
                return n;
            }
        }

        public int PerchedFlocks
        {
            get
            {
                int n = 0;
                foreach (var f in _flocks) if (f.state == FlockState.Perched) n++;
                return n;
            }
        }

        public FlockState StateOf(int index) => _flocks[index].state;
        public int Dives { get; private set; }
        public int Murmurs { get; private set; }
        public bool IsDiving(int index, int bird) => _flocks[index].birds[bird].dive > 0f;
        public float OrbitDirection(int index) => _flocks[index].seabird ? -1f : 1f;
        public float FormationOf(int index) => _flocks[index].form;

        public int DivingBirds
        {
            get
            {
                int n = 0;
                foreach (var f in _flocks) foreach (var b in f.birds) if (b.dive > 0f) n++;
                return n;
            }
        }

        // World position of one bird as it is drawn (formation, dive and perch included), y = height.
        public Vector3 BirdPosition(int index, int bird)
        {
            var f = _flocks[index];
            BirdPose(f, f.birds[bird], bird, out var pos, out _, out _, out _, out _);
            return pos;
        }
        public float HeightOf(int index) => _flocks[index].height;
        public Vector2 PositionOf(int index) => _flocks[index].pos;
        public Island TargetOf(int index) => _flocks[index].target;

        void OnEnable() => Reseed(seed);

        public void Reseed(int newSeed)
        {
            seed = newSeed;
            _flocks.Clear();
            _cliffs.Clear();
            _init = false;
            _rnd = new System.Random(seed);
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
            _go = null;
            DestroyMesh();
        }

        void OnDestroy() => DestroyMesh();

        void DestroyMesh()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
            _mesh = null;
        }

        void Update()
        {
            if (player == null) player = FindPlayer();
            if (player == null) return;
            if (!_init) Init();
            if (!Application.isPlaying) return;
            Step(Time.deltaTime);
        }

        static Island FindPlayer()
        {
            foreach (var i in Island.All) if (i != null && i.useKeyboardInput) return i;
            return null;
        }

        float Rand() => (float)_rnd.NextDouble();
        float Rand(float a, float b) => a + Rand() * (b - a);

        void Init()
        {
            _init = true;
            for (int i = 0; i < flockCount + seabirdFlocks; i++)
            {
                var f = new Flock { seabird = i >= flockCount };
                Respawn(f);
                _flocks.Add(f);
            }
            RebuildMesh();
        }

        void Respawn(Flock f)
        {
            float ang = Rand(0f, Mathf.PI * 2f);
            Vector2 p = player.PlanarPosition + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Rand(spawnMin, spawnMax);
            f.pos = p;
            f.vel = Vector2.zero;
            f.height = altitude * Rand(0.8f, 1.3f);
            f.target = null;
            f.state = FlockState.Cruise;
            f.form = 0f;
            f.murmured = false;
            f.birds.Clear();
            int n = f.seabird ? _rnd.Next(seabirdMinBirds, seabirdMaxBirds + 1) : _rnd.Next(minBirds, maxBirds + 1);
            for (int i = 0; i < n; i++)
            {
                float a = Rand(0f, Mathf.PI * 2f);
                f.birds.Add(new Bird
                {
                    offset = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Rand(0.4f, f.seabird ? 1.6f : 2.2f),
                    phase = Rand(0f, 6.28f),
                    scale = f.seabird ? Rand(1.1f, 1.4f) : Rand(0.9f, 1.25f),
                    variant = _rnd.Next(LifeMeshes.Variants)
                });
            }
        }

        float Peak(Island i)
        {
            for (int k = 0; k < _cliffs.Count; k++)
            {
                var c = _cliffs[k];
                if (c.island != i) continue;
                if (c.version == i.Version) return c.peak;
                c.version = i.Version;
                c.peak = i.MaxHeight;
                _cliffs[k] = c;
                return c.peak;
            }
            for (int k = _cliffs.Count - 1; k >= 0; k--) if (_cliffs[k].island == null) _cliffs.RemoveAt(k);
            float peak = i.MaxHeight;
            _cliffs.Add(new CliffInfo { island = i, version = i.Version, peak = peak });
            return peak;
        }

        public bool IsCliff(Island i) =>
            i != null && (i.kind == IslandKind.Barren || i.kind == IslandKind.Volcanic || Peak(i) > cliffHeight);

        bool IsCandidate(Island i, Flock f) =>
            i != null && i.isActiveAndEnabled && i != f.target && (i.PlanarPosition - f.pos).sqrMagnitude <= 300f * 300f;

        Island PickTarget(Flock f)
        {
            var all = Island.All;
            if (f.seabird)
            {
                // The current target stays a candidate, so a lone cliff island keeps its seabirds.
                int cliffs = 0;
                for (int k = 0; k < all.Count; k++) if ((IsCandidate(all[k], f) || all[k] == f.target) && IsCliff(all[k])) cliffs++;
                if (cliffs > 0)
                {
                    int pickCliff = _rnd.Next(cliffs);
                    for (int k = 0; k < all.Count; k++)
                        if ((IsCandidate(all[k], f) || all[k] == f.target) && IsCliff(all[k]) && pickCliff-- == 0) return all[k];
                }
            }
            int count = 0;
            for (int k = 0; k < all.Count; k++) if (IsCandidate(all[k], f)) count++;
            if (count == 0) return null;
            int pick = _rnd.Next(count);
            for (int k = 0; k < all.Count; k++)
                if (IsCandidate(all[k], f) && pick-- == 0) return all[k];
            return null;
        }

        bool TryPerchSpot(Island island, out Vector2 local)
        {
            Rect b = island.LocalBounds;
            for (int i = 0; i < 16; i++)
            {
                local = new Vector2(b.xMin + Rand() * b.width, b.yMin + Rand() * b.height);
                float h = island.SampleHeight(local);
                if (h > 0.15f && h < 2.6f) return true;
            }
            local = default;
            return false;
        }

        // Ground height in world space under a perch spot (island y plus terrain height there).
        static float GroundY(Island island, Vector2 local) => island.transform.position.y + island.SampleHeight(local);

        public bool TryLand(int index)
        {
            var f = _flocks[index];
            if (f.target == null || !f.target.isActiveAndEnabled) return false;
            if (!TryPerchSpot(f.target, out var local)) return false;
            f.perchLocal = local;
            f.state = FlockState.Descend;
            return true;
        }

        void Leave(Flock f)
        {
            f.target = PickTarget(f);
            f.state = FlockState.Cruise;
            f.murmured = false;
        }

        static bool Daylight => LifeEnvironment.NightAmount < 0.5f;

        // End of an orbit (or of the murmur that followed it): songbirds may dance once, then land or move on.
        void EndOrbit(Flock f)
        {
            if (!f.seabird && !f.murmured && Daylight && Rand() < murmurChance)
            {
                StartMurmur(f);
                return;
            }
            Vector2 local = default;
            bool land = !f.seabird && f.target.LandArea > landingMinArea && !f.target.IsEmerging && Rand() < landChance
                        && TryPerchSpot(f.target, out local);
            if (land)
            {
                f.perchLocal = local;
                f.state = FlockState.Descend;
            }
            else Leave(f);
        }

        void StartMurmur(Flock f)
        {
            f.state = FlockState.Murmur;
            f.murmured = true;
            f.murmurT = 0f;
            f.orbitTime = murmurTime * Rand(0.8f, 1.2f);
            Murmurs++;
        }

        // Starts the flock's special move now if it can (tests, screenshots): a murmur for songbirds circling or
        // cruising to an island, a dive for a seabird flock over water.
        public bool TryStartSpecialMove(int index)
        {
            var f = _flocks[index];
            if (f.target == null || !f.target.isActiveAndEnabled) return false;
            if (!f.seabird)
            {
                if (f.state != FlockState.Orbit && f.state != FlockState.Cruise) return false;
                StartMurmur(f);
                return true;
            }
            return TryStartDive(f);
        }

        bool TryStartDive(Flock f)
        {
            if (f.target == null || f.birds.Count == 0) return false;
            int start = _rnd.Next(f.birds.Count);
            for (int k = 0; k < f.birds.Count; k++)
            {
                int i = (start + k) % f.birds.Count;
                var b = f.birds[i];
                if (b.dive > 0f) continue;
                BirdPose(f, b, i, out var pos, out _, out _, out _, out _);
                Vector2 local = f.target.ToLocal(new Vector2(pos.x, pos.z));
                if (f.target.SampleHeight(local) > -0.05f) continue;
                b.dive = 1e-4f;
                b.diveLocal = local;
                f.birds[i] = b;
                Dives++;
                return true;
            }
            return false;
        }

        public void SetTarget(int index, Island island)
        {
            var f = _flocks[index];
            f.target = island;
            f.state = FlockState.Cruise;
        }

        public void Step(float dt)
        {
            if (player == null) player = FindPlayer();
            if (player == null) return;
            if (!_init) Init();
            _clock += dt;
            var storms = StormSystem.Instance;

            foreach (var f in _flocks)
            {
                if ((f.pos - player.PlanarPosition).magnitude > leashDistance) Respawn(f);

                if (f.target == null || !f.target.isActiveAndEnabled)
                {
                    f.target = PickTarget(f);
                    f.state = FlockState.Cruise;
                }

                float storm = storms != null ? storms.IntensityAt(f.pos) : 0f;
                StormData stormData = default;
                bool avoiding = storm > stormAvoidance && storms.NearestStorm(f.pos, out stormData);
                if (avoiding && (f.state == FlockState.Perched || f.state == FlockState.Descend)) f.state = FlockState.TakeOff;
                if (avoiding && f.state == FlockState.Murmur) f.state = FlockState.Cruise;

                Vector2 desired;
                float targetHeight = altitude;
                bool grounded = false;
                float speedOf = f.seabird ? seabirdSpeed : cruiseSpeed;

                if (avoiding)
                {
                    Vector2 away = f.pos - stormData.center;
                    away = away.sqrMagnitude > 1e-3f ? away.normalized : Vector2.right;
                    desired = f.pos + away * 30f;
                }
                else if (f.target != null)
                {
                    Vector2 tp = f.target.PlanarPosition;
                    float orbitR = f.target.BoundingRadius + (f.seabird ? seabirdOrbitMargin : 4f);
                    f.orbitR = orbitR;
                    if (f.seabird) targetHeight = Mathf.Max(altitude * 0.6f, f.target.transform.position.y + Peak(f.target) + seabirdAltitude);
                    switch (f.state)
                    {
                        case FlockState.Cruise:
                            desired = tp;
                            if ((tp - f.pos).magnitude < orbitR + 3f)
                            {
                                f.state = FlockState.Orbit;
                                f.orbitTime = f.seabird ? Rand(seabirdOrbitMin, seabirdOrbitMax) : Rand(7f, 13f);
                                Vector2 rel = f.pos - tp;
                                f.orbitAngle = Mathf.Atan2(rel.y, rel.x);
                            }
                            break;
                        case FlockState.Orbit:
                            // Seabirds circle clockwise, songbirds counter-clockwise.
                            f.orbitAngle += (f.seabird ? -1f : 1f) * speedOf * 0.8f / orbitR * dt;
                            desired = tp + new Vector2(Mathf.Cos(f.orbitAngle), Mathf.Sin(f.orbitAngle)) * orbitR;
                            f.orbitTime -= dt;
                            if (f.orbitTime <= 0f) EndOrbit(f);
                            break;
                        case FlockState.Murmur:
                            // Hovering over the island's middle on a slow small circle while the birds swirl.
                            f.murmurT += dt;
                            f.orbitTime -= dt;
                            desired = tp + new Vector2(Mathf.Cos(f.murmurT * 0.3f), Mathf.Sin(f.murmurT * 0.3f)) * 1.5f;
                            targetHeight = altitude * 1.15f;
                            speedOf *= 0.45f;
                            if (f.orbitTime <= 0f) EndOrbit(f);
                            break;
                        case FlockState.Descend:
                        {
                            Vector2 spot = f.target.ToWorld(f.perchLocal);
                            desired = spot;
                            float ground = GroundY(f.target, f.perchLocal);
                            float dist = (spot - f.pos).magnitude;
                            targetHeight = ground + Mathf.Clamp01((dist - 1.5f) / 12f) * altitude;
                            if (dist < 1.2f && f.height < ground + 0.25f)
                            {
                                f.state = FlockState.Perched;
                                f.perchTime = Rand(perchMin, perchMax);
                                f.pos = spot;
                                f.vel = Vector2.zero;
                            }
                            break;
                        }
                        case FlockState.Perched:
                        {
                            Vector2 spot = f.target.ToWorld(f.perchLocal);
                            desired = spot;
                            targetHeight = GroundY(f.target, f.perchLocal);
                            grounded = true;
                            f.perchTime -= dt;
                            if (f.perchTime <= 0f || f.target.SampleHeight(f.perchLocal) < 0.1f) f.state = FlockState.TakeOff;
                            break;
                        }
                        default:
                            desired = f.pos + (f.vel.sqrMagnitude > 0.01f ? f.vel.normalized : new Vector2(Mathf.Sin(f.yaw * Mathf.Deg2Rad), Mathf.Cos(f.yaw * Mathf.Deg2Rad))) * 20f;
                            if (f.height >= altitude * 0.9f) Leave(f);
                            break;
                    }
                }
                else
                {
                    desired = f.pos + (f.vel.sqrMagnitude > 0.01f ? f.vel.normalized : Vector2.right) * 20f;
                }

                if (grounded)
                {
                    f.pos = desired;
                    f.vel = Vector2.zero;
                    f.height = targetHeight;
                }
                else
                {
                    Vector2 dir = desired - f.pos;
                    float speed = f.state == FlockState.Descend ? Mathf.Lerp(speedOf * 0.35f, speedOf, Mathf.Clamp01((dir.magnitude - 1f) / 10f)) : speedOf;
                    Vector2 desiredVel = dir.sqrMagnitude > 0.01f ? dir.normalized * speed : Vector2.zero;
                    f.vel = Vector2.Lerp(f.vel, desiredVel, 1f - Mathf.Exp(-(f.state == FlockState.Descend ? 3f : 1.6f) * dt));
                    f.pos += f.vel * dt;
                    float rate = f.height > targetHeight ? descentRate : climbRate;
                    f.height = Mathf.MoveTowards(f.height, targetHeight, rate * dt);
                }

                if (f.vel.sqrMagnitude > 0.01f)
                {
                    float newYaw = Mathf.Atan2(f.vel.x, f.vel.y) * Mathf.Rad2Deg;
                    float turn = Mathf.DeltaAngle(f.yaw, newYaw);
                    f.roll = Mathf.Lerp(f.roll, Mathf.Clamp(-turn * 1.5f, -30f, 30f), 1f - Mathf.Exp(-3f * dt));
                    f.yaw = Mathf.LerpAngle(f.yaw, newYaw, 1f - Mathf.Exp(-4f * dt));
                }
                else f.roll = Mathf.Lerp(f.roll, 0f, 1f - Mathf.Exp(-3f * dt));

                bool formed = f.target != null && (f.seabird ? f.state == FlockState.Orbit : f.state == FlockState.Murmur && f.orbitTime > 1.5f);
                f.form = Mathf.MoveTowards(f.form, formed ? 1f : 0f, dt / 1.5f);
                if (f.seabird) StepDives(f, dt, !avoiding && f.state == FlockState.Orbit);
            }

            float nearest = float.MaxValue;
            foreach (var f in _flocks) nearest = Mathf.Min(nearest, LifeLod.Distance(new Vector3(f.pos.x, 0f, f.pos.y)));
            _meshTimer += dt;
            if (_meshTimer >= (nearest < nearDistance ? meshInterval : farMeshInterval))
            {
                _meshTimer = 0f;
                RebuildMesh();
            }
        }

        void StepDives(Flock f, float dt, bool may)
        {
            for (int i = 0; i < f.birds.Count; i++)
            {
                var b = f.birds[i];
                if (b.dive <= 0f) continue;
                b.dive += dt;
                if (b.dive >= diveTime || f.target == null) b.dive = 0f;
                f.birds[i] = b;
            }
            if (!may || seabirdDivesPerMinute <= 0f) return;
            if (f.diveWait < 0f) f.diveWait = -Mathf.Log(Mathf.Max(1e-4f, Rand())) * 60f;
            f.diveWait -= dt * seabirdDivesPerMinute;
            if (f.diveWait > 0f) return;
            f.diveWait = -1f;
            TryStartDive(f);
        }

        // Where one bird is drawn. Cruising and orbiting songbirds fly as a loose pulk round the flock position;
        // an orbiting seabird flock strings out one behind the other on its circle; a murmuring flock swirls on a
        // breathing, turning figure eight with a height wave rolling along it; a diving seabird plunges to the
        // water, is under for a moment and climbs back to its place.
        void BirdPose(Flock f, in Bird b, int index, out Vector3 pos, out float yaw, out float roll, out float pitch, out bool hidden)
        {
            float yawRad = f.yaw * Mathf.Deg2Rad;
            float cs = Mathf.Cos(yawRad), sn = Mathf.Sin(yawRad);
            bool perched = f.state == FlockState.Perched && f.target != null;
            Vector2 o = b.offset * (perched ? 0.6f : 1f);
            Vector2 world = f.pos + new Vector2(o.x * cs + o.y * sn, -o.x * sn + o.y * cs);
            float y = f.height;
            yaw = f.yaw;
            roll = f.roll;
            pitch = 0f;
            hidden = false;
            if (perched)
            {
                y = GroundY(f.target, f.target.ToLocal(world));
                yaw += b.phase * 20f;
                roll = 0f;
                pos = new Vector3(world.x, y + 0.02f, world.y);
                return;
            }
            if (f.seabird)
            {
                world += new Vector2(Mathf.Sin(_clock * 0.5f + b.phase), Mathf.Cos(_clock * 0.4f + b.phase)) * 0.3f;
                y += Mathf.Sin(_clock * 0.6f + b.phase) * 0.5f;
                yaw += Mathf.Sin(_clock * 0.7f + b.phase) * 5f;
                roll += Mathf.Sin(_clock * 0.8f + b.phase) * 6f;
                if (f.form > 0f && f.target != null)
                {
                    float a = f.orbitAngle + index * seabirdChainSpacing * Mathf.Deg2Rad;
                    float r = f.orbitR * (1f + 0.1f * Mathf.Sin(_clock * 0.45f + b.phase));
                    Vector2 ring = f.target.PlanarPosition + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    float ringYaw = Mathf.Atan2(Mathf.Sin(a), -Mathf.Cos(a)) * Mathf.Rad2Deg;
                    float k = Mathf.SmoothStep(0f, 1f, f.form);
                    world = Vector2.Lerp(world, ring, k);
                    yaw = Mathf.LerpAngle(yaw, ringYaw, k);
                    roll = Mathf.Lerp(roll, 14f, k);
                }
                if (b.dive > 0f && f.target != null)
                {
                    float u = b.dive / Mathf.Max(0.1f, diveTime);
                    Vector2 spot = f.target.ToWorld(b.diveLocal);
                    float water = f.target.transform.position.y;
                    if (u < 0.35f)
                    {
                        float s = u / 0.35f;
                        s *= s;
                        world = Vector2.Lerp(world, spot, s);
                        y = Mathf.Lerp(y, water, s);
                        pitch = 70f * Mathf.Min(1f, u / 0.08f);
                        roll = 0f;
                    }
                    else if (u < 0.55f)
                    {
                        world = spot;
                        y = water - 0.3f;
                        hidden = true;
                    }
                    else
                    {
                        float s = (u - 0.55f) / 0.45f;
                        s = 1f - (1f - s) * (1f - s);
                        world = Vector2.Lerp(spot, world, s);
                        y = Mathf.Lerp(water, y, s);
                        pitch = -35f * (1f - s);
                    }
                }
                pos = new Vector3(world.x, y, world.y);
                return;
            }
            world += new Vector2(Mathf.Sin(_clock * 0.9f + b.phase), Mathf.Cos(_clock * 0.7f + b.phase)) * 0.35f;
            y += Mathf.Sin(_clock * 1.3f + b.phase) * 0.35f;
            yaw += Mathf.Sin(_clock + b.phase) * 8f;
            if (f.form > 0f)
            {
                float u = b.phase + _clock * 1.7f;
                float turn = _clock * 0.35f;
                float A = murmurSize * (1f + 0.35f * Mathf.Sin(_clock * 0.8f));
                float B = murmurSize * 0.55f * (1f + 0.4f * Mathf.Cos(_clock * 0.63f));
                float lx = A * Mathf.Cos(u), lz = B * Mathf.Sin(2f * u);
                float dx = -A * Mathf.Sin(u), dz = 2f * B * Mathf.Cos(2f * u);
                float ct = Mathf.Cos(turn), st = Mathf.Sin(turn);
                Vector2 fig = f.pos + new Vector2(lx * ct - lz * st, lx * st + lz * ct);
                Vector2 dir = new Vector2(dx * ct - dz * st, dx * st + dz * ct);
                float k = Mathf.SmoothStep(0f, 1f, f.form);
                world = Vector2.Lerp(world, fig, k);
                y = Mathf.Lerp(y, f.height + 0.9f * Mathf.Sin(2f * u + _clock * 1.3f), k);
                yaw = Mathf.LerpAngle(yaw, Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg, k);
                roll = Mathf.Lerp(roll, 25f * Mathf.Sin(u), k);
            }
            pos = new Vector3(world.x, y, world.y);
        }

        static PlantTemplate _splash;

        // A flat white ring (8 segments) with a small inner disc: the splash where a seabird went in.
        static PlantTemplate Splash
        {
            get
            {
                if (_splash != null) return _splash;
                const int seg = 8;
                var v = new Vector3[1 + seg * 2];
                var n = new Vector3[v.Length];
                var c = new Color[v.Length];
                var t = new int[seg * 9];
                Color foam = new Color(0.95f, 0.97f, 1f);
                v[0] = new Vector3(0f, 0.02f, 0f);
                for (int i = 0; i < seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    v[1 + i] = new Vector3(Mathf.Cos(a) * 0.6f, 0.01f, Mathf.Sin(a) * 0.6f);
                    v[1 + seg + i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                }
                for (int i = 0; i < v.Length; i++) { n[i] = Vector3.up; c[i] = i == 0 ? foam * 0.9f : foam; }
                int k = 0;
                for (int i = 0; i < seg; i++)
                {
                    int i0 = 1 + i, i1 = 1 + (i + 1) % seg, o0 = 1 + seg + i, o1 = 1 + seg + (i + 1) % seg;
                    t[k++] = 0; t[k++] = i1; t[k++] = i0;
                    t[k++] = i0; t[k++] = i1; t[k++] = o1;
                    t[k++] = i0; t[k++] = o1; t[k++] = o0;
                }
                _splash = new PlantTemplate { vertices = v, normals = n, colors = c, triangles = t };
                return _splash;
            }
        }

        void EnsureObject()
        {
            if (_go != null) return;
            _go = new GameObject(ObjName);
            _go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            _go.transform.SetParent(transform, false);
            _go.AddComponent<MeshFilter>();
            var mr = _go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sharedMaterial = LifeMeshes.FlockMaterial;
            _mesh = new Mesh { name = "Flocks", hideFlags = HideFlags.DontSave };
            _go.GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        void RebuildMesh()
        {
            EnsureObject();
            MeshBuilds++;
            _batch.Begin();
            foreach (var f in _flocks)
            {
                bool perched = f.state == FlockState.Perched && f.target != null;
                for (int i = 0; i < f.birds.Count; i++)
                {
                    var b = f.birds[i];
                    BirdPose(f, b, i, out var pos, out float yaw, out float roll, out float pitch, out bool hidden);
                    if (f.seabird && b.dive > 0f && f.target != null)
                    {
                        float u = b.dive / Mathf.Max(0.1f, diveTime);
                        if (u > 0.3f && u < 0.85f)
                        {
                            Vector2 spot = f.target.ToWorld(b.diveLocal);
                            float r = 0.25f + 0.55f * Mathf.Sin((u - 0.3f) / 0.55f * Mathf.PI);
                            _batch.Add(Splash, new Vector3(spot.x, f.target.transform.position.y + 0.03f, spot.y), b.phase * 40f, r);
                        }
                    }
                    if (hidden) continue;
                    // Sitting birds and gliders get no wing alpha, so the shader does not flap them.
                    var kind = f.seabird ? LifeKind.Seabird : LifeKind.Bird;
                    _batch.Add(Markings.BirdTemplate(kind, b.variant), pos, yaw, b.scale, roll, pitch, !perched && !f.seabird);
                }
            }
            _batch.Apply(_mesh);
        }
    }
}
