using System.Collections.Generic;
using System.IO;
using Drift.Core;
using Drift.World;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Drift.EditorTools
{
    public static class PlanetTopologyBaker
    {
        const string OutputPath = "Assets/_Drift/Data/PlanetTopology.asset";

        [MenuItem("Drift/World/Bake Planet Topology")]
        public static void BakeDefault()
        {
            Bake(subdivisions: 6, seed: 12345, oceanLevel: 0.02f);
        }

        public static PlanetTopologyAsset Bake(int subdivisions, int seed, float oceanLevel)
        {
            IcosphereGenerator.IcosphereData ico = IcosphereGenerator.Generate(subdivisions);
            int tileCount = ico.Vertices.Length;

            var elevation = new float[tileCount];
            var biome = new int[tileCount];
            var plate = new int[tileCount];

            var rnd = new Unity.Mathematics.Random((uint)(seed == 0 ? 1 : seed));
            float3 noiseOffset = rnd.NextFloat3(-1000f, 1000f);

            for (int i = 0; i < tileCount; i++)
            {
                float3 p = (Vector3)ico.Vertices[i];
                float n =
                    noise.snoise(p * 1.5f + noiseOffset) * 0.6f +
                    noise.snoise(p * 3.5f + noiseOffset) * 0.3f +
                    noise.snoise(p * 7.0f + noiseOffset) * 0.1f;
                elevation[i] = n - oceanLevel;
                biome[i] = -1;
                plate[i] = -1;
            }

            var neighborSets = new HashSet<int>[tileCount];
            for (int i = 0; i < tileCount; i++) neighborSets[i] = new HashSet<int>();
            int triCount = ico.Triangles.Length / 3;
            for (int t = 0; t < triCount; t++)
            {
                int a = ico.Triangles[t * 3 + 0];
                int b = ico.Triangles[t * 3 + 1];
                int c = ico.Triangles[t * 3 + 2];
                neighborSets[a].Add(b); neighborSets[a].Add(c);
                neighborSets[b].Add(a); neighborSets[b].Add(c);
                neighborSets[c].Add(a); neighborSets[c].Add(b);
            }

            var neighborOffsets = new int[tileCount + 1];
            var neighborList = new List<int>();
            for (int i = 0; i < tileCount; i++)
            {
                neighborOffsets[i] = neighborList.Count;
                neighborList.AddRange(neighborSets[i]);
            }
            neighborOffsets[tileCount] = neighborList.Count;

            Directory.CreateDirectory("Assets/_Drift/Data");
            var asset = AssetDatabase.LoadAssetAtPath<PlanetTopologyAsset>(OutputPath);
            bool isNew = asset == null;
            if (isNew) asset = ScriptableObject.CreateInstance<PlanetTopologyAsset>();

            asset.tilePositions = ico.Vertices;
            asset.tileElevation = elevation;
            asset.tileBiome = biome;
            asset.tilePlate = plate;
            asset.neighborOffsets = neighborOffsets;
            asset.neighborIndices = neighborList.ToArray();
            asset.triangles = ico.Triangles;
            asset.triangleChunkId = ico.TriangleChunkId;
            asset.chunkCount = 20;

            if (isNew) AssetDatabase.CreateAsset(asset, OutputPath);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[PlanetTopologyBaker] Baked {tileCount} tiles, {triCount} triangles, {asset.chunkCount} chunks -> {OutputPath}");
            return asset;
        }
    }
}
