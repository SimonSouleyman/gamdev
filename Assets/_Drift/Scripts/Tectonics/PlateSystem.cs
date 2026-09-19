using System.Collections.Generic;
using UnityEngine;

namespace Drift.Tectonics
{
    public enum BoundaryKind { Convergent, Divergent, Transform }

    [System.Serializable]
    public class PlateSaveData
    {
        public float time;
        public List<PlateOffsetData> offsets = new();
    }

    [System.Serializable]
    public struct PlateOffsetData
    {
        public int cx, cz;
        public float ox, oy, px, py;
    }

    [ExecuteAlways]
    public class PlateSystem : MonoBehaviour
    {
        public class Plate
        {
            public Vector2Int cell;
            public Vector2 home;
            public Vector2 dir;
            public float amp;
            public float omega;
            public float phase;
            public Vector2 offset;
            public Vector2 pushVel;
            public Vector2 position;
            public Vector2 velocity;
        }

        public struct Border
        {
            public Plate a, b;
            public Vector2 p0, p1;
            public BoundaryKind kind;
            public float closing;
        }

        const string BorderObjectName = "PlateBorders";

        static readonly List<IPlateRider> Riders = new();

        public static PlateSystem Instance { get; private set; }

        public static void Register(IPlateRider rider)
        {
            if (!Riders.Contains(rider)) Riders.Add(rider);
        }

        public static void Unregister(IPlateRider rider) => Riders.Remove(rider);

        public float cellSize = 90f;
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
        public float borderWidth = 0.6f;
        public float borderHeight = 0.05f;
        public Material borderMaterial;

        public readonly List<Border> Borders = new();

        readonly Dictionary<Vector2Int, Plate> _plates = new();
        readonly HashSet<Plate> _dynamic = new();
        readonly List<Plate> _active = new();
        readonly List<Plate> _dynamicScratch = new();
        float _time;
        Mesh _borderMesh;
        readonly List<Vector3> _verts = new();
        readonly List<Color> _cols = new();
        readonly List<int> _tris = new();

        public int PlateCount => _plates.Count;

        public PlateSaveData Capture()
        {
            var d = new PlateSaveData { time = _time };
            foreach (var p in _plates.Values)
            {
                if (p.offset.sqrMagnitude < 1e-6f && p.pushVel.sqrMagnitude < 1e-6f) continue;
                d.offsets.Add(new PlateOffsetData { cx = p.cell.x, cz = p.cell.y, ox = p.offset.x, oy = p.offset.y, px = p.pushVel.x, py = p.pushVel.y });
            }
            return d;
        }

        public void Restore(PlateSaveData d)
        {
            if (d == null) return;
            _plates.Clear();
            _dynamic.Clear();
            _time = d.time;
            foreach (var o in d.offsets)
            {
                var p = GetPlate(o.cx, o.cz);
                p.offset = new Vector2(o.ox, o.oy);
                p.pushVel = new Vector2(o.px, o.py);
                _dynamic.Add(p);
            }
            ComputeBorders();
            RebuildBorderMesh();
        }

        void OnEnable()
        {
            Instance = this;
            _plates.Clear();
            _dynamic.Clear();
            _time = 0f;
            ComputeBorders();
            RebuildBorderMesh();
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
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

        Plate GetPlate(int cx, int cz)
        {
            var key = new Vector2Int(cx, cz);
            if (!_plates.TryGetValue(key, out var p))
            {
                var rnd = new System.Random(Hash(seed, cx, cz));
                float ang = Rand(rnd, 0f, Mathf.PI * 2f);
                p = new Plate
                {
                    cell = key,
                    home = new Vector2(cx + 0.5f + Rand(rnd, -jitter, jitter), cz + 0.5f + Rand(rnd, -jitter, jitter)) * cellSize,
                    dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)),
                    amp = waveAmplitude * Rand(rnd, 0.6f, 1.4f),
                    omega = waveSpeed * Rand(rnd, 0.7f, 1.3f),
                    phase = Rand(rnd, 0f, Mathf.PI * 2f)
                };
                _plates[key] = p;
            }
            Refresh(p);
            return p;
        }

