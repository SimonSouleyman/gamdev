using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Drift.Islands;
using Drift.Life;
using Drift.Tectonics;
using UnityEngine;

namespace Drift.SaveSystem
{
    [Serializable]
    public class SaveGame
    {
        public const int CurrentVersion = 6;
        // Version 2 files are still read; they carry no seed, so the scene's own per-system seeds stay in effect.
        // Version 3 files carry no run stats; they continue with zeroed stats. Version 4 files carry no journal.
        // Version 5 files carry the 11-species journal; DiscoveryJournal.Restore reads it as "seen" entries.
        public const int MinReadableVersion = 2;

        public int version = CurrentVersion;
        // The WorldStreamer layout the world was planned in (WorldGenVersion, or LegacyWorldGenVersion for a world
        // started before the small world). No initialiser on purpose: JsonUtility keeps initialisers for keys a file
        // lacks, and an old file must read 0 (= a layout that no longer exists, not continuable).
        public int worldGenVersion;
        public long savedUtcTicks;
        public int worldSeed;
        public bool legacySeeds;
        public SessionStats stats = new SessionStats();
        public JournalSaveData journal;
        public IslandSaveData player;
        public LifeSaveData life;
        public PlateSaveData plates;
        public long[] consumed;
        public float startX, startZ;
        public List<IslandSaveData> aiIslands = new();
        public List<IslandSaveData> volcanoes = new();
        public float volcanoCooldown;
        // Bit per Milestone reached in this run. An older file has no such key and reads 0; the milestones are
        // then derived from stats.islandsAbsorbed alone (Milestones.Restore), so no version bump is needed.
        public int milestones;
        // The run's species pool (Drift.Life.SpeciesPool.Mask, bit per LifeKind); 0 in an older file, which then keeps
        // the pool GameSession chose for its seed.
        public long speciesPool;
    }

    public class SaveManager : MonoBehaviour
    {
        public Island player;
        public WorldStreamer streamer;
        public GameSession session;
        public VolcanoSpawner volcanoSpawner;
        public bool loadOnStart = true;
        public float autosaveInterval = 20f;
        public float maxOfflineLifeSeconds = 1800f;
        public string fileName = "drift_save.json";

        float _timer;
        int _savedFrame = -1;
        string _validPath;
        long _validTicks = -1, _validLength = -1;
        bool _validResult;

        public string SavePath => Path.Combine(Application.persistentDataPath, fileName);
        long _lastSaveBytes;
        public long LastSaveBytes => Interlocked.Read(ref _lastSaveBytes);
        // The write of the last autosave, running off the main thread. Every later write chains onto it, and
        // everything that reads or deletes the file (Load, DeleteSave, HasValidSave) waits for it first.
        Task _write;
        public int LoadedWorldSeed { get; private set; }
        public bool LoadedLegacySeeds { get; private set; } = true;
        // The run stats of the last successful Load; GameSession.ContinueGame starts Playing from them.
        public SessionStats LoadedStats { get; } = new SessionStats();
        // The run's collection album (filled by the watch tools); saved with the run, empty for pre-v5 files.
        public DiscoveryJournal Journal { get; } = new DiscoveryJournal();
        // The milestone mask of the last successful Load (0 for a file from before the milestones).
        public int LoadedMilestones { get; private set; }

        // A save that Load() would accept: exists, parses, has a player heightfield and a readable version.
        // Cached on the file's size + write time so the title screen can poll it cheaply.
        public bool HasValidSave
        {
            get
            {
                WaitForWrite();
                string path = SavePath;
                if (!File.Exists(path)) { _validTicks = -1; return false; }
                var info = new FileInfo(path);
                long ticks = info.LastWriteTimeUtc.Ticks;
                if (path == _validPath && ticks == _validTicks && info.Length == _validLength) return _validResult;
                _validPath = path;
                _validTicks = ticks;
                _validLength = info.Length;
                _validResult = Peek(path, out _, out _);
                return _validResult;
            }
        }

