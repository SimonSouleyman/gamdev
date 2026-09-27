using UnityEngine;

namespace Drift.World
{
    [CreateAssetMenu(fileName = "PlanetTopology", menuName = "Drift/World/Planet Topology Asset")]
    public class PlanetTopologyAsset : ScriptableObject
    {
        [Header("Baked Tile Graph")]
        public Vector3[] tilePositions;
        public float[] tileElevation;
        public int[] tileBiome;
        public int[] tilePlate;

        [Header("Adjacency (flattened CSR)")]
        public int[] neighborOffsets;
        public int[] neighborIndices;

        [Header("Render Mesh Source")]
        public int[] triangles;
        public int[] triangleChunkId;
        public int chunkCount = 20;

        public int TileCount => tilePositions != null ? tilePositions.Length : 0;

        public int GetNeighborStart(int tileId) => neighborOffsets[tileId];
        public int GetNeighborEnd(int tileId) => neighborOffsets[tileId + 1];
    }
}
