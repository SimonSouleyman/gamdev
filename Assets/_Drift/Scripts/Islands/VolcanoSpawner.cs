using System.Collections.Generic;
using Drift.Tectonics;
using UnityEngine;

namespace Drift.Islands
{
    // Turns rift events on divergent plate boundaries into new volcanic islands that rise out of the sea,
    // now and then as an island arc: two or three cones of different size in a row along the same rift.
    [ExecuteAlways]
    public class VolcanoSpawner : MonoBehaviour
    {
        public Island player;
        public Material islandMaterial;
        public int maxLive = 10;
        public float cooldown = 25f;
        public float minPlayerDistance = 30f;
        public float maxPlayerDistance = 200f;
        public float minIslandDistance = 25f;
        public float unloadDistance = 260f;
        public float minRadius = 2.2f;
        public float maxRadius = 5.5f;
        public float emergeDuration = 12f;
        public float chainChance = 0.35f;
        public int chainMax = 3;
        public float chainStagger = 5f;
        public float seamSnapDistance = 4f;
        public float seamReturnSpeed = 6f;
        public bool holdAfterEmergence = true;
        public int seed = 31337;

        struct SeamAnchor
        {
            public Vector2Int a, b;
        }

        readonly List<Island> _live = new();
        readonly Dictionary<Island, SeamAnchor> _anchors = new();
        readonly List<Island> _chain = new();
        System.Random _rnd;
        PlateSystem _subscribed;
        float _cooldownLeft;

        public int LiveVolcanoes
        {
            get
            {
                Prune();
                return _live.Count;
            }
        }

        public float CooldownRemaining => Mathf.Max(0f, _cooldownLeft);
        public int TotalSpawned { get; private set; }
        public int ChainsSpawned { get; private set; }

        void OnEnable()
        {
            _rnd = new System.Random(seed);
            _cooldownLeft = 0f;
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
            Clear();
        }

        public void Reseed(int newSeed)
        {
            seed = newSeed;
            _rnd = new System.Random(seed);
            _cooldownLeft = 0f;
            TotalSpawned = 0;
            ChainsSpawned = 0;
            Clear();
        }

        void Update()
        {
            if (player == null) player = FindPlayer();
            Subscribe();
            if (!Application.isPlaying) return;
            Step(Time.deltaTime);
        }

        // Cooldown, unloading and the seam anchor of rising cones; public so it can be driven headlessly.
        public void Step(float dt)
        {
            _cooldownLeft = Mathf.Max(0f, _cooldownLeft - dt);
            Prune();
            HoldOnSeam(dt);
        }

        static Island FindPlayer()
        {
            foreach (var i in Island.All) if (i != null && i.useKeyboardInput) return i;
            return null;
        }

        void Subscribe()
        {
            var ps = PlateSystem.Instance;
            if (ps == _subscribed) return;
            Unsubscribe();
            _subscribed = ps;
            if (_subscribed != null) _subscribed.EventStarted += OnPlateEvent;
        }

        void Unsubscribe()
        {
            if (_subscribed != null) _subscribed.EventStarted -= OnPlateEvent;
            _subscribed = null;
        }

        void OnPlateEvent(PlateEventData e)
        {
            if (!Application.isPlaying) return;
            TryHandleRift(e);
        }

        // Returns how many volcanoes the event produced (0 = filtered out).
        public int TryHandleRift(PlateEventData e)
        {
            if (e.type != PlateEventType.Rift) return 0;
            if (player == null) player = FindPlayer();
            if (player == null || islandMaterial == null) return 0;
            if (_cooldownLeft > 0f) return 0;
            Prune();
            if (_live.Count >= maxLive) return 0;

            _chain.Clear();
            if (!Admissible(e.position)) return 0;

            _rnd ??= new System.Random(seed);
            bool onSeam = FindSeam(e.position, 3f, out var seam);
            var first = Spawn(e.position, e.strength);
            if (onSeam) _anchors[first] = new SeamAnchor { a = seam.a.cell, b = seam.b.cell };
            _chain.Add(first);

            if (onSeam && _rnd.NextDouble() < chainChance) GrowChain(e.position, first.landRadius, seam);
            if (_chain.Count > 1) ChainsSpawned++;
            int made = _chain.Count;
            _chain.Clear();
            return made;
        }