        static float Rand(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        void Refresh(Plate p)
        {
            float s = Mathf.Sin(p.omega * _time + p.phase);
            float c = Mathf.Cos(p.omega * _time + p.phase);
            p.position = p.home + p.dir * (p.amp * s) + p.offset;
            p.velocity = (p.dir * (p.amp * p.omega * c) + p.pushVel) * timeScale;
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
            p.pushVel += velocity;
            _dynamic.Add(p);
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
                p.pushVel += rider.SelfVelocity * (k * riderPush * gdt);
                _dynamic.Add(p);
            }

            float damp = Mathf.Exp(-pushDamping * gdt);
            float spring = Mathf.Exp(-springBack * gdt);
            float maxOffset = cellSize * 0.45f;
            _dynamicScratch.Clear();
            _dynamicScratch.AddRange(_dynamic);
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

            ComputeBorders();
            RebuildBorderMesh();
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

            var verts = new List<Vector2>();
            var tags = new List<int>();
            float near = cellSize * 2.6f;
            for (int i = 0; i < _active.Count; i++)
            {
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
                    Clip(verts, tags, _active[i].position, _active[j].position, j);
                }

                for (int k = 0; k < verts.Count; k++)
                {
                    int tag = tags[k];
                    if (tag <= i) continue;
                    Vector2 a = verts[k];
                    Vector2 b = verts[(k + 1) % verts.Count];
                    if ((b - a).sqrMagnitude < 0.0004f) continue;

                    Plate pa = _active[i], pb = _active[tag];
                    Vector2 nrm = (pb.position - pa.position).normalized;
                    float closing = -Vector2.Dot(pb.velocity - pa.velocity, nrm);
                    BoundaryKind kind = closing > transformThreshold ? BoundaryKind.Convergent
                        : closing < -transformThreshold ? BoundaryKind.Divergent
                        : BoundaryKind.Transform;
                    Borders.Add(new Border { a = pa, b = pb, p0 = a, p1 = b, kind = kind, closing = closing });
                }
            }
        }

        static void Clip(List<Vector2> v, List<int> tags, Vector2 si, Vector2 sj, int j)
        {
            Vector2 m = (si + sj) * 0.5f;
            Vector2 nrm = (sj - si).normalized;
            var ov = new List<Vector2>();
            var ot = new List<int>();
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
            v.Clear(); v.AddRange(ov);
            tags.Clear(); tags.AddRange(ot);
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

        void RebuildBorderMesh()
        {
            GameObject go = null;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (child.name != BorderObjectName) continue;
                if (go == null) go = child;
                else KillObject(child);
            }

            if (!showBorders)
            {
                if (go != null) go.SetActive(false);
                return;
            }

            if (go == null)
            {
                go = new GameObject(BorderObjectName);
                go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
            }
            go.SetActive(true);

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = borderMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            _verts.Clear(); _cols.Clear(); _tris.Clear();
            foreach (var b in Borders)
            {
                Color col = KindColor(b.kind);
                float w = borderWidth * (0.85f + Mathf.Min(Mathf.Abs(b.closing), 2f) * 0.25f);
                AddLine(b.p0, b.p1, w, col);

                Vector2 line = b.p1 - b.p0;
                float len = line.magnitude;
                Vector2 dirLine = line / len;
                Vector2 nrm = (b.b.position - b.a.position).normalized;
                int count = Mathf.Max(1, Mathf.FloorToInt(len / 28f) + 1);
                for (int k = 0; k < count; k++)
                {
                    Vector2 mid = b.p0 + dirLine * (len * (k + 0.5f) / count);
                    AddArrow(mid - nrm * 2.8f, b.a.velocity, col);
                    AddArrow(mid + nrm * 2.8f, b.b.velocity, col);
                }
            }

            if (_borderMesh == null) _borderMesh = new Mesh { name = "PlateBorders", hideFlags = HideFlags.DontSave };
            _borderMesh.Clear();
            if (_verts.Count > 65000) _borderMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _borderMesh.SetVertices(_verts);
            _borderMesh.SetColors(_cols);
            _borderMesh.SetTriangles(_tris, 0);
            var normals = new Vector3[_verts.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            _borderMesh.SetNormals(normals);
            _borderMesh.RecalculateBounds();
            go.GetComponent<MeshFilter>().sharedMesh = _borderMesh;
        }

        void KillObject(GameObject g)
        {
            if (Application.isPlaying) Destroy(g);
            else DestroyImmediate(g);
        }

        void AddQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col)
        {
            int s = _verts.Count;
            float y = borderHeight;
            _verts.Add(new Vector3(a.x, y, a.y)); _verts.Add(new Vector3(b.x, y, b.y));
            _verts.Add(new Vector3(c.x, y, c.y)); _verts.Add(new Vector3(d.x, y, d.y));
            for (int i = 0; i < 4; i++) _cols.Add(col);
            _tris.Add(s); _tris.Add(s + 1); _tris.Add(s + 2);
            _tris.Add(s); _tris.Add(s + 2); _tris.Add(s + 3);
        }

        void AddLine(Vector2 p0, Vector2 p1, float width, Color col)
        {
            Vector2 d = (p1 - p0).normalized;
            Vector2 n = new Vector2(-d.y, d.x) * (width * 0.5f);
            AddQuad(p0 - n, p0 + n, p1 + n, p1 - n, col);
        }

        void AddArrow(Vector2 origin, Vector2 velocity, Color col)
        {
            float speed = velocity.magnitude;
            if (speed < 0.05f) return;
            Vector2 d = velocity / speed;
            float len = Mathf.Clamp(speed * 1.8f, 1.6f, 4.6f);
            const float head = 0.9f;
            Vector2 n = new Vector2(-d.y, d.x);
            Vector2 baseC = origin + d * (len - head);
            AddQuad(origin - n * 0.13f, origin + n * 0.13f, baseC + n * 0.13f, baseC - n * 0.13f, col);
            Vector2 tip = origin + d * len;
            AddQuad(baseC - n * 0.55f, baseC + n * 0.55f, tip, tip, col);
        }
    }
}
