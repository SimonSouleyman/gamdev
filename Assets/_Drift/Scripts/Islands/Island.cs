using System;
using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using Drift.Tectonics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Drift.Islands
{
    [ExecuteAlways]
    public class Island : MonoBehaviour, IIslandSurface, IPlateRider
    {
        static readonly List<Island> Registry = new();

        public static IReadOnlyList<Island> All => Registry;

        public static event Action<float> Impact;

        public float landRadius = 3f;
        public float cellSize = 0.5f;
        public float moveSpeed = 8f;
        public float acceleration = 3.6f;
        public float dragBase = 0.45f;
        public float turnRateDegPerSec = 90f;
        public float turnResponse = 3f;
        public float agilityArea = 40f;
        public bool useKeyboardInput = true;
        public int shapeSeed = 12345;
        public float heightOffset = 0f;
        public float bobAmplitude = 0.05f;
        public float carryResponse = 2.5f;
        public float upliftDuration = 6f;

        IslandShape _shape;
        Mesh _mesh;
        Color[] _colors;
        Vector2 _pos;
        Vector2 _carry;
        Vector2 _selfVel;
        float _turnVel;
        float _upliftT = 1f;
        float _upliftDur = 6f;
        float _bobPhase;
        int _version;
        float _boundRadius;
        float _area;

        public Vector3 Normal { get; private set; } = Vector3.up;
        public Vector3 Forward { get; private set; } = Vector3.forward;

        public Vector2 PlanarVelocity => _selfVel + _carry;
        public bool IsUplifting => _upliftT < 1f;
        public float Agility => Mathf.Pow(agilityArea / (agilityArea + _area), 0.6f);
        public float MaxSpeed => moveSpeed * (0.55f + 0.45f * Agility);

        public float MaxHeight
        {
            get
            {
                float m = 0f;
                for (int j = 0; j < _shape.nz; j++)
                    for (int i = 0; i < _shape.nx; i++)
                        m = Mathf.Max(m, _shape.Height(i, j));
                return m;
            }
        }

        // IIslandSurface
        public Transform SurfaceTransform => transform;
        public Rect LocalBounds => _shape.Bounds;
        public float BoundingRadius => _boundRadius;
        public float LandArea => _area;
        public int Version => _version;
        public float SampleHeight(Vector2 localXZ) => _shape.Sample(localXZ);

        // IPlateRider
        public Vector2 PlanarPosition => _pos;
        public Vector2 SelfVelocity => _selfVel;
        public float Mass => Mathf.Max(1f, _area);

        void OnEnable()
        {
            Normal = Vector3.up;
            Vector3 f = Vector3.ProjectOnPlane(transform.forward, Normal);
            Forward = f.sqrMagnitude > 0.001f ? f.normalized : Vector3.forward;
            _pos = new Vector2(transform.position.x, transform.position.z);
            _bobPhase = (Mathf.Abs(shapeSeed) % 97) * 0.13f;
            _upliftDur = upliftDuration;

            GenerateShape();
            ApplyTransform();

            if (!Registry.Contains(this)) Registry.Add(this);
            PlateSystem.Register(this);
        }

        void OnDisable()
        {
            Registry.Remove(this);
            PlateSystem.Unregister(this);
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            Vector2 input = useKeyboardInput ? ReadKeyboardInput() : Vector2.zero;
            Tick(input, Time.deltaTime);
            AdvanceUplift(Time.deltaTime);
        }

        static Vector2 ReadKeyboardInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return Vector2.zero;
            float turn = 0f, throttle = 0f;
            if (kb.dKey.isPressed) turn += 1f;
            if (kb.aKey.isPressed) turn -= 1f;
            if (kb.wKey.isPressed) throttle += 1f;
            if (kb.sKey.isPressed) throttle -= 1f;
            return new Vector2(turn, throttle);
        }

        public void Tick(Vector2 input, float dt)
        {
            float ag = Agility;

            float targetTurn = input.x * turnRateDegPerSec * ag;
            _turnVel = Mathf.Lerp(_turnVel, targetTurn, 1f - Mathf.Exp(-turnResponse * ag * dt));
            if (Mathf.Abs(_turnVel) > 0.001f)
            {
                Quaternion turn = Quaternion.AngleAxis(_turnVel * dt, Vector3.up);
                Forward = (turn * Forward).normalized;
            }

            _selfVel += new Vector2(Forward.x, Forward.z) * (input.y * acceleration * ag * dt);
            _selfVel *= Mathf.Exp(-dragBase * ag * dt);
            float max = MaxSpeed;
            if (_selfVel.sqrMagnitude > max * max) _selfVel = _selfVel.normalized * max;

            Vector2 plateVel = PlateSystem.Instance != null ? PlateSystem.Instance.SampleVelocity(_pos) : Vector2.zero;
            _carry = Vector2.Lerp(_carry, plateVel, 1f - Mathf.Exp(-carryResponse * dt));
            _pos += (_selfVel + _carry) * dt;

            ApplyTransform();
        }

        public void SetPlanarPosition(Vector2 p)
        {
            _pos = p;
            ApplyTransform();
        }

        public void SetSelfVelocity(Vector2 v) => _selfVel = v;

        void ApplyTransform()
        {
            float bob = Application.isPlaying ? bobAmplitude * Mathf.Sin(Time.time * 1.3f + _bobPhase) : 0f;
            transform.position = new Vector3(_pos.x, heightOffset + bob, _pos.y);
            transform.rotation = Quaternion.LookRotation(Forward, Normal);
        }

        Vector2 Right2 => new Vector2(Forward.z, -Forward.x);
        Vector2 Forward2 => new Vector2(Forward.x, Forward.z);

        public Vector2 ToWorld(Vector2 local) => _pos + Right2 * local.x + Forward2 * local.y;

        public Vector2 ToLocal(Vector2 world)
        {
            Vector2 d = world - _pos;
            return new Vector2(Vector2.Dot(d, Right2), Vector2.Dot(d, Forward2));
        }

        // ---- shape / mesh ----

        [ContextMenu("Regenerate Shape")]
        public void RegenerateShape() => GenerateShape();

        void GenerateShape()
        {
            _shape = IslandShape.CreateBlob(landRadius, shapeSeed, cellSize);
            _upliftT = 1f;
            _carry = Vector2.zero;
            _colors = null;
            RecomputeStats();
            RebuildMesh();
            _version++;
            var life = GetComponent<IslandLifeSystem>();
            if (life != null) life.Repopulate();
        }

        void RecomputeStats()
        {
            int count = 0;
            float maxR = 0f;
            for (int j = 0; j < _shape.nz; j++)
                for (int i = 0; i < _shape.nx; i++)
                {
                    if (_shape.Height(i, j) <= 0f) continue;
                    count++;
                    maxR = Mathf.Max(maxR, _shape.CellPos(i, j).magnitude);
                }
            _area = count * _shape.cell * _shape.cell;
            _boundRadius = maxR + _shape.cell;
        }

        void RebuildMesh()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            if (GetComponent<MeshRenderer>() == null) gameObject.AddComponent<MeshRenderer>();
            if (_mesh == null) _mesh = new Mesh { name = "IslandTerrain", hideFlags = HideFlags.DontSave };
            _shape.FillMesh(_mesh);
            if (_colors == null || _colors.Length != _shape.nx * _shape.nz)
            {
                _colors = new Color[_shape.nx * _shape.nz];
                for (int i = 0; i < _colors.Length; i++) _colors[i] = Color.white;
            }
            _mesh.SetColors(_colors);
            mf.sharedMesh = _mesh;
        }

        public void ApplyGroundTint(Func<Vector2, Color> tintAtLocal)
        {
            if (_mesh == null || _shape == null) return;
            int n = _shape.nx * _shape.nz;
            if (_colors == null || _colors.Length != n) _colors = new Color[n];
            for (int j = 0; j < _shape.nz; j++)
                for (int i = 0; i < _shape.nx; i++)
                    _colors[j * _shape.nx + i] = tintAtLocal(_shape.CellPos(i, j));
            _mesh.SetColors(_colors);
        }

        public void AdvanceUplift(float dt)
        {
            if (_upliftT >= 1f) return;
            _upliftT = _upliftDur <= 0f ? 1f : Mathf.Min(1f, _upliftT + dt / _upliftDur);
            _shape.upliftWeight = IslandShape.Smooth(0f, 1f, _upliftT);
            if (_upliftT >= 1f)
            {
                _shape.Bake();
                RecomputeStats();
                _version++;
            }
            RebuildMesh();
        }

        public void FinishUplift() => AdvanceUplift(_upliftDur + 1f);

        public IslandSaveData Capture()
        {
            var heights = new float[_shape.nx * _shape.nz];
            for (int j = 0; j < _shape.nz; j++)
                for (int i = 0; i < _shape.nx; i++)
                {
                    int k = j * _shape.nx + i;
                    heights[k] = _shape.h[k] + (_shape.uplift != null ? _shape.uplift[k] : 0f);
                }
            return new IslandSaveData
            {
                posX = _pos.x, posZ = _pos.y,
                yaw = Mathf.Atan2(Forward.x, Forward.z) * Mathf.Rad2Deg,
                velX = _selfVel.x, velZ = _selfVel.y,
                landRadius = landRadius, cell = _shape.cell, shapeSeed = shapeSeed,
                nx = _shape.nx, nz = _shape.nz,
                originX = _shape.origin.x, originZ = _shape.origin.y,
                heights = heights
            };
        }

        public void Restore(IslandSaveData d)
        {
            landRadius = d.landRadius;
            shapeSeed = d.shapeSeed;
            _pos = new Vector2(d.posX, d.posZ);
            Forward = Quaternion.Euler(0f, d.yaw, 0f) * Vector3.forward;
            _selfVel = new Vector2(d.velX, d.velZ);
            _turnVel = 0f;
            _carry = Vector2.zero;

            _shape = new IslandShape(d.cell, d.nx, d.nz, new Vector2(d.originX, d.originZ));
            Array.Copy(d.heights, _shape.h, d.heights.Length);
            _upliftT = 1f;
            _colors = null;
            RecomputeStats();
            RebuildMesh();
            ApplyTransform();
            _version++;
        }

        // ---- collisions ----

        public static bool Near(Island a, Island b) =>
            Vector2.Distance(a._pos, b._pos) < a._boundRadius + b._boundRadius;

        public bool DetectContact(Island other, out Vector2 contactLocal, out int cells)
        {
            contactLocal = Vector2.zero;
            cells = 0;
            var b = other._shape;
            Vector2 sum = Vector2.zero;
            for (int j = 0; j < b.nz; j++)
                for (int i = 0; i < b.nx; i++)
                {
                    if (b.Height(i, j) <= 0.05f) continue;
                    Vector2 pa = ToLocal(other.ToWorld(b.CellPos(i, j)));
                    if (_shape.Sample(pa) <= -0.3f) continue;
                    cells++;
                    sum += pa;
                }
            if (cells > 0) contactLocal = sum / cells;
            return cells > 0;
        }

        public void MergeFrom(Island other, float closingSpeed, float convergence)
        {
            _shape.Bake();
            other._shape.Bake();
            var a = _shape;
            var b = other._shape;

            DetectContact(other, out Vector2 contact, out int contactCells);
            Vector2 otherCenter = ToLocal(other._pos);
            Vector2 n = otherCenter.sqrMagnitude > 1e-4f ? otherCenter.normalized : Vector2.right;
            if (contactCells == 0) contact = otherCenter * 0.5f;
            Vector2 tang = new Vector2(-n.y, n.x);
            float minR = Mathf.Min(_boundRadius, other._boundRadius);

            float mh = Mathf.Max(1f, _area), mg = Mathf.Max(1f, other._area);
            float mu = mh * mg / (mh + mg);
            float vc = Mathf.Max(0.5f, closingSpeed);
            float energy = 0.5f * mu * vc * vc;
            float H = Mathf.Clamp(0.22f * Mathf.Sqrt(energy) * (1f + 0.25f * convergence), 0.6f, 1.0f + 0.35f * minR);
            _upliftDur = upliftDuration + H;
            Vector2 momentum = (_selfVel * mh + other._selfVel * mg) / (mh + mg);

            Rect ra = a.LandBounds(0.15f);
            Vector2 min = ra.min, max = ra.max;
            for (int j = 0; j < b.nz; j++)
                for (int i = 0; i < b.nx; i++)
                {
                    if (b.h[j * b.nx + i] <= IslandShape.Sea + 0.15f) continue;
                    Vector2 pa = ToLocal(other.ToWorld(b.CellPos(i, j)));
                    min = Vector2.Min(min, pa);
                    max = Vector2.Max(max, pa);
                }
            float margin = 3f * a.cell;
            min -= Vector2.one * margin;
            max += Vector2.one * margin;

            var origin = new Vector2(
                a.origin.x + Mathf.Floor((min.x - a.origin.x) / a.cell) * a.cell,
                a.origin.y + Mathf.Floor((min.y - a.origin.y) / a.cell) * a.cell);
            int nx = Mathf.CeilToInt((max.x - origin.x) / a.cell) + 1;
            int nz = Mathf.CeilToInt((max.y - origin.y) / a.cell) + 1;
            var merged = new IslandShape(a.cell, nx, nz, origin);
            var up = new float[nx * nz];

            float w = 0.55f * minR + 0.8f;
            float L = 1.2f * minR + 1.2f;
            Vector2 noiseOff = new Vector2(shapeSeed * 0.37f, other.shapeSeed * 0.11f);

            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i;
                    Vector2 p = merged.CellPos(i, j);
                    float hA = a.Sample(p);
                    float hB = b.Sample(other.ToLocal(ToWorld(p)));
                    float hh = Mathf.Max(hA, hB);
                    if (hA > 0.1f && hB > 0.1f) hh += 0.35f * Mathf.Min(hA, hB);
                    if (hA > -0.6f && hB > -0.6f) hh += 0.3f * Mathf.Max(0f, 0.7f - Mathf.Abs(hA - hB));

                    Vector2 rel = p - contact;
                    float s = Vector2.Dot(rel, n);
                    float u = Vector2.Dot(rel, tang);

                    float bs = s / (0.9f * minR + 0.6f), bu = u / (1.1f * minR + 1f);
                    float bridge = Mathf.Exp(-bs * bs) * Mathf.Exp(-bu * bu);
                    hh = Mathf.Max(hh, Mathf.Lerp(IslandShape.Sea, 0.65f, IslandShape.Smooth(0.2f, 0.75f, bridge)));
                    merged.h[k] = hh;
                    float g = Mathf.Exp(-(s / w) * (s / w)) * Mathf.Exp(-(u / L) * (u / L));
                    float sf = s / (2.2f * w), uf = u / (1.5f * L);
                    float flank = 0.22f * Mathf.Exp(-sf * sf) * Mathf.Exp(-uf * uf);
                    float ridged = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(noiseOff.x + p.x * 0.8f, noiseOff.y + p.y * 0.8f) - 1f);
                    float landMask = IslandShape.Smooth(-0.2f, 0.3f, hh);
                    up[k] = H * (g * (0.6f + 0.5f * ridged) + flank) * landMask;
                }

            merged.uplift = up;
            merged.upliftWeight = 0f;
            _shape = merged;
            _upliftT = _upliftDur <= 0f ? 1f : 0f;

            Vector2 c = _shape.LandCentroid();
            _pos = ToWorld(c);
            _shape.origin -= c;
            ApplyTransform();

            _selfVel = momentum;
            _colors = null;

            var life = GetComponent<IslandLifeSystem>();
            if (life != null)
            {
                life.ShiftLocal(-c);
                life.AbsorbFrom(other.GetComponent<IslandLifeSystem>());
            }

            var plates = PlateSystem.Instance;
            if (plates != null) plates.Impulse(_pos, momentum * 0.8f + n * (vc * 0.4f));
            Impact?.Invoke(Mathf.Clamp01(energy / 120f));

            other.enabled = false;
            other.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(other.gameObject);
            else DestroyImmediate(other.gameObject);

            if (_upliftT >= 1f) _shape.Bake();
            RecomputeStats();
            RebuildMesh();
            _version++;
        }
    }
}
