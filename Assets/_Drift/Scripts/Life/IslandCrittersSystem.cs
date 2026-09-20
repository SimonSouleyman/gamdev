using System;
using System.Collections.Generic;
using Drift.Core;
using UnityEngine;

namespace Drift.Life
{
    [Serializable]
    public class CritterSaveData
    {
        // Residents only (crabs, turtles); butterflies and fireflies are transient. Per critter: kind
        // (LifeKind), variant, state (CritterState) and Stride floats: x, z, yaw, scale, timer, targetX, targetZ.
        public const int Stride = 7;
        public int[] kind, variant, state;
        public float[] m;
    }

    public enum CritterState { Idle, Move, Dig, Hidden, Rest }

    // Phase 4 small life per island in one mesh (Drift/Critter): beach crabs and turtles are residents seeded
    // from the island seed and saved; butterflies (by day, over flowers) are transient and only exist while the
    // island is in the near tier. Everything is capped per island and the mesh (<= ~900 verts) is rebuilt at
    // meshInterval only when something moved or faded.
    // Fireflies live on EVERY island at night, in a second child "FireflyGlow" drawn with the additive glow
    // variant of Drift/Critter (GlowMaterial, one draw call): per island a seeded set of homes in a few clusters
    // (BuildHomes, per shape Version). Near tier: one wandering firefly per home (tappable, in the critter list),
    // its halo re-baked at meshInterval. Mid / far tier: the homes are baked ONCE as halos that drift, bob and
    // blink in the vertex shader - no CPU per frame. Under every cluster lies a soft ground-hugging glow blob.
    // How many show is one float per renderer (_FireflyAmount: dusk fireflyDusk..nightThreshold, thinned by the
    // island's storm), so the swarm comes and goes one by one on the GPU too.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class IslandCrittersSystem : MonoBehaviour
    {
        const string ObjName = "Critters";

        public int seed = 1;
        // Tiers (LifeLod): near < detailDistance steps every frame; mid < simDistance steps the residents every
        // midStepInterval with the accumulated time and freezes the transients; far steps nothing and holds no
        // transients at all.
        public float simDistance = 100f;
        public float hideDistance = 260f;
        public float detailDistance = 45f;
        public float meshInterval = 1f / 12f;
        public float midStepInterval = 0.2f;

        // Crabs live on the shore band and scuttle sideways along it; one per crabShoreSpacing units of the
        // island's estimated shoreline (a disc of the land area), never more than maxCrabs. Above diveSpeed
        // (the island's own drive) they dive into their hole after a 0-0.6 s reaction and come back out
        // hiddenMin..hiddenMax s after the island slowed down again.
        public float shoreMin = 0.02f;
        public float shoreMax = 0.25f;
        public int maxCrabs = 12;
        public float crabShoreSpacing = 4f;
        public float crabSpeed = 0.35f;
        public float crabRange = 1.5f;
        public float crabScale = 0.06f;
        public float diveSpeed = 1.5f;
        public float hiddenMin = 3f;
        public float hiddenMax = 6f;

        // Turtles only on islands above turtleMinArea (1 + one more per 40 area, at most maxTurtles): they crawl
        // from the shore band up to turtleInland and back by day and rest turtleRestMin..turtleRestMax s.
        public int maxTurtles = 3;
        public float turtleMinArea = 20f;
        public float turtleSpeed = 0.08f;
        public float turtleInland = 0.55f;
        public float turtleRestMin = 20f;
        public float turtleRestMax = 60f;
        public float turtleScale = 0.09f;

        // Butterflies exist while NightAmount < dayThreshold, one per butterflyFlowers living flowers, and fly
        // flower to flower within butterflyRange. Fireflies: one per areaPerFirefly of land, at least minFireflies
        // (fewer on a rock of a few square units), at most maxFireflies; their homes prefer meadow-to-wood cells
        // (stage fireflyStageMin..Max), then any grown ground, then any dry ground.
        public int maxButterflies = 16;
        public int butterflyFlowers = 3;
        public float butterflySpeed = 0.5f;
        public float butterflyRange = 3f;
        public float butterflyHeight = 0.12f;
        public float butterflyScale = 0.07f;
        public float dayThreshold = 0.5f;
        public int maxFireflies = 48;
        public int minFireflies = 12;
        public float areaPerFirefly = 2.5f;
        public float fireflySpeed = 0.25f;
        public float fireflyRange = 1.5f;
        public float fireflyHeight = 0.35f;
        // Most fireflies hover low, some rise to the treetops (height + spread * f^2, f fixed per home): under a
        // closed canopy the swarm would otherwise be invisible from the chase camera.
        public float fireflyHeightSpread = 1.1f;
        // Turtles rest above nightThreshold; the firefly swarm grows from fireflyDusk to its full size there.
        public float nightThreshold = 0.6f;
        public float fireflyDusk = 0.35f;
        // Share of the swarm a full storm over the island sends into hiding.
        public float fireflyStormCut = 0.7f;
        public int farFireflies = 8;
        // Halo radius up close (world units) and the smallest radius on screen (tan of the half angle): from the
        // chase camera a firefly would be far below a pixel, the minimum keeps it a small soft dot.
        public float fireflyHalo = 0.11f;
        public float fireflyMinAngle = 0.0065f;
        public float fireflyDrift = 0.45f;
        public float fireflyClusterRadius = 1.7f;
        public Color fireflyColor = new Color(0.85f, 1.3f, 0.4f, 1f);
        // Ground glow: one blob per cluster (one cluster per areaPerGlowBlob, at most maxGlowBlobs), 25 verts
        // sampled onto the terrain and lifted by glowBlobLift.
        public int maxGlowBlobs = 8;
        public float areaPerGlowBlob = 14f;
        public float glowBlobRadius = 1.9f;
        public float glowBlobLift = 0.05f;
        public Color groundGlowColor = new Color(0.14f, 0.18f, 0.06f, 1f);
        public float fireflyStageMin = 0.3f;
        public float fireflyStageMax = 0.95f;
        public float fadeTime = 1.5f;
        public float spawnInterval = 1.5f;
        public int spawnsPerInterval = 2;
        [SerializeField] Material critterMaterial;
        [SerializeField] Material glowMaterial;

        class Critter
        {
            public LifeKind kind;
            public int variant;
            public Vector2 pos, target;
            public float yaw, scale, phase, timer, fade = 1f, react, alarm;
            public CritterState state;
            public bool dying;
            public int slot = -1;
        }

        readonly List<Critter> _critters = new();
        IIslandSurface _surface;
        IslandLifeSystem _life;
        System.Random _rnd;
        int _version = -1;
        bool _populated;
        float _peakArea;
        float _meshTimer, _stepTimer, _spawnTimer, _clock, _night;
        bool _meshDirty;

        GameObject _go;
        MeshFilter _filter;
        MeshRenderer _renderer;
        Mesh _mesh;
        readonly TemplateBatch _batch = new();

        const string GlowObjName = "FireflyGlow";
        static readonly int FireflyAmountId = Shader.PropertyToID("_FireflyAmount");
        static Material _sharedGlowMaterial;
        static MaterialPropertyBlock _block;

        // NonSerialized: the Editor keeps plain private fields across a domain reload but not the Critter objects,
        // which left homes without the slots of their fireflies - after a recompile everything starts over.
        [NonSerialized] Vector2[] _homePos = new Vector2[0];
        [NonSerialized] float[] _homePhase = new float[0];
        [NonSerialized] bool[] _homeStrict = new bool[0];
        [NonSerialized] Critter[] _homeFly = new Critter[0];
        [NonSerialized] Vector2[] _blobPos = new Vector2[0];
        [NonSerialized] float[] _blobPhase = new float[0];
        [NonSerialized] int _homeCount, _blobCount, _homeVersion = -1, _glowVersion = -1, _glowTier = -1;
        [NonSerialized] float _homeTimer, _glowTimer, _amount, _pushedAmount = -1f;
        [NonSerialized] bool _glowDirty, _fromStatic;
        GameObject _glowGo;
        MeshRenderer _glowRenderer;
        Mesh _glowMesh;
        readonly GlowBatch _glow = new();

        public LifeTier Tier { get; private set; }
        public int MeshBuilds { get; private set; }
        public int GlowMeshBuilds { get; private set; }
        public int HomeBuilds { get; private set; }
        // 0..1 share of the island's swarm that is out: dusk and dawn fade, thinned by a storm over the island.
        public float FireflyAmount => _amount;
        public int FireflyHomes => _homeCount;
        public int GlowBlobs => _blobCount;
        // What the glow mesh currently holds (halos: wandering fireflies in the near tier, baked homes otherwise).
        public int GlowHaloCount => _glowMesh != null ? _glow.HaloCount : 0;
        public int GlowBlobCount => _glowMesh != null ? _glow.BlobCount : 0;
        public int GlowVertexCount => _glowMesh != null ? _glowMesh.vertexCount : 0;
        public bool GlowVisible => _glowRenderer != null && _glowRenderer.enabled;
        // Fireflies a viewer sees on this island right now, whatever the tier (audio, journal).
        public int FirefliesShown => Tier == LifeTier.Near ? FireflyCount : GlowVisible ? Mathf.RoundToInt(GlowHaloCount * Mathf.Clamp01(_amount / 0.9f)) : 0;
        public Vector2 FireflyHomeOf(int i) => _homePos[i];
        public Vector2 GlowBlobOf(int i) => _blobPos[i];

        // Additive variant of Drift/Critter shared by every firefly swarm and the settlements' window halos.
        public static Material SharedGlowMaterial
        {
            get
            {
                if (_sharedGlowMaterial == null)
                {
                    var shader = Shader.Find("Drift/Critter");
                    if (shader == null) return null;
                    _sharedGlowMaterial = new Material(shader) { name = "FireflyGlow", hideFlags = HideFlags.HideAndDontSave };
                    _sharedGlowMaterial.SetFloat("_GlowMode", 1f);
                    _sharedGlowMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                    _sharedGlowMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                    _sharedGlowMaterial.SetFloat("_ZWrite", 0f);
                    _sharedGlowMaterial.SetFloat("_Cull", 0f);
                    _sharedGlowMaterial.SetFloat("_OffsetFactor", -1f);
                    _sharedGlowMaterial.SetFloat("_OffsetUnits", -1f);
                    _sharedGlowMaterial.SetFloat("_GlowMin", 0.12f);
                    // After the water (Transparent), the plate seams (+1) and the fish (+5).
                    _sharedGlowMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 20;
                }
                return _sharedGlowMaterial;
            }
        }

        public Material GlowMaterial => glowMaterial != null ? glowMaterial : SharedGlowMaterial;
        public int Dives { get; private set; }
        public int ButterflySpawns { get; private set; }
        public int FireflySpawns { get; private set; }
        public int CritterCount => _critters.Count;
        public int MeshVertexCount => _mesh != null ? _mesh.vertexCount : 0;
        public bool HasMesh => _mesh != null;
        public Material CritterMaterial => critterMaterial != null ? critterMaterial : LifeMeshes.CritterMaterial;

        public int CountOf(LifeKind kind)
        {
            int n = 0;
            foreach (var c in _critters) if (c.kind == kind) n++;
            return n;
        }

        public int CrabCount => CountOf(LifeKind.Crab);
        public int TurtleCount => CountOf(LifeKind.Turtle);
        public int ButterflyCount => CountOf(LifeKind.Butterfly);
        public int FireflyCount => CountOf(LifeKind.Firefly);

        public int HiddenCrabs
        {
            get
            {
                int n = 0;
                foreach (var c in _critters) if (c.kind == LifeKind.Crab && c.state == CritterState.Hidden) n++;
                return n;
            }
        }

        public LifeKind KindOf(int i) => _critters[i].kind;
        public Vector2 PositionOf(int i) => _critters[i].pos;
        public Vector2 TargetOf(int i) => _critters[i].target;
        public CritterState StateOf(int i) => _critters[i].state;
        public float FadeOf(int i) => _critters[i].fade;
        public float YawOf(int i) => _critters[i].yaw;
        public bool DyingOf(int i) => _critters[i].dying;

        void OnEnable()
        {
            _surface = GetComponent<IIslandSurface>();
            _life = GetComponent<IslandLifeSystem>();
            if (_surface != null && _surface.LandArea > 0f) Repopulate();
        }

        void OnDestroy()
        {
            DestroyMesh(ref _mesh);
            DestroyMesh(ref _glowMesh);
        }

        static void DestroyMesh(ref Mesh m)
        {
            if (m == null) return;
            if (Application.isPlaying) Destroy(m);
            else DestroyImmediate(m);
            m = null;
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            Step(Time.deltaTime);
        }

        float Rand() => (float)_rnd.NextDouble();
        float Rand(float a, float b) => a + Rand() * (b - a);
        Vector2 RandDir()
        {
            float a = Rand(0f, Mathf.PI * 2f);
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        int CritterSeed => unchecked(seed * 17 + 0x7C4B);

        // ---------------------------------------------------------------- habitat

        bool OnShore(float h) => h >= shoreMin && h <= shoreMax;
        bool TurtleGround(float h) => h >= shoreMin && h <= turtleInland + 0.1f;

        // edge: only the meadow-to-wood band (fireflyStageMin..Max); otherwise any grown ground, which is where
        // a firefly wanders and where it spawns when an island is old growth all over (the player's start island).
        bool FireflyGround(Vector2 p, bool edge)
        {
            float h = _surface.SampleHeight(p);
            if (h < 0.15f || h > 2.8f) return false;
            if (_life == null || !_life.HasGrid) return h >= 0.3f && h <= 1.9f;
            float s = _life.StageAt(p);
            return s >= fireflyStageMin && (!edge || s <= fireflyStageMax);
        }

        // Bare islands (rock, fresh lava, a sandbank) have fireflies too: there any dry ground will do.
        bool DryGround(Vector2 p)
        {
            float h = _surface.SampleHeight(p);
            return h >= 0.15f && h <= 2.8f;
        }

        public int DesiredFireflies()
        {
            float area = _surface.LandArea;
            if (area <= 0.5f || maxFireflies <= 0) return 0;
            int floor = Mathf.Min(minFireflies, Mathf.CeilToInt(area * 1.5f));
            return Mathf.Clamp(Mathf.RoundToInt(area / Mathf.Max(0.5f, areaPerFirefly)), Mathf.Min(floor, maxFireflies), maxFireflies);
        }

        float ComputeAmount()
        {
            float n = Mathf.Clamp01((_night - fireflyDusk) / Mathf.Max(0.01f, nightThreshold - fireflyDusk));
            return n * (1f - fireflyStormCut * Mathf.Clamp01(_surface.StormIntensity));
        }

        // Home i is out while its rank is below the amount, so the swarm grows and thins one firefly at a time
        // and is complete at amount 0.9.
        static float RankOf(int i, int count) => count > 0 ? 0.9f * i / count : 0f;

        // A point with ground height in [minH, maxH]: random tries over the bounds (or a disc round `near`), and
        // from a higher point a march downhill in a random direction with a final bisection, so even a thin
        // shore band is found on any island shape.
        bool TryBand(float minH, float maxH, out Vector2 p, Vector2 near = default, float range = 0f)
        {
            Rect b = _surface.LocalBounds;
            for (int t = 0; t < 24; t++)
            {
                p = range > 0f ? near + RandDir() * (Rand() * range) : new Vector2(b.xMin + Rand() * b.width, b.yMin + Rand() * b.height);
                float h = _surface.SampleHeight(p);
                if (h >= minH && h <= maxH) return true;
                if (h <= maxH) continue;
                Vector2 dir = RandDir();
                Vector2 prev = p;
                for (int k = 1; k <= 40; k++)
                {
                    Vector2 q = p + dir * (0.3f * k);
                    float hq = _surface.SampleHeight(q);
                    if (hq >= minH && hq <= maxH) { p = q; return true; }
                    if (hq < minH)
                    {
                        Vector2 lo = prev, hi = q;
                        for (int i = 0; i < 6; i++)
                        {
                            Vector2 mid = (lo + hi) * 0.5f;
                            float hm = _surface.SampleHeight(mid);
                            if (hm >= minH && hm <= maxH) { p = mid; return true; }
                            if (hm > maxH) lo = mid; else hi = mid;
                        }
                        break;
                    }
                    prev = q;
                }
            }
            p = default;
            return false;
        }

        // Next scuttle target: along the local shore tangent (perpendicular to the height gradient) and nudged
        // back into the band along the gradient, so crabs run sideways along the water line.
        bool TryShoreTarget(Vector2 from, out Vector2 target)
        {
            const float e = 0.15f;
            float gx = _surface.SampleHeight(from + new Vector2(e, 0f)) - _surface.SampleHeight(from - new Vector2(e, 0f));
            float gz = _surface.SampleHeight(from + new Vector2(0f, e)) - _surface.SampleHeight(from - new Vector2(0f, e));
            Vector2 grad = new Vector2(gx, gz);
            if (grad.sqrMagnitude > 1e-6f)
            {
                grad.Normalize();
                Vector2 tangent = new Vector2(-grad.y, grad.x) * (Rand() < 0.5f ? 1f : -1f);
                Vector2 q = from + tangent * Rand(0.4f, crabRange);
                for (int s = 0; s < 7; s++)
                {
                    float off = (s + 1) / 2 * 0.17f * (s % 2 == 0 ? 1f : -1f);
                    Vector2 r = q + grad * off;
                    if (OnShore(_surface.SampleHeight(r))) { target = r; return true; }
                }
            }
            return TryBand(shoreMin, shoreMax, out target, from, crabRange);
        }

        public int DesiredCrabs()
        {
            float area = _surface.LandArea;
            if (area <= 1f) return 0;
            float shore = 2f * Mathf.Sqrt(Mathf.PI * area);
            return Mathf.Clamp(Mathf.RoundToInt(shore / Mathf.Max(0.5f, crabShoreSpacing)), 0, maxCrabs);
        }

        public int DesiredTurtles()
        {
            float area = _surface.LandArea;
            if (area <= turtleMinArea) return 0;
            return Mathf.Min(maxTurtles, 1 + Mathf.FloorToInt((area - turtleMinArea) / 40f));
        }

        // ---------------------------------------------------------------- population

        // Residents from the seed; a repeat call for the same surface Version is a no-op. Later shape changes go
        // through Step (Relocate + Rebalance).
        public void Repopulate()
        {
            if (_surface == null) _surface = GetComponent<IIslandSurface>();
            if (_surface == null) return;
            _life ??= GetComponent<IslandLifeSystem>();
            if (_populated && _rnd != null && _version == _surface.Version) return;
            _rnd = new System.Random(CritterSeed);
            _critters.Clear();
            Array.Clear(_homeFly, 0, _homeFly.Length);
            _glowDirty = true;
            _version = _surface.Version;
            _populated = true;
            _peakArea = _surface.LandArea;
            _night = LifeEnvironment.NightAmount;
            Rebalance();
            RebuildMesh();
            _meshTimer = 0f;
            _meshDirty = false;
        }

        void Rebalance()
        {
            int wantCrabs = DesiredCrabs();
            for (int guard = 0; CountOf(LifeKind.Crab) < wantCrabs && guard < 24; guard++)
            {
                if (!TryBand(shoreMin, shoreMax, out var p)) break;
                AddResident(LifeKind.Crab, p);
            }
            int wantTurtles = DesiredTurtles();
            for (int guard = 0; CountOf(LifeKind.Turtle) < wantTurtles && guard < 12; guard++)
            {
                if (!TryBand(shoreMin + 0.03f, 0.3f, out var p)) break;
                AddResident(LifeKind.Turtle, p);
            }
        }

        Critter AddResident(LifeKind kind, Vector2 p)
        {
            var c = new Critter
            {
                kind = kind, pos = p, target = p, variant = _rnd.Next(LifeMeshes.Variants),
                yaw = Rand(0f, 360f), scale = Rand(0.8f, 1.2f), phase = Rand(0f, 6.28f),
                state = kind == LifeKind.Crab ? CritterState.Idle : CritterState.Rest,
                timer = kind == LifeKind.Crab ? Rand(0.5f, 3f) : Rand(2f, turtleRestMax * 0.5f),
                react = Rand(0f, 0.6f)
            };
            _critters.Add(c);
            _meshDirty = true;
            return c;
        }

        Critter AddTransient(LifeKind kind, Vector2 p)
        {
            var c = new Critter
            {
                kind = kind, pos = p, target = p, variant = _rnd.Next(LifeMeshes.Variants),
                yaw = Rand(0f, 360f), scale = Rand(0.8f, 1.2f), phase = Rand(0f, 6.28f),
                state = kind == LifeKind.Butterfly ? CritterState.Idle : CritterState.Move,
                timer = Rand(0.5f, 2f), fade = 0f
            };
            _critters.Add(c);
            _meshDirty = true;
            return c;
        }

        // Shape changed: residents whose ground left their habitat move to the nearest band or go; new land
        // past the previous peak brings new residents (a sinking island never refills).
        void Relocate()
        {
            for (int i = _critters.Count - 1; i >= 0; i--)
            {
                var c = _critters[i];
                if (c.kind == LifeKind.Butterfly || c.kind == LifeKind.Firefly) continue;
                float h = _surface.SampleHeight(c.pos);
                bool ok = c.kind == LifeKind.Crab ? h >= shoreMin && h <= shoreMax + 0.15f : TurtleGround(h);
                if (ok) continue;
                if (TryBand(shoreMin, c.kind == LifeKind.Crab ? shoreMax : 0.3f, out var p, c.pos, 3f))
                {
                    c.pos = c.target = p;
                    c.state = c.kind == LifeKind.Crab ? CritterState.Idle : CritterState.Rest;
                    c.timer = Rand(1f, 4f);
                }
                else _critters.RemoveAt(i);
                _meshDirty = true;
            }
        }

        // ---------------------------------------------------------------- stepping

        public void Step(float dt)
        {
            if (_surface == null) _surface = GetComponent<IIslandSurface>();
            if (_surface == null || _rnd == null) return;
            _clock += dt;
            _night = LifeEnvironment.NightAmount;

            float dist = LifeLod.Distance(transform.position);
            if (_renderer != null) _renderer.enabled = dist < hideDistance;
            Tier = LifeLod.Tier(dist, detailDistance, simDistance);

            if (_surface.Version != _version)
            {
                _version = _surface.Version;
                Relocate();
                if (_surface.LandArea > _peakArea + 0.5f)
                {
                    _peakArea = _surface.LandArea;
                    Rebalance();
                }
                _meshDirty = true;
                _meshTimer = meshInterval;
            }

            _meshTimer += dt;
            float interval = meshInterval;
            if (Tier == LifeTier.Far)
            {
                for (int i = _critters.Count - 1; i >= 0; i--)
                {
                    if (_critters[i].kind == LifeKind.Butterfly) { _critters.RemoveAt(i); _meshDirty = true; }
                }
                DropFireflies();
                _stepTimer = 0f;
            }
            else if (Tier == LifeTier.Mid)
            {
                DropFireflies();
                _stepTimer += dt;
                if (_stepTimer >= midStepInterval)
                {
                    StepResidents(_stepTimer);
                    _stepTimer = 0f;
                }
                interval = meshInterval * 2f;
            }
            else
            {
                _stepTimer = 0f;
                StepResidents(dt);
                StepTransients(dt);
                _spawnTimer += dt;
                if (_spawnTimer >= spawnInterval)
                {
                    _spawnTimer = 0f;
                    SpawnTransients();
                }
            }

            if (_meshDirty && _meshTimer >= interval)
            {
                _meshTimer = 0f;
                _meshDirty = false;
                RebuildMesh();
            }
            StepGlow(dt, dist < hideDistance);
        }

        // ---------------------------------------------------------------- fireflies

        void DropFireflies()
        {
            bool any = false;
            for (int i = _critters.Count - 1; i >= 0; i--)
                if (_critters[i].kind == LifeKind.Firefly) { _critters.RemoveAt(i); any = true; }
            if (!any) return;
            Array.Clear(_homeFly, 0, _homeFly.Length);
            _glowDirty = true;
        }

        // The whole firefly side of a step. By day, hidden or without land this is a handful of compares; at
        // night the near tier re-bakes its halos at meshInterval, the other tiers bake once per tier and Version.
        void StepGlow(float dt, bool visible)
        {
            _amount = ComputeAmount();
            _homeTimer += dt;
            _glowTimer += dt;
            if (!visible || _amount <= 0.001f)
            {
                if (_glowRenderer != null) _glowRenderer.enabled = false;
                if (Tier == LifeTier.Near) SyncFireflies();
                _fromStatic = Tier != LifeTier.Near;
                return;
            }
            if (_homeVersion != _version && (_homeVersion < 0 || _homeTimer >= 2f)) BuildHomes();
            if (_homeVersion < 0) return;

            if (Tier == LifeTier.Near)
            {
                SyncFireflies();
                _fromStatic = false;
                bool stale = _glowTier != (int)LifeTier.Near || _glowVersion != _homeVersion;
                if (stale || (_glowDirty && _glowTimer >= meshInterval)) RebuildGlow();
            }
            else
            {
                _fromStatic = true;
                if (_glowTier != (int)Tier || _glowVersion != _homeVersion) RebuildGlow();
            }

            if (_glowRenderer == null) return;
            if (Mathf.Abs(_amount - _pushedAmount) > 0.01f || (_amount >= 0.9f) != (_pushedAmount >= 0.9f))
            {
                _pushedAmount = _amount;
                _block ??= new MaterialPropertyBlock();
                _block.SetFloat(FireflyAmountId, _amount);
                _glowRenderer.SetPropertyBlock(_block);
            }
            _glowRenderer.enabled = _glowMesh != null && _glowMesh.vertexCount > 0;
        }

        // Seeded per island and shape, independent of the residents' random stream: a few cluster centres on
        // meadow-to-wood ground (then any grown ground, then any dry ground on a bare island) and the homes
        // scattered round them in turn, so a thinned swarm still covers every cluster.
        void BuildHomes()
        {
            HomeBuilds++;
            _homeVersion = _version;
            _homeTimer = 0f;
            int want = DesiredFireflies();
            if (_homePos.Length < want)
            {
                _homePos = new Vector2[want];
                _homePhase = new float[want];
                _homeStrict = new bool[want];
            }
            if (_homeFly.Length < want) Array.Resize(ref _homeFly, want);
            int maxBlobs = Mathf.Max(1, maxGlowBlobs);
            if (_blobPos.Length < maxBlobs) { _blobPos = new Vector2[maxBlobs]; _blobPhase = new float[maxBlobs]; }

            var rnd = new System.Random(unchecked(CritterSeed * 7919 + 0x51F1));
            Rect b = _surface.LocalBounds;
            float area = _surface.LandArea;
            int clusters = Mathf.Clamp(Mathf.CeilToInt(area / Mathf.Max(1f, areaPerGlowBlob)), 1, Mathf.Min(maxBlobs, Mathf.Max(1, want / 4)));
            float apart = Mathf.Min(fireflyClusterRadius * 1.6f, Mathf.Sqrt(Mathf.Max(area, 0.1f) / clusters));
            _blobCount = 0;
            bool grown = false;
            for (int k = 0; k < clusters && want > 0; k++)
            {
                for (int t = 0; t < 40; t++)
                {
                    Vector2 p = new Vector2(b.xMin + (float)rnd.NextDouble() * b.width, b.yMin + (float)rnd.NextDouble() * b.height);
                    bool ok = t < 16 ? FireflyGround(p, true) : t < 28 ? FireflyGround(p, false) : !grown && DryGround(p);
                    if (!ok) continue;
                    float need = t < 28 ? apart : apart * 0.5f;
                    bool clear = true;
                    for (int j = 0; j < _blobCount; j++) if ((_blobPos[j] - p).sqrMagnitude < need * need) { clear = false; break; }
                    if (!clear) continue;
                    if (t < 28) grown = true;
                    _blobPos[_blobCount] = p;
                    _blobPhase[_blobCount] = (float)rnd.NextDouble() * 6.28f;
                    _blobCount++;
                    break;
                }
            }

            _homeCount = 0;
            for (int i = 0; i < want && _blobCount > 0; i++)
            {
                Vector2 c = _blobPos[i % _blobCount];
                Vector2 p = c;
                for (int t = 0; t < 6; t++)
                {
                    double a = rnd.NextDouble() * Math.PI * 2.0;
                    float r = Mathf.Sqrt((float)rnd.NextDouble()) * fireflyClusterRadius;
                    Vector2 q = c + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * r;
                    if (grown ? !FireflyGround(q, false) : !DryGround(q)) continue;
                    p = q;
                    break;
                }
                _homePos[_homeCount] = p;
                _homePhase[_homeCount] = (float)rnd.NextDouble() * 6.28f;
                _homeStrict[_homeCount] = grown;
                _homeCount++;
            }
            for (int i = 0; i < _homeFly.Length; i++)
            {
                var f = _homeFly[i];
                if (f == null) continue;
                if (i >= _homeCount || !DryGround(f.pos)) f.dying = true;
                else if (!DryGround(f.target)) f.target = f.pos;
            }
            _glowDirty = true;
        }

        // Near tier: one wandering firefly per home whose rank is below the amount. Coming in from the static
        // swarm (mid tier) they start full bright at their homes, so the hand-over does not show.
        void SyncFireflies()
        {
            bool night = _amount > 0.001f && _homeVersion >= 0;
            for (int i = 0; i < _homeFly.Length; i++)
            {
                var f = _homeFly[i];
                bool live = night && i < _homeCount && RankOf(i, _homeCount) < _amount;
                if (f != null)
                {
                    if (!live) f.dying = true;
                    else if (f.dying && i < _homeCount && DryGround(f.pos)) f.dying = false;
                    continue;
                }
                if (!live) continue;
                var c = AddTransient(LifeKind.Firefly, _homePos[i]);
                c.slot = i;
                c.phase = _homePhase[i];
                c.timer = Rand(0f, 1.5f);
                if (_fromStatic) c.fade = 1f;
                _homeFly[i] = c;
                FireflySpawns++;
                _glowDirty = true;
            }
        }

        void EnsureGlowObject()
        {
            if (_glowGo == null || _glowGo.transform.parent != transform)
            {
                _glowGo = null;
                for (int i = transform.childCount - 1; i >= 0; i--)
                {
                    var child = transform.GetChild(i).gameObject;
                    if (child.name != GlowObjName) continue;
                    if (_glowGo == null) _glowGo = child;
                    else if (Application.isPlaying) Destroy(child);
                    else DestroyImmediate(child);
                }
                if (_glowGo == null)
                {
                    _glowGo = new GameObject(GlowObjName);
                    _glowGo.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                    _glowGo.transform.SetParent(transform, false);
                    _glowGo.AddComponent<MeshFilter>();
                    var mr = _glowGo.AddComponent<MeshRenderer>();
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                }
                _pushedAmount = -1f;
            }
            var filter = _glowGo.GetComponent<MeshFilter>();
            _glowRenderer = _glowGo.GetComponent<MeshRenderer>();
            _glowRenderer.sharedMaterial = GlowMaterial;
            if (_glowMesh == null) _glowMesh = filter.sharedMesh;
            if (_glowMesh == null) _glowMesh = new Mesh { name = GlowObjName, hideFlags = HideFlags.DontSave };
            filter.sharedMesh = _glowMesh;
        }

        float FireflyLift(int i)
        {
            float f = Mathf.Repeat((i + 1) * 0.6180339f, 1f);
            return fireflyHeight + fireflyHeightSpread * f * f;
        }

        Color FireflyTone(int i)
        {
            Color c = fireflyColor;
            return i % 3 == 1 ? new Color(c.g * 0.92f, c.g * 0.85f, c.b, 1f) : c;
        }

        void RebuildGlow()
        {
            EnsureGlowObject();
            GlowMeshBuilds++;
            _glowTimer = 0f;
            _glowDirty = false;
            // The blobs only change with the homes or the tier: the 12 Hz near-tier rebuild keeps them (their
            // terrain samples are most of a rebuild) and rewrites the halos behind them.
            if (_glowTier == (int)Tier && _glowVersion == _homeVersion && _glow.Marked) _glow.Rewind();
            else
            {
                _glow.Begin();
                int blobs = Tier == LifeTier.Far ? Mathf.Min(2, _blobCount) : _blobCount;
                for (int i = 0; i < blobs; i++)
                    _glow.AddBlob(_surface, _blobPos[i], glowBlobRadius, glowBlobLift, groundGlowColor, _blobPhase[i], 0.04f + 0.3f * i / Mathf.Max(1, blobs));
                _glow.Mark();
            }
            _glowTier = (int)Tier;
            _glowVersion = _homeVersion;
            if (Tier == LifeTier.Near)
            {
                foreach (var c in _critters)
                {
                    if (c.kind != LifeKind.Firefly || c.fade <= 0f) continue;
                    float fade = Mathf.Clamp01(c.fade);
                    float y = Mathf.Max(_surface.SampleHeight(c.pos), 0f) + FireflyLift(c.slot);
                    _glow.AddHalo(new Vector3(c.pos.x, y, c.pos.y), fireflyHalo * c.scale * (0.4f + 0.6f * fade), FireflyTone(c.slot) * fade,
                        c.phase, 0.08f, 0.06f, 1f, -1f, fireflyMinAngle * fade);
                }
            }
            else
            {
                int n = Tier == LifeTier.Far ? Mathf.Min(farFireflies, _homeCount) : _homeCount;
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = _homePos[i];
                    float y = Mathf.Max(_surface.SampleHeight(p), 0f) + FireflyLift(i);
                    _glow.AddHalo(new Vector3(p.x, y, p.y), fireflyHalo, FireflyTone(i), _homePhase[i], 0.08f, fireflyDrift, 1f, RankOf(i, n), fireflyMinAngle);
                }
            }
            _glow.Apply(_glowMesh, 2.5f);
        }

        void StepResidents(float dt)
        {
            float speed = _surface.Speed;
            bool fast = speed > diveSpeed;
            for (int i = 0; i < _critters.Count; i++)
            {
                var c = _critters[i];
                if (c.kind == LifeKind.Crab) { if (StepCrab(c, dt, fast)) _meshDirty = true; }
                else if (c.kind == LifeKind.Turtle) { if (StepTurtle(c, dt)) _meshDirty = true; }
            }
        }

        bool StepCrab(Critter c, float dt, bool fast)
        {
            if (c.state == CritterState.Hidden)
            {
                if (fast) { c.timer = Mathf.Max(c.timer, hiddenMin); c.alarm = 0f; return false; }
                c.timer -= dt;
                if (c.timer > 0f) return false;
                c.state = CritterState.Idle;
                c.timer = Rand(0.5f, 2.5f);
                return true;
            }
            if (fast)
            {
                c.alarm += dt;
                if (c.alarm < c.react) return false;
                c.state = CritterState.Hidden;
                c.timer = Rand(hiddenMin, hiddenMax);
                c.alarm = 0f;
                Dives++;
                return true;
            }
            c.alarm = 0f;
            c.timer -= dt;
            switch (c.state)
            {
                case CritterState.Move:
                {
                    Vector2 to = c.target - c.pos;
                    float d = to.magnitude;
                    if (d < 0.02f || c.timer <= 0f) { c.state = CritterState.Idle; c.timer = Rand(1f, 4f); return true; }
                    Vector2 next = c.pos + to / d * Mathf.Min(crabSpeed * dt, d);
                    if (next == c.pos) return false;
                    if (!OnShore(_surface.SampleHeight(next))) { c.state = CritterState.Idle; c.timer = Rand(0.5f, 2f); return true; }
                    c.pos = next;
                    // Sideways: the body faces across the direction of travel, the side fixed per crab.
                    Vector2 face = c.variant % 2 == 0 ? new Vector2(-to.y, to.x) : new Vector2(to.y, -to.x);
                    c.yaw = Mathf.Atan2(face.x, face.y) * Mathf.Rad2Deg;
                    return true;
                }
                case CritterState.Dig:
                    if (c.timer > 0f) return false;
                    c.state = CritterState.Idle;
                    c.timer = Rand(1f, 4f);
                    return true;
                default:
                    if (c.timer > 0f) return false;
                    if (Rand() < 0.3f) { c.state = CritterState.Dig; c.timer = Rand(2f, 4f); return true; }
                    if (TryShoreTarget(c.pos, out var t))
                    {
                        c.target = t;
                        c.state = CritterState.Move;
                        c.timer = (t - c.pos).magnitude / crabSpeed + 2f;
                        return true;
                    }
                    c.timer = Rand(1f, 3f);
                    return false;
            }
        }

        bool StepTurtle(Critter c, float dt)
        {
            c.timer -= dt;
            if (c.state == CritterState.Move)
            {
                Vector2 to = c.target - c.pos;
                float d = to.magnitude;
                if (d < 0.03f || c.timer <= 0f) { c.state = CritterState.Rest; c.timer = Rand(turtleRestMin, turtleRestMax); return true; }
                Vector2 next = c.pos + to / d * Mathf.Min(turtleSpeed * dt, d);
                if (next == c.pos) return false;
                if (!TurtleGround(_surface.SampleHeight(next))) { c.state = CritterState.Rest; c.timer = Rand(turtleRestMin, turtleRestMax) * 0.5f; return true; }
                c.pos = next;
                c.yaw = Mathf.LerpAngle(c.yaw, Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg, 1f - Mathf.Exp(-2f * dt));
                return true;
            }
            if (c.timer > 0f || _night >= nightThreshold) return false;
            float h = _surface.SampleHeight(c.pos);
            bool found = h < 0.3f
                ? TryBand(0.3f, turtleInland, out var t, c.pos, 3f)
                : TryBand(shoreMin + 0.03f, 0.3f, out t, c.pos, 4f);
            if (!found) { c.timer = Rand(5f, 15f); return false; }
            c.target = t;
            c.state = CritterState.Move;
            c.timer = (t - c.pos).magnitude / turtleSpeed + 5f;
            return true;
        }

        void StepTransients(float dt)
        {
            bool day = _night < dayThreshold;
            for (int i = _critters.Count - 1; i >= 0; i--)
            {
                var c = _critters[i];
                bool fly = c.kind == LifeKind.Firefly;
                if (c.kind == LifeKind.Butterfly) { if (!day) c.dying = true; }
                else if (!fly) continue;

                if (c.dying)
                {
                    c.fade -= dt / Mathf.Max(fadeTime, 0.01f);
                    if (fly) _glowDirty = true; else _meshDirty = true;
                    if (c.fade <= 0f)
                    {
                        if (fly && c.slot >= 0 && c.slot < _homeFly.Length && _homeFly[c.slot] == c) _homeFly[c.slot] = null;
                        _critters.RemoveAt(i);
                    }
                    continue;
                }
                if (c.fade < 1f)
                {
                    c.fade = Mathf.Min(1f, c.fade + dt / Mathf.Max(fadeTime, 0.01f));
                    if (fly) _glowDirty = true; else _meshDirty = true;
                }
                if (c.kind == LifeKind.Butterfly) StepButterfly(c, dt);
                else StepFirefly(c, dt);
            }
        }

        void StepButterfly(Critter c, float dt)
        {
            if (c.state == CritterState.Move)
            {
                Vector2 to = c.target - c.pos;
                float d = to.magnitude;
                if (d < 0.03f) { c.pos = c.target; c.state = CritterState.Idle; c.timer = Rand(1f, 4f); _meshDirty = true; return; }
                c.pos += to / d * Mathf.Min(butterflySpeed * dt, d);
                c.yaw = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
                _meshDirty = true;
                return;
            }
            c.timer -= dt;
            if (c.timer > 0f) return;
            if (_life != null && _life.TryRandomPlant(LifeKind.Flower, _rnd, c.pos, butterflyRange, out var next))
            {
                c.target = next;
                c.state = CritterState.Move;
            }
            else c.dying = true;
        }

        // Wanders round its home, so a swarm stays over its cluster and its ground glow; a short pause now and then.
        void StepFirefly(Critter c, float dt)
        {
            Vector2 to = c.target - c.pos;
            float d = to.magnitude;
            if (d < 0.05f)
            {
                c.timer -= dt;
                if (c.timer > 0f) return;
                bool homed = c.slot >= 0 && c.slot < _homeCount;
                Vector2 home = homed ? _homePos[c.slot] : c.pos;
                bool strict = !homed || _homeStrict[c.slot];
                for (int t = 0; t < 8; t++)
                {
                    Vector2 q = home + RandDir() * Rand(0.2f, fireflyRange);
                    if (strict ? FireflyGround(q, false) : DryGround(q)) { c.target = q; break; }
                }
                c.timer = Rand(0f, 1.5f);
                return;
            }
            c.pos += to / d * Mathf.Min(fireflySpeed * dt, d);
            c.yaw = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
            _glowDirty = true;
        }

        // Near tier only: a few butterflies per interval so meadows fill up gradually; surplus over the current
        // want (fewer flowers) fades out. Fireflies follow their homes and the amount instead (SyncFireflies).
        void SpawnTransients()
        {
            bool day = _night < dayThreshold;
            int butterflies = 0;
            foreach (var c in _critters)
                if (!c.dying && c.kind == LifeKind.Butterfly) butterflies++;
            int wantB = day && _life != null ? Mathf.Min(maxButterflies, _life.CountOf(LifeKind.Flower) / Mathf.Max(1, butterflyFlowers)) : 0;
            for (int n = 0; n < spawnsPerInterval && butterflies < wantB; n++)
            {
                if (!_life.TryRandomPlant(LifeKind.Flower, _rnd, default, 0f, out var p)) break;
                AddTransient(LifeKind.Butterfly, p);
                ButterflySpawns++;
                butterflies++;
            }
            for (int i = _critters.Count - 1; i >= 0 && butterflies > wantB; i--)
            {
                var c = _critters[i];
                if (c.dying || c.kind != LifeKind.Butterfly) continue;
                c.dying = true;
                butterflies--;
            }
        }

        // ---------------------------------------------------------------- mesh

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
            _renderer.sharedMaterial = CritterMaterial;
            if (_mesh == null) _mesh = _filter.sharedMesh;
            if (_mesh == null) _mesh = new Mesh { name = "Critters", hideFlags = HideFlags.DontSave };
            _filter.sharedMesh = _mesh;
        }

