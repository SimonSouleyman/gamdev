using Drift.Audio;
using Drift.Core;
using Drift.SaveSystem;
using Drift.Visuals;
using UnityEngine;

namespace Drift.Bridge
{
    // Drift.Audio cannot see Drift.Visuals or Drift.SaveSystem, so the sea's sound cues and whether an adventure run
    // is being played (AudioDirector.AdventureRunning, for the adventure music) are forwarded from here.
    public class SeaAudioBridge : MonoBehaviour
    {
        public SeaLifeSystem seaLife;
        public float pollInterval = 0.25f;

        public GameSession session;

        int _breaches = -1, _surfacings = -1;
        float _timer, _sessionSearch;

        void OnEnable()
        {
            ShipSystem.ShipBeached += OnShipBeached;
            _breaches = _surfacings = -1;
        }

        void OnDisable()
        {
            ShipSystem.ShipBeached -= OnShipBeached;
            AudioDirector.AdventureRunning = false;
        }

        static void OnShipBeached(Vector3 pos, float intensity)
        {
            var audio = AudioDirector.Instance;
            if (audio != null) audio.TriggerShipBeached(intensity, new Vector2(pos.x, pos.z));
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            UpdateAdventureRunning();
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = pollInterval;
            if (seaLife == null) seaLife = FindAnyObjectByType<SeaLifeSystem>();
            var audio = AudioDirector.Instance;
            if (seaLife == null || audio == null) return;

            if (_breaches >= 0 && seaLife.WhaleBreaches > _breaches) audio.TriggerBigSplash(1f);
            else if (_surfacings >= 0 && seaLife.WhaleSurfacings > _surfacings) audio.TriggerWhaleBlow();
            _breaches = seaLife.WhaleBreaches;
            _surfacings = seaLife.WhaleSurfacings;
        }

        void UpdateAdventureRunning()
        {
            if (session == null)
            {
                _sessionSearch -= Time.unscaledDeltaTime;
                if (_sessionSearch <= 0f)
                {
                    session = FindAnyObjectByType<GameSession>();
                    _sessionSearch = 2f;
                }
            }
            var state = session != null ? session.Current : GameSession.State.Title;
            AudioDirector.AdventureRunning = GameModes.IsAdventure
                && (state == GameSession.State.Playing || state == GameSession.State.Paused);
        }
    }
}
