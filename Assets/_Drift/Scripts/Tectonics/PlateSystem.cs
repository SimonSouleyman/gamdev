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
            public float amp;
            public float omega;
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

        public static void Register(IPlateRider rider)
        {
            if (!Riders.Contains(rider)) Riders.Add(rider);
        }

        public static void Unregister(IPlateRider rider) => Riders.Remove(rider);

        public float cellSize = 110f;
        public int gridPeriod = 6;
        public float jitter = 0.25f;
        public int seed = 4242;
        public float waveAmplitude = 14f;
        public float waveSpeed = 0.28f;
        public float timeScale = 1f;
        public float plateMass = 1500f;
        public float riderPush = 25f;
        public float pushDamping = 1.5f;
        public float springBack = 0.25f;
        public float transformThreshold = 0.25f;
        public float convergenceInfluence = 14f;
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

        public int PlateCount => _cores.Count;

        public int SeamVertexCount => _vCount;
        public int SeamIndexCount => _iCount;

        public float WorldSize => gridPeriod > 0 ? gridPeriod * cellSize : 0f;

        public PlateSaveData Capture()
        {
            var d = new PlateSaveData { time = _time };
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
            foreach (var o in d.offsets)
            {
                var core = GetCore(o.cx, o.cz);
                core.offset = new Vector2(o.ox, o.oy);
                core.pushVel = new Vector2(o.px, o.py);
                _dynamic.Add(core);
            }
            RestoreEvents(d);
            ComputeBorders();
            RebuildBorderMesh();
        }

        void OnEnable()
        {
            Instance = this;
            _plates.Clear();
            _cores.Clear();
            _dynamic.Clear();
            _time = 0f;
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
                var rnd = new System.Random(Hash(seed, wx, wz));
                float ang = Rand(rnd, 0f, Mathf.PI * 2f);
                c = new PlateCore
                {
                    id = key,
                    home = new Vector2(wx + 0.5f + Rand(rnd, -jitter, jitter), wz + 0.5f + Rand(rnd, -jitter, jitter)) * cellSize,
                    dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)),
                    amp = waveAmplitude * Rand(rnd, 0.6f, 1.4f),
                    omega = waveSpeed * Rand(rnd, 0.7f, 1.3f),
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
                int wx = Wrap(cx), wz = Wrap(cz);
                p = new Plate
                {
                    core = GetCore(wx, wz),
                    cell = key,
                    shift = new Vector2(cx - wx, cz - wz) * cellSize
                };
                _plates[key] = p;
            }
            Refresh(p);
            return p;
        }

        static float Rand(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        void Refresh(Plate p)
        {
            var c = p.core;
            float s = Mathf.Sin(c.omega * _time + c.phase);
            float co = Mathf.Cos(c.omega * _time + c.phase);
            p.position = c.home + p.shift + c.dir * (c.amp * s) + c.offset;
            p.velocity = (c.dir * (c.amp * c.omega * co) + c.pushVel) * timeScale;
        }

        public Plate NearestPlate(Vector2 pos)
        {
            int cx = Mathf.FloorToInt(pos.x / cellSize);
            int cz = Mathf.FloorToInt(pos.y / cellSize);
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

        public Vector2 SampleVelocity(Vector2 pos) => NearestPlate(pos).velocity;

        public void Impulse(Vector2 pos, Vector2 velocity)
        {
            var p = NearestPlate(pos);
            p.core.pushVel += velocity;
            _dynamic.Add(p.core);
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

            foreach (var rider in Riders)
            {
                var p = NearestPlate(rider.PlanarPosition);
                float k = rider.Mass / (rider.Mass + plateMass);
                p.core.pushVel += rider.SelfVelocity * (k * riderPush * gdt);
                _dynamic.Add(p.core);
            }

            float damp = Mathf.Exp(-pushDamping * gdt);
            float spring = Mathf.Exp(-springBack * gdt);
            float maxOffset = cellSize * 0.45f;
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
            int cx0 = Mathf.FloorToInt((f.x - R) / cellSize) - 1;
            int cx1 = Mathf.FloorToInt((f.x + R) / cellSize) + 1;
            int cz0 = Mathf.FloorToInt((f.y - R) / cellSize) - 1;
            int cz1 = Mathf.FloorToInt((f.y + R) / cellSize) + 1;
            for (int cx = cx0; cx <= cx1; cx++)
                for (int cz = cz0; cz <= cz1; cz++)
                    _active.Add(GetPlate(cx, cz));

            float near = cellSize * 2.6f;
            for (int i = 0; i < _active.Count; i++)
            {
                var verts = _clipVerts;
                var tags = _clipTags;
                verts.Clear();
                tags.Clear();
                verts.Add(f + new Vector2(-R, -R)); tags.Add(-1);
                verts.Add(f + new Vector2(R, -R)); tags.Add(-1);
                verts.Add(f + new Vector2(R, R)); tags.Add(-1);
                verts.Add(f + new Vector2(-R, R)); tags.Add(-1);
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
            float fadeEnd = Mathf.Max(1f, Mathf.Min(seamFadeDistance, viewRadius - seamWidth * 0.5f));
            _seamBlock ??= new MaterialPropertyBlock();
            _seamBlock.SetVector(SeamFocusId, new Vector4(f.x, f.y, fadeEnd * 0.65f, fadeEnd));
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
                if (ChordDistance(b, f) > reach) continue;
                total += b.length;
                visible++;
            }
            float segLen = Mathf.Max(Mathf.Max(seamSegment, 1f), 4f * total / Mathf.Max(64f, MaxSeamVertices - 8f * visible));

            var storms = StormSystem.Instance;
            if (storms != null && storms.ActiveCount == 0) storms = null;
            for (int i = 0; i < Borders.Count; i++)
            {
                var b = Borders[i];
                if (ChordDistance(b, f) > reach) continue;
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
