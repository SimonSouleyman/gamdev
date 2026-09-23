using System;
using Drift.Core;
using Drift.Islands;
using Drift.Tectonics;
using UnityEngine;

namespace Drift.SaveSystem
{
    [Serializable]
    public sealed class SessionStats
    {
        public float timeSurvived;
        public int islandsAbsorbed;
        public int volcanoesAbsorbed;
        public float peakLandMass;

        public void Reset()
        {
            timeSurvived = 0f;
            islandsAbsorbed = 0;
            volcanoesAbsorbed = 0;
            peakLandMass = 0f;
        }

        public void CopyFrom(SessionStats other)
        {
            if (other == null) { Reset(); return; }
            timeSurvived = Mathf.Max(0f, other.timeSurvived);
            islandsAbsorbed = Mathf.Max(0, other.islandsAbsorbed);
            volcanoesAbsorbed = Mathf.Max(0, other.volcanoesAbsorbed);
            peakLandMass = Mathf.Max(0f, other.peakLandMass);
        }
    }

    public sealed class SessionModel
    {
        public float restartDelay = 4f;

        bool _restartDue;
        bool _pangaea;

        public GameSession.State Current { get; private set; } = GameSession.State.Title;
        public SessionStats Stats { get; } = new SessionStats();
        public float RestartCountdown { get; private set; }

        public event Action<GameSession.State, GameSession.State> StateChanged;
        public event Action<bool> PangaeaReachedChanged;

        public bool IsPlaying => Current == GameSession.State.Playing;
        public bool InGame => Current == GameSession.State.Playing || Current == GameSession.State.Paused;

        // The cozy Pangäa is complete but the run is NOT over: the world stays in Playing so the owner can look
        // around for as long as they like, and only "Weiter" (CompleteRun) ends it. Leaving the game in any
        // direction - title, game over, the finale itself - drops it; a pause keeps it.
        public bool PangaeaReached
        {
            get => _pangaea;
            set
            {
                if (_pangaea == value || (value && !InGame)) return;
                _pangaea = value;
                PangaeaReachedChanged?.Invoke(value);
            }
        }

        public bool PangaeaFreeLook => _pangaea && IsPlaying;

        // continueFrom carries a loaded run's stats into the new Playing state; null starts them at zero.
        public bool BeginPlaying(SessionStats continueFrom = null)
        {
            Stats.CopyFrom(continueFrom);
            RestartCountdown = 0f;
            _restartDue = false;
            return Transition(GameSession.State.Playing);
        }

        public bool Pause() => IsPlaying && Transition(GameSession.State.Paused);

        // Going to the background only ever pauses a running game; a paused, title or game-over session stays as it is.
        public bool EnterBackground() => Pause();

        public bool Resume() => Current == GameSession.State.Paused && Transition(GameSession.State.Playing);

        // A cozy run ends when everything is merged into one Pangäa (GameSession.CompleteRun).
        public bool CompleteRun() => InGame && Transition(GameSession.State.RunComplete);

        public bool Sink()
        {
            if (!InGame) return false;
            RestartCountdown = restartDelay;
            _restartDue = false;
            return Transition(GameSession.State.GameOver);
        }

        public bool ReturnToTitle()
        {
            RestartCountdown = 0f;
            _restartDue = false;
            return Transition(GameSession.State.Title);
        }

        public bool Step(float dt)
        {
            switch (Current)
            {
                case GameSession.State.Playing:
                    Stats.timeSurvived += Mathf.Max(0f, dt);
                    return false;
                case GameSession.State.GameOver:
                    if (_restartDue) return false;
                    RestartCountdown = Mathf.Max(0f, RestartCountdown - Mathf.Max(0f, dt));
                    if (RestartCountdown > 0f) return false;
                    _restartDue = true;
                    return true;
                default:
                    return false;
            }
        }

        public void RecordMerge(bool volcano)
        {
            if (!IsPlaying) return;
            Stats.islandsAbsorbed++;
            if (volcano) Stats.volcanoesAbsorbed++;
        }

        public void RecordLandMass(float area)
        {
            if (!IsPlaying) return;
            if (area > Stats.peakLandMass) Stats.peakLandMass = area;
        }

