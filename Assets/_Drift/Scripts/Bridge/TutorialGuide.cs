using Drift.Audio;
using Drift.Islands;
using Drift.SaveSystem;
using Drift.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Drift.Bridge
{
    // Tilda the little volcano walks a first-time player through the game in a speech bubble (TildaBubble; her 3D
    // portrait comes from the shared TildaPortrait and stands left of it, the bubble's tail at her mouth). The
    // steps live in the pure TutorialModel; this component feeds it real events and draws it. It reads the
    // simulation and writes nothing but GameSession.SinkingSuspended (steps 1-4), which it always clears again. She
    // grumbles along with the text the bubble types. Outside the tutorial the same bubble shows the short remarks
    // she makes during play (TildaVoice.LineStarted).
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class TutorialGuide : MonoBehaviour
    {
        public enum EditorPreview { None, Greeting, Arrow, Buoyancy, Form, Sleepy }

        public const string DoneKey = "drift_tutorial_done";
        const string CanvasName = "TutorialCanvas";
        const float BubbleWidth = 1000f, PortraitSize = 330f, WidePortraitSize = 680f, MinWidePortraitSize = 480f;
        const float NarrowBubbleLeft = 312f, NarrowSide = 20f, TailGap = 46f;
        // The HUD's minimap (bottom left) and hint chip (bottom right) the bubble keeps clear of.
        const float MapSize = 340f, HintWidth = 760f, HintHeight = 64f;

        public GameSession session;
        public Island player;
        public IslandChaseCamera chaseCamera;
        public WatchTools watch;
        public WorldHud hud;
        public TouchControls touch;
        public EditorPreview editorPreview = EditorPreview.None;
        // Above the session screens (10), below the watch tools (15) and the thumbstick (20).
        public int sortingOrder = 12;
        public float charsPerSecond = 45f;
        // Below this canvas width (portrait) the bubble sits above the minimap instead of between minimap and hint.
        public float wideLayoutWidth = 1850f;
        public float targetRefresh = 0.5f;

        static int s_playSession;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnPlaySessionStart() => s_playSession++;

        TutorialModel _model = new TutorialModel();
        int _initSession = -1;
        bool _replayRequested, _savedDone;
        GameSession.State _lastState = (GameSession.State)(-1);
        Vector2 _lastPos;
        bool _hasLastPos;
        float _lookupTimer, _targetTimer;
        Island _arrowTarget;

        Canvas _canvas;
        RectTransform _root, _holder, _arrow, _arrowGlyph, _ring;
        Image _ringImage;
        TildaView _tilda;
        TildaBubble _bubble;
        Button _skip;
        RectTransform _portrait, _portraitShadow;
        string _remarkKey, _previewShown, _hint = "";
        float _layoutWidth = -1f, _layoutHeight = -1f;
        bool _wasVisible, _sleepSaid, _wide, _layoutNext;
        int _shownVersion = -1;

        public TutorialModel Model => _model;
        public bool ReplayPending => _replayRequested || _model.Active;
        public bool BubbleVisible => _holder != null && _holder.gameObject.activeSelf;
        public Island ArrowTarget => _arrowTarget;

        void OnEnable()
        {
            if (touch == null) touch = GetComponent<TouchControls>();
            if (watch == null) watch = GetComponent<WatchTools>();
            Build();
            Island.Merged += OnMerged;
            TildaVoice.LineStarted += OnLineStarted;
            TildaVoice.LineEnded += OnLineEnded;
            _shownVersion = -1;
            _lookupTimer = 0f;
        }

        void OnDisable()
        {
            Island.Merged -= OnMerged;
            TildaVoice.LineStarted -= OnLineStarted;
            TildaVoice.LineEnded -= OnLineEnded;
            _remarkKey = null;
            if (session != null) session.SinkingSuspended = false;
        }

        // ---------------------------------------------------------------- public controls

        // From the Anleitung screen: forget the done flag; in a running game Tilda starts over right away,
        // otherwise with the next game that begins.
        public void RequestReplay()
        {
            _replayRequested = true;
            if (!Application.isPlaying) return;
            PlayerPrefs.DeleteKey(DoneKey);
            PlayerPrefs.Save();
            _savedDone = false;
            if (session != null && session.Model.InGame)
            {
                _model.Begin();
                _replayRequested = false;
            }
        }

        public void Skip() => _model.Skip();

        // Finishes the typing, turns the page, and on the last page moves on to the next step.
        public void Continue()
        {
            if (_bubble != null) _bubble.Next();
        }

        // A remark during play (no menu, so no big Tilda to say it): the tutorial's bubble borrows itself out, unless
        // the tutorial is using it.
        void OnLineStarted(TildaLine line)
        {
            if (line.shown || !Application.isPlaying || _bubble == null || _model.Active) return;
            if (session == null || session.Current != GameSession.State.Playing) return;
            if (watch != null && (watch.PhotoActive || watch.JournalOpen)) return;
            line.shown = true;
            _remarkKey = line.key;
            _shownVersion = -1;
            Show(true);
            _bubble.SetCounter("");
            _bubble.SetFinal(null, null);
            if (_skip != null) _skip.gameObject.SetActive(false);
            string key = line.key;
            _bubble.Show(line.text, line.mood, TildaBubble.RemarkCharsPerSecond, true, () => TildaVoice.Hush(key), TildaBubble.RemarkMaxChars);
            _layoutWidth = -1f;
            PlaceBubble();
            _tilda.SetPose(line.mood == GrumbleMood.Cheerful ? TildaPose.Wave : TildaPose.Talk, true);
        }

        void OnLineEnded(string key)
        {
            if (key == _remarkKey) _remarkKey = null;
        }

        void OnMerged(Island host, Island guest, float energy)
        {
            if (!Application.isPlaying || host == null || host != player) return;
            _model.ReportMerge();
        }

        // ---------------------------------------------------------------- update

        void Resolve()
        {
            if (player == null)
                foreach (var i in Island.All) if (i != null && i.useKeyboardInput) { player = i; break; }
            if (touch == null) touch = GetComponent<TouchControls>();
            if (watch == null) watch = GetComponent<WatchTools>();
            if (session != null && chaseCamera != null && hud != null) return;
            _lookupTimer -= Time.unscaledDeltaTime;
            if (_lookupTimer > 0f) return;
            _lookupTimer = 1f;
            if (session == null) session = FindAnyObjectByType<GameSession>();
            if (chaseCamera == null) chaseCamera = FindAnyObjectByType<IslandChaseCamera>();
            if (hud == null) hud = FindAnyObjectByType<WorldHud>();
        }

        bool TouchDevice => InputMode.TouchPreferred || (touch != null && touch.forceShowTouch);

        void Update()
        {
            if (_canvas == null) return;
            if (_canvas.worldCamera == null && Camera.main != null) _canvas.worldCamera = Camera.main;
            Resolve();

            if (!Application.isPlaying)
            {
                EditorPreviewUpdate();
                return;
            }

            // Play Mode can start without a domain or scene reload, so per-play state is reset here, not in OnEnable.
            if (_initSession != s_playSession)
            {
                _initSession = s_playSession;
                _model = new TutorialModel();
                if (PlayerPrefs.GetInt(DoneKey, 0) == 1) _model.MarkDone();
                _savedDone = _model.Done;
                _replayRequested = false;
                _lastState = (GameSession.State)(-1);
                _hasLastPos = false;
                _shownVersion = -1;
            }
            if (session == null) { Show(false); return; }

            var s = session.Current;
            if (s != _lastState)
            {
                bool begins = s == GameSession.State.Playing && session.Stats.timeSurvived < 0.5f && session.Stats.islandsAbsorbed == 0;
                _lastState = s;
                _hasLastPos = false;
                if (begins && (!_model.Done || _replayRequested))
                {
                    if (!_model.Active || _model.Step < TutorialStep.Buoyancy) _model.Begin();
                    _replayRequested = false;
                }
            }

            bool inGame = s == GameSession.State.Playing || s == GameSession.State.Paused;
            if (_model.Active && inGame && watch != null && (watch.PopupVisible || watch.JournalOpen)) _model.ReportWatched();
            bool visible = _model.Active && s == GameSession.State.Playing && !(watch != null && (watch.PhotoActive || watch.JournalOpen));
            if (visible) Feed();

            session.SinkingSuspended = _model.SinkingSuspended;
            if (_model.Done && !_savedDone)
            {
                _savedDone = true;
                PlayerPrefs.SetInt(DoneKey, 1);
                PlayerPrefs.Save();
            }

            visible &= _model.Active;
            bool remark = !visible && _remarkKey != null;
            if (remark && (s != GameSession.State.Playing || (watch != null && (watch.PhotoActive || watch.JournalOpen))))
            {
                TildaVoice.Hush(_remarkKey);
                _remarkKey = null;
                remark = false;
            }
            // Hidden by a menu or skipped: she stops mid-grumble.
            if (_wasVisible && !visible && !remark) TildaVoice.Quiet();
            _wasVisible = visible || remark;
            Show(visible || remark);
            if (remark)
            {
                PlaceBubble();
                _tilda.SetPose(_bubble.Typing ? TildaPose.Talk : TildaPose.Idle, _bubble.Typing);
                return;
            }
            if (!visible) return;
            Draw(Time.unscaledDeltaTime, Time.unscaledTime);
        }

        void Feed()
        {
            if (player != null)
            {
                Vector2 pos = player.PlanarPosition;
                if (_hasLastPos && player.Speed > 0.3f) _model.ReportTravel(Mathf.Min(2f, (pos - _lastPos).magnitude));
                _lastPos = pos;
                _hasLastPos = true;
                if (player.Speed > 0.25f) _model.ReportActivity();
            }
            if (chaseCamera != null) _model.ReportZoom(chaseCamera.ZoomTarget);
            if (AnyPress()) _model.ReportActivity();
            // Reading her explanation is not idling: she must not doze off over her own words.
            if (_bubble != null && _bubble.Typing) _model.ReportActivity();
            _model.Tick(Time.unscaledDeltaTime);
        }

        static bool AnyPress()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.isPressed) return true;
            var m = Mouse.current;
            if (m != null && (m.leftButton.isPressed || m.rightButton.isPressed || Mathf.Abs(m.scroll.ReadValue().y) > 0.01f)) return true;
            var ts = Touchscreen.current;
            return ts != null && ts.primaryTouch.press.isPressed;
        }

        void Show(bool on)
        {
            if (_holder.gameObject.activeSelf != on) _holder.gameObject.SetActive(on);
            if (!on)
            {
                SetActive(_arrow.gameObject, false);
                SetActive(_ring.gameObject, false);
            }
        }

        static void SetActive(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        // ---------------------------------------------------------------- drawing

        static string K(string word) => TildaBubble.Key(word);

        // Rich text: TildaBubble.Key marks the one or two words a step is about.
        public static string TextFor(TutorialStep step, bool touchDevice)
        {
            switch (step)
            {
                case TutorialStep.Greeting:
                    return "Hallo! Ich bin " + K("Tilda") + ", ein kleiner Vulkan. Schön, dass du da bist! Ich zeige dir, wie deine Insel wächst.";
                case TutorialStep.Move:
                    return touchDevice
                        ? "Probier es gleich aus: Leg den " + K("Daumen links") + " aufs Wasser und zieh. Nach oben gibt Tempo, zur Seite lenkt."
                        : "Probier es gleich aus: " + K("W") + " gibt Tempo, " + K("S") + " bremst, mit " + K("A") + " und " + K("D") + " lenkst du. Fahr einfach ein Stück los!";
                case TutorialStep.Zoom:
                    return touchDevice
                        ? "Prima, du fährst! Zieh " + K("zwei Finger") + " auseinander oder zusammen, dann siehst du näher hin oder mehr vom Meer."
                        : "Prima, du fährst! Mit dem " + K("Mausrad") + " oder mit " + K("Q") + " und " + K("E") + " gehst du näher heran oder siehst mehr vom Meer.";
                case TutorialStep.Ram:
                    return "Siehst du den " + K("Pfeil") + "? Dort treibt eine Insel. Fahr mitten hinein und " + K("ramme") + " sie – dann wächst sie an deine an.";
                case TutorialStep.Buoyancy:
                    return "Juhu, bei mir sprühen die Funken! Deine Insel ist gewachsen. Ab jetzt sinkt sie langsam. Behalte den Balken " + K("„Auftrieb“") + " im Blick: Jede neue Insel hebt dich wieder.";
                case TutorialStep.Form:
                    return "Daneben steht die " + K("Form") + ". Runde Inseln schwimmen gut, längliche sinken schneller. Beim Rammen " + K("dreht sich") + " deine Insel, damit sie rund wächst.";
                case TutorialStep.Watch:
                    return touchDevice
                        ? "Auf den Inseln ist viel los! " + K("Tippe ein Tier an") + ", um es kennenzulernen, oder schau ins " + K("Tagebuch") + " oben rechts."
                        : "Auf den Inseln ist viel los! " + K("Klicke ein Tier an") + ", um es kennenzulernen, oder schau ins " + K("Tagebuch") + " oben rechts.";
                case TutorialStep.Farewell:
                    return "Viel Spaß beim Treiben! Wenn du mich brauchst: Ich dampfe im Menü unter " + K("Anleitung") + " vor mich hin.";
                default:
                    return "";
            }
        }

        static string HintFor(TutorialStep step)
        {
            switch (step)
            {
                case TutorialStep.Move: return "Fahr ein Stück los!";
                case TutorialStep.Ram: return "Folge dem grünen Pfeil!";
                default: return "";
            }
        }

        void Draw(float dt, float time)
        {
            if (_model.Version != _shownVersion)
            {
                _shownVersion = _model.Version;
                _remarkKey = null;
                SetStepTexts(_model.Step, _model.StepNumber, _model.CanContinue);
                bool cheer = _model.Cheering && _model.Step != TutorialStep.Greeting;
                var mood = cheer ? GrumbleMood.Giggly : _model.Step == TutorialStep.Greeting || _model.Step == TutorialStep.Farewell ? GrumbleMood.Cheerful : GrumbleMood.Warm;
                _layoutWidth = -1f;
                PlaceBubble();
                // Steps that wait for the player turn their pages by themselves.
                _bubble.Show(TextFor(_model.Step, TouchDevice), mood, charsPerSecond, !_model.CanContinue);
            }
            bool typing = _bubble.Typing;

            bool sleeping = _model.Mood == TildaMood.Sleep;
            if (sleeping && !_sleepSaid) TildaVoice.Murmur(TildaVoiceLines.SleepyMumble, GrumbleMood.Sleepy, 7f);
            _sleepSaid = sleeping;

            PlaceBubble();
            var pose = PoseFor(_model.Mood, typing);
            if (_wide && _model.Mood == TildaMood.Happy && typing) pose = TildaPose.Present;
            _tilda.SetPose(pose, typing);

            UpdateArrow(_model.ShowsArrow, dt, time);
            UpdateRing(RingTarget(_model.Step), time);
        }

        void SetStepTexts(TutorialStep step, int number, bool canContinue)
        {
            _bubble.SetCounter(number + " / " + TutorialModel.StepCount);
            _bubble.SetFinal(canContinue ? (step == TutorialStep.Farewell ? "Fertig" : "Weiter") : null, () => _model.Continue());
            _hint = HintFor(step);
            if (_skip != null && !_skip.gameObject.activeSelf) _skip.gameObject.SetActive(true);
        }

        // Greeting -> Wave, a solved step or the farewell -> Cheer, 20 s without input -> Sleepy, typing -> Talk.
        public static TildaPose PoseFor(TildaMood mood, bool typing)
        {
            switch (mood)
            {
                case TildaMood.Sleep: return TildaPose.Sleepy;
                case TildaMood.Cheer: return TildaPose.Cheer;
                case TildaMood.Wave: return TildaPose.Wave;
                default: return typing ? TildaPose.Talk : TildaPose.Idle;
            }
        }

        // Wide screens: Tilda stands big at the left end of the bubble, the pair between the minimap and the hint
        // chip (raised above the chip where the two would meet). Narrow (portrait): the pair sits above the minimap,
        // small Tilda on the left, the bubble filling the rest of the width. Either way the tail aims at her mouth.
        void PlaceBubble()
        {
            Vector2 canvas = TildaPresenter.CanvasSize(_root);
            bool wide = canvas.x >= wideLayoutWidth;
            bool nextShown = _bubble.NextButton != null && _bubble.NextButton.gameObject.activeSelf;
            if (wide == _wide && Mathf.Approximately(canvas.x, _layoutWidth) && Mathf.Approximately(_bubble.Height, _layoutHeight) && nextShown == _layoutNext) return;
            _wide = wide;
            _layoutWidth = canvas.x;
            _layoutNext = nextShown;

            float size, bubbleLeft, bubbleWidth, y;
            Vector2 corner;
            if (wide)
            {
                float reserved = UiStyle.Margin + MapSize + UiStyle.Gap;
                size = Mathf.Clamp((canvas.x - UiStyle.Margin - reserved - UiStyle.Gap - BubbleWidth) / (2f * TildaPresenter.BodyHalfWidth), MinWidePortraitSize, WidePortraitSize);
                float body = 2f * TildaPresenter.BodyHalfWidth * size;
                float left = Mathf.Max(reserved, (canvas.x - body - UiStyle.Gap - BubbleWidth) * 0.5f);
                bubbleLeft = left + body + UiStyle.Gap;
                bubbleWidth = BubbleWidth;
                corner = new Vector2(left + body * 0.5f - size * 0.5f, -20f);
                bool meetsHint = bubbleLeft + bubbleWidth > canvas.x - UiStyle.Margin - HintWidth - UiStyle.Gap;
                y = UiStyle.Margin + (meetsHint ? HintHeight + UiStyle.Gap : 0f);
            }
            else
            {
                size = PortraitSize;
                bubbleLeft = NarrowBubbleLeft;
                bubbleWidth = Mathf.Max(520f, canvas.x - NarrowBubbleLeft - NarrowSide);
                corner = new Vector2(4f, -8f);
                y = UiStyle.Margin + MapSize + UiStyle.Gap;
            }

            _holder.anchoredPosition = new Vector2(0f, y);
            _bubble.SetWidth(bubbleWidth);
            _bubble.Rect.Place(Vector2.zero, Vector2.zero, new Vector2(bubbleLeft, 0f), new Vector2(bubbleWidth, _bubble.Height));
            _layoutHeight = _bubble.Height;
            // Only the right half of the screen takes taps, so the floating thumbstick keeps working under the bubble.
            _bubble.SetTapFrom(canvas.x * 0.5f - bubbleLeft);

            _portrait.Place(Vector2.zero, Vector2.zero, corner, new Vector2(size, size));
            float pixels = size * (_canvas != null ? _canvas.scaleFactor : 1f);
            _tilda.SetResolution(pixels > 400f ? TildaPortrait.LargeSize : TildaPortrait.Size);
            _tilda.SetFullBody(wide);
            _tilda.PresentSide = 1f;
            _portraitShadow.gameObject.SetActive(wide);
            _portraitShadow.Place(Vector2.zero, new Vector2(0.5f, 0.5f),
                new Vector2(corner.x + size * 0.5f, corner.y + size * TildaPresenter.GroundFraction),
                new Vector2(size * TildaPresenter.ShadowWidth, size * TildaPresenter.ShadowHeight));

            Vector2 mouth = TildaPortrait.MouthIn(wide);
            Vector3 world = _portrait.TransformPoint(new Vector3(mouth.x * size, mouth.y * size, 0f));
            _bubble.PointTailAt(world, wide ? TailGap + 0.12f * size : 30f + 0.12f * size);

            // The skip button slides to the corner when there is no "Weiter" beside it; the hint takes what is left.
            if (_skip != null)
            {
                float offset = nextShown ? 268f + 16f : 0f;
                ((RectTransform)_skip.transform).anchoredPosition = new Vector2(-offset, 0f);
                float room = bubbleWidth - 2f * TildaBubble.PadX - offset - 236f - 16f;
                _bubble.SetHint(_remarkKey == null && room > 240f ? _hint : "", Mathf.Max(0f, room));
            }
        }

        // ---------------------------------------------------------------- arrow and highlight

        Island NearestIsland()
        {
            if (player == null) return null;
            Island best = null;
            float bestGap = float.MaxValue;
            foreach (var island in Island.All)
            {
                if (island == null || island == player || island.IsEmerging || island.IsSunk || island.LandArea <= 0f) continue;
                float gap = (island.PlanarPosition - player.PlanarPosition).magnitude - island.BoundingRadius;
                if (gap >= bestGap) continue;
                bestGap = gap;
                best = island;
            }
            return best;
        }

        void UpdateArrow(bool wanted, float dt, float time)
        {
            var cam = Camera.main;
            if (wanted)
            {
                _targetTimer -= dt;
                if (_arrowTarget == null || _targetTimer <= 0f)
                {
                    _targetTimer = targetRefresh;
                    _arrowTarget = NearestIsland();
                }
            }
            if (!wanted || _arrowTarget == null || cam == null)
            {
                SetActive(_arrow.gameObject, false);
                return;
            }
            SetActive(_arrow.gameObject, true);

            Vector2 p = _arrowTarget.PlanarPosition;
            Vector3 screen = cam.WorldToScreenPoint(new Vector3(p.x, 0f, p.y));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, _canvas.worldCamera, out var local);
            if (screen.z < 0f) local = -local;

            var rect = _root.rect;
            float bottom = rect.yMin + _holder.anchoredPosition.y + Mathf.Max(PortraitSize, _bubble.Height) + 130f;
            var inner = Rect.MinMaxRect(rect.xMin + 120f, bottom, rect.xMax - 120f, rect.yMax - 340f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(time * 4f);
            if (screen.z > 0f && inner.Contains(local))
            {
                _arrow.anchoredPosition = local + new Vector2(0f, 110f + pulse * 22f);
                _arrowGlyph.localRotation = Quaternion.Euler(0f, 0f, 180f);
                return;
            }
            Vector2 c = inner.center;
            Vector2 dir = local - c;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector2.up;
            float kx = Mathf.Abs(dir.x) > 1e-4f ? inner.width * 0.5f / Mathf.Abs(dir.x) : float.MaxValue;
            float ky = Mathf.Abs(dir.y) > 1e-4f ? inner.height * 0.5f / Mathf.Abs(dir.y) : float.MaxValue;
            Vector2 n = dir.normalized;
            _arrow.anchoredPosition = c + dir * Mathf.Min(kx, ky) - n * (pulse * 22f);
            _arrowGlyph.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(n.y, n.x) * Mathf.Rad2Deg - 90f);
        }

        readonly Vector3[] _corners = new Vector3[4];

        RectTransform RingTarget(TutorialStep step)
        {
            if (hud == null) return null;
            return step == TutorialStep.Buoyancy ? hud.BuoyancyRect : step == TutorialStep.Form ? hud.FormRect : null;
        }

        void UpdateRing(RectTransform target, float time)
        {
            if (target == null || !target.gameObject.activeInHierarchy)
            {
                SetActive(_ring.gameObject, false);
                return;
            }
            SetActive(_ring.gameObject, true);
            target.GetWorldCorners(_corners);
            Vector2 min = _root.InverseTransformPoint(_corners[0]);
            Vector2 max = _root.InverseTransformPoint(_corners[2]);
            float pulse = 0.5f + 0.5f * Mathf.Sin(time * 3.5f);
            float pad = 9f + pulse * 5f;
            _ring.anchoredPosition = (min + max) * 0.5f;
            _ring.sizeDelta = (max - min) + new Vector2(pad * 2f + 12f, pad * 2f);
            _ringImage.color = UiStyle.WithAlpha(UiStyle.Sand, 0.55f + 0.45f * pulse);
        }

        // ---------------------------------------------------------------- editor preview

        void EditorPreviewUpdate()
        {
            bool on = editorPreview != EditorPreview.None;
            Show(on);
            if (!on) return;
            TutorialStep step =
                editorPreview == EditorPreview.Arrow ? TutorialStep.Ram :
                editorPreview == EditorPreview.Buoyancy ? TutorialStep.Buoyancy :
                editorPreview == EditorPreview.Form ? TutorialStep.Form :
                editorPreview == EditorPreview.Sleepy ? TutorialStep.Move : TutorialStep.Greeting;
            string shown = step + "|" + TouchDevice + "|" + (UiStyle.Font != null ? UiStyle.Font.name : "");
            if (shown != _previewShown)
            {
                _previewShown = shown;
                SetStepTexts(step, (int)step + 1, step != TutorialStep.Ram && step != TutorialStep.Move);
                _layoutWidth = -1f;
                PlaceBubble();
                _bubble.ShowStill(TextFor(step, TouchDevice));
            }
            _shownVersion = -1;
            bool sleepy = editorPreview == EditorPreview.Sleepy;
            PlaceBubble();
            var pose = sleepy ? TildaPose.Sleepy : step == TutorialStep.Greeting ? TildaPose.Wave : step == TutorialStep.Buoyancy ? TildaPose.Cheer : _wide ? TildaPose.Present : TildaPose.Talk;
            _tilda.SetPose(pose, pose == TildaPose.Wave || pose == TildaPose.Talk || pose == TildaPose.Present);
            _targetTimer = 0f;
            UpdateArrow(step == TutorialStep.Ram, 0f, 0.4f);
            UpdateRing(RingTarget(step), 0.45f);
        }

        // ---------------------------------------------------------------- layout

        void Build()
        {
            UiStyle.DestroyChildrenNamed(transform, CanvasName);
            _canvas = UiStyle.Canvas(transform, CanvasName, sortingOrder, true, false, out _root);

            _ring = BuildRing(_root);
            _arrow = BuildArrow(_root);
            _holder = BuildBubble(_root);

            _previewShown = null;
            _layoutWidth = -1f;
            _holder.gameObject.SetActive(false);
            _arrow.gameObject.SetActive(false);
            _ring.gameObject.SetActive(false);
        }

        RectTransform BuildRing(RectTransform root)
        {
            _ringImage = UiStyle.Shape(root, "HighlightRing", UiSprites.Ring, UiStyle.Sand);
            return _ringImage.rectTransform.Center(Vector2.zero, new Vector2(200f, 80f));
        }

        RectTransform BuildArrow(RectTransform root)
        {
            var arrow = UiStyle.Rect(root, "IslandArrow").Center(Vector2.zero, new Vector2(150f, 150f));
            UiStyle.Shape(arrow, "Glow", UiSprites.SoftCircle, UiStyle.WithAlpha(UiStyle.Mint, 0.4f)).rectTransform.Stretch(-20f, -20f, -20f, -20f);
            _arrowGlyph = UiStyle.Rect(arrow, "Glyph").Stretch();
            var shadow = UiStyle.Shape(_arrowGlyph, "Shadow", UiSprites.Arrow, UiStyle.Shadow);
            shadow.rectTransform.Center(new Vector2(0f, -6f), new Vector2(116f, 116f));
            UiStyle.Shape(_arrowGlyph, "Body", UiSprites.Arrow, UiStyle.Mint).rectTransform.Center(Vector2.zero, new Vector2(108f, 108f));
            UiStyle.Shape(_arrowGlyph, "Core", UiSprites.Arrow, UiStyle.WithAlpha(UiStyle.Cream, 0.85f)).rectTransform.Center(new Vector2(0f, -4f), new Vector2(54f, 54f));
            return arrow;
        }

        // Input goes to the two buttons and to taps on the bubble's right-hand part only, so the floating
        // thumbstick on the left half of the screen keeps working underneath.
        RectTransform BuildBubble(RectTransform root)
        {
            var holder = UiStyle.Rect(root, "TildaBubble");
            holder.anchorMin = new Vector2(0f, 0f);
            holder.anchorMax = new Vector2(1f, 0f);
            holder.pivot = new Vector2(0.5f, 0f);
            holder.sizeDelta = new Vector2(0f, 10f);
            holder.anchoredPosition = new Vector2(0f, UiStyle.Margin);

            _portraitShadow = UiStyle.Shape(holder, "TildaShadow", UiSprites.SoftCircle, UiStyle.WithAlpha(UiStyle.Shadow, TildaPresenter.ShadowAlpha)).rectTransform;
            _portraitShadow.gameObject.SetActive(false);
            var portrait = TildaPortrait.CreateImage(holder, new Vector2(PortraitSize, PortraitSize));
            _portrait = portrait.rectTransform;
            _portrait.Place(Vector2.zero, Vector2.zero, new Vector2(4f, -8f), new Vector2(PortraitSize, PortraitSize));
            _tilda = portrait.GetComponent<TildaView>();
            _wide = false;

            // After her picture: the tail lies over her flank and points at her mouth.
            _bubble = TildaBubble.Create(holder, "Bubble", BubbleWidth, true);
            _skip = _bubble.AddFooterButton("SkipTutorial", "Überspringen", 236f, 0f, Skip);
            return holder;
        }
    }
}
