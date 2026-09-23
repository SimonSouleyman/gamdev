using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Drift.Tectonics
{
    public enum BoundaryKind { Convergent, Divergent, Transform }

    [System.Serializable]
    public class PlateSaveData
    {
        public float time;
        // Absent in older saves: JsonUtility keeps the initialiser, and -1 means "derive it from time".
        public float waveClock = -1f;
        public List<PlateOffsetData> offsets = new();
        public uint eventRng;
        public List<PlateEventData> events = new();
    }

    [System.Serializable]
    public struct PlateOffsetData
    {
        public int cx, cz;
        public float ox, oy, px, py;
    }

    [ExecuteAlways]
    public partial class PlateSystem : MonoBehaviour
    {
        public class PlateCore
        {
            public Vector2Int id;
            public Vector2 home;
            public Vector2 dir;
            // Per-plate factors on waveAmplitude / waveSpeed, applied every frame so the Inspector sliders act live.
            public float ampK;
            public float omegaK;
            public float phase;
            public Vector2 offset;
            public Vector2 pushVel;
        }

        public class Plate
        {
            public PlateCore core;
            public Vector2Int cell;
            public Vector2 shift;
            public Vector2 position;
            public Vector2 velocity;
        }

        // p0 -> p1 is the straight Voronoi chord (what "which plate am I on" uses). The visible seam is the curve
        // of PlateSystem.Seams.cs over that chord; everything below `closing` parameterises it and is canonical
        // for the plate pair: the same in every wrapped copy of the torus and whichever plate is `a`.
        public struct Border
        {
            public Plate a, b;
            public Vector2 p0, p1;
            public BoundaryKind kind;
            public float closing;
            public Vector2 normal;
            public float s0, length;
            public float amplitude, taper;
            public float freq, phase;
            public int key;
            public bool open0, open1;
        }

        const string BorderObjectName = "PlateBorders";
        const string SeamShaderName = "Drift/PlateSeam";

        // Chevron cell of the current hints; keep in sync with CHEVRON_U / CHEVRON_V in PlateSeam.shader.
        const float ChevronPeriodU = 1.8f;
        const float ChevronPeriodV = 1.2f;

        // Pooled, fixed-size mesh buffers: the 30 Hz rebuild writes one interleaved array and uploads the used part.
        public const int MaxSeamVertices = 1808;
        const int MaxPulseVertices = 240;
        const int MaxSeamSegments = 24;

        [StructLayout(LayoutKind.Sequential)]
        struct SeamVertex
        {
            public Vector3 pos;
            public Vector4 uv0, uv1, uv2, uv3;
        }

        static readonly VertexAttributeDescriptor[] SeamLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4),
        };

        const MeshUpdateFlags SeamUpload = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds
            | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;

        static readonly int SeamFocusId = Shader.PropertyToID("_SeamFocus");
        static readonly int SeamOpacityId = Shader.PropertyToID("_SeamOpacity");

        static readonly List<IPlateRider> Riders = new();

        public static PlateSystem Instance { get; private set; }

        // Fired at the end of Restore (a new game, a restart, a load): listeners rebuild what depends on the world.
        public static event System.Action Restored;

        public static void Register(IPlateRider rider)
        {
            if (!Riders.Contains(rider)) Riders.Add(rider);
        }

        public static void Unregister(IPlateRider rider) => Riders.Remove(rider);

        [Header("Plattenraster")]
        [Tooltip("Kantenlänge einer Plattenzelle: so groß ist eine Platte ungefähr. cellSize · gridPeriod muss genau die Weltbreite sein (WorldStreamer: chunkSize · worldChunks), sonst passen die Platten an der Weltkante nicht aneinander. Gemütliche Welt: 3 × 102 = 306.")]
        public float cellSize = 102f;
        [Tooltip("Platten pro Weltseite (die Welt wiederholt sich danach). Mindestens 3, damit keine Platte ihr eigener Nachbar ist.")]
        public int gridPeriod = 3;
        public float jitter = 0.25f;
        public int seed = 4242;

        [Header("Ringwelt (Abenteuer)")]
        [Tooltip("So viele Surfspuren liegen nebeneinander über dem Band: ihre Grenzen laufen längs der Strecke.")]
        [Range(1, 5)] public int ringLanes = 3;
        [Tooltip("Länge eines Spurabschnitts entlang des Rings. An seinen Enden versetzen sich die Spuren seitlich – so lange hält eine Grenze, auf der man surft (160 u ≈ 8 s bei 20 u/s, 6 s bei 26 u/s). Es passen immer ganze Abschnitte auf eine Runde.")]
        [Range(30f, 300f)] public float ringPlateLength = 160f;
        [Tooltip("Zufälliger seitlicher Versatz der Spuren relativ zum normalen Versatz.")]
        [Range(0f, 1f)] public float ringJitter = 0.2f;
        [Tooltip("Wie weit die Spuren seitlich wandern (Anteil einer Spurbreite). Rücken zwei zusammen, verschmelzen sie; laufen sie auseinander, teilt sich die Bahn.")]
        [Range(0f, 0.45f)] public float ringLaneShift = 0.3f;
        [Tooltip("Sekunden für ein Hin und Her der seitlichen Wanderung.")]
        [Range(5f, 180f)] public float ringLaneShiftPeriod = 34f;
        [Tooltip("Wie weit die Versatzstellen der Spuren entlang des Rings wandern (Anteil einer Abschnittslänge). Mehr = manche Abschnitte werden zeitweise deutlich kürzer.")]
        [Range(0f, 0.4f)] public float ringRowDrift = 0.12f;
        [Tooltip("Sekunden für ein Hin und Her dieser Wanderung.")]
        [Range(10f, 300f)] public float ringRowPeriod = 70f;
        [Tooltip("Strömung (u/s) entlang einer Spur. Spuren mit verschiedener Strömung gleiten aneinander vorbei – dort surft es sich am besten.")]
        [Range(0f, 8f)] public float ringFlowSpeed = 2.2f;
        [Tooltip("Sekunden, in denen die Strömung einer Spur einmal wechselt.")]
        [Range(5f, 300f)] public float ringFlowPeriod = 45f;
        [Tooltip("Surf-Schub an einer ruhigen Grenze (beide Spuren gleich schnell), Anteil des vollen Schubs.")]
        [Range(0f, 1f)] public float ringCalmSurf = 0.45f;
        [Tooltip("Ab diesem Gleittempo (u/s) der beiden Spuren gibt eine Grenze den vollen Surf-Schub.")]
        [Range(0.2f, 8f)] public float ringSlideFull = 2.5f;

        [Header("Abenteuer-Fahrt (Surfspuren)")]
        [Tooltip("Halbe Breite einer Surfspur im Abenteuer (statt der gemütlichen Breite). Breiter = die Spur ist leichter zu treffen und zu halten.")]
        [Range(1f, 30f)] public float ringSurfWidth = 13f;
        [Tooltip("Wie viel mehr Schub eine Surfspur im Abenteuer gibt (Faktor auf das Surf-Tempo).")]
        [Range(1f, 3f)] public float ringSurfBoost = 1.7f;

        [Header("Plattenbewegung")]
        [Tooltip("Wie weit jede Platte hin und her wandert. Mehr = Grenzen verschieben sich stärker, Platten fahren tiefer ineinander.")]
        [Range(0f, 45f)] public float waveAmplitude = 14f;
        [Tooltip("Tempo dieser Pendelbewegung. Spitzengeschwindigkeit einer Platte ≈ Weite × Tempo.")]
        [Range(0f, 1f)] public float waveSpeed = 0.28f;
        [Range(0f, 4f)] public float timeScale = 1f;

        [Header("Schub durch Inseln und Ereignisse")]
        [Tooltip("Trägheit der Platten gegenüber Inseln: kleiner = die Insel schiebt ihre Platte leichter mit.")]
        [Range(100f, 5000f)] public float plateMass = 1500f;
        [Tooltip("Wie stark eine fahrende Insel ihre Platte mitschiebt.")]
        [Range(0f, 150f)] public float riderPush = 25f;
        [Tooltip("Obergrenze für das Mitschieben (Einheiten/s). Ohne sie zieht eine große Insel ihre Platte ein Vielfaches ihres eigenen Tempos hinter sich her - samt aller Inseln darauf.")]
        [Range(0f, 20f)] public float maxRiderPlateSpeed = 3f;
        [Tooltip("Wie schnell ein Schub abklingt.")]
        [Range(0.1f, 5f)] public float pushDamping = 1.5f;
        [Tooltip("Wie schnell eine verschobene Platte an ihren Platz zurückfedert. Kleiner = Verschiebungen bleiben länger.")]
        [Range(0f, 2f)] public float springBack = 0.25f;
        public float transformThreshold = 0.25f;
        public float convergenceInfluence = 14f;

        [Header("Surfen an Plattengrenzen (Spielerinsel)")]
        [Tooltip("Extra-Tempo entlang einer Grenze, wenn die Insel an ihr entlang fährt.")]
        [Range(0f, 30f)] public float surfSpeed = 6f;
        [Tooltip("Zusätzlich × die Geschwindigkeit, mit der die beiden Platten aneinander vorbeigleiten.")]
        [Range(0f, 4f)] public float surfPlateGain = 1f;
        [Tooltip("Halbe Breite des Surf-Streifens um die Grenze.")]
        [Range(1f, 40f)] public float surfWidth = 8f;
        [Tooltip("Anteil des Inselradius, der zur Streifenbreite dazukommt: große Inseln erwischen die Welle von weiter weg.")]
        [Range(0f, 1f)] public float surfRadiusShare = 0.35f;
        [Tooltip("Ab welchem Winkel zur Grenze es losgeht (|cos|). 0 = auch quer, 1 = nur exakt parallel.")]
        [Range(0f, 0.95f)] public float surfMinAlign = 0.35f;
        [Tooltip("Zug zur Grenzmitte, solange man surft - hält die Insel auf der Welle.")]
        [Range(0f, 3f)] public float surfPull = 0.5f;
        [Tooltip("Stärke an aufeinander zu- oder auseinanderlaufenden Grenzen (an Gleitgrenzen immer 1).")]
        [Range(0f, 2f)] public float surfOtherKinds = 0.75f;
        [Tooltip("Gemütlich: Faktor auf das Surf-Tempo an einer Grenze. Die Grenzen sind der Ort für richtig viel Schub – im Inneren der Platten ist die Strömung sanft (siehe unten). Das Abenteuer nutzt ringSurfBoost.")]
        [Range(1f, 3f)] public float cozySurfBoost = 1.6f;

        [Header("Strömung im Platteninneren (Gemütlich)")]
        [Tooltip("Welchen Anteil der Plattenbewegung eine Insel als Strömung spürt. Kleiner = man kommt auch gegen die Strömung gut voran. Eine ruhende Insel treibt trotzdem langsam mit ihrer Platte. Das Abenteuer spürt immer die volle Strömung seiner Spuren.")]
        [Range(0f, 1f)] public float interiorCurrentShare = 0.55f;
        [Tooltip("Obergrenze dieser Strömung als Anteil des Höchsttempos der Insel, weich erreicht. 0,4 = gegen die stärkste Strömung bleiben immer mindestens 60 % des eigenen Tempos, mit ihr kommen höchstens 40 % dazu.")]
        [Range(0.05f, 1f)] public float interiorCurrentCap = 0.4f;
        public Transform focus;
        public float viewRadius = 130f;
        public bool showBorders = true;
        public float seamWidth = 8f;
        [Range(0f, 1f)] public float seamOpacity = 0.85f;
        public float seamFadeDistance = 120f;
        public float seamFlowGain = 1.5f;
        public float seamMeander = 0.12f;
        public float seamMeanderMax = 6f;
        public float seamSegment = 6f;
        [Range(0f, 0.4f)] public float seamWidthVariation = 0.25f;
        public float borderHeight = 0.02f;
        public Material borderMaterial;
        public float borderRefreshInterval = 1f / 30f;

        public readonly List<Border> Borders = new();

        readonly Dictionary<Vector2Int, Plate> _plates = new();
        readonly Dictionary<Vector2Int, PlateCore> _cores = new();
        readonly HashSet<PlateCore> _dynamic = new();
        readonly List<Plate> _active = new();
        readonly List<PlateCore> _dynamicScratch = new();
        float _time;
        // Integral of waveSpeed over time: the plates' pendulum phase. Integrated rather than waveSpeed * _time so
        // moving the slider changes the pace instead of jumping every plate to another point of its swing.
        float _waveClock;
        float _borderTimer;
        Mesh _borderMesh;
        GameObject _borderGo;
        Material _fallbackMaterial;
        MaterialPropertyBlock _seamBlock;
        SeamVertex[] _vb;
        ushort[] _ib;
        int _vCount, _iCount;
        bool _meshLaidOut;
        readonly Vector2[] _pts = new Vector2[MaxSeamSegments + 1];
        readonly float[] _ptT = new float[MaxSeamSegments + 1];
        List<Vector2> _clipVerts = new(), _clipOut = new();
        List<int> _clipTags = new(), _clipOutTags = new();

        // Ring layout (adventure): lanes of cells _ringCellX wide across the band starting at _ringX0, _ringCellZ long,
        // periodic along z with _ringPeriodZ cells = one circumference. Runtime only, set by RingWorld.
        [System.NonSerialized] bool _ring;
        [System.NonSerialized] float _ringX0, _ringCellX, _ringCellZ, _ringHalfWidth, _ringCircumference;
        [System.NonSerialized] int _ringPeriodZ, _ringLanesUsed;
        const float RingOuterPush = 1.5f;
        const int RingSalt = 0x3C6EF372;

        public int PlateCount => _cores.Count;

        public int SeamVertexCount => _vCount;
        public int SeamIndexCount => _iCount;

        public float WorldSize => gridPeriod > 0 ? gridPeriod * cellSize : 0f;

        public bool RingLayout => _ring;
        public float RingCellLength => _ringCellZ;

        public bool RingMatches(float centerX, float halfWidth, float circumference) =>
            _ring && Mathf.Approximately(_ringX0, centerX - halfWidth) && Mathf.Approximately(_ringHalfWidth, halfWidth)
            && Mathf.Approximately(_ringCircumference, circumference) && _ringLanesUsed == Mathf.Max(1, ringLanes);

        // Plates of the adventure ring: ringLanes columns of sites across the band, in rows of ringPlateLength along
        // it, a whole number of rows per circumference. A Voronoi edge between two neighbouring columns only runs
        // along the track while the two sites share their position along it, so a row moves as one (ringRowDrift)
        // and all the life is sideways: each site swings across the band on its own (ringLaneShift), which slides the
        // lane boundaries sideways, pinches a lane shut where two boundaries meet and opens it again.
        // The plate velocity is not the movement of the site but the lane's own current along the track
        // (ringFlowSpeed): neighbouring lanes slide past each other, which is what makes a boundary a surf lane, and
        // the water carries islands and foam along it. Boundaries inside a column would cross the track, so they are
        // left out; columns outside the band are pushed further out, and every border is clipped to the band.
        public void SetRingLayout(float centerX, float halfWidth, float circumference)
        {
            _ring = true;
            _ringLanesUsed = Mathf.Max(1, ringLanes);
            _ringHalfWidth = Mathf.Max(1f, halfWidth);
            _ringCircumference = Mathf.Max(10f, circumference);
            _ringX0 = centerX - _ringHalfWidth;
            _ringCellX = 2f * _ringHalfWidth / _ringLanesUsed;
            _ringPeriodZ = Mathf.Max(3, Mathf.RoundToInt(_ringCircumference / Mathf.Max(1f, ringPlateLength)));
            _ringCellZ = _ringCircumference / _ringPeriodZ;
            RebuildLayout();
        }

        public void ClearRingLayout()
        {
            if (!_ring) return;
            _ring = false;
            RebuildLayout();
        }

        void RebuildLayout()
        {
            _plates.Clear();
            _cores.Clear();
            _dynamic.Clear();
            ResetEvents();
            ComputeBorders();
            RebuildBorderMesh();
        }

        int WrapRing(int c) => ((c % _ringPeriodZ) + _ringPeriodZ) % _ringPeriodZ;

        public PlateSaveData Capture()
        {
            var d = new PlateSaveData { time = _time, waveClock = _waveClock };
            foreach (var c in _cores.Values)
            {
                if (c.offset.sqrMagnitude < 1e-6f && c.pushVel.sqrMagnitude < 1e-6f) continue;
                d.offsets.Add(new PlateOffsetData { cx = c.id.x, cz = c.id.y, ox = c.offset.x, oy = c.offset.y, px = c.pushVel.x, py = c.pushVel.y });
            }
            CaptureEvents(d);
            return d;
        }

        public void Restore(PlateSaveData d)
        {
            if (d == null) return;
            _plates.Clear();
            _cores.Clear();
            _dynamic.Clear();
            _time = d.time;
            _waveClock = d.waveClock >= 0f ? d.waveClock : d.time * waveSpeed;
            foreach (var o in d.offsets)
            {
                // A save from a finer plate grid (4 x 76.5 before 2026-09-23) names cells this grid does not have.
                if (!_ring && gridPeriod > 0 && (o.cx < 0 || o.cz < 0 || o.cx >= gridPeriod || o.cz >= gridPeriod)) continue;
                var core = GetCore(o.cx, o.cz);
                core.offset = new Vector2(o.ox, o.oy);
                core.pushVel = new Vector2(o.px, o.py);
                _dynamic.Add(core);
            }
            RestoreEvents(d);
            ComputeBorders();
            RebuildBorderMesh();
            Restored?.Invoke();
        }

        void OnEnable()
        {
            Instance = this;
            _plates.Clear();
            _cores.Clear();
            _dynamic.Clear();
            _time = 0f;
            _waveClock = 0f;
            ResetEvents();
            ComputeBorders();
            RebuildBorderMesh();
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        void OnDestroy()
        {
            KillAsset(_borderMesh);
            KillAsset(_fallbackMaterial);
            _borderMesh = null;
            _fallbackMaterial = null;
        }

        static void KillAsset(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            Step(Time.deltaTime);
        }

        static int Hash(int a, int b, int c)
        {
            unchecked { return a * 73856093 ^ b * 19349663 ^ c * 83492791; }
        }

        int Wrap(int c) => gridPeriod > 0 ? ((c % gridPeriod) + gridPeriod) % gridPeriod : c;

        PlateCore GetCore(int wx, int wz)
        {
            var key = new Vector2Int(wx, wz);
            if (!_cores.TryGetValue(key, out var c))
            {
                if (_ring) return _cores[key] = RingCore(key);
                var rnd = new System.Random(Hash(seed, wx, wz));
                float ang = Rand(rnd, 0f, Mathf.PI * 2f);
                c = new PlateCore
                {
                    id = key,
                    home = new Vector2(wx + 0.5f + Rand(rnd, -jitter, jitter), wz + 0.5f + Rand(rnd, -jitter, jitter)) * cellSize,
                    dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)),
                    ampK = Rand(rnd, 0.6f, 1.4f),
                    omegaK = Rand(rnd, 0.7f, 1.3f),
                    phase = Rand(rnd, 0f, Mathf.PI * 2f)
                };
                _cores[key] = c;
            }
            return c;
        }

        Plate GetPlate(int cx, int cz)
        {
            var key = new Vector2Int(cx, cz);
            if (!_plates.TryGetValue(key, out var p))
            {
                int wx = _ring ? cx : Wrap(cx), wz = _ring ? WrapRing(cz) : Wrap(cz);
                p = new Plate
                {
                    core = GetCore(wx, wz),
                    cell = key,
                    shift = _ring ? new Vector2(0f, (cz - wz) * _ringCellZ) : new Vector2(cx - wx, cz - wz) * cellSize
                };
                _plates[key] = p;
            }
            Refresh(p);
            return p;
        }

        static float Rand(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        PlateCore RingCore(Vector2Int key)
        {
            int wx = key.x, wz = key.y;
            var rnd = new System.Random(Hash(seed ^ RingSalt, wx, wz));
            float jx = Rand(rnd, -jitter, jitter) * ringJitter;
            float push = wx < 0 ? -RingOuterPush : wx >= _ringLanesUsed ? RingOuterPush : 0f;
            return new PlateCore
            {
                // No jitter along the ring: a row has to keep one line, or its lane boundaries run across the track.
                id = key,
                home = new Vector2(_ringX0 + (wx + 0.5f + jx + push) * _ringCellX, (wz + 0.5f) * _ringCellZ),
                dir = Vector2.up,
                ampK = Rand(rnd, 0.6f, 1.4f),
                omegaK = Rand(rnd, 0.7f, 1.3f),
                phase = Rand(rnd, 0f, Mathf.PI * 2f)
            };
        }

        const float Tau = Mathf.PI * 2f;

        // The ring's own movement: the site swings across the band, the whole row drifts along it, and the velocity
        // is the lane current, not the site's motion.
        void RefreshRing(Plate p)
        {
            var c = p.core;
            float lateral = ringLaneShift * _ringCellX * c.ampK
                * Mathf.Sin(c.omegaK * _time * Tau / Mathf.Max(1f, ringLaneShiftPeriod) + c.phase);
            float along = ringRowDrift * _ringCellZ
                * Mathf.Sin(_time * Tau / Mathf.Max(1f, ringRowPeriod) + Hash01(seed ^ RingSalt, c.id.y, 7) * Tau);
            p.position = c.home + p.shift + new Vector2(lateral, along) + c.offset;
            float w = Tau / Mathf.Max(1f, ringFlowPeriod);
            float flow = ringFlowSpeed * (0.7f * Mathf.Sin(_time * w + Hash01(seed ^ RingSalt, c.id.x, 13) * Tau)
                + 0.3f * Mathf.Sin(c.omegaK * _time * w + c.phase));
            p.velocity = (new Vector2(0f, flow) + c.pushVel) * timeScale;
        }

        int RingColumn(float x) => Mathf.Clamp(Mathf.FloorToInt((x - _ringX0) / _ringCellX), -1, _ringLanesUsed);

        void Refresh(Plate p)
        {
            if (_ring)
            {
                RefreshRing(p);
                return;
            }
            var c = p.core;
            float angle = c.omegaK * _waveClock + c.phase;
            float s = Mathf.Sin(angle);
            float co = Mathf.Cos(angle);
            float amp = waveAmplitude * c.ampK;
            p.position = c.home + p.shift + c.dir * (amp * s) + c.offset;
            p.velocity = (c.dir * (amp * c.omegaK * waveSpeed * co) + c.pushVel) * timeScale;
        }

        public Plate NearestPlate(Vector2 pos)
        {
            int cx = _ring ? RingColumn(pos.x) : Mathf.FloorToInt(pos.x / cellSize);
            int cz = Mathf.FloorToInt(pos.y / (_ring ? _ringCellZ : cellSize));
            Plate best = null;
            float bestD = float.MaxValue;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var p = GetPlate(cx + dx, cz + dz);
                    float d = (p.position - pos).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = p; }
                }
            return best;
        }

        // Every plate NearestPlate can return for a point inside [min, max]: the cells over the rectangle plus the ring
        // of cells around it. A few dozen cell lookups instead of a NearestPlate scan over a grid of sample points.
        public void PlatesInRect(Vector2 min, Vector2 max, List<Plate> result)
        {
            result.Clear();
            float cz = _ring ? _ringCellZ : cellSize;
            int x0 = (_ring ? RingColumn(min.x) : Mathf.FloorToInt(min.x / cellSize)) - 1;
            int x1 = (_ring ? RingColumn(max.x) : Mathf.FloorToInt(max.x / cellSize)) + 1;
            int z0 = Mathf.FloorToInt(min.y / cz) - 1, z1 = Mathf.FloorToInt(max.y / cz) + 1;
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    var p = GetPlate(x, z);
                    if (!result.Contains(p)) result.Add(p);
                }
        }

        public Vector2 SampleVelocity(Vector2 pos) => NearestPlate(pos).velocity;

        // The current a floating island feels inside a plate: interiorCurrentShare of the plate's motion, eased
        // into interiorCurrentCap x the rider's own top speed, so steering against it always keeps
        // (1 - cap) of that speed. The plates themselves (seams, events, mountains, surf slide) keep their full
        // velocity; only the carry is gentle. The adventure ring's lanes carry in full.
        public Vector2 CarryVelocity(Vector2 pos, float riderTopSpeed)
        {
            Vector2 v = NearestPlate(pos).velocity;
            return _ring ? v : InteriorCurrent(v, riderTopSpeed);
        }

        public Vector2 InteriorCurrent(Vector2 plateVelocity, float riderTopSpeed)
        {
            float speed = plateVelocity.magnitude;
            float felt = speed * interiorCurrentShare;
            if (felt < 1e-5f) return Vector2.zero;
            float cap = interiorCurrentCap * Mathf.Max(0f, riderTopSpeed);
            felt = cap > 1e-4f ? cap * (1f - Mathf.Exp(-felt / cap)) : 0f;
            return plateVelocity * (felt / speed);
        }

        public void Impulse(Vector2 pos, Vector2 velocity)
        {
            var p = NearestPlate(pos);
            p.core.pushVel += velocity;
            _dynamic.Add(p.core);
        }

        // The wave a plate boundary throws: extra velocity along the seam in the direction the rider faces, strongest
        // on the seam and when driving parallel to it, plus a pull back onto the seam. drive (0..1) is how much the
        // rider is actually under way, so a parked island is not dragged along. strength = 0..1 for feedback.
        public Vector2 SurfVelocity(Vector2 pos, Vector2 heading, float radius, float drive, out float strength)
        {
            strength = 0f;
            if (drive <= 0f || (surfSpeed <= 0f && surfPlateGain <= 0f)) return Vector2.zero;
            float width = Mathf.Max(0.5f, (_ring ? ringSurfWidth : surfWidth) + surfRadiusShare * Mathf.Max(0f, radius));
            if (!NearestSeam(pos, width, out var b, out float t, out Vector2 q)) return Vector2.zero;
            Vector2 tan = SeamTangent(b, t);
            float cos = Vector2.Dot(heading, tan);
            float align = Mathf.InverseLerp(surfMinAlign, 1f, Mathf.Abs(cos));
            align = align * align * (3f - 2f * align);
            Vector2 toSeam = q - pos;
            float d = toSeam.magnitude;
            float x = d / width;
            float falloff = 1f - x * x;
            float kind = b.kind == BoundaryKind.Transform ? 1f : surfOtherKinds;
            strength = Mathf.Clamp01(align * falloff * kind * Mathf.Clamp01(drive));
            if (strength <= 0f) return Vector2.zero;
            float slide = Mathf.Abs(Vector2.Dot(b.a.velocity - b.b.velocity, tan));
            // In the ring a lane boundary only carries while the two lanes really slide past each other: where their
            // currents have come together the lanes have merged and the line is worth little.
            float push = _ring ? surfSpeed * ringSurfBoost * Mathf.Lerp(ringCalmSurf, 1f, SlideShare(slide)) : surfSpeed * cozySurfBoost;
            Vector2 along = tan * (Mathf.Sign(cos) * (push + surfPlateGain * slide));
            return (along + toSeam * surfPull) * strength;
        }

        // 0..1: how far the two lanes of a ring seam are sliding past each other (ringSlideFull = fully).
        public float SlideShare(float slide)
        {
            float x = Mathf.Clamp01(slide / Mathf.Max(0.05f, ringSlideFull));
            return x * x * (3f - 2f * x);
        }

        public float ConvergenceAt(Vector2 pos)
        {
            var self = NearestPlate(pos);
            float best = 0f;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var o = GetPlate(self.cell.x + dx, self.cell.y + dz);
                    if (o == self) continue;
                    Vector2 n = (o.position - self.position).normalized;
                    Vector2 m = (self.position + o.position) * 0.5f;
                    float d = Mathf.Abs(Vector2.Dot(pos - m, n));
                    if (d >= convergenceInfluence) continue;
                    float closing = -Vector2.Dot(o.velocity - self.velocity, n);
                    if (closing <= 0f) continue;
                    best = Mathf.Max(best, closing * (1f - d / convergenceInfluence));
                }
            return best;
        }

        public void Step(float dt)
        {
            float gdt = dt * timeScale;
            _time += gdt;
            _waveClock += waveSpeed * gdt;

            foreach (var rider in Riders)
            {
                var p = NearestPlate(rider.PlanarPosition);
                float k = rider.Mass / (rider.Mass + plateMass);
                Vector2 v = rider.SelfVelocity;
                float speed = v.magnitude;
                if (speed < 1e-3f) continue;
                Vector2 dir = v / speed;
                // Only up to maxRiderPlateSpeed along the rider's heading; event kicks are not capped here.
                float room = maxRiderPlateSpeed - Vector2.Dot(p.core.pushVel, dir);
                if (room <= 0f) continue;
                p.core.pushVel += dir * Mathf.Min(room, speed * k * riderPush * gdt);
                _dynamic.Add(p.core);
            }

            float damp = Mathf.Exp(-pushDamping * gdt);
            float spring = Mathf.Exp(-springBack * gdt);
            // In the ring a pushed plate must not leave its column, or a lane boundary jumps past its neighbour.
            float maxOffset = _ring ? _ringCellX * 0.2f : cellSize * 0.45f;
            _dynamicScratch.Clear();
            foreach (var p in _dynamic) _dynamicScratch.Add(p);
            foreach (var p in _dynamicScratch)
            {
                p.pushVel *= damp;
                p.offset += p.pushVel * gdt;
                p.offset *= spring;
                if (p.offset.magnitude > maxOffset) p.offset = p.offset.normalized * maxOffset;
                if (p.offset.sqrMagnitude < 0.0004f && p.pushVel.sqrMagnitude < 0.0004f)
                {
                    p.offset = Vector2.zero;
                    p.pushVel = Vector2.zero;
                    _dynamic.Remove(p);
                }
            }

            StepEvents(gdt);

            // Plates move a few units per second at most, so the border geometry (the only per-frame
            // clipping + mesh upload in this system) only needs refreshing at ~30 Hz.
            _borderTimer += dt;
            if (borderRefreshInterval <= 0f || _borderTimer >= borderRefreshInterval - 1e-4f)
            {
                _borderTimer = borderRefreshInterval <= 0f ? 0f : Mathf.Min(_borderTimer - borderRefreshInterval, borderRefreshInterval);
                ComputeBorders();
                RebuildBorderMesh();
            }
        }

        Vector2 FocusPoint()
        {
            if (focus != null) return new Vector2(focus.position.x, focus.position.z);
            var cam = Camera.main;
            return cam != null ? new Vector2(cam.transform.position.x, cam.transform.position.z) : Vector2.zero;
        }

        void ComputeBorders()
        {
            Borders.Clear();
            _active.Clear();
            Vector2 f = FocusPoint();
            float R = viewRadius;
            float xMin = f.x - R, xMax = f.x + R;
            int cx0, cx1, cz0, cz1;
            float near;
            if (_ring)
            {
                xMin = _ringX0;
                xMax = _ringX0 + 2f * _ringHalfWidth;
                cx0 = -1;
                cx1 = _ringLanesUsed;
                cz0 = Mathf.FloorToInt((f.y - R) / _ringCellZ) - 1;
                cz1 = Mathf.FloorToInt((f.y + R) / _ringCellZ) + 1;
                near = Mathf.Max(_ringCellX * (1f + 2f * RingOuterPush), _ringCellZ) * 2.6f;
            }
            else
            {
                cx0 = Mathf.FloorToInt((f.x - R) / cellSize) - 1;
                cx1 = Mathf.FloorToInt((f.x + R) / cellSize) + 1;
                cz0 = Mathf.FloorToInt((f.y - R) / cellSize) - 1;
                cz1 = Mathf.FloorToInt((f.y + R) / cellSize) + 1;
                near = cellSize * 2.6f;
            }
            for (int cx = cx0; cx <= cx1; cx++)
                for (int cz = cz0; cz <= cz1; cz++)
                    _active.Add(GetPlate(cx, cz));

            for (int i = 0; i < _active.Count; i++)
            {
                var verts = _clipVerts;
                var tags = _clipTags;
                verts.Clear();
                tags.Clear();
                verts.Add(new Vector2(xMin, f.y - R)); tags.Add(-1);
                verts.Add(new Vector2(xMax, f.y - R)); tags.Add(-1);
                verts.Add(new Vector2(xMax, f.y + R)); tags.Add(-1);
                verts.Add(new Vector2(xMin, f.y + R)); tags.Add(-1);
                for (int j = 0; j < _active.Count && verts.Count > 0; j++)
                {
                    if (j == i) continue;
                    if ((_active[j].position - _active[i].position).sqrMagnitude > near * near) continue;
                    Clip(verts, tags, _clipOut, _clipOutTags, _active[i].position, _active[j].position, j);
                    (verts, _clipOut) = (_clipOut, verts);
                    (tags, _clipOutTags) = (_clipOutTags, tags);
                }
                _clipVerts = verts;
                _clipTags = tags;

                int vc = verts.Count;
                for (int k = 0; k < vc; k++)
                {
                    int tag = tags[k];
                    if (tag <= i) continue;
                    Vector2 a = verts[k];
                    Vector2 b = verts[(k + 1) % vc];
                    if ((b - a).sqrMagnitude < 0.0004f) continue;

                    Plate pa = _active[i], pb = _active[tag];
                    // In the ring only the boundaries between lanes are seams; the ones inside a lane cross the track.
                    if (_ring && pa.cell.x == pb.cell.x) continue;
                    Vector2 nrm = (pb.position - pa.position).normalized;
                    float closing = -Vector2.Dot(pb.velocity - pa.velocity, nrm);
                    BoundaryKind kind = closing > transformThreshold ? BoundaryKind.Convergent
                        : closing < -transformThreshold ? BoundaryKind.Divergent
                        : BoundaryKind.Transform;
                    Borders.Add(MakeBorder(pa, pb, a, b, tags[(k + vc - 1) % vc] < 0, tags[(k + 1) % vc] < 0, kind, closing));
                }
            }
        }

        static void Clip(List<Vector2> v, List<int> tags, List<Vector2> ov, List<int> ot, Vector2 si, Vector2 sj, int j)
        {
            Vector2 m = (si + sj) * 0.5f;
            Vector2 nrm = (sj - si).normalized;
            ov.Clear();
            ot.Clear();
            int c = v.Count;
            for (int k = 0; k < c; k++)
            {
                Vector2 P = v[k];
                Vector2 Q = v[(k + 1) % c];
                int T = tags[k];
                float dp = Vector2.Dot(P - m, nrm);
                float dq = Vector2.Dot(Q - m, nrm);
                bool pin = dp <= 0f;
                bool qin = dq <= 0f;
                if (pin) { ov.Add(P); ot.Add(T); }
                if (pin != qin)
                {
                    float t = dp / (dp - dq);
                    ov.Add(P + (Q - P) * t);
                    ot.Add(pin ? j : T);
                }
            }
        }

        public static Color KindColor(BoundaryKind kind)
        {
            switch (kind)
            {
                case BoundaryKind.Convergent: return new Color(1f, 0.28f, 0.22f).linear;
                case BoundaryKind.Divergent: return new Color(0.6f, 0.95f, 1f).linear;
                default: return new Color(1f, 0.88f, 0.3f).linear;
            }
        }

        GameObject FindBorderObject()
        {
            if (_borderGo != null && _borderGo.transform.parent == transform) return _borderGo;
            _borderGo = null;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (child.name != BorderObjectName) continue;
                if (_borderGo == null) _borderGo = child;
                else KillObject(child);
            }
            return _borderGo;
        }

        Material SeamMaterial()
        {
            if (borderMaterial != null) return borderMaterial;
            if (_fallbackMaterial == null)
            {
                var shader = Shader.Find(SeamShaderName);
                if (shader != null) _fallbackMaterial = new Material(shader) { name = "PlateSeam (runtime)", hideFlags = HideFlags.DontSave };
            }
            return _fallbackMaterial;
        }

        void RebuildBorderMesh()
        {
            GameObject go = FindBorderObject();

            if (!showBorders)
            {
                if (go != null && go.activeSelf) go.SetActive(false);
                return;
            }

            if (go == null)
            {
                go = new GameObject(BorderObjectName);
                go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                var newMr = go.AddComponent<MeshRenderer>();
                newMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                newMr.receiveShadows = false;
                _borderGo = go;
            }
            if (!go.activeSelf) go.SetActive(true);

            var mr = go.GetComponent<MeshRenderer>();
            var mat = SeamMaterial();
            if (mr.sharedMaterial != mat) mr.sharedMaterial = mat;

            Vector2 f = FocusPoint();
            Vector2 fade = SeamFade();
            float fadeEnd = fade.y;
            _seamBlock ??= new MaterialPropertyBlock();
            _seamBlock.SetVector(SeamFocusId, new Vector4(f.x, f.y, fade.x, fadeEnd));
            _seamBlock.SetFloat(SeamOpacityId, seamOpacity);
            mr.SetPropertyBlock(_seamBlock);

            BuildSeamGeometry(f, fadeEnd);

            int vCap = MaxSeamVertices + MaxPulseVertices;
            if (_borderMesh == null)
            {
                _borderMesh = new Mesh { name = "PlateBorders", hideFlags = HideFlags.DontSave };
                _meshLaidOut = false;
            }
            if (!_meshLaidOut)
            {
                _borderMesh.SetVertexBufferParams(vCap, SeamLayout);
                _borderMesh.SetIndexBufferParams(_ib.Length, IndexFormat.UInt16);
                _borderMesh.subMeshCount = 1;
                _meshLaidOut = true;
            }
            // Unused vertices stay in the buffer unreferenced, so the bounds are set by hand (generous: the curved
            // world bends the far parts down and event rings outlive the window they were born in).
            float ext = viewRadius + 80f;
            var bounds = new Bounds(new Vector3(f.x, -20f, f.y), new Vector3(ext * 2f, 80f, ext * 2f));
            if (_vCount > 0) _borderMesh.SetVertexBufferData(_vb, 0, 0, _vCount, 0, SeamUpload);
            if (_iCount > 0) _borderMesh.SetIndexBufferData(_ib, 0, 0, _iCount, SeamUpload);
            _borderMesh.SetSubMesh(0, new SubMeshDescriptor(0, _iCount) { bounds = bounds, firstVertex = 0, vertexCount = Mathf.Max(_vCount, 1) }, SeamUpload);
            _borderMesh.bounds = bounds;
            var mf = go.GetComponent<MeshFilter>();
            if (mf.sharedMesh != _borderMesh) mf.sharedMesh = _borderMesh;
        }

        void BuildSeamGeometry(Vector2 f, float fadeEnd)
        {
            _vb ??= new SeamVertex[MaxSeamVertices + MaxPulseVertices];
            _ib ??= new ushort[(MaxSeamVertices / 4) * 12 + (MaxPulseVertices / 4) * 6];
            _vCount = 0;
            _iCount = 0;

            // Borders that end up entirely behind the distance fade cost nothing; the rest share the vertex budget.
            float reach = fadeEnd + seamWidth + seamMeanderMax;
            float total = 0f;
            int visible = 0;
            for (int i = 0; i < Borders.Count; i++)
            {
                var b = Borders[i];
                if (ChordDistance(b, f) > reach || !SeamDrawn(b)) continue;
                total += b.length;
                visible++;
            }
            float segLen = Mathf.Max(Mathf.Max(seamSegment, 1f), 4f * total / Mathf.Max(64f, MaxSeamVertices - 8f * visible));

            var storms = StormSystem.Instance;
            if (storms != null && storms.ActiveCount == 0) storms = null;
            for (int i = 0; i < Borders.Count; i++)
            {
                var b = Borders[i];
                if (ChordDistance(b, f) > reach || !SeamDrawn(b)) continue;
                AddSeam(b, storms, segLen);
            }

            AddEventCues();
        }

        static float ChordDistance(in Border b, Vector2 p)
        {
            Vector2 line = b.p1 - b.p0;
            float t = Mathf.Clamp01(Vector2.Dot(p - b.p0, line) / Mathf.Max(line.sqrMagnitude, 1e-6f));
            return (b.p0 + line * t - p).magnitude;
        }

        void KillObject(GameObject g)
        {
            if (Application.isPlaying) Destroy(g);
            else DestroyImmediate(g);
        }

        // One soft ribbon per boundary along the meandering seam curve (PlateSystem.Seams.cs), split along the
        // centre line so each half carries its own plate's flow: a continuous strip with miter joins, UV0.y = true
        // arc length (anchored to the plate midpoint so the foam does not slide when a junction moves). Borders only
        // meet at triple junctions, where the curve has no displacement: both ends overshoot by the half width and
        // the shader rounds them into a capsule. Everything that scrolls the foam is a closed-form function of the
        // plate positions (no accumulated state): the water of a side is displaced against the seam by
        // W = along * tangent + inward * normal (inward = half the change of the plate distance, along = half the
        // arc the pair has turned through relative to its rest direction), projected on the local seam frame at
        // every cross-section.
        void AddSeam(in Border b, StormSystem storms, float segLen)
        {
            Plate pa = b.a, pb = b.b;
            Vector2 d = pb.position - pa.position;
            float dist = d.magnitude;
            if (dist < 1e-3f || b.length < 1e-3f) return;
            Vector2 nrm = d / dist;
            float flip = Vector2.Dot(nrm, b.normal) >= 0f ? 1f : -1f;

            int seg = Mathf.Clamp(Mathf.CeilToInt(b.length / segLen), 1, MaxSeamSegments);
            if (_vCount + 4 * (seg + 1) > MaxSeamVertices) seg = (MaxSeamVertices - _vCount) / 4 - 1;
            if (seg < 1) return;

            float strength = Mathf.Clamp01((pa.velocity - pb.velocity).magnitude / 3f);
            float hw = seamWidth * 0.5f * (0.85f + 0.15f * strength);
            // A ring lane that has come to rest beside its neighbour draws a thin line: the two have merged.
            if (_ring) hw *= Mathf.Lerp(0.4f, 1f, SlideShare((pa.velocity - pb.velocity).magnitude));
            hw *= Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(b.length / (3f * hw)));
            float closing = b.closing / Mathf.Max(transformThreshold, 0.01f);

            Vector2 rest = (pb.core.home + pb.shift) - (pa.core.home + pa.shift);
            float turned = Mathf.Atan2(rest.x * nrm.y - rest.y * nrm.x, Vector2.Dot(rest, nrm));
            float restDist = rest.magnitude;
            Vector2 water = new Vector2(nrm.y, -nrm.x) * (0.5f * restDist * turned * seamFlowGain)
                + nrm * (-0.5f * (dist - restDist) * seamFlowGain);
            float seed = (b.key & 1023) * 0.37f;

            // Scalar maths on purpose: this loop is the whole cost of the 30 Hz rebuild.
            float p0x = b.p0.x, p0y = b.p0.y, chx = b.p1.x - p0x, chy = b.p1.y - p0y, nx = b.normal.x, ny = b.normal.y;
            float arcTotal = 0f, inv = 1f / seg;
            for (int j = 0; j <= seg; j++)
            {
                float t = j * inv;
                float off = SeamOffset(b, t);
                float px = p0x + chx * t + nx * off, py = p0y + chy * t + ny * off;
                if (j > 0)
                {
                    float ex = px - _pts[j - 1].x, ey = py - _pts[j - 1].y;
                    arcTotal += Mathf.Sqrt(ex * ex + ey * ey);
                }
                _pts[j].x = px;
                _pts[j].y = py;
                _ptT[j] = arcTotal;
            }

            Vector4 plateA = ChevronAnchor(pa), plateB = ChevronAnchor(pb);
            float speedA = Vector2.Dot(pa.velocity, pa.core.dir), speedB = Vector2.Dot(pb.velocity, pb.core.dir);
            float y = borderHeight;
            float e0 = b.s0, e1 = b.s0 + arcTotal;
            int start = _vCount;
            int key = b.key;
            float variation = seamWidthVariation;

            for (int j = 0; j <= seg; j++)
            {
                float cx = _pts[j].x, cy = _pts[j].y;
                float lin = j > 0 ? _ptT[j] - _ptT[j - 1] : _ptT[1];
                float lout = j < seg ? _ptT[j + 1] - _ptT[j] : lin;
                float il = 1f / Mathf.Max(lin, 1e-5f), ol = 1f / Mathf.Max(lout, 1e-5f);
                float dix = j > 0 ? (cx - _pts[j - 1].x) * il : (_pts[1].x - cx) * il;
                float diy = j > 0 ? (cy - _pts[j - 1].y) * il : (_pts[1].y - cy) * il;
                float dox = j < seg ? (_pts[j + 1].x - cx) * ol : dix;
                float doy = j < seg ? (_pts[j + 1].y - cy) * ol : diy;
                float tx = dix + dox, ty = diy + doy;
                float tl = Mathf.Sqrt(tx * tx + ty * ty);
                if (tl > 1e-4f) { tx /= tl; ty /= tl; } else { tx = dox; ty = doy; }
                float cosHalf = Mathf.Max(tx * dox + ty * doy, 0.6f);
                float miter = 1f / cosHalf;

                float along = e0 + _ptT[j];
                float hwj = hw * (1f + variation * WidthNoise(key, along * 0.06f));
                // Inside of a bend: keep the offset edge from running past the neighbouring cross-sections.
                float hwLeft = hwj, hwRight = hwj;
                if (cosHalf < 0.9987f)
                {
                    float tanHalf = Mathf.Sqrt(1f - cosHalf * cosHalf) * miter;
                    float room = 0.45f * Mathf.Min(lin, lout) / tanHalf;
                    if (room < hwj)
                    {
                        if (dix * doy - diy * dox > 0f) hwLeft = room; else hwRight = room;
                    }
                }

                float storm = storms != null ? storms.IntensityAt(_pts[j]) : 0f;
                if (j == 0) { cx -= dox * hwj; cy -= doy * hwj; along -= hwj; }
                else if (j == seg) { cx += dix * hwj; cy += diy * hwj; along += hwj; }

                // across = (-ty, tx), the left of the running direction.
                float flowAlong = water.x * tx + water.y * ty;
                float flowIn = (water.y * tx - water.x * ty) * flip;

                for (int k = 0; k < 2; k++)
                {
                    float side = k == 0 ? -1f : 1f;
                    float dirSide = side * flip;
                    float hws = dirSide > 0f ? hwLeft : hwRight;
                    float reach = dirSide * hws * miter;
                    float scroll = seed - dirSide * flowAlong;
                    float speed = k == 0 ? speedA : speedB;

                    ref var v0 = ref _vb[_vCount];
                    ref var v1 = ref _vb[_vCount + 1];
                    _vCount += 2;
                    v0.pos.x = cx; v0.pos.y = y; v0.pos.z = cy;
                    v1.pos.x = cx - ty * reach; v1.pos.y = y; v1.pos.z = cy + tx * reach;
                    v0.uv0.x = 0f; v0.uv0.y = along; v0.uv0.z = e0; v0.uv0.w = e1;
                    v1.uv0.x = side; v1.uv0.y = along; v1.uv0.z = e0; v1.uv0.w = e1;
                    v0.uv1.x = closing; v0.uv1.y = strength; v0.uv1.z = hws; v0.uv1.w = side;
                    v0.uv2.x = scroll; v0.uv2.y = flowIn; v0.uv2.z = storm; v0.uv2.w = speed;
                    v0.uv3 = k == 0 ? plateA : plateB;
                    v1.uv1 = v0.uv1; v1.uv2 = v0.uv2; v1.uv3 = v0.uv3;
                }
            }

            for (int j = 0; j < seg; j++)
            {
                int q = start + j * 4;
                for (int k = 0; k < 2; k++)
                {
                    int a0 = q + k * 2, a1 = a0 + 1, b0 = a0 + 4, b1 = a0 + 5;
                    _ib[_iCount++] = (ushort)a0; _ib[_iCount++] = (ushort)a1; _ib[_iCount++] = (ushort)b1;
                    _ib[_iCount++] = (ushort)a0; _ib[_iCount++] = (ushort)b1; _ib[_iCount++] = (ushort)b0;
                }
            }
        }

        static Vector4 ChevronAnchor(Plate p)
        {
            Vector2 axis = p.core.dir;
            Vector2 perp = new Vector2(-axis.y, axis.x);
            return new Vector4(axis.x, axis.y,
                Mathf.Repeat(Vector2.Dot(p.position, axis), ChevronPeriodU),
                Mathf.Repeat(Vector2.Dot(p.position, perp), ChevronPeriodV * 2f));
        }

        void AddPulse(Vector2 p, float size, float type, float progress, float strength)
        {
            if (_vCount + 4 > _vb.Length || _iCount + 6 > _ib.Length) return;
            int s = _vCount;
            float y = borderHeight;
            var info = new Vector4(0f, strength, size, 0f);
            for (int i = 0; i < 4; i++)
            {
                float cx = i < 2 ? -1f : 1f;
                float cy = i == 1 || i == 2 ? 1f : -1f;
                ref var v = ref _vb[_vCount++];
                v.pos = new Vector3(p.x + cx * size, y, p.y + cy * size);
                v.uv0 = new Vector4(cx, cy, type, progress);
                v.uv1 = info;
                v.uv2 = Vector4.zero;
                v.uv3 = Vector4.zero;
            }
            _ib[_iCount++] = (ushort)s; _ib[_iCount++] = (ushort)(s + 1); _ib[_iCount++] = (ushort)(s + 2);
            _ib[_iCount++] = (ushort)s; _ib[_iCount++] = (ushort)(s + 2); _ib[_iCount++] = (ushort)(s + 3);
        }
    }
}