        bool Transition(GameSession.State next)
        {
            if (next == Current) return false;
            if (next != GameSession.State.Playing && next != GameSession.State.Paused) PangaeaReached = false;
            var prev = Current;
            Current = next;
            StateChanged?.Invoke(prev, next);
            return true;
        }
    }

    public class GameSession : MonoBehaviour
    {
        public enum State { Title, Playing, Paused, GameOver, RunComplete }

        public Island player;
        public WorldStreamer streamer;
        public SaveManager saveManager;
        public IslandChaseCamera chaseCamera;
        public float restartDelay = 4f;
        public float autosaveInterval = 20f;
        public bool titleAfterGameOver;
        public bool randomSeedOnFirstTitle = true;

        readonly SessionModel _model = new SessionModel();
        bool _subscribed;
        int _worldSeed;
        bool _legacySeeds = true;
        bool _seedInitialized;
        bool _worldIsSavedState;
        bool _sinkingSuspended;
        bool _photoSinkHold;
        bool _followSinkHold;
        bool _photoInputHold;
        bool _followInputHold;

        public SessionModel Model => _model;
        public State Current => _model.Current;
        public SessionStats Stats => _model.Stats;
        public event Action<State> StateChanged;
        public event Action WorldSeedChanged;
        // Fired once when a cozy run reaches its Pangäa, after the state became RunComplete and the save was cleared.
        public event Action RunCompleted;
        // Raised when the free look after the last merge begins (true) and when it ends (false).
        public event Action<bool> PangaeaReachedChanged;

        // Which game runs (GameModes.Current); each mode keeps its own save file.
        public GameMode Mode => GameModes.Current;
        public bool IsRunComplete => Current == State.RunComplete;

        // The last island is merged but the run is NOT over yet: the world stays in Playing so the owner can drive
        // around, watch the animals and take photos for as long as they like. PangaeaFinale sets this when it has
        // confirmed the Pangäa and shows its "Weiter" banner; only that button calls CompleteRun. While it is set
        // nothing may end the run, so it holds sinking like the tutorial and the watch tools do (the state machine
        // in SessionModel drops it as soon as the game is left in any direction).
        // It is deliberately not saved: a continued run confirms its finished world again and the banner returns.
        public bool PangaeaReached
        {
            get => _model.PangaeaReached;
            set => _model.PangaeaReached = value;
        }

        // The free look itself: the Pangäa is complete and the player is still driving around in it.
        public bool PangaeaFreeLook => _model.PangaeaFreeLook;

        public bool IsGameOver => Current == State.GameOver;
        public bool IsPaused => Current == State.Paused;
        public bool InputLocked => Current != State.Playing;
        public bool SavingAllowed => _model.InGame;
        public float RestartCountdown => _model.RestartCountdown;
        public bool HasSave => saveManager != null && saveManager.HasValidSave;

        // The master seed every per-world random stream is derived from (WorldSeeds). LegacySeeds means the
        // scene's own per-system seeds are in effect: before any new game and after loading a pre-v3 save.
        public int WorldSeed => _worldSeed;
        public bool LegacySeeds => _legacySeeds;
        public string SeedLabel => _legacySeeds ? "Welt: Standard" : $"Welt #{_worldSeed}";
        // True while the world currently holds the state of the save file, so "Weiter" needs no second load.
        public bool WorldIsSavedState => _worldIsSavedState;

        // The tutorial's only grip on the simulation: while set, the player island does not sink. Owned by
        // TutorialGuide, which clears it on skip, finish and disable.
        public bool SinkingSuspended
        {
            get => _sinkingSuspended;
            set
            {
                if (_sinkingSuspended == value) return;
                _sinkingSuspended = value;
                ApplySinking();
            }
        }

        // Photo mode's own hold on sinking (owned by WatchTools). A separate flag, so neither the tutorial nor
        // photo mode can release the other's hold.
        public bool PhotoSinkHold
        {
            get => _photoSinkHold;
            set
            {
                if (_photoSinkHold == value) return;
                _photoSinkHold = value;
                ApplySinking();
            }
        }

