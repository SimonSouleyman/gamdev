using System;
using System.Collections.Generic;
using Drift.Core;
using UnityEngine;

namespace Drift.Life
{
    [Serializable]
    public class LifeSaveData
    {
        public float originX, originZ, noiseX, noiseZ, cellSize;
        public int nx, nz;
        public float[] stage, burn, fireT;
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class IslandLifeSystem : MonoBehaviour
    {
        const string VegObjectName = "Vegetation";
        const int K = 4;

        public float timeScale = 6f;
        public float tickInterval = 0.2f;
        public float cellSize = 1.4f;
        public float growthRate = 0.008f;
        public float fireRate = 0.00015f;
        public float lightningRate = 0.004f;
        public float fireDuration = 20f;
        public float simDistance = 110f;
        public float hideDistance = 260f;
        public float meshInterval = 0.15f;
        public int maxPlants = 3000;
        public int seed = 1;

        class Plant
        {
            public LifeKind kind;
            public int variant;
            public Vector2 pos;
            public float yaw;
            public float jitter;
            public float scaleTarget;
            public float scale;
            public float growth;
            public bool dying;
            public float fade = 1f;
            public float burnT;
            public int cell = -1;
        }

        struct Import
        {
            public Vector2 pos;
            public float stage, burn, fireT;
        }

        static readonly List<Vector3> V = new();
        static readonly List<Vector3> N = new();
        static readonly List<Color> C = new();
        static readonly List<int> T = new();
        static readonly float[] SwayFactor = { 1f, 1f, 0.5f, 0.35f };

        readonly List<Plant> _plants = new();
        readonly List<Import> _pending = new();
        IIslandSurface _surface;
        System.Random _rnd;
        Vector2 _noiseOff;

        int _nx, _nz;
        Vector2 _origin;
        float[] _stage, _burn, _fireT, _fert, _maxStage;
        bool[] _land;
        int[] _counts, _quota;
        int _landCells;
        bool _hasGrid;
        int _gridVersion = -1;

        GameObject _vegGo;
        MeshFilter _vegFilter;
        MeshRenderer _vegRenderer;
        Mesh _vegMesh;
        Func<Vector2, Color> _tintFunc;

        float _tickTimer, _meshTimer;
        bool _dirty, _tintDirty;

        public int CreatureCount => _plants.Count;
        public int PlantCount => _plants.Count;

        public int CountOf(LifeKind kind)
        {
            int n = 0;
            foreach (var p in _plants) if (p.kind == kind && !p.dying) n++;
            return n;
        }

        public void GetStats(out float meanStage, out int burning, out int landCells)
        {
            float sum = 0f;
            burning = 0;
            landCells = 0;
            for (int i = 0; i < _land.Length; i++)
            {
                if (!_land[i]) continue;
                landCells++;
                sum += _stage[i];
                if (_fireT[i] > 0f) burning++;
            }
            meanStage = landCells > 0 ? sum / landCells : 0f;
        }

        public void GetStageCounts(out int bare, out int plains, out int shrub, out int forest)
        {
            bare = plains = shrub = forest = 0;
            for (int i = 0; i < _land.Length; i++)
            {
                if (!_land[i]) continue;
                float s = _stage[i];
                if (s < 0.15f) bare++;
                else if (s < 0.4f) plains++;
                else if (s < 0.75f) shrub++;
                else forest++;
            }
        }

        void OnEnable()
        {
            _surface = GetComponent<IIslandSurface>();
            _tintFunc = TintAt;
            if (_surface != null && _surface.LandArea > 0f) Repopulate();
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            Step(Time.deltaTime);
        }

        float Rand() => (float)_rnd.NextDouble();
        float Rand(float a, float b) => a + Rand() * (b - a);

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        // ---------------------------------------------------------------- setup

        public void Repopulate()
        {
            if (_surface == null) _surface = GetComponent<IIslandSurface>();
            if (_surface == null) return;
            _tintFunc ??= TintAt;
            _rnd = new System.Random(seed + gameObject.name.GetHashCode());
            _noiseOff = new Vector2(Rand() * 100f, Rand() * 100f);

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (child.GetComponent<CreatureMarker>() == null) continue;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }

            _plants.Clear();
            _pending.Clear();
            _hasGrid = false;
            RebuildGrid();
            SyncPlants(true);
            RebuildVegetationMesh();
            _surface.ApplyGroundTint(_tintFunc);
            _dirty = false;
            _tintDirty = false;
        }

