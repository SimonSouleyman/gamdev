using Drift.Audio;
using Drift.Visuals;
using UnityEngine;

namespace Drift.Bridge
{
    // Drift.Audio cannot see Drift.Visuals, so the sea's sound cues are forwarded from here.
    public class SeaAudioBridge : MonoBehaviour
    {
        public SeaLifeSystem seaLife;
        public float pollInterval = 0.25f;

        int _breaches = -1, _surfacings = -1;
        float _timer;

        void OnEnable()
        {
            ShipSystem.ShipBeached += OnShipBeached;
            _breaches = _surfacings = -1;
        }

        void OnDisable() => ShipSystem.ShipBeached -= OnShipBeached;

        static void OnShipBeached(Vector3 pos, float intensity)
        {
            var audio = AudioDirector.Instance;
            if (audio != null) audio.TriggerShipBeached(intensity, new Vector2(pos.x, pos.z));
        }

        void Update()
        {
            if (!Application.isPlaying) return;
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
    }
}
