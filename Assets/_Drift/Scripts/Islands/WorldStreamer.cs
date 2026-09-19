using System.Collections.Generic;
using UnityEngine;

namespace Drift.Islands
{
    [ExecuteAlways]
    public class WorldStreamer : MonoBehaviour
    {
        public Island player;
        public Material islandMaterial;
        public int seed = 777;
        public float chunkSize = 110f;
        public int loadRadius = 1;
        public int unloadMargin = 1;
        public int minPerChunk = 2;
        public int maxPerChunk = 4;
        public float minLandRadius = 1.5f;
        public float maxLandRadius = 7f;
        public float startExclusion = 18f;
        public int spawnsPerFrame = 1;

        class Slot
        {
            public Vector2 pos;
            public float radius;
            public int shapeSeed;
            public int index;
            public Island island;
            public bool spawned;
        }

        class Chunk
        {
            public Vector2Int coord;
            public readonly List<Slot> slots = new();
        }

        readonly Dictionary<Vector2Int, Chunk> _chunks = new();
        readonly HashSet<long> _consumed = new();
        Vector2 _startPos;
        bool _started;

        public int LoadedChunks => _chunks.Count;

        public Vector2 StartPosition => _startPos;

        public long[] ConsumedKeys()
        {
            var a = new long[_consumed.Count];
            _consumed.CopyTo(a);
            return a;
        }

        public void RestoreState(Vector2 startPos, long[] consumed)
        {
            _startPos = startPos;
            _started = true;
            _consumed.Clear();
            if (consumed != null) foreach (var k in consumed) _consumed.Add(k);
        }

        public int LiveIslands
        {
            get
            {
                int n = 0;
                foreach (var c in _chunks.Values)
                    foreach (var s in c.slots)
                        if (s.spawned && s.island != null) n++;
                return n;
            }
        }

        void OnEnable()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
            _chunks.Clear();
            _consumed.Clear();
            _started = false;
        }

        void Update()
        {
            if (player == null) player = FindPlayer();
            if (player == null) return;
            if (!Application.isPlaying)
            {
                if (!_started) StreamAround(true);
                return;
            }
            StreamAround(false);
        }

        static Island FindPlayer()
        {
            foreach (var i in Island.All) if (i != null && i.useKeyboardInput) return i;
            return null;
        }

        static long Key(Vector2Int c, int idx) => ((long)c.x << 40) ^ ((long)c.y << 20) ^ idx;

        static int Hash(int a, int b, int c)
        {
            unchecked { return a * 73856093 ^ b * 19349663 ^ c * 83492791; }
        }

        public void StreamAround(bool immediate)
        {
            if (!_started)
            {
                _started = true;
                _startPos = new Vector2(player.transform.position.x, player.transform.position.z);
            }

            Vector2 pp = new Vector2(player.transform.position.x, player.transform.position.z);
            var pc = new Vector2Int(Mathf.FloorToInt(pp.x / chunkSize), Mathf.FloorToInt(pp.y / chunkSize));

            for (int dx = -loadRadius; dx <= loadRadius; dx++)
                for (int dz = -loadRadius; dz <= loadRadius; dz++)
                {
                    var c = new Vector2Int(pc.x + dx, pc.y + dz);
                    if (!_chunks.ContainsKey(c)) _chunks[c] = Plan(c);
                }

            var toRemove = new List<Vector2Int>();
            foreach (var kv in _chunks)
            {
                var c = kv.Key;
                if (Mathf.Max(Mathf.Abs(c.x - pc.x), Mathf.Abs(c.y - pc.y)) > loadRadius + unloadMargin)
                {
                    Unload(kv.Value);
                    toRemove.Add(c);
                }
            }
            foreach (var c in toRemove) _chunks.Remove(c);

            int budget = immediate ? int.MaxValue : spawnsPerFrame;
            foreach (var chunk in _chunks.Values)
                foreach (var slot in chunk.slots)
                {
                    if (slot.spawned)
                    {
                        if (slot.island == null) _consumed.Add(Key(chunk.coord, slot.index));
                        continue;
                    }
                    if (budget <= 0) return;
                    if (_consumed.Contains(Key(chunk.coord, slot.index))) { slot.spawned = true; continue; }
                    Spawn(chunk, slot);
                    budget--;
                }
        }

        Chunk Plan(Vector2Int coord)
        {
            var chunk = new Chunk { coord = coord };
            var rnd = new System.Random(Hash(seed, coord.x, coord.y));
            int count = rnd.Next(minPerChunk, maxPerChunk + 1);
            Vector2 min = new Vector2(coord.x, coord.y) * chunkSize;
            for (int i = 0; i < count; i++)
            {
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    float radius = minLandRadius + (maxLandRadius - minLandRadius) * Mathf.Pow((float)rnd.NextDouble(), 1.6f);
                    Vector2 pos = min + new Vector2(
                        Mathf.Lerp(10f, chunkSize - 10f, (float)rnd.NextDouble()),
                        Mathf.Lerp(10f, chunkSize - 10f, (float)rnd.NextDouble()));
                    if (_started && Vector2.Distance(pos, _startPos) < startExclusion + radius) continue;

                    bool clash = false;
                    foreach (var o in chunk.slots)
                        if (Vector2.Distance(o.pos, pos) < (o.radius + radius) * 1.8f + 6f) { clash = true; break; }
                    if (clash) continue;

                    chunk.slots.Add(new Slot { pos = pos, radius = radius, shapeSeed = rnd.Next(), index = i });
                    break;
                }
            }
            return chunk;
        }

        void Spawn(Chunk chunk, Slot slot)
        {
            var go = new GameObject($"Island_{chunk.coord.x}_{chunk.coord.y}_{slot.index}");
            go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(slot.pos.x, 0f, slot.pos.y);
            go.transform.rotation = Quaternion.Euler(0f, (slot.shapeSeed & 1023) * 0.35f, 0f);

            var island = go.AddComponent<Island>();
            island.useKeyboardInput = false;
            island.landRadius = slot.radius;
            island.shapeSeed = slot.shapeSeed;
            go.AddComponent<Drift.Life.IslandLifeSystem>().seed = slot.shapeSeed;
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = islandMaterial;
            go.SetActive(true);

            slot.island = island;
            slot.spawned = true;
        }

        void Unload(Chunk chunk)
        {
            foreach (var slot in chunk.slots)
            {
                if (!slot.spawned) continue;
                if (slot.island == null)
                {
                    _consumed.Add(Key(chunk.coord, slot.index));
                    continue;
                }
                var go = slot.island.gameObject;
                go.SetActive(false);
                if (Application.isPlaying) Destroy(go);
                else DestroyImmediate(go);
            }
        }
    }
}
