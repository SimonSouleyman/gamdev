using System;
using System.Collections.Generic;
using System.IO;
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
        // WorldStreamer.WorldGenVersion at save time. No initialiser on purpose: JsonUtility keeps initialisers
        // for keys a file lacks, and an old file must read 0 (= another world layout, not continuable).
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
        public long LastSaveBytes { get; private set; }
        public int LoadedWorldSeed { get; private set; }
        public bool LoadedLegacySeeds { get; private set; } = true;
        // The run stats of the last successful Load; GameSession.ContinueGame starts Playing from them.
        public SessionStats LoadedStats { get; } = new SessionStats();
        // The run's collection album (filled by the watch tools); saved with the run, empty for pre-v5 files.
        public DiscoveryJournal Journal { get; } = new DiscoveryJournal();

        // A save that Load() would accept: exists, parses, has a player heightfield and a readable version.
        // Cached on the file's size + write time so the title screen can poll it cheaply.
        public bool HasValidSave
        {
            get
            {
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
            return data.version >= SaveGame.MinReadableVersion && data.worldGenVersion == WorldStreamer.WorldGenVersion;
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

        void OnApplicationPause(bool paused)
        {
            if (paused) Save();
        }

        void OnApplicationQuit()
        {
            Save();
        }

        void Resolve()
        {
            if (player == null)
                foreach (var i in Island.All) if (i != null && i.useKeyboardInput) { player = i; break; }
            if (streamer == null) streamer = FindAnyObjectByType<WorldStreamer>();
            if (volcanoSpawner == null) volcanoSpawner = FindAnyObjectByType<VolcanoSpawner>();
        }

        // Backgrounding fires GameSession's explicit save and this component's OnApplicationPause in the same
        // frame (and a quit can follow a pause); the world cannot change in between, so one write is enough.
        public bool Save()
        {
            Resolve();
            if (session == null) session = FindAnyObjectByType<GameSession>();
            if (session != null && !session.SavingAllowed) return false;
            if (player == null || player.IsSunk) return false;
            if (_savedFrame == Time.frameCount && File.Exists(SavePath)) return true;
            try
            {
                var life = player.GetComponent<IslandLifeSystem>();
                var plates = PlateSystem.Instance;
                var data = new SaveGame
                {
                    savedUtcTicks = DateTime.UtcNow.Ticks,
                    worldGenVersion = WorldStreamer.WorldGenVersion,
                    worldSeed = session != null ? session.WorldSeed : LoadedWorldSeed,
                    legacySeeds = session != null ? session.LegacySeeds : LoadedLegacySeeds,
                    stats = session != null ? session.Stats : LoadedStats,
                    journal = Journal.Capture(),
                    player = player.Capture(),
                    life = life != null ? life.Capture() : null,
                    plates = plates != null ? plates.Capture() : null,
                    startX = streamer != null ? streamer.StartPosition.x : 0f,
                    startZ = streamer != null ? streamer.StartPosition.y : 0f,
                };
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

                string json = JsonUtility.ToJson(data);
                string tmp = SavePath + ".tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
                File.WriteAllText(tmp, json);
                if (!File.Exists(SavePath)) File.Move(tmp, SavePath);
                else
                {
                    try { File.Replace(tmp, SavePath, null); }
                    catch (IOException)
                    {
                        File.Copy(tmp, SavePath, true);
                        File.Delete(tmp);
                    }
                }
                LastSaveBytes = new FileInfo(SavePath).Length;
                _savedFrame = Time.frameCount;
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
                if (data.worldGenVersion != WorldStreamer.WorldGenVersion)
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
                if (session == null) session = FindAnyObjectByType<GameSession>();
                if (session != null) session.ApplyWorldSeed(worldSeed, legacySeeds);
                else if (!legacySeeds) WorldSeeds.Apply(worldSeed, player, streamer);

                var plates = PlateSystem.Instance;
                if (plates != null) plates.Restore(data.plates);

                if (streamer != null)
                {
                    streamer.ResetWorld();
                    streamer.RestoreState(new Vector2(data.startX, data.startZ), data.consumed);
                    streamer.RestoreOverrides(data.aiIslands);
                }

                player.Restore(data.player);
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

        public void DeleteSave()
        {
            _savedFrame = -1;
            LoadedStats.Reset();
            Journal.Reset();
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }
    }
}
