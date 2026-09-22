using Drift.Core;
using Drift.Islands;
using UnityEngine;

namespace Drift.Visuals
{
    // Reading the water pays off: catching a plate boundary gives a short kick, and travelling with the current
    // for a while adds a gentle push on top of the ride the current already gives. Both go through Island's
    // public SpeedBoost only, and only in the cozy mode - the adventure's timed merge and flotsam boosts are
    // never touched (BoostRemaining gates the flow push, so a running boost always wins).
    [DefaultExecutionOrder(160)]
    public class DriveFeel : MonoBehaviour
    {
        public Island player;

        [Header("Strömung und Surfen")]
        [Tooltip("Belohnung fürs Lesen des Wassers: Schub beim Aufspringen auf eine Plattengrenze und ein leichter Zusatzschub, wenn du lange mit der Strömung fährst. Aus = nur die Strömung selbst trägt.")]
        public bool rewardsEnabled = true;
        [Tooltip("Ab dieser Surf-Stärke gilt eine Plattengrenze als erwischt.")]
        [Range(0.05f, 1f)] public float surfCatchThreshold = 0.35f;
        [Tooltip("Wie kräftig der Schub beim Erwischen einer Plattengrenze ist (1 = kein Schub).")]
        [Range(1f, 1.6f)] public float surfKickFactor = 1.18f;
        [Tooltip("Wie lange der Schub beim Erwischen anhält (Sekunden).")]
        [Range(0.1f, 3f)] public float surfKickSeconds = 0.9f;
        [Tooltip("So viele Sekunden, bevor ein neues Aufspringen wieder Schub gibt.")]
        [Range(0f, 10f)] public float surfKickCooldown = 2.5f;
        [Tooltip("Zusatztempo, wenn du lange mit der Strömung fährst (1 = keins).")]
        [Range(1f, 1.4f)] public float flowFactor = 1.1f;
        [Tooltip("Ab diesem Strömungsgefühl (0..1) greift der Zusatzschub.")]
        [Range(0f, 1f)] public float flowThreshold = 0.5f;

        readonly SpeedFeel.Tracker _feel = new();
        bool _wasSurfing;
        float _cooldownLeft;

        public float SpeedDrive => _feel.Drive;
        public float FlowAmount => _feel.Flow;
        public float SurfAmount => _feel.Surf;
        public bool Surfing => _wasSurfing;
        public int SurfKicks { get; private set; }
        public int FlowPushes { get; private set; }

        public Island FindPlayer()
        {
            if (player != null && player.isActiveAndEnabled) return player;
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var isl = all[i];
                if (isl != null && isl.useKeyboardInput && isl.isActiveAndEnabled) return isl;
            }
            return null;
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            Step(FindPlayer(), Time.deltaTime);
        }

        public void Step(Island p, float dt)
        {
            _feel.Step(p, dt);
            if (_cooldownLeft > 0f) _cooldownLeft = Mathf.Max(0f, _cooldownLeft - dt);
            if (!rewardsEnabled || GameModes.IsAdventure || Island.InputLocked || p == null || p.IsSunk)
            {
                _wasSurfing = false;
                return;
            }

            if (surfKickFactor > 1f && SpeedFeel.CatchesSurf(_feel.Surf, _wasSurfing, surfCatchThreshold, _cooldownLeft, p.Boosting))
            {
                p.SpeedBoost(surfKickSeconds, surfKickFactor);
                _cooldownLeft = surfKickCooldown;
                SurfKicks++;
            }
            _wasSurfing = _feel.Surf >= surfCatchThreshold;

            // Refreshed shortly before it runs out, so the push is steady instead of stuttering; a longer boost
            // from somewhere else (adventure merge, flotsam) always has more time left and keeps the field.
            if (p.BoostRemaining > 0.25f) return;
            float want = SpeedFeel.FlowPush(_feel.Flow, flowThreshold, flowFactor);
            if (want <= 1.001f) return;
            p.SpeedBoost(0.4f, want);
            FlowPushes++;
        }
    }
}
