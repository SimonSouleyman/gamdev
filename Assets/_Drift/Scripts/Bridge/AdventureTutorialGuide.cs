using Drift.Audio;
using Drift.Core;
using Drift.Islands;
using Drift.SaveSystem;
using Drift.UI;
using Drift.Visuals;
using UnityEngine;
using UnityEngine.UI;

namespace Drift.Bridge
{
    // Tilda's Abenteuer briefing: in her "schnelle Brille" she talks a first-time racer through the ring in six
    // short steps (AdventureTutorialModel), in the same speech bubble and portrait as the cozy tutorial. It runs
    // only in Abenteuer (the cozy TutorialGuide only in Gemütlich), starts with the first adventure run while
    // AdventureTutorialProgress is not done or a replay was requested, and never holds the race up: steps end by
    // themselves, and the only write to the simulation is GameSession.SinkingSuspended during the first step.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class AdventureTutorialGuide : MonoBehaviour
    {
        public enum EditorPreview { None, Ring, Clock, Flotsam, Dodge, Surf, Go }

        const string CanvasName = "AdventureTutorialCanvas";
        const float BubbleWidth = 1000f, PortraitSize = 330f, WidePortraitSize = 680f, MinWidePortraitSize = 480f;
        const float NarrowBubbleLeft = 312f, NarrowSide = 20f, TailGap = 46f;
        const float MapSize = 340f, HintWidth = 760f, HintHeight = 64f;

        public GameSession session;
        public Island player;
        public WatchTools watch;
        public WorldHud hud;
        public ShipSystem ships;
        public EditorPreview editorPreview = EditorPreview.None;
        [Tooltip("Über den Sitzungs-Bildschirmen (10), unter den Beobachtungs-Werkzeugen (15) und dem Stick (20).")]
        public int sortingOrder = 12;
        [Tooltip("Tippgeschwindigkeit der Sprechblase in Zeichen pro Sekunde (etwas flotter als im gemütlichen Tutorial).")]
        public float charsPerSecond = 50f;
        [Tooltip("Unter dieser Canvas-Breite (Hochformat) sitzt die Blase über der Minikarte.")]
        public float wideLayoutWidth = 1850f;
        public float targetRefresh = 0.5f;

        static int s_playSession;
        static bool s_replayRequested;
        static AdventureTutorialGuide s_instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnPlaySessionStart()
        {
            s_playSession++;
            s_replayRequested = false;
        }

        AdventureTutorialModel _model = new AdventureTutorialModel();
        int _initSession = -1;
        bool _savedDone, _holdingSink, _wasVisible, _wide, _layoutNext;
        GameSession.State _lastState = (GameSession.State)(-1);
        int _flotsamSeen = -1, _shownVersion = -1;
        float _lookupTimer, _targetTimer, _layoutWidth = -1f, _layoutHeight = -1f;
        Vector2 _arrowTarget;
        bool _hasArrowTarget;
        string _previewShown, _hint = "";

        Canvas _canvas;
        RectTransform _root, _holder, _arrow, _arrowGlyph, _ring, _portrait, _portraitShadow;
        Image _ringImage;
        TildaView _tilda;
        TildaBubble _bubble;
        Button _skip;

        public AdventureTutorialModel Model => _model;
        public bool BubbleVisible => _holder != null && _holder.gameObject.activeSelf;
        // True while the briefing is running in a race; the cozy guide leaves Tilda's remarks alone meanwhile.
        public static bool Running => s_instance != null && s_instance._model.Active && GameModes.IsAdventure;

        // From the help screen: forget the done flag; in a running adventure she starts over right away, otherwise
        // with the next adventure run.
        public static void RequestReplay()
        {
            s_replayRequested = true;
            if (!Application.isPlaying) return;
            AdventureTutorialProgress.SetDone(false);
            if (s_instance != null) s_instance.ReplayNowIfRacing();
        }

        void ReplayNowIfRacing()
        {
            _savedDone = false;
            if (session == null || !session.Model.InGame || session.Mode != GameMode.Adventure) return;
            BeginBriefing();
            s_replayRequested = false;
        }

        void OnEnable()
        {
            s_instance = this;
            Build();
            RingWorld.Dodged += OnDodged;
            TildaVoice.LineStarted += OnLineStarted;
            _shownVersion = -1;
            _lookupTimer = 0f;
        }

        void OnDisable()
        {
            RingWorld.Dodged -= OnDodged;
            TildaVoice.LineStarted -= OnLineStarted;
            if (s_instance == this) s_instance = null;
            ReleaseSink();
        }

        public void Skip() => _model.Skip();

        public void Continue()
        {
            if (_bubble != null) _bubble.Next();
        }

        void OnDodged(Island island)
        {
            if (!Application.isPlaying || !GameModes.IsAdventure) return;
            _model.ReportDodge();
        }

        // During the briefing the bubble is hers; a remark that starts meanwhile is claimed and faded out at once.
        void OnLineStarted(TildaLine line)
        {
            if (line.shown || !Running) return;
            line.shown = true;
            TildaVoice.Hush(line.key);
        }

        void BeginBriefing()
        {
            _model.Begin();
            _flotsamSeen = ships != null ? ships.FlotsamCollectedTotal : -1;
            _shownVersion = -1;
            TildaVoice.Hush();
        }

        // ---------------------------------------------------------------- update

        void Resolve()
        {
            if (player == null)
                foreach (var i in Island.All) if (i != null && i.useKeyboardInput) { player = i; break; }
            if (watch == null) watch = GetComponent<WatchTools>();
            if (session != null && hud != null && ships != null) return;
            _lookupTimer -= Time.unscaledDeltaTime;
            if (_lookupTimer > 0f) return;
            _lookupTimer = 1f;
            if (session == null) session = FindAnyObjectByType<GameSession>();
            if (hud == null) hud = FindAnyObjectByType<WorldHud>();
            if (ships == null) ships = FindAnyObjectByType<ShipSystem>();
        }

        bool TouchDevice
        {
            get
            {
                var touch = GetComponent<TouchControls>();
                return InputMode.TouchPreferred || (touch != null && touch.forceShowTouch);
            }
        }

        void Update()
        {
            if (_canvas == null) return;
            if (s_instance == null) s_instance = this;
            Resolve();

            if (!Application.isPlaying)
            {
                EditorPreviewUpdate();
                return;
            }

            // Play Mode can start without a domain reload: per-play state is reset here, not in OnEnable.
            if (_initSession != s_playSession)
            {
                _initSession = s_playSession;
                _model = new AdventureTutorialModel();
                if (AdventureTutorialProgress.IsDone) _model.MarkDone();
                _savedDone = _model.Done;
                _lastState = (GameSession.State)(-1);
                _shownVersion = -1;
                _holdingSink = false;
            }
            if (session == null) { Show(false); return; }

            var s = session.Current;
            bool adventure = session.Mode == GameMode.Adventure;
            if (s != _lastState)
            {
                bool begins = adventure && s == GameSession.State.Playing && _lastState != GameSession.State.Paused
                              && session.Stats.timeSurvived < 0.5f && session.Stats.islandsAbsorbed == 0;
                _lastState = s;
                // A run that sank during the briefing starts it again, unless it was already past the ring and clock.
                if (begins && (!_model.Done || s_replayRequested))
                {
                    if (!_model.Active || _model.Step < AdventureTutorialStep.Flotsam) BeginBriefing();
                    s_replayRequested = false;
                }
            }

            bool visible = adventure && _model.Active && s == GameSession.State.Playing && !(watch != null && (watch.PhotoActive || watch.JournalOpen));
            if (visible) Feed();
            visible &= _model.Active;

            if (_model.Done && !_savedDone)
            {
                _savedDone = true;
                AdventureTutorialProgress.SetDone(true);
            }

            if (_wasVisible && !visible) TildaVoice.Quiet();
            _wasVisible = visible;
            Show(visible);
            if (visible) Draw(Time.unscaledDeltaTime, Time.unscaledTime);
        }

        // After every Update, so a guide that clears the shared flag earlier in the frame cannot undo the hold.
        void LateUpdate()
        {
            if (!Application.isPlaying || session == null) return;
            bool hold = session.Mode == GameMode.Adventure && session.Current == GameSession.State.Playing && _model.SinkingSuspended;
            if (hold == _holdingSink && !hold) return;
            _holdingSink = hold;
            session.SinkingSuspended = hold;
        }

        void ReleaseSink()
        {
            if (!_holdingSink) return;
            _holdingSink = false;
            if (session != null) session.SinkingSuspended = false;
        }

        void Feed()
        {
            float dt = Time.unscaledDeltaTime;
            if (player != null) _model.ReportSurf(player.SurfStrength, Time.deltaTime);
            if (ships != null)
            {
                int total = ships.FlotsamCollectedTotal;
                if (_flotsamSeen >= 0 && total > _flotsamSeen) _model.ReportFlotsam();
                _flotsamSeen = total;
            }
            _model.Tick(dt);
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

        // ---------------------------------------------------------------- texts

        static string K(string word) => TildaBubble.Key(word);

        // Race-coach Tilda, still warm. Rich text: TildaBubble.Key marks the one or two words a step is about.
        public static string TextFor(AdventureTutorialStep step, bool touchDevice)
        {
            switch (step)
            {
                case AdventureTutorialStep.Ring:
                    return "Brille auf, Schatz – jetzt wird gerast! Du fährst " + K("immer") + ", ganz von allein. Du " + K("lenkst nur") + " nach links und rechts; zurück bremst, stehen bleibst du nie.";
                case AdventureTutorialStep.Clock:
                    return "Die " + K("Uhr") + " oben ist deine Punktzahl: Jede Sekunde zählt. Aber Achtung, ab jetzt " + K("sinkt") + " deine Insel langsam!";
                case AdventureTutorialStep.Flotsam:
                    return touchDevice
                        ? "Dagegen hilft " + K("Treibgut") + ": Kisten, Fässer, Flaschen. Einfach drüberfahren – jedes Teil " + K("hebt") + " dich wieder."
                        : "Dagegen hilft " + K("Treibgut") + ": Kisten, Fässer, Flaschen. Einfach mitnehmen – jedes Teil " + K("hebt") + " dich wieder.";
                case AdventureTutorialStep.Dodge:
                    return "Die Inseln sind hier " + K("Hindernisse") + ". " + K("Weich aus") + "! Jeder Rempler kostet Tempo und viel Auftrieb. Am " + K("Rand") + " endet die Welt.";
                case AdventureTutorialStep.Surf:
                    return "Siehst du die breiten " + K("Schaumspuren") + "? Dort " + K("surfst") + " du und wirst richtig schnell – sie wandern, bleib dran!";
                case AdventureTutorialStep.Go:
                    return "Mehr brauchst du nicht. Jede " + K("Stufe") + " macht dich schneller – auf die Plätze, fertig, " + K("los") + "!";
                default:
                    return "";
            }
        }

        static string HintFor(AdventureTutorialStep step)
        {
            switch (step)
            {
                case AdventureTutorialStep.Flotsam: return "Schnapp dir Treibgut!";
                case AdventureTutorialStep.Dodge: return "Weich der Insel aus!";
                case AdventureTutorialStep.Surf: return "Ab an die Plattengrenze!";
                default: return "";
            }
        }

        public static TildaPose PoseFor(AdventureTutorialStep step, bool cheering, bool typing, bool wide)
        {
            if (cheering || step == AdventureTutorialStep.Go) return TildaPose.Cheer;
            if (step == AdventureTutorialStep.Ring && typing) return TildaPose.Wave;
            if (!typing) return TildaPose.Idle;
            return wide ? TildaPose.Present : TildaPose.Talk;
        }

        void Draw(float dt, float time)
        {
            var step = _model.Step;
            if (_model.Version != _shownVersion)
            {
                _shownVersion = _model.Version;
                SetStepTexts(step, _model.StepNumber, _model.CanContinue);
                bool cheer = _model.Cheering;
                var mood = cheer ? GrumbleMood.Giggly : step == AdventureTutorialStep.Ring || step == AdventureTutorialStep.Go ? GrumbleMood.Cheerful : GrumbleMood.Warm;
                _layoutWidth = -1f;
                PlaceBubble();
                _bubble.Show(TextFor(step, TouchDevice), mood, charsPerSecond, !_model.CanContinue);
            }
            PlaceBubble();
            bool typing = _bubble.Typing;
            _tilda.SetPose(PoseFor(step, _model.Cheering, typing, _wide), typing);
            UpdateArrow(step, _model.ShowsArrow, dt, time);
            UpdateRing(step == AdventureTutorialStep.Clock && hud != null ? hud.BuoyancyRect : null, time);
        }

        void SetStepTexts(AdventureTutorialStep step, int number, bool canContinue)
        {
            _bubble.SetCounter(number + " / " + AdventureTutorialModel.StepCount);
            _bubble.SetFinal(canContinue ? (step == AdventureTutorialStep.Go ? "Los!" : "Weiter") : null, () => _model.Continue());
            _hint = HintFor(step);
        }

        // The cozy tutorial's layout: wide screens put a big standing Tilda at the bubble's left end between the
        // minimap and the hint chip, portrait screens the bust above the minimap. The tail aims at her mouth.
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

            if (_skip != null)
            {
                float offset = nextShown ? 268f + 16f : 0f;
                ((RectTransform)_skip.transform).anchoredPosition = new Vector2(-offset, 0f);
                float room = bubbleWidth - 2f * TildaBubble.PadX - offset - 236f - 16f;
                _bubble.SetHint(room > 240f ? _hint : "", Mathf.Max(0f, room));
            }
        }

        // ---------------------------------------------------------------- arrow and highlight

        // What the arrow points at: the next piece of flotsam ahead, or the island in the way.
        bool ArrowTarget(AdventureTutorialStep step, out Vector2 target)
        {
            target = Vector2.zero;
            if (player == null) return false;
            Vector2 pos = player.PlanarPosition;
            Vector2 fwd = new Vector2(player.Forward.x, player.Forward.z);
            float best = float.MaxValue;
            bool found = false;
            if (step == AdventureTutorialStep.Flotsam)
            {
                if (ships == null) return false;
                for (int i = 0; i < ships.FlotsamSlots; i++)
                {
                    if (!ships.FlotsamActive(i) || ships.FlotsamCollecting(i)) continue;
                    if (!ShipSystem.IsCollectible(ships.FlotsamKindOf(i))) continue;
                    Vector2 d = ships.FlotsamPosition(i) - pos;
                    if (Vector2.Dot(d, fwd) <= 0f || d.magnitude >= best) continue;
                    best = d.magnitude;
                    target = ships.FlotsamPosition(i);
                    found = true;
                }
                return found;
            }
            foreach (var island in Island.All)
            {
                if (island == null || island == player || island.IsEmerging || island.IsSunk || island.LandArea <= 0f) continue;
                Vector2 d = island.PlanarPosition - pos;
                if (Vector2.Dot(d, fwd) <= 0f) continue;
                float gap = d.magnitude - island.BoundingRadius;
                if (gap >= best) continue;
                best = gap;
                target = island.PlanarPosition;
                found = true;
            }
            return found;
        }

        void UpdateArrow(AdventureTutorialStep step, bool wanted, float dt, float time)
        {
            var cam = Camera.main;
            if (wanted)
            {
                _targetTimer -= dt;
                if (!_hasArrowTarget || _targetTimer <= 0f)
                {
                    _targetTimer = targetRefresh;
                    _hasArrowTarget = ArrowTarget(step, out _arrowTarget);
                }
            }
            else _hasArrowTarget = false;
            if (!wanted || !_hasArrowTarget || cam == null)
            {
                SetActive(_arrow.gameObject, false);
                return;
            }
            SetActive(_arrow.gameObject, true);

            Vector2 p = _arrowTarget;
            Vector3 screen = cam.WorldToScreenPoint(new Vector3(p.x, 0f, p.y));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out var local);
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
            var step = (AdventureTutorialStep)((int)editorPreview - 1);
            string shown = step + "|" + TouchDevice + "|" + (UiStyle.Font != null ? UiStyle.Font.name : "");
            if (shown != _previewShown)
            {
                _previewShown = shown;
                SetStepTexts(step, (int)step + 1, !AdventureTutorialModel.IsAction(step));
                _layoutWidth = -1f;
                PlaceBubble();
                _bubble.ShowStill(TextFor(step, TouchDevice));
            }
            _shownVersion = -1;
            PlaceBubble();
            var pose = PoseFor(step, false, true, _wide);
            _tilda.SetPose(pose, pose != TildaPose.Cheer);
            _targetTimer = 0f;
            UpdateArrow(step, AdventureTutorialModel.IsAction(step) && step != AdventureTutorialStep.Surf, 0f, 0.4f);
            UpdateRing(step == AdventureTutorialStep.Clock && hud != null ? hud.BuoyancyRect : null, 0.45f);
        }

