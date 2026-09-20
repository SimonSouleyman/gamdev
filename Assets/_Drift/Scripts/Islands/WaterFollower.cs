using System.Collections.Generic;
using UnityEngine;

namespace Drift.Islands
{
    // Keeps the sea under the view and gives it a mesh the curved-world bend (Shaders/DriftCurve.hlsl) can
    // work on: the scene's Plane has 100 u cells, this is a nested grid (3 u cells out to 72 u, doubling per
    // level out to 1152 u, past the far clip) with the level seams stitched, so there are no T-junction cracks
    // once the vertices are bent. The object follows the point the camera looks at, snapped to the cell size of
    // level 2, so the vertices of the three inner levels stay on fixed world positions and the bent surface
    // (and with it every shoreline) does not swim while the view moves.
    [ExecuteAlways]
    [DefaultExecutionOrder(250)]
    public class WaterFollower : MonoBehaviour
    {
        public Transform follow;
        public bool buildMesh = true;
        public float cellSize = 3f;
        public int innerCells = 48;
        [Range(1, 6)] public int levels = 5;

        const string MeshName = "DriftWaterGrid";

        Mesh _mesh;
        Mesh _original;
        MeshFilter _filter;
        Vector3 _builtScale;
        float _builtCell;
        int _builtCells, _builtLevels;
        Transform _followed;
        bool _followedIsCamera;

        public float Snap => cellSize * 4f;
        public float HalfExtent => cellSize * innerCells * 0.5f * (1 << (Mathf.Max(1, levels) - 1));
        public int VertexCount => _mesh != null ? _mesh.vertexCount : 0;
        public int TriangleCount { get; private set; }

        // The ground point a camera looks at, kept within reach of the camera when it looks at the horizon.
        // CurvedWorld uses the same point as the zero of the bend.
        public static Vector2 ViewFocus(Transform cam)
        {
            Vector3 p = cam.position, f = cam.forward;
            var here = new Vector2(p.x, p.z);
            var flat = new Vector2(f.x, f.z);
            float fl = flat.magnitude;
            if (fl < 1e-4f) return here;
            float h = Mathf.Max(0f, p.y);
            float maxDist = 1.5f * h + 5f;
            float dist = f.y < -1e-4f ? Mathf.Min(maxDist, h * fl / -f.y) : maxDist;
            return here + flat * (dist / fl);
        }

        void OnEnable()
        {
            EnsureMesh();
            LateUpdate();
        }

        void OnDisable()
        {
            if (_filter != null && _mesh != null && _filter.sharedMesh == _mesh)
                _filter.sharedMesh = _original != null ? _original : Resources.GetBuiltinResource<Mesh>("New-Plane.fbx");
            if (_mesh != null)
            {
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
                _mesh = null;
            }
        }

        void LateUpdate()
        {
            EnsureMesh();
            Transform t = follow != null ? follow : (Camera.main != null ? Camera.main.transform : null);
            if (t == null) return;
            if (t != _followed)
            {
                _followed = t;
                _followedIsCamera = t.GetComponent<Camera>() != null;
            }
            Vector2 c = _followedIsCamera ? ViewFocus(t) : new Vector2(t.position.x, t.position.z);
            if (buildMesh && _mesh != null)
            {
                float s = Snap;
                c = new Vector2(Mathf.Round(c.x / s) * s, Mathf.Round(c.y / s) * s);
            }
            var pos = new Vector3(c.x, transform.position.y, c.y);
            if (pos != transform.position) transform.position = pos;
        }

        void EnsureMesh()
        {
            if (_filter == null) _filter = GetComponent<MeshFilter>();
            if (_filter == null) return;
            if (!buildMesh)
            {
                if (_mesh != null) OnDisable();
                return;
            }
            Vector3 scale = transform.lossyScale;
            bool stale = _mesh == null || scale != _builtScale || cellSize != _builtCell || innerCells != _builtCells || levels != _builtLevels;
            if (stale) Build(scale);
            if (_filter.sharedMesh != _mesh)
            {
                if (_filter.sharedMesh != null && _filter.sharedMesh.name != MeshName) _original = _filter.sharedMesh;
                _filter.sharedMesh = _mesh;
            }
        }

