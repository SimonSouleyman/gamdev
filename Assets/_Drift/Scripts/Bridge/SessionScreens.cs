using System;
using Drift.Core;
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
        public enum EditorPreview { None, Title, Pause, GameOver, Help, AdventureGameOver }

        const string CanvasName = "SessionScreensCanvas";
        const int EditorSampleSeed = 482913;

        public GameSession session;
        public Island player;
        public IslandChaseCamera chaseCamera;
        public TouchControls touch;
        // Optional sibling component: tilt steering (IMU). Present = the pause menu offers "Steuerung".
        public TiltSteering tilt;
        [Tooltip("Voreinstellung der Steuerung: an = Daumenstick, WASD und Kippen geben die Richtung, die Insel treibt dorthin ohne sich zu drehen. Im Pausenmenü unter „Steuerung“ umschaltbar; die dortige Wahl gilt.")]
        public bool directSteering = true;
        // Optional sibling component (Phase 7): journal, photo mode and photo album from the menus.
        public WatchTools watch;
        // Optional sibling component: Tilda's tutorial, restarted from the Anleitung screen.
        public TutorialGuide tutorial;
        public EditorPreview editorPreview = EditorPreview.None;
        [Range(0, HelpScreen.PageCount - 1)] public int editorHelpPage;
        public bool editorHelpTouchFirst;
        public int sortingOrder = 10;
        public string tagline = ModeTexts.CozyTagline;
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
        GameObject _overNewIsland, _overAdventureRow;
        Button _continueButton;
        Text _titleBestText, _recordText, _pauseRestartLabel;
        BestTimes.Result _lastResult;
        GameMode _overMode = (GameMode)(-1);
        RectTransform _titleExtras;
        readonly HelpScreen _help = new HelpScreen();
        readonly TiltSettingsScreen _tiltScreen = new TiltSettingsScreen();
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
        Func<Vector2> _provider, _directionProvider;
        int _steerFrame = -10;
        Vector2 _flyLook;
        float _flyZoom = 1f;

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

        public bool TiltSettingsOpen => _tiltScreen.IsOpen;

        public void OpenTiltSettings() => _tiltScreen.Open();

        public void CloseTiltSettings() => _tiltScreen.Hide();

        void OnReplayTutorial()
        {
            if (Drift.Core.GameModes.IsAdventure) { AdventureTutorialGuide.RequestReplay(); return; }
            var t = Tutorial;
            if (t != null) t.RequestReplay();
        }

        void OnEnable()
        {
            if (touch == null) touch = GetComponent<TouchControls>();
            if (tilt == null) tilt = GetComponent<TiltSteering>();
            if (watch == null) watch = GetComponent<WatchTools>();
            if (tutorial == null) tutorial = GetComponent<TutorialGuide>();
            Build();
            _shown = (GameSession.State)(-1);
            _lookupTimer = 0f;
            if (!Application.isPlaying) return;
            EnsureEventSystem();
            SteerSettings.SetFallback(directSteering);
            InstallInput();
            Resolve();
            Attach(session);
        }

        void InstallInput()
        {
            _provider = ProvideMove;
            _directionProvider = ProvideDirection;
            Island.InputProvider = _provider;
            Island.DirectionProvider = _directionProvider;
            Island.DirectionSteering = SteerSettings.Direct;
        }

        void OnDisable()
        {
            Detach(session);
            if (_provider != null && Island.InputProvider == _provider) Island.InputProvider = null;
            if (_directionProvider != null && Island.DirectionProvider == _directionProvider)
            {
                Island.DirectionProvider = null;
                // Leaving Play Mode never reloads the domain: a static left true would make the next edit-mode
                // island tick in direct mode with nobody to feed it a direction.
                Island.DirectionSteering = false;
            }
            _directionProvider = null;
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
            if (tilt == null) tilt = GetComponent<TiltSteering>();
            if (watch == null) watch = GetComponent<WatchTools>();
            if (session != null && chaseCamera != null) return;
            _lookupTimer -= Time.unscaledDeltaTime;
            if (_lookupTimer > 0f) return;
            _lookupTimer = 1f;
            if (session == null) session = FindAnyObjectByType<GameSession>();
            if (chaseCamera == null) chaseCamera = FindAnyObjectByType<IslandChaseCamera>();
        }

        // (turn, throttle) for the old steering-wheel scheme. Under the direction scheme Island ignores this
        // and reads DirectionProvider instead; the tilt keeps its stop-gap here for players who switch back.
        Vector2 ProvideMove()
        {
            if (session == null || session.Current != GameSession.State.Playing) return Vector2.zero;
            if (MoveOverride != null) return MoveOverride();
            if (tilt != null && tilt.Active)
            {
                Vector2 world = ProvideDirection();
                if (world.sqrMagnitude < 1e-6f || player == null) return Vector2.zero;
                return TiltMath.AsTurnThrottle(world, new Vector2(player.Forward.x, player.Forward.z));
            }
            return touch != null ? touch.Move : Vector2.zero;
        }

        // The steering direction as a world XZ vector, length 0..1. Tilt, thumbstick and W/A/S/D all name a
        // direction on the SCREEN, and the camera's yaw turns it into a world direction: "up the screen" is
        // always away from the camera, whatever the island's body is doing. The island then drifts straight
        // that way - there is no front to turn into first.
        public Vector2 ProvideDirection()
        {
            // Locked = photo mode, a followed herd or the fly-over after the Pangäa: the island is not steered
            // at all there, whatever the thumbstick happens to report.
            if (session == null || session.Current != GameSession.State.Playing || Island.InputLocked) return Vector2.zero;
            Vector2 screen = ReadSteerScreen();
            if (screen.sqrMagnitude < 1e-6f) return Vector2.zero;
            _steerFrame = Time.frameCount;
            return TiltMath.ToWorld(TiltMath.ClampStick(screen), SteerYaw);
        }

        // Playtest bots steer through these instead of the stick: a screen direction (mapped through SteerYaw like
        // stick and tilt) and, for the old wheel scheme, (turn, throttle).
        public static System.Func<Vector2> ScreenOverride;
        public static System.Func<Vector2> MoveOverride;

        // The raw screen direction of stick, keys or tilt (0..1), without any of the locks: the fly-over uses
        // the very same input to move the camera once the island has stopped.
        public Vector2 ReadSteerScreen()
        {
            Vector2 screen = Vector2.zero;
            if (ScreenOverride != null) screen = ScreenOverride();
            else if (tilt != null && tilt.Active) screen = tilt.Direction;
            else if (SteerSettings.Direct)
            {
                if (touch != null && touch.inputEnabled && touch.stickEnabled) screen = touch.Move;
                if (screen.sqrMagnitude < 1e-6f) screen = ReadKeys();
            }
            return TiltMath.ClampStick(screen);
        }

        // The yaw the screen directions are mapped through: the camera's steering frame, which stands still
        // while a direction is held even though the view swings onto the course, 0 while there is no camera yet.
        public float SteerYaw => chaseCamera != null ? chaseCamera.SteerYawDeg : 0f;
        // Where the camera actually looks.
        public float ViewYaw => chaseCamera != null ? chaseCamera.ViewYawDeg : 0f;

        // W/A/S/D as a screen direction, not as a turn: W is up the screen, D is to the right.
        static Vector2 ReadKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return Vector2.zero;
            return TiltMath.KeysToScreen(kb.aKey.isPressed, kb.dKey.isPressed, kb.wKey.isPressed, kb.sKey.isPressed);
        }

        void OnStateChanged(GameSession.State s)
        {
            if (s == GameSession.State.GameOver && session != null && session.Mode == GameMode.Adventure)
                _lastResult = BestTimes.Submit(GameMode.Adventure, session.Stats.timeSurvived);
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
                // Leaving Play Mode here reloads neither the domain nor the scene, so OnDisable may never run:
                // a static left on would make the edit-mode islands tick in direct mode with no provider.
                if (Island.DirectionSteering || Island.DirectionProvider != null)
                {
                    Island.DirectionSteering = false;
                    Island.DirectionProvider = null;
                }
                bool t = editorPreview == EditorPreview.Title, p = editorPreview == EditorPreview.Pause;
                bool adventureOver = editorPreview == EditorPreview.AdventureGameOver;
                bool o = editorPreview == EditorPreview.GameOver || adventureOver;
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
                if (o)
                {
                    var sample = new SessionStats { timeSurvived = adventureOver ? 262f : 83f, islandsAbsorbed = adventureOver ? 11 : 4, volcanoesAbsorbed = 1, peakLandMass = 212f };
                    var result = new BestTimes.Result { seconds = 262f, previous = 222f, best = 262f, isRecord = true };
                    FillGameOver(sample, 3.2f, adventureOver ? GameMode.Adventure : GameMode.Cozy, result);
                }
                if (t) RefreshTitleBest();
                if (p) SetPauseMode(GameModes.IsAdventure);
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
                InstallInput();
            }
            // The setting can change from the pause menu at any time.
            Island.DirectionSteering = SteerSettings.Direct;

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
                // A run starts from however the player happens to be holding the phone; a resume keeps the
                // middle it had, so a pause in mid-turn does not silently re-zero the steering.
                if (s == GameSession.State.Playing && _shown != GameSession.State.Paused && tilt != null) tilt.Recalibrate();
                _shown = s;
                ShowPanels(s == GameSession.State.Title, s == GameSession.State.Paused, s == GameSession.State.GameOver, s == GameSession.State.Playing);
                if (s != GameSession.State.Title && s != GameSession.State.Paused) _help.Hide();
                if (s != GameSession.State.Paused) _tiltScreen.Hide();
                if (s == GameSession.State.Title)
                {
                    // The title works with the cozy game: "Weiter" and the seed field belong to its save.
                    session.SelectMode(GameMode.Cozy);
                    _hasSave = session.HasSave;
                    _saveCheckTimer = 0.5f;
                    RefreshTitleBest();
                    RefreshSeedField(true);
                }
                if (s == GameSession.State.Paused) SetPauseMode(session.Mode == GameMode.Adventure);
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
                FillGameOver(session.Stats, session.RestartCountdown, session.Mode, _lastResult);
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
            // The run journal handles back itself while open and in the frame it closed.
            if (kb != null && kb.escapeKey.wasPressedThisFrame && !RunJournalPanel.OwnsBack && !(_seedField != null && _seedField.isFocused))
            {
                if (_tiltScreen.IsOpen) _tiltScreen.Hide();
                else if (_help.IsOpen) _help.Hide();
                else if (album) watch.AlbumBack();
                else if (photo) watch.ExitPhotoMode();
                else if (journal) watch.CloseJournal();
                else if (watch != null && watch.Following) watch.ReturnToIsland();
                else session.TogglePause();
            }

            bool steering = s == GameSession.State.Playing && !photo && !journal && !album && !(watch != null && watch.Following);
            if (tilt != null)
            {
                // Paused, in the album, in photo mode or while a herd is followed the phone may be moved freely.
                tilt.inputEnabled = steering;
                if (_tiltScreen.IsOpen) _tiltScreen.Tick();
                else if (tilt.previewing) tilt.previewing = false;
            }

            // The fly-over over the finished Pangäa: the island is locked, so the same stick, keys and tilt fly
            // the camera instead, a drag looks around and the pinch changes the height.
            bool flyOver = session.PangaeaFreeLook && !photo && !journal && !album && !(watch != null && watch.Following);
            if (chaseCamera != null) chaseCamera.SteerHeld = !flyOver && Time.frameCount - _steerFrame <= 1;

            if (touch != null)
            {
                bool following = watch != null && watch.Following;
                touch.inputEnabled = steering;
                // While the tilt drives, the thumbstick is gone (every tap is a tap again) but pinch zoom stays.
                touch.stickEnabled = tilt == null || !tilt.Active;
                touch.lookEnabled = flyOver;
                float pinch = touch.ConsumePinchFactor();
                if (flyOver) _flyZoom *= pinch;
                else if (chaseCamera != null && !photo && !following && Mathf.Abs(pinch - 1f) > 1e-4f) chaseCamera.ZoomBy(pinch);
                if (flyOver) _flyLook += touch.ConsumeLookDelta();
                else touch.ConsumeLookDelta();
            }
            if (flyOver) StepFlyInput(Time.unscaledDeltaTime);
        }

        // Look and zoom arrive between the frames the fly-over asks for them, so they are collected here and
        // drained by ReadFlyInput. Mouse and Q/E keep the Editor able to fly without a touchscreen.
        void StepFlyInput(float dt)
        {
            _flyLook += MouseLookDelta();
            var mouse = Mouse.current;
            if (mouse != null)
            {
                float sc = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(sc) > 0.01f) _flyZoom *= Mathf.Exp(-sc * 0.0012f);
            }
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.qKey.isPressed || kb.numpadPlusKey.isPressed) _flyZoom *= Mathf.Exp(-1.2f * dt);
                if (kb.eKey.isPressed || kb.numpadMinusKey.isPressed) _flyZoom *= Mathf.Exp(1.2f * dt);
            }
        }

        static Vector2 MouseLookDelta()
        {
            var m = Mouse.current;
            if (m == null || !m.leftButton.isPressed) return Vector2.zero;
            var es = EventSystem.current;
            if (es != null && es.IsPointerOverGameObject()) return Vector2.zero;
            return m.delta.ReadValue();
        }

        // What PangaeaFinale flies the camera with: the steering input as a move, the collected drag as a look
        // and the collected pinch / scroll / Q-E as a height factor.
        public FlyOverCamera.Input ReadFlyInput()
        {
            var input = new FlyOverCamera.Input
            {
                move = session != null && session.PangaeaFreeLook ? ReadSteerScreen() : Vector2.zero,
                look = _flyLook,
                zoomFactor = _flyZoom,
            };
            _flyLook = Vector2.zero;
            _flyZoom = 1f;
            return input;
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

        // Abenteuer scores the time survived and waits for "Nochmal" / "Zum Titel"; the cozy path (no longer
        // reachable in normal play) keeps its stats and the automatic new island.
        void FillGameOver(SessionStats st, float countdown, GameMode mode, BestTimes.Result result)
        {
            bool adventure = mode == GameMode.Adventure;
            if (mode != _overMode)
            {
                _overMode = mode;
                if (_overNewIsland != null) _overNewIsland.SetActive(!adventure);
                if (_overAdventureRow != null) _overAdventureRow.SetActive(adventure);
                if (_overSeedText != null) _overSeedText.gameObject.SetActive(!adventure);
                if (_recordText != null) _recordText.gameObject.SetActive(adventure);
            }
            if (_overTitle != null)
            {
                var pl = session != null ? session.player : null;
                _overTitle.text = ModeTexts.LostTitle(mode, pl != null && pl.LostOverEdge);
            }
            if (_statsText != null)
                _statsText.text = adventure ? ModeTexts.AdventureStats(st, result.best) : ModeTexts.CozyStats(st);
            if (_recordText != null && adventure) _recordText.text = ModeTexts.RecordLine(result);
            if (_countdownText != null)
                _countdownText.text = !adventure && countdown > 0f ? $"Neue Insel in {Mathf.CeilToInt(countdown)} …" : "";
        }

        void RefreshTitleBest()
        {
            if (_titleBestText != null) _titleBestText.text = ModeTexts.BestTimeCaption(BestTimes.Get(GameMode.Adventure));
        }

        void SetPauseMode(bool adventure)
        {
            if (_pauseRestartLabel != null) _pauseRestartLabel.text = ModeTexts.RestartLabel(adventure ? GameMode.Adventure : GameMode.Cozy);
            LayoutPauseExtras(adventure);
        }

        static readonly Vector2 ExtraSize = new Vector2(328f, 116f);
        readonly System.Collections.Generic.List<RectTransform> _pauseExtras = new();
        readonly System.Collections.Generic.List<bool> _pauseExtraCozy = new();

        void Extra(RectTransform panel, string name, string label, bool cozyOnly, Action onClick)
        {
            var b = UiStyle.SecondaryButton(panel, name, label, ExtraSize, onClick);
            UiStyle.LabelOf(b).fontSize = 44;
            _pauseExtras.Add((RectTransform)b.transform);
            _pauseExtraCozy.Add(cozyOnly);
        }

        // Two per row, in order, skipping whatever the mode does not offer.
        void LayoutPauseExtras(bool adventure)
        {
            var mode = adventure ? GameMode.Adventure : GameMode.Cozy;
            int slot = 0;
            for (int i = 0; i < _pauseExtras.Count; i++)
            {
                var rt = _pauseExtras[i];
                if (rt == null) continue;
                // WatchRules allows the album in both modes, but inside a run it belongs to the cozy game, so the
                // adventure pause menu drops it too - the title screen keeps it reachable for both.
                bool on = !_pauseExtraCozy[i] || WatchRules.Allowed(WatchFeature.Journal, mode);
                if (rt.gameObject.activeSelf != on) rt.gameObject.SetActive(on);
                if (!on) continue;
                rt.TopCenter(new Vector2(slot % 2 == 0 ? -176f : 176f, -736f - 132f * (slot / 2)), ExtraSize);
                slot++;
            }
        }

        void OnRestartPressed()
        {
            if (session == null) return;
            if (session.Mode == GameMode.Adventure) session.StartNewGame(GameMode.Adventure);
            else session.StartNewGame();
        }

        void OnCozyPressed()
        {
            if (session == null) return;
            session.SelectMode(GameMode.Cozy);
            OnStartPressed();
        }

        void OnContinuePressed()
        {
            if (session == null) return;
            session.SelectMode(GameMode.Cozy);
            session.ContinueGame();
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
        static Vector2 PausePanel => new Vector2(880f, Application.isMobilePlatform ? 1300f : 1464f);
        const float TitleContinueY = -1132f, TitleHelpY = -1292f;
        const float ModeButtonWidth = 404f, ModeButtonX = 214f;
        // The legacy scene value promised sinking; the cozy game no longer ends that way.
        const string LegacyTagline = "Sammle Inseln, bevor deine versinkt.";
        string Tagline => string.IsNullOrEmpty(tagline) || tagline == LegacyTagline ? ModeTexts.CozyTagline : tagline;

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
            _tiltScreen.Build(root, tilt, null);
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
            Line(panel, Tagline, UiStyle.Body, UiStyle.CreamSoft, -204f, 50f);

            _titlePreview = UiStyle.Picture(panel, "Preview", new Vector2(440f, 440f), out var frame);
            frame.TopCenter(new Vector2(0f, -268f), new Vector2(440f, 440f));

            Line(panel, "Welt-Nummer", UiStyle.Caption, UiStyle.Muted, -722f, 36f);
            _seedField = MakeSeedField(panel, "SeedField", new Vector2(-64f, -762f), new Vector2(440f, 104f));
            _seedField.onEndEdit.AddListener(OnSeedEdited);
            var dice = UiStyle.IconButton(panel, "RandomSeed", 104f, UiIcon.Dice, OnRandomSeed);
            ((RectTransform)dice.transform).TopCenter(new Vector2(224f, -762f), new Vector2(104f, 104f));

            // Two modes side by side: the cozy world (seed above) and the adventure run with its best time below.
            var modeSize = new Vector2(ModeButtonWidth, UiStyle.ButtonHeight);
            var cozy = UiStyle.PrimaryButton(panel, "Start", GameModes.Label(GameMode.Cozy), modeSize, OnCozyPressed);
            ((RectTransform)cozy.transform).TopCenter(new Vector2(-ModeButtonX, -892f), modeSize);
            var adventure = UiStyle.PrimaryButton(panel, "Adventure", GameModes.Label(GameMode.Adventure), modeSize, () => session?.StartNewGame(GameMode.Adventure));
            ((RectTransform)adventure.transform).TopCenter(new Vector2(ModeButtonX, -892f), modeSize);
            var cozyHint = UiStyle.Label(panel, ModeTexts.CozyHint, UiStyle.Caption, UiStyle.Muted, TextAnchor.UpperCenter);
            cozyHint.rectTransform.TopCenter(new Vector2(-ModeButtonX, -1042f), new Vector2(ModeButtonWidth, 40f));
            _titleBestText = UiStyle.Label(panel, ModeTexts.BestTimeCaption(0f), UiStyle.Caption, UiStyle.Sand, TextAnchor.UpperCenter, true);
            _titleBestText.rectTransform.TopCenter(new Vector2(ModeButtonX, -1042f), new Vector2(ModeButtonWidth, 40f));
            UiStyle.FitWidth(_titleBestText);

            const float rowWidth = ModeButtonWidth + 2f * ModeButtonX;
            _continueButton = Secondary(panel, "Continue", "Weiter", new Vector2(0f, TitleContinueY), new Vector2(rowWidth, UiStyle.ButtonHeight), OnContinuePressed);
            _titleExtras = UiStyle.Rect(panel, "Extras").TopCenter(new Vector2(0f, TitleHelpY), new Vector2(rowWidth, 120f));
            var third = new Vector2((rowWidth - 28f) / 3f, 120f);
            float thirdX = third.x + 14f;
            UiStyle.LabelOf(Secondary(_titleExtras, "Help", "Anleitung", new Vector2(-thirdX, 0f), third, () => OpenHelp())).fontSize = 40;
            UiStyle.LabelOf(Secondary(_titleExtras, "Album", "Fotoalbum", new Vector2(0f, 0f), third, () => Watch?.OpenAlbum())).fontSize = 40;
            UiStyle.LabelOf(Secondary(_titleExtras, "Runs", "Durchgänge", new Vector2(thirdX, 0f), third, RunJournal.RequestOpen)).fontSize = 40;
            Line(panel, "Gemütlich wachsen – oder im Abenteuer ausweichen.", UiStyle.Caption, UiStyle.Muted, -1456f, 40f);
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
            _pauseRestartLabel = UiStyle.LabelOf(Secondary(panel, "Restart", ModeTexts.RestartLabel(GameMode.Cozy), new Vector2(0f, -408f), wide, OnRestartPressed));
            UiStyle.ButtonIcon(Secondary(panel, "Title", "Zum Titel", new Vector2(0f, -572f), wide, () => session?.ReturnToTitle()), UiIcon.Home);
            // The smaller extras: three rows of 116 at a 132 step below the three big buttons. Watching,
            // photographing and the species journal are the cozy game, so the adventure menu simply leaves them
            // out (WatchTools refuses them there) and the rest closes the gap.
            _pauseExtras.Clear();
            _pauseExtraCozy.Clear();
            Extra(panel, "Journal", "Tagebuch", true, () => Watch?.OpenJournal());
            Extra(panel, "Photo", "Fotomodus", true, () => Watch?.EnterPhotoMode());
            Extra(panel, "Album", "Fotoalbum", true, () => Watch?.OpenAlbum());
            Extra(panel, "Help", "Anleitung", false, () => OpenHelp());
            Extra(panel, "Runs", "Durchgänge", false, RunJournal.RequestOpen);
            Extra(panel, "Controls", "Steuerung", false, OpenTiltSettings);
            LayoutPauseExtras(GameModes.IsAdventure);
            // Mobile apps are left through the home button; Application.Quit is against Apple's guidelines.
            if (quit) Secondary(panel, "Quit", "Beenden", new Vector2(0f, -1140f), wide, () => session?.QuitGame(), true);
            _pauseVoice = UiStyle.Toggle(panel, "VoiceToggle", "Tildas Stimme", new Vector2(560f, 92f), TildaVoice.Enabled, OnToggleVoice);
            ((RectTransform)_pauseVoice.transform).TopCenter(new Vector2(0f, quit ? -1304f : -1140f), new Vector2(560f, 92f));
            return screen.gameObject;
        }

        Text _overTitle;

        GameObject BuildGameOver(RectTransform root)
        {
            var screen = Screen(root, "GameOverScreen", UiStyle.Dim, OverPanel, out var panel);
            _overTitle = Line(panel, "Versunken", UiStyle.Title, UiStyle.Coral, -36f, 130f, true);
            _overPreview = UiStyle.Picture(panel, "Preview", new Vector2(400f, 400f), out var frame);
            frame.TopCenter(new Vector2(0f, -190f), new Vector2(400f, 400f));
            var card = UiStyle.Card(panel, "Stats", new Vector2(760f, 270f)).TopCenter(new Vector2(0f, -620f), new Vector2(760f, 270f));
            _statsText = UiStyle.Label(card, "", 40, UiStyle.Cream, TextAnchor.MiddleCenter);
            _statsText.rectTransform.Stretch(UiStyle.Gap, UiStyle.GapSmall, UiStyle.Gap, UiStyle.GapSmall);
            _statsText.lineSpacing = UiStyle.Lines(1.3f);
            _overSeedText = Line(panel, "", UiStyle.Caption, UiStyle.Muted, -914f, 40f);
            _recordText = Line(panel, "", UiStyle.Heading, UiStyle.Mint, -910f, 72f, true);
            _countdownText = Line(panel, "", UiStyle.Body, UiStyle.CreamSoft, -966f, 56f);
            _overNewIsland = Primary(panel, "NewIsland", "Neue Insel", -1080f, () => session?.RestartFromGameOver()).gameObject;

            var row = UiStyle.Rect(panel, "AdventureButtons").TopCenter(new Vector2(0f, -1080f), new Vector2(ButtonWidth, UiStyle.ButtonHeight));
            _overAdventureRow = row.gameObject;
            var half = new Vector2(328f, UiStyle.ButtonHeight);
            var again = UiStyle.PrimaryButton(row, "Again", "Nochmal", half, () => session?.StartNewGame(GameMode.Adventure));
            ((RectTransform)again.transform).TopCenter(new Vector2(-176f, 0f), half);
            UiStyle.ButtonIcon(Secondary(row, "Title", "Zum Titel", new Vector2(176f, 0f), half, () => session?.ReturnToTitle()), UiIcon.Home);
            _recordText.gameObject.SetActive(false);
            _overAdventureRow.SetActive(false);
            _overMode = GameMode.Cozy;
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