        // ---------------------------------------------------------------- layout

        void Build()
        {
            UiStyle.DestroyChildrenNamed(transform, CanvasName);
            _canvas = UiStyle.Canvas(transform, CanvasName, sortingOrder, true, out _root);

            _ringImage = UiStyle.Shape(_root, "HighlightRing", UiSprites.Ring, UiStyle.Sand);
            _ring = _ringImage.rectTransform.Center(Vector2.zero, new Vector2(200f, 80f));
            _arrow = BuildArrow(_root);
            _holder = BuildBubble(_root);

            _previewShown = null;
            _layoutWidth = -1f;
            _holder.gameObject.SetActive(false);
            _arrow.gameObject.SetActive(false);
            _ring.gameObject.SetActive(false);
        }

        RectTransform BuildArrow(RectTransform root)
        {
            var arrow = UiStyle.Rect(root, "IslandArrow").Center(Vector2.zero, new Vector2(150f, 150f));
            UiStyle.Shape(arrow, "Glow", UiSprites.SoftCircle, UiStyle.WithAlpha(UiStyle.Mint, 0.4f)).rectTransform.Stretch(-20f, -20f, -20f, -20f);
            _arrowGlyph = UiStyle.Rect(arrow, "Glyph").Stretch();
            UiStyle.Shape(_arrowGlyph, "Shadow", UiSprites.Arrow, UiStyle.Shadow).rectTransform.Center(new Vector2(0f, -6f), new Vector2(116f, 116f));
            UiStyle.Shape(_arrowGlyph, "Body", UiSprites.Arrow, UiStyle.Mint).rectTransform.Center(Vector2.zero, new Vector2(108f, 108f));
            UiStyle.Shape(_arrowGlyph, "Core", UiSprites.Arrow, UiStyle.WithAlpha(UiStyle.Cream, 0.85f)).rectTransform.Center(new Vector2(0f, -4f), new Vector2(54f, 54f));
            return arrow;
        }

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

            _bubble = TildaBubble.Create(holder, "Bubble", BubbleWidth, true);
            _skip = _bubble.AddFooterButton("SkipAdventureTutorial", "Überspringen", 236f, 0f, Skip);
            return holder;
        }
    }
}