        void Build(Vector3 scale)
        {
            int n = Mathf.Max(8, innerCells / 4 * 4);
            int lv = Mathf.Max(1, levels);
            float c = Mathf.Max(0.25f, cellSize);
            _builtScale = scale;
            _builtCell = cellSize;
            _builtCells = innerCells;
            _builtLevels = levels;

            // Integer lattice in units of the finest cell; a vertex shared by two levels is emitted once.
            var index = new Dictionary<long, int>(12000);
            var verts = new List<Vector3>(12000);
            var tris = new List<int>(60000);
            float sx = Mathf.Abs(scale.x) > 1e-5f ? 1f / scale.x : 1f;
            float sz = Mathf.Abs(scale.z) > 1e-5f ? 1f / scale.z : 1f;

            int Vert(int ix, int iz)
            {
                long key = ((long)(ix + 1000000) << 32) | (uint)(iz + 1000000);
                if (index.TryGetValue(key, out int v)) return v;
                v = verts.Count;
                verts.Add(new Vector3(ix * c * sx, 0f, iz * c * sz));
                index[key] = v;
                return v;
            }

            var poly = new int[8];
            int h0 = n / 4, h1 = 3 * n / 4;
            for (int l = 0; l < lv; l++)
            {
                int step = 1 << l;
                int half = n / 2 * step;
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        bool inI = i >= h0 && i < h1, inJ = j >= h0 && j < h1;
                        if (l > 0 && inI && inJ) continue;
                        int x0 = -half + i * step, x1 = x0 + step, z0 = -half + j * step, z1 = z0 + step;
                        int xm = x0 + step / 2, zm = z0 + step / 2;
                        bool fineRight = l > 0 && i == h0 - 1 && inJ, fineLeft = l > 0 && i == h1 && inJ;
                        bool fineTop = l > 0 && j == h0 - 1 && inI, fineBottom = l > 0 && j == h1 && inI;

                        // Clockwise seen from above (the face looks up); the fan starts at the seam midpoint so the
                        // finer level's vertex on that edge is part of this cell too.
                        int m = 0, fan = 0;
                        poly[m++] = Vert(x0, z0);
                        if (fineLeft) { fan = m; poly[m++] = Vert(x0, zm); }
                        poly[m++] = Vert(x0, z1);
                        if (fineTop) { fan = m; poly[m++] = Vert(xm, z1); }
                        poly[m++] = Vert(x1, z1);
                        if (fineRight) { fan = m; poly[m++] = Vert(x1, zm); }
                        poly[m++] = Vert(x1, z0);
                        if (fineBottom) { fan = m; poly[m++] = Vert(xm, z0); }
                        for (int t = 1; t < m - 1; t++)
                        {
                            tris.Add(poly[fan]);
                            tris.Add(poly[(fan + t) % m]);
                            tris.Add(poly[(fan + t + 1) % m]);
                        }
                    }
            }

            if (_mesh == null) _mesh = new Mesh { name = MeshName, hideFlags = HideFlags.DontSave };
            else _mesh.Clear();
            _mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.SetVertices(verts);
            _mesh.SetTriangles(tris, 0, false);
            var normals = new Vector3[verts.Count];
            for (int k = 0; k < normals.Length; k++) normals[k] = Vector3.up;
            _mesh.normals = normals;
            float ext = c * n * (1 << (lv - 1));
            float sy = Mathf.Abs(scale.y) > 1e-5f ? 1f / scale.y : 1f;
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(ext * sx, 100f * sy, ext * sz));
            _mesh.UploadMeshData(false);
            TriangleCount = tris.Count / 3;
        }
    }
}