        void RebuildMesh()
        {
            EnsureObject();
            MeshBuilds++;
            _batch.Begin();
            foreach (var c in _critters)
            {
                if (c.state == CritterState.Hidden || c.fade <= 0f || c.kind == LifeKind.Firefly) continue;
                float h = _surface.SampleHeight(c.pos);
                float x = c.pos.x, z = c.pos.y, y = Mathf.Max(h, 0f);
                float size, flutter = 0f, bob = 0f;
                switch (c.kind)
                {
                    case LifeKind.Crab:
                        size = crabScale * c.scale;
                        if (c.state == CritterState.Dig) y -= size * 0.4f;
                        break;
                    case LifeKind.Turtle:
                        size = turtleScale * c.scale;
                        break;
                    case LifeKind.Butterfly:
                        size = butterflyScale * c.scale * Mathf.Clamp01(c.fade);
                        y += butterflyHeight;
                        x += Mathf.Sin(_clock * 2.3f + c.phase) * 0.05f;
                        z += Mathf.Cos(_clock * 1.9f + c.phase) * 0.05f;
                        flutter = 1f;
                        bob = 0.03f;
                        break;
                    default:
                        continue;
                }
                if (size < 0.001f) continue;
                _batch.AddCritter(LifeMeshes.GetTemplate(c.kind, c.variant), new Vector3(x, y, z), c.yaw, size, flutter, bob, c.phase, 0f);
            }
            _batch.Apply(_mesh);
        }