        // Following a herd holds sinking the same way (owned by WatchTools, independent of the other two).
        public bool FollowSinkHold
        {
            get => _followSinkHold;
            set
            {
                if (_followSinkHold == value) return;
                _followSinkHold = value;
                ApplySinking();
            }
        }

        // Island.InputLocked is a single static bool that the session rewrites on every state change, so the
        // watch tools do not write it themselves: they hold input here (photo mode and herd following, one flag
        // each) and the session composes the holds with its own state.
        public bool PhotoInputHold
        {
            get => _photoInputHold;
            set
            {
                if (_photoInputHold == value) return;
                _photoInputHold = value;
                ApplyInputLock();
            }
        }

        public bool FollowInputHold
        {
            get => _followInputHold;
            set
            {
                if (_followInputHold == value) return;
                _followInputHold = value;
                ApplyInputLock();
            }
        }

        public bool WatchInputHold => _photoInputHold || _followInputHold;

        public static bool SinkAllowed(bool playing, bool tutorialHold, bool photoHold) => SinkAllowed(playing, tutorialHold, photoHold, false);

        public static bool SinkAllowed(bool playing, bool tutorialHold, bool photoHold, bool followHold) => SinkAllowed(playing, tutorialHold, photoHold, followHold, false);

        public static bool SinkAllowed(bool playing, bool tutorialHold, bool photoHold, bool followHold, bool pangaeaHold) =>
            playing && !tutorialHold && !photoHold && !followHold && !pangaeaHold;

        public static bool IslandInputLocked(bool playing, bool photoHold, bool followHold) => IslandInputLocked(playing, photoHold, followHold, false);

        // The free look after the last merge steers nothing but the camera: the Pangäa itself stops taking input
        // (PangaeaFinale also pins it, so no current carries it away) and the player flies over their island.
        public static bool IslandInputLocked(bool playing, bool photoHold, bool followHold, bool pangaeaFreeLook) =>
            !playing || photoHold || followHold || pangaeaFreeLook;

        void ApplySinking()
        {
            if (Application.isPlaying && player != null) player.sinkEnabled = SinkAllowed(_model.IsPlaying, _sinkingSuspended, _photoSinkHold, _followSinkHold, _model.PangaeaReached);
        }

        void ApplyInputLock()
        {
            if (Application.isPlaying) SetIslandInputLocked(IslandInputLocked(_model.IsPlaying, _photoInputHold, _followInputHold, _model.PangaeaFreeLook));
        }

        static void SetIslandInputLocked(bool locked) => Island.InputLocked = locked;

        void OnEnable()
        {
            // Enter Play Mode skips scene reload, so edit-time values of these flags would otherwise carry into Play.
            _seedInitialized = false;
            _worldIsSavedState = false;
            _sinkingSuspended = false;
            _photoSinkHold = false;
            _followSinkHold = false;
            _photoInputHold = false;
            _followInputHold = false;
            _model.PangaeaReached = false;
            if (saveManager == null) saveManager = FindAnyObjectByType<SaveManager>();
            if (saveManager != null) saveManager.fileName = GameModes.SaveFile(GameModes.Current);
            _model.restartDelay = restartDelay;
            _model.StateChanged += OnModelStateChanged;
            _model.PangaeaReachedChanged += OnPangaeaReachedChanged;
            Island.Merged += OnMerged;
            Resolve();
            Subscribe();
        }

        void OnDisable()
        {
            _model.StateChanged -= OnModelStateChanged;
            _model.PangaeaReachedChanged -= OnPangaeaReachedChanged;
            Island.Merged -= OnMerged;
            Unsubscribe();
            if (Application.isPlaying)
            {
                Time.timeScale = 1f;
                SetIslandInputLocked(false);
                // Leaving Play Mode (no domain reload) must not leave the Editor in the ring world: the scene would
                // then be edited and saved with the streamer off and the adventure camera.
                GameModes.Set(GameMode.Cozy);
            }
        }

        void Start()
        {
            if (!Application.isPlaying) return;
            ApplyState(_model.Current);
        }