        int CellIndex(Vector2 p)
        {
            int i = Mathf.FloorToInt((p.x - _origin.x) / cellSize);
            int j = Mathf.FloorToInt((p.y - _origin.y) / cellSize);
            if (i < 0 || j < 0 || i >= _nx || j >= _nz) return -1;
            return j * _nx + i;
        }

        Vector2 CellCenter(int i, int j) => _origin + new Vector2((i + 0.5f) * cellSize, (j + 0.5f) * cellSize);

        void CellProps(int idx, float h)
        {
            _land[idx] = h > 0.12f;
            if (!_land[idx]) { _fert[idx] = 0f; _maxStage[idx] = 0f; return; }
            if (h < 0.35f) { _fert[idx] = 0.35f; _maxStage[idx] = 0.4f; }
            else if (h < 1.9f) { _fert[idx] = 1f; _maxStage[idx] = 1.3f; }
            else if (h < 2.8f) { _fert[idx] = Mathf.Lerp(0.6f, 0.15f, (h - 1.9f) / 0.9f); _maxStage[idx] = 0.6f; }
            else { _fert[idx] = 0.05f; _maxStage[idx] = 0.15f; }
        }

        float InitStage(Vector2 p) =>
            Mathf.Clamp01(0.08f + 0.95f * Mathf.PerlinNoise(_noiseOff.x + p.x * 0.22f, _noiseOff.y + p.y * 0.22f));

        void RebuildGrid()
        {
            Rect b = _surface.LocalBounds;
            Vector2 newOrigin = new Vector2(Mathf.Floor(b.xMin / cellSize) * cellSize, Mathf.Floor(b.yMin / cellSize) * cellSize);
            int nx = Mathf.CeilToInt((b.xMax - newOrigin.x) / cellSize) + 1;
            int nz = Mathf.CeilToInt((b.yMax - newOrigin.y) / cellSize) + 1;

            var oStage = _stage; var oBurn = _burn; var oFire = _fireT; var oLand = _land;
            int onx = _nx, onz = _nz; Vector2 oOrigin = _origin;
            bool hadGrid = _hasGrid;

            _origin = newOrigin; _nx = nx; _nz = nz;
            int n = nx * nz;
            _stage = new float[n]; _burn = new float[n]; _fireT = new float[n];
            _fert = new float[n]; _maxStage = new float[n]; _land = new bool[n];
            _counts = new int[n * K]; _quota = new int[n * K];
            _landCells = 0;

            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int idx = j * nx + i;
                    Vector2 p = CellCenter(i, j);
                    CellProps(idx, _surface.SampleHeight(p));
                    if (!_land[idx]) continue;
                    _landCells++;

                    int oi = -1;
                    if (hadGrid)
                    {
                        int ci = Mathf.FloorToInt((p.x - oOrigin.x) / cellSize);
                        int cj = Mathf.FloorToInt((p.y - oOrigin.y) / cellSize);
                        if (ci >= 0 && cj >= 0 && ci < onx && cj < onz && oLand[cj * onx + ci]) oi = cj * onx + ci;
                    }
                    if (oi >= 0)
                    {
                        _stage[idx] = oStage[oi]; _burn[idx] = oBurn[oi]; _fireT[idx] = oFire[oi];
                    }
                    else
                    {
                        _stage[idx] = InitStage(p);
                    }
                    _stage[idx] = Mathf.Min(_stage[idx], _maxStage[idx]);
                }

            foreach (var imp in _pending)
            {
                int idx = CellIndex(imp.pos);
                if (idx < 0 || !_land[idx]) continue;
                _stage[idx] = Mathf.Min(imp.stage, _maxStage[idx]);
                _burn[idx] = imp.burn;
                _fireT[idx] = imp.fireT;
            }
            _pending.Clear();

            for (int k = _plants.Count - 1; k >= 0; k--)
            {
                var p = _plants[k];
                p.cell = CellIndex(p.pos);
                if (p.cell < 0 || !_land[p.cell]) { p.dying = true; p.cell = -1; continue; }
                if (!p.dying) _counts[p.cell * K + (int)p.kind]++;
            }

            _hasGrid = true;
            _gridVersion = _surface.Version;
            _dirty = true;
            _tintDirty = true;
        }