        public static bool Peek(string path, out int worldSeed, out bool legacySeeds)
        {
            worldSeed = 0;
            legacySeeds = true;
            try
            {
                var data = JsonUtility.FromJson<SaveGame>(File.ReadAllText(path));
                if (!Readable(data)) return false;
                ReadSeed(data, out worldSeed, out legacySeeds);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static bool Readable(SaveGame data)
        {
            if (data == null || data.player == null || data.player.heights == null || data.player.heights.Length == 0) return false;
            return data.version >= SaveGame.MinReadableVersion && WorldStreamer.IsKnownLayout(data.worldGenVersion);
        }

        static void ReadSeed(SaveGame data, out int worldSeed, out bool legacySeeds)
        {
            legacySeeds = data.version < 3 || data.legacySeeds;
            worldSeed = legacySeeds ? 0 : data.worldSeed;
        }

        void Start()
        {
            if (!Application.isPlaying) return;
            Resolve();
            if (loadOnStart) Load();
        }

        void Update()
        {
            if (!Application.isPlaying || autosaveInterval <= 0f) return;
            _timer += Time.unscaledDeltaTime;
            if (_timer >= autosaveInterval)
            {
                _timer = 0f;
                Save();
            }
        }

        // The OS may suspend or kill the process right after these, so they write before returning.
        void OnApplicationPause(bool paused)
        {
            if (paused) SaveBlocking();
        }

        void OnApplicationQuit()
        {
            SaveBlocking();
        }

        void OnDisable() => WaitForWrite();

        void Resolve()
        {
            if (player == null)
                foreach (var i in Island.All) if (i != null && i.useKeyboardInput) { player = i; break; }
            if (streamer == null) streamer = FindAnyObjectByType<WorldStreamer>();
            if (volcanoSpawner == null) volcanoSpawner = FindAnyObjectByType<VolcanoSpawner>();
        }

        // Autosave, "Zum Titel", backgrounding: the capture runs here on the main thread (about 1 ms even with
        // 70 changed islands), the JSON text and the file write run on a worker. Serialising and writing a
        // ~600 KB file used to stall the frame for 20 ms on desktop - a visible hitch every 20 s on a phone.
        public bool Save() => Save(false);

        // Same, but the file is complete on disk when this returns.
        public bool SaveBlocking() => Save(true);

        // Backgrounding fires GameSession's explicit save and this component's OnApplicationPause in the same
        // frame (and a quit can follow a pause); the world cannot change in between, so one write is enough.
        bool Save(bool blocking)
        {
            Resolve();
            if (session == null) session = FindAnyObjectByType<GameSession>();
            if (session != null && !session.SavingAllowed) return false;
            // Adventure runs are never continued, so nothing of them is written (autosave, pause or quit).
            if (Drift.Core.GameModes.IsAdventure) return false;
            if (player == null || player.IsSunk) return false;
            if (_savedFrame == Time.frameCount && (_write != null || File.Exists(SavePath)))
            {
                if (blocking) WaitForWrite();
                return true;
            }
            SaveGame data;
            try
            {
                data = Capture();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"SaveManager: save failed: {e.Message}");
                return false;
            }
            _savedFrame = Time.frameCount;
            // LoadedStats has to describe the file on disk: "Zum Titel" saves and then continues from the
            // world still in memory without a Load, and took its stats from here - the run's counters
            // (islands absorbed, time, peak size) came back as whatever the app had loaded at start-up.
            if (session != null) LoadedStats.CopyFrom(session.Stats);

            // The captured SaveGame is only read from here on: every Capture() hands out fresh arrays.
            string path = SavePath;
            if (blocking)
            {
                WaitForWrite();
                return Write(data, path);
            }
            var previous = _write;
            _write = Task.Run(() =>
            {
                if (previous != null) try { previous.Wait(); } catch (Exception) { }
                Write(data, path);
            });
            return true;
        }

        // Blocks until a background write has finished. Cheap when none is running.
        public void WaitForWrite()
        {
            var t = _write;
            if (t == null) return;
            try { t.Wait(); } catch (Exception) { }
            if (_write == t) _write = null;
        }

        SaveGame Capture()
        {
            var life = player.GetComponent<IslandLifeSystem>();
            var plates = PlateSystem.Instance;
            var data = new SaveGame
            {
                savedUtcTicks = DateTime.UtcNow.Ticks,
                worldGenVersion = streamer != null ? streamer.Layout : WorldStreamer.WorldGenVersion,
                worldSeed = session != null ? session.WorldSeed : LoadedWorldSeed,
                legacySeeds = session != null ? session.LegacySeeds : LoadedLegacySeeds,
                stats = new SessionStats(),
                journal = Journal.Capture(),
                player = player.Capture(),
                life = life != null ? life.Capture() : null,
                plates = plates != null ? plates.Capture() : null,
                startX = streamer != null ? streamer.StartPosition.x : 0f,
                startZ = streamer != null ? streamer.StartPosition.y : 0f,
                milestones = Milestones.Capture(),
                speciesPool = (long)SpeciesPool.Mask,
            };
            // A copy: the live stats keep counting while the worker serialises.
            data.stats.CopyFrom(session != null ? session.Stats : LoadedStats);
            if (streamer != null)
            {
                // CaptureOverrides is what marks a slot whose island was merged this frame as consumed,
                // so the consumed keys have to be read after it or the absorbed island respawns on load.
                data.aiIslands = streamer.CaptureOverrides();
                data.consumed = streamer.ConsumedKeys();
            }
            if (volcanoSpawner != null)
            {
                data.volcanoes = volcanoSpawner.Capture(out float cd);
                data.volcanoCooldown = cd;
            }
            return data;
        }

        // Runs on the worker: JsonUtility and System.IO only, no UnityEngine.Object access.
        bool Write(SaveGame data, string path)
        {
            try
            {
                string json = JsonUtility.ToJson(data);
                string tmp = path + ".tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(tmp, json);
                if (!File.Exists(path)) File.Move(tmp, path);
                else
                {
                    try { File.Replace(tmp, path, null); }
                    catch (IOException)
                    {
                        File.Copy(tmp, path, true);
                        File.Delete(tmp);
                    }
                }
                Interlocked.Exchange(ref _lastSaveBytes, new FileInfo(path).Length);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"SaveManager: save failed: {e.Message}");
                return false;
            }
        }

