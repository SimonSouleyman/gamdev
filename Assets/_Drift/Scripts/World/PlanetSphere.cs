using System.Collections.Generic;
using UnityEngine;

namespace Drift.World
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class PlanetSphere : MonoBehaviour
    {
        public PlanetTopologyAsset topology;
        public float planetRadius = 400f;
        public float elevationScale = 3f;
        public Material chunkMaterial;
        public bool buildCollider = true;

        readonly List<GameObject> _chunkObjects = new();

        void OnEnable()
        {
            BuildChunks();
        }

        public void BuildChunks()
        {
            // _chunkObjects is plain C# state and does not survive a domain reload, so rebuilding
            // relies on the actual transform hierarchy (always exactly the procedural chunks) to
            // find prior chunks to remove, rather than the (possibly stale/empty) tracking list.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
            _chunkObjects.Clear();

            if (topology == null || topology.TileCount == 0)
            {
                Debug.LogError("PlanetSphere: no baked PlanetTopologyAsset assigned.", this);
                return;
            }

            if (chunkMaterial != null)
            {
                chunkMaterial.SetVector("_PlanetCenter", transform.position);
                chunkMaterial.SetFloat("_PlanetRadius", planetRadius);
            }

            var byChunk = new Dictionary<int, List<int>>();
            for (int t = 0; t < topology.triangleChunkId.Length; t++)
            {
                int chunk = topology.triangleChunkId[t];
                if (!byChunk.TryGetValue(chunk, out var list)) byChunk[chunk] = list = new List<int>();
                list.Add(t);
            }

            foreach (var kvp in byChunk)
            {
                var chunkGo = new GameObject($"Chunk_{kvp.Key:00}");
                chunkGo.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                chunkGo.transform.SetParent(transform, false);
                var mf = chunkGo.AddComponent<MeshFilter>();
                var mr = chunkGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = chunkMaterial;
                var mesh = BuildChunkMesh(kvp.Value);
                mf.sharedMesh = mesh;
                if (buildCollider)
                {
                    var mc = chunkGo.AddComponent<MeshCollider>();
                    mc.sharedMesh = mesh;
                }
                _chunkObjects.Add(chunkGo);
            }
        }

        Mesh BuildChunkMesh(List<int> triIndices)
        {
            var remap = new Dictionary<int, int>();
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>(triIndices.Count * 3);

            int Remap(int globalTileId)
            {
                if (remap.TryGetValue(globalTileId, out int local)) return local;
                Vector3 n = topology.tilePositions[globalTileId].normalized;
                float elevation = topology.tileElevation[globalTileId];
                float r = planetRadius + elevation * elevationScale;
                verts.Add(n * r);
                normals.Add(n);
                local = verts.Count - 1;
                remap[globalTileId] = local;
                return local;
            }

            foreach (int t in triIndices)
            {
                int a = topology.triangles[t * 3 + 0];
                int b = topology.triangles[t * 3 + 1];
                int c = topology.triangles[t * 3 + 2];
                tris.Add(Remap(a));
                tris.Add(Remap(b));
                tris.Add(Remap(c));
            }

            var mesh = new Mesh { name = "PlanetChunk" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
