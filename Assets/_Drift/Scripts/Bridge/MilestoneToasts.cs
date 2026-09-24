using Drift.Core;
using Drift.Islands;
using Drift.Life;
using Drift.SaveSystem;
using Drift.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Drift.Bridge
{
    // One milestone toast at a time: a milestone reached while another one is up follows after a short gap (a
    // merge that jumps from 5 to 10 islands brings the Hafen and the Seevögel), instead of replacing it unread.
    // Pure so it can be tested; the component ticks it only while the toast can be seen.
    public sealed class MilestoneToastQueue
    {
        public const int FreeText = -2;

        public float showSeconds = 8.5f;
        public float gapSeconds = 0.5f;

        readonly int[] _queue = new int[Milestones.Count + 4];
        int _count;
        float _timer, _gap;

        // The milestone on screen (its index), FreeText for a line from ShowText, -1 for none.
        public int Current { get; private set; } = -1;
        public bool Showing => Current != -1;
        public int Pending => _count;
        public float Left => _timer;

        public void Push(Milestone m)
        {
            int i = (int)m;
            if (Current == i) return;
            for (int k = 0; k < _count; k++) if (_queue[k] == i) return;
            if (_count < _queue.Length) _queue[_count++] = i;
        }

        // A free line (tests, debug) replaces whatever is up; the queue waits behind it.
        public void ShowText()
        {
            Current = FreeText;
            _timer = Mathf.Max(0.1f, showSeconds);
            _gap = 0f;
        }

        // The toast was tapped: it goes now, the next one follows after the gap.
        public void Dismiss()
        {
            if (!Showing) return;
            Current = -1;
            _timer = 0f;
            _gap = gapSeconds;
        }

        public void Clear()
        {
            _count = 0;
            Current = -1;
            _timer = _gap = 0f;
        }

        // True when Current changed.
        public bool Tick(float dt)
        {
            if (Showing)
            {
                _timer -= dt;
                if (_timer > 0f) return false;
                Current = -1;
                _gap = gapSeconds;
                return true;
            }
            if (_gap > 0f)
            {
                _gap -= dt;
                if (_gap > 0f) return false;
            }
            if (_count == 0) return false;
            Current = _queue[0];
            for (int k = 1; k < _count; k++) _queue[k - 1] = _queue[k];
            _count--;
            _timer = Mathf.Max(0.1f, showSeconds);
            return true;
        }
    }

    // Cozy milestones: every few merged islands something on the island changes for good - a Leuchtturm, a Hafen,
    // Seevögel over the high ground, an evening Fest with lanterns. This is the director: it watches the run's
    // island count, hands each milestone's effect to the system that owns it (the settlement, the flocks) and
    // shows a short congratulation of its own, which Tilda comments on.
    //
    // Tapping the toast watches what it is about, like a discovery toast watches its animal: the lighthouse, the
    // harbour, the seabirds, the festival ground (WatchSubjects.OfMilestone). While there is nothing to look at yet
    // the chip shows no chevron and takes no input, so a tap under it still reaches the world.
    // Nothing here blocks play: everything is retried instead of forced, and a missing reference just means the
    // milestone is applied as soon as it is there.
    // The notice chips (news and milestone toast) at their 1.5 x size: a long line wraps onto a second line and the
    // chip grows, instead of UiStyle.Chip's label shrinking the text back below its old size.
    public static class NoticeChip
    {
        public const float Pad = 20f;

        public static Text WrapLabel(RectTransform chip, Text chipLabel, int fontSize, Color color)
        {
            if (chipLabel != null)
            {
                chipLabel.gameObject.SetActive(false);
                if (Application.isPlaying) Object.Destroy(chipLabel.gameObject);
                else Object.DestroyImmediate(chipLabel.gameObject);
            }
            var t = UiStyle.Label(chip, "", fontSize, color, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 0.92f;
            t.raycastTarget = false;
            return t;
        }

        // Height for the label's text at its current width, never below minHeight; applied to the chip.
        public static float Fit(RectTransform chip, Text label, float minHeight)
        {
            if (chip == null || label == null) return minHeight;
            float height = Mathf.Max(minHeight, Mathf.Ceil(label.preferredHeight + 2f * Pad));
            if (Mathf.Abs(chip.sizeDelta.y - height) > 0.5f) chip.sizeDelta = new Vector2(chip.sizeDelta.x, height);
            return height;
        }
    }

    [DisallowMultipleComponent]
    public class MilestoneToasts : MonoBehaviour
    {
        const string CanvasName = "MilestoneCanvas";
        // 1.5 x the first size (owner, 2026-09-24), the width capped to the portrait screen like the news chip.
        public static readonly Vector2 ToastSize = new Vector2(1000f, 126f);
        public const int ToastFont = 54;
        public const float GoSize = 45f;
        const float TextInset = UiStyle.Gap + 12f, GoInset = 105f;
        // Under the news chip (WatchTools.NewsTop, 114 high), which sits under the round buttons on the right.
        public const float ToastTop = -606f;
        // Under the watch controls and the news chip that sits under them while something is watched.
        public const float ToastTopWatching = -854f;
        public RectTransform ToastRect => _chip;

        public GameSession session;
        public SaveManager saveManager;
        public Island player;
        public FlockSystem flocks;
        public WatchTools watch;
        public int sortingOrder = 14;

        [Header("Meilensteine")]
        // Renamed from toastSeconds (5.5 s) when the owner asked for every notice to stay 3 s longer, so the old
        // value serialized in the scene does not override the new default.
        [Tooltip("Wie lange die Meilenstein-Meldung stehen bleibt (Sekunden).")]
        [Range(2f, 15f)] public float toastShowSeconds = 8.5f;
        [Tooltip("Wie oft (Sekunden) nachgesehen wird, ob Leuchtturm und Hafen noch stehen - nach einer Umformung der Insel werden sie wieder gebaut.")]
        [Range(2f, 60f)] public float retrySeconds = 8f;
        [Tooltip("Wie viele Seevogelschwärme ab dem Meilenstein über der eigenen Insel kreisen.")]
        [Range(1, 4)] public int homeSeabirdFlocks = 2;

        Canvas _canvas;
        RectTransform _root, _chip;
        CanvasGroup _group;
        Text _text;
        Image _body, _go;
        float _retry, _lookup, _tapCheck;
        bool _tappable;
        string _freeText = "";
        readonly MilestoneToastQueue _queue = new();
        GameSession.State _lastState = GameSession.State.Title;

        public string CurrentToast => _text != null && _queue.Showing ? _text.text : null;
        public bool ToastVisible => _queue.Showing;
        // The milestone on screen, -1 when none (or a free line).
        public int CurrentMilestone => _queue.Current >= 0 ? _queue.Current : -1;
        public bool ToastTappable => _tappable;
        public MilestoneToastQueue Queue => _queue;

        void OnEnable()
        {
            Build();
            Milestones.Celebrated += OnCelebrated;
            _lastState = GameSession.State.Title;
            _retry = 0f;
            _queue.Clear();
        }

        void OnDisable()
        {
            Milestones.Celebrated -= OnCelebrated;
        }

        void Build()
        {
            UiStyle.DestroyChildrenNamed(transform, CanvasName);
            _canvas = UiStyle.Canvas(transform, CanvasName, sortingOrder, true, out _root);
            var size = ToastSize;
            _chip = UiStyle.Chip(_root, "MilestoneToast", "", size, out _text);
            _chip.TopCenter(new Vector2(0f, ToastTop), size);
            _text = NoticeChip.WrapLabel(_chip, _text, ToastFont, UiStyle.Sand);
            _text.rectTransform.Stretch(TextInset, 0f, GoInset, 3f);
            _group = _chip.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _group.alpha = 0f;

            // The same tap affordance as the news chip: the whole chip is the button, a chevron says so.
            var body = _chip.Find("Body");
            _body = body != null ? body.GetComponent<Image>() : null;
            var button = _chip.gameObject.AddComponent<Button>();
            button.targetGraphic = _body;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => OnToastTapped());
            _chip.gameObject.AddComponent<UiPressFeedback>().pressedScale = 0.96f;
            _go = UiStyle.Icon(_chip, "Go", UiIcon.Back, GoSize, UiStyle.Sand);
            _go.rectTransform.Place(new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-60f, 0f), new Vector2(GoSize, GoSize));
            _go.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            _go.raycastTarget = false;
            SetTappable(false);
            _canvas.enabled = false;
        }

        void Resolve()
        {
            if (player == null)
                foreach (var i in Island.All) if (i != null && i.useKeyboardInput) { player = i; break; }
            if (session != null && saveManager != null && flocks != null && watch != null) return;
            _lookup -= Time.unscaledDeltaTime;
            if (_lookup > 0f) return;
            _lookup = 1f;
            if (session == null) session = FindAnyObjectByType<GameSession>();
            if (saveManager == null) saveManager = FindAnyObjectByType<SaveManager>();
            if (flocks == null) flocks = FindAnyObjectByType<FlockSystem>();
            if (watch == null && !TryGetComponent(out watch)) watch = FindAnyObjectByType<WatchTools>();
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
                    // A toast of the previous run (or the other mode) is not carried into the new one.
                    _queue.Clear();
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
            _queue.Push(m);
            // Her line is added by TildaVoiceLines; until it is there Say simply does nothing.
            TildaVoice.Say(Milestones.VoiceKeyOf(m), VoicePriority.Queue);
            // The landmark is built now, not after retrySeconds, so the toast can already be tapped.
            _retry = 0f;
            if (Application.isPlaying && session != null && session.Current == GameSession.State.Playing) Apply();
            if (_queue.Tick(0f)) Refresh();
        }

        public void ShowToast(string text)
        {
            if (_text == null) return;
            _freeText = text ?? "";
            _queue.showSeconds = Mathf.Max(2f, toastShowSeconds);
            _queue.ShowText();
            Refresh();
        }

        // The chip was tapped (public so tests and eval take the same route as a finger): the camera goes to what
        // the milestone is about with the one watch mode, exactly like tapping an animal.
        public bool OnToastTapped()
        {
            int m = _queue.Current;
            if (m < 0 || watch == null || !WatchRules.Allowed(WatchFeature.Watch)) return false;
            var subject = WatchSubjects.OfMilestone((Milestone)m, player, flocks);
            if (subject == null || !watch.BeginWatch(subject)) return false;
            _queue.Dismiss();
            Refresh();
            return true;
        }

        void Refresh()
        {
            if (_text == null) return;
            int m = _queue.Current;
            if (m == MilestoneToastQueue.FreeText) _text.text = _freeText;
            else if (m >= 0) _text.text = Milestones.ToastOf((Milestone)m);
            NoticeChip.Fit(_chip, _text, ToastSize.y);
            _tapCheck = 0f;
            UpdateTappable(0f);
        }

        void SetTappable(bool on)
        {
            _tappable = on;
            if (_go != null && _go.gameObject.activeSelf != on) _go.gameObject.SetActive(on);
            if (_body != null) _body.raycastTarget = on;
            if (_group != null)
            {
                _group.blocksRaycasts = on;
                _group.interactable = on;
            }
            // Without the chevron the text may use the whole chip.
            if (_text != null)
            {
                _text.rectTransform.Stretch(TextInset, 0f, on ? GoInset : TextInset, 3f);
                NoticeChip.Fit(_chip, _text, ToastSize.y);
            }
        }

        // Whether the milestone has something to look at right now; asked a few times a second, not every frame.
        void UpdateTappable(float dt)
        {
            _tapCheck -= dt;
            if (_tapCheck > 0f) return;
            _tapCheck = 0.25f;
            int m = _queue.Current;
            bool on = m >= 0 && watch != null && watch.isActiveAndEnabled && WatchRules.Allowed(WatchFeature.Watch)
                      && WatchSubjects.MilestoneHasSubject((Milestone)m, player, flocks);
            if (on != _tappable) SetTappable(on);
        }

        bool Hidden =>
            (session != null && session.Current != GameSession.State.Playing)
            || WatchTools.HudHidden || (watch != null && (watch.JournalOpen || watch.AlbumOpen));

        void StepToast(float dt)
        {
            if (_canvas == null || _group == null) return;
            bool hidden = Hidden;
            _queue.showSeconds = Mathf.Max(2f, toastShowSeconds);
            // A toast waits while it cannot be seen (pause menu, photo mode) instead of running out unread.
            if (!hidden && _queue.Tick(dt)) Refresh();
            if (_queue.Showing) UpdateTappable(dt);
            // Under the news chip whenever that one shows (a two-line news chip pushes the toast further down).
            float top = watch != null && watch.Following ? ToastTopWatching : ToastTop;
            if (watch != null && watch.NewsShowing) top = Mathf.Min(top, watch.NewsBottom - 16f);
            _chip.anchoredPosition = new Vector2(0f, top);
            float target = _queue.Showing && !hidden ? 1f : 0f;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, dt / 0.25f);
            if (target <= 0f && _tappable) SetTappable(false);
            bool on = _group.alpha > 0.001f;
            if (_canvas.enabled != on) _canvas.enabled = on;
        }
    }
}