        // ------------------------------------------------------------ simulation

        float DistanceToFocus()
        {
            var cam = Camera.main;
            if (cam == null) return 0f;
            Vector3 a = cam.transform.position, b = transform.position;
            return new Vector2(a.x - b.x, a.z - b.z).magnitude;
        }

        public void Step(float dt)
        {
            if (_surface == null) _surface = GetComponent<IIslandSurface>();
            if (_surface == null || !_hasGrid) return;

            float dist = DistanceToFocus();
            bool near = dist < simDistance;
            if (_vegRenderer != null) _vegRenderer.enabled = dist < hideDistance;

            _tickTimer += dt;
            float interval = near ? tickInterval : tickInterval * 8f;
            if (_tickTimer >= interval)
            {
                float ldt = _tickTimer * timeScale;
                _tickTimer = 0f;
                Tick(ldt);
            }

            if (!near) return;

            AnimatePlants(dt);
            if (_tintDirty)
            {
                _surface.ApplyGroundTint(_tintFunc);
                _tintDirty = false;
            }
            _meshTimer += dt;
            if (_dirty && _meshTimer >= meshInterval)
            {
                _meshTimer = 0f;
                RebuildVegetationMesh();
                _dirty = false;
            }
        }

        public void Tick(float ldt)
        {
            if (_surface.Version != _gridVersion) RebuildGrid();

            if (_landCells > 0 && Rand() < lightningRate * ldt * Mathf.Clamp(_landCells / 40f, 0.3f, 4f))
            {
                for (int tries = 0; tries < 12; tries++)
                {
                    int idx = _rnd.Next(_land.Length);
                    if (_land[idx] && _fireT[idx] <= 0f && _stage[idx] > 0.3f) { Ignite(idx); break; }
                }
            }

            for (int j = 0; j < _nz; j++)
                for (int i = 0; i < _nx; i++)
                {
                    int idx = j * _nx + i;
                    if (!_land[idx]) continue;

                    if (_fireT[idx] > 0f)
                    {
                        _fireT[idx] -= ldt;
                        _burn[idx] = 1f;
                        TrySpread(i - 1, j, ldt); TrySpread(i + 1, j, ldt);
                        TrySpread(i, j - 1, ldt); TrySpread(i, j + 1, ldt);
                        if (_fireT[idx] <= 0f) { _stage[idx] = 0.02f; _burn[idx] = 0.9f; }
                        continue;
                    }

                    float s = _stage[idx];
                    float grow = growthRate * _fert[idx] * ldt * (1f + _burn[idx]);
                    s = Mathf.Min(s + grow, _maxStage[idx]);
                    _burn[idx] = Mathf.Max(0f, _burn[idx] - 0.025f * ldt);

                    if (s > 1.05f && Rand() < (s - 1.05f) * 0.08f * ldt) s = 0.4f;
                    else if (s > 0.55f && Rand() < fireRate * s * s * ldt) { _stage[idx] = s; Ignite(idx); continue; }
                    _stage[idx] = s;
                }

            SyncPlants(false);
            _tintDirty = true;
        }

        void Ignite(int idx)
        {
            _fireT[idx] = fireDuration * Rand(0.7f, 1.3f);
            _burn[idx] = 1f;
        }

        void TrySpread(int i, int j, float ldt)
        {
            if (i < 0 || j < 0 || i >= _nx || j >= _nz) return;
            int n = j * _nx + i;
            if (!_land[n] || _fireT[n] > 0f || _stage[n] <= 0.3f || _burn[n] >= 0.5f) return;
            if (Rand() < Mathf.Min(0.9f, 0.10f * ldt)) Ignite(n);
        }

        static float Bump(float s, float a, float peak, float b)
        {
            if (s <= a || s >= b) return 0f;
            return s < peak ? Smooth(a, peak, s) : 1f - Smooth(peak, b, s);
        }