        // ---------------------------------------------------------------- merging

        public void ShiftLocal(Vector2 delta)
        {
            foreach (var c in _critters) { c.pos += delta; c.target += delta; }
            for (int i = 0; i < _homeCount; i++) _homePos[i] += delta;
            for (int i = 0; i < _blobCount; i++) _blobPos[i] += delta;
            _glowVersion = -2;
            _glowDirty = true;
        }

        public void AbsorbFrom(IslandCrittersSystem other)
        {
            if (other == null || other == this) return;
            foreach (var c in other._critters)
            {
                if (c.kind == LifeKind.Butterfly || c.kind == LifeKind.Firefly) continue;
                c.pos = Convert(other.transform, c.pos);
                c.target = c.pos;
                if (c.state == CritterState.Move) c.state = c.kind == LifeKind.Crab ? CritterState.Idle : CritterState.Rest;
                _critters.Add(c);
            }
            other._critters.Clear();
            Array.Clear(other._homeFly, 0, other._homeFly.Length);
            other._glowDirty = true;
            EnforceCaps();
            _meshDirty = true;
        }

        void EnforceCaps()
        {
            int crabs = 0, turtles = 0;
            for (int i = 0; i < _critters.Count; i++)
            {
                var c = _critters[i];
                if (c.kind == LifeKind.Crab && ++crabs > maxCrabs) _critters.RemoveAt(i--);
                else if (c.kind == LifeKind.Turtle && ++turtles > maxTurtles) _critters.RemoveAt(i--);
            }
        }

