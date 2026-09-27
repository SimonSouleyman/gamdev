using System.Collections.Generic;
using System.IO;
using Drift.Core;
using Drift.Islands;
using Drift.Life;
using Drift.SaveSystem;
using Drift.Tectonics;
using Drift.Visuals;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace Drift.EditorTools.Playtest
{
    // Watches a running game: every Moments / world-event / encounter report with where it was on screen, frame
    // cost, feel numbers (optic flow, camera jitter) and, in the race, every obstacle the player passes.
    public class PlaytestRecorder
    {
        // A cozy event counts as "noticed" when it is on screen and its subject is at least this tall in pixels.
        public const float NoticePx = 20f;

        public readonly PlaytestResult result;
        public Island player;
        public Camera cam;
        public float time;

        string _phase = "";
        PhaseAcc _acc;
        readonly List<float> _noticedTimes = new();
        readonly Dictionary<string, int> _noticedKinds = new();
        int _allEvents;

        ProfilerRecorder _main, _gc, _batches, _setPass, _tris;
        bool _recorders;

        Vector3 _flowWorld;
        bool _hasFlow;
        Vector3 _lastFwd;
        float _lastAngVel;
        bool _hasRot;
        float _memT;

        // Race bookkeeping.
        readonly Dictionary<int, float> _along = new();
        readonly List<PendingPass> _pending = new();
        LevelAcc _level;
        int _hits0, _dodges0, _flotsam0;

        public PlaytestRecorder(PlaytestResult result) { this.result = result; }

        public void Start()
        {
            Moments.Seen += OnMoment;
            WorldEvents.Started += OnWorldEvent;
            Encounters.Started += OnEncounter;
            Encounters.CompanionJoined += OnCompanion;
            Island.Merged += OnMerged;
            Milestones.Celebrated += OnMilestone;
            RingWorld.Dodged += OnDodged;
            _main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            _gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 1);
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 1);
            _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1);
            _recorders = true;
        }

        public void Stop()
        {
            EndPhase();
            EndLevel();
            Moments.Seen -= OnMoment;
            WorldEvents.Started -= OnWorldEvent;
            Encounters.Started -= OnEncounter;
            Encounters.CompanionJoined -= OnCompanion;
            Island.Merged -= OnMerged;
            Milestones.Celebrated -= OnMilestone;
            RingWorld.Dodged -= OnDodged;
            if (_recorders)
            {
                _main.Dispose(); _gc.Dispose(); _batches.Dispose(); _setPass.Dispose(); _tris.Dispose();
                _recorders = false;
            }
        }

        // ------------------------------------------------------------------ phases

        public void BeginPhase(string name)
        {
            EndPhase();
            _phase = name;
            _acc = new PhaseAcc { r = new PhaseResult { name = name, t0 = time } };
            _noticedTimes.Clear();
            _noticedKinds.Clear();
            _allEvents = 0;
            _hasFlow = false;
            _hasRot = false;
            if (player != null) _acc.r.landArea0 = player.LandArea;
        }

        public void EndPhase()
        {
            if (_acc == null) return;
            var r = _acc.r;
            r.t1 = time;
            r.allEvents = _allEvents;
            r.noticed = _noticedTimes.Count;
            GapStats(_noticedTimes, r.t0, r.t1, out r.medianGap, out r.maxGap, out r.windowsCovered);
            var kinds = new List<string>();
            foreach (var kv in _noticedKinds) kinds.Add(kv.Key + ":" + kv.Value);
            kinds.Sort();
            r.noticedByKind = string.Join(" ", kinds);
            int n = Mathf.Max(1, _acc.frames);
            r.frames = _acc.frames;
            r.avgMs = _acc.ms / n;
            r.avgMainMs = _acc.mainMs / n;
            r.gcKbPerFrame = _acc.gcBytes / n / 1024f;
            r.batches = _acc.batches / n;
            r.setPass = _acc.setPass / n;
            r.trisK = _acc.tris / n / 1000f;
            r.avgSpeed = _acc.speed / n;
            r.avgTrackSpeed = _acc.trackSpeed / n;
            r.opticFlow = _acc.flowN > 0 ? _acc.flow / _acc.flowN : 0f;
            r.cameraJitter = _acc.jitterN > 0 ? _acc.jitter / _acc.jitterN : 0f;
            _acc.frameMs.Sort();
            if (_acc.frameMs.Count > 0)
            {
                r.p95Ms = _acc.frameMs[Mathf.Min(_acc.frameMs.Count - 1, Mathf.FloorToInt(_acc.frameMs.Count * 0.95f))];
                r.maxMs = _acc.frameMs[_acc.frameMs.Count - 1];
            }
            if (player != null) r.landArea1 = player.LandArea;
            if (!_acc.viewTaken) SnapshotView();
            result.phases.Add(r);
            _acc = null;
            _phase = "";
        }

        // Zoom and on-screen sizes of the phase; the runner takes it mid-phase (the end may already be the next setup).
        public void SnapshotView()
        {
            if (_acc == null) return;
            _acc.viewTaken = true;
            if (cam != null && player != null)
            {
                _acc.r.pxPerUnitAtPlayer = PxPerUnit(player.transform.position);
                _acc.r.animalPx = NearestAnimalPx();
            }
            var chase = cam != null ? cam.GetComponent<IslandChaseCamera>() : null;
            if (chase != null) _acc.r.zoom = chase.Zoom;
        }

        public PhaseResult Last => result.phases.Count > 0 ? result.phases[result.phases.Count - 1] : null;

        // Gaps between event times inside [t0, t1], the phase edges included; windowsCovered = share of the
        // 10 s windows that hold at least one event.
        public static void GapStats(List<float> times, float t0, float t1, out float median, out float max, out float covered)
        {
            var gaps = new List<float>();
            float prev = t0;
            foreach (var t in times) { gaps.Add(t - prev); prev = t; }
            gaps.Add(t1 - prev);
            gaps.Sort();
            median = gaps[gaps.Count / 2];
            max = gaps[gaps.Count - 1];
            int windows = Mathf.Max(1, Mathf.FloorToInt((t1 - t0) / 10f));
            int hit = 0;
            for (int w = 0; w < windows; w++)
            {
                float a = t0 + w * 10f, b = a + 10f;
                foreach (var t in times) if (t >= a && t < b) { hit++; break; }
            }
            covered = hit / (float)windows;
        }

        // ------------------------------------------------------------------ per frame

        public void Frame(float dt, float realDt)
        {
            time += dt;
            if (_acc == null) return;
            var a = _acc;
            a.frames++;
            float ms = realDt * 1000f;
            a.ms += ms;
            a.frameMs.Add(ms);
            if (_recorders)
            {
                a.mainMs += _main.LastValue * 1e-6f;
                a.gcBytes += _gc.LastValue;
                a.batches += _batches.LastValue;
                a.setPass += _setPass.LastValue;
                a.tris += _tris.LastValue;
            }
            if (player != null)
            {
                a.speed += player.PlanarVelocity.magnitude;
                a.trackSpeed += player.AdventureRacing ? player.TrackSpeed : 0f;
            }
            SampleFlow(dt);
            SampleCamera(dt);
            SampleRace(dt);
            _memT -= realDt;
        }

        void SampleFlow(float dt)
        {
            if (cam == null || dt <= 0f) return;
            float h = cam.pixelHeight;
            if (_hasFlow)
            {
                Vector3 s = cam.WorldToScreenPoint(_flowWorld);
                if (s.z > 0f)
                {
                    Vector2 d = (Vector2)s - new Vector2(cam.pixelWidth * 0.5f, h * 0.3f);
                    float flow = d.magnitude / h / dt;
                    if (flow < 20f) { _acc.flow += flow; _acc.flowN++; if (_level != null) { _level.flow += flow; _level.flowN++; } }
                }
            }
            var ray = cam.ScreenPointToRay(new Vector3(cam.pixelWidth * 0.5f, h * 0.3f, 0f));
            _hasFlow = false;
            if (ray.direction.y < -1e-3f)
            {
                float k = -ray.origin.y / ray.direction.y;
                _flowWorld = ray.origin + ray.direction * k;
                _hasFlow = true;
            }
        }

        void SampleCamera(float dt)
        {
            if (cam == null || dt <= 0f) return;
            Vector3 fwd = cam.transform.forward;
            if (_hasRot)
            {
                // atan2(|cross|, dot) stays exact for the tiny per-frame angles Quaternion.Angle rounds to 0.
                float angVel = Mathf.Atan2(Vector3.Cross(_lastFwd, fwd).magnitude, Vector3.Dot(_lastFwd, fwd)) * Mathf.Rad2Deg / dt;
                _acc.jitter += Mathf.Abs(angVel - _lastAngVel);
                _acc.jitterN++;
                _lastAngVel = angVel;
            }
            _lastFwd = fwd;
            _hasRot = true;
        }

        public bool MemoryDue => _memT <= 0f;

        public void SampleMemory(float every, bool countObjects)
        {
            _memT = every;
            var m = new MemorySample
            {
                t = time,
                totalMb = Profiler.GetTotalAllocatedMemoryLong() / 1048576f,
                monoMb = Profiler.GetMonoUsedSizeLong() / 1048576f,
                gfxMb = Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576f,
                islands = Island.All.Count,
                gameObjects = countObjects ? Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length : -1,
            };
            string save = Path.Combine(Application.persistentDataPath, "drift_save.json");
            m.saveKb = File.Exists(save) ? new FileInfo(save).Length / 1024f : 0f;
            result.memory.Add(m);
        }

        // ------------------------------------------------------------------ events

        static float SubjectSize(string kind) =>
            System.Enum.TryParse<MomentKind>(kind, out var k) ? Moments.SubjectSize(k) : 4f;

        public float PxPerUnit(Vector3 world)
        {
            if (cam == null) return 0f;
            float d = Vector3.Distance(cam.transform.position, world);
            return cam.pixelHeight / (2f * Mathf.Max(0.01f, d) * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
        }

        float NearestAnimalPx()
        {
            var herds = player != null ? player.GetComponent<IslandHerdSystem>() : null;
            if (herds == null || herds.HerdCount == 0) return 0f;
            float best = 0f;
            for (int h = 0; h < herds.HerdCount; h++)
            {
                Vector3 w = player.transform.TransformPoint(herds.AnimalLocalPosition(h, 0));
                Vector3 s = cam.WorldToViewportPoint(w);
                if (s.z <= 0f || s.x < 0f || s.x > 1f || s.y < 0f || s.y > 1f) continue;
                best = Mathf.Max(best, PxPerUnit(w) * 0.5f);
            }
            return best;
        }

        public void Record(string kind, Vector3 world, bool alwaysNoticed = false)
        {
            var e = new EventRec { t = time, phase = _phase, kind = kind, x = world.x, y = world.y, z = world.z };
            if (cam != null)
            {
                Vector3 v = cam.WorldToViewportPoint(world);
                e.dist = Vector3.Distance(cam.transform.position, world);
                e.onScreen = v.z > 0f && v.x > 0.02f && v.x < 0.98f && v.y > 0.02f && v.y < 0.98f;
                e.px = SubjectSize(kind) * PxPerUnit(world);
            }
            e.noticed = alwaysNoticed || (e.onScreen && e.px >= NoticePx);
            if (_acc == null) return;
            _allEvents++;
            if (e.noticed)
            {
                _noticedTimes.Add(time);
                _noticedKinds.TryGetValue(kind, out int c);
                _noticedKinds[kind] = c + 1;
            }
            if (result.events.Count < 20000 && (e.noticed || e.onScreen)) result.events.Add(e);
        }

        void OnMoment(MomentKind k, Vector3 w) => Record(k.ToString(), w);
        void OnWorldEvent(WorldEventKind k, Vector3 w) => Record("World:" + k, w, true);
        void OnEncounter(EncounterKind k, Vector3 w) => Record("Encounter:" + k, w);
        void OnCompanion(CompanionKind k, Vector3 w) => Record("Companion:" + k, w);
        void OnMilestone(Milestone m) => Record("Milestone:" + m, player != null ? player.transform.position : Vector3.zero, true);

        void OnMerged(Island host, Island guest, float speed)
        {
            if (_acc != null && host == player) _acc.r.merges++;
            if (host == player) Record("Merge", host.transform.position, true);
        }

        void OnDodged(Island isl) { }

        // ------------------------------------------------------------------ race

        class PendingPass { public float t; public float gap; }

        public void BeginLevel(int level)
        {
            EndLevel();
            _level = new LevelAcc { r = new LevelResult { level = level, t0 = time } };
            _hits0 = AdventureRunStats.Hits;
            _dodges0 = AdventureRunStats.Dodges;
            _flotsam0 = AdventureRunStats.Flotsam;
        }

        public void EndLevel()
        {
            if (_level == null) return;
            var r = _level.r;
            r.t1 = time;
            int n = Mathf.Max(1, _level.frames);
            r.avgTrackSpeed = _level.trackSpeed / n;
            r.opticFlow = _level.flowN > 0 ? _level.flow / _level.flowN : 0f;
            r.boostShare = _level.boost / n;
            r.surfShare = _level.surf / n;
            r.staggerShare = _level.stagger / n;
            r.hits = AdventureRunStats.Hits - _hits0;
            r.dodges = AdventureRunStats.Dodges - _dodges0;
            r.flotsam = AdventureRunStats.Flotsam - _flotsam0;
            float dur = Mathf.Max(1f, r.t1 - r.t0);
            r.decisionsPer10s = (r.threats + r.pickups) * 10f / dur;
            result.levels.Add(r);
            _level = null;
        }

        public int CurrentLevel => _level != null ? _level.r.level : 0;

        static float LandRadius(Island i) => i.BoundingRadius;

        void SampleRace(float dt)
        {
            if (_level == null || player == null || !player.AdventureRacing) return;
            var ring = RingWorld.Active;
            if (ring == null) return;
            var r = _level.r;
            _level.frames++;
            _level.trackSpeed += player.TrackSpeed;
            _level.boost += player.Boosting ? 1f : 0f;
            _level.surf += player.SurfStrength > 0.3f ? 1f : 0f;
            _level.stagger += player.StaggerFactor < 0.99f ? 1f : 0f;

            var geo = ring.Geometry;
            Vector2 p = player.PlanarPosition;
            float sign = player.TrackDirection.y >= 0f ? 1f : -1f;
            float pr = LandRadius(player);

            // Near misses are only confirmed if no hit follows within half a second.
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (AdventureRunStats.Hits != _pendingHits) { _pending.Clear(); break; }
                if (time - _pending[i].t > 0.5f) { r.nearMisses++; _pending.RemoveAt(i); }
            }
            _pendingHits = AdventureRunStats.Hits;

            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (o == null || o == player || o.LandArea <= 0f) continue;
                Pass(o.GetHashCode(), o.PlanarPosition, LandRadius(o), false, geo, p, sign, pr);
            }
            if (_ships == null) _ships = Object.FindAnyObjectByType<ShipSystem>();
            var ships = _ships;
            if (ships != null)
            {
                for (int i = 0; i < ships.ShipSlots; i++)
                    if (ships.ShipActive(i)) Pass(100000 + i, ships.ShipPosition(i), 2f, false, geo, p, sign, pr);
                for (int i = 0; i < ships.FlotsamSlots; i++)
                    if (ships.FlotsamActive(i) && ShipSystem.IsCollectible(ships.FlotsamKindOf(i)))
                        Pass(200000 + i, ships.FlotsamPosition(i), 0.5f, true, geo, p, sign, pr);
            }
            var storms = StormSystem.Instance;
            if (storms != null)
                for (int i = 0; i < storms.Storms.Count; i++)
                {
                    var s = storms.Storms[i];
                    if (s.IsRing) Pass(300000 + s.slot, s.center, s.radius * 0.5f, false, geo, p, sign, pr);
                }
        }

        int _pendingHits;
        ShipSystem _ships;

        void Pass(int id, Vector2 pos, float radius, bool pickup, RingGeometry geo, Vector2 p, float sign, float pr)
        {
            float along = geo.AlongDelta(pos.y, p.y) * sign;
            if (_along.TryGetValue(id, out float before) && before > 0f && along <= 0f && before < 30f)
            {
                float gap = Mathf.Abs(pos.x - p.x) - radius - pr;
                if (pickup)
                {
                    if (gap < 6f) _level.r.pickups++;
                }
                else if (gap < 6f)
                {
                    _level.r.threats++;
                    if (gap > 0f && gap < 1.5f) _pending.Add(new PendingPass { t = time, gap = gap });
                }
            }
            _along[id] = along;
        }

        class PhaseAcc
        {
            public PhaseResult r;
            public int frames;
            public float ms, mainMs, gcBytes, batches, setPass, tris;
            public float speed, trackSpeed, flow, jitter;
            public int flowN, jitterN;
            public bool viewTaken;
            public readonly List<float> frameMs = new();
        }

        class LevelAcc
        {
            public LevelResult r;
            public int frames;
            public float trackSpeed, flow, boost, surf, stagger;
            public int flowN;
        }
    }
}