        bool Admissible(Vector2 pos)
        {
            float d = Vector2.Distance(pos, player.PlanarPosition);
            if (d < minPlayerDistance || d > maxPlayerDistance) return false;
            foreach (var island in Island.All)
            {
                if (island == null || _chain.Contains(island)) continue;
                if (Vector2.Distance(island.PlanarPosition, pos) < minIslandDistance + island.BoundingRadius) return false;
            }
            return true;
        }

        // An island arc: further, smaller cones in a row along the (curved) seam, alternating sides of the
        // first one, each rising a little later than the one before. Steps are arc length on the seam curve.
        void GrowChain(Vector2 origin, float firstRadius, in PlateSystem.Border seam)
        {
            var ps = PlateSystem.Instance;
            if (ps == null || seam.length < 1f) return;
            ps.ClosestOnSeam(seam, origin, out float t0, out _);
            float tAhead = t0, tBehind = t0;

            int want = _rnd.Next(2, Mathf.Max(2, chainMax) + 1);
            float lastAhead = firstRadius, lastBehind = firstRadius;
            for (int i = 1; i < want && _live.Count < maxLive; i++)
            {
                float radius = Mathf.Max(minRadius * 0.7f, firstRadius * Mathf.Lerp(0.5f, 0.85f, (float)_rnd.NextDouble()));
                bool forward = (i & 1) == 1;
                float step = ((forward ? lastAhead : lastBehind) + radius) * 1.6f + 6f + 6f * (float)_rnd.NextDouble();
                float t = forward ? (tAhead = ps.SeamAdvance(seam, tAhead, step)) : (tBehind = ps.SeamAdvance(seam, tBehind, -step));
                if (forward) lastAhead = radius; else lastBehind = radius;
                if (t * seam.length < radius || (1f - t) * seam.length < radius) continue;

                Vector2 pos = ps.SeamPoint(seam, t);
                if (!Admissible(pos)) continue;
                var island = Create(pos, RandomYaw(out int shapeSeed), radius, shapeSeed);
                island.BeginEmergence(emergeDuration + chainStagger * _chain.Count);
                _anchors[island] = new SeamAnchor { a = seam.a.cell, b = seam.b.cell };
                _live.Add(island);
                _chain.Add(island);
                TotalSpawned++;
            }
        }

        float RandomYaw(out int shapeSeed)
        {
            shapeSeed = _rnd.Next();
            return (shapeSeed & 1023) * 0.35f;
        }

        public Island Spawn(Vector2 pos, float strength)
        {
            _rnd ??= new System.Random(seed);
            _cooldownLeft = cooldown;
            float radius = Mathf.Lerp(minRadius, maxRadius, Mathf.Clamp01(strength));
            float yaw = RandomYaw(out int shapeSeed);

            var island = Create(pos, yaw, radius, shapeSeed);
            island.BeginEmergence(emergeDuration);
            _live.Add(island);
            TotalSpawned++;
            return island;
        }

        static bool FindSeam(Vector2 pos, float within, out PlateSystem.Border seam)
        {
            seam = default;
            var ps = PlateSystem.Instance;
            if (ps == null) return false;
            return ps.NearestSeam(pos, within, out seam, out _, out _);
        }

        // A plate carries its riders away from a spreading seam at several units per second, so a cone would
        // surface 10-20 units off the rift that made it and be 15 away seconds later. Volcanoes are therefore
        // held on the seam of their two plates (snap onto the closest point of the curved seam PlateSystem draws,
        // PlateSystem.ClosestOnSeam; they still travel with the seam and along it): while they rise, and with
        // holdAfterEmergence for good, so the cones keep marking the boundary.
        // Borders only exist around the focus; a cone that was out of that window comes back at seamReturnSpeed.
        void HoldOnSeam(float dt)
        {
            if (_anchors.Count == 0) return;
            var ps = PlateSystem.Instance;
            if (ps == null) return;
            for (int i = 0; i < _live.Count; i++)
            {
                var island = _live[i];
                if (island == null || !_anchors.TryGetValue(island, out var anchor)) continue;
                if (!island.IsEmerging && !holdAfterEmergence) { _anchors.Remove(island); continue; }

                Vector2 pos = island.PlanarPosition;
                foreach (var b in ps.Borders)
                {
                    bool same = (b.a.cell == anchor.a && b.b.cell == anchor.b) || (b.a.cell == anchor.b && b.b.cell == anchor.a);
                    if (!same) continue;
                    // Closest point only: clamping to the border would shove every cone of a shrinking border
                    // into its triple junction. Past the ends the cone simply rides its plate for a while.
                    if (!ps.ClosestOnSeam(b, pos, out float along, out Vector2 on)) break;
                    float off = Vector2.Distance(on, pos);
                    Vector2 dir = ps.SeamTangent(b, along);
                    // Along the seam a cone rides whichever plate is nearer, so two cones of one arc would
                    // shear past each other on a transform component: make them all travel with the seam.
                    Vector2 mean = (b.a.velocity + b.b.velocity) * 0.5f;
                    on += dir * (Vector2.Dot(mean - island.PlanarVelocity, dir) * dt);
                    if (off > 80f) _anchors.Remove(island);
                    else if (off > seamSnapDistance) island.SetPlanarPosition(Vector2.MoveTowards(pos, on, seamReturnSpeed * dt));
                    else island.SetPlanarPosition(on);
                    break;
                }
            }
        }