        Vector2 Convert(Transform from, Vector2 p)
        {
            Vector3 w = from.TransformPoint(p.x, 0f, p.y);
            Vector3 l = transform.InverseTransformPoint(w);
            return new Vector2(l.x, l.z);
        }

        // ---------------------------------------------------------------- persistence

        public CritterSaveData Capture()
        {
            int n = 0;
            foreach (var c in _critters) if (c.kind == LifeKind.Crab || c.kind == LifeKind.Turtle) n++;
            var d = new CritterSaveData { kind = new int[n], variant = new int[n], state = new int[n], m = new float[n * CritterSaveData.Stride] };
            int k = 0;
            foreach (var c in _critters)
            {
                if (c.kind != LifeKind.Crab && c.kind != LifeKind.Turtle) continue;
                d.kind[k] = (int)c.kind;
                d.variant[k] = c.variant;
                d.state[k] = (int)c.state;
                int o = k * CritterSaveData.Stride;
                d.m[o] = c.pos.x; d.m[o + 1] = c.pos.y; d.m[o + 2] = c.yaw; d.m[o + 3] = c.scale;
                d.m[o + 4] = c.timer; d.m[o + 5] = c.target.x; d.m[o + 6] = c.target.y;
                k++;
            }
            return d;
        }

        public void Restore(CritterSaveData d)
        {
            if (_surface == null) _surface = GetComponent<IIslandSurface>();
            if (_surface == null || d == null || d.kind == null || d.m == null) return;
            _life ??= GetComponent<IslandLifeSystem>();
            _rnd ??= new System.Random(CritterSeed);
            _night = LifeEnvironment.NightAmount;
            _critters.Clear();
            Array.Clear(_homeFly, 0, _homeFly.Length);
            _glowDirty = true;
            int n = Mathf.Min(d.kind.Length, d.m.Length / CritterSaveData.Stride);
            for (int i = 0; i < n; i++)
            {
                var kind = (LifeKind)d.kind[i];
                if (kind != LifeKind.Crab && kind != LifeKind.Turtle) continue;
                int o = i * CritterSaveData.Stride;
                var c = new Critter
                {
                    kind = kind,
                    variant = d.variant != null && i < d.variant.Length ? d.variant[i] : 0,
                    pos = new Vector2(d.m[o], d.m[o + 1]), yaw = d.m[o + 2], scale = d.m[o + 3], timer = d.m[o + 4],
                    target = new Vector2(d.m[o + 5], d.m[o + 6]), phase = Rand(0f, 6.28f), react = Rand(0f, 0.6f)
                };
                var st = d.state != null && i < d.state.Length ? (CritterState)Mathf.Clamp(d.state[i], 0, 4) : CritterState.Idle;
                if (kind == LifeKind.Crab && st == CritterState.Hidden) { st = CritterState.Idle; c.timer = Rand(0.5f, 2f); }
                c.state = st;
                if (c.scale <= 0f) c.scale = 1f;
                _critters.Add(c);
            }
            _version = _surface.Version;
            _populated = true;
            _peakArea = _surface.LandArea;
            Relocate();
            EnforceCaps();
            RebuildMesh();
            _meshTimer = 0f;
            _meshDirty = false;
        }
    }

    // Mesh writer for the additive glow variant of Drift/Critter (see the shader header for the channels): camera
    // facing halos that are expanded on the GPU and ground blobs sampled onto the terrain. Arrays are reused, so
    // a rebuild allocates nothing once they have grown.
    public class GlowBatch
    {
        const int BlobRing = 12;
        public const int HaloVerts = 4;
        public const int BlobVerts = 1 + 2 * BlobRing;

        Vector3[] _v = new Vector3[256];
        Color[] _c = new Color[256];
        Vector4[] _uv0 = new Vector4[256];
        Vector4[] _uv1 = new Vector4[256];
        Vector4[] _uv2 = new Vector4[256];
        int[] _t = new int[512];
        int _vc, _tc;

        public int VertexCount => _vc;
        public int HaloCount { get; private set; }
        public int BlobCount { get; private set; }

        int _markV = -1, _markT, _markHalos, _markBlobs;

        public bool Marked => _markV >= 0;

        public void Begin()
        {
            _vc = 0;
            _tc = 0;
            HaloCount = 0;
            BlobCount = 0;
            _markV = -1;
        }

        // Mark what has been written so far as the part that stays; Rewind drops everything after it.
        public void Mark()
        {
            _markV = _vc;
            _markT = _tc;
            _markHalos = HaloCount;
            _markBlobs = BlobCount;
        }

        public void Rewind()
        {
            if (_markV < 0) { Begin(); return; }
            _vc = _markV;
            _tc = _markT;
            HaloCount = _markHalos;
            BlobCount = _markBlobs;
        }

        void Reserve(int verts, int tris)
        {
            if (_vc + verts > _v.Length)
            {
                int cap = Mathf.Max(_vc + verts, _v.Length * 2);
                Array.Resize(ref _v, cap);
                Array.Resize(ref _c, cap);
                Array.Resize(ref _uv0, cap);
                Array.Resize(ref _uv1, cap);
                Array.Resize(ref _uv2, cap);
            }
            if (_tc + tris > _t.Length) Array.Resize(ref _t, Mathf.Max(_tc + tris, _t.Length * 2));
        }

        // radius in world units; minAngle = smallest radius on screen as tan of the half angle; rank < 0 = always
        // shown, otherwise shown while rank < _FireflyAmount; blink 0 = steady light, 1 = firefly blink.
        public void AddHalo(Vector3 centre, float radius, Color color, float phase, float bob, float drift, float blink, float rank, float minAngle)
        {
            Reserve(HaloVerts, 6);
            int b = _vc;
            color.a = 1f;
            var u1 = new Vector4(bob, radius, drift, blink);
            var u2 = new Vector4(rank, minAngle, 0f, 0f);
            for (int i = 0; i < 4; i++)
            {
                _v[b + i] = centre;
                _c[b + i] = color;
                _uv0[b + i] = new Vector4((i == 1 || i == 2) ? 1f : -1f, i >= 2 ? 1f : -1f, phase, 2f);
                _uv1[b + i] = u1;
                _uv2[b + i] = u2;
            }
            _t[_tc++] = b; _t[_tc++] = b + 2; _t[_tc++] = b + 1;
            _t[_tc++] = b; _t[_tc++] = b + 3; _t[_tc++] = b + 2;
            _vc += HaloVerts;
            HaloCount++;
        }

        // A soft disc hugging the ground: centre, a ring at half the radius and the rim (falloff 1 / 0.55 / 0 in
        // the colour alpha, squared in the shader), every vertex on the terrain (not below the water line) + lift.
        public void AddBlob(IIslandSurface surface, Vector2 centre, float radius, float lift, Color color, float phase, float rank)
        {
            Reserve(BlobVerts, BlobRing * 9);
            int b = _vc;
            var u0 = new Vector4(0f, 0f, phase, 3f);
            var u1 = new Vector4(0f, 0f, 0f, 0.3f);
            var u2 = new Vector4(rank, 0f, 0f, 0f);
            for (int i = 0; i < BlobVerts; i++)
            {
                Vector2 p = centre;
                float fall = 1f;
                if (i > 0)
                {
                    int k = (i - 1) % BlobRing;
                    bool rim = i > BlobRing;
                    float a = (k + (rim ? 0.5f : 0f)) * Mathf.PI * 2f / BlobRing;
                    p += new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (rim ? radius : radius * 0.5f);
                    fall = rim ? 0f : 0.55f;
                }
                _v[b + i] = new Vector3(p.x, Mathf.Max(surface.SampleHeight(p), 0f) + lift, p.y);
                color.a = fall;
                _c[b + i] = color;
                _uv0[b + i] = u0;
                _uv1[b + i] = u1;
                _uv2[b + i] = u2;
            }
            for (int k = 0; k < BlobRing; k++)
            {
                int i0 = b + 1 + k, i1 = b + 1 + (k + 1) % BlobRing, o0 = b + 1 + BlobRing + k;
                int oPrev = b + 1 + BlobRing + (k + BlobRing - 1) % BlobRing;
                _t[_tc++] = b; _t[_tc++] = i1; _t[_tc++] = i0;
                _t[_tc++] = i0; _t[_tc++] = i1; _t[_tc++] = o0;
                _t[_tc++] = i0; _t[_tc++] = o0; _t[_tc++] = oPrev;
            }
            _vc += BlobVerts;
            BlobCount++;
        }

        // pad widens the bounds by what the GPU adds (halo size, drift, bob).
        public void Apply(Mesh m, float pad)
        {
            m.Clear();
            m.SetVertices(_v, 0, _vc);
            m.SetColors(_c, 0, _vc);
            m.SetUVs(0, _uv0, 0, _vc);
            m.SetUVs(1, _uv1, 0, _vc);
            m.SetUVs(2, _uv2, 0, _vc);
            m.SetTriangles(_t, 0, _tc, 0, false);
            m.RecalculateBounds();
            var bounds = m.bounds;
            bounds.Expand(pad * 2f);
            m.bounds = bounds;
        }
    }
}
