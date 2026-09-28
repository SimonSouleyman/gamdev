using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Drift.Islands
{
    // The islands of the adventure ring: a fixed supply spread around the whole band (the ring is closed, so passed
    // islands come round again). Whatever is merged or sinks is replaced by a new island that rises out of the sea on
    // the far part of the ring, never close to the player, so the ring never runs out. Spawning follows WorldStreamer:
    // the heightfield is built on a worker, the island is created when it is done, and its life systems are switched
    // on one per frame. Driven by RingWorld (Step / ResetRing); islands are DontSave children of this object.
    [ExecuteAlways]
    public class RingIslandSpawner : MonoBehaviour
    {
        public Material islandMaterial;
        public int seed = 9001;

        [Header("Inselvorrat")]
        [Tooltip("So viele größere Inseln liegen immer auf dem Ring.")]
        [Range(1, 40)] public int mainIslands = 12;
        [Tooltip("Dazu so viele Inselchen (Radius 1-2).")]
        [Range(0, 30)] public int islets = 5;
        [Tooltip("Anteil sehr großer Inseln (Radius 9-14).")]
        [Range(0f, 0.5f)] public float largeShare = 0.05f;
        [Tooltip("Anteil großer Inseln (Radius 6-9).")]
        [Range(0f, 0.5f)] public float bigShare = 0.15f;
        [Tooltip("Anteil mittlerer Inseln (Radius 3-6); der Rest ist klein (2-3).")]
        [Range(0f, 1f)] public float mediumShare = 0.45f;
        [Tooltip("Um den Startpunkt bleibt so viel Wasser frei.")]
        public float startExclusion = 24f;
        [Tooltip("Ersatzinseln tauchen frühestens so weit (Anteil des Umfangs) vom Spieler entfernt auf.")]
        [Range(0.1f, 0.5f)] public float respawnMinDistance = 0.3f;
        [Tooltip("Sekunden, in denen eine Ersatzinsel aus dem Meer aufsteigt.")]
        public float emergeDuration = 8f;
        [Tooltip("Abstand der Inselküsten von den Randwänden.")]
        public float wallMargin = 2f;
        [Tooltip("Leben auf den Inseln (Pflanzen, Herden, Kleintiere). Im Abenteuer nur Kulisse.")]
        public bool life = true;
        [Tooltip("Siedlungen auf den Inseln.")]
        public bool settlements = false;
        [Tooltip("Pflanzen, Herden und Kleintiere der Ring-Inseln werden erst ab dieser Entfernung (u) ausgeblendet. Der Ring zeigt die halbe Runde auf einmal – das Leben weit oben im Himmel sieht man ohnehin nicht.")]
        [Range(30f, 260f)] public float lifeHideDistance = 90f;
        public int spawnsPerFrame = 1;

        [Header("Vulkane im Abenteuer")]
        [Tooltip("Kleinster Radius eines Vulkans, der vor der Insel aufsteigt.")]
        [Range(1.5f, 8f)] public float volcanoMinRadius = 2.5f;
        [Tooltip("Größter Radius eines Vulkans, der vor der Insel aufsteigt.")]
        [Range(1.5f, 10f)] public float volcanoMaxRadius = 4.8f;
        [Tooltip("So weit (u) vor der Insel steigt ein Vulkan frühestens auf – nie direkt unter oder neben ihr.")]
        [Range(40f, 300f)] public float volcanoAheadMin = 95f;
        [Tooltip("So weit (u) vor der Insel steigt ein Vulkan höchstens auf.")]
        [Range(40f, 320f)] public float volcanoAheadMax = 150f;
        [Tooltip("Der Vulkan ist ganz oben, bevor die Insel ihm so nahe (u) kommt: das Aufsteigen dauert so lange, wie die Fahrt bis dahin.")]
        [Range(10f, 120f)] public float volcanoRiseDoneGap = 40f;
        [Tooltip("Kürzeste und längste Zeit (s), in der ein Vulkan aus dem Meer steigt.")]
        public Vector2 volcanoRiseSeconds = new Vector2(2.5f, 6f);
        [Tooltip("So breit (u, Küste zu Küste) bleibt neben einem neuen Vulkan mindestens eine Durchfahrt frei – Inseln und Stürme auf gleicher Höhe mitgerechnet.")]
        [Range(6f, 40f)] public float volcanoFreeLane = 14f;

        const string ChildPrefix = "RingIsland_";

        class Entry
        {
            public Island island;
            public bool islet;
            public bool spawned;
            public bool emerge;
            public bool volcano;
            public float rise;
            public Vector2 pos;
            public float radius;
            public IslandArchetype type;
            public int shapeSeed;
            public Task<Island.PrebuiltShape> prebuild;
        }

        readonly List<Entry> _entries = new();
        readonly List<Island> _staging = new();
        System.Random _rnd = new(9001);
        int _serial;

        // Adventure difficulty (RingWorld): this many islands on top of mainIslands, added one by one on the far
        // side of the ring as the run gets longer.
        [System.NonSerialized] public int ExtraIslands;

        public int TargetCount => mainIslands + Mathf.Max(0, ExtraIslands) + islets;
        // Islands on the ring right now (spawned and not merged away).
        public int LiveCount
        {
            get
            {
                int n = 0;
                foreach (var e in _entries) if (e.spawned && !Gone(e.island)) n++;
                return n;
            }
        }
        // Planned or live main islands (the islets are counted apart).
        public int MainCount
        {
            get
            {
                int n = 0;
                foreach (var e in _entries) if (!e.islet && !e.volcano && (!e.spawned || !Gone(e.island))) n++;
                return n;
            }
        }

        // Volcanoes raised ahead of the player this run (TryRaiseVolcano) that are still on the ring.
        public int VolcanoCount
        {
            get
            {
                int n = 0;
                foreach (var e in _entries) if (e.volcano && e.spawned && !Gone(e.island)) n++;
                return n;
            }
        }
        public int VolcanoesRaised { get; private set; }

        public int PendingCount
        {
            get
            {
                int n = 0;
                foreach (var e in _entries) if (!e.spawned) n++;
                return n;
            }
        }
        public int TotalSpawned { get; private set; }

        public void CollectIslands(List<Island> into)
        {
            foreach (var e in _entries) if (e.spawned && !Gone(e.island)) into.Add(e.island);
        }

        static bool Gone(Island i) => i == null || !i.gameObject.activeSelf;

        void OnEnable() => Clear();

        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (!child.name.StartsWith(ChildPrefix)) continue;
                child.SetActive(false);
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
            _entries.Clear();
            _staging.Clear();
        }

        static int Hash(int a, int b)
        {
            unchecked { return (a * 73856093) ^ (b * 19349663) ^ 0x52494E47; }
        }

        // A fresh ring around the player: mainIslands + islets planned evenly spread along the band (one stratum
        // each, jittered), none within startExclusion of `around`. immediate = build everything now (edit mode).
        public void ResetRing(RingGeometry ring, Vector2 around, int worldSeed, bool immediate)
        {
            Clear();
            _rnd = new System.Random(Hash(worldSeed, seed));
            if (islandMaterial == null) islandMaterial = FindIslandMaterial();

            int mains = Mathf.Max(0, mainIslands);
            var radii = new float[mains];
            var types = new IslandArchetype[mains];
            for (int i = 0; i < mains; i++) RollMain(out radii[i], out types[i]);
            var strata = Shuffled(mains);
            // Largest first, so the big ones get their room and the rest fills in around them.
            var order = new int[mains];
            for (int i = 0; i < mains; i++) order[i] = i;
            System.Array.Sort(order, (a, b) => radii[b].CompareTo(radii[a]));
            foreach (int i in order) Plan(ring, around, radii[i], types[i], false, strata[i], mains, false);

            int small = Mathf.Max(0, islets);
            var isletStrata = Shuffled(small);
            for (int i = 0; i < small; i++)
            {
                RollIslet(out float r, out var t);
                Plan(ring, around, r, t, true, isletStrata[i], small, false);
            }
            if (immediate) Step(ring, around, true);
        }

        int[] Shuffled(int n)
        {
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = i;
            for (int i = n - 1; i > 0; i--)
            {
                int k = _rnd.Next(i + 1);
                (a[i], a[k]) = (a[k], a[i]);
            }
            return a;
        }

        // Keeps the supply up: gone islands are re-planned far from the player, finished prebuilds are spawned, and
        // the life systems of fresh islands are switched on one per frame.
        public void Step(RingGeometry ring, Vector2 around, bool immediate)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (!e.spawned || !Gone(e.island)) continue;
                _entries.RemoveAt(i--);
                if (e.volcano) continue;
                RollReplacement(e.islet, out float r, out var t);
                Plan(ring, around, r, t, e.islet, 0, 0, true);
            }

            // The difficulty asked for more obstacles: they rise out of the sea far from the player, like a replacement.
            int wantMain = mainIslands + Mathf.Max(0, ExtraIslands);
            for (int guard = 0; MainCount < wantMain && guard < 4; guard++)
            {
                RollMain(out float r, out var t);
                Plan(ring, around, r, t, false, 0, 0, true);
            }

            if (immediate) FlushStaging();
            else if (StageNext()) return;

            int budget = immediate ? int.MaxValue : Mathf.Max(1, spawnsPerFrame);
            for (int i = 0; i < _entries.Count && budget > 0; i++)
            {
                var e = _entries[i];
                if (e.spawned) continue;
                if (!immediate && e.prebuild != null && !e.prebuild.IsCompleted) continue;
                Spawn(ring, around, e, immediate);
                budget--;
            }
        }

        void RollMain(out float radius, out IslandArchetype type)
        {
            float u = (float)_rnd.NextDouble(), t = (float)_rnd.NextDouble();
            if (u < largeShare) { radius = 9f + 5f * t * t; type = Pick(LargeWeights); }
            else if (u < largeShare + bigShare) { radius = 6f + 3f * t; type = Pick(BigWeights); }
            else if (u < largeShare + bigShare + mediumShare) { radius = 3f + 3f * t; type = Pick(MediumWeights); }
            else { radius = 2f + t; type = Pick(SmallWeights); }
        }

        void RollIslet(out float radius, out IslandArchetype type)
        {
            radius = 1.2f + 0.8f * Mathf.Pow((float)_rnd.NextDouble(), 1.3f);
            type = Pick(IsletWeights);
            if (type == IslandArchetype.Archipelago) radius = Mathf.Max(radius, 1.6f);
        }

        void RollReplacement(bool islet, out float radius, out IslandArchetype type)
        {
            if (islet) RollIslet(out radius, out type);
            else RollMain(out radius, out type);
        }

        // Same size classes and archetype weights as WorldStreamer (Blob, Ridge, Crescent, TwinPeak, Sandbank, Mesa,
        // Archipelago, Stack).
        static readonly int[] IsletWeights = { 35, 0, 0, 0, 20, 0, 15, 30 };
        static readonly int[] SmallWeights = { 30, 16, 0, 10, 14, 12, 10, 8 };
        static readonly int[] MediumWeights = { 26, 16, 15, 14, 6, 10, 13, 0 };
        static readonly int[] BigWeights = { 28, 18, 15, 18, 0, 8, 13, 0 };
        static readonly int[] LargeWeights = { 38, 15, 10, 25, 0, 0, 12, 0 };

        IslandArchetype Pick(int[] weights)
        {
            int total = 0;
            for (int i = 0; i < weights.Length; i++) total += weights[i];
            int roll = _rnd.Next(total);
            for (int i = 0; i < weights.Length; i++)
            {
                roll -= weights[i];
                if (roll < 0) return (IslandArchetype)(i + 1);
            }
            return IslandArchetype.Blob;
        }

        // Initial islands sit in their stratum of the ring; replacements anywhere at least respawnMinDistance of the
        // circumference away from the player (the far side, high up in the sky). Falls back to the best attempt.
        void Plan(RingGeometry ring, Vector2 around, float radius, IslandArchetype type, bool islet, int stratum, int strata, bool replacement)
        {
            float c = ring.circumference;
            float reach = 1.4f * radius;
            float across = Mathf.Max(0f, ring.halfWidth - reach - wallMargin);
            Vector2 best = around;
            float bestGap = float.NegativeInfinity;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                float dz;
                if (replacement)
                {
                    float lo = Mathf.Clamp(respawnMinDistance, 0.05f, 0.5f) * c;
                    dz = Mathf.Lerp(lo, c - lo, (float)_rnd.NextDouble());
                }
                else
                {
                    float cell = c / Mathf.Max(1, strata);
                    dz = (stratum + 0.5f + 0.7f * ((float)_rnd.NextDouble() - 0.5f)) * cell;
                }
                float z = ring.WrapNear(around.y + dz, around.y);
                float x = ring.centerX + across * (2f * (float)_rnd.NextDouble() - 1f);
                var p = new Vector2(x, z);
                if (!replacement && Mathf.Abs(ring.AlongDelta(z, around.y)) < startExclusion + reach && Mathf.Abs(x - around.x) < startExclusion + reach)
                    continue;
                float gap = Clearance(ring, p, radius, islet);
                if (gap >= 0f) { best = p; bestGap = gap; break; }
                if (gap > bestGap) { bestGap = gap; best = p; }
            }
            if (float.IsNegativeInfinity(bestGap)) best = new Vector2(ring.centerX, ring.WrapNear(around.y + c * 0.5f, around.y));

            var e = new Entry { pos = best, radius = radius, type = type, islet = islet, shapeSeed = _rnd.Next(), emerge = replacement };
            if (Application.isPlaying)
            {
                float cell = IslandArchetypes.CellSize(radius);
                int s = e.shapeSeed;
                e.prebuild = Task.Run(() => Island.PrebuiltShape.BuildForStreamed(type, radius, s, cell));
            }
            _entries.Add(e);
        }

        // Smallest distance to the required gap from every other island and planned slot (negative = too close).
        float Clearance(RingGeometry ring, Vector2 p, float radius, bool islet)
        {
            float worst = float.PositiveInfinity;
            foreach (var e in _entries)
            {
                Vector2 q = e.spawned && !Gone(e.island) ? e.island.PlanarPosition : e.pos;
                worst = Mathf.Min(worst, Gap(ring, p, q, radius, e.radius, islet || e.islet));
            }
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var isl = all[i];
                if (isl == null || !isl.isActiveAndEnabled || isl.transform.parent == transform) continue;
                worst = Mathf.Min(worst, Gap(ring, p, isl.PlanarPosition, radius, isl.BoundingRadius / 1.4f, false));
            }
            return worst;
        }

        // ---- volcanoes rising ahead of the player (Adventure difficulty, RingWorld decides when) ----

        readonly List<Vector2> _lane = new();

        // A volcano started rising ahead of the player (HUD hint, rumble, camera) - the island is still under water.
        public static event System.Action<Island> VolcanoRising;

        // Raises one volcano volcanoAheadMin..Max ahead of the player (dir = +-1 along z) so that it is fully up
        // volcanoRiseDoneGap before the player gets there at `speed`. The spot has to keep the ring's usual distance
        // from every island (Clearance), and a free lane of volcanoFreeLane water across the band at its height -
        // islands and ring storms there counted - so a volcano can never close the track. Returns null (and changes
        // nothing) when no admissible spot was found; the caller simply tries again a moment later.
        public Island TryRaiseVolcano(RingGeometry ring, Vector2 player, float dir, float speed, float playerRadius)
        {
            dir = dir >= 0f ? 1f : -1f;
            float lo = Mathf.Max(20f, Mathf.Min(volcanoAheadMin, volcanoAheadMax)), hi = Mathf.Max(lo, volcanoAheadMax);
            float rLo = Mathf.Min(volcanoMinRadius, volcanoMaxRadius), rHi = Mathf.Max(volcanoMinRadius, volcanoMaxRadius);
            for (int attempt = 0; attempt < 12; attempt++)
            {
                float radius = Mathf.Lerp(rLo, rHi, (float)_rnd.NextDouble());
                float reach = VolcanoReach(radius);
                float dz = Mathf.Lerp(lo, hi, (float)_rnd.NextDouble());
                float across = Mathf.Max(0f, ring.halfWidth - reach - wallMargin);
                float x = ring.centerX + across * (2f * (float)_rnd.NextDouble() - 1f);
                var p = new Vector2(x, player.y + dir * dz);
                if (Clearance(ring, p, radius, false) < 0f) continue;
                if (!LaneOpen(ring, p, reach, playerRadius)) continue;

                var e = new Entry
                {
                    pos = p, radius = radius, type = IslandArchetype.Blob, shapeSeed = _rnd.Next(), volcano = true,
                    rise = Mathf.Clamp((dz - volcanoRiseDoneGap) / Mathf.Max(1f, speed), volcanoRiseSeconds.x, Mathf.Max(volcanoRiseSeconds.x, volcanoRiseSeconds.y))
                };
                _entries.Add(e);
                Spawn(ring, player, e, !Application.isPlaying);
                VolcanoesRaised++;
                VolcanoRising?.Invoke(e.island);
                return e.island;
            }
            return null;
        }

        // Coast reach of a volcano cone of this land radius (IslandShape.CreateVolcano's skirt plus a cell).
        static float VolcanoReach(float radius) => radius * 1.2f + 0.5f;

        // Is there still a way past a cone of `reach` at p? Everything whose coast overlaps its stretch of the track
        // (widened by the player island, which has to squeeze past both) blocks its part of the band; the widest
        // gap left, coast to coast, has to be at least volcanoFreeLane and wide enough for the player.
        bool LaneOpen(RingGeometry ring, Vector2 p, float reach, float playerRadius)
        {
            _lane.Clear();
            _lane.Add(new Vector2(ring.MinX + 1f, ring.MaxX - 1f));
            Block(p.x - reach, p.x + reach);
            float window = reach + 2f * Mathf.Max(0f, playerRadius);
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var isl = all[i];
                if (isl == null || !isl.isActiveAndEnabled || isl.useKeyboardInput) continue;
                float r = isl.IsEmerging ? Mathf.Max(isl.BoundingRadius, (isl.isVolcano ? VolcanoReach(isl.landRadius) : isl.landRadius * 1.4f)) : isl.BoundingRadius;
                if (Mathf.Abs(ring.AlongDelta(isl.PlanarPosition.y, p.y)) >= window + r) continue;
                Block(isl.PlanarPosition.x - r, isl.PlanarPosition.x + r);
            }
            var storms = Drift.Tectonics.StormSystem.Instance;
            if (storms != null)
            {
                var list = storms.Storms;
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i];
                    if (!s.IsRing || Mathf.Abs(ring.AlongDelta(s.center.y, p.y)) >= window + s.radius) continue;
                    Block(s.center.x - s.radius, s.center.x + s.radius);
                }
            }
            float need = Mathf.Max(volcanoFreeLane, 2f * Mathf.Max(0f, playerRadius) + 3f);
            for (int i = 0; i < _lane.Count; i++)
                if (_lane[i].y - _lane[i].x >= need) return true;
            return false;
        }

        void Block(float a, float b)
        {
            for (int k = _lane.Count - 1; k >= 0; k--)
            {
                Vector2 s = _lane[k];
                if (b <= s.x || a >= s.y) continue;
                _lane.RemoveAt(k);
                if (a > s.x) _lane.Insert(k, new Vector2(s.x, a));
                if (b < s.y) _lane.Insert(k, new Vector2(b, s.y));
            }
        }

        static float Gap(RingGeometry ring, Vector2 a, Vector2 b, float ra, float rb, bool small)
        {
            float sum = ra + rb;
            float need = small ? sum * 1.4f + 5f : sum * 1.8f + 6f;
            var d = new Vector2(a.x - b.x, ring.AlongDelta(a.y, b.y));
            return d.magnitude - need;
        }

        Material FindIslandMaterial()
        {
            var ws = FindAnyObjectByType<WorldStreamer>(FindObjectsInactive.Include);
            if (ws != null && ws.islandMaterial != null) return ws.islandMaterial;
            var vs = FindAnyObjectByType<VolcanoSpawner>(FindObjectsInactive.Include);
            return vs != null ? vs.islandMaterial : null;
        }

        void Spawn(RingGeometry ring, Vector2 around, Entry e, bool immediate)
        {
            if (islandMaterial == null) islandMaterial = FindIslandMaterial();
            Vector2 pos = new Vector2(e.pos.x, ring.WrapNear(e.pos.y, around.y));
            var go = new GameObject(ChildPrefix + (_serial++));
            go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(pos.x, 0f, pos.y);
            go.transform.rotation = Quaternion.Euler(0f, (e.shapeSeed & 1023) * 0.35f, 0f);

            var island = go.AddComponent<Island>();
            island.useKeyboardInput = false;
            island.isVolcano = e.volcano;
            island.landRadius = e.radius;
            island.archetype = e.type;
            island.cellSize = IslandArchetypes.CellSize(e.radius);
            island.shapeSeed = e.shapeSeed;
            if (e.prebuild != null && e.prebuild.Status == TaskStatus.RanToCompletion) island.Prebuilt = e.prebuild.Result;
            e.prebuild = null;

            bool staged = !immediate;
            if (life)
            {
                var lifeSys = go.AddComponent<Drift.Life.IslandLifeSystem>();
                var herds = go.AddComponent<Drift.Life.IslandHerdSystem>();
                var critters = go.AddComponent<Drift.Life.IslandCrittersSystem>();
                lifeSys.seed = herds.seed = critters.seed = e.shapeSeed;
                // Play test: the ring's culling keeps half the lap on screen, and at the default 260 u every island
                // on it kept its plants and animals drawn.
                lifeSys.hideDistance = herds.hideDistance = critters.hideDistance = lifeHideDistance;
                if (staged) lifeSys.enabled = herds.enabled = critters.enabled = false;
                if (settlements)
                {
                    var settlement = go.AddComponent<Drift.Life.IslandSettlementSystem>();
                    settlement.seed = e.shapeSeed;
                    settlement.hideDistance = lifeHideDistance;
                    if (staged) settlement.enabled = false;
                }
            }
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = islandMaterial;
            go.SetActive(true);
            if (e.volcano) island.BeginEmergence(Mathf.Max(0.1f, e.rise));
            else if (e.emerge && Application.isPlaying) island.BeginEmergence(emergeDuration);
            if (staged && life) _staging.Add(island);

            e.island = island;
            e.spawned = true;
            TotalSpawned++;
        }

        bool StageNext()
        {
            while (_staging.Count > 0)
            {
                var island = _staging[0];
                var next = island != null && island.isActiveAndEnabled ? NextDisabledLife(island) : null;
                if (next == null)
                {
                    _staging.RemoveAt(0);
                    continue;
                }
                next.enabled = true;
                return true;
            }
            return false;
        }

        void FlushStaging()
        {
            foreach (var island in _staging)
            {
                if (island == null) continue;
                for (var next = NextDisabledLife(island); next != null; next = NextDisabledLife(island)) next.enabled = true;
            }
            _staging.Clear();
        }

        static Behaviour NextDisabledLife(Island island)
        {
            Behaviour b = island.GetComponent<Drift.Life.IslandLifeSystem>();
            if (b != null && !b.enabled) return b;
            b = island.GetComponent<Drift.Life.IslandHerdSystem>();
            if (b != null && !b.enabled) return b;
            b = island.GetComponent<Drift.Life.IslandCrittersSystem>();
            if (b != null && !b.enabled) return b;
            b = island.GetComponent<Drift.Life.IslandSettlementSystem>();
            if (b != null && !b.enabled) return b;
            return null;
        }
    }
}