        void Resolve()
        {
            if (player == null)
                foreach (var i in Island.All) if (i != null && i.useKeyboardInput) { player = i; break; }
            if (streamer == null) streamer = FindAnyObjectByType<WorldStreamer>();
            if (saveManager == null) saveManager = FindAnyObjectByType<SaveManager>();
            if (chaseCamera == null) chaseCamera = FindAnyObjectByType<IslandChaseCamera>();
        }

        void Subscribe()
        {
            if (_subscribed || player == null) return;
            player.Sunk += OnSunk;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (_subscribed && player != null) player.Sunk -= OnSunk;
            _subscribed = false;
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            if (player == null)
            {
                Resolve();
                Subscribe();
                if (player != null) ApplyState(_model.Current);
                return;
            }
            if (!_seedInitialized) InitializeTitleWorld();
            _model.restartDelay = restartDelay;
            float dt = _model.IsPlaying ? Time.deltaTime : Time.unscaledDeltaTime;
            if (_model.IsPlaying && Mode == GameMode.Adventure && player != null && !player.sinkEnabled) dt = 0f;
            // An adventure run ends on its "Versunken" screen (time, best time, "Nochmal"); only cozy restarts by itself.
            if (_model.Step(dt) && Mode != GameMode.Adventure) RestartFromGameOver();
            if (_model.IsPlaying) _model.RecordLandMass(player.LandArea);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) OnBackgrounded();
        }

        // Devices report backgrounding through OnApplicationPause; in the Editor and desktop players only
        // the focus callback fires, so losing window focus counts as backgrounding there. Nothing auto-resumes.
        void OnApplicationFocus(bool focused)
        {
            if (focused) return;
            if (Application.isEditor || !Application.isMobilePlatform) OnBackgrounded();
        }

        void OnBackgrounded()
        {
            if (!Application.isPlaying || !_model.IsPlaying) return;
            if (saveManager != null && Mode == GameMode.Cozy) saveManager.Save();
            _model.EnterBackground();
        }

        void OnSunk()
        {
            if (!_model.Sink()) return;
            if (saveManager != null) saveManager.DeleteSave();
            _worldIsSavedState = false;
        }

        // The title shows the world it would continue: a readable save is loaded behind the title once,
        // otherwise the fresh world gets a random seed so the preview matches what Start will build.
        void InitializeTitleWorld()
        {
            _seedInitialized = true;
            if (_model.Current != State.Title) return;
            if (HasSave && saveManager.Load())
            {
                _worldIsSavedState = true;
                SnapCamera();
                return;
            }
            if (randomSeedOnFirstTitle) PreviewSeed(WorldSeeds.Random());
        }

        void OnMerged(Island host, Island guest, float energy)
        {
            if (host == null || host != player) return;
            _model.RecordMerge(guest != null && guest.isVolcano);
        }

        // The free look holds sinking and takes the steering off the island; SessionModel drops the flag itself
        // whenever the game is left.
        void OnPangaeaReachedChanged(bool on)
        {
            ApplySinking();
            ApplyInputLock();
            PangaeaReachedChanged?.Invoke(on);
        }

        void OnModelStateChanged(State prev, State next)
        {
            ApplyState(next);
            StateChanged?.Invoke(next);
        }

        void ApplyState(State s)
        {
            if (!Application.isPlaying) return;
            bool playing = s == State.Playing;
            bool inGame = playing || s == State.Paused;
            if (player != null) player.sinkEnabled = SinkAllowed(playing, _sinkingSuspended, _photoSinkHold, _followSinkHold, _model.PangaeaReached);
            SetIslandInputLocked(IslandInputLocked(playing, _photoInputHold, _followInputHold, _model.PangaeaFreeLook));
            Time.timeScale = s == State.Paused ? 0f : 1f;
            if (saveManager != null) saveManager.autosaveInterval = inGame && Mode == GameMode.Cozy ? autosaveInterval : 0f;
        }

        // Switches the mode the title works with (which save "Weiter" continues). Only on the title.
        public void SelectMode(GameMode mode)
        {
            if (_model.Current != State.Title || mode == GameModes.Current) return;
            GameModes.Set(mode);
            if (saveManager != null) saveManager.fileName = GameModes.SaveFile(mode);
            _worldIsSavedState = false;
        }