        Island Create(Vector2 pos, float yaw, float radius, int shapeSeed)
        {
            var go = new GameObject($"Volcano_{shapeSeed & 0xFFFF:X4}");
            go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(pos.x, 0f, pos.y);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            var island = go.AddComponent<Island>();
            island.useKeyboardInput = false;
            island.isVolcano = true;
            island.landRadius = radius;
            island.shapeSeed = shapeSeed;
            go.AddComponent<Drift.Life.IslandLifeSystem>().seed = shapeSeed;
            go.AddComponent<Drift.Life.IslandHerdSystem>().seed = shapeSeed;
            go.AddComponent<Drift.Life.IslandCrittersSystem>().seed = shapeSeed;
            go.AddComponent<Drift.Life.IslandSettlementSystem>().seed = shapeSeed;
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = islandMaterial;
            go.SetActive(true);
            return island;
        }

        public List<IslandSaveData> Capture(out float cooldownRemaining)
        {
            Prune();
            cooldownRemaining = CooldownRemaining;
            var list = new List<IslandSaveData>(_live.Count);
            foreach (var island in _live)
            {
                var d = island.Capture();
                d.slotKey = 0;
                d.emergeRemaining = EmergeRemaining(island);
                d.life = IslandSaveUtil.CaptureLife(island, false);
                list.Add(d);
            }
            return list;
        }

        public void Restore(List<IslandSaveData> saved, float cooldownRemaining)
        {
            Clear();
            _rnd ??= new System.Random(seed);
            _cooldownLeft = Mathf.Max(0f, cooldownRemaining);
            if (saved == null || islandMaterial == null) return;
            foreach (var d in saved)
            {
                if (d == null || !d.HasShape) continue;
                var island = Create(new Vector2(d.posX, d.posZ), d.yaw, d.landRadius, d.shapeSeed);
                IslandSaveUtil.ApplySaved(island, d);
                if (d.emergeRemaining > 0f && d.sink > 0f) ResumeEmergence(island, d.emergeRemaining, d.sink);
                // Anchors are not saved: a held cone sits on its seam, so the nearest border is the right one.
                if (FindSeam(island.PlanarPosition, 8f, out var seam))
                    _anchors[island] = new SeamAnchor { a = seam.a.cell, b = seam.b.cell };
                _live.Add(island);
            }
        }

        public void Clear()
        {
            for (int i = _live.Count - 1; i >= 0; i--) Remove(i);
            _anchors.Clear();
        }

        public float EmergeRemaining(Island island) => island != null ? island.EmergeRemaining : 0f;

        void ResumeEmergence(Island island, float remaining, float sink) => island.BeginEmergence(remaining, sink);

        void Prune()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                if (_live[i] == null) { _anchors.Remove(_live[i]); _live.RemoveAt(i); continue; }
                if (player == null) continue;
                if (Vector2.Distance(_live[i].PlanarPosition, player.PlanarPosition) > unloadDistance) Remove(i);
            }
        }

        void Remove(int index)
        {
            var island = _live[index];
            _live.RemoveAt(index);
            if (island == null) return;
            _anchors.Remove(island);
            island.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(island.gameObject);
            else DestroyImmediate(island.gameObject);
        }
    }
}
