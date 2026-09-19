using System;
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
        public int version = 1;
        public long savedUtcTicks;
        public IslandSaveData player;
        public LifeSaveData life;
        public PlateSaveData plates;
        public long[] consumed;
        public float startX, startZ;
    }

    public class SaveManager : MonoBehaviour
    {
        public Island player;
        public WorldStreamer streamer;
        public bool loadOnStart = true;
        public float autosaveInterval = 20f;
        public float maxOfflineLifeSeconds = 1800f;
        public string fileName = "drift_save.json";

        float _timer;

        public string SavePath => Path.Combine(Application.persistentDataPath, fileName);

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
            if (streamer == null) streamer = FindFirstObjectByType<WorldStreamer>();
        }

        public bool Save()
        {
            Resolve();
            if (player == null) return false;
            try
            {
                var life = player.GetComponent<IslandLifeSystem>();
                var plates = PlateSystem.Instance;
                var data = new SaveGame
                {
                    savedUtcTicks = DateTime.UtcNow.Ticks,
                    player = player.Capture(),
                    life = life != null ? life.Capture() : null,
                    plates = plates != null ? plates.Capture() : null,
                    consumed = streamer != null ? streamer.ConsumedKeys() : null,
                    startX = streamer != null ? streamer.StartPosition.x : 0f,
                    startZ = streamer != null ? streamer.StartPosition.y : 0f,
                };

                string json = JsonUtility.ToJson(data);
                string tmp = SavePath + ".tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
                File.WriteAllText(tmp, json);
                if (File.Exists(SavePath)) File.Replace(tmp, SavePath, null);
                else File.Move(tmp, SavePath);
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
                if (data == null || data.player == null || data.player.heights == null) return false;

                if (streamer != null) streamer.RestoreState(new Vector2(data.startX, data.startZ), data.consumed);
                var plates = PlateSystem.Instance;
                if (plates != null) plates.Restore(data.plates);

                player.Restore(data.player);
                var life = player.GetComponent<IslandLifeSystem>();
                if (life != null && data.life != null)
                {
                    life.Restore(data.life);
                    double elapsed = elapsedOverrideSeconds ?? new TimeSpan(DateTime.UtcNow.Ticks - data.savedUtcTicks).TotalSeconds;
                    float lifeSeconds = (float)Math.Min(Math.Max(elapsed, 0.0) * life.timeScale, maxOfflineLifeSeconds);
                    life.CatchUp(lifeSeconds);
                }
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
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }
    }
}