        public bool Load(double? elapsedOverrideSeconds = null)
        {
            WaitForWrite();
            Resolve();
            if (player == null || !File.Exists(SavePath)) return false;
            try
            {
                var data = JsonUtility.FromJson<SaveGame>(File.ReadAllText(SavePath));
                if (data == null || data.player == null || data.player.heights == null || data.player.heights.Length == 0) return false;
                if (data.version < SaveGame.MinReadableVersion)
                {
                    Debug.Log($"SaveManager: ignoring save of version {data.version} (current {SaveGame.CurrentVersion}, oldest readable {SaveGame.MinReadableVersion})");
                    return false;
                }
                if (!WorldStreamer.IsKnownLayout(data.worldGenVersion))
                {
                    Debug.Log($"SaveManager: ignoring save of world layout {data.worldGenVersion} (current {WorldStreamer.WorldGenVersion})");
                    return false;
                }

                // The seed has to be in place before plates and streamer rebuild, or the consumed slot keys
                // and plate offsets below would refer to a differently planned world.
                ReadSeed(data, out int worldSeed, out bool legacySeeds);
                LoadedWorldSeed = worldSeed;
                LoadedLegacySeeds = legacySeeds;
                LoadedStats.CopyFrom(data.version >= 4 ? data.stats : null);
                Journal.Restore(data.version >= 5 ? data.journal : null);
                LoadedMilestones = data.milestones;
                Milestones.Restore(LoadedMilestones, LoadedStats.islandsAbsorbed);
                if (session == null) session = FindAnyObjectByType<GameSession>();
                if (session != null) session.ApplyWorldSeed(worldSeed, legacySeeds);
                else if (!legacySeeds) WorldSeeds.Apply(worldSeed, player, streamer);
                if (data.speciesPool != 0 && Drift.Core.GameModes.Current == Drift.Core.GameMode.Cozy) SpeciesPool.Restore((ulong)data.speciesPool);

                var plates = PlateSystem.Instance;
                if (plates != null) plates.Restore(data.plates);

                if (streamer != null)
                {
                    streamer.ResetWorld();
                    // An old save keeps its big world; everything new is planned in the small one.
                    streamer.UseLayout(data.worldGenVersion);
                    streamer.RestoreState(new Vector2(data.startX, data.startZ), data.consumed);
                    streamer.RestoreOverrides(data.aiIslands);
                }

                player.Restore(data.player);
                // ChooseSpeciesPool marked the player as the start island (one herd until the first merge); a
                // continued run that has already absorbed islands is past that.
                var herds = player.GetComponent<Drift.Life.IslandHerdSystem>();
                if (herds != null) herds.StartIsland = LoadedStats.islandsAbsorbed == 0;
                var life = player.GetComponent<IslandLifeSystem>();
                if (life != null && data.life != null)
                {
                    life.Restore(data.life);
                    double elapsed = elapsedOverrideSeconds ?? new TimeSpan(DateTime.UtcNow.Ticks - data.savedUtcTicks).TotalSeconds;
                    float lifeSeconds = (float)Math.Min(Math.Max(elapsed, 0.0) * life.timeScale, maxOfflineLifeSeconds);
                    life.CatchUp(lifeSeconds);
                }

                if (volcanoSpawner != null) volcanoSpawner.Restore(data.volcanoes, data.volcanoCooldown);
                if (streamer != null) streamer.StreamAround(true);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"SaveManager: load failed: {e.Message}");
                return false;
            }
        }

        // Only the file: the finale still shows the run's journal and stats after its record is stored.
        public void DeleteSaveFile()
        {
            WaitForWrite();
            _savedFrame = -1;
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }

        public void DeleteSave()
        {
            // An autosave still in flight would otherwise recreate the file right after it was deleted.
            WaitForWrite();
            _savedFrame = -1;
            LoadedStats.Reset();
            Journal.Reset();
            LoadedMilestones = 0;
            Milestones.Reset();
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }
    }
}