        public void StartNewGame(GameMode mode)
        {
            GameModes.Set(mode);
            if (saveManager != null) saveManager.fileName = GameModes.SaveFile(mode);
            StartNewGame(WorldSeeds.Random(), false);
        }

        // Ends the free look and starts the finale: the run is over for good (its save is removed), the world stays
        // as it is for the flight into space. Called by the banner's "Weiter", never by the merge itself.
        // A new run starts with StartNewGame.
        public void CompleteRun()
        {
            if (!_model.CompleteRun()) return;
            // The save (and with it the run's journal layer) is deleted only when the finale is left: the finale
            // still reads the run's species and stats for the run journal. RunComplete never saves.
            _worldIsSavedState = false;
            RunCompleted?.Invoke();
        }

        public void StartNewGame() => StartNewGame(_worldSeed, _legacySeeds);

        public void StartNewGame(int seed) => StartNewGame(seed, false);

        void StartNewGame(int seed, bool legacy)
        {
            if (saveManager != null) saveManager.DeleteSave();
            ApplyWorldSeed(seed, legacy);
            Restart();
            _model.BeginPlaying();
        }

        // Rebuilds the fresh world of a seed without leaving the title (seed field / "Zufall"). A loaded
        // save is discarded from the world but the file stays, so "Weiter" simply loads it again.
        public void PreviewSeed(int seed)
        {
            if (_model.Current != State.Title) return;
            ApplyWorldSeed(seed, false);
            Restart();
        }

        // Called by SaveManager.Load before it restores anything, so streamed content is planned with the saved seed.
        public void ApplyWorldSeed(int seed, bool legacy)
        {
            seed = WorldSeeds.Clamp(seed);
            bool changed = seed != _worldSeed || legacy != _legacySeeds;
            _worldSeed = seed;
            _legacySeeds = legacy;
            _seedInitialized = true;
            if (!legacy)
            {
                Resolve();
                WorldSeeds.Apply(seed, player, streamer);
            }
            if (changed) WorldSeedChanged?.Invoke();
        }

        public bool ContinueGame()
        {
            if (saveManager != null && HasSave && (_worldIsSavedState || saveManager.Load()))
            {
                _worldIsSavedState = false;
                SnapCamera();
                _model.BeginPlaying(saveManager.LoadedStats);
                return true;
            }
            StartNewGame();
            return false;
        }

        // IslandChaseCamera only eases towards its target; after a teleport (load / restart) the pose is set directly.
        public void SnapCamera()
        {
            Resolve();
            var cam = chaseCamera;
            if (cam == null) return;
            if (cam.target == null) cam.target = player;
            cam.SnapToTarget();
        }

        public void Pause() => _model.Pause();

        public void Resume() => _model.Resume();

        public void TogglePause()
        {
            if (_model.IsPlaying) _model.Pause();
            else if (_model.Current == State.Paused) _model.Resume();
        }

        public void RestartFromGameOver()
        {
            Restart();
            if (titleAfterGameOver) _model.ReturnToTitle();
            else _model.BeginPlaying();
        }

        public void ReturnToTitle()
        {
            if (IsRunComplete && saveManager != null) saveManager.DeleteSave();
            // Adventure runs are short and never continued: nothing to save.
            if (_model.InGame && saveManager != null && Mode == GameMode.Cozy) _worldIsSavedState = saveManager.Save();
            _model.ReturnToTitle();
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void Restart()
        {
            _worldIsSavedState = false;
            PangaeaReached = false;
            var plates = PlateSystem.Instance;
            if (plates != null) plates.Restore(new PlateSaveData());
            var storms = StormSystem.Instance;
            if (storms != null) storms.ClearStorms();
            if (player != null) player.ResetToStart();
            if (streamer != null) streamer.ResetWorld();
            var volcanoes = FindAnyObjectByType<VolcanoSpawner>();
            if (volcanoes != null) volcanoes.Clear();
            SnapCamera();
        }
    }
}
