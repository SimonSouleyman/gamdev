using Drift.Core;
using Drift.Islands;
using Drift.Life;
using Drift.SaveSystem;
using Drift.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Drift.Bridge
{
    // Cozy milestones: every few merged islands something on the island changes for good - a Leuchtturm, a Hafen,
    // Seevögel over the high ground, an evening Fest with lanterns. This is the director: it watches the run's
    // island count, hands each milestone's effect to the system that owns it (the settlement, the flocks) and
    // shows a short congratulation of its own, which Tilda comments on.
    //
    // Nothing here blocks play: the chip takes no input, everything is retried instead of forced, and a missing
    // reference just means the milestone is applied as soon as it is there.
    [DisallowMultipleComponent]
    public class MilestoneToasts : MonoBehaviour
    {
        const string CanvasName = "MilestoneCanvas";

        public GameSession session;
        public SaveManager saveManager;
        public Island player;
        public FlockSystem flocks;
        public int sortingOrder = 14;

        [Header("Meilensteine")]
        [Tooltip("Wie lange die Meilenstein-Meldung stehen bleibt (Sekunden).")]
        [Range(2f, 12f)] public float toastSeconds = 5.5f;
        [Tooltip("Wie oft (Sekunden) nachgesehen wird, ob Leuchtturm und Hafen noch stehen - nach einer Umformung der Insel werden sie wieder gebaut.")]
        [Range(2f, 60f)] public float retrySeconds = 8f;
        [Tooltip("Wie viele Seevogelschwärme ab dem Meilenstein über der eigenen Insel kreisen.")]
        [Range(1, 4)] public int homeSeabirdFlocks = 2;

        Canvas _canvas;
        RectTransform _root, _chip;
        CanvasGroup _group;
        Text _text;
        float _show, _retry, _lookup;
        GameSession.State _lastState = GameSession.State.Title;

        public string CurrentToast => _text != null && _show > 0f ? _text.text : null;
        public bool ToastVisible => _show > 0f;

        void OnEnable()
        {
            Build();
            Milestones.Celebrated += OnCelebrated;
            _lastState = GameSession.State.Title;
            _retry = 0f;
        }

        void OnDisable()
        {
            Milestones.Celebrated -= OnCelebrated;
        }

        void Build()
        {
            UiStyle.DestroyChildrenNamed(transform, CanvasName);
            _canvas = UiStyle.Canvas(transform, CanvasName, sortingOrder, false, out _root);
            var size = new Vector2(820f, 84f);
            _chip = UiStyle.Chip(_root, "MilestoneToast", "", size, out _text);
            _chip.TopCenter(new Vector2(0f, -470f), size);
            _text.rectTransform.Stretch(UiStyle.Gap + 8f, 0f, UiStyle.Gap + 8f, 2f);
            _text.fontSize = UiStyle.Body;
            _text.color = UiStyle.Sand;
            _group = _chip.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _group.alpha = 0f;
            _canvas.enabled = false;
        }

        void Resolve()
        {
            if (player == null)
                foreach (var i in Island.All) if (i != null && i.useKeyboardInput) { player = i; break; }
            if (session != null && saveManager != null && flocks != null) return;
            _lookup -= Time.unscaledDeltaTime;
            if (_lookup > 0f) return;
            _lookup = 1f;
            if (session == null) session = FindAnyObjectByType<GameSession>();
            if (saveManager == null) saveManager = FindAnyObjectByType<SaveManager>();
            if (flocks == null) flocks = FindAnyObjectByType<FlockSystem>();
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            Resolve();
            StepToast(Time.unscaledDeltaTime);
            if (session == null) return;

            var state = session.Current;
            // Entering a game: a new run starts at zero islands and no milestones, a continued one gets the mask
            // of its save back. Either way nothing is celebrated again.
            if (state != _lastState)
            {
                if (state == GameSession.State.Playing && _lastState != GameSession.State.Paused)
                {
                    Milestones.Restore(saveManager != null ? saveManager.LoadedMilestones : 0, session.Stats.islandsAbsorbed);
                    _retry = 0f;
                }
                _lastState = state;
            }
            if (state != GameSession.State.Playing || !Milestones.Enabled) return;

            Milestones.Check(session.Stats.islandsAbsorbed);
            _retry -= Time.deltaTime;
            if (_retry > 0f) return;
            _retry = Mathf.Max(1f, retrySeconds);
            Apply();
        }

        // Puts every reached milestone back in place - a landmark can drown with the next reshape, so this keeps
        // asking. Cheap: one settlement lookup and a few counters every retrySeconds.
        public void Apply()
        {
            if (player == null) return;
            var settlement = player.GetComponent<IslandSettlementSystem>();
            if (settlement != null)
            {
                if (Milestones.Has(Milestone.Lighthouse)) settlement.TryBuildLandmark(BuildingKind.Lighthouse);
                if (Milestones.Has(Milestone.Harbour)) settlement.TryBuildLandmark(BuildingKind.Dock);
                settlement.festivals = Milestones.Has(Milestone.Festival);
            }
            if (Milestones.Has(Milestone.Seabirds) && flocks != null) flocks.SetSeabirdHome(player, homeSeabirdFlocks);
        }

        void OnCelebrated(Milestone m)
        {
            ShowToast(Milestones.ToastOf(m));
            // Her line is added by TildaVoiceLines; until it is there Say simply does nothing.
            TildaVoice.Say(Milestones.VoiceKeyOf(m), VoicePriority.Queue);
            _retry = 0f;
        }

        public void ShowToast(string text)
        {
            if (_text == null) return;
            _text.text = text;
            UiStyle.FitWidth(_text);
            _show = Mathf.Max(2f, toastSeconds);
        }

        void StepToast(float dt)
        {
            if (_canvas == null || _group == null) return;
            bool hidden = session != null && session.Current != GameSession.State.Playing;
            if (_show > 0f) _show -= dt;
            float target = _show > 0f && !hidden ? 1f : 0f;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, dt / 0.25f);
            bool on = _group.alpha > 0.001f;
            if (_canvas.enabled != on) _canvas.enabled = on;
        }
    }
}
