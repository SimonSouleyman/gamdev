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
    // On the adventure ring (RingWorld.Active) the sea is a strip instead: vertex columns exactly on both rims and
    // rows at most ringRowSpacing apart over a whole circumference. The ring bend (DriftRingWS) folds everything past
    // the rims onto the rim plane and wraps z onto a circle of radius C / 2pi; the nested grid's 6-48 u cells beyond
    // 72 u were flat chords of that circle and stuck out past the rims as pale polygons.
    [ExecuteAlways]
    [DefaultExecutionOrder(250)]
    public class WaterFollower : MonoBehaviour
    {
        public Transform follow;
        public bool buildMesh = true;
        public float cellSize = 3f;
        public int innerCells = 48;
        [Range(1, 6)] public int levels = 5;
        [Tooltip("Ring (Abenteuer): größter Abstand der Wasser-Gitterzeilen entlang der Strecke.")]
        [Range(1f, 6f)] public float ringRowSpacing = 3.2f;

        const string MeshName = "DriftWaterGrid";

        Mesh _mesh;
        Mesh _original;
        MeshFilter _filter;
        Vector3 _builtScale;
        float _builtCell;
        int _builtCells, _builtLevels;
        bool _builtRing;
        float _builtHalfWidth, _builtCircumference, _builtRowSpacing, _ringRowStep;
        Transform _followed;
        bool _followedIsCamera;

        public float Snap => cellSize * 4f;
        public float HalfExtent => cellSize * innerCells * 0.5f * (1 << (Mathf.Max(1, levels) - 1));
        public int VertexCount => _mesh != null ? _mesh.vertexCount : 0;
        public int TriangleCount { get; private set; }
        public bool IsRingStrip => _mesh != null && _builtRing;

        public struct RingStripLayout
        {
            public int columns;
            public int halfRows;
            public float columnStep;
            public float rowStep;
            public int Rows => 2 * halfRows;
            public int VertexCount => (columns + 1) * (Rows + 1);
        }

        // A whole number of rows per circumference, so the snapped rows sit on the same world z every lap. halfRows
        // reaches half a lap plus half a row past the snapped centre, which is at most half a row from the focus.
        public static RingStripLayout RingStrip(float bandWidth, float circumference, float maxColumnStep, float maxRowStep)
        {
            bandWidth = Mathf.Max(0.01f, bandWidth);
            circumference = Mathf.Max(1f, circumference);
            int cols = Mathf.Max(1, Mathf.CeilToInt(bandWidth / Mathf.Max(0.1f, maxColumnStep) - 1e-4f));
            int perLap = Mathf.Max(3, Mathf.CeilToInt(circumference / Mathf.Max(0.1f, maxRowStep) - 1e-4f));
            return new RingStripLayout
            {
                columns = cols,
                columnStep = bandWidth / cols,
                rowStep = circumference / perLap,
                halfRows = (perLap + 1) / 2 + 1,
            };
        }

        public static float RingSnapZ(float focusZ, float rowStep) => rowStep > 0f ? Mathf.Round(focusZ / rowStep) * rowStep : focusZ;

        // Local vertices around the strip centre (x = band centre, z = snapped focus), divided by the transform scale.
        // Column 0 and column `columns` lie exactly on -halfWidth / +halfWidth. Faces are wound to look up.
        public static void BuildRingStrip(in RingStripLayout layout, float halfWidth, float invScaleX, float invScaleZ,
            List<Vector3> verts, List<int> tris)
        {
            verts.Clear();
            tris.Clear();
            int cols = layout.columns, rows = layout.Rows;
            for (int k = 0; k <= rows; k++)
            {
                float z = (k - layout.halfRows) * layout.rowStep * invScaleZ;
                for (int i = 0; i <= cols; i++)
                {
                    float x = i == 0 ? -halfWidth : i == cols ? halfWidth : -halfWidth + i * layout.columnStep;
                    verts.Add(new Vector3(x * invScaleX, 0f, z));
                }
            }
            int stride = cols + 1;
            for (int k = 0; k < rows; k++)
                for (int i = 0; i < cols; i++)
                {
                    int a = k * stride + i, b = a + stride, c = b + 1, d = a + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(a); tris.Add(c); tris.Add(d);
                }
        }

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
            if (buildMesh && _mesh != null && _builtRing && RingWorld.Active != null)
                c = new Vector2(RingWorld.Active.Geometry.centerX, RingSnapZ(c.y, _ringRowStep));
            else if (buildMesh && _mesh != null)
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
            var ring = RingWorld.Active;
            bool ringMode = ring != null;
            RingGeometry g = ringMode ? ring.Geometry : default;
            bool stale = _mesh == null || scale != _builtScale || cellSize != _builtCell || ringMode != _builtRing;
            if (!stale && ringMode)
                stale = g.halfWidth != _builtHalfWidth || g.circumference != _builtCircumference || ringRowSpacing != _builtRowSpacing;
            else if (!stale)
                stale = innerCells != _builtCells || levels != _builtLevels;
            if (stale)
            {
                if (ringMode) BuildRing(scale, g);
                else Build(scale);
            }
            if (_filter.sharedMesh != _mesh)
            {
                if (_filter.sharedMesh != null && _filter.sharedMesh.name != MeshName) _original = _filter.sharedMesh;
                _filter.sharedMesh = _mesh;
            }
        }

        void BuildRing(Vector3 scale, RingGeometry g)
        {
            _builtScale = scale;
            _builtCell = cellSize;
            _builtRing = true;
            _builtHalfWidth = g.halfWidth;
            _builtCircumference = g.circumference;
            _builtRowSpacing = ringRowSpacing;
            var layout = RingStrip(2f * g.halfWidth, g.circumference, Mathf.Max(0.25f, cellSize), ringRowSpacing);
            _ringRowStep = layout.rowStep;

            var verts = new List<Vector3>(layout.VertexCount);
            var tris = new List<int>(layout.columns * layout.Rows * 6);
            float sx = Mathf.Abs(scale.x) > 1e-5f ? 1f / scale.x : 1f;
            float sz = Mathf.Abs(scale.z) > 1e-5f ? 1f / scale.z : 1f;
            BuildRingStrip(layout, g.halfWidth, sx, sz, verts, tris);
            float sy = Mathf.Abs(scale.y) > 1e-5f ? 1f / scale.y : 1f;
            Upload(verts, tris, new Vector3(2f * g.halfWidth * sx, 100f * sy, 2f * layout.halfRows * layout.rowStep * sz));
        }

        void Upload(List<Vector3> verts, List<int> tris, Vector3 boundsSize)
        {
            if (_mesh == null) _mesh = new Mesh { name = MeshName, hideFlags = HideFlags.DontSave };
            else _mesh.Clear();
            _mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.SetVertices(verts);
            _mesh.SetTriangles(tris, 0, false);
            var normals = new Vector3[verts.Count];
            for (int k = 0; k < normals.Length; k++) normals[k] = Vector3.up;
            _mesh.normals = normals;
            _mesh.bounds = new Bounds(Vector3.zero, boundsSize);
            _mesh.UploadMeshData(false);
            TriangleCount = tris.Count / 3;
        }

        void Build(Vector3 scale)
        {
            _builtRing = false;
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

            float ext = c * n * (1 << (lv - 1));
            float sy = Mathf.Abs(scale.y) > 1e-5f ? 1f / scale.y : 1f;
            Upload(verts, tris, new Vector3(ext * sx, 100f * sy, ext * sz));
        }
    }
}