        void SyncPlants(bool initial)
        {
            Array.Clear(_quota, 0, _quota.Length);
            var desired = new int[K];

            for (int j = 0; j < _nz; j++)
                for (int i = 0; i < _nx; i++)
                {
                    int idx = j * _nx + i;
                    if (!_land[idx]) continue;

                    if (_fireT[idx] > 0f)
                    {
                        for (int k = 0; k < K; k++) _quota[idx * K + k] = _counts[idx * K + k];
                        continue;
                    }

                    float s = _stage[idx];
                    float f = Mathf.Clamp01(_fert[idx] * 1.4f);
                    float grass = (1f - Smooth(0.4f, 0.9f, s)) * Smooth(0f, 0.12f, s);
                    desired[(int)LifeKind.Grass] = Mathf.RoundToInt(4f * grass * f);
                    desired[(int)LifeKind.Flower] = (((uint)idx * 2654435761u) >> 8) % 3 == 0 && s > 0.12f && s < 0.65f ? 1 : 0;
                    desired[(int)LifeKind.Bush] = Mathf.RoundToInt(2f * Bump(s, 0.2f, 0.5f, 0.9f) * f);
                    desired[(int)LifeKind.Tree] = Mathf.RoundToInt(3f * Smooth(0.5f, 1.15f, s) * f);

                    for (int k = 0; k < K; k++)
                    {
                        int diff = desired[k] - _counts[idx * K + k];
                        if (diff > 0)
                        {
                            for (int d = 0; d < diff && _plants.Count < maxPlants; d++) Spawn((LifeKind)k, idx, i, j, initial);
                        }
                        else if (diff < 0)
                        {
                            _quota[idx * K + k] = -diff;
                        }
                    }
                }

            for (int p = 0; p < _plants.Count; p++)
            {
                var pl = _plants[p];
                if (pl.dying || pl.cell < 0) continue;
                int q = pl.cell * K + (int)pl.kind;
                if (_fireT[pl.cell] > 0f)
                {
                    pl.dying = true;
                    pl.burnT = 1f;
                    _counts[q]--;
                }
                else if (_quota[q] > 0)
                {
                    _quota[q]--;
                    pl.dying = true;
                    _counts[q]--;
                }
                else
                {
                    pl.scaleTarget = TargetScale(pl, _stage[pl.cell]);
                }
            }
            _dirty = true;
        }

        static float TargetScale(Plant p, float stage)
        {
            switch (p.kind)
            {
                case LifeKind.Tree: return Mathf.Lerp(0.35f, 1.3f, Smooth(0.5f, 1.3f, stage)) * p.jitter;
                case LifeKind.Bush: return Mathf.Lerp(0.55f, 1.2f, Smooth(0.2f, 0.9f, stage)) * p.jitter;
                default: return p.jitter;
            }
        }

        void Spawn(LifeKind kind, int cellIdx, int ci, int cj, bool initial)
        {
            Vector2 c = CellCenter(ci, cj);
            Vector2 pos = c;
            bool ok = false;
            for (int t = 0; t < 4 && !ok; t++)
            {
                pos = c + new Vector2(Rand(-0.5f, 0.5f), Rand(-0.5f, 0.5f)) * cellSize;
                float h = _surface.SampleHeight(pos);
                ok = h > 0.15f && h < 3.2f;
            }
            if (!ok) return;

            var pl = new Plant
            {
                kind = kind,
                variant = _rnd.Next(LifeMeshes.Variants),
                pos = pos,
                yaw = Rand(0f, 360f),
                jitter = Rand(0.85f, 1.2f),
                cell = cellIdx,
                growth = initial ? 1f : 0f,
            };
            pl.scaleTarget = TargetScale(pl, _stage[cellIdx]);
            pl.scale = initial ? pl.scaleTarget : 0f;
            _plants.Add(pl);
            _counts[cellIdx * K + (int)kind]++;
        }

        void AnimatePlants(float dt)
        {
            bool changed = false;
            for (int i = _plants.Count - 1; i >= 0; i--)
            {
                var p = _plants[i];
                if (p.dying)
                {
                    p.fade -= dt * (p.burnT > 0f ? 1.6f : 0.8f);
                    changed = true;
                    if (p.fade <= 0f)
                    {
                        _plants[i] = _plants[_plants.Count - 1];
                        _plants.RemoveAt(_plants.Count - 1);
                    }
                    continue;
                }
                if (p.growth < 1f) { p.growth = Mathf.Min(1f, p.growth + dt * 0.7f); changed = true; }
                float goal = p.scaleTarget * (p.growth * p.growth * (3f - 2f * p.growth));
                if (Mathf.Abs(goal - p.scale) > 0.002f)
                {
                    p.scale = Mathf.Lerp(p.scale, goal, 1f - Mathf.Exp(-3f * dt));
                    changed = true;
                }
            }
            if (changed) _dirty = true;
        }

