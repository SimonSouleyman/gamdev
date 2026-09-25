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
        public enum EditorPreview { None, Title, Pause, GameOver, Help, AdventureGameOver, ConfirmNewWorld, CozyChoice }

        const string CanvasName = "SessionScreensCanvas";
        // The way back to the title (pause menu, "Versunken"), with the house icon (owner 2026-09-25; was "Home").
        public const string MainMenuLabel = "Hauptmenü";
        // Just sunk: Tilda is sorry for the player in both modes, never cheering (first-time reviewer 2026-09-25).
        public const TildaPose GameOverPose = TildaPose.Comfort;
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
        GameObject _title, _pause, _over, _pauseButton, _confirm, _choice;
        Action _confirmAction;
        GameObject _overNewIsland, _overAdventureRow;
        Text _choiceSavedText, _choiceNewText;
        bool _adventureReplayAsked;
        Text _titleBestText, _recordText, _pauseRestartLabel;
        BestDistances.Result _lastResult;
        int _overStamp, _titleBestShown = -1;
        GameMode _overMode = (GameMode)(-1);
        readonly HelpScreen _help = new HelpScreen();
        readonly TiltSettingsScreen _tiltScreen = new TiltSettingsScreen();
        int _shownHelpPage = -1;
        bool _shownHelpTouch;
        Text _statsText, _countdownText, _overSeedText, _pauseSeedText, _pauseNoteText;
        InputField _seedField;
        RawImage _titlePreview, _overPreview;
        IslandPreview _preview;
        float _lookupTimer;
        // "Jede Runde neue Welt": the number the title's seed field offers while a saved run sits behind the title.
        int _proposedSeed;
        bool _fieldSaved;
        int _fieldSeed = -1;
        GameSession.State _shown = (GameSession.State)(-1);
        int _shownRun = -1;
        Func<Vector2> _provider, _directionProvider;
        int _steerFrame = -10;
        bool _wasFlyOver;
        FlyOverCamera.Input _flyIn;
        readonly TouchGestures _mouseGestures = new TouchGestures();
        bool _mouseOnUi;

        public IslandPreview Preview => _preview;
        public bool ConfirmOpen => _confirm != null && _confirm.activeSelf;
        // "Gemütlich" with a saved world: the small Weiter / Neu beginnen window over the title.
        public bool CozyChoiceOpen => _choice != null && _choice.activeSelf;
        public GameObject CozyChoice => _choice;
        // The title's cozy flow asks these instead of the session when set (tests): whether a saved world exists,
        // "Neu beginnen" with the seed field's number (null = the world behind the title) and "Weiter".
        public Func<bool> SaveProbe { get; set; }
        public Action<int?> NewCozyWorldOverride { get; set; }
        public Action ContinueCozyOverride { get; set; }
        public InputField SeedField => _seedField;
        public bool HelpOpen => _help.IsOpen;
        public HelpScreen Help => _help;
        public TildaPresenter Presenter => _presenter;
        WatchTools Watch => watch != null ? watch : (watch = GetComponent<WatchTools>());
        TutorialGuide Tutorial => tutorial != null ? tutorial : (tutorial = GetComponent<TutorialGuide>());

        // The title's Anleitung explains both games; opened from a paused run it shows only that run's mode.
        public void OpenHelp(int page = 0)
        {
            bool onTitle = session == null || session.Current == GameSession.State.Title;
            var mode = session != null ? session.Mode : GameModes.Current;
            _help.Open(HelpScreen.ScopeFor(onTitle, mode), page, InputMode.TouchPreferred || (touch != null && touch.forceShowTouch));
        }

        public void CloseHelp() => _help.Hide();

        public bool TiltSettingsOpen => _tiltScreen.IsOpen;

        public void OpenTiltSettings() => _tiltScreen.Open();

        public void CloseTiltSettings() => _tiltScreen.Hide();

        void OnReplayTutorial(GameMode mode)
        {
            if (mode == GameMode.Adventure)
            {
                _adventureReplayAsked = true;
                AdventureTutorialGuide.RequestReplay();
                return;
            }
            var t = Tutorial;
            if (t != null) t.RequestReplay();
        }

        bool ReplayPendingFor(GameMode mode)
        {
            if (mode == GameMode.Adventure) return _adventureReplayAsked || AdventureTutorialGuide.Running;
            var t = Tutorial;
            return t != null && t.ReplayPending;
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

        // Whether the tilt steers: only while the island itself is steered, never in the Pangäa fly-over.
        public static bool TiltSteers(bool steering, bool flyOver) => steering && !flyOver;
        // Whether the thumbstick is there: only while the island is steered by it - never while the tilt steers and
        // never in the fly-over, which is driven like a map (drag, pinch, twist, tap).
        public static bool StickShown(bool tiltActive, bool flyOver) => !flyOver && !tiltActive;

        // The fly-over's move: stick or keys, whatever steering scheme and tilt setting the run uses.
        public Vector2 ReadFlyScreen()
        {
            Vector2 screen = Vector2.zero;
            if (ScreenOverride != null) screen = ScreenOverride();
            else
            {
                if (touch != null && touch.inputEnabled && touch.stickEnabled) screen = touch.Move;
                if (screen.sqrMagnitude < 1e-6f) screen = ReadKeys();
            }
            return TiltMath.ClampStick(screen);
        }

        // The raw screen direction of stick, keys or tilt (0..1), without any of the locks.
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
                _lastResult = BestDistances.Submit(GameMode.Adventure, session.Stats.distance);
            // The asked-for adventure tutorial has started (or will with the next race).
            if (s == GameSession.State.Playing && session != null && session.Mode == GameMode.Adventure) _adventureReplayAsked = false;
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
                bool confirmPreview = editorPreview == EditorPreview.ConfirmNewWorld;
                bool choicePreview = editorPreview == EditorPreview.CozyChoice;
                bool t = editorPreview == EditorPreview.Title || confirmPreview || choicePreview, p = editorPreview == EditorPreview.Pause;
                bool adventureOver = editorPreview == EditorPreview.AdventureGameOver;
                bool o = editorPreview == EditorPreview.GameOver || adventureOver;
                ShowPanels(t, p, o, false);
                if (confirmPreview != ConfirmOpen) { if (confirmPreview) OpenConfirm(null); else CloseConfirm(); }
                if (choicePreview != CozyChoiceOpen) { if (choicePreview) OpenCozyChoice(); else CloseCozyChoice(); }
                if (editorPreview != EditorPreview.Help)
                {
                    _help.Hide();
                    _shownHelpPage = -1;
                }
                else if (!_help.IsOpen || _shownHelpPage != editorHelpPage || _shownHelpTouch != editorHelpTouchFirst)
                {
                    _shownHelpPage = editorHelpPage;
                    _shownHelpTouch = editorHelpTouchFirst;
                    _help.Open(HelpScope.Both, editorHelpPage, editorHelpTouchFirst);
                }
                if (o)
                {
                    var sample = new SessionStats { timeSurvived = adventureOver ? 262f : 83f, distance = adventureOver ? 3456f : 0f, islandsAbsorbed = adventureOver ? 11 : 4, volcanoesAbsorbed = 1, peakLandMass = 212f };
                    var result = new BestDistances.Result { metres = 3456f, previous = 2980f, best = 3456f, isRecord = true };
                    FillGameOver(sample, 3.2f, adventureOver ? GameMode.Adventure : GameMode.Cozy, result);
                }
                if (t) RefreshTitleBest();
                if (p) SetPauseMode(GameModes.IsAdventure);
                UpdatePresenter(editorPreview == EditorPreview.Help ? Stage.Help : t ? Stage.Title : p ? Stage.Pause : o ? Stage.GameOver : Stage.None);
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
                if (_shown == GameSession.State.GameOver && s == GameSession.State.Playing)
                    TildaVoice.Say(TildaVoiceLines.ForMode(TildaVoiceLines.NewIsland, session.Mode == GameMode.Adventure), VoicePriority.Queue);
                // A run starts from however the player happens to be holding the phone; a resume keeps the
                // middle it had, so a pause in mid-turn does not silently re-zero the steering.
                if (s == GameSession.State.Playing && (_shown != GameSession.State.Paused || session.RunNumber != _shownRun) && tilt != null) tilt.Recalibrate();
                _shownRun = session.RunNumber;
                var from = _shown;
                _shown = s;
                ShowPanels(s == GameSession.State.Title, s == GameSession.State.Paused, s == GameSession.State.GameOver, s == GameSession.State.Playing);
                if (s != GameSession.State.Title && s != GameSession.State.Paused) _help.Hide();
                CloseConfirm();
                CloseCozyChoice();
                if (s != GameSession.State.Paused && s != GameSession.State.Title) _tiltScreen.Hide();
                if (s == GameSession.State.Title)
                {
                    // The title works with the cozy game: "Weiter" and the seed field belong to its save.
                    session.SelectMode(GameMode.Cozy);
                    RefreshTitleBest();
                    // Back from a run: the next start never repeats its world. A saved run stays behind the title for
                    // "Weiter" and the field offers a new number; otherwise the new world is built behind the title.
                    if (ReturnedFromRun(from)) _proposedSeed = 0;
                    if (Application.isPlaying && ReturnedFromRun(from) && !session.WorldIsSavedState)
                        session.PreviewSeed(WorldSeeds.RandomOther(session.WorldSeed));
                    RefreshSeedField(true);
                }
                if (s == GameSession.State.Paused) SetPauseMode(session.Mode == GameMode.Adventure);
            }

            if (s == GameSession.State.Title)
            {
                // The save is loaded behind the title a frame after the title appears; the field follows.
                if (_seedField != null && !_seedField.isFocused && (session.WorldIsSavedState != _fieldSaved || session.WorldSeed != _fieldSeed))
                    RefreshSeedField(true);
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
            if (covered)
            {
                CloseConfirm();
                CloseCozyChoice();
            }
            SetPanelActive(_title, !covered && s == GameSession.State.Title);
            SetPanelActive(_pause, !covered && s == GameSession.State.Paused);
            SetPanelActive(_over, !covered && s == GameSession.State.GameOver);
            UpdatePresenter(covered ? Stage.None : _help.IsOpen ? Stage.Help
                : s == GameSession.State.Title ? Stage.Title : s == GameSession.State.Paused ? Stage.Pause
                : s == GameSession.State.GameOver ? Stage.GameOver : Stage.None);

            var kb = Keyboard.current;
            // The run journal handles back itself while open and in the frame it closed.
            if (kb != null && kb.escapeKey.wasPressedThisFrame && !RunJournalPanel.OwnsBack && !(_seedField != null && _seedField.isFocused))
            {
                if (CozyChoiceOpen) CloseCozyChoice();
                else if (ConfirmOpen) CloseConfirm();
                else if (_tiltScreen.IsOpen) _tiltScreen.Hide();
                else if (_help.IsOpen) _help.Hide();
                else if (album) watch.AlbumBack();
                else if (photo) watch.ExitPhotoMode();
                else if (journal) watch.JournalBack();
                else if (watch != null && watch.Following) watch.ReturnToIsland();
                else session.TogglePause();
            }

            bool steering = s == GameSession.State.Playing && !photo && !journal && !album && !(watch != null && watch.Following);
            // The fly-over over the finished Pangäa: the island is locked and the camera is driven like a map
            // (drag, pinch, twist, two-finger tilt, tap; mouse and keys in the Editor).
            bool flyOver = session.PangaeaFreeLook && !photo && !journal && !album && !(watch != null && watch.Following);
            if (tilt != null)
            {
                // Paused, in the album, in photo mode, while a herd is followed and over the finished Pangäa the phone
                // may be moved freely: the owner holds it differently there, so the tilt never flies the camera.
                bool tiltWas = tilt.inputEnabled;
                tilt.inputEnabled = TiltSteers(steering, flyOver);
                // Back from the fly-over the tilt starts from however the phone is held now, not from the pose of
                // the run before it.
                if (_wasFlyOver && !flyOver && tilt.inputEnabled && !tiltWas) tilt.Recalibrate();
                if (_tiltScreen.IsOpen) _tiltScreen.Tick();
                else if (tilt.previewing) tilt.previewing = false;
            }
            _wasFlyOver = flyOver;
            if (chaseCamera != null)
            {
                chaseCamera.SteerHeld = !flyOver && Time.frameCount - _steerFrame <= 1;
                // The tilt keeps the view still: with a phone that is never quite let go, a view that swings onto the
                // course moves the screen directions away under the hand that is holding one.
                chaseCamera.HoldCourse = tilt != null && tilt.Active;
            }

            if (touch != null)
            {
                bool following = watch != null && watch.Following;
                touch.inputEnabled = steering;
                // While the tilt drives, the thumbstick is gone (every tap is a tap again) but pinch zoom stays; the
                // fly-over always has its stick.
                touch.stickEnabled = StickShown(tilt != null && tilt.Active, flyOver);
                touch.lookEnabled = false;
                touch.gesturesEnabled = flyOver;
                float pinch = touch.ConsumePinchFactor();
                if (!flyOver && chaseCamera != null && !photo && !following && Mathf.Abs(pinch - 1f) > 1e-4f) chaseCamera.ZoomBy(pinch);
                touch.ConsumeLookDelta();
                if (flyOver) AddFlyGesture(touch.ConsumeGesture());
            }
            if (flyOver) StepFlyInput(Time.unscaledDeltaTime);
            else
            {
                if (_mouseGestures.Active) _mouseGestures.Reset();
                _flyIn = default;
            }
        }

        // Gestures arrive between the frames the fly-over asks for them, so they are collected here and drained by
        // ReadFlyInput. The mouse flies like a finger for the Editor and PC: left drag grabs the ground, a click is a
        // tap, right drag turns and tilts, the wheel zooms about the pointer, Q/E zoom about the middle.
        void AddFlyGesture(in TouchGesture g)
        {
            if (g.pan)
            {
                if (!_flyIn.pan)
                {
                    _flyIn.pan = true;
                    _flyIn.panFrom = g.panFrom;
                }
                _flyIn.panTo = g.panTo;
            }
            if (g.twoFinger)
            {
                if (!_flyIn.pinch)
                {
                    _flyIn.pinch = true;
                    _flyIn.pinchFrom = g.midFrom;
                    _flyIn.pinchScale = 1f;
                }
                _flyIn.pinchTo = g.midTo;
                _flyIn.pinchScale *= g.scale > 0f ? g.scale : 1f;
                _flyIn.twistDeg += g.twistDeg;
            }
            _flyIn.tiltPixels += g.tiltPixels;
            if (g.fingers > 0 || g.touched) _flyIn.touching = true;
            if (g.released) _flyIn.released = true;
            if (g.tap)
            {
                _flyIn.tap = true;
                _flyIn.tapPos = g.tapPos;
            }
        }

        void StepFlyInput(float dt)
        {
            var mouse = Mouse.current;
            if (mouse != null && !(touch != null && touch.Gestures.Active))
            {
                Vector2 p = mouse.position.ReadValue();
                if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
                {
                    var es = EventSystem.current;
                    _mouseOnUi = es != null && es.IsPointerOverGameObject();
                }
                bool left = mouse.leftButton.isPressed && !_mouseOnUi;
                _mouseGestures.Feed(left ? 1 : 0, -1, p, 0, default, Time.unscaledTime, TouchGestures.PixelScale(UnityEngine.Screen.width, UnityEngine.Screen.height));
                AddFlyGesture(_mouseGestures.Consume());
                if (mouse.rightButton.isPressed && !_mouseOnUi)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    _flyIn.yawDeg -= d.x * 0.25f;
                    _flyIn.pitchDeg -= d.y * 0.15f;
                    _flyIn.touching = true;
                }
                float sc = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(sc) > 0.01f)
                {
                    var es = EventSystem.current;
                    if (es == null || !es.IsPointerOverGameObject())
                    {
                        _flyIn.zoomFactor = (_flyIn.zoomFactor > 0f ? _flyIn.zoomFactor : 1f) * Mathf.Exp(-sc * 0.0012f);
                        _flyIn.zoomAtPoint = true;
                        _flyIn.zoomPoint = p;
                    }
                }
            }
            var kb = Keyboard.current;
            if (kb != null)
            {
                float z = 0f;
                if (kb.qKey.isPressed || kb.numpadPlusKey.isPressed) z -= 1.2f * dt;
                if (kb.eKey.isPressed || kb.numpadMinusKey.isPressed) z += 1.2f * dt;
                if (z != 0f) _flyIn.zoomFactor = (_flyIn.zoomFactor > 0f ? _flyIn.zoomFactor : 1f) * Mathf.Exp(z);
            }
        }

        // What PangaeaFinale flies the camera with: the keys as a move, plus everything the fingers and the mouse did
        // since the last read.
        public FlyOverCamera.Input ReadFlyInput()
        {
            var input = _flyIn;
            input.move = session != null && session.PangaeaFreeLook ? ReadFlyScreen() : Vector2.zero;
            _flyIn = default;
            return input;
        }

        // ---------------------------------------------------------------- Tilda beside the menus

        void UpdatePresenter(Stage stage)
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
                else if (stage == Stage.GameOver) view.SetPose(GameOverPose, true);
                return;
            }
            switch (stage)
            {
                case Stage.Title: TitleStage(view, entered); break;
                case Stage.Pause: if (entered) PauseStage(view); break;
                case Stage.GameOver: if (entered) GameOverStage(view); break;
            }
        }

        // What she said for a menu ends with it; the game-over line may run on into the new game.
        void LeaveStage(Stage stage)
        {
            if (!Application.isPlaying) return;
            if (stage == Stage.Title) TildaVoice.Hush(TildaVoiceLines.Greeting, "idle_");
            else if (stage == Stage.Pause) TildaVoice.Hush(TildaVoiceLines.Pause, TildaVoiceLines.AdventurePrefix + TildaVoiceLines.Pause);
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
            string line = TildaVoiceLines.ForMode(TildaVoiceLines.Pause, session != null && session.Mode == GameMode.Adventure);
            if (!TildaVoice.Say(line, VoicePriority.IfSilent)) return;
            s_pauseLineSaid = true;
            view.Play(TildaPose.Idle, 0f, TildaVoice.LengthOf(line));
        }

        // She stays sorry for as long as the screen is up; the cheer waits for the new island.
        void GameOverStage(TildaView view)
        {
            view.SetPose(GameOverPose);
            string line = TildaVoiceLines.ForMode(TildaVoiceLines.GameOver, session != null && session.Mode == GameMode.Adventure);
            if (TildaVoice.Say(line, VoicePriority.Interrupt)) view.Play(GameOverPose, 0f, TildaVoice.LengthOf(line));
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

        // Abenteuer scores the distance and waits for "Nochmal" / "Hauptmenü"; the cozy path (no longer reachable in
        // normal play) keeps its stats and the automatic new island. Called every frame of the screen: the texts are
        // only rebuilt when something they show has changed.
        void FillGameOver(SessionStats st, float countdown, GameMode mode, BestDistances.Result result)
        {
            bool adventure = mode == GameMode.Adventure;
            bool modeChanged = mode != _overMode;
            if (modeChanged)
            {
                _overMode = mode;
                if (_overNewIsland != null) _overNewIsland.SetActive(!adventure);
                if (_overAdventureRow != null) _overAdventureRow.SetActive(adventure);
                if (_overSeedText != null) _overSeedText.gameObject.SetActive(!adventure);
                if (_recordText != null) _recordText.gameObject.SetActive(adventure);
            }
            int stamp = OverStamp(st, adventure ? 0 : Mathf.CeilToInt(countdown), result);
            if (!modeChanged && stamp == _overStamp) return;
            _overStamp = stamp;
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

        static int OverStamp(SessionStats st, int countdown, BestDistances.Result result)
        {
            unchecked
            {
                int h = BestDistances.Metres(st.distance);
                h = h * 31 + BestDistances.Metres(result.best);
                h = h * 31 + (result.isRecord ? 1 : 0) + (result.previous > 0f ? 2 : 0);
                h = h * 31 + AdventureRunStats.Flotsam;
                h = h * 31 + AdventureRunStats.Hits;
                h = h * 31 + AdventureRunStats.Dodges;
                h = h * 31 + Mathf.FloorToInt(st.timeSurvived);
                h = h * 31 + st.islandsAbsorbed;
                h = h * 31 + st.volcanoesAbsorbed;
                h = h * 31 + Mathf.RoundToInt(st.peakLandMass);
                return h * 31 + countdown;
            }
        }

        void RefreshTitleBest()
        {
            if (_titleBestText == null) return;
            float best = BestDistances.Get(GameMode.Adventure);
            int shown = BestDistances.Metres(best);
            if (shown == _titleBestShown) return;
            _titleBestShown = shown;
            _titleBestText.text = ModeTexts.BestDistanceCaption(best);
        }

        void SetPauseMode(bool adventure)
        {
            if (_pauseRestartLabel != null) _pauseRestartLabel.text = ModeTexts.RestartLabel(adventure ? GameMode.Adventure : GameMode.Cozy);
            // The ring has no world number worth showing; in its place the note that giving up does not score.
            if (_pauseSeedText != null && _pauseSeedText.gameObject.activeSelf == adventure) _pauseSeedText.gameObject.SetActive(!adventure);
            if (_pauseNoteText != null && _pauseNoteText.gameObject.activeSelf != adventure) _pauseNoteText.gameObject.SetActive(adventure);
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
            // A running cozy world is always lost by starting over, saved yet or not.
            else OpenConfirm(() => session?.StartNewGame(WorldSeeds.RandomOther(session.WorldSeed)));
        }

        // "Gemütlich": with a saved world a small window offers "Weiter" and "Neu beginnen" (choosing the latter is the
        // explicit decision to replace the save, so there is no second question); without one the new world starts.
        public void PressCozy()
        {
            if (session != null) session.SelectMode(GameMode.Cozy);
            bool saved = SaveProbe != null ? SaveProbe() : session != null && session.HasSave;
            if (saved) OpenCozyChoice();
            else StartCozyWorld();
        }

        void StartCozyWorld()
        {
            CloseCozyChoice();
            var seed = FieldSeed();
            if (NewCozyWorldOverride != null) { NewCozyWorldOverride(seed); return; }
            if (session == null) return;
            if (seed.HasValue) session.StartNewGame(seed.Value);
            else session.StartNewGame();
        }

        void ContinueCozyWorld()
        {
            CloseCozyChoice();
            if (ContinueCozyOverride != null) { ContinueCozyOverride(); return; }
            if (session == null) return;
            session.SelectMode(GameMode.Cozy);
            session.ContinueGame();
        }

        public void OpenCozyChoice()
        {
            if (_choice == null) return;
            if (_choiceSavedText != null) _choiceSavedText.text = SavedWorldCaption();
            if (_choiceNewText != null)
            {
                var seed = FieldSeed();
                _choiceNewText.text = seed.HasValue ? ModeTexts.SeedCaption(seed.Value, false) : session != null ? session.SeedLabel : "";
            }
            _choice.transform.SetAsLastSibling();
            if (!_choice.activeSelf) _choice.SetActive(true);
        }

        public void CloseCozyChoice()
        {
            if (_choice != null && _choice.activeSelf) _choice.SetActive(false);
        }

        string SavedWorldCaption()
        {
            var sm = session != null ? session.saveManager : null;
            if (sm == null || !SaveManager.Peek(sm.SavePath, out int seed, out bool legacy)) return "";
            return ModeTexts.SeedCaption(seed, legacy);
        }

        // ---------------------------------------------------------------- "Neue Welt beginnen?"

        public void OpenConfirm(Action onConfirm)
        {
            if (_confirm == null) return;
            _confirmAction = onConfirm;
            _confirm.transform.SetAsLastSibling();
            if (!_confirm.activeSelf) _confirm.SetActive(true);
        }

        public void CloseConfirm()
        {
            _confirmAction = null;
            if (_confirm != null && _confirm.activeSelf) _confirm.SetActive(false);
        }

        void OnConfirmPressed()
        {
            var action = _confirmAction;
            CloseConfirm();
            action?.Invoke();
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
            _fieldSaved = session.WorldIsSavedState;
            _fieldSeed = session.WorldSeed;
            if (session.LegacySeeds && !_fieldSaved) { _seedField.SetTextWithoutNotify(""); return; }
            _seedField.SetTextWithoutNotify(TitleFieldSeed(_fieldSaved, session.WorldSeed, ref _proposedSeed).ToString());
        }

        // "Jede Runde neue Welt". The field names the world a fresh start builds: the world behind the title when it is
        // a fresh one, and a new random number (kept until used) while a saved run sits there for "Weiter".
        public static int TitleFieldSeed(bool savedWorldBehind, int worldSeed, ref int proposed)
        {
            if (!savedWorldBehind) return worldSeed;
            if (proposed <= 0 || proposed == worldSeed) proposed = WorldSeeds.RandomOther(worldSeed);
            return proposed;
        }

        // Title entered from a run (not the app's first title).
        public static bool ReturnedFromRun(GameSession.State from) =>
            from == GameSession.State.Playing || from == GameSession.State.Paused || from == GameSession.State.GameOver || from == GameSession.State.RunComplete;

        // ---------------------------------------------------------------- seed row

        int? FieldSeed()
        {
            if (_seedField == null) return null;
            return WorldSeeds.TryParse(_seedField.text, out int s) ? s : (int?)null;
        }

        void OnSeedEdited(string text)
        {
            if (session == null) return;
            var s = FieldSeed();
            if (!s.HasValue) { RefreshSeedField(true); return; }
            // The offered number is only built on Start, so "Weiter" keeps its loaded world.
            if (session.WorldIsSavedState ? s.Value == _proposedSeed : !session.LegacySeeds && s.Value == session.WorldSeed) { RefreshSeedField(true); return; }
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
        const float OverRowWidth = 760f, OverAgainWidth = 316f, OverRowGap = 16f;
        static readonly Vector2 TitlePanel = new Vector2(920f, 1516f), OverPanel = new Vector2(880f, 1270f);
        static Vector2 PausePanel => new Vector2(880f, Application.isMobilePlatform ? 1300f : 1464f);
        const float TitleExtrasY = -1132f;
        // The cozy choice window hangs just below the seed field, over the mode row it was opened from.
        const float ChoiceTop = 876f;
        static readonly Vector2 ChoicePanel = new Vector2(840f, 420f);
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
            _help.Build(root, null, OnReplayTutorial, OnToggleVoice, ReplayPendingFor);
            _tiltScreen.Build(root, tilt, null);
            _presenter = TildaPresenter.Create(root);
            _confirm = BuildConfirm(root);
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

            // Two modes side by side: the cozy world (seed above) and the adventure run with its record below.
            var modeSize = new Vector2(ModeButtonWidth, UiStyle.ButtonHeight);
            var cozy = UiStyle.PrimaryButton(panel, "Start", GameModes.Label(GameMode.Cozy), modeSize, PressCozy);
            ((RectTransform)cozy.transform).TopCenter(new Vector2(-ModeButtonX, -892f), modeSize);
            var adventure = UiStyle.PrimaryButton(panel, "Adventure", GameModes.Label(GameMode.Adventure), modeSize, () => session?.StartNewGame(GameMode.Adventure));
            ((RectTransform)adventure.transform).TopCenter(new Vector2(ModeButtonX, -892f), modeSize);
            var cozyHint = UiStyle.Label(panel, ModeTexts.CozyHint, UiStyle.Caption, UiStyle.Muted, TextAnchor.UpperCenter);
            cozyHint.rectTransform.TopCenter(new Vector2(-ModeButtonX, -1042f), new Vector2(ModeButtonWidth, 40f));
            _titleBestText = UiStyle.Label(panel, ModeTexts.BestDistanceCaption(0f), UiStyle.Caption, UiStyle.Sand, TextAnchor.UpperCenter, true);
            _titleBestText.rectTransform.TopCenter(new Vector2(ModeButtonX, -1042f), new Vector2(ModeButtonWidth, 40f));
            UiStyle.FitWidth(_titleBestText);
            _titleBestShown = -1;

            const float rowWidth = ModeButtonWidth + 2f * ModeButtonX;
            // Two rows of two: "Steuerung" (tilt, steering scheme) must be reachable before the first game, not only
            // from the pause menu - on the phone the tilt option was otherwise never offered.
            var extras = UiStyle.Rect(panel, "Extras").TopCenter(new Vector2(0f, TitleExtrasY), new Vector2(rowWidth, 256f));
            var half = new Vector2((rowWidth - 16f) / 2f, 120f);
            float halfX = (half.x + 16f) * 0.5f;
            UiStyle.LabelOf(Secondary(extras, "Help", "Anleitung", new Vector2(-halfX, 0f), half, () => OpenHelp())).fontSize = 40;
            UiStyle.LabelOf(Secondary(extras, "Controls", "Steuerung", new Vector2(halfX, 0f), half, OpenTiltSettings)).fontSize = 40;
            UiStyle.LabelOf(Secondary(extras, "Album", "Fotoalbum", new Vector2(-halfX, -136f), half, () => Watch?.OpenAlbum())).fontSize = 40;
            UiStyle.LabelOf(Secondary(extras, "Runs", "Durchgänge", new Vector2(halfX, -136f), half, RunJournal.RequestOpen)).fontSize = 40;
            Line(panel, "Gemütlich wachsen – oder im Abenteuer ausweichen.", UiStyle.Caption, UiStyle.Muted, TitleExtrasY - 300f, 40f);
            // Under the menu panel, so a phone screenshot always tells which build it came from.
            var version = UiStyle.Label(screen, "Version " + Application.version, UiStyle.Caption, UiStyle.Muted, TextAnchor.UpperCenter);
            version.rectTransform.anchorMin = version.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            version.rectTransform.pivot = new Vector2(0.5f, 1f);
            version.rectTransform.sizeDelta = new Vector2(600f, 40f);
            version.rectTransform.anchoredPosition = new Vector2(0f, -TitlePanel.y * 0.5f - 16f);
            _choice = BuildCozyChoice(screen);
            return screen.gameObject;
        }

        // No "Abbrechen": a tap anywhere outside the window (a clear full-screen button behind it) or back closes it.
        GameObject BuildCozyChoice(RectTransform screen)
        {
            var root = UiStyle.Rect(screen, "CozyChoice").Stretch();
            var outside = UiStyle.Shape(root, "Outside", null, new Color(0f, 0f, 0f, 0f), true);
            outside.rectTransform.Stretch();
            var close = outside.gameObject.AddComponent<Button>();
            close.transition = Selectable.Transition.None;
            close.navigation = new Navigation { mode = Navigation.Mode.None };
            close.onClick.AddListener(CloseCozyChoice);

            float top = TitlePanel.y * 0.5f - ChoiceTop;
            var window = UiStyle.Panel(root, "Window", ChoicePanel, true).Center(new Vector2(0f, top - ChoicePanel.y * 0.5f), ChoicePanel);
            // Opaque under the glass: it lies over the title's own buttons, which must not show through.
            var backing = UiStyle.Shape(window, "Backing", UiSprites.RoundedLarge, UiStyle.WithAlpha(UiStyle.GlassDense, 1f));
            backing.rectTransform.Stretch();
            backing.transform.SetSiblingIndex(1);
            var title = UiStyle.Label(window, GameModes.Label(GameMode.Cozy), UiStyle.Heading, UiStyle.Sand, TextAnchor.UpperCenter, true);
            title.rectTransform.TopCenter(new Vector2(0f, -30f), new Vector2(760f, 76f));
            var note = UiStyle.Label(window, ModeTexts.CozyChoiceNote, UiStyle.Caption, UiStyle.Muted, TextAnchor.UpperCenter);
            note.rectTransform.TopCenter(new Vector2(0f, -112f), new Vector2(760f, 40f));
            UiStyle.FitWidth(note);

            var size = new Vector2(372f, UiStyle.ButtonHeight);
            const float x = 196f, buttonY = -176f, captionY = -330f;
            var go = UiStyle.PrimaryButton(window, "Continue", ModeTexts.CozyContinue, size, ContinueCozyWorld);
            ((RectTransform)go.transform).TopCenter(new Vector2(-x, buttonY), size);
            var fresh = Secondary(window, "NewWorld", ModeTexts.CozyNewWorld, new Vector2(x, buttonY), size, StartCozyWorld);
            foreach (var b in new[] { go, fresh })
            {
                var label = UiStyle.LabelOf(b);
                label.fontSize = 46;
                UiStyle.FitWidth(label);
            }
            _choiceSavedText = UiStyle.Label(window, "", UiStyle.Caption, UiStyle.Muted, TextAnchor.UpperCenter);
            _choiceSavedText.rectTransform.TopCenter(new Vector2(-x, captionY), new Vector2(size.x, 40f));
            _choiceNewText = UiStyle.Label(window, "", UiStyle.Caption, UiStyle.Muted, TextAnchor.UpperCenter);
            _choiceNewText.rectTransform.TopCenter(new Vector2(x, captionY), new Vector2(size.x, 40f));
            UiStyle.FitWidth(_choiceSavedText);
            UiStyle.FitWidth(_choiceNewText);

            UiStyle.FadeIn(root.gameObject, window);
            root.gameObject.SetActive(false);
            return root.gameObject;
        }

        GameObject BuildPause(RectTransform root)
        {
            bool quit = !Application.isMobilePlatform;
            var screen = Screen(root, "PauseScreen", UiStyle.Dim, PausePanel, out var panel);
            Line(panel, "Pause", UiStyle.Title, UiStyle.Sand, -36f, 130f, true);
            _pauseSeedText = Line(panel, "", UiStyle.Caption, UiStyle.Muted, -172f, 40f);
            _pauseNoteText = Line(panel, ModeTexts.AdventurePauseNote, UiStyle.Caption, UiStyle.Sand, -172f, 40f);
            UiStyle.FitWidth(_pauseNoteText);
            _pauseNoteText.gameObject.SetActive(false);
            Primary(panel, "Resume", "Fortsetzen", -244f, () => session?.Resume());
            var wide = new Vector2(ButtonWidth, UiStyle.ButtonHeight);
            _pauseRestartLabel = UiStyle.LabelOf(Secondary(panel, "Restart", ModeTexts.RestartLabel(GameMode.Cozy), new Vector2(0f, -408f), wide, OnRestartPressed));
            UiStyle.ButtonIcon(Secondary(panel, "Title", MainMenuLabel, new Vector2(0f, -572f), wide, () => session?.ReturnToTitle()), UiIcon.Home);
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

        static readonly Vector2 ConfirmPanel = new Vector2(820f, 520f);

        GameObject BuildConfirm(RectTransform root)
        {
            var screen = Screen(root, "ConfirmNewWorld", UiStyle.Dim, ConfirmPanel, out var panel);
            var title = UiStyle.Label(panel, ModeTexts.NewWorldTitle, UiStyle.Heading, UiStyle.Sand, TextAnchor.UpperCenter, true);
            title.rectTransform.TopCenter(new Vector2(0f, -56f), new Vector2(740f, 80f));
            UiStyle.FitWidth(title);
            var body = UiStyle.Label(panel, ModeTexts.NewWorldBody, UiStyle.Body, UiStyle.CreamSoft, TextAnchor.UpperCenter);
            body.rectTransform.TopCenter(new Vector2(0f, -164f), new Vector2(740f, 100f));
            var half = new Vector2(340f, UiStyle.ButtonHeight);
            Secondary(panel, "Cancel", ModeTexts.NewWorldCancel, new Vector2(-180f, -312f), half, CloseConfirm);
            Secondary(panel, "Confirm", ModeTexts.NewWorldConfirm, new Vector2(180f, -312f), half, OnConfirmPressed, true);
            screen.gameObject.SetActive(false);
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

            // As wide as the stats card: "Hauptmenü" behind its house icon needs more room than "Nochmal".
            var row = UiStyle.Rect(panel, "AdventureButtons").TopCenter(new Vector2(0f, -1080f), new Vector2(OverRowWidth, UiStyle.ButtonHeight));
            _overAdventureRow = row.gameObject;
            var againSize = new Vector2(OverAgainWidth, UiStyle.ButtonHeight);
            var menuSize = new Vector2(OverRowWidth - OverAgainWidth - OverRowGap, UiStyle.ButtonHeight);
            var again = UiStyle.PrimaryButton(row, "Again", "Nochmal", againSize, () => session?.StartNewGame(GameMode.Adventure));
            ((RectTransform)again.transform).TopCenter(new Vector2((againSize.x - OverRowWidth) * 0.5f, 0f), againSize);
            UiStyle.ButtonIcon(Secondary(row, "Title", MainMenuLabel, new Vector2((OverRowWidth - menuSize.x) * 0.5f, 0f), menuSize, () => session?.ReturnToTitle()), UiIcon.Home);
            _recordText.gameObject.SetActive(false);
            _overAdventureRow.SetActive(false);
            _overMode = GameMode.Cozy;
            _overStamp = int.MinValue;
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
