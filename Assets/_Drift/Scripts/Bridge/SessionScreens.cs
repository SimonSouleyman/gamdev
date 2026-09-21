using System;
using Drift.Islands;
using Drift.SaveSystem;
using Drift.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Drift.Bridge
{
    [ExecuteAlways]
    [RequireComponent(typeof(TouchControls))]
    public class SessionScreens : MonoBehaviour
    {
        public enum EditorPreview { None, Title, Pause, GameOver, Help }

        const string CanvasName = "SessionScreensCanvas";
        const int EditorSampleSeed = 482913;

        public GameSession session;
        public Island player;
        public IslandChaseCamera chaseCamera;
        public TouchControls touch;
        // Optional sibling component (Phase 7): journal, photo mode and photo album from the menus.
        public WatchTools watch;
        // Optional sibling component: Tilda's tutorial, restarted from the Anleitung screen.
        public TutorialGuide tutorial;
        public EditorPreview editorPreview = EditorPreview.None;
        [Range(0, HelpScreen.PageCount - 1)] public int editorHelpPage;
        public bool editorHelpTouchFirst;
        public int sortingOrder = 10;
        public string tagline = "Sammle Inseln, bevor deine versinkt.";
        public int previewSize = 512;
        public float previewFps = 4f;
        // Tilda beside the menus: her picture's share of the canvas height on wide screens.
        [Range(0.3f, 0.7f)] public float presenterTitle = 0.6f;
        [Range(0.3f, 0.7f)] public float presenterPause = 0.42f;
        [Range(0.3f, 0.7f)] public float presenterGameOver = 0.5f;
        [Range(0.3f, 0.7f)] public float presenterHelp = 0.56f;
        // Seconds of silence on the title before Tilda drops one of her idle remarks.
        public float idleRemarkSeconds = 28f;

        static bool s_greeted, s_pauseLineSaid;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnPlaySessionStart() => s_greeted = s_pauseLineSaid = false;

        enum Stage { None, Title, Pause, GameOver, Help }

        Canvas _canvas;
        RectTransform _root;
        TildaPresenter _presenter;
        Stage _stage = Stage.None;
        UiToggle _pauseVoice;
        float _idleTimer;
        int _idleIndex;
        bool _overCheered;
        GameObject _title, _pause, _over, _pauseButton;
        Button _continueButton;
        RectTransform _titleExtras;
        readonly HelpScreen _help = new HelpScreen();
        int _shownHelpPage = -1;
        bool _shownHelpTouch;
        Text _statsText, _countdownText, _overSeedText, _pauseSeedText;
        InputField _seedField;
        RawImage _titlePreview, _overPreview;
        IslandPreview _preview;
        float _saveCheckTimer;
        float _lookupTimer;
        bool _hasSave;
        GameSession.State _shown = (GameSession.State)(-1);
        Func<Vector2> _provider;

        public IslandPreview Preview => _preview;
        public InputField SeedField => _seedField;
        public bool HelpOpen => _help.IsOpen;
        public HelpScreen Help => _help;
        public TildaPresenter Presenter => _presenter;
        WatchTools Watch => watch != null ? watch : (watch = GetComponent<WatchTools>());
        TutorialGuide Tutorial => tutorial != null ? tutorial : (tutorial = GetComponent<TutorialGuide>());

        public void OpenHelp(int page = 0)
        {
            var t = Tutorial;
            _help.Open(page, InputMode.TouchPreferred || (touch != null && touch.forceShowTouch), t != null && t.ReplayPending);
        }

        public void CloseHelp() => _help.Hide();

        void OnReplayTutorial()
        {
            var t = Tutorial;
            if (t != null) t.RequestReplay();
        }

        void OnEnable()
        {
            if (touch == null) touch = GetComponent<TouchControls>();
            if (watch == null) watch = GetComponent<WatchTools>();
            if (tutorial == null) tutorial = GetComponent<TutorialGuide>();
            Build();
            _shown = (GameSession.State)(-1);
            _lookupTimer = 0f;
            if (!Application.isPlaying) return;
            EnsureEventSystem();
            _provider = ProvideMove;
            Island.InputProvider = _provider;
            Resolve();
            Attach(session);
        }

        void OnDisable()
        {
            Detach(session);
            if (_provider != null && Island.InputProvider == _provider) Island.InputProvider = null;
            _provider = null;
            _preview?.Dispose();
            _preview = null;
        }

        void Attach(GameSession s)
        {
            if (s == null) return;
            s.StateChanged += OnStateChanged;
            s.WorldSeedChanged += OnSeedChanged;
            _hasSave = s.HasSave;
            RefreshSeedTexts();
        }

        void Detach(GameSession s)
        {
            if (s == null) return;
            s.StateChanged -= OnStateChanged;
            s.WorldSeedChanged -= OnSeedChanged;
        }

        // Scene-wide searches are only retried once a second so a legitimately absent reference does not
        // cost a FindAnyObjectByType every frame.
        void Resolve()
        {
            if (player == null)
                foreach (var i in Island.All) if (i != null && i.useKeyboardInput) { player = i; break; }
            if (touch == null) touch = GetComponent<TouchControls>();
            if (watch == null) watch = GetComponent<WatchTools>();
            if (session != null && chaseCamera != null) return;
            _lookupTimer -= Time.unscaledDeltaTime;
            if (_lookupTimer > 0f) return;
            _lookupTimer = 1f;
            if (session == null) session = FindAnyObjectByType<GameSession>();
            if (chaseCamera == null) chaseCamera = FindAnyObjectByType<IslandChaseCamera>();
        }

        Vector2 ProvideMove()
        {
            if (touch == null || session == null || session.Current != GameSession.State.Playing) return Vector2.zero;
            return touch.Move;
        }

        void OnStateChanged(GameSession.State s)
        {
            _saveCheckTimer = 0f;
            _hasSave = session != null && session.HasSave;
            _preview?.RequestRender();
            RefreshSeedTexts();
        }

        void OnSeedChanged()
        {
            _preview?.RequestRender();
            RefreshSeedTexts();
        }

        void Update()
        {
            if (_canvas == null) return;

            if (!Application.isPlaying)
            {
                bool t = editorPreview == EditorPreview.Title, p = editorPreview == EditorPreview.Pause, o = editorPreview == EditorPreview.GameOver;
                ShowPanels(t, p, o, false);
                SetContinueVisible(true);
                if (editorPreview != EditorPreview.Help)
                {
                    _help.Hide();
                    _shownHelpPage = -1;
                }
                else if (!_help.IsOpen || _shownHelpPage != editorHelpPage || _shownHelpTouch != editorHelpTouchFirst)
                {
                    _shownHelpPage = editorHelpPage;
                    _shownHelpTouch = editorHelpTouchFirst;
                    _help.Open(editorHelpPage, editorHelpTouchFirst, false);
                }
                if (o) FillGameOver(new SessionStats { timeSurvived = 83f, islandsAbsorbed = 4, volcanoesAbsorbed = 1, peakLandMass = 212f }, 3.2f);
                UpdatePresenter(editorPreview == EditorPreview.Help ? Stage.Help : t ? Stage.Title : p ? Stage.Pause : o ? Stage.GameOver : Stage.None, 3.2f);
                if (editorPreview != EditorPreview.None) FillEditorSample();
                if (player == null)
                    foreach (var i in Island.All) if (i != null && i.useKeyboardInput) { player = i; break; }
                TickPreview(t || o, previewFps);
                return;
            }

            if (_provider == null)
            {
                EnsureEventSystem();
                _provider = ProvideMove;
                Island.InputProvider = _provider;
            }

            var prevSession = session;
            Resolve();
            if (session == null) return;
            if (session != prevSession)
            {
                Detach(prevSession);
                Attach(session);
            }

            var s = session.Current;
            if (s != _shown)
            {
                if (_shown == GameSession.State.GameOver && s == GameSession.State.Playing) TildaVoice.Say(TildaVoiceLines.NewIsland, VoicePriority.Queue);
                _shown = s;
                ShowPanels(s == GameSession.State.Title, s == GameSession.State.Paused, s == GameSession.State.GameOver, s == GameSession.State.Playing);
                if (s != GameSession.State.Title && s != GameSession.State.Paused) _help.Hide();
                if (s == GameSession.State.Title) RefreshSeedField(true);
            }

            if (s == GameSession.State.Title)
            {
                _saveCheckTimer -= Time.unscaledDeltaTime;
                if (_saveCheckTimer <= 0f)
                {
                    _saveCheckTimer = 0.5f;
                    _hasSave = session.HasSave;
                }
                SetContinueVisible(_hasSave);
            }
            else if (s == GameSession.State.GameOver)
            {
                FillGameOver(session.Stats, session.RestartCountdown);
            }

            // The sunk island does not change any more, so the game-over picture is rendered once.
            TickPreview(s == GameSession.State.Title || s == GameSession.State.GameOver, s == GameSession.State.Title ? previewFps : 0f);

            // Photo mode, the journal and the album borrow the screen: no pause button, no thumbstick, and
            // Escape (Android back) leaves them instead of toggling the pause menu.
            bool photo = watch != null && watch.PhotoActive;
            bool journal = watch != null && watch.JournalOpen;
            bool album = watch != null && watch.AlbumOpen;
            bool pauseButton = s == GameSession.State.Playing && !photo && !journal && !album;
            if (_pauseButton != null && _pauseButton.activeSelf != pauseButton) _pauseButton.SetActive(pauseButton);

            // The journal, the album and photo mode lie over the menus; Tilda steps aside for them.
            bool covered = photo || journal || album;
            // Hidden, not just dimmed: through the translucent journal card the pause buttons read as a
            // blurred second menu and make the small journal text look out of focus.
            SetPanelActive(_title, !covered && s == GameSession.State.Title);
            SetPanelActive(_pause, !covered && s == GameSession.State.Paused);
            SetPanelActive(_over, !covered && s == GameSession.State.GameOver);
            UpdatePresenter(covered ? Stage.None : _help.IsOpen ? Stage.Help
                : s == GameSession.State.Title ? Stage.Title : s == GameSession.State.Paused ? Stage.Pause
                : s == GameSession.State.GameOver ? Stage.GameOver : Stage.None, session.RestartCountdown);

            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && !(_seedField != null && _seedField.isFocused))
            {
                if (_help.IsOpen) _help.Hide();
                else if (album) watch.AlbumBack();
                else if (photo) watch.ExitPhotoMode();
                else if (journal) watch.CloseJournal();
                else session.TogglePause();
            }

            if (touch != null)
            {
                bool following = watch != null && watch.Following;
                touch.inputEnabled = s == GameSession.State.Playing && !photo && !journal && !album && !following;
                float pinch = touch.ConsumePinchFactor();
                if (chaseCamera != null && !photo && !following && Mathf.Abs(pinch - 1f) > 1e-4f) chaseCamera.ZoomBy(pinch);
            }
        }

        // ---------------------------------------------------------------- Tilda beside the menus

        void UpdatePresenter(Stage stage, float countdown)
        {
            if (_presenter == null || _root == null) return;
            Vector2 canvas = TildaPresenter.CanvasSize(_root);
            var placement = TildaPresenter.Placement.Hidden;
            switch (stage)
            {
                case Stage.Title: placement = _presenter.Present((RectTransform)_title.transform, canvas, TitlePanel, presenterTitle); break;
                case Stage.Pause: placement = _presenter.Present((RectTransform)_pause.transform, canvas, PausePanel, presenterPause); break;
                case Stage.GameOver: placement = _presenter.Present((RectTransform)_over.transform, canvas, OverPanel, presenterGameOver); break;
                // In portrait the Anleitung keeps its own small Tilda next to her tip card.
                case Stage.Help: placement = _presenter.Present((RectTransform)_help.Root.transform, canvas, HelpScreen.PanelSize, presenterHelp, false); break;
                default: _presenter.Dismiss(); break;
            }
            _help.UsePresenter(stage == Stage.Help && placement == TildaPresenter.Placement.Side ? _presenter.View : null);

            bool entered = stage != _stage;
            if (entered)
            {
                LeaveStage(_stage);
                _stage = stage;
            }
            // The Anleitung poses her itself. What she says does not depend on whether there is room to show her.
            if (stage == Stage.None || stage == Stage.Help) return;
            var view = _presenter.View;
            if (!Application.isPlaying)
            {
                if (stage == Stage.Title) view.SetPose(TildaPose.Wave);
                else if (stage == Stage.Pause) view.SetPose(TildaPose.Idle);
                else if (stage == Stage.GameOver) view.SetPose(TildaPose.Comfort, true);
                return;
            }
            switch (stage)
            {
                case Stage.Title: TitleStage(view, entered); break;
                case Stage.Pause: if (entered) PauseStage(view); break;
                case Stage.GameOver: GameOverStage(view, entered, countdown); break;
            }
        }

        // What she said for a menu ends with it; the game-over line may run on into the new game.
        void LeaveStage(Stage stage)
        {
            if (!Application.isPlaying) return;
            if (stage == Stage.Title) TildaVoice.Hush(TildaVoiceLines.Greeting, "idle_");
            else if (stage == Stage.Pause) TildaVoice.Hush(TildaVoiceLines.Pause);
            else if (stage == Stage.Help) TildaVoice.Hush("help_");
        }

        void TitleStage(TildaView view, bool entered)
        {
            if (entered)
            {
                _idleTimer = 0f;
                view.SetPose(TildaPose.Idle);
                bool greets = !s_greeted && TildaVoice.Say(TildaVoiceLines.Greeting, VoicePriority.Queue);
                s_greeted = true;
                float seconds = greets ? TildaVoice.LengthOf(TildaVoiceLines.Greeting) : 0f;
                view.Play(TildaPose.Wave, greets ? Mathf.Min(4.5f, seconds) : 2.2f, seconds);
                return;
            }
            if (TildaVoice.Speaking || !TildaVoice.Enabled || _idleIndex >= TildaVoiceLines.IdleCount || (_seedField != null && _seedField.isFocused))
            {
                _idleTimer = 0f;
                return;
            }
            _idleTimer += Time.unscaledDeltaTime;
            if (_idleTimer < idleRemarkSeconds) return;
            _idleTimer = 0f;
            string key = TildaVoiceLines.Idle(_idleIndex++);
            if (!TildaVoice.Say(key, VoicePriority.IfSilent)) return;
            float length = TildaVoice.LengthOf(key);
            view.Play(TildaPose.Present, length, length);
        }

        void PauseStage(TildaView view)
        {
            view.SetPose(TildaPose.Idle);
            if (s_pauseLineSaid || !Application.isFocused) return;
            if (!TildaVoice.Say(TildaVoiceLines.Pause, VoicePriority.IfSilent)) return;
            s_pauseLineSaid = true;
            view.Play(TildaPose.Idle, 0f, TildaVoice.LengthOf(TildaVoiceLines.Pause));
        }

        void GameOverStage(TildaView view, bool entered, float countdown)
        {
            if (entered)
            {
                _overCheered = false;
                view.SetPose(TildaPose.Idle);
                bool spoken = TildaVoice.Say(TildaVoiceLines.GameOver, VoicePriority.Interrupt);
                float seconds = spoken ? TildaVoice.LengthOf(TildaVoiceLines.GameOver) : 3f;
                view.Play(TildaPose.Comfort, seconds, seconds);
                return;
            }
            if (_overCheered || countdown <= 0f || countdown > 1f) return;
            _overCheered = true;
            view.SetPose(TildaPose.Cheer);
        }

        void OnToggleVoice()
        {
            TildaVoice.Toggle();
            RefreshVoiceLabels();
        }

        void RefreshVoiceLabels()
        {
            if (_pauseVoice != null) _pauseVoice.Set(TildaVoice.Enabled);
            _help.RefreshVoiceLabel();
        }

        void TickPreview(bool visible, float fps)
        {
            if (_preview == null)
            {
                if (!visible) return;
                _preview = new IslandPreview(transform) { size = Mathf.Clamp(previewSize, 64, 2048) };
            }
            _preview.Tick(player, visible, fps, Time.unscaledDeltaTime);
            var tex = _preview.Texture;
            if (_titlePreview != null && _titlePreview.texture != tex) _titlePreview.texture = tex;
            if (_overPreview != null && _overPreview.texture != tex) _overPreview.texture = tex;
        }

        // Without a save the Anleitung / Fotoalbum row moves up into the slot of the hidden "Weiter".
        void SetContinueVisible(bool on)
        {
            if (_continueButton == null) return;
            if (_continueButton.gameObject.activeSelf != on) _continueButton.gameObject.SetActive(on);
            if (_titleExtras != null) _titleExtras.anchoredPosition = new Vector2(0f, on ? TitleHelpY : TitleContinueY);
        }

        static void SetPanelActive(GameObject panel, bool on)
        {
            if (panel != null && panel.activeSelf != on) panel.SetActive(on);
        }

        void ShowPanels(bool title, bool pause, bool over, bool pauseButton)
        {
            if (_title != null) _title.SetActive(title);
            if (pause && _pause != null && !_pause.activeSelf) RefreshVoiceLabels();
            if (_pause != null) _pause.SetActive(pause);
            if (_over != null) _over.SetActive(over);
            if (_pauseButton != null) _pauseButton.SetActive(pauseButton);
        }

        void FillGameOver(SessionStats st, float countdown)
        {
            if (_statsText != null)
            {
                int secs = Mathf.FloorToInt(st.timeSurvived);
                _statsText.text =
                    $"Überlebt   {secs / 60}:{secs % 60:00}\n" +
                    $"Inseln gesammelt   {st.islandsAbsorbed}\n" +
                    $"Davon Vulkane   {st.volcanoesAbsorbed}\n" +
                    $"Größte Landmasse   {st.peakLandMass:F0}";
            }
            if (_countdownText != null)
                _countdownText.text = countdown > 0f ? $"Neue Insel in {Mathf.CeilToInt(countdown)} …" : "";
        }

        void FillEditorSample()
        {
            string label = session != null && !session.LegacySeeds ? session.SeedLabel : $"Welt #{EditorSampleSeed}";
            if (_overSeedText != null) _overSeedText.text = label;
            if (_pauseSeedText != null) _pauseSeedText.text = label;
            if (_seedField != null && string.IsNullOrEmpty(_seedField.text))
                _seedField.SetTextWithoutNotify(session != null && !session.LegacySeeds ? session.WorldSeed.ToString() : EditorSampleSeed.ToString());
        }

        void RefreshSeedTexts()
        {
            string label = session != null ? session.SeedLabel : "";
            if (_overSeedText != null) _overSeedText.text = label;
            if (_pauseSeedText != null) _pauseSeedText.text = label;
            RefreshSeedField(false);
        }

        void RefreshSeedField(bool force)
        {
            if (_seedField == null || session == null) return;
            if (!force && _seedField.isFocused) return;
            _seedField.SetTextWithoutNotify(session.LegacySeeds ? "" : session.WorldSeed.ToString());
        }

        // ---------------------------------------------------------------- seed row

        int? FieldSeed()
        {
            if (_seedField == null) return null;
            return WorldSeeds.TryParse(_seedField.text, out int s) ? s : (int?)null;
        }

        void OnStartPressed()
        {
            if (session == null) return;
            var s = FieldSeed();
            if (s.HasValue) session.StartNewGame(s.Value);
            else session.StartNewGame();
        }

        void OnSeedEdited(string text)
        {
            if (session == null) return;
            var s = FieldSeed();
            if (!s.HasValue) { RefreshSeedField(true); return; }
            if (!session.LegacySeeds && s.Value == session.WorldSeed) { RefreshSeedField(true); return; }
            session.PreviewSeed(s.Value);
            RefreshSeedField(true);
        }

        void OnRandomSeed()
        {
            int s = WorldSeeds.Random();
            if (_seedField != null) _seedField.SetTextWithoutNotify(s.ToString());
            if (session != null && Application.isPlaying) session.PreviewSeed(s);
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }

        // ---------------------------------------------------------------- layout

        const float ButtonWidth = 680f;
        static readonly Vector2 TitlePanel = new Vector2(920f, 1540f), OverPanel = new Vector2(880f, 1270f);
        static Vector2 PausePanel => new Vector2(880f, Application.isMobilePlatform ? 1196f : 1360f);
        const float TitleContinueY = -1144f, TitleHelpY = -1304f;

        void Build()
        {
            UiStyle.DestroyChildrenNamed(transform, CanvasName);
            IslandPreview.DestroyExisting(transform);
            _preview?.Dispose();
            _preview = null;

            _canvas = UiStyle.Canvas(transform, CanvasName, sortingOrder, true, out var root);
            _root = root;
            _title = BuildTitle(root);
            _pause = BuildPause(root);
            _over = BuildGameOver(root);
            _pauseButton = BuildPauseButton(root);
            _help.Build(root, null, OnReplayTutorial, OnToggleVoice);
            _presenter = TildaPresenter.Create(root);
            _stage = Stage.None;
            _shownHelpPage = -1;
            ShowPanels(false, false, false, false);
        }

        static Button Primary(RectTransform panel, string name, string label, float y, Action onClick, float height = UiStyle.ButtonHeight)
        {
            var b = UiStyle.PrimaryButton(panel, name, label, new Vector2(ButtonWidth, height), onClick);
            ((RectTransform)b.transform).TopCenter(new Vector2(0f, y), new Vector2(ButtonWidth, height));
            return b;
        }

        static Button Secondary(RectTransform panel, string name, string label, Vector2 pos, Vector2 size, Action onClick, bool warning = false)
        {
            var b = UiStyle.SecondaryButton(panel, name, label, size, onClick, warning);
            ((RectTransform)b.transform).TopCenter(pos, size);
            return b;
        }

        static Text Line(RectTransform panel, string text, int size, Color color, float y, float height, bool bold = false)
        {
            var t = UiStyle.Label(panel, text, size, color, TextAnchor.UpperCenter, bold);
            t.rectTransform.TopCenter(new Vector2(0f, y), new Vector2(840f, height));
            return t;
        }

        static RectTransform Screen(RectTransform root, string name, Color scrim, Vector2 panelSize, out RectTransform panel)
        {
            var screen = UiStyle.Scrim(root, name, scrim);
            panel = UiStyle.Panel(screen, "Panel", panelSize, true).Center(Vector2.zero, panelSize);
            UiStyle.FadeIn(screen.gameObject, panel);
            return screen;
        }

        GameObject BuildTitle(RectTransform root)
        {
            var screen = Screen(root, "TitleScreen", UiStyle.DimLight, TitlePanel, out var panel);
            Line(panel, "Drift", UiStyle.Display, UiStyle.Sand, -30f, 176f, true);
            Line(panel, tagline, UiStyle.Body, UiStyle.CreamSoft, -204f, 50f);

            _titlePreview = UiStyle.Picture(panel, "Preview", new Vector2(500f, 500f), out var frame);
            frame.TopCenter(new Vector2(0f, -276f), new Vector2(500f, 500f));

            Line(panel, "Welt-Nummer", UiStyle.Caption, UiStyle.Muted, -796f, 36f);
            _seedField = MakeSeedField(panel, "SeedField", new Vector2(-64f, -838f), new Vector2(440f, 104f));
            _seedField.onEndEdit.AddListener(OnSeedEdited);
            var dice = UiStyle.IconButton(panel, "RandomSeed", 104f, UiIcon.Dice, OnRandomSeed);
            ((RectTransform)dice.transform).TopCenter(new Vector2(224f, -838f), new Vector2(104f, 104f));

            Primary(panel, "Start", "Start", -984f, OnStartPressed);
            _continueButton = Secondary(panel, "Continue", "Weiter", new Vector2(0f, TitleContinueY), new Vector2(ButtonWidth, UiStyle.ButtonHeight), () => session?.ContinueGame());
            _titleExtras = UiStyle.Rect(panel, "Extras").TopCenter(new Vector2(0f, TitleHelpY), new Vector2(ButtonWidth, 120f));
            var halfTitle = new Vector2(328f, 120f);
            UiStyle.LabelOf(Secondary(_titleExtras, "Help", "Anleitung", new Vector2(-176f, 0f), halfTitle, () => OpenHelp())).fontSize = 44;
            UiStyle.LabelOf(Secondary(_titleExtras, "Album", "Fotoalbum", new Vector2(176f, 0f), halfTitle, () => Watch?.OpenAlbum())).fontSize = 44;
            Line(panel, "Steuere deine Insel und ramme andere, um zu wachsen.", UiStyle.Caption, UiStyle.Muted, -1456f, 40f);
            return screen.gameObject;
        }

        GameObject BuildPause(RectTransform root)
        {
            bool quit = !Application.isMobilePlatform;
            var screen = Screen(root, "PauseScreen", UiStyle.Dim, PausePanel, out var panel);
            Line(panel, "Pause", UiStyle.Title, UiStyle.Sand, -36f, 130f, true);
            _pauseSeedText = Line(panel, "", UiStyle.Caption, UiStyle.Muted, -172f, 40f);
            Primary(panel, "Resume", "Fortsetzen", -244f, () => session?.Resume());
            var wide = new Vector2(ButtonWidth, UiStyle.ButtonHeight);
            Secondary(panel, "Restart", "Neu beginnen", new Vector2(0f, -408f), wide, () => session?.StartNewGame());
            UiStyle.ButtonIcon(Secondary(panel, "Title", "Zum Titel", new Vector2(0f, -572f), wide, () => session?.ReturnToTitle()), UiIcon.Home);
            var half = new Vector2(328f, UiStyle.ButtonHeight);
            UiStyle.LabelOf(Secondary(panel, "Journal", "Tagebuch", new Vector2(-176f, -736f), half, () => Watch?.OpenJournal())).fontSize = 46;
            UiStyle.LabelOf(Secondary(panel, "Photo", "Fotomodus", new Vector2(176f, -736f), half, () => Watch?.EnterPhotoMode())).fontSize = 46;
            UiStyle.LabelOf(Secondary(panel, "Album", "Fotoalbum", new Vector2(-176f, -900f), half, () => Watch?.OpenAlbum())).fontSize = 46;
            UiStyle.LabelOf(Secondary(panel, "Help", "Anleitung", new Vector2(176f, -900f), half, () => OpenHelp())).fontSize = 46;
            // Mobile apps are left through the home button; Application.Quit is against Apple's guidelines.
            if (quit) Secondary(panel, "Quit", "Beenden", new Vector2(0f, -1064f), wide, () => session?.QuitGame(), true);
            _pauseVoice = UiStyle.Toggle(panel, "VoiceToggle", "Tildas Stimme", new Vector2(560f, 92f), TildaVoice.Enabled, OnToggleVoice);
            ((RectTransform)_pauseVoice.transform).TopCenter(new Vector2(0f, quit ? -1232f : -1068f), new Vector2(560f, 92f));
            return screen.gameObject;
        }

        GameObject BuildGameOver(RectTransform root)
        {
            var screen = Screen(root, "GameOverScreen", UiStyle.Dim, OverPanel, out var panel);
            Line(panel, "Versunken", UiStyle.Title, UiStyle.Coral, -36f, 130f, true);
            _overPreview = UiStyle.Picture(panel, "Preview", new Vector2(400f, 400f), out var frame);
            frame.TopCenter(new Vector2(0f, -190f), new Vector2(400f, 400f));
            var card = UiStyle.Card(panel, "Stats", new Vector2(760f, 270f)).TopCenter(new Vector2(0f, -620f), new Vector2(760f, 270f));
            _statsText = UiStyle.Label(card, "", 40, UiStyle.Cream, TextAnchor.MiddleCenter);
            _statsText.rectTransform.Stretch(UiStyle.Gap, UiStyle.GapSmall, UiStyle.Gap, UiStyle.GapSmall);
            _statsText.lineSpacing = UiStyle.Lines(1.3f);
            _overSeedText = Line(panel, "", UiStyle.Caption, UiStyle.Muted, -914f, 40f);
            _countdownText = Line(panel, "", UiStyle.Body, UiStyle.CreamSoft, -966f, 56f);
            Primary(panel, "NewIsland", "Neue Insel", -1080f, () => session?.RestartFromGameOver());
            return screen.gameObject;
        }

        GameObject BuildPauseButton(RectTransform root)
        {
            var b = UiStyle.IconButton(root, "PauseButton", 120f, UiIcon.Pause, () => session?.Pause());
            ((RectTransform)b.transform).Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-UiStyle.Margin, -UiStyle.Margin), new Vector2(120f, 120f));
            return b.gameObject;
        }

        static InputField MakeSeedField(RectTransform parent, string name, Vector2 pos, Vector2 size)
        {
            // A warm cream well with ink digits: the one place where the player reads and types.
            var body = UiStyle.Pill(parent, name, size, UiStyle.Cream, true);
            var rt = body.rectTransform.TopCenter(pos, size);
            UiStyle.Shape(rt, "Shade", UiSprites.PillHighlightOf(size.y), UiStyle.WithAlpha(UiStyle.CreamDeep, 0.7f)).rectTransform.Stretch();

            Text MakeText(string n, Color color, bool italic)
            {
                var t = UiStyle.Label(rt, "", UiStyle.ButtonText, color, TextAnchor.MiddleCenter, !italic);
                t.name = n;
                if (italic) t.fontStyle = UiStyle.FontIsLight ? FontStyle.BoldAndItalic : FontStyle.Italic;
                t.rectTransform.Stretch(32f, 4f, 32f, 8f);
                t.supportRichText = false;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                return t;
            }

            var text = MakeText("Text", UiStyle.Ink, false);
            var placeholder = MakeText("Placeholder", UiStyle.WithAlpha(UiStyle.Ink, 0.4f), true);
            placeholder.text = "Standard";

            var field = body.gameObject.AddComponent<InputField>();
            field.targetGraphic = body;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.contentType = InputField.ContentType.IntegerNumber;
            field.lineType = InputField.LineType.SingleLine;
            field.characterLimit = WorldSeeds.MaxDigits;
            field.customCaretColor = true;
            field.caretColor = UiStyle.Ink;
            field.caretWidth = 3;
            field.selectionColor = UiStyle.WithAlpha(UiStyle.MintDeep, 0.5f);
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = field.colors;
            colors.normalColor = new Color(0.96f, 0.96f, 0.96f, 1f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            field.colors = colors;
            return field;
        }
    }
}