        // ------------------------------------------------------------- rendering

        void EnsureVegObject()
        {
            if (_vegGo != null && _vegGo.transform.parent == transform) return;
            _vegGo = null;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (child.name != VegObjectName) continue;
                if (_vegGo == null) _vegGo = child;
                else if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
            if (_vegGo == null)
            {
                _vegGo = new GameObject(VegObjectName);
                _vegGo.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                _vegGo.transform.SetParent(transform, false);
                _vegGo.AddComponent<MeshFilter>();
                var mr = _vegGo.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            _vegFilter = _vegGo.GetComponent<MeshFilter>();
            _vegRenderer = _vegGo.GetComponent<MeshRenderer>();
            _vegRenderer.sharedMaterial = LifeMeshes.Material;
            if (_vegMesh == null) _vegMesh = new Mesh { name = "Vegetation", hideFlags = HideFlags.DontSave };
            _vegFilter.sharedMesh = _vegMesh;
        }

        static readonly Color Char = new Color(0.07f, 0.06f, 0.05f).linear;

        void RebuildVegetationMesh()
        {
            EnsureVegObject();
            V.Clear(); N.Clear(); C.Clear(); T.Clear();

            foreach (var p in _plants)
            {
                float scale = p.scale * Mathf.Clamp01(p.fade);
                if (scale < 0.01f) continue;
                var tpl = LifeMeshes.GetTemplate(p.kind, p.variant);
                float h = _surface.SampleHeight(p.pos);
                Vector3 origin = new Vector3(p.pos.x, h, p.pos.y);
                float rad = p.yaw * Mathf.Deg2Rad;
                float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
                float swayF = SwayFactor[(int)p.kind];
                int baseIndex = V.Count;

                for (int i = 0; i < tpl.vertices.Length; i++)
                {
                    Vector3 v = tpl.vertices[i] * scale;
                    V.Add(origin + new Vector3(v.x * cs + v.z * sn, v.y, -v.x * sn + v.z * cs));
                    Vector3 nn = tpl.normals[i];
                    N.Add(new Vector3(nn.x * cs + nn.z * sn, nn.y, -nn.x * sn + nn.z * cs));
                    Color col = tpl.colors[i];
                    if (p.burnT > 0f) col = Color.Lerp(col, Char, p.burnT);
                    C.Add(new Color(col.r, col.g, col.b, tpl.sway[i] * swayF));
                }
                for (int i = 0; i < tpl.triangles.Length; i++) T.Add(baseIndex + tpl.triangles[i]);
            }

            _vegMesh.Clear();
            if (V.Count > 65000) _vegMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _vegMesh.SetVertices(V);
            _vegMesh.SetNormals(N);
            _vegMesh.SetColors(C);
            _vegMesh.SetTriangles(T, 0);
            _vegMesh.RecalculateBounds();
        }

        static readonly Color Fresh = new Color(1.15f, 1.2f, 0.75f);
        static readonly Color Plains = new Color(1.3f, 1.2f, 0.62f);
        static readonly Color Shrub = new Color(1.0f, 1.05f, 0.7f);
        static readonly Color Forest = new Color(0.6f, 0.8f, 0.55f);
        static readonly Color Burnt = new Color(0.28f, 0.22f, 0.18f);

        Color CellColor(int idx)
        {
            if (!_land[idx]) return Color.white;
            float s = _stage[idx];
            Color c = s < 0.25f ? Color.Lerp(Fresh, Plains, s / 0.25f)
                : s < 0.6f ? Color.Lerp(Plains, Shrub, (s - 0.25f) / 0.35f)
                : Color.Lerp(Shrub, Forest, Mathf.Clamp01((s - 0.6f) / 0.5f));
            c = Color.Lerp(c, Burnt, Mathf.Clamp01(_burn[idx]));
            c.a = 1f - 0.85f * Mathf.Clamp01(_burn[idx]);
            return c;
        }

        Color TintAt(Vector2 p)
        {
            if (!_hasGrid) return Color.white;
            float fx = (p.x - _origin.x) / cellSize - 0.5f;
            float fz = (p.y - _origin.y) / cellSize - 0.5f;
            int i0 = Mathf.FloorToInt(fx), j0 = Mathf.FloorToInt(fz);
            float tx = fx - i0, tz = fz - j0;
            Color a = CellColorSafe(i0, j0), b = CellColorSafe(i0 + 1, j0);
            Color c = CellColorSafe(i0, j0 + 1), d = CellColorSafe(i0 + 1, j0 + 1);
            Color r = Color.Lerp(Color.Lerp(a, b, tx), Color.Lerp(c, d, tx), tz);
            return r;
        }

        Color CellColorSafe(int i, int j)
        {
            i = Mathf.Clamp(i, 0, _nx - 1);
            j = Mathf.Clamp(j, 0, _nz - 1);
            return CellColor(j * _nx + i);
        }

        // -------------------------------------------------------------- merging

        public void ShiftLocal(Vector2 delta)
        {
            _origin += delta;
            foreach (var p in _plants) p.pos += delta;
        }

        public void AbsorbFrom(IslandLifeSystem other)
        {
            if (other == null || other == this || !other._hasGrid) return;

            foreach (var p in other._plants)
            {
                Vector3 w = other.transform.TransformPoint(p.pos.x, 0f, p.pos.y);
                Vector3 l = transform.InverseTransformPoint(w);
                p.pos = new Vector2(l.x, l.z);
                p.cell = -1;
                _plants.Add(p);
            }
            other._plants.Clear();

            for (int j = 0; j < other._nz; j++)
                for (int i = 0; i < other._nx; i++)
                {
                    int idx = j * other._nx + i;
                    if (!other._land[idx]) continue;
                    Vector2 c = other.CellCenter(i, j);
                    Vector3 w = other.transform.TransformPoint(c.x, 0f, c.y);
                    Vector3 l = transform.InverseTransformPoint(w);
                    _pending.Add(new Import { pos = new Vector2(l.x, l.z), stage = other._stage[idx], burn = other._burn[idx], fireT = other._fireT[idx] });
                }
            _gridVersion = -1;
            _dirty = true;
        }

        public LifeSaveData Capture()
        {
            return new LifeSaveData
            {
                originX = _origin.x, originZ = _origin.y, noiseX = _noiseOff.x, noiseZ = _noiseOff.y,
                cellSize = cellSize, nx = _nx, nz = _nz,
                stage = (float[])_stage.Clone(), burn = (float[])_burn.Clone(), fireT = (float[])_fireT.Clone()
            };
        }

        public void Restore(LifeSaveData d)
        {
            if (_surface == null) _surface = GetComponent<IIslandSurface>();
            if (_surface == null || d == null || d.stage == null) return;
            _rnd ??= new System.Random(seed + gameObject.name.GetHashCode());
            _noiseOff = new Vector2(d.noiseX, d.noiseZ);
            cellSize = d.cellSize;
            _origin = new Vector2(d.originX, d.originZ);
            _nx = d.nx; _nz = d.nz;
            int n = _nx * _nz;
            _stage = (float[])d.stage.Clone(); _burn = (float[])d.burn.Clone(); _fireT = (float[])d.fireT.Clone();
            _fert = new float[n]; _maxStage = new float[n]; _land = new bool[n];
            _counts = new int[n * K]; _quota = new int[n * K];
            _landCells = 0;
            for (int j = 0; j < _nz; j++)
                for (int i = 0; i < _nx; i++)
                {
                    int idx = j * _nx + i;
                    CellProps(idx, _surface.SampleHeight(CellCenter(i, j)));
                    if (_land[idx]) { _landCells++; _stage[idx] = Mathf.Min(_stage[idx], _maxStage[idx]); }
                }
            _plants.Clear();
            _pending.Clear();
            _hasGrid = true;
            _gridVersion = _surface.Version;
            SyncPlants(true);
            RebuildVegetationMesh();
            _surface.ApplyGroundTint(_tintFunc ??= TintAt);
            _dirty = false;
            _tintDirty = false;
        }

        public void CatchUp(float lifeSeconds)
        {
            if (lifeSeconds > 0.5f) Simulate(lifeSeconds, 10f);
        }

        public void Simulate(float lifeSeconds, float step = 1f)
        {
            float t = 0f;
            while (t < lifeSeconds)
            {
                Tick(step);
                t += step;
            }
            for (int i = 0; i < 8; i++) AnimatePlants(0.5f);
            RebuildVegetationMesh();
            _surface.ApplyGroundTint(_tintFunc);
            _dirty = false;
            _tintDirty = false;
        }
    }
}
