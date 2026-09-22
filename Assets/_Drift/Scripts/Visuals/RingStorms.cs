using Drift.Islands;
using Drift.Tectonics;
using UnityEngine;

namespace Drift.Visuals
{
    // Storms on the adventure track. They are soft obstacles, not walls: driving through one only pushes the island
    // off its line (the swirling gust goes into Island's carried velocity, never into its own speed), and the dark
    // cloud mass, the rain and the rough water of StormVisuals / Drift/Storm make them readable long before you
    // reach them. RingStormPlan keeps a lane of clear water open beside every one of them, so a line around it
    // always exists.
    //
    // The ring is closed, so a storm never has to blow over: a fixed set of slots is spread along the band and a
    // storm you dodged comes round again. The longer a run lasts, the more slots are filled (RingWorld.Level).
    // Lightning inside these storms is StormVisuals' (it is what actually touches the island).
    [ExecuteAlways]
    [DefaultExecutionOrder(130)]
    public class RingStorms : MonoBehaviour
    {
        [Header("Stürme auf der Bahn (Abenteuer)")]
        [Tooltip("Stürme auf den Ring legen. Aus = nur die Zufallsstürme aus Plattenereignissen.")]
        public bool onTrack = true;
        [Tooltip("So viele Stürme liegen auf Stufe 1 auf der Bahn.")]
        [Range(0f, 4f)] public float stormsAtFirstLevel = 1f;
        [Tooltip("So viele Stürme kommen je weiterer Stufe dazu (0,5 = jede zweite Stufe einer).")]
        [Range(0f, 2f)] public float stormsPerLevel = 0.5f;
        [Tooltip("Mehr als so viele Stürme liegen nie gleichzeitig auf der Bahn (Wasser und Wolken kennen höchstens vier).")]
        [Range(0, 4)] public int maxStorms = 3;
        [Tooltip("Radius eines Sturms in Einheiten – so weit reichen Wolken, Regen und Wind.")]
        [Range(8f, 60f)] public float stormRadius = 16f;
        [Tooltip("So viel freies Wasser bleibt neben jedem Sturm quer zur Bahn: hier fährt man vorbei.")]
        [Range(4f, 60f)] public float freeLane = 16f;
        [Tooltip("Stärke eines Sturms (0..1): Wind, Wolkendichte und rauhes Wasser.")]
        [Range(0.2f, 1f)] public float strength = 0.85f;
        [Tooltip("Wie stark der Wind um das Auge kreist (1 = ganz im Kreis): so schiebt der Sturm quer, statt nur in eine Richtung.")]
        [Range(0f, 1f)] public float swirl = 1f;
        [Tooltip("Ein Sturm, der später dazukommt, taucht erst so weit (Anteil des Umfangs) vom Spieler entfernt auf.")]
        [Range(0.1f, 0.5f)] public float spawnDistance = 0.3f;
        [Tooltip("Solange der Ring läuft, keine zusätzlichen Zufallsstürme aus Plattenereignissen.")]
        public bool onlyRingStorms = true;
        public int seed = 5231;

        // Storms lying on the track right now, and how many the level asks for.
        public int Active { get; private set; }
        public int Wanted { get; private set; }

        int _fill = int.MinValue;
        int _runSeed;
        float _originZ;
        bool _applied;

        void OnDisable() => Release();

        void Update() => Step();

        void Release()
        {
            var storms = StormSystem.Instance;
            if (storms != null && _applied)
            {
                storms.ClearRingStorms();
                storms.suppressEventStorms = false;
            }
            _applied = false;
            _fill = int.MinValue;
            Active = 0;
            Wanted = 0;
        }

        public void Step()
        {
            var storms = StormSystem.Instance;
            var ring = RingWorld.Active;
            if (storms == null) return;
            if (ring == null || !onTrack || !isActiveAndEnabled) { Release(); return; }

            storms.suppressEventStorms = onlyRingStorms;
            _applied = true;
            RingGeometry g = ring.Geometry;
            float refZ = ring.ReferenceZ();
            bool immediate = false;
            if (_fill != ring.FillCount)
            {
                storms.ClearRingStorms();
                _fill = ring.FillCount;
                unchecked { _runSeed = seed * 73856093 ^ _fill * 19349663; }
                _originZ = refZ;
                immediate = true;
            }

            int slots = Mathf.Clamp(maxStorms, 0, StormVisuals.MaxStorms);
            Wanted = RingStormPlan.CountForLevel(ring.Level, stormsAtFirstLevel, stormsPerLevel, slots);
            int live = 0;
            for (int i = 0; i < slots; i++)
            {
                if (i >= Wanted)
                {
                    storms.RemoveRingStorm(i);
                    continue;
                }
                RingStormPlan.Place(i, slots, g.circumference, g.centerX, g.halfWidth, stormRadius, freeLane, _runSeed,
                                    out float along, out float x, out float radius);
                var at = new Vector2(x, g.WrapNear(_originZ + along, refZ));
                if (storms.HasRingSlot(i)) storms.SetRingStormCenter(i, at);
                else if (immediate || Mathf.Abs(g.AlongDelta(at.y, refZ)) >= spawnDistance * g.circumference)
                    storms.StartRingStorm(at, radius, strength, swirl, i);
                if (storms.HasRingSlot(i)) live++;
            }
            Active = live;
        }
    }
}
